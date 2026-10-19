using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Networking;
using Source.Engine.Client;
using Source.Engine.Server;

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static Source.Common.Networking.Protocol;
using static Source.Constants;

namespace Source.Engine;

/// <summary>
/// Various clientside methods. In Source, these would mostly be represented by
/// CL_MethodName's in the static global namespace
/// </summary>
public partial class CL(IServiceProvider services, Net Net,
	ClientGlobalVariables clientGlobalVariables,
	CommonHostState host_state, Host Host, Cbuf Cbuf, Scr Scr,
#if !SWDS
	IEngineVGuiInternal? EngineVGui,
#endif
	Shader Shader, ClientDLL ClientDLL, EngineRecvTable RecvTable, Sound Sound)
{
	public IPrediction ClientSidePrediction => ClientDLL.ClientSidePrediction;
	public IClientEntityList EntityList => ClientDLL.EntityList;
	public ICenterPrint CenterPrint => ClientDLL.CenterPrint;
	public IClientLeafSystemEngine ClientLeafSystem => ClientDLL.ClientLeafSystem;


	public IBaseClientDLL clientDLL = null!;
	public IEngineClient engineClient = null!;
	public void ApplyAddAngle() {

	}

	public static readonly ConVar cl_language = new("cl_language", "english", FCvar.UserInfo, "Language (from HKCU\\Software\\Valve\\Steam\\Language)");
	static readonly ConVar cl_LocalNetworkBackdoor =new("cl_localnetworkbackdoor", "1", 0, "Enable network optimizations for single player games.");
	static readonly ConVar cl_ignorepackets = new("cl_ignorepackets", "0", FCvar.Cheat, "Force client to ignore packets (for debugging).");

	public void CheckClientState() {
		bool useBackdoor = cl_LocalNetworkBackdoor.GetInt() != 0 &&
						(cl.NetChannel != null && cl.NetChannel.IsLoopback()) &&
						sv.IsActive() &&
						Host.IsSinglePlayerGame();

		SetupLocalNetworkBackDoor(useBackdoor);
	}



	public void ExtraMouseUpdate(double frameTime) {
		if (!cl.IsActive())
			return;

		if (!Host.ShouldRun())
			return;

		clientDLL.ExtraMouseSample(frameTime, !cl.Paused);
	}

	public void Init() {
		cl.Clear();

		clientDLL = services.GetRequiredService<IBaseClientDLL>();
		engineClient = services.GetRequiredService<IEngineClient>();
	}

	public void ReadPackets(bool finalTick) {
		//if(!Host.ShouldRun) return;

		cl.OldTickCount = cl.GetServerTickCount();
		if (!cl.IsPaused()) {
			cl.SetClientTickCount(cl.GetClientTickCount() + 1);

			if (!ClockDriftMgr.Enabled)
				cl.SetServerTickCount(cl.GetClientTickCount());

			clientGlobalVariables.TickCount = cl.GetClientTickCount();
			clientGlobalVariables.CurTime = cl.GetTime();
		}
		clientGlobalVariables.FrameTime = cl.GetFrameTime();

		Net.ProcessSocket(NetSocketType.Client, cl);
	}

	public void RunPrediction(PredictionReason reason) {
		if (!cl.IsActive())
			return;

		if (cl.DeltaTick < 0)
			return;

		bool valid = cl.DeltaTick > 0;

		Predict(cl.DeltaTick, valid, cl.LastCommandAck, cl.LastOutgoingCommand + cl.ChokedCommands);
	}

	private void Predict(int worldStartState, bool validFrame, int startCommand, int stopCommand) {
		g_ClientSidePrediction.Update(worldStartState, validFrame, startCommand, stopCommand);
	}

	public void SendMove() {
		byte[] data = ArrayPool<byte>.Shared.Rent(MAX_CMD_BUFFER);
		{
			int nextcommandnr = cl.LastOutgoingCommand + cl.ChokedCommands + 1;
			CLC_Move move = new();

			move.DataOut.StartWriting(data, data.Length, 0);
			int cl_cmdbackup = 2;
			move.BackupCommands = Math.Clamp(cl_cmdbackup, 0, MAX_BACKUP_COMMANDS);
			move.NewCommands = 1 + cl.ChokedCommands;
			move.NewCommands = Math.Clamp(move.NewCommands, 0, MAX_NEW_COMMANDS);
			int numcmds = move.NewCommands + move.BackupCommands;
			int from = -1;
			bool ok = true;
			for (int to = nextcommandnr - numcmds + 1; to <= nextcommandnr; to++) {
				bool isnewcmd = to >= (nextcommandnr - move.NewCommands + 1);
				ok = ok && clientDLL.WriteUsercmdDeltaToBuffer(move.DataOut, from, to, isnewcmd);
				from = to;
			}

			if (ok)
				cl.NetChannel?.SendNetMsg(move);
		}
		ArrayPool<byte>.Shared.Return(data, true);
	}

	ConVarRef cl_cmdrate;
	public void Move(double accumulatedExtraSamples, bool finalTick) {
		if (!cl.IsConnected())
			return;

		if (!Host.ShouldRun())
			return;

		bool sendPacket = true;

		if ((!cl.NetChannel!.IsLoopback() || Host.host_limitlocal.GetInt() != 0) &&
			(Net.Time < cl.NextCmdTime || !cl.NetChannel!.CanPacket() || !finalTick))
			sendPacket = false;

		if (cl.IsActive()) {
			int nextcmdnr = cl.LastOutgoingCommand + cl.ChokedCommands + 1;
			clientDLL.CreateMove(nextcmdnr, host_state.IntervalPerTick - accumulatedExtraSamples, !cl.IsPaused());

			if (sendPacket)
				SendMove();
			else {
				cl.NetChannel?.SetChoked();
				cl.ChokedCommands++;
			}
		}

		if (!sendPacket)
			return;

		bool hasProblem = cl.NetChannel!.IsTimingOut() && cl.IsActive();
		if (hasProblem) {
			Con_NPrint_s np = default;
			np.TimeToLive = 1.0;
			np.Index = 2;
			np.FixedWidthFont = false;
			np.Color[0] = 1.0f;
			np.Color[1] = 0.2f;
			np.Color[2] = 0.2f;

			TimeUnit_t timeout = cl.NetChannel.GetTimeoutSeconds();
			TimeUnit_t remainingTime = timeout - cl.NetChannel.GetTimeSinceLastReceived();
			Con.NXPrintF(in np, "WARNING:  Connection Problem");
			np.Index = 3;
			Con.NXPrintF(in np, $"Auto-disconnect in {remainingTime:F2} seconds");
		}

		if (cl.IsActive()) {
			NET_Tick mymsg = new NET_Tick(cl.DeltaTick, (float)Host.FrameTimeUnbounded, (float)Host.FrameTimeStandardDeviation);
			cl.NetChannel?.SendNetMsg(mymsg);
		}

		cl.LastOutgoingCommand = cl.NetChannel!.SendDatagram(null);
		cl.ChokedCommands = 0;

		if (cl.IsActive()) {
			cl_cmdrate.Init("cl_cmdrate");
			float commandInterval = 1.0f / cl_cmdrate.GetFloat();
			float maxDelta = Math.Min((float)host_state.IntervalPerTick, commandInterval);
			float delta = Math.Clamp((float)(Net.Time - cl.NextCmdTime), 0, maxDelta);
			cl.NextCmdTime = Net.Time + commandInterval - delta;
		}
		else {
			cl.NextCmdTime = Net.Time + (1f / 5f);
		}
	}

	public void FullyConnected() {
#if !SWDS
		EngineVGui?.UpdateProgressBar(LevelLoadingProgress.FullyConnected);
#endif
		StaticPropMgr().LevelInitClient();

		// Flush dynamic models
		// modelloader.FlushDynamicModels();

		// Purge unused models
		modelloader.PurgeUnusedModels();

		// Shutdown preload data
		// MDLCache.ShutdownPreloadData();

		// Pending pure file reloads

		// Level init post entity
		clientDLL.LevelInitPostEntity();

#if !SWDS
		IShaderSystem shaderSystem = Singleton<IShaderSystem>();
		shaderSystem.QueueAllShaderCombos();
		int lastShaderPercent = -1;
		shaderSystem.WaitForPendingCompiles((done, total) => {
			int percent = done * 100 / total;
			if (percent == lastShaderPercent)
				return;
			lastShaderPercent = percent;
			EngineVGui?.UpdateCustomProgressBar((float)done / total, "Compiling shaders..."); // TODO: LOCALIZE THIS STRING #214
		});
#endif

		// Start notifying dependencies
		uint ip = cl.NetChannel!.GetRemoteAddress()!.GetIPHostByteOrder();
		short port = cl.NetChannel.GetRemoteAddress()!.GetPort();

		if (port == 0) {
			ip = Net.LocalAdr.GetIPHostByteOrder();
			port = Net.LocalAdr.GetPort();
		}

		int queryPort = GetServerQueryPort();

#if !SWDS
		EngineVGui!.NotifyOfServerConnect(Common.Gamedir, (int)ip, port, queryPort);
		EngineVGui!.UpdateProgressBar(LevelLoadingProgress.ReadyToPlay);
#endif
#if GMOD_DLL && !SWDS
		launcherMgr.FlashWindow(true);
#endif
		// MDL cache end map load

		if (Host.developer.GetInt() > 0)
			ConDMsg($"Signon traffic \"{cl.NetChannel.GetName()}\":  incoming {cl.NetChannel.GetTotalData(NetFlow.FLOW_INCOMING)}, outgoing {cl.NetChannel.GetTotalData(NetFlow.FLOW_OUTGOING)}\n");

		Scr.EndLoadingPlaque();
		// EndLoadingUpdates();

		// commandline checks todo

		if (!engineClient.IsLevelMainMenuBackground())
			Host.AllowQueuedMaterialSystem(true);
	}

	public void Connect(ReadOnlySpan<char> address, string sourceTag) {
		if (strcmp(address[..Math.Min(address.Length, 9)], "localhost") != 0) {
			Host.Disconnect(false);
			Net.SetMultiplayer(true);
#if !SWDS
			EngineVGui?.EnabledProgressBarForNextLoad();
#endif
			Scr.BeginLoadingPlaque();
#if !SWDS
			EngineVGui?.UpdateProgressBar(LevelLoadingProgress.BeginConnect);
#endif
		}
		else {
			cl.Disconnect("Connecting to local host", false);
		}

		cl.Connect(address, sourceTag);
	}

	int GetServerQueryPort() {
		return 0; // TODO
	}

	[ConCommand]
	public void connect(in TokenizedCommand cmd) {
		var splits = cmd.ArgS().Split(' ');
		if (splits.Length == 1) {
			Connect(splits[0], "");
		}
		else if (splits.Length == 2) {
			Connect(splits[0], splits[1]);
		}
		else {
			Dbg.ConMsg("Usage:  connect <server>\n");
		}
	}

	[ConCommand(helpText: "Retry connection to last server.")]
	void retry() {
		Retry();
	}

	public void Retry() {
		if (cl == null) return;

		if (cl.RetryAddress == null) {
			Dbg.ConMsg("Can't retry, no previous connection\n");
			return;
		}

		bool canAddExecutionMarkers = Cbuf.HasRoomForExecutionMarkers(2);
		Dbg.ConMsg($"Commencing connection retry to {cl.RetryAddress}\n");

		ReadOnlySpan<char> command = $"connect {cl.RetryAddress} {cl.RetryChallenge}\n";
		if (cl.RestrictServerCommands && canAddExecutionMarkers)
			Cbuf.AddTextWithMarkers(CmdExecutionMarker.DisableServerCanExecute, command, CmdExecutionMarker.EnableServerCanExecute);
		else
			Cbuf.AddText(command);
	}

	internal void ProcessVoiceData() {
		Voice.Idle(Host.FrameTime);
		SendVoicePacket(false);
	}

	internal void TakeSnapshotAndSwap() {
		Shader.SwapBuffers();
	}

	public void DumpStringTables() => cl.StringTableContainer?.Dump();

	internal bool CheckCRCs(ReadOnlySpan<char> levelFileName) {
		return true;
	}

	readonly byte[] voiceData = new byte[2048];

	public void SendVoicePacket(bool final) {
		if (!Voice.IsRecording())
			return;

		CLC_VoiceData voiceMsg = new();

		voiceMsg.DataOut.StartWriting(voiceData, voiceData.Length);

		voiceMsg.Length = Voice.GetCompressedData(voiceData.AsSpan(), final) * 8;

		if (voiceMsg.Length == 0)
			return;

		voiceMsg.DataOut.Seek(voiceMsg.Length);    // set correct writing position

		if (cl.IsActive())
			cl.NetChannel!.SendNetMsg(voiceMsg);
	}

	internal void RegisterResources() {
		host_state.SetWorldModel(cl.GetModel(1));
		if (host_state.WorldModel == null)
			Host.Error("CL.RegisterResources: host_state.WorldModel/cl.GetModel(1) == NULL\n");
	}

	public ref PrecacheUserData GetPrecacheUserData(INetworkStringTable table, int index) {
		Span<byte> d = table.GetStringUserData(index);
		ref PrecacheUserData data = ref MemoryMarshal.Cast<byte, PrecacheUserData>(d)[0];
		if (!Unsafe.IsNullRef(ref data) && d.Length != Unsafe.SizeOf<PrecacheUserData>())
			Error($"CL.GetPrecacheUserData({table.GetTableId()}, {index}) - length ({d.Length}) invalid.");

		return ref data;
	}

	public int PropsDecoded;


	private void CallPostDataUpdates(EntityReadInfo u) {
		for (int i = 0; i < u.NumPostDataUpdateCalls; i++) {
			ref PostDataUpdateCall call = ref u.PostDataUpdateCalls[i];

			IClientNetworkable? ent = EntityList.GetClientNetworkable(call.Ent);
			ErrorIfNot(ent != null, $"CL_CallPostDataUpdates: missing ent {call.Ent}");

			ent.PostDataUpdate(call.UpdateType);
		}
	}

	private void MarkEntitiesOutOfPVS(ref MaxEdictsBitSet pvsFlags) {
		int highest_index = EntityList.GetHighestEntityIndex();
		for (int i = 0; i <= highest_index; i++) {
			IClientNetworkable? ent = EntityList.GetClientNetworkable(i);
			if (ent == null)
				continue;

			bool curstate = !ent.IsDormant();
			bool newstate = pvsFlags.Get(i) != 0 ? true : false;

			if (!curstate && newstate)
				ent.NotifyShouldTransmit(ShouldTransmiteState.Start);
			else if (curstate && !newstate) {
				ent.NotifyShouldTransmit(ShouldTransmiteState.End);
				RecordLeavePVS(i);
			}
		}
	}

	public void DeleteDLLEntity(int entIndex, ReadOnlySpan<char> reason, bool onRecreatingAllEntities = false) {
		IClientNetworkable? net = EntityList.GetClientNetworkable(entIndex);

		if (net != null) {
			ClientClass clientClass = net.GetClientClass();

			RecordDeleteEntity(entIndex, clientClass);
			if (onRecreatingAllEntities)
				net.SetDestroyedOnRecreateEntities();

			net.Release();
		}
	}

	private void FlushEntityPacket(ClientFrame newFrame, ReadOnlySpan<char> v) {
		ConMsg(v);

		Span<char> str = stackalloc char[2048];
		sprintf(str, "WARNING:  CL_FlushEntityPacket, %s").S(v);

		Con_NPrint_s np = new() {
			FixedWidthFont = false,
			TimeToLive = 1.0,
			Index = 1
		};

		np.Color[0] = 1.0f;
		np.Color[1] = 0.2f;
		np.Color[2] = 0.0f;
		Con.NXPrintF(in np, str);

		cl.FrameManager.FreeFrame(newFrame);
	}

	internal void PreprocessEntities() {
		bool isUsingMultiplayerNetworking = Net.IsMultiplayer();
		bool lastOutgoingCommandEqualsLastAcknowledgedCommand = cl.LastOutgoingCommand == cl.CommandAck;

		if (isUsingMultiplayerNetworking || lastOutgoingCommandEqualsLastAcknowledgedCommand)
			RunPrediction(PredictionReason.SimulationResultsArrivingOnSendFrame);

		int number_of_commands_executed = cl.CommandAck - cl.LastCommandAck;
		ClientSidePrediction.PreEntityPacketReceived(number_of_commands_executed, cl.CurrentSequence);
	}

	internal bool DetermineUpdateType(EntityReadInfo u) {
		if (!u.IsEntity || (u.NewEntity > u.OldEntity)) {
			if (u.From == null || (u.OldEntity > u.From.LastEntity)) {
				Assert(!u.IsEntity);
				u.UpdateType = UpdateType.Finished;
				return false;
			}

			u.UpdateType = UpdateType.PreserveEnt;
		}
		else {
			if ((u.UpdateFlags & DeltaEncodingFlags.EnterPVS) != 0)
				u.UpdateType = UpdateType.EnterPVS;
			else if ((u.UpdateFlags & DeltaEncodingFlags.LeavePVS) != 0)
				u.UpdateType = UpdateType.LeavePVS;
			else
				u.UpdateType = UpdateType.DeltaEnt;
		}

		return true;
	}

	public void ParseDeltaHeader(EntityReadInfo u) {
		u.UpdateFlags = DeltaEncodingFlags.Zero;

		u.NewEntity = (int)(u.HeaderBase + 1 + u.Buf!.ReadUBitVar());
		u.HeaderBase = u.NewEntity;

		if (u.Buf!.ReadOneBit() == 0) {
			if (u.Buf!.ReadOneBit() != 0)
				u.UpdateFlags |= DeltaEncodingFlags.EnterPVS;
		}
		else {
			u.UpdateFlags |= DeltaEncodingFlags.LeavePVS;

			if (u.Buf!.ReadOneBit() != 0)
				u.UpdateFlags |= DeltaEncodingFlags.Delete;
		}
	}

	internal void CopyNewEntity(EntityReadInfo u, int iClass, int iSerialNum) {
		if (u.NewEntity < 0 || u.NewEntity >= MAX_EDICTS) {
			Host.Error("CL.CopyNewEntity: u.m_nNewEntity < 0 || m_nNewEntity >= MAX_EDICTS");
			return;
		}

		IClientNetworkable? ent = EntityList.GetClientNetworkable(u.NewEntity);

		if (iClass >= cl.NumServerClasses) {
			Host.Error($"CL.CopyNewEntity: invalid class index ({iClass}, max {cl.NumServerClasses}).\n");
			return;
		}

		ClientClass? pClass = cl.ServerClasses![iClass]?.ClientClass;
		bool bNew = false;
		if (ent != null) {
			if (ent.GetIClientUnknown()!.GetRefEHandle()!.GetSerialNumber() != iSerialNum) {
				DeleteDLLEntity(u.NewEntity, "CopyNewEntity");
				ent = null;
			}
		}

		if (ent == null) {
			ent = CreateDLLEntity(u.NewEntity, iClass, iSerialNum);
			if (ent == null) {
				ReadOnlySpan<char> networkName = cl.ServerClasses[iClass]?.ClientClass?.NetworkName ?? "";
				clientDLL.ErrorCreatingEntity(u.NewEntity, iClass, iSerialNum);
				Host.Error($"CL.CopyNewEntity: Error creating entity {networkName}({u.NewEntity})\n");
				return;
			}

			bNew = true;
		}

		int start_bit = u.Buf!.BitsRead;

		DataUpdateType updateType = bNew ? DataUpdateType.Created : DataUpdateType.DataTableChanged;
		ent.PreDataUpdate(updateType);

		byte[]? fromData;
		int fromBits;

		PackedEntity? baseline = u.AsDelta ? cl.GetEntityBaseline(u.Baseline, u.NewEntity) : null;
		if (baseline != null && baseline.ClientClass == pClass) {
			Assert(!baseline.IsCompressed());
			fromData = baseline.GetData();
			fromBits = baseline.GetNumBits();
		}
		else {
			ErrorIfNot(cl.GetClassBaseline(iClass, out fromData, out fromBits), $"CL.CopyNewEntity: GetClassBaseline({iClass}) failed.");
			fromBits *= 8;
		}

		bf_read fromBuf = new("CL.CopyNewEntity->fromBuf", fromData, Bits2Bytes(fromBits), fromBits);

		RecvTable? recvTable = GetEntRecvTable(u.NewEntity);

		if (recvTable == null)
			Host.Error($"CL.CopyNewEntity: invalid recv table for ent {u.NewEntity}.\n");

		if (u.UpdateBaselines) {
			byte[] packedData = ArrayPool<byte>.Shared.Rent(MAX_PACKEDENTITY_DATA);
			bf_write writeBuf = new(packedData, packedData.Length);

			RecvTable.MergeDeltas(recvTable, fromBuf, u.Buf, writeBuf, -1, null, true);

			cl.SetEntityBaseline((u.Baseline == 0) ? 1 : 0, pClass, u.NewEntity, packedData, writeBuf.BytesWritten);

			fromBuf.StartReading(packedData, writeBuf.BytesWritten);

			RecvTable.Decode(recvTable, ent.GetDataTableBasePtr(), fromBuf, u.NewEntity, false);
			ArrayPool<byte>.Shared.Return(packedData, true);
		}
		else {
			RecvTable.Decode(recvTable, ent.GetDataTableBasePtr(), fromBuf, u.NewEntity, false);
			RecvTable.Decode(recvTable, ent.GetDataTableBasePtr(), u.Buf, u.NewEntity, true);
		}

		// A decode that overflows the buffer means this entity's datatable consumed
		// the wrong number of bits and the rest of the packet is now misaligned. This
		// is the point where a stream desync first becomes detectable.
		if (u.Buf.Overflowed)
			Warning($"CL.CopyNewEntity: buffer overflow decoding new ent {u.NewEntity} class {iClass} '{pClass?.NetworkName}' - entity stream desynced\n");

		AddPostDataUpdateCall(u, u.NewEntity, updateType);

		Assert(u.To!.LastEntity <= u.NewEntity);
		u.To!.LastEntity = u.NewEntity;
		u.To!.TransmitEntity.Set(u.NewEntity);

		int bit_count = u.Buf.BitsRead - start_bit;

		// Per-entity bit accounting. Enable cl_entitydecode_report to trace exactly
		// which datatable consumed how many bits; the last entity logged before a
		// "missing client entity"/overflow error is the one whose decode desynced.
		if (cl_entitydecode_report.GetBool())
			DevMsg($"cl decode: ent {u.NewEntity,4} class {iClass,3} '{pClass?.NetworkName}' consumed {bit_count} bits (buf pos {u.Buf.BitsRead}, {u.Buf.BitsLeft} left)\n");

		if (cl_entityreport.GetBool())
			RecordEntityBits(u.NewEntity, bit_count);

		if (IsPlayerIndex(u.NewEntity)) {
			if (u.NewEntity == cl.PlayerSlot + 1)
				u.LocalPlayerBits += bit_count;
			else
				u.OtherPlayerBits += bit_count;
		}
	}

	private RecvTable? GetEntRecvTable(int entityNum) {
		IClientNetworkable? networkable = EntityList.GetClientNetworkable(entityNum);
		return networkable?.GetClientClass().RecvTable;
	}

	private IClientNetworkable? CreateDLLEntity(int iEnt, int iClass, int iSerialNum) {
		ClientClass? clientClass;
		if ((clientClass = cl.ServerClasses![iClass]?.ClientClass) != null) {
			RecordAddEntity(iEnt);
			if (!cl.IsActive())
				Common.TimestampedLog($"cl:  create({iEnt}, {iClass}, {iSerialNum}) '{clientClass.NetworkName}'\n");

			return clientClass.CreateFn(iEnt, iSerialNum);
		}

		Assert(false);
		return null;
	}

	public bool IsPlayerIndex(int index) {
		return (index >= 1 && index <= cl.MaxClients);
	}

	private void AddPostDataUpdateCall(EntityReadInfo u, int entIdx, DataUpdateType updateType) {
		ErrorIfNot(u.NumPostDataUpdateCalls < MAX_EDICTS, "CL_AddPostDataUpdateCall: overflowed u.m_PostDataUpdateCalls");

		u.PostDataUpdateCalls[u.NumPostDataUpdateCalls].Ent = entIdx;
		u.PostDataUpdateCalls[u.NumPostDataUpdateCalls].UpdateType = updateType;
		++u.NumPostDataUpdateCalls;
	}

	internal void CopyExistingEntity(EntityReadInfo u) {
		int start_bit = u.Buf!.BitsRead;

		IClientNetworkable? ent = EntityList.GetClientNetworkable(u.NewEntity);
		if (ent == null) {
			Host.Error($"CL.CopyExistingEntity: missing client entity {u.NewEntity}.\n");
			return;
		}

		ent.PreDataUpdate(DataUpdateType.DataTableChanged);
		RecvTable? recvTable = GetEntRecvTable(u.NewEntity);

		if (recvTable == null) {
			Host.Error($"CL.CopyExistingEntity: invalid recv table for ent {u.NewEntity}.\n");
			return;
		}

		RecvTable.Decode(recvTable, ent.GetDataTableBasePtr(), u.Buf, u.NewEntity);

		AddPostDataUpdateCall(u, u.NewEntity, DataUpdateType.DataTableChanged);

		u.To!.LastEntity = u.NewEntity;
		u.To.TransmitEntity.Set(u.NewEntity);

		int bit_count = u.Buf.BitsRead - start_bit;

		if (cl_entityreport.GetBool())
			RecordEntityBits(u.NewEntity, bit_count);

		if (IsPlayerIndex(u.NewEntity)) {
			if (u.NewEntity == cl.PlayerSlot + 1)
				u.LocalPlayerBits += bit_count;
			else
				u.OtherPlayerBits += bit_count;
		}
	}

	internal void PreserveExistingEntity(int oldEntity) {
		IClientNetworkable? pEnt = EntityList.GetClientNetworkable(oldEntity);
		if (pEnt == null) {
			Host.Error($"CL.PreserveExistingEntity: missing client entity {oldEntity}.\n");
			return;
		}

		pEnt.OnDataUnchangedInPVS();
	}


	internal void Shutdown() {

	}


	// TODO: This shouldn't be a list
	readonly List<SoundInfo> SoundMessages = [];

	public void AddSound(in SoundInfo soundInfo) {
		SoundMessages.Add(soundInfo);
	}

	public void DispatchSounds() {
		Span<SoundInfo> sounds = SoundMessages.AsSpan();
		for (int i = 0; i < sounds.Length; i++) {
			ref readonly SoundInfo msg = ref sounds[i];
			DispatchSound(in msg);
		}

		SoundMessages.Clear();
	}

	ConVar? snd_show;

	private void DispatchSound(in SoundInfo sound) {
		int nSoundNum = sound.SoundNum;

		SfxTable? pSfx = null;
		Span<char> name = stackalloc char[MAX_PATH];

		name[0] = '\0';
		if (sound.IsSentence) {
			// make dummy sfx for sentences
			//  ReadOnlySequence<char> sentenceName = VOX_SentenceNameFromIndex(sound.nSoundNum);
			//  if (!pSentenceName) {
			//  	pSentenceName = "";
			//  }
			//  
			//  V_snprintf(name, sizeof(name), "%c%s", CHAR_SENTENCE, pSentenceName);
			//  pSfx = S_DummySfx(name);
			Msg("Sentences are not yet implemented\n");
			return;
		}
		else {
			cl.GetSoundName(sound.SoundNum).ClampedCopyTo(name);
			pSfx = cl.GetSound(nSoundNum);
		}

		snd_show ??= cvar.FindVar("snd_show");
		if (snd_show != null && snd_show.GetInt() >= 2) {
			DevMsg($"{Host.FrameCount} (seq {sound.SequenceNumber}) {((ReadOnlySpan<char>)name).SliceNullTerminatedString()} : src {sound.EntityIndex} : ch {(int)sound.Channel} : {(int)sound.Soundlevel} dB : vol {sound.Volume:F2} : time {cl.GetTime():F3} ({sound.Delay:F4} delay) @{sound.Origin.X:F1} {sound.Origin.Y:F1} {sound.Origin.Z:F1}\n");
		}

		StartSoundParams parms = new();
		parms.StaticSound = (sound.Channel == SoundEntityChannel.Static) ? true : false;
		parms.SoundSource = sound.EntityIndex;
		parms.EntChannel = parms.StaticSound ? SoundEntityChannel.Static : sound.Channel;
		parms.Sfx = pSfx;
		parms.Origin = sound.Origin;
		parms.Volume = sound.Volume;
		parms.SoundLevel = sound.Soundlevel;
		parms.Flags = sound.Flags;
		parms.Pitch = sound.Pitch;
		parms.SpecialDSP = sound.SpecialDSP;
		parms.FromServer = true;
		parms.Delay = sound.Delay;
		if ((sound.Flags & SoundFlags.Delay) != 0) {
			if (sound.Delay > -0.100f) {
				TimeUnit_t soundtime = cl.LastServerTickTime + sound.Delay;
				// this adjusts for host_thread_mode or any other cases where we're running more than one
				// tick at a time, but we get network updates on the first tick
				soundtime -= ((clientGlobalVariables.SimTicksThisFrame - 1) * host_state.IntervalPerTick);
				// this sound was networked over from the server, use server clock
				parms.Delay = Sound.ComputeDelayForSoundtime(soundtime, ClockSyncIndex.Server);
				if (parms.Delay <= 0)
					parms.Delay = 1e-6f;
			}
			else
				parms.Delay = sound.Delay;
		}
		parms.SpeakerEntity = sound.SpeakerEntity;

		// ClientDLL.ClientAdjustStartSoundparms(parms);

		Sound.StartSound(in parms);
	}


	internal void ClearState() {
		ResetEntityBits();

		R.UnloadSkys();
		g_ClientDLL?.LevelShutdown();

		R.LevelShutdown();

		if (LocalNetworkBackdoor != null)
			LocalNetworkBackdoor.ClearState();

		Host.FreeStateAndWorld(false);
		Host.FreeToLowMark(false);

		cl.Clear();
	}

	public static readonly DLight[] DLights = new DLight[MAX_DLIGHTS].InstantiateArray();
	public static readonly DLight[] ELights = new DLight[MAX_ELIGHTS].InstantiateArray();
	public static bool ActiveDlights;
	public static bool ActiveElights;

	static int AllocLightFromArray(DLight[] lights, int lightCount, int key) {
		int i;

		if (key != 0) {
			for (i = 0; i < lightCount; i++) {
				if (lights[i].Key == key)
					return i;
			}
		}

		for (i = 0; i < lightCount; i++) {
			if (lights[i].Die < cl.GetTime())
				return i;
		}

		return 0;
	}

	public static DLight AllocDlight(int key) {
		int i = AllocLightFromArray(DLights, MAX_DLIGHTS, key);
		DLight dl = DLights[i];
		Render.MarkDLightNotVisible(i);
		dl.Clear();
		dl.Key = key;
		Render.DLightChanged |= 1 << i;
		Render.DLightActive |= 1 << i;
		ActiveDlights = true;
		return dl;
	}

	public static DLight AllocElight(int key) {
		int i = AllocLightFromArray(ELights, MAX_ELIGHTS, key);
		DLight el = ELights[i];
		el.Clear();
		el.Key = key;
		ActiveElights = true;
		return el;
	}

	public static void DecayLights() {
		double time = cl.GetFrameTime();
		if (time <= 0.0f)
			return;

		ActiveDlights = false;
		ActiveElights = false;

		Render.DLightChanged = 0;
		Render.DLightActive = 0;

		for (int i = 0; i < MAX_DLIGHTS; i++) {
			DLight dl = DLights[i];

			if (!dl.IsRadiusGreaterThanZero()) {
				Render.MarkDLightNotVisible(i);
				continue;
			}

			if (dl.Die < cl.GetTime()) {
				Render.DLightChanged |= 1 << i;
				dl.Radius = 0;
			}
			else if (dl.Decay != 0) {
				Render.DLightChanged |= 1 << i;

				dl.Radius -= (float)time * dl.Decay;
				if (dl.Radius < 0)
					dl.Radius = 0;
			}

			if (dl.IsRadiusGreaterThanZero()) {
				ActiveDlights = true;
				Render.DLightActive |= 1 << i;
			}
			else
				Render.MarkDLightNotVisible(i);
		}

		for (int i = 0; i < MAX_ELIGHTS; i++) {
			DLight el = ELights[i];

			if (!el.IsRadiusGreaterThanZero())
				continue;

			if (el.Die < cl.GetTime()) {
				el.Radius = 0;
				continue;
			}

			el.Radius -= (float)time * el.Decay;
			if (el.Radius < 0)
				el.Radius = 0;

			if (el.IsRadiusGreaterThanZero())
				ActiveElights = true;
		}
	}
}

/// <summary>
/// Loads and shuts down the client DLL
/// </summary>
/// <param name="services"></param>
public class ClientDLL(IServiceProvider services
#if !SWDS
, EngineRecvTable RecvTable
#endif
)
{
	public IPrediction ClientSidePrediction = null!;
	public IClientEntityList EntityList = null!;
	public ICenterPrint CenterPrint = null!;
	public IClientLeafSystemEngine ClientLeafSystem = null!;
	public void Init() {
		if (!g_ClientDLL!.Init())
			Sys.Error("Client.dll Init() in library client failed.");

		ClientSidePrediction = services.GetRequiredService<IPrediction>();
		ClientSidePrediction.Init();

		EntityList = services.GetRequiredService<IClientEntityList>();
		CenterPrint = services.GetRequiredService<ICenterPrint>();
		ClientLeafSystem = services.GetRequiredService<IClientLeafSystemEngine>();

		InitRecvTableMgr();
	}

	private void InitRecvTableMgr() {
		RecvTable?[] recvTables = new RecvTable[MAX_DATATABLES];
		int nRecvTables = 0;
		for (ClientClass? cur = GetAllClasses(); cur != null; cur = cur.Next) {
			ErrorIfNot(nRecvTables < MAX_DATATABLES, "ClientDLL_InitRecvTableMgr: overflowed MAX_DATATABLES");
			recvTables[nRecvTables++] = cur.RecvTable;
		}
#if !SWDS
		RecvTable.Init(recvTables.AsSpan()[..nRecvTables]!); // << ! is acceptable here; anything beyond recvTables is null, anything before it shouldnt be
																												 // (and if something is null before that point something else is already horribly broken)
#endif
	}

	public void Update() {
		if (sv.IsDedicated())
			return;

		if (g_ClientDLL == null)
			return;

		g_ClientDLL.HudUpdate(true);
	}

	public void ProcessInput() => g_ClientDLL?.HudProcessInput(cl.IsConnected());

	public static void FrameStageNotify(ClientFrameStage stage) {
		g_ClientDLL?.FrameStageNotify(stage);
	}

	public ClientClass? GetAllClasses() {
		return g_ClientDLL != null ? g_ClientDLL.GetAllClasses() : ClientClass.Head;
	}

	internal void Shutdown() {
		g_ClientDLL?.Shutdown();
	}
}
