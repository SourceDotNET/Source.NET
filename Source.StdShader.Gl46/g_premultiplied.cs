#if GMOD_DLL
using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class g_premultiplied : BaseVSShader
{
	public static string HelpString = "Help for g_premultiplied";
	public static ShaderFlags Flags = ShaderFlags.None;

	public override string? GetFallbackShader(IMaterialVar[] vars) => null;
	public override ShaderFlags GetFlags() => Flags;

	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		LoadTexture((int)ShaderMaterialVars.BaseTexture);
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (IsSnapshotting()) {
			ShaderShadow.EnableTexture(Sampler.Sampler0, true);
			ShaderShadow.EnableDepthWrites(false);
			ShaderShadow.VertexShaderVertexFormat(VertexFormat.Position, 1, null, 0);
			ShaderShadow.SetVertexShader("g_screenquad", 0);
			ShaderShadow.SetPixelShader("g_premultiplied", 0);
			ShaderShadow.EnableBlending(true);
			ShaderShadow.BlendFunc(ShaderBlendFactor.One, ShaderBlendFactor.OneMinusSrcAlpha);
			ShaderShadow.EnableBlendingSeparateAlpha(true);
			ShaderShadow.BlendFuncSeparateAlpha(ShaderBlendFactor.SrcAlpha, ShaderBlendFactor.OneMinusSrcAlpha);
			DefaultFog();
		}
		else {
			BindTexture(Sampler.Sampler0, (int)ShaderMaterialVars.BaseTexture, -1);
			shaderAPI.SetVertexShaderIndex(0);
		}
		Draw();
	}
}
#endif
