using Source.Common;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

class TreeNodeText : TextEntry
{
	private const int CLICK_TO_EDIT_DELAY_MS = 500;
	bool EditingInPlace;
	string OriginalText;
	bool LabelEditingAllowed;
	bool ArmForEditing;
	bool WaitingForRelease;
	long ArmingTime;
	TreeView Tree;

	public TreeNodeText(Panel? parent, ReadOnlySpan<char> name, TreeView tree) : base(parent, name) {
		Tree = tree;
		EditingInPlace = false;
		LabelEditingAllowed = false;
		SetDragEnabled(false);
		SetDropEnabled(false);
		AddActionSignalTarget(this);
		ArmForEditing = false;
		WaitingForRelease = false;
		ArmingTime = 0L;
		SetAllowKeyBindingChainToParent(true);
	}

	public override void OnTextChanged(Panel from) {
		base.OnTextChanged(from);
		GetParent()!.InvalidateLayout();
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		if (message.Name == "TextChanged") {
			OnTextChanged((Panel)sender!);
			return;
		}
		base.OnMessage(message, sender);
	}

	public override bool IsKeyRebound(ButtonCode code, KeyModifier modifiers) {
		if (EditingInPlace)
			return false;
		return base.IsKeyRebound(code, modifiers);
	}

	public override void PaintBackground() {
		base.PaintBackground();

		if (!LabelEditingAllowed || !EditingInPlace)
			return;

		GetSize(out int w, out int h);
		Surface.DrawSetColor(GetFgColor());
		Surface.DrawOutlinedRect(0, 0, w, h);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetBorder(null);
		SetCursor(CursorCode.Arrow);
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (EditingInPlace) {
			if (code == ButtonCode.KeyEnter)
				FinishEditingInPlace();
			else if (code == ButtonCode.KeyEscape)
				FinishEditingInPlace(true);
			else
				base.OnKeyCodeTyped(code);
			return;
		}
		else if (code == ButtonCode.KeyEnter && IsLabelEditingAllowed())
			EnterEditingInPlace();
		else
			CallParentFunction(new("KeyCodeTyped", "code", code.ToString()));
	}

	public override void OnTick() {
		base.OnTick();

		if (ArmForEditing) {
			long msecSinceArming = system.GetTimeMillis() - ArmingTime;
			if (msecSinceArming >= CLICK_TO_EDIT_DELAY_MS) {
				ArmForEditing = false;
				WaitingForRelease = false;
				VGui.RemoveTickSignal(this);
				EnterEditingInPlace();
			}
		}
	}

	public override void OnMouseReleased(ButtonCode code) {
		if (EditingInPlace) {
			base.OnMouseReleased(code);
			return;
		}

		if (WaitingForRelease /*&& !IsBeingDragged()*/) { //todo 
			ArmForEditing = true;
			WaitingForRelease = false;
			ArmingTime = system.GetTimeMillis();
			VGui.AddTickSignal(this);
		}
		else
			WaitingForRelease = false;

		CallParentFunction(new("MouseReleased", "code", code.ToString()));
	}

	public override void OnCursorMoved(int x, int y) => CallParentFunction(new("OnCursorMoved", "x", x, "y", y));

	public override void OnMousePressed(ButtonCode code) {
		if (EditingInPlace) {
			base.OnMousePressed(code);
			return;
		}

		bool shift = Input.IsKeyDown(ButtonCode.KeyLShift) || Input.IsKeyDown(ButtonCode.KeyRShift);
		bool ctrl = Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl);

		List<int> list = [];
		Tree.GetSelectedItems(ref list);
		bool isOnlyOneItemSelected = list.Count == 1;

		if (!shift && !ctrl && !ArmForEditing && IsLabelEditingAllowed() && isOnlyOneItemSelected && IsTextFullSelected() /*&& !IsBeingDragged()*/) //todo
			WaitingForRelease = true;

		CallParentFunction(new("MousePressed", "code", code.ToString()));
	}

	public void SetLabelEditingAllowed(bool allowed) => LabelEditingAllowed = allowed;
	public bool IsLabelEditingAllowed() => LabelEditingAllowed;

	public override void OnMouseDoublePressed(ButtonCode code) {
		if (EditingInPlace) {
			base.OnMouseDoublePressed(code);
			return;
		}

		if (ArmForEditing) {
			ArmForEditing = false;
			WaitingForRelease = false;
			VGui.RemoveTickSignal(this);
		}

		CallParentFunction(new("MouseDoublePressed", "code", code.ToString()));
	}

	public void EnterEditingInPlace() {
		if (EditingInPlace)
			return;

		EditingInPlace = true;
		Span<char> buf = stackalloc char[1024];
		GetText(buf);
		OriginalText = buf.ToString();
		SetCursor(CursorCode.IBeam);
		SetEditable(true);
		SelectNone();
		GotoTextEnd();
		RequestFocus();
		SelectAllText(false);
		Tree.SetLabelBeingEdited(true);
	}

	private void FinishEditingInPlace(bool revert = false) {
		if (!EditingInPlace)
			return;

		Tree.SetLabelBeingEdited(false);
		SetEditable(false);
		SetCursor(CursorCode.Arrow);
		EditingInPlace = false;
		Span<char> buf = stackalloc char[1024];
		GetText(buf);

		if (strcmp(buf, OriginalText) == 0)
			return;

		if (revert) {
			SetText(OriginalText);
			GetParent()!.InvalidateLayout();
		}
		else {
			KeyValues msg = new("LabelChanged", "original", OriginalText, "changed", buf.ToString());
			PostActionSignal(msg);
		}
	}

	public override void OnKillFocus(Panel? newPanel) {
		base.OnKillFocus(newPanel);
		FinishEditingInPlace();
	}

	public override void OnMouseWheeled(int delta) {
		if (EditingInPlace) {
			base.OnMouseWheeled(delta);
			return;
		}

		CallParentFunction(new("MouseWheeled", "delta", delta));
	}

	public bool IsBeingEdited() => EditingInPlace;
}

public class TreeNodeImage : ImagePanel
{
	public TreeNodeImage(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetBlockDragChaining(true);
	}

	public override void OnMousePressed(ButtonCode code) => CallParentFunction(new("MousePressed", "code", code.ToString()));
	public override void OnMouseDoublePressed(ButtonCode code) => CallParentFunction(new("MouseDoublePressed", "code", code.ToString()));
	public override void OnMouseWheeled(int delta) => CallParentFunction(new("MouseWheeled", "delta", delta));
	public override void OnCursorMoved(int x, int y) => CallParentFunction(new("OnCursorMoved", "x", x, "y", y));
}

class TreeViewSubPanel : Panel
{
	public TreeViewSubPanel(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) { }
	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetBorder(null);
	}

	public override void OnMousePressed(ButtonCode code) => CallParentFunction(new("MousePressed", "code", code.ToString()));
	public override void OnMouseDoublePressed(ButtonCode code) => CallParentFunction(new("MouseDoublePressed", "code", code.ToString()));
	public override void OnMouseWheeled(int delta) => CallParentFunction(new("MouseWheeled", "delta", delta));
	public override void OnCursorMoved(int x, int y) => CallParentFunction(new("OnCursorMoved", "x", x, "y", y));
}

public class TreeNode : Panel
{
	const int TREE_INDENT_AMOUNT = 20;
	public int ItemIndex;
	public int ParentIndex;
	public KeyValues Data;
	public List<TreeNode> Children = [];
	public bool Expand;
	int NodeWidth;
	int MaxVisibleWidth;
	TreeNodeText Text;
	TextImage ExpandImage;
	TreeNodeImage ImagePanel;
	bool ExpandableWithoutChildren;
	TreeView TreeView;
	int ClickedItem;
	bool ClickedSelected;

	public TreeNode(Panel? parent, TreeView treeView) : base(parent, "TreeNode") {
		ClickedItem = 0;
		ClickedSelected = false;
		Data = null;
		TreeView = treeView;
		ItemIndex = -1;
		NodeWidth = 0;
		MaxVisibleWidth = 0;

		ExpandImage = new("+");
		ExpandImage.SetPos(3, 1);

		ImagePanel = new(this, "TreeImage");
		ImagePanel.SetPos(TREE_INDENT_AMOUNT, 3);

		Text = new(this, "TreeNodeText", treeView);
		Text.SetMultiline(false);
		Text.SetEditable(false);
		Text.SetPos(TREE_INDENT_AMOUNT * 2, 0);
		Text.AddActionSignalTarget(this);

		Expand = false;
		ExpandableWithoutChildren = false;
	}

	public void SetText(ReadOnlySpan<char> text) => Text.SetText(text);

	public void SetLabelEditingAllowed(bool state) {
		Assert(TreeView.IsLabelEditingAllowed());
		Text.SetLabelEditingAllowed(state);
	}

	public bool IsLabelEditingAllowed() => Text.IsLabelEditingAllowed();
	public override bool GetDropContextMenu(Menu menu, List<KeyValues> msglist) => TreeView.GetItemDropContextMenu(ItemIndex, menu, msglist);
	public override bool IsDroppable(List<KeyValues> msglist) => TreeView.IsItemDroppable(ItemIndex, msglist);
	public void OnPanelDropped(List<KeyValues> msglist) => TreeView.OnItemDropped(ItemIndex, msglist);
	public override CursorCode GetDropCursor(List<KeyValues> msglist) => TreeView.GetItemDropCursor(ItemIndex, msglist);

	public override void OnCreateDragData(KeyValues msg) { // TODO: override
		TreeView.AddSelectedItem(ItemIndex, false);
		TreeView.GenerateDragDataForItem(ItemIndex, msg);
	}

	public override void OnGetAdditionalDragPanels(ref List<Panel> draggables) {
		List<int> list = [];
		TreeView.GetSelectedItems(ref list);
		for (int i = 0; i < list.Count; i++) {
			int itemIndex = list[i];
			if (itemIndex == ItemIndex)
				continue;

			draggables.Add(TreeView.GetItem(itemIndex)!);
		}
	}

	private void OnLabelChanged(KeyValues data) {
		ReadOnlySpan<char> oldString = Data.GetString("original");
		ReadOnlySpan<char> newString = data.GetString("text");
		if (TreeView.IsLabelEditingAllowed())
			TreeView.OnLabelChanged(ItemIndex, oldString, newString);
	}

	public void EditLabel() {
		if (Text.IsLabelEditingAllowed() && !Text.IsBeingEdited())
			Text.EnterEditingInPlace();
	}

	public void SetFont(IFont font) {
		Assert(font != null);
		if (font == null)
			return;

		Text.SetFont(font);
		ExpandImage.SetFont(font);
		InvalidateLayout();
		foreach (var child in Children)
			child.SetFont(font);
	}

	public void SetKeyValues(KeyValues data) {
		if (data != Data)
			Data = data.MakeCopy();

		SetText(data.GetString("Text", ""));
		ExpandableWithoutChildren = data.GetInt("Expand", 0) != 0;
		InvalidateLayout();
	}

	public bool IsSelected() => TreeView.IsItemSelected(ItemIndex);

	public override void PaintBackground() {
		if (!Text.IsBeingEdited()) {
			if (IsSelected())
				Text.SelectAllText(false);
			else
				Text.SelectNoText();
		}

		base.PaintBackground();
	}

	public override void PerformLayout() {
		base.PerformLayout();

		int width;
		if (Data.GetInt("SelectedImage", 0) == 0 && Data.GetInt("Image", 0) == 0)
			width = TREE_INDENT_AMOUNT;
		else
			width = TREE_INDENT_AMOUNT * 2;

		Text.SetPos(width, 0);

		Text.SetToFullWidth();
		Text.GetSize(out int contentWide, out _);
		contentWide += 10;
		Text.SetSize(contentWide, TreeView.GetRowHeight());
		width += contentWide;
		SetSize(width, TreeView.GetRowHeight());

		NodeWidth = width;
		CalculateVisibleMaxWidth();
	}

	public TreeNode? GetParentNode() {
		if (ParentIndex < 0 || ParentIndex >= TreeView.NodeList.Count)
			return null;
		return TreeView.NodeList[ParentIndex];
	}

	public int GetChildrenCount() => Children.Count;

	private int ComputeInsertionPosition(TreeNode child) {
		if (TreeView.SortFunc == null)
			return GetChildrenCount();

		int start = 0;
		int end = GetChildrenCount() - 1;
		while (start <= end) {
			int mid = (start + end) >> 1;
			if (TreeView.SortFunc(Children[mid].Data, child.Data))
				start = mid + 1;
			else if (TreeView.SortFunc(child.Data, Children[mid].Data))
				end = mid - 1;
			else
				return mid;
		}
		return end;
	}

	public int FindChild(TreeNode child) {
		if (TreeView.SortFunc == null) {
			AssertMsg(false, "This code has never been tested. Is it correct?");
			for (int i = 0; i < Children.Count; i++) {
				if (Children[i] == child)
					return i;
			}
			return -1;
		}

		int start = 0;
		int end = GetChildrenCount() - 1;
		while (start <= end) {
			int mid = (start + end) >> 1;
			if (Children[mid] == child)
				return mid;
			if (TreeView.SortFunc(Children[mid].Data, child.Data))
				start = mid + 1;
			else
				end = mid - 1;
		}

		int max = GetChildrenCount();
		while (end < max) {
			if (TreeView.SortFunc(child.Data, Children[end].Data))
				return -1;

			if (Children[end] == child)
				return end;

			++end;
		}

		return -1;
	}

	public void AddChild(TreeNode child) {
		int i = ComputeInsertionPosition(child);
		Children.Insert(i, child);
	}

	public void SetNodeExpanded(bool expanded) {
		Expand = expanded;

		if (Expand) {
			TreeView.GenerateChildrenOfNode(ItemIndex);

			if (GetChildrenCount() < 1) {
				Expand = false;
				ExpandableWithoutChildren = false;
				TreeView.InvalidateLayout();
				return;
			}

			ExpandImage.SetText("-");
		}
		else {
			ExpandImage.SetText("+");

			if (ExpandableWithoutChildren && GetChildrenCount() > 0)
				TreeView.RemoveChildrenOfNode(ItemIndex);

			int selectedItem = TreeView.GetFirstSelectedItem();
			if (selectedItem != -1 && TreeView.NodeList[selectedItem].HasParent(this))
				TreeView.AddSelectedItem(ItemIndex, true);
		}

		CalculateVisibleMaxWidth();
		TreeView.InvalidateLayout();
	}

	public bool IsExpanded() => Expand;

	public int CountVisibleNodes() {
		int count = 1; // myself
		if (Expand) {
			for (int i = 0; i < GetChildrenCount(); i++)
				count += Children[i].CountVisibleNodes();
		}
		return count;
	}

	private void CalculateVisibleMaxWidth() {
		int width;
		if (Expand) {
			int childMaxWidth = GetMaxChildrenWidth();
			childMaxWidth += TREE_INDENT_AMOUNT;
			width = Math.Max(childMaxWidth, NodeWidth);
		}
		else
			width = NodeWidth;

		if (width != MaxVisibleWidth) {
			MaxVisibleWidth = width;
			if (GetParentNode() != null)
				GetParentNode()!.OnChildWidthChange();
			else
				TreeView.InvalidateLayout();
		}
	}

	private void OnChildWidthChange() => CalculateVisibleMaxWidth();

	public int GetMaxChildrenWidth() {
		int maxWidth = 0;
		foreach (var child in Children) {
			int childWidth = child.GetVisibleMaxWidth();
			if (childWidth > maxWidth)
				maxWidth = childWidth;
		}
		return maxWidth;
	}

	public int GetVisibleMaxWidth() => MaxVisibleWidth;

	private int GetDepth() {
		int depth = 0;
		TreeNode? parent = GetParentNode();
		while (parent != null) {
			depth++;
			parent = parent.GetParentNode();
		}
		return depth;
	}

	private bool HasParent(TreeNode treeNode) {
		TreeNode? parent = GetParentNode();
		while (parent != null) {
			if (parent == treeNode)
				return true;

			parent = parent.GetParentNode();
		}
		return false;
	}

	private bool IsBeingDisplayed() {
		TreeNode? parent = GetParentNode();
		while (parent != null) {
			if (!parent.Expand)
				return false;

			parent = parent.GetParentNode();
		}
		return true;
	}

	public override void SetVisible(bool state) {
		base.SetVisible(state);

		bool childrenVisible = state && Expand;
		foreach (var child in Children)
			child.SetVisible(childrenVisible);
	}

	public override void Paint() {
		if (GetChildrenCount() > 0 || ExpandableWithoutChildren)
			ExpandImage.Paint();

		int imageIndex;
		if (IsSelected())
			imageIndex = Data.GetInt("SelectedImage", 0);
		else
			imageIndex = Data.GetInt("Image", 0);

		if (imageIndex != 0) {
			IImage? image = TreeView.GetImage(imageIndex);
			if (image != null)
				ImagePanel.SetImage(image);
			ImagePanel.Paint();
		}

		Text.Paint();
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetBorder(null);
		SetFgColor(TreeView.GetFgColor());
		SetBgColor(TreeView.GetBgColor());
		SetFont(TreeView.GetFont());
	}

	public void SetSelectionTextColor(Color clr) => Text?.SetSelectionTextColor(clr);
	public void SetSelectionBgColor(Color clr) => Text?.SetSelectionBgColor(clr);
	public void SetSelectionUnfocusedBgColor(Color clr) => Text?.SetSelectionUnfocusedBgColor(clr);

	public override void SetBgColor(in Color clr) {
		base.SetBgColor(clr);
		Text?.SetBgColor(clr);
	}

	public override void SetFgColor(in Color clr) {
		base.SetFgColor(clr);
		Text?.SetFgColor(clr);
	}

	public override void OnSetFocus() => Text.RequestFocus();

	public int GetPrevChildItemIndex(TreeNode currentChild) {
		int i;
		for (i = 0; i < GetChildrenCount(); i++) {
			if (Children[i] == currentChild) {
				if (i <= 0)
					return -1;

				return Children[i - 1].ItemIndex;
			}
		}
		return -1;
	}

	public int GetNextChildItemIndex(TreeNode currentChild) {
		int i;
		for (i = 0; i < GetChildrenCount(); i++) {
			if (Children[i] == currentChild) {
				if (i > GetChildrenCount() - 1)
					return -1;

				return Children[i + 1].ItemIndex;
			}
		}
		return -1;
	}

	private void SelectPrevChild(TreeNode currentChild) {
		int i;
		for (i = 0; i < GetChildrenCount(); i++) {
			if (Children[i] == currentChild)
				break;
		}

		if (i == GetChildrenCount()) {
			Assert(false);
			return;
		}

		if (i == 0)
			TreeView.AddSelectedItem(GetParentNode()!.ItemIndex, true);
		else {
			TreeNode child = Children[i - 1];

			while (child.Expand && child.GetChildrenCount() > 0)
				child = child.Children[child.GetChildrenCount() - 1];
			TreeView.AddSelectedItem(child.ItemIndex, true);
		}
	}

	private void SelectNextChild(TreeNode currentChild) {
		int i;
		for (i = 0; i < GetChildrenCount(); i++) {
			if (Children[i] == currentChild)
				break;
		}

		if (i == GetChildrenCount()) {
			Assert(false);
			return;
		}

		if (i == GetChildrenCount() - 1)
			GetParentNode()?.SelectNextChild(this);
		else
			TreeView.AddSelectedItem(Children[i + 1].ItemIndex, true);
	}

	private void ClosePreviousParents(TreeNode? previousParent) {
		List<int> selected = [];
		TreeView.GetSelectedItems(ref selected);

		if (selected.Count == 0) {
			Assert(false);
			return;
		}

		TreeNode? selectedItem = TreeView.GetItem(selected[0]);
		TreeNode? parent = selectedItem?.GetParentNode();

		if (previousParent != null && parent != null) {
			while (previousParent!.ItemIndex > parent.ItemIndex) {
				previousParent.SetNodeExpanded(false);
				previousParent = previousParent.GetParentNode();
			}
		}
	}

	private void StepInto(bool closePrevious) {
		if (!Expand)
			SetNodeExpanded(true);

		if (GetChildrenCount() > 0 && Expand)
			TreeView.AddSelectedItem(Children[0].ItemIndex, true);
		else if (GetParentNode() != null) {
			TreeNode parent = GetParentNode();
			parent.SelectNextChild(this);

			if (closePrevious)
				ClosePreviousParents(parent);
		}
	}

	private void StepOut(bool closePrevious) {
		TreeNode? parent = GetParentNode();
		if (parent != null) {
			TreeView.AddSelectedItem(parent.ItemIndex, true);
			if (parent.GetParentNode() != null && closePrevious)
				parent.GetParentNode().SelectNextChild(parent);
			if (closePrevious)
				ClosePreviousParents(parent);
			else
				parent.SetNodeExpanded(true);
		}
	}

	private void StepOver(bool closePrevious) {
		TreeNode? parent = GetParentNode();
		if (parent != null) {
			GetParentNode().SelectNextChild(this);
			if (closePrevious) {
				ClosePreviousParents(parent);
			}
		}
	}

	public override void OnKeyCodeTyped(ButtonCode code) {

	}

	public override void OnMouseWheeled(int delta) => CallParentFunction(new KeyValues("MouseWheeled", "delta", delta));

	public override void OnMouseDoublePressed(ButtonCode code) {
		Input.GetCursorPos(out int x, out int y);

		if (code == ButtonCode.MouseLeft) {
			ScreenToLocal(ref x, ref y);

			if (x < TREE_INDENT_AMOUNT)
				SetNodeExpanded(!Expand);
		}
	}

	public bool IsDragEnabled() {
		Input.GetCursorPos(out int x, out int y);
		ScreenToLocal(ref x, ref y);

		if (x < TREE_INDENT_AMOUNT)
			return false;

		return false;//todo base.IsDragEnabled();
	}

	public override void OnMouseReleased(ButtonCode code) {
		base.OnMouseReleased(code);

		if (Input.GetMouseCapture() == this) {
			Input.SetMouseCapture(null);
			return;
		}

		Input.GetCursorPos(out int x, out int y);
		ScreenToLocal(ref x, ref y);

		if (x < TREE_INDENT_AMOUNT)
			return;

		bool ctrldown = Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl);
		bool shiftdown = Input.IsKeyDown(ButtonCode.KeyLShift) || Input.IsKeyDown(ButtonCode.KeyRShift);

		if (!ctrldown && !shiftdown && (code == ButtonCode.MouseLeft))
			TreeView.AddSelectedItem(ItemIndex, true);
	}

	public override void OnCursorMoved(int x, int y) {
		if (Input.GetMouseCapture() != this)
			return;

		LocalToScreen(ref x, ref y);
		TreeView.ScreenToLocal(ref x, ref y);
		int newItem = TreeView.FindItemUnderMouse(x, y);
		if (newItem == -1)
			return;

		int startItem = ClickedItem;
		int endItem = newItem;
		if (startItem > endItem)
			(endItem, startItem) = (startItem, endItem);

		List<TreeNode> list = [];
		TreeView.RootNode!.FindNodesInRange(ref list, startItem, endItem);

		for (int i = 0; i < list.Count; i++) {
			TreeNode item = list[i];
			if (ClickedSelected)
				TreeView.AddSelectedItem(item.ItemIndex, false);
			else
				TreeView.RemoveSelectedItem(item.ItemIndex);
		}
	}

	public override void OnMousePressed(ButtonCode code) {
		base.OnMousePressed(code);

		bool ctrl = Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl);
		bool shift = Input.IsKeyDown(ButtonCode.KeyLShift) || Input.IsKeyDown(ButtonCode.KeyRShift);

		Input.GetCursorPos(out int x, out int y);

		bool expandTree = TreeView.LeftClickExpandsTree;

		if (code == ButtonCode.MouseLeft) {
			ScreenToLocal(ref x, ref y);

			if (x < TREE_INDENT_AMOUNT) {
				if (expandTree)
					SetNodeExpanded(!Expand);
			}
			else {
				ClickedItem = ItemIndex;
				if (TreeView.IsMultipleItemDragEnabled())
					Input.SetMouseCapture(this);

				if (shift)
					TreeView.RangeSelectItems(ItemIndex);
				else if (!IsSelected() || ctrl) {
					if (IsSelected() && ctrl)
						TreeView.RemoveSelectedItem(ItemIndex);
					else
						TreeView.AddSelectedItem(ItemIndex, !ctrl);
				}
				else if (IsSelected() && TreeView.IsMultipleItemDragEnabled())
					TreeView.AddSelectedItem(ItemIndex, !shift);
			}

			ClickedSelected = TreeView.IsItemSelected(ItemIndex);
		}
		else if (code == ButtonCode.MouseRight) {
			if (!TreeView.IsItemSelected(ItemIndex))
				TreeView.AddSelectedItem(ItemIndex, true);
			TreeView.GenerateContextMenu(ItemIndex, x, y);
		}
	}

	public void RemoveChildren() {
		for (int i = GetChildrenCount() - 1; i >= 0; i--)
			TreeView.RemoveItem(Children[i].ItemIndex, false, true);
		Children.Clear();
	}

	public void FindNodesInRange(ref List<TreeNode> list, int startIndex, int endIndex) {
		list.Clear();
		bool finished = false;
		bool foundStart = false;
		FindNodesInRange_R(ref list, ref finished, ref foundStart, startIndex, endIndex);
	}

	private void FindNodesInRange_R(ref List<TreeNode> list, ref bool finished, ref bool foundStart, int startIndex, int endIndex) {
		if (finished)
			return;

		if (foundStart == true) {
			list.Add(this);

			if (ItemIndex == startIndex || ItemIndex == endIndex) {
				finished = true;
				return;
			}
		}
		else if (ItemIndex == startIndex || ItemIndex == endIndex) {
			foundStart = true;
			list.Add(this);

			if (startIndex == ItemIndex) {
				finished = true;
				return;
			}
		}

		if (!Expand)
			return;

		for (int i = 0; i < GetChildrenCount(); i++)
			Children[i].FindNodesInRange_R(ref list, ref finished, ref foundStart, startIndex, endIndex);
	}

	public void PositionAndSetVisibleNodes(ref int start, ref int count, int x, ref int y) {
		if (start == 0) {
			base.SetVisible(true);
			SetPos(x, y);
			y += TreeView.GetRowHeight();
			count--;
		}
		else {
			start--;
			base.SetVisible(false);
		}

		x += TREE_INDENT_AMOUNT;
		for (int i = 0; i < GetChildrenCount(); i++) {
			if (count > 0 && Expand)
				Children[i].PositionAndSetVisibleNodes(ref start, ref count, x, ref y);
			else
				Children[i].SetVisible(false);
		}
	}

	public TreeNode? FindItemUnderMouse(ref int start, ref int count, int x, int y, int mx, int my) {
		if (start == 0) {
			GetPos(out _, out int posy);
			if (mx >= posy && my < posy + TreeView.GetRowHeight())
				return this;
			y += TreeView.GetRowHeight();
			count--;
		}
		else
			start--;

		x += TREE_INDENT_AMOUNT;
		for (int i = 0; i < GetChildrenCount(); i++) {
			if (count > 0 && Expand) {
				TreeNode? child = Children[i].FindItemUnderMouse(ref start, ref count, x, y, mx, my);
				if (child != null)
					return child;
			}
		}

		return null;
	}

	public int CountVisibleIndex() {
		int count = 1; // myself
		if (GetParentNode() != null) {
			for (int i = 0; i < GetParentNode()!.GetChildrenCount(); i++) {
				TreeNode child = GetParentNode()!.Children[i];
				if (child == this)
					break;

				count += child.CountVisibleNodes();
			}
			return count + GetParentNode()!.CountVisibleIndex();
		}
		return count;
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		if (message.Name == "LabelChanged") {
			OnLabelChanged(message);
			return;
		}
		base.OnMessage(message, sender);
	}
}

public class TreeView : Panel
{
	public delegate bool TreeViewSortFunc_t(KeyValues node1, KeyValues node2);

	const int WINDOW_BORDER_WIDTH = 2;
	const int TREE_INDENT_AMOUNT = 20;

	bool AllowLabelEditing;
	bool DragEnabledItems;
	bool DeleteImageListWhenDone;
	public bool LeftClickExpandsTree;
	bool LabelBeingEdited;
	bool MultipleItemDragging;
	bool AllowMultipleSelections;

	public List<TreeNode> NodeList = [];
	ScrollBar HorzScrollBar;
	ScrollBar VertScrollBar;
	int RowHeight;

	ImageList? ImageList;
	public TreeNode? RootNode;
	public TreeViewSortFunc_t? SortFunc;
	IFont? Font;

	List<TreeNode> SelectedItems = [];
	TreeViewSubPanel SubPanel;

	int MostRecentlySelectedItem;
	bool[] ScrollbarExternal = new bool[2];

	public TreeView(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		ScrollbarExternal[0] = false;
		ScrollbarExternal[1] = false;

		RowHeight = 20;
		RootNode = null;
		ImageList = null;
		SortFunc = null;

		SubPanel = new(this, null);
		SubPanel.SetVisible(true);
		SubPanel.SetPos(0, 0);

		HorzScrollBar = new(this, "HorizScrollBar", false);
		HorzScrollBar.AddActionSignalTarget(this);
		HorzScrollBar.SetVisible(false);

		VertScrollBar = new(this, "VertScrollBar", true);
		VertScrollBar.SetVisible(false);
		VertScrollBar.AddActionSignalTarget(this);

		AllowLabelEditing = false;
		DragEnabledItems = false;
		DeleteImageListWhenDone = false;
		LabelBeingEdited = false;
		MultipleItemDragging = false;
		LeftClickExpandsTree = true;
		AllowMultipleSelections = false;
		MostRecentlySelectedItem = -1;
	}

	private void SetSortFunc(TreeViewSortFunc_t func) => SortFunc = func;
	public IFont GetFont() => Font;

	public void SetFont(IFont font) {
		if (font == null) {
			Assert(false);
			return;
		}

		Font = font;
		RowHeight = Surface.GetFontTall(Font) + 2;

		if (RootNode != null)
			RootNode.SetFont(font);

		InvalidateLayout();
	}

	public int GetRowHeight() => RowHeight;

	public int GetVisibleMaxWidth() {
		if (RootNode != null)
			return RootNode.GetVisibleMaxWidth();
		return 0;
	}

	public int AddItem(KeyValues data, int parentIndex) {
		Assert(parentIndex == -1 || (parentIndex >= 0 && parentIndex < NodeList.Count));

		TreeNode treeNode = new(SubPanel, this);
		treeNode.SetDragEnabled(DragEnabledItems);
		treeNode.ItemIndex = NodeList.Count;
		NodeList.Add(treeNode);
		treeNode.SetKeyValues(data);

		if (Font != null)
			treeNode.SetFont(Font);
		treeNode.SetBgColor(GetBgColor());

		if (data.GetInt("droppable", 0) != 0) {
			float? contextDelay = data.GetFloat("drophoverdelay");
			if (contextDelay != null)
				treeNode.SetDropEnabled(true, contextDelay.Value);
			else
				treeNode.SetDropEnabled(true);
		}

		if (parentIndex == -1) {
			Assert(RootNode == null);
			RootNode = treeNode;
			treeNode.ParentIndex = -1;
		}
		else {
			treeNode.ParentIndex = parentIndex;
			treeNode.GetParentNode()!.AddChild(treeNode);
		}

		treeNode.MakeReadyForUse();

		return treeNode.ItemIndex;
	}

	public int GetRootItemIndex() {
		if (RootNode != null)
			return RootNode.ItemIndex;
		return -1;
	}

	public int GetNumChildren(int itemIndex) {
		if (itemIndex == -1)
			return 0;

		return NodeList[itemIndex].GetChildrenCount();
	}

	public int GetChild(int parentIndex, int childIndex) => NodeList[parentIndex].Children[childIndex].ItemIndex;

	public TreeNode? GetItem(int itemIndex) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count) {
			Assert(false);
			return null;
		}

		return NodeList[itemIndex];
	}

	public int GetItemCount() => NodeList.Count;

	public KeyValues? GetItemData(int itemIndex) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return null;
		return NodeList[itemIndex].Data;
	}

	public void RemoveItem(int itemIndex, bool promoteChildren, bool fullDelete = false) {
		if (itemIndex < 0) {
			itemIndex = -itemIndex;
			fullDelete = true;
		}

		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return;

		TreeNode node = NodeList[itemIndex];
		TreeNode? parent = node.GetParentNode();

		if (promoteChildren && parent != null) {
			for (int i = 0; i < node.GetChildrenCount(); i++) {
				TreeNode child = node.Children[i];
				child.ParentIndex = parent.ItemIndex;
			}
		}
		else {
			if (fullDelete)
				while (node.GetChildrenCount() > 0)
					RemoveItem(-node.Children[0].ItemIndex, false, true);
			else {
				for (int i = 0; i < node.GetChildrenCount(); i++) {
					TreeNode child = node.Children[i];
					RemoveItem(child.ItemIndex, false);
				}
			}
		}

		parent?.Children.Remove(node);
		NodeList.RemoveAt(itemIndex);

		SelectedItems.Remove(node);

		// We must update indexes after each removal, as their index in List<> will change!
		for (int i = itemIndex; i < NodeList.Count; i++) {
			TreeNode n = NodeList[i];
			n.ItemIndex = i;

			if (n.ParentIndex > itemIndex)
				n.ParentIndex--;
			else if (n.ParentIndex == itemIndex)
				n.ParentIndex = -1;
		}

		if (fullDelete)
			node.Delete();
		else
			node.MarkForDeletion();
	}

	public void RemoveAll() {
		for (int i = 0; i < NodeList.Count; i++)
			NodeList[i].MarkForDeletion();
		NodeList.Clear();
		RootNode = null;
		ClearSelection();
	}

	public bool ModifyItem(int itemIndex, KeyValues data) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return false;

		TreeNode node = NodeList[itemIndex];
		TreeNode? parent = node.GetParentNode();
		bool resort = SortFunc != null && parent != null;
		int childIndex = -1;
		if (resort)
			childIndex = parent!.FindChild(node);
		node.SetKeyValues(data);

		if (resort) {
			int children = parent!.GetChildrenCount();
			bool leftBad = childIndex > 0 && SortFunc!(node.Data, parent.Children[childIndex - 1].Data);
			bool rightBad = childIndex < children - 1 && SortFunc!(parent.Children[childIndex + 1].Data, node.Data);

			if (leftBad || rightBad) {
				parent.Children.RemoveAt(childIndex);
				parent.AddChild(node);
			}
		}

		InvalidateLayout();

		return true;
	}

	public void SetItemSelectionTextColor(int itemIndex, Color clr) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count) {
			Assert(false);
			return;
		}

		NodeList[itemIndex].SetSelectionTextColor(clr);
	}

	public void SetitemSelectionBgColor(int itemIndex, Color clr) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count) {
			Assert(false);
			return;
		}

		NodeList[itemIndex].SetSelectionBgColor(clr);
	}

	public void SetItemSelectionUnfocusedBgColor(int itemIndex, Color clr) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count) {
			Assert(false);
			return;
		}

		NodeList[itemIndex].SetSelectionUnfocusedBgColor(clr);
	}

	public void SetItemBgColor(int itemIndex, Color clr) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count) {
			Assert(false);
			return;
		}

		NodeList[itemIndex].SetBgColor(clr);
	}

	public void SetItemFgColor(int itemIndex, Color clr) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count) {
			Assert(false);
			return;
		}

		NodeList[itemIndex].SetFgColor(clr);
	}

	public int GetItemParent(int itemIndex) => NodeList[itemIndex].ParentIndex;

	public void SetImageList(ImageList list, bool deleteWhenDone) {
		ImageList = list;
		DeleteImageListWhenDone = deleteWhenDone;
	}

	public IImage? GetImage(int index) => ImageList.GetImage(index);

	public void GetSelectedItems(ref List<int> itemIndices) {
		itemIndices.Clear();
		foreach (var item in SelectedItems)
			itemIndices.Add(item.ItemIndex);
	}

	public void GetSelectedItemData(ref List<KeyValues> itemData) {
		itemData.Clear();
		foreach (var item in SelectedItems)
			itemData.Add(item.Data);
	}

	public bool IsItemIDValid(int itemIndex) => itemIndex >= 0 && itemIndex < NodeList.Count;
	public int GetHighestItemID() => NodeList.Count - 1;

	public void ExpandItem(int itemIndex, bool expand) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return;

		NodeList[itemIndex].SetNodeExpanded(expand);
		InvalidateLayout();
	}

	public bool IsItemExpanded(int itemIndex) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return false;

		return NodeList[itemIndex].IsExpanded();
	}

	public override void OnMouseWheeled(int delta) {
		if (!VertScrollBar.IsVisible())
			return;

		int val = VertScrollBar.GetValue();
		val -= delta * 3;
		VertScrollBar.SetValue(val);
	}

	public override void OnSizeChanged(int newWide, int newTall) {
		base.OnSizeChanged(newWide, newTall);
		InvalidateLayout();
		Repaint();
	}

	public void GetScrollBarSize(bool vertical, out int wide, out int tall) {
		int idx = vertical ? 0 : 1;
		if (ScrollbarExternal[idx]) {
			wide = 0;
			tall = 0;
			return;
		}

		if (vertical)
			VertScrollBar.GetSize(out wide, out tall);
		else
			HorzScrollBar.GetSize(out wide, out tall);
	}

	public override void PerformLayout() {
		GetSize(out int wide, out int tall);

		if (RootNode == null) {
			SubPanel.SetSize(wide, tall);
			return;
		}

		GetScrollBarSize(false, out _, out int sbhh);
		GetScrollBarSize(true, out int sbvw, out _);

		int nodesVisible = tall / RowHeight;
		int visibleItemCount = RootNode.CountVisibleNodes();
		int maxWidth = RootNode.GetVisibleMaxWidth() + 10;

		bool hbarNeeded = false;
		bool vbarNeeded = visibleItemCount > nodesVisible;

		if (!vbarNeeded) {
			if (maxWidth > wide) {
				hbarNeeded = true;
				nodesVisible = (tall - sbhh) / RowHeight;
				vbarNeeded = visibleItemCount > nodesVisible;
			}
		}
		else {
			hbarNeeded = maxWidth > (wide - (sbvw + 2));
			if (hbarNeeded)
				nodesVisible = (tall - sbhh) / RowHeight;
		}

		int subPanelWidth = wide;
		int subPanelTall = tall;

		int vbarPos = 0;
		if (vbarNeeded) {
			subPanelWidth -= sbvw + 2;
			int barSize = tall;
			if (hbarNeeded)
				barSize -= sbhh;

			VertScrollBar.SetVisible(true);
			VertScrollBar.SetEnabled(false);
			VertScrollBar.SetRangeWindow(nodesVisible);
			VertScrollBar.SetRange(0, visibleItemCount);
			VertScrollBar.SetButtonPressedScrollValue(1);

			if (!ScrollbarExternal[0]) {
				VertScrollBar.SetPos(wide - (sbvw + WINDOW_BORDER_WIDTH), 0);
				VertScrollBar.SetSize(sbvw, barSize - 2);
			}

			vbarPos = VertScrollBar.GetValue();
		}
		else {
			VertScrollBar.SetVisible(false);
			VertScrollBar.SetValue(0);
		}

		int hbarPos = 0;
		if (hbarNeeded) {
			subPanelTall -= sbhh + 2;
			int barSize = wide;
			if (vbarNeeded)
				barSize -= sbvw;

			HorzScrollBar.SetVisible(true);
			HorzScrollBar.SetEnabled(false);
			HorzScrollBar.SetRangeWindow(barSize);
			HorzScrollBar.SetRange(0, maxWidth);
			HorzScrollBar.SetButtonPressedScrollValue(10);

			if (!ScrollbarExternal[1]) {
				HorzScrollBar.SetPos(0, tall - (sbhh + WINDOW_BORDER_WIDTH));
				HorzScrollBar.SetSize(barSize - 2, sbhh);
			}

			hbarPos = HorzScrollBar.GetValue();
		}
		else {
			HorzScrollBar.SetVisible(false);
			HorzScrollBar.SetValue(0);
		}

		SubPanel.SetSize(subPanelWidth, subPanelTall);

		int y = 0;
		RootNode.PositionAndSetVisibleNodes(ref vbarPos, ref nodesVisible, -hbarPos, ref y);

		Repaint();
	}

	public void MakeItemVisible(int itemIndex) {
		TreeNode node = NodeList[itemIndex];
		TreeNode? parent = node.GetParentNode();

		while (parent != null) {
			if (!parent.Expand)
				parent.SetNodeExpanded(true);
			parent = parent.GetParentNode();
		}

		PerformLayout();

		if (!VertScrollBar.IsVisible())
			return;

		int visibleIndex = node.CountVisibleIndex() - 1;
		int range = VertScrollBar.GetRangeWindow();
		int vbarPos = VertScrollBar.GetValue();

		if (visibleIndex < vbarPos)
			VertScrollBar.SetValue(visibleIndex);
		else if (visibleIndex >= vbarPos + range)
			VertScrollBar.SetValue(visibleIndex - range + 1);

		InvalidateLayout();
	}

	public void GetVBarInfo(out int top, out int itemsVisible, out bool hbarVisible) {
		GetSize(out _, out int tall);
		itemsVisible = tall / RowHeight;

		if (VertScrollBar.IsVisible())
			top = VertScrollBar.GetValue();
		else
			top = 0;
		hbarVisible = HorzScrollBar.IsVisible();
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetBorder(scheme.GetBorder("ButtonDepressedBorder"));
		SetBgColor(GetSchemeColor("TreeView.BgColor", GetSchemeColor("WindowDisabledBgColor", scheme), scheme));
		SetFont(scheme.GetFont("Default", IsProportional())!);
		SubPanel.SetBgColor(GetBgColor());
	}

	public override void SetBgColor(in Color color) {
		base.SetBgColor(color);
		SubPanel.SetBgColor(color);
	}

	private void OnSliderMoved(int position) {
		InvalidateLayout();
		Repaint();
	}

	public void GenerateDragDataForItem(int itemIndex, KeyValues msg) { } // Implemented by subclassed TreeView
	public void SetDragEnabledItems(bool state) => DragEnabledItems = state;
	public void OnLabelChanged(int intemIndex, ReadOnlySpan<char> oldString, ReadOnlySpan<char> newString) { }
	public bool IsLabelEditingAllowed() => AllowLabelEditing;
	public void SetLabelBeingEdited(bool state) => LabelBeingEdited = state;
	public bool IsLabelBeingEdited() => LabelBeingEdited;
	public void SetAllowLabelEditing(bool state) => AllowLabelEditing = state;
	public void EnableExpandTreeOnLeftClick(bool state) => LeftClickExpandsTree = state;

	public int FindItemUnderMouse(int mx, int my) {
		mx = Math.Clamp(mx, 0, GetWide() - 1);
		my = Math.Clamp(my, 0, GetTall() - 1);

		if (mx >= TREE_INDENT_AMOUNT) {
			int vbarPos = VertScrollBar.IsVisible() ? VertScrollBar.GetValue() : 0;
			int hbarPos = HorzScrollBar.IsVisible() ? HorzScrollBar.GetValue() : 0;
			int count = RootNode!.CountVisibleNodes();

			int y = 0;
			TreeNode? item = RootNode.FindItemUnderMouse(ref vbarPos, ref count, -hbarPos, y, mx, my);
			if (item != null)
				return item.ItemIndex;
		}

		return -1;
	}

	public override void OnMousePressed(ButtonCode code) {
		if (code == ButtonCode.MouseLeft && RootNode != null) {
			bool ctrl = Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl);
			bool shift = Input.IsKeyDown(ButtonCode.KeyLShift) || Input.IsKeyDown(ButtonCode.KeyRShift);

			Input.GetCursorPos(out int mx, out int my);
			ScreenToLocal(ref mx, ref my);

			if (mx >= TREE_INDENT_AMOUNT) {
				int vbarPos = VertScrollBar.IsVisible() ? VertScrollBar.GetValue() : 0;
				int hbarPos = HorzScrollBar.IsVisible() ? HorzScrollBar.GetValue() : 0;
				int count = RootNode.CountVisibleNodes();

				int y = 0;
				TreeNode? item = RootNode.FindItemUnderMouse(ref vbarPos, ref count, -hbarPos, y, mx, my);
				if (item != null) {
					if (!item.IsSelected())
						AddSelectedItem(item.ItemIndex, !ctrl && !shift);
					return;
				}
				else
					ClearSelection();
			}
		}

		base.OnMousePressed(code);
	}

	public void SetAllowMultipleSelections(bool state) => AllowMultipleSelections = state;
	public bool IsMultipleSelectionAllowed() => AllowMultipleSelections;
	public int GetSelectedItemCount() => SelectedItems.Count;

	static readonly KeyValues KV_TreeViewItemSelectionCleared = new("TreeViewItemSelectionCleared");
	public void ClearSelection() {
		SelectedItems.Clear();
		MostRecentlySelectedItem = -1;
		PostActionSignal(KV_TreeViewItemSelectionCleared);
	}

	public void RangeSelectItems(int endItem) {
		int startItem = MostRecentlySelectedItem;
		ClearSelection();
		MostRecentlySelectedItem = startItem;

		if (startItem < 0 || startItem >= NodeList.Count) {
			AddSelectedItem(endItem, false);
			return;
		}

		Assert(endItem >= 0 && endItem < NodeList.Count);

		if (RootNode == null)
			return;

		List<TreeNode> list = [];
		RootNode.FindNodesInRange(ref list, startItem, endItem);

		foreach (var node in list)
			AddSelectedItem(node.ItemIndex, false);
	}

	public void FindNodesInRange(int startIndex, int endIndex, ref List<int> itemIndices) {
		List<TreeNode> nodes = new List<TreeNode>();
		RootNode!.FindNodesInRange(ref nodes, startIndex, endIndex);

		foreach (var node in nodes)
			itemIndices.Add(node.ItemIndex);
	}

	public void RemoveSelectedItem(int itemIndex) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return;

		TreeNode sel = NodeList[itemIndex];
		Assert(sel != null);

		int slot = SelectedItems.IndexOf(sel);
		if (slot != -1) {
			SelectedItems.RemoveAt(slot);
			PostActionSignal(new KeyValues("TreeViewItemDeselected", "itemIndex", itemIndex));
			MostRecentlySelectedItem = itemIndex;
		}
	}

	public void AddSelectedItem(int itemIndex, bool clearCurrentSelection, bool requestFocus = true, bool makeItemVisible = true) {
		if (clearCurrentSelection)
			ClearSelection();

		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return;

		TreeNode sel = NodeList[itemIndex];
		Assert(sel != null);

		if (requestFocus)
			sel.RequestFocus();

		int slot = SelectedItems.IndexOf(sel);
		if (slot == -1)
			SelectedItems.Add(sel);
		else if (slot != 0) {
			SelectedItems.RemoveAt(slot);
			SelectedItems.Add(sel);
		}

		if (makeItemVisible)
			MakeItemVisible(itemIndex);

		PostActionSignal(new KeyValues("TreeViewItemSelected", "itemIndex", itemIndex));
		InvalidateLayout();

		if (clearCurrentSelection)
			MostRecentlySelectedItem = itemIndex;
	}

	public int GetFirstSelectedItem() {
		if (SelectedItems.Count <= 0)
			return -1;

		return SelectedItems[0].ItemIndex;
	}

	public bool IsItemSelected(int itemIndex) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return false;

		return SelectedItems.Contains(NodeList[itemIndex]);
	}

	public void SetLabelEditingAllowed(int itemIndex, bool allowed) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return;

		NodeList[itemIndex].SetLabelEditingAllowed(allowed);
	}

	public void StartEditingLabel(int itemIndex) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return;

		Assert(IsLabelEditingAllowed());

		TreeNode node = NodeList[itemIndex];
		Assert(node.IsLabelEditingAllowed());
		if (!node.IsLabelEditingAllowed())
			return;

		node.EditLabel();
	}

	public int GetPrevChildItemIndex(int itemIndex) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return -1;

		return NodeList[itemIndex].GetPrevChildItemIndex(NodeList[itemIndex]);
	}

	public int GetNextChildItemIndex(int itemIndex) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return -1;

		return NodeList[itemIndex].GetNextChildItemIndex(NodeList[itemIndex]);
	}

	public bool IsItemDroppable(int itemIndex, List<KeyValues> msglist) => false;
	public void OnItemDropped(int itemIndex, List<KeyValues> msglist) { }
	public bool GetItemDropContextMenu(int itemIndex, Menu menu, List<KeyValues> msglist) => false;
	public CursorCode GetItemDropCursor(int itemIndex, List<KeyValues> msglist) => CursorCode.Arrow;

	public void RemoveChildrenOfNode(int itemIndex) {
		if (itemIndex < 0 || itemIndex >= NodeList.Count)
			return;

		NodeList[itemIndex].RemoveChildren();
	}

	public ScrollBar SetScrollBarExternal(bool vertical, Panel newParent) {
		if (vertical) {
			ScrollbarExternal[0] = true;
			VertScrollBar.SetParent(newParent);
			return VertScrollBar;
		}

		ScrollbarExternal[1] = true;
		HorzScrollBar.SetParent(newParent);
		return HorzScrollBar;
	}

	public void SetMultipleItemDragEnabled(bool state) => MultipleItemDragging = state;
	public bool IsMultipleItemDragEnabled() => MultipleItemDragging;

	public void SelectAll() {
		SelectedItems.Clear();
		foreach (var node in NodeList)
			SelectedItems.Add(node);

		PostActionSignal(new KeyValues("TreeViewItemSelected", "itemIndex", GetRootItemIndex()));
		InvalidateLayout();
	}

	virtual public void GenerateChildrenOfNode(int itemIndex) { }
	virtual public void GenerateContextMenu(int itemIndex, int x, int y) { }

	public override void OnMessage(KeyValues message, IPanel? sender) {
		if (message.Name == "SliderMoved") {
			OnSliderMoved(message.GetInt("position", 0));
			return;
		}

		base.OnMessage(message, sender);
	}
}
