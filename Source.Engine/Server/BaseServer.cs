using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Hashing;
using Source.Common.Networking;
using Source.Common.Server;
using Source.Common.Steam;
using Source.Common.Utilities;

using Steamworks;

namespace Source.Engine.Server;

public enum ServerState
{
	Dead,
	Loading,
	Active,
	Paused
}

/// <summary>
/// Base server, in SERVER
/// </summary>
public abstract class BaseServer : IServer
{

	protected readonly Net Net = Singleton<Net>();
	protected readonly Host Host = Singleton<Host>();
	protected readonly Filter Filter = Singleton<Filter>();
	protected readonly FrameSnapshotManager framesnapshotmanager = Singleton<FrameSnapshotManager>();
	internal static readonly ConVar sv_region = new("sv_region", "-1", FCvar.None, "The region of the world to report this server in.");
	internal static readonly ConVar sv_instancebaselines = new("sv_instancebaselines", "1", FCvar.DevelopmentOnly, "Enable instanced baselines. Saves network overhead.");
	internal static readonly ConVar sv_stats = new("sv_stats", "1", 0, "Collect CPU usage stats");
	internal static readonly ConVar sv_enableoldqueries = new("sv_enableoldqueries", "0", 0, "Enable support for old style (HL1) server queries");
	internal static readonly ConVar sv_password = new("sv_password", "", FCvar.Notify | FCvar.Protected | FCvar.DontRecord, "Server password for entry into multiplayer games");
	internal static readonly ConVar sv_visiblemaxplayers = new("sv_visiblemaxplayers", "-1", 0, "Overrides the max players reported to prospective clients");
	internal static readonly ConVar sv_alternateticks = new("sv_alternateticks", "0", FCvar.SingleplayerOnly, "If set, server only simulates entities on even numbered ticks.\n");
	internal static readonly ConVar sv_allow_wait_command = new("sv_allow_wait_command", "1", FCvar.Replicated, "Allow or disallow the wait command on clients connected to this server.");
	internal static readonly ConVar sv_allow_color_correction = new("sv_allow_color_correction", "1", FCvar.Replicated, "Allow or disallow clients to use color correction on this server.");
	internal static readonly ConVar sv_tags = new("sv_tags", "", FCvar.Notify, "Server tags. Used to provide extra information to clients when they're browsing for servers. Separate tags with a comma.", callback: SvTagsChangeCallback);
	internal static readonly ConVar sv_debugtempentities = new("sv_debugtempentities", "0", FCvar.None, "Show temp entity bandwidth usage.");
	internal static readonly ConVar sv_downloadurl = new("sv_downloadurl", "", FCvar.Replicated | FCvar.Notify, "Location from which clients can fast-download missing content.");
	static bool bTagsChangeCallback = false;
	private static void SvTagsChangeCallback(IConVar var, in ConVarChangeContext ctx) {
		if (bTagsChangeCallback)
			return;

		bTagsChangeCallback = true;
		bTagsChangeCallback = false;
	}

	public virtual int GetNumClients() {
		int count = 0;

		for (int i = 0; i < Clients.Count; i++)
			if (Clients[i].IsConnected())
				count++;

		return count;
	}
	public virtual int GetNumProxies() {
		int count = 0;

		for (int i = 0; i < Clients.Count; i++)
			if (Clients[i].IsConnected() && Clients[i].IsHLTV())
				count++;

		return count;
	}
	public virtual int GetNumFakeClients() {
		int count = 0;

		for (int i = 0; i < Clients.Count; i++)
			if (Clients[i].IsFakeClient())
				count++;

		return count;
	}
	public virtual int GetMaxClients() => MaxClients;  // returns current client limit
	public virtual int GetUDPPort() => Net.GetUDPPort(Socket);
	public virtual IClient? GetClient(int index) => Clients[index];  // returns interface to client 
	public virtual int GetClientCount() => Clients.Count; // for iteration;
	public virtual TimeUnit_t GetTime() {
		return TickCount * TickInterval;
	}
	public virtual long GetTick() => TickCount;
	public virtual TimeUnit_t GetTickInterval() => TickInterval;
	public virtual ReadOnlySpan<char> GetName() {
		return Host.host_name.GetString();
	}
	public virtual ReadOnlySpan<char> GetMapName() => ((ReadOnlySpan<char>)MapName).SliceNullTerminatedString();
	public virtual int GetSpawnCount() => SpawnCount;
	public virtual int GetNumClasses() => ServerClasses;
	public virtual int GetClassBits() => ServerClassBits;
	public virtual void GetNetStats(out double avgIn, out double avgOut) {
		avgIn = avgOut = 0.0f;

		for (int i = 0; i < Clients.Count; i++) {
			BaseClient cl = Clients[i];

			// Fake clients get killed in here.
			if (cl.IsFakeClient())
				continue;

			if (!cl.IsConnected())
				continue;

			NetChannel netchan = (NetChannel)cl.GetNetChannel()!;

			avgIn += netchan.GetAvgData(NetFlow.FLOW_INCOMING);
			avgOut += netchan.GetAvgData(NetFlow.FLOW_OUTGOING);
		}
	}
	public virtual int GetNumPlayers() {
		int count = 0;
		if (GetUserInfoTable() == null) {
			return 0;
		}

		int maxPlayers = GetUserInfoTable()!.GetNumStrings();

		for (int i = 0; i < maxPlayers; i++) {
			if (!PlayerInfo.FromBytes(UserInfoTable!.GetStringUserData(i), out PlayerInfo pi))
				continue;

			if (pi.FakePlayer)
				continue;

			count++;
		}

		return count;
	}
	public virtual bool GetPlayerInfo(int clientIndex, out PlayerInfo pinfo) {
		if (clientIndex < 0 || GetUserInfoTable() == null || clientIndex >= GetUserInfoTable()!.GetNumStrings()) {
			pinfo = default;
			return false;
		}

		return PlayerInfo.FromBytes(UserInfoTable!.GetStringUserData(clientIndex), out pinfo);
	}
	public virtual float GetCPUUsage() => CPUPercent;

	public virtual bool IsActive() => State >= ServerState.Active;
	public virtual bool IsLoading() => State == ServerState.Loading;
	public virtual bool IsDedicated() => Dedicated;
	public virtual bool IsPaused() => State == ServerState.Paused;
	public virtual bool IsMultiplayer() => MaxClients > 1;
	public virtual bool IsPausable() => false;

	public virtual bool IsHLTV() => false;
	public virtual bool IsReplay() => false;

	public virtual void BroadcastMessage(INetMessage msg, bool onlyActive = false, bool reliable = false) {
		for (int i = 0; i < Clients.Count; i++) {
			BaseClient cl = Clients[i];

			if ((onlyActive && !cl.IsActive()) || !cl.IsSpawned())
				continue;

			if (!cl.SendNetMsg(msg, reliable)) {
				if (msg.IsReliable() || reliable) {
					DevMsg($"BroadcastMessage: Reliable broadcast message overflow for client {cl.GetClientName()}");
				}
			}
		}
	}
	public virtual void BroadcastMessage(INetMessage msg, IRecipientFilter filter) {
		// #if DEBUG
		// 		ConDColorMsg(new Color(255, 0, 0), $"Broadcasting message of type {msg.GetType()} to {filter.GetRecipientCount()} recipients (reliable: {filter.IsReliable()}, init: {filter.IsInitMessage()})\n");
		// #endif
		if (filter.IsInitMessage()) {
			// This really only applies to the first player to connect, but that works in single player well enought
			if (IsActive())
				ConDMsg("SV_BroadcastMessage: Init message being created after signon buffer has been transmitted\n");

			if (!msg.WriteToBuffer(Signon)) {
				Sys.Error("SV_BroadcastMessage: Init message would overflow signon buffer!\n");
				return;
			}
		}
		else {
			msg.SetReliable(filter.IsReliable());

			int num = filter.GetRecipientCount();

			for (int i = 0; i < num; i++) {
				int index = filter.GetRecipientIndex(i);

				if (index < 1 || index > Clients.Count) {
					Msg("SV_BroadcastMessage:  Recipient Filter for message type %i (reliable: %s, init: %s) with bogus client index (%i) in list of %i clients\n",
							msg.GetType(),
							filter.IsReliable() ? "yes" : "no",
							filter.IsInitMessage() ? "yes" : "no",
							index, num);

					if (msg.IsReliable())
						Host.Error($"Reliable message (type {msg.GetType()}) discarded.");

					continue;
				}

				BaseClient cl = Clients[index - 1];

				if (!cl.IsSpawned())
					continue;

				if (!cl.SendNetMsg(msg)) {
					if (msg.IsReliable()) {
						DevMsg($"BroadcastMessage: Reliable filter message overflow for client {cl.GetClientName()}\n");
					}
				}
			}
		}
	}

	public virtual void BroadcastPrintf(ReadOnlySpan<char> msg) {
		SVC_Print print = new(msg);
		BroadcastMessage(print);
	}

	public virtual ReadOnlySpan<char> GetPassword() {
		string password = sv_password.GetString();

		// if password is empty or "none", return NULL
		if (password.Length == 0 || password.Equals("none", StringComparison.OrdinalIgnoreCase))
			return null;

		return password;
	}

	public virtual void SetMaxClients(int number) {
		MaxClients = Math.Clamp(number, 1, Constants.ABSOLUTE_PLAYER_LIMIT);
	}
	public virtual void SetPaused(bool paused) {
		if (!IsPausable()) {
			return;
		}

		if (!IsActive())
			return;

		if (paused)
			State = ServerState.Paused;
		else
			State = ServerState.Active;

		SVC_SetPause setpause = new() { Paused = paused };
		BroadcastMessage(setpause);
	}
	public virtual void SetPassword(ReadOnlySpan<char> password) {
		if (!password.IsEmpty)
			password.ClampedCopyTo(Password);
		else
			Password[0] = '\0';
	}

	public virtual void DisconnectClient(IClient client, ReadOnlySpan<char> reason) => client.Disconnect(reason);

	public virtual void WriteDeltaEntities(BaseClient client, ClientFrame to, ClientFrame from, bf_write pBuf) {
		EntityWriteInfo u = new() {
			Buffer = pBuf,
			To = to,
			ToSnapshot = to.GetSnapshot(),
			Baseline = client.Baseline,
			FullProps = 0,
			Server = this,
			ClientEntity = client.EntityIndex
		};

		if (IsHLTV() || IsReplay()) {
			// cull props only on master proxy
			u.CullProps = sv.IsActive();
		}
		else {
			u.CullProps = true;  // always cull props for players
		}

		if (from != null) {
			u.AsDelta = true;
			u.From = from;
			u.FromSnapshot = from.GetSnapshot();
			Assert(u.FromSnapshot);
		}
		else {
			u.AsDelta = false;
			u.From = null;
			u.FromSnapshot = null;
		}

		u.HeaderCount = 0;

		// set FromBaseline pointer if this snapshot may become a baseline update
		if (client.BaselineUpdateTick == -1) {
			client.BaselinesSent.ClearAll();
			to.FromBaseline = client.BaselinesSent;
		}

		// Write the header

		// TRACE_PACKET(("WriteDeltaEntities (%d)\n", u.ToSnapshot.NumEntities));

		u.Buffer.WriteUBitLong(SVC.PacketEntities, Protocol.NETMSG_TYPE_BITS);

		u.Buffer.WriteUBitLong((uint)u.ToSnapshot.NumEntities, Constants.MAX_EDICT_BITS);

		if (u.AsDelta) {
			u.Buffer.WriteOneBit(1); // use delta sequence

			u.Buffer.WriteLong((int)u.From.TickCount);    // This is the sequence # that we are updating from.
		}
		else {
			u.Buffer.WriteOneBit(0); // use baseline
		}

		u.Buffer.WriteUBitLong((uint)client.BaselineUsed, 1);  // tell client what baseline we are using

		// Store off current position
		int savepos = u.Buffer.BitsWritten;

		// Save room for number of headers to parse, too
		int bits = Constants.MAX_EDICT_BITS + Constants.DELTASIZE_BITS + 1;
		Span<byte> headerBits = stackalloc byte[Net.Bits2Bytes(bits)];
		u.Buffer.WriteBits(headerBits, bits);

		int startbit = u.Buffer.BitsWritten;

		bool isTracing = client.Tracing != 0;
		if (isTracing)
			client.TraceNetworkData(pBuf, "Delta Entities Overhead");

		// Don't work too hard if we're using the optimized single-player mode.
		if (CL.LocalNetworkBackdoor == null) {

			// Iterate through the in PVS bitfields until we find an entity 
			// that was either in the old pack or the new pack
			u.NextOldEntity();
			u.NextNewEntity();

			while ((u.OldEntity != PackedEntity.ENTITY_SENTINEL) || (u.NewEntity != PackedEntity.ENTITY_SENTINEL)) {
				u.NewPack = (u.NewEntity != PackedEntity.ENTITY_SENTINEL) ? framesnapshotmanager.GetPackedEntity(u.ToSnapshot, u.NewEntity) : null;
				u.OldPack = (u.OldEntity != PackedEntity.ENTITY_SENTINEL) ? framesnapshotmanager.GetPackedEntity(u.FromSnapshot, u.OldEntity) : null;

				int entStartBit = pBuf.BitsWritten;

				// Figure out how we want to write this entity.
				EntsWrite.DetermineUpdateType(u);
				EntsWrite.WriteEntityUpdate(u);

				if (!isTracing)
					continue;

				switch (u.UpdateType) {
					default:
					case UpdateType.PreserveEnt:
						break;
					case UpdateType.EnterPVS: {
							ReadOnlySpan<char> eString = sv.Edicts[u.NewPack.EntityIndex].GetNetworkable().GetClassName();
							client.TraceNetworkData(pBuf, $"enter [{eString}]");
						}
						break;
					case UpdateType.LeavePVS: {
							// Note, can't use GetNetworkable() since the edict has been freed at this point
							ReadOnlySpan<char> eString = u.OldPack.ServerClass.NetworkName;
							client.TraceNetworkData(pBuf, $"leave [{eString}]");
						}
						break;
					case UpdateType.DeltaEnt: {
							ReadOnlySpan<char> eString = sv.Edicts[u.OldPack.EntityIndex].GetNetworkable().GetClassName();
							client.TraceNetworkData(pBuf, $"delta [{eString}]");
						}
						break;
				}
			}

			// Now write out the express deletions
			int nNumDeletions = EntsWrite.WriteDeletions(u);
			if (isTracing)
				client.TraceNetworkData(pBuf, $"Delta: [{nNumDeletions}] deletions");
		}

		// get number of written bits
		int length = u.Buffer.BitsWritten - startbit;

		// go back to header and fill in correct length now
		int endbitpos = u.Buffer.BitsWritten;
		u.Buffer.Seek(savepos);
		u.Buffer.WriteUBitLong((uint)u.HeaderCount, Constants.MAX_EDICT_BITS);
		u.Buffer.WriteUBitLong((uint)length, Constants.DELTASIZE_BITS);

		bool bUpdateBaseline = ((client.BaselineUpdateTick == -1) &&
			(u.FullProps > 0 || !u.AsDelta));

		if (bUpdateBaseline && u.Baseline != null) {
			// tell client to use this snapshot as baseline update
			u.Buffer.WriteOneBit(1);
			client.BaselineUpdateTick = (int)to.TickCount;
		}
		else
			u.Buffer.WriteOneBit(0);

		u.Buffer.Seek(endbitpos);

		if (isTracing)
			client.TraceNetworkData(pBuf, "Delta Finish");

	}

	private readonly byte[] tempEntsBuffer = new byte[Protocol.MAX_PAYLOAD];
	private readonly List<EventInfo> tempEntsSorted = [];
	public virtual void WriteTempEntities(BaseClient client, FrameSnapshot to, FrameSnapshot from, bf_write pBuf, int nMaxEnts) {
		SVC_TempEntities msg = new();
		msg.DataOut.StartWriting(tempEntsBuffer, tempEntsBuffer.Length);
		bf_write buffer = msg.DataOut;

		FrameSnapshot? pSnapshot;
		EventInfo? lastEvent = null;

		bool bDebug = sv_debugtempentities.GetBool();

		nMaxEnts = Math.Min(nMaxEnts, (1 << EventInfo.EVENT_INDEX_BITS) - 1);

		if (from != null)
			pSnapshot = from.NextSnapshot();
		else
			pSnapshot = to;

		List<EventInfo> sorted = tempEntsSorted;
		sorted.Clear();

		while (pSnapshot != null && sorted.Count < nMaxEnts) {
			for (int i = 0; i < pSnapshot.NumTempEntities; i++) {
				EventInfo evnt = pSnapshot.TempEntities![i];

				if (client.IgnoreTempEntity(evnt))
					continue;

				sorted.Add(evnt);

				if (sorted.Count >= nMaxEnts)
					break;
			}

			if (pSnapshot == to)
				break;

			pSnapshot = framesnapshotmanager.NextSnapshot(pSnapshot);
		}

		if (sorted.Count <= 0)
			return;

		sorted.Sort((a, b) => a.ClassID.CompareTo(b.ClassID));

		for (int i = 0; i < sorted.Count; i++) {
			EventInfo evnt = sorted[i];

			if (evnt.FireDelay == 0.0f) {
				buffer.WriteOneBit(0);
			}
			else {
				buffer.WriteOneBit(1);
				buffer.WriteSBitLong((int)(evnt.FireDelay * 100.0f), 8);
			}

			if (lastEvent != null && lastEvent.ClassID == evnt.ClassID) {
				buffer.WriteOneBit(0);

				int startBit = bDebug ? buffer.BitsWritten : 0;

				EntsWrite.WriteAllDeltaProps(evnt.SendTable!,
					lastEvent.Data!,
					lastEvent.Bits,
					evnt.Data!,
					evnt.Bits,
					-1,
					buffer);

				if (bDebug) {
					int length = buffer.BitsWritten - startBit;
					DevMsg(2, $"TE {evnt.SendTable!.NetTableName} delta bits: {length}\n");
				}
			}
			else {
				buffer.WriteOneBit(1);

				int startBit = bDebug ? buffer.BitsWritten : 0;

				buffer.WriteUBitLong((uint)evnt.ClassID, GetClassBits());

				if (IsMultiplayer()) {
					EntsWrite.WriteAllDeltaProps(evnt.SendTable!,
						null,
						0,
						evnt.Data!,
						evnt.Bits,
						-1,
						buffer);
				}
				else
					buffer.WriteBits(evnt.Data!, evnt.Bits);

				if (bDebug) {
					int length = buffer.BitsWritten - startBit;
					DevMsg(2, $"TE {evnt.SendTable!.NetTableName} full bits: {length}\n");
				}
			}

			if (IsMultiplayer())
				lastEvent = evnt;
		}

		msg.NumEntries = sorted.Count;
		msg.WriteToBuffer(pBuf);
		sorted.Clear();
	}


	static readonly ConVar sv_max_queries_sec = new("sv_max_queries_sec", "3.0", 0, "Maximum queries per second to respond to from a single IP address.");
	static readonly ConVar sv_max_queries_window = new("sv_max_queries_window", "30", 0, "Window over which to average queries per second averages.");
	static readonly ConVar sv_max_queries_sec_global = new("sv_max_queries_sec_global", "3000", 0, "Maximum queries per second to respond to from anywhere.");
	static readonly ConVar sv_max_connects_sec = new("sv_max_connects_sec", "2.0", 0, "Maximum connections per second to respond to from a single IP address.");
	static readonly ConVar sv_max_connects_window = new("sv_max_connects_window", "4", 0, "Window over which to average connections per second averages.");
	static readonly ConVar sv_max_connects_sec_global = new("sv_max_connects_sec_global", "0", 0, "Maximum connections per second to respond to from anywhere.");


	readonly IPRateLimit QueryRateChecker = new(sv_max_queries_sec, sv_max_queries_window, sv_max_queries_sec_global);
	readonly IPRateLimit ConnectRateChecker = new(sv_max_connects_sec, sv_max_connects_window, sv_max_connects_sec_global);

	public virtual bool ProcessConnectionlessPacket(NetPacket packet) {
		bf_read msg = packet.Message;
		byte c = (byte)msg.ReadChar();

		if (c == 0)
			return false;

		switch (c) {
			case A2S.GetChallenge: {
					int clientChallenge = msg.ReadLong();
					ReplyChallenge(packet.From, clientChallenge);
				}
				break;
			case A2S.ServerQueryGetChallenge:
				ReplyServerChallenge(packet.From);
				break;
			case C2S.Connect: {
					Span<byte> cdkey = stackalloc byte[Protocol.STEAM_KEYSIZE];
					Span<char> name = stackalloc char[256];
					Span<char> password = stackalloc char[256];
					Span<char> productVersion = stackalloc char[32];

					int protocol = msg.ReadLong();
					int authProtocol = msg.ReadLong();
					int challengeNr = msg.ReadLong();
					int clientChallenge = msg.ReadLong();

					if (!CheckChallengeNr(packet.From, challengeNr)) {
						RejectConnection(packet.From, clientChallenge, "#GameUI_ServerRejectBadChallenge");
						break;
					}

					if (!ConnectRateChecker.CheckIP(packet.From))
						return false;
#if GMOD_DLL
					uint checksum = msg.ReadUBitLong(32); // Ignoring for now
#endif
					msg.ReadString(name);
					msg.ReadString(password);
					msg.ReadString(productVersion);

					ReadOnlySpan<char> versionInP4 = "2000";
					ReadOnlySpan<char> versionString = GetSteamInfIDVersionInfo().PatchVersion;
					if (strcmp(versionString, versionInP4) != 0 && strcmp(productVersion, versionInP4) != 0) {
						int nVersionCheck = strcmp(versionString, productVersion);
						if (nVersionCheck < 0) {
							RejectConnection(packet.From, clientChallenge, "#GameUI_ServerRejectOldVersion");
							break;
						}
						if (nVersionCheck > 0) {
							RejectConnection(packet.From, clientChallenge, "#GameUI_ServerRejectNewVersion");
							break;
						}
					}

					if (authProtocol == Protocol.PROTOCOL_STEAM) {
						int keyLen = msg.ReadShort();
						if (keyLen < 0 || keyLen > cdkey.Length) {
							RejectConnection(packet.From, clientChallenge, "#GameUI_ServerRejectBadSteamKey");
							break;
						}
						msg.ReadBytes(cdkey[..keyLen]);

						ConnectClient(packet.From, protocol, challengeNr, clientChallenge, authProtocol, name, password, cdkey, keyLen);   // cd key is actually a raw encrypted key	
					}
					else {
						msg.ReadString(cdkey);
						ConnectClient(packet.From, protocol, challengeNr, clientChallenge, authProtocol, name, password, cdkey, (int)strlen(cdkey));
					}
				}

				break;

			default: {
					if (!QueryRateChecker.CheckIP(packet.From))
						return false;

					if (Steam3Server().SteamGameServer() != null) {
						SteamGameServer.HandleIncomingPacket(
							packet.Message.GetData(),
							packet.Message.BytesAvailable,
							(uint)packet.From.Endpoint!.AddressFamily,
							(ushort)packet.From.Endpoint!.Port
							);

						ForwardPacketsFromMasterServerUpdater();
					}
				}

				break;
		}

		return true;
	}

	public virtual void Init(bool isDedicated) {
		MaxClients = 0;
		SpawnCount = 0;
		UserID = 1;
		NumConnections = 0;
		Dedicated = isDedicated;
		Socket = NetSocketType.Server;

		Signon.DebugName = "m_Signon";

		cvar.Changed += ServerNotifyVarChangeCallback;
		SetMasterServerRulesDirty();

		Clear();
	}

	private void ServerNotifyVarChangeCallback(IConVar var, in ConVarChangeContext ctx) {
		if (!var.IsFlagSet(FCvar.Notify))
			return;

		ISteamGameServer? updater = Steam3Server().SteamGameServer();

		if (updater == null) {
			// This will force it to send all the rules whenever the master server updater is there.
			sv.SetMasterServerRulesDirty();
			return;
		}

		SetMasterServerKeyValue(updater, var);
	}

	private void SetMasterServerKeyValue(ISteamGameServer server, IConVar var) {
		if (var.IsFlagSet(FCvar.Protected)) {
			if (var.GetString()[0] != '\0' && 0 != stricmp(var.GetString(), "none"))
				SteamGameServer.SetKeyValue(var.GetName(), "1");
			else
				SteamGameServer.SetKeyValue(var.GetName(), "0");
		}
		else
			SteamGameServer.SetKeyValue(var.GetName(), var.GetString());

		if (Steam3Server().BIsActive())
			sv.RecalculateTags();
	}

	public virtual void Clear() {
		if (StringTables != null) {
			StringTables.RemoveAllTables();
			StringTables = null;
			serverGameDLL?.GMOD_OnStringTablesRemoved();
		}

		InstanceBaselineTable = null;
		LightStyleTable = null;
		UserInfoTable = null;
		ServerStartupTable = null;

		State = ServerState.Dead;

		TickCount = 0;

		memreset((Span<char>)MapName);
		memreset((Span<char>)Skyname);
		memreset(ref WorldmapMD5);

		// Use a different limit on the signon buffer, so we can save some memory in SP (for xbox).
		if (IsMultiplayer() || IsDedicated())
			SignonBuffer.EnsureCapacity(Protocol.MAX_PAYLOAD);
		else
			SignonBuffer.EnsureCapacity(16384);

		Signon.StartWriting(SignonBuffer.Base(), (nuint)SignonBuffer.Count(), 0);
		Signon.DebugName = "m_Signon";

		ServerClasses = 0;
		ServerClassBits = 0;

		LastRandomNonce = CurrentRandomNonce = 0;
		PausedTimeEnd = -1;
	}
	public virtual void Shutdown() {
		if (!IsActive())
			return;

		State = ServerState.Dead;

		// Only drop clients if we have not cleared out entity data prior to this.
		for (int i = Clients.Count - 1; i >= 0; i--) {
			BaseClient cl = Clients[i];
			if (cl.IsConnected())
				cl.Disconnect("Server shutting down");
			else {
				// free any memory do this out side here in case the reason the server is shutting down 
				// is because the listen server client typed disconnect, in which case we won't call
				// cl->DropClient, but the client might have some frame snapshot references left over, etc.
				cl.Clear();
			}

			cl.Dispose();

			Clients.RemoveAt(i);
		}

		// Let drop messages go out
		Sys.Sleep(100);

		// clear everything
		Clear();
	}
	public virtual BaseClient? CreateFakeClient(ReadOnlySpan<char> name) {
		NetAddress adr = new(); // it's an empty address
		BaseClient? fakeclient = GetFreeClient(adr);

		if (fakeclient == null) {
			// server is full
			return null;
		}

		INetChannel? netchan = null;
		if (sv_stressbots.GetBool()) {
			NetAddress adrNull = new();
			adrNull.SetIP(0, 0); // 0.0.0.0:0 signifies a bot. It'll plumb all the way down to winsock calls but it won't make them.
			netchan = Net.CreateNetChannel(Socket, adrNull, adrNull.ToString(), fakeclient, true);
		}

		// a NULL netchannel signals a fakeclient
		UserID = GetNextUserID();
		NumConnections++;

		fakeclient.SetReportThisFakeClient(ReportNewFakeClients);
		fakeclient.Connect(name, UserID, netchan, true, 0);

		// fake some cvar settings
		//fakeclient->SetUserCVar( "name", name ); // set already by Connect()
		fakeclient.SetUserCVar("rate", "30000");
		fakeclient.SetUserCVar("cl_updaterate", "20");
		fakeclient.SetUserCVar("cl_interp_ratio", "1.0");
		fakeclient.SetUserCVar("cl_interp", "0.1");
		fakeclient.SetUserCVar("cl_interpolate", "0");
		fakeclient.SetUserCVar("cl_predict", "1");
		fakeclient.SetUserCVar("cl_predictweapons", "1");
		fakeclient.SetUserCVar("cl_lagcompensation", "1");
		fakeclient.SetUserCVar("closecaption", "0");
		fakeclient.SetUserCVar("english", "1");

		fakeclient.SetUserCVar("cl_clanid", "0");
		fakeclient.SetUserCVar("cl_team", "blue");
		fakeclient.SetUserCVar("hud_classautokill", "1");
		fakeclient.SetUserCVar("tf_medigun_autoheal", "0");
		fakeclient.SetUserCVar("cl_autorezoom", "1");
		fakeclient.SetUserCVar("fov_desired", "75");
		fakeclient.SetUserCVar("tf_remember_lastswitched", "0");

		fakeclient.SetUserCVar("cl_autoreload", "0");
		fakeclient.SetUserCVar("tf_remember_activeweapon", "0");
		fakeclient.SetUserCVar("hud_combattext", "0");
		fakeclient.SetUserCVar("cl_flipviewmodels", "0");

		fakeclient.ActivatePlayer();

		fakeclient.SignOnTick = TickCount;

		return fakeclient;
	}
	public virtual void RemoveClientFromGame(BaseClient cl) { }

	public virtual void SendClientMessages(bool bSendSnapshots) {
		for (int i = 0; i < Clients.Count; i++) {
			BaseClient cl = Clients[i];

			if (!cl.ShouldSendMessages())
				continue;

			if (cl.NetChannel != null) {
				cl.NetChannel.Transmit();
				cl.UpdateSendState();
			}
			else
				Msg("Client has no netchannel.\n");
		}
	}

	public virtual void FillServerInfo(SVC_ServerInfo serverinfo) {
		serverinfo.Protocol = Protocol.VERSION;
		serverinfo.ServerCount = GetSpawnCount();
		if (WorldmapMD5 != default) // FIXME: singleplayer crash
			serverinfo.MapMD5 = WorldmapMD5;
		serverinfo.MaxClients = GetMaxClients();
		serverinfo.MaxClasses = ServerClasses;
		serverinfo.IsDedicated = IsDedicated();
		serverinfo.TickInterval = GetTickInterval();
		serverinfo.GameDirectory = "garrysmod"; // TODO: Common.Gamedir. For now, lie!
		serverinfo.MapName = new(GetMapName());
		serverinfo.SkyName = new(((ReadOnlySpan<char>)Skyname).SliceNullTerminatedString());
		serverinfo.HostName = new(GetName());
		serverinfo.IsHLTV = IsHLTV();
	}

	public virtual void UserInfoChanged(int nClientIndex) {
		if (Clients[nClientIndex].FillUserInfo(out PlayerInfo pi)) {
			Span<byte> data = stackalloc byte[PlayerInfo.SIZEOF];
			pi.ToBytes(data);
			UserInfoTable!.SetStringUserData(nClientIndex, PlayerInfo.SIZEOF, data);
		}
		else
			UserInfoTable!.SetStringUserData(nClientIndex, 0, null);
	}

	public bool GetClassBaseline(ServerClass pClass, out byte[]? pData) {
		if (sv_instancebaselines.GetBool()) {
			ErrorIfNot(pClass.InstanceBaselineIndex != INetworkStringTable.INVALID_STRING_INDEX, $"SV_GetInstanceBaseline: missing instance baseline for class '{pClass.NetworkName}'\n");

			pData = GetInstanceBaselineTable()!.GetStringUserData(pClass.InstanceBaselineIndex);
			if (pData?.Length == 0) pData = [0];
			return true;
		}
		else {
			pData = [0];
			return true;
		}
	}

	public const double CHALLENGE_NONCE_LIFETIME = 6d;
	public const int MAX_DELTA_TICKS = 192;

	public void RunFrame() {
		Net.ProcessSocket(Socket, this);

		CheckTimeouts();
		UpdateUserSettings();
		SendPendingServerInfo();
		CalculateCPUUsage();
		UpdateMasterServer();

		if (LastRandomNumberGenerationTime < 0 || (LastRandomNumberGenerationTime + CHALLENGE_NONCE_LIFETIME) < serverGlobalVariables.RealTime) {
			LastRandomNonce = CurrentRandomNonce;
			CurrentRandomNonce = (uint)((RandomInt(0, 0xFFFF)) << 16) | (uint)RandomInt(0, 0xFFFF);
			LastRandomNumberGenerationTime = serverGlobalVariables.RealTime;
		}

		if (PausedTimeEnd >= 0 && State == ServerState.Paused && Sys.Time >= PausedTimeEnd)
			SetPausedForced(false);
	}

	public void InactivateClients() {
		for (int i = 0; i < Clients.Count; i++) {
			BaseClient cl = Clients[i];

			if (cl.IsFakeClient() && !cl.IsHLTV()) {
				Steam3Server().NotifyClientDisconnect(cl);
				cl.Clear();
				continue;
			}
			else if (!cl.IsConnected())
				continue;

			cl.Inactivate();
		}
	}

	public void ReconnectClients() {
		for (int i = 0; i < Clients.Count; i++) {
			BaseClient cl = Clients[i];

			if (cl.IsConnected()) {
				cl.SignOnState = SignOnState.Connected;
				NET_SignonState signon = new(cl.SignOnState, -1);
				cl.SendNetMsg(signon);
			}
		}
	}
	public void CheckTimeouts() {
		int i;

#if DEBUG
		for (i = 0; i < Clients.Count; i++) {
			IClient cl = Clients[i];

			if (cl.IsFakeClient() || !cl.IsConnected())
				continue;

			INetChannel? netchan = cl.GetNetChannel();
			if (netchan == null)
				continue;

			if (netchan.IsTimedOut())
				cl.Disconnect($"{cl.GetClientName()} timed out");
		}
#endif

		for (i = 0; i < Clients.Count; i++) {
			IClient cl = Clients[i];

			if (cl.IsFakeClient() || !cl.IsConnected())
				continue;

			if (cl.GetNetChannel() != null && cl.GetNetChannel()!.IsOverflowed())
				cl.Disconnect($"Client {i} overflowed reliable channel.");
		}
	}
	public void UpdateUserSettings() {
		for (int i = 0; i < Clients.Count; i++) {
			BaseClient cl = Clients[i];

			cl.CheckFlushNameChange();

			if (cl.ConVarsChanged)
				cl.UpdateUserSettings();
		}
	}
	public void SendPendingServerInfo() {
		for (int i = 0; i < Clients.Count; i++) {
			BaseClient? cl = Clients[i];

			if (cl.NeedSendServerInfo)
				cl.SendServerInfo();
		}
	}

	static readonly byte[] s_packedData = new byte[Constants.MAX_PACKEDENTITY_DATA];
	// We really need bitbuffers to be span objects sometime... or at least have the ability to do this...
	// todo fix s_unpackedDataTemp
	static readonly byte[] s_unpackedDataTemp = new byte[Constants.MAX_PACKEDENTITY_DATA];

	public ReadOnlySpan<byte> CompressPackedEntity(ServerClass pServerClass, ReadOnlySpan<byte> data, out int bits) {
		bf_write writeBuf = new(s_packedData, s_packedData.Length);

		byte[]? pBaselineData = null;
		Assert(pServerClass != null);

		GetClassBaseline(pServerClass, out pBaselineData);
		int nBaselineBits = (pBaselineData?.Length ?? 0) * 8;

		Assert(pBaselineData?.Length != 0);

		SendTable.WriteAllDeltaProps(
			pServerClass.Table,
			pBaselineData!,
			nBaselineBits,
			data,
			out bits,
			-1,
			writeBuf);

		//overwrite in bits with out bits
		bits = writeBuf.BitsWritten;

		return s_packedData;
	}
	public ReadOnlySpan<byte> UncompressPackedEntity(PackedEntity pPackedEntity, out int bits) {
		ref UnpackedDataCache pdc = ref framesnapshotmanager.GetCachedUncompressedEntity(pPackedEntity);

		if (pdc.Bits > 0) {
			// found valid uncompressed version in cache
			bits = pdc.Bits;
			return pdc.Data;
		}

		// not in cache, so uncompress it

		byte[]? pBaseline;

		GetClassBaseline(pPackedEntity.ServerClass!, out pBaseline);
		int nBaselineBytes = pBaseline?.Length ?? 0;

		Assert(pBaseline != null);

		// store this baseline in u.m_pUpdateBaselines
		bf_read oldBuf = new(pBaseline, nBaselineBytes);
		bf_read newBuf = new(pPackedEntity!.GetData()!, Protocol.Bits2Bytes(pPackedEntity.GetNumBits()));
		bf_write outBuf = new(s_unpackedDataTemp, Constants.MAX_PACKEDENTITY_DATA);

		Assert(pPackedEntity.ClientClass);

		RecvTable.MergeDeltas(
			pPackedEntity.ClientClass!.RecvTable,
			oldBuf,
			newBuf,
			outBuf);

		bits = pdc.Bits = outBuf.BitsWritten;
		s_unpackedDataTemp.CopyTo(pdc.Data);

		return pdc.Data;
	}

	public INetworkStringTable? GetInstanceBaselineTable() => InstanceBaselineTable ??= StringTables!.FindTable(Protocol.INSTANCE_BASELINE_TABLENAME);
	public INetworkStringTable? GetLightStyleTable() => LightStyleTable ??= StringTables!.FindTable(Protocol.LIGHT_STYLES_TABLENAME);
	public INetworkStringTable? GetUserInfoTable() => UserInfoTable ??= StringTables?.FindTable(Protocol.USER_INFO_TABLENAME);

	public virtual void RejectConnection(NetAddress adr, int clientChallenge, ReadOnlySpan<char> s) {
		byte[] msg_buffer = new byte[Protocol.MAX_ROUTABLE_PAYLOAD];
		bf_write msg = new(msg_buffer, msg_buffer.Length);

		msg.WriteLong(Protocol.CONNECTIONLESS_HEADER);
		msg.WriteByte(S2C.ConnectionRejected);
		msg.WriteLong(clientChallenge);
		msg.WriteString(s);

		Net.SendPacket(null!, Socket, adr, msg.GetData(), msg.BytesWritten);
	}

	public TimeUnit_t GetFinalTickTime() => (TickCount + (Host.FrameTicks - Host.CurrentFrameTick)) * TickInterval;

	public virtual bool CheckIPRestrictions(NetAddress adr, int nAuthProtocol) {
		return true; // todo
	}

	public void SetMasterServerRulesDirty() {
		MasterServerRulesDirty = true;
	}
	public void SendQueryPortToClient(NetAddress adr) {
		throw new NotImplementedException();
	}

	public void RecalculateTags() {
		throw new NotImplementedException();
	}
	public void AddTag(ReadOnlySpan<char> pszTag) {
		throw new NotImplementedException();
	}
	public void RemoveTag(ReadOnlySpan<char> pszTag) {
		throw new NotImplementedException();
	}

	public int GetNumConnections() => NumConnections;

	public void SetReportNewFakeClients(bool bReportNewFakeClients) { ReportNewFakeClients = bReportNewFakeClients; }

	public void SetPausedForced(bool paused, TimeUnit_t flDuration = -1) {
		if (!IsActive())
			return;

		State = (paused) ? ServerState.Paused : ServerState.Active;
		PausedTimeEnd = (paused && flDuration > 0) ? Sys.Time + flDuration : -1;

		// SVC_SetPauseTimed setpause = new(paused, PausedTimeEnd);
		// BroadcastMessage(setpause);
	}

	protected virtual IClient? ConnectClient(NetAddress adr, int protocol, int challenge, int clientChallenge, int authProtocol,
							ReadOnlySpan<char> name, ReadOnlySpan<char> password, ReadOnlySpan<byte> hashedCDkey, int cdKeyLen) {
		Common.TimestampedLog("CBaseServer::ConnectClient");

		if (!IsActive())
			return null;

		if (name.IsEmpty || password.IsEmpty || hashedCDkey.IsEmpty)
			return null;

		// Make sure protocols match up
		if (!CheckProtocol(adr, protocol, clientChallenge))
			return null;

		if (!CheckChallengeNr(adr, challenge)) {
			RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectBadChallenge");
			return null;
		}

		if (!IsHLTV() && !IsReplay()) {
#if !NO_STEAM
			if (!CheckIPRestrictions(adr, authProtocol)) {
				RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectLANRestrict");
				return null;
			}
#endif
			if (!CheckPassword(adr, password, name)) {
				ConMsg("%s:  password failed.\n", adr.ToString());
				RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectBadPassword");
				return null;
			}
		}

		Common.TimestampedLog("BaseServer.ConnectClient: GetFreeClient");

		BaseClient? client = GetFreeClient(adr);

		if (client == null) {
			RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectServerFull");
			return null;
		}

		int nNextUserID = GetNextUserID();
		if (!CheckChallengeType(client, nNextUserID, adr, authProtocol, hashedCDkey, cdKeyLen, clientChallenge))
			return null;

		ISteamGameServer? steamGameServer = Steam3Server().SteamGameServer();
		if (steamGameServer == null && authProtocol == Protocol.PROTOCOL_STEAM)
			Warning("NULL ISteamGameServer in ConnectClient. Steam authentication may fail.\n");

		if (Filter.IsUserBanned(client.GetNetworkID())) {
			if (steamGameServer != null && authProtocol == Protocol.PROTOCOL_STEAM)
				steamGameServer.SendUserDisconnect_DEPRECATED(client.SteamID);

			RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectBanned");
			return null;
		}

		Common.TimestampedLog("CBaseServer::ConnectClient:  NET_CreateNetChannel");

		// create network channel
		INetChannel? netchan = Net.CreateNetChannel(Socket, adr, adr.ToString(), client);

		if (netchan == null) {
			if (steamGameServer != null && authProtocol == Protocol.PROTOCOL_STEAM)
				steamGameServer.SendUserDisconnect_DEPRECATED(client.SteamID);

			RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectFailedChannel");
			return null;
		}

		netchan.SetChallengeNr((uint)challenge);

		Common.TimestampedLog("CBaseServer::ConnectClient:  client->Connect");
		client.Connect(name, nNextUserID, netchan, false, clientChallenge);

		UserID = nNextUserID;
		NumConnections++;

		client.SnapshotInterval = 1.0f / 20.0f;
		client.NextMessageTime = Net.Time + client.SnapshotInterval;
		client.DeltaTick = -1;
		client.SignOnTick = 0;
		client.StringTableAckTick = 0;
		// client.LastSnapshot = NULL;

		{
			byte[] msg_buffer = new byte[Protocol.MAX_ROUTABLE_PAYLOAD];
			bf_write msg = new(msg_buffer, msg_buffer.Length);

			msg.WriteLong(Protocol.CONNECTIONLESS_HEADER);
			msg.WriteByte(S2C.Connection);
			msg.WriteLong(clientChallenge);
			msg.WriteString("0000000000");

			Net.SendPacket(null!, Socket, adr, msg.GetData(), msg.BytesWritten);
		}

		if (authProtocol == Protocol.PROTOCOL_HASHEDCDKEY) {
			strcpy(client.GUID, hashedCDkey);
			client.GUID[Constants.SIGNED_GUID_LEN] = 0;
		}
		else if (authProtocol == Protocol.PROTOCOL_STEAM) {
			// StartSteamValidation() above initialized the clients networkid
		}

		if (netchan != null && !netchan.IsLoopback())
			ConMsg($"Client \"{client.GetClientName()}\" connected ({netchan.GetAddress()}).\n");

		serverGameClients.GMOD_ClientConnected(client.GetPlayerSlot());

		return client;
	}

	protected virtual BaseClient? GetFreeClient(NetAddress adr) {
		BaseClient? freeclient = null;

		for (int slot = 0; slot < Clients.Count; slot++) {
			BaseClient client = Clients[slot];

			if (client.IsFakeClient())
				continue;

			if (client.IsConnected()) {
				if (adr.CompareAdr(client.NetChannel.GetRemoteAddress())) {
					ConMsg($"{adr.ToString()}:reconnect\n");

					RemoveClientFromGame(client);

					// perform a silent netchannel shutdown, don't send disconnect msg
					client.NetChannel.Shutdown(null);
					client.NetChannel = null!;

					client.Clear();
					return client;
				}
			}
			else {
				freeclient ??= client;
			}
		}

		if (freeclient == null) {
			int count = Clients.Count;

			if (count >= MaxClients)
				return null;

			// we have to create a new client slot
			freeclient = CreateNewClient(count);

			Clients.Add(freeclient);
		}

		return freeclient;
	}

	protected virtual BaseClient CreateNewClient(int i) { AssertMsg(false, "BaseServer.CreateNewClient() being called - must be implemented in derived class!"); return null!; } // must be derived


	protected virtual bool FinishCertificateCheck(NetAddress adr, int a, ReadOnlySpan<char> b, int c) { return true; }

	protected virtual unsafe int GetChallengeNr(NetAddress adr) {
		ulong challenge = ((ulong)adr.GetIPNetworkByteOrder() << 32) + CurrentRandomNonce;
		CRC32_t hash = default;
		CRC32.Init(ref hash);
		CRC32.ProcessBuffer(ref hash, &challenge, sizeof(ulong));
		CRC32.Final(ref hash);
		return (int)hash;
	}

	bool AllowDebugDedicatedServerOutsideSteam() {
#if ALLOW_DEBUG_DEDICATED_SERVER_OUTSIDE_STEAM
	return true;
#else
		return false;
#endif
	}

	protected virtual int GetChallengeType(NetAddress adr) {
		if (AllowDebugDedicatedServerOutsideSteam())
			return Protocol.PROTOCOL_HASHEDCDKEY;

#if !SWDS
		if (Host.IsSinglePlayerGame() || !IsDedicated())
			return Protocol.PROTOCOL_HASHEDCDKEY;
		else
#endif
			return Protocol.PROTOCOL_STEAM;
	}

	protected virtual bool CheckProtocol(NetAddress adr, int nProtocol, int clientChallenge) {
		if (nProtocol != Protocol.VERSION) {
			if (nProtocol > Protocol.VERSION)
				RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectOldProtocol");
			else
				RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectNewProtocol");
			return false;
		}

		return true;
	}
	protected virtual unsafe bool CheckChallengeNr(NetAddress adr, int nChallengeValue) {
		if (adr.IsLoopback())
			return true;

		ulong challenge = ((ulong)adr.GetIPNetworkByteOrder() << 32) + CurrentRandomNonce;
		CRC32_t hash = default;
		CRC32.Init(ref hash);
		CRC32.ProcessBuffer(ref hash, &challenge, sizeof(ulong));
		CRC32.Final(ref hash);
		if ((int)hash == nChallengeValue)
			return true;

		challenge &= 0xffffffff00000000ul;
		challenge += LastRandomNonce;
		hash = 0;
		CRC32.Init(ref hash);
		CRC32.ProcessBuffer(ref hash, &challenge, sizeof(ulong));
		CRC32.Final(ref hash);
		if ((int)hash == nChallengeValue)
			return true;

		return false;
	}
	protected virtual bool CheckChallengeType(BaseClient client, int nNewUserID, NetAddress adr, int nAuthProtocol, ReadOnlySpan<byte> pchLogonCookie, int cbCookie, int clientChallenge) {
		if (AllowDebugDedicatedServerOutsideSteam())
			return true;

		if ((nAuthProtocol <= 0) || (nAuthProtocol > Protocol.PROTOCOL_LASTVALID)) {
			RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectInvalidConnection");
			return false;
		}

		if ((nAuthProtocol == Protocol.PROTOCOL_HASHEDCDKEY) && (pchLogonCookie.IsEmpty || strlen(pchLogonCookie) != 32)) {
			RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectInvalidCertLen");
			return false;
		}

		Assert(!IsReplay());

		if (IsHLTV()) {
			Assert(nAuthProtocol == Protocol.PROTOCOL_HASHEDCDKEY);
			Assert(!client.SteamID.IsValid());
		}
		else if (nAuthProtocol == Protocol.PROTOCOL_STEAM) {
			client.SetSteamID(new());
			if (cbCookie <= 0 || cbCookie >= Protocol.STEAM_KEYSIZE) {
				RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectInvalidSteamCertLen");
				return false;
			}

			NetAddress checkAdr = adr.Copy();
			if (adr.Type == NetAddressType.Loopback || adr.IsLocalhost())
				checkAdr.SetIP(Net.LocalAdr.GetIPHostByteOrder());

			if (!Steam3Server().NotifyClientConnect(client, nNewUserID, checkAdr, pchLogonCookie, cbCookie) && !Steam3Server().BLanOnly()) {
				RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectSteam");
				return false;
			}

			// Matchmaking
		}
		else {
			if (!Steam3Server().NotifyLocalClientConnect(client)) {
				RejectConnection(adr, clientChallenge, "#GameUI_ServerRejectGS");
				return false;
			}
		}

		return true;
	}
	protected virtual bool CheckPassword(NetAddress adr, ReadOnlySpan<char> password, ReadOnlySpan<char> name) {
		ReadOnlySpan<char> server_password = GetPassword();

		if (server_password.IsEmpty)
			return true;

		if (adr.IsLocalhost() || adr.IsLoopback())
			return true;

		int iServerPassLen = (int)strlen(server_password);

		if (iServerPassLen != (int)strlen(password))
			return false;

		if (strncmp(password, server_password, iServerPassLen) == 0)
			return true;

		return false;
	}
	protected virtual bool CheckIPConnectionReuse(NetAddress adr) {
		throw new NotImplementedException();
	}

	protected virtual void ReplyChallenge(NetAddress adr, int clientChallenge) {
		byte[] buffer = new byte[Protocol.STEAM_KEYSIZE + 32];
		bf_write msg = new(buffer, buffer.Length);

		// get a free challenge number
		int challengeNr = GetChallengeNr(adr);
		int authprotocol = GetChallengeType(adr);

		msg.WriteLong(Protocol.CONNECTIONLESS_HEADER);

		msg.WriteByte(S2C.Challenge);
		msg.WriteLong((int)S2C.MagicVersion);
		msg.WriteLong(challengeNr);
		msg.WriteLong(clientChallenge);
		msg.WriteLong(authprotocol);
		if (authprotocol == Protocol.PROTOCOL_STEAM) {
			msg.WriteShort(0);
			CSteamID steamID = Steam3Server().GetGSSteamID();
			ulong unSteamID = steamID.ConvertToUint64();
			msg.WriteBytes(new ReadOnlySpan<ulong>(in unSteamID).Cast<ulong, byte>());
			msg.WriteByte(Steam3Server().BSecure() ? 1 : 0);
		}
		Net.SendPacket(null!, Socket, adr, msg.GetData(), msg.BytesWritten);
	}
	protected virtual void ReplyServerChallenge(NetAddress adr) {
		throw new NotImplementedException();
	}

	protected virtual void CalculateCPUUsage() {

	}

	// Keep the master server data updated.
	protected virtual bool ShouldUpdateMasterServer() {
		return true;
	}

	protected void CheckMasterServerRequestRestart() {
		// todo
	}
	const double MASTER_SERVER_UPDATE_INTERVAL = 2.0;
	static bool bUpdateMasterServers;
	protected void UpdateMasterServer() {
		if (!ShouldUpdateMasterServer())
			return;

		if (Steam3Server().SteamGameServer() == null)
			return;

		// Only update every so often.
		TimeUnit_t curtime = Platform.Time;
		if (curtime - LastMasterServerUpdateTime < MASTER_SERVER_UPDATE_INTERVAL)
			return;

		LastMasterServerUpdateTime = curtime;

		ForwardPacketsFromMasterServerUpdater();
		CheckMasterServerRequestRestart();


		if (Net.Dedicated && sv_region.GetInt() == -1) {
			sv_region.SetValue(255); // HACK!HACK! undo me once we want to enforce regions

			//Log_Printf( "You must set sv_region in your server.cfg or use +sv_region on the command line\n" );
			//Con_Printf( "You must set sv_region in your server.cfg or use +sv_region on the command line\n" );
			//Cbuf_AddText( "quit\n" );
			//return;
		}

		bUpdateMasterServers = 0 == commandLine.FindParm("-nomaster");
		if (!bUpdateMasterServers)
			return;

		bool active = IsActive() && IsMultiplayer();
		if (serverGameDLL != null && serverGameDLL.ShouldHideServer())
			active = false;

		SteamGameServer.SetAdvertiseServerActive(active);

		if (!active)
			return;

		UpdateMasterServerRules();
		UpdateMasterServerPlayers();
		Steam3Server().SendUpdatedServerDetails();
	}
	protected void UpdateMasterServerRules() { }
	protected virtual void UpdateMasterServerPlayers() { }
	readonly byte[] packetData = new byte[16 * 1024];
	readonly NetAddress adr = new();
	protected void ForwardPacketsFromMasterServerUpdater() {
		ISteamGameServer? p = Steam3Server().SteamGameServer();
		if (p == null)
			return;

		while (true) {
			uint netadrAddress;
			ushort netadrPort;
			int len = p.GetNextOutgoingPacket(packetData, packetData.Length, out netadrAddress, out netadrPort);
			if (len <= 0)
				break;

			// Send this packet for them..
			adr.SetIP(netadrAddress, netadrPort);
			Net.SendPacket(null!, Socket, adr, packetData, len);
		}
	}

	public bool RestartOnLevelChange() => bRestartOnLevelChange;
	public void SetRestartOnLevelChange(bool state) { bRestartOnLevelChange = state; }

	protected bool RequireValidChallenge(NetAddress adr) => !sv_enableoldqueries.GetBool();
	protected bool ValidChallenge(NetAddress adr, int challengeNr) {
		if (!IsActive())
			return false;

		if (!IsMultiplayer())
			return false;

		if (RequireValidChallenge(adr)) {
			if (!CheckChallengeNr(adr, challengeNr)) {
				ReplyServerChallenge(adr);
				return false;
			}
		}

		return true;
	}
	protected bool ValidInfoChallenge(NetAddress adr, ReadOnlySpan<char> nugget) {
		if (!IsActive())
			return false;

		if (!IsMultiplayer())
			return false;

		if (IsReplay())
			return false;

		if (RequireValidChallenge(adr))
			if (stricmp(nugget, A2S.KEY_STRING) != 0)
				return false;

		return true;
	}


	public ServerState State;     // some actions are only valid during load
	public NetSocketType Socket;       // network socket 
	public long TickCount;   // current server tick
	public bool SimulatingTicks;        // whether or not the server is currently simulating ticks
	public InlineArray64<char> MapName;       // map name
	public InlineArray64<char> MapFilename;   // map filename, may bear no resemblance to map name
	public InlineArray64<char> Skyname;       // skybox name
	public InlineArray32<char> Password;        // server password

	public MD5Value WorldmapMD5;     // For detecting that client has a hacked local copy of map, the client will be dropped if this occurs.

	public NetworkStringTableContainer? StringTables;   // newtork string table container

	public INetworkStringTable? InstanceBaselineTable;
	public INetworkStringTable? LightStyleTable;
	public INetworkStringTable? UserInfoTable;
	public INetworkStringTable? ServerStartupTable;
	public INetworkStringTable? DownloadableFileTable;

	// This will get set to NET_MAX_PAYLOAD if the server is MP.
	public readonly bf_write Signon = new();
	public readonly UtlMemory<byte> SignonBuffer = new();

	public int ServerClasses;      // number of unique server classes
	public int ServerClassBits; // log2 of serverclasses

	// Gets the next user ID mod SHRT_MAX and unique (not used by any active clients).
	private int GetNextUserID() {
		for (int i = 0; i < Clients.Count + 1; i++) {
			int nTestID = (UserID + i + 1) % short.MaxValue;

			// Make sure no client has this user ID.		
			int iClient;
			for (iClient = 0; iClient < Clients.Count; iClient++) {
				if (Clients[iClient].GetUserID() == nTestID)
					break;
			}

			// Ok, no client has this ID, so return it.		
			if (iClient == Clients.Count)
				return nTestID;
		}

		AssertMsg(false, "GetNextUserID: can't find a unique ID.");
		return UserID + 1;
	}
	private int UserID;          // increases by one with every new client


	protected int MaxClients;         // Current max #
	protected int SpawnCount;          // Number of servers spawned since start,
									   // used to check late spawns (e.g., when d/l'ing lots of
									   // data)
	protected TimeUnit_t TickInterval;     // time for 1 tick in seconds


	protected readonly List<BaseClient> Clients = [];     // array of up to [maxclients] client slots.

	protected bool Dedicated;

	protected uint CurrentRandomNonce;
	protected uint LastRandomNonce;
	protected TimeUnit_t LastRandomNumberGenerationTime;
	protected float CPUPercent;
	protected TimeUnit_t StartTime;
	protected TimeUnit_t LastCPUCheckTime;

	// This is only used for Steam's master server updater to refer to this server uniquely.
	public bool bRestartOnLevelChange;

	protected bool MasterServerRulesDirty;
	protected TimeUnit_t LastMasterServerUpdateTime;

	protected int NumConnections;      //Number of successful client connections.

	protected bool ReportNewFakeClients; // Whether or not newly created fake clients should be included in server browser totals
	protected TimeUnit_t PausedTimeEnd;
}
