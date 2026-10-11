using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Source.MaterialSystem;

public class DummyHardwareConfig : IMaterialSystemHardwareConfig
{
	public bool HasDestAlphaBuffer() => false;
	public bool HasStencilBuffer() => false;
	public int StencilBufferBits() => 0;
	public int GetFrameBufferColorDepth() => 0;
	public int GetSamplerCount() => 0;
	public bool HasSetDeviceGammaRamp() => false;
	public bool SupportsCompressedTextures() => false;
	public VertexCompressionType SupportsCompressedVertices() => VertexCompressionType.None;
	public bool SupportsVertexAndPixelShaders() => false;
	public bool SupportsPixelShaders_1_4() => false;
	public bool SupportsPixelShaders_2_0() => false;
	public bool SupportsPixelShaders_2_b() => false;
	public bool ActuallySupportsPixelShaders_2_b() => false;
	public bool SupportsStaticControlFlow() => false;
	public bool SupportsVertexShaders_2_0() => false;
	public bool SupportsShaderModel_3_0() => false;
	public int MaximumAnisotropicLevel() => 1;
	public int MaxTextureWidth() => 0;
	public int MaxTextureHeight() => 0;
	public int MaxTextureDepth() => 0;
	public nint TextureMemorySize() => 0;
	public bool SupportsOverbright() => false;
	public bool SupportsCubeMaps() => false;
	public bool SupportsMipmappedCubemaps() => false;
	public bool SupportsNonPow2Textures() => false;

	// The number of texture stages represents the number of computations
	// we can do in the pixel pipeline, it is *not* related to the
	// simultaneous number of textures we can use
	public int GetTextureStageCount() => 0;
	public int NumVertexShaderConstants() => 0;
	public int NumBooleanVertexShaderConstants() => 0;
	public int NumIntegerVertexShaderConstants() => 0;
	public int NumPixelShaderConstants() => 0;
	public int MaxNumLights() => 0;
	public bool SupportsHardwareLighting() => false;
	public int MaxBlendMatrices() => 0;
	public int MaxBlendMatrixIndices() => 0;
	public int MaxTextureAspectRatio() => 0;
	public int MaxVertexShaderBlendMatrices() => 0;
	public int MaxUserClipPlanes() => 0;
	public bool UseFastClipping() => false;
	public bool UseFastZReject() => false;
	public bool PreferReducedFillrate() => false;

	// This here should be the major item looked at when checking for compat
	// from anywhere other than the material system	shaders
	public int GetDXSupportLevel() => 90;
	public ReadOnlySpan<char> GetShaderDLLName() => null;

	public bool ReadPixelsFromFrontBuffer() => false;

	// Are dx dynamic textures preferred?
	public bool PreferDynamicTextures() => false;

	public bool SupportsHDR() => false;
	public HDRType GetHDRType() => HDRType.None;
	public HDRType GetHardwareHDRType() => HDRType.None;

	public bool HasProjectedBumpEnv() => false;
	public bool SupportsSpheremapping() => false;
	public bool NeedsAAClamp() => false;
	public bool HasFastZReject() => false;
	public bool NeedsATICentroidHack() => false;
	public bool SupportsColorOnSecondStream() => false;
	public bool SupportsStaticPlusDynamicLighting() => false;
	public bool SupportsStreamOffset() => false;
	public int GetMaxDXSupportLevel() => 90;
	public bool SpecifiesFogColorInLinearSpace() => false;
	public bool SupportsSRGB() => false;
	public bool FakeSRGBWrite() => false;
	public bool CanDoSRGBReadFromRTs() => true;
	public bool SupportsGLMixedSizeTargets() => false;
	public bool IsAAEnabled() => false;
	public int GetVertexTextureCount() => 0;
	public int GetMaxVertexTextureDimension() => 0;
	public int MaxViewports() => 1;
	public void OverrideStreamOffsetSupport(bool enabled, bool enableSupport) { }
	public int GetShadowFilterMode() => 0;
	public int NeedsShaderSRGBConversion() => 0;
	public bool UsesSRGBCorrectBlending() => false;
	public bool HasFastVertexTextures() => false;
	public int MaxHWMorphBatchCount() => 0;
	public bool SupportsHDRMode(HDRType nMode) => false;
	public bool GetHDREnabled() => true;
	public void SetHDREnabled(bool bEnable) { }
	public bool SupportsBorderColor() => true;
	public bool SupportsFetch4() => false;
	public bool CanStretchRectFromTextures() => false;

	public static readonly DummyHardwareConfig g_DummyHardwareConfig = new();
}

public class DummyMesh : IMesh
{
	static unsafe ushort* g_DummyIndices;
	static unsafe float* dummyFloat;
	static unsafe byte* dummyChar;
	static unsafe void allocCheckDummyBuffers() {
		if (g_DummyIndices == null) g_DummyIndices = (ushort*)Marshal.AllocHGlobal(6 * sizeof(ushort));
		if (dummyFloat == null) dummyFloat = (float*)Marshal.AllocHGlobal(32 * sizeof(float));
		if (dummyChar == null) dummyChar = (byte*)Marshal.AllocHGlobal(32);
	}

	public void BeginCastBuffer(VertexFormat format) { }
	public void BeginCastBuffer(MaterialIndexFormat format) { }
	public void Draw(int firstIndex = -1, int indexCount = 0) { }
	public void Draw(ReadOnlySpan<PrimList> lists, int numLists) { }
	public void EndCastBuffer() { }
	public int GetRoomRemaining() => 1;
	public VertexFormat GetVertexFormat() => VertexFormat.Position;
	public int IndexCount() => 0;
	public MaterialIndexFormat IndexFormat() => MaterialIndexFormat.x16;
	public bool IsDynamic() => false;
	public unsafe bool Lock(int vertexCount, bool append, ref VertexDesc desc) {
		allocCheckDummyBuffers();
		memreset(ref desc);
		desc.Position = dummyFloat;
		desc.BoneWeight = dummyFloat;
		desc.BoneMatrixIndex = dummyChar;
		desc.Normal = dummyFloat;
		desc.Color = dummyChar;
		desc.Specular = dummyChar;
		for (int i = 0; i < IMesh.VERTEX_MAX_TEXTURE_COORDINATES; i++)
			desc.SetTexCoord(i, dummyFloat);
		desc.TangentS = dummyFloat;
		desc.TangentT = dummyFloat;
		desc.Wrinkle = dummyFloat;

		// user data
		desc.UserData = dummyFloat;
		desc.FirstVertex = 0;
		desc.OffsetVertex = 0;
		return true;
	}
	public int Lock(bool readOnly, int firstIndex, int indexCount, ref IndexDesc desc) => 0;
	public void LockMesh(int vertexCount, int indexCount, ref MeshDesc desc) {
		Lock(vertexCount, false, ref desc.Vertex);
		Lock(indexCount, false, ref desc.Index);
	}
	// FIXME: Make this work! Unsupported methods of IIndexBuffer
	unsafe bool Lock(int maxIndexCount, bool append, ref IndexDesc desc) {
		allocCheckDummyBuffers();
		desc.Indices = g_DummyIndices;
		desc.IndexSize = 0;
		desc.FirstIndex = 0;
		desc.OffsetIndex = 0;
		return true;
	}
	public void MarkAsDrawn() { }
	public unsafe void ModifyBegin(int firstVertex, int vertexCount, int firstIndex, int indexCount, ref MeshDesc desc) {
		allocCheckDummyBuffers();
		desc.Index.Indices = g_DummyIndices;
		desc.Index.IndexSize = 0;
		desc.Index.FirstIndex = 0;
		desc.Index.OffsetIndex = 0;
	}
	public void ModifyEnd(ref MeshDesc desc) { }
	public void SetColorMesh(IMesh colorMesh, int vertexOffset) { }
	public void SetFlexMesh(IMesh? mesh, int vertexOffset) { }
	public void DisableFlexMesh() { }
	public void SetPrimitiveType(MaterialPrimitiveType type) { }
	public bool Unlock(int vertexCount, ref VertexDesc desc) => false;
	public bool Unlock(int writtenIndexCount, ref IndexDesc desc) => false;
	public void UnlockMesh(int vertexCount, int indexCount, ref MeshDesc desc) { }
	public int VertexCount() => 0;
}

public class DummyTexture : ITexture
{
	public void Dispose() { }
	public void Download(Rectangle rect = default, int additionalCreationFlags = 0) { }
	public void ForceLODOverride(int numLodOverrideUpOrDown) { }
	public int GetActualDepth() => 1;
	public int GetActualHeight() => 512;
	public int GetActualWidth() => 512;
	public nint GetApproximateVidMemBytes() => 64;
	public int GetFlags() => 0;
	public ImageFormat GetImageFormat() => ImageFormat.RGBA8888;
	public void GetLowResColorSample(float s, float t, Span<float> color) { }
	public int GetMappingDepth() => 1;
	public int GetMappingHeight() => 512;
	public int GetMappingWidth() => 512;
	public ReadOnlySpan<char> GetName() => "DummyTexture";
	public NormalDecodeMode GetNormalDecodeMode() => NormalDecodeMode.None;
	public int GetNumAnimationFrames() => 0;
	public Span<byte> GetResourceData(uint type) => null;

	public void IncrementReferenceCount() { }
	public void DecrementReferenceCount() { }
	public void DeleteIfUnreferenced() { }
	public bool IsCubeMap() => false;
	public bool IsError() => false;
	public bool IsMipmapped() => false;
	public bool IsNormalMap() => false;
	public bool IsProcedural() => false;
	public bool IsRenderTarget() => false;
	public bool IsTranslucent() => false;
	public bool IsVertexLit() => false;
	public bool IsVolumeTexture() => false;
	public bool SaveToFile(ReadOnlySpan<char> fileName) => false;
	public void SetTextureRegenerator(ITextureRegenerator textureRegen) { }
	public void SwapContents(ITexture other) { }
}

public class DummyMaterialVar : IMaterialVar
{
	public override void CopyFrom(IMaterialVar materialVar) { }
	public override void GetFourCCValue(ulong type, out object? data) { data = null; }
	public override IMaterial? GetMaterialValue() => DummyMaterialSystem.g_DummyMaterial;
	public override Matrix4x4 GetMatrixValue() => DummyMaterialSystem.g_DummyMatrix;
	public override ReadOnlySpan<char> GetName() => "DummyMaterialVar";
	public override IMaterial GetOwningMaterial() => DummyMaterialSystem.g_DummyMaterial;
	public override string GetStringValue() => "";
	public override ITexture? GetTextureValue() => DummyMaterialSystem.g_DummyTexture;
	public override void GetVecValue(Span<float> color) {
		for (int i = 0; i < color.Length; i++)
			color[i] = 1;
	}
	public override bool IsDefined() => true;
	public override bool MatrixIsIdentity() => false;
	public override void SetFloatValue(float val) { }
	public override void SetFourCCValue(ulong type, object? data) { }
	public override void SetIntValue(int val) { }
	public override void SetMaterialValue(IMaterial? material) { }
	public override void SetMatrixValue(in Matrix4x4 matrix) { }
	public override void SetStringValue(ReadOnlySpan<char> val) { }
	public override void SetTextureValue(ITexture? texture) { }
	public override void SetUndefined() { }
	public override void SetValueAutodetectType(ReadOnlySpan<char> val) { }
	public override void SetVecComponentValue(float val, int component) { }
	public override void SetVecValue(ReadOnlySpan<float> val) { }
	public override void SetVecValue(float x, float y) { }
	public override void SetVecValue(float x, float y, float z) { }
	public override void SetVecValue(float x, float y, float z, float w) { }
	protected override float GetFloatValueInternal() => 1;
	protected override int GetIntValueInternal() => 1;
	static readonly float[] vecvalX3 = [1, 1, 1];
	static readonly float[] vecvalX4 = [1, 1, 1, 1];
	protected override Span<float> GetVecValueInternal() => vecvalX4;
	protected override void GetVecValueInternal(Span<float> val) {
		for (int i = 0; i < val.Length; i++)
			val[i] = 1;
	}
	protected override int VectorSizeInternal() => 3;

	public override Span<float> GetVecValue() => vecvalX4;
}

public class DummyMaterial : IMaterial
{
	public void CallBindProxy(object? clientEntity) { }
	public void DecrementReferenceCount() { }
	public void DeleteIfUnreferenced() { }
	public IMaterialVar FindVar(ReadOnlySpan<char> varName, out bool found, bool complain = true) {
		found = true;
		return DummyMaterialSystem.g_DummyMaterialVar;
	}
	public IMaterialVar? FindVarFast(ReadOnlySpan<char> name, ref TokenCache lightmapVarCache) => null;
	public int GetEnumerationID() => 0;
	public void RecomputeStateSnapshots() { }
	public float GetMappingHeight() => 512;
	public float GetMappingWidth() => 512;
	public void GetReflectivity(out Vector3 reflect) => reflect = new(0.2f, 0.2f, 0.2f);
	public IMaterial GetMaterialPage() => null!;
	public ReadOnlySpan<char> GetName() => "dummy material";
	public int GetNumAnimationFrames() => 0;
	public bool GetPropertyFlag(MaterialPropertyTypes needsBumpedLightmaps) => true;
	public IMaterialVar[]? GetShaderParams() => null;
	public string? GetShaderName() => null;
	public VertexFormat GetVertexFormat() => 0;
	public bool HasProxy() => false;
	public void IncrementReferenceCount() { }
	public bool InMaterialPage() => false;
	public void GetMaterialOffset(Span<float> offset) {
		offset[0] = 0.0f;
		offset[1] = 0.0f;
	}
	public void GetMaterialScale(Span<float> scale) {
		scale[0] = 1.0f;
		scale[1] = 1.0f;
	}
	public bool IsErrorMaterialInternal() => false;
	public bool IsRealTimeVersion() => false;
	public bool IsTranslucent() => false;
	public bool IsVertexLit() => false;
	public void Refresh() { }
	public void RefreshPreservingMaterialVars() { }
	public int ShaderParamCount() => 0;
	public bool TryFindVar(ReadOnlySpan<char> varName, [NotNullWhen(true)] out IMaterialVar? found, bool complain = true) { found = null; return false; }
	public void ColorModulate(float r, float g, float b) { }
	public void AlphaModulate(float alpha) { }
	public float GetAlphaModulation() => 1.0f;
	public void GetColorModulation(out float r, out float g, out float b) => r = g = b = 1; 
	public void SetMaterialVarFlag(MaterialVarFlags flag, bool on) { }
	public bool GetMaterialVarFlag(MaterialVarFlags flag) => false;
	public bool IsTwoSided() => false;
	public bool IsAlphaTested() => false;
	public bool UsesEnvCubemap() => false;
	public bool NeedsTangentSpace() => false;
	public bool NeedsSoftwareSkinning() => false;
	public bool NeedsSoftwareLighting() => false;
	public void SetUseFixedFunctionBakedLighting(bool enable) { }
	public bool NeedsPowerOfTwoFrameBufferTexture(bool checkSpecificToThisFrame) => false;
	public bool NeedsFullFrameBufferTexture(bool checkSpecificToThisFrame) => false;
	public bool NeedsLightmapBlendAlpha() => false;
}

public class DummyMorph : IMorph
{
	public void AddMorph(in MorphVertexInfo info) {	}
	public void Lock(float flFloatToFixedScale = 1) {	}

	public void Unlock() {}
}

public class DummyMaterialSystem : IMaterialSystemStub, IShaderUtil, IMatRenderContext
{
	internal static readonly DummyMaterial g_DummyMaterial = new();
	internal static readonly DummyMaterialVar g_DummyMaterialVar = new();
	internal static Matrix4x4 g_DummyMatrix = Matrix4x4.Identity;
	internal static readonly DummyTexture g_DummyTexture = new();
	internal static DummyMesh g_DummyMesh = null!;
	internal static readonly MaterialSystem_Config g_dummyConfig = new();

	static DummyMesh GetDummyMesh() => g_DummyMesh ??= new();

	IMaterialSystem? RealMaterialSystem;

	public void SetRealMaterialSystem(IMaterialSystem? sys) => RealMaterialSystem = sys;
	public void Init(IServiceProvider shaderAPIFactory, IMaterialProxyFactory materialProxyFactory, IServiceProvider fileSystemFactory, IServiceProvider? cvarFactory = null) { }
	public void SetShaderAPI(IServiceProvider shaderAPIFactory) { }
	public void SetAdapter(uint adapter, int flags) { }
	public void ModInit() { }
	public void ModShutdown() { }
	public void SetThreadMode(MaterialThreadMode mode, int nServiceThread = -1) { }
	public MaterialThreadMode GetThreadMode() => MaterialThreadMode.SingleThreaded;
	public bool IsRenderThreadSafe() => true;
	public void ExecuteQueued() { }
	public IMaterialSystemHardwareConfig GetHardwareConfig(ReadOnlySpan<char> pVersion, out int returnCode) {
		returnCode = 1;
		return DummyHardwareConfig.g_DummyHardwareConfig;
	}
	public bool UpdateConfig(bool forceUpdate) => false;
	public bool OverrideConfig(MaterialSystem_Config config, bool forceUpdate) => false;
	static readonly MaterialSystem_Config dummy_GetCurrentConfigForVideoCard = new();
	public MaterialSystem_Config GetCurrentConfigForVideoCard() => dummy_GetCurrentConfigForVideoCard;
	public bool GetRecommendedConfigurationInfo(int nDXLevel, KeyValues keyValues) => false;
	public uint GetDisplayAdapterCount() => 0;
	public uint GetCurrentAdapter() => 0;
	public void GetDisplayAdapterInfo(uint adapter, out MaterialAdapterInfo info) { info = default; }
	public uint GetModeCount(uint adapter) => 0;
	public void AddModeChangeCallBack(ModeChangeCallbackFunc func) { }
	public void GetDisplayMode(out UserVideoMode mode) { mode = default; }
	public bool SetMode(IWindow window, MaterialSystem_Config mode) => true;
	public bool SupportsMSAAMode(int nMSAAMode) => false;
	static readonly MaterialSystemHardwareIdentifier dummy_GetVideoCardIdentifier = new();
	public ref readonly MaterialSystemHardwareIdentifier GetVideoCardIdentifier() => ref dummy_GetVideoCardIdentifier;
	public void SpewDriverInfo() { }
	public void GetDriverLevelDefaults(out GraphicsDriver maxLevel, out GraphicsDriver recommendedLevel) {
		maxLevel = recommendedLevel = GraphicsDriver.OpenGL46;
	}
	public void GetBackBufferDimensions(out int width, out int height) { width = 1024; height = 768; }
	public ImageFormat GetBackBufferFormat() => ImageFormat.RGBA8888;
	public bool SupportsHDRMode(HDRType hdrMode) => false;
	public bool AddView(IWindow hwnd) => false;
	public void RemoveView(IWindow hwnd) { }
	public void SetView(IWindow hwnd) { }
	public void BeginFrame(TimeUnit_t frameTime) { }
	public void EndFrame() { }
	public void Flush(bool flushHardware = false) { }
	public void SwapBuffers() { }
	public void EvictManagedResources() { }
	public void ReleaseResources() { }
	public void ReacquireResources() { }
	public void AddReleaseFunc(MaterialBufferReleaseFunc func) { }
	public void RemoveReleaseFunc(MaterialBufferReleaseFunc func) { }
	public void AddRestoreFunc(MaterialBufferRestoreFunc func) { }
	public void RemoveRestoreFunc(MaterialBufferRestoreFunc func) { }
	public void ResetTempHWMemory(bool bExitingLevel = false) { }
	public void HandleDeviceLost() { }
	public int ShaderCount() => 0;
	public int GetShaders(int firstShader, Span<IShader> shaderList) => 0;
	public int ShaderFlagCount() => 0;
	public ReadOnlySpan<char> ShaderFlagName(int index) => default;
	public void GetShaderFallback(ReadOnlySpan<char> shaderName, Span<char> fallbackShader) { fallbackShader[0] = '\0'; }
	public IMaterialProxyFactory? GetMaterialProxyFactory() => null;
	public void SetMaterialProxyFactory(IMaterialProxyFactory? factory) { }
	public void EnableEditorMaterials() { }
	public void SetInStubMode(bool inStubMode) { }
	public void DebugPrintUsedMaterials(ReadOnlySpan<char> searchSubString, bool verbose) { }
	public void DebugPrintUsedTextures() { }
	public void ToggleSuppressMaterial(ReadOnlySpan<char> materialName) { }
	public void ToggleDebugMaterial(ReadOnlySpan<char> materialName) { }
	public bool UsingFastClipping() => true;
	public int StencilBufferBits() => 0;
	public void UncacheAllMaterials() { }
	public void UncacheUnusedMaterials(bool bRecomputeStateSnapshots = false) { }
	public void CacheUsedMaterials() { }
	public void ReloadTextures() { }
	public void ReloadMaterials(ReadOnlySpan<char> subString = default) { }
	public IMaterial? CreateMaterial(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName, KeyValues vmtKeyValues) => g_DummyMaterial;
	public IMaterial? CreateMaterial(ReadOnlySpan<char> materialName, KeyValues vmtKeyValues) => g_DummyMaterial;
	public IMaterial? FindMaterial(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName, bool complain = true, ReadOnlySpan<char> complainPrefix = default) => RealMaterialSystem != null ? RealMaterialSystem.FindMaterial(materialName, textureGroupName, complain, complainPrefix) : g_DummyMaterial;
	public bool IsMaterialLoaded(ReadOnlySpan<char> materialName) => RealMaterialSystem != null ? RealMaterialSystem.IsMaterialLoaded(materialName) : false;
	public MaterialHandle_t FirstMaterial() => 0;
	public MaterialHandle_t NextMaterial(MaterialHandle_t h) => 0;
	public MaterialHandle_t InvalidMaterial() => RealMaterialSystem != null ? RealMaterialSystem.InvalidMaterial() : 0;
	public IMaterial? GetMaterial(MaterialHandle_t h) => RealMaterialSystem != null ? RealMaterialSystem.GetMaterial(h) : g_DummyMaterial;
	public int GetNumMaterials() => RealMaterialSystem != null ? RealMaterialSystem.GetNumMaterials() : 0;
	public ITexture? FindTexture(ReadOnlySpan<char> textureName, ReadOnlySpan<char> textureGroupName, bool complain = true, CreateTextureFlags additionalCreationFlags = 0) => RealMaterialSystem != null ? RealMaterialSystem.FindTexture(textureName, textureGroupName, complain, additionalCreationFlags) : g_DummyTexture;
	public bool IsTextureLoaded(ReadOnlySpan<char> textureName) => false;
	public ITexture? CreateProceduralTexture(ReadOnlySpan<char> textureName, ReadOnlySpan<char> textureGroupName, int w, int h, ImageFormat fmt, TextureFlags flags) => g_DummyTexture;
	public void BeginRenderTargetAllocation() { }
	public void EndRenderTargetAllocation() { }
	public ITexture? CreateRenderTargetTexture(int w, int h, RenderTargetSizeMode sizeMode, ImageFormat format, MaterialRenderTargetDepth depth = MaterialRenderTargetDepth.Shared) => null;
	public ITexture? CreateNamedRenderTargetTextureEx(ReadOnlySpan<char> rtName, int w, int h, RenderTargetSizeMode sizeMode, ImageFormat format, MaterialRenderTargetDepth depth = MaterialRenderTargetDepth.Shared, TextureFlags textureFlags = TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags renderTargetFlags = 0) => g_DummyTexture;
	public ITexture? CreateNamedRenderTargetTexture(ReadOnlySpan<char> rtName, int w, int h, RenderTargetSizeMode sizeMode, ImageFormat format, MaterialRenderTargetDepth depth = MaterialRenderTargetDepth.Shared, bool clampTexCoords = true, bool autoMipMap = false) => null;
	public ITexture? CreateNamedRenderTargetTextureEx2(ReadOnlySpan<char> rtName, int w, int h, RenderTargetSizeMode sizeMode, ImageFormat format, MaterialRenderTargetDepth depth = MaterialRenderTargetDepth.Shared, TextureFlags textureFlags = TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags renderTargetFlags = 0) => null;
	public void BeginLightmapAllocation() { }
	public void EndLightmapAllocation() { }
	public int AllocateLightmap(int width, int height, Span<int> offsetIntoLightmapPage, IMaterial? material) => 0;
	public int AllocateWhiteLightmap(IMaterial? material) => 0;
	public void UpdateLightmap(int lightmapPageID, Span<int> lightmapSize, Span<int> offsetIntoLightmapPage, Span<float> pFloatImage, Span<float> pFloatImageBump1, Span<float> pFloatImageBump2, Span<float> pFloatImageBump3) { }
	public int GetNumSortIDs() => 10;
	public void GetSortInfo(Span<MaterialSystem_SortInfo> sortInfoArray) { }
	public void GetLightmapPageSize(int lightmap, out int width, out int height) {
		if (RealMaterialSystem != null)
			RealMaterialSystem.GetLightmapPageSize(lightmap, out width, out height);
		else
			width = height = 32;
	}
	public void ResetMaterialLightmapPageInfo() { }
	public void ClearBuffers(bool clearColor, bool clearDepth, bool clearStencil = false) { }
	public IMatRenderContext GetRenderContext() => this;
	public bool SupportsShadowDepthTextures() => false;
	public void BeginUpdateLightmaps() { }
	public void EndUpdateLightmaps() { }
	public MaterialLock Lock() => new(0);
	public void Unlock(MaterialLock l) { }
	public ImageFormat GetShadowDepthTextureFormat() => ImageFormat.Unknown;
	public bool SupportsFetch4() => false;
	public bool SupportsCSAAMode(int nNumSamples, int nQualityLevel) => false;
	public void RemoveModeChangeCallBack(ModeChangeCallbackFunc func) { }
	public IMaterial? FindProceduralMaterial(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName, KeyValues vmtKeyValues) =>
		RealMaterialSystem != null ? RealMaterialSystem.FindProceduralMaterial(materialName, textureGroupName, vmtKeyValues) : g_DummyMaterial;
	public ImageFormat GetNullTextureFormat() => ImageFormat.Unknown;
	public void AddTextureAlias(ReadOnlySpan<char> alias, ReadOnlySpan<char> realName) { }
	public void RemoveTextureAlias(ReadOnlySpan<char> alias) { }
	public int AllocateDynamicLightmap(Span<int> lightmapSize, Span<int> outOffsetIntoPage, int frameID) => 0;
	public void SetExcludedTextures(ReadOnlySpan<char> scriptName) { }
	public void UpdateExcludedTextures() { }
	public bool IsInFrame() => false;
	public void CompactMemory() { }
	public void ReloadFilesInList(IFileList filesToReload) { }
	public bool AllowThreading(bool aAllow, int serviceThread) => false;
	public IMaterial? FindMaterialEx(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName, int context, bool complain = true, ReadOnlySpan<char> complainPrefix = default) =>
		RealMaterialSystem != null ? RealMaterialSystem.FindMaterialEx(materialName, textureGroupName, context, complain, complainPrefix) : g_DummyMaterial;
	public void DoStartupShaderPreloading() { }
	public ReadOnlySpan<char> GetDisplayDeviceName() => "";
	public void GMOD_FlushQueue() { }
	public bool GMOD_TextureExists(ReadOnlySpan<char> textureName) => true;
	public bool GMOD_IsMaterialMissing(ReadOnlySpan<char> materialName) => false;
	public IMaterial? GMOD_GetErrorMaterial() => null;
	public void GMOD_MarkMissing(ReadOnlySpan<char> materialName) { }
	public void GMOD_ClearMissing(bool unknown) { }
	public void SetRenderTargetFrameBufferSizeOverrides(int width, int height) { }
	public void GetRenderTargetFrameBufferDimensions(out int width, out int height) => GetBackBufferDimensions(out width, out height);
	public ITexture? CreateTextureFromBits(int w, int h, int mips, ImageFormat fmt, int srcBufferSize, Span<byte> srcBits) => null;
	public void OverrideRenderTargetAllocation(bool rtAlloc) { }
	public ITextureCompositor? NewTextureCompositor(int w, int h, ReadOnlySpan<char> compositeName, int teamNum, ulong randomSeed, KeyValues stageDesc, CreateTextureFlags texCompositeCreateFlags = 0) => null;
	public void AsyncFindTexture<T>(ReadOnlySpan<char> pFilename, ReadOnlySpan<char> textureGroupName, IAsyncTextureOperationReceiver<T> recipient, ref T extraArgs, bool complain = true, CreateTextureFlags additionalCreationFlags = 0) { }
	public ITexture? CreateNamedTextureFromBitsEx(ReadOnlySpan<char> name, ReadOnlySpan<char> textureGroupName, int w, int h, int mips, ImageFormat fmt, int srcBufferSize, Span<byte> srcBits, CreateTextureFlags flags) => null;
	public bool AddTextureCompositorTemplate(ReadOnlySpan<char> name, KeyValues tmplDesc, int texCompositeTemplateFlags = 0) => false;
	public bool VerifyTextureCompositorTemplates() => false;
	public bool HasShaderAPI() => true;
	public bool InFlashlightMode() => false;
	public bool OnDrawMesh(IMesh mesh, int firstIndex, int indexCount) => false;
	public bool OnSetPrimitiveType(IMesh mesh, MaterialPrimitiveType type) => false;
	public bool OnFlushBufferedPrimitives() => false;
	public void SyncMatrices() { }
	public void SyncMatrix(MaterialMatrixMode mode) { }
	public void RestoreShaderObjects(IServiceProvider services, int changeFlags = 0) { }
	public void BindStandardTexture(Sampler sampler, StandardTextureId id) { }
	public void BeginRender() { }
	public void EndRender() { }
	public void BindLocalCubemap(ITexture? texture) { }
	public void SetRenderTarget(ITexture? texture) { }
	public ITexture? GetRenderTarget() => g_DummyTexture;
	public void GetRenderTargetDimensions(out int width, out int height) => width = height = 256;
	public void Bind(IMaterial? material, object? proxyData = null) { }
	public void BindLightmapPage(int lightmapPageID) { }
	public void DepthRange(float zNear, float zFar) { }
	public void ReadPixels(int x, int y, int width, int height, Span<byte> data, ImageFormat dstFormat) { }
	public void SetAmbientLight(float r, float g, float b) { }
	public void SetLight(int lightNum, in LightDesc desc) { }
	public void SetAmbientLightCube(Span<Vector4> cube) { }
	public void CopyRenderTargetToTexture(ITexture? texture) { }
	public void SetFrameBufferCopyTexture(ITexture? texture, int textureIndex = 0) { }
	public ITexture? GetFrameBufferCopyTexture(int textureIndex) => g_DummyTexture;
	public void MatrixMode(MaterialMatrixMode matrixMode) { }
	public void PushMatrix() { }
	public void PopMatrix() { }
	public void LoadMatrix(in Matrix4x4 matrix) { }
	public void LoadMatrix(Matrix3x4 matrix) { }
	public void MultMatrix(in Matrix4x4 matrix) { }
	public void MultMatrix(in Matrix3x4 matrix) { }
	public void MultMatrixLocal(in Matrix4x4 matrix) { }
	public void MultMatrixLocal(in Matrix3x4 matrix) { }
	public void GetMatrix(MaterialMatrixMode matrixMode, out Matrix4x4 matrix) => MathLib.SetIdentityMatrix(out matrix);
	public void GetMatrix(MaterialMatrixMode matrixMode, out Matrix3x4 matrix) => MathLib.SetIdentityMatrix(out matrix);
	public void LoadIdentity() { }
	public void Ortho(double left, double top, double right, double bottom, double zNear, double zFar) { }
	public void PerspectiveX(double fovx, double aspect, double zNear, double zFar) { }
	public void PickMatrix(int x, int y, int width, int height) { }
	public void Rotate(float angle, float x, float y, float z) { }
	public void Translate(float x, float y, float z) { }
	public void Scale(float x, float y, float z) { }
	public void Viewport(int x, int y, int width, int height) { }
	public void GetViewport(out int x, out int y, out int width, out int height) {
		x = y = 0;
		width = height = 640;
	}
	public void CullMode(MaterialCullMode cullMode) { }
	public void SetHeightClipMode(MaterialHeightClipMode heightClipMode) { }
	public void SetHeightClipZ(float z) { }
	public void FogMode(MaterialFogMode fogMode) { }
	public void FogStart(float fStart) { }
	public void FogEnd(float fEnd) { }
	public void SetFogZ(float fogZ) { }
	public MaterialFogMode GetFogMode() => MaterialFogMode.None;
	public void FogRadial(bool radial) { }
	public bool GetFogRadial() => false;
	public void FogColor3f(float r, float g, float b) { }
	public void FogColor3fv(ReadOnlySpan<float> rgb) { }
	public void FogColor3ub(byte r, byte g, byte b) { }
	public void FogColor3ubv(ReadOnlySpan<byte> rgb) { }
	public void GetFogColor(out Color rgb) => rgb = default;
	public void SetNumBoneWeights(int numBones) { }
	public IMesh? CreateStaticMesh(VertexFormat fmt, ReadOnlySpan<char> textureBudgetGroup, IMaterial material = null) => GetDummyMesh();
	public void DestroyStaticMesh(IMesh? mesh) { }
	public IMesh? GetDynamicMesh(bool buffered = true, IMesh? vertexOverride = null, IMesh? indexOverride = null, IMaterial? autoBind = null) => GetDummyMesh();
	public int SelectionMode(bool selectionMode) => 0;
	public void SelectionBuffer(Span<uint> buffer) { }
	public void ClearSelectionNames() { }
	public void LoadSelectionName(int name) { }
	public void PushSelectionName(int name) { }
	public void PopSelectionName() { }
	public void ClearColor3ub(byte r, byte g, byte b) { }
	public void ClearColor4ub(byte r, byte g, byte b, byte a) { }
	public void OverrideDepthEnable(bool enable, bool depthEnable) { }
	public void DrawScreenSpaceQuad(IMaterial? material) { }
	public void SyncToken(ReadOnlySpan<char> token) { }
	public float ComputePixelWidthOfSphere(in Vector3 origin, float radius) => 1.0F;
	public OcclusionQueryObjectHandle_t CreateOcclusionQueryObject() => INVALID_OCCLUSION_QUERY_OBJECT_HANDLE;
	public void DestroyOcclusionQueryObject(OcclusionQueryObjectHandle_t handle) { }
	public void BeginOcclusionQueryDrawing(OcclusionQueryObjectHandle_t handle) { }
	public void EndOcclusionQueryDrawing(OcclusionQueryObjectHandle_t handle) { }
	public int OcclusionQuery_GetNumPixelsRendered(OcclusionQueryObjectHandle_t handle) => 0;
	public void SetFlashlightMode(bool enable) { }
	public void SetFlashlightState(in FlashlightState state, in Matrix4x4 worldToTexture) { }
	public MaterialHeightClipMode GetHeightClipMode() => MaterialHeightClipMode.Disable;
	public float ComputePixelDiameterOfSphere(in Vector3 absOrigin, float radius) => 1.0f;
	public void EnableUserClipTransformOverride(bool bEnable) { }
	public void UserClipTransform(in Matrix4x4 worldToView) { }
	public bool GetFlashlightMode() => false;
	public void ResetOcclusionQueryObject(nint handle) { }
	static readonly DummyMorph s_DummyMorph = new();
	public IMorph CreateMorph(MorphFormatFlags format, ReadOnlySpan<char> debugName) => s_DummyMorph;
	public void DestroyMorph(IMorph morph) { }
	public void BindMorph(IMorph morph) { }
	public void SetFlexWeights(int firstWeight, ReadOnlySpan<MorphWeight> weights) { }
	public void ReadPixelsAndStretch(ref Rectangle srcRect, ref Rectangle pDstRect, Span<byte> buffer, ImageFormat dstFormat, int dstStride) { }
	public void GetWindowSize(out int width, out int height) {
		width = height = 0;
		if (RealMaterialSystem != null ) {
			using MatRenderContextPtr renderContext = new(RealMaterialSystem );
			renderContext.GetWindowSize(out width, out height);
		}
	}
	public void DrawScreenSpaceRectangle(IMaterial? material, int destX, int destY, int width, int height, float srcTextureX0, float srcTextureY0, float srcTextureX1, float srcTextureY1, int srcTextureWidth, int srcTextureHeight, IClientRenderable? clientRenderable = null, int xDice = 1, int yDice = 1) { }
	public void LoadBoneMatrix(int boneIndex, in Matrix3x4 matrix) { }
	public void PushRenderTargetAndViewport() { }
	public void PushRenderTargetAndViewport(ITexture? texture) { }
	public void PushRenderTargetAndViewport(ITexture? texture, int viewX, int viewY, int viewW, int viewH) { }
	public void PushRenderTargetAndViewport(ITexture? texture, ITexture? depthTexture, int viewX, int viewY, int viewW, int viewH) { }
	public void PopRenderTargetAndViewport() { }
	public void BindLightmatexture(ITexture? lightmapTexture) { }
	public void CopyRenderTargetToTextureEx(ITexture? texture, int renderTargetID, ref Rectangle pSrcRect, ref Rectangle pDstRect) { }
	public void CopyTextureToRenderTargetEx(int renderTargetID, ITexture? texture, ref Rectangle pSrcRect, ref Rectangle pDstRect) { }
	public void PerspectiveOffCenterX(double fovx, double aspect, double zNear, double zFar, double bottom, double top, double left, double right) { }
	public void SetFloatRenderingParameter(int parm_number, float value) { }
	public void SetIntRenderingParameter(int parm_number, int value) { }
	public void SetVectorRenderingParameter(int parm_number, in Vector3 value) { }
	public void SetStencilEnable(bool onoff) { }
	public void SetStencilFailOperation(StencilOperation op) { }
	public void SetStencilZFailOperation(StencilOperation op) { }
	public void SetStencilPassOperation(StencilOperation op) { }
	public void SetStencilCompareFunction(StencilComparisonFunction cmpfn) { }
	public void SetStencilReferenceValue(int reference) { }
	public void SetStencilTestMask(uint msk) { }
	public void SetStencilWriteMask(uint msk) { }
	public void ClearStencilBufferRectangle(int xmin, int ymin, int xmax, int ymax, int value) { }
	public void SetRenderTargetEx(int renderTargetID, ITexture? texture) { }
	public void PushCustomClipPlane(ReadOnlySpan<float> plane) { }
	public void PopCustomClipPlane() { }
	public void GetMaxToRender(IMesh? pMesh, bool bMaxUntilFlush, Span<int> maxVerts, Span<int> maxIndices) { }
	public int GetMaxVerticesToRender(IMaterial? material) => 32768;
	public int GetMaxIndicesToRender() => 32768;
	public void DisableAllLocalLights() { }
	public int CompareMaterialCombos(IMaterial? material1, IMaterial? material2, int lightMapID1, int lightMapID2) => 0;
	public IMesh? GetFlexMesh() => GetDummyMesh();
	public void SetFlashlightStateEx(in FlashlightState state, in Matrix4x4 worldToTexture, ITexture? flashlightDepthTexture) { }
	public ITexture? GetLocalCubemap() => g_DummyTexture;
	public void ClearBuffersObeyStencil(bool clearColor, bool clearDepth) { }
	public bool EnableClipping(bool enable) => true;
	public void GetFogDistances(out float start, out float end, out float fogZ) => start = end = fogZ = default;
	public void BeginPIXEvent(Color color, ReadOnlySpan<char> name) { }
	public void EndPIXEvent() { }
	public void SetPIXMarker(Color color, ReadOnlySpan<char> name) { }
	public void BeginBatch(IMesh? pIndices) { }
	public void BindBatch(IMesh? pVertices, IMaterial? autoBind = null) { }
	public void DrawBatch(int firstIndex, int numIndices) { }
	public void EndBatch() { }
	public ICallQueue? GetCallQueue() => null;
	public void GetWorldSpaceCameraPosition(out Vector3 cameraPos) => cameraPos = default;
	public void GetWorldSpaceCameraVectors(out Vector3 forward, out Vector3 right, out Vector3 up) => forward = right = up = default;
	public void ResetToneMappingScale(float monoscale) { }
	public void SetGoalToneMappingScale(float monoscale) { }
	public void TurnOnToneMapping() { }
	public void SetToneMappingScaleLinear(in Vector3 scale) { }
	public Vector3 GetToneMappingScaleLinear() => new(1, 1, 1);
	public void SetShadowDepthBiasFactors(float slopeScaleDepthBias, float depthBias) { }
	public void PerformFullScreenStencilOperation() { }
	public void SetLightingOrigin(Vector3 lightingOrigin) { }
	public void SetScissorRect(int left, int top, int right, int bottom, bool enableScissor) { }
	public void BeginMorphAccumulation() { }
	public void EndMorphAccumulation() { }
	public void AccumulateMorph(IMorph morph, ReadOnlySpan<MorphWeight> weights) { }
	public void PushDeformation(ref readonly DeformationBase deformation) { }
	public void PopDeformation() { }
	public int GetNumActiveDeformations() => default;
	public bool GetMorphAccumulatorTexCoord(out Vector2 texCoord, IMorph morph, int vertex) {
		texCoord = default;
		return false;
	}
	public IMesh? GetDynamicMeshEx(VertexFormat vertexFormat, bool buffered = true, IMesh? vertexOverride = null, IMesh? indexOverride = null, IMaterial? autoBind = null) => GetDummyMesh();
	public void FogMaxDensity(float maxDensity) { }
	public IMaterial? GetCurrentMaterial() => default;
	public int GetCurrentNumBones() => 0;
	public object? GetCurrentProxy() => null;
	public void EnableColorCorrection(bool enable) { }
	public ColorCorrectionHandle_t AddLookup(ReadOnlySpan<char> name) => 0;
	public bool RemoveLookup(ColorCorrectionHandle_t handle) => false;
	public void LockLookup(ColorCorrectionHandle_t handle) { }
	public void LoadLookup(ColorCorrectionHandle_t handle, ReadOnlySpan<char> lookuname) { }
	public void UnlockLookup(ColorCorrectionHandle_t handle) { }
	public void SetLookupWeight(ColorCorrectionHandle_t handle, float weight) { }
	public void ResetLookupWeights() { }
	public void SetResetable(ColorCorrectionHandle_t handle, bool resetable) { }
	public void SetFullScreenDepthTextureValidityFlag(bool isValid) { }
	public void SetNonInteractivePacifierTexture(ITexture? texture, float normalizedX, float normalizedY, float normalizedSize) { }
	public void SetNonInteractiveTempFullscreenBuffer(ITexture? texture, MaterialNonInteractiveMode mode) { }
	public void EnableNonInteractiveMode(MaterialNonInteractiveMode mode) { }
	public void RefreshFrontBufferNonInteractive() { }
	public object? LockRenderData(int sizeInBytes) => null;
	public void UnlockRenderData(object? data) { }
	public E? LockRenderDataTyped<E>(int count, E? srcData = null) where E : class {
		return null;
	}

	public void AddRefRenderData() { }
	public void ReleaseRenderData() { }
	public bool IsRenderData(object? data) => false;
	public float Knob(Span<char> knobname, Span<float> setvalue = default) => 0;
	public void OverrideAlphaWriteEnable(bool enable, bool alphaWriteEnable) { }
	public void OverrideColorWriteEnable(bool overrideEnable, bool colorWriteEnable) { }
	public void ClearBuffersObeyStencilEx(bool clearColor, bool clearAlpha, bool clearDepth) { }
	public void AsyncCreateTextureFromRenderTarget<T>(ITexture? srcRt, ReadOnlySpan<char> dstName, ImageFormat dstFmt, bool genMips, CreateTextureFlags additionalCreationFlags, IAsyncTextureOperationReceiver<T> recipient, ref T extraArgs) { }
	public int AddRef() => 0;
	public int Release() => 0;

	public void GMOD_ForceFilterMode(bool unk1, int unk2) {	}
	public void OverrideBlend(bool unk1, bool unk2, int unk3, int unk4, int unk5) {	}
	public void OverrideBlendSeparateAlpha(bool unk1, bool unk2, int unk3, int unk4, int unk5) {	}
}
