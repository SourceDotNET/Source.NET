#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;

using static Source.Common.Mathematics.MathLib;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaAngle
{
	[LuaClass(typeof(QAngle))]
	public static readonly LuaClass LC_Angle = new("Angle", LuaType.Angle, null, null);

	public static ref QAngle Get_Angle(int stackPos) => ref LC_Angle.GetValue<QAngle>(stackPos);

	public static void Push_Angle(in QAngle ang) => g_Lua!.PushAngle(in ang);

	static char FirstChar(ReadOnlySpan<char> str) => str.IsEmpty ? '\0' : str[0];

	[LuaMethod]
	static int Angle____newindex(ILuaInterface lua) {
		ref QAngle ang = ref Get_Angle(1);
		int index;
		double value;
		if (g_Lua!.GetType(2) == LuaType.Number) {
			index = (int)g_Lua.CheckNumber(2) - 1;
			value = g_Lua.CheckNumber(3);
			if (index < 0 || index > 2)
				return 0;
		}
		else {
			switch (FirstChar(g_Lua.CheckString(2))) {
				case 'P': case 'X': case 'p': case 'x': index = 0; break;
				case 'Y': case 'y': index = 1; break;
				case 'R': case 'Z': case 'r': case 'z': index = 2; break;
				default:
					g_Lua.CheckNumber(3);
					return 0;
			}
			value = g_Lua.CheckNumber(3);
		}

		ang[index] = (float)value;
		return 0;
	}

	[LuaMethod]
	static int Angle____index(ILuaInterface lua) {
		if (g_Lua!.FindOnObjectsMetaTable(1, 2))
			return 1;

		ref QAngle ang = ref Get_Angle(1);
		int index;
		if (g_Lua.GetType(2) == LuaType.Number) {
			index = (int)g_Lua.CheckNumber(2) - 1;
			if (index < 0 || index > 2)
				return 0;
		}
		else {
			switch (FirstChar(g_Lua.CheckString(2))) {
				case 'P': case 'X': case 'p': case 'x': index = 0; break;
				case 'Y': case 'y': index = 1; break;
				case 'R': case 'Z': case 'r': case 'z': index = 2; break;
				default: return 0;
			}
		}

		g_Lua.PushNumber(ang[index]);
		return 1;
	}

	[LuaMethod]
	static string Angle____tostring(ref QAngle ang) {
		string str = $"{FormatFixed(ang.X, 3)} {FormatFixed(ang.Y, 3)} {FormatFixed(ang.Z, 3)}";
		return str.Length > 63 ? str[..63] : str;
	}

	[LuaMethod]
	static QAngle Angle____add(ref QAngle a, ref QAngle b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

	[LuaMethod]
	static QAngle Angle____sub(ref QAngle a, ref QAngle b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

	[LuaMethod]
	static QAngle Angle____unm(ref QAngle ang) => new(-ang.X, -ang.Y, -ang.Z);

	[LuaMethod]
	static int Angle____mul(ILuaInterface lua) {
		bool numberFirst = g_Lua!.GetType(1) == LuaType.Number;
		ref QAngle ang = ref Get_Angle(numberFirst ? 2 : 1);
		float scale = (float)g_Lua.CheckNumber(numberFirst ? 1 : 2);
		Push_Angle(new QAngle(ang.X * scale, ang.Y * scale, ang.Z * scale));
		return 1;
	}

	[LuaMethod]
	static int Angle____div(ILuaInterface lua) {
		bool numberFirst = g_Lua!.GetType(1) == LuaType.Number;
		ref QAngle ang = ref Get_Angle(numberFirst ? 2 : 1);
		float oofl = 1.0f / (float)g_Lua.CheckNumber(numberFirst ? 1 : 2);
		Push_Angle(new QAngle(ang.X * oofl, ang.Y * oofl, ang.Z * oofl));
		return 1;
	}

	[LuaMethod]
	static void Angle__Add(ref QAngle a, ref QAngle b) {
		a.X += b.X;
		a.Y += b.Y;
		a.Z += b.Z;
	}

	[LuaMethod]
	static void Angle__Sub(ref QAngle a, ref QAngle b) {
		a.X -= b.X;
		a.Y -= b.Y;
		a.Z -= b.Z;
	}

	[LuaMethod]
	static void Angle__Mul(ref QAngle ang, float scale) {
		ang.X *= scale;
		ang.Y *= scale;
		ang.Z *= scale;
	}

	[LuaMethod]
	static void Angle__Div(ref QAngle ang, float divisor) {
		float oofl = 1.0f / divisor;
		ang.X *= oofl;
		ang.Y *= oofl;
		ang.Z *= oofl;
	}

	[LuaMethod]
	static bool Angle__IsEqualTol(ref QAngle a, ref QAngle b, float tolerance) => !(MathF.Abs(a.X - b.X) > tolerance) && !(MathF.Abs(a.Y - b.Y) > tolerance) && tolerance >= MathF.Abs(a.Z - b.Z);

	[LuaMethod]
	static Vector3 Angle__Forward(in QAngle ang) {
		AngleVectors(ang, out Vector3 forward);
		return forward;
	}

	[LuaMethod]
	static Vector3 Angle__Right(in QAngle ang) {
		AngleVectors(ang, out _, out Vector3 right, out _);
		return right;
	}

	[LuaMethod]
	static Vector3 Angle__Up(in QAngle ang) {
		AngleVectors(ang, out _, out _, out Vector3 up);
		return up;
	}

	[LuaMethod]
	static void Angle__RotateAroundAxis(ref QAngle ang, ref Vector3 axis, float degrees) {
		AngleMatrix(ang, out Matrix3x4 matrix);
		VectorIRotate(axis, matrix, out Vector3 localAxis);
		AxisAngleQuaternion(localAxis, degrees, out Quaternion q);
		QuaternionMatrix(q, vec3_origin, out Matrix3x4 rotation);
		ConcatTransforms(matrix, rotation, out Matrix3x4 result);
		MatrixAngles(result, out QAngle rotated);
		ang = rotated;
	}

	[LuaMethod]
	static bool Angle____eq(ref QAngle a, ref QAngle b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

	[LuaMethod]
	static void Angle__Normalize(ref QAngle ang) {
		ang.X = AngleNormalize(ang.X);
		ang.Y = AngleNormalize(ang.Y);
		ang.Z = AngleNormalize(ang.Z);
	}

	[LuaMethod]
	static void Angle__Set(ref QAngle ang, QAngle other) => ang = other;

	[LuaMethod]
	static void Angle__Zero(ref QAngle ang) => ang = default;

	[LuaMethod]
	static bool Angle__IsZero(ref QAngle ang) => ang.X == 0 && ang.Y == 0 && ang.Z == 0;

	[LuaMethod]
	static (float, float, float) Angle__Unpack(ref QAngle ang) => (ang.X, ang.Y, ang.Z);

	[LuaMethod]
	static void Angle__Random(ref QAngle ang, [LuaOpt<float>(-360.0f)] float minVal, [LuaOpt<float>(360.0f)] float maxVal) {
		float range = maxVal - minVal;
		ang.X = Random.Shared.NextSingle() * range + minVal;
		ang.Y = Random.Shared.NextSingle() * range + minVal;
		ang.Z = Random.Shared.NextSingle() * range + minVal;
	}

	[LuaMethod]
	static int Angle__SetUnpacked(ILuaInterface lua) {
		ref QAngle ang = ref Get_Angle(1);
		ang.X = (float)g_Lua!.CheckNumber(2);
		ang.Y = (float)g_Lua.CheckNumber(3);
		ang.Z = (float)g_Lua.CheckNumber(4);
		return 0;
	}

	[LuaMethod]
	static int Angle__ToTable(ILuaInterface lua) {
		ref QAngle ang = ref Get_Angle(1);
		LuaTable table = new(null, 3);
		table.SetMemberDouble(1, ang.X);
		table.SetMemberDouble(2, ang.Y);
		table.SetMemberDouble(3, ang.Z);
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaGlobal]
	static int Angle(ILuaInterface lua) {
		int top = lua.Top();
		Push_Angle(default);
		ref QAngle ang = ref Get_Angle(-1);

		LuaType type = lua.GetType(1);
		if (type == LuaType.String) {
			Span<float> values = stackalloc float[3];
			if (ScanFloats(g_Lua!.CheckString(1), values) == 3) {
				ang = new(values[0], values[1], values[2]);
				return 1;
			}
		}
		else if (type == LuaType.Angle && top > 0) {
			ang = Get_Angle(1);
			return 1;
		}
		else if (type != LuaType.Number
			&& g_Lua!.GetType(2) != LuaType.Number && g_Lua.GetType(2) != LuaType.String
			&& g_Lua.GetType(3) != LuaType.Number && g_Lua.GetType(3) != LuaType.String) {
			ang = default;
			return 1;
		}

		double z = lua.GetNumber(3);
		double y = lua.GetNumber(2);
		double x = lua.GetNumber(1);
		ang = new((float)x, (float)y, (float)z);
		return 1;
	}

	[LuaGlobal]
	static QAngle LerpAngle(float frac, ref QAngle from, ref QAngle to) {
		if (to.X == from.X && to.Y == from.Y && to.Z == from.Z)
			return from;

		AngleQuaternion(from, out Quaternion src);
		AngleQuaternion(to, out Quaternion dest);
		QuaternionBlend(src, dest, frac, out Quaternion result);
		QuaternionAngles(result, out QAngle angles);
		return angles;
	}
}
#endif
