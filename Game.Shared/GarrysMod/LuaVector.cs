#if CLIENT_DLL || GAME_DLL
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;

using static Source.Common.Mathematics.MathLib;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaVector
{
	[LuaClass(typeof(Vector3))]
	public static readonly LuaClass LC_Vector = new("Vector", LuaType.Vector, null, null);

	public static ref Vector3 Get_Vector(int stackPos) => ref LC_Vector.GetValue<Vector3>(stackPos);

	public static void Push_Vector(in Vector3 vec) => g_Lua!.PushVector(in vec);

	static char FirstChar(int stackPos) {
		g_Lua!.CheckType(stackPos, LuaType.String);
		ReadOnlySpan<byte> str = g_Lua.GetStringBytes(stackPos);
		return str.IsEmpty ? '\0' : (char)str[0];
	}

	[LuaMethod]
	static int Vector____index(ILuaInterface lua) {
		if (g_Lua!.FindOnObjectsMetaTable(1, 2))
			return 1;

		ref Vector3 vec = ref Get_Vector(1);
		int index;
		if (g_Lua.GetType(2) == LuaType.Number) {
			index = (int)g_Lua.CheckNumber(2) - 1;
			if (index < 0 || index > 2)
				return 0;
		}
		else {
			switch (FirstChar(2)) {
				case 'X': case 'r': case 'x': index = 0; break;
				case 'Y': case 'g': case 'y': index = 1; break;
				case 'Z': case 'b': case 'z': index = 2; break;
				default: return 0;
			}
		}

		g_Lua.PushNumber(vec[index]);
		return 1;
	}

	[LuaMethod]
	static int Vector____newindex(ILuaInterface lua) {
		ref Vector3 vec = ref Get_Vector(1);
		int index;
		double value;
		if (g_Lua!.GetType(2) == LuaType.Number) {
			index = (int)g_Lua.CheckNumber(2) - 1;
			value = g_Lua.CheckNumber(3);
			if (index < 0 || index > 2)
				return 0;
		}
		else {
			switch (FirstChar(2)) {
				case 'X': case 'r': case 'x': index = 0; break;
				case 'Y': case 'g': case 'y': index = 1; break;
				case 'Z': case 'b': case 'z': index = 2; break;
				default:
					g_Lua.CheckNumber(3);
					return 0;
			}
			value = g_Lua.CheckNumber(3);
		}

		vec[index] = (float)value;
		return 0;
	}

	[LuaMethod]
	static string Vector____tostring(ref Vector3 vec) {
		string str = $"{FormatFixed(vec.X, 6)} {FormatFixed(vec.Y, 6)} {FormatFixed(vec.Z, 6)}";
		return str.Length > 127 ? str[..127] : str;
	}

	[LuaMethod]
	static float Vector__Length(ref Vector3 vec) => MathF.Sqrt(vec.X * vec.X + vec.Y * vec.Y + vec.Z * vec.Z);

	[LuaMethod]
	static bool Vector____eq(ref Vector3 a, ref Vector3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

	[LuaMethod]
	static Vector3 Vector____add(ref Vector3 a, ref Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

	[LuaMethod]
	static void Vector__Add(ref Vector3 a, ref Vector3 b) {
		a.X += b.X;
		a.Y += b.Y;
		a.Z += b.Z;
	}

	[LuaMethod]
	static void Vector__Sub(ref Vector3 a, ref Vector3 b) {
		a.X -= b.X;
		a.Y -= b.Y;
		a.Z -= b.Z;
	}

	[LuaMethod]
	static int Vector__Mul(ILuaInterface lua) {
		ref Vector3 vec = ref Get_Vector(1);
		LuaType type = g_Lua!.GetType(2);
		if (type == LuaType.Vector) {
			ref Vector3 other = ref Get_Vector(2);
			vec.X *= other.X;
			vec.Y *= other.Y;
			vec.Z *= other.Z;
		}
		// todo: type == LuaType.Matrix: *vec = *Get_VMatrix(2) * *vec;
		else {
			float scale = (float)g_Lua.CheckNumber(2);
			vec.X *= scale;
			vec.Y *= scale;
			vec.Z *= scale;
		}
		return 0;
	}

	[LuaMethod]
	static int Vector__Div(ILuaInterface lua) {
		ref Vector3 vec = ref Get_Vector(1);
		if (g_Lua!.GetType(2) == LuaType.Vector) {
			ref Vector3 other = ref Get_Vector(2);
			vec.X /= other.X;
			vec.Y /= other.Y;
			vec.Z /= other.Z;
		}
		else {
			float oofl = 1.0f / (float)g_Lua.CheckNumber(2);
			vec.X *= oofl;
			vec.Y *= oofl;
			vec.Z *= oofl;
		}
		return 0;
	}

	[LuaMethod]
	static Vector3 Vector____sub(ref Vector3 a, ref Vector3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

	[LuaMethod]
	static Vector3 Vector____unm(ref Vector3 vec) => new(-vec.X, -vec.Y, -vec.Z);

	[LuaMethod]
	static int Vector____mul(ILuaInterface lua) {
		LuaType type = g_Lua!.GetType(1);
		if (type == LuaType.Vector && g_Lua.GetType(2) == LuaType.Vector) {
			ref Vector3 a = ref Get_Vector(1);
			ref Vector3 b = ref Get_Vector(2);
			Push_Vector(new Vector3(a.X * b.X, a.Y * b.Y, a.Z * b.Z));
			return 1;
		}

		bool numberFirst = type == LuaType.Number;
		ref Vector3 vec = ref Get_Vector(numberFirst ? 2 : 1);
		float scale = (float)g_Lua.CheckNumber(numberFirst ? 1 : 2);
		Push_Vector(new Vector3(vec.X * scale, vec.Y * scale, vec.Z * scale));
		return 1;
	}

	[LuaMethod]
	static int Vector____div(ILuaInterface lua) {
		LuaType type = g_Lua!.GetType(1);
		if (type == LuaType.Vector && g_Lua.GetType(2) == LuaType.Vector) {
			ref Vector3 a = ref Get_Vector(1);
			ref Vector3 b = ref Get_Vector(2);
			Push_Vector(new Vector3(a.X / b.X, a.Y / b.Y, a.Z / b.Z));
			return 1;
		}

		bool numberFirst = type == LuaType.Number;
		ref Vector3 vec = ref Get_Vector(numberFirst ? 2 : 1);
		float oofl = 1.0f / (float)g_Lua.CheckNumber(numberFirst ? 1 : 2);
		Push_Vector(new Vector3(vec.X * oofl, vec.Y * oofl, vec.Z * oofl));
		return 1;
	}

	[LuaMethod]
	static void Vector__Normalize(ref Vector3 vec) => VectorNormalize(ref vec);

	[LuaMethod]
	[LuaMethod("GetNormalized")]
	static Vector3 Vector__GetNormal(Vector3 vec) {
		VectorNormalize(ref vec);
		return vec;
	}

	[LuaMethod]
	[LuaMethod("DotProduct")]
	static float Vector__Dot(ref Vector3 a, ref Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

	[LuaMethod]
	static Vector3 Vector__Cross(Vector3 a, Vector3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

	[LuaMethod]
	static float Vector__Distance(ref Vector3 a, ref Vector3 b) {
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		float dz = a.Z - b.Z;
		return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
	}

	[LuaMethod]
	static QAngle Vector__Angle(Vector3 forward) {
		VectorNormalize(ref forward);
		VectorAngles(forward, out QAngle angles);
		return angles;
	}

	[LuaMethod]
	static QAngle Vector__AngleEx(Vector3 forward, Vector3 up) {
		VectorNormalize(ref forward);
		VectorNormalize(ref up);
		VectorAngles(forward, up, out QAngle angles);
		return angles;
	}

	[LuaMethod]
	static void Vector__Rotate(ref Vector3 vec, in QAngle angle) {
		AngleMatrix(angle, out Matrix3x4 matrix);
		VectorRotate(vec, matrix, out Vector3 rotated);
		vec = rotated;
	}

	[LuaMethod]
	static float Vector__Length2D(ref Vector3 vec) => MathF.Sqrt(vec.X * vec.X + vec.Y * vec.Y);

	[LuaMethod]
	static float Vector__LengthSqr(ref Vector3 vec) => vec.X * vec.X + vec.Y * vec.Y + vec.Z * vec.Z;

	[LuaMethod]
	static float Vector__Length2DSqr(ref Vector3 vec) => vec.X * vec.X + vec.Y * vec.Y;

	[LuaMethod]
	static float Vector__Distance2D(ref Vector3 a, ref Vector3 b) {
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return MathF.Sqrt(dx * dx + dy * dy);
	}

	[LuaMethod]
	static float Vector__Distance2DSqr(ref Vector3 a, ref Vector3 b) {
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		return dx * dx + dy * dy;
	}

	[LuaMethod]
	static float Vector__DistToSqr(ref Vector3 a, ref Vector3 b) {
		float dx = a.X - b.X;
		float dy = a.Y - b.Y;
		float dz = a.Z - b.Z;
		return dx * dx + dy * dy + dz * dz;
	}

	static void OrderVectors(in Vector3 a, in Vector3 b, out Vector3 mins, out Vector3 maxs) {
		mins = default;
		maxs = default;
		for (int i = 0; i < 3; i++) {
			mins[i] = a[i] < b[i] ? a[i] : b[i];
			maxs[i] = a[i] > b[i] ? a[i] : b[i];
		}
	}

	[LuaMethod]
	static bool Vector__WithinAABox(ref Vector3 vec, ref Vector3 boxStart, ref Vector3 boxEnd) {
		OrderVectors(boxStart, boxEnd, out Vector3 mins, out Vector3 maxs);
		return vec.X >= mins.X && maxs.X >= vec.X && vec.Y >= mins.Y && maxs.Y >= vec.Y && vec.Z >= mins.Z && maxs.Z >= vec.Z;
	}

	[LuaMethod]
	static bool Vector__IsZero(ref Vector3 vec) {
		const float tolerance = 0.01f;
		return vec.X > -tolerance && vec.X < tolerance && vec.Y > -tolerance && vec.Y < tolerance && vec.Z > -tolerance && vec.Z < tolerance;
	}

	[LuaMethod]
	static bool Vector__IsEqualTol(ref Vector3 a, ref Vector3 b, float tolerance) => !(MathF.Abs(a.X - b.X) > tolerance) && !(MathF.Abs(a.Y - b.Y) > tolerance) && tolerance >= MathF.Abs(a.Z - b.Z);

	[LuaMethod]
	static void Vector__Zero(ref Vector3 vec) => vec = default;

	[LuaMethod]
	static void Vector__Set(ref Vector3 vec, Vector3 other) => vec = other;

	[LuaMethod]
	static (float, float, float) Vector__Unpack(ref Vector3 vec) => (vec.X, vec.Y, vec.Z);

	[LuaMethod]
	static int Vector__SetUnpacked(ILuaInterface lua) {
		ref Vector3 vec = ref Get_Vector(1);
		vec.X = (float)g_Lua!.CheckNumber(2);
		vec.Y = (float)g_Lua.CheckNumber(3);
		vec.Z = (float)g_Lua.CheckNumber(4);
		return 0;
	}

	[LuaMethod]
	static int Vector__ToTable(ILuaInterface lua) {
		ref Vector3 vec = ref Get_Vector(1);
		LuaTable table = new(null, 3);
		table.SetMemberDouble(1, vec.X);
		table.SetMemberDouble(2, vec.Y);
		table.SetMemberDouble(3, vec.Z);
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaMethod]
	static void Vector__Random(ref Vector3 vec, [LuaOpt<float>(-1.0f)] float minVal, [LuaOpt<float>(1.0f)] float maxVal) {
		float range = maxVal - minVal;
		vec.X = Random.Shared.NextSingle() * range + minVal;
		vec.Y = Random.Shared.NextSingle() * range + minVal;
		vec.Z = Random.Shared.NextSingle() * range + minVal;
	}

	[LuaMethod]
	static void Vector__Negate(ref Vector3 vec) {
		vec.X = -vec.X;
		vec.Y = -vec.Y;
		vec.Z = -vec.Z;
	}

	[LuaMethod]
	static Vector3 Vector__GetNegated(ref Vector3 vec) => new(-vec.X, -vec.Y, -vec.Z);

#if CLIENT_DLL
	[LuaMethod]
	static int Vector__ToScreen(ILuaInterface lua) {
		ref Vector3 vec = ref Get_Vector(1);
		bool behind = ScreenTransform(vec, out Vector3 screen);
		LuaTable table = new(null, 0);
		table.SetMember("x", (screen.X + 1.0f) * 0.5f * ScreenWidth());
		table.SetMember("y", (0.5f - screen.Y * 0.5f) * ScreenHeight());
		table.SetMember("visible", !behind);
		table.Push();
		table.UnReference();
		return 1;
	}
#endif

	[LuaGlobal]
	static int Vector(ILuaInterface lua) {
		int top = lua.Top();
		Push_Vector(default);
		ref Vector3 vec = ref Get_Vector(-1);

		LuaType type = lua.GetType(1);
		if (type == LuaType.String) {
			Span<float> values = stackalloc float[3];
			if (ScanFloats(g_Lua!.CheckString(1), values) == 3) {
				vec = new(values[0], values[1], values[2]);
				return 1;
			}
		}
		else if (type == LuaType.Vector && top > 0) {
			vec = Get_Vector(1);
			return 1;
		}
		else if (type != LuaType.Number
			&& g_Lua!.GetType(2) != LuaType.Number && g_Lua.GetType(2) != LuaType.String
			&& g_Lua.GetType(3) != LuaType.Number && g_Lua.GetType(3) != LuaType.String) {
			vec = default;
			return 1;
		}

		double z = lua.GetNumber(3);
		double y = lua.GetNumber(2);
		double x = lua.GetNumber(1);
		vec = new((float)x, (float)y, (float)z);
		return 1;
	}

	[LuaGlobal]
	static void OrderVectors(ref Vector3 a, ref Vector3 b) {
		OrderVectors(a, b, out Vector3 mins, out Vector3 maxs);
		a = mins;
		b = maxs;
	}

	[LuaGlobal]
	static Vector3 LerpVector(float frac, ref Vector3 from, ref Vector3 to) => new(
		(to.X - from.X) * frac + from.X,
		(to.Y - from.Y) * frac + from.Y,
		(to.Z - from.Z) * frac + from.Z
	);
}
#endif
