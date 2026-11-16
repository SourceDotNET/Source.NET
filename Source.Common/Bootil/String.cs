namespace Bootil;

public static class String
{
	public static void Lower(ref string str) => str = str.ToLowerInvariant();

	public static class Format
	{
		public static string Memory(ulong bytes) {
			float mb = bytes / 1024.0f / 1024.0f;

			if (mb / 1024.0f >= 1.0)
				return FormattableString.Invariant($"{mb / 1024.0f:0.0} GB");

			if (mb >= 1.0)
				return FormattableString.Invariant($"{mb:0.0} MB");

			float kb = bytes / 1024.0f;
			if (kb >= 1.0)
				return FormattableString.Invariant($"{kb:0.0} KB");

			return $"{bytes} B";
		}

		public static string UInt64(ulong num) => num.ToString(System.Globalization.CultureInfo.InvariantCulture);

		const double ExactLimit = 549755813888.0;

		static ulong RoundTenThousandths(double magnitude) {
			ulong bits = BitConverter.DoubleToUInt64Bits(magnitude);
			int exponent = (int)(bits >> 52) & 0x7FF;
			ulong mantissa = bits & 0xFFFFFFFFFFFFF;
			if (exponent == 0)
				exponent = 1;
			else
				mantissa |= 1UL << 52;

			int shift = 1071 - exponent;
			if (shift > 63)
				return 0;

			ulong scaled = mantissa * 625;
			ulong quotient = scaled >> shift;
			ulong rest = scaled & ((1UL << shift) - 1);
			ulong half = 1UL << (shift - 1);
			if (rest > half || (rest == half && (quotient & 1) != 0))
				quotient++;

			return quotient;
		}

		public static int NiceFloat(float value, Span<byte> target) {
			double number = value;
			if (!double.IsFinite(number) || Math.Abs(number) >= ExactLimit) {
				string text;
				if (double.IsFinite(number))
					text = Source.Common.CFormatting.FormatFixed(number, 4).TrimEnd('0').TrimEnd('.');
				else {
#if WIN32
					text = Source.Common.CFormatting.FormatFixed(number, 4);
#elif LINUX
					text = double.IsNaN(number) ? double.IsNegative(number) ? "-nan" : "nan" : number < 0 ? "-inf" : "inf";
#else
					text = double.IsNaN(number) ? "nan" : number < 0 ? "-inf" : "inf";
#endif
				}

				return System.Text.Encoding.ASCII.GetBytes(text, target);
			}

			ulong scaled = RoundTenThousandths(Math.Abs(number));
			int length = 0;
			if (double.IsNegative(number))
				target[length++] = (byte)'-';

			(scaled / 10000).TryFormat(target[length..], out int written, default, System.Globalization.CultureInfo.InvariantCulture);
			length += written;

			uint fraction = (uint)(scaled % 10000);
			if (fraction != 0) {
				target[length] = (byte)'.';
				target[length + 1] = (byte)('0' + fraction / 1000);
				target[length + 2] = (byte)('0' + fraction / 100 % 10);
				target[length + 3] = (byte)('0' + fraction / 10 % 10);
				target[length + 4] = (byte)('0' + fraction % 10);
				length += 5;
				while (target[length - 1] == '0')
					length--;
			}

			return length;
		}

		// note: returns what To::Double reads back from the NiceDouble text
		public static double NiceDouble(double value) {
			if (!double.IsFinite(value) || Math.Abs(value) >= ExactLimit)
				return value;

			double rounded = RoundTenThousandths(Math.Abs(value)) / 10000.0;
			return double.IsNegative(value) ? -rounded : rounded;
		}
	}

	public static class To
	{
		public static ulong UInt64(ReadOnlySpan<char> str) {
			int i = 0;
			while (i < str.Length && char.IsWhiteSpace(str[i]))
				i++;

			if (i < str.Length && str[i] == '+')
				i++;

			ulong value = 0;
			while (i < str.Length && str[i] >= '0' && str[i] <= '9') {
				value = value * 10 + (ulong)(str[i] - '0');
				i++;
			}

			return value;
		}
	}

	public static class Decode
	{
		static int Base64Value(byte c) {
			if (c >= 'A' && c <= 'Z') return c - 'A';
			if (c >= 'a' && c <= 'z') return c - 'a' + 26;
			if (c >= '0' && c <= '9') return c - '0' + 52;
			if (c == '+') return 62;
			if (c == '/') return 63;
			return 64;
		}

		static bool IsSpace(byte c) => c == ' ' || (c >= '\t' && c <= '\r');

		static int NextValue(ReadOnlySpan<byte> input, ref int pos) {
			while (pos < input.Length) {
				byte c = input[pos++];
				int value = Base64Value(c);
				if (value != 64)
					return value;
				if (IsSpace(c))
					continue;
				if (c == '=')
					return -1;
				throw new FormatException("base64 decode error");
			}
			return -1;
		}

		public static void Base64(ReadOnlySpan<byte> input, List<byte> output) {
			output.Clear();
			try {
				int pos = 0;
				while (true) {
					int a = NextValue(input, ref pos);
					if (a < 0)
						return;
					int b = NextValue(input, ref pos);
					if (b < 0)
						throw new FormatException("base64 decode error");
					output.Add((byte)((a << 2) | (b >> 4)));
					int c = NextValue(input, ref pos);
					if (c < 0)
						return;
					output.Add((byte)((b << 4) | (c >> 2)));
					int d = NextValue(input, ref pos);
					if (d < 0)
						return;
					output.Add((byte)((c << 6) | d));
				}
			}
			catch (FormatException) { }
		}
	}

	public static class Test
	{
		public static bool StartsWith(string str, string strFind) => str.StartsWith(strFind, StringComparison.Ordinal);

		public static bool Contains(string strHaystack, string strNeedle, bool ignoreCaps = false) {
			if (ignoreCaps) {
				string haystack = strHaystack, needle = strNeedle;
				Lower(ref haystack);
				Lower(ref needle);
				return Contains(haystack, needle, false);
			}

			return strHaystack.Contains(strNeedle, StringComparison.Ordinal);
		}

		public static bool ContainsChar(string str, char chr) => str.Contains(chr);

		public static bool ContainsOnly(string str, string chars) {
			foreach (char c in str) {
				if (!ContainsChar(chars, c))
					return false;
			}
			return true;
		}

		public static bool EndsWith(string str, string strFind) {
			int i = str.LastIndexOf(strFind, StringComparison.Ordinal);
			return i != -1 && i == str.Length - strFind.Length;
		}

		public static bool Wildcard(string strWildcard, string strHaystack) {
			int w = 0, s = 0;
			int mp = -1, cp = -1;

			while (s < strHaystack.Length && (w >= strWildcard.Length || strWildcard[w] != '*')) {
				if (w >= strWildcard.Length || (strWildcard[w] != strHaystack[s] && strWildcard[w] != '?'))
					return false;
				w++;
				s++;
			}

			while (s < strHaystack.Length) {
				if (w < strWildcard.Length && strWildcard[w] == '*') {
					if (++w >= strWildcard.Length)
						return true;
					mp = w;
					cp = s + 1;
				}
				else if (w < strWildcard.Length && (strWildcard[w] == strHaystack[s] || strWildcard[w] == '?')) {
					w++;
					s++;
				}
				else {
					if (mp == -1)
						return false;
					w = mp;
					s = cp++;
				}
			}

			while (w < strWildcard.Length && strWildcard[w] == '*')
				w++;

			return w >= strWildcard.Length;
		}
	}

	public static class File
	{
		public static void CleanPath(ref string strIn) {
			FixSlashes(ref strIn);
			Util.Trim(ref strIn, "/");
			Util.FindAndReplace(ref strIn, "//", "/");
		}

		public static void FixSlashes(ref string strIn, string strFrom = "\\", string strTo = "/") => Util.FindAndReplace(ref strIn, strFrom, strTo);

		public static void ExtractFilename(ref string str) {
			int i = str.LastIndexOf('/');
			if (i == -1)
				i = str.LastIndexOf('\\');
			if (i == -1)
				return;

			str = str[(i + 1)..];
		}

		public static void StripFilename(ref string str) {
			int i = str.LastIndexOf('/');
			if (i == -1)
				i = str.LastIndexOf('\\');
			if (i == -1) {
				str = "";
				return;
			}

			str = str[..(i + 1)];
		}

		public static string GetFileExtension(string str) {
			int i = str.LastIndexOf('.');
			if (i == -1)
				return "";

			return str[(i + 1)..];
		}

		public static void StripExtension(ref string str) {
			int i = str.LastIndexOf('.');
			if (i == -1)
				return;

			str = str[..i];
		}
	}

	public static class Util
	{
		public static void FindAndReplace(ref string strIn, string strFind, string strReplace) {
			if (strFind.Length == 0)
				return;

			int pos = 0;
			while ((pos = strIn.IndexOf(strFind, pos, StringComparison.Ordinal)) != -1) {
				strIn = strIn[..pos] + strReplace + strIn[(pos + strFind.Length)..];
				pos += strReplace.Length;
			}
		}

		public static void TrimBefore(ref string str, string strFind, bool includeFind) {
			int i = str.IndexOf(strFind, StringComparison.Ordinal);
			if (i == -1)
				return;
			str = str[(includeFind ? i + strFind.Length : i + 1)..];
		}

		public static void TrimAfter(ref string str, string strFind, bool includeFind) {
			int i = str.IndexOf(strFind, StringComparison.Ordinal);
			if (i == -1)
				return;
			if (!includeFind)
				i += strFind.Length;
			str = str[..i];
		}

		public static void TrimRight(ref string str, string strChars) => str = str.TrimEnd(strChars.ToCharArray());
		public static void TrimLeft(ref string str, string strChars) => str = str.TrimStart(strChars.ToCharArray());

		public static void Trim(ref string str, string strChars = " \n\t\r") {
			TrimLeft(ref str, strChars);
			TrimRight(ref str, strChars);
		}

		public static int Count(string str, char chrFind) {
			int count = 0;
			foreach (char c in str)
				if (c == chrFind)
					count++;
			return count;
		}
	}
}
