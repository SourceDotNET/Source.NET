using CommunityToolkit.HighPerformance;

using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.Common.Steam;

using Steamworks;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Source.Filesystem.GarrysMod;

public class GameDepotSystem : GameDepot.System
{
	readonly List<IGameDepotSystem.Information> Games = [];
	readonly Dictionary<UtlSymId_t, int> GamesNameLUT = [];

	void MountGame(ref IGameDepotSystem.Information game) {
		if (game.Mounted || !game.Installed) return;
		Msg($"Mounting game '{game.Title}' ({game.Folder}, {(uint)game.AppID})\n");
		if (game.Folder.Equals("hl2", StringComparison.OrdinalIgnoreCase)) {
			game.Mounted = true;
			return;
		}
		SteamApps.GetAppInstallDir(game.AppID, out string dir, MAX_PATH);
		foreach (string extra in game.ExtraFolders)
			MountContentDir(Path.Combine(dir, extra), game.Folder);
		MountContentDir(Path.Combine(dir, game.Folder), game.Folder);
		game.Mounted = true;
	}

	void UnmountGame(ref IGameDepotSystem.Information game) {
		if (!game.Mounted) return;
		SteamApps.GetAppInstallDir(game.AppID, out string dir, MAX_PATH);
		foreach (string extra in game.ExtraFolders)
			UnmountContentDir(Path.Combine(dir, extra), game.Folder);
		UnmountContentDir(Path.Combine(dir, game.Folder), game.Folder);
		game.Mounted = false;
	}

	static void MountContentDir(string absDir, ReadOnlySpan<char> mountID) {
		if (!Directory.Exists(absDir)) return;
		foreach (string vpk in Directory.EnumerateFiles(absDir, "*_dir.vpk")) {
			g_FullFileSystem.AddSearchPath(vpk, "GAME", groupName: PathGroupName.GameContent);
			g_FullFileSystem.AddSearchPath(vpk, mountID, groupName: PathGroupName.GameContent);
		}
		g_FullFileSystem.AddSearchPath(absDir, "GAME", groupName: PathGroupName.GameContent);
		g_FullFileSystem.AddSearchPath(absDir, mountID, groupName: PathGroupName.GameContent);
	}

	static void UnmountContentDir(string absDir, ReadOnlySpan<char> mountID) {
		if (!Directory.Exists(absDir)) return;
		foreach (string vpk in Directory.EnumerateFiles(absDir, "*_dir.vpk")) {
			g_FullFileSystem.RemoveSearchPath(vpk, "GAME");
			g_FullFileSystem.RemoveSearchPath(vpk, mountID);
		}
		g_FullFileSystem.RemoveSearchPath(absDir, "GAME");
		g_FullFileSystem.RemoveSearchPath(absDir, mountID);
	}


	public void Clear() {
		Games.Clear();
		GamesNameLUT.Clear();
	}

	public List<IGameDepotSystem.Information> GetList() => Games;

	public void MarkGameAsMounted(ReadOnlySpan<char> folder) {
		Span<IGameDepotSystem.Information> games = Games.AsSpan();
		for (int i = 0; i < games.Length; i++) {
			ref IGameDepotSystem.Information game = ref games[i];
			if (folder.SequenceEqual(game.Folder)) {
				game.Mounted = true;
				break;
			}
		}

		Save();
	}

	public void MountAsMapFix(AppId_t appID) {
		// todo
		// This almost certainly is a per-map-latch thing im guessing for specific maps needing content
	}

	public void MountCurrentGame(ReadOnlySpan<char> game) { } // todo
	public void Refresh() {
		UnmountAll();
		Clear();
		LoadGameList();
		LoadMountPrefs();

		Span<IGameDepotSystem.Information> games = Games.AsSpan();
		for (int i = 0; i < games.Length; i++) {
			ref IGameDepotSystem.Information game = ref games[i];
			if (game.Enabled)
				MountGame(ref game);
		}
	}

	private void LoadMountPrefs() {
		KeyValues gamedepotsystem = new("gamedepotsystem");
		if (!gamedepotsystem.LoadFromFile(g_FullFileSystem, "cfg/mountdepots.txt", "MOD"))
			return;

		foreach (KeyValues enabledGame in gamedepotsystem) {
			if (GamesNameLUT.TryGetValue(enabledGame.Name.Hash(), out int idx)) {
				ref IGameDepotSystem.Information game = ref Games.AsSpan()[idx];
				if (enabledGame.GetBool())
					MountGame(ref game);
				else
					UnmountGame(ref game);
			}
		}
	}

	private void LoadGameList() {
#if SWDS

#else
		Games.Clear();

		KeyValues manifest = new("mountable_games");
		if (!manifest.LoadFromFile(g_FullFileSystem, "sdn_resource/mountable_game_manifest.txt", "MOD"))
			return;

		foreach (KeyValues game in manifest) {
			string[] extra = new string(game.GetString("extra")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
			AppId_t appID = (AppId_t)uint.Parse(game.Name);
			Games.Add(new IGameDepotSystem.Information {
				AppID = appID,
				Title = new(game.GetString("title")),
				Folder = new(game.GetString("folder")),
				Enabled = game.GetBool("mount", false),
				Retail = game.GetBool("retail", false),
				Bundled = game.GetBool("bundled", false),
				ExtraFolders = [.. extra],
				Installed = SteamApps.BIsAppInstalled(appID)
			});

			GamesNameLUT[game.GetString("folder").Hash()] = Games.Count - 1;
		}
#endif
	}

	private void UnmountAll() {
		Span<IGameDepotSystem.Information> games = Games.AsSpan();
		for (int i = 0; i < games.Length; i++) {
			ref IGameDepotSystem.Information game = ref games[i];
			if (game.Mounted)
				UnmountGame(ref game);
		}
	}

	public void Save() {
		KeyValues gamedepotsystem = new("gamedepotsystem");

		if (!gamedepotsystem.LoadFromFile(g_FullFileSystem, "cfg/mountdepots.txt", "MOD"))
			return;

		Span<IGameDepotSystem.Information> games = Games.AsSpan();
		for (int i = 0; i < games.Length; i++) {
			ref IGameDepotSystem.Information game = ref games[i];
			if (game.Mounted)
				gamedepotsystem.SetString(game.Folder, "1");
		}

		if (!gamedepotsystem.WriteToFile(g_FullFileSystem, "cfg/mountdepots.txt", "MOD"))
			Warning("Could not save cfg/mountdepots.txt!\n");
	}

	public void SetMount(AppId_t appID, bool shouldMount) {
		Span<IGameDepotSystem.Information> games = Games.AsSpan();
		for (int i = 0; i < games.Length; i++) {
			ref IGameDepotSystem.Information game = ref games[i];
			if (game.AppID != appID)
				continue;

			game.Enabled = shouldMount;
			if (shouldMount)
				MountGame(ref game);
			else
				UnmountGame(ref game);

			break;
		}

		Save();
	}
}
