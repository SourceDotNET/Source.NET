using Source.Common.GUI;

using FreeTypeSharp;
using static FreeTypeSharp.FT;
using Source.Common.Launcher;
using System.Runtime.InteropServices;

namespace Source.MaterialSystem.Surface;

public unsafe class FreeTypeFont : BaseFont
{
	internal static readonly FT_LibraryRec_* Library;
	static FreeTypeFont() {
		FT_Error error;
		fixed (FT_LibraryRec_** outRec = &Library)
			error = FT_Init_FreeType(outRec);

		if (error != 0)
			throw new Exception("FT_Init_FreeType failed");

		int prop = 35;
		error = FT_Property_Set(Library, (byte*)Marshal.StringToCoTaskMemAnsi("truetype"), (byte*)Marshal.StringToCoTaskMemAnsi("interpreter-version"), &prop);

		if (error != 0)
			throw new Exception("FT_Init_FreeType failed");
	}
	internal override int GetHeightRequested() => Tall;	
	internal override ReadOnlySpan<char> GetName() => Name;
	internal override ReadOnlySpan<char> GetFamilyName() => null;

	internal readonly FontManager fontManager;
	internal readonly ISystem system;
	public FreeTypeFont(FontManager fontManager, ISystem system) {
		this.fontManager = fontManager;
		this.system = system;
	}

	string? Name;
	ulong Symbol;
	short Tall;
	ushort Weight;
	SurfaceFontFlags Flags = 0;
	ushort ScanLines;
	ushort Blur;
	bool Underlined;
	uint Height;
	uint MaxCharWidth;
	uint Ascent;
	uint DropShadowOffset;
	uint OutlineSize;
	bool AntiAliased;
	bool Rotary;
	bool Additive;
	bool IsSymbol;
	ushort[] BitmapSize = new ushort[2];

	public override SurfaceFontFlags GetFlags() => Flags;
	public override int GetHeight() => (int)Height;
	public override int GetMaxCharWidth() => (int)MaxCharWidth;

	public override bool IsEqualTo(ReadOnlySpan<char> fontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags) {
		return fontName.Hash() == Symbol && Tall == tall && Weight == weight && ScanLines == scanlines && flags == Flags;
	}

	FT_FaceRec_* face;

	public bool Create(ReadOnlySpan<char> fontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags) {
		Name = new(fontName);
		Symbol = fontName.Hash();
		Tall = (short)tall;
		Weight = (ushort)weight;
		Flags = flags;
		AntiAliased = 0 != (flags & SurfaceFontFlags.Antialias);
		Underlined = 0 != (flags & SurfaceFontFlags.Underline);
		DropShadowOffset = 0 != (flags & SurfaceFontFlags.DropShadow) ? 1u : 0u;
		OutlineSize = 0 != (flags & SurfaceFontFlags.Outline) ? 1u : 0u;
		Rotary = 0 != (flags & SurfaceFontFlags.Rotary);
		Additive = 0 != (flags & SurfaceFontFlags.Additive);
		IsSymbol = 0 != (flags & SurfaceFontFlags.Symbol);
		Blur = (ushort)blur;
		ScanLines = (ushort)scanlines;

		FT_Error error;

		byte* font = fontManager.GetFontBinary(fontName, weight, out nint length);
		if (font == null)
			return false;

		fixed (FT_FaceRec_** facePtr = &face)
			error = FT_New_Memory_Face(Library, font, length, 0, facePtr);
		if (error != FT_Error.FT_Err_Ok) { DevMsg($"Upcoming error info: {fontName}\n"); Assert(false); Warning($"FreeType error during new face initialization: {error}\n"); return false; }

		if (IsSymbol)
			FT_Select_Charmap(face, FT_Encoding_.FT_ENCODING_MS_SYMBOL);

		error = FT_Set_Pixel_Sizes(face, 0, (uint)tall);
		if (error != FT_Error.FT_Err_Ok) { Warning($"FreeType error during pixel size set: {error}\n"); return false; }

		FT_Size_Metrics_ tm = face->size->metrics;

		int actualCellHeight = (int)(tm.height >> 6);

		if (actualCellHeight != tall && actualCellHeight > 0) {
			uint scaledPixelSize = (uint)((tall * (long)tall) / actualCellHeight);
			error = FT_Set_Pixel_Sizes(face, 0, scaledPixelSize);
			if (error != FT_Error.FT_Err_Ok) { Warning($"FreeType error during scaled pixel size set: {error}\n"); return false; }
			tm = face->size->metrics;
		}

		Height = (uint)((tm.height + ((DropShadowOffset + 2 * OutlineSize) << 6)) >> 6);
		MaxCharWidth = (uint)(tm.max_advance >> 6);
		Ascent = (uint)(tm.ascender >> 6);

		BitmapSize[0] = (ushort)((int)(tm.max_advance >> 6) + OutlineSize * 2);
		BitmapSize[1] = (ushort)((int)(tm.height >> 6) + DropShadowOffset + OutlineSize * 2);
		return true;
	}

	public struct GlyphABC
	{
		public int A;
		public int B;
		public int C;
	}
	Dictionary<char, GlyphABC> Glyphs = [];
	Dictionary<char, int> GlyphsY = [];

	private void LoadChar(char ch, bool render = false) {
		uint glyph_index = FT_Get_Char_Index(face, ch);

		if (glyph_index == 0 && IsSymbol)
			glyph_index = FT_Get_Char_Index(face, (uint)((int)ch | 0xF000));

		if (glyph_index != 0) {
			FT_Load_Glyph(face, glyph_index, FT_LOAD.FT_LOAD_DEFAULT);
		}
	}

	public override int GetCharYOffset(char ch) {
		if (!GlyphsY.TryGetValue(ch, out int y)) {
			LoadChar(ch);
			FT_GlyphSlotRec_* g = face->glyph;
			y = (int)(g->metrics.horiBearingY >> 6);
			GlyphsY.Add(ch, y);
		}
		return y;
	}
	public override void GetCharABCwidths(char ch, out int a, out int b, out int c) {
		if (!Glyphs.TryGetValue(ch, out GlyphABC glyphABC)) {
			LoadChar(ch);
			FT_GlyphSlotRec_* g = face->glyph;
			ref FT_Glyph_Metrics_ metrics = ref g->metrics;
			a = (int)((metrics.horiBearingX >> 6) - Blur - OutlineSize);
			b = (int)((metrics.width >> 6) + ((Blur + OutlineSize) * 2) + DropShadowOffset);
			c = (int)(((metrics.horiAdvance - metrics.horiBearingX - metrics.width) >> 6) - Blur - DropShadowOffset - OutlineSize);

			glyphABC = new() {
				A = a,
				B = b,
				C = c
			};
			Glyphs.Add(ch, glyphABC);
			return;
		}

		a = glyphABC.A;
		b = glyphABC.B;
		c = glyphABC.C;
	}

	internal override void GetCharRGBA(char ch, int rgbaWide, int rgbaTall, Span<byte> rgba) {
		LoadChar(ch, render: true);
		FT_GlyphSlotRec_* slot = face->glyph;
		FT_Render_Glyph(slot, AntiAliased ? FT_Render_Mode_.FT_RENDER_MODE_NORMAL : FT_Render_Mode_.FT_RENDER_MODE_MONO);
		DrawBitmap(slot, rgbaWide, rgbaTall, rgba);

		FontEffects.ApplyDropShadowToTexture(rgbaWide, rgbaTall, rgba, DropShadowOffset);
		FontEffects.ApplyOutlineToTexture(rgbaWide, rgbaTall, rgba, OutlineSize);
		FontEffects.ApplyGaussianBlurToTexture(rgbaWide, rgbaTall, rgba, Blur);
		FontEffects.ApplyScanlineEffectToTexture(rgbaWide, rgbaTall, rgba, ScanLines);
		FontEffects.ApplyRotaryEffectToTexture(rgbaWide, rgbaTall, rgba, Rotary);
	}

	private unsafe void DrawBitmap(FT_GlyphSlotRec_* rec, int rgbaWide, int rgbaTall, Span<byte> rgba) {
		FT_Bitmap_* bitmap = &rec->bitmap;
		byte* buffer = bitmap->buffer;
		int bmpWidth = (int)bitmap->width;
		int bmpHeight = (int)bitmap->rows;
		int pitch = Math.Abs(bitmap->pitch);

		for (int y = 0; y < bmpHeight; y++) {
			byte* row = buffer + y * pitch;
			for (int x = 0; x < bmpWidth; x++) {
				int dstX = x + (int)OutlineSize + Blur; 
				int dstY = (int)(Ascent - rec->bitmap_top + y) + (int)OutlineSize;

				if (dstX < 0 || dstY < 0 || dstX >= rgbaWide || dstY >= rgbaTall)
					continue;

				byte coverage = 0;

				switch (bitmap->pixel_mode) {
					case FT_Pixel_Mode_.FT_PIXEL_MODE_GRAY:
						coverage = row[x];
						break;

					case FT_Pixel_Mode_.FT_PIXEL_MODE_MONO:
						int byteIndex = x >> 3;
						int bitIndex = 7 - (x & 7);
						coverage = (row[byteIndex] & (1 << bitIndex)) != 0 ? (byte)255 : (byte)0;
						break;

					default:
						coverage = 0;
						break;
				}

				int dstIndex = (dstY * rgbaWide + dstX) * 4;
				rgba[dstIndex + 0] = 255;
				rgba[dstIndex + 1] = 255;
				rgba[dstIndex + 2] = 255;
				rgba[dstIndex + 3] = coverage;
			}
		}
	}

	public override bool GetUnderlined() {
		return false;
	}

	internal override void GetKernedCharWidth(char ch, char chBefore, char chAfter, out float flWide, out float flabcA, out float flabcC) {
		GetCharABCwidths(ch, out int a, out int b, out int c);
		flWide = (float)(a + b + c);
		flabcA = (float)(a);
		flabcC = 0;
	}
}
