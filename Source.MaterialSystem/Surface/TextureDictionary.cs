using Source.Bitmap;
using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.MaterialSystem;

using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace Source.MaterialSystem.Surface;

public enum MatSystemTextureFlags
{
	IsProcedural = 0x1,
	IsReference = 0x2
}

public class FontTextureRegen : ITextureRegenerator
{
	int Width;
	int Height;
	ImageFormat Format;
	byte[] TextureBits;
	public FontTextureRegen(int width, int height, ImageFormat format) {
		Width = width;
		Height = height;
		Format = format;

		TextureBits = new byte[ImageLoader.GetMemRequired(Width, Height, 1, Format, false)];
	}

	public void RegenerateTextureBits(ITexture texture, IVTFTexture vtfTexture, in Rectangle subRect) {
		if (TextureBits == null)
			return;

		Assert((vtfTexture.Width() == Width) && (vtfTexture.Height() == Height));

		int nFormatBytes = ImageLoader.SizeInBytes(Format);
		if (nFormatBytes == 4) {
			if (Format == vtfTexture.Format()) {
				int ymax = subRect.Y + subRect.Height;
				for (int y = subRect.Y; y < ymax; ++y) {
					Span<byte> pchData = vtfTexture.ImageData(0, 0, 0, 0, y)[(subRect.X * nFormatBytes)..];
					nint size = ImageLoader.GetMemRequired(subRect.Width, 1, 1, Format, false);
					memcpy(pchData, TextureBits.AsSpan()[((y * Width + subRect.X) * nFormatBytes)..], (int)size);
				}
			}
			else {
				using PixelWriter pixelWriter = new();
				pixelWriter.SetPixelMemory(vtfTexture.Format(), vtfTexture.ImageData(0, 0, 0), vtfTexture.RowSizeInBytes(0));

				int xmax = subRect.X + subRect.Width;
				int ymax = subRect.Y + subRect.Height;
				int x, y;

				for (y = subRect.Y; y < ymax; ++y) {
					pixelWriter.Seek(subRect.X, y);
					Span<byte> rgba = TextureBits.AsSpan()[((y * Width + subRect.X) * nFormatBytes)..];

					for (x = subRect.X; x < xmax; ++x) {
						pixelWriter.WritePixel(rgba[0], rgba[1], rgba[2], rgba[3]);
						rgba = rgba[nFormatBytes..];
					}
				}
			}
		}
		else {
			if (subRect.Width != Width || subRect.Height != Height) {
				Assert(false);
				return;
			}
			nint size = ImageLoader.GetMemRequired(Width, Height, 1, Format, false);
			memcpy(vtfTexture.ImageData(0, 0, 0), TextureBits.AsSpan());
		}
	}

	internal unsafe void UpdateBackingBits(Rectangle subRect, Span<byte> bits, Rectangle uploadRect, ImageFormat format) {
		nint size = ImageLoader.GetMemRequired(Width, Height, 1, Format, false);

		if (TextureBits == null) {
			TextureBits = new byte[size];
			TextureBits.AsSpan().Clear();
		}

		int bytesPerPixel = ImageLoader.SizeInBytes(Format);

		if (bytesPerPixel == 4) {
			bool isSubRectSmaller = subRect.Width != uploadRect.Width || subRect.Height != uploadRect.Height;

			Assert(subRect.X >= 0 && subRect.Y >= 0);
			Assert(subRect.X + subRect.Width <= Width && subRect.Y + subRect.Height <= Height);

			for (int y = 0; y < subRect.Height; ++y) {
				int dstIndex = ((subRect.Y + y) * Width + subRect.X) * bytesPerPixel;

				int srcOffset = isSubRectSmaller
					? ((subRect.Y + y) * uploadRect.Width + subRect.X) * bytesPerPixel
					: y * uploadRect.Width * bytesPerPixel;

				var srcRow = bits.Slice(srcOffset, subRect.Width * bytesPerPixel);
				var dstRow = TextureBits.AsSpan(dstIndex, subRect.Width * bytesPerPixel);

				ImageLoader.ConvertImageFormat(srcRow, format, dstRow, Format, subRect.Width, 1);
			}
		}
		else {
			if (subRect.Width != Width || subRect.Height != Height) {
				AssertMsg(false, "Cannot subrect copy when format is not RGBA.\n");
				return;
			}

			bits.CopyTo(TextureBits);
		}
	}

}

public class MatSystemTexture(IMaterialSystem materials)
{
	public TextureID ID { get; set; }
	public ulong Hash { get; set; }

	public IMaterial? Material;
	public ITexture? Texture;
	public ITexture? OverrideTexture;
	public MatSystemTextureFlags Flags;
	public float Wide, Tall, S0, T0, S1, T1;
	public int InputWide;
	public int InputTall;
	public FontTextureRegen? Regen;

	public void SetMaterial(IMaterial? material) {
		CleanUpMaterial();

		Material = material;

		if (material == null) {
			Wide = Tall = 0;
			S0 = T0 = 0.0f;
			S1 = T1 = 1.0f;
			return;
		}

		material.IncrementReferenceCount();

		Wide = material.GetMappingWidth();
		Tall = material.GetMappingHeight();

		float flPixelCenterX = 0.0f;
		float flPixelCenterY = 0.0f;

		if (Wide > 0.0f && Tall > 0.0f) {
			flPixelCenterX = 0.5f / Wide;
			flPixelCenterY = 0.5f / Tall;
		}

		S0 = flPixelCenterX;
		T0 = flPixelCenterY;

		S1 = 1.0F - flPixelCenterX;
		T1 = 1.0F - flPixelCenterY;

		if (IsProcedural()) {
			if (Material!.TryFindVar("$basetexture", out IMaterialVar? var) && var.IsTexture()) {
				Texture = var.GetTextureValue();
				if (Texture != null) {
					Texture.IncrementReferenceCount();
					CreateRegen(Wide, Tall, Texture.GetImageFormat());
					Texture.SetTextureRegenerator(Regen);
				}
			}
		}
	}
	public void SetMaterial(ReadOnlySpan<char> filename) {
		IMaterial? material = materials.FindMaterial(filename, TEXTURE_GROUP_VGUI);
		Material = material;
		if (material == null) {
			Wide = Tall = 0;
			S0 = T0 = 0;
			S1 = T1 = 0;
			return;
		}

		Wide = material.GetMappingWidth();
		Tall = material.GetMappingHeight();

		float pixelCenterX = 0.0f;
		float pixelCenterY = 0.0f;

		if (Wide > 0.0f && Tall > 0.0f) {
			pixelCenterX = 0.5f / Wide;
			pixelCenterY = 0.5f / Tall;
		}

		S0 = pixelCenterX;
		T0 = pixelCenterY;
		S1 = 1.0F - pixelCenterX;
		T1 = 1.0F - pixelCenterY;

		if (IsProcedural()) {
			if (Material!.TryFindVar("$basetexture", out IMaterialVar? var) && var.IsTexture()) {
				Texture = var.GetTextureValue();
				if (Texture != null) {
					CreateRegen(Wide, Tall, Texture.GetImageFormat());
					Texture.SetTextureRegenerator(Regen);
				}
			}
		}
	}

	[MemberNotNull(nameof(Regen))]
	private void CreateRegen(float width, float height, ImageFormat format) {
		Assert(IsProcedural());
		if (Regen == null)
			Regen = new FontTextureRegen((int)width, (int)height, format);
	}

	private bool IsProcedural() => 0 != (Flags & MatSystemTextureFlags.IsProcedural);

	public void SetProcedural(bool proc) {
		if (proc)
			Flags |= MatSystemTextureFlags.IsProcedural;
		else
			Flags &= ~MatSystemTextureFlags.IsProcedural;
	}

	public ITexture? GetTextureValue() {
		if (Material == null)
			return null;

		return OverrideTexture ?? Texture;
	}

	internal void SetSubTextureRGBAEx(int drawX, int drawY, Span<byte> rgba, int subTextureWide, int subTextureTall, ImageFormat format) {
		ITexture? texture = GetTextureValue();
		if (texture == null)
			return;

		Assert(IsProcedural());
		if (!IsProcedural())
			return;

		Assert(drawX < Wide);
		Assert(drawY < Tall);
		Assert(drawX + subTextureWide <= Wide);
		Assert(drawY + subTextureTall <= Tall);

		Assert(Regen);

		Assert(!rgba.IsEmpty);

		Rectangle subRect = new();
		subRect.X = drawX;
		subRect.Y = drawY;
		subRect.Width = subTextureWide;
		subRect.Height = subTextureTall;

		Rectangle textureSize = new();
		textureSize.X = 0;
		textureSize.Y = 0;
		textureSize.Width = subTextureWide;
		textureSize.Height = subTextureTall;

		Regen!.UpdateBackingBits(subRect, rgba, textureSize, format);
		texture.Download(subRect);

	}

	internal unsafe void UpdateSubTextureRGBA(int drawX, int drawY, nint rgba, int subTextureWide, int subTextureTall, ImageFormat format) {
		ITexture? texture = GetTextureValue();
		if (texture == null)
			return;

		Assert(IsProcedural());
		if (!IsProcedural())
			return;

		Assert(drawX < Wide);
		Assert(drawY < Tall);
		Assert(drawX + subTextureWide <= Wide);
		Assert(drawY + subTextureTall <= Tall);

		Assert(Regen);

		Assert(rgba != 0);

		Span<byte> bits = new((void*)rgba, InputWide * InputTall * ImageLoader.SizeInBytes(format));

		Rectangle subRect = new() {
			X = drawX,
			Y = drawY,
			Width = subTextureWide,
			Height = subTextureTall
		};

		Rectangle textureSize = new() {
			X = 0,
			Y = 0,
			Width = InputWide,
			Height = InputTall
		};

		Regen!.UpdateBackingBits(subRect, bits, textureSize, format);
		texture.Download(subRect);
	}
	static int textureID = 0;
	internal void SetTextureRGBA(Span<byte> rgba, int wide, int tall, ImageFormat format, bool fixupTextCoords) {
		Assert(IsProcedural());
		if (!IsProcedural())
			return;

		if (Material == null) {
			int width = wide;
			int height = tall;

			int i;
			for (i = 0; i < 32; i++) {
				width = 1 << i;
				if (width >= wide) {
					break;
				}
			}

			for (i = 0; i < 32; i++) {
				height = 1 << i;
				if (height >= tall) {
					break;
				}
			}

			Span<char> textureName = stackalloc char[64];
			sprintf(textureName, "__vgui_texture_%d").D(textureID);
			++textureID;

			ITexture pTexture = materials.CreateProceduralTexture(
				textureName,
				TEXTURE_GROUP_VGUI,
				width,
				height,
				format,
				TextureFlags.ClampS | TextureFlags.ClampT |
				TextureFlags.NoMip | TextureFlags.NoLOD |
				TextureFlags.Procedural | TextureFlags.SingleCopy | TextureFlags.PointSample);

			KeyValues vmtTKeyValues = new("UnlitGeneric");
			vmtTKeyValues.SetInt("$vertexcolor", 1);
			vmtTKeyValues.SetInt("$vertexalpha", 1);
			vmtTKeyValues.SetInt("$ignorez", 1);
			vmtTKeyValues.SetInt("$no_fullbright", 1);
			vmtTKeyValues.SetInt("$translucent", 1);
			vmtTKeyValues.SetString("$basetexture", textureName);

			IMaterial pMaterial = materials.CreateMaterial(textureName, vmtTKeyValues);
			pMaterial.Refresh();

			SetMaterial(pMaterial);
			InputTall = tall;
			InputWide = wide;
			if (fixupTextCoords && (wide != width || tall != height)) {
				S1 = (float)wide / width;
				T1 = (float)tall / height;
			}
		}

		Assert(wide <= Wide);
		Assert(tall <= Tall);

		SetSubTextureRGBAEx(0, 0, rgba, wide, tall, format);
	}

	internal void ReferenceOtherProcedural(MatSystemTexture texture, IMaterial material) {
		CleanUpMaterial();

		Assert(texture.IsProcedural());

		Flags |= MatSystemTextureFlags.IsReference;

		Material = material;

		if (material == null) {
			Wide = Tall = 0;
			S0 = T0 = 0.0f;
			S1 = T1 = 1.0f;
			return;
		}

		Wide = texture.Wide;
		Tall = texture.Tall;
		S0 = texture.S0;
		T0 = texture.T0;
		S1 = texture.S1;
		T1 = texture.T1;

		Assert((material.GetMappingWidth() == Wide) && (material.GetMappingHeight() == Tall));

		if (Material!.TryFindVar("$basetexture", out IMaterialVar? var) && var.IsTexture()) {
			Texture = var.GetTextureValue();
			if (Texture != null) {
				Assert(Texture == texture.Texture);
				Regen = texture.Regen;

			}
		}
	}

	internal void CleanUpMaterial() {
		if (Material != null) {
			Material.DecrementReferenceCount();
			Material.DeleteIfUnreferenced();
			Material = null;
		}

		if (Texture != null) {
			Texture.SetTextureRegenerator(null!);
			Texture.DecrementReferenceCount();
			Texture.DeleteIfUnreferenced();
			Texture = null;
		}

		if (Regen != null)
			Regen = null;
	}
}


public class TextureDictionary(IMaterialSystem materials, MatSystemSurface surface)
{
	long idx;
	Dictionary<long, MatSystemTexture> Textures = [];

	public bool IsValidId(in TextureID id, [NotNullWhen(true)] out MatSystemTexture? tex) {
		return Textures.TryGetValue(id.ID, out tex);
	}

	internal void BindTextureToFile(in TextureID id, ReadOnlySpan<char> filename) {
		if (!IsValidId(id, out MatSystemTexture? tex)) {
			Msg($"BindTextureToFile: Invalid texture id for file {filename}\n");
			return;
		}

		ulong curhash = filename.Hash();
		if (tex.Material == null || tex.Hash != curhash) {
			tex.Hash = curhash;
			tex.SetMaterial(filename);
		}
	}

	internal void BindTextureToMaterial(in TextureID id, IMaterial material) {
		if (!IsValidId(id, out MatSystemTexture? tex)) {
			Msg($"BindTextureToMaterial: Invalid texture id {id}\n");
			return;
		}

		tex.SetMaterial(material);
	}

	internal void BindTextureToMaterialReference(in TextureID id, in TextureID refID, IMaterial material) {
		if (!IsValidId(id, out MatSystemTexture? tex)) {
			Msg($"BindTextureToMaterialReference: Invalid texture id {id}\n");
			return;
		}
		if (!IsValidId(refID, out MatSystemTexture? refTex)) {
			Msg($"BindTextureToMaterialReference: Invalid ref. texture id {refID}\n");
			return;
		}

		tex.ReferenceOtherProcedural(refTex, material);
	}

	internal TextureID CreateTexture(bool procedural) {
		long idx = this.idx++;
		MatSystemTexture texture = new(materials);
		texture.SetProcedural(procedural);
		texture.ID = idx;
		Textures[idx] = texture;
		return idx;
	}

	internal void DestroyTexture(in TextureID id) {
		if (Textures.Count <= 1)
			return;

		if (id != TextureID.INVALID && Textures.TryGetValue(id.ID, out MatSystemTexture? tex)) {
			tex.CleanUpMaterial();
			Textures.Remove(id.ID);
		}
	}

	internal int FindTextureIdForTextureId(ReadOnlySpan<char> filename) {
		foreach (var kvp in Textures) {
			int i = (int)kvp.Key;
			MatSystemTexture tex = kvp.Value;

			IMaterial? mat = tex.Material;
			if (mat == null)
				continue;

			if (mat.GetName().Equals(filename, StringComparison.OrdinalIgnoreCase))
				return i;
		}

		return -1;
	}

	internal IMaterial? GetTextureMaterial(in TextureID id) {
		if (!IsValidId(id, out MatSystemTexture? tex))
			return null;
		return tex.Material;
	}

	internal void GetTextureSize(TextureID id, out int wide, out int tall) {
		if (!IsValidId(id, out MatSystemTexture? tex)) {
			wide = tall = 0;
			return;
		}

		wide = (int)tex.Wide;
		tall = (int)tex.Tall;
	}

	internal void GetTextureTexCoords(in TextureID id, out float s0, out float t0, out float s1, out float t1) {
		if (!IsValidId(id, out MatSystemTexture? tex)) {
			s0 = t0 = 0.0f;
			s1 = t1 = 1.0f;
			return;
		}

		s0 = tex.S0;
		t0 = tex.T0;
		s1 = tex.S1;
		t1 = tex.T1;
	}

	internal void SetSubTextureRGBA(in TextureID id, int drawX, int drawY, Span<byte> rgba, int subTextureWide, int subTextureTall, ImageFormat format) {
		if (!IsValidId(id, out MatSystemTexture? tex)) {
			Msg($"SetSubTextureRGBA: Invalid texture id {id}\n");
			return;
		}
		tex.SetSubTextureRGBAEx(drawX, drawY, rgba, subTextureWide, subTextureTall, format);
	}

	internal void UpdateSubTextureRGBA(in TextureID id, int drawX, int drawY, nint rgba, int subTextureWide, int subTextureTall, ImageFormat format) {
		if (!IsValidId(id, out MatSystemTexture? tex)) {
			Msg($"UpdateSubTextureRGBA: Invalid texture id {id}\n");
			return;
		}
		tex.UpdateSubTextureRGBA(drawX, drawY, rgba, subTextureWide, subTextureTall, format);
	}

	internal void SetTextureRGBAEx(in TextureID id, Span<byte> rgba, int wide, int tall, ImageFormat format, bool fixupTextCoords) {
		if (!IsValidId(id, out MatSystemTexture? tex)) {
			Msg($"SetSubTextureRGBA: Invalid texture id {id}\n");
			return;
		}
		tex.SetTextureRGBA(rgba, wide, tall, format, fixupTextCoords);
	}
}
