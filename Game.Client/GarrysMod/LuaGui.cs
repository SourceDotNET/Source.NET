using Source.Common.GarrysMod.Lua;

using Source.Common.GUI;
using Source.Common.Input;

using System.Numerics;

#if MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Client.GarrysMod;
#endif

public static partial class LuaGui
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_gui = new("gui");

#if !MENU_DLL
	[LuaFunction]
	static int EnableScreenClicker(ILuaInterface lua) {
		if (!g_Lua!.GetBool(1)) {
			GModBase.GetMouseInput()!.SetMouseInputEnabled(false);
			GModBase.GetMouseInput()!.SetVisible(false);
			return 0;
		}

		GModBase.GetGModBasePanel(true)!.SetMouseInputEnabled(true);
		GModBase.GetMouseInput()!.SetVisible(true);
		GModBase.GetMouseInput()!.MakePopup(true, false);
		GModBase.GetMouseInput()!.SetMouseInputEnabled(true);
		GModBase.GetMouseInput()!.SetKeyboardInputEnabled(false);
		return 0;
	}
#endif

#if !MENU_DLL
	public static Vector3 ScreenToVector(int x, int y) {
		float halfWide = ScreenWidth() * 0.5f;
		float dx = x - halfWide;
		float dy = ScreenHeight() * 0.5f - y;
		float dz = (float)(halfWide / Math.Tan(ViewRender.g_FOV * MathF.PI / 360.0f));

		Vector3 vec = ViewRender.g_VecVRight * dx + ViewRender.g_VecVForward * dz + ViewRender.g_VecVUp * dy;
		return Vector3.Normalize(vec);
	}
#endif

	[LuaFunction]
	static (int, int) MousePos() {
		int x = 0, y = 0;
		if ((engine == null || engine.IsActiveApp()) && surface.IsCursorVisible())
			vguiInput.GetCursorPos(out x, out y);
		return (x, y);
	}

	[LuaFunction]
	static int MouseX() {
		int x = 0;
		if ((engine == null || engine.IsActiveApp()) && surface.IsCursorVisible())
			vguiInput.GetCursorPos(out x, out _);
		return x;
	}

	[LuaFunction]
	static int MouseY() {
		int y = 0;
		if ((engine == null || engine.IsActiveApp()) && surface.IsCursorVisible())
			vguiInput.GetCursorPos(out _, out y);
		return y;
	}

	[LuaFunction]
	static void SetMousePos([LuaGet] int x, [LuaGet] int y) {
		if (engine != null && !engine.IsActiveApp())
			return;
		if (!enginevgui.IsGameUIVisible())
			vguiInput.SetCursorPos(x, y);
	}

#if MENU_DLL
	static bool CanInjectInput() {
		IPanel? focus = vguiInput.GetFocus();
		if (focus == null)
			return true;
		return focus.IsVisible();
	}

	[LuaFunction]
	static int InternalCursorMoved(ILuaInterface lua) {
		if (!enginevgui.IsGameUIVisible() && CanInjectInput()) {
			int y = (int)lua.CheckNumber(2);
			vguiInput.InternalCursorMoved((int)lua.CheckNumber(1), y);
		}
		return 0;
	}

	[LuaFunction]
	static int InternalMousePressed(ILuaInterface lua) {
		if (!enginevgui.IsGameUIVisible() && CanInjectInput()) {
			ButtonCode code = (ButtonCode)(int)lua.CheckNumber(1);
			vguiInput.SetMouseCodeState(code, MouseCodeState.Pressed);
			vguiInput.InternalMousePressed(code);
		}
		return 0;
	}

	[LuaFunction]
	static int InternalMouseDoublePressed(ILuaInterface lua) {
		if (!enginevgui.IsGameUIVisible() && CanInjectInput())
			vguiInput.InternalMouseDoublePressed((ButtonCode)(int)lua.CheckNumber(1));
		return 0;
	}

	[LuaFunction]
	static int InternalMouseReleased(ILuaInterface lua) {
		if (!enginevgui.IsGameUIVisible() && CanInjectInput()) {
			ButtonCode code = (ButtonCode)(int)lua.CheckNumber(1);
			vguiInput.SetMouseCodeState(code, MouseCodeState.Released);
			vguiInput.InternalMouseReleased(code);
		}
		return 0;
	}

	[LuaFunction]
	static int InternalMouseWheeled(ILuaInterface lua) {
		if (!enginevgui.IsGameUIVisible() && CanInjectInput())
			vguiInput.InternalMouseWheeled((int)lua.CheckNumber(1));
		return 0;
	}

	[LuaFunction]
	static int InternalKeyCodePressed(ILuaInterface lua) {
		if (!enginevgui.IsGameUIVisible() && CanInjectInput())
			vguiInput.InternalKeyCodePressed((ButtonCode)(int)lua.CheckNumber(1));
		return 0;
	}

	[LuaFunction]
	static int InternalKeyCodeTyped(ILuaInterface lua) {
		if (!enginevgui.IsGameUIVisible() && CanInjectInput())
			vguiInput.InternalKeyCodeTyped((ButtonCode)(int)lua.CheckNumber(1));
		return 0;
	}

	[LuaFunction]
	static int InternalKeyTyped(ILuaInterface lua) {
		if (!enginevgui.IsGameUIVisible() && CanInjectInput())
			vguiInput.InternalKeyTyped((char)(int)lua.CheckNumber(1));
		return 0;
	}

	[LuaFunction]
	static int InternalKeyCodeReleased(ILuaInterface lua) {
		if (!enginevgui.IsGameUIVisible() && CanInjectInput())
			vguiInput.InternalKeyCodeReleased((ButtonCode)(int)lua.CheckNumber(1));
		return 0;
	}

	public static void OpenURLExternal(string url) {
		if (url.Length > 0x200)
			return;
		if (url.Contains('\n') || url.Contains('\r'))
			return;
		if (!url.StartsWith("http://") && !url.StartsWith("https://"))
			return;

		if (Steamworks.SteamUtils.IsOverlayEnabled()) {
			Steamworks.SteamFriends.ActivateGameOverlayToWebPage(url);
			return;
		}

		if (g_Lua == null || g_Lua.Global() == null)
			return;

		LuaObject func = new();
		g_Lua.Global().GetMember("GMOD_OpenURLNoOverlay", func);
		if (func.isFunction()) {
			func.Push();
			g_Lua.PushString(url);
			g_Lua.CallInternalNoReturns(1);
		}
		func.UnReference();
	}

	[LuaFunction]
	static int OpenURL(ILuaInterface lua) {
		OpenURLExternal(lua.CheckString(1).ToString());
		return 0;
	}
#endif

	[LuaFunction]
	static bool IsGameUIVisible() => enginevgui.IsGameUIVisible();

	[LuaFunction]
	static bool IsConsoleVisible() => enginevgui.IsConsoleVisible();

	[LuaFunction]
	static void ActivateGameUI() {
		if (!enginevgui.IsGameUIVisible())
			engine.ClientCmd_Unrestricted("gameui_activate");
	}

#if MENU_DLL
	[LuaFunction]
	static int ShowConsole(ILuaInterface lua) {
		enginevgui.ShowConsole();
		return 0;
	}

	[LuaFunction]
	static int HideGameUI(ILuaInterface lua) {
		engine.ClientCmd_Unrestricted("gameui_hide");
		return 0;
	}
#endif
	// todo: AddCaption
}
