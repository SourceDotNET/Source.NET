using SharpCompress.Common;

using Source.Common.Engine;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;

namespace Source.Common.Bitmap;

public struct PixRGBAF
{
	public float Red;
	public float Green;
	public float Blue;
	public float Alpha;

	public PixRGBA8 To8(in PixRGBAF f) {
		PixRGBA8 x = new() {
			Red = To8(f.Red),
			Green = To8(f.Green),
			Blue = To8(f.Blue),
			Alpha = To8(f.Alpha)
		};
		return x;
	}

	static byte To8(float f) {
		double v = 255.0 * (double)f;
		return v < 0 ? (byte)0 : (v > 255.0 ? (byte)255 : (byte)(int)v);
	}
}

public struct PixRGBA8
{
	public byte Red;
	public byte Green;
	public byte Blue;
	public byte Alpha;

	public PixRGBAF ToF(in PixRGBA8 x) {
		PixRGBAF f = new() {
			Red = x.Red / 255f,
			Green = x.Green / 255f,
			Blue = x.Blue / 255f,
			Alpha = x.Alpha / 255f
		};
		return f;
	}
}

public class FloatBitMap
{
	public int Width, Height;
	public float[]? RGBAData;

	public FloatBitMap() { }
	public FloatBitMap(int width, int height) {
		AllocateRGB(width, height);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref float Pixel(int x, int y, int comp) {
		Assert((x >= 0) && (x < Width));
		Assert((y >= 0) && (y < Height));
		return ref RGBAData![4 * (x + Width * y) + comp];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref float PixelWrapped(int x, int y, int comp) {
		if (x < 0)
			x += Width;
		else
			if (x >= Width)
			x -= Width;

		if (y < 0)
			y += Height;
		else
			if (y >= Height)
			y -= Height;

		return ref RGBAData![4 * (x + Width * y) + comp];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float LinInterp(float frac, float L, float R) {
		return (((R - L) * frac) + L);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float BiLinInterp(float Xfrac, float Yfrac, float UL, float UR, float LL, float LR) {
		float iu = LinInterp(Xfrac, UL, UR);
		float il = LinInterp(Xfrac, LL, LR);

		return (LinInterp(Yfrac, iu, il));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref float PixelClamped(int x, int y, int comp) {
		x = Math.Clamp(x, 0, Width - 1);
		y = Math.Clamp(y, 0, Height - 1);
		return ref RGBAData![4 * (x + Width * y) + comp];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ref float Alpha(int x, int y) {
		Assert((x >= 0) && (x < Width));
		Assert((y >= 0) && (y < Height));
		return ref RGBAData![3 + 4 * (x + Width * y)];
	}

	public float InterpolatedPixel(float x, float y, int comp) {
		int Top = (int)(MathF.Floor(y));
		float Yfrac = y - Top;
		int Bot = Math.Min(Height - 1, Top + 1);
		int Left = (int)(MathF.Floor(x));
		float Xfrac = x - Left;
		int Right = Math.Min(Width - 1, Left + 1);
		return
			BiLinInterp(Xfrac, Yfrac,
			Pixel(Left, Top, comp),
			Pixel(Right, Top, comp),
			Pixel(Left, Bot, comp),
			Pixel(Right, Bot, comp));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WritePixelRGBAF(int x, int y, PixRGBAF value) {
		Assert((x >= 0) && (x < Width));
		Assert((y >= 0) && (y < Height));

		int RGBoffset = 4 * (x + Width * y);
		RGBAData![RGBoffset + 0] = value.Red;
		RGBAData![RGBoffset + 1] = value.Green;
		RGBAData![RGBoffset + 2] = value.Blue;
		RGBAData![RGBoffset + 3] = value.Alpha;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]

	public void WritePixel(int x, int y, int comp, float value) {
		Assert((x >= 0) && (x < Width));
		Assert((y >= 0) && (y < Height));
		RGBAData![4 * (x + Width * y) + comp] = value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public PixRGBAF PixelRGBAF(int x, int y) {
		Assert((x >= 0) && (x < Width));
		Assert((y >= 0) && (y < Height));

		int RGBoffset = 4 * (x + Width * y);
		PixRGBAF RetPix = new() {
			Red = RGBAData![RGBoffset + 0],
			Green = RGBAData![RGBoffset + 1],
			Blue = RGBAData![RGBoffset + 2],
			Alpha = RGBAData![RGBoffset + 3]
		};

		return RetPix;
	}

	public void Clear(float r, float g, float b, float alpha) {
		for (int y = 0; y < Height; y++)
			for (int x = 0; x < Width; x++) {
				Pixel(x, y, 0) = r;
				Pixel(x, y, 1) = g;
				Pixel(x, y, 2) = b;
				Pixel(x, y, 3) = alpha;
			}
	}

	public void ScaleRGB(float scaleFactor) {
		for (int y = 0; y < Height; y++)
			for (int x = 0; x < Width; x++)
				for (int c = 0; c < 3; c++)
					Pixel(x, y, c) *= scaleFactor;
	}

	static float ScaleValue(float f, float overbright) {
		int ival = (int)f;
		return (float)(ival * ((double)overbright / 255.0));
	}

	static float IScaleValue(float f, float overbright) {
		f = (float)((double)f * (1.0 / (double)overbright));
		double ival = Math.Ceiling((double)f * 255.0);
		return ival > 255.0 ? 255.0f : (float)(int)ival;
	}

	public void CompressTo8Bits(float overbright) {
		FloatBitMap tmp = new(Width, Height);

		for (int y = 0; y < Height; y++)
			for (int x = 0; x < Width; x++)
				for (int c = 0; c < 3; c++) {
					float value = Pixel(x, y, c);
					Pixel(x, y, c) = value < overbright ? value : overbright;
				}

		for (int y = 0; y < Height; y++)
			for (int x = 0; x < Width; x++) {
				float maxc = Math.Max(Pixel(x, y, 0), Math.Max(Pixel(x, y, 1), Pixel(x, y, 2)));
				if (maxc == 0) {
					for (int c = 0; c < 4; c++)
						tmp.Pixel(x, y, c) = 0;
				}
				else {
					float desiredFloatScale = maxc;
					float closestIScale = IScaleValue(desiredFloatScale, overbright);
					float scaleValueWeGot = ScaleValue(closestIScale, overbright);
					tmp.Pixel(x, y, 3) = closestIScale;
					for (int c = 0; c < 3; c++)
						tmp.Pixel(x, y, c) = Pixel(x, y, c) / scaleValueWeGot;
				}
			}

		tmp.RGBAData.AsSpan(0, Width * Height * 4).CopyTo(RGBAData);

		for (int y = 0; y < Height; y++)
			for (int x = 0; x < Width; x++)
				Pixel(x, y, 3) = (float)((double)Pixel(x, y, 3) * (1.0 / 255.0));
	}

	public float[] AllocateRGB(int width, int height) {
		RGBAData = null;
		RGBAData = new float[width * height * sizeof(float)];
		Width = width;
		Height = height;
		return RGBAData;
	}
}
