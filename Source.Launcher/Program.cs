using Game.Client;
using Game.Lua;
using Game.MenuSystem;
using Game.Server;
using Game.UI;

using Microsoft.Extensions.DependencyInjection;

using Nucleus.SDL3Window;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.Physics;
using Source.Common.ShaderAPI;
using Source.Common.SoundEmitterSystem;
using Source.DataCache;
using Source.Engine;
using Source.FileSystem;
using Source.GUI.Controls;
using Source.MaterialSystem;
using Source.Physics;
using Source.SDLManager;
using Source.ShaderAPI.Gl46;
using Source.SoundEmitterSystem;
using Source.StdShader.Gl46;
using Source.StudioRender;

using Steamworks;

namespace Source.Launcher;


public class Bootloader : IDisposable
{
	ICommandLine commandLine;
	IClientLauncherAPI? engineAPI;

	string baseDir;
	bool isEditMode;
	bool isTextMode;

	public Bootloader() {
		commandLine = new CommandLine();
		commandLine.CreateCmdLine(Environment.CommandLine);
		GetBaseDirectory(commandLine, out baseDir);
		SteamAPI.Init();
		isTextMode = commandLine.CheckParm("-textmode");
	}
	public void Boot() {
		bool needsRestart;
		do {
			engineAPI = new EngineBuilder(commandLine)
				// These assemblies have no reference to them, so they must be manually loaded.
				.WithAssembly("Source.GUI")
				.WithAssembly("Source.GUI.Controls")
				.WithAssembly("Source.VTF")
				.WithAssembly("Game.ServerBrowser")
				// Base file system implementation
				.WithComponent<IFileSystem, BaseFileSystem>()
				// SDL3 specific components. This provides ILauncherManager and IGraphicsProvider (the current implementation merges both classes)
				.WithComponent<SDL3_LauncherManager>()
				.WithResolvedComponent<ILauncherManager, SDL3_LauncherManager>(x => x.GetRequiredService<SDL3_LauncherManager>())
				.WithResolvedComponent<IGraphicsProvider, SDL3_LauncherManager>(x => x.GetRequiredService<SDL3_LauncherManager>())
				.WithComponent<ISystem, SDL3_System>()
				// SDL3 input system
				.WithComponent<IInputSystem, SDL3_InputSystem>()
				// Physics
				.WithComponent<IPhysics, PhysicsInterface>()
				// Rendering abstraction
				.WithFullMaterialSystem()
				.WithComponent<IShaderAPI, ShaderAPIGl46>()
				.WithComponent<ISoundEmitterSystemBase, SoundEmitterSystemBase>()
				// Datacache impl
				.WithComponent<IDataCache, DataCache.DataCache>()
				.WithComponent<IAudioSystem, AudioSystem.AudioSystem>()
				.WithComponent<MDLCache>()
				.WithResolvedComponent<IMDLCache, MDLCache>(x => x.GetRequiredService<MDLCache>())
				.WithResolvedComponent<IStudioDataCache, MDLCache>(x => x.GetRequiredService<MDLCache>())
				#if GMOD_DLL
				.WithComponent<ILuaShared, LuaSharedImpl>()
				.WithComponent<IMenuSystem, MenuSystem>()
				.WithKeyedComponent<IHtmlSystem>("html_chromium", HTML.CEF.CefSystem.g_pHtmlSystem)
				.WithKeyedComponent<IHtmlSystem>("html_stub", HTML.Stub.StubSystem.g_pHtmlSystem)
				#endif
				.WithComponent<IStudioRender, StudioRenderContext>()
				// Our game DLL's. Server/game impl, client impl, UI impl.
				.WithGameDLL<ServerGameDLL>()
				.WithClientDLL<HLClient>()
				.WithGameUIDLL<GameUI>()
				// Shaders we want to load
				.WithStdShader<StdShaderGl46>()
				// Let the engine builder take over and inject engine-specific dependencies
				.BuildClient();

			// Start using this provider for the engine
			using ServiceLocatorScope locatorScope = new(engineAPI);

			// Generate our startup information
			PreInit();

			// Run the game
			var res = engineAPI.Run();

			// If the engine requested a restart, re-loop
			needsRestart = res == IClientLauncherAPI.Result.InitRestart || res == IClientLauncherAPI.Result.RunRestart;
		} while (needsRestart);
	}

	static void GetBaseDirectory(ICommandLine cmdLine, out string baseDirectory) {
		baseDirectory = cmdLine.CheckParm("-basedir", out var values) ? values.FirstOrDefault() ?? AppContext.BaseDirectory : AppContext.BaseDirectory;
	}

	private void PreInit() {
		StartupInfo info = new();
		info.BaseDirectory = baseDir;
		info.InitialMod = DetermineInitialMod();
		info.InitialGame = DetermineInitialGame();
		info.TextMode = isTextMode;

		engineAPI!.SetStartupInfo(in info);

		// Preload VGUI controls
		Panel.InitializeControls();
	}

	const string defaultHalfLife2GameDirectory = "sdn";

	private string DetermineInitialMod() {
		return !isEditMode ? commandLine.ParmValue("-game", defaultHalfLife2GameDirectory) : throw new NotImplementedException("No editmode support");
	}

	private string DetermineInitialGame() {
		return !isEditMode ? commandLine.ParmValue("-game", defaultHalfLife2GameDirectory) : throw new NotImplementedException("No editmode support");
	}
	public void Dispose() {
		SteamAPI.Shutdown();
	}
}

internal class Program
{
	static void Main(string[] _) {
		Platform.Initialize();
		using (Bootloader bootloader = new())
			bootloader.Boot();
	}
}
