// TODO: Remove unused flags that aren't applicable in our use cases.
// (although this goal applies to the entire project, frankly)

using Source.Bitmap;
using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Filesystem;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.ShaderAPI;

using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.MaterialSystem;

public enum InternalTextureFlags
{
	Error = 0x00000001,
	Allocated = 0x00000002,
	Excluded = 0x00000020, // actual exclusion state
	ShouldExclude = 0x00000040, // desired exclusion state
}
public struct TexDimensions
{
	public ushort Width;
	public ushort Height;
	public ushort MipCount;
	public ushort Depth;

	public TexDimensions(ushort width = 0, ushort height = 0, ushort mips = 0, ushort depth = 1) {
		Width = width;
		Height = height;
		MipCount = mips;
		Depth = depth;
	}
}

public class Texture(MaterialSystem materials) : ITextureInternal
{
	int RefCount;

	public void DeleteIfUnreferenced() {
		if (RefCount > 0)
			return;
		materials.TextureSystem.RemoveTexture(this);
	}

	public void Dispose() {
		FreeShaderAPITextures();
		ReleaseTextureHandles();
	}

	public void Download(out Rectangle rect, int additionalCreationFlags = 0) {
		throw new NotImplementedException();
	}

	public void ForceLODOverride(int numLodOverrideUpOrDown) {
		throw new NotImplementedException();
	}

	public int GetActualDepth() => DimsActual.Depth;
	public int GetActualHeight() => DimsActual.Height;
	public int GetActualWidth() => DimsActual.Width;

	public nint GetApproximateVidMemBytes() {
		throw new NotImplementedException();
	}

	public int GetFlags() => (int)Flags;
	public ImageFormat GetImageFormat() => ImageFormat;

	public void GetLowResColorSample(float s, float t, Span<float> color) {
		throw new NotImplementedException();
	}

	public int GetMappingDepth() => DimsMapping.Depth;

	public int GetMappingHeight() => DimsMapping.Height;

	public int GetMappingWidth() => DimsMapping.Width;

	public ReadOnlySpan<char> GetName() => Name;
	public ReadOnlySpan<char> GetTextureGroupName() => TextureGroupName;

	public NormalDecodeMode GetNormalDecodeMode() {
		throw new NotImplementedException();
	}

	public int GetNumAnimationFrames() {
		return FrameCount;
	}

	public Span<byte> GetResourceData(uint type) {
		throw new NotImplementedException();
	}

	public void IncrementReferenceCount() => ++RefCount;

	public void DecrementReferenceCount() => --RefCount;

	public bool IsCubeMap() => ((TextureFlags)Flags & TextureFlags.EnvMap) != 0;
	public bool IsError() => ((InternalTextureFlags)InternalFlags & InternalTextureFlags.Error) != 0;
	public bool IsMipmapped() => ((TextureFlags)Flags & TextureFlags.NoMip) == 0;
	public bool IsNormalMap() => ((TextureFlags)Flags & TextureFlags.Normal) != 0;
	public bool IsProcedural() => ((TextureFlags)Flags & TextureFlags.Procedural) != 0;
	public bool IsRenderTarget() => ((TextureFlags)Flags & TextureFlags.RenderTarget) != 0;
	public bool IsTranslucent() => ((((TextureFlags)Flags) & TextureFlags.OneBitAlpha) | (((TextureFlags)Flags) & TextureFlags.EightBitAlpha)) != 0;
	public bool IsVolumeTexture() => DimsMapping.Depth > 1;

	public bool SaveToFile(ReadOnlySpan<char> fileName) {
		throw new NotImplementedException();
	}

	public void SetTextureRegenerator(ITextureRegenerator? textureRegen) {
		TextureRegenerator = textureRegen;
	}

	public void SwapContents(ITexture other) {
		throw new NotImplementedException();
	}

	public void Init(int w, int h, int d, ImageFormat fmt, int flags, int frameCount) {
		SetErrorTexture(false);

		FreeShaderAPITextures();
		ReleaseTextureHandles();

		DimsMapping.Width = (ushort)w;
		DimsMapping.Height = (ushort)h;
		DimsMapping.Depth = (ushort)d;
		ImageFormat = fmt;
		FrameCount = (ushort)frameCount;

		DimsActual.Width = DimsActual.Height = 0;
		DimsActual.Depth = 1;
		DimsActual.MipCount = 0;

		DimsAllocated.Width = 0;
		DimsAllocated.Height = 0;
		DimsAllocated.Depth = 0;
		DimsAllocated.MipCount = 0;
		StreamingMips = 0;

		Flags &= ~(uint)TextureFlags.DepthRenderTarget;
		Flags |= (uint)flags;

		ResidenceTarget = ResidencyType.None;
		ResidenceCurrent = ResidencyType.None;

		AllocateTextureHandles();
	}

	internal void InitFileTexture(ReadOnlySpan<char> fileName, ReadOnlySpan<char> textureGroupName) {
		SetName(fileName);
		TextureGroupName = new(textureGroupName);
	}

	private void SetName(ReadOnlySpan<char> fileName) {
		Span<char> cleanName = stackalloc char[MATERIAL_MAX_PATH];
		Name = new(ITextureInternal.NormalizeTextureName(fileName, cleanName));
	}

	static readonly ThreadLocal<IVTFTexture> VTFTextures = new();

	private IVTFTexture GetScratchVTFTexture() {
		if (!VTFTextures.IsValueCreated)
			VTFTextures.Value = IVTFTexture.Create();

		return VTFTextures.Value!;
	}

	private void ReleaseScratchVTFTexture(IVTFTexture scratchVTF) {
		StreamingVTF = null;
	}


	public void GetReflectivity(out Vector3 reflectivity) {
		Precache();
		reflectivity = Reflectivity;
	}

	public void Precache() {
		if (IsRenderTarget() || IsProcedural())
			return;

		if (HasBeenAllocated())
			return;

		if (Name.Equals("env_cubemap", StringComparison.InvariantCulture))
			return;

		int nAdditionalFlags = 0;
		if ((Flags & (uint)TextureFlags.Streamable) != 0) {
			// If we were previously streamed in, make sure we still do this time around.
			nAdditionalFlags = (int)TextureFlags.StreamableCoarse;
			Assert((Flags & (long)TextureFlags.StreamableFine) == 0);
		}

		IVTFTexture vtfTexture = GetScratchVTFTexture();

		// The texture name doubles as the relative file name
		// It's assumed to have already been set by this point	
		// Compute the cache name
		Span<char> cacheFileName = stackalloc char[MATERIAL_MAX_PATH];
		sprintf(cacheFileName, "materials/%s.vtf").S(Name);

		ushort nHeaderSize = IVTFTexture.FileHeaderSize(IVTFTexture.VTF_MAJOR_VERSION);
		Span<byte> mem = stackalloc byte[nHeaderSize];
		cacheFileName = cacheFileName.SliceNullTerminatedString();
		if (!materials.FileSystem.ReadFile(cacheFileName, null, mem, 0)) {
			goto precacheFailed;
		}
		unsafe {
			fixed (byte* pMem = mem) {
				using UnmanagedMemoryStream vtfStream = new UnmanagedMemoryStream(pMem, mem.Length);
				if (!vtfTexture.Unserialize(vtfStream, true)) {
					Warning($"Error reading material \"{cacheFileName}\"\n");
					goto precacheFailed;
				}

				// NOTE: Don't set the image format in case graphics are active
				Reflectivity = vtfTexture.Reflectivity();
				DimsMapping.Width = (ushort)vtfTexture.Width();
				DimsMapping.Height = (ushort)vtfTexture.Height();
				DimsMapping.Depth = (ushort)vtfTexture.Depth();
				Flags = (uint)(vtfTexture.Flags() | nAdditionalFlags);
				FrameCount = (ushort)vtfTexture.FrameCount();
				if (TextureHandles == null) {
					AllocateTextureHandles();
				}

				return;
			}
		}

	precacheFailed:
		Reflectivity = new(0, 0, 0);
		DimsMapping.Width = 32;
		DimsMapping.Height = 32;
		DimsMapping.Depth = 1;
		Flags = (uint)TextureFlags.NoMip;
		SetErrorTexture(true);
		FrameCount = 1;
		if (TextureHandles == null)
			AllocateTextureHandles();

	}

	/// <summary>
	/// FrameCount and TextureHandles are closely associated
	/// todo: can we just make it an array in C# land with no FrameCount...?
	/// </summary>
	private void AllocateTextureHandles() {
		Assert(TextureHandles == null);
		Assert(FrameCount > 0);
		if ((Flags & (uint)TextureFlags.DepthRenderTarget) != 0)
			Assert(FrameCount >= 2);

		TextureHandles = new ShaderAPITextureHandle_t[FrameCount];
		for (int i = 0; i < FrameCount; i++)
			TextureHandles[i] = INVALID_SHADERAPI_TEXTURE_HANDLE;
	}
	private void ReleaseTextureHandles() {
		if (TextureHandles != null) {
			TextureHandles = null;
		}
	}

	private void SetErrorTexture(bool v) {
		if (v)
			InternalFlags |= (uint)InternalTextureFlags.Error;
		else
			InternalFlags &= ~(uint)InternalTextureFlags.Error;
	}

	private bool HasBeenAllocated() {
		return (InternalFlags & (int)InternalTextureFlags.Allocated) != 0;
	}

	public void CopyFrameBufferToMe(int renderTargetID = 0, Rectangle? srcRect = null, Rectangle? dstRect = null) {
		Assert(TextureHandles != null && FrameCount >= 1);

		if (TextureHandles != null && FrameCount >= 1) {
			Rectangle src = srcRect.GetValueOrDefault(), dst = dstRect.GetValueOrDefault();
			materials.ShaderAPI.CopyRenderTargetToTextureEx(TextureHandles[0], renderTargetID,
				ref srcRect.HasValue ? ref src : ref Unsafe.NullRef<Rectangle>(),
				ref dstRect.HasValue ? ref dst : ref Unsafe.NullRef<Rectangle>());
		}
	}

	public void Bind(Sampler sampler) => Bind(sampler, 0);
	public void Bind(Sampler sampler, int frame) {
		if (HasBeenAllocated())
			materials.ShaderAPI.BindTexture(sampler, TextureHandles![frame]);
		else
			Warning($"Tried to bind texture {GetName()}, but texture handles are not valid.\n");
	}

	public void Download(Rectangle rect, int additionalCreationFlags) {
		if (materials.ShaderAPI.CanDownloadTextures()) {
			Flags |= (uint)additionalCreationFlags;
			DownloadTexture(rect);
		}
	}

	private void DownloadTexture(Rectangle rect, bool copyFromCurrent = false) {
		if (!materials.ShaderDevice.IsUsingGraphics())
			return;

		if (rect == default) {
			ReconstructTexture(copyFromCurrent);
		}
		else {
			Assert(!copyFromCurrent);
			ReconstructPartialTexture(rect);
		}

		SetFilteringAndClampingMode();

		if (((InternalTextureFlags)InternalFlags & InternalTextureFlags.ShouldExclude) > 0)
			InternalFlags |= (uint)InternalTextureFlags.Excluded;
		else
			InternalFlags &= ~(uint)InternalTextureFlags.Excluded;

		ActualDimensionLimit = DesiredDimensionLimit;
	}

	private void SetFilteringAndClampingMode(bool onlyLodValues = false) {
		if (!HasBeenAllocated())
			return;

		for (int frame = 0; frame < FrameCount; ++frame) {
			Modify(frame);
			if (!onlyLodValues) {
				SetWrapState();
				SetFilterState();
			}
			else
				SetLodState();
		}
	}

	private void SetLodState() {
	}

	private void SetWrapState() {
		if ((Flags & (int)TextureFlags.Border) != 0) {
			ShaderAPI.TexWrap(TexCoordComponent.S, TexWrapMode.Border);
			ShaderAPI.TexWrap(TexCoordComponent.T, TexWrapMode.Border);
			ShaderAPI.TexWrap(TexCoordComponent.U, TexWrapMode.Border);
			return;
		}

		if ((Flags & (int)TextureFlags.ClampS) != 0)
			ShaderAPI.TexWrap(TexCoordComponent.S, TexWrapMode.Clamp);
		else
			ShaderAPI.TexWrap(TexCoordComponent.S, TexWrapMode.Repeat);

		if ((Flags & (int)TextureFlags.ClampT) != 0)
			ShaderAPI.TexWrap(TexCoordComponent.T, TexWrapMode.Clamp);
		else
			ShaderAPI.TexWrap(TexCoordComponent.T, TexWrapMode.Repeat);

		if ((Flags & (int)TextureFlags.ClampU) != 0)
			ShaderAPI.TexWrap(TexCoordComponent.U, TexWrapMode.Clamp);
		else
			ShaderAPI.TexWrap(TexCoordComponent.U, TexWrapMode.Repeat);
	}

	private void SetFilterState() {
		if ((Flags & (int)TextureFlags.PointSample) != 0) {
			ShaderAPI.TexMinFilter(TexFilterMode.Nearest);
			ShaderAPI.TexMagFilter(TexFilterMode.Nearest);
			return;
		}

		bool enableMipmapping = (Flags & (int)TextureFlags.NoMip) == 0;
		if (!enableMipmapping) {
			ShaderAPI.TexMinFilter(TexFilterMode.Linear);
			ShaderAPI.TexMagFilter(TexFilterMode.Linear);
			return;
		}

		bool isAnisotropic = false, isTrilinear = false;
		if (g_config.ForceAnisotropicLevel > 1 && HardwareConfig.MaximumAnisotropicLevel() > 1)
			isAnisotropic = true;
		else if (g_config.ForceTrilinear()) {
			isAnisotropic = ((Flags & (int)TextureFlags.Anisotropic) != 0) && (HardwareConfig.MaximumAnisotropicLevel() > 1);
			isTrilinear = true;
		}
		else {
			isAnisotropic = ((Flags & (int)TextureFlags.Anisotropic) != 0) && (HardwareConfig.MaximumAnisotropicLevel() > 1);
			isTrilinear = (Flags & (int)TextureFlags.Trilinear) != 0;
		}

		if (isAnisotropic) {
			ShaderAPI.TexMinFilter(TexFilterMode.Anisotropic);
			ShaderAPI.TexMagFilter(TexFilterMode.Anisotropic);
		}
		else {
			if (isTrilinear) {
				ShaderAPI.TexMinFilter(TexFilterMode.LinearMipmapLinear);
				ShaderAPI.TexMagFilter(TexFilterMode.Linear);
			}
			else {
				ShaderAPI.TexMinFilter(TexFilterMode.LinearMipmapNearest);
				ShaderAPI.TexMagFilter(TexFilterMode.Linear);
			}
		}

		SetLodState();
	}

	private void ReconstructTexture(bool copyFromCurrent) {
		int oldWidth = DimsAllocated.Width;
		int oldHeight = DimsAllocated.Height;
		int oldDepth = DimsAllocated.Depth;
		int oldMipCount = DimsAllocated.MipCount;
		int oldFrameCount = FrameCount;

		string? resolvedFilename = null;
		IVTFTexture? vtfTexture = null;

		{
			if (IsProcedural()) {
				// This will call the installed texture bit regeneration interface
				vtfTexture = ReconstructProceduralBits();
			}
			else if (IsRenderTarget()) {
				// Compute the actual size + format based on the current mode
				bool ignorePicmip = RenderTargetSizeMode != RenderTargetSizeMode.LiteralPicmip;
				ComputeActualSize(ignorePicmip);
			}
			else if (copyFromCurrent) {
				ComputeActualSize(false, null, true);
			}
			else {
				NotifyUnloadedFile();

				Span<char> cacheFileName = stackalloc char[MATERIAL_MAX_PATH];
				GetCacheFilename(ref cacheFileName);

				// Get the data from disk...
				// NOTE: Reloading the texture bits can cause the texture size, frames, format, pretty much *anything* can change.
				vtfTexture = LoadTextureBitsFromFile(cacheFileName, out resolvedFilename);
			}
		}

		if (!HasBeenAllocated() ||
			DimsAllocated.Width != oldWidth || DimsAllocated.Height != oldHeight ||
			DimsAllocated.Depth != oldDepth || DimsAllocated.MipCount != oldMipCount ||
			FrameCount != oldFrameCount) {

			const bool canStretchRectTextures = true;
			bool shouldMigrateTextures = ((Flags & (uint)TextureFlags.StreamableFine) != 0) && FrameCount == oldFrameCount;

			// If we're just streaming in more data--or demoting ourselves, do a migration instead. 
			if (copyFromCurrent || (canStretchRectTextures && shouldMigrateTextures)) {
				MigrateShaderAPITextures();

				// Ahh--I feel terrible about this, but we genuinely don't need anything else if we're streaming.
				if (copyFromCurrent)
					return;
			}
			else {
				// If we're doing a wholesale copy, we need to restore these values that will be cleared by FreeShaderAPITextures.
				// Record them here, restore them below.
				uint restoreStreamingFlag = (Flags & (uint)TextureFlags.Streamable);
				ResidencyType restoreResidenceCurrent = ResidenceCurrent;
				ResidencyType restoreResidenceTarget = ResidenceTarget;

				if (HasBeenAllocated()) {
					FreeShaderAPITextures();
				}

				// Create the shader api textures
				if (!AllocateShaderAPITextures())
					return;

				// Restored once we successfully allocate the shader api textures, but only if we're 
				// 
				if (!canStretchRectTextures && shouldMigrateTextures) {
					Flags |= restoreStreamingFlag;
					ResidenceCurrent = restoreResidenceCurrent;
					ResidenceTarget = restoreResidenceTarget;
				}
			}
		}
		else if (copyFromCurrent) {
			AssertMsg(false, "We're about to crash, last chance to examine this texture.");
		}


		// Render Targets just need to be cleared, they have no upload
		if (IsRenderTarget()) {
			if (!IsDepthTextureFormat(ImageFormat)) {
				using MatRenderContextPtr renderContext = new(materials);
				ITexture? thisTexture = GetEmbeddedTexture(0);
				renderContext.PushRenderTargetAndViewport(thisTexture);
				ShaderAPI.ClearColor4ub(0, 0, 0, 0xFF);
				ShaderAPI.ClearBuffers(true, false, false, DimsActual.Width, DimsActual.Height);
				renderContext.PopRenderTargetAndViewport();
			}

			return;
		}

		// Blit down the texture faces, frames, and mips into the board memory
		int firstFace, faceCount;
		GetDownloadFaceCount(out firstFace, out faceCount);

		WriteDataToShaderAPITexture(FrameCount, faceCount, firstFace, DimsActual.MipCount, vtfTexture, ImageFormat);

		ReleaseScratchVTFTexture(vtfTexture);
		vtfTexture = null;
	}

	private ITexture? GetEmbeddedTexture(int index) => index == 0 ? this : null;

	private bool IsDepthTextureFormat(ImageFormat imageFormat) => imageFormat == ImageFormat.NV_DST16 ||
			 imageFormat == ImageFormat.NV_DST24 ||
			 imageFormat == ImageFormat.NV_IntZ ||
			 imageFormat == ImageFormat.NV_RawZ ||
			 imageFormat == ImageFormat.ATI_DST16 ||
			 imageFormat == ImageFormat.ATI_DST24;

	private void WriteDataToShaderAPITexture(ushort frameCount, int faceCount, int firstFace, ushort mipCount, IVTFTexture? vtfTexture, ImageFormat imageFormat) {
		if ((Flags & (uint)TextureFlags.StagingMemory) > 0)
			return;

		for (int i = 0; i < FrameCount; i++) {
			Modify(i);
			materials.ShaderAPI.TexImageFromVTF(vtfTexture, i);
		}
	}

	private void Modify(int frame) {
		Assert(frame >= 0 && frame < FrameCount);
		Assert(HasBeenAllocated());
		materials.ShaderAPI.ModifyTexture(TextureHandles![frame]);
	}

	private void GetDownloadFaceCount(out int firstFace, out int faceCount) {
		faceCount = 1;
		firstFace = 0;
		if (IsCubeMap()) {
			if (materials.HardwareConfig.SupportsCubeMaps()) {
				faceCount = (int)CubeMapFaceIndex.Count - 1;
			}
			else {
				// This will cause us to use the spheremap instead of the cube faces
				// in the case where we don't support cubemaps
				firstFace = (int)CubeMapFaceIndex.Spheremap;
			}
		}
	}

	private bool AllocateShaderAPITextures() {
		int count = FrameCount;

		CreateTextureFlags createFlags = 0;
		if ((Flags & (uint)TextureFlags.EnvMap) > 0 && materials.HardwareConfig.SupportsCubeMaps()) {
			createFlags |= CreateTextureFlags.Cubemap;
		}

		bool bIsFloat = (ImageFormat == ImageFormat.RGBA16161616F) || (ImageFormat == ImageFormat.R32F) ||
						(ImageFormat == ImageFormat.RGB323232F) || (ImageFormat == ImageFormat.RGBA32323232F);

		// Don't do sRGB on floating point textures
		if ((Flags & (uint)TextureFlags.SRGB) > 0 && !bIsFloat) {
			createFlags |= CreateTextureFlags.SRGB;    // for Posix/GL only
		}

		if ((Flags & (uint)TextureFlags.RenderTarget) > 0) {
			createFlags |= CreateTextureFlags.RenderTarget;

			// This here is simply so we can use a different call to
			// create the depth texture below	
			if ((Flags & (uint)TextureFlags.DepthRenderTarget) > 0 && (count == 2)) //nCount must be 2 on pc
			{
				--count;
			}
		}
		else {
			// If it's not a render target, use the texture manager in dx
			if ((Flags & (uint)TextureFlags.StagingMemory) > 0)
				createFlags |= CreateTextureFlags.SysMem;
			else {
				createFlags |= CreateTextureFlags.Managed;
			}
		}

		if ((Flags & (uint)TextureFlags.PointSample) > 0) {
			createFlags |= CreateTextureFlags.UnfilterableOK;
		}

		if ((Flags & (uint)TextureFlags.VertexTexture) > 0) {
			createFlags |= CreateTextureFlags.VertexTexture;
		}

		int nCopies = 1;
		if (IsProcedural()) {
			// This is sort of hacky... should we store the # of copies in the VTF?
			if ((Flags & (uint)TextureFlags.SingleCopy) == 0) {
				// FIXME: That 6 there is heuristically what I came up with what I
				// need to get eyes not to stall on map alyx3. We need a better way
				// of determining how many copies of the texture we should store.
				nCopies = 6;
			}
		}

		// For depth only render target: adjust texture width/height
		// Currently we just leave it the same size, will update with further testing
		int shaderApiCreateTextureDepth = ((Flags & (uint)TextureFlags.DepthRenderTarget) != 0 && (OriginalRenderTargetType == RenderTargetType.OnlyDepth)) ? 1 : DimsAllocated.Depth;

		// Create all animated texture frames in a single call
		materials.ShaderAPI.CreateTextures(
			TextureHandles.AsSpan(0, count),
			DimsAllocated.Width, DimsAllocated.Height, shaderApiCreateTextureDepth, ImageFormat, DimsAllocated.MipCount,
			nCopies, (int)createFlags, GetName(), GetTextureGroupName());

		int accountingCount = count;

		// Create the depth render target buffer
		if ((Flags & (uint)TextureFlags.DepthRenderTarget) > 0) {
			Assert(count == 1);

			Span<char> debugName = stackalloc char[128];
			sprintf(debugName, "%s_ZBuffer").S(GetName());
			Assert(FrameCount >= 2);
			TextureHandles![1] = materials.ShaderAPI.CreateDepthTexture(
					ImageFormat,
					DimsAllocated.Width,
					DimsAllocated.Height,
					debugName,
					OriginalRenderTargetType == RenderTargetType.OnlyDepth);
			accountingCount += 1;
		}

		InternalFlags |= (uint)InternalTextureFlags.Allocated;

		return true;
	}

	private void FreeShaderAPITextures() {
		if (TextureHandles != null && HasBeenAllocated()) {
			for (int i = FrameCount; --i >= 0;) {
				if (materials.ShaderAPI.IsTexture(TextureHandles[i])) {
					materials.ShaderAPI.DeleteTexture(TextureHandles[i]);
					TextureHandles[i] = INVALID_SHADERAPI_TEXTURE_HANDLE;
				}
			}
		}

		InternalFlags &= ~(uint)InternalTextureFlags.Allocated;
	}

	private void MigrateShaderAPITextures() {
		throw new NotImplementedException();
	}

	TextureLODControlSettings CachedFileLodSettings;

	private IVTFTexture? LoadTextureBitsFromFile(Span<char> cacheFileName, out string? resolvedFilename) {
		resolvedFilename = null;

		uint stripFlags;
		IVTFTexture? vtfTexture = StreamingVTF;
		if (vtfTexture == null) {
			vtfTexture = GetScratchVTFTexture();

			IFileHandle? fileHandle = null;

			if (!GetFileHandle(out fileHandle, cacheFileName, out resolvedFilename))
				return HandleFileLoadFailedTexture(vtfTexture);

			TextureLODControlSettings settings = CachedFileLodSettings;
			if (!SLoadTextureBitsFromFile(ref vtfTexture, fileHandle, Flags, ref settings, DesiredDimensionLimit, ref StreamingMips, GetName(), cacheFileName, out DimsMapping, out DimsActual, out DimsAllocated, out stripFlags)) {
				fileHandle.Dispose();
				return HandleFileLoadFailedTexture(vtfTexture);
			}

			fileHandle?.Dispose();
		}

		if ((Flags & (uint)TextureFlags.StreamableFine) == 0) {
			TexDimensions actual = DimsActual, allocated = DimsAllocated;

			Init(DimsMapping.Width, DimsMapping.Height, DimsMapping.Depth, vtfTexture.Format(), vtfTexture.Flags(), vtfTexture.FrameCount());

			DimsActual = actual;
			DimsAllocated = allocated;
		}

		// TODO: How does Source stream textures?
		if (ConvertToActualFormat(vtfTexture)) {

		}

		return vtfTexture;
	}

	private bool SLoadTextureBitsFromFile(ref IVTFTexture vtfTexture, IFileHandle fileHandle, uint flags,
											ref TextureLODControlSettings settings, ushort desiredDimensionLimit,
											ref ushort streamingMips, ReadOnlySpan<char> name, Span<char> cacheFileName,
											out TexDimensions dimsMapping, out TexDimensions dimsActual, out TexDimensions dimsAllocated,
											out uint stripFlags) {
		// TODO; finish the complexities of texture loading
		if (!vtfTexture!.Unserialize(fileHandle.Stream, false)) {
			Warning($"VTF texture '{fileHandle.GetPath()}' failed to load!\n");
		}

		dimsMapping = new() {
			Width = (ushort)vtfTexture.Width(),
			Height = (ushort)vtfTexture.Height(),
			Depth = (ushort)vtfTexture.Depth(),
			MipCount = (ushort)vtfTexture.MipCount()
		};

		uint fullFlags = (uint)vtfTexture.Flags() | flags;

		int nMipSkipCount = ComputeMipSkipCount(name, dimsMapping, false, vtfTexture, fullFlags, DesiredDimensionLimit, ref StreamingMips, out settings, out dimsActual, out dimsAllocated, out stripFlags);


		return true;
	}

	unsafe byte* ImageData;

	private bool ConvertToActualFormat(IVTFTexture vtfTexture) {
		if (!materials.ShaderDevice.IsUsingGraphics())
			return false;

		bool converted = false;

		ImageFormat fmt = ImageFormat;

		ImageFormat dstFormat = ComputeActualFormat(vtfTexture.Format());
		if (fmt != dstFormat) {
			vtfTexture.ConvertImageFormat(dstFormat, false);

			ImageFormat = dstFormat;
			converted = true;
		}
		else if (materials.HardwareConfig.GetHDRType() == HDRType.Integer &&
					dstFormat == ImageFormat.RGBA16161616F) {
			// This is to force at most the precision of int16 for fp16 texture when running the integer path.
			vtfTexture.ConvertImageFormat(ImageFormat.RGBA16161616, false);
			vtfTexture.ConvertImageFormat(ImageFormat.RGBA16161616F, false);
			converted = true;
		}

		return converted;
	}

	private ImageFormat ComputeActualFormat(ImageFormat srcFormat) {
		ImageFormat dstFormat;
		bool bIsCompressed = srcFormat.IsCompressed();
		if (materials.HardwareConfig.SupportsCompressedTextures() && bIsCompressed) {
			// for the runtime compressed formats the srcFormat won't equal the dstFormat, and we need to return srcFormat here
			if (srcFormat.IsRuntimeCompressed()) {
				return srcFormat;
			}

			// don't do anything since we are already in a compressed format.
			dstFormat = materials.ShaderAPI.GetNearestSupportedFormat(srcFormat);
			Assert(dstFormat == srcFormat);
			return dstFormat;
		}

		// NOTE: Below this piece of code is only called when compressed textures are
		// turned off, or if the source texture is not compressed.

		if ((srcFormat == ImageFormat.UVWQ8888) || (srcFormat == ImageFormat.UV88) || (srcFormat == ImageFormat.UVLX8888)) {
			Assert(false);
		}

		if ((srcFormat == ImageFormat.UVWQ8888) || (srcFormat == ImageFormat.UV88) ||
			(srcFormat == ImageFormat.UVLX8888) || (srcFormat == ImageFormat.RGBA16161616) ||
			(srcFormat == ImageFormat.RGBA16161616F))
			dstFormat = materials.ShaderAPI.GetNearestSupportedFormat(srcFormat, false);
		else if ((Flags & (uint)(TextureFlags.EightBitAlpha | TextureFlags.OneBitAlpha)) != 0)
			dstFormat = materials.ShaderAPI.GetNearestSupportedFormat(ImageFormat.BGRA8888);
		else if (srcFormat == ImageFormat.I8)
			dstFormat = materials.ShaderAPI.GetNearestSupportedFormat(ImageFormat.I8);
		else
			dstFormat = materials.ShaderAPI.GetNearestSupportedFormat(ImageFormat.BGR888);

		return dstFormat;
	}

	private IVTFTexture? HandleFileLoadFailedTexture(IVTFTexture? vtfTexture) {
		vtfTexture.Init(32, 32, 1, ImageFormat.BGRA8888, (int)Flags, 1);
		Init(vtfTexture.Width(), vtfTexture.Height(), vtfTexture.Depth(), vtfTexture.Format(), vtfTexture.Flags(), vtfTexture.FrameCount());

		DimsAllocated.Width = DimsActual.Width = DimsMapping.Width = (ushort)vtfTexture.Width();
		DimsAllocated.Height = DimsActual.Height = DimsMapping.Height = (ushort)vtfTexture.Height();
		DimsAllocated.Depth = 1;
		DimsAllocated.MipCount = DimsActual.MipCount = 1;
		StreamingMips = 0;

		materials.TextureSystem.GenerateErrorTexture(this, vtfTexture);
		ConvertToActualFormat(vtfTexture);

		Flags &= ~(uint)TextureFlags.Procedural;
		SetErrorTexture(true);

		return vtfTexture;
	}

	private bool GetFileHandle([NotNullWhen(true)] out IFileHandle? fileHandle, Span<char> cacheFileName, out string? resolvedFilename) {
		fileHandle = null;
		resolvedFilename = null; // Requires OpenEx; do later
		while (fileHandle == null) {
			fileHandle = materials.FileSystem.Open(cacheFileName, FileOpenOptions.Read | FileOpenOptions.Binary, materials.GetForcedTextureLoadPathID());
			if (fileHandle == null) {
				break; // TODO; fallbacks
			}
		}

		if (fileHandle == null) {
			DevWarning($"\"{cacheFileName}\": can't be found on disk\n");
			return false;
		}

		return true;
	}

	private void GetCacheFilename(ref Span<char> pCacheFileName) {
		int written = sprintf(pCacheFileName, "materials/%s.vtf").S(Name);
		pCacheFileName = pCacheFileName[..written];
	}

	private void NotifyUnloadedFile() {

	}

	private int ComputeActualSize(bool ignorePicmip = false, IVTFTexture? vtfTexture = null, bool textureMigration = false) {
		uint stripFlags = 0;
		return ComputeMipSkipCount(GetName(), DimsMapping, ignorePicmip, vtfTexture, Flags, DesiredDimensionLimit, ref StreamingMips, out CachedFileLodSettings, out DimsActual, out DimsAllocated, out stripFlags);
	}

	private unsafe int ComputeMipSkipCount(ReadOnlySpan<char> readOnlySpan, TexDimensions mappingDims, bool ignorePicmip, IVTFTexture? vtfTexture, uint flags, ushort desiredDimensionLimit, ref ushort streamingMips, out TextureLODControlSettings cachedFileLodSettings, out TexDimensions dimsActual, out TexDimensions dimsAllocated, out uint outStripFlags) {
		TexDimensions actualDims = mappingDims, allocatedDims = new();

		bool bTextureMigration = (Flags & (uint)TextureFlags.Streamable) != 0;
		uint stripFlags = 0;

		int nClampX = actualDims.Width;  // no clamping (clamp to texture dimensions)
		int nClampY = actualDims.Height;
		int nClampZ = actualDims.Depth;

		// TODO: LOD controls

		// In case clamp values exceed texture dimensions, then fix up
		// the clamping values
		nClampX = Math.Min(nClampX, (int)actualDims.Width);
		nClampY = Math.Min(nClampY, (int)actualDims.Height);

		//
		// Honor dimension limit restrictions
		//
		if (DesiredDimensionLimit > 0) {
			while (nClampX > DesiredDimensionLimit ||
					nClampY > DesiredDimensionLimit) {
				nClampX >>= 1;
				nClampY >>= 1;
			}
		}

		//
		// Unless ignoring picmip, reflect the global picmip level in clamp dimensions
		//
		if (!ignorePicmip) {
			/*if ((Flags & (uint)CompiledVtfFlags.NoLOD) == 0 && (g_config.skipMipLevels > 0)) {
				for (int iDegrade = 0; iDegrade < g_config.skipMipLevels; ++iDegrade) {
					// don't go lower than 4, or dxt textures won't work properly
					if (nClampX > 4 &&
						 nClampY > 4) {
						nClampX >>= 1;
						nClampY >>= 1;
					}
				}
			}

			if (g_config.skipMipLevels < 0) {
				for (int iUpgrade = 0; iUpgrade < -g_config.skipMipLevels; ++iUpgrade) {
					if (nClampX < actualDims.m_nWidth &&
						 nClampY < actualDims.m_nHeight) {
						nClampX <<= 1;
						nClampY <<= 1;
					}
					else
						break;
				}
			}
			*/
		}

		//
		// Now use hardware settings to clamp our "clamping dimensions"
		//
		int hwWidth = materials.HardwareConfig.MaxTextureWidth();
		int hwHeight = materials.HardwareConfig.MaxTextureHeight();
		int hwDepth = materials.HardwareConfig.MaxTextureDepth();

		nClampX = Math.Min(nClampX, Math.Max(hwWidth, 4));
		nClampY = Math.Min(nClampY, Math.Max(hwHeight, 4));
		nClampZ = Math.Min(nClampZ, Math.Max(hwDepth, 1));

		// In case clamp values exceed texture dimensions, then fix up
		// the clamping values.
		nClampX = Math.Min(nClampX, actualDims.Width);
		nClampY = Math.Min(nClampY, actualDims.Height);
		nClampZ = Math.Min(nClampZ, actualDims.Depth);

		//
		// Clamp to the determined dimensions
		//
		int numMipsSkipped = 0; // will compute now when clamping how many mips we drop
		while ((actualDims.Width > nClampX) ||
				(actualDims.Height > nClampY) ||
				(actualDims.Depth > nClampZ)) {
			actualDims.Width >>= 1;
			actualDims.Height >>= 1;
			actualDims.Depth = (ushort)Math.Max(1, actualDims.Depth >> 1);

			++numMipsSkipped;
		}

		Assert(actualDims.Width > 0 && actualDims.Height > 0 && actualDims.Depth > 0);

		// Now that we've got the actual size, we can figure out the mip count
		actualDims.MipCount = ComputeActualMipCount(actualDims, Flags);

		// If we're streaming, cut down what we're loading.
		// We can only stream things that have a mipmap pyramid (not just a single mipmap).
		bool bHasSetAllocation = false;
		if ((Flags & (uint)TextureFlags.Streamable) == (uint)TextureFlags.StreamableCoarse) {
			if (actualDims.MipCount > 1) {
				allocatedDims.Width = actualDims.Width;
				allocatedDims.Height = actualDims.Height;
				allocatedDims.Depth = actualDims.Depth;
				allocatedDims.MipCount = actualDims.MipCount;
				const int STREAMING_START_MIPMAP = 3;

				for (int i = 0; i < STREAMING_START_MIPMAP; ++i) {
					// Stop when width or height is at 4 pixels (or less). We could do better, 
					// but some textures really can't function if they're less than 4 pixels (compressed textures, for example).
					if (allocatedDims.Width <= 4 || allocatedDims.Height <= 4)
						break;

					allocatedDims.Width >>= 1;
					allocatedDims.Height >>= 1;
					allocatedDims.Depth = (ushort)Math.Max(1, allocatedDims.Depth >> 1);
					allocatedDims.MipCount = (ushort)Math.Max(1, allocatedDims.MipCount - 1);

					++numMipsSkipped;
				}

				bHasSetAllocation = true;
			}
			else {
				// Clear out that we're streaming, this isn't a texture we can stream.
				stripFlags |= (uint)TextureFlags.StreamableCoarse;
			}
		}

		if (!bHasSetAllocation) {
			allocatedDims.Width = actualDims.Width;
			allocatedDims.Height = actualDims.Height;
			allocatedDims.Depth = actualDims.Depth;
			allocatedDims.MipCount = actualDims.MipCount;
		}

		dimsActual = actualDims;
		dimsAllocated = allocatedDims;
		outStripFlags = stripFlags;
		cachedFileLodSettings = default;

		// Returns the number we skipped
		return numMipsSkipped;
	}

	private ushort ComputeActualMipCount(TexDimensions actualDims, uint flags) {
		if ((flags & (uint)TextureFlags.EnvMap) > 0) {
			if (!materials.HardwareConfig.SupportsMipmappedCubemaps()) {
				return 1;
			}
		}

		if ((flags & (uint)TextureFlags.NoMip) != 0) {
			return 1;
		}

		// Unless ALLMIPS is set, we stop mips at 32x32
		const int nMaxMipSize = 32;
		if ((false && (Flags & (uint)TextureFlags.AllMips) == 0) || (true && (Flags & (uint)TextureFlags.Border) > 0)) {
			int nNumMipLevels = 1;
			int h = actualDims.Width;
			int w = actualDims.Height;
			while (Math.Min(w, h) > nMaxMipSize) {
				++nNumMipLevels;

				w >>= 1;
				h >>= 1;
			}
			return (ushort)nNumMipLevels;
		}

		return (ushort)ImageLoader.GetNumMipMapLevels(actualDims.Width, actualDims.Height, actualDims.Depth);
	}

	private IVTFTexture? ReconstructProceduralBits() {
		bool ignorePicmip = (Flags & (uint)(TextureFlags.StagingMemory | TextureFlags.IgnorePicmip)) != 0;
		ComputeActualSize(ignorePicmip);

		IVTFTexture texture = GetScratchVTFTexture();
		texture.Init(DimsActual.Width, DimsActual.Height, DimsActual.Depth, ComputeActualFormat(ImageFormat), (int)Flags, FrameCount);

		if (TextureRegenerator != null) {
			Rectangle rect = new();
			rect.X = rect.Y = 0;
			rect.Width = DimsActual.Width;
			rect.Height = DimsActual.Height;
			TextureRegenerator.RegenerateTextureBits(this, texture, in rect);
		}
		else {
			materials.TextureSystem.GenerateErrorTexture(this, texture);
		}
		return texture;
	}

	private void ReconstructPartialTexture(Rectangle rect) {
		Rectangle vtfRect;
		IVTFTexture vtfTexture = ReconstructPartialProceduralBits(in rect, out vtfRect);
		Assert(vtfTexture.Depth() == 1);
		if (!HasBeenAllocated()) {
			if (!AllocateShaderAPITextures())
				return;
		}
		GetDownloadFaceCount(out int firstFace, out int faceCount);

		// Blit down portions of the various VTF frames into the board memory
		int stride;
		Rectangle mipRect;
		for (int frame = 0; frame < FrameCount; ++frame) {
			Modify(frame);

			for (int iFace = 0; iFace < faceCount; ++iFace) {
				for (int iMip = 0; iMip < DimsActual.MipCount; ++iMip) {
					vtfTexture.ComputeMipLevelSubRect(in vtfRect, iMip, out mipRect);
					stride = vtfTexture.RowSizeInBytes(iMip);

					Span<byte> bits = vtfTexture.ImageData(frame, iFace + firstFace, iMip, mipRect.X, mipRect.Y, 0);

					materials.ShaderAPI.TexSubImage2D(
						iMip,
						iFace,
						mipRect.X,
						mipRect.Y,
						0,
						mipRect.Width,
						mipRect.Height,
						vtfTexture.Format(),
						stride,
						false,
						bits);
				}
			}
		}
	}

	private IVTFTexture ReconstructPartialProceduralBits(in Rectangle rect, out Rectangle vtfRect) {
		bool ignorePicmip = (Flags & (uint)(TextureFlags.StagingMemory | TextureFlags.IgnorePicmip)) != 0;
		ComputeActualSize(ignorePicmip);

		int sizeFactor = 1;
		int width = GetActualWidth();
		if (width != 0)
			sizeFactor = GetMappingWidth() / width;

		int mipSkipCount = 0;
		while (sizeFactor > 1) {
			sizeFactor >>= 1;
			++mipSkipCount;
		}

		ComputeMipLevelSubRect(in rect, mipSkipCount, out vtfRect);

		IVTFTexture? vtfTexture = GetScratchVTFTexture();

		vtfTexture.Init(DimsActual.Width, DimsActual.Height, DimsActual.Depth,
			ComputeActualFormat(ImageFormat), (int)Flags, FrameCount);

		if (TextureRegenerator != null)
			TextureRegenerator.RegenerateTextureBits(this, vtfTexture, vtfRect);
		else
			materials.TextureSystem.GenerateErrorTexture(this, vtfTexture);


		return vtfTexture;
	}

	private void ComputeMipLevelSubRect(in Rectangle rect, int mipLevel, out Rectangle subRect) {
		if (mipLevel == 0) {
			subRect = rect;
			return;
		}

		float flInvShrink = 1.0f / (float)(1 << mipLevel);
		subRect = new();
		subRect.X = (int)(rect.X * flInvShrink);
		subRect.Y = (int)(rect.Y * flInvShrink);
		subRect.Width = (int)MathF.Ceiling((rect.X + rect.Width) * flInvShrink) - rect.X;
		subRect.Height = (int)MathF.Ceiling((rect.Y + rect.Height) * flInvShrink) - rect.Y;
	}

	internal void InitProceduralTexture(ReadOnlySpan<char> textureName, ReadOnlySpan<char> textureGroup, int w, int h, int d, ImageFormat format, TextureFlags flags, ITextureRegenerator? generator) {
		Assert((flags & (TextureFlags.RenderTarget | TextureFlags.DepthRenderTarget)) == 0);

		SetName(textureName);

		// Eliminate flags that are inappropriate...
		flags &= ~TextureFlags.HintDXT5 | TextureFlags.OneBitAlpha | TextureFlags.EightBitAlpha | TextureFlags.RenderTarget | TextureFlags.DepthRenderTarget;

		// Insert required flags
		flags |= TextureFlags.Procedural;
		int nAlphaBits = ImageLoader.ImageFormatInfo(format).AlphaBits;
		if (nAlphaBits > 1) {
			flags |= TextureFlags.EightBitAlpha;
		}
		else if (nAlphaBits == 1) {
			flags |= TextureFlags.OneBitAlpha;
		}

		// Procedural textures are always one frame only
		Init(w, h, d, format, (int)flags, 1);

		SetTextureRegenerator(generator);

		TextureGroupName = new(textureGroup);
	}

	ITextureRegenerator? TextureRegenerator;

	public void OnRestore() {
		// May have to change whether or not we have a depth buffer.
		// Are we a render target?
		if ((Flags & (uint)TextureFlags.RenderTarget) != 0) {
			int newWidth = 0, newHeight = 0;

			// Did they not ask for a depth buffer?
			if (OriginalRenderTargetType == RenderTargetType.RenderTarget) {
				// But, did we force them to have one, or should we force them to have one this time around?
				bool shouldForce = ShaderAPI.DoRenderTargetsNeedSeparateDepthBuffer();
				bool didForce = (Flags & (uint)TextureFlags.DepthRenderTarget) != 0;
				if (shouldForce != didForce) {
					uint flags = Flags;
					int frameCount = FrameCount;
					if (shouldForce) {
						Assert((flags & (uint)TextureFlags.DepthRenderTarget) == 0);
						frameCount = 2;
						flags |= (uint)TextureFlags.DepthRenderTarget;
					}
					else {
						Assert((flags & (uint)TextureFlags.DepthRenderTarget) != 0);
						frameCount = 1;
						flags &= ~(uint)TextureFlags.DepthRenderTarget;
					}

					Shutdown();

					ApplyRenderTargetSizeMode(ref newWidth, ref newHeight, ImageFormat);

					Init(newWidth, newHeight, 1, ImageFormat, (int)flags, frameCount);
					return;
				}
			}

			// If we didn't recreate it up above, then we may need to resize it anyway if the framebuffer
			// got smaller than we are.
			ApplyRenderTargetSizeMode(ref newWidth, ref newHeight, ImageFormat);
			if (newWidth != DimsMapping.Width || newHeight != DimsMapping.Height) {
				Shutdown();
				Init(newWidth, newHeight, 1, ImageFormat, (int)Flags, FrameCount);
				return;
			}
		}
	}

	private void Shutdown() {
		// Frees the texture regen class
		TextureRegenerator = null;

		ResidenceTarget = ResidencyType.None;
		ResidenceCurrent = ResidencyType.None;

		// This deletes the textures
		FreeShaderAPITextures();
		ReleaseTextureHandles();
		NotifyUnloadedFile();
	}

	readonly IMaterialSystemHardwareConfig HardwareConfig = Singleton<IMaterialSystemHardwareConfig>();
	readonly IShaderAPI ShaderAPI = Singleton<IShaderAPI>();

	static int rtTexID = 0;
	public void InitRenderTarget(ReadOnlySpan<char> rtName, int w, int h, RenderTargetSizeMode sizeMode, ImageFormat format, RenderTargetType type, TextureFlags textureFlags, CreateRenderTargetFlags renderTargetFlags) {
		if (!rtName.IsEmpty)
			SetName(rtName);
		else {
			Span<char> newName = stackalloc char[128];
			sprintf(newName, "__render_target_%d").D(rtTexID++);
			SetName(newName.SliceNullTerminatedString());
		}

		if (0 != (renderTargetFlags & CreateRenderTargetFlags.HDR))
			if (HardwareConfig.GetHDRType() == HDRType.Float)
				format = ImageFormat.RGBA16161616F;

		int nFrameCount = 1;

		TextureFlags flags = TextureFlags.NoMip | TextureFlags.RenderTarget;
		flags |= textureFlags;

		if (type == RenderTargetType.NoDepth) {
			flags |= TextureFlags.NoDepthBuffer;
		}
		else if (type == RenderTargetType.WithDepth || type == RenderTargetType.OnlyDepth || ShaderAPI.DoRenderTargetsNeedSeparateDepthBuffer()) {
			flags |= TextureFlags.DepthRenderTarget;
			++nFrameCount;
		}

		OriginalRenderTargetType = type;
		RenderTargetSizeMode = sizeMode;
		OriginalRTWidth = (ushort)w;
		OriginalRTHeight = (ushort)h;
		ImageFormatInfo fmtInfo = ImageLoader.ImageFormatInfo(format);

		if (fmtInfo.AlphaBits > 1)
			flags |= TextureFlags.EightBitAlpha;
		else if (fmtInfo.AlphaBits == 1)
			flags |= TextureFlags.OneBitAlpha;

		ApplyRenderTargetSizeMode(ref w, ref h, format);

		Init(w, h, 1, format, (int)flags, nFrameCount);
		TextureGroupName = TEXTURE_GROUP_RENDER_TARGET;
	}

	readonly MaterialSystem_Config g_config = Singleton<MaterialSystem_Config>();

	private void ApplyRenderTargetSizeMode(ref int width, ref int height, ImageFormat format) {
		width = OriginalRTWidth;
		height = OriginalRTHeight;

		switch (RenderTargetSizeMode) {
			case RenderTargetSizeMode.FullFrameBuffer: {
					materials.GetRenderTargetFrameBufferDimensions(out width, out height);
					if (!HardwareConfig.SupportsNonPow2Textures()) {
						width = MathLib.FloorPow2(width + 1);
						height = MathLib.FloorPow2(height + 1);
					}
				}
				break;

			case RenderTargetSizeMode.FullFrameBufferRoundedUp: {
					materials.GetRenderTargetFrameBufferDimensions(out width, out height);
					if (!HardwareConfig.SupportsNonPow2Textures()) {
						width = MathLib.CeilPow2(width);
						height = MathLib.CeilPow2(height);
					}
				}
				break;

			case RenderTargetSizeMode.Picmip: {
					materials.GetRenderTargetFrameBufferDimensions(out int fbWidth, out int fbHeight);
					int picmip = g_config.SkipMipLevels;
					while (picmip > 0) {
						width >>= 1;
						height >>= 1;
						picmip--;
					}

					while (width > fbWidth) {
						width >>= 1;
					}
					while (height > fbHeight) {
						height >>= 1;
					}
				}
				break;

			case RenderTargetSizeMode.Default: {
					Assert((width & (width - 1)) == 0);
					Assert((height & (height - 1)) == 0);
					materials.GetRenderTargetFrameBufferDimensions(out int fbWidth, out int fbHeight);
					while (width > fbWidth) {
						width >>= 1;
					}
					while (height > fbHeight) {
						height >>= 1;
					}
				}
				break;

			case RenderTargetSizeMode.HDR: {
					materials.GetRenderTargetFrameBufferDimensions(out width, out height);
					width >>= 2;
					height >>= 2;
				}
				break;

			case RenderTargetSizeMode.Offscreen: {
					materials.GetRenderTargetFrameBufferDimensions(out int fbWidth, out int fbHeight);
					while ((width > fbWidth) || (height > fbHeight)) {
						width >>= 1;
						height >>= 1;
					}
				}
				break;

			case RenderTargetSizeMode.Literal: break;
			case RenderTargetSizeMode.LiteralPicmip: break;
			default:
#if !GMOD_DLL
				Assert(RenderTargetSizeMode == RenderTargetSizeMode.NoChange);
				Assert(OriginalRenderTargetType == RenderTargetType.NoDepth);
#endif
				break;
		}
	}

	public bool SetRenderTarget(int renderTargetID, ITexture? depthTexture = null) {
		if ((Flags & (int)TextureFlags.RenderTarget) == 0)
			return false;

		Assert(HasBeenAllocated());
		ShaderAPITextureHandle_t textureHandle = TextureHandles![0];
		ShaderAPITextureHandle_t depthTextureHandle = (ShaderAPITextureHandle_t)ShaderRenderTarget.Depthbuffer;

		if ((Flags & (int)TextureFlags.DepthRenderTarget) != 0) {
			Assert(FrameCount >= 2);
			depthTextureHandle = TextureHandles[1];
		}
		else if ((Flags & (int)TextureFlags.NoDepthBuffer) != 0)
			depthTextureHandle = (ShaderAPITextureHandle_t)ShaderRenderTarget.None;


		if (depthTexture != null)
			depthTextureHandle = ((ITextureInternal)depthTexture).GetTextureHandle(0);

		ShaderAPI.SetRenderTargetEx(renderTargetID, textureHandle, depthTextureHandle);
		return true;
	}

	public int GetTextureHandle(int frame, int textureChannel = 0) {
		if (frame < 0) {
			frame = 0;
			Warning("CTexture::GetTextureHandle(): nFrame is < 0!\n");
		}

		if (frame >= FrameCount) {
			Assert(frame < FrameCount);
			return INVALID_SHADERAPI_TEXTURE_HANDLE;
		}

		Assert(TextureHandles);
		Assert(HasBeenAllocated());
		if (TextureHandles == null || !HasBeenAllocated())
			return INVALID_SHADERAPI_TEXTURE_HANDLE;

		return TextureHandles![frame];
	}

	Vector3 Reflectivity;
	string Name;
	string TextureGroupName;
	uint Flags;
	uint InternalFlags;
	// int refCount 
	ImageFormat ImageFormat;

	TexDimensions DimsMapping;
	TexDimensions DimsActual;
	TexDimensions DimsAllocated;

	ushort FrameCount;
	ushort StreamingMips;
	ushort OriginalRTWidth;
	ushort OriginalRTHeight;
	byte LowResImageWidth;
	byte LowResImageHeight;
	ushort DesiredDimensionLimit;
	ushort ActualDimensionLimit;

	IVTFTexture? StreamingVTF;
	ResidencyType ResidenceTarget;
	ResidencyType ResidenceCurrent;

	ShaderAPITextureHandle_t[]? TextureHandles;
	RenderTargetType OriginalRenderTargetType;
	RenderTargetSizeMode RenderTargetSizeMode;
}
