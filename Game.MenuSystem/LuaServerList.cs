#if MENU_DLL
using Source;
using Source.Common.GarrysMod.Lua;
using Source.Common.Networking;
using Source.Common.Steam;

using Steamworks;

using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Game.MenuSystem.GarrysMod;

public static partial class LuaServerList
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_serverlist = new("serverlist");

	static readonly HashSet<object> ActiveQueries = [];
	static readonly Dictionary<ulong, bool> FavoritesCache = [];
	static double LastFavoritesRefresh;

	static bool ParseAddress(ReadOnlySpan<char> address, bool useDNS, out uint ip, out ushort port) {
		ip = 0;
		port = 0;

		ReadOnlySpan<char> host = address;
		int colon = address.LastIndexOf(':');
		if (colon >= 0) {
			host = address[..colon];
			if (!ushort.TryParse(address[(colon + 1)..], out port))
				port = 0;
		}

		if (!IPAddress.TryParse(host, out IPAddress? parsed)) {
			if (!useDNS)
				return false;
			try {
				parsed = Dns.GetHostAddresses(new string(host)).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
			}
			catch (SocketException) {
				parsed = null;
			}
			if (parsed == null)
				return false;
		}

		byte[] bytes = parsed.MapToIPv4().GetAddressBytes();
		ip = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
		return true;
	}

	static bool IsValidAddress(uint ip, ushort port) => ip != 0 && port != 0;

	static ulong FavoriteKey(uint ip, ushort port) => (ulong)port << 32 | ip;

	static string AddressToString(uint ip, ushort port) => $"{ip >> 24}.{(ip >> 16) & 0xff}.{(ip >> 8) & 0xff}.{ip & 0xff}:{port}";

	static void RefreshFavorites() {
		if (LastFavoritesRefresh + 10.0 > Platform.Time && FavoritesCache.Count != 0)
			return;

		FavoritesCache.Clear();
		LastFavoritesRefresh = Platform.Time;

		int count = SteamMatchmaking.GetFavoriteGameCount();
		for (int i = 0; i < count; i++) {
			SteamMatchmaking.GetFavoriteGame(i, out AppId_t appID, out uint ip, out ushort connPort, out _, out uint flags, out _);
			if ((appID.m_AppId == 4000 || appID.m_AppId == 0) && (flags & Steamworks.Constants.k_unFavoriteFlagFavorite) != 0)
				FavoritesCache[FavoriteKey(ip, connPort)] = true;
		}
	}

	static void ClearFavorites() => FavoritesCache.Clear();

	static void SetFavorite(uint ip, ushort port, bool favorite) {
		if (favorite)
			SteamMatchmaking.AddFavoriteGame(SteamUtils.GetAppID(), ip, port, port, Steamworks.Constants.k_unFavoriteFlagFavorite, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
		else
			SteamMatchmaking.RemoveFavoriteGame(SteamUtils.GetAppID(), ip, port, port, Steamworks.Constants.k_unFavoriteFlagFavorite);
	}

	[LuaFunction]
	static int AddCurrentServerToFavorites(ILuaInterface lua) {
		if (!SteamworksExts.HasSteamClient()) {
			Warning("Failed to add server to favorites.\n");
			return 0;
		}

		INetChannelInfo? nci = engine.GetNetChannelInfo();
		if (nci == null)
			return 0;

		bool add = lua.GetBool(1);
		ParseAddress(nci.GetAddress(), false, out uint ip, out ushort port);
		SetFavorite(ip, port, add);
		ClearFavorites();
		RefreshFavorites();
		return 0;
	}

	[LuaFunction]
	static int AddServerToFavorites(ILuaInterface lua) {
		if (!SteamworksExts.HasSteamClient()) {
			Warning("Failed to add server to favorites.\n");
			return 0;
		}

		ParseAddress(lua.CheckString(1), false, out uint ip, out ushort port);
		SetFavorite(ip, port, true);
		ClearFavorites();
		RefreshFavorites();
		return 0;
	}

	[LuaFunction]
	static int RemoveServerFromFavorites(ILuaInterface lua) {
		if (!SteamworksExts.HasSteamClient()) {
			Warning("Failed to remove server from favorites.\n");
			return 0;
		}

		ParseAddress(lua.CheckString(1), false, out uint ip, out ushort port);
		SetFavorite(ip, port, false);
		ClearFavorites();
		RefreshFavorites();
		return 0;
	}

	[LuaFunction]
	static int IsServerFavorite(ILuaInterface lua) {
		if (!SteamworksExts.HasSteamClient()) {
			Warning("IsServerFavorite: SteamMatchmaking does not exist!\n");
			lua.PushBool(false);
			return 1;
		}

		RefreshFavorites();
		ParseAddress(lua.CheckString(1), false, out uint ip, out ushort port);
		lua.PushBool(FavoritesCache.GetValueOrDefault(FavoriteKey(ip, port)));
		return 1;
	}

	[LuaFunction]
	static int IsCurrentServerFavorite(ILuaInterface lua) {
		if (!SteamworksExts.HasSteamClient()) {
			Warning("IsCurrentServerFavorite: No SteamUtils or SteamMatchmaking!\n");
			lua.PushBool(false);
			return 1;
		}

		INetChannelInfo? nci = engine.GetNetChannelInfo();
		if (nci == null) {
			lua.PushBool(false);
			return 1;
		}

		RefreshFavorites();
		ParseAddress(nci.GetAddress(), false, out uint ip, out ushort port);
		lua.PushBool(FavoritesCache.GetValueOrDefault(FavoriteKey(ip, port)));
		return 1;
	}

	static string FindTag(string tags, string prefix, int maxLength = int.MaxValue) {
		int index = tags.IndexOf(prefix, StringComparison.Ordinal);
		if (index < 0)
			return "";

		int start = index + prefix.Length;
		int end = start < tags.Length ? tags.IndexOfAny([' ', ',', ';'], start) : -1;
		int length = (end < 0 ? tags.Length : end) - start;
		return tags.Substring(start, Math.Max(0, Math.Min(length, maxLength)));
	}

	static readonly byte[] InvalidDescriptionBytes = Encoding.UTF8.GetBytes(",./*\\#~`¬{}=+_[]()\"");

	static bool DescriptionNeedsReplacing(string description) {
		byte[] bytes = Encoding.UTF8.GetBytes(description);
		if (bytes.Length < 3 || bytes.Length > 32)
			return true;
		return bytes.AsSpan().IndexOfAny(InvalidDescriptionBytes) >= 0;
	}

	public static int PushServerDetails(ILuaInterface lua, gameserveritem_t server) {
		if (server.m_nMaxPlayers <= 1)
			server.m_nMaxPlayers = 2;
		else if (server.m_nMaxPlayers > 128)
			server.m_nMaxPlayers = 128;

		server.m_nPlayers = Math.Clamp(server.m_nPlayers, 0, 128);
		server.m_nBotPlayers = Math.Clamp(server.m_nBotPlayers, 0, 128);
		if (server.m_nBotPlayers > server.m_nPlayers)
			server.m_nBotPlayers = server.m_nPlayers;

		string tags = server.GetGameTags();
		string gamemode = FindTag(tags, "gm:");
		string workshopID = FindTag(tags, "gmws:");
		string version = FindTag(tags, "ver:");
		string location = FindTag(tags, "loc:", 5);
		string category = FindTag(tags, "gmc:");

		string description = server.GetGameDescription();
		if (DescriptionNeedsReplacing(description))
			description = gamemode;

		bool anonymous = !(server.m_steamID.IsValid() && server.m_steamID.GetEAccountType() == EAccountType.k_EAccountTypeGameServer);

		string address = AddressToString(server.m_NetAdr.GetIP(), server.m_NetAdr.GetConnectionPort());
		string name = server.GetServerName();

		lua.PushNumber(server.m_nPing);
		lua.PushString(name.Length == 0 ? address : name);
		lua.PushString(description);
		lua.PushString(server.GetMap());
		lua.PushNumber(server.m_nPlayers);
		lua.PushNumber(server.m_nMaxPlayers);
		lua.PushNumber(server.m_nBotPlayers);
		lua.PushBool(server.m_bPassword);
		lua.PushNumber(server.m_ulTimeLastPlayed);
		lua.PushString(address);
		lua.PushString(gamemode);
		lua.PushString(workshopID);
		lua.PushBool(anonymous);
		lua.PushNumber(server.m_nServerVersion);
		lua.PushString(version);
		lua.PushString(location);
		lua.PushString(category);
		return 17;
	}

	class ServerListQuery
	{
		readonly int LuaStarts = MenuSystem.g_MenuSystem.LuaStarts;
		readonly HashSet<(uint, ulong)> SeenSteamIDs = [];
		readonly HashSet<(uint, ushort)> SeenAddresses = [];
		readonly List<HServerListRequest> Requests = [];
		readonly LuaObject Data = new();
		readonly ISteamMatchmakingServerListResponse Response;

		public ServerListQuery(ILuaObject data) {
			Response = new(ServerResponded, ServerFailedToRespond, RefreshComplete);
			ActiveQueries.Add(this);
			Data.Set(data);

			AppId_t appID = new((uint)Data.GetMemberInt("AppID", 4000));
			string type = Data.GetMemberStr("Type", "internet") ?? "";
			string gameDir = Data.GetMemberStr("GameDir", "garrysmod") ?? "";
			MatchMakingKeyValuePair_t[] filters = [new() { m_szKey = "gamedir", m_szValue = gameDir }];

			switch (type) {
				case "internet": Requests.Add(SteamMatchmakingServers.RequestInternetServerList(appID, filters, (uint)filters.Length, Response)); break;
				case "lan": Requests.Add(SteamMatchmakingServers.RequestLANServerList(appID, Response)); break;
				case "favorite": Requests.Add(SteamMatchmakingServers.RequestFavoritesServerList(appID, filters, (uint)filters.Length, Response)); break;
				case "history": Requests.Add(SteamMatchmakingServers.RequestHistoryServerList(appID, filters, (uint)filters.Length, Response)); break;
				case "friends": Requests.Add(SteamMatchmakingServers.RequestFriendsServerList(appID, filters, (uint)filters.Length, Response)); break;
			}
		}

		bool LuaValid() => LuaStarts == MenuSystem.g_MenuSystem.LuaStarts;

		void ServerResponded(HServerListRequest request, int index) {
			if (!LuaValid()) {
				Delete();
				return;
			}

			gameserveritem_t server = SteamMatchmakingServers.GetServerDetails(request, index);
			if (server == null)
				return;

			if (!server.m_bHadSuccessfulResponse) {
				ServerFailedToRespond(request, index);
				return;
			}

			if (server.GetGameTags().Contains("hltv:1", StringComparison.Ordinal))
				return;

			uint ip = server.m_NetAdr.GetIP();
			ushort port = server.m_NetAdr.GetConnectionPort();
			if (SeenSteamIDs.Contains((ip, server.m_steamID.m_SteamID)) || SeenAddresses.Contains((ip, port)))
				return;

			SeenSteamIDs.Add((ip, server.m_steamID.m_SteamID));
			SeenAddresses.Add((ip, port));

			LuaObject callback = new();
			Data.GetMember("Callback", callback);
			if (callback.isFunction()) {
				ILuaInterface lua = g_Lua!;
				callback.Push();
				int args = PushServerDetails(lua, server);
				LuaObject result = new();
				if (lua.CallInternalGet(args, result) && !result.isNil() && !result.GetBool()) {
					foreach (HServerListRequest pending in Requests.ToArray())
						SteamMatchmakingServers.CancelQuery(pending);
				}
				result.UnReference();
			}
			callback.UnReference();
		}

		void ServerFailedToRespond(HServerListRequest request, int index) {
			if (!LuaValid()) {
				Delete();
				return;
			}

			gameserveritem_t server = SteamMatchmakingServers.GetServerDetails(request, index);
			if (server == null)
				return;

			LuaObject callback = new();
			Data.GetMember("CallbackFailed", callback);
			if (callback.isFunction()) {
				callback.Push();
				g_Lua!.PushString(AddressToString(server.m_NetAdr.GetIP(), server.m_NetAdr.GetQueryPort()));
				LuaObject result = new();
				g_Lua.CallInternalGet(1, result);
				result.UnReference();
			}
			callback.UnReference();
		}

		void RefreshComplete(HServerListRequest request, EMatchMakingServerResponse response) {
			Requests.Remove(request);
			SteamMatchmakingServers.ReleaseRequest(request);
			if (Requests.Count != 0)
				return;

			if (LuaValid()) {
				LuaObject finished = new();
				Data.GetMember("Finished", finished);
				if (finished.isFunction()) {
					finished.Push();
					g_Lua!.CallInternalNoReturns(0);
				}
				finished.UnReference();
			}

			Delete();
		}

		void Delete() {
			foreach (HServerListRequest request in Requests)
				SteamMatchmakingServers.ReleaseRequest(request);
			Requests.Clear();
			Data.UnReference();
			ActiveQueries.Remove(this);
		}
	}

	[LuaFunction]
	static int Query(ILuaInterface lua) {
		ILuaObject? data = lua.GetObject(1);
		if (data != null && data.isTable())
			_ = new ServerListQuery(data);
		return 0;
	}

	class PingQuery
	{
		readonly int LuaStarts = MenuSystem.g_MenuSystem.LuaStarts;
		readonly LuaObject Callback = new();
		readonly ISteamMatchmakingPingResponse Response;
		HServerQuery Query = HServerQuery.Invalid;

		public PingQuery(string address, ILuaObject callback) {
			Response = new(ServerResponded, ServerFailedToRespond);
			ActiveQueries.Add(this);
			Callback.Set(callback);

			if (!SteamworksExts.HasSteamClient() || !ParseAddress(address, true, out uint ip, out ushort port) || !IsValidAddress(ip, port)) {
				ServerFailedToRespond();
				return;
			}

			Query = SteamMatchmakingServers.PingServer(ip, port, Response);
		}

		void ServerResponded(gameserveritem_t server) {
			Query = HServerQuery.Invalid;
			if (LuaStarts == MenuSystem.g_MenuSystem.LuaStarts) {
				Callback.Push();
				g_Lua!.CallInternalNoReturns(PushServerDetails(g_Lua, server));
			}
			Delete();
		}

		void ServerFailedToRespond() {
			if (LuaStarts == MenuSystem.g_MenuSystem.LuaStarts) {
				Callback.Push();
				g_Lua!.CallInternalNoReturns(0);
			}
			Delete();
		}

		void Delete() {
			if (Query != HServerQuery.Invalid)
				SteamMatchmakingServers.CancelServerQuery(Query);
			Query = HServerQuery.Invalid;
			Callback.UnReference();
			ActiveQueries.Remove(this);
		}
	}

	[LuaFunction]
	static int PingServer(ILuaInterface lua) {
		string address = lua.CheckString(1);
		ILuaObject? callback = lua.GetObject(2);
		if (callback != null && callback.isFunction())
			_ = new PingQuery(address, callback);
		return 0;
	}

	class PlayerQuery
	{
		readonly int LuaStarts = MenuSystem.g_MenuSystem.LuaStarts;
		readonly LuaTable Results = new();
		readonly LuaObject Callback = new();
		readonly ISteamMatchmakingPlayersResponse Response;
		int Index = 1;

		public PlayerQuery(string address, ILuaObject callback) {
			Response = new(AddPlayerToList, PlayersFailedToRespond, PlayersRefreshComplete);
			ActiveQueries.Add(this);
			Callback.Set(callback);

			ParseAddress(address, true, out uint ip, out ushort port);
			SteamMatchmakingServers.PlayerDetails(ip, port, Response);
		}

		void AddPlayerToList(string name, int score, float timePlayed) {
			if (LuaStarts != MenuSystem.g_MenuSystem.LuaStarts)
				return;

			LuaTable player = new();
			player.SetMember("name", name);
			player.SetMember("score", (float)score);
			player.SetMember("time", timePlayed);
			Results.SetMember(Index, player);
			player.UnReference();
			Index++;
		}

		void Finish(bool failed) {
			if (LuaStarts == MenuSystem.g_MenuSystem.LuaStarts) {
				Callback.Push();
				Results.Push();
				g_Lua!.PushBool(failed);
				g_Lua.CallInternalNoReturns(2);
			}

			Callback.UnReference();
			Results.UnReference();
			ActiveQueries.Remove(this);
		}

		void PlayersFailedToRespond() => Finish(true);
		void PlayersRefreshComplete() => Finish(false);
	}

	[LuaFunction]
	static int PlayerList(ILuaInterface lua) {
		string address = lua.CheckString(1);
		ILuaObject? callback = lua.GetObject(2);
		if (callback != null && callback.isFunction())
			_ = new PlayerQuery(address, callback);
		return 0;
	}

	class RulesQuery
	{
		readonly int LuaStarts = MenuSystem.g_MenuSystem.LuaStarts;
		readonly LuaTable Results = new();
		readonly LuaObject Callback = new();
		readonly ISteamMatchmakingRulesResponse Response;

		public RulesQuery(string address, ILuaObject callback) {
			Response = new(RulesResponded, RulesFailedToRespond, RulesRefreshComplete);
			ActiveQueries.Add(this);
			Callback.Set(callback);

			ParseAddress(address, true, out uint ip, out ushort port);
			SteamMatchmakingServers.ServerRules(ip, port, Response);
		}

		void RulesResponded(string rule, string value) {
			if (LuaStarts == MenuSystem.g_MenuSystem.LuaStarts)
				Results.SetMember(rule, value);
		}

		void Finish(bool failed) {
			if (LuaStarts == MenuSystem.g_MenuSystem.LuaStarts) {
				Callback.Push();
				Results.Push();
				g_Lua!.PushBool(failed);
				g_Lua.CallInternalNoReturns(2);
			}

			Callback.UnReference();
			Results.UnReference();
			ActiveQueries.Remove(this);
		}

		void RulesFailedToRespond() => Finish(true);
		void RulesRefreshComplete() => Finish(false);
	}

	[LuaFunction]
	static int ServerRules(ILuaInterface lua) {
		string address = lua.CheckString(1);
		ILuaObject? callback = lua.GetObject(2);
		if (callback != null && callback.isFunction())
			_ = new RulesQuery(address, callback);
		return 0;
	}
}
#endif
