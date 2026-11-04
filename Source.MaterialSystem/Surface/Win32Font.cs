#if WIN32
using Source.Common.GUI;

using System.Runtime.InteropServices;

namespace Source.MaterialSystem.Surface;

public unsafe class Win32Font : BaseFont, IDisposable
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
	struct LOGFONTA
	{
		public int lfHeight;
		public int lfWidth;
		public int lfEscapement;
		public int lfOrientation;
		public int lfWeight;
		public byte lfItalic;
		public byte lfUnderline;
		public byte lfStrikeOut;
		public byte lfCharSet;
		public byte lfOutPrecision;
		public byte lfClipPrecision;
		public byte lfQuality;
		public byte lfPitchAndFamily;
		public fixed byte lfFaceName[32];
	}

	[StructLayout(LayoutKind.Sequential)]
	struct TEXTMETRICA
	{
		public int tmHeight;
		public int tmAscent;
		public int tmDescent;
		public int tmInternalLeading;
		public int tmExternalLeading;
		public int tmAveCharWidth;
		public int tmMaxCharWidth;
		public int tmWeight;
		public int tmOverhang;
		public int tmDigitizedAspectX;
		public int tmDigitizedAspectY;
		public byte tmFirstChar;
		public byte tmLastChar;
		public byte tmDefaultChar;
		public byte tmBreakChar;
		public byte tmItalic;
		public byte tmUnderlined;
		public byte tmStruckOut;
		public byte tmPitchAndFamily;
		public byte tmCharSet;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct BITMAPINFOHEADER
	{
		public uint biSize;
		public int biWidth;
		public int biHeight;
		public ushort biPlanes;
		public ushort biBitCount;
		public uint biCompression;
		public uint biSizeImage;
		public int biXPelsPerMeter;
		public int biYPelsPerMeter;
		public uint biClrUsed;
		public uint biClrImportant;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct FIXED
	{
		public ushort fract;
		public short value;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct MAT2
	{
		public FIXED eM11;
		public FIXED eM12;
		public FIXED eM21;
		public FIXED eM22;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct GLYPHMETRICS
	{
		public uint gmBlackBoxX;
		public uint gmBlackBoxY;
		public int gmptGlyphOriginX;
		public int gmptGlyphOriginY;
		public short gmCellIncX;
		public short gmCellIncY;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct ABC
	{
		public int abcA;
		public uint abcB;
		public int abcC;
	}

	[StructLayout(LayoutKind.Sequential)]
	struct SIZE
	{
		public int cx;
		public int cy;
	}

	const byte ANSI_CHARSET = 0;
	const byte DEFAULT_CHARSET = 1;
	const byte SYMBOL_CHARSET = 2;
	const byte SHIFTJIS_CHARSET = 128;
	const uint OUT_TT_ONLY_PRECIS = 7;
	const uint CLIP_DEFAULT_PRECIS = 0;
	const uint NONANTIALIASED_QUALITY = 3;
	const uint ANTIALIASED_QUALITY = 4;
	const uint DEFAULT_PITCH = 0;
	const uint FF_DONTCARE = 0;
	const int MM_TEXT = 1;
	const uint TA_UPDATECP = 1;
	const uint BI_RGB = 0;
	const uint DIB_RGB_COLORS = 0;
	const uint GGO_METRICS = 0;
	const uint GGO_BITMAP = 1;
	const uint GGO_GRAY8_BITMAP = 6;
	const uint CP_ACP = 0;
	internal const uint FR_PRIVATE = 16;

	[DllImport("gdi32")] static extern nint CreateCompatibleDC(nint hdc);
	[DllImport("gdi32")] static extern int DeleteDC(nint hdc);
	[DllImport("gdi32")] static extern int DeleteObject(nint ho);
	[DllImport("gdi32")] static extern nint SelectObject(nint hdc, nint h);
	[DllImport("gdi32")] static extern int EnumFontFamiliesExA(nint hdc, LOGFONTA* logfont, delegate* unmanaged[Stdcall]<LOGFONTA*, TEXTMETRICA*, uint, nint, int> proc, nint lParam, uint flags);
	[DllImport("gdi32", CharSet = CharSet.Ansi, SetLastError = true)] static extern nint CreateFontA(int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);
	[DllImport("gdi32")] static extern int SetMapMode(nint hdc, int mode);
	[DllImport("gdi32")] static extern uint SetTextAlign(nint hdc, uint align);
	[DllImport("gdi32")] static extern int GetTextMetricsA(nint hdc, TEXTMETRICA* tm);
	[DllImport("gdi32")] static extern nint CreateDIBSection(nint hdc, BITMAPINFOHEADER* pbmi, uint usage, byte** ppvBits, nint hSection, uint offset);
	[DllImport("gdi32")] static extern uint GetGlyphOutlineW(nint hdc, uint uChar, uint fuFormat, GLYPHMETRICS* lpgm, uint cjBuffer, void* pvBuffer, MAT2* lpmat2);
	[DllImport("gdi32")] static extern uint GetGlyphOutlineA(nint hdc, uint uChar, uint fuFormat, GLYPHMETRICS* lpgm, uint cjBuffer, void* pvBuffer, MAT2* lpmat2);
	[DllImport("gdi32")] static extern int GetCharABCWidthsW(nint hdc, uint wFirst, uint wLast, ABC* lpABC);
	[DllImport("gdi32")] static extern int GetCharABCWidthsA(nint hdc, uint wFirst, uint wLast, ABC* lpABC);
	[DllImport("gdi32")] static extern int GetTextExtentPoint32A(nint hdc, byte* lpString, int c, SIZE* lpsz);
	[DllImport("gdi32", CharSet = CharSet.Ansi)] internal static extern int AddFontResourceExA(string name, uint fl, nint res);
	[DllImport("gdi32", CharSet = CharSet.Ansi)] internal static extern int AddFontResourceA(string name);
	[DllImport("kernel32")] static extern int WideCharToMultiByte(uint codePage, uint flags, char* lpWideCharStr, int cchWideChar, byte* lpMultiByteStr, int cbMultiByte, byte* lpDefaultChar, int* lpUsedDefaultChar);

	struct ABCCache
	{
		public short B;
		public sbyte A;
		public sbyte C;
	}

	nint Font;
	nint DC;
	nint DIB;
	byte* Buf;

	string? Name;

	short Tall;
	ushort Weight;
	ushort Flags;
	ushort ScanLines;
	ushort Blur;
	ushort[] BitmapSize = new ushort[2];
	bool Underlined;

	byte Height;
	byte MaxCharWidth;
	byte Ascent;
	uint DropShadowOffset;
	uint OutlineSize;
	bool AntiAliased;
	bool Rotary;
	bool Additive;

	readonly Dictionary<char, ABCCache> ExtendedABCWidthsCache = [];

	static bool FontFound;

	[UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
	static int FontEnumProc(LOGFONTA* logfont, TEXTMETRICA* tm, uint fontType, nint lParam) {
		FontFound = true;
		return 0;
	}

	public bool Create(ReadOnlySpan<char> windowsFontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags) {
		Name = new(windowsFontName);
		Tall = (short)tall;
		Weight = (ushort)weight;
		Flags = (ushort)flags;
		AntiAliased = (flags & SurfaceFontFlags.Antialias) != 0;
		Underlined = (flags & SurfaceFontFlags.Underline) != 0;
		DropShadowOffset = (flags & SurfaceFontFlags.DropShadow) != 0 ? 1u : 0u;
		OutlineSize = (flags & SurfaceFontFlags.Outline) != 0 ? 1u : 0u;
		Blur = (ushort)blur;
		ScanLines = (ushort)scanlines;
		Rotary = (flags & SurfaceFontFlags.Rotary) != 0;
		Additive = (flags & SurfaceFontFlags.Additive) != 0;

		uint charset = (flags & SurfaceFontFlags.Symbol) != 0 ? SYMBOL_CHARSET : ANSI_CHARSET;

		if (stricmp(windowsFontName, "win98japanese") == 0) {
			charset = SHIFTJIS_CHARSET;
			Name = "Tahoma";
		}

		DC = CreateCompatibleDC(0);

		LOGFONTA logfont = default;
		logfont.lfCharSet = DEFAULT_CHARSET;
		logfont.lfPitchAndFamily = 0;
		int nameLength = Math.Min(Name.Length, 31);
		for (int i = 0; i < nameLength; i++)
			logfont.lfFaceName[i] = (byte)Name[i];
		logfont.lfFaceName[nameLength] = 0;
		FontFound = false;
		EnumFontFamiliesExA(DC, &logfont, &FontEnumProc, 0, 0);
		if (FontFound) {
			Font = CreateFontA(tall, 0, 0, 0, Weight, (uint)flags & (uint)SurfaceFontFlags.Italic, (uint)flags & (uint)SurfaceFontFlags.Underline, (uint)flags & (uint)SurfaceFontFlags.Strikeout, charset, OUT_TT_ONLY_PRECIS, CLIP_DEFAULT_PRECIS, AntiAliased ? ANTIALIASED_QUALITY : NONANTIALIASED_QUALITY, DEFAULT_PITCH | FF_DONTCARE, new(windowsFontName));
			if (Font == 0)
				Warning($"Couldn't create windows font '{windowsFontName}' ({Marshal.GetLastPInvokeError()})\n");
			else {
#if false
				DevMsg($"Creating Win32Font '{windowsFontName}' [{string.Join(';', new[] { $"tall {tall}", $"weight {weight}", $"blur {blur}", $"scanlines {scanlines}", $"flags {flags}" }.Where(x => !x.EndsWith(" 0") && !x.EndsWith(" None")))}]\n");
#endif
				SetMapMode(DC, MM_TEXT);
				SelectObject(DC, Font);
				SetTextAlign(DC, TA_UPDATECP);

				TEXTMETRICA tm = default;
				if (GetTextMetricsA(DC, &tm) != 0) {
					Height = (byte)(tm.tmHeight + DropShadowOffset + 2 * OutlineSize);
					MaxCharWidth = (byte)tm.tmMaxCharWidth;
					Ascent = (byte)tm.tmAscent;

					BitmapSize[0] = (ushort)(tm.tmMaxCharWidth + OutlineSize * 2);
					BitmapSize[1] = (ushort)(tm.tmHeight + DropShadowOffset + OutlineSize * 2);

					BITMAPINFOHEADER header = default;
					header.biSize = (uint)sizeof(BITMAPINFOHEADER);
					header.biWidth = BitmapSize[0];
					header.biHeight = -BitmapSize[1];
					header.biPlanes = 1;
					header.biBitCount = 32;
					header.biCompression = BI_RGB;

					byte* buf;
					DIB = CreateDIBSection(DC, &header, DIB_RGB_COLORS, &buf, 0, 0);
					Buf = buf;
					SelectObject(DC, DIB);
					return true;
				}
			}
		}

		Name = null;
		return false;
	}

	internal override void GetCharRGBA(char ch, int rgbaWide, int rgbaTall, Span<byte> rgba) {
		GetCharABCwidths(ch, out _, out _, out _);

		SelectObject(DC, Font);

		uint fontHeight = Height;
		MAT2 mat2 = default;
		mat2.eM11.value = 1;
		mat2.eM22.value = 1;
		uint format = AntiAliased ? GGO_GRAY8_BITMAP : GGO_BITMAP;

		GLYPHMETRICS glyphMetrics;
		uint bytesNeeded = GetGlyphOutlineW(DC, ch, format, &glyphMetrics, 0, null, &mat2);
		if (bytesNeeded == 0)
			return;

		byte* lpbuf = stackalloc byte[(int)bytesNeeded];
		if (GetGlyphOutlineW(DC, ch, format, &glyphMetrics, bytesNeeded, lpbuf, &mat2) == 0)
			return;

		uint pitch = glyphMetrics.gmBlackBoxX;
		if (!AntiAliased)
			pitch = ((glyphMetrics.gmBlackBoxX + 31) >> 3) & 536870908;
		while ((pitch & 3) != 0)
			pitch++;

		uint xOffset = OutlineSize + Blur;
		for (uint i = 0; i < glyphMetrics.gmBlackBoxX; i++) {
			uint x = i + xOffset;
			for (uint j = 0; j < glyphMetrics.gmBlackBoxY; j++) {
				uint y = (uint)(j + (Ascent - glyphMetrics.gmptGlyphOriginY));
				if (x < (uint)rgbaWide && y < (uint)rgbaTall) {
					byte value;
					if (!AntiAliased)
						value = (lpbuf[(i >> 3) + j * pitch] & (128 >> (int)(i & 7))) != 0 ? (byte)255 : (byte)0;
					else {
						double gray = lpbuf[j * pitch + i] * 4.0;
						if (gray >= 0.0) {
							if (gray >= 255.0)
								gray = 255.0;
							value = (byte)(int)gray;
						}
						else
							value = 0;
					}

					int dst = (int)((x + y * (uint)rgbaWide) * 4);
					rgba[dst + 3] = ch != '\t' ? value : (byte)0;
					rgba[dst + 0] = 255;
					rgba[dst + 1] = 255;
					rgba[dst + 2] = 255;
				}
			}
		}

		if (Underlined) {
			GLYPHMETRICS metrics;
			MAT2 identity = default;
			identity.eM11.value = 1;
			identity.eM22.value = 1;
			GetGlyphOutlineA(DC, 'a', GGO_METRICS, &metrics, 0, null, &identity);

			uint start = (uint)rgbaTall - fontHeight / 10;
			if (start <= metrics.gmBlackBoxY - 1)
				start = metrics.gmBlackBoxY - 1;

			uint end = fontHeight / 15 + start;
			if (end >= (uint)rgbaTall)
				end = (uint)rgbaTall;
			if (start + 1 >= end)
				end = start + 1;

			for (uint x = 0; x < (uint)rgbaWide; x++) {
				for (uint y = start; y < end; y++) {
					if (y < (uint)rgbaTall) {
						int dst = (int)((y * (uint)rgbaWide + x) * 4);
						rgba[dst + 0] = 255;
						rgba[dst + 1] = 255;
						rgba[dst + 2] = 255;
						rgba[dst + 3] = 255;
					}
				}
			}
		}

		FontEffects.ApplyDropShadowToTexture(rgbaWide, rgbaTall, rgba, DropShadowOffset);
		FontEffects.ApplyOutlineToTexture(rgbaWide, rgbaTall, rgba, OutlineSize);
		FontEffects.ApplyGaussianBlurToTexture(rgbaWide, rgbaTall, rgba, Blur);
		FontEffects.ApplyScanlineEffectToTexture(rgbaWide, rgbaTall, rgba, ScanLines);
		FontEffects.ApplyRotaryEffectToTexture(rgbaWide, rgbaTall, rgba, Rotary);
	}

	public override bool IsEqualTo(ReadOnlySpan<char> windowsFontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags) {
		return stricmp(windowsFontName, Name) == 0
			&& Tall == tall
			&& Weight == weight
			&& Blur == blur
			&& ScanLines == scanlines
			&& Flags == (int)flags;
	}

	public bool IsValid() => Name != null && Name.Length > 0;

	public void SetAsActiveFont(nint hdc) => SelectObject(hdc, Font);

	public override void GetCharABCwidths(char ch, out int a, out int b, out int c) {
		if (ExtendedABCWidthsCache.TryGetValue(ch, out ABCCache cached)) {
			a = cached.A;
			b = cached.B;
			c = cached.C;
			return;
		}

		ABC abc;
		if (GetCharABCWidthsW(DC, ch, ch, &abc) != 0 || GetCharABCWidthsA(DC, ch, ch, &abc) != 0) {
			a = abc.abcA;
			b = (int)abc.abcB + 2;
			c = abc.abcC - 2;
		}
		else {
			byte* mbcs = stackalloc byte[6];
			new Span<byte>(mbcs, 6).Clear();
			char wch = ch;
			WideCharToMultiByte(CP_ACP, 0, &wch, 1, mbcs, 6, null, null);
			int length = 0;
			while (mbcs[length] != 0)
				length++;
			SIZE size;
			if (GetTextExtentPoint32A(DC, mbcs, length, &size) != 0) {
				a = 0;
				b = size.cx + 2;
				c = -2;
			}
			else {
				a = c = 0;
				b = MaxCharWidth;
			}
		}

		ExtendedABCWidthsCache[ch] = new() {
			A = (sbyte)(a - OutlineSize - Blur),
			B = (short)((OutlineSize + Blur) * 2 + DropShadowOffset + b),
			C = (sbyte)(c - DropShadowOffset - OutlineSize - Blur)
		};
	}

	public override int GetCharYOffset(char ch) => 0;

	public override int GetHeight() => Height;
	internal override int GetHeightRequested() => Tall;
	public int GetAscent() => Ascent;
	public override int GetMaxCharWidth() => MaxCharWidth;
	public override SurfaceFontFlags GetFlags() => (SurfaceFontFlags)Flags;
	public override bool GetUnderlined() => Underlined;
	internal override ReadOnlySpan<char> GetName() => Name;
	internal override ReadOnlySpan<char> GetFamilyName() => null;

	internal override void GetKernedCharWidth(char ch, char chBefore, char chAfter, out float wide, out float abcA, out float abcC) {
		GetCharABCwidths(ch, out int a, out int b, out int c);
		wide = a + b + c;
		abcA = a;
		abcC = 0;
	}

	public void Dispose() {
		if (Font != 0)
			DeleteObject(Font);
		if (DC != 0)
			DeleteDC(DC);
		if (DIB != 0)
			DeleteObject(DIB);
	}
}
#endif
