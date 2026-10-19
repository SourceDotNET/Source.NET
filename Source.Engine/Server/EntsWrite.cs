using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Networking;

namespace Source.Engine.Server;

class EntityWriteInfo : EntityInfo
{
	public bf_write Buffer;
	public int ClientEntity;
	public PackedEntity? OldPack;
	public PackedEntity? NewPack;
	public MaxEdictsBitSet DeletionFlags;
	public FrameSnapshot? FromSnapshot; // = From->GetSnapshot();
	public FrameSnapshot ToSnapshot; // = m_pTo->GetSnapshot();
	public FrameSnapshot Baseline; // the clients baseline
	public BaseServer Server; // the server who writes this entity
	public int FullProps; // number of properties send as full update (Enter PVS)
	public bool CullProps;  // filter props by clients in recipient lists
}

static class EntsWrite
{
	static readonly FrameSnapshotManager framesnapshotmanager = Singleton<FrameSnapshotManager>();
	static readonly Host host = Singleton<Host>();

	public static bool NeedsExplicitDestroy(int entnum, FrameSnapshot? from, FrameSnapshot to) {
		if (entnum >= to.NumEntities || to.Entities![entnum].Class == null) {
			if (entnum >= from!.NumEntities)
				return false;

			if (from.Entities![entnum].Class != null)
				return true;
		}

		return false;
	}

	static void UpdateHeaderDelta(EntityWriteInfo u, int entnum) {
		u.HeaderCount++;
		u.HeaderBase = entnum;
	}

	public static void WriteDeltaHeader(EntityWriteInfo u, int entnum, DeltaEncodingFlags flags) {
		bf_write buffer = u.Buffer;

		int offset = entnum - u.HeaderBase - 1;

		Assert(offset >= 0);

		buffer.WriteUBitVar((uint)offset);

		if ((flags & DeltaEncodingFlags.LeavePVS) != 0) {
			buffer.WriteOneBit(1);
			buffer.WriteOneBit((flags & DeltaEncodingFlags.Delete) != 0 ? 1 : 0);
		}
		else {
			buffer.WriteOneBit(0);
			buffer.WriteOneBit((flags & DeltaEncodingFlags.EnterPVS) != 0 ? 1 : 0);
		}

		UpdateHeaderDelta(u, entnum);
	}

	static int CalcDeltaAndWriteProps(EntityWriteInfo u, byte[]? fromData, int nFromBits, PackedEntity to) {
		throw new NotImplementedException();
	}

	static void WritePropsFromPackedEntity(EntityWriteInfo u, int[] checkProps, int nCheckProps) {
		PackedEntity? to = u.NewPack;
		PackedEntity? from = u.OldPack;
		SendTable? sendTable = to!.ServerClass!.Table;

		byte[]? toData;
		int toBits;

		if (to.IsCompressed())
			throw new NotImplementedException();
		else {
			toData = to.GetData();
			toBits = to.GetNumBits();
		}

		Assert(toData != null);

		int[] pSendProps = new int[Constants.MAX_DATATABLE_PROPS];
		int[] sendProps = checkProps;
		int nSendProps = nCheckProps;
		bf_write bufStart = new();

		if (u.CullProps) {
			sendProps = pSendProps;

			nSendProps = EngineSendTable.CullPropsFromProxies(sendTable, checkProps, nCheckProps, u.ClientEntity - 1, from.GetRecipients(), from.GetNumRecipients(), to.GetRecipients(), to.GetNumRecipients(), pSendProps, pSendProps.Length);
		}
		else
			bufStart = u.Buffer;

		EngineSendTable.WritePropList(
			sendTable!,
			toData,
			toBits,
			u.Buffer,
			to.EntityIndex,
			sendProps,
			nSendProps);

		if (!u.CullProps && u.Server.IsHLTV()) {
			throw new NotImplementedException();
		}
	}

	static bool NeedsExplicitCreate(EntityWriteInfo u) {
		if (!u.AsDelta)
			return false;

		int index = u.NewEntity;
		if (index >= u.FromSnapshot!.NumEntities)
			return true;

		ref readonly FrameSnapshotEntry fromEnt = ref u.FromSnapshot.Entities![index];
		ref readonly FrameSnapshotEntry toEnt = ref u.ToSnapshot.Entities![index];

		return (fromEnt.Class == null) || fromEnt.SerialNumber != toEnt.SerialNumber;
	}

	public static void DetermineUpdateType(EntityWriteInfo u) {
		if (u.NewEntity < u.OldEntity) {
			u.UpdateType = UpdateType.EnterPVS;
			return;
		}

		if (u.NewEntity > u.OldEntity) {
			u.UpdateType = UpdateType.LeavePVS;
			return;
		}

		Assert(u.ToSnapshot.Entities![u.NewEntity].Class != null);

		bool recreate = NeedsExplicitCreate(u);

		if (recreate) {
			u.UpdateType = UpdateType.EnterPVS;
			return;
		}

		Assert(u.OldPack!.ServerClass == u.NewPack!.ServerClass);

		if (u.OldPack == u.NewPack) {
			Assert(u.OldPack != null);
			u.UpdateType = UpdateType.PreserveEnt;
			return;
		}

		int[] checkProps = new int[Constants.MAX_DATATABLE_PROPS];
		int nCheckProps = u.NewPack.GetPropsChangedAfterTick(u.FromSnapshot!.TickCount, checkProps);

		if (nCheckProps == -1) {

			byte[]? oldData;
			int nOldBits;

			if (u.OldPack.IsCompressed())
				throw new NotImplementedException();
			else {
				oldData = u.OldPack.GetData();
				nOldBits = u.OldPack.GetNumBits();
			}

			byte[]? newData;
			int nNewBits;

			if (u.NewPack.IsCompressed()) {
				throw new NotImplementedException();
			}
			else {
				newData = u.NewPack.GetData();
				nNewBits = u.NewPack.GetNumBits();
			}

			nCheckProps = EngineSendTable.CalcDelta(
				u.NewPack.ServerClass!.Table,
				oldData,
				nOldBits,
				newData!,
				nNewBits,
				checkProps,
				checkProps.Length,
				u.NewEntity);
		}

		if (nCheckProps > 0) {
#if GMOD_DLL
			GMODDataTable.s_CurrentTable = u.NewPack.GMODDataTable;
			GMODDataTable.s_ReferenceTick = u.FromSnapshot.TickCount;
			GMODDataTable.s_TargetTick = u.ToSnapshot.TickCount;
#endif
			WriteDeltaHeader(u, u.NewEntity, DeltaEncodingFlags.Zero);

			WritePropsFromPackedEntity(u, checkProps, nCheckProps);

			u.UpdateType = UpdateType.DeltaEnt;
#if GMOD_DLL
			GMODDataTable.s_CurrentTable = null;
			GMODDataTable.s_ReferenceTick = 0;
			GMODDataTable.s_TargetTick = 0;
#endif
		}
		else
			u.UpdateType = UpdateType.PreserveEnt;
	}

	static void WriteEnterPVS(EntityWriteInfo u) {
		WriteDeltaHeader(u, u.NewEntity, DeltaEncodingFlags.EnterPVS);

		Assert(u.NewEntity < u.ToSnapshot.NumEntities);

		ref readonly FrameSnapshotEntry entry = ref u.ToSnapshot.Entities![u.NewEntity];

		ServerClass? entryClass = entry.Class;

		if (entryClass == null)
			host.Error($"SV_CreatePacketEntities: GetEntServerClass failed for ent {u.NewEntity}.\n");

		if (entryClass.ClassID >= u.Server.ServerClasses) {
			ConMsg($"entryClass.ClassID({entryClass.ClassID}) >= {u.Server.ServerClasses}\n");
			Assert(false);
		}

		u.Buffer.WriteUBitLong((uint)entryClass.ClassID, u.Server.ServerClassBits);
		u.Buffer.WriteUBitLong((uint)entry.SerialNumber, Constants.NUM_NETWORKED_EHANDLE_SERIAL_NUMBER_BITS);

		PackedEntity? baseline = u.AsDelta ? framesnapshotmanager.GetPackedEntity(u.Baseline, u.NewEntity) : null;
		byte[]? fromData;
		int nFromBits;

		if (baseline != null && (baseline.ServerClass == u.NewPack!.ServerClass)) {
			Assert(!baseline.IsCompressed());
			fromData = baseline.GetData();
			nFromBits = baseline.GetNumBits();
		}
		else {
			if (!u.Server.GetClassBaseline(entryClass, out fromData))
				Error($"SV_WriteEnterPVS: missing instance baseline for '{entryClass.NetworkName}'.");

			ErrorIfNot(fromData?.Length != 0, $"SV_WriteEnterPVS: missing pFromData for '{entryClass.NetworkName}'.");

			nFromBits = (fromData?.Length ?? 0) * 8;
		}

		if (u.To?.FromBaseline != null)
			u.To.FromBaseline.Set(u.NewEntity);

		byte[]? toData;
		int nToBits;

		if (u.NewPack!.IsCompressed())
			throw new NotImplementedException();
		else {
			toData = u.NewPack.GetData();
			nToBits = u.NewPack.GetNumBits();
		}

#if GMOD_DLL
		GMODDataTable.s_CurrentTable = u.NewPack.GMODDataTable;
		GMODDataTable.s_ReferenceTick = u.Baseline != null ? u.Baseline.TickCount : 0;
		GMODDataTable.s_TargetTick = u.ToSnapshot.TickCount;
#endif
		u.FullProps += WriteAllDeltaProps(entryClass.Table, fromData!, nFromBits, toData!, nToBits, u.NewPack.EntityIndex, u.Buffer);
#if GMOD_DLL
		GMODDataTable.s_CurrentTable = null;
		GMODDataTable.s_ReferenceTick = 0;
		GMODDataTable.s_TargetTick = 0;
#endif

		if (u.NewEntity == u.OldEntity)
			u.NextOldEntity();

		u.NextNewEntity();
	}

	static void WriteLeavePVS(EntityWriteInfo u) {
		DeltaEncodingFlags headerflags = DeltaEncodingFlags.LeavePVS;
		bool deleteentity = false;

		if (u.AsDelta)
			deleteentity = NeedsExplicitDestroy(u.OldEntity, u.FromSnapshot, u.ToSnapshot);

		if (deleteentity) {
			u.DeletionFlags.Set(u.OldEntity);
			headerflags |= DeltaEncodingFlags.Delete;
		}

		WriteDeltaHeader(u, u.OldEntity, headerflags);

		u.NextOldEntity();
	}

	static void WriteDeltaEnt(EntityWriteInfo u) {
		u.NextOldEntity();
		u.NextNewEntity();
	}

	static void PreserveEnt(EntityWriteInfo u) {
		u.NextOldEntity();
		u.NextNewEntity();
	}

	public static void WriteEntityUpdate(EntityWriteInfo u) {
		switch (u.UpdateType) {
			case UpdateType.EnterPVS:
				WriteEnterPVS(u);
				break;
			case UpdateType.LeavePVS:
				WriteLeavePVS(u);
				break;
			case UpdateType.DeltaEnt:
				WriteDeltaEnt(u);
				break;
			case UpdateType.PreserveEnt:
				PreserveEnt(u);
				break;
		}
	}

	public static int WriteDeletions(EntityWriteInfo u) {
		if (!u.AsDelta)
			return 0;

		int numDeletions = 0;

		FrameSnapshot fromSnapshot = u.FromSnapshot!;
		FrameSnapshot toSnapshot = u.ToSnapshot;

		int last = Math.Max(fromSnapshot.NumEntities, toSnapshot.NumEntities);
		for (int i = 0; i < last; i++) {
			if (u.DeletionFlags.Get(i) != 0)
				continue;

			if (u.To!.TransmitEntity.Get(i) != 0)
				continue;

			bool needsExplicitDelete = NeedsExplicitDestroy(i, fromSnapshot, toSnapshot);
			if (!needsExplicitDelete && u.To != null)
				needsExplicitDelete = toSnapshot.ExplicitDeleteSlots.Contains(i);

			if (needsExplicitDelete) {
				u.Buffer.WriteOneBit(1);
				u.Buffer.WriteUBitLong((uint)i, Constants.MAX_EDICT_BITS);
				++numDeletions;
			}
		}

		u.Buffer.WriteOneBit(0);

		return numDeletions;
	}

	internal static int WriteAllDeltaProps(SendTable table, byte[]? fromData, int nFromBits, byte[] toData, int nToBits, int objectId, bf_write bufOut) {
		int[] deltaProps = new int[Constants.MAX_DATATABLE_PROPS];

		int nDeltaProps = EngineSendTable.CalcDelta(table, fromData, nFromBits, toData, nToBits, deltaProps, deltaProps.Length, objectId);

		EngineSendTable.WritePropList(table, toData, nToBits, bufOut, objectId, deltaProps, nDeltaProps);

		return nDeltaProps;
	}
}
