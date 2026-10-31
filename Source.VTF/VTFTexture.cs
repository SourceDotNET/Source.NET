using Source.Bitmap;
using Source.Common;
using Source.Common.Bitmap;

using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Source.VTF;

public sealed class VTFTexture : IVTFTexture
{
	readonly int[] Version = new int[2];
	int Width;
	int Height;
	int Depth;
	ImageFormat Format;

	int MipCount;
	int FaceCount;
	int FrameCount;

	int Flags;
	byte[]? ImageData;

	Vector3 Reflectivity;
	float BumpScale;

	int StartFrame;

	ImageFormat LowResImageFormat;
	int LowResImageWidth;
	int LowResImageHeight;
	byte[]? LowResImageData;

	float AlphaThreshold;
	float AlphaHiFreqThreshold;

	int FinestMipmapLevel;
	int CoarsestMipmapLevel;

	List<ResourceEntryInfo> ResourcesInfo = [];
	float IVTFTexture.BumpScale() => BumpScale;

	public void ComputeAlphaFlags() {
		throw new NotImplementedException();
	}

	public int ComputeFaceSize(int startingMipLevel = 0) {
		return ComputeFaceSize(startingMipLevel, Format);
	}

	public int ComputeFaceSize(int startingMipLevel, ImageFormat format) {
		int size = 0;
		int w = Width;
		int h = Height;
		int d = Depth;

		for (int i = 0; i < MipCount; ++i) {
			if (i >= startingMipLevel) {
				size += ImageLoader.GetMemRequired(w, h, d, format, false);
			}
			w >>= 1;
			h >>= 1;
			d >>= 1;
			if (w < 1) {
				w = 1;
			}
			if (h < 1) {
				h = 1;
			}
			if (d < 1) {
				d = 1;
			}
		}
		return size;
	}

	public void ComputeMipLevelDimensions(int level, out int width, out int height, out int depth) {
		width = Width >> level;
		height = Height >> level;
		depth = Depth >> level;

		if (width < 1)
			width = 1;
		if (height < 1)
			height = 1;
		if (depth < 1)
			depth = 1;
	}

	public void ComputeMipLevelSubRect(in Rectangle srcRect, int mipLevel, out Rectangle subRect) {
		Assert(srcRect.X >= 0 && srcRect.Y >= 0 && (srcRect.X + srcRect.Width <= Width) && (srcRect.Y + srcRect.Height <= Height));

		if (mipLevel == 0) {
			subRect = srcRect;
			return;
		}

		subRect = new();

		float flInvShrink = 1.0f / (float)(1 << mipLevel);
		subRect.X = (int)(srcRect.X * flInvShrink);
		subRect.Y = (int)(srcRect.Y * flInvShrink);
		subRect.Width = (int)MathF.Ceiling((srcRect.X + srcRect.Width) * flInvShrink) - subRect.X;
		subRect.Height = (int)MathF.Ceiling((srcRect.Y + srcRect.Height) * flInvShrink) - subRect.Y;
	}

	public int ComputeMipSize(int mipLevel) {
		return ComputeMipSize(mipLevel, Format);
	}

	public int ComputeMipSize(int mipLevel, ImageFormat format) {
		ComputeMipLevelDimensions(mipLevel, out int w, out int h, out int d);
		return ImageLoader.GetMemRequired(w, h, d, format, false);
	}

	public void ComputeReflectivity() {
		throw new NotImplementedException();
	}

	public int ComputeTotalSize() {
		return ComputeTotalSize(Format);
	}

	public int ComputeTotalSize(ImageFormat format) {
		nint memRequired = ComputeFaceSize(0, format);
		return (int)(FaceCount * FrameCount * memRequired);
	}

	public void ConstructLowResImage() {
		throw new NotImplementedException();
	}

	public void ConvertImageFormat(ImageFormat fmt, bool normalToDUDV) {
		if (ImageData == null)
			return;

		nint convertedSize = ComputeTotalSize(fmt);
		byte[] convertedImage = ArrayPool<byte>.Shared.Rent((int)convertedSize);

		for (int mip = 0; mip < MipCount; mip++) {
			ComputeMipLevelDimensions(mip, out int width, out int height, out int depth);
			nint srcFaceStride = ImageLoader.GetMemRequired(width, height, 1, Format, false);
			nint dstFaceStride = ImageLoader.GetMemRequired(width, height, 1, fmt, false);
			for (int frame = 0; frame < FrameCount; frame++) {
				for (int face = 0; face < FaceCount; face++) {
					Span<byte> srcData = GetImageData(frame, face, mip);
					Span<byte> dstData = convertedImage.AsSpan()[(int)GetImageOffset(frame, face, mip, fmt)..];
					for (int z = 0; z < depth; ++z, srcData = srcData[(int)srcFaceStride..], dstData = dstData[(int)dstFaceStride..]) {
						if (normalToDUDV) {
							Error("No\n");
							return;
						}
						else {
							ImageLoader.ConvertImageFormat(srcData, Format, dstData, fmt, width, height);
						}
					}
				}
			}
		}

		memcpy<byte>(ImageData, convertedImage[..(int)convertedSize]);
		Format = fmt;

		if (!ImageLoader.IsCompressed(fmt)) {
			ref ImageFormatInfo info = ref ImageLoader.ImageFormatInfo(fmt);
			int alphaBits = info.AlphaBits;
			if (alphaBits > 1) {
				Flags |= (int)(TextureFlags.EightBitAlpha);
				Flags &= ~(int)(TextureFlags.OneBitAlpha);
			}
			if (alphaBits <= 1) {
				Flags &= ~(int)(TextureFlags.EightBitAlpha);
				if (alphaBits == 0) {
					Flags &= ~(int)(TextureFlags.OneBitAlpha);
				}
			}
		}
		else {
			if ((fmt == ImageFormat.DXT5) || (fmt == ImageFormat.ATI2N) || (fmt == ImageFormat.ATI1N)) {
				Flags &= ~(int)(TextureFlags.OneBitAlpha | TextureFlags.EightBitAlpha);
			}
		}

		ArrayPool<byte>.Shared.Return(convertedImage, true);
	}

	int IVTFTexture.Depth() => Depth;

	public void Dispose() {

	}

	int IVTFTexture.FaceCount() => FaceCount;

	public int FaceSizeInBytes(int mipLevel) {
		nint nWidth = Width >> mipLevel;
		if (nWidth < 1) {
			nWidth = 1;
		}
		nint nHeight = Height >> mipLevel;
		if (nHeight < 1) {
			nHeight = 1;
		}
		return (int)(ImageLoader.SizeInBytes(Format) * nWidth * nHeight);
	}

	public int FileSize(int mipSkipCount = 0) {
		throw new NotImplementedException();
	}

	int IVTFTexture.Flags() => Flags;

	ImageFormat IVTFTexture.Format() => Format;

	int IVTFTexture.FrameCount() => FrameCount;

	public void GenerateMipmaps() {
		throw new NotImplementedException();
	}

	public unsafe Span<byte> GetResourceData(ResourceEntryType type) {
		ref ResourceEntryInfo info = ref FindResourceEntryInfo(type);

		return null;
	}

	public bool HasResourceEntry(ResourceEntryType type) {
		throw new NotImplementedException();
	}

	int IVTFTexture.Height() => Height;

	Span<byte> IVTFTexture.ImageData() {
		return GetImageData(0, 0, 0);
	}

	Span<byte> IVTFTexture.ImageData(int frame, int face, int mipLevel) {
		return GetImageData(frame, face, mipLevel);
	}

	public nint GetImageOffset(int frame, int face, int mipLevel, ImageFormat format) {
		Assert(frame < FrameCount);
		Assert(face < FaceCount);
		Assert(mipLevel < MipCount);

		int i;
		nint iOffset = 0;

		int iFaceSize = ComputeFaceSize(0, format);
		iOffset = frame * FaceCount * iFaceSize;

		// Get to the right face
		iOffset += face * iFaceSize;

		// Get to the right mip level
		for (i = 0; i < mipLevel; ++i) {
			iOffset += ComputeMipSize(i, format);
		}

		return iOffset;
	}

	Span<byte> IVTFTexture.ImageData(int frame, int face, int mipLevel, int x, int y, int z = 0) {
		ComputeMipLevelDimensions(mipLevel, out int width, out int height, out int depth);
		Assert((x >= 0) && (x <= width) && (y >= 0) && (y <= height) && (z >= 0) && (z <= depth));

		nint faceBytes = FaceSizeInBytes(mipLevel);
		nint rowBytes = RowSizeInBytes(mipLevel);
		nint texelBytes = ImageLoader.SizeInBytes(Format);

		Span<byte> mipBits = GetImageData(frame, face, mipLevel);
		mipBits = mipBits[(int)(z * faceBytes + y * rowBytes + x * texelBytes)..];
		return mipBits;
	}


	private ref ResourceEntryInfo FindOrCreateResourceEntryInfo(ResourceEntryType type) {
		for (int i = 0, c = ResourcesInfo.Count; i < c; i++) {
			ref ResourceEntryInfo searchResource = ref CollectionsMarshal.AsSpan(ResourcesInfo)[i];
			if (searchResource.Tag == type)
				return ref searchResource;
		}

		ResourcesInfo.Add(new() {
			Tag = type
		});

		ref ResourceEntryInfo newResource = ref CollectionsMarshal.AsSpan(ResourcesInfo)[ResourcesInfo.Count - 1];
		return ref newResource;
	}

	private ref ResourceEntryInfo FindResourceEntryInfo(ResourceEntryType type) {
		for (int i = 0, c = ResourcesInfo.Count; i < c; i++) {
			ref ResourceEntryInfo searchResource = ref CollectionsMarshal.AsSpan(ResourcesInfo)[i];
			if ((searchResource.Tag & ~ResourceEntryType.Flag_Mask) == type)
				return ref searchResource;
		}

		return ref Unsafe.NullRef<ResourceEntryInfo>();
	}

	public void ImageFileInfo(int frame, int face, int mipLevel, out int startLocation, out int sizeInBytes) {
		int i, mipWidth, mipHeight, mipDepth;

		ref ResourceEntryInfo imageDataInfo = ref FindResourceEntryInfo(ResourceEntryType.HighResImageData);

		if (Unsafe.IsNullRef(ref imageDataInfo)) {
			Dbg.Assert(false);
			startLocation = 0;
			sizeInBytes = 0;
			return;
		}

		int offset = (int)imageDataInfo.Offset;
		for (i = MipCount - 1; i > mipLevel; --i) {
			ComputeMipLevelDimensions(i, out mipWidth, out mipHeight, out mipDepth);
			int mipLevelSize = ImageLoader.GetMemRequired(mipWidth, mipHeight, mipDepth, Format, false);
			offset += mipLevelSize;
		}

		ComputeMipLevelDimensions(mipLevel, out mipWidth, out mipHeight, out mipDepth);
		int faceSize = ImageLoader.GetMemRequired(mipWidth, mipHeight, mipDepth, Format, false);

		int facesToRead = FaceCount;
		if (IsCubeMap()) {
			if (Version[0] == 7 && Version[1] < 1) {
				facesToRead = 6;
				if (face == (int)CubeMapFaceIndex.Spheremap)
					face--;
			}
			else if (Version[0] == 7 && Version[1] >= 5) {
				facesToRead = 6;
				if (face == (int)CubeMapFaceIndex.Spheremap)
					face--;
			}
		}

		int framesize = facesToRead * faceSize;
		offset += framesize * frame;

		offset += face * faceSize;

		startLocation = offset;
		sizeInBytes = faceSize;
	}


	static bool IsMultipleOf4(int value) => value <= 2 || (value & 0x3) == 0;
	public bool Init(int width, int height, int depth, ImageFormat format, int flags, int frameCount, int forceMipCount = -1) {
		if (depth == 0) {
			depth = 1;
		}

		if ((flags & (int)TextureFlags.EnvMap) != 0) {
			if (width != height) {
				Warning("Height and width must be equal for cubemaps!\n");
				return false;
			}
			if (depth != 1) {
				Warning("Depth must be 1 for cubemaps!\n");
				return false;
			}
		}

		if ((flags & (int)TextureFlags.Procedural) == 0 && (!IsMultipleOf4(width) || !IsMultipleOf4(height) || !IsMultipleOf4(depth))) {
			Warning("Image dimensions must be multiple of 4!\n");
			return false;
		}

		if (format == 0) {
			format = ImageFormat.RGBA8888;
		}

		Width = width;
		Height = height;
		Depth = depth;
		Format = format;
		Flags = flags;

		// THIS CAUSED A BUG!!!  We want all of the mip levels in the vtf file even with nomip in case we have lod.
		// NOTE: But we don't want more than 1 mip level for procedural textures
		if ((flags & (uint)(TextureFlags.NoMip | TextureFlags.Procedural)) == (uint)(TextureFlags.NoMip | TextureFlags.Procedural)) {
			forceMipCount = 1;
		}

		if (forceMipCount == -1)
			MipCount = ComputeMipCount();
		else
			MipCount = forceMipCount;

		FrameCount = frameCount;

		FaceCount = (flags & (uint)TextureFlags.EnvMap) != 0 ? 6 : 1;

		// Need to do this because Shutdown deallocates the low-res image
		LowResImageWidth = LowResImageHeight = 0;

		// Allocate me some bits!
		nint iMemorySize = ComputeTotalSize();
		if (!AllocateImageData(iMemorySize))
			return false;

		// As soon as we have image indicate so in the resources
		if (iMemorySize != 0)
			FindOrCreateResourceEntryInfo(ResourceEntryType.HighResImageData);
		else
			RemoveResourceEntryInfo(ResourceEntryType.HighResImageData);

		return true;
	}

	private void RemoveResourceEntryInfo(ResourceEntryType tag) {
		ResourcesInfo.RemoveAll(x => x.Tag == tag);
	}

	public void InitLowResImage(int width, int height, ImageFormat format) {
		throw new NotImplementedException();
	}

	public bool IsCubeMap() => ((TextureFlags)Flags & TextureFlags.EnvMap) == TextureFlags.EnvMap;
	public bool IsNormalMap() => ((TextureFlags)Flags & TextureFlags.Normal) == TextureFlags.Normal;

	public bool IsVolumeTexture() => Depth > 1;

	public void LowResFileInfo(out int startLocation, out int sizeInBytes) {
		throw new NotImplementedException();
	}

	public ImageFormat LowResFormat() => LowResImageFormat;

	Span<byte> IVTFTexture.LowResImageData() {
		throw new NotImplementedException();
	}

	public int LowResWidth() => LowResImageWidth;

	public int LowResHeight() => LowResImageHeight;

	int IVTFTexture.MipCount() => MipCount;

	Vector3 IVTFTexture.Reflectivity() => Reflectivity;

	public int RowSizeInBytes(int mipLevel) {
		nint nWidth = (Width >> mipLevel);
		if (nWidth < 1) {
			nWidth = 1;
		}
		return ImageLoader.SizeInBytes(Format) * (int)nWidth;
	}

	public bool Serialize(Stream stream) {
		throw new NotImplementedException();
	}

	public void SetBumpScale(float scale) {
		throw new NotImplementedException();
	}

	public void SetReflectivity(in Vector3 vecReflectivity) {
		throw new NotImplementedException();
	}

	public void SetResourceData(ResourceEntryType type, Span<byte> data) {
		throw new NotImplementedException();
	}

	public bool Unserialize(Stream stream, bool headerOnly = false, int skipMipLevels = 0) {
		return UnserializeEx(stream, headerOnly, 0, skipMipLevels);
	}

	public bool UnserializeEx(Stream stream, bool headerOnly, int forceFlags, int skipMipLevels) {
		VTFFileHeader header;
		if (!ReadHeader(stream, out header))
			return false;

		header.Flags |= (uint)forceFlags;
		var flags = (TextureFlags)header.Flags;


		if ((flags & TextureFlags.EnvMap) == TextureFlags.EnvMap && header.Width != header.Height) {
			Dbg.Warning("*** Encountered VTF non-square cubemap!\n");
			return false;
		}
		if ((flags & TextureFlags.EnvMap) == TextureFlags.EnvMap && header.Depth != 1) {
			Dbg.Warning("*** Encountered VTF volume texture cubemap!\n");
			return false;
		}
		if (header.Width <= 0 || header.Height <= 0 || header.Depth <= 0) {
			Dbg.Warning("*** Encountered VTF invalid texture size!\n");
			return false;
		}
		if (header.ImageFormat < ImageFormat.Unknown || header.ImageFormat >= ImageFormat.Count) {
			Dbg.Warning("*** Encountered VTF invalid image format!\n");
			return false;
		}

		Width = header.Width;
		Height = header.Height;
		Depth = header.Depth;
		Format = header.ImageFormat;
		Flags = (int)header.Flags;
		FrameCount = header.NumFrames;

		FaceCount = (Flags & (int)TextureFlags.EnvMap) == (int)TextureFlags.EnvMap ? (int)CubeMapFaceIndex.Count : 1;
		MipCount = ComputeMipCount();

		FinestMipmapLevel = 0;
		CoarsestMipmapLevel = MipCount - 1;

		Reflectivity = header.Reflectivity;
		BumpScale = header.BumpScale;

		StartFrame = header.StartFrame;

		Version[0] = header.Version[0];
		Version[1] = header.Version[1];

		if (header.LowResImageWidth == 0 || header.LowResImageHeight == 0) {
			LowResImageWidth = LowResImageHeight = 0;
		}
		else {
			LowResImageWidth = header.LowResImageWidth;
			LowResImageHeight = header.LowResImageHeight;
		}
		LowResImageFormat = header.LowResImageFormat;

		if (LowResImageFormat < ImageFormat.Unknown || LowResImageFormat >= ImageFormat.Count)
			return false;

		if (header.NumResources > 0) {
			ResourcesInfo = new((int)header.NumResources);
			long curStreamPos = stream.Position;
			stream.Seek(header.ResourcesOffset, SeekOrigin.Begin);
			using (BinaryReader reader = new(stream, Encoding.ASCII, true)) {
				for (int i = 0; i < header.NumResources; i++) {
					ResourcesInfo.Add(new());
					ref ResourceEntryInfo resource = ref CollectionsMarshal.AsSpan(ResourcesInfo)[i];

					byte b1 = reader.ReadByte(), b2 = reader.ReadByte(), b3 = reader.ReadByte();
					resource.Tag = ResourceEntryInfo.ParseTag(b1, b2, b3);
					resource.Flags = reader.ReadByte();
					resource.Offset = reader.ReadUInt32();
				}
			}
			stream.Seek(curStreamPos, SeekOrigin.Begin);
		}
		else {
			// VTF 7.0 -> 7.2 does not have resource entries
			// have to write our own based on the old offsets
			int lowResImageSize = ImageLoader.GetMemRequired(LowResImageWidth, LowResImageHeight, 1, LowResImageFormat, false);
			if (lowResImageSize > 0) {
				ref ResourceEntryInfo reiLq = ref FindOrCreateResourceEntryInfo(ResourceEntryType.LowResThumbnail);
				reiLq.Offset = (uint)stream.Position;
			}

			ref ResourceEntryInfo reiHq = ref FindOrCreateResourceEntryInfo(ResourceEntryType.HighResImageData);
			reiHq.Offset = (uint)(lowResImageSize + stream.Position);
		}
		// Caller wants the header component only
		if (headerOnly)
			return true;

		ref ResourceEntryInfo lowResDataInfo = ref FindResourceEntryInfo(ResourceEntryType.LowResThumbnail);
		if (!Unsafe.IsNullRef(ref lowResDataInfo)) {
			stream.Seek(lowResDataInfo.Offset, SeekOrigin.Begin);
			if (!LoadLowResData(stream))
				return false;
		}

		// TODO: LoadNewResources

		ref ResourceEntryInfo imageDataInfo = ref FindResourceEntryInfo(ResourceEntryType.HighResImageData);
		if (!Unsafe.IsNullRef(ref imageDataInfo)) {
			stream.Seek(imageDataInfo.Offset, SeekOrigin.Begin);
			if (!LoadImageData(stream, header, skipMipLevels))
				return false;
		}
		else
			return false;

		return true;
	}

	private bool LoadImageData(Stream stream, VTFFileHeader header, int skipMipLevels) {
		if (skipMipLevels > 0) {
			if (header.NumMipLevels < skipMipLevels) {
				Warning("Warning! Encountered old format VTF file; please rebuild it!\n");
				return false;
			}

			ComputeMipLevelDimensions(skipMipLevels, out Width, out Height, out Depth);
			MipCount -= skipMipLevels;
		}

		nint imageSize = ComputeFaceSize();
		imageSize *= FaceCount * FrameCount;

		int facesToRead = FaceCount;
		if (IsCubeMap()) {
			if (Version[0] == 7 && Version[1] < 1)
				facesToRead = 6;
			else if (Version[0] == 7 && Version[1] >= 5)
				facesToRead = 6;
		}

		if (!AllocateImageData(imageSize))
			return false;

		bool mipDataPresent = true;
		int firstAvailableMip = 0;
		int lastAvailableMip = MipCount - 1;

		Span<byte> data = GetResourceData(ResourceEntryType.TextureLOD);
		if (!data.IsEmpty) {
			ref TextureStreamSettings_t streamSettings = ref MemoryMarshal.Cast<byte, TextureStreamSettings_t>(data)[0];
			firstAvailableMip = Math.Max(0, streamSettings.FirstAvailableMip - skipMipLevels);
			lastAvailableMip = Math.Max(0, streamSettings.LastAvailableMip - skipMipLevels);
			mipDataPresent = false;
		}

		// Not doing streamable textures right now

		Assert(firstAvailableMip >= 0 && firstAvailableMip <= lastAvailableMip && lastAvailableMip < MipCount);

		FinestMipmapLevel = firstAvailableMip;
		CoarsestMipmapLevel = lastAvailableMip;

		for (int mip = MipCount; --mip >= 0;) {
			if (header.NumMipLevels - skipMipLevels <= mip)
				continue;

			int mipSize = ComputeMipSize(mip);

			if (mip > lastAvailableMip || mip < firstAvailableMip) {
				if (mipDataPresent)
					for (int frame = 0; frame < FrameCount; frame++)
						for (int face = 0; face < facesToRead; face++)
							stream.Seek(mipSize, SeekOrigin.Current);
				continue;
			}

			for (int frame = 0; frame < FrameCount; frame++) {
				for (int face = 0; face < facesToRead; face++) {
					Span<byte> mipBits = GetImageData(frame, face, mip);
					int bytesRead = stream.Read(mipBits);
					Assert(mipBits.Length == bytesRead);
				}
			}
		}

		return stream.Position <= stream.Length;
	}

	private Span<byte> GetImageData(int frame, int face, int mip) {
		Assert(ImageData != null);
		nint offset = GetImageOffset(frame, face, mip, Format);
		nint size = ComputeMipSize(mip, Format);
		return ImageData.AsSpan()[(int)offset..(int)(offset + size)];
	}

	private bool AllocateImageData(nint imageSize) {
		return GenericAllocateReusableData(ref ImageData, imageSize);
	}

	private static bool GenericAllocateReusableData(ref byte[]? imageData, nint numRequested) {
		if (imageData == null || imageData.Length < numRequested)
			imageData = new byte[numRequested];
		return true;
	}

	private bool LoadLowResData(Stream stream) {
		return true; // not reading that right now
	}

	private int ComputeMipCount() {
		return ImageLoader.GetNumMipMapLevels(Width, Height, Depth);
	}

	private static readonly sbyte[] VTF0 = [86, 84, 70, 0]; // VTF\0
	private unsafe bool ReadHeader(Stream stream, out VTFFileHeader header) {
		using BinaryReader reader = new(stream, Encoding.ASCII, true);
		header = new();

		reader.ReadInto<sbyte>(header.FileTypeString);
		reader.ReadInto<int>(header.Version);
		reader.ReadInto(ref header.HeaderSize);

		if (!header.FileTypeString.SequenceEqual(VTF0)) {
			Dbg.Warning("*** Tried to load a non-VTF file as a VTF file!\n");
			return false;
		}

		if (header.Version[0] != IVTFTexture.VTF_MAJOR_VERSION) {
			Dbg.Warning("*** Encountered VTF file with an invalid version!\n");
			return false;
		}

		if (!ReadHeaderFromBufferPastBaseHeader(reader, header)) {
			Dbg.Warning("*** Encountered VTF file with an invalid full header!\n");
			return false;
		}

		switch (header.Version[1]) {
			case 0:
			case 1:
				header.Depth = 1;
				goto case 2;
			case 2:
				header.NumResources = 0;
				goto case 3;
			case 3:
				header.Flags &= (uint)VersionedVtfFlags.Mask_7_3;
				goto case IVTFTexture.VTF_MINOR_VERSION;
			case IVTFTexture.VTF_MINOR_VERSION:
			case 5:

				break;
		}
		stream.Seek(header.HeaderSize, SeekOrigin.Begin);
		return true;
	}

	private static bool ReadV0(BinaryReader reader, VTFFileBaseHeader header) {
		// Nothing here to do
		return reader.PeekChar() != -1;
	}
	private static bool ReadV1(BinaryReader reader, VTFFileHeaderV7_1 header) {
		reader.ReadInto(ref header.Width);
		reader.ReadInto(ref header.Height);
		reader.ReadInto(ref header.Flags);
		reader.ReadInto(ref header.NumFrames);
		reader.ReadInto(ref header.StartFrame);
		reader.ReadNothing(4); // << what are these?
		reader.ReadInto(ref header.Reflectivity);
		reader.ReadNothing(4); // << what are these?
		reader.ReadInto(ref header.BumpScale);
		reader.ReadInto(ref header.ImageFormat);
		reader.ReadInto(ref header.NumMipLevels);
		reader.ReadInto(ref header.LowResImageFormat);
		reader.ReadInto(ref header.LowResImageWidth);
		reader.ReadInto(ref header.LowResImageHeight);
		return reader.PeekChar() != -1;
	}
	private static bool ReadV2(BinaryReader reader, VTFFileHeaderV7_2 header) {
		reader.ReadInto(ref header.Depth);
		return reader.PeekChar() != -1;
	}
	private static bool ReadV3(BinaryReader reader, VTFFileHeaderV7_3 header) {
		reader.ReadInto<sbyte>(header.Pad4);
		reader.ReadInto(ref header.NumResources);
		reader.ReadNothing(8);
		header.ResourcesOffset = reader.BaseStream.Position;
		return reader.PeekChar() != -1;
	}
	private static bool ReadV4(BinaryReader reader, VTFFileHeader header) {
		return reader.PeekChar() != -1;
	}
	private static bool ReadV5(BinaryReader reader, VTFFileHeader header) {
		return reader.PeekChar() != -1;
	}


	private static bool ReadHeaderFromBufferPastBaseHeader(BinaryReader reader, VTFFileHeader header) {
		switch (header.Version[1]) {
			case 0:
			case 1:
				if (!ReadV0(reader, header)) return false;
				if (!ReadV1(reader, header)) return false;
				return true;
			case 2:
				if (!ReadV0(reader, header)) return false;
				if (!ReadV1(reader, header)) return false;
				if (!ReadV2(reader, header)) return false;
				return true;
			case 3:
				if (!ReadV0(reader, header)) return false;
				if (!ReadV1(reader, header)) return false;
				if (!ReadV2(reader, header)) return false;
				if (!ReadV3(reader, header)) return false;
				return true;
			case 4:
				if (!ReadV0(reader, header)) return false;
				if (!ReadV1(reader, header)) return false;
				if (!ReadV2(reader, header)) return false;
				if (!ReadV3(reader, header)) return false;
				if (!ReadV4(reader, header)) return false;
				return true;
			case 5:
				if (!ReadV0(reader, header)) return false;
				if (!ReadV1(reader, header)) return false;
				if (!ReadV2(reader, header)) return false;
				if (!ReadV3(reader, header)) return false;
				if (!ReadV4(reader, header)) return false;
				if (!ReadV5(reader, header)) return false;
				return true;
			default:
				Dbg.Warning("*** Encountered VTF file with an invalid minor version!\n");
				return false;
		}
	}

	int IVTFTexture.Width() => Width;

	public uint GetResourceTypes(Span<ResourceEntryType> arrRsrcTypes) {
		if (!arrRsrcTypes.IsEmpty)
			for (int i = 0; i < Math.Min(arrRsrcTypes.Length, ResourcesInfo.Count); i++)
				arrRsrcTypes[i] = ResourcesInfo[i].Tag;

		return (uint)ResourcesInfo.Count;
	}
}
