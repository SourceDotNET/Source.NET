using CommunityToolkit.HighPerformance;

using Source.Common.Bitmap;

using System.Runtime.CompilerServices;

namespace Source.Common.MaterialSystem;

[Flags]
public enum PixelWriterUsing : byte
{
	Float = 0x01,
	Float16 = 0x02,
	SwapBytes = 0x04
}
public static partial class FloatHelpers
{

	[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_value")]
	static extern ref ushort getBits(ref Half half);
	public static ushort GetBits(this ref Half half) => getBits(ref half);

	public static uint FloatBits(this ref readonly float r) => new ReadOnlySpan<float>(in r).Cast<float, uint>()[0];
}

public struct PixelWriterState
{
	public ImageFormat Format;
	public int Bits;
	public byte Size;
	public int BytesPerRow;
	public PixelWriterUsing Flags;
	public short RShift;
	public short GShift;
	public short BShift;
	public short AShift;
	public uint RMask;
	public uint GMask;
	public uint BMask;
	public uint AMask;

	internal void SetFormat(ImageFormat format, uint stride) {
		Format = format;
		BytesPerRow = (int)stride;
		switch (format) {
			case ImageFormat.R32F:
				Size = 4;
				RShift = 0;
				GShift = 0;
				BShift = 0;
				AShift = 0;
				RMask = 0xFFFFFFFF;
				GMask = 0x0;
				BMask = 0x0;
				AMask = 0x0;
				Flags |= PixelWriterUsing.Float;
				break;

			case ImageFormat.RGBA32323232F:
				Size = 16;
				RShift = 0;
				GShift = 32;
				BShift = 64;
				AShift = 96;
				RMask = 0xFFFFFFFF;
				GMask = 0xFFFFFFFF;
				BMask = 0xFFFFFFFF;
				AMask = 0xFFFFFFFF;
				Flags |= PixelWriterUsing.Float;
				break;

			case ImageFormat.RGBA16161616F:
				Size = 8;
				RShift = 0;
				GShift = 16;
				BShift = 32;
				AShift = 48;
				RMask = 0xFFFF;
				GMask = 0xFFFF;
				BMask = 0xFFFF;
				AMask = 0xFFFF;
				Flags |= PixelWriterUsing.Float | PixelWriterUsing.Float16;
				break;

			case ImageFormat.RGBA8888:
				Size = 4;
				RShift = 0;
				GShift = 8;
				BShift = 16;
				AShift = 24;
				RMask = 0xFF;
				GMask = 0xFF;
				BMask = 0xFF;
				AMask = 0xFF;
				break;

			case ImageFormat.BGRA8888:
				Size = 4;
				RShift = 16;
				GShift = 8;
				BShift = 0;
				AShift = 24;
				RMask = 0xFF;
				GMask = 0xFF;
				BMask = 0xFF;
				AMask = 0xFF;
				break;

			case ImageFormat.BGRX8888:
				Size = 4;
				RShift = 16;
				GShift = 8;
				BShift = 0;
				AShift = 24;
				RMask = 0xFF;
				GMask = 0xFF;
				BMask = 0xFF;
				AMask = 0x00;
				break;

			case ImageFormat.BGRA4444:
				Size = 2;
				RShift = 4;
				GShift = 0;
				BShift = -4;
				AShift = 8;
				RMask = 0xF0;
				GMask = 0xF0;
				BMask = 0xF0;
				AMask = 0xF0;
				break;

			case ImageFormat.BGR888:
				Size = 3;
				RShift = 16;
				GShift = 8;
				BShift = 0;
				AShift = 0;
				RMask = 0xFF;
				GMask = 0xFF;
				BMask = 0xFF;
				AMask = 0x00;
				break;

			case ImageFormat.BGR565:
				Size = 2;
				RShift = 8;
				GShift = 3;
				BShift = -3;
				AShift = 0;
				RMask = 0xF8;
				GMask = 0xFC;
				BMask = 0xF8;
				AMask = 0x00;
				break;

			case ImageFormat.BGRA5551:
			case ImageFormat.BGRX5551:
				Size = 2;
				RShift = 7;
				GShift = 2;
				BShift = -3;
				AShift = 8;
				RMask = 0xF8;
				GMask = 0xF8;
				BMask = 0xF8;
				AMask = 0x80;
				break;

			case ImageFormat.A8:
				Size = 1;
				RShift = 0;
				GShift = 0;
				BShift = 0;
				AShift = 0;
				RMask = 0x00;
				GMask = 0x00;
				BMask = 0x00;
				AMask = 0xFF;
				break;

			case ImageFormat.UVWQ8888:
				AssertMsg(false, "What????");
				Size = 4;
				RShift = 0;
				GShift = 8;
				BShift = 16;
				AShift = 24;
				RMask = 0xFF;
				GMask = 0xFF;
				BMask = 0xFF;
				AMask = 0xFF;
				break;

			case ImageFormat.RGBA16161616:
				Size = 8;
				RShift = 0;
				GShift = 16;
				BShift = 32;
				AShift = 48;
				RMask = 0xFFFF;
				GMask = 0xFFFF;
				BMask = 0xFFFF;
				AMask = 0xFFFF;
				break;

			case ImageFormat.I8:
				// whatever goes into R is considered the intensity.
				Size = 1;
				RShift = 0;
				GShift = 0;
				BShift = 0;
				AShift = 0;
				RMask = 0xFF;
				GMask = 0x00;
				BMask = 0x00;
				AMask = 0x00;
				break;
			// FIXME: Add more color formats as need arises
			default: {
					if (!format_error_printed[(int)format]) {
						Assert(false, $"Unsupported image format {format}");
						Msg($"PixelWriter.SetPixelMemory:  Unsupported image format {format}\n");
						format_error_printed[(int)format] = true;
					}
					Size = 0;
					RShift = 0;
					GShift = 0;
					BShift = 0;
					AShift = 0;
					RMask = 0x00;
					GMask = 0x00;
					BMask = 0x00;
					AMask = 0x00;
				}
				break;
		}
	}
	static bool[] format_error_printed = new bool[(int)ImageFormat.Count];
}

public static class PixelWriterImpl
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void Seek(ref PixelWriterState State, Span<byte> Base, int x, int y) {
		State.Bits = y * State.BytesPerRow + x * State.Size;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void SkipBytes(ref PixelWriterState State, int n) {
		State.Bits += n;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void WritePixel(ref PixelWriterState State, Span<byte> Base, int r, int g, int b, int a = 255) {
		WritePixelNoAdvance(ref State, Base, r, g, b, a);
		State.Bits += State.Size;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void WritePixelF(ref PixelWriterState State, Span<byte> Base, float r, float g, float b, float a = 1.0f) {
		WritePixelNoAdvanceF(ref State, Base, r, g, b, a);
		State.Bits += State.Size;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void WritePixelNoAdvanceF(ref PixelWriterState State, Span<byte> Base, float r, float g, float b, float a) {
		Assert(IsUsingFloatFormat(ref State));
		if ((State.Flags & PixelWriterUsing.Float16) != 0) {
			Span<Half> fp16 = stackalloc Half[4];
			fp16[0] = (Half)r;
			fp16[1] = (Half)g;
			fp16[2] = (Half)b;
			fp16[3] = (Half)a;
			Span<ushort> buf = stackalloc ushort[4];
			buf[State.RShift >> 4] |= (ushort)((fp16[0].GetBits() & State.RMask) << (State.RShift & 0xF));
			buf[State.GShift >> 4] |= (ushort)((fp16[1].GetBits() & State.GMask) << (State.GShift & 0xF));
			buf[State.BShift >> 4] |= (ushort)((fp16[2].GetBits() & State.BMask) << (State.BShift & 0xF));
			buf[State.AShift >> 4] |= (ushort)((fp16[3].GetBits() & State.AMask) << (State.AShift & 0xF));
			memcpy(Base[..State.Size], buf[..State.Size].Cast<ushort, byte>());
		}
		else {
			Span<uint> buf = stackalloc uint[4];
			buf[State.RShift >> 5] |= (r.FloatBits() & State.RMask) << (State.RShift & 0x1F);
			buf[State.GShift >> 5] |= (g.FloatBits() & State.GMask) << (State.GShift & 0x1F);
			buf[State.BShift >> 5] |= (b.FloatBits() & State.BMask) << (State.BShift & 0x1F);
			buf[State.AShift >> 5] |= (a.FloatBits() & State.AMask) << (State.AShift & 0x1F);
			memcpy(Base[..State.Size], buf[..State.Size].Cast<uint, byte>());
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void WritePixelNoAdvance(ref PixelWriterState State, Span<byte> Base, int r, int g, int b, int a) {
		if (State.Size <= 0) return;

		uint rmask = State.RMask, gmask = State.GMask, bmask = State.BMask, amask = State.AMask;
		short rshift = State.RShift, gshift = State.GShift, bshift = State.BShift, ashift = State.AShift;

		Span<byte> bits = Base[State.Bits..];
		if (State.Size < 5) {
			uint val = (uint)((r & rmask) << rshift);
			val |= (uint)((g & gmask) << gshift);
			val |= (uint)((bshift > 0) ? ((b & bmask) << bshift) : ((b & bmask) >> -bshift));
			val |= (uint)((a & amask) << ashift);

			switch (State.Size) {
				default:
					Assert(false);
					return;
				case 1: {
						bits[0] = (byte)(val & 0xff);
						return;
					}
				case 2: {
						reinterpret<byte, ushort>(bits)[0] = (ushort)(val & 0xffff);
						return;
					}
				case 3: {
						reinterpret<byte, ushort>(bits)[0] = (ushort)(val & 0xffff);
						bits[2] = (byte)((val >> 16) & 0xff);
						return;
					}
				case 4: {
						reinterpret<byte, uint>(bits)[0] = val;
						return;
					}
			}
		}
		else {
			long val = (r & rmask) << rshift;
			val |= (g & gmask) << gshift;
			val |= (bshift > 0) ? ((b & bmask) << bshift) : ((b & bmask) >> -bshift);
			val |= (a & amask) << ashift;

			switch (State.Size) {
				case 6: {
						reinterpret<byte, uint>(bits)[0] = (uint)(val & 0xffffffff);
						reinterpret<byte, ushort>(bits)[2] = (ushort)((val >> 32) & 0xffff);

						return;
					}
				case 8: {
						reinterpret<byte, uint>(bits)[0] = (uint)(val & 0xffffffff);
						reinterpret<byte, uint>(bits)[1] = (uint)((val >> 32) & 0xffffffff);
						return;
					}
				default:
					Assert(false);
					return;
			}
		}
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsUsingFloatFormat(ref PixelWriterState State) => (State.Flags & PixelWriterUsing.Float) != 0;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void WritePixel(ref PixelWriterState State, Span<byte> Base, in Color color) => WritePixel(ref State, Base, color.R, color.G, color.B, color.A);
}

public ref struct PixelWriter
{
	Span<byte> Base;
	PixelWriterState State;

	/// <summary>
	/// Allows reuse of the internal state structure if we lose control of the pixel writer ref struct
	/// </summary>
	/// <param name="state"></param>
	public readonly void Export(out PixelWriterState state, out Span<byte> memory) {
		state = State;
		memory = Base;
	}
	/// <summary>
	/// Allows reuse of the internal state structure if we lose control of the pixel writer ref struct
	/// </summary>
	/// <param name="state"></param>
	public void Import(in PixelWriterState state, in Span<byte> memory) {
		State = state;
		Base = memory;
	}
	public void SetPixelMemory(ImageFormat format, in Span<byte> memory, int stride) {
		State.SetFormat(format, (uint)stride);
		Base = memory;
	}
	public void Dispose() {
		Base = null;
		State = default;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly int GetPixelSize() => State.Size;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Seek(int x, int y) => PixelWriterImpl.Seek(ref State, Base, x, y);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SkipBytes(int n) => PixelWriterImpl.SkipBytes(ref State, n);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void WritePixel(int r, int g, int b, int a = 255) => PixelWriterImpl.WritePixel(ref State, Base, r, g, b, a);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void WritePixelF(float r, float g, float b, float a = 1.0f) => PixelWriterImpl.WritePixelF(ref State, Base, r, g, b, a);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void WritePixelNoAdvanceF(float r, float g, float b, float a) => PixelWriterImpl.WritePixelNoAdvanceF(ref State, Base, r, g, b, a);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void WritePixelNoAdvance(int r, int g, int b, int a) => PixelWriterImpl.WritePixelNoAdvance(ref State, Base, r, g, b, a);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsUsingFloatFormat() => PixelWriterImpl.IsUsingFloatFormat(ref State);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void WritePixel(in Color color) => PixelWriterImpl.WritePixel(ref State, Base, in color);
}


public struct PixelWriterMem
{
	Memory<byte> Base;
	PixelWriterState State;

	/// <summary>
	/// Allows reuse of the internal state structure if we lose control of the pixel writer ref struct
	/// </summary>
	/// <param name="state"></param>
	public readonly void Export(out PixelWriterState state, out Memory<byte> memory) {
		state = State;
		memory = Base;
	}
	/// <summary>
	/// Allows reuse of the internal state structure if we lose control of the pixel writer ref struct
	/// </summary>
	/// <param name="state"></param>
	public void Import(in PixelWriterState state, in Memory<byte> memory) {
		State = state;
		Base = memory;
	}
	public void SetPixelMemory(ImageFormat format, in Memory<byte> memory, int stride) {
		State.SetFormat(format, (uint)stride);
		Base = memory;
	}
	public void Dispose() {
		Base = null;
		State = default;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public readonly int GetPixelSize() => State.Size;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void Seek(int x, int y) => PixelWriterImpl.Seek(ref State, Base.Span, x, y);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SkipBytes(int n) => PixelWriterImpl.SkipBytes(ref State, n);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void WritePixel(int r, int g, int b, int a = 255) => PixelWriterImpl.WritePixel(ref State, Base.Span, r, g, b, a);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void WritePixelF(float r, float g, float b, float a = 1.0f) => PixelWriterImpl.WritePixelF(ref State, Base.Span, r, g, b, a);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void WritePixelNoAdvanceF(float r, float g, float b, float a) => PixelWriterImpl.WritePixelNoAdvanceF(ref State, Base.Span, r, g, b, a);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void WritePixelNoAdvance(int r, int g, int b, int a) => PixelWriterImpl.WritePixelNoAdvance(ref State, Base.Span, r, g, b, a);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsUsingFloatFormat() => PixelWriterImpl.IsUsingFloatFormat(ref State);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void WritePixel(in Color color) => PixelWriterImpl.WritePixel(ref State, Base.Span, in color);
}
