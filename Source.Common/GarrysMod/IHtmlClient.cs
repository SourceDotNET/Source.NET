using System.Runtime.InteropServices;

namespace Source.Common.GarrysMod;

public interface IHtmlClient
{
	[Flags]
	public enum EventModifiers
	{
		None = 0,
		Shift = 1 << 0,
		Control = 1 << 1,
		Alt = 1 << 2,
		LeftMouse = 1 << 3,
		MiddleMouse = 1 << 4,
		RightMouse = 1 << 5,
		OSX_Cmd = 1 << 6,
		ShiftR = 1 << 7,
		ControlR = 1 << 8,
		AltGr = 1 << 9,
		Numpad = 1 << 10
	}

	[StructLayout(LayoutKind.Explicit)]
	public struct KeyEvent
	{
		public enum Type
		{
			KeyDown,
			KeyUp,
			KeyChar,
		}

		[FieldOffset(0)] public Type EventType;
		[FieldOffset(4)] public ushort KeyChar;
		[FieldOffset(4)] public int WindowsKeyCode;
		[FieldOffset(8)] public int NativeKeyCode;
		[FieldOffset(12)] public EventModifiers Modifiers;
	}

	public enum MouseButton
	{
		Left,
		Middle,
		Right,
	}

	public struct MouseEvent
	{
		public int X;
		public int Y;
		public EventModifiers Modifiers;
	}

	void Close();

	void SetSize(int wide, int tall);
	void SetFocused(bool hasFocus);

	void SendKeyEvent(KeyEvent keyEvent);

	void SendMouseMoveEvent(MouseEvent mouseEvent, bool mouseLeave);
	void SendMouseWheelEvent(MouseEvent mouseEvent, int deltaX, int deltaY);
	void SendMouseClickEvent(MouseEvent mouseEvent, MouseButton mouseButton, bool mouseUp, int clickCount);

	void LoadUrl(ReadOnlySpan<char> url);
	void SetHtml(ReadOnlySpan<char> html);
	void Refresh();
	void Stop();
	void GoBack();
	void GoForward();

	void RunJavaScript(ReadOnlySpan<char> code);

	void RegisterJavaScriptFunction(ReadOnlySpan<char> objName, ReadOnlySpan<char> funcName);

	void SetOpenLinksExternally(bool openLinksExternally);

	bool LockImageData();
	void UnlockImageData();
	ReadOnlySpan<byte> GetImageData(out int imageWide, out int imageTall);
}
