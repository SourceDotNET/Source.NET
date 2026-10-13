using Source.Common.Bitmap;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.Common.ShaderAPI;

public enum CreateTextureFlags
{
	Cubemap = 0x0001,
	RenderTarget = 0x0002,
	Managed = 0x0004,
	DepthBuffer = 0x0008,
	Dynamic = 0x0010,
	AutoMipmap = 0x0020,
	VertexTexture = 0x0040,
	SysMem = 0x0200,
	UnfilterableOK = 0x1000,
	SRGB = 0x4000,
}

public struct SamplerShadowState
{
	public bool SRGBReadEnable;
}

[InlineArray((int)Sampler.MaxSamplers)]
public struct SamplerShadowStates
{
	private SamplerShadowState element;
}

/// <summary>
/// A basic representation of the graphics state machine
/// </summary>
public struct GraphicsBoardState
{
	public SamplerShadowStates SamplerState;

	public bool Blending;
	public ShaderBlendFactor SourceBlend;
	public ShaderBlendFactor DestinationBlend;
	public ShaderBlendOp BlendOperation;

	public bool AlphaSeparateBlend;
	public ShaderBlendFactor AlphaSourceBlend;
	public ShaderBlendFactor AlphaDestinationBlend;
	public ShaderBlendOp AlphaBlendOperation;

	public bool DepthTest;
	public bool ColorWrite;
	public bool AlphaWrite;
	public bool DepthWrite;
	public bool CullEnable;
	public bool AlphaToCoverage;
	public bool SRGBWriteEnable;

	public ShaderDepthFunc DepthFunc;
	public ShaderPolyMode FillMode;
	public PolygonOffsetMode ZBias;
}

public interface IMeshMgr
{

}

public enum ShaderFillMode
{
	Solid,
	Wireframe
}

public struct RasterState
{
	public ShaderFillMode FillMode;
	public MaterialCullMode CullMode;
	public bool CullEnable;
	public bool DepthBias;
	public bool ScissorEnable;
	public bool MultisampleEnable;
}

public interface IShaderAPI : IShaderDynamicAPI
{
	// Buffer clearing
	void ClearBuffers(bool clearColor, bool clearDepth, bool clearStencil, int renderTargetWidth, int renderTargetHeight);
	void ClearColor3ub(byte r, byte g, byte b);
	void ClearColor4ub(byte r, byte g, byte b, byte a);

	// Methods related to binding shaders
	void BindVertexShader(VertexShaderHandle vertexShader);
	void BindGeometryShader(GeometryShaderHandle geometryShader);
	void BindPixelShader(PixelShaderHandle pixelShader);

	// Methods related to state objects
	void SetRasterState(in RasterState state);

	// Sets the mode...
	bool SetMode(IWindow wnd, uint adapter, in ShaderDeviceInfo info);

	void ChangeVideoMode(in ShaderDeviceInfo info);

	void TexMinFilter(TexFilterMode texFilterMode);
	void TexMagFilter(TexFilterMode texFilterMode);
	void TexWrap(TexCoordComponent coord, TexWrapMode wrapMode);

	void CopyRenderTargetToTexture(ShaderAPITextureHandle_t textureHandle);

	// Binds a particular material to render with
	void Bind(IMaterial material);

	// Flushes any primitives that are buffered
	void FlushBufferedPrimitives();

	// Gets the dynamic mesh; note that you've got to render the mesh
	// before calling this function a second time. Clients should *not*
	// call DestroyStaticMesh on the mesh returned by this call.
	IMesh GetDynamicMesh(IMaterial material, int hwSkinBoneCount, bool buffered = true, IMesh? vertexOverride = null, IMesh? indexOverride = null);
	IMesh GetDynamicMeshEx(IMaterial material, VertexFormat vertexFormat, int hwSkinBoneCount, bool buffered = true, IMesh? vertexOverride = null, IMesh? indexOverride = null);

	// Renders a single pass of a material
	void RenderPass();

	// Set the number of bone weights
	void SetNumBoneWeights(int numBones);

	// Sets the lights
	void SetLight(int lightNum, in LightDesc desc);

	// Lighting origin for the current model
	void SetLightingOrigin(Vector3 lightingOrigin);

	void SetAmbientLight(float r, float g, float b);
	void SetAmbientLightCube(Span<Vector4> cube);

	// The shade mode
	void ShadeMode(ShadeMode mode);

	// The cull mode
	void CullMode(MaterialCullMode cullMode);

	// Force writes only when z matches. . . useful for stenciling things out
	// by rendering the desired Z values ahead of time.
	void ForceDepthFuncEquals(bool bEnable);

	// Forces Z buffering to be on or off
	void OverrideDepthEnable(bool bEnable, bool bDepthEnable);

	void SetHeightClipZ(float z);
	void SetHeightClipMode(MaterialHeightClipMode heightClipMode);

	void SetClipPlane(int index, ReadOnlySpan<float> plane);
	void EnableClipPlane(int index, bool bEnable);

	// Put all the model matrices into vertex shader constants.
	void SetSkinningMatrices();

	// Returns the nearest supported format
	ImageFormat GetNearestSupportedFormat(ImageFormat fmt, bool bFilteringRequired = true);
	ImageFormat GetNearestRenderTargetFormat(ImageFormat fmt);

	// When AA is enabled, render targets are not AA and require a separate
	// depth buffer.
	bool DoRenderTargetsNeedSeparateDepthBuffer();

	// Texture management methods
	// For CreateTexture also see CreateTextures below
	ShaderAPITextureHandle_t CreateTexture(
		int width,
		int height,
		int depth,
		ImageFormat dstImageFormat,
		int numMipLevels,
		int numCopies,
		int flags,
		ReadOnlySpan<char> debugName,
		ReadOnlySpan<char> pTextureGroupName);

	void DeleteTexture(ShaderAPITextureHandle_t textureHandle);

	ShaderAPITextureHandle_t CreateDepthTexture(
		ImageFormat renderTargetFormat,
		int width,
		int height,
		ReadOnlySpan<char> debugName,
		bool bTexture);

	bool IsTexture(ShaderAPITextureHandle_t textureHandle);
	bool IsTextureResident(ShaderAPITextureHandle_t textureHandle);

	// Indicates we're going to be modifying this texture
	// TexImage2D, TexSubImage2D, TexWrap, TexMinFilter, and TexMagFilter
	// all use the texture specified by this function.
	void ModifyTexture(ShaderAPITextureHandle_t textureHandle);

	void TexImage2D(
		int level,
		int cubeFaceID,
		ImageFormat dstFormat,
		int zOffset,
		int width,
		int height,
		ImageFormat srcFormat,
		bool srcIsTiled,       // NOTE: for X360 only
		Span<byte> imageData);

	void TexSubImage2D(
		int level,
		int cubeFaceID,
		int xOffset,
		int yOffset,
		int zOffset,
		int width,
		int height,
		ImageFormat srcFormat,
		int srcStride,
		bool srcIsTiled,       // NOTE: for X360 only
		Span<byte> imageData);

	void TexImageFromVTF(IVTFTexture? vtf, int vtfFrame);

	// An alternate (and faster) way of writing image data
	// (locks the current Modify Texture). Use the pixel writer to write the data
	// after Lock is called
	// Doesn't work for compressed textures 
	bool TexLock(int level, int cubeFaceID, int xOffset, int yOffset, int width, int height, ref PixelWriter writer);
	void TexUnlock();

	// These are bound to the texture
	void TexSetPriority(int priority);

	// Sets the texture state
	void BindTexture(Sampler sampler, ShaderAPITextureHandle_t textureHandle);

	// Set the render target to a texID.
	// Set to SHADER_RENDERTARGET_BACKBUFFER if you want to use the regular framebuffer.
	// Set to SHADER_RENDERTARGET_DEPTHBUFFER if you want to use the regular z buffer.
	void SetRenderTarget(ShaderAPITextureHandle_t colorTextureHandle = (ShaderAPITextureHandle_t)ShaderRenderTarget.Backbuffer,
		ShaderAPITextureHandle_t depthTextureHandle = (ShaderAPITextureHandle_t)ShaderRenderTarget.Depthbuffer);

	// stuff that isn't to be used from within a shader
	void ClearBuffersObeyStencil(bool clearColor, bool clearDepth);
	void ReadPixels(int x, int y, int width, int height, Span<byte> data, ImageFormat dstFormat);
	void ReadPixels(ref System.Drawing.Rectangle srcRect, ref System.Drawing.Rectangle dstRect, Span<byte> data, ImageFormat dstFormat, int dstStride);

	void FlushHardware();

	// Use this to begin and end the frame
	void BeginFrame();
	void EndFrame();

	// Selection mode methods
	int SelectionMode(bool selectionMode);
	void SelectionBuffer(Span<uint> buffer);
	void ClearSelectionNames();
	void LoadSelectionName(int name);
	void PushSelectionName(int name);
	void PopSelectionName();

	// Force the hardware to finish whatever it's doing
	void ForceHardwareSync();

	// Used to clear the transition table when we know it's become invalid.
	void ClearSnapshots();

	void FogStart(float start);
	void FogEnd(float end);
	void SetFogZ(float fogZ);
	// Scene fog state.
	void SceneFogColor3ub(byte r, byte g, byte b);
	void GetSceneFogColor(out Color rgb);
	void SceneFogMode(MaterialFogMode fogMode);
	void SceneFogRadial(bool radial);
	bool GetSceneFogRadial();

	// Can we download textures?
	bool CanDownloadTextures();

	void ResetRenderState(bool fullReset = true);

	// We use smaller dynamic VBs during level transitions, to free up memory
	int GetCurrentDynamicVBSize();
	void DestroyVertexBuffers(bool exitingLevel = false);

	void EvictManagedResources();

	// Level of anisotropic filtering
	void SetAnisotropicLevel(int anisotropyLevel);

	// For debugging and building recording files. This will stuff a token into the recording file,
	// then someone doing a playback can watch for the token.
	void SyncToken(ReadOnlySpan<char> pToken);

	// Setup standard vertex shader constants (that don't change)
	// This needs to be called anytime that overbright changes.
	void SetStandardVertexShaderConstants(float fOverbright);

	//
	// Occlusion query support
	//

	public const ShaderAPIOcclusionQuery_t INVALID_SHADERAPI_OCCLUSION_QUERY_HANDLE = 0;
	public const int OCCLUSION_QUERY_RESULT_PENDING = -1;
	public const int OCCLUSION_QUERY_RESULT_ERROR = -2;
	public static bool OCCLUSION_QUERY_FINISHED(int queryResult) => queryResult != OCCLUSION_QUERY_RESULT_PENDING;

	// Allocate and delete query objects.
	ShaderAPIOcclusionQuery_t CreateOcclusionQueryObject();
	void DestroyOcclusionQueryObject(ShaderAPIOcclusionQuery_t query);

	// Bracket drawing with begin and end so that we can get counts next frame.
	void BeginOcclusionQueryDrawing(ShaderAPIOcclusionQuery_t query);
	void EndOcclusionQueryDrawing(ShaderAPIOcclusionQuery_t query);

	// OcclusionQuery_GetNumPixelsRendered
	//	Get the number of pixels rendered between begin and end on an earlier frame.
	//	Calling this in the same frame is a huge perf hit!
	// Returns iQueryResult:
	//	iQueryResult >= 0					-	iQueryResult is the number of pixels rendered
	//	OCCLUSION_QUERY_RESULT_PENDING		-	query results are not available yet
	//	OCCLUSION_QUERY_RESULT_ERROR		-	query failed
	// Use OCCLUSION_QUERY_FINISHED( iQueryResult ) to test if query finished.
	int OcclusionQuery_GetNumPixelsRendered(ShaderAPIOcclusionQuery_t query, bool flush = false);

	void SetFlashlightState(in FlashlightState state, in Matrix4x4 worldToTexture);

	void ClearVertexAndPixelShaderRefCounts();
	void PurgeUnusedVertexAndPixelShaders();

	// Called when the dx support level has changed
	void DXSupportLevelChanged();

	// By default, the material system applies the VIEW and PROJECTION matrices	to the user clip
	// planes (which are specified in world space) to generate projection-space user clip planes
	// Occasionally (for the particle system in hl2, for example), we want to override that
	// behavior and explictly specify a View transform for user clip planes. The PROJECTION
	// will be mutliplied against this instead of the normal VIEW matrix.
	void EnableUserClipTransformOverride(bool enable);
	void UserClipTransform(in Matrix4x4 worldToView);


	// Set the render target to a texID.
	// Set to SHADER_RENDERTARGET_BACKBUFFER if you want to use the regular framebuffer.
	// Set to SHADER_RENDERTARGET_DEPTHBUFFER if you want to use the regular z buffer.
	void SetRenderTargetEx(int renderTargetID, ShaderAPITextureHandle_t colorTextureHandle = (ShaderAPITextureHandle_t)ShaderRenderTarget.Backbuffer, ShaderAPITextureHandle_t depthTextureHandle = (ShaderAPITextureHandle_t)ShaderRenderTarget.Depthbuffer);

	void CopyRenderTargetToTextureEx(ShaderAPITextureHandle_t textureHandle, int renderTargetID, ref System.Drawing.Rectangle srcRect, ref System.Drawing.Rectangle dstRect);
	void CopyTextureToRenderTargetEx(int renderTargetID, ShaderAPITextureHandle_t textureHandle, ref System.Drawing.Rectangle srcRect, ref System.Drawing.Rectangle dstRect);

	// For dealing with device lost in cases where SwapBuffers isn't called all the time (Hammer)
	void HandleDeviceLost();

	void EnableLinearColorSpaceFrameBuffer(bool bEnable);

	// Lets the shader know about the full-screen texture so it can 
	void SetFullScreenTextureHandle(ShaderAPITextureHandle_t h);

	void SetFastClipPlane(ReadOnlySpan<float> plane);
	void EnableFastClip(bool enable);

	// Returns the number of vertices + indices we can render using the dynamic mesh
	// Passing true in the second parameter will return the max # of vertices + indices
	// we can use before a flush is provoked and may return different values 
	// if called multiple times in succession. 
	// Passing false into the second parameter will return
	// the maximum possible vertices + indices that can be rendered in a single batch
	void GetMaxToRender(IMesh mesh, bool maxUntilFlush, out int maxVerts, out int maxIndices);

	// Returns the max number of vertices we can render for a given material
	int GetMaxVerticesToRender(IMaterial material);
	int GetMaxIndicesToRender();

	// disables all local lights
	void DisableAllLocalLights();

	IMesh GetFlexMesh();

	void SetFlashlightStateEx(in FlashlightState state, in Matrix4x4 worldToTexture, ITexture? flashlightDepthTexture);

	bool SupportsMSAAMode(int nMSAAMode);

	bool OwnGPUResources(bool bEnable);

	//get fog distances entered with FogStart(), FogEnd(), and SetFogZ()
	void GetFogDistances(out float start, out float end, out float fogZ);

	// Hooks for firing PIX events from outside the Material System...
	void BeginPIXEvent(Color color, ReadOnlySpan<char> name);
	void EndPIXEvent();
	void SetPIXMarker(Color color, ReadOnlySpan<char> name);

	// Enables and disables for Alpha To Coverage
	void EnableAlphaToCoverage();
	void DisableAlphaToCoverage();

	// Computes the vertex buffer pointers 
	void ComputeVertexDescription(Span<byte> buffer, VertexFormat vertexFormat, ref MeshDesc desc);

	bool SupportsShadowDepthTextures();

	void SetDisallowAccess(bool access);
	void EnableShaderShaderMutex(bool enable);
	void ShaderLock();
	void ShaderUnlock();

	ImageFormat GetShadowDepthTextureFormat();

	bool SupportsFetch4();
	void SetShadowDepthBiasFactors(float shadowSlopeScaleDepthBias, float shadowDepthBias);

	// Apply stencil operations to every pixel on the screen without disturbing depth or color buffers
	void PerformFullScreenStencilOperation();

	void SetScissorRect(int left, int top, int right, int bottom, bool enableScissor);

	// nVidia CSAA modes, different from SupportsMSAAMode()
	bool SupportsCSAAMode(int numSamples, int qualityLevel);

	//Notifies the shaderapi to invalidate the current set of delayed constants because we just finished a draw pass. Either actual or not.
	void InvalidateDelayedShaderConstants();

	// Gamma<->Linear conversions according to the video hardware we're running on
	float GammaToLinear_HardwareSpecific(float gamma);
	float LinearToGamma_HardwareSpecific(float linear);

	//Set's the linear->gamma conversion textures to use for this hardware for both srgb writes enabled and disabled(identity)
	void SetLinearToGammaConversionTextures(ShaderAPITextureHandle_t srgbWriteEnabledTexture, ShaderAPITextureHandle_t identityTexture);

	ImageFormat GetNullTextureFormat();

	void BindVertexTexture(VertexTextureSampler sampler, ShaderAPITextureHandle_t textureHandle);

	// Enables hardware morphing
	void EnableHWMorphing(bool enable);

	// Sets flexweights for rendering
	void SetFlexWeights(int firstWeight, ReadOnlySpan<MorphWeight> weights);

	void FogMaxDensity(float maxDensity);

	// Create a multi-frame texture (equivalent to calling "CreateTexture" multiple times, but more efficient)
	void CreateTextures(
		Span<ShaderAPITextureHandle_t> handles,
		int width,
		int height,
		int depth,
		ImageFormat dstImageFormat,
		int numMipLevels,
		int numCopies,
		int flags,
		ReadOnlySpan<char> debugName,
		ReadOnlySpan<char> textureGroupName);

	void AcquireThreadOwnership();
	void ReleaseThreadOwnership();

	// debug logging
	// only implemented in some subclasses
	float Knob(Span<char> knobname, Span<float> setvalue = default);
	// Allows us to override the alpha write setting of a material
	void OverrideAlphaWriteEnable(bool enable, bool alphaWriteEnable);
	void OverrideColorWriteEnable(bool overrideEnable, bool colorWriteEnable);

	//extended clear buffers function with alpha independent from color
	void ClearBuffersObeyStencilEx(bool clearColor, bool clearAlpha, bool clearDepth);

#if GMOD_DLL
	void GMOD_ForceFilterMode(bool forceFilter, int filterType);
	void OverrideBlend(bool overrideEnable, bool useSeparateAlpha, int srcBlend, int destBlend, int blendFunc);
	void OverrideBlendSeparateAlpha(bool overrideEnable, bool useSeparateAlpha, int srcBlend, int destBlend, int blendFunc);
#else
#error Reimplement these!!
#endif

	bool TexLock(int level, int cubeFaceID, int xOffset, int yOffset, int width, int height, ref PixelWriterMem writer);
	IShaderShadow NewShaderShadow(ReadOnlySpan<char> materialName);
	bool IsTranslucent(IShaderShadow renderState);
	bool IsAlphaTested(IShaderShadow renderState);
}
