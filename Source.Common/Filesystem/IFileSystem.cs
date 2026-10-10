
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.Common.Utilities;

namespace Source.Common.Filesystem;

/// <summary>
/// This enum is both organizational and drives path priority
/// </summary>
public enum PathGroupName
{
	Default,
	EngineCore,
	Lua,
	Map,
	AddonContent,
	GMContent,
	GModCore,
	CurrentGame,
	SourceSDK,
	BAddonContent,
	GameContent,
	MountCfg,
	Downloads,
	Fallbacks
}

public enum FSAsyncStatus
{
	// Filename not part of the specified file system, try a different one.  (Used internally to find the right filesystem)
	ErrNotMine = -8,
	// Failure for a reason that might be temporary.  You might retry, but not immediately.  (E.g. Network problems)
	ErrRetryLater = -7,
	// read parameters invalid for unbuffered IO
	ErrAlignment = -6,
	// hard subsystem failure
	ErrFailure = -5,
	// read error on file
	ErrReading = -4,
	// out of memory for file read
	ErrNoMemory = -3,
	// caller's provided id is not recognized
	ErrUnknownID = -2,
	// filename could not be opened (bad path, not exist, etc)
	ErrFileOpen = -1,
	// operation is successful
	OK = 0,
	// file is properly queued, waiting for service
	StatusPending,
	// file is being accessed
	StatusInProgress,
	// file was aborted by caller
	StatusAborted,
	// file is not yet queued
	StatusUnserviced,
}

[Flags]
public enum FSAsyncFlags
{
	// do the allocation for dataPtr, but don't free
	AllocNoFree = 1 << 0,
	// free the memory for the dataPtr post callback
	FreeDataPtr = 1 << 1,
	// Actually perform the operation synchronously. Used to simplify client code paths
	Sync = 1 << 2,
	// allocate an extra byte and null terminate the buffer read in
	NullTerminate = 1 << 3,
}

/// <summary>
/// Optional completion callback for each async file serviced (or failed)
/// call is not reentrant, async i/o guaranteed suspended until return
/// </summary>
public delegate void FSAsyncCallbackFunc(in FileAsyncRequest request, int bytesRead, FSAsyncStatus err);
public delegate byte[] FSAllocFunc(string fileName, int bytes);

/// <summary>
/// Description of an async request
/// </summary>
public struct FileAsyncRequest
{
	// file system name
	public string? FileName;
	// optional, system will alloc/free if NULL
	public byte[]? Data;
	// optional initial seek_set, 0=beginning
	public int Offset;
	// optional read clamp, -1=exist test, 0=full read
	public int Bytes;
	// optional completion callback
	public FSAsyncCallbackFunc? Callback;
	// caller's unique file identifier
	public object? Context;
	// inter list priority, 0=lowest
	public int Priority;
	// behavior modifier
	public FSAsyncFlags Flags;
	// path ID (NOTE: this field is here to remain binary compatible with release HL2 filesystem interface)
	public string? PathID;
	// custom allocator. can be null. not compatible with FSASYNC_FLAGS_FREEDATAPTR
	public FSAllocFunc? Alloc;
}

public abstract class FSAsyncControl;

public interface ISearchPath
{
	bool Exists(scoped ReadOnlySpan<char> path); // Returns if the file or directory exists
	bool IsDirectory(scoped ReadOnlySpan<char> path); // Returns true if the path is a directory
	bool IsFileWritable(scoped ReadOnlySpan<char> path); // Returns true if the path can be written to
	IFileHandle? Open(scoped ReadOnlySpan<char> path, FileOpenOptions options); // Can return null if something went wrong
	bool RemoveFile(scoped ReadOnlySpan<char> path); // Return true if the file was deleted
	bool RenameFile(scoped ReadOnlySpan<char> oldPath, ReadOnlySpan<char> newPath); // Renames a single file, returns true if it worked
	bool SetFileWritable(scoped ReadOnlySpan<char> path, bool writable); // Determines if the file is writable
	long Size(scoped ReadOnlySpan<char> path); // Gets the size of a file
	/// <summary>
	/// Gets the last modified time of a file (UTC)
	/// </summary>
	/// <param name="path"></param>
	/// <returns></returns>
	DateTime Time(scoped ReadOnlySpan<char> path);
	ReadOnlySpan<char> GetPathString();
	object? GetPackFile();
	object? GetPackedStore();
	void PrepareFinds(List<string> files, List<string> dirs, string? wildcard);
	PathGroupName GetGroupName();
	void SetGroupName(PathGroupName name);
	ReadOnlySpan<char> GetDiskPath();



	public static ReadOnlySpan<char> Normalize(scoped ReadOnlySpan<char> unnormalizedString, Span<char> normalizedOutput) {
		int len = Math.Min(normalizedOutput.Length, unnormalizedString.Length);

		for (int i = 0; i < len; i++) {
			char c = unnormalizedString[i];
			normalizedOutput[i] = c == '\\' ? '/' : c;
		}

		return normalizedOutput[..len];
	}
	public static ReadOnlySpan<char> Concat(ISearchPath searchPath, scoped ReadOnlySpan<char> fileNameUnnormalized, Span<char> target) {
		Span<char> fileNameNormalized = stackalloc char[MAX_PATH];
		ReadOnlySpan<char> fileName = Normalize(fileNameUnnormalized, fileNameNormalized);

		int writePtr = 0;
		ReadOnlySpan<char> diskpath = searchPath.GetDiskPath();
		diskpath.CopyTo(target[writePtr..]); writePtr += diskpath.Length;
		if (diskpath.EndsWith('\\'))
			target[writePtr - 1] = '/';

		bool hasSlash = target[writePtr - 1] == '/';
		if (!hasSlash) {
			// Write a slash now
			target[writePtr] = '/'; writePtr++;
			hasSlash = true;
		}
		// Confirm we arent writing another slash
		if ((fileName.Length > 0 && (fileName[0] == '/' || fileName[0] == '\\')) && hasSlash)
			fileName = fileName[1..];

		fileName.ClampedCopyTo(target[writePtr..]); writePtr += fileName.Length;
		return target[..writePtr];
	}
}

public interface IBaseFileSystem
{
	/// <summary>
	/// Tries to open the file. May return null.
	/// </summary>
	/// <param name="fileName">The file name.</param>
	/// <param name="options">File options.<br/><code>
	/// |==============================================|
	/// | r  | Read                                    |
	/// | w  | Read                                    |
	/// | a  | Read                                    |
	/// | +  | Extended                                |
	/// | b  | Binary                                  |
	/// | n  | Text                                    |
	/// |==============================================|
	/// | r+ | Read   + Extended (or just ReadEx)      |
	/// | w+ | Write  + Extended (or just WriteEx)     |
	/// | a+ | Append + Extended (or just AppendEx)    |
	/// |==============================================|
	/// </code></param>
	/// <param name="pathID"></param>
	/// <returns></returns>
	public IFileHandle? Open(ReadOnlySpan<char> fileName, FileOpenOptions options, ReadOnlySpan<char> pathID);
	/// <summary>
	/// Tries to open the file. May return null.
	/// </summary>
	/// <param name="fileName">The file name.</param>
	/// <param name="options">File options.<br/><code>
	/// |==============================================|
	/// | r  | Read                                    |
	/// | w  | Read                                    |
	/// | a  | Read                                    |
	/// | +  | Extended                                |
	/// | b  | Binary                                  |
	/// | n  | Text                                    |
	/// |==============================================|
	/// | r+ | Read   + Extended (or just ReadEx)      |
	/// | w+ | Write  + Extended (or just WriteEx)     |
	/// | a+ | Append + Extended (or just AppendEx)    |
	/// |==============================================|
	/// </code></param>
	/// <returns></returns>
	public IFileHandle? Open(ReadOnlySpan<char> fileName, FileOpenOptions options)
		=> Open(fileName, options, null);
	/// <summary>
	/// Checks if the file is writable.
	/// </summary>
	/// <param name="fileName">The file name.</param>
	/// <param name="pathID">The search path ID.</param>
	/// <returns>True if the file is writable, and vice versa.</returns>
	public bool IsFileWritable(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID);
	/// <summary>
	/// Tries to set the file as writable.
	/// </summary>
	/// <param name="fileName">The file name.</param>
	/// <param name="writable">Is it writable?</param>
	/// <param name="pathID">The search path ID.</param>
	/// <returns>True if the operation succeded, and false if it didn't.</returns>
	public bool SetFileWritable(ReadOnlySpan<char> fileName, bool writable, ReadOnlySpan<char> pathID);
	public long Size(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID = default);
	public DateTime GetFileTime(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID = default);
	public bool FileExists(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID = default);


	public bool ReadFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> path, Span<byte> buf, int startingByte);
	public bool ReadFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> path, Span<char> buf, int startingByte);
}

public interface IFileSystem : IBaseFileSystem
{
	public FileSystemMountRetval MountSteamContent(long extraAppID = -1);
	/// <summary>
	/// Add a search path.
	/// </summary>
	/// <param name="path"></param>
	/// <param name="pathID"></param>
	/// <param name="addType"></param>
	public void AddSearchPath(ReadOnlySpan<char> diskPath, ReadOnlySpan<char> pathID, SearchPathAdd addType = SearchPathAdd.ToTail, PathGroupName groupName = PathGroupName.Default);
	/// <summary>
	/// Add a search path.
	/// </summary>
	/// <param name="path"></param>
	/// <param name="pathID"></param>
	/// <param name="addType"></param>
	public void AddSearchPath(ISearchPath searchPathImpl, ReadOnlySpan<char> pathID, SearchPathAdd addType = SearchPathAdd.ToTail, PathGroupName groupName = PathGroupName.Default);
	/// <summary>
	/// Remove a search path.
	/// </summary>
	public bool RemoveSearchPath(ReadOnlySpan<char> diskPath, ReadOnlySpan<char> pathID);
	/// <summary>
	/// Remove a search path.
	/// </summary>
	public bool RemoveSearchPath(ISearchPath searchPathImpl, ReadOnlySpan<char> pathID);
	/// <summary>
	/// Remove a search path.
	/// </summary>
	public bool RemoveSearchPath(Predicate<ISearchPath> search, ReadOnlySpan<char> pathID);
	/// <summary>
	/// Remove all search paths.
	/// </summary>
	public void RemoveAllSearchPaths();
	/// <summary>
	/// Remove all search paths associated with a given path ID.
	/// </summary>
	/// <param name="pathID"></param>
	public void RemoveSearchPaths(ReadOnlySpan<char> pathID);

	/// <summary>
	/// Marks a path ID by request only, which means files inside of it will only be accessed if the path ID is specifically requested.<br/>
	/// Otherwise, it will be ignored (in the case of global lookups without a path ID). <br/><br/>
	/// <b>NOTE</b>: <i>If there are currently no search paths with this path ID, then it will still remember it for later if you add other search paths with that path ID.</i>
	/// </summary>
	/// <param name="pathID"></param>
	/// <param name="requestOnly"></param>
	public void MarkPathIDByRequestOnly(ReadOnlySpan<char> pathID, bool requestOnly);
	int GetSearchPath(ReadOnlySpan<char> pathID, bool getPackFiles, Span<char> dest);

	bool RemoveFile(ReadOnlySpan<char> relativePath, ReadOnlySpan<char> pathID);
	bool RemoveFile(ReadOnlySpan<char> relativePath) => RemoveFile(relativePath, null);
	bool RenameFile(ReadOnlySpan<char> oldPath, ReadOnlySpan<char> newPath, ReadOnlySpan<char> pathID);
	bool RenameFile(ReadOnlySpan<char> oldPath, ReadOnlySpan<char> newPath) => RenameFile(oldPath, newPath, null);
	void CreateDirHierarchy(ReadOnlySpan<char> path, ReadOnlySpan<char> pathID);
	void CreateDirHierarchy(ReadOnlySpan<char> path) => CreateDirHierarchy(path, null);
	bool IsDirectory(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID);
	bool IsDirectory(ReadOnlySpan<char> fileName) => IsDirectory(fileName, null);
	void GetLocalCopy(ReadOnlySpan<char> path);
	ReadOnlySpan<char> RelativePathToFullPath(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID, Span<char> dest, PathTypeFilter filter = PathTypeFilter.None);
	bool FullPathToRelativePath(ReadOnlySpan<char> fullPath, Span<char> relative);
	bool FullPathToRelativePathEx(ReadOnlySpan<char> fullPath, ReadOnlySpan<char> pathID, Span<char> relative);
	bool WriteFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID, ReadOnlySpan<byte> buf);
	void MarkAllCRCsUnverified();
	ReadOnlySpan<char> WhereIsFile(ReadOnlySpan<char> relativePath, ReadOnlySpan<char> pathID = default);
	void PrintSearchPaths();

	/// <summary>
	/// FileNameHandle_t's are case-insensitive and slash-insensitive.
	/// </summary>
	/// <param name="name"></param>
	/// <returns></returns>
	FileNameHandle_t FindOrAddFileName(ReadOnlySpan<char> name);
	FileNameHandle_t FindFileName(ReadOnlySpan<char> name);
	void BeginMapAccess();
	void EndMapAccess();

	ReadOnlySpan<char> FindFirstEx(ReadOnlySpan<char> wildcard, ReadOnlySpan<char> pathID, out ulong findHandle);
	ReadOnlySpan<char> FindNext(ulong findHandle);
	void FindClose(ulong findHandle);

	ReadOnlySpan<char> String(FileNameHandle_t nameHandle);

	FSAsyncStatus AsyncRead(in FileAsyncRequest request) => AsyncReadMultiple(new ReadOnlySpan<FileAsyncRequest>(in request), default);
	FSAsyncStatus AsyncRead(in FileAsyncRequest request, out FSAsyncControl? control) {
		control = null;
		return AsyncReadMultiple(new ReadOnlySpan<FileAsyncRequest>(in request), new Span<FSAsyncControl?>(ref control));
	}
	FSAsyncStatus AsyncReadMultiple(ReadOnlySpan<FileAsyncRequest> requests, Span<FSAsyncControl?> controls);
	void AsyncFinishAll(int toPriority = 0);

	FSAsyncStatus AsyncFinish(FSAsyncControl control, bool wait = true);
	FSAsyncStatus AsyncGetResult(FSAsyncControl control, out byte[]? data, out int size);
	FSAsyncStatus AsyncStatus(FSAsyncControl control);

	public enum KeyValuesPreloadType
	{
		VMT,
		SoundEmitter,
		SoundScape,
		NumTypes,
	}

	void LoadCompiledKeyValues(KeyValuesPreloadType type, ReadOnlySpan<char> archiveFile);
	KeyValues? LoadKeyValues(KeyValuesPreloadType type, ReadOnlySpan<char> filename, ReadOnlySpan<char> pathID = default);
	bool LoadKeyValues(KeyValues head, KeyValuesPreloadType type, ReadOnlySpan<char> filename, ReadOnlySpan<char> pathID = default);

	ReadOnlySpan<char> ReadLine(Span<char> output, IFileHandle file);

#if GMOD_DLL
	void RemoveSearchPathsByGroup(PathGroupName groupName);
	void SetGet(IGet get);
	Addon.FileSystem Addons();
	Gamemode.System Gamemodes();
	GameDepot.System Games();
	LegacyAddons.System LegacyAddons();
	Language Language();
	void DoFilesystemRefresh();
	int LastFilesystemRefresh();
	void AddVPKFileFromPath(ReadOnlySpan<char> vpk, ReadOnlySpan<char> path, uint id);
	void GMOD_SetupDefaultPaths(ReadOnlySpan<char> path, ReadOnlySpan<char> game);
	void GMOD_FixPathCase(Span<char> a);
#endif

	bool FindIsDirectory(FileFindHandle_t findHandle);
	WaitForResourcesHandle_t WaitForResources(ReadOnlySpan<char> levelBaseName);
	bool GetWaitForResourcesProgress(int waitForResourcesHandle, out float progress, out bool complete);
	void CancelWaitForResources(WaitForResourcesHandle_t handle);
}

public enum PathTypeFilter
{
	None,
	CullPack,
	CullNonPack
}
