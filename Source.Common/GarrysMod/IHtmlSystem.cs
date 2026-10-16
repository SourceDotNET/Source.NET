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
