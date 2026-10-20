using Source.Common.Formats.Keyvalues;
using Source.GUI.Controls;

namespace Game.ServerBrowser;

class DialogServerPassword : Frame
{
	Label InfoLabel;
	Label GameLabel;
	TextEntry PasswordEntry;
	Button ConnectButton;
	int ServerID;

	public DialogServerPassword(Panel parent) : base(parent, "DialogServerPassword") {
		ServerID = -1;
		SetSize(320, 240);
		SetDeleteSelfOnClose(true);
		SetSizeable(true);

		InfoLabel = new(this, "InfoLabel", "ServerBrowser_ServerRequiresPassword");
		GameLabel = new(this, "GameLabel", "<game label>");
		PasswordEntry = new(this, "PasswordEntry");
		ConnectButton = new(this, "ConnectButton", "ServerBrowser_Connect");
		PasswordEntry.SetTextHidden(true);

		LoadControlSettings("Servers/DialogServerPassword.res");

		SetTitle("#ServerBrowser_ServerRequiresPasswordTitle", true);

		MoveToCenterOfScreen();
	}

	public void Activate(ReadOnlySpan<char> serverName, uint serverID) {
		GameLabel.SetText(serverName);
		ServerID = (int)serverID;
		ConnectButton.SetAsDefaultButton(true);
		PasswordEntry.RequestFocus();

		base.Activate();
	}

	readonly static KeyValues KV_Close = new("Close");
	public override void OnCommand(ReadOnlySpan<char> command) {
		bool close = false;

		if (stricmp(command, "Connect") == 0) {
			KeyValues msg = new("JoinServerWithPassword");
			Span<char> buffer = stackalloc char[64];
			PasswordEntry.GetText(buffer);
			msg.SetString("password", buffer);
			msg.SetInt("serverID", ServerID);
			PostActionSignal(msg);
			close = true;
		}
		else if (stricmp(command, "Close") == 0)
			close = true;
		else
			base.OnCommand(command);

		if (close)
			PostMessage(this, KV_Close);
	}
}