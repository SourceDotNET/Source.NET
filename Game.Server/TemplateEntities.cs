using CommunityToolkit.HighPerformance;

using Game.Shared;

using Source.Common.Commands;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Game.Server;

public class TemplateEntityData
{
	public string Name;
	public Memory<byte> pszMapData; // ????
	public Memory<byte> iszMapData; // ????
	public int MapDataLength;
	public bool NeedsEntityIOFixup;   // If true, this template has entity I/O in its mapdata that needs fixup before spawning.
	public Memory<byte> FixedMapData;      // A single copy of this template that we used to fix up the Entity I/O whenever someone wants a fixed version of this template
}

public struct GroupTemplate
{
	public EntityMapData MapDataParser;
	public InlineArrayMapKeyMaxLength<byte> Name;
	public int Index;
	public bool ChangeTargetname;
}

public static class Templates {
	public static readonly List<TemplateEntityData> g_Templates = [];
	public static int g_iCurrentTemplateInstance;
	const string ENTITYIO_FIXUP_STRING = "&0000";

	public static nint Add(BaseEntity entity, ReadOnlySpan<byte> mapData, int len){
		string name = entity.GetEntityName();
		if (name.Length == 0) {
			DevWarning(1, $"RegisterTemplateEntity: template entity with no name, class {entity.GetClassname()}\n");
			return -1;
		}

		TemplateEntityData pEntData = new TemplateEntityData();
		pEntData.Name = name;

		// We may modify the values of the keys in this mapdata chunk later on to fix Entity I/O
		// connections. For this reason, we need to ensure we have enough memory to do that.
		int iKeys = MapEntity.GetNumKeysInEntity(mapData);
		nint extraSpace = ENTITYIO_FIXUP_STRING.Length * iKeys;

		// Extra 1 because the mapdata passed in isn't null terminated
		pEntData.MapDataLength = (int)(len + extraSpace + 1);
		pEntData.pszMapData = new byte[pEntData.MapDataLength];
		memcpy<byte>(pEntData.pszMapData.Span, mapData[..(len + 1)]);
		pEntData.pszMapData.Span[len] = 0;

		// We don't alloc these suckers right now because that gives us no time to
		// tweak them for Entity I/O purposes.
		pEntData.iszMapData = default;
		pEntData.NeedsEntityIOFixup = false;
		pEntData.FixedMapData = default;

		g_Templates.Add(pEntData);
		return g_Templates.Count - 1;
	}

	public static bool IndexRequiresEntityIOFixup(int index){
		Assert(index < g_Templates.Count);
		return g_Templates[index].NeedsEntityIOFixup;
	}

	// Looks up a template entity by its index in the templates.
	// Used by point_templates because they often have multiple templates with the same name.
	public static Memory<byte> FindByIndex(int index){
		Assert(index < g_Templates.Count);

		// First time through we alloc the mapdata copy.
		// It's safe to do it now because this isn't called until post Entity I/O cleanup.
		if (g_Templates[index].iszMapData.IsEmpty)
			g_Templates[index].iszMapData = g_Templates[index].pszMapData.ToArray();

		return g_Templates[index].iszMapData;
	}

	public static Memory<byte> FindByTargetName(ReadOnlySpan<char> name) {
		int count = g_Templates.Count;
		for (int i = 0; i < count; i++) {
			TemplateEntityData template = g_Templates[i];
			if (name.Equals(template.Name, StringComparison.OrdinalIgnoreCase))
				return FindByIndex(i);
		}

		return default;
	}

	public static int GetStringSize(int index) {
		Assert(index < g_Templates.Count);
		return g_Templates[index].MapDataLength;
	}

	static readonly ConVar template_debug = new("template_debug", "0");

	const byte VMF_IOPARAM_STRING_DELIMITER = 0x1b;

	static string AsciiString(ReadOnlySpan<byte> bytes) => Encoding.ASCII.GetString(bytes[..MapEntity.StrLen(bytes)]);

	public static void ReconnectIOForGroup(PointTemplate group) {
		int count = group.GetNumTemplates();
		if (count == 0)
			return;

		GroupTemplate[] groupTemplates = new GroupTemplate[count];
		int i;
		for (i = 0; i < count; i++) {
			ref GroupTemplate newGroupTemplate = ref groupTemplates[i];
			newGroupTemplate.Index = group.GetTemplateIndexForTemplate(i);
			TemplateEntityData data = g_Templates[newGroupTemplate.Index];
			newGroupTemplate.MapDataParser = new EntityMapData(data.pszMapData, data.MapDataLength);
			newGroupTemplate.MapDataParser.ExtractValue("targetname"u8, newGroupTemplate.Name);
			newGroupTemplate.ChangeTargetname = false;
		}

		if (group.AllowNameFixup()) {
			Span<byte> keyName = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];
			Span<byte> value = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];
			Span<byte> newValue = stackalloc byte[EntityMapData.MAPKEY_MAXLENGTH];
			ReadOnlySpan<byte> fixup = Encoding.ASCII.GetBytes(ENTITYIO_FIXUP_STRING);

			for (i = 0; i < count; i++) {
				Dictionary<string, int> keyInstanceCount = new(StringComparer.OrdinalIgnoreCase);
				EntityMapData mapData = groupTemplates[i].MapDataParser;

				if (!mapData.GetFirstKey(keyName, value))
					continue;

				do {
					ReadOnlySpan<byte> key = keyName[..MapEntity.StrLen(keyName)];
					if (stricmp(key, "targetname"u8) == 0)
						continue;

					string keyString = Encoding.ASCII.GetString(key);
					keyInstanceCount.TryGetValue(keyString, out int keyCount);
					keyCount++;
					keyInstanceCount[keyString] = keyCount;

					ReadOnlySpan<byte> fullValue = value[..MapEntity.StrLen(value)];
					byte delimiter = VMF_IOPARAM_STRING_DELIMITER;
					if (fullValue.IndexOf(delimiter) < 0)
						delimiter = (byte)',';

					int s = fullValue.IndexOf(delimiter);
					ReadOnlySpan<byte> sValue = s >= 0 ? fullValue[..s] : fullValue;

					for (int iTName = 0; iTName < count; iTName++) {
						ReadOnlySpan<byte> name = groupTemplates[iTName].Name;
						if (stricmp(name, sValue) != 0)
							continue;

						if (template_debug.GetInt() != 0)
							Msg($"Template Connection Found: Key {keyString} (\"{Encoding.ASCII.GetString(sValue)}\") in entity named \"{AsciiString(groupTemplates[i].Name)}\"({i}) matches entity {iTName}'s targetname\n");

						int keyInstance = keyCount - 1;

						newValue.Clear();
						int length = Math.Min(sValue.Length, newValue.Length - 1);
						sValue[..length].CopyTo(newValue);
						int appendLength = Math.Min(fixup.Length, newValue.Length - 1 - length);
						fixup[..appendLength].CopyTo(newValue[length..]);
						length += appendLength;
						if (s >= 0) {
							appendLength = Math.Min(fullValue.Length - s, newValue.Length - 1 - length);
							fullValue.Slice(s, appendLength).CopyTo(newValue[length..]);
						}
						mapData.SetValue(key, newValue, keyInstance);

						groupTemplates[iTName].ChangeTargetname = true;

						g_Templates[groupTemplates[i].Index].NeedsEntityIOFixup = true;
						g_Templates[groupTemplates[iTName].Index].NeedsEntityIOFixup = true;
					}
				}
				while (mapData.GetNextKey(keyName, value));
			}

			for (i = 0; i < count; i++) {
				if (groupTemplates[i].ChangeTargetname) {
					EntityMapData mapData = groupTemplates[i].MapDataParser;
					value.Clear();
					mapData.ExtractValue("targetname"u8, value);
					int length = MapEntity.StrLen(value);
					int appendLength = Math.Min(fixup.Length, value.Length - 1 - length);
					fixup[..appendLength].CopyTo(value[length..]);
					mapData.SetValue("targetname"u8, value);
				}
			}
		}
	}

	public static void StartUniqueInstance() {
		g_iCurrentTemplateInstance++;

		int max = (int)Math.Pow(10.0f, ENTITYIO_FIXUP_STRING.Length - 1);
		if (g_iCurrentTemplateInstance >= max) {
			Assert(false);
			g_iCurrentTemplateInstance = 0;
		}
	}

	public static Memory<byte> GetEntityIOFixedMapData(int index) {
		Assert(IndexRequiresEntityIOFixup(index));

		TemplateEntityData template = g_Templates[index];
		if (template.FixedMapData.IsEmpty) {
			template.FixedMapData = new byte[template.MapDataLength];
			Span<byte> source = template.pszMapData.Span;
			int length = Math.Min(MapEntity.StrLen(source), template.MapDataLength - 1);
			source[..length].CopyTo(template.FixedMapData.Span);
		}

		int fixupSize = ENTITYIO_FIXUP_STRING.Length;
		ReadOnlySpan<byte> ourFixup = Encoding.ASCII.GetBytes($"{ENTITYIO_FIXUP_STRING[0]}{g_iCurrentTemplateInstance:D4}");

		Span<byte> data = template.FixedMapData.Span;
		int c = 0;
		do {
			if (data[c] == ENTITYIO_FIXUP_STRING[0]) {
				bool valid = true;
				for (int i = 1; i < fixupSize; i++) {
					if (c + i >= data.Length || !(data[c + i] >= '0' && data[c + i] <= '9')) {
						valid = false;
						break;
					}
				}

				if (valid) {
					ourFixup[..fixupSize].CopyTo(data[c..]);
					c += fixupSize;
				}
			}
			c++;
		} while (c < data.Length && data[c] != 0);

		return template.FixedMapData;
	}

	public static void RemoveAll() => g_Templates.Clear();

	public class TemplatesHook() : AutoGameSystem("CTemplatesHook")
	{
		public override void LevelShutdownPostEntity() => RemoveAll();
	}

	public static readonly TemplatesHook g_TemplateEntityHook = new();
}
