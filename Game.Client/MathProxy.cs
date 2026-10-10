using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;

using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Client;

[ExposeMaterialProxy(Name = "Sine")]
public class SineProxy : ResultProxy {
	public override bool Init(IMaterial material, KeyValues keyValues) {
		if (!base.Init(material, keyValues))
			return false;

		if (!SinePeriod.Init(material, keyValues, "sinePeriod", 1.0f))
			return false;
		if (!SineMax.Init(material, keyValues, "sineMax", 1.0f))
			return false;
		if (!SineMin.Init(material, keyValues, "sineMin", 0.0f))
			return false;
		if (!SineTimeOffset.Init(material, keyValues, "timeOffset", 0.0f))
			return false;

		return true;
	}
	public override void OnBind(object? o) {
		float flValue;
		float flSineTimeOffset = SineTimeOffset.GetFloat();
		float flSineMax = SineMax.GetFloat();
		float flSineMin = SineMin.GetFloat();
		float flSinePeriod = SinePeriod.GetFloat();
		if (flSinePeriod == 0)
			flSinePeriod = 1;

		// get a value in [0,1]
		flValue = (float)((Math.Sin(2.0f * Math.PI * (gpGlobals.CurTime - flSineTimeOffset) / flSinePeriod) * 0.5) + 0.5);
		// get a value in [min,max]	
		flValue = (flSineMax - flSineMin) * flValue + flSineMin;

		SetFloatResult(flValue);
	}

	readonly FloatInput SinePeriod = new();
	readonly FloatInput SineMax = new();
	readonly FloatInput SineMin = new();
	readonly FloatInput SineTimeOffset = new();
}

[ExposeMaterialProxy(Name = "Add")]
public class AddProxy : FunctionProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		bool ok = base.Init(material, keyValues);
		ok = ok && Src2 != null;
		return ok;
	}

	public override void OnBind(object? o) {
		int vecSize = 0;
		ComputeResultType(out MaterialVarType resultType, ref vecSize);

		switch (resultType) {
			case MaterialVarType.Vector: {
					Span<float> a = stackalloc float[4], b = stackalloc float[4];
					Src1!.GetVecValue(a[..vecSize]);
					Src2!.GetVecValue(b[..vecSize]);
					for (int i = 0; i < vecSize; i++)
						a[i] += b[i];
					Result!.SetVecValue(a[..vecSize]);
				}
				break;

			case MaterialVarType.Float:
				SetFloatResult(Src1!.GetFloatValue() + Src2!.GetFloatValue());
				break;

			case MaterialVarType.Int:
				Result!.SetFloatValue(Src1!.GetIntValue() + Src2!.GetIntValue());
				break;
		}
	}
}

[ExposeMaterialProxy(Name = "Subtract")]
public class SubtractProxy : FunctionProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		bool ok = base.Init(material, keyValues);
		ok = ok && Src2 != null;
		return ok;
	}

	public override void OnBind(object? o) {
		int vecSize = 0;
		ComputeResultType(out MaterialVarType resultType, ref vecSize);

		switch (resultType) {
			case MaterialVarType.Vector: {
					Span<float> a = stackalloc float[4], b = stackalloc float[4];
					Src1!.GetVecValue(a[..vecSize]);
					Src2!.GetVecValue(b[..vecSize]);
					for (int i = 0; i < vecSize; i++)
						a[i] -= b[i];
					Result!.SetVecValue(a[..vecSize]);
				}
				break;

			case MaterialVarType.Float:
				SetFloatResult(Src1!.GetFloatValue() - Src2!.GetFloatValue());
				break;

			case MaterialVarType.Int:
				Result!.SetFloatValue(Src1!.GetIntValue() - Src2!.GetIntValue());
				break;
		}
	}
}

[ExposeMaterialProxy(Name = "Multiply")]
public class MultiplyProxy : FunctionProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		bool ok = base.Init(material, keyValues);
		ok = ok && Src2 != null;
		return ok;
	}

	public override void OnBind(object? o) {
		int vecSize = 0;
		ComputeResultType(out MaterialVarType resultType, ref vecSize);

		switch (resultType) {
			case MaterialVarType.Vector: {
					Span<float> a = stackalloc float[4], b = stackalloc float[4];
					Src1!.GetVecValue(a[..vecSize]);
					Src2!.GetVecValue(b[..vecSize]);
					for (int i = 0; i < vecSize; i++)
						a[i] *= b[i];
					Result!.SetVecValue(a[..vecSize]);
				}
				break;

			case MaterialVarType.Float:
				SetFloatResult(Src1!.GetFloatValue() * Src2!.GetFloatValue());
				break;

			case MaterialVarType.Int:
				Result!.SetFloatValue(Src1!.GetIntValue() * Src2!.GetIntValue());
				break;
		}
	}
}

[ExposeMaterialProxy(Name = "Divide")]
public class DivideProxy : FunctionProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		bool ok = base.Init(material, keyValues);
		ok = ok && Src2 != null;
		return ok;
	}

	public override void OnBind(object? o) {
		int vecSize = 0;
		ComputeResultType(out MaterialVarType resultType, ref vecSize);

		switch (resultType) {
			case MaterialVarType.Vector: {
					Span<float> a = stackalloc float[4], b = stackalloc float[4];
					Src1!.GetVecValue(a[..vecSize]);
					Src2!.GetVecValue(b[..vecSize]);
					for (int i = 0; i < vecSize; i++)
						a[i] /= b[i];
					Result!.SetVecValue(a[..vecSize]);
				}
				break;

			case MaterialVarType.Float:
				if (Src2!.GetFloatValue() != 0)
					SetFloatResult(Src1!.GetFloatValue() / Src2.GetFloatValue());
				else
					SetFloatResult(Src1!.GetFloatValue());
				break;

			case MaterialVarType.Int:
				if (Src2!.GetIntValue() != 0)
					Result!.SetFloatValue(Src1!.GetIntValue() / Src2.GetIntValue());
				else
					Result!.SetFloatValue(Src1!.GetIntValue());
				break;
		}
	}
}

[ExposeMaterialProxy(Name = "Clamp")]
public class ClampProxy : FunctionProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		if (!base.Init(material, keyValues))
			return false;

		if (!Min.Init(material, keyValues, "min", 0))
			return false;

		if (!Max.Init(material, keyValues, "max", 1))
			return false;

		return true;
	}

	public override void OnBind(object? o) {
		int vecSize = 0;
		ComputeResultType(out MaterialVarType resultType, ref vecSize);

		float min = Min.GetFloat();
		float max = Max.GetFloat();

		if (min > max)
			(min, max) = (max, min);

		switch (resultType) {
			case MaterialVarType.Vector: {
					Span<float> a = stackalloc float[4];
					Src1!.GetVecValue(a[..vecSize]);
					for (int i = 0; i < vecSize; ++i) {
						if (a[i] < min)
							a[i] = min;
						else if (a[i] > max)
							a[i] = max;
					}
					Result!.SetVecValue(a[..vecSize]);
				}
				break;

			case MaterialVarType.Float: {
					float src = Src1!.GetFloatValue();
					if (src < min)
						src = min;
					else if (src > max)
						src = max;
					SetFloatResult(src);
				}
				break;

			case MaterialVarType.Int: {
					int src = Src1!.GetIntValue();
					if (src < min)
						src = (int)min;
					else if (src > max)
						src = (int)max;
					Result!.SetIntValue(src);
				}
				break;
		}
	}

	readonly FloatInput Min = new();
	readonly FloatInput Max = new();
}

[ExposeMaterialProxy(Name = "Equals")]
public class EqualsProxy : FunctionProxy
{
	public override void OnBind(object? o) {
		int vecSize = 0;
		ComputeResultType(out MaterialVarType resultType, ref vecSize);

		switch (resultType) {
			case MaterialVarType.Vector: {
					Span<float> a = stackalloc float[4];
					Src1!.GetVecValue(a[..vecSize]);
					Result!.SetVecValue(a[..vecSize]);
				}
				break;

			case MaterialVarType.Float:
				SetFloatResult(Src1!.GetFloatValue());
				break;

			case MaterialVarType.Int:
				Result!.SetIntValue(Src1!.GetIntValue());
				break;
		}
	}
}

[ExposeMaterialProxy(Name = "Frac")]
public class FracProxy : FunctionProxy
{
	public override void OnBind(object? o) {
		int vecSize = 0;
		ComputeResultType(out MaterialVarType resultType, ref vecSize);

		switch (resultType) {
			case MaterialVarType.Vector: {
					Span<float> a = stackalloc float[4];
					Src1!.GetVecValue(a[..vecSize]);
					a[0] -= (int)a[0];
					a[1] -= (int)a[1];
					a[2] -= (int)a[2];
					Result!.SetVecValue(a[..vecSize]);
				}
				break;

			case MaterialVarType.Float: {
					float a = Src1!.GetFloatValue();
					a -= (int)a;
					SetFloatResult(a);
				}
				break;

			case MaterialVarType.Int:
				Result!.SetIntValue(Src1!.GetIntValue());
				break;
		}
	}
}

[ExposeMaterialProxy(Name = "Int")]
public class IntProxy : FunctionProxy
{
	public override void OnBind(object? o) {
		int vecSize = 0;
		ComputeResultType(out MaterialVarType resultType, ref vecSize);

		switch (resultType) {
			case MaterialVarType.Vector: {
					Span<float> a = stackalloc float[4];
					Src1!.GetVecValue(a[..vecSize]);
					a[0] = (int)a[0];
					a[1] = (int)a[1];
					a[2] = (int)a[2];
					Result!.SetVecValue(a[..vecSize]);
				}
				break;

			case MaterialVarType.Float: {
					float a = Src1!.GetFloatValue();
					a = (int)a;
					SetFloatResult(a);
				}
				break;

			case MaterialVarType.Int:
				Result!.SetIntValue(Src1!.GetIntValue());
				break;
		}
	}
}

[ExposeMaterialProxy(Name = "LinearRamp")]
public class LinearRampProxy : ResultProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		if (!base.Init(material, keyValues))
			return false;

		if (!Rate.Init(material, keyValues, "rate", 1))
			return false;

		if (!InitialValue.Init(material, keyValues, "initialValue", 0))
			return false;

		return true;
	}

	public override void OnBind(object? o) {
		float value = (float)(Rate.GetFloat() * gpGlobals.CurTime + InitialValue.GetFloat());
		SetFloatResult(value);
	}

	readonly FloatInput Rate = new();
	readonly FloatInput InitialValue = new();
}

[ExposeMaterialProxy(Name = "UniformNoise")]
public class UniformNoiseProxy : ResultProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		if (!base.Init(material, keyValues))
			return false;

		if (!MinVal.Init(material, keyValues, "minVal", 0))
			return false;

		if (!MaxVal.Init(material, keyValues, "maxVal", 1))
			return false;

		return true;
	}

	public override void OnBind(object? o) {
		SetFloatResult(random.RandomFloat(MinVal.GetFloat(), MaxVal.GetFloat()));
	}

	readonly FloatInput MinVal = new();
	readonly FloatInput MaxVal = new();
}

[ExposeMaterialProxy(Name = "GaussianNoise")]
public class GaussianNoiseProxy : ResultProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		if (!base.Init(material, keyValues))
			return false;

		if (!Mean.Init(material, keyValues, "mean", 0.0f))
			return false;

		if (!StdDev.Init(material, keyValues, "halfwidth", 1.0f))
			return false;

		if (!MinVal.Init(material, keyValues, "minVal", -float.MaxValue))
			return false;

		if (!MaxVal.Init(material, keyValues, "maxVal", float.MaxValue))
			return false;

		return true;
	}

	public override void OnBind(object? o) {
		float mean = Mean.GetFloat();
		float stdDev = StdDev.GetFloat();
		float val = randomgaussian.RandomFloat(mean, stdDev);
		float maxVal = MaxVal.GetFloat();
		float minVal = MinVal.GetFloat();

		if (minVal > maxVal)
			(minVal, maxVal) = (maxVal, minVal);

		if (val < minVal)
			val = minVal;
		else if (val > maxVal)
			val = maxVal;

		SetFloatResult(val);
	}

	readonly FloatInput Mean = new();
	readonly FloatInput StdDev = new();
	readonly FloatInput MinVal = new();
	readonly FloatInput MaxVal = new();
}

[ExposeMaterialProxy(Name = "Exponential")]
public class ExponentialProxy : FunctionProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		if (!base.Init(material, keyValues))
			return false;

		if (!Scale.Init(material, keyValues, "scale", 1.0f))
			return false;

		if (!Offset.Init(material, keyValues, "offset", 0.0f))
			return false;

		if (!MinVal.Init(material, keyValues, "minVal", -float.MaxValue))
			return false;

		if (!MaxVal.Init(material, keyValues, "maxVal", float.MaxValue))
			return false;

		return true;
	}

	public override void OnBind(object? o) {
		float val = Scale.GetFloat() * MathF.Exp(Src1!.GetFloatValue() + Offset.GetFloat());

		float maxVal = MaxVal.GetFloat();
		float minVal = MinVal.GetFloat();

		if (minVal > maxVal)
			(minVal, maxVal) = (maxVal, minVal);

		if (val < minVal)
			val = minVal;
		else if (val > maxVal)
			val = maxVal;

		SetFloatResult(val);
	}

	readonly FloatInput Scale = new();
	readonly FloatInput Offset = new();
	readonly FloatInput MinVal = new();
	readonly FloatInput MaxVal = new();
}

[ExposeMaterialProxy(Name = "Abs")]
public class AbsProxy : FunctionProxy
{
	public override void OnBind(object? o) {
		SetFloatResult(MathF.Abs(Src1!.GetFloatValue()));
	}
}

[ExposeMaterialProxy(Name = "Empty")]
public class EmptyProxy : IMaterialProxy
{
	public bool Init(IMaterial material, KeyValues keyValues) => true;
	public void OnBind(object? o) { }
	public void Release() { }
	public IMaterial GetMaterial() => null!;
}

[ExposeMaterialProxy(Name = "LessOrEqual")]
public class LessOrEqualProxy : FunctionProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		ReadOnlySpan<char> lessEqualVar = keyValues.GetString("lessEqualVar");
		if (lessEqualVar.IsEmpty)
			return false;

		bool foundVar;
		LessVar = material.FindVar(lessEqualVar, out foundVar, true);
		if (!foundVar)
			return false;

		ReadOnlySpan<char> greaterVar = keyValues.GetString("greaterVar");
		if (greaterVar.IsEmpty)
			return false;

		GreaterVar = material.FindVar(greaterVar, out foundVar, true);
		if (!foundVar)
			return false;

		bool ok = base.Init(material, keyValues);
		ok = ok && Src2 != null;
		return ok;
	}

	public override void OnBind(object? o) {
		IMaterialVar sourceVar;
		if (Src1!.GetFloatValue() <= Src2!.GetFloatValue())
			sourceVar = LessVar!;
		else
			sourceVar = GreaterVar!;

		int vecSize = 0;
		MaterialVarType resultType = Result!.GetVarType();
		if (resultType == MaterialVarType.Vector) {
			if (ResultVecComp >= 0)
				resultType = MaterialVarType.Float;
			vecSize = Result.VectorSize();
		}
		else if (resultType == MaterialVarType.Undefined) {
			resultType = sourceVar.GetVarType();
			if (resultType == MaterialVarType.Vector)
				vecSize = sourceVar.VectorSize();
		}

		switch (resultType) {
			case MaterialVarType.Vector: {
					Span<float> src = stackalloc float[4];
					sourceVar.GetVecValue(src[..vecSize]);
					Result.SetVecValue(src[..vecSize]);
				}
				break;

			case MaterialVarType.Float:
				SetFloatResult(sourceVar.GetFloatValue());
				break;

			case MaterialVarType.Int:
				Result.SetFloatValue(sourceVar.GetIntValue());
				break;
		}
	}

	IMaterialVar? LessVar;
	IMaterialVar? GreaterVar;
}

[ExposeMaterialProxy(Name = "WrapMinMax")]
public class WrapMinMaxProxy : FunctionProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		if (!base.Init(material, keyValues))
			return false;

		if (!MinVal.Init(material, keyValues, "minVal", 0))
			return false;

		if (!MaxVal.Init(material, keyValues, "maxVal", 1))
			return false;

		return true;
	}

	public override void OnBind(object? o) {
		if (MaxVal.GetFloat() <= MinVal.GetFloat())
			SetFloatResult(MinVal.GetFloat());
		else {
			float result = (Src1!.GetFloatValue() - MinVal.GetFloat()) / (MaxVal.GetFloat() - MinVal.GetFloat());

			if (result >= 0.0f)
				result -= (int)result;
			else
				result -= ((int)result) - 1;

			result *= MaxVal.GetFloat() - MinVal.GetFloat();
			result += MinVal.GetFloat();

			SetFloatResult(result);
		}
	}

	readonly FloatInput MinVal = new();
	readonly FloatInput MaxVal = new();
}

[ExposeMaterialProxy(Name = "SelectFirstIfNonZero")]
public class SelectFirstIfNonZeroProxy : FunctionProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		bool ok = base.Init(material, keyValues);
		ok = ok && Src2 != null;
		return ok;
	}

	public override void OnBind(object? o) {
		int vecSize = 0;
		ComputeResultType(out MaterialVarType resultType, ref vecSize);

		switch (resultType) {
			case MaterialVarType.Vector: {
					Span<float> a = stackalloc float[4], b = stackalloc float[4];
					Src1!.GetVecValue(a[..vecSize]);
					Src2!.GetVecValue(b[..vecSize]);

					const float tolerance = 0.01f;
					bool aIsZero = a[0] > -tolerance && a[0] < tolerance &&
						a[1] > -tolerance && a[1] < tolerance &&
						a[2] > -tolerance && a[2] < tolerance;

					if (!aIsZero)
						Result!.SetVecValue(a[..vecSize]);
					else
						Result!.SetVecValue(b[..vecSize]);
				}
				break;

			case MaterialVarType.Float:
				if (Src1!.GetFloatValue() != 0)
					SetFloatResult(Src1.GetFloatValue());
				else
					SetFloatResult(Src2!.GetFloatValue());
				break;

			case MaterialVarType.Int:
				if (Src1!.GetIntValue() != 0)
					Result!.SetFloatValue(Src1.GetIntValue());
				else
					Result!.SetFloatValue(Src2!.GetIntValue());
				break;
		}
	}
}
