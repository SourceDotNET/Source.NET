using Source.Common.GarrysMod.Lua;
using Source.Common.Input;

namespace Game.Client.GarrysMod;

public static partial class LuaInput
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_input = new("input");

	[LuaFunction]
	static int IsButtonDown(ILuaInterface lua) {
		int code = (int)lua.CheckNumber(1);
		if ((uint)code < 1042) {
			lua.PushBool(inputsystem.IsButtonDown((ButtonCode)code));
			return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int GetAnalogValue(ILuaInterface lua) {
		int code = (int)lua.CheckNumber(1);
		if ((uint)code < 28) {
			lua.PushNumber(inputsystem.GetAnalogValue((AnalogCode)code));
			return 1;
		}
		lua.PushNumber(0);
		return 1;
	}

	[LuaFunction]
	static int IsMouseDown(ILuaInterface lua) {
		ButtonCode code = (ButtonCode)(int)lua.CheckNumber(1);
		if (code >= ButtonCode.MouseFirst && code <= ButtonCode.MouseLast) {
			lua.PushBool(vguiInput.IsMouseDown(code));
			return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int SetCursorPos(ILuaInterface lua) {
		if (engine != null && !engine.IsActiveApp())
			return 0;
		if (enginevgui.IsGameUIVisible())
			return 0;

		int y = (int)lua.CheckNumber(2);
		int x = (int)lua.CheckNumber(1);
		vguiInput.SetCursorPos(x, y);
		return 0;
	}

	[LuaFunction]
	static int GetCursorPos(ILuaInterface lua) {
		if (engine != null && !engine.IsActiveApp()) {
			lua.PushNumber(0);
			lua.PushNumber(0);
			return 2;
		}

		vguiInput.GetCursorPos(out int x, out int y);
		lua.PushNumber(x);
		lua.PushNumber(y);
		return 2;
	}

	[LuaFunction]
	static int WasMousePressed(ILuaInterface lua) {
		int code = (int)lua.CheckNumber(1);
		if ((uint)(code - 107) < 7) {
			lua.PushBool(vguiInput.WasMousePressed((ButtonCode)code));
			return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int WasMouseReleased(ILuaInterface lua) {
		int code = (int)lua.CheckNumber(1);
		if ((uint)(code - 107) < 7) {
			lua.PushBool(vguiInput.WasMouseReleased((ButtonCode)code));
			return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int WasMouseDoublePressed(ILuaInterface lua) {
		int code = (int)lua.CheckNumber(1);
		if ((uint)(code - 107) < 7) {
			lua.PushBool(vguiInput.WasMouseDoublePressed((ButtonCode)code));
			return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int WasKeyPressed(ILuaInterface lua) {
		int code = (int)lua.CheckNumber(1);
		if ((uint)code < 107) {
			lua.PushBool(vguiInput.WasKeyPressed((ButtonCode)code));
			return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int IsKeyDown(ILuaInterface lua) {
		int code = (int)lua.CheckNumber(1);
		if ((uint)code < 107) {
			lua.PushBool(vguiInput.IsKeyDown((ButtonCode)code));
			return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int IsShiftDown(ILuaInterface lua) {
		lua.PushBool(vguiInput.IsKeyDown(ButtonCode.KeyLShift) || vguiInput.IsKeyDown(ButtonCode.KeyRShift));
		return 1;
	}
	[LuaFunction]
	static int IsControlDown(ILuaInterface lua) {
		lua.PushBool(vguiInput.IsKeyDown(ButtonCode.KeyLControl) || vguiInput.IsKeyDown(ButtonCode.KeyRControl));
		return 1;
	}
	[LuaFunction]
	static int WasKeyTyped(ILuaInterface lua) {
		int code = (int)lua.CheckNumber(1);
		if ((uint)code < 107) {
			lua.PushBool(vguiInput.WasKeyTyped((ButtonCode)code));
			return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int WasKeyReleased(ILuaInterface lua) {
		int code = (int)lua.CheckNumber(1);
		if ((uint)code < 107) {
			lua.PushBool(vguiInput.WasKeyReleased((ButtonCode)code));
			return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	[LuaFunction]
	static int GetKeyName(ILuaInterface lua) {
		int code = (int)lua.CheckNumber(1);
		if ((uint)(code - 1) < 1041) {
			lua.PushString(inputsystem.ButtonCodeToString((ButtonCode)code));
			return 1;
		}
		return 0;
	}

	[LuaFunction]
	static int GetKeyCode(ILuaInterface lua) {
		lua.PushNumber((int)inputsystem.StringToButtonCode(lua.CheckString(1)));
		return 1;
	}

	static bool KeyTrapping;

	[LuaFunction]
	static int StartKeyTrapping(ILuaInterface lua) {
		engine.StartKeyTrapMode();
		KeyTrapping = true;
		return 0;
	}

	[LuaFunction]
	static int IsKeyTrapping(ILuaInterface lua) {
		lua.PushBool(KeyTrapping);
		return 1;
	}

	[LuaFunction]
	static int CheckKeyTrapping(ILuaInterface lua) {
		if (engine.CheckDoneKeyTrapping(out ButtonCode code)) {
			lua.PushNumber((int)code);
			KeyTrapping = false;
			return 1;
		}
		return 0;
	}

	[LuaFunction]
	static int LookupBinding(ILuaInterface lua) {
		string binding = lua.CheckString(1);
		ReadOnlySpan<char> key;
		if (lua.GetType(2) != LuaType.Nil && lua.GetBool(2))
			key = engine.Key_LookupBindingExact(binding);
		else
			key = engine.Key_LookupBinding(binding);

		if (key.IsEmpty)
			return 0;
		lua.PushString(key);
		return 1;
	}
	[LuaFunction]
	static int LookupKeyBinding(ILuaInterface lua) {
		ReadOnlySpan<char> binding = engine.Key_BindingForKey((ButtonCode)(int)lua.CheckNumber(1));
		if (binding.IsEmpty)
			return 0;
		lua.PushString(binding);
		return 1;
	}

	[LuaFunction]
	static int TranslateAlias(ILuaInterface lua) {
		ReadOnlySpan<char> alias = engine.GMOD_TranslateAlias(lua.CheckString(1));
		if (alias.IsEmpty)
			return 0;
		lua.PushString(alias);
		return 1;
	}

	static void SelectWeapon(BaseEntity? ent) {
		if (ent == null || !ent.IsBaseCombatWeapon()) {
			g_Lua!.Error("Weapon is NULL/Not a Weapon");
			ent = null;
		}
		input.MakeWeaponSelection((BaseCombatWeapon?)ent);
	}

	[LuaFunction]
	static int SelectWeapon(ILuaInterface lua) {
		SelectWeapon(LuaEntity.Get_Entity(1, false));
		return 0;
	}
}
