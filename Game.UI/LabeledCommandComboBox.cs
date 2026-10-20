using Source;
using Source.Common.Client;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Game.UI;

[PanelAlias("CLabeledCommandComboBox")]
public class LabeledCommandComboBox : ComboBox
{
	readonly public IEngineClient engine = Singleton<IEngineClient>();

	const int MAX_NAME_LEN = 256;
	const int MAX_COMMAND_LEN = 256;

	struct CommandItem
	{
		public char[] name;
		public char[] command;
		public int comboBoxID;
	}

	List<CommandItem> items = [];
	int CurrentSelection;
	int StartSelection;

	public static Panel Create_LabeledCommandComboBox() => new LabeledCommandComboBox(null, null);

	public LabeledCommandComboBox(Panel? parent, ReadOnlySpan<char> name) : base(parent, name, 0, false) {
		AddActionSignalTarget(this);
		CurrentSelection = -1;
		StartSelection = -1;
	}

	public void DeleteAllItems() {
		RemoveAll();
		items.Clear();
	}

	public void AddItem(ReadOnlySpan<char> text, ReadOnlySpan<char> command) {
		CommandItem newItem = new CommandItem {
			name = new char[MAX_NAME_LEN],
			command = new char[MAX_COMMAND_LEN],
			comboBoxID = base.AddItem(text, null)
		};
		items.Add(newItem);

		text.CopyTo(newItem.name);

		if (text[0] == '#') {
			ReadOnlySpan<char> localized = Localize.Find(text);
			if (!localized.IsEmpty)
				localized.CopyTo(newItem.name);
		}

		command.CopyTo(newItem.command);
	}

	public override void ActivateItem(int index) {
		if (index < items.Count) {
			base.ActivateItem(items[index].comboBoxID);
			CurrentSelection = index;
		}
	}

	public void SetInitialItem(int index) {
		if (index < items.Count) {
			StartSelection = index;
			ActivateItem(items[index].comboBoxID);
		}
	}

	readonly static KeyValues KV_ControlModified = new("ControlModified");
	public void OnTextChanged(ReadOnlySpan<char> text) {
		text = text.SliceNullTerminatedString();
		for (int i = 0; i < items.Count; i++) {
			ReadOnlySpan<char> name = items[i].name.AsSpan().SliceNullTerminatedString();
			if (text.Equals(name, StringComparison.OrdinalIgnoreCase)) {
				CurrentSelection = i;
				break;
			}
		}

		if (HasBeenModified())
			PostActionSignal(KV_ControlModified);
	}

	public ReadOnlySpan<char> GetActiveItemCommand() {
		if (CurrentSelection == -1)
			return null;

		return items[CurrentSelection].command;
	}

	public void ApplyChanges() {
		if (CurrentSelection == -1) return;
		if (items.Count < 1) return;

		Assert(CurrentSelection < items.Count);
		CommandItem item = items[CurrentSelection];
		engine.ClientCmd_Unrestricted(item.command);
		StartSelection = CurrentSelection;
	}

	public bool HasBeenModified() => StartSelection != CurrentSelection;

	public void Reset() {
		if (StartSelection != -1)
			ActivateItem(StartSelection);
	}

	public override void OnMessage(KeyValues message, IPanel? sender) {
		switch (message.Name) {
			case "TextChanged":
				OnTextChanged(message.GetString("text", ""));
				return;
			default:
				base.OnMessage(message, sender);
				return;
		}
	}
}