using CommunityToolkit.HighPerformance;

using System.Buffers.Binary;
using System.Runtime.InteropServices;

using FIELD_W_TYPE = System.UInt32;
using FIELD_X_TYPE = System.UInt32;
using FIELD_Y_TYPE = System.UInt32;
using FIELD_Z_TYPE = System.UInt32;
using HASH_ALGORITHM = System.Security.Cryptography.MD5;
using STRUCT_TYPE = Source.Common.Hashing.MD5Value;

namespace Source.Common.Hashing;

[StructLayout(LayoutKind.Sequential, Pack = 1, Size = SIZE_BYTES)]
public readonly record struct MD5Value
{
	public MD5Value(ReadOnlySpan<byte> data) => data[..SIZE_BYTES].CopyTo(ToEditableBytes(ref this));

	public readonly Span<byte> ToBytes(Span<byte> bytes) {
		ToBytes(in this).CopyTo(bytes);
		return bytes;
	}

	public static STRUCT_TYPE FromBytes(ReadOnlySpan<byte> bytes) {
		if (bytes.Length < SIZE_BYTES)
			throw new ArgumentException($"Span must be at least {SIZE_BYTES} bytes.", nameof(bytes));

		return new(bytes);
	}

	public readonly byte[] ToBytes() {
		byte[] result = new byte[SIZE_BYTES];
		ToBytes(result);
		return result;
	}

	public static STRUCT_TYPE Compute(ReadOnlySpan<byte> data) {
		Span<byte> hash = stackalloc byte[SIZE_BYTES];
		HASH_ALGORITHM.HashData(data, hash);
		return FromBytes(hash);
	}

	public static uint PseudoRandom(uint seed) {
		Span<byte> data = stackalloc byte[sizeof(uint)];
		BinaryPrimitives.WriteUInt32LittleEndian(data, seed);
		Span<byte> hash = stackalloc byte[SIZE_BYTES];
		HASH_ALGORITHM.HashData(data, hash);
		return BinaryPrimitives.ReadUInt32LittleEndian(hash[6..]);
	}

	public readonly override string ToString() {
		Span<char> chars = stackalloc char[SIZE_HEX_CHARACTERS];
		ToString(chars);
		return new string(chars);
	}

	public readonly Span<char> ToString(Span<char> chars) {
		ReadOnlySpan<byte> bytes = ToBytes(in this);
		for (int i = 0; i < SIZE_BYTES; i++) {
			byte b = bytes[i];
			chars[i * 2] = GetHexChar(b >> 4);
			chars[i * 2 + 1] = GetHexChar(b & 0xF);
		}
		return chars;
	}

	private static char GetHexChar(int value) => (char)(value < 10 ? '0' + value : 'a' + (value - 10));

	public readonly FIELD_X_TYPE X;
	public readonly FIELD_Y_TYPE Y;
	public readonly FIELD_Z_TYPE Z;
	public readonly FIELD_W_TYPE W;

	public const int SIZEOF_X = sizeof(FIELD_X_TYPE);
	public const int SIZEOF_Y = sizeof(FIELD_Y_TYPE);
	public const int SIZEOF_Z = sizeof(FIELD_Z_TYPE);
	public const int SIZEOF_W = sizeof(FIELD_W_TYPE);

	public const int OFFSET_X = 0;
	public const int OFFSET_Y = SIZEOF_X;
	public const int OFFSET_Z = SIZEOF_X + SIZEOF_Y;
	public const int OFFSET_W = SIZEOF_X + SIZEOF_Y + SIZEOF_W;

	public const int SIZE_BYTES = SIZEOF_X + SIZEOF_Y + SIZEOF_Z + SIZEOF_W;
	public const int SIZE_BITS = SIZE_BYTES * 8;
	public const int SIZE_HEX_CHARACTERS = SIZE_BYTES * 2;

	public static ReadOnlySpan<byte> ToBytes(ref readonly STRUCT_TYPE md5) => new ReadOnlySpan<STRUCT_TYPE>(in md5).Cast<STRUCT_TYPE, byte>();
	public static Span<byte> ToEditableBytes(ref STRUCT_TYPE md5) => new Span<STRUCT_TYPE>(ref md5).Cast<STRUCT_TYPE, byte>();
}
