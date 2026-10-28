#if CLIENT_DLL || MENU_DLL
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.MenuSystem.GarrysMod;
#endif

public static partial class LuaVideo
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_video = new("video");

	[LuaFunction]
	static int Record(ILuaInterface lua) {
		// todo!!!
		return 0;
	}
}
#endif
