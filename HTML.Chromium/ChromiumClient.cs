using CefSharp;

using Source.Common.GarrysMod;

namespace HTML.Chromium;

public class ChromiumClient : IHtmlClient
{
	readonly ChromiumBrowser Browser;
	readonly IHtmlClientListener Listener;
	bool Closed;

	public ChromiumClient(ChromiumBrowser browser, IHtmlClientListener listener) {
		Browser = browser;
		Listener = listener;
		ChromiumSystem.g_ChromiumSystem.OnClientOpen(this);
	}

	static void PostTask(Action task) => Cef.UIThreadTaskFactory.StartNew(task);

	public void Update() {
		MessageQueue messageQueue = Browser.GetMessageQueue();

		messageQueue.Lock();
		try {
			while (!Closed && !messageQueue.Empty()) {
				MessageQueue.Message msg = messageQueue.Pop();

				switch (msg.Type) {
					case MessageQueue.Type.OnAddressChange:
						Listener.OnAddressChange(msg.String1);
						break;
					case MessageQueue.Type.OnConsoleMessage:
						Listener.OnConsoleMessage(msg.String1, msg.String2, msg.Integer, msg.Integer2);
						break;
					case MessageQueue.Type.OnTitleChange:
						Listener.OnTitleChange(msg.String1);
						break;
					case MessageQueue.Type.OnCursorChange:
						Listener.OnCursorChange((IHtmlClientListener.CursorType)msg.Integer);
						break;
					case MessageQueue.Type.OnLoadStart:
						Listener.OnLoadStart(msg.String1);
						break;
					case MessageQueue.Type.OnLoadEnd:
						Listener.OnDocumentReady(msg.String1);
						Listener.OnLoadEnd(msg.String1);
						break;
					case MessageQueue.Type.OnCreateChildView:
						Listener.OnCreateChildView(msg.String1, msg.String2, msg.Integer != 0);
						break;
					case MessageQueue.Type.OnJavaScriptCall:
						HandleJavaScriptCall(msg.String1, msg.String2, msg.JSParams, msg.Integer, msg.FrameId, msg.Nonce);
						break;
				}
			}
		}
		finally {
			messageQueue.Unlock();
		}
	}

	void HandleJavaScriptCall(string objName, string funcName, in JSValue paramArray, int callbackId, long frameId, string nonce) {
		JSValue retArray = Listener.OnJavaScriptCall(objName, funcName, paramArray);

		if (callbackId == -1)
			return;

		PostTask(() => Browser.ExecuteCallback(callbackId, frameId, nonce, retArray));
	}

	public void Close() {
		PostTask(Browser.Close);

		ChromiumSystem.g_ChromiumSystem.OnClientClose(this);
		Closed = true;
	}

	public void SetSize(int wide, int tall) => PostTask(() => Browser.SetSize(wide, tall));

	public void SetFocused(bool hasFocus) => PostTask(() => Browser.SetFocused(hasFocus));

	public void SendKeyEvent(IHtmlClient.KeyEvent keyEvent) => PostTask(() => Browser.SendKeyEvent(keyEvent));

	public void SendMouseMoveEvent(IHtmlClient.MouseEvent mouseEvent, bool mouseLeave) => PostTask(() => Browser.SendMouseMoveEvent(mouseEvent, mouseLeave));

	public void SendMouseWheelEvent(IHtmlClient.MouseEvent mouseEvent, int deltaX, int deltaY) => PostTask(() => Browser.SendMouseWheelEvent(mouseEvent, deltaX, deltaY));

	public void SendMouseClickEvent(IHtmlClient.MouseEvent mouseEvent, IHtmlClient.MouseButton mouseButton, bool mouseUp, int clickCount) => PostTask(() => Browser.SendMouseClickEvent(mouseEvent, mouseButton, mouseUp, clickCount));

	public void LoadUrl(ReadOnlySpan<char> url) {
		string str = new(url);
		PostTask(() => Browser.LoadUrl(str));
	}

	public void SetHtml(ReadOnlySpan<char> html) {
		string str = new(html);
		PostTask(() => Browser.SetHtml(str));
	}

	public void Refresh() => PostTask(Browser.Refresh);

	public void Stop() => PostTask(Browser.Stop);

	public void GoBack() => PostTask(Browser.GoBack);

	public void GoForward() => PostTask(Browser.GoForward);

	public void RunJavaScript(ReadOnlySpan<char> code) {
		string str = new(code);
		PostTask(() => Browser.RunJavaScript(str));
	}

	public void RegisterJavaScriptFunction(ReadOnlySpan<char> objName, ReadOnlySpan<char> funcName) {
		string obj = new(objName);
		string func = new(funcName);
		PostTask(() => Browser.RegisterJavaScriptFunction(obj, func));
	}

	public void SetOpenLinksExternally(bool openLinksExternally) => PostTask(() => Browser.SetOpenLinksExternally(openLinksExternally));

	public bool LockImageData() {
		ImageData imageData = Browser.GetImageData();

		imageData.Lock();

		if (!imageData.IsDirty()) {
			imageData.Unlock();
			return false;
		}

		return true;
	}

	public void UnlockImageData() {
		ImageData imageData = Browser.GetImageData();
		imageData.SetDirty(false);
		imageData.Unlock();
	}

	public ReadOnlySpan<byte> GetImageData(out int imageWide, out int imageTall) {
		ImageData imageData = Browser.GetImageData();
		return imageData.GetData(out imageWide, out imageTall);
	}
}
