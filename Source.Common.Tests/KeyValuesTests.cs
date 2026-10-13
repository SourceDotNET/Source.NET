using Source.Common.Formats.Keyvalues;

using System.Text;

namespace Source.Common.Tests;

public class KeyValuesTests
{
	static KeyValues Parse(string text, bool escapeSequences = false) {
		KeyValues kv = new();
		kv.UsesEscapeSequences(escapeSequences);
		Assert.True(kv.LoadFromBuffer("test", text));
		return kv;
	}

	static KeyValues Value(string rawValue, bool escapeSequences = false)
		=> Parse("\"Root\"\n{\n\t\"v\"\t" + rawValue + "\n}\n", escapeSequences).FindKey("v")!;

	static string[] ChildNames(KeyValues kv) => [.. kv.Select(x => x.Name)];

	[Fact]
	public void SkipWhitespaceMatchesCharIsWhiteSpace() {
		for (int c = char.MinValue; c <= char.MaxValue; c++) {
			KeyValues.KeyValuesReader reader = new((char)c + "x");

			Assert.Equal(char.IsWhiteSpace((char)c), KeyValues.SkipWhitespace(reader));
			Assert.Equal(char.IsWhiteSpace((char)c) ? 'x' : c, reader.Peek());
		}
	}

	[Fact]
	public void QuotedRootWithPairs() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"a\"\t\"1\"\n\t\"b\"\t\"two\"\n}\n");

		Assert.Equal("Root", kv.Name);
		Assert.Equal(KeyValues.Types.None, kv.Type);
		Assert.Equal(2, kv.Count);
		Assert.Equal(["a", "b"], ChildNames(kv));
		Assert.Equal(1, kv.GetInt("a"));
		Assert.Equal("two", kv.GetString("b").ToString());
	}

	[Fact]
	public void UnquotedTokens() {
		KeyValues kv = Parse("Root\n{\n\tkey value\n\tnum 42\n}\n");

		Assert.Equal("Root", kv.Name);
		Assert.Equal(["key", "num"], ChildNames(kv));
		Assert.Equal("value", kv.GetString("key").ToString());
		Assert.Equal(42, kv.GetInt("num"));
	}

	[Fact]
	public void MixedQuotedAndUnquotedTokens() {
		KeyValues kv = Parse("\"Root\"\n{\n\tkey \"quoted value\"\n\t\"quoted key\" value\n}\n");

		Assert.Equal("quoted value", kv.GetString("key").ToString());
		Assert.Equal("value", kv.GetString("quoted key").ToString());
	}

	[Fact]
	public void NestedSectionsKeepOrder() {
		KeyValues kv = Parse("""
			"Root"
			{
				"first"		"1"
				"Section"
				{
					"inner"		"x"
					"Deeper"
					{
						"leaf"	"y"
					}
				}
				"last"		"2"
			}
			""");

		Assert.Equal(["first", "Section", "last"], ChildNames(kv));

		KeyValues section = kv.FindKey("Section")!;
		Assert.Equal(KeyValues.Types.None, section.Type);
		Assert.Equal(["inner", "Deeper"], ChildNames(section));
		Assert.Equal("x", section.GetString("inner").ToString());
		Assert.Equal("y", section.FindKey("Deeper")!.GetString("leaf").ToString());
		Assert.Equal(2, kv.GetInt("last"));
	}

	[Fact]
	public void EmptySection() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"Empty\"\n\t{\n\t}\n\t\"after\"\t\"1\"\n}\n");

		KeyValues empty = kv.FindKey("Empty")!;
		Assert.Equal(KeyValues.Types.None, empty.Type);
		Assert.Equal(0, empty.Count);
		Assert.Equal(1, kv.GetInt("after"));
	}

	[Fact]
	public void CompactSingleLine() {
		KeyValues kv = Parse("\"Root\"{\"a\" \"1\" \"Sub\"{\"b\" \"2\"}\"c\" \"3\"}");

		Assert.Equal(["a", "Sub", "c"], ChildNames(kv));
		Assert.Equal(2, kv.FindKey("Sub")!.GetInt("b"));
		Assert.Equal(3, kv.GetInt("c"));
	}

	[Fact]
	public void NoTrailingNewline() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"a\"\t\"1\"\n}");

		Assert.Equal(1, kv.Count);
		Assert.Equal(1, kv.GetInt("a"));
	}

	[Fact]
	public void WindowsLineEndingsAndTabs() {
		KeyValues kv = Parse("\"Root\"\r\n{\r\n\t\"a\"\t\t\"1\"\r\n\t\"b\"\t\t\"two words\"\r\n}\r\n");

		Assert.Equal(["a", "b"], ChildNames(kv));
		Assert.Equal("two words", kv.GetString("b").ToString());
	}

	[Fact]
	public void CommentsAreSkipped() {
		KeyValues kv = Parse("""
			// leading comment
			"Root" // after the root name
			{
				// a full line comment
				"a"		"1" // trailing comment
				//"commented"	"out"
				"b"		"2"
			}
			// comment at the end with no newline
			""");

		Assert.Equal("Root", kv.Name);
		Assert.Equal(["a", "b"], ChildNames(kv));
		Assert.Equal(1, kv.GetInt("a"));
		Assert.Equal(2, kv.GetInt("b"));
	}

	[Fact]
	public void QuotedValueKeepsSpecialCharacters() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"k\"\t\"a b { } // not a comment [$X360]\"\n\t\"after\"\t\"1\"\n}\n");

		Assert.Equal("a b { } // not a comment [$X360]", kv.GetString("k").ToString());
		Assert.Equal(1, kv.GetInt("after"));
	}

	[Fact]
	public void UnquotedValueMayContainSlash() {
		KeyValues kv = Parse("\"Root\"\n{\n\tpath sound/weapons/shot.wav\n\tabs /root/file\n}\n");

		Assert.Equal("sound/weapons/shot.wav", kv.GetString("path").ToString());
		Assert.Equal("/root/file", kv.GetString("abs").ToString());
	}

	[Fact]
	public void DuplicateKeysAreKeptInOrder() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"wave\"\t\"a.wav\"\n\t\"wave\"\t\"b.wav\"\n\t\"wave\"\t\"c.wav\"\n}\n");

		Assert.Equal(3, kv.Count);
		Assert.Equal(["a.wav", "b.wav", "c.wav"], kv.Select(x => x.GetString().ToString()).ToArray());
		Assert.Equal("a.wav", kv.GetString("wave").ToString());
	}

	[Fact]
	public void FindKeyIsCaseInsensitive() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"MixedCase\"\t\"1\"\n}\n");

		Assert.NotNull(kv.FindKey("mixedcase"));
		Assert.NotNull(kv.FindKey("MIXEDCASE"));
		Assert.Equal("MixedCase", kv.FindKey("mixedcase")!.Name);
		Assert.Null(kv.FindKey("missing"));
	}

	[Theory]
	[InlineData("\"5\"", 5)]
	[InlineData("\"-12\"", -12)]
	[InlineData("\"+7\"", 7)]
	[InlineData("\"0\"", 0)]
	[InlineData("42", 42)]
	[InlineData("\"2147483647\"", int.MaxValue)]
	public void IntegerValues(string raw, int expected) {
		KeyValues v = Value(raw);

		Assert.Equal(KeyValues.Types.Int, v.Type);
		Assert.Equal(expected, Assert.IsType<int>(v.Value));
	}

	[Theory]
	[InlineData("\"1.5\"", 1.5)]
	[InlineData("\"-0.25\"", -0.25)]
	[InlineData("\".5\"", 0.5)]
	[InlineData("\"1e3\"", 1000.0)]
	[InlineData("0.75", 0.75)]
	public void FloatValues(string raw, double expected) {
		KeyValues v = Value(raw);

		Assert.Equal(KeyValues.Types.Double, v.Type);
		Assert.Equal(expected, Assert.IsType<double>(v.Value));
	}

	[Fact]
	public void FloatValuesAreSinglePrecision() {
		KeyValues v = Value("\"1.1\"");

		Assert.Equal((double)1.1f, Assert.IsType<double>(v.Value));
	}

	[Theory]
	[InlineData("\"abc\"", "abc")]
	[InlineData("\"12abc\"", "12abc")]
	[InlineData("\"1.5.2\"", "1.5.2")]
	[InlineData("\"\"", "")]
	[InlineData("\"99999999999\"", "99999999999")]
	[InlineData("\"0x10\"", "0x10")]
	[InlineData("\"255 128 0 255\"", "255 128 0 255")]
	[InlineData("word", "word")]
	public void StringValues(string raw, string expected) {
		KeyValues v = Value(raw);

		Assert.Equal(KeyValues.Types.String, v.Type);
		Assert.Equal(expected, Assert.IsType<string>(v.Value));
	}

	[Theory]
	[InlineData("\"0x00000000000000FF\"", 0xFFul)]
	[InlineData("\"0x0123456789abcdef\"", 0x0123456789ABCDEFul)]
	[InlineData("\"0xFFFFFFFFFFFFFFFF\"", ulong.MaxValue)]
	public void Uint64Values(string raw, ulong expected) {
		KeyValues v = Value(raw);

		Assert.Equal(KeyValues.Types.Uint64, v.Type);
		Assert.Equal(expected, Assert.IsType<ulong>(v.Value));
	}

	[Fact]
	public void BackslashesAreLiteralWithoutEscapeSequences() {
		KeyValues v = Value("\"C:\\dir\\new\"", escapeSequences: false);

		Assert.Equal("C:\\dir\\new", v.GetString().ToString());
	}

	[Fact]
	public void EscapeSequencesAreTranslated() {
		KeyValues v = Value("\"line1\\nline2 q:\\\"x\\\"\"", escapeSequences: true);

		Assert.Equal("line1\nline2 q:\"x\"", v.GetString().ToString());
	}

	[Fact]
	public void ConditionalsOnValues() {
		KeyValues kv = Parse("""
			"Root"
			{
				"pc"		"1"	[$WIN32]
				"notpc"		"1"	[!$WIN32]
				"x360"		"1"	[$X360]
				"notx360"	"1"	[!$X360]
				"unknown"	"1"	[$SOMETHINGELSE]
				"notunknown"	"1"	[!$SOMETHINGELSE]
				"plain"		"1"
			}
			""");

		Assert.Equal(["pc", "notx360", "notunknown", "plain"], ChildNames(kv));
	}

	[Fact]
	public void ConditionalsOnSections() {
		KeyValues kv = Parse("""
			"Root"
			{
				"Console"	[$X360]
				{
					"a"	"1"
				}
				"Desktop"	[$WIN32]
				{
					"b"	"2"
				}
				"after"		"3"
			}
			""");

		Assert.Equal(["Desktop", "after"], ChildNames(kv));
		Assert.Equal(2, kv.FindKey("Desktop")!.GetInt("b"));
		Assert.Equal(3, kv.GetInt("after"));
	}

	[Fact]
	public void PlatformConditionalsMatchTheBuildPlatform() {
		KeyValues kv = Parse("""
			"Root"
			{
				"windows"	"1"	[$WINDOWS]
				"linux"		"1"	[$LINUX]
				"osx"		"1"	[$OSX]
				"posix"		"1"	[$POSIX]
			}
			""");

		Assert.Equal(OperatingSystem.IsWindows(), kv.FindKey("windows") != null);
		Assert.Equal(OperatingSystem.IsLinux(), kv.FindKey("linux") != null);
		Assert.Equal(OperatingSystem.IsMacOS(), kv.FindKey("osx") != null);
		Assert.Equal(OperatingSystem.IsLinux(), kv.FindKey("posix") != null);
	}

	[Fact]
	public void MultipleRootsBecomePeers() {
		KeyValues kv = Parse("\"A\"\n{\n\t\"x\"\t\"1\"\n}\n\"B\"\n{\n\t\"y\"\t\"2\"\n}\n");

		Assert.Equal("A", kv.Name);
		Assert.Equal(1, kv.GetInt("x"));

		KeyValues? b = kv.GetNextKey();
		Assert.NotNull(b);
		Assert.Equal("B", b.Name);
		Assert.Equal(2, b.GetInt("y"));
		Assert.Null(b.GetNextKey());
	}

	[Theory]
	[InlineData("")]
	[InlineData("   \r\n\t\n")]
	[InlineData("// only a comment\n")]
	public void NothingToParseFails(string text) {
		KeyValues kv = new();

		Assert.False(kv.LoadFromBuffer("test", text));
	}

	[Fact]
	public void NullStreamFails() {
		Assert.False(new KeyValues().LoadFromStream(null));
	}

	[Fact]
	public void StreamWithUtf8ByteOrderMark() {
		byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("\"Root\"\n{\n\t\"name\"\t\"caf\u00e9\"\n}\n")];
		KeyValues kv = new();

		Assert.True(kv.LoadFromStream(new MemoryStream(bytes)));
		Assert.Equal("Root", kv.Name);
		Assert.Equal("caf\u00e9", kv.GetString("name").ToString());
	}

	[Fact]
	public void SoundScriptShapedFile() {
		KeyValues kv = Parse("""
			// Channels
			//	CHAN_AUTO		= 0,

			"Weapon_Pistol.Single"
			{
				"channel"		"CHAN_WEAPON"
				"volume"		"0.55"
				"soundlevel"	"SNDLVL_GUNFIRE"
				"pitch"			"98,102"

				"rndwave"
				{
					"wave"	")weapons/pistol/pistol_fire2.wav"
					"wave"	")weapons/pistol/pistol_fire3.wav"
				}
			}

			"Weapon_Pistol.Empty"
			{
				"channel"		"CHAN_ITEM"
				"volume"		"0.8"
				"soundlevel"	"SNDLVL_NORM"
				"pitch"			"PITCH_NORM"
				"wave"			"weapons/pistol/pistol_empty.wav"
			}
			""");

		Assert.Equal("Weapon_Pistol.Single", kv.Name);
		Assert.Equal("CHAN_WEAPON", kv.GetString("channel").ToString());
		Assert.Equal(0.55f, kv.GetFloat("volume"));
		Assert.Equal("98,102", kv.GetString("pitch").ToString());

		KeyValues rndwave = kv.FindKey("rndwave")!;
		Assert.Equal([")weapons/pistol/pistol_fire2.wav", ")weapons/pistol/pistol_fire3.wav"], rndwave.Select(x => x.GetString().ToString()).ToArray());

		KeyValues empty = kv.GetNextKey()!;
		Assert.Equal("Weapon_Pistol.Empty", empty.Name);
		Assert.Equal("weapons/pistol/pistol_empty.wav", empty.GetString("wave").ToString());
		Assert.Null(empty.GetNextKey());
	}

	[Fact]
	public void MissingKeysReturnDefaults() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"a\"\t\"1\"\n}\n");

		Assert.Equal("fallback", kv.GetString("missing", "fallback").ToString());
		Assert.True(kv.GetString("missing").IsEmpty);
		Assert.Equal(7, kv.GetInt("missing", 7));
		Assert.Equal(0, kv.GetInt("missing"));
		Assert.Equal(1.5f, kv.GetFloat("missing", 1.5f));
		Assert.Equal(2.5, kv.GetDouble("missing", 2.5));
		Assert.True(kv.GetBool("missing", true));
		Assert.False(kv.GetBool("missing", false));
		Assert.Equal(9ul, kv.GetUint64("missing", 9ul));
		Assert.Equal(new Color(1, 2, 3, 4), kv.GetColor("missing", new Color(1, 2, 3, 4)));
		Assert.Null(kv.GetPtr("missing"));
	}

	[Fact]
	public void NumericConversionsByKey() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"i\"\t\"5\"\n\t\"f\"\t\"2.75\"\n\t\"s\"\t\"text\"\n\t\"zero\"\t\"0\"\n}\n");

		Assert.Equal(5, kv.GetInt("i"));
		Assert.Equal(5f, kv.GetFloat("i"));
		Assert.Equal(5.0, kv.GetDouble("i"));
		Assert.True(kv.GetBool("i", false));

		Assert.Equal(2, kv.GetInt("f"));
		Assert.Equal(2.75f, kv.GetFloat("f"));
		Assert.Equal(2.75, kv.GetDouble("f"));

		Assert.Equal(3, kv.GetInt("s", 3));
		Assert.Equal(3f, kv.GetFloat("s", 3f));
		Assert.False(kv.GetBool("zero", true));
	}

	[Fact]
	public void GetStringFormatsNumbers() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"i\"\t\"5\"\n\t\"f\"\t\"1.5\"\n}\n");

		Assert.Equal("5", kv.GetString("i").ToString());
		Assert.Equal("1.500000", kv.GetString("f").ToString());
	}

	[Fact]
	public void GetColorParsesComponents() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"c\"\t\"255 128 0 64\"\n}\n");

		Assert.Equal(new Color(255, 128, 0, 64), kv.GetColor("c"));
	}

	[Fact]
	public void SettersCreateAndOverwriteKeys() {
		KeyValues kv = new("Root");

		kv.SetString("s", "one");
		kv.SetInt("i", 3);
		kv.SetFloat("f", 0.5f);
		kv.SetUint64("u", 0x1_0000_0000ul);
		kv.SetColor("c", new Color(1, 2, 3, 4));
		kv.SetString("s", "two");
		kv.SetInt("I", 4);

		Assert.Equal(["s", "i", "f", "u", "c"], ChildNames(kv));
		Assert.Equal("two", kv.GetString("s").ToString());
		Assert.Equal(4, kv.GetInt("i"));
		Assert.Equal(0.5f, kv.GetFloat("f"));
		Assert.Equal(0x1_0000_0000ul, kv.GetUint64("u"));
		Assert.Equal(new Color(1, 2, 3, 4), kv.GetColor("c"));
	}

	[Fact]
	public void FindKeyCanCreate() {
		KeyValues kv = new("Root");

		Assert.Null(kv.FindKey("new"));
		KeyValues created = kv.FindKey("new", create: true)!;

		Assert.Equal("new", created.Name);
		Assert.Same(created, kv.FindKey("NEW"));
		Assert.Equal(1, kv.Count);
	}

	[Fact]
	public void SubKeyIterationHelpers() {
		KeyValues kv = Parse("""
			"Root"
			{
				"v1"	"1"
				"S1"
				{
					"a"	"1"
				}
				"v2"	"2"
				"S2"
				{
					"b"	"2"
				}
			}
			""");

		List<string> all = [];
		for (KeyValues? k = kv.GetFirstSubKey(); k != null; k = k.GetNextKey())
			all.Add(k.Name);
		Assert.Equal(["v1", "S1", "v2", "S2"], all);

		List<string> sections = [];
		for (KeyValues? k = kv.GetFirstTrueSubKey(); k != null; k = k.GetNextTrueSubKey())
			sections.Add(k.Name);
		Assert.Equal(["S1", "S2"], sections);

		List<string> values = [];
		for (KeyValues? k = kv.GetFirstValue(); k != null; k = k.GetNextValue())
			values.Add(k.Name);
		Assert.Equal(["v1", "v2"], values);
	}

	[Fact]
	public void IsEmptyReportsMissingAndEmptySections() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"Empty\"\n\t{\n\t}\n\t\"Full\"\n\t{\n\t\t\"a\"\t\"1\"\n\t}\n\t\"value\"\t\"1\"\n}\n");

		Assert.True(kv.IsEmpty("missing"));
		Assert.True(kv.IsEmpty("Empty"));
		Assert.False(kv.IsEmpty("Full"));
		Assert.False(kv.IsEmpty("value"));
	}

	[Fact]
	public void MakeCopyIsDeep() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"a\"\t\"1\"\n\t\"Sub\"\n\t{\n\t\t\"b\"\t\"text\"\n\t}\n}\n");

		KeyValues copy = kv.MakeCopy();
		copy.SetInt("a", 99);
		copy.FindKey("Sub")!.SetString("b", "changed");
		copy.SetString("added", "x");

		Assert.Equal("Root", copy.Name);
		Assert.Equal(1, kv.GetInt("a"));
		Assert.Equal("text", kv.FindKey("Sub")!.GetString("b").ToString());
		Assert.Null(kv.FindKey("added"));
		Assert.Equal(99, copy.GetInt("a"));
		Assert.Equal("changed", copy.FindKey("Sub")!.GetString("b").ToString());
	}

	[Fact]
	public void RemoveSubKey() {
		KeyValues kv = Parse("\"Root\"\n{\n\t\"a\"\t\"1\"\n\t\"b\"\t\"2\"\n\t\"c\"\t\"3\"\n}\n");

		kv.RemoveSubKey(kv.FindKey("b"));
		Assert.Equal(["a", "c"], ChildNames(kv));

		kv.RemoveSubKey(kv.FindKey("a"));
		Assert.Equal(["c"], ChildNames(kv));

		kv.RemoveSubKey(null);
		Assert.Equal(["c"], ChildNames(kv));
	}

	[Fact]
	public void CreateNewKeyUsesNextNumber() {
		KeyValues kv = new("Root");

		Assert.Equal("1", kv.CreateNewKey().Name);
		Assert.Equal("2", kv.CreateNewKey().Name);
		kv.CreateKey("10");
		Assert.Equal("11", kv.CreateNewKey().Name);
		Assert.Equal(4, kv.Count);
	}

	[Fact]
	public void ChainedKeyValuesAreSearchedAfterOwnKeys() {
		KeyValues baseKv = Parse("\"Base\"\n{\n\t\"shared\"\t\"base\"\n\t\"onlybase\"\t\"1\"\n}\n");
		KeyValues kv = Parse("\"Derived\"\n{\n\t\"shared\"\t\"derived\"\n}\n");
		kv.ChainKeyValue(baseKv);

		Assert.Equal("derived", kv.GetString("shared").ToString());
		Assert.Equal(1, kv.GetInt("onlybase"));
		Assert.Null(kv.FindKey("nowhere"));
	}

	[Fact]
	public void DirectValueSetters() {
		KeyValues kv = new("k");

		kv.SetStringValue("text");
		Assert.Equal(KeyValues.Types.String, kv.Type);
		Assert.Equal("text", kv.GetString().ToString());

		kv.SetInt(12);
		Assert.Equal(KeyValues.Types.Int, kv.Type);
		Assert.Equal(12, kv.GetInt());
		Assert.True(kv.GetBool());

		kv.SetName("renamed");
		Assert.Equal("renamed", kv.Name);
	}
}
