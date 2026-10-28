using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Source.Engine;
#if !SWDS
class ConVarCheckbutton : CheckButton
{
	[CvarIgnore]
	ConVar? ConVar;
	public ConVarCheckbutton(Panel parent, ReadOnlySpan<char> name, ReadOnlySpan<char> text) : base(parent, name, text) {
		ConVar = null;
	}

	public void SetConVar(ConVar convar) {
		ConVar = convar;
		SetSelected(ConVar.GetBool());
	}

	public override void SetSelected(bool state) {
		base.SetSelected(state);
		ConVar?.SetValue(state ? 1 : 0);
	}
}

public class VGuiTree : TreeView
{
	public VGuiTree(Panel parent, ReadOnlySpan<char> name) : base(parent, name) { }

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetFont(scheme.GetFont("ConsoleText", false)!);
		SetPaintBackgroundEnabled(false);
	}
}

public class DrawTreeFrame : Frame
{
	public static Panel? DrawTreeSelectedPanel = null;
	public VGuiTree Tree;
	ConVarCheckbutton ShowVisible;
	ConVarCheckbutton ShowHidden;
	ConVarCheckbutton PopupsOnly;
	ConVarCheckbutton Freeze;
	ConVarCheckbutton ShowPanelPtr;
	ConVarCheckbutton ShowPanelAlpha;
	ConVarCheckbutton RenderOrder;
	ConVarCheckbutton DrawFocus;
	ConVarCheckbutton ShowBounds;
	ConVarCheckbutton HighlightSelected;
	Button PerformLayoutBtn;
	Button ReloadSchemeBtn;

	public DrawTreeFrame(Panel parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetTitle("VGUI Hierarchy", false);
		// SetMenuButtonVisible(false);

		Tree = new(this, "Tree View");
		Tree.MakeReadyForUse();
		Tree.SetVisible(true);

		ShowVisible = new(this, "show visible", "Show Visible");
		ShowVisible.MakeReadyForUse();
		ShowVisible.SetConVar(VGuiDrawTree.vgui_drawtree_visible);
		ShowVisible.SetVisible(true);

		ShowHidden = new(this, "show hidden", "Show Hidden");
		ShowHidden.MakeReadyForUse();
		ShowHidden.SetConVar(VGuiDrawTree.vgui_drawtree_hidden);
		ShowHidden.SetVisible(true);

		PopupsOnly = new(this, "popups only", "Popups Only");
		PopupsOnly.MakeReadyForUse();
		PopupsOnly.SetConVar(VGuiDrawTree.vgui_drawtree_popupsonly);
		PopupsOnly.SetVisible(true);

		DrawFocus = new(this, "draw focus", "Highlight MouseOver");
		DrawFocus.MakeReadyForUse();
		DrawFocus.SetConVar(FocusOverlayPanel.vgui_drawfocus);
		DrawFocus.SetVisible(true);

		Freeze = new(this, "freeze option", "Freeze");
		Freeze.MakeReadyForUse();
		Freeze.SetConVar(VGuiDrawTree.vgui_drawtree_freeze);
		Freeze.SetVisible(true);

		ShowPanelPtr = new(this, "panel ptr option", "Show Addresses");
		ShowPanelPtr.MakeReadyForUse();
		ShowPanelPtr.SetConVar(VGuiDrawTree.vgui_drawtree_panelptr);
		ShowPanelPtr.SetVisible(true);

		ShowPanelAlpha = new(this, "panel alpha option", "Show Alpha");
		ShowPanelAlpha.MakeReadyForUse();
		ShowPanelAlpha.SetConVar(VGuiDrawTree.vgui_drawtree_panelalpha);
		ShowPanelAlpha.SetVisible(true);

		RenderOrder = new(this, "render order option", "In Render Order");
		RenderOrder.MakeReadyForUse();
		RenderOrder.SetConVar(VGuiDrawTree.vgui_drawtree_render_order);
		RenderOrder.SetVisible(true);

		ShowBounds = new(this, "show panel bounds", "Show Panel Bounds");
		ShowBounds.MakeReadyForUse();
		ShowBounds.SetConVar(VGuiDrawTree.vgui_drawtree_bounds);
		ShowBounds.SetVisible(true);

		HighlightSelected = new(this, "highlight selected", "Highlight Selected");
		HighlightSelected.MakeReadyForUse();
		HighlightSelected.SetConVar(VGuiDrawTree.vgui_drawtree_draw_selected);
		HighlightSelected.SetVisible(true);

		PerformLayoutBtn = new(this, "performlayout", "Perform Layout (Highlighted)");
		PerformLayoutBtn.MakeReadyForUse();
		PerformLayoutBtn.SetVisible(true);

		ReloadSchemeBtn = new(this, "reloadscheme", "Reload Scheme (Highlighted)");
		ReloadSchemeBtn.MakeReadyForUse();
		ReloadSchemeBtn.SetVisible(true);

		GetBgColor().GetColor(out int r, out int g, out int b, out int a);
		SetBgColor(new(r, g, b, 128));
	}

	public override void PerformLayout() {
		base.PerformLayout();

		GetClientArea(out int x, out int y, out int w, out int t);

		int yOffset = y;

		ShowVisible.SetPos(x, yOffset);
		ShowVisible.SetWide(w / 2);
		yOffset += ShowVisible.GetTall();

		ShowHidden.SetPos(x, yOffset);
		ShowHidden.SetWide(w / 2);
		yOffset += ShowHidden.GetTall();

		PopupsOnly.SetPos(x, yOffset);
		PopupsOnly.SetWide(w / 2);
		yOffset += PopupsOnly.GetTall();

		DrawFocus.SetPos(x, yOffset);
		DrawFocus.SetWide(w / 2);
		yOffset += DrawFocus.GetTall();

		ShowBounds.SetPos(x, yOffset);
		ShowBounds.SetWide(w / 2);

		yOffset = y;
		Freeze.SetPos(x + w / 2, yOffset);
		Freeze.SetWide(w / 2);
		yOffset += Freeze.GetTall();

		ShowPanelPtr.SetPos(x + w / 2, yOffset);
		ShowPanelPtr.SetWide(w / 2);
		yOffset += ShowPanelPtr.GetTall();

		ShowPanelAlpha.SetPos(x + w / 2, yOffset);
		ShowPanelAlpha.SetWide(w / 2);
		yOffset += ShowPanelAlpha.GetTall();

		RenderOrder.SetPos(x + w / 2, yOffset);
		RenderOrder.SetWide(w / 2);
		yOffset += RenderOrder.GetTall();

		HighlightSelected.SetPos(x + w / 2, yOffset);
		HighlightSelected.SetWide(w / 2);
		yOffset += HighlightSelected.GetTall();

		PerformLayoutBtn.SetPos(x, yOffset);
		PerformLayoutBtn.SizeToContents();
		PerformLayoutBtn.SetWide(w);
		yOffset += PerformLayoutBtn.GetTall();

		ReloadSchemeBtn.SetPos(x, yOffset);
		ReloadSchemeBtn.SizeToContents();
		ReloadSchemeBtn.SetWide(w);
		yOffset += ReloadSchemeBtn.GetTall();

		Tree.SetBounds(x, yOffset, w, t - (yOffset - y));
	}

	[UiMessage("TreeViewItemSelected")]
	public void RecalculateSelectedHighlight() {
		Assert(Tree != null);

		if (VGuiDrawTree.vgui_drawtree_draw_selected.GetInt() == 0)
			return;

		if (Tree.GetSelectedItemCount() != 1)
			return;

		List<int> list = [];
		Tree.GetSelectedItems(ref list);

		Assert(list.Count == 1);

		KeyValues? data = Tree.GetItemData(list[0]);
		if (data == null)
			DrawTreeSelectedPanel = null;
		else
			DrawTreeSelectedPanel = (Panel?)data.GetPtr("PanelPtr");
	}

	static readonly KeyValues KV_Command_PerformLayout = new("Command", "command", "performlayout");
	static readonly KeyValues KV_Command_ReloadScheme = new("Command", "command", "reloadscheme");
	[UiCommand("performlayout")]
	private void OnPerformLayout() => DrawTreeSelectedPanel?.SendMessage(KV_Command_PerformLayout, this);

	[UiCommand("reloadscheme")]
	private void OnReloadScheme() => DrawTreeSelectedPanel?.SendMessage(KV_Command_ReloadScheme, this);
}

public class VGuiDrawTree
{
	static DrawTreeFrame? DrawTreeFrame;
	static bool ForceRefresh;

	public static ConVar vgui_drawtree = new("vgui_drawtree", "0", FCvar.Cheat, "Draws the vgui panel hiearchy to the specified depth level.");
	public static ConVar vgui_drawtree_visible = new("vgui_drawtree_visible", "1", FCvar.None, "Draw the visible panels.", callback: ChangeCallback_RefreshDrawTree);
	public static ConVar vgui_drawtree_hidden = new("vgui_drawtree_hidden", "0", FCvar.None, "Draw the hidden panels.", callback: ChangeCallback_RefreshDrawTree);
	public static ConVar vgui_drawtree_popupsonly = new("vgui_drawtree_popupsonly", "0", FCvar.None, "Draws the vgui popup list in hierarchy(1) or most recently used(2) order.", callback: ChangeCallback_RefreshDrawTree);
	public static ConVar vgui_drawtree_freeze = new("vgui_drawtree_freeze", "0", FCvar.None, "Set to 1 to stop updating the vgui_drawtree view.", callback: ChangeCallback_RefreshDrawTree);
	public static ConVar vgui_drawtree_panelptr = new("vgui_drawtree_panelptr", "0", FCvar.None, "Show the panel pointer values in the vgui_drawtree view.", callback: ChangeCallback_RefreshDrawTree);
	public static ConVar vgui_drawtree_panelalpha = new("vgui_drawtree_panelalpha", "0", FCvar.None, "Show the panel alpha values in the vgui_drawtree view.", callback: ChangeCallback_RefreshDrawTree);
	public static ConVar vgui_drawtree_render_order = new("vgui_drawtree_render_order", "0", FCvar.None, "List the vgui_drawtree panels in render order.", callback: ChangeCallback_RefreshDrawTree);
	public static ConVar vgui_drawtree_bounds = new("vgui_drawtree_bounds", "0", FCvar.None, "Show panel bounds.", callback: ChangeCallback_RefreshDrawTree);
	public static ConVar vgui_drawtree_draw_selected = new("vgui_drawtree_draw_selected", "0", FCvar.None, "Highlight the selected panel", callback: ChangeCallback_RefreshDrawTree);

	private static void ChangeCallback_RefreshDrawTree(IConVar var, in ConVarChangeContext ctx) => ForceRefresh = true;
	[ConCommand("+vgui_drawtree")] static void vgui_drawtree_on() => vgui_drawtree.SetValue(1);
	[ConCommand("-vgui_drawtree")] static void vgui_drawtree_off() => vgui_drawtree.SetValue(0);

	private readonly static ISurface Surface = Singleton<ISurface>();

	private static void RecursivePrintTree(IPanel current, KeyValues currentPanel, int popupDepthCounter) {
		if (current == null)
			return;

		if (vgui_drawtree_visible.GetInt() == 0 && current.IsVisible())
			return;
		else if (vgui_drawtree_hidden.GetInt() == 0 && !current.IsVisible())
			return;
		else if (popupDepthCounter <= 0 && current.IsPopup())
			return;

		KeyValues newParent = currentPanel;
		KeyValues val = currentPanel.CreateNewKey();

		Span<char> name = stackalloc char[1024];
		ReadOnlySpan<char> inputName = current.GetName();
		if (!inputName.IsEmpty)
			sprintf(name, "%s").S(inputName);
		else
#if DEBUG
			sprintf(name, "%s").S(current.GetClassName());
#else
			sprintf(name, "%s").S("<no name>");
#endif

		if (current.IsMouseInputEnabled()) sprintf(name, "%s, +m").S(name);
		if (current.IsKeyboardInputEnabled()) sprintf(name, "%s, +k").S(name);

		if (vgui_drawtree_bounds.GetInt() != 0) {
			current.GetPos(out int x, out int y);
			current.GetSize(out int w, out int h);
			Span<char> b = stackalloc char[128];
			sprintf(name, "%s [%d %d %d %d]").S(name).D(x - 4).D(y - 4).D(w - 4).D(h - 4);
			sprintf(name, "%s, %s").S(name).S(b);
		}

		Span<char> str = stackalloc char[1024];
		if (vgui_drawtree_panelptr.GetInt() != 0)
			sprintf(str, "%s - [0x%d]").S(name).D(current.GetHashCode());
		else if (vgui_drawtree_panelalpha.GetInt() != 0)
			sprintf(str, "%s - [%d]").S(name).D(current.GetAlpha());
		else
			sprintf(str, "%s").S(name);

		val.SetString("Text", str);
		val.SetPtr("PanelPtr", current);

		newParent = val;

		if (current == DrawTreeFrame!.Tree)
			return;

		int count = current.GetChildCount();
		for (int i = 0; i < count; i++) {
			IPanel childPanel = current.GetChild(i);
			RecursivePrintTree(childPanel, newParent, popupDepthCounter - 1);
		}
	}

	private static bool UpdateItemState(TreeView tree, int childItemId, KeyValues sub) {
		bool ret = false;
		KeyValues itemData = tree.GetItemData(childItemId)!;

		if (itemData.GetInt("PanelPtr") != sub.GetInt("PanelPtr") || strcmp(itemData.GetString("Text"), sub.GetString("Text")) != 0) {
			tree.ModifyItem(childItemId, sub);
			ret = true;
		}

		Panel panel = (Panel)sub.GetPtr("PanelPtr")!;

		int[] baseColor = [255, 255, 255];
		if (panel.IsPopup()) {
			baseColor[0] = 255; baseColor[1] = 255; baseColor[2] = 0;
		}

		if (FocusOverlayPanel.FocusPanelList.Contains(panel)) {
			baseColor[0] = 0; baseColor[1] = 255; baseColor[2] = 0;
			tree.ExpandItem(childItemId, true);
		}

		if (!panel.IsVisible()) {
			baseColor[0] >>= 1; baseColor[1] >>= 1; baseColor[2] >>= 1;
		}

		tree.SetItemFgColor(childItemId, new(baseColor[0], baseColor[1], baseColor[2], 255));
		return ret;
	}

	delegate bool UpdateItemStateFn(TreeView tree, int childItemId, KeyValues sub);

	private static void IncrementalUpdateTree_R(TreeView tree, int curTreeNode, KeyValues values, ref bool changes, UpdateItemStateFn fn) {
		int curChild = 0;
		int numChildren = tree.GetNumChildren(curTreeNode);
		KeyValues? sub = values.GetFirstSubKey();

		while (curChild < numChildren || sub != null) {
			if (sub != null) {
				ReadOnlySpan<char> sentinel = "*\0*".AsSpan();
				ReadOnlySpan<char> subText = sub.GetString("Text".AsSpan(), sentinel);
				if (!subText.SequenceEqual(sentinel)) {
					if (curChild < numChildren) {
						int childItemId = tree.GetChild(curTreeNode, curChild);

						if (fn(tree, childItemId, sub))
							changes = true;

						IncrementalUpdateTree_R(tree, childItemId, sub, ref changes, fn);
					}
					else {
						changes = true;
						int childItemId = tree.AddItem(sub, curTreeNode);
						if (fn(tree, childItemId, sub))
							changes = true;
						IncrementalUpdateTree_R(tree, childItemId, sub, ref changes, fn);
					}
					++curChild;
				}
				sub = sub.GetNextKey();
			}
			else {
				int childItemId = tree.GetChild(curTreeNode, curChild);
				--numChildren;
				changes = true;
				tree.RemoveItem(-childItemId, false);
			}
		}
	}

	private static bool IncrementalUpdateTree(TreeView tree, KeyValues values, UpdateItemStateFn fn, int root) {
		if (root == -1) {
			root = tree.GetRootItemIndex();
			if (root == -1) {
				KeyValues tempValues = new("");
				tempValues.SetString("Text", "");
				root = tree.AddItem(tempValues, root);
			}
		}

		bool changes = false;
		IncrementalUpdateTree_R(tree, root, values, ref changes, fn);
		return changes;
	}

	private static void IncrementalUpdateTree(TreeView tree, KeyValues values) {
		if (!ForceRefresh && vgui_drawtree_freeze.GetInt() != 0)
			return;

		ForceRefresh = false;

		bool invalidLayout = IncrementalUpdateTree(tree, values, UpdateItemState, -1);
		tree.ExpandItem(tree.GetRootItemIndex(), true);

		DrawTreeFrame?.RecalculateSelectedHighlight();

		if (invalidLayout)
			tree.InvalidateLayout();
	}

	private static bool WillPanelBeVisible(IPanel? panel) {
		while (panel != null) {
			if (!panel.IsVisible())
				return false;
			panel = panel.GetParent();
		}
		return true;
	}

	private static void AddPopupsToKeyValues(KeyValues currentParent) {
		int count = Surface.GetPopupCount();
		for (int i = 0; i < count; i++) {
			IPanel popup = Surface.GetPopup(i)!;
			if (vgui_drawtree_hidden.GetInt() != 0 || WillPanelBeVisible(popup))
				RecursivePrintTree(popup, currentParent, 1);
		}
	}

	private static void FillKeyValues(KeyValues currentParent) {
		// if (!EngineVGui().IsInitialized())
		// return;

		IPanel Base = Surface.GetEmbeddedPanel();
		if (vgui_drawtree_popupsonly.GetInt() != 0)
			AddPopupsToKeyValues(currentParent);
		else if (vgui_drawtree_render_order.GetInt() != 0) {
			RecursivePrintTree(Base, currentParent, 0);
			AddPopupsToKeyValues(currentParent);
		}
		else
			RecursivePrintTree(Base, currentParent, 99999);
	}

	private static void DrawHierarchy() {
		if (vgui_drawtree.GetInt() <= 0) {
			DrawTreeFrame?.SetVisible(false);
			return;
		}

		DrawTreeFrame!.SetVisible(true);

		KeyValues root = new("");
		root.SetString("Text", "<shouldn't see this>");

		FillKeyValues(root);

		IncrementalUpdateTree(DrawTreeFrame.Tree, root);
	}

	public static void CreateDrawTreePanel(Panel parent) {
		if (DrawTreeFrame != null)
			return;

		int widths = 300;

		DrawTreeFrame = new DrawTreeFrame(parent, "VGuiDrawTree");
		DrawTreeFrame.MakeReadyForUse();
		DrawTreeFrame.SetVisible(false);
		DrawTreeFrame.SetBounds(parent.GetWide() - widths, 0, widths, parent.GetTall() - 10);
		DrawTreeFrame.MakePopup(false, false);
		DrawTreeFrame.SetKeyboardInputEnabled(true);
		DrawTreeFrame.SetMouseInputEnabled(true);
	}

	static void MoveDrawTreePanelToFront() => DrawTreeFrame?.MoveToFront();

	public static void UpdateDrawTreePanel() {
		if (DrawTreeFrame != null)
			DrawHierarchy();
	}

	[ConCommand()]
	public static void vgui_drawtree_clear() {
		if (DrawTreeFrame != null && DrawTreeFrame.Tree != null)
			DrawTreeFrame.Tree.RemoveAll();
	}
}
#endif
