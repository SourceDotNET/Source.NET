using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;

namespace Source.GUI.Controls;

class CheckImage : TextImage
{
	public Color BorderColor1;
	public Color BorderColor2;
	public Color CheckColor;
	public Color BgColor;
	CheckButton CheckButton;

	public CheckImage(CheckButton CheckButton) : base("g") {
		this.CheckButton = CheckButton;
		SetSize(20, 13);
	}

	public override void SetColor(Color color) {
		BorderColor1 = color;
		BorderColor2 = color;
		CheckColor = color;
	}

	public override void Paint() {
		DrawSetTextFont(GetFont()!);

		if (CheckButton.IsEnabled() && CheckButton.IsCheckButtonCheckable())
			DrawSetTextColor(BgColor);
		else
			DrawSetTextColor(CheckButton.GetDisabledBgColor());
		DrawPrintChar(0, 1, 'g');

		DrawSetTextColor(BorderColor1);
		DrawPrintChar(0, 1, 'e');
		DrawSetTextColor(BorderColor2);
		DrawPrintChar(0, 1, 'f');

		if (CheckButton.IsSelected()) {
			if (!CheckButton.IsEnabled())
				DrawSetTextColor(CheckButton.GetDisabledFgColor());
			else
				DrawSetTextColor(CheckColor);

			DrawPrintChar(0, 2, 'b');
		}
	}
}

public class CheckButton : ToggleButton
{
	public static Panel Create_CheckButton() => new CheckButton(null, null, "CheckButton");

	private const int CHECK_INSET = 6;

	bool CheckButtonCheckable;
	bool UseSmallCheckImage;
	CheckImage CheckBoxImage;
	Color DisabledFgColor;
	Color DisabledBgColor;
	Color HighlightFgColor;

	public CheckButton(Panel? parent, ReadOnlySpan<char> name, ReadOnlySpan<char> text) : base(parent, name, text) {
		SetContentAlignment(Alignment.West);
		CheckButtonCheckable = true;
		UseSmallCheckImage = false;

		CheckBoxImage = new(this);

		SetTextImageIndex(1);
		SetImageAtIndex(0, CheckBoxImage, CHECK_INSET);

		SelectedFgColor = new(196, 181, 80, 255);
		DisabledFgColor = new(130, 130, 130, 255);
		DisabledBgColor = new(62, 70, 55, 255);
	}

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);
		UseSmallCheckImage = resourceData.GetBool("smallcheckimage", false);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetDefaultColor(GetSchemeColor("CheckButton.TextColor", scheme), GetBgColor());
		CheckBoxImage.BgColor = GetSchemeColor("CheckButton.BgColor", new Color(62, 70, 55, 255), scheme);
		CheckBoxImage.BorderColor1 = GetSchemeColor("CheckButton.Border1", new Color(20, 20, 20, 255), scheme);
		CheckBoxImage.BorderColor2 = GetSchemeColor("CheckButton.Border2", new Color(90, 90, 90, 255), scheme);
		CheckBoxImage.CheckColor = GetSchemeColor("CheckButton.Check", new Color(20, 20, 20, 255), scheme);
		SelectedFgColor = GetSchemeColor("CheckButton.SelectedTextColor", GetSchemeColor("ControlText", scheme), scheme);
		DisabledFgColor = GetSchemeColor("CheckButton.DisabledFgColor", new Color(130, 130, 130, 255), scheme);
		DisabledBgColor = GetSchemeColor("CheckButton.DisabledBgColor", new Color(62, 70, 55, 255), scheme);

		Color bgArmedColor = GetSchemeColor("CheckButton.ArmedBgColor", new Color(62, 70, 55, 255), scheme);
		SetArmedColor(GetFgColor(), bgArmedColor);

		Color bgDepressedColor = GetSchemeColor("CheckButton.DepressedBgColor", new Color(62, 70, 55, 255), scheme);
		SetDepressedColor(GetFgColor(), bgDepressedColor);

		HighlightFgColor = GetSchemeColor("CheckButton.HighlightFgColor", new Color(62, 70, 55, 255), scheme);

		SetContentAlignment(Alignment.West);

		CheckBoxImage.SetFont(scheme.GetFont(UseSmallCheckImage ? "MarlettSmall" : "Marlett", IsProportional()));
		CheckBoxImage.ResizeImageToContent();
		SetImageAtIndex(0, CheckBoxImage, CHECK_INSET);

		SetPaintBackgroundEnabled(false);
	}

	public override IBorder? GetBorder(bool depressed, bool armed, bool selected, bool keyfocus) => null;

	public override void SetSelected(bool state) {
		if (CheckButtonCheckable) {
			KeyValues msg = new("CheckButtonChecked", "state", state ? 1 : 0);
			PostActionSignal(msg);

			base.SetSelected(state);
		}
	}

	public void SetCheckButtonCheckable(bool state) {
		CheckButtonCheckable = state;
		Repaint();
	}

	public virtual bool IsCheckButtonCheckable() => CheckButtonCheckable;

	public Color GetDisabledFgColor() => DisabledFgColor;
	public Color GetDisabledBgColor() => DisabledBgColor;

	public override Color GetButtonFgColor() {
		if (IsArmed())
			return HighlightFgColor;

		if (IsSelected())
			return SelectedFgColor;

		return base.GetButtonFgColor();
	}

	[UiMessage("CheckButtonChecked")]
	public virtual void OnCheckButtonChecked(Panel sender) { }

	public void SetHighlightColor(Color color) {
		if (HighlightFgColor != color) {
			HighlightFgColor = color;
			InvalidateLayout(false);
		}
	}
}
