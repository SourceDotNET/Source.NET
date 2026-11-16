using Game.Shared;

using Source;
using Source.Common.Bitbuffers;
using Source.Common.GarrysMod.Lua;

namespace Game.Client.GarrysMod;

public static class LuaUserMessages
{
	public static void RegisterUserMessages(UserMessages messages) {
		messages.Register("LuaUserMessage", -1);
		messages.HookMessage("LuaUserMessage", MsgFunc_LuaUserMessage);
	}

	static void MsgFunc_LuaUserMessage(bf_read msg) {
		if (g_Lua == null || g_Lua.Global() == null)
			return;

		Span<char> name = stackalloc char[512];
		msg.ReadString(name, false, out _);
		ReadOnlySpan<char> nameStr = ((ReadOnlySpan<char>)name).SliceNullTerminatedString();

		LuaObject usermessage = new();
		g_Lua.Global().GetMember("usermessage", usermessage);
		if (!usermessage.isTable()) {
			Warning("Got Lua Message - but haven't got usermessage global table!");
			Warning($"Message was: '{nameStr}'\n");
			usermessage.UnReference();
			return;
		}

		LuaObject incomingMessage = new();
		usermessage.GetMember("IncomingMessage", incomingMessage);
		if (!incomingMessage.isFunction()) {
			Warning("Got Lua Message - but haven't got usermessage.IncomingMessage function!");
			Warning($"Message was: '{nameStr}'\n");
		}
		else {
			LuaAchievements.UnsetIncrementFlag = true;
			incomingMessage.Push();
			g_Lua.PushString(nameStr);
			LuaBfRead.Push_bf_read(msg);
			LuaDisposable disposable = new(g_Lua.Top(), LuaType.None);
			g_Lua.CallInternalNoReturns(2);
			LuaAchievements.UnsetIncrementFlag = false;
			disposable.Dispose();
		}
		incomingMessage.UnReference();
		usermessage.UnReference();
	}
}
