using Source;
using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Client;
using Source.Common.GUI;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;
using Source.Engine;

using TextureFlags = Source.Common.TextureFlags;

using System.Numerics;

namespace Game.Client.GarrysMod;

public static partial class LuaRender
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_render = new("render");
	static readonly IMaterialSystemHardwareConfig HardwareConfig = Singleton<IMaterialSystemHardwareConfig>();
	static readonly TextureReference RenderTextureReference = new();

	struct NamedRenderTarget
	{
		public InlineArray256<char> Name;
		public ITexture? Texture;
	}

	static readonly List<NamedRenderTarget> NamedRenderTargets = [];

	static ITexture? CreateNamedRenderTarget(ReadOnlySpan<char> name, int w, int h, RenderTargetSizeMode sizeMode, MaterialRenderTargetDepth depth, TextureFlags textureFlags, CreateRenderTargetFlags renderTargetFlags, bool unused, ImageFormat format) {
		if (name.IsEmpty)
			return null;

		int index = -1;
		for (int i = 0; i < NamedRenderTargets.Count; i++) {
			NamedRenderTarget rt = NamedRenderTargets[i];
			if (stricmp(((ReadOnlySpan<char>)rt.Name).SliceNullTerminatedString(), name) == 0) {
				if (rt.Texture == null || rt.Texture.IsError()) {
					index = i;
					break;
				}
				return rt.Texture;
			}
		}

		if (stricmp("_rt_ResolvedFullFrameDepth", name) == 0) {
			Warning($"Warning! Creating an RT ({name}) with name of an existing texture!\n");
			return materials.FindTexture("_rt_ResolvedFullFrameDepth", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET, true, 0);
		}

		if ((textureFlags & TextureFlags.EnvMap) != 0) {
			Warning($"Warning! Creating an RT ({name}) with TEXTUREFLAGS_ENVMAP! This will cause crashes! Bailing..\n");
			return null;
		}

		if ((textureFlags & TextureFlags.Procedural) != 0) {
			Warning($"Warning! Creating an RT ({name}) with TEXTUREFLAGS_PROCEDURAL! Not allowed.\n");
			return null;
		}

		if (format == ImageFormat.Unknown)
			format = materials.GetBackBufferFormat();

		ITexture? texture = materials.CreateNamedRenderTargetTextureEx(name, w, h, sizeMode, format, depth, textureFlags, renderTargetFlags);
		if (texture == null) {
			Warning($"Warning! Failed to create render target {name}!\n");
			return null;
		}

		NamedRenderTarget entry = default;
		strcpy(entry.Name, name);
		entry.Texture = texture;
		if (index == -1)
			NamedRenderTargets.Add(entry);
		else
			NamedRenderTargets[index] = entry;
		return texture;
	}

	[LuaGlobal]
	static int GetRenderTarget(ILuaInterface lua) {
		ITexture? texture = CreateNamedRenderTarget(lua.CheckString(1), (int)lua.CheckNumber(2), (int)lua.CheckNumber(3), RenderTargetSizeMode.NoChange, MaterialRenderTargetDepth.Separate, TextureFlags.Trilinear | TextureFlags.NoMip, 0, true, ImageFormat.Unknown);
		if (texture != null && !texture.IsError()) {
			LuaTexture.Push(texture);
			return 1;
		}
		return 0;
	}

	[LuaGlobal]
	static int GetRenderTargetEx(ILuaInterface lua) {
		float sizeMode = (int)lua.CheckNumber(4);
		if (sizeMode <= 0)
			sizeMode = 0;
		if ((float)RenderTargetSizeMode.LiteralPicmip <= sizeMode)
			sizeMode = (float)RenderTargetSizeMode.LiteralPicmip;

		float depth = (int)lua.CheckNumber(5);
		if (depth <= 0)
			depth = 0;
		if ((float)MaterialRenderTargetDepth.Only <= depth)
			depth = (float)MaterialRenderTargetDepth.Only;

		int format = (int)lua.CheckNumber(8);
		if (format < (int)ImageFormat.Unknown || format >= (int)ImageFormat.Count) {
			lua.Error("GetRenderTargetEx: Invalid image format\n");
			return 0;
		}

		ITexture? texture = CreateNamedRenderTarget(lua.CheckString(1), (int)lua.CheckNumber(2), (int)lua.CheckNumber(3), (RenderTargetSizeMode)(uint)sizeMode, (MaterialRenderTargetDepth)(uint)depth, (TextureFlags)(int)lua.CheckNumber(6), (CreateRenderTargetFlags)(int)lua.CheckNumber(7), false, (ImageFormat)format);
		if (texture != null && !texture.IsError()) {
			LuaTexture.Push(texture);
			return 1;
		}
		return 0;
	}

	// todo: DrawSprite
	// todo: DrawQuadEasy
	// todo: DrawQuad
	// todo: DrawScreenQuad
	// todo: DrawScreenQuadEx
	// todo: DrawBeam
	// todo: StartBeam
	// todo: AddBeam
	// todo: EndBeam
	// todo: SetMaterial
	// todo: SetLightmapTexture
	// todo: GetLightColor
	[LuaFunction]
	static int GetAmbientLightColor(ILuaInterface lua) {
		engine.GetAmbientLightColor(out Vector3 color);
		LuaVector.Push_Vector(color);
		return 1;
	}
	// todo: ComputeDynamicLighting
	// todo: ComputeLighting
	// todo: GetSurfaceColor

	[LuaFunction]
	static int GetDXLevel() => HardwareConfig.GetDXSupportLevel();

	[LuaFunction]
	static bool SupportsHDR() => HardwareConfig.GetHardwareHDRType() != 0;

	[LuaFunction]
	static bool GetHDREnabled() => HardwareConfig.GetHDREnabled();

	[LuaFunction]
	static bool SupportsPixelShaders_1_4() => HardwareConfig.SupportsPixelShaders_1_4();

	[LuaFunction]
	static bool SupportsPixelShaders_2_0() => HardwareConfig.SupportsPixelShaders_2_0();

	[LuaFunction]
	static bool SupportsVertexShaders_2_0() => HardwareConfig.SupportsVertexShaders_2_0();

	[LuaFunction]
	static int MaxTextureWidth() => HardwareConfig.MaxTextureWidth();

	[LuaFunction]
	static int MaxTextureHeight() => HardwareConfig.MaxTextureHeight();
	// todo: UpdateFullScreenDepthTexture
	// todo: GetFullScreenDepthTexture
	// todo: GetResolvedFullFrameDepth
	// todo: BindLocalCubemap
	// todo: UpdateScreenEffectTexture
	// todo: UpdateRefractTexture
	// todo: GetRefractTexture
	// todo: UpdatePowerOfTwoTexture
	// todo: GetPowerOfTwoTexture
	[LuaFunction("GetRenderTarget")]
	static int GetCurrentRenderTarget(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		ITexture? texture = renderContext.GetRenderTarget();
		if (texture == null)
			return 0;
		LuaTexture.Push(texture);
		return 1;
	}

	[LuaFunction]
	static int SetRenderTarget(ILuaInterface lua) {
		ITexture? texture = null;
		if (lua.IsType(1, LuaType.Texture))
			texture = (ITexture?)LuaTexture.LC_ITexture.Get(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Flush(false);
		renderContext.SetRenderTarget(texture);
		return 0;
	}

	[LuaFunction]
	static int SetRenderTargetEx(ILuaInterface lua) {
		int id = (int)(uint)lua.CheckNumber(1);
		if (id < 0)
			id = 0;
		else if (id > 4)
			id = 4;
		ITexture? texture = null;
		if (lua.IsType(2, LuaType.Texture))
			texture = (ITexture?)LuaTexture.LC_ITexture.Get(2);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Flush(false);
		renderContext.SetRenderTargetEx(id, texture);
		return 0;
	}
	static int RenderTargetStack;

	[LuaFunction]
	static int PushRenderTarget(ILuaInterface lua) {
		ITexture? texture = null;
		if (lua.IsType(1, LuaType.Texture))
			texture = (ITexture?)LuaTexture.LC_ITexture.Get(1);

		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Flush(false);
		if (lua.GetType(2) == LuaType.Number)
			renderContext.PushRenderTargetAndViewport(texture, (int)lua.CheckNumber(2), (int)lua.CheckNumber(3), (int)lua.CheckNumber(4), (int)lua.CheckNumber(5));
		else
			renderContext.PushRenderTargetAndViewport(texture);

		RenderTargetStack++;
		return 0;
	}

	[LuaFunction]
	static int PopRenderTarget(ILuaInterface lua) {
		if (RenderTargetStack > 0) {
			RenderTargetStack--;
			using MatRenderContextPtr renderContext = new(materials);
			renderContext.Flush(false);
			renderContext.PopRenderTargetAndViewport();
			return 0;
		}

		lua.ErrorFromLua("render.PopRenderTarget underflow\n");
		return 0;
	}
	[LuaFunction]
	static int GetScreenEffectTexture(ILuaInterface lua) {
		int index = 0;
		if (lua.GetType(1) == LuaType.Number)
			index = (int)lua.GetNumber(1);

		ITexture? texture = RenderTexture.GetFullFrameFrameBufferTexture(index);
		if (texture != null && !texture.IsError()) {
			LuaTexture.Push(texture);
			return 1;
		}
		return 0;
	}
	[LuaFunction]
	static int GetBloomTex0(ILuaInterface lua) {
		LuaTexture.Push(RenderTexture.GetBloomTex0());
		return 1;
	}

	[LuaFunction]
	static int GetBloomTex1(ILuaInterface lua) {
		LuaTexture.Push(RenderTexture.GetBloomTex1());
		return 1;
	}

	[LuaFunction]
	static int GetMoBlurTex0(ILuaInterface lua) {
		LuaTexture.Push(RenderTexture.GetMoBlurTex0());
		return 1;
	}

	[LuaFunction]
	static int GetMoBlurTex1(ILuaInterface lua) {
		LuaTexture.Push(RenderTexture.GetMoBlurTex1());
		return 1;
	}
	// todo: GetMorphTex0
	// todo: GetMorphTex1
	// todo: GetSmallTex0
	// todo: GetSmallTex1
	[LuaFunction]
	static int GetSuperFPTex(ILuaInterface lua) {
		LuaTexture.Push(RenderTexture.GetSuperFPTex(null));
		return 1;
	}

	[LuaFunction]
	static int GetSuperFPTex2(ILuaInterface lua) {
		LuaTexture.Push(RenderTexture.GetSuperFPTex2(null));
		return 1;
	}
	// todo: SuppressEngineLighting
	// todo: SetLocalModelLights
	// todo: ResetModelLighting
	// todo: SetModelLighting
	// todo: SetAmbientLight
	[LuaFunction]
	static int SetLightingOrigin(ILuaInterface lua) {
		ref Vector3 origin = ref LuaVector.Get_Vector(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetLightingOrigin(origin);
		return 0;
	}
	[LuaFunction]
	static void SetColorModulation([LuaGet] float r, [LuaGet] float g, [LuaGet] float b) {
		ReadOnlySpan<float> color = [r, g, b];
		render.SetColorModulation(color);
	}

	[LuaFunction]
	static (float, float, float) GetColorModulation() {
		Vector3 color = render.GetColorModulation();
		return (color.X, color.Y, color.Z);
	}

	[LuaFunction]
	static void SetBlend([LuaGet] float blend) => render.SetBlend(blend);

	[LuaFunction]
	static float GetBlend() => render.GetBlend();
	public static ViewID RenderViewID = ViewID.Illegal;
	public static readonly List<bool> DrawViewerStack = [];

	static float UnscaleFOVByWidthRatio(float fovDegrees, float ratio) {
		float halfAngleRadians = fovDegrees * (0.5f * MathF.PI / 180.0f);
		float t = MathF.Tan(halfAngleRadians);
		t /= ratio;
		float retDegrees = (180.0f / MathF.PI) * MathF.Atan(t);
		return retDegrees * 2.0f;
	}

	[LuaFunction]
	static int RenderView(ILuaInterface lua) {
		ViewSetup setup = view.GetViewSetup();
		LuaTable data = new(null, 0);
		data.SetFromStack(1);

		RenderViewInfo whatToDraw = data.GetMemberBool("drawhud", false) ? RenderViewInfo.DrawHUD : 0;
		if (data.GetMemberBool("drawviewmodel", true))
			whatToDraw |= RenderViewInfo.DrawViewmodel;
		if (!data.GetMemberBool("dopostprocess", false))
			whatToDraw |= RenderViewInfo.NoPostProcess;
		if (!data.GetMemberBool("drawmonitors", false))
			whatToDraw |= RenderViewInfo.SuppressMonitorRendering;
		bool drawViewer = data.GetMemberBool("drawviewer", false);
		setup.DoBloomAndToneMapping = data.GetMemberBool("bloomtone", true);
		setup.CacheFullSceneState = false;

		setup.X = (int)data.GetMemberFloat("x", setup.X);
		setup.Y = (int)data.GetMemberFloat("y", setup.Y);
		setup.Width = (int)data.GetMemberFloat("w", setup.Width);
		setup.Height = (int)data.GetMemberFloat("h", setup.Height);

		LuaObject offCenter = new();
		data.GetMember("offcenter", offCenter);
		if (offCenter.isTable()) {
			setup.OffCenter = true;
			float invWidth = 1.0f / setup.Width;
			float invHeight = 1.0f / setup.Height;
			setup.OffCenterLeft = offCenter.GetMemberFloat("left", setup.OffCenterLeft) * invWidth;
			setup.OffCenterRight = offCenter.GetMemberFloat("right", setup.OffCenterRight) * invWidth;
			setup.OffCenterTop = offCenter.GetMemberFloat("bottom", setup.OffCenterTop) * invHeight;
			setup.OffCenterBottom = offCenter.GetMemberFloat("top", setup.OffCenterBottom) * invHeight;
		}
		else
			setup.OffCenter = false;

		setup.Origin = data.GetMemberVector("origin", setup.Origin);
		setup.Angles = data.GetMemberAngle("angles", setup.Angles);
		setup.FOV = data.GetMemberFloat("fov", setup.FOV);
		setup.FOVViewmodel = data.GetMemberFloat("viewmodelfov", setup.FOVViewmodel);
		setup.ZNear = data.GetMemberFloat("znear", setup.ZNear);
		setup.ZFar = data.GetMemberFloat("zfar", setup.ZFar);
		setup.ZNearViewmodel = data.GetMemberFloat("znearviewmodel", setup.ZNearViewmodel);
		setup.ZFarViewmodel = data.GetMemberFloat("zfarviewmodel", setup.ZFarViewmodel);

		LuaObject ortho = new();
		data.GetMember("ortho", ortho);
		if (ortho.isTable()) {
			setup.Ortho = true;
			setup.OrthoLeft = ortho.GetMemberFloat("left", setup.OrthoLeft);
			setup.OrthoRight = ortho.GetMemberFloat("right", setup.OrthoRight);
			setup.OrthoTop = ortho.GetMemberFloat("top", setup.OrthoTop);
			setup.OrthoBottom = ortho.GetMemberFloat("bottom", setup.OrthoBottom);
		}
		else if (ortho.isBool() && ortho.GetBool()) {
			setup.Ortho = true;
			setup.OrthoLeft = data.GetMemberFloat("ortholeft", setup.OrthoLeft);
			setup.OrthoTop = data.GetMemberFloat("orthotop", setup.OrthoTop);
			setup.OrthoRight = data.GetMemberFloat("orthoright", setup.OrthoRight);
			setup.OrthoBottom = data.GetMemberFloat("orthobottom", setup.OrthoBottom);
		}
		else
			setup.Ortho = false;

		setup.RenderToSubrectOfLargerScreen = setup.X != 0 || setup.Y != 0 || setup.Width != ScreenWidth() || setup.Height != ScreenHeight();

		float aspect = engine.GetScreenAspectRatio();
		if (aspect <= 0.0f)
			aspect = (float)setup.Width / setup.Height;
		aspect = data.GetMemberFloat("aspect", aspect);
		setup.AspectRatio = data.GetMemberFloat("aspectratio", aspect);

		if (drawViewer)
			DrawViewerStack.Add(true);

		LuaCam.ResetFrameStateChecked();
		RenderViewID = (ViewID)Math.Clamp(data.GetMemberInt("viewid", 0), 0, 9);
		view.RenderView(in setup, ClearFlags.ClearColor | ClearFlags.ClearDepth | ClearFlags.ClearStencil, whatToDraw);
		RenderViewID = ViewID.Illegal;

		if (drawViewer)
			DrawViewerStack.RemoveAt(DrawViewerStack.Count - 1);

		LuaCam.ResetFrameStateChecked();
		ortho.UnReference();
		offCenter.UnReference();
		data.UnReference();
		return 0;
	}

	[LuaFunction]
	static int GetViewSetup(ILuaInterface lua) {
		ref ViewSetup setup = ref view.GetPlayerViewSetup();
		if (lua.GetType(1) != LuaType.Nil && lua.GetBool(1))
			setup = ref view.GetViewSetup();

		LuaTable table = new(null, 0);
		table.SetMemberAngle("angles", setup.Angles);
		table.SetMemberVector("origin", setup.Origin);
		table.SetMember("height", setup.Height);
		table.SetMember("width", setup.Width);
		table.SetMember("x", setup.X);
		table.SetMember("y", setup.Y);
		table.SetMember("znear", setup.ZNear);
		table.SetMember("zfar", setup.ZFar);
		table.SetMember("znearviewmodel", setup.ZNearViewmodel);
		table.SetMember("zfarviewmodel", setup.ZFarViewmodel);
		table.SetMember("fov", setup.FOV);
		table.SetMember("fovviewmodel", setup.FOVViewmodel);
		table.SetMember("fov_unscaled", UnscaleFOVByWidthRatio(setup.FOV, setup.AspectRatio / (4.0f / 3.0f)));
		table.SetMember("fovviewmodel_unscaled", UnscaleFOVByWidthRatio(setup.FOVViewmodel, setup.AspectRatio / (4.0f / 3.0f)));
		table.SetMember("bloomtone", setup.DoBloomAndToneMapping);
		table.SetMember("subrect", setup.RenderToSubrectOfLargerScreen);
		table.SetMember("aspect", setup.AspectRatio);
		table.SetMember("viewid", (int)ViewRender.g_CurrentViewID);

		if (setup.Ortho) {
			LuaTable ortho = new(null, 0);
			ortho.SetMember("left", setup.OrthoLeft);
			ortho.SetMember("right", setup.OrthoRight);
			ortho.SetMember("top", setup.OrthoTop);
			ortho.SetMember("bottom", setup.OrthoBottom);
			table.SetMember("ortho", ortho);
			ortho.UnReference();
		}

		if (setup.OffCenter) {
			LuaTable offCenter = new(null, 0);
			offCenter.SetMember("left", setup.OffCenterLeft);
			offCenter.SetMember("right", setup.OffCenterRight);
			offCenter.SetMember("top", setup.OffCenterTop);
			offCenter.SetMember("bottom", setup.OffCenterBottom);
			table.SetMember("offcenter", offCenter);
			offCenter.UnReference();
		}

		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaFunction]
	static int RenderHUD(ILuaInterface lua) {
		int x = (int)lua.CheckNumber(1);
		int y = (int)lua.CheckNumber(2);
		int w = (int)lua.CheckNumber(3);
		int h = (int)lua.CheckNumber(4);

		using MatRenderContextPtr renderContext = new(materials);
		LuaCam.SetInSurface3D(true);

		ViewSetup hudView = default;
		hudView.X = x;
		hudView.Y = y;
		hudView.Width = w;
		hudView.Height = h;
		hudView.Origin = vec3_origin;
		hudView.Angles = default;
		hudView.RenderToSubrectOfLargerScreen = false;
		hudView.AspectRatio = 0;
		hudView.OffCenter = false;
		hudView.DoBloomAndToneMapping = true;
		hudView.CacheFullSceneState = false;
		hudView.ViewToProjectionOverride = false;
		hudView.Ortho = false;
		hudView.UnscaledHeight = 0;
		render.Push2DView(hudView, 0, renderContext.GetRenderTarget(), view.GetFrustum());

		IPanel? clientDll = enginevgui.GetPanel(VGuiPanelType.ClientDll);
		if (clientDll != null) {
			clientDll.SetPos(hudView.X, hudView.Y);
			clientDll.SetSize(hudView.Width, hudView.Height);
		}

		IPanel? clientDllTools = enginevgui.GetPanel(VGuiPanelType.ClientDllTools);
		if (clientDllTools != null) {
			clientDllTools.SetPos(hudView.X, hudView.Y);
			clientDllTools.SetSize(hudView.Width, hudView.Height);
		}

		render.VGui_Paint(PaintMode.InGamePanels);
		renderContext.Flush(false);
		render.PopView(view.GetFrustum());
		LuaCam.SetInSurface3D(false);
		return 0;
	}

	[LuaFunction]
	static int SetViewPort(ILuaInterface lua) {
		double x = lua.CheckNumber(1);
		double y = lua.CheckNumber(2);
		double w = lua.CheckNumber(3);
		double h = lua.CheckNumber(4);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.Viewport((int)x, (int)y, (int)w, (int)h);
		return 0;
	}
	// todo: Clear
	// todo: ClearDepth
	// todo: RenderView
	// todo: GetViewSetup
	// todo: RenderHUD
	// todo: CopyRenderTargetToTexture
	static int CustomClipPlaneCount;

	[LuaFunction]
	static int PushCustomClipPlane(ILuaInterface lua) {
		ref Vector3 normal = ref LuaVector.Get_Vector(1);
		double distance = lua.CheckNumber(2);
		Span<float> plane = [normal.X, normal.Y, normal.Z, (float)distance];
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.PushCustomClipPlane(plane);
		CustomClipPlaneCount++;
		return 0;
	}

	[LuaFunction]
	static int PopCustomClipPlane(ILuaInterface lua) {
		if (CustomClipPlaneCount < 1) {
			lua.ErrorFromLua("render.PopCustomClipPlane underflow\n");
			return 0;
		}
		CustomClipPlaneCount--;
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.PopCustomClipPlane();
		return 0;
	}

	[LuaFunction]
	static int EnableClipping(ILuaInterface lua) {
		bool enable = lua.GetBool(1);
		using MatRenderContextPtr renderContext = new(materials);
		lua.PushBool(renderContext.EnableClipping(enable));
		return 1;
	}
	[LuaFunction]
	static int SetStencilEnable(ILuaInterface lua) {
		bool enable = lua.GetBool(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetStencilEnable(enable);
		return 0;
	}

	[LuaFunction]
	static int SetStencilFailOperation(ILuaInterface lua) {
		double op = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetStencilFailOperation((StencilOperation)(int)op);
		return 0;
	}

	[LuaFunction]
	static int SetStencilZFailOperation(ILuaInterface lua) {
		double op = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetStencilZFailOperation((StencilOperation)(int)op);
		return 0;
	}

	[LuaFunction]
	static int SetStencilPassOperation(ILuaInterface lua) {
		double op = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetStencilPassOperation((StencilOperation)(int)op);
		return 0;
	}

	[LuaFunction]
	static int SetStencilCompareFunction(ILuaInterface lua) {
		double cmp = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetStencilCompareFunction((StencilComparisonFunction)(int)cmp);
		return 0;
	}

	[LuaFunction]
	static int SetStencilReferenceValue(ILuaInterface lua) {
		double reference = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetStencilReferenceValue((int)reference);
		return 0;
	}

	[LuaFunction]
	static int SetStencilTestMask(ILuaInterface lua) {
		double mask = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetStencilTestMask((uint)(int)mask);
		return 0;
	}

	[LuaFunction]
	static int SetStencilWriteMask(ILuaInterface lua) {
		double mask = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetStencilWriteMask((uint)(int)mask);
		return 0;
	}

	[LuaFunction]
	static int ClearStencilBufferRectangle(ILuaInterface lua) {
		double xmin = lua.CheckNumber(1);
		double ymin = lua.CheckNumber(2);
		double xmax = lua.CheckNumber(3);
		double ymax = lua.CheckNumber(4);
		double value = lua.CheckNumber(5);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.ClearStencilBufferRectangle((int)xmin, (int)ymin, (int)xmax, (int)ymax, (int)value);
		return 0;
	}

	[LuaFunction]
	static int ClearStencil(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.ClearBuffers(false, false, true);
		return 0;
	}
	[LuaFunction]
	static int ClearBuffersObeyStencil(ILuaInterface lua) {
		double r = lua.CheckNumber(1);
		double g = lua.CheckNumber(2);
		double b = lua.CheckNumber(3);
		double a = lua.CheckNumber(4);
		bool depth = lua.GetBool(5);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.ClearColor3ub((byte)(int)r, (byte)(int)g, (byte)(int)b);
		renderContext.ClearColor4ub((byte)(int)r, (byte)(int)g, (byte)(int)b, (byte)(int)a);
		renderContext.ClearBuffersObeyStencil(true, depth);
		return 0;
	}

	[LuaFunction]
	static int PerformFullScreenStencilOperation(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.PerformFullScreenStencilOperation();
		return 0;
	}

	[LuaFunction]
	static int FogMode(ILuaInterface lua) {
		double mode = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.FogMode((MaterialFogMode)(int)mode);
		return 0;
	}

	[LuaFunction]
	static int FogStart(ILuaInterface lua) {
		double start = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.FogStart((float)start);
		return 0;
	}

	[LuaFunction]
	static int FogEnd(ILuaInterface lua) {
		double end = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.FogEnd((float)end);
		return 0;
	}

	[LuaFunction]
	static int SetFogZ(ILuaInterface lua) {
		double fogZ = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetFogZ((float)fogZ);
		return 0;
	}

	[LuaFunction]
	static int GetFogMode(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		lua.PushNumber((int)renderContext.GetFogMode());
		return 1;
	}

	[LuaFunction]
	static int FogColor(ILuaInterface lua) {
		double r = lua.CheckNumber(1);
		double g = lua.CheckNumber(2);
		double b = lua.CheckNumber(3);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.FogColor3ub((byte)(int)r, (byte)(int)g, (byte)(int)b);
		return 0;
	}

	[LuaFunction]
	static int GetFogColor(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.GetFogColor(out Color rgb);
		lua.PushNumber(rgb.R);
		lua.PushNumber(rgb.G);
		lua.PushNumber(rgb.B);
		return 3;
	}

	[LuaFunction]
	static int GetFogDistances(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.GetFogDistances(out float start, out float end, out float fogZ);
		lua.PushNumber(start);
		lua.PushNumber(end);
		lua.PushNumber(fogZ);
		return 3;
	}

	// todo: GetFogMaxDensity
	// todo: FogMaxDensity

	[LuaFunction]
	static int CullMode(ILuaInterface lua) {
		float mode = (int)lua.CheckNumber(1);
		if (mode <= 0)
			mode = 0;
		if (2 <= mode)
			mode = 2;
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.CullMode((MaterialCullMode)(int)mode);
		return 0;
	}

	[LuaFunction]
	static int ResetToneMappingScale(ILuaInterface lua) {
		double scale = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.ResetToneMappingScale((float)scale);
		return 0;
	}

	[LuaFunction]
	static int SetGoalToneMappingScale(ILuaInterface lua) {
		double scale = lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetGoalToneMappingScale((float)scale);
		return 0;
	}

	[LuaFunction]
	static int TurnOnToneMapping(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.TurnOnToneMapping();
		return 0;
	}

	[LuaFunction]
	static int SetToneMappingScaleLinear(ILuaInterface lua) {
		ref Vector3 scale = ref LuaVector.Get_Vector(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetToneMappingScaleLinear(scale);
		return 0;
	}

	[LuaFunction]
	static int GetToneMappingScaleLinear(ILuaInterface lua) {
		using MatRenderContextPtr renderContext = new(materials);
		LuaVector.Push_Vector(renderContext.GetToneMappingScaleLinear());
		return 1;
	}

	// todo: CapturePixels
	// todo: ReadPixel

	[LuaFunction]
	static int OverrideDepthEnable(ILuaInterface lua) {
		bool enable = lua.GetBool(1);
		bool depthEnable = lua.GetBool(2);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.OverrideDepthEnable(enable, depthEnable);
		return 0;
	}

	[LuaFunction]
	static int OverrideAlphaWriteEnable(ILuaInterface lua) {
		bool enable = lua.GetBool(1);
		bool alphaWriteEnable = lua.GetBool(2);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.OverrideAlphaWriteEnable(enable, alphaWriteEnable);
		return 0;
	}

	[LuaFunction]
	static int OverrideColorWriteEnable(ILuaInterface lua) {
		bool enable = lua.GetBool(1);
		bool colorWriteEnable = lua.GetBool(2);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.OverrideColorWriteEnable(enable, colorWriteEnable);
		return 0;
	}

	// todo: OverrideBlend
	// todo: OverrideBlendFunc

	[LuaFunction]
	static int DepthRange(ILuaInterface lua) {
		double zNear = lua.CheckNumber(1);
		double zFar = lua.CheckNumber(2);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.DepthRange((float)zNear, (float)zFar);
		return 0;
	}
	// todo: MaterialOverride
	// todo: MaterialOverrideByIndex
	// todo: DrawSphere
	// todo: DrawWireframeSphere
	// todo: DrawWireframeBox
	// todo: DrawBox
	// todo: DrawLine
	// todo: PushFlashlightMode
	// todo: PopFlashlightMode
	// todo: SetShadowDirection
	// todo: SetShadowColor
	// todo: SetShadowDistance
	// todo: SetShadowsDisabled
	// todo: WorldMaterialOverride
	// todo: BrushMaterialOverride
	// todo: ModelMaterialOverride
	[LuaFunction]
	static int SetLightingMode(ILuaInterface lua) {
		int mode = (int)lua.CheckNumber(1);
		using MatRenderContextPtr renderContext = new(materials);
		if (mode < 0)
			mode = 0;
		else if (mode > 2)
			mode = 2;
		renderContext.SetIntRenderingParameter(0, mode);
		return 0;
	}
	// todo: Capture
	static readonly List<int> FilterMinStack = [0];
	static readonly List<int> FilterMagStack = [0];

	[LuaFunction]
	static int PushFilterMag(ILuaInterface lua) {
		RenderTextureReference.Shutdown();
		int mode = Math.Max(Math.Min((int)lua.CheckNumber(1), 8), 0);
		if (FilterMagStack.Count > 200) {
			lua.ErrorFromLua("render.PushFilterMag overflow\n");
			return 0;
		}

		using MatRenderContextPtr renderContext = new(materials);
		FilterMagStack.Add(mode);
		renderContext.GMOD_ForceFilterMode(false, FilterMagStack[^1]);
		return 0;
	}

	[LuaFunction]
	static int SetScissorRect(ILuaInterface lua) {
		int left = (int)lua.CheckNumber(1);
		int top = (int)lua.CheckNumber(2);
		int right = (int)lua.CheckNumber(3);
		int bottom = (int)lua.CheckNumber(4);
		bool enable = lua.GetBool(5);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetScissorRect(left, top, right, bottom, enable);
		return 0;
	}

	[LuaFunction]
	static int PopFilterMag(ILuaInterface lua) {
		RenderTextureReference.Shutdown();
		if (FilterMagStack.Count <= 1) {
			lua.ErrorFromLua("render.PopFilterMag underflow!\n");
			return 0;
		}

		using MatRenderContextPtr renderContext = new(materials);
		FilterMagStack.RemoveAt(FilterMagStack.Count - 1);
		renderContext.GMOD_ForceFilterMode(false, FilterMagStack[^1]);
		return 0;
	}

	[LuaFunction]
	static int PushFilterMin(ILuaInterface lua) {
		RenderTextureReference.Shutdown();
		int mode = Math.Max(Math.Min((int)lua.CheckNumber(1), 8), 0);
		if (FilterMinStack.Count > 200) {
			lua.ErrorFromLua("render.PushFilterMin overflow\n");
			return 0;
		}

		using MatRenderContextPtr renderContext = new(materials);
		FilterMinStack.Add(mode);
		renderContext.GMOD_ForceFilterMode(true, FilterMinStack[^1]);
		return 0;
	}

	[LuaFunction]
	static int PopFilterMin(ILuaInterface lua) {
		RenderTextureReference.Shutdown();
		if (FilterMinStack.Count <= 1) {
			lua.ErrorFromLua("render.PopFilterMin underflow!\n");
			return 0;
		}

		using MatRenderContextPtr renderContext = new(materials);
		FilterMinStack.RemoveAt(FilterMinStack.Count - 1);
		renderContext.GMOD_ForceFilterMode(true, FilterMinStack[^1]);
		return 0;
	}
	// todo: RedownloadAllLightmaps
	[LuaFunction]
	static int SetWriteDepthToDestAlpha(ILuaInterface lua) {
		bool enable = lua.GetBool(1);
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.SetIntRenderingParameter(10, enable ? 1 : 0);
		return 0;
	}
	// todo: RenderFlashlights
	[LuaFunction]
	static int ComputePixelDiameterOfSphere(ILuaInterface lua) {
		ref Vector3 origin = ref LuaVector.Get_Vector(1);
		double radius = lua.CheckNumber(2);
		using MatRenderContextPtr renderContext = new(materials);
		lua.PushNumber(renderContext.ComputePixelDiameterOfSphere(origin, (float)radius));
		return 1;
	}

	[LuaFunction]
	static int IsTakingScreenshot(ILuaInterface lua) {
		lua.PushBool(engine.IsTakingScreenshot());
		return 1;
	}
}
