using System.Numerics;

namespace RapidJson;

public static unsafe class Dtoa
{
	const int kDiySignificandSize = 64;
	const int kDpSignificandSize = 52;
	const int kDpExponentBias = 0x3FF + kDpSignificandSize;
	const int kDpMinExponent = -kDpExponentBias;
	const ulong kDpExponentMask = 0x7FF0000000000000;
	const ulong kDpSignificandMask = 0x000FFFFFFFFFFFFF;
	const ulong kDpHiddenBit = 0x0010000000000000;

	public const int MaxLength = 25;

	struct DiyFp
	{
		public ulong F;
		public int E;

		public DiyFp(ulong f, int e) {
			F = f;
			E = e;
		}

		public DiyFp(double d) {
			ulong u64 = BitConverter.DoubleToUInt64Bits(d);
			int biasedE = (int)((u64 & kDpExponentMask) >> kDpSignificandSize);
			ulong significand = u64 & kDpSignificandMask;
			if (biasedE != 0) {
				F = significand + kDpHiddenBit;
				E = biasedE - kDpExponentBias;
			}
			else {
				F = significand;
				E = kDpMinExponent + 1;
			}
		}

		public static DiyFp operator -(DiyFp a, DiyFp b) => new(a.F - b.F, a.E);

		public static DiyFp operator *(DiyFp a, DiyFp b) {
			ulong h = Math.BigMul(a.F, b.F, out ulong l);
			if ((l & (1UL << 63)) != 0)
				h++;
			return new(h, a.E + b.E + 64);
		}

		public readonly DiyFp Normalize() {
			int s = BitOperations.LeadingZeroCount(F);
			return new(F << s, E - s);
		}

		public readonly DiyFp NormalizeBoundary() {
			DiyFp res = this;
			while ((res.F & (kDpHiddenBit << 1)) == 0) {
				res.F <<= 1;
				res.E--;
			}

			res.F <<= kDiySignificandSize - kDpSignificandSize - 2;
			res.E -= kDiySignificandSize - kDpSignificandSize - 2;
			return res;
		}

		public readonly void NormalizedBoundaries(out DiyFp minus, out DiyFp plus) {
			DiyFp pl = new DiyFp((F << 1) + 1, E - 1).NormalizeBoundary();
			DiyFp mi = F == kDpHiddenBit ? new DiyFp((F << 2) - 1, E - 2) : new DiyFp((F << 1) - 1, E - 1);
			mi.F <<= mi.E - pl.E;
			mi.E = pl.E;
			plus = pl;
			minus = mi;
		}
	}

	static readonly ulong[] kCachedPowers_F = [
		0xFA8FD5A0081C0288, 0xBAAEE17FA23EBF76, 0x8B16FB203055AC76, 0xCF42894A5DCE35EA,
		0x9A6BB0AA55653B2D, 0xE61ACF033D1A45DF, 0xAB70FE17C79AC6CA, 0xFF77B1FCBEBCDC4F,
		0xBE5691EF416BD60C, 0x8DD01FAD907FFC3C, 0xD3515C2831559A83, 0x9D71AC8FADA6C9B5,
		0xEA9C227723EE8BCB, 0xAECC49914078536D, 0x823C12795DB6CE57, 0xC21094364DFB5637,
		0x9096EA6F3848984F, 0xD77485CB25823AC7, 0xA086CFCD97BF97F4, 0xEF340A98172AACE5,
		0xB23867FB2A35B28E, 0x84C8D4DFD2C63F3B, 0xC5DD44271AD3CDBA, 0x936B9FCEBB25C996,
		0xDBAC6C247D62A584, 0xA3AB66580D5FDAF6, 0xF3E2F893DEC3F126, 0xB5B5ADA8AAFF80B8,
		0x87625F056C7C4A8B, 0xC9BCFF6034C13053, 0x964E858C91BA2655, 0xDFF9772470297EBD,
		0xA6DFBD9FB8E5B88F, 0xF8A95FCF88747D94, 0xB94470938FA89BCF, 0x8A08F0F8BF0F156B,
		0xCDB02555653131B6, 0x993FE2C6D07B7FAC, 0xE45C10C42A2B3B06, 0xAA242499697392D3,
		0xFD87B5F28300CA0E, 0xBCE5086492111AEB, 0x8CBCCC096F5088CC, 0xD1B71758E219652C,
		0x9C40000000000000, 0xE8D4A51000000000, 0xAD78EBC5AC620000, 0x813F3978F8940984,
		0xC097CE7BC90715B3, 0x8F7E32CE7BEA5C70, 0xD5D238A4ABE98068, 0x9F4F2726179A2245,
		0xED63A231D4C4FB27, 0xB0DE65388CC8ADA8, 0x83C7088E1AAB65DB, 0xC45D1DF942711D9A,
		0x924D692CA61BE758, 0xDA01EE641A708DEA, 0xA26DA3999AEF774A, 0xF209787BB47D6B85,
		0xB454E4A179DD1877, 0x865B86925B9BC5C2, 0xC83553C5C8965D3D, 0x952AB45CFA97A0B3,
		0xDE469FBD99A05FE3, 0xA59BC234DB398C25, 0xF6C69A72A3989F5C, 0xB7DCBF5354E9BECE,
		0x88FCF317F22241E2, 0xCC20CE9BD35C78A5, 0x98165AF37B2153DF, 0xE2A0B5DC971F303A,
		0xA8D9D1535CE3B396, 0xFB9B7CD9A4A7443C, 0xBB764C4CA7A44410, 0x8BAB8EEFB6409C1A,
		0xD01FEF10A657842C, 0x9B10A4E5E9913129, 0xE7109BFBA19C0C9D, 0xAC2820D9623BF429,
		0x80444B5E7AA7CF85, 0xBF21E44003ACDD2D, 0x8E679C2F5E44FF8F, 0xD433179D9C8CB841,
		0x9E19DB92B4E31BA9, 0xEB96BF6EBADF77D9, 0xAF87023B9BF0EE6B,
	];

	static readonly short[] kCachedPowers_E = [
		-1220, -1193, -1166, -1140, -1113, -1087, -1060, -1034, -1007, -980,
		-954, -927, -901, -874, -847, -821, -794, -768, -741, -715,
		-688, -661, -635, -608, -582, -555, -529, -502, -475, -449,
		-422, -396, -369, -343, -316, -289, -263, -236, -210, -183,
		-157, -130, -103, -77, -50, -24, 3, 30, 56, 83,
		109, 136, 162, 189, 216, 242, 269, 295, 322, 348,
		375, 402, 428, 455, 481, 508, 534, 561, 588, 614,
		641, 667, 694, 720, 747, 774, 800, 827, 853, 880,
		907, 933, 960, 986, 1013, 1039, 1066,
	];

	static readonly ulong[] kPow10 = [
		1, 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000, 1000000000,
		10000000000, 100000000000, 1000000000000, 10000000000000, 100000000000000, 1000000000000000,
		10000000000000000, 100000000000000000, 1000000000000000000, 10000000000000000000,
	];

	static DiyFp GetCachedPower(int e, out int K) {
		double dk = (-61 - e) * 0.30102999566398114 + 347;
		int k = (int)dk;
		if (dk - k > 0.0)
			k++;

		int index = (k >> 3) + 1;
		K = -(-348 + (index << 3));
		return new(kCachedPowers_F[index], kCachedPowers_E[index]);
	}

	static void GrisuRound(byte* buffer, int len, ulong delta, ulong rest, ulong ten_kappa, ulong wp_w) {
		while (rest < wp_w && delta - rest >= ten_kappa && (rest + ten_kappa < wp_w || wp_w - rest > rest + ten_kappa - wp_w)) {
			buffer[len - 1]--;
			rest += ten_kappa;
		}
	}

	static int CountDecimalDigit32(uint n) {
		if (n < 10) return 1;
		if (n < 100) return 2;
		if (n < 1000) return 3;
		if (n < 10000) return 4;
		if (n < 100000) return 5;
		if (n < 1000000) return 6;
		if (n < 10000000) return 7;
		if (n < 100000000) return 8;
		return 9;
	}

	static void DigitGen(in DiyFp W, in DiyFp Mp, ulong delta, byte* buffer, out int len, ref int K) {
		DiyFp one = new(1UL << -Mp.E, Mp.E);
		DiyFp wp_w = Mp - W;
		uint p1 = (uint)(Mp.F >> -one.E);
		ulong p2 = Mp.F & (one.F - 1);
		int kappa = CountDecimalDigit32(p1);
		len = 0;

		while (kappa > 0) {
			uint d = 0;
			switch (kappa) {
				case 9: d = p1 / 100000000; p1 %= 100000000; break;
				case 8: d = p1 / 10000000; p1 %= 10000000; break;
				case 7: d = p1 / 1000000; p1 %= 1000000; break;
				case 6: d = p1 / 100000; p1 %= 100000; break;
				case 5: d = p1 / 10000; p1 %= 10000; break;
				case 4: d = p1 / 1000; p1 %= 1000; break;
				case 3: d = p1 / 100; p1 %= 100; break;
				case 2: d = p1 / 10; p1 %= 10; break;
				case 1: d = p1; p1 = 0; break;
			}

			if (d != 0 || len != 0)
				buffer[len++] = (byte)('0' + d);
			kappa--;
			ulong tmp = ((ulong)p1 << -one.E) + p2;
			if (tmp <= delta) {
				K += kappa;
				GrisuRound(buffer, len, delta, tmp, kPow10[kappa] << -one.E, wp_w.F);
				return;
			}
		}

		for (; ; ) {
			p2 *= 10;
			delta *= 10;
			byte d = (byte)(p2 >> -one.E);
			if (d != 0 || len != 0)
				buffer[len++] = (byte)('0' + d);
			p2 &= one.F - 1;
			kappa--;
			if (p2 < delta) {
				K += kappa;
				int index = -kappa;
				GrisuRound(buffer, len, delta, p2, one.F, wp_w.F * (index < 20 ? kPow10[index] : 0));
				return;
			}
		}
	}

	static void Grisu2(double value, byte* buffer, out int length, out int K) {
		DiyFp v = new(value);
		v.NormalizedBoundaries(out DiyFp w_m, out DiyFp w_p);

		DiyFp c_mk = GetCachedPower(w_p.E, out K);
		DiyFp W = v.Normalize() * c_mk;
		DiyFp Wp = w_p * c_mk;
		DiyFp Wm = w_m * c_mk;
		Wm.F++;
		Wp.F--;
		DigitGen(in W, in Wp, Wp.F - Wm.F, buffer, out length, ref K);
	}

	static int WriteExponent(int K, byte* buffer) {
		int i = 0;
		if (K < 0) {
			buffer[i++] = (byte)'-';
			K = -K;
		}

		if (K >= 100) {
			buffer[i++] = (byte)('0' + K / 100);
			K %= 100;
			buffer[i++] = (byte)('0' + K / 10);
			buffer[i++] = (byte)('0' + K % 10);
		}
		else if (K >= 10) {
			buffer[i++] = (byte)('0' + K / 10);
			buffer[i++] = (byte)('0' + K % 10);
		}
		else
			buffer[i++] = (byte)('0' + K);

		return i;
	}

	static int Prettify(byte* buffer, int length, int k, int maxDecimalPlaces) {
		int kk = length + k;

		if (0 <= k && kk <= 21) {
			for (int i = length; i < kk; i++)
				buffer[i] = (byte)'0';
			buffer[kk] = (byte)'.';
			buffer[kk + 1] = (byte)'0';
			return kk + 2;
		}
		else if (0 < kk && kk <= 21) {
			Buffer.MemoryCopy(buffer + kk, buffer + kk + 1, length - kk, length - kk);
			buffer[kk] = (byte)'.';
			if (0 > k + maxDecimalPlaces) {
				for (int i = kk + maxDecimalPlaces; i > kk + 1; i--) {
					if (buffer[i] != '0')
						return i + 1;
				}

				return kk + 2;
			}
			else
				return length + 1;
		}
		else if (-6 < kk && kk <= 0) {
			int offset = 2 - kk;
			Buffer.MemoryCopy(buffer, buffer + offset, length, length);
			buffer[0] = (byte)'0';
			buffer[1] = (byte)'.';
			for (int i = 2; i < offset; i++)
				buffer[i] = (byte)'0';
			if (length - kk > maxDecimalPlaces) {
				for (int i = maxDecimalPlaces + 1; i > 2; i--) {
					if (buffer[i] != '0')
						return i + 1;
				}

				return 3;
			}
			else
				return length + offset;
		}
		else if (kk < -maxDecimalPlaces) {
			buffer[0] = (byte)'0';
			buffer[1] = (byte)'.';
			buffer[2] = (byte)'0';
			return 3;
		}
		else if (length == 1) {
			buffer[1] = (byte)'e';
			return 2 + WriteExponent(kk - 1, buffer + 2);
		}
		else {
			Buffer.MemoryCopy(buffer + 1, buffer + 2, length - 1, length - 1);
			buffer[1] = (byte)'.';
			buffer[length + 1] = (byte)'e';
			return length + 2 + WriteExponent(kk - 1, buffer + length + 2);
		}
	}

	public static int Write(double value, Span<byte> target, int maxDecimalPlaces = 324) {
		ArgumentOutOfRangeException.ThrowIfLessThan(target.Length, MaxLength);

		fixed (byte* buffer = target) {
			if (value == 0) {
				int i = 0;
				if (double.IsNegative(value))
					buffer[i++] = (byte)'-';
				buffer[i++] = (byte)'0';
				buffer[i++] = (byte)'.';
				buffer[i++] = (byte)'0';
				return i;
			}

			int sign = 0;
			if (value < 0) {
				buffer[0] = (byte)'-';
				value = -value;
				sign = 1;
			}

			Grisu2(value, buffer + sign, out int length, out int K);
			return sign + Prettify(buffer + sign, length, K, maxDecimalPlaces);
		}
	}
}
