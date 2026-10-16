using Source.Common.Filesystem;
using Source.FileSystem;
using Source.VPK;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Source.Filesystem;

public class VpkFileHandle(IFileSystem filesystem, FileNameHandle_t fileName, MemoryStream data) : IFileHandle, IDisposable
{
	private bool disposedValue;

	public Stream Stream => data;
	public FileNameHandle_t FileNameHandle => fileName;
	public ReadOnlySpan<char> GetPath() => filesystem.String(fileName);

	protected virtual void Dispose(bool disposing) {
		if (!disposedValue && disposing)
			data.Dispose();
	}

	public void Dispose() {
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public bool IsOK() => !disposedValue && data != null;
}

public class PackStoreSearchPath : BaseSearchPath
{
	sealed class PackStore
	{
		public readonly string Key;
		public readonly VpkArchive Vpk = new();
		public readonly Dictionary<UtlSymId_t, VpkEntry> EntryLookups = [];
		public readonly Dictionary<UtlSymId_t, VpkDirectory> DirectoryLookups = [];
		public int References;

		public PackStore(string key, string vpkPath) {
			Key = key;
			Vpk.Load(vpkPath);

			DirectoryLookups.EnsureCapacity(Vpk.Directories.Count);
			EntryLookups.EnsureCapacity(Vpk.EntryCount);

			Span<char> buildPath = stackalloc char[260];
			foreach (VpkDirectory dir in Vpk.Directories) {
				int pathLength = Encoding.Latin1.GetChars(Vpk.GetPath(in dir), buildPath);
				DirectoryLookups[((ReadOnlySpan<char>)buildPath[..pathLength]).Hash()] = dir;
			}

			foreach (VpkEntry entry in Vpk.EnumerateEntries()) {
				ReadOnlySpan<byte> path = Vpk.GetPath(in entry);
				ReadOnlySpan<byte> filename = Vpk.GetFilename(in entry);
				ReadOnlySpan<byte> ext = Vpk.GetExtension(in entry);

				int strlen = 0;
				if (path.Length > 0 && path[0] != ' ') {
					strlen += Encoding.Latin1.GetChars(path, buildPath[strlen..]);
					buildPath[..strlen].Replace('\\', '/');
					buildPath[strlen] = '/'; strlen += 1;
				}
				strlen += Encoding.Latin1.GetChars(filename, buildPath[strlen..]);
				buildPath[strlen] = '.'; strlen += 1;
				strlen += Encoding.Latin1.GetChars(ext, buildPath[strlen..]);

				ReadOnlySpan<char> finalSpan = buildPath[..strlen];
				EntryLookups[finalSpan.Hash()] = entry;
			}
		}
	}

	static readonly Dictionary<string, PackStore> packStores = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

	static PackStore AcquirePackStore(string vpkPath) {
		string key = Path.GetFullPath(vpkPath);
		lock (packStores) {
			if (!packStores.TryGetValue(key, out PackStore? store)) {
				store = new(key, vpkPath);
				packStores[key] = store;
			}

			store.References++;
			return store;
		}
	}

	static void ReleasePackStore(PackStore store) {
		lock (packStores) {
			if (--store.References > 0)
				return;

			packStores.Remove(store.Key);
		}

		store.Vpk.Dispose();
	}

	private readonly IFileSystem parent;
	private readonly PackStore? store;
	private readonly string VpkPath;

	private readonly Dictionary<UtlSymId_t, VpkEntry> vpkEntryLookups;
	private readonly Dictionary<UtlSymId_t, VpkDirectory> vpkDirectoryLookups;

	~PackStoreSearchPath(){
		if (store != null)
			ReleasePackStore(store);
	}

	public PackStoreSearchPath(IFileSystem filesystem, string absPath) {
		absPath = absPath.EndsWith(".vpk") ? absPath.Substring(0, absPath.Length - ".vpk".Length) : absPath;
		absPath = absPath.EndsWith("_dir") ? absPath.Substring(0, absPath.Length - "_dir".Length) : absPath;
		absPath = absPath.Replace('\\', '/');
		absPath = $"{absPath}_dir.vpk";
		VpkPath = absPath;
		parent = filesystem;
		store = AcquirePackStore(absPath);
		vpkEntryLookups = store.EntryLookups;
		vpkDirectoryLookups = store.DirectoryLookups;

		if (!Path.IsPathFullyQualified(absPath))
			absPath = Path.GetFullPath(absPath);

		SetDiskPath(absPath);
	}

	public override bool Exists(ReadOnlySpan<char> path) {
		ulong hash = path.Hash();
		return vpkEntryLookups.ContainsKey(hash) || vpkDirectoryLookups.ContainsKey(hash);
	}

	public override bool IsDirectory(ReadOnlySpan<char> path) {
		return vpkDirectoryLookups.ContainsKey(path.Hash());
	}

	public override bool IsFileWritable(ReadOnlySpan<char> path) {
		return false;
	}

	public override IFileHandle? Open(ReadOnlySpan<char> path, FileOpenOptions options) {
		ref VpkEntry entry = ref CollectionsMarshal.GetValueRefOrNullRef(vpkEntryLookups, path.Hash());
		if (!Unsafe.IsNullRef(ref entry))
			return new VpkFileHandle(parent, parent.FindOrAddFileName(path), new MemoryStream(store!.Vpk.ReadData(in entry)));

		return null;
	}

	public override bool RemoveFile(ReadOnlySpan<char> path) => false;

	public override bool RenameFile(ReadOnlySpan<char> oldPath, ReadOnlySpan<char> newPath) => false;
	public override bool SetFileWritable(ReadOnlySpan<char> path, bool writable) => false;

	public override long Size(ReadOnlySpan<char> path) {
		ref VpkEntry entry = ref CollectionsMarshal.GetValueRefOrNullRef(vpkEntryLookups, path.Hash());
		if (!Unsafe.IsNullRef(ref entry))
			return entry.EntryLength;
		return -1;
	}

	public override DateTime Time(ReadOnlySpan<char> path) {
		if (!vpkEntryLookups.ContainsKey(path.Hash()))
			return DateTime.UnixEpoch;
		return File.GetLastWriteTimeUtc(VpkPath);
	}

	public override object? GetPackedStore() {
		return null; // TODO: Review GetPackedStore again
	}

	public override object? GetPackFile() {
		return null; // TODO: Review GetPackFile again
	}

	public override ReadOnlySpan<char> GetPathString() {
		return DiskPath;
	}

	public override void PrepareFinds(List<string> files, List<string> dirs, string? wildcard) {
		ReadOnlySpan<char> wildcardDir = string.Empty, wildcardFile = string.Empty, wildcardExt = string.Empty;
		wildcard?.FileInfo(null, out wildcardDir, out wildcardFile, out wildcardExt);

		int wildcardAsteriskAtDir = wildcardDir.IndexOf('*');
		int wildcardAsteriskAtFile = wildcardFile.IndexOf('*');
		int wildcardAsteriskAtExt = wildcardExt.IndexOf('*');

		VpkArchive vpk = store!.Vpk;
		Span<char> pathBuffer = stackalloc char[260];
		Span<char> filenameBuffer = stackalloc char[260];
		Span<char> extBuffer = stackalloc char[260];

		foreach (var directory in vpkDirectoryLookups) {
			VpkDirectory dir = directory.Value;
			ReadOnlySpan<char> path = pathBuffer[..Encoding.Latin1.GetChars(vpk.GetPath(in dir), pathBuffer)];
			path.FileInfo(null, out var baseDirectory, out var baseName, out _);

			if (wildcardAsteriskAtDir == -1) {
				if (!baseDirectory.PathEquals(wildcardDir))
					continue;
			}
			else {
				if (!path.PathStartsWith(wildcardDir))
					continue;
			}

			if (wildcard != null && !System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(Path.GetFileName(wildcard), baseName))
				continue;

			dirs.Add(new(baseName));
		}

		foreach (var entryKVP in vpkEntryLookups) {
			VpkEntry entry = entryKVP.Value;
			ReadOnlySpan<char> entryPath = pathBuffer[..Encoding.Latin1.GetChars(vpk.GetPath(in entry), pathBuffer)];

			if (wildcardAsteriskAtDir == -1) {
				if (!entryPath.PathEquals(wildcardDir))
					continue;
			}
			else {
				if (wildcardDir != "*" && !entryPath.PathStartsWith(wildcardDir))
					continue;
			}

			ReadOnlySpan<char> entryFilename = filenameBuffer[..Encoding.Latin1.GetChars(vpk.GetFilename(in entry), filenameBuffer)];

			if (wildcardAsteriskAtFile == -1) {
				if (!entryFilename.PathEquals(wildcardFile))
					continue;
			}
			else {
				if (wildcardFile[0] != '*' && !entryFilename.PathStartsWith(wildcardFile))
					continue;
			}

			ReadOnlySpan<char> entryExt = extBuffer[..Encoding.Latin1.GetChars(vpk.GetExtension(in entry), extBuffer)];

			if (wildcardAsteriskAtExt == -1) {
				if (!entryExt.PathEquals(wildcardExt))
					continue;
			}
			else {
				if (!wildcardExt.IsEmpty && wildcardExt[0] != '*' && !entryExt.PathStartsWith(wildcardExt))
					continue;
			}

			files.Add(string.Concat(entryFilename, ".", entryExt));
		}
	}
}
