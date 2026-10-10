using CommunityToolkit.HighPerformance;

using Source.Common.Bitbuffers;

using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Source.Common;

public class StandardRecvProxies
{
	public static readonly StandardRecvProxies g_StandardRecvProxies = new();

	public StandardRecvProxies(){
		Int32ToInt8 = RecvProxy_Int32ToInt8;
		Int32ToInt16 = RecvProxy_Int32ToInt16;
		Int32ToInt32 = RecvProxy_Int32ToInt32;
		FloatToFloat = RecvProxy_FloatToFloat;
		VectorToVector = RecvProxy_VectorToVector;
	}

	public RecvVarProxyFn Int32ToInt8;
	public RecvVarProxyFn Int32ToInt16;
	public RecvVarProxyFn Int32ToInt32;
	public RecvVarProxyFn FloatToFloat;
	public RecvVarProxyFn VectorToVector;
}

public delegate void ArrayLengthRecvProxyFn(object instance, int objectID, int currentArrayLength);
public static class RecvPropHelpers
{
	public static void RecvProxy_FloatToFloat(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		field.SetValue(instance, data.Value.Float);
	}

	public static void RecvProxy_VectorToVector(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		field.SetValue(instance, data.Value.Vector);
	}

	public static void RecvProxy_VectorXYToVectorXY(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		Vector3 vec = field.GetValue<Vector3>(instance);
		vec.X = data.Value.Vector[0];
		vec.Y = data.Value.Vector[1];
		field.SetValue(instance, vec);
	}

	// Specialized proxy for Garry's Mod's Time64
	public static void RecvProxy_VectorXYToDouble(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		Span<float> cast = stackalloc float[2];
		cast[0] = data.Value.Vector.X;
		cast[1] = data.Value.Vector.Y;
		field.SetValue(instance, cast.Cast<float, double>()[0]);
	}

	public static void DataTableRecvProxy_StaticDataTable(RecvProp prop, out object? outInstance, object? instance, IFieldAccessor fieldInfo, int objectID) {
		outInstance = instance;
	}

	public static void DataTableRecvProxy_PointerDataTable(RecvProp prop, out object? outInstance, object? instance, IFieldAccessor fieldInfo, int objectID) {
		outInstance = fieldInfo?.GetValue<object>(instance ?? throw new NullReferenceException());
	}

	public static void RecvProxy_Int32ToInt8(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => field.SetValue(instance, unchecked((sbyte)data.Value.Int));
	public static void RecvProxy_Int32ToInt16(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => field.SetValue(instance, unchecked((short)data.Value.Int));
	public static void RecvProxy_Int32ToInt32(ref readonly RecvProxyData data, object instance, IFieldAccessor field) => field.SetValue(instance, unchecked(data.Value.Int));
	public static void RecvProxy_StringToString(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		if (data.RecvProp.StringBufferSize <= 0)
			return;
		// TODO: Speed this up with something in IFieldAccessor which can return a span?
		Span<char> strOut = stackalloc char[data.RecvProp.StringBufferSize];
		data.Value.String?.CopyTo(strOut);
		field.CopyFrom(instance, strOut.SliceNullTerminatedString());
	}

	public static void RecvProxy_IntToEHandle(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		BaseHandle ehandle = field.GetValue<BaseHandle>(instance);

		if (data.Value.Int == Constants.INVALID_NETWORKED_EHANDLE_VALUE) {
			ehandle.Index = (uint)Constants.INVALID_EHANDLE_INDEX;
		}
		else {
			int entity = data.Value.Int & ((1 << Constants.MAX_EDICT_BITS) - 1);
			int serialNum = data.Value.Int >> Constants.MAX_EDICT_BITS;

			ehandle.Init(entity, serialNum);
		}

		field.SetValue(instance, ehandle);
	}
	// field may be null for UtlVector/list element templates (mirrors C++ RecvPropEHandle(NULL, 0, 0)).
	public static RecvProp RecvPropEHandle(IFieldAccessor? field = null, RecvVarProxyFn? proxyFn = null) {
		proxyFn ??= RecvProxy_IntToEHandle;
		return RecvPropInt(field, 0, proxyFn);
	}
	public static RecvProp RecvPropFloat(IFieldAccessor field, PropFlags flags = 0, RecvVarProxyFn? proxyFn = null) {
		RecvProp ret = new();
		proxyFn ??= RecvProxy_FloatToFloat;

		ret.FieldInfo = field;
		ret.RecvType = SendPropType.Float;
		ret.Flags = flags;
		ret.SetProxyFn(proxyFn);

		return ret;
	}
	public static RecvProp RecvPropQAngles(IFieldAccessor field, PropFlags flags = 0, RecvVarProxyFn? proxyFn = null)
		=> RecvPropVector(field, flags, proxyFn);
	public static RecvProp RecvPropVector(IFieldAccessor field, PropFlags flags = 0, RecvVarProxyFn? proxyFn = null) {
		RecvProp ret = new();
		proxyFn ??= RecvProxy_VectorToVector;

		ret.FieldInfo = field;
		ret.RecvType = SendPropType.Vector;
		ret.Flags = flags;
		ret.SetProxyFn(proxyFn);

		return ret;
	}
	public static RecvProp RecvPropVectorXY(IFieldAccessor field, PropFlags flags = 0, RecvVarProxyFn? proxyFn = null) {
		RecvProp ret = new();
		proxyFn ??= RecvProxy_VectorXYToVectorXY;

		ret.FieldInfo = field;
		ret.RecvType = SendPropType.VectorXY;
		ret.Flags = flags;
		ret.SetProxyFn(proxyFn);

		return ret;
	}
	public static RecvProp RecvPropInt(string fieldName, PropFlags flags = 0, RecvVarProxyFn? proxyFn = null, int sizeOfVar = -1)
		=> RecvPropInt(fieldName, null, flags, proxyFn, sizeOfVar);
	public static RecvProp RecvPropInt(IFieldAccessor field, PropFlags flags = 0, RecvVarProxyFn? proxyFn = null, int sizeOfVar = -1)
		=> RecvPropInt(null, field, flags, proxyFn, sizeOfVar);
	public static RecvProp RecvPropInt(string? nameOverride, IFieldAccessor? field, PropFlags flags = 0, RecvVarProxyFn? proxyFn = null, int sizeOfVar = -1) {
		RecvProp ret = new();

		sizeOfVar = sizeOfVar == -1 ? field == null ? -1 : DataTableHelpers.FieldSizes.TryGetValue(field.FieldType.IsEnum ? Enum.GetUnderlyingType(field.FieldType) : field.FieldType, out int v) ? v : -1 : sizeOfVar;
		if (proxyFn == null) {
			if (sizeOfVar == 1)
				proxyFn = RecvProxy_Int32ToInt8;
			else if (sizeOfVar == 2)
				proxyFn = RecvProxy_Int32ToInt16;
			else if (sizeOfVar == 4)
				proxyFn = RecvProxy_Int32ToInt32;
			else {
				AssertMsg(false, $"RecvPropInt var has invalid size {(sizeOfVar == -1 ? "UNDEFINED" : sizeOfVar)}");
				proxyFn = RecvProxy_Int32ToInt8;
			}
		}

		ret.FieldInfo = field;
		ret.NameOverride = nameOverride;
		ret.RecvType = SendPropType.Int;
		ret.Flags = flags;
		ret.SetProxyFn(proxyFn);

		return ret;
	}

	public static RecvProp RecvPropArray(DynamicArrayAccessor accessor) {
		return RecvPropVariableLengthArray(null, accessor);
	}

	private static RecvProp RecvPropVariableLengthArray(ArrayLengthRecvProxyFn? fn, DynamicArrayAccessor accessor) {
		return InternalRecvPropArray(accessor.Length, accessor.NetworkName, null);
	}

	public static RecvProp RecvPropString(IFieldAccessor field, int bufferSize = -1, PropFlags flags = 0, RecvVarProxyFn? proxyFn = null) {
		RecvProp ret = new();
		proxyFn ??= RecvProxy_StringToString;
		if (bufferSize == -1) {
			// Try to see if this is inline. If it is then we can set the explicit size ourselves
			var inlineArrayAttr = field.FieldType.GetCustomAttribute<InlineArrayAttribute>();
			if (inlineArrayAttr != null)
				bufferSize = inlineArrayAttr.Length;
		}

		ret.FieldInfo = field;
		ret.RecvType = SendPropType.String;
		ret.Flags = flags;
		ret.StringBufferSize = bufferSize;
		ret.SetProxyFn(proxyFn);

		return ret;
	}

	public static RecvProp RecvPropList<T>(DynamicArrayAccessor field, ResizeVectorFn fn,RecvProp arrayProp) where T : new() {
		EnsureCapacityFn ensureFn = arrayProp.RecvType == SendPropType.DataTable
			? (_, list, capacity) => {
				var realList = (IList<T>)list;
				while (realList.Count < capacity)
					realList.Add(new T());
			}
			: (_, list, capacity) => ((List<T>)list).EnsureCapacity(capacity);
		return RecvPropList(field, fn, ensureFn, field.Info.MaxLength, arrayProp);
	}
	public static RecvProp RecvPropList(IFieldAccessor field, ResizeVectorFn fn, EnsureCapacityFn ensureFn, int maxElements, RecvProp arrayProp) {
		RecvProp ret = new();

		Assert(maxElements <= Constants.MAX_ARRAY_ELEMENTS);

		ret.RecvType = SendPropType.DataTable;
		ret.FieldInfo = field;
		ret.SetOffset(0);
		ret.SetDataTableProxyFn(DataTableRecvProxy_StaticDataTable);

		RecvProp[] props = new RecvProp[maxElements + 1];

		RecvPropExtra_UtlVector extraData = new RecvPropExtra_UtlVector();

		extraData.MaxElements = maxElements;
		extraData.ResizeFn = fn;
		extraData.EnsureCapacityFn = ensureFn;
		extraData.FieldInfo = field;
		
		if (arrayProp.RecvType == SendPropType.DataTable)
			extraData.DataTableProxyFn = arrayProp.GetDataTableProxyFn();
		else
			extraData.ProxyFn = arrayProp.GetProxyFn();

		RecvProp lengthProp = new RecvProp();
		lengthProp = RecvPropInt($"lengthprop{maxElements}", 0, RecvProxy_UtlVectorLength);
		lengthProp.SetExtraData(extraData);

		string lengthProxyTableName = DtUtlVectorCommon.AllocateUniqueDataTableName(false, $"_LPT_{((field as DynamicAccessor)?.NetworkName ?? field.Name)}_{maxElements}");
		RecvTable lengthTable = new RecvTable(lengthProxyTableName, [lengthProp]);
		props[0] = RecvPropDataTable("lengthproxy", null, lengthTable, 0, DataTableRecvProxy_LengthProxy);
		props[0].SetExtraData(extraData);

		for (int i = 1; i < maxElements + 1; i++) {
			props[i] = arrayProp.Copy();
			props[i].FieldInfo = field;
			props[i].SetOffset(0); 
			props[i].NameOverride = ClientElementNames[i - 1];
			var indexedData = extraData.Clone();
			indexedData.Index = i - 1;
			props[i].SetExtraData(indexedData);
												
			if (arrayProp.RecvType == SendPropType.DataTable) {
				props[i].SetDataTableProxyFn(RecvProxy_UtlVectorElement_DataTable);
			}
			else {
				props[i].SetProxyFn(RecvProxy_UtlVectorElement);
			}
		}

		RecvTable table = new RecvTable(DtUtlVectorCommon.AllocateUniqueDataTableName(false, $"_ST_{((field as DynamicAccessor)?.NetworkName ?? field.Name)}_{maxElements}"), props); 
		ret.SetDataTable(table);
		return ret;
	}

	private static void DataTableRecvProxy_LengthProxy(RecvProp prop, out object? outInstance, object? instance, IFieldAccessor fieldInfo, int objectID) {
		RecvPropExtra_UtlVector extra = (RecvPropExtra_UtlVector)prop.GetExtraData()!;
		extra.EnsureCapacityFn(instance, extra.FieldInfo.GetValue<object>(instance!), extra.MaxElements);

		outInstance = instance;
	}

	private static void RecvProxy_UtlVectorElement(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		RecvPropExtra_UtlVector extra = (RecvPropExtra_UtlVector)data.RecvProp.GetExtraData()!;

		int iElement = extra.Index;

		System.Collections.ICollection utlVec = extra.FieldInfo.GetValue<System.Collections.ICollection>(instance);
		if (iElement >= utlVec.Count)
			return;

		// Call through to the proxy they passed in, making pStruct=the CUtlVector.
		// Note: there should be space here as long as the element is < the max # elements
		// that we ensured capacity for in DataTableRecvProxy_LengthProxy.
		extra.ProxyFn(in data, instance, ((IFieldAccessorIndexable)extra.FieldInfo).AtIndex(iElement));
	}

	private static void RecvProxy_UtlVectorElement_DataTable(RecvProp prop, out object? outInstance, object? instance, IFieldAccessor fieldInfo, int objectID) {
		RecvPropExtra_UtlVector extra = (RecvPropExtra_UtlVector)prop.GetExtraData()!;

		int iElement = extra.Index;
		Assert(iElement < extra.MaxElements);

		if (fieldInfo is DynamicArrayAccessor dynamicArrayAccessor) {
			extra.DataTableProxyFn(prop, out outInstance, dynamicArrayAccessor.AtIndex(iElement).GetValue<object>(instance!), null, objectID);
		}
		else
			throw new Exception("Somehow got non-DynamicArrayAccessor here - re-evaluate.");
	}

	private static void RecvProxy_UtlVectorLength(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
		RecvPropExtra_UtlVector extra = (RecvPropExtra_UtlVector)data.RecvProp.GetExtraData()!;
		extra.ResizeFn(instance, extra.FieldInfo.GetValue<object>(instance), data.Value.Int);
	}

	static readonly string[] ClientElementNames = GeneratePaddedStrings(Constants.MAX_ARRAY_ELEMENTS);

	/// <summary>
	/// Requires a variable template directly above the call!
	/// </summary>
	public static RecvProp RecvPropArray2(ArrayLengthRecvProxyFn arrayLengthProxy, int elementCount, ReadOnlySpan<char> arrayName) {
		return InternalRecvPropArray(elementCount, arrayName, arrayLengthProxy);
	}

	public static RecvProp InternalRecvPropArray(int elementCount, ReadOnlySpan<char> name, ArrayLengthRecvProxyFn? arrayLengthFn = null) {
		RecvProp ret = new();

		ret.InitArray(elementCount);
		ret.NameOverride = new(name);
		ret.SetArrayLengthProxy(arrayLengthFn);

		return ret;
	}

	public static RecvProp RecvPropArray3(DynamicArrayAccessor field, RecvProp arrayProp, DataTableRecvVarProxyFn? varProxy = null) {
		varProxy ??= DataTableRecvProxy_StaticDataTable;

		RecvProp ret = new();
		int elements = field.Length;

		Assert(elements != -1);
		Assert(elements <= Constants.MAX_ARRAY_ELEMENTS);

		ret.FieldInfo = field;
		ret.RecvType = SendPropType.DataTable;
		ret.SetDataTableProxyFn(varProxy);

		RecvProp[] props = new RecvProp[elements];

		for (int i = 0; i < elements; i++) {
			props[i] = arrayProp.Copy();
			props[i].FieldInfo = new DynamicArrayIndexAccessor(field, i);
			props[i].NameOverride = ClientElementNames[i];
			props[i].SetParentArrayPropName(((field as DynamicAccessor)?.NetworkName ?? field.Name));
		}

		RecvTable table = new RecvTable(((field as DynamicAccessor)?.NetworkName ?? field.Name), props);
		ret.SetDataTable(table);

		return ret;
	}
	public static RecvProp RecvPropDataTable(string name, IFieldAccessor field, RecvTable table, PropFlags flags = 0, DataTableRecvVarProxyFn? proxyFn = null) {
		RecvProp ret = new();
		proxyFn ??= DataTableRecvProxy_StaticDataTable;
		ret.NameOverride = name;
		ret.FieldInfo = field;
		ret.RecvType = SendPropType.DataTable;
		ret.Flags = flags;
		ret.SetDataTableProxyFn(proxyFn);
		ret.SetDataTable(table);
		return ret;
	}
	public static RecvProp RecvPropDataTable(string name, RecvTable table, PropFlags flags = 0, DataTableRecvVarProxyFn? proxyFn = null) {
		RecvProp ret = new();
		proxyFn ??= DataTableRecvProxy_StaticDataTable;
		ret.NameOverride = name;
		ret.RecvType = SendPropType.DataTable;
		ret.Flags = flags;
		ret.SetDataTableProxyFn(proxyFn);
		ret.SetDataTable(table);
		return ret;
	}

	public static DataTableRecvVarProxyFn RECV_GET_OBJECT_AT_FIELD(IFieldAccessor field) {
		return (RecvProp prop, out object? outInstance, object? instance, IFieldAccessor fieldInfo, int objectID) => {
			outInstance = field.GetValue<object>(instance);
		};
	}

	public static RecvProp RecvPropGModTable(IFieldAccessor field) {
		RecvProp ret = new();

		ret.RecvType = SendPropType.GModTable;
		ret.FieldInfo = field;
		ret.SetProxyFn(static (ref readonly data, instance, field) => { });

		return ret;
	}
}


[DebuggerDisplay("RecvProp<{RecvType}> {GetName()} [{Flags,ac}]")]
public class RecvProp : IDataTableProp
{
	public IFieldAccessor FieldInfo;
	public SendPropType RecvType;
	public PropFlags Flags;
	public int StringBufferSize;

	public RecvProp() {

	}
	public string? NameOverride;
	bool InsideArray;
	RecvProp? ArrayProp;
	RecvVarProxyFn ProxyFn;
	DataTableRecvVarProxyFn DataTableProxyFn;
	RecvTable? DataTable;
	int Offset;
	int Elements = 1;
	object? ExtraData;

	public int GetOffset() => Offset;
	public void SetOffset(int value) => Offset = value;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public T GetValue<T>(object instance) => FieldInfo.GetValue<T>(instance);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void SetValue<T>(object instance, in T value) => FieldInfo.SetValue(instance, in value);

	public RecvVarProxyFn GetProxyFn() => ProxyFn;
	public void SetProxyFn(RecvVarProxyFn fn) => ProxyFn = fn;
	public DataTableRecvVarProxyFn GetDataTableProxyFn() => DataTableProxyFn;
	public void SetDataTableProxyFn(DataTableRecvVarProxyFn fn) => DataTableProxyFn = fn;

	public ReadOnlySpan<char> GetName() => NameOverride ?? (FieldInfo as DynamicAccessor)?.NetworkName ?? FieldInfo?.Name ?? "<??UNNAMED??>";

	public bool IsSigned() => (Flags & PropFlags.Unsigned) == 0;
	public bool IsExcludeProp() => (Flags & PropFlags.Exclude) != 0;
	public bool IsInsideArray() => InsideArray;
	public void SetInsideArray() => InsideArray = true;
	public bool SetArrayProp<PropType>(PropType propType) where PropType : IDataTableProp => (ArrayProp = (propType is RecvProp rp) ? rp : throw new InvalidCastException()) != null;
	public PropType? GetArrayProp<PropType>() where PropType : IDataTableProp => ArrayProp == null ? default : ArrayProp is PropType pt ? pt : throw new InvalidCastException();
	public RecvProp? GetArrayProp() => ArrayProp;
	public int GetNumElements() => Elements;
	public int SetNumElements(int elements) => Elements = elements;
	public SendPropType GetPropType() => RecvType;
	public PropFlags GetFlags() => Flags;
	public void SetFlags(PropFlags flags) => Flags = flags;

	public IDataTableBase<PropType>? GetDataTable<PropType>() where PropType : IDataTableProp
		=> DataTable == null ? null : DataTable is IDataTableBase<PropType> dt ? dt : throw new InvalidCastException();

	public void SetDataTable<PropType>(IDataTableBase<PropType>? dt) where PropType : IDataTableProp
		=> DataTable = dt == null ? null : dt is RecvTable rt ? rt : throw new InvalidCastException();

	public RecvProp Copy() => new() {
		FieldInfo = FieldInfo,
		RecvType = RecvType,
		Flags = Flags,
		StringBufferSize = StringBufferSize,
		NameOverride = NameOverride,
		InsideArray = InsideArray,
		ArrayProp = ArrayProp,
		ProxyFn = ProxyFn,
		DataTableProxyFn = DataTableProxyFn,
		DataTable = DataTable,
		Offset = Offset,
		ParentArrayPropName = ParentArrayPropName,
		Elements = Elements,
		arrayLengthProxyFn = arrayLengthProxyFn,
		ExtraData = ExtraData
	};

	string? ParentArrayPropName;
	public string? GetParentArrayPropName() => ParentArrayPropName;
	public void SetParentArrayPropName(string name) => ParentArrayPropName = name;

	ArrayLengthRecvProxyFn? arrayLengthProxyFn;
	public ArrayLengthRecvProxyFn? GetArrayLengthProxy() => arrayLengthProxyFn; 
	public void SetArrayLengthProxy(ArrayLengthRecvProxyFn? arrayLengthFn) {
		arrayLengthProxyFn = arrayLengthFn;
	}

	internal void InitArray(int elementCount) {
		RecvType = SendPropType.Array;
		Elements = elementCount;
	}

	public object? GetExtraData() => ExtraData;
	public void SetExtraData(object? data) => ExtraData = data;
}

public class RecvDecoder
{
	public RecvDecoder() {

	}

	public ReadOnlySpan<char> GetName() {
		throw new NotImplementedException();
	}
	public SendTable? GetSendTable() => Precalc.GetSendTable();
	public RecvTable? GetRecvTable() => Table;
	public int GetNumProps() => Props.Count;
	public RecvProp? GetProp(int i) => (uint)i < (uint)GetNumProps() ? Props[i] : null;
	public SendProp GetSendProp(int i) => Precalc.GetProp(i);

	public void DumpNames() {
		int i = 0;
		foreach (var propname in Props.Where(x => x != null).Select(x => new string(x.GetName())))
			Msg($"Prop #{i++}: {propname}\n");
	}

	public int GetNumDatatableProps() => DataTableProps.Count;
	public RecvProp GetDatatableProp(int i) => DataTableProps[i];


	public RecvTable Table;
	public ClientSendTable ClientSendTable;

	public readonly SendTablePrecalc Precalc = new();

	public readonly List<RecvProp> Props = [];
	public readonly List<RecvProp> DataTableProps = [];
}

public class SendTablePrecalc
{
#if GMOD_DLL
	public SendProp? GMODTableProp;
#endif
	public SendTablePrecalc() {

	}

	public bool SetupFlatPropertyArray() {
		SendTable? table = GetSendTable();

		table!.SetupArrayProps_R();

		ExcludeProp[] excludeProps = new ExcludeProp[Constants.MAX_EXCLUDE_PROPS];
		int numExcludeProps = 0;
		if (!SendTable.GetPropsExcluded(table!, excludeProps.AsSpan(), ref numExcludeProps, Constants.MAX_EXCLUDE_PROPS))
			return false;

		// Now build the hierarchy.
		BuildHierarchyStruct bhs = default;
		bhs.ExcludeProps = excludeProps;
		bhs.NumExcludeProps = numExcludeProps;
		bhs.NumProps = bhs.NumDataTableProps = 0;
		bhs.PropProxies = 0;
		SendTable.BuildHierarchy(GetRootNode(), table, ref bhs);
		SendTable.SortByPriority(ref bhs);

		Props.Clear(); 
		Props.AddRange(bhs.Props[..bhs.NumProps]);
#if GMOD_DLL
		GMODTableProp = Props.FirstOrDefault(static p => p.Type == SendPropType.GModTable);
#endif

		DataTableProps.Clear(); DataTableProps.AddRange(bhs.DataTableProps[..bhs.NumDataTableProps]);
		PropProxyIndices.Clear(); PropProxyIndices.AddRange(bhs.PropProxyIndices[..bhs.NumProps]);

		SetNumDataTableProxies(0);
		DataTableHelpers.SetDataTableProxyIndices_R(this, GetRootNode(), ref bhs);

		int proxyIndices = 0;
		DataTableHelpers.SetRecursiveProxyIndices_R(table, GetRootNode(), ref proxyIndices);

		SendTable.GenerateProxyPaths(this, proxyIndices);
		return true;
	}

	public int GetNumProps() => Props.Count;
	public SendProp? GetProp(int i) => i < 0 ? null : i >= Props.Count ? null : Props[i];
	public int GetNumDatatableProps() => DataTableProps.Count;

	public SendProp GetDatatableProp(int i) => DataTableProps[i];

	public SendTable? GetSendTable() => SendTable;
	public SendNode GetRootNode() => Root;

	public int GetNumDataTableProxies() => DataTableProxies;

	public void SetNumDataTableProxies(int count) {
		DataTableProxies = count;
	}

	public struct ProxyPathEntry
	{
		public ushort DataTableProp;
		public ushort Proxy;
	}

	public struct ProxyPath
	{
		public ushort FirstEntry;
		public ushort Entries;
	}


	public readonly List<ProxyPathEntry> ProxyPathEntries = [];
	public readonly List<ProxyPath> ProxyPaths = [];
	public readonly List<SendProp> Props = [];
	public readonly List<byte> PropProxyIndices = [];
	public readonly List<SendProp> DataTableProps = [];
	public readonly SendNode Root = new();
	public SendTable SendTable;
	// TODO: public DTISendTable DTITable;
	public readonly FastLocalTransferInfo FastLocalTransfer = new();
	public int DataTableProxies;
	public readonly Dictionary<IFieldAccessor, ushort> PropAccessorToIndexMap = [];
}

public class ClientSendProp
{
	private string? TableName;

	public ReadOnlySpan<char> GetTableName() => TableName;
	public void SetTableName(ReadOnlySpan<char> str) => TableName = new(str);
}
public class ClientSendTable
{
	public int GetNumProps() => SendTable?.Props?.Length ?? 0;
	public ClientSendProp GetClientProp(int i) => Props[i];
	public ReadOnlySpan<char> GetName() => SendTable == null ? null : SendTable.GetName();
	public SendTable? GetSendTable() => SendTable;

	public SendTable SendTable = new();
	public readonly List<ClientSendProp> Props = [];
}
public class SendNode
{
	public SendNode() {
		DataTableProp = -1;
		Table = null;

		FirstRecursiveProp = RecursiveProps = 0;
		DataTableProxyIndex = Constants.DATATABLE_PROXY_INDEX_INVALID;
	}

	public int GetNumChildren() => Children.Count;
	public SendNode GetChild(int i) => Children[i];
	public bool IsPropInRecursiveProps(int i) {
		int index = i - (int)FirstRecursiveProp;
		return index >= 0 && index < RecursiveProps;
	}
	public ushort GetDataTableProxyIndex() {
		Assert(DataTableProxyIndex != Constants.DATATABLE_PROXY_INDEX_INVALID);
		return DataTableProxyIndex;
	}
	public void SetDataTableProxyIndex(ushort val) {
		DataTableProxyIndex = val;
	}
	public ushort GetRecursiveProxyIndex() {
		return RecursiveProxyIndex;
	}
	public void SetRecursiveProxyIndex(ushort val) {
		RecursiveProxyIndex = val;
	}

	public readonly List<SendNode> Children = [];
	public short DataTableProp;

	public SendTable? Table;
	public ushort FirstRecursiveProp;
	public ushort RecursiveProps;
	public ushort DataTableProxyIndex;
	public ushort RecursiveProxyIndex;
}

[DebuggerDisplay("RecvTable '{NetTableName}'")]
public class RecvTable : IEnumerable<RecvProp>, IDataTableBase<RecvProp>
{
	public RecvTable() { }
	public RecvTable(RecvProp[] props) {
		Props = props;
	}
	public RecvTable(RecvTable parent, IEnumerable<RecvProp> props) {
		Props = new RecvProp[props.Count() + 1];
		Props[0] = RecvPropDataTable("baseclass", parent, 0, DataTableRecvProxy_StaticDataTable);
		int i = 1;
		foreach (var prop in props)
			Props[i++] = prop;
	}
	public RecvTable(string name, RecvProp[] props) {
		NetTableName = name;
		Props = props;
	}
	public RecvProp[]? Props;
	public RecvDecoder? Decoder;
	public string? NetTableName;

	bool Initialized;
	bool InMainList;

	public IEnumerator<RecvProp> GetEnumerator() {
		return ((IEnumerable<RecvProp>)Props).GetEnumerator();
	}

	public ReadOnlySpan<char> GetName() => NetTableName;

	public int GetNumProps() => Props?.Length ?? 0;

	public RecvProp GetProp(int index) => Props![index];

	public bool IsInitialized() => Initialized;

	public void SetInitialized(bool initialized) => Initialized = initialized;

	IEnumerator IEnumerable.GetEnumerator() {
		return Props.GetEnumerator();
	}

	public bool IsInMainList() {
		return InMainList;
	}
	public void SetInMainList(bool inList) {
		InMainList = inList;
	}

	public static void MergeDeltas(RecvTable? recvTable, bf_read oldBuf, bf_read newBuf, bf_write outBuf) {
		throw new NotImplementedException();
	}
}

public delegate void RecvVarProxyFn(ref readonly RecvProxyData data, object instance, IFieldAccessor field);
public delegate void DataTableRecvVarProxyFn(RecvProp prop, out object? outInstance, object? instance, IFieldAccessor fieldInfo, int objectID);
