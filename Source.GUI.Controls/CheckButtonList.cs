using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;

namespace Source.GUI.Controls;

class CheckButtonList : EditablePanel
{
	struct CheckItem
	{
		public CheckButton CheckButton;
		public KeyValues UserData;
	}

	readonly List<CheckItem> CheckItems = [];
	ScrollBar ScrollBar;

	public CheckButtonList(Panel? parent, ReadOnlySpan<char> panelName) : base(parent, panelName) => ScrollBar = new(this, null, true);

	// FIXME #37
	public override void Dispose() {
		base.Dispose();
		RemoveAll();
	}

	int AddItem(ReadOnlySpan<char> itemText, bool startsSelected, KeyValues userData) {
		CheckItem checkItem = new() {
			CheckButton = new CheckButton(this, null, itemText),
			UserData = userData
		};
		checkItem.CheckButton.SetSilentMode(true);
		checkItem.CheckButton.SetSelected(startsSelected);
		checkItem.CheckButton.SetSilentMode(false);
		checkItem.CheckButton.AddActionSignalTarget(this);
		InvalidateLayout();
		CheckItems.Add(checkItem);
		return CheckItems.Count - 1;
	}

	void RemoveAll() {
		for (int i = 0; i < CheckItems.Count; i++)
			CheckItems[i].CheckButton.MarkForDeletion();

		CheckItems.Clear();
	}

	public int GetCheckedItemCount() {
		int count = 0;
		for (int i = 0; i < CheckItems.Count; i++) {
			if (CheckItems[i].CheckButton.IsSelected())
				count++;
		}
		return count;
	}

	public override void PerformLayout() {
		base.PerformLayout();

		int x = 4, y = 4, wide = GetWide() - ((x * 2) + ScrollBar.GetWide()), tall = 22;
		int totalHeight = y + (CheckItems.Count * tall);
		if (totalHeight > GetTall()) {
			ScrollBar.SetRange(0, totalHeight + 1);
			ScrollBar.SetRangeWindow(GetTall());
			ScrollBar.SetVisible(true);
			ScrollBar.SetBounds(GetWide() - 21, 0, 19, GetTall() - 2);
			SetPaintBorderEnabled(true);
			y -= ScrollBar.GetValue();
		}
		else {
			ScrollBar.SetVisible(false);
			SetPaintBorderEnabled(false);
		}

		for (int i = 0; i < CheckItems.Count; i++) {
			CheckItems[i].CheckButton.SetBounds(x, y, wide, tall);
			y += tall;
		}
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetBorder(scheme.GetBorder("ButtonDepressedBorder"));
	}

	public bool IsItemIDValid(int itemID) => itemID >= 0 && itemID < CheckItems.Count;
	public int GetHighestItemID() => CheckItems.Count - 1;
	public KeyValues GetItemData(int itemID) => CheckItems[itemID].UserData;
	public int GetItemCount() => CheckItems.Count;
	public bool IsItemChecked(int itemID) => CheckItems[itemID].CheckButton.IsSelected();
	public void SetItemCheckable(int itemID, bool state) => CheckItems[itemID].CheckButton.SetCheckButtonCheckable(state);

	void OnCheckButtonChecked(KeyValues _params) {
		Panel panel = (Panel)_params.GetPtr("panel")!;
		for (int i = 0; i < CheckItems.Count; i++) {
			if (CheckItems[i].CheckButton == panel) {
				KeyValues kv = new("CheckButtonChecked", "itemid", i);
				kv.SetInt("state", _params.GetInt("state"));
				PostActionSignal(kv);
				break;
			}
		}
	}

	void OnScrollBarSliderMoved() {
		InvalidateLayout();
		Repaint();
	}

	public override void OnMouseWheeled(int delta) {
		int val = ScrollBar.GetValue();
		val -= delta * 15;
		ScrollBar.SetValue(val);
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		if (message.Name == "CheckButtonChecked")
			OnCheckButtonChecked(message);
		else if (message.Name == "ScrollBarSliderMoved")
			OnScrollBarSliderMoved();
		else
			base.OnMessage(message, sender);
	}
}