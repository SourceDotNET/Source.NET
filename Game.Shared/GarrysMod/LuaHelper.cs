#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static class LuaHelper
{
	public static bool IsInErrorCB;

	public static int cvttsd2si(double value) => double.IsNaN(value) || value >= 2147483648.0 || value <= -2147483649.0 ? int.MinValue : (int)value;

	public static long cvttsd2si64(double value) => double.IsNaN(value) || value >= 9223372036854775808.0 || value < -9223372036854775808.0 ? long.MinValue : (long)value;

	public static void KeyValuesToTable(KeyValues kv, ILuaObject table, bool preserveKeyCase) {
		for (KeyValues? sub = kv.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
			string name = sub.Name.Length > 511 ? sub.Name[..511] : sub.Name;
			if (!preserveKeyCase)
				name = name.ToLowerInvariant();

			switch (sub.Type) {
				case KeyValues.Types.Int:
					table.SetMember(name, sub.GetInt());
					break;
				case KeyValues.Types.Double:
					table.SetMember(name, MathF.Floor(sub.GetFloat() * 10000.0f) / 10000.0f);
					break;
				case KeyValues.Types.None: {
						LuaTable subTable = new(null, 0);
						table.SetMember(name, subTable);
						KeyValuesToTable(sub, subTable, preserveKeyCase);
						subTable.UnReference();
						break;
					}
				default:
					table.SetMember(name, sub.GetString());
					break;
			}
		}
	}

	public static void KeyValuesToTablePreserveOrder(KeyValues kv, ILuaObject? table, bool preserveKeyCase) {
		int count = 0;
		int arrayIndex = 1;
		bool isArray = true;
		KeyValues? sub = kv.GetFirstSubKey();
		if (sub != null) {
			do {
				count++;
				if (sub.Name != "0") {
					isArray = false;
					break;
				}
				sub = sub.GetNextKey();
			} while (sub != null);
			if (count == 1)
				isArray = false;
		}

		int entryIndex = 1;
		for (sub = kv.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
			string name = sub.Name.Length > 255 ? sub.Name[..255] : sub.Name;
			if (!preserveKeyCase)
				name = name.ToLowerInvariant();
			if (name == "0" && isArray)
				name = (arrayIndex++).ToString();

			LuaTable entry = new(null, 0);
			entry.SetMember("Key", name);
			table?.SetMember(entryIndex++, entry);

			switch (sub.Type) {
				case KeyValues.Types.None: {
						LuaTable subTable = new(null, 0);
						entry.SetMember("Value", subTable);
						KeyValuesToTablePreserveOrder(sub, subTable, preserveKeyCase);
						subTable.UnReference();
						break;
					}
				case KeyValues.Types.Int:
				case KeyValues.Types.Double:
					entry.SetMember("Value", sub.GetFloat());
					break;
				default:
					entry.SetMember("Value", sub.GetString());
					break;
			}
			entry.UnReference();
		}
	}

	public static void CallOnLuaErrorHook(in LuaError error, string? addonTitle, ulong workshopID) {
		if (g_Lua == null || g_Lua.Global() == null)
			return;

		if (IsInErrorCB) {
			Warning($"Error during OnLuaError on {error.Side}!\n");
			return;
		}

		LuaObject hook = new();
		g_Lua.Global().GetMember("hook", hook);
		if (!hook.isTable()) {
			hook.UnReference();
			return;
		}

		LuaObject run = new();
		hook.GetMember("Run", run);
		if (!run.isFunction()) {
			run.UnReference();
			hook.UnReference();
			return;
		}

		run.Push();
		g_Lua.PushString("OnLuaError");
		g_Lua.PushString(error.Message);
		g_Lua.PushString(error.Side);

		LuaTable stack = new();
		for (int i = 0; i < error.Stack.Count;) {
			LuaTable entry = new();
			entry.SetMember("File", error.Stack[i].Source);
			entry.SetMember("Line", (float)error.Stack[i].Line);
			entry.SetMember("Function", error.Stack[i].Function);
			i++;
			stack.SetMember((float)i, entry);
			entry.UnReference();
		}

		stack.Push();
		IsInErrorCB = true;

		if (addonTitle != null) {
			g_Lua.PushString(addonTitle);
			g_Lua.PushString(workshopID.ToString());
			g_Lua.CallInternalNoReturns(6);
		}
		else
			g_Lua.CallInternalNoReturns(4);

		IsInErrorCB = false;

		stack.UnReference();
		run.UnReference();
		hook.UnReference();
	}

	static bool GetLegacyAddon(string path, ref IAddonSystem.Information local) {
		int start = path.IndexOf("addons/", StringComparison.Ordinal);
		if (start == -1)
			return false;

		start += 7;
		int end = path.IndexOf('/', start);
		if (end == -1)
			return false;

		local.Title = path[start..end];
		return true;
	}

	public static bool GetAddonFromError(in LuaError error, out IAddonSystem.Information info, out bool overriding) {
		IAddonSystem.Information local = new() { Title = "", File = "", Tags = "", Failure = "" };
		IAddonSystem.Information owner = default;
		bool found = false;
		bool overridingFile = false;

		int colon = error.Message.IndexOf(':');
		if (colon != -1) {
			string file = error.Message[..colon];
			if (Bootil.String.Test.EndsWith(file, ".lua")) {
				if (filesystem != null && filesystem.Addons() != null && filesystem.Addons().FindFileOwner(file, out owner))
					found = true;
				else if (GetLegacyAddon(file, ref local)) {
					owner = local;
					found = true;
				}
			}
		}

		foreach (LuaError.StackEntry entry in error.Stack) {
			if (entry.Source.Length == 0)
				continue;

			if (found || overridingFile)
				break;

			if (filesystem != null && filesystem.Addons() != null) {
				found = filesystem.Addons().FindFileOwner(entry.Source, out owner);

				LuaFile? cache = get.LuaShared()!.GetCache(entry.Source);
				if (cache != null && cache.Time > 1) {
					if (found) {
						Warning($"Local file is overriding addon's file! {entry.Source} ({owner.Title})\n");
						found = false;
						overridingFile = true;
						continue;
					}
				}
				else if (found) {
					overridingFile = false;
					continue;
				}
			}

			overridingFile = false;
			found = GetLegacyAddon(entry.Source, ref local);
			if (found)
				owner = local;
		}

		overriding = overridingFile;

		if (!found) {
			info = default;
			return false;
		}

		info = owner;
		info.Title ??= "";
		return true;
	}
}
#endif
