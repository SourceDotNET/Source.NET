using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

using Steamworks;

using System.ComponentModel.DataAnnotations;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Source.GUI.Controls;

class HTMLInterior : Panel
{
	HTML HTML;
	public HTMLInterior(HTML parent) : base(parent, "HTMLInterior") {
		HTML = parent;
		SetPaintBackgroundEnabled(false);
		SetKeyboardInputEnabled(false);
		SetMouseInputEnabled(false);
	}
}

class HTMLPopup : Frame
{
	public HTMLPopup(Panel? parent, ReadOnlySpan<char> url, ReadOnlySpan<char> title) : base(null, "HTMLPopup", true) {

	}
}

public class HTML : Panel
{
	public static Panel Create_HTML() => new HTML(null, null);

	Panel InteriorPanel;
	ScrollBar HBar, VBar;
	FileOpenDialog? FileOpenDialog;

	public class HTMLFindBar : EditablePanel
	{
		TextEntry FindBar;
		HTML Parent;
		Label FindCountLabel;
		bool Hidden;
		public HTMLFindBar(HTML parent) : base(parent, null) {
			Parent = parent;
			Hidden = false;
			FindBar = new(this, "FindEntry");
			FindBar.AddActionSignalTarget(parent);
			FindBar.SendNewLine(true);
			FindCountLabel = new(this, "FindCount", "");
			FindCountLabel.SetVisible(false);

			if (fileSystem.FileExists("resource/layout/htmlfindbar.layout"))
				LoadControlSettings("resource/layout/htmlfindbar.layout");
		}

		internal void SetHidden(bool v) => Hidden = v;

		internal bool BIsHidden() => Hidden;

		internal void GetText(Span<char> txt) => FindBar.GetText(txt);

		internal void SetText(ReadOnlySpan<char> v) => FindBar.SetText(v);

		internal void HideCountLabel() => FindCountLabel.SetVisible(false);

		internal void ShowCountLabel() => FindCountLabel.SetVisible(true);
	}

	HTMLFindBar FindBar;

	int MouseX, MouseY;

	int ScrollBorderX, ScrollBorderY;
	int WideLastHTMLSize, TallLastHTMLSize;
	int CopyLinkMenuItemID;

	bool ScrollBarEnabled;
	bool ContextMenuEnabled;
	int ScrollbarSize;
	bool bNewWindowsOnly;
	int ViewSourceAllowedIndex;
	string? DragURL;
	int DragStartX, DragStartY;

	struct CustomURLHandler_t
	{
		public Panel Panel;
		public InlineArray32<char> URL;
	}
	List<CustomURLHandler_t> CustomURLHandlers;

	TextureID HTMLTextureID;

	int AllocedTextureWidth;
	int AllocedTextureHeight;
	bool NeedsFullTextureUpload;
	string CurrentURL;
	bool InFind;
	string LastSearchString;

	bool CanGoBack;
	bool CanGoForward;

	struct LinkAtPos_t()
	{
		public UInt32 X;
		public UInt32 Y;
		public string URL;
	}
	LinkAtPos_t LinkAtPos;
	bool RequestingDragURL;
	bool RequestingCopyLink;

	struct ScrollData_t()
	{
		public bool Visible;
		public int Max;
		public int Scroll;
		public float Zoom;

		public static bool operator ==(ScrollData_t lhs, ScrollData_t rhs) => lhs.Visible == rhs.Visible && lhs.Max == rhs.Max && lhs.Scroll == rhs.Scroll;
		public static bool operator !=(ScrollData_t lhs, ScrollData_t rhs) => !(lhs == rhs);
		public override readonly bool Equals(object? obj) => obj is ScrollData_t other && this == other;
		public override readonly int GetHashCode() => HashCode.Combine(Visible, Max, Scroll);
	}

	ScrollData_t ScrollHorizontal;
	ScrollData_t ScrollVertical;
	float Zoom;

	string PendingURLLoad;
	string PendingPostData;

	Menu ContextMenu;

	struct CustomCursorCache_t
	{
		public float CacheTime;
		public CursorCode Cursor;
		public object? Data;

		public CustomCursorCache_t() { }
		public CustomCursorCache_t(object? data) {
			CacheTime = -1;
			Cursor = CursorCode.No;
			Data = data;
		}

		public static bool operator ==(CustomCursorCache_t lhs, CustomCursorCache_t rhs) => ReferenceEquals(lhs.Data, rhs.Data);
		public static bool operator !=(CustomCursorCache_t lhs, CustomCursorCache_t rhs) => !(lhs == rhs);
		public override readonly bool Equals(object? obj) => obj is CustomCursorCache_t other && this == other;
		public override readonly int GetHashCode() => Data?.GetHashCode() ?? 0;
	}
	List<CustomCursorCache_t> HCursor;

	HHTMLBrowser BrowserHandle;

	BrowserCallback<HTML_NeedsPaint_t> NeedsPaint;
	BrowserCallback<HTML_StartRequest_t> StartRequest;
	BrowserCallback<HTML_URLChanged_t> URLChanged;
	BrowserCallback<HTML_FinishedRequest_t> FinishedRequest;
	BrowserCallback<HTML_OpenLinkInNewTab_t> LinkInNewTab;
	BrowserCallback<HTML_ChangedTitle_t> ChangeTitle;
	BrowserCallback<HTML_NewWindow_t> NewWindow;
	BrowserCallback<HTML_FileOpenDialog_t> FileLoadDialog;
	BrowserCallback<HTML_SearchResults_t> SearchResults;
	BrowserCallback<HTML_CloseBrowser_t> CloseBrowser;
	BrowserCallback<HTML_HorizontalScroll_t> HorizScroll;
	BrowserCallback<HTML_VerticalScroll_t> VertScroll;
	BrowserCallback<HTML_LinkAtPosition_t> LinkAtPosResp;
	BrowserCallback<HTML_JSAlert_t> JSAlert;
	BrowserCallback<HTML_JSConfirm_t> JSConfirm;
	BrowserCallback<HTML_CanGoBackAndForward_t> CanGoBackForward;
	BrowserCallback<HTML_SetCursor_t> SetCursor;
	BrowserCallback<HTML_StatusText_t> StatusText;
	BrowserCallback<HTML_ShowToolTip_t> ShowTooltip;
	BrowserCallback<HTML_UpdateToolTip_t> UpdateTooltip;
	BrowserCallback<HTML_HideToolTip_t> HideTooltip;

	CallResult<HTML_BrowserReady_t> SteamCallResultBrowserReady;

	class BrowserCallback<T> where T : struct
	{
		static readonly FieldInfo HandleField = typeof(T).GetField("unBrowserHandle")!;

		readonly HTML Owner;
		readonly Action<T> Handler;
		readonly Callback<T> Callback;

		public BrowserCallback(HTML owner, Action<T> handler) {
			Owner = owner;
			Handler = handler;
			Callback = Callback<T>.Create(OnCallback);
		}

		void OnCallback(T param) {
			if ((HHTMLBrowser)HandleField.GetValue(param)! == Owner.BrowserHandle)
				Handler(param);
		}
	}

	public HTML(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		NeedsPaint = new(this, BrowserNeedsPaint);
		StartRequest = new(this, BrowserStartRequest);
		URLChanged = new(this, BrowserURLChanged);
		FinishedRequest = new(this, BrowserFinishedRequest);
		LinkInNewTab = new(this, BrowserOpenNewTab);
		ChangeTitle = new(this, BrowserSetHTMLTitle);
		NewWindow = new(this, BrowserPopupHTMLWindow);
		FileLoadDialog = new(this, BrowserFileLoadDialog);
		SearchResults = new(this, BrowserSearchResults);
		CloseBrowser = new(this, BrowserClose);
		HorizScroll = new(this, BrowserHorizontalScrollBarSizeResponse);
		VertScroll = new(this, BrowserVerticalScrollBarSizeResponse);
		LinkAtPosResp = new(this, BrowserLinkAtPositionResponse);
		JSAlert = new(this, BrowserJSAlert);
		JSConfirm = new(this, BrowserJSConfirm);
		CanGoBackForward = new(this, BrowserCanGoBackandForward);
		SetCursor = new(this, BrowserSetCursor);
		StatusText = new(this, BrowserStatusText);
		ShowTooltip = new(this, BrowserShowToolTip);
		UpdateTooltip = new(this, BrowserUpdateToolTip);
		HideTooltip = new(this, BrowserHideToolTip);

		HTMLTextureID = 0;
		CanGoBack = false;
		CanGoForward = false;
		InFind = false;
		RequestingDragURL = false;
		RequestingCopyLink = false;
		Zoom = 100.0f;
		NeedsFullTextureUpload = false;

		InteriorPanel = new HTMLInterior(this);
		SetPostChildPaintEnabled(true);

		BrowserHandle = HHTMLBrowser.Invalid;

		SteamAPI.Init();
		SteamHTMLSurface.Init();

		SteamCallResultBrowserReady = CallResult<HTML_BrowserReady_t>.Create(OnBrowserReady);

		SteamAPICall_t SteamAPICall = SteamHTMLSurface.CreateBrowser("Valve Client", null); // surface().GetWebkitHTMLUserAgentString() todo
		SteamCallResultBrowserReady.Set(SteamAPICall);

		ScrollBorderX = ScrollBorderY = 0;
		ScrollBarEnabled = true;
		ContextMenuEnabled = true;
		bNewWindowsOnly = false;
		MouseX = MouseY = 0;
		DragStartX = DragStartY = 0;
		ViewSourceAllowedIndex = -1;
		WideLastHTMLSize = TallLastHTMLSize = 0;

		HBar = new ScrollBar(this, "HorizScrollBar", false);
		HBar.SetVisible(false);
		HBar.AddActionSignalTarget(this);

		VBar = new ScrollBar(this, "VertScrollBar", true);
		VBar.SetVisible(false);
		VBar.AddActionSignalTarget(this);

		FindBar = new HTMLFindBar(this);
		FindBar.SetZPos(2);
		FindBar.SetVisible(false);

		ContextMenu = new Menu(this, "contextmenu");
		ContextMenu.AddMenuItem("#vgui_HTMLBack", new KeyValues("Command", "command", "back"), this);
		ContextMenu.AddMenuItem("#vgui_HTMLForward", new KeyValues("Command", "command", "forward"), this);
		ContextMenu.AddMenuItem("#vgui_HTMLReload", new KeyValues("Command", "command", "reload"), this);
		ContextMenu.AddMenuItem("#vgui_HTMLStop", new KeyValues("Command", "command", "stop"), this);
		ContextMenu.AddSeparator();
		ContextMenu.AddMenuItem("#vgui_HTMLCopyUrl", new KeyValues("Command", "command", "copyurl"), this);
		CopyLinkMenuItemID = ContextMenu.AddMenuItem("#vgui_HTMLCopyLink", new KeyValues("Command", "command", "copylink"), this);
		ContextMenu.AddMenuItem("#TextEntry_Copy", new KeyValues("Command", "command", "copy"), this);
		ContextMenu.AddMenuItem("#TextEntry_Paste", new KeyValues("Command", "command", "paste"), this);
		ContextMenu.AddSeparator();
		ViewSourceAllowedIndex = ContextMenu.AddMenuItem("#vgui_HTMLViewSource", new KeyValues("Command", "command", "viewsource"), this);
	}

	public override void Dispose() {
		base.Dispose();

		ContextMenu?.MarkForDeletion();
		SteamHTMLSurface.RemoveBrowser(BrowserHandle);
		HCursor?.Clear();
	}

	void OnBrowserReady(HTML_BrowserReady_t browserReady, bool ioFailure) {
		BrowserHandle = browserReady.unBrowserHandle;
		BrowserResize();

		if (!string.IsNullOrEmpty(PendingURLLoad)) {
			PostURL(PendingURLLoad, PendingPostData, false);
			PendingURLLoad = "";
		}
	}

	void OnSetCursorVGUI(int cursor) => SetCursor((CursorCode)cursor);

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);
		BrowserResize();
	}

	public override void Paint() {
		base.Paint();

		if (HTMLTextureID != 0) {
			Surface.DrawSetTexture(HTMLTextureID);
			Surface.DrawSetColor(255, 255, 255, 255);
			GetSize(out int tw, out int tt);
			Surface.DrawTexturedRect(0, 0, tw, tt);
		}

		if (ScrollBorderX > 0 || ScrollBorderY > 0) {
			GetSize(out int w, out int h);
			IBorder? border = GetBorder();
			int left = 0, top = 0, right = 0, bottom = 0;
			border?.GetInset(out left, out top, out right, out bottom);
			if (ScrollBorderX != 0)
				Surface.DrawFilledRect(w - ScrollBorderX - right, top, w, h - bottom);
			if (ScrollBorderY != 0)
				Surface.DrawFilledRect(left, h - ScrollBorderY - bottom, w - ScrollBorderX - right, h);
		}
	}

	public override void PerformLayout() {
		base.PerformLayout();
		Repaint();
		int vbarInset = VBar.IsVisible() ? VBar.GetWide() : 0;
		int maxw = GetWide() - vbarInset;
		InteriorPanel.SetBounds(0, 0, maxw, GetTall());

		IScheme clientScheme = SchemeManager.GetScheme("ClientScheme");

		int SearchInsetY = QuickPropScale(5);
		int SearchInsetX = QuickPropScale(5);
		int SearchTall = QuickPropScale(24);
		int SearchWide = QuickPropScale(150);

		ReadOnlySpan<char> resourceString = clientScheme.GetResourceString("HTML.SearchInsetY");
		if (!resourceString.IsEmpty)
			SearchInsetY = QuickPropScale(int.Parse(resourceString));

		resourceString = clientScheme.GetResourceString("HTML.SearchInsetX");
		if (!resourceString.IsEmpty)
			SearchInsetX = QuickPropScale(int.Parse(resourceString));

		resourceString = clientScheme.GetResourceString("HTML.SearchTall");
		if (!resourceString.IsEmpty)
			SearchTall = QuickPropScale(int.Parse(resourceString));

		resourceString = clientScheme.GetResourceString("HTML.SearchWide");
		if (!resourceString.IsEmpty)
			SearchWide = QuickPropScale(int.Parse(resourceString));

		FindBar.SetBounds(GetWide() - SearchWide - SearchInsetX - vbarInset, FindBar.BIsHidden() ? -1 * SearchTall - QuickPropScale(5) : SearchInsetY, SearchWide, SearchTall);
	}

	void OpenURL(string URL, string postData, bool force) => PostURL(URL, postData, force);

	void PostURL(string URL, string postData, bool force) {
		if (BrowserHandle == HHTMLBrowser.Invalid) {
			PendingURLLoad = URL;
			PendingPostData = postData;
			return;
		}

		if (false) { // Offline Mode

		}
		else {
			if (!string.IsNullOrEmpty(postData))
				SteamHTMLSurface.LoadURL(BrowserHandle, URL, postData);
			else
				SteamHTMLSurface.LoadURL(BrowserHandle, URL, null);
		}
	}

	bool StopLoading() {
		SteamHTMLSurface.StopLoad(BrowserHandle);
		return true;
	}

	bool Refresh() {
		SteamHTMLSurface.Reload(BrowserHandle);
		return true;
	}

	void GoBack() => SteamHTMLSurface.GoBack(BrowserHandle);

	void GoForward() => SteamHTMLSurface.GoForward(BrowserHandle);

	bool BCanGoBack() => CanGoBack;

	bool BCanGoFoward() => CanGoForward;

	public override void OnSizeChanged(int wide, int tall) {
		base.OnSizeChanged(wide, tall);
		UpdateSizeAndScrollBars();
	}

	void RunJavascript(string script) => SteamHTMLSurface.ExecuteJavascript(BrowserHandle, script);

	static EHTMLMouseButton ConvertMouseCodeToCEFCode(ButtonCode code) => code switch {
		ButtonCode.MouseLeft => EHTMLMouseButton.eHTMLMouseButton_Left,
		ButtonCode.MouseRight => EHTMLMouseButton.eHTMLMouseButton_Right,
		ButtonCode.MouseMiddle => EHTMLMouseButton.eHTMLMouseButton_Middle,
		_ => EHTMLMouseButton.eHTMLMouseButton_Left,
	};

	public override void OnMousePressed(ButtonCode code) {
		DragURL = null;

		if (code == ButtonCode.Mouse4) {
			PostActionSignal(new("HTMLBackRequested"));
			return;
		}

		if (code == ButtonCode.Mouse5) {
			PostActionSignal(new("HTMLForwardRequested"));
			return;
		}

		if (code == ButtonCode.MouseRight && ContextMenuEnabled) {
			GetLinkAtPosition(MouseX, MouseY);
			Menu.PlaceContextMenu(this, ContextMenu);
		}

		RequestFocus();

		if (code != ButtonCode.MouseRight)
			SteamHTMLSurface.MouseDown(BrowserHandle, ConvertMouseCodeToCEFCode(code));

		if (code == ButtonCode.MouseLeft) {
			Input.GetCursorPos(out DragStartX, out DragStartY);
			GetAbsPos(out int htmlx, out int htmly);

			GetLinkAtPosition(DragStartX - htmlx, DragStartY - htmly);

			RequestingDragURL = true;

			if (!string.IsNullOrEmpty(DragURL))
				Input.SetMouseCapture(this);
		}
	}

	public override void OnMouseReleased(ButtonCode code) {
		if (code == ButtonCode.MouseLeft) {
			Input.SetMouseCapture(null);
			Input.SetCursorOveride(0);

			if (!string.IsNullOrEmpty(DragURL) && Input.GetMouseOver() != this && Input.GetMouseOver() != null) {
				KeyValues kv = new("DragDrop");
				if (((Panel?)Input.GetMouseOver())!.RequestInfo(kv) && kv.GetPtr("AcceptPanel") != null) {
					IPanel vpanel = (IPanel)kv.GetPtr("AcceptPanel")!;
					VGui.PostMessage(vpanel, new KeyValues("DragDrop", "text", DragURL), this);
				}
			}
			DragURL = null;
		}

		SteamHTMLSurface.MouseUp(BrowserHandle, ConvertMouseCodeToCEFCode(code));
	}

	public override void OnCursorMoved(int x, int y) {
		if (Input.GetMouseOver() == this) {
			MouseX = x;
			MouseY = y;

			SteamHTMLSurface.MouseMove(BrowserHandle, MouseX, MouseY);
		}
		else if (!string.IsNullOrEmpty(DragURL)) {
			if (Input.GetMouseOver() == null)
				DragURL = null;
		}

		if (!string.IsNullOrEmpty(DragURL) && Input.GetCursorOveride() == 0)
			Input.GetCursorPos(out int gx, out int gy);
	}

	public override void OnMouseDoublePressed(ButtonCode code) => SteamHTMLSurface.MouseDoubleClick(BrowserHandle, ConvertMouseCodeToCEFCode(code));

	public override void OnKeyTyped(char unichar) => SteamHTMLSurface.KeyChar(BrowserHandle, unichar, (EHTMLKeyModifiers)GetKeyModifiers());

	int GetKeyModifiers() {
		int modifierCodes = 0;
		if (Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl))
			modifierCodes |= (int)EHTMLKeyModifiers.k_eHTMLKeyModifier_CtrlDown;

		if (Input.IsKeyDown(ButtonCode.KeyLAlt) || Input.IsKeyDown(ButtonCode.KeyRAlt))
			modifierCodes |= (int)EHTMLKeyModifiers.k_eHTMLKeyModifier_AltDown;

		if (Input.IsKeyDown(ButtonCode.KeyLShift) || Input.IsKeyDown(ButtonCode.KeyRShift))
			modifierCodes |= (int)EHTMLKeyModifiers.k_eHTMLKeyModifier_ShiftDown;

		return modifierCodes;
	}

	void ShowFindDialog() {
		IScheme? clientScheme = SchemeManager.GetScheme("ClientScheme");
		if (clientScheme == null)
			return;

		FindBar.SetVisible(true);
		FindBar.RequestFocus();
		FindBar.SetText("");
		FindBar.HideCountLabel();
		FindBar.SetHidden(false);
		FindBar.GetBounds(out int x, out int y, out int w, out int h);
		FindBar.SetPos(x, -1 * h);

		int searchInsetY = 0;
		ReadOnlySpan<char> resourceString = clientScheme.GetResourceString("HTML.SearchInsetY");
		if (!resourceString.IsEmpty)
			searchInsetY = QuickPropScale(int.TryParse(resourceString, out int value) ? value : 0);

		float animationTime = 0;
		resourceString = clientScheme.GetResourceString("HTML.SearchAnimationTime");
		if (!resourceString.IsEmpty)
			animationTime = strtof(resourceString, out _);

		GetAnimationController().RunAnimationCommand(FindBar, "ypos", searchInsetY, 0, animationTime, Interpolators.Linear);
	}

	void HideFindDialog() {
		IScheme? clientScheme = SchemeManager.GetScheme("ClientScheme");
		if (clientScheme == null)
			return;

		FindBar.GetBounds(out int x, out int y, out int w, out int h);

		float animationTime = 0;
		ReadOnlySpan<char> resourceString = clientScheme.GetResourceString("HTML.SearchAnimationTime");
		if (!resourceString.IsEmpty)
			animationTime = strtof(resourceString, out _);

		GetAnimationController().RunAnimationCommand(FindBar, "ypos", -1 * h - QuickPropScale(5), 0, animationTime, Interpolators.Linear);
		FindBar.SetHidden(true);
		StopFind();
	}

	bool FindDialogVisible() => FindBar.IsVisible() && !FindBar.BIsHidden();

	public override void OnKeyCodeTyped(ButtonCode code) {
		switch (code) {
			case ButtonCode.KeyPageDown: {
					int val = VBar.GetValue();
					val += 200;
					VBar.SetValue(val);
					break;
				}
			case ButtonCode.KeyPageUp: {
					int val = VBar.GetValue();
					val -= 200;
					VBar.SetValue(val);
					break;
				}
			case ButtonCode.KeyF5: {
					Refresh();
					break;
				}
			case ButtonCode.KeyF: {
					if (Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl)
						|| (IsOSX && (Input.IsKeyDown(ButtonCode.KeyLWin) || Input.IsKeyDown(ButtonCode.KeyRWin)))) {
						if (!FindDialogVisible())
							ShowFindDialog();
						else
							HideFindDialog();
						break;
					}
					goto case ButtonCode.KeyEscape;
				}
			case ButtonCode.KeyEscape: {
					if (FindDialogVisible()) {
						HideFindDialog();
						break;
					}
					break;
				}
			case ButtonCode.KeyTab: {
					if (Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl)) {
						base.OnKeyTyped((char)code);
						return;
					}
					break;
				}
		}

		SteamHTMLSurface.KeyDown(BrowserHandle, KeyCode_VGUIToVirtualKey(code), (EHTMLKeyModifiers)GetKeyModifiers());
	}

	public override void OnKeyCodeReleased(ButtonCode code) => SteamHTMLSurface.KeyUp(BrowserHandle, KeyCode_VGUIToVirtualKey(code), (EHTMLKeyModifiers)GetKeyModifiers());

	static uint KeyCode_VGUIToVirtualKey(ButtonCode code) => (uint)input.ButtonCodeToVirtualKey(code);

	public override void OnMouseWheeled(int delta) {
		if (VBar != null) {
			int val = VBar.GetValue();
			val -= (int)(delta * 100.0f / 3);
			VBar.SetValue(val);

			SteamHTMLSurface.MouseWheel(BrowserHandle, (int)(delta * 100.0f / 3));
		}
	}

	void AddCustomURLHandler(ReadOnlySpan<char> customProtocolName, Panel target) { }

	void BrowserResize() {
		if (BrowserHandle == HHTMLBrowser.Invalid)
			return;

		GetSize(out int w, out int h);
		int right = 0, bottom = 0;

		if (WideLastHTMLSize != (w - ScrollBorderX - right) || TallLastHTMLSize != (h - ScrollBorderY - bottom)) {
			WideLastHTMLSize = w - ScrollBorderX - right;
			TallLastHTMLSize = h - ScrollBorderY - bottom;
			if (TallLastHTMLSize <= 0) {
				SetTall(QuickPropScale(64));
				TallLastHTMLSize = QuickPropScale(64) - bottom;
			}

			SteamHTMLSurface.SetSize(BrowserHandle, (uint)WideLastHTMLSize, (uint)TallLastHTMLSize);

			int scrollV = VBar.GetValue();
			int scrollH = HBar.GetValue();

			SteamHTMLSurface.SetHorizontalScroll(BrowserHandle, (uint)scrollH);
			SteamHTMLSurface.SetVerticalScroll(BrowserHandle, (uint)scrollV);
		}
	}

	void OnSliderMoved() {
		if (HBar.IsVisible()) {
			int scrollX = HBar.GetValue();
			SteamHTMLSurface.SetHorizontalScroll(BrowserHandle, (uint)scrollX);
		}

		if (VBar.IsVisible()) {
			int scrollY = VBar.GetValue();
			SteamHTMLSurface.SetVerticalScroll(BrowserHandle, (uint)scrollY);
		}

		PostActionSignal(new("HTMLSliderMoved"));
	}

	bool IsScrolledToBottom() {
		if (!VBar.IsVisible())
			return false;

		return ScrollVertical.Scroll >= ScrollVertical.Max;
	}

	bool IsScrollbarVisible() => VBar.IsVisible();

	void SetScrollbarsEnabled(bool state) => ScrollBarEnabled = state;

	void SetContextMenuEnabled(bool state) => ContextMenuEnabled = state;

	void SetViewSourceEnabled(bool state) => ContextMenu.SetItemVisible(ViewSourceAllowedIndex, state);

	void NewWindowsOnly(bool state) => bNewWindowsOnly = state;

	void AddHeader(string header, string value) => SteamHTMLSurface.AddHeader(BrowserHandle, header, value);

	public override void OnSetFocus() {
		SteamHTMLSurface.SetKeyFocus(BrowserHandle, true);
		base.OnSetFocus();
	}

	public override void OnKillFocus(Panel? panel) {
		base.OnKillFocus(panel);

		if (!ContextMenu.HasFocus())
			SteamHTMLSurface.SetKeyFocus(BrowserHandle, false);
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		if (stricmp(command, "back") == 0)
			PostActionSignal(new KeyValues("HTMLBackRequested"));
		else if (stricmp(command, "forward") == 0)
			PostActionSignal(new KeyValues("HTMLForwardRequested"));
		else if (stricmp(command, "reload") == 0)
			Refresh();
		else if (stricmp(command, "stop") == 0)
			StopLoading();
		else if (stricmp(command, "viewsource") == 0)
			SteamHTMLSurface.ViewSource(BrowserHandle);
		else if (stricmp(command, "copy") == 0)
			SteamHTMLSurface.CopyToClipboard(BrowserHandle);
		else if (stricmp(command, "paste") == 0)
			SteamHTMLSurface.PasteFromClipboard(BrowserHandle);
		else if (stricmp(command, "copyurl") == 0) {
			// system.SetClipboardText(CurrentURL);
		}
		else if (stricmp(command, "copylink") == 0) {
			ContextMenu.GetPos(out int x, out int y);
			GetAbsPos(out int htmlx, out int htmly);

			RequestingCopyLink = true;
			GetLinkAtPosition(x - htmlx, y - htmly);
		}
		else
			base.OnCommand(command);
	}

	void OnFileSelected(ReadOnlySpan<char> selectedFile) {
		byte[] bytes = new byte[Encoding.UTF8.GetByteCount(selectedFile) + 1];
		Encoding.UTF8.GetBytes(selectedFile, bytes);
		nint file = Marshal.AllocHGlobal(bytes.Length);
		Marshal.Copy(bytes, 0, file, bytes.Length);
		nint selectedFiles = Marshal.AllocHGlobal(IntPtr.Size * 2);
		Marshal.WriteIntPtr(selectedFiles, 0, file);
		Marshal.WriteIntPtr(selectedFiles, IntPtr.Size, IntPtr.Zero);
		SteamHTMLSurface.FileLoadDialogResponse(BrowserHandle, selectedFiles);
		Marshal.FreeHGlobal(file);
		Marshal.FreeHGlobal(selectedFiles);

		FileOpenDialog.Close();
	}

	void OnFileSelectionCancelled() {
		SteamHTMLSurface.FileLoadDialogResponse(BrowserHandle, 0);
		FileOpenDialog.Close();
	}

	void Find(string subStr) {
		InFind = false;
		if (LastSearchString == subStr)
			InFind = true;

		LastSearchString = subStr;

		SteamHTMLSurface.Find(BrowserHandle, subStr, InFind, false);
	}

	void FindPrevious() => SteamHTMLSurface.Find(BrowserHandle, LastSearchString, InFind, true);

	void FindNext() => Find(LastSearchString);

	void StopFind() {
		SteamHTMLSurface.StopFind(BrowserHandle);
		InFind = false;
	}

	void OnEditNewLine(Panel panel) => OnTextChanged(panel);

	public override void OnTextChanged(Panel panel) {
		Span<char> txt = stackalloc char[2000];
		FindBar.GetText(txt);
		Find(txt.SliceNullTerminatedString().ToString());
	}

	void BrowserNeedsPaint(HTML_NeedsPaint_t callback) {
		int tw = 0, tt = 0;
		if (HTMLTextureID != 0) {
			tw = AllocedTextureWidth;
			tt = AllocedTextureHeight;
		}

		if (HTMLTextureID != 0 && ((VBar.IsVisible() && callback.unScrollY > 0 && Math.Abs((int)callback.unScrollY - ScrollVertical.Scroll) > 5) || (HBar.IsVisible() && callback.unScrollX > 0 && Math.Abs((int)callback.unScrollX - ScrollHorizontal.Scroll) > 5))) {
			NeedsFullTextureUpload = true;
			return;
		}

		if (NeedsFullTextureUpload || HTMLTextureID == 0 || tw != (int)callback.unWide || tt != (int)callback.unTall) {
			NeedsFullTextureUpload = false;
			if (HTMLTextureID != 0)
				Surface.DeleteTextureByID(HTMLTextureID);

			HTMLTextureID = Surface.CreateNewTextureID(true);
			Surface.DrawSetTextureRGBAEx(HTMLTextureID, callback.pBGRA, (int)callback.unWide, (int)callback.unTall, ImageFormat.BGRA8888);
			AllocedTextureWidth = (int)callback.unWide;
			AllocedTextureHeight = (int)callback.unTall;
		}
		else if ((int)callback.unUpdateWide > 0 && (int)callback.unUpdateTall > 0)
			Surface.DrawUpdateRegionTextureRGBA(HTMLTextureID, (int)callback.unUpdateX, (int)callback.unUpdateY, callback.pBGRA, (int)callback.unUpdateWide, (int)callback.unUpdateTall, ImageFormat.BGRA8888);
		else
			Surface.DrawSetTextureRGBAEx(HTMLTextureID, callback.pBGRA, (int)callback.unWide, (int)callback.unTall, ImageFormat.BGRA8888);

		Repaint();
	}

	bool OnStartRequest(ReadOnlySpan<char> url, ReadOnlySpan<char> target, ReadOnlySpan<char> oostData, bool isRedirect) {
		// throw new NotImplementedException();
		return true; // TODO
	}

	void BrowserStartRequest(HTML_StartRequest_t cmd) => SteamHTMLSurface.AllowStartRequest(BrowserHandle, OnStartRequest(cmd.pchURL, cmd.pchTarget, cmd.pchPostData, cmd.bIsRedirect));

	void BrowserURLChanged(HTML_URLChanged_t cmd) {
		CurrentURL = cmd.pchURL;

		KeyValues msg = new("OnURLChanged");
		msg.SetString("url", cmd.pchURL);
		msg.SetString("postdata", cmd.pchPostData);
		msg.SetInt("isredirect", cmd.bIsRedirect ? 1 : 0);

		PostActionSignal(msg);

		// OnURLChanged(CurrentURL, cmd.pchPostData, cmd.bIsRedirect); todo?
	}

	void BrowserFinishedRequest(HTML_FinishedRequest_t cmd) {
		PostActionSignal(new("OnFinishRequest", "url", cmd.pchURL));

		if (cmd.pchPageTitle.Length > 0)
			PostActionSignal(new("PageTitleChange", "title", cmd.pchPageTitle));

		// OnFinishRequest todo
	}

	void BrowserOpenNewTab(HTML_OpenLinkInNewTab_t cmd) { }

	void BrowserPopupHTMLWindow(HTML_NewWindow_t cmd) {
		HTMLPopup popup = new(this, cmd.pchURL, "");

		uint wide = cmd.unWide;
		uint tall = cmd.unTall;

		if (wide == 0 || tall == 0) {
			wide = (uint)Math.Max(QuickPropScale(640), GetWide());
			tall = (uint)Math.Max(QuickPropScale(480), GetTall());
		}

		popup.SetBounds((int)cmd.unX, (int)cmd.unY, (int)wide, (int)tall);
		popup.SetDeleteSelfOnClose(true);

		if (cmd.unX == 0 || cmd.unY == 0)
			popup.MoveToCenterOfScreen();
		popup.Activate();
	}

	void BrowserSetHTMLTitle(HTML_ChangedTitle_t cmd) {
		PostMessage(GetParent(), new("OnSetHTMLTitle", "title", cmd.pchTitle));
		OnSetHTMLTitle(cmd.pchTitle);
	}

	public virtual void OnSetHTMLTitle(string pchTitle) {

	}

	void BrowserStatusText(HTML_StatusText_t cmd) => PostActionSignal(new("OnSetStatusText", "status", cmd.pchMsg));

	void BrowserSetCursor(HTML_SetCursor_t cmd) { }

	void BrowserFileLoadDialog(HTML_FileOpenDialog_t cmd) {
		FileOpenDialog?.Dispose();
		FileOpenDialog = null;

		FileOpenDialog = new(this, cmd.pchTitle, true);
		FileOpenDialog.SetStartDirectory(cmd.pchInitialFile);
		FileOpenDialog.AddActionSignalTarget(this);
		FileOpenDialog.SetAutoDelete(true);
		FileOpenDialog.DoModal();
	}

	void BrowserShowToolTip(HTML_ShowToolTip_t cmd) { }

	void BrowserUpdateToolTip(HTML_UpdateToolTip_t cmd) { }

	void BrowserHideToolTip(HTML_HideToolTip_t cmd) { }

	void BrowserSearchResults(HTML_SearchResults_t cmd) {
		if (cmd.unResults == 0)
			FindBar.HideCountLabel();
		else
			FindBar.ShowCountLabel();

		if (cmd.unResults > 0)
			FindBar.SetDialogVariable("findcount", cmd.unResults);

		if (cmd.unCurrentMatch > 0)
			FindBar.SetDialogVariable("findactive", cmd.unCurrentMatch);

		FindBar.InvalidateLayout();
	}

	void BrowserClose(HTML_CloseBrowser_t cmd) => PostActionSignal(new("OnCloseWindow"));

	void BrowserHorizontalScrollBarSizeResponse(HTML_HorizontalScroll_t cmd) {
		ScrollData_t scrollHorizontal = new() {
			Scroll = (int)cmd.unScrollCurrent,
			Max = (int)cmd.unScrollMax,
			Visible = cmd.bVisible,
			Zoom = cmd.flPageScale
		};

		if (scrollHorizontal != ScrollHorizontal) {
			ScrollHorizontal = scrollHorizontal;
			UpdateSizeAndScrollBars();
			NeedsFullTextureUpload = true;
		}
		else
			ScrollHorizontal = scrollHorizontal;
	}

	void BrowserVerticalScrollBarSizeResponse(HTML_VerticalScroll_t cmd) {
		ScrollData_t scrollVertical = new() {
			Scroll = (int)cmd.unScrollCurrent,
			Max = (int)cmd.unScrollMax,
			Visible = cmd.bVisible,
			Zoom = cmd.flPageScale
		};

		if (scrollVertical != ScrollHorizontal) {
			ScrollHorizontal = scrollVertical;
			UpdateSizeAndScrollBars();
			NeedsFullTextureUpload = true;
		}
		else
			ScrollHorizontal = scrollVertical;
	}

	void BrowserLinkAtPositionResponse(HTML_LinkAtPosition_t cmd) {
		LinkAtPos.URL = cmd.pchURL;
		LinkAtPos.X = cmd.x;
		LinkAtPos.Y = cmd.y;

		ContextMenu.SetItemVisible(CopyLinkMenuItemID, LinkAtPos.URL.Length != 0);
		if (RequestingDragURL) {
			RequestingDragURL = false;
			DragURL = LinkAtPos.URL;

			if (DragURL.Length > 0)
				Input.SetMouseCapture(this);
		}

		if (RequestingCopyLink) {
			RequestingCopyLink = false;

			// todo
		}


	}

	void BrowserJSAlert(HTML_JSAlert_t cmd) {
		MessageBox dlg = new(CurrentURL, cmd.pchMessage, this);
		dlg.AddActionSignalTarget(this);
		dlg.SetCommand(new KeyValues("DismissJSDialog", "result", 0));
		dlg.DoModal();
	}

	void BrowserJSConfirm(HTML_JSConfirm_t cmd) {
		MessageBox dlg = new(CurrentURL, cmd.pchMessage, this);
		dlg.AddActionSignalTarget(this);
		dlg.SetOKCommand(new KeyValues("DismissJSDialog", "result", 1));
		dlg.SetCancelCommand(new KeyValues("DismissJSDialog", "result", 0));
		dlg.DoModal();
	}

	void DismissJSDialog(int result) => SteamHTMLSurface.JSDialogResponse(BrowserHandle, result != 0);

	void BrowserCanGoBackandForward(HTML_CanGoBackAndForward_t cmd) {
		CanGoBack = cmd.bCanGoBack;
		CanGoForward = cmd.bCanGoForward;
	}

	void GetLinkAtPosition(int x, int y) => SteamHTMLSurface.GetLinkAtPosition(BrowserHandle, x, y);

	void UpdateSizeAndScrollBars() {
		BrowserResize();
		InvalidateLayout();
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		switch (message.Name) {
			case "ScrollBarSliderMoved":
				OnSliderMoved();
				break;
			case "FileSelected":
				OnFileSelected(message.GetString("fullpath"));
				break;
			case "FileSelectionCancelled":
				OnFileSelectionCancelled();
				break;
			case "TextChanged":
				OnTextChanged((Panel)sender!);
				break;
			case "TextNewLine":
				OnEditNewLine((Panel)sender!);
				break;
			case "DismissJSDialog":
				DismissJSDialog(message.GetInt("result", 0));
				break;
			default:
				base.OnMessage(message, sender);
				break;
		}
	}

#if DEBUG
	[ConCommand("sdn_createhtml")]
	static void CreateHTML(in TokenizedCommand args) {
		Surface.GetScreenSize(out int screenWide, out int screenTall);

		int wide = screenWide / 2;
		int tall = screenTall / 2;

		Frame frame = new(null, "TestHTMLFrame");
		frame.SetParent(Surface.GetEmbeddedPanel());
		frame.SetTitle("HTML Test", true);
		frame.SetSize(wide, tall);
		frame.MoveToCenterOfScreen();
		frame.SetSizeable(true);

		const int inset = 8;
		const int titleInset = 32;
		HTML html = new(frame, "TestHTML");
		html.SetBounds(inset, titleInset, wide - inset * 2, tall - titleInset - inset);
		html.SetAutoResize(PinCorner.TopLeft, AutoResize.DownAndRight, inset, titleInset, -inset, -inset);
		html.SetVisible(true);
		html.OpenURL("https://store.steampowered.com/", "", false);

		frame.Activate();
	}
#endif
}
