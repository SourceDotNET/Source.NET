using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;

namespace Source.GUI.Controls;

public class PanelListPanel : EditablePanel
{
	const int DEFAULT_HEIGHT = 24;
	const int PANELBUFFER = 5;

	struct DataItem
	{
		public Panel Panel;
		public Panel? LabelPanel;
	}

	List<DataItem> DataItems = new();
	List<int> SortedItems = [];
	ScrollBar Vbar;
	Panel PanelEmbedded;
	Panel? SelectedItem;
	int FirstColumnWidth;
	int NumColumns;
	int DefaultHeight = DEFAULT_HEIGHT;
	int PanelBuffer = PANELBUFFER;
	[PanelAnimationVar("autohide_scrollbar", "0")] bool AutoHideScrollbar;

	public PanelListPanel(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetBounds(0, 0, 100, 100);

		Vbar = new(this, "PanellistPanelVScroll", true);
		Vbar.SetVisible(false);
		Vbar.AddActionSignalTarget(this);

		PanelEmbedded = new EditablePanel(this, "PanellistPanelEmbedded");
		PanelEmbedded.SetBounds(0, 0, 20, 20);
		PanelEmbedded.SetPaintBackgroundEnabled(false);
		PanelEmbedded.SetPaintBorderEnabled(false);

		FirstColumnWidth = 100;
		NumColumns = 1;

		if (IsProportional()) {
			DefaultHeight = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, DEFAULT_HEIGHT);
			PanelBuffer = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, PANELBUFFER);
		}
	}

	// FIXME #37
	public override void Dispose() {
		base.Dispose();
		DeleteAllItems();
	}

	public void SetVerticalBufferPixels(int pixels) {
		PanelBuffer = pixels;
		InvalidateLayout();
	}

	public int ComputeVPixelsNeeded() {
		int CurrentItem = 0;
		int LargestH = 0;

		int pixels = 0;
		for (int i = 0; i < SortedItems.Count; i++) {
			Panel panel = DataItems.ElementAt(SortedItems[i]).Panel;
			if (panel == null) continue;

			if (panel.IsLayoutInvalid())
				panel.InvalidateLayout(true);

			int CurrentColumn = CurrentItem % NumColumns;

			panel.GetSize(out int w, out int h);

			if (LargestH < h)
				LargestH = h;

			if (CurrentColumn == 0)
				pixels += PanelBuffer;

			if (CurrentColumn == NumColumns - 1) {
				pixels += LargestH;
				LargestH = 0;
			}

			CurrentItem++;
		}

		pixels += LargestH + PanelBuffer;

		return pixels;
	}

	public Panel? GetCellRenderer(int row) {
		if (row < 0 || row >= SortedItems.Count)
			return null;

		return DataItems.ElementAt(SortedItems[row]).Panel;
	}

	public int AddItem(Panel? labelPanel, Panel? panel) {
		Assert(panel != null);

		labelPanel?.SetParent(PanelEmbedded);
		panel.SetParent(PanelEmbedded);

		int itemId = DataItems.Count;
		DataItems.Add(new() { Panel = panel, LabelPanel = labelPanel });
		SortedItems.Add(itemId);

		InvalidateLayout();
		return itemId;
	}

	public int GetItemCount() => DataItems.Count;

	public int GetItemIDFromRow(int row) {
		if (row < 0 || row >= GetItemCount())
			return -1;

		return SortedItems[row];
	}

	public int FirstItem() => DataItems.Count > 0 ? 0 : -1;

	public int NextItem(int item) {
		if (item < 0 || item >= DataItems.Count - 1)
			return -1;

		return item + 1;
	}

	public int InvalidItemID() => -1;

	public Panel? GetItemLabel(int itemId) {
		if (itemId < 0 || itemId >= DataItems.Count)
			return null;

		return DataItems.ElementAt(itemId).LabelPanel;
	}

	public Panel? GetItemPanel(int itemId) {
		if (itemId < 0 || itemId >= DataItems.Count)
			return null;

		return DataItems.ElementAt(itemId).Panel;
	}

	public void RemoveItem(int itemId) {
		if (itemId < 0 || itemId >= DataItems.Count)
			return;

		DataItem dataItem = DataItems.ElementAt(itemId);

		dataItem.Panel?.MarkForDeletion();
		dataItem.LabelPanel?.MarkForDeletion();

		DataItems.Remove(dataItem);
		SortedItems.Remove(itemId);

		InvalidateLayout();
	}

	public void DeleteAllItems() {
		foreach (var dataItem in DataItems) {
			if (dataItem.Panel != null)
				dataItem.Panel.MarkForDeletion();
		}

		DataItems.Clear();
		SortedItems.Clear();

		InvalidateLayout();
	}

	public void RemoveAll() {
		DataItems.Clear();
		SortedItems.Clear();

		Vbar.SetValue(0);
		InvalidateLayout();
	}

	public List<int> GetSortedVector() => SortedItems;

	public override void OnSizeChanged(int newWide, int newTall) {
		base.OnSizeChanged(newWide, newTall);
		InvalidateLayout();
		Repaint();
	}

	public override void PerformLayout() {
		GetSize(out int wide, out int tall);

		int vpixels = ComputeVPixelsNeeded();

		Vbar.SetRange(0, vpixels);
		Vbar.SetRangeWindow(tall);
		Vbar.SetButtonPressedScrollValue(tall / 4);
		Vbar.SetPos(wide - Vbar.GetWide() - 2, 0);
		Vbar.SetSize(Vbar.GetWide(), tall - 2);

		int top = Vbar.GetValue();
		PanelEmbedded.SetPos(0, -top);
		PanelEmbedded.SetSize(wide - Vbar.GetWide(), vpixels);

		bool ScrollbarVisible = true;
		if (AutoHideScrollbar)
			ScrollbarVisible = PanelEmbedded.GetTall() > tall;
		Vbar.SetVisible(ScrollbarVisible);

		int y = 0;
		int h = 0;
		int totalh = 0;

		int xpos = FirstColumnWidth + PanelBuffer;
		int ColumnWidth = (wide - xpos - Vbar.GetWide() - 12) / NumColumns;

		for (int i = 0; i < SortedItems.Count; i++) {
			int CurrentColumn = i % NumColumns;

			if (CurrentColumn == 0)
				y += PanelBuffer;

			DataItem item = DataItems.ElementAt(SortedItems[i]);

			if (h < item.Panel.GetTall())
				h = item.Panel.GetTall();

			item.LabelPanel?.SetBounds(0, y, FirstColumnWidth, item.Panel.GetTall());

			item.Panel.SetBounds(xpos + CurrentColumn * ColumnWidth, y, ColumnWidth, item.Panel.GetTall());

			if (CurrentColumn >= NumColumns - 1) {
				y += h;
				totalh += h;
				h = 0;
			}
		}
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetBorder(scheme.GetBorder("ButtonDepressedBorder"));
		SetBgColor(GetSchemeColor("lISTpANEL.BgColor", GetBgColor(), scheme));
	}

	[UiMessage("ScrollBarSliderMoved")]
	public void OnSliderMoved(int position) {
		InvalidateLayout();
		Repaint();
	}

	public void MoveScrollBarToTop() => Vbar.SetValue(0);
	public void SetFirstColumnWidth(int width) => FirstColumnWidth = width;
	public int GetFirstColumnWidth() => FirstColumnWidth;
	public void SetNumColumns(int num) => NumColumns = num;
	public int GetNumColumns() => NumColumns;

	public override void OnMouseWheeled(int delta) {
		int val = Vbar.GetValue();
		val -= delta * DEFAULT_HEIGHT;
		Vbar.SetValue(val);
	}

	readonly static KeyValues KV_PanelSelected_0 = new("PanelSelected", "state", 0);
	readonly static KeyValues KV_PanelSelected_1 = new("PanelSelected", "state", 1);
	public void SetSelectedPanel(Panel panel) {
		if (panel == SelectedItem)
			return;

		if (SelectedItem != null)
			PostMessage(SelectedItem, KV_PanelSelected_0);

		if (panel != null)
			PostMessage(panel, KV_PanelSelected_1);

		SelectedItem = panel;
	}

	public Panel? GetSelectedPanel() => SelectedItem;

	public void ScrollToItem(int itemNumber) {
		if (!Vbar.IsVisible())
			return;

		DataItem item = DataItems.ElementAt(SortedItems[itemNumber]);
		if (item.Panel == null)
			return;

		item.Panel.GetPos(out int x, out int y);

		int lx = x, ly = y;
		PanelEmbedded.LocalToScreen(ref lx, ref ly);
		ScreenToLocal(ref lx, ref ly);

		int h = item.Panel.GetTall();
		if (ly >= 0 && ly + h < GetTall())
			return;

		Vbar.SetValue(y);
		InvalidateLayout();
	}
}
