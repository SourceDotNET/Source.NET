using System.Reflection;

namespace Source.Common.GarrysMod;

public interface IHtmlSystem
{
	bool Init(ReadOnlySpan<char> baseDir, IHtmlResourceHandler resourceHandler);
	void Shutdown();

	IHtmlClient CreateClient(IHtmlClientListener listener);

	void Update();

	JSValue CreateArray(ReadOnlySpan<JSValue> values);
	JSValue CreateString(ReadOnlySpan<char> value);
	JSValue CreateHashMap(ReadOnlySpan<string> keys, ReadOnlySpan<JSValue> values);
}

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class HtmlSystemExportAttribute(Type type) : Attribute
{
	public Type Type => type;

	public static IHtmlSystem? Find(Assembly assembly) {
		HtmlSystemExportAttribute? export = assembly.GetCustomAttribute<HtmlSystemExportAttribute>();
		FieldInfo? field = export?.Type.GetField("g_pHtmlSystem", BindingFlags.Public | BindingFlags.Static);
		return field?.GetValue(null) as IHtmlSystem;
	}
}
