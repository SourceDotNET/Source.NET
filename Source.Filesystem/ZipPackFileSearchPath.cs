using SharpCompress.Archives.Zip;

using Source.Common.Filesystem;
using Source.Common.Formats.BSP;
using Source.FileSystem;

namespace Source.Filesystem;

public class ZipArchiveEntryHandle : IFileHandle
{
	// TODO: Memory optimization here. We have to read the full uncompressed stream into a new memory stream -
	// can we read the uncompressed stream into a scratch buffer which is pulled from a stack in case of multiple
	// active ziparchiveentryhandles at a time?
	readonly Stream? stream;

	IFileSystem filesystem;
	FileNameHandle_t fileName;
	ZipArchiveEntry? entry;

	public ZipArchiveEntryHandle(IFileSystem filesystem, FileNameHandle_t fileName, ZipArchiveEntry? entry) {
		this.filesystem = filesystem;
		this.fileName = fileName;
		this.entry = entry;
		Stream? incoming = entry?.OpenEntryStream();
		if (incoming == null)
			return;


		stream = new MemoryStream();
		incoming.CopyTo(stream);
		stream.Position = 0; // << Prepares it for reading
	}

	public Stream Stream => stream!;
	public FileNameHandle_t FileNameHandle => fileName;
	public ReadOnlySpan<char> GetPath() => filesystem.String(fileName);

	public void Dispose() {
		stream?.Dispose();
	}

	public bool IsOK() => entry != null && stream != null;
}

public class ZipPackFileSearchPath : BaseSearchPath
{
	readonly IFileSystem filesystem;
	readonly bool valid;
	readonly ZipArchive archive;
	public bool IsValid() => valid;
	readonly Dictionary<FileNameHandle_t, ZipArchiveEntry> Entries = [];
	public ZipPackFileSearchPath(IFileSystem filesystem, string path, Stream stream, in BSPLump lump) {
		this.filesystem = filesystem;
		SetDiskPath(path);
		byte[] data = lump.ReadBytes(stream);

		// Unpack the zip
		archive = (ZipArchive)ZipArchive.OpenArchive(new MemoryStream(data), new());
		// Create optimized lookup
		foreach(var entry in archive.Entries) {
			Entries[filesystem.FindOrAddFileName(entry.Key)] = entry;
		}

		valid = true;
	}
	private ZipArchiveEntry? getArchive(ReadOnlySpan<char> path) {
		if (Entries.TryGetValue(filesystem.FindOrAddFileName(path), out ZipArchiveEntry? entry))
			return entry;
		return null;
	}
	public override bool Exists(ReadOnlySpan<char> path) => getArchive(path) != null;

	public override bool IsDirectory(ReadOnlySpan<char> path) {
		return false;
	}

	public override bool IsFileWritable(ReadOnlySpan<char> path) => false;

	public override IFileHandle? Open(ReadOnlySpan<char> path, FileOpenOptions options) {
		ZipArchiveEntry? entry = getArchive(path);
		if (entry == null)
			return null;

		lock (archive)
			return new ZipArchiveEntryHandle(filesystem, filesystem.FindOrAddFileName(path), entry);
	}

	public override bool RemoveFile(ReadOnlySpan<char> path) => false;

	public override bool RenameFile(ReadOnlySpan<char> oldPath, ReadOnlySpan<char> newPath) => false;

	public override bool SetFileWritable(ReadOnlySpan<char> path, bool writable) => false;

	public override long Size(ReadOnlySpan<char> path) {
		if (Entries.TryGetValue(filesystem.FindOrAddFileName(path), out ZipArchiveEntry? entry))
			return entry.Size;
		return 0;
	}

	public override DateTime Time(ReadOnlySpan<char> path) {
		if (Entries.TryGetValue(filesystem.FindOrAddFileName(path), out ZipArchiveEntry? entry))
			return entry.LastModifiedTime ?? default;
		return DateTime.UnixEpoch;
	}

	public override object? GetPackedStore() => null;

	public override object? GetPackFile() => null;

	public override ReadOnlySpan<char> GetPathString() => this.DiskPath;

	public override void PrepareFinds(List<string> files, List<string> dirs, string? wildcard) {
		// TODO.
	}
}
