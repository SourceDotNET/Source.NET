using CommunityToolkit.HighPerformance;

using Source.Common.Formats.BSP;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;

namespace Source.Common.Mathematics;

public static class MathLibConsts
{
	public const int PITCH = 0;
	public const int YAW = 1;
	public const int ROLL = 2;
	public const vec_t VEC_T_NAN = vec_t.NaN;

	public const float FLT_EPSILON = 1.192092896e-07f; // FLT_EPSILON (2^-23), MathF.BitIncrement(1.0f) - 1.0f, not the same as float.Epsilon
	public const double DBL_EPSILON = 2.2204460492503131e-16; // DBL_EPSILON (2^-52), Math.BitIncrement(1.0) - 1.0, not the same as double.Epsilon
	public const float EQUAL_EPSILON = 0.001f;

	public static readonly Vector3 vec3_origin = new(0, 0, 0);
	public static readonly QAngle vec3_angle = new(0, 0, 0);
	public static readonly Vector3 vec3_invalid = new(float.MaxValue, float.MaxValue, float.MaxValue);

	public static Vector3 RandomAngularImpulse(float minVal, float maxVal) {
		Vector3 angImp = default;
		angImp.Random(minVal, maxVal);
		return angImp;
	}
}

[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 6)]
public struct Quaternion48
{
	public static implicit operator Quaternion(Quaternion48 self) {
		Quaternion tmp;
		tmp.X = ((int)self.x - 32768) * (1 / 32768.0f);
		tmp.Y = ((int)self.y - 32768) * (1 / 32768.0f);
		tmp.Z = ((int)self.z - 16384) * (1 / 16384.0f);
		tmp.W = MathF.Sqrt(1 - tmp.X * tmp.X - tmp.Y * tmp.Y - tmp.Z * tmp.Z);
		if (self.wneg)
			tmp.W = -tmp.W;
		return tmp;
	}

	public static implicit operator Quaternion48(Quaternion vOther) {
		Quaternion48 result;
		result.data = 0;
		result.x = (ushort)Math.Clamp((int)(vOther.X * 32768) + 32768, 0, 65535);
		result.y = (ushort)Math.Clamp((int)(vOther.Y * 32768) + 32768, 0, 65535);
		result.z = (ushort)Math.Clamp((int)(vOther.Z * 16384) + 16384, 0, 32767);
		result.wneg = vOther.W < 0;
		return result;
	}

	public ulong data;

	private const ulong X_MASK = 0xFFFF;           // 16 bits
	private const ulong Y_MASK = 0xFFFF;           // 16 bits
	private const ulong Z_MASK = 0x7FFF;           // 15 bits
	private const ulong WNEG_MASK = 0x1;           // 1 bit

	private const int X_SHIFT = 0;
	private const int Y_SHIFT = 16;
	private const int Z_SHIFT = 32;
	private const int WNEG_SHIFT = 47;

	public ushort x {
		readonly get => (ushort)((data >> X_SHIFT) & X_MASK);
		set => data = (data & ~(X_MASK << X_SHIFT)) | ((ulong)(value & X_MASK) << X_SHIFT);
	}

	public ushort y {
		readonly get => (ushort)((data >> Y_SHIFT) & Y_MASK);
		set => data = (data & ~(Y_MASK << Y_SHIFT)) | ((ulong)(value & Y_MASK) << Y_SHIFT);
	}

	public ushort z {
		readonly get => (ushort)((data >> Z_SHIFT) & Z_MASK);
		set => data = (data & ~(Z_MASK << Z_SHIFT)) | ((ulong)(value & Z_MASK) << Z_SHIFT);
	}

	public bool wneg {
		readonly get => ((data >> WNEG_SHIFT) & WNEG_MASK) != 0;
		set {
			if (value)
				data |= (WNEG_MASK << WNEG_SHIFT);
			else
				data &= ~(WNEG_MASK << WNEG_SHIFT);
		}
	}
}

[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 6)]
public struct Vector48
{
	public Vector48(vec_t x, vec_t y, vec_t z) {
		X = (Half)x;
		Y = (Half)y;
		Z = (Half)z;
	}

	public static implicit operator Vector3(Vector48 self) => new((vec_t)self.X, (vec_t)self.Y, (vec_t)self.Z);

	public readonly float this[int i] => (float)(i switch {
		0 => X,
		1 => Y,
		2 => Z,
		_ => throw new IndexOutOfRangeException()
	});

	public Half X;
	public Half Y;
	public Half Z;
}



[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 8)]
public struct Quaternion64
{
	public static implicit operator Quaternion(Quaternion64 self) {
		Quaternion tmp;
		// shift to -1048576, + 1048575, then round down slightly to -1.0 < x < 1.0
		tmp.X = ((int)self.x - 1048576) * (1 / 1048576.5f);
		tmp.Y = ((int)self.y - 1048576) * (1 / 1048576.5f);
		tmp.Z = ((int)self.z - 1048576) * (1 / 1048576.5f);
		tmp.W = MathF.Sqrt(1 - tmp.X * tmp.X - tmp.Y * tmp.Y - tmp.Z * tmp.Z);
		if (self.wneg)
			tmp.W = -tmp.W;
		return tmp;
	}

	public static implicit operator Quaternion64(Quaternion vOther) {
		Quaternion64 result;
		result.data = 0;
		result.x = (uint)Math.Clamp((int)(vOther.X * 1048576) + 1048576, 0, 2097151);
		result.y = (uint)Math.Clamp((int)(vOther.Y * 1048576) + 1048576, 0, 2097151);
		result.z = (uint)Math.Clamp((int)(vOther.Z * 1048576) + 1048576, 0, 2097151);
		result.wneg = vOther.W < 0;
		return result;
	}

	public ulong data;

	private const ulong X_MASK = 0x1FFFFF;
	private const ulong Y_MASK = 0x1FFFFF;
	private const ulong Z_MASK = 0x1FFFFF;
	private const ulong WNEG_MASK = 0x1;

	private const int X_SHIFT = 0;
	private const int Y_SHIFT = 21;
	private const int Z_SHIFT = 42;
	private const int WNEG_SHIFT = 63;

	public uint x {
		readonly get => (uint)((data >> X_SHIFT) & X_MASK);
		set => data = (data & ~(X_MASK << X_SHIFT)) | ((value & X_MASK) << X_SHIFT);
	}

	public uint y {
		readonly get => (uint)((data >> Y_SHIFT) & Y_MASK);
		set => data = (data & ~(Y_MASK << Y_SHIFT)) | ((value & Y_MASK) << Y_SHIFT);
	}

	public uint z {
		readonly get => (uint)((data >> Z_SHIFT) & Z_MASK);
		set => data = (data & ~(Z_MASK << Z_SHIFT)) | ((value & Z_MASK) << Z_SHIFT);
	}

	public bool wneg {
		readonly get => ((data >> WNEG_SHIFT) & WNEG_MASK) != 0;
		set {
			if (value)
				data |= (WNEG_MASK << WNEG_SHIFT);
			else
				data &= ~(WNEG_MASK << WNEG_SHIFT);
		}
	}
}

public struct RadianEuler
{
	public vec_t X, Y, Z;
}

/// <summary>
/// Mostly for data structure compatibility
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 48)]
public record struct Matrix3x4
{
	public static readonly Matrix3x4 Identity = new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);
	public float M00, M01, M02, M03;
	public float M10, M11, M12, M13;
	public float M20, M21, M22, M23;
	public unsafe Span<float> this[int row] {
		get {
			if (row < 0 || row >= 3) throw new ArgumentOutOfRangeException(nameof(row));
			return new(Unsafe.AsPointer(ref Unsafe.Add(ref M00, (row * 4))), 4);
		}
	}
	public unsafe float this[int row, int col] {
		get {
			if (row < 0 || row >= 3) throw new ArgumentOutOfRangeException(nameof(row));
			if (col < 0 || col >= 4) throw new ArgumentOutOfRangeException(nameof(col));

			return Unsafe.Add(ref M00, (row * 4) + col);
		}
		set {
			if (row < 0 || row >= 3) throw new ArgumentOutOfRangeException(nameof(row));
			if (col < 0 || col >= 4) throw new ArgumentOutOfRangeException(nameof(col));

			Unsafe.Add(ref M00, (row * 4) + col) = value;
		}
	}

	public static implicit operator Matrix4x4(Matrix3x4 self) {
		Matrix4x4 ret = default;
		ret.Init(self);
		return ret;
	}

	public Matrix3x4(
		float m00, float m01, float m02, float m03,
		float m10, float m11, float m12, float m13,
		float m20, float m21, float m22, float m23) {
		M00 = m00; M01 = m01; M02 = m02; M03 = m03;
		M10 = m10; M11 = m11; M12 = m12; M13 = m13;
		M20 = m20; M21 = m21; M22 = m22; M23 = m23;
	}
}

public enum PlaneType : byte
{
	NormalX = 0,
	NormalY = 4,
	NormalZ = 8,
	Dist = 12,
	Type = 16,
	SignBits = 17,
	Pad0 = 18,
	Pad1 = 19
}


public struct CollisionBrushSide
{
	public Memory<CollisionPlane> PlaneBackingMemory;
	public int PlaneIdx;
	public ref CollisionPlane Plane => ref PlaneBackingMemory.Span[PlaneIdx];

	public ushort SurfaceIndex;
	public bool Bevel;

	public void SetPlanePointer(CollisionPlane[] collisionPlanes, ushort planeNum) {
		PlaneBackingMemory = collisionPlanes;
		PlaneIdx = planeNum;
	}
}

public struct CollisionBrush
{
	public const int NUMSIDES_BOXBRUSH = -1;
	public Contents Contents;
	public int NumSides;
	public int FirstBrushSide;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int GetBox() => FirstBrushSide;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void SetBox(int boxID) {
		NumSides = NUMSIDES_BOXBRUSH;
		FirstBrushSide = boxID;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsBox() => NumSides == NUMSIDES_BOXBRUSH;
}

public struct CollisionBoxBrush
{
	public Vector3 Mins;
	public Vector3 Maxs;

	public InlineArray6<ushort> SurfaceIndex;
	public InlineArray2<ushort> Pad2;
}

public struct CollisionLeaf
{
	public Contents Contents;
	public short Cluster;

	private short areaFlags;
	public ushort FirstLeafBrush;
	public ushort NumLeafBrushes;
	public ushort DispListStart;
	public ushort DispCount;

	public short Area {
		readonly get => (short)(areaFlags & 0x1FF);
		set => areaFlags = (short)((areaFlags & ~0x1FF) | (value & 0x1FF));
	}

	public short Flags {
		readonly get => (short)((areaFlags >> 9) & 0x7F);
		set => areaFlags = (short)((areaFlags & ~(0x7F << 9)) | ((value & 0x7F) << 9));
	}
}

public struct CollisionNode
{
	public int CollisionPlaneIdx;
	public InlineArray2<int> Children;
}

public struct CollisionArea
{
	public int NumAreaPortals;
	public int FirstAreaPortal;
	public int FloodNum;
	public int FloodValid;
}


/// <summary>
/// Analog of cplane_t
/// </summary>
public struct CollisionPlane
{
	public Vector3 Normal;
	public float Dist;
	public PlaneType Type;
	public byte SignBits;
	public InlineArray2<byte> Pad;
}

public class Frustum_t
{
	readonly CollisionPlane[] Plane = new CollisionPlane[(int)FrustumPlane.NumPlanes];
	readonly Vector3[] AbsNormal = new Vector3[(int)FrustumPlane.NumPlanes];

	public void SetPlane(int i, byte nType, in Vector3 normal, float dist) {
		Plane[i].Normal = normal;
		Plane[i].Dist = dist;
		Plane[i].Type = (PlaneType)nType;
		Plane[i].SignBits = MathLib.SignbitsForPlane(in Plane[i]);
		AbsNormal[i] = new(MathF.Abs(normal.X), MathF.Abs(normal.Y), MathF.Abs(normal.Z));
	}

	public ref readonly CollisionPlane GetPlane(int i) => ref Plane[i];
	public ref readonly Vector3 GetAbsNormal(int i) => ref AbsNormal[i];
}
public static class MathLib
{
	public static bool MathlibInitialized;
	public static void Init(float gamma, float texGamma, float brightness, int overbright) {
		if (MathlibInitialized) return;
		MathlibInitialized = true;
		BuildGammaTable(gamma, texGamma, brightness, overbright);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float SimpleSplineRemapValClamped(float val, float A, float B, float C, float D) {
		if (A == B)
			return val >= B ? D : C;
		float cval = (val - A) / (B - A);
		cval = Math.Clamp(cval, 0.0f, 1.0f);
		return C + (D - C) * SimpleSpline(cval);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double SimpleSplineRemapValClamped(double val, double A, double B, double C, double D) {
		if (A == B)
			return val >= B ? D : C;
		double cval = (val - A) / (B - A);
		cval = Math.Clamp(cval, 0.0f, 1.0f);
		return C + (D - C) * SimpleSpline(cval);
	}

	private static void BuildGammaTable(float gamma, float texGamma, float brightness, int overbright) {
		int i;

		// Con_Printf("BuildGammaTable %.1f %.1f %.1f\n", g, v_lightgamma.GetFloat(), v_texgamma.GetFloat() );

		float g = 1.0f / MathF.Min(gamma, 3.0F);
		float g1 = texGamma * g;
		float g3;

		if (brightness <= 0.0)
			g3 = 0.125f;
		else if (brightness > 1.0f)
			g3 = 0.05f;
		else
			g3 = 0.125f - (brightness * brightness) * 0.075f;

		for (i = 0; i < 256; i++) {
			int inf = (int)(255 * MathF.Pow(i / 255f, g1));
			texgammatable[i] = (byte)(int)(MathF.Max(MathF.Min(inf, 255), 0));
		}

		for (i = 0; i < 1024; i++) {
			float f = i / 1023.0f;

			// scale up
			if (brightness > 1.0f)
				f = f * brightness;

			// shift up
			if (f <= g3)
				f = (f / g3) * 0.125f;
			else
				f = 0.125f + ((f - g3) / (1.0f - g3)) * 0.875f;

			// convert linear space to desired gamma space
			int inf = (int)(255 * MathF.Pow(f, g));
			lineartoscreen[i] = Math.Max(Math.Min(inf, 255), 0);
		}

		for (i = 0; i < 256; i++) {
			// convert from nonlinear texture space (0..255) to linear space (0..1)
			texturetolinear[i] = MathF.Pow(i / 255f, texGamma);

			// convert from linear space (0..1) to nonlinear (sRGB) space (0..1)
			Mathlib_LinearToGamma[i] = LinearToGammaFullRange(i / 255f);

			// convert from sRGB gamma space (0..1) to linear space (0..1)
			Mathlib_GammaToLinear[i] = GammaToLinearFullRange(i / 255f);
		}

		for (i = 0; i < 1024; i++) {
			// convert from linear space (0..1) to nonlinear texture space (0..255)
			lineartotexture[i] = (int)(MathF.Pow(i / 1023.0f, 1.0f / texGamma) * 255);
		}

		{
			float f;
			float overbrightFactor = 1.0f;

			// Can't do overbright without texcombine
			// UNDONE: Add GAMMA ramp to rectify this
			if (overbright == 2)
				overbrightFactor = 0.5f;
			else if (overbright == 4)
				overbrightFactor = 0.25f;

			for (i = 0; i < 4096; i++) {
				// convert from linear 0..4 (x1024) to screen corrected vertex space (0..1?)
				f = MathF.Pow(i / 1024.0f, 1.0f / gamma);

				lineartovertex[i] = f * overbrightFactor;
				if (lineartovertex[i] > 1)
					lineartovertex[i] = 1;

				int lightmap = RoundFloatToInt(f * 255 * overbrightFactor);
				lightmap = Math.Clamp(lightmap, 0, 255);
				lineartolightmap[i] = (byte)lightmap;
			}
		}
	}

	public static float GammaToLinearFullRange(float gamma) => MathF.Pow(gamma, 2.2f);
	public static float LinearToGammaFullRange(float linear) => MathF.Pow(linear, 1.0f / 2.2f);
	public static float GammaToLinear(float gamma) {
		Assert(MathlibInitialized);
		if (gamma < 0.0f)
			return 0.0f;

		if (gamma >= 0.95f)
			return 1.0f;

		int index = RoundFloatToInt(gamma * 255.0f);
		Assert(index >= 0 && index < 256);
		return Mathlib_GammaToLinear[index];
	}

	public static byte LinearToLightmap(float f) {
		int i = RoundFloatToInt(f * 1024.0f);
		if ((uint)i > 4095)
			i = i < 0 ? 0 : 4095;
		return lineartolightmap[i];
	}

	public static float LinearToGamma(float linear) {
		Assert(MathlibInitialized);
		if (linear < 0.0f) {
			return 0.0f;
		}
		if (linear > 1.0f) {
			// Use LinearToGammaFullRange maybe if you trip this.
			Assert(0);
			return 1.0f;
		}

		int index = RoundFloatToInt(linear * 255.0f);
		Assert(index >= 0 && index < 256);
		return Mathlib_LinearToGamma[index];
	}
	public static float SrgbGammaToLinear(float srgbGammaValue) {
		float x = Math.Clamp(srgbGammaValue, 0.0f, 1.0f);
		return (x <= 0.04045f) ? (x / 12.92f) : (MathF.Pow((x + 0.055f) / 1.055f, 2.4f));
	}
	public static float SrgbLinearToGamma(float linearValue) {
		float x = Math.Clamp(linearValue, 0.0f, 1.0f);
		return (x <= 0.0031308f) ? (x * 12.92f) : (1.055f * MathF.Pow(x, (1.0f / 2.4f))) - 0.055f;
	}
	public static float TextureToLinear(int c) {
		Assert(MathlibInitialized);
		if (c < 0)
			return 0;
		if (c > 255)
			return 1.0f;

		return texturetolinear[c];
	}

	// convert texture to linear 0..1 value
	public static int LinearToTexture(float f) {
		Assert(MathlibInitialized);
		int i = Math.Max(Math.Min((int)(f * 1023), 1023), 0);  // assume 0..1 range
		return lineartotexture[i];
	}
	public static int LinearToScreenGamma(float f) {
		Assert(MathlibInitialized);
		int i = Math.Max(Math.Min((int)(f * 1023), 1023), 0);  // assume 0..1 range
		return lineartoscreen[i];
	}

	public static void ColorRGBExp32ToVector(in ColorRGBExp32 input, out Vector3 output) {
		Assert(MathlibInitialized);
		// FIXME: Why is there a factor of 255 built into this?
		output.X = 255.0f * TexLightToLinear(input.R, input.Exponent);
		output.Y = 255.0f * TexLightToLinear(input.G, input.Exponent);
		output.Z = 255.0f * TexLightToLinear(input.B, input.Exponent);
	}

	public static readonly byte[] texgammatable = new byte[256]; // palette is sent through this to convert to screen gamma

	public static readonly float[] texturetolinear = new float[256];  // texture (0..255) to linear (0..1)
	public static readonly int[] lineartotexture = new int[1024];   // linear (0..1) to texture (0..255)
	public static readonly int[] lineartoscreen = new int[1024];    // linear (0..1) to gamma corrected vertex light (0..255)

	// build a lightmap texture to combine with surface texture, adjust for src*dst+dst*src, ramp reprogramming, etc
	public static readonly float[] lineartovertex = new float[4096]; // linear (0..4) to screen corrected vertex space (0..1?)
	public static readonly byte[] lineartolightmap = new byte[4096];   // linear (0..4) to screen corrected texture value (0..255)

	public static readonly float[] Mathlib_GammaToLinear = new float[256];  // gamma (0..1) to linear (0..1)
	public static readonly float[] Mathlib_LinearToGamma = new float[256];  // linear (0..1) to gamma (0..1)


	public static readonly Vector128<float> Four_PointFives = Vector128.Create(0.5f, 0.5f, 0.5f, 0.5f);
	public static readonly Vector128<float> Four_Zeros = Vector128.Create(0.0f, 0.0f, 0.0f, 0.0f);
	public static readonly Vector128<float> Four_Ones = Vector128.Create(1.0f, 1.0f, 1.0f, 1.0f);
	public static readonly Vector128<float> Four_Twos = Vector128.Create(2.0f, 2.0f, 2.0f, 2.0f);
	public static readonly Vector128<float> Four_Threes = Vector128.Create(3.0f, 3.0f, 3.0f, 3.0f);
	public static readonly Vector128<float> Four_Fours = Vector128.Create(4.0f, 4.0f, 4.0f, 4.0f);
	public static readonly Vector128<float> Four_Origin = Vector128.Create(0f, 0f, 0f, 1f);
	public static readonly Vector128<float> Four_NegativeOnes = Vector128.Create(-1f, -1f, -1f, -1f);
	public static readonly Vector128<float> Four_2ToThe21s = Vector128.Create((float)(1 << 21), (float)(1 << 21), (float)(1 << 21), (float)(1 << 21));
	public static readonly Vector128<float> Four_2ToThe22s = Vector128.Create((float)(1 << 22), (float)(1 << 22), (float)(1 << 22), (float)(1 << 22));
	public static readonly Vector128<float> Four_2ToThe23s = Vector128.Create((float)(1 << 23), (float)(1 << 23), (float)(1 << 23), (float)(1 << 23));
	public static readonly Vector128<float> Four_2ToThe24s = Vector128.Create((float)(1 << 24), (float)(1 << 24), (float)(1 << 24), (float)(1 << 24));
	public static readonly Vector128<float> Four_Point225s = Vector128.Create(.225f, .225f, .225f, .225f);
	public static readonly Vector128<float> Four_Epsilons = Vector128.Create(FLT_EPSILON, FLT_EPSILON, FLT_EPSILON, FLT_EPSILON);
	public static readonly Vector128<float> Four_FLT_MAX = Vector128.Create(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue);
	public static readonly Vector128<float> Four_Negative_FLT_MAX = Vector128.Create(-float.MaxValue, -float.MaxValue, -float.MaxValue, -float.MaxValue);
	public static Vector3 AsVector3(this ReadOnlySpan<float> span) => new(span[0], span[1], span[2]);
	static MathLib() {

	}

	// These can just be Systems.Numerics calls since I think System.Numerics SIMD's them. These are just to have consistent naming with Source
	// (and maybe allow us to deviate easily if need be at some point).

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorAdd(in Vector3 a, in Vector3 b, out Vector3 result) => result = Vector3.Add(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorAdd(in Vector3 a, vec_t b, out Vector3 result) => result = Vector3.Add(a, new(b));
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorSubtract(in Vector3 a, in Vector3 b, out Vector3 result) => result = Vector3.Subtract(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorSubtract(in Vector3 a, vec_t b, out Vector3 result) => result = Vector3.Subtract(a, new(b));
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorMultiply(in Vector3 inVec, in Vector3 scale, out Vector3 result) => result = Vector3.Multiply(inVec, scale);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorMultiply(in Vector3 inVec, vec_t scale, out Vector3 result) => result = Vector3.Multiply(inVec, scale);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorDivide(in Vector3 inVec, in Vector3 scale, out Vector3 result) => result = Vector3.Divide(inVec, scale);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorDivide(in Vector3 inVec, vec_t scale, out Vector3 result) => result = Vector3.Divide(inVec, scale);


	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorMin(in Vector3 a, in Vector3 b, out Vector3 result) => result = Vector3.Min(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorMax(in Vector3 a, in Vector3 b, out Vector3 result) => result = Vector3.Max(a, b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float VectorMaximum(in Vector3 v) => MathF.Max(v.X, MathF.Max(v.Y, v.Z));
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static int RoundFloatToInt(float f) => (int)f;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static byte RoundFloatToByte(float f) => (byte)RoundFloatToInt(f);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ColorClamp(ref Vector3 color) {
		float maxc = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
		if (maxc > 1.0f) {
			float ooMax = 1.0f / maxc;
			color.X *= ooMax;
			color.Y *= ooMax;
			color.Z *= ooMax;
		}

		if (color[0] < 0f) color[0] = 0f;
		if (color[1] < 0f) color[1] = 0f;
		if (color[2] < 0f) color[2] = 0f;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorScale(in Vector3 inVec, in Vector3 scale, out Vector3 result) => result = Vector3.Multiply(inVec, scale);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorScale(in Vector3 inVec, vec_t scale, out Vector3 result) => result = Vector3.Multiply(inVec, scale);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorScale(in Vector3 inVec, in Vector3 scale, Span<float> result) {
		Vector3 v = Vector3.Multiply(inVec, scale);
		ReadOnlySpan<Vector3> vS = new(in v);
		vS.Cast<Vector3, float>().CopyTo(result);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorScale(in Vector3 inVec, vec_t scale, Span<float> result) {
		Vector3 v = Vector3.Multiply(inVec, scale);
		ReadOnlySpan<Vector3> vS = new(in v);
		vS.Cast<Vector3, float>().CopyTo(result);
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorScale(ReadOnlySpan<float> inVec, in Vector3 scale, out Vector3 result) => result = Vector3.Multiply(inVec.Cast<float, Vector3>()[0], scale);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorScale(ReadOnlySpan<float> inVec, vec_t scale, out Vector3 result) => result = Vector3.Multiply(inVec.Cast<float, Vector3>()[0], scale);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorScale(ReadOnlySpan<float> inVec, in Vector3 scale, Span<float> result) {
		Vector3 v = Vector3.Multiply(inVec.Cast<float, Vector3>()[0], scale);
		ReadOnlySpan<Vector3> vS = new(in v);
		vS.Cast<Vector3, float>().CopyTo(result);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorScale(ReadOnlySpan<float> inVec, vec_t scale, Span<float> result) {
		Vector3 v = Vector3.Multiply(inVec.Cast<float, Vector3>()[0], scale);
		ReadOnlySpan<Vector3> vS = new(in v);
		vS.Cast<Vector3, float>().CopyTo(result);
	}



	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref Vector3 AsVector3D(this ref Vector4 vec)
		=> ref new Span<Vector4>(ref vec).Cast<Vector4, float>()[..3].Cast<float, Vector3>()[0];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref Vector2 AsVector2D(this ref Vector4 vec)
		=> ref new Span<Vector4>(ref vec).Cast<Vector4, float>()[..2].Cast<float, Vector2>()[0];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector2 AsVector2D(this Vector3 v) => new(v.X, v.Y);

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static vec_t DistTo(this in Vector3 vec, in Vector3 other) => Vector3.Distance(vec, other);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Min(this in Vector3 vec, in Vector3 other) => Vector3.Min(vec, other);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector3 Max(this in Vector3 vec, in Vector3 other) => Vector3.Max(vec, other);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Negate(this ref Vector3 vec) => vec = Vector3.Negate(vec);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static vec_t DistToSqr(this in Vector3 vec, in Vector3 other) => Vector3.DistanceSquared(vec, other);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static vec_t Length2D(this in Vector3 vec) => MathF.Sqrt(vec.X * vec.X + vec.Y * vec.Y);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static vec_t Length2DSqr(this in Vector3 vec) => vec.X * vec.X + vec.Y * vec.Y;


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float Lerp(float percent, float start, float end) {
		return start + (end - start) * percent;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float Lerp(float f1, float f2, float i1, float i2, float x) {
		return f1 + (f2 - f1) * (x - i1) / (i2 - i1);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double Fmod(double x, double y) {
		return x - y * Math.Truncate(x / y);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float Fmodf(float x, float y) {
		return x - y * (float)MathF.Truncate(x / y);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double Fmodf(double x, double y) {
		return x - y * Math.Truncate(x / y);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int Modulo(int a, int b) {
		return (Math.Abs(a * b) + a) % b;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float SimpleSpline(float value) {
		return (value * value) * (3 - 2 * value);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double SimpleSpline(double value) {
		return (value * value) * (3 - 2 * value);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int CeilPow2(int input) {
		int retval = 1;
		while (retval < input)
			retval <<= 2;
		return retval;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int FloorPow2(int input) {
		int retval = 1;
		while (retval < input)
			retval <<= 1;
		return retval >> 1;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static nuint CeilPow2(nuint input) {
		nuint retval = 1;
		while (retval < input)
			retval <<= 2;
		return retval;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static nuint FloorPow2(nuint input) {
		nuint retval = 1;
		while (retval < input)
			retval <<= 1;
		return retval >> 1;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double Bias(double x, double biasAmount) {
		double fRet = Math.Pow(x, Math.Log(biasAmount) * -1.4427);
		Assert(!double.IsNaN(fRet));
		return fRet;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double Gain(double x, double biasAmount) {
		if (x < 0.5)
			return 0.5f * Bias(2 * x, 1 - biasAmount);
		else
			return 1 - 0.5f * Bias(2 - 2 * x, 1 - biasAmount);
	}


	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float RAD2DEG(float x) => x * (180f / MathF.PI);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static double RAD2DEG(double x) => x * (180 / Math.PI);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float DEG2RAD(float x) => x * (MathF.PI / 180);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static double DEG2RAD(double x) => x * (Math.PI / 180);

	private static void FrustumPlanesFromMatrixHelper(in Matrix4x4 shadowToWorld, in Vector3 p1, in Vector3 p2, in Vector3 p3, out Vector3 normal, out float dist) {
		Vector3DMultiplyPositionProjective(in shadowToWorld, in p1, out Vector3 world1);
		Vector3DMultiplyPositionProjective(in shadowToWorld, in p2, out Vector3 world2);
		Vector3DMultiplyPositionProjective(in shadowToWorld, in p3, out Vector3 world3);

		VectorSubtract(world2, world1, out Vector3 v1);
		VectorSubtract(world3, world1, out Vector3 v2);

		CrossProduct(v1, v2, out normal);
		VectorNormalize(ref normal);
		dist = DotProduct(normal, world1);
	}

	public static void FrustumPlanesFromMatrix(in Matrix4x4 clipToWorld, Frustum_t frustum) {
		Vector3 normal;
		float dist;

		FrustumPlanesFromMatrixHelper(in clipToWorld,
			new(0.0f, 0.0f, 0.0f), new(1.0f, 0.0f, 0.0f), new(0.0f, 1.0f, 0.0f), out normal, out dist);
		frustum.SetPlane((int)FrustumPlane.NearZ, 5, normal, dist);

		FrustumPlanesFromMatrixHelper(in clipToWorld,
			new(0.0f, 0.0f, 1.0f), new(0.0f, 1.0f, 1.0f), new(1.0f, 0.0f, 1.0f), out normal, out dist);
		frustum.SetPlane((int)FrustumPlane.FarZ, 5, normal, dist);

		FrustumPlanesFromMatrixHelper(in clipToWorld,
			new(1.0f, 0.0f, 0.0f), new(1.0f, 1.0f, 1.0f), new(1.0f, 1.0f, 0.0f), out normal, out dist);
		frustum.SetPlane((int)FrustumPlane.Right, 5, normal, dist);

		FrustumPlanesFromMatrixHelper(in clipToWorld,
			new(0.0f, 0.0f, 0.0f), new(0.0f, 1.0f, 1.0f), new(0.0f, 0.0f, 1.0f), out normal, out dist);
		frustum.SetPlane((int)FrustumPlane.Left, 5, normal, dist);

		FrustumPlanesFromMatrixHelper(in clipToWorld,
			new(1.0f, 1.0f, 0.0f), new(1.0f, 1.0f, 1.0f), new(0.0f, 1.0f, 1.0f), out normal, out dist);
		frustum.SetPlane((int)FrustumPlane.Top, 5, normal, dist);

		FrustumPlanesFromMatrixHelper(in clipToWorld,
			new(1.0f, 0.0f, 0.0f), new(0.0f, 0.0f, 1.0f), new(1.0f, 0.0f, 1.0f), out normal, out dist);
		frustum.SetPlane((int)FrustumPlane.Bottom, 5, normal, dist);
	}

	public static void GeneratePerspectiveFrustum(in Vector3 origin, in Vector3 forward, in Vector3 right, in Vector3 up, float zNear, float zFar, float fovX, float fovY, Frustum_t frustum) {
		float intercept = DotProduct(origin, forward);

		frustum.SetPlane((int)FrustumPlane.FarZ, 5, -forward, -zFar - intercept);
		frustum.SetPlane((int)FrustumPlane.NearZ, 5, forward, zNear + intercept);

		fovX *= 0.5f;
		fovY *= 0.5f;

		float tanX = MathF.Tan(DEG2RAD(fovX));
		float tanY = MathF.Tan(DEG2RAD(fovY));

		VectorMA(right, tanX, forward, out Vector3 normalPos);
		VectorMA(normalPos, -2.0f, right, out Vector3 normalNeg);

		VectorNormalize(ref normalPos);
		VectorNormalize(ref normalNeg);

		frustum.SetPlane((int)FrustumPlane.Left, 5, normalPos, Vector3.Dot(normalPos, origin));
		frustum.SetPlane((int)FrustumPlane.Right, 5, normalNeg, Vector3.Dot(normalNeg, origin));

		VectorMA(up, tanY, forward, out normalPos);
		VectorMA(normalPos, -2.0f, up, out normalNeg);

		VectorNormalize(ref normalPos);
		VectorNormalize(ref normalNeg);

		frustum.SetPlane((int)FrustumPlane.Bottom, 5, normalPos, Vector3.Dot(normalPos, origin));
		frustum.SetPlane((int)FrustumPlane.Top, 5, normalNeg, Vector3.Dot(normalNeg, origin));
	}

	public static float CalcFovX(float fovY, float aspect)
		=> RAD2DEG(MathF.Atan(MathF.Tan(DEG2RAD(fovY) * 0.5f) * aspect)) * 2.0f;
	public static float CalcFovY(float fovX, float aspect) {
		if (fovX < 0 || fovX > 179)
			fovX = 90;

		return RAD2DEG(MathF.Atan(MathF.Tan(DEG2RAD(fovX) * 0.5f) / aspect)) * 2.0f;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint RoundFloatToUnsignedLong(float f) {
		long rounded = checked((long)MathF.Round(f, MidpointRounding.ToEven));
		return (uint)rounded;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe int FastFloatToSmallInt(float f) {
		float shifted = f + (3 << 22);
		int* ptr = (int*)&shifted;
		return (*ptr & ((1 << 23) - 1)) - (1 << 22);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float AngleMod(float a) => (360f / 65536) * ((int)(a * (65536f / 360.0f)) & 65535);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void AngleMatrix(in QAngle angles, in Vector3 position, out Matrix3x4 matrix) {
		AngleMatrix(in angles, out matrix);
		MatrixSetColumn(in position, 3, ref matrix);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void MatrixSetColumn(in Vector3 inVec, int column, ref Matrix3x4 outMatrix) {
		outMatrix[0, column] = inVec.X;
		outMatrix[1, column] = inVec.Y;
		outMatrix[2, column] = inVec.Z;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void MatrixSetTranslation(in Vector3 inVec, ref Matrix3x4 outMatrix) => MatrixSetColumn(in inVec, 3, ref outMatrix);

	public static void MatrixScaleByZero(ref Matrix3x4 outMatrix) {
		for (int i = 0; i < 3; i++)
			for (int j = 0; j < 4; j++)
				outMatrix[i, j] = 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref float SubFloat(ref Vector3 a, int idx) {
		ArgumentOutOfRangeException.ThrowIfNegative(idx);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(idx, 3);

		return ref new Span<Vector3>(ref a).Cast<Vector3, float>()[idx];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe ref float SubFloat(ref fltx4 a, int idx) {
		ArgumentOutOfRangeException.ThrowIfNegative(idx);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(idx, 4);
		fixed (fltx4* ap = &a) {
			// This is probably terrible!
			return ref ((float*)ap)[idx];
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ref float SubFloat(ref Vector4 a, int idx) {
		ArgumentOutOfRangeException.ThrowIfNegative(idx);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(idx, 4);

		return ref new Span<Vector4>(ref a).Cast<Vector4, float>()[idx];
	}

	public static byte SignbitsForPlane(in CollisionPlane outPlane) {
		byte bits = 0;
		if (outPlane.Normal.X < 0) bits |= 1 << 0;
		if (outPlane.Normal.Y < 0) bits |= 1 << 1;
		if (outPlane.Normal.Z < 0) bits |= 1 << 2;
		return bits;
	}

	public static bool R_CullBox(in Vector3 mins, in Vector3 maxs, Frustum_t frustum) {
		return (BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.Right)) == 2) ||
			(BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.Left)) == 2) ||
			(BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.Top)) == 2) ||
			(BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.Bottom)) == 2) ||
			(BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.NearZ)) == 2) ||
			(BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.FarZ)) == 2);
	}

	public static bool R_CullBoxSkipNear(in Vector3 mins, in Vector3 maxs, Frustum_t frustum) {
		return (BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.Right)) == 2) ||
			(BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.Left)) == 2) ||
			(BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.Top)) == 2) ||
			(BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.Bottom)) == 2) ||
			(BoxOnPlaneSide(mins, maxs, frustum.GetPlane((int)FrustumPlane.FarZ)) == 2);
	}

	// Returns which side(s) of a plane a box straddles: 1 = front, 2 = back, 3 = both (straddling).
	// Analog of BOX_ON_PLANE_SIDE / BoxOnPlaneSide.
	public static int BoxOnPlaneSide(in Vector3 emins, in Vector3 emaxs, in CollisionPlane p) {
		// fast axial cases
		int type = (byte)p.Type;
		if (type < 3) {
			if (p.Dist <= emins[type])
				return 1;
			if (p.Dist >= emaxs[type])
				return 2;
			return 3;
		}

		// general case: pick the corner that maximizes and minimizes the dot with the normal
		Vector3 n = p.Normal;
		Vector3 cmax, cmin;
		cmax.X = n.X < 0.0f ? emins.X : emaxs.X;
		cmin.X = n.X < 0.0f ? emaxs.X : emins.X;
		cmax.Y = n.Y < 0.0f ? emins.Y : emaxs.Y;
		cmin.Y = n.Y < 0.0f ? emaxs.Y : emins.Y;
		cmax.Z = n.Z < 0.0f ? emins.Z : emaxs.Z;
		cmin.Z = n.Z < 0.0f ? emaxs.Z : emins.Z;

		float dist1 = Vector3.Dot(n, cmax);
		float dist2 = Vector3.Dot(n, cmin);

		int sides = 0;
		if (dist1 >= p.Dist)
			sides = 1;
		if (dist2 < p.Dist)
			sides |= 2;

		return sides;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe uint SubInt(in fltx4 a, int idx) {
		return a.AsUInt32().GetElement(idx);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe uint SubInt(in Vector3 a, int idx) {
		float val = a[idx];
		return Unsafe.As<float, uint>(ref val);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe uint SubInt(in Vector4 a, int idx) {
		float val = a[idx];
		return Unsafe.As<float, uint>(ref val);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void AngleQuaternion(in RadianEuler angles, out Quaternion outQuat) {
		fixed (RadianEuler* pQ = &angles) {
			Vector4 radians = new(*(Vector3*)pQ, 0);
			radians = Vector4.Multiply(radians, 0.5f);
			(Vector4 sine, Vector4 cosine) = Vector4.SinCos(radians);

			float sr = SubFloat(ref sine, 0), sp = SubFloat(ref sine, 1), sy = SubFloat(ref sine, 2);
			float cr = SubFloat(ref cosine, 0), cp = SubFloat(ref cosine, 1), cy = SubFloat(ref cosine, 2);

			float srXcp = sr * cp, crXsp = cr * sp;
			outQuat.X = srXcp * cy - crXsp * sy;
			outQuat.Y = crXsp * cy + srXcp * sy;

			float crXcp = cr * cp, srXsp = sr * sp;
			outQuat.Z = crXcp * sy - srXsp * cy;
			outQuat.W = crXcp * cy + srXsp * sy;
		}
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void AngleQuaternion(in QAngle angles, out Quaternion outQuat) {
		fixed (QAngle* pQ = &angles) {
			Vector4 radians = new(*(Vector3*)pQ, 0);
			radians = Vector4.Multiply(radians, MathF.PI / 360.0f);
			(Vector4 sine, Vector4 cosine) = Vector4.SinCos(radians);

			// NOTE: The ordering here is *different* from the RadianEuler AngleQuaternion above
			// because p, y, r are not in the same locations in QAngle + RadianEuler. Yay!
			float sp = SubFloat(ref sine, 0), sy = SubFloat(ref sine, 1), sr = SubFloat(ref sine, 2);
			float cp = SubFloat(ref cosine, 0), cy = SubFloat(ref cosine, 1), cr = SubFloat(ref cosine, 2);

			float srXcp = sr * cp, crXsp = cr * sp;
			outQuat.X = srXcp * cy - crXsp * sy;
			outQuat.Y = crXsp * cy + srXcp * sy;

			float crXcp = cr * cp, srXsp = sr * sp;
			outQuat.Z = crXcp * sy - srXsp * cy;
			outQuat.W = crXcp * cy + srXsp * sy;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void AngleMatrix(in QAngle angles, out Matrix3x4 matrix) {
		matrix = default;

		float radiansX = angles.X * (MathF.PI / 180.0f);
		float radiansY = angles.Y * (MathF.PI / 180.0f);
		float radiansZ = angles.Z * (MathF.PI / 180.0f);

		float sp = MathF.Sin(radiansX);
		float sy = MathF.Sin(radiansY);
		float sr = MathF.Sin(radiansZ);
		float cp = MathF.Cos(radiansX);
		float cy = MathF.Cos(radiansY);
		float cr = MathF.Cos(radiansZ);

		matrix[0, 0] = cp * cy;
		matrix[1, 0] = cp * sy;
		matrix[2, 0] = -sp;

		float crcy = cr * cy;
		float crsy = cr * sy;
		float srcy = sr * cy;
		float srsy = sr * sy;
		matrix[0, 1] = sp * srcy - crsy;
		matrix[1, 1] = sp * srsy + crcy;
		matrix[2, 1] = sr * cp;

		matrix[0, 2] = (sp * crcy + srsy);
		matrix[1, 2] = (sp * crsy - srcy);
		matrix[2, 2] = cr * cp;

		matrix[0, 3] = 0.0f;
		matrix[1, 3] = 0.0f;
		matrix[2, 3] = 0.0f;
	}

	public static void AngleIMatrix(in QAngle angles, out Matrix3x4 matrix) {
		matrix = default;

		SinCos(DEG2RAD(angles[YAW]), out float sy, out float cy);
		SinCos(DEG2RAD(angles[PITCH]), out float sp, out float cp);
		SinCos(DEG2RAD(angles[ROLL]), out float sr, out float cr);

		matrix[0, 0] = cp * cy;
		matrix[0, 1] = cp * sy;
		matrix[0, 2] = -sp;
		matrix[1, 0] = sr * sp * cy + cr * -sy;
		matrix[1, 1] = sr * sp * sy + cr * cy;
		matrix[1, 2] = sr * cp;
		matrix[2, 0] = (cr * sp * cy + -sr * -sy);
		matrix[2, 1] = (cr * sp * sy + -sr * cy);
		matrix[2, 2] = cr * cp;
		matrix[0, 3] = 0.0f;
		matrix[1, 3] = 0.0f;
		matrix[2, 3] = 0.0f;
	}

	public static void AngleIMatrix(in QAngle angles, in Vector3 position, out Matrix3x4 mat) {
		AngleIMatrix(angles, out mat);

		VectorRotate(position, mat, out Vector3 translation);
		translation *= -1.0f;
		MatrixSetColumn(translation, 3, ref mat);
	}

	public static float MatrixRowDotProduct(in Matrix3x4 in1, int row, in Vector3 in2) {
		Assert((row >= 0) && (row < 3));
		return in1[row, 0] * in2[0] + in1[row, 1] * in2[1] + in1[row, 2] * in2[2];
	}

	public static float MatrixColumnDotProduct(in Matrix3x4 in1, int col, in Vector3 in2) {
		Assert((col >= 0) && (col < 4));
		return in1[0, col] * in2[0] + in1[1, col] * in2[1] + in1[2, col] * in2[2];
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void NormalizeAngles(ref QAngle angles) {
		int i;

		for (i = 0; i < 3; i++) {
			if (angles[i] > 180.0f)
				angles[i] -= 360.0f;
			else if (angles[i] < -180.0f)
				angles[i] += 360.0f;
		}
	}

	public static void QuaternionMult(in Quaternion p, in Quaternion q, out Quaternion qt) {
		qt = Quaternion.Multiply(p, q);
	}
	public static void QuaternionScale(in Quaternion p, float t, out Quaternion q) {
		float r;
		float sinom = MathF.Sqrt(DotProduct(in p, in p));
		sinom = Math.Min(sinom, 1.0f);

		float sinsom = MathF.Sin(MathF.Asin(sinom) * t);

		t = sinsom / (sinom + FLT_EPSILON);
		q = default;
		VectorScale(in p.AsVector3ReadOnlyRef(), t, out q.AsVector3Ref());

		// rescale rotation
		r = 1.0f - sinsom * sinsom;

		// Assert( r >= 0 );
		if (r < 0.0f)
			r = 0.0f;
		r = MathF.Sqrt(r);

		// keep sign of rotation
		if (p.W < 0)
			q.W = -r;
		else
			q.W = r;

		Assert(q.IsValid());
	}

	public static void QuaternionSM(float s, in Quaternion p, in Quaternion q, ref Quaternion qt) {
		Quaternion p1, q1;
		QuaternionScale(p, s, out p1);
		QuaternionMult(p1, q, out q1);
		QuaternionNormalize2(ref q1);

		qt = q1;
	}
	public static void QuaternionMA(in Quaternion p, float s, in Quaternion q, ref Quaternion qt) {
		// TODO: simd
		QuaternionScale(q, s, out Quaternion q1);
		QuaternionMult(p, q1, out Quaternion p1);
		QuaternionNormalize2(ref p1);
		qt[0] = p1[0];
		qt[1] = p1[1];
		qt[2] = p1[2];
		qt[3] = p1[3];
	}

	public static void VectorMA(in Vector3 start, float scale, in Vector3 direction, out Vector3 dest) {
		dest.X = start.X + direction.X * scale;
		dest.Y = start.Y + direction.Y * scale;
		dest.Z = start.Z + direction.Z * scale;
	}

	public static void Vector4DMultiply(Matrix4x4 src1, in Vector4 src2, ref Vector4 dst) {
		Vector4 v = src2;

		dst.X = src1.M11 * v.X + src1.M12 * v.Y + src1.M13 * v.Z + src1.M14 * v.W;
		dst.Y = src1.M21 * v.X + src1.M22 * v.Y + src1.M23 * v.Z + src1.M24 * v.W;
		dst.Z = src1.M31 * v.X + src1.M32 * v.Y + src1.M33 * v.Z + src1.M34 * v.W;
		dst.W = src1.M41 * v.X + src1.M42 * v.Y + src1.M43 * v.Z + src1.M44 * v.W;
	}

	public static void ConcatTransforms(in Matrix3x4 in1, in Matrix3x4 in2, out Matrix3x4 result) {
		float a00 = in1.M00, a01 = in1.M01, a02 = in1.M02, a03 = in1.M03;
		float a10 = in1.M10, a11 = in1.M11, a12 = in1.M12, a13 = in1.M13;
		float a20 = in1.M20, a21 = in1.M21, a22 = in1.M22, a23 = in1.M23;

		float b00 = in2.M00, b01 = in2.M01, b02 = in2.M02, b03 = in2.M03;
		float b10 = in2.M10, b11 = in2.M11, b12 = in2.M12, b13 = in2.M13;
		float b20 = in2.M20, b21 = in2.M21, b22 = in2.M22, b23 = in2.M23;

		float out00 = a00 * b00 + a01 * b10 + a02 * b20;
		float out01 = a00 * b01 + a01 * b11 + a02 * b21;
		float out02 = a00 * b02 + a01 * b12 + a02 * b22;
		float out03 = a00 * b03 + a01 * b13 + a02 * b23 + a03;

		float out10 = a10 * b00 + a11 * b10 + a12 * b20;
		float out11 = a10 * b01 + a11 * b11 + a12 * b21;
		float out12 = a10 * b02 + a11 * b12 + a12 * b22;
		float out13 = a10 * b03 + a11 * b13 + a12 * b23 + a13;

		float out20 = a20 * b00 + a21 * b10 + a22 * b20;
		float out21 = a20 * b01 + a21 * b11 + a22 * b21;
		float out22 = a20 * b02 + a21 * b12 + a22 * b22;
		float out23 = a20 * b03 + a21 * b13 + a22 * b23 + a23;

		result = new Matrix3x4(
			out00, out01, out02, out03,
			out10, out11, out12, out13,
			out20, out21, out22, out23
		);
	}

	public static void QuaternionMatrix(in Quaternion quaternion, in Vector3 pos, out Matrix3x4 matrix) {
		QuaternionMatrix(quaternion, out matrix);

		matrix[0, 3] = pos.X;
		matrix[1, 3] = pos.Y;
		matrix[2, 3] = pos.Z;
	}

	public static void QuaternionSlerp(in Quaternion p, in Quaternion q, float t, out Quaternion qt) {
		QuaternionAlign(p, q, out Quaternion q2);
		QuaternionSlerpNoAlign(p, q2, t, out qt);
	}

	public static void QuaternionSlerpNoAlign(in Quaternion p, in Quaternion q, float t, out Quaternion qt) {
		qt = default;
		float cosom = p.X * q.X + p.Y * q.Y + p.Z * q.Z + p.W * q.W;

		if ((1.0f + cosom) > 0.000001f) {
			float sclp, sclq;
			if ((1.0f - cosom) > 0.000001f) {
				float omega = MathF.Acos(cosom);
				float sinom = MathF.Sin(omega);
				sclp = MathF.Sin((1.0f - t) * omega) / sinom;
				sclq = MathF.Sin(t * omega) / sinom;
			}
			else {
				sclp = 1.0f - t;
				sclq = t;
			}

			qt.X = sclp * p.X + sclq * q.X;
			qt.Y = sclp * p.Y + sclq * q.Y;
			qt.Z = sclp * p.Z + sclq * q.Z;
			qt.W = sclp * p.W + sclq * q.W;
		}
		else {
			qt.X = -q.Y;
			qt.Y = q.X;
			qt.Z = -q.W;
			qt.W = q.Z;

			float sclp = MathF.Sin((1.0f - t) * (0.5f * MathF.PI));
			float sclq = MathF.Sin(t * (0.5f * MathF.PI));

			qt.X = sclp * p.X + sclq * qt.X;
			qt.Y = sclp * p.Y + sclq * qt.Y;
			qt.Z = sclp * p.Z + sclq * qt.Z;
		}
	}

	public static void QuaternionMatrix(in Quaternion q, out Matrix3x4 m) {
		m = default;
		m[0, 0] = 1.0F - 2.0F * q.Y * q.Y - 2.0F * q.Z * q.Z;
		m[1, 0] = 2.0F * q.X * q.Y + 2.0F * q.W * q.Z;
		m[2, 0] = 2.0F * q.X * q.Z - 2.0F * q.W * q.Y;
		m[0, 1] = 2.0F * q.X * q.Y - 2.0F * q.W * q.Z;
		m[1, 1] = 1.0F - 2.0F * q.X * q.X - 2.0F * q.Z * q.Z;
		m[2, 1] = 2.0F * q.Y * q.Z + 2.0F * q.W * q.X;
		m[0, 2] = 2.0F * q.X * q.Z + 2.0F * q.W * q.Y;
		m[1, 2] = 2.0F * q.Y * q.Z - 2.0F * q.W * q.X;
		m[2, 2] = 1.0F - 2.0F * q.X * q.X - 2.0F * q.Y * q.Y;
		m[0, 3] = 0.0F;
		m[1, 3] = 0.0F;
		m[2, 3] = 0.0F;
	}

	public static void SetIdentityMatrix(out Matrix3x4 matrix) {
		matrix = default;
		matrix[0, 0] = 1.0f;
		matrix[1, 1] = 1.0f;
		matrix[2, 2] = 1.0f;
	}
	public static void SetIdentityMatrix(out Matrix4x4 matrix) {
		matrix = Matrix4x4.Identity;
	}

	public static void MatrixInvert(in Matrix3x4 inM, out Matrix3x4 outM) {
		outM = default;
		// transpose the matrix
		outM[0, 0] = inM[0, 0];
		outM[0, 1] = inM[1, 0];
		outM[0, 2] = inM[2, 0];
		outM[1, 0] = inM[0, 1];
		outM[1, 1] = inM[1, 1];
		outM[1, 2] = inM[2, 1];
		outM[2, 0] = inM[0, 2];
		outM[2, 1] = inM[1, 2];
		outM[2, 2] = inM[2, 2];

		// now fix up the translation to be in the other space
		Span<float> tmp = stackalloc float[3];
		tmp[0] = inM[0, 3];
		tmp[1] = inM[1, 3];
		tmp[2] = inM[2, 3];

		outM[0, 3] = -DotProduct(tmp, outM[0]);
		outM[1, 3] = -DotProduct(tmp, outM[1]);
		outM[2, 3] = -DotProduct(tmp, outM[2]);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static vec_t DotProduct(ReadOnlySpan<vec_t> v1, ReadOnlySpan<vec_t> v2) => v1[0] * v2[0] + v1[1] * v2[1] + v1[2] * v2[2];
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static vec_t DotProduct(ReadOnlySpan<vec_t> v1, in Vector3 v2) => v1[0] * v2[0] + v1[1] * v2[1] + v1[2] * v2[2];
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static vec_t DotProduct(in Vector3 v1, ReadOnlySpan<vec_t> v2) => v1[0] * v2[0] + v1[1] * v2[1] + v1[2] * v2[2];
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static vec_t DotProduct(in Vector3 v1, in Vector3 v2) => v1[0] * v2[0] + v1[1] * v2[1] + v1[2] * v2[2];

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static vec_t DotProductAbs(in Vector3 v0, ReadOnlySpan<vec_t> v1) => FloatMakePositive(v0.X * v1[0]) + FloatMakePositive(v0.Y * v1[1]) + FloatMakePositive(v0.Z * v1[2]);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static vec_t DotProductAbs(in Vector3 v0, in Vector3 v1) => FloatMakePositive(v0.X * v1.X) + FloatMakePositive(v0.Y * v1.Y) + FloatMakePositive(v0.Z * v1.Z);


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe vec_t DotProduct(in Quaternion v1, in Quaternion v2) {
		fixed (Quaternion* pV1 = &v1)
		fixed (Quaternion* pV2 = &v2) {
			return DotProduct(new(pV1, 4), new(pV2, 4));
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static vec_t DotProduct2D(in Vector2 v1, in Vector2 v2) => v1.X * v2.X + v1.Y * v2.Y;

	public static void MatrixBuildPerspectiveX(ref Matrix4x4 dst, float fovX, float aspectRatio, float zNear, float zFar) {
		float flWidthScale = 1.0f / MathF.Tan(fovX * MathF.PI / 360.0f);
		float flHeightScale = aspectRatio * flWidthScale;
		dst.Init(flWidthScale, 0.0f, 0.0f, 0.0f,
					0.0f, flHeightScale, 0.0f, 0.0f,
					0.0f, 0.0f, 0.0f, 0.0f,
					0.0f, 0.0f, -1.0f, 0.0f);

		MatrixBuildPerspectiveZRange(ref dst, zNear, zFar);
	}

	private static void MatrixBuildPerspectiveZRange(ref Matrix4x4 dst, float znear, float zfar) {
		dst[2, 0] = 0.0f;
		dst[2, 1] = 0.0f;
		dst[2, 2] = zfar / (znear - zfar);
		dst[2, 3] = znear * zfar / (znear - zfar);
	}

	private static void CalculateAABBForNormalizedFrustum_Helper(float x, float y, float z, in Matrix4x4 volumeToWorld, ref Vector3 mins, ref Vector3 maxs) {
		Vector3 volumeSpacePos = new(x, y, z);

		Assert(volumeSpacePos[0] >= -1e-3f);
		Assert(volumeSpacePos[0] - 1.0f <= 1e-3f);
		Assert(volumeSpacePos[1] >= -1e-3f);
		Assert(volumeSpacePos[1] - 1.0f <= 1e-3f);
		Assert(volumeSpacePos[2] >= -1e-3f);
		Assert(volumeSpacePos[2] - 1.0f <= 1e-3f);

		Vector3DMultiplyPositionProjective(in volumeToWorld, in volumeSpacePos, out Vector3 worldPos);
		AddPointToBounds(in worldPos, ref mins, ref maxs);
	}
	public static void CalcClosestPointOnLine(in Vector3 P, in Vector3 vLineA, in Vector3 vLineB, out Vector3 vClosest, out float outT) {
		Assert(MathlibInitialized);
		float t = CalcClosestPointToLineT(P, vLineA, vLineB, out Vector3 vDir);
		outT = t;
		VectorMA(vLineA, t, vDir, out vClosest);
	}
	public static float CalcDistanceToLine(in Vector3 P, in Vector3 vLineA, in Vector3 vLineB, out float outT) {
		Assert(MathlibInitialized);
		CalcClosestPointOnLine(P, vLineA, vLineB, out Vector3 vClosest, out outT);
		return P.DistTo(vClosest);
	}

	public static float CalcDistanceSqrToLine(in Vector3 P, in Vector3 vLineA, in Vector3 vLineB, out float outT) {
		Assert(MathlibInitialized);
		CalcClosestPointOnLine(P, vLineA, vLineB, out Vector3 vClosest, out outT);
		return P.DistToSqr(vClosest);
	}


	public static void CalculateAABBFromProjectionMatrixInverse(in Matrix4x4 volumeToWorld, out Vector3 mins, out Vector3 maxs) {
		ClearBounds(out mins, out maxs);
		CalculateAABBForNormalizedFrustum_Helper(0, 0, 0, in volumeToWorld, ref mins, ref maxs);
		CalculateAABBForNormalizedFrustum_Helper(0, 0, 1, in volumeToWorld, ref mins, ref maxs);
		CalculateAABBForNormalizedFrustum_Helper(0, 1, 0, in volumeToWorld, ref mins, ref maxs);
		CalculateAABBForNormalizedFrustum_Helper(0, 1, 1, in volumeToWorld, ref mins, ref maxs);
		CalculateAABBForNormalizedFrustum_Helper(1, 0, 0, in volumeToWorld, ref mins, ref maxs);
		CalculateAABBForNormalizedFrustum_Helper(1, 0, 1, in volumeToWorld, ref mins, ref maxs);
		CalculateAABBForNormalizedFrustum_Helper(1, 1, 0, in volumeToWorld, ref mins, ref maxs);
		CalculateAABBForNormalizedFrustum_Helper(1, 1, 1, in volumeToWorld, ref mins, ref maxs);
	}

	public static void CalculateAABBFromProjectionMatrix(in Matrix4x4 worldToVolume, out Vector3 mins, out Vector3 maxs) {
		MatrixInverseGeneral(in worldToVolume, out Matrix4x4 volumeToWorld);
		CalculateAABBFromProjectionMatrixInverse(in volumeToWorld, out mins, out maxs);
	}

	public static void CalculateSphereFromProjectionMatrixInverse(in Matrix4x4 volumeToWorld, out Vector3 center, out float radius) {
		Vector3DMultiplyPositionProjective(in volumeToWorld, new(0.5f, 0.5f, 0.0f), out Vector3 centerNear);
		Vector3DMultiplyPositionProjective(in volumeToWorld, new(0.5f, 0.5f, 1.0f), out Vector3 centerFar);
		Vector3DMultiplyPositionProjective(in volumeToWorld, new(0.0f, 0.0f, 0.0f), out Vector3 nearEdge);
		Vector3DMultiplyPositionProjective(in volumeToWorld, new(0.0f, 0.0f, 1.0f), out Vector3 farEdge);

		VectorSubtract(centerFar, centerNear, out Vector3 delta);
		float l = delta.Length();
		float h1Sqr = centerNear.DistToSqr(nearEdge);
		float h2Sqr = centerFar.DistToSqr(farEdge);
		float x = (l * l + h2Sqr - h1Sqr) / (2.0f * l);
		VectorMA(centerNear, x / l, delta, out center);
		radius = MathF.Sqrt(h1Sqr + x * x);
	}

	public static void CalculateSphereFromProjectionMatrix(in Matrix4x4 worldToVolume, out Vector3 center, out float radius) {
		MatrixInverseGeneral(in worldToVolume, out Matrix4x4 volumeToWorld);
		CalculateSphereFromProjectionMatrixInverse(in volumeToWorld, out center, out radius);
	}

	public static void MatrixBuildPerspective(out Matrix4x4 dst, float fovX, float fovY, float zNear, float zFar) {
		float width = 2 * zNear * MathF.Tan(fovX * (MathF.PI / 180.0f) * 0.5f);
		float height = 2 * zNear * MathF.Tan(fovY * (MathF.PI / 180.0f) * 0.5f);

		dst = default;
		dst[0, 0] = 2.0f * zNear / width;
		dst[1, 1] = 2.0f * zNear / height;
		dst[2, 2] = -zFar / (zNear - zFar);
		dst[3, 2] = 1.0f;
		dst[2, 3] = zNear * zFar / (zNear - zFar);

		Matrix4x4 negateXY = Matrix4x4.Identity;
		negateXY[0, 0] = -1.0f;
		negateXY[1, 1] = -1.0f;
		MatrixMultiply(in negateXY, in dst, out dst);

		Matrix4x4 addW = Matrix4x4.Identity;
		addW[0, 3] = 1.0f;
		addW[1, 3] = 1.0f;
		addW[2, 3] = 0.0f;
		MatrixMultiply(in addW, in dst, out dst);

		Matrix4x4 scaleHalf = Matrix4x4.Identity;
		scaleHalf[0, 0] = 0.5f;
		scaleHalf[1, 1] = 0.5f;
		MatrixMultiply(in scaleHalf, in dst, out dst);
	}

	public static bool IsZero(this in Vector3 v, float tolerance = 0.01f) {
		Vector3 zero = Vector3.Zero;
		Vector3 diff = Vector3.Abs(v - zero);
		Vector3 toleranceVec = new(tolerance);

		return diff.X <= toleranceVec.X &&
				 diff.Y <= toleranceVec.Y &&
				 diff.Z <= toleranceVec.Z;
	}

	public static void Init(this ref Vector2 v, vec_t ix, vec_t iy) {
		v.X = ix;
		v.Y = iy;
		Assert(IsValid(ref v));
	}
	public static void Init(this ref Vector2 v) => v.X = v.Y = 0;
	public static void Init(this ref Vector3 v) => v.X = v.Y = v.Z = 0;
	public static void Init(this ref QAngle a) => a.X = a.Y = a.Z = 0;
	public static void Init(this ref RadianEuler r) => r.X = r.Y = r.Z = 0;
	public static void Init(this ref Vector4 v) => v.X = v.Y = v.Z = v.W = 0;
	public static void Init(this ref Quaternion v) => v.X = v.Y = v.Z = v.W = 0;
	public static vec_t Dot(this in Vector3 a, in Vector3 b) => Vector3.Dot(a, b);
	public static vec_t NormalizeInPlace(this ref Vector3 self) => VectorNormalize(ref self);
	public static vec_t NormalizeInPlace(this ref Vector2 self) => VectorNormalize(ref self);
	public static vec_t LengthSqr(this in Vector3 self) => self.LengthSquared();


	public static void Init(this ref Vector3 m, float x, float y, float z) {
		m.X = x;
		m.Y = y;
		m.Z = z;
	}

	public static void Init(this ref Vector3 m, ReadOnlySpan<float> f) {
		m.X = f[0];
		m.Y = f[1];
		m.Z = f[2];
	}

	public static void Init(this ref Quaternion m, float x, float y, float z, float w) {
		m.X = x;
		m.Y = y;
		m.Z = z;
		m.W = w;
	}

	public static ref Vector3 AsVector3Ref(this ref Quaternion q) => ref new Span<Quaternion>(ref q).Cast<Quaternion, float>()[..3].Cast<float, Vector3>()[0];
	public static ref Vector4 AsVector4Ref(this ref Quaternion q) => ref new Span<Quaternion>(ref q).Cast<Quaternion, Vector4>()[0];
	public static ref readonly Vector3 AsVector3ReadOnlyRef(this ref readonly Quaternion q) => ref new ReadOnlySpan<Quaternion>(in q).Cast<Quaternion, float>()[..3].Cast<float, Vector3>()[0];
	public static ref readonly Vector4 AsVector4ReadOnlyRef(this ref readonly Quaternion q) => ref new ReadOnlySpan<Quaternion>(in q).Cast<Quaternion, Vector4>()[0];


	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CalcClosestPointOnAABB(in Vector3 mins, in Vector3 maxs, in Vector3 point, out Vector3 closestOut) {
		closestOut.X = Math.Clamp(point.X, mins.X, maxs.X);
		closestOut.Y = Math.Clamp(point.Y, mins.Y, maxs.Y);
		closestOut.Z = Math.Clamp(point.Z, mins.Z, maxs.Z);
	}

	public static float CalcSqrDistanceToAABB(in Vector3 mins, in Vector3 maxs, in Vector3 point) {
		float delta;
		float distSqr = 0.0f;

		if (point.X < mins.X) {
			delta = mins.X - point.X;
			distSqr += delta * delta;
		}
		else if (point.X > maxs.X) {
			delta = point.X - maxs.X;
			distSqr += delta * delta;
		}

		if (point.Y < mins.Y) {
			delta = mins.Y - point.Y;
			distSqr += delta * delta;
		}
		else if (point.Y > maxs.Y) {
			delta = point.Y - maxs.Y;
			distSqr += delta * delta;
		}

		if (point.Z < mins.Z) {
			delta = mins.Z - point.Z;
			distSqr += delta * delta;
		}
		else if (point.Z > maxs.Z) {
			delta = point.Z - maxs.Z;
			distSqr += delta * delta;
		}

		return distSqr;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsValid(this ref Vector2 v) => !Vector2.AnyWhereAllBitsSet(Vector2.IsNaN(v));
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsValid(this ref Vector3 v) => !Vector3.AnyWhereAllBitsSet(Vector3.IsNaN(v));
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsValid(this ref Vector4 v) => !Vector4.AnyWhereAllBitsSet(Vector4.IsNaN(v));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool IsValid(this ref Quaternion q) {
		fixed (Quaternion* pQ = &q)
			return !Vector4.AnyWhereAllBitsSet(Vector4.IsNaN(*(Vector4*)pQ));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool IsValid(this ref QAngle a) {
		fixed (QAngle* pA = &a)
			return !Vector3.AnyWhereAllBitsSet(Vector3.IsNaN(*(Vector3*)pA));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe bool IsValid(this ref RadianEuler R) {
		fixed (RadianEuler* pR = &R)
			return !Vector3.AnyWhereAllBitsSet(Vector3.IsNaN(*(Vector3*)pR));
	}

	public static void MatrixInverseGeneral(in Matrix4x4 src, out Matrix4x4 dst) => Matrix4x4.Invert(src, out dst);

	public static void V3Mul(this in Matrix4x4 m, in Vector3 vIn, out Vector3 vOut) {
		float rw = 1.0f / (m[3, 0] * vIn.X + m[3, 1] * vIn.Y + m[3, 2] * vIn.Z + m[3, 3]);
		vOut.X = (m[0, 0] * vIn.X + m[0, 1] * vIn.Y + m[0, 2] * vIn.Z + m[0, 3]) * rw;
		vOut.Y = (m[1, 0] * vIn.X + m[1, 1] * vIn.Y + m[1, 2] * vIn.Z + m[1, 3]) * rw;
		vOut.Z = (m[2, 0] * vIn.X + m[2, 1] * vIn.Y + m[2, 2] * vIn.Z + m[2, 3]) * rw;
	}

	public static void Init(this ref Matrix4x4 m, in Matrix3x4 m3x4) {
		new ReadOnlySpan<Matrix3x4>(in m3x4).Cast<Matrix3x4, float>().CopyTo(new Span<Matrix4x4>(ref m).Cast<Matrix4x4, float>());

		m[3, 0] = 0.0f;
		m[3, 1] = 0.0f;
		m[3, 2] = 0.0f;
		m[3, 3] = 1.0f;
	}


	public static void Init(this ref Matrix4x4 m, vec_t m00, vec_t m01, vec_t m02, vec_t m03,
	vec_t m10, vec_t m11, vec_t m12, vec_t m13,
	vec_t m20, vec_t m21, vec_t m22, vec_t m23,
	vec_t m30, vec_t m31, vec_t m32, vec_t m33) {
		m[0, 0] = m00;
		m[0, 1] = m01;
		m[0, 2] = m02;
		m[0, 3] = m03;

		m[1, 0] = m10;
		m[1, 1] = m11;
		m[1, 2] = m12;
		m[1, 3] = m13;

		m[2, 0] = m20;
		m[2, 1] = m21;
		m[2, 2] = m22;
		m[2, 3] = m23;

		m[3, 0] = m30;
		m[3, 1] = m31;
		m[3, 2] = m32;
		m[3, 3] = m33;
	}

	public static void MatrixOrtho(ref Matrix4x4 dst, float left, float right, float bottom, float top, float zNear, float zFar) {
		Matrix4x4 mat = default;
		MatrixBuildOrtho(ref mat, left, top, right, bottom, zNear, zFar);

		MatrixMultiply(in dst, in mat, out Matrix4x4 temp);
		dst = temp;
	}

	// TODO: SIMD this
	public static void MatrixMultiply(in Matrix4x4 src1, in Matrix4x4 src2, out Matrix4x4 dst) {
		float a00 = src1.M11, a01 = src1.M12, a02 = src1.M13, a03 = src1.M14;
		float a10 = src1.M21, a11 = src1.M22, a12 = src1.M23, a13 = src1.M24;
		float a20 = src1.M31, a21 = src1.M32, a22 = src1.M33, a23 = src1.M34;
		float a30 = src1.M41, a31 = src1.M42, a32 = src1.M43, a33 = src1.M44;

		float b00 = src2.M11, b01 = src2.M12, b02 = src2.M13, b03 = src2.M14;
		float b10 = src2.M21, b11 = src2.M22, b12 = src2.M23, b13 = src2.M24;
		float b20 = src2.M31, b21 = src2.M32, b22 = src2.M33, b23 = src2.M34;
		float b30 = src2.M41, b31 = src2.M42, b32 = src2.M43, b33 = src2.M44;

		float out00 = a00 * b00 + a01 * b10 + a02 * b20 + a03 * b30;
		float out01 = a00 * b01 + a01 * b11 + a02 * b21 + a03 * b31;
		float out02 = a00 * b02 + a01 * b12 + a02 * b22 + a03 * b32;
		float out03 = a00 * b03 + a01 * b13 + a02 * b23 + a03 * b33;

		float out10 = a10 * b00 + a11 * b10 + a12 * b20 + a13 * b30;
		float out11 = a10 * b01 + a11 * b11 + a12 * b21 + a13 * b31;
		float out12 = a10 * b02 + a11 * b12 + a12 * b22 + a13 * b32;
		float out13 = a10 * b03 + a11 * b13 + a12 * b23 + a13 * b33;

		float out20 = a20 * b00 + a21 * b10 + a22 * b20 + a23 * b30;
		float out21 = a20 * b01 + a21 * b11 + a22 * b21 + a23 * b31;
		float out22 = a20 * b02 + a21 * b12 + a22 * b22 + a23 * b32;
		float out23 = a20 * b03 + a21 * b13 + a22 * b23 + a23 * b33;

		float out30 = a30 * b00 + a31 * b10 + a32 * b20 + a33 * b30;
		float out31 = a30 * b01 + a31 * b11 + a32 * b21 + a33 * b31;
		float out32 = a30 * b02 + a31 * b12 + a32 * b22 + a33 * b32;
		float out33 = a30 * b03 + a31 * b13 + a32 * b23 + a33 * b33;

		dst = default;
		dst.Init(
			out00, out01, out02, out03,
			out10, out11, out12, out13,
			out20, out21, out22, out23,
			out30, out31, out32, out33
		);
	}

	private static void MatrixBuildOrtho(ref Matrix4x4 dst, float left, float top, float right, float bottom, float zNear, float zFar) {
		dst.Init(2.0f / (right - left), 0.0f, 0.0f, (left + right) / (left - right),
				0.0f, 2.0f / (bottom - top), 0.0f, (bottom + top) / (top - bottom),
				0.0f, 0.0f, 1.0f / (zNear - zFar), zNear / (zNear - zFar),
				0.0f, 0.0f, 0.0f, 1.0f);
	}

	public static void MatrixBuildScale(out Matrix4x4 dst, float x, float y, float z) {
		dst = default;
		dst[0, 0] = x; dst[0, 1] = 0.0f; dst[0, 2] = 0.0f; dst[0, 3] = 0.0f;
		dst[1, 0] = 0.0f; dst[1, 1] = y; dst[1, 2] = 0.0f; dst[1, 3] = 0.0f;
		dst[2, 0] = 0.0f; dst[2, 1] = 0.0f; dst[2, 2] = z; dst[2, 3] = 0.0f;
		dst[3, 0] = 0.0f; dst[3, 1] = 0.0f; dst[3, 2] = 0.0f; dst[3, 3] = 1.0f;
	}

	public static void MatrixBuildTranslation(out Matrix4x4 dst, float x, float y, float z) {
		dst = Matrix4x4.Identity;
		dst[0, 3] = x;
		dst[1, 3] = y;
		dst[2, 3] = z;
	}

	public static void BasisToQuaternion(in Vector3 forward, in Vector3 right, in Vector3 up, out Quaternion q) {
		Assert(MathF.Abs(forward.LengthSquared() - 1.0f) < 1e-3);
		Assert(MathF.Abs(right.LengthSquared() - 1.0f) < 1e-3);
		Assert(MathF.Abs(up.LengthSquared() - 1.0f) < 1e-3);

		VectorMultiply(in right, -1.0f, out Vector3 left);

		Matrix3x4 mat = default;
		MatrixSetColumn(in forward, 0, ref mat);
		MatrixSetColumn(in left, 1, ref mat);
		MatrixSetColumn(in up, 2, ref mat);

		MatrixAngles(in mat, out QAngle angles);

		AngleQuaternion(in angles, out q);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void MatrixAngles(in Matrix3x4 matrix, out QAngle angles, out Vector3 position) {
		MatrixAngles(matrix, out angles);
		MatrixPosition(matrix, out position);
	}

	public static void MatrixAngles(in Matrix3x4 matrix, out QAngle angles) {
		angles = default;
		Span<float> forward = stackalloc float[3];
		Span<float> left = stackalloc float[3];
		Span<float> up = stackalloc float[3];

		//
		// Extract the basis vectors from the matrix. Since we only need the Z
		// component of the up vector, we don't get X and Y.
		//
		forward[0] = matrix[0][0];
		forward[1] = matrix[1][0];
		forward[2] = matrix[2][0];
		left[0] = matrix[0][1];
		left[1] = matrix[1][1];
		left[2] = matrix[2][1];
		up[2] = matrix[2][2];

		float xyDist = MathF.Sqrt(forward[0] * forward[0] + forward[1] * forward[1]);

		// enough here to get angles?
		if (xyDist > 0.001f) {
			// (yaw)	y = ATAN( forward.y, forward.x );		-- in our space, forward is the X axis
			angles[1] = RAD2DEG(MathF.Atan2(forward[1], forward[0]));

			// (pitch)	x = ATAN( -forward.z, sqrt(forward.x*forward.x+forward.y*forward.y) );
			angles[0] = RAD2DEG(MathF.Atan2(-forward[2], xyDist));

			// (roll)	z = ATAN( left.z, up.z );
			angles[2] = RAD2DEG(MathF.Atan2(left[2], up[2]));
		}
		else    // forward is mostly Z, gimbal lock-
		{
			// (yaw)	y = ATAN( -left.x, left.y );			-- forward is mostly z, so use right for yaw
			angles[1] = RAD2DEG(MathF.Atan2(-left[0], left[1]));

			// (pitch)	x = ATAN( -forward.z, sqrt(forward.x*forward.x+forward.y*forward.y) );
			angles[0] = RAD2DEG(MathF.Atan2(-forward[2], xyDist));

			// Assume no roll in this case as one degree of freedom has been lost (i.e. yaw == roll)
			angles[2] = 0;
		}
	}

	public static void QuaternionBlend(in Quaternion p, in Quaternion q, float t, out Quaternion qt) {
		float dot = p.X * q.X + p.Y * q.Y + p.Z * q.Z + p.W * q.W;

		float sign = dot < 0.0f ? -1.0f : 1.0f;

		float oneMinusT = 1.0f - t;
		qt.X = p.X * oneMinusT + (q.X * sign) * t;
		qt.Y = p.Y * oneMinusT + (q.Y * sign) * t;
		qt.Z = p.Z * oneMinusT + (q.Z * sign) * t;
		qt.W = p.W * oneMinusT + (q.W * sign) * t;

		float length = MathF.Sqrt(qt.X * qt.X + qt.Y * qt.Y + qt.Z * qt.Z + qt.W * qt.W);
		if (length > 0.0f) {
			float invLength = 1.0f / length;
			qt.X *= invLength;
			qt.Y *= invLength;
			qt.Z *= invLength;
			qt.W *= invLength;
		}
	}

	public static void QuaternionBlendNoAlign(in Quaternion p, in Quaternion q, float t, out Quaternion qt) {
		float oneMinusT = 1.0f - t;
		qt.X = p.X * oneMinusT + q.X * t;
		qt.Y = p.Y * oneMinusT + q.Y * t;
		qt.Z = p.Z * oneMinusT + q.Z * t;
		qt.W = p.W * oneMinusT + q.W * t;

		float length = MathF.Sqrt(qt.X * qt.X + qt.Y * qt.Y + qt.Z * qt.Z + qt.W * qt.W);
		if (length > 0.0f) {
			float invLength = 1.0f / length;
			qt.X *= invLength;
			qt.Y *= invLength;
			qt.Z *= invLength;
			qt.W *= invLength;
		}
	}

	public static void QuaternionIdentityBlend(in Quaternion p, float t, out Quaternion qt) {
		float sclp = 1.0f - t;
		qt.X = p.X * sclp;
		qt.Y = p.Y * sclp;
		qt.Z = p.Z * sclp;
		qt.W = p.W * sclp;

		if (qt.W < 0.0f)
			t = -t;

		qt.W += t;

		float length = MathF.Sqrt(qt.X * qt.X + qt.Y * qt.Y + qt.Z * qt.Z + qt.W * qt.W);
		if (length > 0.0f) {
			float invLength = 1.0f / length;
			qt.X *= invLength;
			qt.Y *= invLength;
			qt.Z *= invLength;
			qt.W *= invLength;
		}
	}
	public static void Hermite_Spline(in Vector3 p1, in Vector3 p2, in Vector3 d1, in Vector3 d2, float t, out Vector3 output) {
		float tSqr = t * t;
		float tCube = t * tSqr;

		float b1 = 2.0f * tCube - 3.0f * tSqr + 1.0f;
		float b2 = 1.0f - b1; // -2*tCube+3*tSqr;
		float b3 = tCube - 2 * tSqr + t;
		float b4 = tCube - tSqr;

		VectorScale(p1, b1, out output);
		VectorMA(output, b2, p2, out output);
		VectorMA(output, b3, d1, out output);
		VectorMA(output, b4, d2, out output);
	}
	public static void Hermite_Spline(in Vector3 p0, in Vector3 p1, in Vector3 p2, float t, out Vector3 output) {
		Vector3 e10 = p1 - p0, e21 = p2 - p1;
		Hermite_Spline(p1, p2, e10, e21, t, out output);
	}

	public static void Hermite_Spline(in Quaternion q0, in Quaternion q1, in Quaternion q2, float t, out Quaternion output) {
		Quaternion q0a;
		Quaternion q1a;

		QuaternionAlign(q2, q0, out q0a);
		QuaternionAlign(q2, q1, out q1a);

		output.X = Hermite_Spline(q0a.X, q1a.X, q2.X, t);
		output.Y = Hermite_Spline(q0a.Y, q1a.Y, q2.Y, t);
		output.Z = Hermite_Spline(q0a.Z, q1a.Z, q2.Z, t);
		output.W = Hermite_Spline(q0a.W, q1a.W, q2.W, t);

		QuaternionNormalize2(ref output);
	}

	public static void QuaternionAlign(in Quaternion p, in Quaternion q, out Quaternion qt) {
		qt = default;
		int i;
		// decide if one of the quaternions is backwards
		float a = 0;
		float b = 0;
		for (i = 0; i < 4; i++) {
			a += (p[i] - q[i]) * (p[i] - q[i]);
			b += (p[i] + q[i]) * (p[i] + q[i]);
		}
		if (a > b) {
			for (i = 0; i < 4; i++) {
				qt[i] = -q[i];
			}
		}
		else if (!Unsafe.AreSame(in q, ref qt)) {
			for (i = 0; i < 4; i++) {
				qt[i] = q[i];
			}
		}
	}

	public static float Hermite_Spline(float p1, float p2, float d1, float d2, float t) {
		float output;
		float tSqr = t * t;
		float tCube = t * tSqr;

		float b1 = 2.0f * tCube - 3.0f * tSqr + 1.0f;
		float b2 = 1.0f - b1; // -2*tCube+3*tSqr;
		float b3 = tCube - 2 * tSqr + t;
		float b4 = tCube - tSqr;

		output = p1 * b1;
		output += p2 * b2;
		output += d1 * b3;
		output += d2 * b4;

		return output;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float Hermite_Spline(float p0, float p1, float p2, float t) {
		return Hermite_Spline(p1, p2, p1 - p0, p2 - p1, t);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void QuaternionNormalize2(ref Quaternion q) {
		q = Quaternion.Normalize(q);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	internal static void Hermite_Spline(Quaternion q0, Quaternion q1, Quaternion q2, float t, out Quaternion output) {
		QuaternionAlign(q2, q0, out Quaternion q0a);
		QuaternionAlign(q2, q1, out Quaternion q1a);

		output.X = Hermite_Spline(q0a.X, q1a.X, q2.X, t);
		output.Y = Hermite_Spline(q0a.Y, q1a.Y, q2.Y, t);
		output.Z = Hermite_Spline(q0a.Z, q1a.Z, q2.Z, t);
		output.W = Hermite_Spline(q0a.W, q1a.W, q2.W, t);

		QuaternionNormalize2(ref output);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float RemapVal(float val, float A, float B, float C, float D) {
		if (A == B)
			return val >= B ? D : C;
		return C + (D - C) * (val - A) / (B - A);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double RemapVal(double val, double A, double B, double C, double D) {
		if (A == B)
			return val >= B ? D : C;
		return C + (D - C) * (val - A) / (B - A);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool VectorsAreEqual(in Vector3 src1, in Vector3 src2, float tolerance = 0.0f) =>
		MathF.Abs(src1.X - src2.X) <= tolerance &&
		MathF.Abs(src1.Y - src2.Y) <= tolerance &&
		MathF.Abs(src1.Z - src2.Z) <= tolerance;

	public static bool CloseEnough(float a, float b, float epsilon = EQUAL_EPSILON) => MathF.Abs(a - b) <= epsilon;

	public static bool CloseEnough(in Vector3 a, in Vector3 b, float epsilon = EQUAL_EPSILON) =>
		MathF.Abs(a.X - b.X) <= epsilon &&
		MathF.Abs(a.Y - b.Y) <= epsilon &&
		MathF.Abs(a.Z - b.Z) <= epsilon;

	public static float Approach(float target, float value, float speed) {
		float delta = target - value;

		if (delta > speed)
			value += speed;
		else if (delta < -speed)
			value -= speed;
		else
			value = target;

		return value;
	}

	public static double Approach(double target, double value, double speed) {
		double delta = target - value;

		if (delta > speed)
			value += speed;
		else if (delta < -speed)
			value -= speed;
		else
			value = target;

		return value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double RemapValClamped(double val, double A, double B, double C, double D) {
		if (A == B)
			return val >= B ? D : C;

		double cVal = (val - A) / (B - A);
		cVal = Math.Clamp(cVal, 0.0, 1.0);

		return C + (D - C) * cVal;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static double SimpleSplineRemapVal(double val, double A, double B, double C, double D) {
		if (A == B)
			return val >= B ? D : C;
		double cVal = (val - A) / (B - A);
		return C + (D - C) * SimpleSpline(cVal);
	}
	public static QAngle RandomAngle(float minVal, float maxVal) {
		QAngle ret = new();
		ret.Random(minVal, maxVal);
		return ret;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void AngleVectors(in QAngle angles, out Vector3 forward) {
		SinCos(DEG2RAD(angles.X), out float sp, out float cp);
		SinCos(DEG2RAD(angles.Y), out float sy, out float cy);
		forward = new(cp * cy, cp * sy, -sp);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static unsafe void AngleVectors(in QAngle angles, out Vector3 forward, out Vector3 right, out Vector3 up) {
		fixed (QAngle* aptr = &angles) {
			Vector3 radians = Vector3.Multiply(*(Vector3*)aptr, MathF.PI / 180f);
			(Vector3 sine, Vector3 cosine) = Vector3.SinCos(radians);

			float sp = SubFloat(ref sine, 0), sy = SubFloat(ref sine, 1), sr = SubFloat(ref sine, 2);
			float cp = SubFloat(ref cosine, 0), cy = SubFloat(ref cosine, 1), cr = SubFloat(ref cosine, 2);

			forward = new(cp * cy, cp * sy, -sp);
			right = new(-1 * sr * sp * cy + -1 * cr * -sy, -1 * sr * sp * sy + -1 * cr * cy, -1 * sr * cp);
			up = new(cr * sp * cy + -sr * -sy, cr * sp * sy + -sr * cy, cr * cp);
		}
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorClear(out Vector3 v) => v = default;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Vector2DClear(out Vector2 v) => v = default;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorCopy(in Vector3 inV, out Vector3 outV) => outV = inV;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorCopy(in Vector3 inV, out QAngle outV) => outV = inV;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Vector2DCopy(in Vector2 inV, out Vector2 outV) => outV = inV;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void VectorLerp(in Vector3 src1, in Vector3 src2, float t, out Vector3 dest) => dest = Vector3.Lerp(src1, src2, t);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Vector2DLerp(in Vector2 src1, in Vector2 src2, float t, out Vector2 dest) => dest = Vector2.Lerp(src1, src2, t);

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Vector2DAdd(in Vector2 a, in Vector2 b, out Vector2 c) => c = a + b;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Vector2DSubtract(in Vector2 a, in Vector2 b, out Vector2 c) => c = a - b;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Vector2DMultiply(in Vector2 a, float b, out Vector2 c) => c = a * b;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Vector2DMultiply(in Vector2 a, in Vector2 b, out Vector2 c) => c = a * b;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Vector2DMin(in Vector2 a, in Vector2 b, out Vector2 c) => c = Vector2.Min(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static void Vector2DMax(in Vector2 a, in Vector2 b, out Vector2 c) => c = Vector2.Max(a, b);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float VectorNormalizeFast(ref Vector3 v) {
		float sqlen = v.X * v.X + v.Y * v.Y + v.Z * v.Z + 1.0e-10f;
		float invlen = MathF.ReciprocalSqrtEstimate(sqlen);
		invlen = invlen * ((3.0f - invlen * invlen * sqlen) * 0.5f);
		v = new Vector3(v.X * invlen, v.Y * invlen, v.Z * invlen);
		return 1.0f / invlen;
	}
	public static Vector3 RandomVector(float minVal, float maxVal) {
		Vector3 random = default;
		random.Random(minVal, maxVal);
		return random;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float VectorNormalize(ref Vector3 vec) {
		float radius = MathF.Sqrt(vec.X * vec.X + vec.Y * vec.Y + vec.Z * vec.Z);

		// FLT_EPSILON is added to the radius to eliminate the possibility of divide by zero.
		float iradius = 1.0f / (radius + FLT_EPSILON);

		vec.X *= iradius;
		vec.Y *= iradius;
		vec.Z *= iradius;

		return radius;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float VectorNormalize(ref Vector2 fwd) {
		float len = fwd.Length();
		if (len != 0)
			fwd = Vector2.Normalize(fwd);
		return len;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CrossProduct(in Vector3 a, in Vector3 b, out Vector3 result) {
		result = Vector3.Cross(a, b);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorVectors(in Vector3 forward, out Vector3 right, out Vector3 up) {
		right = default;
		up = default;
		if (forward[0] == 0 && forward[1] == 0) {
			right[0] = 0;
			right[1] = -1;
			right[2] = 0;
			up[0] = -forward[2];
			up[1] = 0;
			up[2] = 0;
		}
		else {
			Vector3 tmp = default;
			tmp[0] = 0; tmp[1] = 0; tmp[2] = 1;
			CrossProduct(forward, tmp, out right);
			VectorNormalize(ref right);
			CrossProduct(right, forward, out up);
			VectorNormalize(ref up);
		}
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void MatrixGetColumn(in Matrix3x4 inMatrix, int column, out Vector3 outVec) {
		outVec.X = inMatrix[0][column];
		outVec.Y = inMatrix[1][column];
		outVec.Z = inMatrix[2][column];
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void PositionMatrix(in Vector3 position, ref Matrix3x4 mat) {
		MatrixSetColumn(in position, 3, ref mat);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void MatrixPosition(in Matrix3x4 matrix, out Vector3 origin) {
		MatrixGetColumn(matrix, 3, out origin);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorTransform(in Vector3 in1, in Matrix3x4 in2, out Vector3 vecOut) {

		vecOut = VectorTransform(in1, in2);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static Vector3 VectorTransform(in Vector3 in1, in Matrix3x4 in2) {
		Vector3 translation = new(in2.M03, in2.M13, in2.M23);

		Vector3 dotResult = new(
			in1.X * in2.M00 + in1.Y * in2.M01 + in1.Z * in2.M02,
			in1.X * in2.M10 + in1.Y * in2.M11 + in1.Z * in2.M12,
			in1.X * in2.M20 + in1.Y * in2.M21 + in1.Z * in2.M22
		);

		return Vector3.Add(dotResult, translation);
	}

	public static void VectorITransform(in Vector3 in1, in Matrix3x4 in2, out Vector3 vecOut) {
		Vector3 in1t = new(in1.X - in2.M03, in1.Y - in2.M13, in1.Z - in2.M23);

		vecOut = new(
			in1t.X * in2.M00 + in1t.Y * in2.M10 + in1t.Z * in2.M20,
			in1t.X * in2.M01 + in1t.Y * in2.M11 + in1t.Z * in2.M21,
			in1t.X * in2.M02 + in1t.Y * in2.M12 + in1t.Z * in2.M22
		);
	}

	public static void TransformAABB(in Matrix3x4 transform, in Vector3 minsIn, in Vector3 maxsIn, out Vector3 minsOut, out Vector3 maxsOut) {
		Matrix3x4 mat = transform;

		Vector3 localCenter = (minsIn + maxsIn) * 0.5f;
		Vector3 localExtents = maxsIn - localCenter;

		VectorTransform(localCenter, mat, out Vector3 worldCenter);

		Vector3 worldExtents;
		worldExtents.X = DotProductAbs(localExtents, mat[0]);
		worldExtents.Y = DotProductAbs(localExtents, mat[1]);
		worldExtents.Z = DotProductAbs(localExtents, mat[2]);

		minsOut = worldCenter - worldExtents;
		maxsOut = worldCenter + worldExtents;
	}

	public static float VectorLength(in Vector3 delta) => delta.Length();
	public static float Vector2DLength(in Vector2 v) => MathF.Sqrt(DotProduct2D(v, v));

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector3 Cross(this Vector3 vec, Vector3 other) {
		CrossProduct(vec, other, out Vector3 res);
		return res;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static int Square(int x) => x * x;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float Square(float x) => x * x;

	public static void SinCos(float v, out float s, out float c) => (s, c) = float.SinCos(v);

	static readonly float[] power2_n = [
		1.152445441982634800E-041f, 2.304890883965269600E-041f, 4.609781767930539200E-041f, 9.219563535861078400E-041f,
		1.843912707172215700E-040f, 3.687825414344431300E-040f, 7.375650828688862700E-040f, 1.475130165737772500E-039f,
		2.950260331475545100E-039f, 5.900520662951090200E-039f, 1.180104132590218000E-038f, 2.360208265180436100E-038f,
		4.720416530360872100E-038f, 9.440833060721744200E-038f, 1.888166612144348800E-037f, 3.776333224288697700E-037f,
		7.552666448577395400E-037f, 1.510533289715479100E-036f, 3.021066579430958200E-036f, 6.042133158861916300E-036f,
		1.208426631772383300E-035f, 2.416853263544766500E-035f, 4.833706527089533100E-035f, 9.667413054179066100E-035f,
		1.933482610835813200E-034f, 3.866965221671626400E-034f, 7.733930443343252900E-034f, 1.546786088668650600E-033f,
		3.093572177337301200E-033f, 6.187144354674602300E-033f, 1.237428870934920500E-032f, 2.474857741869840900E-032f,
		4.949715483739681800E-032f, 9.899430967479363700E-032f, 1.979886193495872700E-031f, 3.959772386991745500E-031f,
		7.919544773983491000E-031f, 1.583908954796698200E-030f, 3.167817909593396400E-030f, 6.335635819186792800E-030f,
		1.267127163837358600E-029f, 2.534254327674717100E-029f, 5.068508655349434200E-029f, 1.013701731069886800E-028f,
		2.027403462139773700E-028f, 4.054806924279547400E-028f, 8.109613848559094700E-028f, 1.621922769711818900E-027f,
		3.243845539423637900E-027f, 6.487691078847275800E-027f, 1.297538215769455200E-026f, 2.595076431538910300E-026f,
		5.190152863077820600E-026f, 1.038030572615564100E-025f, 2.076061145231128300E-025f, 4.152122290462256500E-025f,
		8.304244580924513000E-025f, 1.660848916184902600E-024f, 3.321697832369805200E-024f, 6.643395664739610400E-024f,
		1.328679132947922100E-023f, 2.657358265895844200E-023f, 5.314716531791688300E-023f, 1.062943306358337700E-022f,
		2.125886612716675300E-022f, 4.251773225433350700E-022f, 8.503546450866701300E-022f, 1.700709290173340300E-021f,
		3.401418580346680500E-021f, 6.802837160693361100E-021f, 1.360567432138672200E-020f, 2.721134864277344400E-020f,
		5.442269728554688800E-020f, 1.088453945710937800E-019f, 2.176907891421875500E-019f, 4.353815782843751100E-019f,
		8.707631565687502200E-019f, 1.741526313137500400E-018f, 3.483052626275000900E-018f, 6.966105252550001700E-018f,
		1.393221050510000300E-017f, 2.786442101020000700E-017f, 5.572884202040001400E-017f, 1.114576840408000300E-016f,
		2.229153680816000600E-016f, 4.458307361632001100E-016f, 8.916614723264002200E-016f, 1.783322944652800400E-015f,
		3.566645889305600900E-015f, 7.133291778611201800E-015f, 1.426658355722240400E-014f, 2.853316711444480700E-014f,
		5.706633422888961400E-014f, 1.141326684577792300E-013f, 2.282653369155584600E-013f, 4.565306738311169100E-013f,
		9.130613476622338300E-013f, 1.826122695324467700E-012f, 3.652245390648935300E-012f, 7.304490781297870600E-012f,
		1.460898156259574100E-011f, 2.921796312519148200E-011f, 5.843592625038296500E-011f, 1.168718525007659300E-010f,
		2.337437050015318600E-010f, 4.674874100030637200E-010f, 9.349748200061274400E-010f, 1.869949640012254900E-009f,
		3.739899280024509800E-009f, 7.479798560049019500E-009f, 1.495959712009803900E-008f, 2.991919424019607800E-008f,
		5.983838848039215600E-008f, 1.196767769607843100E-007f, 2.393535539215686200E-007f, 4.787071078431372500E-007f,
		9.574142156862745000E-007f, 1.914828431372549000E-006f, 3.829656862745098000E-006f, 7.659313725490196000E-006f,
		1.531862745098039200E-005f, 3.063725490196078400E-005f, 6.127450980392156800E-005f, 1.225490196078431400E-004f,
		2.450980392156862700E-004f, 4.901960784313725400E-004f, 9.803921568627450800E-004f, 1.960784313725490200E-003f,
		3.921568627450980300E-003f, 7.843137254901960700E-003f, 1.568627450980392100E-002f, 3.137254901960784300E-002f,
		6.274509803921568500E-002f, 1.254901960784313700E-001f, 2.509803921568627400E-001f, 5.019607843137254800E-001f,
		1.003921568627451000E+000f, 2.007843137254901900E+000f, 4.015686274509803900E+000f, 8.031372549019607700E+000f,
		1.606274509803921500E+001f, 3.212549019607843100E+001f, 6.425098039215686200E+001f, 1.285019607843137200E+002f,
		2.570039215686274500E+002f, 5.140078431372548900E+002f, 1.028015686274509800E+003f, 2.056031372549019600E+003f,
		4.112062745098039200E+003f, 8.224125490196078300E+003f, 1.644825098039215700E+004f, 3.289650196078431300E+004f,
		6.579300392156862700E+004f, 1.315860078431372500E+005f, 2.631720156862745100E+005f, 5.263440313725490100E+005f,
		1.052688062745098000E+006f, 2.105376125490196000E+006f, 4.210752250980392100E+006f, 8.421504501960784200E+006f,
		1.684300900392156800E+007f, 3.368601800784313700E+007f, 6.737203601568627400E+007f, 1.347440720313725500E+008f,
		2.694881440627450900E+008f, 5.389762881254901900E+008f, 1.077952576250980400E+009f, 2.155905152501960800E+009f,
		4.311810305003921500E+009f, 8.623620610007843000E+009f, 1.724724122001568600E+010f, 3.449448244003137200E+010f,
		6.898896488006274400E+010f, 1.379779297601254900E+011f, 2.759558595202509800E+011f, 5.519117190405019500E+011f,
		1.103823438081003900E+012f, 2.207646876162007800E+012f, 4.415293752324015600E+012f, 8.830587504648031200E+012f,
		1.766117500929606200E+013f, 3.532235001859212500E+013f, 7.064470003718425000E+013f, 1.412894000743685000E+014f,
		2.825788001487370000E+014f, 5.651576002974740000E+014f, 1.130315200594948000E+015f, 2.260630401189896000E+015f,
		4.521260802379792000E+015f, 9.042521604759584000E+015f, 1.808504320951916800E+016f, 3.617008641903833600E+016f,
		7.234017283807667200E+016f, 1.446803456761533400E+017f, 2.893606913523066900E+017f, 5.787213827046133800E+017f,
		1.157442765409226800E+018f, 2.314885530818453500E+018f, 4.629771061636907000E+018f, 9.259542123273814000E+018f,
		1.851908424654762800E+019f, 3.703816849309525600E+019f, 7.407633698619051200E+019f, 1.481526739723810200E+020f,
		2.963053479447620500E+020f, 5.926106958895241000E+020f, 1.185221391779048200E+021f, 2.370442783558096400E+021f,
		4.740885567116192800E+021f, 9.481771134232385600E+021f, 1.896354226846477100E+022f, 3.792708453692954200E+022f,
		7.585416907385908400E+022f, 1.517083381477181700E+023f, 3.034166762954363400E+023f, 6.068333525908726800E+023f,
		1.213666705181745400E+024f, 2.427333410363490700E+024f, 4.854666820726981400E+024f, 9.709333641453962800E+024f,
		1.941866728290792600E+025f, 3.883733456581585100E+025f, 7.767466913163170200E+025f, 1.553493382632634000E+026f,
		3.106986765265268100E+026f, 6.213973530530536200E+026f, 1.242794706106107200E+027f, 2.485589412212214500E+027f,
		4.971178824424429000E+027f, 9.942357648848857900E+027f, 1.988471529769771600E+028f, 3.976943059539543200E+028f,
		7.953886119079086300E+028f, 1.590777223815817300E+029f, 3.181554447631634500E+029f, 6.363108895263269100E+029f,
		1.272621779052653800E+030f, 2.545243558105307600E+030f, 5.090487116210615300E+030f, 1.018097423242123100E+031f,
		2.036194846484246100E+031f, 4.072389692968492200E+031f, 8.144779385936984400E+031f, 1.628955877187396900E+032f,
		3.257911754374793800E+032f, 6.515823508749587500E+032f, 1.303164701749917500E+033f, 2.606329403499835000E+033f,
		5.212658806999670000E+033f, 1.042531761399934000E+034f, 2.085063522799868000E+034f, 4.170127045599736000E+034f,
		8.340254091199472000E+034f, 1.668050818239894400E+035f, 3.336101636479788800E+035f, 6.672203272959577600E+035f
	];

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float TexLightToLinear(int c, int exponent) {
		Assert(exponent >= -128 && exponent <= 127);
		return (float)c * power2_n[exponent + 128];
	}

	static int VectorToColorRGBExp32_CalcExponent(float pin) {
		if (pin == 0.0f)
			return 0;

		uint bits = BitConverter.SingleToUInt32Bits(pin);

		const uint biasedSeven = 7 + 127;

		int expComponent = (int)((bits & 0x7F800000) >> 23);
		expComponent -= (int)biasedSeven;
		return expComponent;
	}

	public static void VectorToColorRGBExp32(in Vector3 vin, out ColorRGBExp32 c) {
		Assert(MathlibInitialized);
		Assert(vin.X >= 0.0f && vin.Y >= 0.0f && vin.Z >= 0.0f);

		float max = MathF.Max(MathF.Max(vin.X, vin.Y), vin.Z);

		int exponent = VectorToColorRGBExp32_CalcExponent(max);

		Assert(exponent > sbyte.MinValue && exponent <= sbyte.MaxValue);

		uint bits = (uint)(127 - exponent) << 23;
		float scalar = BitConverter.UInt32BitsToSingle(bits);

		float r = vin.X * scalar;
		float g = vin.Y * scalar;
		float b = vin.Z * scalar;

		AssertMsg(r <= 255.0f, $"(R {r:F2}, G {g:F2}, B {b:F2}): R > 255.");
		AssertMsg(g <= 255.0f, $"(R {r:F2}, G {g:F2}, B {b:F2}): G > 255.");
		AssertMsg(b <= 255.0f, $"(R {r:F2}, G {g:F2}, B {b:F2}): B > 255.");

		c.R = (byte)(int)r;
		c.G = (byte)(int)g;
		c.B = (byte)(int)b;

		c.Exponent = (sbyte)exponent;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void VectorRotate(in Vector3 in1, in Matrix3x4 in2, out Vector3 outVec) {
		outVec.X = in1.X * in2.M00 + in1.Y * in2.M01 + in1.Z * in2.M02;
		outVec.Y = in1.X * in2.M10 + in1.Y * in2.M11 + in1.Z * in2.M12;
		outVec.Z = in1.X * in2.M20 + in1.Y * in2.M21 + in1.Z * in2.M22;
	}

	public static int PolyFromPlane(Span<Vector3> outVerts, in Vector3 normal, float dist, float halfScale = 9000.0f) {
		int i, x;
		float max, v;
		Vector3 org, vup;

		max = -16384; //MAX_COORD_INTEGER
		x = -1;
		for (i = 0; i < 3; i++) {
			v = MathF.Abs(normal[i]);
			if (v > max) {
				x = i;
				max = v;
			}
		}

		if (x == -1)
			return 0;

		vup = new(0, 0, 0);
		switch (x) {
			case 0:
			case 1:
				vup[2] = 1;
				break;
			case 2:
				vup[0] = 1;
				break;
		}

		v = Vector3.Dot(vup, normal);
		VectorMA(vup, -v, normal, out vup);
		VectorNormalize(ref vup);

		org = normal * dist;
		CrossProduct(vup, normal, out Vector3 vright);

		vup *= halfScale;
		vright *= halfScale;

		outVerts[0] = org - vright; // left
		outVerts[0] += vup;         // up

		outVerts[1] = org + vright; // right
		outVerts[1] += vup;         // up

		outVerts[2] = org + vright; // right
		outVerts[2] -= vup;         // down

		outVerts[3] = org - vright; // left
		outVerts[3] -= vup;         // down

		return 4;
	}

	public static void ComputeTrianglePlane(in Vector3 v1, in Vector3 v2, in Vector3 v3, out Vector3 normal, out float intercept) {
		VectorSubtract(v2, v1, out Vector3 e1);
		VectorSubtract(v3, v1, out Vector3 e2);
		CrossProduct(e1, e2, out normal);
		VectorNormalize(ref normal);
		intercept = DotProduct(normal, v1);
	}

	public static int ClipPolyToPlane(Span<Vector3> inVerts, int vertCount, Span<Vector3> outVerts, in Vector3 normal, float dist, float onPlaneEpsilon = 0.1f) {
		Span<float> dists = stackalloc float[vertCount * 4]; //4x vertcount should cover all cases
		Span<int> sides = stackalloc int[vertCount * 4];
		Span<int> counts = stackalloc int[3];
		float dot;
		int i, j;
		Vector3 mid = new(0, 0, 0);
		int outCount;

		counts[0] = counts[1] = counts[2] = 0;

		for (i = 0; i < vertCount; i++) {
			dot = Vector3.Dot(inVerts[i], normal) - dist;
			dists[i] = dot;
			if (dot > onPlaneEpsilon)
				sides[i] = VPlane.SIDE_FRONT;
			else if (dot < -onPlaneEpsilon)
				sides[i] = VPlane.SIDE_BACK;
			else
				sides[i] = VPlane.SIDE_ON;
			counts[sides[i]]++;
		}
		sides[i] = sides[0];
		dists[i] = dists[0];

		if (counts[0] == 0)
			return 0;

		if (counts[1] == 0) {
			for (i = 0; i < vertCount; i++)
				outVerts[i] = inVerts[i];
			return vertCount;
		}

		outCount = 0;
		for (i = 0; i < vertCount; i++) {
			Vector3 p1 = inVerts[i];

			if (sides[i] == VPlane.SIDE_ON) {
				outVerts[outCount] = p1;
				outCount++;
				continue;
			}

			if (sides[i] == VPlane.SIDE_FRONT) {
				outVerts[outCount] = p1;
				outCount++;
			}

			if (sides[i + 1] == VPlane.SIDE_ON || sides[i + 1] == sides[i])
				continue;

			Vector3 p2 = inVerts[(i + 1) % vertCount];

			dot = dists[i] / (dists[i] - dists[i + 1]);
			for (j = 0; j < 3; j++) {
				if (normal[j] == 1)
					mid[j] = dist;
				else if (normal[j] == -1)
					mid[j] = -dist;
				else
					mid[j] = p1[j] + dot * (p2[j] - p1[j]);
			}

			outVerts[outCount] = mid;
			outCount++;
		}

		return outCount;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool VectorCompare(in Vector3 v1, in Vector3 v2) {
		return v1 == v2;
	}



	public static void VectorIRotate(ReadOnlySpan<float> in1, in Matrix3x4 in2, Span<float> @out) {
		@out[0] = in1[0] * in2[0][0] + in1[1] * in2[1][0] + in1[2] * in2[2][0];
		@out[1] = in1[0] * in2[0][1] + in1[1] * in2[1][1] + in1[2] * in2[2][1];
		@out[2] = in1[0] * in2[0][2] + in1[1] * in2[1][2] + in1[2] * in2[2][2];
	}

	public static unsafe void VectorIRotate(in Vector3 relVelocity, in Matrix3x4 in2, out Vector3 @out) {
		fixed (Vector3* pin1 = &relVelocity)
		fixed (Vector3* pout = &@out) {
			VectorIRotate(new ReadOnlySpan<float>(pin1, 3), in in2, new Span<float>(pout, 3));
		}
	}

	public static unsafe void VectorYawRotate(in Vector3 @in, float yaw, out Vector3 @out) {
		fixed (Vector3* inAddr = &@in)
		fixed (Vector3* outAddr = &@out) {
			if (inAddr == outAddr) {
				Vector3 tmp;
				tmp = @in;
				VectorYawRotate(tmp, yaw, out @out);
				return;
			}
		}

		float sy, cy;

		SinCos(DEG2RAD(yaw), out sy, out cy);

		@out.X = @in.X * cy - @in.Y * sy;
		@out.Y = @in.X * sy + @in.Y * cy;
		@out.Z = @in.Z;
	}

	public static void QuaternionAngles(in Quaternion q, out QAngle angles) {
		QuaternionMatrix(q, out Matrix3x4 matrix);
		MatrixAngles(matrix, out angles);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> SubSIMD(Vector128<float> a, Vector128<float> b) {
		if (Sse.IsSupported)
			return Sse.Subtract(a, b);
		if (AdvSimd.IsSupported)
			return AdvSimd.Subtract(a, b);
		return Vector128.Subtract(a, b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> AddSIMD(Vector128<float> a, Vector128<float> b) {
		if (Sse.IsSupported)
			return Sse.Add(a, b);
		if (AdvSimd.IsSupported)
			return AdvSimd.Add(a, b);
		return Vector128.Add(a, b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> MulSIMD(Vector128<float> a, Vector128<float> b) {
		if (Sse.IsSupported)
			return Sse.Multiply(a, b);
		if (AdvSimd.IsSupported)
			return AdvSimd.Multiply(a, b);
		return Vector128.Multiply(a, b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> MaddSIMD(Vector128<float> a, Vector128<float> b, Vector128<float> c) => AddSIMD(MulSIMD(a, b), c);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static int TestSignSIMD(in Vector128<float> a) => (int)Vector128.ExtractMostSignificantBits(a);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> AndSIMD(Vector128<float> a, Vector128<float> b) {
		if (Sse.IsSupported)
			return Sse.And(a, b);
		if (AdvSimd.IsSupported)
			return AdvSimd.And(a.AsUInt32(), b.AsUInt32()).AsSingle();
		return Vector128.BitwiseAnd(a, b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> OrSIMD(Vector128<float> a, Vector128<float> b) {
		if (Sse.IsSupported)
			return Sse.Or(a, b);
		if (AdvSimd.IsSupported)
			return AdvSimd.Or(a.AsUInt32(), b.AsUInt32()).AsSingle();
		return Vector128.BitwiseOr(a, b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> XorSIMD(Vector128<float> a, Vector128<float> b) {
		if (Sse.IsSupported)
			return Sse.Xor(a, b);
		if (AdvSimd.IsSupported)
			return AdvSimd.Xor(a.AsUInt32(), b.AsUInt32()).AsSingle();
		return Vector128.Xor(a, b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> MinSIMD(Vector128<float> a, Vector128<float> b) {
		if (Sse.IsSupported)
			return Sse.Min(a, b);
		if (AdvSimd.IsSupported)
			return AdvSimd.Min(a, b);
		return Vector128.Min(a, b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> MaxSIMD(Vector128<float> a, Vector128<float> b) {
		if (Sse.IsSupported)
			return Sse.Max(a, b);
		if (AdvSimd.IsSupported)
			return AdvSimd.Max(a, b);
		return Vector128.Max(a, b);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> ReplicateX4(float value) => Vector128.Create(value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> SetWToZeroSIMD(Vector128<float> v) {
		if (Sse.IsSupported)
			return Sse.And(v, Vector128.Create(~0u, ~0u, ~0u, 0u).AsSingle());

		if (AdvSimd.IsSupported)
			return AdvSimd.And(
				v.AsUInt32(),
				Vector128.Create(~0u, ~0u, ~0u, 0u)
			).AsSingle();

		return Vector128.AndNot(Vector128.Create(0u, 0u, 0u, ~0u).AsSingle(), v);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAnyNegative(Vector128<float> v) {
		if (Sse.IsSupported)
			return Sse.MoveMask(v) != 0;
		if (AdvSimd.Arm64.IsSupported) {
			Vector128<uint> signs = AdvSimd.ShiftRightLogical(v.AsUInt32(), 31);
			return AdvSimd.Arm64.MaxAcross(signs).ToScalar() != 0;
		}
		return (v.GetElement(0) < 0) | (v.GetElement(1) < 0) |
				 (v.GetElement(2) < 0) | (v.GetElement(3) < 0);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsAllZeros(Vector128<float> v) {
		if (Sse.IsSupported)
			return Sse.MoveMask(v) == 0;
		if (AdvSimd.Arm64.IsSupported) {
			Vector128<uint> signs = AdvSimd.ShiftRightLogical(v.AsUInt32(), 31);
			return AdvSimd.Arm64.MaxAcross(signs).ToScalar() == 0;
		}
		return v.AsUInt32().GetElement(0) == 0 && v.AsUInt32().GetElement(1) == 0 &&
				 v.AsUInt32().GetElement(2) == 0 && v.AsUInt32().GetElement(3) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> MaskedAssign(Vector128<float> mask, Vector128<float> yes, Vector128<float> no) {
		if (Sse41.IsSupported)
			return Sse41.BlendVariable(no, yes, mask);
		if (Sse.IsSupported)
			return Sse.Or(Sse.And(mask, yes), Sse.AndNot(mask, no));
		if (AdvSimd.IsSupported)
			return AdvSimd.BitwiseSelect(mask.AsUInt32(), yes.AsUInt32(), no.AsUInt32()).AsSingle();

		return Vector128.ConditionalSelect(mask.AsInt32(), yes.AsInt32(), no.AsInt32()).AsSingle();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> FindLowestSIMD3(Vector128<float> v) {
		if (Sse.IsSupported) {
			Vector128<float> y = Sse.Shuffle(v, v, 0x55);
			Vector128<float> z = Sse.Shuffle(v, v, 0xAA);
			Vector128<float> xy = Sse.Min(v, y);
			return Sse.Min(xy, z);
		}
		if (AdvSimd.Arm64.IsSupported) {
			float x = v.GetElement(0);
			float yf = v.GetElement(1);
			float zf = v.GetElement(2);
			float m = MathF.Min(MathF.Min(x, yf), zf);
			return Vector128.Create(m);
		}

		{
			float m = MathF.Min(MathF.Min(v.GetElement(0), v.GetElement(1)), v.GetElement(2));
			return Vector128.Create(m);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> FindHighestSIMD3(Vector128<float> v) {
		if (Sse.IsSupported) {
			Vector128<float> y = Sse.Shuffle(v, v, 0x55);
			Vector128<float> z = Sse.Shuffle(v, v, 0xAA);
			Vector128<float> xy = Sse.Max(v, y);
			return Sse.Max(xy, z);
		}
		if (AdvSimd.Arm64.IsSupported) {
			float x = v.GetElement(0);
			float yf = v.GetElement(1);
			float zf = v.GetElement(2);
			float m = MathF.Max(MathF.Max(x, yf), zf);
			return Vector128.Create(m);
		}

		{
			float m = MathF.Max(MathF.Max(v.GetElement(0), v.GetElement(1)), v.GetElement(2));
			return Vector128.Create(m);
		}
	}

	public const float VALVE_RAND_MAX = 0x7fff;
	public static vec_t rand(vec_t minVal, vec_t maxVal) {
		return vec_t.Lerp(System.Random.Shared.Next(), minVal, maxVal);
	}
	public static vec_t rand() {
		return System.Random.Shared.Next((int)VALVE_RAND_MAX + 1);
	}
	public static void Random(ref this Vector3 v, vec_t minVal, vec_t maxVal) {
		fltx4 rn = Vector128.Create(rand() / VALVE_RAND_MAX, rand() / VALVE_RAND_MAX, rand() / VALVE_RAND_MAX, 0);
		fltx4 mn = Vector128.Create(minVal);

		StoreFloat3(ref v, Vector128.FusedMultiplyAdd(rn, Vector128.Subtract(Vector128.Create(maxVal), mn), mn));
	}

	public static void StoreFloat3(ref Vector3 v, fltx4 x4) {
		v = x4.AsVector3();
	}

	static void SetupMatrixAnglesInternal(ref this Matrix4x4 m, in QAngle angles) {
		fltx4 sine, cosine;
		fltx4 radians = Vector128.Multiply(LoadFloat3(MemoryMarshal.Cast<QAngle, float>(new(in angles))), Vector128.Create(MathF.PI / 180f));
		(sine, cosine) = Vector128.SinCos(radians);

		float sp = sine[0], sy = sine[1], sr = sine[2];
		float cp = cosine[0], cy = cosine[1], cr = cosine[2];

		// matrix = (YAW * PITCH) * ROLL
		m[0, 0] = cp * cy;
		m[1, 0] = cp * sy;
		m[2, 0] = -sp;
		m[0, 1] = sr * sp * cy + cr * -sy;
		m[1, 1] = sr * sp * sy + cr * cy;
		m[2, 1] = sr * cp;
		m[0, 2] = (cr * sp * cy + -sr * -sy);
		m[1, 2] = (cr * sp * sy + -sr * cy);
		m[2, 2] = cr * cp;
		m[0, 3] = 0f;
		m[1, 3] = 0f;
		m[2, 3] = 0f;
	}

	public static void SetupMatrixOrgAngles(ref this Matrix4x4 m, in Vector3 origin, in QAngle angles) {
		SetupMatrixAnglesInternal(ref m, angles);

		// Add translation
		m[0, 3] = origin.X;
		m[1, 3] = origin.Y;
		m[2, 3] = origin.Z;
		m[3, 0] = 0.0f;
		m[3, 1] = 0.0f;
		m[3, 2] = 0.0f;
		m[3, 3] = 1.0f;
	}

	public const uint PERMUTE_0X = 0;
	public const uint PERMUTE_0Y = 1;
	public const uint PERMUTE_0Z = 2;
	public const uint PERMUTE_0W = 3;
	public const uint PERMUTE_1X = 4;
	public const uint PERMUTE_1Y = 5;
	public const uint PERMUTE_1Z = 6;
	public const uint PERMUTE_1W = 7;

	public const uint AXIS_X = 0;
	public const uint AXIS_Y = 1;
	public const uint AXIS_Z = 2;
	public const uint AXIS_W = 3;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> LoadFloat2(in Vector2 v2) => Vector128.Create(v2.X, v2.Y, 0, 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> LoadFloat3(in Vector3 v3) => Vector128.Create(v3.X, v3.Y, v3.Z, 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> LoadFloat4(in Vector4 v4) => Vector128.Create(v4.X, v4.Y, v4.Z, v4.W);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<int> LoadInt2(in Vector2 v2) => Vector128.Create((int)v2.X, (int)v2.Y, 0, 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<int> LoadInt3(in Vector3 v3) => Vector128.Create((int)v3.X, (int)v3.Y, (int)v3.Z, 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<int> LoadInt4(in Vector4 v4) => Vector128.Create((int)v4.X, (int)v4.Y, (int)v4.Z, (int)v4.W);

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> LoadFloat2(ReadOnlySpan<float> a) => Vector128.Create(a[0], a[1], 0, 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> LoadFloat3(ReadOnlySpan<float> a) => Vector128.Create(a[0], a[1], a[2], 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> LoadFloat4(ReadOnlySpan<float> a) => Vector128.Create(a[0], a[1], a[2], a[3]);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<int> LoadInt2(ReadOnlySpan<int> a) => Vector128.Create((int)a[0], (int)a[1], 0, 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<int> LoadInt3(ReadOnlySpan<int> a) => Vector128.Create((int)a[0], (int)a[1], (int)a[2], 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<int> LoadInt4(ReadOnlySpan<int> a) => Vector128.Create((int)a[0], (int)a[1], (int)a[2], (int)a[3]);

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> LoadFloat2(Span<float> a) => Vector128.Create(a[0], a[1], 0, 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> LoadFloat3(Span<float> a) => Vector128.Create(a[0], a[1], a[2], 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> LoadFloat4(Span<float> a) => Vector128.Create(a[0], a[1], a[2], a[3]);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<int> LoadInt2(Span<int> a) => Vector128.Create((int)a[0], (int)a[1], 0, 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<int> LoadInt3(Span<int> a) => Vector128.Create((int)a[0], (int)a[1], (int)a[2], 0);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<int> LoadInt4(Span<int> a) => Vector128.Create((int)a[0], (int)a[1], (int)a[2], (int)a[3]);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static fltx4 SplatXSIMD(fltx4 a) {
		var v = a[(int)AXIS_X];
		return Vector128.Create(v, v, v, v);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static fltx4 SplatYSIMD(fltx4 a) {
		var v = a[(int)AXIS_Y];
		return Vector128.Create(v, v, v, v);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static fltx4 SplatZSIMD(fltx4 a) {
		var v = a[(int)AXIS_Z];
		return Vector128.Create(v, v, v, v);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static fltx4 SplatWSIMD(fltx4 a) {
		var v = a[(int)AXIS_W];
		return Vector128.Create(v, v, v, v);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> RotateLeft(fltx4 a) {
		Vector128<int> control = Vector128.Create((int)AXIS_Y, (int)AXIS_Z, (int)AXIS_W, (int)AXIS_X);
		return Vector128.Shuffle(a, control);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> RotateLeft2(fltx4 a) {
		Vector128<int> control = Vector128.Create((int)AXIS_Z, (int)AXIS_W, (int)AXIS_X, (int)AXIS_Y);
		return Vector128.Shuffle(a, control);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> RotateRight(fltx4 a) {
		Vector128<int> control = Vector128.Create((int)AXIS_W, (int)AXIS_X, (int)AXIS_Y, (int)AXIS_Z);
		return Vector128.Shuffle(a, control);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector128<float> RotateRight2(fltx4 a) {
		Vector128<int> control = Vector128.Create((int)AXIS_Z, (int)AXIS_W, (int)AXIS_X, (int)AXIS_Y);
		return Vector128.Shuffle(a, control);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> CmpGtSIMD(fltx4 a, fltx4 b) => Vector128.GreaterThan(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> CmpGeSIMD(fltx4 a, fltx4 b) => Vector128.GreaterThanOrEqual(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> CmpLtSIMD(fltx4 a, fltx4 b) => Vector128.LessThan(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static Vector128<float> CmpLeSIMD(fltx4 a, fltx4 b) => Vector128.LessThanOrEqual(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsAllGreaterThan(fltx4 a, fltx4 b) => Vector128.GreaterThanAll(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsAllGreaterThanOrEq(fltx4 a, fltx4 b) => Vector128.GreaterThanOrEqualAll(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool IsAllEqual(fltx4 a, fltx4 b) => Vector128.EqualsAll(a, b);
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float AngleNormalize(float angle) {
		angle = Fmodf(angle, 360.0f);
		if (angle > 180)
			angle -= 360;
		if (angle < -180)
			angle += 360;

		return angle;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static float anglemod(float a) => (360f / 65536) * ((int)(a * (65536f / 360.0f)) & 65535);

	public static float ApproachAngle(float target, float value, float speed) {
		target = anglemod(target);
		value = anglemod(value);

		float delta = target - value;

		// Speed is assumed to be positive
		if (speed < 0)
			speed = -speed;

		if (delta < -180)
			delta += 360;
		else if (delta > 180)
			delta -= 360;

		if (delta > speed)
			value += speed;
		else if (delta < -speed)
			value -= speed;
		else
			value = target;

		return value;
	}

	public static void VectorAngles(in Vector3 forward, out QAngle angles) {
		float tmp, yaw, pitch;

		if (forward[1] == 0 && forward[0] == 0) {
			yaw = 0;
			if (forward[2] > 0)
				pitch = 270;
			else
				pitch = 90;
		}
		else {
			yaw = (float)(Math.Atan2(forward[1], forward[0]) * (180 / Math.PI));
			if (yaw < 0)
				yaw += 360;

			tmp = MathF.Sqrt(forward[0] * forward[0] + forward[1] * forward[1]);
			pitch = (float)(Math.Atan2(-forward[2], tmp) * (180 / Math.PI));
			if (pitch < 0)
				pitch += 360;
		}

		angles = new(pitch, yaw, 0);
	}

	/// <summary>
	/// Forward direction vector with a reference up vector -> Euler angles
	/// </summary>
	public static void VectorAngles(in Vector3 forward, in Vector3 pseudoup, out QAngle angles) {
		angles = default;

		CrossProduct(pseudoup, forward, out Vector3 left);
		VectorNormalize(ref left);

		float xyDist = MathF.Sqrt(forward[0] * forward[0] + forward[1] * forward[1]);

		// enough here to get angles?
		if (xyDist > 0.001f) {
			// (yaw)	y = ATAN( forward.y, forward.x );		-- in our space, forward is the X axis
			angles[1] = RAD2DEG(MathF.Atan2(forward[1], forward[0]));

			// The engine does pitch inverted from this, but we always end up negating it in the DLL
			// UNDONE: Fix the engine to make it consistent
			// (pitch)	x = ATAN( -forward.z, sqrt(forward.x*forward.x+forward.y*forward.y) );
			angles[0] = RAD2DEG(MathF.Atan2(-forward[2], xyDist));

			float up_z = (left[1] * forward[0]) - (left[0] * forward[1]);

			// (roll)	z = ATAN( left.z, up.z );
			angles[2] = RAD2DEG(MathF.Atan2(left[2], up_z));
		}
		else    // forward is mostly Z, gimbal lock-
		{
			// (yaw)	y = ATAN( -left.x, left.y );			-- forward is mostly z, so use right for yaw
			angles[1] = RAD2DEG(MathF.Atan2(-left[0], left[1])); //This was originally copied from the "void MatrixAngles( const matrix3x4_t& matrix, float *angles )" code, and it's 180 degrees off, negated the values and it all works now (Dave Kircher)

			// The engine does pitch inverted from this, but we always end up negating it in the DLL
			// UNDONE: Fix the engine to make it consistent
			// (pitch)	x = ATAN( -forward.z, sqrt(forward.x*forward.x+forward.y*forward.y) );
			angles[0] = RAD2DEG(MathF.Atan2(-forward[2], xyDist));

			// Assume no roll in this case as one degree of freedom has been lost (i.e. yaw == roll)
			angles[2] = 0;
		}
	}

	public static void AxisAngleQuaternion(in Vector3 axis, float angle, out Quaternion q) {
		SinCos(DEG2RAD(angle) * 0.5f, out float sa, out float ca);
		q.X = axis.X * sa;
		q.Y = axis.Y * sa;
		q.Z = axis.Z * sa;
		q.W = ca;
	}

	public static void Vector3DMultiplyPosition(in Matrix4x4 src1, in Vector3 src2, out Vector3 dst) {
		dst.X = src1[0][0] * src2.X + src1[0][1] * src2.Y + src1[0][2] * src2.Z + src1[0][3];
		dst.Y = src1[1][0] * src2.X + src1[1][1] * src2.Y + src1[1][2] * src2.Z + src1[1][3];
		dst.Z = src1[2][0] * src2.X + src1[2][1] * src2.Y + src1[2][2] * src2.Z + src1[2][3];
	}

	public static void Vector3DMultiply(in Matrix4x4 src1, in Vector3 src2, out Vector3 dst) {
		dst.X = src1[0][0] * src2.X + src1[0][1] * src2.Y + src1[0][2] * src2.Z;
		dst.Y = src1[1][0] * src2.X + src1[1][1] * src2.Y + src1[1][2] * src2.Z;
		dst.Z = src1[2][0] * src2.X + src1[2][1] * src2.Y + src1[2][2] * src2.Z;
	}

	public static void ClearBounds(out Vector3 mins, out Vector3 maxs) {
		mins = new(99999, 99999, 99999);
		maxs = new(-99999, -99999, -99999);
	}

	public static void AddPointToBounds(in Vector3 v, ref Vector3 mins, ref Vector3 maxs) {
		vec_t val = v.X;
		if (val < mins.X)
			mins.X = val;
		if (val > maxs.X)
			maxs.X = val;

		val = v.Y;
		if (val < mins.Y)
			mins.Y = val;
		if (val > maxs.Y)
			maxs.Y = val;

		val = v.Z;
		if (val < mins.Z)
			mins.Z = val;
		if (val > maxs.Z)
			maxs.Z = val;
	}

	public static void Vector3DMultiplyPositionProjective(in Matrix4x4 src1, in Vector3 src2, out Vector3 dst) {
		float w = src1[3][0] * src2.X + src1[3][1] * src2.Y + src1[3][2] * src2.Z + src1[3][3];
		if (w != 0.0f)
			w = 1.0f / w;

		dst.X = (src1[0][0] * src2.X + src1[0][1] * src2.Y + src1[0][2] * src2.Z + src1[0][3]) * w;
		dst.Y = (src1[1][0] * src2.X + src1[1][1] * src2.Y + src1[1][2] * src2.Z + src1[1][3]) * w;
		dst.Z = (src1[2][0] * src2.X + src1[2][1] * src2.Y + src1[2][2] * src2.Z + src1[2][3]) * w;
	}

	public static void MatrixSetColumn(ref Matrix4x4 src, int col, in Vector3 column) {
		Assert((col >= 0) && (col <= 3));

		src[0, col] = column.X;
		src[1, col] = column.Y;
		src[2, col] = column.Z;
	}

	public static Vector3 VMul4x3(ref this Matrix4x4 m, in Vector3 vec) {
		Vector3DMultiplyPosition(ref m, vec, out Vector3 result);
		return result;
	}

	public static Matrix4x4 Transpose3x3(ref this Matrix4x4 m, in Vector3 vec) {
		return new Matrix4x4(
			m[0][0], m[1][0], m[2][0], m[0][3],
			m[0][1], m[1][1], m[2][1], m[1][3],
			m[0][2], m[1][2], m[2][2], m[2][3],
			m[3][0], m[3][1], m[3][2], m[3][3]
		);
	}

	public static Matrix4x4 Transpose(ref this Matrix4x4 m, in Vector3 vec) {
		return new Matrix4x4(
			m[0][0], m[1][0], m[2][0], m[3][0],
			m[0][1], m[1][1], m[2][1], m[3][1],
			m[0][2], m[1][2], m[2][2], m[3][2],
			m[0][3], m[1][3], m[2][3], m[3][3]
		);
	}

	public static Vector3 VMul4x3Transpose(ref this Matrix4x4 m, in Vector3 vec) {
		Vector3 tmp = vec;
		tmp.X -= m[0][3];
		tmp.Y -= m[1][3];
		tmp.Z -= m[2][3];

		return new Vector3(
			m[0][0] * tmp.X + m[1][0] * tmp.Y + m[2][0] * tmp.Z,
			m[0][1] * tmp.X + m[1][1] * tmp.Y + m[2][1] * tmp.Z,
			m[0][2] * tmp.X + m[1][2] * tmp.Y + m[2][2] * tmp.Z
		);
	}

	public static Vector3 VMul3x3(ref this Matrix4x4 m, in Vector3 vec) {
		return new Vector3(
			m[0][0] * vec.X + m[0][1] * vec.Y + m[0][2] * vec.Z,
			m[1][0] * vec.X + m[1][1] * vec.Y + m[1][2] * vec.Z,
			m[2][0] * vec.X + m[2][1] * vec.Y + m[2][2] * vec.Z
		);
	}

	public static Vector3 VMul3x3Transpose(ref this Matrix4x4 m, in Vector3 vec) {
		return new Vector3(
			m[0][0] * vec.X + m[1][0] * vec.Y + m[2][0] * vec.Z,
			m[0][1] * vec.X + m[1][1] * vec.Y + m[2][1] * vec.Z,
			m[0][2] * vec.X + m[1][2] * vec.Y + m[2][2] * vec.Z
		);
	}

	public static void MatrixMul(ref this Matrix4x4 m, in Matrix4x4 vm, out Matrix4x4 @out) {
		MatrixMultiply(in m, in vm, out @out);
	}

	public static void SetForward(ref this Matrix4x4 m, in Vector3 forward) {
		m[0, 0] = forward.X;
		m[1, 0] = forward.Y;
		m[2, 0] = forward.Z;
	}

	public static void SetLeft(ref this Matrix4x4 m, in Vector3 left) {
		m[0, 1] = left.X;
		m[1, 1] = left.Y;
		m[2, 1] = left.Z;
	}

	public static void SetUp(ref this Matrix4x4 m, in Vector3 up) {
		m[0, 2] = up.X;
		m[1, 2] = up.Y;
		m[2, 2] = up.Z;
	}

	public static void GetBasisVectors(this in Matrix4x4 m, out Vector3 forward, out Vector3 left, out Vector3 up) {
		forward = new(m[0, 0], m[1, 0], m[2, 0]);
		left = new(m[0, 1], m[1, 1], m[2, 1]);
		up = new(m[0, 2], m[1, 2], m[2, 2]);
	}

	public static void SetBasisVectors(ref this Matrix4x4 m, in Vector3 forward, in Vector3 left, in Vector3 up) {
		m.SetForward(in forward);
		m.SetLeft(in left);
		m.SetUp(in up);
	}

	public static void SetTranslation(ref this Matrix4x4 m, in Vector3 trans) {
		m[0, 3] = trans.X;
		m[1, 3] = trans.Y;
		m[2, 3] = trans.Z;
	}

	public static void MatrixTranspose(in Matrix4x4 src, out Matrix4x4 dst) {
		dst = default;
		dst[0, 0] = src[0, 0]; dst[0, 1] = src[1, 0]; dst[0, 2] = src[2, 0]; dst[0, 3] = src[3, 0];
		dst[1, 0] = src[0, 1]; dst[1, 1] = src[1, 1]; dst[1, 2] = src[2, 1]; dst[1, 3] = src[3, 1];
		dst[2, 0] = src[0, 2]; dst[2, 1] = src[1, 2]; dst[2, 2] = src[2, 2]; dst[2, 3] = src[3, 2];
		dst[3, 0] = src[0, 3]; dst[3, 1] = src[1, 3]; dst[3, 2] = src[2, 3]; dst[3, 3] = src[3, 3];
	}

	public static ref Vector3 GetTranslation(this in Matrix4x4 m, ref Vector3 trans) {
		trans.X = m[0, 3];
		trans.Y = m[1, 3];
		trans.Z = m[2, 3];
		return ref trans;
	}

	public static void MatrixTransformPlane(in Matrix4x4 src, in CollisionPlane inPlane, out CollisionPlane outPlane) {
		Vector3 trans = default;
		outPlane = default;
		Vector3DMultiply(in src, in inPlane.Normal, out outPlane.Normal);
		outPlane.Dist = inPlane.Dist * DotProduct(outPlane.Normal, outPlane.Normal);
		outPlane.Dist += DotProduct(outPlane.Normal, src.GetTranslation(ref trans));
	}

	public static void MatrixInverseTR(in Matrix4x4 src, out Matrix4x4 dst) {
		dst = default;
		dst[0, 0] = src[0, 0]; dst[0, 1] = src[1, 0]; dst[0, 2] = src[2, 0];
		dst[1, 0] = src[0, 1]; dst[1, 1] = src[1, 1]; dst[1, 2] = src[2, 1];
		dst[2, 0] = src[0, 2]; dst[2, 1] = src[1, 2]; dst[2, 2] = src[2, 2];

		Vector3 trans = new(-src[0, 3], -src[1, 3], -src[2, 3]);
		Vector3DMultiply(in dst, in trans, out Vector3 newTrans);
		MatrixSetColumn(ref dst, 3, in newTrans);

		dst[3, 0] = dst[3, 1] = dst[3, 2] = 0.0f;
		dst[3, 3] = 1.0f;
	}

	public static byte FastFToC(float c) => (byte)(int)(c * 255.0f);

	public static float LinearToVertexLight(float f) {
		int i = RoundFloatToInt(f * 1024f);

		if ((uint)i > 4095) {
			if (i < 0)
				i = 0;
			else
				i = 4095;
		}

		return lineartovertex[i];
	}

	public static void MatrixToAngles(in Matrix4x4 src, out QAngle angles) {
		Span<float> forward = stackalloc float[3];
		Span<float> left = stackalloc float[3];
		Span<float> up = stackalloc float[3];

		// Extract the basis vectors from the matrix. Since we only need the Z
		// component of the up vector, we don't get X and Y.
		forward[0] = src[0][0];
		forward[1] = src[1][0];
		forward[2] = src[2][0];
		left[0] = src[0][1];
		left[1] = src[1][1];
		left[2] = src[2][1];
		up[2] = src[2][2];

		float xyDist = MathF.Sqrt(forward[0] * forward[0] + forward[1] * forward[1]);

		// enough here to get angles?
		if (xyDist > 0.001f) {
			// (yaw)	y = ATAN( forward.y, forward.x );		-- in our space, forward is the X axis
			angles.Y = RAD2DEG(MathF.Atan2(forward[1], forward[0]));

			// The engine does pitch inverted from this, but we always end up negating it in the DLL
			// UNDONE: Fix the engine to make it consistent
			// (pitch)	x = ATAN( -forward.z, sqrt(forward.x*forward.x+forward.y*forward.y) );
			angles.X = RAD2DEG(MathF.Atan2(-forward[2], xyDist));

			// (roll)	z = ATAN( left.z, up.z );
			angles.Z = RAD2DEG(MathF.Atan2(left[2], up[2]));
		}
		else    // forward is mostly Z, gimbal lock-
		{
			// (yaw)	y = ATAN( -left.x, left.y );			-- forward is mostly z, so use right for yaw
			angles.Y = RAD2DEG(MathF.Atan2(-left[0], left[1]));

			// The engine does pitch inverted from this, but we always end up negating it in the DLL
			// UNDONE: Fix the engine to make it consistent
			// (pitch)	x = ATAN( -forward.z, sqrt(forward.x*forward.x+forward.y*forward.y) );
			angles.X = RAD2DEG(MathF.Atan2(-forward[2], xyDist));

			// Assume no roll in this case as one degree of freedom has been lost (i.e. yaw == roll)
			angles.Z = 0;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void MatrixCopy(Matrix3x4 @in, out Matrix3x4 @out) {
		@out = @in;
	}

	public static void AngularImpulseToQAngle(in Vector3 angImpulse, out QAngle vecResult) {
		vecResult.X = angImpulse.X;
		vecResult.Y = angImpulse.Y;
		vecResult.Z = angImpulse.Z;
	}

	public static float CalcClosestPointToLineT(in Vector3 P, in Vector3 vLineA, in Vector3 vLineB, out Vector3 vDir) {
		Assert(MathlibInitialized);
		VectorSubtract(vLineB, vLineA, out vDir);

		// D dot [P - (A + D*t)] = 0
		// t = ( DP - DA) / DD
		float div = vDir.Dot(vDir);
		if (div < 0.00001f)
			return 0;
		else {
			return (vDir.Dot(P) - vDir.Dot(vLineA)) / div;
		}
	}

	public static void CalcClosestPointOnLineSegment(in Vector3 P, in Vector3 vLineA, in Vector3 vLineB, out Vector3 vClosest, out float outT) {
		float t = CalcClosestPointToLineT(P, vLineA, vLineB, out Vector3 vDir);
		t = Math.Clamp(t, 0, 1);
		outT = t;
		VectorMA(vLineA, t, vDir, out vClosest);
	}

	const int NUMVERTEXNORMALS = 162;
	public static readonly Vector3[] Anorms = [
		new(-0.525731f, 0.000000f, 0.850651f),
		new(-0.442863f, 0.238856f, 0.864188f),
		new(-0.295242f, 0.000000f, 0.955423f),
		new(-0.309017f, 0.500000f, 0.809017f),
		new(-0.162460f, 0.262866f, 0.951056f),
		new(0.000000f, 0.000000f, 1.000000f),
		new(0.000000f, 0.850651f, 0.525731f),
		new(-0.147621f, 0.716567f, 0.681718f),
		new(0.147621f, 0.716567f, 0.681718f),
		new(0.000000f, 0.525731f, 0.850651f),
		new(0.309017f, 0.500000f, 0.809017f),
		new(0.525731f, 0.000000f, 0.850651f),
		new(0.295242f, 0.000000f, 0.955423f),
		new(0.442863f, 0.238856f, 0.864188f),
		new(0.162460f, 0.262866f, 0.951056f),
		new(-0.681718f, 0.147621f, 0.716567f),
		new(-0.809017f, 0.309017f, 0.500000f),
		new(-0.587785f, 0.425325f, 0.688191f),
		new(-0.850651f, 0.525731f, 0.000000f),
		new(-0.864188f, 0.442863f, 0.238856f),
		new(-0.716567f, 0.681718f, 0.147621f),
		new(-0.688191f, 0.587785f, 0.425325f),
		new(-0.500000f, 0.809017f, 0.309017f),
		new(-0.238856f, 0.864188f, 0.442863f),
		new(-0.425325f, 0.688191f, 0.587785f),
		new(-0.716567f, 0.681718f, -0.147621f),
		new(-0.500000f, 0.809017f, -0.309017f),
		new(-0.525731f, 0.850651f, 0.000000f),
		new(0.000000f, 0.850651f, -0.525731f),
		new(-0.238856f, 0.864188f, -0.442863f),
		new(0.000000f, 0.955423f, -0.295242f),
		new(-0.262866f, 0.951056f, -0.162460f),
		new(0.000000f, 1.000000f, 0.000000f),
		new(0.000000f, 0.955423f, 0.295242f),
		new(-0.262866f, 0.951056f, 0.162460f),
		new(0.238856f, 0.864188f, 0.442863f),
		new(0.262866f, 0.951056f, 0.162460f),
		new(0.500000f, 0.809017f, 0.309017f),
		new(0.238856f, 0.864188f, -0.442863f),
		new(0.262866f, 0.951056f, -0.162460f),
		new(0.500000f, 0.809017f, -0.309017f),
		new(0.850651f, 0.525731f, 0.000000f),
		new(0.716567f, 0.681718f, 0.147621f),
		new(0.716567f, 0.681718f, -0.147621f),
		new(0.525731f, 0.850651f, 0.000000f),
		new(0.425325f, 0.688191f, 0.587785f),
		new(0.864188f, 0.442863f, 0.238856f),
		new(0.688191f, 0.587785f, 0.425325f),
		new(0.809017f, 0.309017f, 0.500000f),
		new(0.681718f, 0.147621f, 0.716567f),
		new(0.587785f, 0.425325f, 0.688191f),
		new(0.955423f, 0.295242f, 0.000000f),
		new(1.000000f, 0.000000f, 0.000000f),
		new(0.951056f, 0.162460f, 0.262866f),
		new(0.850651f, -0.525731f, 0.000000f),
		new(0.955423f, -0.295242f, 0.000000f),
		new(0.864188f, -0.442863f, 0.238856f),
		new(0.951056f, -0.162460f, 0.262866f),
		new(0.809017f, -0.309017f, 0.500000f),
		new(0.681718f, -0.147621f, 0.716567f),
		new(0.850651f, 0.000000f, 0.525731f),
		new(0.864188f, 0.442863f, -0.238856f),
		new(0.809017f, 0.309017f, -0.500000f),
		new(0.951056f, 0.162460f, -0.262866f),
		new(0.525731f, 0.000000f, -0.850651f),
		new(0.681718f, 0.147621f, -0.716567f),
		new(0.681718f, -0.147621f, -0.716567f),
		new(0.850651f, 0.000000f, -0.525731f),
		new(0.809017f, -0.309017f, -0.500000f),
		new(0.864188f, -0.442863f, -0.238856f),
		new(0.951056f, -0.162460f, -0.262866f),
		new(0.147621f, 0.716567f, -0.681718f),
		new(0.309017f, 0.500000f, -0.809017f),
		new(0.425325f, 0.688191f, -0.587785f),
		new(0.442863f, 0.238856f, -0.864188f),
		new(0.587785f, 0.425325f, -0.688191f),
		new(0.688191f, 0.587785f, -0.425325f),
		new(-0.147621f, 0.716567f, -0.681718f),
		new(-0.309017f, 0.500000f, -0.809017f),
		new(0.000000f, 0.525731f, -0.850651f),
		new(-0.525731f, 0.000000f, -0.850651f),
		new(-0.442863f, 0.238856f, -0.864188f),
		new(-0.295242f, 0.000000f, -0.955423f),
		new(-0.162460f, 0.262866f, -0.951056f),
		new(0.000000f, 0.000000f, -1.000000f),
		new(0.295242f, 0.000000f, -0.955423f),
		new(0.162460f, 0.262866f, -0.951056f),
		new(-0.442863f, -0.238856f, -0.864188f),
		new(-0.309017f, -0.500000f, -0.809017f),
		new(-0.162460f, -0.262866f, -0.951056f),
		new(0.000000f, -0.850651f, -0.525731f),
		new(-0.147621f, -0.716567f, -0.681718f),
		new(0.147621f, -0.716567f, -0.681718f),
		new(0.000000f, -0.525731f, -0.850651f),
		new(0.309017f, -0.500000f, -0.809017f),
		new(0.442863f, -0.238856f, -0.864188f),
		new(0.162460f, -0.262866f, -0.951056f),
		new(0.238856f, -0.864188f, -0.442863f),
		new(0.500000f, -0.809017f, -0.309017f),
		new(0.425325f, -0.688191f, -0.587785f),
		new(0.716567f, -0.681718f, -0.147621f),
		new(0.688191f, -0.587785f, -0.425325f),
		new(0.587785f, -0.425325f, -0.688191f),
		new(0.000000f, -0.955423f, -0.295242f),
		new(0.000000f, -1.000000f, 0.000000f),
		new(0.262866f, -0.951056f, -0.162460f),
		new(0.000000f, -0.850651f, 0.525731f),
		new(0.000000f, -0.955423f, 0.295242f),
		new(0.238856f, -0.864188f, 0.442863f),
		new(0.262866f, -0.951056f, 0.162460f),
		new(0.500000f, -0.809017f, 0.309017f),
		new(0.716567f, -0.681718f, 0.147621f),
		new(0.525731f, -0.850651f, 0.000000f),
		new(-0.238856f, -0.864188f, -0.442863f),
		new(-0.500000f, -0.809017f, -0.309017f),
		new(-0.262866f, -0.951056f, -0.162460f),
		new(-0.850651f, -0.525731f, 0.000000f),
		new(-0.716567f, -0.681718f, -0.147621f),
		new(-0.716567f, -0.681718f, 0.147621f),
		new(-0.525731f, -0.850651f, 0.000000f),
		new(-0.500000f, -0.809017f, 0.309017f),
		new(-0.238856f, -0.864188f, 0.442863f),
		new(-0.262866f, -0.951056f, 0.162460f),
		new(-0.864188f, -0.442863f, 0.238856f),
		new(-0.809017f, -0.309017f, 0.500000f),
		new(-0.688191f, -0.587785f, 0.425325f),
		new(-0.681718f, -0.147621f, 0.716567f),
		new(-0.442863f, -0.238856f, 0.864188f),
		new(-0.587785f, -0.425325f, 0.688191f),
		new(-0.309017f, -0.500000f, 0.809017f),
		new(-0.147621f, -0.716567f, 0.681718f),
		new(-0.425325f, -0.688191f, 0.587785f),
		new(-0.162460f, -0.262866f, 0.951056f),
		new(0.442863f, -0.238856f, 0.864188f),
		new(0.162460f, -0.262866f, 0.951056f),
		new(0.309017f, -0.500000f, 0.809017f),
		new(0.147621f, -0.716567f, 0.681718f),
		new(0.000000f, -0.525731f, 0.850651f),
		new(0.425325f, -0.688191f, 0.587785f),
		new(0.587785f, -0.425325f, 0.688191f),
		new(0.688191f, -0.587785f, 0.425325f),
		new(-0.955423f, 0.295242f, 0.000000f),
		new(-0.951056f, 0.162460f, 0.262866f),
		new(-1.000000f, 0.000000f, 0.000000f),
		new(-0.850651f, 0.000000f, 0.525731f),
		new(-0.955423f, -0.295242f, 0.000000f),
		new(-0.951056f, -0.162460f, 0.262866f),
		new(-0.864188f, 0.442863f, -0.238856f),
		new(-0.951056f, 0.162460f, -0.262866f),
		new(-0.809017f, 0.309017f, -0.500000f),
		new(-0.864188f, -0.442863f, -0.238856f),
		new(-0.951056f, -0.162460f, -0.262866f),
		new(-0.809017f, -0.309017f, -0.500000f),
		new(-0.681718f, 0.147621f, -0.716567f),
		new(-0.681718f, -0.147621f, -0.716567f),
		new(-0.850651f, 0.000000f, -0.525731f),
		new(-0.688191f, 0.587785f, -0.425325f),
		new(-0.587785f, 0.425325f, -0.688191f),
		new(-0.425325f, 0.688191f, -0.587785f),
		new(-0.425325f, -0.688191f, -0.587785f),
		new(-0.587785f, -0.425325f, -0.688191f),
		new(-0.688191f, -0.587785f, -0.425325f)
	];

	public static void Catmull_Rom_Spline(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		float tSqr = t * t * 0.5f;
		float tSqrSqr = t * tSqr;
		t *= 0.5f;

		output = default;

		Vector3 a, b, c, d;

		// matrix row 1
		VectorScale(p1, -tSqrSqr, out a);       // 0.5 t^3 * [ (-1*p1) + ( 3*p2) + (-3*p3) + p4 ]
		VectorScale(p2, tSqrSqr * 3, out b);
		VectorScale(p3, tSqrSqr * -3, out c);
		VectorScale(p4, tSqrSqr, out d);

		output += a;
		output += b;
		output += c;
		output += d;

		// matrix row 2
		VectorScale(p1, tSqr * 2, out a);       // 0.5 t^2 * [ ( 2*p1) + (-5*p2) + ( 4*p3) - p4 ]
		VectorScale(p2, tSqr * -5, out b);
		VectorScale(p3, tSqr * 4, out c);
		VectorScale(p4, -tSqr, out d);

		output += a;
		output += b;
		output += c;
		output += d;

		// matrix row 3
		VectorScale(p1, -t, out a);             // 0.5 t * [ (-1*p1) + p3 ]
		VectorScale(p3, t, out b);

		output += a;
		output += b;

		// matrix row 4
		output += p2;    // p2
	}

	public static float ExponentialDecay(float decayTo, float decayTime, float dt) => MathF.Exp(MathF.Log(decayTo) / decayTime * dt);

	public static void Spline_Normalize(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, out Vector3 p1n, out Vector3 p4n) {
		float dt = p3.X - p2.X;

		p1n = p1;
		p4n = p4;

		if (dt != 0.0) {
			if (p1.X != p2.X)
				MathLib.VectorLerp(p2, p1, dt / (p2.X - p1.X), out p1n);
			if (p4.X != p3.X)
				MathLib.VectorLerp(p3, p4, dt / (p4.X - p3.X), out p4n);
		}
	}

	public static void Catmull_Rom_Spline_Tangent(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		float tOne = 3 * t * t * 0.5f;
		float tTwo = 2 * t * 0.5f;
		float tThree = 0.5f;

		output = default;

		Vector3 a, b, c, d;

		MathLib.VectorScale(p1, -tOne, out a);
		MathLib.VectorScale(p2, tOne * 3, out b);
		MathLib.VectorScale(p3, tOne * -3, out c);
		MathLib.VectorScale(p4, tOne, out d);

		output += a;
		output += b;
		output += c;
		output += d;

		MathLib.VectorScale(p1, tTwo * 2, out a);
		MathLib.VectorScale(p2, tTwo * -5, out b);
		MathLib.VectorScale(p3, tTwo * 4, out c);
		MathLib.VectorScale(p4, -tTwo, out d);

		output += a;
		output += b;
		output += c;
		output += d;

		MathLib.VectorScale(p1, -tThree, out a);
		MathLib.VectorScale(p3, tThree, out b);

		output += a;
		output += b;
	}

	public static void Catmull_Rom_Spline_Integral(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		output = p2 * t
				- 0.25f * (p1 - p3) * t * t
				+ (1.0f / 6.0f) * (2.0f * p1 - 5.0f * p2 + 4.0f * p3 - p4) * t * t * t
				- 0.125f * (p1 - 3.0f * p2 + 3.0f * p3 - p4) * t * t * t * t;
	}

	public static void Catmull_Rom_Spline_Normalize(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		float dt = Vector3.Distance(p3, p2);

		MathLib.VectorSubtract(p1, p2, out Vector3 p1n);
		MathLib.VectorSubtract(p4, p3, out Vector3 p4n);

		MathLib.VectorNormalize(ref p1n);
		MathLib.VectorNormalize(ref p4n);

		MathLib.VectorMA(p2, dt, p1n, out p1n);
		MathLib.VectorMA(p3, dt, p4n, out p4n);

		MathLib.Catmull_Rom_Spline(p1n, p2, p3, p4n, t, out output);
	}

	public static void Catmull_Rom_Spline_Integral_Normalize(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		float dt = Vector3.Distance(p3, p2);

		MathLib.VectorSubtract(p1, p2, out Vector3 p1n);
		MathLib.VectorSubtract(p4, p3, out Vector3 p4n);

		MathLib.VectorNormalize(ref p1n);
		MathLib.VectorNormalize(ref p4n);

		MathLib.VectorMA(p2, dt, p1n, out p1n);
		MathLib.VectorMA(p3, dt, p4n, out p4n);

		Catmull_Rom_Spline_Integral(p1n, p2, p3, p4n, t, out output);
	}

	public static void Catmull_Rom_Spline_NormalizeX(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		Spline_Normalize(p1, p2, p3, p4, out Vector3 p1n, out Vector3 p4n);
		MathLib.Catmull_Rom_Spline(p1n, p2, p3, p4n, t, out output);
	}

	public static void Kochanek_Bartels_Spline(float tension, float bias, float continuity, in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		float ffa, ffb, ffc, ffd;

		ffa = (1.0f - tension) * (1.0f + continuity) * (1.0f + bias);
		ffb = (1.0f - tension) * (1.0f - continuity) * (1.0f - bias);
		ffc = (1.0f - tension) * (1.0f - continuity) * (1.0f + bias);
		ffd = (1.0f - tension) * (1.0f + continuity) * (1.0f - bias);

		float tSqr = t * t * 0.5f;
		float tSqrSqr = t * tSqr;
		t *= 0.5f;

		output = default;

		Vector3 a, b, c, d;

		MathLib.VectorScale(p1, tSqrSqr * -ffa, out a);
		MathLib.VectorScale(p2, tSqrSqr * (4.0f + ffa - ffb - ffc), out b);
		MathLib.VectorScale(p3, tSqrSqr * (-4.0f + ffb + ffc - ffd), out c);
		MathLib.VectorScale(p4, tSqrSqr * ffd, out d);

		output += a;
		output += b;
		output += c;
		output += d;

		MathLib.VectorScale(p1, tSqr * 2 * ffa, out a);
		MathLib.VectorScale(p2, tSqr * (-6 - 2 * ffa + 2 * ffb + ffc), out b);
		MathLib.VectorScale(p3, tSqr * (6 - 2 * ffb - ffc + ffd), out c);
		MathLib.VectorScale(p4, tSqr * -ffd, out d);

		output += a;
		output += b;
		output += c;
		output += d;

		MathLib.VectorScale(p1, t * -ffa, out a);
		MathLib.VectorScale(p2, t * (ffa - ffb), out b);
		MathLib.VectorScale(p3, t * ffb, out c);

		output += a;
		output += b;
		output += c;

		output += p2;
	}

	public static void Kochanek_Bartels_Spline_NormalizeX(float tension, float bias, float continuity, in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		Spline_Normalize(p1, p2, p3, p4, out Vector3 p1n, out Vector3 p4n);
		Kochanek_Bartels_Spline(tension, bias, continuity, p1n, p2, p3, p4n, t, out output);
	}

	public static void Cubic_Spline(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		float tSqr = t * t;
		float tSqrSqr = t * tSqr;

		output = default;

		Vector3 b, c;

		MathLib.VectorScale(p2, tSqrSqr * 2, out b);
		MathLib.VectorScale(p3, tSqrSqr * -2, out c);

		output += b;
		output += c;

		MathLib.VectorScale(p2, tSqr * -3, out b);
		MathLib.VectorScale(p3, tSqr * 3, out c);

		output += b;
		output += c;

		output += p2;
	}

	public static void Cubic_Spline_NormalizeX(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		Spline_Normalize(p1, p2, p3, p4, out Vector3 p1n, out Vector3 p4n);
		Cubic_Spline(p1n, p2, p3, p4n, t, out output);
	}

	public static void BSpline(in Vector3 p1, in Vector3 p2, in Vector3 p3, in Vector3 p4, float t, out Vector3 output) {
		float oneOver6 = 1.0f / 6.0f;

		float tSqr = t * t * oneOver6;
		float tSqrSqr = t * tSqr;
		t *= oneOver6;

		output = default;

		Vector3 a, b, c, d;

		MathLib.VectorScale(p1, -tSqrSqr, out a);
		MathLib.VectorScale(p2, tSqrSqr * 3.0f, out b);
		MathLib.VectorScale(p3, tSqrSqr * -3.0f, out c);
		MathLib.VectorScale(p4, tSqrSqr, out d);

		output += a;
		output += b;
		output += c;
		output += d;

		MathLib.VectorScale(p1, tSqr * 3.0f, out a);
		MathLib.VectorScale(p2, tSqr * -6.0f, out b);
		MathLib.VectorScale(p3, tSqr * 3.0f, out c);

		output += a;
		output += b;
		output += c;

		MathLib.VectorScale(p1, t * -3.0f, out a);
		MathLib.VectorScale(p3, t * 3.0f, out c);

		output += a;
		output += c;

		MathLib.VectorScale(p1, oneOver6, out a);
		MathLib.VectorScale(p2, 4.0f * oneOver6, out b);
		MathLib.VectorScale(p3, oneOver6, out c);

		output += a;
		output += b;
		output += c;
	}
}

[StructLayout(LayoutKind.Sequential, Pack = 16, Size = sizeof(float) * 4 * 3)]
public struct FourVectors
{
	public Vector4 x, y, z;

	public FourVectors(Vector3 v) {
		x = new Vector4(v.X);
		y = new Vector4(v.Y);
		z = new Vector4(v.Z);
	}

	// Broadcast a single vector into all four lanes.
	public void DuplicateVector(in Vector3 v) {
		x = new Vector4(v.X);
		y = new Vector4(v.Y);
		z = new Vector4(v.Z);
	}

	// Load four vectors into the lanes: x = (a.x, b.x, c.x, d.x), etc.
	public void LoadAndSwizzle(in Vector3 a, in Vector3 b, in Vector3 c, in Vector3 d) {
		x = new Vector4(a.X, b.X, c.X, d.X);
		y = new Vector4(a.Y, b.Y, c.Y, d.Y);
		z = new Vector4(a.Z, b.Z, c.Z, d.Z);
	}

	public static FourVectors operator -(FourVectors a, FourVectors b) => new() { x = a.x - b.x, y = a.y - b.y, z = a.z - b.z };
	public static FourVectors operator +(FourVectors a, FourVectors b) => new() { x = a.x + b.x, y = a.y + b.y, z = a.z + b.z };
	public static FourVectors operator *(FourVectors a, FourVectors b) => new() { x = a.x * b.x, y = a.y * b.y, z = a.z * b.z };

	public static FourVectors Min(in FourVectors a, in FourVectors b) => new() { x = Vector4.Min(a.x, b.x), y = Vector4.Min(a.y, b.y), z = Vector4.Min(a.z, b.z) };
	public static FourVectors Max(in FourVectors a, in FourVectors b) => new() { x = Vector4.Max(a.x, b.x), y = Vector4.Max(a.y, b.y), z = Vector4.Max(a.z, b.z) };

	public readonly Vector4 Dot(in FourVectors b) => x * b.x + y * b.y + z * b.z;

	public static FourVectors operator *(FourVectors a, Vector4 scale) => new() { x = a.x * scale, y = a.y * scale, z = a.z * scale };

	public void VectorNormalizeFast() {
		Vector4 magSq = x * x + y * y + z * z;
		Vector4 invMag = new(1.0f / MathF.Sqrt(magSq.X), 1.0f / MathF.Sqrt(magSq.Y), 1.0f / MathF.Sqrt(magSq.Z), 1.0f / MathF.Sqrt(magSq.W));
		x *= invMag; y *= invMag; z *= invMag;
	}

	// Build a 4-bit mask, one bit per lane, where a[lane] <= b[lane].
	private static int LeMask(in Vector4 a, in Vector4 b) {
		int mask = 0;
		if (a.X <= b.X) mask |= 1;
		if (a.Y <= b.Y) mask |= 2;
		if (a.Z <= b.Z) mask |= 4;
		if (a.W <= b.W) mask |= 8;
		return mask;
	}

	// SIMD test of a swept ray/box against all four child boxes at once. Returns a 4-bit hit mask.
	public static int IntersectRayWithFourBoxes(in FourVectors rayStart, in FourVectors invDelta, in FourVectors rayExtents, in FourVectors boxMins, in FourVectors boxMaxs) {
		FourVectors hitMins = boxMins - rayStart;
		FourVectors hitMaxs = boxMaxs - rayStart;

		// adjust for swept box by enlarging the child bounds to shrink the sweep to a point
		hitMins -= rayExtents;
		hitMaxs += rayExtents;

		// parametric distance along the ray of intersection in each dimension
		hitMins *= invDelta;
		hitMaxs *= invDelta;

		FourVectors exitT = Max(hitMins, hitMaxs);
		FourVectors entryT = Min(hitMins, hitMaxs);

		Vector4 boxEntryT = Vector4.Max(Vector4.Max(entryT.x, entryT.y), entryT.z);
		Vector4 boxExitT = Vector4.Min(Vector4.Min(exitT.x, exitT.y), exitT.z);

		boxEntryT = Vector4.Max(boxEntryT, Vector4.Zero);
		boxExitT = Vector4.Min(boxExitT, Vector4.One);

		// hit if entry <= exit
		return LeMask(boxEntryT, boxExitT);
	}

	// SIMD test of one box against four boxes. Returns a 4-bit overlap mask.
	public static int IntersectFourBoxPairs(in FourVectors mins0, in FourVectors maxs0, in FourVectors mins1, in FourVectors maxs1) {
		FourVectors intersectMins = Max(mins0, mins1);
		FourVectors intersectMaxs = Min(maxs0, maxs1);

		int maskX = LeMask(intersectMins.x, intersectMaxs.x);
		int maskY = LeMask(intersectMins.y, intersectMaxs.y);
		int maskZ = LeMask(intersectMins.z, intersectMaxs.z);

		return maskX & maskY & maskZ;
	}
}

public static class VectorFieldExts
{
	// Writable float[3] view over a vector's components (unlike the read-only Vector3 indexer).
	public static Span<float> Base(this ref Vector3 v) => MemoryMarshal.CreateSpan(ref Unsafe.As<Vector3, float>(ref v), 3);
	public static Span<float> Base(this ref QAngle a) => MemoryMarshal.CreateSpan(ref Unsafe.As<QAngle, float>(ref a), 3);


	public static unsafe ReadOnlySpan<float> ReadOnlyBase(this in Vector3 v) => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<Vector3, float>(ref Unsafe.AsRef<Vector3>(in v)), 3);
	public static unsafe ReadOnlySpan<float> ReadOnlyBase(this in QAngle a) => MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<QAngle, float>(ref Unsafe.AsRef<QAngle>(in a)), 3);
}
