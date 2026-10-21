using CommunityToolkit.HighPerformance;

using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Utilities;

namespace Source.GUI.Controls;

public class ClickPanel : Panel
{

	private int TestIndex;
	private int ViewIndex;

	public static Panel Create_RichText() => new RichText(null, null);
	public ClickPanel(RichText richText) {
		ViewIndex = 0;
		TestIndex = 0;

		SetParent(richText);
		AddActionSignalTarget(richText);

		SetCursor(CursorCode.Hand);

		SetPaintBackgroundEnabled(false);
		SetPaintEnabled(false);
	}

	internal void SetTextIndex(int LinkStartIdx, int ViewStartIdx) {
		TestIndex = LinkStartIdx;
		ViewIndex = ViewStartIdx;
	}

	internal int GetTextIndex() {
		return TestIndex;
	}

	internal int GetViewTextIndex() {
		return ViewIndex;
	}

	public override void OnMousePressed(ButtonCode code) {
		if (code == ButtonCode.MouseLeft)
			PostActionSignal(new KeyValues("ClickPanel", "index", TestIndex));
		else
			GetParent()?.OnMousePressed(code);
	}
}

public class RichTextInterior : Panel
{
	public RichTextInterior(RichText parent, string name) : base(parent, name) {
		SetKeyboardInputEnabled(false);
		SetMouseInputEnabled(false);
		SetPaintBackgroundEnabled(false);
		SetPaintEnabled(false);
		RichText = parent;
	}

	RichText RichText;
}
public class RichText : Panel
{
	struct Fade
	{
		public double FadeStartTime;
		public double FadeLength;
		public double FadeSustain;
		public int OriginalAlpha;

		public override string ToString() {
			return $"Start {FadeStartTime}, Sustain {FadeSustain}, Length {FadeLength}, Original Alpha {OriginalAlpha}";
		}
	}
	struct FormatStreamPiece
	{
		public Color Color;
		public int PixelsIndent;
		public bool TextClickable;
		public UtlSymbol ClickableTextAction;
		public Fade Fade;
		public int TextStreamIndex;

		public override string ToString() {
			return $"Text[{TextStreamIndex}] Color {Color}, PixelsIndent {PixelsIndent}, Fade [{Fade}]";
		}
	}
	bool ResetFades;
	bool Interactive;
	bool UnusedScrollbarInvis;
	bool AllTextAlphaIsZero;
	public readonly List<char> TextStream = [];
	readonly List<int> LineBreaks = [];
	readonly List<FormatStreamPiece> FormatStream = [];
	bool RecalcLineBreaks;
	int RecalculateBreaksIndex;
	bool InvalidateVerticalScrollbarSlider;
	int CursorPos;
	bool MouseSelection;
	bool MouseDragSelection;
	readonly int[] Select = new int[2];
	int PixelsIndent;
	int MaxCharCount;
	IFont? Font;
	IFont? FontUnderline;
	Color SelectionColor;
	Color SelectionTextColor;
	bool CurrentTextClickable;
	int ClickableTextIndex;
	Color DefaultTextColor;
	int DrawOffsetX;
	int DrawOffsetY;
	Panel Interior;
	string? InitialText;
	bool RecalcSavedRenderState;
	protected ScrollBar VertScrollBar;
	List<ClickPanel> ClickableTextPanels = [];
	struct RenderState
	{
		public int X, Y;
		public Color TextColor;
		public int PixelsIndent;
		public bool TextClickable;
		public int FormatStreamIndex;
	}
	RenderState CachedRenderState;
	Menu? EditMenu;

	const int MAX_BUFFER_SIZE = 999999;
	const int DRAW_OFFSET_X = 3;
	const int DRAW_OFFSET_Y = 1;
	public RichText(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		AllTextAlphaIsZero = false;
		Font = null;
		FontUnderline = null;
		RecalcLineBreaks = true;
		InitialText = null;
		CursorPos = 0;
		MouseSelection = false;
		MouseDragSelection = false;
		VertScrollBar = new ScrollBar(this, "ScrollBar", true);
		VertScrollBar.AddActionSignalTarget(this);
		RecalcSavedRenderState = true;
		MaxCharCount = 64 * 1024;
		AddActionSignalTarget(this);
		Interior = new RichTextInterior(this, null);
		Select[0] = -1;
		Select[1] = -1;
		EditMenu = null;
		SetCursor(CursorCode.IBeam);
		GotoTextEnd();
		DefaultTextColor = new(0, 0, 0, 0);
		InvalidateLineBreakStream();
		if (IsProportional()) {
			Surface.GetProportionalBase(out int width, out int height);
			Surface.GetScreenSize(out int sw, out int sh);

			DrawOffsetX = (int)(DRAW_OFFSET_X * ((float)sw / width));
			DrawOffsetY = (int)(DRAW_OFFSET_Y * ((float)sw / width));
		}
		else {
			DrawOffsetX = DRAW_OFFSET_X;
			DrawOffsetY = DRAW_OFFSET_Y;
		}

		FormatStreamPiece stream = new();
		stream.Color = DefaultTextColor;
		stream.Fade.FadeStartTime = 0.0;
		stream.Fade.FadeLength = -1.0;
		stream.PixelsIndent = 0;
		stream.TextStreamIndex = 0;
		stream.TextClickable = false;
		FormatStream.Add(stream);

		ResetFades = false;
		Interactive = true;
		UnusedScrollbarInvis = false;
	}

	public override void OnMouseFocusTicked() {
		if (MouseDragSelection)
			OnCursorMoved(0, 0);
	}

	public override void OnCursorEntered() {
		MouseDragSelection = false;
	}

	public override void OnCursorExited() {
		if (MouseSelection) MouseDragSelection = true;
	}

	public override void OnCursorMoved(int x, int y) {
		if (!MouseSelection)
			return;

		Input.GetCursorPos(out int mx, out int my);
		ScreenToLocal(ref mx, ref my);
		CursorPos = PixelToCursorSpace(mx, my);

		if (CursorPos != Select[1]) {
			Select[1] = CursorPos;
			Repaint();
		}
	}

	public override void OnMousePressed(ButtonCode code) {
#if GMOD_DLL
		// todo: OnMousePressed hook
#endif
		if (code == ButtonCode.MouseLeft) {
			SelectNone();

			Input.GetCursorPos(out int x, out int y);
			ScreenToLocal(ref x, ref y);

			CursorPos = PixelToCursorSpace(x, y);

			if (Interactive) {
				Input.SetMouseCapture(this);
				MouseSelection = true;

				if (Select[0] < 0)
					Select[0] = CursorPos;
				Select[1] = CursorPos;

				RequestFocus();
				Repaint();
			}
		}
		else if (code == ButtonCode.MouseRight) {
			if (Interactive) {
				CreateEditMenu();
				Assert(EditMenu);

				OpenEditMenu();
			}
		}
	}

	public override void OnMouseReleased(ButtonCode code) {
#if GMOD_DLL
		// todo: OnMouseReleased hook
#endif
		MouseSelection = false;
		Input.SetMouseCapture(null);

		if (GetSelectedRange(out int start, out int end) && end - start == 0)
			Select[0] = -1;
	}

	public override void OnMouseWheeled(int delta) {
#if GMOD_DLL
		// todo: OnMouseWheeled hook
#endif
		MoveScrollBar(delta);
	}

	public void MoveScrollBar(int delta) {
		MoveScrollBarDirect(delta * 3);
	}

	public void MoveScrollBarDirect(int delta) {
		if (!VertScrollBar.IsVisible())
			return;

		int val = VertScrollBar.GetValue();
		val -= delta;
		VertScrollBar.SetValue(val);
		RecalcSavedRenderState = true;
	}

	public void SetDrawOffsets(int x, int y) {
		DrawOffsetX = x;
		DrawOffsetY = y;
	}
	public void SetDrawTextOnly() {
		SetDrawOffsets(0, 0);
		SetPaintBackgroundEnabled(false);
		SetPostChildPaintEnabled(false);
		Interior.SetVisible(false);
		SetVerticalScrollbar(false);
	}

	public void SetVerticalScrollbar(bool state) {
		if (VertScrollBar.IsVisible() != state) {
			VertScrollBar.SetVisible(state);
			InvalidateLineBreakStream();
			InvalidateLayout();
		}
	}

	public void CreateEditMenu() {
		if (EditMenu != null)
			return;

		EditMenu = new Menu(this, "EditMenu");
		EditMenu.AddMenuItem("C&opy", new KeyValues("DoCopySelected"), this);
		EditMenu.SetVisible(false);
		EditMenu.SetParent(this);
		EditMenu.AddActionSignalTarget(this);
	}

	public void SelectNoText() {
		Select[0] = 0;
		Select[1] = 0;
	}

	private void InvalidateLineBreakStream() {
		LineBreaks.Clear();
		LineBreaks.Add(MAX_BUFFER_SIZE);
		RecalculateBreaksIndex = 0;
		RecalcLineBreaks = true;
	}

	public void GotoTextEnd() {
		CursorPos = TextStream.Count;
		InvalidateVerticalScrollbarSlider = true;

		VertScrollBar.GetRange(out int min, out int max);
		VertScrollBar.SetValue(max);

		Repaint();
	}

	public void SetFont(IFont? font) {
		Font = font;
		InvalidateLayout();
		RecalcLineBreaks = true;
		Repaint();
	}

	public override void SetFgColor(in Color color) {
		Span<FormatStreamPiece> formatStream = FormatStream.AsSpan();
		if (formatStream.Length == 1 && (formatStream[0].Color == DefaultTextColor || formatStream[0].Color == GetFgColor()))
			formatStream[0].Color = color;
		base.SetFgColor(color);
	}

	public override void Paint() {
		AllTextAlphaIsZero = true;
		IFont? fontCurrent = Font;

		for (int j = 0; j < ClickableTextPanels.Count; j++)
			ClickableTextPanels[j].SetVisible(false);

		if (!HasText())
			return;

		GetSize(out int wide, out int tall);

		// var temp = TextStream.AsSpan().SliceNullTerminatedString();
		// Span<char> tempCopy = stackalloc char[temp.Length];
		// temp.CopyTo(tempCopy);
		// tempCopy.Replace('\n', 'N');
		// tempCopy.Replace('\0', 'Z');
		// Surface.DrawSetTextPos(wide, tall);
		// Surface.DrawSetTextColor(new(255, 255, 255));
		// Surface.DrawPrintText(tempCopy);

		int startIndex = GetStartDrawIndex(out int lineBreakIndexIndex);
		CurrentTextClickable = false;

		ClickableTextIndex = GetClickableTextIndexStart(startIndex);

		if (RecalcSavedRenderState)
			RecalculateDefaultState(startIndex);

		RenderState renderState = CachedRenderState;

		PixelsIndent = CachedRenderState.PixelsIndent;
		CurrentTextClickable = CachedRenderState.TextClickable;

		renderState.TextClickable = CurrentTextClickable;

		if (FormatStream.IsValidIndex(renderState.FormatStreamIndex))
			renderState.TextColor = FormatStream[renderState.FormatStreamIndex].Color;

		CalculateFade(ref renderState);

		renderState.FormatStreamIndex++;

		if (CurrentTextClickable)
			ClickableTextIndex = startIndex;

		// where to start drawing
		renderState.X = DrawOffsetX + PixelsIndent;
		renderState.Y = DrawOffsetY;

		GetSelectedRange(out int selection0, out int selection1);

		Surface.DrawSetTextFont(fontCurrent);

		for (int i = startIndex; i < TextStream.Count && renderState.Y < tall;) {
			int nXBeforeStateChange = renderState.X;
			if (UpdateRenderState(i, ref renderState) || i == startIndex) {
				if (renderState.TextClickable != CurrentTextClickable) {
					if (renderState.TextClickable) {
						ClickableTextIndex++;
						fontCurrent = FontUnderline;
						Surface.DrawSetTextFont(fontCurrent);

						ClickPanel? clickPanel = ClickableTextPanels.IsValidIndex(ClickableTextIndex) ? ClickableTextPanels[ClickableTextIndex] : null;

						if (clickPanel != null)
							clickPanel.SetPos(renderState.X, renderState.Y);
					}
					else {
						FinishingURL(nXBeforeStateChange, renderState.Y);
						fontCurrent = Font;
						Surface.DrawSetTextFont(fontCurrent);
					}
					CurrentTextClickable = renderState.TextClickable;
				}
			}

			if (LineBreaks.IsValidIndex(lineBreakIndexIndex) && LineBreaks[lineBreakIndexIndex] <= i) {
				if (CurrentTextClickable)
					FinishingURL(renderState.X, renderState.Y);

				AddAnotherLine(ref renderState.X, ref renderState.Y);
				lineBreakIndexIndex++;

				if (renderState.TextClickable) {
					ClickableTextIndex++;
					ClickPanel? clickPanel = ClickableTextPanels.IsValidIndex(ClickableTextIndex) ? ClickableTextPanels[ClickableTextIndex] : null;
					if (clickPanel != null)
						clickPanel.SetPos(renderState.X, renderState.Y);
				}
			}

			int iLast = TextStream.Count - 1;

			if (LineBreaks.IsValidIndex(lineBreakIndexIndex) && LineBreaks[lineBreakIndexIndex] <= iLast)
				iLast = LineBreaks[lineBreakIndexIndex] - 1;

			if (FormatStream.IsValidIndex(renderState.FormatStreamIndex) && FormatStream[renderState.FormatStreamIndex].TextStreamIndex <= iLast)
				iLast = FormatStream[renderState.FormatStreamIndex].TextStreamIndex - 1;

			if (i < selection0 && iLast >= selection0)
				iLast = selection0 - 1;
			if (i >= selection0 && i < selection1 && iLast >= selection1)
				iLast = selection1 - 1;

			for (int iT = i; iT <= iLast; iT++) {
				if (char.IsControl(TextStream[iT])) {
					iLast = iT - 1;
					break;
				}
			}

			if (iLast < i) {
				if (TextStream[i] == '\t') {
					int dxTabWidth = 8 * Surface.GetCharacterWidth(fontCurrent, ' ');
					dxTabWidth = Math.Max(1, dxTabWidth);

					renderState.X = (dxTabWidth * (1 + (renderState.X / dxTabWidth)));
				}
				i++;
			}
			else {
				renderState.X += DrawString(i, iLast, ref renderState, fontCurrent);
				i = iLast + 1;
			}
		}

		if (renderState.TextClickable)
			FinishingURL(renderState.X, renderState.Y);
		// DrawDebug();
	}

	private void DrawDebug() {
		GetSize(out int w, out int h);
		Surface.DrawSetTextFont(Font);

		int y = 0;
		for (int i = 0; i < FormatStream.Count; i++, y++) {
			var piece = FormatStream[i];
			Surface.DrawSetTextPos(w + 16, y * 16);
			Surface.DrawPrintText($"Format Stream #{i}: {piece}");
		}
		y += 1;
		for (int i = 0; i < LineBreaks.Count; i++, y++) {
			var piece = LineBreaks[i];
			Surface.DrawSetTextPos(w + 16, y * 16);
			Surface.DrawPrintText($"Line break #{i} = {piece}");
		}
		y += 1;
		int x = 0;
		int max = 8;
		for (int i = 0; i < TextStream.Count; i++, y = (x > max ? y + 1 : y), x = (x > max ? 0 : x + 1)) {
			Surface.DrawSetTextPos(w + 16 + (x * 48), y * 16);
			if (char.IsControl(TextStream[i]))
				if (TextStream[i] == '\n')
					Surface.DrawPrintText($"\\n");
				else
					Surface.DrawPrintText($"0x{(int)TextStream[i]}");
			else if (char.IsWhiteSpace(TextStream[i]))
				Surface.DrawPrintText("WS");
			else
				Surface.DrawPrintText($"{TextStream[i]}");
		}
	}

	private int DrawString(int first, int last, ref RenderState renderState, IFont? font) {
		int fontTall = Surface.GetFontTall(font);
		int charWide = 0;
		for (int i = first; i <= last; i++)
			charWide += Surface.GetCharacterWidth(font, TextStream[i]);

		GetSelectedRange(out int selection0, out int selection1);

		if (first >= selection0 && first < selection1) {
			Surface.DrawSetColor(SelectionColor);
			Surface.DrawFilledRect(renderState.X, renderState.Y, renderState.X + charWide, renderState.Y + 1 + fontTall);

			Surface.DrawSetTextColor(SelectionTextColor);
			AllTextAlphaIsZero = false;
		}
		else
			Surface.DrawSetTextColor(renderState.TextColor);

		if (renderState.TextColor.A != 0) {
			AllTextAlphaIsZero = false;
			Surface.DrawSetTextPos(renderState.X, renderState.Y);
			Surface.DrawPrintText(TextStream.AsSpan()[first..][..(last - first + 1)]);
		}

		return charWide;
	}

	private void FinishingURL(int x, int y) {
		if (ClickableTextPanels.IsValidIndex(ClickableTextIndex)) {
			ClickPanel clickPanel = ClickableTextPanels[ClickableTextIndex];
			clickPanel.GetPos(out int px, out int py);
			int fontTall = GetLineHeight();
			clickPanel.SetSize(Math.Max(x - px, 6), y - py + fontTall);
			clickPanel.SetVisible(true);

			if (x - px <= 0) {
				--ClickableTextIndex;
				clickPanel.SetVisible(false);
			}
		}
	}

	private bool UpdateRenderState(int textStreamPos, ref RenderState renderState) {
		if (FormatStream.IsValidIndex(renderState.FormatStreamIndex) && FormatStream[renderState.FormatStreamIndex].TextStreamIndex == textStreamPos) {
			// set the current formatting
			renderState.TextColor = FormatStream[renderState.FormatStreamIndex].Color;
			renderState.TextClickable = FormatStream[renderState.FormatStreamIndex].TextClickable;

			CalculateFade(ref renderState);

			int indentChange = FormatStream[renderState.FormatStreamIndex].PixelsIndent - renderState.PixelsIndent;
			renderState.PixelsIndent = FormatStream[renderState.FormatStreamIndex].PixelsIndent;

			if (indentChange != 0)
				renderState.X = renderState.PixelsIndent + DrawOffsetX;

			PixelsIndent = renderState.PixelsIndent;
			renderState.FormatStreamIndex++;
			return true;
		}

		return false;
	}

	private void CalculateFade(ref RenderState renderState) {
		if (FormatStream.IsValidIndex(renderState.FormatStreamIndex)) {
			if (ResetFades == false) {
				ref Fade fade = ref FormatStream.AsSpan()[renderState.FormatStreamIndex].Fade;
				if (fade.FadeLength != -1.0f) {
					float frac = (float)((fade.FadeStartTime - system.GetCurrentTime()) / fade.FadeLength);

					int alpha = (int)(frac * fade.OriginalAlpha);
					alpha = Math.Clamp(alpha, 0, fade.OriginalAlpha);

					renderState.TextColor.SetColor(renderState.TextColor.R, renderState.TextColor.G, renderState.TextColor.B, alpha);
				}
			}
		}
	}

	private void RecalculateDefaultState(int startIndex) {
		if (!HasText())
			return;

		Assert(startIndex < TextStream.Count);

		CachedRenderState.TextColor = GetFgColor();
		PixelsIndent = 0;
		CurrentTextClickable = false;
		ClickableTextIndex = GetClickableTextIndexStart(startIndex);

		GenerateRenderStateForTextStreamIndex(startIndex, ref CachedRenderState);
		RecalcSavedRenderState = false;
	}

	private void GenerateRenderStateForTextStreamIndex(int textStreamIndex, ref RenderState renderState) {
		renderState.FormatStreamIndex = FindFormatStreamIndexForTextStreamPos(textStreamIndex);

		renderState.TextColor = FormatStream[renderState.FormatStreamIndex].Color;
		renderState.PixelsIndent = FormatStream[renderState.FormatStreamIndex].PixelsIndent;
		renderState.TextClickable = FormatStream[renderState.FormatStreamIndex].TextClickable;
	}

	private int FindFormatStreamIndexForTextStreamPos(int textStreamIndex) {
		int formatStreamIndex = 0;
		for (; FormatStream.IsValidIndex(formatStreamIndex); formatStreamIndex++)
			if (FormatStream[formatStreamIndex].TextStreamIndex > textStreamIndex)
				break;

		formatStreamIndex--;
		if (!FormatStream.IsValidIndex(formatStreamIndex))
			formatStreamIndex = 0;

		return formatStreamIndex;
	}

	private int GetClickableTextIndexStart(int startIndex) {
		for (int i = 0; i < ClickableTextPanels.Count; i++)
			if (ClickableTextPanels[i].GetViewTextIndex() >= startIndex)
				return i - 1;

		return -1;
	}

	private bool HasText() => TextStream.Count != 0;

	public override void OnKillFocus(Panel? newPanel) {
#if GMOD_DLL
		// todo: OnLoseFocus hook
#endif
		bool mouseRightClicked = Input.WasMousePressed(ButtonCode.MouseRight);
		bool mouseRightUp = Input.WasMouseReleased(ButtonCode.MouseRight);
		bool mouseRightDown = Input.IsMouseDown(ButtonCode.MouseRight);

		if (mouseRightClicked || mouseRightDown || mouseRightUp) {
			if (GetSelectedRange(out int start, out int end)) {
				CursorToPixelSpace(start, out int startX, out int startY);
				CursorToPixelSpace(end, out int endX, out int endY);
				Input.GetCursorPos(out int cursorX, out int cursorY);
				ScreenToLocal(ref cursorX, ref cursorY);

				int fontTall = GetLineHeight();
				endY = endY + fontTall;
				if ((startY < cursorY) && (endY > cursorY))
					return;
			}
		}

		SelectNone();

		base.OnKillFocus(newPanel);
	}

	public int PixelToCursorSpace(int cx, int cy) {
		int FontTall = GetLineHeight();

		int YStart = DrawOffsetY;
		int x = DrawOffsetX, y = YStart;
		PixelsIndent = 0;

		int StartIndex = GetStartDrawIndex(out int LineBreakIndexIndex);
		if (RecalcSavedRenderState)
			RecalculateDefaultState(StartIndex);

		PixelsIndent = CachedRenderState.PixelsIndent;
		CurrentTextClickable = CachedRenderState.TextClickable;
		RenderState renderState = CachedRenderState;

		bool OnRightLine = false;
		int i;
		for (i = StartIndex; i < TextStream.Count; i++) {
			char ch = TextStream[i];

			renderState.X = x;
			if (UpdateRenderState(i, ref renderState))
				x = renderState.X;

			if (LineBreaks[LineBreakIndexIndex] == i) {
				AddAnotherLine(ref x, ref y);
				LineBreakIndexIndex++;

				if (OnRightLine)
					break;
			}

			if (cy < YStart)
				OnRightLine = true;
			else if (cy >= y && (cy < (y + FontTall + DrawOffsetY)))
				OnRightLine = true;

			int wide = Surface.GetCharacterWidth(Font, ch);

			if (OnRightLine) {
				if (cx > GetWide()) { } // off right side of window
				else if (cx < (DrawOffsetX + renderState.PixelsIndent) || cy < YStart) // off left side of window
					return i;

				if (cx >= x && cx < (x + wide)) {
					if (cx < (x + (wide / 2)))
						return i;
					else
						return i + 1;
				}
			}
			x += wide;
		}

		return i;
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		bool shift = Input.IsKeyDown(ButtonCode.KeyLShift) || Input.IsKeyDown(ButtonCode.KeyRShift);
		bool ctrl = Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl);
		bool alt = Input.IsKeyDown(ButtonCode.KeyLAlt) || Input.IsKeyDown(ButtonCode.KeyRAlt);
		bool winkey = Input.IsKeyDown(ButtonCode.KeyLWin) || Input.IsKeyDown(ButtonCode.KeyRWin);
		bool fallThrough = false;

		if (ctrl || (winkey && IsOSX)) {
			switch (code) {
				case ButtonCode.KeyA:
					CursorPos = 0;
					Select[0] = 0;
					Select[1] = TextStream.Count;
					return;

				case ButtonCode.KeyInsert:
				case ButtonCode.KeyC:
				case ButtonCode.KeyX:
					CopySelected();
					return;

				case ButtonCode.KeyPageUp:
				case ButtonCode.KeyHome:
					GotoTextStart();
					break;

				case ButtonCode.KeyPageDown:
				case ButtonCode.KeyEnd:
					GotoTextEnd();
					break;

				default:
					fallThrough = true;
					break;
			}
		}
		else if (alt)
			fallThrough = true;
		else {
			switch (code) {
				case ButtonCode.KeyTab:
				case ButtonCode.KeyLShift:
				case ButtonCode.KeyRShift:
				case ButtonCode.KeyEscape:
				case ButtonCode.KeyEnter:
					fallThrough = true;
					break;

				case ButtonCode.KeyDelete:
					if (shift)
						CopySelected();
					break;

				case ButtonCode.KeyHome:
					GotoTextStart();
					break;

				case ButtonCode.KeyEnd:
					GotoTextEnd();
					break;

				case ButtonCode.KeyPageUp:
					if (VertScrollBar.IsVisible()) {
						int window = VertScrollBar.GetRangeWindow();
						int newval = VertScrollBar.GetValue();
						VertScrollBar.SetValue(newval - window - 1);
					}
					break;

				case ButtonCode.KeyPageDown:
					if (VertScrollBar.IsVisible()) {
						int window = VertScrollBar.GetRangeWindow();
						int newval = VertScrollBar.GetValue();
						VertScrollBar.SetValue(newval + window + 1);
					}
					break;

				default:
					return;
			}
		}

		if (code != ButtonCode.KeyLControl && code != ButtonCode.KeyRControl)
			Select[1] = CursorPos;

		if (fallThrough)
			base.OnKeyCodeTyped(code);
	}

	public bool GetSelectedRange(out int start, out int end) {
		if (Select[0] == -1) {
			start = end = -1;
			return false;
		}

		start = Select[0];
		end = Select[1];
		if (end < start)
			(end, start) = (start, end);

		return true;
	}

	private void OpenEditMenu() {
		if (EditMenu == null)
			return;

		Input.GetCursorPos(out int cursorX, out int cursorY);
		if (GetSelectedRange(out _, out _)) {
			EditMenu.SetItemEnabled("&Cut", true);
			EditMenu.SetItemEnabled("C&opy", true);
		}
		else {
			EditMenu.SetItemEnabled("&Cut", false);
			EditMenu.SetItemEnabled("C&opy", false);
		}
		EditMenu.SetVisible(true);
		EditMenu.RequestFocus();
		EditMenu.InvalidateLayout();
		EditMenu.GetSize(out int menuWide, out int menuTall);

		Surface.GetScreenSize(out int wide, out int tall);

		if (wide - menuWide > cursorX) {
			if (tall - menuTall > cursorY)
				EditMenu.SetPos(cursorX, cursorY);
			else
				EditMenu.SetPos(cursorX, cursorY - menuTall);
		}
		else {
			if (tall - menuTall > cursorY)
				EditMenu.SetPos(cursorX - menuWide, cursorY);
			else
				EditMenu.SetPos(cursorX - menuWide, cursorY - menuTall);
		}

		EditMenu.RequestFocus();
	}

	private void CursorToPixelSpace(int cursorPos, out int cx, out int cy) {
		int yStart = DrawOffsetY;
		int x = DrawOffsetX, y = yStart;
		PixelsIndent = 0;

		for (int i = GetStartDrawIndex(out int lineBreakIndexIndex); i < TextStream.Count; i++) {
			char ch = TextStream[i];

			if (cursorPos == i) {
				if (LineBreaks[lineBreakIndexIndex] == i) {
					AddAnotherLine(ref x, ref y);
					lineBreakIndexIndex++;
				}
				break;
			}

			if (LineBreaks[lineBreakIndexIndex] == i) {
				AddAnotherLine(ref x, ref y);
				lineBreakIndexIndex++;
			}

			x += Surface.GetCharacterWidth(Font, ch);
		}

		cx = x;
		cy = y;
	}

	private void AddAnotherLine(ref int x, ref int y) {
		x = DrawOffsetX + PixelsIndent;
		y += GetLineHeight() + DrawOffsetY;
	}

	private int GetStartDrawIndex(out int lineBreakIndexIndex) {
		int startIndex = 0;
		int startLine = VertScrollBar.GetValue();

		if (startLine >= LineBreaks.Count)
			startLine = LineBreaks.Count - 1;

		lineBreakIndexIndex = startLine;
		if (startLine != 0 && startLine < LineBreaks.Count)
			startIndex = LineBreaks[startLine - 1];

		return startIndex;
	}

#if GMOD_DLL
	public short LineHeight = -1;
	public bool CustomSelectionColors;

	public int GetLineHeight() => LineHeight < 0 ? Surface.GetFontTall(Font) : LineHeight;
#else
	public int GetLineHeight() => Surface.GetFontTall(Font);
#endif
	public int GetNumLines() => LineBreaks.Count;
	public int GetMaximumCharCount() => MaxCharCount;
	public IFont? GetFont() => Font;

	public void SelectAllText() {
		CursorPos = 0;
		Select[0] = 0;
		Select[1] = TextStream.Count;
	}

	public void InsertPossibleURLString(ReadOnlySpan<char> text, Color urlTextColor, Color normalTextColor) {
		InsertColorChange(normalTextColor);

		string str = text.ToString();
		int pos = 0;
		while (pos < str.Length) {
			pos = ParseTextStringForUrls(str, pos, out string urlText, out string url, out bool clickable);

			if (clickable) {
				InsertClickableTextStart(url);
				InsertColorChange(urlTextColor);
			}

			InsertString(urlText);

			if (clickable) {
				InsertColorChange(normalTextColor);
				InsertClickableTextEnd();
			}
		}
	}

	static int ParseTextStringForUrls(string text, int startPos, out string urlText, out string url, out bool clickable) {
		int i = startPos;
		while (i < text.Length) {
			bool urlFound = false;
			ReadOnlySpan<char> rest = text.AsSpan(i);

			if (rest.StartsWith("<a href=", StringComparison.OrdinalIgnoreCase)) {
				if (i > startPos)
					break;

				clickable = true;
				i += "<a href=".Length;
				int urlEnd = text.IndexOf('>', i);
				if (urlEnd < 0)
					urlEnd = text.Length;
				url = text[i..urlEnd];
				i = Math.Min(urlEnd + 1, text.Length);

				int textEnd = text.IndexOf("</a>", StringComparison.Ordinal);
				if (textEnd < i)
					textEnd = text.Length;
				urlText = text[i..textEnd];
				i = Math.Min(textEnd + "</a>".Length, text.Length);
				return i;
			}
			else if (rest.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) {
				if (text.IndexOf('.', Math.Min(i + 5, text.Length)) >= 0)
					urlFound = true;
			}
			else if (rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)
				|| rest.StartsWith("steam://", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("steambeta://", StringComparison.OrdinalIgnoreCase)
				|| rest.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
				urlFound = true;

			if (urlFound) {
				if (i == startPos) {
					clickable = true;
					int end = i;
					while (end < text.Length && !char.IsWhiteSpace(text[end]))
						end++;
					urlText = text[i..end];
					url = urlText;
					return end;
				}
				break;
			}

			i++;
		}

		clickable = false;
		urlText = text[startPos..i];
		url = urlText;
		return i;
	}
	public void SetUnderlineFont(IFont? font) => FontUnderline = font;

	public void SetToFullHeight() {
#if GMOD_DLL
		PerformLayout();
		GetSize(out int wide, out int tall);
		tall = GetNumLines() * (GetLineHeight() + DrawOffsetY) + DrawOffsetY + 2;
		SetSize(wide, tall);
		PerformLayout();
#else
		RecalculateLineBreaks();
		PerformLayout();
		GetSize(out int wide, out int tall);
		tall = GetNumLines() * (GetLineHeight() + DrawOffsetY) + DrawOffsetY + QuickPropScale(2);
		SetSize(wide, tall);
		PerformLayout();
#endif
	}

	public void InsertClickableTextStart(ReadOnlySpan<char> clickAction) {
		ref FormatStreamPiece prevItem = ref FormatStream.AsSpan()[FormatStream.Count - 1];
		if (prevItem.TextStreamIndex == TextStream.Count) {
			prevItem.TextClickable = true;
			prevItem.ClickableTextAction = clickAction;
		}
		else {
			FormatStreamPiece formatStreamCopy = prevItem;
			formatStreamCopy.TextStreamIndex = TextStream.Count;
			formatStreamCopy.TextClickable = true;
			formatStreamCopy.ClickableTextAction = clickAction;
			FormatStream.Add(formatStreamCopy);
		}

		InvalidateLineBreakStream();
		InvalidateLayout();
	}

	public void InsertClickableTextEnd() {
		ref FormatStreamPiece prevItem = ref FormatStream.AsSpan()[FormatStream.Count - 1];
		if (!prevItem.TextClickable) {
		}
		else if (prevItem.TextStreamIndex == TextStream.Count) {
			prevItem.TextClickable = false;
		}
		else {
			FormatStreamPiece streamItem = prevItem;
			streamItem.TextClickable = false;
			streamItem.TextStreamIndex = TextStream.Count;
			FormatStream.Add(streamItem);
		}
	}

	public virtual void CutSelected() {
		CopySelected();
		RequestFocus();
	}

	[UiMessage("DoCopySelected")]
	public virtual void CopySelected() {
		if (GetSelectedRange(out int x0, out int x1)) {
			List<char> buf = [];
			for (int i = x0; i < x1; i++) {
				if (!TextStream.IsValidIndex(i))
					continue;

				if (TextStream[i] == '\n') {
					if (buf.Count == 0)
						continue;

					buf.Add('\r');
				}
				buf.Add(TextStream[i]);
			}
			buf.Add('\0');
			system.SetClipboardText(buf.AsSpan());
		}

		RequestFocus();
	}

	public void SelectNone() {
		Select[0] = -1;
		Repaint();
	}

	public override void ApplySchemeSettings(IScheme scheme) {
#if GMOD_DLL
		// todo: ApplySchemeSettings hook
#endif
		base.ApplySchemeSettings(scheme);

		Font = scheme.GetFont("Default", IsProportional());
		FontUnderline = scheme.GetFont("DefaultUnderline", IsProportional());

		SetFgColor(GetSchemeColor("RichText.TextColor", scheme));
		SetBgColor(GetSchemeColor("RichText.BgColor", scheme));

#if GMOD_DLL
		if (!CustomSelectionColors) {
#endif
			SelectionTextColor = GetSchemeColor("RichText.SelectedTextColor", GetFgColor(), scheme);
			SelectionColor = GetSchemeColor("RichText.SelectedBgColor", scheme);
#if GMOD_DLL
		}
#endif

		ReadOnlySpan<char> insetX = scheme.GetResourceString("RichText.InsetX");
		if (!insetX.IsEmpty && insetX.Length != 0)
			SetDrawOffsets(int.TryParse(scheme.GetResourceString("RichText.InsetX"), out int i1) ? i1 : 0, int.TryParse(scheme.GetResourceString("RichText.InsetY"), out int i2) ? i2 : 0);
	}

	public void SetMaximumCharCount(int maxChars) => MaxCharCount = maxChars;

	public void GetText(Span<char> buf) => GetText(0, buf);
	public void GetText(int offset, Span<char> buf) {
		if (buf.IsEmpty || buf.Length <= 0)
			return;

		int i;
		for (i = offset; i < (offset + buf.Length - 1); i++) {
			if (i >= TextStream.Count)
				break;

			buf[i - offset] = TextStream[i];
		}

		buf[i - offset] = '\0';
		buf[^1] = '\0';
	}

#if GMOD_DLL
	public override void SetText(ReadOnlySpan<char> text) {
#else
	public void SetText(ReadOnlySpan<char> text) {
#endif
		if (text.IsEmpty)
			text = "";

		Span<char> unicode = stackalloc char[1024];

		if (text.Length > 0 && text[0] == '#') {
			ResolveLocalizedTextAndVariables(text, unicode);
			SetText(unicode);
			return;
		}

		FormatStream.Clear();
		FormatStreamPiece stream = new();
		stream.Color = GetFgColor();
		stream.Fade.FadeLength = -1.0f;
		stream.Fade.FadeStartTime = 0.0f;
		stream.PixelsIndent = 0;
		stream.TextStreamIndex = 0;
		stream.TextClickable = false;
		FormatStream.Add(stream);

		TextStream.Clear();
		if (!text.IsEmpty && text.Length > 0) {
			ReadOnlySpan<char> t = text.SliceNullTerminatedString();
			TextStream.EnsureCapacity(t.Length + 1);
			for (int i = 0; i < t.Length; i++)
				TextStream.Add(text[i]);
			TextStream.Add('\0');
		}

		GotoTextStart();
		SelectNone();

		InvalidateLineBreakStream();
		InvalidateLayout();
	}

	public void GotoTextStart() {
		CursorPos = 0;
		InvalidateVerticalScrollbarSlider = true;
		VertScrollBar.SetValue(0);
		Repaint();
	}

	public void InsertColorChange(in Color clr) {
		ref FormatStreamPiece prevItem = ref FormatStream.AsSpan()[FormatStream.Count - 1];
		if (prevItem.Color == clr)
			return;

		if (prevItem.TextStreamIndex == TextStream.Count) {
			prevItem.Color = clr;
		}
		else {
			FormatStreamPiece streamItem = prevItem;
			streamItem.Color = clr;
			streamItem.TextStreamIndex = TextStream.Count;
			FormatStream.Add(streamItem);
		}
	}

	public override void OnThink() {
		if (RecalcLineBreaks) {
			RecalcSavedRenderState = true;
			RecalculateLineBreaks();

			if (InvalidateVerticalScrollbarSlider)
				LayoutVerticalScrollBarSlider();
		}
	}

	public override void OnSizeChanged(int wide, int tall) {
		base.OnSizeChanged(wide, tall);
		InvalidateVerticalScrollbarSlider = true;
		InvalidateLineBreakStream();
		InvalidateLayout();

		if (VertScrollBar?.IsVisible() ?? false) {
			VertScrollBar?.MakeReadyForUse();
			Interior?.SetBounds(0, 0, wide - (VertScrollBar?.GetWide() ?? 0), tall);
		}
		else
			Interior?.SetBounds(0, 0, wide, tall);
	}
	public void ResetAllFades(bool hold, bool onlyExpired = false, double newSustain = -1) {
		ResetFades = hold;

		if (ResetFades == false) {
			Span<FormatStreamPiece> formatStream = FormatStream.AsSpan();
			for (int i = 1; i < formatStream.Length; i++) {
				ref FormatStreamPiece streamPiece = ref formatStream[i];
				if (onlyExpired == true)
					if (streamPiece.Fade.FadeStartTime >= system.GetCurrentTime())
						continue;

				if (newSustain == -1.0f)
					newSustain = streamPiece.Fade.FadeSustain;

				streamPiece.Fade.FadeStartTime = system.GetCurrentTime() + newSustain;
			}
		}
	}
	public void InsertFade(float sustain, float length) {
		Span<FormatStreamPiece> formatStream = FormatStream.AsSpan();
		ref FormatStreamPiece prevItem = ref formatStream[^1];
		if (prevItem.TextStreamIndex == TextStream.Count) {
			prevItem.Fade.FadeStartTime = system.GetCurrentTime() + sustain;
			prevItem.Fade.FadeSustain = sustain;
			prevItem.Fade.FadeLength = length;
			prevItem.Fade.OriginalAlpha = prevItem.Color.A;
		}
		else {
			FormatStreamPiece streamItem = prevItem;

			prevItem.Fade.FadeStartTime = system.GetCurrentTime() + sustain;
			prevItem.Fade.FadeLength = length;
			prevItem.Fade.FadeSustain = sustain;
			prevItem.Fade.OriginalAlpha = prevItem.Color.A;

			streamItem.TextStreamIndex = TextStream.Count;
			FormatStream.Add(streamItem);
		}
	}

	private void RecalculateLineBreaks() {
		if (!RecalcLineBreaks)
			return;

		int wide = GetWide();
		if (wide == 0)
			return;

		wide -= DrawOffsetX;

		RecalcLineBreaks = false;
		RecalcSavedRenderState = true;
		if (!HasText())
			return;

		int selection0 = -1, selection1 = -1;

		if (VertScrollBar.IsVisible())
			wide -= VertScrollBar.GetWide();

		int x = DrawOffsetX, y = DrawOffsetY;

		int wordStartIndex = 0;
		int lineStartIndex = 0;
		bool hasWord = false;
		bool justStartedNewLine = true;
		bool wordStartedOnNewLine = true;

		int startChar = 0;
		if (RecalculateBreaksIndex <= 0) {
			LineBreaks.Clear();
		}
		else {
			for (int i = RecalculateBreaksIndex + 1; i < LineBreaks.Count; ++i) {
				LineBreaks.RemoveAt(i);
				--i;
			}
			startChar = LineBreaks[RecalculateBreaksIndex];
			lineStartIndex = LineBreaks[RecalculateBreaksIndex];
			wordStartIndex = lineStartIndex;
		}

		if (TextStream[startChar] == '\r' || TextStream[startChar] == '\n') {
			startChar++;
			lineStartIndex = startChar;
		}

		int clickableTextNum = GetClickableTextIndexStart(startChar);
		clickableTextNum++;

		RenderState renderState = new();
		GenerateRenderStateForTextStreamIndex(startChar, ref renderState);
		CurrentTextClickable = false;

		IFont? font = Font;

		bool forceBreak = false;
		float lineWidthSoFar = 0;

		for (int i = startChar; i < TextStream.Count; ++i) {
			char ch = TextStream[i];
			renderState.X = x;
			if (UpdateRenderState(i, ref renderState)) {
				x = renderState.X;
				int preI = i;

				if (renderState.TextClickable != CurrentTextClickable) {
					if (renderState.TextClickable) {
						if (clickableTextNum >= ClickableTextPanels.Count)
							ClickableTextPanels.Add(new ClickPanel(this));

						ClickPanel? clickPanel = ClickableTextPanels[clickableTextNum++];
						clickPanel.SetTextIndex(preI, preI);
					}

					CurrentTextClickable = renderState.TextClickable;
				}
			}

			bool isWSpace = char.IsWhiteSpace(ch) ? true : false;

			bool previousWordStartedOnNewLine = wordStartedOnNewLine;
			int previousWordStartIndex = wordStartIndex;
			if (!isWSpace && ch != '\t' && ch != '\n' && ch != '\r') {
				if (!hasWord) {
					wordStartIndex = i;
					hasWord = true;
					wordStartedOnNewLine = justStartedNewLine;
				}
			}
			else {
				hasWord = false;
			}

			char wchBefore = '\0';
			char wchAfter = '\0';

			if (i > 0 && i > lineStartIndex && i != selection0 && i - 1 != selection1)
				wchBefore = TextStream[i - 1];
			if (i < TextStream.Count - 1 && i + 1 != selection0 && i != selection1)
				wchAfter = TextStream[i + 1];

			Surface.GetKernedCharWidth(font, ch, wchBefore, wchAfter, out float w, out float flabcA);
			lineWidthSoFar += w;

			if (Math.Floor(lineWidthSoFar + 0.6) + x > wide) {
				forceBreak = true;
			}

			if (!char.IsControl(ch)) {
				justStartedNewLine = false;
			}

			if (forceBreak || ch == '\r' || ch == '\n') {
				forceBreak = false;
				AddAnotherLine(ref x, ref y);

				if (ch == '\r' || ch == '\n') {
					lineStartIndex = i + 1;
					LineBreaks.Add(i + 1);
				}
				else if (previousWordStartedOnNewLine || previousWordStartIndex <= lineStartIndex) {
					lineStartIndex = i;
					LineBreaks.Add(i);

					if (renderState.TextClickable) {
						int oldIndex = ClickableTextPanels[clickableTextNum - 1].GetTextIndex();

						if (clickableTextNum >= ClickableTextPanels.Count)
							ClickableTextPanels.Add(new ClickPanel(this));

						ClickPanel clickPanel = ClickableTextPanels[clickableTextNum++];
						clickPanel.SetTextIndex(oldIndex, i);
					}
				}
				else {
					LineBreaks.Add(previousWordStartIndex);
					lineStartIndex = previousWordStartIndex;
					i = previousWordStartIndex;

					RenderState renderStateAtLastWord = new();
					GenerateRenderStateForTextStreamIndex(i, ref renderStateAtLastWord);

					if (renderStateAtLastWord.TextClickable && FormatStream[renderStateAtLastWord.FormatStreamIndex].TextStreamIndex < i) {
						int oldIndex = ClickableTextPanels[clickableTextNum - 1].GetTextIndex();

						if (clickableTextNum >= ClickableTextPanels.Count)
							ClickableTextPanels.Add(new ClickPanel(this));

						ClickPanel clickPanel = ClickableTextPanels[clickableTextNum++];
						clickPanel.SetTextIndex(oldIndex, i);
					}
				}

				lineWidthSoFar = 0;
				justStartedNewLine = true;
				hasWord = false;
				wordStartedOnNewLine = false;
				CurrentTextClickable = false;
				continue;
			}
		}

		LineBreaks.Add(MAX_BUFFER_SIZE);

		InvalidateVerticalScrollbarSlider = true;
	}

	private void LayoutVerticalScrollBarSlider() {
		InvalidateVerticalScrollbarSlider = false;

		int previousValue = VertScrollBar.GetValue();
		bool bCurrentlyAtEnd = false;
		VertScrollBar.GetRange(out int rmin, out int rmax);
		// DEVIATION: >= rmax fixes an issue in textchat.
		if (rmax != 0 && (previousValue + rmin + VertScrollBar.GetRangeWindow() >= rmax))
			bCurrentlyAtEnd = true;

		GetSize(out int wide, out int tall);

		VertScrollBar.SetPos(wide - VertScrollBar.GetWide(), 0);
		VertScrollBar.SetSize(VertScrollBar.GetWide(), tall);

		int displayLines = tall / (GetLineHeight() + DrawOffsetY);
		int numLines = LineBreaks.Count;

		if (numLines <= displayLines) {
			VertScrollBar.SetEnabled(false);
			VertScrollBar.SetRange(0, numLines);
			VertScrollBar.SetRangeWindow(numLines);
			VertScrollBar.SetValue(0);

			if (UnusedScrollbarInvis)
				SetVerticalScrollbar(false);
		}
		else {
			if (UnusedScrollbarInvis)
				SetVerticalScrollbar(true);

			VertScrollBar.SetRange(0, numLines);
			VertScrollBar.SetRangeWindow(displayLines);
			VertScrollBar.SetEnabled(true);

			VertScrollBar.SetButtonPressedScrollValue(1);
			if (bCurrentlyAtEnd)
				VertScrollBar.SetValue(numLines - displayLines);

			VertScrollBar.InvalidateLayout();
			VertScrollBar.Repaint();
		}
	}

	public void InsertString(ReadOnlySpan<char> text, bool doLocalize = true) {
		if (doLocalize && text.Length > 0 && text[0] == '#') {
			Span<char> unicode = stackalloc char[1024];
			ReadOnlySpan<char> unicodeInput = ResolveLocalizedTextAndVariables(text, unicode);
			InsertString(unicodeInput, false); // If localizing, and we fail to resolve localized text, this will stack overflow
			return;
		}

		for (int i = 0; i < text.Length && text[i] != '\0'; i++)
			InsertChar(text[i]);

		InvalidateLayout();
		RecalcLineBreaks = true;
		Repaint();
	}

	private void InsertChar(char ch) {
		if (ch == '\r')
			return;

		if (MaxCharCount > 0 && TextStream.Count > MaxCharCount)
			TruncateTextStream();

		TextStream.Add(ch);

		RecalculateBreaksIndex = LineBreaks.Count - 2;
		Repaint();
	}

	private void TruncateTextStream() {
		if (MaxCharCount < 1)
			return;

		int cullPos = MaxCharCount / 2;

		TextStream.RemoveRange(0, cullPos);

		int formatIndex = FindFormatStreamIndexForTextStreamPos(cullPos);
		if (formatIndex > 0) {
			Span<FormatStreamPiece> piecesTemp = FormatStream.AsSpan();
			piecesTemp[0] = FormatStream[formatIndex];
			piecesTemp[0].TextStreamIndex = 0;
			FormatStream.RemoveRange(1, formatIndex);
		}


		Span<FormatStreamPiece> pieces = FormatStream.AsSpan();

		for (int i = 1; i < pieces.Length; i++) {
			Assert(pieces[i].TextStreamIndex > cullPos);
			pieces[i].TextStreamIndex -= cullPos;
		}

		InvalidateLineBreakStream();
		InvalidateLayout();
		InvalidateVerticalScrollbarSlider = true;
	}

	private ReadOnlySpan<char> ResolveLocalizedTextAndVariables(ReadOnlySpan<char> lookup, Span<char> outbuf) {
		if (lookup[0] == '#') {
			ulong index = Localize.FindIndex(lookup[1..]);

			if (index != UtlSymbol.UTL_INVAL_SYMBOL) {
				ReadOnlySpan<char> localized = Localize.GetValueByIndex(index);
				if (localized.Length > 0) {
					localized.CopyTo(outbuf);
					return outbuf[..localized.Length];
				}
			}
		}

		lookup.CopyTo(outbuf);

		return outbuf[..lookup.Length];
	}
}
