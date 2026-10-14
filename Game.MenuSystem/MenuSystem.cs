using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

#if MENU_DLL
using Game.MenuSystem.GarrysMod;

using Source.GUI.Controls;
#endif

namespace Game.MenuSystem;

public class MenuSystem : IMenuSystem
{
#if MENU_DLL
	public static MenuSystem g_MenuSystem = null!;

	bool WasInGame;
	readonly AddonDownloadNotifications DownloadNotifications = new();
	int LuaStarts;
	int LastFilesystemRefresh;

	public MenuSystem() => g_MenuSystem = this;

	public int Init(IServiceProvider services, IGet get, IGarrysMod gmod, GlobalVarsBase vars) {
		SourceDllMain.gpGlobals = vars;
		SourceDllMain.garrysmod = gmod;
		SourceDllMain.get = get;

		filesystem.Addons().SetDownloadNotify(DownloadNotifications);
		return 1;
	}

	public void Shutdown() {
		filesystem.Addons().SetDownloadNotify(null);
		CloseLua();
		DestroyLoadingPanel();
		DestroyMenuPanels();
	}

	public void SetupNetworkString(INetworkStringTableContainer networkStringTableContainer) => networkstringtable = networkStringTableContainer;

	static void CallHook(ReadOnlySpan<char> name) {
		if (g_Lua == null || g_Lua.Global() == null)
			return;

		LuaObject hook = new();
		g_Lua.Global().GetMember("hook", hook);
		if (hook.isTable()) {
			LuaObject call = new();
			hook.GetMember("Call", call);
			if (call.isFunction()) {
				call.Push();
				g_Lua.PushString(name);
				g_Lua.CallInternalNoReturns(1);
			}
			call.UnReference();
		}
		hook.UnReference();
	}

	public void Think() {
		if (g_Lua != null && g_Lua.Global() != null) {
			g_Lua.Cycle();
			LuaTimer.Cycle();
			CallHook("Think");
		}

		garrysmod.Update();

		if (filesystem.LastFilesystemRefresh() != LastFilesystemRefresh) {
			LastFilesystemRefresh = filesystem.LastFilesystemRefresh();
			CallHook("GameContentChanged");
		}

		WasInGame = engine.IsInGame();
	}

	void CloseLua() {
		if (g_Lua == null)
			return;

		LuaStarts++;
		get.LuaShared()!.UnMountLua("LuaMenu");
		get.LuaShared()!.CloseLuaInterface(g_Lua);
		g_Lua = null;
	}

	public void StartLua() {
		CloseLua();
		MenuPanels.CreateMenuPanels();

		get.LuaShared()!.MountLua("LuaMenu");
		g_Lua = get.LuaShared()!.CreateLuaInterface(Realm.Menu, false);
		g_Lua.Init(LuaMenuCallback.g_MenuLuaCallback, commandLine.CheckParm("-withjit"));
		g_Lua.SetPathID("LuaMenu");
		g_Lua.SetType(2);

		g_Lua.Global().SetMember("CLIENT", true);
		g_Lua.Global().SetMember("SERVER", false);
		g_Lua.Global().SetMember("VERSION", (float)get.Version());
		g_Lua.Global().SetMember("VERSIONSTR", get.VersionStr());
		g_Lua.Global().SetMember("NETVERSIONSTR", engine.GetProductVersionString());
		g_Lua.Global().SetMember("BRANCH", get.Branch());

		LuaObject debug = new();
		g_Lua.Global().GetMember("debug", debug);
		if (debug.isTable())
			debug.SetMemberNil("getupvalue");
		debug.UnReference();

		LuaUser.InitLuaLibraries(g_Lua);
		LuaClass.InitLuaClasses(g_Lua);

		g_Lua.FindAndRunScript("includes/init_menu.lua", true, true, "!UNKNOWN", true);
		g_Lua.FindAndRunScript("derma/init.lua", true, true, "!UNKNOWN", true);
		g_Lua.FindAndRunScript("includes/vgui_base.lua", true, true, "!UNKNOWN", true);
		g_Lua.FindAndRunScript("skins/default.lua", true, true, "!UNKNOWN", true);
		g_Lua.FindAndRunScript("includes/menu.lua", true, true, "!UNKNOWN", true);

		CreateLoadingPanel();

		CallHook("MenuStart");
	}

	static void DestroyLoadingPanel() {
		gameUI.SetLoadingBackgroundDialog(null);
		if (MenuPanels.g_hLoadingPanel == null)
			return;

		MenuPanels.g_hLoadingPanel.SetParent((Panel?)null);
		MenuPanels.g_hLoadingPanel.MarkForDeletion();
		MenuPanels.g_hLoadingPanel = null;
	}

	static void DestroyMenuPanels() {
		gameUI.SetMainMenuOverride(null);

		if (MenuPanels.g_hMenuPanel != null) {
			MenuPanels.g_hMenuPanel.SetParent((Panel?)null);
			MenuPanels.g_hMenuPanel.MarkForDeletion();
			MenuPanels.g_hMenuPanel = null;
		}

		if (MenuPanels.g_hOverlayPanel != null) {
			MenuPanels.g_hOverlayPanel.SetParent((Panel?)null);
			MenuPanels.g_hOverlayPanel.DeletePanel();
			MenuPanels.g_hOverlayPanel = null;
		}
	}

	static void CreateLoadingPanel() {
		DestroyLoadingPanel();

		if (g_Lua == null || g_Lua.Global() == null)
			return;

		GModLoadingPanel loading = new();
		Surface.GetScreenSize(out int wide, out int tall);
		loading.SetSize(wide, tall);
		loading.SetPos(0, 0);
		MenuPanels.g_hLoadingPanel = loading;
		loading.SetVisible(false);
		loading.MakePopup(false, false);
		gameUI.SetLoadingBackgroundDialog(loading);

		LuaObject getLoadPanel = new();
		g_Lua.Global().GetMember("GetLoadPanel", getLoadPanel);
		if (getLoadPanel.isFunction()) {
			LuaObject result = new();
			getLoadPanel.Push();
			if (g_Lua.CallInternalGet(0, result) && result.GetType() == LuaType.Panel) {
				result.Push();
				Panel? panel = LuaVGUI.Get_Panel(-1);
				g_Lua.Pop(1);
				if (panel != null) {
					panel.InvalidateLayout(false, true);
					panel.SetParent(MenuPanels.g_hLoadingPanel);
				}
				g_Lua.Global().SetMemberNil("GetLoadPanel");
			}
			result.UnReference();
		}
		getLoadPanel.UnReference();
	}

	public void OnLuaError(in LuaError err, IAddonSystem.Information? addonInfo) {
		if (addonInfo.HasValue)
			LuaHelper.CallOnLuaErrorHook(in err, addonInfo.Value.Title ?? "", addonInfo.Value.WorkshopID);
		else
			LuaHelper.CallOnLuaErrorHook(in err, null, 0);
	}
#else
	public int Init(IServiceProvider services, IGet get, IGarrysMod gmod, GlobalVarsBase vars) => 1;
	public void Shutdown() { }
	public void SetupNetworkString(INetworkStringTableContainer networkStringTableContainer) { }
	public void Think() { }
	public void StartLua() { }
	public void OnLuaError(in LuaError err, IAddonSystem.Information? addonInfo) { }
#endif

	public bool IsServerBlacklisted(ReadOnlySpan<char> address, ReadOnlySpan<char> hostname, ReadOnlySpan<char> description, ReadOnlySpan<char> gamemode, ReadOnlySpan<char> map) {
		throw new NotImplementedException();
	}

	public void SendProblemToMenu(ReadOnlySpan<char> id, int severity, ReadOnlySpan<char> parms) {
		Msg($"SendProblemToMenu not implemented! ({id.SliceNullTerminatedString()}, {severity}, {parms.SliceNullTerminatedString()})\n");
	}

	public void ServerDetails(ReadOnlySpan<char> unk1, ReadOnlySpan<char> unk2, ReadOnlySpan<char> unk3, int unk4, ReadOnlySpan<char> unk5) {
		throw new NotImplementedException();
	}
}
