using Source.Common.Bitmap;
using Source.Common.Utilities;

namespace Source.Bitmap;

public static class TGAWriter
{
	public static bool WriteToBuffer(Span<byte> imageData, UtlBuffer buffer, int width, int height, ImageFormat srcFormat, ImageFormat dstFormat) {
		switch (dstFormat) {
			case ImageFormat.RGB888:
				dstFormat = ImageFormat.BGR888;
				break;
			case ImageFormat.RGBA8888:
				dstFormat = ImageFormat.BGRA8888;
				break;
		}

		byte imageType;
		byte pixelSize;
		switch (dstFormat) {
			case ImageFormat.BGR888:
				imageType = 2;
				pixelSize = 24;
				break;
			case ImageFormat.BGRA8888:
				imageType = 2;
				pixelSize = 32;
				break;
			case ImageFormat.I8:
				imageType = 1;
				pixelSize = 8;
				break;
			default:
				return false;
		}

		buffer.PutType<byte>(0);
		buffer.PutType<byte>(0);
		buffer.PutType(imageType);
		buffer.PutUnsignedShort(0);
		buffer.PutUnsignedShort(0);
		buffer.PutType<byte>(0);
		buffer.PutUnsignedShort(0);
		buffer.PutUnsignedShort(0);
		buffer.PutUnsignedShort((ushort)width);
		buffer.PutUnsignedShort((ushort)height);
		buffer.PutType(pixelSize);
		buffer.PutType<byte>(0x20);

		int sizeInBytes = width * height * dstFormat.SizeInBytes();
		buffer.EnsureCapacity(buffer.TellPut() + sizeInBytes);
		Span<byte> dst = buffer.Base().AsSpan(buffer.TellPut(), sizeInBytes);

		if (!ImageLoader.ConvertImageFormat(imageData, srcFormat, dst, dstFormat, width, height))
			return false;

		buffer.SeekPut(SeekOrigin.Current, sizeInBytes);
		return true;
	}
}
