using Source.Common.GarrysMod.Lua;

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
			GModBase.GetMouseInput()!.SetKeyboardInputEnabled(false);
			GModBase.GetMouseInput()!.SetVisible(false);
			return 0;
		}

		GModBase.GetGModBasePanel(true)!.SetKeyboardInputEnabled(true);
		GModBase.GetMouseInput()!.SetVisible(true);
		GModBase.GetMouseInput()!.MakePopup(true, false);
		GModBase.GetMouseInput()!.SetKeyboardInputEnabled(true);
		GModBase.GetMouseInput()!.SetMouseInputEnabled(false);
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

	// todo: ScreenToVector
	// todo: InternalCursorMoved
	// todo: InternalMousePressed
	// todo: InternalMouseDoublePressed
	// todo: InternalMouseReleased
	// todo: InternalMouseWheeled
	// todo: InternalKeyCodePressed
	// todo: InternalKeyCodeTyped
	// todo: InternalKeyTyped
	// todo: InternalKeyCodeReleased
	// todo: OpenURL

	[LuaFunction]
	static bool IsGameUIVisible() => enginevgui.IsGameUIVisible();

	[LuaFunction]
	static bool IsConsoleVisible() => enginevgui.IsConsoleVisible();

	[LuaFunction]
	static void ActivateGameUI() {
		if (!enginevgui.IsGameUIVisible())
			engine.ClientCmd_Unrestricted("gameui_activate");
	}

	// todo: HideGameUI
	// todo: AddCaption
}
