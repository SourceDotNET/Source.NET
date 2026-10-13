using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.GarrysMod;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Source.Engine;

public struct DecodeInfo
{
	public RecvProxyData RecvProxyData;
	public IFieldAccessor FieldInfo;
	public object Object;
	public SendProp Prop;
	public bf_read In;

	public void CopyVars(ref readonly DecodeInfo decodeInfo) {
		Object = decodeInfo.Object;
		FieldInfo = decodeInfo.FieldInfo;
		RecvProxyData.RecvProp = decodeInfo.RecvProxyData.RecvProp;
		Prop = decodeInfo.Prop;
		In = decodeInfo.In;
		RecvProxyData.ObjectID = decodeInfo.RecvProxyData.ObjectID;
		RecvProxyData.Element = decodeInfo.RecvProxyData.Element;
	}
}

public delegate void EncodeFn(object instance, ref DVariant var, SendProp prop, bf_write writeOut, int objectID);
public delegate void DecodeFn(ref DecodeInfo info);
public delegate int CompareDeltasFn(SendProp prop, bf_read p1, bf_read p2);
public delegate void FastCopyFn(SendProp sendProp, RecvProp recvProp, object sendData, IFieldAccessor sendFieldInfo, object recvData, IFieldAccessor recvFieldInfo, int objectID);
public delegate ReadOnlySpan<char> GetTypeNameStringFn();
public delegate bool IsZeroFn(object instance, ref DVariant var, SendProp prop);
public delegate void DecodeZeroFn(ref DecodeInfo info);
public delegate bool IsEncodedZeroFn(SendProp prop, bf_read p);
public delegate void SkipPropFn(SendProp prop, bf_read p);

public struct PropTypeFns
{
	// Field members
	public EncodeFn Encode;
	public DecodeFn Decode;
	public CompareDeltasFn CompareDeltas;
	public FastCopyFn FastCopy;
	public GetTypeNameStringFn GetTypeNameString;
	public IsZeroFn IsZero;
	public DecodeZeroFn DecodeZero;
	public IsEncodedZeroFn IsEncodedZero;
	public SkipPropFn SkipProp;

	public PropTypeFns(EncodeFn encode, DecodeFn decode, CompareDeltasFn compareDeltas, FastCopyFn fastCopy, GetTypeNameStringFn getTypeNameString,
						 IsZeroFn isZero, DecodeZeroFn decodeZero, IsEncodedZeroFn isEncodedZero, SkipPropFn skipProp) {
		Encode = encode;
		Decode = decode;
		CompareDeltas = compareDeltas;
		FastCopy = fastCopy;
		GetTypeNameString = getTypeNameString;
		IsZero = isZero;
		DecodeZero = decodeZero;
		IsEncodedZero = isEncodedZero;
		SkipProp = skipProp;
	}

	public static void EncodeFloat(SendProp prop, float incoming, bf_write outBuffer, int objectID) {
		var flags = prop.GetFlags();
		if ((flags & PropFlags.Coord) != 0)
			outBuffer.WriteBitCoord(incoming);
		else if ((flags & (PropFlags.CoordMP | PropFlags.CoordMPLowPrecision | PropFlags.CoordMPIntegral)) != 0)
			outBuffer.WriteBitCoordMP(incoming, (((int)flags >> 15) & 1) != 0, (((int)flags >> 14) & 1) != 0);
		else if ((flags & PropFlags.Normal) != 0)
			outBuffer.WriteBitNormal(incoming);
		else {
			uint val;
			int bits = prop.Bits;
			if ((flags & PropFlags.NoScale) != 0) {
				val = BitConverter.SingleToUInt32Bits(incoming);
				bits = 32;
			}
			else if (incoming < prop.LowValue) {
				val = 0;

				if ((flags & PropFlags.RoundUp) == 0)
					Warning($"(class {Server.PackedEntities.GetObjectClassName(objectID)}): Out-of-range value ({incoming} < {prop.LowValue}) in SendPropFloat '{prop.GetName()}', clamping.\n");
			}
			else if (incoming > prop.HighValue) {
				val = (uint)((1 << prop.Bits) - 1);

				if ((flags & PropFlags.RoundDown) == 0)
					Warning($"(class {Server.PackedEntities.GetObjectClassName(objectID)}): Out-of-range value ({incoming} > {prop.HighValue}) in SendPropFloat '{prop.GetName()}', clamping.\n");
			}
			else {
				float fRangeVal = (incoming - prop.LowValue) * prop.HighLowMul;
				if (prop.Bits <= 22)
					val = (uint)MathLib.FastFloatToSmallInt(fRangeVal);
				else
					val = MathLib.RoundFloatToUnsignedLong(fRangeVal);
			}
			outBuffer.WriteUBitLong(val, bits);
		}
	}


	internal static float DecodeFloat(SendProp prop, bf_read inBuffer) {
		var flags = prop.GetFlags();
		if ((flags & PropFlags.Coord) != 0)
			return inBuffer.ReadBitCoord();
		else if ((flags & (PropFlags.CoordMP | PropFlags.CoordMPLowPrecision | PropFlags.CoordMPIntegral)) != 0)
			return inBuffer.ReadBitCoordMP((flags & PropFlags.CoordMPIntegral) != 0, (flags & PropFlags.CoordMPLowPrecision) != 0);
		else if ((flags & PropFlags.NoScale) != 0)
			return inBuffer.ReadBitFloat();
		else if ((flags & PropFlags.Normal) != 0)
			return inBuffer.ReadBitNormal();
		else {
			uint dwInterp = inBuffer.ReadUBitLong(prop.Bits);
			float fVal = (float)dwInterp / ((1 << prop.Bits) - 1);
			fVal = prop.LowValue + (prop.HighValue - prop.LowValue) * fVal;
			return fVal;
		}
	}

	public static Vector3 DecodeVector(SendProp prop, bf_read inBuffer) {
		Span<float> stackFloats = stackalloc float[3];
		DecodeVector(prop, inBuffer, stackFloats);
		return new(stackFloats);
	}

	public static void DecodeVector(SendProp prop, bf_read inBuffer, Span<float> v) {
		v[0] = DecodeFloat(prop, inBuffer);
		v[1] = DecodeFloat(prop, inBuffer);

		if ((prop.GetFlags() & PropFlags.Normal) == 0)
			v[2] = DecodeFloat(prop, inBuffer);
		else {
			int signbit = inBuffer.ReadOneBit();

			float v0v0v1v1 = v[0] * v[0] +
				v[1] * v[1];
			if (v0v0v1v1 < 1.0f)
				v[2] = MathF.Sqrt(1.0f - v0v0v1v1);
			else
				v[2] = 0.0f;

			if (signbit != 0)
				v[2] *= -1.0f;
		}
	}

	public static void EncodeVector(SendProp prop, Vector3 incoming, bf_write outBuffer, int objectID) {
		EncodeFloat(prop, incoming[0], outBuffer, objectID);
		EncodeFloat(prop, incoming[1], outBuffer, objectID);

		if ((prop.GetFlags() & PropFlags.Normal) == 0)
			EncodeFloat(prop, incoming[2], outBuffer, objectID);
		else {
			// writing a sign bit for Z instead
			outBuffer.WriteBool(incoming[2] <= -NORMAL_RESOLUTION);
		}
	}

	public static Vector3 DecodeVectorXY(SendProp prop, bf_read inBuffer) {
		Span<float> stackFloats = stackalloc float[3];
		DecodeVectorXY(prop, inBuffer, stackFloats);
		return new(stackFloats);
	}

	public static void DecodeVectorXY(SendProp prop, bf_read inBuffer, Span<float> v) {
		v[0] = DecodeFloat(prop, inBuffer);
		v[1] = DecodeFloat(prop, inBuffer);
	}

	public static void EncodeVectorXY(SendProp prop, Vector3 incoming, bf_write outBuffer, int objectID) {
		EncodeFloat(prop, incoming[0], outBuffer, objectID);
		EncodeFloat(prop, incoming[1], outBuffer, objectID);
	}

	public static string? DecodeString(SendProp prop, bf_read inBuffer) {
		int len = (int)inBuffer.ReadUBitLong(Constants.DT_MAX_STRING_BITS);
		if (len >= Constants.DT_MAX_STRING_BUFFERSIZE) {
			Warning($"String_Decode( {prop.GetName()} ) invalid length ({len})\n");
			len = Constants.DT_MAX_STRING_BUFFERSIZE - 1;
		}

		Span<char> str = stackalloc char[len];
		for (int i = 0; i < len; i++)
			str[i] = (char)inBuffer.ReadByte();
		return new string(str);
	}

	public static void EncodeString(SendProp prop, string? incoming, bf_write outBuffer, int objectID) {
		ReadOnlySpan<char> str = incoming;
		int len = Math.Min(str.Length, Constants.DT_MAX_STRING_BUFFERSIZE - 1);
		if (str.Length > len)
			DevWarning($"String_Encode( {prop.GetName()} ) string too long ({str.Length}), truncated to {len}\n");

		outBuffer.WriteUBitLong((uint)len, Constants.DT_MAX_STRING_BITS);
		for (int i = 0; i < len; i++)
			outBuffer.WriteByte(str[i]);
	}

	/// <summary>
	/// Indices correlate to <see cref="SendPropType"/> enum values.
	/// </summary>
	public static readonly PropTypeFns[] g_PropTypeFns = [
		new(Int_Encode, Int_Decode, Int_CompareDeltas, Generic_FastCopy, Int_GetTypeNameString, Int_IsZero, Int_DecodeZero, Int_IsEncodedZero, Int_SkipProp),
		new(Float_Encode, Float_Decode, Float_CompareDeltas, Generic_FastCopy, Float_GetTypeNameString, Float_IsZero, Float_DecodeZero, Float_IsEncodedZero, Float_SkipProp),
		new(Vector_Encode, Vector_Decode, Vector_CompareDeltas, Generic_FastCopy, Vector_GetTypeNameString, Vector_IsZero, Vector_DecodeZero, Vector_IsEncodedZero, Vector_SkipProp),
		new(VectorXY_Encode, VectorXY_Decode, VectorXY_CompareDeltas, Generic_FastCopy, VectorXY_GetTypeNameString, VectorXY_IsZero, VectorXY_DecodeZero, VectorXY_IsEncodedZero, VectorXY_SkipProp),
		new(String_Encode, String_Decode, String_CompareDeltas, Generic_FastCopy, String_GetTypeNameString, String_IsZero, String_DecodeZero, String_IsEncodedZero, String_SkipProp),
		new(Array_Encode, Array_Decode, Array_CompareDeltas, Array_FastCopy, Array_GetTypeNameString, Array_IsZero, Array_DecodeZero, Array_IsEncodedZero, Array_SkipProp),
		new(DataTable_Encode, DataTable_Decode, DataTable_CompareDeltas, Generic_FastCopy, DataTable_GetTypeNameString, DataTable_IsZero, DataTable_DecodeZero, DataTable_IsEncodedZero, DataTable_SkipProp),
#if GMOD_DLL
		new(GModTable_Encode, GModTable_Decode, GModTable_CompareDeltas, GModTable_FastCopy, GModTable_GetTypeNameString, GModTable_IsZero, GModTable_DecodeZero, GModTable_IsEncodedZero, GModTable_SkipProp),
#endif
	];

	public static ref readonly PropTypeFns Get(SendPropType propType) => ref g_PropTypeFns[(int)propType];

	// Implementations for prop type fns.
	private static void Generic_FastCopy(SendProp sendProp, RecvProp recvProp, object sendData, IFieldAccessor sendFieldInfo, object recvData, IFieldAccessor recvFieldInfo, int objectID) {
		// Get the data out of the ent.
		RecvProxyData recvProxyData;
		recvProxyData.Value = new();

		sendProp.GetProxyFn()(sendProp, sendData, sendFieldInfo, ref recvProxyData.Value, 0, objectID);

		// Fill in the data for the recv proxy.
		recvProxyData.RecvProp = recvProp;
		recvProxyData.Element = 0;
		recvProxyData.ObjectID = objectID;
		recvProp.GetProxyFn()(ref recvProxyData, recvData, recvFieldInfo);
	}

	#region SendPropType.Int
	public static int Int_CompareDeltas(SendProp prop, bf_read p1, bf_read p2) {
		if ((prop.GetFlags() & PropFlags.VarInt) != 0) {
			if ((prop.GetFlags() & PropFlags.Unsigned) != 0)
				return (p1.ReadVarInt32() != p2.ReadVarInt32()) ? 1 : 0;

			return (p1.ReadSignedVarInt32() != p2.ReadSignedVarInt32()) ? 1 : 0;
		}

		return p1.CompareBits(p2, prop.Bits) ? 1 : 0;
	}
	public static void Int_Decode(ref DecodeInfo decodeInfo) {
		SendProp prop = decodeInfo.Prop;
		PropFlags flags = prop.GetFlags();

		if ((flags & PropFlags.VarInt) != 0) {
			if ((flags & PropFlags.Unsigned) != 0)
				decodeInfo.RecvProxyData.Value.Int = (int)decodeInfo.In.ReadVarInt32();
			else
				decodeInfo.RecvProxyData.Value.Int = decodeInfo.In.ReadSignedVarInt32();
		}
		else {
			int bits = prop.Bits;
			decodeInfo.RecvProxyData.Value.Int = (int)decodeInfo.In.ReadUBitLong(bits);

			if (bits != 32 && (flags & PropFlags.Unsigned) == 0) {
				uint highbit = (uint)(1 << (prop.Bits - 1));
				if ((decodeInfo.RecvProxyData.Value.Int & highbit) != 0) {
					decodeInfo.RecvProxyData.Value.Int -= (int)highbit;
					decodeInfo.RecvProxyData.Value.Int -= (int)highbit;
				}
			}
		}

		decodeInfo.RecvProxyData.RecvProp?.GetProxyFn()(ref decodeInfo.RecvProxyData, decodeInfo.Object, decodeInfo.FieldInfo);
	}
	public static void Int_DecodeZero(ref DecodeInfo info) {
		info.RecvProxyData.Value.Int = 0;

		info.RecvProxyData.RecvProp?.GetProxyFn()(ref info.RecvProxyData, info.Object, info.FieldInfo);
	}
	public static void Int_Encode(object instance, ref DVariant var, SendProp prop, bf_write writeOut, int objectID) {
		int value = var.Int;

		if ((prop.GetFlags() & PropFlags.VarInt) != 0) {
			if ((prop.GetFlags() & PropFlags.Unsigned) != 0)
				writeOut.WriteVarInt32((uint)value);
			else
				writeOut.WriteSignedVarInt32(value);
		}
		else {
			int nPreserveBits = (0x7FFFFFFF >> (32 - prop.Bits));
			nPreserveBits |= (int)(((prop.GetFlags() & PropFlags.Unsigned) != 0) ? 0xFFFFFFFF : 0);
			int nSignExtension = (value >> 31) & ~nPreserveBits;

			value &= nPreserveBits;
			value |= nSignExtension;

			writeOut.WriteUBitLong((uint)value, prop.Bits, false);
		}
	}
	public static ReadOnlySpan<char> Int_GetTypeNameString() => "DPT_Int";
	public static bool Int_IsEncodedZero(SendProp prop, bf_read p) {
		if ((prop.GetFlags() & PropFlags.VarInt) != 0) {
			if ((prop.GetFlags() & PropFlags.Unsigned) != 0)
				return p.ReadVarInt32() == 0;

			return p.ReadSignedVarInt32() == 0;
		}

		return p.ReadUBitLong(prop.Bits) == 0;
	}
	public static bool Int_IsZero(object instance, ref DVariant var, SendProp prop) => var.Int == 0;
	public static void Int_SkipProp(SendProp prop, bf_read p) {
		if ((prop.GetFlags() & PropFlags.VarInt) != 0) {
			if ((prop.GetFlags() & PropFlags.Unsigned) != 0)
				p.ReadVarInt32();
			else
				p.ReadSignedVarInt32();
		}
		else {
			p.SeekRelative(prop.Bits);
		}
	}
	#endregion
	#region SendPropType.Float
	public static void Float_SkipProp(SendProp prop, bf_read p) {
		if ((prop.GetFlags() & PropFlags.Coord) != 0) {
			uint val = p.ReadUBitLong(2);
			if (val != 0) {
				int seekDist = 1;

				if ((val & 1) != 0)
					seekDist += (int)COORD_INTEGER_BITS;
				if ((val & 2) != 0)
					seekDist += (int)COORD_FRACTIONAL_BITS;

				p.SeekRelative(seekDist);
			}
		}
		else if ((prop.GetFlags() & PropFlags.CoordMP) != 0)
			p.ReadBitCoordMP(false, false);
		else if ((prop.GetFlags() & PropFlags.CoordMPLowPrecision) != 0)
			p.ReadBitCoordMP(false, true);
		else if ((prop.GetFlags() & PropFlags.CoordMPIntegral) != 0)
			p.ReadBitCoordMP(true, false);
		else if ((prop.GetFlags() & PropFlags.NoScale) != 0)
			p.SeekRelative(32);
		else if ((prop.GetFlags() & PropFlags.Normal) != 0)
			p.SeekRelative(NORMAL_FRACTIONAL_BITS + 1);
		else
			p.SeekRelative(prop.Bits);
	}
	public static void Float_Encode(object instance, ref DVariant var, SendProp prop, bf_write writeOut, int objectID) {
		EncodeFloat(prop, var.Float, writeOut, objectID);
	}
	public static void Float_Decode(ref DecodeInfo decodeInfo) {
		decodeInfo.RecvProxyData.Value.Float = DecodeFloat(decodeInfo.Prop, decodeInfo.In);

		if (decodeInfo.RecvProxyData.RecvProp != null)
			decodeInfo.RecvProxyData.RecvProp.GetProxyFn()(in decodeInfo.RecvProxyData, decodeInfo.Object, decodeInfo.FieldInfo);
	}
	public static bool Float_IsZero(object instance, ref DVariant var, SendProp prop) {
		return var.Float == 0.0f;
	}
	public static void Float_DecodeZero(ref DecodeInfo decodeInfo) {
		decodeInfo.RecvProxyData.Value.Float = 0.0f;

		if (decodeInfo.RecvProxyData.RecvProp != null)
			decodeInfo.RecvProxyData.RecvProp.GetProxyFn()(in decodeInfo.RecvProxyData, decodeInfo.Object, decodeInfo.FieldInfo);
	}
	public static ReadOnlySpan<char> Float_GetTypeNameString() => "DPT_Float";
	public static int Float_CompareDeltas(SendProp prop, bf_read p1, bf_read p2) {
		return (DecodeFloat(prop, p1) != DecodeFloat(prop, p2)) ? 1 : 0;
	}
	public static bool Float_IsEncodedZero(SendProp prop, bf_read p) {
		return DecodeFloat(prop, p) == 0.0f;
	}
	#endregion
	#region SendPropType.Vector
	public static int Vector_CompareDeltas(SendProp prop, bf_read p1, bf_read p2) {
		int c1 = Float_CompareDeltas(prop, p1, p2);
		int c2 = Float_CompareDeltas(prop, p1, p2);

		int c3;
		if ((prop.GetFlags() & PropFlags.Normal) != 0)
			c3 = (p1.ReadOneBit() != p2.ReadOneBit()) ? 1 : 0;
		else
			c3 = Float_CompareDeltas(prop, p1, p2);

		return c1 | c2 | c3;
	}
	public static void Vector_Decode(ref DecodeInfo decodeInfo) {
		Vector3 vec = DecodeVector(decodeInfo.Prop, decodeInfo.In);
		decodeInfo.RecvProxyData.Value.Vector = vec;
		decodeInfo.RecvProxyData.RecvProp?.GetProxyFn()(ref decodeInfo.RecvProxyData, decodeInfo.Object, decodeInfo.FieldInfo);
	}
	public static void Vector_DecodeZero(ref DecodeInfo decodeInfo) {
		decodeInfo.RecvProxyData.Value.Vector = new();
		decodeInfo.RecvProxyData.RecvProp?.GetProxyFn()(ref decodeInfo.RecvProxyData, decodeInfo.Object, decodeInfo.FieldInfo);
	}
	public static void Vector_Encode(object instance, ref DVariant var, SendProp prop, bf_write writeOut, int objectID) {
		EncodeVector(prop, var.Vector, writeOut, objectID);
	}
	public static ReadOnlySpan<char> Vector_GetTypeNameString() => "DPT_Vector";
	public static bool Vector_IsEncodedZero(SendProp prop, bf_read p) {
		Vector3 v = DecodeVector(prop, p);
		return v[0] == 0.0f && v[1] == 0.0f && v[2] == 0.0f;
	}
	public static bool Vector_IsZero(object instance, ref DVariant var, SendProp prop) {
		return var.Vector[0] == 0.0f && var.Vector[1] == 0.0f && var.Vector[2] == 0.0f;
	}
	public static void Vector_SkipProp(SendProp prop, bf_read p) {
		Float_SkipProp(prop, p);
		Float_SkipProp(prop, p);
		if ((prop.GetFlags() & PropFlags.Normal) != 0)
			p.SeekRelative(1);
		else
			Float_SkipProp(prop, p);
	}
	#endregion
	#region SendPropType.VectorXY
	public static int VectorXY_CompareDeltas(SendProp prop, bf_read p1, bf_read p2) {
		int c1 = Float_CompareDeltas(prop, p1, p2);
		int c2 = Float_CompareDeltas(prop, p1, p2);

		return c1 | c2;
	}
	public static void VectorXY_Decode(ref DecodeInfo decodeInfo) {
		Vector3 vec = DecodeVectorXY(decodeInfo.Prop, decodeInfo.In);
		decodeInfo.RecvProxyData.Value.Vector = vec;
		decodeInfo.RecvProxyData.RecvProp?.GetProxyFn()(ref decodeInfo.RecvProxyData, decodeInfo.Object, decodeInfo.FieldInfo);
	}
	public static void VectorXY_DecodeZero(ref DecodeInfo decodeInfo) {
		decodeInfo.RecvProxyData.Value.Vector = new();
		decodeInfo.RecvProxyData.RecvProp?.GetProxyFn()(ref decodeInfo.RecvProxyData, decodeInfo.Object, decodeInfo.FieldInfo);
	}
	public static void VectorXY_Encode(object instance, ref DVariant var, SendProp prop, bf_write writeOut, int objectID) {
		EncodeVectorXY(prop, var.Vector, writeOut, objectID);
	}
	public static ReadOnlySpan<char> VectorXY_GetTypeNameString() => "DPT_VectorXY";
	public static bool VectorXY_IsEncodedZero(SendProp prop, bf_read p) {
		Vector3 vec = DecodeVectorXY(prop, p);
		return vec[0] == 0.0f && vec[1] == 0.0f;
	}
	public static bool VectorXY_IsZero(object instance, ref DVariant var, SendProp prop) => var.Vector[0] == 0.0f && var.Vector[1] == 0.0f;
	public static void VectorXY_SkipProp(SendProp prop, bf_read p) {
		Float_SkipProp(prop, p);
		Float_SkipProp(prop, p);
	}
	#endregion
	#region SendPropType.String
	public static int String_CompareDeltas(SendProp prop, bf_read p1, bf_read p2) {
		int len1 = (int)p1.ReadUBitLong(Constants.DT_MAX_STRING_BITS);
		int len2 = (int)p2.ReadUBitLong(Constants.DT_MAX_STRING_BITS);

		if (len1 == len2) {
			if (len1 == 0)
				return 0;

			return p1.CompareBits(p2, len1 * 8) ? 1 : 0;
		}
		else {
			p1.SeekRelative(len1 * 8);
			p2.SeekRelative(len2 * 8);
			return 1;
		}
	}
	public static void String_Decode(ref DecodeInfo decodeInfo) {
		decodeInfo.RecvProxyData.Value.String = DecodeString(decodeInfo.Prop, decodeInfo.In);
		decodeInfo.RecvProxyData.RecvProp?.GetProxyFn()(ref decodeInfo.RecvProxyData, decodeInfo.Object, decodeInfo.FieldInfo);
	}
	public static void String_DecodeZero(ref DecodeInfo decodeInfo) {
		decodeInfo.RecvProxyData.Value.String = "";
		decodeInfo.RecvProxyData.RecvProp?.GetProxyFn()(ref decodeInfo.RecvProxyData, decodeInfo.Object, decodeInfo.FieldInfo);
	}
	public static void String_Encode(object instance, ref DVariant var, SendProp prop, bf_write writeOut, int objectID) {
		EncodeString(prop, var.String, writeOut, objectID);
	}
	public static ReadOnlySpan<char> String_GetTypeNameString() => "DPT_String";
	public static bool String_IsEncodedZero(SendProp prop, bf_read p) {
		int len = (int)p.ReadUBitLong(Constants.DT_MAX_STRING_BITS);
		p.SeekRelative(len * 8);
		return len == 0;
	}
	public static bool String_IsZero(object instance, ref DVariant var, SendProp prop) => var.String?.Length == 0;
	public static void String_SkipProp(SendProp prop, bf_read p) {
		int len = (int)p.ReadUBitLong(Constants.DT_MAX_STRING_BITS);
		p.SeekRelative(len * 8);
	}
	#endregion
	#region SendPropType.Array
	private static int Array_GetLength(object instance, SendProp prop, int objectID) {
		ArrayLengthSendProxyFn? proxy = prop.GetArrayLengthProxy();
		if (proxy == null) return prop.GetNumElements();

		int nElements = proxy(instance, objectID);
		if (nElements > prop.GetNumElements()) {
			Assert(false);
			nElements = prop.GetNumElements();
		}

		return nElements;
	}
	public static int Array_CompareDeltas(SendProp prop, bf_read p1, bf_read p2) {
		SendProp? arrayProp = prop.GetArrayProp();
		AssertMsg(arrayProp != null, $"Array_CompareDeltas: missing m_pArrayProp for SendProp '{prop.GetName()}'.");

		int nLengthBits = prop.GetNumArrayLengthBits();
		int length1 = (int)p1.ReadUBitLong(nLengthBits);
		int length2 = (int)p2.ReadUBitLong(nLengthBits);

		int bDifferent = (length1 != length2) ? 1 : 0;

		// Compare deltas on the props that are the same.
		int nSame = Math.Min(length1, length2);
		for (int iElement = 0; iElement < nSame; iElement++) {
			bDifferent |= g_PropTypeFns[(int)arrayProp.Type].CompareDeltas(arrayProp, p1, p2);
		}

		// Now just eat up the remaining properties in whichever buffer was larger.
		if (length1 != length2) {
			bf_read buffer = (length1 > length2) ? p1 : p2;

			int nExtra = Math.Max(length1, length2) - nSame;
			for (int iEatUp = 0; iEatUp < nExtra; iEatUp++) {
				g_PropTypeFns[(int)arrayProp.Type].SkipProp(arrayProp, buffer);
			}
		}

		return bDifferent;
	}
	public static void Array_Decode(ref DecodeInfo decodeInfo) {
		SendProp? arrayProp = decodeInfo.Prop.GetArrayProp();
		AssertMsg(arrayProp != null, "Array_Decode: missing SendProp for a property.");

		DecodeInfo subDecodeInfo = default;
		subDecodeInfo.CopyVars(ref decodeInfo);
		subDecodeInfo.Prop = arrayProp;

		IFieldAccessor targetField;

		ArrayLengthRecvProxyFn lengthProxy = null!;
		if (decodeInfo.RecvProxyData.RecvProp != null) {
			RecvProp? arrayRecvProp = decodeInfo.RecvProxyData.RecvProp.GetArrayProp();
			subDecodeInfo.RecvProxyData.RecvProp = arrayRecvProp!;

			subDecodeInfo.Object = decodeInfo.Object;
			subDecodeInfo.FieldInfo = arrayRecvProp!.FieldInfo;

			lengthProxy = decodeInfo.RecvProxyData.RecvProp.GetArrayLengthProxy()!;
			targetField = subDecodeInfo.FieldInfo;
		}
		else
			targetField = arrayProp.FieldInfo;

		if (targetField is not DynamicArrayAccessor arrayFieldInfo) {
			if (targetField is not DynamicArrayIndexAccessor arrayFieldindexInfo) {
				Warning("Cannot Array_Decode on a non-ArrayFieldInfo target!\n");
				Assert(false);
				return;
			}
			arrayFieldInfo = arrayFieldindexInfo.BaseArrayAccessor;
		}


		int nElements = (int)decodeInfo.In.ReadUBitLong(decodeInfo.Prop.GetNumArrayLengthBits());

		if (lengthProxy != null)
			lengthProxy(decodeInfo.Object, decodeInfo.RecvProxyData.ObjectID, nElements);

		for (subDecodeInfo.RecvProxyData.Element = 0; subDecodeInfo.RecvProxyData.Element < nElements; subDecodeInfo.RecvProxyData.Element++) {
			var element = arrayFieldInfo.AtIndex(subDecodeInfo.RecvProxyData.Element);
			if (element == null) {
				Warning($"Invalid element at {subDecodeInfo.RecvProxyData.Element}\n");
				continue;
			}
			subDecodeInfo.FieldInfo = element;
			Get(arrayProp.GetPropType()).Decode(ref subDecodeInfo);
		}
	}

	private static RecvProp? GetMatchingRecvProp(SendProp arraySendProp) {
		throw new NotImplementedException();
	}

	private static DynamicArrayAccessor? AsBaseArray(IFieldAccessor? field) {
		if (field is DynamicArrayAccessor a) return a;
		if (field is DynamicArrayIndexAccessor idx) return idx.BaseArrayAccessor;
		return null;
	}

	public static void Array_FastCopy(SendProp sendProp, RecvProp recvProp, object sendData, IFieldAccessor sendFieldInfo, object recvData, IFieldAccessor recvFieldInfo, int objectID) {
		SendProp? arraySendProp = sendProp.GetArrayProp();
		RecvProp? arrayRecvProp = recvProp.GetArrayProp();
		AssertMsg(arraySendProp != null, $"Array_FastCopy: missing array SendProp for '{sendProp.GetName()}'.");
		AssertMsg(arrayRecvProp != null, $"Array_FastCopy: missing array RecvProp for '{recvProp.GetName()}'.");

		DynamicArrayAccessor? sendArray = AsBaseArray(arraySendProp!.FieldInfo);
		DynamicArrayAccessor? recvArray = AsBaseArray(arrayRecvProp!.FieldInfo);
		if (sendArray == null || recvArray == null) {
			Warning($"Array_FastCopy: non-array field for '{sendProp.GetName()}'.\n");
			Assert(false);
			return;
		}

		int nElements = Array_GetLength(sendData, sendProp, objectID);

		ArrayLengthRecvProxyFn? lengthProxy = recvProp.GetArrayLengthProxy();
		lengthProxy?.Invoke(recvData, objectID, nElements);

		RecvProxyData recvProxyData = default;
		recvProxyData.Value = new();
		recvProxyData.RecvProp = arrayRecvProp;
		recvProxyData.ObjectID = objectID;

		SendVarProxyFn sendElemProxy = arraySendProp.GetProxyFn();
		RecvVarProxyFn recvElemProxy = arrayRecvProp.GetProxyFn();

		for (int i = 0; i < nElements; i++) {
			IFieldAccessor? sendElem = sendArray.AtIndex(i);
			IFieldAccessor? recvElem = recvArray.AtIndex(i);
			if (sendElem == null || recvElem == null) {
				Warning($"Array_FastCopy: invalid element {i} for '{sendProp.GetName()}'.\n");
				continue;
			}

			recvProxyData.Element = i;
			sendElemProxy(arraySendProp, sendData, sendElem, ref recvProxyData.Value, i, objectID);
			recvElemProxy(ref recvProxyData, recvData, recvElem);
		}
	}

	public static void Array_DecodeZero(ref DecodeInfo info) => throw new NotImplementedException();
	public static void Array_Encode(object instance, ref DVariant var, SendProp prop, bf_write writeOut, int objectID) {
		SendProp? arrayProp = prop.GetArrayProp();
		AssertMsg(arrayProp != null, $"Array_Decode: missing m_pArrayProp for SendProp '{prop.GetName()}'.");

		int nElements = Array_GetLength(instance, prop, objectID);

		// Write the number of elements.
		writeOut.WriteUBitLong((uint)nElements, prop.GetNumArrayLengthBits());

		DynamicArrayAccessor? arrayField = AsBaseArray(arrayProp!.FieldInfo);
		for (int iElement = 0; iElement < nElements; iElement++) {
			DVariant arrayVar = new();

			// Call the proxy to get the value, then encode.
			IFieldAccessor? element = arrayField?.AtIndex(iElement) ?? arrayProp.FieldInfo;
			arrayProp.GetProxyFn()(arrayProp, instance, element, ref arrayVar, iElement, objectID);
			g_PropTypeFns[(int)arrayProp.GetPropType()].Encode(instance, ref arrayVar, arrayProp, writeOut, objectID);
		}
	}
	public static ReadOnlySpan<char> Array_GetTypeNameString() => "DPT_Array";
	public static bool Array_IsEncodedZero(SendProp prop, bf_read p) {
		SendProp arrayProp = prop.GetArrayProp()!;
		int nElements = (int)p.ReadUBitLong(prop.GetNumArrayLengthBits());

		for (int i = 0; i < nElements; i++)
			g_PropTypeFns[(int)arrayProp.GetPropType()].IsEncodedZero(arrayProp, p);

		return nElements == 0;
	}
	public static bool Array_IsZero(object instance, ref DVariant var, SendProp prop) {
		int nElements = Array_GetLength(instance, prop, -1);
		return nElements == 0;
	}
	public static void Array_SkipProp(SendProp prop, bf_read p) {
		SendProp? arrayProp = prop.GetArrayProp();
		AssertMsg(arrayProp != null, "Array_SkipProp: missing m_pArrayProp for a property.");

		int nElements = (int)p.ReadUBitLong(prop.GetNumArrayLengthBits());

		for (int i = 0; i < nElements; i++)
			Get(arrayProp.GetPropType()).SkipProp(arrayProp, p);
	}
	#endregion
	#region SendPropType.DataTable
	public static int DataTable_CompareDeltas(SendProp prop, bf_read p1, bf_read p2) => throw new NotImplementedException();
	public static void DataTable_Decode(ref DecodeInfo decodeInfo) => throw new NotImplementedException();
	public static void DataTable_DecodeZero(ref DecodeInfo info) => throw new NotImplementedException();
	public static void DataTable_Encode(object instance, ref DVariant var, SendProp prop, bf_write writeOut, int objectID) => throw new NotImplementedException();
	public static ReadOnlySpan<char> DataTable_GetTypeNameString() => "DPT_DataTable";
	public static bool DataTable_IsEncodedZero(SendProp prop, bf_read p) => throw new NotImplementedException();
	public static bool DataTable_IsZero(object instance, ref DVariant var, SendProp prop) => throw new NotImplementedException();
	public static void DataTable_SkipProp(SendProp prop, bf_read p) => throw new NotImplementedException();
	#endregion
	#region SendPropType.GModTable
#if GMOD_DLL
	public static int GModTable_CompareDeltas(SendProp prop, bf_read p1, bf_read p2) {
		GMODDataTable? dt = GMODDataTable.s_CurrentTable;
		int tick = GMODDataTable.s_TargetTick;
		if (dt == null)
			Error("GMODTable_CompareDeltas: dt is null!");
		return GMODDataTable.Compare(p1, p2, dt, tick) ? 1 : 0;
	}
	public static void GModTable_Decode(ref DecodeInfo decodeInfo) {
		GMODDataTable? dt = decodeInfo.FieldInfo?.GetValue<IGMODDataTable>(decodeInfo.Object) as GMODDataTable;
		if (dt != null)
			dt.Decode(decodeInfo.Object, decodeInfo.In);
		else
			GMODDataTable.Skip(decodeInfo.In);
	}
	public static void GModTable_DecodeZero(ref DecodeInfo info) { }
	public static void GModTable_Encode(object instance, ref DVariant var, SendProp prop, bf_write writeOut, int objectID) {
		GMODDataTable? dt = var.Data as GMODDataTable;
		if (dt == null)
			Error("GMODTable_Encode: dt is null!");
		dt!.Encode(instance, writeOut);
	}
	public static void GModTable_FastCopy(SendProp sendProp, RecvProp recvProp, object sendData, IFieldAccessor sendFieldInfo, object recvData, IFieldAccessor recvFieldInfo, int objectID) {
		GMODDataTable src = (GMODDataTable)sendFieldInfo.GetValue<IGMODDataTable>(sendData)!;
		GMODDataTable dest = (GMODDataTable)recvFieldInfo.GetValue<IGMODDataTable>(recvData)!;
		dest.CopyFrom(recvData, sendData, src);
	}
	public static ReadOnlySpan<char> GModTable_GetTypeNameString() => "DPT_GMODTable";
	public static bool GModTable_IsEncodedZero(SendProp prop, bf_read p) => GMODDataTable.Skip(p);
	public static bool GModTable_IsZero(object instance, ref DVariant var, SendProp prop) => ((GMODDataTable)var.Data!).IsEmpty();
	public static void GModTable_SkipProp(SendProp prop, bf_read p) => GMODDataTable.Skip(p);
#endif
	#endregion
}
