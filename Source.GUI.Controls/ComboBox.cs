using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

class ComboBoxButton : Button
{
	Color DisabledBgColor;

	public ComboBoxButton(Panel parent, ReadOnlySpan<char> name, ReadOnlySpan<char> text) : base(parent, name, text) {
		SetButtonActivationType(ActivationType.OnPressed);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetFont(scheme.GetFont("Marlett", IsProportional()));
		SetContentAlignment(Alignment.West);

		if (IsOSX)
			SetTextInset(-3, 0);
		else
			SetTextInset(QuickPropScale(3), 0);
		SetDefaultBorder(scheme.GetBorder("ScrollBarButtonBorder"));

		SetDefaultColor(GetSchemeColor("ComboBoxButton.ArrowColor", scheme), GetSchemeColor("ComboBoxButton.BgColor", scheme));
		SetArmedColor(GetSchemeColor("ComboBoxButton.ArmedArrowColor", scheme), GetSchemeColor("ComboBoxButton.BgColor", scheme));
		SetDepressedColor(GetSchemeColor("ComboBoxButton.ArmedArrowColor", scheme), GetSchemeColor("ComboBoxButton.BgColor", scheme));
		DisabledBgColor = GetSchemeColor("ComboBoxButton.DisabledBgColor", scheme);
	}

	public override IBorder? GetBorder(bool depressed, bool armed, bool selected, bool keyfocus) => null;

	static readonly KeyValues KV_CursorExited = new("CursorExited");
	public override void OnCursorExited() {
		CallParentFunction(KV_CursorExited);
	}

	public override Color GetButtonBgColor() {
		if (IsEnabled())
			return base.GetButtonBgColor();
		return DisabledBgColor;
	}
}

public class ComboBox : TextEntry
{
	public static Panel Create_ComboBox() => new ComboBox(null, null, 5, true);

	Menu DropDown;
	ComboBoxButton Button;
	bool PreventTextChangeMessage;
	bool Highlight;
	MenuDirection Direction;
	int OpenOffsetY;
	char[] BorderOverride = new char[64];

	public ComboBox(Panel? parent, ReadOnlySpan<char> name, int numLines, bool allowEdit) : base(parent, name) {
		SetEditable(allowEdit);
		SetHorizontalScrolling(false);

		DropDown = new Menu(this, null);
		DropDown.AddActionSignalTarget(this);
		DropDown.SetTypeAheadMode(MenuTypeAheadMode.TYPE_AHEAD_MODE);

		Button = new ComboBoxButton(this, "Button", "u");
		Button.SetCommand("ButtonClicked");
		Button.AddActionSignalTarget(this);

		SetNumberOfEditLines(numLines);

		Highlight = false;
		Direction = MenuDirection.DOWN;
		OpenOffsetY = 0;
		PreventTextChangeMessage = false;
		BorderOverride[0] = '\0';
	}
	// FIXME #37
	public override void Dispose() {
		base.Dispose();
		DropDown?.DeletePanel();
		Button?.DeletePanel();
	}
	public void SetNumberOfEditLines(int numLines) => DropDown.SetNumberOfVisibleItems(numLines);

	public virtual int AddItem(ReadOnlySpan<char> itemText, KeyValues? userData) {
		return DropDown.AddMenuItem(itemText, new KeyValues("SetText", "text", itemText), this, userData);
	}

	public void DeleteItem(int itemID) {
		if (!DropDown.IsValidMenuID(itemID))
			return;

		DropDown.DeleteItem(itemID);
	}

	public bool UpdateItem(int itemID, ReadOnlySpan<char> itemText, KeyValues? userData) {
		if (!DropDown.IsValidMenuID(itemID))
			return false;

		KeyValues kv = new("SetText");
		kv.SetString("text", itemText);
		DropDown.UpdateMenuItem(itemID, itemText, kv, userData);
		InvalidateLayout();
		return true;
	}

	public bool IsItemIDValid(int itemID) => DropDown.IsValidMenuID(itemID);

	public void SetItemEnabled(ReadOnlySpan<char> itemText, bool state) => DropDown.SetItemEnabled(itemText, state);

	public void SetItemEnabled(int itemID, bool state) => DropDown.SetItemEnabled(itemID, state);

	public void RemoveAll() => DropDown.DeleteAllItems();

	public int GetItemCount() => DropDown.GetItemCount();

	public int GetItemIDFromRow(int row) => DropDown.GetMenuID(row);

	public virtual void ActivateItem(int itemID) => DropDown.ActivateItem(itemID);

	public void ActivateItemByRow(int row) => DropDown.ActivateItemByRow(row);

	public void SilentActivateItemByRow(int row) {
		int itemID = GetItemIDFromRow(row);
		if (itemID >= 0)
			SilentActivateItem(itemID);
	}

	public void SilentActivateItem(int itemID) {
		DropDown.SilentActivateItem(itemID);

		Span<char> name = stackalloc char[256];
		GetItemText(itemID, name);

		PreventTextChangeMessage = true;
		OnSetText(name);
		PreventTextChangeMessage = false;
	}

	public void SetMenu(Menu menu) {
		DropDown?.MarkForDeletion();

		DropDown = menu;
		DropDown?.SetParent(this);
	}

	public override void PerformLayout() {
		GetPaintSize(out int wide, out int tall);
		base.PerformLayout();

		IFont buttonFont = Button.GetFont()!;

		int fontTall = Surface.GetFontTall(buttonFont);
		int buttonSize = Math.Min(tall, fontTall);
		int buttonY = (tall - 1 - buttonSize) / 2;

		Button.GetContentSize(out int buttonWide, out _);
		buttonWide = Math.Max(buttonSize, buttonWide);

		Button.SetBounds(wide - buttonWide, buttonY, buttonWide, buttonSize);

		if (IsEditable())
			SetCursor(CursorCode.IBeam);
		else
			SetCursor(CursorCode.Arrow);

		Button.SetEnabled(IsEnabled());

		DoMenuLayout();
	}

	public void DoMenuLayout() {
		DropDown.PositionRelativeToPanel(this, Direction, OpenOffsetY);
		DropDown.SetFixedWidth(GetWide());
		DropDown.ForceCalculateWidth();
	}

	public void SortItems() { }

	public int GetActiveItem() => DropDown.GetActiveItem();

	public KeyValues? GetActiveItemUserData() => DropDown.GetItemUserData(GetActiveItem());

	public KeyValues? GetItemUserData(int itemID) => DropDown.GetItemUserData(itemID);

	public void GetItemText(int itemID, Span<char> text) => DropDown.GetItemText(itemID, text);

	public bool IsDropdownVisible() => DropDown.IsVisible();

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetBorder(scheme.GetBorder(BorderOverride[0] != '\0' ? BorderOverride : "ComboBoxBorder"));
	}

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);

		ReadOnlySpan<char> border = resourceData.GetString("border_override", "");
		if (border.Length > 0)
			border.CopyTo(BorderOverride);

		KeyValues KVButton = resourceData.FindKey("Button")!;
		if (KVButton != null && Button != null)
			Button.ApplySettings(KVButton);
	}

	public void SetDropdownButtonVisible(bool state) =>
		Button.SetVisible(state);

	public override void OnMousePressed(ButtonCode code) {
		if (DropDown == null || !IsEnabled())
			return;

		if (!IsCursorOver()) {
			HideMenu();
			return;
		}

		if (IsEditable()) {
			base.OnMousePressed(code);
			HideMenu();
		}
		else {
			RequestFocus();
			DoClick();
		}
	}

	public override void OnMouseDoublePressed(ButtonCode code) {
		if (IsEditable())
			base.OnMouseDoublePressed(code);
		else
			OnMousePressed(code);
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		if (string.Equals(new(command), "ButtonClicked", StringComparison.OrdinalIgnoreCase))
			DoClick();

		base.OnCommand(command);
	}

	public override void OnSetText(ReadOnlySpan<char> text) {
		if (!text.IsEmpty && text[0] == '#') {
			ulong unlocalizedTextSymbol = Localize.FindIndex(text[1..]);
			if (unlocalizedTextSymbol != 0 && unlocalizedTextSymbol != ulong.MaxValue)
				text = Localize.GetValueByIndex(unlocalizedTextSymbol);
		}

		Span<char> buf = stackalloc char[255];
		GetText(buf);

		if (!streq(buf, text)) {
			SetText(text);

			if (!PreventTextChangeMessage)
				PostActionSignal(new KeyValues("TextChanged", "text", text));

			Repaint();
		}

		HideMenu();
	}

	public void HideMenu() {
		if (DropDown == null)
			return;

		DropDown.SetVisible(false);
		Repaint();
		OnHideMenu();
	}

	public void ShowMenu() {
		if (DropDown == null)
			return;

		DropDown.SetVisible(false);
		DoClick();
	}

	public override void OnKillFocus(Panel? newPanel) {
		SelectNoText();
	}

	public void OnMenuClose() {
		HideMenu();

		if (HasFocus())
			SelectAllText(false);
		else if (Highlight) {
			Highlight = false;
			RequestFocus();
		}
		else if (IsCursorOver()) {
			SelectAllText(false);
			OnCursorEntered();
			RequestFocus();
		}
		else
			Button.SetArmed(false);
	}

	public void DoClick() {
		if (DropDown.IsVisible()) {
			HideMenu();
			return;
		}

		if (!DropDown.IsEnabled())
			return;

		DropDown.PerformLayout();

		int itemToSelect = -1;
		Span<char> comboBoxContents = stackalloc char[255];
		GetText(comboBoxContents);

		Span<char> menuItemName = stackalloc char[255];
		for (int i = 0; i < DropDown.GetItemCount(); i++) {
			menuItemName.Clear();
			int menuID = DropDown.GetMenuID(i);
			DropDown.GetMenuItem(menuID)!.GetText(menuItemName);
			if (streq(comboBoxContents, menuItemName)) {
				itemToSelect = i;
				break;
			}
		}

		if (itemToSelect >= 0)
			DropDown.SetCurrentlyHighlightedItem(itemToSelect);

		DoMenuLayout();
		MoveToFront();

		Color c = DropDown.GetBgColor();
		c[3] = 255;
		DropDown.SetBgColor(c);

		OnShowMenu(DropDown);

		DropDown.SetVisible(true);
		DropDown.RequestFocus();
		SelectNoText();
		Button.SetArmed(true);
		Repaint();
	}

	public override void OnCursorEntered() {
		Button.OnCursorEntered();
		base.OnCursorEntered();
	}

	public override void OnCursorExited() {
		if (DropDown.IsVisible()) {
			Button.SetArmed(false);
			base.OnCursorExited();
		}
	}

	public void OnMenuItemSelected() {
		Highlight = true;

		int idx = GetActiveItem();
		if (idx >= 0) {
			Span<char> name = stackalloc char[256];
			GetItemText(idx, name);
			OnSetText(name);
		}

		Repaint();
	}

	public override void OnSizeChanged(int newWide, int newTall) {
		base.OnSizeChanged(newWide, newTall);

		if (Button == null)
			return;

		PerformLayout();
		Button.GetSize(out int bwide, out _);
		SetDrawWidth(newWide - bwide);
	}

	public override void OnSetFocus() {
		base.OnSetFocus();
		GotoTextEnd();
		SelectAllText(false);
	}

	public override void OnKeyCodePressed(ButtonCode code) {

	}

	public override void OnKeyCodeTyped(ButtonCode code) {

	}

	public override void OnKeyTyped(char ch) {

	}

	public void SelectMenuItem(int itemToSelect) {
		if (itemToSelect >= 0 && itemToSelect < DropDown.GetItemCount()) {
			Span<char> menuItemName = stackalloc char[255];

			int menuID = DropDown.GetMenuID(itemToSelect);
			DropDown.GetMenuItem(menuID)!.GetText(menuItemName);
			OnSetText(menuItemName);
			SelectAllText(false);
		}
	}

	public void MoveAlongMenuItemList(int direction) {
		int itemToSelect = -1;

		Span<char> comboBoxContents = stackalloc char[255];
		GetText(comboBoxContents);

		Span<char> menuItemName = stackalloc char[255];
		for (int i = 0; i < DropDown.GetItemCount(); i++) {
			menuItemName.Clear();
			int menuID = DropDown.GetMenuID(i);
			DropDown.GetMenuItem(menuID)!.GetText(menuItemName);
			if (streq(comboBoxContents, menuItemName)) {
				itemToSelect = i;
				break;
			}
		}

		if (itemToSelect >= 0) {
			int newwItem = itemToSelect + direction;
			if (newwItem < 0)
				newwItem = 0;
			else if (newwItem >= DropDown.GetItemCount())
				newwItem = DropDown.GetItemCount() - 1;
			SelectMenuItem(newwItem);
		}
	}

	public void MoveToFirstMenuItem() => SelectMenuItem(0);

	public void MoveToLastMenuItem() => SelectMenuItem(DropDown.GetItemCount() - 1);

	public void SetOpenDirection(MenuDirection direction) => Direction = direction;

	public override void SetFont(IFont? font) {
		base.SetFont(font);
		DropDown.SetFont(font);
	}

	public override void SetUseFallbackFont(bool state, IFont Fallback) {
		base.SetUseFallbackFont(state, Fallback);
		DropDown.SetUseFallbackFont(state, Fallback);
	}

	public virtual void OnShowMenu(Menu menu) { }
	public virtual void OnHideMenu() { }

	public override void OnMessage(KeyValues message, IPanel? sender) {
		switch (message.Name) {
			case "MenuItemSelected":
				OnMenuItemSelected();
				break;
			case "SetText":
				OnSetText(message.GetString("text", ""));
				break;
			case "MenuClose":
				OnMenuClose();
				break;
			case "ActivateItem":
				ActivateItem(message.GetInt("itemID", -1));
				break;
			default:
				base.OnMessage(message, sender);
				break;
		}
	}
}
