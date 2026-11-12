
using Microsoft.Extensions.DependencyInjection;

using SDL;

using Source;
using Source.Common.Commands;
using Source.Common.Input;
using Source.Common.Launcher;

using System.Runtime.InteropServices;

namespace Nucleus.SDL3Window;

public class InputState
{
	public bool[] ButtonState;
	public int[] ButtonPressedTick;
	public int[] ButtonReleasedTick;
	public int[] AnalogDelta;
	public int[] AnalogValue;
	public Queue<InputEvent> Events;
	public bool Dirty;
	public InputState() {
		ButtonState = new bool[(int)ButtonCode.Count];
		ButtonPressedTick = new int[(int)ButtonCode.Count];
		ButtonReleasedTick = new int[(int)ButtonCode.Count];
		AnalogDelta = new int[(int)AnalogCode.Last];
		AnalogValue = new int[(int)AnalogCode.Last];
		Events = [];
		Dirty = false;
	}
}

public class SDL3_InputSystem(IServiceProvider services) : IInputSystem
{
	public InputState[] InputStates = [new(), new()];

	InputState InputState => IsPolling ? InputStates[1] : InputStates[0];
	InputState QueuedInputState => InputStates[(int)InputStateType.Queued];
	InputState CurrentInputState => InputStates[(int)InputStateType.Current];

	public IWindow? Window;
	public bool InputEnabled;
	public bool PumpEnabled;
	public bool IsPolling;

	int LastSampleTick;
	int LastPollTick;
	int PollCount;

	public unsafe void StartTextInput() {
		SDL_Window* window = (SDL_Window*)Window!.GetHandle();
		SDL3.SDL_StartTextInput(window);
	}

	public unsafe void StopTextInput() {
		SDL_Window* window = (SDL_Window*)Window!.GetHandle();
		SDL3.SDL_StopTextInput(window);
	}

	ILauncherManager launcherMgr;

	public void AttachToWindow(IWindow window) {
		Window = window;
		launcherMgr ??= services.GetRequiredService<ILauncherManager>();
		ClearInputState();
	}

	public int ButtonCodeToVirtualKey(ButtonCode code) {
		return buttonCodeToVirtual[(int)code];
	}

	public void DetachFromWindow() {
		Window = null;
	}

	public void EnableInput(bool enabled) => InputEnabled = enabled;
	public void EnableMessagePump(bool enabled) => PumpEnabled = enabled;

	public int GetButtonPressedTick(ButtonCode code) {
		throw new NotImplementedException();
	}

	public int GetButtonReleasedTick(ButtonCode code) {
		throw new NotImplementedException();
	}

	public long GetEventCount() => CurrentInputState.Events.Count;
	public IEnumerable<InputEvent> GetEventData() => CurrentInputState.Events;

	public int GetPollCount() {
		return PollCount;
	}

	public int GetPollTick() {
		return LastPollTick;
	}

	public bool GetRawMouseAccumulators(out int accumX, out int accumY) {
		throw new NotImplementedException();
	}

	public bool IsButtonDown(ButtonCode code) {
		return InputState.ButtonState.IsBitSet((int)code);
	}

	public int GetAnalogValue(AnalogCode code) => InputState.AnalogValue[(int)code];

	public void ClearInputState() {
		foreach (var state in InputStates) {
			state.Events.Clear();
			Array.Clear(state.ButtonState);
			Array.Clear(state.ButtonPressedTick);
			Array.Clear(state.ButtonReleasedTick);
			Array.Clear(state.AnalogValue);
			Array.Clear(state.AnalogDelta);
			state.Dirty = false;
		}
	}

	static ButtonCode[] scantokey;
	static SDL3_InputSystem() {
		scantokey = new ButtonCode[(int)SDL_Scancode.SDL_SCANCODE_COUNT];
		for (int i = (int)SDL_Scancode.SDL_SCANCODE_A; i <= (int)SDL_Scancode.SDL_SCANCODE_Z; i++)
			scantokey[i] = ButtonCode.KeyA + (i - (int)SDL_Scancode.SDL_SCANCODE_A);
		for (int i = (int)SDL_Scancode.SDL_SCANCODE_1; i <= (int)SDL_Scancode.SDL_SCANCODE_9; i++)
			scantokey[i] = ButtonCode.Key1 + (i - (int)SDL_Scancode.SDL_SCANCODE_1);
		for (int i = (int)SDL_Scancode.SDL_SCANCODE_F1; i <= (int)SDL_Scancode.SDL_SCANCODE_F12; i++)
			scantokey[i] = ButtonCode.KeyF1 + (i - (int)SDL_Scancode.SDL_SCANCODE_F1);
		for (int i = (int)SDL_Scancode.SDL_SCANCODE_KP_1; i <= (int)SDL_Scancode.SDL_SCANCODE_KP_9; i++)
			scantokey[i] = ButtonCode.KeyPad1 + (i - (int)SDL_Scancode.SDL_SCANCODE_KP_1);

		scantokey[(int)SDL_Scancode.SDL_SCANCODE_0] = ButtonCode.Key0;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_KP_0] = ButtonCode.KeyPad0;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_RETURN] = ButtonCode.KeyEnter;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_ESCAPE] = ButtonCode.KeyEscape;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_BACKSPACE] = ButtonCode.KeyBackspace;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_TAB] = ButtonCode.KeyTab;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_SPACE] = ButtonCode.KeySpace;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_MINUS] = ButtonCode.KeyMinus;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_EQUALS] = ButtonCode.KeyEqual;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_LEFTBRACKET] = ButtonCode.KeyLBracket;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET] = ButtonCode.KeyRBracket;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_BACKSLASH] = ButtonCode.KeyBackslash;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_SEMICOLON] = ButtonCode.KeySemicolon;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_APOSTROPHE] = ButtonCode.KeyApostrophe;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_GRAVE] = ButtonCode.KeyBackquote;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_COMMA] = ButtonCode.KeyComma;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_PERIOD] = ButtonCode.KeyPeriod;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_SLASH] = ButtonCode.KeySlash;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_CAPSLOCK] = ButtonCode.KeyCapsLock;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_SCROLLLOCK] = ButtonCode.KeyScrollLock;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_INSERT] = ButtonCode.KeyInsert;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_HOME] = ButtonCode.KeyHome;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_PAGEUP] = ButtonCode.KeyPageUp;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_DELETE] = ButtonCode.KeyDelete;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_END] = ButtonCode.KeyEnd;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_PAGEDOWN] = ButtonCode.KeyPageDown;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_RIGHT] = ButtonCode.KeyRight;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_LEFT] = ButtonCode.KeyLeft;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_DOWN] = ButtonCode.KeyDown;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_UP] = ButtonCode.KeyUp;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_NUMLOCKCLEAR] = ButtonCode.KeyNumLock;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_KP_DIVIDE] = ButtonCode.KeyPadDivide;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_KP_MULTIPLY] = ButtonCode.KeyPadMultiply;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_KP_MINUS] = ButtonCode.KeyPadMinus;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_KP_PLUS] = ButtonCode.KeyPadPlus;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_KP_ENTER] = ButtonCode.KeyEnter;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_KP_PERIOD] = ButtonCode.KeyPadDecimal;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_APPLICATION] = ButtonCode.KeyApp;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_LCTRL] = ButtonCode.KeyLControl;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_LSHIFT] = ButtonCode.KeyLShift;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_LALT] = ButtonCode.KeyLAlt;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_LGUI] = ButtonCode.KeyLWin;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_RCTRL] = ButtonCode.KeyRControl;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_RSHIFT] = ButtonCode.KeyRShift;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_RALT] = ButtonCode.KeyRAlt;
		scantokey[(int)SDL_Scancode.SDL_SCANCODE_RGUI] = ButtonCode.KeyRWin;

		scantokey_QWERTY = (ButtonCode[])scantokey.Clone();

		ButtonCode_InitKeyTranslationTable();
		ButtonCode_UpdateScanCodeLayout();
	}

	static ButtonCode[] scantokey_QWERTY;

#if WIN32
	static readonly ButtonCode[] s_pScanToButtonCode_QWERTY = [
		ButtonCode.None, ButtonCode.KeyEscape, ButtonCode.Key1, ButtonCode.Key2, ButtonCode.Key3, ButtonCode.Key4, ButtonCode.Key5, ButtonCode.Key6,
		ButtonCode.Key7, ButtonCode.Key8, ButtonCode.Key9, ButtonCode.Key0, ButtonCode.KeyMinus, ButtonCode.KeyEqual, ButtonCode.KeyBackspace, ButtonCode.KeyTab,
		ButtonCode.KeyQ, ButtonCode.KeyW, ButtonCode.KeyE, ButtonCode.KeyR, ButtonCode.KeyT, ButtonCode.KeyY, ButtonCode.KeyU, ButtonCode.KeyI,
		ButtonCode.KeyO, ButtonCode.KeyP, ButtonCode.KeyLBracket, ButtonCode.KeyRBracket, ButtonCode.KeyEnter, ButtonCode.KeyLControl, ButtonCode.KeyA, ButtonCode.KeyS,
		ButtonCode.KeyD, ButtonCode.KeyF, ButtonCode.KeyG, ButtonCode.KeyH, ButtonCode.KeyJ, ButtonCode.KeyK, ButtonCode.KeyL, ButtonCode.KeySemicolon,
		ButtonCode.KeyApostrophe, ButtonCode.KeyBackquote, ButtonCode.KeyLShift, ButtonCode.KeyBackslash, ButtonCode.KeyZ, ButtonCode.KeyX, ButtonCode.KeyC, ButtonCode.KeyV,
		ButtonCode.KeyB, ButtonCode.KeyN, ButtonCode.KeyM, ButtonCode.KeyComma, ButtonCode.KeyPeriod, ButtonCode.KeySlash, ButtonCode.KeyRShift, ButtonCode.KeyPadMultiply,
		ButtonCode.KeyLAlt, ButtonCode.KeySpace, ButtonCode.KeyCapsLock, ButtonCode.KeyF1, ButtonCode.KeyF2, ButtonCode.KeyF3, ButtonCode.KeyF4, ButtonCode.KeyF5,
		ButtonCode.KeyF6, ButtonCode.KeyF7, ButtonCode.KeyF8, ButtonCode.KeyF9, ButtonCode.KeyF10, ButtonCode.KeyNumLock, ButtonCode.KeyScrollLock, ButtonCode.KeyHome,
		ButtonCode.KeyUp, ButtonCode.KeyPageUp, ButtonCode.KeyPadMinus, ButtonCode.KeyLeft, ButtonCode.KeyPad5, ButtonCode.KeyRight, ButtonCode.KeyPadPlus, ButtonCode.KeyEnd,
		ButtonCode.KeyDown, ButtonCode.KeyPageDown, ButtonCode.KeyInsert, ButtonCode.KeyDelete, ButtonCode.None, ButtonCode.None, ButtonCode.None, ButtonCode.KeyF11,
		ButtonCode.KeyF12, ButtonCode.KeyBreak,
	];

	static readonly SDL_Scancode[] s_pScanToSDLScancode = [
		SDL_Scancode.SDL_SCANCODE_UNKNOWN, SDL_Scancode.SDL_SCANCODE_ESCAPE, SDL_Scancode.SDL_SCANCODE_1, SDL_Scancode.SDL_SCANCODE_2, SDL_Scancode.SDL_SCANCODE_3, SDL_Scancode.SDL_SCANCODE_4, SDL_Scancode.SDL_SCANCODE_5, SDL_Scancode.SDL_SCANCODE_6,
		SDL_Scancode.SDL_SCANCODE_7, SDL_Scancode.SDL_SCANCODE_8, SDL_Scancode.SDL_SCANCODE_9, SDL_Scancode.SDL_SCANCODE_0, SDL_Scancode.SDL_SCANCODE_MINUS, SDL_Scancode.SDL_SCANCODE_EQUALS, SDL_Scancode.SDL_SCANCODE_BACKSPACE, SDL_Scancode.SDL_SCANCODE_TAB,
		SDL_Scancode.SDL_SCANCODE_Q, SDL_Scancode.SDL_SCANCODE_W, SDL_Scancode.SDL_SCANCODE_E, SDL_Scancode.SDL_SCANCODE_R, SDL_Scancode.SDL_SCANCODE_T, SDL_Scancode.SDL_SCANCODE_Y, SDL_Scancode.SDL_SCANCODE_U, SDL_Scancode.SDL_SCANCODE_I,
		SDL_Scancode.SDL_SCANCODE_O, SDL_Scancode.SDL_SCANCODE_P, SDL_Scancode.SDL_SCANCODE_LEFTBRACKET, SDL_Scancode.SDL_SCANCODE_RIGHTBRACKET, SDL_Scancode.SDL_SCANCODE_RETURN, SDL_Scancode.SDL_SCANCODE_LCTRL, SDL_Scancode.SDL_SCANCODE_A, SDL_Scancode.SDL_SCANCODE_S,
		SDL_Scancode.SDL_SCANCODE_D, SDL_Scancode.SDL_SCANCODE_F, SDL_Scancode.SDL_SCANCODE_G, SDL_Scancode.SDL_SCANCODE_H, SDL_Scancode.SDL_SCANCODE_J, SDL_Scancode.SDL_SCANCODE_K, SDL_Scancode.SDL_SCANCODE_L, SDL_Scancode.SDL_SCANCODE_SEMICOLON,
		SDL_Scancode.SDL_SCANCODE_APOSTROPHE, SDL_Scancode.SDL_SCANCODE_GRAVE, SDL_Scancode.SDL_SCANCODE_LSHIFT, SDL_Scancode.SDL_SCANCODE_BACKSLASH, SDL_Scancode.SDL_SCANCODE_Z, SDL_Scancode.SDL_SCANCODE_X, SDL_Scancode.SDL_SCANCODE_C, SDL_Scancode.SDL_SCANCODE_V,
		SDL_Scancode.SDL_SCANCODE_B, SDL_Scancode.SDL_SCANCODE_N, SDL_Scancode.SDL_SCANCODE_M, SDL_Scancode.SDL_SCANCODE_COMMA, SDL_Scancode.SDL_SCANCODE_PERIOD, SDL_Scancode.SDL_SCANCODE_SLASH, SDL_Scancode.SDL_SCANCODE_RSHIFT, SDL_Scancode.SDL_SCANCODE_KP_MULTIPLY,
		SDL_Scancode.SDL_SCANCODE_LALT, SDL_Scancode.SDL_SCANCODE_SPACE, SDL_Scancode.SDL_SCANCODE_CAPSLOCK, SDL_Scancode.SDL_SCANCODE_F1, SDL_Scancode.SDL_SCANCODE_F2, SDL_Scancode.SDL_SCANCODE_F3, SDL_Scancode.SDL_SCANCODE_F4, SDL_Scancode.SDL_SCANCODE_F5,
		SDL_Scancode.SDL_SCANCODE_F6, SDL_Scancode.SDL_SCANCODE_F7, SDL_Scancode.SDL_SCANCODE_F8, SDL_Scancode.SDL_SCANCODE_F9, SDL_Scancode.SDL_SCANCODE_F10, SDL_Scancode.SDL_SCANCODE_NUMLOCKCLEAR, SDL_Scancode.SDL_SCANCODE_SCROLLLOCK, SDL_Scancode.SDL_SCANCODE_KP_7,
		SDL_Scancode.SDL_SCANCODE_KP_8, SDL_Scancode.SDL_SCANCODE_KP_9, SDL_Scancode.SDL_SCANCODE_KP_MINUS, SDL_Scancode.SDL_SCANCODE_KP_4, SDL_Scancode.SDL_SCANCODE_KP_5, SDL_Scancode.SDL_SCANCODE_KP_6, SDL_Scancode.SDL_SCANCODE_KP_PLUS, SDL_Scancode.SDL_SCANCODE_KP_1,
		SDL_Scancode.SDL_SCANCODE_KP_2, SDL_Scancode.SDL_SCANCODE_KP_3, SDL_Scancode.SDL_SCANCODE_KP_0, SDL_Scancode.SDL_SCANCODE_KP_PERIOD, SDL_Scancode.SDL_SCANCODE_UNKNOWN, SDL_Scancode.SDL_SCANCODE_UNKNOWN, SDL_Scancode.SDL_SCANCODE_NONUSBACKSLASH, SDL_Scancode.SDL_SCANCODE_F11,
		SDL_Scancode.SDL_SCANCODE_F12,
	];

	[DllImport("user32.dll")] static extern nint GetKeyboardLayout(uint idThread);
	[DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int nBuff, [Out] nint[] lpList);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint LoadKeyboardLayoutW(string pwszKLID, uint flags);
	[DllImport("user32.dll")] static extern bool UnloadKeyboardLayout(nint hkl);
	[DllImport("user32.dll")] static extern uint MapVirtualKeyExW(uint uCode, uint uMapType, nint dwhkl);
#endif

	static void ButtonCode_UpdateScanCodeLayout() {
		Array.Copy(scantokey_QWERTY, scantokey, scantokey.Length);

#if WIN32
		nint[] layoutList = new nint[128];
		int layoutCount = GetKeyboardLayoutList(layoutList.Length, layoutList);
		nint currentKb = GetKeyboardLayout(0);
		nint englishKb = LoadKeyboardLayoutW("00000409", 0);

		if (englishKb != 0 && englishKb != currentKb) {
			for (uint i = 0; i < 128; i++) {
				ButtonCode code = i < s_pScanToButtonCode_QWERTY.Length ? s_pScanToButtonCode_QWERTY[i] : ButtonCode.None;

				if (code != ButtonCode.None && code != ButtonCode.KeyBackquote && code >= ButtonCode.Key0 && code <= ButtonCode.KeyEqual) {
					uint vk = MapVirtualKeyExW(i, 1, englishKb);
					uint newScanCode = MapVirtualKeyExW(vk, 0, currentKb) & 0x0000007F;

					if (newScanCode < s_pScanToSDLScancode.Length) {
						SDL_Scancode sdlScanCode = s_pScanToSDLScancode[newScanCode];
						if (sdlScanCode != SDL_Scancode.SDL_SCANCODE_UNKNOWN)
							scantokey[(int)sdlScanCode] = code;
					}
				}
			}

			if (Array.IndexOf(layoutList, englishKb, 0, Math.Max(layoutCount, 0)) < 0)
				UnloadKeyboardLayout(englishKb);
		}
#endif

		scantokey[(int)SDL_Scancode.SDL_SCANCODE_UNKNOWN] = ButtonCode.None;
	}

	[ConCommand(helpText: "Updates game keyboard layout to current windows keyboard setting.")]
	static void key_updatelayout() => ButtonCode_UpdateScanCodeLayout();

	static ButtonCode[] virtualKeyToButtonCode = null!;
	static int[] buttonCodeToVirtual = null!;

	const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11,
		VK_MENU = 0x12, VK_PAUSE = 0x13, VK_CAPITAL = 0x14, VK_ESCAPE = 0x1B, VK_SPACE = 0x20,
		VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24, VK_LEFT = 0x25, VK_UP = 0x26,
		VK_RIGHT = 0x27, VK_DOWN = 0x28, VK_INSERT = 0x2D, VK_DELETE = 0x2E, VK_LWIN = 0x5B,
		VK_RWIN = 0x5C, VK_APPS = 0x5D, VK_NUMPAD0 = 0x60, VK_MULTIPLY = 0x6A, VK_ADD = 0x6B,
		VK_SUBTRACT = 0x6D, VK_DECIMAL = 0x6E, VK_DIVIDE = 0x6F, VK_F1 = 0x70, VK_NUMLOCK = 0x90,
		VK_SCROLL = 0x91;

	static void ButtonCode_InitKeyTranslationTable() {
		virtualKeyToButtonCode = new ButtonCode[256];
		ButtonCode[] vk = virtualKeyToButtonCode;

		for (int i = 0; i <= 9; i++)
			vk['0' + i] = ButtonCode.Key0 + i;
		for (int i = 0; i < 26; i++)
			vk['A' + i] = ButtonCode.KeyA + i;

		for (int i = 0; i <= 9; i++)
			vk[VK_NUMPAD0 + i] = ButtonCode.KeyPad0 + i;
		vk[VK_DIVIDE] = ButtonCode.KeyPadDivide;
		vk[VK_MULTIPLY] = ButtonCode.KeyPadMultiply;
		vk[VK_SUBTRACT] = ButtonCode.KeyPadMinus;
		vk[VK_ADD] = ButtonCode.KeyPadPlus;
		vk[VK_DECIMAL] = ButtonCode.KeyPadDecimal;

		vk[0xdb] = ButtonCode.KeyLBracket;
		vk[0xdd] = ButtonCode.KeyRBracket;
		vk[0xba] = ButtonCode.KeySemicolon;
		vk[0xde] = ButtonCode.KeyApostrophe;
		vk[0xc0] = ButtonCode.KeyBackquote;
		vk[0xbc] = ButtonCode.KeyComma;
		vk[0xbe] = ButtonCode.KeyPeriod;
		vk[0xbf] = ButtonCode.KeySlash;
		vk[0xdc] = ButtonCode.KeyBackslash;
		vk[0xbd] = ButtonCode.KeyMinus;
		vk[0xbb] = ButtonCode.KeyEqual;

		vk[VK_RETURN] = ButtonCode.KeyEnter;
		vk[VK_SPACE] = ButtonCode.KeySpace;
		vk[VK_BACK] = ButtonCode.KeyBackspace;
		vk[VK_TAB] = ButtonCode.KeyTab;
		vk[VK_CAPITAL] = ButtonCode.KeyCapsLock;
		vk[VK_NUMLOCK] = ButtonCode.KeyNumLock;
		vk[VK_ESCAPE] = ButtonCode.KeyEscape;
		vk[VK_SCROLL] = ButtonCode.KeyScrollLock;
		vk[VK_INSERT] = ButtonCode.KeyInsert;
		vk[VK_DELETE] = ButtonCode.KeyDelete;
		vk[VK_HOME] = ButtonCode.KeyHome;
		vk[VK_END] = ButtonCode.KeyEnd;
		vk[VK_PRIOR] = ButtonCode.KeyPageUp;
		vk[VK_NEXT] = ButtonCode.KeyPageDown;
		vk[VK_PAUSE] = ButtonCode.KeyBreak;
		vk[VK_SHIFT] = ButtonCode.KeyLShift;
		vk[VK_MENU] = ButtonCode.KeyLAlt;
		vk[VK_CONTROL] = ButtonCode.KeyLControl;
		vk[VK_LWIN] = ButtonCode.KeyLWin;
		vk[VK_RWIN] = ButtonCode.KeyRWin;
		vk[VK_APPS] = ButtonCode.KeyApp;
		vk[VK_UP] = ButtonCode.KeyUp;
		vk[VK_LEFT] = ButtonCode.KeyLeft;
		vk[VK_DOWN] = ButtonCode.KeyDown;
		vk[VK_RIGHT] = ButtonCode.KeyRight;
		for (int i = 0; i < 12; i++)
			vk[VK_F1 + i] = ButtonCode.KeyF1 + i;

		buttonCodeToVirtual = new int[(int)ButtonCode.Count];
		for (int i = 0; i < virtualKeyToButtonCode.Length; i++)
			buttonCodeToVirtual[(int)virtualKeyToButtonCode[i]] = i;
		buttonCodeToVirtual[0] = 0;
	}


#if OSX
	static readonly ButtonCode[] s_pCocoaKeyToButtonCode = [
		ButtonCode.KeyA, ButtonCode.KeyS, ButtonCode.KeyD, ButtonCode.KeyF, ButtonCode.KeyH, ButtonCode.KeyG, ButtonCode.KeyZ, ButtonCode.KeyX,
		ButtonCode.KeyC, ButtonCode.KeyV, ButtonCode.KeyBackquote, ButtonCode.KeyB, ButtonCode.KeyQ, ButtonCode.KeyW, ButtonCode.KeyE, ButtonCode.KeyR,
		ButtonCode.KeyY, ButtonCode.KeyT, ButtonCode.Key1, ButtonCode.Key2, ButtonCode.Key3, ButtonCode.Key4, ButtonCode.Key6, ButtonCode.Key5,
		ButtonCode.KeyEqual, ButtonCode.Key9, ButtonCode.Key7, ButtonCode.KeyMinus, ButtonCode.Key8, ButtonCode.Key0, ButtonCode.KeyRBracket, ButtonCode.KeyO,
		ButtonCode.KeyU, ButtonCode.KeyLBracket, ButtonCode.KeyI, ButtonCode.KeyP, ButtonCode.KeyEnter, ButtonCode.KeyL, ButtonCode.KeyJ, ButtonCode.KeyApostrophe,
		ButtonCode.KeyK, ButtonCode.KeySemicolon, ButtonCode.KeyBackslash, ButtonCode.KeyComma, ButtonCode.KeySlash, ButtonCode.KeyN, ButtonCode.KeyM, ButtonCode.KeyPeriod,
		ButtonCode.KeyTab, ButtonCode.KeySpace, ButtonCode.KeyBackquote, ButtonCode.KeyBackspace, ButtonCode.None, ButtonCode.KeyEscape, ButtonCode.KeyRWin, ButtonCode.KeyLWin,
		ButtonCode.KeyLShift, ButtonCode.KeyCapsLock, ButtonCode.KeyLAlt, ButtonCode.KeyLControl, ButtonCode.KeyLShift, ButtonCode.None, ButtonCode.KeyRControl, ButtonCode.None,
		ButtonCode.None, ButtonCode.KeyPadDecimal, ButtonCode.None, ButtonCode.KeyPadMultiply, ButtonCode.None, ButtonCode.KeyPadPlus, ButtonCode.None, ButtonCode.KeyNumLock,
		ButtonCode.None, ButtonCode.None, ButtonCode.None, ButtonCode.KeyPadDivide, ButtonCode.KeyPadEnter, ButtonCode.None, ButtonCode.KeyPadMinus, ButtonCode.None,
		ButtonCode.None, ButtonCode.KeyPadDivide, ButtonCode.KeyPad0, ButtonCode.KeyPad1, ButtonCode.KeyPad2, ButtonCode.KeyPad3, ButtonCode.KeyPad4, ButtonCode.KeyPad5,
		ButtonCode.KeyPad6, ButtonCode.KeyPad7, ButtonCode.None, ButtonCode.KeyPad8, ButtonCode.KeyPad9, ButtonCode.None, ButtonCode.None, ButtonCode.None,
		ButtonCode.KeyF5, ButtonCode.KeyF6, ButtonCode.KeyF7, ButtonCode.KeyF3, ButtonCode.KeyF8, ButtonCode.KeyF9, ButtonCode.None, ButtonCode.KeyF11,
		ButtonCode.None, ButtonCode.None, ButtonCode.None, ButtonCode.None, ButtonCode.None, ButtonCode.KeyF10, ButtonCode.KeyApp, ButtonCode.KeyF12,
		ButtonCode.None, ButtonCode.None, ButtonCode.KeyInsert, ButtonCode.KeyHome, ButtonCode.KeyPageUp, ButtonCode.KeyDelete, ButtonCode.KeyF4, ButtonCode.KeyEnd,
		ButtonCode.KeyF2, ButtonCode.KeyPageDown, ButtonCode.KeyF1, ButtonCode.KeyLeft, ButtonCode.KeyRight, ButtonCode.KeyDown, ButtonCode.KeyUp, ButtonCode.None,
	];
#endif

	static bool MapVirtualKeyToButtonCode(int virtualKeyCode, out ButtonCode pOut) {
		if (virtualKeyCode < 0)
			pOut = (ButtonCode)(-1 * virtualKeyCode);
		else {
#if OSX
			if (virtualKeyCode >= s_pCocoaKeyToButtonCode.Length) {
				pOut = ButtonCode.None;
				return false;
			}
			pOut = s_pCocoaKeyToButtonCode[virtualKeyCode];
#else
			virtualKeyCode &= 0x000000ff;
			pOut = (ButtonCode)scantokey[virtualKeyCode];
#endif
		}

		return true;
	}

	static ButtonCode WindowEventVirtualCode(WindowEvent ev, ButtonCode scanCode) {
#if WIN32
		if (ev.WindowsVirtualKey > 0)
			return virtualKeyToButtonCode[ev.WindowsVirtualKey];
#endif
		return scanCode;
	}

	public void PostButtonPressedEvent(InputEventType type, int tick, ButtonCode scanCode, ButtonCode virtualCode) {
		InputState state = InputState;

		if (!state.ButtonState.IsBitSet((int)scanCode)) {
			state.ButtonState.Set((int)scanCode);
			state.ButtonPressedTick[(int)scanCode] = tick;

			PostEvent(type, tick, (int)scanCode, (int)virtualCode, 0);
		}
	}

	public void PostButtonReleasedEvent(InputEventType type, int tick, ButtonCode scanCode, ButtonCode virtualCode) {
		InputState state = InputState;

		if (state.ButtonState.IsBitSet((int)scanCode)) {
			state.ButtonState.Clear((int)scanCode);
			state.ButtonReleasedTick[(int)scanCode] = tick;

			PostEvent(type, tick, (int)scanCode, (int)virtualCode, 0);
		}
	}

	private enum InputStateType
	{
		Queued,
		Current,
		Count
	}

	WindowEvent[] eventBuffer = new WindowEvent[32];
	public void CopyInputState(InputState dest, InputState src, bool copyEvents) {
		dest.Events.Clear();
		dest.Dirty = false;
		if (src.Dirty) {
			dest.ButtonState = src.ButtonState;
			Array.ConstrainedCopy(src.ButtonPressedTick, 0, dest.ButtonPressedTick, 0, dest.ButtonPressedTick.Length);
			Array.ConstrainedCopy(src.ButtonReleasedTick, 0, dest.ButtonReleasedTick, 0, dest.ButtonReleasedTick.Length);
			Array.ConstrainedCopy(src.AnalogDelta, 0, dest.AnalogDelta, 0, dest.AnalogDelta.Length);
			Array.ConstrainedCopy(src.AnalogValue, 0, dest.AnalogValue, 0, dest.AnalogValue.Length);
			if (copyEvents) {
				if (src.Events.Count > 0) {
					dest.Events.Clear();
					dest.Events.EnsureCapacity(src.Events.Count);
					foreach (var queued in src.Events) dest.Events.Enqueue(queued);
				}
			}
		}
	}
	public void PollInputState() {
		IsPolling = true;
		PollCount++;

		InputState queuedState = QueuedInputState;
		CopyInputState(CurrentInputState, queuedState, true);
		SampleDevices();

		LastPollTick = LastSampleTick;

		PollInputState_Platform();
		CopyInputState(queuedState, CurrentInputState, false);
		IsPolling = false;
	}

	private void SampleDevices() {
		LastSampleTick = ComputeSampleTick();
	}

	private int ComputeSampleTick() {
		int sampleTick;

		uint nCurrentTick = Convert.ToUInt32(Platform.MSTime);
		sampleTick = (int)nCurrentTick;
		return sampleTick;
	}

	public void PollInputState_Platform() {
		InputState state = InputState;

		while (true) {
			int events = launcherMgr?.GetEvents(eventBuffer, eventBuffer.Length) ?? 0;
			if (events == 0) break;
			for (int i = 0; i < events; i++) {
				WindowEvent ev = eventBuffer[i];
				switch (ev.EventType) {
					case WindowEventType.AppActivate: {
							InputEvent newEv = new();
							newEv.Type = InputEventType.App_AppActivated;
							newEv.Data = ev.WasWindowFocused ? 1 : 0;

							PostUserEvent(in newEv);
							if (!ev.WasWindowFocused) {
								ResetInputState();
							}
						}
						break;
					case WindowEventType.KeyDown: {
							if (MapVirtualKeyToButtonCode(ev.VirtualKeyCode, out ButtonCode scancode)) {
								ButtonCode virtualCode = WindowEventVirtualCode(ev, scancode);
								if (scancode != ButtonCode.None)
									PostButtonPressedEvent(InputEventType.IE_ButtonPressed, LastSampleTick, scancode, virtualCode);

								InputEvent newEv = new() {
									Tick = GetPollTick(),
									Type = InputEventType.Gui_KeyCodeTyped,
									Data = (int)virtualCode
								};
								PostUserEvent(newEv);
							}

							if (0 == (ev.ModifierKeyMask & KeyModifier.Command) && ev.VirtualKeyCode >= 0 && ev.UTF8Key > 0) {
								InputEvent newEv = new() {
									Tick = GetPollTick(),
									Type = InputEventType.Gui_KeyTyped,
									Data = ev.UTF8Key
								};
								PostUserEvent(newEv);
							}
						}
						break;

					case WindowEventType.KeyUp: {
							if (MapVirtualKeyToButtonCode(ev.VirtualKeyCode, out ButtonCode scancode)) {
								ButtonCode virtualCode = WindowEventVirtualCode(ev, scancode);
								if (scancode != ButtonCode.None)
									PostButtonReleasedEvent(InputEventType.IE_ButtonReleased, LastSampleTick, scancode, virtualCode);
							}
						}
						break;
					case WindowEventType.MouseButtonDown: {
							UpdateMouseButtonState(ev.MouseButtonFlags, ev.MouseClickCount > 1 ? ev.MouseButton switch {
								MouseButton.Right => ButtonCode.MouseRight,
								MouseButton.Middle => ButtonCode.MouseMiddle,
								MouseButton.Button4 => ButtonCode.Mouse4,
								MouseButton.Button5 => ButtonCode.Mouse5,
								MouseButton.Left or _ => ButtonCode.MouseLeft,
							} : ButtonCode.Invalid);
						}
						break;
					case WindowEventType.MouseButtonUp:
						UpdateMouseButtonState(ev.MouseButtonFlags);
						break;

					case WindowEventType.MouseMove:
						UpdateMousePositionState(InputState, (short)ev.MousePos[0], (short)ev.MousePos[1]);

						InputEvent newEvent = new();
						newEvent.Tick = GetPollTick();
						newEvent.Type = InputEventType.Gui_LocateMouseClick;
						newEvent.Data = (short)ev.MousePos[0];
						newEvent.Data2 = (short)ev.MousePos[1];
						PostUserEvent(newEvent);

						break;
					case WindowEventType.MouseScroll:
						ButtonCode code = (short)ev.MousePos[1] > 0 ? ButtonCode.MouseWheelUp : ButtonCode.MouseWheelDown;
						state.ButtonPressedTick[(int)code] = state.ButtonReleasedTick[(int)code] = LastSampleTick;
						PostEvent(InputEventType.IE_ButtonPressed, LastSampleTick, (int)code, (int)code);
						PostEvent(InputEventType.IE_ButtonReleased, LastSampleTick, (int)code, (int)code);

						state.AnalogDelta[(int)AnalogCode.MouseWheel] = (int)ev.MousePos[1];
						state.AnalogValue[(int)AnalogCode.MouseWheel] += state.AnalogDelta[(int)AnalogCode.MouseWheel];
						PostEvent(InputEventType.IE_AnalogValueChanged, LastSampleTick, (int)AnalogCode.MouseWheel, state.AnalogValue[(int)AnalogCode.MouseWheel], state.AnalogDelta[(int)AnalogCode.MouseWheel]);
						break;

					case WindowEventType.AppQuit:
						PostEvent(InputEventType.System_Quit, LastSampleTick, 0, 0, 0);
						break;
				}
			}
		}
	}

	private void ResetInputState() {
		ReleaseAllButtons();
		ZeroAnalogState(0, (int)AnalogCode.Last - 1);
	}

	private void ZeroAnalogState(int firstState, int lastState) {
		InputState state = InputState;
		memset(state.AnalogDelta.AsSpan()[firstState..lastState], 0);
		memset(state.AnalogValue.AsSpan()[firstState..lastState], 0);
	}

	private void ReleaseAllButtons(int firstButton = 0, int lastButton = (int)ButtonCode.Last - 1) {
		for (int i = firstButton; i <= lastButton; ++i)
			PostButtonReleasedEvent(InputEventType.IE_ButtonReleased, LastSampleTick, (ButtonCode)i, (ButtonCode)i);
	}

	private void UpdateMousePositionState(InputState state, short x, short y) {
		int nOldX = state.AnalogValue[(int)AnalogCode.MouseX];
		int nOldY = state.AnalogValue[(int)AnalogCode.MouseY];

		state.AnalogValue[(int)AnalogCode.MouseX] = x;
		state.AnalogValue[(int)AnalogCode.MouseY] = y;

		int nDeltaX = x - nOldX;
		int nDeltaY = y - nOldY;

		state.AnalogDelta[(int)AnalogCode.MouseX] = nDeltaX;
		state.AnalogDelta[(int)AnalogCode.MouseY] = nDeltaY;

		if (nDeltaX != 0)
			PostEvent(InputEventType.IE_AnalogValueChanged, LastSampleTick, (int)AnalogCode.MouseX, x, nDeltaX);

		if (nDeltaY != 0)
			PostEvent(InputEventType.IE_AnalogValueChanged, LastSampleTick, (int)AnalogCode.MouseY, y, nDeltaY);

		if (nDeltaX != 0 || nDeltaY != 0)
			PostEvent(InputEventType.IE_AnalogValueChanged, LastSampleTick, (int)AnalogCode.MouseXY, x, y);
	}

	private void UpdateMouseButtonState(MouseButton buttonMask, ButtonCode dblClickCode = ButtonCode.Invalid) {
		for (int i = 0; i < 5; ++i) {
			ButtonCode code = ButtonCode.MouseFirst + i;
			bool down = ((int)buttonMask & (1 << i)) != 0;
			if (down) {
				InputEventType type = code != dblClickCode ? InputEventType.IE_ButtonPressed : InputEventType.IE_ButtonDoubleClicked;
				PostButtonPressedEvent(type, LastSampleTick, code, code);
			}
			else
				PostButtonReleasedEvent(InputEventType.IE_ButtonReleased, LastSampleTick, code, code);
		}
	}

	public void PostEvent(InputEventType type, int tick, int data = 0, int data2 = 0, int data3 = 0) {
		InputEvent ev = new();
		ev.Type = type;
		ev.Tick = tick;
		ev.Data = data;
		ev.Data2 = data2;
		ev.Data3 = data3;
		PostUserEvent(ev);
	}

	public void PostUserEvent(in InputEvent ev) {
		InputState state = InputState;
		state.Events.Enqueue(ev);
		state.Dirty = true;
	}

	public ButtonCode ScanCodeToButtonCode(int param) {
		throw new NotImplementedException();
	}

	public void SetConsoleTextMode(bool consoleTextMode) {
		throw new NotImplementedException();
	}

	public unsafe void GetCursorPosition(out int x, out int y) {
		float fx, fy;
		SDL3.SDL_GetMouseState(&fx, &fy);
		x = (int)fx;
		y = (int)fy;
	}

	public unsafe void SetCursorPosition(int x, int y) => SDL3.SDL_WarpMouseInWindow((SDL_Window*)Window!.GetHandle(), x, y);

	public ButtonCode VirtualKeyToButtonCode(int virtualKey) {
		return virtualKeyToButtonCode[virtualKey];
	}

	static string[] ButtonCodeName ={
		"",
		"0",
		"1",
		"2",
		"3",
		"4",
		"5",
		"6",
		"7",
		"8",
		"9",
		"a",
		"b",
		"c",
		"d",
		"e",
		"f",
		"g",
		"h",
		"i",
		"j",
		"k",
		"l",
		"m",
		"n",
		"o",
		"p",
		"q",
		"r",
		"s",
		"t",
		"u",
		"v",
		"w",
		"x",
		"y",
		"z",
		"KP_INS",
		"KP_END",
		"KP_DOWNARROW",
		"KP_PGDN",
		"KP_LEFTARROW",
		"KP_5",
		"KP_RIGHTARROW",
		"KP_HOME",
		"KP_UPARROW",
		"KP_PGUP",
		"KP_SLASH",
		"KP_MULTIPLY",
		"KP_MINUS",
		"KP_PLUS",
		"KP_ENTER",
		"KP_DEL",
		"[",
		"]",
		"SEMICOLON",
		"'",
		"`",
		",",
		".",
		"/",
		"\\",
		"-",
		"=",
		"ENTER",
		"SPACE",
		"BACKSPACE",
		"TAB",
		"CAPSLOCK",
		"NUMLOCK",
		"ESCAPE",
		"SCROLLLOCK",
		"INS",
		"DEL",
		"HOME",
		"END",
		"PGUP",
		"PGDN",
		"PAUSE",
		"SHIFT",
		"RSHIFT",
		"ALT",
		"RALT",
		"CTRL",
		"RCTRL",
		"LWIN",
		"RWIN",
		"APP",
		"UPARROW",
		"LEFTARROW",
		"DOWNARROW",
		"RIGHTARROW",
		"F1",
		"F2",
		"F3",
		"F4",
		"F5",
		"F6",
		"F7",
		"F8",
		"F9",
		"F10",
		"F11",
		"F12",

		"CAPSLOCKTOGGLE",
		"NUMLOCKTOGGLE",
		"SCROLLLOCKTOGGLE",

		"MOUSE1",
		"MOUSE2",
		"MOUSE3",
		"MOUSE4",
		"MOUSE5",

		"MWHEELUP",
		"MWHEELDOWN",

		"JOY1",
		"JOY2",
		"JOY3",
		"JOY4",
		"JOY5",
		"JOY6",
		"JOY7",
		"JOY8",
		"JOY9",
		"JOY10",
		"JOY11",
		"JOY12",
		"JOY13",
		"JOY14",
		"JOY15",
		"JOY16",
		"JOY17",
		"JOY18",
		"JOY19",
		"JOY20",
		"JOY21",
		"JOY22",
		"JOY23",
		"JOY24",
		"JOY25",
		"JOY26",
		"JOY27",
		"JOY28",
		"JOY29",
		"JOY30",
		"JOY31",
		"JOY32",
		"POV_UP",
		"POV_RIGHT",
		"POV_DOWN",
		"POV_LEFT",
		"X AXIS POS",
		"X AXIS NEG",
		"Y AXIS POS",
		"Y AXIS NEG",
		"Z AXIS POS",
		"Z AXIS NEG",
		"R AXIS POS",
		"R AXIS NEG",
		"U AXIS POS",
		"U AXIS NEG",
		"V AXIS POS",
		"V AXIS NEG",
		"FALCON_NULL",
		"FALCON_1",
		"FALCON_2",
		"FALCON_3",
		"FALCON_4",
		"FALCON2_1",
		"FALCON2_2",
		"FALCON2_3",
		"FALCON2_4",
	};

	public ButtonCode StringToButtonCode(ReadOnlySpan<char> str) {
		if (str.IsEmpty || str.Length <= 0)
			return ButtonCode.Invalid;

		for (ButtonCode i = 0; i < ButtonCode.Last; ++i)
			if (str.Equals(ButtonCodeName[(int)i], StringComparison.OrdinalIgnoreCase))
				return i;

		return ButtonCode.Invalid;
	}

	public ReadOnlySpan<char> ButtonCodeToString(ButtonCode code) {
		return ButtonCodeName[(int)code];
	}
}
