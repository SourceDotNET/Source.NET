using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class BlurFilterY : BaseVSShader
{
	public static string HelpString = "Help for BlurFilterY";
	public static int Flags = (int)ShaderParamFlags.NotEditable;
	public static List<ShaderParam> ShaderParams = [];

	public class ShaderParam
	{
		public readonly ShaderParamInfo Info;
		public readonly int Index;
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

	public static readonly ShaderParam BLOOMAMOUNT = new($"${nameof(BLOOMAMOUNT)}", ShaderParamType.Float, "1.0", "");
	public static readonly ShaderParam FRAMETEXTURE = new($"${nameof(FRAMETEXTURE)}", ShaderParamType.Texture, "_rt_SmallHDR0", "");

	public override string? GetFallbackShader(IMaterialVar[] vars) => null;
	public override int GetFlags() => Flags;
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

	protected override void OnInitShaderParams(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		if (!vars[BLOOMAMOUNT].IsDefined())
			vars[BLOOMAMOUNT].SetFloatValue(1.0f);
	}

	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		if (vars[(int)ShaderMaterialVars.BaseTexture].IsDefined())
			LoadTexture((int)ShaderMaterialVars.BaseTexture);
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (IsSnapshotting()) {
			ShaderShadow.EnableDepthWrites(false);
			ShaderShadow.EnableAlphaWrites(true);
			ShaderShadow.EnableTexture(Sampler.Sampler0, true);
			ShaderShadow.VertexShaderVertexFormat(VertexFormat.Position, 1, null, 0);

			ShaderShadow.EnableSRGBRead(Sampler.Sampler0, false);
			ShaderShadow.EnableSRGBWrite(false);

			StaticShaderIndex vshIndex = new(ShaderShadow, ShaderType.Vertex, "blurfilter");
			ShaderShadow.SetVertexShader("blurfilter", vshIndex.GetIndex());

			StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "blurfilter");
			ShaderShadow.SetPixelShader("blurfilter", pshIndex.GetIndex());

			if (IsFlagSet(vars, MaterialVarFlags.Additive))
				EnableAlphaBlending(ShaderBlendFactor.One, ShaderBlendFactor.One);
		}
		else {
			BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, -1);

			ITexture srcTexture = vars[(int)ShaderMaterialVars.BaseTexture].GetTextureValue()!;
			int height = srcTexture.GetActualWidth();
			float dY = 1.0f / height;
			Span<float> v = [0, 0, 0, 0];

			v[0] = 0.0f;
			v[1] = 1.3366f * dY;
			v[2] = 0;
			v[3] = 0;
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst0, v, 1);
			v[0] = 0.0f;
			v[1] = 3.4295f * dY;
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst1, v, 1);
			v[0] = 0.0f;
			v[1] = 5.4264f * dY;
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst2, v, 1);

			v[0] = 0.0f;
			v[1] = 7.4359f * dY;
			shaderAPI.SetPixelShaderConstant(0, v, 1);
			v[0] = 0.0f;
			v[1] = 9.4436f * dY;
			shaderAPI.SetPixelShaderConstant(1, v, 1);
			v[0] = 0.0f;
			v[1] = 11.4401f * dY;
			shaderAPI.SetPixelShaderConstant(2, v, 1);

			v[0] = v[1] = v[2] = vars[BLOOMAMOUNT].GetFloatValue();

			shaderAPI.SetPixelShaderConstant(3, v, 1);

			DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
			shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

			DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
			shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
		}
		Draw();
	}
}
