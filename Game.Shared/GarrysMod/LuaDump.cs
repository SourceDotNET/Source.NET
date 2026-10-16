#if DEBUG && (CLIENT_DLL || GAME_DLL || MENU_DLL)
using Source;
using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;

using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Game.Client.GarrysMod;

public static class LuaDump
{
	const string OutDir = "sdn_luadump";

	const string Script = """
		local lower, sort, concat = string.lower, table.sort, table.concat
		local getinfo = debug.getinfo
		local out, metas, sources = {}, {}, {}

		local function Kind(f)
			local info = getinfo(f, "S")
			if not info then return "lua" end
			if info.what == "C" then return "c" end
			if isstring(info.short_src) and string.sub(info.short_src, 1, 9) == "[builtin:" then return "c" end
			if isstring(info.source) and string.sub(info.source, 1, 1) == "@" then sources[string.sub(info.source, 2)] = true end
			return "lua"
		end

		local function Emit(addr, lang)
			addr = lower(addr)
			if not out[addr] then out[addr] = lang end
		end

		local function TableKind(t)
			for _, v in pairs(t) do
				if isfunction(v) and Kind(v) == "c" then return "c" end
			end
			return "lua"
		end

		local function Scan(prefix, sep, t)
			for k, v in pairs(t) do
				if isstring(k) then
					if isfunction(v) then
						Emit(prefix .. sep .. k, Kind(v))
					elseif istable(v) and v ~= t then
						Emit(prefix .. "." .. k, TableKind(v))
					end
				end
			end
		end

		local classNames = {
			"Angle", "Vector", "VMatrix", "Color", "Entity", "Player", "Weapon", "NPC", "NextBot", "Vehicle", "CSEnt",
			"PhysObj", "PhysCollide", "Panel", "IMaterial", "ITexture", "IMesh", "IVideoWriter", "IGModAudioChannel",
			"IRestore", "ISave", "File", "ConVar", "CEffectData", "CLuaEmitter", "CLuaParticle", "CLuaLocomotion",
			"CMoveData", "CUserCmd", "CTakeDamageInfo", "CSoundPatch", "CNavArea", "CNavLadder", "CNewParticleEffect",
			"PathFollower", "ProjectedTexture", "MarkupObject", "Schedule", "Task", "SurfaceInfo", "Stack", "Tool",
			"bf_read", "pixelvis_handle_t", "sensor", "SchedulingObject", "Collection",
		}
		local reg = debug.getregistry and debug.getregistry()
		if istable(reg) then
			for name in pairs(reg) do
				if isstring(name) then classNames[#classNames + 1] = name end
			end
		end
		for _, name in ipairs(classNames) do
			local mt = FindMetaTable and FindMetaTable(name)
			if istable(mt) and not metas[mt] then
				metas[mt] = true
				Scan(name, ":", mt)
			end
		end

		if derma and derma.GetControlList then
			for _, ctrl in pairs(derma.GetControlList()) do
				if istable(ctrl) and isstring(ctrl.ClassName) and istable(ctrl.PanelTable) then
					metas[ctrl.PanelTable] = true
					Scan(ctrl.ClassName, ":", ctrl.PanelTable)
				end
			end
		end

		for name, v in pairs(_G) do
			if isstring(name) then
				if isfunction(v) then
					Emit("global." .. name, Kind(v))
				elseif istable(v) and v ~= _G and not metas[v] and name ~= "package" and name ~= "_LOADED" and name ~= "GAMEMODE" and name ~= "GM" then
					local isPanel = isfunction(rawget(v, "Init")) or isstring(rawget(v, "Base"))
					Scan(name, isPanel and ":" or ".", v)
				end
			end
		end

		local lines, files = {}, {}
		for addr, lang in pairs(out) do lines[#lines + 1] = addr .. " = " .. lang end
		for src in pairs(sources) do files[#files + 1] = src end
		sort(lines)
		SDN_LUADUMP = concat(lines, "\n")
		SDN_LUADUMP_SRC = concat(files, "\n")
		""";

	static readonly (string Project, string[] Realms)[] Projects = [
		("Game.Client", ["client"]),
		("Game.Server", ["server"]),
		("Game.Shared", ["client", "server"]),
		("Game.MenuSystem", ["menu"]),
		("Source.GUI.Controls", ["client", "menu"]),
	];

	static readonly Regex GamemodeHook = new(@"gGM[!?]?\.(?:Call|CallWithArgs)\(\s*(?:\(int\))?(?:PooledStrings\.)?LUA_POOLEDSTRING\.(\w+)");
	static readonly Regex GamemodeHookString = new(@"\bCall(?:WithArgs)?\(""(\w+)""\)");
	static readonly Regex PanelHook = new(@"PushLuaHook\(LUA_POOLEDSTRING\.(\w+)|UpdateLuaHook\(panel, ""(\w+)""|GetField\(-1, ""(On\w+)""\)");
	static readonly Regex EntityHook = new(@"ScriptedEntity\.(?:StartFunction|CallFunction)\(\s*\(int\)LUA_POOLEDSTRING\.(\w+)");
	static readonly Regex LuaHookCall = new(@"hook\.(?:Run|Call)\(\s*[""'](\w+)[""']|(?<!function\s+)\b(?:GAMEMODE|GM):(\w+)\(");

	static string? FindRepoRoot() {
		for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent) {
			if (File.Exists(Path.Combine(dir.FullName, "Source.NET.slnx")))
				return dir.FullName;
		}
		return null;
	}

	static void ScanCSharpHooks(string realm, SortedDictionary<string, string> hooks) {
		string? root = FindRepoRoot();
		if (root == null) {
			Warning("sdn_luadump: Source.NET.slnx not found above the binary, C# hook calls skipped\n");
			return;
		}

		foreach (var (project, realms) in Projects) {
			if (Array.IndexOf(realms, realm) == -1)
				continue;

			string dir = Path.Combine(root, project);
			if (!Directory.Exists(dir))
				continue;

			foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)) {
				string text = File.ReadAllText(file);
				string name = Path.GetFileName(file);

				foreach (Match m in GamemodeHook.Matches(text))
					hooks["gm:" + m.Groups[1].Value.ToLowerInvariant()] = "c";
				if (name == "LuaGamemode.cs")
					foreach (Match m in GamemodeHookString.Matches(text))
						hooks["gm:" + m.Groups[1].Value.ToLowerInvariant()] = "c";
				foreach (Match m in PanelHook.Matches(text)) {
					string hook = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
					hooks[(hook == "DoClick" ? "dlabel:" : "panel:") + hook.ToLowerInvariant()] = "c";
				}
				string owner = name.Contains("SWEP") ? "weapon:" : "entity:";
				foreach (Match m in EntityHook.Matches(text))
					hooks[owner + m.Groups[1].Value.ToLowerInvariant()] = "c";
			}
		}
	}

	static string? ReadFile(string path, string pathID) {
		long size = filesystem.Size(path, pathID);
		if (size <= 0)
			return null;
		byte[] buf = new byte[size];
		if (!filesystem.ReadFile(path, pathID, buf, 0))
			return null;
		return Encoding.UTF8.GetString(buf);
	}

	static string? ReadLuaSource(ILuaInterface lua, string src) {
		string? text = ReadFile(src, "GAME") ?? ReadFile(src, lua.GetPathID());
		if (text == null && src.StartsWith("lua/"))
			text = ReadFile(src["lua/".Length..], lua.GetPathID());
		if (text == null && src.StartsWith("gamemodes/"))
			text = ReadFile(src["gamemodes/".Length..], lua.GetPathID());
		return text;
	}

	static void ScanLuaHooks(ILuaInterface lua, string sources, SortedDictionary<string, string> hooks) {
		string[] files = sources.Split('\n', StringSplitOptions.RemoveEmptyEntries);
		int unread = 0;
		string? firstUnread = null;
		foreach (string src in files) {
			string? text = ReadLuaSource(lua, src);
			if (text == null) {
				unread++;
				firstUnread ??= src;
				continue;
			}
			foreach (Match m in LuaHookCall.Matches(text))
				hooks.TryAdd("gm:" + (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToLowerInvariant(), "lua");
		}
		Msg($"sdn_luadump: scanned {files.Length - unread}/{files.Length} Lua sources for hook calls\n");
		if (firstUnread != null)
			Warning($"sdn_luadump: {unread} Lua sources unreadable, first: {firstUnread}\n");
	}

	static string? GetGlobalString(ILuaInterface lua, string name) {
		lua.PushSpecial(Special.Glob);
		lua.GetField(-1, name);
		string? value = lua.GetString(-1);
		lua.Pop(2);
		return value;
	}

	public static void Dump(ILuaInterface? lua, string realm) {
		if (!commandLine.CheckParm("-noaddons") || !commandLine.CheckParm("-noworkshop")) {
			Warning("launch with -noaddons -noworkshop so data isn't corrupted :(\n");
			return;
		}

		if (lua == null) {
			Warning($"sdn_luadump: no {realm} Lua state\n");
			return;
		}

		if (!lua.RunString("sdn_luadump", "", Script, true, true)) {
			Warning($"sdn_luadump: {realm} dump script failed\n");
			return;
		}

		string functions = GetGlobalString(lua, "SDN_LUADUMP") ?? "";
		string sources = GetGlobalString(lua, "SDN_LUADUMP_SRC") ?? "";
		lua.RunString("sdn_luadump", "", "SDN_LUADUMP = nil SDN_LUADUMP_SRC = nil", true, true);

		SortedDictionary<string, string> hooks = [];
		ScanCSharpHooks(realm, hooks);
		ScanLuaHooks(lua, sources, hooks);

		StringBuilder sb = new();
		sb.Append("# sdn_luadump 2\n# realm ").Append(realm).Append('\n');
		sb.Append(functions).Append('\n');
		sb.Append("# hooks\n");
		foreach (var (addr, lang) in hooks)
			sb.Append(addr).Append(" = ").Append(lang).Append('\n');

		Directory.CreateDirectory(OutDir);
		string path = Path.Combine(OutDir, "sdn_luadump_" + realm + ".txt");
		File.WriteAllText(path, sb.ToString());
		Msg($"sdn_luadump: {realm} realm dumped to {Path.GetFullPath(path)} ({hooks.Count} hooks)\n");
	}

#if CLIENT_DLL
	[ConCommand("sdn_luadump_client")]
	static void CC_LuaDumpClient() => Dump(g_Lua, "client");

	[ConCommand("sdn_luadump")]
	static void CC_LuaDump() {
		StringBuilder sb = new();
		foreach (string realm in (string[])["server", "client", "menu"]) {
			string path = Path.Combine(OutDir, "sdn_luadump_" + realm + ".txt");
			if (!File.Exists(path)) {
				Warning($"sdn_luadump: missing {path}, run sdn_luadump_{realm} first\n");
				continue;
			}
			sb.Append(File.ReadAllText(path));
		}

		Directory.CreateDirectory(OutDir);
		string outPath = Path.Combine(OutDir, "sdn_luadump.txt");
		File.WriteAllText(outPath, sb.ToString());
		Msg($"sdn_luadump: combined dump written to {Path.GetFullPath(outPath)}\n");
	}
#elif GAME_DLL
	[ConCommand("sdn_luadump_server")]
	static void CC_LuaDumpServer() => Dump(g_Lua, "server");
#elif MENU_DLL
	[ConCommand("sdn_luadump_menu")]
	static void CC_LuaDumpMenu() => Dump(g_Lua, "menu");
#endif
}
#endif
