using Game.Client.HUD;

using Source;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Engine;
using Source.GUI.Controls;

namespace Game.Client;

public interface IViewPortPanel
{
	ReadOnlySpan<char> GetName();
	void SetData(KeyValues? data);
	void Reset();
	void Update();
	bool NeedsUpdate();
	bool HasInputElements();
	void ShowPanel(bool state);
	GameActionSet GetPreferredActionSet();
	bool IsVisible();
	void SetParent(IPanel? parent);
}

public interface IViewPort
{
	void UpdateAllPanels();
	void ShowPanel(ReadOnlySpan<char> name, bool state);
	void ShowPanel(IViewPortPanel? panel, bool state);
	void ShowBackGround(bool show);
	IViewPortPanel? FindPanelByName(ReadOnlySpan<char> panelName);
	IViewPortPanel? GetActivePanel();
	void PostMessageToPanel(ReadOnlySpan<char> name, KeyValues? keyValues);
}

public class BaseViewport : EditablePanel, IViewPort
{
	IViewPortPanel? ActivePanel;
	IViewPortPanel? LastActivePanel;
	IPanel? LastPanel;
	readonly List<IViewPortPanel> Panels = [];

	readonly Hud HUD = Singleton<Hud>();
	readonly IEngineVGui EngineVGui = Singleton<IEngineVGui>();
	bool Initialized;
	new bool HasParent;
	InlineArray2<int> OldSize;

	public override void OnScreenSizeChanged(int oldWide, int oldTall) {
		base.OnScreenSizeChanged(oldWide, oldTall);

		ReloadScheme(null);

		RemoveAllPanels();

		CreateDefaultPanels();
	}

	public override void SetParent(IPanel? newParent) {
		base.SetParent(newParent);
		base.SetProportional(true);

		if (newParent != null)
			AnimController.SetProportional(true);
		HasParent = newParent != null;
	}

	public override void OnThink() {
		if (ActivePanel != null && !ActivePanel.IsVisible()) {
			if (LastActivePanel != null) {
				ActivePanel = LastActivePanel;
				ShowPanel(ActivePanel, true);
				LastActivePanel = null;
			}
			else
				ActivePanel = null;
		}


		AnimController.UpdateAnimations(gpGlobals.CurTime);
		int count = Panels.Count;

		for (int i = 0; i < count; i++) {
			IViewPortPanel panel = Panels[i];
			if (panel.NeedsUpdate() && panel.IsVisible())
				panel.Update();
		}

		EngineVGui.GetPanel(VGuiPanelType.ClientDll).GetSize(out int w, out int h);

		if (OldSize[0] != w || OldSize[1] != h) {
			OldSize[0] = w;
			OldSize[1] = h;
			clientMode!.Layout();
		}

		base.OnThink();
	}

	public override void OnParentChanged(IPanel? oldParent, IPanel? newParent) {
		base.OnParentChanged(oldParent, newParent);
	}

	public bool AddNewPanel(IViewPortPanel? panel, ReadOnlySpan<char> debugName) {
		if (panel == null) {
			DevMsg($"BaseViewport.AddNewPanel({debugName}): NULL panel.\n");
			return false;
		}

		if (FindPanelByName(panel.GetName()) != null) {
			DevMsg($"BaseViewport.AddNewPanel: panel with name '{panel.GetName()}' already exists.\n");
			return false;
		}

		Panels.Add(panel);
		panel.SetParent(this);

		return true;
	}

	public static IViewPort? g_ViewPortInterface = null;
	public BaseViewport() : base(null, "CBaseViewport") {
		SetSize(10, 10);
		g_ViewPortInterface = this;
		Initialized = false;
		SetKeyboardInputEnabled(false);
		SetMouseInputEnabled(false);
		IScheme? scheme = SchemeManager.LoadSchemeFromFileEx(EngineVGui.GetPanel(VGuiPanelType.ClientDll), "resource/ClientScheme.res", "ClientScheme");
		SetScheme(scheme!);
		SetProportional(true);

		AnimController = new AnimationController(this);
		AnimController.SetScheme(scheme!);
		AnimController.SetProportional(true);

		if (LoadHudAnimations() == false)
			if (!AnimController.SetScriptFile(this, "scripts/HudAnimations.txt", true))
				Assert(false);

		OldSize[0] = 0;
		OldSize[1] = 0;
	}

	AnimationController AnimController;
	public new virtual AnimationController GetAnimationController() => AnimController;

	public IViewPortPanel? FindPanelByName(ReadOnlySpan<char> panelName) {
		foreach (var panel in Panels) {
			if (panel.GetName().Equals(panelName, StringComparison.Ordinal))
				return panel;
		}
		return null;
	}

	public IViewPortPanel? GetActivePanel() {
		return ActivePanel;
	}

	public void PostMessageToPanel(ReadOnlySpan<char> name, KeyValues? keyValues) {
		PostMessage(((Panel)FindPanelByName(name)!)!, keyValues!);
	}

	public void ShowBackGround(bool show) {
		// todo
	}

	public void ShowPanel(ReadOnlySpan<char> name, bool state) {
		((Panel)FindPanelByName(name)!)!.SetVisible(state);
	}

	public void ShowPanel(IViewPortPanel? panel, bool state) {
		((Panel?)panel)!.SetVisible(state);
	}

	public void UpdateAllPanels() {
		foreach (var panel in Panels) {
			if (panel.IsVisible())
				panel.Update();
		}
	}

	internal void ReloadScheme(ReadOnlySpan<char> from) {
		if (!from.IsEmpty) {
			IScheme scheme = SchemeManager.LoadSchemeFromFileEx(EngineVGui.GetPanel(VGuiPanelType.ClientDll), from, "HudScheme")!;

			SetScheme(scheme);
			SetProportional(true);
			AnimController.SetScheme(scheme);
		}

		if (LoadHudAnimations() == false)
			if (!AnimController.SetScriptFile(this, "scripts/HudAnimations.txt", true))
				Assert(false);

		SetProportional(true);
		// todo: conditions
		// reload the .res file from disk
		LoadControlSettings("scripts/HudLayout.res", null, null, null);

		HUD.RefreshHudTextures();
		InvalidateLayout(true, true);
		HUD.ResetHUD();
	}

	private bool LoadHudAnimations() {
		ReadOnlySpan<char> file = "scripts/hudanimations_manifest.txt";
		KeyValues? manifest = new(file);

		if (manifest.LoadFromFile(filesystem, file, "GAME") == false)
			return false;

		bool clearScript = true;

		for (KeyValues? sub = manifest.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
			if (sub.Name.Equals("file", StringComparison.Ordinal)) {
				if (AnimController.SetScriptFile(this, sub.GetString(), clearScript) == false)
					Assert(false);

				clearScript = false;
				continue;
			}
		}

		return true;
	}

	public void Start() {
		CreateDefaultPanels();

		Initialized = true;
	}

	public override void PaintBackground() {

	}

	public override void Paint() {

	}

	private void RemoveAllPanels() {
		LastPanel = null;

		foreach (var panel in Panels)
			((Panel)panel).DeletePanel();

		Panels.Clear();

		ActivePanel = null;
		LastActivePanel = null;
	}

	private IViewPortPanel? CreatePanelByName(ReadOnlySpan<char> name) {
		IViewPortPanel? panel = null;

#if false
		if (strcmp(name, "scoreboard") == 0)
			panel = new Scoreboard(this);
		else if (strcmp(name, "info") == 0)
			panel = new TextWindow(this);
		else if (strcmp(name, "team") == 0)
			panel = new TeamMenu(this);
		else if (strcmp(name, "specmenu") == 0)
			panel = new SpectatorMenu(this);
		else if (strcmp(name, "specgui") == 0)
			panel = new SpectatorGUI(this);
		else
#endif
		if (strcmp(name, "nav_progress") == 0)
			panel = new NavProgress(this);
#if false
		else if (strcmp(name, "commentary_modelviewer") == 0)
			panel = new CommentaryModelViewer(this);
#endif

		return panel;
	}

	private void CreateDefaultPanels() {
		AddNewPanel(CreatePanelByName("scores"), "PANEL_SCOREBOARD");
		AddNewPanel(CreatePanelByName("info"), "PANEL_INFO");
		AddNewPanel(CreatePanelByName("specgui"), "PANEL_SPECGUI");
		AddNewPanel(CreatePanelByName("specmenu"), "PANEL_SPECMENU");
		AddNewPanel(CreatePanelByName("nav_progress"), "PANEL_NAV_PROGRESS");
	}
}
