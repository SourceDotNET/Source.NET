using Source.Common;
using Source.Common.Commands;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.Common.Utilities;

using System.Numerics;

namespace Source.MaterialSystem;

public abstract class MatRenderContextBase
{
	protected readonly MaterialSystem materials;

	protected MatRenderContextBase(MaterialSystem materials) {
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

		DirtyViewState = true;
		DirtyViewProjState = true;
	}
	protected RefStack<RenderTargetStackElement> RenderTargetStack;
	protected RefStack<MatrixStackItem>[] MatrixStacks;
	protected bool[] MatrixStacksDirtyStates;
	protected MaterialMatrixMode matrixMode;
	protected ShaderViewport ActiveViewport = new(0, 0, 0, 0, 0, 1);
	public ref MatrixStackItem CurMatrixItem => ref MatrixStacks[(int)matrixMode].Top();
	public ref RenderTargetStackElement CurRenderTargetStack => ref RenderTargetStack.Top();

	protected IMaterialInternal? currentMaterial;
	protected object? CurrentProxyData;
	protected int LightmapPageID;
	protected ITexture? LocalCubemapTexture;

	public const int MAX_FB_TEXTURES = 4;
	protected readonly ITexture?[] CurrentFrameBufferCopyTexture = new ITexture?[MAX_FB_TEXTURES];

	protected bool FlashlightEnable;
	protected bool DirtyViewState;
	protected bool DirtyViewProjState;
	protected bool EnableClippingValue = true;
	protected MaterialHeightClipMode HeightClipMode;
	protected float HeightClipZ;

	protected Vector3 LastSetToneMapScale = new(1, 1, 1);
	protected TimeUnit_t FrameTime;
	protected float CurToneMapScale = 1.0f;
	protected float GoalToneMapScale = 1.0f;

	protected Vector3 VecViewOrigin;
	protected Vector3 VecViewForward;
	protected Vector3 VecViewRight;
	protected Vector3 VecViewUp;
	protected Matrix4x4 ViewProjMatrix;

	public virtual void InitializeFrom(MatRenderContextBase initialState) {
		currentMaterial = initialState.currentMaterial;
		CurrentProxyData = initialState.CurrentProxyData;
		LightmapPageID = initialState.LightmapPageID;
		LocalCubemapTexture = initialState.LocalCubemapTexture;

		initialState.CurrentFrameBufferCopyTexture.CopyTo(CurrentFrameBufferCopyTexture, 0);

		EnableClippingValue = initialState.EnableClippingValue;

		HeightClipMode = initialState.HeightClipMode;
		HeightClipZ = initialState.HeightClipZ;

		while (RenderTargetStack.Count > 0)
			RenderTargetStack.Pop();
		for (int i = 0; i < initialState.RenderTargetStack.Count; i++)
			RenderTargetStack.Push(initialState.RenderTargetStack[i]);

		matrixMode = initialState.matrixMode;
		for (int i = 0; i < MatrixStacks.Length; i++) {
			RefStack<MatrixStackItem> stack = MatrixStacks[i];
			RefStack<MatrixStackItem> initialStack = initialState.MatrixStacks[i];
			while (stack.Count > 0)
				stack.Pop();
			for (int j = 0; j < initialStack.Count; j++)
				stack.Push(initialStack[j]);
			MatrixStacksDirtyStates[i] = initialState.MatrixStacksDirtyStates[i];
		}

		FlashlightEnable = initialState.FlashlightEnable;
		FrameTime = initialState.FrameTime;
		GoalToneMapScale = initialState.GoalToneMapScale;
		CurToneMapScale = initialState.CurToneMapScale;
		LastSetToneMapScale = initialState.LastSetToneMapScale;
	}

	public virtual void SetFrameTime(double frameTime) {
		FrameTime = frameTime;
	}

	public ITexture? GetRenderTarget() {
		if (RenderTargetStack.Count > 0)
			return RenderTargetStack.Top().RenderTarget0;
		else
			return null;
	}

	public ITexture? GetRenderTargetEx(int renderTargetId) {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public IMaterialInternal? GetCurrentMaterialInternal() {
		return currentMaterial;
	}
	public virtual void SetCurrentMaterialInternal(IMaterialInternal? mat) {
		currentMaterial = mat;
	}

	public IMaterial? GetCurrentMaterial() {
		return currentMaterial;
	}

	public virtual object? GetCurrentProxy() => CurrentProxyData;
	public virtual void SetCurrentProxy(object? proxyData) => CurrentProxyData = proxyData;

	public virtual void Bind(IMaterial iMaterial, object? proxyData) {
		IMaterialInternal material = (IMaterialInternal)iMaterial;
		if (material == null) {
			Warning("Programming error: MatRenderContextBase.Bind: NULL material\n");
			material = materials.errorMaterial;
		}

		material = material.GetRealTimeVersion();

		if (currentMaterial != material) {
			if (!material.IsPrecached())
				material.Precache();
			currentMaterial = material;
		}

		CurrentProxyData = proxyData;
	}

	public virtual void BindLightmapPage(int lightmapPageID) {
		LightmapPageID = lightmapPageID;
	}

	public virtual void BindLocalCubemap(ITexture? texture) {
		LocalCubemapTexture = texture ?? materials.TextureSystem.ErrorTexture();
	}

	public virtual void MatrixMode(MaterialMatrixMode mode) {
		matrixMode = mode;
	}

	public virtual void PushMatrix() {
		RefStack<MatrixStackItem> curStack = MatrixStacks[(int)matrixMode];
		curStack.Push(CurMatrixItem);
		CurrentMatrixChanged();
	}

	public virtual void PopMatrix() {
		RefStack<MatrixStackItem> curStack = MatrixStacks[(int)matrixMode];
		curStack.Pop();
		CurrentMatrixChanged();
	}

	public virtual void LoadMatrix(in Matrix4x4 matrix) {
		ref MatrixStackItem item = ref CurMatrixItem;
		item.Matrix = matrix;
		CurrentMatrixChanged();
	}

	public virtual void LoadMatrix(Matrix3x4 matrix) {
		ref MatrixStackItem item = ref CurMatrixItem;
		item.Matrix = matrix;
		CurrentMatrixChanged();
	}

	public virtual void MultMatrix(in Matrix3x4 matrix) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual void MultMatrix(in Matrix4x4 matrix) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual void MultMatrixLocal(in Matrix4x4 mat) {
		MathLib.MatrixMultiply(in CurMatrixItem.Matrix, in mat, out Matrix4x4 result);
		CurMatrixItem.Matrix = result;
		CurrentMatrixChanged();
	}

	public virtual void MultMatrixLocal(in Matrix3x4 matrix) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void GetMatrix(MaterialMatrixMode mode, out Matrix4x4 viewMatrix) {
		var stack = MatrixStacks[(int)mode];
		if (stack.Count == 0) {
			viewMatrix = Matrix4x4.Identity;
			return;
		}

		viewMatrix = stack.Top().Matrix;
	}

	public void GetMatrix(MaterialMatrixMode matrixMode, out Matrix3x4 matrix) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual void LoadIdentity() {
		ref MatrixStackItem item = ref CurMatrixItem;
		item.Matrix = Matrix4x4.Identity;
		CurrentMatrixChanged();
	}

	public virtual void Ortho(double left, double top, double right, double bottom, double near, double far) {
		ref Matrix4x4 item = ref CurMatrixItem.Matrix;
		MathLib.MatrixOrtho(ref item, (float)left, (float)right, (float)bottom, (float)top, (float)near, (float)far);
		CurrentMatrixChanged();
	}

	public virtual void PerspectiveX(double fovx, double aspect, double zNear, double zFar) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual void PerspectiveOffCenterX(double fovx, double aspect, double zNear, double zFar, double bottom, double top, double left, double right) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual void PickMatrix(int x, int y, int width, int height) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual void Rotate(float angle, float x, float y, float z) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual void Translate(float x, float y, float z) {
		MathLib.MatrixBuildTranslation(out Matrix4x4 mat, x, y, z);
		MultMatrixLocal(in mat);
	}

	public virtual void Scale(float x, float y, float z) {
		MathLib.MatrixBuildScale(out Matrix4x4 mat, x, y, z);
		MultMatrixLocal(in mat);
	}

	public virtual void SetFrameBufferCopyTexture(ITexture? texture, int textureIndex = 0) {
		if (textureIndex < 0 || textureIndex >= MAX_FB_TEXTURES) {
			Assert(false);
			return;
		}

		CurrentFrameBufferCopyTexture[textureIndex] = texture;
	}

	public ITexture? GetFrameBufferCopyTexture(int textureIndex) {
		if (textureIndex < 0 || textureIndex >= MAX_FB_TEXTURES) {
			Assert(false);
			return null; // FIXME!  This should return the error texture.
		}
		return CurrentFrameBufferCopyTexture[textureIndex];
	}

	public MaterialHeightClipMode GetHeightClipMode() => HeightClipMode;

	public virtual void SetHeightClipMode(MaterialHeightClipMode heightClipMode) {
		if (HeightClipMode != heightClipMode) {
			HeightClipMode = heightClipMode;
			UpdateHeightClipUserClipPlane();
		}
	}

	public virtual void SetHeightClipZ(float z) {
		if (z != HeightClipZ) {
			HeightClipZ = z;
			UpdateHeightClipUserClipPlane();
		}
	}

	public virtual bool EnableClipping(bool enable) {
		if (enable != EnableClippingValue) {
			EnableClippingValue = enable;
			ApplyCustomClipPlanes();

			return !enable;
		}
		return enable;
	}

	public void SetRenderTarget(ITexture? texture) => SetRenderTargetEx(0, texture);

	public virtual void SetRenderTargetEx(int renderTargetID, ITexture? newTarget) {
		Assert(RenderTargetStack.Count > 0);

		RenderTargetStackElement newTOS = RenderTargetStack.Top();
		newTOS[renderTargetID] = newTarget;
		RenderTargetStack.Pop();
		RenderTargetStack.Push(newTOS);
	}

	public virtual void Viewport(int x, int y, int width, int height) {
		Dbg.Assert(RenderTargetStack.Count > 0);

		RenderTargetStackElement newTOS = new();
		newTOS = CurRenderTargetStack; // copy
		newTOS.ViewX = x;
		newTOS.ViewY = y;
		newTOS.ViewW = width;
		newTOS.ViewH = height;
		RenderTargetStack.Pop();
		RenderTargetStack.Push(newTOS);
	}

	public virtual void PushRenderTargetAndViewport() {
		RenderTargetStackElement top = RenderTargetStack.Top();
		RenderTargetStack.Push(top);
		CommitRenderTargetAndViewport();
	}

	public virtual void PushRenderTargetAndViewport(ITexture? thisTexture) {
		RenderTargetStackElement element = new(thisTexture, 0, 0, -1, -1);
		RenderTargetStack.Push(element);
		CommitRenderTargetAndViewport();
	}

	public virtual void PushRenderTargetAndViewport(ITexture? thisTexture, int x, int y, int w, int h) {
		RenderTargetStackElement element = new(thisTexture, x, y, w, h);
		RenderTargetStack.Push(element);
		CommitRenderTargetAndViewport();
	}

	public virtual void PushRenderTargetAndViewport(ITexture? colorTexture, ITexture? depthTexture, int x, int y, int w, int h) {
		RenderTargetStackElement element = new(colorTexture, depthTexture, x, y, w, h);
		RenderTargetStack.Push(element);
		CommitRenderTargetAndViewport();
	}

	public virtual void PopRenderTargetAndViewport() {
		if (RenderTargetStack.Count == 0) {
			AssertMsg(false, "MatRenderContext.PopRenderTargetAndViewport: Stack is empty!\n");
			return;
		}

		Flush();

		RenderTargetStack.Pop();
		CommitRenderTargetAndViewport();
	}

	public virtual void SyncMatrices() { }
	public virtual void SyncMatrix(MaterialMatrixMode mode) { }

	protected virtual void UpdateHeightClipUserClipPlane() { }
	protected virtual void ApplyCustomClipPlanes() { }

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

	public float ComputePixelWidthOfSphere(in Vector3 origin, float radius) {
		return ComputePixelDiameterOfSphere(origin, radius) * 2.0f;
	}

	public virtual void GetWorldSpaceCameraPosition(out Vector3 vecCameraPos) {
		vecCameraPos = VecViewOrigin;
	}

	public virtual void GetWorldSpaceCameraVectors(out Vector3 forward, out Vector3 right, out Vector3 up) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual Vector3 GetToneMappingScaleLinear() {
		if (materials.HardwareConfig.GetHDRType() == HDRType.None)
			return new(1, 1, 1);
		else
			return LastSetToneMapScale;
	}

	static readonly ConVar mat_accelerate_adjust_exposure_down = new("mat_accelerate_adjust_exposure_down", "3.0", FCvar.Cheat);
	static readonly ConVar mat_hdr_manual_tonemap_rate = new("mat_hdr_manual_tonemap_rate", "1.0");
	static readonly ConVar mat_tonemap_algorithm = new("mat_tonemap_algorithm", "1", FCvar.Cheat, "0 = Original Algorithm 1 = New Algorithm");

	public abstract void SetToneMappingScaleLinear(in Vector3 scale);

	public void TurnOnToneMapping() {
		if ((materials.HardwareConfig.GetHDRType() != HDRType.None) && (FrameTime > 0.0)) {
			TimeUnit_t elapsedTime = FrameTime;
			float goalScale = GoalToneMapScale;
			float rate = mat_hdr_manual_tonemap_rate.GetFloat();

			if (mat_tonemap_algorithm.GetInt() == 1)
				rate *= 2.0f;

			if (rate == 0.0f)
				CurToneMapScale = goalScale;
			else {
				if (goalScale < CurToneMapScale) {
					float accExposureAdjust = mat_accelerate_adjust_exposure_down.GetFloat();
					rate = Math.Min(accExposureAdjust * rate, MathLib.Lerp(rate, accExposureAdjust * rate, 0.0f, 1.5f, CurToneMapScale - goalScale));
				}

				float rateTimesTime = (float)(rate * elapsedTime);
				if (mat_tonemap_algorithm.GetInt() == 1)
					rateTimesTime = Math.Min(rateTimesTime, (1.0f / 16.0f) * 0.25f);

				float alpha = Math.Max(0.0f, Math.Min(1.0f, rateTimesTime));
				CurToneMapScale = (goalScale * alpha) + (CurToneMapScale * (1.0f - alpha));

				if (!float.IsFinite(CurToneMapScale)) {
					Assert(false);
					CurToneMapScale = goalScale;
				}
			}

			SetToneMappingScaleLinear(new(CurToneMapScale, CurToneMapScale, CurToneMapScale));
			LastSetToneMapScale = new(CurToneMapScale, CurToneMapScale, CurToneMapScale);
		}
	}

	public void ResetToneMappingScale(float monoscale) {
		CurToneMapScale = monoscale;
		SetToneMappingScaleLinear(new(CurToneMapScale, CurToneMapScale, CurToneMapScale));
		LastSetToneMapScale = new(CurToneMapScale, CurToneMapScale, CurToneMapScale);
		GoalToneMapScale = 1;
	}

	public void SetGoalToneMappingScale(float monoscale) {
		Assert(float.IsFinite(monoscale));
		if (float.IsFinite(monoscale))
			GoalToneMapScale = monoscale;
	}

	public virtual int GetLightmapPage() {
		throw new NotImplementedException("Incomplete port of IMatRenderContextInternal");
	}

	public virtual object? LockRenderData(int sizeInBytes) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual void UnlockRenderData(object? data) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual void AddRefRenderData() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual void ReleaseRenderData() {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public virtual bool IsRenderData(object? data) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	public void MarkRenderDataUnused(bool v) {

	}

	public virtual float Knob(Span<char> knobname, Span<float> setvalue = default) {
		throw new NotImplementedException("Incomplete port of IMatRenderContext");
	}

	protected virtual void CommitRenderTargetAndViewport() { }

	protected void RecomputeViewState() {
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

	protected void RecomputeViewProjState() {
		if (DirtyViewProjState) {
			Matrix4x4 viewMatrix, projMatrix;

			// FIXME: Should consider caching this upon change for projection or view matrix.
			GetMatrix(MaterialMatrixMode.View, out viewMatrix);
			GetMatrix(MaterialMatrixMode.Projection, out projMatrix);
			ViewProjMatrix = projMatrix * viewMatrix;
			DirtyViewProjState = false;
		}
	}

	protected void MarkDirty() => MatrixStacksDirtyStates[(int)matrixMode] = true;

	protected void CurrentMatrixChanged() {
		MarkDirty();

		if (matrixMode == MaterialMatrixMode.View) {
			DirtyViewState = true;
			DirtyViewProjState = true;
		}
		else if (matrixMode == MaterialMatrixMode.Projection)
			DirtyViewProjState = true;
	}

	public abstract void GetViewport(out int x, out int y, out int width, out int height);
	public abstract void Flush(bool flushHardware = false);
}
