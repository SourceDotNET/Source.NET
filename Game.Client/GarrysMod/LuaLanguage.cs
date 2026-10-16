using Source;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;

using System.Runtime.CompilerServices;

#if MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Client.GarrysMod;
#endif

public static partial class LuaLanguage
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_language = new("language");

	static KeyValues? LocalizedStrings;

	static KeyValues GetLocalizedStringsKV() => LocalizedStrings ??= new KeyValues("LocalizedStrings");

	[LuaFunction]
	static int Add(ILuaInterface lua) {
		string? key = lua.GetString(1);
		string? value = lua.GetString(2);
		if (key == null)
			lua.ErrorFromLua("language.Add expects string as first argument!\n");
		else if (value == null)
			lua.ErrorFromLua("language.Add expects string as second argument!\n");
		else {
			KeyValues kv = GetLocalizedStringsKV();
			kv.SetString(key, value);
			localize.AddString(key, kv.GetString(key, ""), "GMod_Language_Add.txt");
		}
		return 0;
	}

	[InlineArray(4096)] struct InlineArrayPhraseBuffer { char first; }
	static InlineArrayPhraseBuffer PhraseBuffer;

	[LuaFunction]
	static int GetPhrase(ILuaInterface lua) {
		string token = lua.CheckString(1);
		if (filesystem.Language().GetString(token, PhraseBuffer)) {
			lua.PushString(((ReadOnlySpan<char>)PhraseBuffer).SliceNullTerminatedString());
			return 1;
		}

		ReadOnlySpan<char> localized = localize.Find(token);
		if (localized.IsEmpty) {
			lua.PushString(token);
			return 1;
		}

		lua.PushString(localized);
		return 1;
	}
}
