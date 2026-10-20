using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;

namespace Source.GUI.Controls;

public class MessageBox : Frame
{
	protected Button OkButton;
	protected Button CancelButton;
	protected Label MessageLabel;

	protected KeyValues? OkCommand;
	protected KeyValues? CancelCommand;
	Frame? FrameOver;
	bool NoAutoClose;
	bool ShowMessageBoxOverCursor;

	public MessageBox(string title, string text, Panel? parent = null) : base(parent, null, false) {
		SetTitle(title, true);
		MessageLabel = new Label(this, null, text);
		Init();
	}

	public MessageBox(ReadOnlySpan<char> title, ReadOnlySpan<char> text, Panel? parent = null) : base(parent, null, false) {
		SetTitle(title, true);
		MessageLabel = new Label(this, null, new string(text));
		Init();
	}

	void Init() {
		SetDeleteSelfOnClose(true);

		FrameOver = null;
		ShowMessageBoxOverCursor = false;

		SetMenuButtonResponsive(false);
		SetMinimizeButtonVisible(false);
		SetCloseButtonVisible(false);
		SetSizeable(false);

		OkButton = new Button(this, null, "#MessageBox_OK");
		OkButton.SetCommand("OnOk");
		OkButton.AddActionSignalTarget(this);

		CancelButton = new Button(this, null, "#MessageBox_Cancel");
		CancelButton.SetCommand("OnCancel");
		CancelButton.AddActionSignalTarget(this);
		CancelButton.SetVisible(false);

		OkCommand = CancelCommand = null;
		NoAutoClose = false;
	}

	public void SetOKButtonVisible(bool state) => OkButton.SetVisible(state);
	public void SetCancelButtonVisible(bool state) => CancelButton.SetVisible(state);
	public void SetOKButtonText(ReadOnlySpan<char> buttonText) {
		OkButton.SetText(buttonText);
		InvalidateLayout();
	}
	public void SetCancelButtonText(ReadOnlySpan<char> buttonText) {
		CancelButton.SetText(buttonText);
		InvalidateLayout();
	}
	public void SetOKCommand(KeyValues? command) => OkCommand = command;
	public void SetCancelCommand(KeyValues? command) => CancelCommand = command;
	public virtual void DoModal(Frame? frameOver = null) {
		ShowWindow(frameOver);
		Input.SetAppModalSurface(this);
	}

	[UiCommand("OnOk")]
	private void OnOk() {
		ReleaseModalSurface();
		if (OkCommand != null)
			PostActionSignal(OkCommand.MakeCopy());
		AutoClose();
	}

	[UiCommand("OnCancel")]
	private void OnCancel() {
		ReleaseModalSurface();
		if (CancelCommand != null)
			PostActionSignal(CancelCommand.MakeCopy());
		AutoClose();
	}

	[UiCommand("Close")]
	[UiCommand("CloseModal")]
	private void OnCloseCommand() {
		ReleaseModalSurface();
		AutoClose();
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		ReleaseModalSurface();
		AutoClose();
	}

	private void ReleaseModalSurface() {
		if (Input.GetAppModalSurface() == this)
			Input.ReleaseAppModalSurface();
	}

	private void AutoClose() {
		if (!NoAutoClose)
			OnShutdownRequest();
	}

	private void OnShutdownRequest() {
		PostMessage(this, new("Close"));
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		MessageLabel.GetContentSize(out int wide, out int tall);
		MessageLabel.SetSize(wide, tall);
		wide += 100;
		tall += 100;
		SetSize(wide, tall);

		if (ShowMessageBoxOverCursor) {
			PlaceUnderCursor();
		}

		if (FrameOver != null) {
			FrameOver.GetPos(out int frameX, out int frameY);
			FrameOver.GetSize(out int frameWide, out int frameTall);

			SetPos((frameWide - wide) / 2 + frameX, (frameTall - tall) / 2 + frameY);
		}
		else {
			Surface.GetScreenSize(out int screenWide, out int screenTall);
			SetPos((screenWide - wide) / 2, (screenTall - tall) / 2);
		}
	}

	public override void PerformLayout() {
		GetClientArea(out int x, out int y, out int wide, out int tall);
		wide += x;
		tall += y;

		GetSize(out int boxWidth, out int boxTall);

		OkButton.GetSize(out int oldWide, out int oldTall);

		OkButton.GetContentSize(out int btnWide, out int btnTall);
		btnWide = Math.Max(oldWide, btnWide + 10);
		btnTall = Math.Max(oldTall, btnTall + 10);
		OkButton.SetSize(btnWide, btnTall);

		int btnWide2 = 0, btnTall2 = 0;
		if (CancelButton.IsVisible()) {
			CancelButton.GetSize(out oldWide, out oldTall);

			CancelButton.GetContentSize(out btnWide2, out btnTall2);
			btnWide2 = Math.Max(oldWide, btnWide2 + 10);
			btnTall2 = Math.Max(oldTall, btnTall2 + 10);
			CancelButton.SetSize(btnWide2, btnTall2);
		}

		boxWidth = Math.Max(boxWidth, MessageLabel.GetWide() + 100);
		boxWidth = Math.Max(boxWidth, (btnWide + btnWide2) * 2 + 30);
		SetSize(boxWidth, boxTall);

		GetSize(out boxWidth, out boxTall);

		MessageLabel.SetPos((wide / 2) - (MessageLabel.GetWide() / 2) + x, y + 5);
		if (!CancelButton.IsVisible())
			OkButton.SetPos((wide / 2) - (OkButton.GetWide() / 2) + x, tall - OkButton.GetTall() - 15);
		else {
			OkButton.SetPos((wide / 4) - (OkButton.GetWide() / 2) + x, tall - OkButton.GetTall() - 15);
			CancelButton.SetPos((3 * wide / 4) - (OkButton.GetWide() / 2) + x, tall - OkButton.GetTall() - 15);
		}

		base.PerformLayout();
	}

	private void PlaceUnderCursor() {
		throw new NotImplementedException();
	}

	public void ShowWindow(Frame? frameOver) {
		FrameOver = frameOver;

		SetVisible(true);
		SetEnabled(true);
		MoveToFront();

		if (OkButton.IsVisible())
			OkButton.RequestFocus();
		else
			RequestFocus();

		InvalidateLayout();
	}

	public void SetCommand(ReadOnlySpan<char> command) => OkCommand = new("Command", "command", command);

	public void SetCommand(KeyValues command) => OkCommand = command;
}
