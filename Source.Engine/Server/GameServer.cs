using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Formats.BSP;
using Source.Common.Networking;
using Source.Common.Utilities;

using System.Runtime.InteropServices;

using CommunityToolkit.HighPerformance;
using System.Runtime.CompilerServices;

namespace Source.Engine.Server;

/// <summary>
/// Base server, in SERVER. Often referred to by 'sv'
/// </summary>
public class GameServer : BaseServer
{
#if GMOD_DLL
	readonly ConVar sv_startup_time = new("sv_startup_time", "0", 0, "Sets the starting CurTime of the server (in seconds). This is used for finding issues on a server that has been running for a long time. You don't need to change this.");
	readonly ConVar sv_hibernate_think = new("sv_hibernate_think", "0", 0, "Forces the server to think even when hibernating");
	readonly ConVar sv_hibernate_drop_bots = new("sv_hibernate_drop_bots", "1", 0, "Kicks bots when the server enters hibernation");
#endif
	protected readonly ED ED = Singleton<ED>();
	protected readonly Scr Scr = Singleton<Scr>();
	protected readonly SV SV = Singleton<SV>();
	protected readonly ICommandLine CommandLine = Singleton<ICommandLine>();
	public readonly FrameSnapshotManager FrameSnapshotManager = Singleton<FrameSnapshotManager>();

	PureServerWhitelist? PureServerWhitelist;

	public override void SetMaxClients(int number) {
		MaxClients = Math.Clamp(number, 1, MaxClientsLimit);
		Host.deathmatch.SetValue(MaxClients > 1);
		ConMsg($"maxplayers set to {MaxClients}\n");
	}

	public override void Init(bool dedicated) {
		base.Init(dedicated);
		FullSendTables.DebugName = "FullSendTables";
		DLLInitialized = false;
	}

	public bool IsInPureServerMode() => PureServerWhitelist != null;

	public override bool IsPausable() {
		if (IsMultiplayer())
			return SV.sv_pausable.GetBool();
		else
			return true;
	}

	public override void Shutdown() {
		LevelMainMenuBackground = false;
		base.Shutdown();
		framesnapshotmanager.LevelChanged();
		IGameEvent? ev = g_GameEventManager.CreateEvent("server_shutdown");

		if (ev != null) {
			ev.SetString("reason", "quit");
			g_GameEventManager.FireEvent(ev);
		}

		Steam3Server().Shutdown();
		if (serverGameDLL != null) 
			serverGameDLL.GameServerSteamAPIShutdown();

		g_DownloadListGenerator.OnLevelLoadEnd();
	}

	protected override BaseClient CreateNewClient(int slot) {
		BaseClient client = new GameClient(slot, this);
		return client;
	}

	public void SetQueryPortFromSteamServer() {
		// todo
	}
	internal bool IsLevelMainMenuBackground() {
		return LevelMainMenuBackground;
	}

	public bool LoadGame;           // handle connections specially

	public InlineArray64<char> Startspot;

	public int NumEdicts;
	public int MaxEdicts;
	public int FreeEdicts;
	public Edict[]? Edicts;
	public IChangeInfoAccessor[] EdictChangeInfo = [];

	public int MaxClientsLimit;    // Max allowed on server.

	public bool AllowSignOnWrites;
	public bool DLLInitialized;    // Have we loaded the game dll.

	public bool LevelMainMenuBackground;  // true if the level running only as the background to the main menu

	public readonly List<EventInfo> TempEntities = [];     // temp entities

	public readonly bf_write FullSendTables = new();
	public readonly UtlMemory<byte> FullSendTablesBuffer = new();

	public bool LoadedPlugins;

	public override void Clear() {
		host_state.SetWorldModel(null);

		for (ServerClass? cls = serverGameDLL.GetAllServerClasses(); cls != null; cls = cls.Next)
			cls.InstanceBaselineIndex = INetworkStringTable.INVALID_STRING_INDEX;

		TempEntities.Clear();

		base.Clear();
	}

	public void CreateEngineStringTables() {
		StringTableBits.SV_SetupNetworkStringTableBits();

		ModelPrecache = new PrecacheItem[PrecacheItem.MAX_MODELS];
		GenericPrecache = new PrecacheItem[PrecacheItem.MAX_GENERIC];
		DecalPrecache = new PrecacheItem[PrecacheItem.MAX_BASE_DECAL];
		SoundPrecache = new PrecacheItem[PrecacheItem.MAX_SOUNDS];

		StringTables!.SetTick(TickCount);

		int size = Unsafe.SizeOf<PrecacheUserData>();
		DownloadableFileTable = StringTables.CreateStringTable(Protocol.DOWNLOADABLE_FILE_TABLENAME, 8192, 0, 0); // DOWNLOADABLE_FILE_TABLENAME, MAX_DOWNLOADABLE_FILES
		ModelPrecacheTable = StringTables.CreateStringTableEx(PrecacheItem.MODEL_PRECACHE_TABLENAME, PrecacheItem.MAX_MODELS, size, PrecacheItem.PRECACHE_USER_DATA_NUMBITS, false);
		GenericPrecacheTable = StringTables.CreateStringTableEx(PrecacheItem.GENERIC_PRECACHE_TABLENAME, PrecacheItem.MAX_GENERIC, size, PrecacheItem.PRECACHE_USER_DATA_NUMBITS, false);
		SoundPrecacheTable = StringTables.CreateStringTableEx(PrecacheItem.SOUND_PRECACHE_TABLENAME, PrecacheItem.MAX_SOUNDS, size, PrecacheItem.PRECACHE_USER_DATA_NUMBITS, false);
		DecalPrecacheTable = StringTables.CreateStringTableEx(PrecacheItem.DECAL_PRECACHE_TABLENAME, PrecacheItem.MAX_BASE_DECAL, size, PrecacheItem.PRECACHE_USER_DATA_NUMBITS, false);
		InstanceBaselineTable = StringTables.CreateStringTable(Protocol.INSTANCE_BASELINE_TABLENAME, Constants.MAX_DATATABLES);
		LightStyleTable = StringTables.CreateStringTable(Protocol.LIGHT_STYLES_TABLENAME, BSPFileCommon.MAX_LIGHTSTYLES);
		UserInfoTable = StringTables.CreateStringTable(Protocol.USER_INFO_TABLENAME, 1 << Constants.ABSOLUTE_PLAYER_LIMIT_DW);
		DynamicModelsTable = StringTables.CreateStringTable(Protocol.DYNAMIC_MODELS_TABLENAME, 2048, 1, 1);
		ServerStartupDataTable = StringTables.CreateStringTable(Protocol.SERVER_STARTUP_DATA_TABLENAME, 4);

		SetQueryPortFromSteamServer();
		// CopyPureServerWhitelistToStringTable();

		Assert(
			DownloadableFileTable != null &&
			ModelPrecacheTable != null &&
			GenericPrecacheTable != null &&
			SoundPrecacheTable != null &&
			DecalPrecacheTable != null &&
			InstanceBaselineTable != null &&
			LightStyleTable != null &&
			UserInfoTable != null &&
			DynamicModelsTable != null &&
			ServerStartupDataTable != null
		);

		int j;

		Span<char> nameBuffer = stackalloc char[8];
		for (int i = 0; i < BSPFileCommon.MAX_LIGHTSTYLES; i++) {
			ReadOnlySpan<char> name = sprintf(nameBuffer, "%i").I(i);
			j = LightStyleTable.AddString(true, name);
			Assert(j == i);
		}

		for (int i = 0; i < GetMaxClients(); i++) {
			ReadOnlySpan<char> name = sprintf(nameBuffer, "%i").I(i);
			j = UserInfoTable.AddString(true, name);
			Assert(j == i);
		}

		g_DownloadListGenerator.SetStringTable(DownloadableFileTable);
	}

	public INetworkStringTable? GetModelPrecacheTable() => ModelPrecacheTable;
	public INetworkStringTable? GetGenericPrecacheTable() => GenericPrecacheTable;
	public INetworkStringTable? GetSoundPrecacheTable() => SoundPrecacheTable;
	public INetworkStringTable? GetDecalPrecacheTable() => DecalPrecacheTable;
	public INetworkStringTable? GetDynamicModelsTable() => DynamicModelsTable;

	public static readonly ConVar sv_forcepreload = new("sv_forcepreload", "0", FCvar.Archive, "Force server side preloading.");

	public int PrecacheModel(ReadOnlySpan<char> name, Res flags, Model? model = null) {
		if (ModelPrecacheTable == null)
			return -1;

		int idx = ModelPrecacheTable.AddString(true, name);
		if (idx == INetworkStringTable.INVALID_STRING_INDEX)
			return -1;

		PrecacheUserData p = new();
		Span<PrecacheUserData> existing = ModelPrecacheTable.GetStringUserData(idx).AsSpan().Cast<byte, PrecacheUserData>();

		if (existing.IsEmpty)
			p.Flags = flags;
		else {
			p = existing[0];
			p.Flags |= flags;
		}

		ModelPrecacheTable.SetStringUserData(idx, Unsafe.SizeOf<PrecacheUserData>(), MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref p, 1)));

		PrecacheItem slot = ModelPrecache[idx];
		slot ??= ModelPrecache[idx] = new PrecacheItem();

		if (model != null)
			slot.SetModel(model);

		bool loadNow;
		loadNow = (slot.GetModel() == null && ((flags & Res.Preload) != 0 || IsX360()));
		if (CommandLine.FindParm("-nopreload") != 0 || CommandLine.FindParm("-nopreloadmodels") != 0)
			loadNow = false;
		else if (sv_forcepreload.GetInt() != 0 || CommandLine.FindParm("-preload") != 0)
			loadNow = true;

		if (idx != 0) {
			if (loadNow) {
				slot.SetModel(modelloader.GetModelForName(name, ModelLoaderFlags.Server));
#if !SWDS
				EngineVGui().UpdateProgressBar(LevelLoadingProgress.Precache);
#endif
				// todo: MapReslistGenerator().OnModelPrecached(name);
			}
			else {
				modelloader.ReferenceModel(name, ModelLoaderFlags.Server);
				slot.SetModel(null);
			}
		}

		return idx;
	}
	public Model? GetModel(int index) {
		if (index <= 0 || ModelPrecacheTable == null)
			return null;
		if (index >= ModelPrecacheTable.GetNumStrings())
			return null;
		PrecacheItem slot = ModelPrecache[index];
		Model? model = slot.GetModel();
		if (model != null)
			return model;

		if (index == 1)
			return null;

		ReadOnlySpan<char> name = ModelPrecacheTable.GetString(index);
		if (name.IsEmpty)
			return null;

		model = modelloader.GetModelForName(name, ModelLoaderFlags.Server);
		slot.SetModel(model);
		return model;
	}
	public int LookupModelIndex(ReadOnlySpan<char> name) {
		if (ModelPrecacheTable == null)
			return -1;
		int idx = ModelPrecacheTable.FindStringIndex(name);
		return idx == INetworkStringTable.INVALID_STRING_INDEX ? -1 : idx;
	}

	// Accessors to model precaching stuff
	public int PrecacheSound(ReadOnlySpan<char> name, Res flags) {
		if (SoundPrecacheTable == null)
			return -1;
		int idx = SoundPrecacheTable.AddString(true, name);
		if (idx == INetworkStringTable.INVALID_STRING_INDEX)
			return -1;

		PrecacheUserData p = new();
		Span<PrecacheUserData> existing = SoundPrecacheTable.GetStringUserData(idx).AsSpan().Cast<byte, PrecacheUserData>();

		if (existing.IsEmpty)
			p.Flags = flags;
		else {
			p = existing[0];
			p.Flags |= flags;
		}

		SoundPrecacheTable.SetStringUserData(idx, Unsafe.SizeOf<PrecacheUserData>(), MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref p, 1)));

		PrecacheItem slot = SoundPrecache[idx];
		slot ??= SoundPrecache[idx] = new PrecacheItem();
		slot.SetName(new(name));

		return idx;
	}
	public ReadOnlySpan<char> GetSound(int index) {
		if (index <= 0 || SoundPrecacheTable == null)
			return null;
		if (index >= SoundPrecacheTable.GetNumStrings())
			return null;
		PrecacheItem slot = SoundPrecache![index];
		return slot.GetName();
	}
	public int LookupSoundIndex(ReadOnlySpan<char> name) {
		if (SoundPrecacheTable == null)
			return -1;
		int idx = SoundPrecacheTable.FindStringIndex(name);
		return idx == INetworkStringTable.INVALID_STRING_INDEX ? -1 : idx;
	}

	public int PrecacheGeneric(ReadOnlySpan<char> name, Res flags) {
		if (GenericPrecacheTable == null)
			return -1;
		int idx = GenericPrecacheTable.AddString(true, name);
		if (idx == INetworkStringTable.INVALID_STRING_INDEX)
			return -1;

		PrecacheUserData p = new();
		Span<PrecacheUserData> existing = GenericPrecacheTable.GetStringUserData(idx).AsSpan().Cast<byte, PrecacheUserData>();

		if (existing.IsEmpty)
			p.Flags = flags;
		else {
			p = existing[0];
			p.Flags |= flags;
		}

		GenericPrecacheTable.SetStringUserData(idx, Unsafe.SizeOf<PrecacheUserData>(), MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref p, 1)));

		PrecacheItem slot = GenericPrecache[idx];
		slot ??= GenericPrecache[idx] = new PrecacheItem();
		slot.SetGeneric(new(name));

		return idx;
	}
	public ReadOnlySpan<char> GetGeneric(int index) {
		if (index <= 0 || GenericPrecacheTable == null)
			return null;
		if (index >= GenericPrecacheTable.GetNumStrings())
			return null;
		PrecacheItem slot = GenericPrecache![index];
		return slot.GetName();
	}
	public int LookupGenericIndex(ReadOnlySpan<char> name) {
		if (GenericPrecacheTable == null)
			return -1;
		int idx = GenericPrecacheTable.FindStringIndex(name);
		return idx == INetworkStringTable.INVALID_STRING_INDEX ? -1 : idx;
	}

	public int PrecacheDecal(ReadOnlySpan<char> name, Res flags) {
		if (DecalPrecacheTable == null)
			return -1;
		int idx = DecalPrecacheTable.AddString(true, name);
		if (idx == INetworkStringTable.INVALID_STRING_INDEX)
			return -1;
		throw new NotImplementedException();
	}
	public int LookupDecalIndex(ReadOnlySpan<char> name) {
		if (DecalPrecacheTable == null)
			return -1;
		int idx = DecalPrecacheTable.FindStringIndex(name);
		return idx == INetworkStringTable.INVALID_STRING_INDEX ? -1 : idx;
	}

	public void DumpPrecacheStats(INetworkStringTable? table) {

	}

	public bool IsHibernating() => Hibernating;
	public void UpdateHibernationState() {
		// todo
	}


	public PrecacheItem[] ModelPrecache = null!;
	public PrecacheItem[] GenericPrecache = null!;
	public PrecacheItem[] SoundPrecache = null!;
	public PrecacheItem[] DecalPrecache = null!;

	public GameClient Client(int i) => (GameClient)Clients[i];

	private void SetHibernating(bool hibernating) {
		// todo
	}

	internal void InitMaxClients() {
		int newmaxplayers = CommandLine.ParmValue("-maxplayers", -1);
		if (newmaxplayers == -1)
			newmaxplayers = CommandLine.ParmValue("+maxplayers", -1);

		SetupMaxPlayers(newmaxplayers);
	}
	static readonly ConVar tv_enable = new("tv_enable", "0", FCvar.NotConnected, "Activates SourceTV on server. not implemented!");

	private void SetupMaxPlayers(int iDesiredMaxPlayers) {
		int minmaxplayers = 1;
		int maxmaxplayers = Constants.ABSOLUTE_PLAYER_LIMIT;
		int defaultmaxplayers = 1;

		if (SV.ServerGameClients != null) {
			SV.ServerGameClients.GetPlayerLimits(out minmaxplayers, out maxmaxplayers, out defaultmaxplayers);

			if (minmaxplayers < 1)
				Sys.Error($"GetPlayerLimits:  min maxplayers must be >= 1 ({minmaxplayers})");
			else if (defaultmaxplayers < 1)
				Sys.Error($"GetPlayerLimits:  default maxplayers must be >= 1 ({minmaxplayers})");


			if (minmaxplayers > maxmaxplayers || defaultmaxplayers > maxmaxplayers)
				Sys.Error($"GetPlayerLimits:  min maxplayers {minmaxplayers} > max {maxmaxplayers}");
			if (maxmaxplayers > Constants.ABSOLUTE_PLAYER_LIMIT)
				Sys.Error($"GetPlayerLimits:  max players limited to {Constants.ABSOLUTE_PLAYER_LIMIT}");
		}

		sv.MaxClientsLimit = maxmaxplayers;
		int newmaxplayers = iDesiredMaxPlayers;

		if (newmaxplayers >= 1) {
			newmaxplayers = Math.Min(newmaxplayers, maxmaxplayers);
			sv.MaxClientsLimit = newmaxplayers;
		}
		else {
			newmaxplayers = defaultmaxplayers;
		}

		if (tv_enable.GetBool()) {
			newmaxplayers += 1;
			sv.MaxClientsLimit += 1;
		}

		newmaxplayers = Math.Clamp(newmaxplayers, minmaxplayers, sv.MaxClientsLimit);

		if (sv.GetMaxClients() < newmaxplayers || !tv_enable.GetBool())
			sv.SetMaxClients(newmaxplayers);
	}

	public override void SendClientMessages(bool bSendSnapshots) {
		InlineArray128<GameClient> receivingClientsBuffer = default;
		SpanList<GameClient> receivingClients = new(receivingClientsBuffer);

		for (int i = 0; i < GetClientCount(); i++) {
			GameClient client = Client(i);

			if (!client.ShouldSendMessages())
				continue;

			if (bSendSnapshots && client.IsActive()) {
				receivingClients.Add(client);
			}
			else {
				if (client.IsFakeClient())
					continue;

				if (Net.IsMultiplayer() && client.NetChannel!.GetSequenceNumber(1) == 0) {
					// Net.OutOfBandPrintf(client.NetChannel.RemoteAddress, $"{(char)Protocol.S2C_CONNECTION}00000000000000");
				}

				client.NetChannel!.Transmit();
				client.UpdateSendState();
			}
		}

		int receivingClientCount = receivingClients.Count;
		if (receivingClientCount > 0) {
			FrameSnapshot snapshot = FrameSnapshotManager.TakeTickSnapshot((int)TickCount);
			CopyTempEntities(snapshot);

			PackedEntities.ComputeClientPacks(receivingClientCount, receivingClients.AsSpan(), snapshot);

			// if (receivingClientCount > 1 && sv_parallel_sendsnapshot.GetBool()) {
			// 
			// }

			for (int i = 0; i < receivingClientCount; i++) {
				GameClient client = receivingClients[i];
				if (client == null)
					continue;
				ClientFrame frame = client.GetSendFrame()!;
				client.SendSnapshot(frame);
				client.UpdateSendState();
			}

			snapshot.ReleaseReference();
		}
	}

	void CopyTempEntities(FrameSnapshot snapshot) {
		Assert(snapshot.TempEntities == null);

		if (TempEntities.Count > 0) {
			snapshot.NumTempEntities = TempEntities.Count;
			snapshot.TempEntities = new EventInfo[TempEntities.Count];
			for (int i = 0; i < TempEntities.Count; i++)
				snapshot.TempEntities[i] = TempEntities[i];
			TempEntities.Clear();
		}
	}

	int CurrentSkill;

	internal bool SpawnServer(ReadOnlySpan<char> mapName, ReadOnlySpan<char> mapFile, ReadOnlySpan<char> startspot) {
		mapName = mapName.SliceNullTerminatedString();
		mapFile = mapFile.SliceNullTerminatedString();
		startspot = startspot.SliceNullTerminatedString();
		modelloader.ResetModelServerCounts();

#if GMOD_DLL && !SWDS
		EngineVGui().UpdateCustomProgressBar(0.01f, "Waiting for Steam Workshop to finish...");
		while (!g_pFileSystem.Addons().AllJobsFinished()) {
			g_pFileSystem.Addons().Think();
			Scr.UpdateScreen();
			Thread.Sleep(50);
		}
#endif

		// ReloadWhitelist(mapName);
		Common.TimestampedLog($"SV_SpawnServer({mapName})");
#if !SWDS
		EngineVGui().UpdateProgressBar(LevelLoadingProgress.SpawnServer);
#endif
#if !SWDS
		Scr.CenterStringOff();
#endif

		if (!startspot.IsEmpty)
			ConDMsg("Spawn Server: {mapName}: [{startspot}]\n");
		else
			ConDMsg($"Spawn Server: {mapName}\n");

		Host.SpawnCount = ++SpawnCount;

		Host.deathmatch.SetValue(IsMultiplayer() ? 1 : 0);
		if (Host.coop.GetInt() != 0)
			Host.deathmatch.SetValue(0);

		CurrentSkill = (int)(Host.skill.GetFloat() + 0.5);
		CurrentSkill = Math.Max(CurrentSkill, 0);
		CurrentSkill = Math.Min(CurrentSkill, 3);

		Host.skill.SetValue((float)CurrentSkill);

		Common.TimestampedLog("StaticPropMgr()->LevelShutdown()");

#if !SWDS
		g_ShadowMgr.LevelShutdown();
#endif
		StaticPropMgr().LevelShutdown();

		Common.TimestampedLog("Host_FreeToLowMark");

		Host.FreeStateAndWorld(true);
		Host.FreeToLowMark(true);

		serverGlobalVariables.MapVersion = 0;

		Common.TimestampedLog("sv.Clear()");

		Clear();

		Common.TimestampedLog("framesnapshotmanager->LevelChanged()");

		// framesnapshotmanager->LevelChanged();

		// set map name
		strcpy(MapName, mapName);
		strcpy(MapFilename, mapFile);

		// set startspot
		if (!startspot.IsEmpty)
			strcpy(Startspot, startspot);
		else
			Startspot[0] = '\0';

		// Preload any necessary data from the xzps:
		// g_pFileSystem.SetupPreloadData();
		// g_pMDLCache.InitPreloadData(false);

		// Allocate server memory
		MaxEdicts = Constants.MAX_EDICTS;


		serverGlobalVariables.MaxEntities = MaxEdicts;
		serverGlobalVariables.MaxClients = GetMaxClients();
#if !SWDS
		clientGlobalVariables.NetworkProtocol = Protocol.VERSION;
#endif

		NumEdicts = GetMaxClients() + 1;

		Common.TimestampedLog("SV_AllocateEdicts");

		SV.AllocateEdicts();

		SV.ServerGameEnts!.SetDebugEdictBase(Edicts!);

		AllowSignOnWrites = true;

		ServerClasses = 0;
		ServerClassBits = 0;

		AssignClassIds();

		Common.TimestampedLog("Set up players");

		for (int i = 0; i < GetClientCount(); i++) {
			GameClient pClient = Client(i);

			pClient.Edict = Edicts![i + 1];
			InitializeEntityDLLFields(pClient.Edict!);
		}

		Common.TimestampedLog("Set up players(done)");

		State = ServerState.Loading;

		// Set initial time values.
		TickInterval = host_state.IntervalPerTick;
#if GMOD_DLL
		TickCount = (int)(1.0 / host_state.IntervalPerTick) + 1 + TIME_TO_TICKS(sv_startup_time.GetInt()); // Verify: Does GMOD set sv_startup_time here?
#else
		TickCount = (int)(1.0 / host_state.interval_per_tick) + 1; // Start at appropriate 1
#endif

		serverGlobalVariables.TickCount = TickCount;
		serverGlobalVariables.CurTime = GetTime();

		g_pFileSystem.AddSearchPath(mapFile, "GAME", SearchPathAdd.ToHead, groupName: PathGroupName.Map);
		g_pFileSystem.BeginMapAccess();

		Common.TimestampedLog($"modelloader->GetModelForName({mapFile}) -- Start");

		host_state.SetWorldModel(modelloader.GetModelForName(mapFile, ModelLoaderFlags.Server));
		if (host_state.WorldModel == null) {
			ConMsg($"Couldn't spawn server {mapFile.SliceNullTerminatedString()}\n");
			State = ServerState.Dead;
			g_pFileSystem.EndMapAccess();
			return false;
		}

		Common.TimestampedLog($"modelloader->GetModelForName({mapFile}) -- Finished");

		if (IsMultiplayer()) {
#if !SWDS
			EngineVGui().UpdateProgressBar(LevelLoadingProgress.CrcMap);
#endif
			memreset(ref WorldmapMD5);

			if (!ChecksumEngine.MD5_MapFile(out WorldmapMD5, mapFile)) {
				ConMsg($"Couldn't CRC server map: {mapFile.SliceNullTerminatedString()}\n");
				State = ServerState.Dead;
				g_pFileSystem.EndMapAccess();
				return false;
			}
#if !SWDS
			EngineVGui().UpdateProgressBar(LevelLoadingProgress.CrcClientDll);
#endif
		}
		else {
			memreset(ref WorldmapMD5);
		}

		StringTables = networkStringTableContainerServer;

		Common.TimestampedLog("SV_CreateNetworkStringTables");

#if !SWDS
		EngineVGui().UpdateProgressBar(LevelLoadingProgress.CreateNetworkStringTables);
#endif

		SV.CreateNetworkStringTables();
		PrecacheModel("", 0);
		PrecacheGeneric("", 0);
		PrecacheSound("", 0);

		Common.TimestampedLog($"Precache world model ({mapFile})");

#if !SWDS
		EngineVGui().UpdateProgressBar(LevelLoadingProgress.PrecacheWorld);
#endif
		PrecacheModel(mapFile, Res.FatalIfMissing | Res.Preload, host_state.WorldModel);

		Common.TimestampedLog("Precache brush models");

		Span<char> localmodel = stackalloc char[5];
		for (int i = 1; i < host_state.WorldBrush!.NumSubModels; i++) {
			// Add in world brush models
			memreset(localmodel);
			sprintf(localmodel, "*%i").I(i);
			PrecacheModel(localmodel, Res.FatalIfMissing | Res.Preload, modelloader.GetModelForName(localmodel, ModelLoaderFlags.Server));
		}

#if !SWDS
		EngineVGui().UpdateProgressBar(LevelLoadingProgress.ClearWorld);
#endif
		Common.TimestampedLog("SV_ClearWorld");

		SV.ClearWorld();

		Common.TimestampedLog("InitializeEntityDLLFields");

		InitializeEntityDLLFields(Edicts![0]);

		ED.ClearFreeFlag(Edicts![0]);

		if (Host.coop.GetFloat() != 0)
			serverGlobalVariables.Coop = (Host.coop.GetInt() != 0);
		else
			serverGlobalVariables.Deathmatch = (Host.deathmatch.GetInt() != 0);

		serverGlobalVariables.MapName = new(((ReadOnlySpan<char>)MapName).SliceNullTerminatedString());
		serverGlobalVariables.StartSpot = new(((ReadOnlySpan<char>)Startspot).SliceNullTerminatedString());

		// set game event
		IGameEvent? ev = g_GameEventManager.CreateEvent("server_spawn");
		if (ev != null) {
			ev.SetString("hostname", Host.host_name.GetString());
			ev.SetString("address", Net.LocalAdr.ToString(false));
			ev.SetInt("port", GetUDPPort());
			ev.SetString("game", Common.Gamedir);
			ev.SetString("mapname", GetMapName());
			ev.SetInt("maxplayers", GetMaxClients());
			ev.SetInt("password", 0);              // TODO
#if WIN32
			ev.SetString("os", "WIN32");
#elif LINUX
		ev.SetString( "os", "LINUX" );
#elif OSX
		ev.etString( "os", "OSX" );
#else
#error Please define your platform
#endif
			ev.SetInt("dedicated", IsDedicated() ? 1 : 0);

			g_GameEventManager.FireEvent(ev);
		}

		Common.TimestampedLog("SV_SpawnServer -- Finished");

		g_pFileSystem.EndMapAccess();
		return true;
	}

	private void InitializeEntityDLLFields(Edict edict) {
		edict.InitializeEntityDLLFields();
	}

	private void AssignClassIds() {
		ServerClass classes = serverGameDLL.GetAllServerClasses();

		int nClasses = 0;
		for (ServerClass count = classes; count != null; count = count.Next)
			nClasses++;

		ErrorIfNot(nClasses <= Constants.MAX_SERVER_CLASSES, $"CGameServer::AssignClassIds: too many server classes ({nClasses}, MAX = {Constants.MAX_SERVER_CLASSES}).\n");

		ServerClasses = nClasses;
		ServerClassBits = (int)(Math.Log2(ServerClasses) + 1);

		bool spew = CommandLine.FindParm("-netspike") != 0;
		int curID = 0;
		for (ServerClass? c = classes; c != null; c = c.Next) {
			c.ClassID = curID++;

			if (spew)
				Msg($"{c.ClassID} == '{c.NetworkName}'\n");
		}
	}

	INetworkStringTable? ModelPrecacheTable;
	INetworkStringTable? SoundPrecacheTable;
	INetworkStringTable? GenericPrecacheTable;
	INetworkStringTable? DecalPrecacheTable;

	INetworkStringTable? DynamicModelsTable;

	INetworkStringTable? ServerStartupDataTable;

	bool Hibernating;    // Are we hibernating.  Hibernation makes server process consume approx 0 CPU when no clients are connected

	public void InstallClientStringTableMirrors() {
#if !SWDS
		int numTables = networkStringTableContainerServer.GetNumTables();
		for (int i = 0; i < numTables; i++) {
			NetworkStringTable? serverTable = (NetworkStringTable?)networkStringTableContainerServer.GetTable(i);
			if (serverTable == null)
				continue;

			NetworkStringTable? clientTable = (NetworkStringTable?)networkStringTableContainerClient.FindTable(serverTable.GetTableName());

			if (clientTable == null) {
				DevMsg($"SV_InstallClientStringTableMirrors! Missing client table \"{serverTable.GetTableName()}\".\n ");
				continue;
			}

			serverTable.SetMirrorTable(clientTable);
		}
#endif
	}

	internal void BroadcastSound<T>(SoundInfo sound, T filter) where T : IRecipientFilter {
		int num = filter.GetRecipientCount();

		// don't add sounds while paused, unless we're in developer mode
		if (IsPaused() && 0 == Host.developer.GetInt())
			return;

		for (int i = 0; i < num; i++) {
			int index = filter.GetRecipientIndex(i);

			if (index < 1 || index > GetClientCount()) {
				Msg("CGameServer::BroadcastSound:  Recipient Filter for sound (reliable: %s, init: %s) with bogus client index (%i) in list of %i clients\n",
						filter.IsReliable() ? "yes" : "no",
						filter.IsInitMessage() ? "yes" : "no",
						index, num);

				continue;
			}

			GameClient client = Client(index - 1)!;

			// client must be fully connect to hear sounds
			if (!client.IsActive())
				continue;

			client.SendSound(sound, filter.IsReliable());
		}
	}
}
