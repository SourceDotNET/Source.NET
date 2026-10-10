// TODO: Logging calls when things go wrong, ie. try/catches


using CommunityToolkit.HighPerformance;

using Source.Common.Filesystem;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.Common.Utilities;
using Source.Filesystem;
using Source.Filesystem.GarrysMod;

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Source.FileSystem;

// Maybe we redo this one day...
public partial class BaseFileSystem : IFileSystem
{
	readonly Lock SearchPathLock = new();
	readonly Lock FileNameLock = new();

	readonly record struct searchPathInternal(ISearchPath path, string pathID);
	private readonly SearchPathIDCollection SearchPaths = [];
	private readonly List<searchPathInternal>[] SearchPathGroups = new List<searchPathInternal>[(int)PathGroupName.Fallbacks + 1];

	private List<searchPathInternal> GetSearchPathGroupsFor(PathGroupName groupName) 
		=> SearchPathGroups[(int)groupName] ??= [];

	private void AddSearchPathFromGroup(ISearchPath searchPath, ReadOnlySpan<char> pathID) 
		=> GetSearchPathGroupsFor(searchPath.GetGroupName()).Add(new(searchPath, new(pathID.SliceNullTerminatedString())));

	private void RemoveSearchPathFromGroup(ISearchPath searchPath, ReadOnlySpan<char> pathID) 
		=> GetSearchPathGroupsFor(searchPath.GetGroupName()).Remove(new(searchPath, new(pathID.SliceNullTerminatedString())));

	private void AddSearchPathFinal(ISearchPath searchPath, SearchPathAdd addType, SearchPathCollection collection, PathGroupName groupName, ReadOnlySpan<char> pathID) {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		if (addType == SearchPathAdd.ToHead)
			collection.GetAddOrder().Insert(0, searchPath);
		else
			collection.GetAddOrder().Add(searchPath);
		collection.InvalidateOrder();
		searchPath.SetGroupName(groupName);
		AddSearchPathFromGroup(searchPath, pathID);
	}

	public BaseFileSystem() {
		RemoveSearchPaths("EXECUTABLE_PATH");
		AddSearchPath(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "EXECUTABLE_PATH", name: PathGroupName.EngineCore);
		AddSearchPath(AppContext.BaseDirectory, "BASE_PATH", name: PathGroupName.EngineCore);
	}

	private void AddMapPackFile(ReadOnlySpan<char> path, ReadOnlySpan<char> pathID, SearchPathAdd addType, PathGroupName groupName) {
		using IFileHandle? file = Open(path, FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
		if (file == null) {
			Warning("Couldn't open BSP for embedded pack file\n");
			return;
		}

		BSPDHeader header = default;
		file.Stream.ReadToStruct(ref header);

		BSPLump pakfile = header.GetLump(LumpIndex.PakFile);
		if (pakfile.FileLength <= Unsafe.SizeOf<BSPLump>()) {
			// Must be invalid
			return;
		}

		Span<char> fullPath = stackalloc char[MAX_PATH];
		ReadOnlySpan<char> newPath = RelativePathToFullPath(path, "GAME", fullPath);

		if (!SearchPaths.OpenOrCreateCollection(pathID, out SearchPathCollection collection)) {
			for (int i = 0, c = collection.Count; i < c; i++) {
				var searchPath = collection.GetAddOrder()[i];
				if (searchPath.GetDiskPath() == newPath) {
					if ((addType == SearchPathAdd.ToHead && i == 0) || addType == SearchPathAdd.ToTail)
						return;
					else {
						RemoveSearchPathFromGroup(searchPath, pathID);
						collection.GetAddOrder().RemoveAt(i);
						collection.InvalidateOrder();
						i--;
						c--;
						break;
					}
				}
			}
		}

		ZipPackFileSearchPath zip = new(this, new(newPath), file.Stream, in pakfile);
		if (!zip.IsValid()) {
			Warning("ZipPackFileSearchPath not valid\n");
			return;
		}

		AddSearchPathFinal(zip, addType, collection, groupName, pathID);
	}

	private void AddVPKFile(ReadOnlySpan<char> path, ReadOnlySpan<char> pathID, SearchPathAdd addType, PathGroupName groupName) {
		string newPath = Path.IsPathFullyQualified(path) ? new(path) : Path.GetFullPath(new(path));

		if (!SearchPaths.OpenOrCreateCollection(pathID, out SearchPathCollection collection)) {
			for (int i = 0, c = collection.Count; i < c; i++) {
				var searchPath = collection.GetAddOrder()[i];
				if (searchPath.GetDiskPath() == newPath) {
					if ((addType == SearchPathAdd.ToHead && i == 0) || addType == SearchPathAdd.ToTail)
						return;
					else {
						RemoveSearchPathFromGroup(searchPath, pathID);
						collection.GetAddOrder().RemoveAt(i);
						collection.InvalidateOrder();
						i--;
						c--;
						break;
					}
				}
			}
		}

		ISearchPath createdSearchPath = new PackStoreSearchPath(this, newPath);
		AddSearchPathFinal(createdSearchPath, addType, collection, groupName, pathID);
	}
	private void AddPackFiles(ReadOnlySpan<char> path, ReadOnlySpan<char> pathID, SearchPathAdd addType) { } // TODO 
	private void AddSeparatorAndFixPath(ref string path) { // this sucks fix it later
		path = (path.TrimEnd('\\').TrimEnd('/') + "/").Replace("\\", "/");
	}
	private void AddSearchPathDiskInternal(ReadOnlySpan<char> path, ReadOnlySpan<char> pathID, SearchPathAdd addType, PathGroupName groupName, bool addPackFiles, bool workshop = false) {
		var ext = Path.GetExtension(path);

		switch (ext) {
			case ".bsp": AddMapPackFile(path, pathID, addType, groupName); return;
			case ".vpk": AddVPKFile(path, pathID, addType, groupName); return;
		}

		string newPath = Path.IsPathFullyQualified(path) ? new(path) : Path.GetFullPath(new(path));
		AddSeparatorAndFixPath(ref newPath);

		if (!SearchPaths.OpenOrCreateCollection(pathID, out SearchPathCollection collection)) {
			for (int i = 0, c = collection.Count; i < c; i++) {
				var searchPath = collection.GetAddOrder()[i];
				if (searchPath.GetDiskPath() == newPath) {
					if ((addType == SearchPathAdd.ToHead && i == 0) || addType == SearchPathAdd.ToTail)
						return;
					else {
						RemoveSearchPathFromGroup(searchPath, pathID);
						collection.GetAddOrder().RemoveAt(i);
						collection.InvalidateOrder();
						i--;
						c--;
						break;
					}
				}
			}
		}

		if (addPackFiles) {
			AddPackFiles(newPath, pathID, addType);
		}

		ISearchPath createdSearchPath = new DiskSearchPath(this, newPath) { Workshop = workshop };
		AddSearchPathFinal(createdSearchPath, addType, collection, groupName, pathID);
	}

	public void AddSearchPath(ReadOnlySpan<char> path, ReadOnlySpan<char> pathID, SearchPathAdd addType = SearchPathAdd.ToTail, PathGroupName name = PathGroupName.Default, bool workshop = false) {
		AddSearchPathDiskInternal(path, pathID, addType, name, true, workshop);
	}

	public void AddSearchPath(ISearchPath path, ReadOnlySpan<char> pathID, SearchPathAdd addType = SearchPathAdd.ToTail, PathGroupName groupName = PathGroupName.Default) {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		if (!SearchPaths.OpenOrCreateCollection(pathID, out SearchPathCollection collection)) {
			for (int i = 0, c = collection.Count; i < c; i++) {
				var searchPath = collection.GetAddOrder()[i];
				if (searchPath != path)
					continue;

				if ((addType == SearchPathAdd.ToHead && i == 0) || addType == SearchPathAdd.ToTail)
					return;
				else {
					RemoveSearchPathFromGroup(searchPath, pathID);
					collection.GetAddOrder().RemoveAt(i);
					collection.InvalidateOrder();
					i--;
					c--;
					break;
				}
			}
		}

		AddSearchPathFinal(path, addType, collection, groupName, pathID);
	}

	public struct CollectionIterator(ulong hashID, SearchPathIDCollection collections)
	{
		readonly ulong HashID = hashID;
		readonly SearchPathIDCollection Collections = collections;
		bool iterateCollections;

		int currentCollectionIdx;
		int currentSearchPathIdx;

		SearchPathCollection? currentCollection;
		ISearchPath? currentSearchPath;

		bool initialized;

		public bool MoveNext() {
			if (!initialized) {
				currentCollectionIdx = 0;
				currentSearchPathIdx = 0;
				if (HashID == 0) {
					iterateCollections = true;
					currentCollection = Collections.At(0);
				}
				else {
					iterateCollections = false;
					Collections.TryGetValue(HashID, out currentCollection);
				}
				initialized = true;
			}

		checkCollection:
			if (currentCollection == null)
				return false;

			currentSearchPath = currentCollection.AtSorted(currentSearchPathIdx);
			if (currentSearchPath == null) {
				if (iterateCollections) {
					currentCollectionIdx++;
					currentSearchPathIdx = 0;
					currentCollection = Collections.At(currentCollectionIdx);
					goto checkCollection;
				}
				return false;
			}

			currentSearchPathIdx++;   // advance for next call
			return true;
		}

		public readonly ISearchPath Current => currentSearchPath!;

		public void Reset(){
			initialized = false;
			iterateCollections = false;
			currentCollectionIdx = 0;
			currentSearchPathIdx = 0;
			currentCollection = null;
			currentSearchPath = null;
		}
	}

	public CollectionIterator GetCollections(ulong hashID) {
		return new CollectionIterator(hashID, SearchPaths);   
	}

	interface IFirstToThePostOp<T>
	{
		T Invoke(ISearchPath p, scoped ReadOnlySpan<char> name);
		bool Win(T v);
	}

	/// <summary>
	/// Iterates through all <see cref="SearchPathCollection"/>'s (or a single lookup if pathID != null), and returns the first time <paramref name="winCondition"/> returns true.
	/// <br/> 
	/// If nothing returns true, the method returns <see cref="loseDefault"/>.
	/// </summary>
	/// <param name="filename">A local-to-searchpath filename</param>
	/// <param name="pathID">A pathID. If null, will search through every <see cref="SearchPathCollection"/>; otherwise searches for the single collection in the <see cref="SearchPaths"/> lookup table.</param>
	/// <param name="func">A delegate to run on every <see cref="ISearchPath"/></param>
	/// <param name="winCondition">Compares the return value from the search path. Return true if the search path won.</param>
	/// <param name="loseDefault">If no search paths won, then this value is returned.</param>
	/// <param name="winner">The <see cref="ISearchPath"/> that won (if the method returns true)</param>
	/// <returns>True if a <see cref="ISearchPath"/> won.</returns>
	private T? FirstToThePost<T, TOp>(
		ReadOnlySpan<char> filename,
		ReadOnlySpan<char> pathID,
		in TOp op,
		T? loseDefault,
		[NotNullWhen(true)] out ISearchPath? winner
	) where TOp : struct, IFirstToThePostOp<T>, allows ref struct {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		filename = filename.SliceNullTerminatedString();
		Span<char> filenameNormalizedBuffer = stackalloc char[MAX_PATH];
		ReadOnlySpan<char> filenameNormalized = ISearchPath.Normalize(filename, filenameNormalizedBuffer);
		ulong hashID = pathID.Hash();
		CollectionIterator iterator = GetCollections(hashID);
		while (iterator.MoveNext()){ 
			ISearchPath path = iterator.Current;
			T? ret = op.Invoke(path, filenameNormalized);
			if (op.Win(ret)) {
				winner = path;
				return ret;
			}
		}
		winner = null;
		return loseDefault;
	}

	public ReadOnlySpan<char> RelativePathToFullPath(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID, Span<char> dest, PathTypeFilter filter = PathTypeFilter.None) {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		fileName = fileName.SliceNullTerminatedString();
		Span<char> filenameNormalizedBuffer = stackalloc char[MAX_PATH];
		ReadOnlySpan<char> filenameNormalized = ISearchPath.Normalize(fileName, filenameNormalizedBuffer);
		CollectionIterator iterator = GetCollections(pathID.Hash());
		while (iterator.MoveNext()) {
			ISearchPath path = iterator.Current;
			if (path is DiskSearchPath disk) {
				if (disk.ExistsOnDisk(filenameNormalized))
					return ISearchPath.Concat(path, fileName, dest);

				string resolved = g_AddonFileSystem.ResolveFile(disk.GetFullPath(filenameNormalized));
				if (resolved.Length != 0) {
					int len = strcpy(dest, resolved);
					return dest[..len];
				}
			}
			else if (path.Exists(filenameNormalized))
				return ISearchPath.Concat(path, fileName, dest);
		}
		return null;
	}

	public bool FullPathToRelativePath(ReadOnlySpan<char> fullPath, Span<char> relative) => FullPathToRelativePathEx(fullPath, null, relative);

	public bool FullPathToRelativePathEx(ReadOnlySpan<char> fullPath, ReadOnlySpan<char> pathID, Span<char> relative) {
		fullPath = fullPath.SliceNullTerminatedString();
		pathID = pathID.SliceNullTerminatedString();
		if (fullPath.IsEmpty) {
			if (!relative.IsEmpty)
				relative[0] = '\0';
			return false;
		}

		strcpy(relative, fullPath);

		Span<char> fullPathNormalized = stackalloc char[MAX_PATH];
		ReadOnlySpan<char> normalized = ISearchPath.Normalize(fullPath, fullPathNormalized);

		ulong pathIDHash = pathID.IsEmpty ? 0 : pathID.Hash();
		foreach (var searchPaths in SearchPaths) {
			if (!pathID.IsEmpty && searchPaths.Key != pathIDHash)
				continue;

			foreach (var searchPath in searchPaths.Value.GetSortOrder()) {
				if (searchPath is not DiskSearchPath)
					continue;

				Span<char> diskPathNormalized = stackalloc char[MAX_PATH];
				ReadOnlySpan<char> diskPath = ISearchPath.Normalize(searchPath.GetDiskPath(), diskPathNormalized);
				if (diskPath.IsEmpty || !normalized.StartsWith(diskPath, StringComparison.OrdinalIgnoreCase))
					continue;

				ReadOnlySpan<char> remainder = normalized[diskPath.Length..];
				if (remainder.Length >= relative.Length)
					return false;

				remainder.CopyTo(relative);
				relative[remainder.Length] = '\0';
				return true;
			}
		}

		return false;
	}

	public bool WriteFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID, ReadOnlySpan<byte> buf) {
		using IFileHandle? handle = Open(fileName, FileOpenOptions.Write | FileOpenOptions.Binary, pathID);
		if (handle == null)
			return false;

		handle.Stream.Write(buf);
		return true;
	}

	readonly ref struct IsDirectory_Op() : IFirstToThePostOp<bool>
	{
		public bool Invoke(ISearchPath p, scoped ReadOnlySpan<char> name) => p.IsDirectory(name);
		public bool Win(bool v) => v;
	}

	public bool IsDirectory(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID) {
		return FirstToThePost(fileName, pathID, new IsDirectory_Op(), false, out _);
	}

	readonly ref struct IsFileWritable_Op() : IFirstToThePostOp<bool>
	{
		public bool Invoke(ISearchPath p, scoped ReadOnlySpan<char> name) => p.IsFileWritable(name);
		public bool Win(bool v) => v;
	}


	public bool IsFileWritable(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID) {
		return FirstToThePost(fileName, pathID, new IsFileWritable_Op(), false, out _);
	}

	public void MarkPathIDByRequestOnly(ReadOnlySpan<char> pathID, bool requestOnly) {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		ulong hashID = pathID.Hash();

		if (!SearchPaths.TryGetValue(hashID, out var collection))
			return;

		collection.RequestOnly = requestOnly;
	}

	public int GetSearchPath(ReadOnlySpan<char> pathID, bool getPackFiles, Span<char> dest) {
		if (!dest.IsEmpty)
			dest[0] = '\0';

		StringBuilder path = new();
		if (SearchPaths.TryGetValue(pathID.Hash(), out var collection)) {
			foreach (ISearchPath searchPath in collection.GetSortOrder()) {
				if (!getPackFiles && searchPath.GetPackFile() != null)
					continue;

				if (path.Length > 0)
					path.Append(';');

				path.Append(searchPath.GetPathString());
				if (searchPath.GetPackFile() != null)
					path.Append('\\');
			}
		}

		if (!dest.IsEmpty)
			strcpy(dest, path.ToString());

		return path.Length + 1;
	}

	public FileSystemMountRetval MountSteamContent(long extraAppID = -1) {
		throw new NotImplementedException(); // todo
	}

	readonly ref struct Open_Op(FileOpenOptions options) : IFirstToThePostOp<IFileHandle?>
	{
		readonly FileOpenOptions Options = options;
		public IFileHandle? Invoke(ISearchPath p, scoped ReadOnlySpan<char> name) => p.Open(name, Options);
		public bool Win(IFileHandle? v) => v != null;
	}

	public IFileHandle? Open(ReadOnlySpan<char> fileName, FileOpenOptions options, ReadOnlySpan<char> pathID) {
		return FirstToThePost<IFileHandle?, Open_Op>(fileName, pathID, new Open_Op(options), null, out _);
	}


	public void RemoveAllSearchPaths() {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		SearchPaths.Clear();
	}

	readonly ref struct RemoveFile_Op() : IFirstToThePostOp<bool>
	{
		public bool Invoke(ISearchPath p, scoped ReadOnlySpan<char> name) => p.RemoveFile(name);
		public bool Win(bool v) => v;
	}

	public bool RemoveFile(ReadOnlySpan<char> relativePath, ReadOnlySpan<char> pathID) {
		string fn = new(relativePath);
		return FirstToThePost(relativePath, pathID, new RemoveFile_Op(), false, out _);
	}
	public bool RemoveSearchPath(ReadOnlySpan<char> path, ReadOnlySpan<char> pathID) {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		ulong hash = pathID.Hash();
		if (hash == 0) return false;

		if (!SearchPaths.TryGetValue(hash, out var collection))
			return false;

		bool ret = false;

		for (int i = collection.Count - 1; i >= 0; i--) {
			if (collection.GetAddOrder()[i].GetDiskPath() != path)
				continue;
			RemoveSearchPathFromGroup(collection.GetAddOrder()[i], pathID);
			collection.GetAddOrder().RemoveAt(i);
			collection.InvalidateOrder();
			ret = true;
		}

		return ret;
	}

	public void RemoveSearchPaths(ReadOnlySpan<char> pathID) {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		ulong hash = pathID.Hash();
		if (hash == 0) return;
		SearchPaths.Remove(hash);
		foreach (var group in SearchPathGroups) {
			if (group == null)
				continue;
			for (int i = group.Count - 1; i >= 0; i--)
				if (group[i].pathID.Equals(pathID, StringComparison.OrdinalIgnoreCase))
					group.RemoveAt(i);
		}
	}

	readonly ref struct RenameFile_Op(ReadOnlySpan<char> newPath) : IFirstToThePostOp<bool>
	{
		readonly ReadOnlySpan<char> NewPath = newPath;
		public bool Invoke(ISearchPath p, scoped ReadOnlySpan<char> name) => p.RenameFile(name, NewPath);
		public bool Win(bool v) => v;
	}

	public unsafe bool RenameFile(ReadOnlySpan<char> oldPath, ReadOnlySpan<char> newPath, ReadOnlySpan<char> pathID) {
		return FirstToThePost(oldPath, pathID, new RenameFile_Op(newPath), false, out _);
	}

	readonly ref struct SetFileWritable_Op(bool writable) : IFirstToThePostOp<bool>
	{
		readonly bool Writable = writable;
		public bool Invoke(ISearchPath p, scoped ReadOnlySpan<char> name) => p.SetFileWritable(name, Writable);
		public bool Win(bool v) => v;
	}

	public bool SetFileWritable(ReadOnlySpan<char> fileName, bool writable, ReadOnlySpan<char> pathID) {
		return FirstToThePost(fileName, pathID, new SetFileWritable_Op(writable), false, out _);
	}

	readonly ref struct Size_Op() : IFirstToThePostOp<long>
	{
		public long Invoke(ISearchPath p, scoped ReadOnlySpan<char> name) => p.Size(name);
		public bool Win(long v) => v != -1;
	}

	public long Size(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID) {
		return FirstToThePost<long, Size_Op>(fileName, pathID, new Size_Op(), -1, out _);
	}

	readonly ref struct GetFileTime_Op() : IFirstToThePostOp<DateTime>
	{
		public DateTime Invoke(ISearchPath p, scoped ReadOnlySpan<char> name) => p.Time(name);
		public bool Win(DateTime v) => v != DateTime.UnixEpoch;
	}

	public DateTime GetFileTime(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID) {
		return FirstToThePost(fileName, pathID, new GetFileTime_Op(), DateTime.UnixEpoch, out _);
	}

	readonly ref struct FileExists_Op() : IFirstToThePostOp<bool>
	{
		public bool Invoke(ISearchPath p, scoped ReadOnlySpan<char> name) => p.Exists(name);
		public bool Win(bool v) => v;
	}

	public bool FileExists(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID) {
		return FirstToThePost(fileName, pathID, new FileExists_Op(), false, out _);
	}

	public void CreateDirHierarchy(ReadOnlySpan<char> relativePath, ReadOnlySpan<char> pathId) {
		Span<char> scratchFileName = stackalloc char[MAX_PATH];
		ReadOnlySpan<char> relativePathNormalized = ISearchPath.Normalize(relativePath, scratchFileName);
		if (!Path.IsPathFullyQualified(relativePathNormalized)) {
			Assert(!pathId.IsEmpty);
			ComputeFullWritePath(scratchFileName, relativePathNormalized, pathId);
		}
		else {
			relativePathNormalized.CopyTo(scratchFileName);
		}
		
		Directory.CreateDirectory(new(scratchFileName.SliceNullTerminatedString()));
	}
	private ISearchPath? FindWritePath(ReadOnlySpan<char> filename, ReadOnlySpan<char> pathID) {
		ulong hash = pathID.Hash();
		if (hash == 0) return null;

		foreach (var searchPaths in SearchPaths) {
			foreach (var searchPath in searchPaths.Value.GetSortOrder()) {
				if (searchPath is not DiskSearchPath)
					continue;

				if (pathID.IsEmpty || searchPaths.Key == hash)
					return searchPath;
			}
		}
		return null;
	}
	private ReadOnlySpan<char> GetWritePath(ReadOnlySpan<char> filename, ReadOnlySpan<char> pathID) {
		ISearchPath? searchPath = null;
		if (!pathID.IsEmpty && pathID.Length > 0) {
			if (pathID.Equals("game", StringComparison.OrdinalIgnoreCase))
				searchPath = FindWritePath(filename, "game_write");
			else if (pathID.Equals("game", StringComparison.OrdinalIgnoreCase))
				searchPath = FindWritePath(filename, "mod_write");

			searchPath ??= FindWritePath(filename, pathID);
			if (searchPath != null)
				return searchPath.GetPathString();

			Warning("Requested non-existent write path %s!\n", new string(pathID));
		}

		searchPath = FindWritePath(filename, "DEFAULT_WRITE_PATH");
		if (searchPath != null) return searchPath.GetPathString();

		searchPath = FindWritePath(filename, null);
		if (searchPath != null) return searchPath.GetPathString();

		// Hope this is reasonable!!
		return "./";
	}

	private void ComputeFullWritePath(Span<char> dest, ReadOnlySpan<char> relativePath, ReadOnlySpan<char> pathID) {
		string combined = Path.Combine(new(GetWritePath(relativePath, pathID)), new(relativePath));
		combined.AsSpan().CopyTo(dest);
	}

	public bool ReadFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> path, Span<byte> buf, int startingByte) {
		using var handle = Open(fileName, FileOpenOptions.Read, path);
		if (handle == null) return false;

		int bytes = handle.Stream.Read(buf[startingByte..]);
		return bytes > 0;
	}

	public bool ReadFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> path, Span<char> buf, int startingByte) {
		throw new Exception();
	}

	public void GetLocalCopy(ReadOnlySpan<char> path) {

	}

	public void MarkAllCRCsUnverified() {
		// Todo
	}



	public ReadOnlySpan<char> WhereIsFile(ReadOnlySpan<char> fileName, ReadOnlySpan<char> pathID = default) {
		if (FirstToThePost(fileName, pathID, new FileExists_Op(), false, out ISearchPath? path)) {
			Span<char> concatBuffer = stackalloc char[MAX_PATH];
			return new string(ISearchPath.Concat(path, fileName, concatBuffer));
		}
		return null;
	}

	public void PrintSearchPaths() {
		Msg("Paths:\n");

		for (int i = 0; i < SearchPathGroups.Length; i++) {
			var searchpathgroup = SearchPathGroups[i];
			if (searchpathgroup == null || searchpathgroup.Count == 0)
				continue;

			PathGroupName groupName = (PathGroupName)i;
			Msg($"  --- {groupName.ToString().ToUpper()} --- \n");
			foreach (var searchpath in searchpathgroup) {
				ReadOnlySpan<char> pathID = searchpath.pathID;
				ISearchPath spi = searchpath.path;
				ReadOnlySpan<char> pack = "";
				ReadOnlySpan<char> type = "";
				if (false /* TODO: Map-based pack files */) {
					// type = "(map)";
				}
				else if (spi is PackStoreSearchPath pssp) {
					type = "(VPK)";
					pack = pssp.DiskPath;
				}
				else if (spi is DiskSearchPath dsp) {
					pack = dsp.DiskPath;
				}

				Msg($"    \"{pack}\" \"{pathID}\" {type}\n");
			}
		}
	}

	readonly Dictionary<ulong, FileNameHandle_t> fileNameHandles = [];
	readonly Dictionary<FileNameHandle_t, string> fileNameStrings = [];
	FileNameHandle_t currentHandle;

	Span<char> FormatFileName(ReadOnlySpan<char> name, Span<char> newNameBuffer) {
		int newNamePtr = 0;
		for (int i = 0; i < name.Length; i++) {
			char c = char.ToLowerInvariant(name[i]);
			if (c != '/' && c != '\\')
				newNameBuffer[newNamePtr++] = c;
		}

		return newNameBuffer[..newNamePtr];
	}

	public FileNameHandle_t FindFileName(ReadOnlySpan<char> name) {
		using Lock.Scope scope = FileNameLock.EnterScope();
		ulong hash = FormatFileName(name, stackalloc char[name.Length]).Hash();
		return fileNameHandles.TryGetValue(hash, out FileNameHandle_t handle) ? handle : FILENAMEHANDLE_INVALID;
	}
	public FileNameHandle_t FindOrAddFileName(ReadOnlySpan<char> name) {
		using Lock.Scope scope = FileNameLock.EnterScope();
		ulong hash = FormatFileName(name, stackalloc char[name.Length]).Hash();
		if (!fileNameHandles.TryGetValue(hash, out var handle)) {
			handle = fileNameHandles[hash] = ++currentHandle;
			Span<char> lowercased = stackalloc char[name.Length];
			name.ToLower(lowercased, null);
			fileNameStrings[handle] = new(lowercased); // Make a copy of the string to live forever
		}

		return handle;
	}

	public void BeginMapAccess() {

	}

	public void EndMapAccess() {

	}

	public struct FileFindContext
	{
		public int Locked;

		public UtlSymbol Wildcard;
		public UtlSymbol PathID;
		public FileFindHandle_t FindHandle;
		public volatile int FileIdx;
		public volatile int PathIdx;
		public volatile int CollectionIdx;

		int ranAtLeastOnce;
		BaseFileSystem system;
		SearchPathCollection? currentCollection;
		ISearchPath? currentPath;
		HashSet<FileNameHandle_t>? foundAlready;
#if GMOD_DLL
		List<SearchFile>? addonFiles;
#endif

		public bool IsDirectory;

		public void FullyLock(BaseFileSystem system, FileFindHandle_t lockedIdx, ReadOnlySpan<char> wildcard, ReadOnlySpan<char> pathID) {
			this.system = system;
			Reset();

			FindHandle = lockedIdx;
			Wildcard = new UtlSymbol(wildcard);
			PathID = new UtlSymbol(pathID);

#if GMOD_DLL
			if (pathID.IsEmpty) {
				foreach (SearchPathCollection collection in system.SearchPaths.Values) {
					if (collection.RequestOnly)
						continue;
					FindInLooseAddonPaths(collection, wildcard);
				}
			}
			else if (!pathID.Equals("BSP", StringComparison.Ordinal) && system.SearchPaths.TryGetValue(new string(pathID).Hash(), out SearchPathCollection? collection))
				FindInLooseAddonPaths(collection, wildcard);

			if (!pathID.IsEmpty && !pathID.Equals("MOD", StringComparison.Ordinal) && !pathID.Equals("GAME", StringComparison.Ordinal) && !pathID.Equals("workshop", StringComparison.Ordinal))
				g_AddonFileSystem.FindInAddon(new string(pathID), new string(wildcard), addonFiles!);
#endif
		}

#if GMOD_DLL
		readonly void FindInLooseAddonPaths(SearchPathCollection collection, ReadOnlySpan<char> wildcard) {
			for (int i = 0; ; i++) {
				ISearchPath? searchPath = collection.AtSorted(i);
				if (searchPath == null)
					break;
				if (searchPath is not DiskSearchPath)
					continue;

				ReadOnlySpan<char> path = searchPath.GetPathString();
				if (path.Equals("GAME", StringComparison.Ordinal))
					continue;

				string full = $"{path}{wildcard}";
				full = full.Replace('\\', '/');
				g_AddonFileSystem.FindFirst(full, addonFiles!, null);
			}
		}
#endif

		public void Reset() {
			Wildcard = default;
			PathID = default;

			FileIdx = -1;
			PathIdx = -1;
			CollectionIdx = -1;
			ranAtLeastOnce = 0;

			currentCollection = null;
			currentPath = null;

			foundAlready ??= [];
			foundAlready.Clear();
#if GMOD_DLL
			addonFiles ??= [];
			addonFiles.Clear();
#endif
		}



		public ReadOnlySpan<char> Next() {
		findCollection:
			if (currentCollection == null) {
				currentCollection = PathID == 0
					? system.SearchPaths.At(Interlocked.Increment(ref CollectionIdx))
					: Interlocked.CompareExchange(ref ranAtLeastOnce, 1, 0) == 0
					? (system.SearchPaths.TryGetValue(PathID.String().Hash(), out var found) ? found : null)
						: null;

				if (currentCollection != null) {
					// Reset these parts...
					Interlocked.Exchange(ref FileIdx, -1);
					Interlocked.Exchange(ref PathIdx, -1);
					goto findPath; // We don't need to perform the next check
				}
			}
			if (currentCollection == null) {
#if GMOD_DLL
				if (addonFiles!.Count != 0) {
					SearchFile file = addonFiles[0];
					addonFiles.RemoveAt(0);
					IsDirectory = file.Folder;
					return file.FileName;
				}
#endif
				return null; // Cannot continue.
			}

		findPath:
			if (currentPath == null) {
				// Find the next collection.
				currentPath = currentCollection.AtSorted(Interlocked.Increment(ref PathIdx));

				if (currentPath != null) {
					currentPath.LockFinds(Wildcard, foundAlready!);
					Interlocked.Exchange(ref FileIdx, -1);
					// We don't need to perform the next check
					goto findFileDir;
				}
			}

			if (currentPath == null) {
				// Search for a new collection?
				currentCollection = null;
				goto findCollection;
			}

		findFileDir:
			var currentFile = currentPath.FindAt(Interlocked.Increment(ref FileIdx));
			if (!currentFile.HasValue) {
				// Search for a new path?
				currentPath.UnlockFinds();
				currentPath = null;
				goto findPath;
			}
			IsDirectory = currentFile.Value.Item2;
			return currentFile.Value.Item1;
		}

		public void Close() {
			if (Locked == 0) {
				Warning("Tried to unlock a file handle that was already unlocked!!!\n");
				Assert(false);
				return;
			}

			currentPath?.UnlockFinds();
			Locked = 0;
			Reset();
		}
	}

	const int MAX_FILE_HANDLES = 512;
	readonly FileFindContext[] contexts = new FileFindContext[MAX_FILE_HANDLES];
	FileFindHandle_t currentFindHandle;

	public ReadOnlySpan<char> FindFirstEx(ReadOnlySpan<char> wildcard, ReadOnlySpan<char> pathID, out FileFindHandle_t findHandle) {
		for (int i = 0; i < MAX_FILE_HANDLES; i++) {
			findHandle = Interlocked.Increment(ref currentFindHandle);
			ref FileFindContext ctx = ref contexts[(int)(findHandle % MAX_FILE_HANDLES)];
			if (Interlocked.CompareExchange(ref ctx.Locked, 1, 0) == 0) {
				ctx.FullyLock(this, findHandle, wildcard, pathID);
				return ctx.Next();
			}
		}

		throw new Exception("File find error - we likely aren't as thread safe as we had hoped, or 512+ file handles are currently allocated");
	}

	public ReadOnlySpan<char> FindNext(FileFindHandle_t findHandle) {
		ref FileFindContext ctx = ref contexts[(int)(findHandle % MAX_FILE_HANDLES)];
		return ctx.Next();
	}

	public bool FindIsDirectory(FileFindHandle_t findHandle) {
		ref FileFindContext ctx = ref contexts[(int)(findHandle % MAX_FILE_HANDLES)];
		return ctx.IsDirectory;
	}

	public void FindClose(FileFindHandle_t findHandle) {
		ref FileFindContext ctx = ref contexts[(int)(findHandle % MAX_FILE_HANDLES)];
		ctx.Close();
	}

	public ReadOnlySpan<char> String(FileNameHandle_t handle) {
		using Lock.Scope scope = FileNameLock.EnterScope();
		return fileNameStrings.TryGetValue(handle, out string? v) ? v : null;
	}

	public void LoadCompiledKeyValues(IFileSystem.KeyValuesPreloadType type, ReadOnlySpan<char> archiveFile) {
		throw new NotImplementedException();
	}

	public KeyValues? LoadKeyValues(IFileSystem.KeyValuesPreloadType type, ReadOnlySpan<char> filename, ReadOnlySpan<char> pathID = default) {
		KeyValues? kv = new KeyValues(filename);
		kv.LoadFromFile(this, filename, pathID);
		// TODO: There is more here, but need to look more into it (for compiled keyvalues)
		return kv;
	}

	public bool LoadKeyValues(KeyValues head, IFileSystem.KeyValuesPreloadType type, ReadOnlySpan<char> filename, ReadOnlySpan<char> pathID = default) {
		return head.LoadFromFile(this, filename, pathID);
	}

	public bool RemoveSearchPath(ISearchPath searchPathImpl, ReadOnlySpan<char> pathID) {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		ulong hash = pathID.Hash();
		if (hash == 0) return false;

		if (!SearchPaths.TryGetValue(hash, out var collection))
			return false;

		bool ret = false;

		for (int i = collection.Count - 1; i >= 0; i--) {
			if (collection.GetAddOrder()[i] != searchPathImpl)
				continue;
			RemoveSearchPathFromGroup(collection.GetAddOrder()[i], pathID);
			collection.GetAddOrder().RemoveAt(i);
			collection.InvalidateOrder();
			ret = true;
		}

		return ret;
	}

	public bool RemoveSearchPath(Predicate<ISearchPath> search, ReadOnlySpan<char> pathID) {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		ulong hash = pathID.Hash();
		if (hash == 0) return false;

		if (!SearchPaths.TryGetValue(hash, out var collection))
			return false;

		bool ret = false;

		for (int i = collection.Count - 1; i >= 0; i--) {
			if (!search(collection.GetAddOrder()[i]))
				continue;
			RemoveSearchPathFromGroup(collection.GetAddOrder()[i], pathID);
			collection.GetAddOrder().RemoveAt(i);
			collection.InvalidateOrder();
			ret = true;
		}

		return ret;
	}

	public ReadOnlySpan<char> ReadLine(Span<char> output, IFileHandle file) {
		if (output.IsEmpty)
			return default;

		Stream stream = file.Stream;
		int count = 0;
		while (count < output.Length - 1) {
			int c = stream.ReadByte();
			if (c == -1)
				break;
			output[count++] = (char)c;
			if (c == '\n')
				break;
		}

		if (count == 0)
			return default;

		output[count] = '\0';
		return output[..count];
	}

	public void RemoveSearchPathsByGroup(PathGroupName groupName) {
		using Lock.Scope scope = SearchPathLock.EnterScope();
		List<searchPathInternal> group = GetSearchPathGroupsFor(groupName);
		for (int i = group.Count - 1; i >= 0; i--)
			RemoveSearchPath(group[i].path, group[i].pathID);
	}


#if GMOD_DLL
	internal static IGet get = null!;
	internal static readonly AddonFileSystem g_AddonFileSystem = new();
	static readonly GamemodeSystem g_GamemodeSystem = new();
	static readonly GameDepotSystem g_GameDepotSystem = new();
	static readonly LegacyAddonSystem g_LegacyAddons = new();
	static readonly Language2 g_LanguageSystem = new();

	public void SetGet(IGet get) {
		BaseFileSystem.get = get;
	}

	public Addon.FileSystem Addons() => g_AddonFileSystem;
	public Gamemode.System Gamemodes() => g_GamemodeSystem;
	public GameDepot.System Games() => g_GameDepotSystem;
	public LegacyAddons.System LegacyAddons() => g_LegacyAddons;
	public Language Language() => g_LanguageSystem;

	int FilesystemRefresh;

	public void DoFilesystemRefresh() => FilesystemRefresh++;

	public int LastFilesystemRefresh() => FilesystemRefresh;

	public void AddVPKFileFromPath(ReadOnlySpan<char> vpk, ReadOnlySpan<char> path, uint id) {
		AddVPKFile(vpk, path, (SearchPathAdd)id, PathGroupName.Default);
	}

	public void GMOD_SetupDefaultPaths(ReadOnlySpan<char> path, ReadOnlySpan<char> game) {
		string workshop = Path.Combine(new string(game), "workshop");
		AddSearchPath(new AddonSearchPath(g_AddonFileSystem, workshop), "GAME", SearchPathAdd.ToHead, PathGroupName.GModCore);
		AddSearchPath(new AddonSearchPath(g_AddonFileSystem, workshop), "workshop", SearchPathAdd.ToHead, PathGroupName.GModCore);
		AddSearchPath(new AddonSearchPath(g_AddonFileSystem, workshop), "thirdparty", SearchPathAdd.ToHead, PathGroupName.GModCore);
		MarkPathIDByRequestOnly("workshop", true);
		MarkPathIDByRequestOnly("thirdparty", true);
	}

	public void GMOD_FixPathCase(Span<char> a) {

	}

	public WaitForResourcesHandle_t WaitForResources(ReadOnlySpan<char> resourcelist) {
		return 1;
	}

	public bool GetWaitForResourcesProgress(int waitForResourcesHandle, out float progress, out bool complete) {
		progress = 0.0f;
		complete = true;
		return true; 
	}

	public void CancelWaitForResources(WaitForResourcesHandle_t handle){

	}
#endif
}
