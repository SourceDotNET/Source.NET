#if GMOD_DLL

using Microsoft.Extensions.DependencyInjection;

using Source.Common.Filesystem;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;
using Source.Common.Steam;

using Steamworks;

using System.Reflection;
using System.Runtime.InteropServices;

namespace Source.Engine.GarrysMod;

public class Get(IServiceProvider appSystemFactory, EngineParms host_parms) : IGet
{
	IFileSystem? fileSystem;
	ILuaShared? luaShared;
	ILuaConVars? luaConVars;
	IMenuSystem? menuSystem;
	IIntroScreen? introScreen;
	IMaterialSystem? materials;
	IResources? resources;
	IGMod_Audio? audio;
	// IAnalytics? analytics;
	IServerAddons? serverAddons;
	IHtmlSystem? html;
	InlineArrayMaxPath<char> gameDirParent;
	InlineArrayMaxPath<char> gameDir;
	bool filterTextInitialized;
	IMotionSensor? motionSensor;

	static int version;
	static string versionStr = "";
	static string branch = "";
	static string versionTimeStr = "";

	public void OnLoadFailed(ReadOnlySpan<char> module) {
		strcpy(gameDir, host_parms.BaseDir);
		FixSlashes(gameDir, Path.DirectorySeparatorChar);

		if (!IsDedicatedServer() && HasSteamClient())
			SteamApps.MarkContentCorrupt(false);

		if (module.IsEmpty) {
			Error("Startup Failure!");
			return;
		}

		string path = Path.Combine(new string(GameDir()), new string(module));
		string reason = "unknown";
		try {
			NativeLibrary.Load(path);
			reason = "unknown (loaded fine in test)";
		}
		catch (Exception e) {
			reason = e.Message;
		}

		Error($"Couldn't load: {path}\nReason: {reason}\n\nPlease try again.");
		Error("Startup Failure!");
	}

	public ReadOnlySpan<char> GameDir() => ((ReadOnlySpan<char>)gameDir).SliceNullTerminatedString();
	public bool IsDedicatedServer() => sv.IsDedicated();
	public int GetClientCount() => sv.GetClientCount();
	public IFileSystem? FileSystem() => fileSystem;
	public ILuaShared? LuaShared() => luaShared;
	public ILuaConVars? LuaConVars() => luaConVars;
	public IMenuSystem? MenuSystem() => menuSystem;
	public IResources? Resources() => resources;
	public IIntroScreen? IntroScreen() => introScreen;
	public IMaterialSystem? Materials() => materials;
	public IServerAddons? ServerAddons() => serverAddons;
	public IHtmlSystem? HTML() => html;

	public ISteamHTTP? SteamHTTP() {
#if !SWDS
		if (!IsDedicatedServer()) {
			ISteamHTTP? http = Steam3Client().SteamHTTP();
			if (http != null)
				return http;
		}
#endif
		return Steam3Server().SteamHTTP();
	}

	public ISteamUtils? SteamUtils() {
#if !SWDS
		if (!IsDedicatedServer()) {
			ISteamUtils? utils = Steam3Client().SteamUtils();
			if (utils != null)
				return utils;
		}
#endif
		return Steam3Server().SteamGameServerUtils();
	}

	public ISteamUGC? SteamUGC() {
#if !SWDS
		if (!IsDedicatedServer()) {
			ISteamUGC? ugc = Steam3Client().SteamUGC();
			if (ugc != null)
				return ugc;
		}
#endif
		return Steam3Server().SteamUGC();
	}

	public ISteamNetworking? SteamNetworking() {
#if !SWDS
		if (!IsDedicatedServer()) {
			ISteamNetworking? networking = Steam3Client().SteamNetworking();
			if (networking != null)
				return networking;
		}
#endif
		return Steam3Server().SteamGameServerNetworking();
	}

	public void Initialize(IFileSystem fileSystem) {
		this.fileSystem = fileSystem;

		luaShared = appSystemFactory.GetRequiredService<ILuaShared>();
		luaConVars = appSystemFactory.GetRequiredService<ILuaConVars>();

		luaShared.Init(appSystemFactory, false, this);
		luaConVars.Init();

		if (!IsDedicatedServer()) {
			if (OperatingSystem.IsWindowsVersionAtLeast(6, 1)) {
				// todo: analytics
			}

			menuSystem = appSystemFactory.GetRequiredService<IMenuSystem>();
			introScreen = appSystemFactory.GetRequiredService<IIntroScreen>();
			materials = appSystemFactory.GetRequiredService<IMaterialSystem>();
			resources = appSystemFactory.GetRequiredService<IResources>();
			// todo: audio
			serverAddons = appSystemFactory.GetRequiredService<IServerAddons>();

			if (html == null) {
				if (!commandLine.CheckParm("-nochromium")) {
					Msg("Attempting to load Chromium...\n");
					LoadHTML("html_chromium");
				}

				// if (html == null && !commandLine.CheckParm("-noawesomium")) {
					// Msg("Attempting to load Awesomium...\n");
					// todo: LoadHTML("html_awesomium.dll");
				// }
			}

			if (html == null) {
				Msg("Attempting to load Stub...\n");
				LoadHTML("html_stub");
				if (html == null)
					Error("Couldn't find an HTML system.");
			}
		}

		strcpy(gameDir, Common.Gamedir);
		FixSlashes(gameDir, Path.DirectorySeparatorChar);
		strcpy(gameDirParent, Common.Gamedir);
		FixSlashes(gameDirParent, Path.DirectorySeparatorChar);
		StripLastDir(gameDirParent);
	}

	bool LoadHTML(string name) {
		html = appSystemFactory.GetKeyedService<IHtmlSystem>(name);
		return html != null;
	}

	static void FixSlashes(Span<char> str, char separator) {
		for (int i = 0; i < str.Length && str[i] != '\0'; i++) {
			if (str[i] == '/' || str[i] == '\\')
				str[i] = separator;
		}
	}

	static void StripLastDir(Span<char> str) {
		int i = ((ReadOnlySpan<char>)str).SliceNullTerminatedString().Length - 1;
		if (i <= 0)
			return;

		while (i > 0) {
			if (str[i] == '\\' || str[i] == '/')
				break;
			i--;
		}

		str[i] = '\0';
	}

	public void ShutDown() {
		fileSystem = null;
		menuSystem = null;
		luaShared = null;
		materials = null;
		introScreen = null;
		resources = null;
		serverAddons = null;
	}

	public void RunSteamCallbacks() {
		if (IsDedicatedServer())
			SteamGameServer_RunCallbacks();
		else
			SteamAPI.RunCallbacks();
	}

	public void SetMotionSensor(IMotionSensor? sensor) => motionSensor = sensor;
	public IMotionSensor? MotionSensor() => motionSensor;

	public int Version() {
		if (version == 0 && FileSystem() != null) {
			using (IFileHandle? file = FileSystem()!.Open("garrysmod.ver", FileOpenOptions.Read, "MOD")) {
				if (file != null) {
					Span<char> line = stackalloc char[64];
					line[0] = '\0';
					FileSystem()!.ReadLine(line, file);
					version = atoi(((ReadOnlySpan<char>)line).SliceNullTerminatedString());
				}
			}

			if (version == 0)
				version = 1;
		}

		return version;
	}

	public ReadOnlySpan<char> VersionStr() {
		if (versionStr.Length == 0 && Version() > 0) {
			int v = Version();
			versionStr = $"20{v / 10000:00}.{v % 10000 / 100:00}.{v % 100:00}";
		}

		return versionStr;
	}

	public ReadOnlySpan<char> Branch() {
		if (branch.Length != 0)
			return branch;

		if (IsDedicatedServer() || !HasSteamClient()) {
			using IFileHandle? file = FileSystem()!.Open("garrysmod.ver", FileOpenOptions.Read, "MOD");
			if (file == null)
				return branch;

			Span<char> line = stackalloc char[64];
			line[0] = '\0';
			FileSystem()!.ReadLine(line, file);
			FileSystem()!.ReadLine(line, file);
			FileSystem()!.ReadLine(line, file);
			branch = new string(((ReadOnlySpan<char>)line).SliceNullTerminatedString()).Trim(" \n\t\r".ToCharArray());

			if (IsDedicatedServer() && branch == "prerelease")
				branch = "unknown";

			return branch;
		}

		branch = "unknown";
		if (SteamApps.GetCurrentBetaName(out string betaName, 64))
			branch = betaName;

		return branch;
	}

	public IGMod_Audio? Audio() => audio;

	public ReadOnlySpan<char> VersionTimeStr() {
		if (versionTimeStr.Length != 0 || FileSystem() == null)
			return versionTimeStr;

		using (IFileHandle? file = FileSystem()!.Open("garrysmod.ver", FileOpenOptions.Read, "MOD")) {
			if (file != null) {
				Span<char> line = stackalloc char[512];
				line[0] = '\0';
				FileSystem()!.ReadLine(line, file);
				FileSystem()!.ReadLine(line, file);
				versionTimeStr = new string(((ReadOnlySpan<char>)line).SliceNullTerminatedString());
			}
		}

		if (versionTimeStr.Length == 0)
			versionTimeStr = "error";

		return versionTimeStr;
	}

	public void UpdateRichPresense(ReadOnlySpan<char> status) {
		if (!HasSteamClient())
			return;

		SteamFriends.SetRichPresence("steam_display", "#Status_Generic");
		SteamFriends.SetRichPresence("generic", new(status));
		SteamFriends.SetRichPresence("status", new(status));
	}

	public void ResetRichPresense() {
		if (!HasSteamClient())
			return;

		SteamFriends.SetRichPresence("steam_display", "#Status_InMenu");
		SteamFriends.SetRichPresence("status", "In menus");
		SteamFriends.SetRichPresence("generic", null);
	}

	public ReadOnlySpan<char> GameDirParent() => ((ReadOnlySpan<char>)gameDirParent).SliceNullTerminatedString();

	public void FilterText(ReadOnlySpan<char> input, Span<char> output, ETextFilteringContext context, CSteamID sourceSteamID) {
		strcpy(output, input);

		if (!filterTextInitialized && SteamUtils() != null) {
			if (!SteamUtils()!.InitFilterText(0))
				Warning("SteamUtils->InitFilterText failed for current language. Text filtering will not work.");
			filterTextInitialized = true;
		}

		if (SteamUtils() != null) {
			SteamUtils()!.FilterText(context, sourceSteamID, input, out string filteredText, (uint)output.Length);
			strcpy(output, filteredText);
		}

		Span<char> filtered = output[..((ReadOnlySpan<char>)output).SliceNullTerminatedString().Length];
		string text = new string(filtered).ToLowerInvariant();
		text = text.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
		text = text.Replace("1", "i").Replace("4", "a").Replace("3", "e").Replace("7", "t").Replace("0", "o");

		bool child = text.Contains("child") || !stristr(filtered, "12yo").IsEmpty || !stristr(filtered, "11yo").IsEmpty;
		bool porn = text.Contains("porn") || text.Contains("pron");
		bool link = text.Contains("link") || text.Contains("http");

		if (child && (porn || link || text.Contains("rape")))
			strcpy(output, "*****");
	}
}
#endif
