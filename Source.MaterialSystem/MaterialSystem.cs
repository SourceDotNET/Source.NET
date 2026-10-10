using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;
using Source.MaterialSystem.Surface;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.MaterialSystem;

public static class MatSysBootstrap
{

	public static T WithFullMaterialSystem<T>(this T services) where T : IServiceCollection {
		services.AddSingleton<IMaterialSystem, MaterialSystem>();
		services.AddSingleton<MatSystemSurface>();
		services.AddSingleton<IMatSystemSurface>(x => x.GetRequiredService<MatSystemSurface>());
		services.AddSingleton<ISurface>(x => x.GetRequiredService<MatSystemSurface>());
		services.AddSingleton<ITextureManager, TextureManager>();
		services.AddSingleton<MaterialSystem_Config>();
		return services;
	}

	public static T WithStubMaterialSystem<T>(this T services) where T : IServiceCollection {
		services.AddSingleton<IMaterialSystem, DummyMaterialSystem>();
		services.AddSingleton<IMaterialSystemHardwareConfig, DummyHardwareConfig>();
		return services;
	}

}

public class MaterialSystem : IMaterialSystemInternal, IShaderUtil
{
	public readonly MaterialDict MaterialDict;
	nint graphics;

	public IShaderUtil GetShaderUtil() => this;

	readonly IServiceProvider services;

	public readonly IFileSystem FileSystem;
	public readonly TextureManager TextureSystem;
	public readonly IShaderSystem ShaderSystem;
	public IShaderDevice ShaderDevice;
	public IShaderAPI ShaderAPI;
	public readonly IMeshMgr MeshMgr;
	public readonly IMaterialSystemHardwareConfig HardwareConfig;
	public readonly MaterialSystem_Config Config;

	readonly static ConVar mat_vsync = new("mat_vsync", "0", 0, "Force sync to vertical retrace", 0, 1);
	readonly static ConVar mat_forcehardwaresync = new(IsPC() ? "1" : "0", 0);
	readonly static ConVar mat_trilinear = new("1", 0);
	readonly static ConVar mat_forceaniso = new("16", FCvar.Archive); // 0 = Bilinear, 1 = Trilinear, 2+ = Aniso
	readonly static ConVar mat_filterlightmaps = new("1", 0);
	readonly static ConVar mat_filtertextures = new("1", 0);
	readonly static ConVar mat_mipmaptextures = new("1", 0);
	readonly static ConVar mat_vrmode_adapter = new("-1", 0);
	readonly static ConVar mat_showmiplevels = new("0", FCvar.Cheat, "color-code miplevels 2: normalmaps, 1: everything else"); // , callback: mat_showmiplevels_Callback_f
	readonly static ConVar mat_specular = new("1", 0, "Enable/Disable specularity for perf testing.  Will cause a material reload upon change.");
	readonly static ConVar mat_bumpmap = new("1", 0);
	readonly static ConVar mat_phong = new("1", 0);
	readonly static ConVar mat_parallaxmap = new("1", FCvar.Hidden | 0);
	readonly static ConVar mat_reducefillrate = new("0", 0);
	readonly static ConVar mat_picmip = new("0", FCvar.Archive, "", -1, 4);
	readonly static ConVar mat_slopescaledepthbias_normal = new("0.0f", FCvar.Cheat);
	readonly static ConVar mat_depthbias_normal = new("0.0f", FCvar.Cheat | 0);
	readonly static ConVar mat_slopescaledepthbias_decal = new("-0.5", FCvar.Cheat); // Reciprocals of these biases sent to API
	readonly static ConVar mat_depthbias_decal = new("-262144", FCvar.Cheat | 0);
	readonly static ConVar mat_slopescaledepthbias_shadowmap = new("16", FCvar.Cheat);
	readonly static ConVar mat_depthbias_shadowmap = new("0.0005", FCvar.Cheat);
	readonly static ConVar mat_monitorgamma = new("2.2", FCvar.Archive, "monitor gamma (typically 2.2 for CRT and 1.7 for LCD)", 1.6f, 2.6f);
	readonly static ConVar mat_monitorgamma_tv_range_min = new("16", 0);
	readonly static ConVar mat_monitorgamma_tv_range_max = new("255", 0);
	readonly static ConVar mat_monitorgamma_tv_exp = new("2.5", 0, "", 1.0f, 4.0f);
	readonly static ConVar mat_monitorgamma_tv_enabled = new("0", FCvar.Archive, "");
	readonly static ConVar mat_antialias = new("0", FCvar.Archive);
	readonly static ConVar mat_aaquality = new("0", FCvar.Archive);
	readonly static ConVar mat_diffuse = new("1", FCvar.Cheat);
	readonly static ConVar mat_showlowresimage = new("0", FCvar.Cheat);
	readonly static ConVar mat_fullbright = new("0", FCvar.Cheat);
	readonly static ConVar mat_normalmaps = new("0", FCvar.Cheat);
	readonly static ConVar mat_measurefillrate = new("0", FCvar.Cheat);
	readonly static ConVar mat_fillrate = new("0", FCvar.Cheat);
	readonly static ConVar mat_reversedepth = new("0", FCvar.Cheat);
	readonly static ConVar mat_bufferprimitives = new("1", 0);
	readonly static ConVar mat_drawflat = new("0", FCvar.Cheat);
	readonly static ConVar mat_softwarelighting = new("0", 0);
	readonly static ConVar mat_proxy = new("0", FCvar.Cheat, ""); // , callback: MatProxyCallback
	readonly static ConVar mat_norendering = new("0", FCvar.Cheat);
	readonly static ConVar mat_compressedtextures = new("1", 0);
	readonly static ConVar mat_fastspecular = new("1", 0, "Enable/Disable specularity for visual testing.  Will not reload materials and will not affect perf.");
	readonly static ConVar mat_fastnobump = new("0", FCvar.Cheat); // Binds 1-texel normal map for quick internal testing

	// These are not controlled by the material system, but are limited by settings in the material system
	readonly static ConVar r_shadowrendertotexture = new("0", FCvar.Archive);
	readonly static ConVar r_flashlightdepthtexture = new("1", 0);
	readonly static ConVar r_waterforceexpensive = new("0", FCvar.Archive);
	readonly static ConVar r_waterforcereflectentities = new("0", 0);
	readonly static ConVar mat_motion_blur_enabled = new("0", FCvar.Archive);

	public MaterialSystem(IServiceProvider services) {
		MaterialDict = new(this);
		this.services = services;

		FileSystem = services.GetRequiredService<IFileSystem>();
		ShaderAPI = services.GetRequiredService<IShaderAPI>()!;
		ShaderDevice = services.GetRequiredService<IShaderDevice>();
		TextureSystem = (services.GetRequiredService<ITextureManager>() as TextureManager)!;
		MeshMgr = services.GetRequiredService<IMeshMgr>(); // todo: interface
		HardwareConfig = services.GetRequiredService<IMaterialSystemHardwareConfig>(); // todo: interface
		ShaderSystem = services.GetRequiredService<IShaderSystem>();
		Config = services.GetRequiredService<MaterialSystem_Config>()!;
		HardwareRenderContext = new(this);

		// Link up
		ShaderDevice.PreInit(this, services);

		TextureSystem.MaterialSystem = this;

		ShaderSystem.LoadAllShaderDLLs();
		TextureSystem.Init();
		ShaderSystem.Init();
		CreateDebugMaterials();
		MatLightmaps = new(this);
	}

	ILauncherManager launcherMgr;


	public void ModInit() {
		launcherMgr = services.GetRequiredService<ILauncherManager>();

		GenerateConfigFromConfigKeyValues(Config, false);
		UpdateConfig(false);
	}

	public void GenerateConfigFromConfigKeyValues(MaterialSystem_Config config, bool overwriteCommandLineValues) {
		config.Flags = 0;

		launcherMgr.GetNativeDisplayInfo(-1, out uint width, out uint height, out uint refreshHz);
		config.VideoMode.Width = (int)width;
		config.VideoMode.Height = (int)height;
	}

	public bool UpdateConfig(bool forceUpdate) {
		MaterialSystem_Config config = new();
		Config.CopyInstantiatedReferenceTo(config);
		ReadConfigFromConVars(config);
		return OverrideConfig(config, forceUpdate);
	}

	public bool OverrideConfig(MaterialSystem_Config config, bool forceUpdate) {
		bool redownloadLightmaps = false;
		bool redownloadTextures = false;
		bool recomputeSnapshots = false;
		bool reloadMaterials = false;
		bool resetAnisotropy = false;
		bool setStandardVertexShaderConstants = false;
		bool monitorGammaChanged = false;
		bool videoModeChange = false;
		bool resetTextureFilter = false;


		if (!ShaderDevice.IsUsingGraphics()) {
			// Config = config;
			config.CopyInstantiatedReferenceTo(Config);

			ColorSpace.SetGamma(2.2f, 2.2f, IMaterialSystem.OVERBRIGHT, Config.AllowCheats, false);

			return redownloadLightmaps;
		}

		ShaderAPI.SetDefaultState();

		// if (config.HDREnabled() != Config.HDREnabled()) {
		// 	forceUpdate = true;
		// 	reloadMaterials = true;
		// }

		if (config.ShadowDepthTexture != Config.ShadowDepthTexture) {
			forceUpdate = true;
			reloadMaterials = true;
			recomputeSnapshots = true;
		}

		// Don't use compressed textures for the moment if we don't support them
		if (!HardwareConfig.SupportsCompressedTextures())
			config.CompressedTextures = false;

		if (forceUpdate) {
			MatLightmaps.EnableLightmapFiltering(config.FilterLightmaps);
			recomputeSnapshots = true;
			redownloadLightmaps = true;
			redownloadTextures = true;
			resetAnisotropy = true;
			setStandardVertexShaderConstants = true;
		}

		// toggle bump mapping
		if (config.DisableSpecular() != Config.DisableSpecular() || config.DisablePhong() != Config.DisablePhong()) {
			recomputeSnapshots = true;
			reloadMaterials = true;
			resetAnisotropy = true;
		}

		// toggle specularity
		if (config.DisableSpecular() != Config.DisableSpecular()) {
			recomputeSnapshots = true;
			reloadMaterials = true;
			resetAnisotropy = true;
		}

		// toggle parallax mapping
		if (config.EnableParallaxMapping() != Config.EnableParallaxMapping())
			reloadMaterials = true;

		// Reload materials if we want reduced fillrate
		if (config.ReduceFillrate() != Config.ReduceFillrate())
			reloadMaterials = true;

		// toggle reverse depth
		if (config.ReverseDepth != Config.ReverseDepth) {
			recomputeSnapshots = true;
			resetAnisotropy = true;
		}

		// toggle no transparency
		if (config.NoTransparency != Config.NoTransparency) {
			recomputeSnapshots = true;
			resetAnisotropy = true;
		}

		// toggle lightmap filtering
		if (config.FilterLightmaps != Config.FilterLightmaps)
			MatLightmaps.EnableLightmapFiltering(config.FilterLightmaps);

		// toggle software lighting
		if (config.SoftwareLighting != Config.SoftwareLighting)
			reloadMaterials = true;

		// generic things that cause us to redownload textures
		if (config.AllowCheats != Config.AllowCheats ||
				config.SkipMipLevels != Config.SkipMipLevels ||
				config.ShowMipLevels != Config.ShowMipLevels ||
				((config.CompressedTextures != Config.CompressedTextures) && HardwareConfig.SupportsCompressedTextures()) ||
				config.ShowLowResImage != Config.ShowLowResImage) {

			redownloadTextures = true;
			recomputeSnapshots = true;
			resetAnisotropy = true;
		}

		if (config.ForceTrilinear() != Config.ForceTrilinear())
			resetTextureFilter = true;

		if (config.ForceAnisotropicLevel != Config.ForceAnisotropicLevel) {
			resetAnisotropy = true;
			resetTextureFilter = true;
		}

		if (config.MonitorGamma != Config.MonitorGamma || config.GammaTVRangeMin != Config.GammaTVRangeMin ||
				config.GammaTVRangeMax != Config.GammaTVRangeMax || config.GammaTVExponent != Config.GammaTVExponent ||
				config.GammaTVEnabled != Config.GammaTVEnabled)
			monitorGammaChanged = true;

		if (config.VideoMode.Width != Config.VideoMode.Width ||
				config.VideoMode.Height != Config.VideoMode.Height ||
				config.VideoMode.RefreshRate != Config.VideoMode.RefreshRate ||
				config.AASamples != Config.AASamples ||
				config.AAQuality != Config.AAQuality ||
				config.Windowed() != Config.Windowed() ||
				config.NoWindowBorder() != Config.NoWindowBorder() ||
				config.Stencil() != Config.Stencil())
			videoModeChange = true;

		if (!config.Windowed() && (config.WaitForVSync() != Config.WaitForVSync()))
			videoModeChange = true;

		config.CopyInstantiatedReferenceTo(Config);

		if (redownloadTextures || redownloadLightmaps)
			ColorSpace.SetGamma(2.2f, 2.2f, IMaterialSystem.OVERBRIGHT, Config.AllowCheats, false);

		if (resetAnisotropy || recomputeSnapshots || redownloadLightmaps ||
				redownloadTextures || resetAnisotropy || videoModeChange ||
				setStandardVertexShaderConstants || resetTextureFilter) {
			// ForceSingleThreaded();
		}

		if (reloadMaterials)
			ReloadMaterials();

		if (resetAnisotropy)
			ShaderAPI.SetAnisotropicLevel(config.ForceAnisotropicLevel);

		if (redownloadTextures) {
			if (ShaderAPI.CanDownloadTextures()) {
				TextureSystem.RestoreRenderTargets();
				TextureSystem.RestoreNonRenderTargetTextures();
			}
		}
		else if (resetTextureFilter) {
			TextureSystem.ResetTextureFilteringState();
		}

		// Recompute all state snapshots
		if (recomputeSnapshots)
			RecomputeAllStateSnapshots();

		// if (setStandardVertexShaderConstants)
		// ShaderAPI.SetStandardVertexShaderConstants(IMaterialSystem.OVERBRIGHT);

		if (monitorGammaChanged) {
			// 	ShaderDevice.SetHardwareGammaRamp(config.MonitorGamma, config.GammaTVRangeMin, config.GammaTVRangeMax, config.GammaTVExponent, config.GammaTVEnabled);
		}

		if (videoModeChange) {
			ConvertModeStruct(config, out ShaderDeviceInfo info);
			ShaderAPI.ChangeVideoMode(info);

			if (ShaderAPI.CanDownloadTextures())
				TextureSystem.RestoreRenderTargets();
		}

		// if (videoModeChange)
		// ForceSingleThreaded();

		return redownloadLightmaps;
	}

	private void ReadConfigFromConVars(MaterialSystem_Config config) {
		config.SetFlag(MaterialSystem_Config_Flags.NoWaitForVSync, !mat_vsync.GetBool());
		config.SetFlag(MaterialSystem_Config_Flags.ForceTrilinear, mat_trilinear.GetBool());
		config.SetFlag(MaterialSystem_Config_Flags.DisableSpecular, !mat_specular.GetBool());
		config.SetFlag(MaterialSystem_Config_Flags.DisableBumpmap, !mat_bumpmap.GetBool());
		config.SetFlag(MaterialSystem_Config_Flags.DisableBumpmap, !mat_phong.GetBool());
		config.SetFlag(MaterialSystem_Config_Flags.EnableParallaxMapping, mat_parallaxmap.GetBool());
		config.SetFlag(MaterialSystem_Config_Flags.ReduceFillrate, mat_reducefillrate.GetBool());
		config.ForceAnisotropicLevel = Math.Max(mat_forceaniso.GetInt(), 1);
		config.SkipMipLevels = mat_picmip.GetInt();
		config.SetFlag(MaterialSystem_Config_Flags.ForceHardwareSync, mat_forcehardwaresync.GetBool());
		config.SlopeScaleDepthBias_Decal = mat_slopescaledepthbias_decal.GetFloat();
		config.DepthBias_Decal = mat_depthbias_decal.GetFloat();
		config.SlopeScaleDepthBias_Normal = mat_slopescaledepthbias_normal.GetFloat();
		config.DepthBias_Normal = mat_depthbias_normal.GetFloat();
		config.SlopeScaleDepthBias_ShadowMap = mat_slopescaledepthbias_shadowmap.GetFloat();
		config.DepthBias_ShadowMap = mat_depthbias_shadowmap.GetFloat();
		config.MonitorGamma = mat_monitorgamma.GetFloat();
		config.GammaTVRangeMin = mat_monitorgamma_tv_range_min.GetFloat();
		config.GammaTVRangeMax = mat_monitorgamma_tv_range_max.GetFloat();
		config.GammaTVExponent = mat_monitorgamma_tv_exp.GetFloat();
		config.GammaTVEnabled = mat_monitorgamma_tv_enabled.GetBool();
		config.AASamples = mat_antialias.GetInt();
		config.AAQuality = mat_aaquality.GetInt();
		config.ShowDiffuse = mat_diffuse.GetBool();
		config.ShowNormalMap = mat_normalmaps.GetBool();
		config.ShowLowResImage = mat_showlowresimage.GetBool();
		config.MeasureFillRate = mat_measurefillrate.GetBool();
		config.VisualizeFillRate = mat_fillrate.GetBool();
		config.FilterLightmaps = mat_filterlightmaps.GetBool();
		config.FilterTextures = mat_filtertextures.GetBool();
		config.MipMapTextures = mat_mipmaptextures.GetBool();
		config.ShowMipLevels = (sbyte)mat_showmiplevels.GetInt();
		config.ReverseDepth = mat_reversedepth.GetBool();
		config.BufferPrimitives = mat_bufferprimitives.GetBool();
		config.DrawFlat = mat_drawflat.GetBool();
		config.SoftwareLighting = mat_softwarelighting.GetBool();
		config.ProxiesTestMode = (byte)mat_proxy.GetInt();
		config.SuppressRendering = mat_norendering.GetInt() != 0;
		config.CompressedTextures = mat_compressedtextures.GetBool();
		config.ShowSpecular = mat_fastspecular.GetBool();
		config.Fullbright = (byte)mat_fullbright.GetInt();
		config.FastNoBump = mat_fastnobump.GetInt() != 0;
		config.MotionBlur = mat_motion_blur_enabled.GetBool();
		config.SupportFlashlight = true; // mat_supportflashlight.GetBool();
		config.ShadowDepthTexture = r_flashlightdepthtexture.GetBool();
		config.SetFlag(MaterialSystem_Config_Flags.ENABLE_HDR, HardwareConfig.GetHDREnabled());
	}

	public uint GetDisplayAdapterCount() {
		throw new NotImplementedException();
	}

	public uint GetCurrentAdapter() => (uint)ShaderDevice.GetCurrentAdapter();

	public void ModShutdown() {

	}


	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	unsafe static void raylibSpew(int logLevel, sbyte* text, sbyte* args) {
		//var message = Logging.GetLogMessage((nint)text, (nint)args);

		/*Dbg._SpewMessage((TraceLogLevel)logLevel switch {
			TraceLogLevel.Info => SpewType.Message,
			TraceLogLevel.Trace => SpewType.Log,
			TraceLogLevel.Debug => SpewType.Message,
			TraceLogLevel.Warning => SpewType.Warning,
			TraceLogLevel.Error => SpewType.Warning,
			TraceLogLevel.Fatal => SpewType.Error,
			_ => SpewType.Message,
		}, "raylib", 1, new Color(255, 255, 255), message + "\n");*/
	}

	public uint DebugVarsSignature;

	public ITexture GetErrorTexture() => TextureSystem.ErrorTexture();
	public void BeginFrame(double frameTime) {
		if (!ThreadInMainThread() || IsInFrame())
			return;


		DebugVarsSignature = (uint)(
			((mat_specular.GetInt() != 0) ? 1 : 0)
			+ (mat_normalmaps.GetInt() << 1)
			+ (mat_fullbright.GetInt() << 2)
		);

		var renderContext = GetRenderContextInternal();

		renderContext.MarkRenderDataUnused(true);
		renderContext.BeginFrame();
		renderContext.SetFrameTime(frameTime);

		Assert(!InFrame);
		InFrame = true;
	}

	bool InFrame = false;


	private Matrix4x4 GetScreenMatrix() {
		launcherMgr.DisplayedSize(out int screenWidth, out _);
		int renderWidth = 0, renderHeight = 0;
		launcherMgr.RenderedSize(false, ref renderWidth, ref renderHeight);
		float scaleRatio = (float)renderWidth / (float)screenWidth;
		MathLib.MatrixBuildScale(out Matrix4x4 m, scaleRatio, scaleRatio, 1);
		return m;
	}

	public bool IsInFrame() => InFrame;

	public void EndFrame() {
		if (!ThreadInMainThread() || !IsInFrame())
			return;

		GetRenderContextInternal().EndFrame();

		Assert(InFrame);
		InFrame = false;
	}

	ulong FrameNum;

	public void SwapBuffers() {
		GetRenderContextInternal().SwapBuffers();
		FrameNum++;
	}

	public MaterialSystem_Config GetCurrentConfigForVideoCard() => Config;

	readonly ThreadLocal<IMatRenderContextInternal> RenderContext = new();
	readonly MatRenderContext HardwareRenderContext;

	public IMatRenderContext GetRenderContext() {
		IMatRenderContext? result = RenderContext.Value;
		if (result == null) {
			result = HardwareRenderContext;
			RenderContext.Value = HardwareRenderContext;
		}
		return result;
	}

	public bool SetMode(IWindow window, MaterialSystem_Config config) {
		MaterialSystem MaterialSystem = (MaterialSystem)Singleton<IMaterialSystem>();
		TextureManager TextureManager = (TextureManager)Singleton<ITextureManager>();

		ShaderDeviceInfo info;
		ConvertModeStruct(config, out info);

		bool previouslyUsingGraphics = ShaderDevice.IsUsingGraphics();

		bool bOk = ShaderAPI.SetMode(window, GetCurrentAdapter(), info);
		if (!bOk)
			return false;

		int width = info.DisplayMode.Width;
		int height = info.DisplayMode.Height;
		launcherMgr.RenderedSize(true, ref width, ref height);

		TextureManager.FreeStandardRenderTargets();
		TextureManager.AllocateStandardRenderTargets();
		if (!previouslyUsingGraphics) {
			if (IsPC()) {
				TextureManager.RestoreRenderTargets();
				TextureManager.RestoreNonRenderTargetTextures();
				if (MaterialSystem.CanUseEditorMaterials())
					MathLib.Init(2.2f, 2.2f, 0.0f, (int)IMaterialSystem.OVERBRIGHT);

				AllocateStandardTextures();
			}
		}

		// Copy over that state which isn't stored currently in convars
		Config.VideoMode = config.VideoMode;
		Config.SetFlag(MaterialSystem_Config_Flags.Windowed, config.Windowed());
		Config.SetFlag(MaterialSystem_Config_Flags.Stencil, config.Stencil());
		Config.SetFlag(MaterialSystem_Config_Flags.VRMode, config.VRMode());
		// WriteConfigIntoConVars(config);

		return true;
	}

	public void AddModeChangeCallBack(ModeChangeCallbackFunc func) => ShaderDevice.AddModeChangeCallBack(func);


	ShaderAPITextureHandle_t FullbrightLightmapTextureHandle;
	ShaderAPITextureHandle_t FullbrightBumpedLightmapTextureHandle;
	ShaderAPITextureHandle_t BlackTextureHandle;
	ShaderAPITextureHandle_t FlatNormalTextureHandle;
	ShaderAPITextureHandle_t GreyTextureHandle;
	ShaderAPITextureHandle_t GreyAlphaZeroTextureHandle;
	ShaderAPITextureHandle_t WhiteTextureHandle;
	ShaderAPITextureHandle_t LinearToGammaTableTextureHandle;
	ShaderAPITextureHandle_t LinearToGammaIdentityTableTextureHandle;
	ShaderAPITextureHandle_t MaxDepthTextureHandle;
	bool StandardTexturesAllocated = false;

	private void AllocateStandardTextures() {
		if (StandardTexturesAllocated)
			return;

		StandardTexturesAllocated = true;

		float nominal_lightmap_value = 1.0f;
		if (HardwareConfig.GetHDRType() == HDRType.Integer)
			nominal_lightmap_value = 1.0f / 16.0f;

		Span<byte> texel = stackalloc byte[4];
		texel[3] = 255;

		CreateTextureFlags tcFlags = CreateTextureFlags.Managed;
		CreateTextureFlags tcFlagsSRGB = CreateTextureFlags.Managed | CreateTextureFlags.SRGB;


		FullbrightLightmapTextureHandle = ShaderAPI.CreateTexture(1, 1, 1, ImageFormat.BGRX8888, 1, 1, (int)tcFlags, "[FULLBRIGHT_LIGHTMAP_TEXID]", TEXTURE_GROUP_LIGHTMAP);
		ShaderAPI.ModifyTexture(FullbrightLightmapTextureHandle);
		ShaderAPI.TexMinFilter(TexFilterMode.Linear);
		ShaderAPI.TexMagFilter(TexFilterMode.Linear);
		Span<float> tmpVect = [nominal_lightmap_value, nominal_lightmap_value, nominal_lightmap_value];
		ColorSpace.LinearToLightmap(texel, tmpVect);
		ShaderAPI.TexImage2D(0, 0, ImageFormat.BGRX8888, 0, 1, 1, ImageFormat.BGRX8888, false, texel);

		BlackTextureHandle = ShaderAPI.CreateTexture(1, 1, 1, ImageFormat.BGRX8888, 1, 1, (int)tcFlagsSRGB, "[BLACK_TEXID]", TEXTURE_GROUP_OTHER);
		ShaderAPI.ModifyTexture(BlackTextureHandle);
		ShaderAPI.TexMinFilter(TexFilterMode.Linear);
		ShaderAPI.TexMagFilter(TexFilterMode.Linear);
		texel[0] = texel[1] = texel[2] = 0;
		ShaderAPI.TexImage2D(0, 0, ImageFormat.BGRX8888, 0, 1, 1, ImageFormat.BGRX8888, false, texel);
		ShaderAPI.SetStandardTextureHandle(StandardTextureId.Black, BlackTextureHandle);

		WhiteTextureHandle = ShaderAPI.CreateTexture(1, 1, 1, ImageFormat.BGRX8888, 1, 1, (int)tcFlagsSRGB, "[WHITE_TEXID]", TEXTURE_GROUP_OTHER);
		ShaderAPI.ModifyTexture(WhiteTextureHandle);
		ShaderAPI.TexMinFilter(TexFilterMode.Linear);
		ShaderAPI.TexMagFilter(TexFilterMode.Linear);
		texel[0] = texel[1] = texel[2] = 255;
		ShaderAPI.TexImage2D(0, 0, ImageFormat.BGRX8888, 0, 1, 1, ImageFormat.BGRX8888, false, texel);
		ShaderAPI.SetStandardTextureHandle(StandardTextureId.White, WhiteTextureHandle);

		GreyTextureHandle = ShaderAPI.CreateTexture(1, 1, 1, ImageFormat.BGRX8888, 1, 1, (int)tcFlagsSRGB, "[GREY_TEXID]", TEXTURE_GROUP_OTHER);
		ShaderAPI.ModifyTexture(GreyTextureHandle);
		ShaderAPI.TexMinFilter(TexFilterMode.Linear);
		ShaderAPI.TexMagFilter(TexFilterMode.Linear);
		texel[0] = texel[1] = texel[2] = 128;
		texel[3] = 255;
		ShaderAPI.TexImage2D(0, 0, ImageFormat.BGRX8888, 0, 1, 1, ImageFormat.BGRX8888, false, texel);
		ShaderAPI.SetStandardTextureHandle(StandardTextureId.Grey, GreyTextureHandle);

		GreyAlphaZeroTextureHandle = ShaderAPI.CreateTexture(1, 1, 1, ImageFormat.RGBA8888, 1, 1, (int)tcFlagsSRGB, "[GREYALPHAZERO_TEXID]", TEXTURE_GROUP_OTHER);
		ShaderAPI.ModifyTexture(GreyAlphaZeroTextureHandle);
		ShaderAPI.TexMinFilter(TexFilterMode.Linear);
		ShaderAPI.TexMagFilter(TexFilterMode.Linear);
		texel[0] = texel[1] = texel[2] = 128;
		texel[3] = 0;
		ShaderAPI.TexImage2D(0, 0, ImageFormat.RGBA8888, 0, 1, 1, ImageFormat.RGBA8888, false, texel);
		texel[3] = 255;
		ShaderAPI.SetStandardTextureHandle(StandardTextureId.GreyAlphaZero, GreyAlphaZeroTextureHandle);

		FlatNormalTextureHandle = ShaderAPI.CreateTexture(1, 1, 1, ImageFormat.BGRX8888, 1, 1, (int)tcFlags, "[FLAT_NORMAL_TEXTURE]", TEXTURE_GROUP_OTHER);
		ShaderAPI.ModifyTexture(FlatNormalTextureHandle);
		ShaderAPI.TexMinFilter(TexFilterMode.Linear);
		ShaderAPI.TexMagFilter(TexFilterMode.Linear);
		texel[0] = 255;
		texel[1] = 127;
		texel[2] = 127;
		ShaderAPI.TexImage2D(0, 0, ImageFormat.BGRX8888, 0, 1, 1, ImageFormat.BGRX8888, false, texel);
		ShaderAPI.SetStandardTextureHandle(StandardTextureId.NormalMapFlat, FlatNormalTextureHandle);

		FullbrightBumpedLightmapTextureHandle = ShaderAPI.CreateTexture(1, 1, 1, ImageFormat.BGRX8888, 1, 1, (int)tcFlags, "[FULLBRIGHT_BUMPED_LIGHTMAP_TEXID]", TEXTURE_GROUP_LIGHTMAP);
		ShaderAPI.ModifyTexture(FullbrightBumpedLightmapTextureHandle);
		ShaderAPI.TexMinFilter(TexFilterMode.Linear);
		ShaderAPI.TexMagFilter(TexFilterMode.Linear);
		Span<float> linearColor = [nominal_lightmap_value, nominal_lightmap_value, nominal_lightmap_value];
		Span<byte> dummy = stackalloc byte[3];
		ColorSpace.LinearToBumpedLightmap(linearColor, linearColor, linearColor, linearColor, dummy, texel, dummy, dummy);
		ShaderAPI.TexImage2D(0, 0, ImageFormat.BGRX8888, 0, 1, 1, ImageFormat.BGRX8888, false, texel);
		ShaderAPI.SetStandardTextureHandle(StandardTextureId.LightmapBumpedFullbright, FullbrightBumpedLightmapTextureHandle);

		{
			CreateTextureFlags iGammaLookupFlags = tcFlags;
			ImageFormat gammalookupfmt;
			gammalookupfmt = ImageFormat.I8;

			{
				const int LINEAR_TO_GAMMA_TABLE_WIDTH = 512;
				LinearToGammaTableTextureHandle = ShaderAPI.CreateTexture(LINEAR_TO_GAMMA_TABLE_WIDTH, 1, 1, gammalookupfmt, 1, 1, (int)iGammaLookupFlags, "[LINEAR_TO_GAMMA_LOOKUP_SRGBON_TEXID]", TEXTURE_GROUP_PIXEL_SHADERS);
				ShaderAPI.ModifyTexture(LinearToGammaTableTextureHandle);
				ShaderAPI.TexMinFilter(TexFilterMode.Linear);
				ShaderAPI.TexMagFilter(TexFilterMode.Linear);
				ShaderAPI.TexWrap(TexCoordComponent.S, TexWrapMode.Clamp);
				ShaderAPI.TexWrap(TexCoordComponent.T, TexWrapMode.Clamp);
				ShaderAPI.TexWrap(TexCoordComponent.U, TexWrapMode.Clamp);

				Span<float> pixelData = stackalloc float[LINEAR_TO_GAMMA_TABLE_WIDTH];
				for (int i = 0; i != LINEAR_TO_GAMMA_TABLE_WIDTH; ++i) {
					float fLookupResult = ((float)i) / ((float)(LINEAR_TO_GAMMA_TABLE_WIDTH - 1));
					fLookupResult = ShaderAPI.LinearToGamma_HardwareSpecific(fLookupResult);

					fLookupResult = ShaderAPI.LinearToGamma_HardwareSpecific(fLookupResult);

					int iColor = MathLib.RoundFloatToInt(fLookupResult * 255.0f);
					if (iColor > 255)
						iColor = 255;

					pixelData.Cast<float, byte>()[i] = (byte)iColor;
				}

				ShaderAPI.TexImage2D(0, 0, gammalookupfmt, 0, LINEAR_TO_GAMMA_TABLE_WIDTH, 1, gammalookupfmt, false, pixelData.Cast<float, byte>());
			}

			// generate the identity conversion table texture.
			{
				const int LINEAR_TO_GAMMA_IDENTITY_TABLE_WIDTH = 256;
				LinearToGammaIdentityTableTextureHandle = ShaderAPI.CreateTexture(LINEAR_TO_GAMMA_IDENTITY_TABLE_WIDTH, 1, 1, gammalookupfmt, 1, 1, (int)tcFlags, "[LINEAR_TO_GAMMA_LOOKUP_SRGBOFF_TEXID]", TEXTURE_GROUP_PIXEL_SHADERS);
				ShaderAPI.ModifyTexture(LinearToGammaIdentityTableTextureHandle);
				ShaderAPI.TexMinFilter(TexFilterMode.Linear);
				ShaderAPI.TexMagFilter(TexFilterMode.Linear);
				ShaderAPI.TexWrap(TexCoordComponent.S, TexWrapMode.Clamp);
				ShaderAPI.TexWrap(TexCoordComponent.T, TexWrapMode.Clamp);
				ShaderAPI.TexWrap(TexCoordComponent.U, TexWrapMode.Clamp);

				Span<float> pixelData = stackalloc float[LINEAR_TO_GAMMA_IDENTITY_TABLE_WIDTH];
				for (int i = 0; i != LINEAR_TO_GAMMA_IDENTITY_TABLE_WIDTH; ++i) {
					float fLookupResult = ((float)i) / ((float)(LINEAR_TO_GAMMA_IDENTITY_TABLE_WIDTH - 1));

					//do an extra srgb conversion because we'll be converting back on texture read
					fLookupResult = ShaderAPI.LinearToGamma_HardwareSpecific(fLookupResult);

					int iColor = MathLib.RoundFloatToInt(fLookupResult * 255.0f);
					if (iColor > 255)
						iColor = 255;

					pixelData.Cast<float, byte>()[i] = (byte)iColor;
				}

				ShaderAPI.TexImage2D(0, 0, gammalookupfmt, 0, LINEAR_TO_GAMMA_IDENTITY_TABLE_WIDTH, 1, gammalookupfmt, false, pixelData.Cast<float, byte>());
			}
		}

		//create the maximum depth texture
		{
			MaxDepthTextureHandle = ShaderAPI.CreateTexture(1, 1, 1, ImageFormat.RGBA8888, 1, 1, (int)tcFlags, "[MAXDEPTH_TEXID]", TEXTURE_GROUP_OTHER);
			ShaderAPI.ModifyTexture(MaxDepthTextureHandle);
			ShaderAPI.TexMinFilter(TexFilterMode.Linear);
			ShaderAPI.TexMagFilter(TexFilterMode.Linear);

			texel[0] = texel[1] = texel[2] = 255;
			texel[3] = 255;

			ShaderAPI.TexImage2D(0, 0, ImageFormat.RGBA8888, 0, 1, 1, ImageFormat.RGBA8888, false, texel);
		}

		//only the shaderapi can handle switching between textures correctly, so pass off the textures to it.
		ShaderAPI.SetLinearToGammaConversionTextures(LinearToGammaTableTextureHandle, LinearToGammaIdentityTableTextureHandle);
	}

	public void ReleaseStandardTextures() {
		if (StandardTexturesAllocated) {
			if (IsPC()) {
				ShaderAPI.DeleteTexture(BlackTextureHandle);
				ShaderAPI.DeleteTexture(WhiteTextureHandle);
				ShaderAPI.DeleteTexture(GreyTextureHandle);
				ShaderAPI.DeleteTexture(GreyAlphaZeroTextureHandle);
			}
			ShaderAPI.DeleteTexture(FullbrightLightmapTextureHandle);
			ShaderAPI.DeleteTexture(FlatNormalTextureHandle);
			ShaderAPI.DeleteTexture(FullbrightBumpedLightmapTextureHandle);

			ShaderAPI.DeleteTexture(LinearToGammaTableTextureHandle);
			ShaderAPI.DeleteTexture(LinearToGammaIdentityTableTextureHandle);
			ShaderAPI.SetLinearToGammaConversionTextures(INVALID_SHADERAPI_TEXTURE_HANDLE, INVALID_SHADERAPI_TEXTURE_HANDLE);

			ShaderAPI.DeleteTexture(MaxDepthTextureHandle);

			StandardTexturesAllocated = false;
		}
	}

	private void ConvertModeStruct(MaterialSystem_Config config, out ShaderDeviceInfo mode) {
		mode = new ShaderDeviceInfo();
		mode.DisplayMode.Width = config.VideoMode.Width;
		mode.DisplayMode.Height = config.VideoMode.Height;
		mode.DisplayMode.Format = config.Format;
		mode.DisplayMode.RefreshRateNumerator = config.VideoMode.RefreshRate;
		mode.DisplayMode.RefreshRateDenominator = config.VideoMode.RefreshRate >= 0 ? 1 : 0;
		mode.BackBufferCount = 1;
		mode.AASamples = config.AASamples;
		mode.AAQuality = config.AAQuality;
		mode.Driver = config.Driver;
		mode.WindowedSizeLimitWidth = (int)config.WindowedSizeLimitWidth;
		mode.WindowedSizeLimitHeight = (int)config.WindowedSizeLimitHeight;

		mode.Windowed = config.Windowed();
		mode.Borderless = config.NoWindowBorder();
		mode.Resizing = config.Resizing();
		mode.UseStencil = config.Stencil();
		mode.LimitWindowedSize = config.LimitWindowedSize();
		mode.WaitForVSync = config.WaitForVSync();
		mode.ScaleToOutputResolution = config.ScaleToOutputResolution();
		mode.UsingMultipleWindows = config.UsingMultipleWindows();
	}
	IMaterial IMaterialSystem.CreateMaterial(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroup, KeyValues keyValues) => CreateMaterial(materialName, textureGroup, keyValues);
	IMaterial IMaterialSystem.CreateMaterial(ReadOnlySpan<char> materialName, KeyValues keyValues) => CreateMaterial(materialName, TEXTURE_GROUP_OTHER, keyValues);

	public IMaterialInternal CreateMaterial(ReadOnlySpan<char> materialName, KeyValues? keyValues)
		=> CreateMaterial(materialName, MaterialDefines.TEXTURE_GROUP_OTHER, keyValues);
	public IMaterialInternal CreateMaterial(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroup, KeyValues? keyValues) {
		IMaterialInternal material;
		lock (this) {
			material = new Material(this, materialName, textureGroup, keyValues);
		}

		MaterialDict.AddMaterialToMaterialList(material);
		return material;
	}


	public IMaterial? GetCurrentMaterial() {
		return GetRenderContext().GetCurrentMaterial();
	}

	public void AddMaterialToMaterialList(IMaterialInternal? material) => MaterialDict.AddMaterialToMaterialList(material!);
	public void RemoveMaterial(IMaterialInternal? material) => MaterialDict.RemoveMaterial(material!);
	public void RemoveMaterialSubRect(IMaterialInternal? material) {
		throw new NotImplementedException("Incomplete port of IMaterialSystemInternal");
	}

	public int GetLightmapWidth(int lightmap) => MatLightmaps.GetLightmapWidth(lightmap);
	public int GetLightmapHeight(int lightmap) => MatLightmaps.GetLightmapHeight(lightmap);

	public int GetLightmapPage() => GetRenderContextInternal().GetLightmapPage();
	public ITexture? GetLocalCubemap() => GetRenderContextInternal().GetLocalCubemap();
	public void ForceDepthFuncEquals(bool enable) => GetRenderContextInternal().ForceDepthFuncEquals(enable);
	public MaterialHeightClipMode GetHeightClipMode() => GetRenderContextInternal().GetHeightClipMode();

	public MatCallQueue GetRenderCallQueue() => GetRenderContextInternal().GetCallQueueInternal();

	public void UnbindMaterial(IMaterial? material) {
		Assert(material == null || ((IMaterialInternal)material).IsRealTimeVersion());
		if (HardwareRenderContext.GetCurrentMaterial() == material)
			HardwareRenderContext.Bind(errorMaterial, null);
	}

	uint RenderThreadID = 0xFFFFFFFF;
	public uint GetRenderThreadId() => RenderThreadID;

	public bool CanUseEditorMaterials() {
		return false; //todo
	}

	public bool IsInStubMode() => false;

	public bool OnDrawMesh(IMesh mesh, int firstIndex, int indexCount) {
		if (IsInStubMode())
			return false;

		return GetRenderContextInternal().OnDrawMesh(mesh, firstIndex, indexCount);
	}

	public IMatRenderContextInternal GetRenderContextInternal() {
		IMatRenderContextInternal? renderContext = RenderContext.Value;
		return renderContext ?? HardwareRenderContext;
	}

	public bool InFlashlightMode() {
		return GetRenderContextInternal().InFlashlightMode();
	}

	public bool OnSetPrimitiveType(IMesh mesh, MaterialPrimitiveType type) {
		return GetRenderContextInternal().OnSetPrimitiveType(mesh, type);
	}

	public bool OnFlushBufferedPrimitives() {
		throw new NotImplementedException();
	}

	public void SyncMatrices() => GetRenderContextInternal().SyncMatrices();
	public void SyncMatrix(MaterialMatrixMode mode) => GetRenderContextInternal().SyncMatrix(mode);

	public ITexture? FindTexture(ReadOnlySpan<char> textureName, ReadOnlySpan<char> textureGroupName, bool complain = true, CreateTextureFlags additionalCreationFlags = 0) {
		ITextureInternal? texture = TextureSystem.FindOrLoadTexture(textureName, textureGroupName, (int)additionalCreationFlags);
		Assert(texture != null);
		if (texture != null && texture.IsError()) {
			if (complain) {
				DevWarning($"Texture '{textureName}' not found.\n");
			}
		}

		return texture;
	}

	public bool IsTextureLoaded(ReadOnlySpan<char> textureName) => TextureSystem.IsTextureLoaded(textureName);

	public ReadOnlySpan<char> GetForcedTextureLoadPathID() {
		return "GAME";
	}
	public IMaterial? FindMaterialEx(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName, int context, bool complain = true, ReadOnlySpan<char> complainPrefix = default) {
		materialName = materialName.SliceNullTerminatedString();
		Span<char> tempNameBuffer = stackalloc char[materialName.Length];
		for (int i = 0; i < materialName.Length; i++) {
			char c = materialName[i];
			tempNameBuffer[i] = c == '\\' ? '/' : c;
		}
		IMaterialInternal? existingMaterial = MaterialDict.FindMaterial(tempNameBuffer, false);

		if (existingMaterial != null)
			return existingMaterial;

		Span<char> vmtNameBuffer = stackalloc char["materials/".Length + tempNameBuffer.Length + 1];
		vmtNameBuffer.Clear();

		bool isUNC = tempNameBuffer.Length > 2 && tempNameBuffer[0] == '/' && tempNameBuffer[1] == '/' && tempNameBuffer[2] != '/';
		if (!isUNC) {
			"materials/".CopyTo(vmtNameBuffer);
			tempNameBuffer.CopyTo(vmtNameBuffer["materials/".Length..]);

			StrTools.FixDoubleSlashes(vmtNameBuffer);
		}
		else
			tempNameBuffer.CopyTo(vmtNameBuffer);

		ReadOnlySpan<char> vmtName = vmtNameBuffer.SliceNullTerminatedString();

		List<FileNameHandle_t>? includes = null;
		KeyValues keyValues = new("vmt");
		KeyValues patchKeyValues = new("vmt_patches");
		if (!Material.LoadVMTFile(FileSystem, ref keyValues, patchKeyValues, vmtName, true, null)) {
			keyValues = null!;
			patchKeyValues = null!;
		}
		else {
			int len = tempNameBuffer.Length + ".vmt".Length;
			Span<char> matNameWithExtension = stackalloc char[len];
			tempNameBuffer.CopyTo(matNameWithExtension);
			".vmt".CopyTo(matNameWithExtension[tempNameBuffer.Length..]);

			IMaterialInternal? mat = null;
			if (keyValues.Name.Equals("subrect", StringComparison.OrdinalIgnoreCase)) {
				mat = MaterialDict.AddMaterialSubRect(matNameWithExtension, textureGroupName, keyValues, patchKeyValues);
			}
			else {
				mat = MaterialDict.AddMaterial(matNameWithExtension, textureGroupName);
				if (ShaderDevice.IsUsingGraphics()) {
					mat.PrecacheVars(keyValues, patchKeyValues, includes, (MaterialFindContext)context);
					ForcedTextureLoadPathID = null;
				}
			}
			keyValues = null!;
			patchKeyValues = null!;

			return mat;
		}

		if (complain) {
			Assert(!tempNameBuffer.IsEmpty);

			if (MaterialDict.NoteMissing(vmtName)) {
				if (!complainPrefix.IsEmpty)
					DevWarning(complainPrefix);

				DevWarning($"material \"{vmtName}\" not found.\n");
			}
		}

		return errorMaterial;
	}

	public string? ForcedTextureLoadPathID;

	public IMaterial? FindMaterial(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName, bool complain, ReadOnlySpan<char> complainPrefix) {
		return FindMaterialEx(materialName, textureGroupName, (int)MaterialFindContext.None, complain, complainPrefix);
	}

	public void CreateDebugMaterials() {
		KeyValues vmtKeyValues;


		vmtKeyValues = new("UnlitGeneric");
		vmtKeyValues.SetInt("$model", 1);
		vmtKeyValues.SetFloat("$decalscale", 0.05f);
		vmtKeyValues.SetString("$basetexture", "error");
		errorMaterial = CreateMaterial("___error.vmt", vmtKeyValues);
	}

	public IMaterial? FindProceduralMaterial(ReadOnlySpan<char> materialName, ReadOnlySpan<char> textureGroupName, KeyValues keyValues) {
		IMaterialInternal? material = MaterialDict.FindMaterial(materialName, true);
		if (keyValues != null) {
			if (material != null) {
				keyValues = null;
			}
			else {
				material = CreateMaterial(materialName, textureGroupName, keyValues);
			}

			return material;
		}
		else {
			if (material == null)
				return GetErrorMaterial();

			return material;
		}
	}

	private IMaterial? GetErrorMaterial() {
		throw new NotImplementedException();
	}

	void ReleaseShaderObjects() {
		// todo
		for (int i = 0; i < ReleaseFunc.Count; i++)
			ReleaseFunc[i]();
	}

	public void RestoreShaderObjects(IServiceProvider? services, int changeFlags) {
		if (services != null) {
			ShaderAPI = services.GetRequiredService<IShaderAPI>();
			ShaderDevice = services.GetRequiredService<IShaderDevice>();
		}

		foreach (var material in MaterialDict) {
			// material.ReportVarChanged TODO
		}

		TextureSystem.RestoreRenderTargets();
		Restore?.Invoke();
		for (int i = 0; i < RestoreFunc.Count; i++)
			RestoreFunc[i]((RestoreChangeFlags)changeFlags);
		TextureSystem.RestoreNonRenderTargetTextures();
	}

	// TODO: How much of this is needed these days... I'm fairly sure not a lot of it
	bool AllocatingRenderTargets;
	public void BeginRenderTargetAllocation() {
		AllocatingRenderTargets = true;
	}

	public void EndRenderTargetAllocation() {
		ShaderAPI.FlushBufferedPrimitives();
		AllocatingRenderTargets = false;

		// I believe this step is unnecessary (and breaks how textures work rn)
		if (ShaderAPI.CanDownloadTextures()) {
			ShaderDevice.ReleaseResources();
			ShaderDevice.ReacquireResources();
		}
	}

	public ITexture CreateProceduralTexture(ReadOnlySpan<char> textureName, ReadOnlySpan<char> textureGroup, int wide, int tall, ImageFormat format, TextureFlags flags) {
		return TextureSystem.CreateProceduralTexture(textureName, textureGroup, wide, tall, 1, format, flags)!;
	}

	public ITexture? CreateNamedRenderTargetTextureEx(ReadOnlySpan<char> rtName, int w, int h, RenderTargetSizeMode sizeMode, ImageFormat format, MaterialRenderTargetDepth depthMode, TextureFlags textureFlags, CreateRenderTargetFlags renderTargetFlags) {
		RenderTargetType rtType;

		switch (depthMode) {
			case MaterialRenderTargetDepth.Separate:
				rtType = RenderTargetType.WithDepth;
				break;
			case MaterialRenderTargetDepth.None:
				rtType = RenderTargetType.NoDepth;
				break;
			case MaterialRenderTargetDepth.Only:
				rtType = RenderTargetType.OnlyDepth;
				break;
			case MaterialRenderTargetDepth.Shared:
			default:
				rtType = RenderTargetType.RenderTarget;
				break;
		}

		ITextureInternal? tex = TextureSystem.CreateRenderTargetTexture(rtName, w, h, sizeMode, format, rtType, textureFlags, renderTargetFlags);
		tex?.IncrementReferenceCount();

		if (!AllocatingRenderTargets)
			EndRenderTargetAllocation();

		return tex;
	}

	/// <summary>
	/// New version which must be called inside BeginRenderTargetAllocation-EndRenderTargetAllocation block
	/// </summary>
	public ITexture? CreateNamedRenderTargetTextureEx2(ReadOnlySpan<char> rtName, int w, int h, RenderTargetSizeMode sizeMode, ImageFormat format, MaterialRenderTargetDepth depth = MaterialRenderTargetDepth.Shared, TextureFlags textureFlags = TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags renderTargetFlags = 0) {
		// Only proceed if we are between BeginRenderTargetAllocation and EndRenderTargetAllocation
		if (!AllocatingRenderTargets) {
			Warning("Tried to create render target outside of MaterialSystem.BeginRenderTargetAllocation/EndRenderTargetAllocation block\n");
			return null;
		}

		ITexture? texture = CreateNamedRenderTargetTextureEx(rtName, w, h, sizeMode, format, depth, textureFlags, renderTargetFlags);

		texture?.DecrementReferenceCount(); // Follow the same convention as TextureManager.LoadTexture (return refcount of 0).
		return texture;
	}

	int RT_FB_WidthOverride;
	int RT_FB_HeightOverride;

	public void SetRenderTargetFrameBufferSizeOverrides(int width, int height) {
		RT_FB_WidthOverride = width;
		RT_FB_HeightOverride = height;
	}

	public void AddTextureAlias(ReadOnlySpan<char> alias, ReadOnlySpan<char> realName) => TextureSystem.AddTextureAlias(alias, realName);
	public void RemoveTextureAlias(ReadOnlySpan<char> alias) => TextureSystem.RemoveTextureAlias(alias);
	public ImageFormat GetBackBufferFormat() => ShaderDevice.GetBackBufferFormat();

	public void GetRenderTargetFrameBufferDimensions(out int fbWidth, out int fbHeight) {
		if (RT_FB_WidthOverride > 0 && RT_FB_HeightOverride > 0) {
			fbWidth = RT_FB_WidthOverride;
			fbHeight = RT_FB_HeightOverride;
		}
		else ShaderAPI.GetBackBufferDimensions(out fbWidth, out fbHeight);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int GetNumSortIDs() => MatLightmaps.GetNumSortIDs();
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void GetSortInfo(Span<MaterialSystem_SortInfo> sortInfoArray) => MatLightmaps.GetSortInfo(sortInfoArray);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void BeginLightmapAllocation() => MatLightmaps.BeginLightmapAllocation();
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void EndLightmapAllocation() {
		MatLightmaps.EndLightmapAllocation();
		AllocateStandardTextures();
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int AllocateLightmap(int allocationWidth, int allocationHeight, Span<int> offsetIntoLightmapPage, IMaterial? material) => MatLightmaps.AllocateLightmap(allocationWidth, allocationHeight, offsetIntoLightmapPage, material);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int AllocateWhiteLightmap(IMaterial? material) => MatLightmaps.AllocateWhiteLightmap(material);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void GetLightmapPageSize(int lightmap, out int width, out int height) => MatLightmaps.GetLightmapPageSize(lightmap, out width, out height);

	public void UpdateLightmap(int lightmapPageID, Span<int> lightmapSize, Span<int> offsetIntoLightmapPage, Span<float> floatImage, Span<float> floatImageBump1, Span<float> floatImageBump2, Span<float> floatImageBump3)
		=> MatLightmaps.UpdateLightmap(lightmapPageID, lightmapSize, offsetIntoLightmapPage, floatImage, floatImageBump1, floatImageBump2, floatImageBump3);


	public void GetBackBufferDimensions(out int width, out int height) => ShaderAPI.GetBackBufferDimensions(out width, out height);

	public void BeginUpdateLightmaps() => MatLightmaps.BeginUpdateLightmaps();
	public void EndUpdateLightmaps() => MatLightmaps.EndUpdateLightmaps();

	public void BindStandardTexture(Sampler sampler, StandardTextureId id) => GetRenderContextInternal().BindStandardTexture(sampler, id);

	public IMaterialProxy? DetermineProxyReplacements(IMaterial? material, KeyValues? fallbackKeyValues) {
		throw new NotImplementedException();
	}

	IMaterialProxyFactory? MaterialProxyFactory;

	public IMaterialProxyFactory? GetMaterialProxyFactory() => MaterialProxyFactory;
	public void SetMaterialProxyFactory(IMaterialProxyFactory? factory) {
		UncacheAllMaterials();
		MaterialProxyFactory = factory;
	}

	public void UncacheAllMaterials() {
		// todo: finish me!!
		foreach (var material in MaterialDict) {
			material.Uncache();
		}
	}

	public void ReloadTextures() {
		// todo
	}

	public void ReloadMaterials(ReadOnlySpan<char> subString = default) {
		// todo
	}

	void RecomputeAllStateSnapshots() {
		// todo
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetBlackTextureHandle() => BlackTextureHandle;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetFlatNormalTextureHandle() => FlatNormalTextureHandle;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetGreyTextureHandle() => GreyTextureHandle;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetGreyAlphaZeroTextureHandle() => GreyAlphaZeroTextureHandle;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ShaderAPITextureHandle_t GetWhiteTextureHandle() => WhiteTextureHandle;

	public event Action? Restore;

	readonly List<MaterialBufferRestoreFunc> RestoreFunc = [];
	readonly List<MaterialBufferReleaseFunc> ReleaseFunc = [];

	public void AddReleaseFunc(MaterialBufferReleaseFunc func) {
		Assert(!ReleaseFunc.Contains(func));
		ReleaseFunc.Add(func);
	}

	public void RemoveReleaseFunc(MaterialBufferReleaseFunc func) {
		ReleaseFunc.Remove(func);
	}

	public void AddRestoreFunc(MaterialBufferRestoreFunc func) {
		Assert(!RestoreFunc.Contains(func));
		RestoreFunc.Add(func);
	}

	public void RemoveRestoreFunc(MaterialBufferRestoreFunc func) {
		RestoreFunc.Remove(func);
	}

	public bool SupportsShadowDepthTextures() => ShaderAPI.SupportsShadowDepthTextures();

	public ImageFormat GetShadowDepthTextureFormat() => ShaderAPI.GetShadowDepthTextureFormat();

	public ImageFormat GetNullTextureFormat() => ShaderAPI.GetNullTextureFormat();

	public IMaterialInternal errorMaterial;
	public readonly MatLightmaps MatLightmaps;

	public bool AddTextureCompositorTemplate(ReadOnlySpan<char> name, KeyValues tmplDesc, int texCompositeTemplateFlags = 0) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool AddView(IWindow hwnd) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public int AllocateDynamicLightmap(Span<int> lightmapSize, Span<int> outOffsetIntoPage, int frameID) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool AllowThreading(bool allow, int serviceThread) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void AsyncFindTexture<T>(ReadOnlySpan<char> pFilename, ReadOnlySpan<char> textureGroupName, IAsyncTextureOperationReceiver<T> recipient, ref T extraArgs, bool complain = true, CreateTextureFlags additionalCreationFlags = 0) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void CacheUsedMaterials() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void ClearBuffers(bool clearColor, bool clearDepth, bool clearStencil = false) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void CompactMemory() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public ITexture? CreateNamedRenderTargetTexture(ReadOnlySpan<char> rtName, int w, int h, RenderTargetSizeMode sizeMode, ImageFormat format, MaterialRenderTargetDepth depth = MaterialRenderTargetDepth.Shared, bool clampTexCoords = true, bool autoMipMap = false) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public ITexture? CreateNamedTextureFromBitsEx(ReadOnlySpan<char> name, ReadOnlySpan<char> textureGroupName, int w, int h, int mips, ImageFormat fmt, int srcBufferSize, Span<byte> srcBits, CreateTextureFlags flags) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public ITexture? CreateRenderTargetTexture(int w, int h, RenderTargetSizeMode sizeMode, ImageFormat format, MaterialRenderTargetDepth depth = MaterialRenderTargetDepth.Shared) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public ITexture? CreateTextureFromBits(int w, int h, int mips, ImageFormat fmt, int srcBufferSize, Span<byte> srcBits) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void DebugPrintUsedMaterials(ReadOnlySpan<char> searchSubString, bool verbose) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void DebugPrintUsedTextures() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void DoStartupShaderPreloading() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void EnableEditorMaterials() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void EvictManagedResources() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void ExecuteQueued() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public MaterialHandle_t FirstMaterial() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void Flush(bool flushHardware = false) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void GMOD_ClearMissing(bool unknown) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void GMOD_FlushQueue() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public IMaterial? GMOD_GetErrorMaterial() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool GMOD_IsMaterialMissing(ReadOnlySpan<char> materialName) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void GMOD_MarkMissing(ReadOnlySpan<char> materialName) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool GMOD_TextureExists(ReadOnlySpan<char> textureName) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void GetDisplayAdapterInfo(uint adapter, out MaterialAdapterInfo info) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public ReadOnlySpan<char> GetDisplayDeviceName() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void GetDisplayMode(out UserVideoMode mode) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void GetDriverLevelDefaults(out GraphicsDriver maxLevel, out GraphicsDriver recommendedLevel) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public IMaterialSystemHardwareConfig GetHardwareConfig(ReadOnlySpan<char> pVersion, out int returnCode) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public IMaterial? GetMaterial(MaterialHandle_t h) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public uint GetModeCount(uint adapter) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public int GetNumMaterials() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool GetRecommendedConfigurationInfo(int nDXLevel, KeyValues keyValues) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void GetShaderFallback(ReadOnlySpan<char> shaderName, Span<char> fallbackShader) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public int GetShaders(int firstShader, Span<IShader> shaderList) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public MaterialThreadMode GetThreadMode() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public ref readonly MaterialSystemHardwareIdentifier GetVideoCardIdentifier() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void HandleDeviceLost() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool HasShaderAPI() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void Init(IServiceProvider shaderAPIFactory, IMaterialProxyFactory materialProxyFactory, IServiceProvider fileSystemFactory, IServiceProvider? cvarFactory = null) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public MaterialHandle_t InvalidMaterial() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool IsMaterialLoaded(ReadOnlySpan<char> materialName) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool IsRenderThreadSafe() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public MaterialLock Lock() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public ITextureCompositor? NewTextureCompositor(int w, int h, ReadOnlySpan<char> compositeName, int teamNum, ulong randomSeed, KeyValues stageDesc, CreateTextureFlags texCompositeCreateFlags = 0) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public MaterialHandle_t NextMaterial(MaterialHandle_t h) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void OverrideRenderTargetAllocation(bool rtAlloc) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void ReacquireResources() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void ReleaseResources() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void ReloadFilesInList(IFileList filesToReload) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void RemoveModeChangeCallBack(ModeChangeCallbackFunc func) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void RemoveView(IWindow hwnd) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void ResetMaterialLightmapPageInfo() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void ResetTempHWMemory(bool bExitingLevel = false) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void SetAdapter(uint adapter, int flags) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void SetExcludedTextures(ReadOnlySpan<char> scriptName) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void SetInStubMode(bool inStubMode) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void SetShaderAPI(IServiceProvider shaderAPIFactory) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void SetThreadMode(MaterialThreadMode mode, int nServiceThread = -1) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void SetView(IWindow hwnd) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public int ShaderCount() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public int ShaderFlagCount() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public ReadOnlySpan<char> ShaderFlagName(int index) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void SpewDriverInfo() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public int StencilBufferBits() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool SupportsCSAAMode(int nNumSamples, int nQualityLevel) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool SupportsFetch4() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool SupportsHDRMode(HDRType hdrMode) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool SupportsMSAAMode(int nMSAAMode) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void ToggleDebugMaterial(ReadOnlySpan<char> materialName) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void ToggleSuppressMaterial(ReadOnlySpan<char> materialName) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void UncacheUnusedMaterials(bool bRecomputeStateSnapshots = false) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void Unlock(MaterialLock l) {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public void UpdateExcludedTextures() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool UsingFastClipping() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}

	public bool VerifyTextureCompositorTemplates() {
		throw new NotImplementedException("Incomplete port of IMaterialSystem");
	}
}

public enum MatrixStackFlags : uint
{
	Dirty = 1 << 0
}

public struct MatrixStackItem
{
	public Matrix4x4 Matrix;
}

public struct RenderTargetStackElement
{
	public ITexture? RenderTarget0;
	public ITexture? RenderTarget1;
	public ITexture? RenderTarget2;
	public ITexture? RenderTarget3;

	public readonly ITexture? this[int index] => index switch {
		0 => RenderTarget0,
		1 => RenderTarget1,
		2 => RenderTarget2,
		3 => RenderTarget3,
		_ => null
	};

	public ITexture? DepthTexture;

	public int ViewX;
	public int ViewY;
	public int ViewW;
	public int ViewH;

	public const int NUM_RENDER_TARGET_BINDS = 4;

	public readonly int Size => NUM_RENDER_TARGET_BINDS;

	public RenderTargetStackElement(int viewX, int viewY, int viewW, int viewH) {
		this.ViewX = viewX;
		this.ViewY = viewY;
		this.ViewW = viewW;
		this.ViewH = viewH;
	}
	public RenderTargetStackElement(ITexture? rt0, int viewX, int viewY, int viewW, int viewH) : this(viewX, viewY, viewW, viewH) {
		RenderTarget0 = rt0;
	}
	public RenderTargetStackElement(ITexture? rt0, ITexture? rt1, int viewX, int viewY, int viewW, int viewH) : this(viewX, viewY, viewW, viewH) {
		RenderTarget0 = rt0;
		RenderTarget1 = rt1;
	}
	public RenderTargetStackElement(ITexture? rt0, ITexture? rt1, ITexture? rt2, int viewX, int viewY, int viewW, int viewH) : this(viewX, viewY, viewW, viewH) {
		RenderTarget0 = rt0;
		RenderTarget1 = rt1;
		RenderTarget2 = rt2;
	}
	public RenderTargetStackElement(ITexture? rt0, ITexture? rt1, ITexture? rt2, ITexture? rt3, int viewX, int viewY, int viewW, int viewH) : this(viewX, viewY, viewW, viewH) {
		RenderTarget0 = rt0;
		RenderTarget1 = rt1;
		RenderTarget2 = rt2;
		RenderTarget3 = rt3;
	}
}
