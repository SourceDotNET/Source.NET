#if MENU_DLL
using Game.MenuSystem.GarrysMod;

using Source.Common.GarrysMod.Lua;
using Source.Common.Steam;

using Steamworks;

namespace Game.MenuSystem;

public class GMODGetAPIManifest
{
	readonly int LuaStarts;
	readonly LuaObject Callback = new();
	readonly CallResult<HTTPRequestCompleted_t> RequestCompleted;

	GMODGetAPIManifest() {
		LuaStarts = MenuSystem.g_MenuSystem.LuaStarts;
		RequestCompleted = CallResult<HTTPRequestCompleted_t>.Create(OnRequestCompleted);
	}

	public static void Request(ILuaObject callback) {
		GMODGetAPIManifest request = new();
		request.Callback.Set(callback);

		if (!request.Callback.isFunction()) {
			Warning("GetAPIManifest no callback!\n");
			request.Delete();
			return;
		}

		if (!SteamworksExts.HasSteamHTTP()) {
			Warning("GetAPIManifest no HTTP!\n");
			request.Delete();
			return;
		}

		HTTPRequestHandle handle = SteamHTTP.CreateHTTPRequest(EHTTPMethod.k_EHTTPMethodGET, "https://api.facepunch.com/api/public/manifest?public_key=RWsOQQrO860EaGY3qPsSsBQSev3gNO0KrcF3kv4Rl5frjE9OuUKQgAsRutxMZ4aU");
		SteamHTTP.SendHTTPRequest(handle, out SteamAPICall_t call);
		request.RequestCompleted.Set(call);
	}

	void OnRequestCompleted(HTTPRequestCompleted_t result, bool ioFailure) {
		if (LuaStarts != MenuSystem.g_MenuSystem.LuaStarts) {
			Warning("GetAPIManifest no Lua!\n");
			Delete();
			return;
		}

		if (!result.m_bRequestSuccessful) {
			Warning("Failed to request data from Facepunch servers!\n");
			Delete();
			return;
		}

		byte[] body = new byte[result.m_unBodySize];
		if (!SteamHTTP.GetHTTPResponseBodyData(result.m_hRequest, body, result.m_unBodySize))
			Warning("Failed to read data from Facepunch servers!\n");
		else {
			int length = Array.IndexOf(body, (byte)0);
			ReadOnlySpan<byte> manifest = length < 0 ? body : body.AsSpan(0, length);

			if (Callback.isFunction()) {
				Callback.Push();
				g_Lua!.PushString(manifest);
				g_Lua.CallInternalNoReturns(1);
			}
		}

		Delete();
	}

	void Delete() {
		RequestCompleted.Dispose();
		Callback.UnReference();
	}
}
#endif
