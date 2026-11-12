using SDL;

using Source.Common.Input;
using Source.Common.Launcher;

using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.SDLManager;

public unsafe class SDL3_Window : IWindow
{
	private SDL_Window* window;
	internal SDL_Window* HardwareHandle => window;
	nint IWindow.GetHandle() => (nint)window;

	public int ID => (int)SDL3.SDL_GetWindowID(window);

	public bool CursorVisible = true;
	public SDL_Cursor* CursorHandle;
	public bool SetMouseCursorCalled;

	public bool HasFocus;
	public bool FullScreen;
	public bool SizeWindowFullScreenState;
	public bool ForbidMouseGrab;
	public bool ShownAndRaised;

	public bool ExpectSyntheticMouseMotion = true;
	public int MouseTargetX;
	public int MouseTargetY;
	public int MouseXDelta;
	public int MouseYDelta;
	public int WarpDelta;
	public bool RawInput;

	public KeyModifier KeyModifierMask;

	public GranularKeyModifier KeyModifiers;
	public MouseButton MouseButtons;

	public float MouseXScale = 1.0f;
	public float MouseYScale = 1.0f;

	public int GetEvents(WindowEvent[] buffer, int maxEvents) {
		if (maxEvents == 0) return 0;

		int c = 0;
		while (Events.TryDequeue(out WindowEvent? ev)) {
			buffer[c++] = ev;
			if (c >= maxEvents) break;
		}

		return c;
	}

	public bool IsValid() => window != null;

	private readonly IServiceProvider systems;
	public SDL3_Window(IServiceProvider systems) {
		this.systems = systems;
	}

	public Vector2 Position {
		get {
			int x, y;
			SDL3.SDL_GetWindowPosition(window, &x, &y);
			return new Vector2(x, y);
		}
		set {
			SDL3.SDL_SetWindowPosition(window, (int)value.X, (int)value.Y);
		}
	}

	public int Width {
		get {
			int x, y;
			SDL3.SDL_GetWindowSize(window, &x, &y);
			return x;
		}
	}

	public int Height {
		get {
			int x, y;
			SDL3.SDL_GetWindowSize(window, &x, &y);
			return y;
		}
	}

	public Vector2 Size {
		get {
			int x, y;
			SDL3.SDL_GetWindowSize(window, &x, &y);
			return new Vector2(x, y);
		}
		set {
			SDL3.SDL_SetWindowSize(window, (int)value.X, (int)value.Y);
		}
	}
	public string Title {
		get => SDL3.SDL_GetWindowTitle(window) ?? "";
		set => SDL3.SDL_SetWindowTitle(window, value);
	}

	public bool Visible {
		get => 0 != (SDL3.SDL_GetWindowFlags(window) & SDL_WindowFlags.SDL_WINDOW_HIDDEN) == false;
		set {
			if (value) SDL3.SDL_ShowWindow(window);
			else SDL3.SDL_HideWindow(window);
		}
	}

	static readonly WindowEventType[] KEY_DOWN_THEN_UP = [WindowEventType.KeyDown, WindowEventType.KeyUp];

	public void PumpMessages() {
		SDL_Event ev;
		int eventsProcessed = 0;
		while (SDL3.SDL_PollEvent(&ev) && eventsProcessed < 100) {
			eventsProcessed += 1;

			switch (ev.Type) {
				case SDL_EventType.SDL_EVENT_MOUSE_MOTION: {
						if (!HasFocus) break;

						if (ExpectSyntheticMouseMotion && ev.motion.x == MouseTargetX && ev.motion.y == MouseTargetY) {
							ExpectSyntheticMouseMotion = false;
							break;
						}

						if (SDL3.SDL_GetWindowRelativeMouseMode(window)) {
							MouseXDelta += (int)ev.motion.xrel;
							MouseYDelta += (int)ev.motion.yrel;
						}
						// Msg($"MouseXDelta = {ev.motion.xrel}, MouseYDelta = {ev.motion.yrel}\n");

						if (!RawInput && !CursorVisible && (
							ev.motion.x < MouseTargetX - WarpDelta ||
							 ev.motion.x > MouseTargetX + WarpDelta ||
							 ev.motion.y < MouseTargetY - WarpDelta ||
							 ev.motion.y > MouseTargetY + WarpDelta)) {
							SDL3.SDL_WarpMouseInWindow(window, MouseTargetX, MouseTargetY);
							ExpectSyntheticMouseMotion = true;
						}

						WindowEvent newEvent = new();
						newEvent.EventType = WindowEventType.MouseMove;
						newEvent.MousePos = new((int)(ev.motion.x * MouseXScale), (int)(ev.motion.y * MouseYScale));
						newEvent.MouseButtonFlags = MouseButtons;
						PostEvent(newEvent);
					}
					break;
				case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_UP:
				case SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN: {
						int button = ev.button.button switch {
							1 => 1,
							2 => 3,
							3 => 2,
							_ => 4 + (ev.button.button & 0x1)
						};

						bool pressed = ev.Type == SDL_EventType.SDL_EVENT_MOUSE_BUTTON_DOWN;
						MouseButton evButton = (MouseButton)(1 << (button - 1));
						if (pressed) MouseButtons |= evButton;
						else MouseButtons &= ~evButton;

						bool doublePress = false;

						WindowEvent newEvent = new();
						newEvent.EventType = pressed ? WindowEventType.MouseButtonDown : WindowEventType.MouseButtonUp;
						newEvent.MousePos = new((int)(ev.button.x * MouseXScale), (int)(ev.button.y * MouseYScale));
						newEvent.MouseButtonFlags = MouseButtons;
						newEvent.MouseClickCount = (uint)(doublePress ? 2 : 1);
						newEvent.MouseButton = evButton;
						PostEvent(newEvent);
					}
					break;
				case SDL_EventType.SDL_EVENT_MOUSE_WHEEL: {
						WindowEvent newEvent = new();
						newEvent.EventType = WindowEventType.MouseScroll;
						newEvent.MousePos = new Vector2((int)ev.wheel.x, (int)ev.wheel.y);
						PostEvent(newEvent);
					}
					break;
				// Window events.

				case SDL_EventType.SDL_EVENT_WINDOW_EXPOSED: break;
				case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_GAINED:
					HasFocus = true;
					if (CursorVisible) SDL3.SDL_ShowCursor(); else SDL3.SDL_HideCursor();
					PostEvent(new() {
						EventType = WindowEventType.AppActivate,
						WasWindowFocused = true
					});

					break;
				case SDL_EventType.SDL_EVENT_WINDOW_FOCUS_LOST:
					HasFocus = false;
					SDL3.SDL_ShowCursor();
					PostEvent(new() {
						EventType = WindowEventType.AppActivate,
						WasWindowFocused = false
					});
					break;


				case SDL_EventType.SDL_EVENT_WINDOW_MOUSE_LEAVE:
					if (!OperatingSystem.IsWindows() && !RawInput && !CursorVisible && HasFocus) {
						SDL3.SDL_WarpMouseInWindow(window, MouseTargetX, MouseTargetY);
						ExpectSyntheticMouseMotion = true;
					}
					break;

				case SDL_EventType.SDL_EVENT_KEY_UP:
					HandleKeyEvent(ref ev);
					break;
				case SDL_EventType.SDL_EVENT_KEY_DOWN:
					HandleKeyEvent(ref ev);
					break;
				case SDL_EventType.SDL_EVENT_TEXT_INPUT:
					string? text = ev.text.GetText();
					if (!string.IsNullOrEmpty(text)) {
						for (int i = 0; i < text.Length; i++) {
							char ch = text[i];
							foreach (var type in KEY_DOWN_THEN_UP) {
								PostEvent(new() {
									EventType = type,
									VirtualKeyCode = 0,
									UTF8Key = ch,
									UTF8KeyUnmodified = ch,
									ModifierKeyMask = KeyModifierMask
								});
							}
						}
					}
					break;

				case SDL_EventType.SDL_EVENT_QUIT: {
						WindowEvent newEvent = new();
						newEvent.EventType = WindowEventType.AppQuit;
						PostEvent(newEvent);
					}
					break;
			}
		}
	}
	private void KeySymCase(WindowEvent ev, GranularKeyModifier gran, bool op, ButtonCode key) {
		if (op)
			KeyModifiers |= gran;
		else
			KeyModifiers &= ~gran;

		ev.VirtualKeyCode = -(int)key;
	}
	private void HandleKeyEvent(ref SDL_Event ev) {
		bool pressed = ev.Type == SDL_EventType.SDL_EVENT_KEY_DOWN;
		WindowEvent newEvent = new WindowEvent();
		newEvent.EventType = pressed ? WindowEventType.KeyDown : WindowEventType.KeyUp;
		newEvent.UTF8Key = '\0';
		newEvent.UTF8KeyUnmodified = '\0';
#if OSX
		if (!HandleCocoaKeyEvent(ref ev, newEvent, pressed))
			return;
#else
		newEvent.VirtualKeyCode = (int)ev.key.scancode;
#if WIN32
		newEvent.WindowsVirtualKey = WindowsVirtualKeys[WindowsScanCodeIndex(ev.key.raw)];
#endif

		switch (ev.key.key) {
			case SDL_Keycode.SDLK_CAPSLOCK: KeySymCase(newEvent, GranularKeyModifier.CapsLock, pressed, ButtonCode.KeyCapsLock); break;

			case SDL_Keycode.SDLK_LSHIFT: KeySymCase(newEvent, GranularKeyModifier.ShiftL, pressed, ButtonCode.KeyLShift); break;
			case SDL_Keycode.SDLK_RSHIFT: KeySymCase(newEvent, GranularKeyModifier.ShiftR, pressed, ButtonCode.KeyRShift); break;

			case SDL_Keycode.SDLK_LCTRL: KeySymCase(newEvent, GranularKeyModifier.ControlL, pressed, ButtonCode.KeyLControl); break;
			case SDL_Keycode.SDLK_RCTRL: KeySymCase(newEvent, GranularKeyModifier.ControlR, pressed, ButtonCode.KeyRControl); break;

			case SDL_Keycode.SDLK_LGUI: KeySymCase(newEvent, GranularKeyModifier.CommandL, pressed, ButtonCode.KeyLWin); break;
			case SDL_Keycode.SDLK_RGUI: KeySymCase(newEvent, GranularKeyModifier.CommandR, pressed, ButtonCode.KeyRWin); break;

			case SDL_Keycode.SDLK_LALT: KeySymCase(newEvent, GranularKeyModifier.AltL, pressed, ButtonCode.KeyLAlt); break;
			case SDL_Keycode.SDLK_RALT: KeySymCase(newEvent, GranularKeyModifier.AltR, pressed, ButtonCode.KeyRAlt); break;
		}
#endif

		KeyModifierMask = 0;

		if (0 != (KeyModifiers & GranularKeyModifier.CapsLock))
			KeyModifierMask |= KeyModifier.CapsLock;
		if (0 != (KeyModifiers & GranularKeyModifier.ShiftL) || 0 != (KeyModifiers & GranularKeyModifier.ShiftR))
			KeyModifierMask |= KeyModifier.Shift;
		if (0 != (KeyModifiers & GranularKeyModifier.ControlL) || 0 != (KeyModifiers & GranularKeyModifier.ControlR))
			KeyModifierMask |= KeyModifier.Control;
		if (0 != (KeyModifiers & GranularKeyModifier.AltL) || 0 != (KeyModifiers & GranularKeyModifier.AltR))
			KeyModifierMask |= KeyModifier.Alt;
		if (0 != (KeyModifiers & GranularKeyModifier.CommandL) || 0 != (KeyModifiers & GranularKeyModifier.CommandR))
			KeyModifierMask |= KeyModifier.Command;

		newEvent.ModifierKeyMask = KeyModifierMask;

		PostEvent(newEvent);
	}

#if OSX
	private bool HandleCocoaKeyEvent(ref SDL_Event ev, WindowEvent newEvent, bool pressed) {
		switch (ev.key.key) {
			case SDL_Keycode.SDLK_CAPSLOCK: {
					if (!pressed)
						return false;
					bool locked = (ev.key.mod & SDL_Keymod.SDL_KMOD_CAPS) != 0;
					newEvent.EventType = locked ? WindowEventType.KeyDown : WindowEventType.KeyUp;
					KeySymCase(newEvent, GranularKeyModifier.CapsLock, locked, ButtonCode.KeyCapsLock);
					return true;
				}

			case SDL_Keycode.SDLK_LSHIFT: return CocoaModifierCase(newEvent, GranularKeyModifier.ShiftL, GranularKeyModifier.ShiftR, pressed, ButtonCode.KeyLShift);
			case SDL_Keycode.SDLK_RSHIFT: return CocoaModifierCase(newEvent, GranularKeyModifier.ShiftR, GranularKeyModifier.ShiftL, pressed, ButtonCode.KeyLShift);

			case SDL_Keycode.SDLK_LCTRL: return CocoaModifierCase(newEvent, GranularKeyModifier.ControlL, GranularKeyModifier.ControlR, pressed, ButtonCode.KeyLControl);
			case SDL_Keycode.SDLK_RCTRL: return CocoaModifierCase(newEvent, GranularKeyModifier.ControlR, GranularKeyModifier.ControlL, pressed, ButtonCode.KeyLControl);

			case SDL_Keycode.SDLK_LGUI: return CocoaModifierCase(newEvent, GranularKeyModifier.CommandL, GranularKeyModifier.CommandR, pressed, ButtonCode.KeyLWin);
			case SDL_Keycode.SDLK_RGUI: return CocoaModifierCase(newEvent, GranularKeyModifier.CommandR, GranularKeyModifier.CommandL, pressed, ButtonCode.KeyLWin);

			case SDL_Keycode.SDLK_LALT: return CocoaModifierCase(newEvent, GranularKeyModifier.AltL, GranularKeyModifier.AltR, pressed, ButtonCode.KeyLAlt);
			case SDL_Keycode.SDLK_RALT: return CocoaModifierCase(newEvent, GranularKeyModifier.AltR, GranularKeyModifier.AltL, pressed, ButtonCode.KeyLAlt);
		}

		if (ev.key.raw == 0 && ev.key.scancode != SDL_Scancode.SDL_SCANCODE_A)
			return false;

		newEvent.VirtualKeyCode = ev.key.raw;
		return true;
	}

	private bool CocoaModifierCase(WindowEvent ev, GranularKeyModifier side, GranularKeyModifier otherSide, bool pressed, ButtonCode key) {
		bool otherSideHeld = (KeyModifiers & otherSide) != 0;
		KeySymCase(ev, side, pressed, key);
		return !otherSideHeld;
	}
#endif

	private ConcurrentQueue<WindowEvent> Events = [];
	private WindowEvent PostEvent(WindowEvent newEvent, bool debugEvent = false) {
		Events.Enqueue(newEvent);
		return newEvent;
	}

	public SDL3_Window Create(string title, int width, int height, SDL_WindowFlags flags) {
		window = SDL3.SDL_CreateWindow(title, width, height, flags);
#if WIN32
		SDL3.SDL_SetWindowsMessageHook(&WindowsMessageHook, 0);
#endif

		return this;
	}

#if WIN32
	const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
	const int KF_EXTENDED = 0x0100;

	static readonly int[] WindowsVirtualKeys = new int[512];

	static int WindowsScanCodeIndex(int scanCode) => (scanCode & 0xFF) | ((scanCode & 0xFF00) != 0 ? 0x100 : 0);

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	static SDLBool WindowsMessageHook(nint userdata, MSG* msg) {
		uint message = msg->message;
		if (message == WM_KEYDOWN || message == WM_KEYUP || message == WM_SYSKEYDOWN || message == WM_SYSKEYUP) {
			int keyFlags = (int)(((long)msg->lParam >> 16) & 0xFFFF);
			int scanCode = keyFlags & 0x7F;
			if (scanCode != 0) {
				if ((keyFlags & KF_EXTENDED) == KF_EXTENDED)
					scanCode |= 0xE000;
				else if (scanCode == 0x45)
					scanCode = 0xE046;
				WindowsVirtualKeys[WindowsScanCodeIndex(scanCode)] = (int)(nuint)msg->wParam & 0xFF;
			}
		}
		return true;
	}
#endif
}