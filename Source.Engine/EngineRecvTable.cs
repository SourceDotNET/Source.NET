// #define DUMP_DECODE_DEBUGGING_INFO

using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Engine;

using System.Collections;
using System.Diagnostics;

namespace Source.Engine;


public struct DeltaBitsReader : IDisposable
{
	bf_read? Buffer;
	int LastProp;

	public readonly bf_read GetBitBuf() => Buffer!;

	public DeltaBitsReader(bf_read? buf) {
		Buffer = buf;
		LastProp = -1;
	}

	public void ForceFinished() {
		Buffer = null;
	}

	public uint ReadNextPropIndex() {
		Assert(Buffer != null);

		if (Buffer.BitsLeft >= 7) {
			uint bits = Buffer.ReadUBitLong(7);
			if ((bits & 1) != 0) {
				uint delta = bits >> 3;
				if ((bits & 6) != 0)
					delta = Buffer.ReadUBitVarInternal((int)((bits & 6) >> 1));

				LastProp = (int)(LastProp + 1 + delta);
				Assert(LastProp < Constants.MAX_DATATABLE_PROPS);
				return (uint)LastProp;
			}
			Buffer.BitsRead -= 6;
		}
		else {
			if (Buffer.ReadOneBit() != 0)
				Buffer.Seek(-1);
		}
		ForceFinished();
		return ~0u;
	}

	public void SkipPropData(SendProp prop) => PropTypeFns.Get(prop.Type).SkipProp(prop, Buffer!);
	public void CopyPropData(bf_write outWrite, SendProp prop) {
		int start = Buffer!.BitsRead;
		PropTypeFns.Get(prop.Type).SkipProp(prop, Buffer!);
		int len = Buffer!.BitsRead - start;
		Buffer!.Seek(start);
		outWrite.WriteBitsFromBuffer(Buffer!, len);
	}
	public int ComparePropData(ref DeltaBitsReader inReader, SendProp prop) => PropTypeFns.Get(prop.Type).CompareDeltas(prop, Buffer!, inReader.Buffer!);

	public void Dispose() {
		Assert(Buffer == null);
	}
}

public struct DeltaBitsWriter : IDisposable
{
	bf_write? buf;
	int LastProp;
	public DeltaBitsWriter(bf_write buf) {
		this.buf = buf;
		LastProp = -1;
	}

	public readonly bf_write GetBitBuf() => buf!;

	public void WritePropIndex(int prop) {
		Assert(prop >= 0 && prop < Constants.MAX_DATATABLE_PROPS);

		int diff = prop - LastProp;
		LastProp = prop;
		Assert(diff > 0 && diff <= Constants.MAX_DATATABLE_PROPS);

		Debug.Assert(Constants.MAX_DATATABLE_PROPS <= 0x1000);

		int n = ((diff < 0x11) ? -1 : 0) + ((diff < 0x101) ? -1 : 0);
		uint value = (uint)(diff * 8 - 8 + 4 + n * 2 + 1);
		int numBits = 8 + n * 4 + 4 + 2 + 1;

		buf!.WriteUBitLong(value, numBits);
	}


	public readonly void Dispose() {
		buf!.WriteOneBit(0);
	}
}

public class PropVisitor<TableType, PropType>(TableType table) : IEnumerable<KeyValuePair<TableType, PropType>> where TableType : IDataTableBase<PropType> where PropType : IDataTableProp
{
	public IEnumerator<KeyValuePair<TableType, PropType>> GetEnumerator() {
		foreach (var r in Visit(table))
			yield return r;
	}
	public IEnumerable<KeyValuePair<TableType, PropType>> Visit(TableType table) {
		for (int i = 0; i < table.GetNumProps(); i++) {
			PropType type = table.GetProp(i);
			yield return new(table, type);
			if (type.GetPropType() == SendPropType.DataTable)
				foreach (var t in Visit((TableType)type.GetDataTable<PropType>()!))
					yield return t;
		}
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public abstract class DatatableStack
{
	public SendTablePrecalc Precalc;
	public SendProxyRecipients[]? Recipients;
	public InlineArray256<object?> Proxies;
	public object Instance;
	protected int CurPropIndex;
	protected SendProp? CurProp;
	protected int ObjectID;
	protected bool Initted;
	public DatatableStack(SendTablePrecalc precalc, object instance, int objectID) {
		Precalc = precalc;
		Instance = instance;
		ObjectID = objectID;
	}

	public void Reset(SendTablePrecalc precalc, object instance, int objectID) {
		Precalc = precalc;
		Instance = instance;
		ObjectID = objectID;
		CurPropIndex = 0;
		CurProp = null;
		Initted = false;
	}

	protected static readonly object Uncomputed = new();

	public void Init(bool explicitRoutes = false) {
		if (explicitRoutes) {
			int count = Precalc.ProxyPaths.Count;
			for (int i = 0; i < count; i++)
				Proxies[i] = Uncomputed;
		}
		else {
			RecurseAndCallProxies(Precalc.Root, Instance);
		}
		Initted = true;
	}

	protected virtual object? CallExplicitProxy(object structBase, int datatableProp) {
		SendProp prop = Precalc.GetDatatableProp(datatableProp)!;
		return prop.GetDataTableProxyFn()(prop, structBase, prop.FieldInfo, s_Recipients, GetObjectID());
	}

	public object? UpdateRoutesExplicit() {
		ushort iPropProxyIndex = Precalc.PropProxyIndices[CurPropIndex];
		if (!ReferenceEquals(Proxies[iPropProxyIndex], Uncomputed))
			return Proxies[iPropProxyIndex];

		object? structBase = Instance;

		SendTablePrecalc.ProxyPath proxyPath = Precalc.ProxyPaths[iPropProxyIndex];
		for (ushort i = 0; i < proxyPath.Entries; i++) {
			SendTablePrecalc.ProxyPathEntry entry = Precalc.ProxyPathEntries[proxyPath.FirstEntry + i];
			int iProxy = entry.Proxy;

			if (ReferenceEquals(Proxies[iProxy], Uncomputed)) {
				Proxies[iProxy] = CallExplicitProxy(structBase!, entry.DataTableProp);
				if (Proxies[iProxy] == null) {
					Proxies[iPropProxyIndex] = null;
					break;
				}
			}

			structBase = Proxies[iProxy];
		}

		return structBase;
	}

	public virtual void RecurseAndCallProxies(SendNode node, object? instance) {
		Proxies[node.GetRecursiveProxyIndex()] = instance;

		for (int iChild = 0; iChild < node.GetNumChildren(); iChild++) {
			SendNode? curChild = node.GetChild(iChild);

			object? newStructBase = null;
			if (instance != null)
				newStructBase = CallPropProxy(curChild, curChild.DataTableProp, instance);

			RecurseAndCallProxies(curChild, newStructBase);
		}
	}

	static SendProxyRecipients s_Recipients = new();
	public virtual object? CallPropProxy(SendNode curChild, int prop, object instance) {
		SendProp sendprop = Precalc.GetDatatableProp(prop)!;

		SendProxyRecipients? recipients = default;
		if (Recipients != null && curChild.GetDataTableProxyIndex() != Constants.DATATABLE_PROXY_INDEX_NOPROXY) {
			recipients = Recipients[curChild.GetDataTableProxyIndex()];
			recipients.SetAllRecipients();
		}

		return sendprop.GetDataTableProxyFn()(
			sendprop,
			instance,
			sendprop.FieldInfo,
			recipients ?? s_Recipients,
			GetObjectID()
		);
	}

	public SendProp? GetCurProp() => CurProp;
	public bool IsPropProxyValid(int iProp) => Proxies[Precalc.PropProxyIndices[iProp]] != null;
	public bool IsCurProxyValid() => Proxies[Precalc.PropProxyIndices[CurPropIndex]] != null;
	public object? GetCurStructBase() => Proxies[Precalc.PropProxyIndices[CurPropIndex]];
	public void SeekToProp(uint iProp) {
		CurPropIndex = (int)iProp;
		CurProp = Precalc.GetProp((int)iProp);
	}

	public int GetObjectID() {
		return ObjectID;
	}
}

public class ClientDatatableStack : DatatableStack
{
	RecvDecoder Decoder;

	public ClientDatatableStack(RecvDecoder decoder, object instance, int objectID) : base(decoder.Precalc, instance, objectID) {
		Decoder = decoder;
	}

	public void Reset(RecvDecoder decoder, object instance, int objectID) {
		Decoder = decoder;
		Reset(decoder.Precalc, instance, objectID);
	}

	public override void RecurseAndCallProxies(SendNode node, object? instance) {
		Proxies[node.GetRecursiveProxyIndex()] = instance;

		for (int iChild = 0; iChild < node.GetNumChildren(); iChild++) {
			SendNode? curChild = node.GetChild(iChild);

			object? newStructBase = null;
			if (instance != null)
				newStructBase = CallPropProxy(curChild, curChild.DataTableProp, instance);

			RecurseAndCallProxies(curChild, newStructBase);
		}
	}

	protected override object? CallExplicitProxy(object structBase, int datatableProp) {
		RecvProp prop = Decoder.GetDatatableProp(datatableProp);
		prop.GetDataTableProxyFn()(prop, out object? val, structBase, prop.FieldInfo, GetObjectID());
		return val;
	}

	// TODO: Review why this wasn't overridden. There may have been a good reason for it, I forget. But it's giving compiler warnings.
	private object? CallPropProxy(SendNode curChild, int prop, object instance) {
		RecvProp recvProp = Decoder.GetDatatableProp(prop);

		object? val = null;
		Assert(recvProp != null);
		if (recvProp == null)
			return null;

		recvProp.GetDataTableProxyFn()(
			recvProp,
			out val,
			instance,
			recvProp.FieldInfo,
			GetObjectID()
			);

		return val;
	}
}

public class ServerDatatableStack : DatatableStack
{
	public ServerDatatableStack(SendTablePrecalc precalc, object instance, int objectID) : base(precalc, instance, objectID) {
	}
}

[EngineComponent]
public class EngineRecvTable(DtCommonEng DtCommonEng)
{
	bool NewTable(RecvTable table) {
		if (table.IsInMainList())
			return false;

		table.SetInMainList(true);
		DtCommonEng.RecvTables.AddLast(table);

		return true;
	}
	public bool Init(Span<RecvTable> tables) {
		foreach (var table in tables) {
			NewTable(table);
			PropVisitor<RecvTable, RecvProp> visitor = new(table);
			foreach (var subTable in visitor) {
				if (!NewTable(subTable.Key))
					continue;
			}
		}

		return true;
	}

	// A hashset of datatables that don't need decode prints.
	// In the future, this should be made a whitelist of some sort which is enabled
	// at compile time. But for right now during indev this is fine.
#if DUMP_DECODE_DEBUGGING_INFO
	static readonly HashSet<ulong> OKDatatables = [
		"DT_WORLD".Hash(),
		"DT_GMOD_Player".Hash(),
		"DT_PlayerResource".Hash(),
		"DT_GMODGameRulesProxy".Hash(),
		"DT_EnvTonemapController".Hash(),
		"DT_PredictedViewModel".Hash(),
		"DT_WeaponSWEP".Hash(),
		"DT_SENT_point".Hash(),
		"DT_SENT_anim".Hash(),
		"DT_WeaponPhysGun".Hash(),
		"DT_BaseEntity".Hash(),
		"DT_PhysBeam".Hash(),
	];
#endif

	public void DecodeZeros(RecvTable table, object instance, int objectID) {
		RecvDecoder? decoder = table.Decoder;
		ErrorIfNot(decoder != null, $"RecvTable_DecodeZeros: table '{table.GetName()}' missing a decoder.");

		ClientDatatableStack theStack = new(decoder, instance, objectID);

		theStack.Init();

		for (int iProp = 0; iProp < decoder.GetNumProps(); iProp++) {
			theStack.SeekToProp((uint)iProp);

			RecvProp? recvProp = decoder.GetProp(iProp);
			if (recvProp == null)
				continue;

			DecodeInfo decodeInfo = new();
			decodeInfo.Object = theStack.GetCurStructBase()!;
			decodeInfo.FieldInfo = recvProp.FieldInfo;
			decodeInfo.RecvProxyData.RecvProp = theStack.IsCurProxyValid() ? recvProp : null!;
			decodeInfo.Prop = decoder.GetSendProp(iProp);
			decodeInfo.In = null!;
			decodeInfo.RecvProxyData.ObjectID = objectID;

			PropTypeFns.Get(decodeInfo.Prop.GetPropType()).DecodeZero(ref decodeInfo);
		}
	}

	public bool Decode(RecvTable table, object instance, bf_read inRead, int objectID, bool updateDTI = true) {
		RecvDecoder? decoder = table.Decoder;
		ErrorIfNot(decoder != null, $"RecvTable_Decode: table '{table.GetName()}' missing a decoder.");

		ClientDatatableStack theStack = new(decoder, instance, objectID);

		theStack.Init();
		int /* iStartBit = 0, nIndexBits = 0, */ iLastBit = inRead.BitsRead;
		uint iProp;
		using DeltaBitsReader deltaBitsReader = new(inRead);
		//decoder.DumpNames();
		while ((iProp = deltaBitsReader.ReadNextPropIndex()) < Constants.MAX_DATATABLE_PROPS) {
			theStack.SeekToProp(iProp);

			RecvProp? recvProp = decoder.GetProp((int)iProp);
			//Msg($"  - decoding: {(recvProp == null ? "NULL PROP???" : recvProp.GetName())}\n");

			DecodeInfo decodeInfo = new();
			decodeInfo.Object = theStack.GetCurStructBase()! /* validate? */;

			if (recvProp != null)
				decodeInfo.FieldInfo = recvProp.FieldInfo;
			else
				decodeInfo.FieldInfo = null!;

#if DUMP_DECODE_DEBUGGING_INFO
			if (!OKDatatables.Contains(table.GetName().Hash()))
				if (recvProp?.GetParentArrayPropName() != null)
					DevMsg($"Decoding [{iProp}] {table.GetName()}/{recvProp.GetParentArrayPropName()}/{(recvProp != null ? recvProp.GetName() : "<NULL NAME>")} for {instance}\n");
				else
					DevMsg($"Decoding [{iProp}] {table.GetName()}/{(recvProp != null ? recvProp.GetName() : "<NULL NAME>")} for {instance}\n");
#endif

			decodeInfo.RecvProxyData.RecvProp = theStack.IsCurProxyValid() ? recvProp! : null!;
			decodeInfo.Prop = decoder.GetSendProp((int)iProp);
			decodeInfo.In = inRead;
			decodeInfo.RecvProxyData.ObjectID = objectID;

			PropTypeFns.Get(decodeInfo.Prop.GetPropType()).Decode(ref decodeInfo);
		}

		return !inRead.Overflowed;
	}
	public int MergeDeltas(RecvTable? table, bf_read? oldState, bf_read newState, bf_write outState, int objectID = -1, Span<int> changedProps = default, bool updateDTI = false) {
		using DeltaBitsReader oldStateReader = new(oldState);
		using DeltaBitsReader newStateReader = new(newState);

		using DeltaBitsWriter deltaBitsWriter = new(outState);

		RecvDecoder? decoder = table?.Decoder;
		ErrorIfNot(decoder != null, $"RecvTable_MergeDeltas: table '{(table == null ? "<NULL>" : table.GetName())}' is missing its decoder.");

		int changed = 0;

		uint oldProp = ~0u;
		if (oldState != null)
			oldProp = oldStateReader.ReadNextPropIndex();

		int /* iStartBit = 0, nIndexBits = 0, */ iLastBit = newState.BitsRead;

		uint newProp = newStateReader.ReadNextPropIndex();

		while (true) {
			while (oldProp < newProp) {
				deltaBitsWriter.WritePropIndex((int)oldProp);
				oldStateReader.CopyPropData(deltaBitsWriter.GetBitBuf(), decoder.GetSendProp((int)oldProp));
				oldProp = oldStateReader.ReadNextPropIndex();
			}

			if (newProp >= Constants.MAX_DATATABLE_PROPS)
				break;

			if (oldProp == newProp) {
				oldStateReader.SkipPropData(decoder.GetSendProp((int)oldProp));
				oldProp = oldStateReader.ReadNextPropIndex();
			}

			deltaBitsWriter.WritePropIndex((int)newProp);
			var prop = decoder.GetSendProp((int)newProp);
			newStateReader.CopyPropData(deltaBitsWriter.GetBitBuf(), prop);

			if (!changedProps.IsEmpty)
				changedProps[changed] = (int)newProp;

			changed++;

			newProp = newStateReader.ReadNextPropIndex();
		}

		Assert(changed <= Constants.MAX_DATATABLE_PROPS);

		ErrorIfNot(!(oldState != null && oldState.Overflowed) && !newState.Overflowed && !outState.Overflowed, $"RecvTable_MergeDeltas: overflowed in RecvTable '{(table == null ? "<NULL>" : table.GetName())}'.");

		return changed;
	}

	public bool SetupClientSendTableHierarchy() {
		foreach (ClientSendTable table in DtCommonEng.ClientSendTables) {
			for (int i = 0; i < table.GetNumProps(); i++) {
				ClientSendProp clientProp = table.GetClientProp(i);
				SendProp prop = table.SendTable.Props![i];

				if (prop.Type == SendPropType.DataTable) {
					ReadOnlySpan<char> tableName = clientProp.GetTableName();
					ErrorIfNot(!tableName.IsEmpty && tableName.Length > 0, $"SetupClientSendTableHierarchy: missing table name for prop '{prop.GetName()}'.");

					ClientSendTable? child = FindClientSendTable(tableName);
					if (child == null) {
						Warning($"SetupClientSendTableHierarchy: missing SendTable '{tableName}' (referenced by '{table.GetName()}').\n");
						return false;
					}

					prop.SetDataTable(child.SendTable);
				}
			}
		}

		return true;
	}

	private ClientSendTable? FindClientSendTable(ReadOnlySpan<char> tableName) {
		foreach (var table in DtCommonEng.ClientSendTables) {
			if (table.GetName().Equals(tableName, StringComparison.OrdinalIgnoreCase))
				return table;
		}

		return null;
	}

	internal bool CreateDecoders(bool allowMismatches, out bool anyMismatches) {
		anyMismatches = false;
		if (!SetupClientSendTableHierarchy())
			return false;

		foreach (var decoder in DtCommonEng.RecvDecoders) {
			if (decoder.ClientSendTable == null)
				return false;

			if (!decoder.Precalc.SetupFlatPropertyArray())
				return false;

			Dictionary<SendProp, RecvProp> propLookup = [];
			if (!MatchRecvPropsToSendProps_R(propLookup, decoder.GetSendTable()!.NetTableName, decoder.GetSendTable()!, DtCommonEng.FindRecvTable(decoder.GetSendTable()!.NetTableName)!, allowMismatches, out anyMismatches))
				return false;

			SendTablePrecalc precalc = decoder.Precalc;
			CopySendPropsToRecvProps(propLookup, precalc.Props, decoder.Props);
			CopySendPropsToRecvProps(propLookup, precalc.DataTableProps, decoder.DataTableProps);
		}

		return true;
	}

	private bool MatchRecvPropsToSendProps_R(Dictionary<SendProp, RecvProp> lookup, ReadOnlySpan<char> sendTableName, SendTable sendTable, RecvTable? recvTable, bool allowMismatches, out bool anyMismatches) {
		anyMismatches = false;

		for (int i = 0; i < (sendTable.Props?.Length ?? 0); i++) {
			SendProp sendProp = sendTable.Props![i];
			if (sendProp.IsExcludeProp() || sendProp.IsInsideArray())
				continue;

			RecvProp? recvProp = null;
			if (recvTable != null)
				recvProp = FindRecvProp(recvTable, sendProp.GetName());

			if (recvProp != null) {
				RecvPropMismatchReason mismatch = CompareRecvPropToSendProp(recvProp, sendProp);
				if (mismatch != RecvPropMismatchReason.Matched) {
					Warning($"{mismatch switch {
						RecvPropMismatchReason.MissingRecvProp => "Missing RecvProp",
						RecvPropMismatchReason.MissingSendProp => "Missing SendProp",
						RecvPropMismatchReason.MismatchedPropType => "RecvProp property type doesn't match server property type",
						RecvPropMismatchReason.MismatchedArrayType => "RecvProp array state doesn't match server array state",
						RecvPropMismatchReason.MismatchedArrayElements => "RecvProp array elements doesn't match server array elements",
						_ => "RecvProp type doesn't match server type",
					}} for {sendTable.GetName()}/{sendProp.GetName()}\n");
					return false;
				}

				lookup[sendProp] = recvProp;
			}
			else {
				anyMismatches = true;
				Warning($"Missing RecvProp for {sendTableName} - {sendTable.GetName()}/{sendProp.GetName()}\n");
				if (!allowMismatches)
					return false;
			}

			if (sendProp.GetPropType() == SendPropType.DataTable)
				if (!MatchRecvPropsToSendProps_R(lookup, sendTableName, sendProp.GetDataTable()!, DtCommonEng.FindRecvTable(sendProp.GetDataTable()!.NetTableName), allowMismatches, out anyMismatches))
					return false;
		}

		return true;
	}

	enum RecvPropMismatchReason
	{
		Matched,
		MissingRecvProp,
		MissingSendProp,
		MismatchedPropType,
		MismatchedArrayType,
		MismatchedArrayElements,
	}

	private RecvPropMismatchReason CompareRecvPropToSendProp(RecvProp? recvProp, SendProp? sendProp) {
		while (true) {
			if (recvProp == null)
				return RecvPropMismatchReason.MissingRecvProp;
			if (sendProp == null)
				return RecvPropMismatchReason.MissingSendProp;

			if (recvProp.GetPropType() != sendProp.GetPropType())
				return RecvPropMismatchReason.MismatchedPropType;
			if (recvProp.IsInsideArray() != sendProp.IsInsideArray())
				return RecvPropMismatchReason.MismatchedArrayType;

			if (recvProp.GetPropType() == SendPropType.Array) {
				if (recvProp.GetNumElements() != sendProp.GetNumElements())
					return RecvPropMismatchReason.MismatchedArrayElements;

				recvProp = recvProp.GetArrayProp<RecvProp>();
				sendProp = sendProp.GetArrayProp<SendProp>();
			}
			else
				return RecvPropMismatchReason.Matched;
		}
	}

	private RecvProp? FindRecvProp(RecvTable table, ReadOnlySpan<char> name) {
		for (int i = 0; i < table.GetNumProps(); i++) {
			RecvProp prop = table.GetProp(i);
			if (prop.GetName().Equals(name, StringComparison.OrdinalIgnoreCase))
				return prop;
		}
		return null;
	}

	private void CopySendPropsToRecvProps(Dictionary<SendProp, RecvProp> lookup, List<SendProp> sendProps, List<RecvProp> recvProps) {
		recvProps!.SetSize(sendProps.Count); // justified suppression since below statement handles it
		for (int i = 0; i < sendProps.Count; i++) {
			SendProp sendProp = sendProps[i];
			if (!lookup.TryGetValue(sendProp, out RecvProp? recv))
				recvProps[i] = null!;
			else
				recvProps[i] = recv ?? throw new NullReferenceException();
		}
	}

	public SendTable? ReadInfos(bf_read buf, int demoProtocol){
		SendTable pTable = new SendTable();

		pTable.NetTableName = buf.ReadAndAllocateString();

		// Read the property list.
		int numProps = (int)buf.ReadUBitLong(DtCommonEng.PROPINFOBITS_NUMPROPS);
		pTable.Props = numProps == 0 ? null : new SendProp[numProps];

		for (int iProp = 0; iProp < numProps; iProp++) {
			SendProp prop = pTable.Props![iProp] = new SendProp();

			prop.Type = (SendPropType)buf.ReadUBitLong(DtCommonEng.PROPINFOBITS_TYPE);
			/* TODO: Store this. We use field info though... may need a hack */ buf.ReadAndAllocateString();

			int flagsBits = DtCommonEng.PROPINFOBITS_FLAGS;

			// HACK to playback old demos. SPROP_NUMFLAGBITS was 11, now 13
			// old nDemoProtocol was 2 
			if (demoProtocol == 2) 
				flagsBits = 11;

			prop.SetFlags((PropFlags)buf.ReadUBitLong(flagsBits));

			if (prop.Type == SendPropType.DataTable) 
				prop.ExcludeDTName = buf.ReadAndAllocateString();
			else {
				if (prop.IsExcludeProp()) 
					prop.ExcludeDTName = buf.ReadAndAllocateString();
				else if (prop.GetPropType() == SendPropType.Array) 
					prop.SetNumElements((int)buf.ReadUBitLong(DtCommonEng.PROPINFOBITS_NUMELEMENTS));
				else {
					prop.LowValue = buf.ReadBitFloat();
					prop.HighValue = buf.ReadBitFloat();
					prop.Bits = (int)buf.ReadUBitLong(DtCommonEng.PROPINFOBITS_NUMBITS);
				}
			}
		}

		return pTable;
	}

	public bool RecvClassInfos(bf_read buf, bool needsDecoder, int demoProtocol = 0){
		SendTable? sendTable = ReadInfos(buf, demoProtocol);

		if (sendTable == null)
			return false;

		bool ret = DtCommonEng.SetupReceiveTableFromSendTable(sendTable, needsDecoder);

		return ret;
	}
}
