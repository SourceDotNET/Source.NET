using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Source.Common;

public interface IUniformRandomStream
{
	void SetSeed(int seed);
	float RandomFloat(float minVal, float maxVal);
	int RandomInt(int minVal, int maxVal);
	float RandomFloatExp(float minVal, float maxVal, float exponent);
}

public static class RandomGlobals {
	static readonly UniformRandomStream s_UniformStream = new();
	internal static IUniformRandomStream UniformStream => s_UniformStream;
	public static void RandomSeed(int seed) => s_UniformStream.SetSeed(seed);
	public static float RandomFloat(float min, float max) => s_UniformStream.RandomFloat(min, max);
	public static float RandomFloatExp(float min, float max, float exponent) => s_UniformStream.RandomFloatExp(min, max, exponent);
	public static int RandomInt(int min, int max) => s_UniformStream.RandomInt(min, max);
}

public class UniformRandomStream : IUniformRandomStream
{
	const int IA = 16807;
	const int IM = 2147483647;
	const int IQ = 127773;
	const int IR = 2836;
	const int NTAB = 32;
	const int NDIV = 1 + (IM - 1) / NTAB;
	const ulong MAX_RANDOM_RANGE = 0x7FFFFFFFUL;

	const float AM = 1.0f / IM;
	const float EPS = 1.2e-7f;
	const float RNMX = 1.0f - EPS;

	int m_idum;
	int m_iy;
	readonly int[] m_iv = new int[NTAB];

	int GenerateRandomNumber() {
		int j;
		int k;

		if (m_idum <= 0 || m_iy == 0) {
			if (-(m_idum) < 1)
				m_idum = 1;
			else
				m_idum = -(m_idum);

			for (j = NTAB + 7; j >= 0; j--) {
				k = (m_idum) / IQ;
				m_idum = IA * (m_idum - k * IQ) - IR * k;
				if (m_idum < 0)
					m_idum += IM;
				if (j < NTAB)
					m_iv[j] = m_idum;
			}
			m_iy = m_iv[0];
		}
		k = (m_idum) / IQ;
		m_idum = IA * (m_idum - k * IQ) - IR * k;
		if (m_idum < 0)
			m_idum += IM;
		j = m_iy / NDIV;

		// We're seeing some strange memory corruption in the contents of s_pUniformStream. 
		// Perhaps it's being caused by something writing past the end of this array? 
		// Bounds-check in release to see if that's the case.
		if (j >= NTAB || j < 0) {
			Debugger.Break();
			// Clamp j.
			j &= NTAB - 1;
		}

		m_iy = m_iv[j];
		m_iv[j] = m_idum;

		return m_iy;
	}

	public float RandomFloat(float low, float high) {
		float fl = AM * GenerateRandomNumber();
		if (fl > RNMX) {
			fl = RNMX;
		}
		return (fl * (high - low)) + low; // float in [low,high)
	}

	public float RandomFloatExp(float min, float max, float exponent) {
		float fl = AM * GenerateRandomNumber();
		if (fl > RNMX) 
			fl = RNMX;
		if (exponent != 1.0f) 
			fl = MathF.Pow(fl, exponent);
		return (fl * (max - min)) + min; // float in [low,high)
	}

	public int RandomInt(int low, int high) {
		uint maxAcceptable;
		uint x = unchecked((uint)(high - low + 1));
		uint n;

		// If you hit either of these assert, you're not getting back the random number that you thought you were.
		Assert(x == high - (long)low + 1); // Check that we didn't overflow int
		Assert(x - 1 <= MAX_RANDOM_RANGE); // Check that the values provide an acceptable range

		if (x <= 1 || MAX_RANDOM_RANGE < x - 1) {
			Assert(low == high); // This is the only time it is OK to have a range containing a single number
			return low;
		}

		// The following maps a uniform distribution on the interval [0,MAX_RANDOM_RANGE]
		// to a smaller, client-specified range of [0,x-1] in a way that doesn't bias
		// the uniform distribution unfavorably. Even for a worst case x, the loop is
		// guaranteed to be taken no more than half the time, so for that worst case x,
		// the average number of times through the loop is 2. For cases where x is
		// much smaller than MAX_RANDOM_RANGE, the average number of times through the
		// loop is very close to 1.
		//
		maxAcceptable = unchecked((uint)(MAX_RANDOM_RANGE - ((MAX_RANDOM_RANGE + 1) % x)));
		do n = (uint)GenerateRandomNumber(); while (n > maxAcceptable);

		return (int)(low + (n % x));
	}

	public void SetSeed(int seed) {
		m_idum = ((seed < 0) ? seed : -seed);
		m_iy = 0;
	}
}

public class GaussianRandomStream
{
	IUniformRandomStream? UniformStream;
	bool HaveValue;
	float RandomValue;

	public GaussianRandomStream(IUniformRandomStream? uniformStream = null) => AttachToStream(uniformStream);

	public void AttachToStream(IUniformRandomStream? uniformStream) {
		UniformStream = uniformStream;
		HaveValue = false;
	}

	public float RandomFloat(float mean = 0.0f, float stdDev = 1.0f) {
		IUniformRandomStream uniformStream = UniformStream ?? RandomGlobals.UniformStream;
		float fac, rsq, v1, v2;

		if (!HaveValue) {
			do {
				v1 = 2.0f * uniformStream.RandomFloat(0.0f, 1.0f) - 1.0f;
				v2 = 2.0f * uniformStream.RandomFloat(0.0f, 1.0f) - 1.0f;
				rsq = v1 * v1 + v2 * v2;
			} while ((rsq > 1.0f) || (rsq == 0.0f));

			fac = MathF.Sqrt(-2.0f * MathF.Log(rsq) / rsq);

			RandomValue = v1 * fac;
			HaveValue = true;

			return stdDev * (v2 * fac) + mean;
		}
		else {
			HaveValue = false;
			return stdDev * RandomValue + mean;
		}
	}
}
