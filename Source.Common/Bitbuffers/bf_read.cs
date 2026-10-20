using Source.Common.Mathematics;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Source.Common.Bitbuffers;

/// <summary>
/// Bit-buffer reader. Operates on a <see cref="byte[]"/>
/// </summary>
public unsafe class bf_read : BitBuffer
{
	protected byte[]? data;

	/// <summary>
	/// How many bytes have been read so far
	/// </summary>
	public int BytesRead => BitByte(curBit);
	/// <summary>
	/// How many bits have been read so far
	/// </summary>
	public int BitsRead {
		get => curBit;
		set => curBit = value;
	}

	public override byte[]? BaseArray => data;

	public bf_read() {
		data = null;
		dataBytes = 0;
		dataBits = -1;
		curBit = 0;
		overflow = false;
	}

	public bf_read Copy() {
		bf_read copy = new bf_read();
		CopyTo(copy);
		return copy;
	}

	/// <summary>
	/// Copies the state to another <see cref="bf_read"/> instance.
	/// <br/>
	/// <b>Important: </b> This does NOT make a copy of the internal data the bitbuffer is reading from! It merely copies the state.
	/// </summary>
	public void CopyTo(bf_read other) {
		other.data = data;
		other.dataBytes = dataBytes;
		other.dataBits = dataBits;
		other.curBit = curBit;
		other.overflow = overflow;
	}

	/// <summary>
	/// Copies the state from another <see cref="bf_read"/> instance.
	/// <br/>
	/// <b>Important: </b> This does NOT make a copy of the internal data the bitbuffer is reading from! It merely copies the state.
	/// </summary>
	public void CopyFrom(bf_read other) {
		data = other.data;
		dataBytes = other.dataBytes;
		dataBits = other.dataBits;
		curBit = other.curBit;
		overflow = other.overflow;
	}

	public bf_read(byte[]? pData, uint bytes) {
		StartReading(pData, (int)bytes, 0);
	}

	public bf_read(byte[]? pData, int bytes) {
		StartReading(pData, bytes, 0);
	}

	public bf_read(byte[]? pData, int bytes, int bits) {
		StartReading(pData, bytes, 0, bits);
	}

	public bf_read(string name, byte[]? pData, int bytes, int bits = -1) {
		debugName = name;
		StartReading(pData, bytes, 0, bits);
	}


	public void StartReading(byte[]? pData, int bytes, int startBit = 0, int bits = -1) {
		data = pData;
		dataBytes = bytes;

		if (bits == -1)
			dataBits = dataBytes << 3;
		else {
			Debug.Assert(bits <= bytes * 8);
			dataBits = bits;
		}

		curBit = startBit;
		overflow = false;
	}

	public void Reset() {
		curBit = 0;
		overflow = false;
	}

	public uint CheckReadUBitLong(int numbits) {
		// Ok, just read bits out.
		int i, nBitValue;
		uint r = 0;

		for (i = 0; i < numbits; i++) {
			nBitValue = ReadOneBitNoCheck();
			r |= (uint)(nBitValue << i);
		}
		curBit -= numbits;

		return r;
	}

	public void ReadBits(byte[] pOutData, int nBits) => ReadBits(pOutData.AsSpan(), nBits);

	public int ReadBitsClamped(Span<byte> pOut, uint nBits) => ReadBitsClamped_ptr(pOut, (uint)pOut.Length, nBits);

	private int ReadBitsClamped_ptr(Span<byte> pOutData, uint outSizeBytes, uint nBits) {
		uint outSizeBits = outSizeBytes * 8;
		uint readSizeBits = nBits;
		int skippedBits = 0;
		if (readSizeBits > outSizeBits) {
			readSizeBits = outSizeBits;
			skippedBits = (int)(nBits - outSizeBits);
		}

		ReadBits(pOutData, (int)readSizeBits);
		SeekRelative(skippedBits);

		return (int)readSizeBits;
	}

	public void ReadBits(Span<byte> pOutData, int nBits) {
		fixed (byte* pOutRO = pOutData) {
			byte* pOut = pOutRO;
			int nBitsLeft = nBits;

			// read dwords
			while (nBitsLeft >= 32) {
				*(uint*)pOut = ReadUBitLong(32);
				pOut += sizeof(uint);
				nBitsLeft -= 32;
			}

			// read remaining bytes
			while (nBitsLeft >= 8) {
				*pOut = (byte)ReadUBitLong(8);
				++pOut;
				nBitsLeft -= 8;
			}

			// read remaining bits
			if (nBitsLeft > 0) {
				*pOut = (byte)ReadUBitLong(nBitsLeft);
			}
		}
	}

	public float ReadBitAngle(int numbits) {
		float fReturn;
		int i;
		float shift;

		shift = BitForBitnum(numbits);

		i = (int)ReadUBitLong(numbits);
		fReturn = i * (360.0f / shift);

		return fReturn;
	}

	public uint PeekUBitLong(int numbits) {
		uint r;
		int i, nBitValue;
		int nShifts = numbits;

		// Save current state info
		var prevBit = curBit;
		var prevOverflowed = Overflowed;

		r = 0;
		for (i = 0; i < numbits; i++) {
			nBitValue = ReadOneBit();

			// Append to current stream
			if (nBitValue > 0) {
				r |= BitForBitnum(i);
			}
		}

		// Restore state
		curBit = prevBit;
		overflow = prevOverflowed;

		return r;
	}

	public uint ReadUBitLongNoInline(int numbits) {
		return ReadUBitLong(numbits);
	}

	public uint ReadUBitVarInternal(int encodingType) {
		curBit -= 4;
		// int bits = { 4, 8, 12, 32 }[ encodingType ];
		int bits = 4 + encodingType * 4 + (2 - encodingType >> 31 & 16);
		return ReadUBitLong(bits);
	}

	public int ReadSBitLong(int numbits) {
		uint r = ReadUBitLong(numbits);
		uint s = (uint)(1 << numbits - 1);
		if (r >= s)
			r = r - s - s;

		return (int)r;
	}
	public uint ReadVarInt32() {
		uint result = 0;
		int count = 0;
		uint b;

		do {
			if (count == MaxVarInt32Bytes) {
				return result;
			}
			b = ReadUBitLong(8);
			result |= (b & 0x7F) << 7 * count;
			++count;
		} while ((b & 0x80) > 0);

		return result;
	}

	public ulong ReadVarInt64() {
		ulong result = 0;
		int count = 0;
		ulong b;

		do {
			if (count == MaxVarintBytes) {
				return result;
			}
			b = ReadUBitLong(8);
			result |= (b & 0x7F) << 7 * count;
			++count;
		} while ((b & 0x80) > 0);

		return result;
	}

	public int ReadSignedVarInt32() {
		uint value = ReadVarInt32();
		return ZigZagDecode32(value);
	}

	public long ReadSignedVarInt64() {
		ulong value = ReadVarInt64();
		return ZigZagDecode64(value);
	}

	public uint ReadBitLong(int numbits, bool bSigned) {
		if (bSigned)
			return (uint)ReadSBitLong(numbits);
		else
			return ReadUBitLong(numbits);
	}

	public float ReadBitCoord() {
		int intval = 0, fractval = 0, signbit = 0;
		float value = 0.0f;

		// Read the required integer and fraction flags
		intval = ReadOneBit();
		fractval = ReadOneBit();

		// If we got either parse them, otherwise it's a zero.
		if (intval > 0 || fractval > 0) {
			// Read the sign bit
			signbit = ReadOneBit();

			// If there's an integer, read it in
			if (intval > 0) {
				// Adjust the integers from [0..MAX_COORD_VALUE-1] to [1..MAX_COORD_VALUE]
				intval = (int)ReadUBitLong(COORD_INTEGER_BITS) + 1;
			}

			// If there's a fraction, read it in
			if (fractval > 0) {
				fractval = (int)ReadUBitLong(COORD_FRACTIONAL_BITS);
			}

			// Calculate the correct floating point value
			value = intval + fractval * COORD_RESOLUTION;

			// Fixup the sign if negative.
			if (signbit > 0)
				value = -value;
		}

		return value;
	}


	static float[] ReadBitCoordMP_mul_table =
	{
		1f / (1 << (int)COORD_FRACTIONAL_BITS),
		-1f / (1 << (int)COORD_FRACTIONAL_BITS),
		1f / (1 << (int)COORD_FRACTIONAL_BITS_MP_LOWPRECISION),
		-1f / (1 << (int)COORD_FRACTIONAL_BITS_MP_LOWPRECISION)
	};
	static byte[] ReadBitCoordMP_numbits_table =
	{
		(byte)COORD_FRACTIONAL_BITS,
		(byte)COORD_FRACTIONAL_BITS,
		(byte)(COORD_FRACTIONAL_BITS + COORD_INTEGER_BITS),
		(byte)(COORD_FRACTIONAL_BITS + COORD_INTEGER_BITS_MP),
		(byte)COORD_FRACTIONAL_BITS_MP_LOWPRECISION,
		(byte)COORD_FRACTIONAL_BITS_MP_LOWPRECISION,
		(byte)(COORD_FRACTIONAL_BITS_MP_LOWPRECISION + COORD_INTEGER_BITS),
		(byte)(COORD_FRACTIONAL_BITS_MP_LOWPRECISION + COORD_INTEGER_BITS_MP)
	};
	enum ReadBitCoordMPFlags { INBOUNDS = 1, INTVAL = 2, SIGN = 4 }
	public float ReadBitCoordMP(bool bIntegral, bool bLowPrecision) {
		// BitCoordMP float encoding: inbounds bit, integer bit, sign bit, optional int bits, float bits
		// BitCoordMP integer encoding: inbounds bit, integer bit, optional sign bit, optional int bits.
		// int bits are always encoded as (value - 1) since zero is handled by the integer bit

		// With integer-only encoding, the presence of the third bit depends on the second
		ReadBitCoordMPFlags flags = (ReadBitCoordMPFlags)ReadUBitLong(3 - (bIntegral ? 1 : 0));

		if (bIntegral) {
			if ((flags & ReadBitCoordMPFlags.INTVAL) != 0) {
				// Read the third bit and the integer portion together at once
				uint tbits = ReadUBitLong((flags & ReadBitCoordMPFlags.INBOUNDS) != 0 ? COORD_INTEGER_BITS_MP + 1 : COORD_INTEGER_BITS + 1);
				// Remap from [0,N] to [1,N+1]
				int intval = (int)(tbits >> 1) + 1;
				return (tbits & 1) == 1 ? -intval : intval;
			}
			return 0f;
		}

		float multiply = ReadBitCoordMP_mul_table[((flags & ReadBitCoordMPFlags.SIGN) != 0 ? 1 : 0) + (bLowPrecision ? 1u : 0u) * 2];

		uint bits = ReadUBitLong(ReadBitCoordMP_numbits_table[((uint)(flags & (ReadBitCoordMPFlags.INBOUNDS | ReadBitCoordMPFlags.INTVAL))) + (bLowPrecision ? 4u : 0u)]);

		if ((flags & ReadBitCoordMPFlags.INTVAL) != 0) {
			// Shuffle the bits to remap the integer portion from [0,N] to [1,N+1]
			// and then paste in front of the fractional parts so we only need one
			// int-to-float conversion.

			uint fracbitsMP = bits >> (int)COORD_INTEGER_BITS_MP;
			uint fracbits = bits >> (int)COORD_INTEGER_BITS;

			uint intmaskMP = (1 << (int)COORD_INTEGER_BITS_MP) - 1;
			uint intmask = (1 << (int)COORD_INTEGER_BITS) - 1;

			uint selectNotMP = (uint)(((flags & ReadBitCoordMPFlags.INBOUNDS) != 0 ? 1 : 0) - 1);

			fracbits -= fracbitsMP;
			fracbits &= selectNotMP;
			fracbits += fracbitsMP;

			intmask -= intmaskMP;
			intmask &= selectNotMP;
			intmask += intmaskMP;

			uint intpart = (bits & intmask) + 1;
			uint intbitsLow = intpart << (int)COORD_FRACTIONAL_BITS_MP_LOWPRECISION;
			uint intbits = intpart << (int)COORD_FRACTIONAL_BITS;
			uint selectNotLow = (bLowPrecision ? 1u : 0u) - 1;

			intbits -= intbitsLow;
			intbits &= selectNotLow;
			intbits += intbitsLow;

			bits = fracbits | intbits;
		}

		return (int)bits * multiply;
	}

	static int[] ReadBitCoordBits_numbits_table =
		{
		(int)COORD_INTEGER_BITS + 1,
		(int)COORD_FRACTIONAL_BITS + 1,
		(int)COORD_INTEGER_BITS + (int)COORD_FRACTIONAL_BITS + 1
	};
	public uint ReadBitCoordBits() {
		uint flags = ReadUBitLong(2);
		if (flags == 0)
			return 0;

		return ReadUBitLong(ReadBitCoordMP_numbits_table[flags - 1]) * 4 + flags;
	}

	enum ReadBitCoordMPBitsFlags { INBOUNDS = 1, INTVAL = 2 };
	static byte[] ReadBitCoordMPBits_numbits_table =
	{
		(byte)(1 + COORD_FRACTIONAL_BITS),
		(byte)(1 + COORD_FRACTIONAL_BITS),
		(byte)(1 + COORD_FRACTIONAL_BITS + COORD_INTEGER_BITS),
		(byte)(1 + COORD_FRACTIONAL_BITS + COORD_INTEGER_BITS_MP),
		(byte)(1 + COORD_FRACTIONAL_BITS_MP_LOWPRECISION),
		(byte)(1 + COORD_FRACTIONAL_BITS_MP_LOWPRECISION),
		(byte)(1 + COORD_FRACTIONAL_BITS_MP_LOWPRECISION + COORD_INTEGER_BITS),
		(byte)(1 + COORD_FRACTIONAL_BITS_MP_LOWPRECISION + COORD_INTEGER_BITS_MP)
	};

	public sbyte ReadChar() => (sbyte)ReadUBitLong(8);
	public byte ReadByte() => (byte)ReadUBitLong(8);
	public short ReadShort() => (short)ReadUBitLong(16);
	public ushort ReadWord() => (ushort)ReadUBitLong(16);
	public int ReadLong() => (int)ReadUBitLong(32);
	public uint ReadULong() => ReadUBitLong(32);

	public uint ReadBitCoordMPBits(bool bIntegral, bool bLowPrecision) {
		ReadBitCoordMPBitsFlags flags = (ReadBitCoordMPBitsFlags)ReadUBitLong(2);

		int numbits = 0;

		if (bIntegral) {
			if ((flags & ReadBitCoordMPBitsFlags.INTVAL) != 0)
				numbits = (flags & ReadBitCoordMPBitsFlags.INBOUNDS) != 0 ? 1 + (int)COORD_INTEGER_BITS_MP : 1 + (int)COORD_INTEGER_BITS;
			else
				return (uint)flags; // no extra bits
		}
		else
			numbits = ReadBitCoordMPBits_numbits_table[(uint)flags + (bLowPrecision ? 1u : 0u) * 4];

		return (uint)flags + ReadUBitLong(numbits) * 4;
	}

	public void ReadBitVec3Coord(out Vector3 fa) {
		int xflag, yflag, zflag;

		// This vector must be initialized! Otherwise, If any of the flags aren't set, 
		// the corresponding component will not be read and will be stack garbage.
		fa = new(0, 0, 0);

		xflag = ReadOneBit();
		yflag = ReadOneBit();
		zflag = ReadOneBit();

		if (xflag > 0)
			fa[0] = ReadBitCoord();
		if (yflag > 0)
			fa[1] = ReadBitCoord();
		if (zflag > 0)
			fa[2] = ReadBitCoord();
	}
	public Vector3 ReadBitVec3Coord() { ReadBitVec3Coord(out var fa); return fa; }


	public float ReadBitNormal() {
		// Read the sign bit
		int signbit = ReadOneBit();

		// Read the fractional part
		uint fractval = ReadUBitLong(NORMAL_FRACTIONAL_BITS);

		// Calculate the correct floating point value
		float value = fractval * NORMAL_RESOLUTION;

		// Fixup the sign if negative.
		if (signbit > 0)
			value = -value;

		return value;
	}


	public void ReadBitVec3Normal(out Vector3 fa) {
		fa = new(0, 0, 0);

		int xflag = ReadOneBit();
		int yflag = ReadOneBit();

		if (xflag > 0)
			fa[0] = ReadBitNormal();
		else
			fa[0] = 0.0f;

		if (yflag > 0)
			fa[1] = ReadBitNormal();
		else
			fa[1] = 0.0f;

		// The first two imply the third (but not its sign)
		int znegative = ReadOneBit();

		float fafafbfb = fa[0] * fa[0] + fa[1] * fa[1];
		if (fafafbfb < 1.0f)
			fa[2] = MathF.Sqrt(1.0f - fafafbfb);
		else
			fa[2] = 0.0f;

		if (znegative > 0)
			fa[2] = -fa[2];
	}
	public Vector3 ReadBitVec3Normal() { ReadBitVec3Normal(out var fa); return fa; }

	public void ReadBitAngles(out QAngle fa) {
		ReadBitVec3Coord(out Vector3 tmp);
		fa = new(tmp);
	}
	public QAngle ReadBitAngles() {
		ReadBitVec3Coord(out Vector3 tmp);
		QAngle fa = new(tmp);
		return fa;
	}

	public long ReadLongLong() {
		long retval;
		uint* pLongs = (uint*)&retval;

		// Read the two DWORDs according to network endian
		short endianIndex = 0x0100;
		byte* idx = (byte*)&endianIndex;
		pLongs[*idx++] = ReadUBitLong(sizeof(int) << 3);
		pLongs[*idx] = ReadUBitLong(sizeof(int) << 3);

		return retval;
	}

	public float ReadFloat() {
		Span<byte> ret = stackalloc byte[sizeof(float)];
		ReadBits(ret, 32);

		return MemoryMarshal.Cast<byte, float>(ret)[0];
	}

	public bool ReadBytes(byte[] pOut) {
		fixed (byte* pOutRO = pOut)
			return ReadBytes(new Span<byte>(pOutRO, pOut.Length));
	}

	public bool ReadBytes(Span<byte> pOut) {
		ReadBits(pOut, pOut.Length << 3);
		return !Overflowed;
	}

	public unsafe bool ReadInto<T>(out T output) where T : unmanaged {
		output = default;
		Span<byte> data = stackalloc byte[sizeof(T)];
		if (!ReadBytes(data))
			return false;

		// Cast the Span<byte> into output (hate this!)
		fixed (byte* dataPtr = data) {
			// cast byte* to T*
			T* ptr = (T*)dataPtr;
			// dereference cast
			output = *ptr;
		}

		return true;
	}

	public bool ReadString(out string? str, int maxLen, bool bLine = false) {
		Span<byte> buffer = stackalloc byte[maxLen];
		bool fits = ReadString(buffer, bLine, out int chars);
		str = Encoding.ASCII.GetString(buffer[..chars]);
		return fits;
	}
	public bool ReadString(Span<char> str, bool bLine, out int outNumChars) {
		Assert(!str.IsEmpty);
		int maxLen = str.Length;

		bool bTooSmall = false;
		int iChar = 0;
		while (true) {
			char val = (char)ReadChar();
			if (val == 0)
				break;
			else if (bLine && val == '\n')
				break;

			if (iChar < (maxLen - 1)) {
				str[iChar] = val;
				++iChar;
			}
			else {
				bTooSmall = true;
			}
		}

		// Make sure it's null-terminated.
		Assert(iChar < maxLen);
		str[iChar] = '\0';
		outNumChars = iChar;

		return !Overflowed && !bTooSmall;
	}

	public bool ReadString(Span<byte> str, bool bLine, out int outNumChars) {
		Assert(!str.IsEmpty);
		int maxLen = str.Length;

		bool bTooSmall = false;
		int iChar = 0;
		while (true) {
			byte val = ReadByte();
			if (val == 0)
				break;
			else if (bLine && val == '\n')
				break;

			if (iChar < (maxLen - 1)) {
				str[iChar] = val;
				++iChar;
			}
			else {
				bTooSmall = true;
			}
		}

		// Make sure it's null-terminated.
		Assert(iChar < maxLen);
		str[iChar] = 0;
		outNumChars = iChar;

		return !Overflowed && !bTooSmall;
	}

	public string ReadAndAllocateString() => ReadAndAllocateString(out _);
	public string ReadAndAllocateString(out bool overflow){
		Span<char> str = stackalloc char[2048];

		overflow = !ReadString(str, false, out int chars);
		return new string(str[..chars]);
	}

	public int ReadString(Span<char> target, bool bLine = false) {
		if (target.Length < 1) return 0;
		Span<byte> data = stackalloc byte[target.Length];
		ReadString(data, bLine, out int bytes);
		int chars = Encoding.ASCII.GetChars(data[..bytes], target);
		target[chars] = '\0';
		return chars;
	}
	public int ReadString(Span<byte> target, bool bLine = false) {
		Assert(target.Length != 0);
		int i = 0;
		while (!Overflowed) {
			byte val = ReadByte();
			if (val == 0)
				break;
			else if (bLine && val == '\n')
				break;
			if (i < target.Length)
				target[i++] = val;
		}
		return i;
	}
	public string? ReadString(int maxLen, bool bLine = false) {
		if (maxLen == 0)
			return string.Empty;

		Span<byte> writeTarget = stackalloc byte[maxLen];
		ReadString(writeTarget, bLine, out int chars);
		return Encoding.ASCII.GetString(writeTarget[..chars]);
	}

	public void ExciseBits(int startbit, int bitstoremove) {
		int endbit = startbit + bitstoremove;
		int remaining_to_end = dataBits - endbit;

		bf_write temp = new();
		temp.StartWriting(data, dataBits << 3, startbit);

		Seek(endbit);

		for (int i = 0; i < remaining_to_end; i++) {
			temp.WriteOneBit(ReadOneBit());
		}

		Seek(startbit);

		dataBits -= bitstoremove;
		dataBytes = dataBits >> 3;
	}

	// CompareBitsAt?

	public bool Seek(int bit) {
		if (bit < 0 || bit > dataBits) {
			SetOverflowFlag();
			curBit = dataBits;
			return false;
		}

		curBit = bit;
		return true;
	}

	public bool SeekRelative(int bitDelta) => Seek(curBit + bitDelta);

	public int ReadOneBitNoCheck() {
		byte value = (byte)(data![curBit >> 3] >> (curBit & 7));
		++curBit;
		return value & 1;
	}

	public int ReadOneBit() {
		if (BitsLeft <= 0) {
			TriggerOverflow();

			return 0;
		}

		return ReadOneBitNoCheck();
	}

	public bool ReadBool() => ReadOneBit() == 1;

	public float ReadBitFloat() {
		uint bits = ReadUBitLong(32);
		return BitConverter.Int32BitsToSingle((int)bits);
	}

	public uint ReadUBitVar() {
		uint sixbits = ReadUBitLong(6);
		uint encoding = sixbits & 3;
		if (encoding > 0) {
			return ReadUBitVarInternal((int)encoding);
		}
		return sixbits >> 2;
	}

	public uint ReadUBitLong(uint bits) => ReadUBitLong((int)bits); // macro

	public uint ReadUBitLong(int bits) {
		Assert(bits > 0 && bits <= 32);

		if (BitsLeft < bits) {
			curBit = dataBits;
			TriggerOverflow();
			return 0;
		}

		uint iStartBit = (uint)(curBit & 31u);
		nint iLastBit = curBit + bits - 1;
		uint iWordOffset1 = (uint)(curBit >> 5);
		uint iWordOffset2 = (uint)(iLastBit >> 5);

		curBit += bits;

		uint bitmask = g_ExtraMasks[bits];

		fixed (byte* dataB = data) {
			uint* data = (uint*)dataB;
			uint dw1 = LoadLittleDWord(data, (int)iWordOffset1) >> (int)iStartBit;
			uint dw2 = LoadLittleDWord(data, (int)iWordOffset2) << (int)(32 - iStartBit);

			return (dw1 | dw2) & bitmask;
		}
	}

	public bool CompareBits(bf_read other, int bits) {
		return ReadUBitLong(bits) != other.ReadUBitLong(bits);
	}

	public override uint ChecksumXOR() {
		throw new NotImplementedException();
	}
}
