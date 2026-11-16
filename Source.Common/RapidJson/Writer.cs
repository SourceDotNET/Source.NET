using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace RapidJson;

public struct Writer : IDisposable
{
	static ReadOnlySpan<byte> HexDigits => "0123456789ABCDEF"u8;
	static readonly SearchValues<byte> Escaped = SearchValues.Create([
		0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, (byte)'"', (byte)'\\'
	]);

	const int MaxPooled = 1 << 20;

	[ThreadStatic] static WeakReference<byte[]?>? spare;

	byte[] buffer;
	int length;
	readonly bool pretty;

	public Writer(bool pretty) {
		buffer = ArrayPool<byte>.Shared.Rent(4096);
		this.pretty = pretty;
	}

	public int Length {
		readonly get => length;
		set => length = value;
	}

	public readonly ReadOnlySpan<byte> WrittenSpan => buffer.AsSpan(0, length);
	public readonly ref byte this[int offset] => ref buffer[offset];

	static byte[] Rent(int size) {
		if (size <= MaxPooled)
			return ArrayPool<byte>.Shared.Rent(size);

		if (spare != null && spare.TryGetTarget(out byte[]? cached) && cached.Length >= size) {
			spare.SetTarget(null);
			return cached;
		}

		return GC.AllocateUninitializedArray<byte>(size);
	}

	static void Return(byte[] array) {
		if (array.Length <= MaxPooled)
			ArrayPool<byte>.Shared.Return(array);
		else if (spare == null)
			spare = new(array);
		else if (!spare.TryGetTarget(out byte[]? cached) || cached.Length < array.Length)
			spare.SetTarget(array);
	}

	public void Dispose() {
		if (buffer.Length != 0)
			Return(buffer);
		buffer = [];
	}

	void Grow(int count) {
		long size = Math.Max((long)buffer.Length * 2, (long)length + count);
		if (size > Array.MaxLength)
			size = Math.Max(Array.MaxLength, (long)length + count);

		byte[] larger = Rent(checked((int)size));
		buffer.AsSpan(0, length).CopyTo(larger);
		Return(buffer);
		buffer = larger;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	void Ensure(int count) {
		if (buffer.Length - length < count)
			Grow(count);
	}

	Span<byte> Reserve(int count) {
		Ensure(count);
		return buffer.AsSpan(length);
	}

	public void Put(byte c) {
		if (length == buffer.Length)
			Grow(1);
		buffer[length++] = c;
	}

	void PutN(byte c, int count) {
		Ensure(count);
		buffer.AsSpan(length, count).Fill(c);
		length += count;
	}

	void Put(scoped ReadOnlySpan<byte> text) {
		Ensure(text.Length);
		text.CopyTo(buffer.AsSpan(length));
		length += text.Length;
	}

	public void Prefix(ref int valueCount, bool inArray, int level) {
		if (!pretty) {
			if (valueCount > 0)
				Put(inArray ? (byte)',' : valueCount % 2 == 0 ? (byte)',' : (byte)':');
		}
		else if (inArray) {
			if (valueCount > 0)
				Put((byte)',');
			Put((byte)'\n');
			PutN((byte)'\t', level);
		}
		else {
			if (valueCount > 0) {
				if (valueCount % 2 == 0) {
					Put((byte)',');
					Put((byte)'\n');
				}
				else {
					Put((byte)':');
					Put((byte)' ');
				}
			}
			else
				Put((byte)'\n');

			if (valueCount % 2 == 0)
				PutN((byte)'\t', level);
		}

		valueCount++;
	}

	public void End(byte bracket, int valueCount, int level) {
		if (pretty && valueCount != 0) {
			Put((byte)'\n');
			PutN((byte)'\t', level);
		}

		Put(bracket);
	}

	public void Int(int value) {
		Span<byte> target = Reserve(11);
		value.TryFormat(target, out int written, default, CultureInfo.InvariantCulture);
		length += written;
	}

	public bool Double(double value) {
		if (!double.IsFinite(value))
			return false;

		length += Dtoa.Write(value, Reserve(Dtoa.MaxLength));
		return true;
	}

	public void Null() => Put("null"u8);

	public void Bool(bool value) => Put(value ? "true"u8 : "false"u8);

	public void String(scoped ReadOnlySpan<byte> text) {
		Ensure(text.Length + 2);
		buffer[length++] = (byte)'"';
		while (true) {
			int index = text.IndexOfAny(Escaped);
			if (index < 0) {
				Put(text);
				break;
			}

			Put(text[..index]);
			byte c = text[index];
			Span<byte> target = Reserve(6);
			target[0] = (byte)'\\';
			switch (c) {
				case (byte)'"': target[1] = (byte)'"'; length += 2; break;
				case (byte)'\\': target[1] = (byte)'\\'; length += 2; break;
				case 8: target[1] = (byte)'b'; length += 2; break;
				case 9: target[1] = (byte)'t'; length += 2; break;
				case 10: target[1] = (byte)'n'; length += 2; break;
				case 12: target[1] = (byte)'f'; length += 2; break;
				case 13: target[1] = (byte)'r'; length += 2; break;
				default:
					target[1] = (byte)'u';
					target[2] = (byte)'0';
					target[3] = (byte)'0';
					target[4] = HexDigits[c >> 4];
					target[5] = HexDigits[c & 0xF];
					length += 6;
					break;
			}

			text = text[(index + 1)..];
		}

		Put((byte)'"');
	}
}
