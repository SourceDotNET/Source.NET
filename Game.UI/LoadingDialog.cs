using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Formats.Keyvalues;
using Source.Common.GameUI;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Game.UI;

public class LoadingDialog : Frame
{
	readonly public ModInfo ModInfo = Singleton<ModInfo>();

	ProgressBar Progress;
	ProgressBar Progress2;
	Label InfoLabel;
	Label TimeRemainingLabel;
	Button CancelButton;
	Panel? LoadingBackground;

	bool ShowingSecondaryProgress;
	float SecondaryProgress;
	double LastSecondaryProgressUpdateTime;
	double SecondaryProgressStartTime;
	bool ShowingVACInfo;
	bool Center;
	bool ConsoleStyle;
	float ProgressFraction;

	[PanelAnimationVar("0")] protected int AdditionalIndentX;
	[PanelAnimationVar("0")] protected int AdditionalIndentY;

	public override void PerformLayout() {
		if (ConsoleStyle) {
			Surface.GetScreenSize(out int screenWide, out int screenTall);
			GetSize(out int wide, out int tall);
			float x, y;

			if (ModInfo.IsSinglePlayerOnly()) {
				x = (screenWide - wide) * 0.50f;
				y = (screenTall - tall) * 0.86f;
			}
			else {
				x = screenWide - (wide * 1.30f);
				y = screenTall * 0.875f;
			}

			SetPos((int)x, (int)y);
		}
		else if (Center)
			MoveToCenterOfScreen();
		else {
			Surface.GetWorkspaceBounds(out _, out _, out int screenWide, out int screenTall);
			GetSize(out int wide, out int tall);

			int x = screenWide - (wide + 10);
			int y = screenTall - (tall + 10);

			x -= AdditionalIndentX;
			y -= AdditionalIndentY;

			SetPos(x, y);
		}

		base.PerformLayout();
		MoveToFront();
	}
	public override void OnClose() {
		HideOtherDialogs(false);
		base.OnClose();
	}
	public override void OnCommand(ReadOnlySpan<char> command) {
		if (command.Equals("Cancel", StringComparison.OrdinalIgnoreCase)) {
			engine.ClientCmd_Unrestricted("disconnect\n");
			Close();
		}
		else
			base.OnCommand(command);
	}

	public LoadingDialog(Panel? parent) : base(parent, "LoadingDialog") {
		SetDeleteSelfOnClose(true);

		ConsoleStyle = gameUI.IsConsoleUI();

		if (!ConsoleStyle) {
			SetSize(416, 100);
			SetTitle("#GameUI_Loading", true);
		}

#if GMOD_DLL
		Center = false;
#else
		Center = !gameUI.HasLoadingBackgroundDialog();
#endif

		ShowingSecondaryProgress = false;
		SecondaryProgress = 0.0f;
		LastSecondaryProgressUpdateTime = 0.0f;
		SecondaryProgressStartTime = 0.0f;

		Progress = new ProgressBar(this, "Progress");
		Progress2 = new ProgressBar(this, "Progress2");
		InfoLabel = new Label(this, "InfoLabel", "");
		CancelButton = new Button(this, "CancelButton", "#GameUI_Cancel");
		TimeRemainingLabel = new Label(this, "TimeRemainingLabel", "");
		CancelButton.SetCommand("Cancel");

		if (ModInfo.IsSinglePlayerOnly() == false && ConsoleStyle == true)
			LoadingBackground = new Panel(this, "LoadingDialogBG");
		else
			LoadingBackground = null;

		SetMinimizeButtonVisible(false);
		SetMaximizeButtonVisible(false);
		SetCloseButtonVisible(false);
		SetSizeable(false);
		SetMoveable(false);

		if (ConsoleStyle) {
			Center = false;
			Progress.SetVisible(false);
			Progress2.SetVisible(false);
			InfoLabel.SetVisible(false);
			CancelButton.SetVisible(false);
			TimeRemainingLabel.SetVisible(false);

			SetMinimumSize(0, 0);
			SetTitleBarVisible(false);

			ProgressFraction = 0;
		}
		else {
			InfoLabel.SetBounds(20, 32, 392, 24);
			Progress.SetBounds(20, 64, 300, 24);
			CancelButton.SetBounds(330, 64, 72, 24);
			Progress2.SetVisible(false);
		}

		SetupControlSettings(false);
	}

	private void SetupControlSettings(bool forceShowProgressText) {
		ShowingVACInfo = false;

		if (gameUI.IsConsoleUI()) {
			// KeyValues controlSettings = BasePanel.GetConsoleControlSettings().FindKey("LoadingDialogNoBanner.res");
			// LoadControlSettings("null", null, controlSettings);
			// return;
		}

#if GMOD_DLL
		LoadControlSettings("resource/LoadingDialogGMod.res");
#else
	if (ModInfo.IsSinglePlayerOnly() && !forceShowProgressText)
		LoadControlSettings("resource/LoadingDialogNoBannerSingle.res");
	else
		LoadControlSettings("resource/LoadingDialogNoBanner.res");
#endif
	}

	internal void DisplayGenericError(ReadOnlySpan<char> failureReason, ReadOnlySpan<char> extendedReason) {
		if (ConsoleStyle)
			return;

		Activate();

		SetupControlSettingsForErrorDisplay("resource/LoadingDialogError.res");

		if (!extendedReason.IsEmpty && extendedReason.Length > 0) {
			ReadOnlySpan<char> fail = failureReason[0] == '#' ? Localize.Find(failureReason) : failureReason;
			ReadOnlySpan<char> ext = extendedReason[0] == '#' ? Localize.Find(extendedReason) : extendedReason;

			InfoLabel.SetText(string.Concat(fail, ext));
		}
		else
			InfoLabel.SetText(failureReason.Trim('\n'));

		InfoLabel.GetContentSize(out int wide, out int tall);
		InfoLabel.GetPos(out int x, out int y);
		SetTall(tall + y + 50);

		CancelButton.GetPos(out int buttonX, out int buttonY);
		CancelButton.SetPos(buttonX, tall + y + 6);
		CancelButton.RequestFocus();

		InfoLabel.InvalidateLayout();
		SetSizeable(true);
	}

	internal void DisplayNoSteamConnectionError() {
		if (ConsoleStyle)
			return;

		SetupControlSettingsForErrorDisplay("resource/LoadingDialogErrorNoSteamConnection.res");
	}

	internal void DisplayVACBannedError() {
		if (ConsoleStyle)
			return;

		SetupControlSettingsForErrorDisplay("resource/LoadingDialogErrorVACBanned.res");
		SetTitle("#VAC_ConnectionRefusedTitle", true);
	}

	internal void DisplayLoggedInElsewhereError() {
		if (ConsoleStyle)
			return;

		SetupControlSettingsForErrorDisplay("resource/LoadingDialogErrorLoggedInElsewhere.res");
		CancelButton.SetText("#GameUI_RefreshLogin_Login");
		CancelButton.SetCommand("Login");
	}

	private void SetupControlSettingsForErrorDisplay(ReadOnlySpan<char> settingsFile) {
		Center = true;
		SetTitle("#GameUI_Disconnected", true);
		LoadControlSettings(settingsFile);
		HideOtherDialogs(true);

		base.Activate();

		Progress.SetVisible(false);
		InfoLabel.SetVisible(true);
		CancelButton.SetText("#GameUI_Close");
		CancelButton.SetCommand("Close");

		InfoLabel.InvalidateLayout();
	}

	internal void Open() {
		if (!ConsoleStyle)
			SetTitle("#GameUI_Loading", true);

		HideOtherDialogs(true);
		base.Activate();

		if (!ConsoleStyle) {
			Progress.SetVisible(true);
			if (ModInfo.IsSinglePlayerOnly())
				InfoLabel.SetVisible(true);

			CancelButton.SetText("#GameUI_Cancel");
			CancelButton.SetCommand("Cancel");
		}
	}

	private void HideOtherDialogs(bool hide) {
		if (hide) {
			if (gameUI.HasLoadingBackgroundDialog()) {
				gameUI.ShowLoadingBackgroundDialog();
				MoveToFront();
				Input.SetAppModalSurface(this);
			}
			else
				Surface.RestrictPaintToSinglePanel(this);
		}
		else {
			if (gameUI.HasLoadingBackgroundDialog()) {
				gameUI.HideLoadingBackgroundDialog();
				Input.SetAppModalSurface(null);
			}
			else
				Surface.RestrictPaintToSinglePanel(null);
		}
	}

	internal bool SetProgressPoint(float progress) {
		if (ConsoleStyle) {
			if (progress >= 0.99f)
				progress = 1.0f;

			progress = Math.Clamp(progress, 0.0f, 1.0f);
			if ((int)(progress * 25) != ProgressFraction) {
				ProgressFraction = progress;
				return true;
			}

			return false;
		}

		if (!ShowingVACInfo)
			SetupControlSettings(false);

		int oldDrawnSegments = Progress.GetDrawnSegmentCount();
		Progress.SetProgress(progress);
		int newDrawSegments = Progress.GetDrawnSegmentCount();
		return oldDrawnSegments != newDrawSegments;
	}

	internal void SetSecondaryProgress(float progress) {
		if (!ConsoleStyle)
			return;

		if (!ShowingSecondaryProgress && progress > 0.99f)
			return;

		if (!ShowingSecondaryProgress) {
			LoadControlSettings("resource/LoadingDialogDualProgress.res");
			ShowingSecondaryProgress = true;
			Progress2.SetVisible(true);
			SecondaryProgressStartTime = system.GetFrameTime();
		}

		if (progress > SecondaryProgress) {
			Progress2.SetProgress(progress);
			SecondaryProgress = progress;
			LastSecondaryProgressUpdateTime = system.GetFrameTime();
		}

		if (progress < SecondaryProgress) {
			Progress2.SetProgress(progress);
			SecondaryProgress = progress;
			LastSecondaryProgressUpdateTime = system.GetFrameTime();
			SecondaryProgressStartTime = system.GetFrameTime();
		}
	}

	internal void SetStatusText(ReadOnlySpan<char> statusText) {
		if (ConsoleStyle)
			return;

		InfoLabel.SetText(statusText);
	}

	internal bool SetShowProgressText(bool show) {
		if (ConsoleStyle)
			return false;

		bool bret = InfoLabel.IsVisible();
		if (bret != show) {
			SetupControlSettings(show);
			InfoLabel.SetVisible(show);
		}
		return bret;
	}

	public override void OnThink() {
		base.OnThink();

		if (!ConsoleStyle && ShowingSecondaryProgress) {
			Span<char> unicode = stackalloc char[512];
			if (SecondaryProgress >= 1.0f)
				TimeRemainingLabel.SetText("complete");
			else if (ProgressBar.ConstructTimeRemainingString(unicode, SecondaryProgressStartTime, system.GetFrameTime(), SecondaryProgress, (float)LastSecondaryProgressUpdateTime, true))
				TimeRemainingLabel.SetText(unicode);
			else
				TimeRemainingLabel.SetText("");
		}

		// SetAlpha(255);
	}

	public override void PaintBackground() {
		if (!ConsoleStyle) {
			base.PaintBackground();
			return;
		}

		GetSize(out int panelWide, out int panelTall);
		Progress.GetSize(out int barWide, out int barTall);
		int x = (panelWide - barWide) / 2;
		int y = panelTall - barTall;

		if (LoadingBackground != null) {
			IScheme? scheme = SchemeManager.GetScheme("ClientScheme");
			Color color = GetSchemeColor("TanDarker", new(255, 255, 255, 255), scheme);

			LoadingBackground.SetFgColor(color);
			LoadingBackground.SetBgColor(color);

			LoadingBackground.SetPaintBackgroundEnabled(true);
		}

		if (ModInfo.IsSinglePlayerOnly())
			DrawBox(x, y, barWide, barTall, new(0, 0, 0, 255), 1.0f);

		DrawBox(x + 2, y + 2, barWide - 4, barTall - 4, new(100, 100, 100, 255), 1.0f);

		barWide = (int)ProgressFraction * (barWide - 4);
		if (barWide >= 12)
			DrawBox(x + 2, y + 2, barWide, barTall - 4, new(200, 100, 0, 255), 1.0f);
	}
}
