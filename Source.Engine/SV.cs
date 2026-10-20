
using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Networking;
using Source.Common.Server;
using Source.Engine.Server;

using System.Numerics;

using static Source.Common.OptimizedModel;

namespace Source.Engine;

public enum RedirectType
{
	None,
	Client,
	Packet,
	Socket
}

/// <summary>
/// Various serverside methods. In Source, these would mostly be represented by
/// SV_MethodName's in the static global namespace
/// </summary>
public partial class SV(IServiceProvider services, Cbuf Cbuf, ED ED, Host Host, CommonHostState host_state, ICvar cvar, IModelLoader modelloader, ServerGlobalVariables serverGlobalVariables, [FromKeyedServices(Realm.Server)] NetworkStringTableContainer networkStringTableContainerServer, IHostState HostState, ServerPlugin serverPluginHandler)
{
	public static IServerGameDLL? ServerGameDLL;
	public static IServerGameEnts? ServerGameEnts;
	public static IServerGameClients? ServerGameClients;

#if !SWDS
	Con Con => field ??= Singleton<Con>();
	EngineVGui EngineVGui => field ??= Singleton<EngineVGui>();
#endif

	public static readonly ConVar sv_pure_kick_clients = new("sv_pure_kick_clients", "1", 0, "If set to 1, the server will kick clients with mismatching files. Otherwise, it will issue a warning to the client.");
	public static readonly ConVar sv_pure_trace = new("sv_pure_trace", "0", 0, "If set to 1, the server will print a message whenever a client is verifying a CRC for a file.");
	public static readonly ConVar sv_pure_consensus = new("sv_pure_consensus", "5", 0, "Minimum number of file hashes to agree to form a consensus.");
	public static readonly ConVar sv_pure_retiretime = new("sv_pure_retiretime", "900", 0, "Seconds of server idle time to flush the sv_pure file hash cache.");
	public static readonly ConVar sv_pausable = new("sv_pausable", "0", FCvar.Notify, "Is the server pausable.");
	public static readonly ConVar sv_lan = new("sv_lan", "0", 0, "Server is a lan server ( no heartbeat, no authentication, no non-class C addresses )");
	public static readonly ConVar sv_client_predict = new("sv_client_predict", "-1", FCvar.Replicated, "This can be used to force the value of cl_predict for connected clients (only while they are connected).\n-1 = let clients set cl_predict to anything\n0 = force cl_predict to 0\n1 = force cl_predict to 1");
	public static readonly ConVar sv_client_min_interp_ratio = new("sv_client_min_interp_ratio", "1", FCvar.Replicated, "This can be used to limit the value of cl_interp_ratio for connected clients (only while they are connected).\n-1 = let clients set cl_interp_ratio to anything\nany other value = set minimum value for cl_interp_ratio");
	public static readonly ConVar sv_client_max_interp_ratio = new("sv_client_max_interp_ratio", "5", FCvar.Replicated, "This can be used to limit the value of cl_interp_ratio for connected clients (only while they are connected). If sv_client_min_interp_ratio is -1, then this cvar has no effect.");

	public static ConVar sv_cheats = new(nameof(sv_cheats), "0", FCvar.Notify | FCvar.Replicated, "Allow cheats on server", callback: SV_CheatsChanged);

	private static void SV_CheatsChanged(IConVar var, in ConVarChangeContext ctx) {

	}

	static int FatBytes;
	static byte[]? FatPVS;

	public static readonly List<int> g_AreasNetworked = [];

	static void AddToFatPVS(in Vector3 org) {
		Span<byte> pvs = stackalloc byte[BSPFileCommon.MAX_MAP_LEAFS / 8];

		CM.Vis(pvs, pvs.Length, CM.LeafCluster(CM.PointLeafnum(org)), CM.DVIS_PVS);
		for (int i = 0; i < FatBytes; i++)
			FatPVS![i] |= pvs[i];
	}

	public static void ResetPVS(byte[] pvs, int pvssize) {
		FatPVS = pvs;
		FatBytes = Protocol.Bits2Bytes(CM.NumClusters());

		if (FatBytes > pvssize)
			Sys.Error($"SV_ResetPVS:  Size {FatBytes} too big for buffer {pvssize}\n");

		FatPVS.AsSpan(0, FatBytes).Clear();
		g_AreasNetworked.Clear();
	}

	public static void AddOriginToPVS(in Vector3 origin) {
		AddToFatPVS(origin);
		int area = CM.LeafArea(CM.PointLeafnum(origin));
		for (int i = 0; i < g_AreasNetworked.Count; i++) {
			if (g_AreasNetworked[i] == area)
				return;
		}
		g_AreasNetworked.Add(area);
	}

	internal void DumpStringTables() {

	}

	internal bool InitGameDLL() {
		Cbuf.Execute();
		if (sv.DLLInitialized)
			return true;

		ServerGameDLL = services.GetService<IServerGameDLL>();
		if (ServerGameDLL == null) {
			Warning("Failed to load server binary\n");
			goto IgnoreThisDLL;
		}

		ServerGameEnts = services.GetService<IServerGameEnts>();
		if (ServerGameEnts == null) {
			Warning("Could not get IServerGameEnts interface\n");
			goto IgnoreThisDLL;
		}

		ServerGameClients = services.GetService<IServerGameClients>();
		if (ServerGameClients == null) {
			Warning("Could not get IServerGameClients interface\n");
			goto IgnoreThisDLL;
		}

		sv.DLLInitialized = true;
		if (!ServerGameDLL.DLLInit(services))
			Host.Error("IDLLFunctions.DLLInit returned false.\n");

		if (Host.host_name.GetString().Length == 0)
			Host.host_name.SetValue(ServerGameDLL.GetGameDescription());

		InitSendTables(ServerGameDLL.GetAllServerClasses());
		host_state.IntervalPerTick = ServerGameDLL.GetTickInterval();
		sv.InitMaxClients();
		Cbuf.Execute();

		return true;
	IgnoreThisDLL:
		ServerGameDLL = null;
		ServerGameEnts = null;
		ServerGameClients = null;
		return false;
	}

	private void InitSendTables(ServerClass? classes) {
		SendTable[] tables = new SendTable[Constants.MAX_DATATABLES];
		int numTables = BuildSendTablesArray(classes, tables);
		EngineSendTable.Init(tables.AsSpan()[..numTables]);
	}

	readonly ConVar tv_enable = new("tv_enable", "0", FCvar.Notify, "Activates SourceTV on server.");


	[ConCommand(helpText: "Change the maximum number of players allowed on this server.")]
	void maxplayers(in TokenizedCommand args) {
		if (args.ArgC() != 2) {
			ConMsg($"\"maxplayers\" is \"{sv.GetMaxClients()}\"\n");
			return;
		}

		if (sv.IsActive()) {
			ConMsg("Cannot change maxplayers while the server is running\n");
			return;
		}

		SetupMaxPlayers(int.TryParse(args[1], out int i) ? i : 0);
	}

	public void SetupMaxPlayers(int desiredMaxPlayers) {
		int minmaxplayers = 1;
		int maxmaxplayers = Constants.ABSOLUTE_PLAYER_LIMIT;
		int defaultmaxplayers = 1;

		if (ServerGameClients != null) {
			ServerGameClients.GetPlayerLimits(out minmaxplayers, out maxmaxplayers, out defaultmaxplayers);

			if (minmaxplayers < 1)
				Sys.Error($"GetPlayerLimits:  min maxplayers must be >= 1 ({minmaxplayers})");
			else if (defaultmaxplayers < 1)
				Sys.Error($"GetPlayerLimits:  default maxplayers must be >= 1 ({minmaxplayers})");

			if (minmaxplayers > maxmaxplayers || defaultmaxplayers > maxmaxplayers)
				Sys.Error($"GetPlayerLimits:  min maxplayers {minmaxplayers} > max {maxmaxplayers}");
			if (maxmaxplayers > Constants.ABSOLUTE_PLAYER_LIMIT)
				Sys.Error($"GetPlayerLimits:  max players limited to {Constants.ABSOLUTE_PLAYER_LIMIT}");
		}

		// Determine absolute limit
		sv.MaxClientsLimit = maxmaxplayers;

		// Check for command line override
		int newmaxplayers = desiredMaxPlayers;

		if (newmaxplayers >= 1) {
			// Never go above what the game .dll can handle
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

	private int BuildSendTablesArray(ServerClass? classes, SendTable[] tables) {
		int i = 0;
		while (classes != null) {
			tables[i++] = classes.Table;
			classes = classes.Next;
		}
		return i;
	}

	internal void ShutdownGameDLL() {

	}

	public void AllocateEdicts() {
		sv.Edicts = new Edict[sv.MaxEdicts];
		for (int i = 0; i < sv.MaxEdicts; i++) {
			sv.Edicts[i] = new();
			sv.Edicts[i].EdictIndex = i;
			sv.Edicts[i].FreeTime = 0;
		}
		ED.ClearFreeEdictList();
		sv.EdictChangeInfo = new IChangeInfoAccessor[sv.MaxEdicts];
		for (int i = 0; i < sv.MaxEdicts; i++)
			sv.EdictChangeInfo[i] = new IChangeInfoAccessor();
	}

	internal void InitGameServerSteam() {
		if (sv.IsMultiplayer()) {
			Steam3Server().Activate(ServerType.Normal);
			sv.SetQueryPortFromSteamServer();
			ServerGameDLL!.GameServerSteamAPIActivated();
		}
	}

	internal bool ActivateServer() {
		Common.TimestampedLog("SV.ActivateServer");
#if !SWDS
		EngineVGui.UpdateProgressBar(LevelLoadingProgress.ActivateServer);
#endif

		Common.TimestampedLog("serverGameDLL.ServerActivate");

		serverPluginHandler.ServerActivate(sv.Edicts!, sv.NumEdicts, sv.GetMaxClients());

		host_state.IntervalPerTick = ServerGameDLL!.GetTickInterval();
		if (host_state.IntervalPerTick < Constants.MINIMUM_TICK_INTERVAL || host_state.IntervalPerTick > Constants.MAXIMUM_TICK_INTERVAL) {
			Sys.Error($"GetTickInterval returned bogus tick interval ({host_state.IntervalPerTick})[{Constants.MINIMUM_TICK_INTERVAL} to {Constants.MAXIMUM_TICK_INTERVAL} is valid range]");
		}

		Msg($"SV.ActivateServer: setting tickrate to {1.0 / host_state.IntervalPerTick}\n");
		// TODO
		sv.State = ServerState.Active;

		Common.TimestampedLog("SV.CreateBaseline");

		// create a baseline for more efficient communications
		CreateBaseline();

		sv.AllowSignOnWrites = false;

		// set skybox name
		ConVar? skyname = cvar.FindVar("sv_skyname");

		if (skyname != null)
			strcpy(sv.Skyname, skyname.GetString());
		else
			strcpy(sv.Skyname, "unknown");

		Common.TimestampedLog("Send Reconnects");

		// Tell connected clients to reconnect
		sv.ReconnectClients();

		if (sv.IsMultiplayer())
			ConDMsg("%i player server started\n", sv.GetMaxClients());
		else
			ConDMsg("Game started\n");

		if (sv.IsDedicated()) {
			// purge unused models and their data hierarchy (materials, shaders, etc)
			modelloader.PurgeUnusedModels();
		}

		InitGameServerSteam();

		// TODO: Steam3Server

		Common.TimestampedLog("SV_ActivateServer(finished)");

		return true;
	}

	static readonly ConVar sv_voiceenable = new("sv_voiceenable", "1", FCvar.Archive | FCvar.Notify); // set to 0 to disable all voice forwarding.
	static readonly ConVar sv_voicecodec = new("sv_voicecodec", "vaudio_celt", 0,
							 "Specifies which voice codec to use. Valid options are:\n" +
							 "vaudio_speex - Legacy Speex codec (lowest quality)\n" +
							 "vaudio_celt - Newer CELT codec\n" +
							 "steam - Use Steam voice API");

	public static void WriteVoiceCodec(bf_write buf) {
		// Only send in multiplayer. Otherwise, we don't want voice.

		ReadOnlySpan<char> codec = sv.IsMultiplayer() ? sv_voicecodec.GetString() : null;
		int sampleRate = !codec.IsEmpty ? Voice.GetDefaultSampleRate(codec) : 0;
		SVC_VoiceInit voiceinit = new(codec, sampleRate);
		voiceinit.WriteToBuffer(buf);
	}

	// Gets voice data from a client and forwards it to anyone who can hear this client.
	static readonly ConVar voice_debugfeedbackfrom = new("voice_debugfeedbackfrom", "0");

	public static void BroadcastVoiceData(IClient client, int bytes, byte[] data) {
		// Disable voice?
		if (sv_voiceenable.GetInt() == 0)
			return;

		// Build voice message once
		SVC_VoiceData voiceData = new();
		voiceData.FromClient = client.GetPlayerSlot();
		voiceData.Length = bytes * 8;    // length in bits
		voiceData.DataOut = data;

		if (voice_debugfeedbackfrom.GetBool())
			Msg($"Sending voice from: {client.GetClientName()} - playerslot: {client.GetPlayerSlot() + 1}\n");

		for (int i = 0; i < sv.GetClientCount(); i++) {
			IClient destClient = sv.GetClient(i)!;

			bool self = destClient == client;

			// Only send voice to active clients
			if (!destClient.IsActive())
				continue;

			// Does the game code want cl sending to this client?

			bool hearsPlayer = destClient.IsHearingClient(voiceData.FromClient);
			voiceData.Proximity = destClient.IsProximityHearingClient(voiceData.FromClient);

			if (!hearsPlayer && !self)
				continue;

			voiceData.Length = bytes * 8;

			// Is loopback enabled?
			if (!hearsPlayer) {
				// Still send something, just zero length (this is so the client 
				// can display something that shows knows the server knows it's talking).
				voiceData.Length = 0;
			}

			destClient.SendNetMsg(voiceData);
		}
	}

	private void CreateBaseline() {
		WriteVoiceCodec(sv.Signon);

		ServerClass? pClasses = serverGameDLL.GetAllServerClasses();

		if (sv_sendtables.GetBool()) {
			sv.FullSendTablesBuffer.EnsureCapacity(288000 /*NET_MAX_PAYLOAD*/);
			sv.FullSendTables.StartWriting(sv.FullSendTablesBuffer.Base(), (nuint)sv.FullSendTablesBuffer.Count());

			PackedEntities.WriteSendTables(pClasses, sv.FullSendTables);

			if (sv.FullSendTables.Overflowed) {
				Host.Error("SV_CreateBaseline: WriteSendTables overflow.\n");
				return;
			}

			PackedEntities.WriteClassInfos(pClasses!, sv.FullSendTables);

			if (sv.FullSendTables.Overflowed) {
				Host.Error("SV_CreateBaseline: WriteClassInfos overflow.\n");
				return;
			}
		}

		if (CL.LocalNetworkBackdoor == null) {
			int count = 0;
			int bytes = 0;

			for (int entnum = 0; entnum < sv.NumEdicts; entnum++) {
				Edict edict = sv.Edicts![entnum];

				if (edict.IsFree() || edict.GetUnknown() == null)
					continue;

				ServerClass? pClass = edict.GetNetworkable()?.GetServerClass();

				if (pClass == null) {
					Assert(pClass);
					continue;
				}

				if (pClass.InstanceBaselineIndex != INetworkStringTable.INVALID_STRING_INDEX)
					continue;

				SendTable pSendTable = pClass.Table;

				byte[] packedData = new byte[Constants.MAX_PACKEDENTITY_DATA];
				bf_write writeBuf = new(packedData, Constants.MAX_PACKEDENTITY_DATA);

				if (!EngineSendTable.Encode(pSendTable, edict.GetUnknown(), writeBuf, entnum, null, false))
					Host.Error($"SV_CreateBaseline: SendTable_Encode returned false (ent {entnum}).\n");

				PackedEntities.EnsureInstanceBaseline(pClass, entnum, packedData, writeBuf.BytesWritten);

				bytes += writeBuf.BytesWritten;
				count++;
			}
			DevMsg($"Created class baseline: {count} classes, {bytes} bytes.\n");
		}

		g_GameEventManager.ReloadEventDefinitions();

		SVC_GameEventList gameevents = new();
		byte[] data = new byte[288000 /*NET_MAX_PAYLOAD*/];
		gameevents.DataOut.StartWriting(data, data.Length);

		// gameEventManager.WriteEventList(&gameevents);
		gameevents.WriteToBuffer(sv.Signon);
	}
	public bool HasPlayers() => sv.GetClientCount() > 0;

	public bool IsSimulating() {
		if (sv.IsPaused())
			return false;

#if !SWDS
		if (!sv.IsMultiplayer()) {
			if (cl.IsActive() && (Con.IsVisible() || EngineVGui.ShouldPause()))
				return false;
		}
#endif
		return true;
	}
	ConVar? sv_noclipduringpause;
	internal void Frame(bool finalTick) {
		if (ServerGameDLL != null && finalTick)
			ServerGameDLL.Think(finalTick);

		if (!sv.IsActive() || !Host.ShouldRun()) {
			return;
		}

		serverGlobalVariables.FrameTime = host_state.IntervalPerTick;

		bool isSimulating = IsSimulating();
		bool sendDuringPause = sv_noclipduringpause != null ? sv_noclipduringpause.GetBool() : false;

		sv.RunFrame();

		bool simulated = false;
		if (HasPlayers()) {
			bool serverCanSimulate = true; // TODO: Restoring

			if (serverCanSimulate && (isSimulating || sendDuringPause)) {
				simulated = true;
				sv.TickCount++;
				networkStringTableContainerServer.SetTick(sv.TickCount);
			}

			Think(isSimulating);
		}
		else if (sv.IsMultiplayer()) {
			Think(false);
		}

		sv.SimulatingTicks = simulated;

		if (finalTick) {
			if (!EngineThreads.IsEngineThreaded() || sv.IsMultiplayer())
				SendClientUpdates(isSimulating, sendDuringPause);
			// else
			// DeferredServerWork = CreateFunctor(SendClientUpdates, isSimulating, sendDuringPause);

		}

		if (sv.IsMultiplayer())
			Steam3Server().RunFrame();
	}

	public bool ForcedSend;

	private void SendClientUpdates(bool isSimulating, bool sendDuringPause) {
		bool forcedSend = ForcedSend;
		ForcedSend = false;

		PreClientUpdate(isSimulating);
		sv.SendClientMessages(isSimulating || forcedSend);
		networkStringTableContainerServer.SetTick(sv.TickCount + 1);
	}

	private void PreClientUpdate(bool isSimulating) {
		ServerGameDLL?.PreClientUpdate(isSimulating);
	}

	public double TimeForceShutdown;

	public static ServerClass? FindServerClass(ReadOnlySpan<char> name) {
		ServerClass? cur = serverGameDLL.GetAllServerClasses();
		while (cur != null) {
			if (stricmp(cur.NetworkName, name) == 0)
				return cur;

			cur = cur.Next;
		}

		return null;
	}

	public static ServerClass? FindServerClass(int index) {
		ServerClass? cur = serverGameDLL.GetAllServerClasses();
		int count = 0;

		while ((count < index) && (cur != null)) {
			count++;
			cur = cur.Next;
		}

		return null;
	}

	private void Think(bool isSimulating) {
		sv.UpdateHibernationState();

		if (TimeForceShutdown > 0.0) {
			if (TimeForceShutdown < Platform.Time) {
				Warning("Server shutting down because sv_shutdown was requested and timeout has expired.\n");
				HostState.Shutdown();
			}
		}

		serverGlobalVariables.TickCount = sv.TickCount;
		serverGlobalVariables.CurTime = sv.GetTime();
		serverGlobalVariables.FrameTime = isSimulating ? host_state.IntervalPerTick : 0;

		isSimulating = isSimulating && (sv.IsMultiplayer() || cl.IsActive());
		serverPluginHandler.GameFrame(isSimulating);
	}

	public static readonly ConVar sv_precache_modelbits = new("sv_precache_modelbits", "12", 0, "number of bits to use for the modelprecache stringtable", 4, 15);
	public static readonly ConVar sv_precache_generalbits = new("sv_precache_generalbits", "9", 0, "number of bits to use for the generalprecache stringtable", 4, 15);
	public static readonly ConVar sv_precache_soundbits = new("sv_precache_soundbits", "14", 0, "number of bits to use for the soundprecache stringtable", 4, 15);
	public static readonly ConVar sv_precache_decalbits = new("sv_precache_decalbits", "9", 0, "number of bits to use for the decalprecache stringtable", 4, 15);

	internal void CreateNetworkStringTables() {
		networkStringTableContainerServer.RemoveAllTables();

		networkStringTableContainerServer.SetAllowCreation(true);

		sv.CreateEngineStringTables();

		serverGameDLL.CreateNetworkStringTables();

		networkStringTableContainerServer.SetAllowCreation(false);
	}
}
