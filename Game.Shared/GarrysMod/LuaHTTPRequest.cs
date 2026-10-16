#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source;
using Source.Common.GarrysMod.Lua;
using Source.Common.Steam;

using Steamworks;

#if GAME_DLL
using SteamHTTP = Steamworks.SteamGameServerHTTP;
#endif

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public class LuaHTTPRequest
{
	static readonly string[] MaliciousEndpoints = [
		"kvac.cz", "gvac.cz", "scriptenforcer.net", "kvacdoor", "panelpetitvac", "cometedoor.site", "anti-leak.cz", "lkpanel.me",
		"gblk.ga", "vcmod.xyz", "lunodoors.com", "gmodstore.xyz", "patchfix.xyz", "weaponstore.xyz", "kvcdoor"
	];

	static readonly string[] LocalPrefixes = [
		"://0.", "://0000.", "://0/", "://0:", "://0x", "://[", "://10.", "://172.16.", "://192.168.", "://127.", "://localhost"
	];

	static readonly string[] ResponseHeaders = [
		"Connection", "Content-Encoding", "Content-Type", "Date", "Server", "Transfer-Encoding", "Vary", "Cache-Control", "Expires",
		"Get-Dictionary", "Status", "Version", "Pragma", "Set-Cookie", "X-Powered-By", "X-UA-Compatible", "Age", "Allow", "Last-Modified",
		"Location", "Refresh", "Warning", "Via", "Access-Control-Allow-Origin", "Accept-Ranges", "Content-Range", "Content-Location",
		"Content-MD5", "Content-Length", "Retry-After", "X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset",
		"X-Rate-Limit-Limit", "X-Rate-Limit-Remaining", "X-Rate-Limit-Reset", "ETag"
	];

	readonly int LuaStarts;
	HTTPRequestHandle Request;
	readonly LuaObject Table = new();
	readonly string Url;
	readonly CallResult<HTTPRequestCompleted_t> RequestCompleted;

	LuaHTTPRequest(ILuaObject table) {
		LuaStarts = g_LuaStarts;
		Table.Set(table);
		Url = Table.GetMemberStr("url", "") ?? "";
		RequestCompleted = CallResult<HTTPRequestCompleted_t>.Create(OnComplete);

		if (Url.Length == 0) {
			Fail("invalid url");
			return;
		}

		if (!Url.StartsWith("http://", StringComparison.Ordinal) && !Url.StartsWith("https://", StringComparison.Ordinal)) {
			Fail("invalid url format");
			return;
		}

		if (IsMaliciousEndpoint(Url)) {
			g_Lua!.ErrorFromLua($"Attempted request to known malicious code endpoint '{Url}'! Blocking!");
			Fail("malicious url");
			return;
		}

		if (IsLocalResource(Url)) {
			Fail("requests to local resources are not allowed");
			return;
		}

		Send();
	}

	static bool IsMaliciousEndpoint(string url) {
		string lower = url.ToLowerInvariant();
		foreach (string endpoint in MaliciousEndpoints) {
			if (lower.Contains(endpoint, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	static bool IsNumericHost(string host) {
		if (host.Length == 0)
			return false;
		return double.TryParse(host, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
	}

	static bool IsLocalResource(string url) {
		string lower = url.ToLowerInvariant();
		foreach (string prefix in LocalPrefixes) {
			if (lower.Contains(prefix, StringComparison.Ordinal))
				return true;
		}

		int scheme = lower.IndexOf("://", StringComparison.Ordinal);
		if (scheme < 0)
			return false;

		int start = scheme + 3;
		int slash = start < lower.Length ? lower.IndexOf('/', start) : -1;
		int colon = start < lower.Length ? lower.IndexOf(':', start) : -1;
		int end = lower.Length;
		if (slash >= 0)
			end = Math.Min(end, slash);
		if (colon >= 0)
			end = Math.Min(end, colon);

		string host = start <= end ? lower[start..end] : "";
		if (IsNumericHost(host))
			return true;
		if (host.EndsWith(".localhost", StringComparison.Ordinal))
			return true;
		return host.Contains('@');
	}

	void CallFailed(string reason) {
		LuaObject failed = new();
		Table.GetMember("failed", failed);
		if (failed.isFunction()) {
			failed.Push();
			g_Lua!.PushString(reason);
			g_Lua.CallInternalNoReturns(1);
		}
		failed.UnReference();
	}

	void Fail(string? reason) {
		CallFailed(reason ?? "unknown");
		Delete();
	}

	static EHTTPMethod MethodFromName(string method) => method switch {
		"get" => EHTTPMethod.k_EHTTPMethodGET,
		"put" => EHTTPMethod.k_EHTTPMethodPUT,
		"head" => EHTTPMethod.k_EHTTPMethodHEAD,
		"post" => EHTTPMethod.k_EHTTPMethodPOST,
		"delete" => EHTTPMethod.k_EHTTPMethodDELETE,
		"options" => EHTTPMethod.k_EHTTPMethodOPTIONS,
		"patch" => EHTTPMethod.k_EHTTPMethodPATCH,
		_ => EHTTPMethod.k_EHTTPMethodInvalid
	};

	void ForEachStringPair(ReadOnlySpan<char> member, Action<string, string> action) {
		LuaObject table = new();
		Table.GetMember(member, table);
		if (table.isTable()) {
			ILuaInterface lua = g_Lua!;
			table.Push();
			lua.PushNil();
			while (lua.Next(-2)) {
				if (lua.IsType(-2, LuaType.String) && lua.IsType(-1, LuaType.String))
					action(lua.GetString(-2)!, lua.GetString(-1)!);
				lua.Pop(1);
			}
			lua.Pop(1);
		}
		table.UnReference();
	}

	void Send() {
		string method = (Table.GetMemberStr("method", "get") ?? "get").ToLowerInvariant();
		Request = SteamHTTP.CreateHTTPRequest(MethodFromName(method), Url);
		if (Request == HTTPRequestHandle.Invalid) {
			Fail("invalid request");
			return;
		}

		ForEachStringPair("headers", (key, value) => SteamHTTP.SetHTTPRequestHeaderValue(Request, key, value));
		ForEachStringPair("parameters", (key, value) => SteamHTTP.SetHTTPRequestGetOrPostParameter(Request, key, value));

		string type = Table.GetMemberStr("type", "text/plain; charset=utf-8") ?? "";
		int timeout = Math.Clamp(Table.GetMemberInt("timeout", 60), 1, 3600);
		SteamHTTP.SetHTTPRequestNetworkActivityTimeout(Request, (uint)timeout);

		LuaObject body = new();
		Table.GetMember("body", body);
		if (body.isString()) {
			if (type.Length == 0) {
				body.UnReference();
				Fail("invalid body content type");
				return;
			}

			body.Push();
			byte[] bytes = g_Lua!.GetStringBytes(-1).ToArray();
			g_Lua.Pop(1);
			SteamHTTP.SetHTTPRequestRawPostBody(Request, type, bytes, (uint)bytes.Length);
		}
		body.UnReference();

		if (!SteamHTTP.SendHTTPRequest(Request, out SteamAPICall_t call)) {
			Fail("invalid request");
			return;
		}

		RequestCompleted.Set(call);
	}

	void OnComplete(HTTPRequestCompleted_t result, bool ioFailure) {
		if (!ThreadInMainThread())
			Warning("CLuaHTTPRequest::OnComplete not in main thread?\n");

		if (LuaStarts != g_LuaStarts) {
			Delete();
			return;
		}

		if (ioFailure) {
			Fail("error");
			return;
		}

		if (!result.m_bRequestSuccessful) {
			SteamHTTP.GetHTTPRequestWasTimedOut(result.m_hRequest, out bool timedOut);
			Fail(timedOut ? "timeout" : "unsuccessful");
			return;
		}

		LuaTable headers = new();
		foreach (string name in ResponseHeaders) {
			if (!SteamHTTP.GetHTTPResponseHeaderSize(Request, name, out uint size))
				continue;

			byte[] value = new byte[size];
			if (SteamHTTP.GetHTTPResponseHeaderValue(Request, name, value, size)) {
				int length = Array.IndexOf(value, (byte)0);
				headers.SetMember(name, length < 0 ? value : value.AsSpan(0, length));
			}
		}

		SteamHTTP.GetHTTPResponseBodySize(result.m_hRequest, out uint bodySize);
		byte[] responseBody = new byte[bodySize];
		SteamHTTP.GetHTTPResponseBodyData(result.m_hRequest, responseBody, bodySize);

		LuaObject success = new();
		Table.GetMember("success", success);
		if (success.isFunction()) {
			success.Push();
			g_Lua!.PushNumber((int)result.m_eStatusCode);
			g_Lua.PushString(responseBody);
			headers.Push();
			g_Lua.CallInternalNoReturns(3);
		}
		success.UnReference();
		headers.UnReference();

		Delete();
	}

	void Delete() {
		RequestCompleted.Dispose();
		if (Request != HTTPRequestHandle.Invalid) {
			SteamHTTP.ReleaseHTTPRequest(Request);
			Request = HTTPRequestHandle.Invalid;
		}
		Table.UnReference();
	}

	public static void Create(ILuaObject table) => _ = new LuaHTTPRequest(table);
}

public static partial class LuaHTTP
{
	static void CallFailed(ILuaObject table, string reason) {
		LuaObject failed = new();
		table.GetMember("failed", failed);
		if (failed.isFunction()) {
			failed.Push();
			g_Lua!.PushString(reason);
			g_Lua.CallInternalNoReturns(1);
		}
		failed.UnReference();
	}

	[LuaGlobal]
	static int HTTP(ILuaInterface lua) {
		ILuaObject? table = lua.GetObject(1);
		if (table == null || !table.isTable()) {
			lua.TypeError("table", 1);
			lua.PushBool(false);
			return 1;
		}

		if (commandLine.FindParm("-disablehttp") != 0) {
			CallFailed(table, "HTTP is disabled by -disablehttp");
			lua.ErrorFromLua($"HTTP function blocked by -disablehttp launch parameter. URL: {table.GetMemberStr("url", "")}\n");
			lua.PushBool(false);
			return 1;
		}

#if GAME_DLL
		if (!SteamworksExts.HasSteamGameServer()) {
#else
		if (!SteamworksExts.HasSteamHTTP()) {
#endif
			lua.ErrorFromLua("HTTP failed - ISteamHTTP isn't available!\n");
			lua.PushBool(false);
			return 1;
		}

		LuaHTTPRequest.Create(table);
		lua.PushBool(true);
		return 1;
	}
}
#endif
