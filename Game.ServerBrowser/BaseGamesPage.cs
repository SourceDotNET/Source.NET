using Source;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.ServerBrowser;
using Source.GUI.Controls;

using Steamworks;

namespace Game.ServerBrowser;

enum SecureFilter
{
	AllServers = 0,
	SecureOnly,
	InsecureOnly
}

struct ServerMaps
{
	public string OriginalName;
	public string FriendlyName;
	public int PanelIndex;
	public bool OnDisk;
}

struct GameTypes
{
	public string Prefix;
	public string GametypeName;
}

struct ServerQualitySort
{
	public int Index;
	public int Ping;
	public int PlayerCount;
	public int MaxPlayerCount;
}

public class GameListPanel : ListPanel
{
	BaseGamesPage Outer;

	public GameListPanel(BaseGamesPage parent, ReadOnlySpan<char> panelName) : base(parent, panelName) => Outer = parent;

	public override void OnKeyCodePressed(ButtonCode code) {
		if (code == ButtonCode.KeyEnter && Outer.OnGameListEnterPressed())
			return;

		base.OnKeyCodePressed(code);
	}
}

class QuickListMapServerList
{
	private readonly List<int> Data;

	public QuickListMapServerList() => Data = new List<int>(1);
	public QuickListMapServerList(QuickListMapServerList src) => Data = [.. src.Data];

	public QuickListMapServerList Assign(QuickListMapServerList src) {
		Data.Clear();
		Data.AddRange(src.Data);
		return this;
	}

	public int Count() => Data.Count;

	public int this[int index] {
		get => Data[index];
		set => Data[index] = value;
	}

	public void Add(int value) => Data.Add(value);

	public void Clear() => Data.Clear();
}

class CheckBoxWithStatus : CheckButton
{
	public CheckBoxWithStatus(Panel parent, ReadOnlySpan<char> panelName, ReadOnlySpan<char> text) : base(parent, panelName, text) { }
	public override void OnCursorEntered() => ServerBrowserDialog.Instance!.UpdateStatusText("#ServerBrowser_QuickListExplanation");
	public override void OnCursorExited() => ServerBrowserDialog.Instance!.UpdateStatusText("");
}

public class BaseGamesPage : PropertyPage, IGameList
{
	public static ConVar sb_mod_suggested_maxplayers = new("sb_mod_suggested_maxplayers", "0", FCvar.Hidden);
	static ConVar sb_filter_incompatible_versions = new("sb_filter_incompatible_versions",
#if DEBUG
		"0",
#else
		"1",
#endif
		0, "Hides servers running incompatible versions from the server browser.  (Internet tab only.)");

	public enum PageType
	{
		InternetServer,
		LANServer,
		FriendsServer,
		FavoritesServer,
		HistoryServer,
		SpectatorServer
	}

	public enum Column
	{
		Password,
		Secure,
		Replay,
		Name,
		IPAddress,
		GameDesc,
		Players,
		Bots,
		Map,
		Ping
	}

	public enum WorkshopMode_t
	{
		None,
		WorkshopOnly,
		SubscribedOnly
	}

	bool AutoSelectFirstItemInGameList;

	public GameListPanel GameList;
	PanelListPanel QuickList;
	public ComboBox LocationFilter;
	public Button Connect;
	public Button RefreshAll;
	Button RefreshQuick;
	Button AddServer;
	Button AddCurrentServer;
	Button AddToFavoritesButton;
	public ToggleButton Filter;
	ComboBox GameFilter;
	TextEntry MapFilter;
	TextEntry MaxPlayerFilterEntry;
	ComboBox PingFilterCombo;
	ComboBox SecureFilterCombo;
	ComboBox TagsIncludeFilter;
	ComboBox? WorkshopFilter;
	CheckButton NoFullServersFilterCheck;
	CheckButton NoEmptyServersFilterCheck;
	CheckButton NoPasswordFilterCheck;
	CheckBoxWithStatus QuickListCheckButton;
	Label FilterString;
	string ComboAllText;
	CheckButton ReplayFilterCheck;
	public bool FiltersVisible;
	IFont? Font;
	Dictionary<ulong, int> GamesFilterItem = [];
	public Dictionary<int, ServerDisplay> Servers = [];
	Dictionary<servernetadr_t, int> ServerIp = [];
	public List<MatchMakingKeyValuePair_t> ServerFilters = [];
	Dictionary<string, QuickListMapServerList> QuicklistServerList = [];
	public int ServerRefreshCount;
	List<ServerMaps> MapNamesFound = [];
	PageType MatchMakingType;
	HServerListRequest Request;
	string? CustomResFilename;
	int ImageIndexPassword;
	int ImageIndexSecure;
	int ImageIndexSecureVacBanned;
	int ImageIndexReplay;
	string GameFilterText;
	string MapFilterText;
	int MaxPlayerFilter;
	int PingFilter;
	bool FilterNoFullServers;
	bool FilterNoEmptyServers;
	bool FilterNoPasswordedServers;
	SecureFilter SecureFilter;
	int ServersBlacklisted;
	bool FilterReplayServers;
	CGameID LimitToAppID;

	private ISteamMatchmakingServerListResponse _steamResponse;

	public BaseGamesPage(Panel parent, ReadOnlySpan<char> panelName, PageType type, string? customResFilename = null) : base(parent, panelName) {
		_steamResponse = new ISteamMatchmakingServerListResponse(ServerResponded, ServerFailedToRespond, RefreshComplete);

		Request = HServerListRequest.Invalid;
		CustomResFilename = customResFilename;

		SetSize(624, 278);
		GameFilterText = "";
		MapFilterText = "";
		MaxPlayerFilter = 0;
		PingFilter = 0;
		ServerRefreshCount = 0;
		FilterNoFullServers = false;
		FilterNoEmptyServers = false;
		FilterNoPasswordedServers = false;
		SecureFilter = SecureFilter.AllServers;
		Font = null;
		MatchMakingType = type;
		FilterReplayServers = false;

		WorkshopFilter = null;

		bool runningTF2 = false;// GameSupportsReplay();

		ReadOnlySpan<char> all = Localize.Find("ServerBrowser_All");

		Connect = new(this, "ConnectButton", "#ServerBrowser_Connect");
		Connect.SetEnabled(false);
		RefreshAll = new(this, "RefreshButton", "#ServerBrowser_Refresh");
		RefreshQuick = new(this, "RefreshQuickButton", "#ServerBrowser_RefreshQuick");
		AddServer = new(this, "AddServerButton", "#ServerBrowser_AddServer");
		AddCurrentServer = new(this, "AddCurrentServerButton", "#ServerBrowser_AddCurrentServer");
		GameList = new(this, "gamelist");
		GameList.SetAllowUserModificationOfColumns(true);

		QuickList = new(this, "quicklist");
		QuickList.SetFirstColumnWidth(0);

		AddToFavoritesButton = new(this, "AddToFavoritesButton", "");
		AddToFavoritesButton.SetEnabled(false);
		AddToFavoritesButton.SetVisible(false);

		GameList.UserConfigFileVersion = 2;

		GameList.AddColumnHeader((int)Column.Password, "Password", "#ServerBrowser_Password", 16, ListPanel.ColumnFlags.FixedSize | ListPanel.ColumnFlags.Image);
		GameList.AddColumnHeader((int)Column.Secure, "Secure", "#ServerBrowser_Secure", 16, ListPanel.ColumnFlags.FixedSize | ListPanel.ColumnFlags.Image);

		int replayWidth = runningTF2 ? 16 : 0;

		GameList.AddColumnHeader((int)Column.Replay, "Replay", "#ServerBrowser_Replay", replayWidth, ListPanel.ColumnFlags.FixedSize | ListPanel.ColumnFlags.Image);
		GameList.AddColumnHeader((int)Column.Name, "Name", "#ServerBrowser_Servers", 50, ListPanel.ColumnFlags.ResizeWithWindow | ListPanel.ColumnFlags.Unhidable);
		GameList.AddColumnHeader((int)Column.IPAddress, "IPAddr", "#ServerBrowser_IPAddress", 64, ListPanel.ColumnFlags.Hidden);
		GameList.AddColumnHeader((int)Column.GameDesc, "GameDesc", "#ServerBrowser_Game", 112, 112, 300, 0);
		GameList.AddColumnHeader((int)Column.Players, "Players", "#ServerBrowser_Players", 55, ListPanel.ColumnFlags.FixedSize);
		GameList.AddColumnHeader((int)Column.Bots, "Bots", "#ServerBrowser_Bots", 40, ListPanel.ColumnFlags.FixedSize);
		GameList.AddColumnHeader((int)Column.Map, "Map", "#ServerBrowser_Map", 90, 90, 300, 0);
		GameList.AddColumnHeader((int)Column.Ping, "Ping", "#ServerBrowser_Latency", 55, ListPanel.ColumnFlags.FixedSize);

		GameList.SetColumnHeaderTooltip((int)Column.Password, "#ServerBrowser_PasswordColumn_Tooltip");
		GameList.SetColumnHeaderTooltip((int)Column.Bots, "#ServerBrowser_BotColumn_Tooltip");
		GameList.SetColumnHeaderTooltip((int)Column.Secure, "#ServerBrowser_SecureColumn_Tooltip");

		if (runningTF2)
			GameList.SetColumnHeaderTooltip((int)Column.Replay, "#ServerBrowser_ReplayColumn_Tooltip");

		GameList.SetSortFunc((int)Column.Password, ServerListCompare.Password);
		GameList.SetSortFunc((int)Column.Bots, ServerListCompare.Bots);
		GameList.SetSortFunc((int)Column.Secure, ServerListCompare.Secure);

		if (runningTF2)
			GameList.SetSortFunc((int)Column.Replay, ServerListCompare.Replay);

		GameList.SetSortFunc((int)Column.Name, ServerListCompare.ServerName);
		GameList.SetSortFunc((int)Column.IPAddress, ServerListCompare.IPAddress);
		GameList.SetSortFunc((int)Column.GameDesc, ServerListCompare.Game);
		GameList.SetSortFunc((int)Column.Players, ServerListCompare.Players);
		GameList.SetSortFunc((int)Column.Map, ServerListCompare.Map);
		GameList.SetSortFunc((int)Column.Ping, ServerListCompare.Ping);

		GameList.SetSortColumn((int)Column.Ping);

		CreateFilters();
		LoadFilterSettings();

		AutoSelectFirstItemInGameList = false;

		if (runningTF2)
			sb_mod_suggested_maxplayers.SetValue(24);
	}

	// FIXME #37
	public override void Dispose() {
		base.Dispose();
		if (Request != HServerListRequest.Invalid) {
			SteamMatchmakingServers.ReleaseRequest(Request);
			Request = HServerListRequest.Invalid;
		}
	}

	public int GetInvalidServerListID() => -1;

	public override void PerformLayout() {
		base.PerformLayout();

		if (GetSelectedServerID() == -1)
			Connect.SetEnabled(false);
		else
			Connect.SetEnabled(true);

		if (SupportsItem(IGameList.InterfaceItem.AddServer))
			AddServer.SetEnabled(true);
		else
			AddServer.SetEnabled(false);

		if (SupportsItem(IGameList.InterfaceItem.AddCurrentServer))
			AddCurrentServer.SetEnabled(true);
		else
			AddCurrentServer.SetEnabled(false);

		if (IsRefreshing())
			RefreshAll.SetText("#ServerBrowser_StopRefreshingList");

		if (GameList.GetItemCount() > 0)
			RefreshQuick.SetEnabled(true);
		else
			RefreshQuick.SetEnabled(false);

		Repaint();
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		ImageList imageList = new(false);
		ImageIndexPassword = imageList.AddImage(SchemeManager.GetImage("servers/icon_password", false));
		ImageIndexSecure = imageList.AddImage(SchemeManager.GetImage("servers/icon_robotron", false));
		ImageIndexSecureVacBanned = imageList.AddImage(SchemeManager.GetImage("servers/icon_secure_deny", false));
		ImageIndexReplay = imageList.AddImage(SchemeManager.GetImage("servers/icon_replay", false));
		int passwordColumnImage = imageList.AddImage(SchemeManager.GetImage("servers/icon_password_column", false));
		int secureColumnImage = imageList.AddImage(SchemeManager.GetImage("servers/icon_robotron_column", false));
		int replayColumnImage = imageList.AddImage(SchemeManager.GetImage("servers/icon_replay_column", false));

		GameList.SetImageList(imageList, true);

		Font = scheme.GetFont("ListSmall", IsProportional()) ?? scheme.GetFont("DefaultSmall", IsProportional());

		GameList.SetFont(Font!);
		GameList.SetColumnHeaderImage((int)Column.Password, passwordColumnImage);
		GameList.SetColumnHeaderImage((int)Column.Secure, secureColumnImage);
		GameList.SetColumnHeaderImage((int)Column.Replay, replayColumnImage);
	}

	void SelectQuickListServers() { }

	void PrepareQuickListMap(ReadOnlySpan<char> mapName, int listID) {
		Span<char> cMapName = stackalloc char[512];
		sprintf(cMapName, "%s").S(mapName);
		for (int i = 0; cMapName[i] != '\0'; i++)
			cMapName[i] = char.ToLowerInvariant(cMapName[i]);

		Span<char> path = stackalloc char[512];
		sprintf(path, "maps/%s.bsp").S(cMapName);
		path = path.SliceNullTerminatedString();

		var key = path.ToString();

		if (!QuicklistServerList.TryGetValue(key, out QuickListMapServerList? serverList)) {
			serverList = new QuickListMapServerList();
			int index = QuicklistServerList.Count;
			QuicklistServerList.Add(key, serverList);

			ReadOnlySpan<char> friendlyName = stackalloc char[Source.Constants.MAX_MAP_NAME];
			ReadOnlySpan<char> friendlyGameTypeName = ServerBrowser.Instance!.GetMapFriendlyNameAndGameType(cMapName, out friendlyName);

			if (QuickList != null) {
				ServerMaps serverMap = new() {
					FriendlyName = friendlyName.ToString(),
					OriginalName = cMapName.SliceNullTerminatedString().ToString(),
					OnDisk = fileSystem.FileExists(path, "MOD"),
				};

				QuickListPanel quickListPanel = new(QuickList, "QuickListPanel");
				if (quickListPanel != null) {
					quickListPanel.InvalidateLayout();
					quickListPanel.SetName(serverMap.OriginalName);
					quickListPanel.SetMapName(serverMap.FriendlyName);
					quickListPanel.SetImage(serverMap.OriginalName);
					quickListPanel.SetGameType(friendlyGameTypeName);
					quickListPanel.SetVisible(true);
					quickListPanel.SetRefreshing();
					serverMap.PanelIndex = QuickList.AddItem(null, quickListPanel);
				}

				MapNamesFound.Add(serverMap);
				MapNamesFound.Sort(ServerListSort.MapNames);
			}

			List<int> panelSort = QuickList!.GetSortedVector();
			panelSort.Clear();
			for (int i = 0; i < MapNamesFound.Count; i++)
				panelSort.Add(MapNamesFound[i].PanelIndex);
		}

		if (serverList != null) {
			bool found = false;
			for (int i = 0; i < serverList.Count(); i++) {
				if (serverList[i] == listID) {
					found = true;
					break;
				}
			}

			if (!found)
				serverList.Add(listID);
		}
	}

	public gameserveritem_t GetServer(uint serverID) {
		return SteamMatchmakingServers.GetServerDetails(Request, (int)serverID);
	}

	bool TagsExclude() {
		if (TagsIncludeFilter == null)
			return false;

		return TagsIncludeFilter.GetActiveItem() != 0;
	}

	WorkshopMode_t WorkshopMode() {
		if (WorkshopFilter == null || !ServerBrowser.Instance!.IsWorkshopEnabled())
			return WorkshopMode_t.None;

		return (WorkshopMode_t)WorkshopFilter.GetActiveItem();
	}

	void HideReplayFilter() {
		if (ReplayFilterCheck != null && ReplayFilterCheck.IsVisible())
			ReplayFilterCheck.SetVisible(false);
	}

	void CreateFilters() {
		Filter = new(this, "Filter", "#ServerBrowser_Filters");
		FilterString = new(this, "FilterString", "");

		if (false) { //cstrike
			Filter.SetSelected(false);
			FiltersVisible = false;
		}
		else {
			Filter.SetSelected(true);
			FiltersVisible = true;
		}

		GameFilter = new(this, "GameFilter", 6, false);

		LocationFilter = new(this, "LocationFilter", 6, false);
		LocationFilter.AddItem("", null);

		MapFilter = new(this, "MapFilter");
		MaxPlayerFilterEntry = new(this, "MaxPlayerFilter");
		PingFilterCombo = new(this, "PingFilter", 6, false);
		PingFilterCombo.AddItem("#ServerBrowser_All", null);
		PingFilterCombo.AddItem("#ServerBrowser_LessThan50", null);
		PingFilterCombo.AddItem("#ServerBrowser_LessThan100", null);
		PingFilterCombo.AddItem("#ServerBrowser_LessThan150", null);
		PingFilterCombo.AddItem("#ServerBrowser_LessThan250", null);
		PingFilterCombo.AddItem("#ServerBrowser_LessThan350", null);
		PingFilterCombo.AddItem("#ServerBrowser_LessThan600", null);

		SecureFilterCombo = new(this, "SecureFilter", 3, false);
		SecureFilterCombo.AddItem("#ServerBrowser_All", null);
		SecureFilterCombo.AddItem("#ServerBrowser_SecureOnly", null);
		SecureFilterCombo.AddItem("#ServerBrowser_InsecureOnly", null);

		TagsIncludeFilter = new(this, "TagsInclude", 2, false);
		TagsIncludeFilter.AddItem("#ServerBrowser_TagsInclude", null);
		TagsIncludeFilter.AddItem("#ServerBrowser_TagsDoNotInclude", null);
		TagsIncludeFilter.SetVisible(false);

		if (ServerBrowser.Instance!.IsWorkshopEnabled()) {
			WorkshopFilter = new(this, "WorkshopFilter", 3, false);
			WorkshopFilter.AddItem("#ServerBrowser_All", null);
			WorkshopFilter.AddItem("#ServerBrowser_WorkshopFilterWorkshopOnly", null);
			WorkshopFilter.AddItem("#ServerBrowser_WorkshopFilterSubscribed", null);
			WorkshopFilter.SetVisible(false);
		}

		NoEmptyServersFilterCheck = new(this, "ServerEmptyFilterCheck", "");
		NoFullServersFilterCheck = new(this, "ServerFullFilterCheck", "");
		NoPasswordFilterCheck = new(this, "NoPasswordFilterCheck", "");
		QuickListCheckButton = new(this, "QuickListCheck", "");
		ReplayFilterCheck = new(this, "ReplayFilterCheck", "");

		KeyValues kv = new("mod", "gamedir", "", "appid", null);
		GameFilter.AddItem("#ServerBrowser_All", kv);

		// for (int i = 0; i < ModList.Instance!.ModCount(); i++) {

		// }
	}

	public void LoadFilterSettings() {
		KeyValues filter = ServerBrowserDialog.Instance!.GetFilterSaveData(GetName());

		if (ServerBrowserDialog.Instance!.GetActiveModName().Length > 0) {
			GameFilterText = ServerBrowserDialog.Instance.GetActiveModName().SliceNullTerminatedString().ToString();
			LimitToAppID = ServerBrowserDialog.Instance!.GetActiveAppID();
		}
		else {
			GameFilterText = filter.GetString("game", "").ToString();
			LimitToAppID = new CGameID((ulong)filter.GetInt("appid", 0));//GetUInt64("appid", 0));
		}

		MapFilterText = filter.GetString("map", "").ToString();
		MaxPlayerFilter = filter.GetInt("MaxPlayerCount", 0);
		PingFilter = filter.GetInt("ping", 0);
		FilterNoFullServers = filter.GetBool("NoFull", false);
		FilterNoEmptyServers = filter.GetBool("NoEmpty", false);
		FilterNoPasswordedServers = filter.GetBool("NoPassword", false);
		FilterReplayServers = filter.GetBool("Replay", false);
		QuickListCheckButton.SetSelected(filter.GetBool("QuickList", false));

		SecureFilter = (SecureFilter)filter.GetInt("Secure", 0);
		SecureFilterCombo.ActivateItem((int)SecureFilter);

		int tagsinclude = filter.GetInt("tagsinclude", 0);
		TagsIncludeFilter.ActivateItem(tagsinclude);

		if (WorkshopFilter != null) {
			int workshopFilter = filter.GetInt("workshopfilter", 0);
			WorkshopFilter.ActivateItem(workshopFilter);
		}

		UpdateGameFilter();

		if (MaxPlayerFilter > 0) {
			Span<char> buff = stackalloc char[32];
			sprintf(buff, "%d").D(MaxPlayerFilter);
			MaxPlayerFilterEntry.SetText(buff.SliceNullTerminatedString());
		}

		if (PingFilter > 0) {
			Span<char> buff = stackalloc char[32];
			sprintf(buff, "< %d").D(PingFilter);
			PingFilterCombo.SetText(buff.SliceNullTerminatedString());
		}

		NoFullServersFilterCheck.SetSelected(FilterNoFullServers);
		NoEmptyServersFilterCheck.SetSelected(FilterNoEmptyServers);
		NoPasswordFilterCheck.SetSelected(FilterNoPasswordedServers);
		ReplayFilterCheck.SetSelected(FilterReplayServers);

		OnLoadFilter(filter);
		UpdateFilterSettings();
		UpdateFilterAndQuickListVisibility();
	}

	void UpdateGameFilter() { }

	public virtual void ServerResponded(gameserveritem_t server) {
		int index = int.MinValue;
		while (Servers.ContainsKey(index))
			index++;

		ServerResponded(index, server);
	}

	public virtual void ServerResponded(HServerListRequest request, int serverIndex) {
		var serverItem = SteamMatchmakingServers.GetServerDetails(request, serverIndex);
		if (serverItem == null) {
			AssertMsg(false, "Missing server response");
			return;
		}

		// SOURCE: FIXME: This is a workaround for a steam bug, where it inproperly reads signed bytes out of the
		//        				message. Once the upstream fix makes it into our SteamSDK, this block can be removed.
		serverItem.m_nPlayers = (byte)serverItem.m_nPlayers;
		serverItem.m_nBotPlayers = (byte)serverItem.m_nBotPlayers;
		serverItem.m_nMaxPlayers = (byte)serverItem.m_nMaxPlayers;

		ServerResponded(serverIndex, serverItem);
	}

	public virtual void ServerResponded(int server, gameserveritem_t serverItem) {
		if (!Servers.TryGetValue(server, out ServerDisplay serverDis)) {
			servernetadr_t netAdr = new();
			netAdr.Init(serverItem.m_NetAdr.GetIP(), serverItem.m_NetAdr.GetQueryPort(), serverItem.m_NetAdr.GetConnectionPort());
			if (ServerIp.TryGetValue(netAdr, out int existingServer)) {
				Servers.Remove(existingServer);
				ServerIp.Remove(netAdr);
			}

			ServerDisplay serverDisplay = new() {
				ListID = -1,
				DoNotRefresh = false,
			};
			serverDis = serverDisplay;
			Servers.Add(server, serverDis);
			ServerIp.Add(netAdr, server);
		}

		serverDis.ServerID = server;
		Assert(serverItem.m_NetAdr.GetIP() != 0);

		bool removeItem = false;
		if (!CheckPrimaryFilters(serverItem)) {
			serverDis.DoNotRefresh = true;
			removeItem = true;

			if (GameList.IsValidItemID(serverDis.ListID)) {
				GameList.RemoveItem(serverDis.ListID);
				serverDis.ListID = GetInvalidServerListID();
			}

			return;
		}
		else if (!CheckSecondaryFilters(serverItem))
			removeItem = true;

		KeyValues? kv;
		if (GameList.IsValidItemID(serverDis.ListID)) {
			kv = GameList.GetItem(serverDis.ListID);
			GameList.SetUserData(serverDis.ListID, (uint)serverDis.ServerID);
		}
		else
			kv = new("Server");

		kv!.SetString("name", serverItem.GetServerName());
		kv.SetString("map", serverItem.GetMap());
		kv.SetString("GameDir", serverItem.GetGameDir());
		kv.SetString("GameDesc", serverItem.GetGameDescription());
		kv.SetInt("password", serverItem.m_bPassword ? ImageIndexPassword : 0);

		if (serverItem.m_nBotPlayers > 0)
			kv.SetInt("bots", serverItem.m_nBotPlayers);
		else
			kv.SetString("bots", "");

		if (serverItem.m_bSecure)
			kv.SetInt("secure", ServerBrowser.Instance!.IsVACBannedFromGame((int)serverItem.m_nAppID) ? ImageIndexSecureVacBanned : ImageIndexSecure);
		else
			kv.SetInt("secure", 0);

		kv.SetString("IPAddr", serverItem.m_NetAdr.GetConnectionAddressString());

		int adjustedForBotsPlayers = Math.Max(0, serverItem.m_nPlayers - serverItem.m_nBotPlayers);

		Span<char> buf = stackalloc char[32];
		sprintf(buf, "%d / %d").D(adjustedForBotsPlayers).D(serverItem.m_nMaxPlayers);
		kv.SetString("Players", buf.SliceNullTerminatedString());

		kv.SetInt("PlayerCount", adjustedForBotsPlayers);
		kv.SetInt("MaxPlayerCount", serverItem.m_nMaxPlayers);

		kv.SetInt("Ping", serverItem.m_nPing);

		kv.SetString("Tags", serverItem.GetGameTags());

		kv.SetInt("Replay", 0);// IsReplayServer(serverItem) ? ImageIndexReplay : 0);

		if (serverItem.m_ulTimeLastPlayed != 0) {
			DateTime time = DateTimeOffset.FromUnixTimeSeconds(serverItem.m_ulTimeLastPlayed).LocalDateTime;
			Span<char> timeBuf = stackalloc char[64];
			time.ToString("ddd dd MMM hh:mmtt").AsSpan().CopyTo(timeBuf);
			for (int i = timeBuf.Length - 4; i < timeBuf.Length; i++)
				timeBuf[i] = char.ToLowerInvariant(timeBuf[i]);

			kv.SetString("LastPlayed", timeBuf.SliceNullTerminatedString());
		}

		if (serverDis.DoNotRefresh) {
			kv.SetString("Ping", "");
			kv.SetString("GameDesc", Localize.Find("#ServerBrowser_NotResponding"));
			kv.SetString("Players", "");
			kv.SetString("map", "");
		}

		if (!GameList.IsValidItemID(serverDis.ListID)) {
			serverDis.ListID = GameList.AddItem(kv, (uint)serverDis.ServerID, false, false);
			if (AutoSelectFirstItemInGameList && GameList.GetItemCount() == 1)
				GameList.AddSelectedItem(serverDis.ListID);

			GameList.SetItemVisible(serverDis.ListID, !removeItem);
		}
		else {
			GameList.ApplyItemChanges(serverDis.ListID);
			GameList.SetItemVisible(serverDis.ListID, !removeItem);
		}

		PrepareQuickListMap(serverItem.GetMap(), serverDis.ListID);
		UpdateStatus();
		ServerRefreshCount++;
	}

	void UpdateFilterAndQuickListVisibility() {
		bool showQuickList = QuickListCheckButton.IsSelected();
		bool showFilter = Filter.IsSelected();

		FiltersVisible = !showQuickList && CustomResFilename == null && showFilter;

		GetSize(out int wide, out int tall);
		SetSize(624, 278);

		UpdateDerivedLayouts();
		UpdateGameFilter();

		if (Font != null) {
			GameList.MakeReadyForUse();
			GameList.SetFont(Font);
		}

		SetSize(wide, tall);

		QuickList.SetVisible(showQuickList);
		GameList.SetVisible(!showQuickList);
		Filter.SetVisible(!showQuickList);
		FilterString.SetVisible(!showQuickList);

		InvalidateLayout();
		UpdateFilterSettings();
		ApplyGameFilters();
	}

	void SetQuickListEnabled(bool enabled) {
		QuickListCheckButton.SetEnabled(enabled);
		QuickList.SetVisible(QuickListCheckButton.IsSelected());
		GameList.SetVisible(!QuickListCheckButton.IsSelected());
		Filter.SetVisible(!QuickListCheckButton.IsSelected());
		FilterString.SetVisible(!QuickListCheckButton.IsSelected());
	}

	void SetFiltersVisible(bool visible) {
		if (visible == Filter.IsVisible())
			return;

		Filter.SetVisible(visible);
		OnButtonToggled(Filter, visible ? 1 : 0);
	}

	[UiMessage("ButtonToggled")]
	void OnButtonToggled(Panel sender, int state) {
		UpdateFilterAndQuickListVisibility();

		if (sender == NoFullServersFilterCheck || sender == NoEmptyServersFilterCheck || sender == NoPasswordFilterCheck || sender == ReplayFilterCheck)
			OnTextChanged(sender, "");
	}

	void UpdateDerivedLayouts() {
		Span<char> controlSettings = stackalloc char[MAX_PATH];
		if (CustomResFilename != null)
			sprintf(controlSettings, "%s").S(CustomResFilename);
		else {
			if (Filter.IsSelected() && !QuickListCheckButton.IsSelected())
				sprintf(controlSettings, "servers/InternetGamesPage_Filters.res");
			else
				sprintf(controlSettings, "servers/InternetGamesPage.res");
		}

		ReadOnlySpan<char> pathID = "PLATFORM";
		if (fileSystem.FileExists(controlSettings.SliceNullTerminatedString(), "MOD"))
			pathID = "MOD";

		LoadControlSettings(controlSettings.SliceNullTerminatedString(), pathID);

		if (/*!GameSupportsReplay()*/ true)
			HideReplayFilter();
	}

	[UiMessage("TextChanged")]
	void OnTextChanged(Panel sender, ReadOnlySpan<char> text) {
		if (stricmp(text, ComboAllText) == 0) {
			if (sender is ComboBox combo) {
				combo.SetText("");
				text = "";
			}
		}

		UpdateFilterSettings();
		ApplyGameFilters();

		if (FiltersVisible && (sender == GameFilter || sender == LocationFilter) && ServerBrowserDialog.Instance!.IsVisible()) {
			StopRefresh();
			GetNewServerList();
		}
	}

	void ApplyGameFilters() { }

	public void UpdateStatus() {
		if (GameList.GetItemCount() > 1) {
			Span<char> header = stackalloc char[256];
			Span<char> count = stackalloc char[128];
			Span<char> blacklistcount = stackalloc char[128];

			sprintf(count, "%d").D(GameList.GetItemCount());
			sprintf(blacklistcount, "%d").D(ServersBlacklisted);
			Localize.ConstructString(header, Localize.Find("#ServerBrowser_ServersCountWithBlacklist"), count, blacklistcount);
			GameList.SetColumnHeaderText((int)Column.Name, header);
		}
		else
			GameList.SetColumnHeaderText((int)Column.Name, Localize.Find("#ServerBrowser_Servers"));
	}

	public void UpdateFilterSettings() { }

	public virtual void OnSaveFilter(KeyValues filter) { }
	public virtual void OnLoadFilter(KeyValues filter) { }

	void RecalculateFilterString() { }

	bool CheckPrimaryFilters(gameserveritem_t server) {
		// if (GameFilterText.Length > 0 && (server.GetGameDir().Length > 0 || server.m_nPing > 0) && !GameFilterText.Equals(server.GetGameDir(), StringComparison.OrdinalIgnoreCase))
		// 	return false;

		if (ServerBrowserDialog.Instance!.IsServerBlacklisted(server)) {
			ServersBlacklisted++;
			return false;
		}

		return true;
	}

	bool CheckSecondaryFilters(gameserveritem_t server) {
		bool filterNoEmpty = FilterNoEmptyServers;
		bool filterNoFull = FilterNoFullServers;
		int filterPing = PingFilter;
		int filterMaxPlayerCount = MaxPlayerFilter;
		bool filterNoPassword = FilterNoPasswordedServers;
		int filterSecure = (int)SecureFilter;

		if (QuickList.IsVisible()) {
			filterNoEmpty = true;
			filterNoFull = true;
			filterPing = 100; // QUICKLIST_FILTER_MIN_PING
			filterNoPassword = true;
			filterSecure = (int)SecureFilter.SecureOnly;
			filterMaxPlayerCount = sb_mod_suggested_maxplayers.GetInt();
		}

		if (filterNoEmpty && (server.m_nPlayers - server.m_nBotPlayers) < 1)
			return false;

		if (filterNoFull && server.m_nPlayers >= server.m_nMaxPlayers)
			return false;

		if (filterPing != 0 && server.m_nPing > filterPing)
			return false;

		if (filterMaxPlayerCount != 0 && server.m_nMaxPlayers > filterMaxPlayerCount)
			return false;

		if (filterNoPassword && server.m_bPassword)
			return false;

		if (filterSecure == (int)SecureFilter.SecureOnly && !server.m_bSecure)
			return false;

		if (filterSecure == (int)SecureFilter.InsecureOnly && server.m_bSecure)
			return false;

		// if (FilterReplayServers && !IsReplayServer(server))
		// return false;

		if (!QuickList.IsVisible()) {
			if (MapFilterText.Length > 0 && stricmp(server.GetMap(), MapFilterText) != 0)
				return false;
		}

		return CheckTagFilter(server) && CheckWorkshopFilter(server);
	}

	public virtual bool CheckTagFilter(gameserveritem_t server) => true;
	public virtual bool CheckWorkshopFilter(gameserveritem_t server) => true;

	public uint GetServerFilters(out MatchMakingKeyValuePair_t[] filters) {
		filters = [.. ServerFilters];
		return (uint)filters.Length;
	}

	public virtual void SetRefreshing(bool state) {
		if (state) {
			ServerBrowserDialog.Instance!.UpdateStatusText("#ServerBrowser_RefreshingServerList");
			GameList.SetEmptyListText("");
			RefreshAll.SetText("#ServerBrowser_StopRefreshingList");
			RefreshAll.SetCommand("stoprefresh");
			RefreshQuick.SetEnabled(false);
		}
		else {
			ServerBrowserDialog.Instance!.UpdateStatusText("");
			if (SupportsItem(IGameList.InterfaceItem.GetNewList))
				RefreshAll.SetText("#ServerBrowser_RefreshAll");
			else
				RefreshAll.SetText("#ServerBrowser_Refresh");
			RefreshAll.SetCommand("GetNewList");

			if (GameList.GetItemCount() > 0)
				RefreshQuick.SetEnabled(true);
			else
				RefreshQuick.SetEnabled(false);
		}
	}

	[UiCommand("refresh")]
	private void OnRefresh() {
		SteamMatchmakingServers.RefreshQuery(Request);
		SetRefreshing(true);
		ServerRefreshCount = 0;
		ClearQuickList();
	}

	[UiMessage("ItemSelected")]
	void OnItemSelected() {
		if (GetSelectedServerID() == -1)
			Connect.SetEnabled(false);
		else
			Connect.SetEnabled(true);
	}

	public override void OnKeyCodePressed(ButtonCode code) {
		if (code == ButtonCode.KeyF5)
			StartRefresh();
		else
			base.OnKeyCodePressed(code);
	}

	public bool OnGameListEnterPressed() => false;
	int GetSelectedItemsCount() => GameList.GetSelectedItemsCount();

	[UiMessage("AddToFavorites")]
	public virtual void OnAddToFavorites() { }

	[UiMessage("AddToBlacklist")]
	public virtual void OnAddToBlacklist() { }

	public virtual void ServerFailedToRespond(HServerListRequest req, int server) => ServerResponded(req, server);

	public void RemoveServer(ServerDisplay server) {
		if (server.ListID > 0 && server.ListID < GameList.GetItemCount())
			GameList.SetItemVisible(server.ListID, false);

		UpdateStatus();
	}

	[UiMessage("RefreshServer")]
	void OnRefreshServer(int serverID) {
		for (int i = 0; i < GameList.GetSelectedItemsCount(); i++) {
			int serverId = GameList.GetItemUserData(GameList.GetSelectedItem(i));
			SteamMatchmakingServers.RefreshServer(Request, serverId);
		}

		SetRefreshing(IsRefreshing());
	}

	public virtual void StartRefresh() {
		ClearServerList();

		uint filterCount = GetServerFilters(out MatchMakingKeyValuePair_t[] filters);

		if (Request != HServerListRequest.Invalid) {
			SteamMatchmakingServers.ReleaseRequest(Request);
			Request = HServerListRequest.Invalid;
		}

		switch (MatchMakingType) {
			case PageType.FavoritesServer:
				Request = SteamMatchmakingServers.RequestFavoritesServerList(LimitToAppID.AppID(), filters, filterCount, _steamResponse);
				break;
			case PageType.HistoryServer:
				Request = SteamMatchmakingServers.RequestHistoryServerList(LimitToAppID.AppID(), filters, filterCount, _steamResponse);
				break;
			case PageType.InternetServer:
				Request = SteamMatchmakingServers.RequestInternetServerList(LimitToAppID.AppID(), filters, filterCount, _steamResponse);
				break;
			case PageType.SpectatorServer:
				Request = SteamMatchmakingServers.RequestSpectatorServerList(LimitToAppID.AppID(), filters, filterCount, _steamResponse);
				break;
			case PageType.FriendsServer:
				Request = SteamMatchmakingServers.RequestFriendsServerList(LimitToAppID.AppID(), filters, filterCount, _steamResponse);
				break;
			case PageType.LANServer:
				Request = SteamMatchmakingServers.RequestLANServerList(LimitToAppID.AppID(), _steamResponse);
				break;
			default:
				AssertMsg(false, "Unknown server type");
				break;
		}

		SetRefreshing(true);
		ServerRefreshCount = 0;
	}

	void ClearQuickList() {
		QuickList.RemoveAll();
		MapNamesFound.Clear();

		foreach (var serverList in QuicklistServerList.Values)
			serverList.Clear();

		QuicklistServerList.Clear();
	}

	void ClearServerList() {
		Servers.Clear();
		ServerIp.Clear();
		GameList.RemoveAll();
		ServersBlacklisted = 0;
		ClearQuickList();
	}

	[UiCommand("GetNewList")]
	public virtual void GetNewServerList() => StartRefresh();

	[UiCommand("stoprefresh")]
	public virtual void StopRefresh() {
		ServerRefreshCount = 0;

		if (Request != HServerListRequest.Invalid)
			SteamMatchmakingServers.ReleaseRequest(Request);

		RefreshComplete(Request, EMatchMakingServerResponse.eServerResponded);

		ApplyGameFilters();
	}

	public virtual void RefreshComplete(HServerListRequest request, EMatchMakingServerResponse response) => SelectQuickListServers();
	public bool IsRefreshing() => SteamMatchmakingServers.IsRefreshing(Request);
	public override void OnPageShow() => StartRefresh();
	public override void OnPageHide() => StopRefresh();
	public Panel GetActiveList() => QuickList.IsVisible() ? QuickList : GameList;

	public int GetSelectedServerID() {
		KeyValues? _ = null;
		return GetSelectedServerID(ref _);
	}

	public int GetSelectedServerID(ref KeyValues? kv) {
		int serverId = -1;

		if (QuickList.IsVisible()) {
			if (IsRefreshing())
				return -1;

			if (QuickList.GetSelectedPanel() != null) {
				QuickListPanel? quickPanel = (QuickListPanel?)QuickList.GetSelectedPanel();
				if (quickPanel != null) {
					serverId = GameList.GetItemUserData(quickPanel.GetListID());
					if (kv != null)
						kv = GameList.GetItem(quickPanel.GetListID());
				}
			}
		}
		else {
			if (GameList.GetSelectedItemsCount() == 0)
				return -1;

			serverId = GameList.GetItemUserData(GameList.GetSelectedItem(0));

			if (kv != null)
				kv = GameList.GetItem(GameList.GetSelectedItem(0));
		}

		return serverId;
	}

	[UiCommand("Connect")]
	[UiMessage("ConnectToServer")]
	public void OnBeginConnect() {
		KeyValues? kv = null;
		int serverID = GetSelectedServerID(ref kv);

		if (serverID == -1)
			return;

		StopRefresh();

		ConVarRef sb_dontshow_maxplayer_warning = new("sb_dontshow_maxplayer_warning", true);

		if (sb_dontshow_maxplayer_warning.IsValid()) {
			int maxPlayers = sb_mod_suggested_maxplayers.GetInt();
			if (maxPlayers != 0 && kv != null && !sb_dontshow_maxplayer_warning.GetBool()) {
				int maxCount = kv.GetInt("MaxPlayerCount", 0);
				if (maxCount > maxPlayers) {
					DialogServerWarning dlg = new(this, this, serverID);
					dlg.MakeReadyForUse();
					dlg.MoveToCenterOfScreen();
					dlg.DoModal();

					Span<char> warning = stackalloc char[512];
					Span<char> serverMaxPlayers = stackalloc char[12];
					Span<char> designedMaxPlayers = stackalloc char[12];
					Span<char> gameName = stackalloc char[256];
					sprintf(serverMaxPlayers, "%d").D(maxCount);
					sprintf(designedMaxPlayers, "%d").D(maxPlayers);
					sprintf(gameName, "%s").S(ModList.Instance!.GetModNameForModDir(LimitToAppID));
					Localize.ConstructString(warning, Localize.Find("#ServerBrowser_ServerWarning_MaxPlayers"), serverMaxPlayers, gameName, designedMaxPlayers, designedMaxPlayers);
					dlg.SetDialogVariable("warning", warning.SliceNullTerminatedString());
					return;
				}
			}
		}

		ServerBrowserDialog.Instance!.JoinGame(this, (uint)serverID);
	}

	[UiMessage("ViewGameInfo")]
	void OnViewGameInfo() {
		int serverID = GetSelectedServerID();
		if (serverID == -1)
			return;

		StopRefresh();

		ServerBrowserDialog.Instance!.OpenGameInfoDialog(this, (uint)serverID);
	}

	public ReadOnlySpan<char> GetConnectCode() {
		ReadOnlySpan<char> connectCode = "serverbrowser";

		switch (MatchMakingType) {
			default:
				AssertMsg(false, $"Unknown matchmaking type {MatchMakingType}");
				break;
			case PageType.InternetServer:
				connectCode = "serverbrowser_internet";
				break;
			case PageType.LANServer:
				connectCode = "serverbrowser_lan";
				break;
			case PageType.FriendsServer:
				connectCode = "serverbrowser_friends";
				break;
			case PageType.FavoritesServer:
				connectCode = "serverbrowser_favorites";
				break;
			case PageType.HistoryServer:
				connectCode = "serverbrowser_history";
				break;
			case PageType.SpectatorServer:
				connectCode = "serverbrowser_spectator";
				break;
		}

		return connectCode;
	}

	void OnFavoritesMsg(FavoritesListChanged_t favListChanged) { }

	public virtual bool SupportsItem(IGameList.InterfaceItem item) => false;
	[UiMessage("OpenContextMenu")]
	public virtual void OnOpenContextMenu(int itemID) { }
}

class DialogServerWarning : Frame
{
	IGameList GameList;
	int ServerID;
	CheckButton DontShowThisAgainCheckButton;

	public DialogServerWarning(Panel parent, IGameList gameList, int serverID) : base(parent, "DialogServerWarning") {
		GameList = gameList;
		ServerID = serverID;

		DontShowThisAgainCheckButton = new(this, "DontShowThisAgainCheckbutton", "");

		SetDeleteSelfOnClose(true);
		SetSizeable(false);
	}

	public override void ApplySchemeSettings(IScheme Scheme) {
		base.ApplySchemeSettings(Scheme);
		LoadControlSettings("servers/DialogServerWarning.res");
	}

	readonly static KeyValues KV_Close = new("Close");
	[UiCommand("OK")]
	private void OnOK() {
		PostMessage(this, KV_Close);
		ServerBrowserDialog.Instance!.JoinGame(GameList, (uint)ServerID);
		base.OnCommand("OK");
	}

	public void OnButtonToggled(Panel sender, int state) {
		ConVarRef sb_dontshow_maxplayer_warning = new("sb_dontshow_maxplayer_warning");
		if (sb_dontshow_maxplayer_warning.IsValid())
			sb_dontshow_maxplayer_warning.SetValue(state);
	}
}
