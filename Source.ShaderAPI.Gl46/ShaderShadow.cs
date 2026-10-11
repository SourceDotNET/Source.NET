using Source.Common.MaterialSystem;
using Source.Common.ShaderAPI;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Source.ShaderAPI.Gl46;

/// <summary>
/// Shared uniforms between both types of shaders.
/// </summary>
public struct SourceSharedShadowState
{

}

/// <summary>
/// Uniforms for the vertex shader the ShadowState represents.
/// </summary>
public struct SourceVertexSharedShadowState
{
	public int NumBones;
	public int LightCount;
	public int Pad0;
	public int Pad1;
	public Vector4 LightEnabled;
}

/// <summary>
/// Uniforms for the pixel shader the ShadowState represents.
/// </summary>
public struct SourcePixelSharedShadowState
{
	public int IsAlphaTesting;
	public int AlphaTestFunc;
	public float AlphaTestRef;
}

/// <summary>
/// A shader state. Represents the board (GL state machine) and shader uniforms together.
/// During shader initialization/recomputes, this state is recalculated based on input variables, etc.
/// </summary>
public class ShadowStateGl46 : IShaderShadow
{
	internal readonly IShaderSystemInternal Shaders;
	internal readonly ShaderAPIGl46 ShaderAPI;
	readonly IMaterialSystemHardwareConfig HardwareConfig = Singleton<IMaterialSystemHardwareConfig>();

	public uint BASE_UBO;
	public uint VERTEX_UBO;
	public uint PIXEL_UBO;

	public GraphicsBoardState State;
	public SourceSharedShadowState Base;
	public SourceVertexSharedShadowState Vertex;
	public SourcePixelSharedShadowState Pixel;

	public VertexShaderHandle VertexShader;
	public PixelShaderHandle PixelShader;

	public BasePerMaterialContextData? ContextData;
	public ShaderUsingFlags ModulationFlags;

	List<IMaterialVar> shaderUniforms = [];

	public void SetShaderUniform(IMaterialVar textureVar) {
		shaderUniforms.Add(textureVar);
	}
	public void ActivateShaderUniforms() {
		foreach (var var in shaderUniforms) {
			ShaderAPI.SetShaderUniform(var);
		}
	}
	private static unsafe int SizeAligned<T>(int alignment = 16) where T : unmanaged {
		var size = sizeof(T);
		var a = alignment - (size % alignment);
		return size + a;
	}

	string? name;
	public unsafe ShadowStateGl46(ShaderAPIGl46 shaderAPI, IShaderSystemInternal shaderSystem, ReadOnlySpan<char> name = default) {
		ShaderAPI = shaderAPI;
		Shaders = shaderSystem;
		this.name = name.IsEmpty ? null : new(name);

		if (shaderAPI.IsActive()) {
			CreateShaderObjects();
		}
	}

	public VertexFormat GetVertexFormat() => VertexFormat;

	bool createdShaderObjects = false;
	private unsafe void CreateShaderObjects() {
		if (createdShaderObjects)
			return;

		BASE_UBO = glCreateBuffer();
		glObjectLabel(GL_BUFFER, BASE_UBO, $"ShaderAPI ShadowState[base] '{name}'");
		glNamedBufferData(BASE_UBO, SizeAligned<SourceSharedShadowState>(), null, GL_DYNAMIC_DRAW);

		VERTEX_UBO = glCreateBuffer();
		glObjectLabel(GL_BUFFER, VERTEX_UBO, $"ShaderAPI ShadowState[vertex] '{name}'");
		glNamedBufferData(VERTEX_UBO, SizeAligned<SourceVertexSharedShadowState>(), null, GL_DYNAMIC_DRAW);

		PIXEL_UBO = glCreateBuffer();
		glObjectLabel(GL_BUFFER, PIXEL_UBO, $"ShaderAPI ShadowState[pixel] '{name}'");
		glNamedBufferData(PIXEL_UBO, SizeAligned<SourcePixelSharedShadowState>(), null, GL_DYNAMIC_DRAW);

		createdShaderObjects = true;
	}

	bool needsBufferUpload = true;
	internal VertexFormat VertexFormat;

	public unsafe void Dispose() {
		if (!ThreadInMainThread()) {
			Warning("NOT IN MAIN THREAD - CANNOT DELETE UBO - GRAPHICS MEMORY LEAK\n");
			return;
		}

		glDeleteBuffers(BASE_UBO, VERTEX_UBO, PIXEL_UBO);
	}

	ShaderFlags Flags;
	public ShaderFlags GetFlags() => Flags;
	public void SetFlags(ShaderFlags flags) => Flags = flags;

	public unsafe void Activate() {
		CreateShaderObjects(); // Recreate UBO's, if we were lazy-loaded
		ReuploadBuffers(); // Reupload UBO's, if needed

		ComputeAggregateShadowState();

		// Set GL states. We compare our last upload state to the current desired state and adjust if it differs.
		ShaderAPI.SetBoardState(in State);

		// Set VSH and PSH. Shader API can bind these whenever it needs to
		ShaderAPI.SetCurrentShadow(this);
		ShaderAPI!.BindVertexShader(VertexShader);
		ShaderAPI!.BindPixelShader(PixelShader);

		// Bind UBO binding locations to their respective ranges in our UBO object
		UniformBufferBindings.Bind(UniformBufferBindingLocation.SharedBaseShader, BASE_UBO);
		UniformBufferBindings.Bind(UniformBufferBindingLocation.SharedVertexShader, VERTEX_UBO);
		UniformBufferBindings.Bind(UniformBufferBindingLocation.SharedPixelShader, PIXEL_UBO);

		// Activate per-shader-instance uniforms...
		ActivateShaderUniforms();

		// And now the shader shadow state is activated
	}

	private unsafe void ReuploadBuffers() {
		SourceVertexSharedShadowState curVertex = ((ShaderAPIGl46)ShaderAPI).GetVertexSharedState();
		curVertex.NumBones = ShaderAPI.GetCurrentNumBones();
		if (curVertex.NumBones != Vertex.NumBones || curVertex.LightCount != Vertex.LightCount || curVertex.LightEnabled != Vertex.LightEnabled)
			needsBufferUpload = true;

		if (!needsBufferUpload)
			return;

		// Reupload UBO states.
		Vertex = curVertex;

		fixed (SourceSharedShadowState* pBase = &Base)
		fixed (SourceVertexSharedShadowState* pVertex = &Vertex)
		fixed (SourcePixelSharedShadowState* pPixel = &Pixel) {
			glNamedBufferData(BASE_UBO, SizeAligned<SourceSharedShadowState>(), pBase, GL_DYNAMIC_DRAW);
			glNamedBufferData(VERTEX_UBO, SizeAligned<SourceVertexSharedShadowState>(), pVertex, GL_DYNAMIC_DRAW);
			glNamedBufferData(PIXEL_UBO, SizeAligned<SourcePixelSharedShadowState>(), pPixel, GL_DYNAMIC_DRAW);
		}

		needsBufferUpload = false;
	}

	public void DepthFunc(ShaderDepthFunc depthFunc) {
		State.DepthFunc = depthFunc;
	}

	public void EnableDepthWrites(bool enable) {
		State.DepthWrite = enable;
	}

	public void EnableDepthTest(bool enable) {
		State.DepthTest = enable;
	}

	public void EnablePolyOffset(PolygonOffsetMode offsetMode) {
		State.ZBias = offsetMode;
	}

	public void EnableColorWrites(bool enable) {
		State.ColorWrite = enable;
	}

	public void EnableAlphaWrites(bool enable) {
		State.AlphaWrite = enable;
	}

	public void EnableBlending(bool enable) {
		State.Blending = enable;
	}

	public void BlendFunc(ShaderBlendFactor srcFactor, ShaderBlendFactor dstFactor) {
		State.SourceBlend = srcFactor;
		State.DestinationBlend = dstFactor;
	}

	public void EnableAlphaTest(bool enable) {
		int enableI = enable ? 1 : 0;
		if (Pixel.IsAlphaTesting != enableI) {
			Pixel.IsAlphaTesting = enableI;
			needsBufferUpload = true;
		}
	}

	public void AlphaFunc(ShaderAlphaFunc alphaFunc, float alphaRef) {
		int alphaFuncI = (int)alphaFunc;

		if (Pixel.AlphaTestFunc != alphaFuncI) {
			Pixel.AlphaTestFunc = alphaFuncI;
			needsBufferUpload = true;
		}

		if (Pixel.AlphaTestRef != alphaRef) {
			Pixel.AlphaTestRef = alphaRef;
			needsBufferUpload = true;
		}
	}

	public void PolyMode(ShaderPolyModeFace face, ShaderPolyMode polyMode) {
		if (face == ShaderPolyModeFace.Back)
			return;

		State.FillMode = polyMode;
	}

	public void EnableCulling(bool enable) {
		State.CullEnable = enable;
	}

	public void EnableConstantColor(bool enable) {
		throw new NotImplementedException();
	}

	public void VertexShaderVertexFormat(VertexFormat format, int texCoordCount, Span<int> texCoordDimensions, int userDataSize) {
		VertexFormat = ((ShaderAPIGl46)ShaderAPI).MeshMgr.ComputeVertexFormat(format | VertexFormat.BoneIndex | VertexFormat.BoneWeights2 | VertexFormat.UserData4, texCoordCount, texCoordDimensions, 0, userDataSize);
	}

	public GraphicsDriver GetDriver() => ShaderAPI.GetDriver();

	ShaderComboState? vertexCombos;
	ShaderComboState? pixelCombos;

	private ShaderComboState VertexCombos => vertexCombos ??= new(Shaders, ShaderType.Vertex);
	private ShaderComboState PixelCombos => pixelCombos ??= new(Shaders, ShaderType.Pixel);

	public void SetVertexShader(ReadOnlySpan<char> fileName, int staticIndex = 0) {
		VertexShader = VertexCombos.SetShader($"{fileName}_{GetDriver().Extension(ShaderType.Vertex)}", staticIndex);
	}

	public void SetPixelShader(ReadOnlySpan<char> fileName, int staticIndex = 0) {
		PixelShader = PixelCombos.SetShader($"{fileName}_{GetDriver().Extension(ShaderType.Pixel)}", staticIndex);
	}

	private ShaderComboState Combos(ShaderType type) => type == ShaderType.Vertex ? VertexCombos : PixelCombos;
	public int GetStaticComboScale(ShaderType type, ReadOnlySpan<char> fileName, ReadOnlySpan<char> name) => Combos(type).GetStaticComboScale($"{fileName}_{GetDriver().Extension(type)}", name);
	internal int GetDynamicComboScale(ShaderType type, ReadOnlySpan<char> name) => Combos(type).GetDynamicComboScale(name);

	internal VertexShaderHandle GetVertexShaderVariant(int dynamicIndex) => VertexCombos.GetVariant(dynamicIndex);
	internal PixelShaderHandle GetPixelShaderVariant(int dynamicIndex) => PixelCombos.GetVariant(dynamicIndex);

	public void EnableVertexBlend(bool enable) {
		throw new NotImplementedException();
	}

	public void OverbrightValue(TextureStage stage, float value) {
		throw new NotImplementedException();
	}

	bool[] samplerState = new bool[(int)Sampler.MaxSamplers];

	public void EnableTexture(Sampler sampler, bool enable) {
		if ((int)sampler < 16) {
			samplerState[(int)sampler] = enable;
		}
		else {
			Warning($"Attempting to bind a texture to an invalid sampler {(int)sampler}!\n");
		}
	}

	public void EnableTexGen(TextureStage stage, bool enable) {
		throw new NotImplementedException();
	}

	public void TexGen(TextureStage stage, ShaderTexGenParam param) {
		throw new NotImplementedException();
	}

	public void EnableCustomPixelPipe(bool enable) {
		throw new NotImplementedException();
	}

	public void CustomTextureStages(int stageCount) {
		throw new NotImplementedException();
	}

	public void CustomTextureOperation(TextureStage stage, ShaderTexChannel channel, ShaderTexOp op, ShaderTexArg arg1, ShaderTexArg arg2) {
		throw new NotImplementedException();
	}

	public void EnableAlphaPipe(bool enable) {
		throw new NotImplementedException();
	}

	public void EnableConstantAlpha(bool enable) {
		throw new NotImplementedException();
	}

	public void EnableVertexAlpha(bool enable) {
		throw new NotImplementedException();
	}

	public void EnableTextureAlpha(TextureStage stage, bool enable) {
		throw new NotImplementedException();
	}

	public void EnableBlendingSeparateAlpha(bool enable) {
		State.AlphaSeparateBlend = enable;
	}

	public void BlendFuncSeparateAlpha(ShaderBlendFactor srcFactor, ShaderBlendFactor dstFactor) {
		State.AlphaSourceBlend = srcFactor;
		State.AlphaDestinationBlend = dstFactor;
	}

	public void ComputeAggregateShadowState() {
		// Alpha to coverage
		if (State.AlphaToCoverage) {
			// Only allow this to be enabled if blending is disabled and testing is enabled
			if ((State.Blending == true) || (Pixel.IsAlphaTesting == 0))
				State.AlphaToCoverage = false;
		}
	}

	public ShaderFogMode FogModeState;
	public bool DisableFogGammaCorrectionState;

	public void FogMode(ShaderFogMode fogMode) {
		Assert(fogMode >= 0 && fogMode < ShaderFogMode.Num);
		FogModeState = fogMode;
	}

	public void SetDiffuseMaterialSource(ShaderMaterialSource materialSource) {
		throw new NotImplementedException();
	}

	public void DisableFogGammaCorrection(bool bDisable) {
		DisableFogGammaCorrectionState = bDisable;
	}

	public void EnableAlphaToCoverage(bool enable) {
		State.AlphaToCoverage = enable;
	}

	public void SetShadowDepthFiltering(Sampler stage) {
		// throw new NotImplementedException();
		// TODO!
	}

	public void BlendOp(ShaderBlendOp blendOp) {
		State.BlendOperation = blendOp;
	}

	public void BlendOpSeparateAlpha(ShaderBlendOp blendOp) {
		State.AlphaBlendOperation = blendOp;
	}

	public void SetDefaultState() {
		DepthFunc(ShaderDepthFunc.NearerOrEqual);
		EnableColorWrites(true);
		EnableAlphaWrites(false);
		EnableDepthWrites(true);
		EnableDepthTest(true);
		EnableAlphaTest(false);
		EnableBlending(false);
		EnableCulling(true);
		PolyMode(ShaderPolyModeFace.FrontAndBack, ShaderPolyMode.Fill);
		BlendFunc(ShaderBlendFactor.One, ShaderBlendFactor.Zero);
		BlendOp(ShaderBlendOp.Add);
		EnableBlendingSeparateAlpha(false);
		BlendFuncSeparateAlpha(ShaderBlendFactor.One, ShaderBlendFactor.Zero);
		BlendOpSeparateAlpha(ShaderBlendOp.Add);
		AlphaFunc(ShaderAlphaFunc.GreaterEqual, 0.7f);
		EnableAlphaToCoverage(false);
		EnableSRGBWrite(false);
		EnablePolyOffset(PolygonOffsetMode.Disable);
		FogMode(ShaderFogMode.Disabled);
		DisableFogGammaCorrection(false);

		int samplerCount = HardwareConfig.GetSamplerCount();
		for (int i = 0; i < samplerCount; i++) {
			EnableTexture((Sampler)i, false);
			EnableSRGBRead((Sampler)i, false);
		}
	}

	public void EnableSRGBRead(Sampler sampler, bool enable) {
		if (!HardwareConfig.SupportsSRGB()) {
			State.SamplerState[(int)sampler].SRGBReadEnable = false;
			return;
		}

		if ((int)sampler < HardwareConfig.GetSamplerCount())
			State.SamplerState[(int)sampler].SRGBReadEnable = enable;
		else
			Warning($"Attempting set SRGBRead state on an invalid sampler ({(int)sampler})!\n");
	}

	public void EnableSRGBWrite(bool enable) {
		if (HardwareConfig.SupportsSRGB())
			State.SRGBWriteEnable = enable;
		else
			State.SRGBWriteEnable = false;
	}
}

internal sealed class ShaderComboState(IShaderSystemInternal shaders, ShaderType type)
{
	string? file;
	ShaderCombo[] staticCombos = [];
	ShaderCombo[] dynamicCombos = [];
	int[] staticComboScales = [];
	int[] dynamicComboScales = [];
	int numDynamicCombos = 1;
	int staticComboIndex;
	readonly Dictionary<int, nint> variants = [];
	readonly StringBuilder defines = new(256);

	public nint SetShader(string fileName, int staticIndex) {
		file = fileName;
		staticComboIndex = staticIndex;

		var (statics, dynamics) = ((ShaderSystem)shaders).GetShaderCombos(fileName);

		staticCombos = [.. statics];
		dynamicCombos = [.. dynamics];

		staticComboScales = ComputeComboScales(staticCombos, out _);
		dynamicComboScales = ComputeComboScales(dynamicCombos, out numDynamicCombos);

		variants.Clear();

		return GetVariant(0);
	}

	private static int[] ComputeComboScales(ShaderCombo[] combos, out int total) {
		int[] scales = new int[combos.Length];
		total = 1;
		for (int i = 0; i < combos.Length; i++) {
			scales[i] = total;
			total *= combos[i].Range;
		}
		return scales;
	}

	public int GetStaticComboScale(string fileName, ReadOnlySpan<char> name) {
		var (statics, _) = ((ShaderSystem)shaders).GetShaderCombos(fileName);
		int scale = 1;
		for (int i = 0; i < statics.Count; i++) {
			if (name.SequenceEqual(statics[i].Name))
				return scale;
			scale *= statics[i].Range;
		}
		return 0;
	}

	public int GetDynamicComboScale(ReadOnlySpan<char> name) {
		for (int i = 0; i < dynamicCombos.Length; i++) {
			if (name.SequenceEqual(dynamicCombos[i].Name))
				return dynamicComboScales[i];
		}
		return 0;
	}

	private static void AppendComboDefines(StringBuilder defines, ShaderCombo[] combos, int[] scales, int index) {
		for (int i = 0; i < combos.Length; i++) {
			if (defines.Length != 0)
				defines.Append(';');

			ref readonly var combo = ref combos[i];

			defines.Append(combo.Name);
			defines.Append(' ');
			defines.Append(combo.Min + (index / scales[i]) % combo.Range);
		}
	}

	private nint Compile(int variant) {
		int staticIndex = variant / numDynamicCombos;
		int dynamicIndex = variant % numDynamicCombos;

		defines.Clear();
		AppendComboDefines(defines, staticCombos, staticComboScales, staticIndex);
		AppendComboDefines(defines, dynamicCombos, dynamicComboScales, dynamicIndex);

		string source = defines.ToString();
		return type == ShaderType.Vertex ? (nint)shaders.LoadVertexShader(file!, source) : shaders.LoadPixelShader(file!, source);
	}

	public nint GetVariant(int dynamicIndex) {
		int variant = staticComboIndex * numDynamicCombos + dynamicIndex;
		if (!variants.TryGetValue(variant, out nint handle)) {
			handle = Compile(variant);
			variants.Add(variant, handle);
		}
		return handle;
	}
}
