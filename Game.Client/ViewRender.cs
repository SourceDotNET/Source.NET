global using static Game.Client.ViewRenderConVars;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Engine;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Game.Client;

public static class ViewRenderConVars
{
	internal readonly static ConVar cl_maxrenderable_dist = new("3000", FCvar.Cheat, "Max distance from the camera at which things will be rendered");
	internal readonly static ConVar r_drawopaqueworld = new("1", FCvar.Cheat);
	internal readonly static ConVar r_drawtranslucentworld = new("1", FCvar.Cheat);
	internal readonly static ConVar r_3dsky = new("1", 0, "Enable the rendering of 3d sky boxes");
	internal readonly static ConVar r_3dskyinreflection = new("1", 0, "Enable the rendering of 3d sky boxes in water reflection");
	internal readonly static ConVar r_skybox = new("1", FCvar.Cheat, "Enable the rendering of sky boxes");
	internal readonly static ConVar r_drawviewmodel = new("1", FCvar.Cheat);
	internal readonly static ConVar r_drawtranslucentrenderables = new("1", FCvar.Cheat);
	internal readonly static ConVar r_drawopaquerenderables = new("1", FCvar.Cheat);
	internal readonly static ConVar r_threaded_renderables = new("0", 0);
	internal readonly static ConVar r_DrawDetailProps = new("1", FCvar.None, "0=Off, 1=Normal, 2=Wireframe");
	internal readonly static ConVar r_debugcheapwater = new("0", FCvar.Cheat);
	internal readonly static ConVar r_waterforceexpensive = new("0", FCvar.Archive);
	internal readonly static ConVar r_waterforcereflectentities = new("0", 0);
	internal readonly static ConVar r_WaterDrawRefraction = new("1", 0, "Enable water refraction");
	internal readonly static ConVar r_WaterDrawReflection = new("1", 0, "Enable water reflection");
	internal readonly static ConVar r_ForceWaterLeaf = new("1", 0, "Enable for optimization to water - considers view in leaf under water for purposes of culling");
	internal readonly static ConVar mat_drawwater = new("1", FCvar.Cheat);
	internal readonly static ConVar mat_clipz = new("1", 0);
	internal readonly static ConVar fog_override = new("0", FCvar.Cheat);
	internal readonly static ConVar fog_start = new("-1", FCvar.Cheat);
	internal readonly static ConVar fog_end = new("-1", FCvar.Cheat);
	internal readonly static ConVar fog_color = new("-1 -1 -1", FCvar.Cheat);
	internal readonly static ConVar fog_enable = new("1", FCvar.Cheat);
	internal readonly static ConVar fog_startskybox = new("-1", FCvar.Cheat);
	internal readonly static ConVar fog_endskybox = new("-1", FCvar.Cheat);
	internal readonly static ConVar fog_maxdensityskybox = new("-1", FCvar.Cheat);
	internal readonly static ConVar fog_colorskybox = new("-1 -1 -1", FCvar.Cheat);
	internal readonly static ConVar fog_enableskybox = new("1", FCvar.Cheat);
	internal readonly static ConVar fog_maxdensity = new("-1", FCvar.Cheat);
	internal readonly static ConVar fog_radial = new("-1", FCvar.Cheat);
	internal readonly static ConVar fog_radialskybox = new("-1", FCvar.Cheat);
}

public struct WaterRenderInfo
{
	public bool CheapWater;
	public bool Reflect;
	public bool Refract;
	public bool ReflectEntities;
	public bool DrawWaterSurface;
	public bool OpaqueWater;
}

public class RenderExecutor
{
	public virtual void AddView(Rendering3dView view) { }
	public virtual void Execute() { }
	public RenderExecutor(ViewRender mainView) {
		this.mainView = mainView;
	}
	protected ViewRender mainView;
}

public class SimpleRenderExecutor : RenderExecutor
{
	public SimpleRenderExecutor(ViewRender mainView) : base(mainView) {

	}
	public override void AddView(Rendering3dView view) {
		Base3dView? prevRenderer = mainView.SetActiveRenderer(view);
		view.Draw();
		mainView.SetActiveRenderer(prevRenderer);
	}
	public override void Execute() {

	}
}

public struct IntroDataBlendPass
{
	public int BlendMode;
	public float Alpha;
}

public class IntroData
{
	public bool DrawPrimary;
	public Vector3 CameraView;
	public QAngle CameraViewAngles;
	public float PlayerViewFOV;
	public readonly List<IntroDataBlendPass> Passes = [];

	public InlineArray4<float> CurrentFadeColor;

	public static IntroData? g_pIntroData;
}

public enum ViewID : sbyte
{
	Illegal = -2,
	None = -1,
	Main = 0,
	Sky3D = 1,
	Monitor = 2,
	Reflection = 3,
	Refraction = 4,
	IntroPlayer = 5,
	IntroCamera = 6,
	ShadowDepthTexture = 7,
	SSAO = 8,
	Count
}

public class Base3dView
{
	protected Frustum frustum;
	protected ViewRender mainView;
	public Base3dView(ViewRender mainView) {
		this.mainView = mainView;
		frustum = mainView.GetFrustum();
	}

	protected ViewSetup setup;
	public Frustum GetFrustrum() => frustum;
	public virtual DrawFlags GetDrawFlags() => 0;
}
public class BaseWorldView : Rendering3dView
{
	public BaseWorldView(ViewRender mainView) : base(mainView) { }

	static Vector3 SavedLinearLightMapScale = new(-1, -1, -1);

	protected static void SetLightmapScaleForWater() {
		if (Singleton<IMaterialSystemHardwareConfig>().GetHDRType() == HDRType.Integer) {
			using MatRenderContextPtr renderContext = new(materials);
			SavedLinearLightMapScale = renderContext.GetToneMappingScaleLinear();
			Vector3 t25 = SavedLinearLightMapScale;
			t25 *= 0.25f;
			renderContext.SetToneMappingScaleLinear(t25);
		}
	}

	protected bool AdjustView(float waterHeight) {
		if ((DrawFlags & DrawFlags.RenderRefraction) != 0) {
			ITexture texture = RenderTexture.GetWaterRefractionTexture()!;

			setup.X = setup.Y = 0;
			setup.Width = texture.GetActualWidth();
			setup.Height = texture.GetActualHeight();

			return true;
		}

		if ((DrawFlags & DrawFlags.RenderReflection) != 0) {
			ITexture texture = RenderTexture.GetWaterReflectionTexture()!;

			if (setup.ViewToProjectionOverride)
				setup.ViewToProjection.M23 = -setup.ViewToProjection.M23;

			setup.X = setup.Y = 0;
			setup.Width = texture.GetActualWidth();
			setup.Height = texture.GetActualHeight();
			setup.Angles.X = -setup.Angles.X;
			setup.Angles.Z = -setup.Angles.Z;
			setup.Origin.Z -= 2.0f * (setup.Origin.Z - waterHeight);
			return true;
		}

		return false;
	}

	protected void PushView(float waterHeight) {
		float spread = 2.0f;
		if ((DrawFlags & DrawFlags.FudgeUp) != 0)
			waterHeight += spread;
		else
			waterHeight -= spread;

		MaterialHeightClipMode clipMode = MaterialHeightClipMode.Disable;
		if ((DrawFlags & DrawFlags.ClipZ) != 0 && mat_clipz.GetBool()) {
			if ((DrawFlags & DrawFlags.ClipBelow) != 0)
				clipMode = MaterialHeightClipMode.RenderAboveHeight;
			else
				clipMode = MaterialHeightClipMode.RenderBelowHeight;
		}

		using MatRenderContextPtr renderContext = new(mainView.materials);

		if ((DrawFlags & DrawFlags.RenderRefraction) != 0) {
			renderContext.SetFogZ(waterHeight);
			renderContext.SetHeightClipZ(waterHeight);
			renderContext.SetHeightClipMode(clipMode);

			render.Push3DView(in setup, ClearFlags, RenderTexture.GetWaterRefractionTexture(), GetFrustrum(), null);

			return;
		}

		if ((DrawFlags & DrawFlags.RenderReflection) != 0) {
			ITexture? texture = RenderTexture.GetWaterReflectionTexture();

			renderContext.SetFogZ(waterHeight);
			renderContext.SetHeightClipZ(waterHeight);
			renderContext.SetHeightClipMode(clipMode);

			render.Push3DView(in setup, ClearFlags, texture, GetFrustrum(), null);

			SetLightmapScaleForWater();
			return;
		}

		if ((ClearFlags & (ClearFlags.ClearDepth | ClearFlags.ClearColor | ClearFlags.ClearStencil)) != 0) {
			if ((ClearFlags & ClearFlags.ClearObeyStencil) != 0)
				renderContext.ClearBuffersObeyStencil((ClearFlags & ClearFlags.ClearColor) != 0, (ClearFlags & ClearFlags.ClearDepth) != 0);
			else
				renderContext.ClearBuffers((ClearFlags & ClearFlags.ClearColor) != 0, (ClearFlags & ClearFlags.ClearDepth) != 0, (ClearFlags & ClearFlags.ClearStencil) != 0);
		}

		renderContext.SetHeightClipMode(clipMode);
		if (clipMode != MaterialHeightClipMode.Disable)
			renderContext.SetHeightClipZ(waterHeight);
	}

	protected void PopView() {
		using MatRenderContextPtr renderContext = new(mainView.materials);

		renderContext.SetHeightClipMode(MaterialHeightClipMode.Disable);
		if ((DrawFlags & (DrawFlags.RenderRefraction | DrawFlags.RenderReflection)) != 0) {
			render.PopView(GetFrustrum());
			if (SavedLinearLightMapScale.X >= 0) {
				renderContext.SetToneMappingScaleLinear(SavedLinearLightMapScale);
				SavedLinearLightMapScale.X = -1;
			}
		}
	}

	protected void DrawSetup(float waterHeight, DrawFlags setupFlags, float waterZAdjust, int forceViewLeaf = -1) {
		ViewID savedViewID = ViewRender.g_CurrentViewID;
		ViewRender.g_CurrentViewID = ViewID.Illegal;

		bool viewChanged = AdjustView(waterHeight);

		if (viewChanged)
			render.Push3DView(in setup, 0, null, GetFrustrum(), null);

		render.BeginUpdateLightmaps();

		bool drawEntities = (setupFlags & DrawFlags.DrawEntities) != 0;
		bool drawReflection = (setupFlags & DrawFlags.RenderReflection) != 0;
		BuildWorldRenderLists(drawEntities, forceViewLeaf, true, false, drawReflection ? new Span<float>(ref waterHeight) : default);

		if (drawEntities)
			BuildRenderableRenderLists(savedViewID);

		render.EndUpdateLightmaps();

		if (viewChanged)
			render.PopView(GetFrustrum());

		ViewRender.g_CurrentViewID = savedViewID;
	}

	static void MaybeInvalidateLocalPlayerAnimation() {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if ((player != null) /*&& pPlayer.InFirstPersonView()*/) {
			player.InvalidateBoneCache();

			C_BaseCombatWeapon? weapon = player.GetActiveWeapon();
			if (weapon != null)
				weapon.InvalidateBoneCache();
		}
	}

	protected void DrawExecute(float waterHeight, ViewID viewID, float waterZAdjust) {
		ViewID savedViewID = ViewRender.g_CurrentViewID;

		ViewRender.g_CurrentViewID = ViewID.ShadowDepthTexture;
		MaybeInvalidateLocalPlayerAnimation();
		g_ClientShadowMgr.ComputeShadowTextures(in setup, WorldListInfo.LeafCount, WorldListInfo.LeafList);
		MaybeInvalidateLocalPlayerAnimation();

		engine.Sound_ExtraUpdate();

		ViewRender.g_CurrentViewID = viewID;

		DrawFlags drawFlagsBackup = DrawFlags;
		DrawFlags |= mainView.GetBaseDrawFlags();

		PushView(waterHeight);

		using MatRenderContextPtr renderContext = new(mainView.materials);

		ITexture? saveFrameBufferCopyTexture = renderContext.GetFrameBufferCopyTexture(0);
		renderContext.SetFrameBufferCopyTexture(RenderTexture.GetPowerOfTwoFrameBufferTexture());

		RenderDepthMode depthMode = RenderDepthMode.Normal;

		if ((DrawFlags & DrawFlags.DrawEntities) != 0) {
			DrawWorld(waterZAdjust);
			DrawOpaqueRenderables(depthMode);
			DrawTranslucentRenderables(depthMode);
		}
		else {
			DrawWorld(waterZAdjust);
			DrawTranslucentWorldInLeaves(false);
		}

		renderContext.SetFrameBufferCopyTexture(saveFrameBufferCopyTexture);
		PopView();

		DrawFlags = drawFlagsBackup;

		ViewRender.g_CurrentViewID = savedViewID;
	}
}

public class SimpleWorldView : BaseWorldView
{
	VisibleFogVolumeInfo FogInfo;

	public SimpleWorldView(ViewRender mainView) : base(mainView) { }
	public void Setup(in ViewSetup view, ClearFlags clearFlags, bool drawSkybox, in VisibleFogVolumeInfo fogInfo, in WaterRenderInfo waterInfo) {
		base.Setup(in view);

		ClearFlags = clearFlags;
		DrawFlags = DrawFlags.DrawEntities;

		if (!waterInfo.OpaqueWater)
			DrawFlags |= DrawFlags.RenderUnderwater | DrawFlags.RenderAbovewater;
		else {
			bool viewIntersectsWater = ViewRender.DoesViewPlaneIntersectWater(fogInfo.WaterHeight, fogInfo.VisibleFogVolume);
			if (viewIntersectsWater)
				DrawFlags |= DrawFlags.RenderUnderwater | DrawFlags.RenderAbovewater;
			else if (fogInfo.EyeInFogVolume)
				DrawFlags |= DrawFlags.RenderUnderwater;
			else
				DrawFlags |= DrawFlags.RenderAbovewater;
		}
		if (waterInfo.DrawWaterSurface)
			DrawFlags |= DrawFlags.RenderWater;

		if (!fogInfo.EyeInFogVolume && drawSkybox)
			DrawFlags |= DrawFlags.DrawSkybox;

		FogInfo = fogInfo;
	}
	public override void Draw() {
		DrawSetup(0, DrawFlags, 0);

		if (!FogInfo.EyeInFogVolume)
			EnableWorldFog();
		else {
			ClearFlags |= ClearFlags.ClearColor;

			SetFogVolumeState(in FogInfo, false);

			using MatRenderContextPtr fogRenderContext = new(mainView.materials);

			fogRenderContext.GetFogColor(out Color fogColor);
			fogRenderContext.ClearColor4ub(fogColor.R, fogColor.G, fogColor.B, 255);
		}

		DrawExecute(0, CurrentViewID(), 0);

		using MatRenderContextPtr renderContext = new(mainView.materials);
		renderContext.ClearColor4ub(0, 0, 0, 255);
	}
}

public class BaseWaterView : BaseWorldView
{
	protected VisibleFogVolumeInfo FogInfo;
	protected float WaterHeight;

	public BaseWaterView(ViewRender mainView) : base(mainView) { }
}

public class AboveWaterView : BaseWaterView
{
	WaterRenderInfo WaterInfo;
	readonly ReflectionView Reflection;
	readonly RefractionView Refraction;
	readonly IntersectionView Intersection;
	readonly SkyboxReflectionView SkyboxReflection;

	public AboveWaterView(ViewRender mainView) : base(mainView) {
		Reflection = new(mainView, this);
		Refraction = new(mainView, this);
		Intersection = new(mainView, this);
		SkyboxReflection = new(mainView, this);
	}

	public void Setup(in ViewSetup view, bool drawSkybox, in VisibleFogVolumeInfo fogInfo, in WaterRenderInfo waterInfo) {
		base.Setup(in view);

		WaterHeight = fogInfo.WaterHeight;

		DrawFlags = DrawFlags.RenderAbovewater | DrawFlags.DrawEntities;
		ClearFlags = ClearFlags.ClearDepth;

		if (drawSkybox)
			DrawFlags |= DrawFlags.DrawSkybox;

		if (waterInfo.DrawWaterSurface)
			DrawFlags |= DrawFlags.RenderWater;
		if (!waterInfo.Refract && !waterInfo.OpaqueWater)
			DrawFlags |= DrawFlags.RenderUnderwater;

		FogInfo = fogInfo;
		WaterInfo = waterInfo;
	}

	public override void ReleaseLists() {
		base.ReleaseLists();
		Reflection.ReleaseLists();
		Refraction.ReleaseLists();
		Intersection.ReleaseLists();
		SkyboxReflection.ReleaseLists();
	}

	public override void Draw() {
		if (WaterInfo.Reflect) {
			bool drew3dSkybox = false;
			if (r_3dskyinreflection.GetBool() && r_3dsky.GetInt() != 0 && SkyboxReflection.Setup()) {
				mainView.AddViewToScene(SkyboxReflection);
				drew3dSkybox = true;

				mainView.SetupVis(in setup, out _);
			}

			Reflection.Setup(WaterInfo.ReflectEntities, drew3dSkybox);
			mainView.AddViewToScene(Reflection);
		}

		bool viewIntersectsWater = false;

		if (WaterInfo.Refract) {
			Refraction.Setup();
			mainView.AddViewToScene(Refraction);

			viewIntersectsWater = ViewRender.DoesViewPlaneIntersectWater(FogInfo.WaterHeight, FogInfo.VisibleFogVolume);
		}
		else if ((DrawFlags & DrawFlags.DrawSkybox) == 0)
			ClearFlags |= ClearFlags.ClearColor;

		if (viewIntersectsWater)
			DrawFlags |= DrawFlags.ClipZ | DrawFlags.ClipBelow;

		DrawSetup(WaterHeight, DrawFlags, 0);
		EnableWorldFog();
		DrawExecute(WaterHeight, CurrentViewID(), 0);

		if (WaterInfo.Refract && viewIntersectsWater) {
			Intersection.Setup();
			mainView.AddViewToScene(Intersection);
		}
	}

	class ReflectionView(ViewRender mainView, AboveWaterView outer) : BaseWorldView(mainView)
	{
		public void Setup(bool reflectEntities, bool drew3dSkybox) {
			base.Setup(in outer.setup);

			ClearFlags = ClearFlags.ClearDepth;

			DrawFlags = DrawFlags.RenderReflection | DrawFlags.ClipZ | DrawFlags.ClipBelow | DrawFlags.RenderAbovewater;

			if (!drew3dSkybox)
				DrawFlags |= DrawFlags.DrawSkybox;

			if (reflectEntities)
				DrawFlags |= DrawFlags.DrawEntities;
		}

		public override void Draw() {
			ViewID saveViewID = CurrentViewID();
			SetupCurrentView(in setup.Origin, in setup.Angles, ViewID.Reflection);

			DrawSetup(outer.FogInfo.WaterHeight, DrawFlags, 0.0f, outer.FogInfo.VisibleFogVolumeLeaf);

			EnableWorldFog();
			DrawExecute(outer.FogInfo.WaterHeight, ViewID.Reflection, 0.0f);

			SetupCurrentView(in setup.Origin, in setup.Angles, saveViewID);

			using MatRenderContextPtr renderContext = new(mainView.materials);
			renderContext.Flush();
		}
	}

	class SkyboxReflectionView(ViewRender mainView, AboveWaterView outer) : BaseWorldView(mainView)
	{
		SafeFieldPointer<PlayerLocalData, Sky3DParams> Sky3dParams = new();

		public bool Setup() {
			base.Setup(in outer.setup);

			SkyboxVisibility skyboxVisible = SkyboxVisibility.Skybox3D;
			Sky3dParams = PreRender3dSkyboxWorld(ref skyboxVisible);

			if (Sky3dParams.IsNull)
				return false;

			ClearFlags = ClearFlags.ClearDepth;
			ClearFlags |= ClearFlags.ClearColor;

			DrawFlags = DrawFlags.RenderReflection | DrawFlags.ClipZ | DrawFlags.ClipBelow | DrawFlags.RenderAbovewater;
			DrawFlags |= DrawFlags.DrawSkybox;
			return true;
		}

		public override void Draw() {
			if (Sky3dParams.IsNull)
				return;

			ref Sky3DParams sky3dParams = ref Sky3dParams.Get();

			Span<byte> areaBits = render.GetAreaBits();
			Span<byte> saveBits = stackalloc byte[Constants.MAX_AREA_STATE_BYTES];
			areaBits.CopyTo(saveBits);
			areaBits.Clear();

			areaBits[sky3dParams.Area >> 3] |= (byte)(1 << (sky3dParams.Area & 7));

			setup.ZNear = 2.0f;
			setup.ZFar = WorldSize.MAX_TRACE_LENGTH;

			float scale = (sky3dParams.Scale > 0) ? (1.0f / sky3dParams.Scale) : 1.0f;
			Vector3 skyOrigin = sky3dParams.Origin;
			setup.Origin *= scale;
			setup.Origin += skyOrigin;

			float waterHeight = skyOrigin.Z + (scale * outer.FogInfo.WaterHeight);
			AdjustView(waterHeight);

			Enable3dSkyboxFog();

			render.ViewSetupVisEx(false, new(ref sky3dParams.Origin), out _);

			using MatRenderContextPtr renderContext = new(mainView.materials);

			ITexture? texture = RenderTexture.GetWaterReflectionTexture();

			renderContext.SetHeightClipZ(waterHeight);

			MaterialHeightClipMode clipMode = MaterialHeightClipMode.Disable;
			if ((DrawFlags & DrawFlags.ClipZ) != 0 && mat_clipz.GetBool())
				clipMode = MaterialHeightClipMode.RenderAboveHeight;

			renderContext.SetHeightClipMode(clipMode);

			SetLightmapScaleForWater();

			render.Push3DView(in setup, ClearFlags, texture, GetFrustrum(), null);

			ViewID saveViewID = CurrentViewID();
			Vector3 oldOrigin = CurrentViewOrigin();
			QAngle oldAngles = CurrentViewAngles();
			SetupCurrentView(in setup.Origin, in setup.Angles, ViewID.Sky3D);

			render.BeginUpdateLightmaps();
			BuildWorldRenderLists(true, -1, true);
			BuildRenderableRenderLists(ViewID.Sky3D);
			render.EndUpdateLightmaps();

			engine.Sound_ExtraUpdate();

			DrawWorld(0.0f);

			DrawOpaqueRenderables(RenderDepthMode.Normal);
			DrawTranslucentRenderables(RenderDepthMode.Normal);
			mainView.DisableFog();

			renderContext.Flush();
			saveBits.CopyTo(areaBits);

			PopView();

			SetupCurrentView(in oldOrigin, in oldAngles, saveViewID);
		}
	}

	class RefractionView(ViewRender mainView, AboveWaterView outer) : BaseWorldView(mainView)
	{
		public void Setup() {
			base.Setup(in outer.setup);

			ClearFlags = ClearFlags.ClearColor | ClearFlags.ClearDepth;

			DrawFlags = DrawFlags.RenderRefraction | DrawFlags.ClipZ | DrawFlags.RenderUnderwater | DrawFlags.FudgeUp | DrawFlags.DrawEntities;
		}

		public override void Draw() {
			ViewID saveViewID = CurrentViewID();
			SetupCurrentView(in setup.Origin, in setup.Angles, ViewID.Refraction);

			DrawSetup(outer.WaterHeight, DrawFlags, 0);

			SetFogVolumeState(in outer.FogInfo, true);
			SetClearColorToFogColor();
			DrawExecute(outer.WaterHeight, ViewID.Refraction, 0);

			SetupCurrentView(in setup.Origin, in setup.Angles, saveViewID);

			using MatRenderContextPtr renderContext = new(mainView.materials);
			renderContext.ClearColor4ub(0, 0, 0, 255);
			renderContext.Flush();
		}
	}

	class IntersectionView(ViewRender mainView, AboveWaterView outer) : BaseWorldView(mainView)
	{
		public void Setup() {
			base.Setup(in outer.setup);
			DrawFlags = DrawFlags.RenderUnderwater | DrawFlags.ClipZ | DrawFlags.DrawEntities;
		}

		public override void Draw() {
			DrawSetup(outer.FogInfo.WaterHeight, DrawFlags, 0);

			SetFogVolumeState(in outer.FogInfo, true);
			SetClearColorToFogColor();
			DrawExecute(outer.FogInfo.WaterHeight, ViewID.None, 0);
			using MatRenderContextPtr renderContext = new(mainView.materials);
			renderContext.ClearColor4ub(0, 0, 0, 255);
		}
	}
}

public class UnderWaterView : BaseWaterView
{
	WaterRenderInfo WaterInfo;
	bool DrawSkybox;
	readonly RefractionView Refraction;

	public UnderWaterView(ViewRender mainView) : base(mainView) {
		Refraction = new(mainView, this);
	}

	public void Setup(in ViewSetup view, bool drawSkybox, in VisibleFogVolumeInfo fogInfo, in WaterRenderInfo waterInfo) {
		base.Setup(in view);

		WaterHeight = fogInfo.WaterHeight;

		DrawFlags = DrawFlags.FudgeUp | DrawFlags.RenderUnderwater | DrawFlags.DrawEntities;
		ClearFlags = ClearFlags.ClearDepth;

		DrawFlags |= DrawFlags.ClipZ;
		if (waterInfo.DrawWaterSurface)
			DrawFlags |= DrawFlags.RenderWater;
		if (!waterInfo.Refract && !waterInfo.OpaqueWater)
			DrawFlags |= DrawFlags.RenderAbovewater;

		FogInfo = fogInfo;
		WaterInfo = waterInfo;
		DrawSkybox = drawSkybox;
	}

	public override void ReleaseLists() {
		base.ReleaseLists();
		Refraction.ReleaseLists();
	}

	public override void Draw() {
		using MatRenderContextPtr renderContext = new(mainView.materials);
		if (WaterInfo.Refract) {
			Refraction.Setup();
			mainView.AddViewToScene(Refraction);
		}

		if (!WaterInfo.Refract) {
			SetFogVolumeState(in FogInfo, true);
			renderContext.GetFogColor(out Color fogColor);
			renderContext.ClearColor4ub(fogColor.R, fogColor.G, fogColor.B, 255);
		}

		DrawSetup(WaterHeight, DrawFlags, 0);
		SetFogVolumeState(in FogInfo, false);
		DrawExecute(WaterHeight, CurrentViewID(), 0);
		ClearFlags = 0;

		renderContext.ClearColor4ub(0, 0, 0, 255);
	}

	class RefractionView(ViewRender mainView, UnderWaterView outer) : BaseWorldView(mainView)
	{
		public void Setup() {
			base.Setup(in outer.setup);
			DrawFlags = DrawFlags.ClipZ | DrawFlags.ClipBelow | DrawFlags.RenderAbovewater | DrawFlags.DrawEntities;

			ClearFlags = ClearFlags.ClearDepth;
			if (outer.DrawSkybox) {
				ClearFlags |= ClearFlags.ClearColor;
				DrawFlags |= DrawFlags.DrawSkybox | DrawFlags.ClipSkybox;
			}
		}

		public override void Draw() {
			using MatRenderContextPtr renderContext = new(mainView.materials);

			SetFogVolumeState(in outer.FogInfo, true);
			renderContext.GetFogColor(out Color fogColor);
			renderContext.ClearColor4ub(fogColor.R, fogColor.G, fogColor.B, 255);

			DrawSetup(outer.WaterHeight, DrawFlags, 0);

			EnableWorldFog();
			DrawExecute(outer.WaterHeight, ViewID.Refraction, 0);

			System.Drawing.Rectangle srcRect = new(setup.X, setup.Y, setup.Width, setup.Height);

			ITexture? texture = RenderTexture.GetWaterRefractionTexture();
			renderContext.CopyRenderTargetToTextureEx(texture, 0, ref srcRect, ref System.Runtime.CompilerServices.Unsafe.NullRef<System.Drawing.Rectangle>());
		}
	}
}
public class Rendering3dView : Base3dView
{
	protected DrawFlags DrawFlags;
	protected ClearFlags ClearFlags;

	ClientRenderablesList RenderablesList = null!;
	protected IWorldRenderList? WorldRenderList;
	protected WorldListInfo WorldListInfo;

	public Rendering3dView(ViewRender mainView) : base(mainView) {

	}

	protected void BuildWorldRenderLists(bool drawEntities, int forceViewLeaf = -1, bool useCacheIfEnabled = true, bool shadowDepth = false, Span<float> reflectionWaterHeight = default) {
		Assert(WorldRenderList == null);

		mainView.IncWorldListsNumber();

		WorldRenderList = render.CreateWorldList();
		render.BuildWorldLists(WorldRenderList, ref WorldListInfo, forceViewLeaf, [], shadowDepth, reflectionWaterHeight);

		if (drawEntities)
			UpdateRenderablesOpacity();
	}

	protected void BuildRenderableRenderLists(ViewID viewID) {
		if (viewID != ViewID.ShadowDepthTexture)
			render.BeginUpdateLightmaps();

		mainView.IncRenderablesListsNumber();

		ref WorldListInfo info = ref WorldListInfo;

		if (mainView.ShouldDrawEntities() && viewID != ViewID.ShadowDepthTexture)
			clientLeafSystem.ComputeTranslucentRenderLeaf(info.LeafCount, info.LeafList, info.LeafFogVolume, mainView.BuildRenderablesListsNumber(), (int)viewID);

		SetupRenderablesList(viewID);


		if (viewID != ViewID.ShadowDepthTexture) {
			// todo
			render.EndUpdateLightmaps();
		}
	}

	protected void UpdateRenderablesOpacity() {
		float factor = 1.0f;
		C_BasePlayer? local = C_BasePlayer.GetLocalPlayer();
		if (local != null)
			factor = local.GetFOVDistanceAdjustFactor();

		// if (cl_leveloverview.GetFloat() > 0) // todo
		// 	factor = -1;

		StaticPropMgrGlobals.g_StaticPropMgr.ComputePropOpacity(CurrentViewOrigin(), factor);

		((DetailObjectSystem)DetailObjectSystem.GetDetailObjectSystem()).BuildDetailObjectRenderLists(CurrentViewOrigin());
	}
	public virtual void Setup(in ViewSetup setup) {
		this.setup = setup;
		ReleaseLists();

		RenderablesList = ClientRenderablesList.Shared.Alloc();
	}
	public virtual void ReleaseLists() {
		WorldRenderList?.Release();
		WorldRenderList = null;
		ClientRenderablesList.Shared.Free(RenderablesList);
	}
	public override DrawFlags GetDrawFlags() {
		return DrawFlags;
	}
	public virtual void Draw() {

	}

	private void DrawOpaqueRenderables_DrawBrushModels(RenderGroup group, RenderDepthMode depthMode) {
		int count = RenderablesList.Count(group);
		for (int i = 0; i < count; i++) {
			ref ClientRenderablesList.Entry entry = ref RenderablesList[group, i];
			Assert(!entry.TwoPass);
			DrawOpaqueRenderable(entry.Renderable!, false, depthMode);
		}
	}

	private void DrawOpaqueRenderables_DrawStaticProps(RenderGroup group, RenderDepthMode depthMode) {
		int count = RenderablesList.Count(group);
		if (count == 0)
			return;

		Span<float> one = [1.0f, 1.0f, 1.0f, 1.0f];
		render.SetColorModulation(one);
		render.SetBlend(1.0f);

		const int MAX_STATICS_PER_BATCH = 512;
		InlineArray512<IClientRenderable> statics = new();

		int numScheduled = 0, numAvailable = MAX_STATICS_PER_BATCH;

		for (int i = 0; i < count; i++) {
			ref ClientRenderablesList.Entry entry = ref RenderablesList[group, i];
			if (entry.Renderable == null)
				continue;

			statics[numScheduled++] = entry.Renderable;
			if (--numAvailable > 0)
				continue;

			StaticPropMgrGlobals.g_StaticPropMgr.DrawStaticProps(ref statics, numScheduled, depthMode != RenderDepthMode.Normal, false /*vcollide_wireframe*/);
			numScheduled = 0;
			numAvailable = MAX_STATICS_PER_BATCH;
		}

		if (numScheduled != 0)
			StaticPropMgrGlobals.g_StaticPropMgr.DrawStaticProps(ref statics, numScheduled, depthMode != RenderDepthMode.Normal, false /*vcollide_wireframe*/);
	}

	private void DrawOpaqueRenderables_Range(RenderGroup group, RenderDepthMode depthMode) {
		int count = RenderablesList.Count(group);
		for (int i = 0; i < count; i++) {
			ref ClientRenderablesList.Entry entry = ref RenderablesList[group, i];
			if (entry.Renderable != null)
				DrawOpaqueRenderable(entry.Renderable, entry.TwoPass, depthMode);
		}
	}

	protected void DrawOpaqueRenderables(RenderDepthMode depthMode) {
		if (!r_drawopaquerenderables.GetBool())
			return;

		if (!mainView.ShouldDrawEntities())
			return;

		render.SetBlend(1);

		DrawFlags drawFlags = GetDrawFlags();
#if GMOD_DLL
		if (depthMode == RenderDepthMode.Normal && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PreDrawOpaqueRenderables)) {
			g_Lua!.PushBool(false);
			g_Lua.PushBool((drawFlags & DrawFlags.DrawSkybox) != 0);
			g_Lua.PushBool(SkyboxView.Rendering3DSkybox);
			if (gGM.CallFinish(3))
				return;
		}
#endif

		// todo, this has more

		// First do the brush models
		DrawOpaqueRenderables_DrawBrushModels(RenderGroup.OpaqueBrush, depthMode);

		// Draw static props + opaque entities from the biggest bucket to the smallest
		for (int bucket = 0; bucket < (int)RenderGroup_Config_t.NumOpaqueEntBuckets; bucket++) {
			DrawOpaqueRenderables_Range(RenderGroup.OpaqueEntityHuge + 2 * bucket, depthMode);
			DrawOpaqueRenderables_DrawStaticProps(RenderGroup.OpaqueStaticHuge + 2 * bucket, depthMode);
		}

#if GMOD_DLL
		if (depthMode == RenderDepthMode.Normal && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PostDrawOpaqueRenderables)) {
			g_Lua!.PushBool(false);
			g_Lua.PushBool((drawFlags & DrawFlags.DrawSkybox) != 0);
			g_Lua.PushBool(SkyboxView.Rendering3DSkybox);
			gGM.CallNoReturns(3);
		}
#endif
	}

	private void DrawOpaqueRenderable(IClientRenderable ent, bool twoPass, RenderDepthMode depthMode, StudioFlags defaultFlags = 0) {
		Span<float> color = stackalloc float[3];

		ent.GetColorModulation(color);
		render.SetColorModulation(color);

		StudioFlags flags = defaultFlags | StudioFlags.Render;
		if (twoPass)
			flags |= StudioFlags.TwoPass;

		if (depthMode == RenderDepthMode.Shadow) {
			flags |= StudioFlags.ShadowDepthTexture;
		}
		else if (depthMode == RenderDepthMode.SSAO) {
			flags |= StudioFlags.SSAODepthTexture;
		}

		// todo: entity clip planes

		Assert(view.GetCurrentlyDrawingEntity() == null);
		view.SetCurrentlyDrawingEntity(ent.GetIClientUnknown().GetBaseEntity());
		ent.DrawModel(flags);
		view.SetCurrentlyDrawingEntity(null);
	}

	protected void DrawTranslucentRenderables(RenderDepthMode depthMode) {
		bool shadowDepth = depthMode != RenderDepthMode.Normal;
		IDetailObjectSystem detailObjectSystem = DetailObjectSystem.GetDetailObjectSystem();

		if (!r_drawtranslucentworld.GetBool()) {
			DrawTranslucentRenderablesNoWorld(depthMode);
			return;
		}

		int prevLeaf = WorldListInfo.LeafCount - 1;
		int detailLeafCount = 0;
		Span<LeafIndex_t> detailLeafList = stackalloc LeafIndex_t[WorldListInfo.LeafCount];

		uint engineDrawFlags = (uint)BuildEngineDrawWorldListFlags(DrawFlags & ~DrawFlags.DrawSkybox);

		detailObjectSystem.BeginTranslucentDetailRendering();

		if (mainView.ShouldDrawEntities() && r_drawtranslucentrenderables.GetBool()) {
			// DrawParticleSingletons(); // todo

			int curTranslucentEntity = RenderablesList.Count(RenderGroup.TranslucentEntity) - 1;

			while (curTranslucentEntity >= 0) {
				int thisLeaf = RenderablesList[RenderGroup.TranslucentEntity, curTranslucentEntity].WorldListInfoLeaf;

				DrawTranslucentWorldAndDetailPropsInLeaves(prevLeaf, thisLeaf, engineDrawFlags, ref detailLeafCount, detailLeafList, shadowDepth);

				prevLeaf = thisLeaf - 1;

				int leaf = WorldListInfo.LeafList[thisLeaf];

				bool drawDetailProps = ((ClientLeafSystem)clientLeafSystem).ShouldDrawDetailObjectsInLeaf(leaf, mainView.BuildWorldListsNumber());
				if (drawDetailProps) {
					--detailLeafCount;
					detailObjectSystem.RenderTranslucentDetailObjects(CurrentViewOrigin(), CurrentViewForward(), CurrentViewRight(), CurrentViewUp(), detailLeafCount, detailLeafList);

					for (; curTranslucentEntity >= 0 && RenderablesList[RenderGroup.TranslucentEntity, curTranslucentEntity].WorldListInfoLeaf == thisLeaf; --curTranslucentEntity) {
						ref ClientRenderablesList.Entry entry = ref RenderablesList[RenderGroup.TranslucentEntity, curTranslucentEntity];
						IClientRenderable renderable = entry.Renderable!;

						Vector3 renderOrigin = renderable.GetRenderOrigin();
						detailObjectSystem.RenderTranslucentDetailObjectsInLeaf(CurrentViewOrigin(), CurrentViewForward(), CurrentViewRight(), CurrentViewUp(), leaf, renderOrigin);

						// todo

						DrawTranslucentRenderable(renderable, entry.TwoPass, depthMode);
					}

					detailObjectSystem.RenderTranslucentDetailObjectsInLeaf(CurrentViewOrigin(), CurrentViewForward(), CurrentViewRight(), CurrentViewUp(), leaf, null);
				}
				else {
					detailObjectSystem.RenderTranslucentDetailObjects(CurrentViewOrigin(), CurrentViewForward(), CurrentViewRight(), CurrentViewUp(), detailLeafCount, detailLeafList);

					for (; curTranslucentEntity >= 0 && RenderablesList[RenderGroup.TranslucentEntity, curTranslucentEntity].WorldListInfoLeaf == thisLeaf; --curTranslucentEntity) {
						ref ClientRenderablesList.Entry entry = ref RenderablesList[RenderGroup.TranslucentEntity, curTranslucentEntity];
						IClientRenderable renderable = entry.Renderable!;

						// todo

						DrawTranslucentRenderable(renderable, entry.TwoPass, depthMode);
					}
				}

				detailLeafCount = 0;
			}
		}

		DrawTranslucentWorldAndDetailPropsInLeaves(prevLeaf, 0, engineDrawFlags, ref detailLeafCount, detailLeafList, shadowDepth);

		detailObjectSystem.RenderTranslucentDetailObjects(CurrentViewOrigin(), CurrentViewForward(), CurrentViewRight(), CurrentViewUp(), detailLeafCount, detailLeafList);

		render.SetBlend(1);
	}

	protected void DrawTranslucentRenderablesNoWorld(RenderDepthMode depthMode) {
		if (!mainView.ShouldDrawEntities() || !r_drawtranslucentrenderables.GetBool())
			return;

		// DrawParticleSingletons(); // todo

		int curTranslucentEntity = RenderablesList.Count(RenderGroup.TranslucentEntity) - 1;

		while (curTranslucentEntity >= 0) {
			ref ClientRenderablesList.Entry entry = ref RenderablesList[RenderGroup.TranslucentEntity, curTranslucentEntity];
			IClientRenderable renderable = entry.Renderable!;

			// todo

			DrawTranslucentRenderable(renderable, entry.TwoPass, depthMode);
			--curTranslucentEntity;
		}
	}

	protected void DrawTranslucentWorldInLeaves(bool shadowDepth) {
		for (int curLeafIndex = WorldListInfo.LeafCount - 1; curLeafIndex >= 0; curLeafIndex--) {
			int actualLeafIndex = curLeafIndex;
			if (render.LeafContainsTranslucentSurfaces(WorldRenderList, actualLeafIndex, (uint)DrawFlags))
				render.DrawTranslucentSurfaces(WorldRenderList, actualLeafIndex, (uint)DrawFlags, shadowDepth);
		}
	}

	protected void DrawTranslucentWorldAndDetailPropsInLeaves(int curLeafIndex, int finalLeafIndex, uint engineDrawFlags, ref int detailLeafCount, Span<LeafIndex_t> detailLeafList, bool shadowDepth) {
		IDetailObjectSystem detailObjectSystem = DetailObjectSystem.GetDetailObjectSystem();
		for (; curLeafIndex >= finalLeafIndex; curLeafIndex--) {
			int actualLeafIndex = curLeafIndex;
			if (render.LeafContainsTranslucentSurfaces(WorldRenderList, actualLeafIndex, engineDrawFlags)) {
				detailObjectSystem.RenderTranslucentDetailObjects(CurrentViewOrigin(), CurrentViewForward(), CurrentViewRight(), CurrentViewUp(), detailLeafCount, detailLeafList);
				detailLeafCount = 0;

				render.DrawTranslucentSurfaces(WorldRenderList, actualLeafIndex, engineDrawFlags, shadowDepth);
			}

			if (((ClientLeafSystem)clientLeafSystem).ShouldDrawDetailObjectsInLeaf(WorldListInfo.LeafList[curLeafIndex], mainView.BuildWorldListsNumber())) {
				detailLeafList[detailLeafCount] = WorldListInfo.LeafList[curLeafIndex];
				++detailLeafCount;
			}
		}
	}

	private void DrawTranslucentRenderable(IClientRenderable ent, bool twoPass, RenderDepthMode depthMode) {
		float blend = ent.GetFxBlend() / 255.0f;
		if (blend <= 0.0f)
			return;

		render.SetBlend(blend);

		Span<float> color = stackalloc float[3];
		ent.GetColorModulation(color);
		render.SetColorModulation(color);

		StudioFlags flags = StudioFlags.Render | StudioFlags.Transparency;
		if (twoPass)
			flags |= StudioFlags.TwoPass;

		if (depthMode == RenderDepthMode.Shadow) {
			flags |= StudioFlags.ShadowDepthTexture;
		}
		else if (depthMode == RenderDepthMode.SSAO) {
			flags |= StudioFlags.SSAODepthTexture;
		}

		Assert(view.GetCurrentlyDrawingEntity() == null);
		view.SetCurrentlyDrawingEntity(ent.GetIClientUnknown().GetBaseEntity());
		ent.DrawModel(flags);
		view.SetCurrentlyDrawingEntity(null);

		render.SetBlend(1);
	}

	protected static void SetClearColorToFogColor() {
		using MatRenderContextPtr renderContext = new(materials);

		renderContext.GetFogColor(out Color fogColor);
		if (Singleton<IMaterialSystemHardwareConfig>().GetHDRType() == HDRType.Integer) {
			float scale = MathLib.LinearToGammaFullRange(renderContext.GetToneMappingScaleLinear().X);
			fogColor.R = (byte)(fogColor.R * scale);
			fogColor.G = (byte)(fogColor.G * scale);
			fogColor.B = (byte)(fogColor.B * scale);
		}
		renderContext.ClearColor4ub(fogColor.R, fogColor.G, fogColor.B, 255);
	}

	static void ParseFogColor(string fogColorString, Span<float> color) {
		string[] tokens = fogColorString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < 3 && i < tokens.Length; i++) {
			if (!float.TryParse(tokens[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
				break;
			color[i] = value;
		}
	}

	static void CheckAndTransitionColor(float percent, Span<float> color, ReadOnlySpan<float> lerpToColor) {
		if (lerpToColor[0] != color[0] || lerpToColor[1] != color[1] || lerpToColor[2] != color[2]) {
			color[0] = MathLib.Lerp(percent, color[0], lerpToColor[0]);
			color[1] = MathLib.Lerp(percent, color[1], lerpToColor[1]);
			color[2] = MathLib.Lerp(percent, color[2], lerpToColor[2]);
		}
		else {
			color[0] = lerpToColor[0];
			color[1] = lerpToColor[1];
			color[2] = lerpToColor[2];
		}
	}

	static void GetFogColorTransition(ref FogParams fogParams, Span<float> colorPrimary, Span<float> colorSecondary) {
		if (fogParams.LerpTime >= gpGlobals.CurTime) {
			float percent = (float)(1.0 - ((fogParams.LerpTime - gpGlobals.CurTime) / fogParams.Duration));

			ReadOnlySpan<float> primaryColorLerp = [fogParams.ColorPrimaryLerpTo.R, fogParams.ColorPrimaryLerpTo.G, fogParams.ColorPrimaryLerpTo.B];
			ReadOnlySpan<float> secondaryColorLerp = [fogParams.ColorSecondaryLerpTo.R, fogParams.ColorSecondaryLerpTo.G, fogParams.ColorSecondaryLerpTo.B];

			CheckAndTransitionColor(percent, colorPrimary, primaryColorLerp);
			CheckAndTransitionColor(percent, colorSecondary, secondaryColorLerp);
		}
	}

	static void GetFogColor(C_BasePlayer? pbp, Span<float> color) {
		if (pbp == null)
			return;

		ref FogParams fogParams = ref pbp.GetFogParams();

		if (fog_override.GetInt() != 0)
			ParseFogColor(fog_color.GetString(), color);
		else {
			Span<float> primaryColor = [fogParams.ColorPrimary.R, fogParams.ColorPrimary.G, fogParams.ColorPrimary.B];
			Span<float> secondaryColor = [fogParams.ColorSecondary.R, fogParams.ColorSecondary.G, fogParams.ColorSecondary.B];

			GetFogColorTransition(ref fogParams, primaryColor, secondaryColor);

			if (fogParams.Blend) {
				pbp.EyeVectors(out Vector3 forward);

				MathLib.VectorNormalize(ref fogParams.DirPrimary);

				float blendFactor = 0.5f * Vector3.Dot(forward, fogParams.DirPrimary) + 0.5f;

				color[0] = primaryColor[0] * blendFactor + secondaryColor[0] * (1 - blendFactor);
				color[1] = primaryColor[1] * blendFactor + secondaryColor[1] * (1 - blendFactor);
				color[2] = primaryColor[2] * blendFactor + secondaryColor[2] * (1 - blendFactor);
			}
			else {
				color[0] = primaryColor[0];
				color[1] = primaryColor[1];
				color[2] = primaryColor[2];
			}
		}

		color[0] *= 1.0f / 255.0f;
		color[1] *= 1.0f / 255.0f;
		color[2] *= 1.0f / 255.0f;
	}

	static float GetFogStart(C_BasePlayer? pbp) {
		if (pbp == null)
			return 0.0f;

		ref FogParams fogParams = ref pbp.GetFogParams();

		if (fog_override.GetInt() != 0) {
			if (fog_start.GetFloat() == -1.0f)
				return fogParams.Start;
			else
				return fog_start.GetFloat();
		}
		else {
			if (fogParams.LerpTime > gpGlobals.CurTime) {
				if (fogParams.Start != fogParams.StartLerpTo) {
					if (fogParams.LerpTime > gpGlobals.CurTime) {
						float percent = (float)(1.0 - ((fogParams.LerpTime - gpGlobals.CurTime) / fogParams.Duration));

						return MathLib.Lerp(percent, fogParams.Start, fogParams.StartLerpTo);
					}
					else {
						if (fogParams.Start != fogParams.StartLerpTo)
							fogParams.Start = fogParams.StartLerpTo;
					}
				}
			}

			return fogParams.Start;
		}
	}

	static float GetFogEnd(C_BasePlayer? pbp) {
		if (pbp == null)
			return 0.0f;

		ref FogParams fogParams = ref pbp.GetFogParams();

		if (fog_override.GetInt() != 0) {
			if (fog_end.GetFloat() == -1.0f)
				return fogParams.End;
			else
				return fog_end.GetFloat();
		}
		else {
			if (fogParams.LerpTime > gpGlobals.CurTime) {
				if (fogParams.End != fogParams.EndLerpTo) {
					if (fogParams.LerpTime > gpGlobals.CurTime) {
						float percent = (float)(1.0 - ((fogParams.LerpTime - gpGlobals.CurTime) / fogParams.Duration));

						return MathLib.Lerp(percent, fogParams.End, fogParams.EndLerpTo);
					}
					else {
						if (fogParams.End != fogParams.EndLerpTo)
							fogParams.End = fogParams.EndLerpTo;
					}
				}
			}

			return fogParams.End;
		}
	}

	static bool GetFogEnable(C_BasePlayer? pbp) {
		if (fog_override.GetInt() != 0)
			return fog_enable.GetInt() != 0;
		else {
			if (pbp != null)
				return pbp.GetFogParams().Enable != false;

			return false;
		}
	}

	static float GetFogMaxDensity(C_BasePlayer? pbp) {
		if (pbp == null)
			return 1.0f;

		ref FogParams fogParams = ref pbp.GetFogParams();

		if (fog_override.GetInt() != 0) {
			if (fog_maxdensity.GetFloat() == -1.0f)
				return fogParams.MaxDensity;
			else
				return fog_maxdensity.GetFloat();
		}
		else
			return fogParams.MaxDensity;
	}

	static bool GetFogRadial(C_BasePlayer? pbp) {
		if (fog_override.GetInt() != 0) {
			if (fog_radial.GetInt() != -1)
				return fog_radial.GetBool();
		}

		if (pbp == null)
			return false;

		return pbp.GetFogParams().Radial;
	}

	protected static void GetSkyboxFogColor(Span<float> color) {
		C_BasePlayer? pbp = C_BasePlayer.GetLocalPlayer();
		if (pbp == null)
			return;

		PlayerLocalData local = pbp.Local;

		if (fog_override.GetInt() != 0)
			ParseFogColor(fog_colorskybox.GetString(), color);
		else {
			if (local.Skybox3D.Fog.Blend) {
				pbp.EyeVectors(out Vector3 forward);

				MathLib.VectorNormalize(ref local.Skybox3D.Fog.DirPrimary);

				float blendFactor = 0.5f * Vector3.Dot(forward, local.Skybox3D.Fog.DirPrimary) + 0.5f;

				color[0] = local.Skybox3D.Fog.ColorPrimary.R * blendFactor + local.Skybox3D.Fog.ColorSecondary.R * (1 - blendFactor);
				color[1] = local.Skybox3D.Fog.ColorPrimary.G * blendFactor + local.Skybox3D.Fog.ColorSecondary.G * (1 - blendFactor);
				color[2] = local.Skybox3D.Fog.ColorPrimary.B * blendFactor + local.Skybox3D.Fog.ColorSecondary.B * (1 - blendFactor);
			}
			else {
				color[0] = local.Skybox3D.Fog.ColorPrimary.R;
				color[1] = local.Skybox3D.Fog.ColorPrimary.G;
				color[2] = local.Skybox3D.Fog.ColorPrimary.B;
			}
		}

		color[0] *= 1.0f / 255.0f;
		color[1] *= 1.0f / 255.0f;
		color[2] *= 1.0f / 255.0f;
	}

	protected static float GetSkyboxFogStart() {
		C_BasePlayer? pbp = C_BasePlayer.GetLocalPlayer();
		if (pbp == null)
			return 0.0f;

		PlayerLocalData local = pbp.Local;

		if (fog_override.GetInt() != 0) {
			if (fog_startskybox.GetFloat() == -1.0f)
				return local.Skybox3D.Fog.Start;
			else
				return fog_startskybox.GetFloat();
		}
		else
			return local.Skybox3D.Fog.Start;
	}

	protected static float GetSkyboxFogEnd() {
		C_BasePlayer? pbp = C_BasePlayer.GetLocalPlayer();
		if (pbp == null)
			return 0.0f;

		PlayerLocalData local = pbp.Local;

		if (fog_override.GetInt() != 0) {
			if (fog_endskybox.GetFloat() == -1.0f)
				return local.Skybox3D.Fog.End;
			else
				return fog_endskybox.GetFloat();
		}
		else
			return local.Skybox3D.Fog.End;
	}

	protected static float GetSkyboxFogMaxDensity() {
		C_BasePlayer? pbp = C_BasePlayer.GetLocalPlayer();
		if (pbp == null)
			return 1.0f;

		PlayerLocalData local = pbp.Local;

		if (fog_override.GetInt() != 0) {
			if (fog_maxdensityskybox.GetFloat() == -1.0f)
				return local.Skybox3D.Fog.MaxDensity;
			else
				return fog_maxdensityskybox.GetFloat();
		}
		else
			return local.Skybox3D.Fog.MaxDensity;
	}

	protected static bool GetSkyboxFogRadial() {
		C_BasePlayer? pbp = C_BasePlayer.GetLocalPlayer();
		if (pbp == null)
			return false;

		PlayerLocalData local = pbp.Local;

		if (fog_override.GetInt() != 0) {
			if (fog_radialskybox.GetInt() != -1)
				return fog_radialskybox.GetBool();
		}

		return local.Skybox3D.Fog.Radial;
	}

	protected void EnableWorldFog() {
		using MatRenderContextPtr renderContext = new(mainView.materials);

		C_BasePlayer? pbp = C_BasePlayer.GetLocalPlayer();

		if (gGM != null && gGM.Call((int)LUA_POOLEDSTRING.SetupWorldFog))
			return;

		if (GetFogEnable(pbp)) {
			Span<float> fogColor = stackalloc float[3];
			GetFogColor(pbp, fogColor);

			renderContext.FogMode(MaterialFogMode.Linear);
			renderContext.FogColor3fv(fogColor);
			renderContext.FogStart(GetFogStart(pbp));
			renderContext.FogEnd(GetFogEnd(pbp));
			renderContext.FogMaxDensity(GetFogMaxDensity(pbp));
			renderContext.FogRadial(GetFogRadial(pbp));
		}
		else
			renderContext.FogMode(MaterialFogMode.None);
	}

	protected void SetFogVolumeState(in VisibleFogVolumeInfo fogInfo, bool useHeightFog) {
		render.SetFogVolumeState(fogInfo.VisibleFogVolume, useHeightFog);
	}

	protected static SafeFieldPointer<PlayerLocalData, Sky3DParams> PreRender3dSkyboxWorld(ref SkyboxVisibility skyboxVisible) {
		if ((skyboxVisible != SkyboxVisibility.Skybox3D) && r_3dsky.GetInt() != 2)
			return SafeFieldPointer<PlayerLocalData, Sky3DParams>.Null;

		if (r_3dsky.GetInt() == 0)
			return SafeFieldPointer<PlayerLocalData, Sky3DParams>.Null;

		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null)
			return SafeFieldPointer<PlayerLocalData, Sky3DParams>.Null;

		PlayerLocalData local = player.Local;
		if (local.Skybox3D.Area == 255)
			return SafeFieldPointer<PlayerLocalData, Sky3DParams>.Null;

		return new(local, GetSkybox3DRef);
	}

	static ref Sky3DParams GetSkybox3DRef(PlayerLocalData local) => ref local.Skybox3D;

	protected static bool GetSkyboxFogEnable() {
		C_BasePlayer? pbp = C_BasePlayer.GetLocalPlayer();
		if (pbp == null)
			return false;

		PlayerLocalData local = pbp.Local;

		if (fog_override.GetInt() != 0)
			return fog_enableskybox.GetInt() != 0;
		else
			return local.Skybox3D.Fog.Enable;
	}

	protected void Enable3dSkyboxFog() {
		C_BasePlayer? pbp = C_BasePlayer.GetLocalPlayer();
		if (pbp == null)
			return;

		PlayerLocalData local = pbp.Local;

		using MatRenderContextPtr renderContext = new(mainView.materials);

		float scale = 1.0f;
		if (local.Skybox3D.Scale > 0.0f)
			scale = 1.0f / local.Skybox3D.Scale;

		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.SetupSkyboxFog)) {
			g_Lua!.PushNumber(scale);
			if (gGM.CallFinish(1))
				return;
		}

		if (GetSkyboxFogEnable()) {
			Span<float> fogColor = stackalloc float[3];
			GetSkyboxFogColor(fogColor);

			renderContext.FogMode(MaterialFogMode.Linear);
			renderContext.FogColor3fv(fogColor);
			renderContext.FogStart(GetSkyboxFogStart() * scale);
			renderContext.FogEnd(GetSkyboxFogEnd() * scale);
			renderContext.FogMaxDensity(GetSkyboxFogMaxDensity());
			renderContext.FogRadial(GetSkyboxFogRadial());
		}
		else
			renderContext.FogMode(MaterialFogMode.None);
	}
	protected void SetupRenderablesList(ViewID viewID) {
		// Clear the list.
		int i;
		for (i = 0; i < (int)RenderGroup.Count; i++)
			RenderablesList.RenderGroupCounts[i] = 0;

		// Now collate the entities in the leaves.
		if (mainView.ShouldDrawEntities()) {
			SetupRenderInfo setupInfo = default;
			setupInfo.WorldListInfo = WorldListInfo;
			setupInfo.RenderFrame = mainView.BuildRenderablesListsNumber();
			setupInfo.DetailBuildFrame = mainView.BuildWorldListsNumber();
			setupInfo.RenderList = RenderablesList;
			setupInfo.DrawDetailObjects = clientMode.ShouldDrawDetailObjects() && r_DrawDetailProps.GetBool();
			setupInfo.DrawTranslucentObjects = (viewID != ViewID.ShadowDepthTexture);

			setupInfo.RenderOrigin = setup.Origin;
			setupInfo.RenderForward = CurrentViewForward();

			float fMaxDist = cl_maxrenderable_dist.GetFloat();

			setupInfo.RenderDistSq = (viewID == ViewID.ShadowDepthTexture) ? Math.Min(setup.ZFar, fMaxDist) : fMaxDist;
			setupInfo.RenderDistSq *= setupInfo.RenderDistSq;

			clientLeafSystem.BuildRenderablesList(setupInfo);
		}
	}
	protected void DrawWorld(float waterZAdjust) {
		if (!r_drawopaqueworld.GetBool())
			return;

		DrawWorldListFlags engineFlags = BuildEngineDrawWorldListFlags(DrawFlags);
		mainView.render.DrawWorldLists(WorldRenderList, (uint)engineFlags, waterZAdjust);
	}

	private DrawWorldListFlags BuildEngineDrawWorldListFlags(DrawFlags drawFlags) {
		DrawWorldListFlags engineFlags = 0;

		if ((drawFlags & DrawFlags.DrawSkybox) != 0)
			engineFlags |= DrawWorldListFlags.Skybox;

		if ((drawFlags & DrawFlags.RenderAbovewater) != 0) {
			engineFlags |= DrawWorldListFlags.StrictlyAboveWater;
			engineFlags |= DrawWorldListFlags.IntersectsWater;
		}

		if ((drawFlags & DrawFlags.RenderUnderwater) != 0) {
			engineFlags |= DrawWorldListFlags.StrictlyUnderWater;
			engineFlags |= DrawWorldListFlags.IntersectsWater;
		}

		if ((drawFlags & DrawFlags.RenderWater) != 0)
			engineFlags |= DrawWorldListFlags.WaterSurface;

		if ((drawFlags & DrawFlags.ClipSkybox) != 0)
			engineFlags |= DrawWorldListFlags.ClipSkybox;

		if ((drawFlags & DrawFlags.ShadowDepthMap) != 0)
			engineFlags |= DrawWorldListFlags.ShadowDepth;

		if ((drawFlags & DrawFlags.RenderRefraction) != 0)
			engineFlags |= DrawWorldListFlags.Refraction;

		if ((drawFlags & DrawFlags.RenderReflection) != 0)
			engineFlags |= DrawWorldListFlags.Reflection;

		if ((drawFlags & DrawFlags.SSAODepthPass) != 0) {
			engineFlags |= DrawWorldListFlags.SSAO | DrawWorldListFlags.StrictlyUnderWater | DrawWorldListFlags.IntersectsWater | DrawWorldListFlags.StrictlyAboveWater;
			engineFlags &= ~(DrawWorldListFlags.WaterSurface | DrawWorldListFlags.Refraction | DrawWorldListFlags.Reflection);
		}

		return engineFlags;
	}
}

public class SkyboxView : Rendering3dView
{
	public static bool Rendering3DSkybox;
	SafeFieldPointer<PlayerLocalData, Sky3DParams> Sky3dParams = new();
	public SkyboxView(ViewRender mainView) : base(mainView) {

	}

	public bool Setup(in ViewSetup viewRender, ref ClearFlags clearFlags, ref SkyboxVisibility skyboxVisible) {
		base.Setup(in viewRender);

		skyboxVisible = ComputeSkyboxVisibility();
		Sky3dParams = PreRender3dSkyboxWorld(ref skyboxVisible);
		if (Sky3dParams.IsNull)
			return false;

		ClearFlags = clearFlags;
		clearFlags &= ~(ClearFlags.ClearColor | ClearFlags.ClearDepth | ClearFlags.ClearStencil | ClearFlags.ClearFullTarget);
		clearFlags |= ClearFlags.ClearDepth;

		DrawFlags = DrawFlags.RenderUnderwater | DrawFlags.RenderAbovewater | DrawFlags.RenderWater;
		if (r_skybox.GetBool())
			DrawFlags |= DrawFlags.DrawSkybox;

		return true;
	}

	public override void Draw() {
		ITexture? rtColor = null;
		ITexture? rtDepth = null;
		if (setup.StereoEye != StereoEye.Mono)
			throw new Exception("No support for multi-stereo-eye yet");
		DrawInternal(ViewID.Sky3D, true, rtColor, rtDepth);
	}

	private void DrawInternal(ViewID skyBoxViewID, bool invokePreAndPostRender, ITexture? rtColor, ITexture? rtDepth) {
		ref Sky3DParams sky3dParams = ref Sky3dParams.Get();

		Span<byte> areaBits = render.GetAreaBits();
		Span<byte> saveBits = stackalloc byte[Constants.MAX_AREA_STATE_BYTES];
		areaBits.CopyTo(saveBits);
		areaBits.Clear();

		// set the sky area bit
		areaBits[sky3dParams.Area >> 3] |= (byte)(1 << (sky3dParams.Area & 7));

		setup.ZNear = 2;
		setup.ZFar = WorldSize.MAX_TRACE_LENGTH;
		if (sky3dParams.Scale > 0)
			setup.Origin *= 1f / sky3dParams.Scale;
		setup.Origin += sky3dParams.Origin;
		Rendering3DSkybox = true;

		Enable3dSkyboxFog();

		render.ViewSetupVisEx(false, new(ref sky3dParams.Origin), out _);
		render.Push3DView(in setup, ClearFlags, rtColor, GetFrustrum(), rtDepth);

		SetupCurrentView(in setup.Origin, in setup.Angles, skyBoxViewID);

		if (invokePreAndPostRender)
			IGameSystem.PreRenderAllSystems();

		// render.BeginUpdateLightmaps();
		BuildWorldRenderLists(true, -1, true);
		BuildRenderableRenderLists(skyBoxViewID);
		// render.EndUpdateLightmaps();

		g_ClientShadowMgr.ComputeShadowTextures(in setup, WorldListInfo.LeafCount, WorldListInfo.LeafList);

		DrawWorld(0);

		// Iterate over all leaves and render objects in those leaves
		DrawOpaqueRenderables(RenderDepthMode.Normal);

		// Iterate over all leaves and render objects in those leaves
		DrawTranslucentRenderables(RenderDepthMode.Normal);
		// todo: DrawNoZBufferTranslucentRenderables()

		mainView.DisableFog();

		// restore old area bits
		saveBits.CopyTo(areaBits);

		if (invokePreAndPostRender) {
			IGameSystem.PostRenderAllSystems();
			// FinishCurrentView();
		}

		render.PopView(GetFrustrum());

#if GMOD_DLL
		if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PostDrawSkyBox) && gGM.CallFinish(0))
			return;
#endif
		Rendering3DSkybox = false;
	}

	private SkyboxVisibility ComputeSkyboxVisibility() {
		return engine.IsSkyboxVisibleFromPoint(setup.Origin);
	}
}
