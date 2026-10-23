using Source.Common.Engine;


namespace Source.Common;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Property)]
public class NetworkNameAttribute(string name) : Attribute
{
	public readonly string Name = name;
	public string? Table { get; set; }
	public bool NoBase { get; set; }
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class NetworkVarAttribute(string? name = null) : Attribute
{
	public readonly string? Name = name;
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class NetworkTableAttribute(string? name = null) : Attribute
{
	public readonly string? Name = name;
	public bool NoBase { get; set; }
	public NetworkSide Side { get; set; }
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class PredictionTableAttribute : Attribute
{
}

public enum NetworkSide
{
	Both,
	Server,
	Client
}

public sealed class NetworkProp(string member)
{
	public readonly string Member = member;
	public static implicit operator NetworkProp(string member) => new(member);
	public NetEncoding Encoding { get; init; }
	public int Bits { get; init; }
	public PropFlags Flags { get; init; }
	public float Low { get; init; }
	public float High { get; init; }
	public Type? Proxy { get; init; }
	public string? DataTable { get; init; }
	public int Count { get; init; }
	public NetworkSide Side { get; init; }
	public string? Name { get; init; }
	public bool Exclude { get; init; }
}

public sealed class PredictionField(string member)
{
	public readonly string Member = member;
	public static implicit operator PredictionField(string member) => new(member);
	public FieldType Type { get; init; }
	public FieldTypeDescFlags Flags { get; init; }
	public float Tolerance { get; init; }
	public bool Embedded { get; init; }
}
public enum NetEncoding
{
	Auto,
	Int,
	Bool,
	Float,
	Time,
	Time64,
	Vector,
	VectorXY,
	QAngles,
	Angle,
	EHandle,
	ModelIndex,
	String,
	IntWithMinusOneFlag,
	DataTable,
	Array,
	Array2,
	VectorElements,
	PredictableId,
	GModTable,
	List,
	Elements
}
public class ServerClass
{
	public static ServerClass? Head;

	public string NetworkName;
	public SendTable Table;
	public ServerClass? Next;
	public int ClassID;
	public int InstanceBaselineIndex = INetworkStringTable.INVALID_STRING_INDEX;
	public ServerClass(string networkName, SendTable table, string? tableName) {
		NetworkName = networkName;
		Table = table;
		if (tableName != null)
			table.NetTableName = tableName;

		Next = null;
		InstanceBaselineIndex = INetworkStringTable.INVALID_STRING_INDEX;
		if (Head == null) {
			Head = this;
			Next = null;
		}
		else {
			ServerClass? p1 = Head;
			ServerClass? p2 = p1.Next;

			if (stricmp(p1.NetworkName, NetworkName) > 0) {
				Next = Head;
				Head = this;
				p1 = null;
			}

			while (p1 != null) {
				if (p2 == null || stricmp(p2.NetworkName, NetworkName) > 0) {
					Next = p2;
					p1.Next = this;
					break;
				}
				p1 = p2;
				p2 = p2.Next;
			}
		}
		ClassID = -1;
	}
}
