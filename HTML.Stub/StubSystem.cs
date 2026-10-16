using Source.Common.GarrysMod;

namespace HTML.Stub;

public class StubSystem : IHtmlSystem
{
	public static readonly IHtmlSystem g_pHtmlSystem = new StubSystem();

	public bool Init(ReadOnlySpan<char> baseDir, IHtmlResourceHandler resourceHandler) => true;
	public void Shutdown() { }
	public void Update() { }
	public IHtmlClient CreateClient(IHtmlClientListener listener) => new StubClient();
	public JSValue CreateArray(ReadOnlySpan<JSValue> values) => new();
	public JSValue CreateString(ReadOnlySpan<char> value) => new();
	public JSValue CreateHashMap(ReadOnlySpan<string> keys, ReadOnlySpan<JSValue> values) => new();
}
