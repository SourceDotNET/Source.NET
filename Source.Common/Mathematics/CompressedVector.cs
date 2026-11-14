namespace Source.Common.Mathematics;

public struct Float16
{
	public const int float32bias = 127;
	public const int float16bias = 15;
	public const float maxfloat16bits = 65504.0f;

	public ushort RawWord;

	public void Init() => RawWord = 0;

	public readonly ushort GetBits() => RawWord;
	public readonly float GetFloat() => Convert16bitFloatTo32bits(RawWord);
	public void SetFloat(float value) => RawWord = ConvertFloatTo16bits(value);

	public readonly bool IsInfinity() => IsInfinity(RawWord);
	public readonly bool IsNaN() => IsNaN(RawWord);

	static uint Mantissa(ushort value) => (uint)(value & 0x3FF);
	static uint BiasedExponent(ushort value) => (uint)((value >> 10) & 0x1F);
	static uint Sign(ushort value) => (uint)(value >> 15);

	static bool IsNaN(ushort value) => BiasedExponent(value) == 31 && Mantissa(value) != 0;
	static bool IsInfinity(ushort value) => BiasedExponent(value) == 31 && Mantissa(value) == 0;

	public static ushort ConvertFloatTo16bits(float input) {
		if (input > maxfloat16bits)
			input = maxfloat16bits;
		else if (input < -maxfloat16bits)
			input = -maxfloat16bits;

		uint inFloat = BitConverter.SingleToUInt32Bits(input);
		uint inMantissa = inFloat & 0x7FFFFF;
		uint inBiasedExponent = (inFloat >> 23) & 0xFF;
		uint sign = inFloat >> 31;

		uint mantissa = 0;
		uint biasedExponent = 0;

		if (inBiasedExponent == 0 && inMantissa == 0) {
			mantissa = 0;
			biasedExponent = 0;
		}
		else if (inBiasedExponent == 0 && inMantissa != 0) {
			mantissa = 0;
			biasedExponent = 0;
		}
		else if (inBiasedExponent == 0xff && inMantissa == 0) {
			mantissa = 0x3ff;
			biasedExponent = 0x1e;
		}
		else if (inBiasedExponent == 0xff && inMantissa != 0) {
			mantissa = 0;
			biasedExponent = 0;
		}
		else {
			int newExp = (int)inBiasedExponent - 127;

			if (newExp < -24) {
				mantissa = 0;
				biasedExponent = 0;
			}

			if (newExp < -14) {
				biasedExponent = 0;
				uint expVal = (uint)(-14 - ((int)inBiasedExponent - float32bias));
				if (expVal > 0 && expVal < 11)
					mantissa = (uint)(1 << (int)(10 - expVal)) + (inMantissa >> (int)(13 + expVal));
			}
			else if (newExp > 15) {
				mantissa = 0x3ff;
				biasedExponent = 0x1e;
			}
			else {
				biasedExponent = (uint)(newExp + 15);
				mantissa = inMantissa >> 13;
			}
		}

		return (ushort)((mantissa & 0x3FF) | ((biasedExponent & 0x1F) << 10) | (sign << 15));
	}

	public static float Convert16bitFloatTo32bits(ushort input) {
		if (IsInfinity(input))
			return maxfloat16bits * ((Sign(input) == 1) ? -1.0f : 1.0f);

		if (IsNaN(input))
			return 0.0f;

		if (BiasedExponent(input) == 0 && Mantissa(input) != 0) {
			const float half_denorm = 1.0f / 16384.0f;
			float mantissa = ((float)Mantissa(input)) / 1024.0f;
			float sgn = (Sign(input) != 0) ? -1.0f : 1.0f;
			return sgn * mantissa * half_denorm;
		}
		else {
			uint mantissa = Mantissa(input);
			uint biasedExponent = BiasedExponent(input);
			uint sign = Sign(input) << 31;
			biasedExponent = ((biasedExponent - float16bias + float32bias) * (biasedExponent != 0 ? 1u : 0u)) << 23;
			mantissa <<= (23 - 10);
			return BitConverter.UInt32BitsToSingle(mantissa | biasedExponent | sign);
		}
	}
}

public struct Float16WithAssign
{
	public Float16 Value;

	public Float16WithAssign(float f) => Value.RawWord = Float16.ConvertFloatTo16bits(f);

	public readonly ushort GetBits() => Value.RawWord;

	public static implicit operator Float16WithAssign(float f) => new(f);
	public static implicit operator float(Float16WithAssign f) => Float16.Convert16bitFloatTo32bits(f.Value.RawWord);
}
