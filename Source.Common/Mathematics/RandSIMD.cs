using System.Runtime.Intrinsics;

namespace Source.Common.Mathematics;

public static class RandSIMD
{
	const int MAX_SIMULTANEOUS_RANDOM_STREAMS = 32;

	sealed class StreamContext
	{
		readonly Vector128<float>[] RandY = new Vector128<float>[55];
		int J;
		int K;

		public void Seed(uint seed) {
			J = 23;
			K = 54;
			Span<float> lanes = stackalloc float[4];
			for (int i = 0; i < 55; i++) {
				for (int j = 0; j < 4; j++) {
					lanes[j] = (float)((seed >> 16) / 65536.0);
					seed = unchecked((seed + 1) * 3141592621u);
				}
				RandY[i] = Vector128.Create(lanes[0], lanes[1], lanes[2], lanes[3]);
			}
		}

		public Vector128<float> Next() {
			Vector128<float> ret = RandY[K] + RandY[J];
			Vector128<float> overflow = Vector128.GreaterThanOrEqual(ret, MathLib.Four_Ones);
			ret -= MathLib.Four_Ones & overflow;
			RandY[K] = ret;
			if (--J < 0)
				J = 54;
			if (--K < 0)
				K = 54;
			return ret;
		}
	}

	static readonly StreamContext[] Contexts = CreateContexts();
	static readonly int[] ContextsInUse = new int[MAX_SIMULTANEOUS_RANDOM_STREAMS];

	static StreamContext[] CreateContexts() {
		StreamContext[] contexts = new StreamContext[MAX_SIMULTANEOUS_RANDOM_STREAMS];
		for (int i = 0; i < contexts.Length; i++)
			contexts[i] = new StreamContext();
		return contexts;
	}

	public static void Seed(uint seed) {
		for (int i = 0; i < MAX_SIMULTANEOUS_RANDOM_STREAMS; i++)
			Contexts[i].Seed(unchecked(seed + (uint)i));
	}

	public static int GetContext() {
		while (true) {
			for (int i = 0; i < MAX_SIMULTANEOUS_RANDOM_STREAMS; i++) {
				if (ContextsInUse[i] == 0 && Interlocked.CompareExchange(ref ContextsInUse[i], 1, 0) == 0)
					return i;
			}
			Thread.Sleep(0);
		}
	}

	public static void ReleaseContext(int context) => ContextsInUse[context] = 0;

	public static Vector128<float> Next(int context) => Contexts[context].Next();

	public static Vector128<float> PowFixedPointExponent(Vector128<float> x, int exponent) {
		Vector128<float> result = MathLib.Four_Ones;
		int absExp = Math.Abs(exponent);
		if ((absExp & 3) != 0) {
			Vector128<float> sqrtX = Vector128.Sqrt(x);
			if ((absExp & 1) != 0)
				result = Vector128.Sqrt(sqrtX);
			if ((absExp & 2) != 0)
				result *= sqrtX;
		}
		absExp >>= 2;
		Vector128<float> curPower = x;
		while (true) {
			if ((absExp & 1) != 0)
				result *= curPower;
			absExp >>= 1;
			if (absExp == 0)
				break;
			curPower *= curPower;
		}
		if (exponent < 0) {
			Vector128<float> zeroFix = Vector128.Equals(result, Vector128<float>.Zero) & Vector128.Create(1.1920929e-07f);
			result = MathLib.Four_Ones / (result | zeroFix);
		}
		return result;
	}
}
