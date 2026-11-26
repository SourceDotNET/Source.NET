#if GMOD_DLL
using Source.Common.GUI;

namespace Source.GUI.Controls;

public class LuaEditablePanel : EditablePanel
{
	static LuaEditablePanel() => ChainToAnimationMap<LuaEditablePanel>();

	public LuaEditablePanel(Panel? parent, ReadOnlySpan<char> panelName) : base(parent, panelName) {
		SetBuildModeEditable(true);
	}

	// FIXME #37
	public override void Dispose() {
		Input.PopAppModalSurface(this);
		base.Dispose();
	}

	public override void OnChildAdded(IPanel child) => base.OnChildAdded(child);

	public override void MarkForDeletion() {
		Input.PopAppModalSurface(this);
		base.MarkForDeletion();
	}

	public override void DoModal() { // TODO: Should this be an override?
		if (Surface.IsCursorVisible())
			Input.PushAppModalSurface(this);
	}
}
#endif
