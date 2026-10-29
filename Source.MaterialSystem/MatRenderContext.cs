using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.Utilities;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.MaterialSystem;

public class MatRenderContext : MatRenderContextBase, IMatRenderContextInternal
{
	readonly IShaderAPI shaderAPI;

	public MatRenderContext(MaterialSystem materials) : base(materials) {
		shaderAPI = materials.ShaderAPI;
	}

	public void BeginRender() {

	}

	public void ClearColor3ub(byte r, byte g, byte b) => shaderAPI.ClearColor3ub(r, g, b);
	public void ClearColor4ub(byte r, byte g, byte b, byte a) => shaderAPI.ClearColor4ub(r, g, b, a);

	public void ClearBuffers(bool clearColor, bool clearDepth, bool clearStencil = false) {
		int width, height;
		GetRenderTargetDimensions(out width, out height);
		shaderAPI.ClearBuffers(clearColor, clearDepth, clearStencil, width, height);
	}

	public void GetRenderTargetDimensions(out int width, out int height) {
		ITexture? tos = RenderTargetStack.Top().RenderTarget0;

		if (tos != null) {
			width = tos.GetActualWidth();
			height = tos.GetActualHeight();
		}
		else
			shaderAPI.GetBackBufferDimensions(out width, out height);
	}

	public void DepthRange(float near, float far) {
		ActiveViewport.MinZ = near;
		ActiveViewport.MaxZ = far;
		shaderAPI.SetViewports(new(ref ActiveViewport));
	}

	public void EndRender() {

	}

	public override void Flush(bool flushHardware = false) {
		shaderAPI.FlushBufferedPrimitives();
	}

	public void GMOD_ForceFilterMode(bool min, int mode) => shaderAPI.GMOD_ForceFilterMode(min, mode);

	public override void GetViewport(out int x, out int y, out int width, out int height) {
		Assert(RenderTargetStack.Count > 0);
		ref RenderTargetStackElement element = ref RenderTargetStack.Top();
		if (element.ViewW <= 0 || element.ViewH <= 0) {
			x = y = 0;
			ITexture? renderTarget = element.RenderTarget0;
			if (renderTarget == null) {
				shaderAPI.GetBackBufferDimensions(out width, out height);
			}
			else {
				width = renderTarget.GetActualWidth();
				height = renderTarget.GetActualHeight();
			}
		}
		else {
			x = element.ViewX;
			y = element.ViewY;
			width = element.ViewW;
			height = element.ViewH;
		}
	}

	public override void PushMatrix() {
		base.PushMatrix();
		shaderAPI.PushMatrix();
	}

	public override void Viewport(int x, int y, int width, int height) {
		base.Viewport(x, y, width, height);

		if ((width < 0) || (height < 0)) {
			ITexture? target = RenderTargetStack.Top().RenderTarget0;

			if (target == null) {
				ActiveViewport.TopLeftX = 0;
				ActiveViewport.TopLeftY = 0;
				shaderAPI.GetBackBufferDimensions(out ActiveViewport.Width, out ActiveViewport.Height);
				shaderAPI.SetViewports(new Span<ShaderViewport>(ref ActiveViewport));
			}
			else {
				ActiveViewport.TopLeftX = 0;
				ActiveViewport.TopLeftY = 0;
				ActiveViewport.Width = target.GetActualWidth();
				ActiveViewport.Height = target.GetActualHeight();
				shaderAPI.SetViewports(new Span<ShaderViewport>(ref ActiveViewport));
			}
		}
		else {
			ActiveViewport.TopLeftX = x;
			ActiveViewport.TopLeftY = y;
			ActiveViewport.Width = width;
			ActiveViewport.Height = height;
			shaderAPI.SetViewports(new Span<ShaderViewport>(ref ActiveViewport));
		}
	}

	public override void Bind(IMaterial iMaterial, object? proxyData) {
		if (iMaterial == null) {
			if (materials.errorMaterial == null)
				return;
			Warning("Programming error: MatRenderContext.Bind: NULL material\n");
			iMaterial = materials.errorMaterial;
		}
		else {
			// Proxy replacements?
		}

		IMaterialInternal material = (IMaterialInternal)iMaterial;
		material = material.GetRealTimeVersion(); // TODO: figure out how to do this.

		if (material == null) {
			Warning("Programming error: MatRenderContext.Bind NULL material\n");
			material = ((MaterialSystem)materials).errorMaterial;
		}

		base.Bind(material, proxyData);

		IMaterialInternal? matInternal = GetCurrentMaterialInternal();
		Assert(matInternal != null);

		SyncMatrices();
		if (materials.GetThreadMode() == MaterialThreadMode.SingleThreaded)
			matInternal!.CallBindProxy(proxyData);

		shaderAPI.Bind(matInternal);
	}

	public int GetCurrentNumBones() => shaderAPI.GetCurrentNumBones();

	public override void PopMatrix() {
		shaderAPI.PopMatrix(); // We need to tell ShaderAPI *NOW* so it can flush primitives trigger matrix sync etc
													 // ^^ is NOT source behavior. But I think, for all intents and purposes, it will act as such (we'll see if I eat my words on that)
		base.PopMatrix();
	}

	public IShaderAPI GetShaderAPI() {
		return materials.ShaderAPI;
	}

	public bool OnDrawMesh(IMesh mesh, int firstIndex, int indexCount) {
		SyncMatrices();
		return true;
	}

	public IMesh GetFlexMesh() => shaderAPI.GetFlexMesh();

	public IMesh GetDynamicMeshEx(VertexFormat vertexFormat, bool buffered = true, IMesh? vertexOverride = null, IMesh? indexOverride = null, IMaterial? autoBind = null) {
		if (autoBind != null)
			Bind(autoBind, null);

		int nCurrentBoneCount = shaderAPI.GetCurrentNumBones();
		Assert(nCurrentBoneCount <= 4);
		if (nCurrentBoneCount > 1)
			--nCurrentBoneCount;

		return shaderAPI.GetDynamicMeshEx(GetCurrentMaterialInternal()!, vertexFormat, nCurrentBoneCount, buffered, vertexOverride, indexOverride);
	}

	public IMesh GetDynamicMesh(bool buffered, IMesh? vertexOverride = null, IMesh? indexOverride = null, IMaterial? autoBind = null) {
		if (autoBind != null) {
			Bind(autoBind, null);
		}

		// For anything more than 1 bone, imply the last weight from the 1 - the sum of the others.
		int nCurrentBoneCount = shaderAPI.GetCurrentNumBones();
		Assert(nCurrentBoneCount <= 4);
		if (nCurrentBoneCount > 1) {
			--nCurrentBoneCount;
		}

		return shaderAPI.GetDynamicMesh(GetCurrentMaterialInternal()!, nCurrentBoneCount, buffered, vertexOverride, indexOverride);
	}

	IMesh? BatchIndices;
	IMesh? BatchMesh;

	public void BeginBatch(IMesh indices) {
		Assert(BatchMesh == null && BatchIndices == null);
		BatchIndices = indices;
	}

	public void BindBatch(IMesh vertices, IMaterial? autoBind = null) {
		BatchMesh = GetDynamicMesh(false, vertices, BatchIndices, autoBind);
	}

	public void DrawBatch(int firstIndex, int numIndices) {
		Assert(BatchMesh != null);
		BatchMesh!.Draw(firstIndex, numIndices);
	}

	public void EndBatch() {
		BatchIndices = null;
		BatchMesh = null;
	}

	struct PlaneStackElement
	{
		public InlineArray4<float> Values;
		public bool Hack_IsHeightClipPlane;
	}

	readonly List<PlaneStackElement> CustomClipPlanes = [];

	protected override void UpdateHeightClipUserClipPlane() {
		PlaneStackElement pse = default;
		pse.Hack_IsHeightClipPlane = true;

		int existingHeightClipPlaneIndex;
		for (existingHeightClipPlaneIndex = CustomClipPlanes.Count; --existingHeightClipPlaneIndex >= 0;) {
			if (CustomClipPlanes[existingHeightClipPlaneIndex].Hack_IsHeightClipPlane)
				break;
		}

		switch (HeightClipMode) {
			case MaterialHeightClipMode.Disable:
				if (existingHeightClipPlaneIndex != -1)
					CustomClipPlanes.RemoveAt(existingHeightClipPlaneIndex);
				break;
			case MaterialHeightClipMode.RenderAboveHeight:
				pse.Values[0] = 0.0f;
				pse.Values[1] = 0.0f;
				pse.Values[2] = 1.0f;
				pse.Values[3] = HeightClipZ;
				if (existingHeightClipPlaneIndex != -1)
					CustomClipPlanes[existingHeightClipPlaneIndex] = pse;
				else
					CustomClipPlanes.Add(pse);
				break;
			case MaterialHeightClipMode.RenderBelowHeight:
				pse.Values[0] = 0.0f;
				pse.Values[1] = 0.0f;
				pse.Values[2] = -1.0f;
				pse.Values[3] = -HeightClipZ;
				if (existingHeightClipPlaneIndex != -1)
					CustomClipPlanes[existingHeightClipPlaneIndex] = pse;
				else
					CustomClipPlanes.Add(pse);
				break;
		}

		ApplyCustomClipPlanes();
	}

	public void PushCustomClipPlane(ReadOnlySpan<float> plane) {
		PlaneStackElement pse = default;
		plane[..4].CopyTo(pse.Values);
		pse.Hack_IsHeightClipPlane = false;
		CustomClipPlanes.Add(pse);
		ApplyCustomClipPlanes();
	}

	public void PopCustomClipPlane() {
		Assert(CustomClipPlanes.Count != 0);

		int i;
		for (i = CustomClipPlanes.Count; --i >= 0;) {
			if (!CustomClipPlanes[i].Hack_IsHeightClipPlane) {
				CustomClipPlanes.RemoveAt(i);
				break;
			}
		}
		Assert(i != -1);
		ApplyCustomClipPlanes();
	}

	protected override void ApplyCustomClipPlanes() {
		int maxClipPlanes = materials.HardwareConfig.MaxUserClipPlanes();
		int customPlanes = EnableClippingValue ? CustomClipPlanes.Count : 0;

		float fakePlaneVal = BitConverter.Int32BitsToSingle(unchecked((int)0xFFFFFFFF));
		ReadOnlySpan<float> fakePlane = [fakePlaneVal, fakePlaneVal, fakePlaneVal, fakePlaneVal];

		SyncMatrices();

		Span<PlaneStackElement> planes = CollectionsMarshal.AsSpan(CustomClipPlanes);
		if (maxClipPlanes >= customPlanes) {
			int i;
			for (i = 0; i < customPlanes; ++i) {
				shaderAPI.SetClipPlane(i, planes[i].Values);
				shaderAPI.EnableClipPlane(i, true);
			}
			for (; i < maxClipPlanes; ++i) {
				shaderAPI.EnableClipPlane(i, false);
				shaderAPI.SetClipPlane(i, fakePlane);
			}
		}
		else {
			int customPlaneOffset = customPlanes - maxClipPlanes;

			for (int i = customPlaneOffset; i < customPlanes; ++i) {
				shaderAPI.SetClipPlane(i % maxClipPlanes, planes[i].Values);
				shaderAPI.EnableClipPlane(i % maxClipPlanes, true);
			}
		}
	}

	public bool InFlashlightMode() {
		return FlashlightEnable;
	}

	public override void SetToneMappingScaleLinear(in Vector3 scale) => shaderAPI.SetToneMappingScaleLinear(in scale);

	public void BeginFrame() => shaderAPI.BeginFrame();
	public void EndFrame() => shaderAPI.EndFrame();

	public void SwapBuffers() {
		materials.OcclusionQueryMgr.AdvanceFrame();
		materials.ShaderDevice.Present();
	}

	public bool OnSetPrimitiveType(IMesh mesh, MaterialPrimitiveType type) {
		return true;
	}

	public static bool ShouldValidateMatrices() => false;
	public static bool AllowLazyMatrixSync() => true;

	public void ForceSyncMatrix(MaterialMatrixMode mode) {
		ref MatrixStackItem top = ref MatrixStacks[(int)mode].Top();
		if (MatrixStacksDirtyStates[(int)mode]) {
			bool setMode = matrixMode != mode;
			if (setMode)
				shaderAPI.MatrixMode(mode);

			if (!top.Matrix.IsIdentity) {
				Matrix4x4 transposeTop = Matrix4x4.Transpose(top.Matrix);
				shaderAPI.LoadMatrix(in transposeTop);
			}
			else {
				shaderAPI.LoadIdentity();
			}

			if (setMode)
				shaderAPI.MatrixMode(matrixMode);

			MatrixStacksDirtyStates[(int)mode] = false;
		}
	}

	public override void SyncMatrices() {
		if (!ShouldValidateMatrices() && AllowLazyMatrixSync()) {
			for (int i = 0; i < (int)MaterialMatrixMode.Count; i++) {
				ref MatrixStackItem top = ref MatrixStacks[i].Top();
				if (MatrixStacksDirtyStates[i]) {
					shaderAPI.MatrixMode((MaterialMatrixMode)i);
					if (!top.Matrix.IsIdentity) {
						Matrix4x4 transposeTop = Matrix4x4.Transpose(top.Matrix);
						shaderAPI.LoadMatrix(in transposeTop);
					}
					else {
						shaderAPI.LoadIdentity();
					}

					MatrixStacksDirtyStates[i] = false;
				}
			}
		}
	}

	public override void SyncMatrix(MaterialMatrixMode mode) {
		if (!ShouldValidateMatrices() && AllowLazyMatrixSync())
			ForceSyncMatrix(mode);
	}

	protected override void CommitRenderTargetAndViewport() {
		Assert(RenderTargetStack.Count > 0);

		ref RenderTargetStackElement element = ref RenderTargetStack.Top();

		for (int rt = 0, size = element.Size; rt < size; rt++) {
			if (element[rt] == null) {
				shaderAPI.SetRenderTargetEx(rt);

				if (rt == 0) {
					if ((element.ViewW < 0) || (element.ViewH < 0)) {
						ActiveViewport.TopLeftX = 0;
						ActiveViewport.TopLeftY = 0;
						shaderAPI.GetBackBufferDimensions(out ActiveViewport.Width, out ActiveViewport.Height);
						shaderAPI.SetViewports(new Span<ShaderViewport>(ref ActiveViewport));
					}
					else {
						ActiveViewport.TopLeftX = element.ViewX;
						ActiveViewport.TopLeftY = element.ViewY;
						ActiveViewport.Width = element.ViewW;
						ActiveViewport.Height = element.ViewH;
						shaderAPI.SetViewports(new Span<ShaderViewport>(ref ActiveViewport));
					}
				}
			}
			else {
				ITextureInternal texInt = (ITextureInternal)element[rt]!;
				texInt.SetRenderTarget(rt, element.DepthTexture);

				if (rt == 0) {
					if (element[rt]!.GetImageFormat() == Common.Bitmap.ImageFormat.RGBA16161616F)
						shaderAPI.EnableLinearColorSpaceFrameBuffer(true);
					else
						shaderAPI.EnableLinearColorSpaceFrameBuffer(false);

					if ((element.ViewW < 0) || (element.ViewH < 0)) {
						ActiveViewport.TopLeftX = 0;
						ActiveViewport.TopLeftY = 0;
						ActiveViewport.Width = element[rt]!.GetActualWidth();
						ActiveViewport.Height = element[rt]!.GetActualHeight();
						shaderAPI.SetViewports(new Span<ShaderViewport>(ref ActiveViewport));
					}
					else {
						ActiveViewport.TopLeftX = element.ViewX;
						ActiveViewport.TopLeftY = element.ViewY;
						ActiveViewport.Width = element.ViewW;
						ActiveViewport.Height = element.ViewH;
						shaderAPI.SetViewports(new Span<ShaderViewport>(ref ActiveViewport));
					}
				}
			}
		}
	}

	public void CopyRenderTargetToTexture(ITexture texture) => CopyRenderTargetToTextureEx(texture, 0, ref Unsafe.NullRef<System.Drawing.Rectangle>(), ref Unsafe.NullRef<System.Drawing.Rectangle>());

	public void CopyRenderTargetToTextureEx(ITexture? texture, int renderTargetID, ref System.Drawing.Rectangle srcRect, ref System.Drawing.Rectangle dstRect) {
		if (texture == null) {
			Assert(false);
			return;
		}

		Flush(false);
		((ITextureInternal)texture).CopyFrameBufferToMe(renderTargetID, Unsafe.IsNullRef(ref srcRect) ? null : srcRect, Unsafe.IsNullRef(ref dstRect) ? null : dstRect);
	}

	public void GetWindowSize(out int w, out int h) {
		shaderAPI.GetBackBufferDimensions(out w, out h);
	}

	public IMesh CreateStaticMesh(VertexFormat format, ReadOnlySpan<char> textureGroup, IMaterial? material) {
		return materials.ShaderDevice.CreateStaticMesh(format, textureGroup, material);
	}

	public void DestroyStaticMesh(IMesh mesh) {
		materials.ShaderDevice.DestroyStaticMesh(mesh);
	}

	public int GetMaxVerticesToRender(IMaterial material) => materials.ShaderAPI.GetMaxVerticesToRender(material);
	public int GetMaxIndicesToRender() => materials.ShaderAPI.GetMaxIndicesToRender();

	public void SetNumBoneWeights(int numBones) {
		shaderAPI.SetNumBoneWeights(numBones);
	}

	public void SetAmbientLightCube(Span<Vector4> cube) => shaderAPI.SetAmbientLightCube(cube);

	public void LoadBoneMatrix(int boneIndex, in Matrix3x4 matrix) {
		shaderAPI.LoadBoneMatrix(boneIndex, in matrix);
	}

	public override void BindLightmapPage(int lightmapPageID) {
		if (lightmapPageID == LightmapPageID)
			return;

		shaderAPI.FlushBufferedPrimitives();
		base.BindLightmapPage(lightmapPageID);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetBlackTextureHandle() => materials.GetBlackTextureHandle();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetFlatNormalTextureHandle() => materials.GetFlatNormalTextureHandle();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetGreyTextureHandle() => materials.GetGreyTextureHandle();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetGreyAlphaZeroTextureHandle() => materials.GetGreyAlphaZeroTextureHandle();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetWhiteTextureHandle() => materials.GetWhiteTextureHandle();

	readonly ITextureManager TextureSystem = (Singleton<ITextureManager>() as TextureManager)!;
	public void BindStandardTexture(Sampler sampler, StandardTextureId id) {
		switch (id) {
			case StandardTextureId.Lightmap: BindLightmap(sampler); break;
			case StandardTextureId.White: shaderAPI.BindTexture(sampler, GetWhiteTextureHandle()); break;
			case StandardTextureId.NormalMapFlat: shaderAPI.BindTexture(sampler, GetFlatNormalTextureHandle()); break;
			case StandardTextureId.Black: shaderAPI.BindTexture(sampler, GetBlackTextureHandle()); break;
			case StandardTextureId.Grey: shaderAPI.BindTexture(sampler, GetGreyTextureHandle()); break;
			case StandardTextureId.GreyAlphaZero: shaderAPI.BindTexture(sampler, GetGreyAlphaZeroTextureHandle()); break;
			case StandardTextureId.NormalizationCubemapSigned: TextureSystem.SignedNormalizationCubemap().Bind(sampler); break;
			case StandardTextureId.FrameBufferFullTexture0:
			case StandardTextureId.FrameBufferFullTexture1: {
					int textureIndex = id - StandardTextureId.FrameBufferFullTexture0;
					if (CurrentFrameBufferCopyTexture[textureIndex] != null)
						((ITextureInternal)CurrentFrameBufferCopyTexture[textureIndex]!).Bind(sampler);
				}
				break;
			default: Assert(false); break;
		}
	}

	public override void SetFrameBufferCopyTexture(ITexture? texture, int textureIndex = 0) {
		if (textureIndex < 0 || textureIndex >= MAX_FB_TEXTURES) {
			Assert(false);
			return;
		}

		if (CurrentFrameBufferCopyTexture[textureIndex] != texture)
			shaderAPI.FlushBufferedPrimitives();

		CurrentFrameBufferCopyTexture[textureIndex] = texture;
	}

	public override void BindLocalCubemap(ITexture? texture) {
		ITexture? previousTexture = LocalCubemapTexture;

		base.BindLocalCubemap(texture);

		if (LocalCubemapTexture != previousTexture)
			shaderAPI.FlushBufferedPrimitives();
	}

	public ITexture? GetLocalCubemap() => LocalCubemapTexture;

	public void SetLightingOrigin(Vector3 lightingOrigin) => shaderAPI.SetLightingOrigin(lightingOrigin);
	public void SetAmbientLight(float r, float g, float b) => shaderAPI.SetAmbientLight(r, g, b);
	public void SetLight(int lightNum, in LightDesc desc) => shaderAPI.SetLight(lightNum, desc);
	public void DisableAllLocalLights() => shaderAPI.DisableAllLocalLights();
	public int GetMaxLights() => shaderAPI.GetMaxLights();

	public void SetFlashlightMode(bool enable) {
		if (enable != FlashlightEnable) {
			shaderAPI.FlushBufferedPrimitives();
			FlashlightEnable = enable;
		}
	}
	public bool GetFlashlightMode() => FlashlightEnable;
	public void SetFlashlightState(in FlashlightState state, in Matrix4x4 worldToTexture) => SetFlashlightStateEx(state, worldToTexture, null);
	public void SetFlashlightStateEx(in FlashlightState state, in Matrix4x4 worldToTexture, ITexture? flashlightDepthTexture) => shaderAPI.SetFlashlightStateEx(state, worldToTexture, flashlightDepthTexture);

	public void SetStencilEnable(bool onoff) => shaderAPI.SetStencilEnable(onoff);
	public void SetStencilFailOperation(StencilOperation op) => shaderAPI.SetStencilFailOperation(op);
	public void SetStencilZFailOperation(StencilOperation op) => shaderAPI.SetStencilZFailOperation(op);
	public void SetStencilPassOperation(StencilOperation op) => shaderAPI.SetStencilPassOperation(op);
	public void SetStencilCompareFunction(StencilComparisonFunction cmpfn) => shaderAPI.SetStencilCompareFunction(cmpfn);
	public void SetStencilReferenceValue(int reference) => shaderAPI.SetStencilReferenceValue(reference);
	public void SetStencilTestMask(uint msk) => shaderAPI.SetStencilTestMask(msk);
	public void SetStencilWriteMask(uint msk) => shaderAPI.SetStencilWriteMask(msk);
	public void SetScissorRect(int left, int top, int right, int bottom, bool enableScissor) => shaderAPI.SetScissorRect(left, top, right, bottom, enableScissor);

	readonly TextureReference[] NonInteractiveTempFullscreenBuffer = [new(), new()];

	public void SetNonInteractiveTempFullscreenBuffer(ITexture? texture, MaterialNonInteractiveMode mode) {
		if (mode != MaterialNonInteractiveMode.None)
			NonInteractiveTempFullscreenBuffer[(int)mode].Init(texture);
	}

	public MatLightmaps GetLightmaps() => materials.MatLightmaps;

	public void BindLightmap(Sampler sampler) {
		switch (LightmapPageID) {
			default:
				Assert((LightmapPageID == 0 && GetLightmaps().GetNumLightmapPages() == 0) || (LightmapPageID >= 0 && LightmapPageID < GetLightmaps().GetNumLightmapPages()));
				if (LightmapPageID >= 0 && LightmapPageID < GetLightmaps().GetNumLightmapPages())
					shaderAPI.BindTexture(sampler, GetLightmaps().GetLightmapPageTextureHandle(LightmapPageID));

				break;

			case StandardLightmap.UserDefined:
				// shaderAPI.BindTexture(sampler, UserDefinedLightmap.GetTextureHandle(0));
				break;

			case StandardLightmap.White:
				break;

			case StandardLightmap.WhiteBump:
				break;
		}
	}

	public void AccumulateMorph(IMorph morph, ReadOnlySpan<MorphWeight> weights) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public ColorCorrectionHandle_t AddLookup(ReadOnlySpan<char> name) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void BeginMorphAccumulation() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void BeginOcclusionQueryDrawing(OcclusionQueryObjectHandle_t handle) => materials.OcclusionQueryMgr.BeginOcclusionQueryDrawing(handle);

	public void BeginPIXEvent(Color color, ReadOnlySpan<char> name) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void BindLightmatexture(ITexture? lightmapTexture) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void BindMorph(IMorph morph) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ClearBuffersObeyStencil(bool clearColor, bool clearDepth) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ClearBuffersObeyStencilEx(bool clearColor, bool clearAlpha, bool clearDepth) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ClearSelectionNames() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ClearStencilBufferRectangle(int xmin, int ymin, int xmax, int ymax, int value) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public int CompareMaterialCombos(IMaterial? material1, IMaterial? material2, int lightMapID1, int lightMapID2) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void CopyTextureToRenderTargetEx(int renderTargetID, ITexture? texture, ref System.Drawing.Rectangle pSrcRect, ref System.Drawing.Rectangle pDstRect) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public IMorph CreateMorph(MorphFormatFlags format, ReadOnlySpan<char> debugName) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public OcclusionQueryObjectHandle_t CreateOcclusionQueryObject() {
		OcclusionQueryObjectHandle_t h = materials.OcclusionQueryMgr.CreateOcclusionQueryObject();
		materials.OcclusionQueryMgr.OnCreateOcclusionQueryObject(h);
		return h;
	}

	public void CullMode(MaterialCullMode cullMode) => shaderAPI.CullMode(cullMode);

	public void DestroyMorph(IMorph morph) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void DestroyOcclusionQueryObject(OcclusionQueryObjectHandle_t handle) => materials.OcclusionQueryMgr.DestroyOcclusionQueryObject(handle);

	public void DrawScreenSpaceQuad(IMaterial? material) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void DrawScreenSpaceRectangle( IMaterial? material, int destX, int destY, int width, int height, float srcTextureX0, float srcTextureY0, float srcTextureX1, float srcTextureY1, int srcTextureWidth, int srcTextureHeight, IClientRenderable? clientRenderable = null, int xDice = 1, int yDice = 1) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void EnableColorCorrection(bool enable) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void EnableNonInteractiveMode(MaterialNonInteractiveMode mode) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void EnableUserClipTransformOverride(bool enable) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void EndMorphAccumulation() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void EndOcclusionQueryDrawing(OcclusionQueryObjectHandle_t handle) => materials.OcclusionQueryMgr.EndOcclusionQueryDrawing(handle);

	public void EndPIXEvent() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void FogColor3f(float r, float g, float b) {
		byte r8 = (byte)Math.Clamp((int)(r * 255.0f), 0, 255);
		byte g8 = (byte)Math.Clamp((int)(g * 255.0f), 0, 255);
		byte b8 = (byte)Math.Clamp((int)(b * 255.0f), 0, 255);
		shaderAPI.SceneFogColor3ub(r8, g8, b8);
	}

	public void FogColor3fv(ReadOnlySpan<float> rgb) {
		byte r8 = (byte)Math.Clamp((int)(rgb[0] * 255.0f), 0, 255);
		byte g8 = (byte)Math.Clamp((int)(rgb[1] * 255.0f), 0, 255);
		byte b8 = (byte)Math.Clamp((int)(rgb[2] * 255.0f), 0, 255);
		shaderAPI.SceneFogColor3ub(r8, g8, b8);
	}

	public void FogColor3ub(byte r, byte g, byte b) => shaderAPI.SceneFogColor3ub(r, g, b);

	public void FogColor3ubv(ReadOnlySpan<byte> rgb) => shaderAPI.SceneFogColor3ub(rgb[0], rgb[1], rgb[2]);

	public void FogEnd(float end) => shaderAPI.FogEnd(end);

	public void FogMaxDensity(float maxDensity) => shaderAPI.FogMaxDensity(maxDensity);

	public void FogMode(MaterialFogMode fogMode) => shaderAPI.SceneFogMode(fogMode);

	public void FogStart(float start) => shaderAPI.FogStart(start);

	public void FogRadial(bool radial) => shaderAPI.SceneFogRadial(radial);

	public bool GetFogRadial() => shaderAPI.GetSceneFogRadial();

	public void GMOD_FlushQueue() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public ICallQueue? GetCallQueue() => null;

	public void GetFogColor(out Color rgb) => shaderAPI.GetSceneFogColor(out rgb);

	public void GetFogDistances(out float start, out float end, out float fogZ) => shaderAPI.GetFogDistances(out start, out end, out fogZ);

	public MaterialFogMode GetFogMode() => shaderAPI.GetSceneFogMode();

	public void GetMaxToRender(IMesh? pMesh, bool bMaxUntilFlush, Span<int> maxVerts, Span<int> maxIndices) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public bool GetMorphAccumulatorTexCoord(out Vector2 texCoord, IMorph morph, int vertex) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public int GetNumActiveDeformations() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void LoadLookup(ColorCorrectionHandle_t handle, ReadOnlySpan<char> lookuname) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void LoadSelectionName(int name) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void LockLookup(ColorCorrectionHandle_t handle) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public E? LockRenderDataTyped<E>(int count, E? srcData = null) where E : class {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public int OcclusionQuery_GetNumPixelsRendered(OcclusionQueryObjectHandle_t handle) => materials.OcclusionQueryMgr.OcclusionQuery_GetNumPixelsRendered(handle, true);

	public void OverrideAlphaWriteEnable(bool enable, bool alphaWriteEnable) => shaderAPI.OverrideAlphaWriteEnable(enable, alphaWriteEnable);

	public void OverrideBlend(bool overrideEnable, bool useSeparateAlpha, int srcBlend, int destBlend, int blendFunc) => shaderAPI.OverrideBlend(overrideEnable, useSeparateAlpha, srcBlend, destBlend, blendFunc);

	public void OverrideBlendSeparateAlpha(bool overrideEnable, bool useSeparateAlpha, int srcBlend, int destBlend, int blendFunc) => shaderAPI.OverrideBlendSeparateAlpha(overrideEnable, useSeparateAlpha, srcBlend, destBlend, blendFunc);

	public void OverrideColorWriteEnable(bool overrideEnable, bool colorWriteEnable) => shaderAPI.OverrideColorWriteEnable(overrideEnable, colorWriteEnable);

	public void OverrideDepthEnable(bool enable, bool depthEnable) => shaderAPI.OverrideDepthEnable(enable, depthEnable);

	public void PerformFullScreenStencilOperation() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PopDeformation() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PopSelectionName() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PushDeformation(ref readonly DeformationBase deformation) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PushSelectionName(int name) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ReadPixels(int x, int y, int width, int height, Span<byte> data, ImageFormat dstFormat) => shaderAPI.ReadPixels(x, y, width, height, data, dstFormat);

	public void ReadPixelsAndStretch(ref System.Drawing.Rectangle srcRect, ref System.Drawing.Rectangle pDstRect, Span<byte> buffer, ImageFormat dstFormat, int dstStride) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void RefreshFrontBufferNonInteractive() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public bool RemoveLookup(ColorCorrectionHandle_t handle) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ResetLookupWeights() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ResetOcclusionQueryObject(OcclusionQueryObjectHandle_t handle) => materials.OcclusionQueryMgr.ResetOcclusionQueryObject(handle);

	public void SelectionBuffer(Span<uint> buffer) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public int SelectionMode(bool selectionMode) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetFlexWeights(int firstWeight, ReadOnlySpan<MorphWeight> weights) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetFloatRenderingParameter(int parm_number, float value) => shaderAPI.SetFloatRenderingParameter(parm_number, value);

	public void SetFogZ(float fogZ) => shaderAPI.SetFogZ(fogZ);

	public void SetFullScreenDepthTextureValidityFlag(bool isValid) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetIntRenderingParameter(int parm_number, int value) => shaderAPI.SetIntRenderingParameter(parm_number, value);

	public void SetLookupWeight(ColorCorrectionHandle_t handle, float weight) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetNonInteractivePacifierTexture(ITexture? texture, float normalizedX, float normalizedY, float normalizedSize) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetPIXMarker(Color color, ReadOnlySpan<char> name) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public override void SetRenderTargetEx(int renderTargetID, ITexture? newTarget) {
		Assert(RenderTargetStack.Count > 0);

		ITexture? oldTarget = RenderTargetStack.Top()[renderTargetID];

		base.SetRenderTargetEx(renderTargetID, newTarget);

		if (newTarget != oldTarget) {
			if (newTarget == null) {
				if (renderTargetID == 0) {
					ActiveViewport.TopLeftX = 0;
					ActiveViewport.TopLeftY = 0;
					shaderAPI.GetBackBufferDimensions(out ActiveViewport.Width, out ActiveViewport.Height);
					shaderAPI.SetViewports(new Span<ShaderViewport>(ref ActiveViewport));
				}
				shaderAPI.SetRenderTargetEx(renderTargetID);
			}
			else {
				bool reset = true;
				if (renderTargetID == 0) {
					ActiveViewport.TopLeftX = 0;
					ActiveViewport.TopLeftY = 0;
					ActiveViewport.Width = newTarget.GetActualWidth();
					ActiveViewport.Height = newTarget.GetActualHeight();
					shaderAPI.SetViewports(new Span<ShaderViewport>(ref ActiveViewport));
				}
				if (newTarget is ITextureInternal texInt) {
					reset = !texInt.SetRenderTarget(renderTargetID);
					if (reset)
						shaderAPI.SetRenderTargetEx(renderTargetID);
				}

				if (newTarget.GetImageFormat() == ImageFormat.RGBA16161616F)
					shaderAPI.EnableLinearColorSpaceFrameBuffer(true);
				else
					shaderAPI.EnableLinearColorSpaceFrameBuffer(false);
			}
		}
		CommitRenderTargetAndViewport();
	}

	public void SetResetable(ColorCorrectionHandle_t handle, bool resetable) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetShadowDepthBiasFactors(float slopeScaleDepthBias, float depthBias) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetVectorRenderingParameter(int parm_number, in Vector3 value) => shaderAPI.SetVectorRenderingParameter(parm_number, value);

	public void SyncToken(ReadOnlySpan<char> token) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void UnlockLookup(ColorCorrectionHandle_t handle) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void UserClipTransform(in Matrix4x4 worldToView) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void DrawClearBufferQuad(byte r, byte g, byte b, byte a, bool clearColor, bool clearAlpha, bool clearDepth) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public void ForceDepthFuncEquals(bool force) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public void ForceHardwareSync() {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public MorphFormatFlags GetBoundMorphFormat() {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public MatCallQueue? GetCallQueueInternal() => null;

	public float GetFloatRenderingParameter(int parmNumber) => shaderAPI.GetFloatRenderingParameter(parmNumber);

	public int GetIntRenderingParameter(int parmNumber) => shaderAPI.GetIntRenderingParameter(parmNumber);

	public void GetLightmapDimensions(out int w, out int h) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public Vector3 GetVectorRenderingParameter(int parmNumber) => shaderAPI.GetVectorRenderingParameter(parmNumber);

	public unsafe bool OnDrawMesh(IMesh mesh, PrimList* pLists, int nLists) {
		SyncMatrices();
		return true;
	}

	public bool OnFlushBufferedPrimitives() => true;

	public bool OnSetColorMesh(IMesh staticMesh, IMesh? mesh, int nVertexOffsetInBytes) => true;

	public bool OnSetFlexMesh(IMesh staticMesh, IMesh? mesh, int nVertexOffsetInBytes) => true;

	public int AddRef() {
		throw new NotImplementedException("Incomplete port of IRefCounted");
	}

	public int Release() {
		throw new NotImplementedException("Incomplete port of IRefCounted");
	}
}
