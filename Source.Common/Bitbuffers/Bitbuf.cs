using System.Diagnostics;

namespace Source.Common.Bitbuffers;


using int_ptr = nint;

public abstract class BitHelpers
{
	private static readonly uint[] bitStringEndMasks = new uint[]
	{
		0xffffffffU, 0x00000001U, 0x00000003U, 0x00000007U, 0x0000000fU,
		0x0000001fU, 0x0000003fU, 0x0000007fU, 0x000000ffU, 0x000001ffU,
		0x000003ffU, 0x000007ffU, 0x00000fffU, 0x00001fffU, 0x00003fffU,
		0x00007fffU, 0x0000ffffU, 0x0001ffffU, 0x0003ffffU, 0x0007ffffU,
		0x000fffffU, 0x001fffffU, 0x003fffffU, 0x007fffffU, 0x00ffffffU,
		0x01ffffffU, 0x03ffffffU, 0x07ffffffU, 0x0fffffffU, 0x1fffffffU,
		0x3fffffffU, 0x7fffffffU
	};

	public static uint GetEndMask(int numBits) {
		return bitStringEndMasks[numBits % 32];
	}

	private static readonly uint[] bitsForBitnum = new uint[]
	{
		1U << 0, 1U << 1, 1U << 2, 1U << 3, 1U << 4, 1U << 5, 1U << 6, 1U << 7,
		1U << 8, 1U << 9, 1U << 10, 1U << 11, 1U << 12, 1U << 13, 1U << 14, 1U << 15,
		1U << 16, 1U << 17, 1U << 18, 1U << 19, 1U << 20, 1U << 21, 1U << 22, 1U << 23,
		1U << 24, 1U << 25, 1U << 26, 1U << 27, 1U << 28, 1U << 29, 1U << 30, 1U << 31
	};

	public static uint GetBitForBitnum(int bitNum) {
		return bitsForBitnum[bitNum & 31];
	}

	private static readonly byte[] bitsForBitnumByte = new byte[]
	{
		1 << 0, 1 << 1, 1 << 2, 1 << 3,
		1 << 4, 1 << 5, 1 << 6, 1 << 7
	};

	public static byte GetBitForBitnumByte(int bitNum) {
		return bitsForBitnumByte[bitNum & 7];
	}
}

/// <summary>
/// Todo: Rewrite in pure, safe C#. Need to do further research on how to best approach that...
/// </summary>
public abstract unsafe class BitBuffer
{
	public abstract uint ChecksumXOR();

	public static readonly uint[] g_LittleBits = new uint[32];
	public static readonly uint[,] g_BitWriteMasks = new uint[32, 33];
	public static readonly uint[] g_ExtraMasks = new uint[33];

	public const int MaxVarintBytes = 10;
	public const int MaxVarInt32Bytes = 5;

	public static uint ZigZagEncode32(int n) {
		// Note:  the right-shift must be arithmetic
		return (uint)(n << 1 ^ n >> 31);
	}

	public static int ZigZagDecode32(uint n) {
		return (int)(n >> 1) ^ -(int)(n & 1);
	}

	public static ulong ZigZagEncode64(long n) {
		// Note:  the right-shift must be arithmetic
		return (ulong)(n << 1 ^ n >> 63);
	}

	public static long ZigZagDecode64(ulong n) {
		return (long)(n >> 1) ^ -(long)(n & 1);
	}

	// Initializes masks
	static BitBuffer() {
		for (uint startbit = 0; startbit < 32; startbit++) {
			for (uint nBitsLeft = 0; nBitsLeft < 33; nBitsLeft++) {
				uint endbit = startbit + nBitsLeft;
				g_BitWriteMasks[startbit, nBitsLeft] = BitForBitnum(startbit) - 1;
				if (endbit < 32)
					g_BitWriteMasks[startbit, nBitsLeft] |= ~(BitForBitnum(endbit) - 1);
			}
		}

		for (uint maskBit = 0; maskBit < 32; maskBit++)
			g_ExtraMasks[maskBit] = BitForBitnum(maskBit) - 1;
		g_ExtraMasks[32] = ~0U;

		fixed (uint* littleBitsPtr = g_LittleBits) {
			for (uint littleBit = 0; littleBit < 32; littleBit++)
				StoreLittleDWord(littleBitsPtr, (int)littleBit, 1u << (int)littleBit);
		}
	}

	protected int dataBytes;
	protected int dataBits;
	protected int curBit;
	protected bool overflow;
	protected string? debugName;

	/// <summary>
	/// How many bytes can be read from/written to this bitbuffer.
	/// </summary>
	public int BytesAvailable => dataBytes;

	/// <summary>
	/// Optional debugging name, can be set when creating bitbuffers if somethings a headache
	/// </summary>
	public string DebugName { get => debugName ?? ""; set => debugName = value; }

	public delegate void OverflowedDelegate(BitBuffer bf, string? dbgName);
	public event OverflowedDelegate? OnOverflowed;

	/// <summary>
	/// Triggers an overflow. Sets <see cref="Overflowed"/> to true, calls <see cref="Debug.Assert(bool, string?)"/>, then invokes <see cref="OnOverflowed"/> for error handling
	/// </summary>
	protected void TriggerOverflow() {
		SetOverflowFlag();
		Assert(false, "Overflowed!");
		OnOverflowed?.Invoke(this, debugName);
	}

	/// <summary>
	/// Macro (doesn't even really get used though, I'm just assuming everythings little endian)
	/// </summary>
	/// <param name="val"></param>
	/// <returns></returns>
	public static uint LittleDWord(uint val) => val;
	/// <summary>
	/// Helper method for unsafe uint* loading
	/// </summary>
	/// <param name="baseArray"></param>
	/// <param name="dwordIndex"></param>
	/// <returns></returns>
	public static uint LoadLittleDWord(uint* baseArray, int dwordIndex) {
		return LittleDWord(baseArray[dwordIndex]);
	}
	/// <summary>
	/// Helper method for unsafe uint* storing
	/// </summary>
	/// <param name="baseArray"></param>
	/// <param name="dwordIndex"></param>
	/// <param name="dword"></param>
	public static void StoreLittleDWord(uint* baseArray, int dwordIndex, uint dword) {
		baseArray[dwordIndex] = LittleDWord(dword);
	}

	/// <summary>
	/// The base arrray the bitbuffer is reading
	/// </summary>
	public abstract byte[]? BaseArray { get; }
	public byte[]? GetData() => BaseArray;

	// should go in a diff method
	private static IEnumerable<string> SplitInParts(string s, int partLength) {
		if (s == null)
			throw new ArgumentNullException(nameof(s));
		if (partLength <= 0)
			throw new ArgumentException("Part length has to be positive.", nameof(partLength));

		for (var i = 0; i < s.Length; i += partLength)
			yield return s.Substring(i, Math.Min(partLength, s.Length - i));
	}

	/// <summary>
	/// Dumps the <see cref="BaseArray"/> to a space-separated hexadecimal string for each <see cref="byte"/>. Useful for throwing into a bit-piecewise debugger (I have one if you really need it, just ask)
	/// </summary>
	/// <param name="bytes"></param>
	/// <returns></returns>
	public string DumpHexString(int? bytes = null) {
		if (BaseArray == null) return "<null ptr>";
		string hex = Convert.ToHexString(bytes == null ? BaseArray : BaseArray[..bytes.Value]);

		return string.Join(" ", SplitInParts(hex, 2));
	}

	/// <summary>
	/// Converts bits -> bytes, (<paramref name="bits"/> + 7) >> 3
	/// </summary>
	/// <param name="bits"></param>
	/// <returns></returns>
	public static int BitByte(int bits) => bits + 7 >> 3;

	public static uint BitForBitnum(uint bitnum) => BitHelpers.GetBitForBitnum((int)bitnum);
	public static uint BitForBitnum(int bitnum) => BitHelpers.GetBitForBitnum(bitnum);

	public bool CheckForOverflow(int_ptr bits) {
		if (curBit + bits > dataBits) {
			TriggerOverflow();
		}

		return overflow;
	}
	/// <summary>
	/// Sets <see cref="Overflowed"/> to true
	/// </summary>
	public void SetOverflowFlag() {
		overflow = true;
	}

	/// <summary>
	/// Max bits available.
	/// </summary>
	public int_ptr MaxBits => dataBits;
	/// <summary>
	/// Bits left in the bitbuffer (ie. <see cref="MaxBits"/> - <see cref="curBit"/>)
	/// </summary>
	public int_ptr BitsLeft => dataBits - curBit;
	/// <summary>
	/// Bytes left in the bitbuffer; <see cref="BitsLeft"/> >> 3
	/// </summary>
	public int_ptr BytesLeft => BitsLeft >> 3;
	/// <summary>
	/// Has <see cref="curBit"/> overflowed past the bounds of <see cref="BaseArray"/> 
	/// </summary>
	public bool Overflowed => overflow;
}
