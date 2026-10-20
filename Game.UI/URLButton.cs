using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public class URLButton : Label
{
	public static Panel Create_URLButton() => new URLButton(null, null, "Button");
	ButtonFlags ButtonFlags;
	int MouseClickMask;
	KeyValues? ActionMessage;
	ActivationType ActivationType;
	Color DefaultFgColor;
	Color DefaultBgColor;
	bool SelectionStateSaved;

	public URLButton(Panel parent, ReadOnlySpan<char> name, ReadOnlySpan<char> text, Panel? actionSignalTarget = null, string? cmd = null) : base(parent, name, text) {
		Init();

		if (actionSignalTarget != null && cmd != null) {
			AddActionSignalTarget(actionSignalTarget);
			SetCommand(cmd);
		}
	}

	public void Init() {
		ButtonFlags |= ButtonFlags.UseCaptureMouse | ButtonFlags.ButtonBorderEnabled;
		MouseClickMask = 0;
		ActionMessage = null;
		SelectionStateSaved = false;
		SetTextInset(0, 0);
		SetMouseClickEnabled(ButtonCode.MouseLeft, true);
		SetButtonActivationType(ActivationType.OnPressedAndReleased);

		SetPaintBackgroundEnabled(true);
	}

	public void SetButtonActivationType(ActivationType type) {
		ActivationType = type;
	}

	public void SetButtonBorderEnabled(bool state) {
		if (state != (0 != (ButtonFlags & ButtonFlags.ButtonBorderEnabled))) {
			ButtonFlags ^= ButtonFlags.ButtonBorderEnabled;
			InvalidateLayout(false);
		}
	}

	public virtual void SetSelected(bool state) {
		if ((ButtonFlags & ButtonFlags.Selected) != 0 != state) {
			if (state)
				ButtonFlags |= ButtonFlags.Selected;
			else
				ButtonFlags &= ~ButtonFlags.Selected;

			RecalculateDepressedState();
			InvalidateLayout(false);
		}
	}

	public void ForceDepressed(bool state) {
		if ((ButtonFlags & ButtonFlags.ForceDepressed) != 0 != state) {
			if (state) ButtonFlags |= ButtonFlags.ForceDepressed;
			else ButtonFlags &= ~ButtonFlags.ForceDepressed;

			RecalculateDepressedState();
			InvalidateLayout(false);
		}
	}

	private void RecalculateDepressedState() {
		bool newState;
		if (!IsEnabled())
			newState = false;
		else
			newState = (ButtonFlags & ButtonFlags.ForceDepressed) != 0
							|| ((ButtonFlags & ButtonFlags.Armed) != 0 && (ButtonFlags & ButtonFlags.Selected) != 0);

		if (newState)
			ButtonFlags |= ButtonFlags.Depressed;
		else
			ButtonFlags &= ~ButtonFlags.Depressed;
	}

	public void SetUseCaptureMouse(bool state) {
		if (state) ButtonFlags |= ButtonFlags.UseCaptureMouse;
		else ButtonFlags &= ~ButtonFlags.UseCaptureMouse;
	}

	public bool IsUseCaptureMouseEnabled() => (ButtonFlags & ButtonFlags.UseCaptureMouse) != 0;

	public void SetArmed(bool state) {
		if ((ButtonFlags & ButtonFlags.Armed) != 0 != state) {
			ButtonFlags ^= ButtonFlags.Armed;
			RecalculateDepressedState();
			InvalidateLayout(false);
		}
	}

	public bool IsArmed() => (ButtonFlags & ButtonFlags.Armed) != 0;

	[UiMessage("PressButton")]
	[UiMessage("Hotkey")]
	public virtual void DoClick() {
		SetSelected(true);
		FireActionSignal();
		SetSelected(false);
	}

	public bool IsSelected() => (ButtonFlags & ButtonFlags.Selected) != 0;
	public bool IsDepressed() => (ButtonFlags & ButtonFlags.Depressed) != 0;
	public bool IsDrawingFocusBox() => (ButtonFlags & ButtonFlags.DrawFocusBox) != 0;
	public void DrawFocusBox() => ButtonFlags |= ButtonFlags.DrawFocusBox;

	public override void Paint() {
		base.Paint();

		GetSize(out _, out int controlHeight);
		GetContentSize(out int textWidth, out _);
		int x = textWidth;
		int y = controlHeight - 4;

		Surface.DrawSetColor(GetButtonFgColor());
		Surface.DrawLine(0, y, x, y);
	}

	public override void PerformLayout() {
		SetFgColor(GetButtonFgColor());
		SetBgColor(GetButtonBgColor());

		base.PerformLayout();
	}

	public virtual Color GetButtonFgColor() => DefaultFgColor;
	public Color GetButtonBgColor() => DefaultBgColor;

	public override void OnSetFocus() {
		InvalidateLayout(false);
		base.OnSetFocus();
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		DefaultFgColor = GetSchemeColor("Button.TextColor", new(255, 255, 255, 255), scheme);
		DefaultBgColor = GetSchemeColor("Button.BgColor", new(0, 0, 0, 255), scheme);
		InvalidateLayout();
	}

	public void SetMouseClickEnabled(ButtonCode code, bool state) {
		if (state)
			MouseClickMask |= unchecked(1 << unchecked((int)(code + 1)));
		else
			MouseClickMask &= ~unchecked(1 << unchecked((int)(code + 1)));
	}

	public void SetCommand(ReadOnlySpan<char> command) => SetCommand(new KeyValues("Command", "command", command));
	public void SetCommand(KeyValues command) {
		ActionMessage = null;
		ActionMessage = command;
	}
	public KeyValues? GetCommand() => ActionMessage;

	public virtual void FireActionSignal() {
		if (ActionMessage == null)
			return;

		// url todo

		PostActionSignal(ActionMessage.MakeCopy());
	}

	public override bool RequestInfo(KeyValues outputData) {
		if (string.Equals(outputData.Name, "GetState", StringComparison.OrdinalIgnoreCase)) {
			outputData.SetInt("state", IsSelected() ? 1 : 0);
			return true;
		}

		if (string.Equals(outputData.Name, "GetCommand", StringComparison.OrdinalIgnoreCase)) {
			if (ActionMessage != null)
				outputData.SetString("command", ActionMessage.GetString("command", ""));
			else
				outputData.SetString("command", "");
			return true;
		}

		return base.RequestInfo(outputData);
	}

	public override void GetSettings(KeyValues outResourceData) {
		base.GetSettings(outResourceData);

		if (ActionMessage != null)
			outResourceData.SetString("command", ActionMessage.GetString("command", ""));

		outResourceData.SetInt("default", (ButtonFlags & ButtonFlags.DefaultButton) != 0 ? 1 : 0);

		if (SelectionStateSaved)
			outResourceData.SetInt("selected", IsSelected() ? 1 : 0);
	}

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);

		ReadOnlySpan<char> cmd = resourceData.GetString("command", "");
		if (cmd.Length > 0)
			SetCommand(cmd);

		int selected = resourceData.GetInt("selected", -1);
		if (selected != -1) {
			SetSelected(selected != 0);
			SelectionStateSaved = true;
		}
	}

	[UiMessage("SetState")]
	public virtual void OnSetState(int state) {
		SetSelected(state != 0);
		Repaint();
	}

	public override void OnCursorEntered() {
		if (IsEnabled())
			SetArmed(true);
	}

	public override void OnCursorExited() {
		if ((ButtonFlags & ButtonFlags.ButtonKeyDown) == 0)
			SetArmed(false);
	}

	public override void OnMousePressed(ButtonCode code) {
		if (!IsEnabled())
			return;

		if (ActivationType == ActivationType.OnPressed) {
			if (IsKeyboardInputEnabled())
				RequestFocus();
			DoClick();
			return;
		}

		if (IsUseCaptureMouseEnabled() && ActivationType == ActivationType.OnPressedAndReleased) {
			if (IsKeyboardInputEnabled())
				RequestFocus();
			SetSelected(true);
			Repaint();
			Input.SetMouseCapture(this);
		}
	}

	public override void OnMouseDoublePressed(ButtonCode code) => OnMousePressed(code);

	public override void OnMouseReleased(ButtonCode code) {
		if (IsUseCaptureMouseEnabled())
			Input.SetMouseCapture(null);

		if (ActivationType == ActivationType.OnPressed)
			return;

		if (!IsSelected() && ActivationType == ActivationType.OnPressedAndReleased)
			return;

		if (IsEnabled() && (this == Input.GetMouseOver() || (ButtonFlags & ButtonFlags.ButtonKeyDown) != 0))
			DoClick();
		else
			SetSelected(false);

		Repaint();
	}

	public override void OnKeyCodePressed(ButtonCode code) {
		if (code == ButtonCode.KeySpace || code == ButtonCode.KeyEnter) {
			SetArmed(true);
			ButtonFlags |= ButtonFlags.ButtonKeyDown;
			OnMousePressed(ButtonCode.MouseLeft);
			if (IsUseCaptureMouseEnabled())
				Input.SetMouseCapture(null);
		}
		else {
			ButtonFlags &= ~ButtonFlags.ButtonKeyDown;
			base.OnKeyCodePressed(code);
		}
	}

	public override void OnKeyCodeReleased(ButtonCode code) {
		if ((0 != (ButtonFlags & ButtonFlags.ButtonKeyDown)) && (code == ButtonCode.KeySpace || code == ButtonCode.KeyEnter)) {
			SetArmed(true);
			OnMouseReleased(ButtonCode.MouseLeft);
		}
		else
			base.OnKeyCodeReleased(code);

		ButtonFlags &= ~ButtonFlags.ButtonKeyDown;
		SetArmed(false);
	}

	public override void SizeToContents() {
		GetContentSize(out int wide, out int tall);
		SetSize(wide + Content, tall + Content);
	}
}
