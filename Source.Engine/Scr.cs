using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Client;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.MaterialSystem;
using Source.Engine.Client;

namespace Source.Engine;

/// <summary>
/// Screen library.
/// </summary>
public class Scr(IEngineAPI engineAPI)
{
	public bool Initialized;
	public bool DisabledForLoading;
	public bool DrawLoading;
	public int NextDrawTick;

	ClientDLL ClientDLL = null!;
	Host Host = null!;
	Sys Sys = null!;
	CL CL = null!;
	IEngineVGuiInternal? EngineVGui = null;
	Con Con = null!;
	Common Common = null!;
	Render Render = null!;
	View View = null!;
	Sound Sound = null!;
	IHostState HostState = null!;
	IFileSystem FileSystem = null!;
	IBaseClientDLL clientDll = null!;
	ClientGlobalVariables clientGlobalVariables = null!;
	IMaterialSystem materials = null!;
	IMDLCache MDLCache = null!;
	public void Init() {
		Initialized = true;

		ClientDLL = engineAPI.GetRequiredService<ClientDLL>();
		Host = engineAPI.GetRequiredService<Host>();
		Common = engineAPI.GetRequiredService<Common>();
		View = engineAPI.GetRequiredService<View>();
		Sound = engineAPI.GetRequiredService<Sound>();
		Render = engineAPI.GetRequiredService<Render>();
		Sys = engineAPI.GetRequiredService<Sys>();
		EngineVGui = engineAPI.GetRequiredService<IEngineVGuiInternal>();
		Con = engineAPI.GetRequiredService<Con>();
		HostState = engineAPI.GetRequiredService<IHostState>();
		FileSystem = engineAPI.GetRequiredService<IFileSystem>();
		clientDll = engineAPI.GetRequiredService<IBaseClientDLL>();
		CL = engineAPI.GetRequiredService<CL>();
		materials = engineAPI.GetRequiredService<IMaterialSystem>();
		clientGlobalVariables = engineAPI.GetRequiredService<ClientGlobalVariables>();
		MDLCache = engineAPI.GetRequiredService<IMDLCache>();
	}

	public void BeginLoadingPlaque() {
		if (!DrawLoading) {
			EngineVGui!.SetNotAllowedToShowGameUI(false);
			Host.AllowQueuedMaterialSystem(false);
			DrawLoading = true;

			Sound.StopAllSounds(true);
			Sound.OnLoadScreen(true);

			MDLCache.FinishPendingLoads();

			Con.ClearNotify();
			CenterStringOff();

			clientDll?.HudText(null);
			EngineVGui!.EnabledProgressBarForNextLoad();
			EngineVGui!.OnLevelLoadingStarted();

			clientGlobalVariables.FrameTime = 0;
			clientGlobalVariables.FrameCount = ++Host.FrameCount;
			// Ensures the screen is painted to reflect the loading state
			UpdateScreen();
			clientGlobalVariables.FrameCount = ++Host.FrameCount;
			UpdateScreen();

			clientGlobalVariables.FrameTime = cl.GetFrameTime();

			DisabledForLoading = true;
		}
	}

	public void EndLoadingPlaque() {
		if (DrawLoading) {
			EngineVGui?.OnLevelLoadingFinished();
			Sound.OnLoadScreen(false);
		}
		else if (Sys.ExtendedError) {
			EngineVGui?.ShowErrorMessage();
		}

		DisabledForLoading = false;
		DrawLoading = false;
	}

	public void UpdateScreen() {
		if (NextDrawTick != 0) {
			if (Host.TickCount < NextDrawTick)
				return;
			NextDrawTick = 0;
		}

		if (DisabledForLoading) {
			if (!Host.IsSinglePlayerGame()) {
				View.RenderGuiOnly();
			}

			return;
		}

		if (!Initialized || !Common.Initialized)
			return;

		materials.BeginFrame(Host.FrameTime);
		{
			EngineVGui?.Simulate();
		}

		ClientDLL.FrameStageNotify(ClientFrameStage.RenderStart);
		{
			Render.FrameBegin();
		}

		View.RenderView();
		CL.TakeSnapshotAndSwap();

		ClientDLL.FrameStageNotify(ClientFrameStage.RenderEnd);
		{
			Render.FrameEnd();
		}

		materials.EndFrame();
	}

	public void CenterPrint(ReadOnlySpan<char> str) { }
	public void CenterStringOff() { }

	internal void Shutdown() {

	}
}
