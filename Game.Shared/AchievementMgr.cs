using Source;
#if CLIENT_DLL || GAME_DLL
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.Steam;

using Steamworks;

using static Source.Engine.Client.Steam3ClientAccessor;
#if CLIENT_DLL
using Game.Client;
#endif

namespace Game.Shared;

public class AchievementMgr : AutoGameSystemPerFrame, IGameEventListener2, IAchievementMgr
{
	public const float THINK_CLEAR = -1;

	public enum SteamCloudPersisting
	{
		SteamCloudPersist_Off = 0,
		SteamCloudPersist_On
	}

	struct AchievementThink
	{
		public float ThinkTime;
		public BaseAchievement Achievement;
	}

	readonly SortedDictionary<int, BaseAchievement> MapAchievement = [];
	readonly List<BaseAchievement> Achievement = [];
	readonly List<BaseAchievement> KillEventListeners = [];
	readonly List<BaseAchievement> MapEventListeners = [];
	readonly List<BaseAchievement> ComponentListeners = [];
	readonly SortedDictionary<int, Achievement_AchievedCount> MapMetaAchievement = [];
	readonly List<AchievementThink> ThinkListeners = [];

	float LastClassChangeTime;
	float TeamplayStartTime;
	int MiniroundsCompleted;
	InlineArrayMaxPath<char> Map;
	bool GlobalStateDirty;
	bool SteamDataDirty;
	bool GlobalStateLoaded;
	bool CheatsEverOn;
	double TimeLastSaved;
	bool PersistToSteamCloud;
	readonly List<int> AchievementsAwarded = [];

	Callback<UserStatsReceived_t>? CallbackUserStatsReceived;
	Callback<UserStatsStored_t>? CallbackUserStatsStored;

	bool RegisteredForEvents;

	static void WriteAchievementGlobalState(KeyValues kv, bool persistToSteamCloud = false) {
		ReadOnlySpan<char> filename = "GameState.txt";

		kv.WriteToFile(filesystem, filename);

		if (persistToSteamCloud) {
			ISteamRemoteStorage? remoteStorage = Steam3Client().SteamClient() != null ? Steam3Client().SteamRemoteStorage() : null;

			if (remoteStorage != null) {
				if (remoteStorage.GetQuota(out ulong totalBytes, out ulong availableBytes)) {
					if (totalBytes > 0) {
						int filesize = (int)filesystem.Size(filename);

						if (filesize > 0) {
							byte[] data = new byte[filesize];

							using IFileHandle? handle = filesystem.Open(filename, FileOpenOptions.Read);

							if (handle != null) {
								int read = handle.Stream.Read(data, 0, filesize);
								if (read == filesize)
									remoteStorage.FileWrite(filename, data, filesize);
							}
						}
					}
				}
			}
		}
	}

	public AchievementMgr(SteamCloudPersisting persistToSteamCloud = SteamCloudPersisting.SteamCloudPersist_Off) : base("CAchievementMgr") {
		CallbackUserStatsReceived = Callback<UserStatsReceived_t>.Create(Steam_OnUserStatsReceived);
		CallbackUserStatsStored = Callback<UserStatsStored_t>.Create(Steam_OnUserStatsStored);

		LastClassChangeTime = 0;
		TeamplayStartTime = 0;
		MiniroundsCompleted = 0;
		Map[0] = '\0';
		SteamDataDirty = false;
		GlobalStateDirty = false;
		GlobalStateLoaded = false;
		CheatsEverOn = false;
		TimeLastSaved = 0;

		if (persistToSteamCloud == SteamCloudPersisting.SteamCloudPersist_Off)
			PersistToSteamCloud = false;
		else
			PersistToSteamCloud = true;

		AchievementsAwarded.Clear();
	}

	public void ListenForGameEvent(ReadOnlySpan<char> name) {
		RegisteredForEvents = true;
#if CLIENT_DLL
		bool serverSide = false;
#else
		bool serverSide = true;
#endif
		gameeventmanager?.AddListener(this, name, serverSide);
	}

	public void StopListeningForAllEvents() {
		if (RegisteredForEvents) {
			gameeventmanager?.RemoveListener(this);
			RegisteredForEvents = false;
		}
	}

	public override bool Init() {
#if CLIENT_DLL
		engine.SetAchievementMgr(this);
#else
		// todo: SetAchievementMgr
#endif

#if GAME_DLL
		ListenForGameEvent("entity_killed");
		ListenForGameEvent("game_init");
#else
		ListenForGameEvent("player_death");
		ListenForGameEvent("player_stats_updated");
		Singleton<UserMessages>().HookMessage("AchievementEvent", MsgFunc_AchievementEvent);
#endif

		return true;
	}

	public override void PostInit() {
		ReadOnlySpan<char> gameDir = COM_GetModDirectory();

		BaseAchievementHelper? achievementHelper = BaseAchievementHelper.First;
		while (achievementHelper != null) {
			BaseAchievement achievement = achievementHelper.Create();
			achievement.AchievementMgr = this;
			achievement.Init();
			achievement.CalcProgressMsgIncrement();

			string? gameDirFilter = achievement.GameDirFilter;
			if (gameDirFilter == null || 0 == strcmp(gameDir, gameDirFilter)) {
				MapAchievement[achievement.GetAchievementID()] = achievement;
				if (achievement.IsMetaAchievement())
					MapMetaAchievement[achievement.GetAchievementID()] = (Achievement_AchievedCount)achievement;
			}

			achievementHelper = achievementHelper.Next;
		}

		foreach (BaseAchievement achievement in MapAchievement.Values)
			Achievement.Add(achievement);

		LoadGlobalState();

		DownloadUserData();
	}

	public override void Shutdown() {
		SaveGlobalState(false);

		MapAchievement.Clear();
		MapMetaAchievement.Clear();
		Achievement.Clear();
		KillEventListeners.Clear();
		MapEventListeners.Clear();
		ComponentListeners.Clear();
		AchievementsAwarded.Clear();
		GlobalStateLoaded = false;
	}

	public void InitializeAchievements() {
		Shutdown();
		PostInit();
	}

#if CLIENT_DLL
	static ConVar? sv_cheats;
#endif

#if GAME_DLL
	public override void FrameUpdatePostEntityThink() => Update(0.0f);
#endif

#if CLIENT_DLL
	public override void Update(TimeUnit_t frametime) {
#else
	public void Update(TimeUnit_t frametime) {
#endif
#if CLIENT_DLL
		sv_cheats ??= cvar.FindVar("sv_cheats");
#endif

#if !DEBUG
		if (!WereCheatsEverOn()) {
			if (sv_cheats != null && sv_cheats.GetBool())
				CheatsEverOn = true;
		}
#endif

		int count = ThinkListeners.Count;
		for (int i = count - 1; i >= 0; i--) {
			if (ThinkListeners[i].ThinkTime < gpGlobals.CurTime) {
				ThinkListeners[i].Achievement.Think();

				if (ThinkListeners[i].Achievement.IsAchieved() || ThinkListeners[i].ThinkTime < gpGlobals.CurTime)
					ThinkListeners.RemoveAt(i);
			}
		}

		if (SteamDataDirty)
			UploadUserData();
	}

	public override void LevelInitPreEntity() {
		CheatsEverOn = false;

		EnsureGlobalStateLoaded();

#if GAME_DLL
		Assert(!g_pGameRules.IsMultiplayer());
#else
		Assert(g_pGameRules.IsMultiplayer());
#endif

		KillEventListeners.Clear();
		MapEventListeners.Clear();
		ComponentListeners.Clear();

		AchievementsAwarded.Clear();

		LastClassChangeTime = 0;
		TeamplayStartTime = 0;
		MiniroundsCompleted = 0;

#if CLIENT_DLL
		engine.GetLevelName().FileBase(Map);
#else
		strcpy(Map, gpGlobals.MapName);
#endif

		foreach (BaseAchievement achievement in MapAchievement.Values) {
			string? mapNameFilter = achievement.MapNameFilter;
			if (mapNameFilter != null && 0 != strcmp(Map, mapNameFilter))
				continue;

			if ((achievement.GetFlags() & AchievementFlags.ListenKillEvents) != 0)
				KillEventListeners.Add(achievement);
			if ((achievement.GetFlags() & AchievementFlags.ListenMapEvents) != 0)
				MapEventListeners.Add(achievement);
			if ((achievement.GetFlags() & AchievementFlags.ListenComponentEvents) != 0)
				ComponentListeners.Add(achievement);
			if (achievement.IsActive())
				achievement.ListenForEvents();
		}
	}

	public override void LevelShutdownPreEntity() {
		foreach (BaseAchievement achievement in MapAchievement.Values) {
			if (!achievement.AlwaysListen())
				achievement.StopListeningForAllEvents();
		}

		SaveGlobalStateIfDirty();

		UploadUserData();
	}

	public BaseAchievement? GetAchievementByID(int achievementID) => MapAchievement.TryGetValue(achievementID, out BaseAchievement? achievement) ? achievement : null;
	IAchievement? IAchievementMgr.GetAchievementByID(int id) => GetAchievementByID(id);
	public SortedDictionary<int, BaseAchievement> GetAchievements() => MapAchievement;

	public BaseAchievement? GetAchievementByName(ReadOnlySpan<char> name) {
		foreach (BaseAchievement achievement in MapAchievement.Values) {
			if (achievement != null && 0 == stricmp(name, achievement.GetName()))
				return achievement;
		}
		return null;
	}

	public bool HasAchieved(ReadOnlySpan<char> name) {
		BaseAchievement? achievement = GetAchievementByName(name);
		if (achievement != null)
			return achievement.IsAchieved();
		return false;
	}

	public void DownloadUserData() {
		if (Steam3Client().SteamUserStats() != null)
			Steam3Client().SteamUserStats().RequestCurrentStats();
	}

	static InlineArrayMaxPath<char> modDir;
	static ReadOnlySpan<char> COM_GetModDirectory() {
		if (modDir[0] == '\0') {
			ICommandLine commandLine = Singleton<ICommandLine>();
			string gamedir = commandLine.ParmValue("-game", commandLine.ParmValue("-defaultgamedir", "sdn"));
			strcpy(modDir, gamedir);
			if (!strchr(modDir, '/').IsEmpty || !strchr(modDir, '\\').IsEmpty) {
				StrTools.StripLastDir(modDir);
				nint dirlen = strlen(modDir);
				strcpy(modDir, gamedir.AsSpan((int)dirlen));
			}
		}

		return ((ReadOnlySpan<char>)modDir).SliceNullTerminatedString();
	}

	public void UploadUserData() {
		if (Steam3Client().SteamUserStats() != null) {
			Steam3Client().SteamUserStats().StoreStats();
			SteamDataDirty = false;
		}
	}

	public void LoadGlobalState() {
		ReadOnlySpan<char> filename = "GameState.txt";

		if (PersistToSteamCloud) {
			ISteamRemoteStorage? remoteStorage = Steam3Client().SteamClient() != null ? Steam3Client().SteamRemoteStorage() : null;

			if (remoteStorage != null) {
				if (remoteStorage.FileExists(filename)) {
					int fileSize = remoteStorage.GetFileSize(filename);

					if (fileSize > 0) {
						byte[] data = new byte[fileSize];

						int sizeRead = remoteStorage.FileRead(filename, data, fileSize);

						if (sizeRead == fileSize) {
							using IFileHandle? handle = filesystem.Open(filename, FileOpenOptions.Write);

							handle?.Stream.Write(data, 0, fileSize);
						}
					}
				}
			}
		}

		KeyValues kv = new("GameState");
		if (kv.LoadFromFile(filesystem, filename, "MOD")) {
			KeyValues? node = kv.GetFirstSubKey();
			while (node != null) {
				int achievementID = node.GetInt("id", 0);
				if (achievementID > 0) {
					BaseAchievement? achievement = GetAchievementByID(achievementID);
					achievement?.ApplySettings(node);
				}

				node = node.GetNextKey();
			}

			GlobalStateLoaded = true;
		}
	}

	public void SaveGlobalState(bool async = false) {
		KeyValues kv = new("GameState");
		foreach (BaseAchievement achievement in MapAchievement.Values) {
			if (achievement.ShouldSaveGlobal()) {
				KeyValues node = kv.CreateNewKey();
				node.SetInt("id", achievement.GetAchievementID());

				achievement.GetSettings(node);
			}
		}

		if (!async)
			WriteAchievementGlobalState(kv, PersistToSteamCloud);
		else
			Task.Run(() => WriteAchievementGlobalState(kv));

		TimeLastSaved = Platform.Time;
		GlobalStateDirty = false;
	}

	public void EnsureGlobalStateLoaded() {
		if (!GlobalStateLoaded)
			LoadGlobalState();
	}

	public void SaveGlobalStateIfDirty(bool async = false) {
		if (GlobalStateDirty)
			SaveGlobalState(async);
	}

	public void AwardAchievement(int achievementID) {
		BaseAchievement? achievement = GetAchievementByID(achievementID);
		Assert(achievement != null);
		if (achievement == null)
			return;

		if (!achievement.AlwaysEnabled() && !CheckAchievementsEnabled()) {
			Msg($"Achievements disabled, ignoring achievement unlock for {achievement.GetName()}\n");
			return;
		}

		if (achievement.IsAchieved()) {
			if (BaseAchievement.cc_achievement_debug.GetInt() > 0)
				Msg($"Achievement award called but already achieved: {achievement.GetName()}\n");
			return;
		}
		achievement.SetAchieved(true);

#if CLIENT_DLL
		// todo: gamestats
#endif

		achievement.OnAchieved();

		IGameEvent? ev = gameeventmanager!.CreateEvent("achievement_earned_local");
		if (ev != null) {
			ev.SetInt("achievement", achievement.GetAchievementID());
			gameeventmanager.FireEventClientSide(ev);
		}

		if (BaseAchievement.cc_achievement_debug.GetInt() > 0)
			Msg($"Achievement awarded: {achievement.GetName()}\n");

		SetDirty(true);

		if (Steam3Client().SteamUserStats() != null) {
			bool ret = Steam3Client().SteamUserStats().SetAchievement(achievement.GetName());
			if (ret)
				AchievementsAwarded.Add(achievementID);
		}
	}

	public void UpdateAchievement(int achievementID, int data) {
		BaseAchievement? achievement = GetAchievementByID(achievementID);
		Assert(achievement != null);
		if (achievement == null)
			return;

		if (!achievement.AlwaysEnabled() && !CheckAchievementsEnabled()) {
			Msg($"Achievements disabled, ignoring achievement update for {achievement.GetName()}\n");
			return;
		}

		if (achievement.IsAchieved()) {
			if (BaseAchievement.cc_achievement_debug.GetInt() > 0)
				Msg($"Achievement update called but already achieved: {achievement.GetName()}\n");
			return;
		}

		achievement.UpdateAchievement(data);
	}

	public void PreRestoreSavedGame() {
		EnsureGlobalStateLoaded();

		foreach (BaseAchievement achievement in MapAchievement.Values)
			achievement.PreRestoreSavedGame();
	}

	public void PostRestoreSavedGame() {
		foreach (BaseAchievement achievement in MapAchievement.Values)
			achievement.PostRestoreSavedGame();
	}

	public bool CheckAchievementsEnabled() {
		if (!LoggedIntoSteam()) {
			Msg("Achievements disabled: Steam not running.\n");
			return false;
		}

#if CLIENT_DLL
		if (IsInCommentaryMode()) {
			Msg("Achievements disabled: in commentary mode.\n");
			return false;
		}
#else
		// todo: IsInCommentaryMode
#endif

#if CLIENT_DLL
		if (engine.IsPlayingDemo()) {
			Msg("Achievements disabled: demo playing.\n");
			return false;
		}
#endif

		if (WereCheatsEverOn()) {
			if (developer.GetInt() == 0 || EUniverse.k_EUniverseInvalid == GetUniverse() || EUniverse.k_EUniversePublic == GetUniverse()) {
				Msg("Achievements disabled: cheats turned on in this app session.\n");
				return false;
			}
		}

		return true;
	}

	static EUniverse GetUniverse() => Steam3Client().SteamUtils() != null ? Steam3Client().SteamUtils().GetConnectedUniverse() : EUniverse.k_EUniverseInvalid;

	public void ResetAchievements() {
		if (!LoggedIntoSteam()) {
			Msg("Steam not running, achievements disabled. Cannot reset achievements.\n");
			return;
		}

		foreach (BaseAchievement achievement in MapAchievement.Values)
			ResetAchievement_Internal(achievement);

		if (Steam3Client().SteamUserStats() != null)
			Steam3Client().SteamUserStats().StoreStats();

		if (BaseAchievement.cc_achievement_debug.GetInt() > 0)
			Msg("All achievements reset.\n");
	}

	public void ResetAchievement(int achievementID) {
		if (!LoggedIntoSteam()) {
			Msg("Steam not running, achievements disabled. Cannot reset achievements.\n");
			return;
		}

		BaseAchievement? achievement = GetAchievementByID(achievementID);
		Assert(achievement != null);
		if (achievement != null) {
			ResetAchievement_Internal(achievement);
			if (Steam3Client().SteamUserStats() != null)
				Steam3Client().SteamUserStats().StoreStats();

			if (BaseAchievement.cc_achievement_debug.GetInt() > 0)
				Msg($"Achievement {achievement.GetName()} reset.\n");
		}
	}

	public void PrintAchievementStatus() {
		if (!LoggedIntoSteam()) {
			Msg("Steam not running, achievements disabled. Cannot view or unlock achievements.\n");
			return;
		}

		Msg($"{"Name:",42} {"Status:",-20} Point value:\n");
		int totalAchievements = 0, totalPoints = 0;
		foreach (BaseAchievement achievement in MapAchievement.Values) {
			Msg($"[achiev] {achievement.GetName().ToString(),42} ");

			FailableAchievement? failableAchievement = achievement as FailableAchievement;
			if (achievement.IsAchieved())
				Msg($"{"ACHIEVED",-20}");
			else if (failableAchievement != null && failableAchievement.IsFailed())
				Msg($"{"FAILED",-20}");
			else
				Msg($"{$"({achievement.GetCount()}/{achievement.GetGoal()}){(achievement.IsActive() ? "" : " (inactive)")}",-20}");
			Msg($" {achievement.GetPointValue()}   ");
			achievement.PrintAdditionalStatus();
			Msg("\n");
			totalAchievements++;
			totalPoints += achievement.GetPointValue();
		}
		Msg($"Total achievements: {totalAchievements}  Total possible points: {totalPoints}\n");
	}

	public float GetLastClassChangeTime() => LastClassChangeTime;
	public float GetTeamplayStartTime() => TeamplayStartTime;
	public int GetMiniroundsCompleted() => MiniroundsCompleted;
	public ReadOnlySpan<char> GetMapName() => ((ReadOnlySpan<char>)Map).SliceNullTerminatedString();

	public void SetDirty(bool dirty) {
		if (dirty) {
			GlobalStateDirty = true;
			SteamDataDirty = true;
		}
	}

	public bool LoggedIntoSteam() => Steam3Client().SteamUser() != null && Steam3Client().SteamUserStats() != null && SteamUser.BLoggedOn();
	public double GetTimeLastUpload() => TimeLastSaved;
	public bool WereCheatsEverOn() => CheatsEverOn;

	public void FireGameEvent(IGameEvent ev) {
		ReadOnlySpan<char> name = ev.GetName();
		if (name.IsEmpty)
			return;
		if (0 == strcmp(name, "entity_killed")) {
#if GAME_DLL
			BaseEntity? victim = Util.EntityByIndex(ev.GetInt("entindex_killed", 0));
			BaseEntity? attacker = Util.EntityByIndex(ev.GetInt("entindex_attacker", 0));
			BaseEntity? inflictor = Util.EntityByIndex(ev.GetInt("entindex_inflictor", 0));
			OnKillEvent(victim, attacker, inflictor, ev);
#endif
		}
		else if (0 == strcmp(name, "game_init")) {
#if GAME_DLL
			PreRestoreSavedGame();
			PostRestoreSavedGame();
#endif
		}
#if CLIENT_DLL
		else if (0 == strcmp(name, "player_death")) {
			BaseEntity? victim = cl_entitylist.GetEnt(engine.GetPlayerForUserID(ev.GetInt("userid")));
			BaseEntity? attacker = cl_entitylist.GetEnt(engine.GetPlayerForUserID(ev.GetInt("attacker")));
			OnKillEvent(victim, attacker, null, ev);
		}
		else if (0 == strcmp(name, "localplayer_changeclass"))
			LastClassChangeTime = (float)gpGlobals.CurTime;
		else if (0 == strcmp(name, "localplayer_changeteam")) {
			C_BasePlayer? localPlayer = C_BasePlayer.GetLocalPlayer();
			if (localPlayer != null) {
				int team = localPlayer.GetTeamNumber();
				if (team > Source.Constants.TEAM_SPECTATOR) {
					if (0 == TeamplayStartTime)
						TeamplayStartTime = (float)gpGlobals.CurTime;
				}
				else
					TeamplayStartTime = 0;
			}
		}
		else if (0 == strcmp(name, "teamplay_round_start")) {
			if (ev.GetBool("full_reset"))
				MiniroundsCompleted = 0;
		}
		else if (0 == strcmp(name, "teamplay_round_win")) {
			if (false == ev.GetBool("full_round", true))
				MiniroundsCompleted++;
		}
		else if (0 == strcmp(name, "player_stats_updated")) {
			foreach (BaseAchievement achievement in MapAchievement.Values)
				achievement.OnPlayerStatsUpdate();
		}
#endif
	}

	void OnKillEvent(BaseEntity? victim, BaseEntity? attacker, BaseEntity? inflictor, IGameEvent ev) {
		if (victim == null)
			return;

		bool attackerIsPlayer = false;
		bool victimIsPlayerEnemy = false;
#if GAME_DLL
		if (!g_pGameRules!.IsMultiplayer()) {
			BasePlayer? localPlayer = Util.GetLocalPlayer();
			if (localPlayer != null) {
				if (attacker == localPlayer)
					attackerIsPlayer = true;

				// todo: IRelationType
			}
		}
#else
		C_BasePlayer? localPlayer = C_BasePlayer.GetLocalPlayer();
		// todo: InSameTeam
		if (attacker == localPlayer)
			attackerIsPlayer = true;
#endif

		foreach (BaseAchievement achievement in KillEventListeners) {
			if (!achievement.IsActive())
				continue;

#if CLIENT_DLL
			if (!achievement.LocalPlayerCanEarn())
				continue;
#endif

			if ((achievement.GetFlags() & AchievementFlags.FilterAttackerIsPlayer) != 0 && !attackerIsPlayer)
				continue;

			if ((achievement.GetFlags() & AchievementFlags.FilterVictimIsPlayerEnemy) != 0 && !victimIsPlayerEnemy)
				continue;

#if GAME_DLL
			string? victimClassNameFilter = achievement.VictimClassNameFilter;
			if (victimClassNameFilter != null && !victim.ClassMatches(victimClassNameFilter))
				continue;

			string? inflictorClassNameFilter = achievement.InflictorClassNameFilter;
			if (inflictorClassNameFilter != null && (inflictor == null || !inflictor.ClassMatches(inflictorClassNameFilter)))
				continue;

			string? attackerClassNameFilter = achievement.AttackerClassNameFilter;
			if (attackerClassNameFilter != null && (attacker == null || !attacker.ClassMatches(attackerClassNameFilter)))
				continue;

			string? inflictorEntityNameFilter = achievement.InflictorEntityNameFilter;
			if (inflictorEntityNameFilter != null && (inflictor == null || !inflictor.NameMatches(inflictorEntityNameFilter)))
				continue;
#endif

			achievement.Event_EntityKilled(victim, attacker!, inflictor!, ev);
		}
	}

	public void OnAchievementEvent(int achievementID, int count = 1) {
		if (MapAchievement.Count != 0) {
			BaseAchievement? achievement = GetAchievementByID(achievementID);
			Assert(achievement != null);
			if (achievement != null) {
				if (!achievement.IsAchieved())
					achievement.IncrementCount(count);
#if DEBUG
				else if (BaseAchievement.cc_achievement_debug.GetInt() != 0)
					Msg($"Achievement event ignored for {achievement.GetName()}: already achieved\n");
#endif
			}
		}
	}

	public void OnMapEvent(ReadOnlySpan<char> eventName) {
		Assert(!eventName.IsEmpty);
		if (eventName.IsEmpty)
			return;

		foreach (BaseAchievement achievement in ComponentListeners) {
			Assert(achievement.ComponentPrefix != null);
			if (0 == strncmp(eventName, achievement.ComponentPrefix, achievement.ComponentPrefixLen)) {
				achievement.OnComponentEvent(eventName);
				return;
			}
		}

		foreach (BaseAchievement achievement in MapEventListeners)
			achievement.OnMapEvent(eventName);
	}

	public IAchievement? GetAchievementByIndex(int index) {
		Assert(index >= 0 && index < Achievement.Count);
		return Achievement[index];
	}

	public int GetAchievementCount() => Achievement.Count;

	void Steam_OnUserStatsReceived(UserStatsReceived_t userStatsReceived) {
		Assert(Steam3Client().SteamUserStats() != null);
		if (Steam3Client().SteamUserStats() == null)
			return;

		if (BaseAchievement.cc_achievement_debug.GetInt() > 0)
			Msg($"CAchievementMgr::Steam_OnUserStatsReceived: result = {(int)userStatsReceived.m_eResult}\n");

		if (userStatsReceived.m_eResult != EResult.k_EResultOK) {
			DevMsg($"CTFSteamStats: failed to download stats from Steam, EResult {(int)userStatsReceived.m_eResult}\n");
			return;
		}

		UpdateStateFromSteam_Internal();
	}

	void Steam_OnUserStatsStored(UserStatsStored_t userStatsStored) {
		if (BaseAchievement.cc_achievement_debug.GetInt() > 0)
			Msg($"CAchievementMgr::Steam_OnUserStatsStored: result = {(int)userStatsStored.m_eResult}\n");

		if (EResult.k_EResultOK != userStatsStored.m_eResult && EResult.k_EResultInvalidParam != userStatsStored.m_eResult)
			SetDirty(true);
		else {
			if (EResult.k_EResultInvalidParam == userStatsStored.m_eResult)
				UpdateStateFromSteam_Internal();

			while (AchievementsAwarded.Count > 0) {
#if !GAME_DLL
				if (g_pGameRules != null && g_pGameRules.IsMultiplayer()) {
					C_BasePlayer? localPlayer = C_BasePlayer.GetLocalPlayer();
					if (localPlayer != null) {
						int achievementID = AchievementsAwarded[0];
						BaseAchievement achievement = GetAchievementByID(achievementID)!;

						if (achievement.IsAchieved()) {
							bool ret = Steam3Client().SteamUserStats().GetAchievementAndUnlockTime(achievement.GetName(), out bool achieved, out uint unlockTime);
							if (ret && achieved)
								achievement.SetUnlockTime(unlockTime);

							KeyValues kv = new("AchievementEarned");
							kv.SetInt("achievementID", achievementID);
							engine.ServerCmdKeyValues(kv);
						}
					}
				}
#endif
				AchievementsAwarded.RemoveAt(0);
			}

			CheckMetaAchievements();
		}
	}

	public void CheckMetaAchievements() {
		foreach (Achievement_AchievedCount metaAchievement in MapMetaAchievement.Values) {
			if (metaAchievement == null || metaAchievement.IsAchieved())
				continue;

			int achieved = 0;
			for (int i = metaAchievement.GetLowRange(); i <= metaAchievement.GetHighRange(); i++) {
				BaseAchievement? achievement = GetAchievementByID(i);
				if (achievement != null && achievement.IsAchieved())
					achieved++;
			}
			if (achieved >= metaAchievement.GetNumRequired())
				metaAchievement.IncrementCount();
		}
	}

	void ResetAchievement_Internal(BaseAchievement achievement) {
		Assert(achievement != null);

		if (Steam3Client().SteamUserStats() != null)
			Steam3Client().SteamUserStats().ClearAchievement(achievement.GetName());
		achievement.SetAchieved(false);
		achievement.SetCount(0);
		if (achievement.HasComponents())
			achievement.SetComponentBits(0);
		achievement.SetProgressShown(0);
		achievement.StopListeningForAllEvents();
		if (achievement.IsActive())
			achievement.ListenForEvents();
	}

	public void SetAchievementThink(BaseAchievement achievement, float thinkTime) {
		int count = ThinkListeners.Count;
		for (int i = 0; i < count; i++) {
			if (ThinkListeners[i].Achievement == achievement) {
				if (thinkTime == THINK_CLEAR) {
					ThinkListeners.RemoveAt(i);
					return;
				}

				ThinkListeners[i] = ThinkListeners[i] with { ThinkTime = (float)gpGlobals.CurTime + thinkTime };
				return;
			}
		}

		if (thinkTime == THINK_CLEAR)
			return;

		ThinkListeners.Add(new AchievementThink { Achievement = achievement, ThinkTime = (float)gpGlobals.CurTime + thinkTime });
	}

	void UpdateStateFromSteam_Internal() {
		foreach (BaseAchievement achievement in MapAchievement.Values) {
			bool ret = Steam3Client().SteamUserStats().GetAchievementAndUnlockTime(achievement.GetName(), out bool achieved, out uint unlockTime);

			if (ret) {
				achievement.SetAchieved(achieved);
				achievement.SetUnlockTime(unlockTime);
			}
			else
				DevMsg($"ISteamUserStats::GetAchievement failed for {achievement.GetName()}\n");

			if (achievement.StoreProgressInSteam()) {
				Span<char> progressName = stackalloc char[1024];
				sprintf(progressName, "%s_STAT").S(achievement.GetStat());
				ret = Steam3Client().SteamUserStats().GetStat(progressName.SliceNullTerminatedString(), out int value);
				if (ret) {
					achievement.SetCount(value);
					achievement.EvaluateNewAchievement();
				}
				else
					DevMsg($"ISteamUserStats::GetStat failed to get progress value from Steam for achievement {progressName.SliceNullTerminatedString()}\n");
			}
		}

		IGameEvent? ev = gameeventmanager!.CreateEvent("user_data_downloaded");
		if (ev != null) {
#if GAME_DLL
			gameeventmanager.FireEvent(ev);
#else
			gameeventmanager.FireEventClientSide(ev);
#endif
		}
	}

#if CLIENT_DLL
	// todo: CalcPlayersOnFriendsList
	// todo: CalcHasNumClanPlayers
	// todo: CalcTeammateCount
	// todo: CalcPlayerCount

	static void MsgFunc_AchievementEvent(bf_read msg) {
		int achievementID = msg.ReadShort();
		int count = msg.ReadShort();
		if (engine.GetAchievementMgr() is not AchievementMgr achievementMgr)
			return;
		achievementMgr.OnAchievementEvent(achievementID, count);
	}

#if DEBUG
	[ConCommand("achievement_status", "Shows status of all achievement", FCvar.Cheat)]
	static void achievement_status() {
		if (engine.GetAchievementMgr() is not AchievementMgr achievementMgr)
			return;
		achievementMgr.PrintAchievementStatus();
	}
#endif
#endif
}
#endif
