using Source.Common.GarrysMod;

using Xilium.CefGlue;

namespace HTML.CEF;

public class CefSystem : IHtmlSystem
{
	public static readonly CefSystem g_CefSystem = new();
	public static readonly IHtmlSystem g_pHtmlSystem = g_CefSystem;

	readonly IHtmlSystem Fallback = HTML.Stub.StubSystem.g_pHtmlSystem;
	bool Initialized;

	readonly List<CefHtmlClient> ActiveClients = [];

	readonly Lock RequestsLock = new();
	List<ResourceHandler> Requests = [];

	public bool Init(ReadOnlySpan<char> pBaseDir, IHtmlResourceHandler resourceHandler) {
		ResourceHandler.g_pHtmlResourceHandler = resourceHandler;

		string baseDir = new(pBaseDir);
		string chromiumDir = AppContext.BaseDirectory;

		try {
			if (OperatingSystem.IsWindows())
				CefRuntime.Load(chromiumDir);
			else
				CefRuntime.Load();
		}
		catch (Exception e) {
			resourceHandler.Message($"Chromium failed to load: {e.Message}\n");
			return false;
		}

		CefSettings settings = new() {
			WindowlessRenderingEnabled = true,
			NoSandbox = true,
			MultiThreadedMessageLoop = OperatingSystem.IsWindows(),
			ExternalMessagePump = false,
			CommandLineArgsDisabled = true,
			RemoteDebuggingPort = 0,
			LogSeverity = CefLogSeverity.Default,
			LogFile = Path.Combine(baseDir, "chromium.log"),
			RootCachePath = Path.Combine(baseDir, "cache", "chromium"),
			UserAgent = "Mozilla/5.0 (Windows NT; Valve Source Client) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.198 Safari/537.36 GMod/13",
			ResourcesDirPath = chromiumDir,
			LocalesDirPath = Path.Combine(chromiumDir, "locales"),
			BrowserSubprocessPath = Path.Combine(chromiumDir, OperatingSystem.IsWindows() ? "HTML.CEF.Process.exe" : "HTML.CEF.Process"),
		};

		try {
			CefRuntime.Initialize(new CefMainArgs([]), settings, new CefApplication(), 0);
		}
		catch (Exception e) {
			resourceHandler.Message($"Chromium failed to initialize: {e.Message}\n");
			return false;
		}

		RegisterScheme("garrysmod", new ResourceHandlerFactory());
		RegisterScheme("mapimage", new ResourceHandlerFactory());
		RegisterScheme("html", new HtmlResourceHandlerFactory());

		resourceHandler.Message($"Chromium {CefRuntime.ChromeVersion} initialized.\n");
		Initialized = true;
		return true;
	}

	static void RegisterScheme(string host, CefSchemeHandlerFactory factory) {
		CefRuntime.RegisterSchemeHandlerFactory("asset", host, factory);
		CefRuntime.AddCrossOriginWhitelistEntry($"asset://{host}", "http", "", true);
		CefRuntime.AddCrossOriginWhitelistEntry($"asset://{host}", "https", "", true);
		CefRuntime.AddCrossOriginWhitelistEntry($"asset://{host}", "asset", "", true);
	}

	public void Shutdown() {
		if (!Initialized) {
			Fallback.Shutdown();
			return;
		}

		CefRuntime.Shutdown();
		Initialized = false;
	}

	public IHtmlClient CreateClient(IHtmlClientListener listener) {
		if (!Initialized)
			return Fallback.CreateClient(listener);

		CefBrowserSettings browserSettings = new() {
			DefaultEncoding = "UTF-8",
			WindowlessFrameRate = 60,
			JavaScriptAccessClipboard = CefState.Disabled,
			JavaScriptCloseWindows = CefState.Disabled,
			WebGL = CefState.Disabled
		};

		CefBrowserClient browser = new();
		CefRuntime.PostTask(CefThreadId.UI, new ActionTask(() => browser.CreateBrowser(browserSettings)));

		return new CefHtmlClient(browser, listener);
	}

	public void Update() {
		if (!Initialized) {
			Fallback.Update();
			return;
		}

		if (!OperatingSystem.IsWindows())
			CefRuntime.DoMessageLoopWork();

		List<ResourceHandler> requests;
		lock (RequestsLock) {
			requests = Requests;
			Requests = [];
		}

		foreach (ResourceHandler request in requests)
			request.Handle();

		foreach (CefHtmlClient client in ActiveClients.ToArray())
			client.Update();
	}

	public void OnClientOpen(CefHtmlClient client) => ActiveClients.Add(client);
	public void OnClientClose(CefHtmlClient client) => ActiveClients.Remove(client);

	public void QueueRequest(ResourceHandler resourceHandler) {
		lock (RequestsLock)
			Requests.Add(resourceHandler);
	}

	public JSValue CreateArray(ReadOnlySpan<JSValue> values) => JSArray.Create(values.ToArray());

	public JSValue CreateString(ReadOnlySpan<char> value) => JSString.Create(new string(value));

	public JSValue CreateHashMap(ReadOnlySpan<string> keys, ReadOnlySpan<JSValue> values) {
		Dictionary<string, JSValue> container = new(keys.Length);

		for (int i = 0; i < keys.Length; i++)
			container[keys[i]] = values[i];

		return JSHashMap.Create(container);
	}
}
