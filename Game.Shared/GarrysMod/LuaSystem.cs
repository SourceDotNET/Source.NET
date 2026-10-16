#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaSystem
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_system = new("system");

	[LuaFunction]
	static bool IsWindows() => OperatingSystem.IsWindows();

	[LuaFunction]
	static bool IsOSX() => OperatingSystem.IsMacOS();

	[LuaFunction]
	static bool IsLinux() => OperatingSystem.IsLinux();

#if CLIENT_DLL || MENU_DLL
	[LuaFunction]
	static int HasFocus(ILuaInterface lua) {
		lua.PushBool(engine.IsActiveApp());
		return 1;
	}

	[LuaFunction]
	static int BatteryPower(ILuaInterface lua) {
		lua.PushNumber(Steamworks.SteamUtils.GetCurrentBatteryPower());
		return 1;
	}

	[LuaFunction]
	static int AppTime(ILuaInterface lua) {
		lua.PushNumber(Steamworks.SteamUtils.GetSecondsSinceAppActive());
		return 1;
	}

	[LuaFunction]
	static int UpTime(ILuaInterface lua) {
		lua.PushNumber(Steamworks.SteamUtils.GetSecondsSinceComputerActive());
		return 1;
	}

	[LuaFunction]
	static int SteamTime(ILuaInterface lua) {
		lua.PushNumber(Steamworks.SteamUtils.GetServerRealTime());
		return 1;
	}

	[LuaFunction]
	static int GetCountry(ILuaInterface lua) {
		lua.PushString(Steamworks.SteamUtils.GetIPCountry());
		return 1;
	}

	[LuaFunction]
	static int FlashWindow(ILuaInterface lua) {
		engine.FlashWindow();
		return 0;
	}

	[LuaFunction]
	static int IsWindowed(ILuaInterface lua) {
		lua.PushBool(engine.IsWindowedMode());
		return 1;
	}
#endif
}
#endif
