global using static Game.Server.EngineCallbacks;
global using static Game.Server.GameInterfaceGlobals;

using Game.Server.GarrysMod;
using Game.Shared;

using Microsoft.Extensions.DependencyInjection;

using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.Common.Mathematics;
using Source.Common.Networking;
using Source.Common.Server;

using System.Numerics;

namespace Game.Server;

public static class GameInterfaceGlobals {
	public static readonly ConVar sv_unlockedchapters = new( "sv_unlockedchapters", "1", FCvar.Archive | FCvar.ArchiveXbox);
}

[EngineComponent]
public static class GameInterface
{
	public const int MAX_CHOREO_SCENES_STRING_BITS = 12;
	public const int MAX_CHOREO_SCENES_STRINGS = 1 << MAX_CHOREO_SCENES_STRING_BITS;
	public const int CHOREO_SCENES_INVALID_STRING = MAX_CHOREO_SCENES_STRINGS - 1;

	public const int MAX_MATERIAL_STRING_BITS = 10;
	public const int MAX_MATERIAL_STRINGS = 1 << MAX_MATERIAL_STRING_BITS;

	public static INetworkStringTable? g_pStringTableEffectDispatch;
	public static INetworkStringTable? g_pStringTableClientSideChoreoScenes;
	public static INetworkStringTable? g_pStringTableMaterials;

	public static void PrecacheMaterial(ReadOnlySpan<char> materialName){
		g_pStringTableMaterials!.AddString(true, materialName);
	}

	public static bf_write? g_pMsgBuffer;
	public static void UserMessageBegin(in IRecipientFilter filter, ReadOnlySpan<char> messagename) {
		Assert(g_pMsgBuffer == null);
		Assert(!messagename.IsStringEmpty);

		int msg_type = usermessages.LookupUserMessage(messagename);

		if (msg_type == -1)
			Error($"UserMessageBegin: Unregistered message '{messagename}'\n");

		g_pMsgBuffer = engine.UserMessageBegin(in filter, msg_type);
	}

	public static void EntityMessageBegin(BaseEntity entity, bool reliable) {
		Assert(g_pMsgBuffer == null);
		Assert(entity != null);
		g_pMsgBuffer = engine.EntityMessageBegin(entity.EntIndex(), entity.NetworkProp().GetServerClass(), reliable);
	}

	public static void MessageEnd() {
		Assert(g_pMsgBuffer != null);

		engine.MessageEnd();

		g_pMsgBuffer = null;
	}

	public static void MessageWriteByte(int iValue) {
		if (g_pMsgBuffer == null)
			Error("WRITE_BYTE called with no active message\n");

		g_pMsgBuffer.WriteByte(iValue);
	}

	public static void MessageWriteChar(int iValue) {
		if (g_pMsgBuffer == null)
			Error("WRITE_CHAR called with no active message\n");

		g_pMsgBuffer.WriteChar(iValue);
	}

	public static void MessageWriteShort(int iValue) {
		if (g_pMsgBuffer == null)
			Error("WRITE_SHORT called with no active message\n");

		g_pMsgBuffer.WriteShort(iValue);
	}

	public static void MessageWriteWord(int iValue) {
		if (g_pMsgBuffer == null)
			Error("WRITE_WORD called with no active message\n");

		g_pMsgBuffer.WriteWord(iValue);
	}

	public static void MessageWriteLong(int iValue) {
		if (g_pMsgBuffer == null)
			Error("WriteLong called with no active message\n");

		g_pMsgBuffer.WriteLong(iValue);
	}

	public static void MessageWriteFloat(float flValue) {
		if (g_pMsgBuffer == null)
			Error("WriteFloat called with no active message\n");

		g_pMsgBuffer.WriteFloat(flValue);
	}

	public static void MessageWriteAngle(float flValue) {
		if (g_pMsgBuffer == null)
			Error("WriteAngle called with no active message\n");

		g_pMsgBuffer.WriteBitAngle(flValue, 8);
	}

	public static void MessageWriteCoord(float flValue) {
		if (g_pMsgBuffer == null)
			Error("WriteCoord called with no active message\n");

		g_pMsgBuffer.WriteBitCoord(flValue);
	}

	public static void MessageWriteVec3Coord(in Vector3 rgflValue) {
		if (g_pMsgBuffer == null)
			Error("WriteVec3Coord called with no active message\n");

		g_pMsgBuffer.WriteBitVec3Coord(rgflValue);
	}

	public static void MessageWriteVec3Normal(in Vector3 rgflValue) {
		if (g_pMsgBuffer == null)
			Error("WriteVec3Normal called with no active message\n");

		g_pMsgBuffer.WriteBitVec3Normal(rgflValue);
	}

	public static void MessageWriteAngles(in QAngle rgflValue) {
		if (g_pMsgBuffer == null)
			Error("WriteVec3Normal called with no active message\n");

		g_pMsgBuffer.WriteBitAngles(rgflValue);
	}

	public static void MessageWriteString(ReadOnlySpan<char> sz) {
		if (g_pMsgBuffer == null)
			Error("WriteString called with no active message\n");

		g_pMsgBuffer.WriteString(sz);
	}

	public static void MessageWriteEntity(int iValue) {
		if (g_pMsgBuffer == null)
			Error("WriteEntity called with no active message\n");

		g_pMsgBuffer.WriteShort(iValue);
	}

	static EHANDLE hEnt = new();
	public static void MessageWriteEHandle(BaseEntity? entity) {
		if (g_pMsgBuffer == null)
			Error("WriteEHandle called with no active message\n");

		uint iEncodedEHandle;

		if (entity != null) {
			hEnt.Set(entity);

			int iSerialNum = hEnt.GetSerialNumber() & ((1 << Constants.NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS) - 1);
			iEncodedEHandle = (uint)hEnt.GetEntryIndex() | (uint)(iSerialNum << Constants.MAX_EDICT_BITS);

			hEnt.Set(null);
		}
		else {
			iEncodedEHandle = Constants.INVALID_NETWORKED_EHANDLE_VALUE;
		}

		g_pMsgBuffer.WriteLong((int)iEncodedEHandle);
	}

	// bitwise
	public static void MessageWriteBool(bool bValue) {
		if (g_pMsgBuffer == null)
			Error("WriteBool called with no active message\n");

		g_pMsgBuffer.WriteOneBit(bValue ? 1 : 0);
	}

	public static void MessageWriteUBitLong(uint data, int numbits) {
		if (g_pMsgBuffer == null)
			Error("WriteUBitLong called with no active message\n");

		g_pMsgBuffer.WriteUBitLong(data, numbits);
	}

	public static void MessageWriteSBitLong(int data, int numbits) {
		if (g_pMsgBuffer == null)
			Error("WriteSBitLong called with no active message\n");

		g_pMsgBuffer.WriteSBitLong(data, numbits);
	}

	public static void MessageWriteBits(ReadOnlySpan<byte> pIn, int nBits) {
		if (g_pMsgBuffer == null)
			Error("WriteBits called with no active message\n");

		g_pMsgBuffer.WriteBits(pIn, nBits);
	}
}

public class ServerGameDLL(IFileSystem filesystem, ICommandLine CommandLine) : IServerGameDLL
{
#if GMOD_DLL
	static readonly ConVar host_workshop_collection = new("host_workshop_collection", "", 0, "Set to a Steam Workshop Collection ID, and the dedicated server will download Workshop items from given collection on start up.");
	static readonly ConVar host_workshop_autoupdate = new("host_workshop_autoupdate", "1", 0, "If set to above 0, will auto update addons from host_workshop_collection on server start.");
#endif


	public TimeUnit_t AutoSaveDangerousTime;
	public float AutoSaveDangerousMinHealthToCommit;

	public static void DLLInit(IServiceCollection services) {
		services.AddSingleton<IServerGameEnts, ServerGameEnts>();
		services.AddSingleton<IServerGameClients, ServerGameClients>();

		services.AddSingleton<GlobalEntityList>();
		services.AddSingleton<BaseEntityList>(x => x.GetRequiredService<GlobalEntityList>());
	}

	public PrepareLevelResourcesResult AsyncPrepareLevelResources(Span<char> mapName, Span<char> mapFile, out float progress) {
		throw new NotImplementedException();
	}

	public void BuildAdjacentMapList() {
		throw new NotImplementedException();
	}

	public void CreateNetworkStringTables() {
		// throw new NotImplementedException();

		g_pStringTableEffectDispatch = networkstringtable.CreateStringTable("EffectDispatch", EffectData.MAX_EFFECT_DISPATCH_STRINGS);
		g_pStringTableClientSideChoreoScenes = networkstringtable.CreateStringTable("Scenes", MAX_CHOREO_SCENES_STRINGS);
		g_pStringTableMaterials = networkstringtable.CreateStringTable("Materials", MAX_MATERIAL_STRINGS);

		GameRulesRegister.CreateNetworkStringTables_GameRules();

#if GMOD_DLL
		// TODO: GarrysMod::StringTable::Create
		Game.Server.GarrysMod.NetworkString.Create();
		Game.Server.GarrysMod.NetworkVarNames.Create();
		Game.Server.GarrysMod.GModDataPack.DataPack().Initialize();

		StringTableBits.SV_SetupNetworkStringTableBits();
#endif
	}

	public bool DLLInit(IServiceProvider services) {
		BaseEdict.GetChangeAccessor += x => engine.GetChangeAccessor((Edict)x); // Kind of a hack, but this is defined in gameinterface.cpp like this...
		g_SharedChangeInfo = engine.GetSharedEdictChangeInfo();

		gameeventmanager.LoadEventsFromFile("resource/gameevents.res");

		System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(SoundscapeSystemGlobals).TypeHandle);
		System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(CheckClient).TypeHandle);
		System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(AI_SystemHook).TypeHandle);
		System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(FlexSceneFileManager).TypeHandle);
		System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(LagCompensationGlobals).TypeHandle);

#if GMOD_DLL
		if (CommandLine.FindParm("-noaddons") == 0)
			filesystem.LegacyAddons().Refresh();
		else
			Msg("Game is ran with -noaddons, not loading legacy/folder addons!\n");

		if (CommandLine.FindParm("-noworkshop") == 0)
			filesystem.Addons().ScanForSubscriptions(host_workshop_collection.GetString(), host_workshop_autoupdate.GetInt() != 0);

		filesystem.CreateDirHierarchy("addons/", "MOD");
		filesystem.CreateDirHierarchy("data/", "MOD");
		filesystem.Addons().Refresh();
		filesystem.Games().Refresh();
#endif


#if GMOD_DLL
		garrysmod.InitializeMod(services);
#endif

		IGameSystem.Add(PhysicsGameSystem());
		IGameSystem.Add(g_SoundEmitterSystem);

		if (!IGameSystem.InitAllSystems())
			return false;

		NavMesh.NavMesh.Instance = new();

		return true;
	}

	public void DLLShutdown() {
		throw new NotImplementedException();
	}

	public void GameFrame(bool simulating) {
		if (BaseEntity.IsSimulatingOnAlternateTicks()) {
			if ((gpGlobals.TickCount & 1) != 0) {
				PlayerLocalData.UpdateAllClientData();
				return;
			}

			gpGlobals.FrameTime *= 2.0f;
		}

		TimeUnit_t oldFrameTime = gpGlobals.FrameTime;

		gEntList.CleanupDeleteList();

		IGameSystem.FrameUpdatePreEntityThinkAllSystems();

		GameStartFrame();

		NavMesh.NavMesh.Instance?.Update();

		// nextbots todo

		// UpdateQueryCache();

		Physics.RunThinkFunctions(simulating);

		IGameSystem.FrameUpdatePostEntityThinkAllSystems();

		ServiceEventQueue();

		PlayerLocalData.UpdateAllClientData();

		// g_pGameRules?.EndGameFrame();

		// g_NetworkPropertyEventMgr.FireEvents();

		gpGlobals.FrameTime = oldFrameTime;
	}

	public bool GameInit() {
		ResetGlobalState();
		engine.ServerCommand("exec game.cfg\n");
		engine.ServerExecute();
		BaseEntity.AccurateTriggerBboxChecks = true;

		IGameEvent? ev = gameeventmanager.CreateEvent("game_init");
		if (ev != null)
			gameeventmanager.FireEvent(ev);

		return true;
	}

	public void GameServerSteamAPIActivated() {

	}

	public void GameServerSteamAPIShutdown() {

	}

	public void GameShutdown() {
		ResetGlobalState();
	}

	public ServerClass? GetAllServerClasses() {
		return ServerClass.Head;
	}

	public ReadOnlySpan<char> GetGameDescription() {
#if GMOD_DLL
		return "Garry's Mod";
#else
		return "Half-Life 2";
#endif
	}

	public ReadOnlySpan<char> GetServerBrowserGameData() {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetServerBrowserMapOverride() {
		throw new NotImplementedException();
	}

	public TimeUnit_t GetTickInterval() {
		TimeUnit_t tickinterval = Constants.DEFAULT_TICK_INTERVAL;

		if (CommandLine.CheckParm("-tickrate")) {
			double tickrate = CommandLine.ParmValue("-tickrate", 0d);
			if (tickrate > 10)
				tickinterval = 1.0 / tickrate;
		}

		return tickinterval;
	}

	public bool GetUserMessageInfo(int msg_type, Span<char> name, out int size) {
		if (!usermessages.IsValidIndex(msg_type)) {
			size = 0;
			return false;
		}

		strcpy(name, usermessages.GetUserMessageName(msg_type));
		size = usermessages.GetUserMessageSize(msg_type);
		return true;
	}

	public bool GMOD_CheckPassword(Steamworks.CSteamID steamID, ReadOnlySpan<char> ipAddress, ReadOnlySpan<char> serverPassword, ReadOnlySpan<char> clientPassword, ReadOnlySpan<char> name, Span<char> rejectionMessage) {
		throw new NotImplementedException();
	}

	public void GMOD_ClientSignOnStateChanged(int userID, int oldState, int newState) {
		throw new NotImplementedException();
	}

	public void GMOD_OnAllSoundsStoppedSV() {
		throw new NotImplementedException();
	}

	public void GMOD_OnStringTablesRemoved() {
		// todo: g_pStringTableParticleEffectNames, g_pStringTableEffectDispatch, g_pStringTableVguiScreen, g_pStringTableMaterials, g_pStringTableClientSideChoreoScenes = null
		GameRulesRegister.ResetNetworkStringTables_GameRules();
#if GMOD_DLL
		Game.Server.GarrysMod.NetworkString.Reset();
		Game.Server.GarrysMod.NetworkVarNames.Reset();
		Game.Server.GarrysMod.GModDataPack.DataPack().Reset();
#endif
	}

	public void InvalidateMdlCache() {
		throw new NotImplementedException();
	}

	public bool LevelInit(ReadOnlySpan<char> pMapName, ReadOnlyMemory<byte> pMapEntities, ReadOnlySpan<char> pOldLevel, ReadOnlySpan<char> pLandmarkName, bool loadGame, bool background) {
		// ResetWindspeed();
#if GMOD_DLL
		garrysmod.LevelInit(pMapName, pMapEntities, pOldLevel, pLandmarkName, loadGame, background);
#endif
		// UpdateChapterRestrictions(pMapName);


		//Tony; parse custom manifest if exists!
		// ParseParticleEffectsMap(pMapName, false);

		// IGameSystem::LevelInitPreEntityAllSystems() is called when the world is precached
		// That happens either in LoadGameState() or in MapEntity_ParseAllEntities()
		if (loadGame) {
			if (!pOldLevel.IsEmpty)
				gpGlobals.LoadType = MapLoadType.Transition;
			else
				gpGlobals.LoadType = MapLoadType.LoadGame;

			// BeginRestoreEntities();
			if (!engine.LoadGameState(pMapName, true)) {
				if (!pOldLevel.IsEmpty)
					MapEntity_ParseAllEntities(pMapEntities);
				else
					// Regular save load case
					return false;
			}

			if (!pOldLevel.IsEmpty)
				engine.LoadAdjacentEnts(pOldLevel, pLandmarkName);

			// if (g_OneWayTransition)
			// 	engine.ClearSaveDirAfterClientLoad();

			// if (pOldLevel && sv_autosave.GetBool() == true) {
			// 	// This is a single-player style level transition.
			// 	// Queue up an autosave one second into the level
			// 	BaseEntity? pAutosave = BaseEntity::Create("logic_autosave", vec3_origin, vec3_angle, NULL);
			// 	if (pAutosave != null) {
			// 		g_EventQueue.AddEvent(pAutosave, "Save", 1.0, NULL, NULL);
			// 		g_EventQueue.AddEvent(pAutosave, "Kill", 1.1, NULL, NULL);
			// 	}
			// }
		}
		else {
			if (background)
				gpGlobals.LoadType = MapLoadType.Background;

			else
				gpGlobals.LoadType = MapLoadType.NewGame;

			// Clear out entity references, and parse the entities into it.
			// g_MapEntityRefs.Purge();
			MapLoadEntityFilter filter = new();
			MapEntity_ParseAllEntities(pMapEntities, filter);

			// g_pServerBenchmark.StartBenchmark();

			// Now call the mod specific parse
			// LevelInit_ParseAllEntities(pMapEntities);
		}

		// Check low violence settings for this map
		// g_RagdollLVManager.SetLowViolence(pMapName);

		// Now that all of the active entities have been loaded in, precache any entities who need point_template parameters
		//  to be parsed (the above code has loaded all point_template entities)
		// PrecachePointTemplates();

		// load MOTD from file into stringtable
		// LoadMessageOfTheDay();

		// Sometimes an ent will Remove() itself during its precache, so RemoveImmediate won't happen.
		// This makes sure those ents get cleaned up.
		gEntList.CleanupDeleteList();

		// g_AIFriendliesTalkSemaphore.Release();
		// g_AIFoesTalkSemaphore.Release();
		// g_OneWayTransition = false;

		// clear any pending autosavedangerous
		AutoSaveDangerousTime = 0.0f;
		AutoSaveDangerousMinHealthToCommit = 0.0f;
		return true;
	}

	public void LevelShutdown() {
		IGameSystem.LevelShutdownPreClearSteamAPIContextAllSystems();
		// steamgameserverapicontext.Clear();

		IGameSystem.LevelShutdownPreEntityAllSystems();

		SoundEnt.ShutdownSoundEnt();

		gEntList.Clear();

		// InvalidateQueryCache();

		IGameSystem.LevelShutdownPostEntityAllSystems();

		NavMesh.NavMesh.Instance!.Reset();
	}

	public void PostInit() {
		IGameSystem.PostInitAllSystems();
	}

	public void PreClientUpdate(bool simulating) {
		if (!simulating)
			return;

		IGameSystem.PreClientUpdateAllSystems();
	}

	public void PrepareLevelResources(Span<char> mapName, Span<char> mapFile) {

	}

	public void PreSaveGameLoaded(ReadOnlySpan<char> pSaveName, bool bCurrentlyInGame) {
		throw new NotImplementedException();
	}

	public void ServerActivate(Edict[] pEdictList, int edictCount, int clientMax) {
		// if (InRestore)
		// 	return;

		if (gEntList.ResetDeleteList() != 0)
			Msg("ERROR: Entity delete queue not empty on level start!\n");

		for (BaseEntity? ent = gEntList.FirstEnt(); ent != null; ent = gEntList.NextEnt(ent)) {
			if (ent != null && !ent.IsDormant())
				ent.Activate();
		}

		IGameSystem.LevelInitPostEntityAllSystems();
		BaseEntity.SetAllowPrecache(false);

		NavMesh.NavMesh.Instance.Load();
		NavMesh.NavMesh.Instance.OnServerActivate();

		// todo nextbots
	}

	public void SetServerHibernation(bool bHibernating) {
		throw new NotImplementedException();
	}

	public bool ShouldHideServer() => false;

	public void Think(bool finalTick) {
#if GMOD_DLL
		filesystem.Addons().Think();
#endif
	}

	public StandardSendProxies GetStandardSendProxies() => StandardSendProxies.g_StandardSendProxies;
}


public class ServerGameClients : IServerGameClients
{
	const int CMD_MAXBACKUP = 64;

	public void ClientActive(Edict entity, bool loadGame) {
		GMODClient.ClientActive(entity, loadGame);

		if (gpGlobals.LoadType != MapLoadType.LoadGame) {
			// notify all entities that the player is now in the game
			for (BaseEntity? ent = gEntList.FirstEnt(); ent != null; ent = gEntList.NextEnt(ent))
				ent.PostClientActive();
		}

		BasePlayer player = (BasePlayer)BaseEntity.Instance(entity)!;
		// CSoundEnvelopeController::GetController().CheckLoopingSoundsForPlayer(pPlayer);
		// SceneManager_ClientActive(pPlayer);
	}

	public void ClientCommand(Edict entity, in TokenizedCommand args) {
		BasePlayer? player = ToBasePlayer(BaseEntity.GetContainingEntity(entity));
		GameServerClientGlobals.ClientCommand(player, args);
	}

	public void ClientCommandKeyValues(Edict entity, KeyValues keyValues) {
		if (keyValues == null)
			return;

		g_pGameRules?.ClientCommandKeyValues(entity, keyValues);
	}

	public bool ClientConnect(Edict entity, ReadOnlySpan<char> name, ReadOnlySpan<char> address, Span<char> reject) {
		if (g_pGameRules == null)
			return false;

		return g_pGameRules.ClientConnected(entity, name, address, reject);
	}

	public void ClientDisconnect(Edict entity) {
		throw new NotImplementedException();
	}

	public void ClientEarPosition(Edict entity, out Vector3 earOrigin) {
		BasePlayer? player = (BasePlayer?)BaseEntity.Instance(entity);
		if (player != null)
			earOrigin = player.EarPosition();
		else {
			Assert(false);
			earOrigin = vec3_origin;
		}
	}

	public void ClientPutInServer(Edict entity, ReadOnlySpan<char> playerName) {
		// throw new NotImplementedException();
		GMODClient.ClientPutInServer(entity, playerName);
	}

	static ConVar? MinUpdateRate;
	static ConVar? MaxUpdateRate;
	static ConVar? MinInterpRatio;
	static ConVar? MaxInterpRatio;

	public void ClientSettingsChanged(Edict edict) {
		if (edict.GetUnknown() == null)
			return;

		if (BaseEntity.Instance(edict) is not BasePlayer player)
			return;

		int index = player.EntIndex();

		player.UpdateRate = atoi(engine.GetClientConVarValue(index, "cl_updaterate"));
		MinUpdateRate ??= cvar.FindVar("sv_minupdaterate");
		MaxUpdateRate ??= cvar.FindVar("sv_maxupdaterate");
		if (MinUpdateRate != null && MaxUpdateRate != null)
			player.UpdateRate = Math.Clamp(player.UpdateRate, (int)MinUpdateRate.GetFloat(), (int)MaxUpdateRate.GetFloat());

		bool useInterpolation = atoi(engine.GetClientConVarValue(index, "cl_interpolate")) != 0;
		if (useInterpolation) {
			double lerpRatio = atof(engine.GetClientConVarValue(index, "cl_interp_ratio"));
			if (lerpRatio == 0)
				lerpRatio = 1.0;
			double lerpAmount = atof(engine.GetClientConVarValue(index, "cl_interp"));

			MinInterpRatio ??= cvar.FindVar("sv_client_min_interp_ratio");
			MaxInterpRatio ??= cvar.FindVar("sv_client_max_interp_ratio");
			if (MinInterpRatio != null && MaxInterpRatio != null && MinInterpRatio.GetFloat() != -1)
				lerpRatio = Math.Clamp(lerpRatio, MinInterpRatio.GetFloat(), MaxInterpRatio.GetFloat());
			else if (lerpRatio == 0)
				lerpRatio = 1.0;

			player.LerpTime = Math.Max(lerpAmount, lerpRatio / player.UpdateRate);
		}
		else
			player.LerpTime = 0.0;

		bool usePrediction = atoi(engine.GetClientConVarValue(index, "cl_predict")) != 0;
		if (usePrediction) {
			player.PredictWeapons = atoi(engine.GetClientConVarValue(index, "cl_predictweapons")) != 0;
			player.LagCompensation = atoi(engine.GetClientConVarValue(index, "cl_lagcompensation")) != 0;
		}
		else {
			player.PredictWeapons = false;
			player.LagCompensation = false;
		}

		g_pGameRules.ClientSettingsChanged(player);
	}

	public void ClientSetupVisibility(Edict? viewEntity, Edict client, byte[] pvs, int pvssize) {
		Vector3 org = default;

		engine.ResetPVS(pvs, pvssize);

		BaseEntity? ve = null;
		if (viewEntity != null) {
			ve = BaseEntity.GetContainingEntity(viewEntity);
			if (ve != null) {
				org = ve.EyePosition();
				engine.AddOriginToPVS(org);
			}
		}

		float fovDistanceAdjustFactor = 1;

		BasePlayer? player = BaseEntity.GetContainingEntity(client) as BasePlayer;
		if (player != null) {
			org = player.EyePosition();
			player.SetupVisibility(ve, pvs, pvssize);
			Util.SetClientVisibilityPVS(client, pvs.AsSpan(0, pvssize));
			fovDistanceAdjustFactor = player.GetFOVDistanceAdjustFactorForNetworking();
		}

		Span<byte> portalBits = stackalloc byte[Constants.MAX_AREA_PORTAL_STATE_BYTES];
		portalBits.Clear();

		Span<int> portalNums = stackalloc int[512];
		Span<int> isOpen = stackalloc int[512];
		int outPortal = 0;

		foreach (FuncAreaPortalBase cur in FuncAreaPortalBase.g_AreaPortals) {
			bool isOpenOnClient = true;

			portalNums[outPortal] = cur.PortalNumber;
			isOpen[outPortal] = cur.UpdateVisibility(org, fovDistanceAdjustFactor, ref isOpenOnClient) ? 1 : 0;

			++outPortal;
			if (outPortal >= portalNums.Length) {
				engine.SetAreaPortalStates(portalNums, isOpen);
				outPortal = 0;
			}

			if (cur.PortalVersion == 0)
				isOpenOnClient = true;

			if (isOpenOnClient) {
				if (cur.PortalNumber < 0)
					continue;
				else if (cur.PortalNumber >= portalBits.Length * 8)
					Error($"ClientSetupVisibility: portal number ({cur.PortalNumber}) too large");
				else
					portalBits[cur.PortalNumber >> 3] |= (byte)(1 << (cur.PortalNumber & 7));
			}
		}

		engine.SetAreaPortalStates(portalNums[..outPortal], isOpen[..outPortal]);

		if (player != null)
			player.Local.UpdateAreaBits(player, portalBits);
	}

	public void ClientSpawned(Edict player) => g_pGameRules?.ClientSpawned(player);

	public void GetBugReportInfo(Span<char> buf) {
		throw new NotImplementedException();
	}

	public void GetPlayerLimits(out int minPlayers, out int maxPlayers, out int defaultMaxPlayers) {
		minPlayers = defaultMaxPlayers = 1;
		maxPlayers = Constants.MAX_PLAYERS;
	}

	public PlayerState? GetPlayerState(Edict player) {
		if (player == null || player.GetUnknown() == null)
			return null;

		BasePlayer? pl = BaseEntity.Instance(player) as BasePlayer;
		return pl?.pl;
	}

	public void NetworkIDValidated(ReadOnlySpan<char> userName, ReadOnlySpan<char> networkID) {
		throw new NotImplementedException();
	}

	static readonly UserCmd[] ProcessUsercmdsBuffer = new UserCmd[CMD_MAXBACKUP];
	public TimeUnit_t ProcessUsercmds(Edict player, bf_read buf, int numCmds, int totalCmds, int droppedPackets, bool ignore, bool paused) {
		int i;

		UserCmd from, to;

		UserCmd[] cmds = ProcessUsercmdsBuffer;

		UserCmd cmdNull = new();

		Assert(numCmds >= 0);
		Assert((totalCmds - numCmds) >= 0);

		BasePlayer? pl = null;
		BaseEntity? ent = BaseEntity.Instance(player);

		if (ent != null && ent.IsPlayer())
			pl = (BasePlayer)ent;

		if (totalCmds < 0 || totalCmds >= (CMD_MAXBACKUP - 1)) {
			ReadOnlySpan<char> name = "unknown";
			if (pl != null)
				name = pl.GetPlayerName();

			Msg($"CBasePlayer::ProcessUsercmds: too many cmds {totalCmds} sent for player {name}\n");
			buf.SetOverflowFlag();
			return 0.0f;
		}

		cmdNull.Reset();
		from = cmdNull;

		for (i = totalCmds - 1; i >= 0; i--) {
			UserCmd.ReadUsercmd(buf, ref cmds[i], ref from);
			from = cmds[i];
		}

		if (ignore || pl == null)
			return 0.0f;

		pl.ProcessUsercmds(cmds, numCmds, totalCmds, droppedPackets, paused);

		return TICK_INTERVAL;
	}

	public static int CommandClientIndex = 0;
	public void SetCommandClient(int index) => CommandClientIndex = index;

	public void GMOD_SentClientStringTables(int userID) => Game.Server.GarrysMod.GModDataPack.DataPack().SendFileRequestRequest(userID);

	public void GMOD_ReceiveClientMessage(int userID, Edict player, bf_read msg, int bits) {
		int type = msg.ReadByte();
		if (bits <= 7 || type > 5) {
			DevMsg($"Blocking invalid GMod packet - Length: {bits} Type: {type}\n");
			return;
		}

		int dataBits = bits - 8;
		if (type == (int)GModMessageType.LuaFile) {
			Game.Server.GarrysMod.GModDataPack.DataPack().OnFilesRequested(userID, msg, dataBits);
			return;
		}

		if (type == (int)GModMessageType.LuaAutoRefresh)
			return;

		BaseEntity? ent = BaseEntity.GetContainingEntity(player ?? engine.PEntityOfEntIndex(0));
		if (ent == null)
			return;

		if (type == (int)GModMessageType.LuaError) {
			msg.ReadString(out string? error, 0x1000);
			// todo: HandleClientLuaError(ToBasePlayer(ent), error);
			return;
		}

		if (type != (int)GModMessageType.NetMessage)
			return;

		int curBit = msg.BitsRead;
		int bitOffset = curBit % 8;
		int numBits = dataBits + bitOffset;
		byte[] data = new byte[Protocol.Bits2Bytes(numBits)];
		ReadOnlySpan<byte> source = msg.BaseArray.AsSpan(curBit / 8);
		source[..Math.Min(source.Length, data.Length)].CopyTo(data);

		bf_read read = new("NetMessage(read_sv)", data, data.Length, numBits);
		read.Seek(bitOffset);
		LuaNet.g_NetIncoming = read;

		if (g_Lua != null && g_Lua.Global() != null) {
			LuaObject net = new();
			g_Lua.Global().GetMember("net", net);
			if (net.isTable()) {
				LuaObject incoming = new();
				net.GetMember("Incoming", incoming);
				if (incoming.isFunction()) {
					incoming.Push();
					g_Lua.PushNumber(dataBits);
					LuaEntity.Push_Entity(ent);
					g_Lua.CallInternalNoReturns(2);
				}
				incoming.UnReference();
			}
			net.UnReference();
		}

		LuaNet.g_NetIncoming = null;
	}

	public void GMOD_ClientConnected(int userID) => Game.Server.GarrysMod.GModDataPack.DataPack().OnClientConnected(userID);
}

public class ServerGameEnts : IServerGameEnts
{
	public void FreeContainingEntity(Edict e) => MapEntities.FreeContainingEntity(e);

	public void MarkEntitiesAsTouching(Edict e1, Edict e2) {
		BaseEntity? entity = BaseEntity.GetContainingEntity(e1);
		BaseEntity? entityTouched = BaseEntity.GetContainingEntity(e2);
		if (entity != null && entityTouched != null) {
			// HACKHACK: UNDONE: Pass in the trace here??!?!?
			Trace tr = default;
			Util.ClearTrace(ref tr);
			tr.EndPos = (entity.GetAbsOrigin() + entityTouched.GetAbsOrigin()) * 0.5f;
			entity.PhysicsMarkEntitiesAsTouching(entityTouched, tr);
		}
	}

	public void SetDebugEdictBase(Edict[] edict) {

	}

	static readonly ConVar sv_force_transmit_ents = new("sv_force_transmit_ents", "0", FCvar.Cheat | FCvar.DevelopmentOnly, "Will transmit all entities to client, regardless of PVS conditions (will still skip based on transmit flags, however).");

	public void CheckTransmit(CheckTransmitInfo info, ushort[] edictIndices, int edicts) {
		// NOTE: for speed's sake, this assumes that all networkables are CBaseEntities and that the edict list
		// is consecutive in memory. If either of these things change, then this routine needs to change, but
		// ideally we won't be calling any virtual from this routine. This speedy routine was added as an
		// optimization which would be nice to keep.
		Edict baseEdict = engine.PEntityOfEntIndex(0);

		// get recipient player's skybox:
		BaseEntity? recipientEntity = BaseEntity.Instance(info.ClientEnt);

		Assert(recipientEntity != null && recipientEntity.IsPlayer());
		if (recipientEntity == null)
			return;

		BasePlayer recipientPlayer = (BasePlayer)recipientEntity;
		int skyBoxArea = recipientPlayer.Local.Skybox3D.Area;

		bool isHLTV = false;//recipientPlayer.IsHLTV();
		bool isReplay = false;//recipientPlayer.IsReplay();

		for (int i = 0; i < edicts; i++) {
			int entIndex = edictIndices[i];

			Edict edict = engine.PEntityOfEntIndex(entIndex)!;
			Assert(edict == engine.PEntityOfEntIndex(entIndex));
			EdictFlags flags = edict.StateFlags & (EdictFlags.DontSend | EdictFlags.Always | EdictFlags.PVSCheck | EdictFlags.FullCheck);

			// entity needs no transmit
			if ((flags & EdictFlags.DontSend) != 0)
				continue;

			// entity is already marked for sending
			if (info.TransmitEdict.Get(entIndex) != 0)
				continue;

			if ((flags & EdictFlags.Always) != 0) {
				// S-FIXME: Hey! Shouldn't this be using SetTransmit so as
				// to also force network down dependent entities?
				while (edict != null) {
					// mark entity for sending
					info.TransmitEdict.Set(entIndex);

					if (isHLTV || isReplay)
						info.TransmitAlways.Set(entIndex);

					ServerNetworkProperty? ent = (ServerNetworkProperty?)edict.GetNetworkable();
					if (ent == null)
						break;

					ServerNetworkProperty pParent = ent.GetNetworkParent();
					if (pParent == null)
						break;

					edict = pParent.Edict();
					entIndex = pParent.EntIndex();
				}
				continue;
			}

			// S-FIXME: Would like to remove all dependencies
			BaseEntity entity = (BaseEntity)edict.GetUnknown()!;
			Assert((BaseEntity?)edict.GetUnknown() == entity);

			if (flags == EdictFlags.FullCheck) {
				// do a full ShouldTransmit() check, may return FL_EDICT_CHECKPVS
				flags = entity.ShouldTransmit(info);

				Assert((flags & EdictFlags.FullCheck) == 0);

				if ((flags & EdictFlags.Always) != 0) {
					entity.SetTransmit(info, true);
					continue;
				}
			}

			// don't send this entity
			if ((flags & EdictFlags.PVSCheck) == 0)
				continue;

			ServerNetworkProperty netProp = (ServerNetworkProperty)edict.GetNetworkable()!;

			if (isHLTV || isReplay) {
				// for the HLTV/Replay we don't cull against PVS
				entity.SetTransmit(info, netProp.AreaNum() == skyBoxArea);
				continue;
			}

			// Always send entities in the player's 3d skybox.
			// Sidenote: call of AreaNum() ensures that PVS data is up to date for this entity
			bool sameAreasAsSky = netProp.AreaNum() == skyBoxArea;
			if (sameAreasAsSky) {
				entity.SetTransmit(info, true);
				continue;
			}

			bool inPVS = netProp.IsInPVS(info);
			if (inPVS || sv_force_transmit_ents.GetBool()) {
				// only send if entity is in PVS
				entity.SetTransmit(info, false);
				continue;
			}

			// If the entity is marked "check PVS" but it's in hierarchy, walk up the hierarchy looking for the
			//  for any parent which is also in the PVS.  If none are found, then we don't need to worry about sending ourself
			BaseEntity orig = entity;
			ServerNetworkProperty check = netProp.GetNetworkParent();

			// BUG BUG:  I think it might be better to build up a list of edict indices which "depend" on other answers and then
			// resolve them in a second pass.  Not sure what happens if an entity has two parents who both request PVS check?
			while (check != null) {
				int checkIndex = check.EntIndex();

				// Parent already being sent
				if (info.TransmitEdict.Get(checkIndex) != 0) {
					orig.SetTransmit(info, true);
					break;
				}

				Edict checkEdict = check.Edict();
				EdictFlags checkFlags = checkEdict.StateFlags & (EdictFlags.DontSend | EdictFlags.Always | EdictFlags.PVSCheck | EdictFlags.FullCheck);
				if ((checkFlags & EdictFlags.DontSend) != 0)
					break;

				if ((checkFlags & EdictFlags.Always) != 0) {
					orig.SetTransmit(info, true);
					break;
				}

				if (checkFlags == EdictFlags.FullCheck) {
					// do a full ShouldTransmit() check, may return FL_EDICT_CHECKPVS
					BaseEntity checkEnt = check.GetBaseEntity()!;
					flags = checkEnt.ShouldTransmit(info);
					if ((flags & EdictFlags.Always) != 0) {
						checkEnt.SetTransmit(info, true);
						orig.SetTransmit(info, true);
					}
					break;
				}

				if ((checkFlags & EdictFlags.PVSCheck) != 0) {
					// Check pvs
					check.RecomputePVSInformation();
					bool moveParentInPVS = check.IsInPVS(info);
					if (moveParentInPVS) {
						orig.SetTransmit(info, true);
						break;
					}
				}

				// Continue up chain just in case the parent itself has a parent that's in the PVS...
				check = check.GetNetworkParent();
			}
		}
	}
}

struct MapEntityRef
{
	/// <summary>Which edict slot this entity got. -1 if CreateEntityByName failed.</summary>
	public int Edict;
	/// <summary>The edict serial number.</summary>
	public int SerialNumber;
};


class MapLoadEntityFilter : IMapEntityFilter
{
	public bool ShouldCreateEntity(ReadOnlySpan<char> className) => true;

	public BaseEntity? CreateNextEntity(ReadOnlySpan<char> className) {
		BaseEntity? ret = CreateEntityByName(className);
		MapEntityRef entref = new() {
			Edict = -1,
			SerialNumber = 0
		};

		if (ret != null) {
			entref.Edict = ret.EntIndex();
			if (ret.Edict() != null)
				entref.SerialNumber = ret.Edict()!.NetworkSerialNumber;
		}

		return ret;
	}
}
