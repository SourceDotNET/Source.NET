using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;

using ChromiumProcess;

using Microsoft.Win32.SafeHandles;

using Source.Common.GarrysMod;

using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace HTML.Chromium;

public partial class ChromiumSystem : IHtmlSystem
{
	static readonly bool ResolverInstalled = ModuleResolver.Install();
	public static readonly ChromiumSystem g_ChromiumSystem = new();
	public static readonly IHtmlSystem g_pHtmlSystem = g_ChromiumSystem;

	readonly List<ChromiumClient> ActiveClients = [];

	readonly Lock RequestsLock = new();
	List<ResourceHandler> Requests = [];

	public string BridgeSecret { get; private set; } = "";
	public HtmlBridge.RegistryWriter? Registry { get; private set; }

	static void OnBeforeCommandLineProcessing(CefSettings settings) {
		settings.CefCommandLineArgs.Add("disable-gpu");
		settings.CefCommandLineArgs.Add("disable-gpu-compositing");
		settings.CefCommandLineArgs.Add("disable-smooth-scrolling");
		settings.CefCommandLineArgs.Add("enable-begin-frame-scheduling");
		settings.CefCommandLineArgs.Add("enable-system-flash");

		settings.CefCommandLineArgs["disable-features"] = "TouchpadAndWheelScrollLatching,AsyncWheelEvents";

		settings.CefCommandLineArgs["autoplay-policy"] = "no-user-gesture-required";

		settings.CefCommandLineArgs["enable-blink-features"] = "HTMLImports";

		settings.CefCommandLineArgs["disable-site-isolation-trials"] = "";
	}

	static void OnRegisterCustomSchemes(CefSettings settings) {
		settings.RegisterScheme(new CefCustomScheme("asset", SchemeOptions.Standard | SchemeOptions.CspBypassing));
	}

	[LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16)]
	private static partial uint GetFinalPathNameByHandle(SafeFileHandle hFile, [Out] char[] lpszFilePath, uint cchFilePath, uint dwFlags);

	static string? GetFinalDirectoryPath(string path) {
		try {
			using SafeFileHandle handle = Win32CreateDirectoryHandle(path);
			if (handle.IsInvalid)
				return null;
			char[] buffer = new char[260];
			uint length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, 0);
			if (length == 0 || length >= buffer.Length)
				return null;
			return new string(buffer, 0, (int)length);
		}
		catch (Exception) {
			return null;
		}
	}

	[LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
	private static partial SafeFileHandle CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, nint lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

	static SafeFileHandle Win32CreateDirectoryHandle(string path) {
		const uint GENERIC_READ = 0x80000000;
		const uint FILE_SHARE_READ = 0x00000001;
		const uint OPEN_EXISTING = 3;
		const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
		return CreateFile(path, GENERIC_READ, FILE_SHARE_READ, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
	}

	public bool Init(ReadOnlySpan<char> pBaseDir, IHtmlResourceHandler resourceHandler) {
		ResourceHandler.g_pHtmlResourceHandler = resourceHandler;

		CefSettings settings = new();
		settings.EnableAudio();

		string strBaseDir = new(pBaseDir);
		string moduleDir = Path.GetDirectoryName(typeof(ChromiumSystem).Assembly.Location)!;
		string strChromiumDir = moduleDir;

		settings.RemoteDebuggingPort = 0;
		settings.WindowlessRenderingEnabled = true;
		settings.CommandLineArgsDisabled = true;
		settings.LogSeverity = LogSeverity.Default;

		bool useTempDir = false;

		string? finalBaseDir = GetFinalDirectoryPath(strBaseDir);
		if (finalBaseDir != null) {
			if (finalBaseDir.StartsWith(@"\\?\UNC", StringComparison.Ordinal))
				useTempDir = true;
			else
				strBaseDir = finalBaseDir[4..];
		}

		string? finalChromiumDir = GetFinalDirectoryPath(strChromiumDir);
		if (finalChromiumDir != null) {
			if (finalChromiumDir.StartsWith(@"\\?\UNC", StringComparison.Ordinal))
				useTempDir = true;
			else
				strChromiumDir = finalChromiumDir[4..];
		}

		string chromiumDir = strChromiumDir;

		if (useTempDir) {
			resourceHandler.Message("Copying Chromium resources to temporary directory...\n");

			try {
				string targetPath = Path.Combine(Path.GetTempPath(), "gmod-resources");
				Directory.CreateDirectory(targetPath);

				foreach (string file in Directory.EnumerateFiles(strChromiumDir, "*.pak"))
					File.Copy(file, Path.Combine(targetPath, Path.GetFileName(file)), true);

				string locales = Path.Combine(strChromiumDir, "locales");
				string targetLocales = Path.Combine(targetPath, "locales");
				Directory.CreateDirectory(targetLocales);
				foreach (string file in Directory.EnumerateFiles(locales))
					File.Copy(file, Path.Combine(targetLocales, Path.GetFileName(file)), true);

				chromiumDir = targetPath;
			}
			catch (Exception) {
				return false;
			}
		}

		settings.UserAgent = "Mozilla/5.0 (Windows NT; Valve Source Client) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.198 Safari/537.36 GMod/13";

		settings.ResourcesDirPath = chromiumDir;
		settings.LocalesDirPath = Path.Combine(chromiumDir, "locales");

		settings.MultiThreadedMessageLoop = true;

		settings.LogFile = Path.Combine(strBaseDir, "chromium.log");

		settings.BrowserSubprocessPath = Path.Combine(moduleDir, "ChromiumProcess.exe");

		OnBeforeCommandLineProcessing(settings);
		OnRegisterCustomSchemes(settings);

		BridgeSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
		Environment.SetEnvironmentVariable(HtmlBridge.SecretVariable, BridgeSecret);
		try {
			Registry = new HtmlBridge.RegistryWriter(BridgeSecret);
		}
		catch (Exception) {
			return false;
		}

		CefSharpSettings.ShutdownOnExit = false;

		try {
			if (!Cef.Initialize(settings, performDependencyCheck: false, browserProcessHandler: null))
				return false;
		}
		catch (Exception e) {
			resourceHandler.Message($"Chromium failed to initialize: {e.Message}\n");
			return false;
		}

		{
			ResourceHandlerFactory factory = new();
			IRequestContext context = Cef.GetGlobalRequestContext();

			context.RegisterSchemeHandlerFactory("asset", "garrysmod", factory);
			Cef.AddCrossOriginWhitelistEntry("asset://garrysmod", "http", "", true);
			Cef.AddCrossOriginWhitelistEntry("asset://garrysmod", "https", "", true);
			Cef.AddCrossOriginWhitelistEntry("asset://garrysmod", "asset", "", true);

			context.RegisterSchemeHandlerFactory("asset", "mapimage", factory);
			Cef.AddCrossOriginWhitelistEntry("asset://mapimage", "http", "", true);
			Cef.AddCrossOriginWhitelistEntry("asset://mapimage", "https", "", true);
			Cef.AddCrossOriginWhitelistEntry("asset://mapimage", "asset", "", true);
		}

		{
			HtmlResourceHandlerFactory factory = new();
			IRequestContext context = Cef.GetGlobalRequestContext();

			context.RegisterSchemeHandlerFactory("asset", "html", factory);
			Cef.AddCrossOriginWhitelistEntry("asset://html", "http", "", true);
			Cef.AddCrossOriginWhitelistEntry("asset://html", "https", "", true);
			Cef.AddCrossOriginWhitelistEntry("asset://html", "asset", "", true);
		}

		return true;
	}

	public void Shutdown() {
		Cef.Shutdown();
		Registry?.Dispose();
		Registry = null;
	}

	public IHtmlClient CreateClient(IHtmlClientListener listener) {
		BrowserSettings browserSettings = new() {
			DefaultEncoding = "UTF-8",
			WindowlessFrameRate = 60,
			JavascriptAccessClipboard = CefState.Disabled,
			JavascriptCloseWindows = CefState.Disabled,
			UniversalAccessFromFileUrls = CefState.Disabled,
			FileAccessFromFileUrls = CefState.Disabled,
			WebGl = CefState.Disabled
		};

		ChromiumBrowser cefClient = new(BridgeSecret, Registry);

		cefClient.CreateBrowser(browserSettings);

		return new ChromiumClient(cefClient, listener);
	}

	public void Update() {
		List<ResourceHandler> requests;
		lock (RequestsLock) {
			requests = Requests;
			Requests = [];
		}

		foreach (ResourceHandler request in requests)
			request.Handle();

		foreach (ChromiumClient client in ActiveClients.ToArray())
			client.Update();
	}

	public void OnClientOpen(ChromiumClient client) => ActiveClients.Add(client);
	public void OnClientClose(ChromiumClient client) => ActiveClients.Remove(client);

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
