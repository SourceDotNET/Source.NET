using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class ShadowModel : BaseVSShader
{
	public static string HelpString = "Help for ShadowModel";
	public static ShaderFlags Flags = ShaderFlags.NotEditable;
	public static List<ShaderParam> ShaderParams = [];
	public static ShaderParam[] ShaderParamOverrides = new ShaderParam[(int)ShaderMaterialVars.Count];

	public class ShaderParam
	{
		public readonly ShaderParamInfo Info;
		public readonly int Index;
		public ShaderParam(ShaderMaterialVars var, ShaderParamType type, ReadOnlySpan<char> defaultParam, ReadOnlySpan<char> help, ShaderParamFlags flags) {
			Info.Name = "override";
			Info.Type = type;
			Info.DefaultValue = new(defaultParam);
			Info.Help = new(help);
			Info.Flags = flags;

			if (ShaderParamOverrides[(int)var] == null) {

			}
			else {
				AssertMsg(false, "ShaderParamOverrides at var index had null value");
			}

			ShaderParamOverrides[(int)var] = this;
			Index = (int)var;
		}
		public ShaderParam(string name, ShaderParamType type, ReadOnlySpan<char> defaultParam, ReadOnlySpan<char> help, ShaderParamFlags flags = 0) {
			Info.Name = name;
			Info.Type = type;
			Info.DefaultValue = new(defaultParam);
			Info.Help = new(help);
			Info.Flags = flags;
			Index = (int)ShaderMaterialVars.Count + ShaderParams.Count;
			ShaderParams.Add(this);
		}
		public static implicit operator int(ShaderParam param) => param.Index;
		public ReadOnlySpan<char> GetName() => Info.Name;
		public ShaderParamType GetType() => Info.Type;
		public ReadOnlySpan<char> GetDefaultValue() => Info.DefaultValue;
		public ShaderParamFlags GetFlags() => Info.Flags;
		public ReadOnlySpan<char> GetHelp() => Info.Help;
	}

	public static readonly ShaderParam BASETEXTUREOFFSET = new("$basetextureoffset", ShaderParamType.Vec2, "[0 0]", "$baseTexture texcoord offset");
	public static readonly ShaderParam BASETEXTURESCALE = new("$basetexturescale", ShaderParamType.Vec2, "[1 1]", "$baseTexture texcoord scale");
	public static readonly ShaderParam FALLOFFOFFSET = new("$falloffoffset", ShaderParamType.Float, "0", "Distance at which shadow starts to fade");
	public static readonly ShaderParam FALLOFFDISTANCE = new("$falloffdistance", ShaderParamType.Float, "100", "Max shadow distance");
	public static readonly ShaderParam FALLOFFAMOUNT = new("$falloffamount", ShaderParamType.Float, "0.9", "Amount to brighten the shadow at max dist");

	protected override void OnInitShaderParams(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		if (!vars[BASETEXTURESCALE].IsDefined()) {
			vars[BASETEXTURESCALE].SetVecValue(1, 1);
		}

		if (!vars[FALLOFFDISTANCE].IsDefined())
			vars[FALLOFFDISTANCE].SetFloatValue(100.0f);

		if (!vars[FALLOFFAMOUNT].IsDefined())
			vars[FALLOFFAMOUNT].SetFloatValue(0.9f);
	}

	public override string? GetFallbackShader(IMaterialVar[] vars) {
		return null;
	}
	public override ShaderFlags GetFlags() => Flags;
	public override int GetNumParams() => base.GetNumParams() + ShaderParams.Count;
	public override ReadOnlySpan<char> GetParamName(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamName(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetName();
	}
	public override ReadOnlySpan<char> GetParamHelp(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamHelp(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetHelp();
	}
	public override ShaderParamType GetParamType(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamType(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetType();
	}
	public override ReadOnlySpan<char> GetParamDefault(int paramIndex) {
		int baseClassParamCount = base.GetNumParams();
		if (paramIndex < baseClassParamCount)
			return base.GetParamDefault(paramIndex);
		else
			return ShaderParams[paramIndex - baseClassParamCount].GetDefaultValue();
	}
	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		if (vars[(int)ShaderMaterialVars.BaseTexture].IsDefined()) {
			LoadTexture((int)ShaderMaterialVars.BaseTexture);
		}
	}
	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (ShaderShadow != null) {
			ShaderShadow.EnableTexture(Sampler.Sampler0, true);

			EnableAlphaBlending(ShaderBlendFactor.DstColor, ShaderBlendFactor.Zero);

			VertexFormat fmt = VertexFormat.Position | VertexFormat.Normal;
			ShaderShadow.VertexShaderVertexFormat(fmt, 1, null, 0);

			ShaderShadow.SetVertexShader("shadowmodel");
			ShaderShadow.SetPixelShader("shadowmodel");

			SetStandardShaderUniforms();
		}

		if (shaderAPI != null) {
			BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame);
			SetVertexShaderMatrix3x4(VertexShaderConst.ShaderSpecificConst0, (int)ShaderMaterialVars.BaseTextureTransform);

			Span<float> texOffset = stackalloc float[4];
			vars[BASETEXTUREOFFSET].GetVecValue(texOffset);
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst3, texOffset);

			Span<float> texScale = stackalloc float[4];
			vars[BASETEXTURESCALE].GetVecValue(texScale);
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst4, texScale);

			Span<float> shadow = stackalloc float[4];
			shadow[0] = vars[FALLOFFOFFSET].GetFloatValue();
			shadow[1] = vars[FALLOFFDISTANCE].GetFloatValue() + shadow[0];
			if (shadow[1] != 0.0f)
				shadow[1] = 1.0f / shadow[1];
			shadow[2] = vars[FALLOFFAMOUNT].GetFloatValue();
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst5, shadow);

			SetModulationVertexShaderDynamicState();
		}

		Draw();
	}
}
