using Source.Common.Client;
using Source.Common.Input;
using Source.GUI.Controls;

namespace Game.UI;

public class GameConsoleDialog : ConsoleDialog  {
	public GameConsoleDialog() : base(null, "GameConsole", false) {
		AddActionSignalTarget(this);
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		base.OnKeyCodeTyped(code);

		if (ConsolePanel.TextEntryHasFocus()) {

		}
	}

	readonly public IEngineClient engine = Singleton<IEngineClient>();

	protected override void OnCommandSubmitted(ReadOnlySpan<char> command) => engine.ClientCmd_Unrestricted(command);
}
