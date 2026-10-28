using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.Common;

public static class FieldAccessorRegistry
{
	static readonly Lock RegistryLock = new();
	static readonly Dictionary<(Type Owner, string Expression, string? Name), Func<DynamicAccessor>> Scalars = [];
	static readonly Dictionary<(Type Owner, string Expression), Func<int, DynamicArrayAccessor>> Arrays = [];

	public static void Register(Type owner, string expression, string? name, Func<DynamicAccessor> factory) {
		lock (RegistryLock)
			Scalars[(owner, expression, name)] = factory;
	}

	public static void RegisterArray(Type owner, string expression, Func<int, DynamicArrayAccessor> factory) {
		lock (RegistryLock)
			Arrays[(owner, expression)] = factory;
	}

	public static DynamicAccessor Create(Type owner, string expression, string? name) {
		return TryCreate(owner, expression, name) ?? throw new KeyNotFoundException($"No generated field accessor for {owner.FullName}.{expression}");
	}

	public static DynamicAccessor? TryCreate(Type owner, string expression, string? name) {
		Func<DynamicAccessor>? factory;
		lock (RegistryLock)
			Scalars.TryGetValue((owner, expression, name), out factory);
		return factory?.Invoke();
	}

	public static DynamicArrayAccessor CreateArray(Type owner, string expression, int listMax) {
		Func<int, DynamicArrayAccessor>? factory;
		lock (RegistryLock)
			Arrays.TryGetValue((owner, expression), out factory);
		return factory?.Invoke(listMax) ?? throw new KeyNotFoundException($"No generated array field accessor for {owner.FullName}.{expression}");
	}
}

public static class FieldKeyRegistry
{
	static readonly Lock RegistryLock = new();
	static readonly Dictionary<string, int> Ids = [];

	public static int GetId(string? key) {
		if (key == null)
			return 0;

		lock (RegistryLock) {
			if (!Ids.TryGetValue(key, out int id)) {
				id = Ids.Count + 1;
				Ids.Add(key, id);
			}
			return id;
		}
	}
}

public abstract class FieldAccessor<TField> : DynamicAccessor
{
	protected FieldAccessor(Type targetType, string name, string networkName) : base(targetType, typeof(TField), name, networkName) { }

	public abstract ref TField Ref(object instance);

	public override T GetValue<T>(object instance) => FieldConvert<TField, T>.Convert(in Ref(instance));

	public override bool SetValue<T>(object instance, in T value) {
		Ref(instance) = FieldConvert<T, TField>.Convert(in value);
		return true;
	}

	public override bool TryGetSpan<T>(object instance, int count, out Span<T> span) {
		if (RuntimeHelpers.IsReferenceOrContainsReferences<TField>() || (count == 1 && Unsafe.SizeOf<TField>() != Unsafe.SizeOf<T>())) {
			span = default;
			return false;
		}

		span = MemoryMarshal.CreateSpan(ref Unsafe.As<TField, T>(ref Ref(instance)), count);
		return true;
	}
}

public abstract class NetworkArrayFieldAccessor<TElement> : FieldAccessor<NetworkArray<TElement>> where TElement : unmanaged
{
	protected NetworkArrayFieldAccessor(Type targetType, string name, string networkName) : base(targetType, name, networkName) { }

	public override bool TryGetSpan<T>(object instance, int count, out Span<T> span) {
		TElement[] array = Ref(instance).Value;
		span = MemoryMarshal.CreateSpan(ref Unsafe.As<TElement, T>(ref MemoryMarshal.GetArrayDataReference(array)), array.Length * Unsafe.SizeOf<TElement>() / Unsafe.SizeOf<T>());
		return true;
	}
}

public abstract class InlineArrayFieldAccessor<TField, TElement> : FieldAccessor<TField>
{
	protected InlineArrayFieldAccessor(Type targetType, string name, string networkName) : base(targetType, name, networkName) { }

	public override void CopyFrom<T>(object instance, Span<T> source) {
		if (typeof(T) != typeof(TElement)) {
			base.CopyFrom(instance, source);
			return;
		}

		Span<T> dest = MemoryMarshal.CreateSpan(ref Unsafe.As<TField, T>(ref Ref(instance)), Unsafe.SizeOf<TField>() / Unsafe.SizeOf<T>());
		int count = Math.Min(source.Length, dest.Length);
		source[..count].CopyTo(dest);
		dest[count..].Clear();
	}

	public override void CopyTo<T>(object instance, Span<T> target) {
		if (typeof(T) != typeof(TElement)) {
			base.CopyTo(instance, target);
			return;
		}

		Span<T> source = MemoryMarshal.CreateSpan(ref Unsafe.As<TField, T>(ref Ref(instance)), Unsafe.SizeOf<TField>() / Unsafe.SizeOf<T>());
		int count = Math.Min(source.Length, target.Length);
		source[..count].CopyTo(target);
	}
}

public abstract class ArrayFieldAccessor<TField, TElement> : DynamicArrayAccessor
{
	readonly string elementNetworkFormat;

	protected ArrayFieldAccessor(Type targetType, string name, string networkName, string elementNetworkFormat, int length, bool isList)
		: base(targetType, typeof(TField), name, networkName, new DynamicArrayInfo(typeof(TElement), () => length, isList)) {
		this.elementNetworkFormat = elementNetworkFormat;
	}

	public abstract ref TField Ref(object instance);
	public abstract ref TElement ElementRef(object instance, int index);

	public override string ElementNetworkName(int index) => string.Format(elementNetworkFormat, index);

	public override T GetValue<T>(object instance) => FieldConvert<TField, T>.Convert(in Ref(instance));

	public override bool SetValue<T>(object instance, in T value) {
		Ref(instance) = FieldConvert<T, TField>.Convert(in value);
		return true;
	}

	public override T GetElement<T>(object instance, int index) => FieldConvert<TElement, T>.Convert(in ElementRef(instance, index));

	public override bool SetElement<T>(object instance, int index, in T value) {
		ElementRef(instance, index) = FieldConvert<T, TElement>.Convert(in value);
		return true;
	}
}

public static class FieldConvert<TFrom, TTo>
{
	public delegate TTo ConvertFn(in TFrom from);

	static ConvertFn? Fn;

	public static void Register(ConvertFn fn) => Fn = fn;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static TTo Convert(in TFrom from) {
		if (typeof(TFrom) == typeof(TTo))
			return Unsafe.As<TFrom, TTo>(ref Unsafe.AsRef(in from));
		return (Fn ??= Create())(in from);
	}

	static ConvertFn Create() {
		NumericKind fromKind = NumericInfo<TFrom>.Kind, toKind = NumericInfo<TTo>.Kind;
		if (fromKind != NumericKind.None && toKind != NumericKind.None) {
			return fromKind switch {
				NumericKind.Float => static (in TFrom v) => NumericConvert.FromDouble<TTo>(NumericConvert.ReadDouble(in v)),
				NumericKind.Signed => static (in TFrom v) => NumericConvert.FromInt64<TTo>(NumericConvert.ReadInt64(in v)),
				_ => static (in TFrom v) => NumericConvert.FromUInt64<TTo>(NumericConvert.ReadUInt64(in v)),
			};
		}

		if (!typeof(TFrom).IsValueType || !typeof(TTo).IsValueType)
			return static (in TFrom v) => (TTo)(object)v!;

		throw new NotSupportedException($"No conversion from {typeof(TFrom)} to {typeof(TTo)}");
	}
}

public enum NumericKind
{
	None,
	Signed,
	Unsigned,
	Float,
	Bool
}

public static class NumericInfo<T>
{
	public static readonly NumericKind Kind = NumericConvert.Classify(typeof(T));
}

public static class NumericConvert
{
	public static NumericKind Classify(Type type) {
		if (type == typeof(nint))
			return NumericKind.Signed;
		if (type == typeof(nuint))
			return NumericKind.Unsigned;

		return Type.GetTypeCode(type) switch {
			TypeCode.Boolean => NumericKind.Bool,
			TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => NumericKind.Signed,
			TypeCode.Byte or TypeCode.UInt16 or TypeCode.UInt32 or TypeCode.UInt64 or TypeCode.Char => NumericKind.Unsigned,
			TypeCode.Single or TypeCode.Double => NumericKind.Float,
			_ => NumericKind.None
		};
	}

	public static long ReadInt64<T>(in T v) => Unsafe.SizeOf<T>() switch {
		1 => Unsafe.As<T, sbyte>(ref Unsafe.AsRef(in v)),
		2 => Unsafe.As<T, short>(ref Unsafe.AsRef(in v)),
		4 => Unsafe.As<T, int>(ref Unsafe.AsRef(in v)),
		_ => Unsafe.As<T, long>(ref Unsafe.AsRef(in v)),
	};

	public static ulong ReadUInt64<T>(in T v) => Unsafe.SizeOf<T>() switch {
		1 => Unsafe.As<T, byte>(ref Unsafe.AsRef(in v)),
		2 => Unsafe.As<T, ushort>(ref Unsafe.AsRef(in v)),
		4 => Unsafe.As<T, uint>(ref Unsafe.AsRef(in v)),
		_ => Unsafe.As<T, ulong>(ref Unsafe.AsRef(in v)),
	};

	public static double ReadDouble<T>(in T v) => Unsafe.SizeOf<T>() == 4 ? Unsafe.As<T, float>(ref Unsafe.AsRef(in v)) : Unsafe.As<T, double>(ref Unsafe.AsRef(in v));

	public static T FromInt64<T>(long v) => NumericInfo<T>.Kind switch {
		NumericKind.Float => Unsafe.SizeOf<T>() == 4 ? As<float, T>(v) : As<double, T>(v),
		NumericKind.Bool => As<bool, T>(v != 0),
		_ => Truncate<T>(unchecked((ulong)v)),
	};

	public static T FromUInt64<T>(ulong v) => NumericInfo<T>.Kind switch {
		NumericKind.Float => Unsafe.SizeOf<T>() == 4 ? As<float, T>(v) : As<double, T>(v),
		NumericKind.Bool => As<bool, T>(v != 0),
		_ => Truncate<T>(v),
	};

	public static T FromDouble<T>(double v) => NumericInfo<T>.Kind switch {
		NumericKind.Float => Unsafe.SizeOf<T>() == 4 ? As<float, T>((float)v) : As<double, T>(v),
		NumericKind.Bool => As<bool, T>(v != 0),
		NumericKind.Signed => Unsafe.SizeOf<T>() switch {
			1 => As<sbyte, T>((sbyte)v),
			2 => As<short, T>((short)v),
			4 => As<int, T>((int)v),
			_ => As<long, T>((long)v),
		},
		_ => Unsafe.SizeOf<T>() switch {
			1 => As<byte, T>((byte)v),
			2 => As<ushort, T>((ushort)v),
			4 => As<uint, T>((uint)v),
			_ => As<ulong, T>((ulong)v),
		},
	};

	static T Truncate<T>(ulong v) => Unsafe.SizeOf<T>() switch {
		1 => As<byte, T>((byte)v),
		2 => As<ushort, T>((ushort)v),
		4 => As<uint, T>((uint)v),
		_ => As<ulong, T>(v),
	};

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	static T As<TValue, T>(TValue value) => Unsafe.As<TValue, T>(ref value);
}
