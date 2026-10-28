
using Source.Common.ShaderAPI;

namespace Source.Common.MaterialSystem;

[Flags]
public enum ShaderUsingFlags
{
	ColorModulation = 0x1,
	AlphaModulation = 0x2,
	Flashlight = 0x4,
	FixedFunctionBakedLighting = 0x8,
	Editor = 0x10,
}

public enum ShaderCompileState
{
	Pending,
	Ready,
	Failed
}

public interface IShaderSystem
{
	public static bool IsTranslucent(IShaderShadow renderState) {
		return (renderState.GetFlags() & ShaderRenderStateFlags.OpacityTranslucent) != 0;
	}

	void BindTexture(Sampler sampler, ITexture texture, int frame = 0);
	void Draw(bool makeActualDrawCall = true);
	void Init();

	void LoadAllShaderDLLs();
	bool LoadShaderDLL<T>(T instance) where T : IShaderDLL;
	IShader? FindShader(ReadOnlySpan<char> shaderName);

	void InitShaderParameters(IShader shader, IMaterialVar[] vars, ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName);
	void InitShaderInstance(IShader shader, IMaterialVar[]? shaderParams, ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName);
	bool InitRenderState(IShader shader, IMaterialVar[] shaderParams, ref IShaderShadow shaderRenderState, ReadOnlySpan<char> materialName);
	void ResetShaderState();
	void CleanupRenderState(IShaderShadow? renderState);
	void DrawElements(IShader shader, IMaterialVar[] parms, IShaderShadow renderState, VertexCompressionType vertexCompression, uint materialVarTimeStamp);
	void BeginFrame();
	void QueueAllShaderCombos();
	void WaitForPendingCompiles(Action<int, int>? progress);
	IEnumerable<IShader> GetShaders();
	ReadOnlySpan<char> ShaderStateString(int i);
	int GetShaderAPITextureBindHandle(ITexture? texture, int v, int textureChannel);
}
public interface IShaderDLL
{
	public IEnumerable<IShader> GetShaders();
}
