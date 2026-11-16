// TODO: Logging calls when things go wrong, ie. try/catches


using Source.Common.Filesystem;

namespace Source.FileSystem;

public class DiskFileHandle(IFileSystem filesystem, FileStream data, FileNameHandle_t fileName, string? fullPath = null) : IFileHandle, IDisposable
{
	private bool disposedValue;

	public Stream Stream => data;
	public FileNameHandle_t FileNameHandle => fileName;
	public ReadOnlySpan<char> GetPath() => filesystem.String(fileName);


	protected virtual void Dispose(bool disposing) {
		if (!disposedValue && disposing) {
			if (fullPath != null)
				(filesystem as BaseFileSystem)?.Trace_FClose(this, fullPath);
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
