using Source.Common.Filesystem;
using Source.FileSystem;
using Source.VPK;


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
	private readonly IFileSystem parent;
	private readonly VpkArchive vpk;
	private readonly string VpkPath;

	private Dictionary<UtlSymId_t, VpkEntry> vpkEntryLookups = [];
	private Dictionary<UtlSymId_t, VpkDirectory> vpkDirectoryLookups = [];

	~PackStoreSearchPath(){
		vpk.Dispose();
	}

	public PackStoreSearchPath(IFileSystem filesystem, string absPath) {
		absPath = absPath.EndsWith(".vpk") ? absPath.Substring(0, absPath.Length - ".vpk".Length) : absPath;
		absPath = absPath.EndsWith("_dir") ? absPath.Substring(0, absPath.Length - "_dir".Length) : absPath;
		absPath = absPath.Replace('\\', '/');
		absPath = $"{absPath}_dir.vpk";
		VpkPath = absPath;
		parent = filesystem;
		vpk = new VpkArchive();
		vpk.Load(absPath);

		Span<char> buildPath = stackalloc char[260];
		foreach (var dir in vpk.Directories) {
			vpkDirectoryLookups[dir.Path.Hash()] = dir;

			foreach (var entry in dir.Entries) {
				var path = entry.Path.Replace('\\', '/');
				var filename = entry.Filename;
				var ext = entry.Extension;

				int strlen = 0;
				if (path.Length > 0 && path[0] != ' ') {
					path.CopyTo(buildPath[strlen..]); strlen += path.Length;
					buildPath[strlen] = '/'; strlen += 1;
				}
				filename.CopyTo(buildPath[strlen..]); strlen += filename.Length;
				buildPath[strlen] = '.'; strlen += 1;
				ext.CopyTo(buildPath[strlen..]); strlen += ext.Length;

				ReadOnlySpan<char> finalSpan = buildPath[..strlen];
				vpkEntryLookups[finalSpan.Hash()] = entry;
			}
		}

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
		if (vpkEntryLookups.TryGetValue(path.Hash(), out VpkEntry? entry))
			return new VpkFileHandle(parent, parent.FindOrAddFileName(path), new MemoryStream(entry.Data));

		return null;
	}

	public override bool RemoveFile(ReadOnlySpan<char> path) => false;

	public override bool RenameFile(ReadOnlySpan<char> oldPath, ReadOnlySpan<char> newPath) => false;
	public override bool SetFileWritable(ReadOnlySpan<char> path, bool writable) => false;

	public override long Size(ReadOnlySpan<char> path) {
		if (vpkEntryLookups.TryGetValue(path.Hash(), out VpkEntry? entry))
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

		foreach (var directory in vpkDirectoryLookups) {
			VpkDirectory dir = directory.Value;
			string path = dir.Path;
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

			if (wildcardAsteriskAtDir == -1) {
				if (!entry.Path.PathEquals(wildcardDir))
					continue;
			}
			else {
				if (wildcardDir != "*" && !entry.Path.PathStartsWith(wildcardDir))
					continue;
			}

			if (wildcardAsteriskAtFile == -1) {
				if (!entry.Filename.PathEquals(wildcardFile))
					continue;
			}
			else {
				if (wildcardFile[0] != '*' && !entry.Filename.PathStartsWith(wildcardFile))
					continue;
			}

			if (wildcardAsteriskAtExt == -1) {
				if (!entry.Extension.PathEquals(wildcardExt))
					continue;
			}
			else {
				if (!wildcardExt.IsEmpty && wildcardExt[0] != '*' && !entry.Extension.PathStartsWith(wildcardExt))
					continue;
			}

			files.Add(entry.FilenameAndExtension);
		}
	}
}
