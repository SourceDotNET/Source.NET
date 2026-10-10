// #define DEBUG_ADDONS

using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.FileSystem;

using Steamworks;

using System.Runtime.InteropServices;

namespace Source.Filesystem.GarrysMod;

public struct MountedAddon
{
	public string Path;
	public string Title;
	public IFileHandle? FileHandle;
	public ulong WorkshopID;
	public bool ServerDownloaded;
}

public struct SearchFile
{
	public string FileName;
	public bool Folder;
}

public class AddonFileSystem : Addon.FileSystem
{
#if DEBUG_ADDONS
	internal static readonly ConVar fs_tellmeyoursecrets = new("fs_tellmeyoursecrets", "2", 0, "0:Off, 1:On, 2:Extra");
#else
	internal static readonly ConVar fs_tellmeyoursecrets = new("fs_tellmeyoursecrets", "0", 0, "0:Off, 1:On, 2:Extra");
#endif

	const string ContentPrefix = "content/4000/";
	const uint LegacyCreatedCutoff = 1580421600;

	readonly SortedDictionary<string, SortedDictionary<string, AddonFileInfo>> Folders = new(StringComparer.Ordinal);
	readonly LinkedList<MountedAddon> MountedAddons = [];
	string ModPath = "";

	readonly LinkedList<IAddonSystem.Information> Addons = [];
	readonly LinkedList<IAddonSystem.UGCInfo> UgcAddons = [];
	readonly LinkedList<SteamUGCDetails_t> Subscriptions = [];
	readonly SortedDictionary<ulong, bool> AddonShouldMount = [];
	bool Changed;
	static bool Loaded;
	static IAddonSystem.Information FileOwner;

	readonly LinkedList<Addon.Job.Base> Jobs = [];
	Addon.Job.Base? CurrentJob;

	IAddonDownloadNotification? DownloadNotify;
	Callback<RemoteStoragePublishedFileSubscribed_t>? CallbackSubscribed;
	Callback<RemoteStoragePublishedFileUnsubscribed_t>? CallbackUnsubscribed;

	static bool Secrets => fs_tellmeyoursecrets.GetInt() != 0;
	static bool ExtraSecrets => fs_tellmeyoursecrets.GetInt() > 1;

	public void Clear() {
		Folders.Clear();

		foreach (MountedAddon addon in MountedAddons) {
			if (addon.FileHandle != null) {
				AddonFileHandle.OnPackFileUnmounted(addon.FileHandle);
				addon.FileHandle.Dispose();
			}
		}

		MountedAddons.Clear();
	}

	public void Refresh() {
		UpdateModPath();

		int mounted = 0;
		for (LinkedListNode<IAddonSystem.Information>? node = Addons.First; node != null; node = node.Next) {
			ref IAddonSystem.Information info = ref node.ValueRef;
			if (!string.IsNullOrEmpty(info.Failure))
				continue;

			if (!MountAddon(ref info)) {
				if (info.Legacy && info.File.StartsWith("cache/", StringComparison.Ordinal)) {
					Warning($"Removing bad addon {info.File}\n\n");
					g_FullFileSystem.RemoveFile(info.File, "MOD");
				}
			}
			else if (ShouldMount(info.WorkshopID))
				mounted++;
		}

		Msg($"Mounted {mounted} of {Addons.Count} workshop addons!\n");
		Changed = false;
	}

	public bool MountFile(ReadOnlySpan<char> file, List<string>? files, ulong workshopID, ulong unk1, int unk2) {
		string path = new(file);

		string lowerPath = path;
		Bootil.String.Lower(ref lowerPath);
		if (Bootil.String.Test.StartsWith(lowerPath, ContentPrefix) && lowerPath.Length > ContentPrefix.Length) {
			int slash = lowerPath.IndexOf('/', ContentPrefix.Length);
			if (slash > ContentPrefix.Length) {
				ulong contentWorkshopID = Bootil.String.To.UInt64(path.AsSpan(ContentPrefix.Length, slash - ContentPrefix.Length));
				if (contentWorkshopID != 0) {
					path = GetAddonFilepath(contentWorkshopID, false);
					if (workshopID == 0)
						workshopID = contentWorkshopID;
				}
			}
		}

		string lowerMountPath = path;
		Bootil.String.Lower(ref lowerMountPath);

		bool notServerMounted = false;
		foreach (MountedAddon mounted in MountedAddons) {
			if (!Bootil.String.Test.EndsWith(lowerMountPath, mounted.Path) && lowerMountPath != mounted.Path)
				continue;

			notServerMounted = !mounted.ServerDownloaded;
			if (unk2 == 1) {
				Msg($"Addon '{path}' is already mounted, ignoring...\n");
				return true;
			}

			break;
		}

		AddonReader reader = new(unk1);
		if (!reader.OpenFile(path)) {
			Warning($"Couldn't mount file [{path}]\n");
			return false;
		}

		IFileHandle? packFile = reader.GetPackFile();
		if (packFile == null) {
			Warning($"Couldn't mount file [{path}] (invalid pack file)\n");
			return false;
		}

		bool serverDownloaded = !notServerMounted && (unk2 == 1 || unk2 == 2);

		MountedAddons.AddLast(new MountedAddon() {
			Path = path,
			Title = reader.GetTitle(),
			FileHandle = packFile,
			WorkshopID = workshopID,
			ServerDownloaded = serverDownloaded
		});

		for (int i = 0; i < reader.GetNumFiles(); i++) {
			ref readonly AddonFormat.FileEntry entry = ref reader.GetFile(i);

			string folderName = entry.Name;
			Bootil.String.File.FixSlashes(ref folderName);
			Bootil.String.File.StripFilename(ref folderName);

			SortedDictionary<string, AddonFileInfo> folder = GetFolder(folderName, true)!;
			AddFolder(folderName, packFile, path, serverDownloaded);

			string fileName = entry.Name;
			Bootil.String.File.ExtractFilename(ref fileName);

			if (folder.TryGetValue(fileName, out AddonFileInfo existing) && existing.WorkshopID != workshopID && Bootil.String.Test.EndsWith(entry.Name, ".lua"))
				Warning($"Addon '{reader.GetTitle()}' ({workshopID}) contains file from {existing.WorkshopID}: '{entry.Name}'\n");

			folder[fileName] = new AddonFileInfo() {
				FileName = fileName,
				FolderName = folderName,
				Size = entry.Size,
				Offset = entry.Offset,
				FileHandle = packFile,
				Folder = false,
				ServerDownloaded = serverDownloaded,
				WorkshopID = workshopID
			};

			files?.Add(folderName + fileName);
		}

		reader.ExtractFiles();
		return true;
	}

	public bool ShouldMount(ulong workshopID) {
		Load();

		if (workshopID != 0 && AddonShouldMount.TryGetValue(workshopID, out bool shouldMount))
			return shouldMount;

		return true;
	}

	public void SetShouldMount(ulong workshopID, bool shouldMount) => AddonShouldMount[workshopID] = shouldMount;

	public void Save() {
		KeyValues kv = new("addonnomount");

		foreach (KeyValuePair<ulong, bool> entry in AddonShouldMount) {
			if (entry.Value)
				continue;

			string workshopID = entry.Key.ToString();
			kv.FindKey(workshopID, true)!.SetStringValue(workshopID);
		}

		kv.WriteToFile(g_FullFileSystem, "cfg/addonnomount.txt", "DEFAULT_WRITE_PATH");
	}

	public LinkedList<IAddonSystem.Information> GetList() => Addons;

	public LinkedList<IAddonSystem.UGCInfo> GetUGCList() => UgcAddons;

	public void ScanForSubscriptions(ReadOnlySpan<char> unk1, bool unk2) {
		if (BaseFileSystem.get == null)
			Error("SFS: !get");

		if (BaseFileSystem.get!.IsDedicatedServer()) {
			DedicatedServerAddons.RunAddonProcess(unk1, unk2);
			MountFloatingAddons();
			return;
		}

		CallbackSubscribed?.Unregister();
		CallbackSubscribed = Callback<RemoteStoragePublishedFileSubscribed_t>.Create(OnRemoteStoragePublishedFileSubscribed);
		CallbackUnsubscribed?.Unregister();
		CallbackUnsubscribed = Callback<RemoteStoragePublishedFileUnsubscribed_t>.Create(OnRemoteStoragePublishedFileUnsubscribed);

		AddJob(new AddonTasks.AddFloatingAddons());
		AddJob(new AddonTasks.GetSubscriptions());
		AddJob(new AddonTasks.MountAvailable());

		if (!BaseFileSystem.get.IsDedicatedServer() && SteamUser.BLoggedOn())
			AddJob(new AddonTasks.DownloadAddons(false));

		Think();
	}

	void OnRemoteStoragePublishedFileSubscribed(RemoteStoragePublishedFileSubscribed_t info) {
		if (BaseFileSystem.get.IsDedicatedServer() || !SteamUser.BLoggedOn())
			return;

		if (info.m_nAppID != SteamUtils.GetAppID())
			return;

		if (!IsSubscribed(info.m_nPublishedFileId.m_PublishedFileId))
			AddJob(new AddonTasks.OnSubscribed(info.m_nPublishedFileId.m_PublishedFileId));
	}

	void OnRemoteStoragePublishedFileUnsubscribed(RemoteStoragePublishedFileUnsubscribed_t info) {
		if (BaseFileSystem.get.IsDedicatedServer() || !SteamUser.BLoggedOn())
			return;

		if (info.m_nAppID != SteamUtils.GetAppID())
			return;

		ulong workshopID = info.m_nPublishedFileId.m_PublishedFileId;
		SetShouldMount(workshopID, true);

		for (LinkedListNode<IAddonSystem.Information>? node = Addons.First; node != null; node = node.Next) {
			if (node.Value.WorkshopID != workshopID)
				continue;

			for (LinkedListNode<MountedAddon>? mounted = MountedAddons.First; mounted != null; mounted = mounted.Next) {
				if (mounted.Value.Path != node.Value.File)
					continue;

				Warning($"Unmounting (addon unsubscribed) '{node.Value.File}'\n");
				UnmountPackFile(mounted.Value.FileHandle);
				MountedAddons.Remove(mounted);
				break;
			}

			if (node.Value.Legacy)
				g_FullFileSystem.RemoveFile(node.Value.File, "MOD");

			Addons.Remove(node);
			MarkChanged();
			AddJob(new AddonTasks.MountAvailable());
			Notify()?.NotifySubscriptionChanges();
			return;
		}

		for (LinkedListNode<IAddonSystem.UGCInfo>? node = UgcAddons.First; node != null; node = node.Next) {
			if (node.Value.WorkshopID != workshopID)
				continue;

			UgcAddons.Remove(node);
			break;
		}

		Notify()?.NotifySubscriptionChanges();
	}

	public void Think() {
		if (Jobs.Count == 0) {
			if (Subscriptions.Count == 0)
				return;

			int count = 0;
			foreach (SteamUGCDetails_t details in Subscriptions) {
				AddAddonFromSteamDetails(in details);
				count++;
			}

			Subscriptions.Clear();
			AddJob(new AddonTasks.DownloadAddons(true));

			if (count > 0)
				Notify()?.NotifySubscriptionChanges();
			return;
		}

		Addon.Job.Base front = Jobs.First!.Value;
		if (CurrentJob != front) {
			CurrentJob = front;
			front.Start();
		}

		CurrentJob.Cycle();
		if (CurrentJob.Finished()) {
			Jobs.RemoveFirst();
			CurrentJob = null;
			Think();
		}
	}

	public void SetDownloadNotify(IAddonDownloadNotification? unk1) => DownloadNotify = unk1;

	public IAddonDownloadNotification? Notify() => DownloadNotify;

	public bool IsSubscribed(ulong workshopID) {
		foreach (IAddonSystem.Information info in Addons)
			if (info.WorkshopID == workshopID)
				return true;

		foreach (IAddonSystem.UGCInfo info in UgcAddons)
			if (info.WorkshopID == workshopID)
				return true;

		return false;
	}

	public bool FindFileOwner(ReadOnlySpan<char> unk1, out IAddonSystem.Information info) {
		string fileName = new(unk1);
		NormalizePath(ref fileName);

		string folderName = fileName;
		Bootil.String.File.StripFilename(ref folderName);

		info = default;
		SortedDictionary<string, AddonFileInfo>? folder = GetFolder(folderName, false);
		if (folder == null || folder.Count == 0)
			return false;

		bool found = false;
		foreach (AddonFileInfo entry in folder.Values) {
			if (fileName != entry.FolderName + entry.FileName)
				continue;

			foreach (IAddonSystem.Information addon in Addons) {
				if (addon.WorkshopID != entry.WorkshopID)
					continue;

				if (found) {
					if (addon.WorkshopID != 0)
						return false;

					if (info.File == addon.File)
						return false;
				}

				info = addon;
				found = true;
			}

			if (!found) {
				foreach (MountedAddon mounted in MountedAddons) {
					if (mounted.FileHandle != entry.FileHandle)
						continue;

					FileOwner = default;
					FileOwner.Title = mounted.Title;
					FileOwner.File = mounted.Path;
					FileOwner.Tags = "";
					FileOwner.Failure = "";
					FileOwner.WorkshopID = mounted.WorkshopID;
					info = FileOwner;
					return true;
				}
			}
		}

		return found;
	}

	public void AddAddon(in IAddonSystem.Information info) {
		LinkedListNode<IAddonSystem.Information>? next;
		if (info.WorkshopID != 0) {
			for (LinkedListNode<IAddonSystem.Information>? node = Addons.First; node != null; node = node.Next) {
				if (node.Value.WorkshopID != info.WorkshopID)
					continue;

				Warning($"Replacing existing addon '{info.WorkshopID}'!\n");
				next = node.Next;
				Addons.Remove(node);
				if (next == null)
					break;

				Addons.AddBefore(next, info);
				return;
			}
		}

		Addons.AddLast(info);
	}

	public void ClearUnusedGMAs() => AddJob(new AddonTasks.ClearUnusedGMAs());

	public string GetAddonFilepath(ulong workshopID, bool unk1) {
		if (!SteamUGC.GetItemInstallInfo(new PublishedFileId_t(workshopID), out _, out string folder, MAX_PATH, out _))
			return "";

		ReadOnlySpan<char> file = g_FullFileSystem.FindFirstEx($"{folder}/*.*", "", out ulong findHandle);
		while (!file.IsEmpty) {
			string ext = Bootil.String.File.GetFileExtension(new string(file));
			if (ext == "gma" || !unk1) {
				if (ext == "gma" || ext == "dupe" || ext == "gms" || ext == "dem") {
					string result = $"{folder}/{file}";
					g_FullFileSystem.FindClose(findHandle);
					return result;
				}
			}

			file = g_FullFileSystem.FindNext(findHandle);
		}

		g_FullFileSystem.FindClose(findHandle);
		return "";
	}

	public void UnmountAddon(ulong workshopID, ReadOnlySpan<char> reason) {
		for (LinkedListNode<MountedAddon>? node = MountedAddons.First; node != null; node = node.Next) {
			if (node.Value.WorkshopID != workshopID || workshopID == 0)
				continue;

			Warning($"Unmounting ({reason}) '{node.Value.Path}'\n");
			UnmountPackFile(node.Value.FileHandle);
			MountedAddons.Remove(node);
			return;
		}
	}

	public void UnmountServerAddons() {
		LinkedListNode<MountedAddon>? node = MountedAddons.First;
		while (node != null) {
			LinkedListNode<MountedAddon>? next = node.Next;
			if (node.Value.ServerDownloaded) {
				Warning($"Unmounting server downloaded addon '{node.Value.Path}'\n");
				UnmountPackFile(node.Value.FileHandle);
				MountedAddons.Remove(node);
			}
			node = next;
		}

		if (HasChanges())
			Refresh();
	}

	public string IsAddonValidPreInstall(in SteamUGCDetails_t details) {
		if (details.m_bBanned)
			return "Addon is banned";

		if (details.m_eFileType != EWorkshopFileType.k_EWorkshopFileTypeCommunity)
			return "Bad workshop file type";

		if (details.m_rgchTitle.Length < 1)
			return "Addon is hidden, banned or doesn't exist";

		if (details.m_nConsumerAppID.m_AppId != GetSteamInfIDVersionInfo().AppID)
			return "Bad consumer AppID";

		if ((SteamUGC.GetItemState(details.m_nPublishedFileId) & (uint)EItemState.k_EItemStateLegacyItem) != 0 && details.m_rtimeCreated > LegacyCreatedCutoff)
			return "Addon too new to use old API";

		return "";
	}

	public bool AllJobsFinished() => Jobs.Count == 0;

	public void Shutdown() => Clear();

	public void AddJob(Addon.Job.Base job) {
		job.Init(this);
		Jobs.AddLast(job);
	}

	public LinkedList<SteamUGCDetails_t> GetSubList() => Subscriptions;

	public void MountFloatingAddons() {
		ReadOnlySpan<char> file = g_FullFileSystem.FindFirstEx("*.gma", "GAME", out ulong findHandle);
		while (!file.IsEmpty) {
			string fileName = new(file.SliceNullTerminatedString());

			Span<char> fullPathBuffer = stackalloc char[MAX_PATH];
			ReadOnlySpan<char> fullPath = g_FullFileSystem.RelativePathToFullPath(fileName, "GAME", fullPathBuffer);

			Span<char> relativeBuffer = stackalloc char[MAX_PATH];
			bool converted = !fullPath.IsEmpty && g_FullFileSystem.FullPathToRelativePath(fullPath, relativeBuffer);
			string relative = converted ? new string(((ReadOnlySpan<char>)relativeBuffer).SliceNullTerminatedString()) : "";
			if (converted) {
				Bootil.String.File.FixSlashes(ref relative, "/", "\\");
				if (relative.StartsWith("garrysmod\\", StringComparison.Ordinal))
					relative = relative["garrysmod\\".Length..];
			}

			if (fullPath.IsEmpty || !converted)
				Msg($"Failed to mount floating addon '{fileName}'\n");
			else {
				Msg($"Mounting Floating Addon '{relative}'\n");

				uint time = (uint)new DateTimeOffset(g_FullFileSystem.GetFileTime(fileName, "GAME")).ToUnixTimeSeconds();
				AddAddon(new IAddonSystem.Information() {
					Title = fileName,
					File = relative,
					Tags = "",
					Failure = "",
					TimeUpdated = time,
					Size = (uint)g_FullFileSystem.Size(fileName, "GAME"),
					TimeAdded = time,
					Downloaded = true,
					Legacy = true
				});
			}

			file = g_FullFileSystem.FindNext(findHandle);
		}

		g_FullFileSystem.FindClose(findHandle);
	}

	public void AddAddonFromSteamDetails(in SteamUGCDetails_t details) {
		for (LinkedListNode<IAddonSystem.Information>? node = Addons.First; node != null; node = node.Next) {
			if (node.Value.WorkshopID == details.m_nPublishedFileId.m_PublishedFileId && node.Value.Unloaded) {
				Addons.Remove(node);
				break;
			}
		}

		Addon.AddonType type = GetAddonType(in details);
		if (type == Addon.AddonType.Dupe || type == Addon.AddonType.Save || type == Addon.AddonType.Demo) {
			AddUGCFile(in details, type);
			return;
		}

		if (type == Addon.AddonType.Unknown)
			Warning($"'{details.m_rgchTitle}' is missing Addon, ServerContent, Dupe, Demo or Save tag!\n");

		string failure = IsAddonValidPreInstall(in details);
		if (failure.Length != 0)
			Warning($"Error! Refusing to load addon '{details.m_rgchTitle}' ({details.m_nPublishedFileId.m_PublishedFileId})! {failure}!\n");

		uint itemState = SteamUGC.GetItemState(details.m_nPublishedFileId);
		if ((itemState & (uint)(EItemState.k_EItemStateInstalled | EItemState.k_EItemStateNeedsUpdate)) == (uint)EItemState.k_EItemStateInstalled) {
			if (SteamUGC.GetItemInstallInfo(details.m_nPublishedFileId, out _, out _, MAX_PATH, out uint timeStamp) && timeStamp != details.m_rtimeUpdated)
				itemState |= (uint)EItemState.k_EItemStateNeedsUpdate;
		}

		string legacyPath = $"cache/workshop/{details.m_nPublishedFileId.m_PublishedFileId}.gma";
		if ((itemState & (uint)EItemState.k_EItemStateLegacyItem) == 0)
			legacyPath = "";

		foreach (IAddonSystem.Information addon in Addons) {
			if (addon.Legacy && addon.File == legacyPath && !addon.Unloaded) {
				Warning($"Tried to add duplicate file from '{details.m_rgchTitle}' ({details.m_nPublishedFileId.m_PublishedFileId}) which is already mounted from '{addon.Title}' ({addon.WorkshopID})\n");
				return;
			}
		}

		IAddonSystem.Information info = new() {
			Title = details.m_rgchTitle,
			File = legacyPath,
			Tags = details.m_rgchTags,
			Failure = failure,
			TimeUpdated = details.m_rtimeUpdated,
			Models = 0,
			WorkshopID = details.m_nPublishedFileId.m_PublishedFileId,
			Creator = details.m_ulSteamIDOwner,
			HContentFile = details.m_hFile.m_UGCHandle,
			Size = (uint)details.m_nFileSize,
			HContentPreview = details.m_hPreviewFile.m_UGCHandle,
			TimeAdded = details.m_rtimeAddedToUserList,
			Legacy = (itemState & (uint)EItemState.k_EItemStateLegacyItem) != 0,
			Failed = false,
			CanUpdate = (itemState & (uint)EItemState.k_EItemStateNeedsUpdate) != 0,
			Unloaded = false,
			Downloaded = (itemState & (uint)(EItemState.k_EItemStateInstalled | EItemState.k_EItemStateLegacyItem)) == (uint)EItemState.k_EItemStateInstalled
		};

		if (info.Title.Length == 0)
			info.Title = "Hidden addon #" + info.WorkshopID;

		Bootil.String.Util.FindAndReplace(ref info.Title, "\n", "");
		Bootil.String.Util.FindAndReplace(ref info.Title, "\r", "");

		if (info.Legacy && info.Failure.Length == 0) {
			IFileHandle? file = g_FullFileSystem.Open(legacyPath, FileOpenOptions.Read | FileOpenOptions.Binary, "MOD") ?? g_FullFileSystem.Open(legacyPath, FileOpenOptions.Read | FileOpenOptions.Binary, null);
			if (file != null) {
				info.Downloaded = true;

				Span<byte> timestamp = stackalloc byte[sizeof(uint)];
				timestamp.Clear();
				using (file) {
					file.Stream.Seek(AddonFormat.TimestampOffset, SeekOrigin.Begin);
					file.Stream.ReadAtLeast(timestamp, timestamp.Length, false);
				}

				uint addonTimestamp = BitConverter.ToUInt32(timestamp);
				if (addonTimestamp != info.TimeUpdated) {
					Msg($"Legacy addon update available! [{info.Title}] (US: {addonTimestamp} != THEM: {info.TimeUpdated})\n[{legacyPath}]\n");
					info.CanUpdate = true;
				}

				MarkChanged();
			}
		}

		AddAddon(in info);
	}

	public void OnAddonSubscribed(in SteamUGCDetails_t details) => Subscriptions.AddLast(details);

	public void AddUnloadedSubscription(ulong workshopID) {
		uint itemState = SteamUGC.GetItemState(new PublishedFileId_t(workshopID));
		if ((itemState & (uint)EItemState.k_EItemStateSubscribed) == 0) {
			Warning($"Subscription {workshopID} doesn't have subscribed state?\n");
			return;
		}

		Addon.AddonType type = Addon.AddonType.Addon;
		if ((itemState & (uint)EItemState.k_EItemStateLegacyItem) == 0 && SteamUGC.GetItemInstallInfo(new PublishedFileId_t(workshopID), out _, out string folder, 1024, out _)) {
			ReadOnlySpan<char> file = g_FullFileSystem.FindFirstEx($"{folder}/*.*", "MOD", out ulong findHandle);
			while (!file.IsEmpty) {
				string fileName = new(file);
				if (Bootil.String.Test.EndsWith(fileName, ".gms"))
					type = Addon.AddonType.Save;
				if (Bootil.String.Test.EndsWith(fileName, ".dupe"))
					type = Addon.AddonType.Dupe;
				if (Bootil.String.Test.EndsWith(fileName, ".dem"))
					type = Addon.AddonType.Demo;

				file = g_FullFileSystem.FindNext(findHandle);
			}

			g_FullFileSystem.FindClose(findHandle);

			if (type != Addon.AddonType.Addon) {
				IAddonSystem.UGCInfo ugc = new() {
					Title = "Unknown UGC Addon #" + workshopID,
					Tags = type switch {
						Addon.AddonType.Save => "save",
						Addon.AddonType.Demo => "demo",
						Addon.AddonType.Dupe => "dupe",
						_ => ""
					},
					TimeAdded = 0,
					WorkshopID = workshopID,
					Creator = 0,
					Type = type,
					Unloaded = true
				};

				UgcAddons.AddLast(ugc);
				return;
			}
		}

		AddAddon(new IAddonSystem.Information() {
			Title = "Unknown Addon #" + workshopID,
			File = "",
			Tags = "",
			Failure = "No info about this addon from Steam",
			WorkshopID = workshopID,
			CanUpdate = (itemState & (uint)EItemState.k_EItemStateNeedsUpdate) != 0,
			Downloaded = (itemState & (uint)EItemState.k_EItemStateInstalled) != 0,
			Failed = false,
			Legacy = (itemState & (uint)EItemState.k_EItemStateLegacyItem) != 0,
			Unloaded = true
		});
	}

	public void EnableLoadingUnloadedAddons() {
		for (LinkedListNode<IAddonSystem.Information>? node = Addons.First; node != null; node = node.Next) {
			ref IAddonSystem.Information info = ref node.ValueRef;
			if (info.Failure == "No info about this addon from Steam")
				info.Failure = "";
		}
	}

	public bool HasChanges() => Changed;

	public void MarkChanged() => Changed = true;

	public void OnAddonDownloaded(in IAddonSystem.Information info) {
		MarkChanged();

		for (LinkedListNode<IAddonSystem.Information>? node = Addons.First; node != null; node = node.Next) {
			if (node.Value.WorkshopID != info.WorkshopID)
				continue;

			ref IAddonSystem.Information addon = ref node.ValueRef;
			addon.Legacy = info.Legacy;
			addon.CanUpdate = false;
			addon.Downloaded = true;
		}
	}

	public void OnAddonDownloadFailed(in IAddonSystem.Information info) {
		for (LinkedListNode<IAddonSystem.Information>? node = Addons.First; node != null; node = node.Next) {
			if (node.Value.WorkshopID != info.WorkshopID)
				continue;

			ref IAddonSystem.Information addon = ref node.ValueRef;
			addon.Legacy = info.Legacy;
			addon.Downloaded = false;
			addon.Failed = true;
			addon.CanUpdate = false;
		}
	}

	public void Load() {
		if (Loaded)
			return;

		Loaded = true;

		if (!g_FullFileSystem.FileExists("cfg/addonnomount.txt", "DEFAULT_WRITE_PATH"))
			return;

		KeyValues kv = new("addonnomount");
		if (!kv.LoadFromFile(g_FullFileSystem, "cfg/addonnomount.txt", "DEFAULT_WRITE_PATH"))
			return;

		for (KeyValues? sub = kv.GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
			ulong workshopID = Bootil.String.To.UInt64(sub.GetString());
			if (Addons.Count == 0 || workshopID == 0 || IsSubscribed(workshopID))
				SetShouldMount(workshopID, false);
		}
	}

	void UpdateModPath() {
		ModPath = new string(BaseFileSystem.get.GameDir()) + "\\workshop\\";
		if (Secrets)
			Msg($"Addon[UpdateModPath]: ModPath [{ModPath}]\n");

		Bootil.String.File.FixSlashes(ref ModPath);
		Bootil.String.Lower(ref ModPath);

		if (Secrets)
			Msg($"Addon[UpdateModPath]: Cleaned [{ModPath}]\n");
	}

	public string GetModPath() => ModPath;

	static Addon.AddonType GetAddonType(in SteamUGCDetails_t details) {
		string tags = details.m_rgchTags;
		if (tags.Contains("dupe,", StringComparison.OrdinalIgnoreCase) || tags.Contains(",dupe", StringComparison.OrdinalIgnoreCase))
			return Addon.AddonType.Dupe;
		if (tags.Contains("save,", StringComparison.OrdinalIgnoreCase) || tags.Contains(",save", StringComparison.OrdinalIgnoreCase))
			return Addon.AddonType.Save;
		if (tags.Contains("demo,", StringComparison.OrdinalIgnoreCase) || tags.Contains(",demo", StringComparison.OrdinalIgnoreCase))
			return Addon.AddonType.Demo;
		if (tags.Contains("addon,", StringComparison.OrdinalIgnoreCase) || tags.Contains(",addon", StringComparison.OrdinalIgnoreCase))
			return Addon.AddonType.Addon;
		if (tags.Contains("servercontent,", StringComparison.OrdinalIgnoreCase) || tags.Contains(",servercontent", StringComparison.OrdinalIgnoreCase))
			return Addon.AddonType.ServerContent;
		return Addon.AddonType.Unknown;
	}

	void AddUGCFile(in SteamUGCDetails_t details, Addon.AddonType type) {
		for (LinkedListNode<IAddonSystem.UGCInfo>? node = UgcAddons.First; node != null; node = node.Next) {
			if (node.Value.WorkshopID == details.m_nPublishedFileId.m_PublishedFileId && node.Value.Unloaded) {
				UgcAddons.Remove(node);
				break;
			}
		}

		UgcAddons.AddLast(new IAddonSystem.UGCInfo() {
			Title = details.m_rgchTitle,
			Tags = details.m_rgchTags,
			TimeAdded = details.m_rtimeAddedToUserList,
			WorkshopID = details.m_nPublishedFileId.m_PublishedFileId,
			Creator = details.m_ulSteamIDOwner,
			Type = type,
			Unloaded = false
		});
	}

	SortedDictionary<string, AddonFileInfo>? GetFolder(string path, bool create) {
		string cleanPath = path;
		Bootil.String.File.CleanPath(ref cleanPath);

		if (Folders.TryGetValue(cleanPath, out SortedDictionary<string, AddonFileInfo>? folder))
			return folder;

		if (!create)
			return null;

		folder = new(StringComparer.Ordinal);
		Folders[cleanPath] = folder;
		return folder;
	}

	void AddFolder(string path, IFileHandle packFile, string addonPath, bool serverDownloaded) {
		string parent = path;
		Bootil.String.Util.TrimRight(ref parent, "\\/");
		int slash = parent.LastIndexOfAny(['/', '\\']);
		parent = slash == -1 ? "" : parent[..slash];

		if (parent.Length == 0)
			return;

		GetFolder(parent, true)![addonPath] = new AddonFileInfo() {
			FileName = "",
			FolderName = "",
			FileHandle = packFile,
			Folder = true,
			ServerDownloaded = serverDownloaded
		};

		AddFolder(parent, packFile, addonPath, serverDownloaded);
	}

	bool MountAddon(ref IAddonSystem.Information info) {
		if (!string.IsNullOrEmpty(info.Failure))
			return false;

		string file = info.File;
		if (!info.Downloaded)
			return true;

		if (info.Legacy) {
			string outdated = file + ".outdated";
			if (g_FullFileSystem.FileExists(outdated, "MOD"))
				g_FullFileSystem.RemoveFile(outdated, "MOD");
		}

		if (!ShouldMount(info.WorkshopID))
			return true;

		if (!info.Legacy) {
			file = GetAddonFilepath(info.WorkshopID, true);
			info.File = file;
		}

		if (file.Length == 0) {
			info.Failure = "Missing file";
			Warning($"Addon '{info.Title}' ({info.WorkshopID}) doesn't have a file, nothing to mount...\n");
			BaseFileSystem.get.MenuSystem()?.SendProblemToMenu("missing_addon_file", 2, info.Title);
			return false;
		}

		AddonReader reader = new(info.TimeUpdated);
		if (!reader.OpenFile(file)) {
			info.Failure = "Failed to parse addon file";
			Warning($"Couldn't mount addon file '{file}' from '{info.Title}' ({info.WorkshopID})\n");
			return false;
		}

		if (string.IsNullOrEmpty(info.Title) || info.WorkshopID == 0)
			info.Title = reader.GetTitle();

		info.Models = 0;

		IFileHandle? packFile = reader.GetPackFile();
		if (packFile == null) {
			info.Failure = "Failed to open addon file";
			Warning($"Failed to open addon file '{file}', not mounting!\n");
			return false;
		}

		MountedAddons.AddLast(new MountedAddon() {
			Path = file,
			Title = info.Title,
			FileHandle = packFile,
			WorkshopID = info.WorkshopID,
			ServerDownloaded = false
		});

		for (int i = 0; i < reader.GetNumFiles(); i++) {
			ref readonly AddonFormat.FileEntry entry = ref reader.GetFile(i);

			string folderName = entry.Name;
			Bootil.String.File.FixSlashes(ref folderName);
			Bootil.String.File.StripFilename(ref folderName);

			SortedDictionary<string, AddonFileInfo> folder = GetFolder(folderName, true)!;
			AddFolder(folderName, packFile, info.File, false);

			string fileName = entry.Name;
			Bootil.String.File.ExtractFilename(ref fileName);

			if (Bootil.String.Test.EndsWith(entry.Name, ".mdl"))
				info.Models++;

			if (folder.TryGetValue(fileName, out AddonFileInfo existing) && Bootil.String.Test.EndsWith(entry.Name, ".lua")) {
				if (info.WorkshopID != existing.WorkshopID && entry.Size != existing.Size) {
					IAddonDownloadNotification? notify = Notify();
					if (notify == null)
						Warning($"Addon '{info.Title}' ({info.WorkshopID}) contains file from {existing.WorkshopID}: '{entry.Name}'\n");
					else
						notify.NotifyAddonConflict(info.WorkshopID, existing.WorkshopID, entry.Name);
				}
			}

			folder[fileName] = new AddonFileInfo() {
				FileName = fileName,
				FolderName = folderName,
				Size = entry.Size,
				Offset = entry.Offset,
				FileHandle = packFile,
				Folder = false,
				ServerDownloaded = false,
				WorkshopID = info.WorkshopID
			};
		}

		reader.ExtractFiles();
		return true;
	}

	void UnmountPackFile(IFileHandle? packFile) {
		if (packFile == null)
			return;

		foreach (string folderName in Folders.Keys.ToArray()) {
			SortedDictionary<string, AddonFileInfo> folder = Folders[folderName];
			foreach (KeyValuePair<string, AddonFileInfo> entry in folder.ToArray()) {
				if (entry.Value.FileHandle != packFile)
					continue;

				if (Secrets)
					Msg($"Unmounting '{folderName}/{entry.Value.FileName}'\n");

				folder.Remove(entry.Key);
			}

			if (folder.Count == 0)
				Folders.Remove(folderName);
		}

		AddonFileHandle.OnPackFileUnmounted(packFile);
		packFile.Dispose();
		MarkChanged();
	}

	void NormalizePath(ref string fileName) {
		Bootil.String.File.FixSlashes(ref fileName);
		Bootil.String.Lower(ref fileName);
		Bootil.String.Util.Trim(ref fileName, " \n\t\r");
		Bootil.String.Util.FindAndReplace(ref fileName, "//", "/");

		if (fileName.Length > ModPath.Length && Bootil.String.Test.StartsWith(fileName, ModPath))
			fileName = fileName[ModPath.Length..];
	}

	public bool GetFile(string fileName, out AddonFileInfo info) {
		if (Secrets)
			Msg($"Addon[GetFile]: [{fileName}]\n");

		NormalizePath(ref fileName);
		if (ExtraSecrets)
			Msg($"Addon[GetFile]: Normalized [{fileName}]\n");

		string folderName = fileName;
		Bootil.String.File.StripFilename(ref folderName);

		info = default;
		SortedDictionary<string, AddonFileInfo>? folder = GetFolder(folderName, false);
		if (folder == null || folder.Count == 0) {
			if (ExtraSecrets)
				Msg($"Addon[GetFile]: Dir Not Found [{folderName}]\n");
			return false;
		}

		string name = fileName;
		Bootil.String.File.ExtractFilename(ref name);

		if (folder.TryGetValue(name, out info)) {
			if (ExtraSecrets)
				Msg($"Addon[GetFile]: Found [{name}]\n");
			return true;
		}

		if (ExtraSecrets) {
			Msg($"Addon[GetFile]: File Not Found [{name}]\n");
			foreach (string key in folder.Keys)
				Msg($"Addon[GetFile]: [{key}]\n");
		}

		return false;
	}

	public IFileHandle? GetFileEntry(string fileName) {
		if (!GetFile(fileName, out AddonFileInfo info))
			return null;

		return new AddonFileHandle(in info, g_FullFileSystem.FindOrAddFileName(fileName));
	}

	public string ResolveFile(string fileName) {
		if (!GetFile(fileName, out AddonFileInfo info))
			return "";

		return ModPath + info.FolderName + info.FileName;
	}

	public int GetFileSize(string fileName) {
		if (Secrets)
			Msg($"Addon[GetFileSize]: [{fileName}]\n");

		if (!GetFile(fileName, out AddonFileInfo info))
			return -1;

		if (ExtraSecrets)
			Msg($"Addon[GetFileSize]: Returning [{info.Size}]\n");

		return (int)info.Size;
	}

	public void FindInAddon(string path, string wildcard, List<SearchFile> results) {
		if (Secrets)
			Msg($"Addon[FindInAddon]: [{path}] [{wildcard}]\n");

		foreach (MountedAddon addon in MountedAddons) {
			if (addon.Title != path)
				continue;

			FindFirst(wildcard, results, addon.FileHandle);
		}
	}

	public void FindFirst(string path, List<SearchFile> results, IFileHandle? packFile) {
		if (Secrets)
			Msg($"Addon[FindFirst]: Searching Path [{path}]\n");

		NormalizePath(ref path);
		if (ExtraSecrets)
			Msg($"Addon[FindFirst]: NormalizePath [{path}]\n");

		string dir = path;
		Bootil.String.File.StripFilename(ref dir);
		if (ExtraSecrets)
			Msg($"Addon[FindFirst]: strDir [{dir}]\n");

		foreach (KeyValuePair<string, SortedDictionary<string, AddonFileInfo>> folder in Folders) {
			if (packFile != null) {
				bool hasFileFromPack = false;
				foreach (AddonFileInfo info in folder.Value.Values) {
					if (info.FileHandle == packFile) {
						hasFileFromPack = true;
						break;
					}
				}

				if (!hasFileFromPack)
					continue;
			}

			if (!Bootil.String.Test.Wildcard(path, folder.Key))
				continue;

			if (folder.Key.Length < dir.Length)
				continue;

			string relative = folder.Key[dir.Length..];
			Bootil.String.Util.Trim(ref relative, "/");
			if (relative.Length == 0 || Bootil.String.Util.Count(relative, '/') > 0)
				continue;

			if (ExtraSecrets)
				Msg($"Addon[FindFirst]: Folder Match [{relative}]\n");

			results.Add(new SearchFile() { FileName = relative, Folder = true });
		}

		SortedDictionary<string, AddonFileInfo>? dirFolder = GetFolder(dir, false);
		if (dirFolder == null || dirFolder.Count == 0) {
			if (ExtraSecrets)
				Msg("Addon[FindFirst]: No matching folders or folder contains no files\n");
			return;
		}

		foreach (AddonFileInfo info in dirFolder.Values) {
			if (packFile != null && info.FileHandle != packFile)
				continue;

			string full = info.FolderName + info.FileName;
			if (!Bootil.String.Test.Wildcard(path, full))
				continue;

			if (ExtraSecrets)
				Msg($"Addon[FindFirst]: Adding File [{info.FileName}]\n");

			results.Add(new SearchFile() { FileName = info.FileName, Folder = false });
		}
	}

	public bool IsDirectory(string folderName) {
		if (Secrets)
			Msg($"Addon[IsDirectory]: [{folderName}]\n");

		NormalizePath(ref folderName);
		if (ExtraSecrets)
			Msg($"Addon[IsDirectory]: Normalized [{folderName}]\n");

		SortedDictionary<string, AddonFileInfo>? folder = GetFolder(folderName, false);
		return folder != null && folder.Count != 0;
	}
}
