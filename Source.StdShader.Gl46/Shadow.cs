using Source.Common;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class Shadow : BaseVSShader
{

	public static string HelpString = "Help for Shadow";
	public static ShaderFlags Flags = ShaderFlags.NotEditable;
	public static List<ShaderParam> ShaderParams = [];
	public static ShaderParam[] ShaderParamOverrides = new ShaderParam[(int)ShaderMaterialVars.Count];

	public class ShaderParam
	{
		public readonly ShaderParamInfo Info;
		public readonly int Index;
		public ShaderParam(ShaderMaterialVars var, ShaderParamType type, ReadOnlySpan<char> defaultParam, ReadOnlySpan<char> help, int flags) {
			Info.Name = "override";
			Info.Type = type;
			Info.DefaultValue = new(defaultParam);
			Info.Help = new(help);
			Info.Flags = (ShaderParamFlags)flags;

			if (ShaderParamOverrides[(int)var] == null) {

			}
			else {
				AssertMsg(false, "ShaderParamOverrides at var index had null value");
			}

			ShaderParamOverrides[(int)var] = this;
			Index = (int)var;
		}
		public ShaderParam(string name, ShaderParamType type, ReadOnlySpan<char> defaultParam, ReadOnlySpan<char> help, int flags = 0) {
			Info.Name = name;
			Info.Type = type;
			Info.DefaultValue = new(defaultParam);
			Info.Help = new(help);
			Info.Flags = (ShaderParamFlags)flags;
			Index = (int)ShaderMaterialVars.Count + ShaderParams.Count;
			ShaderParams.Add(this);
		}
		public static implicit operator int(ShaderParam param) => param.Index;
		public ReadOnlySpan<char> GetName() => Info.Name;
		public ShaderParamType GetType() => Info.Type;
		public ReadOnlySpan<char> GetDefaultValue() => Info.DefaultValue;
		public int GetFlags() => (int)Info.Flags;
		public ReadOnlySpan<char> GetHelp() => Info.Help;
	}

	protected override void OnInitShaderParams(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {

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
		LoadTexture((int)ShaderMaterialVars.BaseTexture, (int)TextureFlags.SRGB);
	}
	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (ShaderShadow != null) {
			ShaderShadow.EnableTexture(Sampler.Sampler0, true);

			EnableAlphaBlending(ShaderBlendFactor.Zero, ShaderBlendFactor.SrcColor);

			VertexFormat flags = VertexFormat.Position | VertexFormat.Color;
			int numTexCoords = 1;
			ShaderShadow.VertexShaderVertexFormat(flags | VertexFormat.TexCoord2D_0, numTexCoords, null, 0);

			ShaderShadow.SetVertexShader("shadow");
			ShaderShadow.SetPixelShader("shadow");

			SetStandardShaderUniforms();
		}

		if (shaderAPI != null) {
			BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame);

			SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst0, (int)ShaderMaterialVars.BaseTextureTransform);
			SetPixelShaderConstant(1, (int)ShaderMaterialVars.Color);

			int width = 16;
			int height = 16;
			ITexture? texture = vars[(int)ShaderMaterialVars.BaseTexture].GetTextureValue();
			if (texture != null) {
				width = texture.GetActualWidth();
				height = texture.GetActualHeight();
			}

			Span<float> jitter = [1.0f / width, 1.0f / height, 0.0f, 0.0f];
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst2, jitter);

			jitter[1] *= -1.0f;
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst3, jitter);
		}

		Draw();
	}
}
