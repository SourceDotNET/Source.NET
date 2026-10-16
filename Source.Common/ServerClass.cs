using Source.Common.Engine;

using System.Reflection;
using System.Runtime.CompilerServices;

namespace Source.Common;

public static class ServerClassRetriever
{
	static readonly Dictionary<Type, ServerClass> ClassList = [];

	public static ServerClass GetOrError(Type t) {
		if (ClassList.TryGetValue(t, out ServerClass? c))
			return c;

		FieldInfo? field = t.GetField(nameof(ServerClass), BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
		if (field == null)
			throw new NullReferenceException(nameof(field));

		c = ClassList[t] = (ServerClass)field.GetValue(null)!;
		return c;
	}
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Field | AttributeTargets.Property)]
public class NetworkNameAttribute(string name) : Attribute
{
	public readonly string Name = name;

	public static string Get(Type t) => t.GetCustomAttribute<NetworkNameAttribute>(false)?.Name ?? throw new NullReferenceException($"{t.Name} is missing a {nameof(NetworkNameAttribute)}");
}

static class NetworkClassSite
{
	public static string FileName(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];
}

public class ServerClass
{
	public static ServerClass? Head;

	readonly record struct Site(string TypeName, string? NetworkName);
	static readonly Dictionary<(string File, int Line), Site> Sites = [];

	public static void RegisterSite(string file, int line, string typeName, string? networkName) => Sites[(file, line)] = new(typeName, networkName);

	static string TakeNetworkName(string callerFile, int callerLine) {
		if (!Sites.Remove((NetworkClassSite.FileName(callerFile), callerLine), out Site site))
			throw new NullReferenceException($"No generated ServerClass registration for {callerFile}:{callerLine}");
		return site.NetworkName ?? throw new NullReferenceException($"{site.TypeName} is missing a {nameof(NetworkNameAttribute)}");
	}

	public string NetworkName;
	public SendTable Table;
	public ServerClass? Next;
	public int ClassID;
	public int InstanceBaselineIndex = INetworkStringTable.INVALID_STRING_INDEX;
	public ServerClass(SendTable table, [CallerArgumentExpression(nameof(table))] string? nameOfTable = null, [CallerFilePath] string callerFile = "", [CallerLineNumber] int callerLine = 0) {
		NetworkName = TakeNetworkName(callerFile, callerLine);
		Table = table;
		if (nameOfTable != null)
			table.NetTableName = nameOfTable;

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
