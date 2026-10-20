using Source.Common.Formats.Keyvalues;

namespace Source.Common.GUI;

public enum PaintBackgroundType
{
	Filled = 0,
	Textured = 1,
	Box = 2,
	BoxFade = 3
}


public enum PinCorner
{
	TopLeft = 0,
	TopRight,
	BottomLeft,
	BottomRight,

	CenterTop,
	CenterRight,
	CenterBottom,
	CenterLeft,

	Last
};

public enum AutoResize
{
	None = 0,
	Right,
	Down,
	DownAndRight,
}

public interface IPanel : IExtDisposable
{
	// methods
	void SetPos(int x, int y);
	void GetPos(out int x, out int y);
	void SetSize(int wide, int tall);
	void GetSize(out int wide, out int tall);
	int GetWide();
	int GetTall();
	void SetWide(int wide);
	void SetTall(int tall);
	int GetX();
	int GetY();
	void SetMinimumSize(int wide, int tall);
	void GetMinimumSize(out int wide, out int tall);
	void SetZPos(int z);
	int GetZPos();

	void GetAbsPos(out int x, out int y);
	void GetClipRect(out int x0, out int y0, out int x1, out int y1);
#if GMOD_DLL
	void SetClipRect(int x0, int y0, int x1, int y1);
#endif
	void SetInset(int left, int top, int right, int bottom);
	void GetInset(out int left, out int top, out int right, out int bottom);

	public IBorder? GetBorder();
	public void SetBorder(IBorder? border);

	void SetVisible(bool state);
	bool IsVisible();
	void SetParent(IPanel? newParent);
	int GetChildCount();
	IPanel GetChild(int index);
	IEnumerable<IPanel> GetChildren();
	IPanel? GetParent();
	void MoveToFront();
	void MoveToBack();
	bool HasParent(IPanel potentialParent);

	Color GetBgColor();
	Color GetFgColor();
	void SetBgColor(in Color color);
	void SetFgColor(in Color color);
	bool IsPopup();
	void MakePopup(bool showTaskbarIcon = true, bool disabled = false);
	bool IsFullyVisible();

	// gets the scheme this panel uses
	IScheme? GetScheme();
	// gets whether or not this panel should scale with screen resolution
	bool IsProportional();
	// returns true if auto-deletion flag is set
	bool IsAutoDeleteSet();
	// deletes the Panel * associated with the vpanel
	void DeletePanel();

	// input interest
	void SetKeyboardInputEnabled(bool state);
	void SetMouseInputEnabled(bool state);
	bool IsKeyboardInputEnabled();
	bool IsMouseInputEnabled();

	// calculates the panels current position within the hierarchy
	void Solve();

	// gets names of the object (for debugging purposes)
	ReadOnlySpan<char> GetName();
	ReadOnlySpan<char> GetClassName();

	// these pass through to the IClientPanel
	void Think();
	void PerformApplySchemeSettings();
	void PaintTraverse(bool forceRepaint, bool allowForce = true);
	void Repaint();
	IPanel? IsWithinTraverse(int x, int y, bool traversePopups);

	void OnChildAdded(IPanel child);
	void OnSizeChanged(int newWide, int newTall);

	void InternalFocusChanged(bool lost);
	void RequestFocus(int direction = 0);
	bool RequestFocusPrev(IPanel existingPanel);
	bool RequestFocusNext(IPanel existingPanel);
	IPanel? GetCurrentKeyFocus();
	int GetTabPosition();

	bool IsEnabled();
	void SetEnabled(bool state);

	// Used by the drag/drop manager to always draw on top
	bool IsTopmostPopup();
	void SetTopmostPopup(bool state);
	void InvalidateLayout(bool layoutNow = false, bool reloadScheme = false);

	int GetAlpha();
	void SetAlpha(float alpha);
	void TraverseLevel(int v);
	void SetPopup(bool v);
	void OnTick();
	void SendMessage(KeyValues parms, IPanel? sender);
	IPanel? FindChildByName(ReadOnlySpan<char> childName, bool recurseDown);
	void AddActionSignalTarget(IPanel editablePanel);
}

public interface IAnimationController {
	void UpdateAnimations(double time);
}
