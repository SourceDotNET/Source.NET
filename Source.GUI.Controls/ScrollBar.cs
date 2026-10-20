using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public class ScrollBarButton : Button
{
	public ScrollBarButton(Panel? parent, ReadOnlySpan<char> name, ReadOnlySpan<char> text) : base(parent, name, text) {
		SetButtonActivationType(ActivationType.OnPressed);

		SetContentAlignment(Alignment.Center);
	}

	static readonly KeyValues KV_MouseFocusTicked = new("MouseFocusTicked");
	public override void OnMouseFocusTicked() => CallParentFunction(KV_MouseFocusTicked);

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetFont(scheme.GetFont("Marlett", IsProportional()));
		SetDefaultBorder(scheme.GetBorder("ScrollBarButtonBorder"));
		SetDepressedBorder(scheme.GetBorder("ScrollBarButtonDepressedBorder"));

		SetDefaultColor(GetSchemeColor("ScrollBarButton.FgColor", scheme), GetSchemeColor("ScrollBarButton.BgColor", scheme));
		SetArmedColor(GetSchemeColor("ScrollBarButton.ArmedFgColor", scheme), GetSchemeColor("ScrollBarButton.ArmedBgColor", scheme));
		SetDepressedColor(GetSchemeColor("ScrollBarButton.DepressedFgColor", scheme), GetSchemeColor("ScrollBarButton.DepressedBgColor", scheme));
	}

	public override void OnMousePressed(ButtonCode code) {
		if (!IsEnabled())
			return;

		if (!IsMouseClickEnabled(code))
			return;

		if (IsUseCaptureMouseEnabled()) {
			SetSelected(true);
			Repaint();

			Input.SetMouseCapture(this);
		}
	}

	public override void OnMouseReleased(ButtonCode code) {
		if (!IsEnabled())
			return;

		if (!IsMouseClickEnabled(code))
			return;

		if (IsUseCaptureMouseEnabled()) {
			SetSelected(false);
			Repaint();

			Input.SetMouseCapture(null);
		}

		if (Input.GetMouseOver() == this)
			SetArmed(true);
	}

}
public class ScrollBar : Panel
{
	public const int SCROLL_BAR_DELAY = 400;
	public const int SCROLL_BAR_SPEED = 50;
	public const int SCROLLBAR_DEFAULT_WIDTH = 17;

	readonly Button?[] Button = new Button[2];
	ScrollBarSlider? Slider;
	int ButtonPressedScrollValue;
	int ScrollDelay;
	bool Respond;
	bool NoButtons;
	bool AutoHideButtons;

	ImagePanel? UpArrow;
	ImagePanel? Line;
	ImagePanel? DownArrow;
	ImagePanel? Box;
	readonly Button?[] OverriddenButtons = new Button[2];

	public ScrollBar(Panel parent, ReadOnlySpan<char> panelName, bool vertical) : base(parent, panelName) {
		Slider = null;
		Button[0] = null;
		Button[1] = null;
		ScrollDelay = SCROLL_BAR_DELAY;
		Respond = true;
		UpArrow = null;
		Line = null;
		DownArrow = null;
		Box = null;
		NoButtons = false;
		OverriddenButtons[0] = null;
		OverriddenButtons[1] = null;

		if (vertical) {
			SetSlider(new ScrollBarSlider(null, "Slider", true));
			SetButton(new ScrollBarButton(null, "UpButton", "t"), 0);
			SetButton(new ScrollBarButton(null, "DownButton", "u"), 1);
			Button[0]!.SetTextInset(0, 1);
			Button[1]!.SetTextInset(0, -1);

			SetSize(SCROLLBAR_DEFAULT_WIDTH, 64);
		}
		else {
			SetSlider(new ScrollBarSlider(null, null, false));
			SetButton(new ScrollBarButton(null, null, "w"), 0);
			SetButton(new ScrollBarButton(null, null, "4"), 1);
			Button[0]!.SetTextInset(0, 0);
			Button[1]!.SetTextInset(0, 0);

			SetSize(64, SCROLLBAR_DEFAULT_WIDTH);
		}

		SetPaintBorderEnabled(true);
		SetPaintBackgroundEnabled(false);
		SetPaintEnabled(true);
		SetButtonPressedScrollValue(20);
		SetBlockDragChaining(true);

		Validate();
	}

	private void SetSlider(ScrollBarSlider slider) {
		Slider?.SetParent(null);

		Slider = slider;
		Slider!.AddActionSignalTarget(this);
		Slider!.SetParent(this);
		Validate();
	}

	public Button GetDepressedButton(int index) {
		if (index == 0)
			return (OverriddenButtons[0] != null) ? OverriddenButtons[0]! : Button[0]!;
		return (OverriddenButtons[1] != null) ? OverriddenButtons[1]! : Button[1]!;
	}

	private void SetButton(ScrollBarButton button, int index) {
		Button[index]?.SetParent(null);

		Button[index] = button;
		Button[index]!.SetParent(this);
		Button[index]!.AddActionSignalTarget(this);
		Button[index]!.SetCommand(new KeyValues("ScrollButtonPressed", "index", index));
		Validate();
	}
	public override void PerformLayout() {
		if (Slider != null) {
			GetPaintSize(out int wide, out int tall);
			if (Slider.IsVertical()) {
				if (NoButtons)
					Slider.SetBounds(0, 0, wide, tall + 1);
				else {
					Slider.SetBounds(0, wide, wide, tall - (wide * 2) + 1);
					Button[0]!.SetBounds(0, 0, wide, wide);
					Button[1]!.SetBounds(0, tall - wide, wide, wide);
				}
			}
			else {
				if (NoButtons)
					Slider.SetBounds(tall, 0, wide, tall + 1);
				else {
					Slider.SetBounds(tall, -1, wide - (tall * 2) + 1, tall + 1);
					Button[0]!.SetBounds(0, 0, tall, tall);
					Button[1]!.SetBounds(wide - tall, 0, tall, tall);
				}
			}

			int x, y;

			if (UpArrow != null) {
				Button[0]!.GetBounds(out x, out y, out wide, out tall);
				UpArrow.SetBounds(x, y, wide, tall);
			}

			if (DownArrow != null) {
				Button[1]!.GetBounds(out x, out y, out wide, out tall);
				DownArrow.SetBounds(x, y, wide, tall);
			}

			if (Line != null) {
				Slider.GetBounds(out x, out y, out wide, out tall);
				Line.SetBounds(x, y, wide, tall);
			}

			Box?.SetBounds(0, wide, wide, wide);

			Slider.MoveToFront();
			Slider.InvalidateLayout();

			UpdateSliderImages();
		}

		if (AutoHideButtons)
			SetScrollbarButtonsVisible(Slider!.IsSliderVisible());

		base.PerformLayout();
	}

	private void SetScrollbarButtonsVisible(bool visible) {
		for (int i = 0; i < 2; i++) {
			if (Button[i] != null) {
				Button[i]!.SetShouldPaint(visible);
				Button[i]!.SetEnabled(visible);
			}
		}
	}

	private void UpdateSliderImages() {
		if (UpArrow != null && DownArrow != null) {
			GetRange(out int nMin, out int nMax);
			int nScrollPos = GetValue();
			int nRangeWindow = GetRangeWindow();
			int nBottom = nMax - nRangeWindow;
			if (nBottom < 0)
				nBottom = 0;

			int nAlpha = (nScrollPos - nMin <= 0) ? 90 : 255;
			UpArrow.SetAlpha(nAlpha);

			nAlpha = (nScrollPos >= nBottom) ? 90 : 255;
			DownArrow.SetAlpha(nAlpha);
		}

		if (Line != null && Box != null) {
			ScrollBarSlider? slider = GetSlider();
			if (slider != null && slider.GetRangeWindow() > 0) {
				Line.GetBounds(out int x, out int y, out int w, out int t);

				if (slider.IsLayoutInvalid())
					slider.InvalidateLayout(true);

				slider.GetNobPos(out int min, out int max);

				if (IsVertical())
					Box.SetBounds(x, y + min, w, (max - min));
				else
					Box.SetBounds(x + min, 0, (max - min), t);
			}
		}
	}

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);

		NoButtons = resourceData.GetBool("nobuttons", false);

		KeyValues? SliderKV = resourceData.FindKey("Slider");
		if (SliderKV != null && Slider != null)
			Slider.ApplySettings(SliderKV);

		KeyValues? DownButtonKV = resourceData.FindKey("DownButton");
		if (DownButtonKV != null && Button[1] != null)
			Button[0]!.ApplySettings(DownButtonKV);

		KeyValues? UpButtonKV = resourceData.FindKey("UpButton");
		if (UpButtonKV != null && Button[0] != null)
			Button[1]!.ApplySettings(UpButtonKV);
	}

	public bool IsVertical() => Slider!.IsVertical();
	public ScrollBarSlider? GetSlider() => Slider;
	public int GetRangeWindow() => Slider!.GetRangeWindow();

	private void Validate() {
		if (Slider != null) {
			int buttonOffset = 0;

			for (int i = 0; i < 2; i++) {
				Button? button = Button[i];
				if (button != null) {
					if (button.IsVisible()) {
						if (Slider.IsVertical()) {
							buttonOffset += button.GetTall();
						}
						else {
							buttonOffset += button.GetWide();
						}
					}
				}
			}

			Slider.SetButtonOffset(buttonOffset);
		}
	}

	public void SetButtonPressedScrollValue(int value) => ButtonPressedScrollValue = value;

	public virtual void SendSliderMoveMessage(int value) => PostActionSignal(new KeyValues("ScrollBarSliderMoved", "position", value));

	public void OnSliderMoved(int value) {
		SendSliderMoveMessage(value);
		UpdateSliderImages();
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		switch (message.Name) {
			case "ScrollBarSliderMoved":
				OnSliderMoved(message.GetInt("position"));
				break;
			default:
				base.OnMessage(message, sender);
				break;
		}
	}

	public int GetValue() => Slider!.GetValue();
	public void SetValue(int val) => Slider!.SetValue(val);
	public void SetRange(int min, int max) => Slider!.SetRange(min, max);
	public void GetRange(out int min, out int max) => Slider!.GetRange(out min, out max);
	public void SetRangeWindow(int range) => Slider!.SetRangeWindow(range);
	public Button? GetButton(int index) => Button[index];
}
