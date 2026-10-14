#if CLIENT_DLL || GAME_DLL
using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaConVar
{
	[LuaClass(typeof(ConVar), NullError = "Tried to use a NULL ConVar!")]
	public static readonly LuaClass LC_ConVar = new("ConVar", LuaType.ConVar, null, null);

	static readonly string[] s_BannedInfo = [
		"rcon_password",
		"sv_password",
		"password",
		"tv_password",
		"tv_relaypassword",
		"rcon_address",
		"lua_error_url",
	];

	public static void Push_ConVar(ConVar? convar) => LC_ConVar.Push(convar);

	static void CheckLuaConVar(ConVar convar) {
#if CLIENT_DLL
		if (!convar.IsFlagSet(FCvar.LuaClient))
#else
		if (!convar.IsFlagSet(FCvar.LuaServer))
#endif
			g_Lua!.ArgError(1, "attempted to modify ConVar not created by Lua");
	}

	public static bool IsValidConsoleName(ReadOnlySpan<char> name) {
		foreach (char c in name) {
			if (char.IsAsciiLetter(c) || char.IsAsciiDigit(c))
				continue;
			if (c is '+' or '-' or '.' or '!' or '^' or '_' or '~')
				continue;
			return false;
		}
		return true;
	}

	public static bool IsAllowedToGetConvarInfo(ReadOnlySpan<char> name) {
		foreach (string banned in s_BannedInfo) {
			if (stricmp(banned, name) == 0)
				return false;
		}
		return true;
	}

	public static bool ShouldPushConVar(ConVar? convar) {
		if (convar == null)
			return false;

		bool lua = convar.IsFlagSet(FCvar.LuaClient) || convar.IsFlagSet(FCvar.LuaServer);
		if (convar.IsFlagSet(FCvar.Hidden) || convar.IsFlagSet(FCvar.DevelopmentOnly) || convar.IsFlagSet(FCvar.Unregistered))
			return lua;

		return true;
	}

	[LuaMethod]
	static string ConVar____tostring(ConVar? convar) {
		if (convar == null)
			return "ConVar [NULL]";

		string str = $"ConVar [{convar.GetName()}]";
		return str.Length > 0x1FF ? str[..0x1FF] : str;
	}

	[LuaMethod]
	static string ConVar__GetName(ConVar convar) => convar.GetName();

	[LuaMethod]
	static string ConVar__GetDefault(ConVar convar) => convar.GetDefault();

	[LuaMethod]
	static string ConVar__GetHelpText(ConVar convar) => convar.GetHelpText() ?? "";

	[LuaMethod]
	public static string ConVar__GetString(ConVar convar) {
		if ((convar.GetFlags() & FCvar.NeverAsString) != 0)
			return "FCVAR_NEVER_AS_STRING";
		return convar.GetString();
	}

	[LuaMethod]
	static float ConVar__GetFloat(ConVar convar) => convar.GetFloat();

	[LuaMethod]
	static int ConVar__GetInt(ConVar convar) => convar.GetInt();

	[LuaMethod]
	static bool ConVar__GetBool(ConVar convar) => convar.GetInt() != 0;

	[LuaMethod]
	static void ConVar__SetString([LuaValidate(nameof(CheckLuaConVar))] ConVar convar, string value) => convar.SetValue(value);

	[LuaMethod]
	static void ConVar__SetFloat([LuaValidate(nameof(CheckLuaConVar))] ConVar convar, float value) => convar.SetValue(value);

	[LuaMethod]
	static void ConVar__SetInt([LuaValidate(nameof(CheckLuaConVar))] ConVar convar, int value) => convar.SetValue(value);

	[LuaMethod]
	static void ConVar__SetBool([LuaValidate(nameof(CheckLuaConVar))] ConVar convar, [LuaGet] bool value) => convar.SetValue(value ? 1 : 0);

	[LuaMethod]
	static int ConVar__GetFlags(ConVar convar) => (int)convar.GetFlags();

	[LuaMethod]
	static bool ConVar__IsFlagSet(ConVar convar, int flag) => ((int)convar.GetFlags() & flag) != 0;

	[LuaMethod]
	static void ConVar__Revert([LuaValidate(nameof(CheckLuaConVar))] ConVar convar) => convar.Revert();

	[LuaMethod]
	static float? ConVar__GetMax(ConVar convar) {
		if (!convar.GetMax(out double max))
			return null;
		return (float)max;
	}

	[LuaMethod]
	static float? ConVar__GetMin(ConVar convar) {
		if (!convar.GetMin(out double min))
			return null;
		return (float)min;
	}

	[LuaGlobal]
	static ConVar? GetConVar_Internal(string name) {
		if (!IsAllowedToGetConvarInfo(name) || stricmp("con_logfile", name) == 0)
			return null;

		ConVar? convar = cvar.FindVar(name);
		if (convar == null || !ShouldPushConVar(convar))
			return null;

		return convar;
	}

	[LuaGlobal]
	static int CreateConVar(ILuaInterface lua) {
		string name = g_Lua!.CheckString(1);
		if (!IsAllowedToGetConvarInfo(name) || LuaConCommands.ConCommand_IsBlocked(name) != null) {
			g_Lua.ErrorFromLua($"CreateConVar: ConVar name is blocked! ({name})");
			return 0;
		}

		if (cvar.FindCommand(name) != null) {
			g_Lua.ErrorFromLua($"CreateConVar: Cannot override an existing console command! ({name})");
			return 0;
		}

		if (strlen(name) <= 1) {
			g_Lua.ErrorFromLua($"CreateConVar: ConVar name is too short! ({name})");
			return 0;
		}

		if (!IsValidConsoleName(name)) {
			g_Lua.ErrorFromLua($"CreateConVar: Invalid ConVar name! ({name})");
			return 0;
		}

		string defaultValue = g_Lua.CheckString(2);

		int flags = g_Lua.GetFlags(3);
		if ((flags & (int)FCvar.DontRecord) == 0)
			flags |= (int)FCvar.Demo;

		string? helpString = null;
		if (g_Lua.GetType(4) == LuaType.String)
			helpString = g_Lua.CheckString(4);
		else if (g_Lua.GetType(4) != LuaType.Nil)
			lua.ErrorFromLua($"bad argument #4 to CreateConVar (string expected, got {lua.GetTypeName(lua.GetType(4))})");

		bool hasMin = false;
		float min = 0.0f;
		if (g_Lua.GetType(5) == LuaType.Number) {
			min = (float)g_Lua.CheckNumber(5);
			hasMin = true;
		}
		else if (g_Lua.GetType(5) != LuaType.Nil)
			lua.ErrorFromLua($"bad argument #5 to CreateConVar (number expected, got {lua.GetTypeName(lua.GetType(5))})");

		bool hasMax = false;
		float max = 0.0f;
		if (g_Lua.GetType(6) == LuaType.Number) {
			max = (float)g_Lua.CheckNumber(6);
			hasMax = true;
		}
		else if (g_Lua.GetType(6) != LuaType.Nil)
			lua.ErrorFromLua($"bad argument #6 to CreateConVar (number expected, got {lua.GetTypeName(lua.GetType(6))})");

		ConVar? existing = cvar.FindVar(name);
		if (existing != null) {
			if (!ShouldPushConVar(existing))
				return 0;
			Push_ConVar(existing);
			return 1;
		}

		ConVar convar = g_Lua.CreateConVar(name, defaultValue, helpString, flags);
		convar.SetMin(hasMin, min);
		convar.SetMax(hasMax, max);
		if ((flags & (int)FCvar.UserInfo) != 0)
			cvar.CallGlobalChangeCallbacks(convar, new ConVarChangeContext { Old = "" });

		Push_ConVar(convar);
		return 1;
	}

	[LuaGlobal]
	static bool ConVarExists(string name) {
		if (stricmp(name, "maxplayers") == 0)
			return true;

		ConVar? convar = cvar.FindVar(name);
		if (ShouldPushConVar(convar))
			return convar != null;
		return false;
	}
}
#endif
