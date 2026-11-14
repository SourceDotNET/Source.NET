namespace Source.Common.Mathematics;

public static class Float16
{
	public const float maxfloat16bits = 65504.0f;

	public static ushort ConvertFloatTo16bits(float input) {
		if (input > maxfloat16bits)
			input = maxfloat16bits;
		else if (input < -maxfloat16bits)
			input = -maxfloat16bits;

		uint bits = BitConverter.SingleToUInt32Bits(input);
		uint inMantissa = bits & 0x7FFFFF;
		uint inBiasedExponent = (bits >> 23) & 0xFF;
		uint sign = bits >> 31;

		uint mantissa = 0;
		uint biased_exponent = 0;

		if (inBiasedExponent == 0xFF && inMantissa == 0) {
			mantissa = 0x3FF;
			biased_exponent = 0x1E;
		}
		else if (inBiasedExponent != 0 && inBiasedExponent != 0xFF) {
			int new_exp = (int)inBiasedExponent - 127;

			if (new_exp < -14) {
				uint exp_val = (uint)(-14 - new_exp);
				if (exp_val > 0 && exp_val < 11)
					mantissa = (1u << (int)(10 - exp_val)) + (inMantissa >> (int)(13 + exp_val));
			}
			else if (new_exp > 15) {
				mantissa = 0x3FF;
				biased_exponent = 0x1E;
			}
			else {
				biased_exponent = (uint)(new_exp + 15);
				mantissa = inMantissa >> 13;
			}
		}

		return (ushort)((mantissa & 0x3FF) | ((biased_exponent & 0x1F) << 10) | (sign << 15));
	}

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
