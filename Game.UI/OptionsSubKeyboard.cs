using CommunityToolkit.HighPerformance;

using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;
using Source.GUI.Controls;

namespace Game.UI;

public class OptionsSubKeyboard : PropertyPage
{
	struct KeyBinding
	{
		public string? Binding;
	}

	OptionsSubKeyboardAdvancedDlg? OptionsSubKeyboardAdvancedDlg;
	ControlsListPanel KeyBindList;
	Button SetBindingButton;
	Button ClearBindingButton;

	readonly KeyBinding[] KeyBindings = new KeyBinding[(int)ButtonCode.Last];
	readonly List<string> KeysToUnbind = [];

	public OptionsSubKeyboard(Panel? parent, ReadOnlySpan<char> name) : base(parent, name) {
		for (int i = 0; i < KeyBindings.Length; i++)
			KeyBindings[i] = new();

		KeyBindList = new(this, "listpanel_keybindlist");

		SaveCurrentBindings();
		ParseActionDescriptions();

		SetBindingButton = new(this, "ChangeKeyButton", "");
		ClearBindingButton = new(this, "ClearKeyButton", "");

		LoadControlSettings("resource/OptionsSubKeyboard.res");

		SetBindingButton.SetEnabled(false);
		ClearBindingButton.SetEnabled(false);
	}

	// FIXME #37
	public override void Dispose() {
		base.Dispose();
		DeleteSavedBindings();
	}

	public override void OnResetData() {
		FillInCurrentBindings();
		if (IsVisible())
			KeyBindList.SetSelectedItem(0);
	}

	public override void OnApplyChanges() => ApplyAllBindings();

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.KeyEnter)
			DoCommand("ChangeKey");
		else
			base.OnKeyCodeTyped(code);
	}

	[UiCommand("Defaults")]
	private void OnDefaults() {
		var box = new QueryBox("#GameUI_KeyboardSettings", "#GameUI_KeyboardSettingsText");
		box.AddActionSignalTarget(this);
		box.SetOKCommand(new KeyValues("Command", "command", "DefaultsOK"));
		box.DoModal();
	}

	[UiCommand("DefaultsOK")]
	private void OnDefaultsOK() {
		FillInDefaultBindings();
		KeyBindList.RequestFocus();
	}

	[UiCommand("ChangeKey")]
	private void OnChangeKey() {
		if (!KeyBindList.IsCapturing())
			KeyBindList.StartCaptureMode(CursorCode.Blank);
		else
			base.OnCommand("ChangeKey");
	}

	[UiCommand("ClearKey")]
	private void OnClearKey() {
		if (!KeyBindList.IsCapturing()) {
			OnKeyCodePressed(ButtonCode.KeyDelete);
			KeyBindList.RequestFocus();
		}
		else
			base.OnCommand("ClearKey");
	}

	readonly IEngineClient engine = Singleton<IEngineClient>();
	public void ParseActionDescriptions() {
		Span<char> binding = stackalloc char[256];
		Span<char> description = stackalloc char[256];

		long size = g_pFileSystem.Size("scripts/kb_act.lst", "MOD");
		if (size <= 0) return;

		Span<byte> fileData = stackalloc byte[(int)size];
		if (!g_pFileSystem.ReadFile("scripts/kb_act.lst", "MOD", fileData.AsBytes(), 0))
			return;

		ReadOnlySpan<byte> data = fileData;

		int sectionIndex = 0;
		Span<char> token = stackalloc char[512];

		while (true) {
			data = engine.ParseFile(data, token);
			if (strlen(token) == 0)
				break;

			strcpy(binding, token);

			data = engine.ParseFile(data, token);
			if (strlen(token) == 0)
				break;

			strcpy(description, token);

			if (description[0] != '=') {
				if (strieq(binding, "blank")) {
					KeyBindList.AddSection(++sectionIndex, description);
					KeyBindList.AddColumnToSection(sectionIndex, "Action", description, SectionedListPanel.ColumnBright, 286);
					KeyBindList.AddColumnToSection(sectionIndex, "Key", "#GameUI_KeyButton", SectionedListPanel.ColumnBright, 128);
				}
				else {
					KeyValues item = new("Item");
					item.SetString("Action", description);
					item.SetString("Binding", binding);
					item.SetString("Key", "");
					KeyBindList.AddItem(sectionIndex, item);
				}
			}
		}
	}

	public KeyValues? GetItemForBinding(ReadOnlySpan<char> binding) {
		for (int i = 0; i < KeyBindList.GetItemCount(); i++) {
			KeyValues? item = KeyBindList.GetItemData(KeyBindList.GetItemIDFromRow(i));
			if (item == null)
				continue;

			KeyValues bindingItem = item.FindKey("Binding")!;
			ReadOnlySpan<char> bindString = bindingItem.GetString();

			if (stricmp(bindString, binding) == 0)
				return item;
		}

		return null;
	}

	public void AddBinding(KeyValues item, ReadOnlySpan<char> keyName) {
		if (stricmp(item.GetString("Key", ""), keyName) == 0)
			return;

		RemoveKeyFromBindItems(item, keyName);

		ReadOnlySpan<char> binding = item.GetString("Binding", "");

		for (int i = 0; i < KeyBindList.GetItemCount(); i++) {
			KeyValues? curItem = KeyBindList.GetItemData(KeyBindList.GetItemIDFromRow(i));
			if (curItem == null)
				continue;

			ReadOnlySpan<char> curBinding = curItem.GetString("Binding", "");

			if (stricmp(curBinding, binding) == 0) {
				curItem.SetString("Key", keyName);
				KeyBindList.InvalidateItem(i);
			}
		}
	}

	public void ClearBindItems() {
		for (int i = 0; i < KeyBindList.GetItemCount(); i++) {
			KeyValues? item = KeyBindList.GetItemData(KeyBindList.GetItemIDFromRow(i));
			if (item == null)
				continue;

			item.SetString("Key", "");

			KeyBindList.InvalidateItem(i);
		}

		KeyBindList.InvalidateLayout();
	}

	public void RemoveKeyFromBindItems(KeyValues? orgItem, ReadOnlySpan<char> key) {
		if (key.IsEmpty)
			return;

		string keyName = new(key);

		for (int i = 0; i < KeyBindList.GetItemCount(); i++) {
			KeyValues? item = KeyBindList.GetItemData(KeyBindList.GetItemIDFromRow(i));
			if (item == null)
				continue;

			if (stricmp(keyName, item.GetString("Key", "")) == 0) {
				bool clearEntry = true;

				if (orgItem != null) {
					ReadOnlySpan<char> orgBinding = orgItem.GetString("Binding", "");
					ReadOnlySpan<char> binding = item.GetString("Binding", "");
					if (stricmp(orgBinding, binding) == 0)
						clearEntry = false;
				}

				if (clearEntry) {
					item.SetString("Key", "");
					KeyBindList.InvalidateItem(i);
				}
			}
		}

		KeyBindList.InvalidateLayout();
	}

	public void FillInCurrentBindings() {
		KeysToUnbind.Clear();

		ClearBindItems();

		bool joystick = false;
		ConVarRef joystickVar = new("joystick");
		if (joystickVar.IsValid())
			joystick = joystickVar.GetBool();

		for (int i = 0; i < KeyBindings.Length; i++) {
			ReadOnlySpan<char> binding = gameuifuncs.GetBindingForButtonCode((ButtonCode)i);
			if (binding.IsEmpty)
				continue;

			KeyValues? item = GetItemForBinding(binding);
			if (item != null) {
				ReadOnlySpan<char> keyName = inputSystem.ButtonCodeToString((ButtonCode)i);

				ReadOnlySpan<char> currentKey = item.GetString("Key", "");
				if (!currentKey.IsEmpty) {
					ButtonCode currentBC = gameuifuncs.GetButtonCodeForBind(currentKey);

					bool shouldOverride = joystick && IsJoystickOrNovintCode((ButtonCode)i) && !IsJoystickOrNovintCode(currentBC);
					if (!shouldOverride)
						continue;

					KeysToUnbind.Remove(new(currentKey));
				}

				AddBinding(item, keyName);

				KeysToUnbind.Add(new(keyName));
			}
		}
	}

	static bool IsJoystickOrNovintCode(ButtonCode code) => code.IsJoystickCode() || code.IsNovintCode();

	public void DeleteSavedBindings() {
		for (int i = 0; i < KeyBindings.Length; i++) {
			if (KeyBindings[i].Binding != null)
				KeyBindings[i].Binding = null;
		}
	}

	public void SaveCurrentBindings() {
		DeleteSavedBindings();

		for (int i = 0; i < (int)ButtonCode.Last; i++) {
			ReadOnlySpan<char> binding = gameuifuncs.GetBindingForButtonCode((ButtonCode)i);
			if (binding.IsEmpty)
				continue;

			KeyBindings[i].Binding = new(binding);
		}
	}

	public void BindKey(ReadOnlySpan<char> key, ReadOnlySpan<char> binding) => engine.ClientCmd_Unrestricted($"bind \"{key}\" \"{binding}\"\n");

	public void UnbindKey(ReadOnlySpan<char> key) => engine.ClientCmd_Unrestricted($"unbind \"{key}\"\n");

	public void ApplyAllBindings() {
		for (int i = 0; i < KeysToUnbind.Count; i++)
			UnbindKey(KeysToUnbind[i]);
		KeysToUnbind.Clear();

		DeleteSavedBindings();

		for (int i = 0; i < KeyBindList.GetItemCount(); i++) {
			KeyValues? item = KeyBindList.GetItemData(KeyBindList.GetItemIDFromRow(i));
			if (item == null)
				continue;

			ReadOnlySpan<char> binding = item.GetString("Binding", "");
			if (binding.IsEmpty)
				continue;

			ReadOnlySpan<char> keyName = item.GetString("Key", "");
			if (!keyName.IsEmpty) {
				BindKey(keyName, binding);
				ButtonCode code = inputSystem.StringToButtonCode(keyName);
				if (code != ButtonCode.Invalid)
					KeyBindings[(int)code].Binding = new(binding);
			}
		}

		engine.ClientCmd_Unrestricted("exec userconfig.cfg\nhost_writeconfig\n");
	}

	public void FillInDefaultBindings() {
		long size = g_pFileSystem.Size("cfg/config_default.cfg");
		if (size <= 0) return;

		Span<byte> fileData = stackalloc byte[(int)size];
		if (!g_pFileSystem.ReadFile("cfg/config_default.cfg", null, fileData.AsBytes(), 0))
			return;

		ClearBindItems();

		ReadOnlySpan<byte> data = fileData;
		Span<char> cmd = stackalloc char[1024];
		Span<char> keyName = stackalloc char[1024];
		Span<char> binding = stackalloc char[1024];

		while (true) {
			data = engine.ParseFile(data, cmd);
			if (strlen(cmd) == 0)
				break;

			if (stricmp(cmd, "bind") == 0) {
				data = engine.ParseFile(data, keyName);
				if (strlen(keyName) == 0)
					break;

				data = engine.ParseFile(data, binding);
				if (strlen(keyName) == 0)
					break;

				KeyValues? item = GetItemForBinding(binding[..(int)strlen(binding)]);
				if (item != null)
					AddBinding(item, keyName[..(int)strlen(keyName)]);
			}
		}

		PostActionSignal(KV_ApplyButtonEnable);

		KeyValues? consoleItem = GetItemForBinding("toggleconsole");
		if (consoleItem != null)
			AddBinding(consoleItem, "`");

		KeyValues? escapeItem = GetItemForBinding("cancelselect");
		if (escapeItem != null)
			AddBinding(escapeItem, "ESCAPE");
	}

	[UiMessage("ItemSelected")]
	public void ItemSelected(int itemID) {
		KeyBindList.SetItemOfInterest(itemID);

		if (KeyBindList.IsItemIDValid(itemID)) {
			SetBindingButton.SetEnabled(true);

			KeyValues? kv = KeyBindList.GetItemData(itemID);
			if (kv != null) {
				ReadOnlySpan<char> key = kv.GetString("Key", null);
				if (!key.IsEmpty)
					ClearBindingButton.SetEnabled(true);
				else
					ClearBindingButton.SetEnabled(false);

				if (kv.GetInt("Header") != 0)
					SetBindingButton.SetEnabled(false);
			}
		}
		else {
			SetBindingButton.SetEnabled(false);
			ClearBindingButton.SetEnabled(false);
		}
	}

	static readonly KeyValues KV_ApplyButtonEnable = new("ApplyButtonEnable");
	public void Finish(ButtonCode code) {
		int r = KeyBindList.GetItemOfInterest();
		KeyBindList.EndCaptureMode(CursorCode.Arrow);

		KeyValues? item = KeyBindList.GetItemData(r);
		if (item != null) {
			if (code != ButtonCode.None && code != ButtonCode.KeyEscape && code != ButtonCode.Invalid) {
				AddBinding(item, inputSystem.ButtonCodeToString(code));
				PostActionSignal(KV_ApplyButtonEnable);
			}

			KeyBindList.InvalidateItem(r);
		}

		SetBindingButton.SetEnabled(true);
		ClearBindingButton.SetEnabled(true);
	}

	public override void OnThink() {
		base.OnThink();

		if (KeyBindList.IsCapturing()) {
			if (engine.CheckDoneKeyTrapping(out ButtonCode code))
				Finish(code);
		}
	}

	public override void OnKeyCodePressed(ButtonCode code) {
		if (!KeyBindList.IsCapturing()) {
			int r = KeyBindList.GetItemOfInterest();

			bool visible = KeyBindList.GetCellBounds(r, 1, out _, out _, out _, out _);
			if (visible) {
				if (code == ButtonCode.KeyDelete) {
					KeyValues? kv = KeyBindList.GetItemData(r);

					ReadOnlySpan<char> key = kv != null ? kv.GetString("Key", null) : null;
					if (!key.IsEmpty)
						RemoveKeyFromBindItems(null, key);

					ClearBindingButton.SetEnabled(false);
					KeyBindList.InvalidateItem(r);
					PostActionSignal(KV_ApplyButtonEnable);

					return;
				}
			}
		}

		base.OnKeyCodePressed(code);
	}

	[UiCommand("Advanced")]
	public void OpenKeyboardAdvancedDialog() {
		OptionsSubKeyboardAdvancedDlg ??= new(GetParent());
		OptionsSubKeyboardAdvancedDlg.Activate();
	}
}

class OptionsSubKeyboardAdvancedDlg : Frame
{
	public OptionsSubKeyboardAdvancedDlg(Panel? parent) : base(null, null) {
		SetTitle("#GameUI_KeyboardAdvanced_Title", true);
		SetSize(280, 140);
		LoadControlSettings("resource/OptionsSubKeyboardAdvancedDlg.res");
		MoveToCenterOfScreen();
		SetSizeable(false);
		SetDeleteSelfOnClose(true);
	}

#if GMOD_DLL
	public override void Activate() => base.Activate();

	public void OnApplyData() { }
#else
	public override void Activate() {
		base.Activate();

		Input.SetAppModalSurface(this);

		ConVarRef con_enable = new("con_enable");
		if (con_enable.IsValid())
			SetControlInt("ConsoleCheck", con_enable.GetBool() ? 1 : 0);

		ConVarRef hud_fastswitch = new("hud_fastswitch");
		if (hud_fastswitch.IsValid())
			SetControlInt("FastSwitchCheck", hud_fastswitch.GetBool() ? 1 : 0);
	}

	public void OnApplyData() {
		ConVarRef con_enable = new("con_enable");
		con_enable.SetValue(GetControlInt("ConsoleCheck", 0));
		ConVarRef hud_fastswitch = new("hud_fastswitch");
		hud_fastswitch.SetValue(GetControlInt("FastSwitchCheck", 0));
	}
#endif

	[UiCommand("OK")]
	private void OnOK() {
		OnApplyData();
		Close();
	}

	public override void OnKeyCodeTyped(ButtonCode code) {
		if (code == ButtonCode.KeyEscape)
			Close();
		else
			base.OnKeyCodeTyped(code);
	}
}
