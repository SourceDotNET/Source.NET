using Microsoft.Extensions.DependencyInjection;

using Source.Common.Client;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Launcher;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Source.Engine;

public delegate void GameMessageFn(in InputEvent ev);
public struct GameMessageHandler
{
	public InputEventType EventType;
	public GameMessageFn Function;
	public GameMessageHandler(InputEventType eventType, GameMessageFn function) {
		EventType = eventType;
		Function = function;
	}
}

public class Game : IGame
{
	ILauncherManager? launcherManager;
	readonly Sys Sys;
	readonly IFileSystem fileSystem;
	IInputSystem? inputSystem;
	readonly IMatSystemSurface? surface;
	readonly IEngine eng;
	readonly Host Host;
	IBaseClientDLL? clientDLL;
	readonly IServiceProvider services;
	readonly Key? Key;
	public Game(Host host, Sys Sys, IFileSystem fileSystem, IEngine eng, IServiceProvider services) {
		Host = host;
		this.Sys = Sys;
		this.fileSystem = fileSystem;
		this.eng = eng;
		this.services = services;
		clientDLL = services.GetService<IBaseClientDLL>();
		inputSystem = services.GetService<IInputSystem>();
		surface = services.GetService<IMatSystemSurface>();
		this.Key = services.GetService<Key>();
		launcherManager = OptionalSingleton<ILauncherManager>();
	}
	GameMessageHandler[]? GameMessageHandlers;

	void AppActivate(bool active) {
		if (IsActiveApp() == active)
			return;

		SetCanPostActivateEvents(false);

#if !SWDS
		if (Host.Initialized) {
			ClearIOStates();
			if (!active)
				services.GetRequiredService<IBaseClientDLL>()?.IN_DeactivateMouse();
		}
#endif
		SetActiveApp(active);
		SetCanPostActivateEvents(true);
	}

	private void ClearIOStates() => clientDLL?.IN_ClearStates();
	private void SetCanPostActivateEvents(bool v) {

	}

	public void HandleMsg_ActivateApp(in InputEvent ev) {
		AppActivate(ev.Data != 0);
	}
	public void HandleMsg_WindowMove(in InputEvent ev) { }
	public void HandleMsg_Close(in InputEvent ev) {
		if (eng.GetState() == IEngine.State.Active)
			eng.SetQuitting(IEngine.Quit.ToDesktop);
	}

	public bool CreateGameWindow(int width, int height, bool windowed, bool borderless) {
		GameMessageHandlers = [
			new(InputEventType.App_AppActivated, HandleMsg_ActivateApp),
			new(InputEventType.App_WindowMove, HandleMsg_WindowMove),
			new(InputEventType.App_Close, HandleMsg_Close),
			new(InputEventType.System_Quit, HandleMsg_Close),
		];

		launcherManager = services.GetService<ILauncherManager>();
		if (launcherManager == null) {
			Sys.Error("Tried to call Game.CreateGameWindow without a valid ILauncherManager implementation.");
			return false;
		}

		string windowName = "HALF-LIFE 2";
		{
			KeyValues modinfo = new();
			if (modinfo.LoadFromFile(fileSystem, "gameinfo.txt")) {
				string name = new string(modinfo.GetString("game"));
				if (!string.IsNullOrEmpty(name))
					windowName = name;
			}
		}

		Console.Title = windowName;
		if (!launcherManager.CreateGameWindow(windowName, windowed, borderless, width, height))
			return false;

		SetGameWindow(launcherManager.GetWindow());
		AttachToWindow();

		return true;
	}

	private void AttachToWindow() {
#if !SWDS
		inputSystem.AttachToWindow(window!);
		inputSystem.EnableInput(true);
		inputSystem.EnableMessagePump(false);

		surface.AttachToWindow(window, true);
		surface.EnableWindowsMessages(true);
#endif
	}

	IWindow? window;

	public void DestroyGameWindow() {
		throw new NotImplementedException();
	}

	public void DispatchAllStoredGameMessages() {
#if !SWDS
		foreach (var ev in inputSystem.GetEventData()) {
			DispatchInputEvent(in ev);
		}
#endif
	}

	IEngineVGui? engineVGui;
	[MemberNotNull(nameof(engineVGui))]
	public void FetchEngineVGui() => engineVGui ??= services.GetRequiredService<IEngineVGui>();

	private void DispatchInputEvent(in InputEvent ev) {
		switch (ev.Type) {
			case InputEventType.IE_ButtonPressed:
			case InputEventType.IE_ButtonDoubleClicked:
			case InputEventType.IE_ButtonReleased:
				Key?.Event(in ev);
				break;
			default:
#if GMOD_DLL
				if (ev.Type == InputEventType.IE_AnalogValueChanged && (AnalogCode)ev.Data == AnalogCode.MouseWheel)
					clientDLL?.IN_MouseWheelAnalog(ev.Data3);
#endif
				if (surface?.HandleInputEvent(in ev) ?? false)
					break;

				foreach (GameMessageHandler h in GameMessageHandlers!) {
					if (h.EventType == ev.Type) {
						h.Function(in ev);
						break;
					}
				}

				break;
		}
	}

	uint DesktopWidth;
	uint DesktopHeight;
	uint DesktopRefreshRate;
	public void GetDesktopInfo(out uint width, out uint height, out uint refreshRate) {
#if SWDS
		width = 0;
		height = 0;
		refreshRate = 60;
#else
		// order of initialization means that this might get called early.  In that case go ahead and grab the current
		// screen window and setup based on that.
		// we need to do this when initializing the base list of video modes, for example
		if (DesktopWidth == 0) {
			launcherManager!.GetNativeDisplayInfo(0, out width, out height, out refreshRate);
			return;
		}

		width = DesktopWidth;
		height = DesktopHeight;
		refreshRate = DesktopRefreshRate;
#endif
	}

	void UpdateDesktopInformation() => launcherManager!.GetNativeDisplayInfo(0, out DesktopWidth, out DesktopHeight, out DesktopRefreshRate);

	public IWindow GetMainDeviceWindow() {
		return window!;
	}

	public nint GetMainWindow() {
		throw new NotImplementedException();
	}

	public nint GetMainWindowAddress() {
		throw new NotImplementedException();
	}

	public void GetWindowRect(out int x, out int y, out int w, out int h) {
		throw new NotImplementedException();
	}
	bool ExternallySuppliedWindow = false;
	public bool InputAttachToGameWindow() {
		if (!ExternallySuppliedWindow)
			return true;

#if !SWDS
		surface.OnScreenSizeChanged(((VideoMode_Common)videoMode).GetModeStereoWidth(), ((VideoMode_Common)videoMode).GetModeHeight());
#endif

		AttachToWindow();
		AppActivate(true);

		return true;
	}

	public void InputDetachFromGameWindow() {

	}

	bool ActiveApp;

	public bool IsActiveApp() {
		return ActiveApp;
	}

	public void PlayStartupVideos() {
		throw new NotImplementedException();
	}

	public void SetGameWindow(IWindow window) {
		this.window = window;

		if (DesktopWidth == 0 || DesktopHeight == 0)
			UpdateDesktopInformation();
	}

	public void SetWindowSize(int w, int h) {

	}
	public void SetActiveApp(bool active) {
		ActiveApp = active;
	}
	public void SetWindowXY(int x, int y) {

	}
}
