#if MENU_DLL
using Source;
using Source.Common.GarrysMod.Lua;
using Source.Common.Launcher;

namespace Game.MenuSystem.GarrysMod;

public static partial class LuaMenuGlobals
{
	[LuaGlobal]
	static int GetAddonStatus(ILuaInterface lua) {
		lua.PushBool(commandLine.FindParm("-noaddons") == 0);
		lua.PushBool(commandLine.FindParm("-noworkshop") == 0);
		return 2;
	}

	[LuaGlobal]
	static int GetDefaultLoadingHTML(ILuaInterface lua) {
		lua.PushString("asset://garrysmod/html/loading.html");
		return 1;
	}

	[LuaGlobal]
	static int GetOverlayPanel(ILuaInterface lua) {
		LuaVGUI.Push_Panel(MenuPanels.GetOverlayPanel());
		return 1;
	}

	[LuaGlobal]
	static int IsHostingGame(ILuaInterface lua) {
		lua.PushBool(engine.IsHostingGame());
		return 1;
	}

	[LuaGlobal]
	static int IsInGame(ILuaInterface lua) {
		lua.PushBool(engine.IsInGame());
		return 1;
	}

	[LuaGlobal]
	static int JoinServer(ILuaInterface lua) {
		string? address = lua.CheckString(1);
		if (address == null || address.Contains(';'))
			return 0;
		string command = $"connect {address} serverbrowser\n";
		if (command.Length > 255)
			command = command[..255];
		engine.ClientCmd(command);
		return 0;
	}

	[LuaGlobal]
	static int OpenFolder(ILuaInterface lua) {
		string name = lua.CheckString(1);
		if (name != "saves" && name != "demos" && name != "dupes")
			return 0;

		Span<char> path = stackalloc char[512];
		ReadOnlySpan<char> fullPath = filesystem.RelativePathToFullPath(name, "MOD", path);
		if (!fullPath.IsEmpty)
			Singleton<ISystem>().ShellExecute("open", fullPath);
		return 0;
	}

	[LuaGlobal]
	static int RunGameUICommand(ILuaInterface lua) {
		gameUI.SendMainMenuCommand(lua.CheckString(1));
		return 0;
	}
}
#endif
