using static Source.Common.Input.JoystickConstants;
using System.Runtime.CompilerServices;

namespace Source.Common.Input;

public static class JoystickConstants
{
	public const int JOYSTICK_MAX_BUTTON_COUNT = 32;
	public const int JOYSTICK_POV_BUTTON_COUNT = 4;
	public const int JOYSTICK_AXIS_BUTTON_COUNT = (int)JoystickAxis.MaxJoystickAxes * 2;
}

public enum ButtonCode
{
	Invalid = -1,
	None = 0,

	KeyFirst = 0,
	KeyNone = KeyFirst,
	Key0,
	Key1,
	Key2,
	Key3,
	Key4,
	Key5,
	Key6,
	Key7,
	Key8,
	Key9,
	KeyA,
	KeyB,
	KeyC,
	KeyD,
	KeyE,
	KeyF,
	KeyG,
	KeyH,
	KeyI,
	KeyJ,
	KeyK,
	KeyL,
	KeyM,
	KeyN,
	KeyO,
	KeyP,
	KeyQ,
	KeyR,
	KeyS,
	KeyT,
	KeyU,
	KeyV,
	KeyW,
	KeyX,
	KeyY,
	KeyZ,
	KeyPad0,
	KeyPad1,
	KeyPad2,
	KeyPad3,
	KeyPad4,
	KeyPad5,
	KeyPad6,
	KeyPad7,
	KeyPad8,
	KeyPad9,
	KeyPadDivide,
	KeyPadMultiply,
	KeyPadMinus,
	KeyPadPlus,
	KeyPadEnter,
	KeyPadDecimal,
	KeyLBracket,
	KeyRBracket,
	KeySemicolon,
	KeyApostrophe,
	KeyBackquote,
	KeyComma,
	KeyPeriod,
	KeySlash,
	KeyBackslash,
	KeyMinus,
	KeyEqual,
	KeyEnter,
	KeySpace,
	KeyBackspace,
	KeyTab,
	KeyCapsLock,
	KeyNumLock,
	KeyEscape,
	KeyScrollLock,
	KeyInsert,
	KeyDelete,
	KeyHome,
	KeyEnd,
	KeyPageUp,
	KeyPageDown,
	KeyBreak,
	KeyLShift,
	KeyRShift,
	KeyLAlt,
	KeyRAlt,
	KeyLControl,
	KeyRControl,
	KeyLWin,
	KeyRWin,
	KeyApp,
	KeyUp,
	KeyLeft,
	KeyDown,
	KeyRight,
	KeyF1,
	KeyF2,
	KeyF3,
	KeyF4,
	KeyF5,
	KeyF6,
	KeyF7,
	KeyF8,
	KeyF9,
	KeyF10,
	KeyF11,
	KeyF12,
	KeyCapsLockToggle,
	KeyNumLockToggle,
	KeyScrollLockToggle,

	KeyLast = KeyScrollLockToggle,
	KeyCount = KeyLast - KeyFirst + 1,

	MouseFirst = KeyLast + 1,

	MouseLeft = MouseFirst,
	MouseRight,
	MouseMiddle,
	Mouse4,
	Mouse5,
	MouseWheelUp,     // A fake button which is 'pressed' and 'released' when the wheel is moved up 
	MouseWheelDown,   // A fake button which is 'pressed' and 'released' when the wheel is moved down

	MouseLast = MouseWheelDown,
	MouseCount = MouseLast - MouseFirst + 1,

	JoystickFirst = MouseLast + 1,

	JoystickFirstButton = JoystickFirst,
	JoystickLastButton = (JoystickFirstButton + ((MAX_JOYSTICKS - 1) * JOYSTICK_MAX_BUTTON_COUNT) + (JOYSTICK_MAX_BUTTON_COUNT - 1)),
	JoystickFirstPovButton,
	JoystickLastPovButton = (JoystickFirstPovButton + ((MAX_JOYSTICKS - 1) * JOYSTICK_POV_BUTTON_COUNT) + (JOYSTICK_POV_BUTTON_COUNT - 1)),
	JoystickFirstAxisButton,
	JoystickLastAxisButton = (JoystickFirstAxisButton + ((MAX_JOYSTICKS - 1) * JOYSTICK_AXIS_BUTTON_COUNT) + (JOYSTICK_AXIS_BUTTON_COUNT - 1)),

	JoystickLast = JoystickLastAxisButton,

	NovintFirst = JoystickLast + 2, // plus 1 missing key. +1 seems to cause issues on the first button.

	NovintLogo0 = NovintFirst,
	NovintTriangle0,
	NovintBolt0,
	NovintPlus0,
	NovintLogo1,
	NovintTriangle1,
	NovintBolt1,
	NovintPlus1,

	NovintLast = NovintPlus1,

	Last,
	Count = Last - KeyFirst + 1,

	KeyXButtonUp = JoystickFirstPovButton, // POV buttons
	KeyXButtonRight,
	KeyXButtonDown,
	KeyXButtonLeft,

	KeyXButtonA = JoystickFirstButton,      // Buttons
	KeyXButtonB,
	KeyXButtonX,
	KeyXButtonY,
	KeyXButtonLeftShoulder,
	KeyXButtonRightShoulder,
	KeyXButtonBack,
	KeyXButtonStart,
	KeyXButtonStick1,
	KeyXButtonStick2,

	KeyXStick1Right = JoystickFirstAxisButton, // XAXIS POSITIVE
	KeyXStick1Left,                           // XAXIS NEGATIVE
	KeyXStick1Down,                           // YAXIS POSITIVE
	KeyXStick1Up,                             // YAXIS NEGATIVE
	KeyXButtonLTrigger,                       // ZAXIS POSITIVE
	KeyXButtonRTrigger,                       // ZAXIS NEGATIVE
	KeyXStick2Right,                          // UAXIS POSITIVE
	KeyXStick2Left,                           // UAXIS NEGATIVE
	KeyXStick2Down,                           // VAXIS POSITIVE
	KeyXStick2Up,                             // VAXIS NEGATIVE

}
public static class ButtonCodeExts {
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAlpha(this ButtonCode code) {
		return (code >= ButtonCode.KeyA) && (code <= ButtonCode.KeyZ);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAlphaNumeric(this ButtonCode code) {
		return (code >= ButtonCode.Key0) && (code <= ButtonCode.KeyZ);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsSpace(this ButtonCode code) {
		return (code == ButtonCode.KeyEnter) || (code == ButtonCode.KeyTab) || (code == ButtonCode.KeySpace);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsKeypad(this ButtonCode code) {
		return (code >= ButtonCode.KeyPad0) && (code <= ButtonCode.KeyPadDecimal);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ButtonCode GetBaseButtonCode(this ButtonCode code) {
		return code;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsPunctuation(this ButtonCode code) {
		return (code >= ButtonCode.Key0) && (code <= ButtonCode.KeySpace) && !IsAlphaNumeric(code) && !IsSpace(code) && !IsKeypad(code);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsKeyCode(this ButtonCode code) {
		return (code >= ButtonCode.KeyFirst) && (code <= ButtonCode.KeyLast);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsMouseCode(this ButtonCode code) {
		return (code >= ButtonCode.MouseFirst) && (code <= ButtonCode.MouseLast);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsNovintCode(this ButtonCode code) {
		return (code >= ButtonCode.NovintFirst) && (code <= ButtonCode.NovintLast);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsJoystickCode(this ButtonCode code) {
		return (code >= ButtonCode.JoystickFirst) && (code <= ButtonCode.JoystickLast);
	}
}
