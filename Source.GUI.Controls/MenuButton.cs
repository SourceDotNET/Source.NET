using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public class MenuButton : Button
{
	static MenuButton() => ChainToAnimationMap<MenuButton>();

	Menu? Menu;
	MenuDirection Direction;
	int OpenOffsetY;
	bool DropMenuButtonStyle;
	TextImage? DropMenuImage;
	nint ImageIndex;

	public MenuButton(Panel parent, ReadOnlySpan<char> name, ReadOnlySpan<char> text) : base(parent, name, text) {
		Menu = null;
		Direction = MenuDirection.DOWN;
		DropMenuImage = null;
		ImageIndex = -1;
		OpenOffsetY = 0;
		DropMenuButtonStyle = true;

		SetDropMenuButtonStyle(false);
		SetUseCaptureMouse(false);
		SetButtonActivationType(ActivationType.OnPressed);
	}

	public void SetMenu(Menu menu) {
		Menu = menu;
		if (Menu != null) {
			Menu.SetVisible(false);
			Menu.AddActionSignalTarget(this);
			Menu.SetParent(this);
		}
	}

	public override void DrawFocusBorder(int x0, int y0, int x1, int y1) { }

	public void SetOpenDirection(MenuDirection dir) => Direction = dir;

	public void HideMenu() {
		if (Menu == null)
			return;

		Menu.SetVisible(false);
		ForceDepressed(false);
		Repaint();
		OnHideMenu(Menu);
	}

	public virtual void OnShowMenu(Menu menu) { }
	public virtual void OnHideMenu(Menu menu) { }
	public virtual int OnCheckMenuItemCount() => 0;

	public override void OnKillFocus(Panel? newPanel) {
		if (Menu != null && !Menu.HasFocus() && newPanel != Menu)
			HideMenu();

		base.OnKillFocus(newPanel);
	}

	static readonly KeyValues KV_MenuClose = new("MenuClose");
	public void OnMenuClose() {
		HideMenu();
		PostActionSignal(KV_MenuClose);
	}

	public void SetOpenOffsetY(int offset) => OpenOffsetY = offset;

	public override bool CanBeDefaultButton() => false;

	public override void DoClick() {
		if (IsDropMenuButtonStyle() && DropMenuImage != null) {
			Input.GetCursorPos(out int mx, out int my);
			ScreenToLocal(ref mx, ref my);

			DropMenuImage.GetContentSize(out int contentW, out _);
			int drawX = GetWide() - contentW - 2;
			if (mx > drawX && OnCheckMenuItemCount() == 0) {
				base.DoClick();
				return;
			}
		}

		if (Menu == null)
			return;

		if (Menu.IsVisible()) {
			HideMenu();
			return;
		}

		if (!Menu.IsEnabled())
			return;

		Menu.PerformLayout();
		Menu.PositionRelativeToPanel(this, Direction, OpenOffsetY);
		MoveToFront();
		OnShowMenu(Menu);
		ForceDepressed(true);
		Menu.SetVisible(true);
		Menu.RequestFocus();
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		bool shift = Input.IsKeyDown(ButtonCode.KeyLShift) || Input.IsKeyDown(ButtonCode.KeyRShift);
		bool ctrl = Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl);
		bool alt = Input.IsKeyDown(ButtonCode.KeyLAlt) || Input.IsKeyDown(ButtonCode.KeyRAlt);

		if (!shift && !ctrl && !alt) {
			if (code == ButtonCode.KeyEnter)
				if (!IsDropMenuButtonStyle())
					DoClick();
		}

		base.OnKeyCodeTyped(code);
	}

	public override void OnCursorEntered() {
		base.OnCursorEntered();

		KeyValues msg = new("CursorEnteredMenuButton");
		msg.SetPtr("VPanel", this);
		VGui.PostMessage(GetParent(), msg, this);
	}

	public void SetDropMenuButtonStyle(bool state) {
		bool changed = DropMenuButtonStyle != state;
		DropMenuButtonStyle = state;

		if (!changed)
			return;

		if (state) {
			DropMenuImage = new TextImage("u");
			IScheme? scheme = GetScheme();
			DropMenuImage.SetFont(scheme!.GetFont("Marlett", IsProportional()));
			ImageIndex = AddImage(DropMenuImage, 0);
		}
		else {
			ResetToSimpleTextImage();
			DropMenuImage = null;
			ImageIndex = -1;
		}
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		if (DropMenuImage != null)
			SetImageAtIndex(1, DropMenuImage, 0);
	}

	public override void PerformLayout() {
		base.PerformLayout();

		if (!IsDropMenuButtonStyle())
			return;

		Assert(ImageIndex >= 0);

		if (ImageIndex < 0 || DropMenuImage == null)
			return;

		GetSize(out int w, out int h);

		DropMenuImage.ResizeImageToContent();
		DropMenuImage.GetContentSize(out int contentW, out _);

		SetImageBounds(ImageIndex, w - contentW - 2, contentW);
	}

	public bool IsDropMenuButtonStyle() => DropMenuButtonStyle;

	public override void Paint() {
		base.Paint();

		if (!IsDropMenuButtonStyle())
			return;

		DropMenuImage!.GetContentSize(out int contentW, out int contentH);
		DropMenuImage.SetColor(IsEnabled() ? GetButtonFgColor() : GetDisabledFgColor1());

		int drawX = GetWide() - contentW - 2;

		Surface.DrawSetColor(IsEnabled() ? GetButtonFgColor() : GetDisabledFgColor1());
		Surface.DrawFilledRect(drawX, 3, drawX + 1, GetTall() - 3);
	}

	public override void OnCursorMoved(int x, int y) {
		base.OnCursorMoved(x, y);

		if (!IsDropMenuButtonStyle())
			return;

		DropMenuImage!.GetContentSize(out int contentW, out int contentH);
		int drawX = GetWide() - contentW - 2;
		if (x <= drawX || OnCheckMenuItemCount() == 0) {
			SetButtonActivationType(ActivationType.OnPressedAndReleased);
			SetUseCaptureMouse(true);
		}
		else {
			SetButtonActivationType(ActivationType.OnPressed);
			SetUseCaptureMouse(false);
		}
	}

	public Menu GetMenu() {
		Assert(Menu != null);
		return Menu;
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		if (message.Name == "MenuClose") {
			OnMenuClose();
			return;
		}

		base.OnMessage(message, sender);
	}
}
