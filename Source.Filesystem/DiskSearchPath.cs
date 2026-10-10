// TODO: Logging calls when things go wrong, ie. try/catches
#if !WIN32
#define FORCE_CASE_INSENSITIVE_ON_DISK
#endif


using Source.Common.Filesystem;

namespace Source.FileSystem;

public class DiskSearchPath : BaseSearchPath
{
#if FORCE_CASE_INSENSITIVE_ON_DISK
	class CaseInsensitiveCache {
		public required string RealPath;
	}
	static readonly Dictionary<ulong, CaseInsensitiveCache> cache = [];
	static string ResolveDiskPath(string path) {
		ulong hash = path.Hash(invariant: true);
		if(cache.TryGetValue(hash, out CaseInsensitiveCache? found))
			return found.RealPath;

		if (Path.Exists(path))
			return path;

		string? directory = Path.GetDirectoryName(path);
		if (directory == null)
			return path;

		string resolvedDir = ResolveDiskPath(directory);
		if (!Directory.Exists(resolvedDir))
			return path;

		string fileName = Path.GetFileName(path);
		foreach (string entry in Directory.EnumerateFileSystemEntries(resolvedDir)) {
			if (string.Equals(Path.GetFileName(entry), fileName, StringComparison.OrdinalIgnoreCase)) {
				cache[hash] = new() {
					RealPath = entry
				};
				return entry;
			}
		}

		return path;
	}
#else
	static string ResolveDiskPath(string path) {
		return path;
	}
#endif

	private IFileSystem parent;
	public DiskSearchPath(IFileSystem filesystem, string absPath) {
		parent = filesystem;

		if (!Path.IsPathFullyQualified(absPath))
			absPath = Path.GetFullPath(absPath);

		SetDiskPath(absPath);
	}

	private string GetAbsPath(ReadOnlySpan<char> relPath) => ResolveDiskPath(Path.Combine(DiskPath!, new(relPath)));

	public override bool Exists(ReadOnlySpan<char> path) => Path.Exists(GetAbsPath(path));
	public override bool IsDirectory(ReadOnlySpan<char> path) => Directory.Exists(GetAbsPath(path));

	public override bool IsFileWritable(ReadOnlySpan<char> path) {
		var info = new FileInfo(GetAbsPath(path));
		return info.Exists && !info.IsReadOnly;
	}

	public override IFileHandle? Open(ReadOnlySpan<char> path, FileOpenOptions options) {
		string absPath = GetAbsPath(path);
		var info = new FileInfo(absPath);

		FileOpenOptions operation = options.GetOperation();

		// Scram early if the file doesn't even exist
		if (!info.Exists && operation == FileOpenOptions.Read) return null;

		// Check file options for invalid access
		if (operation == FileOpenOptions.Write && info.IsReadOnly && info.Exists)
			return null;

		// Open the file stream
		FileMode mode = operation switch {
			FileOpenOptions.Read => FileMode.Open,
			FileOpenOptions.Write => FileMode.Create,
			FileOpenOptions.Append => FileMode.Append,
			_ => throw new NotSupportedException()
		};

		FileAccess access = options.Extended() ? FileAccess.ReadWrite : operation switch {
			FileOpenOptions.Read => FileAccess.Read,
			FileOpenOptions.Write => FileAccess.Write,
			FileOpenOptions.Append => FileAccess.Write,
			_ => throw new NotSupportedException()
		};

		FileShare share = access == FileAccess.Read ? FileShare.ReadWrite | FileShare.Delete : FileShare.Read;

		try {
			return new DiskFileHandle(parent, info.Open(mode, access, share), parent.FindOrAddFileName(path));
		}
		catch {
			return null;
		}
	}

	public override bool RemoveFile(ReadOnlySpan<char> path) {
		var absPath = GetAbsPath(path);
		var info = new FileInfo(absPath);
		if (!info.Exists) return false;
		if (info.IsReadOnly) return false;

		try {
			info.Delete();
		}
		catch {
			return false;
		}

		return true;
	}

	public override bool RenameFile(ReadOnlySpan<char> oldPath, ReadOnlySpan<char> newPath) {
		var absPath = GetAbsPath(oldPath);
		var info = new FileInfo(absPath);
		if (!info.Exists) return false;
		if (info.IsReadOnly) return false;

		try {
			info.MoveTo(GetAbsPath(newPath));
		}
		catch {
			return false;
		}

		return true;
	}

	// Do nothing
	public override bool SetFileWritable(ReadOnlySpan<char> path, bool writable) => false;

	public override long Size(ReadOnlySpan<char> path) {
		var absPath = GetAbsPath(path);
		var info = new FileInfo(absPath);
		if (!info.Exists) return -1;

		return info.Length;
	}

	public override DateTime Time(ReadOnlySpan<char> path) {
		var absPath = GetAbsPath(path);
		var info = new FileInfo(absPath);
		if (!info.Exists) return DateTime.UnixEpoch;

		return info.LastWriteTimeUtc;
	}

	public override ReadOnlySpan<char> GetPathString() => DiskPath;

	public override object? GetPackFile() {
		return null;
	}

	public override object? GetPackedStore() {
		return null;
	}

	public override void PrepareFinds(List<string> files, List<string> dirs, string? wildcard) {
		IEnumerable<string> fileSearch, dirSearch;

		if (wildcard != null) {
			var directory = Path.GetDirectoryName(Path.Combine(DiskPath!, wildcard));
			var pattern = Path.GetFileName(wildcard);
			if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
				return;

			fileSearch = Directory.EnumerateFiles(directory, pattern);
			dirSearch = Directory.EnumerateDirectories(directory, pattern);
		}
		else {
			fileSearch = Directory.EnumerateFiles(DiskPath!);
			dirSearch = Directory.EnumerateDirectories(DiskPath!);
		}

		foreach (var file in fileSearch)
			files.Add(Path.GetFileName(file));
		foreach (var dir in dirSearch)
			dirs.Add(Path.GetFileName(dir));
	}
}
