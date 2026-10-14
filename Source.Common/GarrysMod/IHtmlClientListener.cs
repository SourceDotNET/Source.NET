namespace Source.Common.GarrysMod;

public interface IHtmlClientListener
{
	public enum CursorType
	{
		Default,
		Crosshair,
		Hand,
		IBeam,
		Hourglass,
		SizeEW,
		SizeNS,
		SizeNWSE,
		SizeNESW,
		SizeAll,
		No,
		None,
	}

	void OnAddressChange(ReadOnlySpan<char> address);
	void OnConsoleMessage(ReadOnlySpan<char> message, ReadOnlySpan<char> source, int lineNumber, int severity);
	void OnTitleChange(ReadOnlySpan<char> title);
	void OnTargetUrlChange(ReadOnlySpan<char> url);
	void OnCursorChange(CursorType cursorType);

	void OnLoadStart(ReadOnlySpan<char> address);
	void OnLoadEnd(ReadOnlySpan<char> address);
	void OnDocumentReady(ReadOnlySpan<char> address);

	void OnCreateChildView(ReadOnlySpan<char> sourceUrl, ReadOnlySpan<char> targetUrl, bool isPopup);

	JSValue OnJavaScriptCall(ReadOnlySpan<char> objName, ReadOnlySpan<char> funcName, in JSValue parameters);
}
