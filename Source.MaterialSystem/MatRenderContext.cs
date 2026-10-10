using Source.Common;
using Source.Common.Bitmap;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.Utilities;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.MaterialSystem;

public class MatRenderContext : IMatRenderContextInternal
{
	readonly MaterialSystem materials;
	readonly IShaderAPI shaderAPI;

	public MatRenderContext(MaterialSystem materials) {
		this.materials = materials;
		RenderTargetStack = new RefStack<RenderTargetStackElement>();
		MatrixStacks = new RefStack<MatrixStackItem>[(int)MaterialMatrixMode.Count];
		MatrixStacksDirtyStates = new bool[(int)MaterialMatrixMode.Count];
		for (int i = 0; i < MatrixStacks.Length; i++) {
			MatrixStacks[i] = new();
			ref MatrixStackItem item = ref MatrixStacks[i].Push();
			MatrixStacksDirtyStates[i] = true;
			item.Matrix = Matrix4x4.Identity;
		}
		RenderTargetStackElement initialElement = new() {
			DepthTexture = null,
			ViewX = 0,
			ViewY = 0,
			ViewW = -1,
			ViewH = -1
		};
		RenderTargetStack.Push(initialElement);
		shaderAPI = materials.ShaderAPI;

		DirtyViewState = true;
		DirtyViewProjState = true;
	}
	RefStack<RenderTargetStackElement> RenderTargetStack;
	RefStack<MatrixStackItem>[] MatrixStacks;
	bool[] MatrixStacksDirtyStates;
	MaterialMatrixMode matrixMode;
	ShaderViewport ActiveViewport = new(0, 0, 0, 0, 0, 1);
	public ref MatrixStackItem CurMatrixItem => ref MatrixStacks[(int)matrixMode].Top();
	public ref RenderTargetStackElement CurRenderTargetStack => ref RenderTargetStack.Top();

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

	public void Flush(bool flushHardware = false) {
		shaderAPI.FlushBufferedPrimitives();
	}

	public void GMOD_ForceFilterMode(bool min, int mode) => shaderAPI.GMOD_ForceFilterMode(min, mode);

	public void GetViewport(out int x, out int y, out int width, out int height) {
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

	public void LoadIdentity() {
		ref MatrixStackItem item = ref CurMatrixItem;
		item.Matrix = Matrix4x4.Identity;
		CurrentMatrixChanged();
	}

	public void LoadMatrix(in Matrix4x4 matrix) {
		ref MatrixStackItem item = ref CurMatrixItem;
		item.Matrix = matrix;
		CurrentMatrixChanged();
	}

	public void LoadMatrix(Matrix3x4 matrix) {
		ref MatrixStackItem item = ref CurMatrixItem;
		item.Matrix = matrix;
		CurrentMatrixChanged();
	}

	private void MarkDirty() => MatrixStacksDirtyStates[(int)matrixMode] = true;

	public void MatrixMode(MaterialMatrixMode mode) {
		matrixMode = mode;
	}

	public void PushMatrix() {
		RefStack<MatrixStackItem> curStack = MatrixStacks[(int)matrixMode];
		curStack.Push(CurMatrixItem);
		CurrentMatrixChanged();
		shaderAPI.PushMatrix();
	}

	private void CurrentMatrixChanged() {
		MarkDirty();

		if (matrixMode == MaterialMatrixMode.View) {
			DirtyViewState = true;
			DirtyViewProjState = true;
		}
		else if (matrixMode == MaterialMatrixMode.Projection)
			DirtyViewProjState = true;
	}

	public void Viewport(int x, int y, int width, int height) {
		Dbg.Assert(RenderTargetStack.Count > 0);

		RenderTargetStackElement newTOS = new();
		newTOS = CurRenderTargetStack; // copy
		newTOS.ViewX = x;
		newTOS.ViewY = y;
		newTOS.ViewW = width;
		newTOS.ViewH = height;
		RenderTargetStack.Pop();
		RenderTargetStack.Push(newTOS);

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

	IMaterialInternal? currentMaterial;

	public void Bind(IMaterial iMaterial, object? proxyData) {
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

		SyncMatrices();

		if (material == null) {
			Warning("Programming error: MatRenderContext.Bind NULL material\n");
			material = ((MaterialSystem)materials).errorMaterial;
		}

		if (GetCurrentMaterialInternal() != material) {
			if (!material.IsPrecached()) {
				material.Precache();
			}
			SetCurrentMaterialInternal(material);
		}
		SetCurrentProxy(proxyData);

		if (materials.GetThreadMode() == MaterialThreadMode.SingleThreaded)
			GetCurrentMaterialInternal()!.CallBindProxy(proxyData);

		shaderAPI.Bind(GetCurrentMaterialInternal());
	}

	public IMaterialInternal? GetCurrentMaterialInternal() {
		return currentMaterial;
	}
	public void SetCurrentMaterialInternal(IMaterialInternal? mat) {
		currentMaterial = mat;
	}

	public IMaterial? GetCurrentMaterial() {
		return currentMaterial;
	}

	object? CurrentProxyData;
	public object? GetCurrentProxy() => CurrentProxyData;
	public void SetCurrentProxy(object? proxyData) => CurrentProxyData = proxyData;

	public int GetCurrentNumBones() => shaderAPI.GetCurrentNumBones();

	public void PopMatrix() {
		shaderAPI.PopMatrix(); // We need to tell ShaderAPI *NOW* so it can flush primitives trigger matrix sync etc
													 // ^^ is NOT source behavior. But I think, for all intents and purposes, it will act as such (we'll see if I eat my words on that)
		RefStack<MatrixStackItem> curStack = MatrixStacks[(int)matrixMode];
		curStack.Pop();
		CurrentMatrixChanged();
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

	bool FlashlightEnable;
	bool DirtyViewState;
	bool DirtyViewProjState;
	bool EnableClippingValue;
	MaterialHeightClipMode HeightClipMode;

	public bool EnableClipping(bool enable) {
		if (enable != EnableClippingValue) {
			EnableClippingValue = enable;
			return !enable;
		}
		return enable;
	}

	public MaterialHeightClipMode GetHeightClipMode() => HeightClipMode;

	public void SetHeightClipMode(MaterialHeightClipMode heightClipMode) {
		if (HeightClipMode != heightClipMode)
			HeightClipMode = heightClipMode;
	}

	public bool InFlashlightMode() {
		return FlashlightEnable;
	}

	float CurToneMapScale = 1.0f;

	public void TurnOnToneMapping() => SetToneMappingScaleLinear(new(CurToneMapScale, CurToneMapScale, CurToneMapScale));

	public void SetToneMappingScaleLinear(in Vector3 scale) => shaderAPI.SetToneMappingScaleLinear(in scale);

	public void BeginFrame() => shaderAPI.BeginFrame();
	public void EndFrame() => shaderAPI.EndFrame();

	public void MarkRenderDataUnused(bool v) {

	}

	public void SetFrameTime(double frameTime) {

	}

	public void SwapBuffers() {
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

	public void SyncMatrices() {
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

	public void SyncMatrix(MaterialMatrixMode mode) {
		if (!ShouldValidateMatrices() && AllowLazyMatrixSync())
			ForceSyncMatrix(mode);
	}

	public void Translate(float x, float y, float z) {
		MathLib.MatrixBuildTranslation(out Matrix4x4 mat, x, y, z);
		MultMatrixLocal(in mat);
	}

	public void Scale(float x, float y, float z) {
		MathLib.MatrixBuildScale(out Matrix4x4 mat, x, y, z);
		MultMatrixLocal(in mat);
	}


	public void MultMatrixLocal(in Matrix4x4 mat) {
		MathLib.MatrixMultiply(in CurMatrixItem.Matrix, in mat, out Matrix4x4 result);
		CurMatrixItem.Matrix = result;
		CurrentMatrixChanged();
	}

	public void Ortho(double left, double top, double right, double bottom, double near, double far) {
		ref Matrix4x4 item = ref CurMatrixItem.Matrix;
		MathLib.MatrixOrtho(ref item, (float)left, (float)right, (float)bottom, (float)top, (float)near, (float)far);
		CurrentMatrixChanged();
	}

	private void CommitRenderTargetAndViewport() {
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

	public void PushRenderTargetAndViewport(ITexture? thisTexture, int x, int y, int w, int h) {
		RenderTargetStackElement element = new(thisTexture, x, y, w, h);
		RenderTargetStack.Push(element);
		CommitRenderTargetAndViewport();
	}

	public void PushRenderTargetAndViewport(ITexture? colorTexture, ITexture? depthTexture, int x, int y, int w, int h) {
		RenderTargetStackElement element = new(colorTexture, depthTexture, x, y, w, h);
		RenderTargetStack.Push(element);
		CommitRenderTargetAndViewport();
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

	public void PushRenderTargetAndViewport(ITexture? thisTexture) {
		RenderTargetStackElement element = new(thisTexture, 0, 0, -1, -1);
		RenderTargetStack.Push(element);
		CommitRenderTargetAndViewport();
	}

	public void PopRenderTargetAndViewport() {
		if (RenderTargetStack.Count == 0) {
			AssertMsg(false, "MatRenderContext.PopRenderTargetAndViewport: Stack is empty!\n");
			return;
		}

		Flush();

		RenderTargetStack.Pop();
		CommitRenderTargetAndViewport();
	}

	public void GetWindowSize(out int w, out int h) {
		shaderAPI.GetBackBufferDimensions(out w, out h);
	}

	public ITexture? GetRenderTarget() {
		if (RenderTargetStack.Count > 0)
			return RenderTargetStack.Top().RenderTarget0;
		else
			return null;
	}

	public IMesh CreateStaticMesh(VertexFormat format, ReadOnlySpan<char> textureGroup, IMaterial? material) {
		return materials.ShaderDevice.CreateStaticMesh(format, textureGroup, material);
	}

	public void DestroyStaticMesh(IMesh mesh) {
		materials.ShaderDevice.DestroyStaticMesh(mesh);
	}

	public int GetMaxVerticesToRender(IMaterial material) => materials.ShaderAPI.GetMaxVerticesToRender(material);
	public int GetMaxIndicesToRender() => materials.ShaderAPI.GetMaxIndicesToRender();

	public float ComputePixelDiameterOfSphere(in Vector3 absOrigin, float radius) {
		RecomputeViewState();
		RecomputeViewProjState();
		// This is sort of faked, but it's faster that way
		// FIXME: Also, there's a much faster way to do this with similar triangles
		// but I want to make sure it exactly matches the current matrices, so
		// for now, I do it this conservative way
		Vector4 testPoint1 = default, testPoint2 = default;
		MathLib.VectorMA(in absOrigin, radius, in VecViewUp, out testPoint1.AsVector3D());
		MathLib.VectorMA(in absOrigin, -radius, in VecViewUp, out testPoint2.AsVector3D());
		testPoint1.W = testPoint2.W = 1.0f;

		Vector4 clipPos1 = default, clipPos2 = default;
		MathLib.Vector4DMultiply(ViewProjMatrix, in testPoint1, ref clipPos1);
		MathLib.Vector4DMultiply(ViewProjMatrix, in testPoint2, ref clipPos2);

		if (clipPos1.W >= 0.001f)
			clipPos1.Y /= clipPos1.W;
		else
			clipPos1.Y *= 1000;

		if (clipPos2.W >= 0.001f)
			clipPos2.Y /= clipPos2.W;
		else
			clipPos2.Y *= 1000;

		GetViewport(out int vx, out int vy, out int vwidth, out int vheight);

		return vheight * MathF.Abs(clipPos2.Y - clipPos1.Y) / 2.0f;
	}

	Vector3 VecViewOrigin;
	Vector3 VecViewForward;
	Vector3 VecViewRight;
	Vector3 VecViewUp;

	private void RecomputeViewProjState() {
		if (DirtyViewProjState) {
			Matrix4x4 viewMatrix, projMatrix;

			// FIXME: Should consider caching this upon change for projection or view matrix.
			GetMatrix(MaterialMatrixMode.View, out viewMatrix);
			GetMatrix(MaterialMatrixMode.Projection, out projMatrix);
			ViewProjMatrix = projMatrix * viewMatrix;
			DirtyViewProjState = false;
		}
	}
	Matrix4x4 ViewProjMatrix;

	private void RecomputeViewState() {
		if (!DirtyViewState)
			return;
		DirtyViewState = false;

		// FIXME: Cache this off to make it less expensive?
		Matrix4x4 viewMatrix;
		GetMatrix(MaterialMatrixMode.View, out viewMatrix);
		VecViewOrigin.X =
			-(viewMatrix[0][3] * viewMatrix[0][0] +
			viewMatrix[1][3] * viewMatrix[1][0] +
			viewMatrix[2][3] * viewMatrix[2][0]);
		VecViewOrigin.Y =
			-(viewMatrix[0][3] * viewMatrix[0][1] +
			viewMatrix[1][3] * viewMatrix[1][1] +
			viewMatrix[2][3] * viewMatrix[2][1]);
		VecViewOrigin.Z =
			-(viewMatrix[0][3] * viewMatrix[0][2] +
			viewMatrix[1][3] * viewMatrix[1][2] +
			viewMatrix[2][3] * viewMatrix[2][2]);

		// FIXME Implement computation of m_vecViewForward, etc
		VecViewForward = new();
		VecViewRight = new();

		// FIXME: Is this correct?
		VecViewUp = new(viewMatrix[1][0], viewMatrix[1][1], viewMatrix[1][2]);
	}

	public void GetMatrix(MaterialMatrixMode mode, out Matrix4x4 viewMatrix) {
		var stack = MatrixStacks[(int)mode];
		if (stack.Count == 0) {
			viewMatrix = Matrix4x4.Identity;
			return;
		}

		viewMatrix = stack.Top().Matrix;
	}

	public float ComputePixelWidthOfSphere(in Vector3 origin, float radius) {
		return ComputePixelDiameterOfSphere(origin, radius) * 2.0f;
	}

	public void SetNumBoneWeights(int numBones) {
		shaderAPI.SetNumBoneWeights(numBones);
	}

	public void SetAmbientLightCube(Span<Vector4> cube) => shaderAPI.SetAmbientLightCube(cube);

	public void LoadBoneMatrix(int boneIndex, in Matrix3x4 matrix) {
		shaderAPI.LoadBoneMatrix(boneIndex, in matrix);
	}

	public void GetWorldSpaceCameraPosition(out Vector3 vecCameraPos) {
		vecCameraPos = VecViewOrigin;
	}

	int LightmapPageID;
	public void BindLightmapPage(int lightmapPageID) {
		if (lightmapPageID == LightmapPageID)
			return;

		shaderAPI.FlushBufferedPrimitives();
		LightmapPageID = lightmapPageID;
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

	public const int MAX_FB_TEXTURES = 4;
	readonly ITexture?[] CurrentFrameBufferCopyTexture = new ITexture?[MAX_FB_TEXTURES];

	public void SetFrameBufferCopyTexture(ITexture? texture, int textureIndex = 0) {
		if (textureIndex < 0 || textureIndex >= MAX_FB_TEXTURES) {
			Assert(false);
			return;
		}

		if (CurrentFrameBufferCopyTexture[textureIndex] != texture)
			shaderAPI.FlushBufferedPrimitives();

		CurrentFrameBufferCopyTexture[textureIndex] = texture;
	}

	public ITexture? GetFrameBufferCopyTexture(int textureIndex) {
		if (textureIndex < 0 || textureIndex >= MAX_FB_TEXTURES) {
			Assert(false);
			return null; // FIXME!  This should return the error texture.
		}
		return CurrentFrameBufferCopyTexture[textureIndex];
	}

	ITexture? LocalCubemapTexture;

	public void BindLocalCubemap(ITexture? texture) {
		ITexture? previousTexture = LocalCubemapTexture;

		LocalCubemapTexture = texture ?? materials.TextureSystem.ErrorTexture();

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

	public void AddRefRenderData() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void BeginMorphAccumulation() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void BeginOcclusionQueryDrawing(OcclusionQueryObjectHandle_t handle) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

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
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void CullMode(MaterialCullMode cullMode) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void DestroyMorph(IMorph morph) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void DestroyOcclusionQueryObject(OcclusionQueryObjectHandle_t handle) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

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

	public void EndOcclusionQueryDrawing(OcclusionQueryObjectHandle_t handle) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void EndPIXEvent() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void FogColor3f(float r, float g, float b) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void FogColor3fv(ReadOnlySpan<float> rgb) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void FogColor3ub(byte r, byte g, byte b) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void FogColor3ubv(ReadOnlySpan<byte> rgb) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void FogEnd(float end) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void FogMaxDensity(float maxDensity) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void FogMode(MaterialFogMode fogMode) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void FogStart(float start) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void GMOD_FlushQueue() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public ICallQueue? GetCallQueue() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void GetFogColor(out Color rgb) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void GetFogDistances(out float start, out float end, out float fogZ) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public MaterialFogMode GetFogMode() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void GetMatrix(MaterialMatrixMode matrixMode, out Matrix3x4 matrix) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void GetMaxToRender(IMesh? pMesh, bool bMaxUntilFlush, Span<int> maxVerts, Span<int> maxIndices) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public bool GetMorphAccumulatorTexCoord(out Vector2 texCoord, IMorph morph, int vertex) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public int GetNumActiveDeformations() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public Vector3 GetToneMappingScaleLinear() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void GetWorldSpaceCameraVectors(out Vector3 forward, out Vector3 right, out Vector3 up) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public bool IsRenderData(object? data) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public float Knob(Span<char> knobname, Span<float> setvalue = default) {
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

	public object? LockRenderData(int sizeInBytes) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public E? LockRenderDataTyped<E>(int count, E? srcData = null) where E : class {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void MultMatrix(in Matrix3x4 matrix) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void MultMatrix(in Matrix4x4 matrix) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void MultMatrixLocal(in Matrix3x4 matrix) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public int OcclusionQuery_GetNumPixelsRendered(OcclusionQueryObjectHandle_t handle) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void OverrideAlphaWriteEnable(bool enable, bool alphaWriteEnable) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void OverrideBlend(bool unk1, bool unk2, int unk3, int unk4, int unk5) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void OverrideBlendSeparateAlpha(bool unk1, bool unk2, int unk3, int unk4, int unk5) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void OverrideColorWriteEnable(bool overrideEnable, bool colorWriteEnable) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void OverrideDepthEnable(bool enable, bool depthEnable) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PerformFullScreenStencilOperation() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PerspectiveOffCenterX(double fovx, double aspect, double zNear, double zFar, double bottom, double top, double left, double right) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PerspectiveX(double fovx, double aspect, double zNear, double zFar) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PickMatrix(int x, int y, int width, int height) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PopCustomClipPlane() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PopDeformation() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PopSelectionName() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PushCustomClipPlane(ReadOnlySpan<float> plane) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PushDeformation(ref readonly DeformationBase deformation) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PushRenderTargetAndViewport() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void PushSelectionName(int name) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ReadPixels(int x, int y, int width, int height, Span<byte> data, ImageFormat dstFormat) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ReadPixelsAndStretch(ref System.Drawing.Rectangle srcRect, ref System.Drawing.Rectangle pDstRect, Span<byte> buffer, ImageFormat dstFormat, int dstStride) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void RefreshFrontBufferNonInteractive() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ReleaseRenderData() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public bool RemoveLookup(ColorCorrectionHandle_t handle) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ResetLookupWeights() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ResetOcclusionQueryObject(OcclusionQueryObjectHandle_t handle) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void ResetToneMappingScale(float monoscale) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void Rotate(float angle, float x, float y, float z) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SelectionBuffer(Span<uint> buffer) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public int SelectionMode(bool selectionMode) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetFlexWeights(int firstWeight, ReadOnlySpan<MorphWeight> weights) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetFloatRenderingParameter(int parm_number, float value) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetFogZ(float fogZ) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetFullScreenDepthTextureValidityFlag(bool isValid) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetGoalToneMappingScale(float monoscale) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetHeightClipZ(float z) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetIntRenderingParameter(int parm_number, int value) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetLookupWeight(ColorCorrectionHandle_t handle, float weight) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetNonInteractivePacifierTexture(ITexture? texture, float normalizedX, float normalizedY, float normalizedSize) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetPIXMarker(Color color, ReadOnlySpan<char> name) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetRenderTarget(ITexture? texture) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetRenderTargetEx(int renderTargetID, ITexture? texture) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetResetable(ColorCorrectionHandle_t handle, bool resetable) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetShadowDepthBiasFactors(float slopeScaleDepthBias, float depthBias) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SetVectorRenderingParameter(int parm_number, in Vector3 value) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void SyncToken(ReadOnlySpan<char> token) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void UnlockLookup(ColorCorrectionHandle_t handle) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void UnlockRenderData(object? data) {
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

	public MatCallQueue GetCallQueueInternal() {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public float GetFloatRenderingParameter(int parmNumber) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public int GetIntRenderingParameter(int parmNumber) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public void GetLightmapDimensions(out int w, out int h) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public int GetLightmapPage() {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public ITexture? GetRenderTargetEx(int renderTargetId) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public Vector3 GetVectorRenderingParameter(int parmNumber) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public unsafe bool OnDrawMesh(IMesh mesh, PrimList* pLists, int nLists) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public bool OnFlushBufferedPrimitives() {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public bool OnSetColorMesh(IMesh staticMesh, IMesh mesh, int nVertexOffsetInBytes) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public bool OnSetFlexMesh(IMesh staticMesh, IMesh mesh, int nVertexOffsetInBytes) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public int AddRef() {
		throw new NotImplementedException("Incomplete port of IRefCounted");
	}

	public int Release() {
		throw new NotImplementedException("Incomplete port of IRefCounted");
	}
}
