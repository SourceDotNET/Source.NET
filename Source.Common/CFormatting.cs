/*
	This file is a bit of a mess. It was an attempt at implementing a C# version of C-style formatting methods.
	However, I think it's kind of lost the plot

	I have some ideas to rework it, I am particularly interested in sprintf/sscanf functions that use https://github.com/FiniteReality/VariadicGenerics
	for their arguments instead, along with not trying to reinvent the wheel and read actual C stdlib implementations.
*/

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

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
	public readonly Span<T> Remaining => input[writePtr..];

	public void Advance(int count, int wanted) {
		if (writePtr + wanted >= input.Length)
			overflowed = true;
		writePtr += count;
	}

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

	PrintF WriteInteger(long value, char natural, bool wide) {
		WriteAnyLiterals();
		reader.ReadSpec(out CFormatSpec spec);
		CFormatOutput output = new(input.Remaining);
		if (CFormatUtils.IsFloatConversion(spec.Type))
			CFormatUtils.WriteFloat(ref output, in spec, value);
		else if (spec.Type is 'c' or 'C')
			CFormatUtils.WriteChar(ref output, in spec, (char)value);
		else {
			if (!CFormatUtils.IsIntegerConversion(spec.Type))
				spec.Type = natural;
			if (wide && spec.Length == CFormatLength.None)
				spec.Length = CFormatLength.LongLong;
			CFormatUtils.WriteInteger(ref output, in spec, value);
		}

		input.Advance(output.Written, output.Total);
		WriteAnyLiterals();
		return this;
	}

	PrintF WriteFloat(double value, char natural) {
		WriteAnyLiterals();
		reader.ReadSpec(out CFormatSpec spec);
		CFormatOutput output = new(input.Remaining);
		if (CFormatUtils.IsIntegerConversion(spec.Type))
			CFormatUtils.WriteInteger(ref output, in spec, (long)value);
		else {
			if (!CFormatUtils.IsFloatConversion(spec.Type))
				spec.Type = natural;
			CFormatUtils.WriteFloat(ref output, in spec, value);
		}

		input.Advance(output.Written, output.Total);
		WriteAnyLiterals();
		return this;
	}

	public PrintF D(int i) => WriteInteger(i, 'd', false);
	public PrintF D(long i) => WriteInteger(i, 'd', true);
	public PrintF I(int i) => WriteInteger(i, 'd', false);
	public PrintF U(uint i) => WriteInteger(i, 'u', false);
	public PrintF F(float i) => WriteFloat(i, 'f');
	public PrintF F(double i) => WriteFloat(i, 'f');
	public PrintF G(double i) => WriteFloat(i, 'g');

	public PrintF C(char c) {
		WriteAnyLiterals();
		reader.ReadSpec(out CFormatSpec spec);
		CFormatOutput output = new(input.Remaining);
		if (CFormatUtils.IsIntegerConversion(spec.Type))
			CFormatUtils.WriteInteger(ref output, in spec, c);
		else
			CFormatUtils.WriteChar(ref output, in spec, c);

		input.Advance(output.Written, output.Total);
		WriteAnyLiterals();
		return this;
	}

	public PrintF S(scoped ReadOnlySpan<char> str) {
		if (reader.ReadSpec(out CFormatSpec spec)) {
			CFormatOutput output = new(input.Remaining);
			if (spec.Type is 's' or 'S')
				CFormatUtils.WriteString(ref output, in spec, str);
			else
				output.Write(str.SliceNullTerminatedString());

			input.Advance(output.Written, output.Total);
			WriteAnyLiterals();
		}
		return this;
	}
}


public ref struct ScanF
{
	public CFormatReader Format;
	ReadOnlySpan<char> input;
	int assigned;
	bool failed;
	bool inputFailure;

	public readonly int ReadArguments => assigned == 0 && inputFailure ? -1 : assigned;
	public readonly bool ReadAny => assigned > 0;

	public ScanF(ReadOnlySpan<char> input, ReadOnlySpan<char> format) {
		Format = new(format);
		this.input = input.SliceNullTerminatedString();
	}
	public readonly bool Overflowed => Format.Overflowed();

	static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\v' or '\f' or '\r';
	static int DigitValue(char c) => c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : 99;

	void SkipSpaces() {
		while (!input.IsEmpty && IsSpace(input[0]))
			input = input[1..];
	}

	bool Fail(bool atEnd) {
		inputFailure = atEnd;
		failed = true;
		return false;
	}

	bool NextConversion(out CFormatSpec spec) {
		spec = default;
		if (failed)
			return false;

		Span<char> literal = stackalloc char[64];
		while (true) {
			for (int read = Format.ReadLiteral(literal); read > 0; read = Format.ReadLiteral(literal)) {
				for (int i = 0; i < read; i++) {
					if (IsSpace(literal[i])) {
						SkipSpaces();
						continue;
					}

					if (input.IsEmpty || input[0] != literal[i])
						return Fail(input.IsEmpty);
					input = input[1..];
				}
			}

			if (!Format.ReadSpec(out spec))
				return Fail(false);
			if (spec.WidthArg < 0)
				return true;

			if (CFormatUtils.IsFloatConversion(spec.Type) ? !ScanFloat(in spec, out _) : !ScanInteger(in spec, out _))
				return false;
		}
	}

	ReadOnlySpan<char> Window(in CFormatSpec spec) {
		SkipSpaces();
		return spec.Width > 0 && spec.Width < input.Length ? input[..spec.Width] : input;
	}

	bool Consume(bool valid, int length) {
		if (!valid)
			return Fail(length >= input.Length);

		input = input[length..];
		return true;
	}

	bool ScanInteger(in CFormatSpec spec, out long value) {
		ReadOnlySpan<char> text = Window(in spec);
		int i = 0;
		bool negative = false;
		if (i < text.Length && text[i] is '+' or '-')
			negative = text[i++] == '-';

		int radix = spec.Type switch {
			'x' or 'X' => 16,
			'o' or 'O' => 8,
			'i' => 0,
			_ => 10
		};

		if (radix is 0 or 16 && i + 1 < text.Length && text[i] == '0' && text[i + 1] is 'x' or 'X') {
			radix = 16;
			i += 2;
		}
		else if (radix == 0)
			radix = i < text.Length && text[i] == '0' ? 8 : 10;

		int start = i;
		ulong number = 0;
		for (; i < text.Length && DigitValue(text[i]) < radix; i++)
			number = unchecked(number * (ulong)radix + (ulong)DigitValue(text[i]));

		value = unchecked(negative ? -(long)number : (long)number);
		return Consume(i > start, i);
	}

	bool ScanFloat(in CFormatSpec spec, out float value) {
		ReadOnlySpan<char> text = Window(in spec);
		int i = 0;
		bool negative = false;
		if (i < text.Length && text[i] is '+' or '-')
			negative = text[i++] == '-';

		bool valid;
		value = 0;
		if (text[i..].StartsWith("infinity", StringComparison.OrdinalIgnoreCase) || text[i..].StartsWith("inf", StringComparison.OrdinalIgnoreCase)) {
			i += text[i..].StartsWith("infinity", StringComparison.OrdinalIgnoreCase) ? 8 : 3;
			value = float.PositiveInfinity;
			valid = true;
		}
		else if (text[i..].StartsWith("nan", StringComparison.OrdinalIgnoreCase)) {
			i += 3;
			value = float.NaN;
			valid = true;
		}
		else if (i + 1 < text.Length && text[i] == '0' && text[i + 1] is 'x' or 'X') {
			i += 2;
			double mantissa = 0;
			int digits = 0;
			int exponent = 0;
			for (; i < text.Length && DigitValue(text[i]) < 16; i++, digits++)
				mantissa = mantissa * 16 + DigitValue(text[i]);
			if (i < text.Length && text[i] == '.') {
				for (i++; i < text.Length && DigitValue(text[i]) < 16; i++, digits++, exponent -= 4)
					mantissa = mantissa * 16 + DigitValue(text[i]);
			}

			valid = digits > 0;
			if (valid && i < text.Length && text[i] is 'p' or 'P') {
				bool negativeExponent = ++i < text.Length && text[i] == '-';
				if (i < text.Length && text[i] is '+' or '-')
					i++;
				int exponentDigits = i;
				int binaryExponent = 0;
				for (; i < text.Length && char.IsAsciiDigit(text[i]); i++)
					binaryExponent = Math.Min(binaryExponent * 10 + (text[i] - '0'), 100000);
				valid = i > exponentDigits;
				exponent += negativeExponent ? -binaryExponent : binaryExponent;
			}

			value = (float)Math.ScaleB(mantissa, exponent);
		}
		else {
			int start = i;
			int digits = 0;
			for (; i < text.Length && char.IsAsciiDigit(text[i]); i++)
				digits++;
			if (i < text.Length && text[i] == '.') {
				for (i++; i < text.Length && char.IsAsciiDigit(text[i]); i++)
					digits++;
			}

			valid = digits > 0;
			if (valid && i < text.Length && text[i] is 'e' or 'E') {
				if (++i < text.Length && text[i] is '+' or '-')
					i++;
				int exponentDigits = i;
				while (i < text.Length && char.IsAsciiDigit(text[i]))
					i++;
				valid = i > exponentDigits;
			}

			if (valid)
				value = float.Parse(text[start..i], NumberStyles.Float, CultureInfo.InvariantCulture);
		}

		if (negative)
			value = -value;
		return Consume(valid, i);
	}

	[UnscopedRef]
	public ref ScanF Read(out int i) {
		i = 0;
		return ref ReadOrKeep(ref i);
	}

	[UnscopedRef]
	public ref ScanF Read(out float i) {
		i = 0;
		return ref ReadOrKeep(ref i);
	}

	[UnscopedRef]
	public ref ScanF ReadOrKeep(ref int i) {
		if (NextConversion(out CFormatSpec spec)) {
			if (!CFormatUtils.IsIntegerConversion(spec.Type))
				throw new NotImplementedException();

			if (ScanInteger(in spec, out long value)) {
				i = unchecked((int)value);
				assigned++;
			}
		}

		return ref this;
	}

	[UnscopedRef]
	public ref ScanF ReadOrKeep(ref float i) {
		if (NextConversion(out CFormatSpec spec)) {
			if (!CFormatUtils.IsFloatConversion(spec.Type))
				throw new NotImplementedException();

			if (ScanFloat(in spec, out float value)) {
				i = value;
				assigned++;
			}
		}

		return ref this;
	}
}

public enum CFormatLength
{
	None,
	Char,
	Short,
	Long,
	LongLong,
	Size,
	LongDouble
}

public struct CFormatSpec
{
	public char Type;
	public CFormatLength Length;
	public int Width;
	public int Precision;
	public int WidthArg;
	public int PrecisionArg;
	public int ValueArg;
	public int TextStart;
	public int TextLength;
	public bool LeftAlign;
	public bool PlusSign;
	public bool SpaceSign;
	public bool AltForm;
	public bool ZeroPad;
}

public ref struct CFormatOutput
{
	readonly Span<char> target;
	readonly int skip;
	int position;
	int written;

	public CFormatOutput(Span<char> target, int skip = 0) {
		this.target = target;
		this.skip = skip;
	}

	public readonly int Written => written;
	public readonly int Total => position;

	public void Write(char c) {
		if (position >= skip && written < target.Length)
			target[written++] = c;
		position++;
	}

	public void Write(scoped ReadOnlySpan<char> text) {
		for (int i = 0; i < text.Length; i++)
			Write(text[i]);
	}

	public void Fill(char c, int count) {
		for (int i = 0; i < count; i++)
			Write(c);
	}
}

public ref struct CFormatReader
{
	int formatReader = 0;
	bool haltedAtVariable;
	int variableIdx;
	readonly ReadOnlySpan<char> format;
	public int ResumeOffset;

	public CFormatReader(ReadOnlySpan<char> format) {
		this.format = format;
	}

	public bool Overflowed() => formatReader >= format.Length || format[formatReader] == '\0';
	public readonly ReadOnlySpan<char> GetText(in CFormatSpec spec) => format.Slice(spec.TextStart, spec.TextLength);

	/// <summary>
	/// Reads one literal into a read target buffer.
	/// </summary>
	/// <param name="readTarget">A target buffer</param>
	/// <returns>How much was written to the target. >0 if any literal was read at all. 0 if a halt break occured due to an incoming variable or end-of-stream.</returns>
	public int ReadLiteral(scoped Span<char> readTarget) {
		if (haltedAtVariable)
			return 0;

		int chars = 0;
		while (chars < readTarget.Length && !Overflowed()) {
			char c = format[formatReader];
			if (c == '%' && formatReader + 1 < format.Length && format[formatReader + 1] != '\0') {
				formatReader++;
				if (format[formatReader] != '%') {
					haltedAtVariable = true;
					break;
				}
			}

			readTarget[chars++] = c;
			formatReader++;
		}

		return chars;
	}

	bool ReadPrefix(ReadOnlySpan<char> prefix) {
		if (!format[formatReader..].StartsWith(prefix))
			return false;

		formatReader += prefix.Length;
		return true;
	}

	bool ReadNumber(out int value) {
		int start = formatReader;
		long number = 0;
		while (!Overflowed() && char.IsAsciiDigit(format[formatReader]))
			number = Math.Min(number * 10 + (format[formatReader++] - '0'), int.MaxValue);

		value = (int)number;
		return formatReader > start;
	}

	CFormatLength ReadLength() {
		if (ReadPrefix("hh")) return CFormatLength.Char;
		if (ReadPrefix("h")) return CFormatLength.Short;
		if (ReadPrefix("ll") || ReadPrefix("I64") || ReadPrefix("j")) return CFormatLength.LongLong;
		if (ReadPrefix("l")) return CFormatLength.Long;
		if (ReadPrefix("I32") || ReadPrefix("w")) return CFormatLength.None;
		if (ReadPrefix("z") || ReadPrefix("t") || ReadPrefix("I")) return CFormatLength.Size;
		if (ReadPrefix("L")) return CFormatLength.LongDouble;
		return CFormatLength.None;
	}

	public bool ReadSpec(out CFormatSpec spec) {
		spec = new() { Width = -1, Precision = -1, WidthArg = -1, PrecisionArg = -1, ValueArg = -1 };
		if (!haltedAtVariable)
			return false;

		spec.TextStart = formatReader - 1;
		for (; !Overflowed(); formatReader++) {
			char flag = format[formatReader];
			if (flag == '-') spec.LeftAlign = true;
			else if (flag == '+') spec.PlusSign = true;
			else if (flag == ' ') spec.SpaceSign = true;
			else if (flag == '#') spec.AltForm = true;
			else if (flag == '0') spec.ZeroPad = true;
			else break;
		}

		if (ReadPrefix("*"))
			spec.WidthArg = variableIdx++;
		else if (ReadNumber(out int width))
			spec.Width = width;

		if (ReadPrefix(".")) {
			if (ReadPrefix("*"))
				spec.PrecisionArg = variableIdx++;
			else {
				ReadNumber(out int precision);
				spec.Precision = precision;
			}
		}

		spec.Length = ReadLength();
		if (!Overflowed()) {
			spec.Type = format[formatReader++];
			if (CFormatUtils.IsConversion(spec.Type))
				spec.ValueArg = variableIdx++;
		}

		spec.TextLength = formatReader - spec.TextStart;
		haltedAtVariable = false;
		return true;
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

		ReadSpec(out CFormatSpec spec);
		type = char.ToLowerInvariant(spec.Type);
		variableIdx = spec.ValueArg >= 0 ? spec.ValueArg : this.variableIdx++;
		return true;
	}
}

public static class CFormatUtils
{
#if WIN32
	const int LONG_BITS = 32;
#else
	const int LONG_BITS = 64;
#endif

	public static bool IsIntegerConversion(char type) => type is 'd' or 'i' or 'u' or 'o' or 'x' or 'X' or 'D' or 'U' or 'O';
	public static bool IsFloatConversion(char type) => type is 'f' or 'F' or 'e' or 'E' or 'g' or 'G' or 'a' or 'A';
	public static bool IsConversion(char type) => IsIntegerConversion(type) || IsFloatConversion(type) || type is 'c' or 'C' or 's' or 'S' or 'p' or 'n';

	static void WritePadded(ref CFormatOutput output, in CFormatSpec spec, scoped ReadOnlySpan<char> prefix, int zeros, scoped ReadOnlySpan<char> body, bool zeroPad) {
		int padding = Math.Max(0, spec.Width - prefix.Length - zeros - body.Length);
		if (spec.LeftAlign) {
			output.Write(prefix);
			output.Fill('0', zeros);
			output.Write(body);
			output.Fill(' ', padding);
			return;
		}

		if (!zeroPad)
			output.Fill(' ', padding);
		output.Write(prefix);
		output.Fill('0', zeroPad ? zeros + padding : zeros);
		output.Write(body);
	}

	public static void WriteInteger(ref CFormatOutput output, in CFormatSpec spec, long value) {
		int bits = spec.Length switch {
			CFormatLength.Char => 8,
			CFormatLength.Short => 16,
			CFormatLength.Long => LONG_BITS,
			CFormatLength.LongLong or CFormatLength.Size or CFormatLength.LongDouble => 64,
			_ => 32
		};

		bool signed = spec.Type is 'd' or 'i' or 'D';
		bool negative = false;
		ulong magnitude;
		if (signed) {
			long narrowed = value << (64 - bits) >> (64 - bits);
			negative = narrowed < 0;
			magnitude = negative ? unchecked((ulong)-narrowed) : (ulong)narrowed;
		}
		else
			magnitude = bits == 64 ? unchecked((ulong)value) : unchecked((ulong)value) & ((1UL << bits) - 1);

		uint radix = spec.Type switch {
			'x' or 'X' => 16,
			'o' or 'O' => 8,
			_ => 10
		};

		ReadOnlySpan<char> alphabet = spec.Type == 'X' ? "0123456789ABCDEF" : "0123456789abcdef";
		Span<char> digits = stackalloc char[24];
		int start = digits.Length;
		for (ulong rest = magnitude; rest != 0; rest /= radix)
			digits[--start] = alphabet[(int)(rest % radix)];

		int digitCount = digits.Length - start;
		int minDigits = spec.Precision >= 0 ? spec.Precision : 1;
		if (radix == 8 && spec.AltForm && minDigits <= digitCount)
			minDigits = digitCount + 1;

		ReadOnlySpan<char> prefix = "";
		if (signed)
			prefix = negative ? "-" : spec.PlusSign ? "+" : spec.SpaceSign ? " " : "";
		else if (radix == 16 && spec.AltForm && magnitude != 0)
			prefix = spec.Type == 'X' ? "0X" : "0x";

		WritePadded(ref output, in spec, prefix, Math.Max(0, minDigits - digitCount), digits[start..], spec.ZeroPad && spec.Precision < 0);
	}

	public static void WriteFloat(ref CFormatOutput output, in CFormatSpec spec, double value) {
		char type = char.ToLowerInvariant(spec.Type);
		bool upper = type != spec.Type;
		bool finite = double.IsFinite(value);
		string body;
		if (!finite)
			body = FormatFixed(value, 0);
		else {
			double magnitude = Math.Abs(value);
			int precision = spec.Precision >= 0 ? spec.Precision : 6;
			body = type switch {
				'e' => FormatExponent(magnitude, precision, spec.AltForm),
				'g' => FormatGeneral(magnitude, precision, spec.AltForm),
				'a' => FormatHex(magnitude, spec.Precision, spec.AltForm),
				_ => spec.AltForm && precision == 0 ? FormatFixed(magnitude, precision) + "." : FormatFixed(magnitude, precision)
			};
		}

		Span<char> prefix = stackalloc char[3];
		int prefixLength = 0;
		if (finite ? double.IsNegative(value) : body[0] == '-')
			prefix[prefixLength++] = '-';
		else if (spec.PlusSign)
			prefix[prefixLength++] = '+';
		else if (spec.SpaceSign)
			prefix[prefixLength++] = ' ';

		if (finite && type == 'a') {
			prefix[prefixLength++] = '0';
			prefix[prefixLength++] = upper ? 'X' : 'x';
		}

		if (upper)
			body = body.ToUpperInvariant();

		WritePadded(ref output, in spec, prefix[..prefixLength], 0, body.AsSpan(body[0] == '-' ? 1 : 0), finite && spec.ZeroPad);
	}

	public static void WriteString(ref CFormatOutput output, in CFormatSpec spec, scoped ReadOnlySpan<char> text) {
		text = text.SliceNullTerminatedString();
		if (spec.Precision >= 0 && text.Length > spec.Precision)
			text = text[..spec.Precision];

		WritePadded(ref output, in spec, "", 0, text, spec.ZeroPad);
	}

	public static void WriteChar(ref CFormatOutput output, in CFormatSpec spec, char value) {
		WritePadded(ref output, in spec, "", 0, new ReadOnlySpan<char>(in value), spec.ZeroPad);
	}

	static bool TryGetInteger(object? arg, out long value) {
		switch (arg) {
			case int v: value = v; return true;
			case uint v: value = v; return true;
			case long v: value = v; return true;
			case ulong v: value = unchecked((long)v); return true;
			case short v: value = v; return true;
			case ushort v: value = v; return true;
			case sbyte v: value = v; return true;
			case byte v: value = v; return true;
			case char v: value = v; return true;
			case bool v: value = v ? 1 : 0; return true;
			case nint v: value = v; return true;
			case nuint v: value = unchecked((long)v); return true;
			case float v: value = (long)v; return true;
			case double v: value = (long)v; return true;
			case Enum v: value = v.GetTypeCode() == TypeCode.UInt64 ? unchecked((long)Convert.ToUInt64(v)) : Convert.ToInt64(v); return true;
			default: value = 0; return false;
		}
	}

	static bool TryGetFloat(object? arg, out double value) {
		switch (arg) {
			case double v: value = v; return true;
			case float v: value = v; return true;
			case ulong v: value = v; return true;
			default:
				bool converted = TryGetInteger(arg, out long integer);
				value = integer;
				return converted;
		}
	}

	public static void WriteObject(ref CFormatOutput output, in CFormatSpec spec, object? arg) {
		switch (spec.Type) {
			case 'c' or 'C':
				if (TryGetInteger(arg, out long character))
					WriteChar(ref output, in spec, (char)character);
				break;
			case 's' or 'S':
				switch (arg) {
					case null: WriteString(ref output, in spec, "(null)"); break;
					case string text: WriteString(ref output, in spec, text); break;
					case char[] text: WriteString(ref output, in spec, text); break;
					case ReadOnlyMemory<char> text: WriteString(ref output, in spec, text.Span); break;
					case Memory<char> text: WriteString(ref output, in spec, text.Span); break;
					case IFormattable text: WriteString(ref output, in spec, text.ToString(null, CultureInfo.InvariantCulture)); break;
					default: WriteString(ref output, in spec, arg.ToString()); break;
				}
				break;
			case 'p':
				if (arg != null)
					WritePadded(ref output, in spec, "", 0, $"0x{arg.GetHashCode():X}", false);
				break;
			case 'n':
				break;
			default:
				if (IsFloatConversion(spec.Type)) {
					if (TryGetFloat(arg, out double number))
						WriteFloat(ref output, in spec, number);
				}
				else if (TryGetInteger(arg, out long integer))
					WriteInteger(ref output, in spec, integer);
				break;
		}
	}

	public static void WriteArgument(ref CFormatOutput output, in CFormatSpec source, scoped ReadOnlySpan<char> text, scoped ReadOnlySpan<object?> args) {
		if (source.ValueArg < 0 || source.ValueArg >= args.Length) {
			output.Write(text);
			return;
		}

		CFormatSpec spec = source;
		if (spec.WidthArg >= 0 && TryGetInteger(args[spec.WidthArg], out long width)) {
			spec.LeftAlign |= width < 0;
			spec.Width = (int)Math.Abs(Math.Clamp(width, -int.MaxValue, int.MaxValue));
		}

		if (spec.PrecisionArg >= 0 && TryGetInteger(args[spec.PrecisionArg], out long precision))
			spec.Precision = precision < 0 ? -1 : (int)Math.Min(precision, int.MaxValue);

		WriteObject(ref output, in spec, args[spec.ValueArg]);
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

	static string SignificantDigits(double value, int precision, out int exp10) {
		long bits = BitConverter.DoubleToInt64Bits(value);
		long fraction = bits & 0xFFFFFFFFFFFFFL;
		int exponent = (int)((bits >> 52) & 0x7FF);
		exp10 = 0;
		if (exponent == 0 && fraction == 0)
			return new string('0', precision);

		BigInteger num = exponent == 0 ? fraction : fraction | (1L << 52);
		BigInteger den = BigInteger.One;
		int shift = (exponent == 0 ? 1 : exponent) - 1075;
		if (shift >= 0)
			num <<= shift;
		else
			den <<= -shift;

		exp10 = (int)Math.Floor(BigInteger.Log10(num) - BigInteger.Log10(den));
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

		return digits.ToString(CultureInfo.InvariantCulture);
	}

	static string WithExponent(string mantissa, int exp10) {
		int absExp = Math.Abs(exp10);
		return $"{mantissa}e{(exp10 < 0 ? '-' : '+')}{(absExp < 10 ? "0" : "")}{absExp}";
	}

	internal static string FormatExponent(double value, int precision, bool forcePoint) {
		string d = SignificantDigits(value, precision + 1, out int exp10);
		return WithExponent(precision > 0 ? $"{d[0]}.{d[1..]}" : forcePoint ? $"{d}." : d, exp10);
	}

	internal static string FormatGeneral(double value, int precision, bool keepZeros) {
		if (!double.IsFinite(value))
			return FormatFixed(value, 0);

		if (precision == 0)
			precision = 1;

		string d = SignificantDigits(Math.Abs(value), precision, out int exp10);
		bool exponential = exp10 < -4 || exp10 >= precision;
		string result;
		if (exponential)
			result = $"{d[0]}.{d[1..]}";
		else if (exp10 >= 0)
			result = $"{d[..(exp10 + 1)]}.{d[(exp10 + 1)..]}";
		else
			result = $"0.{new string('0', -exp10 - 1)}{d}";

		if (!keepZeros)
			result = result.TrimEnd('0').TrimEnd('.');
		if (exponential)
			result = WithExponent(result, exp10);

		return double.IsNegative(value) ? $"-{result}" : result;
	}

	public static string FormatG(double value, int precision = 6) => FormatGeneral(value, precision, false);

	internal static string FormatHex(double value, int precision, bool forcePoint) {
		long bits = BitConverter.DoubleToInt64Bits(value);
		int exponent = (int)((bits >> 52) & 0x7FF);
		ulong mantissa = (ulong)bits & 0xFFFFFFFFFFFFFUL;
		int exp2 = exponent - 1023;
		if (exponent != 0)
			mantissa |= 1UL << 52;
		else
			exp2 = mantissa == 0 ? 0 : -1022;

		int digits = 13;
		if (precision >= 0 && precision < 13) {
			int drop = (13 - precision) * 4;
			ulong half = 1UL << (drop - 1);
			ulong rest = mantissa & ((1UL << drop) - 1);
			mantissa >>= drop;
			if (rest > half || (rest == half && (mantissa & 1) != 0))
				mantissa++;
			digits = precision;
		}

		string fraction = digits > 0 ? (mantissa & ((1UL << (digits * 4)) - 1)).ToString($"x{digits}") : "";
		if (precision > 13)
			fraction += new string('0', precision - 13);
#if !WIN32
		if (precision < 0)
			fraction = fraction.TrimEnd('0');
#endif

		return $"{mantissa >> (digits * 4):x}{(fraction.Length > 0 || forcePoint ? "." : "")}{fraction}p{(exp2 < 0 ? '-' : '+')}{Math.Abs(exp2)}";
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
		if (Ascii.ToLowerInPlace(str, out int processed) == OperationStatus.Done)
			return;
		for (int i = processed; i < str.Length; i++)
			str[i] = char.ToLowerInvariant(str[i]);
	}

	public static void strupper(Span<char> str) {
		if (Ascii.ToUpperInPlace(str, out int processed) == OperationStatus.Done)
			return;
		for (int i = processed; i < str.Length; i++)
			str[i] = char.ToUpperInvariant(str[i]);
	}

	public static int strcmp(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b) => a.SliceNullTerminatedString().CompareTo(b.SliceNullTerminatedString(), StringComparison.Ordinal);
	public static int strncmp(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b, int c) => a.SliceNullTerminatedString().SliceSafe(c).CompareTo(b.SliceNullTerminatedString().SliceSafe(c), StringComparison.Ordinal);
	public static int strnicmp(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b, int c) => stricmp(a.SliceNullTerminatedString().SliceSafe(c), b.SliceNullTerminatedString().SliceSafe(c));
	public static int stricmp(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b) {
		a = a.SliceNullTerminatedString(); b = b.SliceNullTerminatedString();
		for (int i = 0; i < a.Length && i < b.Length; i++)
			if (a[i] != b[i] && char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[i]))
				return char.ToLowerInvariant(a[i]) - char.ToLowerInvariant(b[i]);
		return a.Length - b.Length;
	}
	public static int stricmp(scoped ReadOnlySpan<byte> a, scoped ReadOnlySpan<byte> b) {
		a = a.SliceNullTerminatedString(); b = b.SliceNullTerminatedString();
		for (int i = 0; i < a.Length && i < b.Length; i++)
			if (a[i] != b[i] && char.ToLowerInvariant((char)a[i]) != char.ToLowerInvariant((char)b[i]))
				return char.ToLowerInvariant((char)a[i]) - char.ToLowerInvariant((char)b[i]);
		return a.Length - b.Length;
	}
	public static int strcmpi(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b) => stricmp(a, b);


	public static bool streq(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b) => a.SliceNullTerminatedString().Equals(b.SliceNullTerminatedString(), StringComparison.Ordinal);
	public static bool strieq(scoped ReadOnlySpan<char> a, scoped ReadOnlySpan<char> b) => a.SliceNullTerminatedString().Equals(b.SliceNullTerminatedString(), StringComparison.OrdinalIgnoreCase);

	public static nint strlen(scoped ReadOnlySpan<char> str) {
		int i = str.IndexOf('\0');
		return i == -1 ? str.Length : i;
	}

	public static nint strlen(scoped ReadOnlySpan<byte> str) {
		int i = str.IndexOf((byte)0);
		return i == -1 ? str.Length : i;
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
	public static int sprintf(scoped Span<char> target, ref CFormatReader reader, params ReadOnlySpan<object?> args) {
		int written = 0;
		while (!reader.Overflowed() && written < target.Length) {
			int literal = reader.ReadLiteral(target[written..]);
			if (literal > 0) {
				written += literal;
				continue;
			}

			CFormatReader start = reader;
			if (!reader.ReadSpec(out CFormatSpec spec))
				throw new FormatException("Unexpected non-overflow, non-variable, and non-literal condition met...");

			CFormatOutput output = new(target[written..], start.ResumeOffset);
			CFormatUtils.WriteArgument(ref output, in spec, reader.GetText(in spec), args);
			if (output.Total - start.ResumeOffset <= target.Length - written) {
				written += output.Written;
				reader.ResumeOffset = 0;
				continue;
			}

			reader = start;
			if (written > 0 && start.ResumeOffset == 0)
				break;

			reader.ResumeOffset += output.Written;
			written += output.Written;
			break;
		}

		return written;
	}
	public static PrintF sprintf(Span<char> target, ReadOnlySpan<char> format) => new(target, format);
	public static void binarytohex(ReadOnlySpan<byte> input, Span<char> output) {
		for (int i = 0; i < input.Length; i++) input[i].TryFormat(output[(i * 2)..], out _, "x2");
		output[input.Length * 2] = '\0';
	}
}
