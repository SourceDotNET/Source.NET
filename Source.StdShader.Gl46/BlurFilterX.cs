using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class BlurFilterX : BaseVSShader
{
	public static string HelpString = "Help for BlurFilterX";
	public static int Flags = (int)ShaderParamFlags.NotEditable;

	public override string? GetFallbackShader(IMaterialVar[] vars) => null;
	public override int GetFlags() => Flags;

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

			ShaderShadow.SetVertexShader("blurfilter", 0);

			StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "blurfilter");
			ShaderShadow.SetPixelShader("blurfilter", pshIndex.GetIndex());

			if (IsFlagSet(vars, MaterialVarFlags.Additive))
				EnableAlphaBlending(ShaderBlendFactor.One, ShaderBlendFactor.One);
		}
		else {
			BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, -1);

			Span<float> v = [0, 0, 0, 0];

			ITexture srcTexture = vars[(int)ShaderMaterialVars.BaseTexture].GetTextureValue()!;
			int width = srcTexture.GetActualWidth();
			float dX = 1.0f / width;

			v[0] = 1.3366f * dX;
			v[1] = 0.0f;
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst0, v, 1);
			v[0] = 3.4295f * dX;
			v[1] = 0.0f;
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst1, v, 1);
			v[0] = 5.4264f * dX;
			v[1] = 0.0f;
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst2, v, 1);

			v[0] = 7.4359f * dX;
			v[1] = 0.0f;
			shaderAPI.SetPixelShaderConstant(0, v, 1);
			v[0] = 9.4436f * dX;
			v[1] = 0.0f;
			shaderAPI.SetPixelShaderConstant(1, v, 1);
			v[0] = 11.4401f * dX;
			v[1] = 0.0f;
			shaderAPI.SetPixelShaderConstant(2, v, 1);
			v[0] = v[1] = v[2] = v[3] = 1.0f;
			shaderAPI.SetPixelShaderConstant(3, v, 1);

			shaderAPI.SetVertexShaderIndex(0);

			DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
			shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
		}
		Draw();
	}
}
