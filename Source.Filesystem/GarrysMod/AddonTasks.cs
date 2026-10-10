using Bootil.Compression;

using Source.Common.Filesystem;
using Source.Common.GarrysMod;
using Source.FileSystem;

using Steamworks;

namespace Source.Filesystem.GarrysMod;

public class ThreadedUGCAccess
{
	const int MaxFolder = 1024;

	readonly object Mutex = new();
	public readonly ulong ItemID;
	public byte[] Buffer = [];
	bool Done;
	bool Success;

	public ThreadedUGCAccess(ulong itemID) {
		ItemID = itemID;
		new Thread(Run) { IsBackground = true }.Start();
	}

	public bool IsDone() {
		lock (Mutex)
			return Done;
	}

	public bool IsSuccess() {
		lock (Mutex)
			return Success;
	}

	void Run() {
		bool installed = SteamUGC.GetItemInstallInfo(new PublishedFileId_t(ItemID), out ulong diskSize, out string folder, MaxFolder, out _);
		if (diskSize == 0)
			diskSize = (ulong)g_FullFileSystem.Size(folder, "MOD");

		if (!installed) {
			Warning($"Threaded UGC Access failed for {ItemID} (GetItemInstallInfo)\n");
			lock (Mutex)
				Done = true;
			return;
		}

		byte[] buffer;
		try {
			buffer = new byte[diskSize];
		}
		catch {
			Warning($"Couldn't allocate memory for addon {ItemID} (maybe {Bootil.String.Format.Memory(diskSize)} is too big for Steam?)\n");
			buffer = [];
		}

		bool success = false;
		using (IFileHandle? file = g_FullFileSystem.Open(folder, FileOpenOptions.Read | FileOpenOptions.Binary, "MOD")) {
			if (file == null)
				Warning($"Failed to read addon file {folder}! Does it exist?\n");
			else {
				file.Stream.ReadAtLeast(buffer, buffer.Length, false);
				success = true;
			}
		}

		lock (Mutex) {
			Buffer = buffer;
			Success = success;
			Done = true;
		}
	}
}

public static class AddonTasks
{
	public class AddFloatingAddons : Addon.Job.Base
	{
		public override void Start() => AddonSystem.MountFloatingAddons();
	}

	public class ClearUnusedGMAs : Addon.Job.Base
	{
		bool IsFinished;

		public override void Start() {
			Msg("ClearUnusedGMAs: Starting work...\n");
			IsFinished = false;
		}

		public override void Cycle() {
			ReadOnlySpan<char> file = g_FullFileSystem.FindFirstEx("cache/workshop/*.gma", "MOD", out ulong findHandle);
			while (!file.IsEmpty) {
				string fileName = new(file);
				string workshopID = fileName;
				Bootil.String.File.StripExtension(ref workshopID);

				ulong id = Bootil.String.To.UInt64(workshopID);
				if (id == 0) {
					string path = "cache/workshop/" + fileName;
					Warning($"\tRemoving '{path}' - Invalid filename, must be Workshop ID\n");
					g_FullFileSystem.RemoveFile(path, "MOD");
				}
				else if ((SteamUGC.GetItemState(new PublishedFileId_t(id)) & (uint)EItemState.k_EItemStateInstalled) == 0) {
					string path = "cache/workshop/" + fileName;
					Warning($"\tRemoving '{path}' - Workshop item is no longer installed\n");
					g_FullFileSystem.RemoveFile(path, "MOD");
				}

				file = g_FullFileSystem.FindNext(findHandle);
			}

			g_FullFileSystem.FindClose(findHandle);
			Msg("ClearUnusedGMAs: Finished!\n");
			IsFinished = true;
		}

		public override bool Finished() => IsFinished;
	}

	public class DownloadAddons(bool mountAfter) : Addon.Job.Base
	{
		readonly bool MountAfter = mountAfter;

		public override void Start() {
			int downloadCount = 0;
			foreach (IAddonSystem.Information addon in AddonSystem.GetList())
				if (!addon.Downloaded || addon.CanUpdate)
					downloadCount++;

			if (downloadCount != 0) {
				AddonSystem.AddJob(new NotifyStart());

				int remaining = downloadCount;
				foreach (IAddonSystem.Information addon in AddonSystem.GetList()) {
					if (addon.Downloaded && !addon.CanUpdate)
						continue;

					AddonSystem.AddJob(new UpdateTotals(--remaining, downloadCount));
					AddonSystem.AddJob(new DownloadFile(in addon));
				}

				AddonSystem.AddJob(new NotifyEnd());
			}
			else {
				if (!MountAfter)
					return;

				AddonSystem.MarkChanged();
			}

			AddonSystem.AddJob(new MountAvailable());
		}
	}

	public class DownloadFile : Addon.Job.Base
	{
		const double StuckTime = 10.0;
		const int MinTransferred = 8192;
		const int MaxFailures = 5;
		const float Percent = 100.0f;

		IAddonSystem.Information Info;
		Callback<DownloadItemResult_t>? DownloadCallback;
		bool IsFinished;
		double WarningTimer;
		int BytesTransferred;
		int PrevDownloadedBytes;
		int FailedCounter;
		LZMA.ExtractionThread? Extractor;
		bool Extracting;
		string FilePath = "";
		ThreadedUGCAccess? Thread;

		public DownloadFile(in IAddonSystem.Information info) {
			Info = info;
			IsFinished = false;
			FailedCounter = 0;
			Extracting = false;
		}

		public override void Start() {
			uint itemState = SteamUGC.GetItemState(new PublishedFileId_t(Info.WorkshopID));
			if ((itemState & (uint)(EItemState.k_EItemStateInstalled | EItemState.k_EItemStateLegacyItem)) == (uint)(EItemState.k_EItemStateInstalled | EItemState.k_EItemStateLegacyItem) && !Info.CanUpdate) {
				AddonSystem.OnAddonDownloaded(in Info);

				FilePath = new string(BaseFileSystem.get.GameDir()) + "/" + Info.File;
				Bootil.String.Util.FindAndReplace(ref FilePath, "\\", "/");

				Thread = new ThreadedUGCAccess(Info.WorkshopID);
				IsFinished = false;

				AddonSystem.Notify()?.StartDownload(Info.WorkshopID, Info.HContentPreview, Info.Title, Info.Size);
				return;
			}

			AddonSystem.UnmountAddon(Info.WorkshopID, "for update");
			if (!SteamUGC.DownloadItem(new PublishedFileId_t(Info.WorkshopID), true)) {
				Warning($"Workshop: Failed to start Workshop Item download for '{Info.Title}' ({Info.WorkshopID})!\n");
				NotifyFailed("Failed to start addon download");
				return;
			}

			DownloadCallback?.Unregister();
			DownloadCallback = Callback<DownloadItemResult_t>.Create(OnItemDownloaded);

			AddonSystem.Notify()?.StartDownload(Info.WorkshopID, Info.HContentPreview, Info.Title, Info.Size);

			WarningTimer = Platform.Time;
			BytesTransferred = 0;
			PrevDownloadedBytes = 0;
		}

		void OnItemDownloaded(DownloadItemResult_t result) {
			if (result.m_unAppID.m_AppId != GetSteamInfIDVersionInfo().AppID) {
				Warning($"OnItemDownloaded: invalid app id {result.m_unAppID.m_AppId}?\n");
				return;
			}

			IsFinished = true;
			Info.Legacy = (SteamUGC.GetItemState(new PublishedFileId_t(Info.WorkshopID)) & (uint)EItemState.k_EItemStateLegacyItem) != 0;

			AddonSystem.Notify()?.FinishDownload(Info.WorkshopID);
			AddonSystem.OnAddonDownloaded(in Info);

			if (result.m_eResult == EResult.k_EResultOK) {
				if (!Info.Legacy)
					return;

				Info.File = $"cache/workshop/{Info.WorkshopID}.gma";
				FilePath = new string(BaseFileSystem.get.GameDir()) + "/" + Info.File;
				Bootil.String.Util.FindAndReplace(ref FilePath, "\\", "/");

				Thread = new ThreadedUGCAccess(result.m_nPublishedFileId.m_PublishedFileId);
				IsFinished = false;
				return;
			}

			Warning($"Error downloading file '{Info.Title}' ({Info.WorkshopID})! - {SteamResult.ToString(result.m_eResult)}\n");
			NotifyFailed("Steam Error: " + SteamResult.ToString(result.m_eResult));
		}

		public override void Cycle() {
			IAddonDownloadNotification? notify = AddonSystem.Notify();
			// if (notify == null) // TODO, add this back once implemented
			// 	return;

			if (Thread != null) {
				if (!Thread.IsDone())
					return;

				if (!Thread.IsSuccess()) {
					Warning($"Failed to load file for {Info.WorkshopID}!\n");
					NotifyFailed("Failed to read addon file");
					Thread = null;
					return;
				}

				byte[] buffer = Thread.Buffer;
				int size = buffer.Length;
				if (size == 0)
					Warning($"This should never happen: Addon '{Info.Title}' ({Info.WorkshopID}) is 0 bytes.\n");
				else {
					if (BitConverter.ToUInt32(buffer, size - sizeof(uint)) == AddonFormat.CompressionSignature)
						size -= 8;
					else
						Warning($"Addon '{Info.Title}' ({Info.WorkshopID}) was created with a 3rd party tool, which might cause install/load issues.\n");
				}

				Extractor = new LZMA.ExtractionThread(buffer.AsSpan(0, size), FilePath);
				Extracting = true;
				IsFinished = false;
				Thread = null;
				return;
			}

			if (Extracting) {
				if (!Extractor!.IsDone()) {
					notify?.ExtractProgress(Info.WorkshopID, Info.HContentPreview, Info.Title, (uint)(Extractor.GetProgress() * Percent));
					return;
				}

				if (Extractor.Success()) {
					Extractor = null;

					using (IFileHandle? file = g_FullFileSystem.Open(FilePath, FileOpenOptions.ReadEx | FileOpenOptions.Binary, null)) {
						if (file == null)
							Warning($"This should not happen - failed to write timestamp to '{FilePath}'!\n");
						else {
							file.Stream.Seek(AddonFormat.TimestampOffset, SeekOrigin.Begin);
							file.Stream.Write(BitConverter.GetBytes(Info.TimeUpdated));
						}
					}

					Extracting = false;
					IsFinished = true;
					AddonSystem.OnAddonDownloaded(in Info);
					return;
				}

				Warning($"Extraction failed.. Oh oh! ({FilePath}) ({Info.WorkshopID})\n");
				Extractor = null;
				Extracting = false;
				NotifyFailed("Failed to extract file");
				return;
			}

			BaseFileSystem.get.RunSteamCallbacks();

			if (SteamUGC.GetItemDownloadInfo(new PublishedFileId_t(Info.WorkshopID), out ulong downloadedBytes, out ulong totalBytes) && totalBytes != 0) {
				notify?.DownloadProgress(Info.WorkshopID, Info.HContentPreview, Info.Title, (uint)downloadedBytes, (uint)totalBytes);
				BytesTransferred += (int)downloadedBytes - PrevDownloadedBytes;
				PrevDownloadedBytes = (int)downloadedBytes;
			}

			if (Platform.Time - WarningTimer > StuckTime) {
				if ((uint)BytesTransferred < MinTransferred) {
					Msg($"Transferred {Bootil.String.Format.Memory((uint)BytesTransferred)} ({BytesTransferred}) in 10 seconds\n");

					if (++FailedCounter > MaxFailures) {
						Warning($"Cancelling workshop download {Info.WorkshopID}, it's too slow.. maybe stuck? Try again later.\n");
						DownloadCallback?.Unregister();
						NotifyFailed("Download was too slow");
					}
				}
				else
					FailedCounter = 0;

				BytesTransferred = 0;
				WarningTimer = Platform.Time;
			}
		}

		public override bool Finished() => IsFinished;

		public virtual void NotifyFailed(ReadOnlySpan<char> reason) {
			IsFinished = true;
			Info.Failed = true;
			AddonSystem.OnAddonDownloadFailed(in Info);

			string error = Info.Title + ";" + new string(reason);
			BaseFileSystem.get.MenuSystem()?.SendProblemToMenu("addon_download_failed", 2, error);
		}
	}

	public class GetSubscriptions : Addon.Job.Base
	{
		const uint FirstTimeAdded = 1000000;

		SteamworksSubscribedFiles? Files;
		bool IsFinished;

		public override void Start() {
			IsFinished = false;
			AddonSystem.Clear();

			Files = new SteamworksSubscribedFiles();
			Files.Start();

			for (uint i = 0; i < Files.Total; i++)
				AddonSystem.AddUnloadedSubscription(Files.Items[i].m_PublishedFileId);
		}

		public override void Cycle() {
			if (!Files!.IsDone()) {
				AddonSystem.Notify()?.SubscriptionsProgress((int)Files.Received, (int)SteamUGC.GetNumSubscribedItems());
				return;
			}

			if (!Files.Succeeded)
				AddonSystem.EnableLoadingUnloadedAddons();

			ReadOnlySpan<char> file = g_FullFileSystem.FindFirstEx("addons/*.gma", "MOD", out ulong findHandle);
			while (!file.IsEmpty) {
				string fileName = new(file);
				string addonPath = "addons/" + fileName;

				string name = addonPath;
				Bootil.String.File.ExtractFilename(ref name);
				Bootil.String.File.StripExtension(ref name);

				int underscore = name.LastIndexOf('_');
				if (name.Length != 0 && underscore != -1) {
					string cachePath = "cache/workshop/" + name[(underscore + 1)..] + ".gma";
					g_FullFileSystem.RemoveFile(cachePath, "MOD");
					g_FullFileSystem.RenameFile(addonPath, cachePath, "MOD");
				}

				file = g_FullFileSystem.FindNext(findHandle);
			}

			g_FullFileSystem.FindClose(findHandle);

			uint timeAdded = FirstTimeAdded;
			foreach (SteamUGCDetails_t item in Files.Details) {
				SteamUGCDetails_t details = item;
				if (details.m_rtimeAddedToUserList == 0)
					details.m_rtimeAddedToUserList = timeAdded--;

				AddonSystem.AddAddonFromSteamDetails(in details);
			}

			CheckForWastedSpace();

			IsFinished = true;
			AddonSystem.Notify()?.Finish();
		}

		static void CheckForWastedSpace() {
			string wasted = "";

			ReadOnlySpan<char> file = g_FullFileSystem.FindFirstEx("cache/workshop/*.gma", "MOD", out ulong findHandle);
			while (!file.IsEmpty) {
				string fileName = new(file);
				string workshopID = fileName;
				Bootil.String.File.StripExtension(ref workshopID);

				ulong id = Bootil.String.To.UInt64(workshopID);
				if (id == 0 || (SteamUGC.GetItemState(new PublishedFileId_t(id)) & (uint)EItemState.k_EItemStateInstalled) == 0)
					wasted += "cache/workshop/" + fileName + ";";

				file = g_FullFileSystem.FindNext(findHandle);
			}

			g_FullFileSystem.FindClose(findHandle);

			if (wasted.Length != 0)
				BaseFileSystem.get.MenuSystem()!.SendProblemToMenu("menu_cleanupgmas", 0, wasted);
		}

		public override bool Finished() => IsFinished;
	}

	public class MountAvailable : Addon.Job.Base
	{
		const int WaitCycles = 2;

		int CycleCount;
		bool IsFinished;

		public override void Start() {
			if (AddonSystem.HasChanges())
				AddonSystem.Notify()?.SendMessage("#ugc.mounting");
		}

		public override void Cycle() {
			if (CycleCount < WaitCycles) {
				CycleCount++;
				return;
			}

			Msg("Addons have changes - remounting\n");
			AddonSystem.Clear();
			AddonSystem.Refresh();

			g_FullFileSystem.Gamemodes().Refresh();
			g_FullFileSystem.DoFilesystemRefresh();

			AddonSystem.Notify()?.Finish();
			IsFinished = true;
		}

		public override bool Finished() => IsFinished;
	}

	public class NotifyStart : Addon.Job.Base
	{
		public override void Start() => AddonSystem.Notify()?.Start();
	}

	public class NotifyEnd : Addon.Job.Base
	{
		public override void Start() => AddonSystem.Notify()?.Finish();
	}

	public class OnSubscribed(ulong workshopID) : Addon.Job.Base
	{
		readonly ulong WorkshopID = workshopID;
		SteamworksFileDetailsRequest? Request;
		bool IsFinished;

		public override void Start() => Request = new SteamworksFileDetailsRequest(WorkshopID, OnReceiveFileInfo);

		void OnReceiveFileInfo(SteamworksFileDetailsRequest request) {
			Msg("Got Subscription details!\n");
			IsFinished = true;

			if (!request.Failed)
				AddonSystem.OnAddonSubscribed(in request.Details);
			else
				Warning($"Error getting new subscription! {SteamResult.ToString(request.Details.m_eResult)}.\n");
		}

		public override bool Finished() => IsFinished;
	}

	public class UpdateTotals(int completed, int total) : Addon.Job.Base
	{
		readonly int Completed = completed;
		readonly int Total = total;

		public override void Start() => AddonSystem.Notify()?.DownloadTotals(Completed, Total);
	}
}
