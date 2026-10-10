global using static Game.Client.ViewConVars;
global using static Game.Client.ViewAccessors;

#if GMOD_DLL
using Game.Client.GarrysMod;
#endif
using Game.Shared;

using Microsoft.Extensions.DependencyInjection;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.GUI;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Engine;

using System.Numerics;
using Source.Common.Input;

namespace Game.Client;

public static class ViewConVars
{
	public static readonly ConVar v_viewmodel_fov = new("viewmodel_fov", "54", FCvar.Cheat, "Sets the field-of-view for the viewmodel.", 0.1, 179.9);
}

public static class ViewAccessors
{
	public static ref readonly Vector3 MainViewOrigin() => ref ViewRender.g_VecRenderOrigin;
	public static ref readonly QAngle MainViewAngles() => ref ViewRender.g_VecRenderAngles;
	public static ref readonly Vector3 MainViewForward() => ref ViewRender.g_VecVForward;
	public static ref readonly Vector3 MainViewRight() => ref ViewRender.g_VecVRight;
	public static ref readonly Vector3 MainViewUp() => ref ViewRender.g_VecVUp;
	public static ref readonly Matrix4x4 MainWorldToViewMatrix() => ref ViewRender.g_MatCamInverse;
	public static ref readonly Vector3 PrevMainViewOrigin() => ref ViewRender.g_VecPrevRenderOrigin;
	public static ref readonly QAngle PrevMainViewAngles() => ref ViewRender.g_VecPrevRenderAngles;


	public static ref readonly Vector3 CurrentViewOrigin() => ref ViewRender.g_VecCurrentRenderOrigin;
	public static ref readonly QAngle CurrentViewAngles() => ref ViewRender.g_VecCurrentRenderAngles;
	public static ref readonly Vector3 CurrentViewForward() => ref ViewRender.g_VecCurrentVForward;
	public static ref readonly Vector3 CurrentViewRight() => ref ViewRender.g_VecCurrentVRight;
	public static ref readonly Vector3 CurrentViewUp() => ref ViewRender.g_VecCurrentVUp;
	public static ref readonly Matrix4x4 CurrentWorldToViewMatrix() => ref ViewRender.g_MatCurrentCamInverse;
	public static void AllowCurrentViewAccess(bool allow) => ViewRender.s_bCanAccessCurrentView = allow;
	public static bool IsCurrentViewAccessAllowed() => ViewRender.s_bCanAccessCurrentView;
	public static ViewID CurrentViewID() {
		Assert(ViewRender.g_CurrentViewID != ViewID.Illegal);
		return ViewRender.g_CurrentViewID;
	}
	/// <summary>
	/// for easy externing
	/// </summary>
	public static bool DrawingShadowDepthView() => CurrentViewID() == ViewID.ShadowDepthTexture;
	/// <summary>
	/// for easy externing
	/// </summary>
	public static bool DrawingMainView() => CurrentViewID() == ViewID.Main;
	public static void ComputeCameraVariables(in Vector3 origin, in QAngle angles, out Vector3 forward, out Vector3 right, out Vector3 up, ref Matrix4x4 currentCamInverse) {
		angles.Vectors(out forward, out right, out up);

		for (int i = 0; i < 3; ++i) {
			currentCamInverse[0, i] = right[i];
			currentCamInverse[1, i] = up[i];
			currentCamInverse[2, i] = -forward[i];
			currentCamInverse[3, i] = 0.0f;
		}

		currentCamInverse[0, 3] = -Vector3.Dot(right, origin);
		currentCamInverse[1, 3] = -Vector3.Dot(up, origin);
		currentCamInverse[2, 3] = Vector3.Dot(forward, origin);
		currentCamInverse[3, 3] = 1.0F;
	}
	public static void SetupCurrentView(in Vector3 origin, in QAngle angles, ViewID viewID) {
		ViewRender.g_VecCurrentRenderOrigin = origin;
		ViewRender.g_VecCurrentRenderAngles = angles;

		// Compute the world->main camera transform
		ComputeCameraVariables(origin, angles,
			out ViewRender.g_VecCurrentVForward, out ViewRender.g_VecCurrentVRight, out ViewRender.g_VecCurrentVUp, ref ViewRender.g_MatCurrentCamInverse);

		ViewRender.g_CurrentViewID = viewID;
		ViewRender.s_bCanAccessCurrentView = true;

		// Cache off fade distances
		view.GetScreenFadeDistances(out float flScreenFadeMinSize, out float flScreenFadeMaxSize);
		// modelinfo.SetViewScreenFadeRange(flScreenFadeMinSize, flScreenFadeMaxSize);
	}
}

public class ViewRender : IViewRender
{
	public const int VIEW_NEARZ = 3;

	static ConVar r_nearz = new(VIEW_NEARZ.ToString(), FCvar.Cheat, "Override the near clipping plane.");
	static ConVar r_farz = new("-1", FCvar.Cheat, "Override the far clipping plane. -1 means to use the value in env_fog_controller.");

	public ViewSetup CurrentView;
	bool ForceNoVis;
	DrawFlags BaseDrawFlags;
	Frustum Frustum;
	Base3dView? ActiveRenderer;
	readonly SimpleRenderExecutor SimpleExecutor;

	internal readonly IMaterialSystem materials;
	readonly IServiceProvider services;
	readonly Render engineRenderer;
	public ViewRender(IMaterialSystem materials, IServiceProvider services, Render engineRenderer) {
		this.materials = materials;
		this.services = services;
		this.engineRenderer = engineRenderer;
		SimpleExecutor = new(this);
	}

	internal IRenderView render;

	public void DisableVis() {
		throw new NotImplementedException();
	}

	public void DriftPitch() {
		throw new NotImplementedException();
	}

	public void FreezeFrame(float flFreezeTime) {
		throw new NotImplementedException();
	}

	public DrawFlags GetDrawFlags() {
		throw new NotImplementedException();
	}

	public Frustum GetFrustum() => ActiveRenderer?.GetFrustrum() ?? Frustum;

	public ref ViewSetup GetPlayerViewSetup() => ref GetView(StereoEye.Mono);
	public void GetScreenFadeDistances(out float min, out float max) {
		min = max = 0;
	}

	public IMaterial? GetScreenOverlayMaterial() {
		throw new NotImplementedException();
	}

	public ref ViewSetup GetViewSetup() {
		return ref CurrentView;
	}

	public void GetWaterLODParams(ref float cheapWaterStartDistance, ref float cheapWaterEndDistance) {
		throw new NotImplementedException();
	}

	public float GetZFar() {
		float farZ;

		if (r_farz.GetFloat() < 1) {
			farZ = 32000; // todo
		}
		else
			farZ = r_farz.GetFloat();

		return farZ;
	}

	public float GetZNear() {
		return r_nearz.GetFloat();
	}

	readonly MaterialReference TranslucentSingleColor = new();
	readonly MaterialReference ModulateSingleColor = new();

	public void Init() {
		render = services.GetRequiredService<IRenderView>();

		TranslucentSingleColor.Init("debug/debugtranslucentsinglecolor", MaterialDefines.TEXTURE_GROUP_OTHER);
		ModulateSingleColor.Init("engine/modulatesinglecolor", MaterialDefines.TEXTURE_GROUP_OTHER);
	}

	public void LevelInit() {
		throw new NotImplementedException();
	}

	public void LevelShutdown() {
		// ScreenSpaceEffects.ShutdownScreenSpaceEffects();
	}

	public void OnRenderStart() {
		SetUpViews();

		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player != null && player.DefaultFOV != 0) {
			default_fov.SetValue(player.DefaultFOV);
		}
	}

	private void SetUpViews() {
		float farZ = GetZFar();
		ref ViewSetup viewEye = ref View;

		viewEye.ZFar = farZ;
		viewEye.ZFarViewmodel = farZ;

		viewEye.ZNear = GetZNear();
		viewEye.ZNearViewmodel = 1;
		viewEye.FOV = default_fov.GetFloat(); // todo

		viewEye.Ortho = false;
		viewEye.ViewToProjectionOverride = false;
		viewEye.StereoEye = StereoEye.Mono;

		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		bool calcViewModelView = false;
		Vector3 viewModelOrigin = default;
		QAngle viewModelAngles = default;

		if (player != null) {
			player.CalcView(ref viewEye.Origin, ref viewEye.Angles, ref viewEye.ZNear, ref viewEye.ZFar, ref viewEye.FOV);

			int viewentity = render.GetViewEntity();

			if (player.Index != viewentity) {
				C_BaseEntity? ve = cl_entitylist.GetEnt(viewentity);
				if (ve != null) {
					viewEye.Origin = ve.GetAbsOrigin();
					viewEye.Angles = ve.GetAbsAngles();
				}
			}

			calcViewModelView = true;
			viewModelOrigin = viewEye.Origin;
			viewModelAngles = viewEye.Angles;
		}

		clientMode.OverrideView(ref viewEye);

		float fDefaultFov = default_fov.GetFloat();
		float flFOVOffset = fDefaultFov - viewEye.FOV;

		viewEye.FOVViewmodel = MathF.Abs(clientMode.GetViewModelFOV() - flFOVOffset);

		if (calcViewModelView) {
			Assert(player != null);
			player.CalcViewModelView(in viewModelOrigin, in viewModelAngles);
		}

		ViewRender.g_VecPrevRenderOrigin = ViewRender.g_VecRenderOrigin;
		ViewRender.g_VecPrevRenderAngles = ViewRender.g_VecRenderAngles;
		ViewRender.g_VecRenderOrigin = viewEye.Origin;
		ViewRender.g_VecRenderAngles = viewEye.Angles;

		ComputeCameraVariables(ViewRender.g_VecRenderOrigin, ViewRender.g_VecRenderAngles,
			out ViewRender.g_VecVForward, out ViewRender.g_VecVRight, out ViewRender.g_VecVUp, ref ViewRender.g_MatCamInverse);

		AudioState audioState = default;
		audioState.Origin = viewEye.Origin;
		audioState.Angles = viewEye.Angles;
		audioState.IsUnderwater = player != null && player.AudioStateIsUnderwater(viewEye.Origin);

		viewEye.Origin = audioState.Origin;
		viewEye.Angles = audioState.Angles;

		engine.SetAudioState(in audioState);

		ViewRender.g_FOV = ScaleFOVByWidthRatio(viewEye.FOV, viewEye.AspectRatio / (4.0f / 3.0f));

		C_BaseAnimating.PopAllowBoneAccess(new("OnRenderStart->ViewRender.SetUpView")); // pops the (true, false) bone access set in OnRenderStart
		C_BaseAnimating.PushAllowBoneAccess(true, true, new("ViewRender.SetUpView->OnRenderEnd")); // pop is in OnRenderEnd()
	}

	public void QueueOverlayRenderView(in ViewSetup view, ClearFlags clearFlags, DrawFlags whatToDraw) {
		throw new NotImplementedException();
	}

	ViewSetup View = new();
	ViewSetup View2D = new();

	StereoEye GetFirstEye() => StereoEye.Mono;
	StereoEye GetLastEye() => StereoEye.Mono;
	ref ViewSetup GetView(StereoEye eye) {
		switch (eye) {
			case StereoEye.Mono:
				return ref View;
			default:
				Assert(false);
				return ref View;
		}
	}

	public static float ScaleFOVByWidthRatio(float fovDegrees, float ratio) {
		float halfAngleRadians = fovDegrees * (0.5f * MathF.PI / 180.0f);
		float t = MathF.Tan(halfAngleRadians);
		t *= ratio;
		float retDegrees = (180.0f / MathF.PI) * MathF.Atan(t);
		return retDegrees * 2.0f;
	}

	public void Render(ViewRects rect) {
		using MatRenderContextPtr renderContext = new(materials);
		ref ViewRect vr = ref rect[0];

		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();

		render.SetMainView(in View.Origin, in View.Angles);

		for (StereoEye eye = GetFirstEye(); eye <= GetLastEye(); eye = eye + 1) {
			ref ViewSetup viewEye = ref GetView(eye);

			float aspectRatio = engine.GetScreenAspectRatio() * 0.75f;
			float limitedAspectRatio = aspectRatio;
			viewEye.FOV = ScaleFOVByWidthRatio(viewEye.FOV, limitedAspectRatio);
			viewEye.FOVViewmodel = ScaleFOVByWidthRatio(viewEye.FOVViewmodel, limitedAspectRatio);

			float viewportScale = 1.0f; // mat_viewportscale todo
			viewEye.UnscaledX = vr.X;
			viewEye.UnscaledY = vr.Y;
			viewEye.UnscaledWidth = vr.Width;
			viewEye.UnscaledHeight = vr.Height;

			switch (eye) {
				case StereoEye.Mono:
					viewEye.X = (int)(vr.X * viewportScale);
					viewEye.Y = (int)(vr.Y * viewportScale);
					viewEye.Width = (int)(vr.Width * viewportScale);
					viewEye.Height = (int)(vr.Height * viewportScale);
					float engineAspectRatio = engine.GetScreenAspectRatio();
					viewEye.AspectRatio = (engineAspectRatio > 0.0f) ? engineAspectRatio : ((float)viewEye.Width / (float)viewEye.Height);
					break;
				default:
					throw new NotImplementedException("Stereo-eye not yet implemented");
			}

#if GMOD_DLL
			if (LuaRender.RenderViewID == ViewID.Illegal)
				SpawnIconRenderer.RenderQueued();
#endif

			ClearFlags clearFlags = ClearFlags.ClearColor | ClearFlags.ClearDepth | ClearFlags.ClearStencil;

			bool drawViewModel = true; // todo

			RenderViewInfo flags = 0;
			if (eye == StereoEye.Mono)
				flags = RenderViewInfo.DrawHUD;

			if (drawViewModel)
				flags |= RenderViewInfo.DrawViewmodel;

			RenderView(in viewEye, clearFlags, flags);
		}

		View2D.X = rect[0].X;
		View2D.Y = rect[0].Y;
		View2D.Width = rect[0].Width;
		View2D.Height = rect[0].Height;

		render.Push2DView(View2D, 0, null, GetFrustum());
		render.VGui_Paint(PaintMode.UIPanels | PaintMode.Cursor);
		// This is dumb and I just needed it to show a prediction thing and was super lazy.
		// var i = Singleton<IInputSystem>();
		// int btnsize = 32;
		// int padsize = 8;
		// bool cond;
		// {
		// 	cond = i.IsButtonDown(ButtonCode.KeyW);
		// 	surface.DrawSetColor(cond ? 0 : 255, cond ? 255 : 0, 0, 255);
		// 	surface.DrawFilledRect(300, 800, 300 + btnsize, 800 + btnsize);
		// 
		// 	cond = i.IsButtonDown(ButtonCode.KeyS);
		// 	surface.DrawSetColor(cond ? 0 : 255, cond ? 255 : 0, 0, 255);
		// 	surface.DrawFilledRect(300, 800 + btnsize + padsize, 300 + btnsize, 800 + btnsize + padsize + btnsize);
		// 
		// 	cond = i.IsButtonDown(ButtonCode.KeyA);
		// 	surface.DrawSetColor(cond ? 0 : 255, cond ? 255 : 0, 0, 255);
		// 	surface.DrawFilledRect(300 - btnsize - padsize, 800 + btnsize + padsize, (300 - btnsize - padsize) + btnsize, 800 + btnsize + padsize + btnsize);
		// 
		// 	cond = i.IsButtonDown(ButtonCode.KeyD);
		// 	surface.DrawSetColor(cond ? 0 : 255, cond ? 255 : 0, 0, 255);
		// 	surface.DrawFilledRect(300 + btnsize + padsize, 800 + btnsize + padsize, 300 + btnsize + padsize + btnsize, 800 + btnsize + padsize + btnsize);
		// }
		render.PopView(GetFrustum());
	}

	IEngineVGui? _enginevgui;
	IEngineVGui enginevgui => _enginevgui ??= Singleton<IEngineVGui>();

	public void RenderView(in ViewSetup viewRender, ClearFlags clearFlags, RenderViewInfo whatToDraw) {
		CurrentView = viewRender;

		using C_BaseAnimating.AutoAllowBoneAccess boneaccess = new(true, true);

		MatRenderContextPtr renderContext;

		using (renderContext = new MatRenderContextPtr(materials)) {
			ITexture? saveRenderTarget = renderContext.GetRenderTarget();
		}

		g_ClientShadowMgr.AdvanceFrame();

		RenderingView = true;
		render.SceneBegin();
		using (renderContext = new MatRenderContextPtr(materials))
			renderContext.TurnOnToneMapping();

		SetupMain3DView(in viewRender, clearFlags);

		bool drew3dSkybox = false;
		SkyboxVisibility skyboxVisible = SkyboxVisibility.NotVisible;

		{
			SkyboxView skyView = new SkyboxView(this);
			if ((drew3dSkybox = skyView.Setup(in viewRender, ref clearFlags, ref skyboxVisible)) != false)
				AddViewToScene(skyView);
			skyView.ReleaseLists();
		}

		if ((clearFlags & ClearFlags.ClearColor) == 0) {
			if (enginetrace.GetPointContents(viewRender.Origin, out _) == Contents.Solid) {
				clearFlags |= ClearFlags.ClearColor;
			}
		}

		if (IntroData.g_pIntroData == null)
			ViewDrawScene(drew3dSkybox, skyboxVisible, in viewRender, clearFlags, ViewID.Main, (whatToDraw & RenderViewInfo.DrawViewmodel) != 0);
		else
			ViewDrawScene_Intro(in viewRender, clearFlags, IntroData.g_pIntroData);
		render.SceneEnd();

		RenderPlayerSprites();

		DrawViewModels(in viewRender, (whatToDraw & RenderViewInfo.DrawViewmodel) != 0);

		// Draw fade over entire screen if needed
		Span<byte> color = stackalloc byte[4];
		vieweffects.GetFadeParams(out color[0], out color[1], out color[2], out color[3], out bool blend);

		// Overlay screen fade on entire screen
		IMaterial? material = blend ? ModulateSingleColor.Get() : TranslucentSingleColor.Get();
		render.ViewDrawFade(color, material);

		CleanupMain3DView(in viewRender);

		if ((whatToDraw & RenderViewInfo.DrawHUD) != 0) {
			int viewWidth = viewRender.UnscaledWidth;
			int viewHeight = viewRender.UnscaledHeight;
			int viewActualWidth = viewRender.UnscaledWidth;
			int viewActualHeight = viewRender.UnscaledHeight;
			int viewX = viewRender.UnscaledX;
			int viewY = viewRender.UnscaledY;
			int viewFramebufferX = 0;
			int viewFramebufferY = 0;
			int viewFramebufferWidth = viewWidth;
			int viewFramebufferHeight = viewHeight;
			bool clear = false;
			bool paintMainMenu = false;
			ITexture? pTexture = null;

			using (renderContext = new MatRenderContextPtr(materials)) {
				if (clear)
					renderContext.ClearBuffers(false, true, true);

				renderContext.PushRenderTargetAndViewport(pTexture, viewX, viewY, viewActualWidth, viewActualHeight);

				// TODO
				// if (pTexture != null) 
				// renderContext.OverrideAlphaWriteEnable(true, true);

				if (clear) {
					renderContext.ClearColor4ub(0, 0, 0, 0);
					renderContext.ClearBuffers(true, false);
				}
			}

			// VGui_PreRender();

			IPanel? root = enginevgui.GetPanel(VGuiPanelType.ClientDll);
			root?.SetSize(viewWidth, viewHeight);

			root = enginevgui.GetPanel(VGuiPanelType.ClientDllTools);
			root?.SetSize(viewWidth, viewHeight);

			AllowCurrentViewAccess(true);

			render.VGui_Paint(PaintMode.InGamePanels);
			if (paintMainMenu)
				render.VGui_Paint(PaintMode.UIPanels | PaintMode.Cursor);

			AllowCurrentViewAccess(false);
			// VGui_PostRender();
			using (renderContext = new MatRenderContextPtr(materials)) {
#if GMOD_DLL
				((ClientModeShared)clientMode).PostRenderVGui(renderContext);
#endif
				if (pTexture != null) {
					// renderContext.OverrideAlphaWriteEnable(false, true);
				}

				renderContext.PopRenderTargetAndViewport();
				renderContext.Flush();
			}
		}
	}

	private void ViewDrawScene(bool drew3dSkybox, SkyboxVisibility skyboxVisible, in ViewSetup viewRender, ClearFlags clearFlags, ViewID viewID, bool drawViewModel = false, DrawFlags baseDrawFlags = 0) {
		// this allows the refract texture to be updated once per *scene* on 360
		// (e.g. once for a monitor scene and once for the main scene)
		g_viewscene_refractUpdateFrame = gpGlobals.FrameCount - 1;

		BaseDrawFlags = baseDrawFlags;
		SetupCurrentView(in viewRender.Origin, in viewRender.Angles, viewID);
		IGameSystem.PreRenderAllSystems();
		SetupVis(in viewRender, out uint visFlags);

		g_ClientShadowMgr.PreRender();

		// todo
		// if (r_flashlightdepthtexture.GetBool() && viewID == ViewID.Main)
		// 	g_ClientShadowMgr.ComputeShadowDepthTextures(viewRender);

		bool drawSkybox = ViewRenderConVars.r_skybox.GetBool();
		if (drew3dSkybox || skyboxVisible == SkyboxVisibility.NotVisible)
			drawSkybox = false;

		DrawWorldAndEntities(drawSkybox, in viewRender, clearFlags);

		DebugViewRender.Draw3DDebuggingInfo(in viewRender);

		// todo
		// if (r_flashlightdepthtexture.GetBool())
		// 	g_ClientShadowMgr.UnlockAllShadowDepthTextures();
	}

	private void ViewDrawScene_Intro(in ViewSetup view, ClearFlags clearFlags, IntroData introData) {
		using MatRenderContextPtr renderContext = new(materials);

		// this allows the refract texture to be updated once per *scene* on 360
		// (e.g. once for a monitor scene and once for the main scene)
		g_viewscene_refractUpdateFrame = gpGlobals.FrameCount - 1;

		renderContext.ClearColor4ub(0, 0, 0, 255);

		if (introData.DrawPrimary) {
			ViewSetup playerView = view;
			playerView.Origin = introData.CameraView;
			playerView.Angles = introData.CameraViewAngles;
			if (introData.PlayerViewFOV != 0)
				playerView.FOV = ScaleFOVByWidthRatio(introData.PlayerViewFOV, engine.GetScreenAspectRatio() / (4.0f / 3.0f));

			g_ClientShadowMgr.PreRender();

			SetupCurrentView(in playerView.Origin, in playerView.Angles, ViewID.IntroPlayer);

			IGameSystem.PreRenderAllSystems();

			SetupVis(in playerView, out uint visFlags);

			render.Push3DView(in playerView, ClearFlags.ClearColor | ClearFlags.ClearDepth, null, GetFrustum(), null);
			DrawWorldAndEntities(true, in playerView, ClearFlags.ClearColor | ClearFlags.ClearDepth);
			render.PopView(GetFrustum());
		}
		else
			renderContext.ClearBuffers(true, true);

		UpdateScreenEffectTexture(0, view.X, view.Y, view.Width, view.Height, false, out System.Drawing.Rectangle actualRect);

		g_ClientShadowMgr.PreRender();

		SetupCurrentView(in view.Origin, in view.Angles, ViewID.IntroCamera);

		IGameSystem.PreRenderAllSystems();

		SetupVis(in view, out _);

		renderContext.ClearColor4ub(0, 0, 0, 255);

		DrawWorldAndEntities(true, in view, ClearFlags.ClearColor | ClearFlags.ClearDepth);

		UpdateScreenEffectTexture(1, view.X, view.Y, view.Width, view.Height, false, out _);

		IMaterial overlayMaterial = materials.FindMaterial("scripted/intro_screenspaceeffect", MaterialDefines.TEXTURE_GROUP_OTHER)!;
		IMaterialVar modeVar = overlayMaterial.FindVar("$mode", out _)!;
		IMaterialVar alphaVar = overlayMaterial.FindVar("$alpha", out _)!;

		renderContext.ClearBuffers(true, true);

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();
		renderContext.LoadIdentity();

		for (int passID = 0; passID < introData.Passes.Count; passID++) {
			IntroDataBlendPass pass = introData.Passes[passID];
			if (pass.Alpha == 0)
				continue;

			if (pass.BlendMode >= 0 && pass.BlendMode <= 9)
				modeVar.SetIntValue(pass.BlendMode);
			else
				Assert(false);

			alphaVar.SetFloatValue(pass.Alpha);

			ITexture texture = RenderTexture.GetFullFrameFrameBufferTexture(0)!;
			renderUtils.DrawScreenSpaceRectangle(overlayMaterial, 0, 0, view.Width, view.Height,
				actualRect.X, actualRect.Y, actualRect.X + actualRect.Width - 1, actualRect.Y + actualRect.Height - 1,
				texture.GetActualWidth(), texture.GetActualHeight(), null, 1, 1, 0);
		}

		renderContext.MatrixMode(MaterialMatrixMode.View);
		renderContext.PopMatrix();

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PopMatrix();

		// Make sure sound doesn't stutter
		engine.Sound_ExtraUpdate();

		// Debugging info goes over the top
		DebugViewRender.Draw3DDebuggingInfo(in view);
	}

	static RenderUtils? _renderUtils;
	static RenderUtils renderUtils => _renderUtils ??= Singleton<RenderUtils>();

	private void DrawWorldAndEntities(bool drawSkybox, in ViewSetup viewRender, ClearFlags clearFlags) {
		SimpleWorldView noWaterView = new SimpleWorldView(this);
		noWaterView.Setup(in viewRender, clearFlags, drawSkybox);
		AddViewToScene(noWaterView);
		noWaterView.ReleaseLists();
	}

	public static Vector3 g_VecRenderOrigin = new(0, 0, 0);
	public static QAngle g_VecRenderAngles = new(0, 0, 0);
	public static Vector3 g_VecPrevRenderOrigin = new(0, 0, 0);
	public static QAngle g_VecPrevRenderAngles = new(0, 0, 0);
	public static Vector3 g_VecVForward = new(0, 0, 0), g_VecVRight = new(0, 0, 0), g_VecVUp = new(0, 0, 0);
	public static Matrix4x4 g_MatCamInverse;
	public static float g_FOV;

	public static Vector3 g_VecCurrentRenderOrigin = new(0, 0, 0);
	public static QAngle g_VecCurrentRenderAngles = new(0, 0, 0);
	public static Vector3 g_VecCurrentVForward = new(0, 0, 0), g_VecCurrentVRight = new(0, 0, 0), g_VecCurrentVUp = new(0, 0, 0);
	public static Matrix4x4 g_MatCurrentCamInverse;

	public static bool s_bCanAccessCurrentView = false;
	public static bool RenderingView = false;
	public static ViewID g_CurrentViewID = ViewID.None;
	public virtual bool ShouldForceNoVis() => ForceNoVis;
	private void SetupVis(in ViewSetup viewRender, out uint visFlags) {
		// TODO: more logic here 
		render.ViewSetupVisEx(ShouldForceNoVis(), new ReadOnlySpan<Vector3>(in viewRender.Origin), out visFlags);
	}

	protected bool ShouldDrawViewModel(bool drawViewmodel) {
		if (!drawViewmodel)
			return false;

		if (!r_drawviewmodel.GetBool())
			return false;

		if (C_BasePlayer.ShouldDrawLocalPlayer())
			return false;

		if (!ShouldDrawEntities())
			return false;

		if (render.GetViewEntity() > gpGlobals.MaxClients)
			return false;

		return true;
	}

	C_BaseEntity? CurrentlyDrawingEntity;

	private void DrawRenderablesInList(List<IClientRenderable> list, StudioFlags flags = 0) {
		int nCount = list.Count();
		for (int i = 0; i < nCount; ++i) {
			IClientUnknown? unk = list[i].GetIClientUnknown();
			Assert(unk != null);

			IClientRenderable? renderable = unk.GetClientRenderable();
			Assert(renderable != null);

			if (renderable.ShouldDraw()) {
				CurrentlyDrawingEntity = unk.GetBaseEntity();
				renderable.DrawModel(StudioFlags.Render | flags);
			}
		}
		CurrentlyDrawingEntity = null;
	}

	//-----------------------------------------------------------------------------
	// Purpose: Renders voice feedback and other sprites attached to players
	// Input  : none
	//-----------------------------------------------------------------------------
	void RenderPlayerSprites() {
		GetClientVoiceMgr().DrawHeadLabels();
	}

	private void DrawViewModels(in ViewSetup viewRender, bool drawViewmodel) {
		bool shouldDrawPlayerViewModel = ShouldDrawViewModel(drawViewmodel);
		bool shouldDrawToolViewModels = ToolsEnabled();

		using MatRenderContextPtr renderContext = new(materials);

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PushMatrix();

		ViewSetup viewModelSetup = viewRender;
		viewModelSetup.ZNear = viewRender.ZNearViewmodel;
		viewModelSetup.ZFar = viewRender.ZFarViewmodel;
		viewModelSetup.FOV = viewRender.FOVViewmodel;
		viewModelSetup.AspectRatio = engine.GetScreenAspectRatio();

		ITexture? rtColor = null;
		ITexture? rtDepth = null;
		if (viewRender.StereoEye != StereoEye.Mono)
			throw new NotImplementedException("Non-mono StereoEye not supported");

		render.Push3DView(viewModelSetup, 0, rtColor, GetFrustum(), rtDepth);

		const bool useDepthHack = true;

		// FIXME: Add code to read the current depth range
		float depthmin = 0.0f;
		float depthmax = 1.0f;

		// HACK HACK:  Munge the depth range to prevent view model from poking into walls, etc.
		// Force clipped down range
		if (useDepthHack)
			renderContext.DepthRange(0.0f, 0.1f);

		if (shouldDrawPlayerViewModel || shouldDrawToolViewModels) {
			List<IClientRenderable> opaqueViewModelList = ListPool<IClientRenderable>.Shared.Alloc(32);
			List<IClientRenderable> translucentViewModelList = ListPool<IClientRenderable>.Shared.Alloc(32);

			clientLeafSystem.CollateViewModelRenderables(opaqueViewModelList, translucentViewModelList);

			if (ToolsEnabled() && (!shouldDrawPlayerViewModel || !shouldDrawToolViewModels)) {
				int opaque = opaqueViewModelList.Count;
				for (int i = opaque - 1; i >= 0; --i) {
					IClientRenderable renderable = opaqueViewModelList[i];
					bool entity = renderable.GetIClientUnknown().GetBaseEntity() != null;
					if ((entity && !shouldDrawPlayerViewModel) || (!entity && !shouldDrawToolViewModels))
						opaqueViewModelList.RemoveAt(i);
				}

				int translucent = translucentViewModelList.Count;
				for (int i = translucent - 1; i >= 0; --i) {
					IClientRenderable renderable = translucentViewModelList[i];
					bool entity = renderable.GetIClientUnknown().GetBaseEntity() != null;
					if ((entity && !shouldDrawPlayerViewModel) || (!entity && !shouldDrawToolViewModels))
						translucentViewModelList.RemoveAt(i);
				}
			}

			//  if (!UpdateRefractIfNeededByList(opaqueViewModelList)) 
			//  	UpdateRefractIfNeededByList(translucentViewModelList);

			DrawRenderablesInList(opaqueViewModelList);
			DrawRenderablesInList(translucentViewModelList, StudioFlags.Transparency);

			ListPool<IClientRenderable>.Shared.Free(opaqueViewModelList);
			ListPool<IClientRenderable>.Shared.Free(translucentViewModelList);
		}

		// Reset the depth range to the original values
		if (useDepthHack)
			renderContext.DepthRange(depthmin, depthmax);

		render.PopView(GetFrustum());

		renderContext.MatrixMode(MaterialMatrixMode.Projection);
		renderContext.PopMatrix();
	}

	private void CleanupMain3DView(in ViewSetup viewRender) {
		render.PopView(GetFrustum());
	}

	private void AddViewToScene(Rendering3dView view) {
		SimpleExecutor.AddView(view);
	}

	// Needs more work. Mostly just to clear the buffers rn
	private void SetupMain3DView(in ViewSetup viewRender, ClearFlags clearFlags) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.ClearColor4ub(0, 0, 0, 255); // FOR DEBUGGING: this used to be 125, 0, 0
		renderContext.ClearBuffers((clearFlags & ClearFlags.ClearColor) != 0, (clearFlags & ClearFlags.ClearDepth) != 0, (clearFlags & ClearFlags.ClearStencil) != 0);

		ITexture? rtColor = null;
		ITexture? rtDepth = null;
		render.Push3DView(in viewRender, clearFlags, rtColor, GetFrustum(), rtDepth);
	}

	public void SetCheapWaterEndDistance(float cheapWaterEndDistance) {
		throw new NotImplementedException();
	}

	public void SetCheapWaterStartDistance(float cheapWaterStartDistance) {
		throw new NotImplementedException();
	}

	public void SetScreenOverlayMaterial(IMaterial? pMaterial) {
		throw new NotImplementedException();
	}

	public bool ShouldDrawBrushModels() {
		throw new NotImplementedException();
	}

	public void Shutdown() {
		throw new NotImplementedException();
	}

	public void StartPitchDrift() {

	}

	public void StopPitchDrift() {

	}

	public bool UpdateShadowDepthTexture(ITexture? pRenderTarget, ITexture? pDepthTexture, in ViewSetup shadowView) {
		throw new NotImplementedException();
	}

	public void WriteSaveGameScreenshot(ReadOnlySpan<char> pFilename) {
		throw new NotImplementedException();
	}

	public void WriteSaveGameScreenshotOfSize(ReadOnlySpan<char> pFilename, int width, int height, bool bCreatePowerOf2Padded = false, bool bWriteVTF = false) {
		throw new NotImplementedException();
	}

	internal Base3dView? SetActiveRenderer(Base3dView? view) {
		Base3dView? previous = ActiveRenderer;
		ActiveRenderer = view;
		return previous;
	}

	ConVar? DrawEntities;

	internal bool ShouldDrawEntities() {
		return DrawEntities == null || DrawEntities.GetInt() != 0;
	}

	public int m_BuildRenderablesListsNumber;

	public long BuildRenderablesListsNumber() => m_BuildRenderablesListsNumber;
	public long IncRenderablesListsNumber() => ++m_BuildRenderablesListsNumber;

	public int m_BuildWorldListsNumber;

	public int BuildWorldListsNumber() => m_BuildWorldListsNumber;
	public int IncWorldListsNumber() => ++m_BuildWorldListsNumber;


	C_BaseEntity? currentlyDrawingEntity;
	public C_BaseEntity? GetCurrentlyDrawingEntity() {
		return currentlyDrawingEntity;
	}

	public void SetCurrentlyDrawingEntity(C_BaseEntity? ent) {
		currentlyDrawingEntity = ent;
	}
}
