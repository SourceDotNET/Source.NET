using Source;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;

namespace Game.Client.GarrysMod;

public static partial class LuaPresets
{
	[LuaGlobal]
	static int LoadPresets(ILuaInterface lua) {
		LuaObject presets = new();
		presets.Set(g_Lua!.GetNewTable());

		ReadOnlySpan<char> group = filesystem.FindFirstEx("settings/presets/*", null, out ulong groupHandle);
		while (!group.IsEmpty) {
			if (filesystem.FindIsDirectory(groupHandle) && group[0] != '.') {
				string groupName = new(group);

				LuaObject groupTable = new();
				groupTable.Set(g_Lua.GetNewTable());
				presets.SetMember(groupName, groupTable);

				ReadOnlySpan<char> file = filesystem.FindFirstEx($"settings/presets/{groupName}/*.txt", null, out ulong fileHandle);
				while (!file.IsEmpty) {
					KeyValues kv = new("");
					if (kv.LoadFromFile(filesystem, $"settings/presets/{groupName}/{file}")) {
						LuaObject preset = new();
						preset.Set(g_Lua.GetNewTable());
						for (KeyValues? sub = kv.GetFirstSubKey(); sub != null; sub = sub.GetNextKey())
							preset.SetMember(sub.Name, sub.GetString(null, ""));

						groupTable.SetMember(kv.Name, preset);
						preset.UnReference();
					}

					file = filesystem.FindNext(fileHandle);
				}
				filesystem.FindClose(fileHandle);
				groupTable.UnReference();
			}

			group = filesystem.FindNext(groupHandle);
		}
		filesystem.FindClose(groupHandle);

		presets.Push();
		presets.UnReference();
		return 1;
	}

	static void SavePreset(string group, string name, LuaObject preset, int index) {
		Span<char> fileName = stackalloc char[260];
		sprintf(fileName, "%d-%s").I(index).S(name);
		strlower(fileName);
		for (int i = 0; i < fileName.Length && fileName[i] != '\0'; i++) {
			switch (fileName[i]) {
				case '\t':
				case '\n':
				case '!':
				case '"':
				case '#':
				case '%':
				case '&':
				case '\'':
				case '*':
				case '.':
				case '/':
				case ':':
				case '<':
				case '>':
				case '?':
				case '@':
				case '\\':
				case '`':
				case '{':
				case '}':
					fileName[i] = '_';
					break;
			}
		}

		string path = $"settings/presets/{group}/{((ReadOnlySpan<char>)fileName).SliceNullTerminatedString()}.txt";
		KeyValues kv = new(name);

		preset.Push();
		g_Lua!.PushNil();
		while (g_Lua.Next(-2)) {
			if (g_Lua.GetType(-2) != LuaType.String)
				g_Lua.ErrorNoHalt($"SavePresets: Preset keyvalue key is a {g_Lua.GetTypeName(g_Lua.GetType(-2))}, string expected");
			else if (g_Lua.GetType(-1) != LuaType.String)
				g_Lua.ErrorNoHalt($"SavePresets: Preset keyvalue value is a {g_Lua.GetTypeName(g_Lua.GetType(-1))}, string expected");
			else
				kv.SetString(g_Lua.GetString(-2), g_Lua.GetString(-1));
			g_Lua.Pop(1);
		}
		g_Lua.Pop(1);

		kv.WriteToFile(filesystem, path, null);
	}

	static void SavePresetGroup(string group, LuaObject presets) {
		if (group.Contains('\\') || group.Contains('/') || group.Contains('.') || group.Contains("..") || group.Contains("./") || group.IndexOfAny([':', '*', '?']) != -1)
			return;

		ReadOnlySpan<char> file = filesystem.FindFirstEx($"settings/presets/{group}/*.txt", null, out ulong handle);
		while (!file.IsEmpty) {
			filesystem.RemoveFile($"settings/presets/{group}/{file}", null);
			file = filesystem.FindNext(handle);
		}
		filesystem.FindClose(handle);
		filesystem.CreateDirHierarchy($"settings/presets/{group}/", null);

		int index = 1;
		presets.Push();
		g_Lua!.PushNil();
		while (g_Lua.Next(-2)) {
			LuaObject key = new(-2, LuaType.None);
			LuaObject value = new(-1, LuaType.None);
			if (!key.isString())
				g_Lua.ErrorNoHalt($"SavePresets: Preset Key is a {g_Lua.GetTypeName(key.GetType())}, string expected");
			else if (!value.isTable())
				g_Lua.ErrorNoHalt($"SavePresets: Preset value is a {g_Lua.GetTypeName(value.GetType())}, table expected");
			else
				SavePreset(group, key.GetString()!, value, index++);
			value.UnReference();
			key.UnReference();
			g_Lua.Pop(1);
		}
		g_Lua.Pop(1);
	}

	[LuaGlobal]
	static int SavePresets(ILuaInterface lua) {
		LuaObject presets = new(1, LuaType.None);
		if (!presets.isTable()) {
			lua.TypeError("table", 1);
			presets.UnReference();
			return 0;
		}

		presets.Push();
		lua.PushNil();
		while (lua.Next(-2)) {
			LuaObject key = new(-2, LuaType.None);
			LuaObject value = new(-1, LuaType.None);
			if (!key.isString())
				lua.ErrorNoHalt($"SavePresets: Group Key is a {lua.GetTypeName(key.GetType())}, string expected");
			else if (!value.isTable())
				lua.ErrorNoHalt($"SavePresets: Group Value is a {lua.GetTypeName(value.GetType())}, table expected");
			else
				SavePresetGroup(key.GetString()!, value);
			value.UnReference();
			key.UnReference();
			lua.Pop(1);
		}
		lua.Pop(1);
		presets.UnReference();
		return 0;
	}
}
