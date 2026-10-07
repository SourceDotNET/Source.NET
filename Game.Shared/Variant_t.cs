using Source;
using Source.Common;

using FT = Source.Common.FieldType;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Shared;

public struct Variant_t
{
	public bool BoolVal;
	public string? StrVal;
	public int IntVal;
	public float FloatVal;
	public Vector3 VecVal;
	public Color RgbaVal;
#if CLIENT_DLL || GAME_DLL
	public Handle<BaseEntity> EntVal;
#endif
	public FieldType fieldType;

	public Variant_t() {
		fieldType = FT.Void;
	}
	public readonly bool Bool() { return (fieldType == FT.Boolean) ? BoolVal : false; }
	public readonly ReadOnlySpan<char> String() { return (fieldType == FT.String) ? StrVal : ToString(); }
	public readonly string? StringID() { return (fieldType == FT.String) ? StrVal : null; }
	public readonly int Int() { return (fieldType == FT.Integer) ? IntVal : 0; }
	public readonly float Float() { return (fieldType == FT.Float) ? FloatVal : 0; }
#if CLIENT_DLL || GAME_DLL
	public readonly Handle<BaseEntity> Entity() {
		if (fieldType == FT.EHandle)
			return EntVal;

		EHANDLE hNull = new();
		hNull.Set(null);
		return hNull;
	}
#endif

	public readonly Color Color32() { return RgbaVal; }
	public readonly void Vector3D(out Vector3 vec) {
		if ((fieldType == FT.Vector) || (fieldType == FT.PositionVector)) {
			vec.X = VecVal[0];
			vec.Y = VecVal[1];
			vec.Z = VecVal[2];
		}
		else
			vec = vec3_origin;
	}

	public readonly FieldType FieldType() => fieldType;

	public void SetBool(bool b) { BoolVal = b; fieldType = FT.Boolean; }
	public void SetString(ReadOnlySpan<char> str) { StrVal = new(str.SliceNullTerminatedString()); fieldType = FT.String; }
	public void SetInt(int val) { IntVal = val; fieldType = FT.Integer; }
	public void SetFloat(float val) { FloatVal = val; fieldType = FT.Float; }
#if CLIENT_DLL || GAME_DLL
	public void SetEntity(BaseEntity? val) {
		fieldType = FT.EHandle;
		EntVal = new();
		EntVal.Set(val);
	}
#endif
	public void SetVector3D(in Vector3 val) { VecVal[0] = val[0]; VecVal[1] = val[1]; VecVal[2] = val[2]; fieldType = FT.Vector; }
	public void SetPositionVector3D(in Vector3 val) { VecVal[0] = val[0]; VecVal[1] = val[1]; VecVal[2] = val[2]; fieldType = FT.PositionVector; }
	public void SetColor32(Color val) { RgbaVal = val; fieldType = FT.Color32; }
	public void SetColor32(byte r, byte g, byte b, byte a) { RgbaVal.R = r; RgbaVal.G = g; RgbaVal.B = b; RgbaVal.A = a; fieldType = FT.Color32; }

	public void Set<T>(FieldType ftype, ref T value) {
		fieldType = ftype;

		switch (ftype) {
			case FT.Boolean: BoolVal = Unsafe.As<T, bool>(ref value); break;
			case FT.Character: IntVal = Unsafe.As<T, sbyte>(ref value); break;
			case FT.Short: IntVal = Unsafe.As<T, short>(ref value); break;
			case FT.Integer: IntVal = Unsafe.As<T, int>(ref value); break;
			case FT.String: StrVal = Unsafe.As<T, string?>(ref value); break;
			case FT.Float: FloatVal = Unsafe.As<T, float>(ref value); break;
			case FT.Color32: RgbaVal = Unsafe.As<T, Color>(ref value); break;
			case FT.Vector:
			case FT.PositionVector:
				VecVal = Unsafe.As<T, Vector3>(ref value);
				break;
			case FT.Input: this = Unsafe.As<T, Variant_t>(ref value); break;
#if CLIENT_DLL || GAME_DLL
			case FT.EHandle: EntVal = Unsafe.As<T, Handle<BaseEntity>>(ref value); break;
#endif
			case FT.Void:
			default:
				IntVal = 0; fieldType = FT.Void;
				break;
		}
	}

	public void Set(FieldType ftype, IFieldAccessor field, object instance) {
		fieldType = ftype;

		switch (ftype) {
			case FT.Boolean: BoolVal = field.GetValue<bool>(instance); break;
			case FT.Character: IntVal = field.GetValue<sbyte>(instance); break;
			case FT.Short: IntVal = field.GetValue<short>(instance); break;
			case FT.Integer: IntVal = field.GetValue<int>(instance); break;
			case FT.String: StrVal = field.GetValue<string?>(instance); break;
			case FT.Float: FloatVal = field.GetValue<float>(instance); break;
			case FT.Color32: RgbaVal = field.GetValue<Color>(instance); break;

			case FT.Vector:
			case FT.PositionVector:
				VecVal = field.GetValue<Vector3>(instance);
				break;

#if CLIENT_DLL || GAME_DLL
			case FT.EHandle: EntVal = field.GetValue<Handle<BaseEntity>>(instance); break;
			case FT.ClassPtr: EntVal = new(); EntVal.Set(field.GetValue<BaseEntity?>(instance)); break;
#endif
			case FT.Void:
			default:
				IntVal = 0; fieldType = FT.Void;
				break;
		}
	}

	public readonly void SetOther(IFieldAccessor field, object instance) {
		switch (fieldType) {
			case FT.Boolean: field.SetValue(instance, BoolVal); break;
			case FT.Character: field.SetValue(instance, (sbyte)IntVal); break;
			case FT.Short: field.SetValue(instance, (short)IntVal); break;
			case FT.Integer: field.SetValue(instance, IntVal); break;
			case FT.String: field.SetValue(instance, StrVal); break;
			case FT.Float: field.SetValue(instance, FloatVal); break;
			case FT.Color32: field.SetValue(instance, RgbaVal); break;
			case FT.Vector:
			case FT.PositionVector:
				field.SetValue(instance, VecVal);
				break;
#if CLIENT_DLL || GAME_DLL
			case FT.EHandle: field.SetValue(instance, EntVal); break;
			case FT.ClassPtr: field.SetValue(instance, EntVal.Get<BaseEntity>()); break;
#endif
		}
	}

	public bool Convert(FieldType newType) {
		if (newType == fieldType)
			return true;

		if (newType == FT.Void) {
			IntVal = 0;
			fieldType = FT.Void;
			return true;
		}

		if (newType == FT.Input)
			return true;

		switch (fieldType) {
			case FT.Integer:
				switch (newType) {
					case FT.Float:
						SetFloat((float)IntVal);
						return true;

					case FT.Boolean:
						SetBool(IntVal != 0);
						return true;
				}
				break;

			case FT.Float:
				switch (newType) {
					case FT.Integer:
						SetInt((int)FloatVal);
						return true;

					case FT.Boolean:
						SetBool(FloatVal != 0);
						return true;
				}
				break;

			case FT.String:
				switch (newType) {
					case FT.Integer:
						if (StrVal != null)
							SetInt(atoi(StrVal));
						else
							SetInt(0);
						return true;

					case FT.Float:
						if (StrVal != null)
							SetFloat(strtof(StrVal, out _));
						else
							SetFloat(0);
						return true;

					case FT.Boolean:
						if (StrVal != null)
							SetBool(atoi(StrVal) != 0);
						else
							SetBool(false);
						return true;

					case FT.Vector:
						Vector3 tmpVec = vec3_origin;
						if (0 == new ScanF(StrVal, "[%f %f %f]").Read(out tmpVec.X).Read(out tmpVec.Y).Read(out tmpVec.Z).ReadArguments)
							new ScanF(StrVal, "%f %f %f").Read(out tmpVec.X).Read(out tmpVec.Y).Read(out tmpVec.Z);
						SetVector3D(tmpVec);
						return true;

					case FT.Color32:
						int red = 0;
						int green = 0;
						int blue = 0;
						int alpha = 255;

						new ScanF(StrVal, "%d %d %d %d").Read(out red).Read(out green).Read(out blue).Read(out alpha);
						SetColor32((byte)red, (byte)green, (byte)blue, (byte)alpha);
						return true;

#if GAME_DLL
					case FT.EHandle:
						BaseEntity? entity = null;
						if (StrVal != null)
							entity = gEntList.FindEntityByName(null, StrVal);
						SetEntity(entity);
						return true;
#endif
				}
				break;

#if GAME_DLL
			case FT.EHandle:
				switch (newType) {
					case FT.String:
						if (EntVal.Get<BaseEntity>() != null)
							SetString(EntVal.Get<BaseEntity>()!.GetEntityName());
						return true;
				}
				break;
#endif
		}

		return false;
	}

	static InlineArray512<char> Buf;

	public readonly new ReadOnlySpan<char> ToString() {
		Span<char> buf = Buf;

		switch (fieldType) {
			case FT.String:
				return StrVal;
			case FT.Boolean:
				if (!BoolVal)
					strcpy(buf, "false");
				else
					strcpy(buf, "true");
				return buf.SliceNullTerminatedString();
			case FT.Integer:
				return sprintf(buf, "%i").I(IntVal);
			case FT.Float:
				return sprintf(buf, "%g").G(FloatVal);
			case FT.Color32:
				return sprintf(buf, "%d %d %d %d").D(RgbaVal.R).D(RgbaVal.G).D(RgbaVal.B).D(RgbaVal.A);
			case FT.Vector:
				return sprintf(buf, "[%g %g %g]").G(VecVal[0]).G(VecVal[1]).G(VecVal[2]);
			case FT.Void:
				buf[0] = '\0';
				return buf.SliceNullTerminatedString();
#if GAME_DLL
			case FT.EHandle:
				strcpy(buf, Entity().Get<BaseEntity>() != null ? Entity().Get<BaseEntity>()!.GetEntityName() : "<<null entity>>");
				return buf.SliceNullTerminatedString();
#endif
		}

		return "No conversion to string";
	}

	public static readonly TypeDescription SaveBool = DEFINE<Variant_t>.FIELD(nameof(BoolVal), FT.Boolean);
	public static readonly TypeDescription SaveInt = DEFINE<Variant_t>.FIELD(nameof(IntVal), FT.Integer);
	public static readonly TypeDescription SaveFloat = DEFINE<Variant_t>.FIELD(nameof(FloatVal), FT.Float);
#if CLIENT_DLL || GAME_DLL
	public static readonly TypeDescription SaveEHandle = DEFINE<Variant_t>.FIELD(nameof(EntVal), FT.EHandle);
#endif
	public static readonly TypeDescription SaveString = DEFINE<Variant_t>.FIELD(nameof(StrVal), FT.String);
	public static readonly TypeDescription SaveColor = DEFINE<Variant_t>.FIELD(nameof(RgbaVal), FT.Color32);
	public static readonly TypeDescription SaveVector = DEFINE<Variant_t>.FIELD(nameof(VecVal), FT.Vector);
	public static readonly TypeDescription SavePositionVector = DEFINE<Variant_t>.FIELD(nameof(VecVal), FT.PositionVector);
}
