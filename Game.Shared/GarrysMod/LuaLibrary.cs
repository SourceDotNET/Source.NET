#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public unsafe class LuaLibraryFunction
{
	public string? Name;
	// public string? Unknown1;
	public CFunc? Function;
	public delegate* unmanaged[Cdecl]<nint, int> NativeFunction;
}

public class LuaLibrary(string name) : LuaUser
{
	public string Name = name;
	public List<LuaLibraryFunction> Functions = [];

	public void Add(LuaLibraryFunction func) {
		Functions.Add(func);
		SetUsingLua(true);
	}

	public unsafe LuaLibraryFunction Add(string name, delegate* unmanaged[Cdecl]<nint, int> function) {
		LuaLibraryFunction func = new() { Name = name, NativeFunction = function };
		Add(func);
		return func;
	}

	public override void InitLibraries(ILuaInterface lua) {
		LuaTable table = new(Name, 0);
		for (int i = 0; i < Functions.Count; i++) {
			unsafe {
				if (Functions[i].NativeFunction != null)
					table.SetMember(Functions[i].Name, Functions[i].NativeFunction);
				else
					table.SetMember(Functions[i].Name, Functions[i].Function!);
			}
		}
		table.UnReference();
	}
}

public class LuaGlobalLibrary() : LuaLibrary("GLOBAL")
{
	static LuaGlobalLibrary? Factory;

	public static LuaGlobalLibrary GetGlobalLuaLibraryFactory() => Factory ??= new();

	public static LuaLibraryFunction Add(string name, CFunc function) {
		LuaLibraryFunction func = new() { Name = name, Function = function };
		GetGlobalLuaLibraryFactory().Add(func);
		return func;
	}

	public static new unsafe LuaLibraryFunction Add(string name, delegate* unmanaged[Cdecl]<nint, int> function) {
		LuaLibraryFunction func = new() { Name = name, NativeFunction = function };
		GetGlobalLuaLibraryFactory().Add(func);
		return func;
	}

	public override void InitLibraries(ILuaInterface lua) {
		for (int i = 0; i < Functions.Count; i++) {
			unsafe {
				if (Functions[i].NativeFunction != null)
					lua.Global().SetMember(Functions[i].Name, Functions[i].NativeFunction);
				else
					lua.Global().SetMember(Functions[i].Name, Functions[i].Function!);
			}
		}
	}
}
#endif
