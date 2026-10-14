#if MENU_DLL
using Game.MenuSystem.GarrysMod;

using Source;
using Source.Common.GarrysMod.Lua;

namespace Game.MenuSystem;

public class LuaMenuCallback : ILuaGameCallback
{
	public static readonly LuaMenuCallback g_MenuLuaCallback = new();

	static readonly Color cMsgColor = new(120, 220, 100, 255);
	static readonly Color cErrorColor = cMsgColor;

	public ILuaObject CreateLuaObject() => new LuaObject();

	public void DestroyLuaObject(ILuaObject obj) => obj?.UnReference();

	public void ErrorPrint(ReadOnlySpan<char> error, bool print) => Dbg._ColorSpewMessage(SpewType.Message, in cErrorColor, error.ToString());

	public void Msg(ReadOnlySpan<char> msg, bool useless) => Dbg._ColorSpewMessage(SpewType.Message, in cMsgColor, msg.ToString());

	public void MsgColour(ReadOnlySpan<char> msg, in Color color) => Dbg._ColorSpewMessage(SpewType.Message, in color, msg.ToString());

	public void LuaError(in LuaError error) {
		get.MenuSystem()?.OnLuaError(in error, null);

		string stack = "";
		string indent = "";
		int i = 0;
		foreach (LuaError.StackEntry entry in error.Stack) {
			indent += " ";
			string function = entry.Function.Length == 0 ? "unknown" : entry.Function;
			string source = entry.Source.Length == 0 ? "filename" : entry.Source;
			stack += $"{indent} {++i}. {function} - {source}:{entry.Line}\n";
		}

		ErrorPrint("\n[MENU ERROR] " + error.Message + "\n" + stack + "\n", true);
	}

	public void InterfaceCreated(ILuaInterface iface) { }
}
#endif
