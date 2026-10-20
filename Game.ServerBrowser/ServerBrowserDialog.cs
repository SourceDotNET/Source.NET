using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.ServerBrowser;
using Source.Engine;
using Source.GUI.Controls;

using Steamworks;

namespace Game.ServerBrowser;

class ServerBrowserDialog : Frame
{
	List<DialogGameInfo> GameInfoDialogs = [];
	IGameList GameList;
	Label StatusLabel;
	PropertySheet TabPanel;
	FavoriteGames Favorites;
	BlacklistedServers Blacklist;
	HistoryGames History;
	InternetGames InternetGames;
	SpectateGames SpectateGames;
	LanGames LanGames;
	FriendsGames FriendsGames;
	KeyValues? SavedData;
	KeyValues? FilterData;
	ServerContextMenu ContextMenu;
	string GameName;
	string ModDir;
	CGameID LimitAppID;
	bool CurrentlyConnected;
	gameserveritem_t CurrentConnection;

	public static ServerBrowserDialog? Instance;

	public ServerBrowserDialog(Panel? parent) : base(parent, "CServerBrowserDialog") {
		Instance = this;

		GameName = "";
		ModDir = "";
		SavedData = null;
		FilterData = null;

		Blacklist = new BlacklistedServers(this);

		LoadUserData();

		InternetGames = new InternetGames(this);
		Favorites = new FavoriteGames(this);
		History = new HistoryGames(this);
		SpectateGames = new SpectateGames(this);
		LanGames = new LanGames(this);
		FriendsGames = new FriendsGames(this);

		SetMinimumSize(640, 384);
		SetSize(640, 384);

		GameList = InternetGames;

		ContextMenu = new ServerContextMenu(this);

		TabPanel = new(this, "GameTabs");
		TabPanel.SetTabWidth(72);
		TabPanel.AddPage(InternetGames, "#ServerBrowser_InternetTab");
		TabPanel.AddPage(Favorites, "#ServerBrowser_FavoritesTab");
		TabPanel.AddPage(History, "#ServerBrowser_HistoryTab");
		TabPanel.AddPage(SpectateGames, "#ServerBrowser_SpectateTab");
		TabPanel.AddPage(LanGames, "#ServerBrowser_LanTab");
		TabPanel.AddPage(FriendsGames, "#ServerBrowser_FriendsTab");
		if (Blacklist != null)
			TabPanel.AddPage(Blacklist, "#ServerBrowser_BlacklistedTab");
		TabPanel.AddActionSignalTarget(this);

		StatusLabel = new Label(this, "StatusLabel", "");

		LoadControlSettings("servers/DialogServerBrowser.res");//anduserconfig

		StatusLabel.SetText("");

		TabPanel.SetActivePage(InternetGames);//todo saveddata

		VGui.AddTickSignal(this);

		// TODO! vguis module loader usually posts this message to us, but until thats done, I'll just do this
		KeyValues kv = new("ActivateGameName");
		kv.SetString("name", "my_cool_game");
		kv.SetString("game", "My Cool Game");
		kv.SetInt("appid", 4000);
		OnMessage(kv, null);
	}

	public void Initialize() {
		SetTitle("#ServerBrowser_Servers", true);
		SetVisible(false);
	}

	public gameserveritem_t GetServer(uint serverID) {
		throw new NotImplementedException();
	}

	public void Open() {
		base.Activate();
		TabPanel.RequestFocus();
		MoveToCenterOfScreen();
	}

	public override void OnTick() {
		base.OnTick();
		GetAnimationController().UpdateAnimations(Sys.Time);
	}

	public void LoadUserData() {
		if (SavedData != null)
			SavedData = null;

		SavedData = new KeyValues("Filters");
		SavedData.LoadFromFile(fileSystem, "ServerBrowser.vdf", "CONFIG");

		KeyValues? filters = SavedData.FindKey("Filters", false);
		if (filters != null) {
			FilterData = filters;
			SavedData.RemoveSubKey(filters);
		}
		else
			FilterData = new KeyValues("Filters");

		if (History != null) {
			History.LoadHistoryList();
			if (IsVisible() && History.IsVisible())
				History.StartRefresh();
		}

		if (Favorites != null) {
			Favorites.LoadFavoritesList();
			ReloadFilterSettings();
			if (IsVisible() && Favorites.IsVisible())
				Favorites.StartRefresh();
		}

		Blacklist?.LoadBlacklistedList();

		InvalidateLayout();
		Repaint();
	}

	void SaveUserData() { }

	void RefreshCurrentPage() { }

	void BlacklistsChanged() { }

	public void UpdateStatusText(ReadOnlySpan<char> text) => StatusLabel?.SetText(text);

	void UpdateStatusText(Span<char> code) { }

	[UiMessage("PageChanged")]
	void OnGameListChanged() { }

	public ServerBrowserDialog? GetInstance() => Instance;

	void AddServerToFavorites(gameserveritem_t server) { }

	void AddServerToBlacklist(gameserveritem_t server) { }

	public bool IsServerBlacklisted(gameserveritem_t server) {
		// todo
		return false;
	}

	public ServerContextMenu GetContextMenu(Panel panel) {
		throw new NotImplementedException();
	}

	public DialogGameInfo JoinGame(IGameList gameList, uint serverIndex) {
		throw new NotImplementedException();
	}

	DialogGameInfo JoinGame(int serverIP, int serverPort, ReadOnlySpan<char> connectCode) {
		throw new NotImplementedException();
	}

	public DialogGameInfo OpenGameInfoDialog(IGameList gameList, uint serverIndex) {
		throw new NotImplementedException();
	}

	DialogGameInfo OpenGameInfoDialog(int serverIP, UInt16 connPort, UInt16 queryPort, ReadOnlySpan<char> connectCode) {
		throw new NotImplementedException();
	}

	void CloseAllGameInfoDialogs() { }

	DialogGameInfo GetDialogGameInfoForFriend(UInt64 ulSteamIDFriend) {
		throw new NotImplementedException();
	}

	public KeyValues GetFilterSaveData(ReadOnlySpan<char> filterSet) => FilterData!.FindKey(filterSet, true)!;

	public ReadOnlySpan<char> GetActiveModName() => ModDir;

	ReadOnlySpan<char> GetActiveGameName() {
		throw new NotImplementedException();
	}

	public CGameID GetActiveAppID() => LimitAppID;

	[UiMessage("ActivateGameName")]
	void OnActiveGameName(KeyValues kv) {
		ModDir = kv.GetString("name", "").ToString();
		GameName = kv.GetString("game", "").ToString();
		LimitAppID = new CGameID((ulong)kv.GetInt("appid", 0));
		ReloadFilterSettings();
	}

	void ReloadFilterSettings() {
		InternetGames.LoadFilterSettings();
		SpectateGames.LoadFilterSettings();
		Favorites.LoadFilterSettings();
		LanGames.LoadFilterSettings();
		FriendsGames.LoadFilterSettings();
		History.LoadFilterSettings();
	}

	[UiMessage("ConnectedToGame")]
	void OnConnectToGame(KeyValues messageValues) { }

	[UiMessage("DisconnectedFromGame")]
	void OnDisconnectFromGame() { }

	[UiMessage("LoadingStarted")]
	void OnLoadingStarted() { }

	public override void ActivateBuildMode() { }

	bool GetDefaultScreenPosition(out int x, out int y, out int wide, out int tall) {
		throw new NotImplementedException();
	}

	public override void OnKeyCodePressed(ButtonCode code) { }
}
