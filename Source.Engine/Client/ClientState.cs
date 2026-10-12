using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.Hashing;
using Source.Common.Mathematics;
using Source.Common.MaterialSystem;
using Source.Common.Networking;
using Source.Common.Server;
using Source.Engine.Server;

using Steamworks;

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

using static Source.Constants;

using GameServer = Source.Engine.Server.GameServer;

namespace Source.Engine.Client;

/// <summary>
/// Client state, in CLIENT. Often referred to by 'cl'
/// </summary>
public class ClientState : BaseClientState
{
	readonly Host Host;
	readonly IFileSystem fileSystem;
	readonly Net Net;
	readonly CommonHostState host_state;
	readonly CL CL;
	readonly IEngineVGuiInternal? EngineVGui;
	readonly IHostState HostState;
	readonly ICvar cvar;
	readonly DtCommonEng DtCommonEng;
	readonly EngineRecvTable RecvTable;
	readonly IPrediction ClientSidePrediction;
	readonly IModelLoader modelloader;
	readonly ClientGlobalVariables clientGlobalVariables;
	readonly Lazy<IEngineClient> engineClient_LAZY;
	IEngineClient engineClient => engineClient_LAZY.Value;
	readonly IServiceProvider services;
	readonly ICommandLine CommandLine;
	readonly Scr Scr;

	public readonly ClientFrameManager FrameManager = new();

	public TimeUnit_t LastServerTickTime;
	public bool InSimulation;

	public int OldTickCount;
	public TimeUnit_t TickRemainder;
	public TimeUnit_t FrameTime;

	public int LastOutgoingCommand;
	public int ChokedCommands;
	public int LastCommandAck;
	public int CommandAck;
	public int SoundSequence;

	public bool IsHLTV;
	// public bool IsReplay; // RaphaelIT7: Gmod has replay completely removed iirc

	public MD5Value ServerMD5;

	public byte[] AreaBits = new byte[MAX_AREA_STATE_BYTES];
	public byte[] AreaPortalBits = new byte[MAX_AREA_PORTAL_STATE_BYTES];
	public bool AreaBitsValid;

	public QAngle ViewAngles;
	List<AddAngle> AddAngle = new();
	public float AddAngleTotal;
	public float PrevAddAngleTotal;
	public CustomFile[] CustomFiles = new CustomFile[MAX_CUSTOM_FILES];
	public uint FriendsID;
	public string? FriendsName;

	public bool UpdateSteamResources;
	public bool ShownSteamResourceUpdateProgress;
	public bool DownloadResources;
	public bool PrepareClientDLL;
	public bool CheckCRCsWithServer;
	public double LastCRCBatchTime;
	public bool MarkedCRCsUnverified;

	public INetworkStringTable? ModelPrecacheTable;
	public INetworkStringTable? GenericPrecacheTable;
	public INetworkStringTable? SoundPrecacheTable;
	public INetworkStringTable? DecalPrecacheTable;
	public INetworkStringTable? InstanceBaselineTable;
	public INetworkStringTable? LightStyleTable;
	public INetworkStringTable? UserInfoTable;
	public INetworkStringTable? ServerStartupTable;
	public INetworkStringTable? DynamicModelsTable;
	public INetworkStringTable? DownloadableFileTable;


	readonly PrecacheItem[] ModelPrecache = ClassUtils.BlankInstantiatedArray<PrecacheItem>(PrecacheItem.MAX_MODELS);
	readonly PrecacheItem[] GenericPrecache = ClassUtils.BlankInstantiatedArray<PrecacheItem>(PrecacheItem.MAX_GENERIC);
	readonly PrecacheItem[] SoundPrecache = ClassUtils.BlankInstantiatedArray<PrecacheItem>(PrecacheItem.MAX_SOUNDS);
	readonly PrecacheItem[] DecalPrecache = ClassUtils.BlankInstantiatedArray<PrecacheItem>(PrecacheItem.MAX_BASE_DECAL);

	public static ConVar cl_timeout = new("30", FCvar.Archive, "After this many seconds without receiving a packet from the server, the client will disconnect itself");
	public static ConVar cl_logofile = new("materials/decals/spraylogo.vtf", FCvar.Archive, "Spraypoint logo decal.");
	public static ConVar cl_allowdownload = new("1", FCvar.Archive, "Client downloads customization files");
	public static ConVar cl_downloadfilter = new("all", FCvar.Archive, "Determines which files can be downloaded from the server (all, none, nosounds, mapsonly)");

	readonly Common Common;
	readonly Sound Sound;

	public ClientState(Host Host, IFileSystem fileSystem, Net Net, CommonHostState host_state, Common Common,
		Cbuf Cbuf, Cmd Cmd, ICvar cvar, IHostState HostState, Scr Scr, IEngineAPI engineAPI,
		IServiceProvider services,
IModelLoader modelloader, ICommandLine commandLine,
		[FromKeyedServices(Realm.Client)] NetworkStringTableContainer networkStringTableContainerClient,

#if !SWDS
		IPrediction ClientSidePrediction,
		IEngineVGuiInternal? EngineVGui,
		CL CL,
		DtCommonEng DtCommonEng,
		EngineRecvTable RecvTable,
#endif
		ClientGlobalVariables clientGlobalVariables, Sound Sound
		)
		: base(Host, fileSystem, Net, Cbuf, cvar, networkStringTableContainerClient
		) {
		this.Host = Host;
		this.fileSystem = fileSystem;
		this.Net = Net;
		this.host_state = host_state;
		this.cvar = cvar;
		this.CL = CL;
		this.Scr = Scr;
		this.modelloader = modelloader;
		this.ClockDriftMgr = new(Host, host_state, Net, clientGlobalVariables);
		this.EngineVGui = EngineVGui;
		this.HostState = HostState;
		this.DtCommonEng = DtCommonEng;
		this.Common = Common;
		this.services = services;
		this.ClientSidePrediction = ClientSidePrediction;
		this.clientGlobalVariables = clientGlobalVariables;
		this.Sound = Sound;
		engineClient_LAZY = new(ProduceEngineClient);
		CommandLine = commandLine;
		this.RecvTable = RecvTable;
	}

	private IEngineClient ProduceEngineClient() => services.GetRequiredService<IEngineClient>();
	public override void Clear() {
		base.Clear();

		ModelPrecacheTable = null;
		GenericPrecacheTable = null;
		SoundPrecacheTable = null;
		DecalPrecacheTable = null;
		InstanceBaselineTable = null;
		LightStyleTable = null;
		UserInfoTable = null;
		ServerStartupTable = null;
		DynamicModelsTable = null;
		DownloadableFileTable = null;

		Array.Clear(AreaBits, 0, AreaBits.Length);
		UpdateSteamResources = false;
		ShownSteamResourceUpdateProgress = false;
		DownloadResources = false;
		PrepareClientDLL = false;

		FrameManager.DeleteClientFrames(-1);

		ViewAngles.Init();
		LastServerTickTime = 0.0;
		OldTickCount = 0;
		InSimulation = false;

		AddAngle.Clear();
		AddAngleTotal = 0.0f;
		PrevAddAngleTotal = 0.0f;

		ModelPrecache.ClearInstantiatedReferences();
		SoundPrecache.ClearInstantiatedReferences();
		DecalPrecache.ClearInstantiatedReferences();
		GenericPrecache.ClearInstantiatedReferences();

		IsHLTV = false;
		ServerMD5 = default;

		LastCommandAck = 0;
		CommandAck = 0;
		SoundSequence = 0;

		if (SignOnState > SignOnState.Connected) {
			SignOnState = SignOnState.Connected;
		}
	}

	public bool ProcessConnectionlessPacket(in NetPacket packet) {
		return false;
	}

	protected override bool ProcessTick(NET_Tick msg) {
		int tick = msg.Tick;

		NetChannel!.SetRemoteFramerate(msg.HostFrameTime, msg.HostFrameDeviation);

		ClockDriftMgr.SetServerTick(tick);

		LastServerTickTime = tick * host_state.IntervalPerTick;

		clientGlobalVariables.TickCount = tick;
		clientGlobalVariables.CurTime = tick * host_state.IntervalPerTick;
		clientGlobalVariables.FrameTime = (tick - OldTickCount) * host_state.IntervalPerTick;

		return true;
	}

	public override bool HookClientStringTable(ReadOnlySpan<char> tableName) {
		INetworkStringTable? table = GetStringTable(tableName);
		if (table == null) {
			Host.clientDLL?.InstallStringTableCallback(tableName);
			return false;
		}

		CL_SetupNetworkStringTableBits(StringTableContainer!, tableName);

		switch (tableName) {
			case PrecacheItem.MODEL_PRECACHE_TABLENAME:
				ModelPrecacheTable = table;
				return true;
			case PrecacheItem.GENERIC_PRECACHE_TABLENAME:
				GenericPrecacheTable = table;
				return true;
			case PrecacheItem.SOUND_PRECACHE_TABLENAME:
				SoundPrecacheTable = table;
				return true;
			case PrecacheItem.DECAL_PRECACHE_TABLENAME:
				DecalPrecacheTable = table;
				table.SetStringChangedCallback(null, Callback_DecalChanged);
				return true;
			case Protocol.INSTANCE_BASELINE_TABLENAME:
				InstanceBaselineTable = table;
				return true;
			case Protocol.LIGHT_STYLES_TABLENAME:
				LightStyleTable = table;
				return true;
			case Protocol.USER_INFO_TABLENAME:
				UserInfoTable = table;
				return true;
			case Protocol.DOWNLOADABLE_FILE_TABLENAME:
				DownloadableFileTable = table;
				return true;
		}

		Host.clientDLL?.InstallStringTableCallback(tableName);
		return false;
	}


	public override void Disconnect(ReadOnlySpan<char> reason, bool showMainMenu) {
		base.Disconnect(reason, showMainMenu);

		IGameEvent? ev = g_GameEventManager.CreateEvent("client_disconnect");
		if (ev != null) {
			ev.SetString("message", reason);
			g_GameEventManager.FireEventClientSide(ev);
		}

		Sound.StopAllSounds(true);
#if !SWDS
		Render.DecalTermAll();
#endif

		if (MaxClients > 1)
			if (EngineVGui!.IsConsoleVisible() == false)
				EngineVGui!.EnabledProgressBarForNextLoad();

		CL.ClearState();

		CL.HTTPStop_f();

		if (showMainMenu)
			Scr.EndLoadingPlaque();

		EngineVGui!.NotifyOfServerDisconnect();

		if (showMainMenu && !engineClient.IsDrawingLoadingImage())
			EngineVGui.ActivateGameUI();

		HostState.OnClientDisconnected();
	}
	public override void FullConnect(NetAddress adr) {
		base.FullConnect(adr);

		LastOutgoingCommand = -1;
		ChokedCommands = 0;
	}

	public override int GetClientTickCount() => ClockDriftMgr.ClientTick;
	public override void SetClientTickCount(int tick) => ClockDriftMgr.ClientTick = tick;
	public override int GetServerTickCount() => ClockDriftMgr.ServerTick;
	public override void SetServerTickCount(int tick) => ClockDriftMgr.ServerTick = tick;
	public override void ConnectionClosing(ReadOnlySpan<char> reason) {
		if (SignOnState > SignOnState.None) {
			if (reason != null && reason.Length > 0 && reason[0] == '#')
				Common.ExplainDisconnection(true, reason);
			else
				Common.ExplainDisconnection(true, $"Disconnect: {reason}.\n");

			Scr.EndLoadingPlaque();
			Host.Disconnect(true, reason);
		}
	}

	GameEventManager? gem;
	GameEventManager gameEventManager => gem ??= (GameEventManager)Singleton<IGameEventManager2>();
	protected override bool ProcessGameEventList(SVC_GameEventList msg) {
		return gameEventManager.ParseEventList(msg);
	}
	protected override bool ProcessBSPDecal(SVC_BSPDecal msg) {
		Model? model;

		if (msg.EntityIndex != 0)
			model = GetModel(msg.ModelIndex);
		else {
			model = host_state.WorldModel;
			if (model == null)
				Warning("ProcessBSPDecal:  Trying to project on world before host_state.worldmodel is set!!!\n");
		}

		if (model == null) {
			IMaterial? mat = Render.Draw_DecalMaterial(msg.DecalTextureIndex);
			ReadOnlySpan<char> matname = "???";
			if (mat != null)
				matname = mat.GetName();

			Warning($"Warning! Static BSP decal ({matname}), on NULL model index {msg.ModelIndex} for entity index {msg.EntityIndex}.\n");
			return true;
		}

		if (r_decals.GetInt() != 0)
			effects.DecalShoot(msg.DecalTextureIndex, msg.EntityIndex, model, in vec3_origin, in vec3_angle, in msg.Pos, null, msg.LowPriority ? 0 : (int)FDecal.Permanent);

		// ReadOnlySpan<char> decalName = DecalPrecacheTable!.GetString(msg.DecalTextureIndex);
		// DevMsg($"Process decal {msg.Pos}, {decalName}, {msg.EntityIndex}, {model}\n");

		return true;
	}

	protected override bool ProcessGameEvent(SVC_GameEvent msg) {
		int startbit = msg.DataIn.BitsRead;

		IGameEvent? ev = gameEventManager.UnserializeEvent(msg.DataIn);

		int length = msg.DataIn.BitsRead - startbit;

		if (length != msg.Length) {
			DevMsg("ClientState.ProcessGameEvent: KeyValue length mismatch.\n");
			return true;
		}

		if (ev == null) {
			DevMsg("ClientState.ProcessGameEvent: UnserializeKeyValue failed.\n");
			return true;
		}

		gameEventManager.FireEventClientSide(ev);
		return true;
	}
	public override bool ProcessClassInfo(SVC_ClassInfo msg) {
		if (msg.CreateOnClient) {
			DtCommonEng.CreateClientTablesFromServerTables();
			DtCommonEng.CreateClientClassInfosFromServerClasses(this);

			LinkClasses();
		}
		else
			base.ProcessClassInfo(msg);

		bool allowMismatches = false;
		if (!RecvTable.CreateDecoders(allowMismatches, out _)) {
			Host.EndGame(true, "CL.ProcessClassInfo: CreateDecoders failed.\n");
			return false;
		}

		LocalNetworkBackdoor.InitFastCopy();

		return true;
	}
	void ProcessSoundsWithProtoVersion(SVC_Sounds msg, List<SoundInfo> sounds, int protoVersion) {
		SoundInfo defaultSound = default;
		defaultSound.SetDefault();
		ref SoundInfo pDeltaSound = ref defaultSound;

		sounds.EnsureCapacity(256);

		for (int i = 0; i < msg.NumSounds; i++) {
			int nSound = sounds.Count; sounds.Add(new());
			ref SoundInfo pSound = ref sounds.AsSpan()[nSound];

			pSound.ReadDelta(ref pDeltaSound, msg.DataIn, protoVersion);

			pDeltaSound = pSound;   // copy delta values

			if (msg.ReliableSound) {
				SoundSequence = (SoundSequence + 1) & SOUND_SEQNUMBER_MASK;
				Assert(pSound.SequenceNumber == 0);
				pSound.SequenceNumber = SoundSequence;
			}
		}
	}
	protected override bool ProcessSounds(SVC_Sounds msg) {
		if (msg.DataIn.Overflowed)
			return false;

		List<SoundInfo> sounds = ListPool<SoundInfo>.Shared.Alloc();

		int startbit = msg.DataIn.BitsRead;

		ProcessSoundsWithProtoVersion(msg, sounds, clientGlobalVariables.NetworkProtocol);

		int nRelativeBitsRead = msg.DataIn.BitsRead - startbit;

		if (msg.Length != nRelativeBitsRead || msg.DataIn.Overflowed) {
			sounds.Clear();

			int nFallbackProtocol = 0;

			if (clientGlobalVariables.NetworkProtocol == Protocol.PROTOCOL_VERSION_18)
				nFallbackProtocol = Protocol.PROTOCOL_VERSION_19;
			else if (clientGlobalVariables.NetworkProtocol == Protocol.PROTOCOL_VERSION_19)
				nFallbackProtocol = Protocol.PROTOCOL_VERSION_18;

			if (nFallbackProtocol != 0) {
				msg.DataIn.Reset();
				msg.DataIn.Seek(startbit);

				ProcessSoundsWithProtoVersion(msg, sounds, nFallbackProtocol);

				nRelativeBitsRead = msg.DataIn.BitsRead - startbit;
			}
		}

		if (msg.Length == nRelativeBitsRead) {
			Span<SoundInfo> soundsSpan = sounds.AsSpan();
			for (int i = 0; i < soundsSpan.Length; ++i) {
				CL.AddSound(in soundsSpan[i]);
			}

			ListPool<SoundInfo>.Shared.Free(sounds);
			return true;
		}

		msg.DataIn.Reset();
		msg.DataIn.Seek(startbit + msg.Length);

		ListPool<SoundInfo>.Shared.Free(sounds);
		return false;
	}
	protected override bool ProcessEntityMessage(SVC_EntityMessage msg) {
		IClientNetworkable? entity = entitylist.GetClientNetworkable(msg.EntityIndex);

		if (entity == null)
			return true;

		byte[] entityData = ArrayPool<byte>.Shared.Rent(MAX_ENTITY_MSG_DATA);
		bf_read entMsg = new("EntityMessage(read)", entityData, entityData.Length);
		int bitsRead = msg.DataIn.ReadBitsClamped(entityData, (uint)msg.Length);
		entMsg.StartReading(entityData, Net.Bits2Bytes(bitsRead));
		entity.ReceiveMessage(msg.ClassID, entMsg);
		ArrayPool<byte>.Shared.Return(entityData, true);

		return true;

	}
	protected override bool ProcessPacketEntities(SVC_PacketEntities msg) {
		if (!msg.IsDelta)
			ClientSidePrediction.OnReceivedUncompressedPacket();
		else {
			if (DeltaTick == -1)
				return true;

			CL.PreprocessEntities();
		}

		if (CL.LocalNetworkBackdoor != null) {
			if (SignOnState == SignOnState.Spawn)
				SetSignonState(SignOnState.Full, ServerCount);

			DeltaTick = GetServerTickCount();
			return true;
		}

		if (!CL.ProcessPacketEntities(msg))
			return false;

		return base.ProcessPacketEntities(msg);
	}

	public override bool SetSignonState(SignOnState state, int count) {
		if (!base.SetSignonState(state, count)) {
			CL.Retry();
			return false;
		}

		ServerCount = count;

		switch (SignOnState) {
			case SignOnState.Challenge:
				EngineVGui?.UpdateProgressBar(LevelLoadingProgress.SignOnChallenge);
				MarkedCRCsUnverified = false;
				break;
			case SignOnState.Connected:
				EngineVGui?.UpdateProgressBar(LevelLoadingProgress.SignOnConnected);
				Scr.BeginLoadingPlaque();

				NetChannel!.Clear();

				NetChannel.SetTimeout(Source.Common.Networking.NetChannel.SIGNON_TIME_OUT);
				NetChannel.SetMaxBufferSize(true, Protocol.MAX_PAYLOAD);

#if GMOD_DLL
				Host.GMOD_BuildConVarUpdateMessage(NetChannel, FCvar.UserInfo, false);
#else
				var convars = new NET_SetConVar();
				Host.BuildConVarUpdateMessage(convars, FCvar.UserInfo, false);
				NetChannel.SendNetMsg(convars);
#endif
				break;
			case SignOnState.New:
				EngineVGui?.UpdateProgressBar(LevelLoadingProgress.SignOnNew);
				StartUpdatingSteamResources();
				return true; // Don't tell the server yet we're at this point
			case SignOnState.PreSpawn:
				break;
			case SignOnState.Spawn:
				EngineVGui?.UpdateProgressBar(LevelLoadingProgress.SignOnSpawn);

				Span<char> mapname = stackalloc char[256];
				CL.SetupMapName(modelloader.GetName(host_state.WorldModel!), mapname);
				mapname = mapname.SliceNullTerminatedString();

				g_ClientDLL!.LevelInitPreEntity(mapname);

				break;
			case SignOnState.Full:
				CL.FullyConnected();
				if (NetChannel != null) {
					NetChannel.SetTimeout(cl_timeout.GetDouble());
					NetChannel.SetMaxBufferSize(true, Protocol.MAX_DATAGRAM_PAYLOAD);
				}

				HostState.OnClientConnected();
				break;
			case SignOnState.ChangeLevel:
				NetChannel!.SetTimeout(Source.Common.Networking.NetChannel.SIGNON_TIME_OUT);

				if (MaxClients > 1)
					EngineVGui?.EnabledProgressBarForNextLoad();

				Scr.BeginLoadingPlaque();

				if (MaxClients > 1)
					EngineVGui?.UpdateProgressBar(LevelLoadingProgress.ChangeLevel);
				break;
		}

		if (state >= SignOnState.Connected && NetChannel != null) {
			var msg = new NET_SignonState(state, ServerCount);
			NetChannel.SendNetMsg(msg);
		}

		return true;
	}

	public override void PacketStart(int incomingSequence, int outgoingAcknowledged) {
		CurrentSequence = incomingSequence;
		CommandAck = outgoingAcknowledged;
	}

	public override void PacketEnd() {
		CL.DispatchSounds();
		if (GetServerTickCount() != DeltaTick)
			return;
		int commandsAcknowledged = CommandAck - LastCommandAck;
		LastCommandAck = CommandAck;
		// clientside prediction todo
		g_ClientSidePrediction.PostNetworkDataReceived(commandsAcknowledged);
	}
	public readonly LinkedList<EventInfo> Events = [];


	protected override bool ProcessGMod_ServerToClient(SVC_GMod_ServerToClient msg) {
		g_ClientDLL?.GMOD_ReceiveServerMessage(new bf_read(msg.RawData.ToArray(), msg.RawData.Length, msg.RawBits), msg.RawBits);
		return true;
	}

	protected override bool ProcessTempEntities(SVC_TempEntities msg) {
		bool reliable = false;

		TimeUnit_t fire_time = GetTime();

		if (MaxClients > 1) {
			TimeUnit_t interpAmount = GetClientInterpAmount();
			fire_time += interpAmount;
		}

		if (msg.NumEntries == 0) {
			reliable = true;
			msg.NumEntries = 1;
		}

		EventFlags flags = reliable ? EventFlags.Reliable : 0;

		bf_read buffer = msg.DataIn;

		int classID = -1;
		C_ServerClassInfo? serverClass = null;
		ClientClass? clientClass = null;
		byte[] data = ArrayPool<byte>.Shared.Rent(EventInfo.MAX_EVENT_DATA);

		bf_write toBuf = new(data, data.Length);
		EventInfo? ei = null;

		for (int i = 0; i < msg.NumEntries; i++) {
			float delay = 0.0f;

			if (buffer.ReadOneBit() != 0)
				delay = (float)buffer.ReadSBitLong(8) / 100.0f;

			toBuf.Reset();

			if (buffer.ReadOneBit() != 0) {
				classID = (int)buffer.ReadUBitLong(ServerClassBits);

				serverClass = ServerClasses?[classID - 1];

				if (serverClass == null) {
					DevMsg($"CL_QueueEvent: missing server class info for {classID - 1}.\n");
					ArrayPool<byte>.Shared.Return(data);
					return false;
				}

				clientClass = FindClientClass(serverClass.ClassName);

				if (clientClass == null || clientClass.RecvTable == null) {
					DevMsg($"CL_QueueEvent: missing client receive table for {serverClass.ClassName}.\n");
					ArrayPool<byte>.Shared.Return(data);
					return false;
				}

				RecvTable.MergeDeltas(clientClass.RecvTable, null, buffer, toBuf);
			}
			else {
				Assert(ei != null);

				uint buffer_size = (uint)PAD_NUMBER(Protocol.Bits2Bytes(ei.Bits), 4);
				bf_read fromBuf = new(ei.Data!, buffer_size);

				RecvTable.MergeDeltas(clientClass!.RecvTable!, fromBuf, buffer, toBuf);
			}

			ei = new();
			Events.AddLast(ei);

			int size = Protocol.Bits2Bytes(toBuf.BitsWritten);

			ei.ClassID = (short)classID;
			ei.FireDelay = fire_time + delay;
			ei.Flags = flags;
			ei.ClientClass = clientClass;
			ei.Bits = toBuf.BitsWritten;

			ei.Data = new byte[size.AlignValue(4)];
			data.AsSpan()[..size].CopyTo(ei.Data);
		}

		ArrayPool<byte>.Shared.Return(data);
		return true;
	}
	public static readonly ConVar cl_allowupload = new("cl_allowupload", "1", FCvar.Archive, "Client uploads customization files");

	public override void FileReceived(ReadOnlySpan<char> fileName, uint transferID) {
		CL.FileReceived(fileName, transferID);
		g_ClientDLL?.FileReceived(fileName, transferID);
	}
	public override void FileRequested(RequestFile type, uint value, uint transferID) {
		// todo
		NetChannel!.DenyFile(transferID);
	}
	public override void FileDenied(uint transferID) {
		CL.FileDenied(transferID);
	}
	public override void FileSent(ReadOnlySpan<char> fileName, uint transferID) {

	}
	public override void ConnectionCrashed(ReadOnlySpan<char> reason) {
		if (SignOnState > SignOnState.None) {
			Debugger.Break();

			Common.ExplainDisconnection(true, $"Disconnect: {reason.SliceNullTerminatedString()}.\n");
			Scr.EndLoadingPlaque();
			Host.EndGame(true, reason);
		}
	}
	protected override bool ProcessVoiceInit(SVC_VoiceInit msg) {
		if (string.IsNullOrEmpty(msg.VoiceCodec) || msg.VoiceCodec[0] == '\0')
			Voice.Deinit();
		else
			Voice.Init(msg.VoiceCodec, msg.SampleRate);

		return true;
	}
	static readonly ConVar cl_voice_filter = new("cl_voice_filter", "", 0, "Filter voice by name substring"); // filter incoming voice data

	protected override bool ProcessVoiceData(SVC_VoiceData msg) {
		Span<byte> received = stackalloc byte[4096];
		int bitsRead = (int)msg.DataIn.ReadBitsClamped(received, (uint)msg.Length);

		int entity = msg.FromClient + 1;
		if (entity == (PlayerSlot + 1))
			Voice.LocalPlayerTalkingAck();

		engineClient.GetPlayerInfo(entity, out PlayerInfo playerinfo);

		if (!cl_voice_filter.GetString().IsStringEmpty && strstr(playerinfo.Name, cl_voice_filter.GetString()).IsEmpty)
			return true;

		if (bitsRead == 0)
			return true;

		if (!Voice.Enabled())
			return true;

		int channel = Voice.GetChannel(entity);
		if (channel == VOICE_CHANNEL_ERROR) {
			channel = Voice.AssignChannel(entity, msg.Proximity);
			if (channel == VOICE_CHANNEL_ERROR) {
				if (Sound.IsInitted())
					ConDMsg($"ProcessVoiceData: Voice.AssignChannel failed for client {entity - 1}!\n");

				return true;
			}
		}

		Voice.AddIncomingData(channel, received, Protocol.Bits2Bytes(bitsRead), CurrentSequence);
		return true;
	}

	WaitForResourcesHandle_t WaitForResourcesHandle;
	public void StartUpdatingSteamResources() {
		Assert(SignOnState == SignOnState.New);

		WaitForResourcesHandle = g_pFileSystem.WaitForResources(LevelBaseName);
		UpdateSteamResources = true;
		ShownSteamResourceUpdateProgress = false;
		DownloadResources = false;
		PrepareClientDLL = false;
	}

	public void CheckUpdatingSteamResources() {
		if (PrepareClientDLL) {
			float prepareProgress = 0f;
			Span<char> szMutableLevelName = stackalloc char[LevelBaseName.Length];
			strcpy(szMutableLevelName, LevelBaseName);

			// if the game .dll doesn't support this call assume everything is prepared
#if !GMOD_DLL
			PrepareLevelResourcesResult result = serverGameDLL.AsyncPrepareLevelResources(szMutableLevelName, _LevelFileName, out PrepareProgress);

			switch (result) {
				case PrepareLevelResourcesResult.InProgress:
					if (!ShownSteamResourceUpdateProgress) {
						// make sure the loading dialog is up
						EngineVGui().StartCustomProgress();
						EngineVGui().ActivateGameUI();
						ShownSteamResourceUpdateProgress = true;
					}
					EngineVGui().UpdateCustomProgressBar(PrepareProgress, g_Localize.Find("#Valve_UpdatingSteamResources"));
					break;
				default:
				case PrepareLevelResourcesResult.Prepared:
					PrepareProgress = 100.f;
					PrepareClientDLL = false;
					UpdateSteamResources = true;
					break;
			}
#else
			prepareProgress = 100f;
			PrepareClientDLL = false;
			UpdateSteamResources = true;
#endif
		}

		if (UpdateSteamResources) {
			bool complete = false;
			float progress = 0.0f;
			g_pFileSystem.GetWaitForResourcesProgress(WaitForResourcesHandle, out progress, out complete);

			if (complete) {
				WaitForResourcesHandle = 0;
				UpdateSteamResources = false;
				DownloadResources = false;

				if (DownloadableFileTable != null) {
					bool allowDownloads = true;
					bool allowSoundDownloads = true;
					bool allowNonMaps = true;
					if (0 == stricmp(cl_downloadfilter.GetString(), "none"))
						allowDownloads = allowSoundDownloads = allowNonMaps = false;
					else if (0 == stricmp(cl_downloadfilter.GetString(), "nosounds"))
						allowSoundDownloads = false;
					else if (0 == stricmp(cl_downloadfilter.GetString(), "mapsonly"))
						allowNonMaps = false;

					if (allowDownloads) {
						Span<char> extension = stackalloc char[MAX_PATH];
						for (int i = 0; i < DownloadableFileTable.GetNumStrings(); ++i) {
							ReadOnlySpan<char> fname = DownloadableFileTable.GetString(i);
							scoped ReadOnlySpan<char> ext = Path.GetExtension(fname);
							ext = extension[..ext.ToLowerInvariant(extension)];

							if (!allowSoundDownloads)
								if (0 == stricmp(extension, "wav") || 0 == stricmp(extension, "mp3"))
									continue;

							if (!allowNonMaps) {
								// The user wants maps only.
								// If the extension is not bsp, skip it.
								if (0 != stricmp(extension, "bsp"))
									continue;
							}

							CL.QueueDownload(fname, i);
						}
					}

					if (CL.GetDownloadQueueSize() != 0) {
						// make sure the loading dialog is up
						EngineVGui().StartCustomProgress();
						EngineVGui().ActivateGameUI();
						DownloadResources = true;
					}
					else {
						DownloadResources = false;
						FinishSignonState_New();
					}
				}
				else {
					Host.Error("Invalid download file table.");
				}
			}
			else if (progress > 0.0f) {
				if (!ShownSteamResourceUpdateProgress) {
					// make sure the loading dialog is up
					EngineVGui().StartCustomProgress();
					EngineVGui().ActivateGameUI();
					ShownSteamResourceUpdateProgress = true;
				}

				// change it to be updating steam resources
				EngineVGui().UpdateCustomProgressBar(progress, g_Localize.Find("#Valve_UpdatingSteamResources"));
			}
		}

		if (DownloadResources) {
			// Check on any HTTP downloads in progress
			bool stillDownloading = CL.DownloadUpdate();

			if (!stillDownloading) {
				DownloadResources = false;
				FinishSignonState_New();
			}
		}
	}
	public void CheckFileCRCsWithServer() { }
	public void SendClientInfo() {
		CLC_ClientInfo info = new CLC_ClientInfo();
		info.SendTableCRC = (int)EngineSendTable.GetCRC();
		info.ServerCount = ServerCount;
		info.IsHLTV = false;
		info.FriendsID = SteamUser.GetSteamID().m_SteamID;
		info.FriendsName = "";

		// check stuff later

		NetChannel!.SendNetMsg(info);
	}

	public void FinishSignonState_New() {
		if (SignOnState != SignOnState.New)
			return;

		if (!MarkedCRCsUnverified) {
			MarkedCRCsUnverified = true;
			fileSystem.MarkAllCRCsUnverified();
		}

		if (!CL.CheckCRCs(LevelFileName))
			Host.Error("Unable to verify map");

		if (sv.State < ServerState.Loading)
			modelloader.ResetModelServerCounts();
		SetModel(1);

		CL.RegisterResources();

		if (host_state.WorldModel == null) {
			Host.Disconnect(true, $"Couldn't load map {LevelFileName}\n");

			return;
		}

		// We can start loading the world now
		Host.Render.LevelInit(); // Tells the rendering system that a new set of world moels exists

		EngineVGui?.UpdateProgressBar(LevelLoadingProgress.SendClientInfo);

		if (NetChannel == null)
			return;

		SendClientInfo();
		g_ClientDLL?.GMOD_RequestLuaFiles();
		var msg = new NET_SignonState(SignOnState, ServerCount);
		NetChannel.SendNetMsg(msg);
	}

	public override void RunFrame() => base.RunFrame();

	public double GetTime() {
		int tickCount = GetClientTickCount();
		double tickTime = tickCount * host_state.IntervalPerTick;
		if (InSimulation)
			return tickTime;

		return tickTime + TickRemainder;
	}
	public bool IsPaused() => Paused;
	public double GetPausedExpireTime() => PausedExpireTime;
	public double GetFrameTime() {
		if (!ClockDriftMgr.Enabled && InSimulation) {
			int elapsedTicks = (GetClientTickCount() - OldTickCount);
			return elapsedTicks * host_state.IntervalPerTick;
		}

		return IsPaused() ? 0 : FrameTime;
	}
	public void SetFrameTime(TimeUnit_t dt) => FrameTime = dt;
	ConVar? s_cl_interp_ratio = null;
	ConVar? s_cl_interp = null;
	ConVar_ServerBounded? s_cl_interp_ratio_bounded = null;

	public TimeUnit_t GetClientInterpAmount() {
		if (s_cl_interp_ratio == null) {
			s_cl_interp_ratio = cvar.FindVar("cl_interp_ratio");
			if (s_cl_interp_ratio == null)
				return 0.1;
		}

		if (s_cl_interp == null) {
			s_cl_interp = cvar.FindVar("cl_interp");
			if (s_cl_interp == null)
				return 0.1;
		}

		float interpRatio = s_cl_interp_ratio.GetFloat();
		float interp = s_cl_interp.GetFloat();

		s_cl_interp_ratio_bounded ??= (ConVar_ServerBounded?)s_cl_interp_ratio;
		if (s_cl_interp_ratio_bounded != null)
			interpRatio = s_cl_interp_ratio_bounded.GetFloat();
		return Math.Max(interpRatio / cl_updaterate.GetFloat(), interp);
	}
	public override void Connect(ReadOnlySpan<char> adr, string sourceTag) {
		base.Connect(adr, sourceTag);
	}

	static readonly char[] szHashedKeyBuffer = new char[64];
	public override string GetCDKeyHash() {
		return "12345678901234567890123456789012";
	}

	public void ClearSounds() // RaphaelIT7: This is used by Snd_Restart_f
	{
		SoundPrecache.ClearInstantiatedReferences();
	}

	public Model? GetModel(int index) {
		if (ModelPrecacheTable == null)
			return null;
		if (index <= 0)
			return null;
		if (index >= ModelPrecacheTable.GetNumStrings()) {
			Assert(false);
			return null;
		}

		PrecacheItem p = ModelPrecache[index];
		Model? model = p.GetModel();
		if (model != null)
			return model;

		if (index == 1) {
			Assert(false);
			Warning("Attempting to get world model before it was loaded\n");
			return null;
		}

		ReadOnlySpan<char> name = ModelPrecacheTable.GetString(index);

		model = modelloader.GetModelForName(name, ModelLoaderFlags.Client);
		if (model == null) {
			ref PrecacheUserData data = ref CL.GetPrecacheUserData(ModelPrecacheTable, index);
			if (!Unsafe.IsNullRef(ref data) && (data.Flags & Res.FatalIfMissing) != 0) {
				Common.ExplainDisconnection(true, $"Cannot continue without model {name}, disconnecting\n");
				Host.Disconnect(true, "Missing model");
			}
		}

		p.SetModel(model);
		return model;
	}
	public void Callback_DecalChanged(object? context, INetworkStringTable stringTable, int stringNumber, ReadOnlySpan<char> newString, ReadOnlySpan<byte> newData) {
		if (stringTable == DecalPrecacheTable)
			SetDecal(stringNumber);
		else
			Assert(false);
	}

	public ReadOnlySpan<char> GetDecalName(int index) {
		if (index <= 0 || DecalPrecacheTable == null)
			return null;

		if (index >= DecalPrecacheTable.GetNumStrings())
			return null;

		PrecacheItem p = DecalPrecache[index];
		ReadOnlySpan<char> d = p.GetDecal();
		return d;
	}

	public void SetDecal(int tableIndex) {
		if (DecalPrecacheTable == null)
			return;

		if (tableIndex < 0 || tableIndex >= DecalPrecacheTable.GetNumStrings())
			return;

		ReadOnlySpan<char> name = DecalPrecacheTable.GetString(tableIndex);
		PrecacheItem p = DecalPrecache[tableIndex];
		p.SetDecal(new(name));

#if !SWDS
		Render.Draw_DecalSetName(tableIndex, name);
#endif
	}

	public void SetModel(int tableIndex) {
		if (ModelPrecacheTable == null)
			return;

		if (tableIndex < 0 || tableIndex >= ModelPrecacheTable.GetNumStrings())
			return;

		ReadOnlySpan<char> name = ModelPrecacheTable.GetString(tableIndex);
		if (tableIndex == 1)
			name = LevelFileName;

		PrecacheItem p = ModelPrecache[tableIndex];
		ref PrecacheUserData data = ref CL.GetPrecacheUserData(ModelPrecacheTable, tableIndex);

		bool loadNow = !Unsafe.IsNullRef(ref data) && (data.Flags & Res.Preload) != 0;
		if (CommandLine.FindParm("-nopreload") != 0 || CommandLine.FindParm("-nopreloadmodels") != 0)
			loadNow = false;
		else if (CommandLine.FindParm("-preload") != 0)
			loadNow = true;

		if (tableIndex == 1) {
			loadNow = true;
		}

		if (loadNow)
			p.SetModel(modelloader.GetModelForName(name, ModelLoaderFlags.Client));
		else
			p.SetModel(null);
	}

	protected override bool ProcessCrosshairAngle(SVC_CrosshairAngle msg) {
		g_ClientDLL!.SetCrosshairAngle(msg.Angle);
		return true;
	}

	internal void ReadPacketEntities(EntityReadInfo u) {
		u.NextOldEntity();

		while (u.UpdateType < UpdateType.Finished) {
			u.HeaderCount--;

			u.IsEntity = (u.HeaderCount >= 0) ? true : false;
			if (u.IsEntity)
				CL.ParseDeltaHeader(u);

			u.UpdateType = UpdateType.PreserveEnt;

			while (u.UpdateType == UpdateType.PreserveEnt) {
				if (CL.DetermineUpdateType(u)) {
					switch (u.UpdateType) {
						case UpdateType.EnterPVS:
							ReadEnterPVS(u);
							break;

						case UpdateType.LeavePVS:
							ReadLeavePVS(u);
							break;

						case UpdateType.DeltaEnt:
							ReadDeltaEnt(u);
							break;

						case UpdateType.PreserveEnt:
							ReadPreserveEnt(u);
							break;

						default:
							DevMsg(1, "ReadPacketEntities: unknown updatetype %i\n", u.UpdateType);
							break;
					}
				}
			}
		}

		if (u.AsDelta && u.UpdateType == UpdateType.Finished)
			ReadDeletions(u);

		if (u.Buf!.Overflowed)
			Host.Error("CL.ParsePacketEntities: buffer read overflow\n");

		if (!u.AsDelta)
			NextCmdTime = 0.0;
	}

	protected override void ReadDeletions(EntityReadInfo u) {
		while (u.Buf!.ReadOneBit() != 0) {
			int idx = (int)u.Buf.ReadUBitLong(MAX_EDICT_BITS);
			CL.DeleteDLLEntity(idx, "ReadDeletions");
		}
	}

	protected override void ReadPreserveEnt(EntityReadInfo u) {
		if (!u.AsDelta) {
			Assert(false);
			ConMsg("WARNING: PreserveEnt on full update\n");
			u.UpdateType = UpdateType.Failed;
			return;
		}

		if (u.OldEntity >= MAX_EDICTS || u.OldEntity < 0 || u.NewEntity >= MAX_EDICTS)
			Host.Error($"CL_ReadPreserveEnt: Entity out of bounds. Old: {u.OldEntity}, New: {u.NewEntity}");

		u.To!.LastEntity = u.OldEntity;
		u.To!.TransmitEntity.Set(u.OldEntity);

		if (CL.cl_entityreport.GetBool())
			CL.RecordEntityBits(u.OldEntity, 0);

		CL.PreserveExistingEntity(u.OldEntity);

		u.NextOldEntity();
	}

	protected override void ReadDeltaEnt(EntityReadInfo u) {
		CL.CopyExistingEntity(u);
		u.NextOldEntity();
	}

	protected override void ReadLeavePVS(EntityReadInfo u) {
		if (!u.AsDelta) {
			Assert(false);
			ConMsg("WARNING: LeavePVS on full update\n");
			u.UpdateType = UpdateType.Failed;
			return;
		}

		if ((u.UpdateFlags & DeltaEncodingFlags.Delete) != 0)
			CL.DeleteDLLEntity(u.OldEntity, "ReadLeavePVS");

		u.NextOldEntity();
	}

	protected override void ReadEnterPVS(EntityReadInfo u) {
		int iClass = (int)u.Buf!.ReadUBitLong(ServerClassBits);

		int iSerialNum = (int)u.Buf!.ReadUBitLong(NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS);

		CL.CopyNewEntity(u, iClass, iSerialNum);

		if (u.NewEntity == u.OldEntity)
			u.NextOldEntity();
	}

	internal void CopyEntityBaseline(int from, int to) {
		for (int i = 0; i < MAX_EDICTS; i++) {
			PackedEntity? blfrom = EntityBaselines[from][i];
			PackedEntity? blto = EntityBaselines[to][i];

			if (blfrom == null) {
				if (blto != null)
					EntityBaselines[to][i] = null;

				continue;
			}

			if (blto == null) {
				blto = EntityBaselines[to][i] = new PackedEntity();
				blto.ClientClass = null;
				blto.ServerClass = null;
				blto.ReferenceCount = 0;
			}

			Assert(blfrom.EntityIndex == i);
			Assert(!blfrom.IsCompressed());

			blto.EntityIndex = blfrom.EntityIndex;
			blto.ClientClass = blfrom.ClientClass;
			blto.ServerClass = blfrom.ServerClass;
			blto.AllocAndCopyPadded(blfrom.GetData());
		}
	}

	public override bool ProcessServerInfo(SVC_ServerInfo msg) {
		// Reset client state
		Clear();

		if (!base.ProcessServerInfo(msg)) {
			Disconnect("CBaseClientState::ProcessServerInfo failed", true);
			return false;
		}

		// is server a HLTV proxy ?
		IsHLTV = msg.IsHLTV;

		// The MD5 of the server map must match the MD5 of the client map. or else
		//  the client is probably cheating.
		ServerMD5 = msg.MapMD5;

		if (MaxClients > 1) {
			if (mp_decals.GetInt() < r_decals.GetInt())
				r_decals.SetValue(mp_decals.GetInt());
		}

		clientGlobalVariables.MaxClients = MaxClients;
		clientGlobalVariables.NetworkProtocol = msg.Protocol;

		StringTableContainer = KeyedSingleton<NetworkStringTableContainer>(Realm.Client);

		CL.ReallocateDynamicData(MaxClients);

		if (sv.IsPaused()) {
			if (msg.TickInterval != host_state.IntervalPerTick) {
				Host.Error($"Expecting interval_per_tick {host_state.IntervalPerTick}, got {msg.TickInterval}\n");
				return false;
			}
		}
		else {
			host_state.IntervalPerTick = msg.TickInterval;
		}

		// Gmod Specific - a global bool. Should we put this into the host state?
		// g_bIsDedicated = msg->m_bIsDedicated;

		g_ClientDLL?.HudVidInit();

		// gHostSpawnCount = m_nServerCount;
#if !SWDS
		((VideoMode_Common)videoMode).MarkClientViewRectDirty();
#endif
		return true;
	}

	internal int LookupModelIndex(ReadOnlySpan<char> name) {
		if (ModelPrecacheTable == null)
			return -1;
		int idx = ModelPrecacheTable.FindStringIndex(name);
		return idx == INetworkStringTable.INVALID_STRING_INDEX ? -1 : idx;
	}

	public int LookupSoundIndex(ReadOnlySpan<char> name) {
		if (SoundPrecacheTable == null)
			return -1;
		int idx = SoundPrecacheTable.FindStringIndex(name);
		return idx == INetworkStringTable.INVALID_STRING_INDEX ? -1 : idx;
	}

	public ReadOnlySpan<char> GetSoundName(int index) {
		if (index <= 0 || SoundPrecacheTable == null)
			return "";

		if (index >= SoundPrecacheTable.GetNumStrings()) {
			return "";
		}

		ReadOnlySpan<char> name = SoundPrecacheTable.GetString(index);
		return name;
	}

	public SfxTable? GetSound(int index) {
		if (index <= 0 || SoundPrecacheTable == null)
			return null;

		if (index >= SoundPrecacheTable.GetNumStrings())
			return null;

		PrecacheItem p = SoundPrecache[index];
		SfxTable? s = p.GetSound();
		if (s != null)
			return s;

		ReadOnlySpan<char> name = SoundPrecacheTable.GetString(index);

		s = Sound.PrecacheSound(name);

		p.SetSound(s!);
		return s;
	}

	internal void SetFriendsID(uint friendsID, ReadOnlySpan<char> friendsName) {
		FriendsID = friendsID;
		FriendsName = new(friendsName.SliceNullTerminatedString());
	}

	internal void SendServerCmdKeyValues(KeyValues keyValues) {
		if (keyValues == null)
			return;
		if (NetChannel == null)
			return;

		// CLC_CmdKeyValues might not exist?
		// What was this anyway
	}

	internal ClientClass? GetClientClass(int index) {
		Assert(index < NumServerClasses);
		return ServerClasses![index]?.ClientClass;
	}
}
