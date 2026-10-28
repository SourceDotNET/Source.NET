
namespace Source.Common;

public delegate IClientNetworkable CreateClientClassFn(int entNum, int serialNum);
public delegate IClientNetworkable CreateEventFn();

[AttributeUsage(AttributeTargets.Method)]
public class ImplementClientClassFactoryAttribute : Attribute
{

}
public class ClientClass
{
	public static ClientClass? Head;

	public CreateClientClassFn CreateFn;
	public CreateEventFn? CreateEventFn;
	public string? NetworkName;
	public RecvTable? RecvTable;
	public ClientClass? Next;
	public int ClassID;

	public ClientClass(string networkName, RecvTable recvTable, string? tableName, CreateClientClassFn createFn) {
		CreateFn = createFn;
		NetworkName = networkName;
		RecvTable = recvTable;

		if (tableName != null)
			recvTable.NetTableName = tableName;

		Next = Head;
		Head = this;
		ClassID = -1;
	}

	public ClientClass AsEvent<T>() where T : IClientNetworkable, new() {
		T instance = new();
		CreateEventFn = () => instance;
		return this;
	}

	public ReadOnlySpan<char> GetName() => NetworkName;
}

