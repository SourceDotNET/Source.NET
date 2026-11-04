using Source.Common;
using Source.Common.Bitmap;
using Source.Common.Filesystem;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;

using System.Runtime.CompilerServices;

namespace Source.MaterialSystem.Surface;

public enum FontPageSize
{
	x16,
	x32,
	x64,
	x128,
	x256,

	Count
}

public struct CacheEntry
{
	public IFont? Font;
	public char Char;
	public nint Page;
	public CharTexCoord TexCoord;

	public readonly ulong FontCharHash() {
		return ((ulong)(uint)RuntimeHelpers.GetHashCode(Font) << 16) | Char;
	}
}

public struct NewChar
{
	public char Char;
	public int FontWide;
	public int FontTall;
	public int Offset;
}

public struct NewPageEntry
{
	public TextureID Page;
	public int DrawX;
	public int DrawY;
}

public struct CharTexCoord
{
	public float X0;
	public float Y0;
	public float X1;
	public float Y1;
}

public class Page
{
	public readonly TextureID[] TextureID = new TextureID[(int)FontDrawType.Count];
	public short MaxFontHeight;
	public short TallestCharOnLine;
	public short Wide;
	public short Tall;
	public short NextX;
	public short NextY;
}

public class FontTextureCache
{
	readonly IMaterialSystem materials;
	readonly IFileSystem fileSystem;
	readonly ISystem system;
	readonly FontManager FontManager;
	readonly MatSystemSurface surface;
	public FontTextureCache(IMaterialSystem materials, IFileSystem fileSystem, ISystem system, FontManager FontManager, MatSystemSurface surface) {
		this.materials = materials;
		this.fileSystem = fileSystem;
		this.system = system;
		this.FontManager = FontManager;
		this.surface = surface;
		Clear();
	}
	static int[] realSizes = [16, 32, 64, 128, 256];
	public bool GetTextureForChar(IFont font, FontDrawType drawType, char ch, Span<TextureID> textureID, Span<CharTexCoord> texCoords) {
		Span<char> inChars = [ch];
		return GetTextureForChars(font, drawType, inChars, textureID, texCoords);
	}

	public bool GetTextureForChars(IFont font, FontDrawType drawType, ReadOnlySpan<char> chars, Span<TextureID> textureID, Span<CharTexCoord> texCoords) {
		Assert(chars.Length >= 1);

		if (drawType == FontDrawType.Default)
			drawType = surface.IsFontAdditive(font) > 0 ? FontDrawType.Additive : FontDrawType.NonAdditive;

		int numChars = chars.Length;
		int typePage = (int)drawType - 1;
		typePage = Math.Clamp(typePage, 0, (int)FontDrawType.Count - 1);

		if (FontManager.IsBitmapFont(font)) {
			throw new NotImplementedException();
		}
		else {
			// Determine how many characters need to have their texture generated
			int numNewChars = 0;
			int maxNewCharTexels = 0;
			int totalNewCharTexels = 0;
			Span<NewChar> newChars = stackalloc NewChar[numChars];
			Span<NewPageEntry> newEntries = stackalloc NewPageEntry[numChars];

			BaseFont? baseFont = null;
			bool baseFontResolved = false;

			for (int i = 0; i < numChars; i++) {
				CacheEntry cacheItem = new();
				cacheItem.Font = font;
				cacheItem.Char = chars[i];
				if (!CharCache.TryGetValue(cacheItem.FontCharHash(), out CacheEntry entry)) {
					if (!baseFontResolved) {
						baseFont = FontManager.GetFontForChar(font, chars[0]);
						baseFontResolved = true;
						if (baseFont == null)
							return false;
					}

					// All characters must come out of the same font
					if (baseFont != FontManager.GetFontForChar(font, chars[i])) {
						return false;
					}

					// get the char details
					baseFont!.GetCharABCwidths(chars[i], out int a, out int b, out int c);
					int fontWide = Math.Max(b, 1);
					int fontTall = Math.Max(baseFont.GetHeight(), 1);
					if (baseFont.GetUnderlined())
						fontWide += (a + c);

					// Get a texture to render into
					if (!AllocatePageForChar(fontWide, fontTall, out nint page, out int drawX, out int drawY, out int twide, out int ttall)) {
						return false;
					}

					newEntries[numNewChars].Page = page;
					newEntries[numNewChars].DrawX = drawX;
					newEntries[numNewChars].DrawY = drawY;
					newChars[numNewChars].Char = chars[i];
					newChars[numNewChars].FontWide = fontWide;
					newChars[numNewChars].FontTall = fontTall;
					newChars[numNewChars].Offset = 4 * totalNewCharTexels;
					totalNewCharTexels += fontWide * fontTall;
					maxNewCharTexels = Math.Max(maxNewCharTexels, fontWide * fontTall);
					numNewChars++;

					cacheItem.Page = page;

					// the 0.5 texel offset is done in CMatSystemTexture::SetMaterial() / CMatSystemSurface::StartDrawing()
					double adjust = 0.0f;

					cacheItem.TexCoord.X0 = (float)((double)drawX / ((double)twide + adjust));
					cacheItem.TexCoord.Y0 = (float)((double)drawY / ((double)ttall + adjust));
					cacheItem.TexCoord.X1 = (float)((double)(drawX + fontWide) / (double)twide);
					cacheItem.TexCoord.Y1 = (float)((double)(drawY + fontTall) / (double)ttall);

					CharCache[cacheItem.FontCharHash()] = cacheItem;
					// Copy cacheItem to entry so its valid for the first frame
					entry = cacheItem;
				}

				nint charPage = entry.Page;
				textureID[i] = PageList[(int)charPage].TextureID[typePage];
				texCoords[i] = entry.TexCoord;
			}

			// Generate texture data for all newly-encountered characters
			if (numNewChars > 0) {
				{
					// create a buffer for new characters to be rendered into
					int nByteCount = maxNewCharTexels * 4;
					Span<byte> pRGBA = stackalloc byte[nByteCount];

					// Generate characters individually
					for (int i = 0; i < numNewChars; i++) {
						ref NewChar newChar = ref newChars[i];
						ref NewPageEntry newEntry = ref newEntries[i];

						// render the character into the buffer
						memset<byte>(pRGBA, 0);

						baseFont.GetCharRGBA(newChar.Char, newChar.FontWide, newChar.FontTall, pRGBA);

						// upload the new sub texture 
						// NOTE: both textureIDs reference the same ITexture, so we're ok)
						surface.DrawSetTexture(PageList[(int)newEntry.Page].TextureID[typePage]);
						surface.DrawSetSubTextureRGBA(PageList[(int)newEntry.Page].TextureID[typePage], newEntry.DrawX, newEntry.DrawY, pRGBA, newChar.FontWide, newChar.FontTall);
					}
				}
			}
		}

		return true;
	}

	public void Clear() {
		CharCache.Clear();
		PageList.Clear();

		for (int i = 0; i < (int)FontPageSize.Count; ++i)
			CurrPage[i] = -1;

		FontPages.Clear();
	}

	const int TEXTURE_PAGE_WIDTH = 256;
	const int TEXTURE_PAGE_HEIGHT = 256;

	static int fontPageId = 0;
	bool AllocatePageForChar(int charWide, int charTall, out nint pageIndex, out int drawX, out int drawY, out int twide, out int ttall) {
		int nPageType = ComputePageType(charTall);
		if (nPageType < 0) {
			AssertMsg(false, "Font is too tall for texture cache of glyphs\n");
			pageIndex = 0;
			drawX = 0; drawY = 0; twide = 0; ttall = 0;
			return false;
		}

		pageIndex = CurrPage[nPageType];

		int nNextX = 0;
		bool bNeedsNewPage = true;
		if (pageIndex > -1) {
			Page curPage = PageList[(int)pageIndex];

			nNextX = curPage.NextX + charWide;

			// make sure we have room on the current line of the texture page
			if (nNextX > curPage.Wide) {
				// move down a line
				curPage.NextX = 0;
				nNextX = charWide;
				curPage.NextY += curPage.TallestCharOnLine;
				curPage.TallestCharOnLine = (short)charTall;
			}
			curPage.TallestCharOnLine = (short)Math.Max(curPage.TallestCharOnLine, (short)charTall);

			bNeedsNewPage = ((curPage.NextY + curPage.TallestCharOnLine) > curPage.Tall);
		}

		if (bNeedsNewPage) {
			// allocate a new page
			pageIndex = PageList.Count;
			Page newPage = new();
			PageList.Add(newPage);
			CurrPage[nPageType] = pageIndex;

			for (int i = 0; i < (int)FontDrawType.Count; ++i) {
				newPage.TextureID[i] = surface.CreateNewTextureID(true);
			}

			newPage.MaxFontHeight = (short)realSizes[nPageType];
			newPage.Wide = TEXTURE_PAGE_WIDTH;
			newPage.Tall = TEXTURE_PAGE_HEIGHT;
			newPage.NextX = 0;
			newPage.NextY = 0;
			newPage.TallestCharOnLine = (short)charTall;

			nNextX = charWide;

			Span<char> textureName = stackalloc char[64];
			Span<char> finalName = sprintf(textureName, "__font_page_%d").D(fontPageId);
			++fontPageId;

			ITexture pTexture = materials.CreateProceduralTexture(
				finalName,
				TEXTURE_GROUP_VGUI,
				newPage.Wide,
				newPage.Tall,
				ImageFormat.RGBA8888,
#if GMOD_DLL
				TextureFlags.ClampS | TextureFlags.ClampT |
#else
				TextureFlags.PointSample | TextureFlags.ClampS | TextureFlags.ClampT |
#endif
				TextureFlags.NoMip | TextureFlags.NoLOD | TextureFlags.Procedural | TextureFlags.SingleCopy
			);

			CreateFontMaterials(ref newPage, pTexture);

			// clear the texture from the inital checkerboard to black
			// allocate for 32bpp format
			int nByteCount = TEXTURE_PAGE_WIDTH * TEXTURE_PAGE_HEIGHT * 4;
			Span<byte> rgba = stackalloc byte[nByteCount];
#if GMOD_DLL
			for (int i = 0; i < nByteCount; i += 4) {
				rgba[i + 0] = 255;
				rgba[i + 1] = 255;
				rgba[i + 2] = 255;
				rgba[i + 3] = 0;
			}
#endif
			int typePageNonAdditive = (int)FontDrawType.NonAdditive - 1;
			surface.DrawSetTextureRGBA(newPage.TextureID[typePageNonAdditive], rgba, newPage.Wide, newPage.Tall, 0, false);
		}

		// output the position
		Page page = PageList[(int)pageIndex];
		drawX = page.NextX;
		drawY = page.NextY;
		twide = page.Wide;
		ttall = page.Tall;

		// Update the next position to draw in
		page.NextX = (short)(nNextX + 1);
		return true;
	}
	void CreateFontMaterials(ref Page page, ITexture fontTexture, bool bitmapFont = false) {
		// The normal material
		KeyValues vmtKeyValues = new KeyValues("UnlitGeneric");
		vmtKeyValues.SetInt("$vertexcolor", 1);
		vmtKeyValues.SetInt("$vertexalpha", 1);
		vmtKeyValues.SetInt("$ignorez", 1);
		vmtKeyValues.SetInt("$no_fullbright", 1);
		vmtKeyValues.SetInt("$translucent", 1);
		vmtKeyValues.SetString("$basetexture", fontTexture.GetName());
		IMaterial material = materials.CreateMaterial("__fontpage", vmtKeyValues);
		material.Refresh();

		int typePageNonAdditive = (int)FontDrawType.NonAdditive - 1;
		surface.DrawSetTextureMaterial(page.TextureID[typePageNonAdditive], material);

		// The additive material
		vmtKeyValues = new KeyValues("UnlitGeneric");
		vmtKeyValues.SetInt("$vertexcolor", 1);
		vmtKeyValues.SetInt("$vertexalpha", 1);
		vmtKeyValues.SetInt("$ignorez", 1);
		vmtKeyValues.SetInt("$no_fullbright", 1);
		vmtKeyValues.SetInt("$translucent", 1);
		vmtKeyValues.SetInt("$additive", 1);
		vmtKeyValues.SetString("$basetexture", fontTexture.GetName());
		material = materials.CreateMaterial("__fontpage_additive", vmtKeyValues);
		material.Refresh();

		int typePageAdditive = (int)FontDrawType.Additive - 1;
		if (bitmapFont)
			surface.DrawSetTextureMaterial(page.TextureID[typePageAdditive], material);
		else
			surface.ReferenceProceduralMaterial(page.TextureID[typePageAdditive], page.TextureID[typePageNonAdditive], material);
	}

	int ComputePageType(int charTall) {
		for (int i = 0; i < (int)FontPageSize.Count; ++i) {
			if (charTall < realSizes[i])
				return i;
		}

		return -1;
	}
	Dictionary<ulong, CacheEntry> CharCache = [];
	List<Page> PageList = [];
	nint[] CurrPage = new nint[(int)Surface.FontPageSize.Count];
	Dictionary<IFont, Page> FontPages = [];
}
