global using static Game.Client.ClassMap;

using Game.Shared;

using Source;

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Game.Client;

class ClassEntry
{
	public ClassEntry() {

	}

	public ReadOnlySpan<char> GetMapName() => ((Span<char>)MapName).SliceNullTerminatedString();
	public void SetMapName(ReadOnlySpan<char> newname) => strcpy(MapName, newname);

	public DispatchFunction Factory = null!;
	private InlineArray40<char> MapName;
	public nint Size;
}

public class ClassMap : IClassMap
{
	public static readonly ClassMap g_ClassMap = new();
	public static ClassMap GetClassMap() => g_ClassMap;
	static ClassMap() {
		// Here is where we start initializing things based on LinkEntityToClassAttribute's
		var typePairs = Assembly.GetExecutingAssembly().GetTypesWithAttributeMulti<LinkEntityToClassAttribute>();
		foreach (var type in typePairs) {
			var attr = type.Value;

			DynamicMethod m = new DynamicMethod("LinkEntity_Instantiate" + type.Key.Name + "_ToMap_" + attr.LocalName, typeof(C_BaseEntity), []);
			ILGenerator il = m.GetILGenerator();

			il.Emit(OpCodes.Newobj, type.Key.GetConstructor([]) ?? throw new Exception("no constructor for auto factory"));
			il.Emit(OpCodes.Castclass, typeof(C_BaseEntity));
			il.Emit(OpCodes.Ret);

			DispatchFunction dispatch = m.CreateDelegate<DispatchFunction>();

			GetClassMap().Add(type.Key, attr.LocalName, type.Key.Name, dispatch);
		}
	}


	readonly Dictionary<ulong, ClassEntry> ClassDict = [];

	public void Add(Type type, ReadOnlySpan<char> mapname, ReadOnlySpan<char> classname, DispatchFunction? factory = null) {
		ReadOnlySpan<char> map = Lookup(classname);
		if (!map.IsEmpty && map.Equals(mapname, StringComparison.Ordinal))
			return;

		if (!map.IsEmpty)
			ClassDict.Remove(classname.Hash(false));

		ClassEntry element = new();
		element.SetMapName(mapname);
		element.Factory = factory!;
		element.Size = 0; // This isn't really relevant at the moment. Implementing it is also easier said than done in .NET-land
		ClassDict[classname.Hash(false)] = element;
	}

	public C_BaseEntity? CreateEntity(ReadOnlySpan<char> mapname) {
#if GMOD_DLL
		if (GarrysMod.GarrysMod.g_LuaManager != null) {
			Span<char> lowered = stackalloc char[512];
			strcpy(lowered, mapname);
			strlower(lowered);
			C_BaseEntity? luaEnt = GarrysMod.GarrysMod.g_LuaManager.CreateEntity(((ReadOnlySpan<char>)lowered).SliceNullTerminatedString());
			if (luaEnt != null)
				return luaEnt;
		}
#endif
		foreach (var kvp in ClassDict) {
			ClassEntry lookup = kvp.Value;
			if (stricmp(lookup.GetMapName(), mapname) != 0)
				continue;

			if (lookup.Factory == null) {
#if DEBUG
				Msg($"No factory for {lookup.GetMapName()}\n");
#endif
				continue;
			}

			return lookup.Factory();
		}

		return null;
	}

	public ReadOnlySpan<char> Lookup(ReadOnlySpan<char> classname) {
		if (!ClassDict.TryGetValue(classname.Hash(false), out var entry))
			return null;

		return entry.GetMapName();
	}

	public nint GetClassSize(ReadOnlySpan<char> classname) {
		if (!ClassDict.TryGetValue(classname.Hash(false), out var entry))
			return -1;

		return entry.Size;
	}
}
