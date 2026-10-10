using Source.Common.Bitmap;
using Source.Common.Client;
using Source.Common.MaterialSystem;

using TextureFlags = Source.Common.TextureFlags;

namespace Game.Client;

public class BaseClientRenderTargets : IClientRenderTargets
{
	protected readonly TextureReference WaterReflectionTexture = new();
	protected readonly TextureReference WaterRefractionTexture = new();
	protected readonly TextureReference CameraTexture = new();

	protected static ITexture? CreateWaterReflectionTexture(IMaterialSystem materialSystem, int size) {
		return materialSystem.CreateNamedRenderTargetTextureEx2("_rt_WaterReflection", size, size, RenderTargetSizeMode.Picmip, materialSystem.GetBackBufferFormat(), MaterialRenderTargetDepth.Shared, TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags.HDR);
	}

	protected static ITexture? CreateWaterRefractionTexture(IMaterialSystem materialSystem, int size) {
		return materialSystem.CreateNamedRenderTargetTextureEx2("_rt_WaterRefraction", size, size, RenderTargetSizeMode.Picmip, ImageFormat.RGBA8888, MaterialRenderTargetDepth.Shared, TextureFlags.ClampS | TextureFlags.ClampT, CreateRenderTargetFlags.HDR);
	}

	protected static ITexture? CreateCameraTexture(IMaterialSystem materialSystem, int size) {
		return materialSystem.CreateNamedRenderTargetTextureEx2("_rt_Camera", size, size, RenderTargetSizeMode.Default, materialSystem.GetBackBufferFormat(), MaterialRenderTargetDepth.Shared, 0, CreateRenderTargetFlags.HDR);
	}

	public virtual void InitClientRenderTargets(IMaterialSystem materialSystem, IMaterialSystemHardwareConfig hardwareConfig) => InitClientRenderTargets(materialSystem, hardwareConfig, 1024, 256);

	public void InitClientRenderTargets(IMaterialSystem materialSystem, IMaterialSystemHardwareConfig hardwareConfig, int waterTextureSize, int cameraTextureSize) {
		// Water effects
		WaterReflectionTexture.Init(CreateWaterReflectionTexture(materialSystem, waterTextureSize));
		WaterRefractionTexture.Init(CreateWaterRefractionTexture(materialSystem, waterTextureSize));

		// Monitors
		CameraTexture.Init(CreateCameraTexture(materialSystem, cameraTextureSize));
	}

	public virtual void ShutdownClientRenderTargets() {
		// Water effects
		WaterReflectionTexture.Shutdown();
		WaterRefractionTexture.Shutdown();

		// Monitors
		CameraTexture.Shutdown();
	}
}

public class ClientRenderTargets : BaseClientRenderTargets
{
	public override void InitClientRenderTargets(IMaterialSystem materialSystem, IMaterialSystemHardwareConfig hardwareConfig) {
		InitClientRenderTargets(materialSystem, hardwareConfig, 1024, 512);
		materialSystem.CreateNamedRenderTargetTexture("_rt_spawnicon", 512, 512, RenderTargetSizeMode.Default, ImageFormat.ARGB8888, MaterialRenderTargetDepth.Separate, true, false);

		RenderTexture.GetMoBlurTex0();
		RenderTexture.GetMoBlurTex1();
		RenderTexture.GetBloomTex0();
		RenderTexture.GetBloomTex1();
		RenderTexture.GetMorphTex0();
		RenderTexture.GetMorphTex1();
		RenderTexture.GetSuperFPTex2(hardwareConfig);
		RenderTexture.GetSuperFPTex(hardwareConfig);
	}
}
