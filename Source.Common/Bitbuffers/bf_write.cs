
using Source.Common.Mathematics;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Source.Common.Bitbuffers;


public unsafe class bf_write : BitBuffer
{
	protected byte[]? data;
	public int BytesWritten => BitByte(curBit);
	public int BitsWritten => curBit; // should be nint

	// Constructors

	public override byte[]? BaseArray => data;

	public bf_write() {
		data = null;
		dataBytes = 0;
		dataBits = -1; // -1 generates an overflow on all operations
		curBit = 0;
		overflow = false;
	}

	public bf_write Copy() {
		bf_write copy = new bf_write();
		CopyTo(copy);
		return copy;
	}

	/// <summary>
	/// Copies the state to another <see cref="bf_write"/> instance.
	/// <br/>
	/// <b>Important: </b> This does NOT make a copy of the internal data the bitbuffer is writing to! It merely copies the state.
	/// </summary>
	public void CopyTo(bf_write other) {
		other.data = data;
		other.dataBits = dataBits;
		other.dataBytes = dataBytes;
		other.curBit = curBit;
		other.overflow = overflow;
	}

	/// <summary>
	/// Copies the state from another <see cref="bf_write"/> instance. 
	/// <br/>
	/// <b>Important: </b> This does NOT make a copy of the internal data the bitbuffer is writing to! It merely copies the state.
	/// </summary>
	public void CopyFrom(bf_write other) {
		data = other.data;
		dataBits = other.dataBits;
		dataBytes = other.dataBytes;
		curBit = other.curBit;
		overflow = other.overflow;
	}

	public bf_write(byte[]? data, int bytes) {
		StartWriting(data, bytes, 0, -1);
	}

	public bf_write(byte[]? data, int bytes, int bits) {
		StartWriting(data, bytes, 0, bits);
	}

	public unsafe void StartWriting(byte[]? inData, nuint bytes, int startBit = 0, int bits = -1)
		=> StartWriting(inData, (int)bytes, startBit, bits);
	public unsafe void StartWriting(byte[]? inData, int bytes, int startBit = 0, int bits = -1) {
		// Ensure d-word alignment
		Debug.Assert(bytes % 4 == 0);

		// Truncate to force alignment
		bytes &= ~3;

		data = inData;
		dataBytes = bytes;

		if (bits == -1)
			dataBits = bytes << 3;
		else {
			Debug.Assert(bits <= bytes * 8);
			dataBits = bits;
		}

		curBit = startBit;
		overflow = false;
	}


	// Methods

	public void Reset() {
		curBit = 0;
		overflow = false;
	}

	public void Seek(int bit) {
		curBit = bit;
	}

	public void WriteSBitLong(int data, int numbits) {
#if NEW_BITBUFFER
		WriteUBitLong((uint)data, numbits, false);
#else
		int nValue = data;
		int nPreserveBits = (0x7FFFFFFF >> (32 - numbits));
		int nSignExtension = (nValue >> 31) & ~nPreserveBits;
		nValue &= nPreserveBits;
		nValue |= nSignExtension;

		WriteUBitLong((uint)nValue, numbits, false);
#endif
	}

	public void WriteUBitVar(uint data) {
		if (data < 0x10u)
			WriteUBitLong((data << 2) | 0, 6);
		else if (data < 0x100u)
			WriteUBitLong((data << 2) | 1, 10);
		else if (data < 0x1000u)
			WriteUBitLong((data << 2) | 2, 14);
		else {
			WriteUBitLong(3, 2);
			WriteUBitLong(data, 32);
		}
	}

	public void WriteVarInt32(uint data) {
		if ((curBit & 7) == 0 && curBit + (nint)MaxVarInt32Bytes * 8 <= dataBits) {
			fixed (byte* pBuf = this.data) {
				byte* target = pBuf + (curBit >> 3);

				target[0] = (byte)(data | 0x80);
				if (data >= 1 << 7) {
					target[1] = (byte)(data >> 7 | 0x80);
					if (data >= 1 << 14) {
						target[2] = (byte)(data >> 14 | 0x80);
						if (data >= 1 << 21) {
							target[3] = (byte)(data >> 21 | 0x80);
							if (data >= 1 << 28) {
								target[4] = (byte)(data >> 28);
								curBit += 5 * 8;
								return;
							}
							else {
								target[3] &= 0x7F;
								curBit += 4 * 8;
								return;
							}
						}
						else {
							target[2] &= 0x7F;
							curBit += 3 * 8;
							return;
						}
					}
					else {
						target[1] &= 0x7F;
						curBit += 2 * 8;
						return;
					}
				}
				else {
					target[0] &= 0x7F;
					curBit += 1 * 8;
					return;
				}
			}
		}
		else // Slow path
		{
			while (data > 0x7Fu) {
				WriteUBitLong(data & 0x7Fu | 0x80, 8);
				data >>= 7;
			}
			WriteUBitLong(data & 0x7Fu, 8);
		}
	}

	public void WriteVarInt64(ulong data) {
		// Check if align and we have room, slow path if not
		fixed (byte* dataPtr = this.data) {
			if ((curBit & 7) == 0 && curBit + MaxVarintBytes * 8 <= dataBits) {
				byte* target = dataPtr + (curBit >> 3);

				// Splitting into 32-bit pieces gives better performance on 32-bit
				// processors.
				uint part0 = (uint)data;
				uint part1 = (uint)(data >> 28);
				uint part2 = (uint)(data >> 56);
				int size;

				// Here we can't really optimize for small numbers, since the data is
				// split into three parts.  Cheking for numbers < 128, for instance,
				// would require three comparisons, since you'd have to make sure part1
				// and part2 are zero.  However, if the caller is using 64-bit integers,
				// it is likely that they expect the numbers to often be very large, so
				// we probably don't want to optimize for small numbers anyway.  Thus,
				// we end up with a hardcoded binary search tree...
				if (part2 == 0) {
					if (part1 == 0) {
						if (part0 < 1 << 14) {
							if (part0 < 1 << 7) {
								size = 1; goto size1;
							}
							else {
								size = 2; goto size2;
							}
						}
						else {
							if (part0 < 1 << 21) {
								size = 3; goto size3;
							}
							else {
								size = 4; goto size4;
							}
						}
					}
					else {
						if (part1 < 1 << 14) {
							if (part1 < 1 << 7) {
								size = 5; goto size5;
							}
							else {
								size = 6; goto size6;
							}
						}
						else {
							if (part1 < 1 << 21) {
								size = 7; goto size7;
							}
							else {
								size = 8; goto size8;
							}
						}
					}
				}
				else {
					if (part2 < 1 << 7) {
						size = 9; goto size9;
					}
					else {
						size = 10; goto size10;
					}
				}

			size10: target[9] = (byte)(part2 >> 7 | 0x80);
			size9: target[8] = (byte)(part2 | 0x80);
			size8: target[7] = (byte)(part1 >> 21 | 0x80);
			size7: target[6] = (byte)(part1 >> 14 | 0x80);
			size6: target[5] = (byte)(part1 >> 7 | 0x80);
			size5: target[4] = (byte)(part1 | 0x80);
			size4: target[3] = (byte)(part0 >> 21 | 0x80);
			size3: target[2] = (byte)(part0 >> 14 | 0x80);
			size2: target[1] = (byte)(part0 >> 7 | 0x80);
			size1: target[0] = (byte)(part0 | 0x80);

				target[size - 1] &= 0x7F;
				curBit += size * 8;
			}
			else // slow path
			{
				while (data > 0x7F) {
					WriteUBitLong((uint)(data & 0x7F | 0x80), 8);
					data >>= 7;
				}
				WriteUBitLong((uint)(data & 0x7F), 8);
			}
		}
	}

	public void WriteSignedVarInt32(int data) {
		WriteVarInt32(ZigZagEncode32(data));
	}

	public void WriteSignedVarInt64(long data) {
		WriteVarInt64(ZigZagEncode64(data));
	}


	public int ByteSizeVarInt32(uint data) {
		int size = 1;
		while (data > 0x7F) {
			size++;
			data >>= 7;
		}
		return size;
	}

	public int ByteSizeVarInt64(ulong data) {
		int size = 1;
		while (data > 0x7F) {
			size++;
			data >>= 7;
		}
		return size;
	}

	public int ByteSizeSignedVarInt32(int data) {
		return ByteSizeVarInt32(ZigZagEncode32(data));
	}

	public int ByteSizeSignedVarInt64(long data) {
		return ByteSizeVarInt64(ZigZagEncode64(data));
	}

	public void WriteBitLong(uint data, int numbits, bool signed) {
		if (signed)
			WriteSBitLong((int)data, numbits);
		else
			WriteUBitLong(data, numbits);
	}

	public void WriteOneBitNoCheck(int bit) {
		fixed (byte* dataB = data) {
			uint* data = (uint*)dataB;
			if (bit != 0)
				data[curBit >> 5] |= g_LittleBits[curBit & 31];
			else
				data[curBit >> 5] &= ~g_LittleBits[curBit & 31];
		}
		++curBit;
	}
	private void memcpy(void* dst, void* src, int len) => NativeMemory.Copy(src, dst, (nuint)len);
	private void memcpy(void* dst, void* src, nuint len) => NativeMemory.Copy(src, dst, len);
	public bool WriteBits(ReadOnlySpan<byte> pInData, int bits) {
		fixed (byte* ptr = pInData)
			return WriteBits(ptr, bits);
	}
	public bool WriteBits(void* pInData, int nBits) {
		byte* pOut = (byte*)pInData;
		int nBitsLeft = nBits;

		// Bounds checking..
		if (curBit + nBits > dataBits) {
			TriggerOverflow();
			return false;
		}

		if (nBitsLeft >= 32 && (curBit & 7) == 0) {
			// current bit is byte aligned, do block copy
			int numbytes = nBitsLeft >> 3;
			int numbits = numbytes << 3;
			fixed (byte* ptr = data)
				memcpy(ptr + (curBit >> 3), pOut, numbytes);
			pOut += numbytes;
			nBitsLeft -= numbits;
			curBit += numbits;
		}

		// X360TBD: Can't write dwords in WriteBits because they'll get swapped
		if (nBitsLeft >= 32) {
			uint iBitsRight = (uint)(curBit & 31);
			uint iBitsLeft = 32 - iBitsRight;
			uint bitMaskLeft = g_BitWriteMasks[iBitsRight, 32];
			uint bitMaskRight = g_BitWriteMasks[0, iBitsRight];
			fixed (byte* ptr = data) {
				uint* uPtr = (uint*)ptr;
				uint* pData = &uPtr[curBit >> 5];

				// Read dwords.
				while (nBitsLeft >= 32) {
					uint curData = *(uint*)pOut;
					pOut += sizeof(uint);

					*pData &= bitMaskLeft;
					*pData |= curData << (int)iBitsRight;

					pData++;

					if (iBitsLeft < 32) {
						curData >>= (int)iBitsLeft;
						*pData &= bitMaskRight;
						*pData |= curData;
					}

					nBitsLeft -= 32;
					curBit += 32;
				}
			}
		}


		// write remaining bytes
		while (nBitsLeft >= 8) {
			WriteUBitLong(*pOut, 8, false);
			++pOut;
			nBitsLeft -= 8;
		}

		// write remaining bits
		if (nBitsLeft > 0) {
			WriteUBitLong(*pOut, nBitsLeft, false);
		}

		return !Overflowed;
	}


	public bool WriteBitsFromBuffer(bf_read pIn, int nBits) {
		while (nBits > 32) {
			WriteUBitLong(pIn.ReadUBitLong(32), 32);
			nBits -= 32;
		}

		WriteUBitLong(pIn.ReadUBitLong(nBits), nBits);
		return !Overflowed && !pIn.Overflowed;
	}

	/// <summary>
	/// Shortcut to <see cref="WriteOneBit(int)"/>, where <see cref="int"/> == b ? 1 : 0
	/// </summary>
	/// <param name="b"></param>
	public void WriteBool(bool b) => WriteOneBit(b ? 1 : 0);

	public void WriteBitAngle(float fAngle, int numbits) {
		int d;
		int mask;
		int shift;

		shift = (int)BitForBitnum(numbits);
		mask = shift - 1;

		d = (int)(fAngle / 360.0 * shift);
		d &= mask;

		WriteUBitLong((uint)d, numbits);
	}

	public void WriteBitCoordMP(float f, bool bIntegral, bool bLowPrecision) {
		int signbit = MathLib.NotGreater(f, -(bLowPrecision ? COORD_RESOLUTION_LOWPRECISION : COORD_RESOLUTION)) ? 1 : 0;
		int intval = MathLib.TruncateOrMinValue(MathF.Abs(f));
		int fractval = bLowPrecision ?
			MathLib.AbsWrapping(MathLib.TruncateOrMinValue(f * COORD_DENOMINATOR_LOWPRECISION)) & COORD_DENOMINATOR_LOWPRECISION - 1 :
			MathLib.AbsWrapping(MathLib.TruncateOrMinValue(f * COORD_DENOMINATOR)) & COORD_DENOMINATOR - 1;

		int bInBounds = intval < 1 << (int)COORD_INTEGER_BITS_MP ? 1 : 0;

		uint bits, numbits;

		if (bIntegral) {
			// Integer encoding: in-bounds bit, nonzero bit, optional sign bit + integer value bits
			if (intval != 0) {
				// Adjust the integers from [1..MAX_COORD_VALUE] to [0..MAX_COORD_VALUE-1]
				--intval;
				bits = (uint)(intval * 8 + signbit * 4 + 2 + bInBounds);
				numbits = 3 + (bInBounds > 0 ? COORD_INTEGER_BITS_MP : COORD_INTEGER_BITS);
			}
			else {
				bits = (uint)bInBounds;
				numbits = 2;
			}
		}
		else {
			// Float encoding: in-bounds bit, integer bit, sign bit, fraction value bits, optional integer value bits
			if (intval != 0) {
				// Adjust the integers from [1..MAX_COORD_VALUE] to [0..MAX_COORD_VALUE-1]
				--intval;
				bits = (uint)(intval * 8 + signbit * 4 + 2 + bInBounds);
				bits += (uint)(bInBounds > 0 ? fractval << 3 + (int)COORD_INTEGER_BITS_MP : fractval << 3 + (int)COORD_INTEGER_BITS);
				numbits = 3 + (bInBounds > 0 ? COORD_INTEGER_BITS_MP : COORD_INTEGER_BITS)
					+ (bLowPrecision ? COORD_FRACTIONAL_BITS_MP_LOWPRECISION : COORD_FRACTIONAL_BITS);
			}
			else {
				bits = (uint)(fractval * 8 + signbit * 4 + 0 + bInBounds);
				numbits = 3 + (bLowPrecision ? COORD_FRACTIONAL_BITS_MP_LOWPRECISION : COORD_FRACTIONAL_BITS);
			}
		}

		WriteUBitLong(bits, (int)numbits);
	}

	public void WriteBitCoord(float f) {
		int signbit = MathLib.NotGreater(f, -COORD_RESOLUTION) ? 1 : 0;
		int intval = MathLib.TruncateOrMinValue(MathF.Abs(f));
		int fractval = MathLib.AbsWrapping(MathLib.TruncateOrMinValue(f * COORD_DENOMINATOR)) & COORD_DENOMINATOR - 1;


		// Send the bit flags that indicate whether we have an integer part and/or a fraction part.
		WriteOneBit(intval);
		WriteOneBit(fractval);

		if (intval != 0 || fractval != 0) {
			// Send the sign bit
			WriteOneBit(signbit);

			// Send the integer if we have one.
			if (intval != 0) {
				// Adjust the integers from [1..MAX_COORD_VALUE] to [0..MAX_COORD_VALUE-1]
				intval--;
				WriteUBitLong((uint)intval, (int)COORD_INTEGER_BITS);
			}

			// Send the fraction if we have one
			if (fractval != 0) {
				WriteUBitLong((uint)fractval, (int)COORD_FRACTIONAL_BITS);
			}
		}
	}

	public void WriteBitVec3Coord(Vector3 fa) {
		int xflag, yflag, zflag;

		xflag = MathLib.NotLess(fa[0], COORD_RESOLUTION) || MathLib.NotGreater(fa[0], -COORD_RESOLUTION) ? 1 : 0;
		yflag = MathLib.NotLess(fa[1], COORD_RESOLUTION) || MathLib.NotGreater(fa[1], -COORD_RESOLUTION) ? 1 : 0;
		zflag = MathLib.NotLess(fa[2], COORD_RESOLUTION) || MathLib.NotGreater(fa[2], -COORD_RESOLUTION) ? 1 : 0;

		WriteOneBit(xflag);
		WriteOneBit(yflag);
		WriteOneBit(zflag);

		if (xflag > 0)
			WriteBitCoord(fa[0]);
		if (yflag > 0)
			WriteBitCoord(fa[1]);
		if (zflag > 0)
			WriteBitCoord(fa[2]);
	}

	public void WriteBitNormal(float f) {
		int signbit = MathLib.NotGreater(f, -NORMAL_RESOLUTION) ? 1 : 0;

		// NOTE: Since +/-1 are valid values for a normal, I'm going to encode that as all ones
		uint fractval = (uint)MathLib.AbsWrapping(MathLib.TruncateOrMinValue(f * NORMAL_DENOMINATOR));

		// clamp..
		if (fractval > NORMAL_DENOMINATOR)
			fractval = NORMAL_DENOMINATOR;

		// Send the sign bit
		WriteOneBit(signbit);

		// Send the fractional component
		WriteUBitLong(fractval, NORMAL_FRACTIONAL_BITS);
	}

	public void WriteBitVec3Normal(Vector3 fa) {
		int xflag, yflag;

		xflag = fa[0] >= NORMAL_RESOLUTION || fa[0] <= -NORMAL_RESOLUTION ? 1 : 0;
		yflag = fa[1] >= NORMAL_RESOLUTION || fa[1] <= -NORMAL_RESOLUTION ? 1 : 0;

		WriteOneBit(xflag);
		WriteOneBit(yflag);

		if (xflag > 0)
			WriteBitNormal(fa[0]);
		if (yflag > 0)
			WriteBitNormal(fa[1]);

		// Write z sign bit
		int signbit = fa[2] <= -NORMAL_RESOLUTION ? 1 : 0;
		WriteOneBit(signbit);
	}

	public void WriteBitAngles(QAngle fa) {
		Vector3 tmp = new(fa.X, fa.Y, fa.Z);
		WriteBitVec3Coord(tmp);
	}

	public void WriteChar(int val) {
		WriteSBitLong(val, sizeof(sbyte) << 3);
	}

	public void WriteByte(int val) {
		WriteUBitLong((uint)val, sizeof(byte) << 3);
	}

	public void WriteShort(int val) {
		WriteSBitLong(val, sizeof(short) << 3);
	}

	public void WriteWord(int val) {
		WriteUBitLong((uint)val, sizeof(ushort) << 3);
	}

	public void WriteLong(int val) {
		WriteSBitLong(val, 32);
	}

	public void WriteLongLong(long val) {
		uint* pLongs = (uint*)&val;

		// Insert the two DWORDS according to network endian
		short endianIndex = 0x0100;
		byte* idx = (byte*)&endianIndex;
		WriteUBitLong(pLongs[*idx++], sizeof(uint) << 3);
		WriteUBitLong(pLongs[*idx], sizeof(uint) << 3);
	}

	public void WriteFloat(float val) {
		// Pre-swap the float, since WriteBits writes raw data
		// LittleFloat(&val, &val);

		WriteBits(&val, sizeof(float) << 3);
	}

	public bool WriteBytes(void* pBuf, int nBytes) {
		return WriteBits(pBuf, nBytes << 3);
	}
	public bool WriteBytes(byte[] pBuf, int nBytes) {
		fixed (byte* pBufTemp = pBuf) {
			return WriteBits(pBufTemp, nBytes << 3);
		}
	}
	public bool WriteBytes(Span<byte> pBuf) {
		fixed (byte* pBufTemp = pBuf) {
			return WriteBits(pBufTemp, pBuf.Length << 3);
		}
	}


	public bool WriteBytes(ReadOnlySpan<byte> pBuf) {
		fixed (byte* pBufTemp = pBuf) {
			return WriteBits(pBufTemp, pBuf.Length << 3);
		}
	}

	/// <summary>
	/// Writes an ASCII string to the bitbuffer.
	/// </summary>
	/// <param name="str">The string to write to the bitbuffer</param>
	/// <param name="nullTerminate">Terminates the string with a null character. Turn off if you communicate the string length in a different way.</param>
	/// <returns></returns>
	public bool WriteString(ReadOnlySpan<char> str, bool nullTerminate = true, int limit = -1) {
		if (limit >= 0 && str.Length >= limit)
			str = str[..limit];

		if (!str.IsEmpty) {
			var i = 0;
			while (i < str.Length && str[i] != '\0') { // Stop at any null terminator
				WriteByte((byte)str[i]);
				i++;
			}
			if (nullTerminate)
				WriteChar(0); // null terminate
		}
		else {
			if (nullTerminate)
				WriteChar(0);
		}

		return !Overflowed;
	}

	public void WriteOneBit(int bit) {
		if (curBit >= dataBits) {
			TriggerOverflow();
			return;
		}

		WriteOneBitNoCheck(bit);
	}

	public void WriteOneBitAt(int bit, int value) {
		if (bit >= dataBits) {
			TriggerOverflow();
			return;
		}

		fixed (byte* dataB = data) {
			uint* data = (uint*)dataB;
			if (value != 0)
				data[bit >> 5] |= g_LittleBits[bit & 31];
			else
				data[bit >> 5] &= ~g_LittleBits[bit & 31];
		}
	}

	public unsafe void WriteUBitLong(uint curData, int numbits, bool checkRange = true) {
#if NEW_BITBUFFER
		// Bounds checking..
		if (curBit + numbits > dataBits) {
			curBit = dataBits;
			TriggerOverflow();
			return;
		}

		int nBitsLeft = numbits;
		int iCurBit = curBit;

		// Mask in a dword.
		uint iDWord = (uint)(iCurBit >> 5);
		Debug.Assert(iDWord * 4 + sizeof(int) <= (uint)dataBytes);

		fixed (byte* pData = data) {
			uint iCurBitMasked = (uint)(iCurBit & 31u);

			uint dword = LoadLittleDWord((uint*)pData, (int)iDWord);

			dword &= g_BitWriteMasks[iCurBitMasked, nBitsLeft];
			dword |= curData << (int)iCurBitMasked;

			// write to stream (lsb to msb ) properly
			StoreLittleDWord((uint*)pData, (int)iDWord, dword);

			// Did it span a dword?
			int nBitsWritten = (int)(32 - iCurBitMasked);
			if (nBitsWritten < nBitsLeft) {
				nBitsLeft -= nBitsWritten;
				curData >>= nBitsWritten;

				// read from stream (lsb to msb) properly 
				dword = LoadLittleDWord((uint*)pData, (int)(iDWord + 1u));

				dword &= g_BitWriteMasks[0, nBitsLeft];
				dword |= curData;

				// write to stream (lsb to msb) properly 
				StoreLittleDWord((uint*)pData, (int)(iDWord + 1u), dword);
			}

			curBit += numbits;
		}
#else
		if (BitsLeft < numbits) {
			curBit = dataBits;
			TriggerOverflow();
			return;
		}

		int iCurBitMasked = curBit & 31;
		int iDWord = curBit >> 5;
		curBit += numbits;

		// Mask in a dword.
		Debug.Assert((iDWord * 4 + sizeof(uint)) <= (uint)dataBytes);
		fixed (byte* m_bData = data) {
			uint* m_pData = (uint*)m_bData;
			uint* pOut = &m_pData[iDWord];

			// Rotate data into dword alignment
			curData = (curData << iCurBitMasked) | (curData >> (32 - iCurBitMasked));

			// Calculate bitmasks for first and second word
			uint temp = 1u << ((int)(numbits - 1));
			uint mask1 = (temp * 2 - 1) << iCurBitMasked;
			uint mask2 = (temp - 1) >> (31 - iCurBitMasked);

			// Only look beyond current word if necessary (avoid access violation)
			int i = (int)(mask2 & 1u);
			uint dword1 = LoadLittleDWord(pOut, 0);
			uint dword2 = LoadLittleDWord(pOut, i);

			// Drop bits into place
			dword1 ^= (mask1 & (curData ^ dword1));
			dword2 ^= (mask2 & (curData ^ dword2));

			// Note reversed order of writes so that dword1 wins if mask2 == 0 && i == 0
			StoreLittleDWord(pOut, i, dword2);
			StoreLittleDWord(pOut, 0, dword1);
		}
#endif
	}

	public void WriteBitFloat(float val) {
		int intVal = BitConverter.SingleToInt32Bits(val);
		WriteUBitLong((uint)intVal, 32);
	}

	// Some Garry decision it seems?
	public override uint ChecksumXOR() {
		uint checksum = 0x1532c78;
		int bytesWritten = BytesWritten;
		if (bytesWritten != 0) {
			do {
				checksum = ((checksum + data![0] * 0x10u) * 4u ^ 0x1532c60u) * 3u + data![0] * unchecked((uint)-0x44) + unchecked((uint)-0x11);
				bytesWritten--;
			} while (bytesWritten != 0);
		}
		return checksum;
	}
}
