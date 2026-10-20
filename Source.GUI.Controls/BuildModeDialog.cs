using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public struct PanelItem
{
	public Panel? EditLabel;
	public TextEntry? EditPanel;
	public ComboBox? Combo;
	public Button? EditButton;
	public string Name;
	public int Type;

	public PanelItem() {
		EditLabel = null;
		EditPanel = null;
		Combo = null;
		EditButton = null;
		Name = string.Empty;
		Type = 0;
	}
}

class SmallTextEntry : TextEntry
{
	public SmallTextEntry(Panel parent, ReadOnlySpan<char> name) : base(parent, name) { }

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		SetFont(scheme.GetFont("DefaultVerySmall"));
	}
}

class BuildModeNavCombo : ComboBox
{
	bool Parents;
	Panel? Context;
	public BuildModeNavCombo(Panel parent, ReadOnlySpan<char> name, int numLines, bool allowEdit, bool getParents, Panel? context) : base(parent, name, numLines, allowEdit) {
		Parents = getParents;
		Context = context;
	}

	public override void OnShowMenu(Menu menu) {
		menu.DeleteAllItems();

		if (Context == null)
			return;

		if (Parents) {
			Panel? parent = Context.GetParent();
			while (parent != null) {
				if (parent is EditablePanel ep && ep.GetBuildGroup() != null) {
					KeyValues kv = new("Panel");
					kv.SetPtr("ptr", ep);
					ReadOnlySpan<char> text = ep.GetName().IsEmpty ? "unnamed" : ep.GetName();
					menu.AddMenuItem(text, new KeyValues("SetText", "text", text), GetParent()!, kv);
				}
				parent = parent.GetParent();
			}
		}
		else {
			for (int i = 0; i < Context.GetChildCount(); i++) {
				Panel child = Context.GetChild(i);
				if (child is EditablePanel ep && ep.IsVisible() && ep.GetBuildGroup() != null) {
					KeyValues kv = new("Panel");
					kv.SetPtr("ptr", ep);
					ReadOnlySpan<char> text = ep.GetName().IsEmpty ? "unnamed" : ep.GetName();
					menu.AddMenuItem(text, new KeyValues("SetText", "text", text), GetParent()!, kv);
				}
			}
		}
	}
}

public class BuildModeDialog : Frame
{
	public static Panel Create_BuildModeDialog() => new BuildModeDialog(new(null, null));

	class PanelList
	{
		public List<PanelItem> panelList = [];
		public PanelListPanel Controls;
		public KeyValues? ResourceData;

		public void AddItem(Panel? label, TextEntry? edit, ComboBox? combo, Button? button, ReadOnlySpan<char> name, Type type) {
			PanelItem item = new() {
				EditLabel = label,
				EditPanel = edit,
				Name = name.ToString(),
				Type = (int)type,
				Combo = combo,
				EditButton = button
			};
			panelList.Add(item);
		}

		public void RemoveAll() {
			for (int i = 0; i < panelList.Count; i++) {
				PanelItem item = panelList[i];
				item.EditLabel?.DeletePanel();
				item.EditPanel?.DeletePanel();
				item.Combo?.DeletePanel();
				item.EditButton?.DeletePanel();
			}

			panelList.Clear();
			Controls.RemoveAll();
		}
	}

	static Dictionary<string, KeyValues> CachedResFiles = [];

	Panel CurrentPanel;
	BuildGroup BuildGroup;
	Label StatusLabel;
	ComboBox FileSelectionCombo;
	Divider Divider;

	PanelList panelList;

	Button SaveButton;
	Button ApplyButton;
	Button ExitButton;
	Button DeleteButton;
	Button ReloadLocalization;
	MenuButton VarsButton;

	bool AutoUpdate;

	ComboBox AddNewControlCombo;
	KeyValues UndoSettings;
	KeyValues CopySettings;
	char[] CopyClassName;
	int Click1;
	int Click2;

	enum Type
	{
		String,
		Integer,
		Color,
		Alignment,
		AutoResize,
		Corner,
		LocalizedString
	}

	Menu ContextMenu;

	BuildModeNavCombo EditableParents;
	BuildModeNavCombo EditableChildren;

	Button NextChild;
	Button PrevChild;

	const int TYPE_STRING = 0;
	const int TYPE_INTEGER = 1;
	const int TYPE_COLOR = 2;
	const int TYPE_ALIGNMENT = 3;
	const int TYPE_AUTORESIZE = 4;
	const int TYPE_CORNER = 5;
	const int TYPE_LOCALIZEDSTRING = 6;

	public BuildModeDialog(BuildGroup buildGroup) : base(buildGroup?.GetContextPanel(), "BuildModeDialog") {
		SetMinimumSize(300, 256);
		SetSize(300, 420);
		CurrentPanel = null;
		EditableParents = null;
		EditableChildren = null;
		NextChild = null;
		PrevChild = null;
		BuildGroup = buildGroup;
		UndoSettings = null;
		CopySettings = null;
		AutoUpdate = false;
		MakePopup(true);
		SetTitle("VGUI Build Mode Editor", true);

		CreateControls();
		// LoadUserConfig("BuildModeDialog"); // TODO: System.GetUserConfigFileData

		// buildmodedialogmgr
	}

	public override void OnClose() {
		Input.SetAppModalSurface(null);
		base.OnClose();
	}

	static readonly KeyValues KV_ReloadLocalization = new("ReloadLocalization");
	public void CreateControls() {
		int i;
		panelList = new() {
			ResourceData = new KeyValues("BuildDialog"),
			Controls = new(this, "BuildModeControls")
		};

		FileSelectionCombo = new(this, "FileSelectionCombo", 10, false);
		for (i = 0; i < BuildGroup.GetRegisteredControlSettingsFileCount(); i++)
			FileSelectionCombo.AddItem(BuildGroup.GetRegisteredControlSettingsFileByIndex(i), null);

		if (FileSelectionCombo.GetItemCount() < 2)
			FileSelectionCombo.SetEnabled(false);

		const int buttonH = 18;

		StatusLabel = new(this, "StatusLabel", "[nothing currently selected]");
		StatusLabel.SetTextColorState(ColorState.Dull);
		StatusLabel.SetTall(buttonH);

		Divider = new(this, "Divider");

		AddNewControlCombo = new(this, null, 30, false);
		AddNewControlCombo.SetSize(116, buttonH);
		AddNewControlCombo.SetOpenDirection(MenuDirection.DOWN);

		EditableParents = new(this, null, 15, false, true, BuildGroup.GetContextPanel());
		EditableParents.SetSize(116, buttonH);
		EditableParents.SetOpenDirection(MenuDirection.DOWN);

		EditableChildren = new(this, null, 15, false, false, BuildGroup.GetContextPanel());
		EditableChildren.SetSize(116, buttonH);
		EditableChildren.SetOpenDirection(MenuDirection.DOWN);

		NextChild = new(this, "NextChild", "Next");
		NextChild.SetCommand(new KeyValues("OnChangeChild", "direction", 1));

		PrevChild = new(this, "PrevChild", "Prev");
		PrevChild.SetCommand(new KeyValues("OnChangeChild", "direction", -1));

		int defaultItem = AddNewControlCombo.AddItem("None", null);

		List<ReadOnlyMemory<char>> names = [];
		// GetFactoryNames

		var sorted = new SortedSet<string>(StringComparer.Ordinal);
		foreach (var name in names) sorted.Add(name.ToString());
		foreach (var name in sorted) AddNewControlCombo.AddItem(name, null);

		AddNewControlCombo.ActivateItem(defaultItem);

		ExitButton = new(this, "ExitButton", "&Exit");
		ExitButton.SetSize(64, buttonH);

		SaveButton = new(this, "SaveButton", "&Save");
		SaveButton.SetSize(64, buttonH);

		ApplyButton = new(this, "ApplyButton", "&Apply");
		ApplyButton.SetSize(64, buttonH);

		ReloadLocalization = new(this, "Localization", "&Reload Localization");
		ReloadLocalization.SetSize(100, buttonH);

		ExitButton.SetCommand("Exit");
		SaveButton.SetCommand("Save");
		ApplyButton.SetCommand("Apply");
		ReloadLocalization.SetCommand(KV_ReloadLocalization);

		DeleteButton = new(this, "DeletePanelButton", "Delete");
		DeleteButton.SetSize(64, buttonH);
		DeleteButton.SetCommand("DeletePanel");

		VarsButton = new(this, "VarsButton", "Variables");
		VarsButton.SetSize(72, buttonH);
		VarsButton.SetOpenDirection(MenuDirection.DOWN);

		// KeyValues vars = BuildGroup.GetDialogVariables();
		// if(vars)...
		VarsButton.SetEnabled(false);

		ApplyButton.SetTabPosition(1);
		panelList.Controls.SetTabPosition(2);
		VarsButton.SetTabPosition(3);
		DeleteButton.SetTabPosition(4);
		AddNewControlCombo.SetTabPosition(5);
		SaveButton.SetTabPosition(6);
		ExitButton.SetTabPosition(7);

		EditableParents.SetTabPosition(8);
		EditableChildren.SetTabPosition(9);

		PrevChild.SetTabPosition(10);
		NextChild.SetTabPosition(11);

		ReloadLocalization.SetTabPosition(12);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		IFont font = scheme.GetFont("DefaultVerySmall")!;
		StatusLabel.SetFont(font);
		ReloadLocalization.SetFont(font);
		ExitButton.SetFont(font);
		SaveButton.SetFont(font);
		ApplyButton.SetFont(font);
		AddNewControlCombo.SetFont(font);
		EditableParents.SetFont(font);
		EditableChildren.SetFont(font);
		DeleteButton.SetFont(font);
		VarsButton.SetFont(font);
		PrevChild.SetFont(font);
		NextChild.SetFont(font);
	}

	public override void PerformLayout() {
		base.PerformLayout();

		const int BORDER_GAP = 16, YGAP_SMALL = 4, YGAP_LARGE = 8, TITLE_HEIGHT = 24, BOTTOM_CONTROLS_HEIGHT = 145, XGAP = 6;

		GetSize(out int wide, out int tall);

		int xpos = BORDER_GAP;
		int ypos = BORDER_GAP + TITLE_HEIGHT;

		FileSelectionCombo.SetBounds(xpos, ypos, wide - (BORDER_GAP * 2), StatusLabel.GetTall());
		ypos += StatusLabel.GetTall() + YGAP_SMALL;

		StatusLabel.SetBounds(xpos, ypos, wide - (BORDER_GAP * 2), StatusLabel.GetTall());
		ypos += StatusLabel.GetTall() + YGAP_SMALL;

		panelList.Controls.SetPos(xpos, ypos);
		panelList.Controls.SetSize(wide - (BORDER_GAP * 2), tall - (ypos + BOTTOM_CONTROLS_HEIGHT));

		ypos = tall - BORDER_GAP;
		xpos = BORDER_GAP + VarsButton.GetWide() + DeleteButton.GetWide() + AddNewControlCombo.GetWide() + (XGAP * 2);

		ypos -= ApplyButton.GetTall();
		xpos -= ApplyButton.GetWide();
		ApplyButton.SetPos(xpos, ypos);

		xpos -= ExitButton.GetWide();
		xpos -= XGAP;
		ExitButton.SetPos(xpos, ypos);

		xpos -= SaveButton.GetWide();
		xpos -= XGAP;
		SaveButton.SetPos(xpos, ypos);

		xpos = BORDER_GAP;
		ypos -= YGAP_LARGE + Divider.GetTall();
		Divider.SetBounds(xpos, ypos, wide - (xpos + BORDER_GAP), 2);

		ypos -= YGAP_LARGE + VarsButton.GetTall();
		xpos = BORDER_GAP;

		EditableParents.SetPos(xpos, ypos);
		EditableChildren.SetPos(xpos + 150, ypos);

		ypos -= YGAP_LARGE + 18;
		xpos = BORDER_GAP;

		ReloadLocalization.SetPos(xpos, ypos);

		xpos += XGAP + ReloadLocalization.GetWide();

		PrevChild.SetPos(xpos, ypos);
		PrevChild.SetSize(64, ReloadLocalization.GetTall());
		xpos += XGAP + PrevChild.GetWide();

		NextChild.SetPos(xpos, ypos);
		NextChild.SetSize(64, ReloadLocalization.GetTall());

		ypos -= YGAP_LARGE + VarsButton.GetTall();
		xpos = BORDER_GAP;

		VarsButton.SetPos(xpos, ypos);
		xpos += XGAP + VarsButton.GetWide();
		DeleteButton.SetPos(xpos, ypos);
		xpos += XGAP + DeleteButton.GetWide();
		AddNewControlCombo.SetPos(xpos, ypos);
	}

	public void RemoveAllControls() => panelList.RemoveAll();

	public void OnTextKillFocus() {
		if (CurrentPanel != null)
			ApplyDataToControls();
	}

	private static ReadOnlySpan<char> ParseTokenFromString(ref ReadOnlySpan<char> s) {
		int i = 0;
		while (i < s.Length && !char.IsLetterOrDigit(s[i])) i++;

		int start = i;
		while (i < s.Length && char.IsLetterOrDigit(s[i])) i++;

		ReadOnlySpan<char> token = s.Slice(start, i - start);
		s = s[i..];

		return token;
	}

	public void SetActiveControl(Panel controlToEdit) {
		if (CurrentPanel == controlToEdit) {
			if (CurrentPanel != null)
				UpdateControlData(CurrentPanel);
			return;
		}

		CurrentPanel = controlToEdit;
		RemoveAllControls();
		panelList.Controls.MoveScrollBarToTop();

		if (CurrentPanel == null) {
			StatusLabel.SetText("[nothing currently selected]");
			StatusLabel.SetTextColorState(ColorState.Dull);
			RemoveAllControls();
			return;
		}

		ReadOnlySpan<char> controlDesc = GetDescription(); // TODO: Add missing description overrides

		int tabPosition = 1;
		while (true) {
			ReadOnlySpan<char> dataType = ParseTokenFromString(ref controlDesc);

			if (dataType.IsEmpty)
				break;

			int dt = TYPE_STRING;
			if (dataType.Equals("int", StringComparison.OrdinalIgnoreCase)) dt = TYPE_STRING;
			else if (dataType.Equals("alignment", StringComparison.OrdinalIgnoreCase)) dt = TYPE_ALIGNMENT;
			else if (dataType.Equals("autoresize", StringComparison.OrdinalIgnoreCase)) dt = TYPE_AUTORESIZE;
			else if (dataType.Equals("corner", StringComparison.OrdinalIgnoreCase)) dt = TYPE_CORNER;
			else if (dataType.Equals("localize", StringComparison.OrdinalIgnoreCase)) dt = TYPE_LOCALIZEDSTRING;

			ReadOnlySpan<char> fieldName = ParseTokenFromString(ref controlDesc);
			int itemHeight = 18;

			Label label = new(this, null, fieldName);
			label.SetSize(96, itemHeight);
			label.SetContentAlignment(Alignment.East);

			TextEntry? edit = null;
			ComboBox? editCombo = null;
			Button? editButton = null;

			if (dt == TYPE_ALIGNMENT) {
				editCombo = new(this, null, 9, false);
				editCombo.AddItem("north-west", null);
				editCombo.AddItem("north", null);
				editCombo.AddItem("north-east", null);
				editCombo.AddItem("west", null);
				editCombo.AddItem("center", null);
				editCombo.AddItem("east", null);
				editCombo.AddItem("south-west", null);
				editCombo.AddItem("south", null);
				editCombo.AddItem("south-east", null);

				edit = editCombo;
			}
			else if (dt == TYPE_AUTORESIZE) {
				editCombo = new(this, null, 4, false);
				editCombo.AddItem("0 - no auto-resize", null);
				editCombo.AddItem("1 - resize right", null);
				editCombo.AddItem("2 - resize down", null);
				editCombo.AddItem("3 - down & right", null);

				edit = editCombo;
			}
			else if (dt == TYPE_CORNER) {
				editCombo = new(this, null, 4, false);
				editCombo.AddItem("0 - top-left", null);
				editCombo.AddItem("1 - top-right", null);
				editCombo.AddItem("2 - bottom-left", null);
				editCombo.AddItem("3 - bottom-right", null);

				edit = editCombo;
			}
			else if (dt == TYPE_LOCALIZEDSTRING) {
				editButton = new(this, null, "...");
				editButton.SetParent(this);
				editButton.AddActionSignalTarget(this);
				editButton.SetTabPosition(tabPosition++);
				editButton.SetTall(itemHeight);
				// label.SetAssociatedControl(editButton);
			}
			else
				edit = new SmallTextEntry(this, null);

			if (edit != null) {
				edit.SetTall(itemHeight);
				edit.SetParent(this);
				edit.AddActionSignalTarget(this);
				edit.SetTabPosition(tabPosition++);
				// label.SetAssociatedControl(edit);
			}

			IFont smallFont = GetScheme()!.GetFont("DefaultVerySmall")!;

			label?.SetFont(smallFont);
			edit?.SetFont(smallFont);
			editCombo?.SetFont(smallFont);
			editButton?.SetFont(smallFont);

			panelList.AddItem(label, edit, editCombo, editButton, fieldName, (Type)dt);

			if (edit != null)
				panelList.Controls.AddItem(label, edit);
			else
				panelList.Controls.AddItem(label, editButton);
		}

		if (controlToEdit.IsBuildModeDeletable())
			DeleteButton.SetEnabled(true);
		else
			DeleteButton.SetEnabled(false);

		UpdateControlData(controlToEdit);

		// if (BuildGroup.GetResourceName())...

		ApplyButton.SetEnabled(false);
		InvalidateLayout();
		Repaint();
	}

	public void UpdateControlData(Panel control) {
		KeyValues dat = panelList.ResourceData!.FindKey(control.GetName(), true)!;
		control.GetSettings(dat);

		for (int i = 0; i < panelList.panelList.Count; i++) {
			ReadOnlySpan<char> name = panelList.panelList[i].Name;
			ReadOnlySpan<char> datString = dat.GetString(name, "");

			UpdateEditControl(panelList.panelList[i], datString);
		}

		Span<char> status = stackalloc char[512];
		sprintf(status, "%s: '%s'").S(control.GetClassName()).S(control.GetName());
		StatusLabel.SetText(status);
		StatusLabel.SetTextColorState(ColorState.Normal);
	}

	public void UpdateEditControl(PanelItem panelItem, ReadOnlySpan<char> datString) {
		switch ((Type)panelItem.Type) {
			case Type.AutoResize:
			case Type.Corner:
				// int dat = int.Parse(datString); // fixme: "" is here
				// panelItem.Combo!.ActivateItem(dat);
				break;
			case Type.LocalizedString:
				panelItem.EditButton!.SetText(datString);
				break;
			default:
				Span<char> buf = stackalloc char[512];
				datString.CopyTo(buf);//ansitounicode
				panelItem.EditPanel!.SetText(buf);
				break;
		}
	}

	public override void OnCommand(ReadOnlySpan<char> command) {

		base.OnCommand(command);
	}

	public void OnDeletePanel() {

	}

	public void ApplyDataToControls() {

	}

	public void StoreUndoSettings() {

	}

	public void DoUndo() {

	}

	public void DoCopy() {

	}

	public void DoPaste() {

	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		switch (message.Name) {
			case "ApplyDataToControls": ApplyDataToControls(); return;
			case "TextChanged": OnTextChanged((Panel)message.GetPtr("panel")!); return;
			case "DeletePanel": OnDeletePanel(); return;
			case "Undo": DoUndo(); return;
			case "Copy": DoCopy(); return;
			case "Paste": DoPaste(); return;
			case "EnableSaveButton": EnableSaveButton(); return;
			case "ShutdownBuildMode": Close(); return;
			case "PanelMoved": OnPanelMoved(); return;
			case "TextKillFocus": OnTextKillFocus(); return;
			case "CreateNewControl": OnCreateNewControl(message.GetString("text")); return;
			case "SetClipboardText": OnSetClipboardText(message.GetString("text")); return;
			case "OnChangeChild": OnChangeChild(message.GetInt("direction")); return;
		}
	}

	public KeyValues StoreSettings() {
		return new();
	}

	public override void OnKeyCodeTyped(ButtonCode code) {

	}

	public override void OnTextChanged(Panel panel) {
		if (panel == FileSelectionCombo) {
			Span<char> newFile = stackalloc char[512];
			FileSelectionCombo.GetText(newFile);

			// GetResourceName
		}

		if (panel == AddNewControlCombo) {
			// FIXME: Freezes program here (KV->FindKey)
			// Span<char> buf = stackalloc char[40];
			// AddNewControlCombo.GetText(buf);
			// if (!buf.Equals("None", StringComparison.OrdinalIgnoreCase)) {
			// 	OnCreateNewControl(buf);
			// 	AddNewControlCombo.ActivateItemByRow(0);
			// }
		}

		if (panel == EditableChildren) {
			KeyValues? kv = EditableChildren.GetActiveItemUserData();
			if (kv != null) {
				EditablePanel? ep = (EditablePanel?)kv.GetPtr("ptr");
				ep?.ActivateBuildMode();
			}
		}

		if (panel == EditableParents) {
			KeyValues? kv = EditableParents.GetActiveItemUserData();
			if (kv != null) {
				EditablePanel? ep = (EditablePanel?)kv.GetPtr("ptr");
				ep?.ActivateBuildMode();
			}
		}

		if (CurrentPanel != null && CurrentPanel.IsBuildModeEditable())
			ApplyButton.SetEnabled(true);

		if (AutoUpdate)
			ApplyDataToControls();
	}

	public void ExitBuildMode() {

	}

	public Panel OnNewControl(ReadOnlySpan<char> name, int x, int y) {
		return null!;
	}

	public void EnableSaveButton() => SaveButton.SetEnabled(true);

	public void RevertToSaved() {

	}

	public void ShowHelp() {

	}

	public void ShutdownBuildMode() {

	}

	public void OnPanelMoved() {

	}

	public void OnSetClipboardText(ReadOnlySpan<char> text) {

	}

	public void OnCreateNewControl(ReadOnlySpan<char> text) {

	}

	public void OnShowNewControlMenu() {

	}

	public void OnReloadLocalizaton() {

	}

	public override bool IsBuildGroupEnabled() => false; // Don't ever edit the actual build dialog!!!

	public void OnChangeChild(int direction) {
		Assert(direction == 1 || direction == -1);

		if (BuildGroup == null)
			return;

		Panel? current = CurrentPanel;
		Panel context = BuildGroup.GetContextPanel()!;

		if (current == null || current == context) {
			current = null;
			if (context.GetChildCount() > 0)
				current = context.GetChild(0);
		}
		else {
			int i;
			int children = context.GetChildCount();

			for (i = 0; i < children; i++) {
				if (context.GetChild(i) == current)
					break; Panel child = context.GetChild(i);
				if (child == current)
					break;
			}

			if (i < children) {
				for (int offset = 1; offset < children; offset++) {
					int test = (i + (direction * offset)) % children;
					if (test < 0)
						test += children;
					if (test == i)
						break;

					Panel check = context.GetChild(test);
					if (check is BuildModeDialog _)
						continue;

					current = check;
					break;
				}
			}
		}

		if (current == null)
			return;

		SetActiveControl(current);
	}
}
