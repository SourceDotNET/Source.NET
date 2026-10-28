using Source.Common.Bitbuffers;

using System.Numerics;
using System.Text;

using static Source.Common.CoordSize;

namespace Source.Common.Tests;

public class BitBufferTests
{
	const int BufferBytes = 16;
	const byte Marker = 0x5A;

	static uint Mask(int bits) => bits == 32 ? uint.MaxValue : (1u << bits) - 1;

	static string Written(bf_write writer, byte[] data) => Convert.ToHexString(data, 0, BitBuffer.BitByte(writer.BitsWritten));

	static int SetBits(byte[] data) => data.Sum(b => BitOperations.PopCount(b));

	static bf_read StringThenMarker(string text) {
		byte[] data = new byte[64];
		bf_write writer = new(data, data.Length);
		writer.WriteString(text);
		writer.WriteByte(Marker);
		return new bf_read(data, data.Length);
	}

	[Fact]
	public void ReadUBitLongReadsWhatWasWrittenAtEveryOffsetAndWidth() {
		Random random = new(1234);
		byte[] data = new byte[BufferBytes];

		for (int offset = 0; offset < 32; offset++) {
			for (int bits = 1; bits <= 32; bits++) {
				uint value = (uint)random.NextInt64() & Mask(bits);

				Array.Clear(data);
				bf_write writer = new(data, data.Length);
				writer.Seek(offset);
				writer.WriteUBitLong(value, bits);

				bf_read reader = new(data, data.Length);
				reader.Seek(offset);
				Assert.Equal(value, reader.ReadUBitLong(bits));
				Assert.Equal(offset + bits, reader.BitsRead);
			}
		}
	}

	[Fact]
	public void ReadUBitLongOnlyReturnsTheRequestedBits() {
		byte[] data = new byte[BufferBytes];
		Array.Fill(data, (byte)0xFF);

		for (int offset = 0; offset < 32; offset++) {
			for (int bits = 1; bits <= 32; bits++) {
				bf_read reader = new(data, data.Length);
				reader.Seek(offset);
				Assert.Equal(Mask(bits), reader.ReadUBitLong(bits));
			}
		}
	}

	[Fact]
	public void ReadUBitLongPastTheEndOverflows() {
		byte[] data = new byte[4];
		bf_read reader = new(data, data.Length);
		reader.ReadUBitLong(20);

		Assert.Equal(0u, reader.ReadUBitLong(13));
		Assert.True(reader.Overflowed);
		Assert.Equal(32, reader.BitsRead);
	}

	[Theory]
	[InlineData(0x12345678u, 0x12345678u, false)]
	[InlineData(0x12345678u, 0x12345679u, true)]
	[InlineData(0x80000000u, 0x00000000u, true)]
	public void CompareBitsReportsDifferenceAndAdvancesBothReaders(uint first, uint second, bool different) {
		for (int offset = 0; offset < 32; offset++) {
			byte[] data1 = new byte[BufferBytes];
			byte[] data2 = new byte[BufferBytes];

			bf_write writer1 = new(data1, data1.Length);
			writer1.Seek(offset);
			writer1.WriteUBitLong(first, 32);
			bf_write writer2 = new(data2, data2.Length);
			writer2.Seek(offset);
			writer2.WriteUBitLong(second, 32);

			bf_read reader1 = new(data1, data1.Length);
			bf_read reader2 = new(data2, data2.Length);
			reader1.Seek(offset);
			reader2.Seek(offset);

			Assert.Equal(different, reader1.CompareBits(reader2, 32));
			Assert.Equal(offset + 32, reader1.BitsRead);
			Assert.Equal(offset + 32, reader2.BitsRead);
		}
	}

	[Theory]
	[InlineData(0, 1)]
	[InlineData(3, 7)]
	[InlineData(13, 19)]
	[InlineData(29, 9)]
	[InlineData(31, 31)]
	public void WritesOnlyTouchTheRequestedBits(int offset, int bits) {
		Action<bf_write>[] writes = [
			w => w.WriteUBitLong(uint.MaxValue, bits, false),
			w => w.WriteSBitLong(-1, bits),
			w => w.WriteBits(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, bits)
		];

		foreach (var write in writes) {
			byte[] data = new byte[BufferBytes];
			bf_write writer = new(data, data.Length);
			writer.Seek(offset);
			write(writer);

			Assert.Equal(offset + bits, writer.BitsWritten);
			Assert.Equal(bits, SetBits(data));
			bf_read reader = new(data, data.Length);
			reader.Seek(offset);
			Assert.Equal(Mask(bits), reader.ReadUBitLong(bits));
		}
	}

	[Fact]
	public void NamedReaderConstructorDefaultsToTheWholeBuffer() {
		byte[] data = [0x78, 0x56, 0x34, 0x12, 0, 0, 0, 0];
		bf_read reader = new("named", data, data.Length);

		Assert.Equal((nint)64, reader.MaxBits);
		Assert.Equal(0x12345678u, reader.ReadUBitLong(32));
		Assert.False(reader.Overflowed);
	}

	[Theory]
	[InlineData(0x0123456789ABCDEFL)]
	[InlineData(-1L)]
	[InlineData(long.MinValue)]
	public void ReadLongLongReadsTwoDwords(long value) {
		byte[] data = new byte[BufferBytes];
		bf_write writer = new(data, data.Length);
		writer.Seek(3);
		writer.WriteLongLong(value);

		bf_read reader = new(data, data.Length);
		reader.Seek(3);
		Assert.Equal(value, reader.ReadLongLong());
		Assert.Equal(67, reader.BitsRead);
		Assert.False(reader.Overflowed);
	}

	[Fact]
	public void TruncatedStringReadsSkipTheRestOfTheString() {
		bf_read reader = StringThenMarker("hello world");
		Assert.False(reader.ReadString(out string? str, 5));
		Assert.Equal("hell", str);
		Assert.Equal(Marker, reader.ReadByte());

		reader = StringThenMarker("hello world");
		Assert.Equal("hell", reader.ReadString(5));
		Assert.Equal(Marker, reader.ReadByte());

		reader = StringThenMarker("hello world");
		char[] chars = new char[5];
		Assert.Equal(4, reader.ReadString(chars.AsSpan()));
		Assert.Equal("hell\0", new string(chars));
		Assert.Equal(Marker, reader.ReadByte());

		reader = StringThenMarker("hello world");
		byte[] bytes = new byte[5];
		Assert.Equal(5, reader.ReadString(bytes.AsSpan()));
		Assert.Equal("hello", Encoding.ASCII.GetString(bytes));
		Assert.Equal(Marker, reader.ReadByte());
	}

	[Theory]
	[InlineData(1f)]
	[InlineData(-0.5f)]
	[InlineData(-3.5f)]
	[InlineData(16384f)]
	public void ReadBitCoordBitsReadsTheWholeCoord(float value) {
		byte[] data = new byte[BufferBytes];
		bf_write writer = new(data, data.Length);
		writer.WriteBitCoord(value);

		bf_read raw = new(data, data.Length);
		uint expected = raw.ReadUBitLong(writer.BitsWritten);
		bf_read reader = new(data, data.Length);
		Assert.Equal(expected, reader.ReadBitCoordBits());
		Assert.Equal(writer.BitsWritten, reader.BitsRead);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(int.MinValue)]
	public void SingleBitWritesTreatNegativeValuesAsSet(int value) {
		byte[] data = new byte[4];
		bf_write writer = new(data, data.Length);
		writer.WriteOneBit(value);
		writer.WriteOneBitNoCheck(value);
		writer.WriteOneBitAt(5, value);

		Assert.Equal(0x23, data[0]);
	}

	[Fact]
	public void WriteStringReturnsWhetherItFit() {
		byte[] data = new byte[8];
		Assert.True(new bf_write(data, data.Length).WriteString("hello"));
		Assert.False(new bf_write(data, 4).WriteString("hello"));
	}

	[Theory]
	[InlineData("é", "E900")]
	[InlineData("a´ÿ", "61B4FF00")]
	[InlineData("\0abc", "00")]
	public void WriteStringWritesBytesLikeGMod(string text, string expected) {
		byte[] data = new byte[BufferBytes];
		bf_write writer = new(data, data.Length);
		writer.WriteString(text);
		Assert.Equal(expected, Written(writer, data));
	}

	[Fact]
	public void StringReadsDoNotSignExtendHighBytes() {
		byte[] data = [0x61, 0xB4, 0xFF, 0x00];
		char[] chars = new char[8];
		Assert.True(new bf_read(data, data.Length).ReadString(chars.AsSpan(), false, out int count));
		Assert.Equal("a´ÿ", new string(chars, 0, count));
		Assert.Equal("a´ÿ", new bf_read(data, data.Length).ReadAndAllocateString());
	}

	[Theory]
	[InlineData(5u, 0x3B201403u)]
	[InlineData(320u, 0x3E201403u)]
	[InlineData(1280u, 0x3F201403u)]
	public void ReadBitNormalRoundsLikeGMod(uint fraction, uint expectedBits) {
		byte[] data = new byte[BufferBytes];
		bf_write writer = new(data, data.Length);
		writer.WriteOneBit(0);
		writer.WriteUBitLong(fraction, NORMAL_FRACTIONAL_BITS);

		float value = new bf_read(data, data.Length).ReadBitNormal();
		Assert.Equal(expectedBits, BitConverter.SingleToUInt32Bits(value));
	}

	[Fact]
	public void WriteBitNormalKeepsThePositiveSignAtNegativeFloatResolution() {
		byte[] data = new byte[BufferBytes];
		bf_write writer = new(data, data.Length);
		writer.WriteBitNormal(-(1f / NORMAL_DENOMINATOR));
		Assert.Equal("0200", Written(writer, data));
	}

	[Theory]
	[InlineData(float.NaN, "FDFF01")]
	[InlineData(float.NegativeInfinity, "FDFF01")]
	[InlineData(3e9f, "F9FF01")]
	public void WriteBitCoordEncodesOutOfRangeValuesLikeGMod(float value, string expected) {
		byte[] data = new byte[BufferBytes];
		bf_write writer = new(data, data.Length);
		writer.WriteBitCoord(value);
		Assert.Equal(expected, Written(writer, data));
	}

	[Theory]
	[InlineData(float.NaN, false, false, "FFFF07")]
	[InlineData(float.NegativeInfinity, true, true, "FF3F")]
	[InlineData(3e9f, false, true, "FBFF01")]
	public void WriteBitCoordMPEncodesOutOfRangeValuesLikeGMod(float value, bool integral, bool lowPrecision, string expected) {
		byte[] data = new byte[BufferBytes];
		bf_write writer = new(data, data.Length);
		writer.WriteBitCoordMP(value, integral, lowPrecision);
		Assert.Equal(expected, Written(writer, data));
	}

	[Theory]
	[InlineData(float.NaN, "FF0F")]
	[InlineData(float.NegativeInfinity, "FF0F")]
	[InlineData(-1e10f, "FF0F")]
	public void WriteBitNormalEncodesOutOfRangeValuesLikeGMod(float value, string expected) {
		byte[] data = new byte[BufferBytes];
		bf_write writer = new(data, data.Length);
		writer.WriteBitNormal(value);
		Assert.Equal(expected, Written(writer, data));
	}

	[Fact]
	public void WriteBitVec3CoordEncodesOutOfRangeValuesLikeGMod() {
		byte[] data = new byte[BufferBytes];
		bf_write writer = new(data, data.Length);
		writer.WriteBitVec3Coord(new Vector3(float.NaN, float.NegativeInfinity, 1e10f));
		Assert.Equal(54, writer.BitsWritten);
		Assert.Equal("EFFFDFFF3FFF3F", Written(writer, data));
	}

	[Theory]
	[InlineData(-258.23f, 31, 607081600u)]
	[InlineData(558.71f, 28, 148168928u)]
	[InlineData(-604.07f, 21, 675341u)]
	[InlineData(678.54f, 31, 0u)]
	public void WriteBitAngleEncodesLikeGMod(float angle, int bits, uint expected) {
		byte[] data = new byte[BufferBytes];
		bf_write writer = new(data, data.Length);
		writer.WriteBitAngle(angle, bits);
		Assert.Equal(expected, new bf_read(data, data.Length).ReadUBitLong(bits));
	}

	[Fact]
	public void ReadBitsAlignsTheOutputBeforeReadingDwords() {
		byte[] data = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0, 0, 0, 0];
		bf_read reader = new(data, 8);
		reader.Seek(4);
		byte[] output = new byte[12];
		Array.Fill(output, (byte)0xCC);

		reader.ReadBits(output.AsSpan(1), 72);

		Assert.True(reader.Overflowed);
		Assert.Equal(64, reader.BitsRead);
		Assert.Equal("CC213243546576870000CCCC", Convert.ToHexString(output));
	}
}
