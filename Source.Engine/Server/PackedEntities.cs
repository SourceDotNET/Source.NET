using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.GarrysMod;
using Source.Common.Networking;

using System.Buffers;
using System.Data;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;

namespace Source.Engine.Server;

struct PackWork
{
	public int Id;
	public Edict Edict;
	public FrameSnapshot Snapshot;
	public static void Process(PackWork item) => PackedEntities.PackEntity(item.Id, item.Edict, item.Snapshot.Entities![item.Id].Class!, item.Snapshot);
}

static class PackedEntities
{
	static readonly Host Host = Singleton<Host>();
	static readonly FrameSnapshotManager frameSnapshotManager = Singleton<FrameSnapshotManager>();

	static readonly ConVar sv_debugmanualmode = new("sv_debugmanualmode", "0", FCvar.None, "Make sure entities correctly report whether or not their network data has changed.");
	static readonly ConVar sv_parallel_packentities = new("sv_parallel_packentities", "0", FCvar.None); // SDN: Defaulted to 0 for now ~Callum

	static bool EnsurePrivateData(Edict edict) {
		if (edict.GetUnknown() != null)
			return true;
		else {
			// Host.Error($"SV_EnsurePrivateData: pEdict.pvPrivateData==NULL (ent {edict.EdictIndex}).\n");
			return false;
		}
	}

	public static void EnsureInstanceBaseline(ServerClass serverClass, int edictId, ReadOnlySpan<byte> data, int bytes) {
		Edict ent = sv.Edicts![edictId];
		ErrorIfNot(EnsurePrivateData(ent), $"SV_EnsureInstanceBaseline: EnsurePrivateData failed for ent {edictId}.");

		ServerClass entClass = ent.GetNetworkable()?.GetServerClass();

		if (entClass.InstanceBaselineIndex == INetworkStringTable.INVALID_STRING_INDEX) {
			Span<char> idString = stackalloc char[32];
			sprintf(idString, "%d").D(entClass.ClassID);
			int storeBytes = Math.Max(bytes, 1);
			int temp = sv.InstanceBaselineTable!.AddString(true, idString, storeBytes, data);
			entClass.InstanceBaselineIndex = temp;
			Assert(entClass.InstanceBaselineIndex != INetworkStringTable.INVALID_STRING_INDEX);
			// DevMsg(1, $"EnsureInstanceBaseline: ADDED '{entClass.NetworkName}' classID={entClass.ClassID} tableIdx={temp} svTick={sv.TickCount}\n");
		}
	}

	public static void PackEntity(int edictId, Edict edict, ServerClass serverClass, FrameSnapshot snapshot) {
		Assert(edictId < snapshot.NumEntities);

		int serialNum = snapshot.Entities![edictId].SerialNumber;

		// Check to see if this entity specifies its changes.
		// If so, then try to early out making the fullpack
		bool usedPrev = false;
		if (!edict.HasStateChanged()) {
			// Now this may not work if we didn't previously send a packet;
			// if not, then we gotta compute it
			usedPrev = frameSnapshotManager.UsePreviouslySentPacket(snapshot, edictId, serialNum);
		}

		if (usedPrev && !sv_debugmanualmode.GetBool()) {
			edict.ClearStateChanged();
			return;
		}

		// First encode the entity's data.
		byte[] packedData = ArrayPool<byte>.Shared.Rent(Constants.MAX_PACKEDENTITY_DATA);
		bf_write writeBuf = new(packedData, Constants.MAX_PACKEDENTITY_DATA);

		SendTable sendTable = serverClass.Table;

		SendProxyRecipients[] recip = new SendProxyRecipients[sendTable.Precalc!.GetNumDataTableProxies()];
		for (int i = 0; i < recip.Length; i++)
			recip[i] = new();

		if (!EngineSendTable.Encode(sendTable, edict.GetUnknown(), writeBuf, edictId, recip, false))
			Host.Error($"SV_PackEntity: SendTable_Encode returned false (ent {edictId}).\n");

		EnsureInstanceBaseline(serverClass, edictId, packedData, writeBuf.BytesWritten);

		int flatProps = EngineSendTable.GetNumFlatProps(sendTable);
		IChangeFrameList? changeFrame;
#if GMOD_DLL
		GMODDataTable? gmodDataTable = sendTable.Precalc!.GMODTableProp?.FieldInfo.GetValue<IGMODDataTable>(edict.GetUnknown()!) as GMODDataTable;
#endif

		// If this entity was previously in there, then it should have a valid IChangeFrameList
		// which we can delta against to figure out which properties have changed.
		//
		// If not, then we want to setup a new IChangeFrameList.
		PackedEntity? prevFrame = frameSnapshotManager.GetPreviouslySentPacket(edictId, snapshot.Entities[edictId].SerialNumber);
		if (prevFrame != null) {
			// Calculate a delta.
			Assert(!prevFrame.IsCompressed());

			int[] deltaProps = new int[Constants.MAX_DATATABLE_PROPS];

#if GMOD_DLL
			GMODDataTable.s_CurrentTable = gmodDataTable;
			GMODDataTable.s_TargetTick = (int)snapshot.TickCount;
#endif
			int changes = EngineSendTable.CalcDelta(sendTable, prevFrame.GetData(), prevFrame.GetNumBits(), packedData, writeBuf.BitsWritten, deltaProps, Constants.MAX_DATATABLE_PROPS, edictId);
#if GMOD_DLL
			GMODDataTable.s_CurrentTable = null;
			GMODDataTable.s_TargetTick = 0;
#endif

			// If it's non-manual-mode, but we detect that there are no changes here, then just
			// use the previous snapshot if it's available (as though the entity were manual mode).
			// It would be interesting to hook here and see how many non-manual-mode entities
			// are winding up with no changes.
			if (changes == 0) {
				if (prevFrame.CompareRecipients(recip)) {
					if (frameSnapshotManager.UsePreviouslySentPacket(snapshot, edictId, serialNum)) {
						edict.ClearStateChanged();
						return;
					}
				}
			}
			else {
				if (!edict.HasStateChanged()) {
					for (int iDeltaProp = 0; iDeltaProp < changes; iDeltaProp++) {
						Assert(sendTable.Precalc);
						Assert(deltaProps[iDeltaProp] < sendTable.Precalc!.GetNumProps());

						SendProp prop = sendTable.Precalc.GetProp(deltaProps[iDeltaProp])!;
						// If a field changed, but it changed because it encoded against tickcount,
						//   then it's just like the entity changed the underlying field, not an error, that is.
						if ((prop.GetFlags() & PropFlags.EncodedAgainstTickCount) != 0)
							continue;

						Msg($"Entity {edictId} (class '{edict.GetClassName()}') reported ENTITY_CHANGE_NONE but '{prop.GetName()}' changed.\n");
					}
				}
			}

			if (false /*hltv && hltv.IsActive()*/) {
				// in HLTV or Replay mode every PackedEntity keeps it's own ChangeFrameList
				// we just copy the ChangeFrameList from prev frame and update it
				changeFrame = prevFrame.GetChangeFrameList();
				changeFrame = changeFrame.Copy(); // allocs and copies ChangeFrameList
			}
			else {
				// Ok, now snag the changeframe from the previous frame and update the 'last frame changed'
				// for the properties in the delta.
				changeFrame = prevFrame.SnagChangeFrameList();
			}

			ErrorIfNot(changeFrame != null, "SV_PackEntity: SnagChangeFrameList returned null");
			ErrorIfNot(changeFrame.GetNumProps() == flatProps, $"SV_PackEntity: SnagChangeFrameList mismatched number of props[{flatProps} vs {changeFrame.GetNumProps()}]");

			changeFrame.SetChangeTick(deltaProps, changes, snapshot.TickCount);
		}
		else {
			// Ok, init the change frames for the first time.
			changeFrame = ChangeFrameList.AllocChangeFrameList(flatProps, snapshot.TickCount);
		}

		// Now make a PackedEntity and store the new packed data in there.
		PackedEntity packedEntity = frameSnapshotManager.CreatePackedEntity(snapshot, edictId);
		packedEntity.SetChangeFrameList(changeFrame);
#if GMOD_DLL
		packedEntity.GMODDataTable = gmodDataTable;
#endif
		packedEntity.SetServerAndClientClass(serverClass, null);
		packedEntity.AllocAndCopyPadded(packedData.AsSpan(0, writeBuf.BytesWritten));
		packedEntity.SetRecipients(recip);

		edict.ClearStateChanged();
	}

	static void FillHLTVData(FrameSnapshot snapshot, Edict edict, int validEdict) {
		// throw new NotImplementedException();
	}

	static void FillReplayData(FrameSnapshot snapshot, Edict edict, int validEdict) {
		// throw new NotImplementedException();
	}

	static SendTable GetEntSendTable(Edict edict) {
		throw new NotImplementedException();
	}

	static void NetworkBackDoor(int clientCount, ReadOnlySpan<GameClient> clients, FrameSnapshot snapshot) {
		Assert(clientCount == 1);

		GameClient client = clients[0];   // update variables cl, pInfo, frame for current client
		CheckTransmitInfo info = client.PackInfo;

		for (int iValidEdict = 0; iValidEdict < snapshot.NumValidEntities; iValidEdict++) {
			int index = snapshot.ValidEntities![iValidEdict];
			Edict edict = sv.Edicts![index];

			// this is a bit of a hack to ensure that we get a "preview" of the
			//  packet timstamp that the server will send so that things that
			//  are encoded relative to packet time will be correct
			Assert(edict.NetworkSerialNumber != -1);

			bool shouldTransmit = info.TransmitEdict.Get(index) != 0 ? true : false;

			//CServerDTITimer timer( pSendTable, SERVERDTI_ENCODE );
			// If we're using the fast path for a single-player game, just pass the entity
			// directly over to the client.
			Assert(index < snapshot.NumEntities);
			ServerClass svClass = snapshot.Entities![index].Class!;
			CL.LocalNetworkBackdoor!.EntState(index, edict.NetworkSerialNumber, svClass.ClassID, svClass.Table, edict.GetUnknown()!, edict.HasStateChanged(), shouldTransmit);
			edict.ClearStateChanged();
		}

		// Tell the client about any entities that are now dormant.
		CL.LocalNetworkBackdoor!.ProcessDormantEntities();
		EngineServer.InvalidateSharedEdictChangeInfos();
	}

	static void Normal(int clientCount, ReadOnlySpan<GameClient> clients, FrameSnapshot snapshot) {
		Assert(snapshot.NumValidEntities >= 0 && snapshot.NumValidEntities <= Constants.MAX_EDICTS);

		List<PackWork> workItems = [];

		// check for all active entities, if they are seen by at least on client, if
		// so, bit pack them
		// Console.WriteLine($"Packing entities for snapshot {snapshot.TickCount} with {snapshot.NumValidEntities} valid entities.");
		for (int iValidEdict = 0; iValidEdict < snapshot.NumValidEntities; ++iValidEdict) {
			int index = snapshot.ValidEntities![iValidEdict];

			Assert(index < snapshot.NumEntities);

			Edict edict = sv.Edicts![index];

			// if HLTV is running save PVS info for each entity
			FillHLTVData(snapshot, edict, iValidEdict);

			// if Replay is running save PVS info for each entity
			FillReplayData(snapshot, edict, iValidEdict);

			// Check to see if the entity changed this frame...
			// ServerDTI_RegisterNetworkStateChange( sendTable, ent.m_bStateChanged );

			for (int iClient = 0; iClient < clientCount; ++iClient) {
				// entities is seen by at least this client, pack it and exit loop
				GameClient client = clients[iClient]; // update variables cl, pInfo, frame for current client
				ClientFrame? frame = client.CurrentFrame;

				if (frame!.TransmitEntity.Get(index) != 0) {
					PackWork w;
					w.Id = index;
					w.Edict = edict;
					w.Snapshot = snapshot;

					workItems.Add(w);
					break;
				}
			}
		}

		if (sv_parallel_packentities.GetBool()) {
			// 	ParallelProcess("PackWork_t::Process", workItems.Base(), workItems.Count(), &PackWork_t::Process);
			Debugger.Break();
		}
		else {
			int c = workItems.Count;
			for (int i = 0; i < c; ++i) {
				PackWork w = workItems[i];
				PackEntity(w.Id, w.Edict, w.Snapshot.Entities![w.Id].Class!, w.Snapshot);
			}
		}

		// InvalidateSharedEdictChangeInfos(); todo
	}

	public static void ComputeClientPacks(int clientCount, ReadOnlySpan<GameClient> clients, FrameSnapshot snapshot) {
		for (int i = 0; i < clientCount; i++) {
			CheckTransmitInfo info = clients[i].PackInfo;

			clients[i].SetupPackInfo(snapshot);
			SV.ServerGameEnts!.CheckTransmit(info, snapshot.ValidEntities!, snapshot.NumValidEntities);
			clients[i].CurrentFrame!.TransmitEntity = info.TransmitEdict;
			clients[i].CurrentFrame!.TransmitAlways = info.TransmitAlways;
			clients[i].SetupPrevPackInfo();
		}

		if (CL.LocalNetworkBackdoor != null) {
			sv.StringTables!.DirectUpdate(clients[0].GetMaxAckTickCount());
			CL.LocalNetworkBackdoor.StartEntityStateUpdate();

#if !SWDS
			int saveClientTicks = cl.GetClientTickCount();
			int saveServerTicks = cl.GetServerTickCount();
			bool bSaveSimulation = cl.InSimulation;
			TimeUnit_t flSaveLastServerTickTime = cl.LastServerTickTime;

			cl.InSimulation = true;
			cl.SetClientTickCount((int)sv.TickCount);
			cl.SetServerTickCount((int)sv.TickCount);

			cl.LastServerTickTime = sv.TickCount * host_state.IntervalPerTick;
			clientGlobalVariables.TickCount = cl.GetClientTickCount();
			clientGlobalVariables.CurTime = cl.GetTime();
#endif

			NetworkBackDoor(clientCount, clients, snapshot);

			CL.LocalNetworkBackdoor.EndEntityStateUpdate();

#if !SWDS
			cl.SetClientTickCount(saveClientTicks);
			cl.SetServerTickCount(saveServerTicks);
			cl.InSimulation = bSaveSimulation;
			cl.LastServerTickTime = flSaveLastServerTickTime;

			clientGlobalVariables.TickCount = cl.GetClientTickCount();
			clientGlobalVariables.CurTime = cl.GetTime();
#endif

			// PrintPartialChangeEntsList();
		}
		else
			Normal(clientCount, clients, snapshot);
	}

	static void MaybeWriteSendTable(SendTable table, bf_write buffer, bool needDecoder) {
		if (table.GetWriteFlag())
			return;

		table.SetWriteFlag(true);

		SVC_SendTable sendTbl = new();

		byte[] buf = new byte[4096];
		sendTbl.DataOut.StartWriting(buf, buf.Length);

		SendTable.WriteInfos(table, sendTbl.DataOut);

		sendTbl.NeedsDecoder = needDecoder;

		sendTbl.WriteToBuffer(buffer);
	}

	static void MaybeWriteSendTable_R(SendTable table, bf_write buffer) {
		MaybeWriteSendTable(table, buffer, false);

		foreach (SendProp prop in table.Props ?? []) {
			if (prop.Type == SendPropType.DataTable)
				MaybeWriteSendTable_R(prop.GetDataTable()!, buffer);
		}
	}

	public static void WriteSendTables(ServerClass serverClasses, bf_write buffer) {
		ServerClass? cur;

		DtCommonEng.ClearWriteFlags(serverClasses);

		for (cur = serverClasses; cur != null; cur = serverClasses.Next)
			MaybeWriteSendTable(cur.Table, buffer, true);

		for (cur = serverClasses; cur != null; cur = serverClasses.Next)
			MaybeWriteSendTable_R(cur.Table, buffer);
	}

	static void ComputeClassInfosCRC(Crc32 crc) {
		throw new NotImplementedException();
	}

	static void AssignClassIds() {
		throw new NotImplementedException();
	}

	public static void WriteClassInfos(ServerClass clases, bf_write buffer) {
		SVC_ClassInfo msg = new() {
			CreateOnClient = false
		};

		for (ServerClass? serverClass = clases; serverClass != null; serverClass = serverClass.Next) {
			SVC_ClassInfo.Class svclass = new() {
				ClassID = serverClass.ClassID,
				DataTableName = serverClass.Table.GetName().ToString(),
				ClassName = serverClass.NetworkName.ToString()
			};
			msg.Classes.Add(svclass);
		}

		msg.WriteToBuffer(buffer);
	}

	public static ReadOnlySpan<char> GetObjectClassName(int objectId) {
		if (objectId >= 0 && objectId < sv.NumEdicts)
			return sv.Edicts![objectId].GetClassName();
		else
			return "[unknown]";
	}
}
