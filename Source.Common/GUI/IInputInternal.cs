using Source.Common.Input;

using HInputContext = int;
namespace Source.Common.GUI;


public enum MouseCodeState
{
	Released,
	Pressed,
	DoubleClicked
}

public interface IVGuiInput
{
	public const HInputContext DEFAULT_INPUT_CONTEXT = ~0;
	// Input

	void SetMouseFocus(IPanel? newMouseFocus);
	void SetMouseCapture(IPanel? panel);

	void GetButtonCodeText(ButtonCode code, Span<char> buffer);

	IPanel? GetFocus();
	IPanel? GetCalculatedFocus();
	IPanel? GetMouseOver();

	void SetCursorPos(int x, int y);
	void GetCursorPos(out int x, out int y);
	bool WasMousePressed(ButtonCode code);
	bool WasMouseDoublePressed(ButtonCode code);
	bool IsMouseDown(ButtonCode code);

	void SetCursorOveride(CursorCode cursor);
	CursorCode GetCursorOveride();

	bool WasMouseReleased(ButtonCode code);
	bool WasKeyPressed(ButtonCode code);
	bool IsKeyDown(ButtonCode code);
	bool WasKeyTyped(ButtonCode code);
	bool WasKeyReleased(ButtonCode code);

	IPanel? GetAppModalSurface();
	void SetAppModalSurface(IPanel? panel);
	void ReleaseAppModalSurface();

	void SetIMEWindow(nint hwnd);
	nint GetIMEWindow();

	void OnChangeIME(bool forward);
	nint GetCurrentIMEHandle();
	nint GetEnglishIMEHandle();

	void GetIMELanguageName(Span<char> buffer);
	void GetIMELanguageShortCode(Span<char> buffer);

	public unsafe struct LanguageItem
	{
		public fixed char ShortName[4];
		public fixed char MenuName[128];
		public nint HandleValue;
		public bool Active;
	};

	public unsafe struct ConversionModeItem
	{
		public fixed char MenuName[128];
		public nint HandleValue;
		public bool Active;
	};

	public unsafe struct SentenceModeItem
	{
		public fixed char MenuName[128];
		public nint HandleValue;
		public bool Active;
	};
	int GetIMELanguageList(Span<LanguageItem> dest);
	int GetIMEConversionModes(Span<ConversionModeItem> dest);
	int GetIMESentenceModes(Span<SentenceModeItem> dest);

	void OnChangeIMEByHandle(nint handleValue);
	void OnChangeIMEConversionModeByHandle(nint handleValue);
	void OnChangeIMESentenceModeByHandle(nint handleValue);

	void OnInputLanguageChanged();
	void OnIMEStartComposition();
	void OnIMEComposition(int flags);
	void OnIMEEndComposition();

	void OnIMEShowCandidates();
	void OnIMEChangeCandidates();
	void OnIMECloseCandidates();
	void OnIMERecomputeModes();

	int GetCandidateListCount();
	void GetCandidate(int num, Span<char> dest);
	int GetCandidateListSelectedItem();
	int GetCandidateListPageSize();
	int GetCandidateListPageStart();

	void SetCandidateWindowPos(int x, int y);

	bool GetShouldInvertCompositionString();
	bool CandidateListStartsAtOne();

	void SetCandidateListPageStart(int start);

	void SetMouseCaptureEx(IPanel? panel, ButtonCode captureStart);

	void RegisterButtonCodeUnhandledListener(IPanel? panel);
	void UnregisterButtonCodeUnhandledListener(IPanel? panel);

	void OnButtonCodeUnhandled(int code);

	void SetModalSubTree(IPanel? subTree, IPanel? unhandledMouseClickListener, bool restrictMessagesToSubTree = true);
	void ReleaseModalSubTree();
	IPanel? GetModalSubTree();

	void SetModalSubTreeReceiveMessages(bool state);
	bool ShouldModalSubTreeReceiveMessages();

	IPanel? GetMouseCapture();
	IPanel? GetMouseFocus();

	// InputInternal

	void RunFrame();

	void UpdateMouseFocus(int x, int y);

	void PanelDeleted(IPanel? panel);

	bool InternalCursorMoved(int x, int y);
	bool InternalMousePressed(ButtonCode code);
	bool InternalMouseDoublePressed(ButtonCode code);
	bool InternalMouseReleased(ButtonCode code);
	bool InternalMouseWheeled(int delta);
	bool InternalButtonCodePressed(ButtonCode code);
	void InternalButtonCodeTyped(ButtonCode code);
	void InternalKeyTyped(char unichar);
	bool InternalButtonCodeReleased(ButtonCode code);

	HInputContext CreateInputContext();
	void DestroyInputContext(HInputContext context);

	void AssociatePanelWithInputContext(HInputContext context, IPanel? root);
	void ActivateInputContext(HInputContext context);

	void PostCursorMessage();

	void UpdateCursorPosInternal(int x, int y);

	void HandleExplicitSetCursor();

	void SetMouseCodeState(ButtonCode code, MouseCodeState state);
	void UpdateButtonState(in InputEvent ev);
	bool InternalKeyCodePressed(ButtonCode code);
	void InternalKeyCodeTyped(ButtonCode data);
	bool InternalKeyCodeReleased(ButtonCode code);
	void OnKeyCodeUnhandled(ButtonCode code);
}
