using Source.Common;
using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;

using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Client;

public class FloatInput {
	public bool Init(IMaterial material, KeyValues keyValues, ReadOnlySpan<char> keyName, float def = 0.0f){
		FloatVar = null;
		KeyValues? section = keyValues.FindKey(keyName);
		if (section != null) {
			if (section.Type == KeyValues.Types.String) {
				ReadOnlySpan<char> varName = section.GetString();

				// Look for numbers...
				float flValue;
				int nCount = new ScanF(varName, "%f").Read(out flValue).ReadArguments;
				if (nCount == 1) {
					Value = flValue;
					return true;
				}

				// Look for array specification...
				int bracket = varName.IndexOf('[');
				if (bracket >= 0) {
					FloatVecComp = ParseVecComp(varName[(bracket + 1)..]);
					varName = varName[..bracket];
				}
				else
					FloatVecComp = -1;

				bool foundVar;
				FloatVar = material.FindVar(varName, out foundVar, true);
				if (!foundVar)
					return false;
			}
			else {
				Value = section.GetFloat();
			}
		}
		else {
			Value = def;
		}

		return true;
	}

	public static int ParseVecComp(ReadOnlySpan<char> str) {
		int end = 0;
		if (end < str.Length && (str[end] == '-' || str[end] == '+'))
			end++;
		while (end < str.Length && char.IsAsciiDigit(str[end]))
			end++;
		return int.TryParse(str[..end], out int value) ? value : 0;
	}

	public float GetFloat(){
		if (FloatVar == null)
			return Value;

		if (FloatVecComp < 0)
			return FloatVar.GetFloatValue();

		int vecSize = FloatVar.VectorSize();
		if (FloatVecComp >= vecSize)
			return 0;

		Span<float> v = stackalloc float[4];
		FloatVar.GetVecValue(v[..vecSize]);
		return v[FloatVecComp];
	}

	float Value;
	IMaterialVar? FloatVar;
	int FloatVecComp;
}

public abstract class ResultProxy : IMaterialProxy
{
	public virtual bool Init(IMaterial material, KeyValues keyValues) {
		ReadOnlySpan<char> result = keyValues.GetString("resultVar");
		if (result.IsEmpty)
			return false;

		int bracket = result.IndexOf('[');
		if (bracket >= 0) {
			ResultVecComp = FloatInput.ParseVecComp(result[(bracket + 1)..]);
			result = result[..bracket];
		}
		else
			ResultVecComp = -1;

		bool foundVar;
		Result = material.FindVar(result, out foundVar, true);
		return foundVar;
	}
	public abstract void OnBind(object? o);
	public virtual void Release() { }
	public virtual IMaterial GetMaterial() => Result!.GetOwningMaterial();

	protected C_BaseEntity? BindArgToEntity(object? arg) {
		IClientRenderable? rend = (IClientRenderable?)arg;
		return rend != null ? rend.GetIClientUnknown().GetBaseEntity() : null;
	}
	protected void SetFloatResult(float result) {
		if (Result.GetVarType() == MaterialVarType.Vector) {
			if (ResultVecComp >= 0) 
				Result.SetVecComponentValue(result, ResultVecComp);
			else {
				Span<float> v = stackalloc float[4];
				int vecSize = Result.VectorSize();

				for (int i = 0; i < vecSize; ++i)
					v[i] = result;

				Result.SetVecValue(v[..vecSize]);
			}
		}
		else {
			Result.SetFloatValue(result);
		}
	}

	protected IMaterialVar? Result;
	protected int ResultVecComp;
}

public abstract class FunctionProxy : ResultProxy
{
	public override bool Init(IMaterial material, KeyValues keyValues) {
		if (!base.Init(material, keyValues))
			return false;

		ReadOnlySpan<char> srcVar1 = keyValues.GetString("srcVar1");
		if (srcVar1.IsEmpty)
			return false;

		bool foundVar;
		Src1 = material.FindVar(srcVar1, out foundVar, true);
		if (!foundVar)
			return false;

		ReadOnlySpan<char> srcVar2 = keyValues.GetString("srcVar2");
		if (!srcVar2.IsEmpty) {
			Src2 = material.FindVar(srcVar2, out foundVar, true);
			if (!foundVar)
				return false;
		}
		else
			Src2 = null;

		return true;
	}

	protected void ComputeResultType(out MaterialVarType resultType, ref int vecSize) {
		resultType = Result!.GetVarType();
		if (resultType == MaterialVarType.Vector) {
			if (ResultVecComp >= 0)
				resultType = MaterialVarType.Float;
			vecSize = Result.VectorSize();
		}
		else if (resultType == MaterialVarType.Undefined) {
			resultType = Src1!.GetVarType();
			if (resultType == MaterialVarType.Vector)
				vecSize = Src1.VectorSize();
			else if ((resultType == MaterialVarType.Undefined) && Src2 != null) {
				resultType = Src2.GetVarType();
				if (resultType == MaterialVarType.Vector)
					vecSize = Src2.VectorSize();
			}
		}
	}

	protected IMaterialVar? Src1;
	protected IMaterialVar? Src2;
}
