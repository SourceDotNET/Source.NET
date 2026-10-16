namespace Source.Common.GarrysMod;

public readonly struct JSValue
{
	public interface IArray
	{
		ReadOnlySpan<JSValue> GetData();
	}

	public interface IString
	{
		ReadOnlySpan<char> GetData();
	}

	public interface IHashMap
	{
		bool Begin(out ReadOnlySpan<char> key, out JSValue value);
		bool Next(out ReadOnlySpan<char> key, out JSValue value);
		int Size();
	}

	public enum Type
	{
		Undefined,
		Bool,
		Number,
		Array,
		String,
		HashMap
	}

	readonly Type type;
	readonly bool boolean;
	readonly double number;
	readonly object? obj;

	public JSValue() => type = Type.Undefined;

	public JSValue(bool value) {
		type = Type.Bool;
		boolean = value;
	}

	public JSValue(double value) {
		type = Type.Number;
		number = value;
	}

	public JSValue(IString str) {
		type = Type.String;
		obj = str;
	}

	public JSValue(IArray array) {
		type = Type.Array;
		obj = array;
	}

	public JSValue(IHashMap hashMap) {
		type = Type.HashMap;
		obj = hashMap;
	}

	public Type GetJSType() => type;
	public bool GetBool() => boolean;
	public double GetNumber() => number;

	public ReadOnlySpan<char> GetString() => ((IString)obj!).GetData();
	public IString? GetInternalString() => obj as IString;

	public ReadOnlySpan<JSValue> GetArray() => ((IArray)obj!).GetData();
	public IArray? GetInternalArray() => obj as IArray;

	public bool HashMap_Begin(out ReadOnlySpan<char> key, out JSValue value) => ((IHashMap)obj!).Begin(out key, out value);
	public bool HashMap_Next(out ReadOnlySpan<char> key, out JSValue value) => ((IHashMap)obj!).Next(out key, out value);
	public int HashMap_Size() => ((IHashMap)obj!).Size();
	public IHashMap? GetInternalHashMap() => obj as IHashMap;
}
