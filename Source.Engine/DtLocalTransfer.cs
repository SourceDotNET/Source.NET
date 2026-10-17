using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Source.Engine;

public static class LocalTransfer
{
	public const int PROP_INDEX_VECTOR_ELEM_MARKER = 0x8000;

	public static readonly List<int> PartialChangeEnts = [];
	public static int TotalPropChanges = 0;
	public static int TotalEntChanges = 0;

	static readonly ConVar dt_UsePartialChangeEnts = new(
		"dt_UsePartialChangeEnts",
		"1",
		0,
		"(SP only) - enable FL_EDICT_PARTIAL_CHANGE optimization."
	);

	static readonly ConVar dt_ShowPartialChangeEnts = new(
		"dt_ShowPartialChangeEnts",
		"0",
		0,
		"(SP only) - show entities that were copied using small optimized lists (FL_EDICT_PARTIAL_CHANGE)."
	);

	static ServerDatatableStack? s_ServerStack;
	static ClientDatatableStack? s_ClientStack;

	public static void TransferEntity(BaseEdict edict, SendTable sendTable, object srcEnt, RecvTable recvTable, object destEnt, bool newlyCreated, bool justEnteredPVS, int objectID) {
		TotalEntChanges++;
		EdictChangeInfo changeInfo = g_SharedChangeInfo!.ChangeInfos[edict.GetChangeInfo()];

		Span<ushort> propIndices = stackalloc ushort[Edict.MAX_CHANGE_OFFSETS * 3];

		// This code tries to only copy fields expressly marked as "changed" (by having the field accessors added to the changed-fields list)
		if (edict.GetChangeInfoSerialNumber() == g_SharedChangeInfo.SerialNumber && !newlyCreated && !justEnteredPVS && dt_UsePartialChangeEnts.GetInt() != 0) {
			SendTablePrecalc precalc = sendTable.Precalc!;

			int changeOffsets = MapPropFieldsToIndices(edict, precalc, changeInfo, propIndices);
			if (changeOffsets == 0)
				return;

			AddToPartialChangeEntsList(Array.IndexOf(sv.Edicts, edict) /* TODO: Need fast way to do this! */, true);
			FastSortList(propIndices, changeOffsets);

			// Setup the structure to traverse the source tree.
			if (precalc == null)
				Error($"SendTable_Encode: Missing precalc for SendTable {sendTable.NetTableName}.");
			ServerDatatableStack serverStack = s_ServerStack ??= new(precalc, srcEnt, objectID);
			serverStack.Reset(precalc, srcEnt, objectID);
			serverStack.Init(true);

			// Setup the structure to traverse the dest tree.
			RecvDecoder? decoder = recvTable.Decoder;
			if (decoder == null)
				Error($"RecvTable_Decode: table '{recvTable.GetName()}' missing a decoder.");
			ClientDatatableStack clientStack = s_ClientStack ??= new(decoder, destEnt, objectID);
			clientStack.Reset(decoder, destEnt, objectID);
			clientStack.Init(true);

			// Cool. We can get away with just transferring a few.
			for (int iChanged = 0; iChanged < changeOffsets; iChanged++) {
				int iProp = propIndices[iChanged];

				++TotalPropChanges;

				serverStack.SeekToProp((uint)iProp);
				SendProp sendProp = serverStack.GetCurProp()!;
				object? sendBase = serverStack.UpdateRoutesExplicit();
				if (sendBase != null) {
					RecvProp? recvProp = decoder.GetProp(iProp);
					Assert(recvProp != null);

					clientStack.SeekToProp((uint)iProp);
					object? recvBase = clientStack.UpdateRoutesExplicit();
					Assert(recvBase != null);

					PropTypeFns.Get(recvProp!.GetPropType()).FastCopy(sendProp, recvProp, sendBase, sendProp.FieldInfo, recvBase!, recvProp.FieldInfo, objectID);
				}
			}
		}
		// Whereas the below code copies _all_ fields, regardless of whether they were changed or not.  We run this only newly created entities, or entities
		//  which were previously dormant/outside the pvs but are now back in the PVS since we could have missed field updates since the changeoffsets get cleared every
		//  frame.
		else {
			// Setup the structure to traverse the source tree.
			SendTablePrecalc precalc = sendTable.Precalc!;
			if (precalc == null)
				Error($"SendTable_Encode: Missing precalc for SendTable {sendTable.NetTableName}.");
			ServerDatatableStack serverStack = s_ServerStack ??= new(precalc, srcEnt, objectID);
			serverStack.Reset(precalc, srcEnt, objectID);
			serverStack.Init();

			// Setup the structure to traverse the dest tree.
			RecvDecoder? decoder = recvTable.Decoder;
			if (decoder == null)
				Error($"RecvTable_Decode: table '{recvTable.GetName()}' missing a decoder.");
			ClientDatatableStack clientStack = s_ClientStack ??= new(decoder, destEnt, objectID);
			clientStack.Reset(decoder, destEnt, objectID);
			clientStack.Init();

			AddToPartialChangeEntsList(Array.IndexOf(sv.Edicts, edict), false);

			// Copy the properties that require proxies.
			List<FastLocalTransferPropInfo> propList = precalc.FastLocalTransfer.OtherProps;
			int nProps = propList.Count;
			for (int i = 0; i < nProps; i++) {
				int iProp = propList[i].Prop;

				serverStack.SeekToProp((uint)iProp);
				SendProp sendProp = serverStack.GetCurProp()!;
				object? sendBase = serverStack.GetCurStructBase();

				if (sendBase != null) {
					RecvProp? recvProp = decoder!.GetProp(iProp);
					Assert(recvProp != null);

					clientStack.SeekToProp((uint)iProp);
					object? recvBase = clientStack.GetCurStructBase();
					Assert(recvBase != null);

					PropTypeFns.Get(recvProp!.GetPropType()).FastCopy(sendProp, recvProp, sendBase, sendProp.FieldInfo, recvBase!, recvProp.FieldInfo, objectID);
				}
			}

			LocalTransferFast(serverStack, clientStack, decoder!, precalc.FastLocalTransfer.FastInt32, objectID);
			LocalTransferFast(serverStack, clientStack, decoder!, precalc.FastLocalTransfer.FastInt16, objectID);
			LocalTransferFast(serverStack, clientStack, decoder!, precalc.FastLocalTransfer.FastInt8, objectID);
			LocalTransferFast(serverStack, clientStack, decoder!, precalc.FastLocalTransfer.FastVector, objectID);
		}
	}

	static void LocalTransferFast(ServerDatatableStack serverStack, ClientDatatableStack clientStack, RecvDecoder decoder, List<FastLocalTransferPropInfo> propList, int objectID) {
		for (int i = 0; i < propList.Count; i++) {
			int iProp = propList[i].Prop;

			serverStack.SeekToProp((uint)iProp);
			object? sendBase = serverStack.GetCurStructBase();
			if (sendBase == null)
				continue;

			SendProp sendProp = serverStack.GetCurProp()!;

			clientStack.SeekToProp((uint)iProp);
			object? recvBase = clientStack.GetCurStructBase();
			Assert(recvBase != null);

			RecvProp? recvProp = decoder.GetProp(iProp);
			Assert(recvProp != null);

			PropTypeFns.Get(recvProp!.GetPropType()).FastCopy(sendProp, recvProp, sendBase, sendProp.FieldInfo, recvBase!, recvProp.FieldInfo, objectID);
		}
	}

	public static void AddPropOffsetToMap(SendTablePrecalc precalc, int prop, IFieldAccessor fieldAccessor) {
		if (precalc.PropAccessorToIndexMap.TryGetValue(fieldAccessor, out ushort _))
			return;

		precalc.PropAccessorToIndexMap[fieldAccessor] = (ushort)prop;
	}

	public static void BuildPropOffsetToIndexMap(SendTablePrecalc precalc, StandardSendProxies sendProxies) {
		for (int i = 0; i < precalc.Props.Count; i++) {
			SendProp prop = precalc.Props[i];

			IFieldAccessor? field = prop.FieldInfo;
			int elementCount = 1;

			if (prop.GetPropType() == SendPropType.Array) {
				SendProp? arrayProp = prop.GetArrayProp();
				if (arrayProp == null)
					continue;

				field = arrayProp.FieldInfo;
				elementCount = prop.GetNumElements();
			}

			if (field == null)
				continue;

			bool isVectorElem = (prop.GetFlags() & PropFlags.IsAVectorElem) != 0;

			for (int j = 0; j < elementCount; j++) {
				IFieldAccessor elementField = field is IFieldAccessorIndexable indexable ? indexable.AtIndex(j) : field;

				if (isVectorElem)
					AddPropOffsetToMap(precalc, i | PROP_INDEX_VECTOR_ELEM_MARKER, elementField);
				else
					AddPropOffsetToMap(precalc, i, elementField);
			}
		}
	}

	static int MapPropFieldsToIndices(BaseEdict edict, SendTablePrecalc precalc, EdictChangeInfo changeInfo, Span<ushort> outIndices) {
		int iOut = 0;

		for (ushort i = 0; i < changeInfo.NumChangeFields; i++) {
			IFieldAccessor? field = changeInfo.ChangedFields[i];
			if (field == null)
				continue;

			if (!precalc.PropAccessorToIndexMap.TryGetValue(field, out ushort propIndex)) {
				// Note: this SHOULD be fine
				// If we can't find a field here, then there isn't a SendProp associated with the CNetworkVar that triggered the change
				// so the change doesn't matter
				if (dt_ShowPartialChangeEnts.GetInt() != 0)
					Warning($"LocalTransfer field miss - DT: {precalc.GetSendTable()?.NetTableName}, field: {field.Name}\n");

				continue;
			}

			if ((propIndex & PROP_INDEX_VECTOR_ELEM_MARKER) != 0)
				outIndices[iOut++] = (ushort)(propIndex & ~PROP_INDEX_VECTOR_ELEM_MARKER);
			else
				outIndices[iOut++] = propIndex;
		}

		return iOut;
	}

	static void FastSortList(Span<ushort> list, int nEntries) {
		if (nEntries <= 1)
			return;

		int i = 0;
		while (true) {
			if (list[i + 1] < list[i]) {
				(list[i], list[i + 1]) = (list[i + 1], list[i]);

				if (i > 0)
					--i;
			}
			else {
				++i;
				if (i >= nEntries - 1)
					return;
			}
		}
	}

	static void AddToPartialChangeEntsList(int iEnt, bool partial) {
		if (dt_ShowPartialChangeEnts.GetInt() == 0)
			return;

		if (!partial)
			iEnt = -iEnt;

		if (!PartialChangeEnts.Contains(iEnt))
			PartialChangeEnts.Add(iEnt);
	}

	public static void InitFastCopy(SendTable sendTable, StandardSendProxies sendProxies, RecvTable recvTable, StandardRecvProxies recvProxies, ref int slowCopyProps, ref int fastCopyProps) {
		SendTablePrecalc precalc = sendTable.Precalc!;

		// Setup the offset-to-index map.
		precalc.PropAccessorToIndexMap.Clear();
		BuildPropOffsetToIndexMap(precalc, sendProxies);

		// Clear the old lists.
		precalc.FastLocalTransfer.FastInt32.Clear();
		precalc.FastLocalTransfer.FastInt16.Clear();
		precalc.FastLocalTransfer.FastInt8.Clear();
		precalc.FastLocalTransfer.FastVector.Clear();
		precalc.FastLocalTransfer.OtherProps.Clear();

		RecvDecoder? decoder = recvTable.Decoder;
		int iNumProp = precalc.GetNumProps();
		for (int prop = 0; prop < iNumProp; prop++) {
			SendProp sendProp = precalc.GetProp(prop)!;
			RecvProp recvProp = decoder.GetProp(prop)!;

			if (recvProp != null) {
				Assert(stricmp(sendProp.GetName(), recvProp.GetName()) == 0);

				List<FastLocalTransferPropInfo> list = precalc.FastLocalTransfer.OtherProps;

				if (sendProp.GetPropType() == SendPropType.Int &&
					(sendProp.GetProxyFn() == sendProxies.Int32ToInt32 || sendProp.GetProxyFn() == sendProxies.UInt32ToInt32) &&
					recvProp.GetProxyFn() == recvProxies.Int32ToInt32) {
					list = precalc.FastLocalTransfer.FastInt32;
					++fastCopyProps;
				}
				else if (sendProp.GetPropType() == SendPropType.Int &&
					(sendProp.GetProxyFn() == sendProxies.Int16ToInt32 || sendProp.GetProxyFn() == sendProxies.UInt16ToInt32) &&
					recvProp.GetProxyFn() == recvProxies.Int32ToInt16) {
					list = precalc.FastLocalTransfer.FastInt16;
					++fastCopyProps;
				}
				else if (sendProp.GetPropType() == SendPropType.Int &&
					(sendProp.GetProxyFn() == sendProxies.Int8ToInt32 || sendProp.GetProxyFn() == sendProxies.UInt8ToInt32) &&
					recvProp.GetProxyFn() == recvProxies.Int32ToInt8) {
					list = precalc.FastLocalTransfer.FastInt8;
					++fastCopyProps;
				}
				else if (sendProp.GetPropType() == SendPropType.Float &&
					sendProp.GetProxyFn() == sendProxies.FloatToFloat &&
					recvProp.GetProxyFn() == recvProxies.FloatToFloat) {
					Assert(sizeof(int) == sizeof(float));
					list = precalc.FastLocalTransfer.FastInt32;
					++fastCopyProps;
				}
				else if (sendProp.GetPropType() == SendPropType.Vector &&
					sendProp.GetProxyFn() == sendProxies.VectorToVector &&
					recvProp.GetProxyFn() == recvProxies.VectorToVector) {
					list = precalc.FastLocalTransfer.FastVector;
					++fastCopyProps;
				}
				else {
					++slowCopyProps;
				}

				FastLocalTransferPropInfo toAdd = new();
				toAdd.Prop = (ushort)prop;
				toAdd.RecvOffset = (ushort)recvProp.GetOffset();
				toAdd.SendOffset = (ushort)sendProp.GetOffset();
				list.Add(toAdd);
			}
		}
	}
}
