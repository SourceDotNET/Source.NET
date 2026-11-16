using System.Globalization;
using System.Text;

namespace Source.Common.Tests;

public class RapidJsonTests
{
	[Theory]
	[InlineData(0.0, "0.0")]
	[InlineData(-0.0, "-0.0")]
	[InlineData(0.1, "0.1")]
	[InlineData(0.5, "0.5")]
	[InlineData(1.5, "1.5")]
	[InlineData(-2.25, "-2.25")]
	[InlineData(0.0001, "0.0001")]
	[InlineData(-0.0001, "-0.0001")]
	[InlineData(123456.789, "123456.789")]
	[InlineData(3000000000.0, "3000000000.0")]
	[InlineData(9007199254740992.0, "9007199254740992.0")]
	[InlineData(1e21, "1e21")]
	[InlineData(1e22, "1e22")]
	[InlineData(1.2345e30, "1.2345e30")]
	[InlineData(1e300, "1e300")]
	[InlineData(5e-324, "5e-324")]
	[InlineData(1.7976931348623157e308, "1.7976931348623157e308")]
	[InlineData(0.3, "0.3")]
	[InlineData(2.0002, "2.0002")]
	[InlineData(1e-5, "0.00001")]
	[InlineData(1.5e-7, "1.5e-7")]
	[InlineData(1234567.1234, "1234567.1234")]
	[InlineData(549755813887.9999, "549755813887.9999")]
	[InlineData(0.3333, "0.3333")]
	[InlineData(4.35, "4.35")]
	[InlineData(1e15, "1000000000000000.0")]
	[InlineData(123456789012345680.0, "123456789012345680.0")]
	[InlineData(-1e-10, "-1e-10")]
	public void DtoaMatchesRapidJson(double value, string expected) {
		Span<byte> buffer = stackalloc byte[RapidJson.Dtoa.MaxLength];
		Assert.Equal(expected, Encoding.ASCII.GetString(buffer[..RapidJson.Dtoa.Write(value, buffer)]));
	}

	static string Write(bool pretty) {
		CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
		RapidJson.Writer writer = new(pretty);
		int root = 0;
		int inner = 0;
		writer.Put((byte)'{');
		writer.Prefix(ref root, false, 1);
		writer.String("a\"\\\n\u0001"u8);
		writer.Prefix(ref root, false, 1);
		writer.Put((byte)'[');
		writer.Prefix(ref inner, true, 2);
		writer.Int(-5);
		writer.Prefix(ref inner, true, 2);
		writer.Double(1.5);
		writer.Prefix(ref inner, true, 2);
		writer.Bool(true);
		writer.Prefix(ref inner, true, 2);
		writer.Null();
		writer.End((byte)']', inner, 1);
		writer.Prefix(ref root, false, 1);
		writer.String("e"u8);
		writer.Prefix(ref root, false, 1);
		writer.Put((byte)'[');
		writer.End((byte)']', 0, 1);
		writer.End((byte)'}', root, 0);
		string result = Encoding.UTF8.GetString(writer.WrittenSpan);
		writer.Dispose();
		return result;
	}

	[Fact]
	public void WriterMatchesRapidJson() {
		Assert.Equal("{\"a\\\"\\\\\\n\\u0001\":[-5,1.5,true,null],\"e\":[]}", Write(false));
	}

	[Fact]
	public void PrettyWriterMatchesRapidJson() {
		Assert.Equal("{\n\t\"a\\\"\\\\\\n\\u0001\": [\n\t\t-5,\n\t\t1.5,\n\t\ttrue,\n\t\tnull\n\t],\n\t\"e\": []\n}", Write(true));
	}

	[Fact]
	public void WriterEscapesLikeRapidJson() {
		RapidJson.Writer writer = new(false);
		writer.String([8, 9, 10, 12, 13, 0, 31, (byte)'/', 127, 200]);
		Assert.Equal("\"\\b\\t\\n\\f\\r\\u0000\\u001F/\u007FÈ\"", Encoding.Latin1.GetString(writer.WrittenSpan));
		writer.Dispose();
	}

	[Fact]
	public void WriterSkipsNaN() {
		RapidJson.Writer writer = new(false);
		Assert.False(writer.Double(double.NaN));
		Assert.Equal(0, writer.Length);
		writer.Dispose();
	}

	[Fact]
	public void WriterGrowsPastThePool() {
		RapidJson.Writer writer = new(false);
		byte[] chunk = new byte[70000];
		Array.Fill(chunk, (byte)'x');
		for (int i = 0; i < 40; i++)
			writer.String(chunk);
		Assert.Equal(40 * 70002, writer.Length);
		Assert.Equal((byte)'"', writer.WrittenSpan[^1]);
		Assert.Equal((byte)'x', writer.WrittenSpan[^2]);
		writer.Dispose();
	}

	[Theory]
	[InlineData(1f, "1")]
	[InlineData(2.5f, "2.5")]
	[InlineData(-3f, "-3")]
	[InlineData(0.00001f, "0")]
	[InlineData(-0.00001f, "-0")]
	[InlineData(0.33333334f, "0.3333")]
	[InlineData(123456.789f, "123456.7891")]
	[InlineData(1e10f, "10000000000")]
	[InlineData(3.4028235e38f, "340282346638528859811704183484516925440")]
	public void NiceFloatMatchesBootil(float value, string expected) {
		CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
		Span<byte> buffer = stackalloc byte[64];
		Assert.Equal(expected, Encoding.ASCII.GetString(buffer[..Bootil.String.Format.NiceFloat(value, buffer)]));
	}
}
