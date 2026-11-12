using Source.Common.Input;

using System.Numerics;
namespace Source.Common.Launcher;

public class WindowEvent
{
	public WindowEventType EventType;
	public int VirtualKeyCode;
	public int WindowsVirtualKey;
	public char UTF8Key;
	public char UTF8KeyUnmodified;
	public KeyModifier ModifierKeyMask;
	public bool WasWindowFocused;
	public Vector2 MousePos;
	public MouseButton MouseButtonFlags;
	public uint MouseClickCount;
	public MouseButton MouseButton;
}