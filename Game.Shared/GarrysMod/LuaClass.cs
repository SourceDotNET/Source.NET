#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public unsafe class LuaClassFunction
{
	public string? Name;
	public string? Unknown1;
	public CFunc? Function;
	public delegate* unmanaged[Cdecl]<nint, int> NativeFunction;
	public int Unknown2;
}

public class LuaClass
{
	public static List<LuaClass>? g_LuaClasses;

	public string Name;
	public string? DerivedFrom;
	public LuaType Type;
	public List<LuaClassFunction>? Functions;
	public LuaObject MetaTable;
	public Action? InitFn;

	public LuaClass(string name, LuaType type, Action? initFn, string? derivedFrom) {
		MetaTable = new();
		Functions = null;
		Name = name;
		DerivedFrom = derivedFrom;
		Type = type;
		InitFn = initFn;
		Functions = [];
		(g_LuaClasses ??= []).Add(this);
	}

	static int __index_Derived(ILuaInterface lua) {
		Error("This should never get called (yet)\n");
		return 0;
	}

	public object? Get(int stackPos) {
		object? obj = g_Lua!.GetObjectUserType<object>(stackPos, Type);
		if (obj == null && !g_Lua.IsType(stackPos, Type)) {
			g_Lua.TypeError(Name, stackPos);
			return null;
		}
		return obj;
	}

	public ref T GetValue<T>(int stackPos) where T : unmanaged {
		if (!g_Lua!.IsType(stackPos, Type))
			g_Lua.TypeError(Name, stackPos);
		return ref g_Lua.GetValueUserType<T>(stackPos, Type);
	}

	public bool Is(int stackPos) => g_Lua!.IsType(stackPos, Type);

	public void Add(LuaClassFunction func) {
		Functions ??= [];
		Functions.Add(func);
	}

	public LuaClassFunction Add(string name, CFunc function) {
		LuaClassFunction func = new() { Name = name, Function = function };
		Add(func);
		return func;
	}

	public unsafe LuaClassFunction Add(string name, delegate* unmanaged[Cdecl]<nint, int> function) {
		LuaClassFunction func = new() { Name = name, NativeFunction = function };
		Add(func);
		return func;
	}

	public void Push(object? data) {
		if (!MetaTable.isTable())
			Error("CLuaClass::Push - Not Table!");
		g_Lua!.PushObjectUserType(data, Type);
	}

	public virtual void MetaTableDerive() {
		if (DerivedFrom == null)
			return;

		ILuaObject? baseMeta = g_Lua!.GetMetaTableObject(DerivedFrom, -1);
		MetaTable.SetMember("MetaBaseClass", baseMeta);
		MetaTable.SetMember("__index", __index_Derived);
		LuaObject gc = new();
		MetaTable.GetMember("__gc", gc);
		if (gc.isNil()) {
			baseMeta!.GetMember("__gc", gc);
			if (!gc.isNil())
				MetaTable.SetMember("__gc", gc);
		}
		gc.UnReference();
	}

	public void InitClasses() {
		ILuaObject? meta = g_Lua!.GetMetaTableObject(Name, (int)Type);
		if (meta == null)
			Error($"Error initializing class {Name}\n");

		MetaTable.Set(meta);
		if (!MetaTable.isTable())
			Error("m_metatable isn't table");

		if (DerivedFrom == null)
			MetaTable.SetMember("__index", MetaTable);

		for (int i = 0; i < Functions!.Count; i++) {
			unsafe {
				if (Functions[i].NativeFunction != null)
					MetaTable.SetMember(Functions[i].Name, Functions[i].NativeFunction);
				else
					MetaTable.SetMember(Functions[i].Name, Functions[i].Function!);
			}
		}

		InitFn?.Invoke();
	}

	public void Shutdown() => MetaTable.UnReference();

	public static void InitLuaClasses(ILuaInterface lua) {
		if (g_LuaClasses == null)
			return;

		for (int i = 0; i < g_LuaClasses.Count; i++)
			g_LuaClasses[i].InitClasses();

		for (int i = 0; i < g_LuaClasses.Count; i++)
			g_LuaClasses[i].MetaTableDerive();
	}

	public static void ShutdownLuaClasses(ILuaInterface lua) {
		if (g_LuaClasses == null)
			return;

		for (int i = 0; i < g_LuaClasses.Count; i++)
			g_LuaClasses[i].MetaTable.UnReference();
	}
}
#endif
