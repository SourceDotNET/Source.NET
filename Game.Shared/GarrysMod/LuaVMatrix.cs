#if CLIENT_DLL || GAME_DLL
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.InteropServices;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaVMatrix
{
	[LuaClass(typeof(Matrix4x4))]
	public static readonly LuaClass LC_VMatrix = new("VMatrix", LuaType.Matrix, null, null);

	public static ref Matrix4x4 Get_VMatrix(int stackPos) {
		ref Matrix4x4 matrix = ref LC_VMatrix.GetValue<Matrix4x4>(stackPos);
		nint ud = g_Lua!.GetUserdata(stackPos);
		if (ud == 0 || Marshal.ReadIntPtr(ud) == 0)
			g_Lua.Error("Tried to use a NULL VMatrix!");
		return ref matrix;
	}

	public static void Push_VMatrix(in Matrix4x4 matrix) => g_Lua!.PushValueUserType(matrix, LuaType.Matrix);

	static bool MatrixIsZero(in Matrix4x4 m) {
		return m.M11 == 0 && m.M12 == 0 && m.M13 == 0 && m.M14 == 0 &&
			m.M21 == 0 && m.M22 == 0 && m.M23 == 0 && m.M24 == 0 &&
			m.M31 == 0 && m.M32 == 0 && m.M33 == 0 && m.M34 == 0 &&
			m.M41 == 0 && m.M42 == 0 && m.M43 == 0 && m.M44 == 0;
	}

	[LuaMethod]
	static int VMatrix____eq(ILuaInterface lua) {
		ref Matrix4x4 a = ref Get_VMatrix(1);
		ref Matrix4x4 b = ref Get_VMatrix(2);
		lua.PushBool(a == b);
		return 1;
	}

	[LuaMethod]
	static int VMatrix____add(ILuaInterface lua) {
		ref Matrix4x4 a = ref Get_VMatrix(1);
		ref Matrix4x4 b = ref Get_VMatrix(2);
		Push_VMatrix(a + b);
		return 1;
	}

	[LuaMethod]
	static int VMatrix____sub(ILuaInterface lua) {
		ref Matrix4x4 a = ref Get_VMatrix(1);
		ref Matrix4x4 b = ref Get_VMatrix(2);
		Push_VMatrix(a - b);
		return 1;
	}

	[LuaMethod]
	static int VMatrix____unm(ILuaInterface lua) {
		ref Matrix4x4 a = ref Get_VMatrix(1);
		Push_VMatrix(-a);
		return 1;
	}

	[LuaMethod]
	static int VMatrix____mul(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		if (lua.GetType(2) == LuaType.Vector) {
			MathLib.Vector3DMultiplyPosition(in m, in LuaVector.Get_Vector(2), out Vector3 ret);
			LuaVector.Push_Vector(ret);
			return 1;
		}
		if (lua.GetType(2) != LuaType.Matrix) {
			lua.TypeError("Vector or Matrix", 2);
			return 0;
		}
		MathLib.MatrixMultiply(in m, in Get_VMatrix(2), out Matrix4x4 product);
		Push_VMatrix(product);
		return 1;
	}

	[LuaMethod]
	static int VMatrix____tostring(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		lua.PushString(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"[{m.M11:F5},\t{m.M12:F5},\t{m.M13:F5},\t{m.M14:F5}]\n[{m.M21:F5},\t{m.M22:F5},\t{m.M23:F5},\t{m.M24:F5}]\n[{m.M31:F5},\t{m.M32:F5},\t{m.M33:F5},\t{m.M34:F5}]\n[{m.M41:F5},\t{m.M42:F5},\t{m.M43:F5},\t{m.M44:F5}]"));
		return 1;
	}

	[LuaMethod]
	static int VMatrix__Add(ILuaInterface lua) {
		ref Matrix4x4 a = ref Get_VMatrix(1);
		ref Matrix4x4 b = ref Get_VMatrix(2);
		a += b;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__Sub(ILuaInterface lua) {
		ref Matrix4x4 a = ref Get_VMatrix(1);
		ref Matrix4x4 b = ref Get_VMatrix(2);
		a -= b;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__Mul(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		MathLib.MatrixMultiply(in m, in Get_VMatrix(2), out Matrix4x4 product);
		m = product;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__ToTable(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		Span<float> s = MemoryMarshal.CreateSpan(ref m.M11, 16);
		LuaTable table = new(null, 4);
		for (int r = 0; r < 4; r++) {
			LuaTable row = new(null, 4);
			row.SetMemberDouble(1, s[r * 4 + 0]);
			row.SetMemberDouble(2, s[r * 4 + 1]);
			row.SetMemberDouble(3, s[r * 4 + 2]);
			row.SetMemberDouble(4, s[r * 4 + 3]);
			table.SetMember(r + 1, row);
			row.UnReference();
		}
		table.Push();
		table.UnReference();
		return 1;
	}

	[LuaMethod]
	static int VMatrix__Unpack(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		Span<float> s = MemoryMarshal.CreateSpan(ref m.M11, 16);
		for (int i = 0; i < 16; i++)
			lua.PushNumber(s[i]);
		return 16;
	}

	[LuaMethod]
	static int VMatrix__SetUnpacked(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		Span<float> s = MemoryMarshal.CreateSpan(ref m.M11, 16);
		for (int i = 2; i < 18; i++)
			s[i - 2] = (float)lua.CheckNumber(i);
		return 0;
	}

	[LuaMethod]
	static int VMatrix__Set(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		m = Get_VMatrix(2);
		return 0;
	}

	[LuaMethod]
	static int VMatrix__Identity(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		MathLib.SetIdentityMatrix(out m);
		return 0;
	}

	[LuaMethod]
	static int VMatrix__IsIdentity(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		lua.PushBool(m.M11 == 1.0f && m.M12 == 0 && m.M13 == 0 && m.M14 == 0 &&
			m.M21 == 0 && m.M22 == 1.0f && m.M23 == 0 && m.M24 == 0 &&
			m.M31 == 0 && m.M32 == 0 && m.M33 == 1.0f && m.M34 == 0 &&
			m.M41 == 0 && m.M42 == 0 && m.M43 == 0 && m.M44 == 1.0f);
		return 1;
	}

	[LuaMethod]
	static int VMatrix__Zero(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		m = default;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__IsZero(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		lua.PushBool(MatrixIsZero(in m));
		return 1;
	}

	[LuaMethod]
	static int VMatrix__IsRotationMatrix(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		lua.PushBool(m.IsRotationMatrix());
		return 1;
	}

	[LuaMethod]
	static int VMatrix__Invert(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		lua.PushBool(MathLib.MatrixInverseGeneral(in m, out m));
		return 1;
	}

	[LuaMethod]
	static int VMatrix__GetInverse(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		if (!MathLib.MatrixInverseGeneral(in m, out Matrix4x4 inverse))
			return 0;
		Push_VMatrix(inverse);
		return 1;
	}

	[LuaMethod]
	static int VMatrix__InvertTR(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		MathLib.MatrixInverseTR(in m, out Matrix4x4 inverse);
		m = inverse;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__GetInverseTR(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		MathLib.MatrixInverseTR(in m, out Matrix4x4 inverse);
		Push_VMatrix(inverse);
		return 1;
	}

	[LuaMethod]
	static int VMatrix__SetForward(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		ref Vector3 v = ref LuaVector.Get_Vector(2);
		m[0, 0] = v.X;
		m[1, 0] = v.Y;
		m[2, 0] = v.Z;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__SetRight(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		ref Vector3 v = ref LuaVector.Get_Vector(2);
		m[0, 1] = -v.X;
		m[1, 1] = -v.Y;
		m[2, 1] = -v.Z;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__SetUp(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		ref Vector3 v = ref LuaVector.Get_Vector(2);
		m[0, 2] = v.X;
		m[1, 2] = v.Y;
		m[2, 2] = v.Z;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__GetForward(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		LuaVector.Push_Vector(new(m[0, 0], m[1, 0], m[2, 0]));
		return 1;
	}

	[LuaMethod]
	static int VMatrix__GetRight(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		LuaVector.Push_Vector(new(-m[0, 1], -m[1, 1], -m[2, 1]));
		return 1;
	}

	[LuaMethod]
	static int VMatrix__GetUp(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		LuaVector.Push_Vector(new(m[0, 2], m[1, 2], m[2, 2]));
		return 1;
	}

	[LuaMethod]
	static int VMatrix__GetField(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		int row = (int)lua.CheckNumber(2);
		int column = (int)lua.CheckNumber(3);
		if ((uint)(row - 1) >= 4 || (uint)(column - 1) >= 4)
			return 0;
		lua.PushNumber(m[row - 1, column - 1]);
		return 1;
	}

	[LuaMethod]
	static int VMatrix__SetField(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		int row = (int)lua.CheckNumber(2);
		int column = (int)lua.CheckNumber(3);
		double value = lua.CheckNumber(4);
		if ((uint)(row - 1) < 4 && (uint)(column - 1) < 4)
			m[row - 1, column - 1] = (float)value;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__Translate(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		MathLib.MatrixMultiply(in m, MathLib.SetupMatrixTranslation(in LuaVector.Get_Vector(2)), out Matrix4x4 product);
		m = product;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__Rotate(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		MathLib.MatrixMultiply(in m, MathLib.SetupMatrixAngles(in LuaAngle.Get_Angle(2)), out Matrix4x4 product);
		m = product;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__Scale(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		MathLib.MatrixMultiply(in m, MathLib.SetupMatrixScale(in LuaVector.Get_Vector(2)), out Matrix4x4 product);
		m = product;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__GetTranslation(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		LuaVector.Push_Vector(new(m[0, 3], m[1, 3], m[2, 3]));
		return 1;
	}

	[LuaMethod]
	static int VMatrix__GetAngles(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		Matrix4x4 normalized = m.NormalizeBasisVectors();
		MathLib.MatrixToAngles(in normalized, out QAngle angles);
		LuaAngle.Push_Angle(angles);
		return 1;
	}

	[LuaMethod]
	static int VMatrix__GetScale(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		LuaVector.Push_Vector(m.GetScale());
		return 1;
	}

	[LuaMethod]
	static int VMatrix__SetTranslation(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		ref Vector3 v = ref LuaVector.Get_Vector(2);
		m[0, 3] = v.X;
		m[1, 3] = v.Y;
		m[2, 3] = v.Z;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__SetAngles(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		QAngle angles = LuaAngle.Get_Angle(2);
		Vector3 scale = m.GetScale();
		Matrix4x4 rotation = default;
		rotation.SetupMatrixOrgAngles(vec3_origin, in angles);
		m[0, 0] = rotation[0, 0] * scale.X;
		m[0, 1] = rotation[0, 1] * scale.X;
		m[0, 2] = rotation[0, 2] * scale.X;
		m[1, 0] = rotation[1, 0] * scale.Y;
		m[1, 1] = rotation[1, 1] * scale.Y;
		m[1, 2] = rotation[1, 2] * scale.Y;
		m[2, 0] = rotation[2, 0] * scale.Z;
		m[2, 1] = rotation[2, 1] * scale.Z;
		m[2, 2] = rotation[2, 2] * scale.Z;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__SetScale(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		ref Vector3 scale = ref LuaVector.Get_Vector(2);
		m.GetBasisVectors(out Vector3 forward, out Vector3 left, out Vector3 up);
		MathLib.VectorNormalizeFast(ref forward);
		MathLib.VectorNormalizeFast(ref left);
		MathLib.VectorNormalizeFast(ref up);
		m.SetBasisVectors(forward * scale.X, left * scale.Y, up * scale.Z);
		return 0;
	}

	[LuaMethod]
	static int VMatrix__ScaleTranslation(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		float scale = (float)lua.CheckNumber(2);
		m[0, 3] *= scale;
		m[1, 3] *= scale;
		m[2, 3] *= scale;
		return 0;
	}

	[LuaMethod]
	static int VMatrix__GetTransposed(ILuaInterface lua) {
		ref Matrix4x4 m = ref Get_VMatrix(1);
		MathLib.MatrixTranspose(in m, out Matrix4x4 transposed);
		Push_VMatrix(transposed);
		return 1;
	}

	[LuaGlobal]
	static int Matrix(ILuaInterface lua) {
		Matrix4x4 matrix = default;
		LuaType type = lua.GetType(1);
		if (type == LuaType.Table) {
			LuaObject table = new(1, LuaType.None);
			LuaObject row = new();
			LuaObject element = new();
			Span<float> elements = MemoryMarshal.CreateSpan(ref matrix.M11, 16);
			for (int r = 1; r <= 4; r++) {
				table.GetMember((float)r, row);
				if (!row.isTable()) {
					g_Lua!.Error($"bad row ({r}) from argument #1 of 'Matrix' (table expected, got {g_Lua.GetTypeName(row.GetType())})");
					element.UnReference();
					row.UnReference();
					table.UnReference();
					return 0;
				}

				for (int c = 1; c <= 4; c++) {
					row.GetMember((float)c, element);
					if (!element.isNumber()) {
						g_Lua!.Error($"bad element ({r}, {c}) from argument #1 of 'Matrix' (number expected, got {g_Lua.GetTypeName(element.GetType())})");
						element.UnReference();
						row.UnReference();
						table.UnReference();
						return 0;
					}
					elements[(r - 1) * 4 + (c - 1)] = element.GetFloat();
				}
			}
			element.UnReference();
			row.UnReference();
			table.UnReference();
		}
		else if (type == LuaType.Nil)
			MathLib.SetIdentityMatrix(out matrix);
		else if (type == LuaType.Matrix)
			matrix = Get_VMatrix(1);
		else {
			g_Lua!.Error($"bad argument #1 to 'Matrix' (table, VMatrix or nil expected, got {g_Lua.GetTypeName(type)})");
			return 0;
		}

		Push_VMatrix(matrix);
		return 1;
	}
}
#endif
