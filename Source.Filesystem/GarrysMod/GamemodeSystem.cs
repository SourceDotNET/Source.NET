using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.FileSystem;

namespace Source.Filesystem.GarrysMod;

public class GamemodeSystem : Gamemode.System
{
	static readonly ConVar gamemode = new("gamemode", "sandbox", 0, "The current gamemode", callback: gamemode_Changed);

	static void gamemode_Changed(IConVar var, in ConVarChangeContext ctx) {
		ConVarRef gm = new("gamemode");
		if (g_FullFileSystem.Gamemodes().Active().Name.AsSpan().SequenceEqual(gm.GetString()))
			return;

		IGamemodeSystem.Information info = g_FullFileSystem.Gamemodes().FindByName(gm.GetString());
		if (!info.Exists) {
			Warning($"Couldn't change active gamemode - '{gm.GetString()}' not found\n");
			var.SetValue(ctx.Old);
			return;
		}

		Msg($"Changing gamemode to {info.Title} ({info.Name})\n");
		g_FullFileSystem.Gamemodes().SetActive(info.Name);
		g_FullFileSystem.Gamemodes().Refresh();
		g_FullFileSystem.DoFilesystemRefresh();
	}

	readonly List<IGamemodeSystem.Information> Gamemodes = [];
	string ActiveGamemode = "sandbox";
	string PreviousGamemode = "";
	string ServerGamemode = "";

	static IGamemodeSystem.Information info;

	public void OnJoinServer(ReadOnlySpan<char> gamemode) {
		ServerGamemode = new(gamemode);
		if (PreviousGamemode.Length == 0)
			PreviousGamemode = Active().Name;

		if (ChangeGamemode(gamemode, false))
			g_FullFileSystem.DoFilesystemRefresh();
	}

	public void OnLeaveServer() {
		ServerGamemode = "";
		if (PreviousGamemode.Length == 0)
			return;

		if (ChangeGamemode(PreviousGamemode, true))
			g_FullFileSystem.DoFilesystemRefresh();

		PreviousGamemode = "";
	}

	public void Refresh() {
		Clear();

		ReadOnlySpan<char> filename = g_FullFileSystem.FindFirstEx("gamemodes/*", "GAME", out FileFindHandle_t findHandle);
		while (!filename.IsEmpty) {
			AddGamemode(new(filename.SliceNullTerminatedString()));
			filename = g_FullFileSystem.FindNext(findHandle);
		}
		g_FullFileSystem.FindClose(findHandle);

		IGamemodeSystem.Information active = Active();
		if (active.Exists)
			Mount(active.Name);
	}

	public void Clear() {
		Gamemodes.Clear();
		g_FullFileSystem.RemoveSearchPathsByGroup(PathGroupName.GMContent);
	}

	public ref IGamemodeSystem.Information Active() => ref FindByName(ActiveGamemode);

	public ref IGamemodeSystem.Information FindByName(ReadOnlySpan<char> name) {
		Span<IGamemodeSystem.Information> list = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Gamemodes);
		for (int i = 0; i < list.Length; i++) {
			if (name.SequenceEqual(list[i].Name))
				return ref list[i];
		}

		info.Exists = false;
		info.Name = new(name);
		return ref info;
	}

	public void SetActive(ReadOnlySpan<char> name) => ActiveGamemode = new(name);

	public List<IGamemodeSystem.Information> GetList() => Gamemodes;

	public bool IsServerBlacklisted(ReadOnlySpan<char> address, ReadOnlySpan<char> hostname, ReadOnlySpan<char> description, ReadOnlySpan<char> gm, ReadOnlySpan<char> map)
		=> BaseFileSystem.get.MenuSystem()!.IsServerBlacklisted(address, hostname, description, gm, map);

	public void OnServerDownloadsMounted() {
		if (ServerGamemode.Length == 0)
			return;

		Refresh();
	}

	bool ChangeGamemode(ReadOnlySpan<char> gamemode, bool restoring) {
		if (Active().Name.AsSpan().SequenceEqual(gamemode))
			return false;

		ref IGamemodeSystem.Information found = ref FindByName(gamemode);
		if (!found.Exists)
			return false;

		Msg($"{(restoring ? "Restoring" : "Switching")} gamemode to {gamemode}\n");
		SetActive(found.Name);
		Refresh();
		return true;
	}

	void Mount(ReadOnlySpan<char> name) {
		IGamemodeSystem.Information gm = FindByName(name);
		if (!gm.Exists)
			return;

		Span<char> fullPath = stackalloc char[MAX_PATH];
		foreach (ILegacyAddons.Information addon in g_FullFileSystem.LegacyAddons().GetList()) {
			string path = addon.GamemodesPath + "/" + gm.Name + "/content";
			if (!string.IsNullOrEmpty(addon.GamemodesPath) && g_FullFileSystem.IsDirectory(path, "MOD")) {
				ReadOnlySpan<char> full = g_FullFileSystem.RelativePathToFullPath(path, "MOD", fullPath);
				if (!full.IsEmpty) {
					g_FullFileSystem.AddSearchPath(full, "GAME", SearchPathAdd.ToTail, PathGroupName.GMContent);
					g_FullFileSystem.AddSearchPath(full, "thirdparty", SearchPathAdd.ToTail, PathGroupName.GMContent);
				}
			}
		}

		string workshop = new string(BaseFileSystem.get.GameDir()) + "/workshop/gamemodes/" + gm.Name + "/content";
		g_FullFileSystem.AddSearchPath(new AddonSearchPath(BaseFileSystem.g_AddonFileSystem, workshop), "GAME", SearchPathAdd.ToTail, PathGroupName.GMContent);
		g_FullFileSystem.AddSearchPath(new AddonSearchPath(BaseFileSystem.g_AddonFileSystem, workshop), "thirdparty", SearchPathAdd.ToTail, PathGroupName.GMContent);

		string content = "gamemodes/" + gm.Name + "/content";
		if (g_FullFileSystem.IsDirectory(content, "MOD")) {
			ReadOnlySpan<char> full = g_FullFileSystem.RelativePathToFullPath(content, "MOD", fullPath);
			if (!full.IsEmpty) {
				g_FullFileSystem.AddSearchPath(full, "GAME", SearchPathAdd.ToTail, PathGroupName.GMContent);
				g_FullFileSystem.AddSearchPath(full, "thirdparty", SearchPathAdd.ToTail, PathGroupName.GMContent);
			}
		}

		Mount(gm.BaseName);
	}

	void AddGamemode(string name) {
		if (name == "." || name == "..")
			return;

		Bootil.String.Lower(ref name);

		KeyValues kv = new("Info");
		if (!kv.LoadFromFile(g_FullFileSystem, "gamemodes/" + name + "/" + name + ".txt", "GAME"))
			return;

		KeyValues? settings = kv.FindKey("settings", false);
		if (settings != null) {
			for (KeyValues? setting = settings.GetFirstSubKey(); setting != null; setting = setting.GetNextKey()) {
				if (setting.GetBool("dontcreate", false))
					continue;

				ReadOnlySpan<char> cvarName = setting.GetString("name", null);
				if (cvarName.IsEmpty)
					Warning($"Gamemode '{name}' has a convar in 'settings' block without a 'name' key!\n");
				else if (!IsValidConsoleName(cvarName))
					Warning($"Not registering convar '{cvarName}' for gamemode '{name}' beacuse it has invalid symbols!\n");
				else if (cvarName.Length < 2)
					Warning($"Not registering convar '{cvarName}' for gamemode '{name}' beacuse its name is too short!\n");
				else if (cvar.FindVar(cvarName) == null) {
					if (cvar.FindCommand(cvarName) == null) {
						string def = AllocString(setting.GetString("default", ""));
						string help = AllocString(setting.GetString("help", ""));
						bool replicate = setting.GetBool("replicate", true);
						FCvar flags = FCvar.Archive | FCvar.Notify | FCvar.LuaServer;
						if (replicate)
							flags |= FCvar.Replicated;
						_ = new ConVar(AllocString(cvarName), def, flags, help);
					}
					else
						Warning($"Not registering convar '{cvarName}' for gamemode '{name}' because there's a command with that name!\n");
				}
			}
		}

		IGamemodeSystem.Information gm;
		gm.Exists = true;
		gm.Name = name;
		gm.Title = new(kv.GetString("title", "Missing Title"));
		gm.Maps = new(kv.GetString("maps", ""));
		gm.BaseName = new(kv.GetString("base", ""));
		gm.Category = new(kv.GetString("category", ""));
		gm.MenuSystem = kv.GetBool("menusystem", false);
		gm.WorkshopID = Bootil.String.To.UInt64(kv.GetString("workshopid", ""));
		Gamemodes.Add(gm);
	}

	static string AllocString(ReadOnlySpan<char> str) => new(str);

	static bool IsValidConsoleName(ReadOnlySpan<char> name) {
		foreach (char c in name) {
			if (char.IsAsciiLetter(c) || char.IsAsciiDigit(c))
				continue;
			if (c is '+' or '-' or '.' or '!' or '^' or '_' or '~')
				continue;
			return false;
		}
		return true;
	}
}
