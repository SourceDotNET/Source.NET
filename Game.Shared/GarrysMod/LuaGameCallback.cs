#if CLIENT_DLL || GAME_DLL
#if CLIENT_DLL
using Game.Shared;

#endif
using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

using System.Text;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public struct LuaErrorToSend()
{
	public string Addon = "";
	public string Gamemode = "";
	public string Stack = "";
	public string Error = "";
	public string Realm = "";
}

public class LuaGameCallback : ILuaGameCallback
{
	public static readonly LuaGameCallback g_LuaCallback = new();

#if CLIENT_DLL
	static readonly Color cMsgColor = new(255, 241, 122, 255);
#else
	static readonly Color cMsgColor = new(156, 241, 255, 255);
#endif
	static readonly Color cErrorColor = cMsgColor;

#if CLIENT_DLL
	static readonly ConVar lua_log = new("lua_log_cl", "0", 0, "Log clientside Lua errors to lua_errors_client.txt.");
#else
	static readonly ConVar lua_log = new("lua_log_sv", "0", 0, "Log serverside Lua errors to lua_errors_server.txt.");
#endif
	static readonly ConVar lua_error_url = new("lua_error_url", "", FCvar.Replicated | FCvar.DontRecord);

	static readonly Dictionary<string, bool> m_ReportedErrors = [];
	static readonly Queue<LuaErrorToSend> LuaErrorQueue = [];

	public ILuaObject CreateLuaObject() => new LuaObject();

	public void DestroyLuaObject(ILuaObject obj) => obj?.UnReference();

#if GAME_DLL
	public void ErrorPrint(ReadOnlySpan<char> error, bool print) {
		if (lua_log.GetBool()) {
			using IFileHandle? file = filesystem.Open("lua_errors_server.txt", FileOpenOptions.Append | FileOpenOptions.Text);
			if (file != null)
				file.Stream.Write(Encoding.UTF8.GetBytes(error.ToString()));
		}

		if (engine.IsDedicatedServer())
			engine.LogPrint($"Lua Error: {error}\n");

		if (print)
			Dbg._ColorSpewMessage(SpewType.Message, in cErrorColor, error.ToString());
	}
#else
	static int ErrorCount;
	static TimeUnit_t LastErrorTime;
	static readonly Queue<byte[]> ErrorsToSend = [];

	public void ErrorPrint(ReadOnlySpan<char> error, bool print) {
		if (lua_log.GetBool()) {
			using IFileHandle? file = filesystem.Open("lua_errors_client.txt", FileOpenOptions.Append | FileOpenOptions.Text);
			if (file != null)
				file.Stream.Write(Encoding.UTF8.GetBytes(error.ToString()));
		}

		if (print)
			Dbg._ColorSpewMessage(SpewType.Message, in cErrorColor, error.ToString());

		Platform.DebugString(error);

		if (engine.GetAchievementMgr() != null) {
			IAchievement? achievement = engine.GetAchievementMgr()!.GetAchievementByID((int)GMODAchievementID.GMA_BADCODER);
			if (achievement != null)
				((BaseAchievement)achievement).IncrementCount(0);
		}

		TimeUnit_t curtime = gpGlobals.CurTime;
		int count;
		if (LastErrorTime < curtime - 1.0f) {
			int decayed = (int)(ErrorCount - (curtime - LastErrorTime));
			count = decayed < 0 ? 0 : Math.Min(decayed, 5);
			ErrorCount = count;
		}
		else
			count = ErrorCount;

		LastErrorTime = curtime;

		if (gpGlobals.MaxClients <= 1 || (uint)count >= 5)
			return;

		bf_write buf = new(new byte[0x8000], 0x8000);
		buf.WriteByte((int)GModMessageType.LuaError);
		buf.WriteBytes(Encoding.UTF8.GetBytes(error.ToString() + "\0"));

		byte[] data = buf.BaseArray.AsSpan(0, buf.BytesWritten).ToArray();
		if (C_BasePlayer.GetLocalPlayer() != null)
			engine.GMOD_SendToServer(data, data.Length * 8, true);
		else
			ErrorsToSend.Enqueue(data);

		ErrorCount++;
	}

	public static void SendQueuedErrors() {
		if (ErrorsToSend.Count == 0 || C_BasePlayer.GetLocalPlayer() == null)
			return;

		while (ErrorsToSend.Count > 0) {
			byte[] data = ErrorsToSend.Dequeue();
			engine.GMOD_SendToServer(data, data.Length * 8, true);
		}
	}

	public static void ClearQueuedErrors() => ErrorsToSend.Clear();
#endif

	public void Msg(ReadOnlySpan<char> msg, bool useless) => MsgColour(msg, in cMsgColor);

	public void MsgColour(ReadOnlySpan<char> msg, in Color color) => Dbg._ColorSpewMessage(SpewType.Message, in color, msg.ToString());

	public void LuaError(in LuaError error) {
		bool isAddon = LuaHelper.GetAddonFromError(in error, out IAddonSystem.Information addon, out bool overriding);

		string stack = "";
		string indent = "";
		int i = 0;
		foreach (LuaError.StackEntry entry in error.Stack) {
			indent += " ";
			string function = entry.Function.Length == 0 ? "unknown" : entry.Function;
			string source = entry.Source.Length == 0 ? "filename" : entry.Source;
			stack += $"{indent}{++i}. {function} - {source}:{entry.Line}\n";
		}

		string message = error.Message;
		string tag = "ERROR";
		if (isAddon)
			tag = addon.Title;

		string full = "\n[" + tag + "] " + message + "\n" + stack + "\n";
		ErrorPrint(full, true);

		get.MenuSystem()?.OnLuaError(in error, isAddon ? addon : null);

		LuaHelper.CallOnLuaErrorHook(in error, isAddon ? addon.Title : null, isAddon ? addon.WorkshopID : 0);

		if (overriding)
			return;

		if (m_ReportedErrors.ContainsKey(error.Message))
			return;

		m_ReportedErrors[error.Message] = true;

		LuaErrorToSend toSend = new() {
			Addon = isAddon ? addon.WorkshopID.ToString() : "0",
			Gamemode = "",
			Stack = stack,
			Error = error.Message,
			Realm = error.Side
		};
		SendErrorToHTTPServer(ref toSend);
	}

#if CLIENT_DLL
	public void InterfaceCreated(ILuaInterface iface) {
		ErrorCount = 0;
		LastErrorTime = 0;
	}
#else
	public void InterfaceCreated(ILuaInterface iface) { }
#endif

	static bool ShouldSendErrorToHTTPServer(in LuaError error) => !m_ReportedErrors.ContainsKey(error.Message);

	static void SendErrorToHTTPServer(ref LuaErrorToSend error) {
		if (get.SteamHTTP() == null) {
			LuaErrorQueue.Enqueue(error);
			return;
		}

		ReadOnlySpan<char> url = lua_error_url.GetString();
		if (url.Length <= 6)
			return;

		if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
			return;

		throw new NotImplementedException();
	}
}
#endif
