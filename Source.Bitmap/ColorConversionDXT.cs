using Source.Common.Bitmap;
using Source.Common.Mathematics;

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Source.Bitmap;

public interface IDestPixel<TSelf> where TSelf : unmanaged, IDestPixel<TSelf>
{
	static abstract TSelf From(BGRA8888 src);
}

public interface IChannelDestPixel<TSelf> : IDestPixel<TSelf> where TSelf : unmanaged, IChannelDestPixel<TSelf>
{
	static abstract void SetR(ref TSelf pixel, byte value);
	static abstract void SetG(ref TSelf pixel, byte value);
	static abstract void SetB(ref TSelf pixel, byte value);
	static abstract void SetA(ref TSelf pixel, byte value);
}

public struct RGB888 : IDestPixel<RGB888>
{
	public byte R;
	public byte G;
	public byte B;

	public static RGB888 From(BGRA8888 src) => new() { R = src.R, G = src.G, B = src.B };
}

public struct BGR888 : IDestPixel<BGR888>
{
	public byte B;
	public byte G;
	public byte R;

	public static BGR888 From(BGRA8888 src) => new() { R = src.R, G = src.G, B = src.B };
}

public struct BGR565 : IDestPixel<BGR565>
{
	public ushort Value;

	public static BGR565 From(BGRA8888 src) => new() { Value = (ushort)((src.B >> 3) | ((src.G >> 2) << 5) | ((src.R >> 3) << 11)) };
}

public struct BGRA5551 : IChannelDestPixel<BGRA5551>
{
	public ushort Value;

	public static BGRA5551 From(BGRA8888 src) => new() { Value = (ushort)((src.B >> 3) | ((src.G >> 3) << 5) | ((src.R >> 3) << 10) | ((src.A >> 7) << 15)) };

	public static void SetB(ref BGRA5551 pixel, byte value) => pixel.Value = (ushort)((pixel.Value & ~0x001F) | (value & 0x1F));
	public static void SetG(ref BGRA5551 pixel, byte value) => pixel.Value = (ushort)((pixel.Value & ~0x03E0) | ((value & 0x1F) << 5));
	public static void SetR(ref BGRA5551 pixel, byte value) => pixel.Value = (ushort)((pixel.Value & ~0x7C00) | ((value & 0x1F) << 10));
	public static void SetA(ref BGRA5551 pixel, byte value) => pixel.Value = (ushort)((pixel.Value & ~0x8000) | ((value & 0x1) << 15));
}

public struct BGRA4444 : IChannelDestPixel<BGRA4444>
{
	public ushort Value;

	public static BGRA4444 From(BGRA8888 src) => new() { Value = (ushort)((src.B >> 4) | ((src.G >> 4) << 4) | ((src.R >> 4) << 8) | ((src.A >> 4) << 12)) };

	public static void SetB(ref BGRA4444 pixel, byte value) => pixel.Value = (ushort)((pixel.Value & ~0x000F) | (value & 0xF));
	public static void SetG(ref BGRA4444 pixel, byte value) => pixel.Value = (ushort)((pixel.Value & ~0x00F0) | ((value & 0xF) << 4));
	public static void SetR(ref BGRA4444 pixel, byte value) => pixel.Value = (ushort)((pixel.Value & ~0x0F00) | ((value & 0xF) << 8));
	public static void SetA(ref BGRA4444 pixel, byte value) => pixel.Value = (ushort)((pixel.Value & ~0xF000) | ((value & 0xF) << 12));
}

public static partial class ImageLoader
{
	const int DXT_COL_BLOCK_SIZE = 8;
	const int DXT_ALPHA_BLOCK_SIZE = 8;

	static void GetColorBlockColorsBGRA8888(ReadOnlySpan<byte> block, out BGRA8888 col_0, out BGRA8888 col_1, out BGRA8888 col_2, out BGRA8888 col_3) {
		ushort color0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
		ushort color1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);

		col_0.A = 0xff;
		col_0.R = (byte)(((color0 >> 11) & 0x1F) << 3);
		col_0.G = (byte)(((color0 >> 5) & 0x3F) << 2);
		col_0.B = (byte)((color0 & 0x1F) << 3);

		col_1.A = 0xff;
		col_1.R = (byte)(((color1 >> 11) & 0x1F) << 3);
		col_1.G = (byte)(((color1 >> 5) & 0x3F) << 2);
		col_1.B = (byte)((color1 & 0x1F) << 3);

		if (color0 > color1) {
			col_2.R = (byte)((col_0.R * 2 + col_1.R) / 3);
			col_2.G = (byte)((col_0.G * 2 + col_1.G) / 3);
			col_2.B = (byte)((col_0.B * 2 + col_1.B) / 3);
			col_2.A = 0xff;

			col_3.R = (byte)((col_0.R + col_1.R * 2) / 3);
			col_3.G = (byte)((col_0.G + col_1.G * 2) / 3);
			col_3.B = (byte)((col_0.B + col_1.B * 2) / 3);
			col_3.A = 0xff;
		}
		else {
			col_2.R = (byte)((col_0.R + col_1.R) / 2);
			col_2.G = (byte)((col_0.G + col_1.G) / 2);
			col_2.B = (byte)((col_0.B + col_1.B) / 2);
			col_2.A = 0xff;

			col_3.R = 0x00;
			col_3.G = 0xff;
			col_3.B = 0xff;
			col_3.A = 0x00;
		}
	}

	static void DecodeColorBlock<T>(Span<T> outputImage, ReadOnlySpan<byte> colorBlock, int width, in BGRA8888 col_0, in BGRA8888 col_1, in BGRA8888 col_2, in BGRA8888 col_3) where T : unmanaged, IDestPixel<T> {
		int output = 0;
		for (int r = 0; r < 4; r++, output += width - 4) {
			for (int n = 0; n < 4; n++) {
				int bits = (colorBlock[4 + r] >> (n * 2)) & 3;
				switch (bits) {
					case 0:
						outputImage[output] = T.From(col_0);
						output++;
						break;
					case 1:
						outputImage[output] = T.From(col_1);
						output++;
						break;
					case 2:
						outputImage[output] = T.From(col_2);
						output++;
						break;
					case 3:
						outputImage[output] = T.From(col_3);
						output++;
						break;
				}
			}
		}
	}

	static void DecodeAlpha3BitLinear<T>(Span<T> image, ReadOnlySpan<byte> alphaBlock, int width, int channelSelect = 3) where T : unmanaged, IChannelDestPixel<T> {
		Span<byte> bitCodes = stackalloc byte[16];
		Span<ushort> alphas = stackalloc ushort[8];

		alphas[0] = alphaBlock[0];
		alphas[1] = alphaBlock[1];

		if (alphas[0] > alphas[1]) {
			alphas[2] = (ushort)((6 * alphas[0] + alphas[1]) / 7);
			alphas[3] = (ushort)((5 * alphas[0] + 2 * alphas[1]) / 7);
			alphas[4] = (ushort)((4 * alphas[0] + 3 * alphas[1]) / 7);
			alphas[5] = (ushort)((3 * alphas[0] + 4 * alphas[1]) / 7);
			alphas[6] = (ushort)((2 * alphas[0] + 5 * alphas[1]) / 7);
			alphas[7] = (ushort)((alphas[0] + 6 * alphas[1]) / 7);
		}
		else {
			alphas[2] = (ushort)((4 * alphas[0] + alphas[1]) / 5);
			alphas[3] = (ushort)((3 * alphas[0] + 2 * alphas[1]) / 5);
			alphas[4] = (ushort)((2 * alphas[0] + 3 * alphas[1]) / 5);
			alphas[5] = (ushort)((alphas[0] + 4 * alphas[1]) / 5);
			alphas[6] = 0;
			alphas[7] = 255;
		}

		const uint mask = 0x00000007;

		uint bits = (uint)(alphaBlock[2] | (alphaBlock[3] << 8) | (alphaBlock[4] << 16));
		for (int i = 0; i < 8; i++) {
			bitCodes[i] = (byte)(bits & mask);
			bits >>= 3;
		}

		bits = (uint)(alphaBlock[5] | (alphaBlock[6] << 8) | (alphaBlock[7] << 16));
		for (int i = 8; i < 16; i++) {
			bitCodes[i] = (byte)(bits & mask);
			bits >>= 3;
		}

		int pos = 0;
		for (int row = 0; row < 4; row++, pos += width - 4) {
			for (int pix = 0; pix < 4; pix++) {
				byte alpha = (byte)alphas[bitCodes[row * 4 + pix]];
				ref T pixel = ref image[pos];
				switch (channelSelect) {
					case 0:
						T.SetR(ref pixel, alpha);
						T.SetG(ref pixel, 0);
						T.SetB(ref pixel, 0);
						T.SetA(ref pixel, 0);
						break;
					case 1:
						T.SetG(ref pixel, alpha);
						break;
					case 2:
						T.SetB(ref pixel, alpha);
						break;
					default:
					case 3:
						T.SetA(ref pixel, alpha);
						break;
				}
				pos++;
			}
		}
	}

	static void ConvertFromDXT1<T>(ReadOnlySpan<byte> src, Span<T> dst, int width, int height) where T : unmanaged, IDestPixel<T> {
		int realWidth = 0;
		int realHeight = 0;
		Span<T> realDst = default;

		if (width < 4 || height < 4) {
			realWidth = width;
			realHeight = height;

			width = (width + 3) & ~3;
			height = (height + 3) & ~3;

			realDst = dst;
			dst = new T[width * height];
		}

		Assert((width % 4) == 0);
		Assert((height % 4) == 0);

		int xblocks = width >> 2;
		int yblocks = height >> 2;

		for (int j = 0; j < yblocks; j++) {
			int block = j * xblocks * DXT_COL_BLOCK_SIZE;
			for (int i = 0; i < xblocks; i++, block += DXT_COL_BLOCK_SIZE) {
				ReadOnlySpan<byte> colorBlock = src.Slice(block, DXT_COL_BLOCK_SIZE);
				GetColorBlockColorsBGRA8888(colorBlock, out BGRA8888 col_0, out BGRA8888 col_1, out BGRA8888 col_2, out BGRA8888 col_3);

				Span<T> dstScan = dst[(i * 4 + j * 4 * width)..];
				DecodeColorBlock(dstScan, colorBlock, width, col_0, col_1, col_2, col_3);
			}
		}

		if (!realDst.IsEmpty) {
			for (int y = 0; y < realHeight; y++)
				for (int x = 0; x < realWidth; x++)
					realDst[x + (y * realWidth)] = dst[x + (y * width)];
		}
	}

	static void ConvertFromDXT5<T>(ReadOnlySpan<byte> src, Span<T> dst, int width, int height) where T : unmanaged, IChannelDestPixel<T> {
		int realWidth = 0;
		int realHeight = 0;
		Span<T> realDst = default;

		if (width < 4 || height < 4) {
			realWidth = width;
			realHeight = height;

			width = (width + 3) & ~3;
			height = (height + 3) & ~3;

			realDst = dst;
			dst = new T[width * height];
		}

		Assert((width % 4) == 0);
		Assert((height % 4) == 0);

		int xblocks = width >> 2;
		int yblocks = height >> 2;

		for (int j = 0; j < yblocks; j++) {
			int block = j * xblocks * (DXT_ALPHA_BLOCK_SIZE + DXT_COL_BLOCK_SIZE);
			for (int i = 0; i < xblocks; i++, block += DXT_ALPHA_BLOCK_SIZE + DXT_COL_BLOCK_SIZE) {
				ReadOnlySpan<byte> alphaBlock = src.Slice(block, DXT_ALPHA_BLOCK_SIZE);
				ReadOnlySpan<byte> colorBlock = src.Slice(block + DXT_ALPHA_BLOCK_SIZE, DXT_COL_BLOCK_SIZE);

				GetColorBlockColorsBGRA8888(colorBlock, out BGRA8888 col_0, out BGRA8888 col_1, out BGRA8888 col_2, out BGRA8888 col_3);

				Span<T> dstScan = dst[(i * 4 + j * 4 * width)..];

				DecodeColorBlock(dstScan, colorBlock, width, col_0, col_1, col_2, col_3);

				DecodeAlpha3BitLinear(dstScan, alphaBlock, width);
			}
		}

		if (!realDst.IsEmpty) {
			for (int y = 0; y < realHeight; y++)
				for (int x = 0; x < realWidth; x++)
					realDst[x + (y * realWidth)] = dst[x + (y * width)];
		}
	}

	static void ConvertFromDXT5IgnoreAlpha<T>(ReadOnlySpan<byte> src, Span<T> dst, int width, int height) where T : unmanaged, IDestPixel<T> {
		int realWidth = 0;
		int realHeight = 0;
		Span<T> realDst = default;

		if (width < 4 || height < 4) {
			realWidth = width;
			realHeight = height;

			width = (width + 3) & ~3;
			height = (height + 3) & ~3;

			realDst = dst;
			dst = new T[width * height];
		}

		Assert((width % 4) == 0);
		Assert((height % 4) == 0);

		int xblocks = width >> 2;
		int yblocks = height >> 2;

		for (int j = 0; j < yblocks; j++) {
			int block = j * xblocks * (DXT_ALPHA_BLOCK_SIZE + DXT_COL_BLOCK_SIZE);
			for (int i = 0; i < xblocks; i++, block += DXT_ALPHA_BLOCK_SIZE + DXT_COL_BLOCK_SIZE) {
				ReadOnlySpan<byte> colorBlock = src.Slice(block + DXT_ALPHA_BLOCK_SIZE, DXT_COL_BLOCK_SIZE);

				GetColorBlockColorsBGRA8888(colorBlock, out BGRA8888 col_0, out BGRA8888 col_1, out BGRA8888 col_2, out BGRA8888 col_3);

				Span<T> dstScan = dst[(i * 4 + j * 4 * width)..];

				DecodeColorBlock(dstScan, colorBlock, width, col_0, col_1, col_2, col_3);
			}
		}

		if (!realDst.IsEmpty) {
			for (int y = 0; y < realHeight; y++)
				for (int x = 0; x < realWidth; x++)
					realDst[x + (y * realWidth)] = dst[x + (y * width)];
		}
	}

	static void ConvertFromATIxN<T>(ReadOnlySpan<byte> src, Span<T> dst, int width, int height, bool ati2n) where T : unmanaged, IChannelDestPixel<T> {
		int realWidth = 0;
		int realHeight = 0;
		Span<T> realDst = default;

		if (width < 4 || height < 4) {
			realWidth = width;
			realHeight = height;

			width = (width + 3) & ~3;
			height = (height + 3) & ~3;

			realDst = dst;
			dst = new T[width * height];
		}

		Assert((width % 4) == 0);
		Assert((height % 4) == 0);

		int xblocks = width >> 2;
		int yblocks = height >> 2;

		int bytesPerBlock = ati2n ? 16 : 8;

		for (int j = 0; j < yblocks; j++) {
			int block = j * xblocks * bytesPerBlock;
			for (int i = 0; i < xblocks; i++, block += DXT_ALPHA_BLOCK_SIZE) {
				Span<T> dstScan = dst[(i * 4 + j * 4 * width)..];

				DecodeAlpha3BitLinear(dstScan, src.Slice(block, DXT_ALPHA_BLOCK_SIZE), width, 0);

				if (ati2n) {
					block += DXT_ALPHA_BLOCK_SIZE;
					DecodeAlpha3BitLinear(dstScan, src.Slice(block, DXT_ALPHA_BLOCK_SIZE), width, 1);
				}
			}
		}

		if (!realDst.IsEmpty) {
			for (int y = 0; y < realHeight; y++)
				for (int x = 0; x < realWidth; x++)
					realDst[x + (y * realWidth)] = dst[x + (y * width)];
		}
	}

	static void ConvertImageFormat_RGBA16161616_To_RGB323232F(ReadOnlySpan<ushort> srcImage, Span<float> dstImage, int width, int height) {
		int srcSize = width * height * 4;
		for (int s = 0, d = 0; s < srcSize; s += 4, d += 3) {
			dstImage[d + 0] = srcImage[s + 0] * (1.0f / (1 << 12));
			dstImage[d + 1] = srcImage[s + 1] * (1.0f / (1 << 12));
			dstImage[d + 2] = srcImage[s + 2] * (1.0f / (1 << 12));
		}
	}

	static void ConvertImageFormat_RGB323232F_To_RGBA16161616(ReadOnlySpan<float> srcImage, Span<ushort> dstImage, int width, int height) {
		int srcSize = width * height * 3;
		for (int s = 0, d = 0; s < srcSize; s += 3, d += 4) {
			dstImage[d + 0] = (ushort)(int)Math.Min(65535.0f, srcImage[s + 0] * (1 << 12));
			dstImage[d + 1] = (ushort)(int)Math.Min(65535.0f, srcImage[s + 1] * (1 << 12));
			dstImage[d + 2] = (ushort)(int)Math.Min(65535.0f, srcImage[s + 2] * (1 << 12));
			dstImage[d + 3] = 65535;
		}
	}

	static void ConvertImageFormat_RGBA16161616F_To_RGB323232F(ReadOnlySpan<Float16> srcImage, Span<float> dstImage, int width, int height) {
		int srcSize = width * height * 4;
		for (int s = 0, d = 0; s < srcSize; s += 4, d += 3) {
			dstImage[d + 0] = srcImage[s + 0].GetFloat();
			dstImage[d + 1] = srcImage[s + 1].GetFloat();
			dstImage[d + 2] = srcImage[s + 2].GetFloat();
		}
	}

	static void ConvertImageFormat_RGBA16161616F_To_RGBA323232F(ReadOnlySpan<Float16> srcImage, Span<float> dstImage, int width, int height, int srcStride) {
		int stride = srcStride / 2;
		int src = 0;
		int dst = 0;
		for (int y = 0; y < height; y++) {
			int srcScan = src;
			int dstScan = dst;
			for (int x = 0; x < width; x++) {
				dstImage[dstScan + 0] = srcImage[srcScan + 0].GetFloat();
				dstImage[dstScan + 1] = srcImage[srcScan + 1].GetFloat();
				dstImage[dstScan + 2] = srcImage[srcScan + 2].GetFloat();
				dstImage[dstScan + 3] = srcImage[srcScan + 3].GetFloat();
				dstScan += 4;
				srcScan += 4;
			}
			src += stride;
			dst += 4 * width;
		}
	}

	static void ConvertImageFormat_RGB323232F_To_RGBA16161616F(ReadOnlySpan<float> srcImage, Span<Float16> dstImage, int width, int height) {
		int srcSize = width * height * 3;
		for (int s = 0, d = 0; s < srcSize; s += 3, d += 4) {
			dstImage[d + 0].SetFloat(srcImage[s + 0]);
			dstImage[d + 1].SetFloat(srcImage[s + 1]);
			dstImage[d + 2].SetFloat(srcImage[s + 2]);
		}
	}

	static FloatBitMap CompressRGB323232FTo8Bits(ReadOnlySpan<float> srcImage, int width, int height) {
		FloatBitMap flbm = new();
		flbm.AllocateRGB(width, height);
		for (int y = 0; y < height; ++y) {
			for (int x = 0; x < width; ++x) {
				int pf = 3 * (x + width * y);
				PixRGBAF fpix;
				fpix.Red = srcImage[pf + 0];
				fpix.Green = srcImage[pf + 1];
				fpix.Blue = srcImage[pf + 2];
				fpix.Alpha = 0.0f;
				flbm.WritePixelRGBAF(x, y, fpix);
			}
		}

		flbm.CompressTo8Bits(8.0f);
		return flbm;
	}

	static void ConvertImageFormat_RGB323232F_To_RGBA8888(ReadOnlySpan<float> srcImage, Span<byte> dst, int width, int height) {
		FloatBitMap flbm = CompressRGB323232FTo8Bits(srcImage, width, height);

		for (int y = 0; y < height; ++y) {
			for (int x = 0; x < width; ++x) {
				PixRGBAF fpix = flbm.PixelRGBAF(x, y);
				PixRGBA8 pix8 = fpix.To8(fpix);
				int pch = 4 * (x + width * y);
				dst[pch + 0] = pix8.Red;
				dst[pch + 1] = pix8.Green;
				dst[pch + 2] = pix8.Blue;
				dst[pch + 3] = pix8.Alpha;
			}
		}
	}

	static void ConvertImageFormat_RGB323232F_To_BGRA8888(ReadOnlySpan<float> srcImage, Span<byte> dst, int width, int height) {
		FloatBitMap flbm = CompressRGB323232FTo8Bits(srcImage, width, height);

		for (int y = 0; y < height; ++y) {
			for (int x = 0; x < width; ++x) {
				PixRGBAF fpix = flbm.PixelRGBAF(x, y);
				PixRGBA8 pix8 = fpix.To8(fpix);
				int pch = 4 * (x + width * y);
				dst[pch + 0] = pix8.Blue;
				dst[pch + 1] = pix8.Green;
				dst[pch + 2] = pix8.Red;
				dst[pch + 3] = pix8.Alpha;
			}
		}
	}

	static void ConvertImageFormat_RGBA16161616_To_RGBA16161616F(ReadOnlySpan<ushort> srcImage, Span<Float16> dstImage, int width, int height) {
		int srcSize = width * height * 4;
		for (int i = 0; i < srcSize; i += 4) {
			dstImage[i + 0].SetFloat(srcImage[i + 0] * (1.0f / (1 << 16)));
			dstImage[i + 1].SetFloat(srcImage[i + 1] * (1.0f / (1 << 16)));
			dstImage[i + 2].SetFloat(srcImage[i + 2] * (1.0f / (1 << 16)));
			dstImage[i + 3].SetFloat(srcImage[i + 3] * (1.0f / (1 << 16)));
		}
	}

	static void ConvertImageFormat_RGBA16161616F_To_RGBA16161616(ReadOnlySpan<Float16> srcImage, Span<ushort> dstImage, int width, int height) {
		int srcSize = width * height * 4;
		for (int i = 0; i < srcSize; i += 4) {
			for (int j = 0; j < 4; j++) {
				float val = srcImage[i + j].GetFloat();
				val *= 1 << 12;
				val = Math.Max(val, 0.0f);
				val = Math.Min(val, 65535.0f);
				dstImage[i + j] = (ushort)val;
			}
		}
	}

	public static void ConvertNormalMapRGBA8888ToDUDVMapUVWQ8888(ReadOnlySpan<byte> src, int width, int height, Span<byte> dst) {
		int lastPixel = width * height * 4;
		for (int s = 0, d = 0; s < lastPixel; s += 4, d += 4) {
			dst[d + 0] = unchecked((byte)(src[s + 0] - 127));
			dst[d + 1] = unchecked((byte)(src[s + 1] - 127));
			dst[d + 2] = unchecked((byte)(src[s + 2] - 127));
			dst[d + 3] = unchecked((byte)(src[s + 3] - 127));
		}
	}

	public static void ConvertNormalMapRGBA8888ToDUDVMapUVLX8888(ReadOnlySpan<byte> src, int width, int height, Span<byte> dst) {
		int lastPixel = width * height * 4;
		for (int s = 0, d = 0; s < lastPixel; s += 4, d += 4) {
			dst[d + 0] = unchecked((byte)(src[s + 0] - 127));
			dst[d + 1] = unchecked((byte)(src[s + 1] - 127));
			dst[d + 2] = src[s + 3];
			dst[d + 3] = 0xFF;
		}
	}

	public static void ConvertNormalMapRGBA8888ToDUDVMapUV88(ReadOnlySpan<byte> src, int width, int height, Span<byte> dst) {
		int lastPixel = width * height * 4;
		for (int s = 0, d = 0; s < lastPixel; s += 4, d += 2) {
			dst[d + 0] = unchecked((byte)(src[s + 0] - 127));
			dst[d + 1] = unchecked((byte)(src[s + 1] - 127));
		}
	}

	public static void NormalizeNormalMapRGBA8888(Span<byte> src, int numTexels) {
		int lastPixel = numTexels * 4;
		for (int i = 0; i < lastPixel; i += 4) {
			float x = (src[i + 0] - 128.0f) * (1.0f / 127.0f);
			float y = (src[i + 1] - 128.0f) * (1.0f / 127.0f);
			float z = (src[i + 2] - 128.0f) * (1.0f / 127.0f);

			float sqlen = x * x + y * y + z * z + 1.0e-10f;
			float invlen = Sse.IsSupported ? Sse.ReciprocalSqrtScalar(Vector128.CreateScalarUnsafe(sqlen)).ToScalar() : MathF.ReciprocalSqrtEstimate(sqlen);
			invlen = (3.0f - invlen * invlen * sqlen) * 0.5f * invlen;

			src[i + 0] = unchecked((byte)(int)(x * invlen * 127.0f + 128.0f));
			src[i + 1] = unchecked((byte)(int)(y * invlen * 127.0f + 128.0f));
			src[i + 2] = unchecked((byte)(int)(z * invlen * 127.0f + 128.0f));
		}
	}
}
