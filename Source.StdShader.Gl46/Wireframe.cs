using Source.Common.MaterialSystem;
using Source.Common.ShaderLib;

namespace Source.StdShader.Gl46;

public class Wireframe : BaseVSShader
{
	public static string HelpString = "Help for Wireframe";
	public static ShaderFlags Flags = ShaderFlags.None;
	protected override void OnInitShaderParams(IMaterialVar[] vars, ReadOnlySpan<char> materialName) {
		vars[(int)ShaderMaterialVars.BaseTexture].SetStringValue("white");
		InitParamsUnlitGeneric((int)ShaderMaterialVars.BaseTexture, -1, -1, -1, -1, -1, -1);
		SetFlags(vars, MaterialVarFlags.NoDebugOverride);
		SetFlags(vars, MaterialVarFlags.NoFog);
		SetFlags(vars, MaterialVarFlags.Wireframe);
	}
	public override string? GetFallbackShader(IMaterialVar[] vars)
		=> null;
	protected override void OnInitShaderInstance(IMaterialVar[] vars, ReadOnlySpan<char> materialName)
		=> InitUnlitGeneric((int)ShaderMaterialVars.BaseTexture, -1, -1, -1);
	protected override void OnDrawElements(IMaterialVar[] vars, IShaderDynamicAPI shaderAPI, VertexCompressionType vertexCompression)
		=> VertexShaderUnlitGenericPass((int)ShaderMaterialVars.BaseTexture, (int)ShaderMaterialVars.Frame, (int)ShaderMaterialVars.BaseTextureTransform, -1, -1, true, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, "unlitgeneric");
}