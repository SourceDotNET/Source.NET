global using static Source.Engine.RenderAccessors;

using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Utilities;

using System.Numerics;
using System.Runtime.CompilerServices;

using static Source.Engine.MatSysInterface;
namespace Source.Engine;

public struct ViewStack
{
	public ViewSetup View;
	public Matrix4x4 MatrixView;
	public Matrix4x4 MatrixProjection;
	public Matrix4x4 MatrixWorldToScreen;
	public bool Is2DView;
	public bool NoDraw;
}


public static class RenderAccessors
{

	public static ref readonly Vector3 CurrentViewOrigin() => ref R.CurrentViewOrigin;
	public static ref readonly Vector3 CurrentViewForward() => ref R.CurrentViewForward;
	public static ref readonly Vector3 CurrentViewRight() => ref R.CurrentViewRight;
	public static ref readonly Vector3 CurrentViewUp() => ref R.CurrentViewUp;
	public static ref readonly Vector3 MainViewOrigin() => ref R.MainViewOrigin;
	public static ref readonly Vector3 MainViewForward() => ref R.MainViewForward;
	public static ref readonly Vector3 MainViewRight() => ref R.MainViewRight;
	public static ref readonly Vector3 MainViewUp() => ref R.MainViewUp;
}

public partial class Render(
	CommonHostState host_state,
	// IMaterialSystem materials,
	Host Host,
	MatSysInterface MaterialSystem,
	ClientGlobalVariables gpGlobals
	) : IRender
{
	int FrameCount = 1;
	RefStack<ViewStack> ViewStack = new();
	Matrix4x4 MatrixView;
	Matrix4x4 MatrixProjection;
	Matrix4x4 MatrixWorldToScreen;

	ModelLoader? _modelLoader;
	ModelLoader modelLoader => _modelLoader ??= (ModelLoader)Singleton<IModelLoader>();

	IStudioRender? _studioRender;
	IStudioRender studioRender => _studioRender ??= Singleton<IStudioRender>();

	IEngineTrace? _engineTrace;
	IEngineTrace engineTrace => _engineTrace ??= KeyedSingleton<IEngineTrace>(Realm.Client);

	int LightmapUpdateDepth;
	float yFOV;
	float Framerate;
	float ZNear;
	float ZFar;
	IMaterial? SkyboxOcclude;

	public Vector3 CurrentViewOrigin = new(0, 0, 0);
	public Vector3 CurrentViewForward = new(1, 0, 0);
	public Vector3 CurrentViewRight = new(0, -1, 0);
	public Vector3 CurrentViewUp = new(0, 0, 1);

	public Vector3 MainViewOrigin = new(0, 0, 0);
	public Vector3 MainViewForward = new(1, 0, 0);
	public Vector3 MainViewRight = new(0, -1, 0);
	public Vector3 MainViewUp = new(0, 0, 1);

	bool CanAccessCurrentView;

	public void FrameBegin() {
		if (host_state.WorldModel != null) {
			r_framecount++;
			GLRLight.AnimateLight();
			GLRLight.PushDlights();
		}

		ModelRender.UpdateStudioRenderConfig();
		studioRender.BeginFrame();

		FrameCount++;
	}

	public void FrameEnd() {

	}

	public ref readonly Vector3 ViewOrigin() => ref CurrentView().Origin;
	public ref readonly QAngle ViewAngles() => ref CurrentView().Angles;
	public ref readonly ViewSetup ViewGetCurrent() => ref CurrentView();
	public float GetFramerate() => Framerate;
	public virtual float GetZNear() => ZNear;
	public virtual float GetZFar() => ZFar;
	public float GetFov() => CurrentView().FOV;
	public float GetFovY() => yFOV;
	public float GetFovViewmodel() => CurrentView().FOVViewmodel;
	public ref readonly Matrix4x4 ViewMatrix() {
		if (ViewStack.Count > 1)
			return ref ViewStack.Top().MatrixView;
		return ref MatrixView;
	}
	public ref readonly Matrix4x4 WorldToScreenMatrix() {
		if (ViewStack.Count > 1)
			return ref ViewStack.Top().MatrixWorldToScreen;
		return ref MatrixWorldToScreen;
	}

	public void PopView(Frustum frustumPlanes) {
		if (!ViewStack.Top().NoDraw) {
			using MatRenderContextPtr renderContext = new(materials);

			renderContext.MatrixMode(MaterialMatrixMode.Projection);
			renderContext.PopMatrix();

			renderContext.MatrixMode(MaterialMatrixMode.View);
			renderContext.PopMatrix();

			renderContext.MatrixMode(MaterialMatrixMode.Model);
			renderContext.PopMatrix();

			renderContext.PopRenderTargetAndViewport();
		}

		bool reset = (ViewStack.Count > 1) ? true : false;
		ViewStack.Pop();

		if (reset) {
			if (!ViewStack.Top().Is2DView) {
				ExtractMatrices();
				OnViewActive(frustumPlanes);
			}
		}
	}

	private void ExtractMatrices() {
		MatrixView = ViewStack.Top().MatrixView;
		MatrixProjection = ViewStack.Top().MatrixProjection;
		MatrixWorldToScreen = ViewStack.Top().MatrixWorldToScreen;
	}

	public void SetMainView(in Vector3 origin, in QAngle angles) {
		MainViewOrigin = origin;
		angles.Vectors(out MainViewForward, out MainViewRight, out MainViewUp);
	}

	public bool ClipTransformWithProjection(in Matrix4x4 worldToScreen, in Vector3 point, out Vector3 clip) {
		// UNDONE: Clean this up some, handle off-screen vertices
		float w;

		clip.X = worldToScreen[0][0] * point[0] + worldToScreen[0][1] * point[1] + worldToScreen[0][2] * point[2] + worldToScreen[0][3];
		clip.Y = worldToScreen[1][0] * point[0] + worldToScreen[1][1] * point[1] + worldToScreen[1][2] * point[2] + worldToScreen[1][3];
		//	z		 = worldToScreen[2][0] * point[0] + worldToScreen[2][1] * point[1] + worldToScreen[2][2] * point[2] + worldToScreen[2][3];
		w = worldToScreen[3][0] * point[0] + worldToScreen[3][1] * point[1] + worldToScreen[3][2] * point[2] + worldToScreen[3][3];

		// Just so we have something valid here
		clip.Z = 0.0f;

		bool behind;
		if (w < 0.001f) {
			behind = true;
			clip.X *= 100000;
			clip.Y *= 100000;
		}
		else {
			behind = false;
			float invw = 1.0f / w;
			clip.X *= invw;
			clip.Y *= invw;
		}

		return behind;
	}

	public bool ClipTransform(in Vector3 point, out Vector3 clip) {
		ref readonly Matrix4x4 worldToScreen = ref g_EngineRenderer.WorldToScreenMatrix();
		return ClipTransformWithProjection(worldToScreen, point, out clip);
	}

	public bool ScreenTransform(in Vector3 point, out Vector3 screen) {
		bool retval = ClipTransform(point, out screen);

		screen.X = 0.5f * (screen.X + 1.0f) * CurrentView().Width + CurrentView().X;
		screen.Y = 0.5f * (screen.Y + 1.0f) * CurrentView().Height + CurrentView().Y;

		return retval;
	}

	private ref ViewSetup CurrentView() => ref ViewStack.Top().View;

	private void OnViewActive(Frustum frustumPlanes) {
		ref ViewSetup view = ref CurrentView();

		yFOV = MathLib.CalcFovY(view.FOV, view.AspectRatio);

		CurrentViewOrigin = view.Origin;
		view.Angles.Vectors(out CurrentViewForward, out CurrentViewRight, out CurrentViewUp);
		CanAccessCurrentView = true;

		if (view.Ortho) {
			OrthoExtractFrustumPlanes(ref frustumPlanes);
		}
		else {
			ExtractFrustumPlanes(ref frustumPlanes);
		}

		// OcclusionSystem.SetView(view.Origin, view.FOV, MatrixView, MatrixProjection, frustumPlanes[FrustumPlane.NearZ]);

		if (!ViewStack.Top().NoDraw) {
			R_SceneBegin();
		}

		if (MathLib.VectorCompare(MainViewOrigin(), view.Origin))
			DebugLeafVis.LeafVisBuild(view.Origin);
	}

	private void ExtractFrustumPlanes(ref Frustum frustumPlanes) {
		ref ViewSetup view = ref CurrentView();

		MathLib.GeneratePerspectiveFrustum(CurrentViewOrigin, CurrentViewForward, CurrentViewRight, CurrentViewUp,
			view.ZNear, view.ZFar, view.FOV, yFOV, g_Frustum);

		for (int i = 0; i < (int)FrustumPlane.NumPlanes; i++)
			frustumPlanes.SetPlane(i, g_Frustum.GetPlane(i).Normal, g_Frustum.GetPlane(i).Dist);
	}

	private void OrthoExtractFrustumPlanes(ref Frustum frustumPlanes) => throw new NotImplementedException();

	public void Push2DView(in ViewSetup view, ClearFlags flags, ITexture? renderTarget, Frustum frustumPlanes) {
		ref ViewStack viewStack = ref ViewStack.Push();
		viewStack.View = view;
		viewStack.Is2DView = true;
		viewStack.NoDraw = (flags & ClearFlags.NoDraw) != 0;
		viewStack.MatrixView = MatrixView;
		viewStack.MatrixProjection = MatrixProjection;
		viewStack.MatrixWorldToScreen = MatrixWorldToScreen;

		ref ViewSetup topView = ref viewStack.View;
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.PushRenderTargetAndViewport(renderTarget, topView.X, topView.Y, topView.Width, topView.Height);
		ClearView(topView, flags, renderTarget);

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
		renderContext.Scale(1, -1, 1);
		renderContext.Ortho(0, 0, topView.Width, topView.Height, -99999, 99999);

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();
	}

	private void ClearView(ViewSetup topView, ClearFlags flags, ITexture? renderTarget, ITexture? depthTexture = null) {
		bool clearColor = (flags & ClearFlags.ClearColor) != 0;
		bool clearDepth = (flags & ClearFlags.ClearDepth) != 0;
		bool clearStencil = (flags & ClearFlags.ClearStencil) != 0;
		bool forceClearWholeRenderTarget = (flags & ClearFlags.ClearFullTarget) != 0;
		bool obeyStencil = (flags & ClearFlags.ClearObeyStencil) != 0;

		if (!clearColor && !clearDepth && !clearStencil)
			return;

		using MatRenderContextPtr renderContext = new(materials);

		if (!forceClearWholeRenderTarget) {
			// if (obeyStencil)
			//	renderContext.ClearBuffersObeyStencil(clearColor, clearDepth);
			// else
			renderContext.ClearBuffers(clearColor, clearDepth, clearStencil);
		}
		else {
			// Get the render target dimensions
			int width, height;
			if (renderTarget != null) {
				width = renderTarget.GetActualWidth();
				height = renderTarget.GetActualHeight();
			}
			else {
				materials.GetBackBufferDimensions(out width, out height);
			}

			renderContext.PushRenderTargetAndViewport(renderTarget, depthTexture, 0, 0, width, height);

			// if (obeyStencil)
			// 	renderContext->ClearBuffersObeyStencil(clearColor, clearDepth);
			// else
			renderContext.ClearBuffers(clearColor, clearDepth, clearStencil);

			renderContext.PopRenderTargetAndViewport();
		}
	}

	public void LevelInit() {
		ConDMsg("Initializing renderer...\n");

		FrameCount = 1;
		ResetLightStyles();
#if !SWDS
		DecalInit();
#endif
		LoadSkys();
		InitStudio();

		// FIXME: Is this the best place to initialize the kd tree when we're client-only?
		if (!sv.IsActive()) {
			g_ShadowMgr.LevelShutdown();
			StaticPropMgr().LevelShutdown();
			SpatialPartition().Init(host_state.WorldModel!.Mins, host_state.WorldModel!.Maxs);
			StaticPropMgr().LevelInit();
			g_ShadowMgr.LevelInit(host_state.WorldBrush!.NumSurfaces);
		}

		UpdateMaterialSystemConfig();

		LoadWorldGeometry();

		Surface_LevelInit();
		Areaportal_LevelInit();
	}

	private void ResetLightStyles() {
		for (int i = 0; i < 256; i++) {
			MatSysInterface.LightStyleValue[i] = 264;
			MatSysInterface.LightStyleFrame[i] = FrameCount;
		}
	}
	private void LoadSkys() {
		bool success = true;
		Span<char> requestedsky = stackalloc char[128];
		ConVarRef skyname = this.skyname.Value;
		if (skyname.IsValid()) {
			ReadOnlySpan<char> skynameValue = skyname.GetString();
			skynameValue.CopyTo(requestedsky);
			requestedsky = requestedsky[..skynameValue.Length];
		}
		else {
			ConDMsg("Unable to find skyname ConVar!!!\n");
			return;
		}

		if (!LoadNamedSkys(requestedsky)) {
			success = false;

			if (((ReadOnlySpan<char>)requestedsky).Equals("sky_urb01", StringComparison.OrdinalIgnoreCase)) {
				skyname.SetValue("sky_urb01");
				if (LoadNamedSkys(skyname.GetString())) {
					ConDMsg($"Unable to load sky {requestedsky}, but successfully loaded {skyname.GetString()}\n");
					success = true;
				}
			}
		}

		if (!success)
			ConDMsg($"Unable to load sky {requestedsky}\n");
	}
	private void InitStudio() {
#if !SWDS
		R_StudioInitLightingCache();
#endif
	}
	private void LoadWorldGeometry() {
		if (host_state.WorldModel == null)
			return;

		MaterialSystem.DestroySortInfo();
		MaterialSystem.RegisterLightmapSurfaces();
		MaterialSystem.CreateSortInfo();

		modelLoader.Map_LoadDisplacements(MaterialSystem.MaterialSortInfoArray!, host_state.WorldModel!);
		RebuildLightmaps();
	}
	private void Surface_LevelInit() { }
	private void Areaportal_LevelInit() => R_Areaportal_LevelInit();


	public void Init() {
		KeyValues kvs = new("Occlude");
		SkyboxOcclude = materials.CreateMaterial("__skybox_occlude", kvs);
	}

	public void DrawSceneBegin() {

	}

	public void DrawSceneEnd() {
		R_SceneEnd();
		DebugLeafVis.LeafVisDraw();
	}

	public void ViewSetupVis(bool novis, ReadOnlySpan<Vector3> origins) {
		ViewSetupVisEx(novis, origins, out _);
	}

	public void ViewSetupVisEx(bool novis, ReadOnlySpan<Vector3> origins, out uint returnFlags) {
		ModVis.Map_VisSetup(host_state.WorldModel, origins, novis, out returnFlags);
	}

	static ConVar r_drawskybox = new("1", FCvar.Cheat);

	static readonly int[] SkyTexOrder = [0, 2, 1, 3, 4, 5];
	static readonly int[] FakePlaneType = [1, -1, 2, -2, 3, -3];
	ConVar fov_desired { get => field ??= cvar.FindVar("fov_desired")!; }
	public void DrawSkybox(float zFar, int drawFlags = 0x3F) {
		if (!r_drawskybox.GetBool())
			return;

		MatRenderContextPtr renderContext = new(materials);

		Vector3 normal;
		Span<Vector3> positionArray = stackalloc Vector3[4];
		Span<Vector2> texCoordArray = stackalloc Vector2[4];

		for (int i = 0; i < 6; i++, drawFlags >>= 1) {
			// Don't draw this panel of the skybox if the flag isn't set:
			if ((drawFlags & 1) == 0)
				continue;

			normal = vec3_origin;
			switch (FakePlaneType[i]) {
				case 1:
					normal[0] = 1;
					break;

				case -1:
					normal[0] = -1;
					break;

				case 2:
					normal[1] = 1;
					break;

				case -2:
					normal[1] = -1;
					break;

				case 3:
					normal[2] = 1;
					break;

				case -3:
					normal[2] = -1;
					break;
			}

			if (skyboxMaterials[SkyTexOrder[i]] != null) {
				renderContext.Bind(skyboxMaterials[SkyTexOrder[i]]!);

				MakeSkyVec(-1.0f, -1.0f, i, zFar, out positionArray[0], out texCoordArray[0]);
				MakeSkyVec(-1.0f, 1.0f, i, zFar, out positionArray[1], out texCoordArray[1]);
				MakeSkyVec(1.0f, 1.0f, i, zFar, out positionArray[2], out texCoordArray[2]);
				MakeSkyVec(1.0f, -1.0f, i, zFar, out positionArray[3], out texCoordArray[3]);

				IMesh mesh = renderContext.GetDynamicMesh();

				MeshBuilder meshBuilder = new();
				meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, 4, 6);

				for (int j = 0; j < 4; ++j) {
					meshBuilder.Position3fv(positionArray[j]);
					meshBuilder.TexCoord2fv(0, texCoordArray[j]);
					meshBuilder.AdvanceVertex();
				}
				ref IndexBuilder indexBuilder = ref meshBuilder.IndexBuilder;
				indexBuilder.FastQuad(0);

				meshBuilder.End();
				mesh.Draw();

				meshBuilder.Dispose();
			}
		}
	}

	public const float SQRT3INV = 0.57735f;
	readonly static int[,] st_to_vec = {
		{ 3, -1, 2 },
		{ -3, 1, 2 },

		{ 1, 3, 2 },
		{ -1, -3, 2 },

		{ -2, -1, 3 },
		{ 2, -1, -3 }
	};
	private void MakeSkyVec(float s, float t, int axis, float zFar, out Vector3 position, out Vector2 texCoord) {
		Vector3 v = default, b = default;
		int j, k;
		float width = zFar * SQRT3INV;

		if (s < -1)
			s = -1;
		else if (s > 1)
			s = 1;
		if (t < -1)
			t = -1;
		else if (t > 1)
			t = 1;

		b[0] = s * width;
		b[1] = t * width;
		b[2] = width;

		for (j = 0; j < 3; j++) {
			k = st_to_vec[axis, j];
			if (k < 0)
				v[j] = -b[-k - 1];
			else
				v[j] = b[k - 1];
			v[j] += CurrentViewOrigin[j];
		}

		s = (s + 1) * 0.5F;
		t = (t + 1) * 0.5F;

		if (s < 1.0F / 512)
			s = 1.0F / 512;
		else if (s > 511.0F / 512)
			s = 511.0F / 512;
		if (t < 1.0F / 512)
			t = 1.0F / 512;
		else if (t > 511.0 / 512)
			t = 511.0F / 512;

		t = 1.0F - t;
		position = v;
		texCoord = new(s, t);
	}


	public static readonly List<LightmapUpdateInfo> g_LightmapUpdateList = [];
	public static readonly List<LightmapTransformInfo> g_LightmapTransformList = [];

	public struct LightmapUpdateInfo
	{
		public BSPMSurface2[]? SurfaceData;
		public int SurfaceIndex;
		public ref BSPMSurface2 SurfHandle => ref SurfaceData![SurfaceIndex];
		public int TransformIndex;
	}

	public struct LightmapTransformInfo
	{
		public Model? Model;
		public Matrix3x4 XForm;
	}

	Comparison<LightmapUpdateInfo>? LightmapPageComparison;

	int LightmapPageCompareFunc(LightmapUpdateInfo surf0, LightmapUpdateInfo surf1) {
		int page0 = MaterialSystem.MaterialSortInfoArray![ModelLoader.MSurf_MaterialSortID(ref surf0.SurfHandle)].LightmapPageID;
		int page1 = MaterialSystem.MaterialSortInfoArray![ModelLoader.MSurf_MaterialSortID(ref surf1.SurfHandle)].LightmapPageID;
		return page0 - page1;
	}

	public bool InLightmapUpdate() => LightmapUpdateDepth != 0;

	public void BeginUpdateLightmaps() {
		if (++LightmapUpdateDepth == 1) {
			Assert(g_LightmapUpdateList.Count() == 0);
			materials.BeginUpdateLightmaps();
			g_LightmapTransformList.Clear();
			g_LightmapTransformList.Add(new());
			int index = g_LightmapTransformList.Count - 1;
			g_LightmapTransformList.AsSpan()[index].Model = host_state.WorldModel;
			MathLib.SetIdentityMatrix(out g_LightmapTransformList.AsSpan()[index].XForm);
		}
	}

	public void UpdateBrushModelLightmap(ModelInfo model, IClientRenderable renderable) {
		if (!r_drawbrushmodels.GetBool() || 0 == LightmapUpdateDepth)
			return;
		// todo
	}

	public void EndUpdateLightmaps() {
		Assert(LightmapUpdateDepth > 0);
		if (--LightmapUpdateDepth == 0) {
			if (g_LightmapUpdateList.Count != 0 && r_dynamiclighting.GetBool() && !ModelLoader.r_unloadlightmaps.GetBool()) {
				DLight[] lights = CL.DLights;

				g_LightmapUpdateList.Sort(LightmapPageComparison ??= LightmapPageCompareFunc);
				for (int i = g_LightmapUpdateList.Count - 1; i >= 0; --i) {
					LightmapUpdateInfo lightmapUpdateInfo = g_LightmapUpdateList[i];
					if (ModelLoader.SurfaceLighting(ref lightmapUpdateInfo.SurfHandle, host_state.WorldBrush!).LastComputedFrame != r_framecount)
						RenderDynamicLightmaps(lights, ref lightmapUpdateInfo.SurfHandle, in g_LightmapTransformList.AsSpan()[lightmapUpdateInfo.TransformIndex].XForm);
				}
			}
			materials.EndUpdateLightmaps();
			g_LightmapUpdateList.Clear();
			g_LightmapTransformList.Clear();
		}
	}

	public void RenderDynamicLightmaps(DLight[] lights, ref BSPMSurface2 surfID, in Matrix3x4 xform) {
		SurfDraw fSurfFlags = ModelLoader.MSurf_Flags(ref surfID);

		if ((fSurfFlags & SurfDraw.NoLight) != 0)
			return;

		bool changed = false;
		ref BSPMSurfaceLighting lighting = ref ModelLoader.SurfaceLighting(ref surfID, host_state.WorldBrush!);
		if ((fSurfFlags & SurfDraw.HasLightStyles) != 0) {
			for (int maps = 0; maps < BSPFileCommon.MAXLIGHTMAPS && lighting.Styles[maps] != 255; maps++) {
				if (MatSysInterface.LightStyleFrame[lighting.Styles[maps]] > lighting.LastComputedFrame) {
					changed = true;
					break;
				}
			}
		}

		bool dlightChanged = lighting.DLightFrame == r_framecount || lighting.DLightBits != 0;
		bool onlyUseLightStyles = false;

		if (r_dynamic.GetInt() == 0) {
			onlyUseLightStyles = true;
			dlightChanged = false;
		}

		if (changed || dlightChanged)
			BuildLightMap(lights, ref surfID, in xform, onlyUseLightStyles);
	}

	public void Push3DView(in ViewSetup view, ClearFlags clearFlags, ITexture? rtColor, Frustum frustum) => Push3DView(in view, clearFlags, rtColor, frustum, null);
	public void Push3DView(in ViewSetup view, ClearFlags clearFlags, ITexture? rtColor, Frustum frustum, ITexture? rtDepth) {
		ref ViewStack writeStack = ref ViewStack.Push();
		writeStack.View = view;
		writeStack.Is2DView = false;
		writeStack.NoDraw = (clearFlags & ClearFlags.NoDraw) != 0;

		ref ViewSetup topView = ref writeStack.View;

		if (topView.AspectRatio == 0.0f)
			topView.AspectRatio = (topView.Height != 0) ? ((float)topView.Width / (float)topView.Height) : 1.0f;

		ref ViewStack viewStack = ref ViewStack.Top();
		topView.AspectRatio = ComputeViewMatrices(ref viewStack.MatrixView, ref viewStack.MatrixProjection, ref viewStack.MatrixWorldToScreen, in topView);

		ZNear = topView.ZNear;
		ZFar = topView.ZFar;

		ExtractMatrices();

		if (!writeStack.NoDraw) {
			using MatRenderContextPtr renderContext = new(materials);

			if (rtColor == null)
				rtColor = renderContext.GetRenderTarget();

			renderContext.PushRenderTargetAndViewport(rtColor, rtDepth, topView.X, topView.Y, topView.Width, topView.Height);

			ClearView(topView, clearFlags, rtColor, rtDepth);

			renderContext.DepthRange(0, 1);
			renderContext.MatrixMode(MaterialMatrixMode.Projection);
			renderContext.PushMatrix();
			renderContext.LoadMatrix(MatrixProjection);
			renderContext.MatrixMode(MaterialMatrixMode.View);
			renderContext.PushMatrix();
			renderContext.LoadMatrix(MatrixView);
			renderContext.MatrixMode(MaterialMatrixMode.Model);
			renderContext.PushMatrix();

			OnViewActive(frustum);
		}
	}

	internal float ComputeViewMatrices(ref Matrix4x4 worldToView, ref Matrix4x4 viewToProjection, ref Matrix4x4 worldToProjection, in ViewSetup viewSetup) {
		float aspectRatio = viewSetup.AspectRatio;
		if (aspectRatio == 0.0f)
			aspectRatio = (viewSetup.Height != 0) ? ((float)viewSetup.Width / (float)viewSetup.Height) : 1.0f;

		// (-12152) + (MathF.Sin((float)gpGlobals.CurTime * 1) * 64))
		// ComputeViewMatrix(ref worldToView, new(-852, 907, (-12152) + (MathF.Sin((float)gpGlobals.CurTime * 1) * 64)), new QAngle(17.68f, -53.19f, 0));
		ComputeViewMatrix(ref worldToView, viewSetup.Origin, viewSetup.Angles);

		if (viewSetup.Ortho) {
			throw new NotImplementedException();
		}
		else if (viewSetup.OffCenter) {
			throw new NotImplementedException();
		}
		else if (viewSetup.ViewToProjectionOverride) {
			throw new NotImplementedException();
		}
		else
			MathLib.MatrixBuildPerspectiveX(ref viewToProjection, viewSetup.FOV, aspectRatio, viewSetup.ZNear, viewSetup.ZFar);

		MathLib.MatrixMultiply(viewToProjection, worldToView, out worldToProjection);

		return aspectRatio;
	}

	private static Matrix4x4 baseRotation;
	private static bool didInit = false;
	private void ComputeViewMatrix(ref Matrix4x4 worldToView, in Vector3 origin, in QAngle angles) {
		angles.Vectors(out Vector3 forward, out Vector3 right, out Vector3 up);

		Matrix4x4 view = default;
		view.Init(
			right.X, right.Y, right.Z, 0,
			up.X, up.Y, up.Z, 0,
			-forward.X, -forward.Y, -forward.Z, 0,
			0, 0, 0, 1
		);

		view.M14 = -Vector3.Dot(right, origin);
		view.M24 = -Vector3.Dot(up, origin);
		view.M34 = Vector3.Dot(forward, origin);

		worldToView = view;
	}
	readonly IMaterial?[] skyboxMaterials = new IMaterial?[6];
	readonly static string[] skyboxsuffix = ["rt", "bk", "lf", "ft", "up", "dn"];
	Lazy<ConVarRef> skyname = new(() => new("sv_skyname"));

	public bool LoadNamedSkys(ReadOnlySpan<char> skyname) {
		Span<char> name = stackalloc char[MAX_PATH];
		IMaterial?[] skies = new IMaterial?[6];
		bool success = true;
		const string SKYBOX_ = "skybox/";
		SKYBOX_.CopyTo(name);
		skyname.CopyTo(name[SKYBOX_.Length..]);
		int writePos = SKYBOX_.Length + skyname.Length;
		for (int i = 0; i < 6; i++) {
			skyboxsuffix[i].CopyTo(name[writePos..]);
			skies[i] = materials.FindMaterial(name[..(writePos + 2)], MaterialDefines.TEXTURE_GROUP_SKYBOX);

			if (!skies[i].IsErrorMaterial())
				continue;

			success = false;
			break;
		}

		if (!success)
			return false;

		for (int i = 0; i < 6; i++) {
			if (skyboxMaterials[i] != null) {
				skyboxMaterials[i]!.DecrementReferenceCount();
				skyboxMaterials[i] = null;
			}

			Assert(skies[i] != null);
			skyboxMaterials[i] = skies[i];
			skyboxMaterials[i]!.IncrementReferenceCount();
		}

		return true;
	}

	public void Shutdown() {

	}

	bool rebuildLightmaps = false;
	public void RebuildLightmaps() => rebuildLightmaps = true;

	public void CheckForLightingConfigChanges() {
		if (rebuildLightmaps) {
			RedownloadAllLightmaps();
		}
	}

	private void RedownloadAllLightmaps() {
		double st = Sys.Time;

		bool onlyUseLightStyles = false;

		if (r_dynamic.GetInt() == 0)
			onlyUseLightStyles = true;

		using MatRenderContextPtr renderContext = new(materials);
		if (!host_state.WorldBrush!.UnloadedLightmaps) {
			int surfaceCount = host_state.WorldBrush.NumSurfaces;

			Span<int> sortedSurfaceIndices = stackalloc int[surfaceCount];
			for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++)
				sortedSurfaceIndices[surfaceIndex] = surfaceIndex;

			SortSurfacesByLightmapID(sortedSurfaceIndices, surfaceCount); //sorts in place, so now the array really is sorted

			materials.BeginUpdateLightmaps();

			MathLib.SetIdentityMatrix(out Matrix3x4 xform);
			for (int surfaceIndex = 0; surfaceIndex < surfaceCount; surfaceIndex++) {
				ref BSPMSurface2 surfID = ref ModelLoader.SurfaceHandleFromIndex(sortedSurfaceIndices[surfaceIndex], host_state.WorldBrush!);

				BuildLightMap(CL.DLights, ref surfID, ref xform, onlyUseLightStyles);
			}

			materials.EndUpdateLightmaps();
		}

		double elapsed = (Sys.Time - st) * 1000.0;
		DevMsg($"R_RedownloadAllLightmaps took {elapsed:F3} msec!\n");

		rebuildLightmaps = false;
	}

	private void BuildLightMap(DLight[] lights, ref BSPMSurface2 surfID, in Matrix3x4 entityToWorld, bool onlyUseLightStyles) {
		bool needsBumpmap = SurfNeedsBumpedLightmaps(ref surfID);
		bool needsLightmap = SurfNeedsLightmap(ref surfID);

		if (!needsBumpmap && !needsLightmap)
			return;

		if (MaterialSystem.MaterialSortInfoArray != null) {
			Assert(ModelLoader.MSurf_MaterialSortID(ref surfID) >= 0 && ModelLoader.MSurf_MaterialSortID(ref surfID) < MaterialSystem.WorldStaticMeshes.Count);
			if ((MaterialSystem.MaterialSortInfoArray[ModelLoader.MSurf_MaterialSortID(ref surfID)].LightmapPageID == StandardLightmap.White) ||
				 (MaterialSystem.MaterialSortInfoArray[ModelLoader.MSurf_MaterialSortID(ref surfID)].LightmapPageID == StandardLightmap.WhiteBump)) {
				return;
			}
		}

		bool bDlightsInLightmap = needsLightmap || needsBumpmap;
		uint dlightMask = UpdateDlightState(lights, ref surfID, in entityToWorld, onlyUseLightStyles, bDlightsInLightmap);

		if (onlyUseLightStyles)
			dlightMask = 0;

		BuildLightMapGuts(lights, ref surfID, in entityToWorld, dlightMask, needsBumpmap, needsLightmap);
	}

	static readonly ConVar r_dynamiclighting = new("r_dynamiclighting", "1", FCvar.Cheat);
	static readonly ConVar r_avglightmap = new("r_avglightmap", "0", FCvar.Cheat | FCvar.MaterialSystemThread);
	static readonly ConVar r_maxdlights = new("r_maxdlights", "32", 0);

	public static int DLightChanged;
	public static int DLightActive;

	static int DLightVisible;
	static int DLightVisibleThisFrame;
	static int VisibleDLightCount;
	static int MaxVisibleDLightCount;

	public static void MarkDLightVisible(int dlight) {
		if ((DLightVisible & (1 << dlight)) == 0) {
			++VisibleDLightCount;
			DLightVisible |= 1 << dlight;
		}
	}

	public static void MarkDLightNotVisible(int dlight) {
		if ((DLightVisible & (1 << dlight)) != 0) {
			--VisibleDLightCount;
			DLightVisible &= ~(1 << dlight);
		}
	}

	public static void DLightStartView() {
		DLightVisibleThisFrame = 0;
		MaxVisibleDLightCount = r_maxdlights.GetInt();
	}

	public static void DLightEndView() {
		if (!CL.ActiveDlights)
			return;

		for (int lnum = 0; lnum < Constants.MAX_DLIGHTS; lnum++) {
			if ((DLightVisibleThisFrame & (1 << lnum)) != 0)
				continue;

			MarkDLightNotVisible(lnum);
		}
	}

	public static bool CanUseVisibleDLight(int dlight) {
		DLightVisibleThisFrame |= 1 << dlight;

		if ((DLightVisible & (1 << dlight)) != 0)
			return true;

		if (VisibleDLightCount >= MaxVisibleDLightCount)
			return false;

		MarkDLightVisible(dlight);
		return true;
	}

	void ComputeLightmapFromLightstyle(ref BSPMSurfaceLighting lighting, bool computeLightmap, bool computeBumpmap, int lightmapSize, bool hasBumpmapLightmapData) {
		Span<ColorRGBExp32> pLightmap = lighting.Samples.Span;

		// Compute iteration range
		int minmap, maxmap;
		if (r_lightmap.GetInt() != -1) {
			minmap = r_lightmap.GetInt();
			maxmap = minmap + 1;
		}
		else {
			minmap = 0; maxmap = BSPFileCommon.MAXLIGHTMAPS;
		}

		for (int maps = minmap; maps < maxmap && lighting.Styles[maps] != 255; ++maps) {
			if (r_lightstyle.GetInt() != -1 && lighting.Styles[maps] != r_lightstyle.GetInt()) {
				continue;
			}

			float fscalar = LightStyleValue(lighting.Styles[maps]);

			if (fscalar > 0.0f) {
				float scalar = fscalar;

				if (computeBumpmap) {
					AccumulateBumpedLightstyles(pLightmap, lightmapSize, scalar);
				}
				else if (computeLightmap) {
					if (r_avglightmap.GetInt() != 0) {
						pLightmap = lighting.AvgLightColor(maps);
						AccumulateLightstylesFlat(pLightmap, lightmapSize, scalar);
					}
					else {
						AccumulateLightstyles(pLightmap, lightmapSize, scalar);
					}
				}
			}

			// It seems like in Source, this is allowed to overflow because the next iteration would be stopping
			// anyway (due to lighting.Styles[maps] equaling 255), but in C#-managed-bounds-checking-land, this
			// errors
			int offset = (hasBumpmapLightmapData ? lightmapSize * (NUM_BUMP_VECTS + 1) : lightmapSize);
			if (offset >= pLightmap.Length)
				pLightmap = Span<ColorRGBExp32>.Empty;
			else
				pLightmap = pLightmap[offset..];
		}
	}

	private void AccumulateLightstyles(Span<ColorRGBExp32> lightmap, int lightmapSize, float scalar) {
		for (int i = 0; i < lightmapSize; ++i) {
			blocklights[0][i][0] += scalar * MathLib.TexLightToLinear(lightmap[i].R, lightmap[i].Exponent);
			blocklights[0][i][1] += scalar * MathLib.TexLightToLinear(lightmap[i].G, lightmap[i].Exponent);
			blocklights[0][i][2] += scalar * MathLib.TexLightToLinear(lightmap[i].B, lightmap[i].Exponent);
		}
	}

	private void AccumulateLightstylesFlat(Span<ColorRGBExp32> lightmap, int lightmapSize, float scalar) {
		for (int i = 0; i < lightmapSize; ++i) {
			blocklights[0][i][0] += scalar * MathLib.TexLightToLinear(lightmap[0].R, lightmap[0].Exponent);
			blocklights[0][i][1] += scalar * MathLib.TexLightToLinear(lightmap[0].G, lightmap[0].Exponent);
			blocklights[0][i][2] += scalar * MathLib.TexLightToLinear(lightmap[0].B, lightmap[0].Exponent);
		}
	}

	private void AccumulateBumpedLightstyles(Span<ColorRGBExp32> lightmap, int lightmapSize, float scalar) {
		Span<ColorRGBExp32> bumpedLightmaps_0 = lightmap[(lightmapSize)..];
		Span<ColorRGBExp32> bumpedLightmaps_1 = lightmap[(2 * lightmapSize)..];
		Span<ColorRGBExp32> bumpedLightmaps_2 = lightmap[(3 * lightmapSize)..];

		for (int i = 0; i < lightmapSize; ++i) {
			blocklights[0][i][0] += scalar * MathLib.TexLightToLinear(lightmap[i].R, lightmap[i].Exponent);
			blocklights[0][i][1] += scalar * MathLib.TexLightToLinear(lightmap[i].G, lightmap[i].Exponent);
			blocklights[0][i][2] += scalar * MathLib.TexLightToLinear(lightmap[i].B, lightmap[i].Exponent);
			Assert(blocklights[0][i][0] >= 0.0f);
			Assert(blocklights[0][i][1] >= 0.0f);
			Assert(blocklights[0][i][2] >= 0.0f);

			blocklights[1][i][0] += scalar * MathLib.TexLightToLinear(bumpedLightmaps_0[i].R, bumpedLightmaps_0[i].Exponent);
			blocklights[1][i][1] += scalar * MathLib.TexLightToLinear(bumpedLightmaps_0[i].G, bumpedLightmaps_0[i].Exponent);
			blocklights[1][i][2] += scalar * MathLib.TexLightToLinear(bumpedLightmaps_0[i].B, bumpedLightmaps_0[i].Exponent);
			Assert(blocklights[1][i][0] >= 0.0f);
			Assert(blocklights[1][i][1] >= 0.0f);
			Assert(blocklights[1][i][2] >= 0.0f);
		}

		for (int i = 0; i < lightmapSize; ++i) {
			blocklights[2][i][0] += scalar * MathLib.TexLightToLinear(bumpedLightmaps_1[i].R, bumpedLightmaps_1[i].Exponent);
			blocklights[2][i][1] += scalar * MathLib.TexLightToLinear(bumpedLightmaps_1[i].G, bumpedLightmaps_1[i].Exponent);
			blocklights[2][i][2] += scalar * MathLib.TexLightToLinear(bumpedLightmaps_1[i].B, bumpedLightmaps_1[i].Exponent);
			Assert(blocklights[2][i][0] >= 0.0f);
			Assert(blocklights[2][i][1] >= 0.0f);
			Assert(blocklights[2][i][2] >= 0.0f);

			blocklights[3][i][0] += scalar * MathLib.TexLightToLinear(bumpedLightmaps_2[i].R, bumpedLightmaps_2[i].Exponent);
			blocklights[3][i][1] += scalar * MathLib.TexLightToLinear(bumpedLightmaps_2[i].G, bumpedLightmaps_2[i].Exponent);
			blocklights[3][i][2] += scalar * MathLib.TexLightToLinear(bumpedLightmaps_2[i].B, bumpedLightmaps_2[i].Exponent);
			Assert(blocklights[3][i][0] >= 0.0f);
			Assert(blocklights[3][i][1] >= 0.0f);
			Assert(blocklights[3][i][2] >= 0.0f);
		}
	}

	static void InitLMSamples(Span<Vector4> samples, int nSamples, float value) {
		for (int i = 0; i < nSamples; i++) {
			samples[i][0] = samples[i][1] = samples[i][2] = value;
			samples[i][3] = 1.0f;
		}
	}

	int ComputeLightmapSize(ref BSPMSurface2 surfID) {
		int smax = (ModelLoader.MSurf_LightmapExtents(ref surfID)[0]) + 1;
		int tmax = (ModelLoader.MSurf_LightmapExtents(ref surfID)[1]) + 1;
		int size = smax * tmax;

		int nMaxSize = ModelLoader.MSurf_MaxLightmapSizeWithBorder(ref surfID);
		if (size > nMaxSize * nMaxSize) {
			ConMsg($"Bad lightmap extents on material \"{MaterialSystem.MaterialSortInfoArray![ModelLoader.MSurf_MaterialSortID(ref surfID)].Material!.GetName()}\"\n");
			return 0;
		}

		return size;
	}
	static Vector4[][] _makeblocklights() {
		Vector4[][] ret = new Vector4[NUM_BUMP_VECTS + 1][];
		for (int i = 0; i < NUM_BUMP_VECTS + 1; i++)
			ret[i] = new Vector4[BSPFileCommon.MAX_LIGHTMAP_DIM_INCLUDING_BORDER * BSPFileCommon.MAX_LIGHTMAP_DIM_INCLUDING_BORDER];
		return ret;
	}
	readonly Vector4[][] blocklights = _makeblocklights();

	public static float LightStyleValue(byte style) {
		return (float)MatSysInterface.LightStyleValue[style] * (1.0f / 264f);
	}

	bool AddSingleDynamicLight(DLight dl, ref BSPMSurface2 surfID, in Vector3 lightOrigin, float perpDistSq, float lightRadiusSq) {
		Vector3 local;

		if (dl.OuterAngle != 0) {
			if (dl.OuterAngle < 180.0f) {
				if (MathLib.DotProduct(dl.Direction, ModelLoader.MSurf_Plane(ref surfID).Normal) >= 0.0f)
					return false;
			}
		}

		ref ModelTexInfo tex = ref ModelLoader.MSurf_TexInfo(ref surfID);
		local.X = MathLib.DotProduct(lightOrigin, tex.LightmapVecsLuxelsPerWorldUnits[0].AsVector3D()) + tex.LightmapVecsLuxelsPerWorldUnits[0][3];
		local.Y = MathLib.DotProduct(lightOrigin, tex.LightmapVecsLuxelsPerWorldUnits[1].AsVector3D()) + tex.LightmapVecsLuxelsPerWorldUnits[1][3];
		local.Z = 0;

		local.X -= ModelLoader.MSurf_LightmapMins(ref surfID)[0];
		local.Y -= ModelLoader.MSurf_LightmapMins(ref surfID)[1];

		Vector3 intensity;
		float lightStyleValue = LightStyleValue((byte)dl.Style);
		intensity.X = MathLib.TexLightToLinear(dl.Color.R, dl.Color.Exponent) * lightStyleValue;
		intensity.Y = MathLib.TexLightToLinear(dl.Color.G, dl.Color.Exponent) * lightStyleValue;
		intensity.Z = MathLib.TexLightToLinear(dl.Color.B, dl.Color.Exponent) * lightStyleValue;

		float minlight = MathF.Max(MinLightingValue, dl.MinLight);
		float ooQuadraticAttn = lightRadiusSq * minlight;
		float ooRadiusSq = 1.0f / lightRadiusSq;

		int smax = ModelLoader.MSurf_LightmapExtents(ref surfID)[0] + 1;
		int tmax = ModelLoader.MSurf_LightmapExtents(ref surfID)[1] + 1;
		for (int t = 0; t < tmax; ++t) {
			float td = (local.Y - t) * tex.WorldUnitsPerLuxel;

			for (int s = 0; s < smax; ++s) {
				float sd = (local.X - s) * tex.WorldUnitsPerLuxel;

				float inPlaneDistSq = sd * sd + td * td;
				float totalDistSq = inPlaneDistSq + perpDistSq;
				if (totalDistSq < lightRadiusSq) {
					float scale = (totalDistSq != 0.0f) ? ooQuadraticAttn / totalDistSq : 1.0f;

					scale *= 1.0f - totalDistSq * ooRadiusSq;

					if (scale > 2.0f)
						scale = 2.0f;

					int idx = t * smax + s;

					blocklights[0][idx][0] += scale * intensity.X;
					blocklights[0][idx][1] += scale * intensity.Y;
					blocklights[0][idx][2] += scale * intensity.Z;
				}
			}
		}
		return true;
	}

	void AddSingleDynamicLightToBumpLighting(DLight dl, ref BSPMSurface2 surfID, in Vector3 lightOrigin, float perpDistSq, float lightRadiusSq, ReadOnlySpan<Vector3> bumpBasis, in Vector3 luxelBasePosition) {
		Vector3 local;
		Assert(dl.OuterAngle == 0.0f);

		ref ModelTexInfo texInfo = ref ModelLoader.MSurf_TexInfo(ref surfID);
		local.X = MathLib.DotProduct(lightOrigin, texInfo.LightmapVecsLuxelsPerWorldUnits[0].AsVector3D()) + texInfo.LightmapVecsLuxelsPerWorldUnits[0][3];
		local.Y = MathLib.DotProduct(lightOrigin, texInfo.LightmapVecsLuxelsPerWorldUnits[1].AsVector3D()) + texInfo.LightmapVecsLuxelsPerWorldUnits[1][3];
		local.Z = 0;

		local.X -= ModelLoader.MSurf_LightmapMins(ref surfID)[0];
		local.Y -= ModelLoader.MSurf_LightmapMins(ref surfID)[1];

		Vector3 intensity;
		float lightStyleValue = LightStyleValue((byte)dl.Style);
		intensity.X = MathLib.TexLightToLinear(dl.Color.R, dl.Color.Exponent) * lightStyleValue;
		intensity.Y = MathLib.TexLightToLinear(dl.Color.G, dl.Color.Exponent) * lightStyleValue;
		intensity.Z = MathLib.TexLightToLinear(dl.Color.B, dl.Color.Exponent) * lightStyleValue;

		float minlight = MathF.Max(MinLightingValue, dl.MinLight);
		float ooQuadraticAttn = lightRadiusSq * minlight;
		float ooRadiusSq = 1.0f / lightRadiusSq;

		Vector3 lightDirection = new(0, 0, 0);
		Vector3 texelWorldPosition;

		bool useLightDirection = (dl.OuterAngle != 0.0f) && (MathF.Abs(dl.Direction.LengthSquared() - 1.0f) < 1e-3);
		if (useLightDirection)
			MathLib.VectorMultiply(dl.Direction, -1.0f, out lightDirection);

		float fixupFactor = texInfo.WorldUnitsPerLuxel * texInfo.WorldUnitsPerLuxel;

		int smax = ModelLoader.MSurf_LightmapExtents(ref surfID)[0] + 1;
		int tmax = ModelLoader.MSurf_LightmapExtents(ref surfID)[1] + 1;
		for (int t = 0; t < tmax; ++t) {
			float td = (local.Y - t) * texInfo.WorldUnitsPerLuxel;

			MathLib.VectorMA(luxelBasePosition, t * fixupFactor, texInfo.LightmapVecsLuxelsPerWorldUnits[1].AsVector3D(), out texelWorldPosition);

			for (int s = 0; s < smax; ++s) {
				float sd = (local.X - s) * texInfo.WorldUnitsPerLuxel;

				float inPlaneDistSq = sd * sd + td * td;
				float totalDistSq = inPlaneDistSq + perpDistSq;

				if (totalDistSq < lightRadiusSq) {
					float scale = (totalDistSq != 0.0f) ? ooQuadraticAttn / totalDistSq : 1.0f;

					scale *= 1.0f - totalDistSq * ooRadiusSq;

					if (scale > 2.0f)
						scale = 2.0f;

					int idx = t * smax + s;

					blocklights[0][idx][0] += scale * intensity.X;
					blocklights[0][idx][1] += scale * intensity.Y;
					blocklights[0][idx][2] += scale * intensity.Z;

					if (!useLightDirection) {
						MathLib.VectorSubtract(lightOrigin, texelWorldPosition, out lightDirection);
						MathLib.VectorNormalize(ref lightDirection);
					}

					float lDotN = MathLib.DotProduct(lightDirection, ModelLoader.MSurf_Plane(ref surfID).Normal);
					if (lDotN < 1e-3)
						lDotN = 1e-3f;
					scale /= lDotN;

					int i;
					for (i = 1; i < NUM_BUMP_VECTS + 1; i++) {
						float dot = MathLib.DotProduct(lightDirection, bumpBasis[i - 1]);
						if (dot <= 0.0f)
							continue;

						blocklights[i][idx][0] += dot * scale * intensity.X;
						blocklights[i][idx][1] += dot * scale * intensity.Y;
						blocklights[i][idx][2] += dot * scale * intensity.Z;
					}
				}
			}

			MathLib.VectorMA(texelWorldPosition, fixupFactor, texInfo.LightmapVecsLuxelsPerWorldUnits[0].AsVector3D(), out texelWorldPosition);
		}
	}

	static void ComputeSurfaceBasis(ref BSPMSurface2 surfID, Span<Vector3> bumpNormals, out Vector3 luxelBasePosition) {
		Vector3 sVect, tVect;
		MathLib.VectorCopy(ModelLoader.MSurf_TexInfo(ref surfID).LightmapVecsLuxelsPerWorldUnits[0].AsVector3D(), out sVect);
		MathLib.VectorNormalize(ref sVect);
		MathLib.VectorCopy(ModelLoader.MSurf_TexInfo(ref surfID).LightmapVecsLuxelsPerWorldUnits[1].AsVector3D(), out tVect);
		MathLib.VectorNormalize(ref tVect);
		BumpVects.GetBumpNormals(in sVect, in tVect, in ModelLoader.MSurf_Plane(ref surfID).Normal, in ModelLoader.MSurf_Plane(ref surfID).Normal, bumpNormals);

		float fixupFactor = ModelLoader.MSurf_TexInfo(ref surfID).WorldUnitsPerLuxel * ModelLoader.MSurf_TexInfo(ref surfID).WorldUnitsPerLuxel;

		MathLib.VectorMultiply(ModelLoader.MSurf_TexInfo(ref surfID).LightmapVecsLuxelsPerWorldUnits[0].AsVector3D(),
			(ModelLoader.MSurf_LightmapMins(ref surfID)[0] - ModelLoader.MSurf_TexInfo(ref surfID).LightmapVecsLuxelsPerWorldUnits[0][3]) * fixupFactor,
			out luxelBasePosition);

		MathLib.VectorMA(luxelBasePosition,
			(ModelLoader.MSurf_LightmapMins(ref surfID)[1] - ModelLoader.MSurf_TexInfo(ref surfID).LightmapVecsLuxelsPerWorldUnits[1][3]) * fixupFactor,
			ModelLoader.MSurf_TexInfo(ref surfID).LightmapVecsLuxelsPerWorldUnits[1].AsVector3D(),
			out luxelBasePosition);

		MathLib.VectorMA(luxelBasePosition, ModelLoader.MSurf_Plane(ref surfID).Dist, ModelLoader.MSurf_Plane(ref surfID).Normal, out luxelBasePosition);
	}

	public uint ComputeDynamicLightMask(DLight[] lights, ref BSPMSurface2 surfID, ref BSPMSurfaceLighting lighting, in Matrix3x4 entityToWorld) {
		if (ModelLoader.SurfaceHasDispInfo(ref surfID))
			return surfID.DispInfo!.ComputeDynamicLightMask(lights);

		if (!CL.ActiveDlights)
			return 0;

		int lightMask = 0;
		for (int lnum = 0, testBit = 1, mask = DLightActive; lnum < Constants.MAX_DLIGHTS; lnum++, mask >>= 1, testBit <<= 1) {
			if ((mask & 1) != 0) {
				if ((lighting.DLightBits & testBit) == 0)
					continue;

				if ((lights[lnum].Flags & (DLightFlags.NoWorldIllumination | DLightFlags.DisplacementMask)) != 0)
					continue;

				if (!CanUseVisibleDLight(lnum))
					continue;

				MathLib.VectorITransform(lights[lnum].Origin, in entityToWorld, out Vector3 lightOrigin);

				float perpDistSq = MathLib.DotProduct(lightOrigin, ModelLoader.MSurf_Plane(ref surfID).Normal) - ModelLoader.MSurf_Plane(ref surfID).Dist;
				if (perpDistSq < GLRLight.DLIGHT_BEHIND_PLANE_DIST) {
					lighting.DLightBits &= ~testBit;
					continue;
				}

				perpDistSq *= perpDistSq;

				float lightRadiusSq = lights[lnum].GetRadiusSquared();
				if (lightRadiusSq <= perpDistSq) {
					lighting.DLightBits &= ~testBit;
					continue;
				}

				lightMask |= testBit;
			}
		}

		return (uint)lightMask;
	}

	public void AddDynamicLights(DLight[] lights, ref BSPMSurface2 surfID, in Matrix3x4 entityToWorld, bool needsBumpmap, uint lightMask) {
		Span<Vector3> bumpNormals = stackalloc Vector3[NUM_BUMP_VECTS];
		bool computedBumpBasis = false;
		Vector3 luxelBasePosition = default;

		if (ModelLoader.SurfaceHasDispInfo(ref surfID)) {
			surfID.DispInfo!.AddDynamicLights(lights, lightMask);
			return;
		}

		for (int lnum = 0, testBit = 1, mask = (int)lightMask; lnum < Constants.MAX_DLIGHTS && mask != 0; lnum++, mask >>= 1, testBit <<= 1) {
			if ((mask & 1) != 0) {
				MathLib.VectorITransform(lights[lnum].Origin, in entityToWorld, out Vector3 lightOrigin);

				float perpDistSq = MathLib.DotProduct(lightOrigin, ModelLoader.MSurf_Plane(ref surfID).Normal) - ModelLoader.MSurf_Plane(ref surfID).Dist;
				if (perpDistSq < GLRLight.DLIGHT_BEHIND_PLANE_DIST)
					continue;

				perpDistSq *= perpDistSq;

				float lightRadiusSq = lights[lnum].GetRadiusSquared();
				if (lightRadiusSq <= perpDistSq)
					continue;

				if (!needsBumpmap) {
					AddSingleDynamicLight(lights[lnum], ref surfID, in lightOrigin, perpDistSq, lightRadiusSq);
					continue;
				}

				if (!computedBumpBasis) {
					ComputeSurfaceBasis(ref surfID, bumpNormals, out luxelBasePosition);
					computedBumpBasis = true;
				}

				AddSingleDynamicLightToBumpLighting(lights[lnum], ref surfID, in lightOrigin, perpDistSq, lightRadiusSq, bumpNormals, in luxelBasePosition);
			}
		}
	}

	public uint UpdateDlightState(DLight[] lights, ref BSPMSurface2 surfID, in Matrix3x4 entityToWorld, bool onlyUseLightStyles, bool lightmap) {
		uint dlightMask = 0;
		ref BSPMSurfaceLighting lighting = ref ModelLoader.SurfaceLighting(ref surfID, host_state.WorldBrush!);

		lighting.DLightBits &= DLightActive;
		lighting.LastComputedFrame = r_framecount;

		if (!onlyUseLightStyles) {
			if (lightmap && lighting.DLightFrame == r_framecount)
				dlightMask = ComputeDynamicLightMask(lights, ref surfID, ref lighting, in entityToWorld);

			if (dlightMask == 0 || lighting.DLightBits == 0) {
				lighting.DLightBits = 0;
				ModelLoader.MSurf_Flags(ref surfID) &= ~SurfDraw.HasDLight;
			}
		}

		return dlightMask;
	}

	public void BuildLightMapGuts(DLight[] lights, ref BSPMSurface2 surfID, in Matrix3x4 entityToWorld, uint dlightMask, bool needsBumpmap, bool needsLightmap) {
		Assert(!host_state.WorldBrush!.UnloadedLightmaps);
		int bumpID;
		ref BSPMSurfaceLighting pLighting = ref ModelLoader.SurfaceLighting(ref surfID, host_state.WorldBrush);

		int size = ComputeLightmapSize(ref surfID);
		if (size == 0)
			return;

		bool hasBumpmap = ModelLoader.SurfHasBumpedLightmaps(ref surfID);
		bool hasLightmap = ModelLoader.SurfHasLightmap(ref surfID);

		if (needsLightmap)
			InitLMSamples(blocklights[0], size, hasLightmap ? 0.0f : 1.0f);

		if (needsBumpmap)
			for (bumpID = 1; bumpID < NUM_BUMP_VECTS + 1; bumpID++)
				InitLMSamples(blocklights[bumpID], size, hasBumpmap ? 0.0f : 1.0f);

		if ((hasLightmap && needsLightmap) || (hasBumpmap && needsBumpmap))
			ComputeLightmapFromLightstyle(ref pLighting, (hasLightmap && needsLightmap), (hasBumpmap && needsBumpmap), size, hasBumpmap);
		else if (!hasBumpmap && needsBumpmap && hasLightmap) {
			ComputeLightmapFromLightstyle(ref pLighting, true, false, size, hasBumpmap);

			for (bumpID = 0; bumpID < (hasBumpmap ? (NUM_BUMP_VECTS + 1) : 1); bumpID++)
				for (int i = 0; i < size; i++)
					blocklights[bumpID][i].AsVector3D() = blocklights[0][i].AsVector3D();
		}
		else if (needsBumpmap && !hasLightmap) {
			// set to full bright if no light data
			InitLMSamples(blocklights[1], size, 0.0f);
			InitLMSamples(blocklights[2], size, 0.0f);
			InitLMSamples(blocklights[3], size, 0.0f);
		}
		else if (!needsBumpmap && !needsLightmap) {

		}
		else if (needsLightmap && !hasLightmap) {

		}
		else {
			Assert(false);
		}

		if (dlightMask != 0)
			AddDynamicLights(lights, ref surfID, in entityToWorld, needsBumpmap, dlightMask);

		// Update the texture state
		UpdateLightmapTextures(ref surfID, needsBumpmap);
	}

	private void UpdateLightmapTextures(ref BSPMSurface2 surfID, bool needsBumpmap) {
		if (MaterialSystem.MaterialSortInfoArray != null) {
			Span<int> lightmapSize = stackalloc int[2];
			Span<int> offsetIntoLightmapPage = stackalloc int[2];
			lightmapSize[0] = (ModelLoader.MSurf_LightmapExtents(ref surfID)[0]) + 1;
			lightmapSize[1] = (ModelLoader.MSurf_LightmapExtents(ref surfID)[1]) + 1;
			offsetIntoLightmapPage[0] = ModelLoader.MSurf_OffsetIntoLightmapPage(ref surfID)[0];
			offsetIntoLightmapPage[1] = ModelLoader.MSurf_OffsetIntoLightmapPage(ref surfID)[1];
			Assert(ModelLoader.MSurf_MaterialSortID(ref surfID) >= 0 && ModelLoader.MSurf_MaterialSortID(ref surfID) < MaterialSystem.WorldStaticMeshes.Count);
			// FIXME: Should differentiate between bumped and unbumped since the perf characteristics
			// are completely different?
			//		MarkPage( materialSortInfoArray[MSurf_MaterialSortID( surfID )].lightmapPageID );

			if (needsBumpmap) {
				materials.UpdateLightmap(MaterialSystem.MaterialSortInfoArray[ModelLoader.MSurf_MaterialSortID(ref surfID)].LightmapPageID,
					lightmapSize, offsetIntoLightmapPage, blocklights[0].AsSpan().Cast<Vector4, float>(), blocklights[1].AsSpan().Cast<Vector4, float>(), blocklights[2].AsSpan().Cast<Vector4, float>(), blocklights[3].AsSpan().Cast<Vector4, float>());
			}
			else {
				materials.UpdateLightmap(MaterialSystem.MaterialSortInfoArray[ModelLoader.MSurf_MaterialSortID(ref surfID)].LightmapPageID,
					lightmapSize, offsetIntoLightmapPage, blocklights[0].AsSpan().Cast<Vector4, float>(), null, null, null);
			}
		}
	}

	private unsafe void SortSurfacesByLightmapID(Span<int> toSort, int surfaceCount) {
		Span<BSPMSurface2> surfaces = host_state.WorldBrush!.Surfaces2;

		int* pSortTemp = stackalloc int[surfaceCount];
		Span<int> iCounts = stackalloc int[256];
		Span<int> iOffsetTable = stackalloc int[256];

		fixed (int* fpToSort = toSort) {
			int* pToSort = fpToSort;
			for (int radix = 0; radix < 4; ++radix) {
				{
					int* pTemp = pToSort;
					pToSort = pSortTemp;
					pSortTemp = pTemp;
				}

				memreset(iCounts);
				int iBitOffset = radix * 8;
				for (int i = 0; i < surfaceCount; ++i) {
					int val = (MaterialSystem.MaterialSortInfoArray![ModelLoader.MSurf_MaterialSortID(ref surfaces[pSortTemp[i]])].LightmapPageID >> iBitOffset) & 0xFF;
					++iCounts[val];
				}

				iOffsetTable[0] = 0;
				for (int i = 0; i < 255; ++i) {
					iOffsetTable[i + 1] = iOffsetTable[i] + iCounts[i];
				}

				for (int i = 0; i < surfaceCount; ++i) {
					int val = (MaterialSystem.MaterialSortInfoArray![ModelLoader.MSurf_MaterialSortID(ref surfaces[pSortTemp[i]])].LightmapPageID >> iBitOffset) & 0xFF;
					int iWriteIndex = iOffsetTable[val];
					pToSort[iWriteIndex] = pSortTemp[i];
					++iOffsetTable[val];
				}
			}
		}
	}

	public void UnloadSkys() {

	}

	public void LevelShutdown() {
		R_Areaportal_LevelShutdown();
	}

	public void ViewDrawFade(Span<byte> color, IMaterial? fadeMaterial) {
		if (color.IsEmpty || color[3] == 0)
			return;

		if (fadeMaterial == null)
			return;

		ref ViewSetup view = ref CurrentView();

		using MatRenderContextPtr renderContext = new(materials);

		renderContext.Bind(fadeMaterial);
		fadeMaterial.AlphaModulate(color[3] * (1.0f / 255.0f));
		fadeMaterial.ColorModulate(color[0] * (1.0f / 255.0f),
			color[1] * (1.0f / 255.0f),
			color[2] * (1.0f / 255.0f));

		bool oldIgnoreZ = fadeMaterial.GetMaterialVarFlag(MaterialVarFlags.IgnoreZ);
		fadeMaterial.SetMaterialVarFlag(MaterialVarFlags.IgnoreZ, true);

		float texWidth = fadeMaterial.GetMappingWidth();
		float texHeight = fadeMaterial.GetMappingHeight();
		float uOffset = 0.5f / texWidth;
		float vOffset = 0.5f / texHeight;

		renderContext.MatrixMode(MaterialMatrixMode.Projection);

		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		renderContext.Scale(1, -1, 1);
		renderContext.Ortho(0, 0, view.Width, view.Height, -99999, 99999);

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		IMesh mesh = renderContext.GetDynamicMesh();
		MeshBuilder meshBuilder = new();
		meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, 1);

		float offset = 0.5f;

		// Note - the viewport has already adjusted the origin
		float x1 = 0.0f - offset;
		float x2 = view.Width - offset;
		float y1 = 0.0f - offset;
		float y2 = view.Height - offset;

		// adjust nominal uvs to reflect adjusted xys
		float u1 = MathLib.Lerp(uOffset, 1 - uOffset, view.X, view.X + view.Width, x1);
		float u2 = MathLib.Lerp(uOffset, 1 - uOffset, view.X, view.X + view.Width, x2);
		float v1 = MathLib.Lerp(vOffset, 1 - vOffset, view.Y, view.Y + view.Height, y1);
		float v2 = MathLib.Lerp(vOffset, 1 - vOffset, view.Y, view.Y + view.Height, y2);

		for (int corner = 0; corner < 4; corner++) {
			bool left = (corner == 0) || (corner == 3);
			meshBuilder.Position3f(left ? x1 : x2, (corner & 2) != 0 ? y2 : y1, 0.0f);
			meshBuilder.TexCoord2f(0, left ? u1 : u2, (corner & 2) != 0 ? v2 : v1);
			meshBuilder.AdvanceVertex();
		}
		meshBuilder.End();
		mesh.Draw();
		meshBuilder.Dispose();

		renderContext.MatrixMode(MaterialMatrixMode.Model);
		renderContext.PopMatrix();
		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PopMatrix();
		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PopMatrix();

		fadeMaterial.SetMaterialVarFlag(MaterialVarFlags.IgnoreZ, oldIgnoreZ);
	}

	public IWorldRenderList? CreateWorldList() => AllocWorldRenderList();

	public void BuildWorldLists(IWorldRenderList? list, ref WorldListInfo info, int forceViewLeaf, ReadOnlySpan<VisOverrideData> visData, bool shadowDepth, Span<float> reflectionWaterHeight) {
		Assert(list);
		Assert(LightmapUpdateDepth > 0 || g_LightmapUpdateList.Count == 0);

		if (!shadowDepth)
			BeginUpdateLightmaps();

		R_BuildWorldLists(list!, ref info, forceViewLeaf, visData, shadowDepth, reflectionWaterHeight);

		if (!shadowDepth)
			EndUpdateLightmaps();

		Assert(LightmapUpdateDepth > 0 || g_LightmapUpdateList.Count == 0);
	}

	public void DrawWorldLists(IWorldRenderList? list, uint flags, float waterZAdjust) {
		Assert(list);
		R_DrawWorldLists(list!, (DrawWorldListFlags)flags, waterZAdjust);
	}

	public void ViewSetupVisEx(bool novis, Span<Vector3> origin, out uint returnFlags) {
		throw new NotImplementedException();
	}

	public void OverrideViewFrustum(Frustum custom) {
		throw new NotImplementedException();
	}

	public void UpdateBrushModelLightmap(Model? model, IClientRenderable? renderable) {
		throw new NotImplementedException();
	}
}
