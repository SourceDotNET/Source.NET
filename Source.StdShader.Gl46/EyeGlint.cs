using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class EyeGlint : BaseVSShader
{
	public static string HelpString = "Help for EyeGlint";
	public static ShaderFlags Flags = ShaderFlags.None;

	public override string? GetFallbackShader(IMaterialVar[] vars) => null;

	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
	}

	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression) {
		if (IsSnapshotting()) {
			ShaderShadow!.EnableDepthWrites(false);

			ShaderShadow.EnableBlending(true);
			ShaderShadow.BlendFunc(ShaderBlendFactor.One, ShaderBlendFactor.One);

			Span<int> texCoords = [2, 2, 3];
			ShaderShadow.VertexShaderVertexFormat(VertexFormat.Position, 3, texCoords, 0);

			ShaderShadow.EnableCulling(false);

			ShaderShadow.EnableSRGBWrite(false);

			StaticShaderIndex vshIndex = new(ShaderShadow, ShaderType.Vertex, "eyeglint");
			ShaderShadow.SetVertexShader("eyeglint", vshIndex.GetIndex());

			StaticShaderIndex pshIndex = new(ShaderShadow, ShaderType.Pixel, "eyeglint");
			ShaderShadow.SetPixelShader("eyeglint", pshIndex.GetIndex());
		}
		else {
			DynamicShaderIndex vshIndex = new(ShaderAPI!, ShaderType.Vertex);
			ShaderAPI!.SetVertexShaderIndex(vshIndex.GetIndex());

			DynamicShaderIndex pshIndex = new(ShaderAPI, ShaderType.Pixel);
			ShaderAPI.SetPixelShaderIndex(pshIndex.GetIndex());
		}
		Draw();
	}
}
