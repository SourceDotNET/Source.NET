using Source.Common.Bitmap;
using Source.Common.Engine;
using Source.Common.ShaderAPI;


namespace Source.Common.MaterialSystem;

public enum MaterialSystem_Config_Flags
{
	Windowed = (1 << 0),
	Resizing = (1 << 1),
	NoWaitForVSync = (1 << 3),
	Stencil = (1 << 4),
	ForceTrilinear = (1 << 5),
	ForceHardwareSync = (1 << 6),
	DisableSpecular = (1 << 7),
	DisableBumpmap = (1 << 8),
	EnableParallaxMapping = (1 << 9),
	UseZPrefill = (1 << 10),
	ReduceFillrate = (1 << 11),
	ENABLE_HDR = (1 << 12),
	LimitedWindowSize = (1 << 13),
	ScaleToOutputResolution = (1 << 14),
	UsingMultipleWindows = (1 << 15),
	DisablePhong = (1 << 16),
	VRMode = (1 << 17),
	NoWindowBorder = (1 << 18)
}

public struct MaterialSystemHardwareIdentifier{
	public string CardName;
	public uint VendorID;
	public uint DeviceID;
}

public class MaterialSystem_Config
{
	public UserVideoMode VideoMode;
	public ImageFormat Format;
	public float MonitorGamma;
	public float GammaTVRangeMin;
	public float GammaTVRangeMax;
	public float GammaTVExponent;
	public bool GammaTVEnabled;

	public GraphicsDriver Driver = GraphicsDriver.OpenGL46; // todo; get this some other way?

	public int AASamples;
	public int ForceAnisotropicLevel;
	public int SkipMipLevels;
	public uint Flags;
	public bool EditMode;             // true if in Hammer.
	public byte ProxiesTestMode;  // 0 = normal, 1 = no proxies, 2 = alpha test all, 3 = color mod all
	public bool CompressedTextures;
	public bool FilterLightmaps;
	public bool FilterTextures;
	public bool ReverseDepth;
	public bool BufferPrimitives;
	public bool DrawFlat;
	public bool MeasureFillRate;
	public bool VisualizeFillRate;
	public bool NoTransparency;
	public bool SoftwareLighting;
	public bool AllowCheats;
	public sbyte ShowMipLevels;
	public bool ShowLowResImage;
	public bool ShowNormalMap;
	public bool MipMapTextures;
	public byte Fullbright;
	public bool FastNoBump;
	public bool SuppressRendering;

	// debug modes
	public bool ShowSpecular; // This is the fast version that doesn't require reloading materials
	public bool ShowDiffuse;  // This is the fast version that doesn't require reloading materials

	// No depth bias
	public float SlopeScaleDepthBias_Normal;
	public float DepthBias_Normal;

	// Depth bias for rendering decals closer to the camera
	public float SlopeScaleDepthBias_Decal;
	public float DepthBias_Decal;

	// Depth bias for biasing shadow depth map rendering away from the camera
	public float SlopeScaleDepthBias_ShadowMap;
	public float DepthBias_ShadowMap;

	public uint WindowedSizeLimitWidth;
	public uint WindowedSizeLimitHeight;
	public int AAQuality;
	public bool ShadowDepthTexture;
	public bool MotionBlur;
	public bool SupportFlashlight;

	public void SetFlag(MaterialSystem_Config_Flags flag, bool val) {
		if (val) {
			Flags |= (uint)flag;
		}
		else {
			Flags &= ~(uint)flag;
		}
	}

	public bool Windowed() => (Flags & (int)MaterialSystem_Config_Flags.Windowed) != 0;
	public bool NoWindowBorder() => (Flags & (int)MaterialSystem_Config_Flags.NoWindowBorder) != 0;
	public bool Resizing() => (Flags & (int)MaterialSystem_Config_Flags.Resizing) != 0;
	public bool WaitForVSync() => (Flags & (int)MaterialSystem_Config_Flags.NoWaitForVSync) == 0;
	public bool Stencil() => (Flags & (int)MaterialSystem_Config_Flags.Stencil) != 0;
	public bool ForceTrilinear() => (Flags & (int)MaterialSystem_Config_Flags.ForceTrilinear) != 0;
	public bool ForceHardwareSync() => (Flags & (int)MaterialSystem_Config_Flags.ForceHardwareSync) != 0;
	public bool UseSpecular() => (Flags & (int)MaterialSystem_Config_Flags.DisableSpecular) == 0;
	public bool DisableSpecular() => (Flags & (int)MaterialSystem_Config_Flags.DisableSpecular) != 0;
	public bool DisableBumpmap() => (Flags & (int)MaterialSystem_Config_Flags.DisableBumpmap) != 0;
	public bool UseBumpmapping() => (Flags & (int)MaterialSystem_Config_Flags.DisableBumpmap) == 0;
	public bool EnableParallaxMapping() => (Flags & (int)MaterialSystem_Config_Flags.EnableParallaxMapping) != 0;
	public bool UseZPrefill() => (Flags & (int)MaterialSystem_Config_Flags.UseZPrefill) != 0;
	public bool ReduceFillrate() => (Flags & (int)MaterialSystem_Config_Flags.ReduceFillrate) != 0;
	public bool HDREnabled() => (Flags & (int)MaterialSystem_Config_Flags.ENABLE_HDR) != 0;
	public bool LimitWindowedSize() => (Flags & (int)MaterialSystem_Config_Flags.LimitedWindowSize) != 0;
	public bool ScaleToOutputResolution() => (Flags & (int)MaterialSystem_Config_Flags.ScaleToOutputResolution) != 0;
	public bool UsingMultipleWindows() => (Flags & (int)MaterialSystem_Config_Flags.UsingMultipleWindows) != 0;
	public bool DisablePhong() => (Flags & (int)MaterialSystem_Config_Flags.DisablePhong) != 0;
	public bool UsePhong() => (Flags & (int)MaterialSystem_Config_Flags.DisablePhong) == 0;
	public bool VRMode() => (Flags & (int)MaterialSystem_Config_Flags.VRMode) != 0;

	public MaterialSystem_Config() {
		SetFlag(MaterialSystem_Config_Flags.Windowed, false);
		SetFlag(MaterialSystem_Config_Flags.Resizing, false);
		SetFlag(MaterialSystem_Config_Flags.NoWaitForVSync, true);
		SetFlag(MaterialSystem_Config_Flags.Stencil, false);
		SetFlag(MaterialSystem_Config_Flags.ForceTrilinear, true);
		SetFlag(MaterialSystem_Config_Flags.ForceHardwareSync, true);
		SetFlag(MaterialSystem_Config_Flags.DisableSpecular, false);
		SetFlag(MaterialSystem_Config_Flags.DisableBumpmap, false);
		SetFlag(MaterialSystem_Config_Flags.EnableParallaxMapping, true);
		SetFlag(MaterialSystem_Config_Flags.UseZPrefill, false);
		SetFlag(MaterialSystem_Config_Flags.ReduceFillrate, false);
		SetFlag(MaterialSystem_Config_Flags.LimitedWindowSize, false);
		SetFlag(MaterialSystem_Config_Flags.ScaleToOutputResolution, false);
		SetFlag(MaterialSystem_Config_Flags.UsingMultipleWindows, false);
		SetFlag(MaterialSystem_Config_Flags.DisablePhong, false);
		SetFlag(MaterialSystem_Config_Flags.VRMode, false);
	}
}
