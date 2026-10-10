#if GMOD_DLL
using FreeImageAPI;

using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.Common.MaterialSystem;

using System.Drawing;
using System.Runtime.InteropServices;

namespace Source.Engine.GarrysMod;

public class Image : ITextureRegenerator
{
	public static readonly HashSet<Image> Images = [];
	public static readonly List<Image> DynamicImages = [];

	public bool Loaded;
	public string Name;
	public string Flags;
	public FIBITMAP Bitmap;
	public FREE_IMAGE_FORMAT Format = FREE_IMAGE_FORMAT.FIF_UNKNOWN;
	public int BPP;
	public int Width;
	public int Height;
	public bool NeedsPow2;
	public int ImageWidth;
	public int ImageHeight;
	public ITexture? Texture;
	public bool Dynamic;

	public Image(ReadOnlySpan<char> name, ReadOnlySpan<char> flags, bool needsPow2, bool dynamic) {
		Name = new(name);
		Flags = new(flags);
		NeedsPow2 = needsPow2;
		Dynamic = dynamic;
		Loaded = Load();
		if (Loaded) {
			if (!Dynamic)
				Images.Add(this);
			else
				DynamicImages.Add(this);
		}
	}

	public bool Load() {
		if (!Bitmap.IsNull)
			return true;

		bool spawnicons = Name.StartsWith("spawnicons");
		ReadOnlySpan<char> pathID = spawnicons ? "MOD" : "GAME";

		IFileHandle? file = g_pFileSystem.Open($"materials/{Name}", FileOpenOptions.Read | FileOpenOptions.Binary, pathID);
		if (file == null) {
			if (spawnicons)
				file = g_pFileSystem.Open($"materials/{Name}", FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");

			if (file == null) {
				file = g_pFileSystem.Open(Name, FileOpenOptions.Read | FileOpenOptions.Binary, pathID);
				if (file == null) {
					DevWarning(2, $"FreeImage: Couldn't open file '{Name}'\n");
					return false;
				}
			}
		}

		uint size = (uint)file.Stream.Length;
		if (size == 0) {
			DevWarning($"FreeImage: Empty file '{Name}'\n");
			file.Dispose();
			return false;
		}

		byte[] data = new byte[size];
		file.Stream.ReadExactly(data);
		file.Dispose();

		GCHandle pin = GCHandle.Alloc(data, GCHandleType.Pinned);
		FIMEMORY memory = FreeImage.OpenMemory(pin.AddrOfPinnedObject(), size);
		Format = FreeImage.GetFileTypeFromMemory(memory, 0);
		if (Format == FREE_IMAGE_FORMAT.FIF_UNKNOWN) {
			DevWarning($"FreeImage: Invalid image format for '{Name}'\n");
			FreeImage.CloseMemory(memory);
			pin.Free();
			return false;
		}

		FIBITMAP loaded = FreeImage.LoadFromMemory(Format, memory, FREE_IMAGE_LOAD_FLAGS.DEFAULT);
		FreeImage.CloseMemory(memory);
		pin.Free();
		if (loaded.IsNull) {
			DevWarning($"FreeImage: Unknown error loading file '{Name}'\n");
			return false;
		}

		if (BPP == 0) {
			BPP = 32;
			Bitmap = FreeImage.ConvertTo32Bits(loaded);
		}
		else if (BPP == 24)
			Bitmap = FreeImage.ConvertTo24Bits(loaded);
		else
			Bitmap = FreeImage.ConvertTo32Bits(loaded);
		FreeImage.Unload(loaded);

		ImageWidth = (int)FreeImage.GetWidth(Bitmap);
		ImageHeight = (int)FreeImage.GetHeight(Bitmap);
		Width = (int)NextPowerOfTwo((uint)ImageWidth);
		Height = (int)NextPowerOfTwo((uint)ImageHeight);

		if (!NeedsPow2 && HardwareConfig.SupportsNonPow2Textures()) {
			Width = ImageWidth;
			Height = ImageHeight;
		}

		if (Width != ImageWidth || Height != ImageHeight) {
			FIBITMAP old = Bitmap;
			Bitmap = FreeImage.Rescale(old, Width, Height, FREE_IMAGE_FILTER.FILTER_BILINEAR);
			FreeImage.Unload(old);
		}

		FreeImage.FlipVertical(Bitmap);
		return true;
	}

	static uint NextPowerOfTwo(uint v) {
		v--;
		v |= v >> 1;
		v |= v >> 2;
		v |= v >> 4;
		v |= v >> 8;
		v |= v >> 16;
		return v + 1;
	}

	public void RegenerateTextureBits(ITexture texture, IVTFTexture vtfTexture, in Rectangle rect) {
		if (vtfTexture == null || vtfTexture.ImageData().IsEmpty) {
			Warning("CImage::RegenerateTextureBits: No VTF texture or no image data! Out of memory?\n");
			return;
		}

		if (!Load()) {
			vtfTexture.ImageData()[..vtfTexture.ComputeTotalSize()].Fill(0xFF);
			return;
		}

		ImageFormat format = BPP != 24 ? ImageFormat.BGRA8888 : ImageFormat.BGR888;
		if (format != vtfTexture.Format())
			vtfTexture.ConvertImageFormat(format, false);

		for (int mip = 0; mip < vtfTexture.MipCount(); mip++) {
			vtfTexture.ComputeMipLevelDimensions(mip, out int mipWidth, out int mipHeight, out _);
			int rowSize = vtfTexture.RowSizeInBytes(mip);
			Span<byte> dest = vtfTexture.ImageData(0, 0, mip);

			if (dest.IsEmpty) {
				Warning($"FreeImage: GetCurrentPixel() == null for mip {mip} of {Name}!\n");
				vtfTexture.ImageData()[..vtfTexture.ComputeTotalSize()].Fill(0xFF);
				continue;
			}

			FIBITMAP bitmap = Bitmap;
			if (Width != mipWidth || Height != mipHeight)
				bitmap = FreeImage.Rescale(bitmap, mipWidth, mipHeight, FREE_IMAGE_FILTER.FILTER_BILINEAR);

			if (bitmap.IsNull)
				DevWarning($"FreeImage: Couldn't rescale image data for '{Name}'\n");
			else {
				IntPtr bits = FreeImage.GetBits(bitmap);
				if (bits != IntPtr.Zero) {
					int pitch = (int)FreeImage.GetPitch(bitmap);
					int copySize = pitch;
					if (pitch <= rowSize)
						copySize = rowSize;

					int offset = 0;
					for (int y = 0; y < mipHeight; y++) {
						unsafe {
							new ReadOnlySpan<byte>((byte*)bits, copySize).CopyTo(dest[offset..]);
							bits += copySize;
						}
						offset += (ushort)rowSize;
					}
				}

				if (bitmap != Bitmap)
					FreeImage.Unload(bitmap);
			}
		}

		if (!Bitmap.IsNull) {
			FreeImage.Unload(Bitmap);
			Bitmap = FIBITMAP.Zero;
		}
	}

	public void Release() {
		if (!Dynamic)
			Images.Remove(this);
		else
			DynamicImages.Remove(this);
	}
}

public class Resources : IResources
{
	static ulong DynamicImageCount;

	public int Init(IServiceProvider services) => throw new NotImplementedException();
	public void Shutdown() => throw new NotImplementedException();
	public IVideoHolly? CreateMovie() => throw new NotImplementedException();
	public Color GetTextureColour(ITexture texture, int x, int y) {
		foreach (Image image in Image.Images) {
			if (image.Texture != texture)
				continue;

			if (!image.Load())
				return new Color(255, 0, 255, 255);

			FreeImage.GetPixelColor(image.Bitmap, (uint)x, (uint)y, out RGBQUAD pixel);
			if (!image.Bitmap.IsNull) {
				FreeImage.Unload(image.Bitmap);
				image.Bitmap = FIBITMAP.Zero;
			}
			return new Color(pixel.rgbRed, pixel.rgbGreen, pixel.rgbBlue, pixel.rgbReserved);
		}

		return new Color(0, 0, 0, 255);
	}
	public void SavePNG(int width, int height, Span<byte> pixels, ReadOnlySpan<char> path, int outWidth, int outHeight) {
		FIBITMAP bitmap = FreeImage.Allocate(width, height, 32, 0, 0, 0);
		if (bitmap.IsNull) {
			Warning("[CResources::SavePNG] Freeimage couldn't allocate!\n");
			return;
		}

		IntPtr bits = FreeImage.GetBits(bitmap);
		if (bits == IntPtr.Zero) {
			Warning("[CResources::SavePNG] Freeimage couldn't GetBits!\n");
			return;
		}

		unsafe {
			pixels[..(width * height * 4)].CopyTo(new Span<byte>((byte*)bits, width * height * 4));
		}

		FIBITMAP output = bitmap;
		if (outHeight != height || outWidth != width) {
			output = FreeImage.Rescale(bitmap, outWidth, outHeight, FREE_IMAGE_FILTER.FILTER_BILINEAR);
			FreeImage.Unload(bitmap);
		}

		FreeImage.FlipVertical(output);

		FIMEMORY memory = FreeImage.OpenMemory(IntPtr.Zero, 0);
		FreeImage.SaveToMemory(FREE_IMAGE_FORMAT.FIF_PNG, output, memory, FREE_IMAGE_SAVE_FLAGS.PNG_Z_BEST_SPEED);

		IntPtr data = IntPtr.Zero;
		uint size = 0;
		FreeImage.AcquireMemory(memory, ref data, ref size);

		IFileHandle? file = size == 0 || data == IntPtr.Zero ? null : g_pFileSystem.Open(path, FileOpenOptions.Write | FileOpenOptions.Binary);
		if (file == null)
			Warning($"Couldn't save PNG to '{path}' ({size} bytes)\n");
		else {
			unsafe {
				file.Stream.Write(new ReadOnlySpan<byte>((byte*)data, (int)size));
			}
			file.Dispose();
		}

		FreeImage.CloseMemory(memory);
		FreeImage.Unload(output);
	}
	public void SaveJPG(int unk1, int unk2, int unk3, Span<byte> unk4, ReadOnlySpan<char> unk5, int unk6, int unk7, Stream unk8) => throw new NotImplementedException();
	public bool ShouldRecordSound() => throw new NotImplementedException();
	public void AudioSamples(Span<byte> unk1, uint unk2, byte unk3, byte unk4) => throw new NotImplementedException();
	public void SavePNGToBuffer(int unk1, int unk2, Span<byte> unk3, Stream unk4, int unk5, int unk6) => throw new NotImplementedException();
	public void SaveJPGToBuffer(int unk1, int unk2, Span<byte> unk3, Stream unk4, int unk5, int unk6, int unk7) => throw new NotImplementedException();
	public void SetImage(ITexture unk1, ReadOnlySpan<char> unk2) => throw new NotImplementedException();

	public IMaterial? FindMaterial(ReadOnlySpan<char> name, ReadOnlySpan<char> flags, bool create, bool anyExtension, bool dynamic) {
		Span<char> extension = stackalloc char[6];
		extension[0] = '\0';
		if (name.Length > 4) {
			ReadOnlySpan<char> ext = Path.GetExtension(name);
			if (!ext.IsEmpty)
				ext = ext[1..];
			strcpy(extension, ext);
		}

		ReadOnlySpan<char> e = ((ReadOnlySpan<char>)extension).SliceNullTerminatedString();
		if (!anyExtension
			&& !e.Equals("png", StringComparison.OrdinalIgnoreCase)
			&& !e.Equals("jpg", StringComparison.OrdinalIgnoreCase)
			&& !e.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
			&& !e.Equals("cache", StringComparison.OrdinalIgnoreCase)
			&& !e.Equals("tga", StringComparison.OrdinalIgnoreCase))
			return null;

		return FindMaterial(new string(name).ToLowerInvariant(), new string(flags), create, dynamic);
	}

	static int GetFlag(int index, string flags) {
		if (index >= flags.Length || flags[index] == '0')
			return 0;
		return flags[index] switch {
			'1' => 1,
			'2' => 2,
			'3' => 3,
			'4' => 4,
			'5' => 5,
			_ => 0
		};
	}

	static IMaterial? FindMaterial(string name, string flags, bool create, bool dynamic) {
		string key = flags + name;
		IMaterial? existing = null;

		if (!dynamic) {
			existing = materials.FindMaterial($"!{key}", "FreeImage", false);
			if (!existing.IsErrorMaterial() && materials.IsTextureLoaded(key)) {
				ITexture? texture = existing.FindVar("$basetexture", out _, true).GetTextureValue();
				if (texture != null && !texture.IsError())
					return existing;
			}
		}

		if (!create)
			return null;

		int shader = GetFlag(0, flags);
		int nocull = GetFlag(1, flags);
		int alphatest = GetFlag(2, flags);
		int mips = GetFlag(3, flags);
		int noclamp = GetFlag(4, flags);
		int smooth = GetFlag(5, flags);
		int ignorez = GetFlag(6, flags);

		Image image = new(name, flags, mips != 0 || noclamp != 0, dynamic);
		if (!image.Loaded) {
			image.Release();
			return null;
		}

		if (dynamic)
			key = $"_Dynamic_CImage_{DynamicImageCount++}";

		uint textureFlags = mips != 0 ? 0x40800u : 0x40b00u;
		if (noclamp == 0)
			textureFlags |= 0xc;
		if (smooth == 0)
			textureFlags |= 0x201;

		ImageFormat format = image.BPP != 24 ? ImageFormat.BGRA8888 : ImageFormat.BGR888;
		ITexture texture2 = materials.CreateProceduralTexture(key, "FreeImage", image.Width, image.Height, format, (TextureFlags)textureFlags);
		texture2.SetTextureRegenerator(image);
		texture2.Download();
		image.Texture = texture2;

		KeyValues keyValues = new(shader == 1 ? "VertexLitGeneric" : "UnlitGeneric");
		keyValues.SetString("$basetexture", key);
		keyValues.SetString("$vertexcolor", "1");
		keyValues.SetInt("$realwidth", image.ImageWidth);
		keyValues.SetInt("$realheight", image.ImageHeight);
		if (nocull == 1)
			keyValues.SetString("$nocull", "1");
		if (alphatest == 1)
			keyValues.SetString("$alphatest", "1");
		else if (alphatest == 0 && shader != 1)
			keyValues.SetString("$vertexalpha", "1");
		if (ignorez == 1)
			keyValues.SetString("$ignorez", "1");

		IMaterial material = existing == null || existing.IsErrorMaterial()
			? materials.CreateMaterial(key, "FreeImage", keyValues)
			: existing;
		material.IncrementReferenceCount();

		material.FindVar("$basetexture", out _, true).SetTextureValue(texture2);
		// todo: IMaterial vfunc 0x1c8
		material.Refresh();

		material.DecrementReferenceCount();
		texture2.DecrementReferenceCount();
		return material;
	}
}
#endif
