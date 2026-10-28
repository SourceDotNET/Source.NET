using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;

using System.Numerics;

using static Source.Common.GarrysMod.Lua.PooledStrings;
using Source.Common.Input;
using Source.Engine;
using Source.GUI.Controls;

namespace Game.Client.GarrysMod;

public class GModBase : Panel
{
	static GModBase() => ChainToAnimationMap<GModBase>();

	static Panel? BasePanel;

	bool ParentToHUD;
	bool FirstThink;

	public static Panel? GetGModBasePanel(bool create) {
		if (BasePanel == null && create)
			BasePanel = new GModBase("GModBasePanel");

		return BasePanel;
	}

	static Panel? MouseInput;

	public static Panel? GetMouseInput() {
		if (MouseInput == null) {
			Panel parent = GetGModBasePanel(true)!;
			GModMouseInput panel = new(parent, "GModMouseInput");
			panel.SetParent(parent);
			panel.SetVisible(false);
			panel.SetPaintBackgroundEnabled(true);
			panel.SetMoveable(false);
			panel.SetCloseButtonVisible(true);
			panel.SetMinimizeButtonVisible(true);
			panel.SetMaximizeButtonVisible(true);
			panel.SetMenuButtonVisible(true);
			panel.SetMenuButtonResponsive(false);
			panel.SetSizeable(false);
			panel.SetPos(-500, -500);
			panel.SetSize(1, 1);
			MouseInput = panel;
			MouseInput.SetKeyboardInputEnabled(false);
			MouseInput.SetVisible(false);
		}

		return MouseInput;
	}

	static Panel? ParentToHUDPanel;

	public static Panel? GetGModParentToHUDPanel() {
		if (ParentToHUDPanel == null) {
			GModBase panel = new("GModParentToHUDPanel");
			ParentToHUDPanel = panel;
			ParentToHUDPanel.SetParent(enginevgui.GetPanel(VGuiPanelType.ClientDll));
			panel.ParentToHUD = true;
		}

		return ParentToHUDPanel;
	}

	static void DestroyPanel(ref Panel? panel) {
		if (panel == null)
			return;
		panel.DeletePanel();
		panel = null;
	}

	public static void Shutdown() {
		if (HudGMod.g_HudGMod != null) {
			while (HudGMod.g_HudGMod.GetChildCount() > 0)
				HudGMod.g_HudGMod.GetChild(0).DeletePanel();
		}

		DestroyPanel(ref MouseInput);
		DestroyPanel(ref BasePanel);
		DestroyPanel(ref ParentToHUDPanel);
	}

	public GModBase(ReadOnlySpan<char> panelName) : base(null, panelName) {
		FirstThink = false;
		SetParent(enginevgui.GetPanel(VGuiPanelType.Root));
		SetScheme(SchemeManager.LoadSchemeFromFileEx(enginevgui.GetPanel(VGuiPanelType.ClientDll), "resource/ClientScheme.res", "ClientScheme")!);
		SetProportional(false);
		SetMouseInputEnabled(true);
		SetKeyboardInputEnabled(true);
		SetVisible(true);
		SetSize(ScreenWidth(), ScreenHeight());
		SetPos(0, 0);
		base.OnScreenSizeChanged(0, 0);
		FirstThink = true;
		SetZPos(90);
	}

	public override void Think() {
		if (FirstThink) {
			LuaFonts.RecreateFonts();
			FirstThink = false;
		}
	}

	public override void OnMousePressed(ButtonCode code) {
		if (engine.IsPaused())
			return;

		vguiInput.GetCursorPos(out int x, out int y);
		Vector3 aim = LuaGui.ScreenToVector(x, y);
		if (gGM == null || !gGM.CallWithArgs((int)LUA_POOLEDSTRING.GUIMousePressed))
			return;

		g_Lua!.PushNumber((int)code);
		g_Lua.PushVector(aim);
		gGM.CallNoReturns(2);
	}

	public override void OnMouseDoublePressed(ButtonCode code) {
		if (engine.IsPaused())
			return;

		vguiInput.GetCursorPos(out int x, out int y);
		Vector3 aim = LuaGui.ScreenToVector(x, y);
		if (gGM == null || !gGM.CallWithArgs((int)LUA_POOLEDSTRING.GUIMouseDoublePressed))
			return;

		g_Lua!.PushNumber((int)code);
		g_Lua.PushVector(aim);
		gGM.CallNoReturns(2);
	}

	public override void OnMouseReleased(ButtonCode code) {
		if (engine.IsPaused())
			return;

		vguiInput.GetCursorPos(out int x, out int y);
		Vector3 aim = LuaGui.ScreenToVector(x, y);
		if (gGM == null || !gGM.CallWithArgs((int)LUA_POOLEDSTRING.GUIMouseReleased))
			return;

		g_Lua!.PushNumber((int)code);
		g_Lua.PushVector(aim);
		gGM.CallNoReturns(2);
	}

	public override void OnScreenSizeChanged(int oldWide, int oldTall) {
		SetSize(ScreenWidth(), ScreenHeight());
		SetPos(0, 0);
		base.OnScreenSizeChanged(oldWide, oldTall);
		FirstThink = true;

		if (oldWide != 0 && !ParentToHUD && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.OnScreenSizeChanged)) {
			g_Lua!.PushNumber(oldWide);
			g_Lua.PushNumber(oldTall);
			g_Lua.PushNumber(ScreenWidth());
			g_Lua.PushNumber(ScreenHeight());
			gGM.CallNoReturns(4);
		}
	}
}

public class GModMouseInput : Frame
{
	static GModMouseInput() => ChainToAnimationMap<GModMouseInput>();

	public GModMouseInput(Panel? parent, ReadOnlySpan<char> name) : base(parent, name, true, true) { }

	public override void OnScreenSizeChanged(int oldWide, int oldTall) {
		base.OnScreenSizeChanged(oldWide, oldTall);
		SetSize(1, 1);
		SetPos(-500, -500);
	}
}
