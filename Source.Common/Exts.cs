using CommunityToolkit.HighPerformance;

using K4os.Hash.xxHash;

using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Utilities;

using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Source;

/// <summary>
/// General purpose realm enumeration
/// </summary>
public enum Realm
{
	Client,
	Server,
	Menu
}

public interface IPoolableObject
{
	void Init();
	void Reset();
}

public class PoolableList<T> : List<T>, IPoolableObject
{
	public void Init() { }
	public void Reset() => Clear();
}

public class ListPool<T>
{
	public static readonly ListPool<T> Shared = new();
	readonly ObjectPool<PoolableList<T>> pool = new();

	public List<T> Alloc(int capacity = 0) {
		List<T> list = pool.Alloc();
		if (capacity > 0)
			list.EnsureCapacity(capacity);
		return list;
	}

	public void Free(List<T> list) {
		if (list is not PoolableList<T> pooledList)
			throw new InvalidCastException("Got a non-poolable list!");
		pool.Free(pooledList);
		pooledList.Clear();
	}
}

public class PooledValueList<V> where V : IPoolableObject, new()
{
	public static readonly PooledValueList<V> Shared = new();
	public int Count() => list.Count;
	readonly List<V> list = [];
	readonly ObjectPool<V> pool = new();
	public int AddToTail() {
		list.Add(pool.Alloc());
		return list.Count - 1;
	}
	public void RemoveAt(int i) {
		for (int j = 0; j < list.Count; j++) {
			if (j == i) {
				pool.Free(list[j]);
				list.RemoveAt(j);
				return;
			}
		}
	}

	public void RemoveAll() {
		for (int j = 0; j < list.Count; j++)
			pool.Free(list[j]);
		list.Clear();
	}

	public V this[int index] => list[index];
}
// Kind of like a CUtlLinkedList, but not really... meant to be memory efficient in a C# context.
public class PooledValueDictionary<V> : IEnumerable<V> where V : IPoolableObject, new()
{
	public static readonly PooledValueDictionary<V> Shared = new();
	public int Count() => dict.Count;
	readonly Dictionary<ulong, V> dict = [];
	readonly ObjectPool<V> pool = new();
	ulong ptr = 0;
	ulong NewPtr() => Interlocked.Increment(ref ptr);

	public bool IsInList(ulong i) => dict.ContainsKey(i);

	public ulong AddToTail() {
		ulong newPtr = NewPtr();
		dict[ptr] = pool.Alloc();
		return newPtr;
	}
	public bool Remove(ulong key) {
		if (dict.Remove(key, out V? value)) {
			pool.Free(value);
			return true;
		}
		return false;
	}

	public void Purge() {
		foreach (var v in this)
			pool.Free(v);
		dict.Clear();
	}

	public IEnumerator<V> GetEnumerator() => dict.Values.GetEnumerator();
	IEnumerator IEnumerable.GetEnumerator() => dict.Values.GetEnumerator();

	public V this[ulong index] => dict[index];
}

public sealed class PooledLinkedList<T> where T : struct
{
	struct Node
	{
		public T Data;
		public int Prev;
		public int Next;
	}

	Node[] _nodes;
	int _freeHead = -1;
	int _capacity;
	int _count;
	int _growSize;

	public const int INVALID_INDEX = -1;

	public PooledLinkedList(int initialCapacity = 256) {
		_capacity = initialCapacity;
		_nodes = new Node[_capacity];
		for (int i = _capacity - 1; i >= 0; --i) {
			_nodes[i].Next = _freeHead;
			_nodes[i].Prev = i;
			_freeHead = i;
		}
	}

	public ref T this[int index] {
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get => ref _nodes[index].Data;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int Next(int index) => _nodes[index].Next;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int Prev(int index) => _nodes[index].Prev;

	public int Alloc() {
		if (_freeHead == -1) Grow();
		int idx = _freeHead;
		_freeHead = _nodes[idx].Next;
		_nodes[idx].Next = -1;
		_nodes[idx].Prev = -1;
		_nodes[idx].Data = default;
		_count++;
		return idx;
	}

	public void LinkBefore(int beforeIdx, int newIdx) {
		int prev = _nodes[beforeIdx].Prev;
		_nodes[newIdx].Next = beforeIdx;
		_nodes[newIdx].Prev = prev;
		_nodes[beforeIdx].Prev = newIdx;
		if (prev != -1)
			_nodes[prev].Next = newIdx;
	}

	public void Remove(int index) {
		int prev = _nodes[index].Prev;
		int next = _nodes[index].Next;
		if (prev != -1) _nodes[prev].Next = next;
		if (next != -1) _nodes[next].Prev = prev;
		_nodes[index].Next = _freeHead;
		_nodes[index].Prev = index;
		_nodes[index].Data = default;
		_freeHead = index;
		_count--;
	}

	public bool IsValidIndex(int index) => index >= 0 && index < _capacity && (_nodes[index].Prev != index || _nodes[index].Next == index);

	public void SetGrowSize(int growSize) => _growSize = growSize;

	public void EnsureCapacity(int num) {
		if (num > _capacity)
			Grow(num);
	}

	public void Clear() {
		_count = 0;
		_freeHead = -1;
		for (int i = _capacity - 1; i >= 0; --i) {
			_nodes[i] = default;
			_nodes[i].Next = _freeHead;
			_nodes[i].Prev = i;
			_freeHead = i;
		}
	}

	void Grow(int num = 0) {
		int newCap = Math.Max(num, _growSize > 0 ? _capacity + _growSize : _capacity * 2);
		Array.Resize(ref _nodes, newCap);
		for (int i = newCap - 1; i >= _capacity; --i) {
			_nodes[i].Next = _freeHead;
			_nodes[i].Prev = i;
			_freeHead = i;
		}
		_capacity = newCap;
	}
}

public static class StrTools
{
	// We're going to use / everywhere instead of \ on Windows, since C#'s API
	// takes both nicely
	public const char CORRECT_PATH_SEPARATOR = '/';
	public const char INCORRECT_PATH_SEPARATOR = '\\';

	public static bool IsPathSeparator(this char c) => c == CORRECT_PATH_SEPARATOR || c == INCORRECT_PATH_SEPARATOR;

	public static ReadOnlySpan<char> FileBase(ReadOnlySpan<char> input, Span<char> output) {
		Debug.Assert(output.Length >= 1);

		int len = input.IndexOf('\0');
		if (len < 0) len = input.Length;

		if (len == 0) {
			output[0] = '\0';
			return default;
		}

		int end = len - 1;
		while (end != 0 && input[end] != '.' && !IsPathSeparator(input[end]))
			end--;

		if (input[end] != '.')
			end = len - 1;
		else
			end--;

		int start = len - 1;
		while (start >= 0 && !IsPathSeparator(input[start]))
			start--;

		start = (start < 0 || !IsPathSeparator(input[start])) ? 0 : start + 1;

		int strLen = end - start + 1;
		int maxcopy = Math.Min(strLen + 1, output.Length);

		int n = maxcopy - 1;
		input.Slice(start, n).CopyTo(output);
		output[n] = '\0';
		return output[..n];
	}

	public static void FixDoubleSlashes(Span<char> str) {
		int len = (int)strlen(str);

		for (int i = 1; i < len - 1; i++) {
			if ((str[i] == '/' || str[i] == '\\') && (str[i + 1] == '/' || str[i + 1] == '\\')) {
				// This means there's a double slash somewhere past the start of the filename. That 
				// can happen in Hammer if they use a material in the root directory. You'll get a filename 
				// that looks like 'materials\\blah.vmt'
				memmove(str[i..], str[(i + 1)..], len - i);
				--len;
			}
		}
	}
	public static void FixSlashes(Span<char> name, char separator = CORRECT_PATH_SEPARATOR) {
		for (int i = 0; i < name.Length && name[i] != '\0'; i++) {
			if (name[i] == INCORRECT_PATH_SEPARATOR || name[i] == CORRECT_PATH_SEPARATOR)
				name[i] = separator;
		}
	}

	public static int StrLen(ReadOnlySpan<char> str) {
		int i = str.IndexOf('\0');
		return i == -1 ? str.Length : i;
	}
	public const int COPY_ALL_CHARACTERS = -1;
	public static ReadOnlySpan<char> Strstr(ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle) {
		int index = haystack.IndexOf(needle, StringComparison.InvariantCulture);
		if (index == -1)
			return null;
		return haystack[index..];
	}
	public static ReadOnlySpan<char> Stristr(ReadOnlySpan<char> haystack, ReadOnlySpan<char> needle) {
		int index = haystack.IndexOf(needle, StringComparison.InvariantCultureIgnoreCase);
		if (index == -1)
			return null;
		return haystack[index..];
	}
	public static Span<char> Strstr(Span<char> haystack, ReadOnlySpan<char> needle) {
		int index = haystack.IndexOf(needle, StringComparison.InvariantCulture);
		if (index == -1)
			return null;
		return haystack[index..];
	}
	public static Span<char> Stristr(Span<char> haystack, ReadOnlySpan<char> needle) {
		int index = haystack.IndexOf(needle, StringComparison.InvariantCultureIgnoreCase);
		if (index == -1)
			return null;
		return haystack[index..];
	}
	public static Span<char> StrConcat(Span<char> dest, ReadOnlySpan<char> src, int max_chars_to_copy = COPY_ALL_CHARACTERS) {
		int charstocopy = 0;
		int len = StrLen(dest);
		int srclen = StrLen(src);

		if (max_chars_to_copy <= COPY_ALL_CHARACTERS)
			charstocopy = srclen;
		else
			charstocopy = Math.Min(max_chars_to_copy, srclen);

		if (len + charstocopy >= dest.Length) {
			charstocopy = dest.Length - len - 1;
		}

		int destLen = 0;
		while (destLen < dest.Length && dest[destLen] != '\0')
			destLen++;

		int i = 0;
		while (i < charstocopy && i < src.Length && destLen + i < dest.Length - 1 && src[i] != '\0') {
			dest[destLen + i] = src[i];
			i++;
		}

		if (destLen + i < dest.Length)
			dest[destLen + i] = '\0';

		return dest;
	}

	public static void AppendSlash(Span<char> str) {
		int len = StrLen(str);
		if (len > 0 && str[len - 1] != CORRECT_PATH_SEPARATOR) {
			if (len + 1 >= str.Length)
				Error($"AppendSlash: ran out of space on {str}.");

			str[len] = CORRECT_PATH_SEPARATOR;
			str[len + 1] = '\0';
		}
	}

	public static void ComposeFileName(ReadOnlySpan<char> path, ReadOnlySpan<char> filename, Span<char> dest) {
		strcpy(dest, path);
		FixSlashes(dest);
		AppendSlash(dest);
		StrConcat(dest, filename, COPY_ALL_CHARACTERS);
		FixSlashes(dest);
	}

	public static void StrLower(Span<char> str) {
		for (int i = 0; i < str.Length; i++)
			str[i] = char.ToLower(str[i]);
	}
	public static void StrUpper(Span<char> str) {
		for (int i = 0; i < str.Length; i++)
			str[i] = char.ToUpper(str[i]);
	}
	public static bool RemoveDotSlashes(Span<char> filename, char separator = CORRECT_PATH_SEPARATOR, bool removeDoubleSlashes = true) {
		int pIn = 0;
		int pOut = 0;
		bool ret = true;
		bool boundary = true;

		while (pIn < filename.Length && filename[pIn] != '\0') {
			if (boundary &&
				filename[pIn] == '.' &&
				pIn + 1 < filename.Length && filename[pIn + 1] == '.' &&
				(pIn + 2 >= filename.Length || IsPathSeparator(filename[pIn + 2]))) {
				while (pOut > 0 && filename[pOut - 1] == separator)
					pOut--;

				while (true) {
					if (pOut == 0) {
						ret = false;
						break;
					}
					pOut--;
					if (filename[pOut] == separator)
						break;
				}

				pIn += 2;
				boundary = (pOut == 0);
			}
			else if (boundary &&
					 filename[pIn] == '.' &&
					 (pIn + 1 >= filename.Length || IsPathSeparator(filename[pIn + 1]))) {
				if (pIn + 1 < filename.Length && IsPathSeparator(filename[pIn + 1])) {
					pIn += 2;
				}
				else {
					if (pOut > 0 && filename[pOut - 1] == separator)
						pOut--;
					pIn += 1;
				}
			}
			else if (IsPathSeparator(filename[pIn])) {
				filename[pOut] = separator;

				if (!(boundary && removeDoubleSlashes && pOut != 0))
					pOut++;

				pIn++;
				boundary = true;
			}
			else {
				if (pOut != pIn)
					filename[pOut] = filename[pIn];

				pOut++;
				pIn++;
				boundary = false;
			}
		}

		if (pOut < filename.Length)
			filename[pOut] = '\0';

		return ret;
	}

	/// <summary>
	/// Rewrites a string in place to be all lowercase
	/// </summary>
	/// <param name="str"></param>
	/// <param name="invariant"></param>
	public static void ToLower(Span<char> str, bool invariant = false) {
		if (invariant)
			for (int i = 0; i < str.Length; i++)
				str[i] = char.ToLowerInvariant(str[i]);
		else
			for (int i = 0; i < str.Length; i++)
				str[i] = char.ToLower(str[i]);
	}

	/// <summary>
	/// Rewrites a string in place to be all uppercase
	/// </summary>
	/// <param name="str"></param>
	/// <param name="invariant"></param>
	public static void ToUpper(Span<char> str, bool invariant = false) {
		if (invariant)
			for (int i = 0; i < str.Length; i++)
				str[i] = char.ToUpperInvariant(str[i]);
		else
			for (int i = 0; i < str.Length; i++)
				str[i] = char.ToUpper(str[i]);
	}

	public static void StripFilename(Span<char> path) {
		int length = (int)(strlen(path) - 1);
		if (length <= 0)
			return;

		while (length > 0 && !IsPathSeparator(path[length]))
			length--;

		path[length] = '\0';
	}

	public static void StripExtension(ReadOnlySpan<char> input, Span<char> output) {
		if (input.IsEmpty) {
			if (!output.IsEmpty)
				output[0] = '\0';
			return;
		}

		int end = input.Length - 1;
		while (end > 0 && input[end] != '.' && !IsPathSeparator(input[end]))
			--end;

		if (end > 0 && !IsPathSeparator(input[end]) && end < output.Length) {
			int nChars = Math.Min(end, output.Length - 1);
			if (!Unsafe.AreSame(in input[0], in output[0]))
				memcpy(output, input[..nChars]);
			output[nChars] = '\0';
		}
		else {
			if (!Unsafe.AreSame(in input[0], in output[0]))
				strcpy(output, input);
		}
	}
	public static bool StripLastDir(Span<char> dirName) {
		if (dirName[0] == '\0' || stricmp(dirName, "./") == 0 || stricmp(dirName, ".\\") == 0)
			return false;

		int len = (int)strlen(dirName);

		Assert(len < dirName.Length);

		if (IsPathSeparator(dirName[len - 1]))
			len--;

		while (len > 0) {
			if (IsPathSeparator(dirName[len - 1])) {
				dirName[len] = '\0';
				FixSlashes(dirName);
				return true;
			}
			len--;
		}

		if (len == 0) {
			sprintf(dirName, ".%c").C(CORRECT_PATH_SEPARATOR);
			return true;
		}

		return true;
	}

	public static void SetExtension(Span<char> path, ReadOnlySpan<char> extension) {
		StripExtension(path, path);

		// We either had an extension and stripped it, or didn't have an extension
		// at all. Either way, we need to concatenate our extension now.

		// extension is not required to start with '.', so if it's not there,
		// then append that first.
		if (extension[0] != '.')
			StrConcat(path, ".", COPY_ALL_CHARACTERS);

		StrConcat(path, extension, COPY_ALL_CHARACTERS);
	}

	public static bool RemoveAllEvilCharacters(Span<char> pch) {
		int cch = (int)strlen(pch);
		bool strippedWhitespace = false;

		int walk = 0;
		for (int i = 0; i < cch; ++i) {
			if (!IsMeanSpaceW(pch[i])) {
				pch[walk] = pch[i];
				++walk;
			}
			else
				strippedWhitespace = true;
		}

		pch[walk] = '\0';

		return strippedWhitespace;
	}

	public static bool IsMeanSpaceW(char ch) {
		bool isMean = false;

		switch (ch) {
			case '\x0082':   // BREAK PERMITTED HERE
			case '\x0083':   // NO BREAK PERMITTED HERE
			case '\x00A0':   // NO-BREAK SPACE
			case '\x034F':   // COMBINING GRAPHEME JOINER
			case '\x2000':   // EN QUAD
			case '\x2001':   // EM QUAD
			case '\x2002':   // EN SPACE
			case '\x2003':   // EM SPACE
			case '\x2004':   // THICK SPACE
			case '\x2005':   // MID SPACE
			case '\x2006':   // SIX SPACE
			case '\x2007':   // figure space
			case '\x2008':   // PUNCTUATION SPACE
			case '\x2009':   // THIN SPACE
			case '\x200A':   // HAIR SPACE
			case '\x200B':   // ZERO-WIDTH SPACE
			case '\x200C':   // ZERO-WIDTH NON-JOINER
			case '\x200D':   // ZERO WIDTH JOINER
			case '\x200E':   // LEFT-TO-RIGHT MARK
			case '\x2028':   // LINE SEPARATOR
			case '\x2029':   // PARAGRAPH SEPARATOR
			case '\x202F':   // NARROW NO-BREAK SPACE
			case '\x2060':   // word joiner
			case '\xFEFF':   // ZERO-WIDTH NO BREAK SPACE
			case '\xFFFC':   // OBJECT REPLACEMENT CHARACTER
				isMean = true;
				break;
		}

		return isMean;
	}

	public static void DefaultExtension(Span<char> path, ReadOnlySpan<char> extension) {
		Assert(extension.Length > 0 && extension[0] == '.');

		int len = (int)strlen(path);
		if (len < 0) len = path.Length;

		// Scan backwards from the last char looking for a '.' before any path separator
		for (int i = len - 1; i > 0; i--) {
			char c = path[i];
			if (c == '/' || c == '\\')
				break;
			if (c == '.')
				return;
		}

		for (int i = 0; i < extension.Length && len + i < path.Length; i++)
			path[len + i] = extension[i];
	}
}

public static class BitVecExts
{
	/// <summary>
	/// Checks if the bit is set.
	/// </summary>
	/// <param name="bools"></param>
	/// <param name="bit"></param>
	/// <returns></returns>
	public static bool IsBitSet(this bool[] bools, int bit) => bools[bit];
	/// <summary>
	/// Sets the bit to 1.
	/// </summary>
	/// <param name="bools"></param>
	/// <param name="bit"></param>
	public static void Set(this bool[] bools, int bit) => bools[bit] = true;
	/// <summary>
	/// Sets the bit to 0.
	/// </summary>
	/// <param name="bools"></param>
	/// <param name="bit"></param>
	public static void Clear(this bool[] bools, int bit) => bools[bit] = false;
}
public static class ListExtensions
{ // Thanks JamesHoux: https://stackoverflow.com/questions/4972951/listt-to-t-without-copying
	delegate ref TField RefAccessor<TObj, TField>(TObj obj);
	static class ArrayAccessor<T>
	{
		public static Func<List<T>, T[]> Getter;
		public static RefAccessor<List<T>, int> GetCount;

		static ArrayAccessor() {
			var dm = new DynamicMethod("get", MethodAttributes.Static | MethodAttributes.Public, CallingConventions.Standard, typeof(T[]), [typeof(List<T>)], typeof(ArrayAccessor<T>), true);
			var il = dm.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0); // Load List<T> argument
			il.Emit(OpCodes.Ldfld, typeof(List<T>).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance)!); // Replace argument by field
			il.Emit(OpCodes.Ret); // Return field
			Getter = (Func<List<T>, T[]>)dm.CreateDelegate(typeof(Func<List<T>, T[]>));

			dm = new DynamicMethod("getSize", MethodAttributes.Static | MethodAttributes.Public, CallingConventions.Standard, typeof(int).MakeByRefType(), [typeof(List<T>)], typeof(ArrayAccessor<T>), true);
			il = dm.GetILGenerator();
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldflda, typeof(List<T>).GetField("_size", BindingFlags.NonPublic | BindingFlags.Instance)!);
			il.Emit(OpCodes.Ret);
			GetCount = (RefAccessor<List<T>, int>)dm.CreateDelegate(typeof(RefAccessor<List<T>, int>));
		}
	}

	static class TempSwap<T>
	{
		public static readonly ThreadLocal<List<T>> List = new(() => []);
	}

	public static void Swap<T>(this List<T> a, List<T> b) {
		var swap = TempSwap<T>.List.Value!;
		swap.Clear();
		swap.EnsureCapacity(a.Count);

		a.Clear();
		a.EnsureCapacity(b.Count);
		b.Base().AsSpan()[..b.Count].CopyTo(a.Base());
		a.CountForEdit() = b.Count;

		b.Clear();
		b.EnsureCapacity(swap.Count);
		swap.Base().AsSpan()[..swap.Count].CopyTo(b.Base());
		b.CountForEdit() = swap.Count;
	}

	public static nint Find<T>(this List<T> list, T? value) {
		for (int i = 0, c = list.Count; i < c; i++) {
			T? at = list[i];
			if (value == null) {
				if (at == null)
					return i;
				else
					continue;
			}

			if (value.Equals(at))
				return i;
		}
		return -1;
	}

	public static T[] Base<T>(this List<T> list) {
		return ArrayAccessor<T>.Getter(list);
	}

	public static ref int CountForEdit<T>(this List<T> list) {
		return ref ArrayAccessor<T>.GetCount(list);
	}
}
public static class ClassUtils
{
	public static ref V TryGetRef<K, V>(this Dictionary<K, V> dict, K key, out bool ok) where K : notnull {
		ref V ret = ref CollectionsMarshal.GetValueRefOrNullRef(dict, key);
		ok = !Unsafe.IsNullRef(ref ret);
		return ref ret;
	}

	public static T[] InstantiateArray<T>(this T[] array) where T : new() {
		for (int i = 0; i < array.Length; i++)
			array[i] ??= new();
		return array;
	}

	public static T[][] Instantiate2DArray<T>(this T[][] array, int size) where T : new() {
		for (int i = 0; i < array.Length; i++)
			array[i] ??= new T[size];
		return array;
	}

	public static bool IsValidIndex<T>(this List<T> list, int index) => index >= 0 && index < list.Count;
	public static bool IsValidIndex<T>(this List<T> list, long index) => index >= 0 && index < list.Count;
	/// <summary>
	/// Each value in the span is null-checked. If null, a new instance is created with no constructor ran. If not null, the existing instance
	/// has all of its fields reset. The latter behavior may break everything and needs further testing.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="array"></param>
	public static void ClearInstantiatedReferences<T>(this T[] array) where T : class => ClearInstantiatedReferences(array.AsSpan());
	public static void ClearInstantiatedReferences<T>(this List<T> array) where T : class => ClearInstantiatedReferences(array.AsSpan());
	private static readonly ConcurrentDictionary<Type, Action<object>> _clearers = new();
	private static readonly ConcurrentDictionary<Type, Action<object, object>> _copiers = new();
	public static void ClearInstantiatedReferences<T>(this Span<T> array) where T : class {
		Action<object> clearer = _clearers.GetOrAdd(typeof(T), CreateClearer);

		foreach (ref T item in array)
			if (item == null)
				item = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
			else
				clearer(item);
	}
	public static void ClearInstantiatedReference<T>(this T target) where T : class {
		Action<object> clearer = _clearers.GetOrAdd(typeof(T), CreateClearer);
		clearer(target);
	}
	public static void CopyInstantiatedReferenceTo<T>(this T source, T dest) where T : class {
		Action<object, object> copier = _copiers.GetOrAdd(typeof(T), CreateCopier);
		copier(source, dest);
	}
	public static T CloneInstance<T>(this T source) where T : class, new() {
		Action<object, object> copier = _copiers.GetOrAdd(typeof(T), CreateCopier);
		T dest = new T();
		copier(source, dest);
		return dest;
	}
	public static Action<object> CreateClearer(Type type) {
		var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (fields.Length == 0)
			return _ => { }; // nothing to clear

		// DynamicMethod signature: void Clear(object target)
		var dm = new DynamicMethod("Clear_" + type.Name, null, new[] { typeof(object) }, true);
		var il = dm.GetILGenerator();

		foreach (var field in fields) {
			il.Emit(OpCodes.Ldarg_0); // load object
			il.Emit(OpCodes.Castclass, type); // cast to actual type

			if (field.FieldType.IsValueType) {
				var local = il.DeclareLocal(field.FieldType);
				il.Emit(OpCodes.Ldloca_S, local);
				il.Emit(OpCodes.Initobj, field.FieldType);
				il.Emit(OpCodes.Ldloc, local);
			}
			else {
				il.Emit(OpCodes.Ldnull);
			}

			il.Emit(OpCodes.Stfld, field);
		}

		il.Emit(OpCodes.Ret);

		return (Action<object>)dm.CreateDelegate(typeof(Action<object>));
	}
	public static Action<object, object> CreateCopier(Type type) {
		var fields = type.GetFields(BindingFlags.Instance |
									BindingFlags.Public |
									BindingFlags.NonPublic);

		var dm = new DynamicMethod("Copy_" + type.Name,
									 null,
									 new[] { typeof(object), typeof(object) },
									 true);

		ILGenerator il = dm.GetILGenerator();

		foreach (var field in fields) {
			il.Emit(OpCodes.Ldarg_1);
			il.Emit(OpCodes.Castclass, type);

			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Castclass, type);

			il.Emit(OpCodes.Ldfld, field);

			il.Emit(OpCodes.Stfld, field);
		}

		il.Emit(OpCodes.Ret);

		return (Action<object, object>)dm.CreateDelegate(typeof(Action<object, object>));
	}


	/// <summary>
	/// Creates an array of class instances, where the class instances are not null, but also uninitialized (ie. a reference to an object exists,
	/// but no constructor etc was ran).
	/// </summary>
	/// <returns></returns>
	public static T[] BlankInstantiatedArray<T>(nuint length) where T : class {
		T[] ret = new T[length];
		ClearInstantiatedReferences(ret);
		return ret;
	}

	private static bool ParametersMatch(ParameterInfo[] parameters, ImmutableArray<Type> argTypes) {
		if (parameters.Length != argTypes.Length)
			return false;

		for (int i = 0; i < parameters.Length; i++) {
			Type paramType = parameters[i].ParameterType;
			Type argType = argTypes[i];

			// Allow for null arguments and assignable types
			if (argType == typeof(object))
				continue;

			if (!paramType.IsAssignableFrom(argType))
				return false;
		}

		return true;
	}

	public static T? InitSubsystem<T>(this IEngineAPI api, params object?[] parms) where T : class
		=> InitSubsystem<T>(api, out _, parms);
	public static T? InitSubsystem<T>(this IEngineAPI api, out string? error, params object?[] parms)
		where T : class {
		// Argument types for the method call.
		var argTypes = parms.Select(arg => arg?.GetType() ?? typeof(object)).ToImmutableArray();
		// The instance from IEngineAPI
		var instance = api.GetRequiredService<T>();
		// Find a method that has
		//     1. "Init" as the name.
		//     2. Matches the parameters the callee provided.
		var method = instance.GetType()
			.GetMethods()
			.Where(x => x.Name == "Init")
			.FirstOrDefault(m => ParametersMatch(m.GetParameters(), argTypes));
		// If the method returns booleans, return whatever the call provides
		if (method != null && method.ReturnType == typeof(bool)) {
			bool ok = (bool)(method.Invoke(instance, parms) ?? true);
			error = ok ? null : $"The subsystem '{typeof(T).Name}' failed to initialize.";
			return ok ? instance : null;
		}
		// Method invoke, return true
		try {
			method?.Invoke(instance, parms);
		}
		// If we don't do this try catch block then we don't get the inner exception in call stacks
		// and instead get the call stack of the target invocation exception (which is pretty useless
		// in this case)
		catch (TargetInvocationException ex) when (ex.InnerException != null) {
			ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
			throw;
		}
		error = null;
		return instance;
	}

	// Some methods for reading binary data into arrays/value references
	public static unsafe bool ReadNothing(this BinaryReader reader, int howMany) {
		while (howMany > 0) {
			if (reader.PeekChar() == -1)
				return false;
			reader.Read();
			howMany--;
		}
		return true;
	}
	public static unsafe int ReadASCIIStringInto(this BinaryReader reader, Span<char> into)
		=> ReadASCIIStringInto(reader, into, into.Length);
	public static unsafe int ReadASCIIStringInto(this BinaryReader reader, Span<char> into, int maxLength) {
		Span<byte> asciiBlock = stackalloc byte[maxLength];
		reader.Read(asciiBlock);
		return Encoding.ASCII.GetChars(asciiBlock, into);
	}
	public static unsafe bool ReadInto<T>(this BinaryReader reader, ref T into) where T : unmanaged {
		int sizeOfOne = sizeof(T);
		Span<byte> byteAlloc = stackalloc byte[sizeOfOne];
		if (sizeOfOne != reader.Read(byteAlloc))
			return false; // Not enough data.
		into = MemoryMarshal.Cast<byte, T>(byteAlloc)[0];
		return true;
	}
	public static unsafe bool ReadInto<T>(this BinaryReader reader, Span<T> into) where T : unmanaged {
		int sizeOfOne = sizeof(T);
		int sizeOfAll = sizeOfOne * into.Length;
		Span<byte> byteAlloc = stackalloc byte[sizeOfAll];
		if (sizeOfAll != reader.Read(byteAlloc))
			return false; // Not enough data.

		MemoryMarshal.Cast<byte, T>(byteAlloc).CopyTo(into);
		return true;
	}
	public static string? ReadString(this BinaryReader reader, int length) {
		Span<char> str = stackalloc char[length];
		for (int i = 0; i < length; i++) {
			if (reader.PeekChar() == -1)
				break;

			str[i] = reader.ReadChar();
		}
		return new(str);
	}
}

public static class UnmanagedUtils
{
	public static int AlignValue(this int val, nuint alignment) => (int)(((nuint)val + alignment - 1) & ~(alignment - 1));
	public static uint AlignValue(this uint val, nuint alignment) => (uint)(((nuint)val + alignment - 1) & ~(alignment - 1));
	public static nint AlignValue(this nint val, nuint alignment) => (int)(((nuint)val + alignment - 1) & ~(alignment - 1));
	public static nuint AlignValue(this nuint val, nuint alignment) => (uint)(((nuint)val + alignment - 1) & ~(alignment - 1));

	public static void SliceNullTerminatedStringInPlace(this ref Span<char> span) {
		int index = System.MemoryExtensions.IndexOf(span, '\0');
		if (index == -1)
			return;
		span = span[..index];
	}
	public static bool ReadToStruct<T>(this Stream sr, ref T str) where T : struct {
		Span<byte> block = MemoryMarshal.AsBytes(new Span<T>(ref str));
		return sr.Read(block) != 0;
	}
	public static Span<char> SliceNullTerminatedString(this Span<char> span) {
		int index = System.MemoryExtensions.IndexOf(span, '\0');
		if (index == -1)
			return span;
		return span[..index];
	}

	public static ReadOnlySpan<char> GetFileExtension(this ReadOnlySpan<char> filepath) {
		for (int length = filepath.Length, i = length - 1; i >= 0; i--) {
			if (filepath[i] == '.')
				return filepath[(i + 1)..];
		}
		return "";
	}
	public static ReadOnlySpan<char> SliceNullTerminatedString(this ReadOnlySpan<char> span) {
		int index = System.MemoryExtensions.IndexOf(span, '\0');
		if (index == -1)
			return span;
		return span[..index];
	}
	public static ReadOnlySpan<byte> SliceNullTerminatedString(this ReadOnlySpan<byte> span) {
		int index = System.MemoryExtensions.IndexOf(span, (byte)0);
		if (index == -1)
			return span;
		return span[..index];
	}

	public static ReadOnlySpan<char> SliceSafe(this ReadOnlySpan<char> span, int to) {
		return span[..Math.Min(span.Length, to)];
	}


	public static void EnsureCount<T>(this List<T> list, int ensureTo) where T : new() {
		list.EnsureCapacity(ensureTo);

		while (list.Count < ensureTo)
			list.Add(new T());
	}

	public static void EnsureCountDefault<T>(this List<T?> list, int ensureTo) {
		list.EnsureCapacity(ensureTo);

		while (list.Count < ensureTo)
			list.Add(default);
	}

	public static void EnsureCountNew<T>(this List<T> list, int ensureTo) where T : new() {
		list.EnsureCapacity(ensureTo);

		while (list.Count < ensureTo)
			list.Add(new());
	}

	public static void SetCount<T>(this List<T> list, int ensureTo) where T : struct {
		list.Clear();
		list.EnsureCapacity(ensureTo);
		while (list.Count < ensureTo)
			list.Add(new());
	}

	public static void SetSize<T>(this List<T?> list, int ensureTo) {
		list.EnsureCapacity(ensureTo);

		while (list.Count > ensureTo)
			list.RemoveAt(list.Count - 1);

		while (list.Count < ensureTo)
			list.Add(default);
	}

	public static void EnsureCapacity<T>(this ref Memory<T> list, int ensureTo) {
		if (list.Length < ensureTo)
			list = new T[ensureTo];
	}

	public static void SetSizeInitialized<T>(this List<T> list, int ensureTo) where T : new() {
		list.EnsureCapacity(ensureTo);

		while (list.Count > ensureTo)
			list.RemoveAt(list.Count - 1);

		while (list.Count < ensureTo)
			list.Add(new());
	}

	public static unsafe ulong Hash(this ReadOnlySpan<char> str, bool invariant = true) {
		// This might be a breaking change 12/21/2025
		// But this should have been done from the start as a major consumer of potentially-not-null-terminated-strings
		str = str.SliceNullTerminatedString();

		if (str.IsEmpty || str.Length == 0)
			return 0;

		ulong hash;

		if (invariant) {
			bool veryLarge = str.Length > 1024;
			if (veryLarge) {
				char[] lowerBuffer = ArrayPool<char>.Shared.Rent(str.Length);
				str.ToLowerInvariant(lowerBuffer);
				hash = XXH64.DigestOf(MemoryMarshal.Cast<char, byte>(lowerBuffer));
				ArrayPool<char>.Shared.Return(lowerBuffer, true);
			}
			else {
				Span<char> lowerBuffer = stackalloc char[str.Length];
				str.ToLowerInvariant(lowerBuffer);
				hash = XXH64.DigestOf(MemoryMarshal.Cast<char, byte>(lowerBuffer));
			}
		}
		else
			hash = XXH64.DigestOf(MemoryMarshal.Cast<char, byte>(str));

		return hash;
	}

	public static unsafe ulong Hash<T>(this T target) where T : unmanaged {
		ref T t = ref target;
		fixed (T* ptr = &t) {
			Span<byte> data = new(ptr, Unsafe.SizeOf<T>());
			return XXH64.DigestOf(data);
		}
	}

	public static unsafe ulong Hash<T>(this Span<T> target) where T : unmanaged {
		if (target.IsEmpty) return 0;
		ReadOnlySpan<byte> data = MemoryMarshal.Cast<T, byte>(target);
		return XXH64.DigestOf(data);
	}

	public static void FileBase(this ReadOnlySpan<char> inSpan, Span<char> outSpan) {
		// Strip inSpan until we reach a .
		while (inSpan.Length > 0 && inSpan[^1] != '.')
			inSpan = inSpan[..^1];
		// Strip the period
		inSpan = inSpan[..^1];

		// Then repeat the same process, except for a slash
		int lenPtr = inSpan.Length - 1;
		while (lenPtr > 0 && (inSpan[lenPtr] != '/' && inSpan[lenPtr] != '\\'))
			lenPtr--;
		// This should be the final span
		inSpan = inSpan[(lenPtr + 1)..];
		// Then copy 
		inSpan.ClampedCopyTo(outSpan);
	}

	public static ReadOnlySpan<char> UnqualifiedFileName(this ReadOnlySpan<char> input) {
		if (input[0] == '\0') return input;
		if (input.IsEmpty) return input;

		for (int i = input.Length - 1; i >= 0; i--) {
			if (input[i] == '\\' || input[i] == '/')
				return input[(i + 1)..];
		}

		return input;
	}

	/// <summary>
	/// Attempts to parse various elements of a filepath 
	/// </summary>
	/// <param name="inStr"></param>
	/// <param name="slashSeparators"></param>
	/// <param name="directoryName"></param>
	/// <param name="fileName"></param>
	/// <param name="fileExtension"></param>
	public static void FileInfo(this ReadOnlySpan<char> inStr, Span<Range> slashSeparators, out ReadOnlySpan<char> directoryName, out ReadOnlySpan<char> fileName, out ReadOnlySpan<char> fileExtension) {
		int slashSepIdx = 0;
		int lastSlashIdx = -1;
		int lastDotIdx = -1;
		int lastMetRange = 0;
		for (int i = 0; i < inStr.Length; i++) {
			char c = inStr[i];
			switch (c) {
				case '/':
				case '\\':
					lastSlashIdx = i;
					lastDotIdx = -1;
					if (!slashSeparators.IsEmpty && slashSepIdx < slashSeparators.Length) {
						slashSeparators[slashSepIdx++] = new(lastMetRange, i - 1);
						lastMetRange = i + 1;
					}
					break;
				case '.':
					lastDotIdx = i;
					break;
			}
		}

		if (lastSlashIdx != -1)
			directoryName = inStr[..lastSlashIdx];
		else
			directoryName = string.Empty;

		if (lastDotIdx == -1) {
			if (lastSlashIdx == -1)
				fileName = inStr;
			else
				fileName = inStr[(lastSlashIdx + 1)..];
			// no extension
			fileExtension = null;
		}
		else {
			if (lastSlashIdx == -1) {
				fileName = inStr[..lastDotIdx];
				fileExtension = inStr[(lastDotIdx + 1)..];
			}
			else {
				fileName = inStr[(lastSlashIdx + 1)..lastDotIdx];
				fileExtension = inStr[(lastDotIdx + 1)..];
			}
		}
	}

	public static void FileBase(this string inStr, Span<char> outSpan) => FileBase(inStr.AsSpan(), outSpan);
	public static void FileInfo(this string inStr, Span<Range> slashSeparators, out ReadOnlySpan<char> directoryName, out ReadOnlySpan<char> fileName, out ReadOnlySpan<char> fileExtension) => FileInfo(inStr.AsSpan(), slashSeparators, out directoryName, out fileName, out fileExtension);
	/// <summary>
	/// Compare two string-spans against each other, with invariant casing and invariant slash usage.
	/// </summary>
	/// <param name="str1"></param>
	/// <param name="str2"></param>
	/// <returns></returns>
	public static bool PathEquals(this string str1, ReadOnlySpan<char> str2) => PathEquals(str1.AsSpan(), str2);
	/// <summary>
	/// Compare two string-spans against each other, with invariant casing and invariant slash usage.
	/// </summary>
	/// <param name="str1"></param>
	/// <param name="str2"></param>
	/// <returns></returns>
	public static bool PathEquals(this ReadOnlySpan<char> str1, ReadOnlySpan<char> str2) {
		if (str1.Length != str2.Length) return false;
		for (int i = 0; i < str1.Length; i++) {
			char c1 = char.ToLowerInvariant(str1[i]);
			char c2 = char.ToLowerInvariant(str2[i]);
			if (c1 != c2) {
				if ((c1 == '\\' && c2 == '/') || (c1 == '/' && c2 == '\\'))
					continue; // Slash invariant
				else
					return false;
			}
		}
		return true;
	}
	/// <summary>
	/// Compare two string-spans against each other, with invariant casing and invariant slash usage.
	/// </summary>
	/// <param name="str1"></param>
	/// <param name="str2"></param>
	/// <returns></returns>
	public static bool PathStartsWith(this string str1, ReadOnlySpan<char> str2) => PathStartsWith(str1.AsSpan(), str2);
	/// <summary>
	/// Compare two string-spans against each other, with invariant casing and invariant slash usage.
	/// </summary>
	/// <param name="str1"></param>
	/// <param name="str2"></param>
	/// <returns></returns>
	public static bool PathStartsWith(this ReadOnlySpan<char> str1, ReadOnlySpan<char> str2) {
		for (int i = 0; i < Math.Min(str1.Length, str2.Length); i++) {
			char c1 = char.ToLowerInvariant(str1[i]);
			char c2 = char.ToLowerInvariant(str2[i]);
			if (c1 != c2) {
				if ((c1 == '\\' && c2 == '/') || (c1 == '/' && c2 == '\\'))
					continue; // Slash invariant
				else
					return false;
			}
		}

		return true;
	}
	public static unsafe ulong Hash(this string target, bool invariant = true) => Hash((ReadOnlySpan<char>)target, invariant);
	public static char Nibble(this char c) {
		if ((c >= '0') && (c <= '9'))
			return (char)(c - '0');

		if ((c >= 'A') && (c <= 'F'))
			return (char)(c - 'A' + 0x0a);

		if ((c >= 'a') && (c <= 'f'))
			return (char)(c - 'a' + 0x0a);

		return '0';
	}

	public static unsafe void ZeroOut<T>(this T[] array) where T : unmanaged {
		fixed (T* ptr = array) {
			for (int i = 0, c = array.Length; i < c; i++)
				ptr[i] = default;
		}
	}

	public static string[] Split(this ReadOnlySpan<char> input, char separator) {
		Span<Range> ranges = stackalloc Range[64];
		var splits = input.Split(ranges, ' ');
		string[] array = new string[splits];
		for (int i = 0; i < splits; i++) {
			array[i] = new(input[ranges[i]]);
		}
		return array;
	}

	public static void EatWhiteSpace(this StringReader buffer) {
		if (buffer.IsValid()) {
			while (buffer.IsValid()) {
				if (!char.IsWhiteSpace(buffer.PeekChar()))
					break;
				buffer.Read();
			}
		}
	}
	public static bool EatCPPComment(this StringReader buffer) {
		if (buffer.IsValid()) {
			ReadOnlySpan<char> peek = buffer.Peek(2);
			if (!peek.Equals("//", StringComparison.OrdinalIgnoreCase))
				return false;

			buffer.Seek(2, SeekOrigin.Current);

			for (char c = buffer.GetChar(); buffer.IsValid(); c = buffer.GetChar()) {
				if (c == '\n')
					break;
			}
			return true;
		}

		return false;
	}

	// Wow this sucks
	private static readonly FieldInfo? posField = typeof(StringReader)
		.GetField("_pos", BindingFlags.NonPublic | BindingFlags.Instance);
	private static readonly FieldInfo? strField = typeof(StringReader)
		.GetField("_s", BindingFlags.NonPublic | BindingFlags.Instance);
	private static void EnsureAvailable() {
		if (posField == null || strField == null)
			throw new InvalidOperationException("Reflection failed: StringReader internal fields not found. Implementation may have changed.");
	}
	public static int TellMaxPut(this StringReader buffer) {
		string? underlying = (string?)strField!.GetValue(buffer);
		if (underlying == null)
			return -1;

		return underlying.Length;
	}
	public static int TellGet(this StringReader buffer) {
		int current = (int)posField!.GetValue(buffer)!;
		return current;
	}
	public static ReadOnlySpan<char> Peek(this StringReader buffer, int length) {
		string? underlying = (string?)strField!.GetValue(buffer);
		if (underlying == null)
			return null;

		int current = (int)posField!.GetValue(buffer)!;
		return underlying.AsSpan()[current..Math.Min(current + length, underlying.Length)];
	}
	public static ReadOnlySpan<char> PeekToEnd(this StringReader buffer) {
		string? underlying = (string?)strField!.GetValue(buffer);
		if (underlying == null)
			return null;

		int current = (int)posField!.GetValue(buffer)!;
		return underlying.AsSpan()[current..];
	}
	public static int Seek(this StringReader reader, int offset, SeekOrigin origin) {
		if (reader == null) throw new ArgumentNullException(nameof(reader));
		EnsureAvailable();

		string? underlying = (string?)strField!.GetValue(reader);
		if (underlying == null)
			throw new InvalidOperationException("Underlying string is null.");

		int length = underlying.Length;
		int current = (int)posField!.GetValue(reader)!;
		long target;
		switch (origin) {
			case SeekOrigin.Begin:
				target = offset;
				break;
			case SeekOrigin.Current:
				target = current + offset;
				break;
			case SeekOrigin.End:
				target = length + offset;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(origin), origin, null);
		}

		if (target < 0) target = 0;
		if (target > length) target = length;

		int newPos = (int)target;
		posField.SetValue(reader, newPos);
		return newPos;
	}


	public static char PeekChar(this StringReader buffer) => (char)buffer.Peek();
	public static char GetChar(this StringReader buffer) => (char)buffer.Read();
	public static bool IsValid(this StringReader buffer) => buffer.Peek() >= 0;
	public static int ParseToken(this StringReader buffer, in CharacterSet breaks, Span<char> tokenBuf, bool parseComments = true) {
		while (true) {
			if (!buffer.IsValid())
				return -1;

			buffer.EatWhiteSpace();
			if (parseComments) {
				if (!buffer.EatCPPComment())
					break;
			}
			else
				break;
		}

		char c = buffer.GetChar();
		if (c == '\"') {
			int len = 0;
			while (buffer.IsValid()) {
				c = buffer.GetChar();
				if (c == '\"' || c == 0) {
					return len;
				}
				tokenBuf[len] = c;
				if (++len == tokenBuf.Length) {
					return tokenBuf.Length;
				}
			}

			return len;
		}

		if (breaks.Contains(c)) {
			tokenBuf[0] = c;
			return 1;
		}

		int wordLen = 0;
		while (true) {
			tokenBuf[wordLen] = c;
			if (++wordLen == tokenBuf.Length) {
				return tokenBuf.Length;
			}

			int next = buffer.Read();
			if (next < 0)
				break;

			c = (char)next;

			if (breaks.Contains(c) || c == '\"' || (c > '\0' && c <= ' ')) {
				buffer.Seek(-1, SeekOrigin.Current);
				break;
			}
		}

		return wordLen;
	}
}


/// <summary>
/// Various C# reflection utilities
/// </summary>
public static class GlobalReflectionUtils
{
	public static Span<O> reinterpret<I, O>(Span<I> value) where I : unmanaged where O : unmanaged => value.Cast<I, O>();
	public static ref O reinterpret<I, O>(ref I value) where I : unmanaged where O : unmanaged => ref new Span<I>(ref value).Cast<I, O>()[0];
	public static ReadOnlySpan<O> const_reinterpret<I, O>(ReadOnlySpan<I> value) where I : unmanaged where O : unmanaged => value.Cast<I, O>();
	public static ref readonly O const_reinterpret<I, O>(ref readonly I value) where I : unmanaged where O : unmanaged => ref new ReadOnlySpan<I>(in value).Cast<I, O>()[0];


	public static string[] GeneratePaddedStrings(int max) => GeneratePaddedStrings(0, max);
	public static string[] GeneratePaddedStrings(int min, int max) {
		string[] arr = new string[max - min + 1];
		for (int i = 0; i < arr.Length; i++) {
			arr[i] = (min + i).ToString("D3");
		}
		return arr;
	}
}
public static class ReflectionUtils
{
	public static bool TryToDelegate<T>(this MethodInfo m, object? instance, [NotNullWhen(true)] out T? asDelegate) where T : Delegate {
		return (asDelegate =
			(T?)(instance == null
				? Delegate.CreateDelegate(typeof(T), m, false)
				: Delegate.CreateDelegate(typeof(T), instance, m, false))
			) != null;
	}

	public static bool TryExtractMethodDelegate<T>(this Type type, object? instance, Func<MethodInfo, bool> preFilter, [NotNullWhen(true)] out T? asDelegate) where T : Delegate {
		if (TryFindMatchingMethod(type, typeof(T), preFilter, out MethodInfo? methodInfo) && TryToDelegate(methodInfo, instance, out asDelegate))
			return true;

		asDelegate = null;
		return false;
	}

	public static bool DoesMethodMatch(this MethodInfo m, Type[] delegateParams, Type delegateReturn, Func<MethodInfo, bool>? preFilter = null) {
		if (preFilter != null)
			return preFilter(m);

		if (m.ReturnType != delegateReturn)
			return false;

		var methodParams = m.GetParameters().Select(p => p.ParameterType).ToArray();
		if (methodParams.Length != delegateParams.Length)
			return false;

		for (int i = 0; i < methodParams.Length; i++) {
			if (methodParams[i] != delegateParams[i])
				return false;
		}

		return true;
	}
	public static MethodInfo? FindMatchingMethod(this Type targetType, Type[] delegateParams, Type delegateReturn, Func<MethodInfo, bool>? preFilter = null)
		=> targetType
			.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
			.FirstOrDefault(m => DoesMethodMatch(m, delegateParams, delegateReturn, preFilter));
	public static MethodInfo? FindMatchingMethod(this Type targetType, Type delegateType, Func<MethodInfo, bool>? preFilter = null) {
		if (!typeof(Delegate).IsAssignableFrom(delegateType))
			throw new ArgumentException("delegateType must be a delegate", nameof(delegateType));

		var invoke = delegateType.GetMethod("Invoke")!;
		var delegateParams = invoke.GetParameters().Select(p => p.ParameterType).ToArray();
		var delegateReturn = invoke.ReturnType;
		return FindMatchingMethod(targetType, delegateParams, delegateReturn, preFilter);
	}
	public static MethodInfo? FindMatchingMethod<T>(this Type targetType, Func<MethodInfo, bool>? preFilter = null) where T : Delegate => FindMatchingMethod(targetType, typeof(T), preFilter);


	public static bool TryFindMatchingMethod(this Type targetType, Type[] delegateParams, Type delegateReturn, Func<MethodInfo, bool>? preFilter, [NotNullWhen(true)] out MethodInfo? info) {
		info = FindMatchingMethod(targetType, delegateParams, delegateReturn, preFilter);
		return info != null;
	}

	public static bool TryFindMatchingMethod(this Type targetType, Type delegateType, Func<MethodInfo, bool>? preFilter, [NotNullWhen(true)] out MethodInfo? info) {
		info = FindMatchingMethod(targetType, delegateType, preFilter);
		return info != null;
	}

	public static bool TryFindMatchingMethod<T>(this Type targetType, Func<MethodInfo, bool>? preFilter, [NotNullWhen(true)] out MethodInfo? info) where T : Delegate {
		info = FindMatchingMethod<T>(targetType, preFilter);
		return info != null;
	}
	static IEnumerable<Type> safeTypeGet(Assembly assembly) {
		if (!IsSourceEngineAssembly(assembly))
			yield break;

		IEnumerable<Type?> types;
		try {
			types = assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException e) {
			types = e.Types;
		}

		foreach (var t in types.Where(t => t != null))
			yield return t!;
	}

	public static bool IsSourceEngineAssembly(Assembly assembly) =>
		assembly.GetCustomAttribute<SourceDllAttribute>() != null;

	public static IEnumerable<Assembly> GetAssemblies()
		=> AppDomain.CurrentDomain.GetAssemblies().Where(IsSourceEngineAssembly);
	public static IEnumerable<Type> GetLoadedTypes()
		=> AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(safeTypeGet);

	public static IEnumerable<KeyValuePair<Type, T>> GetLoadedTypesWithAttribute<T>() where T : Attribute {
		foreach (var type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(safeTypeGet)) {
			T? attr = type.GetCustomAttribute<T>();
			if (attr != null)
				yield return new(type, attr);
		}
	}

	public static IEnumerable<KeyValuePair<Type, T>> GetTypesWithAttribute<T>(this Assembly assembly) where T : Attribute {
		foreach (var type in assembly.GetTypes()) {
			T? attr = type.GetCustomAttribute<T>();
			if (attr != null)
				yield return new(type, attr);
		}
	}

	public static IEnumerable<KeyValuePair<Type, T>> GetTypesWithAttributeMulti<T>(this Assembly assembly) where T : Attribute {
		foreach (var type in assembly.GetTypes()) {
			foreach (var attr in type.GetCustomAttributes<T>())
				yield return new(type, attr);
		}
	}

	public static IEnumerable<KeyValuePair<ConstructorInfo, T>> GetConstructorsWithAttribute<T>(this Type type) where T : Attribute {
		foreach (var constructor in type.GetConstructors()) {
			T? attr = type.GetCustomAttribute<T>();
			if (attr != null)
				yield return new(constructor, attr);
		}
	}
	public static IEnumerable<KeyValuePair<PropertyInfo, T>> GetPropertiesWithAttribute<T>(this Type type) where T : Attribute {
		foreach (var prop in type.GetProperties()) {
			T? attr = type.GetCustomAttribute<T>();
			if (attr != null)
				yield return new(prop, attr);
		}
	}
	public static IEnumerable<KeyValuePair<FieldInfo, T>> GetFieldsWithAttribute<T>(this Type type) where T : Attribute {
		foreach (var field in type.GetFields()) {
			T? attr = type.GetCustomAttribute<T>();
			if (attr != null)
				yield return new(field, attr);
		}
	}
}

/// <summary>
/// Marks the class as being injectable into the <see cref="IEngineAPI"/> dependency injection collection.
/// <br/>
/// Is handled by <see cref="Source.Engine.EngineBuilder"/> later on.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class EngineComponentAttribute : Attribute;

public unsafe ref struct ASCIIStringView : IDisposable
{
	char* str;
	int chars;
	public ASCIIStringView(ReadOnlySpan<byte> data) {
		int indexOfNullTerminator = System.MemoryExtensions.IndexOf(data, (byte)0);
		if (indexOfNullTerminator != -1)
			data = data[..indexOfNullTerminator];

		chars = Encoding.ASCII.GetCharCount(data);
		str = (char*)NativeMemory.Alloc((nuint)chars, sizeof(char));
		Encoding.ASCII.GetChars(data, new Span<char>(str, chars));
	}

	public static implicit operator ReadOnlySpan<char>(ASCIIStringView view) => new(view.str, view.chars);

	public void Dispose() {
		NativeMemory.Free(str);
		chars = 0;
		str = null;
	}
}

public static class StructExts
{
	extension(Rectangle rect)
	{
		public int X0 { get => rect.X; set => rect.X = value; }
		public int Y0 { get => rect.Y; set => rect.Y = value; }
		public int X1 { get => rect.X + rect.Width; set => rect.Width = (value - rect.X); }
		public int Y1 { get => rect.Y + rect.Height; set => rect.Height = (value - rect.Y); }
	}
}
public static class SpanExts
{
	public static int ClampedCopyTo<T>(this ReadOnlySpan<T> source, Span<T> dest) {
		if (dest.Length < source.Length) {
			// We only copy as much as we can fit.
			source[..dest.Length].CopyTo(dest);
			return dest.Length;
		}

		source.CopyTo(dest);
		return source.Length;
	}

	// Useful for debugging
	public static void SaveImageToFile(this Span<byte> data, ReadOnlySpan<char> path, int wide, int tall)
		=> SaveImageToFile(MemoryMarshal.Cast<byte, Color>(data), path, wide, tall);
	public static void SaveImageToFile(this Span<Color> data, ReadOnlySpan<char> path, int wide, int tall) {
		using Bitmap bitmap = new(wide, tall);
		BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, wide, tall), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
		unsafe {
			Span<byte> writeTarget = new Span<byte>((void*)bmpData.Scan0, wide * tall * 4);
			for (int y = 0; y < tall; y++) {
				for (int x = 0; x < wide; x++) {
					ref Color c = ref data[x + (y * wide)];
					int index = y * bmpData.Stride + x * 4;
					writeTarget[index] = c.R;
					writeTarget[index + 1] = c.G;
					writeTarget[index + 2] = c.B;
					writeTarget[index + 3] = c.A;
				}
			}
		}
		bitmap.UnlockBits(bmpData);
		bitmap.Save(new(path));
	}

	public static int ClampedCopyTo<T>(this Span<T> source, Span<T> dest) {
		if (dest.Length < source.Length) {
			// We only copy as much as we can fit.
			source[..dest.Length].CopyTo(dest);
			return dest.Length;
		}

		source.CopyTo(dest);
		return source.Length;
	}

	public static Span<char> StripExtension(this Span<char> incoming, Span<char> outgoing) {
		int index = incoming.LastIndexOf('.');
		incoming.CopyTo(outgoing);
		if (index == -1)
			return outgoing;
		for (int i = index; i < incoming.Length; i++)
			outgoing[i] = '\0';

		return outgoing[..index];
	}

	public static Span<char> StripExtension(this ReadOnlySpan<char> incoming, Span<char> outgoing) {
		int index = incoming.LastIndexOf('.');
		incoming.CopyTo(outgoing);
		if (index == -1)
			return outgoing;
		for (int i = index; i < incoming.Length; i++)
			outgoing[i] = '\0';

		return outgoing[..index];
	}
}


// Saving for a rainy day, if it's ever needed...
/*
public delegate ref T RefFn<T>();
/// <summary>
/// A more complicated way of boxing a ref to a structure. Should be used conservatively, as it will allocate more memory 
/// than a normal box (ie. generating an anonymous lambda) but it is very convenient and avoids unsafe code/keeps GC references proper.
/// </summary>
/// <typeparam name="T"></typeparam>
public class BoxRefPtr<T> {
	RefFn<T> refFn;
	public BoxRefPtr(RefFn<T> refFn) => this.refFn = refFn;
	public ref T Deref() => ref refFn();
}
*/

public struct SafeFieldPointer<TOwner, TType>
{
	public delegate ref TType GetRefFn(TOwner owner);

	public static readonly SafeFieldPointer<TOwner, TType> Null = new();

	TOwner? owner;
	GetRefFn? refFn;

	public readonly Type OwnerType => typeof(TOwner);
	public readonly Type TargetType => typeof(TType);

	[MemberNotNullWhen(false, nameof(owner), nameof(refFn))] public readonly bool IsNull => owner == null || refFn == null;

	public SafeFieldPointer() { }
	public SafeFieldPointer(TOwner owner, GetRefFn refFn) {
		this.owner = owner;
		this.refFn = refFn;
	}

	public TOwner? Owner { readonly get => owner; set => owner = value; }
	public GetRefFn? RefFn { readonly get => refFn; set => refFn = value; }

	public readonly ref TType Get() {
		if (IsNull)
			throw new NullReferenceException("Owner and/or GetRefFn are null.");
		return ref refFn(owner);
	}
}


public struct AnonymousSafeFieldPointer<TType>
{
	public delegate ref TType GetRefFn(object owner);

	public static readonly AnonymousSafeFieldPointer<TType> Null = new();

	object? owner;
	GetRefFn? refFn;

	[MemberNotNullWhen(false, nameof(owner), nameof(refFn))] public readonly bool IsNull => owner == null || refFn == null;

	public AnonymousSafeFieldPointer() { }
	public AnonymousSafeFieldPointer(GetRefFn refFn) {
		this.refFn = refFn;
	}
	public AnonymousSafeFieldPointer(object owner, GetRefFn refFn) {
		this.owner = owner;
		this.refFn = refFn;
	}

	public object? Owner { readonly get => owner; set => owner = value; }
	public GetRefFn? RefFn { readonly get => refFn; set => refFn = value; }

	public readonly ref TType Get() {
		if (IsNull)
			throw new NullReferenceException("Owner and/or GetRefFn are null.");
		return ref refFn(owner);
	}
}

public ref struct SpanBinaryReader
{
	ReadOnlySpan<byte> contents;
	int ptr;
	public SpanBinaryReader(ReadOnlySpan<byte> contents) {
		this.contents = contents;
		this.ptr = 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Advance(int bytes) {
		ptr += bytes;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Advance<T>(int elements) where T : unmanaged {
		ptr += sizeof(T) * elements;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public ReadOnlySpan<byte> ReadBytes(int bytes) {
		ReadOnlySpan<byte> ret = contents[ptr..(ptr + bytes)];
		ptr += bytes;
		return ret;
	}
	/*
	public byte ReadUInt8() => ReadBytes(sizeof(byte))[0];
	public sbyte ReadInt8() => (sbyte)ReadBytes(sizeof(sbyte))[0];
	public ushort ReadUInt16() => ReadBytes(sizeof(ushort)).Cast<byte, ushort>()[0];
	public short ReadInt16() => ReadBytes(sizeof(short)).Cast<byte, short>()[0];
	public uint ReadUInt32() => ReadBytes(sizeof(uint)).Cast<byte, uint>()[0];
	public int ReadInt32() => ReadBytes(sizeof(int)).Cast<byte, int>()[0];
	public ulong ReadUInt64() => ReadBytes(sizeof(ulong)).Cast<byte, ulong>()[0];
	public long ReadInt64() => ReadBytes(sizeof(long)).Cast<byte, long>()[0];
	public float ReadFloat() => ReadBytes(sizeof(float)).Cast<byte, float>()[0];
	public double ReadDouble() => ReadBytes(sizeof(double)).Cast<byte, double>()[0];
	*/

	public unsafe T Read<T>() where T : unmanaged => ReadBytes(sizeof(T)).Cast<byte, T>()[0];
	public unsafe void Read<T>(out T value) where T : unmanaged => value = ReadBytes(sizeof(T)).Cast<byte, T>()[0];
	public unsafe void ReadInto<T>(Span<T> value) where T : unmanaged => ReadBytes(sizeof(T) * value.Length).Cast<byte, T>().CopyTo(value);
}


public ref struct SpanBinaryWriter
{
	Span<byte> contents;
	int ptr;
	public SpanBinaryWriter(Span<byte> contents) {
		this.contents = contents;
		this.ptr = 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Advance(int bytes) {
		ptr += bytes;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public unsafe void Advance<T>(int elements) where T : unmanaged {
		ptr += sizeof(T) * elements;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void WriteBytes(ReadOnlySpan<byte> bytes) {
		bytes.CopyTo(contents[ptr..]);
		ptr += bytes.Length;
	}
	/*
	public byte ReadUInt8() => ReadBytes(sizeof(byte))[0];
	public sbyte ReadInt8() => (sbyte)ReadBytes(sizeof(sbyte))[0];
	public ushort ReadUInt16() => ReadBytes(sizeof(ushort)).Cast<byte, ushort>()[0];
	public short ReadInt16() => ReadBytes(sizeof(short)).Cast<byte, short>()[0];
	public uint ReadUInt32() => ReadBytes(sizeof(uint)).Cast<byte, uint>()[0];
	public int ReadInt32() => ReadBytes(sizeof(int)).Cast<byte, int>()[0];
	public ulong ReadUInt64() => ReadBytes(sizeof(ulong)).Cast<byte, ulong>()[0];
	public long ReadInt64() => ReadBytes(sizeof(long)).Cast<byte, long>()[0];
	public float ReadFloat() => ReadBytes(sizeof(float)).Cast<byte, float>()[0];
	public double ReadDouble() => ReadBytes(sizeof(double)).Cast<byte, double>()[0];
	*/

	public unsafe void Write<T>(in T value) where T : unmanaged {
		ReadOnlySpan<byte> bytes = new ReadOnlySpan<T>(in value).Cast<T, byte>();
		bytes.CopyTo(contents[ptr..]);
		ptr += bytes.Length;
	}
	public unsafe void Write<T>(Span<T> value) where T : unmanaged {
		for (int i = 0; i < value.Length; i++)
			Write(in value[i]);
	}
}
