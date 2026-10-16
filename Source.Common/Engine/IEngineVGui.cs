using Source.Common.GUI;
using Source.Common.Input;

namespace Source.Engine;

public enum VGuiPanelType {
	Root,
	GameUIDll,
	ClientDll,
	Tools,
	InGameScreens,
	GameDll,
	ClientDllTools
}
[Flags]
public enum PaintMode {
	UIPanels = 1 << 0,
	InGamePanels = 1 << 1,
	Cursor = 1 << 2
}
public interface IEngineVGui
{
	void ClearConsole();
	public IPanel GetPanel(VGuiPanelType type);
	bool IsConsoleVisible();
	public bool IsGameUIVisible();
	bool Key_Event(in InputEvent ev);
	void UpdateButtonState(in InputEvent ev);
#if GMOD_DLL
	void UpdateCustomProgressBar(float progress, ReadOnlySpan<char> desc);
	void ShowConsole();
#endif
}
