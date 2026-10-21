using Source;
using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;
using Source.Common.MaterialSystem;
using Source.GUI.Controls;

#if MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Client.GarrysMod;
#endif

public static partial class LuaVGUI
{
	static void UpdateLuaHook(Panel panel, ReadOnlySpan<char> name, ref ILuaObject? hook) {
		LuaObject member = new();
		panel.LuaTable!.GetMember(name, member);
		if (member.isFunction()) {
			hook ??= new LuaObject();
			hook.Set(member);
		}
		else {
			hook?.UnReference();
			hook = null;
		}
		member.UnReference();
	}

	public static void UpdateLuaHooks(Panel panel) {
		if (g_Lua == null || panel.LuaTable == null || !panel.LuaTable.isTable() || panel.IsMarkedForDeletion())
			return;

		UpdateLuaHook(panel, "Paint", ref panel.LuaPaint);
		UpdateLuaHook(panel, "PaintOver", ref panel.LuaPaintOver);
		UpdateLuaHook(panel, "Think", ref panel.LuaThink);
		UpdateLuaHook(panel, "AnimationThink", ref panel.LuaAnimationThink);
		UpdateLuaHook(panel, "OnChildRemoved", ref panel.LuaOnChildRemoved);
		UpdateLuaHook(panel, "OnChildAdded", ref panel.LuaOnChildAdded);
	}

	[LuaMethod]
	static int Panel____tostring(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel == null) {
			lua.PushString("Panel [NULL]");
			return 1;
		}

		panel.GetPos(out int x, out int y);
		lua.PushString($"Panel: [name:{panel.GetName()}][class:{panel.GetClassName()}][{x},{y},{panel.GetWide()},{panel.GetTall()}]");
		return 1;
	}

	[LuaMethod]
	static int Panel____index(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel != null) {
			ILuaObject? table = GetLuaTable(panel);
			if (table != null && table.PushMemberFast(2))
				return 1;
		}

		if (lua.FindOnObjectsMetaTable(1, 2))
			return 1;

		if (panel != null) {
			ReadOnlySpan<byte> key = lua.GetStringBytes(2);
			if (key.Length == 1) {
				if (key[0] == 'x' || key[0] == 'X') {
					panel.GetPos(out int x, out _);
					lua.PushNumber(x);
					return 1;
				}
				if (key[0] == 'y' || key[0] == 'Y') {
					panel.GetPos(out _, out int y);
					lua.PushNumber(y);
					return 1;
				}
			}
		}
		return 0;
	}

	[LuaMethod]
	static int Panel__PaintManual(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.PaintManual(lua.GetType(2) != LuaType.Nil && lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__PaintAt(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.PaintAt((int)lua.CheckNumber(2), (int)lua.CheckNumber(3));
		return 0;
	}

	[LuaMethod]
	static int Panel____newindex(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel == null)
			lua.Error("Tried to use a NULL Panel!");

		ILuaObject key = lua.GetObject(2)!;
		if (key.isString()) {
			ILuaObject value = lua.GetObject(3)!;
			ReadOnlySpan<byte> name = lua.GetStringBytes(2);
			if (name.Length == 1 && (name[0] == 'x' || name[0] == 'X')) {
				panel.GetPos(out _, out int y);
				panel.SetPos((int)value.GetFloat(), y);
				return 0;
			}
			if (name.Length == 1 && (name[0] == 'y' || name[0] == 'Y')) {
				panel.GetPos(out int x, out _);
				panel.SetPos(x, (int)value.GetFloat());
				return 0;
			}
		}

		GetLuaTable(panel)?.SetMemberFast(2, 3);

		LuaType type = lua.GetType(3);
		if (type == LuaType.Function || type == LuaType.Nil)
			UpdateLuaHooks(panel);
		return 0;
	}

	[LuaMethod]
	static (int, int) Panel__GetPos(Panel panel) {
		panel.GetPos(out int x, out int y);
		return (x, y);
	}

	[LuaMethod]
	static (int, int) Panel__GetSize(Panel panel) => (panel.GetWide(), panel.GetTall());

	[LuaMethod]
	static void Panel__SetName(Panel panel, string name) => panel.SetName(name);

	[LuaMethod]
	static string Panel__GetName(Panel panel) => new(panel.GetName());

	[LuaMethod]
	static string Panel__GetClassName(Panel panel) => new(panel.GetClassName());

	[LuaMethod]
	static int Panel__IsVisible(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		lua.PushBool(panel != null && panel.IsVisible());
		return 1;
	}

	[LuaMethod]
	static void Panel__SetVisible(Panel panel, [LuaGet] bool visible) => panel.SetVisible(visible);

	[LuaMethod]
	static void Panel__SetPos(Panel panel, [LuaGet] int x, [LuaGet] int y) => panel.SetPos(x, y);

	[LuaMethod]
	static void Panel__SetSize(Panel panel, [LuaGet] int wide, [LuaGet] int tall) => panel.SetSize(wide, tall);

	[LuaMethod]
	static void Panel__SetParent(ILuaInterface lua, Panel panel) {
		if (!panel.LuaPanel || panel.IsMarkedForDeletion())
			return;

#if MENU_DLL
		Panel? parent = MenuPanels.GetMenuPanel();
#else
		Panel? parent = GModBase.GetGModBasePanel(true);
#endif
		if (lua.GetType(2) == LuaType.Panel)
			parent = Get_Panel(2);

#if MENU_DLL
		if (parent != null && parent != MenuPanels.GetMenuPanel() && parent != MenuPanels.GetOverlayPanel()) {
#else
		if (parent != null && parent != GModBase.GetGModBasePanel(true) && parent != HudGMod.g_HudGMod && parent != GModBase.GetGModParentToHUDPanel()) {
#endif
			if (!parent.LuaPanel || parent.IsMarkedForDeletion())
				return;
		}

		panel.SetParent(parent);
	}

#if !MENU_DLL
	[LuaMethod]
	static int Panel__ParentToHUD(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel == null)
			lua.Error("Tried to use a NULL Panel!");
		else if (!panel.IsMarkedForDeletion())
			panel.SetParent(GModBase.GetGModParentToHUDPanel());
		return 0;
	}
#endif

	[LuaMethod]
	[LuaMethod("Valid")]
	static int Panel__IsValid(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		lua.PushBool(panel != null && (!panel.IsMarkedForDeletion() || panel.RunningOnRemove));
		return 1;
	}

	[LuaMethod]
	static int Panel__Remove(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel != null && panel.LuaPanel) {
			panel.MarkForDeletion();
			panel.GetParent()?.InvalidateLayout(false, false);
		}
		return 0;
	}

	[LuaMethod]
	static int Panel__GetWide(Panel panel) => panel.GetWide();

	[LuaMethod]
	static int Panel__GetTall(Panel panel) => panel.GetTall();

	[LuaMethod]
	static int Panel__GetTable(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel == null)
			return 0;
		GetLuaTable(panel)!.Push();
		return 1;
	}

	[LuaMethod]
	static int Panel__GetParent(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel == null)
			lua.Error("Tried to use a NULL Panel!");

		Panel? parent = panel.GetParent();
		if (parent == null)
			return 0;
		parent.PushLua(lua, PanelClass.Type);
		return 1;
	}

	[LuaMethod]
	static int Panel__IsEnabled(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		lua.PushBool(panel == null || panel.IsEnabled());
		return 1;
	}

	[LuaMethod]
	static void Panel__SetAutoDelete(Panel panel, [LuaGet] bool state) => panel.SetAutoDelete(state);

	[LuaMethod]
	static void Panel__SetMouseInputEnabled(Panel panel, [LuaGet] bool state) => panel.SetMouseInputEnabled(state);

	[LuaMethod]
	[LuaMethod("SetKeyBoardInputEnabled")]
	static void Panel__SetKeyboardInputEnabled(Panel panel, [LuaGet] bool state) => panel.SetKeyboardInputEnabled(state);

	[LuaMethod]
	static void Panel__SetEnabled(Panel panel, [LuaGet] bool state) => panel.SetEnabled(state);

	[LuaMethod]
	static void Panel__SetMinimumSize(Panel panel, [LuaGet] int wide, [LuaGet] int tall) => panel.SetMinimumSize(wide, tall);

	[LuaMethod]
	static bool Panel__IsMarkedForDeletion(Panel panel) => panel.IsMarkedForDeletion();

	[LuaMethod]
	static void Panel__MakePopup(Panel panel) {
		panel.MakePopup(true, false);
		panel.SetMouseInputEnabled(true);
		panel.SetKeyboardInputEnabled(true);
	}

	[LuaMethod]
	static void Panel__InvalidateLayout(Panel panel, [LuaGet] bool layoutNow) => panel.InvalidateLayout(layoutNow, false);

	[LuaMethod]
	static void Panel__SetZPos(Panel panel, [LuaGet] int z) => panel.SetZPos(z);

	[LuaMethod]
	static int Panel__GetZPos(Panel panel) => panel.GetZPos();

	[LuaMethod]
	static bool Panel__HasFocus(Panel panel) => panel.HasFocus();

	[LuaMethod]
	static bool Panel__HasHierarchicalFocus(Panel panel) {
		IPanel? focus = vguiInput.GetFocus();
		return focus != null && focus.HasParent(panel);
	}

	[LuaMethod]
	static int Panel__SetContentAlignment(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is not Label label)
			return 0;

		switch ((int)lua.GetNumber(2)) {
			case 1: label.SetContentAlignment(Alignment.Southwest); break;
			case 2: label.SetContentAlignment(Alignment.South); break;
			case 3: label.SetContentAlignment(Alignment.Southeast); break;
			case 4: label.SetContentAlignment(Alignment.West); break;
			case 5: label.SetContentAlignment(Alignment.Center); break;
			case 6: label.SetContentAlignment(Alignment.East); break;
			case 7: label.SetContentAlignment(Alignment.Northwest); break;
			case 8: label.SetContentAlignment(Alignment.North); break;
			case 9: label.SetContentAlignment(Alignment.Northeast); break;
		}
		return 0;
	}

	[LuaMethod]
	static int Panel__GetContentSize(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		if (panel is not Label label)
			return 0;
		label.GetContentSize(out int wide, out int tall);
		lua.PushNumber(wide);
		lua.PushNumber(tall);
		return 2;
	}

	[LuaMethod]
	static int Panel__GetChildren(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		lua.CreateTable();
		int index = 1;
		for (int i = 0; i < panel.GetChildCount(); i++) {
			Panel? child = panel.GetChild(i);
			if (child == null || !child.LuaPanel)
				continue;
			lua.PushNumber(index++);
			child.PushLua(lua, LuaType.Panel);
			lua.SetTable(-3);
		}
		return 1;
	}

	[LuaMethod]
	static int Panel__GetChild(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		int index = (int)lua.GetNumber(2);
		if (index < 0) {
			index += panel.GetChildCount();
			if (index < 0)
				return 0;
		}
		if (index >= panel.GetChildCount())
			return 0;
		Panel? child = panel.GetChild(index);
		if (child == null || !child.LuaPanel)
			return 0;
		child.PushLua(lua, LuaType.Panel);
		return 1;
	}

	static void ChildrenSize(Panel panel, out int wide, out int tall) {
		wide = 0;
		tall = 0;
		for (int i = 0; i < panel.GetChildCount(); i++) {
			Panel? child = panel.GetChild(i);
			if (child == null || !child.IsVisible() || child.IsMarkedForDeletion())
				continue;
			child.GetPos(out int x, out int y);
			wide = Math.Max(wide, x + child.GetWide());
			tall = Math.Max(tall, y + child.GetTall());
		}
		wide += panel.DockPaddingRight;
		tall += panel.DockPaddingBottom;
	}

	[LuaMethod]
	static int Panel__SizeToChildren(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		ChildrenSize(panel, out int wide, out int tall);
		if (wide >= 0 && lua.GetBool(2))
			panel.SetWide(wide);
		if (tall >= 0 && lua.GetBool(3))
			panel.SetTall(tall);
		return 0;
	}

	[LuaMethod]
	static int Panel__ChildrenSize(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		ChildrenSize(panel, out int wide, out int tall);
		lua.PushNumber(wide);
		lua.PushNumber(tall);
		return 2;
	}

	[LuaMethod]
	static int Panel__SetMultiline(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		if (panel is TextEntry textEntry) {
			textEntry.SetMultiline(lua.GetBool(2));
			textEntry.SetCatchEnterKey(true);
		}
		return 0;
	}

	[LuaMethod]
	static int Panel__GetText(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is TextEntry textEntry) {
			int size = textEntry.GetTextLength() * 4 + 1;
			if (size > 1024) {
				Span<char> large = new char[size];
				textEntry.GetText(large);
				lua.PushString(new string(((ReadOnlySpan<char>)large).SliceNullTerminatedString()));
				return 1;
			}
		}

		if (panel is RichText richText) {
			Span<char> rich = new char[richText.TextStream.Count * 4 + 1];
			richText.GetText(0, rich);
			lua.PushString(new string(((ReadOnlySpan<char>)rich).SliceNullTerminatedString()));
			return 1;
		}

		Span<char> buffer = stackalloc char[1024];
		buffer[0] = '\0';
		if (panel is TextEntry entry)
			entry.GetText(buffer);
		else if (panel is Label label)
			label.GetText(buffer);
		lua.PushString(new string(((ReadOnlySpan<char>)buffer).SliceNullTerminatedString()));
		return 1;
	}

	[LuaMethod]
	static int Panel__GetTextSize(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		if (panel is Label label) {
			TextImage? textImage = label.GetTextImage();
			if (textImage != null) {
				textImage.GetContentSize(out int wide, out int tall);
				lua.PushNumber(wide);
				lua.PushNumber(tall);
				return 2;
			}
		}
		return 0;
	}

	[LuaMethod]
	static int Panel__DrawFilledRect(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.GetSize(out int wide, out int tall);
		surface.DrawFilledRect(0, 0, wide, tall);
		return 0;
	}

	[LuaMethod]
	static int Panel__DrawOutlinedRect(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.GetSize(out int wide, out int tall);
		surface.DrawOutlinedRect(0, 0, wide, tall);
		return 0;
	}

	[LuaMethod]
	static int Panel__GetValue(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is TextEntry textEntry) {
			int size = textEntry.GetTextLength() * 4 + 1;
			if (size > 1024) {
				Span<char> large = new char[size];
				textEntry.GetText(large);
				lua.PushString(new string(((ReadOnlySpan<char>)large).SliceNullTerminatedString()));
				return 1;
			}
		}

		if (panel is RichText richText) {
			Span<char> rich = new char[richText.TextStream.Count * 4 + 1];
			richText.GetText(0, rich);
			lua.PushString(new string(((ReadOnlySpan<char>)rich).SliceNullTerminatedString()));
			return 1;
		}

		Span<char> buffer = stackalloc char[1024];
		buffer[0] = '\0';
		if (panel is TextEntry entry)
			entry.GetText(buffer);
		else if (panel is Label label)
			label.GetText(buffer);
		lua.PushString(new string(((ReadOnlySpan<char>)buffer).SliceNullTerminatedString()));
		return 1;
	}

	[LuaMethod]
	static int Panel__GetCaretPos(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		lua.PushNumber(panel.GetCaretPos());
		return 1;
	}

	[LuaMethod]
	static int Panel__SetCaretPos(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.SetCaretPos((int)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__MouseCapture(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		if (!lua.GetBool(2)) {
			vguiInput.SetMouseCapture(null);
			return 0;
		}
		vguiInput.SetMouseCapture(panel);
		return 0;
	}

	[LuaMethod]
	static int Panel__SetWrap(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		if (panel is Label label)
			label.SetWrap(lua.GetBool(2));
		return 0;
	}

#if !MENU_DLL
	[LuaMethod]
	static int Panel__SetModel(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		if (panel is SpawnIcon spawnIcon)
			spawnIcon.SetModel(lua.CheckString(2), (int)lua.CheckNumberOpt(3, 0), lua.CheckStringOpt(4, ""));
		return 0;
	}
#endif

	[LuaMethod]
	static int Panel__SetAllowNonAsciiCharacters(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		if (panel is TextEntry textEntry)
			textEntry.SetAllowNonAsciiCharacters(lua.GetBool(2));
		return 0;
	}

	static Color GetTextEntryColor(ILuaObject obj) => new(
		obj.GetMemberInt("r", 255),
		obj.GetMemberInt("g", 255),
		obj.GetMemberInt("b", 0),
		obj.GetMemberInt("a", 255)
	);

	[LuaMethod]
	static int Panel__DrawTextEntryText(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is not TextEntry textEntry)
			return 0;

		ILuaObject? text = lua.GetObject(2);
		if (text == null || !text.isTable())
			return 0;
		Color textColor = GetTextEntryColor(text);

		ILuaObject? highlight = lua.GetObject(3);
		if (highlight == null || !highlight.isTable())
			return 0;
		Color highlightColor = GetTextEntryColor(highlight);

		ILuaObject? cursor = lua.GetObject(4);
		if (cursor == null || !cursor.isTable()) {
			lua.TypeError("table", 4);
			return 0;
		}
		Color cursorColor = GetTextEntryColor(cursor);

		textEntry.DrawText(textColor, highlightColor, cursorColor);
		return 0;
	}

	[LuaMethod]
	static int Panel__SetTextInset(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		if (panel is Label label)
			label.SetTextInset((int)lua.GetNumber(2), (int)lua.GetNumber(3));
		return 0;
	}

	[LuaMethod]
	static int Panel__SizeToContents(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		if (panel is Label label)
			label.SizeToContents();
		return 0;
	}

	[LuaMethod]
	static int Panel__Dock(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.Dock = (Panel.DockType)(int)lua.GetNumber(2);
		panel.InvalidateLayout(false, false);
		return 0;
	}

	[LuaMethod]
	static int Panel__GetDock(Panel panel) => (int)panel.Dock;

	[LuaMethod]
	static int Panel__DockMargin(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.DockMarginLeft = (int)lua.GetNumber(2);
		panel.DockMarginTop = (int)lua.GetNumber(3);
		panel.DockMarginRight = (int)lua.GetNumber(4);
		panel.DockMarginBottom = (int)lua.GetNumber(5);
		return 0;
	}

	[LuaMethod]
	static int Panel__DockPadding(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.DockPaddingLeft = (int)lua.GetNumber(2);
		panel.DockPaddingTop = (int)lua.GetNumber(3);
		panel.DockPaddingRight = (int)lua.GetNumber(4);
		panel.DockPaddingBottom = (int)lua.GetNumber(5);
		return 0;
	}

	[LuaMethod]
	static int Panel__GetDockMargin(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		lua.PushNumber(panel.DockMarginLeft);
		lua.PushNumber(panel.DockMarginTop);
		lua.PushNumber(panel.DockMarginRight);
		lua.PushNumber(panel.DockMarginBottom);
		return 4;
	}

	[LuaMethod]
	static int Panel__GetDockPadding(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		lua.PushNumber(panel.DockPaddingLeft);
		lua.PushNumber(panel.DockPaddingTop);
		lua.PushNumber(panel.DockPaddingRight);
		lua.PushNumber(panel.DockPaddingBottom);
		return 4;
	}

	[LuaMethod]
	static int Panel__SetText(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.SetText(lua.GetString(2));
		return 0;
	}

	[LuaMethod]
	static void Panel__Prepare(Panel panel) => UpdateLuaHooks(panel);

	[LuaMethod]
	static int Panel__SetFGColor(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.SetFgColor(new Color((int)lua.CheckNumber(2), (int)lua.CheckNumber(3), (int)lua.CheckNumber(4), (int)lua.CheckNumber(5)));
		return 0;
	}

	[LuaMethod]
	static int Panel__SetBGColor(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.SetBgColor(new Color((int)lua.CheckNumber(2), (int)lua.CheckNumber(3), (int)lua.CheckNumber(4), (int)lua.CheckNumber(5)));
		return 0;
	}

	[LuaMethod]
	static int Panel__DrawTexturedRect(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		panel.GetSize(out int wide, out int tall);
		surface.DrawTexturedRect(0, 0, wide, tall);
		return 0;
	}

	[LuaMethod]
	static int Panel__NewObject(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null)
			lua.Error("Tried to use a NULL Panel!");
		return 0;
	}

	static string OpenURLBuffer = "";

	[LuaMethod]
	static int Panel__OpenURL(ILuaInterface lua) {
		string url = lua.CheckString(2);
		if (!url.Contains(':')) {
			OpenURLBuffer = $"http://{url}";
			if (OpenURLBuffer.Length > 2047)
				OpenURLBuffer = OpenURLBuffer[..2047];
			url = OpenURLBuffer;
		}

		if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
			if (commandLine.FindParm("-disablehttp") != 0) {
				lua.ErrorFromLua($"Panel:OpenURL external URLs blocked by -disablehttp launch parameter. URL: {url}\n");
				return 0;
			}
		}
		else if (!url.StartsWith("asset://", StringComparison.OrdinalIgnoreCase) && url != "about:blank" && url != "chrome://credits/")
			return 0;

		if (url.Length == 0 || url.Contains("10.") || url.Contains("172.16.") || url.Contains("192.168.") || url.Contains("127.") || url.Contains("://localhost"))
			return 0;

		string trimmed = url.TrimStart().Trim(' ').Replace("\n", "").Replace("\r", "");
		if (trimmed.StartsWith("file", StringComparison.Ordinal))
			return 0;

		ReadOnlySpan<string> blockedExtensions = [".dll", ".swf", ".mov", ".lua", ".mp4", ".exe", ".bat", ".zip", ".mp3"];
		foreach (string extension in blockedExtensions)
			if (trimmed.EndsWith(extension, StringComparison.Ordinal))
				return 0;

		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is HtmlPanel html)
			html.OpenURL(url);
		return 0;
	}

	[LuaMethod]
	static int Panel__HasChildren(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		lua.PushBool(panel.GetChildCount() != 0);
		return 1;
	}

	[LuaMethod]
	static int Panel__NewObjectCallback(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is HtmlPanel html) {
			string funcName = lua.CheckString(3);
			string objName = lua.CheckString(2);
			html.NewObjectCallback(objName, funcName);
		}
		return 0;
	}

	[LuaMethod]
	static int Panel__IsLoading(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is not HtmlPanel html)
			return 0;

		lua.PushBool(html.IsLoading());
		return 1;
	}

	[LuaMethod]
	static int Panel__UpdateHTMLTexture(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is HtmlPanel html)
			html.UpdateHTMLTexture();
		return 0;
	}

#if !MENU_DLL
	[LuaMethod]
	static int Panel__GetHTMLMaterial(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is not HtmlPanel html)
			return 0;

		IMaterial? material = html.GetHTMLMaterial();
		if (material == null)
			return 0;

		LuaMaterial.Push(material);
		return 1;
	}
#endif

	[LuaMethod]
	static int Panel__SetHTML(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is HtmlPanel html)
			html.SetHTML(lua.CheckString(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__Refresh(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is HtmlPanel html)
			html.Refresh();
		return 0;
	}

	[LuaMethod]
	static int Panel__StopLoading(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is HtmlPanel html)
			html.StopLoading();
		return 0;
	}

	[LuaMethod]
	static int Panel__GoBack(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is HtmlPanel html)
			html.GoBack();
		return 0;
	}

	[LuaMethod]
	static int Panel__GoForward(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is HtmlPanel html)
			html.GoForward();
		return 0;
	}

	[LuaMethod]
	static int Panel__GoToHistoryOffset(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is HtmlPanel)
			Msg("TODO: HTMLPanel.GoToHistoryOffset is NYI.\n");
		return 0;
	}

	static int RunJavascript(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is HtmlPanel html)
			html.RunJavascript(lua.CheckString(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__Exec(ILuaInterface lua) => RunJavascript(lua);

	[LuaMethod]
	static int Panel__RunJavascript(ILuaInterface lua) => RunJavascript(lua);

	static readonly int[] ContentAlignmentToNumpad = [7, 8, 9, 4, 5, 6, 1, 2, 3];

	[LuaMethod]
	static int Panel__GetContentAlignment(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is not Label label)
			return 0;

		uint alignment = (uint)label.GetContentAlignment();
		lua.PushNumber(alignment < 9 ? ContentAlignmentToNumpad[alignment] : 4);
		return 1;
	}

	[LuaMethod]
	static int Panel__GetName(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}
		lua.PushString(panel.GetName());
		return 1;
	}

	[LuaMethod]
	static int Panel__SetExpensiveShadow(ILuaInterface lua) {
		Panel? panel = (Panel?)PanelClass.Get(1);
		if (panel == null) {
			lua.Error("Tried to use a NULL Panel!");
			return 0;
		}

		if (panel is not Label label)
			return 0;

		Color color;
		ILuaObject? obj = lua.GetObject(3);
		if (obj == null || !obj.isTable())
			color = new(0, 0, 0, 180);
		else {
			int a = obj.GetMemberInt("a", 255);
			int b = obj.GetMemberInt("b", 0);
			int g = obj.GetMemberInt("g", 0);
			int r = obj.GetMemberInt("r", 0);
			color = new(r, g, b, a);
		}

		label.ExpensiveShadowDistance = (int)lua.CheckNumber(2);
		label.ExpensiveShadowColor = color;
		return 0;
	}

	[LuaMethod]
	static void Panel__RequestFocus(Panel panel) => panel.RequestFocus(0);

	[LuaMethod]
	static void Panel__SetPaintedManually(Panel panel, [LuaGet] bool state) => panel.SetPaintedManually(state);

	[LuaMethod]
	static void Panel__SetPaintBorderEnabled(Panel panel, [LuaGet] bool state) => panel.SetPaintBorderEnabled(state);

	[LuaMethod]
	static void Panel__SetPaintBackgroundEnabled(Panel panel, [LuaGet] bool state) => panel.SetPaintBackgroundEnabled(state);

	[LuaMethod]
	static void Panel__SetVerticalScrollbarEnabled(Panel panel) {
		if (panel is RichText richText)
			richText.SetVerticalScrollbar(g_Lua!.GetBool(2));
		if (panel is TextEntry textEntry)
			textEntry.SetVerticalScrollbar(g_Lua!.GetBool(2));
	}

	[LuaMethod]
	static void Panel__MoveToFront(Panel panel) => panel.MoveToFront();

	[LuaMethod]
	static void Panel__MoveToBack(Panel panel) => panel.MoveToBack();

	[LuaMethod]
	static void Panel__SetFocusTopLevel(Panel panel, [LuaGet] bool state) {
		if (panel is EditablePanel editable)
			editable.GetFocusNavGroup().SetFocusTopLevel(state);
	}

	[LuaMethod]
	static void Panel__SetRenderInScreenshots(Panel panel, [LuaGet] bool state) => panel.SetRenderInScreenshots(state);

	[LuaMethod]
	static void Panel__SetTabPosition(Panel panel, [LuaGet] int position) => panel.SetTabPosition(position);

	[LuaMethod]
	static void Panel__SetAlpha(Panel panel, [LuaGet] int alpha) => panel.SetAlpha(alpha);

	[LuaMethod]
	static int Panel__GetAlpha(Panel panel) => panel.GetAlpha();

	[LuaMethod]
	static void Panel__SetDrawOnTop(Panel panel, [LuaGet] bool state) => panel.SetDrawOnTop(state);

	[LuaMethod]
	static void Panel__NoClipping(Panel panel, [LuaGet] bool state) => panel.SetNoClipping(state);

	[LuaMethod]
	static bool Panel__HasParent(Panel panel, Panel parent) => panel.HasParent(parent);

	[LuaMethod]
	static int Panel__ChildCount(Panel panel) => panel.GetChildCount();

	[LuaMethod]
	static bool Panel__IsKeyboardInputEnabled(Panel panel) => panel.IsKeyboardInputEnabled();

	[LuaMethod]
	static bool Panel__IsMouseInputEnabled(Panel panel) => panel.IsMouseInputEnabled();

	[LuaMethod]
	static void Panel__SetWorldClicker(Panel panel, [LuaGet] bool state) => panel.SetWorldClicker(state);

	[LuaMethod]
	static bool Panel__IsWorldClicker(Panel panel) => panel.IsWorldClicker();

	[LuaMethod]
	static bool Panel__IsPopup(Panel panel) => panel.IsPopup();

	[LuaMethod]
	static bool Panel__IsModal(Panel panel) {
		IPanel? modal = vguiInput.GetAppModalSurface();
		if (modal == null)
			return false;
		return modal == panel;
	}

	[LuaMethod]
	static void Panel__SetFontInternal(ILuaInterface lua, Panel panel) {
		IFont? font = LuaFonts.GetFont(lua.GetString(2));
		if (font == null) {
#if MENU_DLL
			font = MenuPanels.GetMenuPanel()!.GetScheme()!.GetFont(lua.GetString(2), false);
#else
			font = GModBase.GetGModBasePanel(true)!.GetScheme()!.GetFont(lua.GetString(2), false);
#endif
			if (font == null) {
				lua.ErrorNoHalt($"SetFontInternal: font doesn't exist ({lua.GetString(2)})\n");
				return;
			}
		}

		if (panel is Label label)
			label.SetFont(font);
		if (panel is TextEntry textEntry)
			textEntry.SetFont(font);
		if (panel is RichText richText)
			richText.SetFont(font);
	}

	[LuaMethod]
	static (int, int) Panel__LocalToScreen(Panel panel, [LuaGet] int x, [LuaGet] int y) {
		panel.LocalToScreen(ref x, ref y);
		return (x, y);
	}

	[LuaMethod]
	static (int, int) Panel__ScreenToLocal(Panel panel, [LuaGet] int x, [LuaGet] int y) {
		panel.ScreenToLocal(ref x, ref y);
		return (x, y);
	}

	[LuaMethod]
	static (int, int) Panel__CursorPos(Panel panel) {
		vguiInput.GetCursorPos(out int x, out int y);
		if (engine != null && !engine.IsActiveApp()) {
			x = 0;
			y = 0;
		}
		panel.ScreenToLocal(ref x, ref y);
		return (x, y);
	}

	[LuaMethod]
	static void Panel__SetCursor(Panel panel, [LuaGet] string? name) {
		if (stricmp(name, "hand") == 0)
			panel.SetCursor(CursorCode.Hand);
		else if (stricmp(name, "no") == 0)
			panel.SetCursor(CursorCode.No);
		else if (stricmp(name, "blank") == 0)
			panel.SetCursor(CursorCode.Blank);
		else if (stricmp(name, "sizenwse") == 0)
			panel.SetCursor(CursorCode.SizeNWSE);
		else if (stricmp(name, "sizenesw") == 0)
			panel.SetCursor(CursorCode.SizeNESW);
		else if (stricmp(name, "sizewe") == 0)
			panel.SetCursor(CursorCode.SizeWE);
		else if (stricmp(name, "sizens") == 0)
			panel.SetCursor(CursorCode.SizeNS);
		else if (stricmp(name, "sizeall") == 0)
			panel.SetCursor(CursorCode.SizeAll);
		else if (stricmp(name, "arrow") == 0)
			panel.SetCursor(CursorCode.Arrow);
		else if (stricmp(name, "beam") == 0)
			panel.SetCursor(CursorCode.IBeam);
		else if (stricmp(name, "hourglass") == 0)
			panel.SetCursor(CursorCode.Hourglass);
		else if (stricmp(name, "waitarrow") == 0)
			panel.SetCursor(CursorCode.WaitArrow);
		else if (stricmp(name, "crosshair") == 0)
			panel.SetCursor(CursorCode.Crosshair);
		else
			panel.SetCursor(stricmp(name, "up") == 0 ? CursorCode.Up : CursorCode.None);
	}

	static Panel? GetPanelOrError(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel == null)
			lua.Error("Tried to use a NULL Panel!");
		return panel;
	}

	[LuaMethod]
	static int Panel____gc(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel != null) {
			lua.ReleaseUserTypeObject(panel);
			panel.LuaHandle = false;
		}
		lua.SetUserType(1, 0);
		return 0;
	}

	[LuaMethod]
	static int Panel____eq(ILuaInterface lua) {
		lua.PushBool(Get_Panel(1) == Get_Panel(2));
		return 1;
	}

	[LuaMethod]
	static int Panel__DoModal(ILuaInterface lua) {
		GetPanelOrError(lua)?.DoModal();
		return 0;
	}

	[LuaMethod]
	static int Panel__LoadControlsFromString(ILuaInterface lua) {
		lua.ErrorFromLua("Panel:LoadControlsFromString is deprecated. Do not use it.");
		return 0;
	}

	[LuaMethod]
	static int Panel__LoadControlsFromFile(ILuaInterface lua) {
		lua.ErrorFromLua("Panel:LoadControlsFromFile is deprecated. Do not use it.");
		return 0;
	}

	[LuaMethod]
	static int Panel__GetFGColor(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel == null)
			return 0;
		lua.PushColor(panel.GetFgColor());
		return 1;
	}

	[LuaMethod]
	static int Panel__GetBGColor(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel == null)
			return 0;
		lua.PushColor(panel.GetBgColor());
		return 1;
	}

	[LuaMethod]
	static int Panel__KillFocus(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel != null && panel.HasFocus())
			panel.OnKillFocus(null);
		return 0;
	}

	[LuaMethod]
	static int Panel__FocusNext(ILuaInterface lua) {
		GetPanelOrError(lua)?.RequestFocusNext();
		return 0;
	}

	[LuaMethod]
	static int Panel__FocusPrevious(ILuaInterface lua) {
		GetPanelOrError(lua)?.RequestFocusPrev();
		return 0;
	}

	[LuaMethod]
	static int Panel__GetBounds(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel == null)
			return 0;
		panel.GetBounds(out int x, out int y, out int w, out int h);
		lua.PushNumber(x);
		lua.PushNumber(y);
		lua.PushNumber(w);
		lua.PushNumber(h);
		return 4;
	}

	[LuaMethod]
	static int Panel__SetAchievement(ILuaInterface lua) {
		GetPanelOrError(lua);
		return 0;
	}

	[LuaMethod]
	static int Panel__LoadTGAImage(ILuaInterface lua) {
		GetPanelOrError(lua);
		return 0;
	}

	[LuaMethod]
	static int Panel__SetURL(ILuaInterface lua) {
		if (GetPanelOrError(lua) is URLLabel label)
			label.SetURL(lua.GetString(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__Find(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel == null)
			return 0;
		Panel? child = panel.FindChildByName(lua.CheckString(2), false);
		if (child == null || !child.LuaPanel)
			return 0;
		Push_Panel(child);
		return 1;
	}

	[LuaMethod]
	static int Panel__InsertClickableTextStart(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.InsertClickableTextStart(lua.GetString(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__InsertClickableTextEnd(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.InsertClickableTextEnd();
		return 0;
	}

	[LuaMethod]
	static int Panel__InsertFade(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.InsertFade((float)lua.GetNumber(2), (float)lua.GetNumber(3));
		return 0;
	}

	[LuaMethod]
	static int Panel__ResetAllFades(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.ResetAllFades(lua.GetBool(2), lua.GetBool(3), lua.GetNumber(4));
		return 0;
	}

	[LuaMethod]
	static int Panel__SetToFullHeight(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.SetToFullHeight();
		return 0;
	}

	[LuaMethod]
	static int Panel__InsertColorChange(ILuaInterface lua) {
		if (GetPanelOrError(lua) is not RichText richText)
			return 0;
		int a = (int)lua.CheckNumber(5);
		int b = (int)lua.CheckNumber(4);
		int g = (int)lua.CheckNumber(3);
		int r = (int)lua.CheckNumber(2);
		richText.InsertColorChange(new Color((byte)r, (byte)g, (byte)b, (byte)a));
		return 0;
	}

	[LuaMethod]
	static int Panel__SetTextSelectionColors(ILuaInterface lua) {
		if (GetPanelOrError(lua) is TextEntry textEntry) {
			textEntry.SetSelectionTextColor(lua.GetColor(2));
			textEntry.SetSelectionBgColor(lua.GetColor(3));
		}
		return 0;
	}

	[LuaMethod]
	static int Panel__AppendText(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.InsertString(lua.GetString(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__GotoTextStart(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.GotoTextStart();
		return 0;
	}

	[LuaMethod]
	static int Panel__GotoTextEnd(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.GotoTextEnd();
		return 0;
	}

	[LuaMethod]
	static int Panel__GetNumLines(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel is RichText richText) {
			lua.PushNumber(richText.GetNumLines());
			return 1;
		}
		if (panel is TextEntry textEntry) {
			lua.PushNumber(textEntry.GetNumLines());
			return 1;
		}
		return 0;
	}

	[LuaMethod]
	static int Panel__GetLineHeight(ILuaInterface lua) {
		if (GetPanelOrError(lua) is not RichText richText)
			return 0;
		lua.PushNumber(richText.GetLineHeight());
		return 1;
	}

	[LuaMethod]
	static int Panel__SetLineHeight(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.LineHeight = (short)(int)lua.GetNumber(2);
		return 0;
	}

	[LuaMethod]
	static int Panel__AppendTextWithURLs(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.InsertPossibleURLString(lua.GetString(2), lua.GetColor(3), lua.GetColor(4));
		return 0;
	}

	[LuaMethod]
	static int Panel__SetMaximumCharCount(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel is TextEntry textEntry)
			textEntry.SetMaximumCharCount((int)lua.GetNumber(2));
		else if (panel is RichText richText)
			richText.SetMaximumCharCount((int)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__GetMaximumCharCount(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel is TextEntry textEntry) {
			lua.PushNumber(textEntry.GetMaximumCharCount());
			return 1;
		}
		if (panel is RichText richText) {
			lua.PushNumber(richText.GetMaximumCharCount());
			return 1;
		}
		return 0;
	}

	[LuaMethod]
	static int Panel__GetSelectedTextRange(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel is TextEntry textEntry) {
			textEntry.GetSelectedRange(out int start, out int end);
			lua.PushNumber(start);
			lua.PushNumber(end);
			return 2;
		}
		if (panel is RichText richText) {
			richText.GetSelectedRange(out int start, out int end);
			lua.PushNumber(start);
			lua.PushNumber(end);
			return 2;
		}
		return 0;
	}

	[LuaMethod]
	static int Panel__SetUnderlineFont(ILuaInterface lua) {
		if (GetPanelOrError(lua) is RichText richText)
			richText.SetUnderlineFont(LuaFonts.GetFont(lua.GetString(2)));
		return 0;
	}

	[LuaMethod]
	static int Panel__GetFont(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		IFont? font = panel switch {
			Label label => label.GetFont(),
			TextEntry textEntry => textEntry.GetFont(),
			RichText richText => richText.GetFont(),
			_ => null
		};
		if (font == null)
			return 0;
		lua.PushString(Surface.GetFontName(font));
		return 1;
	}

	[LuaMethod]
	static int Panel__GetTextInset(ILuaInterface lua) {
		if (GetPanelOrError(lua) is not Label label)
			return 0;
		label.GetTextInset(out int x, out int y);
		lua.PushNumber(x);
		lua.PushNumber(y);
		return 2;
	}

	[LuaMethod]
	static int Panel__SetDrawLanguageIDAtLeft(ILuaInterface lua) {
		if (GetPanelOrError(lua) is TextEntry textEntry)
			textEntry.SetDrawLanguageIDAtLeft(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__SetDrawLanguageID(ILuaInterface lua) {
		if (GetPanelOrError(lua) is TextEntry textEntry)
			textEntry.SetDrawLanguageID(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__SelectAllOnFocus(ILuaInterface lua) {
		if (GetPanelOrError(lua) is TextEntry textEntry)
			textEntry.SelectAllOnFocusAlways(true);
		return 0;
	}

	[LuaMethod]
	static int Panel__SelectAll(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel is TextEntry textEntry)
			textEntry.SelectAllText(false);
		else if (panel is RichText richText)
			richText.SelectAllText();
		return 0;
	}

	[LuaMethod]
	static int Panel__SelectAllText(ILuaInterface lua) {
		if (GetPanelOrError(lua) is TextEntry textEntry)
			textEntry.SelectAllText(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__SelectNone(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel is TextEntry textEntry)
			textEntry.SelectNone();
		else if (panel is RichText richText)
			richText.SelectNone();
		return 0;
	}

	[LuaMethod]
	static int Panel__CutSelected(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel is TextEntry textEntry)
			textEntry.CutSelected();
		else if (panel is RichText richText)
			richText.CutSelected();
		return 0;
	}

	[LuaMethod]
	static int Panel__CopySelected(ILuaInterface lua) {
		Panel? panel = GetPanelOrError(lua);
		if (panel is TextEntry textEntry)
			textEntry.CopySelected();
		else if (panel is RichText richText)
			richText.CopySelected();
		return 0;
	}

	[LuaMethod]
	static int Panel__Paste(ILuaInterface lua) {
		if (GetPanelOrError(lua) is TextEntry textEntry)
			textEntry.Paste();
		return 0;
	}

	[LuaMethod]
	static int Panel__Undo(ILuaInterface lua) {
		if (GetPanelOrError(lua) is TextEntry textEntry)
			textEntry.Undo();
		return 0;
	}

	[LuaMethod]
	static int Panel__SaveUndoState(ILuaInterface lua) {
		if (GetPanelOrError(lua) is TextEntry textEntry)
			textEntry.SaveUndoState();
		return 0;
	}

	[LuaMethod]
	static int Panel__IsMultiline(ILuaInterface lua) {
		if (GetPanelOrError(lua) is not TextEntry textEntry)
			return 0;
		lua.PushBool(textEntry.IsMultiline());
		return 1;
	}

	[LuaMethod]
	static int Panel__GetScrollStartIndexes(ILuaInterface lua) {
		if (GetPanelOrError(lua) is not TextEntry textEntry)
			return 0;
		lua.PushNumber(textEntry.GetCurrentStartIndex());
		lua.PushNumber(textEntry.GetCurrentStartLine());
		return 2;
	}

	[LuaMethod]
	static int Panel__SetTextHidden(ILuaInterface lua) {
		if (GetPanelOrError(lua) is TextEntry textEntry)
			textEntry.SetTextHidden(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__SetPopupStayAtBack(ILuaInterface lua) {
		GetPanelOrError(lua)?.SetPopupStayAtBack(lua.GetBool(2));
		return 0;
	}

	[LuaMethod]
	static int Panel__AddText(ILuaInterface lua) => 0;

#if MENU_DLL
	[LuaMethod]
	static int Panel__SetOpenLinksExternally(ILuaInterface lua) {
		if (GetPanelOrError(lua) is HtmlPanel html)
			html.SetOpenLinksExternally(lua.GetBool(2));
		return 0;
	}
#endif
}
