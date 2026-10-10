using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Client;

[ExposeMaterialProxy(Name = "TextureScroll")]
public class TextureScrollMaterialProxy : IMaterialProxy
{
	public bool Init(IMaterial material, KeyValues keyValues) {
		ReadOnlySpan<char> scrollVarName = keyValues.GetString("textureScrollVar");
		if (scrollVarName.IsEmpty)
			return false;

		bool foundVar;
		TextureScrollVar = material.FindVar(scrollVarName, out foundVar, false);
		if (!foundVar)
			return false;

		TextureScrollRate.Init(material, keyValues, "textureScrollRate", 1.0f);
		TextureScrollAngle.Init(material, keyValues, "textureScrollAngle", 0.0f);
		TextureScale.Init(material, keyValues, "textureScale", 1.0f);

		return true;
	}

	public void OnBind(object? o) {
		if (TextureScrollVar == null)
			return;

		float rate, angle, scale;

		rate = TextureScrollRate.GetFloat();
		angle = TextureScrollAngle.GetFloat();
		scale = TextureScale.GetFloat();

		float sOffset, tOffset;

		sOffset = (float)(gpGlobals.CurTime * MathF.Cos(angle * (MathF.PI / 180.0f)) * rate);
		tOffset = (float)(gpGlobals.CurTime * MathF.Sin(angle * (MathF.PI / 180.0f)) * rate);

		if (sOffset < 0.0f)
			sOffset += 1.0f + -(int)sOffset;
		if (tOffset < 0.0f)
			tOffset += 1.0f + -(int)tOffset;

		sOffset -= (int)sOffset;
		tOffset -= (int)tOffset;

		if (TextureScrollVar.GetVarType() == MaterialVarType.Matrix) {
			Matrix4x4 mat = default;
			mat.Init(scale, 0.0f, 0.0f, sOffset,
				0.0f, scale, 0.0f, tOffset,
				0.0f, 0.0f, 1.0f, 0.0f,
				0.0f, 0.0f, 0.0f, 1.0f);
			TextureScrollVar.SetMatrixValue(mat);
		}
		else
			TextureScrollVar.SetVecValue(sOffset, tOffset, 0.0f);
	}

	public void Release() { }

	public IMaterial GetMaterial() => TextureScrollVar!.GetOwningMaterial();

	IMaterialVar? TextureScrollVar;
	readonly FloatInput TextureScrollRate = new();
	readonly FloatInput TextureScrollAngle = new();
	readonly FloatInput TextureScale = new();
}
