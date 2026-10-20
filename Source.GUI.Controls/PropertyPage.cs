using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public class PropertyPage : EditablePanel
{
	Panel? PageTab;

	public PropertyPage(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {

	}

	[UiMessage("ResetData")]
	public virtual void OnResetData() { }
	[UiMessage("ApplyChanges")]
	public virtual void OnApplyChanges() { }
	[UiMessage("PageShow")]
	public virtual void OnPageShow() { }
	[UiMessage("PageHide")]
	public virtual void OnPageHide() { }
	[UiMessage("PageTabActivated")]
	public void OnPageTabActivated(Panel sender) => PageTab = sender;

	public override void OnKeyCodeTyped(ButtonCode code) {
		switch (code) {
			case ButtonCode.KeyRight:
			case ButtonCode.KeyLeft:
				if (PageTab != null && PageTab.HasFocus())
					base.OnKeyCodeTyped(code);
				break;
			default:
				base.OnKeyCodeTyped(code);
				break;
		}
	}

	public override void SetVisible(bool state) {
		if (IsVisible() && state)
			if (GetFocusNavGroup().GetCurrentDefaultButton() != null)
				GetFocusNavGroup().SetCurrentDefaultButton(null);

		base.SetVisible(state);
	}
}
