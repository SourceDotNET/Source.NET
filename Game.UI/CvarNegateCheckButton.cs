using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Game.UI;

[PanelAlias("CCvarNegateCheckButton")]
public class CvarNegateCheckButton : CheckButton
{
	public static Panel Create_CvarNegateCheckButton() => new CvarNegateCheckButton(null, null, null, null);

	string? CvarName;
	bool StartState;

	public CvarNegateCheckButton(Panel? parent, ReadOnlySpan<char> name, ReadOnlySpan<char> text, ReadOnlySpan<char> cvarName) : base(parent, name, text) {
		CvarName = cvarName.Length > 0 ? new(cvarName) : null;
		Reset();
		AddActionSignalTarget(this);
	}

	public override void Paint() {
		if (CvarName == null) {
			base.Paint();
			return;
		}

		ConVarRef var = new(CvarName);
		if (!var.IsValid())
			return;

		float value = var.GetFloat();

		if (value < 0) {
			if (!StartState) {
				SetSelected(true);
				StartState = true;
			}
		}
		else {
			if (StartState) {
				SetSelected(false);
				StartState = false;
			}
		}

		base.Paint();
	}

	public void Reset() {
		ConVarRef var = new(CvarName);
		if (!var.IsValid())
			return;

		float value = var.GetFloat();

		if (value < 0)
			StartState = true;
		else
			StartState = false;

		SetSelected(StartState);
	}

	public bool HasBeenModified() => IsSelected() != StartState;

	public override void SetSelected(bool state) {
		base.SetSelected(state);
	}

	public void ApplyChanges() {
		if (CvarName == null || CvarName.Length == 0)
			return;

		ConVarRef var = new(CvarName);
		float value = var.GetFloat();

		value = (float)MathF.Abs(value);
		if (value < 0.00001)
			value = 0.022f;

		StartState = IsSelected();
		value = -value;

		float ans = StartState ? value : -value;
		var.SetValue(ans);
	}

	static readonly KeyValues KV_ControlModified = new("ControlModified");
	[UiMessage("CheckButtonChecked")]
	public void OnButtonChecked() {
		if (HasBeenModified())
			PostActionSignal(KV_ControlModified);
	}
}
