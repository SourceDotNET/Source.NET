using CommunityToolkit.HighPerformance;

using Game.Shared;

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
	public InlineArrayMapKeyMaxLength<char> Name;
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
}
