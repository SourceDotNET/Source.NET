using Source.Common.GarrysMod;

namespace HTML.Stub;

public class StubClient : IHtmlClient
{
	public void Close() { }
	public void SetSize(int wide, int tall) { }
	public void SetFocused(bool hasFocus) { }
	public void SendKeyEvent(IHtmlClient.KeyEvent keyEvent) { }
	public void SendMouseMoveEvent(IHtmlClient.MouseEvent mouseEvent, bool mouseLeave) { }
	public void SendMouseWheelEvent(IHtmlClient.MouseEvent mouseEvent, int deltaX, int deltaY) { }
	public void SendMouseClickEvent(IHtmlClient.MouseEvent mouseEvent, IHtmlClient.MouseButton mouseButton, bool mouseUp, int clickCount) { }
	public void LoadUrl(ReadOnlySpan<char> url) { }
	public void SetHtml(ReadOnlySpan<char> html) { }
	public void Refresh() { }
	public void Stop() { }
	public void GoBack() { }
	public void GoForward() { }
	public void RunJavaScript(ReadOnlySpan<char> code) { }
	public void RegisterJavaScriptFunction(ReadOnlySpan<char> objName, ReadOnlySpan<char> funcName) { }
	public void SetOpenLinksExternally(bool openLinksExternally) { }
	public bool LockImageData() => false;
	public void UnlockImageData() { }
	public ReadOnlySpan<byte> GetImageData(out int imageWide, out int imageTall) {
		imageWide = 0;
		imageTall = 0;
		return default;
	}
}
