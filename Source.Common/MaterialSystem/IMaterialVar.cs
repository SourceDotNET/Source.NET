using Source.Common.Utilities;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.Common.MaterialSystem;

public enum MaterialVarType : ushort
{
	Float = 0,
	String,
	Vector,
	Texture,
	Int,
	FourCC,
	Undefined,
	Matrix,
	Material
}

public struct MaterialVarGPU
{
	public nint Program;
	public int Location;
}

public struct TokenCache
{
	public ulong Symbol;
	public int VarIndex;
	public bool Cached;
}

public struct MaterialVarMatrix
{
	public Matrix4x4 Matrix;
	public bool IsIdent;
}

public abstract class IMaterialVar
{
	protected MaterialVarMatrix Matrix;
	protected string StringVal = "";
	protected int IntVal;
	protected Vector4 VecVal;
	protected MaterialVarType Type;
	protected ITexture? TextureValue;
	protected byte NumVectorComps;
	protected bool FakeMaterialVar;
	protected byte TempIndex = 0xFF;
	protected UtlSymbol Name = new();
	public MaterialVarGPU GPU;

	public override string ToString() {
		return $"Material Var '{Name}', typeof {Type}";
	}

	public abstract ITexture? GetTextureValue();
	public abstract ReadOnlySpan<char> GetName();
	public abstract void SetFloatValue(float val);
	public abstract void SetIntValue(int val);
	public abstract void SetStringValue(ReadOnlySpan<char> val);
	public abstract string GetStringValue();
	public abstract void SetFourCCValue(ulong type, object? data);
	public abstract void GetFourCCValue(ulong type, out object? data);

	public abstract void SetVecValue(ReadOnlySpan<float> val);
	public abstract void SetVecValue(float x, float y);
	public abstract void SetVecValue(float x, float y, float z);
	public abstract void SetVecValue(float x, float y, float z, float w);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetVecValue(in Vector2 xy) => SetVecValue(xy.X, xy.Y);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetVecValue(in Vector3 xyz) => SetVecValue(xyz.X, xyz.Y, xyz.Z);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetVecValue(in Vector4 xyzw) => SetVecValue(xyzw.X, xyzw.Y, xyzw.Z, xyzw.W);

	public abstract void GetVecValue(Span<float> color);
	public abstract Span<float> GetVecValue();

	public void GetLinearVecValue(Span<float> pVal, int numComps) {
		Assert(numComps <= 4);
		switch (Type) {
			case MaterialVarType.Vector:
				for (int i = 0; i < numComps; i++)
					pVal[i] = Mathematics.MathLib.GammaToLinear(i == 0 ? VecVal.X : i == 1 ? VecVal.Y : i == 2 ? VecVal.Z : VecVal.W);
				break;
			case MaterialVarType.Int:
				for (int i = 0; i < numComps; i++)
					pVal[i] = Mathematics.MathLib.GammaToLinear(IntVal);
				break;
			case MaterialVarType.Float:
				for (int i = 0; i < numComps; i++)
					pVal[i] = Mathematics.MathLib.GammaToLinear(VecVal.X);
				break;
			case MaterialVarType.Matrix:
			case MaterialVarType.Undefined:
				for (int i = 0; i < numComps; i++)
					pVal[i] = 0.0f;
				break;
			default:
				Warning($"CMaterialVar::GetLinearVecValue: trying to get a vec value for {GetName()} which is of type {(int)Type}\n");
				break;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void GetVecValue(out Vector2 vec) {
		Span<float> retv = stackalloc float[2];
		GetVecValue(retv);
		vec = new(retv[0], retv[1]);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void GetVecValue(out Vector3 vec) {
		Span<float> retv = stackalloc float[3];
		GetVecValue(retv);
		vec = new(retv[0], retv[1], retv[2]);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void GetVecValue(out Vector4 vec) {
		Span<float> retv = stackalloc float[4];
		GetVecValue(retv);
		vec = new(retv[0], retv[1], retv[2], retv[3]);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Vector2 GetVec2Value() {
		Span<float> retv = stackalloc float[2];
		GetVecValue(retv);
		return new(retv[0], retv[1]);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Vector3 GetVec3Value() {
		Span<float> retv = stackalloc float[3];
		GetVecValue(retv);
		return new(retv[0], retv[1], retv[2]);
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public Vector4 GetVec4Value() {
		Span<float> retv = stackalloc float[4];
		GetVecValue(retv);
		return new(retv[0], retv[1], retv[2], retv[3]);
	}

	public abstract void SetTextureValue(ITexture? texture);
	public abstract IMaterial? GetMaterialValue();
	public abstract void SetMaterialValue(IMaterial? material);
	public abstract bool IsDefined();
	public abstract void SetUndefined();
	public abstract void SetMatrixValue(in Matrix4x4 matrix);
	public abstract Matrix4x4 GetMatrixValue();
	public abstract bool MatrixIsIdentity();
	public abstract void CopyFrom(IMaterialVar materialVar);
	public abstract void SetValueAutodetectType(ReadOnlySpan<char> val);
	public abstract IMaterial GetOwningMaterial();
	public abstract void SetVecComponentValue(float val, int component);

	public MaterialVarType GetVarType() => Type;
	public bool IsTexture() => Type == MaterialVarType.Texture;

	protected abstract int GetIntValueInternal();
	protected abstract float GetFloatValueInternal();
	protected abstract Span<float> GetVecValueInternal();
	protected abstract void GetVecValueInternal(Span<float> val);
	protected abstract int VectorSizeInternal();

	public int GetIntValue() {
		return TempIndex == 0xFF ? IntVal : GetIntValueInternal();
	}
	public float GetFloatValue() {
		return TempIndex == 0xFF ? VecVal.X : GetFloatValueInternal();
	}

	public static readonly UtlSymbolTableMT MaterialVarSymbols = new(true);
	public static UtlSymId_t GetSymbol(ReadOnlySpan<char> name) {
		if (name.IsEmpty)
			return UTL_INVAL_SYMBOL;

		Span<char> temp = stackalloc char[1024];
		strcpy(temp, name);
		strlower(temp);
		return MaterialVarSymbols.AddString(temp.SliceNullTerminatedString());
	}

	public static UtlSymId_t FindSymbol(ReadOnlySpan<char> name) {
		if (name.IsEmpty)
			return UTL_INVAL_SYMBOL;
		return MaterialVarSymbols.Find(name);
	}

	public UtlSymId_t GetNameAsSymbol() {
		return Name;
	}

	public static bool SymbolMatches(ReadOnlySpan<char> varName, UtlSymId_t symbol) {
		return varName.CompareTo(MaterialVarSymbols.String(symbol), StringComparison.InvariantCultureIgnoreCase) == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public int VectorSize() {
		return VectorSizeInternal();
	}
}
