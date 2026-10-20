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
	[UiCommand("Connect")]
	private void OnConnect() {
		KeyValues msg = new("JoinServerWithPassword");
		Span<char> buffer = stackalloc char[64];
		PasswordEntry.GetText(buffer);
		msg.SetString("password", buffer);
		msg.SetInt("serverID", ServerID);
		PostActionSignal(msg);
		PostMessage(this, KV_Close);
	}

	[UiCommand("Close")]
	private void OnCloseCommand() => PostMessage(this, KV_Close);
}