using Source;
using Source.Common.Bitmap;
using Source.Common.MaterialSystem;

using TextureFlags = Source.Common.TextureFlags;

namespace Game.Client;

/// <summary>
/// Implements local hooks into named renderable textures.
/// See MatSysInterface.InitWellKnownRenderTargets in the engine for list of available RT's
/// </summary>
public static class RenderTexture
{
	const int MAX_FB_TEXTURES = 4;
	public const int MAX_TEENY_TEXTURES = 3;

	static bool Added;

	static void AddReleaseFunc() {
		if (!Added) {
			Added = true;
			materials.AddReleaseFunc(ReleaseRenderTargets);
		}
	}

	static readonly TextureReference PowerOfTwoFrameBufferTexture = new();
	public static ITexture? GetPowerOfTwoFrameBufferTexture() {
		if (IsX360())
			return GetFullFrameFrameBufferTexture(1);

		if (!PowerOfTwoFrameBufferTexture.IsValid()) {
			PowerOfTwoFrameBufferTexture.Init(materials.FindTexture("_rt_PowerOfTwoFB", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(PowerOfTwoFrameBufferTexture.Get()));
			AddReleaseFunc();
		}

		return PowerOfTwoFrameBufferTexture.Get();
	}

	static readonly TextureReference FullscreenTexture = new();
	public static ITexture? GetFullscreenTexture() {
		if (!FullscreenTexture.IsValid()) {
			FullscreenTexture.Init(materials.FindTexture("_rt_Fullscreen", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(FullscreenTexture.Get()));
			AddReleaseFunc();
		}

		return FullscreenTexture.Get();
	}

	static readonly TextureReference CameraTexture = new();
	public static ITexture? GetCameraTexture() {
		if (!CameraTexture.IsValid()) {
			CameraTexture.Init(materials.FindTexture("_rt_Camera", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(CameraTexture.Get()));
			AddReleaseFunc();
		}

		return CameraTexture.Get();
	}

	static readonly TextureReference FullFrameDepthTexture = new();
	public static ITexture? GetFullFrameDepthTexture() {
		if (!FullFrameDepthTexture.IsValid()) {
			FullFrameDepthTexture.Init(materials.FindTexture("_rt_FullFrameDepth", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(FullFrameDepthTexture.Get()));
			AddReleaseFunc();
		}

		return FullFrameDepthTexture.Get();
	}

	static readonly TextureReference[] FullFrameFrameBufferTexture = [new(), new(), new(), new()];
	public static ITexture? GetFullFrameFrameBufferTexture(int textureIndex) {
		if ((uint)textureIndex >= MAX_FB_TEXTURES)
			return null;

		if (!FullFrameFrameBufferTexture[textureIndex].IsValid()) {
			string name = textureIndex != 0 ? $"{MaterialDefines.FULL_FRAME_FRAMEBUFFER}{textureIndex}" : MaterialDefines.FULL_FRAME_FRAMEBUFFER;
			FullFrameFrameBufferTexture[textureIndex].Init(materials.FindTexture(name, MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(FullFrameFrameBufferTexture[textureIndex].Get()));
			AddReleaseFunc();
		}

		return FullFrameFrameBufferTexture[textureIndex].Get();
	}

	static readonly TextureReference WaterReflectionTexture = new();
	public static ITexture? GetWaterReflectionTexture() {
		if (!WaterReflectionTexture.IsValid()) {
			WaterReflectionTexture.Init(materials.FindTexture("_rt_WaterReflection", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(WaterReflectionTexture.Get()));
			AddReleaseFunc();
		}

		return WaterReflectionTexture.Get();
	}

	static readonly TextureReference WaterRefractionTexture = new();
	public static ITexture? GetWaterRefractionTexture() {
		if (!WaterRefractionTexture.IsValid()) {
			WaterRefractionTexture.Init(materials.FindTexture("_rt_WaterRefraction", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(WaterRefractionTexture.Get()));
			AddReleaseFunc();
		}

		return WaterRefractionTexture.Get();
	}

	static readonly TextureReference SmallBufferHDR0 = new();
	/// <summary>
	/// SmallBufferHDRx=r16g16b16a16 quarter-sized texture
	/// </summary>
	public static ITexture? GetSmallBufferHDR0() {
		if (!SmallBufferHDR0.IsValid()) {
			SmallBufferHDR0.Init(materials.FindTexture("_rt_SmallHDR0", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(SmallBufferHDR0.Get()));
			AddReleaseFunc();
		}

		return SmallBufferHDR0.Get();
	}

	static readonly TextureReference SmallBufferHDR1 = new();
	/// <summary>
	/// SmallBufferHDRx=r16g16b16a16 quarter-sized texture
	/// </summary>
	public static ITexture? GetSmallBufferHDR1() {
		if (!SmallBufferHDR1.IsValid()) {
			SmallBufferHDR1.Init(materials.FindTexture("_rt_SmallHDR1", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(SmallBufferHDR1.Get()));
			AddReleaseFunc();
		}

		return SmallBufferHDR1.Get();
	}

	static readonly TextureReference QuarterSizedFB0 = new();
	/// <summary>
	/// quarter-sized texture, same fmt as screen
	/// </summary>
	public static ITexture? GetSmallBuffer0() {
		if (!QuarterSizedFB0.IsValid()) {
			QuarterSizedFB0.Init(materials.FindTexture("_rt_SmallFB0", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(QuarterSizedFB0.Get()));
			AddReleaseFunc();
		}

		return QuarterSizedFB0.Get();
	}

	static readonly TextureReference QuarterSizedFB1 = new();
	/// <summary>
	/// quarter-sized texture, same fmt as screen
	/// </summary>
	public static ITexture? GetSmallBuffer1() {
		if (!QuarterSizedFB1.IsValid()) {
			QuarterSizedFB1.Init(materials.FindTexture("_rt_SmallFB1", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(QuarterSizedFB1.Get()));
			AddReleaseFunc();
		}

		return QuarterSizedFB1.Get();
	}

	static readonly TextureReference[] TeenyTextures = [new(), new(), new()];
	/// <summary>
	/// tiny 32x32 texture, always 8888
	/// </summary>
	public static ITexture? GetTeenyTexture(int which) {
		if (IsX360()) {
			Assert(false);
			return null;
		}

		Assert(which < MAX_TEENY_TEXTURES);

		if (!TeenyTextures[which].IsValid()) {
			TeenyTextures[which].Init(materials.FindTexture($"_rt_TeenyFB{which}", MaterialDefines.TEXTURE_GROUP_RENDER_TARGET));
			Assert(!ITexture.IsError(TeenyTextures[which].Get()));
			AddReleaseFunc();
		}

		return TeenyTextures[which].Get();
	}

	static readonly TextureReference BloomTex0 = new();
	public static ITexture? GetBloomTex0() {
		if (BloomTex0.IsValid())
			return BloomTex0.Get();

		BloomTex0.Init(materials.CreateNamedRenderTargetTextureEx2("s_pBloomTex0", 256, 256, RenderTargetSizeMode.HDR, materials.GetBackBufferFormat(), MaterialRenderTargetDepth.Separate, TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags.HDR));
		if (!BloomTex0.IsValid())
			Warning("Error Creating Render Target s_pBloomTex0!\n");
		return BloomTex0.Get();
	}

	static readonly TextureReference BloomTex1 = new();
	public static ITexture? GetBloomTex1() {
		if (BloomTex1.IsValid())
			return BloomTex1.Get();

		BloomTex1.Init(materials.CreateNamedRenderTargetTextureEx2("s_pBloomTex1", 256, 256, RenderTargetSizeMode.HDR, materials.GetBackBufferFormat(), MaterialRenderTargetDepth.Separate, TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags.HDR));
		if (!BloomTex1.IsValid())
			Warning("Error Creating Render Target s_pBloomTex1!\n");
		return BloomTex1.Get();
	}

	static readonly TextureReference MoBlurTex0 = new();
	public static ITexture? GetMoBlurTex0() {
		if (MoBlurTex0.IsValid())
			return MoBlurTex0.Get();

		MoBlurTex0.Init(materials.CreateNamedRenderTargetTextureEx2("s_pMoBlurTex0", 256, 256, RenderTargetSizeMode.FullFrameBuffer, materials.GetBackBufferFormat(), MaterialRenderTargetDepth.Separate, TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags.HDR));
		if (!MoBlurTex0.IsValid())
			Warning("Error Creating Render Target s_pMoBlurTex0!\n");
		return MoBlurTex0.Get();
	}

	static readonly TextureReference MoBlurTex1 = new();
	public static ITexture? GetMoBlurTex1() {
		if (MoBlurTex1.IsValid())
			return MoBlurTex1.Get();

		MoBlurTex1.Init(materials.CreateNamedRenderTargetTextureEx2("s_pMoBlurTex1", 256, 256, RenderTargetSizeMode.FullFrameBuffer, materials.GetBackBufferFormat(), MaterialRenderTargetDepth.Separate, TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags.HDR));
		if (!MoBlurTex1.IsValid())
			Warning("Error Creating Render Target s_pMoBlurTex1!\n");
		return MoBlurTex1.Get();
	}

	static readonly TextureReference MorphTex0 = new();
	public static ITexture? GetMorphTex0() {
		if (MorphTex0.IsValid())
			return MorphTex0.Get();

		MorphTex0.Init(materials.CreateNamedRenderTargetTextureEx2("s_pMorphTexture0", 256, 256, RenderTargetSizeMode.FullFrameBuffer, materials.GetBackBufferFormat(), MaterialRenderTargetDepth.Separate, TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags.HDR));
		if (!MorphTex0.IsValid())
			Warning("Error Creating Render Target s_pMorphTexture0!\n");
		return MorphTex0.Get();
	}

	static readonly TextureReference MorphTex1 = new();
	public static ITexture? GetMorphTex1() {
		if (MorphTex1.IsValid())
			return MorphTex1.Get();

		MorphTex1.Init(materials.CreateNamedRenderTargetTextureEx2("s_pMorphTexture1", 256, 256, RenderTargetSizeMode.FullFrameBuffer, materials.GetBackBufferFormat(), MaterialRenderTargetDepth.Separate, TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags.HDR));
		if (!MorphTex1.IsValid())
			Warning("Error Creating Render Target s_pMorphTexture1!\n");
		return MorphTex1.Get();
	}

	static readonly TextureReference SuperFPTex = new();
	public static ITexture? GetSuperFPTex(IMaterialSystemHardwareConfig? hardwareConfig) {
		if (!SuperFPTex.IsValid()) {
			if (hardwareConfig != null && hardwareConfig.GetDXSupportLevel() >= 90)
				SuperFPTex.InitRenderTarget(512, 512, RenderTargetSizeMode.FullFrameBuffer, ImageFormat.RGBA16161616F, MaterialRenderTargetDepth.Shared, true, "__rt_SuperTexture1");
			return SuperFPTex.Get();
		}
		return SuperFPTex.Get();
	}

	static readonly TextureReference SuperFPTex2 = new();
	public static ITexture? GetSuperFPTex2(IMaterialSystemHardwareConfig? hardwareConfig) {
		if (!SuperFPTex2.IsValid()) {
			if (hardwareConfig != null && hardwareConfig.GetDXSupportLevel() >= 90)
				SuperFPTex2.InitRenderTarget(512, 512, RenderTargetSizeMode.FullFrameBuffer, ImageFormat.RGBA16161616F, MaterialRenderTargetDepth.Shared, true, "__rt_SuperTexture2");
			return SuperFPTex2.Get();
		}
		return SuperFPTex2.Get();
	}

	public static void ReleaseRenderTargets() {
		PowerOfTwoFrameBufferTexture.Shutdown();
		CameraTexture.Shutdown();
		WaterReflectionTexture.Shutdown();
		WaterRefractionTexture.Shutdown();
		QuarterSizedFB0.Shutdown();
		QuarterSizedFB1.Shutdown();
		FullFrameDepthTexture.Shutdown();

		for (int i = 0; i < MAX_FB_TEXTURES; ++i)
			FullFrameFrameBufferTexture[i].Shutdown();
	}
}
