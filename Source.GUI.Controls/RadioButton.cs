using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

enum Direction
{
	Up = -1,
	Down = 1
}

class RadioImage : TextImage
{
	public Color BorderColor1;
	public Color BorderColor2;
	public Color CheckColor;
	public Color BgColor;
	RadioButton RadioButton;
	public RadioImage(RadioButton radioButton) : base("n") {
		RadioButton = radioButton;
		SetSize(20, 13);
	}

	public override void Paint() {
		DrawSetTextFont(GetFont());

		if (RadioButton.IsEnabled())
			DrawSetTextColor(CheckColor);
		else
			DrawSetTextColor(RadioButton.GetBgColor());
		DrawPrintChar(0, 1, 'n');

		DrawSetTextColor(BorderColor1);
		DrawPrintChar(0, 1, 'j');
		DrawSetTextColor(BorderColor2);
		DrawPrintChar(0, 1, 'k');

		if (RadioButton.IsSelected()) {
			DrawSetTextColor(CheckColor);
			DrawPrintChar(0, 1, 'h');
		}
	}

	public override void SetColor(Color color) {
		BorderColor1 = color;
		BorderColor2 = color;
		CheckColor = color;
	}
}

public class RadioButton : ToggleButton
{
	RadioImage RadioBoxImage;
	int OldTabPosition;
	int SubTabPosition;

	public RadioButton(Panel parent, string name, string text) : base(parent, name, text) {
		SetContentAlignment(Alignment.West);

		RadioBoxImage = new(this);

		OldTabPosition = 0;
		SubTabPosition = 0;

		SetTextImageIndex(1);
		SetImageAtIndex(0, RadioBoxImage, 0);

		SetButtonActivationType(ActivationType.OnPressed);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		RadioBoxImage.BgColor = GetSchemeColor("CheckButton.BgColor", new Color(150, 150, 150, 0), scheme);
		RadioBoxImage.BorderColor1 = GetSchemeColor("CheckButton.Border1", new Color(20, 20, 20, 0), scheme);
		RadioBoxImage.BorderColor2 = GetSchemeColor("CheckButton.Border2", new Color(90, 90, 90, 0), scheme);
		RadioBoxImage.CheckColor = GetSchemeColor("CheckButton.Check", new Color(20, 20, 20, 0), scheme);

		SetFgColor(GetSchemeColor("RadioButton.TextColor", scheme));
		SelectedFgColor = GetSchemeColor("RadioButton.SelectedTextColor", GetSchemeColor("ControlText", scheme), scheme);

		SetDefaultColor(GetFgColor(), GetBgColor());

		SetArmedColor(GetSchemeColor("RadioButton.ArmedTextColor", scheme), GetButtonArmedBgColor());

		SetContentAlignment(Alignment.West);

		IFont? font = scheme.GetFont("MarlettSmall", IsProportional());
		font ??= scheme.GetFont("Marlett", IsProportional());

		RadioBoxImage.SetFont(font);
		RadioBoxImage.ResizeImageToContent();
		SetImageAtIndex(0, RadioBoxImage, 0);

		SetPaintBackgroundEnabled(false);
	}

	public override IBorder? GetBorder(bool depressed, bool armed, bool selected, bool keyfocus) {
		return null;
	}

	public int GetSubTabPosition() => SubTabPosition;
	public void SetSubTabPosition(int pos) => SubTabPosition = pos;
	public int GetRadioTabPosition() => OldTabPosition;

	public override void SetSelected(bool state) => InternalSetSelected(state, true);

	public void InternalSetSelected(bool state, bool fireEvents) {
		if (state) {
			if (!IsEnabled())
				return;

			SetTabPosition(OldTabPosition);

			if (fireEvents) {
				KeyValues msg = new("RadioButtonChecked");
				msg.SetPtr("panel", this);
				msg.SetInt("tabposition", OldTabPosition);

				Panel? radioParent = GetParent();
				if (radioParent != null) {
					for (int i = 0; i < radioParent.GetChildCount(); i++) {
						Panel child = radioParent.GetChild(i);
						if (child != this)
							PostMessage(child, msg.MakeCopy());
					}
				}

				RequestFocus();
				PostActionSignal(msg);
			}
		}
		else {
			if (GetTabPosition() != 0)
				OldTabPosition = GetTabPosition();
			SetTabPosition(0);
		}

		InvalidateLayout();
		Repaint();

		base.SetSelected(state);
	}

	public void SilentSetSelected(bool state) => InternalSetSelected(state, false);

	public override void PerformLayout() {
		if (IsSelected())
			SetFgColor(SelectedFgColor);
		else
			SetFgColor(GetButtonFgColor());

		base.PerformLayout();
	}

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);

		SetTextColorState(ColorState.Normal);
		SubTabPosition = resourceData.GetInt("SubTabPosition");
		OldTabPosition = resourceData.GetInt("TabPosition");
		SetImageAtIndex(0, RadioBoxImage, 0);
	}

	public override void GetSettings(KeyValues resourceData) {
		base.GetSettings(resourceData);

		resourceData.SetInt("SubTabPosition", SubTabPosition);
		resourceData.SetInt("TabPosition", GetRadioTabPosition());
	}

	public void OnRadioButtonChecked(int tabPosition) {
		if (tabPosition != OldTabPosition)
			return;

		SetSelected(false);
	}

	public override void Paint() {
		base.Paint();
	}

	public override void DoClick() => SetSelected(true);

	public override void OnKeyCodeTyped(ButtonCode code) {
		switch (code) {
			case ButtonCode.KeyEnter:
			case ButtonCode.KeySpace:
				if (!IsSelected())
					SetSelected(true);
				else
					base.OnKeyCodeTyped(code);
				break;
			case ButtonCode.KeyDown:
			case ButtonCode.KeyRight:
				RadioButton? bestRadio = FindBestRadioButton((int)Direction.Down);
				bestRadio?.SetSelected(true);
				break;
			case ButtonCode.KeyUp:
			case ButtonCode.KeyLeft:
				bestRadio = FindBestRadioButton((int)Direction.Up);
				bestRadio?.SetSelected(true);
				break;
			default:
				base.OnKeyCodeTyped(code);
				break;
		}
	}

	public RadioButton? FindBestRadioButton(int direction) {
		RadioButton? bestRadio = null;
		int highestRadio = 0;
		Panel? pr = GetParent();
		if (pr != null) {
			for (int i = 0; i < pr.GetChildCount(); i++) {
				RadioButton? child = (RadioButton)pr.GetChild(i);
				if (child != null && child.GetRadioTabPosition() == OldTabPosition) {
					if (child.GetSubTabPosition() == SubTabPosition + direction) {
						bestRadio = child;
						break;
					}

					if (child.GetSubTabPosition() == 0 && direction == (int)Direction.Down) {
						bestRadio = child;
						continue;
					}

					if (child.GetSubTabPosition() > highestRadio && direction == (int)Direction.Up) {
						bestRadio = child;
						highestRadio = child.GetSubTabPosition();
						continue;
					}

					bestRadio ??= child;
				}
			}

			bestRadio?.RequestFocus();

			InvalidateLayout();
			Repaint();
		}

		return bestRadio;
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		if (message.Name == "RadioButtonChecked") {
			OnRadioButtonChecked(message.GetInt("tabposition", -1));
			return;
		}

		base.OnMessage(message, sender);
	}
}
