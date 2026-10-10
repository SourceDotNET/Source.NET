#if CLIENT_DLL || GAME_DLL
global using static Game.Client.GarrysMod.GarrysModSingletons;

using Game.Shared;

using Microsoft.Extensions.DependencyInjection;

using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.Common.MaterialSystem;
using Source.Common.Networking;

using Steamworks;

using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Client.GarrysMod;

public static class GarrysModSingletons
{
	public static readonly GarrysMod garrysmod = new();
}

public class GarrysMod : IGarrysMod
{
	public static readonly ConVar lua_strict = new("lua_strict", "0", FCvar.Replicated | FCvar.Notify, "Enable extra checks for Lua API, such as argument type checking, errors that normally would be silent, etc. Useful to catch bugs in code when developing addons.");
	public static readonly ConVar sv_allowcslua = new("sv_allowcslua", "0", FCvar.Archive | FCvar.Notify | FCvar.Replicated, "Allow clients to run clientside addons. This will override any gamemode setting!");

	public void DLLInit(IServiceCollection services) {
#if CLIENT_DLL
		services.AddSingleton<IIntroScreen, IntroScreen>();
		services.AddSingleton<IServerAddons, ServerAddons>();
#else

#endif
	}

	public void EndVideoScale(int unk1, int unk2) {
		throw new NotImplementedException();
	}

	public void FireGameEvent(IGameEvent ev) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetMapName() {
		throw new NotImplementedException();
	}



#if CLIENT_DLL
	void AddLanguageFiles(ReadOnlySpan<char> language) {
		ReadOnlySpan<char> fileName = filesystem.FindFirstEx($"resource/language/*{language}.txt", null, out ulong handle);
		while (!fileName.IsEmpty) {
			string path = $"resource/language/{fileName}";
			if (!localize.AddFile(path, null, false)) {
				Warning($"Failed to add language file '{path}', trying different method...\n");
				KeyValues kv = new("");
				bool loaded = kv.LoadFromFile(filesystem, path, null);
				KeyValues? tokens = kv.FindKey("Tokens", false);
				if (tokens == null)
					Warning($"Failed to load language file '{path}' - {(loaded ? 1 : 0)}\n");
				else {
					for (KeyValues? token = tokens.GetFirstSubKey(); token != null; token = token.GetNextKey())
						localize.AddString(token.Name, token.GetString(), path);
				}
			}
			fileName = filesystem.FindNext(handle);
		}
		filesystem.FindClose(handle);
	}
#endif

	public void InitializeMod(IServiceProvider services) {
#if !SWDS
		get.IntroScreen()?.Update("Adding Custom Fonts", true);
		// todo: AddCustomFonts
#if CLIENT_DLL
		if (filesystem == null)
			Error("g_pFullFileSystem is NULL!?");
		if (filesystem!.Language() == null)
			Error("->Language() is NULL!?");

		Span<char> uiLanguage = stackalloc char[64];
		uiLanguage.Clear();
		engine.GetUILanguage(uiLanguage);
		filesystem.Language().ChangeLanguage_Steam(uiLanguage.SliceNullTerminatedString());
#endif
		get.IntroScreen()!.Update("Adding Language Files", true);
#if CLIENT_DLL
		AddLanguageFiles("english");
		if (uiLanguage[0] != '\0' && stricmp(uiLanguage.SliceNullTerminatedString(), "english") != 0)
			AddLanguageFiles(uiLanguage.SliceNullTerminatedString());
#endif
		get.IntroScreen()!.Update("Setup Menu System", true);
		// todo: menu system init
		get.IntroScreen()!.Update("Setting Convar Defaults", true);
		// todo: convar defaults
#endif
#if CLIENT_DLL
		string absPath = $"{engine.GetGameDirectory()}/cache";
#else // TODO: This is really stupid. Why is server different here in the interface. This deserves deviation.
		Span<char> path = stackalloc char[MAX_PATH];
		engine.GetGameDir(path);
		string absPath = $"{path.SliceNullTerminatedString()}/cache";
#endif
		Directory.CreateDirectory(absPath);
		Directory.CreateDirectory(Path.Combine(absPath, "lua"));
		Directory.CreateDirectory(Path.Combine(absPath, "workshop"));
		filesystem.AddSearchPath(absPath, "CACHE");

		filesystem.CreateDirHierarchy("data/", "MOD");
		Span<char> dataPath = stackalloc char[0x104];
		ReadOnlySpan<char> dataFullPath = filesystem.RelativePathToFullPath("data", "MOD", dataPath);
		filesystem.AddSearchPath(dataFullPath, "DATA", SearchPathAdd.ToTail, PathGroupName.Fallbacks);
		filesystem.MarkPathIDByRequestOnly("DATA", true);
	}

#if CLIENT_DLL
	public void LevelInit(ReadOnlySpan<char> mapName) {
		get.Audio()?.StopAllPlayback();

		engine.ClientCmd("net_maxroutable 1260");
		// todo: AddCustomFonts();

		if (gpGlobals.MaxClients > 1) {
			// todo: ParseParticleEffects(true, false);
		}

		string status = (gpGlobals.MaxClients < 2 ? "Singleplayer - " : "Multiplayer - ") + mapName.ToString();

		string? gamemodeName = null; // todo: the client Lua gamemode's name (g_pGamemode)
		if (gamemodeName != null) {
			status += " (";
			IGamemodeSystem.Information info = filesystem.Gamemodes().FindByName(gamemodeName);
			string title;
			if (info.Exists)
				title = info.Title;
			else {
				string name = gamemodeName.Replace("_modded", "");
				info = filesystem.Gamemodes().FindByName(name);
				title = info.Exists ? info.Title : name;
			}
			status += title;
			status += ")";
		}

		get.UpdateRichPresense(status);
	}

	public bool BlockRetryCommand;

	public static bool RunningLuaCmd;
	public static bool RunningNetMessage;
	static readonly byte[] LuaCmd = new byte[0x1800];

	public static void RunLuaCmd(bf_read buffer) {
		RunningLuaCmd = true;

		if (!buffer.ReadString(LuaCmd, false, out int length)) {
			Warning("SendLua/BroadcastLua/lua_run_cl failed to read the code!\n");
			RunningLuaCmd = false;
			return;
		}

		string code = Encoding.UTF8.GetString(LuaCmd, 0, length);
		if (!g_Lua!.RunString("LuaCmd", "", code, true, true))
			Warning($"SendLua/BroadcastLua/lua_run_cl failed with code: {code}\n");

		RunningLuaCmd = false;
	}
#else
	public void LevelInit(ReadOnlySpan<char> mapName, ReadOnlyMemory<byte> mapEntities, ReadOnlySpan<char> oldLevel, ReadOnlySpan<char> landmarkName, bool loadGame, bool background) {
		if (gpGlobals.MaxClients == 1 || !get.IsDedicatedServer())
			engine.ServerCommand("lua_error_url ''\n");

		Lua.Create();
		Lua.OnLoaded();
	}
#endif

#if CLIENT_DLL
	public const string LuaPathID = "lcl";
#else
	public const string LuaPathID = "lsv";
#endif

	public static LuaManager? g_LuaManager;

	public static class Lua
	{
		public static bool Kill() {
#if CLIENT_DLL
			// todo: remove the Lua panels parented to the client DLL root panel
#endif
			if (g_LuaManager != null) {
				get.LuaShared()!.UnMountLua(LuaPathID);
				g_LuaManager.Shutdown();
				g_LuaManager = null;
			}
			gGM?.Dispose();
#if CLIENT_DLL
			LuaTimer.Shutdown();
#else
			Game.Server.GarrysMod.LuaTimer.Shutdown();
#endif
			return true;
		}

		public static bool Create() {
			Kill();
#if CLIENT_DLL
			filesystem.Language().ReloadLanguage();
#endif

			foreach (ILegacyAddons.Information addon in filesystem.LegacyAddons().GetList()) {
				if (!string.IsNullOrEmpty(addon.LuaPath))
					get.LuaShared()!.MountLuaAdd(addon.LuaPath, LuaPathID);
				if (!string.IsNullOrEmpty(addon.GamemodesPath))
					get.LuaShared()!.MountLuaAdd(addon.GamemodesPath, LuaPathID);
			}
			get.LuaShared()!.MountLuaAdd("workshop/lua", LuaPathID);
			get.LuaShared()!.MountLuaAdd("workshop/gamemodes", LuaPathID);
			get.LuaShared()!.MountLua(LuaPathID);

			if (g_LuaManager != null)
				Error("New gLUA when old one exists!\n");
			g_LuaManager = new LuaManager();
			if (gGM != null)
				Error("New gGM when old one exists!\n");
			gGM = new();
			g_LuaManager.Startup();
#if GAME_DLL
			gGM.LoadCurrentlyActiveGamemode();
			Game.Server.GarrysMod.GModDataPack.DataPack().BuildSearchPaths();
#endif
			return true;
		}

#if GAME_DLL
		public static void OnLoaded() {
			// GarrysMod.Ammo.Refresh();
		}
#endif
	}

	public class LuaManager
	{
		public bool RunScript(ReadOnlySpan<char> file, ReadOnlySpan<char> pathId, bool run, ReadOnlySpan<char> source) {
			if (g_Lua == null)
				return false;
			return g_Lua.FindAndRunScript(file, run, true, source, true);
		}

		public bool ScriptExists(ReadOnlySpan<char> file, ReadOnlySpan<char> pathId) => get.LuaShared()!.LoadFile(file, pathId, false, true) != null;

		public void Startup() {
#if CLIENT_DLL
			Msg("Clientside Lua startup!\n");
			enginevgui.UpdateCustomProgressBar(0.95f, "Starting Lua...");
#endif
#if CLIENT_DLL
			if (LuaNetworkedVars.g_LuaNetworkedVars != null)
				Error("g_LuaNetworkedVars");
			LuaNetworkedVars.g_LuaNetworkedVars = new LuaNetworkedVars();
#else
			if (Game.Server.GarrysMod.LuaNetworkedVars.g_LuaNetworkedVars != null)
				Error("g_LuaNetworkedVars");
			Game.Server.GarrysMod.LuaNetworkedVars.g_LuaNetworkedVars = new Game.Server.GarrysMod.LuaNetworkedVars();
#endif
			if (g_Lua != null)
				Error("CLuaManager::Startup Lua already exsits?\n");

			if (commandLine.FindParm("-nolua") != 0) {
				g_Lua = new Source.Common.GarrysMod.Lua.LuaNullInterface();
				g_Lua.SetPathID(LuaPathID);
#if CLIENT_DLL
				g_Lua.SetType(0);
#else
				g_Lua.SetType(1);
#endif
				return;
			}

#if CLIENT_DLL
			g_Lua = get.LuaShared()!.CreateLuaInterface(Realm.Client, false);
			g_Lua.Init(LuaGameCallback.g_LuaCallback, Singleton<ICommandLine>().CheckParm("-withjit"));
			g_Lua.SetPathID(LuaPathID);
			g_Lua.SetType(0);
#else
			g_Lua = get.LuaShared()!.CreateLuaInterface(Realm.Server, false);
			g_Lua.Init(Game.Server.GarrysMod.LuaGameCallback.g_LuaCallback, Singleton<ICommandLine>().CheckParm("-withjit"));
			g_Lua.SetPathID(LuaPathID);
			g_Lua.SetType(1);
#endif
			g_Lua.Global().SetMember("VERSION", (float)get.Version());
			g_Lua.Global().SetMember("VERSIONSTR", get.VersionStr());
			g_Lua.Global().SetMember("BRANCH", get.Branch());
#if GAME_DLL
			Game.Server.GarrysMod.FileServ.Add("lua/send.txt");
#endif
#if CLIENT_DLL
			LuaUser.InitLuaLibraries(g_Lua);
			LuaClass.InitLuaClasses(g_Lua);
#else
			Game.Server.GarrysMod.LuaUser.InitLuaLibraries(g_Lua);
			Game.Server.GarrysMod.LuaClass.InitLuaClasses(g_Lua);
#endif
#if CLIENT_DLL
			g_Lua.Global().SetMember("SERVER", false);
			g_Lua.Global().SetMember("CLIENT", true);
#else
			g_Lua.Global().SetMember("SERVER", true);
			g_Lua.Global().SetMember("CLIENT", false);
#endif
#if CLIENT_DLL
			LuaEntity.MakeLuaNULLEntity();
#else
			Game.Server.GarrysMod.LuaEntity.MakeLuaNULLEntity();
#endif
			g_Lua.FindAndRunScript("includes/init.lua", true, true, "!UNKNOWN", true);
#if CLIENT_DLL
			if (gGM == null)
				Error("We should have a gGM at this point!");
			g_Lua.FindAndRunScript("derma/init.lua", true, true, "!UNKNOWN", true);
			g_Lua.RunString("Startup", "", "require('notification');", true, true);
			gGM.LoadGamemode("base", false);
			RunScriptsInFolder("autorun", "!RELOAD");
			RunScriptsInFolder("autorun/client", "!RELOAD_CL");
			RunScriptsInFolder("postprocess", "!RELOAD_CL");
			RunScriptsInFolder("vgui", "!RELOAD_CL");
			RunScriptsInFolder("matproxy", "!RELOAD_CL");
			g_Lua.FindAndRunScript("skins/default.lua", true, true, "!UNKNOWN", true);
			enginevgui.UpdateCustomProgressBar(0.96f, "Lua Started!");
#else
			if (gGM == null)
				Error("We should have a gGM at this point!");
			gGM.LoadGamemode("base", false);
			RunScriptsInFolder("autorun", "!RELOAD");
			SendScriptsInFolder("matproxy", "!RELOAD_CL");
			SendScriptsInFolder("postprocess", "!RELOAD_CL");
			SendScriptsInFolder("vgui", "!RELOAD_CL");
			SendScriptsInFolder("skins", "!RELOAD_CL");
			SendScriptsInFolder("autorun", "!RELOAD");
			SendScriptsInFolder("autorun/client", "!RELOAD_CL");
			RunScriptsInFolder("autorun/server", "!RELOAD_SV");
			RunScriptsInFolder("autorun/server/sensorbones", "!RELOAD_SV");
			if (commandLine.FindParm("-systemtest") != 0)
				g_Lua.FindAndRunScript("includes/dev_server_test.lua", true, true, "!UNKNOWN", true);
#endif
		}

#if GAME_DLL
		public void SendScriptsInFolder(ReadOnlySpan<char> folder, ReadOnlySpan<char> source) {
			if (g_LuaManager == null)
				return;

			List<LuaFindResult> files = [];
			get.LuaShared()!.FindScripts($"{folder}/*.lua", "lsv", files);
			foreach (LuaFindResult file in files)
				Game.Server.GarrysMod.FileServ.AddCSLuaFile($"{folder}/{file.FileName}", new string(source));
		}
#endif

		public void RunScriptsInFolder(ReadOnlySpan<char> folder, ReadOnlySpan<char> source) {
			if (g_LuaManager == null) {
				Warning($"RunScriptsInFolder: Couldn't run scripts in '{folder}': no gLUA!\n");
				return;
			}

			List<LuaFindResult> files = [];
			get.LuaShared()!.FindScripts($"{folder}/*.lua", LuaPathID, files);
			foreach (LuaFindResult file in files) {
				string path = $"{folder}/{file.FileName}";
				g_Lua?.FindAndRunScript(path, true, true, source.IsEmpty ? "!UNKNOWN" : source, true);
			}
		}

		public void Shutdown() {
#if CLIENT_DLL
			GModBase.Shutdown();
#endif
			g_LuaID++;
#if CLIENT_DLL
			LuaClass.ShutdownLuaClasses(g_Lua!);
#else
			Game.Server.GarrysMod.LuaClass.ShutdownLuaClasses(g_Lua!);
#endif
			get.LuaShared()!.CloseLuaInterface(g_Lua!);
			g_Lua = null;
#if CLIENT_DLL
			if (LuaNetworkedVars.g_LuaNetworkedVars == null)
				Error("!g_LuaNetworkedVars");
			LuaNetworkedVars.g_LuaNetworkedVars = null;
#else
			if (Game.Server.GarrysMod.LuaNetworkedVars.g_LuaNetworkedVars == null)
				Error("!g_LuaNetworkedVars");
			Game.Server.GarrysMod.LuaNetworkedVars.g_LuaNetworkedVars = null;
#endif
		}
	}

#if GAME_DLL
	static bool IsGModAdmin(bool unk) {
		if (gpGlobals.MaxClients == 1)
			return true;

		int index = Util.GetCommandClientIndex();
		if (engine.IsDedicatedServer() && index <= 0)
			return true;

		if (engine.IsDedicatedServer())
			return false;

		return index == 1;
	}

	[ConCommand("lua_run", "Run a Lua command", FCvar.DontRecord)]
	static void CC_LuaRun(in TokenizedCommand args) {
		if (!IsGModAdmin(true) || g_LuaManager == null || args.ArgC() <= 1)
			return;

		Msg($"> {args.ArgS()}...\n");
		g_Lua!.RunString("lua_run", "", args.ArgS(), true, true);
	}

	static readonly byte[] BroadcastLuaData = new byte[0x1800];
	static readonly bf_write BroadcastLuaWrite = new();

	public static void BroadcastLua(Game.Server.RecipientFilter filter, ReadOnlySpan<char> code) {
		BroadcastLuaWrite.DebugName = "BroadcastLua";
		BroadcastLuaWrite.StartWriting(BroadcastLuaData, 0x1800, 0);
		BroadcastLuaWrite.WriteByte((int)GModMessageType.LuaCmd);

		byte[] bytes = Encoding.UTF8.GetBytes(code.ToString() + "\0");
		BroadcastLuaWrite.WriteBytes(bytes);
		if (BroadcastLuaWrite.Overflowed) {
			Warning($"BroadcastLua failed to write code! Is it too long? {bytes.Length - 1}, {0x1800} max\n");
			return;
		}

		engine.GMOD_SendToClient(ref filter, BroadcastLuaData.AsSpan(0, BroadcastLuaWrite.BytesWritten), BroadcastLuaWrite.BitsWritten);
	}

	[ConCommand("lua_run_cl", "Run a Lua command", FCvar.DontRecord)]
	static void CC_LuaRun_cl(in TokenizedCommand args) {
		ConVar sv_cheats = cvar.FindVar("sv_cheats")!;

		if (args.ArgC() <= 1)
			return;

		BasePlayer? player = Util.GetCommandClient();
		if (player == null)
			return;

		if (!sv_allowcslua.GetBool() && !sv_cheats.GetBool())
			return;

		Game.Server.RecipientFilter filter = new Game.Server.SingleUserRecipientFilter(player);
		filter.MakeReliable();
		BroadcastLua(filter, args.ArgS());
	}
#endif

	public void MD5String(Span<byte> outMD5, ReadOnlySpan<byte> unk1, ReadOnlySpan<byte> unk2, ReadOnlySpan<byte> unk3) {
		throw new NotImplementedException();
	}

	public void PlaySound(ReadOnlySpan<char> sound) {
		throw new NotImplementedException();
	}

	public void RunConsoleCommand(ReadOnlySpan<char> cmd) {
		throw new NotImplementedException();
	}

	public void StartVideoScale(int unk1, int unk2) {
		throw new NotImplementedException();
	}

	public void Think() {
		if (g_Lua != null) {
			// CheckForFilesystemChanges();
			g_Lua.Cycle();
#if CLIENT_DLL
			LuaTimer.Cycle();
			LuaFileLibrary.AsyncCycle();
#else
			Game.Server.GarrysMod.LuaNetworkedVars.g_LuaNetworkedVars?.Cycle();
			Game.Server.GarrysMod.LuaTimer.Cycle();
			Game.Server.GarrysMod.LuaFileLibrary.AsyncCycle();
#endif
			// HTTP.Cycle();
			// ErrorReporter.RunQueue();
		}
		// AutoRefresh.Cycle();
		// GMOD_SpewBufferFlush();
	}
}

public class GModRichPresence : AutoGameSystemPerFrame // callum TODO, remove this once the actual gmod impl is complete
{
	private static readonly GModRichPresence _ = new();

	string LastStatus = "";
	TimeUnit_t LastRun;

	public override ReadOnlySpan<char> Name() => "GModRichPresence";

	public override bool Init() {
		LastStatus = "";
		return true;
	}

	public override void Shutdown() {
		if (SteamAPI.IsSteamRunning())
			SteamFriends.ClearRichPresence();
	}

#if CLIENT_DLL
	public override void Update(TimeUnit_t frametime) {
		if (gpGlobals.RealTime < LastRun + 2.0f) return;
		LastRun = gpGlobals.RealTime;

		if (!SteamAPI.IsSteamRunning())
			return;

		if (!engine.IsConnected()) {
			SetStatus("In Menus");
			return;
		}

		if (!engine.IsInGame()) {
			SetStatus("Joining a server");
			return;
		}

		bool multiplayer = engine.GetMaxClients() > 1;
		string? connect = multiplayer && engine.GetNetChannelInfo() is INetChannel netchan && netchan.GetRemoteAddress() is NetAddress address ? $"+connect {address.ToString(false)}" : null;

		SetStatus($"{(multiplayer ? "Multiplayer" : "Singleplayer")} - {GetMapName()} ({GetGamemodeName()})", connect);
	}
#endif

	void SetStatus(string status, string? connect = null) {
		if (status == LastStatus)
			return;
		LastStatus = status;

		SteamFriends.SetRichPresence("status", status);
		SteamFriends.SetRichPresence("Generic", status);
		SteamFriends.SetRichPresence("steam_display", "#Status_Generic");
		SteamFriends.SetRichPresence("connect", connect);
	}

	static ReadOnlySpan<char> GetGamemodeName() {
		return "GAMEMODE"; // TODO
	}

#if CLIENT_DLL
	static ReadOnlySpan<char> GetMapName() {
		ReadOnlySpan<char> level = engine.GetLevelName().SliceNullTerminatedString();
		if (level.IsEmpty)
			return level;

		int slash = level.LastIndexOfAny('/', '\\');
		if (slash >= 0) level = level[(slash + 1)..];

		int dot = level.LastIndexOf('.');
		if (dot >= 0) level = level[..dot];

		return level;
	}
#endif
}
#endif
