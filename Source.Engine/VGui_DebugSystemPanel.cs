using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Source.Engine;
#if !SWDS
class DebugMenuButton : MenuButton
{
	Menu Menu;
	public DebugMenuButton(Panel parent, ReadOnlySpan<char> name, ReadOnlySpan<char> labelText) : base(parent, name, labelText) {
		MakePopup();

		Menu = new(this, "DebugMenu");
		Menu.AddMenuItem("Debug Panel", "toggledebugpanel", parent);
		Menu.AddMenuItem("Quit", "Quit", parent);

		SetOpenDirection(MenuDirection.DOWN);
	}
}

class DebugCommandButton : Button
{
	public DebugCommandButton(Panel parent, ReadOnlySpan<char> name, ReadOnlySpan<char> labelText, ReadOnlySpan<char> command) : base(parent, name, labelText) {
		AddActionSignalTarget(this);
		SetCommand(command);
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		cbuf.AddText(command.ToString() + "\n");
	}

	public override void OnTick() { }
}

class DebugCommandCheckbox : CheckButton
{
	[CvarIgnore]
	private ConVar? Var;
	public DebugCommandCheckbox(Panel parent, ReadOnlySpan<char> name, ReadOnlySpan<char> labelText, ReadOnlySpan<char> command) : base(parent, name, labelText) {
		Var = cvar.FindVar(command);
		SetCommand(command);
		AddActionSignalTarget(this);
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		if (Var == null)
			return;

		cbuf.AddText($"{Var.GetName()} {Var.GetInt()}\n");
	}
}

class DebugIncrementCVarButton : Button
{
	[CvarIgnore]
	private ConVar? Var;
	float MinValue;
	float MaxValue;
	float Increment;
	float PreviousValue;

	public DebugIncrementCVarButton(Panel parent, ReadOnlySpan<char> name, ReadOnlySpan<char> labelText, ReadOnlySpan<char> cvarName) : base(parent, name, labelText) {
		TokenizedCommand args = new();
		args.Tokenize(cvarName);

		Var = null;
		if (args.ArgC() >= 4) {
			Var = cvar.FindVar(args.Arg(0));
			MinValue = args.Arg(1, 0);
			MaxValue = args.Arg(2, 0);
			Increment = args.Arg(3, 0);
		}

		SetCommand("increment");
		AddActionSignalTarget(this);

		PreviousValue = -9999.0f;

		OnTick();
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		if (Var == null)
			return;

		float curValue = Var.GetFloat();
		curValue += Increment;

		if (curValue > MaxValue)
			curValue = MinValue;
		else if (curValue < MinValue)
			curValue = MaxValue;

		Var.SetValue(curValue);
	}

	public override void OnTick() {
		if (Var == null)
			return;

		if (Var.GetFloat() == PreviousValue)
			return;

		Span<char> buff = stackalloc char[512];
		sprintf(buff, $"{Var.GetName()} {Var.GetFloat():F2}");
		SetText(buff);

		SizeToContents();
		PreviousValue = Var.GetFloat();
	}
}

class DebugOptionsPage : PropertyPage
{
	List<Panel> LayoutItems = [];

	public DebugOptionsPage(Panel parent, ReadOnlySpan<char> name) : base(parent, name) {
		VGui.AddTickSignal(this, 250);
	}

	public override void OnTick() {
		base.OnTick();

		if (!IsVisible())
			return;

		int count = LayoutItems.Count;
		for (int i = 0; i < count; i++) {
			Panel item = LayoutItems[i];
			item.OnTick();
		}
	}

	public override void PerformLayout() {
		base.PerformLayout();

		int count = LayoutItems.Count;
		int x = 5;
		int y = 5;

		int w = 150;
		int h = 18;
		int gap = 2;

		int tall = GetTall();

		for (int i = 0; i < count; i++) {
			Panel item = LayoutItems[i];
			item.SetBounds(x, y, w, h);

			y += h + gap;
			if (y >= tall - h) {
				x += w + gap;
				y = 5;
			}
		}
	}

	public void Init(KeyValues kv) {
		for (KeyValues? control = kv.GetFirstSubKey(); control != null; control = control.GetNextKey()) {
			ReadOnlySpan<char> t = control.GetString("command", "");
			if (!t.IsEmpty) {
				DebugCommandButton btn = new(this, "CommandButton", control.Name, t);
				LayoutItems.Add(btn);
				continue;
			}

			t = control.GetString("togglecvar", "");
			if (!t.IsEmpty) {
				DebugCommandCheckbox checkbox = new(this, "CommandCheck", control.Name, t);
				LayoutItems.Add(checkbox);
				continue;
			}

			t = control.GetString("incrementcvar", "");
			if (!t.IsEmpty) {
				DebugIncrementCVarButton increment = new(this, "IncrementCVar", control.Name, t);
				LayoutItems.Add(increment);
				continue;
			}
		}
	}
}

class DebugOptionsPanel : PropertyDialog
{
	public DebugOptionsPanel(Panel parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetTitle("Debug Options", true);

		KeyValues? kv = new("DebugOptions");
		if (kv != null) {
			kv.LoadFromFile(g_pFileSystem, "scripts/debugoptions.txt");
			for (KeyValues? pageKv = kv.GetFirstSubKey(); pageKv != null; pageKv = pageKv.GetNextKey()) {
				if (pageKv.Name.Equals("width", StringComparison.OrdinalIgnoreCase)) {
					SetWide(pageKv.GetInt());
					continue;
				}
				else if (pageKv.Name.Equals("height", StringComparison.OrdinalIgnoreCase)) {
					SetTall(pageKv.GetInt());
					continue;
				}

				DebugOptionsPage page = new(this, pageKv.Name);
				page.Init(pageKv);

				AddPage(page, pageKv.Name);
			}
		}

		GetPropertySheet().SetTabWidth(72);
		SetPos(((VideoMode_Common)videoMode).GetModeStereoWidth() - GetWide() - 10, 10);
		SetVisible(true);

		if (g_pFileSystem.FileExists("resource/DebugOptionsPanel.res"))
			LoadControlSettings("resource/DebugOptionsPanel.res");
	}
}

class DebugSystemPanel : Panel
{
	DebugMenuButton DebugMenu;
	DebugOptionsPanel OptionsPanel;

	public DebugSystemPanel(Panel parent, ReadOnlySpan<char> name) : base(parent, name) {
		SetBounds(0, 0, ((VideoMode_Common)videoMode).GetModeStereoWidth(), ((VideoMode_Common)videoMode).GetModeStereoHeight());

		SetCursor(CursorCode.Arrow);
		SetVisible(false);
		SetPaintEnabled(false);
		SetPaintBackgroundEnabled(false);

		DebugMenu = new(this, "Debug Menu", "Debug Menu");

		DebugMenu.SetPos(0, 0);
		DebugMenu.SetSize(110, 24);

		OptionsPanel = new(this, "DebugOptions");
	}

	public override void SetVisible(bool state) {
		base.SetVisible(state);
		if (state)
			Surface.SetCursor(GetCursor());
	}

	[UiCommand("toggledebugpanel")]
	private void OnToggleDebugPanel() => OptionsPanel?.SetVisible(!OptionsPanel.IsVisible());

	[UiCommand("quit")]
	private void OnQuit() => cbuf.AddText("quit\n");
}
#endif
