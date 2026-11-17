using Source.Common.Bitmap;
using Source.Common.Mathematics;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.Bitmap;

[Flags]
public enum ResampleFlags
{
	NormalMap = 0x1,
	AlphaTest = 0x2,
	NiceFilter = 0x4,
	ClampS = 0x8,
	ClampT = 0x10,
	ClampU = 0x20
}

public ref struct ResampleInfo
{
	public Span<byte> Src;
	public Span<byte> Dest;

	public int SrcWidth;
	public int SrcHeight;
	public int SrcDepth;

	public int DestWidth;
	public int DestHeight;
	public int DestDepth;

	public float SrcGamma;
	public float DestGamma;

	public InlineArray4<float> ColorScale;
	public InlineArray4<float> ColorGoal;

	public float AlphaThreshhold;
	public float AlphaHiFreqThreshhold;

	public ResampleFlags Flags;

	public ResampleInfo() {
		Flags = 0;
		AlphaThreshhold = 0.4f;
		AlphaHiFreqThreshhold = 0.4f;
		SrcDepth = 1;
		DestDepth = 1;

		ColorScale[0] = 1.0f; ColorGoal[0] = 0.0f;
		ColorScale[1] = 1.0f; ColorGoal[1] = 0.0f;
		ColorScale[2] = 1.0f; ColorGoal[2] = 0.0f;
		ColorScale[3] = 1.0f; ColorGoal[3] = 0.0f;
	}
}

public static partial class ImageLoader
{
	static void ConstructFloatGammaTable(Span<float> table, float srcGamma, float dstGamma) {
		for (int i = 0; i < table.Length; i++)
			table[i] = 255.0f * MathF.Pow((float)i / 255.0f, srcGamma / dstGamma);
	}

	static void ConstructGammaTable(Span<byte> table, float srcGamma, float dstGamma) {
		for (int i = 0; i < table.Length; i++) {
			float f = 255.0f * MathF.Pow((float)i / 255.0f, srcGamma / dstGamma);
			int v = (int)(f + 0.5f);
			if (v < 0)
				v = 0;
			else if (v > 255)
				v = 255;
			table[i] = (byte)v;
		}
	}

	public static void GammaCorrectRGBA8888(ReadOnlySpan<byte> src, Span<byte> dst, int width, int height, int depth, ReadOnlySpan<byte> gammaTable) {
		for (int h = 0; h < depth; ++h) {
			for (int i = 0; i < height; ++i) {
				for (int j = 0; j < width; ++j) {
					int idx = (h * width * height + i * width + j) * 4;

					dst[idx] = gammaTable[src[idx]];
					dst[idx + 1] = gammaTable[src[idx + 1]];
					dst[idx + 2] = gammaTable[src[idx + 2]];
				}
			}
		}
	}

	static readonly byte[] s_Gamma = new byte[256];
	static float s_GammaLastSrcGamma = -1;
	static float s_GammaLastDstGamma = -1;

	public static void GammaCorrectRGBA8888(Span<byte> src, Span<byte> dst, int width, int height, int depth, float srcGamma, float dstGamma) {
		if (srcGamma == dstGamma) {
			if (!Unsafe.AreSame(ref MemoryMarshal.GetReference(src), ref MemoryMarshal.GetReference(dst)))
				src[..GetMemRequired(width, height, depth, ImageFormat.RGBA8888, false)].CopyTo(dst);
			return;
		}

		if (s_GammaLastSrcGamma != srcGamma || s_GammaLastDstGamma != dstGamma) {
			ConstructGammaTable(s_Gamma, srcGamma, dstGamma);
			s_GammaLastSrcGamma = srcGamma;
			s_GammaLastDstGamma = dstGamma;
		}

		GammaCorrectRGBA8888(src, dst, width, height, depth, s_Gamma);
	}

	static void GenerateNiceFilter(float wratio, float hratio, float dratio, int kernelDiameter, Span<float> kernel, Span<float> invKernel) {
		int h, i, j;
		int kernelWidth = (int)(kernelDiameter * wratio);
		int kernelHeight = (int)(kernelDiameter * hratio);
		int kernelDepth = (dratio != 0) ? (int)(kernelDiameter * dratio) : 1;

		float dx = 1.0f / (float)wratio;
		float dy = 1.0f / (float)hratio;
		float z, dz;

		if (dratio != 0.0f) {
			dz = 1.0f / (float)dratio;
			z = -((float)kernelDiameter - dz) * 0.5f;
		}
		else {
			dz = 0.0f;
			z = 0.0f;
		}

		float total = 0.0f;
		for (h = 0; h < kernelDepth; ++h) {
			float y = -((float)kernelDiameter - dy) * 0.5f;
			for (i = 0; i < kernelHeight; ++i) {
				float x = -((float)kernelDiameter - dx) * 0.5f;
				for (j = 0; j < kernelWidth; ++j) {
					int kernelIndex = kernelWidth * (i + h * kernelHeight) + j;

					float d = MathF.Sqrt(x * x + y * y + z * z);
					if (d > kernelDiameter * 0.5f) {
						kernel[kernelIndex] = 0.0f;
					}
					else {
						float t = (float)(Math.PI * (double)d);
						if (t != 0) {
							float sinc = MathF.Sin(t) / t;
							float sinc3 = 3.0f * MathF.Sin(t / 3.0f) / t;
							kernel[kernelIndex] = sinc * sinc3;
						}
						else {
							kernel[kernelIndex] = 1.0f;
						}
						total += kernel[kernelIndex];
					}
					x += dx;
				}
				y += dy;
			}
			z += dz;
		}

		float invFactor = (dratio == 0) ? wratio * hratio : dratio * wratio * hratio;
		float invTotal = (total != 0.0f) ? 1.0f / total : 1.0f;

		for (h = 0; h < kernelDepth; ++h) {
			for (i = 0; i < kernelHeight; ++i) {
				int pixel = kernelWidth * (h * kernelHeight + i);
				for (j = 0; j < kernelWidth; ++j) {
					kernel[pixel + j] *= invTotal;
					invKernel[pixel + j] = invFactor * kernel[pixel + j];
				}
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static bool IsPowerOfTwo(int x) => (x & (x - 1)) == 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static byte ResampleClamp(float x) {
		int idx = (int)(x + 0.5f);
		return (byte)Math.Clamp(idx, 0, 255);
	}

	ref struct KernelInfo
	{
		public ReadOnlySpan<float> Kernel;
		public ReadOnlySpan<float> InvKernel;
		public int Width;
		public int Height;
		public int Depth;
		public int Diameter;
	}

	enum KernelType
	{
		Default = 0,
		NormalMap,
		AlphaTest
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static int ActualX(int x, in ResampleInfo info) {
		if ((info.Flags & ResampleFlags.ClampS) != 0)
			return Math.Clamp(x, 0, info.SrcWidth - 1);

		return x & (info.SrcWidth - 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static int ActualY(int y, in ResampleInfo info) {
		if ((info.Flags & ResampleFlags.ClampT) != 0)
			return Math.Clamp(y, 0, info.SrcHeight - 1);

		return y & (info.SrcHeight - 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static int ActualZ(int z, in ResampleInfo info) {
		if ((info.Flags & ResampleFlags.ClampU) != 0)
			return Math.Clamp(z, 0, info.SrcDepth - 1);

		return z & (info.SrcDepth - 1);
	}

	static void ComputeAveragedColor(KernelType type, bool niceFilter, in KernelInfo kernel, in ResampleInfo info, int startX, int startY, int startZ, ReadOnlySpan<float> gammaToLinear, Span<float> total) {
		total[0] = total[1] = total[2] = total[3] = 0.0f;
		for (int j = 0, srcZ = startZ; j < kernel.Depth; ++j, ++srcZ) {
			int sz = ActualZ(srcZ, info);
			sz *= info.SrcWidth * info.SrcHeight;

			for (int k = 0, srcY = startY; k < kernel.Height; ++k, ++srcY) {
				int sy = ActualY(srcY, info);
				sy *= info.SrcWidth;

				int kernelIdx;
				if (niceFilter)
					kernelIdx = kernel.Width * (k + j * kernel.Height);
				else
					kernelIdx = 0;

				for (int l = 0, srcX = startX; l < kernel.Width; ++l, ++srcX, ++kernelIdx) {
					int sx = ActualX(srcX, info);
					int srcPixel = (sz + sy + sx) << 2;

					float kernelFactor;
					if (niceFilter) {
						kernelFactor = kernel.Kernel[kernelIdx];
						if (kernelFactor == 0.0f)
							continue;
					}
					else {
						kernelFactor = kernel.Kernel[0];
					}

					if (type == KernelType.NormalMap) {
						total[0] += kernelFactor * info.Src[srcPixel + 0];
						total[1] += kernelFactor * info.Src[srcPixel + 1];
						total[2] += kernelFactor * info.Src[srcPixel + 2];
						total[3] += kernelFactor * info.Src[srcPixel + 3];
					}
					else if (type == KernelType.AlphaTest) {
						total[0] += kernelFactor * gammaToLinear[info.Src[srcPixel + 0]];
						total[1] += kernelFactor * gammaToLinear[info.Src[srcPixel + 1]];
						total[2] += kernelFactor * gammaToLinear[info.Src[srcPixel + 2]];
						if (info.Src[srcPixel + 3] > 192)
							total[3] += kernelFactor * 255.0f;
					}
					else {
						total[0] += kernelFactor * gammaToLinear[info.Src[srcPixel + 0]];
						total[1] += kernelFactor * gammaToLinear[info.Src[srcPixel + 1]];
						total[2] += kernelFactor * gammaToLinear[info.Src[srcPixel + 2]];
						total[3] += kernelFactor * info.Src[srcPixel + 3];
					}
				}
			}
		}
	}

	static void AddAlphaToAlphaResult(bool niceFilter, in KernelInfo kernel, in ResampleInfo info, int startX, int startY, int startZ, float alpha, Span<float> alphaResult) {
		for (int j = 0, srcZ = startZ; j < kernel.Depth; ++j, ++srcZ) {
			int sz = ActualZ(srcZ, info);
			sz *= info.SrcWidth * info.SrcHeight;

			for (int k = 0, srcY = startY; k < kernel.Height; ++k, ++srcY) {
				int sy = ActualY(srcY, info);
				sy *= info.SrcWidth;

				int kernelIdx;
				if (niceFilter)
					kernelIdx = k * kernel.Width + j * kernel.Width * kernel.Height;
				else
					kernelIdx = 0;

				for (int l = 0, srcX = startX; l < kernel.Width; ++l, ++srcX, ++kernelIdx) {
					int sx = ActualX(srcX, info);
					int srcPixel = sz + sy + sx;

					float kernelFactor;
					if (niceFilter) {
						kernelFactor = kernel.InvKernel[kernelIdx];
						if (kernelFactor == 0.0f)
							continue;
					}
					else {
						kernelFactor = kernel.InvKernel[0];
					}

					alphaResult[srcPixel] += kernelFactor * alpha;
				}
			}
		}
	}

	static void AdjustAlphaChannel(in KernelInfo kernel, in ResampleInfo info, int wratio, int hratio, int dratio, Span<float> alphaResult) {
		for (int k = 0; k < info.SrcDepth; ++k) {
			for (int i = 0; i < info.SrcHeight; ++i) {
				int dstPixel = i * info.SrcWidth + k * info.SrcWidth * info.SrcHeight;
				for (int j = 0; j < info.SrcWidth; ++j, ++dstPixel)
					alphaResult[dstPixel] = MathF.Abs(alphaResult[dstPixel] - info.Src[dstPixel * 4 + 3]);
			}
		}

		int initialZ = (dratio >> 1) - ((dratio * kernel.Diameter) >> 1);
		int initialY = (hratio >> 1) - ((hratio * kernel.Diameter) >> 1);
		int initialX = (wratio >> 1) - ((wratio * kernel.Diameter) >> 1);

		float alphaThreshhold = (info.AlphaHiFreqThreshhold >= 0.0f) ? 255.0f * info.AlphaHiFreqThreshhold : 255.0f * 0.4f;

		float invFactor = (dratio == 0) ? 1.0f / (hratio * wratio) : 1.0f / (hratio * wratio * dratio);

		for (int h = 0; h < info.DestDepth; ++h) {
			int startZ = dratio * h + initialZ;
			for (int i = 0; i < info.DestHeight; ++i) {
				int startY = hratio * i + initialY;
				int dstPixel = (info.DestWidth * (i + h * info.DestHeight)) << 2;
				for (int j = 0; j < info.DestWidth; ++j, dstPixel += 4) {
					if (info.Dest[dstPixel + 3] == 255)
						continue;

					int startX = wratio * j + initialX;
					float alphaDelta = 0.0f;

					for (int m = 0, srcZ = startZ; m < dratio; ++m, ++srcZ) {
						int sz = ActualZ(srcZ, info);
						sz *= info.SrcWidth * info.SrcHeight;

						for (int k = 0, srcY = startY; k < hratio; ++k, ++srcY) {
							int sy = ActualY(srcY, info);
							sy *= info.SrcWidth;

							for (int l = 0, srcX = startX; l < wratio; ++l, ++srcX) {
								int sx = ActualX(srcX, info);

								int srcPixel = sz + sy + sx;
								alphaDelta += alphaResult[srcPixel];
							}
						}
					}

					alphaDelta *= invFactor;
					if (alphaDelta > alphaThreshhold)
						info.Dest[dstPixel + 3] = 255;
				}
			}
		}
	}

	static void ApplyKernel(KernelType type, bool niceFilter, in KernelInfo kernel, in ResampleInfo info, int wratio, int hratio, int dratio, ReadOnlySpan<float> gammaToLinear, Span<float> alphaResult) {
		float invDstGamma = 1.0f / info.DestGamma;

		int initialZ = (dratio >> 1) - ((dratio * kernel.Diameter) >> 1);
		int initialY = (hratio >> 1) - ((hratio * kernel.Diameter) >> 1);
		int initialX = (wratio >> 1) - ((wratio * kernel.Diameter) >> 1);

		float alphaThreshhold = (info.AlphaThreshhold >= 0.0f) ? 255.0f * info.AlphaThreshhold : 255.0f * 0.4f;
		Span<float> total = stackalloc float[4];
		for (int k = 0; k < info.DestDepth; ++k) {
			int startZ = dratio * k + initialZ;

			for (int i = 0; i < info.DestHeight; ++i) {
				int startY = hratio * i + initialY;
				int dstPixel = (i * info.DestWidth + k * info.DestWidth * info.DestHeight) << 2;

				for (int j = 0; j < info.DestWidth; ++j, dstPixel += 4) {
					int startX = wratio * j + initialX;

					ComputeAveragedColor(type, niceFilter, kernel, info, startX, startY, startZ, gammaToLinear, total);

					if (type == KernelType.NormalMap) {
						for (int ch = 0; ch < 4; ++ch)
							info.Dest[dstPixel + ch] = ResampleClamp(info.ColorGoal[ch] + (info.ColorScale[ch] * (total[ch] - info.ColorGoal[ch])));
					}
					else if (type == KernelType.AlphaTest) {
						float alpha = (total[3] >= alphaThreshhold) ? 255.0f : 0;

						for (int ch = 0; ch < 3; ++ch)
							info.Dest[dstPixel + ch] = ResampleClamp(255.0f * MathF.Pow((info.ColorGoal[ch] + (info.ColorScale[ch] * ((total[ch] > 0 ? total[ch] : 0) - info.ColorGoal[ch]))) / 255.0f, invDstGamma));
						info.Dest[dstPixel + 3] = ResampleClamp(alpha);

						AddAlphaToAlphaResult(niceFilter, kernel, info, startX, startY, startZ, alpha, alphaResult);
					}
					else {
						for (int ch = 0; ch < 3; ++ch)
							info.Dest[dstPixel + ch] = ResampleClamp(255.0f * MathF.Pow((info.ColorGoal[ch] + (info.ColorScale[ch] * ((total[ch] > 0 ? total[ch] : 0) - info.ColorGoal[ch]))) / 255.0f, invDstGamma));
						info.Dest[dstPixel + 3] = ResampleClamp(info.ColorGoal[3] + (info.ColorScale[3] * (total[3] - info.ColorGoal[3])));
					}
				}
			}

			if (type == KernelType.AlphaTest)
				AdjustAlphaChannel(kernel, info, wratio, hratio, dratio, alphaResult);
		}
	}

	static readonly float[] s_GammaToLinear = new float[256];
	static float s_LastSrcGamma = -1;
	static readonly float[]?[] s_KernelCache = new float[]?[10];
	static readonly float[]?[] s_InvKernelCache = new float[]?[10];

	public static bool ResampleRGBA8888(in ResampleInfo info) {
		if (info.SrcWidth == info.DestWidth && info.SrcHeight == info.DestHeight && info.SrcDepth == info.DestDepth) {
			GammaCorrectRGBA8888(info.Src, info.Dest, info.SrcWidth, info.SrcHeight, info.SrcDepth, info.SrcGamma, info.DestGamma);
			return true;
		}

		if (!IsPowerOfTwo(info.SrcWidth) || !IsPowerOfTwo(info.SrcHeight) || !IsPowerOfTwo(info.SrcDepth) ||
			!IsPowerOfTwo(info.DestWidth) || !IsPowerOfTwo(info.DestHeight) || !IsPowerOfTwo(info.DestDepth)) {
			return false;
		}

		if ((info.SrcWidth < info.DestWidth) || (info.SrcHeight < info.DestHeight) || (info.SrcDepth < info.DestDepth))
			return false;

		if (s_LastSrcGamma != info.SrcGamma) {
			ConstructFloatGammaTable(s_GammaToLinear, info.SrcGamma, 1.0f);
			s_LastSrcGamma = info.SrcGamma;
		}

		int wratio = info.SrcWidth / info.DestWidth;
		int hratio = info.SrcHeight / info.DestHeight;
		int dratio = (info.SrcDepth != info.DestDepth) ? info.SrcDepth / info.DestDepth : 0;

		scoped KernelInfo kernel = default;

		Span<float> boxKernel = stackalloc float[1];
		Span<float> boxInvKernel = stackalloc float[1];
		if ((info.Flags & ResampleFlags.NiceFilter) != 0) {
			kernel.Diameter = 6;

			kernel.Width = kernel.Diameter * wratio;
			kernel.Height = kernel.Diameter * hratio;
			kernel.Depth = kernel.Diameter * dratio;
			if (kernel.Depth == 0)
				kernel.Depth = 1;

			int power = -1;

			if ((wratio == hratio) && (dratio == 0)) {
				power = 0;
				int tempWidth = wratio;
				while (tempWidth > 1) {
					++power;
					tempWidth >>= 1;
				}

				if (power >= 10)
					power = -1;
			}

			if (power >= 0) {
				if (s_KernelCache[power] == null) {
					s_KernelCache[power] = new float[kernel.Width * kernel.Height];
					s_InvKernelCache[power] = new float[kernel.Width * kernel.Height];
					GenerateNiceFilter(wratio, hratio, dratio, kernel.Diameter, s_KernelCache[power], s_InvKernelCache[power]);
				}

				kernel.Kernel = s_KernelCache[power];
				kernel.InvKernel = s_InvKernelCache[power];
			}
			else {
				float[] tempMemory = new float[kernel.Width * kernel.Height * kernel.Depth];
				float[] tempInvMemory = new float[kernel.Width * kernel.Height * kernel.Depth];
				GenerateNiceFilter(wratio, hratio, dratio, kernel.Diameter, tempMemory, tempInvMemory);
				kernel.Kernel = tempMemory;
				kernel.InvKernel = tempInvMemory;
			}
		}
		else {
			kernel.Width = wratio;
			kernel.Height = hratio;
			kernel.Depth = dratio != 0 ? dratio : 1;

			kernel.Diameter = 1;

			boxKernel[0] = 1.0f / (float)(kernel.Width * kernel.Height * kernel.Depth);
			boxInvKernel[0] = 1.0f;
			kernel.Kernel = boxKernel;
			kernel.InvKernel = boxInvKernel;
		}

		float[]? alphaResult = null;
		KernelType type;
		if ((info.Flags & ResampleFlags.NormalMap) != 0) {
			type = KernelType.NormalMap;
		}
		else if ((info.Flags & ResampleFlags.AlphaTest) != 0) {
			nint size = (nint)info.SrcHeight * info.SrcWidth * info.SrcDepth;
			alphaResult = new float[size];
			type = KernelType.AlphaTest;
		}
		else {
			type = KernelType.Default;
		}

		ApplyKernel(type, (info.Flags & ResampleFlags.NiceFilter) != 0, kernel, info, wratio, hratio, dratio, s_GammaToLinear, alphaResult);

		return true;
	}

	public static bool ResampleRGB323232F(in ResampleInfo info) {
		Assert(IsPowerOfTwo(info.SrcWidth));
		Assert(IsPowerOfTwo(info.SrcHeight));
		Assert(IsPowerOfTwo(info.DestWidth));
		Assert(IsPowerOfTwo(info.DestHeight));

		Assert(info.SrcWidth >= info.DestWidth);
		Assert(info.SrcHeight >= info.DestHeight);

		int sampleWidth = info.SrcWidth / info.DestWidth;
		int sampleHeight = info.SrcHeight / info.DestHeight;

		ReadOnlySpan<float> src = MemoryMarshal.Cast<byte, float>(info.Src);
		Span<float> dst = MemoryMarshal.Cast<byte, float>(info.Dest);

		Span<float> accum = stackalloc float[4];
		for (int y = 0; y < info.DestHeight; y++) {
			int yMultiplier = y * sampleHeight;

			for (int x = 0; x < info.DestWidth; x++) {
				accum.Clear();
				int xMultiplier = x * sampleWidth;

				for (int sampleY = 0; sampleY < sampleHeight; sampleY++) {
					int yIndex = (yMultiplier + sampleY) * info.SrcWidth;

					for (int sampleX = 0; sampleX < sampleWidth; sampleX++) {
						int xIndex = xMultiplier + sampleX;
						int baseIndex = (xIndex + yIndex) * 3;

						accum[0] += src[baseIndex + 0];
						accum[1] += src[baseIndex + 1];
						accum[2] += src[baseIndex + 2];
					}
				}

				int destIndex = (x + y * info.DestWidth) * 3;
				for (int i = 0; i < 3; i++) {
					accum[i] /= (sampleWidth * sampleHeight);
					dst[destIndex + i] = accum[i];
				}
			}
		}
		return true;
	}
}
