using Source.Common.Filesystem;
using Source.FileSystem;

namespace Source.Filesystem.GarrysMod;

public class AddonSearchPath : BaseSearchPath
{
	readonly AddonFileSystem Addons;

	public AddonSearchPath(AddonFileSystem addons, string absPath) {
		Addons = addons;
		SetDiskPath(absPath);
	}

	public override bool Exists(ReadOnlySpan<char> path) => Addons.GetFile(new string(path), out _) || Addons.IsDirectory(new string(path));
	public override bool IsDirectory(ReadOnlySpan<char> path) => Addons.IsDirectory(new string(path));
	public override bool IsFileWritable(ReadOnlySpan<char> path) => false;

	public override IFileHandle? Open(ReadOnlySpan<char> path, FileOpenOptions options) {
		if (options.GetOperation() != FileOpenOptions.Read)
			return null;

		return Addons.GetFileEntry(new string(path));
	}

	public override bool RemoveFile(ReadOnlySpan<char> path) => false;
	public override bool RenameFile(ReadOnlySpan<char> oldPath, ReadOnlySpan<char> newPath) => false;
	public override bool SetFileWritable(ReadOnlySpan<char> path, bool writable) => false;
	public override long Size(ReadOnlySpan<char> path) => Addons.GetFileSize(new string(path));
	public override DateTime Time(ReadOnlySpan<char> path) => DateTime.UnixEpoch;
	public override ReadOnlySpan<char> GetPathString() => DiskPath;
	public override object? GetPackFile() => null;
	public override object? GetPackedStore() => null;

	public override void PrepareFinds(List<string> files, List<string> dirs, string? wildcard) {
		List<SearchFile> results = [];
		Addons.FindFirst(wildcard ?? "*", results, null);

		foreach (SearchFile result in results) {
			if (result.Folder)
				dirs.Add(result.FileName);
			else
				files.Add(result.FileName);
		}
	}
}
