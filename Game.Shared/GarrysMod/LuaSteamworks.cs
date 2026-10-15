#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Bootil.Compression;

using Source;
using Source.Common.Filesystem;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;
using Source.Common.Steam;

using Steamworks;

using System.Runtime.InteropServices;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

#if CLIENT_DLL || MENU_DLL
abstract class LuaCallbackRequest
{
	protected readonly int LuaStarts = g_LuaStarts;
	protected readonly LuaObject Callback = new();

	protected bool LuaValid => LuaStarts == g_LuaStarts;

	protected void Call(LuaObject table) {
		Callback.Push();
		table.Push();
		g_Lua!.CallInternalNoReturns(1);
	}

	protected void CallNoArgs() {
		if (!LuaValid)
			return;
		Callback.Push();
		g_Lua!.CallInternalNoReturns(0);
	}

	protected void CallError(int error) {
		LuaTable table = new();
		table.SetMember("error", error);
		Call(table);
		table.UnReference();
	}

	protected void CallError(ReadOnlySpan<char> error) {
		LuaTable table = new();
		table.SetMember("error", error);
		Call(table);
		table.UnReference();
	}
}

#if MENU_DLL
public static class WorkshopFavoritesCache
{
	public static readonly List<ulong> Favorites = [];
	static uint Page;
	static bool Busy;
	static readonly CallResult<SteamUGCQueryCompleted_t> QueryCompleted = CallResult<SteamUGCQueryCompleted_t>.Create(OnQueryCompleted);

	public static void Refresh() {
		if (Busy || !SteamworksExts.HasSteamClient())
			return;
		Page = 0;
		Busy = true;
		Query();
	}

	static void Query() {
		CSteamID steamID = SteamUser.GetSteamID();
		UGCQueryHandle_t handle = SteamUGC.CreateQueryUserUGCRequest(steamID.GetAccountID(), EUserUGCList.k_EUserUGCList_Favorited, EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items_ReadyToUse, EUserUGCListSortOrder.k_EUserUGCListSortOrder_CreationOrderDesc, new AppId_t(4000), new AppId_t(4000), Page / 50 + 1);
		if (handle == UGCQueryHandle_t.Invalid) {
			Warning("SteamUGFavoritesCache invalid UGC handle!\n");
			return;
		}

		SteamUGC.SetReturnOnlyIDs(handle, true);
		SteamAPICall_t call = SteamUGC.SendQueryUGCRequest(handle);
		if (call == SteamAPICall_t.Invalid) {
			Warning("SteamUGFavoritesCache invalid handle\n");
			return;
		}

		QueryCompleted.Set(call);
	}

	static void OnQueryCompleted(SteamUGCQueryCompleted_t result, bool ioFailure) {
		if (result.m_eResult != EResult.k_EResultOK) {
			Warning($"SteamUGFavoritesCache failed! {LuaSteamworks.EResultString(result.m_eResult)}\n");
			return;
		}

		if (result.m_unNumResultsReturned < 1)
			return;

		for (uint i = 0; i <= result.m_unNumResultsReturned; i++) {
			if (SteamUGC.GetQueryUGCResult(result.m_handle, i, out SteamUGCDetails_t details))
				Favorites.Add(details.m_nPublishedFileId.m_PublishedFileId);
		}

		SteamUGC.ReleaseQueryUGCRequest(result.m_handle);

		uint page = Page + result.m_unNumResultsReturned;
		if (page < result.m_unTotalMatchingResults) {
			Page = page;
			Query();
		}
		else
			Busy = false;
	}
}
#endif

class UGCDownloadJob : LuaCallbackRequest
{
	const int GMATimestampOffset = 13;

	public readonly ulong ID;
	SteamUGCDetails_t Details;
	LZMA.ExtractionThread? Extraction;
	readonly CallResult<SteamUGCQueryCompleted_t> ItemInfo;
	Callback<DownloadItemResult_t>? ItemDownloaded;

	public UGCDownloadJob(ulong id, LuaObject callback) {
		Callback.Set(callback);
		ID = id;
		ItemInfo = CallResult<SteamUGCQueryCompleted_t>.Create(OnItemInfo);
		UGCDownloadManager.SetActive(this);

		UGCQueryHandle_t handle = SteamUGC.CreateQueryUGCDetailsRequest([new PublishedFileId_t(ID)], 1);
		SteamAPICall_t call = handle == UGCQueryHandle_t.Invalid ? SteamAPICall_t.Invalid : SteamUGC.SendQueryUGCRequest(handle);
		if (call == SteamAPICall_t.Invalid) {
			CallNoArgs();
			Destroy();
		}
		else
			ItemInfo.Set(call);

		filesystem.Addons().Notify()?.StartDownload(ID, 0, ID.ToString(), 0);
	}

	void Destroy() {
		if (UGCDownloadManager.Active != null) {
			filesystem.Addons().Notify()?.FinishDownload(UGCDownloadManager.Active.ID);
			UGCDownloadManager.Active = null;
		}
		UGCDownloadManager.ProcessQueue();

		ItemInfo.Dispose();
		ItemDownloaded?.Dispose();
		ItemDownloaded = null;
		Callback.UnReference();
	}

	void Fail() {
		CallNoArgs();
		Destroy();
	}

	void OnItemInfo(SteamUGCQueryCompleted_t result, bool ioFailure) {
		if (ioFailure) {
			Warning($"steamworks.DownloadUGC: OnResult received null result for {ID}\n");
			Destroy();
			return;
		}

		if (!LuaValid) {
			SteamUGC.ReleaseQueryUGCRequest(result.m_handle);
			Destroy();
			return;
		}

		if (result.m_eResult != EResult.k_EResultOK) {
			SteamUGC.ReleaseQueryUGCRequest(result.m_handle);
			Warning($"steamworks.DownloadUGC: Failed to get addon info for {ID}, {LuaSteamworks.EResultString(result.m_eResult)}\n");
			Fail();
			return;
		}

		for (uint i = 0; i < result.m_unNumResultsReturned; i++) {
			if (!SteamUGC.GetQueryUGCResult(result.m_handle, i, out SteamUGCDetails_t details)) {
				Warning($"DownloadUGC: GetQueryUGCResult failed for item {ID}\n");
				continue;
			}

			if (details.m_nPublishedFileId.m_PublishedFileId != ID) {
				Warning($"DownloadUGC: OnItemInfo got wrong ID {details.m_nPublishedFileId.m_PublishedFileId}, expected {ID}\n");
				continue;
			}

			Details = details;
			filesystem.Addons().Notify()?.StartDownload(ID, Details.m_hPreviewFile.m_UGCHandle, Details.m_rgchTitle, (ulong)Details.m_nFileSize);
			StartDownload();
			break;
		}

		SteamUGC.ReleaseQueryUGCRequest(result.m_handle);
	}

	void StartDownload() {
		PublishedFileId_t fileID = new(ID);
		uint state = SteamUGC.GetItemState(fileID);
		if ((state & (uint)(EItemState.k_EItemStateInstalled | EItemState.k_EItemStateNeedsUpdate)) == (uint)EItemState.k_EItemStateInstalled) {
			if (SteamUGC.GetItemInstallInfo(fileID, out _, out _, 260, out uint timeStamp) && timeStamp != Details.m_rtimeUpdated)
				state |= (uint)EItemState.k_EItemStateNeedsUpdate;
		}

		if ((state & 0xe) == (uint)EItemState.k_EItemStateInstalled) {
			OnInstalled();
			return;
		}

		if ((state & (uint)EItemState.k_EItemStateInstalled) != 0 && (state & (uint)EItemState.k_EItemStateLegacyItem) != 0 && (state & (uint)EItemState.k_EItemStateNeedsUpdate) == 0) {
			string path = $"cache/workshop/{ID}.gma";
			using IFileHandle? file = filesystem.Open(path, FileOpenOptions.Read | FileOpenOptions.Binary, "MOD");
			if (file != null) {
				Span<byte> timestamp = stackalloc byte[4];
				file.Stream.Seek(GMATimestampOffset, SeekOrigin.Begin);
				timestamp.Clear();
				file.Stream.ReadAtLeast(timestamp, 4, false);
				file.Dispose();

				if (BitConverter.ToUInt32(timestamp) == Details.m_rtimeUpdated) {
					if (!SteamUGC.GetItemInstallInfo(fileID, out _, out _, 260, out _))
						Warning($"steamworks.DownloadUGC: Failed to GetItemInstallInfo for '{Details.m_rgchTitle}' ({ID}) to mark as used\n");
					Finish(path);
					return;
				}
			}
		}

		if (SteamUGC.DownloadItem(fileID, true)) {
			ItemDownloaded?.Dispose();
			ItemDownloaded = Callback<DownloadItemResult_t>.Create(OnItemDownloaded);
			return;
		}

		Warning($"steamworks.DownloadUGC: Failed to initialize download for addon {ID}\n");
		Fail();
	}

	void OnItemDownloaded(DownloadItemResult_t result) {
		if (!LuaValid) {
			Destroy();
			return;
		}

		if (result.m_eResult != EResult.k_EResultOK) {
			Warning($"steamworks.DownloadUGC: Failed to download addon {ID}, {LuaSteamworks.EResultString(result.m_eResult)}\n");
			Fail();
			return;
		}

		if (result.m_nPublishedFileId.m_PublishedFileId != ID || result.m_unAppID.m_AppId != 4000)
			return;

		if ((SteamUGC.GetItemState(new PublishedFileId_t(ID)) & (uint)EItemState.k_EItemStateLegacyItem) != 0)
			OnLegacyInstalled();
		else
			OnInstalled();
	}

	void OnInstalled() {
		string path = filesystem.Addons().GetAddonFilepath(ID, false);
		if (string.IsNullOrEmpty(path)) {
			Warning($"steamworks.DownloadUGC: Failed to get SteamUGC file for {ID}\n");
			Fail();
			return;
		}

		Finish(path);
	}

	void OnLegacyInstalled() {
		if (!SteamUGC.GetItemInstallInfo(new PublishedFileId_t(ID), out _, out string folder, 260, out _)) {
			Warning($"steamworks.DownloadUGC: Failed to GetItemInstallInfo for {ID}\n");
			Fail();
			return;
		}

		long size = filesystem.Size(folder, null);
		byte[] data = size > 0 ? new byte[size] : [];
		if (size < 0 || !filesystem.ReadFile(folder, null, data, 0)) {
			Warning($"steamworks.DownloadUGC: Failed to read addon file '{folder}' for {ID}\n");
			Fail();
			return;
		}

		int length = data.Length;
		if (length <= 0) {
			Warning($"steamworks.DownloadUGC: This should never happen: Addon {Details.m_rgchTitle} ({ID}) is 0 bytes!?\n");
			Fail();
			return;
		}

		if (BitConverter.ToUInt32(data, length - 4) == 0xbeefcace)
			length -= 8;
		else
			Warning($"Addon {Details.m_rgchTitle} ({ID}) was created with a 3rd party tool, which might cause install/load issues.\n");

		string outFile = $"{get.GameDir()}/cache/workshop/{ID}.gma".Replace('\\', '/');
		Extraction = new LZMA.ExtractionThread(data.AsSpan(0, length), outFile);
	}

	public void Think() {
		if (SteamUGC.GetItemDownloadInfo(new PublishedFileId_t(ID), out ulong downloaded, out ulong total) && total > 0)
			filesystem.Addons().Notify()?.DownloadProgress(ID, 0, "", (uint)downloaded, (uint)total);

		if (Extraction == null)
			return;

		if (!Extraction.IsDone()) {
			filesystem.Addons().Notify()?.ExtractProgress(ID, 0, "", (uint)(int)Extraction.GetProgress());
			return;
		}

		string path = $"cache/workshop/{ID}.gma";
		if (!Extraction.Success()) {
			Warning($"steamworks.DownloadUGC: Extraction failed.. Oh oh! ({path})\n");
			Extraction = null;
			Fail();
			return;
		}

		Extraction = null;

		IFileHandle? file = filesystem.Open(path, FileOpenOptions.ReadEx | FileOpenOptions.Binary, null);
		if (file == null) {
			Warning($"steamworks.DownloadUGC: This should not happen - failed to write timestamp to '{path}'!\n");
			Finish(path);
			return;
		}

		int type;
		using (file) {
			file.Stream.Seek(2, SeekOrigin.Begin);
			type = file.Stream.ReadByte();
			if (type == 'A') {
				file.Stream.Seek(GMATimestampOffset, SeekOrigin.Begin);
				file.Stream.Write(BitConverter.GetBytes(Details.m_rtimeUpdated));
			}
		}

		string oldPath = path;
		string? newPath = type switch {
			'S' => $"cache/workshop/{ID}.gms",
			'P' => $"cache/workshop/{ID}.dupe",
			'O' => $"cache/workshop/{ID}.dem",
			_ => null
		};

		if (newPath != null) {
			path = newPath;
			filesystem.RenameFile(oldPath, path, null);
		}

		Finish(path);
	}

	void Finish(string path) {
		if (!LuaValid) {
			Destroy();
			return;
		}

		path = path.Replace('\\', '/');
		int index = path.IndexOf("steamapps/workshop/content/4000", StringComparison.Ordinal);
		if (index != -1)
			path = path[(index + "steamapps/workshop/".Length)..];

		Callback.Push();
		g_Lua!.PushString(path);
		g_Lua.CallInternalNoReturns(1);
		Destroy();
	}
}

static class UGCDownloadManager
{
	struct QueuedDownload
	{
		public int Reference;
		public ulong ID;
	}

	static readonly List<QueuedDownload> Queue = [];
	public static UGCDownloadJob? Active;
	static int LuaStarts;
	static int Total;

	static void ValidateLuaStarts() {
		if (LuaStarts == g_LuaStarts)
			return;
		Queue.Clear();
		Active = null;
		LuaStarts = g_LuaStarts;
	}

	public static void SetActive(UGCDownloadJob job) {
		if (Active != null)
			Warning("DownloadUGCManager::SetActive: There's already a download! This should never happpen\n");
		else
			Active = job;
	}

	public static void Enqueue(ulong id, int reference) {
		ValidateLuaStarts();
		Queue.Add(new QueuedDownload { Reference = reference, ID = id });
		Total++;
		filesystem.Addons().Notify()?.DownloadTotals(Queue.Count, Total);
		ProcessQueue();
	}

	public static void ProcessQueue() {
		if (LuaStarts != g_LuaStarts) {
			Queue.Clear();
			Active = null;
			LuaStarts = 0;
		}
		else if (Active != null)
			return;

		if (Queue.Count == 0) {
			filesystem.Addons().Notify()?.Finish();
			Total = 0;
			return;
		}

		QueuedDownload next = Queue[0];
		Queue[0] = Queue[^1];
		Queue.RemoveAt(Queue.Count - 1);

		g_Lua!.ReferencePush(next.Reference);
		LuaObject callback = new();
		callback.SetFromStack(-1);
		g_Lua.ReferenceFree(next.Reference);
		g_Lua.Pop(1);

		filesystem.Addons().Notify()?.DownloadTotals(Queue.Count, Total);
		_ = new UGCDownloadJob(next.ID, callback);
		callback.UnReference();
	}

	public static void Think() => Active?.Think();
}
#if MENU_DLL
class UGCPublisher : LuaCallbackRequest
{
	static readonly string[] BadWords = [
		"sex", "fuck", "nigger", "reich", "nazi", "swastika", "penis", "peen", "dick", "cock", "faggot", "gay", "naked", "nude", "18+", "+18",
		"21+", "xxx", "goatse", "blowjob", "blow job", "samus", "shit", "miranda", "lawson", "boob", "fingering", "fingered", "back door action",
		"got wood", "humping", "oral", "peinus", "alma", "mileena", "wank", "fap", "jacking", "jerking", "masturb", "masterb", "\"fun\"", "lolipop",
		"strip", "miku", "honoka", "fnaf", "pony", "splatoon", "sans", "undertale", "achievement", "achiev", "reupload", "crash", "lag", "pls rate",
		"please rate", "pls like", "please like", "pls upvote", "please upvote", "liek", "kill myself", "kys"
	];

	ulong ID;
	readonly string File;
	readonly string Image;
	readonly string Title;
	readonly string Description;
	readonly string Changes;
	readonly List<string> Tags = [];
	readonly CallResult<SubmitItemUpdateResult_t> ItemUpdated;
	readonly CallResult<CreateItemResult_t> ItemCreated;

	static bool ContainsBadWords(string text) {
		string lower = text.ToLowerInvariant();
		foreach (string word in BadWords) {
			if (lower.Contains(word, StringComparison.Ordinal))
				return true;
		}
		return lower.Length > 10 && !lower.Contains(' ');
	}

	public UGCPublisher(string file, string image, string title, string description, LuaObject tags, LuaObject callback, ulong id, string changes) {
		Callback.Set(callback);
		ID = id;
		File = file;
		Image = image;
		Title = title;
		Description = description;
		Changes = changes;
		ItemUpdated = CallResult<SubmitItemUpdateResult_t>.Create(OnItemUpdated);
		ItemCreated = CallResult<CreateItemResult_t>.Create(OnItemCreated);

		if (ContainsBadWords(Title) || ContainsBadWords(Description)) {
			if (LuaValid) {
				Callback.Push();
				g_Lua!.PushNil();
				g_Lua.PushString("badwords");
				g_Lua.CallInternalNoReturns(2);
			}
			Destroy();
			return;
		}

		for (int i = 1; ; i++) {
			string? tag = tags.GetMemberStr(i, null);
			if (tag == null)
				break;
			Tags.Add(tag);
		}

		if (ID != 0) {
			StartUpdate();
			return;
		}

		Changes = "Initial creation";
		SteamAPICall_t call = SteamUGC.CreateItem(new AppId_t(4000), EWorkshopFileType.k_EWorkshopFileTypeCommunity);
		if (call == SteamAPICall_t.Invalid) {
			Fail("badhandle");
			return;
		}

		ItemCreated.Set(call);
	}

	void Destroy() {
		ItemCreated.Dispose();
		ItemUpdated.Dispose();
		Tags.Clear();
		Callback.UnReference();
	}

	void Fail(ReadOnlySpan<char> reason) {
		if (LuaValid) {
			Callback.Push();
			g_Lua!.PushNil();
			g_Lua.PushString(reason);
			g_Lua.CallInternalNoReturns(2);
		}
		Destroy();
	}

	void OnItemCreated(CreateItemResult_t result, bool ioFailure) {
		if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement) {
			Fail("termsofservice");
			return;
		}

		if (result.m_eResult != EResult.k_EResultOK) {
			Warning($"steamworks.Publish failed to create ID - {LuaSteamworks.EResultString(result.m_eResult)}!\n");
			Fail("createid");
			return;
		}

		ID = result.m_nPublishedFileId.m_PublishedFileId;
		StartUpdate();
	}

	void StartUpdate() {
		UGCUpdateHandle_t handle = SteamUGC.StartItemUpdate(new AppId_t(4000), new PublishedFileId_t(ID));
		if (handle == UGCUpdateHandle_t.Invalid) {
			Fail("badupdatehandle");
			return;
		}

		SteamUGC.SetItemTitle(handle, Title);
		SteamUGC.SetItemDescription(handle, Description);
		SteamUGC.SetItemPreview(handle, Image);
		SteamUGC.SetItemContent(handle, File);
		SteamUGC.SetItemTags(handle, Tags);

		SteamAPICall_t call = SteamUGC.SubmitItemUpdate(handle, Changes);
		if (call == SteamAPICall_t.Invalid) {
			Fail("badsubmitupdatehandle");
			return;
		}

		ItemUpdated.Set(call);
	}

	void OnItemUpdated(SubmitItemUpdateResult_t result, bool ioFailure) {
		if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement) {
			Fail("termsofservice");
			return;
		}

		if (result.m_eResult != EResult.k_EResultOK) {
			Warning($"steamworks.Publish failed to update file - {LuaSteamworks.EResultString(result.m_eResult)}!\n");
			Fail("cantupdate");
			return;
		}

		if (LuaValid) {
			Callback.Push();
			g_Lua!.PushNumber(result.m_nPublishedFileId.m_PublishedFileId);
			g_Lua.CallInternalNoReturns(1);
		}
		Destroy();
	}
}
#endif

class UGCFileInfoRequest : LuaCallbackRequest
{
	static readonly string[] ContentDescriptorNames = ["suggestive", "gore", "adult_only", "nudity", "general_mature"];

	readonly ulong ID;
	readonly CallResult<SteamUGCQueryCompleted_t> QueryCompleted;

	public UGCFileInfoRequest(ulong id, LuaObject callback, bool fullInfo) {
		ID = id;
		Callback.Set(callback);
		QueryCompleted = CallResult<SteamUGCQueryCompleted_t>.Create(OnQueryCompleted);

		UGCQueryHandle_t handle = SteamUGC.CreateQueryUGCDetailsRequest([new PublishedFileId_t(id)], 1);
		if (handle == UGCQueryHandle_t.Invalid) {
			CallError(-1);
			Destroy();
			return;
		}

		SteamUGC.SetReturnChildren(handle, true);
		if (fullInfo) {
			SteamUGC.SetReturnLongDescription(handle, true);
			SteamUGC.SetReturnAdditionalPreviews(handle, true);
		}

		SteamAPICall_t call = SteamUGC.SendQueryUGCRequest(handle);
		if (call == SteamAPICall_t.Invalid) {
			CallError(-2);
			Destroy();
			return;
		}

		QueryCompleted.Set(call);
	}

	void Destroy() {
		QueryCompleted.Dispose();
		Callback.UnReference();
	}

	void OnQueryCompleted(SteamUGCQueryCompleted_t result, bool ioFailure) {
		if (!LuaValid) {
			Destroy();
			return;
		}

		if (ioFailure || result.m_eResult != EResult.k_EResultOK) {
			Warning($"steamworks.FileInfo() failed ({LuaSteamworks.EResultString(result.m_eResult)})\n");
			CallError((int)result.m_eResult);
		}
		else if (result.m_unNumResultsReturned != 1)
			CallError(-3);
		else if (!SteamUGC.GetQueryUGCResult(result.m_handle, 0, out SteamUGCDetails_t details))
			CallError(-4);
		else if (details.m_nPublishedFileId.m_PublishedFileId == 0)
			CallError(-5);
		else if (details.m_nPublishedFileId.m_PublishedFileId == ID)
			PushDetails(result.m_handle, in details);

		SteamUGC.ReleaseQueryUGCRequest(result.m_handle);
		Destroy();
	}

	void PushDetails(UGCQueryHandle_t handle, in SteamUGCDetails_t details) {
		SteamFriends.RequestUserInformation(new CSteamID(details.m_ulSteamIDOwner), true);

		LuaTable table = new();
		table.SetMember("id", details.m_nPublishedFileId.m_PublishedFileId.ToString());
		table.SetMember("title", details.m_rgchTitle);
		table.SetMember("description", details.m_rgchDescription);
		table.SetMember("fileid", (details.m_hFile.m_UGCHandle == ulong.MaxValue ? 0 : details.m_hFile.m_UGCHandle).ToString());
		table.SetMember("previewid", details.m_hPreviewFile.m_UGCHandle.ToString());
		table.SetMember("owner", details.m_ulSteamIDOwner.ToString());
		table.SetMember("created", (float)details.m_rtimeCreated);
		table.SetMember("updated", (float)details.m_rtimeUpdated);
		table.SetMember("banned", details.m_bBanned);
		table.SetMember("tags", details.m_rgchTags);
		table.SetMember("size", (float)(uint)details.m_nFileSize);
		table.SetMember("visibility", (float)(int)details.m_eVisibility);
		table.SetMember("previewsize", (float)details.m_nPreviewFileSize);

		if (string.IsNullOrEmpty(details.m_rgchTitle)) {
			table.SetMember("title", "Hidden addon");
			table.SetMember("description", "This workshop item was hidden by its author. You may not be able to use it.");
		}

		Addon.FileSystem addons = filesystem.Addons();
		ulong id = details.m_nPublishedFileId.m_PublishedFileId;
		if (addons.IsSubscribed(id)) {
			table.SetMember("installed", addons.IsSubscribed(id));
			table.SetMember("disabled", !addons.ShouldMount(id));
		}
		else {
			table.SetMember("installed", false);
			table.SetMember("disabled", false);
		}

		table.SetMember("score", details.m_flScore);
		table.SetMember("up", (float)details.m_unVotesUp);
		table.SetMember("down", (float)details.m_unVotesDown);
		table.SetMember("total", (float)(details.m_unVotesUp + details.m_unVotesDown));

		table.SetMember("previewurl", SteamUGC.GetQueryUGCPreviewURL(handle, 0, out string previewURL, 0x200) ? previewURL : "");

		PublishedFileId_t[] children = new PublishedFileId_t[details.m_unNumChildren];
		bool hasChildren = SteamUGC.GetQueryUGCChildren(handle, 0, children, details.m_unNumChildren);
		LuaTable childTable = new();
		if (hasChildren) {
			for (int i = 0; i < children.Length; i++)
				childTable.SetMemberDouble(i + 1, children[i].m_PublishedFileId);
		}
		table.SetMember("children", childTable);
		childTable.UnReference();

		uint previews = SteamUGC.GetQueryUGCNumAdditionalPreviews(handle, 0);
		if (previews != 0) {
			LuaTable previewTable = new();
			for (uint i = 0; i < previews; i++) {
				if (!SteamUGC.GetQueryUGCAdditionalPreview(handle, 0, i, out string url, 0x200, out _, 0, out EItemPreviewType type))
					continue;
				LuaTable preview = new();
				preview.SetMember("url", url);
				preview.SetMember("type", (int)type);
				previewTable.SetMember(i + 1, preview);
				preview.UnReference();
			}
			table.SetMember("extra_previews", previewTable);
			previewTable.UnReference();
		}

		table.SetMember("ownername", SteamFriends.GetFriendPersonaName(new CSteamID(details.m_ulSteamIDOwner)) ?? "Unnammed Player");

		EUGCContentDescriptorID[] descriptors = new EUGCContentDescriptorID[8];
		uint descriptorCount = SteamUGC.GetQueryUGCContentDescriptors(handle, 0, descriptors, 8);
		LuaTable descriptorTable = new();
		for (int i = 0; i < descriptorCount; i++) {
			int descriptor = (int)descriptors[i];
			if (descriptor >= 1 && descriptor <= 5)
				descriptorTable.SetMember(i + 1, ContentDescriptorNames[descriptor - 1]);
			else
				descriptorTable.SetMemberDouble(i + 1, descriptor);
		}
		table.SetMember("content_descriptors", descriptorTable);
		descriptorTable.UnReference();

		Call(table);
		table.UnReference();
	}
}
#if MENU_DLL
class UGCFileUserInfoRequest : LuaCallbackRequest
{
	readonly ulong ID;
	readonly CallResult<GetUserItemVoteResult_t> VoteResult;

	public UGCFileUserInfoRequest(ulong id, LuaObject callback) {
		ID = id;
		Callback.Set(callback);
		VoteResult = CallResult<GetUserItemVoteResult_t>.Create(OnVoteResult);

		SteamAPICall_t call = SteamUGC.GetUserItemVote(new PublishedFileId_t(id));
		if (call == SteamAPICall_t.Invalid) {
			CallError("Invalid API Call Handle");
			Destroy();
			return;
		}

		VoteResult.Set(call);
	}

	void Destroy() {
		VoteResult.Dispose();
		Callback.UnReference();
	}

	void OnVoteResult(GetUserItemVoteResult_t result, bool ioFailure) {
		if (!LuaValid) {
			Destroy();
			return;
		}

		if (ioFailure || result.m_eResult != EResult.k_EResultOK)
			CallError(LuaSteamworks.EResultString(result.m_eResult));
		else if (result.m_nPublishedFileId.m_PublishedFileId == ID) {
			LuaTable table = new();
			table.SetMember("voted_up", result.m_bVotedUp);
			table.SetMember("voted_down", result.m_bVotedDown);
			table.SetMember("favorite", WorkshopFavoritesCache.Favorites.Contains(ID));
			Call(table);
			table.UnReference();
		}

		Destroy();
	}
}
#endif

class UGCListRequest : LuaCallbackRequest
{
	readonly uint NumRetrieve;
	uint Offset;
	readonly uint Days;
	ulong UserID;
	readonly EUGCQuery QueryType;
	readonly EUserUGCList ListType;
	readonly string Search;
	readonly List<string> Tags = [];
	readonly LuaTable Results = new();
	uint Collected;
	uint Total;
	readonly CallResult<SteamUGCQueryCompleted_t> QueryCompleted;

	public UGCListRequest(string type, LuaObject tags, uint offset, uint numRetrieve, uint days, ulong userID, LuaObject callback, string search) {
		QueryCompleted = CallResult<SteamUGCQueryCompleted_t>.Create(OnQueryCompleted);
		Offset = offset;
		NumRetrieve = Math.Min(numRetrieve, 50);
		Days = Math.Min(days, 365);
		UserID = userID;
		Callback.Set(callback);
		Search = search;

		QueryType = 0;
		ListType = 0;
		if (type == "popular" && Days > 0)
			QueryType = (EUGCQuery)10;
		else if (type == "trending")
			QueryType = (EUGCQuery)3;
		else if (type == "latest")
			QueryType = (EUGCQuery)1;
		else if (type == "friends")
			QueryType = (EUGCQuery)5;
		else if (type == "followed") {
			QueryType = (EUGCQuery)7;
			ListType = (EUserUGCList)8;
		}
		else if (type == "friend_favs")
			QueryType = (EUGCQuery)4;
		else if (type == "favorite") {
			ListType = (EUserUGCList)5;
			UserID = 1;
		}

		if (Days == 0)
			Days = 7;

		for (int i = 1; ; i++) {
			string? tag = tags.GetMemberStr(i, null);
			if (tag == null)
				break;
			Tags.Add(tag);
		}

		Run();
	}

	void Run() {
		UGCQueryHandle_t handle;
		uint page = Offset / 50 + 1;
		if (UserID == 0)
			handle = SteamUGC.CreateQueryAllUGCRequest(QueryType, EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items_ReadyToUse, new AppId_t(4000), new AppId_t(4000), page);
		else {
			AccountID_t accountID;
			if (UserID == 1) {
				if (!SteamworksExts.HasSteamClient() || SteamworksExts.GetSteamUser() == IntPtr.Zero) {
					Warning("Couldn't get UGC list for local player, no SteamUser API!\n");
					Finish();
					return;
				}
				accountID = SteamUser.GetSteamID().GetAccountID();
			}
			else
				accountID = new AccountID_t((uint)UserID);

			handle = SteamUGC.CreateQueryUserUGCRequest(accountID, ListType, EUGCMatchingUGCType.k_EUGCMatchingUGCType_Items_ReadyToUse, 0, new AppId_t(4000), new AppId_t(4000), page);
		}

		if (handle == UGCQueryHandle_t.Invalid) {
			Warning("k_UGCQueryHandleInvalid!\n");
			Finish();
			return;
		}

		SteamUGC.SetRankedByTrendDays(handle, Days);
		SteamUGC.SetReturnOnlyIDs(handle, true);
		if (Search.Length != 0)
			SteamUGC.SetSearchText(handle, Search);
		if ((int)QueryType is 0 or 1 or 3)
			SteamUGC.AddExcludedTag(handle, "ServerContent");
		foreach (string tag in Tags)
			SteamUGC.AddRequiredTag(handle, tag);

		SteamAPICall_t call = SteamUGC.SendQueryUGCRequest(handle);
		if (call == SteamAPICall_t.Invalid) {
			Finish();
			return;
		}

		QueryCompleted.Set(call);
	}

	void OnQueryCompleted(SteamUGCQueryCompleted_t result, bool ioFailure) {
		if (result.m_eResult != EResult.k_EResultOK) {
			Warning($"steamworks.GetList failed! {LuaSteamworks.EResultString(result.m_eResult)}\n");
			Finish();
			return;
		}

		Total = result.m_unTotalMatchingResults;
		for (uint i = 0; i <= result.m_unNumResultsReturned; i++) {
			if (i < Offset % 50)
				continue;
			if (Collected >= NumRetrieve)
				break;
			if (SteamUGC.GetQueryUGCResult(result.m_handle, i, out SteamUGCDetails_t details))
				Results.SetMember(Collected++, details.m_nPublishedFileId.m_PublishedFileId.ToString());
		}

		SteamUGC.ReleaseQueryUGCRequest(result.m_handle);

		if (Collected < NumRetrieve && Collected < Total && Offset + Collected < Total) {
			Offset += Collected;
			Run();
			return;
		}

		Finish();
	}

	void Finish() {
		if (LuaValid) {
			LuaTable table = new();
			table.SetMember("totalresults", (float)Total);
			table.SetMember("numresults", (float)Collected);
			table.SetMember("results", Results);
			Call(table);
			table.UnReference();
		}

		QueryCompleted.Dispose();
		Results.UnReference();
		Callback.UnReference();
	}
}

class UGCRemoteDownload : LuaCallbackRequest
{
	readonly ulong Handle;
	readonly bool Uncompress;
	readonly CallResult<RemoteStorageDownloadUGCResult_t> DownloadResult;

	public UGCRemoteDownload(ulong handle, bool uncompress, LuaObject callback, SteamAPICall_t call) {
		Handle = handle;
		Uncompress = uncompress;
		Callback.Set(callback);
		DownloadResult = CallResult<RemoteStorageDownloadUGCResult_t>.Create(OnDownloaded);
		DownloadResult.Set(call);
	}

	void Destroy() {
		DownloadResult.Dispose();
		Callback.UnReference();
	}

	void OnDownloaded(RemoteStorageDownloadUGCResult_t result, bool ioFailure) {
		if (!LuaValid || result.m_eResult != EResult.k_EResultOK) {
			CallNoArgs();
			Destroy();
			return;
		}

		UGCHandle_t handle = new(Handle);
		SteamRemoteStorage.GetUGCDetails(handle, out _, out _, out int size, out _);
		byte[] data = new byte[Math.Max(size, 0)];
		int read = SteamRemoteStorage.UGCRead(handle, data, size, 0, EUGCReadAction.k_EUGCRead_Close);
		if (read == 0) {
			CallNoArgs();
			Destroy();
			return;
		}

		string path = $"cache/workshop/{Handle}.cache";
		if (Uncompress) {
			using MemoryStream output = new();
			LZMA.Extract(data.AsSpan(0, read), output);
			filesystem.WriteFile(path, "MOD", output.GetBuffer().AsSpan(0, (int)output.Length));
		}
		else
			filesystem.WriteFile(path, "MOD", data.AsSpan(0, read));

		Callback.Push();
		g_Lua!.PushString(path);
		g_Lua.CallInternalNoReturns(1);
		Destroy();
	}
}

class PlayerInfoRequest : LuaCallbackRequest
{
	readonly CSteamID SteamID;
	readonly Callback<PersonaStateChange_t> PersonaStateChange;

	public PlayerInfoRequest(CSteamID steamID, LuaObject callback) {
		SteamID = steamID;
		Callback.Set(callback);
		PersonaStateChange = Callback<PersonaStateChange_t>.Create(OnPersonaStateChange);
	}

	void OnPersonaStateChange(PersonaStateChange_t change) {
		if (change.m_ulSteamID != SteamID.m_SteamID || (change.m_nChangeFlags & EPersonaChange.k_EPersonaChangeName) == 0)
			return;

		if (LuaValid && Callback.isFunction()) {
			Callback.Push();
			g_Lua!.PushString(SteamFriends.GetFriendPersonaName(SteamID));
			g_Lua.CallInternalNoReturns(1);
		}

		PersonaStateChange.Dispose();
		Callback.UnReference();
	}
}
#endif

public static partial class LuaSteamworks
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_steamworks = new("steamworks");

#if CLIENT_DLL || MENU_DLL
	static readonly List<ulong> UnusedItems = [];

	[DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl, EntryPoint = "SteamAPI_ISteamUGC_GetDownloadedItems")]
	static unsafe extern uint ISteamUGC_GetDownloadedItems(IntPtr instancePtr, ulong* pvecPublishedFileIDs, uint cMaxEntries);

	public static string EResultString(EResult result) => (int)result switch {
		1 => "OK",
		2 => "Generic failure",
		3 => "No internet connection",
		6 => "Logged in elsewhere",
		8 => "Invalid parameter (Weird symbols in name/descrption?)",
		9 => "File not found",
		10 => "Method is busy",
		15 => "Access denied (Item hidden/banned?)",
		16 => "Timed out",
		17 => "Item/user is banned",
		18 => "Account not found",
		20 => "Service unavailable",
		21 => "Not logged on",
		24 => "Insufficient privilege",
		25 => "Limit exceeded",
		34 => "Log on session replaced (Your GSLT is used elsewhere)",
		37 => "Input/output failure",
		51 => "Operation suspended",
		52 => "Operation cancelled",
		54 => "Disk drive full",
		86 => "Item(GSLT?) deleted",
		93 => "Time is not synched",
		102 => "GSL token banned",
		103 => "GS owner denied",
		106 => "GSL token expired",
		112 => "Limited user account",
		_ => $"Steam error code {(int)result}"
	};

	static ulong ParseID(ReadOnlySpan<char> str) {
		str = str.TrimStart(" \t\n\v\f\r");
		bool negative = false;
		if (str.Length > 0 && (str[0] == '+' || str[0] == '-')) {
			negative = str[0] == '-';
			str = str[1..];
		}

		int digits = 0;
		while (digits < str.Length && char.IsAsciiDigit(str[digits]))
			digits++;

		if (digits == 0 || !ulong.TryParse(str[..digits], out ulong value))
			return 0;
		return negative ? unchecked(0 - value) : value;
	}

	static ulong CheckID(ILuaInterface lua, int stackPos) => ParseID(lua.CheckString(stackPos));

	static bool CheckUGC(ILuaInterface lua) {
		if (get.SteamUGC() != null)
			return true;
		lua.ErrorFromLua("Calling steamworks functions too early.\n");
		return false;
	}

	static LuaObject CheckFunction(ILuaInterface lua, int stackPos) {
		LuaObject func = new(stackPos, LuaType.None);
		if (!func.isFunction())
			lua.TypeError("function", stackPos);
		return func;
	}

	[LuaFunction]
	static int GetPlayerName(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		lua.PushString(SteamFriends.GetFriendPersonaName(new CSteamID(id)));
		return 1;
	}

	[LuaFunction]
	static int RequestPlayerInfo(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		if (id == 0)
			return 0;

		CSteamID steamID = new(id);
		LuaObject callback = new(2, LuaType.None);
		if (!SteamFriends.RequestUserInformation(steamID, true)) {
			if (callback.isFunction()) {
				callback.Push();
				lua.PushString(SteamFriends.GetFriendPersonaName(steamID));
				lua.CallInternalNoReturns(1);
			}
		}
		else
			_ = new PlayerInfoRequest(steamID, callback);

		callback.UnReference();
		return 0;
	}

	[LuaFunction]
	static int ViewFile(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		if (id == 0)
			return 0;
		SteamFriends.ActivateGameOverlayToWebPage($"https://steamcommunity.com/sharedfiles/filedetails/?id={id}");
		return 0;
	}

	[LuaFunction]
	static int OpenWorkshop(ILuaInterface lua) {
		SteamFriends.ActivateGameOverlayToWebPage("http://steamcommunity.com/app/4000/workshop/");
		return 0;
	}

#if MENU_DLL
	[LuaFunction]
	static int Subscribe(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		if (id == 0)
			return 0;
		SteamUGC.SubscribeItem(new PublishedFileId_t(id));
		return 0;
	}

	[LuaFunction]
	static int Unsubscribe(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		if (id == 0)
			return 0;
		SteamUGC.UnsubscribeItem(new PublishedFileId_t(id));
		return 0;
	}

	[LuaFunction]
	static int Vote(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		if (id == 0)
			return 0;
		SteamUGC.SetUserItemVote(new PublishedFileId_t(id), lua.GetBool(2));
		return 0;
	}

	[LuaFunction]
	static int SetFavorite(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		if (id == 0)
			return 0;

		if (lua.GetBool(2)) {
			SteamUGC.AddItemToFavorites(new AppId_t(4000), new PublishedFileId_t(id));
			WorkshopFavoritesCache.Favorites.Add(id);
		}
		else {
			SteamUGC.RemoveItemFromFavorites(new AppId_t(4000), new PublishedFileId_t(id));
			WorkshopFavoritesCache.Favorites.Remove(id);
		}
		return 0;
	}

	[LuaFunction]
	static int SetFilePlayed(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		if (id == 0)
			return 0;
		SteamRemoteStorage.SetUserPublishedFileAction(new PublishedFileId_t(id), EWorkshopFileAction.k_EWorkshopFileActionPlayed);
		return 1;
	}

	[LuaFunction]
	static int SetFileCompleted(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		if (id == 0)
			return 0;
		SteamRemoteStorage.SetUserPublishedFileAction(new PublishedFileId_t(id), EWorkshopFileAction.k_EWorkshopFileActionCompleted);
		return 1;
	}
#endif

	[LuaFunction]
	static int IsSubscribed(ILuaInterface lua) {
		lua.PushBool(filesystem.Addons().IsSubscribed(CheckID(lua, 1)));
		return 1;
	}

	[LuaFunction]
	static int ShouldMountAddon(ILuaInterface lua) {
		lua.PushBool(filesystem.Addons().ShouldMount(CheckID(lua, 1)));
		return 1;
	}

#if MENU_DLL
	[LuaFunction]
	static int SetShouldMountAddon(ILuaInterface lua) {
		filesystem.Addons().SetShouldMount(CheckID(lua, 1), lua.GetBool(2));
		return 0;
	}

	[LuaFunction]
	static int ApplyAddons(ILuaInterface lua) {
		filesystem.Addons().Save();
		filesystem.Addons().Clear();
		filesystem.Addons().Refresh();
		filesystem.Gamemodes().Refresh();
		filesystem.DoFilesystemRefresh();
		return 0;
	}

	[LuaFunction]
	static int MarkDownloadedItemAsUnused(ILuaInterface lua) {
		if (!CheckUGC(lua))
			return 0;

		ulong id = CheckID(lua, 1);
		if (!UnusedItems.Contains(id))
			UnusedItems.Add(id);
		SteamUGC.MarkDownloadedItemAsUnused(new PublishedFileId_t(id));
		return 0;
	}

	[LuaFunction]
	static unsafe int GetDownloadedItems(ILuaInterface lua) {
		if (!CheckUGC(lua))
			return 0;

		uint numDownloaded = SteamUGC.GetNumDownloadedItems();
		ulong[] downloaded = new ulong[numDownloaded];
		uint gotDownloaded;
		fixed (ulong* ids = downloaded)
			gotDownloaded = ISteamUGC_GetDownloadedItems(SteamworksExts.GetSteamUGC(), ids, numDownloaded);
		if (gotDownloaded != numDownloaded)
			Warning("GetDownloadedItems: download count mismatch?\n");

		uint numSubscribed = SteamUGC.GetNumSubscribedItems();
		PublishedFileId_t[] subscribed = new PublishedFileId_t[numSubscribed];
		if (SteamUGC.GetSubscribedItems(subscribed, numSubscribed) != numSubscribed)
			Warning("GetDownloadedItems: subscription count mismatch?\n");

		lua.CreateTable();
		if (numDownloaded == numSubscribed)
			return 1;

		int key = 1;
		foreach (ulong id in downloaded) {
			if (Array.Exists(subscribed, s => s.m_PublishedFileId == id) || UnusedItems.Contains(id))
				continue;
			lua.PushNumber(key++);
			lua.PushString(id.ToString());
			lua.RawSet(-3);
		}
		return 1;
	}
#endif

	[LuaFunction]
	static int FileInfo(ILuaInterface lua) {
		if (!CheckUGC(lua))
			return 0;

		ulong id = CheckID(lua, 1);
		LuaObject callback = CheckFunction(lua, 2);
		bool fullInfo = lua.GetBool(3);
		if (id != 0)
			_ = new UGCFileInfoRequest(id, callback, fullInfo);
		callback.UnReference();
		return 0;
	}

	[LuaFunction]
	static int VoteInfo(ILuaInterface lua) {
		if (!CheckUGC(lua))
			return 0;

		ulong id = CheckID(lua, 1);
		LuaObject callback = CheckFunction(lua, 2);
		if (id != 0)
			_ = new UGCFileInfoRequest(id, callback, false);
		callback.UnReference();
		return 0;
	}

#if MENU_DLL
	[LuaFunction]
	static int FileUserInfo(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		LuaObject callback = CheckFunction(lua, 2);
		if (id != 0)
			_ = new UGCFileUserInfoRequest(id, callback);
		callback.UnReference();
		return 0;
	}
#endif

	[LuaFunction]
	static int GetList(ILuaInterface lua) {
		if (!CheckUGC(lua))
			return 0;

		string type = lua.CheckString(1);
		LuaObject tags = new(2, LuaType.None);
		uint offset = (uint)(int)lua.CheckNumber(3);
		uint numRetrieve = (uint)(int)lua.CheckNumber(4);
		uint days = (uint)(int)lua.CheckNumber(5);
		string userID = lua.CheckStringOpt(6, "0");
		LuaObject callback = CheckFunction(lua, 7);
		string search = lua.CheckStringOpt(8, "");

		_ = new UGCListRequest(type, tags, offset, numRetrieve, days, ParseID(userID), callback, search);
		callback.UnReference();
		tags.UnReference();
		return 0;
	}

	[LuaFunction]
	static int Download(ILuaInterface lua) {
		ulong handle = CheckID(lua, 1);
		bool uncompress = lua.GetBool(2);
		LuaObject callback = CheckFunction(lua, 3);

		if (handle == 0 || handle >= 0xFFFFFFFFFFFFFFFE) {
			callback.UnReference();
			return 0;
		}

		string path = $"cache/workshop/{handle}.cache";
		if (filesystem.FileExists(path, "MOD")) {
			callback.Push();
			lua.PushString(path);
			lua.CallInternalNoReturns(1);
		}
		else
			_ = new UGCRemoteDownload(handle, uncompress, callback, SteamRemoteStorage.UGCDownload(new UGCHandle_t(handle), 1));

		callback.UnReference();
		return 0;
	}

	[LuaFunction]
	static int DownloadUGC(ILuaInterface lua) {
		ulong id = CheckID(lua, 1);
		if (id == 0)
			return 0;

		ILuaObject? callback = lua.GetObject(2);
		if (callback == null || !callback.isFunction()) {
			lua.TypeError("function", 2);
			return 0;
		}

		callback.Push();
		int reference = lua.ReferenceCreate();
		lua.Pop(1);
		lua.Pop(1);
		UGCDownloadManager.Enqueue(id, reference);
		return 0;
	}

#if MENU_DLL
	[LuaFunction]
	static int Publish(ILuaInterface lua) {
		string file = lua.CheckString(1);
		string image = lua.CheckString(2);
		string title = lua.CheckString(3);
		string description = lua.CheckString(4);
		LuaObject tags = new(5, LuaType.None);
		LuaObject callback = new(6, LuaType.None);

		if (!callback.isFunction()) {
			lua.TypeError("function", 6);
			return 0;
		}

		ulong id = lua.GetType(7) != LuaType.Nil ? ParseID(lua.CheckString(7)) : 0;
		string changes = lua.CheckStringOpt(8, "None given.");

		Span<char> fullFile = stackalloc char[260];
		ReadOnlySpan<char> filePath = filesystem.RelativePathToFullPath(file, "MOD", fullFile);
		if (filePath.IsEmpty)
			Warning($"Warning! Failed to get full path for '{file}'!\n");

		Span<char> fullImage = stackalloc char[260];
		ReadOnlySpan<char> imagePath = filesystem.RelativePathToFullPath(image, "MOD", fullImage);
		if (imagePath.IsEmpty)
			Warning($"Warning! Failed to get full path for '{file}'!\n");

		_ = new UGCPublisher(new string(filePath.SliceNullTerminatedString()), new string(imagePath.SliceNullTerminatedString()), title, description, tags, callback, id, changes);

		callback.UnReference();
		tags.UnReference();
		return 0;
	}
#endif
#endif
}
#endif
