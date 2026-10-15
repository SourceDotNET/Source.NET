using Source.Common.GarrysMod;

namespace HTML.Chromium;

public sealed class JSArray : JSValue.IArray
{
	readonly JSValue[] Data;

	JSArray(JSValue[] data) => Data = data;

	public ReadOnlySpan<JSValue> GetData() => Data;
	public JSValue[] GetInternalData() => Data;

	public static JSValue Create(JSValue[] data) => new(new JSArray(data));
}

public sealed class JSString : JSValue.IString
{
	readonly string Data;

	JSString(string data) => Data = data;

	public ReadOnlySpan<char> GetData() => Data;
	public string GetInternalData() => Data;

	public static JSValue Create(string data) => new(new JSString(data));
}

public sealed class JSHashMap : JSValue.IHashMap
{
	readonly Dictionary<string, JSValue> Data;
	Dictionary<string, JSValue>.Enumerator Iterator;

	JSHashMap(Dictionary<string, JSValue> data) => Data = data;

	public bool Begin(out ReadOnlySpan<char> key, out JSValue value) {
		Iterator = Data.GetEnumerator();
		return Next(out key, out value);
	}

	public bool Next(out ReadOnlySpan<char> key, out JSValue value) {
		if (!Iterator.MoveNext()) {
			key = default;
			value = default;
			return false;
		}

		key = Iterator.Current.Key;
		value = Iterator.Current.Value;
		return true;
	}

	public int Size() => Data.Count;
	public Dictionary<string, JSValue> GetInternalData() => Data;

	public static JSValue Create(Dictionary<string, JSValue> data) => new(new JSHashMap(data));
}
