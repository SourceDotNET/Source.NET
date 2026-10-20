using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public class MenuSeparator : Panel
{
	public MenuSeparator(Panel parent, ReadOnlySpan<char> panelName) : base(parent, panelName) {
		SetPaintEnabled(true);
		SetPaintBackgroundEnabled(true);
		SetPaintBorderEnabled(false);
	}

	public override void Paint() {
		GetSize(out int w, out _);

		Surface.DrawSetColor(GetFgColor());
		Surface.DrawFilledRect(4, 1, w - 1, 2);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetFgColor(scheme.GetColor("Menu.SeparatorColor", new(142, 142, 142, 255)));
		SetBgColor(scheme.GetColor("Menu.BgColor", new(0, 0, 0, 255)));
	}
}

public enum MenuDirection
{
	LEFT = 0,
	RIGHT = 1,
	UP = 2,
	DOWN = 3,
	CURSOR = 4,
	ALIGN_WITH_PARENT = 5
}

public enum MenuMode
{
	MOUSE = 0,
	KEYBOARD = 1
}

public enum MenuTypeAheadMode
{
	COMPAT_MODE = 0,
	HOT_KEY_MODE = 1,
	TYPE_AHEAD_MODE = 3
}

public class Menu : Panel
{
	Color BorderDark;

	IFont? ItemFont;
	IFont? FallbackItemFont;
	bool UseFallbackFont;

	public const int MENU_SEPARATOR_HEIGHT = 3;
	public Alignment Alignment;

	protected ScrollBar? Scroller;

	protected List<MenuItem> MenuItems = [];
	protected List<int> SortedItems = [];
	protected List<int> Separators = [];
	protected List<int> VisibleSortedItems = [];
	protected List<MenuSeparator> SeparatorPanels = [];

	protected bool RecalculateWidth = true;
	int MenuItemHeight;
	bool SizedForScrollBar;
	int FixedWidth;
	int MinimumWidth;
	int MenuWide;
	int NumVisibleLines;
	int CheckImageWidth;
	int CurrentlySelectedItemID;
	int ActivatedItem;

	public const int DEFAULT_MENU_ITEM_HEIGHT = 22;
	public const int MENU_UP = -1;
	public const int MENU_DOWN = 1;
	public const int TYPEAHEAD_BUFSIZE = 256;

	MenuMode InputMode;
	MenuTypeAheadMode TypeAheadMode;
	char[] TypeAheadBuffer;
	int NumTypeAheadChars;
	double LastTypeAheadTime;


	public override void Paint() {
		if (Scroller!.IsVisible()) {
			GetSize(out int wide, out int tall);
			Surface.DrawSetColor(BorderDark);
			Surface.DrawFilledRect(wide - Scroller!.GetWide(), -1, wide - Scroller!.GetWide(), tall);
		}
	}

	public Menu(Panel? parent, ReadOnlySpan<char> panelName) : base(parent, panelName) {
		Alignment = Alignment.West;
		FixedWidth = 0;
		MinimumWidth = 0;
		NumVisibleLines = -1;
		CurrentlySelectedItemID = -1;
		Scroller = new ScrollBar(this, "MenuScrollBar", true);
		Scroller.SetVisible(false);
		Scroller.AddActionSignalTarget(this);
		SizedForScrollBar = false;
		SetZPos(1);
		SetVisible(false);
		MakePopup(false);
		SetParent(parent);
		RecalculateWidth = true;
		// UseMenuManager = true;
		CheckImageWidth = 0;
		ActivatedItem = 0;
		UseFallbackFont = false;

		if (IsProportional())
			MenuItemHeight = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, DEFAULT_MENU_ITEM_HEIGHT);
		else
			MenuItemHeight = DEFAULT_MENU_ITEM_HEIGHT;

		TypeAheadMode = MenuTypeAheadMode.COMPAT_MODE;
		TypeAheadBuffer = new char[TYPEAHEAD_BUFSIZE];
		TypeAheadBuffer[0] = '\0';
		NumTypeAheadChars = 0;
		LastTypeAheadTime = 0.0f;
	}


	public void DeleteAllItems() {
		for (int i = 0; i < MenuItems.Count; i++)
			MenuItems[i].MarkForDeletion();

		MenuItems.Clear();
		SortedItems.Clear();
		VisibleSortedItems.Clear();
		Separators.Clear();

		int count = SeparatorPanels.Count;
		for (int i = 0; i < count; i++)
			SeparatorPanels[i].MarkForDeletion();
		SeparatorPanels.Clear();
		InvalidateLayout();
	}

	public virtual int AddMenuItem(MenuItem panel) {
		panel.SetParent(this);
		int itemID = MenuItems.Count;
		MenuItems.Add(panel);
		SortedItems.Add(itemID);
		InvalidateLayout(false);
		RecalculateWidth = true;
		panel.SetContentAlignment(Alignment);

		if (ItemFont != null)
			panel.SetFont(ItemFont);

		if (UseFallbackFont && FallbackItemFont != null) {
			Label l = panel;
			TextImage? ti = l.GetTextImage();
			ti?.SetUseFallbackFont(UseFallbackFont, FallbackItemFont);
		}

		// hotkeys?
		// if (panel.GetHotKey())
		// SetTypeAheadMode(MenuTypeAheadMode.HOT_KEY_MODE);

		return itemID;
	}

	public void DeleteItem(int itemID) {
		Assert(SeparatorPanels.Count == 0); // From Source - FIXME: This doesn't work with seperator panels yet

		MenuItems[itemID].MarkForDeletion();
		MenuItems.Remove(MenuItems[itemID]);

		SortedItems.Remove(itemID);
		VisibleSortedItems.Remove(itemID);

		InvalidateLayout(false);
		RecalculateWidth = true;
	}

	public int AddMenuItemCharCommand(MenuItem item, ReadOnlySpan<char> command, Panel target, KeyValues? userData = null) {
		item.SetCommand(command);
		item.AddActionSignalTarget(target);
		item.SetUserData(userData);
		return AddMenuItem(item);
	}

	public int AddMenuItemKeyValuesCommand(MenuItem item, KeyValues message, Panel target, KeyValues? userData = null) {
		item.SetCommand(message);
		item.AddActionSignalTarget(target);
		item.SetUserData(userData);
		return AddMenuItem(item);
	}

	public virtual int AddMenuItem(ReadOnlySpan<char> itemName, ReadOnlySpan<char> itemText, ReadOnlySpan<char> command, Panel target, KeyValues? userData = null) {
		MenuItem item = new(this, itemName, itemText);
		return AddMenuItemCharCommand(item, command, target, userData);
	}

	public virtual int AddMenuItem(ReadOnlySpan<char> itemText, ReadOnlySpan<char> command, Panel target, KeyValues? userData = null) {
		return AddMenuItem(itemText, itemText, command, target, userData);
	}

	public virtual int AddMenuItem(ReadOnlySpan<char> itemName, ReadOnlySpan<char> itemText, KeyValues message, Panel target, KeyValues? userData = null) {
		MenuItem item = new(this, itemName, itemText);
		return AddMenuItemKeyValuesCommand(item, message, target, userData);
	}

	public virtual int AddMenuItem(ReadOnlySpan<char> itemText, KeyValues message, Panel target, KeyValues? userData = null) {
		return AddMenuItem(itemText, itemText, message, target, userData);
	}

	public virtual int AddMenuItem(ReadOnlySpan<char> itemText, Panel target, KeyValues? userData = null) {
		return AddMenuItem(itemText, itemText, target, userData);
	}

	public virtual int AddMenuItem(ReadOnlySpan<char> itemText, KeyValues userData, Panel target) {
		return AddMenuItem(itemText, itemText, target, userData);
	}

	public virtual int AddCheckableMenuItem(ReadOnlySpan<char> itemName, ReadOnlySpan<char> itemtext, ReadOnlySpan<char> command, Panel target, KeyValues? userData = null) {
		MenuItem item = new(this, itemName, itemtext, null, true);
		return AddMenuItemCharCommand(item, command, target, userData);
	}

	public virtual int AddCheckableMenuItem(ReadOnlySpan<char> itemText, ReadOnlySpan<char> command, Panel target, KeyValues? userData = null) {
		return AddCheckableMenuItem(itemText, itemText, command, target, userData);
	}

	public virtual int AddCheckableMenuItem(ReadOnlySpan<char> itemName, ReadOnlySpan<char> itemText, KeyValues message, Panel target, KeyValues? userData = null) {
		MenuItem item = new(this, itemName, itemText, null, true);
		return AddMenuItemKeyValuesCommand(item, message, target, userData);
	}

	public virtual int AddCheckableMenuItem(ReadOnlySpan<char> itemText, KeyValues message, Panel target, KeyValues? userData = null) {
		return AddCheckableMenuItem(itemText, itemText, message, target, userData);
	}

	public virtual int AddCheckableMenuItem(ReadOnlySpan<char> itemText, Panel target, KeyValues? userData = null) {
		return AddCheckableMenuItem(itemText, itemText, target, userData);
	}

	public virtual int AddCascadingMenuItem(ReadOnlySpan<char> itemName, ReadOnlySpan<char> itemText, ReadOnlySpan<char> command, Panel target, Menu cascadeMenu, KeyValues? userData = null) {
		MenuItem item = new(this, itemName, itemText, cascadeMenu, false);
		return AddMenuItemCharCommand(item, command, target, userData);
	}

	public virtual int AddCascadingMenuItem(ReadOnlySpan<char> itemText, ReadOnlySpan<char> command, Panel target, Menu cascadeMenu, KeyValues? userData = null) {
		return AddCascadingMenuItem(itemText, itemText, command, target, cascadeMenu, userData);
	}

	public virtual int AddCascadingMenuItem(ReadOnlySpan<char> itemName, ReadOnlySpan<char> itemText, KeyValues message, Panel target, Menu cascadeMenu, KeyValues? userData = null) {
		MenuItem item = new(this, itemName, itemText, cascadeMenu, false);
		return AddMenuItemKeyValuesCommand(item, message, target, userData);
	}

	public virtual int AddCascadingMenuItem(ReadOnlySpan<char> itemText, KeyValues message, Panel target, Menu cascadeMenu, KeyValues? userData = null) {
		return AddCascadingMenuItem(itemText, itemText, message, target, cascadeMenu, userData);
	}

	public virtual int AddCascadingMenuItem(ReadOnlySpan<char> itemText, Panel target, Menu cascadeMenu, KeyValues? userData = null) {
		return AddCascadingMenuItem(itemText, itemText, target, cascadeMenu, userData);
	}

	public void UpdateMenuItem(int itemID, ReadOnlySpan<char> itemText, KeyValues? message, KeyValues? userData = null) {
		Assert(itemID >= 0 && itemID < MenuItems.Count);

		MenuItem item = MenuItems[itemID];
		if (item != null) {
			item.SetText(itemText);
			item.SetCommand(message);
			if (userData != null)
				item.SetUserData(userData);
		}

		RecalculateWidth = true;
	}

	public void SetNumberOfVisibleItems(int numLines) {
		NumVisibleLines = numLines;
		InvalidateLayout(false);
	}

	MenuItem? GetParentMenuItem() => GetParent() is MenuItem mi ? mi : null;

	public int GetMenuItemHeight() {
		return MenuItemHeight;
	}

	public void SetContentAlignment(Alignment alignment) {
		if (Alignment != alignment) {
			Alignment = alignment;

			foreach (var menuItem in MenuItems)
				menuItem.SetContentAlignment(alignment);
		}
	}

	public MenuItem? GetMenuItem(int itemID) {
		if (itemID < 0 || itemID >= MenuItems.Count)
			return null;

		return MenuItems[itemID];
	}

	public bool IsValidMenuID(int itemID) {
		return itemID >= 0 && itemID < MenuItems.Count;
	}

	public void SetFixedWidth(int width) {
		FixedWidth = width;
		InvalidateLayout(false);
	}

	public void SetMenuItemHeight(int itemHeight) {
		MenuItemHeight = itemHeight;
	}

	public int CountVisibleItems() {
		int count = 0;
		int len = SortedItems.Count;
		for (int i = 0; i < len; i++)
			if (MenuItems[SortedItems[i]].IsVisible())
				++count;

		return count;
	}

	public override void PerformLayout() {
		MenuItem? parent = GetParentMenuItem();
		bool cascading = parent != null;

		GetInset(out _, out _, out int itop, out int ibottom);
		ComputeWorkspaceSize(out int workWide, out int workTall);

		int fullHeightWouldRequire = ComputeFullMenuHeightWithInsets();
		bool needScrollbar = fullHeightWouldRequire >= workTall;
		int maxVisibleItems = CountVisibleItems();

		if (NumVisibleLines > 0 &&
			maxVisibleItems > NumVisibleLines) {
			needScrollbar = true;
		}

		if (needScrollbar) {
			AddScrollBar();
			MakeItemsVisibleInScrollRange(NumVisibleLines, Math.Min(fullHeightWouldRequire, workTall));
		}
		else {
			RemoveScrollBar();
			VisibleSortedItems.Clear();
			int ip;
			int c = SortedItems.Count;
			for (ip = 0; ip < c; ++ip) {
				int itemID = SortedItems[ip];
				MenuItem child = MenuItems[itemID];
				if (child == null || !child.IsVisible())
					continue;

				VisibleSortedItems.Add(itemID);
			}

			c = SeparatorPanels.Count;
			for (ip = 0; ip < c; ++ip)
				SeparatorPanels[ip]?.SetVisible(false);
		}

		// get the appropriate menu border
		LayoutMenuBorder();

		int trueW = GetWide();
		if (needScrollbar)
			trueW -= Scroller!.GetWide();

		int separatorHeight = MENU_SEPARATOR_HEIGHT;

		int menuTall = 0;
		int totalTall = itop + ibottom;
		int i;
		for (i = 0; i < VisibleSortedItems.Count; i++) {
			int itemId = VisibleSortedItems[i];

			MenuItem? child = MenuItems[itemId];
			Assert(child != null);
			if (child == null)
				continue;

			if (!child.IsVisible())
				continue;

			if (totalTall >= workTall)
				break;

			if (ItemFont != null)
				child.SetFont(ItemFont);

			child.SetPos(0, menuTall);
			child.SetTall(MenuItemHeight);
			menuTall += MenuItemHeight;
			totalTall += MenuItemHeight;

			if (!child.IsCheckable() && CheckImageWidth > 0)
				child.SetTextInset(CheckImageWidth, 0);
			else if (child.IsCheckable())
				child.SetTextInset(0, 0);

			for (int j = 0; j < Separators.Count; j++) {
				if (Separators[j] == itemId) {
					MenuSeparator? sep = SeparatorPanels[j];
					Assert(sep != null);
					sep.SetVisible(true);
					sep.SetBounds(0, menuTall, trueW, separatorHeight);
					menuTall += separatorHeight;
					totalTall += separatorHeight;
					break;
				}
			}
		}

		if (FixedWidth == 0) {
			RecalculateWidth = true;
			CalculateWidth();
		}
		else if (FixedWidth > 0) {
			MenuWide = FixedWidth;
			if (SizedForScrollBar)
				MenuWide -= Scroller!.GetWide();
		}

		SizeMenuItems();

		int extraWidth = 0;
		if (SizedForScrollBar)
			extraWidth = Scroller!.GetWide();

		int mwide = MenuWide + extraWidth;
		if (mwide > workWide)
			mwide = workWide;

		int mtall = menuTall + itop + ibottom;
		if (mtall > workTall)
			mtall = workTall;

		SetSize(mwide, mtall);

		if (cascading)
			PositionCascadingMenu();

		if (Scroller!.IsVisible())
			LayoutScrollBar();

		foreach (var menuItem in MenuItems)
			menuItem.InvalidateLayout();
		Repaint();
	}

	private void PositionCascadingMenu() {
		IPanel? parent = GetParent();
		Assert(parent != null);
		parent.GetSize(out int parentWide, out _);
		parent.GetPos(out int parentX, out _);

		parentX += parentWide;
		int parentY = 0;

		ParentLocalToScreen(ref parentX, ref parentY);

		SetPos(parentX, parentY);

		GetBounds(out int x, out int y, out int wide, out int tall);
		Surface.GetWorkspaceBounds(out int workX, out int workY, out int workWide, out int workTall);

		if (x + wide > workX + workWide) {
			x -= parentWide + wide;
			x -= 2;
		}
		else x += 1;

		if (y + tall > workY + workTall) {
			int lastWorkY = workY + workTall;
			int pixelsOffBottom = y + tall - lastWorkY;
			y -= pixelsOffBottom;
			y -= 2;
		}
		else y -= 1;

		SetPos(x, y);
		MoveToFront();
	}

	private void LayoutScrollBar() {
		Scroller!.SetEnabled(false);
		Scroller.SetRangeWindow(VisibleSortedItems.Count);
		Scroller.SetRange(0, CountVisibleItems());
		Scroller.SetButtonPressedScrollValue(1);

		GetSize(out int wide, out int tall);
		GetInset(out _, out int iright, out int itop, out int ibottom);

		wide -= iright;

		Scroller.SetPos(wide - Scroller.GetWide(), 1);
		Scroller.SetSize(Scroller.GetWide(), tall - ibottom - itop);
	}

	private void SizeMenuItems() {
		GetInset(out int left, out int right, out _, out _);

		foreach (var child in MenuItems)
			child.SetWide(MenuWide - left - right);
	}

	private void CalculateWidth() {
		if (!RecalculateWidth)
			return;

		MenuWide = 0;
		if (FixedWidth == 0) {
			foreach (var menuItem in MenuItems) {
				menuItem.GetContentSize(out int wide, out _);
				if (wide > MenuWide - Label.Content) {
					MenuWide = wide + Label.Content;
				}
			}
		}

		if (MenuWide < MinimumWidth)
			MenuWide = MinimumWidth;

		RecalculateWidth = false;
	}

	protected virtual void LayoutMenuBorder() {
		IScheme? scheme = GetScheme();
		IBorder? menuBorder = scheme?.GetBorder("MenuBorder");
		if (menuBorder != null)
			SetBorder(menuBorder);
	}

	static readonly KeyValues KV_MenuClose = new("MenuClose");
	public override void OnKeyCodeTyped(ButtonCode code) {
		if (!IsEnabled())
			return;

		bool alt = Input.IsKeyDown(ButtonCode.KeyLAlt) || Input.IsKeyDown(ButtonCode.KeyRAlt);
		if (alt) {
			base.OnKeyCodeTyped(code);

			if (TypeAheadMode != MenuTypeAheadMode.TYPE_AHEAD_MODE)
				PostActionSignal(KV_MenuClose);
		}

		switch (code) {
			case ButtonCode.KeyEscape:
				SetVisible(false);
				break;
			case ButtonCode.KeyUp:
				MoveAlongMenuItemList(MENU_UP, 0);
				if (MenuItems[CurrentlySelectedItemID] != null)
					MenuItems[CurrentlySelectedItemID].ArmItem();
				else
					base.OnKeyCodeTyped(code);
				break;
			case ButtonCode.KeyDown:
				MoveAlongMenuItemList(MENU_DOWN, 0);
				if (MenuItems[CurrentlySelectedItemID] != null)
					MenuItems[CurrentlySelectedItemID].ArmItem();
				else
					base.OnKeyCodeTyped(code);
				break;
			case ButtonCode.KeyRight:
				if (MenuItems[CurrentlySelectedItemID] != null)
					ActivateItem(CurrentlySelectedItemID);
				else
					base.OnKeyCodeTyped(code);
				break;
			case ButtonCode.KeyLeft:
				if (GetParentMenuItem() != null)
					SetVisible(false);
				else
					base.OnKeyCodeTyped(code);
				break;
			case ButtonCode.KeyEnter:
				if (MenuItems[CurrentlySelectedItemID] != null)
					ActivateItem(CurrentlySelectedItemID);
				else
					base.OnKeyCodeTyped(code);
				break;
			case ButtonCode.KeyPageUp:
				if (NumVisibleLines > 1) {
					if (CurrentlySelectedItemID < NumVisibleLines)
						MoveAlongMenuItemList(MENU_UP * CurrentlySelectedItemID, 0);
					else
						MoveAlongMenuItemList(MENU_UP * (NumVisibleLines - 1), 0);
				}
				else
					MoveAlongMenuItemList(MENU_UP, 0);

				MenuItems[CurrentlySelectedItemID]?.ArmItem();
				break;
			case ButtonCode.KeyPageDown:
				if (NumVisibleLines > 1) {
					if (CurrentlySelectedItemID + NumVisibleLines >= MenuItems.Count)
						MoveAlongMenuItemList(MENU_DOWN * (MenuItems.Count - CurrentlySelectedItemID - 1), 0);
					else
						MoveAlongMenuItemList(MENU_DOWN * (NumVisibleLines - 1), 0);
				}
				else
					MoveAlongMenuItemList(MENU_DOWN, 0);

				MenuItems[CurrentlySelectedItemID]?.ArmItem();
				break;
			case ButtonCode.KeyHome:
				MoveAlongMenuItemList(MENU_UP * CurrentlySelectedItemID, 0);
				MenuItems[CurrentlySelectedItemID]?.ArmItem();
				break;
			case ButtonCode.KeyEnd:
				MoveAlongMenuItemList(MENU_DOWN * (MenuItems.Count - CurrentlySelectedItemID - 1), 0);
				MenuItems[CurrentlySelectedItemID]?.ArmItem();
				break;
		}
	}

	private void MakeItemsVisibleInScrollRange(int maxVisibleItems, int numPixelsAvailable) {
		foreach (var item in MenuItems)
			item.SetBounds(0, 0, 0, 0);

		for (int i = 0; i < SeparatorPanels.Count; ++i)
			SeparatorPanels[i].SetVisible(false);

		VisibleSortedItems.Clear();

		int tall = 0;

		int startItem = Scroller!.GetValue();
		Assert(startItem >= 0);
		do {
			if (startItem >= SortedItems.Count)
				break;

			int itemId = SortedItems[startItem];

			if (!MenuItems[itemId].IsVisible()) {
				++startItem;
				continue;
			}

			int itemHeight = MenuItemHeight;
			int sepIndex = Separators.FindIndex(x => x == itemId);
			if (sepIndex != -1) {
				itemHeight += MENU_SEPARATOR_HEIGHT;
			}

			if (tall + itemHeight > numPixelsAvailable)
				break;

			// Too many items
			if (maxVisibleItems > 0) {
				if (VisibleSortedItems.Count >= maxVisibleItems)
					break;
			}

			tall += itemHeight;
			VisibleSortedItems.Add(itemId);
			++startItem;
		}
		while (true);
	}

	public void OnTypeAhead(char unichar) {
		if (MenuItems.Count <= 0)
			return;

		double CurrentTime = system.GetCurrentTime();
		if (CurrentTime - LastTypeAheadTime > 0.5f) {
			NumTypeAheadChars = 0;
			TypeAheadBuffer[0] = '\0';
		}
		LastTypeAheadTime = CurrentTime;

		if (NumTypeAheadChars + 1 < TYPEAHEAD_BUFSIZE)
			TypeAheadBuffer[NumTypeAheadChars++] = unichar;

		int itemToSelect = CurrentlySelectedItemID;
		if (itemToSelect < 0 || itemToSelect >= MenuItems.Count)
			itemToSelect = 0;

		int i = itemToSelect;
		Span<char> menuItemName = stackalloc char[255];
		do {
			menuItemName.Clear();
			MenuItems[i].GetText(menuItemName);

			if (((ReadOnlySpan<char>)menuItemName).Equals(TypeAheadBuffer.AsSpan(0, NumTypeAheadChars), StringComparison.OrdinalIgnoreCase)) {
				itemToSelect = i;
				break;
			}

			i = (i + 1) % MenuItems.Count;
		} while (i != itemToSelect);
	}

	private void ComputeWorkspaceSize(out int workWide, out int workTall) {
		GetInset(out _, out _, out int top, out int bottom);

		Surface.GetWorkspaceBounds(out _, out _, out workWide, out workTall);
		workTall -= 20;
		workTall -= top;
		workTall -= bottom;
	}

	public void PositionRelativeToPanel(Panel relative, MenuDirection direction, int additionalYOffset, bool showMenu = false) {
		Assert(relative != null);

		relative.GetBounds(out int rx, out int ry, out int rw, out int rh);
		relative.LocalToScreen(ref rx, ref ry);

		if (direction == MenuDirection.CURSOR) {
			Input.GetCursorPos(out rx, out ry);
			rw = rh = 0;
		}
		else if (direction == MenuDirection.ALIGN_WITH_PARENT && relative.GetParent() != null) {
			rx = 0; ry = 0;
			relative.ParentLocalToScreen(ref rx, ref ry);
			rx -= 1;
			ry = rh + additionalYOffset;
			rw = rh = 0;
		}
		else {
			rx = 0; ry = 0;
			relative.LocalToScreen(ref rx, ref ry);
		}

		ComputeWorkspaceSize(out int workWide, out int workTall);

		int x = 0;
		int y = 0;

		GetSize(out int menuWide, out int menuTall);

		int bottomOfReference;
		switch (direction) {
			case MenuDirection.UP:
				x = rx;
				int topOfReference = ry;
				y = topOfReference - menuTall;
				if (y < 0) {
					bottomOfReference = ry + rh + 1;
					int remainingPixels = workTall - bottomOfReference;

					if (menuTall >= remainingPixels) {
						y = workTall - menuTall;
						x = rx + rw;
						if (x + menuWide > workWide)
							x = rx - menuWide;
					}
					else
						y = bottomOfReference;
				}
				break;
			default:
				x = rx;
				bottomOfReference = ry + rh + 1;
				y = bottomOfReference;
				if (bottomOfReference + menuTall >= workTall) {
					if (menuTall > ry) {
						y = workTall - menuTall;
						x = rx = rw;
						if (x + menuWide > workWide)
							x = rx - menuWide;
					}
					else
						y = ry - menuTall;
				}
				break;
		}

		if (x + menuWide > workWide) {
			x = workWide - menuWide;
			Assert(x >= 0);
		}
		else if (x < 0)
			x = 0;

		SetPos(x, y);
		if (showMenu)
			SetVisible(true);
	}

	private int ComputeFullMenuHeightWithInsets() {
		GetInset(out _, out _, out int top, out int bottom);

		int separatorHeight = 3;

		int totalTall = top + bottom;
		int i;
		for (i = 0; i < SortedItems.Count; i++) {
			int itemId = SortedItems[i];

			MenuItem child = MenuItems[itemId];
			Assert(child != null);
			if (child == null)
				continue;

			if (!child.IsVisible())
				continue;

			totalTall += MenuItemHeight;
			for (int j = 0; j < Separators.Count; j++) {
				if (Separators[j] == itemId) {
					totalTall += separatorHeight;
					break;
				}
			}
		}

		return totalTall;
	}

	public void ForceCalculateWidth() {
		RecalculateWidth = true;
		CalculateWidth();
		PerformLayout();
	}

	public void SetTypeAheadMode(MenuTypeAheadMode mode) {
		TypeAheadMode = mode;
	}

	public int GetTypeAheadMode() {
		return (int)TypeAheadMode;
	}

	public override void OnMouseWheeled(int delta) {
		if (!Scroller!.IsVisible())
			return;

		int val = Scroller.GetValue();
		val -= delta;

		Scroller.SetValue(val);

		InvalidateLayout();
	}

	public override void OnKillFocus(Panel? newPanel) {
		if (newPanel == null || !HasParent(newPanel)) {
			if (!IsKeyboardInputEnabled() && newPanel == null)
				return;

			MenuItem item = GetParentMenuItem()!;
			if (item != null && newPanel == item.GetParent()) {
				if (InputMode == MenuMode.MOUSE) {
					MoveToFront();
					return;
				}
			}

			PostActionSignal(KV_MenuClose);
			SetVisible(false);
		}
	}

	internal static void OnInternalMousePressed(Panel other, ButtonCode code) => MenuManager.Instance.OnInternalMousePressed(other, code);

	public override void SetVisible(bool state) {
		if (state == IsVisible())
			return;

		if (state == false) {
			PostActionSignal(KV_MenuClose);
			CloseOtherMenus(null);
			SetCurrentlySelectedItem(-1);

			MenuManager.Instance.RemoveMenu(this);
		}
		else {
			MoveToFront();
			RequestFocus();

			MenuManager.Instance.AddMenu(this);
		}

		base.SetVisible(state);
		SizedForScrollBar = false;
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetFgColor(GetSchemeColor("Menu.TextColor", scheme));
		SetBgColor(GetSchemeColor("Menu.BgColor", scheme));

		BorderDark = scheme.GetColor("BorderDark", new(255, 255, 255, 0));

		foreach (MenuItem? menuItem in MenuItems) {
			if (menuItem.IsCheckable()) {
				menuItem.GetCheckImageSize(out int wide, out _);
				CheckImageWidth = Math.Max(CheckImageWidth, wide);
			}
		}

		RecalculateWidth = true;
		CalculateWidth();

		InvalidateLayout();
	}

	public override void SetBgColor(in Color color) {
		base.SetBgColor(color);
		foreach (var menuItem in MenuItems)
			if (menuItem.HasMenu())
				menuItem.GetMenu()!.SetBgColor(color);
	}

	public override void SetFgColor(in Color color) {
		base.SetFgColor(color);
		foreach (var menuItem in MenuItems)
			if (menuItem.HasMenu())
				menuItem.GetMenu()!.SetFgColor(color);
	}

	public void OnMenuItemSelected(Panel panel) {
		SetVisible(false);
		Scroller!.SetVisible(false);

		MenuItem item = GetParentMenuItem()!;
		if (item != null) {
			Menu parentMenu = item.GetParentMenu()!;
			if (parentMenu != null) {
				KeyValues kv = new("MenuItemSelected");
				kv.SetPtr("panel", panel);
				VGui.PostMessage(parentMenu, kv, this);
			}
		}

		// bool activeItemSet = false;
		foreach (MenuItem menuItem in MenuItems) {
			if (menuItem == panel) {
				// activeItemSet = true;
				ActivatedItem = MenuItems.FindIndex(x => x == menuItem);
				break;
			}
		}

		// if (!activeItemSet) {
		// 	foreach (MenuItem menuItem in MenuItems) {
		// 		if (menuItem.HasMenu()) {
		// 		}
		// 	}
		// }

		if (GetParent() != null) {
			KeyValues kv = new("MenuItemSelected");
			kv.SetPtr("panel", panel);
			VGui.PostMessage(GetParent(), kv, this);
		}
	}

	public int GetActiveItem() {
		return ActivatedItem;
	}

	public KeyValues? GetItemUserData(int itemID) {
		if (MenuItems[itemID] != null) {
			MenuItem menuItem = MenuItems[itemID];
			if (menuItem != null && menuItem.IsEnabled())
				return menuItem.GetUserData();
		}

		return null;
	}

	public void GetItemText(int itemID, Span<char> text) {
		if (MenuItems[itemID] != null) {
			MenuItem menuItem = MenuItems[itemID];
			if (menuItem != null) {
				menuItem.GetText(text);
				return;
			}
		}

		text[0] = '\0';
	}

	public void ActivateItem(int itemID) {
		if (MenuItems[itemID] != null) {
			MenuItem menuItem = MenuItems[itemID];

			if (menuItem != null && menuItem.IsEnabled()) {
				menuItem.FireActionSignal();
				ActivatedItem = itemID;
			}
		}
	}

	public void SilentActivateItem(int itemID) {
		if (MenuItems[itemID] != null) {
			MenuItem menuItem = MenuItems[itemID];

			if (menuItem != null && menuItem.IsEnabled())
				ActivatedItem = itemID;
		}
	}

	public void ActivateItemByRow(int row) {
		if (SortedItems[row] != -1)
			ActivateItem(SortedItems[row]);
	}

	public int GetItemCount() {
		return MenuItems.Count;
	}

	public int GetMenuID(int index) {
		if (SortedItems[index] == -1)
			return -1;

		return SortedItems[index];
	}

	public int GetCurrentlyVisibleItemsCount() {
		if (MenuItems.Count < NumVisibleLines) {
			int CountMenuItems = 0;
			foreach (var item in MenuItems) {
				if (item.IsVisible())
					++CountMenuItems;
			}
			return CountMenuItems;
		}

		return NumVisibleLines;
	}

	public override void OnKeyCodePressed(ButtonCode code) {
		InputMode = MenuMode.KEYBOARD;
		if (GetParent() != null)
			VGui.PostMessage(GetParent(), new KeyValues("KeyModeSet"), this);

		base.OnKeyCodePressed(code);
	}

	public override void OnKeyTyped(char unichar) {
		if (unichar == '\0')
			return;

		switch (TypeAheadMode) {
			case MenuTypeAheadMode.HOT_KEY_MODE:
				// OnHotKey(unichar);
				break;
			case MenuTypeAheadMode.TYPE_AHEAD_MODE:
				OnTypeAhead(unichar);
				break;
			case MenuTypeAheadMode.COMPAT_MODE:
			default:
				break;
		}

		int itemToSelect = CurrentlySelectedItemID;
		if (itemToSelect < 0)
			itemToSelect = 0;

		Span<char> menuItemName = stackalloc char[255];

		int i = itemToSelect + 1;
		if (i >= MenuItems.Count)
			i = 0;

		while (i != itemToSelect) {
			MenuItems[i].GetText(menuItemName);
			if (char.ToLower(menuItemName[0]) == char.ToLower(unichar)) {
				itemToSelect = i;
				break;
			}

			i++;
			if (i >= MenuItems.Count)
				i = 0;
		}

		if (itemToSelect >= 0) {
			SetCurrentlyHighlightedItem(itemToSelect);
			InvalidateLayout();
		}
	}

	public void SetCurrentlySelectedItem(MenuItem item) {
		int itemNum = -1;
		foreach (MenuItem menuItem in MenuItems) {
			if (menuItem == item) {
				itemNum = MenuItems.FindIndex(x => x == menuItem);
				break;
			}
		}

		Assert(itemNum >= 0);
		SetCurrentlySelectedItem(itemNum);
	}

	public void ClearCurrentlyHighlightedItem() {
		if (MenuItems[CurrentlySelectedItemID] != null)
			MenuItems[CurrentlySelectedItemID].DisarmItem();
		CurrentlySelectedItemID = -1;
	}

	public void SetCurrentlySelectedItem(int itemID) {
		if (itemID == CurrentlySelectedItemID)
			return;

		if (CurrentlySelectedItemID > 0 && CurrentlySelectedItemID < MenuItems.Count)
			MenuItems[CurrentlySelectedItemID].DisarmItem();

		PostActionSignal(new KeyValues("MenuItemHighlight", "itemID", itemID));
		CurrentlySelectedItemID = itemID;
	}

	public void SetItemEnabled(ReadOnlySpan<char> itemName, bool state) {
		foreach (var menuItem in MenuItems) {
			if (string.Equals(new(itemName), new(menuItem.GetName()), StringComparison.Ordinal))
				menuItem.SetEnabled(state);
		}
	}

	public void SetItemEnabled(int itemID, bool state) {
		if (MenuItems[itemID] == null)
			return;

		MenuItems[itemID].SetEnabled(state);
	}

	public void SetItemVisible(ReadOnlySpan<char> itemName, bool state) {
		foreach (var menuItem in MenuItems) {
			if (string.Equals(new(itemName), new(menuItem.GetName()), StringComparison.Ordinal)) {
				menuItem.SetVisible(state);
				InvalidateLayout();
			}
		}
	}

	public void SetItemVisible(int itemID, bool state) {
		if (MenuItems[itemID] == null)
			return;

		MenuItems[itemID].SetVisible(state);
	}

	private void AddScrollBar() {
		Scroller!.SetVisible(true);
		SizedForScrollBar = true;
	}

	private void RemoveScrollBar() {
		Scroller!.SetVisible(false);
		SizedForScrollBar = false;
	}

	public void OnSliderMoved() {
		CloseOtherMenus(null);

		InvalidateLayout();
		Repaint();
	}

	public override void OnCursorMoved(int x, int y) {
		InputMode = MenuMode.MOUSE;

		CallParentFunction(new KeyValues("OnCursorMoved", "x", x, "y", y));
	}

	public void SetCurrentlyHighlightedItem(int itemID) {
		SetCurrentlySelectedItem(itemID);
		int row = SortedItems.FindIndex(x => x == itemID);
		Assert(SortedItems.Count == 0 || row != -1);
		if (row == -1)
			return;

		if (Scroller!.IsVisible()) {
			if (row > Scroller.GetValue() + NumVisibleLines - 1)
				Scroller.SetValue(row - NumVisibleLines + 1);
			else if (row < Scroller.GetValue())
				Scroller.SetValue(row);
		}

		if (MenuItems[itemID] != null) {
			if (!MenuItems[itemID].IsArmed())
				MenuItems[itemID].ArmItem();
		}
	}

	public int GetCurrentlyHighlightedItem() {
		return CurrentlySelectedItemID;
	}

	private void OnCursorEnteredMenuItem(MenuItem panel) {
		if (InputMode == MenuMode.MOUSE) {
			panel.ArmItem();
			SetCurrentlySelectedItem(MenuItems.FindIndex(x => x == panel));

			if (panel.HasMenu()) {
				panel.OpenCasecadeMenu();
				ActivateItem(CurrentlySelectedItemID);
			}
		}
	}

	private void OnCursorExitedMenuItem(MenuItem panel) {
		if (InputMode == MenuMode.MOUSE)
			panel.DisarmItem();
	}

	private void MoveAlongMenuItemList(int direction, int loopCount) {
		if (MenuItems.Count == 0)
			return;

		int itemID = CurrentlySelectedItemID;
		int row = SortedItems.FindIndex(x => x == itemID);
		row += direction;

		if (row > SortedItems.Count - 1) {
			if (Scroller!.IsVisible())
				row = SortedItems.Count - 1;
			else
				row = 0;
		}
		else if (row < 0) {
			if (Scroller!.IsVisible())
				row = Scroller.GetValue();
			else
				row = SortedItems.Count - 1;
		}

		if (Scroller!.IsVisible()) {
			if (row > Scroller.GetValue() + NumVisibleLines - 1) {
				int val = Scroller.GetValue();
				val -= -direction;

				Scroller.SetValue(val);

				InvalidateLayout();
			}
			else if (row < Scroller.GetValue()) {
				int val = Scroller.GetValue();
				val -= -direction;

				Scroller.SetValue(val);

				InvalidateLayout();
			}

			if ((row > Scroller.GetValue() + NumVisibleLines - 1) || (row < Scroller.GetValue()))
				Scroller.SetValue(row);

			if (SortedItems.FindIndex(x => x == row) != -1)
				SetCurrentlySelectedItem(SortedItems[row]);

			if (loopCount < MenuItems.Count) {
				Span<char> text = stackalloc char[256];
				MenuItems[CurrentlySelectedItemID].GetText(text);
				if (text[0] == 0 || !MenuItems[CurrentlySelectedItemID].IsVisible())
					MoveAlongMenuItemList(direction, loopCount + 1);
			}
		}
	}

	public MenuMode GetMenuMode() {
		return InputMode;
	}

	public void OnKeyModeSet() {
		InputMode = MenuMode.KEYBOARD;
	}

	public void SetMenuItemChecked(int itemID, bool state) {
		MenuItems[itemID].SetChecked(state);
	}

	public bool IsChecked(int itemID) {
		return MenuItems[itemID].IsChecked();
	}

	public void SetMinimumWidth(int width) {
		MinimumWidth = width;
	}

	public int GetMinimumWidth() {
		return MinimumWidth;
	}

	public void AddSeparator() {
		int lastID = MenuItems.Count - 1;
		Separators.Add(lastID);
		SeparatorPanels.Add(new MenuSeparator(this, "MenuSeparator"));
	}

	public void AddSeparatorAfterItem(int itemID) {
		Assert(MenuItems[itemID] != null);
		Separators.Add(itemID);
		SeparatorPanels.Add(new MenuSeparator(this, "MenuSeparator"));
	}

	public void MoveMenuItem(int itemID, int moveBeforeThisItemID) {
		int count = SortedItems.Count;
		int i;
		for (i = 0; i < count; i++)
			if (SortedItems[i] == itemID) {
				SortedItems.RemoveAt(i);
				break;
			}

		if (i >= count)
			return;

		count = SortedItems.Count;
		for (i = 0; i < count; i++)
			if (SortedItems[i] == moveBeforeThisItemID) {
				SortedItems.Insert(i, itemID);
				break;
			}
	}

	public void SetFont(IFont? font) {
		ItemFont = font;
		if (font != null)
			MenuItemHeight = Surface.GetFontTall(font) + 2;
		InvalidateLayout();
	}

	public void SetCurrentKeyBinding(int itemID, ReadOnlySpan<char> hotkey) {
		if (MenuItems[itemID] != null)
			MenuItems[itemID].SetCurrentKeyBinding(hotkey);
	}

	public static void PlaceContextMenu(Panel parent, Menu menu) {
		Assert(parent);
		Assert(menu);

		if (menu == null || parent == null)
			return;

		menu.SetVisible(true);
		menu.SetParent(parent);
		menu.AddActionSignalTarget(parent);

		Input.GetCursorPos(out int cursorX, out int cursorY);

		menu.SetVisible(true);
		menu.InvalidateLayout(true);
		menu.GetSize(out int menuWide, out int menuTall);

		Surface.GetScreenSize(out int wide, out int tall);

		if (wide - menuWide > cursorX) {
			if (tall - menuTall > cursorY)
				menu.SetPos(cursorX, cursorY);
			else
				menu.SetPos(cursorX, cursorY - menuTall);
		}
		else {
			if (tall - menuTall > cursorY)
				menu.SetPos(cursorX - menuWide, cursorY);
			else
				menu.SetPos(cursorX - menuWide, cursorY - menuTall);
		}

		menu.RequestFocus();
	}

	public void SetUseFallbackFont(bool state, IFont fallback) {
		FallbackItemFont = fallback;
		UseFallbackFont = state;
	}

	public void CloseOtherMenus(MenuItem? item) {
		foreach (var menuItem in MenuItems) {
			if (menuItem == item)
				continue;

			menuItem.CloseCascadeMenu();
		}
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		PostActionSignal(new KeyValues("Command", "command", command));
		base.OnCommand(command);
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		switch (message.Name) {
			case "MenuItemSelected":
				OnMenuItemSelected((Panel)message.GetPtr("panel")!);
				break;
			case "ScrollBarSliderMoved":
				OnSliderMoved();
				break;
			case "CursorEnteredMenuItem":
				OnCursorEnteredMenuItem((MenuItem)message.GetPtr("Panel")!);
				break;
			case "CursorExitedMenuItem":
				OnCursorExitedMenuItem((MenuItem)message.GetPtr("Panel")!);
				break;
			case "KeyModeSet":
				OnKeyModeSet();
				break;
		}

		base.OnMessage(message, sender);
	}
}

class MenuManager
{
	private readonly List<Menu> Menus = [];
	public static MenuManager Instance = new();

	public void AddMenu(Menu? menu) {
		if (menu == null)
			return;

		int count = Menus.Count;
		for (int i = 0; i < count; i++) {
			if (Menus[i] == menu)
				return;
		}

		Menus.Add(menu);
	}

	public void RemoveMenu(Menu? menu) {
		if (menu == null)
			return;

		Menus.Remove(menu);
	}

	public void OnInternalMousePressed(Panel other, ButtonCode code) {
		int count = Menus.Count;
		if (count == 0)
			return;

		Input.GetCursorPos(out int x, out int y);

		for (int i = 0; i < count; i++) {
			Menu menu = Menus[i];

			if (IsWithinMenuOrRelative(menu, x, y))
				return;
		}

		AbortMenus();
	}

	private void AbortMenus() {
		for (int i = Menus.Count - 1; i >= 0; --i) {
			Menu menu = Menus[i];
			Menus.RemoveAt(i);

			menu.SetVisible(false);
		}

		Menus.Clear();
	}

	private bool IsWithinMenuOrRelative(Panel panel, int x, int y) {
		IPanel? topMost = panel.IsWithinTraverse(x, y, true);
		if (topMost != null) {
			if (topMost == panel)
				return true;

			if (topMost.HasParent(panel))
				return true;
		}

		Panel? parent = panel.GetParent();
		if (parent != null) {
			topMost = parent.IsWithinTraverse(x, y, true);
			if (topMost == parent)
				return true;
		}

		return false;
	}
}
