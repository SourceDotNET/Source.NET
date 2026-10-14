using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GameUI;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Engine;
using Source.GUI.Controls;

namespace Game.UI;

public class GameMenuItem : MenuItem
{
	bool RightAligned;

	public GameMenuItem(Menu panel, ReadOnlySpan<char> name, ReadOnlySpan<char> text) : base(panel, name, text) => RightAligned = false;

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetFgColor(GetSchemeColor("MainMenu.TextColor", scheme));
		SetBgColor(new(0, 0, 0, 0));
		SetDefaultColor(GetSchemeColor("MainMenu.TextColor", scheme), new(0, 0, 0, 0));
		SetArmedColor(GetSchemeColor("MainMenu.ArmedTextColor", scheme), new(0, 0, 0, 0));
		SetDepressedColor(GetSchemeColor("MainMenu.DepressedTextColor", scheme), new(0, 0, 0, 0));
		SetContentAlignment(Alignment.West);
		SetBorder(null);
		SetDefaultBorder(null);
		SetDepressedBorder(null);
		SetKeyFocusBorder(null);

		IFont? mainMenuFont = scheme.GetFont("MainMenuFont", IsProportional());

		if (mainMenuFont != null)
			SetFont(mainMenuFont);
		else
			SetFont(scheme.GetFont("MenuLarge", IsProportional()));

		SetTextInset(0, 0);
		SetArmedSound("UI/buttonrollover.wav");
		SetDepressedSound("UI/buttonclick.wav");
		SetReleasedSound("UI/buttonclickrelease.wav");
		SetButtonActivationType(ActivationType.OnPressed);

		if (RightAligned)
			SetContentAlignment(Alignment.East);
	}

	public void SetRightAlignedText(bool state) => RightAligned = state;
}

public enum BackgroundState
{
	Initial,
	Loading,
	MainMenu,
	Level,
	Disconnected,
	Exiting
}

public class GameMenu(Panel parent, ReadOnlySpan<char> name) : Menu(parent, name)
{
	protected override void LayoutMenuBorder() { }
	public override int AddMenuItem(ReadOnlySpan<char> itemName, ReadOnlySpan<char> itemText, ReadOnlySpan<char> command, Panel? target, KeyValues? userData = null) {
		MenuItem item = new GameMenuItem(this, itemName, itemText);
		item.AddActionSignalTarget(target);
		item.SetCommand(command);
		item.SetText(itemText);
		item.SetUserData(userData);
		return base.AddMenuItem(item);
	}
	public override int AddMenuItem(ReadOnlySpan<char> itemName, ReadOnlySpan<char> itemText, KeyValues command, Panel? target, KeyValues? userData = null) {
		MenuItem item = new GameMenuItem(this, itemName, itemText);
		item.AddActionSignalTarget(target);
		item.SetCommand(command);
		item.SetText(itemText);
		item.SetUserData(userData);
		return base.AddMenuItem(item);
	}
	public void UpdateMenuItemState(bool isInGame, bool isMultiplayer) {
		for (int i = 0; i < GetChildCount(); i++) {
			Panel child = GetChild(i);
			if (child is MenuItem menuItem) {
				bool shouldBeVisible = true;
				// filter the visibility
				KeyValues? kv = menuItem.GetUserData();
				if (kv == null)
					continue;

				bool vrEnabled = false, vrActive = false;

				if (!isInGame && kv.GetInt("OnlyInGame") != 0) shouldBeVisible = false;
				if (!isInGame && !isMultiplayer && kv.GetInt("notsingle") != 0) shouldBeVisible = false;
				else if (isMultiplayer && kv.GetInt("notmulti") != 0) shouldBeVisible = false;
				else if (!vrEnabled && kv.GetInt("OnlyWhenVREnabled") != 0) shouldBeVisible = false;
				else if (!vrActive && kv.GetInt("OnlyWhenVRActive") != 0) shouldBeVisible = false;
				else if (vrEnabled && kv.GetInt("OnlyWhenVRInactive") != 0) shouldBeVisible = false;

				menuItem.SetVisible(shouldBeVisible);
			}
		}

		if (!isInGame) {
			for (int j = 0; j < GetChildCount() - 2; j++)
				MoveMenuItem(j, j + 1);
		}
		else {
			for (int i = 0; i < GetChildCount(); i++) {
				for (int j = i; j < GetChildCount() - 2; j++) {
					int id1 = GetMenuID(j);
					int id2 = GetMenuID(j + 1);

					MenuItem menuItem1 = GetMenuItem(id1)!;
					MenuItem menuItem2 = GetMenuItem(id2)!;
					KeyValues kv1 = menuItem1.GetUserData()!;
					KeyValues kv2 = menuItem2.GetUserData()!;

					if (kv1.GetInt("InGameOrder") > kv2.GetInt("InGameOrder"))
						MoveMenuItem(id2, id1);
				}
			}
		}

		InvalidateLayout();
	}


	IPanel? MainMenuOverridePanel;

	public void SetMainMenuOverride(IPanel? panel) {
		MainMenuOverridePanel = panel;

		if (MainMenuOverridePanel != null)
			DeleteAllItems();
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		if (stricmp(command, "Open") == 0) {
			if (MainMenuOverridePanel != null) {
				MainMenuOverridePanel.MoveToFront();
				MainMenuOverridePanel.RequestFocus();
			}
			else {
				MoveToFront();
				RequestFocus();
			}
		}
		else
			base.OnCommand(command);
	}

	public override void SetVisible(bool state) {
		if (MainMenuOverridePanel != null) {
			MainMenuOverridePanel.SetVisible(true);
			if (!state)
				MainMenuOverridePanel.MoveToBack();
		}

		base.SetVisible(true);

		if (!state)
			MoveToBack();
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetMenuItemHeight(int.TryParse(scheme.GetResourceString("MainMenu.MenuItemHeight"), out int r) ? r : 0);
		SetBgColor(new(0, 0, 0, 0));
		SetBorder(null);
	}

	public override void OnSetFocus() {
		base.OnSetFocus();
	}

	public override void OnKeyCodePressed(ButtonCode code) {
		int dir = 0;
		switch (code) {
			case ButtonCode.KeyUp:
				dir = -1;
				break;
			case ButtonCode.KeyDown:
				dir = 1;
				break;
		}

		if (dir != 0) {

		}

		base.OnKeyCodePressed(code);
	}

	public override void OnKillFocus(Panel? newPanel) {
		base.OnKillFocus(newPanel);

		if (MainMenuOverridePanel != null)
			Surface.MovePopupToBack(MainMenuOverridePanel);
		else
			Surface.MovePopupToBack(this);
	}
}

public class FooterPanel : EditablePanel
{
	struct ButtonLabel
	{
		public bool Visible;
		public InlineArrayMaxPath<char> Name;
		public InlineArrayMaxPath<char> Text;
		public char Icon;
	}
	readonly List<ButtonLabel> ButtonLabels = [];

	Label SizingLabel;
	bool bPaintBackground;
	bool CenterHorizontal;
	int ButtonPinRight;
	int ButtonGap;
	int ButtonGapDefault;
	int FooterTall;
	int ButtonOffsetFromTop;
	int ButtonSeparator;
	int TextAdjust;
	InlineArray64<char> TextFont;
	InlineArray64<char> ButtonFont;
	InlineArray64<char> FGColor;
	InlineArray64<char> BGColor;
	IFont? FButtonFont;
	IFont? FTextFont;
	string? HelpName;

	public FooterPanel(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetVisible(true);
		SetAlpha(0);
		HelpName = null;

		SizingLabel = new(this, "SizingLabel", "");
		SizingLabel.SetVisible(false);

		ButtonGap = 32;
		ButtonGapDefault = 32;
		ButtonPinRight = 100;
		FooterTall = 80;

		Surface.GetScreenSize(out int wide, out int tall);

		if (tall <= 480)
			FooterTall = 60;

		ButtonOffsetFromTop = 0;
		ButtonSeparator = 4;
		TextAdjust = 0;

		bPaintBackground = false;
		CenterHorizontal = false;

		ButtonFont[0] = '\0';
		TextFont[0] = '\0';
		FGColor[0] = '\0';
		BGColor[0] = '\0';
	}

	// FIXME #37
	public override void Dispose() {
		base.Dispose();
		SetHelpNameAndReset(null);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		FButtonFont = scheme.GetFont((ButtonFont[0] != '\0') ? ButtonFont : "GameUIButtons");
		FTextFont = scheme.GetFont((TextFont[0] != '\0') ? TextFont : "MenuLarge");

		SetFgColor(scheme.GetColor(FGColor, new(255, 255, 255, 255)));
		SetBgColor(scheme.GetColor(BGColor, new(0, 0, 0, 255)));

		GetParent()!.GetBounds(out int x, out _, out int w, out int h);
		SetBounds(x, h - FooterTall, w, FooterTall);
	}

	public override void ApplySettings(KeyValues resourceData) {
		throw new NotImplementedException();
	}

	void SetStandardDialogButtons() {
		SetHelpNameAndReset("Dialog");
		AddNewButtonLabel("#GameUI_Action", "#GameUI_Icons_A_BUTTON");
		AddNewButtonLabel("#GameUI_Close", "#GameUI_Icons_B_BUTTON");
	}

	void SetHelpNameAndReset(ReadOnlySpan<char> name) {
		if (HelpName != null)
			HelpName = null;

		if (!name.IsEmpty)
			HelpName = new(name);

		ClearButtons();
	}

	ReadOnlySpan<char> GetHelpName() => HelpName;

	void ClearButtons() => ButtonLabels.Clear();

	void AddNewButtonLabel(ReadOnlySpan<char> text, ReadOnlySpan<char> icon) {
		ButtonLabel button = new();
		strcpy(button.Name, text);
		button.Visible = true;

		ReadOnlySpan<char> lIcon = Localize.Find(icon);
		if (!lIcon.IsEmpty)
			button.Icon = lIcon[0];
		else
			button.Icon = '\0';

		ReadOnlySpan<char> lText = Localize.Find(text);
		if (!lText.IsEmpty)
			strcpy(button.Text, lText);
		else
			button.Text[0] = '\0';

		ButtonLabels.Add(button);
	}

	void ShowButtonLabel(ReadOnlySpan<char> name, bool show) {
		for (int i = 0; i < ButtonLabels.Count; ++i) {
			ButtonLabel button = ButtonLabels[i];
			if (strcmp(button.Name, name) == 0) {
				button.Visible = show;
				ButtonLabels[i] = button;
				return;
			}
		}
	}

	void SetButtonText(ReadOnlySpan<char> name, ReadOnlySpan<char> text) {
		for (int i = 0; i < ButtonLabels.Count; ++i) {
			ButtonLabel button = ButtonLabels[i];
			if (strcmp(button.Name, name) == 0) {
				ReadOnlySpan<char> lText = Localize.Find(text);
				if (!lText.IsEmpty)
					strcpy(button.Text, lText);
				else
					button.Text[0] = '\0';

				ButtonLabels[i] = button;
				return;
			}
		}
	}

	public override void PaintBackground() {
		if (!bPaintBackground)
			return;

		base.PaintBackground();
	}

	public override void Paint() {
		int wide = GetWide();
		int right = wide - ButtonPinRight;

		int buttonHeight = Surface.GetFontTall(FButtonFont);
		int fontHeight = Surface.GetFontTall(FTextFont);
		int textY = (buttonHeight - fontHeight) / 2 + TextAdjust;

		if (textY < 0)
			textY = 0;

		int y = ButtonOffsetFromTop;

		Span<char> icon = stackalloc char[2];
		if (!CenterHorizontal) {
			int x = right;

			for (int i = 0; i < ButtonLabels.Count; ++i) {
				ButtonLabel button = ButtonLabels[i];
				if (!button.Visible)
					continue;

				SizingLabel.SetFont(FTextFont);
				SizingLabel.SetText(button.Text);
				SizingLabel.SizeToContents();

				int textWidth = SizingLabel.GetWide();

				if (textWidth == 0)
					x += ButtonGap;
				else
					x -= textWidth;

				Surface.DrawSetTextFont(FTextFont);
				Surface.DrawSetTextColor(GetFgColor());
				Surface.DrawSetTextPos(x, y + textY);
				Surface.DrawPrintText(button.Text);

				icon[0] = button.Icon;

				x -= Surface.GetCharacterWidth(FButtonFont, button.Icon) + ButtonSeparator;
				Surface.DrawSetTextFont(FButtonFont);
				Surface.DrawSetTextColor(255, 255, 255, 255);
				Surface.DrawSetTextPos(x, y);
				Surface.DrawPrintText(icon);

				x -= ButtonGap;
			}
		}
		else {
			int x = wide / 2;
			int totalWidth = 0;
			int i = 0;
			int nButtonCount = 0;

			for (i = 0; i < ButtonLabels.Count; ++i) {
				ButtonLabel button = ButtonLabels[i];
				if (!button.Visible)
					continue;

				SizingLabel.SetFont(FTextFont);
				SizingLabel.SetText(button.Text);
				SizingLabel.SizeToContents();

				totalWidth += Surface.GetCharacterWidth(FButtonFont, button.Icon);
				totalWidth += ButtonSeparator;
				totalWidth += SizingLabel.GetWide();

				nButtonCount++;
			}

			totalWidth += (nButtonCount - 1) * ButtonGap;
			x -= totalWidth / 2;

			for (i = 0; i < ButtonLabels.Count; ++i) {
				ButtonLabel button = ButtonLabels[i];
				if (!button.Visible)
					continue;

				SizingLabel.SetFont(FTextFont);
				SizingLabel.SetText(button.Text);
				SizingLabel.SizeToContents();

				int textWidth = SizingLabel.GetWide();

				icon[0] = button.Icon;

				Surface.DrawSetTextFont(FButtonFont);
				Surface.DrawSetTextColor(255, 255, 255, 255);
				Surface.DrawSetTextPos(x, y);
				Surface.DrawPrintText(icon);
				x += Surface.GetCharacterWidth(FButtonFont, button.Icon) + ButtonSeparator;

				Surface.DrawSetTextFont(FTextFont);
				Surface.DrawSetTextColor(GetFgColor());
				Surface.DrawSetTextPos(x, y + textY);
				Surface.DrawPrintText(button.Text);

				x += textWidth + ButtonGap;
			}
		}
	}
}

public class MainMenuGameLogo : EditablePanel
{
	readonly public IEngineClient engine = Singleton<IEngineClient>();

	int OffsetX;
	int OffsetY;

	public MainMenuGameLogo(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) { }
	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);

		OffsetX = resourceData.GetInt("offsetX", 0);
		OffsetY = resourceData.GetInt("offsetY", 0);
	}
	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		KeyValues conditions = new KeyValues("conditions");
		Span<char> background = stackalloc char[MAX_PATH];
		engine.GetMainMenuBackgroundName(background);

		KeyValues subKey = new KeyValues(background);
		conditions.AddSubKey(subKey);

		LoadControlSettings("resource/GameLogo.res", null, null, conditions);
	}

	public int GetOffsetX() => OffsetX;
	public int GetOffsetY() => OffsetY;
}
public class BackgroundMenuButton : Button
{
	public BackgroundMenuButton(Panel parent, ReadOnlySpan<char> name) : base(parent, name, "") { }

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetFgColor(new(255, 255, 255, 255));
		SetBgColor(new(0, 0, 0, 0));
		SetDefaultColor(new(255, 255, 255, 255), new(0, 0, 0, 0));
		SetArmedColor(new(255, 255, 0, 255), new(0, 0, 0, 0));
		SetDepressedColor(new(255, 255, 0, 255), new(0, 0, 0, 0));
		SetContentAlignment(Alignment.West);
		SetBorder(null);
		SetDefaultBorder(null);
		SetDepressedBorder(null);
		SetKeyFocusBorder(null);
		SetTextInset(0, 0);
	}
}
public class SaveBeforeQuitQueryDialog : Frame
{
	public SaveBeforeQuitQueryDialog(Panel parent, ReadOnlySpan<char> name) : base(parent, name) {
		LoadControlSettings("resource/SaveBeforeQuitDialog.res");
		SetDeleteSelfOnClose(true);
		SetSizeable(false);
	}

	public override void DoModal() {
		base.Activate();
		Input.SetAppModalSurface(this);
		MoveToCenterOfScreen();
		Surface.RestrictPaintToSinglePanel(this);

		gameUI.PreventEngineHideGameUI();
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.KeyEscape)
			Close();
		else
			base.OnKeyCodeTyped(code);
	}

	public override void OnKeyCodePressed(ButtonCode code) {
		switch (code) {
			case ButtonCode.KeyXButtonB: Close(); break;
			case ButtonCode.KeyXButtonA: OnCommand("SaveAndQuit"); break;
			case ButtonCode.KeyXButtonX: OnCommand("Quit"); break;
			default: base.OnKeyCodePressed(code); break;
		}
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		if (0 == stricmp(command, "Quit")) 
			PostMessage(GetParent(), new KeyValues("Command", "command", "QuitNoConfirm"));
		else if (0 == stricmp(command, "SaveAndQuit")) {
			// find a new name to save
			Span<char> saveName= stackalloc char[128];
			// TODO: SaveGameDialog.FindSaveSlot(saveName, sizeof(saveName));
			// TODO: if (saveName[0] != '\0') {
			// TODO: 	// save the game
			// TODO: 	Span<char> sz = stackalloc char[256];
			// TODO: 	sprintf(sz, "save %s\n").S(saveName);
			// TODO: 	engine.ClientCmd_Unrestricted(sz.SliceNullTerminatedString());
			// TODO: }

			// quit
			PostMessage(GetParent(), new KeyValues("Command", "command", "QuitNoConfirm"));
		}
		else if (0 == stricmp(command, "Cancel")) 
			Close();
		else 
			base.OnCommand(command);
	}

	public override void OnClose() {
		base.OnClose();
		Surface.RestrictPaintToSinglePanel(null);
		gameUI.AllowEngineHideGameUI();
	}
}

public class QuitQueryBox : QueryBox
{
	public QuitQueryBox(ReadOnlySpan<char> title, ReadOnlySpan<char> queryText, Panel? parent = null) : base(title, queryText, parent) { }

	public override void DoModal(Frame? frameOver) {
		base.DoModal(frameOver);
		Surface.RestrictPaintToSinglePanel(this);
		gameUI.PreventEngineHideGameUI();
	}
	public override void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.KeyEscape)
			Close();
		else
			base.OnKeyCodeTyped(code);
	}
	public override void OnClose() {
		base.OnClose();
		Surface.RestrictPaintToSinglePanel(null);
		gameUI.AllowEngineHideGameUI();
	}
}
public class BasePanel : Panel
{
	GameMenu? GameMenu;
	IPanel? MainMenuOverridePanel;

	readonly public IFileSystem FileSystem = Singleton<IFileSystem>();
	readonly public IEngineClient engine = Singleton<IEngineClient>();
	readonly public ModInfo ModInfo = Singleton<ModInfo>();

	TextureID BackgroundImageID = TextureID.INVALID;
	TextureID LoadingImageID = TextureID.INVALID;

	OptionsDialog? OptionsDialog;

	bool FadingInMenus;
	TimeUnit_t FadeMenuStartTime;
	TimeUnit_t FadeMenuEndTime;
	bool RenderingBackgroundTransition;
	TimeUnit_t TransitionStartTime;
	TimeUnit_t TransitionEndTime;

	public static BasePanel? g_BasePanel;

	public override void OnCommand(ReadOnlySpan<char> command) {
		RunMenuCommand(command);
	}

	[ConCommand("gamemenucommand")]
	static void Gamemenucommand(in TokenizedCommand args) {
		if (args.ArgC() < 2) {
			Msg("Usage:  gamemenucommand <commandname>\n");
			return;
		}

		g_BasePanel?.RunMenuCommand(args[1]);
	}

	private void RunMenuCommand(ReadOnlySpan<char> command) {
		DevMsg($"Incoming BasePanel message '{command}'\n");
		switch (command) {
			case "OpenGameMenu": PostMessage(GameMenu, new("Command", "command", "open")); break;
			case "OpenPlayerListDialog": break;
			case "OpenNewGameDialog": break;
			case "OpenLoadGameDialog": break;
			case "OpenSaveGameDialog": break;
			case "OpenBonusMapsDialog": break;
			case "OpenOptionsDialog": OnOpenOptionsDialog(); break;
			case "OpenControllerDialog": break;
			case "OpenBenchmarkDialog": break;
			case "OpenServerBrowser": OnOpenServerBrowser(); break;
			case "OpenFriendsDialog": break;
			case "OpenLoadDemoDialog": break;
			case "OpenCreateMultiplayerGameDialog": break;
			case "OpenChangeGameDialog": break;
			case "OpenLoadCommentaryDialog": break;
			case "OpenLoadSingleplayerCommentaryDialog": break;
			case "OpenMatchmakingBasePanel": break;
			case "OpenAchievementsDialog": break;
			case "OpenCSAchievementsDialog": break;
			case "AchievementsDialogClosing": break;
			case "Quit":
				OnOpenQuitConfirmationDialog();
				break;
			case "QuitNoConfirm":
				SetVisible(false);
				Surface.RestrictPaintToSinglePanel(this);
				engine.ClientCmd_Unrestricted("quit\n");
				break;
			case "QuitRestartNoConfirm": break;
			case "ResumeGame": gameUI.HideGameUI(); break;
			case "Disconnect": engine.ClientCmd_Unrestricted("disconnect"); break;
			case "DisconnectNoConfirm": break;
			case "ReleaseModalWindow": Surface.RestrictPaintToSinglePanel(null); break;
			case "ShowSigninUI": break;
			case "ShowDeviceSelector": break;
			case "SignInDenied": break;
			case "RequiredSignInDenied": break;
			case "RequiredStorageDenied": break;
			case "StorageDeviceDenied": break;
			case "clear_storage_deviceID": break;
			case "RestartWithNewLanguage": break;
			default:
				base.OnCommand(command);
				break;
		}
	}

	private void OnOpenOptionsDialog() {
		if (!OptionsDialog.IsValid()) {
			OptionsDialog = new OptionsDialog(this);

			PositionDialog(OptionsDialog);
		}

		OptionsDialog.Activate();
	}

	// FIXME: This is meant to be done through some ModuleLoader system, but that doesn't exist yet,
	// 				so for now I'm cheating
	static ServerBrowser.ServerBrowser? ServerBrowser;
	private void OnOpenServerBrowser() {
		ServerBrowser ??= new();
		if (ServerBrowser.Initialize())
			if (ServerBrowser.PostInitialize())
				ServerBrowser.Activate();
	}

	public void PositionDialog(Panel? dialog) {
		if (!dialog.IsValid())
			return;

		Surface.GetWorkspaceBounds(out int x, out int y, out int ww, out int wt);
		dialog.GetSize(out int wide, out int tall);
		dialog.SetPos(x + ((ww - wide) / 2), y + ((wt - tall) / 2));
	}

	public void OnOpenQuitConfirmationDialog() {
		if (gameUI.IsConsoleUI()) {
			throw new NotImplementedException();
		}

		if (gameUI.IsInLevel() && engine.GetMaxClients() == 1) {
			SaveBeforeQuitQueryDialog box = new SaveBeforeQuitQueryDialog(this, "SaveBeforeQuitQueryDialog");
			box.DoModal();
		}
		else {
			QueryBox box = new QuitQueryBox("#GameUI_QuitConfirmationTitle", "#GameUI_QuitConfirmationText", this);
			box.SetOKButtonText("#GameUI_Quit");
			box.SetOKCommand(new KeyValues("Command", "command", "QuitNoConfirm"));
			box.SetCancelCommand(new KeyValues("Command", "command", "ReleaseModalWindow"));
			box.AddActionSignalTarget(this);
			box.DoModal();
		}
	}

	static BackgroundMenuButton CreateMenuButton(BasePanel parent, ReadOnlySpan<char> panelName, ReadOnlySpan<char> panelText) {
		BackgroundMenuButton button = new BackgroundMenuButton(parent, panelName);
		button.SetProportional(true);
		button.SetCommand("OpenGameMenu");
		button.SetText(panelText);
		return button;
	}

	public BasePanel() : base(null, "BaseGameUIPanel") {
		g_BasePanel = this;
		CreateGameMenu();
		CreateGameLogo();

		SetMenuAlpha(0);

		GameMenuButtons.Add(CreateMenuButton(this, "GameMenuButton", ModInfo!.GetGameTitle()));
		GameMenuButtons.Add(CreateMenuButton(this, "GameMenuButton2", ModInfo!.GetGameTitle2()));
	}

	[PanelAnimationVar("0")] protected float BackgroundFillAlpha;

	IFont? FontTest;

	public override void PaintBackground() {
		if (!gameUI.IsInLevel() || GameUI.LoadingDialog != null || ExitingFrameCount > 0)
			DrawBackgroundImage();

		if (BackgroundFillAlpha > 0) {
			Surface.DrawSetColor(0, 0, 0, (int)BackgroundFillAlpha);
			Surface.GetScreenSize(out int wide, out int tall);
			Surface.DrawFilledRect(0, 0, wide, tall);
		}
	}

	Coord GameMenuPos;
	int GameMenuInset;

	public override void PerformLayout() {
		base.PerformLayout();

		Surface.GetScreenSize(out _, out int tall);
		GameMenu!.GetSize(out _, out int menuTall);
		int idealMenuY = GameMenuPos.Y;
		if (idealMenuY + menuTall + GameMenuInset > tall)
			idealMenuY = tall - menuTall - GameMenuInset;

		int yDiff = idealMenuY - GameMenuPos.Y;

		for (int i = 0; i < GameMenuButtons.Count; ++i) {
			GameMenuButtons[i].SizeToContents();
			GameMenuButtons[i].SetPos(GameTitlePos[i].X, GameTitlePos[i].Y + yDiff);
		}

		GameLogo?.SetPos(GameMenuPos.X + GameLogo.GetOffsetX(), idealMenuY - GameLogo.GetTall() + GameLogo.GetOffsetY());
		GameMenu.SetPos(GameMenuPos.X, idealMenuY);

		UpdateGameMenus();
	}

	List<Coord> GameTitlePos = [];
	List<BackgroundMenuButton> GameMenuButtons = [];
	float FrameFadeInTime;
	Color BackdropColor;
	int ExitingFrameCount;
	BackgroundState BackgroundState;

	public void SetBackgroundRenderState(BackgroundState state) {
		if (state == BackgroundState)
			return;

		double frametime = Sys.Time;

		RenderingBackgroundTransition = false;
		FadingInMenus = false;

		if (state == BackgroundState.Exiting) {
			// todo
		}
		else if (state == BackgroundState.Disconnected || state == BackgroundState.MainMenu) {
			FadingInMenus = true;
			FadeMenuStartTime = frametime;
			FadeMenuEndTime = frametime + 3.0f;

			if (state == BackgroundState.MainMenu) {
				RenderingBackgroundTransition = true;
				TransitionStartTime = frametime;
				TransitionEndTime = frametime + 3.0f;
			}
		}
		else if (state == BackgroundState.Loading)
			SetMenuAlpha(0);
		else if (state == BackgroundState.Level)
			SetMenuAlpha(255);

		BackgroundState = state;
	}

	public void UpdateBackgroundState() {
		if (ExitingFrameCount != 0)
			SetBackgroundRenderState(BackgroundState.Exiting);
		else if (gameUI.IsInLevel())
			SetBackgroundRenderState(BackgroundState.Level);
		else if (gameUI.IsInBackgroundLevel() && !LevelLoading)
			SetBackgroundRenderState(BackgroundState.MainMenu);
		else if (LevelLoading)
			SetBackgroundRenderState(BackgroundState.Loading);
		else if (EverActivated && PlatformMenuInitialized)
			SetBackgroundRenderState(BackgroundState.Disconnected);

		if (!PlatformMenuInitialized)
			return;

		int i;
		bool haveActiveDialogs = false;
		bool isInLevel = gameUI.IsInLevel();
		for (i = 0; i < GetChildCount(); ++i) {
			Panel? child = GetChild(i);
			if (child != null && child.IsVisible() && child.IsPopup() && child != GameMenu)
				haveActiveDialogs = true;
		}

		IPanel? parent = GetParent();
		for (i = 0; i < (parent?.GetChildCount() ?? 0); ++i) {
			IPanel? child = parent?.GetChild(i);
			if (child != null && child.IsVisible() && child.IsPopup() && child != this)
				haveActiveDialogs = true;
		}

		bool needDarkenedBackground = haveActiveDialogs || isInLevel;
		if (HaveDarkenedBackground != needDarkenedBackground) {
			float targetAlpha, duration;
			if (needDarkenedBackground) {
				targetAlpha = BackdropColor[3];
				duration = FrameFadeInTime;
			}
			else {
				targetAlpha = 0.0f;
				duration = 2.0f;
			}

			HaveDarkenedBackground = needDarkenedBackground;
			GetAnimationController().RunAnimationCommand(this, "BackgroundFillAlpha", targetAlpha, 0.0f, duration, Interpolators.Linear);
		}

		if (LevelLoading)
			return;

		bool bNeedDarkenedTitleText = haveActiveDialogs;
		if (HaveDarkenedTitleText != bNeedDarkenedTitleText || ForceTitleTextUpdate) {
			float targetTitleAlpha, duration;
			if (haveActiveDialogs) {
				duration = FrameFadeInTime;
				targetTitleAlpha = 32.0f;
			}
			else {
				duration = 2.0f;
				targetTitleAlpha = 255.0f;
			}

			if (GameLogo != null)
				GetAnimationController().RunAnimationCommand(GameLogo, "alpha", targetTitleAlpha, 0.0f, duration, Interpolators.Linear);

			for (i = 0; i < GameMenuButtons.Count; ++i)
				GetAnimationController().RunAnimationCommand(GameMenuButtons[i], "alpha", targetTitleAlpha, 0.0f, duration, Interpolators.Linear);

			HaveDarkenedTitleText = bNeedDarkenedTitleText;
			ForceTitleTextUpdate = false;
		}
	}

	bool HaveDarkenedBackground;
	bool HaveDarkenedTitleText;
	bool ForceTitleTextUpdate;
	bool PlatformMenuInitialized;
	bool LevelLoading;

	public void RunFrame() {
		InvalidateLayout();
		GetAnimationController().UpdateAnimations(Sys.Time);

		UpdateBackgroundState();

		if (!PlatformMenuInitialized)
			PlatformMenuInitialized = true;
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		GameMenuInset = int.TryParse(scheme.GetResourceString("MainMenu.Inset"), out int r) ? r : 0;
		GameMenuInset *= 2;

		IScheme? clientScheme = SchemeManager.LoadSchemeFromFile("resource/ClientScheme.res", "ClientScheme");
		List<Color> buttonColor = [];
		if (clientScheme != null) {
			GameTitlePos.Clear();

#if GMOD_DLL
			// todo: gmod menu
			IFont titleFont = Surface.CreateFont();
			Surface.SetFontGlyphSet(titleFont, "Tahoma", SchemeManager.GetProportionalScaledValue(32), 0, 0, 0, SurfaceFontFlags.Antialias);
#endif

			for (int i = 0; i < GameMenuButtons.Count; ++i) {
#if GMOD_DLL // todo ^, then remove both
				GameMenuButtons[i].SetFont(titleFont);
#else
				GameMenuButtons[i].SetFont(clientScheme.GetFont("ClientTitleFont", true));
#endif
				GameTitlePos.Add(new Coord() {
					X = SchemeManager.GetProportionalScaledValue(int.TryParse(clientScheme.GetResourceString($"Main.Title{i + 1}.X"), out int x) ? x : 0),
					Y = SchemeManager.GetProportionalScaledValue(int.TryParse(clientScheme.GetResourceString($"Main.Title{i + 1}.Y"), out int y) ? y : 0),
				});

				buttonColor.Add(clientScheme.GetColor($"Main.Title{i + 1}.Color", new Color(255, 255, 255, 255)));
			}

			GameMenuPos.X = int.TryParse(clientScheme.GetResourceString("Main.Menu.X"), out r) ? r : 0;
			GameMenuPos.X = SchemeManager.GetProportionalScaledValue(GameMenuPos.X);
			GameMenuPos.Y = int.TryParse(clientScheme.GetResourceString("Main.Menu.Y"), out r) ? r : 0;
			GameMenuPos.Y = SchemeManager.GetProportionalScaledValue(GameMenuPos.Y);

			GameMenuInset = int.TryParse(clientScheme.GetResourceString("Main.BottomBorder"), out r) ? r : 0;
			GameMenuInset = SchemeManager.GetProportionalScaledValue(GameMenuInset);
		}
		else {
			for (int i = 0; i < GameMenuButtons.Count; ++i) {
				GameMenuButtons[i].SetFont(scheme.GetFont("TitleFont"));
				buttonColor.Add(new Color(255, 255, 255, 255));
			}
		}

		for (int i = 0; i < GameMenuButtons.Count; ++i) {
			GameMenuButtons[i].SetDefaultColor(buttonColor[i], new Color(0, 0, 0, 0));
			GameMenuButtons[i].SetArmedColor(buttonColor[i], new Color(0, 0, 0, 0));
			GameMenuButtons[i].SetDepressedColor(buttonColor[i], new Color(0, 0, 0, 0));
		}

		FrameFadeInTime = float.TryParse(scheme.GetResourceString("Frame.TransitionEffectTime"), out float f) ? f : 0;
		BackdropColor = scheme.GetColor("mainmenu.backdrop", new Color(0, 0, 0, 128));

		FontTest = scheme.GetFont("TitleFont");

		Surface.GetScreenSize(out int screenWide, out int screenTall);
		float aspectRatio = (float)screenWide / screenTall;
		bool isWidescreen = aspectRatio >= 1.5999f;

		Span<char> filename = stackalloc char[MAX_PATH];
		Span<char> background = stackalloc char[MAX_PATH];
		engine.GetMainMenuBackgroundName(background); background = background.SliceNullTerminatedString();
		Span<char> finalFilename = sprintf(filename, "console/%s").S(background);

		if (BackgroundImageID == TextureID.INVALID)
			BackgroundImageID = Surface.CreateNewTextureID();

		Surface.DrawSetTextureFile(BackgroundImageID, finalFilename, 0, false);

		if (LoadingImageID == TextureID.INVALID)
			LoadingImageID = Surface.CreateNewTextureID();

		Surface.DrawSetTextureFile(LoadingImageID, "Console/startup_loading", 0, false);
	}

	private void DrawBackgroundImage() {
		GetSize(out int wide, out int tall);
		double frametime = Sys.Time;

		int alpha = 255;

		if (RenderingBackgroundTransition) {
			alpha = (int)((TransitionEndTime - frametime) / (TransitionEndTime - TransitionStartTime) * 255);
			alpha = Math.Clamp(alpha, 0, 255);
		}

		if (ExitingFrameCount != 0) {
			alpha = (int)((TransitionEndTime - frametime) / (TransitionEndTime - TransitionStartTime) * 255);
			alpha = 255 - Math.Clamp(alpha, 0, 255);
		}

		if (RenderingBackgroundTransition || BackgroundState == BackgroundState.Loading) {
			Surface.DrawSetColor(255, 255, 255, alpha);
			Surface.DrawSetTexture(LoadingImageID);
			Surface.DrawGetTextureSize(LoadingImageID, out int twide, out int ttall);
			Surface.DrawTexturedRect(wide - twide, tall - ttall, wide, tall);
		}

#if !GMOD_DLL
		if (FadingInMenus) {
			alpha = (int)((frametime - FadeMenuStartTime) / (FadeMenuEndTime - FadeMenuStartTime) * 255);
			alpha = Math.Clamp(alpha, 0, 255);
			GameMenu!.SetAlpha(alpha);
			if (alpha == 255)
				FadingInMenus = false;
		}
#else // gmod has no fade, since it's menu is html based
		Surface.DrawSetColor(255, 255, 255, alpha);
		Surface.DrawSetTexture(BackgroundImageID);
		Surface.DrawTexturedRect(0, 0, wide, tall);

		if (FadingInMenus) {
			FadingInMenus = false;
			SetMenuAlpha(255);
		}
#endif
	}

	private void SetMenuAlpha(int alpha) {
		GameMenu!.SetAlpha(alpha);
		GameLogo?.SetAlpha(alpha);

		for (int i = 0; i < GameMenuButtons.Count; ++i)
			GameMenuButtons[i].SetAlpha(alpha);

		ForceTitleTextUpdate = true;
	}

	private void CreateGameMenu() {
		KeyValues datafile = new KeyValues("GameMenu");
		datafile.UsesEscapeSequences(true);
#if GMOD_DLL
		// todo: gmod menu, then we can remove this
		if (datafile.LoadFromFile(FileSystem, "sdn_resource/GameMenu.res"))
#else
		if (datafile.LoadFromFile(FileSystem, "resource/GameMenu.res"))
#endif
			GameMenu = RecursiveLoadGameMenu(datafile);

		if (!GameMenu.IsValid())
			Error("Could not load file Resource/GameMenu.res\n");
		else {
			GameMenu.MakeReadyForUse();
			GameMenu.SetAlpha(0);
		}
	}

	public void SetMainMenuOverride(IPanel? panel) {
		MainMenuOverridePanel = panel;

		GameMenu?.SetMainMenuOverride(panel);

		MainMenuOverridePanel?.SetParent(this);
	}

	public override void OnThink() {
		// KeyRepeat todo
		base.OnThink();
	}

	private GameMenu RecursiveLoadGameMenu(KeyValues datafile) {
		GameMenu menu = new GameMenu(this, datafile.Name);
		for (KeyValues? dat = datafile.GetFirstSubKey(); dat != null; dat = dat.GetNextKey()) {
			ReadOnlySpan<char> label = dat.GetString("label", "<unknown>");
			ReadOnlySpan<char> cmd = dat.GetString("command", null);
			ReadOnlySpan<char> name = dat.GetString("name", label);

			menu.AddMenuItem(name, label, cmd, this, dat);
		}
		return menu;
	}

	MainMenuGameLogo? GameLogo;

	private void CreateGameLogo() {
		if (ModInfo.UseGameLogo()) {
			GameLogo = new MainMenuGameLogo(this, "GameLogo");

			GameLogo.MakeReadyForUse();
			GameLogo.InvalidateLayout(true, true);
			GameLogo.SetAlpha(0);
		}
	}

	bool EverActivated;

	internal void OnGameUIActivated() {
		// Map load failed?

		if (!EverActivated) {
			UpdateGameMenus();
			EverActivated = true;
		}
	}

	int LastGameMenuState = -1;
	private void UpdateGameMenus() {
		bool isInGame = gameUI.IsInLevel();
		bool isMulti = isInGame && engine.GetMaxClients() > 1;

		int state = (isInGame ? 1 : 0) | (isMulti ? 2 : 0);
		if (state == LastGameMenuState) // prevent unnecessary vgui layouts per frame
			return;
		LastGameMenuState = state;

		GameMenu!.UpdateMenuItemState(isInGame, isMulti);

		if (MainMenuOverridePanel != null)
			Singleton<IVGui>().PostMessage(MainMenuOverridePanel, new KeyValues("UpdateMenu"), null);

		InvalidateLayout();
		GameMenu!.SetVisible(true);
	}

	internal void OnLevelLoadingStarted() {
		LevelLoading = true;
	}

	internal void OnLevelLoadingFinished() {
		LevelLoading = false;
	}

	readonly static KeyValues KV_GameUIHidden = new("GameUIHidden");
	internal void OnGameUIHidden() {
		if (OptionsDialog.IsValid())
			PostMessage(OptionsDialog, KV_GameUIHidden);
	}

	public int GetMenuAlpha() => GameMenu!.GetAlpha();
}
