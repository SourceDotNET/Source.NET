using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public class PropertyPage : EditablePanel
{
	Panel? PageTab;

	public PropertyPage(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {

	}

	public virtual void OnResetData() { }
	public virtual void OnApplyChanges() { }
	public virtual void OnPageShow() { }
	public virtual void OnPageHide() { }
	public void OnPageTabActivated(Panel pageTab) => PageTab = pageTab;

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

	public override void OnMessage(KeyValues message, IPanel? sender) {
		switch (message.Name) {
			case "ApplyChanges":
				OnApplyChanges();
				break;
			case "ResetData":
				OnResetData();
				break;
			case "PageShow":
				OnPageShow();
				break;
			case "PageHide":
				OnPageHide();
				break;
			case "PageTabActivated":
				OnPageTabActivated((Panel)message.GetPtr("Panel")!);
				break;
			default:
				base.OnMessage(message, sender);
				break;
		}
	}
}
