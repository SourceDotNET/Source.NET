using CommunityToolkit.HighPerformance;

using Source.Common.MaterialSystem;

using System.Numerics;

namespace Source.MaterialSystem;

public sealed class MaterialVar : IMaterialVar
{
	IMaterialInternal owningMaterial;
	Matrix4x4 MatrixVal = Matrix4x4.Identity;

	void VarChanged() => owningMaterial?.ReportVarChanged(this);

	void Init() {

	}


	public MaterialVar(IMaterial material, ReadOnlySpan<char> key) {
		Init();
		owningMaterial = (IMaterialInternal)material!;
		Name = new(GetSymbol(key));
		Type = MaterialVarType.Undefined;
	}
	public MaterialVar(IMaterial material, ReadOnlySpan<char> key, int val) {
		Init();
		owningMaterial = (IMaterialInternal)material!;
		Name = new(GetSymbol(key));
		Type = MaterialVarType.Int;
		VecVal[0] = VecVal[1] = VecVal[2] = VecVal[3] = (float)val;
		IntVal = val;
	}
	public MaterialVar(IMaterial material, ReadOnlySpan<char> key, float val) {
		Init();
		owningMaterial = (IMaterialInternal)material!;
		Name = new(GetSymbol(key));
		Type = MaterialVarType.Float;
		VecVal[0] = VecVal[1] = VecVal[2] = VecVal[3] = val;
		IntVal = (int)val;
	}
	public MaterialVar(IMaterial material, ReadOnlySpan<char> key, Span<float> val) {
		Init();
		owningMaterial = (IMaterialInternal)material!;
		Name = new(GetSymbol(key));
		Type = MaterialVarType.Vector;
		NumVectorComps = (byte)Math.Min(val.Length, 4);
		for (int i = 0; i < NumVectorComps; i++)
			VecVal[i] = val[i];

		IntVal = (int)VecVal[0];
	}
	public MaterialVar(IMaterial material, ReadOnlySpan<char> key, ReadOnlySpan<char> val) {
		Init();
		owningMaterial = (IMaterialInternal)material!;
		Name = new(GetSymbol(key));
		StringVal = new(val);
		Type = MaterialVarType.String;
		VecVal[0] = VecVal[1] = VecVal[2] = VecVal[3] = float.TryParse(val, out float r) ? r : 0;
		IntVal = (int)VecVal[0];
	}

	public override void CopyFrom(IMaterialVar materialVar) {
		throw new NotImplementedException();
	}

	public override void GetFourCCValue(ulong type, out object? data) {
		throw new NotImplementedException();
	}

	public override IMaterial? GetMaterialValue() {
		throw new NotImplementedException();
	}

	public override Matrix4x4 GetMatrixValue() {
		if (Type == MaterialVarType.Matrix)
			return Matrix.Matrix;

		return Matrix4x4.Identity;
	}

	public override ReadOnlySpan<char> GetName() {
		if (!Name.IsValid()) {
			Warning("CMaterialVar::GetName: Name is NULL for MaterialVar\n");
			return "";
		}

		return MaterialVarSymbols.String(Name);
	}

	public override IMaterial GetOwningMaterial() {
		return owningMaterial;
	}

	public override string GetStringValue() {
		return StringVal;
	}

	static int reallyAnnoyingLogCount = 0;
	public override ITexture? GetTextureValue() {
		ITexture? retVal = null;

		owningMaterial?.Precache();

		if (Type == MaterialVarType.Texture) {
			if (!ITextureInternal.IsTextureInternalEnvCubemap(TextureValue))
				retVal = TextureValue;
			else
				retVal = ((Material)owningMaterial).materials.GetRenderContext().GetLocalCubemap();

			if (retVal == null)
				Warning("Invalid texture value in CMaterialVar::GetTextureValue\n");
		}
		else if (reallyAnnoyingLogCount++ < 20)
			Warning($"Requesting texture value from var \"{GetName()}\" of type \"{Type}\" which is not a texture value (material: {(owningMaterial != null ? owningMaterial.GetName() : "NULL material")})\n");

		retVal ??= ((Material)owningMaterial!).materials.GetErrorTexture();

		return retVal;
	}

	public override unsafe void GetVecValue(Span<float> val) {
		fixed (Vector4* v4 = &VecVal)
			new Span<float>(v4, val.Length > 4 ? 4 : val.Length).CopyTo(val);
	}

	public override bool IsDefined() {
		return Type != MaterialVarType.Undefined;
	}

	public override bool MatrixIsIdentity() {
		if (Type != MaterialVarType.Matrix)
			return true;

		return Matrix.IsIdent;
	}

	void FlushIfCurrentMaterial() {
		// Gotta flush if we've changed state and this is the current material
		if (owningMaterial is Material material && material.materials.GetCurrentMaterial() == owningMaterial)
			material.materials.ShaderAPI.FlushBufferedPrimitives();
	}

	public override void SetFloatValue(float val) {
		// Suppress all this if we're not actually changing anything
		if (Type == MaterialVarType.Float && VecVal[0] == val)
			return;

		FlushIfCurrentMaterial();

		VecVal[0] = VecVal[1] = VecVal[2] = VecVal[3] = val;
		IntVal = (int)val;
		Type = MaterialVarType.Float;
		VarChanged();
	}

	public override void SetFourCCValue(ulong type, object? data) {
		throw new NotImplementedException();
	}

	public override void SetIntValue(int val) {
		// Suppress all this if we're not actually changing anything
		if (Type == MaterialVarType.Int && IntVal == val)
			return;

		FlushIfCurrentMaterial();

		IntVal = val;
		VecVal[0] = VecVal[1] = VecVal[2] = VecVal[3] = val;
		Type = MaterialVarType.Int;
		VarChanged();
	}

	public override void SetMaterialValue(IMaterial? material) {
		throw new NotImplementedException();
	}

	public override void SetMatrixValue(in Matrix4x4 matrix) {
		FlushIfCurrentMaterial();

		Matrix.Matrix = matrix;
		Type = MaterialVarType.Matrix;
		Matrix.IsIdent = matrix.IsIdentity;
		VecVal = default;
		IntVal = (int)VecVal.X;
		VarChanged();
	}

	public override void SetStringValue(ReadOnlySpan<char> val) {
		FlushIfCurrentMaterial();

		StringVal = new(val.SliceNullTerminatedString());
		Type = MaterialVarType.String;
		VarChanged();
	}

	public override void SetTextureValue(ITexture? texture) {
		// Suppress all this if we're not actually changing anything
		if (Type == MaterialVarType.Texture && TextureValue == texture)
			return;

		FlushIfCurrentMaterial();

		Type = MaterialVarType.Texture;
		TextureValue = texture;
		VarChanged();
	}

	public override void SetUndefined() {
		if (Type == MaterialVarType.Undefined)
			return;

		FlushIfCurrentMaterial();

		Type = MaterialVarType.Undefined;
		VarChanged();
	}

	public override void SetValueAutodetectType(ReadOnlySpan<char> val) {
		throw new NotImplementedException();
	}

	public override void SetVecComponentValue(float val, int component) {
		if (Type == MaterialVarType.Vector && VecVal[component] == val)
			return;

		FlushIfCurrentMaterial();

		Type = MaterialVarType.Vector;
		Assert(component <= 3);

		if (NumVectorComps < component) {
			for (int i = NumVectorComps; i != component; ++i)
				VecVal[i] = 0.0f;

			NumVectorComps = (byte)component;
		}

		VecVal[component] = val;

		VarChanged();
	}

	void SetVecValueInternal(in Vector4 vec, int comps) {
		// Suppress all this if we're not actually changing anything
		if (Type == MaterialVarType.Vector && VecVal == vec)
			return;

		FlushIfCurrentMaterial();

		Type = MaterialVarType.Vector;
		Assert(comps <= 4);
		NumVectorComps = (byte)comps;
		VecVal = vec;
		IntVal = (int)VecVal[0];
		VarChanged();
	}

	public override void SetVecValue(ReadOnlySpan<float> val) {
		int comps = Math.Min(val.Length, 4);
		Vector4 vec = default;
		for (int i = 0; i < comps; i++)
			vec[i] = val[i];
		SetVecValueInternal(in vec, comps);
	}

	public override void SetVecValue(float x, float y) => SetVecValueInternal(new(x, y, 0.0f, 0.0f), 2);
	public override void SetVecValue(float x, float y, float z) => SetVecValueInternal(new(x, y, z, 0.0f), 3);
	public override void SetVecValue(float x, float y, float z, float w) => SetVecValueInternal(new(x, y, z, w), 4);

	protected override float GetFloatValueInternal() {
		throw new NotImplementedException();
	}

	protected override int GetIntValueInternal() {
		throw new NotImplementedException();
	}

	protected override Span<float> GetVecValueInternal() {
		throw new NotImplementedException();
	}

	protected override void GetVecValueInternal(Span<float> val) {
		throw new NotImplementedException();
	}

	protected override int VectorSizeInternal() {
		return NumVectorComps;
	}

	public override Span<float> GetVecValue() => new Span<Vector4>(ref VecVal).Cast<Vector4, float>();
}
