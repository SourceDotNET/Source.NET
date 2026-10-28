using Source.Common.GUI;
using Source.Common.Formats.Keyvalues;

namespace Source.GUI.Controls;

class ScrollableEditablePanel : EditablePanel
{
	ScrollBar ScrollBar;
	EditablePanel Child;

	public ScrollableEditablePanel(Panel? parent, EditablePanel child, ReadOnlySpan<char> name) : base(parent, name) {
		Child = child;
		Child.SetParent(this);

		ScrollBar = new(this, "VerticalScrollBar", true);
		ScrollBar.SetWide(16);
		ScrollBar.SetAutoResize(Common.GUI.PinCorner.TopRight, AutoResize.Down, 0, 0, -16, 0);
		ScrollBar.AddActionSignalTarget(this);
	}

	public override void ApplySettings(KeyValues resourceData) {
		base.ApplySettings(resourceData);

		KeyValues? scrollBarData = resourceData.FindKey("Scrollbar");
		if (scrollBarData != null)
			ScrollBar.ApplySettings(scrollBarData);
	}

	public override void PerformLayout() {
		base.PerformLayout();

		Child.SetWide(GetWide() - ScrollBar.GetWide());
		ScrollBar.SetRange(0, Child.GetTall());
		ScrollBar.SetRangeWindow(GetTall());

		if (ScrollBar.GetSlider() != null)
			ScrollBar.GetSlider()!.SetFgColor(GetFgColor());

		if (ScrollBar.GetButton(0) != null)
			ScrollBar.GetButton(0)!.SetFgColor(GetFgColor());

		if (ScrollBar.GetButton(1) != null)
			ScrollBar.GetButton(1)!.SetFgColor(GetFgColor());
	}

	[UiMessage("ScrollBarSliderMoved")]
	void OnScrollBarSliderMoved() {
		InvalidateLayout();

		int scrollAmount = ScrollBar.GetValue();
		Child.SetPos(0, -scrollAmount);
	}

	public override void OnMouseWheeled(int delta) {
		int val = ScrollBar.GetValue();
		val -= delta * 50;
		ScrollBar.SetValue(val);
	}
}