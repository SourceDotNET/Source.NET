// TODO: Logging calls when things go wrong, ie. try/catches


using Source.Common.Filesystem;
using Source.Common.Utilities;

namespace Source.FileSystem;

public abstract class BaseSearchPath : ISearchPath
{
	public string? DiskPath { get; private set; }
	public ReadOnlySpan<char> GetDiskPath() => DiskPath;
	public void SetDiskPath(scoped ReadOnlySpan<char> diskPath) {
		DiskPath = new(diskPath.SliceNullTerminatedString());
	}
	PathGroupName name;
	public PathGroupName GetGroupName() => name;
	public void SetGroupName(PathGroupName value) => name = value;


	public abstract bool Exists(scoped ReadOnlySpan<char> path); // Returns if the file or directory exists
	public abstract bool IsDirectory(scoped ReadOnlySpan<char> path); // Returns true if the path is a directory
	public abstract bool IsFileWritable(scoped ReadOnlySpan<char> path); // Returns true if the path can be written to
	public abstract IFileHandle? Open(scoped ReadOnlySpan<char> path, FileOpenOptions options); // Can return null if something went wrong
	public abstract bool RemoveFile(scoped ReadOnlySpan<char> path); // Return true if the file was deleted
	public abstract bool RenameFile(scoped ReadOnlySpan<char> oldPath, scoped ReadOnlySpan<char> newPath); // Renames a single file, returns true if it worked
	public abstract bool SetFileWritable(scoped ReadOnlySpan<char> path, bool writable); // Determines if the file is writable
	public abstract long Size(scoped ReadOnlySpan<char> path); // Gets the size of a file
	/// <summary>
	/// Gets the last modified time of a file (UTC)
	/// </summary>
	/// <param name="path"></param>
	/// <returns></returns>
	public abstract DateTime Time(scoped ReadOnlySpan<char> path);
	public abstract ReadOnlySpan<char> GetPathString();
	public abstract object? GetPackFile();
	public abstract object? GetPackedStore();

	public abstract void PrepareFinds(List<string> files, List<string> dirs, string? wildcard);
}
