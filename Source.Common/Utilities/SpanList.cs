namespace Source.Common.Utilities;

/// <summary>
/// List{T} style of operations on fixed/pre-allocated arrays.
/// </summary>
public ref struct SpanList<T>(Span<T> items)
{
	readonly Span<T> Items = items;
	int Used;

	public readonly int Count => Used;
	public readonly int Capacity => Items.Length;

	public readonly ref T this[int index] => ref Items[..Used][index];

	public void Add(in T value) => Items[Used++] = value;

	public void RemoveAt(int index) {
		Items[(index + 1)..Used].CopyTo(Items[index..]);
		Used--;
	}

	public void Clear() => Used = 0;

	public readonly Span<T> AsSpan() => Items[..Used];
}
