using System.Runtime.InteropServices;
using System.Text;

namespace Source.SDLManager;

public unsafe partial class SDL3_System
{
	const string FontConfig = "libfontconfig.so.1";
	const int FcMatchPattern = 0;
	const int FcResultMatch = 0;

	[DllImport(FontConfig)] static extern nint FcNameParse(byte* name);
	[DllImport(FontConfig)] static extern int FcConfigSubstitute(nint config, nint pattern, int kind);
	[DllImport(FontConfig)] static extern void FcDefaultSubstitute(nint pattern);
	[DllImport(FontConfig)] static extern nint FcFontMatch(nint config, nint pattern, out int result);
	[DllImport(FontConfig)] static extern int FcPatternGetString(nint pattern, byte* obj, int n, out byte* value);
	[DllImport(FontConfig)] static extern void FcPatternDestroy(nint pattern);

	public ReadOnlySpan<char> GetSystemFontPath(ReadOnlySpan<char> fontName, int weight = 0, bool italic = false) {
		try {
			byte[] name = Encoding.UTF8.GetBytes(new string(fontName) + '\0');
			nint pattern;
			fixed (byte* pName = name)
				pattern = FcNameParse(pName);
			if (pattern == 0)
				return null;

			try {
				FcConfigSubstitute(0, pattern, FcMatchPattern);
				FcDefaultSubstitute(pattern);

				nint match = FcFontMatch(0, pattern, out int result);
				if (match == 0)
					return null;

				try {
					fixed (byte* pFile = "file"u8)
						if (result == FcResultMatch && FcPatternGetString(match, pFile, 0, out byte* file) == FcResultMatch && file != null)
							return Marshal.PtrToStringUTF8((nint)file);
				}
				finally {
					FcPatternDestroy(match);
				}
			}
			finally {
				FcPatternDestroy(pattern);
			}
		}
		catch (Exception ex) {
			Assert($"GetSystemFontPath: {ex.Message}");
		}

		return null;
	}
}
