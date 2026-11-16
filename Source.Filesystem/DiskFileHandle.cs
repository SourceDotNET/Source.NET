// TODO: Logging calls when things go wrong, ie. try/catches


using Source.Common.Filesystem;

namespace Source.FileSystem;

public sealed class DiskFileStream(BaseFileSystem? filesystem, string path, string traceName, FileMode mode, FileAccess access, FileShare share) : FileStream(path, mode, access, share)
{
	bool closed;

	protected override void Dispose(bool disposing) {
		if (disposing && !closed) {
			closed = true;
			filesystem?.Trace_FClose(this, traceName);
		}

		base.Dispose(disposing);
	}
}

public class DiskFileHandle(IFileSystem filesystem, FileStream data, FileNameHandle_t fileName) : IFileHandle, IDisposable
{
	private bool disposedValue;

	public Stream Stream => data;
	public FileNameHandle_t FileNameHandle => fileName;
	public ReadOnlySpan<char> GetPath() => filesystem.String(fileName);


	protected virtual void Dispose(bool disposing) {
		if (!disposedValue && disposing) {
			data.Dispose();
			disposedValue = true;
		}
	}

	public void Dispose() {
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	public bool IsOK() => !disposedValue && data != null;
}
