using Source.Common.SoundEmitterSystem;

using System.Numerics;

namespace Source.Common;

public enum ThreeState
{
	False,
	True,
	None
}
public struct Interval
{
	public float Start;
	public float Range;
	public static bool Compare(in Interval i1, in Interval i2) {
		return memcmp(in i1, in i2) == 0;
	}

	public static bool Compare<T>(in SoundInterval<T> i1, in SoundInterval<T> i2) where T : unmanaged, INumber<T> {
		return memcmp(in i1, in i2) == 0;
	}

	public static Interval Read(ReadOnlySpan<char> str) {
		Interval tmp;
		tmp.Start = 0;
		tmp.Range = 0;

		int comma = str.IndexOf(',');
		if (comma >= 0) {
			tmp.Start = (float)atof(str[..comma]);
			tmp.Range = (float)atof(str[(comma + 1)..]) - tmp.Start;
		}
		else if (!str.IsEmpty)
			tmp.Start = (float)atof(str);

		return tmp;
	}

	public static float Random(in Interval interval) {
		float ret = interval.Start;
		if (interval.Range != 0)
			ret += RandomFloat(0, interval.Range);
		return ret;
	}
}

public static class BaseTypesGlobals
{
	public static int PAD_NUMBER(int number, int boundary) => (number + (boundary - 1)) / boundary * boundary;

	public static float FloatMakeNegative(vec_t f) => -MathF.Abs(f);
	public static float FloatMakePositive(vec_t f) => MathF.Abs(f);
	public static float FloatNegate(vec_t f) => -f;
}
