using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public enum ActivationType
{
	OnPressedAndReleased,
	OnPressed,
	OnReleased
}

public enum ButtonFlags
{
	Armed = 0x0001,
	Depressed = 0x0002,
	ForceDepressed = 0x0004,
	ButtonBorderEnabled = 0x0008,
	UseCaptureMouse = 0x0010,
	ButtonKeyDown = 0x0020,
	DefaultButton = 0x0040,
	Selected = 0x0080,
	DrawFocusBox = 0x0100,
	Blink = 0x0200,
	AllFlags = 0xFFFF
}

public class Button : Label
{
	public static Panel Create_Button() => new Button(null, null, "Button");
	KeyValues? ActionMessage;
	ActivationType ActivationType;
	ButtonFlags ButtonFlags;
	bool SelectionStateSaved;
	int MouseClickMask;
	bool StayArmedOnClick;
	bool StaySelectedOnClick;
	string? ArmedSoundName;
	string? DepressedSoundName;
	string? ReleasedSoundName;

	public void Init() {
		ButtonFlags |= ButtonFlags.UseCaptureMouse | ButtonFlags.ButtonBorderEnabled;
		MouseClickMask = 0;
		ActionMessage = null;
		SelectionStateSaved = false;
		StaySelectedOnClick = false;
		StayArmedOnClick = false;
		ArmedSoundName = null;
		DepressedSoundName = null;
		ReleasedSoundName = null;
		SetTextInset(QuickPropScale(6), 0);
		SetMouseClickEnabled(ButtonCode.MouseLeft, true);
		SetButtonActivationType(ActivationType.OnPressedAndReleased);

		SetPaintBackgroundEnabled(true);

		paint = true;

		RegisterColorAsOverridable(DefaultFgColor, "defaultFgColor_override");
		RegisterColorAsOverridable(DefaultBgColor, "defaultBgColor_override");
		RegisterColorAsOverridable(ArmedFgColor, "armedFgColor_override");
		RegisterColorAsOverridable(ArmedBgColor, "armedBgColor_override");
		RegisterColorAsOverridable(DepressedFgColor, "depressedFgColor_override");
		RegisterColorAsOverridable(DepressedBgColor, "depressedBgColor_override");
		RegisterColorAsOverridable(SelectedFgColor, "selectedFgColor_override");
		RegisterColorAsOverridable(SelectedBgColor, "selectedBgColor_override");
		RegisterColorAsOverridable(KeyboardFocusColor, "keyboardFocusColor_override");
		RegisterColorAsOverridable(BlinkFgColor, "blinkFgColor_override");
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

	public void SetMouseClickEnabled(ButtonCode code, bool state) {
		if (state)
			MouseClickMask |= unchecked(1 << unchecked((int)(code + 1)));
		else
			MouseClickMask &= ~unchecked(1 << unchecked((int)(code + 1)));
	}

	public Button(Panel? parent, ReadOnlySpan<char> name, ReadOnlySpan<char> text, Panel? actionSignalTarget = null, string? cmd = null) : base(parent, name, text) {
		Init();

		if (actionSignalTarget != null && cmd != null) {
			AddActionSignalTarget(actionSignalTarget);
			SetCommand(cmd);
		}
	}

	[UiMessage("PressButton")]
	[UiMessage("Hotkey")]
	public virtual void DoClick() {
		SetSelected(true);
		FireActionSignal();
		PlayButtonReleasedSound();

		// vgui_nav_lock?

		if (!StaySelectedOnClick)
			SetSelected(false);
#if GMOD_DLL
		if (PushLuaHook(LUA_POOLEDSTRING.DoClick)) {
			Input.GetCursorPos(out int x, out int y);
			ScreenToLocal(ref x, ref y);
			PushLua(Lua!, LuaType.Panel);
			Lua!.PushNumber(x);
			Lua!.PushNumber(y);
			Lua!.CallInternalNoReturns(3);
		}
#endif
	}

	public void SetArmedSound(ReadOnlySpan<char> fileName) {
		ArmedSoundName = string.Intern(new(fileName));
	}
	public void SetDepressedSound(ReadOnlySpan<char> fileName) {
		DepressedSoundName = string.Intern(new(fileName));
	}
	public void SetReleasedSound(ReadOnlySpan<char> fileName) {
		ReleasedSoundName = string.Intern(new(fileName));
	}

	public virtual void FireActionSignal() {
		if (ActionMessage != null) {
			// TODO: URL messages?
			PostActionSignal(ActionMessage.MakeCopy());
		}
	}

	public void PlayButtonReleasedSound() {
		if (ReleasedSoundName != null)
			Surface.PlaySound(ReleasedSoundName);
	}

	public override void PerformLayout() {
		SetBorder(GetBorder((ButtonFlags & ButtonFlags.Depressed) != 0, (ButtonFlags & ButtonFlags.Armed) != 0, (ButtonFlags & ButtonFlags.Selected) != 0, HasFocus()));

		SetFgColor(GetButtonFgColor());
		SetBgColor(GetButtonBgColor());

		base.PerformLayout();
	}

	public virtual IBorder? GetBorder(bool depressed, bool armed, bool selected, bool keyfocus) {
		if (0 != (ButtonFlags & ButtonFlags.ButtonBorderEnabled)) {
			if (depressed)
				return DepressedBorder;

			if (keyfocus)
				return KeyFocusBorder;

			if (IsEnabled() && 0 != (ButtonFlags & ButtonFlags.DefaultButton))
				return KeyFocusBorder;

			return DefaultBorder;
		}
		else {
			if (depressed)
				return DepressedBorder;

			if (armed)
				return DefaultBorder;
		}

		return DefaultBorder;
	}

	public virtual Color GetButtonFgColor() {
		if (0 == (ButtonFlags & ButtonFlags.Blink)) {
			if (0 != (ButtonFlags & ButtonFlags.Depressed))
				return DepressedFgColor;
			if (0 != (ButtonFlags & ButtonFlags.Armed))
				return ArmedFgColor;
			if (0 != (ButtonFlags & ButtonFlags.Selected))
				return SelectedFgColor;
			return DefaultFgColor;
		}

		Color blended;

		if (0 != (ButtonFlags & ButtonFlags.Depressed))
			blended = DepressedFgColor;
		else if (0 != (ButtonFlags & ButtonFlags.Armed))
			blended = ArmedFgColor;
		else if (0 != (ButtonFlags & ButtonFlags.Selected))
			blended = SelectedFgColor;
		else
			blended = DefaultFgColor;

		float fBlink = (MathF.Sin(system.GetTimeMillis() * 0.01f) + 1.0f) * 0.5f;

		if (0 != (ButtonFlags & ButtonFlags.Blink)) {
			blended[0] = (byte)Math.Clamp(blended[0] * fBlink + (float)BlinkFgColor[0] * (1.0f - fBlink), 0, 255);
			blended[1] = (byte)Math.Clamp(blended[1] * fBlink + (float)BlinkFgColor[1] * (1.0f - fBlink), 0, 255);
			blended[2] = (byte)Math.Clamp(blended[2] * fBlink + (float)BlinkFgColor[2] * (1.0f - fBlink), 0, 255);
			blended[3] = (byte)Math.Clamp(blended[3] * fBlink + (float)BlinkFgColor[3] * (1.0f - fBlink), 0, 255);
		}

		return blended;
	}

	public Color GetButtonArmedBgColor() => ArmedBgColor;

	public virtual Color GetButtonBgColor() {
		if (0 != (ButtonFlags & ButtonFlags.Depressed))
			return DepressedBgColor;
		if (0 != (ButtonFlags & ButtonFlags.Armed))
			return ArmedBgColor;
		if (0 != (ButtonFlags & ButtonFlags.Selected))
			return SelectedBgColor;
		return DefaultBgColor;
	}

	public override void Paint() {
		if (!ShouldPaint())
			return;

		base.Paint();

		if (HasFocus() && IsEnabled() && IsDrawingFocusBox()) {
			GetSize(out int wide, out int tall);
			DrawFocusBorder(3, 3, wide - 4, tall - 2);
		}
	}

	[UiMessage("SetState")]
	public virtual void OnSetState(int state) {
		SetSelected(state != 0);
		Repaint();
	}

	public override void OnSetFocus() {
#if GMOD_DLL
		// todo: OnGetFocus hook
#endif
		InvalidateLayout(false);
		base.OnSetFocus();
	}

	public override void OnKillFocus(Panel? newPanel) {
#if GMOD_DLL
		// todo: OnLoseFocus hook
#endif
		InvalidateLayout(false);
		base.OnKillFocus(newPanel);
	}

	public override void OnMousePressed(ButtonCode code) {
#if GMOD_DLL
		// todo: OnMousePressed hook
#endif
		if (!IsEnabled())
			return;

		if (!IsMouseClickEnabled(code))
			return;

		if (ActivationType == ActivationType.OnPressed) {
			if (IsKeyboardInputEnabled())
				RequestFocus();
			DoClick();
			return;
		}

		if (DepressedSoundName != null)
			Surface.PlaySound(DepressedSoundName);

		if (IsUseCaptureMouseEnabled() && ActivationType == ActivationType.OnPressedAndReleased) {
			if (IsKeyboardInputEnabled())
				RequestFocus();

			SetSelected(true);
			Repaint();

			Input.SetMouseCapture(this);
		}
	}

	public override void OnMouseDoublePressed(ButtonCode code) {
		OnMousePressed(code);
	}

	public override void OnMouseReleased(ButtonCode code) {
#if GMOD_DLL
		// todo: OnMouseReleased hook
#endif
		if (IsUseCaptureMouseEnabled())
			Input.SetMouseCapture(null);

		if (ActivationType == ActivationType.OnPressed)
			return;

		if (!IsMouseClickEnabled(code))
			return;

		if (!IsSelected() && ActivationType == ActivationType.OnPressedAndReleased)
			return;

		if (IsEnabled() && (this == Input.GetMouseOver() || (ButtonFlags & ButtonFlags.ButtonKeyDown) != 0))
			DoClick();
		else if (!StaySelectedOnClick)
			SetSelected(false);

		Repaint();
	}

	public override void OnCursorEntered() {
		if (IsEnabled() && !IsSelected())
			SetArmed(true);
	}

	public override void OnCursorExited() {
		if ((ButtonFlags & ButtonFlags.ButtonKeyDown) == 0 && !IsSelected())
			SetArmed(false);
	}

	public virtual void SetSelected(bool state) {
#if GMOD_DLL
		// todo: Selected = state
#endif
		if (((ButtonFlags & ButtonFlags.Selected) != 0) != state) {
			if (state)
				ButtonFlags |= ButtonFlags.Selected;
			else
				ButtonFlags &= ~ButtonFlags.Selected;

			RecalculateDepressedState();
			InvalidateLayout(false);
		}

		if (!StayArmedOnClick && state && (ButtonFlags & ButtonFlags.Armed) != 0) {
			ButtonFlags &= ~ButtonFlags.Armed;
			InvalidateLayout(false);
		}
	}

	public bool IsSelected() {
		return (ButtonFlags & ButtonFlags.Selected) != 0;
	}

	public bool IsMouseClickEnabled(ButtonCode code) {
		return (MouseClickMask & unchecked(1 << unchecked((int)(code + 1)))) != 0;
	}

	public void SetBlink(bool state) {
		if (((ButtonFlags & ButtonFlags.Blink) != 0) != state) {
			if (state) ButtonFlags |= ButtonFlags.Blink;
			else ButtonFlags &= ~ButtonFlags.Blink;

			RecalculateDepressedState();
			InvalidateLayout(false);
		}
	}

	public void ForceDepressed(bool state) {
		if (((ButtonFlags & ButtonFlags.ForceDepressed) != 0) != state) {
			if (state) ButtonFlags |= ButtonFlags.ForceDepressed;
			else ButtonFlags &= ~ButtonFlags.ForceDepressed;

			RecalculateDepressedState();
			InvalidateLayout(false);
		}
	}

	public bool IsUseCaptureMouseEnabled() => (ButtonFlags & ButtonFlags.UseCaptureMouse) != 0;
	public void SetUseCaptureMouse(bool state) {
		if (state) ButtonFlags |= ButtonFlags.UseCaptureMouse;
		else ButtonFlags &= ~ButtonFlags.UseCaptureMouse;
	}
	public bool IsArmed() => (ButtonFlags & ButtonFlags.Armed) != 0;

	public void SetArmed(bool state) {
#if GMOD_DLL
		// todo: Armed = state
#endif
		if (((ButtonFlags & ButtonFlags.Armed) != 0) != state) {
			ButtonFlags ^= ButtonFlags.Armed;
			RecalculateDepressedState();
			InvalidateLayout(false);
			if (state && ArmedSoundName != null)
				Surface.PlaySound(ArmedSoundName);
		}
	}

	private void RecalculateDepressedState() {
		bool newState;
		if (!IsEnabled())
			newState = false;
		else {
			if (StaySelectedOnClick && (ButtonFlags & ButtonFlags.Selected) != 0)
				newState = false;
			else
				newState = (ButtonFlags & ButtonFlags.ForceDepressed) != 0
								|| ((ButtonFlags & ButtonFlags.Armed) != 0 && (ButtonFlags & ButtonFlags.Selected) != 0);
		}

		if (newState)
			ButtonFlags |= ButtonFlags.Depressed;
		else
			ButtonFlags &= ~ButtonFlags.Depressed;
	}

	public KeyValues? GetCommand() => ActionMessage;
	public void SetCommand(ReadOnlySpan<char> command) => SetCommand(new KeyValues("Command", "command", command));
	public void SetCommand(KeyValues command) {
		ActionMessage = null;
		ActionMessage = command;
	}

	Color DefaultFgColor, DefaultBgColor;
	Color ArmedFgColor, ArmedBgColor;
	public Color SelectedFgColor, SelectedBgColor;
	Color DepressedFgColor, DepressedBgColor;
	Color BlinkFgColor;
	Color KeyboardFocusColor;

	IBorder? DefaultBorder, DepressedBorder, KeyFocusBorder;

	public override void ApplySchemeSettings(IScheme scheme) {
#if GMOD_DLL
		// todo: ApplySchemeSettings hook
#endif
		base.ApplySchemeSettings(scheme);
		DefaultBorder = scheme.GetBorder("ButtonBorder");
		DepressedBorder = scheme.GetBorder("ButtonDepressedBorder");
		KeyFocusBorder = scheme.GetBorder("ButtonKeyFocusBorder");

		DefaultFgColor = GetSchemeColor("Button.TextColor", new(255, 255, 255, 255), scheme);
		DefaultBgColor = GetSchemeColor("Button.BgColor", new(0, 0, 0, 255), scheme);

		ArmedFgColor = GetSchemeColor("Button.ArmedTextColor", DefaultFgColor, scheme);
		ArmedBgColor = GetSchemeColor("Button.ArmedBgColor", DefaultBgColor, scheme);

		SelectedFgColor = GetSchemeColor("Button.SelectedTextColor", SelectedFgColor, scheme);
		SelectedBgColor = GetSchemeColor("Button.SelectedBgColor", SelectedBgColor, scheme);

		DepressedFgColor = GetSchemeColor("Button.DepressedTextColor", DefaultFgColor, scheme);
		DepressedBgColor = GetSchemeColor("Button.DepressedBgColor", DefaultBgColor, scheme);
		KeyboardFocusColor = GetSchemeColor("Button.FocusBorderColor", new(0, 0, 0, 255), scheme);

		BlinkFgColor = GetSchemeColor("Button.BlinkColor", new(255, 155, 0, 255), scheme);
		InvalidateLayout();
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

		int DefaultButton = resourceData.GetInt("default");
		if ((DefaultButton & 1) != 0 && CanBeDefaultButton())
			SetAsDefaultButton(true);

		int selected = resourceData.GetInt("selected", -1);
		if (selected != -1) {
			SetSelected(selected != 0);
			SelectionStateSaved = true;
		}

		StaySelectedOnClick = resourceData.GetBool("stayselectedonclick", false);
		StayArmedOnClick = resourceData.GetBool("stay_armed_on_click", false);

		ReadOnlySpan<char> sound = resourceData.GetString("sound_armed", "");
		if (sound.Length > 0)
			SetArmedSound(sound);

		sound = resourceData.GetString("sound_depressed", "");
		if (sound.Length > 0)
			SetDepressedSound(sound);

		sound = resourceData.GetString("sound_released", "");
		if (sound.Length > 0)
			SetReleasedSound(sound);

		ActivationType = (ActivationType)resourceData.GetInt("button_activation_type", (int)ActivationType.OnReleased);
	}

	public override bool RequestInfo(KeyValues outputData) {
		if (outputData.Name.Equals("CanBeDefaultButton", StringComparison.OrdinalIgnoreCase)) {
			outputData.SetInt("result", CanBeDefaultButton() ? 1 : 0);
			return true;
		}

		if (outputData.Name.Equals("GetState", StringComparison.OrdinalIgnoreCase)) {
			outputData.SetInt("state", IsSelected() ? 1 : 0);
			return true;
		}

		if (outputData.Name.Equals("GetCommand", StringComparison.OrdinalIgnoreCase)) {
			if (ActionMessage != null)
				outputData.SetString("command", ActionMessage.GetString("command", ""));
			else
				outputData.SetString("command", "");
			return true;
		}

		return base.RequestInfo(outputData);
	}

	public virtual bool CanBeDefaultButton() {
		return true;
	}

	[UiMessage("SetAsDefaultButton")]
	public void SetAsDefaultButton(bool state) {
		if ((ButtonFlags & ButtonFlags.DefaultButton) != 0 != state) {
			ButtonFlags ^= ButtonFlags.DefaultButton;

			if (state) {
				KeyValues msg = new("DefaultButtonSet");
				msg.SetPtr("button", this);
				CallParentFunction(msg);
			}

			InvalidateLayout(false);
			Repaint();
		}
	}

	public void SetDefaultBorder(IBorder? border) {
		DefaultBorder = border;
		InvalidateLayout(false);
	}

	public void SetDepressedBorder(IBorder? border) {
		DepressedBorder = border;
		InvalidateLayout(false);
	}

	public void SetKeyFocusBorder(IBorder? border) {
		KeyFocusBorder = border;
		InvalidateLayout(false);
	}

	[UiMessage("SetAsCurrentDefaultButton")]
	private void SetAsCurrentDefaultButton(bool state) {
		if ((ButtonFlags & ButtonFlags.DefaultButton) != 0 != state) {
			ButtonFlags ^= ButtonFlags.DefaultButton;

			if (state) {
				KeyValues msg = new("CurrentDefaultButtonSet");
				msg.SetPtr("button", this);
				CallParentFunction(msg);
			}

			InvalidateLayout(false);
			Repaint();
		}
	}

	public void SetDefaultColor(Color fgColor, Color bgColor) {
		if (!(DefaultFgColor == fgColor && DefaultBgColor == bgColor)) {
			DefaultFgColor = fgColor;
			DefaultBgColor = bgColor;

			InvalidateLayout(false);
		}
	}
	public void SetSelectedColor(Color fgColor, Color bgColor) {
		if (!(SelectedFgColor == fgColor && SelectedBgColor == bgColor)) {
			SelectedFgColor = fgColor;
			SelectedBgColor = bgColor;

			InvalidateLayout(false);
		}
	}
	public void SetArmedColor(Color fgColor, Color bgColor) {
		if (!(ArmedFgColor == fgColor && ArmedBgColor == bgColor)) {
			ArmedFgColor = fgColor;
			ArmedBgColor = bgColor;

			InvalidateLayout(false);
		}
	}
	public void SetDepressedColor(Color fgColor, Color bgColor) {
		if (!(DepressedFgColor == fgColor && DepressedBgColor == bgColor)) {
			DepressedFgColor = fgColor;
			DepressedBgColor = bgColor;

			InvalidateLayout(false);
		}
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

		if (!(code == ButtonCode.KeyUp || code == ButtonCode.KeyDown || code == ButtonCode.KeyLeft || code == ButtonCode.KeyRight))
			SetArmed(false);
	}

	public bool IsDrawingFocusBox() {
		return (ButtonFlags & ButtonFlags.DrawFocusBox) != 0;
	}

	public void DrawFocusBox() {
		ButtonFlags |= ButtonFlags.DrawFocusBox;
	}

	public virtual void DrawFocusBorder(int tx0, int ty0, int tx1, int ty1) {
		Surface.DrawSetColor(KeyboardFocusColor);
		DrawDashedLine(tx0, ty0, tx1, ty0 + 1, 1, 1); // top
		DrawDashedLine(tx0, ty0, tx0 + 1, ty1, 1, 1); // left
		DrawDashedLine(tx0, ty1 - 1, tx1, ty1, 1, 1); // bottom
		DrawDashedLine(tx1 - 1, ty0, tx1, ty1, 1, 1); // right
	}

	public override void SizeToContents() {
		GetContentSize(out int wide, out int tall);
		SetSize(wide + Content, tall + Content);
	}

	bool paint;
	public bool ShouldPaint() => paint;
	public void SetShouldPaint(bool paint) => this.paint = paint;
}
