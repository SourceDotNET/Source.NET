#if CLIENT_DLL || GAME_DLL
using Source;
using Source.Common.Commands;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;
#if CLIENT_DLL
using Source.Common.Launcher;
#endif

using System.Globalization;
using System.Numerics;
using System.Text;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaGlobalFunctions
{
	public static Color HSVToColor(float hue, float saturation, float value) {
		float h = (float)(hue % 360.0);
		float v = value * 255.0f;
		double chroma = (double)(255.0f * saturation) * v / 255.0;
		double m = v - chroma;
		double hd = h;
		float r, g, b;

		if (h > 300.0f) {
			r = (int)v;
			g = (int)m;
			b = (int)-(((hd - 360.0) / 60.0) * chroma - m);
		}
		else if (h > 60.0f) {
			if (h >= 180.0f) {
				b = (int)v;
				if (h < 240.0f) {
					r = (int)m;
					g = (int)(m - (hd / 60.0 - 4.0) * chroma);
				}
				else {
					g = (int)m;
					r = (int)((hd / 60.0 - 4.0) * chroma + m);
				}
			}
			else {
				g = (int)v;
				if (h >= 120.0f) {
					r = (int)m;
					b = (int)((hd / 60.0 - 2.0) * chroma + m);
				}
				else {
					b = (int)m;
					r = (int)(m - (hd / 60.0 - 2.0) * chroma);
				}
			}
		}
		else {
			r = (int)v;
			b = (int)m;
			g = (int)((hd / 60.0) * chroma + m);
		}

		return new Color((byte)(int)r, (byte)(int)g, (byte)(int)b, 255);
	}

	public static void RGBtoHSV(int r, int g, int b, out float h, out float s, out float v) {
		int min = Math.Min(r, Math.Min(g, b));
		double max;
		if (Math.Max(g, b) < r)
			max = r;
		else if (b < g)
			max = g;
		else
			max = b;

		int maxInt = (int)max;
		double delta = max - min;

		if (delta == 0.0) {
			h = 0.0f;
			s = 0.0f;
		}
		else {
			double sat = 255.0 * (delta / max);
			double hh;
			if (r == maxInt)
				hh = (g - b) / delta;
			else if (g == maxInt)
				hh = (b - r) / delta + 2.0;
			else
				hh = (r - g) / delta + 4.0;
			hh *= 60.0;
			if (hh < 0.0)
				hh += 360.0;
			h = hh != 360.0 ? (float)hh : 0.0f;
			s = (int)sat / 255.0f;
		}
		v = maxInt / 255.0f;
	}

	[LuaGlobal]
	static int HSVToColor(ILuaInterface lua) {
		float value = (float)lua.CheckNumber(3);
		float saturation = (float)lua.CheckNumber(2);
		float hue = (float)lua.CheckNumber(1);
		lua.PushColor(HSVToColor(hue, saturation, value));
		return 1;
	}

	[LuaGlobal]
	static int ColorToHSV(ILuaInterface lua) {
		LuaTable color = new(null, 0);
		color.SetFromStack(1);
		if (!color.isTable()) {
			lua.TypeError("table", 1);
			color.UnReference();
			return 0;
		}
		int b = (int)color.GetMemberFloat("b", 255.0f);
		int g = (int)color.GetMemberFloat("g", 255.0f);
		int r = (int)color.GetMemberFloat("r", 255.0f);
		RGBtoHSV(r, g, b, out float h, out float s, out float v);
		lua.PushNumber(h);
		lua.PushNumber(s);
		lua.PushNumber(v);
		color.UnReference();
		return 3;
	}

	[LuaGlobal]
	static int include(ILuaInterface lua) {
		string file = g_Lua!.CheckString(1).ToString();
		Bootil.String.Lower(ref file);
		g_Lua.GetCurrentFile(out string current);
		int top = g_Lua.Top();
		g_Lua.FindAndRunScript(file, true, true, current, false);
		return g_Lua.Top() - top;
	}

	[LuaGlobal]
	static int CompileString(ILuaInterface lua) {
		string code = g_Lua!.CheckString(1);
		string identifier = g_Lua.CheckStringOpt(2, "CompileString");
		if (g_Lua.GetType(3) == LuaType.Nil) {
			if (!g_Lua.RunStringEx(identifier, "", code, false, true, true, true))
				return 0;
		}
		else {
			bool handleError = g_Lua.GetBool(3);
			if (!g_Lua.RunStringEx(identifier, "", code, false, handleError, handleError, true) && handleError)
				return 0;
		}
		return 1;
	}

	[LuaGlobal]
	static int ProtectedCall(ILuaInterface lua) {
		LuaObject func = new(1, LuaType.None);
		if (!func.isFunction()) {
			g_Lua!.TypeError("function", 1);
			func.UnReference();
			return 0;
		}

		int top = g_Lua!.Top();
		List<LuaObject> args = [];
		for (int i = 2; i <= top; i++)
			args.Add(new LuaObject(i, LuaType.None));

		func.Push();
		foreach (LuaObject arg in args)
			arg.Push();
		foreach (LuaObject arg in args)
			arg.UnReference();
		args.Clear();

		g_Lua.PushBool(g_Lua.CallFunctionProtected(top - 1, 0, true));
		func.UnReference();
		return 1;
	}

	[LuaGlobal]
	static int DeriveGamemode(ILuaInterface lua) {
		string name = g_Lua!.CheckString(1);
		gGM!.DeriveGamemode(name);
		return 0;
	}

	[LuaGlobal]
	static void require(string name) {
		Bootil.String.Lower(ref name);
		if (name != "timer") // Wow wtf
			g_Lua!.Require(name);
	}

#if GAME_DLL
	static ConVar? hostnameCvar;
#endif

	[LuaGlobal]
	static int GetHostName(ILuaInterface lua) {
#if CLIENT_DLL
		lua.PushString(((ReadOnlySpan<char>)ClientModeShared.HostName).SliceNullTerminatedString());
		return 1;
#else
		ConVar? hostname = hostnameCvar ??= cvar.FindVar("hostname");
		if (hostname == null)
			return 0;
		lua.PushString(LuaConVar.ConVar__GetString(hostname));
		return 1;
#endif
	}

#if CLIENT_DLL
	[LuaGlobal]
	static int LocalPlayer(ILuaInterface lua) {
		LuaEntity.Push_Entity(C_BasePlayer.GetLocalPlayer());
		return 1;
	}
#endif

	static string ToStringArgs(LuaObject tostring, int top, string error) {
		string str = "";
		for (int i = 1; i <= top; i++) {
			LuaObject obj = new(i, LuaType.None);
			tostring.Push();
			obj.Push();
			str += g_Lua!.CallInternalGetString(1) ?? error;
			obj.UnReference();
		}
		return str;
	}

	[LuaGlobal]
	static int Msg(ILuaInterface lua) {
		int top = g_Lua!.Top();
		LuaObject tostring = new();
		g_Lua.Global().GetMember("tostring", tostring);
		string str = ToStringArgs(tostring, top, "Msg tostring ERROR");
		g_Lua.Msg(str);
		tostring.UnReference();
		return 0;
	}

	static Color GetColor(ILuaObject obj) => new(
		(byte)(int)obj.GetMemberFloat("r", 255.0f),
		(byte)(int)obj.GetMemberFloat("g", 255.0f),
		(byte)(int)obj.GetMemberFloat("b", 255.0f),
		(byte)(int)obj.GetMemberFloat("a", 255.0f)
	);

	[LuaGlobal]
	static int MsgC(ILuaInterface lua) {
		int top = g_Lua!.Top();
		Color color = new(0, 200, 255, 255);
		ILuaObject? first = g_Lua.GetObject(1);
		if (first != null && first.isTable())
			color = GetColor(first);

		LuaObject tostring = new();
		g_Lua.Global().GetMember("tostring", tostring);

		string str = "";
		for (int i = 1; i <= top; i++) {
			LuaObject obj = new(i, LuaType.None);
			if (obj.isTable() && !obj.MemberIsNil("r") && !obj.MemberIsNil("g") && !obj.MemberIsNil("b")) {
				if (str.Length != 0)
					g_Lua.MsgColour(in color, str);
				str = "";
				color = new(0, 200, 255, 255);
				if (obj.isTable())
					color = GetColor(obj);
			}
			else {
				tostring.Push();
				obj.Push();
				str += g_Lua.CallInternalGetString(1) ?? "MsgC tostring ERROR";
			}
			obj.UnReference();
		}

		g_Lua.MsgColour(in color, str);
		tostring.UnReference();
		return 0;
	}

	[LuaGlobal]
	static int MsgN(ILuaInterface lua) {
		int top = g_Lua!.Top();
		LuaObject tostring = new();
		g_Lua.Global().GetMember("tostring", tostring);
		string str = ToStringArgs(tostring, top, "MsgN tostring ERROR");
		str += "\n";
		g_Lua.Msg(str);
		tostring.UnReference();
		return 0;
	}

	[LuaGlobal]
	static int ErrorNoHalt(ILuaInterface lua) {
		int top = g_Lua!.Top();
		LuaObject tostring = new();
		g_Lua.Global().GetMember("tostring", tostring);

		StringBuilder buffer = new();
		for (int i = 1; i <= top; i++) {
			LuaObject obj = new(i, LuaType.None);
			tostring.Push();
			obj.Push();
			string str = g_Lua.CallInternalGetString(1) ?? "ErrorNoHalt tostring ERROR";
			buffer.Append(str.AsSpan(0, Math.Min(str.Length, Math.Max(0, 0x1000 - 1 - buffer.Length))));
			obj.UnReference();
		}
		string message = buffer.ToString();

		LuaError error = new() {
			Message = message,
			Side = g_Lua.IsServer() ? "server" : g_Lua.IsMenu() ? "menu" : "client"
		};
		ReadStackFrom(ref error, g_Lua);

		bool isAddon = LuaGameCallback.GetAddonFromError(in error, out IAddonSystem.Information addon, out _);
		get.MenuSystem()?.OnLuaError(in error, isAddon ? addon : null);
		LuaHelper.CallOnLuaErrorHook(in error, isAddon ? addon.Title : null, isAddon ? addon.WorkshopID : 0);

		g_Lua.ErrorNoHalt(message);
		tostring.UnReference();
		return 0;
	}

	[LuaGlobal]
	static int RegisterMetaTable(ILuaInterface lua) {
		LuaObject table = new(2, LuaType.None);
		if (!table.isTable())
			lua.TypeError("table", 2);
		else
			g_Lua!.RegisterMetaTable(g_Lua.CheckString(1), table);
		table.UnReference();
		return 0;
	}

	[LuaGlobal]
	static int FindMetaTable(ILuaInterface lua) {
		ILuaObject? meta = g_Lua!.GetMetaTableObject(g_Lua.CheckString(1), -1);
		if (meta == null)
			return 0;
		meta.Push();
		return 1;
	}

	[LuaGlobal]
	static int TypeID(ILuaInterface lua) {
		g_Lua!.PushNumber((int)g_Lua.GetType(1));
		return 1;
	}

	static int IsType(LuaType type) {
		g_Lua!.PushBool(g_Lua.IsType(1, type));
		return 1;
	}

	[LuaGlobal] static int isbool(ILuaInterface lua) => IsType(LuaType.Bool);
	[LuaGlobal] static int isnumber(ILuaInterface lua) => IsType(LuaType.Number);
	[LuaGlobal] static int isstring(ILuaInterface lua) => IsType(LuaType.String);
	[LuaGlobal] static int istable(ILuaInterface lua) => IsType(LuaType.Table);
	[LuaGlobal] static int isfunction(ILuaInterface lua) => IsType(LuaType.Function);
	[LuaGlobal] static int isentity(ILuaInterface lua) => IsType(LuaType.Entity);
	[LuaGlobal] static int isvector(ILuaInterface lua) => IsType(LuaType.Vector);
	[LuaGlobal] static int isangle(ILuaInterface lua) => IsType(LuaType.Angle);
	[LuaGlobal] static int ispanel(ILuaInterface lua) => IsType(LuaType.Panel);
	[LuaGlobal] static int ismatrix(ILuaInterface lua) => IsType(LuaType.Matrix);

	[LuaGlobal]
	static double CurTime() => gpGlobals.CurTime;

	[LuaGlobal]
	static double UnPredictedCurTime() {
#if CLIENT_DLL
		if (Prediction.UnpredictedCurTime != 0.0)
			return Prediction.UnpredictedCurTime;
#endif
		return gpGlobals.CurTime;
	}

	[LuaGlobal]
	static float RealTime() => (float)gpGlobals.RealTime;

	[LuaGlobal]
	static float FrameTime() => (float)gpGlobals.FrameTime;

	[LuaGlobal]
	static long FrameNumber() => gpGlobals.FrameCount;

	[LuaGlobal]
	static double SysTime() {
#if CLIENT_DLL
		return Singleton<ISystem>().GetCurrentTime();
#else
		return Platform.Time;
#endif
	}

	[LuaGlobal]
	static double VGUIFrameTime() {
#if CLIENT_DLL
		return Singleton<ISystem>().GetFrameTime();
#else
		return Platform.Time;
#endif
	}

#if CLIENT_DLL
	[LuaGlobal]
	static int DisableClipping(ILuaInterface lua) {
		surface.GetClippingRect(out _, out _, out _, out _, out bool clippingDisabled);
		surface.DisableClipping(lua.GetBool(1));
		lua.PushBool(clippingDisabled);
		return 1;
	}
#endif

	[LuaGlobal]
	static int RunConsoleCommand(ILuaInterface lua) {
		string command = g_Lua!.CheckString(1);
		if (!LuaConVar.IsValidConsoleName(command)) {
			g_Lua.ErrorFromLua($"RunConsoleCommand: Command has invalid characters! ({command})\n\tThe first parameter of this function should contain only the command, the second parameter should contain arguments.");
			return 0;
		}

		string? blocked = LuaConCommands.ConCommand_IsBlocked(command);
		if (blocked != null) {
#if CLIENT_DLL
			if (blocked == "connect") {
				// todo menu system
				return 0;
			}
#endif
			g_Lua.ErrorFromLua($"RunConsoleCommand: Command is blocked! ({blocked})");
			return 0;
		}

		if (command.Length <= 1) {
			g_Lua.ErrorFromLua($"RunConsoleCommand: Command is too short, bailing! ({command})");
			return 0;
		}

		StringBuilder buffer = new(command);
		for (int i = 2; i < 64; i++) {
			LuaType type = g_Lua.GetType(i);
			if (type == LuaType.Nil)
				break;

			string? argument = g_Lua.GetString(i);
			if (argument == null)
				break;

			string? blockedArg = LuaConCommands.ConCommand_IsBlockedArg(argument);
			if (blockedArg != null) {
				g_Lua.ErrorFromLua($"RunConsoleCommand: Command argument is blocked! ({command} {blockedArg})");
				return 0;
			}

			if (type == LuaType.Number)
				argument = g_Lua.GetNumber(i).ToString("F2", CultureInfo.InvariantCulture);

			StringBuilder escaped = new();
			for (int c = 0; c < argument.Length && c < 511; c++)
				escaped.Append(argument[c] switch {
					'"' => '\'',
					'\n' => ' ',
					_ => argument[c]
				});

			buffer.Append(' ').Append('"').Append(escaped).Append('"');
		}
		buffer.Append(';');

		string result = buffer.ToString();
		if (result.Length > 1023)
			result = result[..1023];
#if CLIENT_DLL
		engine.ClientCmd(result);
#else
		engine.ServerCommand(result);
#endif
		return 0;
	}

#if CLIENT_DLL
	static Vector3 EyePosition;

	[LuaGlobal]
	static int EyePos(ILuaInterface lua) {
		if (IsCurrentViewAccessAllowed())
			EyePosition = CurrentViewOrigin();
		LuaVector.Push_Vector(EyePosition);
		return 1;
	}
#endif

	public static void ReadStackFrom(ref LuaError error, ILuaInterface lua) {
		error.Stack.Clear();
		lua_Debug ar = default;
		for (int level = 1; level != 17; level++) {
			if (lua.GetStack(level, ref ar) == 0)
				break;
			lua.GetInfo("Slnu", ref ar);
			error.Stack.Add(new LuaError.StackEntry() {
				Source = ar.ShortSource,
				Function = ar.Name ?? "",
				Line = ar.CurrentLine
			});
		}
	}
}
#endif
