using Microsoft.Extensions.DependencyInjection;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GameUI;
using Source.Common.GUI;
using Source.Engine;
using Source.GUI.Controls;

namespace Game.UI;

public class GameUI(IEngineClient engine) : IGameUI
{
	public BasePanel? BasePanel() => staticPanel;

	public bool IsMainMenuVisible() {
		BasePanel? basePanel = BasePanel();
		if (basePanel != null)
			return (basePanel.IsVisible() && basePanel.GetMenuAlpha() > 0);
		return false;
	}

	public void OnConfirmQuit() {
		BasePanel()!.OnOpenQuitConfirmationDialog();
	}

	string? GameIP;
	int GameConnectionPort;
	int GameQueryPort;

	public void OnConnectToServer(ReadOnlySpan<char> game, int ip, int connectionPort, int queryPort) {
		GameIP = new(game);
		GameConnectionPort = connectionPort;
		GameQueryPort = queryPort;
	}

	public void OnDisconnectFromServer(SteamLoginFailure steamLoginFailure) {
		GameIP = null;
		GameConnectionPort = 0;
		GameQueryPort = 0;

		if (LoadingDialog != null) {
			if (steamLoginFailure == SteamLoginFailure.NoSteamLogin)
				LoadingDialog.DisplayNoSteamConnectionError();
			else if (steamLoginFailure == SteamLoginFailure.VacBanned)
				LoadingDialog.DisplayVACBannedError();
			else if (steamLoginFailure == SteamLoginFailure.LoggedInElseWhere)
				LoadingDialog.DisplayLoggedInElsewhereError();
		}
	}

	bool ActivatedUI;
	public void OnGameUIActivated() {
		ActivatedUI = true;
		staticPanel.SetVisible(true);

		if (engine.GetMaxClients() <= 1)
			engine.ClientCmd_Unrestricted("setpause");

		staticPanel.OnGameUIActivated();
	}

	public void OnGameUIHidden() {
		if (engine.GetMaxClients() <= 1)
			engine.ClientCmd_Unrestricted("unpause");

		staticPanel.OnGameUIHidden();
	}

	public void OnLevelLoadingFinished(bool error, ReadOnlySpan<char> failureReason, ReadOnlySpan<char> extendedReason) {
		StopProgressBar(error, failureReason, extendedReason);

		HideGameUI();

		staticPanel.OnLevelLoadingFinished();
	}

	public void HideGameUI() {
		engine.ExecuteClientCmd("gameui_hide");
	}

	public void OnLevelLoadingStarted(bool showProgressDialog) {
		staticPanel.OnLevelLoadingStarted();
		if (showProgressDialog)
			StartProgressBar();
		PlayGameStartupSound = false;
	}

	bool PlayGameStartupSound;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
	BasePanel staticPanel;
	IEngineVGui enginevguifuncs;
	ISurface Surface;
	ILocalize localize;
	IEngineAPI EngineAPI;
	IFileSystem fileSystem;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

	public void Initialize(IEngineAPI engineAPI) {
		enginevguifuncs = engineAPI.GetRequiredService<IEngineVGui>();
		Surface = engineAPI.GetRequiredService<ISurface>();
		localize = engineAPI.GetRequiredService<ILocalize>();
		EngineAPI = engineAPI.GetRequiredService<IEngineAPI>();
		fileSystem = engineAPI.GetRequiredService<IFileSystem>();

		localize.AddFile("Resource/gameui_%language%.txt", "GAME", true);
		engineAPI.GetRequiredService<ModInfo>().LoadCurrentGameInfo();
		localize.AddFile("Resource/valve_%language%.txt", "GAME", true);
		// I have no idea why this one defines needed resource strings... TODO: Is this still needed?
		localize.AddFile("Resource/itemtest_%language%.txt", "GAME", true);

		staticPanel = new BasePanel();
		staticPanel.SetBounds(0, 0, 400, 300);
		staticPanel.SetPaintBorderEnabled(false);
		staticPanel.SetPaintBackgroundEnabled(true);
		staticPanel.SetPaintEnabled(false);
		staticPanel.SetVisible(true);
		staticPanel.SetMouseInputEnabled(false);
		staticPanel.SetKeyboardInputEnabled(false);

		IPanel rootpanel = enginevguifuncs.GetPanel(VGuiPanelType.GameUIDll);
		staticPanel.SetParent(rootpanel);
	}

	public void PostInit() {

	}

	public void RunFrame() {
		Surface.GetScreenSize(out int wide, out int tall);
		staticPanel.SetSize(wide, tall);

		staticPanel.RunFrame();
	}

	public void SetMainMenuOverride(IPanel? panel) {
		BasePanel? basePanel = BasePanel();
		if (basePanel != null)
			basePanel.SetMainMenuOverride(panel);
	}

	public bool SetShowProgressText(bool show) {
		if (LoadingDialog == null)
			return false;

		return LoadingDialog.SetShowProgressText(show);
	}

	private bool FindPlatformDirectory(out ReadOnlySpan<char> platformDir) {
		platformDir = string.Empty;

#if WIN32
		var location = System.Reflection.Assembly.GetExecutingAssembly().Location;
		if (string.IsNullOrEmpty(location))
			return false;

		var dir = Path.GetDirectoryName(location);
		if (dir == null)
			return false;

		platformDir = Path.Combine(dir, "platform") + Path.DirectorySeparatorChar;
		return true;
#else
		string dir = Directory.GetCurrentDirectory();
		if (string.IsNullOrEmpty(dir))
			return false;

		platformDir = Path.Combine(dir, "platform") + Path.DirectorySeparatorChar;
		return true;
#endif
	}

	public void Shutdown() {
		// TODO
	}

	public void Start() {
		if (!FindPlatformDirectory(out ReadOnlySpan<char> platformDir))
			return;

		if (IsPC()) {
			Span<char> configDir = stackalloc char[512];
			strcpy(configDir, platformDir);
			strcat(configDir, "config");

			Msg($"Steam config directory: {configDir.SliceNullTerminatedString()}\n");

			fileSystem.AddSearchPath(configDir.SliceNullTerminatedString(), "CONFIG");
			fileSystem.CreateDirHierarchy("", "CONFIG");

			// todo SetUserConfigFile

			fileSystem.AddSearchPath("platform", "PLATFORM");
		}

		localize.AddFile("Resource/platform_%language%.txt");
		localize.AddFile("Resource/vgui_%language%.txt");

		PlayGameStartupSound = true;
		// TryingToLoadFriends = true;
		// FriendsLoadPauseFrames = 1;
	}

	public bool UpdateProgressBar(float progress, ReadOnlySpan<char> statusText) {
		bool redraw = false;

		if (ContinueProgressBar(progress))
			redraw = true;

		if (SetProgressBarStatusText(statusText))
			redraw = true;

		return redraw;
	}

	private bool SetProgressBarStatusText(ReadOnlySpan<char> statusText) {
		if (LoadingDialog == null)
			return false;

		if (statusText.IsEmpty || statusText.Length <= 0)
			return false;

		if (statusText.Equals(PreviousStatusText, StringComparison.OrdinalIgnoreCase))
			return false;

		LoadingDialog.SetStatusText(statusText);
		PreviousStatusText = new(statusText);
		return true;
	}

	public static LoadingDialog? LoadingDialog;
	string? PreviousStatusText;

	public void StartProgressBar() {
		LoadingDialog ??= new LoadingDialog(staticPanel);
		PreviousStatusText = null;
		LoadingDialog.SetProgressPoint(0);
		LoadingDialog.Open();
	}

	private bool ContinueProgressBar(float progress) {
		if (LoadingDialog == null)
			return false;

		LoadingDialog.Activate();
		return LoadingDialog.SetProgressPoint(progress);
	}

	public void StopProgressBar(bool error, ReadOnlySpan<char> failureReason, ReadOnlySpan<char> extendedReason) {
		if (LoadingDialog == null && error)
			LoadingDialog = new LoadingDialog(staticPanel);

		if (LoadingDialog == null)
			return;

		if (error) {
			LoadingDialog.DisplayGenericError(failureReason, extendedReason);
			LoadingDialog = null; // DEVIATION: Set it to null anyway. Otherwise surface rendering breaks. A hack for a problem that should be further investigated.
		}
		else {
			LoadingDialog.Close();
			LoadingDialog = null;
		}
	}

	public bool IsInLevel() {
		ReadOnlySpan<char> levelName = engine.GetLevelName();
		return !levelName.IsEmpty && levelName.Length > 0 && !engine.IsLevelMainMenuBackground();
	}

	public bool IsInReplay() {
		throw new NotImplementedException();
	}

	public bool IsConsoleUI() {
		return false;
	}

	public bool HasSavedThisMenuSession() {
		throw new NotImplementedException();
	}

	public bool IsInBackgroundLevel() {
		ReadOnlySpan<char> levelName = engine.GetLevelName();
		if (!levelName.IsEmpty && levelName[0] != '\0' && engine.IsLevelMainMenuBackground())
			return true;
		return false;
	}

	public bool HasLoadingBackgroundDialog() {
		return LoadingBackgroundDialog != null;
	}

	Panel? LoadingBackgroundDialog;

	public void ShowLoadingBackgroundDialog() {
		if (LoadingBackgroundDialog != null) {
			LoadingBackgroundDialog.SetParent(staticPanel);
			LoadingBackgroundDialog.PerformApplySchemeSettings();
			LoadingBackgroundDialog.SetVisible(true);
			LoadingBackgroundDialog.MoveToFront();
			LoadingBackgroundDialog.SendMessage(new KeyValues("activate"), staticPanel);
		}
	}

	public void SetLoadingBackgroundDialog(IPanel? panel) {
		LoadingBackgroundDialog = (Panel?)panel;
	}
	public void HideLoadingBackgroundDialog() {
		if (LoadingBackgroundDialog != null) {
			LoadingBackgroundDialog.SetParent(null);
			LoadingBackgroundDialog.SetVisible(false);
			LoadingBackgroundDialog.MoveToBack();
			LoadingBackgroundDialog.SendMessage(new KeyValues("deactivate"), staticPanel);
		}
	}

	public void PreventEngineHideGameUI() => engine.ExecuteClientCmd("gameui_preventescape");
	public void AllowEngineHideGameUI() => engine.ExecuteClientCmd("gameui_allowescape");
}
