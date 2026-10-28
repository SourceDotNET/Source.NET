using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using HInputContext = int;
namespace Source.GUI;

public unsafe struct InputContext
{
	public IPanel? RootPanel;

	public fixed bool MousePressed[(int)ButtonCode.MouseCount];
	public fixed bool MouseDoublePressed[(int)ButtonCode.MouseCount];
	public fixed bool MouseDown[(int)ButtonCode.MouseCount];
	public fixed bool MouseReleased[(int)ButtonCode.MouseCount];
	public fixed bool KeyPressed[(int)ButtonCode.Count];
	public fixed bool KeyTyped[(int)ButtonCode.Count];
	public fixed bool KeyDown[(int)ButtonCode.Count];
	public fixed bool KeyReleased[(int)ButtonCode.Count];

	public IPanel? KeyFocus;
	public IPanel? OldMouseFocus;
	public IPanel? MouseFocus;
	public IPanel? MouseOver;

	public IPanel? MouseCapture;
	public ButtonCode MouseCaptureStartCode;
	public IPanel? AppModalPanel;

	public int CursorX;
	public int CursorY;

	public int LastPostedCursorX;
	public int LastPostedCursorY;

	public int ExternallySetCursorX;
	public int ExternallySetCursorY;
	public bool SetCursorExplicitly;

	public List<IPanel>? KeyCodeUnhandledListeners;

	public IPanel? ModalSubTree;
	public IPanel? UnhandledMouseClickListener;
	public bool RestrictMessagesToModalSubTree;

	public KeyRepeatHandler? KeyRepeater;
	// TODO: IME stuff
}

public class VGuiInput : IVGuiInput
{
	readonly string[] KeyTrans = new string[(int)ButtonCode.KeyLast];
	InputContext DefaultInputContext;
	HInputContext Context = IVGuiInput.DEFAULT_INPUT_CONTEXT;
	readonly Dictionary<HInputContext, InputContext> Contexts = [];
	int DebugMessages;
	readonly ICommandLine CommandLine;
	readonly VGui vgui;
	readonly ISurface surface;
	readonly IInputSystem inputSystem;
	public VGuiInput(ICommandLine commandLine, VGui vgui, ISurface surface, IInputSystem inputSystem) {
		CommandLine = commandLine;
		this.inputSystem = inputSystem;
		this.vgui = vgui;
		this.surface = surface;
		KeyTrans[(int)ButtonCode.Key0] = "0)KEY_0";
		KeyTrans[(int)ButtonCode.Key1] = "1!KEY_1";
		KeyTrans[(int)ButtonCode.Key2] = "2@KEY_2";
		KeyTrans[(int)ButtonCode.Key3] = "3#KEY_3";
		KeyTrans[(int)ButtonCode.Key4] = "4$KEY_4";
		KeyTrans[(int)ButtonCode.Key5] = "5%KEY_5";
		KeyTrans[(int)ButtonCode.Key6] = "6^KEY_6";
		KeyTrans[(int)ButtonCode.Key7] = "7&KEY_7";
		KeyTrans[(int)ButtonCode.Key8] = "8*KEY_8";
		KeyTrans[(int)ButtonCode.Key9] = "9(KEY_9";
		KeyTrans[(int)ButtonCode.KeyA] = "aAKEY_A";
		KeyTrans[(int)ButtonCode.KeyB] = "bBKEY_B";
		KeyTrans[(int)ButtonCode.KeyC] = "cCKEY_C";
		KeyTrans[(int)ButtonCode.KeyD] = "dDKEY_D";
		KeyTrans[(int)ButtonCode.KeyE] = "eEKEY_E";
		KeyTrans[(int)ButtonCode.KeyF] = "fFKEY_F";
		KeyTrans[(int)ButtonCode.KeyG] = "gGKEY_G";
		KeyTrans[(int)ButtonCode.KeyH] = "hHKEY_H";
		KeyTrans[(int)ButtonCode.KeyI] = "iIKEY_I";
		KeyTrans[(int)ButtonCode.KeyJ] = "jJKEY_J";
		KeyTrans[(int)ButtonCode.KeyK] = "kKKEY_K";
		KeyTrans[(int)ButtonCode.KeyL] = "lLKEY_L";
		KeyTrans[(int)ButtonCode.KeyM] = "mMKEY_M";
		KeyTrans[(int)ButtonCode.KeyN] = "nNKEY_N";
		KeyTrans[(int)ButtonCode.KeyO] = "oOKEY_O";
		KeyTrans[(int)ButtonCode.KeyP] = "pPKEY_P";
		KeyTrans[(int)ButtonCode.KeyQ] = "qQKEY_Q";
		KeyTrans[(int)ButtonCode.KeyR] = "rRKEY_R";
		KeyTrans[(int)ButtonCode.KeyS] = "sSKEY_S";
		KeyTrans[(int)ButtonCode.KeyT] = "tTKEY_T";
		KeyTrans[(int)ButtonCode.KeyU] = "uUKEY_U";
		KeyTrans[(int)ButtonCode.KeyV] = "vVKEY_V";
		KeyTrans[(int)ButtonCode.KeyW] = "wWKEY_W";
		KeyTrans[(int)ButtonCode.KeyX] = "xXKEY_X";
		KeyTrans[(int)ButtonCode.KeyY] = "yYKEY_Y";
		KeyTrans[(int)ButtonCode.KeyZ] = "zZKEY_Z";
		KeyTrans[(int)ButtonCode.KeyPad0] = "0\0KEY_PAD_0";
		KeyTrans[(int)ButtonCode.KeyPad1] = "1\0KEY_PAD_1";
		KeyTrans[(int)ButtonCode.KeyPad2] = "2\0KEY_PAD_2";
		KeyTrans[(int)ButtonCode.KeyPad3] = "3\0KEY_PAD_3";
		KeyTrans[(int)ButtonCode.KeyPad4] = "4\0KEY_PAD_4";
		KeyTrans[(int)ButtonCode.KeyPad5] = "5\0KEY_PAD_5";
		KeyTrans[(int)ButtonCode.KeyPad6] = "6\0KEY_PAD_6";
		KeyTrans[(int)ButtonCode.KeyPad7] = "7\0KEY_PAD_7";
		KeyTrans[(int)ButtonCode.KeyPad8] = "8\0KEY_PAD_8";
		KeyTrans[(int)ButtonCode.KeyPad9] = "9\0KEY_PAD_9";
		KeyTrans[(int)ButtonCode.KeyPadDivide] = "//KEY_PAD_DIVIDE";
		KeyTrans[(int)ButtonCode.KeyPadMultiply] = "**KEY_PAD_MULTIPLY";
		KeyTrans[(int)ButtonCode.KeyPadMinus] = "--KEY_PAD_MINUS";
		KeyTrans[(int)ButtonCode.KeyPadPlus] = "++KEY_PAD_PLUS";
		KeyTrans[(int)ButtonCode.KeyPadEnter] = "\0\0KEY_PAD_ENTER";
		KeyTrans[(int)ButtonCode.KeyPadDecimal] = ".\0KEY_PAD_DECIMAL";
		KeyTrans[(int)ButtonCode.KeyLBracket] = "[{KEY_LBRACKET";
		KeyTrans[(int)ButtonCode.KeyRBracket] = "]}KEY_RBRACKET";
		KeyTrans[(int)ButtonCode.KeySemicolon] = ";:KEY_SEMICOLON";
		KeyTrans[(int)ButtonCode.KeyApostrophe] = "'\"KEY_APOSTROPHE";
		KeyTrans[(int)ButtonCode.KeyBackquote] = "`~KEY_BACKQUOTE";
		KeyTrans[(int)ButtonCode.KeyComma] = ",<KEY_COMMA";
		KeyTrans[(int)ButtonCode.KeyPeriod] = ".>KEY_PERIOD";
		KeyTrans[(int)ButtonCode.KeySlash] = "/?KEY_SLASH";
		KeyTrans[(int)ButtonCode.KeyBackslash] = "\\|KEY_BACKSLASH";
		KeyTrans[(int)ButtonCode.KeyMinus] = "-_KEY_MINUS";
		KeyTrans[(int)ButtonCode.KeyEqual] = "=+KEY_EQUAL";
		KeyTrans[(int)ButtonCode.KeyEnter] = "\0\0KEY_ENTER";
		KeyTrans[(int)ButtonCode.KeySpace] = "  KEY_SPACE";
		KeyTrans[(int)ButtonCode.KeyBackspace] = "\0\0KEY_BACKSPACE";
		KeyTrans[(int)ButtonCode.KeyTab] = "\0\0KEY_TAB";
		KeyTrans[(int)ButtonCode.KeyCapsLock] = "\0\0KEY_CAPSLOCK";
		KeyTrans[(int)ButtonCode.KeyNumLock] = "\0\0KEY_NUMLOCK";
		KeyTrans[(int)ButtonCode.KeyEscape] = "\0\0KEY_ESCAPE";
		KeyTrans[(int)ButtonCode.KeyScrollLock] = "\0\0KEY_SCROLLLOCK";
		KeyTrans[(int)ButtonCode.KeyInsert] = "\0\0KEY_INSERT";
		KeyTrans[(int)ButtonCode.KeyDelete] = "\0\0KEY_DELETE";
		KeyTrans[(int)ButtonCode.KeyHome] = "\0\0KEY_HOME";
		KeyTrans[(int)ButtonCode.KeyEnd] = "\0\0KEY_END";
		KeyTrans[(int)ButtonCode.KeyPageUp] = "\0\0KEY_PAGEUP";
		KeyTrans[(int)ButtonCode.KeyPageDown] = "\0\0KEY_PAGEDOWN";
		KeyTrans[(int)ButtonCode.KeyBreak] = "\0\0KEY_BREAK";
		KeyTrans[(int)ButtonCode.KeyLShift] = "\0\0KEY_LSHIFT";
		KeyTrans[(int)ButtonCode.KeyRShift] = "\0\0KEY_RSHIFT";
		KeyTrans[(int)ButtonCode.KeyLAlt] = "\0\0KEY_LALT";
		KeyTrans[(int)ButtonCode.KeyRAlt] = "\0\0KEY_RALT";
		KeyTrans[(int)ButtonCode.KeyLControl] = "\0\0KEY_LCONTROL";
		KeyTrans[(int)ButtonCode.KeyRControl] = "\0\0KEY_RCONTROL";
		KeyTrans[(int)ButtonCode.KeyLWin] = "\0\0KEY_LWIN";
		KeyTrans[(int)ButtonCode.KeyRWin] = "\0\0KEY_RWIN";
		KeyTrans[(int)ButtonCode.KeyApp] = "\0\0KEY_APP";
		KeyTrans[(int)ButtonCode.KeyUp] = "\0\0KEY_UP";
		KeyTrans[(int)ButtonCode.KeyLeft] = "\0\0KEY_LEFT";
		KeyTrans[(int)ButtonCode.KeyDown] = "\0\0KEY_DOWN";
		KeyTrans[(int)ButtonCode.KeyRight] = "\0\0KEY_RIGHT";
		KeyTrans[(int)ButtonCode.KeyF1] = "\0\0KEY_F1";
		KeyTrans[(int)ButtonCode.KeyF2] = "\0\0KEY_F2";
		KeyTrans[(int)ButtonCode.KeyF3] = "\0\0KEY_F3";
		KeyTrans[(int)ButtonCode.KeyF4] = "\0\0KEY_F4";
		KeyTrans[(int)ButtonCode.KeyF5] = "\0\0KEY_F5";
		KeyTrans[(int)ButtonCode.KeyF6] = "\0\0KEY_F6";
		KeyTrans[(int)ButtonCode.KeyF7] = "\0\0KEY_F7";
		KeyTrans[(int)ButtonCode.KeyF8] = "\0\0KEY_F8";
		KeyTrans[(int)ButtonCode.KeyF9] = "\0\0KEY_F9";
		KeyTrans[(int)ButtonCode.KeyF10] = "\0\0KEY_F10";
		KeyTrans[(int)ButtonCode.KeyF11] = "\0\0KEY_F11";
		KeyTrans[(int)ButtonCode.KeyF12] = "\0\0KEY_F12";
	}

	public ref InputContext GetInputContext(HInputContext context) {
		if (context == IVGuiInput.DEFAULT_INPUT_CONTEXT)
			return ref DefaultInputContext;

		return ref CollectionsMarshal.GetValueRefOrNullRef(Contexts, context);
	}

	public void ActivateInputContext(int context) {
		throw new NotImplementedException();
	}

	public void AssociatePanelWithInputContext(int context, IPanel? root) {
		throw new NotImplementedException();
	}

	public bool CandidateListStartsAtOne() {
		throw new NotImplementedException();
	}

	public int CreateInputContext() {
		throw new NotImplementedException();
	}

	public void DestroyInputContext(int context) {
		throw new NotImplementedException();
	}

	public IPanel? GetAppModalSurface() {
		ref InputContext context = ref GetInputContext(Context);
		return context.AppModalPanel;
	}

	public void GetButtonCodeText(ButtonCode code, Span<char> buffer) {
		throw new NotImplementedException();
	}

	public IPanel? GetCalculatedFocus() {
		throw new NotImplementedException();
	}

	public void GetCandidate(int num, Span<char> dest) {
		throw new NotImplementedException();
	}

	public int GetCandidateListCount() {
		throw new NotImplementedException();
	}

	public int GetCandidateListPageSize() {
		throw new NotImplementedException();
	}

	public int GetCandidateListPageStart() {
		throw new NotImplementedException();
	}

	public int GetCandidateListSelectedItem() {
		throw new NotImplementedException();
	}

	public nint GetCurrentIMEHandle() {
		throw new NotImplementedException();
	}

	CursorCode cursorOverride;

	public CursorCode GetCursorOveride() {
		return cursorOverride;
	}

	public void GetCursorPos(out int x, out int y) {
		if (IsDispatchingMessageQueue())
			GetCursorPosition(out x, out y);
		else
			inputSystem.GetCursorPosition(out x, out y);
	}

	private bool IsDispatchingMessageQueue() {
		return vgui.IsDispatchingMessages();
	}

	internal void GetCursorPosition(out int x, out int y) {
		ref InputContext context = ref GetInputContext(Context);
		x = context.CursorX;
		y = context.CursorY;
	}

	public nint GetEnglishIMEHandle() {
		throw new NotImplementedException();
	}

	public IPanel? GetFocus() {
		ref InputContext context = ref GetInputContext(Context);
		return context.KeyFocus;
	}

	public int GetIMEConversionModes(Span<IVGuiInput.ConversionModeItem> dest) {
		throw new NotImplementedException();
	}

	public int GetIMELanguageList(Span<IVGuiInput.LanguageItem> dest) {
		throw new NotImplementedException();
	}

	public void GetIMELanguageName(Span<char> buffer) {
		throw new NotImplementedException();
	}

	public void GetIMELanguageShortCode(Span<char> buffer) {
		throw new NotImplementedException();
	}

	public int GetIMESentenceModes(Span<IVGuiInput.SentenceModeItem> dest) {
		throw new NotImplementedException();
	}

	public nint GetIMEWindow() {
		throw new NotImplementedException();
	}

	public IPanel? GetModalSubTree() {
		ref InputContext context = ref GetInputContext(Context);
		if (Unsafe.IsNullRef(ref context))
			return null;

		return context.ModalSubTree;
	}

	public IPanel? GetMouseCapture() {
		ref InputContext context = ref GetInputContext(Context);
		if (Unsafe.IsNullRef(ref context))
			return null;

		return context.MouseCapture;
	}

	public IPanel? GetMouseOver() {
		return GetInputContext(Context).MouseOver;
	}

	public bool GetShouldInvertCompositionString() {
		return false;
	}

	public void HandleExplicitSetCursor() {
		ref InputContext context = ref GetInputContext(Context);

		if (context.SetCursorExplicitly) {
			context.CursorX = context.ExternallySetCursorX;
			context.CursorY = context.ExternallySetCursorY;
			context.SetCursorExplicitly = false;

			context.LastPostedCursorX = context.LastPostedCursorY = -9999;

			SetCursorPos(context.CursorX, context.CursorY);
			UpdateMouseFocus(context.CursorX, context.CursorY);
		}
	}

	public bool InternalButtonCodePressed(ButtonCode code) {
		throw new NotImplementedException();
	}

	public bool InternalButtonCodeReleased(ButtonCode code) {
		throw new NotImplementedException();
	}

	public void InternalButtonCodeTyped(ButtonCode code) {
		throw new NotImplementedException();
	}

	// Trying not to create so many heap keyvalues... hopefully this optimization is alright...
	KeyValues SetCursorPosKV = new("SetCursorPosInternal");

	public bool InternalCursorMoved(int x, int y) {
		SetCursorPosKV.SetInt("xpos", x);
		SetCursorPosKV.SetInt("ypos", y);
		vgui.PostMessage(null, SetCursorPosKV, null, type: MessageItemType.SetCursorPos);
		return true;
	}

	public bool InternalMouseDoublePressed(ButtonCode code) {
		throw new NotImplementedException();
	}

	public bool InternalMousePressed(ButtonCode code) {
		bool filter = false;

		ref InputContext context = ref GetInputContext(Context);
		IPanel? targetPanel = context.MouseOver;
		if (context.MouseCapture != null && IsChildOfModalPanel(context.MouseCapture)) {
			if (code == ButtonCode.MouseWheelDown || code == ButtonCode.MouseWheelUp)
				return true;

			filter = true;

			bool captureLost = code == context.MouseCaptureStartCode || context.MouseCaptureStartCode == (ButtonCode)(-1);

			vgui.PostMessage(context.MouseCapture, new KeyValues("MousePressed", "code", (int)code), null);
			targetPanel = context.MouseCapture;

			if (captureLost)
				SetMouseCapture(null);
		}
		else if ((context.MouseFocus != null) && IsChildOfModalPanel(context.MouseFocus)) {
			if (code == ButtonCode.MouseWheelDown || code == ButtonCode.MouseWheelUp)
				return true;

			filter = true;

			vgui.PostMessage(context.MouseFocus, new KeyValues("MousePressed", "code", (int)code), null);
			targetPanel = context.MouseFocus;
		}
		else if (context.ModalSubTree != null && context.UnhandledMouseClickListener != null) {
			IPanel? p = GetMouseFocusIgnoringModalSubtree();
			if (p != null) {
				bool isChildOfModal = IsChildOfModalSubTree(p);
				bool isUnRestricted = !context.RestrictMessagesToModalSubTree;

				if (isUnRestricted != isChildOfModal) {
					if (code == ButtonCode.MouseWheelDown || code == ButtonCode.MouseWheelUp)
						return true;

					vgui.PostMessage(context.UnhandledMouseClickListener, new KeyValues("UnhandledMouseClick", "code", (int)code), null);
					targetPanel = context.UnhandledMouseClickListener;
					filter = true;
				}
			}
		}


		if (IsChildOfModalPanel(targetPanel))
			surface.SetTopLevelFocus(targetPanel);

		return filter;
	}

	private IPanel? GetMouseFocusIgnoringModalSubtree() {
		IPanel? focus = null;

		ref InputContext context = ref GetInputContext(Context);

		int x, y;
		x = context.CursorX;
		y = context.CursorY;

		if (context.RootPanel == null) {
			if (surface.IsCursorVisible() && surface.IsWithin(x, y)) {
				for (int i = surface.GetPopupCount() - 1; i >= 0; i--) {
					IPanel? popup = surface.GetPopup(i);
					IPanel? panel = popup;
					bool wantsMouse = panel!.IsMouseInputEnabled();
					bool isVisible = !surface.IsMinimized(panel);

					while (isVisible && panel != null && panel.GetParent() != null) {
						isVisible = panel.IsVisible();
						panel = panel.GetParent();
					}


					if (wantsMouse && isVisible) {
						focus = popup!.IsWithinTraverse(x, y, false);
						if (focus != null)
							break;
					}
				}
				focus ??= surface.GetEmbeddedPanel().IsWithinTraverse(x, y, false);
			}
		}
		else
			focus = context.RootPanel.IsWithinTraverse(x, y, false);

		if (!IsChildOfModalPanel(focus, false))
			focus = null;

		return focus;
	}

	public bool InternalMouseReleased(ButtonCode code) {
		bool filter = false;

		ref InputContext context = ref GetInputContext(Context);
		if (context.MouseCapture != null && IsChildOfModalPanel(context.MouseCapture)) {
			if (code == ButtonCode.MouseWheelDown || code == ButtonCode.MouseWheelUp)
				return true;

			vgui.PostMessage(context.MouseCapture, new KeyValues("MouseReleased", "code", (int)code), null);
			filter = true;
		}
		else if ((context.MouseFocus != null) && IsChildOfModalPanel(context.MouseFocus)) {
			if (code == ButtonCode.MouseWheelDown || code == ButtonCode.MouseWheelUp)
				return true;

			vgui.PostMessage(context.MouseFocus, new KeyValues("MouseReleased", "code", (int)code), null);
			filter = true;
		}

		return filter;
	}

	public bool InternalMouseWheeled(int delta) {
		bool filter = false;

		ref InputContext context = ref GetInputContext(Context);
		if ((context.MouseFocus != null) && IsChildOfModalPanel(context.MouseFocus)) {
			vgui.PostMessage(context.MouseFocus, new KeyValues("MouseWheeled", "delta", delta), null);
			filter = true;
		}
		return filter;
	}

	public unsafe bool IsKeyDown(ButtonCode code) => GetInputContext(Context).KeyDown[code - ButtonCode.KeyFirst];

	public unsafe bool IsMouseDown(ButtonCode code) => GetInputContext(Context).MouseDown[code - ButtonCode.MouseFirst];

	public void OnButtonCodeUnhandled(int code) {
		throw new NotImplementedException();
	}

	public void OnChangeIME(bool forward) {
		throw new NotImplementedException();
	}

	public void OnChangeIMEByHandle(nint handleValue) {
		throw new NotImplementedException();
	}

	public void OnChangeIMEConversionModeByHandle(nint handleValue) {
		throw new NotImplementedException();
	}

	public void OnChangeIMESentenceModeByHandle(nint handleValue) {
		throw new NotImplementedException();
	}

	public void OnIMEChangeCandidates() {
		throw new NotImplementedException();
	}

	public void OnIMECloseCandidates() {
		throw new NotImplementedException();
	}

	public void OnIMEComposition(int flags) {
		throw new NotImplementedException();
	}

	public void OnIMEEndComposition() {
		throw new NotImplementedException();
	}

	public void OnIMERecomputeModes() {
		throw new NotImplementedException();
	}

	public void OnIMEShowCandidates() {
		throw new NotImplementedException();
	}

	public void OnIMEStartComposition() {
		throw new NotImplementedException();
	}

	public void OnInputLanguageChanged() {
		throw new NotImplementedException();
	}

	void PanelDeleted(IPanel? focus, ref InputContext context) {
		if (context.KeyFocus == focus)
			context.KeyFocus = null;
		if (context.MouseOver == focus)
			context.MouseOver = null;
		if (context.OldMouseFocus == focus)
			context.OldMouseFocus = null;
		if (context.MouseFocus == focus)
			context.MouseFocus = null;

		if (context.MouseCapture == focus) {
			SetMouseCapture(null);
			context.MouseCapture = null;
		}
		if (context.AppModalPanel == focus)
			ReleaseAppModalSurface();
		if (context.UnhandledMouseClickListener == focus)
			context.UnhandledMouseClickListener = null;
		if (context.ModalSubTree == focus) {
			context.ModalSubTree = null;
			context.RestrictMessagesToModalSubTree = false;
		}

		if (focus != null)
			context.KeyCodeUnhandledListeners?.Remove(focus);
	}

	public void PanelDeleted(IPanel? panel) {
		foreach (HInputContext key in Contexts.Keys)
			PanelDeleted(panel, ref CollectionsMarshal.GetValueRefOrNullRef(Contexts, key));
		PanelDeleted(panel, ref DefaultInputContext);
	}

	public void PostCursorMessage() {
		ref InputContext context = ref GetInputContext(Context);

		if (context.SetCursorExplicitly) {
			context.CursorX = context.ExternallySetCursorX;
			context.CursorY = context.ExternallySetCursorY;
		}

		if (context.LastPostedCursorX == context.CursorX && context.LastPostedCursorY == context.CursorY)
			return;

		context.LastPostedCursorX = context.CursorX;
		context.LastPostedCursorY = context.CursorY;

		if (context.MouseCapture != null) {
			if (!IsChildOfModalPanel(context.MouseCapture))
				return;

			vgui.PostMessage(context.MouseCapture, new KeyValues("CursorMoved").AddSubKey("xpos", context.CursorX).AddSubKey("ypos", context.CursorY), null);
		}
		else if (context.MouseFocus != null)
			vgui.PostMessage(context.MouseFocus, new KeyValues("CursorMoved").AddSubKey("xpos", context.CursorX).AddSubKey("ypos", context.CursorY), null);
	}

	public void RegisterButtonCodeUnhandledListener(IPanel? panel) {
		throw new NotImplementedException();
	}

	public void ReleaseAppModalSurface() {
		ref InputContext context = ref GetInputContext(Context);
		context.AppModalPanel = null;
	}

	public void ReleaseModalSubTree() {
		throw new NotImplementedException();
	}

	readonly static KeyValues KV_KeyFocusTicked = new("KeyFocusTicked");
	readonly static KeyValues KV_MouseFocusTicked = new("MouseFocusTicked");
	public unsafe void RunFrame() {
		if (DebugMessages == -1) {
			DebugMessages = CommandLine.FindParm("-vguifocus") != 0 ? 1 : 0;
		}

		ref InputContext context = ref GetInputContext(Context);

		if (context.KeyFocus != null)
			if (IsChildOfModalPanel(context.KeyFocus))
				vgui.PostMessage(context.KeyFocus, KV_KeyFocusTicked, null);

		if (context.MouseFocus != null) {
			if (IsChildOfModalPanel(context.MouseFocus))
				vgui.PostMessage(context.MouseFocus, KV_MouseFocusTicked, null);
		}
		else if (context.AppModalPanel != null) {
			surface.SetCursor(CursorCode.Arrow);
		}

		int i;

		for (i = 0; i < (int)ButtonCode.MouseCount; i++) {
			context.MousePressed[i] = false;
			context.MouseDoublePressed[i] = false;
			context.MouseReleased[i] = false;
		}

		for (i = 0; i < (int)ButtonCode.Count; i++) {
			context.KeyPressed[i] = false;
			context.KeyTyped[i] = false;
			context.KeyReleased[i] = false;
		}

		IPanel? wantedKeyFocus = CalculateNewKeyFocus();

		if (context.KeyFocus != wantedKeyFocus) {
			bool shouldEnable = false, shouldDisable = false;
			if (context.KeyFocus != null) {
				context.KeyFocus.InternalFocusChanged(true);

				KeyValues pMessage = new KeyValues("KillFocus");
				pMessage.SetPtr("newPanel", wantedKeyFocus);
				context.KeyFocus.SendMessage(pMessage, wantedKeyFocus);

				context.KeyFocus?.Repaint();

				IPanel? dlg = context.KeyFocus;
				while (dlg != null && !dlg.IsPopup())
					dlg = dlg.GetParent();

				dlg?.Repaint();
				shouldDisable = true;
			}
			if (wantedKeyFocus != null) {
				wantedKeyFocus.InternalFocusChanged(false);

				KeyValues pMessage = new KeyValues("SetFocus");
				wantedKeyFocus.SendMessage(pMessage, null);
				wantedKeyFocus.Repaint();

				IPanel? dlg = wantedKeyFocus;
				while (dlg != null && !dlg.IsPopup())
					dlg = dlg.GetParent();

				dlg?.Repaint();
				shouldDisable = false;
				shouldEnable = true;
			}

			if (shouldEnable)
				inputSystem.StartTextInput();
			else if (shouldDisable)
				inputSystem.StopTextInput();

			context.KeyFocus = wantedKeyFocus;
			context.KeyFocus?.MoveToFront();
		}

		ButtonCode repeatCode = ButtonCode.None; //context.KeyRepeater?.KeyRepeated();
		if (repeatCode != 0) {
			InternalKeyCodePressed(repeatCode);
		}
	}

	private IPanel? CalculateNewKeyFocus() {
		ref InputContext context = ref GetInputContext(Context);

		IPanel? wantedKeyFocus = null;
		IPanel? root = context.RootPanel;
		IPanel? top = root;

		if (surface.GetPopupCount() > 0) {
			int nIndex = surface.GetPopupCount();

			while (nIndex > 0) {
				top = surface.GetPopup(--nIndex);

				if (top != null && top.IsVisible() && top.IsKeyboardInputEnabled() && !surface.IsMinimized(top) && IsChildOfModalSubTree(top) && (root == null || top.HasParent(root))) {
					bool isVisible = top.IsVisible();
					IPanel? p = top.GetParent();
					while (p != null && isVisible) {
						if (!p.IsVisible()) {
							isVisible = false;
							break;
						}
						p = p.GetParent();
					}

					if (isVisible && !surface.IsMinimized(top))
						break;
				}

				top = root;
			}
		}

		if (top != null) {
			wantedKeyFocus = top.GetCurrentKeyFocus();
			wantedKeyFocus ??= top;
		}

		if (!surface.HasFocus())
			wantedKeyFocus = null;

		if (!IsChildOfModalPanel(wantedKeyFocus))
			wantedKeyFocus = null;

		return wantedKeyFocus;
	}

	private void InternalKeyCodePressed(ButtonCode repeatCode) {
		throw new NotImplementedException();
	}

	private bool IsChildOfModalPanel(IPanel? panel, bool checkModalSubTree = true) {
		if (panel == null)
			return true;

		ref InputContext context = ref GetInputContext(Context);

		if (context.AppModalPanel != null)
			if (!panel.HasParent(context.AppModalPanel))
				return false;

		if (!checkModalSubTree)
			return true;

		return IsChildOfModalSubTree(panel);
	}

	private bool IsChildOfModalSubTree(IPanel panel) {
		if (panel == null)
			return true;

		ref InputContext context = ref GetInputContext(Context);
		if (context.ModalSubTree != null) {
			bool isChildOfModal = panel.HasParent(context.ModalSubTree);
			if (isChildOfModal)
				return context.RestrictMessagesToModalSubTree;
			else
				return !context.RestrictMessagesToModalSubTree;
		}

		return true;
	}

	public void SetAppModalSurface(IPanel? panel) {
		ref InputContext context = ref GetInputContext(Context);
		context.AppModalPanel = panel;
	}

	public void SetCandidateListPageStart(int start) {
		throw new NotImplementedException();
	}

	public void SetCandidateWindowPos(int x, int y) {
		throw new NotImplementedException();
	}

	public void SetCursorOveride(CursorCode cursor) {
		cursorOverride = cursor;
	}
	public void SetCursorPos(int x, int y) {
		inputSystem.SetCursorPosition(x, y);
	}

	public void SetIMEWindow(nint hwnd) {
		throw new NotImplementedException();
	}

	public void SetModalSubTree(IPanel? subTree, IPanel? unhandledMouseClickListener, bool restrictMessagesToSubTree = true) {
		throw new NotImplementedException();
	}

	public void SetModalSubTreeReceiveMessages(bool state) {
		throw new NotImplementedException();
	}

	readonly static KeyValues KV_MouseCaptureLost = new("MouseCaptureLost");
	public void SetMouseCapture(IPanel? panel) {
		if (!IsChildOfModalPanel(panel))
			return;

		ref InputContext context = ref GetInputContext(Context);
		Assert(!Unsafe.IsNullRef(ref context));

		context.MouseCaptureStartCode = (ButtonCode)(-1);

		if (context.MouseCapture != null && panel != context.MouseCapture)
			vgui.PostMessage(context.MouseCapture, KV_MouseCaptureLost, null);

		if (panel == null) {
			if (context.MouseCapture != null)
				surface.EnableMouseCapture(context.MouseCapture, false);
		}
		else
			surface.EnableMouseCapture(panel, true);

		context.MouseCapture = panel;
	}

	public void SetMouseCaptureEx(IPanel? panel, ButtonCode captureStart) {
		throw new NotImplementedException();
	}

	readonly static KeyValues KV_CursorExited = new("CursorExited");
	readonly static KeyValues KV_CursorEntered = new("CursorEntered");
	public void SetMouseFocus(IPanel? newMouseFocus) {
		if (!IsChildOfModalPanel(newMouseFocus))
			return;

		bool wantsMouse, isPopup;
		IPanel? panel = newMouseFocus;

		ref InputContext context = ref GetInputContext(Context);

		wantsMouse = false;
		if (newMouseFocus != null) {
			do {
				wantsMouse = panel!.IsMouseInputEnabled();
				isPopup = panel.IsPopup();
				panel = panel.GetParent();
			}
			while (wantsMouse && !isPopup && panel != null && panel.GetParent() != null);
		}

		if (newMouseFocus != null && !wantsMouse)
			return;

		if (context.MouseOver != newMouseFocus || (context.MouseCapture == null && context.MouseFocus != newMouseFocus)) {
			context.OldMouseFocus = context.MouseOver;
			context.MouseOver = newMouseFocus;

			if (context.OldMouseFocus != null)
				if (context.MouseCapture == null || context.OldMouseFocus == context.MouseCapture)
					vgui.PostMessage(context.OldMouseFocus, KV_CursorExited, null);

			if (context.MouseOver != null)
				if (context.MouseCapture == null || context.MouseOver == context.MouseCapture)
					vgui.PostMessage(context.MouseOver, KV_CursorEntered, null);

			IPanel? newFocus = context.MouseCapture ?? context.MouseOver;
			context.MouseFocus = newFocus;
		}
	}

	public bool ShouldModalSubTreeReceiveMessages() {
		throw new NotImplementedException();
	}

	public void UnregisterButtonCodeUnhandledListener(IPanel? panel) {
		throw new NotImplementedException();
	}

	public void UpdateButtonState(in InputEvent ev) {
		switch (ev.Type) {
			case InputEventType.IE_ButtonPressed:
			case InputEventType.IE_ButtonReleased:
			case InputEventType.IE_ButtonDoubleClicked:
				ButtonCode code = (ButtonCode)ev.Data2;

				if (code.IsKeyCode()) {
					SetKeyCodeState(code, (ev.Type != InputEventType.IE_ButtonReleased));
					break;
				}

				if (code.IsMouseCode()) {
					MouseCodeState state = (ev.Type == InputEventType.IE_ButtonReleased) ? MouseCodeState.Released : MouseCodeState.Pressed; ;
					if (ev.Type == InputEventType.IE_ButtonDoubleClicked) {
						state = MouseCodeState.DoubleClicked;
					}

					SetMouseCodeState(code, state);
					break;

				}
				break;
		}
	}

	private unsafe void SetKeyCodeState(ButtonCode code, bool pressed) {
		if (!code.IsKeyCode())
			return;

		ref InputContext context = ref GetInputContext(Context);
		if (pressed)
			context.KeyPressed[code - ButtonCode.KeyFirst] = true;
		else
			context.KeyReleased[code - ButtonCode.KeyFirst] = true;

		context.KeyDown[code - ButtonCode.KeyFirst] = pressed;
	}

	public unsafe void SetMouseCodeState(ButtonCode code, MouseCodeState state) {
		if (!code.IsMouseCode())
			return;

		ref InputContext context = ref GetInputContext(Context);
		switch (state) {
			case MouseCodeState.Released:
				context.MouseReleased[code - ButtonCode.MouseFirst] = true;
				break;

			case MouseCodeState.Pressed:
				context.MousePressed[code - ButtonCode.MouseFirst] = true;
				break;

			case MouseCodeState.DoubleClicked:
				context.MouseDoublePressed[code - ButtonCode.MouseFirst] = true;
				break;
		}

		context.MouseDown[code - ButtonCode.MouseFirst] = state != MouseCodeState.Released;
	}

	public void UpdateCursorPosInternal(int x, int y) {
		ref InputContext context = ref GetInputContext(Context);
		if (context.CursorX == x && context.CursorY == y)
			return;

		context.CursorX = x;
		context.CursorY = y;

		UpdateMouseFocus(x, y);
	}

	public void UpdateMouseFocus(int x, int y) {
		IPanel? focus = null;

		ref InputContext context = ref GetInputContext(Context);
		// Msg($"x = {x}, y = {y}\n");
		if (surface.IsCursorVisible() && surface.IsWithin(x, y)) {
			int c = surface.GetPopupCount();
			for (int i = c - 1; i >= 0; i--) {
				IPanel? popup = surface.GetPopup(i);
				if (popup == null) continue;
				IPanel? panel = popup;

				if (context.RootPanel != null && !popup.HasParent(context.RootPanel)) {
					continue;
				}

				bool wantsMouse = panel.IsMouseInputEnabled() && IsChildOfModalSubTree(panel);
				if (!wantsMouse)
					continue;

				bool isVisible = !surface.IsMinimized(panel);
				if (!isVisible)
					continue;

				while (isVisible && panel != null && panel.GetParent() != null) {
					isVisible = panel.IsVisible();
					panel = panel.GetParent();
				}

				if (!wantsMouse || !isVisible)
					continue;

				focus = popup.IsWithinTraverse(x, y, false);
				if (focus != null)
					break;
			}
			if (focus == null)
				focus = surface.GetEmbeddedPanel().IsWithinTraverse(x, y, false);
		}

		if (!IsChildOfModalPanel(focus))
			focus = null;

		//Msg($"{focus} at {x}, {y}\n");
		SetMouseFocus(focus);
	}

	public unsafe bool WasKeyPressed(ButtonCode code) => GetInputContext(Context).KeyPressed[code - ButtonCode.KeyFirst];
	public unsafe bool WasKeyReleased(ButtonCode code) => GetInputContext(Context).KeyReleased[code - ButtonCode.KeyFirst];
	public unsafe bool WasKeyTyped(ButtonCode code) => GetInputContext(Context).KeyTyped[code - ButtonCode.KeyFirst];
	public unsafe bool WasMouseDoublePressed(ButtonCode code) => GetInputContext(Context).MouseDoublePressed[code - ButtonCode.KeyFirst];
	public unsafe bool WasMousePressed(ButtonCode code) => GetInputContext(Context).MousePressed[code - ButtonCode.KeyFirst];
	public unsafe bool WasMouseReleased(ButtonCode code) => GetInputContext(Context).MouseReleased[code - ButtonCode.KeyFirst];

	public bool PostKeyMessage(KeyValues message) {
		ref InputContext context = ref GetInputContext(Context);
		if (context.KeyFocus != null && IsChildOfModalPanel(context.KeyFocus)) {
			vgui.PostMessage(context.KeyFocus, message, null);
			return true;
		}
		return false;
	}

	bool IVGuiInput.InternalKeyCodePressed(ButtonCode code) {
		ref InputContext context = ref GetInputContext(Context);

		if (!code.IsKeyCode())
			return false;

		bool filter = PostKeyMessage(new KeyValues("KeyCodePressed").AddSubKey("code", (int)code));
		if (filter)
			context.KeyRepeater?.KeyDown(code);

		return filter;
	}

	public unsafe void InternalKeyCodeTyped(ButtonCode code) {
		ref InputContext context = ref GetInputContext(Context);
		if (!code.IsKeyCode())
			return;

		context.KeyTyped[code - ButtonCode.KeyFirst] = true;

		PostKeyMessage(new KeyValues("KeyCodeTyped").AddSubKey("code", (int)code));
	}

	public unsafe void InternalKeyTyped(char ch) {
		ref InputContext context = ref GetInputContext(Context);
		if (ch <= (int)ButtonCode.KeyLast)
			context.KeyTyped[ch] = true;

		PostKeyMessage(new KeyValues("KeyTyped").AddSubKey("unichar", (int)ch));
	}

	public bool InternalKeyCodeReleased(ButtonCode code) {
		ref InputContext context = ref GetInputContext(Context);
		if (!code.IsKeyCode())
			return false;

		context.KeyRepeater?.KeyUp(code);

		return PostKeyMessage(new KeyValues("KeyCodeReleased").AddSubKey("code", (int)code));
	}

	public void OnKeyCodeUnhandled(ButtonCode code) {

	}
}