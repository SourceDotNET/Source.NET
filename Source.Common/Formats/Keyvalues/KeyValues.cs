using Source.Common.Filesystem;
using Source.Common.Utilities;

using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;

namespace Source.Common.Formats.Keyvalues;


[DebuggerDisplay("Name = {Name}, Type = {Type}, Count = {Count}, Value = {Value}")]
public class KeyValues : IEnumerable<KeyValues>
{
	public enum Types
	{
		None = 0,
		String,
		Int,
		Double,
		Pointer,
		Color,
		Uint64,
		NumTypes
	}

	public void Clear() {
		foreach (var child in children)
			child.node = null!; // << Intentionally break children
		children.Clear();
		Type = Types.None;
	}

	public string Name = "";
	public Types Type;
	public object? Value;

	bool useEscapeSequences = false;
	bool evaluateConditionals = false;

	LinkedListNode<KeyValues> node;
	LinkedList<KeyValues> children = [];

	public KeyValues() {
		node = new(this);
	}

	public KeyValues(ReadOnlySpan<char> name) : base() {
		node = new(this);
		Name = new(name);
	}

	public KeyValues(string? name) : base() {
		node = new(this);
		Name = name ?? "";
	}

	public KeyValues(ReadOnlySpan<char> name, int value) : base() {
		node = new(this);
		Name = new(name);

		SetInt(value);
	}
	public KeyValues(ReadOnlySpan<char> name, object? instance) : base() {
		node = new(this);
		Name = new(name);

		SetPtr(instance);
	}

	public KeyValues(ReadOnlySpan<char> name, ReadOnlySpan<char> firstKey, ReadOnlySpan<char> firstValue) : base() {
		node = new(this);
		Name = new(name);

		SetString(firstKey, firstValue);
	}

	public KeyValues(ReadOnlySpan<char> name, ReadOnlySpan<char> firstKey, int firstValue) : base() {
		node = new(this);
		Name = new(name);

		SetInt(firstKey, firstValue);
	}

	public KeyValues(ReadOnlySpan<char> name, ReadOnlySpan<char> firstKey, int firstValue, ReadOnlySpan<char> secondKey, int secondValue) : base() {
		node = new(this);
		Name = new(name);

		SetInt(firstKey, firstValue);
		SetInt(secondKey, secondValue);
	}

	public KeyValues(ReadOnlySpan<char> name, ReadOnlySpan<char> firstKey, ReadOnlySpan<char> firstValue, ReadOnlySpan<char> secondKey, ReadOnlySpan<char> secondValue) : base() {
		node = new(this);
		Name = new(name);

		SetString(firstKey, firstValue);
		SetString(secondKey, secondValue);
	}

	public bool LoadFromStream(Stream? stream) {
		// Clear();
		if (stream == null) return false;

		// Size the buffer to the stream when we can, so the text is decoded in one piece.
		int bufferSize = stream.CanSeek ? (int)Math.Clamp(stream.Length - stream.Position, 1024, 1 << 20) : 1024;
		using StreamReader streamReader = new(stream, System.Text.Encoding.UTF8, true, bufferSize);
		return LoadFromBuffer(new KeyValuesReader(streamReader.ReadToEnd()));
	}

	public sealed class KeyValuesReader(string text)
	{
		readonly string text = text;
		int position;

		public ReadOnlySpan<char> Remaining => text.AsSpan(position);

		public void Advance(int count) => position += count;

		public string ReadString(int count) {
			string str = text.Substring(position, count);
			position += count;
			return str;
		}

		public void Unread(char c) {
			Debug.Assert(position > 0 && text[position - 1] == c, "can only unread the character that was just read");
			position--;
		}

		public unsafe bool SkipWhitespace() {
			int start = position;
			fixed (char* chars = text) {
				int i = position, length = text.Length;
				while (i < length) {
					char c = chars[i];
					if (c != ' ' && (c < '\t' || c > '\r') && (c < 0x80 || !char.IsWhiteSpace(c)))
						break;
					i++;
				}
				position = i;
			}

			return position != start;
		}

		public int Position => position;

		public int Peek() => position < text.Length ? text[position] : -1;

		public int Read() => position < text.Length ? text[position++] : -1;
	}

	public bool WriteToStream(Stream? stream) {
		// Clear();
		if (stream == null) return false;

		using StreamWriter writer = new StreamWriter(stream);
		return WriteToBuffer(writer);
	}

	private void WriteStringToBuffer(StreamWriter writer, string value) {
		if (useEscapeSequences)
			value = value.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
		writer.Write(value);
	}

	private bool WriteToBuffer(StreamWriter writer, int recursionLevel = 0) {
		writer.Write(new string('\t', recursionLevel));
		writer.Write("\"");
		WriteStringToBuffer(writer, Name);
		writer.WriteLine("\"");
		if (children.Count != 0) {
			writer.WriteLine("\"");
			writer.WriteLine("{");
			foreach (var child in children)
				child.WriteToBuffer(writer, 1);
			writer.WriteLine("}");
		}
		else {
			switch (Type) {
				case Types.None:
					writer.WriteLine();
					break;
				case Types.String:
					writer.Write("\"");
					WriteStringToBuffer(writer, Value is string str ? (str ?? "") : "");
					writer.WriteLine("\"");
					break;
				case Types.Int:
					writer.Write("\"");
					WriteStringToBuffer(writer, (Value is long l ? l : Value is int i1 ? i1 : 0).ToString());
					writer.WriteLine("\"");
					break;
				case Types.Double:
					writer.Write("\"");
					WriteStringToBuffer(writer, ((float)(Value is double d ? d : Value is int i2 ? i2 : 0)).ToString("F6", CultureInfo.InvariantCulture));
					writer.WriteLine("\"");
					break;
				case Types.Pointer:
					throw new NotImplementedException();
				case Types.Color:
					throw new NotImplementedException();
				case Types.Uint64:
					throw new NotImplementedException();
			}
		}
		return true;
	}


	private bool LoadFromBuffer(KeyValuesReader reader) {
		LinkedList<KeyValues> peers = [];
		KeyValues? current = this;
		while (SkipUntilParseableTextOrEOF(reader)) {
			current ??= new() { evaluateConditionals = evaluateConditionals, useEscapeSequences = useEscapeSequences };
			if (current.ReadKV(reader)) {
				current.node = peers.AddLast(current);
				current = null;
			}
			else
				current.Clear();
		}
		return node.List == peers;
	}

	// Returns true if we did anything at all to skip whitespace.
	public static bool SkipWhitespace(KeyValuesReader reader) {
		return reader.SkipWhitespace();
	}

	public override string ToString() {
		return $"{Type}<{Value}>";
	}

	// Returns true if we can read something. False if we can't.
	private bool SkipUntilParseableTextOrEOF(KeyValuesReader reader) {
		// We read either
		//    1. A quote mark, in which case we need to read up to a quote
		//    2. Anything else, we read until whitespace

		while (true) {
			int c = reader.Peek();
			if (c == -1)
				return false;
			// Most calls land directly on a token: no whitespace or comment can start with these characters.
			if (c > ' ' && c < 0x7F && c != '/')
				return true;
			// If no whitespace was skipped and no comments were skipped, continue
			if (!SkipWhitespace(reader) && !SkipComments(reader))
				return true;
		}
	}

	/// <summary>
	/// 
	/// </summary>
	/// <param name="reader"></param>
	/// <param name="str"></param>
	/// <param name="match">If [$CONDITION], this value will be true, and if [!$CONDITION], this value will be false.</param>
	/// <returns></returns>
	public static bool EvaluateConditional(Span<char> str) {
		if (str.IsEmpty)
			return false;

		if (str[0] == '[')
			str = str[1..];

		bool bNot = false; // should we negate this command?
		if (str[0] == '!')
			bNot = true;

		if (str.Contains("$WIN32", StringComparison.OrdinalIgnoreCase))
			return IsPC() ^ bNot;

		if (str.Contains("$WINDOWS", StringComparison.OrdinalIgnoreCase))
#if WIN32
			return !bNot;
#else
			return bNot;
#endif
		if (str.Contains("$OSX", StringComparison.OrdinalIgnoreCase))
#if OSX
			return !bNot;
#else
			return bNot;
#endif
		if (str.Contains("$LINUX", StringComparison.OrdinalIgnoreCase))
#if LINUX
			return !bNot;
#else
			return bNot;
#endif
		if (str.Contains("$POSIX", StringComparison.OrdinalIgnoreCase))
#if OSX || LINUX
			return !bNot;
#else
			return bNot;
#endif

		return false;
	}
	public static bool ReadConditional(KeyValuesReader reader, Span<char> condition, out bool match) {
		if (reader.Peek() != '[') {
			match = false;
			return false;
		}

		// Zero out if it's existing memory
		condition.Clear();

		reader.Read();
		// Determine if we're inverted.
		char c = (char)reader.Read();
		match = true;
		if (c == '!') {
			match = false;
			c = (char)reader.Read();
		}

		if (c != '$') {
			Warning("ReadConditional failed.\n");
			return false;
		}

		int i = -1;
		while (++i < condition.Length) {
			char readIn = (char)reader.Read();
			if (readIn == ']')
				break;
			condition[i] = readIn;
		}
		return true;
	}

	public static bool HandleConditional(ReadOnlySpan<char> condition, bool supported) {
		int realStrLength = System.MemoryExtensions.IndexOf(condition, '\0');
		if (realStrLength == -1) {
			Debug.Assert(false, "String overflow!!!");
			return false;
		}
		condition = condition[..realStrLength];

		bool notSupported = !supported; // just so its more obvious

		// NOTE: Apparently, WIN32 means IsPC
		// Thank you Source so much
		switch (condition) {
#if WIN32
			case "WIN32": return supported;
			case "WINDOWS": return supported;
			case "X360": return notSupported;
			case "OSX": return notSupported;
			case "POSIX": return notSupported;
			case "LINUX": return notSupported;
#elif OSX
			case "WIN32": return supported;
			case "WINDOWS": return notSupported;
			case "X360": return notSupported;
			case "OSX": return supported;
			case "POSIX": return notSupported;
			case "LINUX": return notSupported;
#elif LINUX
			case "WIN32": return supported;
			case "WINDOWS": return notSupported;
			case "X360": return notSupported;
			case "OSX": return notSupported;
			case "POSIX": return supported;
			case "LINUX": return supported;
#else
#error Please define how KeyValues.HandleConditional should work on this platform.
#endif
		}
		// Other platforms are not applicable and we should just throw them away
		return notSupported;
	}

	// The reader must already be positioned on the key; both callers skip to parseable text first.
	private bool ReadKV(KeyValuesReader reader) {
		bool quoteTerminated = (char)reader.Peek() == '"';

		string key = quoteTerminated ? ReadQuoteTerminatedString(reader, useEscapeSequences) : ReadWhitespaceTerminatedString(reader);
		Name = key;

		SkipUntilParseableTextOrEOF(reader);

		Span<char> conditional = stackalloc char[16];
		int beforeConditional = reader.Position;
		bool isBlockConditional = ReadConditional(reader, conditional, out bool mustMatch);
		bool matches = isBlockConditional ? HandleConditional(conditional, mustMatch) : true;

		// Nothing was consumed unless there was a conditional, so there is nothing new to skip.
		if (reader.Position != beforeConditional)
			SkipUntilParseableTextOrEOF(reader);

		// Determine what we're reading next.
		// If we run into a {, we read another KVObject and set our value to that.
		// If we run into a ", we read another string-based value terminated by quotes.
		// If we run into anything else, we read another string-based value, terminated by space or EOF.
		// The value will then be set based on the string. int.TryParse will try to make it an int, same for double, and Color.
		// We then will leave.

		char nextAction = (char)reader.Peek();
		string value;
		switch (nextAction) {
			case '{':
				// Ok, now we need to read every single key value until we hit a }
				ReadKVPairs(reader, matches);
				Type = Types.None;
				break;
			case '"':
				value = ReadQuoteTerminatedString(reader, useEscapeSequences);
				goto valueTypeSpecific;
			default:
				value = ReadWhitespaceTerminatedString(reader);
				goto valueTypeSpecific;
		}

		return !isBlockConditional || matches;

	valueTypeSpecific:

		DetermineValueType(value);
		SkipUntilParseableTextOrEOF(reader);
		bool isValueConditional = ReadConditional(reader, conditional, out bool valueMustMatch);
		bool valueMatches = isValueConditional ? HandleConditional(conditional, valueMustMatch) : true;
		return valueMatches;
	}

	void AddToTail(KeyValues kv) => children.AddLast(kv.node);

	private void ReadKVPairs(KeyValuesReader reader, bool matches) {
		int rd = reader.Read();

		while (reader.Peek() != -1) {
			SkipWhitespace(reader);
			if (reader.Peek() == '}') {
				reader.Read();

				SkipUntilParseableTextOrEOF(reader);

				break;
			}
			SkipUntilParseableTextOrEOF(reader);
			// Start reading keyvalues.
			KeyValues kvpair = new() { evaluateConditionals = this.evaluateConditionals, useEscapeSequences = this.useEscapeSequences };

			if (kvpair.ReadKV(reader) && matches) // When conditional, still need to waste time on parsing, but we throw it away after
												  // There's definitely a better way to handle this, but it would need more testing scenarios
												  // The ReadKV call can also determine its condition and will return false if it doesnt want to be added.
				AddToTail(kvpair);
		}
	}

	// Returns true if we did anything at all to skip comments.
	public static bool SkipComments(KeyValuesReader reader) {
		ReadOnlySpan<char> text = reader.Remaining;
		// We need to check the stream for another /
		if (text.Length < 2 || text[0] != '/' || text[1] != '/')
			return false;

		// We got //, its a comment
		// We read until the end of the line.
		int newline = text.IndexOf('\n');
		reader.Advance(newline == -1 ? text.Length : newline + 1);
		return true;
	}

	private void DetermineValueType(string input) {
		if (input.Length == 0) {
			Value = input;
			Type = Types.String;
			return;
		}

		// an 18-byte value prefixed with "0x" (followed by 16 hex digits) is an uint64 value
		if (input.Length == 18 && input[0] == '0' && input[1] == 'x') {
			ulong retVal = 0;
			for (int i = 2; i < 2 + 16; i++) {
				char digit = input[i];
				if (digit >= 'a')
					digit -= (char)('a' - ('9' + 1));
				else if (digit >= 'A')
					digit -= (char)('A' - ('9' + 1));
				retVal = (retVal * 16) + (ulong)(digit - '0');
			}
			Value = retVal;
			Type = Types.Uint64;
			return;
		}

		// strtol consumes the whole string
		if (IsIntegerSyntax(input)) {
			if (int.TryParse(input, NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int i32)) {
				Value = i32;
				Type = Types.Int;
			}
			else {
				Value = input;
				Type = Types.String;
			}
			return;
		}

		// strtod consumes the whole string, further than strtol
		if (double.TryParse(input, NumberStyles.AllowLeadingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double d64)) {
			Value = (double)(float)d64;
			Type = Types.Double;
			return;
		}

		Value = input;
		Type = Types.String;
	}

	static bool IsIntegerSyntax(ReadOnlySpan<char> input) {
		int i = 0;
		while (i < input.Length && char.IsWhiteSpace(input[i]))
			i++;
		if (i < input.Length && (input[i] == '+' || input[i] == '-'))
			i++;
		if (i == input.Length)
			return false;
		for (; i < input.Length; i++)
			if (input[i] < '0' || input[i] > '9')
				return false;
		return true;
	}

	public static string ReadWhitespaceTerminatedString(KeyValuesReader reader) {
		ReadOnlySpan<char> text = reader.Remaining;
		int len = Math.Min(text.Length, MAX_TOKEN_LENGTH);
		int i = 0;
		while (i < len && !char.IsWhiteSpace(text[i]))
			i++;

		if (i >= MAX_TOKEN_LENGTH) {
			Warning("KeyValues: string overflow, ignoring (should we allocate more space?)\n");
			AssertMsg(false, "KeyValues string overflow");
		}

		return reader.ReadString(i);
	}

	const int MAX_TOKEN_LENGTH = 1024;

	public static string ReadQuoteTerminatedString(KeyValuesReader reader, bool useEscapeSequences) {
		int rd = reader.Read();
		Debug.Assert(rd == '"', "invalid quote-terminated string");

		// Nearly every string closes within the token limit and has nothing to unescape
		ReadOnlySpan<char> text = reader.Remaining;
		if (text.Length > MAX_TOKEN_LENGTH)
			text = text[..MAX_TOKEN_LENGTH];

		int end = useEscapeSequences ? text.IndexOfAny('"', '\\') : text.IndexOf('"');
		if (end != -1 && text[end] == '"') {
			string str = reader.ReadString(end);
			reader.Advance(1);
			return str;
		}

		Span<char> work = stackalloc char[MAX_TOKEN_LENGTH];
		int i, len;
		bool lastCharacterWasEscape = false;
		int charI = 0;
		for (i = 0, len = work.Length; i < len; i++) {
			char c = (char)reader.Peek();
			if (c == -1) break;
			if (lastCharacterWasEscape) {
				// Mutate c into the escape value or just insert the raw character.
				c = c switch {
					'n' => '\n',
					'r' => '\r',
					_ => c
				};
				lastCharacterWasEscape = false;
			}
			else {
				if (c == '"') {
					reader.Read();
					break;
				}
				else if (c == '\\' && useEscapeSequences) {
					lastCharacterWasEscape = true;
					reader.Read();
					continue;
				}
			}
			work[charI++] = c;
			reader.Read();
		}

		if (i >= len) {
			Warning("KeyValues: string overflow, ignoring (should we allocate more space?)\n");
			AssertMsg(false, "KeyValues string overflow");
		}

		return new(work[..charI]);
	}

	public bool LoadFromFile(string? filepath) {
		if (filepath == null) return false;

		if (!Path.IsPathFullyQualified(filepath))
			filepath = Path.Combine(AppContext.BaseDirectory, filepath);

		FileInfo info = new(filepath);
		FileStream stream;
		try { stream = info.OpenRead(); }
		catch { return false; }

		bool ok = LoadFromStream(stream);
		stream.Dispose();
		return ok;
	}

	public bool WriteToFile(string? filepath) {
		if (filepath == null) return false;

		if (!Path.IsPathFullyQualified(filepath))
			filepath = Path.Combine(AppContext.BaseDirectory, filepath);

		FileInfo info = new(filepath);
		FileStream stream;
		try { stream = info.OpenRead(); }
		catch { return false; }

		bool ok = WriteToStream(stream);
		stream.Dispose();
		return ok;
	}

	KeyValues? Chain;
	public void ChainKeyValue(KeyValues? chain) => Chain = chain;

	public KeyValues? FindKey(ReadOnlySpan<char> searchStr, bool create = false) {
		foreach (var child in this.children) {
			if (stricmp(child.Name, searchStr) == 0)
				return child;
		}

		if (Chain != null) {
			KeyValues? chained = Chain.FindKey(searchStr, false);
			if (chained != null)
				return chained;
		}

		if (create) {
			KeyValues newKey = new(searchStr);
			newKey.useEscapeSequences = useEscapeSequences;

			AddToTail(newKey);
			return newKey;
		}

		return null;
	}

	public KeyValues CreateNewKey() {
		int newID = 1;
		for (KeyValues? dat = GetFirstSubKey(); dat != null; dat = dat.GetNextKey()) {
			if (int.TryParse(dat.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int val))
				if (newID <= val)
					newID = val + 1;
		}

		Span<char> buf = stackalloc char[12];
		sprintf(buf, $"{newID}");

		return CreateKey(((ReadOnlySpan<char>)buf).SliceNullTerminatedString());
	}

	public KeyValues CreateKey(ReadOnlySpan<char> keyName) {
		KeyValues dat = new(keyName);
		dat.useEscapeSequences = useEscapeSequences;
		AddSubKey(dat);
		return dat;
	}

	public ReadOnlySpan<char> GetString() {
		switch (Type) {
			case Types.Double:
				Value = ((float)(double)Value!).ToString("F6", CultureInfo.InvariantCulture);
				Type = Types.String;
				break;
			case Types.Int:
				Value = ((int)Value!).ToString(CultureInfo.InvariantCulture);
				Type = Types.String;
				break;
			case Types.Uint64:
				Value = ((long)(ulong)Value!).ToString(CultureInfo.InvariantCulture);
				Type = Types.String;
				break;
		}
		return Value is string str ? str : "";
	}
	public ReadOnlySpan<char> GetString(ReadOnlySpan<char> key, ReadOnlySpan<char> defaultValue = default) {
		var keyob = FindKey(key);
		if (keyob == null) return defaultValue;
		return keyob.GetString();
	}

	public void SetString(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		KeyValues? dat = FindKey(keyName, true);
		if (dat != null) {
			if (dat.Type == Types.String && value.Equals(dat.Value?.ToString(), StringComparison.Ordinal))
				return;

			if (value.IsEmpty)
				value = "";
			dat.Value = new string(value.SliceNullTerminatedString());
			dat.Type = Types.String;
		}
	}
	public int GetInt(ReadOnlySpan<char> key, int defaultValue = default) {
		var keyob = FindKey(key);

		if (keyob == null)
			return defaultValue;

		return keyob.Value switch {
			int i => i,
			double d => (int)d,
			ulong u => (int)u,
			string str => int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : defaultValue,
			_ => defaultValue
		};
	}

	public Color GetColor(ReadOnlySpan<char> key, Color defaultValue = default) {
		var keyob = FindKey(key);

		if (keyob == null)
			return defaultValue;

		return keyob.GetColor();
	}

	public float GetFloat(ReadOnlySpan<char> key, float defaultValue = default) {
		var keyob = FindKey(key);

		if (keyob == null)
			return defaultValue;

		return keyob.Value switch {
			double d => (float)d,
			int i => i,
			ulong u => u,
			string str => double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? (float)r : defaultValue,
			_ => defaultValue
		};
	}

	public double GetDouble(ReadOnlySpan<char> key, double defaultValue = default) {
		var keyob = FindKey(key);

		if (keyob == null)
			return defaultValue;

		return keyob.Value switch {
			double d => d,
			int i => i,
			ulong u => u,
			string str => double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) ? r : defaultValue,
			_ => defaultValue
		};
	}

	public void SetInt(int value) {
		Value = value;
		Type = Types.Int;
	}

	public void SetInt(ReadOnlySpan<char> keyName, int value) {
		KeyValues? dat = FindKey(keyName, true);
		if (dat != null) {
			dat.Value = value;
			dat.Type = Types.Int;
		}
	}

	public void SetUint64(ReadOnlySpan<char> keyName, ulong value) {
		KeyValues? dat = FindKey(keyName, true);
		if (dat != null) {
			dat.Value = value;
			dat.Type = Types.Uint64;
		}
	}

	public ulong GetUint64(ReadOnlySpan<char> keyName, ulong defaultValue = default) {
		KeyValues? dat = FindKey(keyName, false);
		if (dat == null)
			return defaultValue;

		return dat.Value is ulong u
			? u
			: dat.Value is string str
				? ulong.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong r)
					? r
					: defaultValue
				: Convert.ToUInt64(dat.Value);
	}

	public bool LoadFromFile(IFileSystem fileSystem, ReadOnlySpan<char> path, ReadOnlySpan<char> pathID) {
		return LoadFromStream(fileSystem.Open(path, FileOpenOptions.Read, pathID)?.Stream);
	}
	public bool LoadFromFile(IFileSystem fileSystem, ReadOnlySpan<char> path) {
		return LoadFromStream(fileSystem.Open(path, FileOpenOptions.Read, null)?.Stream);
	}

	public bool WriteToFile(IFileSystem fileSystem, ReadOnlySpan<char> path, ReadOnlySpan<char> pathID) {
		return WriteToStream(fileSystem.Open(path, FileOpenOptions.Write, pathID)?.Stream);
	}
	public bool WriteToFile(IFileSystem fileSystem, ReadOnlySpan<char> path) {
		return WriteToStream(fileSystem.Open(path, FileOpenOptions.Write, null)?.Stream);
	}



	public KeyValues? GetFirstSubKey() => children.First?.Value;
	public KeyValues? GetFirstTrueSubKey() {
		for (KeyValues? ret = GetFirstSubKey(); ret != null; ret = ret.GetNextKey()) {
			if (ret.Type == Types.None)
				return ret;
		}
		return null;
	}
	public KeyValues? GetNextTrueSubKey() {
		for (LinkedListNode<KeyValues>? ret = node.Next; ret != null; ret = ret.Next) {
			if (ret.Value.Type == Types.None)
				return ret.Value;
		}
		return null;
	}

	public KeyValues? GetFirstValue() {
		for (KeyValues? ret = GetFirstSubKey(); ret != null; ret = ret.GetNextKey()) {
			if (ret.Type != Types.None)
				return ret;
		}
		return null;
	}

	public KeyValues? GetNextValue() {
		for (LinkedListNode<KeyValues>? ret = node.Next; ret != null; ret = ret.Next) {
			if (ret.Value.Type != Types.None)
				return ret.Value;
		}
		return null;
	}

	// TODO: We should cache these!!!!
	public int GetInt() {
		if (Value is string str)
			return int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? (int)f : 0;
		else if (Value is int i)
			return i;
		else
			return Convert.ToInt32(Value);
	}
	public float GetFloat() {
		if (Value is string str)
			return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0;
		else if (Value is float f)
			return f;
		else
			return Convert.ToSingle(Value);
	}
	public double GetDouble() {
		if (Value is string str)
			return double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
		else if (Value is double d)
			return d;
		else
			return Convert.ToDouble(Value);
	}

	public void UsesEscapeSequences(bool value) {
		useEscapeSequences = value;
	}

	public void UsesConditionals(bool value) {
		evaluateConditionals = value;
	}

	public KeyValues? GetNextKey() {
		return node.Next?.Value;
	}

	public IEnumerator<KeyValues> GetEnumerator() {
		foreach (var key in children)
			yield return key;
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	public KeyValues MakeCopy() {
		KeyValues newKeyValue = new(Name);

		newKeyValue.UsesEscapeSequences(useEscapeSequences);
		newKeyValue.UsesConditionals(evaluateConditionals);

		newKeyValue.Type = Type;
		newKeyValue.Value = Value;

		CopySubkeys(newKeyValue);
		return newKeyValue;
	}

	public void CopySubkeys(KeyValues parent) {
		for (KeyValues? sub = GetFirstSubKey(); sub != null; sub = sub.GetNextKey()) {
			KeyValues dat = sub.MakeCopy();
			parent.AddToTail(dat);
		}
	}

	// Untested...
	public bool LoadFromBuffer(ReadOnlySpan<char> resourceName, ReadOnlySpan<char> buffer) {
		using MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(buffer.ToArray()));
		return LoadFromStream(stream);
	}

	public KeyValues AddSubKey(KeyValues subkey) {
		Assert(subkey != null);
		AddToTail(subkey);
		return this;
	}

	public KeyValues AddSubKey(ReadOnlySpan<char> name, int value) {
		AddToTail(new KeyValues(name, value));
		return this;
	}

	public void RemoveSubKey(KeyValues? subkey) {
		if (subkey == null)
			return;

		if (children.First != null && children.First.Value == subkey)
			children.RemoveFirst();
		else {
			var node = children.First;
			while (node != null && node.Next != null) {
				if (node.Next.Value == subkey) {
					children.Remove(node.Next);
					break;
				}
				node = node.Next;
			}
		}
	}

	public void SetPtr(object? ptr) {
		Value = ptr;
		Type = Types.Pointer;
	}

	public void SetPtr(ReadOnlySpan<char> keyName, object? ptr) {
		KeyValues? dat = FindKey(keyName, true);
		if (dat != null) {
			dat.Value = ptr;
			dat.Type = Types.Pointer;
		}
	}

	public Color GetColor() {
		switch (Value) {
			case Color c: return c;
			case int i: return new(i, 0, 0, 0);
			case double d: return new((int)d, 0, 0, 0);
			case string str: {
					// parse the colors out of the string
					Span<float> rgba = stackalloc float[4];
					int n = 0;
					foreach (string part in str.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
						if (n >= 4) break;
						if (!float.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rgba[n])) break;
						n++;
					}
					return new((byte)rgba[0], (byte)rgba[1], (byte)rgba[2], (byte)rgba[3]);
				}
			default: return new(0, 0, 0, 0);
		}
	}

	public void SetName(ReadOnlySpan<char> name) => Name = name.SliceNullTerminatedString().ToString();
	public void SetFloat(ReadOnlySpan<char> keyName, float value) {
		KeyValues? dat = FindKey(keyName, true);
		if (dat != null) {
			dat.Value = (double)value;
			dat.Type = Types.Double;
		}
	}
	public void SetDouble(ReadOnlySpan<char> keyName, double value) {
		KeyValues? dat = FindKey(keyName, true);
		if (dat != null) {
			dat.Value = value;
			dat.Type = Types.Double;
		}
	}
	public void SetColor(ReadOnlySpan<char> keyName, Color value) {
		KeyValues? dat = FindKey(keyName, true);
		if (dat != null) {
			dat.Value = value;
			dat.Type = Types.Color;
		}
	}

	public bool GetBool(ReadOnlySpan<char> name, bool defaultValue) {
		return GetInt(name, defaultValue ? 1 : 0) != 0;
	}

	public bool GetBool() {
		return GetInt() != 0;
	}

	public object? GetPtr() {
		return Value;
	}

	public T? GetPtr<T>() {
		return (T?)Value;
	}

	public object? GetPtr(ReadOnlySpan<char> name) {
		KeyValues? key = FindKey(name, false);
		if (key != null)
			return key.Value;
		return default;
	}

	public T? GetPtr<T>(ReadOnlySpan<char> name) {
		KeyValues? key = FindKey(name, false);
		if (key != null)
			return (T?)key.Value;
		return default;
	}

	public void SetStringValue(ReadOnlySpan<char> str) {
		Value = null;

		if (str.IsEmpty)
			str = "";

		Value = new string(str);
		Type = Types.String;
	}

	public int Count => children.Count;

	public bool IsEmpty(ReadOnlySpan<char> keyName = default) {
		KeyValues? dat = FindKey(keyName, false);
		if (dat == null)
			return true;

		if (dat.Type == Types.None && dat.children.Count == 0)
			return true;

		return false;
	}

	public bool WriteAsBinary(UtlBuffer buffer) {
		if (buffer.IsText())
			return false;
		if (!buffer.IsValid())
			return false;

		// loop through all our peers
		for (KeyValues? dat = this; dat != null; dat = dat.node.Next?.Value) {
			// write type
			buffer.PutUnsignedChar((byte)dat.Type);

			// write name
			buffer.PutString(dat.Name);

			// write type
			switch (dat.Type) {
				case Types.None: {
						dat.GetFirstSubKey()!.WriteAsBinary(buffer);
						break;
					}
				case Types.String: {
						if (dat.Value is not string str)
							buffer.PutString("");
						else
							buffer.PutString(str);
						break;
					}
				case Types.Int: {
						int v;
						switch (dat.Value) {
							case int f: v = f; break;
							case long f: v = Convert.ToInt32(f); break;
							default: v = 0; break;
						}
						buffer.PutInt(v);
						break;
					}

				case Types.Uint64: {
						if (dat.Value is not ulong i)
							i = 0;
						buffer.PutDouble(const_reinterpret<ulong, double>(new(in i))[0]);
						break;
					}

				case Types.Double: {
						float v;
						switch (dat.Value) {
							case float f: v = f; break;
							case double f: v = (float)f; break;
							default: v = 0; break;
						}
						buffer.PutFloat(v);
						break;
					}
				case Types.Color: {
						if (dat.Value is not Color c)
							c = default;
						buffer.PutUnsignedChar(c[0]);
						buffer.PutUnsignedChar(c[1]);
						buffer.PutUnsignedChar(c[2]);
						buffer.PutUnsignedChar(c[3]);
						break;
					}
				case Types.Pointer: {
						// hmm... not sure how to do this proper...

						break;
					}

				default:
					break;
			}
		}

		buffer.PutUnsignedChar((byte)Types.NumTypes);

		return buffer.IsValid();
	}

	public void RemoveEverything() {
		children.Clear();
		Value = null;
		node.List!.Remove(node);
	}

	public void Init() {
		Name = null!;
		Type = Types.None;
		children.Clear();

		useEscapeSequences = false;
		evaluateConditionals = true;
	}

	public const int KEYVALUES_TOKEN_SIZE = 4096;

	public bool ReadAsBinary(UtlBuffer buffer, int nStackDepth = 0) {
		if (buffer.IsText()) // must be a binary buffer
			return false;

		if (!buffer.IsValid()) // must be valid, no overflows etc
			return false;

		RemoveEverything(); // remove current content
		Init(); // reset

		if (nStackDepth > 100) {
			AssertMsg(false, "KeyValues::ReadAsBinary() stack depth > 100\n");
			return false;
		}

		KeyValues? dat = this;
		Types type = (Types)buffer.GetUnsignedChar();

		// loop through all our peers
		Span<char> token = stackalloc char[KEYVALUES_TOKEN_SIZE];

		while (true) {
			if (type == Types.NumTypes)
				break; // no more peers

			dat.Type = type;

			{
				buffer.GetString(token);
				token[KEYVALUES_TOKEN_SIZE - 1] = '\0';
				dat.SetName(token);
			}

			switch (type) {
				case Types.None: {
						var sub = new KeyValues("");
						if (dat.children.Count == 0)
							dat.children.AddFirst(new LinkedListNode<KeyValues>(sub));
						else {
							dat.children.First!.ValueRef = sub;
							sub.node = dat.children.First;
						}

						sub.ReadAsBinary(buffer, nStackDepth + 1);
						break;
					}
				case Types.String: {
						buffer.GetString(token);
						token[KEYVALUES_TOKEN_SIZE - 1] = '\0';

						int len = (int)strlen(token);
						dat.Value = new string(token[..len]);

						break;
					}
				case Types.Int: {
						dat.Value = buffer.GetInt();
						break;
					}

				case Types.Uint64: {
						dat.Value = (ulong)buffer.GetInt64();
						break;
					}

				case Types.Double: {
						dat.Value = (double)buffer.GetFloat();
						break;
					}
				case Types.Color: {
						Color c = new Color();
						c[0] = buffer.GetUnsignedChar();
						c[1] = buffer.GetUnsignedChar();
						c[2] = buffer.GetUnsignedChar();
						c[3] = buffer.GetUnsignedChar();
						dat.Value = c;
						break;
					}
				case Types.Pointer: {
						// WOW: This sucks! Doesn't this imply the binary data going across two sides is dependant on both being 64-bit?? Why?????????
						/*
		# ifdef PLATFORM_64BITS
								dat->m_pValue = (void*)buffer.GetUint64();
		#else
								dat->m_pValue = (void*)buffer.GetUnsignedInt();
		#endif
								*/

						break;
					}

				default:
					break;
			}

			if (!buffer.IsValid()) // error occured
				return false;

			type = (Types)buffer.GetUnsignedChar();

			if (type == Types.NumTypes)
				break;

			// new peer follows
			var peer = new KeyValues("");
			dat.children.AddLast(peer);
			dat = peer;
		}

		return buffer.IsValid();
	}
}
