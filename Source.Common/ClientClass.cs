using System.Reflection;
using System.Runtime.CompilerServices;

namespace Source.Common;

public delegate IClientNetworkable CreateClientClassFn(int entNum, int serialNum);
public delegate IClientNetworkable CreateEventFn();

public static class ClientClassRetriever
{
	static readonly Dictionary<Type, ClientClass> ClassList = [];

	public static ClientClass GetOrError(Type t) {
		if (ClassList.TryGetValue(t, out ClientClass? c))
			return c;

		FieldInfo? field = t.GetField(nameof(ClientClass), BindingFlags.Static | BindingFlags.Public);
		if (field == null)
			throw new NullReferenceException(nameof(field));

		c = ClassList[t] = (ClientClass)field.GetValue(null)!;
		return c;
	}
}

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

	readonly record struct Site(string TypeName, string? NetworkName, CreateClientClassFn? CreateFn);
	static readonly Dictionary<(string File, int Line), Site> Sites = [];

	public static void RegisterSite(string file, int line, string typeName, string? networkName, CreateClientClassFn? createFn) => Sites[(file, line)] = new(typeName, networkName, createFn);

	static Site TakeSite(string callerFile, int callerLine) {
		if (!Sites.Remove((NetworkClassSite.FileName(callerFile), callerLine), out Site site))
			throw new NullReferenceException($"No generated ClientClass registration for {callerFile}:{callerLine}");
		return site;
	}

	static CreateClientClassFn GetCreateFn(in Site site) {
		string typeName = site.TypeName;
		return site.CreateFn ?? ((_, _) => throw new NotImplementedException($"ClientClass did not define how to create type '{typeName}'"));
	}

	static string GetNetworkName(in Site site) => site.NetworkName ?? throw new NullReferenceException($"{site.TypeName} is missing a {nameof(NetworkNameAttribute)}");

	public ClientClass(RecvTable recvTable, [CallerArgumentExpression(nameof(recvTable))] string? nameOfTable = null, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) {
		Site site = TakeSite(callerFile, callerLine);
		CreateFn = GetCreateFn(in site);

		NetworkName = GetNetworkName(in site);
		RecvTable = recvTable;

		if (nameOfTable != null)
			recvTable.NetTableName = nameOfTable;

		Next = Head;
		Head = this;
		ClassID = -1;
	}
	public ClientClass(CreateClientClassFn? createFn, CreateEventFn? createEventFn, RecvTable recvTable, [CallerArgumentExpression(nameof(recvTable))] string? nameOfTable = null, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) {
		Site site = TakeSite(callerFile, callerLine);
		createFn ??= GetCreateFn(in site);

		CreateFn = createFn;
		CreateEventFn = createEventFn;
		NetworkName = GetNetworkName(in site);
		RecvTable = recvTable;

		if (nameOfTable != null)
			recvTable.NetTableName = nameOfTable;

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


[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ImplementClientClassAttribute : Attribute
{
	public required string ClientClassName;
	public required string RecvTable;
	public required string ServerClassName;
}
