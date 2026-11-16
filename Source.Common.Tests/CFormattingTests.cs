using System.Globalization;

namespace Source.Common.Tests;

public class CFormattingTests
{
	static string Format(string format, params object?[] args) {
		CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
		CFormatReader reader = new(format);
		Span<char> piece = stackalloc char[16];
		string result = "";
		while (!reader.Overflowed())
			result += new string(piece[..CFormatting.sprintf(piece, ref reader, args)]);
		return result;
	}

	[Theory]
	[InlineData("%d", 42, "42")]
	[InlineData("%i", -42, "-42")]
	[InlineData("%5d", 42, "   42")]
	[InlineData("%-5d|", 42, "42   |")]
	[InlineData("%05d", -42, "-0042")]
	[InlineData("%+d", 42, "+42")]
	[InlineData("% d", 42, " 42")]
	[InlineData("%.3d", 7, "007")]
	[InlineData("%8.3d", -7, "    -007")]
	[InlineData("%.0d", 0, "")]
	[InlineData("%u", -1, "4294967295")]
	[InlineData("%x", 255, "ff")]
	[InlineData("%X", 255, "FF")]
	[InlineData("%#x", 255, "0xff")]
	[InlineData("%#010x", 255, "0x000000ff")]
	[InlineData("%o", 8, "10")]
	[InlineData("%#o", 8, "010")]
	[InlineData("%hd", 65537, "1")]
	[InlineData("%hhu", 511, "255")]
	[InlineData("%d", 4294967298L, "2")]
	[InlineData("%lld", 4294967298L, "4294967298")]
	[InlineData("%I64d", -5000000000L, "-5000000000")]
	[InlineData("%llu", ulong.MaxValue, "18446744073709551615")]
	[InlineData("%llx", long.MinValue, "8000000000000000")]
	[InlineData("%d", 3000000000u, "-1294967296")]
	[InlineData("%u", 3000000000u, "3000000000")]
	[InlineData("%d", (short)-3, "-3")]
	[InlineData("%d", (byte)200, "200")]
	[InlineData("%d", true, "1")]
	[InlineData("%d", DayOfWeek.Friday, "5")]
	[InlineData("%c", 'x', "x")]
	[InlineData("%c", 65, "A")]
	[InlineData("%3c|", 'z', "  z|")]
	public void Integers(string format, object arg, string expected) => Assert.Equal(expected, Format(format, arg));

	[Theory]
	[InlineData("%f", 700.0, "700.000000")]
	[InlineData("%f", 0.1, "0.100000")]
	[InlineData("%.2f", 3.14159, "3.14")]
	[InlineData("%.0f", 2.5, "2")]
	[InlineData("%.0f", 3.5, "4")]
	[InlineData("%.2f", 0.125, "0.12")]
	[InlineData("%10.3f", -3.14159, "    -3.142")]
	[InlineData("%-10.3f|", 3.14159, "3.142     |")]
	[InlineData("%010.3f", -3.14159, "-00003.142")]
	[InlineData("%+.1f", 9.9999, "+10.0")]
	[InlineData("%#.0f", 1.0, "1.")]
	[InlineData("%3.0f", 7.25, "  7")]
	[InlineData("%e", 12345.678, "1.234568e+04")]
	[InlineData("%E", 0.000123456, "1.234560E-04")]
	[InlineData("%.0e", 12345.678, "1e+04")]
	[InlineData("%g", 0.0001, "0.0001")]
	[InlineData("%g", 0.00001, "1e-05")]
	[InlineData("%g", 100000.0, "100000")]
	[InlineData("%g", 1000000.0, "1e+06")]
	[InlineData("%.3g", 3.14159, "3.14")]
	[InlineData("%#g", 0.5, "0.500000")]
	[InlineData("%G", 1e-10, "1E-10")]
	[InlineData("%f", double.PositiveInfinity, "inf")]
	[InlineData("%F", double.NegativeInfinity, "-INF")]
	[InlineData("%f", -0.0, "-0.000000")]
	[InlineData("%.2a", 1.0, "0x1.00p+0")]
	[InlineData("%.0a", 0.5, "0x1p-1")]
	[InlineData("%f", 700f, "700.000000")]
	[InlineData("%.2f", 3.14159f, "3.14")]
	[InlineData("%g", 0.1f, "0.1")]
	[InlineData("%f", 7, "7.000000")]
	public void Floats(string format, object arg, string expected) => Assert.Equal(expected, Format(format, arg));

	[Theory]
	[InlineData("%s", "abc", "abc")]
	[InlineData("%5s", "abc", "  abc")]
	[InlineData("%-5s|", "abc", "abc  |")]
	[InlineData("%.2s", "abc", "ab")]
	[InlineData("%s", null, "(null)")]
	[InlineData("%s", 1.5, "1.5")]
	[InlineData("%s", DayOfWeek.Monday, "Monday")]
	public void Strings(string format, object? arg, string expected) => Assert.Equal(expected, Format(format, arg));

	[Fact]
	public void SeveralArguments() {
		Assert.Equal("sv_gravity = 700.000000\n", Format("%s = %f\n", "sv_gravity", 700f));
		Assert.Equal("   42|42   |3.14", Format("%*d|%-*d|%.*f", 5, 42, 5, 42, 2, 3.14159));
		Assert.Equal("100% done 3%", Format("100%% done %d%%", 3));
	}

	[Fact]
	public void MissingArgumentsStayVerbatim() {
		Assert.Equal("50% done, 10 % of x, %d %5.2f", Format("50% done, 10 % of x, %d %5.2f"));
		Assert.Equal("1 and %s", Format("%d and %s", 1));
		Assert.Equal("%y then 7", Format("%y then %d", 7));
		Assert.Equal("100%", Format("100%"));
	}

	[Fact]
	public void LongOutputSpansPieces() {
		string big = new('x', 100);
		Assert.Equal($"<{big}> 42 <{big}>", Format("<%s> %d <%s>", big, 42, big));
		Assert.Equal(new string(' ', 99) + "1|a" + new string(' ', 99) + "|", Format("%100d|%-100s|", 1, "a"));
	}

	[Fact]
	public void FluentPrintF() {
		CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
		Span<char> buffer = stackalloc char[64];
		Assert.Equal("5:07", CFormatting.sprintf(buffer, "%d:%.2d").D(5).D(7).ToSpan());
		Assert.Equal("1.500 0.5", CFormatting.sprintf(buffer, "%.3f %g").F(1.5f).G(0.5).ToSpan());
		Assert.Equal("12.500000 -0.100000", CFormatting.sprintf(buffer, "%f %f").F(12.5f).F(-0.1f).ToSpan());
		Assert.Equal("007, 60 fps", CFormatting.sprintf(buffer, "%03d,%3i fps").D(7).I(60).ToSpan());
		Assert.Equal("STEAM_0:1:3000000000", CFormatting.sprintf(buffer, "STEAM_0:%u:%u").U(1).U(3000000000).ToSpan());
		Assert.Equal("ab    |  cd|ef", CFormatting.sprintf(buffer, "%-6s|%4s|%.2s").S("ab").S("cd").S("efgh").ToSpan());
		Assert.Equal("12.0M", CFormatting.sprintf(buffer, "%.1fM").D(12).ToSpan());
		Assert.Equal("a/b 50%", CFormatting.sprintf(buffer, "%s%c%s %d%%").S("a").C('/').S("b").D(50).ToSpan());
		Assert.Equal("4294967298", CFormatting.sprintf(buffer, "%d").D(4294967298L).ToSpan());
	}

	[Fact]
	public void ScanFReadsNumbers() {
		CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
		Assert.Equal(3, new ScanF("[1.5 2.5 3.5]", "[%f %f %f]").Read(out float x).Read(out float y).Read(out float z).ReadArguments);
		Assert.Equal((1.5f, 2.5f, 3.5f), (x, y, z));
		Assert.Equal(0, new ScanF("1 2 3", "[%f %f %f]").Read(out x).Read(out y).Read(out z).ReadArguments);
		Assert.Equal(2, new ScanF("640x480", "%i x %i").Read(out int width).Read(out int height).ReadArguments);
		Assert.Equal((640, 480), (width, height));
		Assert.Equal(2, new ScanF("ff 0x10", "%x %x").Read(out int first).Read(out int second).ReadArguments);
		Assert.Equal((255, 16), (first, second));
		Assert.Equal(1, new ScanF("-1.5e+2x", "%f").Read(out x).ReadArguments);
		Assert.Equal(-150f, x);
	}

	[Fact]
	public void ScanFLeavesUnreadValuesAlone() {
		int red = 1, green = 2, blue = 3, alpha = 255;
		Assert.Equal(3, new ScanF("10 20 30", "%d %d %d %d").Read(out red).Read(out green).Read(out blue).Read(out alpha).ReadArguments);
		Assert.Equal((10, 20, 30, 255), (red, green, blue, alpha));
		Assert.Equal(-1, new ScanF("", "%d").Read(out red).ReadArguments);
		Assert.Equal(0, new ScanF("abc", "%d").Read(out red).ReadArguments);
		Assert.Equal(10, red);
	}

	[Fact]
	public void ScanFKeepsItsStateAcrossCalls() {
		ScanF scan = new("1 2 3 4", "%d %d %d %d");
		scan.Read(out int a).Read(out int b).Read(out int c).Read(out int d);
		Assert.Equal(4, scan.ReadArguments);
		Assert.Equal((1, 2, 3, 4), (a, b, c, d));
	}

	[Fact]
	public void FluentPrintFClampsToTheBuffer() {
		Span<char> buffer = stackalloc char[6];
		PrintF result = CFormatting.sprintf(buffer, "%s-%d").S("abcdef").D(123456);
		Assert.Equal("abcdef", result.ToSpan());
		Assert.True(result.Overflowed);
	}
}
