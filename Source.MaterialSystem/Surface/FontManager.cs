using Source.Common.Filesystem;
using Source.Common.GUI;
using Source.Common.MaterialSystem;

using FreeTypeSharp;
using static FreeTypeSharp.FT;
using System.Runtime.InteropServices;
using Source.Common.Launcher;
using System.Text;
using CommunityToolkit.HighPerformance;
using Source.Common.Formats.Keyvalues;
using System.Diagnostics.CodeAnalysis;

namespace Source.MaterialSystem.Surface;

public abstract class BaseFont
{
	public abstract SurfaceFontFlags GetFlags();
	public abstract int GetHeight();
	public abstract int GetMaxCharWidth();
	public abstract bool IsEqualTo(ReadOnlySpan<char> fontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags);

	public abstract bool GetUnderlined();

	public abstract int GetCharYOffset(char ch);
	public abstract void GetCharABCwidths(char ch, out int a, out int b, out int c);
	internal abstract void GetCharRGBA(char @char, int fontWide, int fontTall, Span<byte> pRGBA);

	internal abstract void GetKernedCharWidth(char ch, char chBefore, char chAfter, out float flWide, out float flabcA, out float flabcC);

	internal abstract ReadOnlySpan<char> GetName();
	internal abstract ReadOnlySpan<char> GetFamilyName();
	internal abstract int GetHeightRequested();
}

public record struct FontRange
{
	public int LowRange;
	public int HighRange;
	public BaseFont Font;
}

public class FontAmalgam : IFont
{
	List<FontRange> Fonts = new(4);
	string Name = "";
	int MaxWidth;
	int MaxHeight;

	public void SetName(ReadOnlySpan<char> name) {
		Name = new(name);
	}

	public SurfaceFontFlags GetFlags(int i) {
		Span<FontRange> fonts = Fonts.AsSpan();
		if (fonts.Length > i) {
			ref FontRange range = ref fonts[i];
			BaseFont? font = range.Font;
			if (font == null) return 0;

			return font.GetFlags();
		}
		else
			return 0;
	}

	public void SetFontScale(float sx, float sy) {
		if (Fonts.Count == 0)
			return;

		if (0 != (GetFlags(0) & SurfaceFontFlags.Bitmap))
			DevWarning("Bitmap isn't supported yet\n");
		else
			Warning("Bitmap isn't supported yet but that's an illegal operation anyway...\n");
	}


	public ReadOnlySpan<char> GetFontName(int i) {
		if (Fonts.IsValidIndex(i) && Fonts[i].Font != null)
			return Fonts[i].Font.GetName();
		else
			return null;
	}

	public ReadOnlySpan<char> GetFontFamilyName(int i) {
		if (Fonts.IsValidIndex(i) && Fonts[i].Font != null)
			return Fonts[i].Font.GetFamilyName();

		return "";
	}

	public nint GetCount() {
		return Fonts.Count;
	}

	public void RemoveAll() {
		Fonts.Clear();
		MaxHeight = 0;
		MaxWidth = 0;
	}

	public BaseFont? GetFontForChar(char ch) {
		Span<FontRange> fonts = Fonts.AsSpan();
		for (int i = 0; i < fonts.Length; i++) {
			if (ch >= fonts[i].LowRange && ch <= fonts[i].HighRange) {
				return fonts[i].Font;
			}
		}

		return null;
	}

	internal void AddFont(BaseFont font, int low, int high) {
		Fonts.Add(new() {
			Font = font,
			LowRange = low,
			HighRange = high
		});

		MaxHeight = Math.Max(font.GetHeight(), MaxHeight);
		MaxWidth = Math.Max(font.GetMaxCharWidth(), MaxWidth);
	}


	public int GetFontHeight() {
		if (Fonts.Count == 0)
			return MaxHeight;

		return Fonts[0].Font.GetHeight();
	}

	internal bool GetUnderlined() {
		if (Fonts.Count == 0)
			return false;

		return Fonts[0].Font.GetUnderlined();
	}

	internal int GetFontMaxWidth() {
		return MaxWidth;
	}

	internal int GetFontHeightRequested() {
		if (Fonts.Count == 0)
			return MaxHeight;

		return Fonts[0].Font.GetHeightRequested();
	}
}

public unsafe class FontManager
{
	readonly IMaterialSystem materialSystem;
	readonly IFileSystem fileSystem;
	readonly ISystem system;
	readonly ISurface surface;
	public FontManager(IMaterialSystem materialSystem, IFileSystem fileSystem, ISystem system, ISurface surface) {
		this.materialSystem = materialSystem;
		this.fileSystem = fileSystem;
		this.system = system;
		this.surface = surface;
	}

	List<FontAmalgam> FontAmalgams = [];
#if WIN32
	List<Win32Font> Win32Fonts = [];
	List<string> CustomFontFileNames = [];
#else
	List<FreeTypeFont> FreeTypeFonts = [];
#endif

	static ulong FontHash(ReadOnlySpan<char> name, int weight) => sprintf(stackalloc char[name.Length + 16], "%s-%i").S(name).I(weight).ToSpan().Hash();

	Dictionary<ulong, nint> FontBinaries = [];
	Dictionary<ulong, nint> FontBinaryLengths = [];
	Dictionary<ulong, string> CustomFontFiles = [];

	public void ClearAllFonts() {
#if WIN32
		for (int i = 0; i < Win32Fonts.Count; i++)
			Win32Fonts[i].Dispose();
		Win32Fonts.Clear();
#else
		FreeTypeFonts.Clear();
#endif

		for (int i = 0; i < FontAmalgams.Count; i++)
			FontAmalgams[i].RemoveAll();
	}

	internal IFont CreateFont() {
		FontAmalgam font = new();
		FontAmalgams.Add(font);
		return font;
	}

	internal bool SetFontGlyphSet(IFont font, ReadOnlySpan<char> fontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags, int rangeMin, int rangeMax) {
		if (font is not FontAmalgam fontAmalgam) {
			Warning($"Invalid IFont input into SetFontGlyphSet!\n");
			return false;
		}

		if (fontAmalgam.GetCount() > 0)
			fontAmalgam.RemoveAll();

		BaseFont? baseFont = CreateOrFindFont(fontName, tall, weight, blur, scanlines, flags);

		do {
			if (IsFontForeignLanguageCapable(fontName)) {
				if (baseFont != null) {
					fontAmalgam.AddFont(baseFont, 0x0000, 0xFFFF);
					return true;
				}
			}
			else {
				ReadOnlySpan<char> localizedFontName = GetForeignFallbackFontName();
				if (baseFont != null && localizedFontName.Equals(fontName, StringComparison.OrdinalIgnoreCase)) {
					fontAmalgam.AddFont(baseFont, 0x0000, 0xFFFF);
					return true;
				}

				BaseFont? extendedFont = CreateOrFindFont(localizedFontName, tall, weight, blur, scanlines, flags);
				if (baseFont != null && extendedFont != null) {
					int min = 0x0000, max = 0x00FF;

					if (rangeMin > 0 || rangeMax > 0) {
						min = rangeMin;
						max = rangeMax;

						if (min > max)
							(max, min) = (min, max);
					}

					if (min > 0)
						fontAmalgam.AddFont(extendedFont, 0x0000, min - 1);

					fontAmalgam.AddFont(baseFont, min, max);

					if (max < 0xFFFF)
						fontAmalgam.AddFont(extendedFont, max + 1, 0xFFFF);

					return true;
				}
				else if (extendedFont != null) {
					fontAmalgam.AddFont(extendedFont, 0x0000, 0xFFFF);
					return true;
				}
			}
		}
		while (!(fontName = GetFallbackFontName(fontName)).IsEmpty);

		return false;
	}

	private ReadOnlySpan<char> GetFallbackFontName(ReadOnlySpan<char> fontName) {
		if (!FontsReady) InitFontSettings();

		int i;
		for (i = 0; FallbackFonts![i].Font != null; i++) {
			if (fontName.Equals(FallbackFonts[i].Font, StringComparison.OrdinalIgnoreCase))
				return FallbackFonts[i].Fallback;
		}

		return FallbackFonts[i].Fallback;
	}

	private ReadOnlySpan<char> GetForeignFallbackFontName() {
#if WIN32
		return "Tahoma";
#elif OSX
		return "Helvetica";
#elif LINUX
		return "WenQuanYi Zen Hei";
#else
#error Please define GetForeignFallbackFontName for this platform.
#endif
	}

	public struct FallbackFont
	{
		public string? Font;
		public string? Fallback;
		public FallbackFont(string? font, string? fallback) {
			Font = font;
			Fallback = fallback;
		}
	}

	string?[]? ValidAsianFonts;
	FallbackFont[]? FallbackFonts;

	private bool IsFontForeignLanguageCapable(ReadOnlySpan<char> fontName) {
		if (!FontsReady) InitFontSettings();
		for (int i = 0; ValidAsianFonts![i] != null; i++) {
			if (fontName.Equals(ValidAsianFonts[i], StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	internal byte* GetFontBinary(ReadOnlySpan<char> fontName, int weight, out nint length) {
		UtlSymId_t hash = FontHash(fontName, weight);

		if (!FontBinaries.TryGetValue(hash, out nint binary)) {
			length = 0;
			return null;
		}

		length = FontBinaryLengths[hash];
		return (byte*)binary;
	}

	private BaseFont? CreateOrFindFont(ReadOnlySpan<char> fontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags) {
#if WIN32
		return CreateOrFindWin32Font(fontName, tall, weight, blur, scanlines, flags);
#else
		return CreateOrFindFreeTypeFont(fontName, tall, weight, blur, scanlines, flags);
#endif
	}

#if WIN32
	private Win32Font? CreateOrFindWin32Font(ReadOnlySpan<char> windowsFontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags) {
		Win32Font? winFont = null;
		int i;
		for (i = 0; i < Win32Fonts.Count; i++) {
			if (Win32Fonts[i].IsEqualTo(windowsFontName, tall, weight, blur, scanlines, flags)) {
				winFont = Win32Fonts[i];
				break;
			}
		}

		if (winFont == null) {
			Win32Font font = new();
			if (font.Create(windowsFontName, tall, weight, blur, scanlines, flags)) {
				Win32Fonts.Add(font);
				winFont = font;
			}
			else {
				font.Dispose();
				return null;
			}
		}

		return winFont;
	}
#else
	private FreeTypeFont? CreateOrFindFreeTypeFont(ReadOnlySpan<char> fontName, int tall, int weight, int blur, int scanlines, SurfaceFontFlags flags) {
		if (!FontsReady) InitFontSettings();

		FreeTypeFont? foundFont = null;
		foreach (var font in FreeTypeFonts) {
			if (font.IsEqualTo(fontName, tall, weight, blur, scanlines, flags)) {
				foundFont = font;
				break;
			}
		}
		if (foundFont == null) {
			ulong hash = FontHash(fontName, weight);
			if (!FontBinaries.TryGetValue(hash, out nint binary)) {
				// Load the binary
				if (!CustomFontFiles.TryGetValue(fontName.Hash(), out string? filePath))
					filePath = new(system.GetSystemFontPath(fontName, weight)); // If not custom font file, load from the OS

				if (string.IsNullOrEmpty(filePath))
					return null; // cannot load...

				FileInfo info = new(filePath);
				if (!info.Exists)
					return null; // cannot load...

				FileStream file = File.OpenRead(filePath);
				binary = (nint)NativeMemory.AllocZeroed((nuint)file.Length);
				using UnmanagedMemoryStream stream = new UnmanagedMemoryStream((byte*)binary, 0, file.Length, FileAccess.Write);
				file.CopyTo(stream);
				FontBinaries[hash] = binary;
				FontBinaryLengths[hash] = (nint)info.Length;
			}

			FreeTypeFont font = new FreeTypeFont(this, system);
			if (font.Create(fontName, tall, weight, blur, scanlines, flags)) {
				foundFont = font;
				FreeTypeFonts.Add(font);
			}
			else
				return null;
		}
		return foundFont;
	}
#endif

	public struct FT_SfntName
	{
		public ushort PlatformID;
		public ushort EncodingID;
		public ushort LanguageID;
		public ushort NameID;
		public byte* String;
		public uint StringLen;
	}

	internal bool AddCustomFontFile(ReadOnlySpan<char> fontName, ReadOnlySpan<char> fontFileName) {
#if WIN32
		ReadOnlySpan<char> fullPath = fileSystem.RelativePathToFullPath(fontFileName, null, stackalloc char[260]);
		if (fullPath.IsEmpty) {
			Warning($"Couldn't find custom font file '{fontFileName}'\n");
			return false;
		}

		string path = new(fullPath);
		int i;
		for (i = 0; i < CustomFontFileNames.Count; i++) {
			if (CustomFontFileNames[i] == path)
				break;
		}
		if (i >= CustomFontFileNames.Count) {
			CustomFontFileNames.Add(path);
			fileSystem.GetLocalCopy(path);
		}

		if (Win32Font.AddFontResourceExA(path, Win32Font.FR_PRIVATE, 0) > 0)
			return true;

		if (Win32Font.AddFontResourceA(path) < 1) {
			Warning($"Failed to load custom font file '{path}'\n");
			return false;
		}
		return true;
#else
		ReadOnlySpan<char> fontFilepath = fileSystem.RelativePathToFullPath(fontFileName, null, stackalloc char[512]);

		if (fontFilepath.IsEmpty) {
			Warning($"Couldn't find custom font file '{fontFileName}' for font '{(fontName.IsEmpty ? "" : fontName)}'\n");
			return false;
		}

		if (CustomFontFiles.TryGetValue(fontName.Hash(), out string? path))
			return true; // Already loaded

		FileInfo info = new(new(fontFilepath));
		if (!info.Exists) {
			Msg($"Failed to load custom font file '{fontFilepath}'\n");
			return false;
		}

		// We now need to resolve fontName if it wasn't provided.
		if (fontName.IsEmpty) {
			// TODO: This means we load the font twice... not ideal..
			Span<byte> filePathAlloc = stackalloc byte[Encoding.UTF8.GetByteCount(fontFilepath) + 1];
			int written = Encoding.UTF8.GetBytes(fontFilepath, filePathAlloc);
			filePathAlloc[written] = 0;
			FT_Error err;
			FT_FaceRec_* face;
			fixed (byte* pathPtr = filePathAlloc)
				err = FT_New_Face(FreeTypeFont.Library, pathPtr, 0, &face);

			if (err != FT_Error.FT_Err_Ok) {
				Assert(false);
				return false;
			}

			byte* name = face->family_name;
			nint len = 0;
			byte* nameReadForLength = name;
			while (*nameReadForLength != 0) {
				len++;
				nameReadForLength++;
			}
			string nameManaged = Encoding.ASCII.GetString(name, (int)len);
			fontName = nameManaged;
			// don't leave a dead copy lying around
			FT_Done_Face(face);
		}

		// Register the custom font file
		CustomFontFiles[fontName.Hash()] = new(fontFilepath);

		return true;
#endif
	}

	internal int GetFontTall(IFont? font) => ((FontAmalgam?)font)?.GetFontHeight() ?? 0;
	internal bool GetFontUnderlined(IFont font) => ((FontAmalgam?)font)!.GetUnderlined();

	internal bool IsBitmapFont(IFont font) {
		return false; // Todo
	}

	internal BaseFont? GetFontForChar(IFont font, char @char) {
		return ((FontAmalgam?)font)!.GetFontForChar(@char);
	}

	internal int GetCharYOffset(IFont? font, char ch) {
		if (font is not FontAmalgam amalgam) {
			return 0;
		}

		BaseFont? baseFont = amalgam.GetFontForChar(ch);
		return baseFont?.GetCharYOffset(ch) ?? 0;
	}
	internal void GetCharABCwide(IFont? font, char ch, out int a, out int b, out int c) {
		if (font is not FontAmalgam amalgam) {
			a = b = c = 0;
			return;
		}

		BaseFont? baseFont = amalgam.GetFontForChar(ch);
		if (baseFont != null)
			baseFont.GetCharABCwidths(ch, out a, out b, out c);
		else {
			a = c = 0;
			b = amalgam.GetFontMaxWidth();
		}
	}

	internal int IsFontAdditive(IFont? font) {
		if (font is not FontAmalgam amalgam)
			return 0;

		return (amalgam.GetFlags(0) & SurfaceFontFlags.Additive) != 0 ? 1 : 0;
	}

	internal int GetCharacterWidth(IFont? font, char ch) {
		if (!char.IsControl(ch)) {
			GetCharABCwide(font, ch, out int a, out int b, out int c);
			return a + b + c;
		}
		return 0;
	}

	internal void GetTextSize(IFont? font, ReadOnlySpan<char> text, out int wide, out int tall) {
		wide = 0;
		tall = 0;

		if (text.IsEmpty)
			return;

		tall = GetFontTall(font);

		float xx = 0;
		char chBefore = '\0';
		char chAfter = '\0';
		for (int i = 0; ; i++) {
			if (i >= text.Length)
				break;

			char ch = text[i];
			if (ch == 0)
				break;

			chAfter = i + 1 >= text.Length ? '\0' : text[i + 1];

			if (ch == '\n') {
				tall += GetFontTall(font);
				xx = 0;
			}
			else if (ch == '&') { }
			else {
				GetKernedCharWidth(font, ch, chBefore, chAfter, out float flWide, out float flabcA, out float flabcC);
				xx += flWide;
				if (xx > wide) {
					wide = (int)MathF.Ceiling(xx);
				}
			}

			chBefore = ch;
		}
	}

	public void GetKernedCharWidth(IFont? font, char ch, char chBefore, char chAfter, out float flWide, out float flabcA, out float flabcC) {
		flWide = 0.0f;
		flabcA = 0.0f;
		flabcC = 0.0f;

		Assert(font != null);
		if (font == null)
			return;

		if (font is not FontAmalgam fontAmalgam)
			return;

		BaseFont? baseFont = fontAmalgam.GetFontForChar(ch);
		if (baseFont == null) {
			// no font for this range, just use the default width
			flabcA = 0.0f;
			flWide = fontAmalgam.GetFontMaxWidth();
			return;
		}

		if (fontAmalgam.GetFontForChar(chBefore) != font)
			chBefore = '\0';

		if (fontAmalgam.GetFontForChar(chAfter) != font)
			chAfter = '\0';

		baseFont.GetKernedCharWidth(ch, chBefore, chAfter, out flWide, out flabcA, out flabcC);
	}

	bool FontsReady = false;
	internal void InitFontSettings() {
		if (FontsReady)
			return;
		KeyValues fontSettings = new KeyValues();
		fontSettings.UsesConditionals(true);
		using IFileHandle? fh = fileSystem.Open("sdn_resource/FontManager.res", FileOpenOptions.Read, "GAME");
		if (fh == null || !fontSettings.LoadFromStream(fh.Stream)) {
			Error("FontManager.res could not be loaded!\n");
			return;
		}

		KeyValues? fallbackFonts = fontSettings.FindKey("FallbackFonts", false);
		KeyValues? asianFonts = fontSettings.FindKey("ValidAsianFonts", false);
		if (fallbackFonts == null || asianFonts == null) {
			Error("FontManager.res was missing keys.\nEnsure that FallbackFonts and ValidAsianFonts are present, and try again.");
			return;
		}

		// The length of valid asian fonts is +1 for the null at the end.
		FallbackFonts = new FallbackFont[fallbackFonts.Count];
		ValidAsianFonts = new string?[asianFonts.Count + 1];
		int ffidx = 0, vafidx = 0;

		for (KeyValues? kv = fallbackFonts.GetFirstSubKey(); kv != null; kv = kv.GetNextKey()) {
			string? key = kv.Name == "NULL" ? null : kv.Name;
			string? value = kv.GetString() == "NULL" ? null : new(kv.GetString());
			FallbackFonts[ffidx++] = new(key, value);
		}

		for (KeyValues? kv = asianFonts.GetFirstSubKey(); kv != null; kv = kv.GetNextKey())
			ValidAsianFonts[vafidx++] = new(kv.GetString());

		FontsReady = true;
	}

	public ReadOnlySpan<char> GetFontFamilyName(IFont font) {
		return ((FontAmalgam)font).GetFontFamilyName(0);
	}

	public ReadOnlySpan<char> GetFontName(IFont font) {
		return ((FontAmalgam)font).GetFontName(0);
	}

	internal int GetFontTallRequested(IFont? font) {
		return ((FontAmalgam)font!).GetFontHeightRequested();
	}

	public void ClearTemporaryFontCache() {
		// todo?
	}
}
