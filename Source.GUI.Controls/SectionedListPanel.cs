using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

class SectionedListPanelHeader : Label
{
	int SectionID;
	Color SectionDividerColor;
	SectionedListPanel ListPanel;
	public bool bDrawDividerBar;

	public SectionedListPanelHeader(SectionedListPanel parent, ReadOnlySpan<char> name, int sectionID) : base(parent, name, "") {
		ListPanel = parent;
		SectionID = sectionID;
		SetTextImageIndex(-1);
		ClearImages();
		SetPaintBackgroundEnabled(false);
		bDrawDividerBar = true;
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetFgColor(GetSchemeColor("SectionedListPanel.HeaderTextColor", scheme));
		SectionDividerColor = GetSchemeColor("SectionedListPanel.DividerColor", scheme);
		SetBgColor(GetSchemeColor("SectionedListPanelHeader.BgColor", GetBgColor(), scheme));
		ClearImages();

		IFont font = ListPanel.GetHeaderFont();
		if (font != null)
			SetFont(font);
		else
			SetFont(scheme.GetFont("DefaultVerySmall", IsProportional()));
	}

	public override void Paint() {
		base.Paint();

		if (!bDrawDividerBar)
			return;

		GetBounds(out _, out _, out int _, out int tall);

		int y = tall - 2;

		Surface.DrawSetColor(SectionDividerColor);
		Surface.DrawFilledRect(1, y, GetWide() - 2, y + 1);
	}

	public void SetColor(Color color) {
		SectionDividerColor = color;
		SetFgColor(color);
	}

	public void SetDividerColor(Color color) => SectionDividerColor = color;

	public override void PerformLayout() {
		base.PerformLayout();

		int colCount = ListPanel.GetColumnCountBySection(SectionID);
		if (colCount != GetImageCount()) {
			for (int i = 0; i < colCount; i++) {
				int columnFlags = ListPanel.GetColumnFlagsBySection(SectionID, i);

				IImage? image;
				if ((columnFlags & SectionedListPanel.HeaderImage) != 0)
					image = null;
				else {
					TextImage textImage = new("");
					textImage.SetFont(GetFont());

					IFont? fallback = ListPanel.GetColumnFallbackFontBySection(SectionID, i);

					if (fallback != null)
						textImage.SetUseFallbackFont(true, fallback);

					textImage.SetColor(GetFgColor());
					image = textImage;
				}

				SetImageAtIndex(i, image, 0);
			}
		}

		for (int repeat = 0; repeat <= 1; repeat++) {
			int xpos = 0;
			for (int i = 0; i < colCount; i++) {
				int columnFlags = ListPanel.GetColumnFlagsBySection(SectionID, i);
				int columnWidth = ListPanel.GetColumnWidthBySection(SectionID, i);
				int maxWidth = columnWidth;

				IImage? image = GetImageAtIndex(i);
				if (image == null) {
					xpos += columnWidth;
					continue;
				}

				int contentWide;
				image.GetContentSize(out int wide, out int tall);
				contentWide = wide;

				if ((columnFlags & SectionedListPanel.ColumnRight) != 0) {
					for (int j = i + 1; j < colCount; j++) {
						int iwide = 0;
						if (GetImageAtIndex(j) != null)
							GetImageAtIndex(j)!.GetContentSize(out iwide, out _);

						if (iwide == 0)
							maxWidth += ListPanel.GetColumnWidthBySection(SectionID, j);
					}
				}

				if (maxWidth >= 0)
					wide = maxWidth;

				if ((columnFlags & SectionedListPanel.ColumnRight) != 0)
					SetImageBounds(i, xpos + wide - contentWide, wide - SectionedListPanel.COLUMN_DATA_GAP);
				else
					SetImageBounds(i, xpos, wide - SectionedListPanel.COLUMN_DATA_GAP);
				xpos += columnWidth;

				if ((columnFlags & SectionedListPanel.HeaderImage) == 0) {
					Assert((TextImage)image != null);
					TextImage textImage = (TextImage)image;
					textImage.SetFont(GetFont());
					textImage.SetText(ListPanel.GetColumnTextBySection(SectionID, i));
					textImage.ResizeImageToContentMaxWidth(maxWidth);
				}
			}
		}
	}

	public void DrawDividerBar(bool draw) => bDrawDividerBar = draw;
}

public class ItemButton : Label
{
	SectionedListPanel ListPanel;
	int ID;
	int SectionID;
	KeyValues Data;
	Color FgColor2;
	Color ArmedFgColor1;
	Color ArmedFgColor2;
	Color OutOfFocusSelectedTextColor;
	Color ArmedBgColor;
	Color SelectionBG2Color;
	List<TextImage> TextImages = [];
	bool Selected;
	bool OverrideColors;
	bool ShowColumns;
	int HorizFillInset;

	public ItemButton(SectionedListPanel parent, int itemID) : base(parent, null, "< item >") {
		ListPanel = parent;
		ID = itemID;
		Data = null;
		Clear();
		HorizFillInset = 0;
	}

	public void Clear() {
		Selected = false;
		OverrideColors = false;
		SectionID = -1;
		SetPaintBackgroundEnabled(false);
		SetTextImageIndex(-1);
		ClearImages();
	}

	public int GetID() => ID;
	public void SetID(int id) => ID = id;
	public int GetSectionID() => SectionID;

	public void SetSectionID(int sectionID) {
		if (sectionID != SectionID) {
			ClearImages();
			TextImages.Clear();
			InvalidateLayout();
		}
		SectionID = sectionID;
	}

	public KeyValues GetData() => Data;

	public void SetData(KeyValues data) {
		Data = data.MakeCopy();
		InvalidateLayout();
	}

	public override void PerformLayout() {
		int colCount = ListPanel.GetColumnCountBySection(SectionID);

		if (Data == null || colCount < 1)
			SetText("< unset >");
		else {
			if (colCount != GetImageCount()) {
				for (int i = 0; i < colCount; i++) {
					int columnFlags = ListPanel.GetColumnFlagsBySection(SectionID, i);

					if ((columnFlags & SectionedListPanel.ColumnImage) == 0) {
						TextImage image = new("");
						TextImages.Add(image);
						image.SetFont(GetFont());

						IFont? fallback = ListPanel.GetColumnFallbackFontBySection(SectionID, i);
						if (fallback != null)
							image.SetUseFallbackFont(true, fallback);
						SetImageAtIndex(i, image, 0);
					}
				}

				for (int i = colCount; i < GetImageCount(); i++)
					AddImage(null, 0);
			}

			int xpos = 0;
			for (int i = 0; i < colCount; i++) {
				ReadOnlySpan<char> keyname = ListPanel.GetColumnNameBySection(SectionID, i);

				int columnFlags = ListPanel.GetColumnFlagsBySection(SectionID, i);
				int maxWidth = ListPanel.GetColumnWidthBySection(SectionID, i);

				IImage? image = null;
				if ((columnFlags & SectionedListPanel.ColumnImage) != 0) {
					if (ListPanel.ImageList != null) {
						int imageIndex = Data.GetInt(keyname, 0);
						if (ListPanel.ImageList.IsValidIndex(imageIndex)) {
							if (imageIndex > 0) {
								image = ListPanel.ImageList.GetImage(imageIndex);
								SetImageAtIndex(i, image, 0);
							}
						}
					}
					else
						Assert("Images columns used in SectionedListPanel with no ImageList set" == null);
				}
				else {
					TextImage? textImage = (TextImage?)GetImageAtIndex(i);
					if (textImage != null) {
						ReadOnlySpan<char> textOverride = Data.GetString(keyname, null);
						if (!textOverride.IsEmpty && textOverride[0] != '#')
							textImage.SetText(textOverride);
						else
							textImage.SetText(Data.GetString(keyname, ""));
						textImage.ResizeImageToContentMaxWidth(maxWidth);

						IPanel? focus = Input.GetFocus();
						if (!OverrideColors) {
							if (IsSelected() && !ListPanel.IsInEditMode()) {
								if (HasFocus() || (focus != null && HasParent(focus)))
									textImage.SetColor(ArmedFgColor2);
								else
									textImage.SetColor(OutOfFocusSelectedTextColor);
							}
							else if ((columnFlags & SectionedListPanel.ColumnBright) != 0)
								textImage.SetColor(ArmedFgColor1);
							else
								textImage.SetColor(FgColor2);
						}
						else {
							if (IsSelected() && (HasFocus() || (focus != null && HasParent(focus))))
								textImage.SetColor(ArmedFgColor2);
							else {
								Color? clrOverride = ListPanel.GetColorOverrideForCell(SectionID, ID, i);
								textImage.SetColor((clrOverride != null) ? clrOverride.Value : GetFgColor());
							}
						}
					}
					image = textImage;
				}

				int imageWide = 0;
				int wide;
				image?.GetContentSize(out imageWide, out _);

				if (maxWidth >= 0)
					wide = maxWidth;
				else
					wide = imageWide;

				if (i == 0 && (columnFlags & SectionedListPanel.ColumnImage) == 0)
					SetImageBounds(i, xpos + SectionedListPanel.COLUMN_DATA_INDENT, wide - (SectionedListPanel.COLUMN_DATA_INDENT + SectionedListPanel.COLUMN_DATA_GAP));
				else {
					if ((columnFlags & SectionedListPanel.ColumnCenter) != 0) {
						int offSet = (wide / 2) - (imageWide / 2);
						SetImageBounds(i, xpos + offSet, wide - offSet - SectionedListPanel.COLUMN_DATA_GAP);
					}
					else if ((columnFlags & SectionedListPanel.ColumnRight) != 0)
						SetImageBounds(i, xpos + wide - imageWide, wide - SectionedListPanel.COLUMN_DATA_GAP);
					else
						SetImageBounds(i, xpos, wide - SectionedListPanel.COLUMN_DATA_GAP);
				}
				xpos += wide;
			}
		}

		base.PerformLayout();
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		ArmedFgColor1 = GetSchemeColor("SectionedListPanel.BrightTextColor", scheme);
		ArmedFgColor2 = GetSchemeColor("SectionedListPanel.SelectedTextColor", scheme);
		OutOfFocusSelectedTextColor = GetSchemeColor("SectionedListPanel.OutOfFocusSelectedTextColor", scheme);
		ArmedBgColor = GetSchemeColor("SectionedListPanel.SelectedBgColor", scheme);

		FgColor2 = GetSchemeColor("SectionedListPanel.TextColor", scheme);

		SetBgColor(GetSchemeColor("SectionedListPanel.BgColor", GetBgColor(), scheme));
		SelectionBG2Color = GetSchemeColor("SectionedListPanel.OutOfFocusSelectedBgColor", scheme);

		IFont? font = ListPanel.GetRowFont();
		if (font != null)
			SetFont(font);
		else {
			ReadOnlySpan<char> fontName = scheme.GetResourceString("SectionedListPanel.Font");
			font = scheme.GetFont(fontName, IsProportional());
			if (font != null)
				SetFont(font);
		}

		ClearImages();
	}

	public override void PaintBackground() {
		GetSize(out int wide, out int tall);

		if (IsSelected() && !ListPanel.IsInEditMode()) {
			IPanel? focus = Input.GetFocus();
			if (HasFocus() || (focus != null && HasParent(focus)))
				Surface.DrawSetColor(ArmedBgColor);
			else
				Surface.DrawSetColor(SelectionBG2Color);
		}
		else
			Surface.DrawSetColor(GetBgColor());
		Surface.DrawFilledRect(HorizFillInset, 0, wide - HorizFillInset, tall);
	}

	public override void Paint() {
		base.Paint();

		if (!ShowColumns)
			return;

		GetSize(out int wide, out int tall);
		Surface.DrawSetColor(255, 255, 255, 255);
		Surface.DrawOutlinedRect(0, 0, wide, tall);

		int colCount = ListPanel.GetColumnCountBySection(SectionID);
		if (Data != null && colCount >= 0) {
			int xpos = 0;
			for (int i = 0; i < colCount; i++) {
				ReadOnlySpan<char> keyname = ListPanel.GetColumnNameBySection(SectionID, i);
				int columnFlags = ListPanel.GetColumnFlagsBySection(SectionID, i);
				int maxWidth = ListPanel.GetColumnWidthBySection(SectionID, i);

				IImage? image = null;
				if ((columnFlags & SectionedListPanel.ColumnImage) != 0) {
					if (ListPanel.ImageList != null) {
						int imageIndex = Data.GetInt(keyname, 0);
						if (ListPanel.ImageList.IsValidIndex(imageIndex)) {
							if (imageIndex > 0) {
								image = ListPanel.ImageList.GetImage(imageIndex);
							}
						}
					}
				}
				else
					image = GetImageAtIndex(i);

				int imageWide = 0;
				image?.GetContentSize(out imageWide, out _);

				if (maxWidth >= 0)
					wide = maxWidth;
				else
					wide = imageWide;

				xpos += wide;
				Surface.DrawOutlinedRect(xpos, 0, xpos, GetTall());
			}
		}
	}

	public override void OnMousePressed(ButtonCode code) {
		if (ListPanel != null && ListPanel.IsClickable() && IsEnabled()) {
			if (code == ButtonCode.MouseLeft)
				ListPanel.PostActionSignal(new KeyValues("ItemLeftClick", "itemID", ID));

			if (code == ButtonCode.MouseRight) {
				KeyValues msg = new("ItemContextMenu", "itemID", ID);
				msg.SetPtr("SubPanel", this);
				ListPanel.PostActionSignal(msg);
			}

			ListPanel.SetSelectedItem(this);
		}
	}

	public void SetSelected(bool state) {
		if (Selected != state) {
			if (state)
				RequestFocus();

			Selected = state;
			SetPaintBackgroundEnabled(state);
			InvalidateLayout();
			Repaint();
		}
	}

	public bool IsSelected() => Selected;

	public override void OnSetFocus() {
		InvalidateLayout();
		base.OnSetFocus();
	}

	public override void OnKillFocus(Panel? newPanel) {
		InvalidateLayout();
		base.OnKillFocus(newPanel);
	}

	public override void OnMouseDoublePressed(ButtonCode code) {

	}

	public void GetCellBounds(int column, ref int xpos, ref int columnWide) {
		xpos = 0;
		columnWide = 0;

		int colCount = ListPanel.GetColumnCountBySection(SectionID);

		for (int i = 0; i < colCount; i++) {
			int maxWidth = ListPanel.GetColumnWidthBySection(SectionID, i);

			IImage? image = GetImageAtIndex(i);
			if (image == null)
				continue;

			image.GetContentSize(out int wide, out _);
			if (maxWidth >= 0)
				wide = maxWidth;

			if (i == column) {
				columnWide = wide;
				return;
			}

			xpos += wide;
		}
	}

	public void GetMaxCellBounds() {

	}

	public void SetOverrideColors(bool state) => OverrideColors = state;
	public void SetShowColumns(bool state) => ShowColumns = state;
	public void SetItemBgHorizFillInset(int inset) => HorizFillInset = inset;
}

public class SectionedListPanel : Panel
{
	public static Panel Create_SectionedListPanel() => new SectionedListPanel(null, null);

	public const byte HeaderImage = 0x01;
	public const byte ColumnImage = 0x02;
	public const byte ColumnBright = 0x04;
	public const byte ColumnCenter = 0x08;
	public const byte ColumnRight = 0x10;

	public const int BUTTON_HEIGHT_DEFAULT = 20;
	public const int BUTTON_HEIGHT_SPACER = 7;
	public const int DEFAULT_LINE_SPACING = 20;
	public const int DEFAULT_SECTION_GAP = 8;
	public const int COLUMN_DATA_INDENT = 6;
	public const int COLUMN_DATA_GAP = 2;

	public delegate bool SectionSortFunc(SectionedListPanel list, int itemID1, int itemID2);

	struct ColorOverride
	{
		public int SectionID;
		public int ItemID;
		public int ColumnID;
		public Color ClrOverride;
	}

	struct Column
	{
		public string ColumnName;
		public string ColumnText;
		public int ColumnFlags;
		public int Width;
		public IFont? FallbackFont;
	}

	struct Section
	{
		public int ID;
		public bool AlwaysVisible;
		public SectionedListPanelHeader? Header;
		public List<Column> Columns = [];
		public SectionSortFunc? SortFunc;
		public int MinimumHeight;

		public Section() { }
	}

	List<Section> Sections = [];
	List<ItemButton> Items = [];
	List<ItemButton> FreeItems = [];
	List<ItemButton> SortedItems = [];
	List<ColorOverride> ColorOverrides = [];

	ItemButton? SelectedItem;
	Panel? EditModePanel;
	int EditModeItemID;
	int EditModeColumn;
	int ContentHeight;
	int LineSpacing;
	int LineGap;
	int SectionGap;

	ScrollBar ScrollBar;
	public ImageList? ImageList;
	bool DeleteImageListWhenDone;
	bool SortNeeded;
	bool VerticalScrollbarEnabled;

	IFont HeaderFont;
	IFont RowFont;

	bool Clickable;
	bool DrawSectionHeaders;

	[PanelAnimationVar("show_columns", "false")] bool ShowColumns;

	public SectionedListPanel(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		ScrollBar = new(this, "SectionedScrollBar", true);
		ScrollBar.SetVisible(false);
		ScrollBar.AddActionSignalTarget(this);

		EditModeItemID = 0;
		EditModeColumn = 0;
		SortNeeded = false;
		VerticalScrollbarEnabled = true;
		LineSpacing = DEFAULT_LINE_SPACING;
		LineGap = 0;
		SectionGap = DEFAULT_SECTION_GAP;

		ImageList = null;
		DeleteImageListWhenDone = false;

		Clickable = true;
		DrawSectionHeaders = true;
	}

	private void ReSortList() {
		SortedItems.Clear();

		int sectionStart;
		for (int sectionIndex = 0; sectionIndex < Sections.Count; sectionIndex++) {
			Section section = Sections[sectionIndex];
			sectionStart = SortedItems.Count;

			for (int i = 0; i < Items.Count; i++) {
				if (Items[i].GetSectionID() == Sections[sectionIndex].ID) {
					if (section.SortFunc != null) {
						int insertionPoint = sectionStart;
						for (; insertionPoint < SortedItems.Count; insertionPoint++) {
							if (section.SortFunc(this, Items[i].GetID(), SortedItems[insertionPoint].GetID()))
								break;
						}

						if (insertionPoint == SortedItems.Count)
							SortedItems.Add(Items[i]);
						else
							SortedItems.Insert(insertionPoint, Items[i]);
					}
					else
						SortedItems.Add(Items[i]);
				}
			}
		}
	}

	public override void PerformLayout() {
		if (SortNeeded) {
			ReSortList();
			SortNeeded = false;
		}

		base.PerformLayout();

		LayoutPanels(ref ContentHeight);
		GetBounds(out _, out _, out int cwide, out int ctall);

		if (ContentHeight > ctall && VerticalScrollbarEnabled) {
			ScrollBar.SetVisible(true);
			ScrollBar.MoveToFront();
			ScrollBar.SetPos(cwide - ScrollBar.GetWide() - 2, 0);
			ScrollBar.SetSize(ScrollBar.GetWide(), ctall - 2);
			ScrollBar.SetRangeWindow(ctall);
			ScrollBar.SetRange(0, ContentHeight);
			ScrollBar.InvalidateLayout();
			ScrollBar.Repaint();

			LayoutPanels(ref ContentHeight);
		}
		else {
			ScrollBar.SetValue(0);

			bool wasVisible = ScrollBar.IsVisible();
			ScrollBar.SetVisible(false);

			if (wasVisible)
				LayoutPanels(ref ContentHeight);
		}
	}

	private void LayoutPanels(ref int contentTall) {
		int tall = GetSectionTall();
		int x = 5, wide = GetWide() - 10;
		int y = 5;

		if (ScrollBar.IsVisible()) {
			y -= ScrollBar.GetValue();
			wide -= ScrollBar.GetWide();
		}

		int start;
		int end;

		bool firstVisibleSection = true;
		for (int sectionIndex = 0; sectionIndex < Sections.Count; sectionIndex++) {
			Section section = Sections[sectionIndex];

			start = -1;
			end = -1;
			for (int i = 0; i < SortedItems.Count; i++) {
				if (SortedItems[i].GetSectionID() == Sections[sectionIndex].ID) {
					if (start == -1)
						start = i;
					end = i;
				}
			}

			if (start == -1 && !section.AlwaysVisible) {
				section.Header?.SetVisible(false);
				continue;
			}

			if (firstVisibleSection)
				firstVisibleSection = false;
			else
				y += SectionGap;

			int nMinNextSectionY = y + section.MinimumHeight;
			if (DrawSectionHeaders) {
				section.Header?.SetBounds(x, y, wide, tall);
				section.Header?.SetVisible(true);
				y += tall;
			}
			else
				section.Header?.SetVisible(false);

			if (start == -1 && section.AlwaysVisible) {
			}
			else {
				for (int i = start; i <= end; i++) {
					ItemButton item = SortedItems[i];
					item.SetBounds(x, y, wide, LineSpacing);

					if (EditModePanel != null && EditModeItemID == item.GetID()) {
						int cx = 0, cwide = 0;
						item.GetCellBounds(1, ref cx, ref cwide);
						EditModePanel.SetBounds(cx, y, cwide, tall);
					}

					y += LineSpacing + LineGap;
				}
			}

			if (y < nMinNextSectionY)
				y = nMinNextSectionY;
		}

		contentTall = y;
		if (ScrollBar.IsVisible())
			contentTall += ScrollBar.GetValue();
	}

	private void ScrollToItem(int itemID) {

	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetFgColor(GetSchemeColor("SectionedListPanel.BgColor", scheme));
		SetBorder(scheme.GetBorder("ButtonDepressedBorder"));

		foreach (ItemButton item in Items)
			item.SetShowColumns(ShowColumns);
	}

	public void SetHeaderFont(IFont font) => HeaderFont = font;
	public IFont GetHeaderFont() => HeaderFont;

	public void SetRowFont(IFont font) => RowFont = font;
	public IFont GetRowFont() => RowFont;

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);

		LineSpacing = resourceData.GetInt("linespacing", 0);
		if (LineSpacing == 0)
			LineSpacing = DEFAULT_LINE_SPACING;

		SectionGap = resourceData.GetInt("sectiongap", 0);
		if (SectionGap == 0)
			SectionGap = DEFAULT_SECTION_GAP;

		LineGap = resourceData.GetInt("linegap", 0);

		if (IsProportional()) {
			LineSpacing = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, LineSpacing);
			LineGap = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, LineGap);
			SectionGap = SchemeManager.GetProportionalScaledValueEx(GetScheme()!, SectionGap);
		}
	}

	public override void SetProportional(bool state) {
		base.SetProportional(state);

		for (int i = 0; i < Sections.Count; i++)
			Sections[i].Header?.SetProportional(state);

		foreach (ItemButton item in Items)
			item.SetProportional(state);
	}

	public void SetVerticalScrollbar(bool state) => VerticalScrollbarEnabled = state;

	public void AddSection(int sectionID, ReadOnlySpan<char> name, SectionSortFunc? sortFunc = null) => AddSection(sectionID, new SectionedListPanelHeader(this, name, sectionID), sortFunc);

	private void AddSection(int sectionID, SectionedListPanelHeader header, SectionSortFunc? sortFunc = null) {
		header.MakeReadyForUse();

		Section newSection = new() {
			ID = sectionID,
			Header = header,
			SortFunc = sortFunc,
			AlwaysVisible = false,
			MinimumHeight = 0
		};

		Sections.Add(newSection);
	}

	public void RemoveAllSections() {
		for (int i = 0; i < Sections.Count; i++) {
			if (!Sections.IsValidIndex(i))
				continue;

			Sections[i].Header!.SetVisible(false);
			Sections[i].Header!.MarkForDeletion();
		}

		Sections.Clear();
		SortedItems.Clear();

		InvalidateLayout();
		ReSortList();
	}

	public bool AddColumnToSection(int sectionID, ReadOnlySpan<char> columnName, ReadOnlySpan<char> columnText, int columnFlags, int width, IFont? fallbackFont = null) {
		ReadOnlySpan<char> localizedText = Localize.Find(columnText);
		if (!localizedText.IsEmpty)
			columnText = localizedText;

		int index = FindSectionIndexByID(sectionID);
		if (index < 0)
			return false;

		Section section = Sections[index];
		Column column = new() {
			ColumnName = columnName.ToString(),
			ColumnText = columnText.ToString(),
			ColumnFlags = columnFlags,
			Width = width,
			FallbackFont = fallbackFont
		};
		section.Columns.Add(column);
		Sections[index] = section;
		return true;
	}

	public void ModifyColumn() {

	}

	public int AddItem(int sectionID, KeyValues data) {
		int itemID = GetNewItemButton();
		ModifyItem(itemID, sectionID, data);

		SortedItems.Add(Items[itemID]);
		SortNeeded = true;

		return itemID;
	}

	public bool ModifyItem(int itemID, int sectionID, KeyValues data) {
		if (itemID < 0 || itemID >= Items.Count)
			return false;

		Items[itemID].SetSectionID(sectionID);
		Items[itemID].SetData(data);
		Items[itemID].InvalidateLayout();
		SortNeeded = true;

		return true;
	}

	public void SetItemFgColor(int itemID, Color color) {
		Assert(itemID >= 0 && itemID < Items.Count);
		if (itemID < 0 || itemID >= Items.Count)
			return;

		Items[itemID].SetFgColor(color);
		Items[itemID].SetOverrideColors(true);
		Items[itemID].InvalidateLayout();
	}

	public void SetItemBgColor(int itemID, Color color) {
		Assert(itemID >= 0 && itemID < Items.Count);
		if (itemID < 0 || itemID >= Items.Count)
			return;

		Items[itemID].SetBgColor(color);
		Items[itemID].SetPaintBackgroundEnabled(true);
		Items[itemID].SetOverrideColors(true);
		Items[itemID].InvalidateLayout();
	}

	public void SetItemBgHorizFillInset(int itemID, int inset) {
		Assert(itemID >= 0 && itemID < Items.Count);
		if (itemID < 0 || itemID >= Items.Count)
			return;

		Items[itemID].SetItemBgHorizFillInset(inset);
	}

	public void SetItemFont(int itemID, IFont font) {
		Assert(itemID >= 0 && itemID < Items.Count);
		if (itemID < 0 || itemID >= Items.Count)
			return;

		Items[itemID].SetFont(font);
	}

	public void SetItemEnabled(int itemID, bool enabled) {
		Assert(itemID >= 0 && itemID < Items.Count);
		if (itemID < 0 || itemID >= Items.Count)
			return;

		Items[itemID].SetEnabled(enabled);
	}

	public void SetSectionFgColor(int sectionID, Color color) {
		if (sectionID < 0 || sectionID >= Sections.Count)
			return;

		Sections[sectionID].Header?.SetColor(color);
	}

	public void SetSectionDividerColor(int sectionID, Color color) {
		if (sectionID < 0 || sectionID >= Sections.Count)
			return;

		Sections[sectionID].Header?.SetDividerColor(color);
	}

	public void SetSectionDrawDividerBar(int sectionID, bool draw) {
		if (sectionID < 0 || sectionID >= Sections.Count)
			return;

		Sections[sectionID].Header?.DrawDividerBar(draw);
	}

	public void SetSectionAlwaysVisible(int sectionID, bool visible) {
		if (sectionID < 0 || sectionID >= Sections.Count)
			return;

		Section section = Sections[sectionID];
		section.AlwaysVisible = visible;
		Sections[sectionID] = section;
	}

	public void SetFontSection(int sectionID, IFont font) {
		if (sectionID < 0 || sectionID >= Sections.Count)
			return;

		Sections[sectionID].Header?.SetFont(font);
	}

	public void SetSectionMinimumHeight(int sectionID, int minimumHeight) {
		if (sectionID < 0 || sectionID >= Sections.Count)
			return;

		Section section = Sections[sectionID];
		section.MinimumHeight = minimumHeight;
		Sections[sectionID] = section;
	}

	public void RemoveItem(int itemID) {
		if (itemID < 0 || itemID >= Items.Count)
			return;

		SortedItems.Remove(Items[itemID]);
		SortNeeded = true;

		Items[itemID].MarkForDeletion();
		Items.Remove(Items[itemID]);

		InvalidateLayout();
	}

	public int GetColumnCountBySection(int sectionID) {
		int index = FindSectionIndexByID(sectionID);
		if (index < 0)
			return 0;

		return Sections[index].Columns.Count;
	}

	public ReadOnlySpan<char> GetColumnNameBySection(int sectionID, int columnIndex) {
		int index = FindSectionIndexByID(sectionID);
		if (index < 0 || columnIndex >= Sections[index].Columns.Count)
			return null;

		return Sections[index].Columns[columnIndex].ColumnName;
	}

	public ReadOnlySpan<char> GetColumnTextBySection(int sectionID, int columnIndex) {
		int index = FindSectionIndexByID(sectionID);
		if (index < 0 || columnIndex >= Sections[index].Columns.Count)
			return null;

		return Sections[index].Columns[columnIndex].ColumnText;
	}

	public int GetColumnFlagsBySection(int sectionID, int columnIndex) {
		int index = FindSectionIndexByID(sectionID);
		if (index < 0)
			return 0;

		if (columnIndex >= Sections[index].Columns.Count)
			return 0;

		return Sections[index].Columns[columnIndex].ColumnFlags;
	}

	public int GetColumnWidthBySection(int sectionID, int columnIndex) {
		int index = FindSectionIndexByID(sectionID);
		if (index < 0)
			return 0;

		if (columnIndex >= Sections[index].Columns.Count)
			return 0;

		return Sections[index].Columns[columnIndex].Width;
	}

	public void SetColumnWidthBySection() {

	}

	public int GetColumnIndexByName(int sectionID, ReadOnlySpan<char> name) {
		int index = FindSectionIndexByID(sectionID);
		if (index < 0)
			return 0;

		for (int i = 0; i < Sections[index].Columns.Count; i++) {
			if (Sections[index].Columns[i].ColumnName.SequenceEqual(name))
				return i;
		}

		return -1;
	}

	public int FindSectionIndexByID(int sectionID) {
		for (int i = 0; i < Sections.Count; i++) {
			if (Sections[i].ID == sectionID)
				return i;
		}

		return -1;
	}

	[UiMessage("ScrollBarSliderMoved")]
	private void OnSliderMoved() {
		InvalidateLayout();
		Repaint();
	}

	public override void OnMouseWheeled(int delta) {
		if (EditModePanel != null) {
			CallParentFunction(new KeyValues("MouseWheeled", "delta", delta));
			return;
		}

		int val = ScrollBar.GetValue();
		val -= delta * BUTTON_HEIGHT_DEFAULT * 3;
		ScrollBar.SetValue(val);
	}

	public override void OnSizeChanged(int wide, int tall) {
		base.OnSizeChanged(wide, tall);
		ScrollBar?.SetValue(0);
		InvalidateLayout();
		Repaint();
	}

	public override void OnMousePressed(ButtonCode code) {
		if (Clickable)
			ClearSelection();
	}

	public void ClearSelection() => SetSelectedItem(null);

	public void MoveSelectionDown() {

	}

	public void MoveSelectionUp() {

	}

	public void NavigateTo() {

	}

	public override void OnKeyCodePressed(ButtonCode code) {

	}

	public void DeleteAllItems() {
		for (int i = 0; i < Items.Count; i++) {
			Items[i].SetVisible(false);
			Items[i].Clear();
			FreeItems.Add(Items[i]);
		}

		Items.Clear();
		SortedItems.Clear();
		SelectedItem = null;
		InvalidateLayout();
		SortNeeded = true;
	}

	public void SetSelectedItem(ItemButton? item) {
		if (SelectedItem == item)
			return;

		SelectedItem?.SetSelected(false);
		SelectedItem = item;
		SelectedItem?.SetSelected(true);

		Repaint();
		PostActionSignal(new KeyValues("ItemSelected", "itemID", SelectedItem?.GetID() ?? -1));
	}

	public int GetSelectedItem() {
		if (SelectedItem != null)
			return SelectedItem.GetID();

		return -1;
	}

	public void SetSelectedItem(int itemID) {
		if (itemID < 0 || itemID >= Items.Count)
			return;

		SetSelectedItem(Items[itemID]);
	}

	public KeyValues? GetItemData(int itemID) {
		Assert(IsItemIDValid(itemID));
		if (!IsItemIDValid(itemID))
			return null;
		return Items[itemID].GetData();
	}

	public int GetItemSection(int itemID) {
		if (itemID < 0 || itemID >= Items.Count)
			return -1;

		return Items[itemID].GetSectionID();
	}

	public bool IsItemIDValid(int itemID) => itemID >= 0 && itemID < Items.Count;
	public int GetHighestItemID() => Items.Count - 1;
	public int GetItemCount() => SortedItems.Count;

	public int GetItemIDFromRow(int row) {
		if (row < 0 || row >= SortedItems.Count)
			return -1;

		return SortedItems[row].GetID();
	}

	public int GetRowFromItemID(int itemID) {
		for (int i = 0; i < SortedItems.Count; i++) {
			if (SortedItems[i].GetID() == itemID)
				return i;
		}

		return -1;
	}

	public void GetCellBounds() {

	}

	public void GetSectionHeaderBounds() {

	}

	public void GetMaxCellBounds() {

	}

	public void GetItemBounds() {

	}

	public void InvalidateItem(int itemID) {
		if (!IsItemIDValid(itemID))
			return;

		Items[itemID].InvalidateLayout();
		Items[itemID].Repaint();
	}

	public void EnterEditMode(int itemID, int column, Panel editPanel) {
		EditModePanel = editPanel;
		EditModeItemID = itemID;
		EditModeColumn = column;
		editPanel.SetParent(this);
		editPanel.SetVisible(true);
		editPanel.RequestFocus();
		editPanel.MoveToFront();
		InvalidateLayout();
	}

	public void LeaveEditMode() {
		if (EditModePanel != null) {
			InvalidateItem(EditModeItemID);
			EditModePanel.SetVisible(false);
			EditModePanel.SetParent(null);
			EditModePanel = null;
		}
	}

	public bool IsInEditMode() => EditModePanel != null;

	public void SetImageList(ImageList imageList, bool deleteWhenDone) {
		DeleteImageListWhenDone = deleteWhenDone;
		ImageList = imageList;
	}

	public override void OnSetFocus() {
		if (SelectedItem != null)
			SelectedItem.OnSetFocus();
		else
			base.OnSetFocus();
	}

	public int GetSectionTall() {
		if (Sections.Count > 0) {
			IFont? font = Sections[0].Header?.GetFont();
			if (font != null)
				return Surface.GetFontTall(font) + BUTTON_HEIGHT_SPACER;
		}

		return BUTTON_HEIGHT_DEFAULT;
	}

	public void GetContentSize(ref int wide, ref int tall) {
		if (IsLayoutInvalid()) {
			if (SortNeeded) {
				ReSortList();
				SortNeeded = false;
			}
			LayoutPanels(ref ContentHeight);
		}

		wide = GetWide();
		tall = ContentHeight;
	}

	public int GetNewItemButton() {
		int itemID = Items.Count;
		if (FreeItems.Count > 0) {
			Items.Add(FreeItems[0]);
			Items[itemID].SetID(itemID);
			Items[itemID].SetVisible(true);
			FreeItems.RemoveAt(0);
		}
		else {
			Items.Add(new ItemButton(this, itemID));
			Items[itemID].SetShowColumns(ShowColumns);
		}

		Items[itemID].SetEnabled(true);

		return itemID;
	}

	public IFont? GetColumnFallbackFontBySection(int sectionID, int columnIndex) {
		int index = FindSectionIndexByID(sectionID);
		if (index < 0)
			return null;

		if (columnIndex >= Sections[index].Columns.Count)
			return null;

		return Sections[index].Columns[columnIndex].FallbackFont;
	}

	public Color? GetColorOverrideForCell(int sectionID, int itemID, int columnID) {
		foreach (ColorOverride clrOverride in ColorOverrides) {
			if (clrOverride.SectionID == sectionID && clrOverride.ItemID == itemID && clrOverride.ColumnID == columnID)
				return clrOverride.ClrOverride;
		}

		return null;
	}

	public void SetColorOverrideForCell(int sectionID, int itemID, int columnID, Color clrOverride) {
		for (int i = 0; i < ColorOverrides.Count; i++) {
			ColorOverride existing = ColorOverrides[i];
			if (existing.SectionID == sectionID && existing.ItemID == itemID && existing.ColumnID == columnID) {
				existing.ClrOverride = clrOverride;
				ColorOverrides[i] = existing;
				return;
			}
		}

		ColorOverride newOverride = new() {
			SectionID = sectionID,
			ItemID = itemID,
			ColumnID = columnID,
			ClrOverride = clrOverride
		};
		ColorOverrides.Add(newOverride);
	}

	internal bool IsClickable() => Clickable;
}
