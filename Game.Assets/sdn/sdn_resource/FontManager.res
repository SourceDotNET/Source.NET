// NOTE: This file is a deviation from Source and is Source.NET exclusive!

FontManager
{
	// These are the fallback fonts MaterialSystem uses when a font cannot be found. The key is the not-found font, and
	// the value is the font it tries to use as a replacement. You can chain these fallback fonts together. 

	// "NULL" is a reserved name which is used when all other font options have been exhausted.
	// The following [$CONDITIONAL]'s can be used:
	//		- [$WINDOWS] for Windows exclusive fonts
	//		- [$OSX]     for Mac OS exclusive fonts
	//		- [$LINUX]   for Linux exclusive fonts
	//		- [$POSIX]   for POSIX (ie. Mac OS/Linux) exclusive fonts

	FallbackFonts
	{
		// Windows font fallbacks
		"Times New Roman"			"Courier New"			[$WINDOWS]
		"Courier New"				"Courier"				[$WINDOWS]
		"Verdana"					"Arial"					[$WINDOWS]
		"Trebuchet MS"				"Arial"					[$WINDOWS]
		"Tahoma"					"NULL"					[$WINDOWS]
		"NULL"						"Tahoma"				[$WINDOWS]

		// Things aren't as well tested beyond this point. Part of the reason for why I'm separating fallback fonts
		// into their own .res file is the hope that it makes it easier to find better fallback fonts.

		// OSX font fallbacks
		"Marlett"					"Apple Symbols"			[$OSX]
		"Lucida Console"			"Lucida Grande"			[$OSX]
		"Tahoma"					"Helvetica"				[$OSX]
		"Helvetica"					"Monaco"				[$OSX]
		"Monaco" 					"NULL"					[$OSX]
		"NULL"						"Monaco"				[$OSX]

		// Linux font fallbacks
		"Noto Sans"					"NULL"					[$LINUX]
		"NULL"						"Noto Sans"				[$LINUX]
	}
	
	// IsFontForeignLanguageCapable checks against these.
	
	ValidAsianFonts
	{
		"1"							"Marlett"				[$WINDOWS]
		
		"1"							"Apple Symbols"			[$OSX]
		
		"1"							"Marlett"				[$LINUX]
		"2"							"WenQuanYi Zen Hei"		[$LINUX]
		"3"							"unifont"				[$LINUX]
	}
}