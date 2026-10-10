using Source.Common.Bitmap;
using Source.Common.MaterialSystem;

using System.Drawing;
using System.Numerics;

namespace Source.MaterialSystem;

public interface ITextureInternal : ITexture
{
	public static ReadOnlySpan<char> NormalizeTextureName(ReadOnlySpan<char> name, Span<char> outName) {
		name = name.SliceNullTerminatedString();

		int len = name.Length + 1;
		if (len <= 5 || 0 != stricmp(name[(len - 5)..], ".hdr"))
			StrTools.StripExtension(name, outName);
		else
			name.ClampedCopyTo(outName);

		strlower(outName);
		StrTools.FixSlashes(outName, '/');

		return outName.SliceNullTerminatedString();
	}

	void Bind(Sampler sampler);
	void Bind(Sampler sampler, int frame);
	int GetTextureHandle(int frame, int textureChannel = 0);
	void OnRestore();
	void SetFilteringAndClampingMode(bool onlyLodValues = false);
	void Precache();
	bool SetRenderTarget(int rt, ITexture? depthTexture = null);
	void CopyFrameBufferToMe(int renderTargetID = 0, Rectangle? srcRect = null, Rectangle? dstRect = null);
	void GetReflectivity(out Vector3 reflectivity);

	public static readonly ITextureInternal EnvCubemap = new EnvCubemapSentinel();

	public static bool IsTextureInternalEnvCubemap(ITexture? texture) {
		return ReferenceEquals(texture, EnvCubemap);
	}
}

file sealed class EnvCubemapSentinel : ITextureInternal
{
	public ReadOnlySpan<char> GetName() => "env_cubemap";
	public int GetMappingWidth() => throw new NotSupportedException();
	public int GetMappingHeight() => throw new NotSupportedException();
	public int GetActualWidth() => throw new NotSupportedException();
	public int GetActualHeight() => throw new NotSupportedException();
	public int GetNumAnimationFrames() => throw new NotSupportedException();
	public bool IsTranslucent() => throw new NotSupportedException();
	public bool IsMipmapped() => throw new NotSupportedException();
	public void GetLowResColorSample(float s, float t, Span<float> color) => throw new NotSupportedException();
	public Span<byte> GetResourceData(uint type) => throw new NotSupportedException();
	public void SetTextureRegenerator(ITextureRegenerator textureRegen) => throw new NotSupportedException();
	public void IncrementReferenceCount() => throw new NotSupportedException();
	public void DecrementReferenceCount() => throw new NotSupportedException();
	public void DeleteIfUnreferenced() => throw new NotSupportedException();
	public void Download(Rectangle rect = default, int additionalCreationFlags = 0) => throw new NotSupportedException();
	public void CopyFrameBufferToMe(int renderTargetID = 0, Rectangle? srcRect = null, Rectangle? dstRect = null) => throw new NotSupportedException();
	public nint GetApproximateVidMemBytes() => throw new NotSupportedException();
	public bool IsError() => false;
	public bool IsVolumeTexture() => throw new NotSupportedException();
	public int GetMappingDepth() => throw new NotSupportedException();
	public int GetActualDepth() => throw new NotSupportedException();
	public ImageFormat GetImageFormat() => throw new NotSupportedException();
	public NormalDecodeMode GetNormalDecodeMode() => throw new NotSupportedException();
	public bool IsRenderTarget() => throw new NotSupportedException();
	public bool IsCubeMap() => throw new NotSupportedException();
	public bool IsNormalMap() => throw new NotSupportedException();
	public bool IsProcedural() => throw new NotSupportedException();
	public void SwapContents(ITexture other) => throw new NotSupportedException();
	public int GetFlags() => throw new NotSupportedException();
	public void ForceLODOverride(int numLodOverrideUpOrDown) => throw new NotSupportedException();
	public bool SaveToFile(ReadOnlySpan<char> fileName) => throw new NotSupportedException();
	public void Dispose() { }
	public void Bind(Sampler sampler) => throw new NotImplementedException();
	public void Bind(Sampler sampler, int frame) => throw new NotSupportedException();
	public int GetTextureHandle(int v) => throw new NotSupportedException();
	public void OnRestore() => throw new NotSupportedException();
	public void SetFilteringAndClampingMode(bool onlyLodValues = false) => throw new NotSupportedException();
	public void Precache() => throw new NotSupportedException();
	public bool SetRenderTarget(int rt, ITexture? depthTexture = null) => throw new NotSupportedException();
	public void GetReflectivity(out Vector3 reflectivity) => throw new NotSupportedException();
	public int GetTextureHandle(int frame, int textureChannel = 0) => throw new NotImplementedException();
}
