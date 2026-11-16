using System.Runtime.InteropServices;

namespace Source.SDLManager;

public unsafe partial class SDL3_System
{
	const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
	const string CoreText = "/System/Library/Frameworks/CoreText.framework/CoreText";
	const uint kCTFontItalicTrait = 1 << 0;
	const uint kCTFontBoldTrait = 1 << 1;

	[DllImport(CoreFoundation)] static extern nint CFStringCreateWithCharacters(nint alloc, char* chars, nint numChars);
	[DllImport(CoreFoundation)] static extern void CFRelease(nint cf);
	[DllImport(CoreFoundation)] static extern byte CFURLGetFileSystemRepresentation(nint url, byte resolveAgainstBase, byte* buffer, nint maxBufLen);
	[DllImport(CoreText)] static extern nint CTFontCreateWithName(nint name, double size, nint matrix);
	[DllImport(CoreText)] static extern nint CTFontCreateCopyWithSymbolicTraits(nint font, double size, nint matrix, uint symTraitValue, uint symTraitMask);
	[DllImport(CoreText)] static extern nint CTFontCopyAttribute(nint font, nint attribute);

	static nint kCTFontURLAttribute;

	public ReadOnlySpan<char> GetSystemFontPath(ReadOnlySpan<char> fontName, int weight = 0, bool italic = false) {
		if (kCTFontURLAttribute == 0)
			kCTFontURLAttribute = *(nint*)NativeLibrary.GetExport(NativeLibrary.Load(CoreText), "kCTFontURLAttribute");

		nint name;
		fixed (char* chars = fontName)
			name = CFStringCreateWithCharacters(0, chars, fontName.Length);
		if (name == 0)
			return null;

		nint font = CTFontCreateWithName(name, 12, 0);
		CFRelease(name);
		if (font == 0)
			return null;

		uint traits = (weight >= 600 ? kCTFontBoldTrait : 0) | (italic ? kCTFontItalicTrait : 0);
		if (traits != 0) {
			nint styled = CTFontCreateCopyWithSymbolicTraits(font, 0, 0, traits, traits);
			if (styled != 0) {
				CFRelease(font);
				font = styled;
			}
		}

		nint url = CTFontCopyAttribute(font, kCTFontURLAttribute);
		CFRelease(font);
		if (url == 0)
			return null;

		byte* path = stackalloc byte[1024];
		bool ok = CFURLGetFileSystemRepresentation(url, 1, path, 1024) != 0;
		CFRelease(url);
		return ok ? Marshal.PtrToStringUTF8((nint)path) : null;
	}
}
