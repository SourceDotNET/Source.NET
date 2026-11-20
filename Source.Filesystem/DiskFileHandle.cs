// TODO: Logging calls when things go wrong, ie. try/catches


using Source.Common.Filesystem;

using System.Buffers;

namespace Source.FileSystem;

public sealed class DiskFileStream(BaseFileSystem? filesystem, string path, string traceName, FileMode mode, FileAccess access, FileShare share) : FileStream(path, mode, access, share)
{
	bool closed;

	public override int Read(byte[] buffer, int offset, int count) {
		int bytesRead = base.Read(buffer, offset, count);
		filesystem?.Trace_FRead(bytesRead, this, traceName);
		return bytesRead;
	}

	public override int ReadByte() {
		int value = base.ReadByte();
		filesystem?.Trace_FRead(value < 0 ? 0 : 1, this, traceName);
		return value;
	}

	public override void CopyTo(Stream destination, int bufferSize) {
		byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
		try {
			int bytesRead;
			while ((bytesRead = Read(buffer, 0, buffer.Length)) != 0)
				destination.Write(buffer, 0, bytesRead);
		}
		finally {
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}

	public override void Write(byte[] buffer, int offset, int count) {
		base.Write(buffer, offset, count);
		filesystem?.Trace_FWrite(count, this, traceName);
	}

	public override void WriteByte(byte value) {
		base.WriteByte(value);
		filesystem?.Trace_FWrite(1, this, traceName);
	}

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
