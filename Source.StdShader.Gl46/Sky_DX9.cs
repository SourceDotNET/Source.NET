using Source.Common;
using Source.Common.Bitmap;
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class Sky_DX9 : BaseVSShader
{
	public static string HelpString = "Help for Sky_DX9 shader";
	public static int Flags = 0;

	public override string? GetFallbackShader(IMaterialVar[] vars) => null;

	protected override void OnInitShaderParams(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		SetFlags(vars, MaterialVarFlags.NoFog);
		SetFlags(vars, MaterialVarFlags.IgnoreZ);
	}

	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		if (vars[(int)ShaderMaterialVars.BaseTexture].IsDefined()) {
			ImageFormat fmt = vars[(int)ShaderMaterialVars.BaseTexture].GetTextureValue()!.GetImageFormat();
			LoadTexture((int)ShaderMaterialVars.BaseTexture, (fmt == ImageFormat.RGBA16161616F) || (fmt == ImageFormat.RGBA16161616) ? 0 : (int)TextureFlags.SRGB);
		}
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (IsSnapshotting()) {
			SetInitialShadowState();

			ShaderShadow.EnableTexture(Sampler.Sampler0, true);
			ITexture txtr = vars[(int)ShaderMaterialVars.BaseTexture].GetTextureValue()!;
			ImageFormat fmt = txtr.GetImageFormat();
			if ((fmt == ImageFormat.RGBA16161616F) || (fmt == ImageFormat.RGBA16161616))
				ShaderShadow.EnableSRGBRead(Sampler.Sampler0, false);
			else
				ShaderShadow.EnableSRGBRead(Sampler.Sampler0, true);

			ShaderShadow.VertexShaderVertexFormat(VertexFormat.Position, 1, null, 0);

			StaticShaderIndex vshIndex = new(ShaderShadow, ShaderType.Vertex, "sky");
			ShaderShadow.SetVertexShader("sky", vshIndex.GetIndex());

			StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "sky");
			ShaderShadow.SetPixelShader("sky", pshIndex.GetIndex());

			ShaderShadow.EnableSRGBWrite(true);

			ShaderShadow.EnableAlphaWrites(true);
		}
		else {
			BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame);
			Span<float> c1 = [0, 0, 0, 0];
			shaderAPI.SetVertexShaderConstant(VertexShaderConst.ShaderSpecificConst0, c1);

			Span<float> c0 = [1, 1, 1, 1];
			if (vars[(int)ShaderMaterialVars.Color].IsDefined())
				vars[(int)ShaderMaterialVars.Color].GetVecValue(c0[..3]);
			ITexture txtr = vars[(int)ShaderMaterialVars.BaseTexture].GetTextureValue()!;
			ImageFormat fmt = txtr.GetImageFormat();
			if ((fmt == ImageFormat.RGBA16161616) ||
				((fmt == ImageFormat.RGBA16161616F) &&
				 (HardwareConfig.GetHDRType() == HDRType.Integer))) {
				c0[0] *= 16.0f;
				c0[1] *= 16.0f;
				c0[2] *= 16.0f;
			}
			shaderAPI.SetPixelShaderConstant(0, c0, 1);
			DynamicShaderIndex vshIndex = new(shaderAPI, ShaderType.Vertex);
			shaderAPI.SetVertexShaderIndex(vshIndex.GetIndex());

			SetVertexShaderTextureTransform(VertexShaderConst.ShaderSpecificConst1, (int)ShaderMaterialVars.BaseTextureTransform);

			DynamicShaderIndex pshIndex = new(shaderAPI, ShaderType.Pixel);
			pshIndex.Set("WRITE_DEPTH_TO_DESTALPHA", shaderAPI.ShouldWriteDepthToDestAlpha());
			shaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
		}
		Draw();
	}
}
