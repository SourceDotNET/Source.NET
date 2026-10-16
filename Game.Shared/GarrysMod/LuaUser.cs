#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public class LuaUser : ILuaUser
{
	static List<LuaUser>? g_LuaUsers;

	bool UsingLua;

	public LuaUser() => UsingLua = false;

	public virtual bool IsUsingLua() => UsingLua;

	public virtual void InitLibraries(ILuaInterface lua) { }

	public void SetUsingLua(bool usingLua) {
		if (UsingLua == usingLua)
			return;

		g_LuaUsers ??= [];
		if (usingLua)
			g_LuaUsers.Add(this);
		else
			g_LuaUsers.Remove(this);

		UsingLua = usingLua;
	}

	public static void InitLuaLibraries(ILuaInterface lua) {
		if (g_LuaUsers == null)
			return;

		foreach (LuaUser user in g_LuaUsers)
			user.InitLibraries(lua);
	}
}
#endif
