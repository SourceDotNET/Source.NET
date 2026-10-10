using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client;

[ExposeMaterialProxy(Name = "TextureTransform")]
public class TextureTransformProxy : ResultProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		CenterVar = null;
		ScaleVar = null;
		RotateVar = null;
		TranslateVar = null;

		ReadOnlySpan<char> varName = keyValues.GetString("centerVar");
		if (!varName.IsEmpty)
			CenterVar = material.FindVar(varName, out _, false);

		varName = keyValues.GetString("scaleVar");
		if (!varName.IsEmpty)
			ScaleVar = material.FindVar(varName, out _, false);

		varName = keyValues.GetString("rotateVar");
		if (!varName.IsEmpty)
			RotateVar = material.FindVar(varName, out _, false);

		varName = keyValues.GetString("translateVar");
		if (!varName.IsEmpty)
			TranslateVar = material.FindVar(varName, out _, false);

		return base.Init(material, keyValues);
	}

	public override void OnBind(object? o) {
		Span<float> center = [0.5f, 0.5f];
		Span<float> translation = [0, 0];

		CenterVar?.GetVecValue(center);
		MathLib.MatrixBuildTranslation(out Matrix4x4 mat, -center[0], -center[1], 0.0f);
		Matrix4x4 temp;

		if (ScaleVar != null) {
			Span<float> scale = stackalloc float[2];
			ScaleVar.GetVecValue(scale);
			MathLib.MatrixBuildScale(out temp, scale[0], scale[1], 1.0f);
			MathLib.MatrixMultiply(temp, mat, out mat);
		}

		if (RotateVar != null) {
			float angle = RotateVar.GetFloatValue();
			MathLib.MatrixBuildRotateZ(out temp, angle);
			MathLib.MatrixMultiply(temp, mat, out mat);
		}
		MathLib.MatrixBuildTranslation(out temp, center[0], center[1], 0.0f);
		MathLib.MatrixMultiply(temp, mat, out mat);

		if (TranslateVar != null) {
			TranslateVar.GetVecValue(translation);
			MathLib.MatrixBuildTranslation(out temp, translation[0], translation[1], 0.0f);
			MathLib.MatrixMultiply(temp, mat, out mat);
		}

		Result!.SetMatrixValue(mat);
	}

	IMaterialVar? CenterVar;
	IMaterialVar? ScaleVar;
	IMaterialVar? RotateVar;
	IMaterialVar? TranslateVar;
}

[ExposeMaterialProxy(Name = "MatrixRotate")]
public class MatrixRotateProxy : ResultProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		AxisVar = null;

		ReadOnlySpan<char> varName = keyValues.GetString("axisVar");
		if (!varName.IsEmpty)
			AxisVar = material.FindVar(varName, out _, false);

		if (!Angle.Init(material, keyValues, "angle", 0))
			return false;

		return base.Init(material, keyValues);
	}

	public override void OnBind(object? o) {
		Vector3 axis = new(0, 0, 1);
		if (AxisVar != null) {
			AxisVar.GetVecValue(out axis);
			if (MathLib.VectorNormalize(ref axis) < 1e-3)
				axis = new(0, 0, 1);
		}

		MathLib.MatrixBuildRotationAboutAxis(out Matrix4x4 mat, axis, Angle.GetFloat());
		Result!.SetMatrixValue(mat);
	}

	readonly FloatInput Angle = new();
	IMaterialVar? AxisVar;
}
