using Source.Common.Filesystem;

using System.Xml.Linq;

using static Source.Common.UtlCachedFileDataGlobals;
namespace Source.Common;

public delegate uint ComputeCacheMetaChecksumFn();

public enum UtlCachedFileDataType
{
	UseTimestamp = 0,
	UseFilesize,
}

[EngineComponent]
public static class UtlCachedFileDataGlobals
{
	[Dependency] public static IFileSystem filesystem = null!;
}

public interface IBaseCacheInfo {
	void Save(Stream buf);
	void Restore(Stream buf);
	void Rebuild(ReadOnlySpan<char> filename);
}

// This deviates a little.
// Should probably redo it sometime.
public class UtlCachedFileData<T> where T : class, IBaseCacheInfo, new()
{
	const long UTL_CACHED_FILE_DATA_UNDEFINED_DISKINFO = -2;
	class ElementType
	{
		public FileNameHandle_t Handle;
		public long FileInfo;
		public long DiskFileInfo;
		public int DataIndex;
		public ElementType() {
			FileInfo = 0;
			DiskFileInfo = UTL_CACHED_FILE_DATA_UNDEFINED_DISKINFO;
		}
	}

	readonly Dictionary<FileNameHandle_t, ElementType> Elements = [];
	readonly List<T> Data = [];
	readonly string RepositoryFileName;
	readonly int Version;
	readonly ComputeCacheMetaChecksumFn? FnMetaChecksum;
	readonly uint CurrentMetaChecksum;
	readonly UtlCachedFileDataType FileCheckType;
	readonly bool NeverCheckDisk;
	readonly bool ReadOnly;
	readonly bool SaveManifest;
	bool Dirty;
	bool Initialized;

	public UtlCachedFileData(ReadOnlySpan<char> repositoryFileName, int version, ComputeCacheMetaChecksumFn? checksumfunc = null, UtlCachedFileDataType fileCheckType = UtlCachedFileDataType.UseTimestamp, bool nevercheckdisk = false, bool isReadonly = false, bool savemanifest = false) {
		RepositoryFileName = new(repositoryFileName.SliceNullTerminatedString());
		Version = version;
		FnMetaChecksum = checksumfunc;
		CurrentMetaChecksum = 0;
		FileCheckType = fileCheckType;
		NeverCheckDisk = nevercheckdisk;
		ReadOnly = isReadonly;
		SaveManifest = savemanifest;
		Dirty = false;
		Initialized = false;
	}

	public bool Init() {
		if (Initialized)
			return true;
		Initialized = true;
		if(RepositoryFileName.Length == 0)
			Error("UtlCachedFileData:  Can't Init, no repository file specified.");
		//todo
		return true; 
	}

	public bool EntryExists(ReadOnlySpan<char> filename) {
		FileNameHandle_t handle = filesystem.FindOrAddFileName(filename);
		return Elements.ContainsKey(handle);
	}

	public T? Get(ReadOnlySpan<char> filename) {
		FileNameHandle_t idx = GetIndex(filename);
		ElementType e = Elements[idx];

		long cachefileinfo = e.FileInfo;
		// Set the disk fileinfo the first time we encounter the filename
		if (e.DiskFileInfo == UTL_CACHED_FILE_DATA_UNDEFINED_DISKINFO) {
			if (NeverCheckDisk) {
				e.DiskFileInfo = cachefileinfo;
			}
			else {
				if (FileCheckType == UtlCachedFileDataType.UseFilesize) {
					e.DiskFileInfo = filesystem.Size(filename, "GAME");
					// Missing files get a disk file size of 0
					if (e.DiskFileInfo == -1) 
						e.DiskFileInfo = 0;
				}
				else {
					e.DiskFileInfo = filesystem.GetFileTime(filename, "GAME").Ticks;
				}
			}
		}

		T data = Data[e.DataIndex];

		// Compare fileinfo to disk fileinfo and rebuild cache if out of date or not correct...
		if (cachefileinfo != e.DiskFileInfo) {
			if (!ReadOnly) {
				RebuildCache(filename, data);
			}
			e.FileInfo = e.DiskFileInfo;
		}

		return data;
	}

	private void RebuildCache(ReadOnlySpan<char> filename, T data) {
		SetDirty(true);
		data.Rebuild(filename);
	}

	private void SetDirty(bool v) {
		Dirty = true;
	}

	private FileNameHandle_t GetIndex(ReadOnlySpan<char> filename) {
		FileNameHandle_t handle = filesystem.FindOrAddFileName(filename);
		if (Elements.TryGetValue(handle, out ElementType? v))
			return handle;

		T data = new T();
		int dataIndex = Data.Count;
		Data.Add(data);
		Elements[handle] = new() {
			Handle = handle,
			DataIndex = dataIndex
		};

		return handle;
	}

	public bool IsUpToDate() {
		return true; // todo
	}

	public int Count() => Elements.Count;
}
