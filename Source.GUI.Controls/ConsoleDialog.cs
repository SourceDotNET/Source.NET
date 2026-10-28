using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;

namespace Source.GUI.Controls;

public class NonFocusableMenu : Menu
{
	private Panel? FocusPanel;

	public NonFocusableMenu(Panel? parent, ReadOnlySpan<char> panelName) : base(parent, panelName) {
		FocusPanel = null;
	}

	public void SetFocusPanel(Panel? panel) {
		FocusPanel = panel;
	}

	public override IPanel? GetCurrentKeyFocus() {
		if (FocusPanel == null)
			return this;
		return FocusPanel;
	}
}

public class TabCatchingTextEntry : TextEntry
{
	private Panel CompletionList;

	public TabCatchingTextEntry(Panel? parent, string? name, Panel comp) : base(parent, name) {
		SetAllowNonAsciiCharacters(true);
		SetDragEnabled(true);

		CompletionList = comp;
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.KeyTab)
			GetParent()!.OnKeyCodeTyped(code);
		else if (code != ButtonCode.KeyEnter)
			base.OnKeyCodeTyped(code);
	}

	public override void OnKillFocus(Panel? newPanel) {
		if (newPanel != CompletionList)
			PostMessage(GetParent(), new KeyValues("CloseCompletionList"));
	}
}

struct HistoryItem
{
	public string? Text;
	public string? ExtraText;
	public bool HasExtra;

	public HistoryItem() {
		Text = null;
		ExtraText = null;
		HasExtra = false;
	}

	public HistoryItem(string text, string? extra = null) {
		Assert(text != null);
		Text = null;
		ExtraText = null;
		HasExtra = false;
		SetText(text, extra);
	}

	public ReadOnlySpan<char> GetText() {
		if (Text != null)
			return Text;
		return null;
	}

	public ReadOnlySpan<char> GetExtra() {
		if (ExtraText != null)
			return ExtraText;
		return null;
	}

	public void SetText(ReadOnlySpan<char> text, ReadOnlySpan<char> extra) {
		Text = text.ToString();

		if (!extra.IsEmpty) {
			HasExtra = true;
			ExtraText = extra.ToString();
		}
		else {
			HasExtra = false;
			ExtraText = null;
		}
	}
}

public class ConsolePanel : EditablePanel, IConsoleDisplayFunc
{
	internal RichText History;
	internal TextEntry Entry;
	internal Button Submit;
	internal NonFocusableMenu CompletionList;

	protected Color PrintColor, DPrintColor;

	protected int NextCompletion;
	protected char[] PartialText = new char[256];
	protected char[] PreviousPartialText = new char[256];
	protected bool AutoCompleteMode;
	protected bool WasBackspacing;
	protected bool StatusVersion;

	List<CompletionItem> CompletionItems = [];
	List<HistoryItem> CommandHistory = [];

	static readonly KeyValues KV_ClosedByHittingTilde = new("ClosedByHittingTilde");
	static readonly KeyValues KV_Close = new("Close");

	public override void OnTextChanged(Panel sender) {
		if (sender != Entry)
			return;

		PartialText.CopyTo(PreviousPartialText);
		Array.Clear(PartialText);
		Entry.GetText(PartialText);

		nint len = strlen(PartialText);

		bool hitTilde = len > 0 && (PartialText[len - 1] == '~' || PartialText[len - 1] == '`');
		bool altKeyDown = Input.IsKeyDown(ButtonCode.KeyLAlt) || Input.IsKeyDown(ButtonCode.KeyRAlt);
		bool ctrlKeyDown = Input.IsKeyDown(ButtonCode.KeyLControl) || Input.IsKeyDown(ButtonCode.KeyRControl);

		if (len > 0 && hitTilde) {
			PartialText[len - 1] = '\0';

			if (!altKeyDown && !ctrlKeyDown) {
				Entry.SetText("");
				PostMessage(this, KV_Close);
				PostActionSignal(KV_ClosedByHittingTilde);
			}
			else {
				Entry.SetText(PartialText);
			}
			return;
		}

		AutoCompleteMode = false;

		int prefix = FindChainedPrefix(PartialText);
		RebuildCompletionList(PartialText.AsSpan(prefix));

		if (CompletionItems.Count < 1)
			CompletionList.SetVisible(false);
		else {
			CompletionList.SetVisible(true);
			CompletionList.DeleteAllItems();

			const int MAX_MENU_ITEMS = 10;

			for (int i = 0; i < CompletionItems.Count && i < MAX_MENU_ITEMS; i++) {
				Span<char> text = new char[256];

				if (i == MAX_MENU_ITEMS - 1)
					strcpy(text, "...");
				else
					strcpy(text, CompletionItems[i].GetItemText());

				text = text.SliceNullTerminatedString();

				KeyValues kv = new("CompletionCommand");
				kv.SetString("command", text);
				CompletionList.AddMenuItem(text, kv, this);
			}

			UpdateCompletionListPosition();
		}

		RequestFocus();
		Entry.RequestFocus();
	}

	[UiCommand("submit")]
	private void OnSubmit() {
		Span<char> incoming = stackalloc char[256];
		int len = Entry.GetText(incoming);
		PostActionSignal(new KeyValues("CommandSubmitted", "command", incoming[..len]));

		Print("] ");
		Print(incoming[..len]);
		Print("\n");
		Entry.SetText("");

		OnTextChanged(Entry);

		History.GotoTextEnd();

		string incomingText = incoming[..len].ToString();
		int extraPtr = incomingText.IndexOf(' ');
		string? extra = null;
		if (extraPtr != -1) {
			extra = incomingText[(extraPtr + 1)..];
			incomingText = incomingText[..extraPtr];
		}

		if (incomingText.Length > 0)
			AddToHistory(incomingText, extra);

		CompletionList.SetVisible(false);
		base.OnCommand("submit");
	}

	private void AddToHistory(ReadOnlySpan<char> commandText, ReadOnlySpan<char> extraText) {
		while (CommandHistory.Count > MAX_HISTORY_ITEMS)
			CommandHistory.RemoveAt(0);

		Span<char> command = stackalloc char[commandText.Length + 1];
		if (!commandText.IsEmpty) {
			command.Clear();
			commandText.CopyTo(command);
			int last = commandText.Length - 1;
			if (last >= 0 && command[last] == ' ')
				command[last] = '\0';
		}

		Span<char> extra = stackalloc char[extraText.Length + 1];
		if (!extraText.IsEmpty) {
			extra.Clear();
			extraText.CopyTo(extra);
			int last = extraText.Length - 1;
			while (last >= 0 && extra[last] == ' ') {
				extra[last] = '\0';
				last--;
			}
		}

		HistoryItem item;
		for (int i = CommandHistory.Count - 1; i >= 0; i--) {
			item = CommandHistory[i];

			if (!strieq(command, item.GetText()))
				continue;

			if (!extra.IsEmpty || !item.GetExtra().IsEmpty) {
				if (extra.IsEmpty || item.GetExtra().IsEmpty)
					continue;

				if (!strieq(extra, item.GetExtra()))
					continue;
			}

			CommandHistory.RemoveAt(i);
		}

		item = new HistoryItem();
		item.SetText(command.ToString(), extra.IsEmpty ? null : extra.ToString());
		CommandHistory.Add(item);

		NextCompletion = 0;
		RebuildCompletionList(PartialText);
	}

	private void ClearCompletionList() {
		CompletionItems.Clear();
	}

	ConCommand? FindAutoCompleteCommandFromPartial(ReadOnlySpan<char> text) {
		Span<char> command = stackalloc char[256];
		strcpy(command, text);

		int space = command.IndexOf(' ');
		if (space != -1)
			command[space] = '\0';

		ConCommand? cmd = cvar.FindCommand(command.SliceNullTerminatedString());
		if (cmd == null)
			return null;

		if (!cmd.CanAutoComplete())
			return null;

		return cmd;
	}

	static int FindChainedPrefix(ReadOnlySpan<char> text) {
		bool inQuotes = false;
		int lastSplit = -1;

		for (int i = 0; i < text.Length; i++) {
			char c = text[i];

			if (c == '\0')
				break;

			if (c == '"')
				inQuotes = !inQuotes;
			else if (!inQuotes && c == ';')
				lastSplit = i;
		}

		if (lastSplit < 0)
			return 0;

		int p = lastSplit + 1;

		while (p < text.Length && text[p] == ' ')
			p++;

		return p;
	}

	private void RebuildCompletionList(ReadOnlySpan<char> text) {
		ClearCompletionList();

		int len = text.IndexOf('\0');
		if (len < 1) {
			for (int i = 0; i < CommandHistory.Count; i++) {
				CompletionItem comp = new();
				comp.IsCommand = false;
				comp.Command = null;
				comp.Text = CommandHistory[i];
				CompletionItems.Add(comp);
			}
			return;
		}

		bool NormalBuild = true;
		int space = text.IndexOf(' ');

		if (space != -1) {
			ConCommand? cmd = FindAutoCompleteCommandFromPartial(text);
			if (cmd == null)
				return;

			NormalBuild = false;

			List<string> commands = [.. cmd.AutoCompleteSuggest(text[..len].ToString())];
			//Assert(commands.Count <= COMMAND_COMPLETION_MAXITEMS);
			Assert(commands.Count <= 64);

			foreach (string completion in commands) {
				CompletionItem item = new();
				item.IsCommand = false;
				item.Command = null;
				item.Text = new HistoryItem(completion);
				CompletionItems.Add(item);
			}
		}

		if (NormalBuild) {
			foreach (ConCommandBase cmd in cvar.GetCommands()) {
				if (cmd.IsFlagSet(FCvar.DevelopmentOnly) || cmd.IsFlagSet(FCvar.Hidden))
					continue;

				ReadOnlySpan<char> cmdName = cmd.GetName();
				if (cmdName.Length < len)
					continue;

				if (text[..len].CompareTo(cmdName[..len], StringComparison.OrdinalIgnoreCase) == 0) {
					CompletionItem item = new();
					item.Command = cmd;
					string tst = cmd.GetName();
					if (!cmd.IsCommand()) {
						item.IsCommand = false;
						ConVar? var = cmd as ConVar;
						ConVar_ServerBounded? pBounded = cmd as ConVar_ServerBounded;

						if (pBounded != null || var.IsFlagSet(FCvar.NeverAsString)) {
							string strValue;

							int intVal = pBounded != null ? pBounded.GetInt() : var.GetInt();
							float floatVal = pBounded != null ? pBounded.GetFloat() : var.GetFloat();

							if (floatVal == intVal)
								strValue = intVal.ToString();
							else
								strValue = floatVal.ToString("F");

							item.Text = new HistoryItem(var.GetName(), strValue);
						}
						else {
							item.Text = new HistoryItem(var.GetName(), var.GetString());
						}
					}
					else {
						item.IsCommand = true;
						item.Text = new HistoryItem(tst);
					}

					CompletionItems.Add(item);
				}
			}
		}

		if (CompletionItems.Count >= 2) {
			for (int i = 0; i < CompletionItems.Count; i++) {
				for (int j = i + 1; j < CompletionItems.Count; j++) {
					CompletionItem item1 = CompletionItems[i];
					CompletionItem item2 = CompletionItems[j];

					if (item1.GetName().CompareTo(item2.GetName(), StringComparison.OrdinalIgnoreCase) > 0)
						(CompletionItems[j], CompletionItems[i]) = (CompletionItems[i], CompletionItems[j]);
				}
			}
		}
	}

	public ConsolePanel(Panel? parent, ReadOnlySpan<char> panelName, bool statusVersion) : base(parent, panelName) {
		StatusVersion = statusVersion;

		SetKeyboardInputEnabled(true);

		if (!StatusVersion)
			SetMinimumSize(100, 100);

		History = new RichText(this, "ConsoleHistory");
		History.SetAllowKeyBindingChainToParent(false);
		History.MakeReadyForUse();
		History.SetVerticalScrollbar(!statusVersion);

		if (StatusVersion)
			History.SetDrawOffsets(3, 3);

		History.GotoTextEnd();

		Submit = new Button(this, "ConsoleSubmit", "#Console_Submit");
		Submit.SetCommand("submit");
		Submit.SetVisible(!StatusVersion);

		CompletionList = new NonFocusableMenu(this, "CompletionList");
		CompletionList.SetVisible(false);

		Entry = new TabCatchingTextEntry(this, "ConsoleEntry", CompletionList);
		Entry.AddActionSignalTarget(this);
		Entry.SendNewLine(true);
		CompletionList.SetFocusPanel(Entry);

		PrintColor = new(216, 222, 211, 255);
		DPrintColor = new(196, 181, 80, 255);

		Entry.SetTabPosition(1);

		AutoCompleteMode = false;

		cvar!.InstallConsoleDisplayFunc(this);
	}


	public override void OnKeyCodeTyped(ButtonCode code) {
		base.OnKeyCodeTyped(code);

		if (TextEntryHasFocus()) {
			if (code == ButtonCode.KeyTab) {
				bool reverse = false;
				if (Input.IsKeyDown(ButtonCode.KeyLShift) || Input.IsKeyDown(ButtonCode.KeyRShift))
					reverse = true;

				OnAutoComplete(reverse);
				Entry.RequestFocus();
			}
			else if (code == ButtonCode.KeyDown) {
				OnAutoComplete(false);
				Entry.RequestFocus();
			}
			else if (code == ButtonCode.KeyUp) {
				OnAutoComplete(true);
				Entry.RequestFocus();
			}
		}
	}

	private void OnAutoComplete(bool reverse) {
		if (!AutoCompleteMode) {
			NextCompletion = 0;
			AutoCompleteMode = true;
		}

		if (reverse) {
			NextCompletion -= 2;
			if (NextCompletion < 0)
				NextCompletion = CompletionItems.Count - 1;
		}

		if (NextCompletion < 0 || NextCompletion >= CompletionItems.Count)
			NextCompletion = 0;

		if (NextCompletion < 0 || NextCompletion >= CompletionItems.Count)
			return;

		Span<char> CompletedText = stackalloc char[255];
		CompletionItem item = CompletionItems[NextCompletion];

		if (!item.IsCommand && item.Command != null) {
			ReadOnlySpan<char> cmd = item.GetCommand();
			strcpy(CompletedText, cmd);
		}
		else {
			ReadOnlySpan<char> txt = item.GetItemText();
			strcpy(CompletedText, txt);
		}

		if (!CompletedText.SliceNullTerminatedString().Contains(' '))
			strcat(CompletedText, " ");

		ReadOnlySpan<char> completed = CompletedText.SliceNullTerminatedString();
		int prefix = FindChainedPrefix(PartialText);
		if (prefix > 0) {
			Span<char> full = stackalloc char[512];
			PartialText.AsSpan(0, prefix).CopyTo(full);
			completed.CopyTo(full[prefix..]);
			Entry.SetText(full[..(prefix + completed.Length)]);
		}
		else
			Entry.SetText(completed);

		Entry.GotoTextEnd();
		Entry.SelectNone();

		NextCompletion++;
	}

	public void Clear() {
		History.SetText("");
		History.GotoTextEnd();
	}
	public void ColorPrint(in Color clr, ReadOnlySpan<char> message) {
		if (StatusVersion)
			Clear();
		History.InsertColorChange(in clr);
		History.InsertString(message);
	}

	public void DPrint(ReadOnlySpan<char> message) {
		ColorPrint(DPrintColor, message);
	}

	public void Print(ReadOnlySpan<char> message) {
		ColorPrint(PrintColor, message);
	}

	public override void ApplySchemeSettings(IScheme scheme) {
		base.ApplySchemeSettings(scheme);

		PrintColor = GetSchemeColor("Console.TextColor", scheme);
		DPrintColor = GetSchemeColor("Console.DevTextColor", scheme);
		History.SetFont(scheme.GetFont("ConsoleText", IsProportional()));
		CompletionList.SetFont(scheme.GetFont("DefaultSmall", IsProportional()));

		InvalidateLayout();
	}

	public override void PerformLayout() {
		base.PerformLayout();

		GetFocusNavGroup().SetDefaultButton(Submit);

		IScheme scheme = GetScheme()!;
		Entry.SetBorder(scheme.GetBorder("DepressedButtonBorder"));
		History.SetBorder(scheme.GetBorder("DepressedButtonBorder"));

		GetSize(out int wide, out int tall);

		if (!StatusVersion) {
			const int inset = 8;
			const int entryHeight = 24;
			const int topHeight = 4;
			const int entryInset = 4;
			const int submitWide = 64;
			const int submitInset = 7;

			History.SetPos(inset, inset + topHeight);
			History.SetSize(wide - (inset * 2), tall - (entryInset * 2 + inset * 2 + topHeight + entryHeight));
			History.InvalidateLayout();

			int nSubmitXPos = wide - (inset + submitWide + submitInset);
			Submit.SetPos(nSubmitXPos, tall - (entryInset * 2 + entryHeight));
			Submit.SetSize(submitWide, entryHeight);

			Entry.SetPos(inset, tall - (entryInset * 2 + entryHeight));
			Entry.SetSize(nSubmitXPos - entryInset - 2 * inset, entryHeight);
		}
		else {
			const int inset = 2;

			int entryWidth = wide / 2;
			if (wide > 400) {
				entryWidth = 200;
			}

			Entry.SetBounds(inset, inset, entryWidth, tall - 2 * inset);

			History.SetBounds(inset + entryWidth + inset, inset, (wide - entryWidth) - inset, tall - 2 * inset);
		}

		UpdateCompletionListPosition();
	}

	private void UpdateCompletionListPosition() {
		Entry.GetPos(out int x, out int y);

		if (!StatusVersion)
			y += Entry.GetTall();
		else
			y -= Entry.GetTall() + 4;

		LocalToScreen(ref x, ref y);
		CompletionList.SetPos(x, y);

		if (CompletionList.IsVisible()) {
			Entry.RequestFocus();
			MoveToFront();
			CompletionList.MoveToFront();
		}
	}

	[UiMessage("CloseCompletionList")]
	internal void OnCloseCompletionList() {
		CompletionList.SetVisible(false);
	}

	public bool TextEntryHasFocus() => Input.GetFocus() == Entry;

	public void TextEntryRequestFocus() => Entry.RequestFocus();

	const int MAX_HISTORY_ITEMS = 500;
	struct CompletionItem
	{
		public bool IsCommand;
		public ConCommandBase? Command;
		public HistoryItem? Text;

		public CompletionItem() {
			IsCommand = false;
			Command = null;
			Text = null;
		}

		public ReadOnlySpan<char> GetName() {
			if (IsCommand)
				return Command!.GetName();
			return Command != null ? Command.GetName() : GetCommand();
		}

		public ReadOnlySpan<char> GetItemText() {
			Span<char> text = new char[256];

			if (Text != null) {
				if (Text.Value.HasExtra)
					sprintf(text, "%s %s").S(Text.Value.GetText()).S(Text.Value.GetExtra());
				else
					strcpy(text, Text.Value.GetText());
			}

			return text;
		}

		public ReadOnlySpan<char> GetCommand() {
			char[] text = new char[256];
			text[0] = '\0';
			if (Text != null)
				strcpy(text, Text.Value.GetText());
			return text;
		}
	}

	internal void Hide() {
		OnClose();
		NextCompletion = 0;
		RebuildCompletionList("");
	}
}

public class ConsoleDialog : Frame
{
	protected ConsolePanel ConsolePanel;

#if GMOD_DLL
	internal TextEntry ConsoleFilter;
	internal CvarToggleCheckButton ConsoleFilterToggle;
	internal Button ClearConsoleButton;
	internal Button ExtraConsoleSettingsBtn;
#endif

	public ConsoleDialog(Panel? parent, ReadOnlySpan<char> name, bool statusVersion) : base(parent, name) {
		SetVisible(false);
		SetTitle("#Console_Title", true);

		ConsolePanel = new ConsolePanel(this, "ConsolePage", statusVersion);
		ConsolePanel.AddActionSignalTarget(this);

#if GMOD_DLL
		ConsoleFilter = new(this, "ConsoleFilter");
		ConsoleFilter.SetMaximumCharCount(128);

		ConVarRef filterCvar = new("con_filter_text");
		if (filterCvar.IsValid()) {
			ReadOnlySpan<char> text = filterCvar.GetString();
			ConsoleFilter.SetText(text[..Math.Min(text.Length, 128)]);
		}

		ConsoleFilterToggle = new(this, "ConsoleFilterToggle", "Remove new entries containing:", "con_filter_enable");

		ClearConsoleButton = new(this, "ClearConsoleButton", "Clear All");

		ClearConsoleButton.SetCommand("Clear");

		ExtraConsoleSettingsBtn = new(this, "ExtraConsoleSettingsBtn", "Extra");
		ExtraConsoleSettingsBtn.SetCommand("OpenExtra");
#endif
	}

#if GMOD_DLL
	private enum ExtraItemType
	{
		Cvar,
		Command,
		Separator
	}

	private struct ExtraItem
	{
		public ExtraItemType Type;
		public string Text;
		public string Cmd;
		public int? Value;
	}

	private List<ExtraItem>? ExtraItems = null;

	[UiCommand("OpenExtra")]
	private void OnOpenExtra() {
		Menu menu = new(this, "ConsoleExtraOptions");
		menu.AddActionSignalTarget(this);

		if (ExtraItems == null) {
			ExtraItems = [];
			KeyValues kv = new();
			if (kv.LoadFromFile(fileSystem, "sdn_resource/ConsoleExtraItems.res", "GAME")) {
				for (KeyValues? itemKv = kv.GetFirstSubKey(); itemKv != null; itemKv = itemKv.GetNextKey()) {
					ExtraItem item = new();
					ReadOnlySpan<char> type = itemKv.GetString("type");

					if (type.SequenceEqual("Cvar")) item.Type = ExtraItemType.Cvar;
					else if (type.SequenceEqual("Command")) item.Type = ExtraItemType.Command;
					else if (type.SequenceEqual("Separator")) item.Type = ExtraItemType.Separator;

					item.Text = itemKv.GetString("text").ToString();
					item.Cmd = itemKv.GetString("cmd").ToString();

					ReadOnlySpan<char> val = itemKv.GetString("value");
					if (!val.IsEmpty) item.Value = int.Parse(val);

					ExtraItems.Add(item);
				}
			}
		}

		foreach (ExtraItem item in ExtraItems) {
			if (item.Type == ExtraItemType.Separator)
				menu.AddSeparator();
			else if (item.Type == ExtraItemType.Cvar) {
				KeyValues kv = new("ExtraCmd");
				kv.SetString("cvar", item.Cmd);
				kv.SetInt("value", item.Value ?? -1);

				int itemId = menu.AddCheckableMenuItem(item.Text, kv, this);

				ConVarRef cvar = new(item.Cmd);
				if (cvar.IsValid())
					menu.SetMenuItemChecked(itemId, cvar.GetInt() == (item.Value ?? 1));
			}
			else
				menu.AddMenuItem(item.Text, new KeyValues("ExtraCmd", "cmd", item.Cmd), this, null);
		}

		menu.SetPos(GetX() + ExtraConsoleSettingsBtn.GetX(), GetY() + ExtraConsoleSettingsBtn.GetY() + ExtraConsoleSettingsBtn.GetTall());
		menu.SetVisible(true);
		menu.MoveToFront();
	}

	[UiMessage("ExtraCmd")]
	private void OnExtraCmd(KeyValues msg) {
		if (!msg.GetString("cvar").IsEmpty) {
			ReadOnlySpan<char> cvarName = msg.GetString("cvar");
			ConVarRef var = new(cvarName);
			if (!var.IsValid())
				return;

			int value = msg.GetInt("value", -1);
			int curValue = var.GetInt();

			// This is for cases where the cvar is not just a 0/1 toggle
			if (value != -1) {
				if (curValue != value) var.SetValue(value);
				else var.SetValue(0);
			}
			else {
				if (curValue == 0) var.SetValue(1);
				else var.SetValue(0);
			}
		}
		else if (!msg.GetString("cmd").IsEmpty)
			PostActionSignal(new("CommandSubmitted", "command", msg.GetString("cmd")));
	}
#endif

#if GMOD_DLL
	[UiCommand("Clear")]
	private void OnClear() {
		ConsolePanel.Clear();
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (Input.GetFocus() == ConsoleFilter && code == ButtonCode.KeyEnter) {
			ConVarRef filterText = new("con_filter_text");
			Span<char> filterValue = stackalloc char[128];
			ConsoleFilter.GetText(filterValue);
			filterText.SetValue(filterValue.SliceNullTerminatedString());
			ConsoleFilterToggle.SetSelected(true);
			return;
		}

		base.OnKeyCodeTyped(code);
	}
#endif

	[UiMessage("CommandSubmitted")]
	protected virtual void OnCommandSubmitted(ReadOnlySpan<char> command) {
		PostActionSignal(new KeyValues("CommandSubmitted", "command", command));
	}

	public override void PerformLayout() {
		base.PerformLayout();

		GetClientArea(out int x, out int y, out int w, out int h);
		ConsolePanel.SetBounds(x, y, w, h);

#if GMOD_DLL
		ConsoleFilter.SetSize(200, 20);
		ConsoleFilter.SetPos(w - 218, 9);

		ConsoleFilterToggle.SetSize(210, 20);
		ConsoleFilterToggle.SetPos(w - 432, 8);

		ClearConsoleButton.SetSize(64, 20);
		ClearConsoleButton.SetPos(w - 500, 9);

		ExtraConsoleSettingsBtn.SetSize(48, 20);
		ExtraConsoleSettingsBtn.SetPos(w - 556, 9);
#endif
	}

	public override void OnScreenSizeChanged(int oldWide, int oldTall) {
		base.OnScreenSizeChanged(oldWide, oldTall);

		Surface.GetScreenSize(out int sx, out int sy);
		GetSize(out int w, out int h);

		if (w > sx || h > sy) {
			if (w > sx)
				w = sx;

			if (h > sy)
				h = sy;

			SetSize(w, h);
		}
	}

	[UiMessage("Activate")]
	public override void Activate() {
		base.Activate();
		ConsolePanel.Entry.RequestFocus();
	}

	public void Clear() {
		ConsolePanel.Clear();
	}
	public void Hide() {
		OnClose();
		ConsolePanel.Hide();
	}

	public void Print(ReadOnlySpan<char> msg) => ConsolePanel.Print(msg);
	public void DPrint(ReadOnlySpan<char> msg) => ConsolePanel.DPrint(msg);
	public void ColorPrint(in Color clr, ReadOnlySpan<char> msg) => ConsolePanel.ColorPrint(in clr, msg);
	public void DumpConsoleTextToFile() => throw new NotImplementedException();
}
