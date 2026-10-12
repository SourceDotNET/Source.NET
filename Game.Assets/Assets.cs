using System.Runtime.InteropServices;
using Source.GUI.Controls;
using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using System.Diagnostics;
using Source;

namespace Game.Assets;

static class AssetUtils
{
	public record AssetMapping(string LocalPath, string RemotePath, bool IsDirectory = false, bool Optional = false);

	private const int GModAppID = 4000;
	private const string GModLocalPath = "steamapps/common/GarrysMod";

	public static void CheckRequired() {
		string exeDir = AppDomain.CurrentDomain.BaseDirectory;

		if (GetRequiredAssets().All(asset => IsAssetLinked(asset.LocalPath, exeDir)))
			return;

		bool result = Singleton<MessageBoxFn>()("Source.NET", "Missing required content, should we automatically link it?", true);

		if (result)
			LinkAllAssets(exeDir);
	}

	public static List<AssetMapping> GetRequiredAssets() {
		List<AssetMapping> list = [
			new("sdn/steam.inf", "garrysmod/steam.inf"),
			new("sourceengine", "sourceengine", IsDirectory: true),
			new("platform", "platform", IsDirectory: true),
			new("sdn/maps", "garrysmod/maps", IsDirectory: true),
			new("sdn/materials", "garrysmod/materials", IsDirectory: true),
			new("sdn/resource", "garrysmod/resource", IsDirectory: true),
			new("sdn/scenes", "garrysmod/scenes", IsDirectory: true),
			#if GMOD_DLL
			new("sdn/gamemodes", "garrysmod/gamemodes", IsDirectory: true),
			new("sdn/lua", "garrysmod/lua", IsDirectory: true),
			#endif
			new("sdn/cache/workshop", "garrysmod/cache/workshop", IsDirectory: true, Optional: false),
			new("sdn/settings", "garrysmod/settings", IsDirectory: true)
		];

		string[] specificGmodVpks = ["dir", "000", "001", "002"];
		foreach (var suffix in specificGmodVpks)
			list.Add(new($"sdn/garrysmod_{suffix}.vpk", $"garrysmod/garrysmod_{suffix}.vpk"));

		return list;
	}

	public static List<AssetMapping> GetOptionalAssets() {
		return [
			new("sdn/cfg/autoexec.cfg", "garrysmod/cfg/autoexec.cfg", Optional: true)
		];
	}

	public static string? FindGarrysModPath() {
		string? steamPath = null;
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
			steamPath = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string;
			if (string.IsNullOrEmpty(steamPath)) {
				steamPath = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432NODE\Valve\Steam", "InstallPath", null) as string;
			}
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
			string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			string[] possiblePaths = [
				Path.Combine(home, ".local", "share", "Steam"),
				Path.Combine(home, ".steam", "steam")
			];

			foreach (var p in possiblePaths) {
				if (Directory.Exists(p)) {
					steamPath = p;
					break;
				}
			}
		}

		if (string.IsNullOrEmpty(steamPath))
			return null;

		string libraryFolders = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
		if (!File.Exists(libraryFolders))
			return null;

		try {
			string content = File.ReadAllText(libraryFolders);
			return ParseLibraryFoldersForApp(content, GModAppID);
		}
		catch {
			return null;
		}
	}

	private static string? ParseLibraryFoldersForApp(string vdfContent, int appId) {
		int index = 0;
		while (true) {
			int pathKey = vdfContent.IndexOf("\"path\"", index, StringComparison.OrdinalIgnoreCase);
			if (pathKey == -1) break;

			int pathStart = vdfContent.IndexOf('"', pathKey + 6);
			int pathEnd = vdfContent.IndexOf('"', pathStart + 1);
			if (pathStart == -1 || pathEnd == -1) break;

			string path = vdfContent.Substring(pathStart + 1, pathEnd - pathStart - 1).Replace("\\\\", "\\");

			int appsKey = vdfContent.IndexOf("\"apps\"", pathEnd, StringComparison.OrdinalIgnoreCase);
			if (appsKey != -1) {
				int appsBlockStart = vdfContent.IndexOf('{', appsKey);
				string appToken = $"\"{appId}\"";
				int appIndex = vdfContent.IndexOf(appToken, appsBlockStart, StringComparison.OrdinalIgnoreCase);

				int nextPath = vdfContent.IndexOf("\"path\"", appsBlockStart, StringComparison.OrdinalIgnoreCase);

				if (appIndex != -1 && (nextPath == -1 || appIndex < nextPath)) {
					return Path.Combine(path, GModLocalPath);
				}
			}

			index = pathEnd;
		}

		return null;
	}

	public static bool IsAssetLinked(string localRelativePath, string projectRoot) {
		string fullPath = Path.Combine(projectRoot, localRelativePath);
		if (Directory.Exists(fullPath))
			return new DirectoryInfo(fullPath).LinkTarget != null;
		return File.Exists(fullPath);
	}

	public static bool UnlinkAsset(string localRelativePath, string projectRoot) {
		try {
			string fullLocalPath = Path.Combine(projectRoot, localRelativePath);
			if (File.Exists(fullLocalPath)) {
				File.Delete(fullLocalPath);
				return true;
			}
			if (Directory.Exists(fullLocalPath)) {
				Directory.Delete(fullLocalPath, false);
				return true;
			}
			return false;
		}
		catch {
			return false;
		}
	}

	public static bool BatchLinkAssets(List<(string Local, string Remote, bool IsDirectory)> assets) {
		foreach (var asset in assets) {
			string? dir = Path.GetDirectoryName(asset.Local);
			if (dir != null && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			if (File.Exists(asset.Local))
				File.Delete(asset.Local);
			else if (asset.IsDirectory && Directory.Exists(asset.Local)) {
				if (new DirectoryInfo(asset.Local).LinkTarget == null)
					Directory.Move(asset.Local, $"{asset.Local}_bak");
				else
					Directory.Delete(asset.Local, false);
			}
		}

		try {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				string batchFile = Path.Combine(Path.GetTempPath(), $"sdn_link_{Guid.NewGuid()}.bat");
				using (var writer = File.CreateText(batchFile)) {
					writer.WriteLine("@echo off");
					foreach (var asset in assets) {
						writer.WriteLine($"mklink {(asset.IsDirectory ? "/D " : "")}\"{asset.Local}\" \"{asset.Remote}\"");
					}
				}

				var startInfo = new ProcessStartInfo {
					FileName = "cmd.exe",
					Arguments = $"/c \"{batchFile}\"",
					UseShellExecute = true,
					Verb = "runas",
					WindowStyle = ProcessWindowStyle.Hidden
				};
				var proc = Process.Start(startInfo);
				if (proc == null) return false;
				proc.WaitForExit();

				if (File.Exists(batchFile))
					File.Delete(batchFile);
			}
			else {
				foreach (var asset in assets) {
					var startInfo = new ProcessStartInfo {
						FileName = "ln",
						Arguments = $"-s \"{asset.Remote}\" \"{asset.Local}\"",
						UseShellExecute = false,
						CreateNoWindow = true
					};
					var proc = Process.Start(startInfo);
					proc?.WaitForExit();
				}
			}
			return true;
		}
		catch (Exception e) {
			Console.WriteLine($"BatchLinkAssets failed: {e.Message}");
			return false;
		}
	}

	public static string FindProjectRoot() {
		string? currentDir = AppDomain.CurrentDomain.BaseDirectory;
		while (!string.IsNullOrEmpty(currentDir)) {
			if (File.Exists(Path.Combine(currentDir, "Source.NET.slnx")))
				return currentDir;

			currentDir = Directory.GetParent(currentDir)?.FullName;
		}
		return AppDomain.CurrentDomain.BaseDirectory!;
	}

	public static void LinkAllAssets(string projectRoot, bool requireRestart = false) =>
		LinkAssets(projectRoot, GetRequiredAssets(), requireRestart);

	public static void LinkAssets(string projectRoot, IEnumerable<AssetMapping> mappings, bool requireRestart = false) {
		string? gmodPath = FindGarrysModPath();
		if (gmodPath == null)
			return;

		List<(string Local, string Remote, bool IsDirectory)> assets = [];

		foreach (var asset in mappings) {
			if (IsAssetLinked(asset.LocalPath, projectRoot))
				continue;

			string fullLocalPath = Path.Combine(projectRoot, asset.LocalPath);
			string remotePath = Path.Combine(gmodPath, asset.RemotePath);
			assets.Add((fullLocalPath, remotePath, asset.IsDirectory));
		}

		if (assets.Count > 0) {
			if (BatchLinkAssets(assets)) {
				if (requireRestart) {
					List<(string Local, string Remote, bool IsDirectory)> outputAssets = [];
					foreach (var (local, remote, isDir) in assets) {
						var relative = Path.GetRelativePath(projectRoot, local);
						var dest = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relative);
						outputAssets.Add((dest, remote, isDir));
					}
					BatchLinkAssets(outputAssets);
				}
			}
		}
	}
}

public class AssetLinker : Frame
{
	private ListPanel ListPanel;
	private Button LinkButton;
	private Button UnlinkAllButton;
	private Button LinkSelectedButton;
	private Button UnlinkSelectedButton;
	private Button RefreshButton;
	private Button CloseButton;

	private readonly List<AssetUtils.AssetMapping> AssetsStringList;
	private readonly string ProjectRoot;

	public AssetLinker() : base(null, "AssetLinker") {
		ProjectRoot = AppDomain.CurrentDomain.BaseDirectory;

		SetTitle("Asset Linker", true);
		SetSize(620, 400);
		SetMoveable(true);
		SetSizeable(true);
		SetVisible(true);

		ListPanel = new ListPanel(this, "AssetList");
		ListPanel.AddColumnHeader(0, "local", "Local Path", 250, ListPanel.ColumnFlags.ResizeWithWindow);
		ListPanel.AddColumnHeader(1, "remote", "Remote Target", 250, ListPanel.ColumnFlags.ResizeWithWindow);
		ListPanel.AddColumnHeader(2, "status", "Status", 80, 0);

		LinkButton = new Button(this, "LinkButton", "Link All", this, "LinkAll");
		UnlinkAllButton = new Button(this, "UnlinkAllButton", "Unlink All", this, "UnlinkAll");
		LinkSelectedButton = new Button(this, "LinkSelectedButton", "Link Selected", this, "LinkSelected");
		UnlinkSelectedButton = new Button(this, "UnlinkSelectedButton", "Unlink Selected", this, "UnlinkSelected");
		RefreshButton = new Button(this, "RefreshButton", "Refresh", this, "Refresh");
		CloseButton = new Button(this, "CloseButton", "Close", this, "Close");

		AssetsStringList = [.. AssetUtils.GetRequiredAssets(), .. AssetUtils.GetOptionalAssets()];

		RefreshList();
		UpdateButtons();
	}

	public override void PerformLayout() {
		base.PerformLayout();
		int wide = GetWide();
		int tall = GetTall();

		ListPanel.SetBounds(10, 30, wide - 20, tall - 70);

		int buttonHeight = 24;
		int buttonY = tall - 35;

		LinkSelectedButton.SetBounds(10, buttonY, 110, buttonHeight);
		UnlinkSelectedButton.SetBounds(130, buttonY, 110, buttonHeight);
		RefreshButton.SetBounds(250, buttonY, 80, buttonHeight);

		LinkButton.SetBounds(wide - 270, buttonY, 80, buttonHeight);
		UnlinkAllButton.SetBounds(wide - 180, buttonY, 80, buttonHeight);
		CloseButton.SetBounds(wide - 90, buttonY, 80, buttonHeight);
	}

	public override void OnThink() {
		base.OnThink();
		UpdateButtons();
	}

	private void UpdateButtons() {
		bool anyMissing = false;
		bool anyLinked = false;

		foreach (var asset in AssetsStringList) {
			if (AssetUtils.IsAssetLinked(asset.LocalPath, ProjectRoot))
				anyLinked = true;
			else
				anyMissing = true;
		}

		LinkButton.SetEnabled(anyMissing);
		UnlinkAllButton.SetEnabled(anyLinked);

		bool canLinkSelected = false;
		bool canUnlinkSelected = false;

		int selectedCount = ListPanel.GetSelectedItemsCount();
		if (selectedCount > 0) {
			int selectedItem = ListPanel.GetSelectedItem(0);
			KeyValues? data = ListPanel.GetItem(selectedItem);
			if (data != null) {
				string status = data.GetString("status", "").ToString();
				canLinkSelected = status != "Linked";
				canUnlinkSelected = status == "Linked";
			}
		}

		LinkSelectedButton.SetEnabled(canLinkSelected);
		UnlinkSelectedButton.SetEnabled(canUnlinkSelected);
	}

	public override void OnCommand(ReadOnlySpan<char> command) {
		if (command.SequenceEqual("LinkAll"))
			LinkAllAssets();
		else if (command.SequenceEqual("UnlinkAll"))
			UnlinkAllAssets();
		else if (command.SequenceEqual("LinkSelected"))
			LinkSelectedAsset();
		else if (command.SequenceEqual("UnlinkSelected"))
			UnlinkSelectedAsset();
		else if (command.SequenceEqual("Refresh"))
			RefreshList();
		else if (command.SequenceEqual("Close"))
			Close();
		else
			base.OnCommand(command);
	}

	private void LinkSelectedAsset() {
		if (ListPanel.GetSelectedItemsCount() == 0)
			return;

		int selectedItem = ListPanel.GetSelectedItem(0);
		if (selectedItem == -1)
			return;

		KeyValues? data = ListPanel.GetItem(selectedItem);
		if (data == null)
			return;

		string localPath = data.GetString("local", "").ToString();
		string remotePath = data.GetString("remote", "").ToString();
		bool isDir = data.GetInt("isdir") != 0;

		if (string.IsNullOrEmpty(localPath) || string.IsNullOrEmpty(remotePath) || remotePath == "Not Found")
			return;

		string relPath = Path.GetRelativePath(ProjectRoot, localPath);

		if (AssetUtils.IsAssetLinked(relPath, ProjectRoot))
			return;

		AssetUtils.BatchLinkAssets([(localPath, remotePath, isDir)]);
		RefreshList();
	}

	private void UnlinkSelectedAsset() {
		if (ListPanel.GetSelectedItemsCount() == 0)
			return;

		int selectedItem = ListPanel.GetSelectedItem(0);
		if (selectedItem == -1)
			return;

		KeyValues? data = ListPanel.GetItem(selectedItem);
		if (data == null)
			return;

		string localPath = data.GetString("local", "").ToString();
		if (string.IsNullOrEmpty(localPath))
			return;

		string relPath = Path.GetRelativePath(ProjectRoot, localPath);

		if (!AssetUtils.IsAssetLinked(relPath, ProjectRoot))
			return;

		AssetUtils.UnlinkAsset(relPath, ProjectRoot);
		RefreshList();
	}

	private void RefreshList() {
		string? selectedPath = null;
		if (ListPanel.GetSelectedItemsCount() > 0) {
			int selectedItem = ListPanel.GetSelectedItem(0);
			KeyValues? data = ListPanel.GetItem(selectedItem);
			if (data != null)
				selectedPath = data.GetString("local", "").ToString();
		}

		ListPanel.DeleteAllItems();

		string? gmodPath = AssetUtils.FindGarrysModPath();

		foreach (var asset in AssetsStringList) {
			var kv = new KeyValues("item");
			string fullLocalPath = Path.Combine(ProjectRoot, asset.LocalPath);
			kv.SetString("local", fullLocalPath);
			kv.SetString("remote", gmodPath != null ? Path.Combine(gmodPath, asset.RemotePath) : "Not Found");
			kv.SetInt("isdir", asset.IsDirectory ? 1 : 0);

			bool isLinked = AssetUtils.IsAssetLinked(asset.LocalPath, ProjectRoot);
			kv.SetString("status", isLinked ? "Linked" : "Missing");

			int itemID = ListPanel.AddItem(kv, 0, false, false);

			if (selectedPath != null && fullLocalPath == selectedPath)
				ListPanel.AddSelectedItem(itemID);
		}

		UpdateButtons();
	}

	private void LinkAllAssets() {
		string? gmodPath = AssetUtils.FindGarrysModPath();
		if (gmodPath == null)
			return;

		AssetUtils.LinkAssets(ProjectRoot, AssetsStringList);
		RefreshList();
	}

	private void UnlinkAllAssets() {
		foreach (var asset in AssetsStringList) {
			if (!AssetUtils.IsAssetLinked(asset.LocalPath, ProjectRoot))
				continue;

			AssetUtils.UnlinkAsset(asset.LocalPath, ProjectRoot);
		}

		RefreshList();
	}

	public static void CheckRequired() => AssetUtils.CheckRequired();

	private static AssetLinker? Linker;
	[ConCommand("sdn_assets")]
	public static void sdn_assets() {
		if (Linker != null) {
			Linker.SetVisible(true);
			Linker.MoveToFront();
			return;
		}

		Linker = new AssetLinker();
		Linker.Activate();
	}
}
