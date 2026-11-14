using Source.Common.Bitmap;

using System.Runtime.InteropServices;

namespace Source.Bitmap;

public static partial class ImageLoader
{
	delegate void UserFormatToRGBA8888Func(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels);
	delegate void RGBA8888ToUserFormatFunc(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels);

	static UserFormatToRGBA8888Func? GetUserFormatToRGBA8888Func(ImageFormat srcImageFormat) {
		switch (srcImageFormat) {
			case ImageFormat.RGBA8888:
				return RGBA8888ToRGBA8888;
			case ImageFormat.ABGR8888:
				return ABGR8888ToRGBA8888;
			case ImageFormat.RGB888:
				return RGB888ToRGBA8888;
			case ImageFormat.BGR888:
				return BGR888ToRGBA8888;
			case ImageFormat.RGB565:
				return null;
			case ImageFormat.I8:
				return I8ToRGBA8888;
			case ImageFormat.IA88:
				return IA88ToRGBA8888;
			case ImageFormat.A8:
				return A8ToRGBA8888;
			case ImageFormat.RGB888_Bluescreen:
				return RGB888_BLUESCREENToRGBA8888;
			case ImageFormat.BGR888_Bluescreen:
				return BGR888_BLUESCREENToRGBA8888;
			case ImageFormat.ARGB8888:
				return ARGB8888ToRGBA8888;
			case ImageFormat.BGRA8888:
				return BGRA8888ToRGBA8888;
			case ImageFormat.BGRX8888:
				return BGRX8888ToRGBA8888;
			case ImageFormat.BGR565:
				return BGR565ToRGBA8888;
			case ImageFormat.BGRX5551:
				return BGRX5551ToRGBA8888;
			case ImageFormat.BGRA5551:
				return BGRA5551ToRGBA8888;
			case ImageFormat.BGRA4444:
				return BGRA4444ToRGBA8888;
			case ImageFormat.UV88:
				return UV88ToRGBA8888;
			case ImageFormat.UVWQ8888:
				return UVWQ8888ToRGBA8888;
			case ImageFormat.UVLX8888:
				return UVLX8888ToRGBA8888;
			case ImageFormat.RGBA16161616:
				return RGBA16161616ToRGBA8888;
			case ImageFormat.RGBA16161616F:
				return null;
			default:
				return null;
		}
	}

	static RGBA8888ToUserFormatFunc? GetRGBA8888ToUserFormatFunc(ImageFormat dstImageFormat) {
		switch (dstImageFormat) {
			case ImageFormat.RGBA8888:
				return RGBA8888ToRGBA8888;
			case ImageFormat.ABGR8888:
				return RGBA8888ToABGR8888;
			case ImageFormat.RGB888:
				return RGBA8888ToRGB888;
			case ImageFormat.BGR888:
				return RGBA8888ToBGR888;
			case ImageFormat.RGB565:
				return null;
			case ImageFormat.I8:
				return RGBA8888ToI8;
			case ImageFormat.IA88:
				return RGBA8888ToIA88;
			case ImageFormat.A8:
				return RGBA8888ToA8;
			case ImageFormat.RGB888_Bluescreen:
				return RGBA8888ToRGB888_BLUESCREEN;
			case ImageFormat.BGR888_Bluescreen:
				return RGBA8888ToBGR888_BLUESCREEN;
			case ImageFormat.ARGB8888:
				return RGBA8888ToARGB8888;
			case ImageFormat.BGRA8888:
				return RGBA8888ToBGRA8888;
			case ImageFormat.BGRX8888:
				return RGBA8888ToBGRX8888;
			case ImageFormat.BGR565:
				return RGBA8888ToBGR565;
			case ImageFormat.BGRX5551:
				return RGBA8888ToBGRX5551;
			case ImageFormat.BGRA5551:
				return RGBA8888ToBGRA5551;
			case ImageFormat.BGRA4444:
				return RGBA8888ToBGRA4444;
			case ImageFormat.UV88:
				return RGBA8888ToUV88;
			case ImageFormat.UVWQ8888:
				return RGBA8888ToUVWQ8888;
			case ImageFormat.UVLX8888:
				return RGBA8888ToUVLX8888;
			case ImageFormat.RGBA16161616F:
				return null;
			default:
				return null;
		}
	}

	static bool ConvertUncompressedImageFormat(ReadOnlySpan<byte> src, ImageFormat srcImageFormat, Span<byte> dst, ImageFormat dstImageFormat, int width, int height, int srcStride, int dstStride) {
		int srcPixelSize = SizeInBytes(srcImageFormat);
		int dstPixelSize = SizeInBytes(dstImageFormat);

		if (srcStride == 0)
			srcStride = srcPixelSize * width;

		if (dstStride == 0)
			dstStride = dstPixelSize * width;

		if (srcImageFormat == dstImageFormat || (srcImageFormat == ImageFormat.BGRA8888 && dstImageFormat == ImageFormat.BGRX8888)) {
			for (int line = 0; line < height; ++line)
				src.Slice(line * srcStride, width * srcPixelSize).CopyTo(dst[(line * dstStride)..]);

			return true;
		}

		UserFormatToRGBA8888Func? userFormatToRGBA8888Func = GetUserFormatToRGBA8888Func(srcImageFormat);
		RGBA8888ToUserFormatFunc? rgba8888ToUserFormatFunc = GetRGBA8888ToUserFormatFunc(dstImageFormat);
		if (userFormatToRGBA8888Func == null || rgba8888ToUserFormatFunc == null)
			return false;

		Span<byte> lineBufRGBA8888 = width * 4 <= 16384 ? stackalloc byte[width * 4] : new byte[width * 4];

		for (int line = 0; line < height; line++) {
			userFormatToRGBA8888Func(src[(line * srcStride)..], lineBufRGBA8888, width);
			rgba8888ToUserFormatFunc(lineBufRGBA8888, dst[(line * dstStride)..], width);
		}

		return true;
	}

	static void RGBA8888ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		src[..(4 * numPixels)].CopyTo(dst);
	}

	static void RGBA8888ToABGR8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 4) {
			dst[d + 0] = src[s + 3];
			dst[d + 1] = src[s + 2];
			dst[d + 2] = src[s + 1];
			dst[d + 3] = src[s + 0];
		}
	}

	static void RGBA8888ToRGB888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 3) {
			dst[d + 0] = src[s + 0];
			dst[d + 1] = src[s + 1];
			dst[d + 2] = src[s + 2];
		}
	}

	static void RGBA8888ToBGR888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 3) {
			dst[d + 0] = src[s + 2];
			dst[d + 1] = src[s + 1];
			dst[d + 2] = src[s + 0];
		}
	}

	static void RGBA8888ToI8(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 1)
			dst[d] = (byte)(0.299f * src[s + 0] + 0.587f * src[s + 1] + 0.114f * src[s + 2]);
	}

	static void RGBA8888ToIA88(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 2) {
			dst[d + 0] = (byte)(0.299f * src[s + 0] + 0.587f * src[s + 1] + 0.114f * src[s + 2]);
			dst[d + 1] = src[s + 3];
		}
	}

	static void RGBA8888ToA8(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 1)
			dst[d] = src[s + 3];
	}

	static void RGBA8888ToRGB888_BLUESCREEN(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 3) {
			if (src[s + 3] == 0) {
				dst[d + 0] = 0;
				dst[d + 1] = 0;
				dst[d + 2] = 255;
			}
			else {
				dst[d + 0] = src[s + 0];
				dst[d + 1] = src[s + 1];
				dst[d + 2] = src[s + 2];
			}
		}
	}

	static void RGBA8888ToBGR888_BLUESCREEN(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 3) {
			if (src[s + 3] == 0) {
				dst[d + 2] = 0;
				dst[d + 1] = 0;
				dst[d + 0] = 255;
			}
			else {
				dst[d + 2] = src[s + 0];
				dst[d + 1] = src[s + 1];
				dst[d + 0] = src[s + 2];
			}
		}
	}

	static void RGBA8888ToARGB8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 4) {
			dst[d + 0] = src[s + 3];
			dst[d + 1] = src[s + 0];
			dst[d + 2] = src[s + 1];
			dst[d + 3] = src[s + 2];
		}
	}

	static void RGBA8888ToBGRA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 4) {
			dst[d + 0] = src[s + 2];
			dst[d + 1] = src[s + 1];
			dst[d + 2] = src[s + 0];
			dst[d + 3] = src[s + 3];
		}
	}

	static void RGBA8888ToBGRX8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 4) {
			dst[d + 0] = src[s + 2];
			dst[d + 1] = src[s + 1];
			dst[d + 2] = src[s + 0];
		}
	}

	static void RGBA8888ToBGR565(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		Span<ushort> dstShort = MemoryMarshal.Cast<byte, ushort>(dst);
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d++)
			dstShort[d] = (ushort)(((src[s + 0] >> 3) << 11) | ((src[s + 1] >> 2) << 5) | (src[s + 2] >> 3));
	}

	static void RGBA8888ToBGRX5551(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		Span<ushort> dstShort = MemoryMarshal.Cast<byte, ushort>(dst);
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d++)
			dstShort[d] = (ushort)(((src[s + 0] >> 3) << 10) | ((src[s + 1] >> 3) << 5) | (src[s + 2] >> 3));
	}

	static void RGBA8888ToBGRA5551(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		Span<ushort> dstShort = MemoryMarshal.Cast<byte, ushort>(dst);
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d++)
			dstShort[d] = (ushort)(((src[s + 0] >> 3) << 10) | ((src[s + 1] >> 3) << 5) | (src[s + 2] >> 3) | ((src[s + 3] >> 7) << 15));
	}

	static void RGBA8888ToBGRA4444(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		Span<ushort> dstShort = MemoryMarshal.Cast<byte, ushort>(dst);
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d++)
			dstShort[d] = (ushort)(((src[s + 0] >> 4) << 8) | ((src[s + 1] >> 4) << 4) | (src[s + 2] >> 4) | ((src[s + 3] >> 4) << 12));
	}

	static void RGBA8888ToUV88(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 2) {
			dst[d + 0] = src[s + 0];
			dst[d + 1] = src[s + 1];
		}
	}

	static void RGBA8888ToUVWQ8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) => RGBA8888ToRGBA8888(src, dst, numPixels);
	static void RGBA8888ToUVLX8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) => RGBA8888ToRGBA8888(src, dst, numPixels);

	static void ABGR8888ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 4) {
			dst[d + 0] = src[s + 3];
			dst[d + 1] = src[s + 2];
			dst[d + 2] = src[s + 1];
			dst[d + 3] = src[s + 0];
		}
	}

	static void RGB888ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 3; s < end; s += 3, d += 4) {
			dst[d + 0] = src[s + 0];
			dst[d + 1] = src[s + 1];
			dst[d + 2] = src[s + 2];
			dst[d + 3] = 255;
		}
	}

	static void BGR888ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 3; s < end; s += 3, d += 4) {
			dst[d + 0] = src[s + 2];
			dst[d + 1] = src[s + 1];
			dst[d + 2] = src[s + 0];
			dst[d + 3] = 255;
		}
	}

	static void I8ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0; s < numPixels; s += 1, d += 4) {
			dst[d + 0] = src[s];
			dst[d + 1] = src[s];
			dst[d + 2] = src[s];
			dst[d + 3] = 255;
		}
	}

	static void IA88ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 2; s < end; s += 2, d += 4) {
			dst[d + 0] = src[s + 0];
			dst[d + 1] = src[s + 0];
			dst[d + 2] = src[s + 0];
			dst[d + 3] = src[s + 1];
		}
	}

	static void A8ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0; s < numPixels; s += 1, d += 4) {
			dst[d + 0] = src[s];
			dst[d + 1] = src[s];
			dst[d + 2] = src[s];
			dst[d + 3] = src[s];
		}
	}

	static void RGB888_BLUESCREENToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 3; s < end; s += 3, d += 4) {
			if (src[s + 0] == 0 && src[s + 1] == 0 && src[s + 2] == 255) {
				dst[d + 0] = 0;
				dst[d + 1] = 0;
				dst[d + 2] = 0;
				dst[d + 3] = 0;
			}
			else {
				dst[d + 0] = src[s + 0];
				dst[d + 1] = src[s + 1];
				dst[d + 2] = src[s + 2];
				dst[d + 3] = 255;
			}
		}
	}

	static void BGR888_BLUESCREENToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 3; s < end; s += 3, d += 4) {
			if (src[s + 2] == 0 && src[s + 1] == 0 && src[s + 0] == 255) {
				dst[d + 0] = 0;
				dst[d + 1] = 0;
				dst[d + 2] = 0;
				dst[d + 3] = 0;
			}
			else {
				dst[d + 2] = src[s + 0];
				dst[d + 1] = src[s + 1];
				dst[d + 0] = src[s + 2];
				dst[d + 3] = 255;
			}
		}
	}

	static void ARGB8888ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 4) {
			dst[d + 0] = src[s + 1];
			dst[d + 1] = src[s + 2];
			dst[d + 2] = src[s + 3];
			dst[d + 3] = src[s + 0];
		}
	}

	static void BGRA8888ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 4) {
			dst[d + 0] = src[s + 2];
			dst[d + 1] = src[s + 1];
			dst[d + 2] = src[s + 0];
			dst[d + 3] = src[s + 3];
		}
	}

	static void BGRX8888ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 4) {
			dst[d + 0] = src[s + 2];
			dst[d + 1] = src[s + 1];
			dst[d + 2] = src[s + 0];
			dst[d + 3] = 255;
		}
	}

	static void BGR565ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		ReadOnlySpan<ushort> srcShort = MemoryMarshal.Cast<byte, ushort>(src);
		for (int s = 0, d = 0; s < numPixels; s++, d += 4) {
			uint blue = srcShort[s] & 0x1FU;
			uint green = (uint)(srcShort[s] >> 5) & 0x3FU;
			uint red = (uint)(srcShort[s] >> 11) & 0x1FU;
			dst[d + 0] = (byte)((red << 3) | (red >> 2));
			dst[d + 1] = (byte)((green << 2) | (green >> 4));
			dst[d + 2] = (byte)((blue << 3) | (blue >> 2));
			dst[d + 3] = 255;
		}
	}

	static void BGRX5551ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		ReadOnlySpan<ushort> srcShort = MemoryMarshal.Cast<byte, ushort>(src);
		for (int s = 0, d = 0; s < numPixels; s++, d += 4) {
			uint blue = srcShort[s] & 0x1FU;
			uint green = (uint)(srcShort[s] >> 5) & 0x1FU;
			uint red = (uint)(srcShort[s] >> 10) & 0x1FU;
			dst[d + 0] = (byte)((red << 3) | (red >> 2));
			dst[d + 1] = (byte)((green << 3) | (green >> 2));
			dst[d + 2] = (byte)((blue << 3) | (blue >> 2));
			dst[d + 3] = 255;
		}
	}

	static void BGRA5551ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		ReadOnlySpan<ushort> srcShort = MemoryMarshal.Cast<byte, ushort>(src);
		for (int s = 0, d = 0; s < numPixels; s++, d += 4) {
			uint blue = srcShort[s] & 0x1FU;
			uint green = (uint)(srcShort[s] >> 5) & 0x1FU;
			uint red = (uint)(srcShort[s] >> 10) & 0x1FU;
			uint alpha = (uint)(srcShort[s] & (1 << 15));
			dst[d + 0] = (byte)((red << 3) | (red >> 2));
			dst[d + 1] = (byte)((green << 3) | (green >> 2));
			dst[d + 2] = (byte)((blue << 3) | (blue >> 2));
			if (alpha != 0)
				dst[d + 3] = 255;
			else
				dst[d + 3] = 0;
		}
	}

	static void BGRA4444ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		ReadOnlySpan<ushort> srcShort = MemoryMarshal.Cast<byte, ushort>(src);
		for (int s = 0, d = 0; s < numPixels; s++, d += 4) {
			uint blue = srcShort[s] & 0xFU;
			uint green = (uint)(srcShort[s] >> 4) & 0xFU;
			uint red = (uint)(srcShort[s] >> 8) & 0xFU;
			uint alpha = (uint)(srcShort[s] >> 12) & 0xFU;
			dst[d + 0] = (byte)((red << 4) | (red >> 4));
			dst[d + 1] = (byte)((green << 4) | (green >> 4));
			dst[d + 2] = (byte)((blue << 4) | (blue >> 4));
			dst[d + 3] = (byte)((alpha << 4) | (alpha >> 4));
		}
	}

	static void UV88ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		for (int s = 0, d = 0, end = numPixels * 2; s < end; s += 2, d += 4) {
			dst[d + 0] = src[s + 0];
			dst[d + 1] = src[s + 1];
			dst[d + 2] = 0;
			dst[d + 3] = 0;
		}
	}

	static void UVWQ8888ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) => RGBA8888ToRGBA8888(src, dst, numPixels);
	static void UVLX8888ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) => RGBA8888ToRGBA8888(src, dst, numPixels);

	static void RGBA16161616ToRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int numPixels) {
		ReadOnlySpan<ushort> srcShort = MemoryMarshal.Cast<byte, ushort>(src);
		for (int s = 0, d = 0, end = numPixels * 4; s < end; s += 4, d += 4) {
			dst[d + 0] = (byte)Math.Min((ushort)255, (ushort)(srcShort[s + 0] >> 4));
			dst[d + 1] = (byte)Math.Min((ushort)255, (ushort)(srcShort[s + 1] >> 4));
			dst[d + 2] = (byte)Math.Min((ushort)255, (ushort)(srcShort[s + 2] >> 4));
			dst[d + 3] = (byte)Math.Min((ushort)255, (ushort)(srcShort[s + 3] >> 8));
		}
	}
}
