using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.ShaderLib;

using System.Numerics;

namespace Source.Common.MaterialSystem;


public struct ShaderParamInfo
{
	public string Name;
	public string Help;
	public ShaderParamType Type;
	public string? DefaultValue;
	public ShaderParamFlags Flags;
}


public interface IShader
{
	string? GetName();
	int GetFlags();
	int GetNumParams();
	ReadOnlySpan<char> GetParamName(int paramIndex);
	ReadOnlySpan<char> GetParamHelp(int paramIndex);
	ShaderParamType GetParamType(int paramIndex);
	ReadOnlySpan<char> GetParamDefault(int paramIndex);
	string? GetFallbackShader(IMaterialVar[] vars);
	void InitShaderParams(IMaterialVar[] vars, IShaderAPI shaderAPI, ReadOnlySpan<char> materialName);
	void InitShaderInstance(IMaterialVar[] shaderParams, IShaderAPI shaderAPI, IShaderInit shaderManager, ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName);
	void DrawElements(IMaterialVar[] shaderParams, IShaderShadow? shadow, IShaderDynamicAPI? shaderAPI, VertexCompressionType none, ref BasePerMaterialContextData? contextData);
	ShaderUsingFlags ComputeModulationFlags(IMaterialVar[] shaderParams, IShaderDynamicAPI shaderAPI);
	bool IsTranslucent(IMaterialVar[]? shaderParams);
	bool NeedsPowerOfTwoFrameBufferTexture(IMaterialVar[]? shaderParams, bool checkSpecificToThisFrame);
	bool NeedsFullFrameBufferTexture(IMaterialVar[]? shaderParams, bool checkSpecificToThisFrame);
}

public interface IShaderInit
{
	public void LoadTexture(IMaterialVar textureVar, ReadOnlySpan<char> textureGroupName, int additionalCreationFlags = 0);
	public void LoadCubeMap(IMaterialVar[] parms, IMaterialVar textureVar, int additionalCreationFlags = 0);
	VertexShaderHandle LoadVertexShader(ReadOnlySpan<char> name, ReadOnlySpan<char> defines = default);
	PixelShaderHandle LoadPixelShader(ReadOnlySpan<char> name, ReadOnlySpan<char> defines = default);
	void LoadBumpMap(IMaterialVar nameVar, string? textureGroupName);
}


public enum VertexCompressionType : uint
{
	Invalid = 0xFFFFFFFF,
	None = 0,
	On = 1
}

public struct ShaderViewport
{
	public int TopLeftX;
	public int TopLeftY;
	public int Width;
	public int Height;
	public float MinZ;
	public float MaxZ;

	public ShaderViewport() {

	}

	public ShaderViewport(int x, int y, int width, int height, float minZ = 0.0f, float maxZ = 1.0f) {
		TopLeftX = x;
		TopLeftY = y;
		Width = width;
		Height = height;
		MinZ = minZ;
		MaxZ = maxZ;
	}
}

public ref struct DynamicShaderIndex(IShaderDynamicAPI shaderAPI, ShaderType type)
{
	readonly IShaderDynamicAPI shaderAPI = shaderAPI;
	readonly ShaderType type = type;
	int index = 0;

	public void Set(ReadOnlySpan<char> name, int value) => index += value * shaderAPI.GetDynamicComboScale(type, name);
	public void Set(ReadOnlySpan<char> name, bool value) => Set(name, value ? 1 : 0);
	public readonly int GetIndex() => index;
}

public ref struct StaticShaderIndex(IShaderShadow shaderShadow, ShaderType type, ReadOnlySpan<char> fileName)
{
	readonly IShaderShadow shaderShadow = shaderShadow;
	readonly ShaderType type = type;
	readonly ReadOnlySpan<char> fileName = fileName;
	int index = 0;

	public void Set(ReadOnlySpan<char> name, int value) => index += value * shaderShadow.GetStaticComboScale(type, fileName, name);
	public void Set(ReadOnlySpan<char> name, bool value) => Set(name, value ? 1 : 0);
	public readonly int GetIndex() => index;
}

public struct ShaderColorCorrectionInfo
{
	public bool IsEnabled;
	public int LookupCount;
	public float DefaultWeight;
	public InlineArray4<float> LookupWeights;
}

public interface IShaderDynamicAPI
{
	void SetViewports(ReadOnlySpan<ShaderViewport> viewports);
	int GetViewports(Span<ShaderViewport> viewports);

	// returns the current time in seconds....
	double CurrentTime();

	// Gets the lightmap dimensions
	void GetLightmapDimensions(out int w, out int h);

	// Scene fog state.
	// This is used by the shaders for picking the proper vertex shader for fogging based on dynamic state.
	MaterialFogMode GetSceneFogMode();
	void GetSceneFogColor(Span<byte> rgb);

	// stuff related to matrix stacks
	void MatrixMode(MaterialMatrixMode matrixMode);
	void PushMatrix();
	void PopMatrix();
	void LoadMatrix(in Matrix4x4 m);
	void MultMatrix(in Matrix4x4 m);
	void MultMatrixLocal(in Matrix4x4 m);
	void GetMatrix(MaterialMatrixMode matrixMode, out Matrix4x4 dst);
	void LoadIdentity();
	void LoadCameraToWorld();
	void Ortho(double left, double right, double bottom, double top, double zNear, double zFar);
	void PerspectiveX(double fovx, double aspect, double zNear, double zFar);
	void PickMatrix(int x, int y, int width, int height);
	void Rotate(float angle, float x, float y, float z);
	void Translate(float x, float y, float z);
	void Scale(float x, float y, float z);
	void ScaleXY(float x, float y);

	// Sets the color to modulate by
	void Color3f(float r, float g, float b);
	void Color3fv(ReadOnlySpan<float> color);
	void Color4f(float r, float g, float b, float a);
	void Color4fv(ReadOnlySpan<float> color);

	void Color3ub(byte r, byte g, byte b);
	void Color3ubv(ReadOnlySpan<byte> color);
	void Color4ub(byte r, byte g, byte b, byte a);
	void Color4ubv(ReadOnlySpan<byte> color);

	// Sets the constant register for vertex and pixel shaders
	void SetVertexShaderConstant(int var, ReadOnlySpan<float> vec, int numConst = 1, bool force = false);
	void SetPixelShaderConstant(int var, ReadOnlySpan<float> vec, int numConst = 1, bool force = false);

	// Sets the default *dynamic* state
	void SetDefaultState();

	// Get the current camera position in world space.
	void GetWorldSpaceCameraPosition(Span<float> pos);

	int GetCurrentNumBones();
	int GetCurrentLightCombo();

	MaterialFogMode GetCurrentFogType();

	// fixme: move this to shadow state
	void SetTextureTransformDimension(TextureStage textureStage, int dimension, bool projected);
	void DisableTextureTransform(TextureStage textureStage);
	void SetBumpEnvMatrix(TextureStage textureStage, float m00, float m01, float m10, float m11);

	// Sets the vertex and pixel shaders
	void SetVertexShaderIndex(int vshIndex = -1);
	void SetPixelShaderIndex(int pshIndex = 0);

	// Get the dimensions of the back buffer.
	void GetBackBufferDimensions(out int width, out int height );

	// Get the lights
	int GetMaxLights();
	ref readonly LightDesc GetLight(int lightNum);

	void SetPixelShaderFogParams(int reg);

	// Render state for the ambient light cube
	void SetVertexShaderStateAmbientLightCube();
	void SetPixelShaderStateAmbientLightCube(int reg, bool forceToBlack = false);
	void CommitPixelShaderLighting(int reg);

	// Use this to get the mesh builder that allows us to modify vertex data
	ref MeshBuilder GetVertexModifyBuilder();
	bool InFlashlightMode();
	ref readonly FlashlightState GetFlashlightState(out Matrix4x4 worldToTexture);
	bool InEditorMode();

	// Gets the bound morph's vertex format; returns 0 if no morph is bound
	MorphFormatFlags GetBoundMorphFormat();

	// Binds a standard texture
	void BindStandardTexture(Sampler sampler, StandardTextureId id);

	ITexture? GetRenderTargetEx(int renderTargetID);

	void SetToneMappingScaleLinear( in Vector3 scale );
	 ref readonly Vector3 GetToneMappingScaleLinear();
	float GetLightMapScaleFactor();

	void LoadBoneMatrix(int boneIndex, in Matrix3x4 m);

	void PerspectiveOffCenterX(double fovx, double aspect, double zNear, double zFar, double bottom, double top, double left, double right);

	void SetFloatRenderingParameter(int parmNumber, float value);

	void SetIntRenderingParameter(int parmNumber, int value);
	void SetVectorRenderingParameter(int parmNumber, in Vector3 value);

	 float GetFloatRenderingParameter(int parmNumber);

	int GetIntRenderingParameter(int parmNumber);

	Vector3 GetVectorRenderingParameter(int parmNumber);

	// stencil buffer operations.
	void SetStencilEnable(bool onoff);
	void SetStencilFailOperation(StencilOperation op);
	void SetStencilZFailOperation(StencilOperation op);
	void SetStencilPassOperation(StencilOperation op);
	void SetStencilCompareFunction(StencilComparisonFunction cmpfn);
	void SetStencilReferenceValue(int reference);
	void SetStencilTestMask(uint msk);
	void SetStencilWriteMask(uint msk);
	void ClearStencilBufferRectangle(int xmin, int ymin, int xmax, int ymax, int value);

	void GetDXLevelDefaults(out GraphicsDriver max, out GraphicsDriver recommended);

	ref readonly FlashlightState GetFlashlightStateEx(out Matrix4x4 worldToTexture, out ITexture? flashlightDepthTexture);

	float GetAmbientLightCubeLuminance();

	void GetDX9LightState(out LightState state);
	int GetPixelFogCombo(); //0 is either range fog, or no fog simulated with rigged range fog values. 1 is height fog
	int GetPixelFogCombo1(bool supportsRadial);

	void BindStandardVertexTexture(VertexTextureSampler sampler, StandardTextureId id);

	// Is hardware morphing enabled?
	bool IsHWMorphingEnabled();

	void GetStandardTextureDimensions(out int width, out int height, StandardTextureId id);

	void SetBooleanVertexShaderConstant(int var, ReadOnlySpan<bool> vec, bool force = false );
	void SetIntegerVertexShaderConstant(int var, ReadOnlySpan<int> vec,  bool force = false );
	void SetBooleanPixelShaderConstant(int var, ReadOnlySpan<bool> vec, bool force = false );
	void SetIntegerPixelShaderConstant(int var, ReadOnlySpan<int> vec, bool force = false );

	//Are we in a configuration that needs access to depth data through the alpha channel later?
	bool ShouldWriteDepthToDestAlpha();


	// deformations
	void PushDeformation(in DeformationBase deformation );
	void PopDeformation();
	int GetNumActiveDeformations();


	// for shaders to set vertex shader constants. returns a packed state which can be used to set
	// the dynamic combo. returns # of active deformations
	int GetPackedDeformationInformation(int maskOfUnderstoodDeformations,
												Span<float> constantValuesOut,
												int bufferSize,
												int maximumDeformations,
												out int numDefsOut);

	// This lets the lower level system that certain vertex fields requested 
	// in the shadow state aren't actually being read given particular state
	// known only at dynamic state time. It's here only to silence warnings.
	void MarkUnusedVertexFields(uint flags, Span<bool> unusedTexCoords);


	void ExecuteCommandBuffer(Span<byte> cmdBuffer);

	// interface for mat system to tell shaderapi about standard texture handles
	void SetStandardTextureHandle(StandardTextureId id, ShaderAPITextureHandle_t handle);

	// Interface for mat system to tell shaderapi about color correction
	void GetCurrentColorCorrection(out ShaderColorCorrectionInfo info);

	void SetPSNearAndFarZ(int reg);

	void SetDepthFeatheringPixelShaderConstant(int constant, float depthBlendScale);

#if GMOD_DLL
	void GMOD_SamplerBorderClamp(Sampler sampler);
#endif

	int GetDynamicComboScale(ShaderType type, ReadOnlySpan<char> name);
	int LocateShaderUniform(ReadOnlySpan<char> name);
	void SetShaderUniform(int uniform, int integer);
	void SetShaderUniform(int uniform, float fl);
	void SetShaderUniform(int uniform, ReadOnlySpan<float> flConsts);
	void SetShaderUniform(IMaterialVar variable);
	nint GetCurrentProgram();
	GraphicsDriver GetDriver();
	void ExecuteCommandBuffer(ICommandStorageBuffer storage);
}

public struct LightState
{
	public int NumLights;
	public bool AmbientLight;
	public bool StaticLightVertex;
	public bool StaticLightTexel;
	public readonly int HasDynamicLight() => (AmbientLight || (NumLights > 0)) ? 1 : 0;
}

public class BasePerMaterialContextData()
{
	public UInt32 VarChangeID = 0xffffffff;
	public bool MaterialVarsChanged = true;
}
