using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public class FrameSystemButton : MenuButton
{
	public FrameSystemButton(Panel parent, string name) : base(parent, name, "") {
		Responsive = true;
		SetEnabled(false);
		SetMouseClickEnabled(ButtonCode.MouseRight, true);
		SetBlockDragChaining(true);
	}

	IImage? Enabled, Disabled;
	Color EnabledColor, DisabledColor;
	bool Responsive;
	string? EnabledImage;
	string? DisabledImage;

	public void SetImages(string enabledImage, string? disabledImage = null) {
		EnabledImage = enabledImage;
		DisabledImage = disabledImage ?? enabledImage;
	}

	public void SetResponsive(bool state) => Responsive = state;

	internal void GetImageSize(out int w, out int h) {
		w = h = 0;

		int tw = 0, th = 0;
		Enabled?.GetSize(out w, out h);
		Disabled?.GetSize(out tw, out th);
		if (tw > w)
			w = tw;

		if (th > h)
			h = th;
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		EnabledColor = GetSchemeColor("FrameSystemButton.FgColor", scheme);
		DisabledColor = GetSchemeColor("FrameSystemButton.BgColor", scheme);

		ReadOnlySpan<char> enabledImage = string.IsNullOrEmpty(EnabledImage) ? scheme.GetResourceString("FrameSystemButton.Icon") : EnabledImage;
		ReadOnlySpan<char> disabledImage = string.IsNullOrEmpty(DisabledImage) ? scheme.GetResourceString("FrameSystemButton.DisabledIcon") : DisabledImage;
		Enabled = SchemeManager.GetImage(enabledImage, false);
		Disabled = SchemeManager.GetImage(disabledImage, false);

		SetTextInset(0, 0);
		SetEnabled(IsEnabled());
	}

	public override IBorder? GetBorder(bool depressed, bool armed, bool selected, bool keyfocus) => null;

	public override void SetEnabled(bool state) {
		base.SetEnabled(state);

		if (IsEnabled()) {
			if (Enabled != null)
				SetImageAtIndex(0, Enabled, 0);

			SetBgColor(EnabledColor);
			SetDefaultColor(EnabledColor, EnabledColor);
			SetArmedColor(EnabledColor, EnabledColor);
			SetDepressedColor(EnabledColor, EnabledColor);
		}
		else {
			if (Disabled != null)
				SetImageAtIndex(0, Disabled, 0);

			SetBgColor(DisabledColor);
			SetDefaultColor(DisabledColor, DisabledColor);
			SetArmedColor(DisabledColor, DisabledColor);
			SetDepressedColor(DisabledColor, DisabledColor);
		}
	}

	public override void OnMousePressed(ButtonCode code) {
		if (!Responsive)
			return;

		base.OnMousePressed(code);
	}

	public override void OnMouseDoublePressed(ButtonCode code) {
		if (!Responsive)
			return;

		if (code == ButtonCode.MouseLeft) {
			Menu? menu = GetMenu();
			if (menu != null && menu.FindChildByName("Close") != null)
				PostMessage(GetParent(), new KeyValues("CloseFrameButtonPressed"));
		}
	}
}

public class GripPanel : Panel
{
	public const int DEFAULT_SNAP_RANGE = 10;
	protected Frame Frame;
	protected bool Dragging;
	protected int DragMultX;
	protected int DragMultY;
	protected readonly int[] DragOrgPos = new int[2];
	protected readonly int[] DragOrgSize = new int[2];
	protected readonly int[] DragStart = new int[2];
	protected IFont? MarlettFont;
	protected int SnapRange;

	public GripPanel(Frame dragFrame, ReadOnlySpan<char> name, int xdir, int ydir) : base(dragFrame, name) {
		Frame = dragFrame;
		Dragging = false;
		DragMultX = xdir;
		DragMultY = ydir;
		MarlettFont = null;
		SetPaintEnabled(false);
		SetPaintBackgroundEnabled(false);
		SetPaintBorderEnabled(false);
		SnapRange = DEFAULT_SNAP_RANGE;

		if (xdir == 1 && ydir == 1) {
			SetPaintEnabled(true);
			SetPaintBackgroundEnabled(true);
		}

		SetBlockDragChaining(true);
	}

	public override void Paint() {
		Surface.DrawSetTextFont(MarlettFont);
		Surface.DrawSetTextPos(0, 0);
		Surface.DrawSetTextColor(GetFgColor());
		Surface.DrawChar('p');
	}
	public override void PaintBackground() {
		Surface.DrawSetTextFont(MarlettFont);
		Surface.DrawSetTextPos(0, 0);
		Surface.DrawSetTextColor(GetBgColor());
		Surface.DrawChar('o');
	}
	public override void OnMouseReleased(ButtonCode code) {
		Dragging = false;
		Input.SetMouseCapture(null);
	}
	public override void OnMousePressed(ButtonCode code) {
		if (code == ButtonCode.MouseLeft) {
			Dragging = true;
			Input.GetCursorPos(out int x, out int y);
			DragStart[0] = x;
			DragStart[1] = y;
			Frame.GetPos(out DragOrgPos[0], out DragOrgPos[1]);
			Frame.GetSize(out DragOrgSize[0], out DragOrgSize[1]);
			Input.SetMouseCapture(this);

			IPanel? focus = Input.GetFocus();
			if (focus == null || !focus.HasParent(Frame))
				Frame.RequestFocus();

			Frame.Repaint();
		}
		else
			GetParent()!.OnMousePressed(code);
	}
	public override void OnMouseCaptureLost() {
		base.OnMouseCaptureLost();
		Dragging = false;
	}
	public override void OnCursorMoved(int x, int y) {
		if (!Dragging)
			return;

		if (!Input.IsMouseDown(ButtonCode.MouseLeft)) {
			OnMouseReleased(ButtonCode.MouseLeft);
			return;
		}

		Input.GetCursorPos(out x, out y);
		Moved((x - DragStart[0]), (y - DragStart[1]));
		Frame.Repaint();
	}
	protected virtual void Moved(int dx, int dy) {
		if (!Frame.IsSizeable())
			return;

		int newX = DragOrgPos[0], newY = DragOrgPos[1];
		int newWide = DragOrgSize[0], newTall = DragOrgSize[1];

		Frame.GetMinimumSize(out int minWide, out int minTall);

		newWide += (dx * DragMultX);
		if (DragMultX == -1) {
			if (newWide < minWide)
				dx = DragOrgSize[0] - minWide;

			newX += dx;
		}

		newTall += (dy * DragMultY);
		if (DragMultY == -1) {
			if (newTall < minTall)
				dy = DragOrgSize[1] - minTall;

			newY += dy;
		}

		if (Frame.GetClipToParent()) {
			if (newX < 0)
				newX = 0;
			if (newY < 0)
				newY = 0;

			Surface.GetScreenSize(out int sx, out int sy);

			Frame.GetSize(out int w, out int h);
			if (newX + w > sx) {
				newX = sx - w;
			}
			if (newY + h > sy) {
				newY = sy - h;
			}
		}

		Frame.SetPos(newX, newY);
		Frame.SetSize(newWide, newTall);
		Frame.InvalidateLayout();
		Frame.Repaint();
	}
	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		bool issmall = ((Frame)GetParent()!).IsSmallCaption();

		MarlettFont = scheme.GetFont(issmall ? "MarlettSmall" : "Marlett", IsProportional());
		SetFgColor(GetSchemeColor("FrameGrip.Color1", scheme));
		SetBgColor(GetSchemeColor("FrameGrip.Color2", scheme));

		ReadOnlySpan<char> snapRange = scheme.GetResourceString("Frame.AutoSnapRange");
		if (!snapRange.IsEmpty && snapRange.Length > 0)
			SnapRange = int.TryParse(snapRange, out int r) ? r : 0;
	}
}

public class CaptionGripPanel : GripPanel
{
	public const int CAPTION_TITLE_BORDER = 7;
	public const int CAPTION_TITLE_BORDER_SMALL = 0;

	public CaptionGripPanel(Frame dragFrame, ReadOnlySpan<char> name) : base(dragFrame, name, 0, 0) {
		Frame = dragFrame;
	}

	protected override void Moved(int dx, int dy) {
		if (!Frame.IsMoveable())
			return;

		int newX = DragOrgPos[0] + dx;
		int newY = DragOrgPos[1] + dy;

		if (SnapRange != 0) {
			Surface.GetWorkspaceBounds(out int wx, out int wy, out int ww, out int wt);
			GetInsideSnapPosition(wx, wy, ww, wt, ref newX, ref newY);

			IPanel root = Surface.GetEmbeddedPanel();
			for (int i = 0; i < root.GetChildCount(); ++i) {
				IPanel child = root.GetChild(i);
				TryToDock(child, ref newX, ref newY);
			}
		}

		if (Frame.GetClipToParent()) {
			if (newX < 0)
				newX = 0;
			if (newY < 0)
				newY = 0;

			Surface.GetScreenSize(out int sx, out int sy);

			((IPanel)Frame).GetSize(out int w, out int h);
			if (newX + w > sx) {
				newX = sx - w;
			}
			if (newY + h > sy) {
				newY = sy - h;
			}
		}

		((IPanel)Frame).SetPos(newX, newY);

	}

	void TryToDock(IPanel window, ref int newX, ref int newY) {
		if (window == Frame)
			return;

		if (window.IsVisible() && window.IsPopup()) {
			window.GetAbsPos(out int cx, out int cy);
			window.GetSize(out int cw, out int ct);
			bool snapped = GetOutsideSnapPosition(cx, cy, cw, ct, ref newX, ref newY);
			if (snapped)
				return;
		}

		for (int i = 0; i < window.GetChildCount(); ++i) {
			IPanel child = window.GetChild(i);
			TryToDock(child, ref newX, ref newY);
		}

	}
	bool GetInsideSnapPosition(int boundX, int boundY, int boundWide, int boundTall, ref int snapToX, ref int snapToY) {
		Frame.GetSize(out int wide, out int tall);
		Assert(wide > 0);
		Assert(tall > 0);

		bool snapped = false;
		if (Math.Abs(snapToX - boundX) < SnapRange) {
			snapToX = boundX;
			snapped = true;
		}
		else if (Math.Abs((snapToX + wide) - (boundX + boundWide)) < SnapRange) {
			snapToX = boundX + boundWide - wide;
			snapped = true;
		}

		if (Math.Abs(snapToY - boundY) < SnapRange) {
			snapToY = boundY;
			snapped = true;
		}
		else if (Math.Abs((snapToY + tall) - (boundY + boundTall)) < SnapRange) {
			snapToY = boundY + boundTall - tall;
			snapped = true;
		}
		return snapped;

	}
	bool GetOutsideSnapPosition(int left, int top, int boundWide, int boundTall, ref int snapToX, ref int snapToY) {
		Assert(boundWide >= 0);
		Assert(boundTall >= 0);

		bool snapped = false;

		int right = left + boundWide;
		int bottom = top + boundTall;

		Frame.GetSize(out int wide, out int tall);
		Assert(wide > 0);
		Assert(tall > 0);

		bool horizSnappable = ((snapToY > top) && (snapToY < bottom))
							 || ((snapToY + tall > top) && (snapToY + tall < bottom))
							 || ((snapToY < top) && (snapToY + tall > bottom));


		bool vertSnappable = ((snapToX > left) && (snapToX < right))
							|| ((snapToX + wide > left) && (snapToX + wide < right))
							|| ((snapToX < left) && (snapToX + wide > right));

		if (!(horizSnappable || vertSnappable))
			return false;

		if ((snapToX <= (right + SnapRange)) &&
			(snapToX >= (right - SnapRange))) {
			if (horizSnappable) {
				snapped = true;
				snapToX = right;
			}
		}
		else if ((snapToX + wide) >= (left - SnapRange) &&
			(snapToX + wide) <= (left + SnapRange)) {
			if (horizSnappable) {
				snapped = true;
				snapToX = left - wide;
			}
		}

		if ((snapToY <= (bottom + SnapRange)) &&
			(snapToY >= (bottom - SnapRange))) {
			if (vertSnappable) {
				snapped = true;
				snapToY = bottom;
			}
		}
		else if ((snapToY + tall) <= (top + SnapRange) &&
			(snapToY + tall) >= (top - SnapRange)) {
			if (vertSnappable) {
				snapped = true;
				snapToY = top - tall;
			}
		}
		return snapped;
	}
}

public class FrameButton : Button
{
	public IBorder? BrightBorder, DepressedBorder, DisabledBorder;
	public Color EnabledFgColor, EnabledBgColor, DisabledFgColor, DisabledBgColor;
	public bool DisabledLook;
	public FrameButton(Panel parent, ReadOnlySpan<char> name, ReadOnlySpan<char> text) : base(parent, name, text) {
		SetSize(GetButtonSide((Frame)parent), GetButtonSide((Frame)parent));
		BrightBorder = null;
		DepressedBorder = null;
		DisabledBorder = null;
		DisabledLook = true;
		SetContentAlignment(Alignment.Northwest);
		SetTextInset(2, 1);
		SetBlockDragChaining(true);
	}

	public virtual void SetDisabledLook(bool state) {
		DisabledLook = state;
		if (!DisabledLook) {
			SetDefaultColor(EnabledFgColor, EnabledBgColor);
			SetArmedColor(EnabledFgColor, EnabledBgColor);
			SetDepressedColor(EnabledFgColor, EnabledBgColor);
		}
		else {
			SetDefaultColor(DisabledFgColor, DisabledBgColor);
			SetArmedColor(DisabledFgColor, DisabledBgColor);
			SetDepressedColor(DisabledFgColor, DisabledBgColor);
		}
	}

	public override void PerformLayout() {
		base.PerformLayout();
		Repaint();
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

	public static int GetButtonSide(Frame frame) {
		if (frame.IsSmallCaption())
			return 12;
		return 18;
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		EnabledFgColor = GetSchemeColor("FrameTitleButton.FgColor", scheme);
		EnabledBgColor = GetSchemeColor("FrameTitleButton.BgColor", scheme);

		DisabledFgColor = GetSchemeColor("FrameTitleButton.DisabledFgColor", scheme);
		DisabledBgColor = GetSchemeColor("FrameTitleButton.DisabledBgColor", scheme);

		BrightBorder = scheme.GetBorder("TitleButtonBorder");
		DepressedBorder = scheme.GetBorder("TitleButtonDepressedBorder");
		DisabledBorder = scheme.GetBorder("TitleButtonDisabledBorder");

		SetDisabledLook(DisabledLook);
	}

	public override IBorder? GetBorder(bool depressed, bool armed, bool selected, bool keyfocus) {
		if (DisabledLook)
			return DisabledBorder;

		if (depressed)
			return DepressedBorder;

		return BrightBorder;
	}
}

public class Frame : EditablePanel
{
	static Frame() => ChainToAnimationMap<Frame>();

	TextImage? Title;
	bool Moveable;
	bool Sizeable;
	bool HasFocus;
	bool FlashWindow;
	bool DrawTitleBar;
	bool PreviouslyVisible;
	bool FadingOut;
	bool DisableFadeEffect;
	double TransitionEffectTime;
	double FocusTransitionEffectTime;
	bool DeleteSelfOnClose;
	int ClientInsetX;
	int ClientInsetY;
	bool ClientInsetXOverridden;
	int TitleTextInsetX;
	bool ClipToParent;
	bool SmallCaption;
	bool ChainKeysToParent;
	bool Primed;
	IFont? CustomTitleFont;

	Color TitleBarBgColor;
	Color TitleBarDisabledBgColor;
	Color TitleBarFgColor;
	Color TitleBarDisabledFgColor;
	Color InFocusBgColor;
	Color OutOfFocusBgColor;

	Panel? TopGrip;
	Panel? BottomGrip;
	Panel? LeftGrip;
	Panel? RightGrip;
	Panel? TopLeftGrip;
	Panel? TopRightGrip;
	Panel? BottomLeftGrip;
	Panel? BottomRightGrip;
	Panel? CaptionGrip;
	FrameButton? MinimizeButton;
	FrameButton? MaximizeButton;
	FrameButton? MinimizeToSysTrayButton;
	FrameButton? CloseButton;
	FrameSystemButton? MenuButton;
	Menu? SysMenu;

	public void SetDeleteSelfOnClose(bool state) => DeleteSelfOnClose = state;

	IPanel? PreviousModal;

	public virtual void Activate() {
		MoveToFront();
		if (IsKeyboardInputEnabled())
			RequestFocus();

		SetVisible(true);
		SetEnabled(true);
		if (FadingOut) {
			FadingOut = false;
			PreviouslyVisible = false;
		}
		Surface.SetMinimized(this, false);
	}

	public virtual void DoModal() {
		MoveToCenterOfScreen();
		InvalidateLayout();
		Activate();
		PreviousModal = Input.GetAppModalSurface();
		Input.SetAppModalSurface(this);
	}

	public Frame(Panel? parent, ReadOnlySpan<char> name, bool showTaskbarIcon = true, bool popup = true) : base(parent, name) {
		SetVisible(false);
		if (popup)
			MakePopup(showTaskbarIcon);

		Title = null;
		Moveable = true;
		Sizeable = true;
		HasFocus = false;
		FlashWindow = false;
		DrawTitleBar = true;
		PreviouslyVisible = false;
		FadingOut = false;
		DisableFadeEffect = false;
		TransitionEffectTime = 0.0f;
		FocusTransitionEffectTime = 0.0f;
		DeleteSelfOnClose = false;
		ClientInsetX = 5;
		ClientInsetY = 5;
		ClientInsetXOverridden = false;
		TitleTextInsetX = 28;
		ClipToParent = false;
		SmallCaption = false;
		ChainKeysToParent = false;
		Primed = false;
		CustomTitleFont = null;

		SetTitle("#Frame_Untitled", parent == null);
		SetBuildGroup(GetBuildGroup());
		SetMinimumSize(128, 66);
		GetFocusNavGroup().SetFocusTopLevel(true);

		SysMenu = null;

		TopGrip = new GripPanel(this, "frame_topGrip", 0, -1);
		BottomGrip = new GripPanel(this, "frame_bottomGrip", 0, 1);
		LeftGrip = new GripPanel(this, "frame_leftGrip", -1, 0);
		RightGrip = new GripPanel(this, "frame_rightGrip", 1, 0);
		TopLeftGrip = new GripPanel(this, "frame_tlGrip", -1, -1);
		TopRightGrip = new GripPanel(this, "frame_trGrip", 1, -1);
		BottomLeftGrip = new GripPanel(this, "frame_blGrip", -1, 1);
		BottomRightGrip = new GripPanel(this, "frame_brGrip", 1, 1);
		CaptionGrip = new CaptionGripPanel(this, "frame_caption");
		CaptionGrip.SetCursor(CursorCode.Arrow);

		MinimizeButton = new FrameButton(this, "frame_minimize", "0");
		MinimizeButton.AddActionSignalTarget(this);
		MinimizeButton.SetCommand(new KeyValues("Minimize"));

		MaximizeButton = new FrameButton(this, "frame_maximize", "1");
		SetMaximizeButtonVisible(false);

		Span<char> str = [(char)0x30, '\0'];
		MinimizeToSysTrayButton = new FrameButton(this, "frame_mintosystray", str);
		MinimizeToSysTrayButton.SetCommand("MinimizeToSysTray");
		SetMinimizeToSysTrayButtonVisible(false);

		CloseButton = new FrameButton(this, "frame_close", "r");
		CloseButton.AddActionSignalTarget(this);
		CloseButton.SetCommand(new KeyValues("CloseFrameButtonPressed"));

		if (!Surface.SupportsFeature(SurfaceFeature.FrameMinimizeMaximize)) {
			SetMinimizeButtonVisible(false);
			SetMaximizeButtonVisible(false);
		}

		if (parent != null) {
			SetMinimizeButtonVisible(false);
			SetMaximizeButtonVisible(false);
		}

		MenuButton = new FrameSystemButton(this, "frame_menu");
		MenuButton.SetMenu(GetSysMenu());

		SetupResizeCursors();

		RegisterColorAsOverridable(InFocusBgColor, "infocus_bgcolor_override");
		RegisterColorAsOverridable(OutOfFocusBgColor, "outoffocus_bgcolor_override");
		RegisterColorAsOverridable(TitleBarBgColor, "titlebarbgcolor_override");
		RegisterColorAsOverridable(TitleBarDisabledBgColor, "titlebardisabledbgcolor_override");
		RegisterColorAsOverridable(TitleBarFgColor, "titlebarfgcolor_override");
		RegisterColorAsOverridable(TitleBarDisabledFgColor, "titlebardisabledfgcolor_override");
	}

	private void SetupResizeCursors() {
		if (IsSizeable()) {
			TopGrip?.SetCursor(CursorCode.SizeNS);
			BottomGrip?.SetCursor(CursorCode.SizeNS);
			LeftGrip?.SetCursor(CursorCode.SizeWE);
			RightGrip?.SetCursor(CursorCode.SizeWE);
			TopLeftGrip?.SetCursor(CursorCode.SizeNWSE);
			TopRightGrip?.SetCursor(CursorCode.SizeNESW);
			BottomLeftGrip?.SetCursor(CursorCode.SizeNESW);
			BottomRightGrip?.SetCursor(CursorCode.SizeNWSE);
			BottomRightGrip?.SetPaintEnabled(true);
			BottomRightGrip?.SetPaintBackgroundEnabled(true);
		}
		else {
			TopGrip?.SetCursor(CursorCode.Arrow);
			BottomGrip?.SetCursor(CursorCode.Arrow);
			LeftGrip?.SetCursor(CursorCode.Arrow);
			RightGrip?.SetCursor(CursorCode.Arrow);
			TopLeftGrip?.SetCursor(CursorCode.Arrow);
			TopRightGrip?.SetCursor(CursorCode.Arrow);
			BottomLeftGrip?.SetCursor(CursorCode.Arrow);
			BottomRightGrip?.SetCursor(CursorCode.Arrow);
			BottomRightGrip?.SetPaintEnabled(false);
			BottomRightGrip?.SetPaintBackgroundEnabled(false);
		}
	}

	public void SetMenuButtonResponsive(bool state) => MenuButton?.SetResponsive(state);
	public void SetMinimizeButtonVisible(bool state) => MinimizeButton?.SetVisible(state);
	public void SetMaximizeButtonVisible(bool state) => MaximizeButton?.SetVisible(state);
	public void SetMinimizeToSysTrayButtonVisible(bool state) => MinimizeToSysTrayButton?.SetVisible(state);
	public void SetCloseButtonVisible(bool state) => CloseButton?.SetVisible(state);

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		SetOverridableColor(ref TitleBarFgColor, GetSchemeColor("FrameTitleBar.TextColor", scheme));
		SetOverridableColor(ref TitleBarBgColor, GetSchemeColor("FrameTitleBar.BgColor", scheme));
		SetOverridableColor(ref TitleBarDisabledFgColor, GetSchemeColor("FrameTitleBar.DisabledTextColor", scheme));
		SetOverridableColor(ref TitleBarDisabledBgColor, GetSchemeColor("FrameTitleBar.DisabledBgColor", scheme));

		ReadOnlySpan<char> font;
		if (SmallCaption)
			font = scheme.GetResourceString("FrameTitleBar.SmallFont");
		else
			font = scheme.GetResourceString("FrameTitleBar.Font");


		IFont? titlefont;
		if (CustomTitleFont != null)
			titlefont = CustomTitleFont;
		else
			titlefont = scheme.GetFont((!font.IsEmpty && font.Length > 0) ? font : "Default", IsProportional());

		Title?.SetFont(titlefont);
		Title?.ResizeImageToContent();

		IFont? marfont = null;
		if (SmallCaption)
			marfont = scheme.GetFont("MarlettSmall", IsProportional());
		else
			marfont = scheme.GetFont("Marlett", IsProportional());

		MinimizeButton?.SetFont(marfont);
		MaximizeButton?.SetFont(marfont);
		MinimizeToSysTrayButton?.SetFont(marfont);
		CloseButton?.SetFont(marfont);

		TransitionEffectTime = float.TryParse(scheme.GetResourceString("Frame.TransitionEffectTime"), out float r) ? r : 0;
		FocusTransitionEffectTime = float.TryParse(scheme.GetResourceString("Frame.FocusTransitionEffectTime"), out r) ? r : 0;

		SetOverridableColor(ref InFocusBgColor, scheme.GetColor("Frame.BgColor", GetBgColor()));
		SetOverridableColor(ref OutOfFocusBgColor, scheme.GetColor("Frame.OutOfFocusBgColor", InFocusBgColor));

		ReadOnlySpan<char> resourceString = scheme.GetResourceString("Frame.ClientInsetX");
		if (!resourceString.IsEmpty)
			ClientInsetX = int.TryParse(resourceString, out int i1) ? i1 : 0;

		resourceString = scheme.GetResourceString("Frame.ClientInsetY");
		if (!resourceString.IsEmpty)
			ClientInsetY = int.TryParse(resourceString, out int i1) ? i1 : 0;

		resourceString = scheme.GetResourceString("Frame.TitleTextInsetX");
		if (!resourceString.IsEmpty)
			TitleTextInsetX = int.TryParse(resourceString, out int i1) ? i1 : 0;

		SetBgColor(InFocusBgColor);
		SetBorder(scheme.GetBorder("FrameBorder"));

		OnFrameFocusChanged(HasFocus);
	}

	const int CAPTION_TITLE_BORDER = 7;
	const int CAPTION_TITLE_BORDER_SMALL = 0;

	const int DRAGGER_SIZE = 5;
	const int CORNER_SIZE = 5;
	const int BOTTOM_RIGHT_SIZE = 18;
	const int CAPTION_HEIGHT = 23;
	public int GetDraggerSize() => SmallCaption ? 3 : DRAGGER_SIZE;
	public int GetCornerSize() => SmallCaption ? 6 : CORNER_SIZE;
	public int GetBottomRightSize() => SmallCaption ? 12 : BOTTOM_RIGHT_SIZE;
	public int GetCaptionHeight() => SmallCaption ? 12 : CAPTION_HEIGHT;

	public override void PerformLayout() {
		base.PerformLayout();

		GetSize(out int wide, out int tall);

		int DRAGGER_SIZE = GetDraggerSize();
		int CORNER_SIZE = GetCornerSize();
		int CORNER_SIZE2 = CORNER_SIZE * 2;
		int BOTTOMRIGHTSIZE = GetBottomRightSize();

		TopGrip?.SetBounds(CORNER_SIZE, 0, wide - CORNER_SIZE2, DRAGGER_SIZE);
		LeftGrip?.SetBounds(0, CORNER_SIZE, DRAGGER_SIZE, tall - CORNER_SIZE2);
		TopLeftGrip?.SetBounds(0, 0, CORNER_SIZE, CORNER_SIZE);
		TopRightGrip?.SetBounds(wide - CORNER_SIZE, 0, CORNER_SIZE, CORNER_SIZE);
		BottomLeftGrip?.SetBounds(0, tall - CORNER_SIZE, CORNER_SIZE, CORNER_SIZE);

		BottomGrip?.SetBounds(CORNER_SIZE, tall - DRAGGER_SIZE, wide - (CORNER_SIZE + BOTTOMRIGHTSIZE), DRAGGER_SIZE);
		RightGrip?.SetBounds(wide - DRAGGER_SIZE, CORNER_SIZE, DRAGGER_SIZE, tall - (CORNER_SIZE + BOTTOMRIGHTSIZE));

		BottomRightGrip?.SetBounds(wide - BOTTOMRIGHTSIZE, tall - BOTTOMRIGHTSIZE, BOTTOMRIGHTSIZE, BOTTOMRIGHTSIZE);

		CaptionGrip?.SetSize(wide - 10, GetCaptionHeight());

		TopGrip?.MoveToFront();
		BottomGrip?.MoveToFront();
		LeftGrip?.MoveToFront();
		RightGrip?.MoveToFront();
		TopLeftGrip?.MoveToFront();
		TopRightGrip?.MoveToFront();
		BottomLeftGrip?.MoveToFront();
		BottomRightGrip?.MoveToFront();

		MaximizeButton?.MoveToFront();
		MenuButton?.MoveToFront();
		MinimizeButton?.MoveToFront();
		MinimizeToSysTrayButton?.MoveToFront();
		MenuButton?.SetBounds(5 + 2, 5 + 3, GetCaptionHeight() - 5, GetCaptionHeight() - 5);

		float scale = 1;
		if (IsProportional()) {
			Surface.GetScreenSize(out _, out int screenH);
			Surface.GetProportionalBase(out _, out int proH);
			scale = screenH / proH;
		}

		int offset_start = (int)(20 * scale);
		int offset = offset_start;

		int top_border_offset = (int)((5 + 3) * scale);
		if (SmallCaption)
			top_border_offset = (int)((3) * scale);

		int side_border_offset = (int)(5 * scale);

		if (CloseButton?.IsVisible() ?? false) {
			CloseButton.SetPos((wide - side_border_offset) - offset, top_border_offset);
			offset += offset_start;
			LayoutProportional(CloseButton);

		}
		if (MinimizeToSysTrayButton?.IsVisible() ?? false) {
			MinimizeToSysTrayButton.SetPos((wide - side_border_offset) - offset, top_border_offset);
			offset += offset_start;
			LayoutProportional(MinimizeToSysTrayButton);
		}
		if (MaximizeButton?.IsVisible() ?? false) {
			MaximizeButton.SetPos((wide - side_border_offset) - offset, top_border_offset);
			offset += offset_start;
			LayoutProportional(MaximizeButton);
		}
		if (MinimizeButton?.IsVisible() ?? false) {
			MinimizeButton.SetPos((wide - side_border_offset) - offset, top_border_offset);
			offset += offset_start;
			LayoutProportional(MinimizeButton);
		}
	}

	private void LayoutProportional(FrameButton button) {
		float scale = 1.0f;

		if (IsProportional()) {
			Surface.GetScreenSize(out _, out int screenH);
			Surface.GetProportionalBase(out _, out int proH);

			scale = (float)screenH / proH;
		}

		button.SetSize((int)(FrameButton.GetButtonSide(this) * scale), (int)(FrameButton.GetButtonSide(this) * scale));
		button.SetTextInset((int)(MathF.Ceiling(2 * scale)), (int)(MathF.Ceiling(1 * scale)));
	}

	public void GetClientArea(out int x, out int y, out int wide, out int tall) {
		x = ClientInsetX;
		y = 0;

		GetSize(out wide, out tall);

		if (DrawTitleBar) {
			int captionTall = Surface.GetFontTall(Title?.GetFont());

			int border = SmallCaption ? CAPTION_TITLE_BORDER_SMALL : CAPTION_TITLE_BORDER;
			int yinset = SmallCaption ? 0 : ClientInsetY;

			yinset += TitleTextInsetYOverride;

			y = yinset + captionTall + border + 1;
			tall = (tall - yinset) - y;
		}

		if (SmallCaption)
			tall -= 5;

		wide = (wide - ClientInsetX) - x;
	}

	public override void LoadControlSettings(ReadOnlySpan<char> resourceName, ReadOnlySpan<char> pathID = default, KeyValues? keyValues = null, KeyValues? conditions = null) {
		base.LoadControlSettings(resourceName, pathID, keyValues, conditions);
		GetFocusNavGroup().GetDefaultPanel()?.RequestFocus();
	}
	public override void OnClose() {
		if (Input.GetAppModalSurface() == this) {
			Input.ReleaseAppModalSurface();
			if (PreviousModal != null) {
				Input.SetAppModalSurface(PreviousModal);
				PreviousModal = null;
			}
		}

		base.OnClose();

		if (TransitionEffectTime != 0 && !DisableFadeEffect) {
			GetAnimationController().RunAnimationCommand(this, "alpha", 0.0f, 0.0f, TransitionEffectTime, Interpolators.Linear);
			FadingOut = true;
			Surface.MovePopupToBack(this);
		}
		else
			FinishClose();
	}

	public void DisableFadeEffects(bool state = true) {
		DisableFadeEffect = state;
		TransitionEffectTime = 0.0f;
	}

	public void SetFadeEffectDisableOverride(bool state) => DisableFadeEffect = state;

	public override void ApplySettings(KeyValues resourceData) {
		resourceData.SetInt("visible", -1);
		base.ApplySettings(resourceData);

		SetCloseButtonVisible(resourceData.GetBool("setclosebuttonvisible", true));

#if GMOD_DLL
		if (resourceData.GetInt("settitlevisible", 1) != 0)
			DrawTitleBar = true;
#endif

		if (resourceData.GetInt("settitlebarvisible", 1) == 0)
			SetTitleBarVisible(false);

		ReadOnlySpan<char> title = resourceData.GetString("title", "");
		if (!title.IsEmpty && title.Length > 0) {
			SetTitle(title, true);
		}

		ReadOnlySpan<char> titlefont = resourceData.GetString("title_font", "");
		if (!titlefont.IsEmpty && titlefont.Length > 0) {
			IScheme? scheme = GetScheme();
			if (scheme != null)
				CustomTitleFont = scheme.GetFont(titlefont);
		}

		KeyValues? clientInsetXOverride = resourceData.FindKey("clientinsetx_override", false);
		if (clientInsetXOverride != null) {
			ClientInsetX = clientInsetXOverride.GetInt();
			ClientInsetXOverridden = true;
		}
	}

	public void SetTitleBarVisible(bool state) {
		DrawTitleBar = state;
		SetMenuButtonVisible(state);
		SetMinimizeButtonVisible(state);
		SetMaximizeButtonVisible(state);
		SetCloseButtonVisible(state);
	}

	public void SetMenuButtonVisible(bool state) {
		MenuButton?.SetVisible(state);
	}

	public Menu GetSysMenu() {
		if (SysMenu == null) {
			SysMenu = new Menu(this, null);
			SysMenu.SetVisible(false);
			SysMenu.AddActionSignalTarget(this);

			SysMenu.AddMenuItem("Minimize", "#SysMenu_Minimize", "Minimize", this);
			SysMenu.AddMenuItem("Maximize", "#SysMenu_Maximize", "Maximize", this);
			SysMenu.AddMenuItem("Close", "#SysMenu_Close", "Close", this);

			Panel menuItem = SysMenu.FindChildByName("Minimize")!;
			menuItem?.SetEnabled(MinimizeButton!.IsVisible());

			menuItem = SysMenu.FindChildByName("Maximize")!;
			menuItem?.SetEnabled(MaximizeButton!.IsVisible());

			menuItem = SysMenu.FindChildByName("Close")!;
			menuItem?.SetEnabled(CloseButton!.IsVisible());

			return SysMenu;
		}

		return SysMenu;
	}

	[UiMessage("Close")]
	[UiCommand("Close")]
	public virtual void Close() {
		OnClose();
	}

	[UiMessage("CloseModal")]
	[UiCommand("CloseModal")]
	private void CloseModal() {
		Input.ReleaseAppModalSurface();
		if (PreviousModal != null) {
			Input.SetAppModalSurface(PreviousModal);
			PreviousModal = null;
		}
		PostMessage(this, new("Close"));
	}

	private void FinishClose() {
		SetVisible(false);
		PreviouslyVisible = false;
		FadingOut = false;

		OnFinishedClose();

		if (DeleteSelfOnClose)
			MarkForDeletion();
	}
	public override void OnThink() {
		base.OnThink();

		// Msg($"{GetAlpha()}\n");

		if (IsVisible() && TransitionEffectTime > 0 && !DisableFadeEffect) {
			if (FadingOut) {
				if (GetAlpha() < 1)
					FinishClose();
			}
			else if (!PreviouslyVisible) {
				PreviouslyVisible = true;
				SetAlpha(0);
				GetAnimationController().RunAnimationCommand(this, "alpha", 255.0f, 0.0f, TransitionEffectTime, Interpolators.Linear);
			}
		}

		bool hasFocus = false;

		if (Input != null) {
			IPanel? focus = Input.GetFocus();
			if (focus != null && focus.HasParent(this)) {
				if (Input.GetAppModalSurface() == null || Input.GetAppModalSurface() == this) {
					hasFocus = true;
				}
			}
		}
		if (hasFocus != HasFocus) {
			if (!Primed) {
				Primed = true;
				return;
			}
			Primed = false;
			HasFocus = hasFocus;
			OnFrameFocusChanged(HasFocus);
		}
		else
			Primed = false;
	}

	public override void OnScreenSizeChanged(int oldWide, int oldTall) {
		base.OnScreenSizeChanged(oldWide, oldTall);

		if (IsProportional())
			return;

		Surface.GetScreenSize(out int newWide, out int newTall);
		GetBounds(out int x, out int y, out int wide, out int tall);

		if (x + wide > newWide)
			x = newWide - wide;

		if (y + tall > newTall)
			y = newTall - tall;

		x = Math.Max(0, x);
		y = Math.Max(0, y);

		SetPos(x, y);
	}

	private void OnFinishedClose() { }

	private void OnFrameFocusChanged(bool hasFocus) {
		MinimizeButton?.SetDisabledLook(!hasFocus);
		MaximizeButton?.SetDisabledLook(!hasFocus);
		CloseButton?.SetDisabledLook(!hasFocus);
		MinimizeToSysTrayButton?.SetDisabledLook(!hasFocus);
		MenuButton?.SetEnabled(hasFocus);
		MinimizeButton?.InvalidateLayout();
		MaximizeButton?.InvalidateLayout();
		MinimizeToSysTrayButton?.InvalidateLayout();
		CloseButton?.InvalidateLayout();
		MenuButton?.InvalidateLayout();

		if (HasFocus)
			Title?.SetColor(TitleBarFgColor);
		else
			Title?.SetColor(TitleBarDisabledFgColor);

		if (HasFocus) {
			if (FocusTransitionEffectTime != 0 && (!DisableFadeEffect))
				GetAnimationController().RunAnimationCommand(this, "BgColor", InFocusBgColor, 0.0f, DisableFadeEffect ? 0.0f : FocusTransitionEffectTime, Interpolators.Linear);
			else
				SetBgColor(InFocusBgColor);
		}
		else {
			if (FocusTransitionEffectTime != 0 && (!DisableFadeEffect))
				GetAnimationController().RunAnimationCommand(this, "BgColor", OutOfFocusBgColor, 0.0f, DisableFadeEffect ? 0.0f : FocusTransitionEffectTime, Interpolators.Linear);
			else
				SetBgColor(OutOfFocusBgColor);
		}

		if (HasFocus && FlashWindow)
			FlashWindowStop();
	}

	private void FlashWindowStop() {
		Surface.FlashWindow(this, false);
		FlashWindow = false;
	}

	public void MoveToCenterOfScreen() {
		Surface.GetWorkspaceBounds(out _, out _, out int w, out int t);
		SetPos((w - GetWide()) / 2, (t - GetTall()) / 2);
	}

	[PanelAnimationVarAliasType("titletextinsetX", "0", "proportional_int")] int TitleTextInsetXOverride;
	[PanelAnimationVar("titletextinsetY", "0")] int TitleTextInsetYOverride;

	public override void PaintBackground() {
		Color titleColor = TitleBarDisabledBgColor;
		if (HasFocus) {
			titleColor = TitleBarBgColor;
		}

		base.PaintBackground();

		if (DrawTitleBar) {
			int wide = GetWide();
			int tall = Surface.GetFontTall(Title?.GetFont());
			if (tall == 0)
				tall = 24; // temporary

			Surface.DrawSetColor(titleColor);
			int inset = SmallCaption ? 3 : 5;
			int captionHeight = SmallCaption ? 14 : 28;

			Surface.DrawFilledRect(inset, inset, wide - inset, captionHeight);

			if (Title != null) {
				int nTitleX = TitleTextInsetXOverride != 0 ? TitleTextInsetXOverride : TitleTextInsetX;
				int nTitleWidth = wide - 72;

				if (MenuButton != null && MenuButton.IsVisible()) {
					MenuButton.GetImageSize(out int mw, out _);
					nTitleX += mw;
					nTitleWidth -= mw;
				}

				int nTitleY;
				if (TitleTextInsetYOverride != 0)
					nTitleY = TitleTextInsetYOverride;
				else
					nTitleY = SmallCaption ? 2 : 9;

				Title.SetPos(nTitleX, nTitleY);
				Title.SetSize(nTitleWidth, tall);
				Title.Paint();
			}
		}
	}

	public void SetTitle(ReadOnlySpan<char> title, bool surfaceTitle) {
		if (Title == null) {
			Title = new TextImage("");
		}

		bool localized = false;
		if (!title.IsEmpty && title.Length > 0 && title[0] == '#') {
			ulong unlocalizedTextSymbol = Localize.FindIndex(title[1..]);
			localized = unlocalizedTextSymbol != ulong.MaxValue;
			if (localized)
				Title.SetText(Localize.GetValueByIndex(unlocalizedTextSymbol));
		}

		if (!localized)
			Title.SetText(title);

		if (surfaceTitle)
			Surface.SetTitle(this, title);

		Repaint();
	}

	public bool IsSizeable() => Sizeable;

	public void SetSizeable(bool state) {
		Sizeable = state;
		SetupResizeCursors();
	}

	public bool IsMoveable() => Moveable;
	public void SetMoveable(bool state) => Moveable = state;
	public bool GetClipToParent() => ClipToParent;
	public void SetClipToParent(bool state) => ClipToParent = state;

	public void SetSmallCaption(bool state) {
		SmallCaption = state;
		InvalidateLayout();
	}

	public bool IsSmallCaption() => SmallCaption;

	public override void OnMousePressed(ButtonCode code) {
		if (!IsBuildGroupEnabled()) {
			IPanel focus = Input.GetFocus()!;
			if (focus == null || !focus.HasParent(this))
				RequestFocus();
		}

		base.OnMousePressed(code);
	}

	[UiMessage("SetTitle")]
	private void InternalSetTitle([MessageParam("text")] ReadOnlySpan<char> title) => SetTitle(title, true);
	[UiMessage("CloseFrameButtonPressed")]
	private void OnCloseFrameButtonPressed() => DoCommand("Close");
	[UiMessage("Minimize")]
	private void OnMinimize() => Surface.SetMinimized(this, true);

	static readonly KeyValues KV_FlashWindow = new("FlashWindow");
	bool NextFlashState = false;
	[UiMessage("FlashWindow")]
	private void InternalFlashWindow() {
		if (FlashWindow) {
			NextFlashState = true;
			Surface.FlashWindow(this, true);
			NextFlashState = !NextFlashState;

			PostMessage(this, KV_FlashWindow, 1.8f);
		}
	}
}
