namespace Source.Common.Mathematics;

public static class Float16
{
	public const float maxfloat16bits = 65504.0f;

	public static float Convert16bitFloatTo32bits(ushort input) {
		int sign = input >> 15;
		int mantissa = input & 1023;
		int biased_exponent = (input >> 10) & 31;

		if (biased_exponent == 31 && mantissa == 0)
			return sign != 0 ? -maxfloat16bits : maxfloat16bits;

		if (biased_exponent == 31 && mantissa != 0)
			return 0.0f;

		if (biased_exponent == 0 && mantissa != 0) {
			float m = mantissa / 1024.0f;
			float s = sign != 0 ? -1.0f : 1.0f;
			return s * m * (1.0f / (1 << 14));
		}

		int exponent = biased_exponent == 0 ? 0 : biased_exponent - 15 + 127;
		return BitConverter.Int32BitsToSingle((sign << 31) | (exponent << 23) | (mantissa << 13));
	}

	public static float Convert16bitFloatTo32bits(Half input) => Convert16bitFloatTo32bits(BitConverter.HalfToUInt16Bits(input));
}
