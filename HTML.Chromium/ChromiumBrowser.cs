using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;

using ChromiumProcess;

using Source.Common.GarrysMod;

using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

using Range = CefSharp.Structs.Range;
using Size = CefSharp.Structs.Size;

namespace HTML.Chromium;

public class ChromiumBrowser : ILifeSpanHandler, ILoadHandler, IDisplayHandler, IRequestHandler, IContextMenuHandler, IDialogHandler, IJsDialogHandler
{
	static readonly JsonDocumentOptions MessageOptions = new() { MaxDepth = 256 };

	static readonly string[] BlockedJavaScriptHosts = [
		"steampowered",
		"steamcommunity",
		"valvesoftware",
		"steamgames",
		"discord.",
		"discordapp",
		"watchanimeattheoffice",
		"bigbeans.solutions",
		"dis.gd",
		"10.",
		"172.16.",
		"192.168.",
		"127.",
		"localhost",
	];

	static bool CefValueToJSValue(out JSValue outValue, JsonElement inValue, int depth = 0) {
		outValue = default;

		if (depth > 16)
			return false;

		if (inValue.ValueKind != JsonValueKind.Array || inValue.GetArrayLength() < 2)
			return true;

		JsonElement value = inValue[1];
		switch (inValue[0].GetInt32()) {
			case 1:
				outValue = new JSValue(value.GetBoolean());
				break;
			case 2:
				if (value.ValueKind == JsonValueKind.String) {
					outValue = new JSValue(value.GetString() switch {
						"Infinity" => double.PositiveInfinity,
						"-Infinity" => double.NegativeInfinity,
						_ => double.NaN
					});
				}
				else
					outValue = new JSValue(value.GetDouble());
				break;
			case 3:
				outValue = JSString.Create(value.GetString() ?? "");
				break;
			case 4: {
					JSValue[] outList = new JSValue[value.GetArrayLength()];
					int i = 0;
					foreach (JsonElement entry in value.EnumerateArray()) {
						if (!CefValueToJSValue(out outList[i], entry, depth + 1))
							return false;
						i++;
					}
					outValue = JSArray.Create(outList);
					break;
				}
			case 5: {
					Dictionary<string, JSValue> outMap = new(value.GetArrayLength());
					foreach (JsonElement entry in value.EnumerateArray()) {
						if (!CefValueToJSValue(out JSValue mapValue, entry[1], depth + 1))
							return false;
						outMap.TryAdd(entry[0].GetString() ?? "", mapValue);
					}
					outValue = JSHashMap.Create(outMap);
					break;
				}
		}

		return true;
	}

	static bool CefListToJSValues(out JSValue[] outList, JsonElement inList) {
		outList = new JSValue[inList.GetArrayLength()];

		int i = 0;
		foreach (JsonElement entry in inList.EnumerateArray()) {
			if (!CefValueToJSValue(out outList[i], entry))
				return false;
			i++;
		}

		return true;
	}

	static bool JSValueToCefValue(Utf8JsonWriter outValue, in JSValue inValue, int depth = 0) {
		if (depth > 16)
			return false;

		switch (inValue.GetJSType()) {
			case JSValue.Type.Undefined:
				outValue.WriteNumberValue(0);
				break;
			case JSValue.Type.Bool:
				outValue.WriteStartArray();
				outValue.WriteNumberValue(1);
				outValue.WriteBooleanValue(inValue.GetBool());
				outValue.WriteEndArray();
				break;
			case JSValue.Type.Number: {
					double number = inValue.GetNumber();
					outValue.WriteStartArray();
					outValue.WriteNumberValue(2);
					if (double.IsFinite(number))
						outValue.WriteNumberValue(number);
					else
						outValue.WriteStringValue(double.IsNaN(number) ? "NaN" : number > 0 ? "Infinity" : "-Infinity");
					outValue.WriteEndArray();
					break;
				}
			case JSValue.Type.String:
				outValue.WriteStartArray();
				outValue.WriteNumberValue(3);
				outValue.WriteStringValue(inValue.GetString());
				outValue.WriteEndArray();
				break;
			case JSValue.Type.Array:
				outValue.WriteStartArray();
				outValue.WriteNumberValue(4);
				outValue.WriteStartArray();
				foreach (JSValue x in inValue.GetArray())
					if (!JSValueToCefValue(outValue, x, depth + 1))
						return false;
				outValue.WriteEndArray();
				outValue.WriteEndArray();
				break;
			case JSValue.Type.HashMap: {
					outValue.WriteStartArray();
					outValue.WriteNumberValue(5);
					outValue.WriteStartArray();
					if (inValue.HashMap_Begin(out ReadOnlySpan<char> key, out JSValue value)) {
						do {
							outValue.WriteStartArray();
							outValue.WriteStringValue(key);
							if (!JSValueToCefValue(outValue, value, depth + 1))
								return false;
							outValue.WriteEndArray();
						}
						while (inValue.HashMap_Next(out key, out value));
					}
					outValue.WriteEndArray();
					outValue.WriteEndArray();
					break;
				}
			default:
				outValue.WriteNumberValue(0);
				break;
		}

		return true;
	}

	static bool JSValuesToCefList(out string outList, ReadOnlySpan<JSValue> inList) {
		outList = "";

		using MemoryStream stream = new();
		try {
			using Utf8JsonWriter writer = new(stream);
			writer.WriteStartArray();
			foreach (JSValue x in inList)
				if (!JSValueToCefValue(writer, x))
					return false;
			writer.WriteEndArray();
		}
		catch (ArgumentException) {
			return false;
		}
		catch (InvalidOperationException) {
			return false;
		}

		outList = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
		return true;
	}

	static CefEventFlags GetModifiers(IHtmlClient.EventModifiers modifiers) {
		CefEventFlags chromiumModifiers = 0;

		if ((modifiers & IHtmlClient.EventModifiers.Shift) != 0)
			chromiumModifiers |= CefEventFlags.ShiftDown | CefEventFlags.IsLeft;

		if ((modifiers & IHtmlClient.EventModifiers.ShiftR) != 0)
			chromiumModifiers |= CefEventFlags.ShiftDown | CefEventFlags.IsRight;

		if ((modifiers & IHtmlClient.EventModifiers.Control) != 0)
			chromiumModifiers |= CefEventFlags.ControlDown | CefEventFlags.IsLeft;

		if ((modifiers & IHtmlClient.EventModifiers.ControlR) != 0)
			chromiumModifiers |= CefEventFlags.ControlDown | CefEventFlags.IsRight;

		if ((modifiers & IHtmlClient.EventModifiers.Alt) != 0)
			chromiumModifiers |= CefEventFlags.AltDown | CefEventFlags.IsLeft;

		if ((modifiers & IHtmlClient.EventModifiers.AltGr) != 0)
			chromiumModifiers |= CefEventFlags.AltDown | CefEventFlags.IsRight;

		if ((modifiers & IHtmlClient.EventModifiers.LeftMouse) != 0)
			chromiumModifiers |= CefEventFlags.LeftMouseButton;

		if ((modifiers & IHtmlClient.EventModifiers.MiddleMouse) != 0)
			chromiumModifiers |= CefEventFlags.MiddleMouseButton;

		if ((modifiers & IHtmlClient.EventModifiers.RightMouse) != 0)
			chromiumModifiers |= CefEventFlags.RightMouseButton;

		if ((modifiers & IHtmlClient.EventModifiers.Numpad) != 0)
			chromiumModifiers |= CefEventFlags.IsKeyPad;

		return chromiumModifiers;
	}

	static bool TryParseScheme(string url, out string scheme) {
		if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) {
			scheme = uri.Scheme;
			return true;
		}

		scheme = "";
		return false;
	}

	int Wide = 512;
	int Tall = 512;

	int PopupX;
	int PopupY;
	int PopupWide;
	int PopupTall;
	byte[]? PopupData;

	readonly MessageQueue MessageQueue = new();

	readonly ImageData ImageData = new();

	ChromiumWebBrowser? WebBrowser;
	IBrowser? Browser;
	IBrowserHost? BrowserHost;

	bool OpenLinksExternally;

	readonly string BridgeSecret;
	readonly HtmlBridge.RegistryWriter? Registry;
	readonly HashSet<(string Obj, string Func)> RegisteredFunctions = [];
	readonly List<Action> PendingTasks = [];
	readonly ResourceRequestHandler ResourceRequest;
	readonly RenderHandler Render;

	public ChromiumBrowser(string bridgeSecret, HtmlBridge.RegistryWriter? registry) {
		BridgeSecret = bridgeSecret;
		Registry = registry;
		ResourceRequest = new ResourceRequestHandler();
		Render = new RenderHandler(this);
	}

	public void CreateBrowser(IBrowserSettings browserSettings) {
		WebBrowser = new ChromiumWebBrowser("", null, null, false);

		WebBrowser.RenderHandler?.Dispose();
		WebBrowser.RenderHandler = Render;
		WebBrowser.LifeSpanHandler = this;
		WebBrowser.LoadHandler = this;
		WebBrowser.DisplayHandler = this;
		WebBrowser.RequestHandler = this;
		WebBrowser.MenuHandler = this;
		WebBrowser.DialogHandler = this;
		WebBrowser.JsDialogHandler = this;
		WebBrowser.JavascriptObjectRepository.Settings.JavascriptBindingApiGlobalObjectName = HtmlBridge.ApiName(BridgeSecret);
		WebBrowser.JavascriptMessageReceived += OnJavascriptMessageReceived;

		WindowInfo windowInfo = new();
		windowInfo.SetAsWindowless(0);

		WebBrowser.CreateBrowser(windowInfo, browserSettings);
	}

	public ImageData GetImageData() => ImageData;

	public MessageQueue GetMessageQueue() => MessageQueue;

	public void QueueMessage(in MessageQueue.Message message) {
		MessageQueue.Lock();
		MessageQueue.Push(message);
		MessageQueue.Unlock();
	}

	void RunOrDefer(Action task) {
		if (Browser == null) {
			PendingTasks.Add(task);
			return;
		}

		task();
	}

	public void Close() {
		PendingTasks.Clear();

		if (Browser != null)
			Registry?.Remove(Browser.Identifier);

		BrowserHost?.CloseBrowser(true);
		WebBrowser?.Dispose();
		WebBrowser = null;
	}

	public void SetSize(int wide, int tall) {
		Wide = wide;
		Tall = tall;
		RunOrDefer(() => BrowserHost!.WasResized());
	}

	public void SetFocused(bool hasFocus) => RunOrDefer(() => BrowserHost!.SendFocusEvent(hasFocus));

	public void SendKeyEvent(IHtmlClient.KeyEvent keyEvent) {
		KeyEvent chromiumKeyEvent = new() {
			Modifiers = GetModifiers(keyEvent.Modifiers)
		};

		switch (keyEvent.EventType) {
			case IHtmlClient.KeyEvent.Type.KeyChar:
				chromiumKeyEvent.Type = KeyEventType.Char;
				chromiumKeyEvent.WindowsKeyCode = keyEvent.WindowsKeyCode;
				chromiumKeyEvent.NativeKeyCode = keyEvent.NativeKeyCode;
				break;
			case IHtmlClient.KeyEvent.Type.KeyDown:
				chromiumKeyEvent.Type = KeyEventType.KeyDown;
				chromiumKeyEvent.WindowsKeyCode = keyEvent.WindowsKeyCode;
				chromiumKeyEvent.NativeKeyCode = keyEvent.NativeKeyCode;
				break;
			case IHtmlClient.KeyEvent.Type.KeyUp:
				chromiumKeyEvent.Type = KeyEventType.KeyUp;
				chromiumKeyEvent.WindowsKeyCode = keyEvent.WindowsKeyCode;
				chromiumKeyEvent.NativeKeyCode = keyEvent.NativeKeyCode;
				break;
		}

		RunOrDefer(() => BrowserHost!.SendKeyEvent(chromiumKeyEvent));
	}

	public void SendMouseMoveEvent(IHtmlClient.MouseEvent gmodMouseEvent, bool mouseLeave) {
		MouseEvent mouseEvent = new(gmodMouseEvent.X, gmodMouseEvent.Y, GetModifiers(gmodMouseEvent.Modifiers));
		RunOrDefer(() => BrowserHost!.SendMouseMoveEvent(mouseEvent, mouseLeave));
	}

	public void SendMouseWheelEvent(IHtmlClient.MouseEvent gmodMouseEvent, int deltaX, int deltaY) {
		MouseEvent mouseEvent = new(gmodMouseEvent.X, gmodMouseEvent.Y, GetModifiers(gmodMouseEvent.Modifiers));
		RunOrDefer(() => BrowserHost!.SendMouseWheelEvent(mouseEvent, deltaX, deltaY));
	}

	public void SendMouseClickEvent(IHtmlClient.MouseEvent gmodMouseEvent, IHtmlClient.MouseButton gmodButtonType, bool mouseUp, int clickCount) {
		MouseEvent mouseEvent = new(gmodMouseEvent.X, gmodMouseEvent.Y, GetModifiers(gmodMouseEvent.Modifiers));

		MouseButtonType buttonType = gmodButtonType switch {
			IHtmlClient.MouseButton.Middle => MouseButtonType.Middle,
			IHtmlClient.MouseButton.Right => MouseButtonType.Right,
			_ => MouseButtonType.Left,
		};

		RunOrDefer(() => BrowserHost!.SendMouseClickEvent(mouseEvent, buttonType, mouseUp, clickCount));
	}

	public void LoadUrl(string url) => RunOrDefer(() => Browser!.MainFrame.LoadUrl(url));

	public void SetHtml(string html) {
		string url = $"asset://html/?{Convert.ToBase64String(Encoding.UTF8.GetBytes(html))}";
		RunOrDefer(() => Browser!.MainFrame.LoadUrl(url));
	}

	public void Refresh() => RunOrDefer(() => Browser!.Reload());

	public void Stop() => RunOrDefer(() => Browser!.StopLoad());

	public void GoBack() => RunOrDefer(() => Browser!.GoBack());

	public void GoForward() => RunOrDefer(() => Browser!.GoForward());

	public void RunJavaScript(string code) {
		RunOrDefer(() => {
			IFrame mainFrame = Browser!.MainFrame;
			string url = mainFrame.Url ?? "";

			int slash = url.Length >= 9 ? url.IndexOf('/', 8) : -1;
			string host = slash == -1 ? url : url[..slash];

			foreach (string blocked in BlockedJavaScriptHosts)
				if (host.Contains(blocked, StringComparison.Ordinal))
					return;

			mainFrame.ExecuteJavaScriptAsync(code, "Lua File", 1);
		});
	}

	public void RegisterJavaScriptFunction(string objName, string funcName) {
		RunOrDefer(() => {
			lock (RegisteredFunctions)
				RegisteredFunctions.Add((objName, funcName));
			Registry?.Add(Browser!.Identifier, objName, funcName);

			string script = HtmlBridge.RegisterScript(BridgeSecret, objName, funcName);
			foreach (long frameId in Browser!.GetFrameIdentifiers()) {
				IFrame? frame = Browser.GetFrame(frameId);
				if (frame != null && frame.IsValid)
					frame.ExecuteJavaScriptAsync(script);
			}
		});
	}

	public void SetOpenLinksExternally(bool openLinksExternally) => OpenLinksExternally = openLinksExternally;

	public void ExecuteCallback(int callbackId, long frameId, string nonce, JSValue paramsArray) {
		if (Browser == null)
			return;

		ReadOnlySpan<JSValue> paramsVector = paramsArray.GetJSType() == JSValue.Type.Array ? paramsArray.GetArray() : default;
		if (!JSValuesToCefList(out string cefArgs, paramsVector))
			return;

		IFrame? frame = Browser.GetFrame(frameId);
		if (frame == null || !frame.IsValid)
			return;

		frame.ExecuteJavaScriptAsync(HtmlBridge.CallbackScript(BridgeSecret, callbackId, nonce, cefArgs));
	}

	void OnJavascriptMessageReceived(object? sender, JavascriptMessageReceivedEventArgs e) {
		if (e.Message is not string message)
			return;

		JsonDocument document;
		try {
			document = JsonDocument.Parse(message, MessageOptions);
		}
		catch (JsonException) {
			return;
		}

		using (document) {
			JsonElement args = document.RootElement;
			if (args.ValueKind != JsonValueKind.Array || args.GetArrayLength() != 7)
				return;

			if (args[0].ValueKind != JsonValueKind.String || args[0].GetString() != "ExecuteFunction")
				return;

			if (args[1].ValueKind != JsonValueKind.String || args[2].ValueKind != JsonValueKind.String || args[3].ValueKind != JsonValueKind.Number || args[4].ValueKind != JsonValueKind.Array
				|| args[5].ValueKind != JsonValueKind.String || args[6].ValueKind != JsonValueKind.String)
				return;

			string objName = args[1].GetString()!;
			string funcName = args[2].GetString()!;
			lock (RegisteredFunctions) {
				if (!RegisteredFunctions.Contains((objName, funcName)))
					return;
			}

			if (!args[3].TryGetInt32(out int callbackId) || callbackId < -1 || callbackId >= 16384)
				return;

			if (!long.TryParse(args[5].GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long frameId))
				return;

			if (!CefListToJSValues(out JSValue[] values, args[4]))
				return;

			MessageQueue.Message msg = new() {
				Type = MessageQueue.Type.OnJavaScriptCall,
				String1 = objName,
				String2 = funcName,
				Integer = callbackId,
				FrameId = frameId,
				Nonce = args[6].GetString()!,
				JSParams = JSArray.Create(values)
			};

			QueueMessage(msg);
		}
	}

	public void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser) {
		Browser = browser;
		BrowserHost = browser.GetHost();

		Action[] pending = [.. PendingTasks];
		PendingTasks.Clear();
		foreach (Action task in pending)
			task();
	}

	public bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser) => false;

	public void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser) {
		Browser = null;
		BrowserHost = null;
	}

	public bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName, WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo, IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser newBrowser) {
		string sourceUrl = frame.Url;

		MessageQueue.Message msg = new() {
			Type = MessageQueue.Type.OnCreateChildView,
			String1 = sourceUrl,
			String2 = targetUrl,
			Integer = targetDisposition == WindowOpenDisposition.NewPopup ? 1 : 0
		};
		QueueMessage(msg);

		newBrowser = null!;
		return true;
	}

	public void OnLoadingStateChange(IWebBrowser chromiumWebBrowser, LoadingStateChangedEventArgs loadingStateChangedArgs) { }

	public void OnFrameLoadStart(IWebBrowser chromiumWebBrowser, FrameLoadStartEventArgs frameLoadStartArgs) {
		IFrame frame = frameLoadStartArgs.Frame;
		if (!frame.IsMain)
			return;

		MessageQueue.Message msg = new() {
			Type = MessageQueue.Type.OnLoadStart,
			String1 = frame.Url
		};
		QueueMessage(msg);
	}

	public void OnFrameLoadEnd(IWebBrowser chromiumWebBrowser, FrameLoadEndEventArgs frameLoadEndArgs) {
		IFrame frame = frameLoadEndArgs.Frame;
		if (!frame.IsMain)
			return;

		MessageQueue.Message msg = new() {
			Type = MessageQueue.Type.OnLoadEnd,
			String1 = frame.Url
		};
		QueueMessage(msg);

		frame.ExecuteJavaScriptAsync("WebSocket = undefined;", "haha", 0);
	}

	public void OnLoadError(IWebBrowser chromiumWebBrowser, LoadErrorEventArgs loadErrorArgs) {
		IFrame frame = loadErrorArgs.Frame;
		if (!frame.IsMain)
			return;

		MessageQueue.Message msg = new() {
			Type = MessageQueue.Type.OnLoadEnd,
			String1 = frame.Url
		};
		QueueMessage(msg);

		frame.ExecuteJavaScriptAsync("WebSocket = undefined;", "haha", 0);
	}

	public void OnAddressChanged(IWebBrowser chromiumWebBrowser, AddressChangedEventArgs addressChangedArgs) {
		IFrame? mainFrame = addressChangedArgs.Browser?.MainFrame;
		if (mainFrame != null && mainFrame.Url != addressChangedArgs.Address)
			return;

		MessageQueue.Message msg = new() {
			Type = MessageQueue.Type.OnAddressChange,
			String1 = addressChangedArgs.Address
		};
		QueueMessage(msg);
	}

	public bool OnAutoResize(IWebBrowser chromiumWebBrowser, IBrowser browser, Size newSize) => false;

	public bool OnCursorChange(IWebBrowser chromiumWebBrowser, IBrowser browser, nint cursor, CursorType type, CursorInfo customCursorInfo) {
		IHtmlClientListener.CursorType gmodCursor = type switch {
			CursorType.Pointer => IHtmlClientListener.CursorType.Default,
			CursorType.Cross => IHtmlClientListener.CursorType.Crosshair,
			CursorType.Hand => IHtmlClientListener.CursorType.Hand,
			CursorType.IBeam => IHtmlClientListener.CursorType.IBeam,
			CursorType.Wait => IHtmlClientListener.CursorType.Hourglass,
			CursorType.Move => IHtmlClientListener.CursorType.SizeAll,
			CursorType.EastResize or CursorType.WestResize or CursorType.EastWestResize => IHtmlClientListener.CursorType.SizeEW,
			CursorType.NorthResize or CursorType.SouthResize or CursorType.NorthSouthResize => IHtmlClientListener.CursorType.SizeNS,
			CursorType.NortheastResize or CursorType.SouthwestResize or CursorType.NortheastSouthwestResize => IHtmlClientListener.CursorType.SizeNESW,
			CursorType.NorthwestResize or CursorType.SoutheastResize or CursorType.NorthwestSoutheastResize => IHtmlClientListener.CursorType.SizeNWSE,
			CursorType.ColumnResize => IHtmlClientListener.CursorType.SizeEW,
			CursorType.RowResize => IHtmlClientListener.CursorType.SizeNS,
			CursorType.NotAllowed => IHtmlClientListener.CursorType.No,
			CursorType.None => IHtmlClientListener.CursorType.None,
			CursorType.MiddlePanning or CursorType.EastPanning or CursorType.NorthPanning or CursorType.NortheastPanning or CursorType.NorthwestPanning
				or CursorType.SouthPanning or CursorType.SoutheastPanning or CursorType.SouthwestPanning or CursorType.WestPanning => IHtmlClientListener.CursorType.SizeAll,
			_ => IHtmlClientListener.CursorType.Default
		};

		MessageQueue.Message msg = new() {
			Type = MessageQueue.Type.OnCursorChange,
			Integer = (int)gmodCursor
		};
		QueueMessage(msg);
		return true;
	}

	public void OnTitleChanged(IWebBrowser chromiumWebBrowser, TitleChangedEventArgs titleChangedArgs) {
		MessageQueue.Message msg = new() {
			Type = MessageQueue.Type.OnTitleChange,
			String1 = titleChangedArgs.Title
		};
		QueueMessage(msg);
	}

	public void OnFaviconUrlChange(IWebBrowser chromiumWebBrowser, IBrowser browser, IList<string> urls) { }

	public void OnFullscreenModeChange(IWebBrowser chromiumWebBrowser, IBrowser browser, bool fullscreen) { }

	public void OnLoadingProgressChange(IWebBrowser chromiumWebBrowser, IBrowser browser, double progress) { }

	public bool OnTooltipChanged(IWebBrowser chromiumWebBrowser, ref string text) => false;

	public void OnStatusMessage(IWebBrowser chromiumWebBrowser, StatusMessageEventArgs statusMessageArgs) { }

	public bool OnConsoleMessage(IWebBrowser chromiumWebBrowser, ConsoleMessageEventArgs consoleMessageArgs) {
		MessageQueue.Message msg = new() {
			Type = MessageQueue.Type.OnConsoleMessage,
			String1 = consoleMessageArgs.Message,
			String2 = consoleMessageArgs.Source,
			Integer = consoleMessageArgs.Line,
			Integer2 = (int)consoleMessageArgs.Level
		};
		QueueMessage(msg);

		return false;
	}

	void OnPopupShow(bool show) {
		if (BrowserHost == null)
			return;

		if (!show) {
			PopupX = 0;
			PopupY = 0;
			PopupWide = 0;
			PopupTall = 0;
			PopupData = null;

			BrowserHost.Invalidate(PaintElementType.View);
		}
	}

	void OnPopupSize(Rect rect) {
		PopupX = rect.X;
		PopupY = rect.Y;
	}

	void OnPaint(PaintElementType type, nint buffer, int width, int height) {
		if (width <= 0 || height <= 0)
			return;

		switch (type) {
			case PaintElementType.Popup:
				PopupData = new byte[width * height * 4];
				System.Runtime.InteropServices.Marshal.Copy(buffer, PopupData, 0, PopupData.Length);
				PopupWide = width;
				PopupTall = height;

				BrowserHost?.Invalidate(PaintElementType.View);
				return;

			case PaintElementType.View:
				ImageData.Lock();
				ImageData.SetData(buffer, width, height);

				if (PopupWide > 0 && PopupTall > 0 &&
					PopupX < width && PopupY < height &&
					(PopupX + PopupWide) > 0 && (PopupY + PopupTall) > 0) {
					ImageData.CopyImage(PopupData, PopupX, PopupY, PopupWide, PopupTall);
				}

				ImageData.SetDirty(true);
				ImageData.Unlock();
				return;
		}
	}

	public bool OnOpenUrlFromTab(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, WindowOpenDisposition targetDisposition, bool userGesture) {
		if (targetDisposition == WindowOpenDisposition.NewPopup || targetDisposition == WindowOpenDisposition.NewForegroundTab || targetDisposition == WindowOpenDisposition.NewBackgroundTab) {
			string sourceUrl = frame.Url;

			MessageQueue.Message msg = new() {
				Type = MessageQueue.Type.OnCreateChildView,
				String1 = sourceUrl,
				String2 = targetUrl,
				Integer = targetDisposition == WindowOpenDisposition.NewPopup ? 1 : 0
			};
			QueueMessage(msg);

			return true;
		}

		return false;
	}

	public bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect) {
		string url = request.Url;
		if (!TryParseScheme(url, out string strScheme))
			return true;

		if (url == "chrome://credits/")
			return false;

		if (strScheme != "http" && strScheme != "https" && strScheme != "asset")
			return true;

		if (OpenLinksExternally) {
			try {
				Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
			}
			catch (Exception) { }
			return true;
		}

		return false;
	}

	public void OnDocumentAvailableInMainFrame(IWebBrowser chromiumWebBrowser, IBrowser browser) { }

	public IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling) => ResourceRequest;

	public bool GetAuthCredentials(IWebBrowser chromiumWebBrowser, IBrowser browser, string originUrl, bool isProxy, string host, int port, string realm, string scheme, IAuthCallback callback) => false;

	public bool OnQuotaRequest(IWebBrowser chromiumWebBrowser, IBrowser browser, string originUrl, long newSize, IRequestCallback callback) => false;

	public bool OnCertificateError(IWebBrowser chromiumWebBrowser, IBrowser browser, CefErrorCode errorCode, string requestUrl, ISslInfo sslInfo, IRequestCallback callback) => false;

	public bool OnSelectClientCertificate(IWebBrowser chromiumWebBrowser, IBrowser browser, bool isProxy, string host, int port, X509Certificate2Collection certificates, ISelectClientCertificateCallback callback) => false;

	public void OnPluginCrashed(IWebBrowser chromiumWebBrowser, IBrowser browser, string pluginPath) { }

	public void OnRenderViewReady(IWebBrowser chromiumWebBrowser, IBrowser browser) { }

	public void OnRenderProcessTerminated(IWebBrowser chromiumWebBrowser, IBrowser browser, CefTerminationStatus status) { }

	public void OnBeforeContextMenu(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model) => model.Clear();

	public bool OnContextMenuCommand(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, CefMenuCommand commandId, CefEventFlags eventFlags) => false;

	public void OnContextMenuDismissed(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame) { }

	public bool RunContextMenu(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IContextMenuParams parameters, IMenuModel model, IRunContextMenuCallback callback) => false;

	public bool OnFileDialog(IWebBrowser chromiumWebBrowser, IBrowser browser, CefFileDialogMode mode, CefFileDialogFlags flags, string title, string defaultFilePath, List<string> acceptFilters, int selectedAcceptFilter, IFileDialogCallback callback) {
		callback.Cancel();
		return true;
	}

	public bool OnJSDialog(IWebBrowser chromiumWebBrowser, IBrowser browser, string originUrl, CefJsDialogType dialogType, string messageText, string defaultPromptText, IJsDialogCallback callback, ref bool suppressMessage) {
		suppressMessage = true;
		return false;
	}

	public bool OnBeforeUnloadDialog(IWebBrowser chromiumWebBrowser, IBrowser browser, string messageText, bool isReload, IJsDialogCallback callback) {
		callback.Continue(true, "");
		return true;
	}

	public void OnResetDialogState(IWebBrowser chromiumWebBrowser, IBrowser browser) { }

	public void OnDialogClosed(IWebBrowser chromiumWebBrowser, IBrowser browser) { }

	sealed class ResourceRequestHandler : IResourceRequestHandler
	{
		public ICookieAccessFilter GetCookieAccessFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) => null!;

		public CefReturnValue OnBeforeResourceLoad(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IRequestCallback callback) {
			string sourceUrl = request.Url;

			if (!TryParseScheme(sourceUrl, out string strScheme))
				return CefReturnValue.Cancel;

			if (sourceUrl == "chrome://credits/" ||
				sourceUrl == "chrome://resources/js/cr.js" ||
				sourceUrl == "chrome://credits/credits.js") {
				return CefReturnValue.Continue;
			}

			if (strScheme != "http" && strScheme != "https" && strScheme != "asset")
				return CefReturnValue.Cancel;

			return CefReturnValue.Continue;
		}

		public IResourceHandler GetResourceHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) => null!;

		public void OnResourceRedirect(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response, ref string newUrl) { }

		public bool OnResourceResponse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response) => false;

		public IResponseFilter GetResourceResponseFilter(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response) => null!;

		public void OnResourceLoadComplete(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, IResponse response, UrlRequestStatus status, long receivedContentLength) { }

		public bool OnProtocolExecution(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request) => false;

		public void Dispose() { }
	}

	sealed class RenderHandler(ChromiumBrowser owner) : IRenderHandler
	{
		public ScreenInfo? GetScreenInfo() => null;

		public Rect GetViewRect() => new(0, 0, owner.Wide, owner.Tall);

		public bool GetScreenPoint(int viewX, int viewY, out int screenX, out int screenY) {
			screenX = viewX;
			screenY = viewY;
			return false;
		}

		public void OnAcceleratedPaint(PaintElementType type, Rect dirtyRect, nint sharedHandle) { }

		public void OnPaint(PaintElementType type, Rect dirtyRect, nint buffer, int width, int height) => owner.OnPaint(type, buffer, width, height);

		public void OnCursorChange(nint cursor, CursorType type, CursorInfo customCursorInfo) { }

		public bool StartDragging(IDragData dragData, DragOperationsMask mask, int x, int y) => false;

		public void UpdateDragCursor(DragOperationsMask operation) { }

		public void OnPopupShow(bool show) => owner.OnPopupShow(show);

		public void OnPopupSize(Rect rect) => owner.OnPopupSize(rect);

		public void OnImeCompositionRangeChanged(Range selectedRange, Rect[] characterBounds) { }

		public void OnVirtualKeyboardRequested(IBrowser browser, TextInputMode inputMode) { }

		public void Dispose() { }
	}
}
