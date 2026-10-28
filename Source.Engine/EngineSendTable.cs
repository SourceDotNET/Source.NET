using System.Text;
using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Engine;
using Source.Common.Hashing;
using Source.Common.Networking;

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Source.Engine;

public static class EngineSendTable
{
	public static CRC32_t GetCRC() => SendTableCRC;
	static readonly List<SendTable> SendTables = [];
	public static CRC32_t SendTableCRC;
	internal static bool Init(Span<SendTable> tables) {
		// Initialize them all.
		for (int i = 0; i < tables.Length; i++) {
			if (!InitTable(tables[i]))
				return false;
		}

		SendTables.EnsureCapacity(tables.Length);
		for (int i = 0; i < tables.Length; i++)
			SendTables.Add(tables[i]);

		SendTableCRC = ComputeCRC(CollectionsMarshal.AsSpan(SendTables));

		return true;
	}

	private static uint ComputeCRC(ReadOnlySpan<SendTable> tables) {
		CRC32_t result = default;
		CRC32.Init(ref result);

		// walk the tables and checksum them
		int c = tables.Length;
		for (int i = 0; i < c; i++) {
			SendTable st = tables[i];
			result = SendTable_CRCTable(ref result, st);
		}

		CRC32.Final(ref result);

		return result;
	}

	private static uint SendTable_CRCTable(ref CRC32_t crc, SendTable table) {
		CRC32.ProcessBuffer(ref crc, Encoding.ASCII.GetBytes(table.NetTableName!).AsSpan());

		CRC32.ProcessBuffer(ref crc, table.Props?.Length ?? 0);

		// Send each property.
		for (int iProp = 0; iProp < table.Props?.Length; iProp++) {
			SendProp prop = table.Props[iProp];

			CRC32.ProcessBuffer(ref crc, (int)prop.Type);
			CRC32.ProcessBuffer(ref crc, Encoding.ASCII.GetBytes(prop.GetName().ToArray()).AsSpan());
			CRC32.ProcessBuffer(ref crc, (int)prop.GetFlags());

			if (prop.Type == SendPropType.DataTable)
				CRC32.ProcessBuffer(ref crc, Encoding.ASCII.GetBytes(prop.GetDataTable()!.NetTableName!).AsSpan());
			else {
				if (prop.IsExcludeProp())
					CRC32.ProcessBuffer(ref crc, Encoding.ASCII.GetBytes(prop.GetExcludeDTName().ToArray()).AsSpan());
				else if (prop.GetPropType() == SendPropType.Array)
					CRC32.ProcessBuffer(ref crc, prop.GetNumElements());
				else {
					CRC32.ProcessBuffer(ref crc, in prop.LowValue);
					CRC32.ProcessBuffer(ref crc, in prop.HighValue);
					CRC32.ProcessBuffer(ref crc, in prop.Bits);
				}
			}
		}

		return crc;
	}

	private static bool InitTable(SendTable table) {
		if (table.Precalc != null)
			return true;

		SendTablePrecalc precalc = new();
		table.Precalc = precalc;

		precalc.SendTable = table;
		table.Precalc = precalc;

		CalcNextVectorElems(table);

		if (!precalc.SetupFlatPropertyArray())
			return false;

		Validate(precalc);
		return true;
	}

	private static void Validate(SendTablePrecalc precalc) {
		SendTable table = precalc.SendTable;
		for (int i = 0; i < table.Props?.Length; i++) {
			SendProp prop = table.Props[i];

			if (prop.GetArrayProp() != null) {
				if (prop.GetArrayProp()!.GetPropType() == SendPropType.DataTable)
					Error($"Invalid property: {table.NetTableName}/{prop.GetName()} (array of datatables) [on prop {i} of {table.Props?.Length ?? 0} ({prop.GetArrayProp()!.GetName()})].");
			}
			else
				ErrorIfNot(prop.GetNumElements() == 1, $"Prop {table.NetTableName}/{prop.GetName()} has an invalid element count for a non-array.");

			if (prop.Bits == 1 && (prop.GetFlags() & PropFlags.Unsigned) == 0)
				DataTable_Warning($"SendTable prop {table.NetTableName}::{prop.GetName()} is a 1-bit signed property. Use PropFlags.Unsigned or the client will never receive a value.\n");
		}

		for (int i = 0; i < precalc.GetNumProps(); ++i) {
			SendProp prop = precalc.GetProp(i)!;
			if ((prop.GetFlags() & PropFlags.EncodedAgainstTickCount) != 0) {
				table.SetHasPropsEncodedAgainstTickcount(true);
				break;
			}
		}
	}

	private static void DataTable_Warning(ReadOnlySpan<char> message) {
		Warning(message);
		Debug.Assert(false, new(message));
	}

	private static void CalcNextVectorElems(SendTable table) {
		for (int i = 0; i < table.GetNumProps(); i++) {
			SendProp prop = table.GetProp(i);

			if (prop.GetPropType() == SendPropType.DataTable)
				CalcNextVectorElems(prop.GetDataTable()!);
			else if (prop.GetOffset() < 0) {
				prop.SetOffset(-prop.GetOffset());
				prop.SetFlags(prop.GetFlags() | PropFlags.IsAVectorElem);
			}
			else if (prop.FieldInfo is DynamicArrayIndexAccessor afii && afii.IsAVectorElement)
				prop.SetFlags(prop.GetFlags() | PropFlags.IsAVectorElem);
		}
	}

	public static bool Encode(SendTable table, object data, bf_write dataOut, int objectId, SendProxyRecipients[]? recipients, bool nonZeroOnly) {
		SendTablePrecalc precalc = table.Precalc!;
		ErrorIfNot(precalc != null, $"SendTable_Encode: Missing precalc for table {table.NetTableName}.");
		if (recipients?.Length > 0)
			ErrorIfNot(recipients.Length >= precalc.GetNumDataTableProxies(), $"SendTable_Encode: recipients array too small (got {recipients.Length}, need {precalc.GetNumDataTableProxies()}).");

		EncodeInfo info = new(precalc, data, objectId, dataOut) {
			Recipients = recipients
		};
		info.Init();

		int numProps = precalc.GetNumProps();
		for (int i = 0; i < numProps; i++) {
			if (!info.IsPropProxyValid(i))
				continue;

			info.SeekToProp((uint)i);

			if (nonZeroOnly && IsPropZero(info, i))
				continue;

			EncodeProp(info, i);
		}

		info.DeltaBitsWriter.Dispose();

		return !dataOut.Overflowed;
	}

	public static void WritePropList(SendTable table, byte[]? state, int nBits, bf_write outBuf, int objectId, Span<int> checkProps, int nCheckProps) {
		if (nCheckProps == 0) {
			outBuf.WriteOneBit(0);
			return;
		}

		SendTablePrecalc precalc = table.Precalc!;
		DeltaBitsWriter bitsWriter = new(outBuf);

		bf_read inputBuf = new("SendTable_WritePropList->inputBuffer", state, BitBuffer.BitByte(nBits), nBits);
		DeltaBitsReader bitsReader = new(inputBuf);

		uint prop = bitsReader.ReadNextPropIndex();

		int i = 0;
		while (i < nCheckProps) {
			while (prop < (uint)checkProps[i]) {
				bitsReader.SkipPropData(precalc.GetProp((int)prop)!);
				prop = bitsReader.ReadNextPropIndex();
			}

			if (prop >= Constants.MAX_DATATABLE_PROPS)
				break;

			if (prop == (uint)checkProps[i]) {
				SendProp p = precalc.GetProp((int)prop)!;
				bitsWriter.WritePropIndex((int)prop);
#if GMOD_DLL
				if (p.Type == SendPropType.GModTable) {
					GMODDataTable? dt = GMODDataTable.s_CurrentTable;
					int referenceTick = GMODDataTable.s_ReferenceTick;
					if (dt == null)
						Error("SendTable_WritePropList: dt is null???");
					dt!.WriteProps(inputBuf, bitsWriter.GetBitBuf(), referenceTick);
				}
				else
#endif
				bitsReader.CopyPropData(bitsWriter.GetBitBuf(), p);
				prop = bitsReader.ReadNextPropIndex();
			}

			i++;
		}

		bitsWriter.Dispose();

		// inputBuf.ForceFinished();
	}

	static bool IsPropZero(EncodeInfo info, int _) {
		SendProp p = info.GetCurProp()!;

		DVariant var = new();
		object baseData = info.GetCurStructBase()!;

		IFieldAccessor accessor = p.FieldInfo;
		p.GetProxyFn()(p, baseData, accessor, ref var, 0, info.GetObjectID());

		return PropTypeFns.g_PropTypeFns[(int)p.Type].IsZero(baseData, ref var, p);
	}

	public static int GetNumFlatProps(SendTable table) {
		SendTablePrecalc precalc = table.Precalc!;
		ErrorIfNot(precalc != null, $"SendTable_GetNumFlatProps: missing pPrecalc.");
		return precalc.GetNumProps();
	}

	static void EncodeProp(EncodeInfo info, int prop) {
		DVariant var = new();

		SendProp p = info.GetCurProp()!;
		object baseData = info.GetCurStructBase()!;

		// to (likely) callum: don't add a try/catch here, confused the hell out of me for a bit...
		// not a big deal though. Just that I imagine the reason this was added was due to missing encoders,
		// of which shouldn't be a problem anyway... and any other problems should throw errors rather than silently dying
		// (and later ruining decoding entirely)
		p.GetProxyFn()(p, baseData, p.FieldInfo, ref var, 0, info.GetObjectID());

		info.DeltaBitsWriter.WritePropIndex(prop);

		PropTypeFns.g_PropTypeFns[(int)p.Type].Encode(baseData, ref var, p, info.DeltaBitsWriter.GetBitBuf(), info.GetObjectID());
	}

	public static int CalcDelta(SendTable table, byte[]? fromState, int nFromBits, byte[] toState, int nToBits, Span<int> deltaProps, int maxDeltaProps, int objectId) {
		int nDeltaProps = 0;

		SendTablePrecalc precalc = table.Precalc!;

		bf_read toBits = new("CalcDelta/toBits", toState, BitBuffer.BitByte(nToBits), nToBits);
		DeltaBitsReader toBitsReader = new(toBits);
		uint iToProp = toBitsReader.ReadNextPropIndex();

		if (fromState != null) {
			bf_read fromBitsBuf = new("CalcDelta/fromBits", fromState, BitBuffer.BitByte(nFromBits), nFromBits);
			DeltaBitsReader fromBitsReader = new(fromBitsBuf);
			uint iFromProp = fromBitsReader.ReadNextPropIndex();

			for (; iToProp < Constants.MAX_DATATABLE_PROPS; iToProp = toBitsReader.ReadNextPropIndex()) {
				Assert((int)iToProp >= 0);

				// Skip any properties in the from state that aren't in the to state.
				while (iFromProp < iToProp) {
					fromBitsReader.SkipPropData(precalc.GetProp((int)iFromProp)!);
					iFromProp = fromBitsReader.ReadNextPropIndex();
				}

				if (iFromProp == iToProp) {
					// The property is in both states, so compare them and write the index
					// if the states are different.
					if (fromBitsReader.ComparePropData(ref toBitsReader, precalc.GetProp((int)iToProp)!) != 0) {
						deltaProps[nDeltaProps++] = (int)iToProp;
						if (nDeltaProps >= maxDeltaProps)
							break;
					}

					// Seek to the next property.
					iFromProp = fromBitsReader.ReadNextPropIndex();
				}
				else {
					// Only the 'to' state has this property, so just skip its data and register a change.
					toBitsReader.SkipPropData(precalc.GetProp((int)iToProp)!);
					deltaProps[nDeltaProps++] = (int)iToProp;
					if (nDeltaProps >= maxDeltaProps)
						break;
				}
			}

			Assert(iToProp == ~0u);

			fromBitsReader.ForceFinished();
		}
		else {
			for (; iToProp != unchecked((uint)-1); iToProp = toBitsReader.ReadNextPropIndex()) {
				Assert((int)iToProp >= 0 && iToProp < Constants.MAX_DATATABLE_PROPS);

				SendProp prop = precalc.GetProp((int)iToProp)!;
				if (!PropTypeFns.g_PropTypeFns[(int)prop.Type].IsEncodedZero(prop, toBits)) {
					deltaProps[nDeltaProps++] = (int)iToProp;
					if (nDeltaProps >= maxDeltaProps)
						break;
				}
			}
		}

		return nDeltaProps;
	}

	public static int CullPropsFromProxies(SendTable table, int[] startProps, int nStartProps, int client, List<SendProxyRecipients>? oldStateProxies, int numOldStateProxies, List<SendProxyRecipients>? newStateProxies, int numNewStateProxies, int[] outProps, int maxOutProps) {
		PropCullStack stack = new(table.Precalc!, client, oldStateProxies, numOldStateProxies, newStateProxies, numNewStateProxies);

		stack.CullPropsFromProxies(startProps, nStartProps, outProps, maxOutProps);

		ErrorIfNot(stack.NumOutProps <= maxOutProps, $"CullPropsFromProxies: overflow in '{table.NetTableName}'.");

		return stack.NumOutProps;
	}
}

class EncodeInfo(SendTablePrecalc precalc, object structData, int objectId, bf_write dataOut) : DatatableStack(precalc, structData, objectId)
{
	public DeltaBitsWriter DeltaBitsWriter = new(dataOut);
}

class PropCullStack : DatatableStack
{
	SendTablePrecalc Precalc;
	int Client;
	List<SendProxyRecipients>? OldStateProxies;
	int NumOldStateProxies;

	List<SendProxyRecipients>? NewStateProxies;
	int NumNewStateProxies;

	int[]? OutProps;
	int MaxOutProps;
	public int NumOutProps;

	readonly int[] NewProxyProps = new int[Constants.MAX_DATATABLE_PROPS + 1];
	int NumNewProxyProps;

	public PropCullStack(SendTablePrecalc precalc, int client, List<SendProxyRecipients>? oldStateProxies, int numOldStateProxies, List<SendProxyRecipients>? newStateProxies, int numNewStateProxies) : base(precalc, (byte)1, -1) {
		Precalc = precalc;
		Client = client;
		OldStateProxies = oldStateProxies;
		NumOldStateProxies = numOldStateProxies;
		NewStateProxies = newStateProxies;
		NumNewStateProxies = numNewStateProxies;
	}

	public void CullPropsFromProxies(int[] startProps, int numStartProps, int[] outProps, int maxOutProps) {
		OutProps = outProps;
		MaxOutProps = maxOutProps;
		NumOutProps = 0;
		NumNewProxyProps = 0;

		Init();

		NewProxyProps[NumNewProxyProps] = -1;
		int curNewProxyProp = 0;

		for (int i = 0; i < numStartProps; i++) {
			int prop = startProps[i];

			while ((uint)NewProxyProps[curNewProxyProp] < (uint)prop) {
				AddProp(NewProxyProps[curNewProxyProp]);
				++curNewProxyProp;
			}

			if (IsPropProxyValid(prop)) {
				AddProp(prop);

				if (NewProxyProps[curNewProxyProp] == prop)
					++curNewProxyProp;
			}
		}

		while ((uint)NewProxyProps[curNewProxyProp] < Constants.MAX_DATATABLE_PROPS) {
			AddProp(NewProxyProps[curNewProxyProp]);
			++curNewProxyProp;
		}
	}

	void AddProp(int prop) {
		if (NumOutProps < MaxOutProps)
			OutProps![NumOutProps++] = prop;
		else
			Error("PropCullStack::AddProp - m_pOutProps overflowed");
	}

	public override object? CallPropProxy(SendNode curChild, int prop, object instance) {
		if (curChild.GetDataTableProxyIndex() == Constants.DATATABLE_PROXY_INDEX_NOPROXY)
			return (byte)1;

		if (NewStateProxies == null || curChild.GetDataTableProxyIndex() >= NumNewStateProxies)
			Error($"PropCullStack::CallPropProxy - invalid new state proxy index {curChild.GetDataTableProxyIndex()} (num new state proxies: {NumNewStateProxies})");

		bool cur = NewStateProxies[curChild.GetDataTableProxyIndex()].Bits.IsBitSet(Client);

		if (OldStateProxies != null) {
			if (curChild.GetDataTableProxyIndex() >= NumOldStateProxies)
				Error($"PropCullStack::CallPropProxy - invalid old state proxy index {curChild.GetDataTableProxyIndex()} (num old state proxies: {NumOldStateProxies})");

			bool prev = OldStateProxies[curChild.GetDataTableProxyIndex()].Bits.IsBitSet(Client);
			if (prev != cur) {
				if (prev)
					return null;
				else {
					for (int i = 0; i < curChild.RecursiveProps; i++) {
						if (NumNewProxyProps < NewProxyProps.Length)
							NewProxyProps[NumNewProxyProps++] = curChild.FirstRecursiveProp + i;
						else
							Error("PropCullStack::CallPropProxy - overflowed m_NewProxyProps");
					}

					return null;
				}
			}
		}

		return cur ? 1 : null;
	}

	public override void RecurseAndCallProxies(SendNode node, object? instance) {
		Proxies[node.GetRecursiveProxyIndex()] = instance;

		for (int i = 0; i < node.GetNumChildren(); i++) {
			SendNode child = node.GetChild(i);

			object? newInstance = null;
			if (instance != null)
				newInstance = CallPropProxy(child, child.DataTableProp, instance);

			RecurseAndCallProxies(child, newInstance);
		}
	}
}
