using CommunityToolkit.HighPerformance;

using SDL;

using SharpCompress.Common;

using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.Keyvalues;
using Source.Common.Networking;
using Source.Common.Server;
using Source.Engine;
using Source.GUI.Controls;

using Steamworks;

using System.Runtime.CompilerServices;
using System.Text;

namespace Source.Engine.Server;


public abstract class BaseClient : IGameEventListener2, IClient, IClientMessageHandler, IDisposable
{
	protected readonly FrameSnapshotManager framesnapshotmanager = Singleton<FrameSnapshotManager>();
	public void SetReportThisFakeClient(bool report) {
		ReportFakeClient = report;
	}
	public int GetPlayerSlot() => ClientSlot;
	public int GetUserID() => UserID;
	// NetworkID?
	public ReadOnlySpan<char> GetClientName() => ((ReadOnlySpan<char>)Name).SliceNullTerminatedString();
	public INetChannel GetNetChannel() => NetChannel;
	public void Dispose() {

	}

	public void ConnectionStart(INetChannel channel) {
		channel.RegisterMessage<NET_Tick>();
		channel.RegisterMessage<NET_StringCmd>();
		channel.RegisterMessage<NET_SetConVar>();
		channel.RegisterMessage<NET_SignonState>();

		channel.RegisterMessage<CLC_VoiceData>();
		channel.RegisterMessage<CLC_Move>();
		channel.RegisterMessage<CLC_ListenEvents>();
		channel.RegisterMessage<CLC_ClientInfo>();
		channel.RegisterMessage<CLC_BaselineAck>();
		channel.RegisterMessage<CLC_RespondCvarValue>();
		channel.RegisterMessage<CLC_FileCRCCheck>();
		channel.RegisterMessage<CLC_FileMD5Check>();
		channel.RegisterMessage<CLC_CmdKeyValues>();
		channel.RegisterMessage<CLC_GMod_ClientToServer>();
	}

	public virtual bool IgnoreTempEntity(EventInfo evnt) {
		int playerIndex = GetPlayerSlot() + 1;

		return !evnt.Filter.IncludesPlayer(playerIndex);
	}
	public virtual void ConnectionClosing(ReadOnlySpan<char> reason) { }
	public virtual void ConnectionCrashed(ReadOnlySpan<char> reason) { }
	public virtual void PacketStart(int incomingSequence, int outgoingAcknowledged) { }
	public virtual void PacketEnd() { }
	public virtual void FileRequested(RequestFile type, uint value, uint transferID) { }
	public virtual void FileReceived(ReadOnlySpan<char> fileName, uint transferID) { }
	public virtual void FileDenied(uint transferID) { }
	public virtual void FileSent(ReadOnlySpan<char> fileName, uint transferID) { }

	public bool ProcessMessage<T>(T message) where T : INetMessage {
		// if (message is not NET_Tick && message is not CLC_Move)
		// 	Common.TimestampedLog($"BaseClient.ProcessMessage: {message.GetType().Name} (IsReliable: {message.IsReliable()})");

		switch (message) {
			case NET_Tick m: return ProcessTick(m);
			case NET_StringCmd m: return ProcessStringCmd(m);
			case NET_SetConVar m: return ProcessSetConVar(m);
			case NET_SignonState m: return ProcessSignonState(m);

			case CLC_VoiceData msg: return ProcessVoiceData(msg);
			case CLC_Move msg: return ProcessMove(msg);
			case CLC_ListenEvents msg: return ProcessListenEvents(msg);
			case CLC_ClientInfo msg: return ProcessClientInfo(msg);
			case CLC_BaselineAck msg: return ProcessBaselineAck(msg);
			case CLC_RespondCvarValue msg: return ProcessRespondCvarValue(msg);
			case CLC_FileCRCCheck msg: return ProcessFileCRCCheck(msg);
			case CLC_FileMD5Check msg: return ProcessFileMD5Check(msg);
			case CLC_CmdKeyValues msg: return ProcessCmdKeyValues(msg);
			case CLC_GMod_ClientToServer msg: return ProcessGMod_ClientToServer(msg);
		}
		return false;
	}

	protected virtual bool ProcessGMod_ClientToServer(CLC_GMod_ClientToServer m) {
		return true;// todo
	}

	protected virtual bool ProcessMove(CLC_Move m) => true;
	protected virtual bool ProcessVoiceData(CLC_VoiceData m) => true;
	protected virtual bool ProcessCmdKeyValues(CLC_CmdKeyValues msg) => true;
	protected virtual bool ProcessRespondCvarValue(CLC_RespondCvarValue msg) => true;
	protected virtual bool ProcessFileCRCCheck(CLC_FileCRCCheck msg) => true;
	protected virtual bool ProcessFileMD5Check(CLC_FileMD5Check msg) => true;

	protected virtual bool ProcessTick(NET_Tick m) {
		NetChannel!.SetRemoteFramerate(m.HostFrameTime, m.HostFrameDeviation);
		return UpdateAcknowledgedFramecount(m.Tick);
	}

	protected virtual bool ProcessStringCmd(NET_StringCmd m) {
		ExecuteStringCommand(m.Command);
		return true;
	}

	protected void OnRequestFullUpdate() {
		LastSnapshot = null;

		FreeBaselines();

		Baseline = framesnapshotmanager.CreateEmptySnapshot(0, Constants.MAX_EDICTS);

		DevMsg($"Sending full update to Client {GetClientName()}\n");
	}

	protected virtual bool UpdateAcknowledgedFramecount(int tick) {
		if (IsFakeClient()) {
			DeltaTick = tick;
			StringTableAckTick = tick;
			return true;
		}

		if (ForceWaitForTick > 0) {
			if (tick > ForceWaitForTick)
				// we should never get here since full updates are transmitted as reliable data now
				return true;
			else if (tick == -1) {
				if (!NetChannel.HasPendingReliableData()) {
					// that's strange: we sent the client a full update, and it was fully received ( no reliable data in waiting buffers )
					// but the client is requesting another full update.
					//
					// This can happen if they request full updates in succession really quickly (using cl_fullupdate or "record X;stop" quickly).
					// There was a bug here where if we just return out, the client will have nuked its entities and we'd send it
					// a supposedly uncompressed update but DeltaTick was not -1, so it was delta'd and it'd miss lots of stuff.
					// Led to clients getting full spectator mode radar while their player was not a spectator.
					ConDMsg("Client forced immediate full update.\n");
					ForceWaitForTick = DeltaTick = -1;
					OnRequestFullUpdate();
					return true;
				}
			}
			else if (tick < ForceWaitForTick)
				return true;
			else
				ForceWaitForTick = -1;
		}
		else {
			if (DeltaTick == -1)
				return true;

			if (tick == -1)
				OnRequestFullUpdate();
			else {
				if (DeltaTick > tick) {
					// client already acknowledged new tick and now switch back to older
					// thats not allowed since we always delete older frames
					Disconnect("Client delta ticks out of order.\n");
					return false;
				}
			}
		}

		DeltaTick = tick;

		if (DeltaTick > -1)
			StringTableAckTick = DeltaTick;

		if ((BaselineUpdateTick > -1) && (DeltaTick > BaselineUpdateTick))
			// server sent a baseline update, but it wasn't acknowledged yet so it was probably lost.
			BaselineUpdateTick = -1;

		return true;
	}

	public void ClientRequestNameChange(ReadOnlySpan<char> newName) {
		bool showStatusMessage = (PendingNameChange[0] == '\0');

		strcpy(PendingNameChange, newName);
		CheckFlushNameChange(showStatusMessage);
	}

	public void CheckFlushNameChange(bool showStatusMessage = false) {
		if (!IsConnected())
			return;

		if (PendingNameChange[0] == '\0')
			return;

		if (PlayerNameLocked)
			return;

		// Did they change it back to the original?
		if (0 == strcmp(PendingNameChange, Name)) {

			// Nothing really pending, they already changed it back
			// we had a chance to apply the other one!
			PendingNameChange[0] = '\0';
			return;
		}

		// Check for throttling name changes
		// Don't do it on bots
		if (!IsFakeClient() && IsNameChangeOnCooldown(showStatusMessage))
			return;

		// Set the new name
		TimeLastNameChange = Platform.Time;
		SetName(PendingNameChange);
	}

	public virtual void UpdateUserSettings() {
		int rate = ConVars!.GetInt("rate", Source.Common.Networking.NetChannel.DEFAULT_RATE);

		if (sv.IsActive()) {
			// If we're running a local listen server then set the rate very high
			// in order to avoid delays due to network throttling. This allows for
			// easier profiling of other issues (it removes most of the frame-render
			// time which can otherwise dominate profiles) and saves developer time
			// by making maps and models load much faster.
			if (rate == Source.Common.Networking.NetChannel.DEFAULT_RATE) {
				// Only override the rate if the user hasn't customized it.
				// The max rate should be a million or so in order to truly
				// eliminate networking delays.
				rate = Source.Common.Networking.NetChannel.MAX_RATE;
			}
		}

		// set server to client network rate
		SetRate(rate, false);

		// set server to client update rate
		SetUpdateRate(ConVars.GetInt("cl_updaterate", 20), false);

		SetMaxRoutablePayloadSize(ConVars.GetInt("net_maxroutable", Protocol.MAX_ROUTABLE_PAYLOAD));

		Server.UserInfoChanged(ClientSlot);

		ConVarsChanged = false;
	}

	public static readonly ConVar sv_namechange_cooldown_seconds = new("sv_namechange_cooldown_seconds", "30.0", 0, "When a client name change is received, wait N seconds before allowing another name change");
	public static readonly ConVar sv_netspike_on_reliable_snapshot_overflow = new("sv_netspike_on_reliable_snapshot_overflow", "0", 0, "If nonzero, the server will dump a netspike trace if a client is dropped due to reliable snapshot overflow");
	public static readonly ConVar sv_netspike_sendtime_ms = new("sv_netspike_sendtime_ms", "0", 0, "If nonzero, the server will dump a netspike trace if it takes more than N ms to prepare a snapshot to a single client.  This feature does take some CPU cycles, so it should be left off when not in use.");
	public static readonly ConVar sv_netspike_output = new("sv_netspike_output", "1", 0, "Where the netspike data be written?  Sum of the following values: 1=netspike.txt, 2=ordinary server log");

	public bool IsNameChangeOnCooldown(bool showStatusMessage = false) {
		if (TimeLastNameChange > 0.0) {
			// Too recent?
			double timeNow = Platform.Time;
			double dNextChangeTime = TimeLastNameChange + sv_namechange_cooldown_seconds.GetFloat();
			if (timeNow < dNextChangeTime) {
				// Cooldown period still active; throttle the name change
				if (showStatusMessage)
					ClientPrintf($"You have changed your name recently, and must wait {(int)Math.Abs(timeNow - dNextChangeTime)} seconds.\n");
				return true;
			}
		}

		return false;
	}

#if GMOD_DLL
	public static readonly ConVar sv_max_userinfo = new("sv_max_userinfo", "4096", 0, "The max amount of userinfo ConVars clients are allowed to send to the server. Setting this too high may result in loss of performance.");
	public static readonly ConVar sv_max_userinfo_rate = new("sv_max_userinfo_rate", "1500", 0, "The max amount of userinfo ConVars clients are allowed to send to the server in 1 second.", 0, 60000);

	ushort UserInfoCount;
	TimeUnit_t UserInfoResetTime;

	protected virtual bool ProcessSetConVar(NET_SetConVar msg) {
		int unique = 0;
		for (KeyValues? key = ConVars!.GetFirstSubKey(); key != null; key = key.GetNextKey())
			unique++;

		if (unique >= sv_max_userinfo.GetInt()) {
			DevMsg($"Player {GetClientName()} has sent too many unique userinfo ConVars, ignoring.\n");
			return true;
		}

		for (int i = 0; i < msg.ConVars.Count; i++) {
			ReadOnlySpan<char> name = msg.ConVars[i].Name;
			ReadOnlySpan<char> value = msg.ConVars[i].Value;

			bool funky = false;
			for (ReadOnlySpan<char> s = name; !s.IsStringEmpty; s = s[1..]) {
				if (!char.IsAsciiLetterOrDigit(s[0]) && s[0] != '_') {
					funky = true;
					break;
				}
			}
			if (funky) {
				DevMsg($"Ignoring convar change request for variable '{name}' from client {GetClientName()}; invalid characters in the variable name\n");
				continue;
			}

			if (stricmp(name, "name") == 0) {
				ClientRequestNameChange(value);
				continue;
			}

			int count = UserInfoCount;
			if (IsActive())
				UserInfoCount = (ushort)(count = UserInfoCount + 1);

			if (count > sv_max_userinfo_rate.GetInt() || UserInfoCount > 60000) {
				Disconnect("Client exceeded sv_max_userinfo_rate");
				return false;
			}

			if (Platform.Time > UserInfoResetTime) {
				UserInfoCount = 0;
				UserInfoResetTime = Platform.Time + 1.0;
			}

			ConVars!.SetString(name, value);
		}

		ConVarsChanged = true;

		return true;
	}
#else
	static double s_dblLastWarned = 0.0;
	protected virtual bool ProcessSetConVar(NET_SetConVar msg) {
		for (int i = 0; i < msg.ConVars.Count; i++) {
			ReadOnlySpan<char> name = msg.ConVars[i].Name;
			ReadOnlySpan<char> value = msg.ConVars[i].Value;

			// Discard any convar change request if contains funky characters
			bool funky = false;
			for (ReadOnlySpan<char> s = name; !s.IsStringEmpty; s = s[1..]) {
				if (!char.IsAsciiLetterOrDigit(s[0]) && s[0] != '_') {
					funky = true;
					break;
				}
			}
			if (funky) {
				Msg($"Ignoring convar change request for variable '{name}' from client {GetClientName()}; invalid characters in the variable name\n");
				continue;
			}

			// "name" convar is handled differently
			if (stricmp(name, "name") == 0) {
				ClientRequestNameChange(value);
				continue;
			}

			// The initial set of convars must contain all client convars that are flagged userinfo. This is a simple fix to
			// exploits that send bogus data later, and catches bugs (why are new userinfo convars appearing later?)
			if (InitialConVarsSet && ConVars!.FindKey(name) == null) {
#if !DEBUG    // warn all the time in debug build                                     
				double dblTimeNow = Platform.Time;
				if (dblTimeNow - s_dblLastWarned > 10)
#endif
				{
#if !DEBUG
					s_dblLastWarned = dblTimeNow;
#endif
					Warning($"Client \"{this.GetClientName()}\" userinfo ignored: \"{name}\" = \"{value}\"\n");
				}
				continue;
			}

			ConVars!.SetString(name, value);

			// DevMsg( 1, " UserInfo update %s: %s = %s\n", m_Client->m_Name, name, value );
		}

		ConVarsChanged = true;
		InitialConVarsSet = true;

		return true;
	}
#endif

	protected virtual bool ProcessSignonState(NET_SignonState msg) {
		Common.TimestampedLog($"BaseClient.ProcessSignonState: {msg.SignOnState} (Current: {SignOnState})");
		if (msg.SignOnState == SignOnState.ChangeLevel)
			return true;

		if (msg.SignOnState > SignOnState.Connected) {
			if (msg.SpawnCount != Server.GetSpawnCount()) {
				Reconnect();
				return true;
			}
		}

		// client must acknowledge our current state, otherwise start again
		if (msg.SignOnState != SignOnState) {
			Reconnect();
			return true;
		}

		return SetSignOnState(msg.SignOnState, msg.SpawnCount);
	}

	protected virtual bool SetSignOnState(SignOnState signOnState, int spawnCount) {
		switch (SignOnState) {
			case SignOnState.Connected:
				NeedSendServerInfo = true;
				break;

			case SignOnState.New:
				if (!SendSignonData())
					return false;

				break;

			case SignOnState.PreSpawn:
				SpawnPlayer();
				break;

			case SignOnState.Spawn:
				ActivatePlayer();
				break;

			case SignOnState.Full:
				OnSignonStateFull();
				break;

			case SignOnState.ChangeLevel: break;
		}

		return true;
	}

	protected virtual void SpawnPlayer() {
		Common.TimestampedLog("CBaseClient::SpawnPlayer");

		if (!IsFakeClient()) {
			FreeBaselines();
			Baseline = framesnapshotmanager.CreateEmptySnapshot(0, Constants.MAX_EDICTS);
		}

		NET_Tick msg = new(Server.GetTick(), (int)Host.FrameTimeUnbounded, (int)Host.FrameTimeStandardDeviation);
		SendNetMsg(msg);

		SignOnState = SignOnState.Spawn;

		NET_SignonState signonState = new(SignOnState, Server.GetSpawnCount());
		SendNetMsg(signonState);
	}

	public virtual void ActivatePlayer() {
		Common.TimestampedLog("CBaseClient::ActivatePlayer");

		Server.UserInfoChanged(ClientSlot);

		SignOnState = SignOnState.Full;

		// MapReslistGenerator().OnPlayerSpawn();
		// NotifyDedicatedServerUI("UpdatePlayers");
	}

	protected virtual void OnSignonStateFull() { }

	protected virtual bool SendSignonData() {
		Common.TimestampedLog("CBaseClient::SendSignonData");
#if !SWDS
		EngineVGui().UpdateProgressBar(LevelLoadingProgress.SendSignonData);
#endif

		if (Server.Signon.Overflowed) {
			Host.Error($"Signon buffer overflowed {Server.Signon.BytesWritten} bytes!!!\n");
			return false;
		}

		NetChannel.SendData(Server.Signon);

		SignOnState = SignOnState.PreSpawn;
		NET_SignonState signonState = new(SignOnState, Server.GetSpawnCount());

		return NetChannel.SendNetMsg(signonState);
	}

	protected virtual bool ProcessClientInfo(CLC_ClientInfo msg) {
		Common.TimestampedLog($"BaseClient.ProcessClientInfo: SignOnState={SignOnState}");
		if (SignOnState != SignOnState.New) {
			Warning($"Dropping ClientInfo packet from client not in appropriate state (State: {SignOnState})\n");
			return false;
		}

		SendTableCRC = (uint)msg.SendTableCRC;

		// Protect against spoofed packets claiming to be HLTV clients
		// if ((hltv && hltv.IsTVRelay()) || tv_enable.GetBool()) {
		// 	HLTV = msg.IsHLTV;
		// else
		HLTV = false;

		FilesDownloaded = 0;
		FriendsID = (uint)msg.FriendsID;
		// strcpy(FriendsName, msg.FriendsName);

		for (int i = 0; i < Constants.MAX_CUSTOM_FILES; i++) {
			CustomFiles[i].CRC = msg.CustomFiles[i];
			CustomFiles[i].ReqID = 0;
		}

		if (msg.ServerCount != Server.GetSpawnCount()) {
			Common.TimestampedLog($"BaseClient.ProcessClientInfo: ServerCount mismatch (msg={msg.ServerCount}, sv={Server.GetSpawnCount()}). Reconnecting.");
			Reconnect();  // client still in old game, reconnect
		}

		return true;
	}

	protected virtual bool ProcessBaselineAck(CLC_BaselineAck msg) {
		if (msg.BaselineTick != BaselineUpdateTick)
			return true;

		if (msg.BaselineNumber != BaselineUsed) {
			DevMsg($"CBaseClient::ProcessBaselineAck: wrong baseline nr received ({msg.BaselineTick})\n");
			return true;
		}

		Assert(Baseline != null);

		ClientFrame? frame = GetDeltaFrame(BaselineUpdateTick);
		if (frame == null)
			return true;

		FrameSnapshot? snapshot = frame.GetSnapshot();
		if (snapshot == null) {
			DevMsg($"CBaseClient::ProcessBaselineAck: invalid frame snapshot ({BaselineUpdateTick})\n");
			return true;
		}

		int index = BaselinesSent.FindNextSetBit(0);
		while (index > 0) {
			PackedEntityHandle_t newEntity = snapshot.Entities![index].PackedData;
			if (newEntity == FrameSnapshotManager.INVALID_PACKED_ENTITY_HANDLE) {
				DevMsg($"CBaseClient::ProcessBaselineAck: invalid packet handle ({index})\n");
				return false;
			}

			PackedEntityHandle_t oldEntity = Baseline.Entities![index].PackedData;

			if (oldEntity != FrameSnapshotManager.INVALID_PACKED_ENTITY_HANDLE)
				framesnapshotmanager.RemoveEntityReference(oldEntity);

			framesnapshotmanager.AddEntityReference(newEntity);

			Baseline.Entities[index] = snapshot.Entities[index];

			index = BaselinesSent.FindNextSetBit(index + 1);
		}

		Baseline.TickCount = BaselineUpdateTick;

		BaselineUsed = (BaselineUsed == 1) ? 0 : 1;
		BaselineUpdateTick = -1;

		return true;
	}

	protected virtual bool ProcessListenEvents(CLC_ListenEvents m) {
		gameEventManager.RemoveListener(this);

		for (int i = 0; i < Constants.MAX_EVENT_NUMBER; i++) {
			if (m.EventArray.Get(i) != 0) {
				GameEventDescriptor? desc = gameEventManager.GetEventDescriptor(i);
				if (desc != null)
					gameEventManager.AddListener(this, desc, GameEventListenerType.Clientstub);
				else {
					DevMsg($"ProcessListenEvents: game event {i} not found.\n");
					return false;
				}
			}
		}

		return true;
	}

	protected virtual void WriteGameSounds(bf_write buf) { }

	protected virtual ClientFrame? GetDeltaFrame(int tick) {
		Assert(false);
		return null;
	}

	const int SNAPSHOT_SCRATCH_BUFFER_SIZE = 160000;
	readonly byte[] SnapshotScratchBuffer = new byte[SNAPSHOT_SCRATCH_BUFFER_SIZE / 4];

	public virtual void SendSnapshot(ClientFrame frame) {
		if (ForceWaitForTick > 0 || LastSnapshot == frame.GetSnapshot()) {
			NetChannel.Transmit();
			return;
		}

		bool failedOnce = false;

	write_again:
		bf_write msg = new(SnapshotScratchBuffer, SNAPSHOT_SCRATCH_BUFFER_SIZE);

		ClientFrame? deltaFrame = GetDeltaFrame(DeltaTick);
		if (deltaFrame == null)
			OnRequestFullUpdate();

		NET_Tick tickmsg = new(frame.TickCount, (int)Host.FrameTimeUnbounded, (int)Host.FrameTimeStandardDeviation);

		StartTrace(msg);

		tickmsg.WriteToBuffer(msg);

		if (Tracing != 0)
			TraceNetworkData(msg, "NET_Tick");

		if (CL.LocalNetworkBackdoor == null)
			Server.StringTables!.WriteUpdateMessage(this, GetMaxAckTickCount(), msg);

		int deltaStartBit = 0;
		if (Tracing != 0)
			deltaStartBit = msg.BitsWritten;

		Server.WriteDeltaEntities(this, frame, deltaFrame, msg);

		if (Tracing != 0) {
			int bits = msg.BitsWritten - deltaStartBit;
			TraceNetworkData(msg, "Total Delta");
		}

		int maxTempEnts = Server.IsMultiplayer() ? 64 : 255;
		Server.WriteTempEntities(this, frame.GetSnapshot(), LastSnapshot, msg, maxTempEnts);

		WriteGameSounds(msg);

		if (msg.Overflowed) {
			bool wasTracing = Tracing != 0;
			if (wasTracing) {
				TraceNetworkMsg(0, $"Finished [delta {(deltaFrame != null ? "yes" : "no")}]");
				EndTrace(msg);
			}

			if (deltaFrame == null) {
				if (!wasTracing) {

					if (sv_netspike_on_reliable_snapshot_overflow.GetBool()) {
						if (!failedOnce) {
							Warning(" RELIABLE SNAPSHOT OVERFLOW!  Triggering trace to see what is so large\n");
							failedOnce = true;
							Tracing = 2;
							goto write_again;
						}

						Tracing = 0;
					}

					Disconnect("ERROR! Reliable snapshot overflow.\n");
					return;
				}
				else {
					ConMsg($"WARNING: msg overflowed for {Name}\n");
					msg.Reset();
				}
			}
		}

		LastSnapshot = frame.GetSnapshot();

		if (FakePlayer && NetChannel == null) {
			DeltaTick = (int)frame.TickCount;
			StringTableAckTick = DeltaTick;
			return;
		}

		bool sendOK;

		if (deltaFrame == null) {
			sendOK = NetChannel.SendData(msg);
			sendOK = sendOK && NetChannel.Transmit();

			ForceWaitForTick = (int)frame.TickCount;
		}
		else
			sendOK = NetChannel.SendDatagram(msg) > 0;

		if (sendOK) {
			if (Tracing != 0) {
				TraceNetworkMsg(0, $"Finished [delta {(deltaFrame != null ? "yes" : "no")}]");
				EndTrace(msg);
			}
		}
		else
			Disconnect($"ERROR! Couldn't send snapshot.\n");
	}

	public int GetClientChallenge() => ClientChallenge;

	static readonly char[] idstr = new char[Constants.MAX_NETWORKID_LENGTH];
	public ReadOnlySpan<char> GetUserIDString(USERID id) {
		idstr[0] = '\0';

		switch (id.IDType) {
			case IDType.Steam:
				if (Steam3Server().BLanOnly() && id.SteamID == CSteamID.Nil)
					strcpy(idstr, "STEAM_ID_LAN");
				else if (id.SteamID == CSteamID.Nil)
					strcpy(idstr, "STEAM_ID_PENDING");
				else
					strcpy(idstr, id.SteamID.Render());
				break;
			case IDType.HLTV: strcpy(idstr, "HLTV"); break;
			case IDType.Replay: strcpy(idstr, "REPLAY"); break;
			default: strcpy(idstr, "UNKNOWN"); break;
		}

		return idstr;
	}

	public USERID GetNetworkID() {
		USERID userID;

		userID.SteamID = SteamID;
		userID.IDType = IDType.Steam;

		return userID;
	}

	public ReadOnlySpan<char> GetNetworkIDString() {
		if (IsFakeClient())
			return "BOT";

		return GetUserIDString(GetNetworkID());
	}

	public virtual void Clear() {
		if (NetChannel != null) {
			NetChannel.Shutdown("Disconnect by server.\n");
			NetChannel = null!;
		}

		if (ConVars != null)
			ConVars = null;

		FreeBaselines();

		// This used to be a memset, but memset will screw up any embedded classes
		// and we want to preserve some things like index.
		SignOnState = SignOnState.None;
		DeltaTick = -1;
		SignOnTick = 0;
		StringTableAckTick = 0;
		LastSnapshot = null;
		ForceWaitForTick = -1;
		FakePlayer = false;
		HLTV = false;
		NextMessageTime = 0;
		SnapshotInterval = 0;
		ReceivedPacket = false;
		UserID = 0;
		Name[0] = '\0';
		FriendsID = 0;
		FriendsName = "";
		SendTableCRC = 0;
		BaselineUpdateTick = -1;
		BaselineUsed = 0;
		FilesDownloaded = 0;
		ConVarsChanged = false;
		NeedSendServerInfo = false;
		FullyAuthenticated = false;
		TimeLastNameChange = 0.0;
		PendingNameChange[0] = '\0';
		memreset(CustomFiles);
	}

	private void FreeBaselines() {
		Baseline?.ReleaseReference();
		Baseline = null;

		BaselineUpdateTick = -1;
		BaselineUsed = 0;
		BaselinesSent.ClearAll();
	}

	public bool IsConnected() => SignOnState >= SignOnState.Connected;

	public bool FillUserInfo(out PlayerInfo userInfo) {
		userInfo = default;

		if (Name[0] == '\0' || !IsConnected())
			return false;

		strcpy(userInfo.Name, GetClientName());
		Encoding.ASCII.GetBytes(GetNetworkIDString()[..Math.Min(GetNetworkIDString().Length, 32)], userInfo.GUID);
		userInfo.FriendsID = FriendsID;
		strcpy(userInfo.FriendsName, FriendsName ?? "");
		userInfo.UserID = GetUserID();
		userInfo.FakePlayer = IsFakeClient();
		userInfo.IsHLTV = IsHLTV();
		userInfo.IsReplay = IsReplay();
		for (int i = 0; i < Constants.MAX_CUSTOM_FILES; i++)
			userInfo.CustomFiles[i] = (CRC32_t)CustomFiles[i].CRC;
		userInfo.FilesDownloaded = (byte)FilesDownloaded;
		return true;
	}
	public virtual void Disconnect(ReadOnlySpan<char> str) {
		if (SignOnState == SignOnState.None)
			return;

		Steam3Server().NotifyClientDisconnect(this);
		SignOnState = SignOnState.None;

		Server.UserInfoChanged(ClientSlot);
		ConMsg($"Dropped {GetClientName()} from server ({str})\n");

		g_GameEventManager.RemoveListener(this);

		if (NetChannel != null) {
			NetChannel.Shutdown(str);
			NetChannel = null!;
		}

		Clear();
		DedicatedServerUI.NotifyDedicatedServerUI("UpdatePlayers");
		Steam3Server().SendUpdatedServerDetails();
	}

	public bool IsActive() => SignOnState == SignOnState.Full;
	public bool IsSpawned() => SignOnState >= SignOnState.New;
	public bool IsFakeClient() => FakePlayer;
	public bool IsHLTV() => HLTV;

	public bool SendNetMsg(INetMessage msg, bool forceReliable = false) {
		if (NetChannel == null)
			return true;

		int nStartBit = NetChannel.GetNumBitsWritten(msg.IsReliable() || forceReliable);
		bool bret = NetChannel.SendNetMsg(msg, forceReliable);
		if (Tracing != 0) {
			int bits = NetChannel.GetNumBitsWritten(msg.IsReliable() || forceReliable) - nStartBit;
			TraceNetworkMsg(bits, $"NetMessage {msg.GetName()}");
		}
		return bret;
	}

	public int ClientSlot;
	public int EntityIndex;
	public int UserID;

	public InlineArrayMaxPlayerNameLength<char> Name;
	public InlineArray33<byte> GUID;

	public CSteamID SteamID;
	public uint FriendsID;
	public string FriendsName;

	protected KeyValues? ConVars;
	bool InitialConVarsSet;
	public bool ConVarsChanged;
	public bool NeedSendServerInfo;
	public BaseServer Server;
	public bool HLTV;
	public bool Replay;
	public int ClientChallenge;

	public uint SendTableCRC;

	public readonly CustomFile[] CustomFiles = new CustomFile[Constants.MAX_CUSTOM_FILES];
	public int FilesDownloaded;

	public INetChannel NetChannel;
	public SignOnState SignOnState;
	public int DeltaTick;
	public int StringTableAckTick;
	public long SignOnTick;
	// CSmartPtr<CFrameSnapshot, CRefCountAccessorLongName>
	FrameSnapshot? LastSnapshot; // todo? ^
	public FrameSnapshot? Baseline;
	public int BaselineUpdateTick;
	public MaxEdictsBitSet BaselinesSent;
	public int BaselineUsed;

	public int ForceWaitForTick;
	public bool FakePlayer;     // JAC: This client is a fake player controlled by the game DLL
	public bool ReportFakeClient; // Should this fake client be reported 
	public bool ReceivedPacket; // true, if client received a packet after the last send packet

	public bool FullyAuthenticated;
	public TimeUnit_t TimeLastNameChange;
	public bool PlayerNameLocked;
	public InlineArrayMaxPlayerNameLength<char> PendingNameChange;
	public TimeUnit_t NextMessageTime;
	public TimeUnit_t SnapshotInterval;

	public bool IsFullyAuthenticated() => FullyAuthenticated;
	public void SetFullyAuthenticated() => FullyAuthenticated = true;
	public void SetPlayerNameLocked(bool bValue) { PlayerNameLocked = bValue; }
	public bool IsPlayerNameLocked() => PlayerNameLocked;

	public void FireGameEvent(IGameEvent ev) {
		byte[] buffer = new byte[Constants.MAX_EVENT_BITS];

		SVC_GameEvent msg = new();
		msg.DataOut.StartWriting(buffer, Constants.MAX_EVENT_BITS, 0);

		if (gameEventManager.SerializeEvent(ev, msg.DataOut)) {
			if (NetChannel != null) {
				bool sent = NetChannel.SendNetMsg(msg);
				if (!sent)
					DevMsg($"GameEventManager: failed to send event '{ev.GetName()}'.\n");
			}
		}
		else
			DevMsg($"GameEventManager: failed to serialize event '{ev.GetName()}'.\n");
	}
	static bool BIgnoreCharInName(char cChar, bool bIsFirstCharacter) {
		return cChar == '%' || cChar == '~' || cChar < 0x09 || (bIsFirstCharacter && cChar == '#');
	}
	public void SetName(ReadOnlySpan<char> playerName) {
		Span<char> name = stackalloc char[Constants.MAX_PLAYER_NAME_LENGTH];
		strcpy(name, playerName);

		PendingNameChange[0] = '\0';

		ValidateName(name);
		if (strcmp(name, Name) == 0)
			return;

		int i;
		int dupc = 1;
		ReadOnlySpan<char> p, val;

		Span<char> newname = stackalloc char[Constants.MAX_PLAYER_NAME_LENGTH];

		ReadOnlySpan<char> pFrom = name;
		Span<char> pTo = Name;

		while (!pFrom.IsEmpty && pTo.Length > 0) {
			if (!BIgnoreCharInName(pFrom[0], Unsafe.AreSame(ref pTo.DangerousGetReference(), ref ((ReadOnlySpan<char>)Name).DangerousGetReference()))) {
				pTo[0] = pFrom[0];
				pTo = pTo[1..];
			}

			pFrom = pFrom[1..];
		}
		pTo[0] = '\0';

		Assert(Name[0] != '\0');
		if (Name[0] == '\0')
			strcpy(Name, "unnamed");

		val = Name;

		while (true) {
			for (i = 0; i < Server.GetClientCount(); i++) {
				IClient client = Server.GetClient(i)!;

				if (!client.IsConnected() || client == this)
					continue;

				if (stricmp(client.GetClientName(), val) == 0 && !(IsFakeClient() && client.IsFakeClient())) {
					BaseClient? pClient = (BaseClient?)client;
					if (IsFakeClient() && pClient != null) {
						pClient.Name[0] = '\0';
						pClient.SetName(val);
					}
					else {
						break;
					}
				}
			}

			if (i >= Server.GetClientCount())
				break;

			p = val;

			if (val[0] == '(') {
				if (val[2] == ')')
					p = val[3..];
				else if (val[3] == ')')
					p = val[4..];
			}

			sprintf(newname, "(%d)%s").D(dupc++).S(p);
			strcpy(Name, newname);

			val = Name;
		}

		ConVars!.SetString("name", Name);
		ConVarsChanged = true;

		Server.UserInfoChanged(ClientSlot);
	}

	static void ValidateName(Span<char> name) {
		if (name.IsEmpty)
			sprintf(name, "unnamed");
		else {
			StrTools.RemoveAllEvilCharacters(name);

			ReadOnlySpan<char> pChar = name;
			while (!pChar.IsEmpty && pChar[0] != '\0' && (pChar[0] == ' ' || BIgnoreCharInName(pChar[0], true)))
				pChar = pChar[1..];

			if (pChar.IsEmpty || pChar[0] == '\0')
				sprintf(name, "unnamed");
		}
	}

	public virtual void Connect(ReadOnlySpan<char> name, int userID, INetChannel netChannel, bool fakePlayer, int clientChallenge) {
		Common.TimestampedLog("CBaseClient::Connect");
#if !SWDS
		EngineVGui().UpdateProgressBar(LevelLoadingProgress.SignOnConnect);
#endif
		Clear();

		ConVars = new KeyValues("userinfo");
		InitialConVarsSet = false;

		UserID = userID;

		SetName(name);
		TimeLastNameChange = 0.0;

		FakePlayer = fakePlayer;
		NetChannel = netChannel;

		if (NetChannel != null && Server != null && Server.IsMultiplayer())
			NetChannel.SetCompressionMode(true);

		ClientChallenge = clientChallenge;

		SignOnState = SignOnState.Connected;

		if (fakePlayer)
			Steam3Server().NotifyLocalClientConnect(this);
	}

	public virtual void Inactivate() {
		FreeBaselines();

		DeltaTick = -1;
		SignOnTick = 0;
		StringTableAckTick = 0;
		LastSnapshot = null;
		ForceWaitForTick = -1;

		SignOnState = SignOnState.ChangeLevel;

		if (NetChannel != null) {
			NetChannel.Clear();

			// if (Net.IsMultiplayer()) {
			// 	NET_SignonState signonState = new(SignOnState, Server.GetSpawnCount());
			// 	NetChannel.SendNetMsg(signonState);
			// 	NetChannel.Transmit();
			// }
		}

		gameEventManager.RemoveListener(this);
	}

	readonly Host Host = Singleton<Host>();

	public virtual void Reconnect() {
		ConMsg("Forcing client reconnect (%i)\n", SignOnState);

		NetChannel.Clear();

		SignOnState = SignOnState.Connected;

		NET_SignonState msg = new(SignOnState, -1);
		NetChannel.SendNetMsg(msg);
	}

	public IServer? GetServer() {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetUserSetting(ReadOnlySpan<char> cvar) {
		if (ConVars == null || cvar.IsEmpty)
			return "";

		ReadOnlySpan<char> value = ConVars.GetString(cvar, "");

		if (value.IsEmpty)
			if (ConVars.FindKey(cvar) == null)
				DevMsg($"GetUserSetting: cvar '{cvar}' unknown.\n");

		return value;
	}

	public void SetUserCVar(ReadOnlySpan<char> cvar, ReadOnlySpan<char> value) {
		if (cvar.IsEmpty || value.IsEmpty)
			return;

		if (stricmp(cvar, "name") == 0) {
			ClientRequestNameChange(value);
			return;
		}

		ConVars!.SetString(cvar, value);
	}

	public void SetRate(int nRate, bool force) => NetChannel?.SetDataRate(nRate);

	public int GetRate() => NetChannel?.GetDataRate() ?? 0;

	public void SetUpdateRate(int updateRate, bool force) {
		updateRate = Math.Clamp(updateRate, 1, 100);
		SnapshotInterval = 1.0f / updateRate;
	}

	public int GetUpdateRate() {
		if (SnapshotInterval > 0)
			return (int)(1.0f / SnapshotInterval);
		else
			return 0;
	}

	public int GetMaxAckTickCount() {
		long maxTick = SignOnTick;

		if (DeltaTick > maxTick)
			maxTick = DeltaTick;

		if (StringTableAckTick > maxTick)
			maxTick = StringTableAckTick;

		return (int)maxTick;
	}

	public virtual bool ExecuteStringCommand(ReadOnlySpan<char> cmd) {
		if (cmd.IsEmpty)
			return false;

		if (strcmp(cmd, "demorestart") == 0) {
			// DemoRestart();
			return false;
		}

		return false;
	}

	public virtual bool ShouldSendMessages() {
		if (!IsConnected())
			return false;

		if (NetChannel != null && NetChannel.IsOverflowed()) {
			NetChannel.Reset();
			Disconnect($"{Name} overflowed reliable buffer\n");
			return false;
		}

		bool sendMessage = NextMessageTime <= Net.Time;

		if (!sendMessage && !IsActive()) {
			if (ReceivedPacket && NetChannel != null && NetChannel.HasPendingReliableData())
				sendMessage = true;
		}

		if (sendMessage && NetChannel != null && !NetChannel.CanPacket()) {
			NetChannel.SetChoked();
			sendMessage = false;
		}

		return sendMessage;
	}

	public void UpdateSendState() {
		ReceivedPacket = false;

		if (!Server.IsMultiplayer() /*&& !host_limitlocal.GetBool()*/) {
			NextMessageTime = Net.Time;
			ReceivedPacket = true;
		}
		else if (IsActive()) {
			float maxDelta = (float)Math.Min(Server.GetTickInterval(), SnapshotInterval);
			float delta = Math.Clamp((float)(Net.Time - NextMessageTime), 0, maxDelta);
			NextMessageTime = Net.Time + SnapshotInterval - delta;
		}
		else {
			if (NetChannel != null && NetChannel.HasPendingReliableData() && NetChannel.GetTimeSinceLastReceived() < 1.0f)
				NextMessageTime = Net.Time;
			else
				NextMessageTime = Net.Time + 1.0f;
		}
	}

	public void ClientPrintf(ReadOnlySpan<char> fmt) {
		if (NetChannel == null)
			return;

		SVC_Print print = new SVC_Print(fmt);
		NetChannel.SendNetMsg(print);
	}

	public virtual bool IsHearingClient(int index) => false;

	public virtual bool IsProximityHearingClient(int index) => false;

	public void SetMaxRoutablePayloadSize(int nMaxRoutablePayloadSize) => NetChannel?.SetMaxRoutablePayloadSize(nMaxRoutablePayloadSize);

	public bool IsReplay() {
		return false;
	}

	public bool ShouldReportThisFakeClient() {
		return false;
	}

	internal void SetSteamID(in CSteamID steamID) {
		SteamID = steamID;
	}

	static readonly ThreadLocal<byte[]> NetPayloadBuffer = new(() => new byte[Protocol.MAX_PAYLOAD]);

	public bool SendServerInfo() {
		Common.TimestampedLog(" BaseClient.SendServerInfo");

		// supporting smaller stack
		byte[] buffer = NetPayloadBuffer.Value!;
		bf_write msg = new(buffer, Protocol.MAX_PAYLOAD);

		// Only send this message to developer console, or multiplayer clients.
		if (Host.developer.GetBool() || Server.IsMultiplayer()) {
			Span<char> devtext = stackalloc char[2048];
			int curplayers = Server.GetNumClients();

			sprintf(devtext, "\n%s\nMap: %s\nPlayers: %i / %i\nBuild: %d\nServer Number: %i\n\n")
				.S(serverGameDLL.GetGameDescription())
				.S(Server.GetMapName())
				.I(curplayers)
				.I(Server.GetMaxClients())
				.I(build_number())
				.I(Server.GetSpawnCount());

			SVC_Print printMsg = new();
			printMsg.Text = new(devtext.SliceNullTerminatedString());

			printMsg.WriteToBuffer(msg);
		}

		SVC_ServerInfo serverinfo = new();  // create serverinfo message

		serverinfo.PlayerSlot = ClientSlot; // own slot number

		Server.FillServerInfo(serverinfo); // fill rest of info message

		serverinfo.WriteToBuffer(msg);

		// send first tick
		SignOnTick = Server.TickCount;

		NET_Tick signonTick = new(SignOnTick, 0, 0);
		signonTick.WriteToBuffer(msg);

		// write stringtable baselines
		Server.StringTables.WriteBaselines(msg);

		// Write replicated ConVars to non-listen server clients only
		if (!NetChannel.IsLoopback()) {
#if GMOD_DLL
			Host.GMOD_BuildConVarUpdateMessageBF(msg, FCvar.Replicated, true);
#else
			NET_SetConVar convars = new();
			Host.BuildConVarUpdateMessage(convars, FCvar.Replicated, true);

			convars.WriteToBuffer(msg);
#endif
		}

		NeedSendServerInfo = false;

		// send signon state
		SignOnState = SignOnState.New;
		NET_SignonState signonMsg = new(SignOnState, Server.GetSpawnCount());
		signonMsg.WriteToBuffer(msg);

		// send server info as one data block
		if (!NetChannel.SendData(msg)) {
			Disconnect("Server info data overflow");
			return false;
		}
		serverGameClients.GMOD_SentClientStringTables(ClientSlot);

		Common.TimestampedLog(" BaseClient.SendServerInfo(finished)");

		return true;
	}

	struct Spike()
	{
		public InlineArray64<char> Desc;
		public int Bits = 0;
	}

	struct NetworkStatTrace()
	{
		public int MinWarningBytes = 0;
		public int StartBit = 0;
		public int CurBit = 0;
		public readonly List<Spike> Records = [];
		public double StartSendTime = 0.0;
	}

	public int Tracing;
	NetworkStatTrace Trace = new();

	void StartTrace(bf_write msg) {
		Trace.MinWarningBytes = 0;
		if (!IsHLTV() && !IsReplay() && !IsFakeClient())
			Trace.MinWarningBytes = -1;

		if (Tracing < 2) {
			if (Trace.MinWarningBytes <= 0 && sv_netspike_sendtime_ms.GetFloat() <= 0.0f) {
				Tracing = 0;
				return;
			}
			Tracing = 1;
		}
		Trace.StartBit = msg.BitsWritten;
		Trace.CurBit = Trace.StartBit;
		Trace.StartSendTime = Platform.Time;
	}

	void EndTrace(bf_write msg) {
		if (Tracing == 0)
			return;

		int bits = Trace.CurBit - Trace.StartBit;
		float elapsedMs = (float)((Platform.Time - Trace.StartSendTime) * 1000.0);
		int threshold = Trace.MinWarningBytes << 3;
		if (Tracing < 2                                                                                       // not forced
				&& (threshold <= 0 || bits < threshold)                                                      // didn't exceed data threshold
				&& (sv_netspike_sendtime_ms.GetFloat() <= 0.0f || elapsedMs < sv_netspike_sendtime_ms.GetFloat())) // didn't exceed time threshold
		{
			Trace.Records.Clear();
			Tracing = 0;
			return;
		}

		StringBuilder logData = new();
		logData.Append($"{Platform.Time}/{Host.TickCount} Player [{GetClientName()}][{GetPlayerSlot()}][adr:{NetChannel.GetAddress()}] was sent a datagram {bits} bits ({(float)bits / 8.0f} bytes), took {elapsedMs:F2}ms\n");

		if ((sv_netspike_output.GetInt() & 2) == 0)
			Log("netspike: %s", logData.ToString());

		for (int i = 0; i < Trace.Records.Count; ++i) {
			Spike sp = Trace.Records[i];
			logData.Append($"{sp.Desc} : {sp.Bits} bits ({(float)sp.Bits / 8.0f} bytes)\n");
		}

		if ((sv_netspike_output.GetInt() & 1) != 0)
			// COM_LogString(SERVER_PACKETS_LOG, logData.String());

			if ((sv_netspike_output.GetInt() & 2) != 0)
				Log("%s", logData.ToString());

		Trace.Records.Clear();
		Tracing = 0;
	}

	public void TraceNetworkData(bf_write msg, ReadOnlySpan<char> fmt) {
		if (Tracing == 0)
			return;

		Spike sp = new();
		fmt.CopyTo(sp.Desc);
		sp.Bits = msg.BitsWritten - Trace.CurBit;
		Trace.Records.Add(sp);
		Trace.CurBit = msg.BitsWritten;
	}

	public void TraceNetworkMsg(int bits, ReadOnlySpan<char> fmt) {
		if (Tracing == 0)
			return;

		Spike sp = new();
		fmt.CopyTo(sp.Desc);
		sp.Bits = bits;
		Trace.Records.Add(sp);
	}
}
