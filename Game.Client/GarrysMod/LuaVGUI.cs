using Source.Common.GarrysMod.Lua;
using Source.Common.GUI;
using Source.GUI.Controls;

namespace Game.Client.GarrysMod;

public static partial class LuaVGUI
{
	[LuaClass(typeof(Panel), NullError = "Tried to use a NULL Panel!")]
	public static readonly LuaClass PanelClass = new("Panel", LuaType.Panel, null, null);

	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_vgui = new("vgui");

	static void AddChildren(List<IPanel> panels, IPanel panel) {
		panels.Add(panel);
		int count = panel.GetChildCount();
		for (int i = 0; i < count; i++)
			AddChildren(panels, panel.GetChild(i));
	}

	[LuaFunction]
	static int GetAll(ILuaInterface lua) {
		IPanel root = surface.GetEmbeddedPanel();
		List<IPanel> panels = [root];
		int count = root.GetChildCount();
		for (int i = 0; i < count; i++)
			AddChildren(panels, root.GetChild(i));

		lua.PreCreateTable(panels.Count, 0);
		int n = 0;
		foreach (IPanel vpanel in panels) {
			if (vpanel is not Panel panel || !panel.LuaPanel)
				continue;
			lua.PushNumber(++n);
			Push_Panel(panel);
			lua.SetTable(-3);
		}
		return 1;
	}

	[LuaFunction]
	static int CursorVisible(ILuaInterface lua) {
		lua.PushBool(surface.IsCursorVisible());
		return 1;
	}

	[LuaFunction]
	static int IsHoveringWorld(ILuaInterface lua) {
		if (!surface.IsCursorVisible()) {
			lua.PushBool(false);
			return 1;
		}
		Panel? world = GModBase.GetGModBasePanel(true);
		lua.PushBool(vguiInput.GetMouseOver() == world);
		return 1;
	}

	[LuaFunction]
	static int GetWorldPanel(ILuaInterface lua) {
		Push_Panel(GModBase.GetGModBasePanel(true));
		return 1;
	}

	[LuaFunction]
	static int FocusedHasParent(ILuaInterface lua) {
		Panel? panel = Get_Panel(1);
		if (panel == null)
			return 0;

		IPanel? focus = vguiInput.GetFocus();
		if (focus != null && focus.HasParent(panel)) {
			lua.PushBool(true);
			return 1;
		}

		lua.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int GetKeyboardFocus(ILuaInterface lua) {
		IPanel? focus = vguiInput.GetFocus();
		if (focus is Panel panel) {
			Push_Panel(panel);
			return 1;
		}
		return 0;
	}
	[LuaFunction]
	static int GetHoveredPanel(ILuaInterface lua) {
		IPanel? hovered = vguiInput.GetMouseOver();
		if (hovered is Panel panel) {
			Push_Panel(panel);
			return 1;
		}
		return 0;
	}

	public static ILuaObject? GetLuaTable(Panel panel) {
		ILuaObject? table = panel.LuaTable;
		if (table == null && g_Lua != null) {
			if (panel.LuaObject != null && panel.LuaObject.GetType() != LuaType.Panel) {
				panel.LuaObject.UnReference();
				panel.LuaObject = null;
			}

			LuaObject newTable = new();
			newTable.Set(g_Lua.GetNewTable());
			panel.LuaTable = newTable;
			Push_Panel(panel);
			panel.LuaTable.SetMember("Panel", g_Lua.GetObject(-1));
			return panel.LuaTable;
		}
		return table;
	}

	public static void Push_Panel(Panel? panel) {
		if (panel != null)
			panel.PushLua(g_Lua!, PanelClass.Type);
		else
			g_Lua!.PushNil();
	}

	public static Panel? Get_Panel(int stackPos) {
		return (Panel?)PanelClass.Get(stackPos);
	}

	public static bool IsValidPanel(Panel? panel) {
		if (panel != null && panel != GModBase.GetGModBasePanel(true) && panel != HudGMod.g_HudGMod && panel != GModBase.GetGModParentToHUDPanel())
			return panel.LuaPanel && !panel.IsMarkedForDeletion();
		return true;
	}

	public static Panel? CreateControl(ReadOnlySpan<char> className) {
		if (stricmp(className, "Awesomium") == 0 || stricmp(className, "Chromium") == 0)
			className = "HTML";

		if (stricmp(className, "Frame") == 0) {
			Frame frame = new(null, "Frame", true, true);
			frame.SetBuildModeEditable(true);
			return frame;
		}

		if (stricmp(className, "EditablePanel") == 0)
			return new LuaEditablePanel(null, "EditablePanel");

		if (stricmp(className, "Panel") == 0 || stricmp(className, "Divider") == 0)
			return new Panel(null, "Panel");

		if (stricmp(className, "EditablePanel") == 0)
			return new EditablePanel(null, null);

		if (stricmp(className, "Label") == 0)
			return new Label(null, null, "Label");

		if (stricmp(className, "URLLabel") == 0)
			return new URLLabel(null, null, "URLLabel", null);

		if (stricmp(className, "Button") == 0)
			return new Button(null, null, "Button");

		if (stricmp(className, "TextEntry") == 0) {
			TextEntry textEntry = new(null, null);
			textEntry.SendNewLine(true);
			return textEntry;
		}

		if (stricmp(className, "RichText") == 0)
			return new RichText(null, "RichText");

		if (stricmp(className, "TGAImage") == 0)
			return null; // TGAImagePanel

		if (stricmp(className, "AchievementIcon") == 0)
			return null; // AchievementIcon

		if (stricmp(className, "ModelImage") == 0)
			return new SpawnIcon(null, "ModelImage");

		if (stricmp(className, "AvatarImage") == 0)
			return null; // AvatarImage

		if (stricmp(className, "HTML") == 0)
			return new HtmlPanel(null, "HtmlPanel");

		return null;
	}

	[LuaFunction]
	static int Create(ILuaInterface lua) {
		string className = lua.CheckString(1);
		Panel? panel = CreateControl(className);
		if (panel == null) {
			lua.ErrorFromLua($"vgui.Create failed to create the VGUI component ({className})");
			return 0;
		}

		panel.LuaPanel = true;
		panel.SetAutoDelete(true);

		if (lua.GetType(2) == LuaType.Panel) {
			Panel? parent = Get_Panel(2);
			if (IsValidPanel(parent))
				panel.SetParent(parent);
		}
		else {
			if (lua.GetType(2) != LuaType.Nil)
				lua.ErrorFromLua($"bad argument #2 to 'Create' (Panel expected, got {lua.GetActualTypeName(2)})");
			panel.SetParent(GModBase.GetGModBasePanel(true));
		}

		if (lua.GetType(3) == LuaType.String)
			panel.SetName(lua.CheckString(3));
		else if (lua.GetType(3) != LuaType.Nil)
			lua.ErrorFromLua($"bad argument #3 to 'Create' (string expected, got {lua.GetActualTypeName(3)})");

		Push_Panel(panel);
		panel.InvalidateLayout(false, false);
		return 1;
	}
}
