using Microsoft.Extensions.DependencyInjection;

using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;

using System.Reflection;

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Source.Engine;

public class ClientLauncherAPI(IGame game, IServiceProvider services, Common COM, Sys Sys, EngineParms host_parms, SV SV, IInputSystem inputSystem, MatSysInterface matSys) : IClientLauncherAPI
{
	public void Dispose() {
		((IDisposable)services).Dispose();
		GC.SuppressFinalize(this);
	}

	StartupInfo startupInfo;
	IVideoMode videomode => field ??= Singleton<IVideoMode>()!;


	public bool InitRegistry(ReadOnlySpan<char> modName) {
		Span<char> regSubPath = stackalloc char[260];
		// NOTE: I decided to prefix the path here with sdn_ so we don't touch config for the actual hl2 etc -Callum
		int n = sprintf(regSubPath, "%s\\sdn_%s").S("Source").S(modName);
		return registry.Init(regSubPath[..n]);
	}

	public void ShutdownRegistry() => registry.Shutdown();

	public bool ModInit(string initialMod, string initialGame) {
		host_parms.Mod = initialMod;
		host_parms.Game = initialGame;

		if (cl != null) {
			cl.RestrictServerCommands = false;
			cl.RestrictClientCommands = false;
		}

		InitRegistry(initialMod);

		MaterialSystem_Config config = materials.GetCurrentConfigForVideoCard();
		int width = config.VideoMode.Width;
		int height = config.VideoMode.Height;
		bool windowed = config.Windowed();
		bool borderless = config.NoWindowBorder();

		if (videomode == null)
			return false;

		return videomode.CreateGameWindow(new(width, height, windowed, borderless));
	}



	public void ModShutdown() {
		host_parms.Mod = null!;
		host_parms.Game = null!;
		Singleton<IGame>().InputDetachFromGameWindow();
		ShutdownRegistry();
	}

	Lazy<IEngine> engR = new(services.GetRequiredService<IEngine>);


	public List<MemberInfo> __INTERNAL_FilledDependencies { get; set; } = [];


	public IClientLauncherAPI.Result RunListenServer() {
		IClientLauncherAPI.Result result = IClientLauncherAPI.Result.RunOK;
		if (ModInit(startupInfo.InitialMod, startupInfo.InitialGame)) {
			Mod mod = new Mod(false, this);
			result = (IClientLauncherAPI.Result)mod.Main();
			ModShutdown();
		}
		EngineBuilder.InvalidateEngineDeps(__INTERNAL_FilledDependencies);
		return result;
	}


	public void SetStartupInfo(in StartupInfo info) {
		startupInfo = info;
		Sys.TextMode = info.TextMode;
		COM.InitFilesystem(info.InitialMod);
	}

	public IClientLauncherAPI.Result Run() {
		return Main();
	}

	private IClientLauncherAPI.Result Main() {
		if (!Init())
			return IClientLauncherAPI.Result.InitFailed;

		ConVar.Register();
		return RunListenServer();
	}

	private bool Init() {
		materials.SetMaterialProxyFactory(Shader.s_MaterialProxyFactory);

		// if (!game.Init())
			// goto onStartupError;
		
		if (!videomode.Init()) 
			goto onStartupShutdownGame;
		
		if (!InitRegistry(startupInfo.InitialMod)) 
			goto onStartupShutdownVideoMode;

		materials.ModInit();

#if !SWDS
		matSys.InitMaterialSystemConfig(InEditMode());
#endif

		ShutdownRegistry();
		return true;

	onStartupShutdownVideoMode:
		// videomode.Shutdown();
	onStartupShutdownGame:
		// game.Shutdown();
	onStartupError:
		return false;
	}

	public object? GetService(Type serviceType) => services.GetService(serviceType);
	public object? GetKeyedService(Type serviceType, object? key) => ((IKeyedServiceProvider)services).GetKeyedService(serviceType, key);
	public object GetRequiredKeyedService(Type serviceType, object? key) => ((IKeyedServiceProvider)services).GetRequiredKeyedService(serviceType, key);

	public void PumpMessages() {
#if !SWDS
		launcherMgr.PumpWindowsMessageLoop();
		inputSystem.PollInputState();
#endif
		game.DispatchAllStoredGameMessages();
	}
	public void PumpMessagesEditMode(bool idle, long idleCount) => throw new NotImplementedException();
	public void ActivateEditModeShaders(bool active) { }

	public bool MainLoop() {
		bool idle = true;
		long idleCount = 0;
		while (true) {
			IEngine eng = engR.Value;
			switch (eng.GetQuitting()) {
				case IEngine.Quit.NotQuitting:
					if (!InEditMode())
						PumpMessages();
					else
						PumpMessagesEditMode(idle, idleCount);

					if (!InEditMode()) {
						ActivateEditModeShaders(false);
						eng.Frame();
						ActivateEditModeShaders(true);
					}

					if (InEditMode()) {
						// hammer.RunFrame()? How would this work? todo; learn how editmode works.
					}
					break;
				case IEngine.Quit.ToDesktop: return false;
				case IEngine.Quit.Restart: return true;
			}
		}
	}
}
