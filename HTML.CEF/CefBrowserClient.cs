using Source.Common.GarrysMod;

using System.Diagnostics;
using System.Text;

using Xilium.CefGlue;

namespace HTML.CEF;

public sealed class CefBrowserClient : CefClient
{
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

	static bool CefValueToJSValue(out JSValue outValue, ICefListValue list, int index, int depth = 0) {
		outValue = default;

		if (depth > 16)
			return false;

		switch (list.GetValueType(index)) {
			case CefValueType.Bool:
				outValue = new JSValue(list.GetBool(index));
				break;
			case CefValueType.Int:
				outValue = new JSValue(list.GetInt(index));
				break;
			case CefValueType.Double:
				outValue = new JSValue(list.GetDouble(index));
				break;
			case CefValueType.String:
				outValue = JSString.Create(list.GetString(index) ?? "");
				break;
			case CefValueType.List: {
					using ICefListValue inner = list.GetList(index);
					if (!CefListToJSValues(out JSValue[] values, inner, depth + 1))
						return false;
					outValue = JSArray.Create(values);
					break;
				}
			case CefValueType.Dictionary: {
					using ICefDictionaryValue inner = list.GetDictionary(index);
					if (!CefDictionaryToJSValue(out outValue, inner, depth + 1))
						return false;
					break;
				}
		}

		return true;
	}

	static bool CefDictionaryToJSValue(out JSValue outValue, ICefDictionaryValue dict, int depth) {
		outValue = default;

		if (depth > 16)
			return false;

		string[] keys = dict.GetKeys();
		Dictionary<string, JSValue> map = new(keys.Length);
		foreach (string key in keys) {
			using CefListValue wrapper = CefListValue.Create();
			using (CefValue? value = dict.GetValue(key)) {
				if (value == null)
					continue;
				wrapper.SetValue(0, value);
			}

			if (!CefValueToJSValue(out JSValue entry, wrapper, 0, depth + 1))
				return false;

			map.TryAdd(key, entry);
		}

		outValue = JSHashMap.Create(map);
		return true;
	}

	static bool CefListToJSValues(out JSValue[] outList, ICefListValue inList, int depth = 0) {
		outList = new JSValue[inList.Count];

		for (int i = 0; i < outList.Length; i++)
			if (!CefValueToJSValue(out outList[i], inList, i, depth))
				return false;

		return true;
	}

	static bool JSValueToCefList(CefListValue outList, int index, in JSValue inValue, int depth = 0) {
		if (depth > 16)
			return false;

		switch (inValue.GetJSType()) {
			case JSValue.Type.Bool:
				outList.SetBool(index, inValue.GetBool());
				break;
			case JSValue.Type.Number:
				outList.SetDouble(index, inValue.GetNumber());
				break;
			case JSValue.Type.String:
				outList.SetString(index, new string(inValue.GetString()));
				break;
			case JSValue.Type.Array: {
					using CefListValue list = CefListValue.Create();
					ReadOnlySpan<JSValue> values = inValue.GetArray();
					for (int i = 0; i < values.Length; i++)
						if (!JSValueToCefList(list, i, values[i], depth + 1))
							return false;
					outList.SetList(index, list);
					break;
				}
			case JSValue.Type.HashMap: {
					using CefDictionaryValue dict = CefDictionaryValue.Create();
					if (inValue.HashMap_Begin(out ReadOnlySpan<char> key, out JSValue value)) {
						do {
							using CefListValue wrapper = CefListValue.Create();
							if (!JSValueToCefList(wrapper, 0, value, depth + 1))
								return false;
							using CefValue? entry = wrapper.GetValue(0);
							if (entry != null)
								dict.SetValue(new string(key), entry);
						}
						while (inValue.HashMap_Next(out key, out value));
					}
					outList.SetDictionary(index, dict);
					break;
				}
			default:
				outList.SetNull(index);
				break;
		}

		return true;
	}

	static CefEventFlags GetModifiers(IHtmlClient.EventModifiers modifiers) {
		CefEventFlags flags = CefEventFlags.None;

		if ((modifiers & IHtmlClient.EventModifiers.Shift) != 0)
			flags |= CefEventFlags.ShiftDown | CefEventFlags.IsLeft;

		if ((modifiers & IHtmlClient.EventModifiers.ShiftR) != 0)
			flags |= CefEventFlags.ShiftDown | CefEventFlags.IsRight;

		if ((modifiers & IHtmlClient.EventModifiers.Control) != 0)
			flags |= CefEventFlags.ControlDown | CefEventFlags.IsLeft;

		if ((modifiers & IHtmlClient.EventModifiers.ControlR) != 0)
			flags |= CefEventFlags.ControlDown | CefEventFlags.IsRight;

		if ((modifiers & IHtmlClient.EventModifiers.Alt) != 0)
			flags |= CefEventFlags.AltDown | CefEventFlags.IsLeft;

		if ((modifiers & IHtmlClient.EventModifiers.AltGr) != 0)
			flags |= CefEventFlags.AltDown | CefEventFlags.IsRight;

		if ((modifiers & IHtmlClient.EventModifiers.LeftMouse) != 0)
			flags |= CefEventFlags.LeftMouseButton;

		if ((modifiers & IHtmlClient.EventModifiers.MiddleMouse) != 0)
			flags |= CefEventFlags.MiddleMouseButton;

		if ((modifiers & IHtmlClient.EventModifiers.RightMouse) != 0)
			flags |= CefEventFlags.RightMouseButton;

		if ((modifiers & IHtmlClient.EventModifiers.Numpad) != 0)
			flags |= CefEventFlags.IsKeyPad;

		return flags;
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

	CefBrowserSettings? Settings;
	CefBrowser? Browser;
	CefBrowserHost? BrowserHost;
	bool Created;
	bool Closing;

	bool OpenLinksExternally;

	readonly List<(string Obj, string Func)> RegisteredFunctions = [];
	readonly List<Action> PendingTasks = [];

	readonly LifeSpanHandler LifeSpan;
	readonly LoadHandler Load;
	readonly DisplayHandler Display;
	readonly RequestHandler Request;
	readonly ContextMenuHandler ContextMenu = new();
	readonly DialogHandler Dialog = new();
	readonly JSDialogHandler JSDialog = new();
	readonly RenderHandler Render;

	public CefBrowserClient() {
		LifeSpan = new LifeSpanHandler(this);
		Load = new LoadHandler(this);
		Display = new DisplayHandler(this);
		Request = new RequestHandler(this);
		Render = new RenderHandler(this);
	}

	public void CreateBrowser(CefBrowserSettings browserSettings) => Settings = browserSettings;

	bool CreateBrowserWithUrl(string url) {
		if (Created || Closing || Settings == null)
			return false;

		Created = true;

		using CefListValue functions = CefListValue.Create();
		for (int i = 0; i < RegisteredFunctions.Count; i++) {
			using CefListValue pair = CefListValue.Create();
			pair.SetString(0, RegisteredFunctions[i].Obj);
			pair.SetString(1, RegisteredFunctions[i].Func);
			functions.SetList(i, pair);
		}

		using CefDictionaryValue extraInfo = CefDictionaryValue.Create();
		extraInfo.SetList("functions", functions);

		CefWindowInfo windowInfo = CefWindowInfo.Create();
		windowInfo.SetAsWindowless(IntPtr.Zero, false);
		CefBrowserHost.CreateBrowser(windowInfo, this, Settings, url, extraInfo);
		return true;
	}

	public ImageData GetImageData() => ImageData;

	public MessageQueue GetMessageQueue() => MessageQueue;

	void QueueMessage(in MessageQueue.Message message) {
		MessageQueue.Lock();
		MessageQueue.Push(message);
		MessageQueue.Unlock();
	}

	void RunOrDefer(Action task) {
		if (Closing)
			return;

		if (Browser == null) {
			PendingTasks.Add(task);
			return;
		}

		task();
	}

	public void Close() {
		PendingTasks.Clear();
		Closing = true;
		BrowserHost?.CloseBrowser(true);
	}

	public void SetSize(int wide, int tall) {
		Wide = wide;
		Tall = tall;
		RunOrDefer(() => BrowserHost!.WasResized());
	}

	public void SetFocused(bool hasFocus) => RunOrDefer(() => BrowserHost!.SetFocus(hasFocus));

	public void SendKeyEvent(IHtmlClient.KeyEvent keyEvent) {
		CefKeyEvent cefKeyEvent = new() {
			Modifiers = GetModifiers(keyEvent.Modifiers),
			WindowsKeyCode = keyEvent.WindowsKeyCode,
			NativeKeyCode = keyEvent.NativeKeyCode,
			EventType = keyEvent.EventType switch {
				IHtmlClient.KeyEvent.Type.KeyChar => CefKeyEventType.Char,
				IHtmlClient.KeyEvent.Type.KeyUp => CefKeyEventType.KeyUp,
				_ => CefKeyEventType.KeyDown,
			}
		};

		if (keyEvent.EventType == IHtmlClient.KeyEvent.Type.KeyChar) {
			cefKeyEvent.Character = (char)keyEvent.KeyChar;
			cefKeyEvent.UnmodifiedCharacter = (char)keyEvent.KeyChar;
		}

		RunOrDefer(() => BrowserHost!.SendKeyEvent(cefKeyEvent));
	}

	public void SendMouseMoveEvent(IHtmlClient.MouseEvent mouseEvent, bool mouseLeave) {
		CefMouseEvent cefMouseEvent = new(mouseEvent.X, mouseEvent.Y, GetModifiers(mouseEvent.Modifiers));
		RunOrDefer(() => BrowserHost!.SendMouseMoveEvent(cefMouseEvent, mouseLeave));
	}

	public void SendMouseWheelEvent(IHtmlClient.MouseEvent mouseEvent, int deltaX, int deltaY) {
		CefMouseEvent cefMouseEvent = new(mouseEvent.X, mouseEvent.Y, GetModifiers(mouseEvent.Modifiers));
		RunOrDefer(() => BrowserHost!.SendMouseWheelEvent(cefMouseEvent, deltaX, deltaY));
	}

	public void SendMouseClickEvent(IHtmlClient.MouseEvent mouseEvent, IHtmlClient.MouseButton mouseButton, bool mouseUp, int clickCount) {
		CefMouseEvent cefMouseEvent = new(mouseEvent.X, mouseEvent.Y, GetModifiers(mouseEvent.Modifiers));

		CefMouseButtonType buttonType = mouseButton switch {
			IHtmlClient.MouseButton.Middle => CefMouseButtonType.Middle,
			IHtmlClient.MouseButton.Right => CefMouseButtonType.Right,
			_ => CefMouseButtonType.Left,
		};

		RunOrDefer(() => BrowserHost!.SendMouseClickEvent(cefMouseEvent, buttonType, mouseUp, clickCount));
	}

	public void LoadUrl(string url) {
		if (CreateBrowserWithUrl(url))
			return;

		RunOrDefer(() => Browser!.GetMainFrame()?.LoadUrl(url));
	}

	public void SetHtml(string html) => LoadUrl($"asset://html/?{Convert.ToBase64String(Encoding.UTF8.GetBytes(html))}");

	public void Refresh() => RunOrDefer(() => Browser!.Reload());

	public void Stop() => RunOrDefer(() => Browser!.StopLoad());

	public void GoBack() => RunOrDefer(() => Browser!.GoBack());

	public void GoForward() => RunOrDefer(() => Browser!.GoForward());

	public void RunJavaScript(string code) {
		RunOrDefer(() => {
			CefFrame? mainFrame = Browser!.GetMainFrame();
			if (mainFrame == null)
				return;

			string url = mainFrame.Url ?? "";

			int slash = url.Length >= 9 ? url.IndexOf('/', 8) : -1;
			string host = slash == -1 ? url : url[..slash];

			foreach (string blocked in BlockedJavaScriptHosts)
				if (host.Contains(blocked, StringComparison.Ordinal))
					return;

			mainFrame.ExecuteJavaScript(code, "Lua File", 1);
		});
	}

	public void RegisterJavaScriptFunction(string objName, string funcName) {
		if (Closing || RegisteredFunctions.Contains((objName, funcName)))
			return;

		RegisteredFunctions.Add((objName, funcName));

		if (Browser == null)
			return;

		foreach (string frameId in Browser.GetFrameIdentifiers()) {
			CefFrame? frame = Browser.GetFrameByIdentifier(frameId);
			if (frame != null && frame.IsValid)
				SendRegisterFunction(frame, objName, funcName);
		}
	}

	static void SendRegisterFunction(CefFrame frame, string objName, string funcName) {
		using CefProcessMessage message = CefProcessMessage.Create("RegisterFunction");
		message.Arguments!.SetString(0, objName);
		message.Arguments!.SetString(1, funcName);
		frame.SendProcessMessage(CefProcessId.Renderer, message);
	}

	public void SetOpenLinksExternally(bool openLinksExternally) => OpenLinksExternally = openLinksExternally;

	public void ExecuteCallback(int callbackId, string frameId, JSValue paramsArray) {
		if (Browser == null)
			return;

		CefFrame? frame = Browser.GetFrameByIdentifier(frameId);
		if (frame == null || !frame.IsValid)
			return;

		using CefProcessMessage message = CefProcessMessage.Create("ExecuteCallback");
		using CefListValue args = CefListValue.Create();

		ReadOnlySpan<JSValue> values = paramsArray.GetJSType() == JSValue.Type.Array ? paramsArray.GetArray() : default;
		for (int i = 0; i < values.Length; i++) {
			if (!JSValueToCefList(args, i, values[i])) {
				ForgetCallback(frame, callbackId);
				return;
			}
		}

		message.Arguments!.SetInt(0, callbackId);
		message.Arguments!.SetList(1, args);
		frame.SendProcessMessage(CefProcessId.Renderer, message);
	}

	static void ForgetCallback(CefFrame frame, int callbackId) {
		using CefProcessMessage message = CefProcessMessage.Create("ForgetCallback");
		message.Arguments!.SetInt(0, callbackId);
		frame.SendProcessMessage(CefProcessId.Renderer, message);
	}

	protected override bool OnProcessMessageReceived(CefBrowser browser, CefFrame frame, CefProcessId sourceProcess, CefProcessMessage message) {
		if (sourceProcess != CefProcessId.Renderer)
			return false;

		switch (message.Name) {
			case "SyncFunctions":
				foreach ((string obj, string func) in RegisteredFunctions)
					SendRegisterFunction(frame, obj, func);
				return true;

			case "ExecuteFunction":
				if (message.Arguments is { } arguments)
					OnExecuteFunction(frame, arguments);
				return true;
		}

		return false;
	}

	void OnExecuteFunction(CefFrame frame, ICefListValue args) {
		if (args.Count != 4 || args.GetValueType(0) != CefValueType.String || args.GetValueType(1) != CefValueType.String
			|| args.GetValueType(2) != CefValueType.Int || args.GetValueType(3) != CefValueType.List)
			return;

		string objName = args.GetString(0) ?? "";
		string funcName = args.GetString(1) ?? "";
		if (!RegisteredFunctions.Contains((objName, funcName)))
			return;

		int callbackId = args.GetInt(2);
		if (callbackId < -1 || callbackId >= 16384)
			return;

		using ICefListValue list = args.GetList(3);
		if (!CefListToJSValues(out JSValue[] values, list)) {
			if (callbackId != -1)
				ForgetCallback(frame, callbackId);
			return;
		}

		QueueMessage(new MessageQueue.Message {
			Type = MessageQueue.Type.OnJavaScriptCall,
			String1 = objName,
			String2 = funcName,
			Integer = callbackId,
			FrameId = frame.Identifier,
			JSParams = JSArray.Create(values)
		});
	}

	void OnAfterCreated(CefBrowser browser) {
		Browser = browser;
		BrowserHost = browser.GetHost();

		if (Closing) {
			BrowserHost.CloseBrowser(true);
			return;
		}

		Action[] pending = [.. PendingTasks];
		PendingTasks.Clear();
		foreach (Action task in pending)
			task();
	}

	void OnBeforeClose() {
		Browser = null;
		BrowserHost = null;
	}

	void QueueChildView(string sourceUrl, string targetUrl, bool isPopup) {
		QueueMessage(new MessageQueue.Message {
			Type = MessageQueue.Type.OnCreateChildView,
			String1 = sourceUrl,
			String2 = targetUrl,
			Integer = isPopup ? 1 : 0
		});
	}

	void OnMainFrameLoaded(CefFrame frame) {
		QueueMessage(new MessageQueue.Message {
			Type = MessageQueue.Type.OnLoadEnd,
			String1 = frame.Url
		});

		frame.ExecuteJavaScript("WebSocket = undefined;", "haha", 0);
	}

	void OnPopupShow(bool show) {
		if (BrowserHost == null || show)
			return;

		PopupX = 0;
		PopupY = 0;
		PopupWide = 0;
		PopupTall = 0;
		PopupData = null;

		BrowserHost.Invalidate(CefPaintElementType.View);
	}

	void OnPopupSize(CefRectangle rect) {
		PopupX = rect.X;
		PopupY = rect.Y;
	}

	void OnPaint(CefPaintElementType type, nint buffer, int width, int height) {
		if (width <= 0 || height <= 0)
			return;

		switch (type) {
			case CefPaintElementType.Popup:
				PopupData = new byte[width * height * 4];
				System.Runtime.InteropServices.Marshal.Copy(buffer, PopupData, 0, PopupData.Length);
				PopupWide = width;
				PopupTall = height;

				BrowserHost?.Invalidate(CefPaintElementType.View);
				return;

			case CefPaintElementType.View:
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

	bool ShouldBlockNavigation(string url) {
		if (!TryParseScheme(url, out string scheme))
			return true;

		if (url == "chrome://credits/")
			return false;

		if (scheme != "http" && scheme != "https" && scheme != "asset")
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

	protected override CefLifeSpanHandler GetLifeSpanHandler() => LifeSpan;
	protected override CefLoadHandler GetLoadHandler() => Load;
	protected override CefDisplayHandler GetDisplayHandler() => Display;
	protected override CefRequestHandler GetRequestHandler() => Request;
	protected override CefContextMenuHandler GetContextMenuHandler() => ContextMenu;
	protected override CefDialogHandler GetDialogHandler() => Dialog;
	protected override CefJSDialogHandler GetJSDialogHandler() => JSDialog;
	protected override CefRenderHandler GetRenderHandler() => Render;

	sealed class LifeSpanHandler(CefBrowserClient owner) : CefLifeSpanHandler
	{
		protected override bool OnBeforePopup(CefBrowser browser, CefFrame frame, int popupId, string targetUrl, string targetFrameName, CefWindowOpenDisposition targetDisposition, bool userGesture, CefPopupFeatures popupFeatures, CefWindowInfo windowInfo, ref CefClient client, CefBrowserSettings settings, ref CefDictionaryValue extraInfo, ref bool noJavascriptAccess) {
			owner.QueueChildView(frame.Url, targetUrl, targetDisposition == CefWindowOpenDisposition.NewPopup);
			return true;
		}

		protected override void OnAfterCreated(CefBrowser browser) => owner.OnAfterCreated(browser);

		protected override bool DoClose(CefBrowser browser) => false;

		protected override void OnBeforeClose(CefBrowser browser) => owner.OnBeforeClose();
	}

	sealed class LoadHandler(CefBrowserClient owner) : CefLoadHandler
	{
		protected override void OnLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType) {
			if (!frame.IsMain)
				return;

			owner.QueueMessage(new MessageQueue.Message {
				Type = MessageQueue.Type.OnLoadStart,
				String1 = frame.Url
			});
		}

		protected override void OnLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode) {
			if (frame.IsMain)
				owner.OnMainFrameLoaded(frame);
		}

		protected override void OnLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl) {
			if (frame.IsMain)
				owner.OnMainFrameLoaded(frame);
		}
	}

	sealed class DisplayHandler(CefBrowserClient owner) : CefDisplayHandler
	{
		protected override void OnAddressChange(CefBrowser browser, CefFrame frame, string url) {
			if (!frame.IsMain)
				return;

			owner.QueueMessage(new MessageQueue.Message {
				Type = MessageQueue.Type.OnAddressChange,
				String1 = url
			});
		}

		protected override void OnTitleChange(CefBrowser browser, string title) {
			owner.QueueMessage(new MessageQueue.Message {
				Type = MessageQueue.Type.OnTitleChange,
				String1 = title
			});
		}

		protected override bool OnConsoleMessage(CefBrowser browser, CefLogSeverity level, string message, string source, int line) {
			owner.QueueMessage(new MessageQueue.Message {
				Type = MessageQueue.Type.OnConsoleMessage,
				String1 = message,
				String2 = source,
				Integer = line,
				Integer2 = (int)level
			});
			return false;
		}

		protected override bool OnCursorChange(CefBrowser browser, nint cursorHandle, CefCursorType type, CefCursorInfo customCursorInfo) {
			IHtmlClientListener.CursorType cursor = type switch {
				CefCursorType.Pointer => IHtmlClientListener.CursorType.Default,
				CefCursorType.Cross => IHtmlClientListener.CursorType.Crosshair,
				CefCursorType.Hand => IHtmlClientListener.CursorType.Hand,
				CefCursorType.IBeam => IHtmlClientListener.CursorType.IBeam,
				CefCursorType.Wait => IHtmlClientListener.CursorType.Hourglass,
				CefCursorType.Move => IHtmlClientListener.CursorType.SizeAll,
				CefCursorType.EastResize or CefCursorType.WestResize or CefCursorType.EastWestResize or CefCursorType.ColumnResize => IHtmlClientListener.CursorType.SizeEW,
				CefCursorType.NorthResize or CefCursorType.SouthResize or CefCursorType.NorthSouthResize or CefCursorType.RowResize => IHtmlClientListener.CursorType.SizeNS,
				CefCursorType.NorthEastResize or CefCursorType.SouthWestResize or CefCursorType.NorthEastSouthWestResize => IHtmlClientListener.CursorType.SizeNESW,
				CefCursorType.NorthWestResize or CefCursorType.SouthEastResize or CefCursorType.NorthWestSouthEastResize => IHtmlClientListener.CursorType.SizeNWSE,
				CefCursorType.NotAllowed => IHtmlClientListener.CursorType.No,
				CefCursorType.None => IHtmlClientListener.CursorType.None,
				CefCursorType.MiddlePanning or CefCursorType.EastPanning or CefCursorType.NorthPanning or CefCursorType.NorthEastPanning or CefCursorType.NorthWestPanning
					or CefCursorType.SouthPanning or CefCursorType.SouthEastPanning or CefCursorType.SouthWestPanning or CefCursorType.WestPanning => IHtmlClientListener.CursorType.SizeAll,
				_ => IHtmlClientListener.CursorType.Default
			};

			owner.QueueMessage(new MessageQueue.Message {
				Type = MessageQueue.Type.OnCursorChange,
				Integer = (int)cursor
			});
			return true;
		}
	}

	sealed class RequestHandler(CefBrowserClient owner) : CefRequestHandler
	{
		readonly ResourceRequestHandler ResourceRequest = new();

		protected override bool OnBeforeBrowse(CefBrowser browser, CefFrame frame, CefRequest request, bool userGesture, bool isRedirect) => owner.ShouldBlockNavigation(request.Url);

		protected override bool OnOpenUrlFromTab(CefBrowser browser, CefFrame frame, string targetUrl, CefWindowOpenDisposition targetDisposition, bool userGesture) {
			if (targetDisposition == CefWindowOpenDisposition.NewPopup || targetDisposition == CefWindowOpenDisposition.NewForegroundTab || targetDisposition == CefWindowOpenDisposition.NewBackgroundTab) {
				owner.QueueChildView(frame.Url, targetUrl, targetDisposition == CefWindowOpenDisposition.NewPopup);
				return true;
			}

			return false;
		}

		protected override CefResourceRequestHandler GetResourceRequestHandler(CefBrowser browser, CefFrame frame, CefRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling) => ResourceRequest;
	}

	sealed class ResourceRequestHandler : CefResourceRequestHandler
	{
		protected override CefCookieAccessFilter GetCookieAccessFilter(CefBrowser browser, CefFrame frame, CefRequest request) => null!;

		protected override CefReturnValue OnBeforeResourceLoad(CefBrowser browser, CefFrame frame, CefRequest request, CefCallback callback) {
			string sourceUrl = request.Url;

			if (!TryParseScheme(sourceUrl, out string scheme))
				return CefReturnValue.Cancel;

			if (sourceUrl == "chrome://credits/" ||
				sourceUrl == "chrome://resources/js/cr.js" ||
				sourceUrl == "chrome://credits/credits.js") {
				return CefReturnValue.Continue;
			}

			if (scheme != "http" && scheme != "https" && scheme != "asset")
				return CefReturnValue.Cancel;

			return CefReturnValue.Continue;
		}
	}

	sealed class ContextMenuHandler : CefContextMenuHandler
	{
		protected override void OnBeforeContextMenu(CefBrowser browser, CefFrame frame, CefContextMenuParams state, CefMenuModel model) => model.Clear();
	}

	sealed class DialogHandler : CefDialogHandler
	{
		protected override bool OnFileDialog(CefBrowser browser, CefFileDialogMode mode, string title, string defaultFilePath, string[] acceptFilters, string[] acceptExtensions, string[] acceptDescriptions, CefFileDialogCallback callback) {
			callback.Cancel();
			return true;
		}
	}

	sealed class JSDialogHandler : CefJSDialogHandler
	{
		protected override bool OnJSDialog(CefBrowser browser, string originUrl, CefJSDialogType dialogType, string message_text, string default_prompt_text, CefJSDialogCallback callback, out bool suppress_message) {
			suppress_message = true;
			return false;
		}

		protected override bool OnBeforeUnloadDialog(CefBrowser browser, string messageText, bool isReload, CefJSDialogCallback callback) {
			callback.Continue(true, "");
			return true;
		}

		protected override void OnResetDialogState(CefBrowser browser) { }

		protected override void OnDialogClosed(CefBrowser browser) { }
	}

	sealed class RenderHandler(CefBrowserClient owner) : CefRenderHandler
	{
		protected override CefAccessibilityHandler GetAccessibilityHandler() => null!;

		protected override void GetViewRect(CefBrowser browser, out CefRectangle rect) => rect = new CefRectangle(0, 0, Math.Max(owner.Wide, 1), Math.Max(owner.Tall, 1));

		protected override bool GetScreenInfo(CefBrowser browser, CefScreenInfo screenInfo) => false;

		protected override void OnPopupShow(CefBrowser browser, bool show) => owner.OnPopupShow(show);

		protected override void OnPopupSize(CefBrowser browser, CefRectangle rect) => owner.OnPopupSize(rect);

		protected override void OnPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, nint buffer, int width, int height) => owner.OnPaint(type, buffer, width, height);

		protected override void OnAcceleratedPaint(CefBrowser browser, CefPaintElementType type, CefRectangle[] dirtyRects, CefAcceleratedPaintInfo info) { }

		protected override void OnScrollOffsetChanged(CefBrowser browser, double x, double y) { }

		protected override void OnImeCompositionRangeChanged(CefBrowser browser, CefRange selectedRange, CefRectangle[] characterBounds) { }
	}
}
