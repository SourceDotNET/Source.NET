using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public class EditablePanel : Panel
{
	public static Panel Create_EditablePanel() => new EditablePanel(null, null, null);

	public FocusNavGroup GetFocusNavGroup() => NavGroup;
	BuildGroup BuildGroup;
	readonly FocusNavGroup NavGroup;
	KeyValues? DialogVariables;
	string? ConfigName;
	int ConfigID;
	bool ShouldSkipAutoResize;

	public EditablePanel(Panel? parent, ReadOnlySpan<char> panelName) : base(parent, panelName) {
		BuildGroup = new BuildGroup(this, this);
		ConfigName = null;
		ConfigID = 0;
		DialogVariables = null;
		ShouldSkipAutoResize = false;
		NavGroup = new FocusNavGroup(this);

		SetBuildGroup(GetBuildGroup());
	}

	public EditablePanel(Panel? parent, ReadOnlySpan<char> panelName, IScheme scheme) : base(parent, panelName, scheme) {
		BuildGroup = new BuildGroup(this, this);
		ConfigName = null;
		ConfigID = 0;
		DialogVariables = null;
		ShouldSkipAutoResize = false;
		NavGroup = new FocusNavGroup(this);

		SetBuildGroup(GetBuildGroup());
	}

	public virtual BuildGroup? GetBuildGroup() {
		return BuildGroup;
	}

	public override bool RequestInfo(KeyValues outputData) {
		if (outputData.Name.Equals("BuildDialog", StringComparison.OrdinalIgnoreCase)) {
			outputData.SetPtr("PanelPtr", new BuildModeDialog((BuildGroup)outputData.GetPtr("BuildGroupPtr")!));
			return true;
		}
		else if (outputData.Name.Equals("ControlFactory")) {
			Panel? newPanel = CreateControlByName(outputData.GetString("ControlName"));
			if (newPanel != null) {
				outputData.SetPtr("PanelPtr", newPanel);
				return true;
			}
		}

		return base.RequestInfo(outputData);
	}

	public virtual Panel? CreateControlByName(ReadOnlySpan<char> controlName) => InstancePanel(controlName);

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);
		BuildGroup.ApplySettings(resourceData);
	}

	public virtual void ActivateBuildMode() {
		BuildGroup.SetEnabled(true);
	}

	public virtual void LoadControlSettings(ReadOnlySpan<char> resourceName, ReadOnlySpan<char> pathID = default, KeyValues? keyValues = null, KeyValues? conditions = null) {
		if (!fileSystem.FileExists(resourceName))
			Msg($"Resource file \"{resourceName}\" not found on disk!\n");

		BuildGroup.LoadControlSettings(resourceName, pathID, keyValues, conditions);
		ForceSubPanelsToUpdateWithNewDialogVariables();
		InvalidateLayout();
	}

	public void LoadUserConfig(ReadOnlySpan<char> configName, int dialogID = 0) {
		KeyValues? data = system.GetUserConfigFileData(configName, dialogID);
		ConfigName = configName.ToString();
		ConfigID = dialogID;

		if (data != null)
			ApplyUserConfigSettings(data);
	}

	public void SaveUserConfig() {
		if (ConfigName == null)
			return;

		KeyValues? data = system.GetUserConfigFileData(ConfigName, ConfigID);
		if (data != null)
			GetUserConfigSettings(data);
	}

	public void ApplyUserConfigSettings(KeyValues userConfig) {
		for (int i = 0; i < GetChildCount(); i++) {
			//Panel child = GetChild(i);
			// if (child.HasUserConfigSettings()) {
			// 	ReadOnlySpan<char> childName = child.GetName();
			// 	if (childName.IsEmpty)
			// 		continue;

			// 	child.ApplyUserConfigSettings(userConfig.FindKey(childName, true));
			// }
		}
	}

	public void GetUserConfigSettings(KeyValues userConfig) {
		for (int i = 0; i < GetChildCount(); i++) {
			Panel child = GetChild(i);
			// if (child.HasUserConfigSettings()) {
			// 	ReadOnlySpan<char> childName = child.GetName();
			// 	if (childName.IsEmpty)
			// 		continue;

			// 	child.GetUserConfigSettings(userConfig.FindKey(childName, true));
			// }
		}
	}

	public override void OnClose() => SaveUserConfig();

	private void ForceSubPanelsToUpdateWithNewDialogVariables() {
		if (DialogVariables == null)
			return;

		SendMessage(DialogVariables, this);
		for (int i = 0; i < GetChildCount(); i++) {
			Panel child = GetChild(i);
			child.SendMessage(DialogVariables, this);
		}
	}

	static ConVarRef vgui_nav_lock_default_button = new("vgui_nav_lock_default_button");

	public override void OnKeyCodePressed(ButtonCode code) {
		if (vgui_nav_lock_default_button.GetInt() == 0) {
			ButtonCode nButtonCode = code.GetBaseButtonCode();

			IPanel? panel = GetFocusNavGroup().GetCurrentDefaultButton();
			if (panel != null && !IsConsoleStylePanel()) {
				switch (nButtonCode) {
					case ButtonCode.KeyEnter:
						if (panel.IsVisible() && panel.IsEnabled()) {
							PostMessage(panel, new KeyValues("Hotkey"));
							return;
						}
						break;
				}
			}
		}

		if (!PassUnhandledInput)
			return;

		base.OnKeyCodePressed(code);
	}
	public override void OnChildAdded(IPanel child) {
		base.OnChildAdded(child);

		Panel? panel = (Panel?)child;
		if (panel != null) {
			panel.SetBuildGroup(BuildGroup);
			panel.AddActionSignalTarget(this);
		}
	}

	public override IPanel? GetCurrentKeyFocus() {
		Panel? focus = NavGroup.GetCurrentFocus();
		if (focus == this)
			return null;

		if (focus != null) {
			if (focus.IsPopup())
				return base.GetCurrentKeyFocus();

			IPanel? subFocus = focus.GetCurrentKeyFocus();
			if (subFocus != null)
				return subFocus;

			return focus;
		}

		return base.GetCurrentKeyFocus();
	}

	public override void OnRequestFocus(Panel subFocus, Panel? defaultPanel) {
		if (!subFocus.IsPopup())
			defaultPanel = NavGroup.SetCurrentFocus(subFocus, defaultPanel);

		base.OnRequestFocus(subFocus, defaultPanel);
	}

	public override bool RequestFocusNext(IPanel? existingPanel = null) {
		bool ret = NavGroup.RequestFocusNext((Panel?)existingPanel);
		// if (IsPC() && !ret && IsConsoleStylePanel())
		// 	NavigateUp();
		return ret;
	}

	public override bool RequestFocusPrev(IPanel? existingPanel = null) {
		bool ret = NavGroup.RequestFocusPrev((Panel?)existingPanel);
		// if (IsPC() && !ret && IsConsoleStylePanel())
		// 	NavigateDown();
		return ret;
	}

	public void SetControlEnabled(ReadOnlySpan<char> controlName, bool enabled, bool recurseDown = false) {
		Panel? control = FindChildByName(controlName, recurseDown);
		control?.SetEnabled(enabled);
	}

	public void SetControlVisible(ReadOnlySpan<char> controlName, bool visible, bool recurseDown = false) {
		Panel? control = FindChildByName(controlName, recurseDown);
		control?.SetVisible(visible);
	}

	public void SetControlString(ReadOnlySpan<char> controlName, ReadOnlySpan<char> str) {
		Panel? control = FindChildByName(controlName, false);
		if (control != null) {
			if (str[0] == '#') {
				ReadOnlySpan<char> text = Localize.Find(str);
				if (!text.IsEmpty)
					PostMessage(control, new KeyValues("SetText", "text", text));
#if DEBUG
				else
					PostMessage(control, new KeyValues("SetText", "text", str));
#endif
			}
			else
				PostMessage(control, new KeyValues("SetText", "text", str));
		}
	}

	public void SetControlInt(ReadOnlySpan<char> controlName, int value) {
		Panel? control = FindChildByName(controlName, false);
		if (control != null)
			PostMessage(control, new KeyValues("SetInt", "value", value));
	}

	public int GetControlInt(ReadOnlySpan<char> controlName, int defaultState) {
		Panel? control = FindChildByName(controlName, false);
		if (control != null) {
			KeyValues data = new("GetState");
			if (control.RequestInfo(data))
				return data.GetInt("state", defaultState);
		}

		return defaultState;
	}

	public void GetControlString(ReadOnlySpan<char> controlName, Span<char> buf, ReadOnlySpan<char> defaultStr) {
		Panel? control = FindChildByName(controlName, false);
		if (control != null) {
			KeyValues data = new("GetText");
			if (control.RequestInfo(data)) {
				ReadOnlySpan<char> text = data.GetString("text");
				text.CopyTo(buf);
				return;
			}
		}

		defaultStr.CopyTo(buf);
	}

	public void SetDialogVariable(ReadOnlySpan<char> varName, ReadOnlySpan<char> value) {
		GetDialogVariables().SetString(varName, value);
		ForceSubPanelsToUpdateWithNewDialogVariables();
	}

	public void SetDialogVariable(ReadOnlySpan<char> varName, int value) {
		GetDialogVariables().SetInt(varName, value);
		ForceSubPanelsToUpdateWithNewDialogVariables();
	}

	public void SetDialogVariable(ReadOnlySpan<char> varName, float value) {
		GetDialogVariables().SetFloat(varName, value);
		ForceSubPanelsToUpdateWithNewDialogVariables();
	}

	public override void OnSizeChanged(int newWide, int newTall) {
		base.OnSizeChanged(newWide, newTall);
		InvalidateLayout();

		for (int i = 0; i < GetChildCount(); i++) {
			Panel? child = GetChild(i);
			if (child == null)
				continue;

			child.GetBounds(out int x, out int y, out int w, out int h);

			child.GetPinOffset(out int px, out int py);

			child.GetResizeOffset(out int ox, out int oy);

			int ex;
			int ey;

			AutoResize resize = child.GetAutoResize();
			bool resizeHoriz = resize == AutoResize.Right || resize == AutoResize.DownAndRight;
			bool resizeVert = resize == AutoResize.Down || resize == AutoResize.DownAndRight;

			if (!ShouldSkipAutoResize) {
				PinCorner pinCorner = child.GetPinCorner();
				if (pinCorner == PinCorner.TopRight || pinCorner == PinCorner.BottomRight) {
					ex = newWide + px;
					x = resizeHoriz ? ox : ex - w;
				}
				else {
					x = px;
					ex = resizeHoriz ? newWide + ox : px + w;
				}

				if (pinCorner == PinCorner.BottomLeft || pinCorner == PinCorner.BottomRight) {
					ey = newTall + py;
					y = resizeVert ? oy : ey - h;
				}
				else {
					y = py;
					ey = resizeVert ? newTall + oy : py + h;
				}

				if (ex < x)
					ex = x;
				if (ey < y)
					ey = y;

				child.SetBounds(x, y, ex - x, ey - y);
				child.InvalidateLayout();
			}
		}

		Repaint();
	}

	public override void RequestFocus(int direction = 0) {
		if (direction == 1)
			RequestFocusNext();
		else if (direction == -1)
			RequestFocusPrev();
		else
			base.RequestFocus(direction);
	}

	public KeyValues GetDialogVariables() => DialogVariables ??= new KeyValues("DialogVariables");

	private void OnCurrentDefaultButtonSet(Panel defaultButton) {
		NavGroup.SetCurrentDefaultButton(defaultButton, false);

		if (GetParent() != null) {
			KeyValues msg = new("CurrentDefaultButtonSet");
			msg.SetPtr("button", defaultButton);
			PostMessage(GetParent()!, msg);
		}
	}

	private void OnDefaultButtonSet(Panel defaultButton) => NavGroup.SetDefaultButton(defaultButton);

	static readonly KeyValues KV_FindDefaultButton = new("FindDefaultButton");
	private void OnFindDefaultButton() {
		if (NavGroup.GetDefaultButton() != null)
			NavGroup.SetCurrentDefaultButton(NavGroup.GetDefaultButton());
		else if (GetParent() != null) {
			PostMessage(GetParent()!, KV_FindDefaultButton);
		}
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		switch (message.Name) {
			case "DefaultButtonSet":
				OnDefaultButtonSet((Panel)message.GetPtr("button")!);
				break;
			case "CurrentDefaultButtonSet":
				OnCurrentDefaultButtonSet((Panel)message.GetPtr("button")!);
				break;
			case "FindDefaultButton":
				OnFindDefaultButton();
				break;
			default:
				base.OnMessage(message, sender);
				break;
		}
	}
}

