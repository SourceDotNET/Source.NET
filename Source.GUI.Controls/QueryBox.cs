using Source.Common.Input;

namespace Source.GUI.Controls;

public class QueryBox : MessageBox
{
	static QueryBox() => ChainToAnimationMap<QueryBox>();
	public QueryBox(ReadOnlySpan<char> title, ReadOnlySpan<char> queryText, Panel? parent = null) : base(title, queryText, parent) {
		CancelButton?.DeletePanel();

		CancelButton = new Button(this, "CancelButton", "#QueryBox_Cancel");
		CancelButton.SetCommand("Cancel");
		CancelButton.AddActionSignalTarget(this);

		OkButton.SetCommand("OK");
		CancelCommand = null;
		OkCommand = null;

		OkButton.SetTabPosition(1);
		CancelButton.SetTabPosition(2);
	}

	public override void PerformLayout() {
		base.PerformLayout();

		GetClientArea(out int x, out int y, out int wide, out int tall);
		wide += x;
		tall += y;

		CancelButton.GetSize(out int oldWide, out int oldTall);

		CancelButton.GetContentSize(out int btnWide, out int btnTall);
		btnWide = Math.Max(oldWide, btnWide + 10);
		btnTall = Math.Max(oldTall, btnTall + 10);
		CancelButton.SetSize(btnWide, btnTall);

		OkButton.SetPos((wide / 2) - (OkButton.GetWide()) - 1 + x, tall - OkButton.GetTall() - 15);
		CancelButton.SetPos((wide / 2) + x + 16, tall - CancelButton.GetTall() - 15);
	}

	[UiCommand("OK")]
	private void OnOK() {
		DoCommand("Close");
		if (OkCommand != null) PostActionSignal(OkCommand.MakeCopy());
		base.OnCommand("OK");
	}

	[UiCommand("Cancel")]
	private void OnCancelCommand() {
		DoCommand("Close");
		if (CancelCommand != null) PostActionSignal(CancelCommand.MakeCopy());
		base.OnCommand("Cancel");
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.KeyEscape)
			DoCommand("Cancel");
		else
			base.OnKeyCodeTyped(code);
	}
}
