#if MENU_DLL
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Game.MenuSystem;

public class MenuScreenPanel : EditablePanel
{
	public MenuScreenPanel() : base(null, "GModMenuBasePanel") { }
}

public class GModLoadingPanel : EditablePanel
{
	public GModLoadingPanel() : base(null, "LoadingScreenBasePanel") { }

	public override void OnActivate() {
		if (GetChildCount() == 0)
			return;

		Panel? child = GetChild(0);
		if (child != null)
			PostMessage(child, new KeyValues("activate"));
	}

	public override void OnDeactivate() {
		if (GetChildCount() == 0)
			return;

		Panel? child = GetChild(0);
		if (child != null)
			PostMessage(child, new KeyValues("deactivate"));
	}
}

public class OverlayPanel : Panel
{
	public OverlayPanel() : base(null, "GModOverlayPopupPanel") { }

	public override void OnThink() {
		if (IsMarkedForDeletion())
			return;

		Surface.MovePopupToFront(this);
		MenuSystem.g_MenuSystem.Think();
	}
}

public static class MenuPanels
{
	public static Panel? g_hMenuPanel;
	public static Panel? g_hOverlayPanel;
	public static Panel? g_hLoadingPanel;

	public static Panel? GetMenuPanel() {
		if (g_hMenuPanel == null)
			Warning("Trying to create VGUI too early!\n");
		return g_hMenuPanel;
	}

	public static Panel? GetOverlayPanel() => g_hOverlayPanel;

	public static bool IsValidPanel(Panel? panel) {
		if (panel != null && panel != GetMenuPanel() && panel != GetOverlayPanel())
			return panel.LuaPanel && !panel.IsMarkedForDeletion();
		return true;
	}

	public static void CreateMenuPanels() {
		gameUI.SetMainMenuOverride(null);

		if (g_hMenuPanel != null) {
			g_hMenuPanel.SetParent((Panel?)null);
			g_hMenuPanel.MarkForDeletion();
			g_hMenuPanel = null;
		}

		if (g_hOverlayPanel != null) {
			g_hOverlayPanel.SetParent((Panel?)null);
			g_hOverlayPanel.DeletePanel();
			g_hOverlayPanel = null;
		}

		MenuScreenPanel menuPanel = new();
		menuPanel.SetScheme(SchemeManager.LoadSchemeFromFile("resource/ClientScheme.res", "ClientScheme")!);
		Surface.GetScreenSize(out int wide, out int tall);
		menuPanel.SetSize(wide, tall);
		menuPanel.SetPos(0, 0);

		OverlayPanel overlayPanel = new();
		Surface.GetScreenSize(out wide, out tall);
		overlayPanel.SetSize(wide, tall);
		overlayPanel.SetPos(0, 0);
		g_hOverlayPanel = overlayPanel;
		g_hOverlayPanel.SetParent(Surface.GetEmbeddedPanel());
		g_hOverlayPanel.MakePopup(true, false);
		g_hOverlayPanel.SetKeyboardInputEnabled(false);
		g_hOverlayPanel.SetMouseInputEnabled(false);

		gameUI.SetMainMenuOverride(menuPanel);
		menuPanel.SetProportional(false);
		menuPanel.SetKeyboardInputEnabled(true);
		menuPanel.SetMouseInputEnabled(true);
		menuPanel.MoveToFront();
		g_hMenuPanel = menuPanel;
	}
}
#endif
