using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Game.Client;

public static class PredictionCopyImpl
{
	extension(ref PredictionCopy self)
	{
		#region ehandle methods
		public DiffType CompareEHandle(ReadOnlySpan<BaseHandle> output, ReadOnlySpan<BaseHandle> input, int count) => self.BASIC_COMPARE(output, input, count, static (in ov, in iv) => ov.Index == iv.Index ? DiffType.Identical : DiffType.Differs);
		public void CopyEHandle(DiffType dt, Span<BaseHandle> output, ReadOnlySpan<BaseHandle> input, int count) => self.BASIC_COPY(dt, output, input, count);
		public void DescribeEHandle(DiffType dt, Span<BaseHandle> outdata, ReadOnlySpan<BaseHandle> indata, int size) {
			if (!self.ErrorCheck) return;
			EHANDLE invalue = indata[0], outvalue = outdata[0];

			if (dt == DiffType.Differs)
				self.ReportFieldsDiffer($"EHandles differ (net) 0x{invalue.Index:X} (pred) 0x{outvalue.Index:X}\n");

#if CLIENT_DLL
			C_BaseEntity? ent = outvalue.Get();
			if (ent != null) {
				ReadOnlySpan<char> classname = ent.GetClassname();
				if (classname.IsStringEmpty)
					classname = ent.GetType().Name;

				self.DescribeFields(dt, $"EHandle (0x{outvalue.Index:X}->{classname})");
			}
			else
				self.DescribeFields(dt, "EHandle (NULL)");

#else
		DescribeFields(dt, $"EHandle (0x{outvalue.Index:X})");
#endif
		}

		public void WatchEHandle(DiffType dt, Span<BaseHandle> outdata, ReadOnlySpan<BaseHandle> indata, int size) {
			if (self.WatchField != self.CurrentField)
				return;
#if CLIENT_DLL
			C_BaseEntity? ent = ((EHANDLE)outdata[0]).Get();
			if (ent != null) {
				ReadOnlySpan<char> classname = ent.GetClassname();
				if (classname.IsStringEmpty)
					classname = ent.GetType().Name;

				self.WatchMsg($"EHandle (0x{outdata[0].Index:X}->{classname})");
			}
			else
				self.WatchMsg("EHandle (NULL)");
#else
		WatchMsg($"EHandle (0x{outdata[0].Index:X})");
#endif
		}
		#endregion

		public int TransferData(scoped ReadOnlySpan<char> operation, int entindex, DataMap? dmap) {
			Assert(dmap != null);
			++PredictionCopy.g_nChainCount;

			TransferData_R(ref self, PredictionCopy.g_nChainCount, dmap);

			return self.ErrorCount;
		}
	}

	static class Scratch<T> where T : unmanaged
	{
		[ThreadStatic] public static T[]? Output;
		[ThreadStatic] public static T[]? Input;
	}

	public static Span<T> FrameOutput<T>(Span<byte> frame, TypeDescription field, int count, out bool writeBack) where T : unmanaged {
		writeBack = false;
		return MemoryMarshal.Cast<byte, T>(frame.Slice((int)field.PackedOffset, Unsafe.SizeOf<T>() * count));
	}

	public static ReadOnlySpan<T> FrameInput<T>(Span<byte> frame, TypeDescription field, int count) where T : unmanaged
		=> MemoryMarshal.Cast<byte, T>(frame.Slice((int)field.PackedOffset, Unsafe.SizeOf<T>() * count));

	public static Span<T> ObjectOutput<TField, T>(ref TField field, int count, out bool writeBack) where T : unmanaged {
		if (!RuntimeHelpers.IsReferenceOrContainsReferences<TField>() && (count != 1 || Unsafe.SizeOf<TField>() == Unsafe.SizeOf<T>())) {
			writeBack = false;
			return MemoryMarshal.CreateSpan(ref Unsafe.As<TField, T>(ref field), count);
		}

		T[] scratch = Scratch<T>.Output ??= new T[1];
		scratch[0] = FieldConvert<TField, T>.Convert(in field);
		writeBack = true;
		return scratch;
	}

	public static ReadOnlySpan<T> ObjectInput<TField, T>(ref TField field, int count) where T : unmanaged {
		if (!RuntimeHelpers.IsReferenceOrContainsReferences<TField>() && (count != 1 || Unsafe.SizeOf<TField>() == Unsafe.SizeOf<T>()))
			return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<TField, T>(ref field), count);

		T[] scratch = Scratch<T>.Input ??= new T[1];
		scratch[0] = FieldConvert<TField, T>.Convert(in field);
		return scratch;
	}

	public static void WriteBack<TField, T>(ref TField field) where T : unmanaged
		=> field = FieldConvert<T, TField>.Convert(in Scratch<T>.Output![0]);

	public static Span<T> NetworkArrayOutput<TElement, T>(in NetworkArray<TElement> array, out bool writeBack) where TElement : unmanaged where T : unmanaged {
		writeBack = false;
		TElement[] values = array.Value;
		return MemoryMarshal.CreateSpan(ref Unsafe.As<TElement, T>(ref MemoryMarshal.GetArrayDataReference(values)), values.Length * Unsafe.SizeOf<TElement>() / Unsafe.SizeOf<T>());
	}

	public static ReadOnlySpan<T> NetworkArrayInput<TElement, T>(in NetworkArray<TElement> array) where TElement : unmanaged where T : unmanaged {
		TElement[] values = array.Value;
		return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<TElement, T>(ref MemoryMarshal.GetArrayDataReference(values)), values.Length * Unsafe.SizeOf<TElement>() / Unsafe.SizeOf<T>());
	}

	static Span<T> GetOutput<T>(bool isObject, object? obj, Span<byte> frame, TypeDescription field, int count, out bool writeBack) where T : unmanaged {
		if (!isObject)
			return FrameOutput<T>(frame, field, count, out writeBack);

		writeBack = false;
		if (field.Accessor.TryGetSpan(obj!, count, out Span<T> span))
			return span;

		T[] scratch = Scratch<T>.Output ??= new T[1];
		scratch[0] = field.Accessor.GetValue<T>(obj!);
		writeBack = true;
		return scratch;
	}

	static ReadOnlySpan<T> GetInput<T>(bool isObject, object? obj, Span<byte> frame, TypeDescription field, int count) where T : unmanaged {
		if (!isObject)
			return FrameInput<T>(frame, field, count);

		if (field.Accessor.TryGetSpan(obj!, count, out Span<T> span))
			return span;

		T[] scratch = Scratch<T>.Input ??= new T[1];
		scratch[0] = field.Accessor.GetValue<T>(obj!);
		return scratch;
	}

	static void WriteBack<T>(object? obj, TypeDescription field, bool writeBack) where T : unmanaged {
		if (writeBack)
			field.Accessor.SetValue(obj!, Scratch<T>.Output![0]);
	}

	static readonly List<string> PooledStrings = [];
	static readonly Dictionary<string, nint> PooledStringIds = [];

	static nint AllocPooledString(string? value) {
		if (value == null)
			return 0;

		if (!PooledStringIds.TryGetValue(value, out nint id)) {
			PooledStrings.Add(value);
			id = PooledStrings.Count;
			PooledStringIds.Add(value, id);
		}
		return id;
	}

	static string? GetString(bool isObject, object? obj, Span<byte> frame, TypeDescription field) {
		if (!isObject) {
			nint id = MemoryMarshal.Read<nint>(frame[(int)field.PackedOffset..]);
			return id == 0 ? null : PooledStrings[(int)id - 1];
		}

		if (field.Accessor.TryGetSpan(obj!, 1, out Span<string?> span))
			return span[0];

		return field.Accessor.GetValue<string?>(obj!);
	}

	static void SetString(bool isObject, object? obj, Span<byte> frame, TypeDescription field, string? value) {
		if (!isObject) {
			MemoryMarshal.Write(frame[(int)field.PackedOffset..], AllocPooledString(value));
			return;
		}

		if (field.Accessor.TryGetSpan(obj!, 1, out Span<string?> span))
			span[0] = value;
		else
			field.Accessor.SetValue(obj!, value);
	}

	static readonly Dictionary<Type, PredictionCopyFieldsFn> GeneratedCopyFields = [];

	public static void RegisterCopyFields(Type dataClassType, PredictionCopyFieldsFn fn) => GeneratedCopyFields[dataClassType] = fn;

	static PredictionCopyFieldsFn GetCopyFields(DataMap dmap)
		=> dmap.PredictionCopyFields ??= GeneratedCopyFields.TryGetValue(dmap.DataClassType, out PredictionCopyFieldsFn? fn) ? fn : CopyFieldsInterpreted;

	static void TransferData_R(ref PredictionCopy self, int chainCount, DataMap dmap) {
		GetCopyFields(dmap)(ref self, chainCount, dmap, dmap.DataDesc);

		if (dmap.BaseMap != null)
			TransferData_R(ref self, chainCount, dmap.BaseMap);
	}

	public static void CopyFieldsInterpreted(ref PredictionCopy self, int chainCount, DataMap rootMap, TypeDescription[] fields) {
		self.CurrentMap = rootMap;
		if (self.CurrentClassName.IsEmpty)
			self.CurrentClassName = rootMap.DataClassName;

		bool destIsObject = self.Relationship is PredictionCopyRelationship.DataFrameToObject or PredictionCopyRelationship.ObjectToObject;
		bool srcIsObject = self.Relationship is PredictionCopyRelationship.ObjectToDataFrame or PredictionCopyRelationship.ObjectToObject;

		for (int i = 0; i < fields.Length; i++) {
			if (BeginField(ref self, chainCount, fields[i]))
				CopyField(ref self, chainCount, rootMap, fields[i], destIsObject, srcIsObject);
		}

		self.CurrentClassName = default;
	}

	public static bool BeginField(ref PredictionCopy self, int chainCount, TypeDescription field) {
		self.CurrentField = field;
		FieldTypeDescFlags flags = field.Flags;

		if (field.OverrideField != null)
			field.OverrideField.OverrideCount = chainCount;

		if (field.OverrideCount == chainCount)
			return false;

		if (field.FieldType != FieldType.Embedded) {
			if ((flags & FieldTypeDescFlags.Private) != 0)
				return false;

			if (self.Type == PredictionCopyType.NonNetworkedOnly && (flags & FieldTypeDescFlags.InSendTable) != 0)
				return false;

			if (self.Type == PredictionCopyType.NetworkedOnly && (flags & FieldTypeDescFlags.InSendTable) == 0)
				return false;
		}

		self.ShouldReport = self.ReportErrors;
		self.ShouldDescribe = true;
		return true;
	}

	public static void CopyEmbedded(ref PredictionCopy self, int chainCount, DataMap rootMap, TypeDescription field, bool destIsObject, bool srcIsObject) {
		object? dest = self.Dest_Object, src = self.Src_Object;
		Span<byte> destFrame = self.Dest_DataFrame, srcFrame = self.Src_DataFrame;
		ReadOnlySpan<char> saveName = self.CurrentClassName;
		self.CurrentClassName = field.TD!.DataClassName;

		if (srcIsObject)
			self.Src_Object = field.Accessor.GetValue<object>(src!);
		else
			self.Src_DataFrame = srcFrame[(int)field.PackedOffset..];

		if (destIsObject)
			self.Dest_Object = field.Accessor.GetValue<object>(dest!);
		else
			self.Dest_DataFrame = destFrame[(int)field.PackedOffset..];

		GetCopyFields(field.TD)(ref self, chainCount, rootMap, field.TD.DataDesc);

		if (destIsObject && field.Accessor.StoringType.IsValueType)
			field.Accessor.SetValue(dest!, self.Dest_Object);

		self.CurrentClassName = saveName;
		self.CurrentField = field;
		self.Dest_Object = dest;
		self.Src_Object = src;
		self.Dest_DataFrame = destFrame;
		self.Src_DataFrame = srcFrame;
	}

	public static void CopyField(ref PredictionCopy self, int chainCount, DataMap rootMap, TypeDescription field, bool destIsObject, bool srcIsObject) {
		int fieldSize = field.FieldSize;
		object? dest = self.Dest_Object, src = self.Src_Object;
		Span<byte> destFrame = self.Dest_DataFrame, srcFrame = self.Src_DataFrame;

		bool shouldWatch = self.WatchField == field;
		DiffType difftype;
		bool writeBack;

		switch (field.FieldType) {
			case FieldType.Embedded:
				CopyEmbedded(ref self, chainCount, rootMap, field, destIsObject, srcIsObject);
				break;
			case FieldType.Float: {
					Span<float> output = GetOutput<float>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<float> input = GetInput<float>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareFloat(output, input, fieldSize);
					self.CopyFloat(difftype, output, input, fieldSize);
					WriteBack<float>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeFloat(difftype, output, input, fieldSize);
					if (shouldWatch) self.WatchFloat(difftype, output, input, fieldSize);
				}
				break;
			case FieldType.Double: {
					Span<double> output = GetOutput<double>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<double> input = GetInput<double>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareDouble(output, input, fieldSize);
					self.CopyDouble(difftype, output, input, fieldSize);
					WriteBack<double>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeDouble(difftype, output, input, fieldSize);
					if (shouldWatch) self.WatchDouble(difftype, output, input, fieldSize);
				}
				break;
			case FieldType.String: {
					string? previous = GetString(destIsObject, dest, destFrame, field), output = previous;
					string? input = GetString(srcIsObject, src, srcFrame, field);
					difftype = self.CompareString(output, input);
					self.CopyString(difftype, ref output, input);
					if (!ReferenceEquals(output, previous))
						SetString(destIsObject, dest, destFrame, field, output);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeString(difftype, output, input);
					if (shouldWatch) self.WatchString(difftype, output, input);
				}
				break;
			case FieldType.Vector: {
					Span<Vector3> output = GetOutput<Vector3>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<Vector3> input = GetInput<Vector3>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareVector(output, input, fieldSize);
					self.CopyVector(difftype, output, input, fieldSize);
					WriteBack<Vector3>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeVector(difftype, output, input, fieldSize);
					if (shouldWatch) self.WatchVector(difftype, output, input, fieldSize);
				}
				break;
			case FieldType.Quaternion: {
					Span<Quaternion> output = GetOutput<Quaternion>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<Quaternion> input = GetInput<Quaternion>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareQuaternion(output, input, fieldSize);
					self.CopyQuaternion(difftype, output, input, fieldSize);
					WriteBack<Quaternion>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeQuaternion(difftype, output, input, fieldSize);
					if (shouldWatch) self.WatchQuaternion(difftype, output, input, fieldSize);
				}
				break;
			case FieldType.Color32: {
					Span<Color> output = GetOutput<Color>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<Color> input = GetInput<Color>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareColor(output, input, fieldSize);
					self.CopyColor(difftype, output, input, fieldSize);
					WriteBack<Color>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeData(difftype, 4 * fieldSize, MemoryMarshal.AsBytes(output), MemoryMarshal.AsBytes(input));
					if (shouldWatch) self.WatchData(difftype, 4 * fieldSize, MemoryMarshal.AsBytes(output), MemoryMarshal.AsBytes(input));
				}
				break;
			case FieldType.Boolean: {
					Span<bool> output = GetOutput<bool>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<bool> input = GetInput<bool>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareBool(output, input, fieldSize);
					self.CopyBool(difftype, output, input, fieldSize);
					WriteBack<bool>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeBool(difftype, output, input, fieldSize);
					if (shouldWatch) self.WatchBool(difftype, output, input, fieldSize);
				}
				break;
			case FieldType.Integer: {
					Span<int> output = GetOutput<int>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<int> input = GetInput<int>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareInt(output, input, fieldSize);
					self.CopyInt(difftype, output, input, fieldSize);
					WriteBack<int>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeInt(difftype, output, input, fieldSize);
					if (shouldWatch) self.WatchInt(difftype, output, input, fieldSize);
				}
				break;
			case FieldType.Short: {
					Span<short> output = GetOutput<short>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<short> input = GetInput<short>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareShort(output, input, fieldSize);
					self.CopyShort(difftype, output, input, fieldSize);
					WriteBack<short>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeShort(difftype, output, input, fieldSize);
					if (shouldWatch) self.WatchShort(difftype, output, input, fieldSize);
				}
				break;
			case FieldType.Character: {
					Span<byte> output = GetOutput<byte>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<byte> input = GetInput<byte>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareByte(output, input, fieldSize);
					self.CopyByte(difftype, output, input, fieldSize);
					WriteBack<byte>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeByte(difftype, output, input, fieldSize);
					if (shouldWatch) self.WatchByte(difftype, output, input, fieldSize);
				}
				break;
			case FieldType.StringCharacter: {
					Span<char> output = GetOutput<char>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<char> input = GetInput<char>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareChar(output, input, fieldSize);
					self.CopyChar(difftype, output, input, fieldSize);
					WriteBack<char>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeChar(difftype, output, input, fieldSize);
					if (shouldWatch) self.WatchData(difftype, 2 * fieldSize, MemoryMarshal.AsBytes(output), MemoryMarshal.AsBytes(input));
				}
				break;
			case FieldType.EHandle: {
					Span<BaseHandle> output = GetOutput<BaseHandle>(destIsObject, dest, destFrame, field, fieldSize, out writeBack);
					ReadOnlySpan<BaseHandle> input = GetInput<BaseHandle>(srcIsObject, src, srcFrame, field, fieldSize);
					difftype = self.CompareEHandle(output, input, fieldSize);
					self.CopyEHandle(difftype, output, input, fieldSize);
					WriteBack<BaseHandle>(dest, field, writeBack);
					if (self.ErrorCheck && self.ShouldDescribe) self.DescribeEHandle(difftype, output, input, fieldSize);
					if (shouldWatch) self.WatchEHandle(difftype, output, input, fieldSize);
				}
				break;
		}
	}
}
