using Source.Common.Bitmap;
using Source.Common.Commands;
using Source.Common.MaterialSystem;

namespace Source.ShaderAPI.Gl46;

public class HardwareConfig : IMaterialSystemHardwareConfig
{
	public bool SupportsShadowDepthTexturesCap = true;
	public ImageFormat ShadowDepthTextureFormat = ImageFormat.NV_DST24;
	public ImageFormat NullTextureFormat = ImageFormat.NV_NULL;

	public bool ActuallySupportsPixelShaders_2_b() {
		throw new NotImplementedException();
	}

	public bool CanDoSRGBReadFromRTs() {
		throw new NotImplementedException();
	}

	public bool FakeSRGBWrite() {
		throw new NotImplementedException();
	}

	public int GetDXSupportLevel() {
		return 90;
	}

	public int GetFrameBufferColorDepth() {
		throw new NotImplementedException();
	}

	public HDRType HDRTypeCap = HDRType.Integer;
	public HDRType MaxHDRType = HDRType.Float;
	bool HDREnabled;

	public HDRType GetHardwareHDRType() {
		return HDRTypeCap;
	}

	public bool GetHDREnabled() {
		return HDREnabled;
	}

	public HDRType GetHDRType() {
		if (HDREnabled)
			return HDRTypeCap;
		return HDRType.None;
	}

	public int GetMaxDXSupportLevel() {
		throw new NotImplementedException();
	}

	public int GetMaxVertexTextureDimension() {
		throw new NotImplementedException();
	}

	public unsafe int GetSamplerCount() {
		int count;
		glGetIntegerv(GL_MAX_TEXTURE_IMAGE_UNITS, &count);
		return Math.Min(count, (int)Sampler.MaxSamplers);
	}

	public ReadOnlySpan<char> GetShaderDLLName() {
		throw new NotImplementedException();
	}

	public enum ShadowFilterMode
	{
		None = 0,
		NvidiaPcfPoisson = 0,
		AtiNoPcf = 1,
		AtiNoPcfFetch4 = 2
	}

	public int GetShadowFilterMode() {
		return ShadowDepthTextureFormat switch {
			ImageFormat.NV_DST16 or ImageFormat.NV_DST24 => (int)ShadowFilterMode.NvidiaPcfPoisson,
			ImageFormat.ATI_DST16 or ImageFormat.ATI_DST24 => (int)ShadowFilterMode.AtiNoPcfFetch4,
			_ => (int)ShadowFilterMode.None,
		};
	}
	public int GetTextureStageCount() {
		return GetSamplerCount();
	}

	public int GetVertexTextureCount() {
		throw new NotImplementedException();
	}

	public bool HasDestAlphaBuffer() {
		throw new NotImplementedException();
	}

	public bool HasFastVertexTextures() {
		return false;
	}

	public bool HasProjectedBumpEnv() {
		throw new NotImplementedException();
	}

	public bool HasSetDeviceGammaRamp() {
		throw new NotImplementedException();
	}

	public bool HasStencilBuffer() {
		throw new NotImplementedException();
	}

	public bool IsAAEnabled() {
		throw new NotImplementedException();
	}

	public int MaxBlendMatrices() {
		throw new NotImplementedException();
	}

	public int MaxBlendMatrixIndices() {
		throw new NotImplementedException();
	}

	public int MaxHWMorphBatchCount() {
		throw new NotImplementedException();
	}

	public unsafe int MaximumAnisotropicLevel() {
		float maxAniso;
		glGetFloatv(GL_MAX_TEXTURE_MAX_ANISOTROPY, &maxAniso);
		return (int)maxAniso;
	}

	public const int MAX_NUM_LIGHTS = 4;

	public int MaxNumLights() {
		return MAX_NUM_LIGHTS;
	}

	public int MaxTextureAspectRatio() {
		return int.MaxValue;
	}

	public unsafe int MaxTextureDepth() {
		int* maxTextureSize = stackalloc int[1];
		glGetIntegerv(GL_MAX_TEXTURE_SIZE, maxTextureSize);
		return *maxTextureSize;
	}

	public unsafe int MaxTextureHeight() {
		int* maxTextureSize = stackalloc int[1];
		glGetIntegerv(GL_MAX_TEXTURE_SIZE, maxTextureSize);
		return *maxTextureSize;
	}

	public unsafe int MaxTextureWidth() {
		int* maxTextureSize = stackalloc int[1];
		glGetIntegerv(GL_MAX_TEXTURE_SIZE, maxTextureSize);
		return *maxTextureSize;
	}

	public const int MAXUSERCLIPPLANES = 2;

	public int MaxUserClipPlanes() {
		return MAXUSERCLIPPLANES;
	}

	public int MaxVertexShaderBlendMatrices() {
		throw new NotImplementedException();
	}

	public int MaxViewports() {
		throw new NotImplementedException();
	}

	public bool NeedsAAClamp() {
		throw new NotImplementedException();
	}

	public bool NeedsATICentroidHack() {
		throw new NotImplementedException();
	}

	public int NeedsShaderSRGBConversion() {
		throw new NotImplementedException();
	}

	public int NumPixelShaderConstants() {
		throw new NotImplementedException();
	}

	public int NumVertexShaderConstants() {
		throw new NotImplementedException();
	}

	public void OverrideStreamOffsetSupport(bool overrideEnabled, bool enableSupport) {
		throw new NotImplementedException();
	}

	public bool PreferDynamicTextures() {
		return false;
	}

	public bool PreferReducedFillrate() {
		return false;
	}

	public bool ReadPixelsFromFrontBuffer() {
		throw new NotImplementedException();
	}

	public void SetHDREnabled(bool bEnable) {
		HDREnabled = bEnable;
	}

	public bool SpecifiesFogColorInLinearSpace() {
		throw new NotImplementedException();
	}

	public int StencilBufferBits() {
		throw new NotImplementedException();
	}

	public bool SupportsBorderColor() {
		// throw new NotImplementedException();
		return true;
	}

	public bool SupportsColorOnSecondStream() {
		return true;
	}

	public bool SupportsCompressedTextures() {
		return true;
	}

	public VertexCompressionType SupportsCompressedVertices() {
		throw new NotImplementedException();
	}

	public bool SupportsCubeMaps() {
		return true;
	}

	public bool SupportsFetch4() {
		throw new NotImplementedException();
	}

	public bool SupportsGLMixedSizeTargets() {
		throw new NotImplementedException();
	}

	public bool SupportsHardwareLighting() {
		throw new NotImplementedException();
	}

	public bool SupportsHDR() {
		throw new NotImplementedException();
	}

	public bool SupportsHDRMode(HDRType nHDRMode) {
		switch (nHDRMode) {
			case HDRType.None:
				return true;
			case HDRType.Integer:
				return MaxHDRType == HDRType.Integer || MaxHDRType == HDRType.Float;
			case HDRType.Float:
				return MaxHDRType == HDRType.Float;
		}
		return false;
	}

	public bool SupportsMipmappedCubemaps() {
		return false;
	}

	public bool SupportsNonPow2Textures() => true;

	public bool SupportsOverbright() {
		throw new NotImplementedException();
	}

	public bool SupportsPixelShaders_1_4() {
		return true;
	}

	public bool SupportsPixelShaders_2_0() {
		return true;
	}

	public bool SupportsPixelShaders_2_b() {
		return true;
	}

	public bool SupportsShaderModel_3_0() {
		return true;
	}

	public bool SupportsSpheremapping() {
		throw new NotImplementedException();
	}

	public bool SupportsSRGB() {
		return true;
	}

	public bool SupportsStaticControlFlow() {
		// throw new NotImplementedException();
		return true;
	}

	public bool SupportsStaticPlusDynamicLighting() {
		return true;
	}

	public bool SupportsStreamOffset() {
		return true;
	}

	public bool SupportsVertexAndPixelShaders() {
		return true;
	}

	public bool SupportsVertexShaders_2_0() {
		throw new NotImplementedException();
	}

	public nint TextureMemorySize() {
		throw new NotImplementedException();
	}

	public bool UseFastClipping() {
		return false;
	}

	static readonly ConVar r_shader_srgb = new("r_shader_srgb", "0", 0, "-1 = use hardware caps. 0 = use hardware srgb. 1 = use shader srgb(software lookup)");
	public bool UsesSRGBCorrectBlending() {
		return r_shader_srgb.GetInt() == 0;
	}
}
