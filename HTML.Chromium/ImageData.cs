using System.Runtime.InteropServices;

namespace HTML.Chromium;

public class ImageData
{
	readonly Lock Mutex = new();
	bool Dirty;
	int Wide;
	int Tall;
	byte[]? Data;

	public void Lock() => Mutex.Enter();
	public void Unlock() => Mutex.Exit();

	public void SetData(nint data, int wide, int tall) {
		int bufSize = wide * tall * 4;

		if (Wide != wide || Tall != tall || Data == null)
			Data = new byte[bufSize];

		Marshal.Copy(data, Data, 0, bufSize);
		Wide = wide;
		Tall = tall;
		Dirty = true;
	}

	public void CopyImage(byte[]? data, int x, int y, int w, int h) {
		if (w < 0 || h < 0 || data == null || Data == null)
			return;

		int dstX = Math.Max(0, x);
		int dstY = Math.Max(0, y);
		int srcX = Math.Max(0, -x);
		int srcY = Math.Max(0, -y);

		int copyW = Math.Min(w - srcX, Wide - dstX);
		int copyH = Math.Min(h - srcY, Tall - dstY);

		if (copyW > 0 && copyH > 0) {
			for (int row = 0; row < copyH; ++row) {
				int dstOffset = ((dstY + row) * Wide + dstX) * 4;
				int srcOffset = ((srcY + row) * w + srcX) * 4;
				Buffer.BlockCopy(data, srcOffset, Data, dstOffset, copyW * 4);
			}

			Dirty = true;
		}
	}

	public ReadOnlySpan<byte> GetData(out int wide, out int tall) {
		wide = Wide;
		tall = Tall;
		return Data;
	}

	public bool IsDirty() => Dirty;
	public void SetDirty(bool dirty) => Dirty = dirty;
}
