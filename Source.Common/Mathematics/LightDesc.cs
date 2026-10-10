using System.Numerics;

namespace Source.Common.Mathematics;

public enum LightType
{
	Disable = 0,
	Point,
	Directional,
	Spot
}

[Flags]
public enum LightTypeOptimizationFlags
{
	HasAttenuation0 = 1,
	HasAttenuation1 = 2,
	HasAttenuation2 = 4,
	DerivedValuesCalced = 8
}

public struct LightDesc
{
	public LightType Type;                          //< MATERIAL_LIGHT_xxx
	public Vector3 Color;                           //< color+intensity
	public Vector3 Position;                        //< light source center position
	public Vector3 Direction;                       //< for SPOT, direction it is pointing
	public float Range;                             //< distance range for light.0=infinite
	public float Falloff;                           //< angular falloff exponent for spot lights
	public float Attenuation0;                      //< constant distance falloff term
	public float Attenuation1;                      //< linear term of falloff
	public float Attenuation2;                      //< quadatic term of falloff
	public float Theta;                             //< inner cone angle. no angular falloff within this cone
	public float Phi;                               //< outer cone angle

	// the values below are derived from the above settings for optimizations
	public float ThetaDot;
	public float PhiDot;
	public uint Flags;
	private float OneOver_ThetaDot_Minus_PhiDot;
	private float RangeSquared;

	public void RecalculateDerivedValues() {
		Flags = (uint)LightTypeOptimizationFlags.DerivedValuesCalced;
		if (Attenuation0 != 0)
			Flags |= (uint)LightTypeOptimizationFlags.HasAttenuation0;
		if (Attenuation1 != 0)
			Flags |= (uint)LightTypeOptimizationFlags.HasAttenuation1;
		if (Attenuation2 != 0)
			Flags |= (uint)LightTypeOptimizationFlags.HasAttenuation2;

		if (Type == LightType.Spot) {
			ThetaDot = MathF.Cos(Theta);
			PhiDot = MathF.Cos(Phi);
			float spread = ThetaDot - PhiDot;
			if (spread > 1.0e-10f)
				OneOver_ThetaDot_Minus_PhiDot = 1.0f / spread;
			else
				OneOver_ThetaDot_Minus_PhiDot = 1.0f;
		}
		if (Type == LightType.Directional) {
			Position = Direction;
			Position *= 2.0e6f;
		}

		RangeSquared = Range * Range;
	}

	public void SetupOldStyleAttenuation(float quadraticAttn, float linearAttn, float constantAttn) {
		if (quadraticAttn < EQUAL_EPSILON)
			quadraticAttn = 0;

		if (linearAttn < EQUAL_EPSILON)
			linearAttn = 0;

		if (constantAttn < EQUAL_EPSILON)
			constantAttn = 0;

		if ((constantAttn < EQUAL_EPSILON) && (linearAttn < EQUAL_EPSILON) && (quadraticAttn < EQUAL_EPSILON))
			constantAttn = 1;

		Attenuation2 = quadraticAttn;
		Attenuation1 = linearAttn;
		Attenuation0 = constantAttn;
		float scaleFactor = quadraticAttn * 10000 + linearAttn * 100 + constantAttn;

		if (scaleFactor > 0)
			Color *= scaleFactor;
	}

	public void SetupNewStyleAttenuation(float fiftyPercentDistance, float zeroPercentDistance) {
		float d50 = fiftyPercentDistance;
		float d0 = zeroPercentDistance;
		if (d0 < d50) {
			Warning($"light has _fifty_percent_distance of {d50:F6} but no zero_percent_distance\n");
			d0 = 2.0f * d50;
		}
		float a = 0, b = 1, c = 0;
		if (!MathLib.SolveInverseQuadraticMonotonic(0, 1.0f, d50, 2.0f, d0, 256.0f, ref a, ref b, ref c))
			Warning($"can't solve quadratic for light {d50:F6} {d0:F6}\n");
		float v50 = c + d50 * (b + d50 * a);
		float scale = 2.0f / v50;
		a *= scale;
		b *= scale;
		c *= scale;
		Attenuation2 = a;
		Attenuation1 = b;
		Attenuation0 = c;
	}
}
