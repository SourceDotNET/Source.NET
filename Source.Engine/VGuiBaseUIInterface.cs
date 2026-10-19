using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.GameUI;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Networking;
using Source.GUI.Controls;

using System.Numerics;

namespace Source.Engine;

public enum LevelLoadingProgress
{
	None,
	ChangeLevel,
	SpawnServer,
	LoadWorldModel,
	CrcMap,
	CrcClientDll,
	CreateNetworkStringTables,
	PrecacheWorld,
	ClearWorld,
	LevelInit,
	Precache,
	ActivateServer,
	BeginConnect,
	SignOnChallenge,
	SignOnConnect,
	SignOnConnected,
	ProcessServerInfo,
	ProcessStringTable,
	SignOnNew,
	SendClientInfo,
	SendSignonData,
	SignOnSpawn,
	FullyConnected,
	ReadyToPlay
}

public struct LoadingProgressDescription
{
	public LevelLoadingProgress Progress;
	public int Percent;
	public int Repeat;
	public string Description;

	public LoadingProgressDescription(LevelLoadingProgress progress, int percent, int repeat, ReadOnlySpan<char> desc) {
		Progress = progress;
		Percent = percent;
		Repeat = repeat;
		Description = new(desc);
	}
}

public interface IEngineVGuiInternal : IEngineVGui
{
	// Level loading
	void OnLevelLoadingStarted();
	void OnLevelLoadingFinished();
	void NotifyOfServerConnect(ReadOnlySpan<char> game, int IP, int connectionPort, int queryPort);
	void NotifyOfServerDisconnect();
	void EnabledProgressBarForNextLoad();
	void UpdateProgressBar(LevelLoadingProgress progress);
#if !GMOD_DLL
	void UpdateCustomProgressBar(float progress, ReadOnlySpan<char> desc);
#endif
	void StartCustomProgress();
	void FinishCustomProgress();
	void ShowErrorMessage();
	void SetNotAllowedToHideGameUI(bool notAllowed);
	void SetNotAllowedToShowGameUI(bool notAllowed);
	void Simulate();
	void Paint(PaintMode paintMode);
	void ActivateGameUI();
	bool HideGameUI();
#if !GMOD_DLL
	void ShowConsole();
#endif
	void HideConsole();
	bool ShouldPause();
}

#if !SWDS
public class EnginePanel : EditablePanel
{
	public EnginePanel(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetMouseInputEnabled(true);
		SetKeyboardInputEnabled(true);
	}

	public void EnableMouseFocus(bool state) {
		SetMouseInputEnabled(state);
		SetKeyboardInputEnabled(state);
	}
}


public class StaticPanel : Panel
{
	public StaticPanel(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetCursor(CursorCode.None);
		SetKeyboardInputEnabled(false);
		SetMouseInputEnabled(false);
	}
}


public class FocusOverlayPanel : Panel
{
	public static readonly ConVar mat_drawTitleSafe = new("mat_drawTitleSafe", "0", FCvar.None, "Enable title safe overlay");
	public static readonly ConVar vgui_drawfocus = new("vgui_drawfocus", "0", FCvar.None, "Report which panel is under the mouse.");
	static public List<Panel> FocusPanelList = [];

	public FocusOverlayPanel(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetPaintEnabled(false);
		SetPaintBorderEnabled(false);
		SetPaintBackgroundEnabled(false);

		MakePopup();

		SetPostChildPaintEnabled(true);
		SetKeyboardInputEnabled(false);
		SetMouseInputEnabled(false);
	}

	public bool DrawTitleSafeOverlay() {
		if (!mat_drawTitleSafe.GetBool())
			return false;

		materials.GetBackBufferDimensions(out int backBufferWidth, out int backBufferHeight);

		int insetX = (int)(0.05f * backBufferWidth);
		int insetY = (int)(0.05f * backBufferHeight);

		int x = insetX;
		int y = insetY;
		int x1 = backBufferWidth - insetX;
		int y1 = backBufferHeight - insetY;

		Surface.DrawSetColor(255, 0, 0, 255);
		Surface.DrawOutlinedRect(x, y, x1, y1);

		insetX = (int)(0.075f * backBufferWidth);
		insetY = (int)(0.075f * backBufferHeight);

		x = insetX;
		y = insetY;
		x1 = backBufferWidth - insetX;
		y1 = backBufferHeight - insetY;

		Surface.DrawSetColor(0, 255, 0, 255);
		Surface.DrawOutlinedRect(x, y, x1, y1);

		return true;
	}

	public override void PostChildPaint() {
		base.PostChildPaint();

		bool needsMoveToFront = false;

		if (DrawTreeFrame.DrawTreeSelectedPanel != null) {
			DrawTreeFrame.DrawTreeSelectedPanel.GetClipRect(out int x, out int y, out int x1, out int y1);
			Surface.DrawSetColor(255, 0, 0, 255);
			Surface.DrawOutlinedRect(x, y, x1, y1);

			needsMoveToFront = true;
		}

		if (DrawTitleSafeOverlay()) needsMoveToFront = true;
		if (DrawFocusPanelList()) needsMoveToFront = true;
		if (needsMoveToFront) MoveToFront();
	}

	public bool DrawFocusPanelList() {
		if (!vgui_drawfocus.GetBool())
			return false;

		int c = FocusPanelList.Count;
		if (c <= 0)
			return false;

		int slot = 0;
		int fullscreeninset = 0;

		for (int i = 0; i < c; i++) {
			if (slot > 31)
				break;

			Panel panel = FocusPanelList[i];
			if (!panel.IsVisible())
				continue;

			GetColorForSlot(slot, out int r, out int g, out int b);
			panel.GetClipRect(out int x, out int y, out int x1, out int y1);

			// if ((x1 - x) == videomode->GetModeUIWidth() && (y1 - y) == videomode->GetModeUIHeight()) {
			// 	x += fullscreeninset;
			// 	y += fullscreeninset;
			// 	x1 -= fullscreeninset;
			// 	y1 -= fullscreeninset;
			// 	fullscreeninset++;
			// }

			Surface.DrawSetColor(r, g, b, 255);
			Surface.DrawOutlinedRect(x, y, x1, y1);
			slot++;
		}

		return true;
	}

	public static void GetColorForSlot(int slot, out int r, out int g, out int b) {
		r = (int)(124 + slot * 47.3) & 255;
		g = (int)(63.78 - slot * 71.4) & 255;
		b = (int)(108.42 + slot * 13.57) & 255;
	}
}


#if DEBUG
public class VisualizeLayoutPanel : Panel
{
	public VisualizeLayoutPanel(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetPaintEnabled(false);
		SetPaintBorderEnabled(false);
		SetPaintBackgroundEnabled(false);

		MakePopup();

		SetPostChildPaintEnabled(true);
		SetKeyboardInputEnabled(false);
		SetMouseInputEnabled(false);
	}

	public override void PostChildPaint() {
		if (!sdn_vgui_visualizelayout.GetBool())
			return;

		if (LayoutVisualizations.Count == 0) return;

		double time = system.GetCurrentTime();
		List<Panel> toRemove = [];

		foreach (var (panel, expireTime) in LayoutVisualizations) {
			if (expireTime < time) {
				toRemove.Add(panel);
				continue;
			}

			if (!panel.IsVisible())
				continue;

			panel.GetAbsPos(out int x, out int y);
			panel.GetSize(out int w, out int h);

			float alpha = (float)Math.Clamp((expireTime - time) / 0.3, 0, 1);
			Surface.DrawSetColor(255, 0, 0, (int)(alpha * 100));
			Surface.DrawFilledRect(x, y, x + w, y + h);
			Surface.DrawSetColor(0, 255, 0, (int)(alpha * 255));
			Surface.DrawOutlinedRect(x, y, x + w, y + h);
		}

		foreach (var p in toRemove)
			LayoutVisualizations.Remove(p);
	}
}
#endif

public class EngineVGui(
	Sys Sys, Net Net, IEngineAPI engineAPI, ISurface surface,
	IMaterialSystem materials, ILauncherManager launcherMgr,
	ICommandLine CommandLine, IFileSystem fileSystem, Cbuf Cbuf, Sound Sound, Host Host
	) : IEngineVGuiInternal
{
	public static LoadingProgressDescription[] ListenServerLoadingProgressDescriptions = [

		new(LevelLoadingProgress.None,                          0,      0,      null),
		new(LevelLoadingProgress.SpawnServer,                   2,      0,      "#LoadingProgress_SpawningServer"),
		new(LevelLoadingProgress.LoadWorldModel,                4,      7,      "#LoadingProgress_LoadMap"),
		new(LevelLoadingProgress.CreateNetworkStringTables,     23,     0,      null),
		new(LevelLoadingProgress.PrecacheWorld,                 23,     0,      "#LoadingProgress_PrecacheWorld"),
		new(LevelLoadingProgress.ClearWorld,                    23,     0,      null),
		new(LevelLoadingProgress.LevelInit,                     34,     0,      "#LoadingProgress_LoadResources"),
		new(LevelLoadingProgress.Precache,                      35,     239,    null),
		new(LevelLoadingProgress.ActivateServer,                68,     0,      null),
		new(LevelLoadingProgress.SignOnChallenge,               68,     0,      null),
		new(LevelLoadingProgress.SignOnConnect,                 70,     0,      null),
		new(LevelLoadingProgress.SignOnConnected,               73,     0,      "#LoadingProgress_SignonLocal"),
		new(LevelLoadingProgress.ProcessServerInfo,             75,     0,      null),
		new(LevelLoadingProgress.ProcessStringTable,            77,     12,     null),	// 16
		new(LevelLoadingProgress.SignOnNew,                     84,     0,      null),
		new(LevelLoadingProgress.SendClientInfo,                88,     0,      null),
		new(LevelLoadingProgress.SendSignonData,                91,     0,      "#LoadingProgress_SignonDataLocal"),
		new(LevelLoadingProgress.SignOnSpawn,                   94,     0,      null),
		new(LevelLoadingProgress.FullyConnected,                97,     0,      null),
		new(LevelLoadingProgress.ReadyToPlay,                   99,     0,      null),
	];
	public static LoadingProgressDescription[] RemoteConnectLoadingProgressDescriptions = [
		new(LevelLoadingProgress.None,                          0,      0,      null),
		new(LevelLoadingProgress.ChangeLevel,                   1,      0,      "#LoadingProgress_Changelevel"),
		new(LevelLoadingProgress.BeginConnect,                  5,      0,      "#LoadingProgress_BeginConnect"),
		new(LevelLoadingProgress.SignOnChallenge,               10,     0,      "#LoadingProgress_Connecting"),
		new(LevelLoadingProgress.SignOnConnected,               15,     0,      null),
		new(LevelLoadingProgress.ProcessServerInfo,             20,     0,      "#LoadingProgress_ProcessServerInfo"),
		new(LevelLoadingProgress.CreateNetworkStringTables,     25,     11,     null),
		new(LevelLoadingProgress.LoadWorldModel,                45,     7,      "#LoadingProgress_LoadMap"),
		new(LevelLoadingProgress.SignOnNew,                     75,     0,      null),
		new(LevelLoadingProgress.SendClientInfo,                80,     0,      "#LoadingProgress_SendClientInfo"),
		new(LevelLoadingProgress.SendSignonData,                85,     0,      "#LoadingProgress_SignonData"),
		new(LevelLoadingProgress.SignOnSpawn,                   90,     0,      null),
		new(LevelLoadingProgress.FullyConnected,                95,     0,      "Fully connected!"),
		new(LevelLoadingProgress.ReadyToPlay,                   99,     0,      "Ready to play!"),
	];

	LoadingProgressDescription[]? activeDescriptions = null;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
	Common Common;
	IGameUI staticGameUIFuncs;
	IGameConsole staticGameConsole;
	IGame game;
	ISurface matSystemSurface;
	IEngineClient engineClient;
	ILocalize localize;
	IVGui vgui;
	ISystem system;
	ISchemeManager vguiScheme;
	StaticPanel staticPanel;
	EnginePanel staticClientDLLPanel;
	EnginePanel staticClientDLLToolsPanel;
	EnginePanel staticGameUIPanel;
	EnginePanel staticGameDLLPanel;
	DebugSystemPanel staticDebugSystemPanel;
	FocusOverlayPanel staticFocusOverlayPanel;
#if DEBUG
	VisualizeLayoutPanel staticVisualizeLayoutPanel;
#endif

	// VProfPanel VProfPanel;
	BudgetPanelEngine BudgetPanel;
	TextureBudgetPanel TextureBudgetPanel;

	CL CL;
	Con Con;

	EnginePanel staticEngineToolsPanel;
#pragma warning restore CS8618

	bool ShowProgressDialog;
	LevelLoadingProgress LastProgressPoint;
	int LastProgressPointRepeatCount;
	double LoadingStartTime;
	struct LoadingProgressEntry
	{
		public double Time;
		public LevelLoadingProgress Progress;
	}
	List<LoadingProgressEntry> LoadingProgress = [];
	bool SaveProgress;
	bool NoShaderAPI;
	double ProgressBias;

	LoadingProgressDescription def = new(LevelLoadingProgress.None, 0, 0, null);
	public ref LoadingProgressDescription GetProgressDescription(LevelLoadingProgress progress) {
		if (activeDescriptions == null)
			return ref def;

		int i = 0;
		while (i < activeDescriptions.Length) {
			ref LoadingProgressDescription desc = ref activeDescriptions[i];
			if (desc.Progress >= progress)
				return ref desc;

			i++;
		}

		return ref activeDescriptions[0];
	}

	public void OnLevelLoadingStarted() {
		staticGameUIFuncs.OnLevelLoadingStarted(ShowProgressDialog);

		LoadingStartTime = Sys.Time;
		LoadingProgress.Clear();
		LastProgressPoint = LevelLoadingProgress.None;
		LastProgressPointRepeatCount = 0;
		ProgressBias = 0;

		if (Net.IsMultiplayer())
			activeDescriptions = RemoteConnectLoadingProgressDescriptions;
		else
			activeDescriptions = ListenServerLoadingProgressDescriptions;

		ShowProgressDialog = false;
	}
	public void OnLevelLoadingFinished() {
		staticGameUIFuncs.OnLevelLoadingFinished(Sys.ExtendedError, Sys.DisconnectReason, Sys.ExtendedDisconnectReason);
		LastProgressPoint = LevelLoadingProgress.None;

		Sys.ExtendedError = false;
		Sys.DisconnectReason = null;
		Sys.ExtendedDisconnectReason = null;

		HideGameUI();
	}

	bool NotAllowedToHideGameUI;
	bool NotAllowedToShowGameUI;

	public bool HideGameUI() {
		if (NotAllowedToHideGameUI)
			return false;

		ReadOnlySpan<char> levelName = engineClient.GetLevelName();
		bool inNonBgLevel = !levelName.IsEmpty && levelName.Length > 0 && !engineClient.IsLevelMainMenuBackground();
		if (inNonBgLevel) {
			staticGameUIPanel.SetVisible(false);
			staticGameUIPanel.SetPaintBackgroundEnabled(false);

			staticClientDLLPanel.SetVisible(true);
			staticClientDLLPanel.MoveToFront();
			staticClientDLLPanel.SetMouseInputEnabled(true);

			SetEngineVisible(true);

			staticGameUIFuncs.OnGameUIHidden();
		}
		else {
			if (!levelName.IsEmpty && levelName.Length > 0 && engineClient.GetMaxClients() <= 1 && engineClient.IsPaused())
				Cbuf.AddText("unpause\n");
		}
		return true;
	}

	public void NotifyOfServerConnect(ReadOnlySpan<char> game, int IP, int connectionPort, int queryPort) {
		if (staticGameUIFuncs == null)
			return;

		staticGameUIFuncs.OnConnectToServer(game, IP, connectionPort, queryPort);
	}

	public void NotifyOfServerDisconnect() {
		if (staticGameUIFuncs == null)
			return;

		staticGameUIFuncs.OnDisconnectFromServer(SteamLoginFailure.None);//g_eSteamLoginFailure
	}

	public void UpdateProgressBar(LevelLoadingProgress progress) {
		if (!Sys.InMainThread())
			return;

		if (activeDescriptions == null)
			return;

		if (progress < LastProgressPoint)
			return;

		if (progress == LastProgressPoint)
			++LastProgressPointRepeatCount;
		else
			LastProgressPointRepeatCount = 0;

		ref LoadingProgressDescription desc = ref GetProgressDescription(progress);
		double perc = desc.Percent / 100d;
		if (desc.Repeat > 1 && LastProgressPointRepeatCount > 0) {
			LastProgressPointRepeatCount = Math.Min(LastProgressPointRepeatCount, desc.Repeat);
			double nextPerc = GetProgressDescription(progress + 1).Percent / 100d;
			perc += (nextPerc - perc) * ((float)LastProgressPointRepeatCount / desc.Repeat);
		}

		perc = perc * (1.0f - ProgressBias) + ProgressBias;
		if (staticGameUIFuncs.UpdateProgressBar((float)perc, desc.Description))
			Host.View.RenderGuiOnly();

		LastProgressPoint = progress;
	}
	public void UpdateCustomProgressBar(float progress, ReadOnlySpan<char> desc) {
		if (staticGameUIFuncs == null)
			return;

		if (!desc.IsEmpty && desc[0] == '#') {
			ReadOnlySpan<char> localized = localize.Find(desc);
			if (!localized.IsEmpty)
				desc = localized;
		}

		if (staticGameUIFuncs.UpdateProgressBar(progress, desc))
			Host.View.RenderGuiOnly();
	}
	public void StartCustomProgress() {
		if (staticGameUIFuncs == null)
			return;

		staticGameUIFuncs.OnLevelLoadingStarted(true);
		SaveProgress = staticGameUIFuncs.SetShowProgressText(true);
	}
	public void FinishCustomProgress() {
		if (staticGameUIFuncs == null)
			return;

		staticGameUIFuncs.SetShowProgressText(SaveProgress);
		staticGameUIFuncs.OnLevelLoadingFinished(false, "", "");
	}
	public void ShowErrorMessage() {
		if (staticGameUIFuncs == null || !Sys.ExtendedError)
			return;

		staticGameUIFuncs.OnLevelLoadingFinished(Sys.ExtendedError, Sys.DisconnectReason, Sys.ExtendedDisconnectReason);
		LastProgressPoint = LevelLoadingProgress.None;

		Sys.ExtendedError = false;
		Sys.DisconnectReason = null;
		Sys.ExtendedDisconnectReason = null;

		HideGameUI();
	}

	public bool IsGameUIVisible() {
		return staticGameUIPanel != null && staticGameUIPanel.IsVisible();
	}

	public void VGui_PlaySound(ReadOnlySpan<char> fileName) {
		Vector3 dummyOrigin = new(0);

		SfxTable? sound = Sound.PrecacheSound(fileName);
		if (sound != null) {
			Sound.MarkUISound(sound);

			StartSoundParams parms = new();
			parms.StaticSound = false;
			parms.SoundSource = cl.ViewEntity;
			parms.EntChannel = SoundEntityChannel.Auto;
			parms.Sfx = sound;
			parms.Origin = dummyOrigin;
			parms.Pitch = 100;
			parms.SoundLevel = SoundLevel.LvlIdle;
			parms.Flags = 0;
			parms.Volume = 1.0f;

			Sound.StartSound(in parms);
		}
	}

	IBaseClientDLL clientDLL;
	public void Init() {
		// Load gameui
		Common = engineAPI.GetRequiredService<Common>();
		matSystemSurface = engineAPI.GetRequiredService<ISurface>();
		game = engineAPI.GetRequiredService<IGame>();
		staticGameUIFuncs = engineAPI.GetRequiredService<IGameUI>();
		engineClient = engineAPI.GetRequiredService<IEngineClient>();
		vgui = engineAPI.GetRequiredService<IVGui>();
		system = engineAPI.GetRequiredService<ISystem>();
		clientDLL = engineAPI.GetRequiredService<IBaseClientDLL>();
		vguiScheme = engineAPI.GetRequiredService<ISchemeManager>();
		localize = engineAPI.GetRequiredService<ILocalize>();
		CL = engineAPI.GetRequiredService<CL>();
		Con = engineAPI.GetRequiredService<Con>();
		vguiScheme.Init();
		// IGameConsole, but later.

		Span<char> lang = stackalloc char[64];
		system.GetUILanguage(lang);

		matSystemSurface.InstallPlaySoundFunc(VGui_PlaySound);

		ReadOnlySpan<char> str = "resource/SourceScheme.res";
		if (vguiScheme.LoadSchemeFromFile(str, "Tracker") == null) {
			Sys.Error($"Error loading file {str}\n");
			return;
		}

		// Try loading ClientScheme
		vguiScheme.LoadSchemeFromFile("resource/ClientScheme.res", "ClientScheme");

		// Ideal hierarchy:

		// Root -- staticPanel
		//		staticBackgroundImagePanel (from gamui) zpos == 0
		//    staticClientDLLPanel ( zpos == 25 )
		//		staticClientDLLToolsPanel ( zpos == 28
		//		staticGameDLLPanel ( zpos == 30 )
		//		staticEngineToolsPanel ( zpos == 75 )
		//		staticGameUIPanel ( GameUI stuff ) ( zpos == 100 )
		//		staticDebugSystemPanel ( Engine debug stuff ) zpos == 125 )

		int w = 0, h = 0;
		launcherMgr.RenderedSize(false, ref w, ref h);

		staticPanel = new StaticPanel(null, "staticPanel");
		staticPanel.SetBounds(0, 0, w, h);
		staticPanel.SetPaintBorderEnabled(false);
		staticPanel.SetPaintBackgroundEnabled(false);
		staticPanel.SetPaintEnabled(false);
		staticPanel.SetVisible(true);
		staticPanel.SetCursor(CursorCode.None);
		staticPanel.SetZPos(0);
		staticPanel.SetVisible(true);
		staticPanel.SetParent(matSystemSurface.GetEmbeddedPanel());

		staticClientDLLPanel = new EnginePanel(staticPanel, "staticClientDLLPanel");
		staticClientDLLPanel.SetBounds(0, 0, w, h);
		staticClientDLLPanel.SetPaintBorderEnabled(false);
		staticClientDLLPanel.SetPaintBackgroundEnabled(false);
		staticClientDLLPanel.SetKeyboardInputEnabled(false);
		staticClientDLLPanel.SetPaintEnabled(false);
		staticClientDLLPanel.SetVisible(false);
		staticClientDLLPanel.SetCursor(CursorCode.None);
		staticClientDLLPanel.SetZPos(25);

		CreateAskConnectPanel(staticPanel);

		staticClientDLLToolsPanel = new EnginePanel(staticPanel, "staticClientDLLToolsPanel");
		staticClientDLLToolsPanel.SetBounds(0, 0, w, h);
		staticClientDLLToolsPanel.SetPaintBorderEnabled(false);
		staticClientDLLToolsPanel.SetPaintBackgroundEnabled(false);
		staticClientDLLToolsPanel.SetKeyboardInputEnabled(false);
		staticClientDLLToolsPanel.SetPaintEnabled(false);
		staticClientDLLToolsPanel.SetVisible(true);
		staticClientDLLToolsPanel.SetCursor(CursorCode.None);
		staticClientDLLToolsPanel.SetZPos(28);

		staticEngineToolsPanel = new EnginePanel(staticPanel, "Engine Tools");
		staticEngineToolsPanel.SetBounds(0, 0, w, h);
		staticEngineToolsPanel.SetPaintBorderEnabled(false);
		staticEngineToolsPanel.SetPaintBackgroundEnabled(false);
		staticEngineToolsPanel.SetPaintEnabled(false);
		staticEngineToolsPanel.SetVisible(true);
		staticEngineToolsPanel.SetCursor(CursorCode.None);
		staticEngineToolsPanel.SetZPos(75);

		staticGameUIPanel = new EnginePanel(staticPanel, "GameUI Panel");
		staticGameUIPanel.SetBounds(0, 0, w, h);
		staticGameUIPanel.SetPaintBorderEnabled(false);
		staticGameUIPanel.SetPaintBackgroundEnabled(false);
		staticGameUIPanel.SetPaintEnabled(false);
		staticGameUIPanel.SetVisible(true);
		staticGameUIPanel.SetCursor(CursorCode.None);
		staticGameUIPanel.SetZPos(100);

		staticGameDLLPanel = new EnginePanel(staticPanel, "staticGameDLLPanel");
		staticGameDLLPanel.SetBounds(0, 0, w, h);
		staticGameDLLPanel.SetPaintBorderEnabled(false);
		staticGameDLLPanel.SetPaintBackgroundEnabled(false);
		staticGameDLLPanel.SetKeyboardInputEnabled(false);
		staticGameDLLPanel.SetPaintEnabled(false);
		staticGameDLLPanel.SetCursor(CursorCode.None);
		staticGameDLLPanel.SetZPos(135);

		if (IsPC()) {
			Common.TimestampedLog("Building Panels (staticDebugSystemPanel)");

			staticDebugSystemPanel = new DebugSystemPanel(staticPanel, "Engine Debug System");
			staticDebugSystemPanel.SetZPos(125);

			// DemoUIPanel.InstallDemoUI(staticEngineToolsPanel);
			// DemoUIPanel2.Install(staticClientDLLPanel, staticEngineToolsPanel, true);

			// FogUIPanel.InstallFogUI(staticEngineToolsPanel);

			TxViewPanel.Install(staticEngineToolsPanel);

			Common.TimestampedLog("Install perf tools");

			EnginePerfTools.InstallPerformanceToolsUI(staticEngineToolsPanel);

			// colorcorrectiontools.InstallColorCorrectionUI(staticEngineToolsPanel);
			// colorcorrectiontools.Init();
		}

		if (CommandLine.CheckParm("-tools"))
			staticGameDLLPanel.SetVisible(true);
		else
			staticGameDLLPanel.SetVisible(false);

		staticFocusOverlayPanel = new(staticPanel, "FocusOverlayPanel");
		staticFocusOverlayPanel.SetBounds(0, 0, w, h);
		staticFocusOverlayPanel.SetZPos(150);
		staticFocusOverlayPanel.MoveToFront();

#if DEBUG
		staticVisualizeLayoutPanel = new(staticPanel, "VisualizeLayoutPanel");
		staticVisualizeLayoutPanel.SetBounds(0, 0, w, h);
		staticVisualizeLayoutPanel.SetZPos(160);
		staticVisualizeLayoutPanel.MoveToFront();
#endif

		// TODO: the other panels...
		// Specifically,
		// - DemoUIPanel (if we even do demos)
		// - FogUIPanel

		if (IsPC()) {
			Con.CreateConsolePanel(staticEngineToolsPanel);
			VGuiDrawTree.CreateDrawTreePanel(staticEngineToolsPanel);
			TextureListPanel.CreateTextureListPanel(staticEngineToolsPanel);
			CL.CreateEntityReportPanel(staticEngineToolsPanel);
			CreateVProfPanels(staticEngineToolsPanel);
		}

		staticEngineToolsPanel.LoadControlSettings("scripts/EngineVGuiLayout.res");

		// materials.CacheUsedMaterials(); todo

		localize.AddFile($"resource/valve_%language%.txt");
		// localize.AddFile($"resource/{engineAPI.GetRequiredService<EngineParms>().Mod}_%language%.txt");
		localize.AddFile($"resource/hl2_%language%.txt");
		localize.AddFile($"resource/garrysmod_%language%.txt");


		staticGameUIFuncs.Initialize(engineAPI);
		staticGameUIFuncs.Start();

		if (IsPC()) {
			staticGameConsole = engineAPI.GetRequiredService<IGameConsole>();
		}

		if (staticGameConsole != null) {
			staticGameConsole.Initialize();
			staticGameConsole.SetParent(staticGameUIPanel);
		}

		ActivateGameUI();

		NoShaderAPI = CommandLine.CheckParm("-noshaderapi");
	}

	void DumpPanels_r(IPanel panel, int level) {
		int i;
		ReadOnlySpan<char> name = panel.GetName();

		Span<char> indentBuff = stackalloc char[64];
		for (i = 0; i < level; i++)
			indentBuff[i] = '.';

		indentBuff = indentBuff[..i];

		string panelName = $"    {indentBuff}{name}         ".PadRight(64);

		ConMsg(panelName);
		panel.GetClipRect(out int x0, out int y0, out int x1, out int y1);
		ConMsg($" cliprect [{x0}, {y0}, {x1}, {y1}]");
		ConMsg($" {(panel.IsPopup() ? "popup" : "")}");
		ConMsg($" {(panel.IsKeyboardInputEnabled() ? "kb" : "")}");
		ConMsg($" {(panel.IsMouseInputEnabled() ? "mouse" : "")}");
		ConMsg($" {(panel.IsEnabled() ? "enabled" : "")}");
		ConMsg($" {(panel.IsVisible() ? "visible" : "")}");
		ConMsg($" {(panel.IsFullyVisible() ? "fullvis" : "")}");
		ConMsg($" bg {panel.GetBgColor()}");
		ConMsg($" fg {panel.GetFgColor()}");

		ConMsg("\n");

		int children = panel.GetChildCount();
		for (i = 0; i < children; i++) {
			IPanel child = panel.GetChild(i);
			DumpPanels_r(child, level + 1);
		}
	}
	public void SetNotAllowedToHideGameUI(bool state) => NotAllowedToHideGameUI = state;
	public void SetNotAllowedToShowGameUI(bool state) => NotAllowedToShowGameUI = state;
	void SetGameDLLPanelsVisible(bool show) {
		staticGameDLLPanel?.SetVisible(show);
	}
	[ConCommand()] void gameui_hide() => HideGameUI();
	[ConCommand()] void gameui_activate() => ActivateGameUI();
	[ConCommand()] void gameui_preventescape() => SetNotAllowedToHideGameUI(true);
	[ConCommand()] void gameui_allowescapetoshow() => SetNotAllowedToShowGameUI(false);
	[ConCommand()] void gameui_preventescapetoshow() => SetNotAllowedToShowGameUI(true);
	[ConCommand()] void gameui_allowescape() => SetNotAllowedToHideGameUI(false);
	[ConCommand(helpText: "Dump panel tree.")]
	void dump_panels() {
		Msg($"State:\n");
		Msg($"    Restricted panel: {surface.GetRestrictedPanel()}\n");
		Msg($"    Input app modal: {Singleton<IVGuiInput>().GetAppModalSurface()}\n");
		Msg($"Panels:\n");
		DumpPanels_r(surface.GetEmbeddedPanel(), 0);
	}

	public void ActivateGameUI() {
		if (staticGameUIFuncs == null)
			return;

		if (NotAllowedToShowGameUI)
			return;

		staticGameUIPanel.SetVisible(true);
		staticGameUIPanel.MoveToFront();

		staticClientDLLPanel.SetVisible(false);
		staticGameUIPanel.SetMouseInputEnabled(true);

		matSystemSurface.SetCursor(CursorCode.Arrow);
		SetEngineVisible(false);
		staticGameUIFuncs.OnGameUIActivated();
	}

	private void SetEngineVisible(bool state) => staticClientDLLPanel?.SetVisible(state);

	private Panel CreateAskConnectPanel(Panel staticPanel) => new AskConnectPanel(staticPanel);

	public void Simulate() {
		vgui.GetAnimationController().UpdateAnimations(Sys.Time);
		int w = 0, h = 0;
		launcherMgr.RenderedSize(false, ref w, ref h);
		using (MatRenderContextPtr renderContext = new(materials))
			renderContext.Viewport(0, 0, w, h);

		staticGameUIFuncs.RunFrame();
		vgui.RunFrame();

		DrawMouseFocus();
		VGuiDrawTree.UpdateDrawTreePanel();
		TextureListPanel.UpdateTextureListPanel();

		surface.CalculateMouseVisible();
		VGui_ActivateMouse();
	}

	private void VGui_ActivateMouse() {
		if (clientDLL == null)
			return;

		if (!game.IsActiveApp()) {
			clientDLL.IN_DeactivateMouse();
			return;
		}

		if (surface.IsCursorLocked())
			clientDLL.IN_ActivateMouse();
		else
			clientDLL.IN_DeactivateMouse();
	}

	readonly ConVar r_drawvgui = new("r_drawvgui", "1", FCvar.Cheat, "Enable the rendering of vgui panels");

	public void Paint(PaintMode mode) {
		if (staticPanel == null)
			return;

		IPanel embedded = matSystemSurface.GetEmbeddedPanel();
		if (embedded == null)
			return;

		bool drawGui = r_drawvgui.GetBool();
		if (!drawGui || NoShaderAPI)
			return;

		Panel panel = staticPanel;
		using (MatRenderContextPtr renderContext = new(materials)) {
			renderContext.GetViewport(out int x, out int y, out int w, out int h);
			panel.SetBounds(0, 0, w, h);
		}
		panel.Repaint();

		if ((mode & PaintMode.UIPanels) == PaintMode.UIPanels) {
			bool saveVisible = staticClientDLLPanel.IsVisible();
			bool saveToolsVisible = staticClientDLLToolsPanel.IsVisible();
			staticClientDLLPanel.SetVisible(false);
			staticClientDLLToolsPanel.SetVisible(false);

			matSystemSurface.PaintTraverseEx(embedded, true);

			staticClientDLLPanel.SetVisible(saveVisible);
			staticClientDLLToolsPanel.SetVisible(saveToolsVisible);
		}

		if ((mode & PaintMode.InGamePanels) == PaintMode.InGamePanels) {
			bool saveVisible = panel.IsVisible();
			panel.SetVisible(false);

			IPanel? saveParent = staticClientDLLPanel.GetParent();
			staticClientDLLPanel.SetParent(null);
			matSystemSurface.PaintTraverseEx(staticClientDLLPanel, true);
			staticClientDLLPanel.SetParent(saveParent);

			IPanel? saveToolParent = staticClientDLLToolsPanel.GetParent();
			staticClientDLLToolsPanel.SetParent(null);
			matSystemSurface.PaintTraverseEx(staticClientDLLToolsPanel, true);
			staticClientDLLToolsPanel.SetParent(saveParent);

			panel.SetVisible(saveVisible);

		}

		if ((mode & PaintMode.Cursor) == PaintMode.Cursor) {
			matSystemSurface.PaintSoftwareCursor();
		}
	}

	static void VGui_RecursePanel(List<Panel> panelList, int x, int y, Panel check, bool include_hidden) {
		if (!include_hidden && !check.IsVisible())
			return;

		if (check.IsWithinTraverse(x, y, true) != null)
			panelList.Add(check);

		int childCount = check.GetChildCount();
		for (int i = 0; i < childCount; i++) {
			Panel child = check.GetChild(i);
			VGui_RecursePanel(panelList, x, y, child, include_hidden);
		}
	}

	public void DrawMouseFocus() {
		var FocusPanelList = FocusOverlayPanel.FocusPanelList;

		FocusPanelList.Clear();

		if (!FocusOverlayPanel.vgui_drawfocus.GetBool())
			return;

		staticFocusOverlayPanel.MoveToFront();

		bool include_hidden = FocusOverlayPanel.vgui_drawfocus.GetInt() == 2;

		vgui.GetInput().GetCursorPos(out int x, out int y);

		Panel embedded = (Panel)surface.GetEmbeddedPanel();

		if (surface.IsCursorVisible() && surface.IsWithin(x, y)) {
			int cCount = surface.GetPopupCount();
			for (int i = cCount - 1; i >= 0; i--) {
				Panel? popup = (Panel?)surface.GetPopup(i);
				if (popup == null)
					continue;

				if (popup == embedded)
					continue;

				if (!popup.IsVisible())
					continue;

				VGui_RecursePanel(FocusPanelList, x, y, popup, include_hidden);
			}
			VGui_RecursePanel(FocusPanelList, x, y, embedded, include_hidden);
		}

		// ConPanel? conPanel = Con.GetConsolePanel();
		// if (conPanel == null)
		// 	return;

		// Con_NPrint_s np = new();
		// np.TimeToLive = 1.0f;

		// int c = FocusPanelList.Count;
		// int slot = 0;
		// for (int i = 0; i < c; i++) {
		// 	if (slot > 31)
		// 		break;

		// 	Panel panel = FocusPanelList[i];
		// 	if (panel == null)
		// 		continue;

		// 	np.Index = slot;

		// 	FocusOverlayPanel.GetColorForSlot(slot, out int r, out int g, out int b);

		// 	np.Color[0] = r / 255.0f;
		// 	np.Color[1] = g / 255.0f;
		// 	np.Color[2] = b / 255.0f;

		// 	conPanel.Con_NXPrintf(np, $"{slot + 1:3}:  {panel.GetName()}\n");

		// 	slot++;
		// }

		// while (slot <= 31) {
		// 	conPanel.Con_NPrintf(slot, "");
		// 	slot++;
		// }
	}

	IPanel IEngineVGui.GetPanel(VGuiPanelType type) => GetRootPanel(type);

	private IPanel GetRootPanel(VGuiPanelType type) {
		if (sv.IsDedicated())
			return null;

		return type switch {
			VGuiPanelType.ClientDll => staticClientDLLPanel,
			VGuiPanelType.GameUIDll => staticGameUIPanel,
			VGuiPanelType.Tools => staticEngineToolsPanel,
			VGuiPanelType.GameDll => staticGameDLLPanel,
			VGuiPanelType.ClientDllTools => staticClientDLLToolsPanel,
			_ => staticPanel,
		};
	}

	public void UpdateButtonState(in InputEvent ev) {
		vgui.GetInput().UpdateButtonState(in ev);
	}

	public void EnabledProgressBarForNextLoad() {
		ShowProgressDialog = true;
	}

	public bool Key_Event(in InputEvent ev) {
		bool down = ev.Type != InputEventType.IE_ButtonReleased;
		ButtonCode code = (ButtonCode)ev.Data;

		if (IsPC() && IsShiftKeyDown()) {
			switch (code) {
				case ButtonCode.KeyF1:
					if (down)
						Cbuf.AddText("debugsystemui");

					return true;

				case ButtonCode.KeyF2:
					if (down)
						Cbuf.AddText("demoui");

					return true;
			}
		}

#if WIN32
		if (IsPC() && code == ButtonCode.KeyBackquote && (IsAltKeyDown() || IsCtrlKeyDown()))
			return true;
#endif

		if (down && code == ButtonCode.KeyEscape && !clientDLL.HandleUiToggle()) {
			if (IsPC()) {
				if (IsGameUIVisible()) {
					ReadOnlySpan<char> levelName = engineClient.GetLevelName();
					if (!levelName.IsEmpty && levelName.Length > 0) {
						Cbuf.AddText("gameui_hide");
						if (IsDebugSystemVisible())
							Cbuf.AddText("debugsystemui 0");
					}
				}
				else {
					Cbuf.AddText("gameui_activate");
				}
				return true;
			}
		}

		if (surface.HandleInputEvent(in ev)) {
			if (IsPC() && (code == ButtonCode.KeyBackquote))
				return false;
			return true;
		}
		return false;
	}

	private bool IsShiftKeyDown() {
		IVGuiInput input = vgui.GetInput();
		if (input == null) return false;
		return input.IsKeyDown(ButtonCode.KeyLShift) | input.IsKeyDown(ButtonCode.KeyRShift);
	}

	private bool IsCtrlKeyDown() {
		IVGuiInput input = vgui.GetInput();
		if (input == null) return false;
		return input.IsKeyDown(ButtonCode.KeyLControl) | input.IsKeyDown(ButtonCode.KeyRControl);
	}

	private bool IsAltKeyDown() {
		IVGuiInput input = vgui.GetInput();
		if (input == null) return false;
		return input.IsKeyDown(ButtonCode.KeyLAlt) | input.IsKeyDown(ButtonCode.KeyRAlt);
	}

	private bool IsDebugSystemVisible() => staticDebugSystemPanel != null && staticDebugSystemPanel.IsVisible();

	private void HideDebugSystem() {
		if (staticDebugSystemPanel != null) {
			staticDebugSystemPanel.SetVisible(false);
			SetEngineVisible(true);
		}
	}


	[ConCommand("debugsystemui")]
	private void ToggleDebugSystemUI(in TokenizedCommand args) {
		if (staticDebugSystemPanel == null)
			return;

		bool Visisible;
		if (args.ArgC() == 1)
			Visisible = !IsDebugSystemVisible();
		else
			Visisible = int.Parse(args[1]) != 0;

		if (!Visisible) {
			staticDebugSystemPanel.SetVisible(false);
			SetEngineVisible(true);
		}
		else {
			// ClearIOStates();
			staticDebugSystemPanel.SetVisible(true);
			SetEngineVisible(false);
		}
	}

	public void ClearConsole() {
		staticGameConsole?.Clear();
	}

	public bool IsConsoleVisible() {
		return IsGameUIVisible() && staticGameConsole != null && staticGameConsole.IsConsoleVisible();
	}

	public void ShowConsole() {
		ActivateGameUI();
		staticGameConsole?.Activate();
	}

	public void HideConsole() {
		staticGameConsole?.Hide();
	}

	void CreateVProfPanels(Panel parent) {
		// VProfPanel = new VProfPanel(parent, "VProfPanel");
		BudgetPanel = new(parent, "BudgetPanel");
		// CreateVProfGraphPanel(parent);
		TextureBudgetPanel = new(parent, "TextureBudgetPanel");
	}

	void DestroyVProfPanels() {

	}

	internal void Shutdown() {
		DestroyVProfPanels();

		// Give panels a chance to settle so things
		// Marked for deletion will actually get deleted
		vgui.RunFrame();
		// unload the gameUI
		staticGameUIFuncs.Shutdown();
		// stop the App running
		vgui.Stop();
	}

	public bool ShouldPause() {
		return false;
	}
}
#endif
