global using static Source.Engine.Server.SvClientConvars;

using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Networking;

using System.Buffers;
using System.Runtime.InteropServices;

namespace Source.Engine.Server;

public static class SvClientConvars
{
	public static readonly ConVar sv_timeout = new("sv_timeout", "65", 0, "After this many seconds without a message from a client, the client is dropped");
	public static readonly ConVar sv_maxrate = new("sv_maxrate", "0", FCvar.Replicated, "Max bandwidth rate allowed on server, 0 == unlimited");
	public static readonly ConVar sv_minrate = new("sv_minrate", "3500", FCvar.Replicated, "Min bandwidth rate allowed on server, 0 == unlimited");
	public static readonly ConVar sv_maxupdaterate = new("sv_maxupdaterate", "66", FCvar.Replicated, "Maximum updates per second that the server will allow");
	public static readonly ConVar sv_minupdaterate = new("sv_minupdaterate", "10", FCvar.Replicated, "Minimum updates per second that the server will allow");
	public static readonly ConVar sv_stressbots = new("sv_stressbots", "0", 0, "If set to 1, the server calculates data and fills packets to bots. Used for perf testing.");
	public static readonly ConVar sv_allowdownload = new("sv_allowdownload", "1", 0, "Allow clients to download files");
	public static readonly ConVar sv_allowupload = new("sv_allowupload", "1", 0, "Allow clients to upload customizations files");
	public static readonly ConVar sv_sendtables = new("sv_sendtables", "0", FCvar.DevelopmentOnly, "Force full sendtable sending path.");
}

/// <summary>
/// Represents a player client in a game server
/// </summary>
public class GameClient : BaseClient
{
	public GameClient(int slot, BaseServer server) {
		Clear();

		ClientSlot = slot;
		EntityIndex = slot + 1;
		Server = server;
		CurrentFrame = null;
		IsInReplayMode = false;
	}
	public bool VoiceLoopback;
	public PlayerBitSet VoiceStreams;
	public PlayerBitSet VoiceProximity;
	public int LastMovementTick;
	public int SoundSequence;
	public Edict Edict = null!;
	public readonly List<SoundInfo> Sounds = [];
	public Edict? ViewEntity;
	public ClientFrame? CurrentFrame;
	public readonly ClientFrameManager FrameManager = new();
	public readonly CheckTransmitInfo PackInfo = new();
	public bool IsInReplayMode;
	public readonly CheckTransmitInfo PrevPackInfo = new();
	public MaxEdictsBitSet PrevTransmitEdict;

	protected override bool ProcessClientInfo(CLC_ClientInfo msg) {
		base.ProcessClientInfo(msg);

		if (HLTV) {
			HLTV = false;
			Disconnect("ProcessClientInfo: SourceTV can not connect to game directly.\n");
			return false;
		}

		if (sv_allowupload.GetBool())
			DownloadCustomizations();

		return true;
	}

	protected override bool ProcessMove(CLC_Move m) {
		if (!IsActive())
			return true;

		if (LastMovementTick == sv.TickCount) {
			// Only one movement command per frame, someone is cheating.
			return true;
		}

		LastMovementTick = (int)sv.TickCount;

		int totalCmds = m.NewCommands + m.BackupCommands;
		int netDrop = NetChannel.GetDropNumber();

		bool ignore = !sv.IsActive();
#if SWDS
		bool paused = sv.IsPaused();
#else
		bool paused = sv.IsPaused() || (!sv.IsMultiplayer() && false /*Con_IsVisible todo*/);
#endif

		serverGlobalVariables.CurTime = sv.GetTime();
		serverGlobalVariables.FrameTime = host_state.IntervalPerTick;

		int startBit = m.DataIn.BitsRead;

		SV.ServerGameClients!.ProcessUsercmds(Edict, m.DataIn, m.NewCommands, totalCmds, netDrop, ignore, paused);

		if (m.DataIn.Overflowed) {
			Disconnect("ProcessUsercmds:  Overflowed reading usercmd data (check sending and receiving code for mismatches)!\n");
			return false;
		}

		int endBit = m.DataIn.BitsRead;
		if (m.Length != (endBit - startBit)) {
			Disconnect("ProcessUsercmds:  Incorrect reading frame (check sending and receiving code for mismatches)!\n");
			return false;
		}

		return true;
	}

	protected override bool ProcessVoiceData(CLC_VoiceData msg) {
		byte[] voiceDataBuffer = new byte[4096];
		int bitsRead = (int)msg.DataIn.ReadBitsClamped(voiceDataBuffer, (uint)msg.Length);

		SV.BroadcastVoiceData(this, Protocol.Bits2Bytes(bitsRead), voiceDataBuffer);

		return true;
	}

	protected override bool ProcessCmdKeyValues(CLC_CmdKeyValues msg) {
		SV.ServerGameClients!.ClientCommandKeyValues(Edict, msg.KeyValues!);
		return true;
	}

	protected override bool ProcessRespondCvarValue(CLC_RespondCvarValue msg) {
		if (msg.Cookie > 0)
			g_pServerPluginHandler?.OnQueryCvarValueFinished(msg.Cookie, Edict, msg.StatusCode, msg.CvarName, msg.CvarValue);

		return true;
	}

	public static readonly ConVar sv_pure_kick_clients = new( "sv_pure_kick_clients", "1", 0, "If set to 1, the server will kick clients with mismatching files. Otherwise, it will issue a warning to the client." );
	public static readonly ConVar sv_pure_trace = new( "sv_pure_trace", "0", 0, "If set to 1, the server will print a message whenever a client is verifying a CRC for a file." );
	public static readonly ConVar sv_pure_consensus = new( "sv_pure_consensus", "5", 0, "Minimum number of file hashes to agree to form a consensus." );
	public static readonly ConVar sv_pure_retiretime = new( "sv_pure_retiretime", "900", 0, "Seconds of server idle time to flush the sv_pure file hash cache." );


	protected override bool ProcessFileCRCCheck(CLC_FileCRCCheck msg) {
		if (!sv.IsInPureServerMode())
			return true;

		Span<char> warningStr = stackalloc char[1024];

		// The client may send us files we don't care about, so filter them here
		//	if ( !sv.GetPureServerWhitelist()->GetForceMatchList()->IsFileInList( msg->m_szFilename ) )
		//		return true;

		// first check against all the other files users have sent
		FileHash filehash = default;
		filehash.MD5Contents = msg.MD5;
		filehash.CRCIOSequence = msg.CRCIOs;
		filehash.FileHashType = (FileHashType)msg.FileHashType;
		filehash.FileLen = msg.FileFraction;
		filehash.PackFileNumber = msg.PackFileNumber;
		filehash.PackFileID = msg.PackFileID;

		ReadOnlySpan<char> path = msg.PathID;
		ReadOnlySpan<char> fileName = msg.Filename;
		if (g_PureFileTracker.DoesFileMatch(path, fileName, msg.FileFraction, ref filehash, GetNetworkID())) {
			// track successful file
		}
		else {
			sprintf(warningStr, "Pure server: file [%s]\\%s does not match the server's file.").S(path).S(fileName);
		}

		// still ToDo:
		// 1. make sure the user sends some files
		// 2. make sure the user doesnt skip any files
		// 3. make sure the user sends the right files...

		if (warningStr[0] != '\0') {
			if (sv_pure_kick_clients.GetInt() != 0) 
				Disconnect(warningStr);
			else {
				ClientPrintf($"Warning: {warningStr}\n");
				if (sv_pure_trace.GetInt() >= 1) 
					Msg($"[{GetNetworkIDString()}] {warningStr.SliceNullTerminatedString()}\n");
			}
		}
		else {
			if (sv_pure_trace.GetInt() >= 2) 
				Msg($"Pure server CRC check: client {GetClientName()} passed check for [{msg.PathID}]\\{msg.Filename}\n");
		}

		return true;
	}

	protected override bool ProcessFileMD5Check(CLC_FileMD5Check msg) {
		return true; // legacy message
	}

	// bool ProcessSaveReplay(CLC_SaveReplay pMsg) { } // seems to be removed?

	void DownloadCustomizations() { }

	public override void Connect(ReadOnlySpan<char> name, int userID, INetChannel netChannel, bool fakePlayer, int clientChallenge) {
		base.Connect(name, userID, netChannel, fakePlayer, clientChallenge);

		Edict = sv.Edicts![EntityIndex];

		PackInfo.ClientEnt = Edict;
		PackInfo.PVSSize = PackInfo.PVS.Length;

		IGameEvent? evnt = gameEventManager.CreateEvent("player_connect");
		if (evnt != null) {
			evnt.SetInt("userid", GetUserID());
			evnt.SetInt("index", ClientSlot);
			evnt.SetString("name", name);
			evnt.SetString("networkid", GetNetworkIDString());
			evnt.SetString("address", netChannel != null ? netChannel.GetAddress() : "none");
			evnt.SetInt("bot", fakePlayer ? 1 : 0);
			gameEventManager.FireEvent(evnt);
		}

		evnt = gameEventManager.CreateEvent("player_connect_client");
		if (evnt != null) {
			evnt.SetInt("userid", GetUserID());
			evnt.SetInt("index", ClientSlot);
			evnt.SetString("name", name);
			evnt.SetString("networkid", GetNetworkIDString());
			evnt.SetInt("bot", fakePlayer ? 1 : 0);
			gameEventManager.FireEvent(evnt);
		}
	}

	public void SetupPackInfo(FrameSnapshot snapshot) {
		PackInfo.PVSSize = (GetCollisionBSPData().NumClusters + 7) / 8;
		SV.ServerGameClients!.ClientSetupVisibility(ViewEntity, PackInfo.ClientEnt!, PackInfo.PVS, PackInfo.PVSSize);

		CurrentFrame = FrameManager.AllocateFrame();
		CurrentFrame.Init(snapshot);

		PackInfo.TransmitEdict.ClearAll();
		PackInfo.TransmitAlways.ClearAll();

		int maxFrames = MAX_CLIENT_FRAMES;
		if (maxFrames < FrameManager.AddClientFrame(CurrentFrame))
			FrameManager.RemoveOldestFrame();

		PackInfo.AreasNetworked = 0;
		int areaCount = SV.g_AreasNetworked.Count;
		for (int j = 0; j < areaCount; j++) {
			PackInfo.Areas[PackInfo.AreasNetworked] = SV.g_AreasNetworked[j];
			PackInfo.AreasNetworked++;

			Assert(PackInfo.AreasNetworked < CheckTransmitInfo.MAX_WORLD_AREAS);
		}

		CM.SetupAreaFloodNums(PackInfo.AreaFloodNums, out PackInfo.MapAreas);
	}

	public void SetupPrevPackInfo() {
		PrevTransmitEdict = PackInfo.TransmitEdict;

		PrevPackInfo.AreasNetworked = PackInfo.AreasNetworked;
		((ReadOnlySpan<int>)PackInfo.Areas)[..PackInfo.AreasNetworked].CopyTo(PrevPackInfo.Areas);

		PrevPackInfo.PVSSize = PackInfo.PVSSize;
		PackInfo.PVS.AsSpan(0, PackInfo.PVSSize).CopyTo(PrevPackInfo.PVS);

		PrevPackInfo.MapAreas = PackInfo.MapAreas;
		((ReadOnlySpan<byte>)PackInfo.AreaFloodNums)[..PackInfo.MapAreas].CopyTo(PrevPackInfo.AreaFloodNums);
	}

	// void SetRate(int nRate, bool force) { }

	// void SetUpdateRate(int udpaterate, bool force) { }

	public override void UpdateUserSettings() {
		// set voice loopback
		VoiceLoopback = ConVars!.GetInt("voice_loopback", 0) != 0;

		base.UpdateUserSettings();

		// Give entity dll a chance to look at the changes.
		// Do this after BaseClient.UpdateUserSettings() so name changes like prepending a (1)
		// take effect before the server dll sees the name.
		serverPluginHandler.ClientSettingsChanged(Edict);
	}

	public override bool IsHearingClient(int index) {
		if (IsHLTV())
			return true;

		if (index == GetPlayerSlot())
			return VoiceLoopback;

		GameClient client = sv.Client(index);
		return client.VoiceStreams.Get(GetPlayerSlot()) != 0;
	}

	public override bool IsProximityHearingClient(int index) {
		GameClient client = sv.Client(index);
		return client.VoiceProximity.Get(GetPlayerSlot()) != 0;
	}

	public override void Inactivate() {
		if (Edict != null && !Edict.IsFree())
			Server.RemoveClientFromGame(this);

		if (IsHLTV()) {

		}

		base.Inactivate();

		Sounds.Clear();
		VoiceStreams.ClearAll();
		VoiceProximity.ClearAll();

		FrameManager.DeleteClientFrames(-1);
	}

	protected override bool UpdateAcknowledgedFramecount(int tick) {
		if (tick != DeltaTick) {
			int removeTick = tick;

			if (removeTick > 0)
				FrameManager.DeleteClientFrames(removeTick);
		}

		return base.UpdateAcknowledgedFramecount(tick);
	}

	public override void Clear() {
		if (HLTV) {

		}

		if (Replay) {

		}

		base.Clear();

		FrameManager.DeleteClientFrames(-1);

		Sounds.Clear();
		VoiceStreams.ClearAll();
		VoiceProximity.ClearAll();
		Edict = null!;
		ViewEntity = null;
		VoiceLoopback = false;
		LastMovementTick = 0;
		SoundSequence = 0;
	}

	public override void Reconnect() {
		sv.RemoveClientFromGame(this);
		base.Reconnect();
	}

	public override void ConnectionClosing(ReadOnlySpan<char> reason) {
		// SV_RedirectEnd();
		// Check for printf format tokens in this reason string. Crash exploit.
		Disconnect(!reason.IsEmpty && reason.IndexOf('%') < 0 ? reason : "Connection closing");
	}

	public override void Disconnect(ReadOnlySpan<char> reason) {
		if (SignOnState == SignOnState.None)
			return; // no recursion

		// notify other clients of player leaving the game
		// send the username and network id so we don't depend on the BasePlayer pointer
		IGameEvent? evnt = gameEventManager.CreateEvent("player_disconnect");
		if (evnt != null) {
			evnt.SetInt("userid", GetUserID());
			evnt.SetString("reason", reason);
			evnt.SetString("name", GetClientName());
			evnt.SetString("networkid", GetNetworkIDString());
			evnt.SetInt("bot", IsFakeClient() ? 1 : 0);
			gameEventManager.FireEvent(evnt);
		}

		Server.RemoveClientFromGame(this);

		base.Disconnect(reason);
	}

	protected override bool SetSignOnState(SignOnState state, int spawncount) {
		if (state == SignOnState.Connected) {
			if (!CheckConnect())
				return false;

			NetChannel!.SetTimeout(Source.Common.Networking.NetChannel.SIGNON_TIME_OUT);
			NetChannel.SetFileTransmissionMode(false);
			NetChannel.SetMaxBufferSize(true, Protocol.MAX_PAYLOAD);
		}
		else if (state == SignOnState.New) {
			if (!sv.IsMultiplayer())
				sv.InstallClientStringTableMirrors();
		}
		else if (state == SignOnState.Full) {
			if (sv.LoadGame) {
				// sv.FinishRestore();
			}

			NetChannel!.SetTimeout(sv_timeout.GetFloat());
			NetChannel.SetFileTransmissionMode(true);
		}

		return base.SetSignOnState(state, spawncount);
	}

	// void SendSound(SoundInfo sound, bool isReliable) { }

	protected override void WriteGameSounds(bf_write buf) {
		if (Sounds.Count == 0)
			return;

		byte[] data = new byte[Protocol.MAX_PAYLOAD];
		SVC_Sounds msg = new();
		msg.DataOut.StartWriting(data, Protocol.MAX_PAYLOAD, 0);

		int soundCount = FillSoundsMessage(msg);
		msg.WriteToBuffer(buf);
	}

	int FillSoundsMessage(SVC_Sounds msg) {
		int i, count = Sounds.Count;

		int max = Server.IsMultiplayer() ? 32 : 255;

		if (count > max)
			count = max;

		if (count == 0)
			return 0;

		SoundInfo defaultSound = new();
		defaultSound.SetDefault();

		SoundInfo deltaSound = defaultSound;

		msg.NumSounds = count;
		msg.ReliableSound = false;
		msg.SetReliable(false);

		Assert(msg.DataOut.BitsLeft > 0);

		for (i = 0; i < count; i++) {
			SoundInfo sound = Sounds[i];
			sound.WriteDelta(ref deltaSound, msg.DataOut, Protocol.VERSION); // FIXME proto version
			deltaSound = sound;
		}

		int remove = Sounds.Count - (count + max);

		if (remove > 0) {
			DevMsg($"Warning! Dropped {remove} unreliable sounds for client {Name}.\n");
			count += remove;
		}

		if (count > 0)
			Sounds.RemoveRange(0, count);

		Assert(Sounds.Count <= max);

		return msg.NumSounds;
	}

	bool CheckConnect() {
		// Allow the game dll to reject this client.
		Span<char> rejectReason = stackalloc char[128];
		"Connection rejected by game\n".CopyTo(rejectReason);

		if (!serverPluginHandler.ClientConnect(Edict, Name, NetChannel!.GetAddress(), rejectReason)) {
			// Reject the connection and drop the client.
			Disconnect(rejectReason.SliceNullTerminatedString());
			return false;
		}

		return true;
	}

	public override void ActivatePlayer() {
		base.ActivatePlayer();

		Common.TimestampedLog("CGameClient::ActivatePlayer -start");

		if (!sv.LoadGame) {
			serverGlobalVariables.CurTime = sv.GetTime();
			Common.TimestampedLog("g_pServerPluginHandler->ClientPutInServer");
			serverPluginHandler.ClientPutInServer(Edict, Name);
		}

		Common.TimestampedLog("g_pServerPluginHandler->ClientActivate");

		serverPluginHandler.ClientActive(Edict, sv.LoadGame);

		Common.TimestampedLog("g_pServerPluginHandler->ClientSettingsChanged");

		serverPluginHandler.ClientSettingsChanged(Edict);

		IGameEvent? evnt = gameEventManager.CreateEvent("player_activate");

		if (evnt != null) {
			evnt.SetInt("userid", GetUserID());
			gameEventManager.FireEvent(evnt);
		}

		Common.TimestampedLog("CGameClient::ActivatePlayer -end");
	}

	protected override bool ProcessGMod_ClientToServer(CLC_GMod_ClientToServer msg) {
		serverGameClients.GMOD_ReceiveClientMessage(ClientSlot, Edict, new bf_read(msg.RawData.ToArray(), msg.RawData.Length, msg.RawBits), msg.RawBits);
		return true;
	}

	protected override bool SendSignonData() {
		bool clientHasDifferentTables = false;

		if (sv.FullSendTables.Overflowed) {
			// Host.Error($"Send Table signon buffer overflowed {sv.FullSendTables.BytesWritten} bytes!!!\n");
			return false;
		}

		if (SendTableCRC != 0) {

		}

		if (clientHasDifferentTables) {

		}

		SVC_ClassInfo msg = new() {
			NumServerClasses = Server.ServerClasses,
			CreateOnClient = true
		};
		NetChannel.SendNetMsg(msg);

		if (!base.SendSignonData())
			return false;

		SoundSequence = 1;

		return true;
	}

	protected override void SpawnPlayer() {
		if (sv.LoadGame)
			sv.SetPaused(false);
		else {
			Assert(SV.ServerGameEnts);
			Edict.InitializeEntityDLLFields();
		}

		EntityIndex = ClientSlot + 1;
		IsInReplayMode = false;

		SVC_SetView msg = new() {
			EntityIndex = EntityIndex
		};
		SendNetMsg(msg);

		base.SpawnPlayer();

		SV.ServerGameClients!.ClientSpawned(Edict);
	}

	protected override ClientFrame? GetDeltaFrame(int tick) {
		Assert(!IsHLTV());
		return FrameManager.GetClientFrame(tick);
	}

	void WriteViewAngleUpdate() {
		if (IsFakeClient())
			return;

		PlayerEngineState pl = SV.ServerGameClients!.GetPlayerState(Edict);
		Assert(pl != null);

		if (pl != null && pl.FixAngle != (int)FixAngle.None) {
			if (pl.FixAngle == (int)FixAngle.Relative) {
				SVC_FixAngle fixAngle = new(true, pl.AngleChange);
				NetChannel.SendNetMsg(fixAngle);
				pl.AngleChange.Init();
			}
			else {
				SVC_FixAngle fixAngle = new(false, pl.ViewingAngle);
				NetChannel.SendNetMsg(fixAngle);
			}

			pl.FixAngle = (int)FixAngle.None;
		}
	}

	static readonly string[] CLCommands = [ // Shouldn't be here
		"status",
		"pause",
		"setpause",
		"unpause",
		"ping",
		"rpt_server_enable",
		"rpt_client_enable",
#if !SWDS
		"rpt",
		"rpt_connect",
		"rpt_password",
		"rpt_screenshot",
		"rpt_download_log",
#endif
	];

	bool IsEngineClientCommand(in TokenizedCommand args) {
		if (args.ArgC() == 0)
			return false;

		for (int i = 0; i < CLCommands.Length; i++) {
			if (args[0].Equals(CLCommands[i], StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	// bool SendNetMsg(INetMessage msg, bool forceReliable) { }

	public override bool ExecuteStringCommand(ReadOnlySpan<char> c) {
		if (base.ExecuteStringCommand(c))
			return true;

		TokenizedCommand args = new();

		if (!args.Tokenize(c))
			return false;

		if (args.ArgC() == 0)
			return false;

		if (IsEngineClientCommand(args)) {
			cmd.ExecuteCommand(ref args, CommandSource.Client, ClientSlot);
			return true;
		}

		ConCommandBase? command = cvar.FindCommandBase(args[0]);

		if (command != null && command.IsCommand() && command.IsFlagSet(FCvar.GameDLL)) {
			// Allow cheat commands in singleplayer, debug, or multiplayer with sv_cheats on
			// NOTE: Don't bother with rpt stuff; commands that matter there shouldn't have FCVAR_GAMEDLL set
			if (command.IsFlagSet(FCvar.Cheat)) {
				if (sv.IsMultiplayer() && !Host.CanCheat())
					return false;
			}

			if (command.IsFlagSet(FCvar.SingleplayerOnly)) {
				if (sv.IsMultiplayer())
					return false;
			}

			// Don't allow clients to execute commands marked as development only.
			if (command.IsFlagSet(FCvar.DevelopmentOnly))
				return false;

			serverPluginHandler.SetCommandClient(ClientSlot);

			cmd.Dispatch(command, args);
		}
		else
			serverPluginHandler.ClientCommand(Edict, args);

		return true;
	}

	public override void SendSnapshot(ClientFrame frame) {
		if (HLTV) {

		}

		WriteViewAngleUpdate();

		base.SendSnapshot(frame);
	}

	public override bool ShouldSendMessages() {
		if (HLTV) {

		}

		if (IsFakeClient())
			return sv_stressbots.GetBool();

		return base.ShouldSendMessages();
	}

	// void FileReceived(ReadOnlySpan<char> fileName, uint transferID) { }

	public override void FileRequested(RequestFile type, uint value, uint transferID) {
		DevMsg($"File request (type {type}, value {value}) from client {NetChannel.GetAddress()}.\n");
		// TODO: User data for I think sprays?
		if (sv_allowdownload.GetBool() && type == RequestFile.Downloadable && sv.DownloadableFileTable is INetworkStringTable table && value < (uint)table.GetNumStrings()) {
			ReadOnlySpan<char> name = table.GetString((int)value);
			if (!name.IsStringEmpty) {
				NetChannel.SendFile(name, transferID);
				return;
			}
		}

		NetChannel.DenyFile(transferID);
	}

	// void FileDenied(ReadOnlySpan<char> fileName, uint transferID) { }

	// void FileSent(ReadOnlySpan<char> fileName, uint transferID) { }

	readonly Host Host = Singleton<Host>();

	public override void PacketStart(int incoming_sequence, int outgoing_acknowledged) {
		LastMovementTick = (int)(sv.TickCount - 1);
		Host.Client = this;
		ReceivedPacket = true;
	}

	public override void PacketEnd() => serverGlobalVariables.FrameTime = host_state.IntervalPerTick;

	public ClientFrame GetSendFrame() {
		ClientFrame? frame = CurrentFrame;
		return frame;
	}

	internal CheckTransmitInfo GetPrevPackInfo() {
		return PrevPackInfo;
	}

	public override bool IgnoreTempEntity(EventInfo evnt) {
		if (IsInReplayMode)
			return false;

		return base.IgnoreTempEntity(evnt);
	}

	internal void SendSound(SoundInfo sound, bool isReliable) {
		if (IsFakeClient() && !IsHLTV())
			return;

		if (IsInReplayMode)
			return;

		if (isReliable) {
			SVC_Sounds sndmsg = new();
			byte[] buffer = ArrayPool<byte>.Shared.Rent(32);

			SoundSequence = (SoundSequence + 1) & SOUND_SEQNUMBER_MASK;   // increase own sound sequence counter
			sound.SequenceNumber = 0; // don't transmit nSequenceNumber for reliable sounds

			sndmsg.DataOut.StartWriting(buffer, buffer.Length);
			sndmsg.NumSounds = 1;
			sndmsg.ReliableSound = true;

			SoundInfo defaultSound = default; defaultSound.SetDefault();

			sound.WriteDelta(ref defaultSound, sndmsg.DataOut);

			// send reliable sound as single message
			SendNetMsg(sndmsg, true);
			ArrayPool<byte>.Shared.Return(buffer);
			return;
		}

		sound.SequenceNumber = SoundSequence;

		Sounds.Add(sound);  // queue sounds until snapshot is send
	}
}
