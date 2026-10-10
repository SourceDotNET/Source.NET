using Source.Common.GarrysMod;

using System;
using System.Collections.Generic;
using System.Text;

namespace Source.Filesystem.GarrysMod;

public class LegacyAddonSystem : LegacyAddons.System
{
	public readonly List<ILegacyAddons.Information> Addons = [];

	public List<ILegacyAddons.Information> GetList() => Addons;
	public void Refresh() {
		foreach (ILegacyAddons.Information info in Addons) {
			g_FullFileSystem.RemoveSearchPath(info.Path, "GAME");
			g_FullFileSystem.RemoveSearchPath(info.Path, "thirdparty");
		}

		Addons.Clear();

		FileFindHandle_t findHandle;
		ReadOnlySpan<char> filename = g_FullFileSystem.FindFirstEx("addons/*", "MOD", out findHandle);
		Span<char> fullpath = stackalloc char[1024];
		while (!filename.IsEmpty) {
			if (g_FullFileSystem.FindIsDirectory(findHandle)) {
				ReadOnlySpan<char> path = $"addons/{filename}";

				g_FullFileSystem.RelativePathToFullPath(path, "MOD", fullpath);
				Msg($"Adding Filesystem Addon '{path}'\n");

				g_FullFileSystem.AddSearchPath(fullpath.SliceNullTerminatedString(), "GAME", groupName: Common.Filesystem.PathGroupName.AddonContent);
				g_FullFileSystem.AddSearchPath(fullpath.SliceNullTerminatedString(), "thirdparty", groupName: Common.Filesystem.PathGroupName.AddonContent);

				string full = new(fullpath.SliceNullTerminatedString());
				string luaPath = "";
				string gamemodesPath = "";
				if (g_FullFileSystem.IsDirectory(full + "/lua", null))
					luaPath = path.ToString() + "/lua";
				if (g_FullFileSystem.IsDirectory(full + "/gamemodes", null))
					gamemodesPath = path.ToString() + "/gamemodes";

				ILegacyAddons.Information information;
				information.Name = new(filename.SliceNullTerminatedString());
				information.Path = full;
				information.LuaPath = luaPath;
				information.GamemodesPath = gamemodesPath;

				Addons.Add(information);
			}

			filename = g_FullFileSystem.FindNext(findHandle);
		}
		g_FullFileSystem.FindClose(findHandle);
	}
}
