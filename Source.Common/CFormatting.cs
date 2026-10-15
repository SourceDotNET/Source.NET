/*
	This file is a bit of a mess. It was an attempt at implementing a C# version of C-style formatting methods.
	However, I think it's kind of lost the plot

	I have some ideas to rework it, I am particularly interested in sprintf/sscanf functions that use https://github.com/FiniteReality/VariadicGenerics
	for their arguments instead, along with not trying to reinvent the wheel and read actual C stdlib implementations.
*/

using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Source.Common;

public enum VariableType
{
	NotApplicable = '\0',
	SignedDecimalInteger = 'i',
	UnsignedDecimalInteger = 'u',
	UnsignedOctal = 'o',
	UnsignedHexadecimalInteger = 'x',
	DecimalFloatingPoint = 'f',
	ScientificNotation = 'e',
	ShortestRepresentation = 'g',
	HexadecimalFloatingPoint = 'a',
	Character = 'c',
	StringOfCharacters = 's',
	PointerAddress = 'p',
	Nothing = 'n'
}

public ref struct SpanWriter<T>
{
	Span<T> input;
	int writePtr;
	bool overflowed;
	public SpanWriter(Span<T> input) {
		this.input = input;
	}

	public SpanWriter<T> Write(in T value) {
		if (writePtr >= input.Length)
			return this;
		input[writePtr++] = value;
		return this;
	}
	public SpanWriter<T> Write(scoped ReadOnlySpan<T> span) {
		if ((writePtr + span.Length) >= input.Length) {
			// We'll clamped-copy it anyway, but set overflow flag
			overflowed = true;
		}
		writePtr += span.ClampedCopyTo(input[writePtr..]);
		return this;
	}

	public readonly int Length => writePtr;
	public readonly bool Overflowed => overflowed;

	public Span<T> ToSpan() => input[..writePtr];
	public static implicit operator Span<T>(SpanWriter<T> self) => self.input[..self.writePtr];
}

public ref struct PrintF
{
	SpanWriter<char> input;
	CFormatReader reader;
	public PrintF(Span<char> input, ReadOnlySpan<char> format) {
		this.input = new(input);
		this.reader = new(format);
		// This gets called to flush any literals so we're immediately at a variable
		// (and any variable writes will do the same)
		WriteAnyLiterals();
	}

	private unsafe void WriteAnyLiterals() {
		Span<char> literalBufferTarget = stackalloc char[64];
		while (!reader.Overflowed()) {
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			int read = reader.ReadLiteral(literalBufferTarget);
			if (read > 0) {
				input.Write(literalBufferTarget[..read]);
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
				continue;
			}

			break;
		}
	}

	public readonly int Length => input.Length;
	public readonly bool Overflowed => input.Overflowed;
	public Span<char> ToSpan() => input.ToSpan();

	public static implicit operator Span<char>(PrintF self) => self.input;
	public static implicit operator int(PrintF self) => self.input.Length;

	public unsafe PrintF D(int i) {
		WriteAnyLiterals();
		reader.ReadVariable(out char t, out int varIdx);
		Span<char> buffer = stackalloc char[11];
		if (i.TryFormat(buffer, out int written))
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			input.Write(buffer[..written]);
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope

		WriteAnyLiterals();
		return this;
	}

	public unsafe PrintF F(float i) {
		WriteAnyLiterals();
		reader.ReadVariable(out char t, out int varIdx);
		Span<char> buffer = stackalloc char[11];
		if (i.TryFormat(buffer, out int written))
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			input.Write(buffer[..written]);
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope

		WriteAnyLiterals();
		return this;
	}

	public unsafe PrintF U(uint i) {
		WriteAnyLiterals();
		reader.ReadVariable(out char t, out int varIdx);
		Span<char> buffer = stackalloc char[11];
		if (i.TryFormat(buffer, out int written))
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			input.Write(buffer[..written]);
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope

		WriteAnyLiterals();
		return this;
	}
	public unsafe PrintF D(long i) {
		WriteAnyLiterals();
		reader.ReadVariable(out char t, out int varIdx);
		Span<char> buffer = stackalloc char[19];
		if (i.TryFormat(buffer, out int written))
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			input.Write(buffer[..written]);
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope

		WriteAnyLiterals();
		return this;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public PrintF I(int i) => D(i);
	public PrintF G(double i) {
		WriteAnyLiterals();
		reader.ReadVariable(out char t, out int varIdx);
		Span<char> buffer = stackalloc char[32];
		if (i.TryFormat(buffer, out int written, t == 'f' ? "F6" : "G6", CultureInfo.InvariantCulture))
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			input.Write(buffer[..written]);
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope

		WriteAnyLiterals();
		return this;
	}
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public PrintF F(double i) => G(i);
	public PrintF C(char c) {
		WriteAnyLiterals();
		reader.ReadVariable(out char t, out int varIdx);
		input.Write([c]);
		WriteAnyLiterals();
		return this;
	}
	public PrintF S(scoped ReadOnlySpan<char> str) {
		if (reader.ReadVariable(out char type, out int variableIdx)) {
			input.Write(str.SliceNullTerminatedString());
			WriteAnyLiterals();
		}
		return this;
	}
}


public ref struct ScanF
{
	public CFormatReader Format;
	ReadOnlySpan<char> input;
	int readArguments = 0;
	public readonly int ReadArguments => readArguments - invalidArguments;
	public readonly bool ReadAny => readArguments > 0;

	public ScanF(ReadOnlySpan<char> input, ReadOnlySpan<char> format) {
		Format = new(format);
		this.input = input.SliceNullTerminatedString();
	}
	public readonly bool Overflowed => Format.Overflowed();

	// NOTE: Only unsafe because C# can't tell we're never going to lose track of the stack allocated buffer target
	// (which is needed to read out a literal)
	private unsafe bool TryReadVariable(Span<char> incoming, out char type, out int len) {
		type = '\0';
		len = 0;
		if (Format.Overflowed() || input.Length <= 0)
			return false;

		// Read until a variable or overflow
		Span<char> literalBufferTarget = stackalloc char[64];
		while (!Format.Overflowed() && input.Length > 0) {
#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			int read = Format.ReadLiteral(literalBufferTarget);
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			if (read > 0) {
				input = input[read..]; // slice so we're aligned
				continue;
			}

			break;
		}

		if (Format.ReadVariable(out type, out int variableIdx)) {
			readArguments = variableIdx + 1;
			int writePtr = 0;
			bool hasWarned = false;
			while (input.Length > 0) {
				char c = input[0];
				if (c == ' ')
					break;
				input = input[1..];
				if (writePtr < incoming.Length) // don't overflow but continue to read to not break further arguments
					incoming[writePtr++] = c;
				else if (!hasWarned) {
					Warning($"Overflowed scanf (max: {incoming.Length}). Consider increasing max here! Truncated argument will be provided.\n");
					hasWarned = true;
				}
			}
			len = writePtr;
			return true;
		}

		return false;
	}
	public const int DEFAULT_SCANF_MAX = 256;
	public unsafe ScanF Read(out int i, int max = DEFAULT_SCANF_MAX) {
		Span<char> incoming = stackalloc char[max];
		i = default;
		int iAttempt;

#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
		if (TryReadVariable(incoming, out char type, out int len))
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			switch (type) {
				case 'd':
				case (char)VariableType.SignedDecimalInteger:
					i = int.TryParse(incoming[..len], out iAttempt) ? iAttempt : default;
					break;
				case (char)VariableType.UnsignedHexadecimalInteger:
					i = int.TryParse(incoming[..len], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out iAttempt) ? iAttempt : default;
					break;
				default: throw new NotImplementedException();
			}

		return this;
	}
	int invalidArguments = 0;
	public unsafe ScanF Read(out float i, int max = DEFAULT_SCANF_MAX) {
		Span<char> incoming = stackalloc char[max];
		i = default;
		float iAttempt;

#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
		if (TryReadVariable(incoming, out char type, out int len))
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			switch (type) {
				case (char)VariableType.DecimalFloatingPoint:
					if (float.TryParse(incoming[..len], out iAttempt))
						i = iAttempt;
					else
						invalidArguments++;
					break;
				default: throw new NotImplementedException();
			}

		return this;
	}
}

public ref struct CFormatReader
{
	int formatReader = 0;
	bool haltedAtVariable;
	int variableIdx;
	readonly ReadOnlySpan<char> format;

	public CFormatReader(ReadOnlySpan<char> format) {
		this.format = format;
	}

	public bool Overflowed() => formatReader >= format.Length || format[formatReader] == '\0';

	/// <summary>
	/// Reads one literal into a read target buffer.
	/// </summary>
	/// <param name="readTarget">A target buffer</param>
	/// <returns>How much was written to the target. >0 if any literal was read at all. 0 if a halt break occured due to an incoming variable or end-of-stream.</returns>
	public int ReadLiteral(Span<char> readTarget) {
		if (haltedAtVariable)
			return 0;

		int chars = 0;
		for (int i = 0; i < readTarget.Length; i++) {
			if (Overflowed())
				return chars;

			char c = format[formatReader];
			if (c == '%') {
				i++;
				if (formatReader + 1 < format.Length && format[formatReader + 1] != '%' && format[formatReader + 1] != '\0') {
					// We have to stop, a variable was reached
					formatReader++;
					haltedAtVariable = true;
					break; // We wrote something if i > 0, even if we guarded here
				}
			}
			// Otherwise, write
			readTarget[chars++] = c;
			formatReader++;
		}

		return chars;
	}
	/// <summary>
	/// Tries to read one variable type. ReadLiteral must have triggered a halt before this function will return true.
	/// </summary>
	/// <param name="type">Out variable type</param>
	/// <param name="variableIdx">Out variable index</param>
	/// <returns>True if a variable could be read. False if a halt break hasn't occured, or if the format pointer overflowed</returns>
	public bool ReadVariable(out char type, out int variableIdx) {
		type = (char)VariableType.NotApplicable;
		variableIdx = -1;
		if (!haltedAtVariable)
			return false;
		if (Overflowed())
			return false;

		type = char.ToLowerInvariant(format[formatReader++]);
		variableIdx = this.variableIdx;
		this.variableIdx++;
		haltedAtVariable = false;
		return true;
	}
}

public static class CFormatUtils
{
	public static int WriteFormattedObject(Span<char> writeBuffer, char type, object? arg) {
		int writeSize = 0;

		VariableType varType;
		if (type == 'd')
			varType = VariableType.SignedDecimalInteger;
		else
			varType = (VariableType)type;

		switch (arg) {
			case int v when varType == VariableType.SignedDecimalInteger:
				v.TryFormat(writeBuffer, out writeSize);
				break;
			case uint v when varType == VariableType.UnsignedDecimalInteger:
				v.TryFormat(writeBuffer, out writeSize);
				break;
			case uint v when varType == VariableType.UnsignedOctal:
				string uoctal = Convert.ToString(v, 8); // ugh
				uoctal.CopyTo(writeBuffer);
				writeSize = uoctal.Length;
				break;
			case uint v when varType == VariableType.UnsignedHexadecimalInteger:
				v.TryFormat(writeBuffer, out writeSize, "x");
				break;
			case double v when varType == VariableType.DecimalFloatingPoint:
				v.TryFormat(writeBuffer, out writeSize, "F");
				break;
			case double v when varType == VariableType.ScientificNotation:
				v.TryFormat(writeBuffer, out writeSize, "E");
				break;
			case double v when varType == VariableType.ShortestRepresentation:
				v.TryFormat(writeBuffer, out writeSize, "G");
				break;
			case double v when varType == VariableType.HexadecimalFloatingPoint:
				var bits = BitConverter.DoubleToInt64Bits(v);
				writeSize = bits.TryFormat(writeBuffer, out var written, "X") ? written : 0;
				break;
			case char v when varType == VariableType.Character:
				writeBuffer[0] = v;
				writeSize = 1;
				break;
			case string v when varType == VariableType.StringOfCharacters:
				v.AsSpan().CopyTo(writeBuffer);
				writeSize = v.Length;
				break;
			case object v when varType == VariableType.PointerAddress:
				var ptrStr = $"0x{v.GetHashCode():X}";
				ptrStr.AsSpan().CopyTo(writeBuffer);
				writeSize = ptrStr.Length;
				break;
			case object _ when varType == VariableType.Nothing:
				break;
			default:
				break;
		}
		return writeSize;
	}
}

public static class CharSpExts
{
	extension(ReadOnlySpan<char> text)
	{
		/// <summary>
		/// A better method than purely <see cref="ReadOnlySpan{T}.IsEmpty"/> - which only validates that the length of the buffer is zero. This method
		/// checks both the buffer length being greater than zero, and if the first character is a null ('\0') or not.
		/// </summary>
		public bool IsStringEmpty => text.IsEmpty || text[0] == '\0';
	}

	extension(Span<char> text)
	{
		/// <summary>
		/// A better method than purely <see cref="Span{T}.IsEmpty"/> - which only validates that the length of the buffer is zero. This method
		/// checks both the buffer length being greater than zero, and if the first character is a null ('\0') or not.
		/// </summary>
		public bool IsStringEmpty => text.IsEmpty || text[0] == '\0';
	}
}

public static class CFormatting
{
	/// <summary>
	/// Performs the following:
	/// <br/>
	/// 1. Determines the null terminator of <paramref name="str"/>
	/// <br/>
	/// 2. Determines the maximum length (<paramref name="target"/>.Length, <paramref name="str"/>.Length)
	/// <br/>
	/// 3. Copies str[..maxLen] to target fully
	/// <br/>
	/// 4. Writes '\0' at target[maxLen]
	/// <br/>
	/// 5. Returns maxLen
	/// </summary>
	/// <param name="target"></param>
	/// <param name="str"></param>
	/// <returns></returns>
	public static float strtof(ReadOnlySpan<char> input, out ReadOnlySpan<char> output) {
		int i = 0;
		while (i < input.Length && input[i] is ' ' or '\t' or '\n' or '\r' or '\f' or '\v')
			i++;

		int start = i;
		if (i < input.Length && (input[i] == '+' || input[i] == '-'))
			i++;

		int mantissaStart = i;
		bool seenDot = false;
		while (i < input.Length && (char.IsAsciiDigit(input[i]) || (input[i] == '.' && !seenDot))) {
			if (input[i] == '.')
				seenDot = true;
			i++;
		}

		if (i > mantissaStart && i < input.Length && (input[i] == 'e' || input[i] == 'E')) {
			int expStart = i;
			i++;
			if (i < input.Length && (input[i] == '+' || input[i] == '-'))
				i++;
			if (i < input.Length && char.IsAsciiDigit(input[i])) {
				while (i < input.Length && char.IsAsciiDigit(input[i]))
					i++;
			}
			else
				i = expStart;
		}

		if (float.TryParse(input[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out float ret)) {
			output = input[i..];
			return ret;
		}
		output = input;
		return 0;
	}

	public static double strtod(ReadOnlySpan<char> input, out ReadOnlySpan<char> output) {
		int i = 0;
		while (i < input.Length && input[i] is ' ' or '\t' or '\n' or '\r' or '\f' or '\v')
			i++;

		int start = i;
		if (i < input.Length && (input[i] == '+' || input[i] == '-'))
			i++;

		int mantissaStart = i;
		bool seenDot = false;
		while (i < input.Length && (char.IsAsciiDigit(input[i]) || (input[i] == '.' && !seenDot))) {
			if (input[i] == '.')
				seenDot = true;
			i++;
		}

		if (i > mantissaStart && i < input.Length && (input[i] == 'e' || input[i] == 'E')) {
			int expStart = i;
			i++;
			if (i < input.Length && (input[i] == '+' || input[i] == '-'))
				i++;
			if (i < input.Length && char.IsAsciiDigit(input[i])) {
				while (i < input.Length && char.IsAsciiDigit(input[i]))
					i++;
			}
			else
				i = expStart;
		}

		if (double.TryParse(input[start..i], NumberStyles.Float, CultureInfo.InvariantCulture, out double ret)) {
			output = input[i..];
			return ret;
		}
		output = input;
		return 0;
	}

	public static double atof(ReadOnlySpan<char> str) => strtod(str, out _);

	// TODO: I really don't like these... we need to really give sprintf and other
	// similar functions some love.
	public static string FormatFixed(double value, int precision) {
		long bits = BitConverter.DoubleToInt64Bits(value);
		bool negative = bits < 0;
		long fraction = bits & 0xFFFFFFFFFFFFFL;
		int exponent = (int)((bits >> 52) & 0x7FF);

		if (exponent == 0x7FF) {
			if (fraction == 0)
				return negative ? "-inf" : "inf";
			if ((fraction & 0x8000000000000L) == 0)
				return negative ? "-nan(snan)" : "nan(snan)";
			if (negative && fraction == 0x8000000000000L)
				return "-nan(ind)";
			return negative ? "-nan" : "nan";
		}

		BigInteger mantissa = exponent == 0 ? fraction : fraction | (1L << 52);
		int shift = (exponent == 0 ? 1 : exponent) - 1075;
		BigInteger scaled;
		if (shift >= 0)
			scaled = (mantissa << shift) * BigInteger.Pow(10, precision);
		else {
			BigInteger num = mantissa * BigInteger.Pow(10, precision);
			BigInteger den = BigInteger.One << -shift;
			scaled = BigInteger.DivRem(num, den, out BigInteger rem);
			int cmp = (rem << 1).CompareTo(den);
			if (cmp > 0 || (cmp == 0 && !scaled.IsEven))
				scaled += 1;
		}

		string digits = scaled.ToString(CultureInfo.InvariantCulture);
		if (digits.Length <= precision)
			digits = new string('0', precision - digits.Length + 1) + digits;

		string result = precision > 0 ? $"{digits[..^precision]}.{digits[^precision..]}" : digits;
		return negative ? "-" + result : result;
	}

	public static string FormatG(double value, int precision = 6) {
		long bits = BitConverter.DoubleToInt64Bits(value);
		bool negative = bits < 0;
		long fraction = bits & 0xFFFFFFFFFFFFFL;
		int exponent = (int)((bits >> 52) & 0x7FF);

		if (exponent == 0x7FF)
			return FormatFixed(value, 0);

		if (precision == 0)
			precision = 1;

		if (exponent == 0 && fraction == 0)
			return negative ? "-0" : "0";

		BigInteger num = exponent == 0 ? fraction : fraction | (1L << 52);
		BigInteger den = BigInteger.One;
		int shift = (exponent == 0 ? 1 : exponent) - 1075;
		if (shift >= 0)
			num <<= shift;
		else
			den <<= -shift;

		int exp10 = (int)Math.Floor(BigInteger.Log10(num) - BigInteger.Log10(den));
		if (exp10 >= 0 ? num < den * BigInteger.Pow(10, exp10) : num * BigInteger.Pow(10, -exp10) < den)
			exp10--;
		else if (exp10 + 1 >= 0 ? num >= den * BigInteger.Pow(10, exp10 + 1) : num * BigInteger.Pow(10, -(exp10 + 1)) >= den)
			exp10++;

		int scale = precision - 1 - exp10;
		BigInteger sn = scale >= 0 ? num * BigInteger.Pow(10, scale) : num;
		BigInteger sd = scale >= 0 ? den : den * BigInteger.Pow(10, -scale);
		BigInteger digits = BigInteger.DivRem(sn, sd, out BigInteger rem);

		int cmp = (rem << 1).CompareTo(sd);
		if (cmp > 0 || (cmp == 0 && !digits.IsEven))
			digits += 1;

		if (digits >= BigInteger.Pow(10, precision)) {
			digits /= 10;
			exp10++;
		}

		string d = digits.ToString(CultureInfo.InvariantCulture);
		string result;
		if (exp10 < -4 || exp10 >= precision) {
			string mant = d.Length > 1 ? $"{d[0]}.{d[1..]}".TrimEnd('0').TrimEnd('.') : d;
			int absExp = Math.Abs(exp10);
			result = $"{mant}e{(exp10 < 0 ? '-' : '+')}{(absExp < 10 ? "0" : "")}{absExp}";
		}
		else if (exp10 >= 0) {
			string whole = d[..(exp10 + 1)];
			string frac = d[(exp10 + 1)..].TrimEnd('0');
			result = frac.Length > 0 ? $"{whole}.{frac}" : whole;
		}
		else {
			string frac = (new string('0', -exp10 - 1) + d).TrimEnd('0');
			result = $"0.{frac}";
		}

		return negative ? $"-{result}" : result;
	}

	public static int ScanFloats(ReadOnlySpan<char> str, Span<float> values) {
		str = str.SliceNullTerminatedString();
		for (int i = 0; i < values.Length; i++) {
			float value = strtof(str, out ReadOnlySpan<char> rest);
			if (rest.Length == str.Length)
				return i;
			values[i] = value;
			str = rest;
		}
		return values.Length;
	}

	public static bool nexttoken(out ReadOnlySpan<char> token, ReadOnlySpan<char> str, char sep, out ReadOnlySpan<char> next) {
		if (str.IsEmpty) {
			token = default;
			next = default;
			return false;
		}

		int i = str.IndexOf(sep);
		if (i < 0) {
			token = str;
			next = default;
			return true;
		}

		token = str[..i];
		next = str[(i + 1)..];
		return true;
	}

	// C atoi: skip leading whitespace, optional sign, parse leading decimal digits, stop at first non-digit.
	public static int atoi(ReadOnlySpan<char> str) {
		int i = 0;
		while (i < str.Length && str[i] is ' ' or '\t' or '\n' or '\r' or '\f' or '\v')
			i++;

		int sign = 1;
		if (i < str.Length && (str[i] == '+' || str[i] == '-')) {
			if (str[i] == '-')
				sign = -1;
			i++;
		}

		long val = 0;
		while (i < str.Length && str[i] >= '0' && str[i] <= '9') {
			val = val * 10 + (str[i] - '0');
			if (val > int.MaxValue)
				return sign < 0 ? int.MinValue : int.MaxValue;
			i++;
		}

		return (int)(sign * val);
	}


	public static void strlower(Span<char> str) {
		for (int i = 0; i < str.Length; i++)
			str[i] = char.ToLower(str[i]);
	}

	public static void strupper(Span<char> str) {
		for (int i = 0; i < str.Length; i++)
			str[i] = char.ToUpper(str[i]);
	}

	public static int strcmp(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b) => a.SliceNullTerminatedString().CompareTo(b.SliceNullTerminatedString(), StringComparison.Ordinal);
	public static int strncmp(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b, int c) => a.SliceNullTerminatedString().SliceSafe(c).CompareTo(b.SliceNullTerminatedString().SliceSafe(c), StringComparison.Ordinal);
	public static int strnicmp(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b, int c) => stricmp(a.SliceNullTerminatedString().SliceSafe(c), b.SliceNullTerminatedString().SliceSafe(c));
	public static int stricmp(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b) {
		a = a.SliceNullTerminatedString(); b = b.SliceNullTerminatedString();
		for (int i = 0; i < a.Length && i < b.Length; i++)
			if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[i]))
				return char.ToLowerInvariant(a[i]) - char.ToLowerInvariant(b[i]);
		return a.Length - b.Length;
	}
	public static int stricmp(scoped ReadOnlySpan<byte> a, scoped ReadOnlySpan<byte> b) {
		a = a.SliceNullTerminatedString(); b = b.SliceNullTerminatedString();
		for (int i = 0; i < a.Length && i < b.Length; i++)
			if (char.ToLowerInvariant((char)a[i]) != char.ToLowerInvariant((char)b[i]))
				return char.ToLowerInvariant((char)a[i]) - char.ToLowerInvariant((char)b[i]);
		return a.Length - b.Length;
	}
	public static int strcmpi(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b) => stricmp(a, b);


	public static bool streq(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b) => a.SliceNullTerminatedString().Equals(b.SliceNullTerminatedString(), StringComparison.Ordinal);
	public static bool strieq(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b) => a.SliceNullTerminatedString().Equals(b.SliceNullTerminatedString(), StringComparison.OrdinalIgnoreCase);

	public static nint strlen(scoped ReadOnlySpan<char> str) {
		int i = 0;
		for (i = 0; i < str.Length; i++) {
			if (str[i] == '\0')
				return i;
		}
		return i;
	}

	public static nint strlen(scoped ReadOnlySpan<byte> str) {
		int i = 0;
		for (i = 0; i < str.Length; i++) {
			if (str[i] == '\0')
				return i;
		}
		return i;
	}
	public static int strcpy(scoped Span<byte> target, scoped ReadOnlySpan<byte> str) {
		str = str.SliceNullTerminatedString();
		int len = Math.Min(target.Length, str.Length);
		str[..len].CopyTo(target);
		target[Math.Min(target.Length - 1, str.Length)] = 0;
		return len;
	}
	public static ReadOnlySpan<char> strstr(ReadOnlySpan<char> target, scoped ReadOnlySpan<char> str) {
		if (str.Length == 0) return target;
		if (str.Length > target.Length) return ReadOnlySpan<char>.Empty;

		char first = str[0];

		for (int i = 0; i <= target.Length - str.Length; i++) {
			if (target[i] != first)
				continue;

			if (target.Slice(i, str.Length).Equals(str, StringComparison.Ordinal))
				return target.Slice(i);
		}

		return ReadOnlySpan<char>.Empty;
	}
	public static Span<char> strstr(Span<char> target, scoped ReadOnlySpan<char> str) {
		if (str.Length == 0) return target;
		if (str.Length > target.Length) return Span<char>.Empty;

		char first = str[0];

		for (int i = 0; i <= target.Length - str.Length; i++) {
			if (target[i] != first)
				continue;

			if (target.Slice(i, str.Length).Equals(str, StringComparison.Ordinal))
				return target.Slice(i);
		}

		return Span<char>.Empty;
	}
	public static ReadOnlySpan<char> stristr(ReadOnlySpan<char> target, scoped ReadOnlySpan<char> str) {
		if (str.Length == 0) return target;
		if (str.Length > target.Length) return ReadOnlySpan<char>.Empty;

		char first = char.ToUpperInvariant(str[0]);

		for (int i = 0; i <= target.Length - str.Length; i++) {
			if (char.ToUpperInvariant(target[i]) != first)
				continue;

			if (target.Slice(i, str.Length).Equals(str, StringComparison.OrdinalIgnoreCase))
				return target.Slice(i);
		}

		return ReadOnlySpan<char>.Empty;
	}
	public static ReadOnlySpan<char> strchr(ReadOnlySpan<char> target, char c) {
		for (int i = 0; i < target.Length; i++) {
			if (target[i] == c)
				return target.Slice(i);
		}


		return ReadOnlySpan<char>.Empty;
	}
	public static Span<char> strchr(Span<char> target, char c) {
		for (int i = 0; i < target.Length; i++) {
			if (target[i] == c)
				return target.Slice(i);
		}


		return Span<char>.Empty;
	}
	public static ReadOnlySpan<char> strrchr(ReadOnlySpan<char> target, char c) {
		for (int i = target.Length - 1; i >= 0; i--) {
			if (target[i] == c)
				return target.Slice(i);
		}


		return ReadOnlySpan<char>.Empty;
	}
	public static Span<char> strrchr(Span<char> target, char c) {
		for (int i = target.Length - 1; i >= 0; i--) {
			if (target[i] == c)
				return target.Slice(i);
		}


		return Span<char>.Empty;
	}
	public static Span<char> stristr(Span<char> target, scoped ReadOnlySpan<char> str) {
		if (str.Length == 0) return target;
		if (str.Length > target.Length) return Span<char>.Empty;

		char first = char.ToUpperInvariant(str[0]);

		for (int i = 0; i <= target.Length - str.Length; i++) {
			if (char.ToUpperInvariant(target[i]) != first)
				continue;

			if (target.Slice(i, str.Length).Equals(str, StringComparison.OrdinalIgnoreCase))
				return target.Slice(i);
		}

		return Span<char>.Empty;
	}
	public static int strcpy(scoped Span<char> target, scoped ReadOnlySpan<char> str) {
		str = str.SliceNullTerminatedString();
		int len = Math.Min(target.Length, str.Length);
		str[..len].CopyTo(target);
		target[Math.Min(target.Length - 1, str.Length)] = '\0';
		return len;
	}
	public static int strcat(scoped Span<char> target, scoped ReadOnlySpan<char> str) {
		int targetLen;
		for (targetLen = 0; targetLen < target.Length; targetLen++) {
			if (target[targetLen] == '\0')
				break;
		}

		str = str.SliceNullTerminatedString();
		int len = Math.Min(target.Length - targetLen, str.Length);
		str[..len].CopyTo(target[targetLen..]);
		target[Math.Min(target.Length - 1, targetLen + str.Length)] = '\0';
		return len;
	}
	// This needs to go in the future, but Dbg currently relies on it.
	public static unsafe int sprintf(scoped Span<char> target, ref CFormatReader reader, params ReadOnlySpan<object?> args) {
		int originalSize = target.Length;

		Span<char> buffer = stackalloc char[256];
		while (!reader.Overflowed() && !target.IsEmpty) {
			// Try reading literal

#pragma warning disable CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			Span<char> read = buffer[0..reader.ReadLiteral(buffer[..Math.Min(buffer.Length, target.Length)])];
#pragma warning restore CS9080 // Use of variable in this context may expose referenced variables outside of their declaration scope
			if (read.Length > 0) {
				read.CopyTo(target);
				target = target[read.Length..];
			}
			// Try reading a variable?
			else if (reader.ReadVariable(out char type, out int idx)) {
				if (idx < args.Length) {
					int writtenVar = CFormatUtils.WriteFormattedObject(target, type, args[idx]);
					target = target[writtenVar..];
				}
				else {
					// Skip it
					target[0] = '%';
					target[1] = type;
					target = target[2..];
				}
			}
			else {
				if (!reader.Overflowed())
					throw new FormatException("Unexpected non-overflow, non-variable, and non-literal condition met...");
			}
		}

		return originalSize - target.Length; // Should return the delta length
	}
	public static PrintF sprintf(Span<char> target, ReadOnlySpan<char> format) => new(target, format);
	public static void binarytohex(ReadOnlySpan<byte> input, Span<char> output) {
		for (int i = 0; i < input.Length; i++) input[i].TryFormat(output[(i * 2)..], out _, "x2");
		output[input.Length * 2] = '\0';
	}
}
