using Source.Common.Commands;
using Source.Engine;
using Source.GUI.Controls;

// TODO: Remove uses of ConVarRef when those cvars exist
#if !SWDS
enum PerformanceTool_t
{
	PERF_TOOL_NONE = 0,
	PERF_TOOL_PROP_FADES,
	PERF_TOOL_AREA_PORTALS,
	PERF_TOOL_OCCLUSION,
	PERF_TOOL_COUNT,
	DEFAULT_PERF_TOOL = PERF_TOOL_NONE
}

class PerfUIChildPanel : EditablePanel
{
	public PerfUIChildPanel(Panel parent, ReadOnlySpan<char> panelName) : base(parent, panelName) {
		SetVisible(false);
	}

	public virtual void Activate() { }
	public virtual void Deactivate() { }
}

class PropFadeUIPanel : PerfUIChildPanel
{
	const int VISUALIZE_NONE = 0;
	const int VISUALIZE_FADE_DISTANCE = 1;
	const int VISUALIZE_FADE_SCREEN_WIDTH = 2;
	const int VISUALIZE_TYPE_COUNT = 3;

	ComboBox Visualization;
	TextEntry MinScreenArea;
	TextEntry MaxScreenArea;

	readonly string[] FadeVisualizeLabel = [
		"No visualization",
		"Show Fade Distance",
		"Show Fade Screen Width"
	];

	public PropFadeUIPanel(Panel parent) : base(parent, "PropFadeUIPanel") {
		Visualization = new(this, "VisualizeMode", VISUALIZE_TYPE_COUNT, false);
		for (int i = 0; i < VISUALIZE_TYPE_COUNT; ++i)
			Visualization.AddItem(FadeVisualizeLabel[i], null);
		Visualization.AddActionSignalTarget(this);
		Visualization.ActivateItem(0);

		MinScreenArea = new(this, "MinFadeSize");
		MaxScreenArea = new(this, "MaxFadeSize");

		LoadControlSettings("Resource/PerfPropFadeUIPanel.res");
	}

	public override void OnTextChanged(Panel sender) {
		if (sender == Visualization) {
			OnVisualizationSelected();
			return;
		}

		if (sender == MinScreenArea || sender == MaxScreenArea) {
			Span<char> buff = stackalloc char[256];

			float? minArea, maxArea;

			MinScreenArea.GetText(buff);
			minArea = float.TryParse(buff, out float val1) ? val1 : null;

			MaxScreenArea.GetText(buff);
			maxArea = float.TryParse(buff, out float val2) ? val2 : null;

			if (minArea != null && maxArea != null) {
				// todo modelinfoclient.SetLevelScreenFadeRange(minArea, maxArea);
			}
		}
	}

	private void OnVisualizationSelected() {
		int mode = Visualization.GetActiveItem();

		ConVarRef r_staticpropinfo = new("r_staticpropinfo");
		if (!r_staticpropinfo.IsValid())
			return;

		switch (mode) {
			case VISUALIZE_NONE:
				r_staticpropinfo.SetValue(0);
				break;
			case VISUALIZE_FADE_DISTANCE:
				r_staticpropinfo.SetValue(3);
				break;
			case VISUALIZE_FADE_SCREEN_WIDTH:
				r_staticpropinfo.SetValue(4);
				break;
		}
	}

	public override void Activate() {
		float minArea, maxArea;
		// modelinfoclient.GetLevelScreenFadeRange(out minArea, out maxArea);
		minArea = 0;//todo ^
		maxArea = 0;

		Span<char> buff = stackalloc char[256];

		minArea.ToString("F2").CopyTo(buff);
		MinScreenArea.SetText(buff);

		maxArea.ToString("F2").CopyTo(buff);
		MaxScreenArea.SetText(buff);

		OnVisualizationSelected();
	}

	public override void Deactivate() {
		ConVarRef r_staticpropinfo = new("r_staticpropinfo");
		if (!r_staticpropinfo.IsValid())
			return;

		r_staticpropinfo.SetValue(0);
	}
}

class AreaPortalsUIPanel : PerfUIChildPanel
{
	ConVarRef r_DrawPortals;
	ConVarRef mat_wireframe;

	public AreaPortalsUIPanel(Panel parent) : base(parent, "AreaPortalUIPanel") {
		r_DrawPortals = new("r_drawportals");
		mat_wireframe = new("mat_wireframe");
	}

	public override void Activate() {
		if (r_DrawPortals.IsValid()) r_DrawPortals.SetValue(1);
		if (mat_wireframe.IsValid()) mat_wireframe.SetValue(3);
	}

	public override void Deactivate() {
		if (r_DrawPortals.IsValid()) r_DrawPortals.SetValue(0);
		if (mat_wireframe.IsValid()) mat_wireframe.SetValue(0);
	}
}

class OcclusionUIPanel : PerfUIChildPanel
{
	const int VISUALIZE_NONE = 0;
	const int VISUALIZE_ON = 1;
	const int VISUALIZE_TYPE_COUNT = 2;

	ComboBox Visualization;
	TextEntry MinOccluderArea;
	TextEntry MaxOccluderArea;
	CheckButton DeactivateOcclusion;

	ConVarRef r_occluderminarea;
	ConVarRef r_occludeemaxarea;
	ConVarRef r_visocclusion;
	ConVarRef mat_wireframe;

	readonly string[] OccVisualizeLabel = [
		"No visualization",
		"View occluders"
	];

	public OcclusionUIPanel(Panel parent) : base(parent, "OcclusionUIPanel") {
		Visualization = new(this, "VisualizeMode", VISUALIZE_TYPE_COUNT, false);
		for (int i = 0; i < VISUALIZE_TYPE_COUNT; ++i)
			Visualization.AddItem(OccVisualizeLabel[i], null);
		Visualization.AddActionSignalTarget(this);
		Visualization.ActivateItem(0);

		MinOccluderArea = new(this, "MinOccluderSize");
		MaxOccluderArea = new(this, "MaxOccluderSize");

		DeactivateOcclusion = new(this, "DeactivateOcclusion", "");
		DeactivateOcclusion.AddActionSignalTarget(this);

		r_occluderminarea = new("r_occluderminarea");
		r_occludeemaxarea = new("r_occludeemaxarea");
		r_visocclusion = new("r_visocclusion");
		mat_wireframe = new("mat_wireframe");

		LoadControlSettings("Resource/PerfOcclusionUIPanel.res");
	}

	public override void Activate() {
		OnVisualizationSelected();
		OnDeactivateOcclusion();

		Span<char> buff = stackalloc char[256];

		if (r_occluderminarea.IsValid()) {
			float minArea = r_occluderminarea.GetFloat();
			minArea.ToString("F2").CopyTo(buff);
			MinOccluderArea.SetText(buff);
		}

		if (r_occludeemaxarea.IsValid()) {
			float maxArea = r_occludeemaxarea.GetFloat();
			maxArea.ToString("F2").CopyTo(buff);
			MaxOccluderArea.SetText(buff);
		}
	}

	public override void OnTextChanged(Panel sender) {
		if (sender == Visualization) {
			OnVisualizationSelected();
			return;
		}

		if (sender == MinOccluderArea || sender == MaxOccluderArea) {
			Span<char> buff = stackalloc char[256];

			float? minArea, maxArea;

			MinOccluderArea.GetText(buff);
			minArea = float.TryParse(buff, out float val1) ? val1 : null;

			MaxOccluderArea.GetText(buff);
			maxArea = float.TryParse(buff, out float val2) ? val2 : null;

			if (minArea != null && r_occluderminarea.IsValid())
				r_occluderminarea.SetValue(minArea.Value);

			if (maxArea != null && r_occludeemaxarea.IsValid())
				r_occludeemaxarea.SetValue(maxArea.Value);
		}
	}

	private void OnVisualizationSelected() {
		int mode = Visualization.GetActiveItem();

		if (!r_visocclusion.IsValid() || !mat_wireframe.IsValid())
			return;

		switch (mode) {
			case VISUALIZE_NONE:
				r_visocclusion.SetValue(0);
				mat_wireframe.SetValue(0);
				break;
			case VISUALIZE_ON:
				r_visocclusion.SetValue(1);
				mat_wireframe.SetValue(3);
				break;
		}
	}

	private void OnDeactivateOcclusion() {
		if (r_visocclusion.IsValid()) r_visocclusion.SetValue(DeactivateOcclusion.IsSelected() ? 0 : 1);
	}

	private void OnCheckButtonChecked(Panel panel) {
		if (panel == DeactivateOcclusion)
			OnDeactivateOcclusion();
	}
}

class PerfUIPanel : Frame
{
	ComboBox PerformanceTool;
	PerformanceTool_t PerfTool;
	PerfUIChildPanel[] ToolPanels = new PerfUIChildPanel[(int)PerformanceTool_t.PERF_TOOL_COUNT];
	PerfUIChildPanel? CurrentToolPanel;

	public PerfUIPanel(Panel parent) : base(parent, "PerfUIPanel") {
		SetTitle("Level Performance Tools", true);

		PerformanceTool = new(this, "PerformanceTool", 10, false);

		VGui.AddTickSignal(this, 0);

		LoadControlSettings("resource/PerfUIPanel.res");

		SetVisible(false);
		SetSizeable(false);
		SetMoveable(true);

		int w = 250;
		int h = 400;
		VideoMode_Common vm = (VideoMode_Common)videoMode;
		int x = vm.GetModeStereoWidth() - w - 10;
		int y = (int)((vm.GetModeStereoHeight() - h) / 2 + vm.GetModeStereoHeight() * 0.2);

		SetBounds(x, y, w, h);

		ToolPanels[(int)PerformanceTool_t.PERF_TOOL_NONE] = new PerfUIChildPanel(this, "PerfNone");
		ToolPanels[(int)PerformanceTool_t.PERF_TOOL_PROP_FADES] = new PropFadeUIPanel(this);
		ToolPanels[(int)PerformanceTool_t.PERF_TOOL_AREA_PORTALS] = new AreaPortalsUIPanel(this);
		ToolPanels[(int)PerformanceTool_t.PERF_TOOL_OCCLUSION] = new OcclusionUIPanel(this);

		for (int i = 0; i < (int)PerformanceTool_t.PERF_TOOL_COUNT; ++i)
			ToolPanels[i].SetBounds(0, 75, w, h - 75);

		PerfTool = PerformanceTool_t.PERF_TOOL_COUNT;
		CurrentToolPanel = null;
		PopulateControls();
	}

	public void Init() {
		GetBounds(out int x, out int y, out int w, out int h);
		Input.SetCursorPos(x + w / 2, y + h / 2);
	}

	public void Shutdown() {
		if (CurrentToolPanel != null) {
			CurrentToolPanel.Deactivate();
			CurrentToolPanel.SetVisible(false);
		}
	}

	readonly string[] PerfToolNames = [
		"None",
		"Prop Fades",
		"Area Portals",
		"Occlusion"
	];

	private void PopulateControls() {
		PerformanceTool.RemoveAll();
		for (int i = 0; i < (int)PerformanceTool_t.PERF_TOOL_COUNT; ++i)
			PerformanceTool.AddItem(PerfToolNames[i], null);
		PerformanceTool.AddActionSignalTarget(this);
		PerformanceTool.ActivateItem(0);
	}

	public override void OnTick() {
		if (!Host.CanCheat())
			Shutdown();

		base.OnTick();
	}

	public override void OnTextChanged(Panel sender) {
		if (sender == PerformanceTool)
			OnPerfToolSelected();
	}

	private void OnPerfToolSelected() {
		int tool = PerformanceTool.GetActiveItem();
		if (tool == (int)PerfTool)
			return;

		if (CurrentToolPanel != null) {
			CurrentToolPanel.Deactivate();
			CurrentToolPanel.SetVisible(false);
		}

		PerfTool = (PerformanceTool_t)tool;
		CurrentToolPanel = ToolPanels[tool];
		CurrentToolPanel.SetVisible(true);
		CurrentToolPanel.Activate();
	}

	public override void Activate() {
		if (!Host.CanCheat())
			return;

		Init();
		base.Activate();
	}
}

public interface IEnginePerfTools
{
	void Init();
	void Shutdown();
	bool ShouldPause();
}

class EnginePerfTools : IEnginePerfTools
{
	static PerfUIPanel? PerfUI;

	public void Init() { }

	public void Shutdown() {
		if (PerfUI != null)
			PerfUI.Shutdown();
	}

	public static void InstallPerformanceToolsUI(Panel parent) {
		if (PerfUI != null)
			return;

		PerfUI = new(parent);
		Assert(PerfUI != null);
	}

	public bool ShouldPause() => false;

	[ConCommand()]
	static void perfui(in TokenizedCommand args) {
		if (PerfUI == null)
			return;

		bool wasVisible = PerfUI.IsVisible();
		if (wasVisible)
			PerfUI.Close();
		else
			PerfUI.Activate();
	}
}
#endif
