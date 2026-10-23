using CommunityToolkit.HighPerformance;

using SharpCompress.Common;

using Source.Common;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Source.Common
{
	public static class DataMapConstants {
		public const float TD_MSECTOLERANCE = 0.001f;
	}

	public enum FieldType
	{
		Void = 0,         // No type or value
		Float,            // Any floating point value
		Double,           // Any double-precision floating point value
		String,           // A string ID (return from ALLOC_STRING)
		Vector,           // Any vector, QAngle, or Vector3
		Quaternion,       // A quaternion
		Integer,          // Any integer or enum
		Boolean,          // boolean, implemented as an int, I may use this as a hint for compression
		Character,        // 1 byte integer
		Byte = Character, // 1 byte integer
		Short,            // 2 byte integer
		StringCharacter,  // a utf16 character
		Color32,          // 8-bit per channel r,g,b,a (32bit color)
		Embedded,         // an embedded object with a datadesc, recursively traverse and embedded class/structure based on an additional typedescription
		Custom,           // special type that contains function pointers to it's read/write/parse functions

		ClassPtr,         // CBaseEntity *
		EHandle,          // Entity handle
		EDict,            // edict_t *

		PositionVector,  // A world coordinate (these are fixed up across level transitions automagically)
		Time,             // a floating point time (these are fixed up automatically too!)
		Tick,             // an integer tick count( fixed up similarly to time)
		ModelName,        // Engine string that is a model name (needs precache)
		SoundName,        // Engine string that is a sound name (needs precache)

		Input,            // a list of inputed data fields (all derived from CMultiInputVar)
		Function,         // A class function pointer (Think, Use, etc)

		Matrix,          // a vmatrix (output coords are NOT worldspace)

		// NOTE: Use float arrays for local transformations that don't need to be fixed up.
		WorldspaceMatrix,// A VMatrix that maps some local space to world space (translation is fixed up on level transitions)
		WorldspaceMatrix3x4, // matrix3x4_t that maps some local space to world space (translation is fixed up on level transitions)

		Interval,         // a start and range floating point interval ( e.g., 3.2->3.6 == 3.2 and 0.4 )
		ModelIndex,       // a model index
		MaterialIndex,    // a material index (using the material precache string table)

		Vector2D,         // 2 floats

		Count,        // MUST BE LAST
	}

	public enum TypeDescriptionOffset
	{
		Normal,
		Packed,
		Count
	}

	public class TypeDescription
	{
		public readonly FieldType FieldType;
		public readonly string FieldName = "";
		public readonly FieldInfo FieldInfo;
		DynamicAccessor? accessor;
		public DynamicAccessor Accessor => accessor ??= FieldAccessorRegistry.TryCreate(FieldInfo.DeclaringType!, FieldInfo.Name, null)
			?? FieldAccessorRegistry.Create(FieldInfo.DeclaringType!, FieldInfo.Name.StartsWith("__nv_", StringComparison.Ordinal) ? FieldInfo.Name[5..] : FieldInfo.Name, null);
		public nuint PackedOffset = nuint.MaxValue;
		public readonly ushort FieldSize;
		public readonly FieldTypeDescFlags Flags;
		public readonly string ExternalName = "";
		public readonly ISaveRestoreOps? SaveRestoreOps;
		public readonly Delegate? InputFunc;
		public readonly DataMap? TD;
		public TypeDescription? OverrideField;
		public int OverrideCount;
		public readonly double FieldTolerance;

		public TypeDescription(FieldType type, ReadOnlySpan<char> fieldName, FieldInfo field, ushort fieldSize, FieldTypeDescFlags flags, ReadOnlySpan<char> externalName, ISaveRestoreOps? saveRestoreOps, DataMap? td, double fieldTolerance, Delegate? inputFunc = null) {
			FieldType = type;
			FieldName = new(fieldName);
			FieldInfo = field;
			FieldSize = fieldSize;
			Flags |= flags;
			TD = td;
			ExternalName = new(externalName);
			SaveRestoreOps = saveRestoreOps;
			FieldTolerance = fieldTolerance;
			InputFunc = inputFunc;
		}

		public TypeDescription(FieldType type, ReadOnlySpan<char> fieldName, DynamicAccessor accessor, ushort fieldSize, FieldTypeDescFlags flags, DataMap? td, double fieldTolerance) {
			FieldType = type;
			FieldName = new(fieldName);
			FieldInfo = null!;
			this.accessor = accessor;
			FieldSize = fieldSize;
			Flags |= flags;
			TD = td;
			FieldTolerance = fieldTolerance;
		}
	}

	public enum FieldTypeDescFlags : ushort
	{
		Global = 0x0001, // This field is masked for global entity save/restore
		Save = 0x0002, // This field is saved to disk
		Key = 0x0004, // This field can be requested and written to by string name at load time
		Input = 0x0008, // This field can be written to by string name at run time, and a function called
		Output = 0x0010, // This field propogates it's value to all targets whenever it changes
		FunctionTable = 0x0020, // This is a table entry for a member function pointer
		Ptr = 0x0040, // This field is a pointer, not an embedded object
		Override = 0x0080, // The field is an override for one in a base class (only used by prediction system for now)
		InSendTable = 0x0100, // This field is present in a network SendTable
		Private = 0x0200, // The field is local to the client or server only (not referenced by prediction code and not replicated by networking)
		NoErrorCheck = 0x0400, // The field is part of the prediction typedescription, but doesn't get compared when checking for errors
		ModelIndex = 0x0800, // The field is a model index (used for debugging output)
		Index = 0x1000, // The field is an index into file data, used for byteswapping. 
		ViewOtherPlayer = 0x2000, // By default you can only view fields on the local player (yourself), 
		ViewOwnTeam = 0x4000, // Only show this data if the player is on the same team as the local player
		ViewNever = 0x8000, // Never show this field to anyone, even the local player (unusual)
	}

	/// <summary>
	/// Prediction copy behavior
	/// </summary>
	public enum PredictionCopyType : byte
	{
		Everything,
		NonNetworkedOnly,
		NetworkedOnly
	}

	/// <summary>
	/// Prediction copy difference type
	/// </summary>
	public enum DiffType : byte
	{
		Differs,
		Identical,
		WithinTolerance
	}

	/// <summary>
	/// The relationship between the source -> destination, ie. the operation that
	/// will be performed
	/// </summary>
	public enum PredictionCopyRelationship : byte
	{
		DataFrameToObject,
		ObjectToDataFrame,
		DataFrameToDataFrame,
		ObjectToObject
	}

	public delegate void FN_FIELD_COMPARE(ReadOnlySpan<char> classname, ReadOnlySpan<char> fieldname, ReadOnlySpan<char> fieldtype, bool networked, bool noterrorchecked, bool differs, bool withintolerance, ReadOnlySpan<char> value);

	public delegate DiffType COMPARE_FUNC<T>(in T o, in T i) where T : unmanaged;
	public delegate DiffType COMPARE_FUNC_TOL<T>(bool usetolerance, double tolerance, in T o, in T i) where T : unmanaged;
	public delegate void WatchMsgFn(ref PredictionCopy self, ReadOnlySpan<char> msg);
	public delegate void PredictionCopyFieldsFn(ref PredictionCopy self, int chainCount, DataMap rootMap, TypeDescription[] fields);
	/// <summary>
	/// The base class lives in Source.Common now, since the delegates require a ref to the prediction copy state.
	/// A lot of the logic lives in Game.Client, where the IL compilation process occurs.
	/// </summary>
	public ref struct PredictionCopy
	{
		#region static impls

		public static WatchMsgFn? WatchMsgFn;
		public void WatchMsg(ReadOnlySpan<char> txt) {
			if (WatchMsgFn == null) return;
			WatchMsgFn(ref this, txt);
		}
		#endregion
		#region fields
		public readonly PredictionCopyType Type;
		public Span<byte> Dest_DataFrame;
		public Span<byte> Src_DataFrame;
		public object? Dest_Object;
		public object? Src_Object;
		public readonly PredictionCopyRelationship Relationship;
		public readonly bool ErrorCheck;
		public readonly bool ReportErrors;
		public readonly bool PerformCopy;
		public readonly bool ShouldDescribeFields;
		public readonly FN_FIELD_COMPARE? FieldCompareFunc;

		public int ErrorCount;

		public DataMap? CurrentMap;
		public TypeDescription? WatchField;
		public TypeDescription? CurrentField;
		public ReadOnlySpan<char> CurrentClassName;

		public static int g_nChainCount = 1;
		public bool ShouldReport;
		public bool ShouldDescribe;

		public string Operation;
		#endregion
		#region constructors
		/// <summary>
		/// Dataframe (destination) <<--- Object (source)
		/// </summary>
		public PredictionCopy(PredictionCopyType type, byte[] dest, object src,
			bool countErrors = false, bool reportErrors = false, bool performCopy = true, bool describeFields = false, FN_FIELD_COMPARE? func = null) {
			Type = type;
			Dest_DataFrame = dest;
			Src_Object = src;
			Relationship = PredictionCopyRelationship.ObjectToDataFrame;

			ErrorCheck = countErrors;
			ReportErrors = reportErrors;
			PerformCopy = performCopy;
			ShouldDescribeFields = describeFields;
			FieldCompareFunc = func;
		}

		/// <summary>
		/// Object (destination) <<--- Dataframe (source)
		/// </summary>
		public PredictionCopy(PredictionCopyType type, object dest, byte[] src,
			bool countErrors = false, bool reportErrors = false, bool performCopy = true, bool describeFields = false, FN_FIELD_COMPARE? func = null) {
			Type = type;
			Dest_Object = dest;
			Src_DataFrame = src;
			Relationship = PredictionCopyRelationship.DataFrameToObject;

			ErrorCheck = countErrors;
			ReportErrors = reportErrors;
			PerformCopy = performCopy;
			ShouldDescribeFields = describeFields;
			FieldCompareFunc = func;
		}

		/// <summary>
		/// Dataframe (destination) <<--- Dataframe (source)
		/// </summary>
		public PredictionCopy(PredictionCopyType type, byte[] dest, byte[] src,
			bool countErrors = false, bool reportErrors = false, bool performCopy = true, bool describeFields = false, FN_FIELD_COMPARE? func = null) {
			Type = type;
			Dest_DataFrame = dest;
			Src_DataFrame = src;
			Relationship = PredictionCopyRelationship.DataFrameToDataFrame;

			ErrorCheck = countErrors;
			ReportErrors = reportErrors;
			PerformCopy = performCopy;
			ShouldDescribeFields = describeFields;
			FieldCompareFunc = func;
		}


		/// <summary>
		/// Object (destination) <<--- Object (source)
		/// </summary>
		public PredictionCopy(PredictionCopyType type, object dest, object src,
			bool countErrors = false, bool reportErrors = false, bool performCopy = true, bool describeFields = false, FN_FIELD_COMPARE? func = null) {
			Type = type;
			Dest_Object = dest;
			Src_Object = src;
			Relationship = PredictionCopyRelationship.ObjectToObject;

			ErrorCheck = countErrors;
			ReportErrors = reportErrors;
			PerformCopy = performCopy;
			ShouldDescribeFields = describeFields;
			FieldCompareFunc = func;
		}
		#endregion
		#region base methods
		public void DescribeFields(DiffType dt, ReadOnlySpan<char> txt) {
			if (!ShouldDescribe)
				return;

			if (FieldCompareFunc == null)
				return;

			Assert(CurrentMap != null);
			Assert(!CurrentClassName.IsEmpty);

			ReadOnlySpan<char> fieldname = "empty";
			FieldTypeDescFlags flags = 0;

			if (CurrentField != null) {
				flags = CurrentField.Flags;
				fieldname = CurrentField.FieldName != null ? CurrentField.FieldName : "NULL";
			}

			bool isnetworked = (flags & FieldTypeDescFlags.InSendTable) != 0;
			bool isnoterrorchecked = (flags & FieldTypeDescFlags.NoErrorCheck) != 0;

			FieldCompareFunc(
				CurrentClassName,
				fieldname,
				CurrentField?.FieldType.ToString(),
				isnetworked,
				isnoterrorchecked,
				dt != DiffType.Identical,
				dt == DiffType.WithinTolerance,
				txt
			);

			ShouldDescribe = false;
		}

		public void ReportFieldsDiffer(ReadOnlySpan<char> txt) {
			++ErrorCount;

			if (!ShouldReport)
				return;

			if (ShouldDescribeFields && FieldCompareFunc != null)
				return;

			Assert(CurrentMap != null);
			Assert(!CurrentClassName.IsEmpty);

			ReadOnlySpan<char> fieldname = "empty";
			FieldTypeDescFlags flags = 0;

			if (CurrentField != null) {
				flags = CurrentField.Flags;
				fieldname = CurrentField.FieldName != null ? CurrentField.FieldName : "NULL";
			}

			if (ErrorCount == 1)
				Msg("\n");

			Msg($"{ErrorCount} {CurrentClassName}::{fieldname} - {txt}");
			ShouldReport = false;
		}

		static TypeDescription? FindFieldByName_R(ReadOnlySpan<char> fieldname, DataMap? dmap) {
			if (dmap == null)
				throw new NullReferenceException("Do not pass a null DataMap into FindFieldByName.");

			int c = dmap.DataNumFields;
			for (int i = 0; i < c; i++) {
				TypeDescription td = dmap.DataDesc[i];
				if (td.FieldType == FieldType.Void)
					continue;

				if (td.FieldType == FieldType.Embedded) {
					// TODO:  this will only find the first subclass with the variable of the specified name
					//  At some point we might want to support multiple levels of overriding automatically
					TypeDescription? ret = FindFieldByName_R(fieldname, td.TD!);
					if (ret != null)
						return ret;
				}

				if (0 == stricmp(td.FieldName, fieldname))
					return td;
			}

			if (dmap.BaseMap != null)
				return FindFieldByName_R(fieldname, dmap.BaseMap);

			return null;
		}
		public static TypeDescription? FindFieldByName(ReadOnlySpan<char> fieldname, DataMap? dmap) {
			return FindFieldByName_R(fieldname, dmap);
		}

		#endregion
		#region compare methods
		public bool CanCheck() => (CurrentField!.Flags & FieldTypeDescFlags.NoErrorCheck) == 0;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public DiffType BASIC_COMPARE<T>(ReadOnlySpan<T> output, ReadOnlySpan<T> input, int count) where T : unmanaged {
			if (!ErrorCheck) return DiffType.Differs;

			if (CanCheck())
				return memcmpb(output[..count], input[..count]) ? DiffType.Identical : DiffType.Differs;

			return DiffType.Identical;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public DiffType BASIC_COMPARE<T>(ReadOnlySpan<T> output, ReadOnlySpan<T> input, int count, COMPARE_FUNC<T> fn) where T : unmanaged {
			if (!ErrorCheck) return DiffType.Differs;

			if (CanCheck()) {
				for (int i = 0; i < count; i++) {
					ref readonly T op = ref output[i];
					ref readonly T ip = ref input[i];
					DiffType dt = fn(in op, in ip);
					if (dt == DiffType.Identical)
						continue;

					return DiffType.Differs;
				}
			}

			return DiffType.Identical;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public DiffType BASIC_COMPARE_TOLERANCE<T>(ReadOnlySpan<T> output, ReadOnlySpan<T> input, int count, COMPARE_FUNC_TOL<T> fn) where T : unmanaged {
			if (!ErrorCheck) return DiffType.Differs;

			DiffType retval = DiffType.Identical;
			if (CanCheck()) {
				double tolerance = CurrentField!.FieldTolerance;
				Assert(tolerance >= 0.0);
				bool usetolerance = tolerance > 0.0;

				for (int i = 0; i < count; i++) {
					ref readonly T op = ref output[i];
					ref readonly T ip = ref input[i];
					if (memcmp(op, ip) == 0)
						continue;

					DiffType dt = fn(usetolerance, tolerance, op, ip);
					if (dt == DiffType.Identical)
						continue;
					else if (dt == DiffType.WithinTolerance) {
						retval = DiffType.WithinTolerance;
						continue;
					}

					return DiffType.Differs;
				}
			}

			return retval;
		}

		#endregion
		#region common comparisons
		public DiffType CompareShort(ReadOnlySpan<short> output, ReadOnlySpan<short> input, int count) => BASIC_COMPARE(output, input, count);
		public DiffType CompareInt(ReadOnlySpan<int> output, ReadOnlySpan<int> input, int count) => BASIC_COMPARE(output, input, count);
		public DiffType CompareByte(ReadOnlySpan<byte> output, ReadOnlySpan<byte> input, int count) => BASIC_COMPARE(output, input, count);
		public DiffType CompareChar(ReadOnlySpan<char> output, ReadOnlySpan<char> input, int count) => BASIC_COMPARE(output, input, count);
		public DiffType CompareBool(ReadOnlySpan<bool> output, ReadOnlySpan<bool> input, int count) => BASIC_COMPARE(output, input, count);
		public DiffType CompareFloat(ReadOnlySpan<float> output, ReadOnlySpan<float> input, int count) => BASIC_COMPARE_TOLERANCE<float>(output, input, count, static (usetolerance, tolerance, in op, in ip) => {
			if (usetolerance && MathF.Abs(op - ip) <= tolerance)
				return DiffType.WithinTolerance;
			return DiffType.Differs;
		});
		public DiffType CompareDouble(ReadOnlySpan<double> output, ReadOnlySpan<double> input, int count) => BASIC_COMPARE_TOLERANCE<double>(output, input, count, static (usetolerance, tolerance, in op, in ip) => {
			if (usetolerance && Math.Abs(op - ip) <= tolerance)
				return DiffType.WithinTolerance;
			return DiffType.Differs;
		});
		public DiffType CompareString(ReadOnlySpan<char> output, ReadOnlySpan<char> input) {
			if (!ErrorCheck) return DiffType.Differs;

			int i = 0;
			if (CanCheck())
				while (true) {
					char oc = output[i];
					char ic = input[i];
					if (oc == '\0' || ic == '\0')
						return oc == ic ? DiffType.Identical : DiffType.Differs;
					if (oc != ic)
						return DiffType.Differs;
					i++;
				}

			return DiffType.Identical;
		}
		public DiffType CompareVector(ReadOnlySpan<Vector3> output, ReadOnlySpan<Vector3> input) => CompareVector(output, input, 1);
		public DiffType CompareVector(ReadOnlySpan<Vector3> output, ReadOnlySpan<Vector3> input, int count) => BASIC_COMPARE_TOLERANCE(output, input, count, static (usetolerance, tolerance, in op, in ip) => {
		    var delta = op - ip;
			if (usetolerance && MathF.Abs(delta.X) <= tolerance && MathF.Abs(delta.Y) <= tolerance && MathF.Abs(delta.Z) <= tolerance)
				return DiffType.WithinTolerance;
			return DiffType.Differs;
		});
		public DiffType CompareQuaternion(ReadOnlySpan<Quaternion> output, ReadOnlySpan<Quaternion> input) => CompareQuaternion(output, input, 1);
		public DiffType CompareQuaternion(ReadOnlySpan<Quaternion> output, ReadOnlySpan<Quaternion> input, int count) => BASIC_COMPARE_TOLERANCE(output, input, count, static (usetolerance, tolerance, in op, in ip) => {
			var delta = op - ip;
			if (usetolerance && MathF.Abs(delta.X) <= tolerance && MathF.Abs(delta.Y) <= tolerance && MathF.Abs(delta.Z) <= tolerance && MathF.Abs(delta.W) <= tolerance)
				return DiffType.WithinTolerance;
			return DiffType.Differs;
		});
		public DiffType CompareColor(ReadOnlySpan<Color> output, ReadOnlySpan<Color> input, int count) => BASIC_COMPARE(output, input, count);
		#endregion
		#region copy methods
		public void BASIC_COPY<T>(DiffType dt, Span<T> output, ReadOnlySpan<T> input, int count) where T : unmanaged {
			if (!PerformCopy) return;
			if (dt == DiffType.Identical) return;
			memcpy(output[..count], input[..count]);
		}
		#endregion
		#region common copy methods
		public void CopyChar(DiffType dt, Span<char> output, ReadOnlySpan<char> input, int count) => BASIC_COPY(dt, output, input, count);
		public void CopyShort(DiffType dt, Span<short> output, ReadOnlySpan<short> input, int count) => BASIC_COPY(dt, output, input, count);
		public void CopyInt(DiffType dt, Span<int> output, ReadOnlySpan<int> input, int count) => BASIC_COPY(dt, output, input, count);
		public void CopyByte(DiffType dt, Span<byte> output, ReadOnlySpan<byte> input, int count) => BASIC_COPY(dt, output, input, count);
		public void CopyBool(DiffType dt, Span<bool> output, ReadOnlySpan<bool> input, int count) => BASIC_COPY(dt, output, input, count);
		public void CopyFloat(DiffType dt, Span<float> output, ReadOnlySpan<float> input, int count) => BASIC_COPY(dt, output, input, count);
		public void CopyDouble(DiffType dt, Span<double> output, ReadOnlySpan<double> input, int count) => BASIC_COPY(dt, output, input, count);
		public void CopyString(DiffType dt, Span<char> output, ReadOnlySpan<char> input) {
			if (!PerformCopy) return;
			if (dt == DiffType.Identical) return;

			int i = 0;
			while (true) {
				output[i] = input[i];
				if (input[i] == '\0')
					break;

				i++;
			}
		}

		public void CopyVector(DiffType dt, Span<Vector3> output, ReadOnlySpan<Vector3> input) => CopyVector(dt, output, input, 1);
		public void CopyVector(DiffType dt, Span<Vector3> output, ReadOnlySpan<Vector3> input, int count) => BASIC_COPY(dt, output, input, count);

		public void CopyQuaternion(DiffType dt, Span<Quaternion> output, ReadOnlySpan<Quaternion> input) => CopyQuaternion(dt, output, input, 1);
		public void CopyQuaternion(DiffType dt, Span<Quaternion> output, ReadOnlySpan<Quaternion> input, int count) => BASIC_COPY(dt, output, input, count);

		public void CopyColor(DiffType dt, Span<Color> output, ReadOnlySpan<Color> input, int count) => BASIC_COPY(dt, output, input, count);
		#endregion
		#region describe/watch
		public void DescribeData(DiffType dt, int size, Span<byte> outdata, ReadOnlySpan<byte> indata) {
			if (!ErrorCheck) return;
			if (dt == DiffType.Differs) ReportFieldsDiffer($"binary data differs ({size} bytes)\n");
			DescribeFields(dt, $"binary ({size} bytes)\n");
		}

		public void WatchData(DiffType dt, int size, Span<byte> outdata, ReadOnlySpan<byte> indata) {
			if (WatchField != CurrentField)
				return;
			WatchMsg($"binary ({size} bytes)");
		}

		public void DescribeShort(DiffType dt, Span<short> outdata, ReadOnlySpan<short> indata, int size) {
			if (!ErrorCheck) return;
			int invalue = indata[0], outvalue = outdata[0];
			if (dt == DiffType.Differs) ReportFieldsDiffer($"short differs (net {invalue} pred {outvalue}) diff({outvalue - invalue})\n");
			DescribeFields(dt, $"short ({outvalue})\n");
		}

		public void WatchShort(DiffType dt, Span<short> outdata, ReadOnlySpan<short> indata, int size) {
			if (WatchField != CurrentField)
				return;
			WatchMsg($"short ({outdata[0]})");
		}

		// TODO: Model indices for the int functions.
		public void DescribeInt(DiffType dt, Span<int> outdata, ReadOnlySpan<int> indata, int size) {
			if (!ErrorCheck) return;
			int invalue = indata[0], outvalue = outdata[0];
			if (dt == DiffType.Differs) ReportFieldsDiffer($"int differs (net {invalue} pred {outvalue}) diff({outvalue - invalue})\n");
			DescribeFields(dt, $"int ({outvalue})\n");
		}

		public void WatchInt(DiffType dt, Span<int> outdata, ReadOnlySpan<int> indata, int size) {
			if (WatchField != CurrentField)
				return;
			WatchMsg($"int ({outdata[0]})");
		}

		public void DescribeChar(DiffType dt, Span<char> outdata, ReadOnlySpan<char> indata, int size) {
			if (!ErrorCheck) return;
			int invalue = indata[0], outvalue = outdata[0];
			if (dt == DiffType.Differs) ReportFieldsDiffer($"char differs (net {invalue} pred {outvalue}) diff({outvalue - invalue})\n");
			DescribeFields(dt, $"char ({outvalue})\n");
		}

		public void WatchChar(DiffType dt, Span<int> outdata, ReadOnlySpan<int> indata, int size) {
			if (WatchField != CurrentField)
				return;
			WatchMsg($"int ({outdata[0]})");
		}

		public void DescribeByte(DiffType dt, Span<byte> outdata, ReadOnlySpan<byte> indata, int size) {
			if (!ErrorCheck) return;
			int invalue = indata[0], outvalue = outdata[0];
			if (dt == DiffType.Differs) ReportFieldsDiffer($"byte differs (net {invalue} pred {outvalue}) diff({outvalue - invalue})\n");
			DescribeFields(dt, $"byte ({outvalue})\n");
		}

		public void WatchByte(DiffType dt, Span<byte> outdata, ReadOnlySpan<byte> indata, int size) {
			if (WatchField != CurrentField)
				return;
			WatchMsg($"int ({outdata[0]})");
		}

		public void DescribeBool(DiffType dt, Span<bool> outdata, ReadOnlySpan<bool> indata, int size) {
			if (!ErrorCheck) return;
			string invalue = indata[0] ? "true" : "false", outvalue = outdata[0] ? "true" : "false";
			if (dt == DiffType.Differs) ReportFieldsDiffer($"bool differs (net {invalue} pred {outvalue})\n");
			DescribeFields(dt, $"bool ({outvalue})\n");
		}

		public void WatchBool(DiffType dt, Span<bool> outdata, ReadOnlySpan<bool> indata, int size) {
			if (WatchField != CurrentField)
				return;
			WatchMsg($"bool ({(outdata[0] ? "true" : "false")})");
		}

		public void DescribeFloat(DiffType dt, Span<float> outdata, ReadOnlySpan<float> indata, int size) {
			if (!ErrorCheck) return;
			float invalue = indata[0], outvalue = outdata[0];
			if (dt == DiffType.Differs) ReportFieldsDiffer($"float differs (net {invalue} pred {outvalue}) diff({outvalue - invalue})\n");
			DescribeFields(dt, $"float ({outvalue})\n");
		}

		public void WatchFloat(DiffType dt, Span<float> outdata, ReadOnlySpan<float> indata, int size) {
			if (WatchField != CurrentField)
				return;
			WatchMsg($"float ({outdata[0]})");
		}

		public void DescribeDouble(DiffType dt, Span<double> outdata, ReadOnlySpan<double> indata, int size) {
			if (!ErrorCheck) return;
			double invalue = indata[0], outvalue = outdata[0];
			if (dt == DiffType.Differs) ReportFieldsDiffer($"double differs (net {invalue} pred {outvalue}) diff({outvalue - invalue})\n");
			DescribeFields(dt, $"double ({outvalue})\n");
		}

		public void WatchDouble(DiffType dt, Span<double> outdata, ReadOnlySpan<double> indata, int size) {
			if (WatchField != CurrentField)
				return;
			WatchMsg($"double ({outdata[0]})");
		}

		public void DescribeString(DiffType dt, Span<char> outstring, ReadOnlySpan<char> instring) {
			if (!ErrorCheck) return;

			if (dt == DiffType.Differs) ReportFieldsDiffer($"string differs (net {instring} pred {outstring})\n");

			DescribeFields(dt, $"string ({outstring})\n");
		}

		public void WatchString(DiffType dt, Span<char> outstring, ReadOnlySpan<char> instring) {
			if (WatchField != CurrentField)
				return;
			WatchMsg($"string ({outstring.SliceNullTerminatedString()})");
		}

		public void DescribeVector(DiffType dt, Span<Vector3> outdata, ReadOnlySpan<Vector3> indata, int size) {
			if (!ErrorCheck) return;
			Vector3 outValue = outdata[0];
			if (dt == DiffType.Differs) {
				int i = 0;
				Vector3 inValue = indata[0];
				Vector3 delta = outValue - inValue;

				ReportFieldsDiffer($"vec[] differs (1st diff) (net {inValue.X} {inValue.Y} {inValue.Z} - pred {outValue.X} {outValue.Y} {outValue.Z}) delta({delta.X} {delta.Y} {delta.Z})\n");
			}

			DescribeFields(dt, $"vector ({outValue.X} {outValue.Y} {outValue.Z})\n");
		}

		public void WatchVector(DiffType dt, Span<Vector3> outdata, ReadOnlySpan<Vector3> indata, int size) {
			if (WatchField != CurrentField)
				return;
			Vector3 outValue = outdata[0];
			WatchMsg($"vector ({outValue.X} {outValue.Y} {outValue.Z})");
		}

		public void DescribeQuaternion(DiffType dt, Span<Quaternion> outdata, ReadOnlySpan<Quaternion> indata, int size) {
			if (!ErrorCheck) return;
			Quaternion outValue = outdata[0];
			if (dt == DiffType.Differs) {
				int i = 0;
				Quaternion inValue = indata[0];
				Quaternion delta = outValue - inValue;

				ReportFieldsDiffer($"quaternion[] differs (1st diff) (net {inValue.X} {inValue.Y} {inValue.Z} {inValue.W} - pred {outValue.X} {outValue.Y} {outValue.Z} {outValue.Z}) delta({delta.X} {delta.Y} {delta.Z} {outValue.W})\n");
			}

			DescribeFields(dt, $"quaternion ({outValue.X} {outValue.Y} {outValue.Z} {outValue.W})\n");
		}

		public void WatchQuaternion(DiffType dt, Span<Quaternion> outdata, ReadOnlySpan<Quaternion> indata, int size) {
			if (WatchField != CurrentField)
				return;
			Quaternion outValue = outdata[0];
			WatchMsg($"quaternion ({outValue.X} {outValue.Y} {outValue.Z} {outValue.W})");
		}

		#endregion
	}

	/*
		Make sure that DEFINE is defined in the C# file as using DEFINE = Source.DEFINE<YOURCLASSHERE>;

			BEGIN_PREDICTION_DATA_NO_BASE:	public static readonly DataMap PredMap = new(nameof(THISCLASS), [
			BEGIN_PREDICTION_DATA:			public static readonly new DataMap PredMap = new(nameof(THISCLASS), BaseDataMap, [
			DEFINE_PRED_FIELD:					DEFINE.PRED_FIELD(nameof(FIELD), FieldType, FieldTypeDescFlags),
			DEFINE_FIELD:						DEFINE.FIELD(nameof(FIELD), FieldType),
			END_PREDICTION_DATA:			]);
	*/

	public class DataMap
	{
		public DataMap() { }

		public PredictionCopyFieldsFn? PredictionCopyFields;

		/// <summary>
		/// Old API, the other constructors are better and closer to the macros... fixme
		/// </summary>
		public DataMap(TypeDescription[]? dataDesc, Type dataClassType, DataMap? baseMap) {
			DataDesc = dataDesc ?? [];
			DataClassType = dataClassType;
			DataClassName = dataClassType.Name;
			BaseMap = baseMap;
		}

		public DataMap(Type dataClassType, TypeDescription[]? dataDesc) {
			DataDesc = dataDesc ?? [];
			DataClassType = dataClassType;
			DataClassName = dataClassType.Name;
		}
		public DataMap(Type dataClassType, DataMap? baseMap, TypeDescription[]? dataDesc) {
			DataDesc = dataDesc ?? [];
			DataClassType = dataClassType;
			DataClassName = dataClassType.Name;
			BaseMap = baseMap;
		}

		public static DataMap GetDataMap(Type type) {
			for (Type? t = type; t != null; t = t.BaseType) {
				FieldInfo? f = t.GetField("DataDesc", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
				if (f != null && f.FieldType == typeof(DataMap))
					return (DataMap?)f.GetValue(null) ?? throw new Exception($"datamap for {type.Name} is not initialized yet");
			}

			throw new Exception($"can't find datamap for {type.Name}");
		}

		public TypeDescription[] DataDesc = [];
		public int DataNumFields => DataDesc?.Length ?? 0;
		public readonly Type DataClassType;
		public readonly string DataClassName = "";
		public DataMap? BaseMap;
		public bool ChainsValidated;
		public bool PackedOffsetsComputed;
		public nuint PackedSize;
#if DEBUG
		public bool ValidityChecked = false;
#endif
	}
}

namespace Source
{
	public static class DEFINE<T>
	{
		static int SIZE_OF_ARRAY(ReadOnlySpan<char> fieldName) {
			var field = typeof(T).GetField(new(fieldName), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
			if (field == null)
				throw new NullReferenceException();

			InlineArrayAttribute? arrayAttr;
			if ((arrayAttr = field.FieldType.GetCustomAttribute<InlineArrayAttribute>()) != null)
				return arrayAttr.Length;

			throw new NotSupportedException("Need a way to resolve SIZE_OF_ARRAY in this case.");
		}

		public static FieldInfo GetField_R(Type t, string name){
			var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) ?? t.GetField("__nv_" + name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
			if (f != null)
				return f;

			var basetype = t.BaseType;
			if (basetype != null)
				return GetField_R(basetype, name);

			throw new Exception($"can't find field {name}");
		}

		static TypeDescription _FIELD(ReadOnlySpan<char> name, FieldType fieldType, int count, FieldTypeDescFlags flags, ReadOnlySpan<char> mapname, float tolerance) {
			return new(fieldType, name, GetField_R(typeof(T), new(name)), (ushort)count, flags, mapname, null, null, tolerance);
		}
		static TypeDescription _FIELD_ARRAY(ReadOnlySpan<char> name, FieldType fieldType, int count, FieldTypeDescFlags flags, ReadOnlySpan<char> mapname, float tolerance) {
			return new(fieldType, name, GetField_R(typeof(T), new(name)), (ushort)count, flags, mapname, null, null, tolerance);
		}

		public static TypeDescription FIELD(ReadOnlySpan<char> name, FieldType fieldType) {
			return _FIELD(name, fieldType, 1, FieldTypeDescFlags.Save, null, 0);
		}

		public static TypeDescription KEYFIELD(ReadOnlySpan<char> name, FieldType fieldType, ReadOnlySpan<char> mapname) {
			return _FIELD(name, fieldType, 1, FieldTypeDescFlags.Key | FieldTypeDescFlags.Save, mapname, 0);
		}

		public static TypeDescription KEYFIELD_NOT_SAVED(ReadOnlySpan<char> name, FieldType fieldType, ReadOnlySpan<char> mapname) {
			return _FIELD(name, fieldType, 1, FieldTypeDescFlags.Key, mapname, 0);
		}

		public static TypeDescription ARRAY(ReadOnlySpan<char> name, FieldType fieldType, int count) {
			return _FIELD_ARRAY(name, fieldType, count, FieldTypeDescFlags.Save, null, 0);
		}

		public static TypeDescription GLOBAL_FIELD(ReadOnlySpan<char> name, FieldType fieldType) {
			return _FIELD(name, fieldType, 1, FieldTypeDescFlags.Global | FieldTypeDescFlags.Save, null, 0);
		}

		public static TypeDescription GLOBAL_KEYFIELD(ReadOnlySpan<char> name, FieldType fieldType, ReadOnlySpan<char> mapname) {
			return _FIELD(name, fieldType, 1, FieldTypeDescFlags.Global | FieldTypeDescFlags.Key | FieldTypeDescFlags.Save, mapname, 0);
		}

		public static TypeDescription AUTO_ARRAY_KEYFIELD(ReadOnlySpan<char> name, FieldType fieldType, ReadOnlySpan<char> mapname) {
			return _FIELD_ARRAY(name, fieldType, SIZE_OF_ARRAY(name), FieldTypeDescFlags.Save, mapname, 0);
		}

		public static TypeDescription INPUT(ReadOnlySpan<char> name, FieldType fieldType, ReadOnlySpan<char> inputname) {
			return _FIELD(name, fieldType, 1, FieldTypeDescFlags.Input | FieldTypeDescFlags.Save | FieldTypeDescFlags.Key, inputname, 0);
		}

		public static TypeDescription INPUTFUNC(FieldType fieldType, ReadOnlySpan<char> inputname, ReadOnlySpan<char> inputfunc, Delegate func) {
			return new(fieldType, inputfunc, null!, 1, FieldTypeDescFlags.Input, inputname, null, null, 0, func);
		}

		public static TypeDescription OUTPUT(ReadOnlySpan<char> name, ReadOnlySpan<char> outputname, ISaveRestoreOps eventFuncs) {
			return new(FieldType.Custom, name, GetField_R(typeof(T), new(name)), 1, FieldTypeDescFlags.Output | FieldTypeDescFlags.Save | FieldTypeDescFlags.Key, outputname, eventFuncs, null, 0);
		}

		public static TypeDescription AUTO_ARRAY(ReadOnlySpan<char> name, FieldType fieldType) {
			return _FIELD_ARRAY(name, fieldType, SIZE_OF_ARRAY(name), FieldTypeDescFlags.Save, null, 0);
		}

		public static TypeDescription PRED_FIELD(ReadOnlySpan<char> name, FieldType fieldType, FieldTypeDescFlags flags) {
			return _FIELD(name, fieldType, 1, flags, null, 0);
		}
		public static TypeDescription PRED_ARRAY(ReadOnlySpan<char> name, FieldType fieldType, int count, FieldTypeDescFlags flags) {
			return _FIELD_ARRAY(name, fieldType, count, flags, null, 0);
		}

		public static TypeDescription PRED_FIELD_TOL(ReadOnlySpan<char> name, FieldType fieldType, FieldTypeDescFlags flags, float tolerance) {
			return _FIELD(name, fieldType, 1, flags, null, tolerance);
		}
		public static TypeDescription PRED_ARRAY_TOL(ReadOnlySpan<char> name, FieldType fieldType, int count, FieldTypeDescFlags flags, float tolerance) {
			return _FIELD_ARRAY(name, fieldType, count, flags, null, tolerance);
		}

		public static TypeDescription PRED_TYPEDESCRIPTION(ReadOnlySpan<char> name, DataMap baseMap) {
			return new(FieldType.Embedded, name, GetField_R(typeof(T), new(name)), (ushort)1, FieldTypeDescFlags.Save, null, null, baseMap, 0f);
		}

		public static TypeDescription EMBEDDED(ReadOnlySpan<char> name) {
			FieldInfo field = GetField_R(typeof(T), new(name));
			return new(FieldType.Embedded, name, field, 1, FieldTypeDescFlags.Save, null, null, DataMap.GetDataMap(field.FieldType), 0);
		}

		public static TypeDescription UTLVECTOR(ReadOnlySpan<char> name, FieldType fieldType) {
			FieldInfo field = GetField_R(typeof(T), new(name));
			return new(FieldType.Custom, name, field, 1, FieldTypeDescFlags.Save, null, UtlVectorDataOps.GetDataOps(field.FieldType, fieldType), null, 0);
		}

		static MethodInfo GetMethod_R(Type t, string name, int paramCount) {
			for (Type? type = t; type != null; type = type.BaseType) {
				MethodInfo? found = null;
				foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
					if (method.Name != name)
						continue;

					int count = method.GetParameters().Length;
					if (paramCount >= 0 && count != paramCount)
						continue;

					if (found == null || count < found.GetParameters().Length)
						found = method;
				}

				if (found != null)
					return found;
			}

			throw new Exception($"can't find function {name}");
		}

		public static TypeDescription FUNCTION_RAW(ReadOnlySpan<char> function, int paramCount) {
			MethodInfo method = GetMethod_R(typeof(T), new(function), paramCount);
			Type[] signature = [typeof(T), .. method.GetParameters().Select(p => p.ParameterType), method.ReturnType];
			Delegate func = method.CreateDelegate(System.Linq.Expressions.Expression.GetDelegateType(signature));
			return new(FieldType.Void, function, null!, 1, FieldTypeDescFlags.FunctionTable, null, null, null, 0, func);
		}

		public static TypeDescription FUNCTION(ReadOnlySpan<char> function) => FUNCTION_RAW(function, -1);
		public static TypeDescription THINKFUNC(ReadOnlySpan<char> function) => FUNCTION_RAW(function, 0);
		public static TypeDescription ENTITYFUNC(ReadOnlySpan<char> function) => FUNCTION_RAW(function, 1);
		public static TypeDescription USEFUNC(ReadOnlySpan<char> function) => FUNCTION_RAW(function, 4);
	}

	public static unsafe class DataFrameExts {
		extension(Span<byte> data) {
			public T GetValueType<T>(nint offset, nint array_index = 0) where T : unmanaged => data[(int)(offset + (sizeof(T) * array_index))..][..sizeof(T)].Cast<byte, T>()[0];
			public ref T GetValueTypeRef<T>(nint offset, nint array_index = 0) where T : unmanaged => ref data[(int)(offset + (sizeof(T) * array_index))..][..sizeof(T)].Cast<byte, T>()[0];
			public void SetValueType<T>(nint offset, in T value, nint array_index = 0) where T : unmanaged => data[(int)(offset + (sizeof(T) * array_index))..][..sizeof(T)].Cast<byte, T>()[0] = value;

			public T? GetRefType<T>(nint offset, nint array_index = 0) where T : class {
				ref GCHandle gc = ref data[(int)(offset + (sizeof(GCHandle) * array_index))..][..sizeof(GCHandle)].Cast<byte, GCHandle>()[0];
				if (!gc.IsAllocated)
					return null;
				return (T?)gc.Target;
			}

			public void SetRefType<T>(nint offset, T? value, nint array_index = 0) where T : class {
				ref GCHandle gc = ref data[(int)(offset + (sizeof(GCHandle) * array_index))..][..sizeof(GCHandle)].Cast<byte, GCHandle>()[0];
				if (gc.IsAllocated)
					gc.Free();
				if (value == null)
					gc = default;
				else
					gc = GCHandle.Alloc(value, GCHandleType.Weak);
			}
		}
	}
}
