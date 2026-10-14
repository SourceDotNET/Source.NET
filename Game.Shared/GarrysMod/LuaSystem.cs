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

	// todo: HasFocus
	// todo: BatteryPower
	// todo: AppTime
	// todo: UpTime
	// todo: SteamTime
	// todo: GetCountry
#if CLIENT_DLL
	// todo: FlashWindow
	// todo: IsWindowed
#endif
}
#endif
