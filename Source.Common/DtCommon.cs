using Source.Common.Mathematics;

using System.Numerics;

namespace Source.Common;

public struct FastLocalTransferPropInfo()
{
	public ushort RecvOffset;
	public ushort SendOffset;
	public ushort Prop;
}

public class FastLocalTransferInfo
{
	public readonly List<FastLocalTransferPropInfo> FastInt32 = [];
	public readonly List<FastLocalTransferPropInfo> FastInt16 = [];
	public readonly List<FastLocalTransferPropInfo> FastInt8 = [];
	public readonly List<FastLocalTransferPropInfo> FastVector = [];
	public readonly List<FastLocalTransferPropInfo> OtherProps = [];
}

public static class DtCommon
{
	public const int SPROP_NUMFLAGBITS_NETWORKED = 16;
	public const int SPROP_NUMFLAGBITS = 17;

	public static int NumBitsForCount(int maxElements) {
		int bits = 0;
		while (maxElements > 0) {
			++bits;
			maxElements >>= 1;
		}
		return bits;
	}
}

public enum SendPropType
{
	Int,
	Float,
	Vector,
	VectorXY,
	String,
	Array,
	DataTable,
	GModTable,
	Max
}

public struct DVariant
{
	public float Float;
	public int Int;
	public Vector3 Vector;
	public string? String;
	public object? Data;
	bool UsingReferenceData;

	public SendPropType Type;
}

public struct RecvProxyData
{
	public RecvProp RecvProp;
	public DVariant Value;
	public int Element;
	public int ObjectID;
}
[Flags]
public enum PropFlags
{
	Unsigned = 1 << 0,
	Coord = 1 << 1,
	NoScale = 1 << 2,
	RoundDown = 1 << 3,
	RoundUp = 1 << 4,
	Normal = 1 << 5,
	Exclude = 1 << 6,
	XYZExponent = 1 << 7,
	InsideArray = 1 << 8,
	ProxyAlwaysYes = 1 << 9,
	ChangesOften = 1 << 10,
	IsAVectorElem = 1 << 11,
	Collapsible = 1 << 12,
	CoordMP = 1 << 13,
	CoordMPLowPrecision = 1 << 14,
	CoordMPIntegral = 1 << 15,
	VarInt = Normal,
	NumFlagBitsNetworked = 16,
	EncodedAgainstTickCount = 1 << 16,
	NumFlagBits = 17,
}

public struct ExcludeProp
{
	public string? TableName;
	public string? PropName;
}

public struct BuildHierarchyStruct
{
	public ExcludeProp[]? ExcludeProps;
	public int NumExcludeProps;

	public InlineArrayMaxTotalSendTableProps<SendProp> DataTableProps;
	public int NumDataTableProps;

	public InlineArrayMaxTotalSendTableProps<SendProp> Props;
	public InlineArrayMaxTotalSendTableProps<byte> PropProxyIndices;
	public int NumProps;

	public byte PropProxies;
}

public interface IDataTableProp
{
	ReadOnlySpan<char> GetName();

	bool IsSigned();
	bool IsExcludeProp();
	bool IsInsideArray();
	void SetInsideArray();
	bool SetArrayProp<PropType>(PropType propType) where PropType : IDataTableProp;
	PropType? GetArrayProp<PropType>() where PropType : IDataTableProp;
	int GetOffset();
	void SetOffset(int value);
	int GetNumElements();
	int SetNumElements(int elements);
	SendPropType GetPropType();
	PropFlags GetFlags();
	void SetFlags(PropFlags flags);
	IDataTableBase<PropType>? GetDataTable<PropType>() where PropType : IDataTableProp;
	void SetDataTable<PropType>(IDataTableBase<PropType>? dt) where PropType : IDataTableProp;

	object? GetExtraData();
	void SetExtraData(object? data);
}

public interface IDataTableBase<PropType> where PropType : IDataTableProp
{
	int GetNumProps();
	bool IsInitialized();
	void SetInitialized(bool v);
	PropType GetProp(int index);
}

public static class DataTableHelpers
{
	public static readonly Dictionary<Type, int> FieldSizes = new() {
		{ typeof(sbyte), 1 }, { typeof(byte), 1 }, { typeof(bool), 1 },
		{ typeof(short), 2 }, { typeof(ushort), 2 },
		{ typeof(int), 4 }, { typeof(uint), 4 }, { typeof(float), 4 },
		{ typeof(long), 8 }, { typeof(ulong), 8 }, { typeof(double), 8 },
		{ typeof(QAngle), 12 },
		{ typeof(Vector3), 12 },
		{ typeof(Color), 4 },
	};
	/// <summary>
	/// This is to be called on SendTables and RecvTables to setup array properties to point at their property 
	/// templates and to set the PropFlags.InsideArray flag on the properties inside arrays.
	/// </summary>
	/// <typeparam name="PropType"></typeparam>
	/// <param name="table"></param>
	public static void SetupArrayProps_R<PropType>(this IDataTableBase<PropType> table) where PropType : IDataTableProp {
		if (table.IsInitialized())
			return;

		table.SetInitialized(true);

		for (int i = 0; i < table.GetNumProps(); i++) {
			PropType prop = table.GetProp(i);

			if (prop.GetPropType() == SendPropType.Array) {
				ErrorIfNot(i >= 1, $"SetupArrayProps_R: array prop '{prop.GetName()}' is at index zero.");

				PropType arrayProp = table.GetProp(i - 1);
				arrayProp.SetInsideArray();
				prop.SetArrayProp(arrayProp);
			}
			else if (prop.GetPropType() == SendPropType.DataTable)
				SetupArrayProps_R(prop.GetDataTable<PropType>()!);
		}
	}

	public static void SetDataTableProxyIndices_R(SendTablePrecalc mainTable, SendNode curTable, ref BuildHierarchyStruct bhs) {
		for (int i = 0; i < curTable.GetNumChildren(); i++) {
			SendNode node = curTable.GetChild(i);
			SendProp prop = bhs.DataTableProps[node.DataTableProp];

			if ((prop.GetFlags() & PropFlags.ProxyAlwaysYes) != 0) {
				node.SetDataTableProxyIndex(Constants.DATATABLE_PROXY_INDEX_NOPROXY);
			}
			else {
				node.SetDataTableProxyIndex((ushort)mainTable.GetNumDataTableProxies());
				mainTable.SetNumDataTableProxies(mainTable.GetNumDataTableProxies() + 1);
			}

			SetDataTableProxyIndices_R(mainTable, node, ref bhs);
		}
	}

	public static void SetRecursiveProxyIndices_R(SendTable baseTable, SendNode curTable, ref int curProxyIndex) {
		const int MAX_PROXY_RESULTS = 256;

		if (curProxyIndex >= MAX_PROXY_RESULTS)
			Error($"Too many proxies for datatable {baseTable.GetName()}.");

		curTable.SetRecursiveProxyIndex((ushort)curProxyIndex);
		curProxyIndex++;

		for (int i = 0; i < curTable.GetNumChildren(); i++) {
			SendNode pNode = curTable.GetChild(i);
			SetRecursiveProxyIndices_R(baseTable, pNode, ref curProxyIndex);
		}
	}
}

public class RenamedRecvTableInfo
{
	public static RenamedRecvTableInfo? Head;
	public string OldName;
	public string NewName;
	public RenamedRecvTableInfo? Next;

	public RenamedRecvTableInfo(string oldName, string newName) {
		OldName = oldName;
		NewName = newName;

		Next = Head;
		Head = this;
	}
}
