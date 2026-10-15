#if CLIENT_DLL || GAME_DLL || MENU_DLL
#if CLIENT_DLL
global using static Game.Client.GarrysMod.LuaGlobals;
#elif MENU_DLL
global using static Game.MenuSystem.GarrysMod.LuaGlobals;
#else
global using static Game.Server.GarrysMod.LuaGlobals;
#endif

using Source;
using Source.Common;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static class LuaGlobals
{
	public static ILuaInterface? g_Lua;
	public static int g_LuaID;
#if MENU_DLL
	public static int g_LuaStarts => Game.MenuSystem.MenuSystem.g_MenuSystem.LuaStarts;
#else
	public static int g_LuaStarts => g_LuaID;
#endif
}

public class LuaObject : ILuaObject
{
	bool userData;
	LuaType luaType;
	int reference;
	int luaID;

	public LuaObject() => Init();

	public LuaObject(int stackPos, LuaType type) {
		Init();
		SetReference(stackPos);
		if (g_Lua != null && type != LuaType.None && luaType != type)
			g_Lua.TypeError(g_Lua.GetTypeName(type), stackPos);
	}

	public void Init() {
		userData = false;
		luaType = LuaType.None;
		reference = -1;
		luaID = g_LuaID;
	}

	void WarnWrongEnvironment() {
		if (luaID != g_LuaID)
			Warning("This should never happen: CLuaObject used on wrong Lua environment.\n");
	}

	public void Set(ILuaObject? obj) {
		if (obj == null) {
			UnReference();
			return;
		}
		obj.Push();
		SetFromStack(-1);
		g_Lua!.Pop(1);
	}

	public void SetFromStack(int i) => SetReference(i);

	public void UnReference() {
		if (reference != -1 && luaID == g_LuaID && luaID != -1)
			g_Lua!.ReferenceFree(reference);
		userData = false;
		luaType = LuaType.None;
		reference = -1;
		luaID = -1;
	}

	public void SetReference(int i) {
		UnReference();
		userData = g_Lua!.isUserData(i);
		luaType = g_Lua.GetType(i);
		luaID = g_LuaID;
		if (luaType != LuaType.Nil)
			g_Lua.Push(i);
		else
			g_Lua.PushNil();
		reference = g_Lua.ReferenceCreate();
	}

	public new LuaType GetType() {
		if (luaID != -1 && luaID != g_LuaID)
			Warning("CLuaObject:GetType with invalid lua state!\n");
		if (luaID != g_LuaID || g_Lua == null)
			UnReference();
		return luaType;
	}

	public string? GetString() {
		Push();
		string? str = g_Lua!.GetString(-1);
		g_Lua.Pop(1);
		return str;
	}

	public string? GetStringLen(out uint len) {
		Push();
		len = (uint)g_Lua!.GetStringBytes(-1).Length;
		string? str = g_Lua.GetString(-1);
		g_Lua.Pop(1);
		return str;
	}

	public float GetFloat() => (float)GetDouble();

	public double GetDouble() {
		if (!isNumber())
			return 0;
		Push();
		double val = g_Lua!.GetNumber(-1);
		g_Lua.Pop(1);
		return val;
	}

	public int GetInt() {
		if (!isNumber())
			return 0;
		Push();
		int val = (int)g_Lua!.GetNumber(-1);
		g_Lua.Pop(1);
		return val;
	}

	public bool GetBool() {
		if (!isBool())
			return false;
		Push();
		bool val = g_Lua!.GetBool(-1);
		g_Lua.Pop(1);
		return val;
	}

	public nint GetUserData() {
		if (!isUserData())
			return 0;
		Push();
		nint ud = g_Lua!.GetUserdata(-1);
		g_Lua.Pop(1);
		return Marshal.ReadIntPtr(ud);
	}

	public void SetMember(ReadOnlySpan<char> name) {
		WarnWrongEnvironment();
		g_Lua!.SetMember(this, name);
	}

	public void SetMember(ReadOnlySpan<char> name, ILuaObject? obj) {
		WarnWrongEnvironment();
		g_Lua!.SetMember(this, name, obj);
	}

	public void SetMember(ReadOnlySpan<char> name, float val) {
		if (!isTable())
			return;
		g_Lua!.PushNumber(val);
		SetMember(name);
	}

	public void SetMember(ReadOnlySpan<char> name, double val) {
		if (!isTable())
			return;
		g_Lua!.PushNumber(val);
		SetMember(name);
	}

	public void SetMember(ReadOnlySpan<char> name, bool val) {
		if (!isTable())
			return;
		g_Lua!.PushBool(val);
		SetMember(name);
	}

	public void SetMember(ReadOnlySpan<char> name, ReadOnlySpan<char> val) {
		if (!isTable())
			return;
		g_Lua!.PushString(val);
		g_Lua.SetMember(this, name);
	}

	public void SetMember(ReadOnlySpan<char> name, ReadOnlySpan<byte> val) {
		if (!isTable())
			return;
		g_Lua!.PushString(val);
		g_Lua.SetMember(this, name);
	}

	public void SetMember(ReadOnlySpan<char> name, CFunc f) {
		if (!isTable())
			return;
		g_Lua!.PushLuaFunction(f);
		g_Lua.SetMember(this, name);
	}

	public unsafe void SetMember(ReadOnlySpan<char> name, delegate* unmanaged[Cdecl]<nint, int> f) {
		if (!isTable())
			return;
		g_Lua!.PushCFunction(f);
		g_Lua.SetMember(this, name);
	}

	public void SetMember(ReadOnlySpan<char> name, int val) {
		if (!isTable())
			return;
		g_Lua!.PushNumber(val);
		SetMember(name);
	}

	public void SetMember(ReadOnlySpan<char> name, ulong val) {
		if (!isTable())
			return;
		g_Lua!.PushString(val.ToString(CultureInfo.InvariantCulture));
		g_Lua.SetMember(this, name);
	}

	public void SetMemberDouble(scoped ReadOnlySpan<char> name, double val) {
		if (!isTable())
			return;
		g_Lua!.PushNumber(val);
		SetMember(name);
	}

	public void SetMember(float key) {
		WarnWrongEnvironment();
		g_Lua!.SetMember(this, key);
	}

	public void SetMember(float key, ILuaObject? obj) {
		WarnWrongEnvironment();
		g_Lua!.SetMember(this, key, obj);
	}

	public void SetMember(float key, float val) {
		if (!isTable())
			return;
		g_Lua!.PushNumber(val);
		SetMember(key);
	}

	public void SetMember(float key, bool val) {
		if (!isTable())
			return;
		g_Lua!.PushBool(val);
		SetMember(key);
	}

	public void SetMember(float key, ReadOnlySpan<char> val) {
		if (!isTable())
			return;
		g_Lua!.PushString(val);
		g_Lua.SetMember(this, key);
	}

	public void SetMember(float key, CFunc f) {
		if (!isTable())
			return;
		g_Lua!.PushLuaFunction(f);
		g_Lua.SetMember(this, key);
	}

	public void SetMemberDouble(float key, double val) {
		if (!isTable())
			return;
		g_Lua!.PushNumber(val);
		SetMember(key);
	}

	public void SetMember(ILuaObject key, ILuaObject? value) {
		WarnWrongEnvironment();
		g_Lua!.SetMember(this, key, value);
	}

	public void SetMemberNil(ReadOnlySpan<char> name) {
		g_Lua!.PushNil();
		SetMember(name);
	}

	public void SetMemberNil(float key) {
		g_Lua!.PushNil();
		SetMember(key);
	}

	public void RemoveMember(ReadOnlySpan<char> name) {
		WarnWrongEnvironment();
		g_Lua!.PushNil();
		g_Lua.SetMember(this, name);
	}

	public void RemoveMember(float key) {
		WarnWrongEnvironment();
		g_Lua!.PushNil();
		g_Lua.SetMember(this, key);
	}

	static bool StrToD(ReadOnlySpan<char> text, out double value) {
		ReadOnlySpan<char> trimmed = text.TrimStart(" \t\n\v\f\r");
		if (trimmed.Length > 2 && (trimmed[0] == '0' || ((trimmed[0] == '-' || trimmed[0] == '+') && trimmed.Length > 3 && trimmed[1] == '0'))) {
			int sign = trimmed[0] == '-' ? -1 : 1;
			ReadOnlySpan<char> hex = trimmed[0] == '0' ? trimmed : trimmed[1..];
			if (hex.Length > 2 && (hex[1] == 'x' || hex[1] == 'X') && ulong.TryParse(hex[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong h)) {
				value = sign * (double)h;
				return true;
			}
		}
		ReadOnlySpan<char> word = trimmed.TrimStart("+-");
		if (word.Equals("inf", StringComparison.OrdinalIgnoreCase) || word.Equals("infinity", StringComparison.OrdinalIgnoreCase) || word.Equals("nan", StringComparison.OrdinalIgnoreCase)) {
			value = word[0] is 'n' or 'N' ? double.NaN : trimmed[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
			return true;
		}
		return double.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value) && trimmed.Length != 0;
	}

	public void SetMember_FixKey(ReadOnlySpan<char> key, float val) {
		if (StrToD(key, out double num))
			SetMember((float)num, val);
		else
			SetMember(key, val);
	}

	public void SetMember_FixKey(ReadOnlySpan<char> key, ReadOnlySpan<char> val) {
		if (StrToD(key, out double num))
			SetMember((float)num, val);
		else
			SetMember(key, val);
	}

	public void SetMember_FixKey(ReadOnlySpan<char> key, ILuaObject? val) {
		if (StrToD(key, out double num))
			SetMember((float)num, val);
		else
			SetMember(key, val);
	}

	public void SetMember_FixKey(ReadOnlySpan<char> key, double val) {
		if (StrToD(key, out double num))
			SetMemberDouble((float)num, val);
		else
			SetMemberDouble(key, val);
	}

	public void SetMember_FixKey(ReadOnlySpan<char> key, int val) {
		if (StrToD(key, out double num))
			SetMember((float)num, (float)val);
		else
			SetMember(key, (ulong)(long)val);
	}

	public bool GetMemberBool(ReadOnlySpan<char> name, bool b = true) {
		if (!isTable() || GetType() != LuaType.Table)
			return b;
		Push();
		g_Lua!.PushString(name);
		g_Lua.GetTable(-2);
		if (g_Lua.GetType(-1) == LuaType.Bool)
			b = g_Lua.GetBool(-1);
		g_Lua.Pop(2);
		return b;
	}

	bool PushMember(ReadOnlySpan<char> name) {
		if (!isTable())
			return false;
		Push();
		g_Lua!.PushString(name);
		g_Lua.GetTable(-2);
		return true;
	}

	bool PushMember(double key) {
		if (!isTable())
			return false;
		Push();
		g_Lua!.PushNumber(key);
		g_Lua.GetTable(-2);
		return true;
	}

	public int GetMemberInt(ReadOnlySpan<char> name, int i = 0) {
		if (!PushMember(name))
			return i;
		if (g_Lua!.GetType(-1) == LuaType.Number)
			i = (int)g_Lua.GetNumber(-1);
		g_Lua.Pop(2);
		return i;
	}

	public uint GetMemberUInt(ReadOnlySpan<char> name, uint def) {
		if (!PushMember(name))
			return def;
		if (g_Lua!.GetType(-1) == LuaType.Number)
			def = (uint)g_Lua.GetNumber(-1);
		g_Lua.Pop(2);
		return def;
	}

	public float GetMemberFloat(ReadOnlySpan<char> name, float f = 0.0f) {
		if (!PushMember(name))
			return f;
		if (g_Lua!.GetType(-1) == LuaType.Number)
			f = (float)g_Lua.GetNumber(-1);
		g_Lua.Pop(2);
		return f;
	}

	public double GetMemberDouble(ReadOnlySpan<char> name, double def) {
		if (!PushMember(name))
			return def;
		if (g_Lua!.GetType(-1) == LuaType.Number)
			def = g_Lua.GetNumber(-1);
		g_Lua.Pop(2);
		return def;
	}

	public double GetMemberDouble(float key, double def) {
		if (!PushMember(key))
			return def;
		if (g_Lua!.GetType(-1) == LuaType.Number)
			def = g_Lua.GetNumber(-1);
		g_Lua.Pop(2);
		return def;
	}

	public string? GetMemberStr(ReadOnlySpan<char> name, string? s = "") {
		if (!PushMember(name))
			return "";
		if (g_Lua!.GetType(-1) == LuaType.String)
			s = g_Lua.GetString(-1);
		g_Lua.Pop(2);
		return s;
	}

	public string? GetMemberStr(float name, string? s = "") {
		if (!PushMember(name))
			return s;
		if (g_Lua!.GetType(-1) == LuaType.String)
			s = g_Lua.GetString(-1);
		g_Lua.Pop(2);
		return s;
	}

	nint PoppedMemberUserData(nint u) {
		if (!g_Lua!.isUserData(-1)) {
			g_Lua.Pop(2);
			return u;
		}
		nint ud = g_Lua.GetUserdata(-1);
		g_Lua.Pop(2);
		nint data = Marshal.ReadIntPtr(ud);
		if (Marshal.ReadByte(ud, nint.Size) != (byte)LuaType.Entity)
			return data;
		return data != 0 ? data : u;
	}

	public nint GetMemberUserData_DontUseMe(ReadOnlySpan<char> name, nint u = 0) => PushMember(name) ? PoppedMemberUserData(u) : u;

	public nint GetMemberUserData_DontUseMe(float name, nint u = 0) => PushMember(name) ? PoppedMemberUserData(u) : u;

	public void GetMember(ReadOnlySpan<char> name, ILuaObject obj) {
		if (!PushMember(name)) {
			obj.UnReference();
			return;
		}
		obj.SetFromStack(-1);
		g_Lua!.Pop(2);
	}

	public void GetMember(ILuaObject key, ILuaObject obj) {
		if (!isTable())
			Dbg.Error("GetMember on a non table");
		Push();
		key.Push();
		g_Lua!.GetTable(-2);
		if (g_Lua.GetType(-1) != LuaType.Nil)
			obj.SetFromStack(-1);
		g_Lua.Pop(2);
	}

	public void GetMember(float key, ILuaObject obj) {
		if (!isTable())
			Dbg.Error("GetMember on a non table");
		Push();
		g_Lua!.PushNumber(key);
		g_Lua.GetTable(-2);
		obj.SetFromStack(-1);
		g_Lua.Pop(2);
	}

	public bool MemberIsNil(ReadOnlySpan<char> name) {
		Push();
		g_Lua!.PushString(name);
		g_Lua.GetTable(-2);
		LuaType type = g_Lua.GetType(-1);
		g_Lua.Pop(2);
		return type == LuaType.Nil;
	}

	public void SetMetaTable(ILuaObject obj) {
		Push();
		obj.Push();
		g_Lua!.SetMetaTable(-2);
		g_Lua.Pop(1);
	}

	public void SetUserData(nint obj) {
		if (obj != 0)
			Dbg.Error("SetUserData called with a non NULL value.");
		Push();
		nint ud = g_Lua!.GetUserdata(-1);
		if (ud != 0)
			Marshal.WriteIntPtr(ud, obj);
		g_Lua.Pop(1);
	}

	public void Push() {
		if (g_Lua == null)
			Dbg.Error("Pushing without Lua");
		if (reference == -1) {
			g_Lua!.PushNil();
			return;
		}
		if (luaID != g_LuaID)
			Dbg.Error("Pushing object with invalid Lua interface!");
		g_Lua!.ReferencePush(reference);
	}

	public bool isNil() {
		if (g_Lua == null || luaID != g_LuaID)
			return true;
		return luaType == LuaType.None || luaType == LuaType.Nil;
	}

	public bool isTable() => GetType() == LuaType.Table;
	public bool isString() => GetType() == LuaType.String;
	public bool isNumber() => GetType() == LuaType.Number;
	public bool isFunction() => GetType() == LuaType.Function;
	public bool isBool() => GetType() == LuaType.Bool;
	public bool isEntity() => GetType() == LuaType.Entity;
	public bool isVector() => GetType() == LuaType.Vector;
	public bool isAngle() => GetType() == LuaType.Angle;

	public bool isUserData() {
		WarnWrongEnvironment();
		return userData;
	}

	public bool PushMemberFast(int stackPos) {
		if (!isTable())
			return false;
		Push();
		g_Lua!.Push(stackPos);
		g_Lua.GetTable(-2);
		if (g_Lua.IsType(-1, LuaType.Nil)) {
			g_Lua.Pop(1);
			return false;
		}
		return true;
	}

	public void SetMemberFast(int key, int value) {
		if (isTable())
			g_Lua!.SetMemberFast(this, key, value);
	}

	public void SetFloat(float val) {
		g_Lua!.PushNumber(val);
		SetFromStack(-1);
		g_Lua.Pop(1);
	}

	public void SetString(ReadOnlySpan<char> val) {
		g_Lua!.PushString(val);
		SetFromStack(-1);
		g_Lua.Pop(1);
	}

	public void SetFromGlobal(ReadOnlySpan<char> name) {
		if (g_Lua == null || g_Lua.Global() == null)
			Dbg.Error("SetFromGlobal null table / no lua?\n");
		g_Lua!.Global().GetMember(name, this);
	}

	bool PushTypedMember(LuaType type) {
		LuaType actual = g_Lua!.GetType(-1);
		if (actual != type && actual != LuaType.Nil) {
			g_Lua.ErrorFromLua($"GetTableMember: Invalid type {g_Lua.GetActualTypeName(-1)}, expecting {g_Lua.GetTypeName(type)}\n");
			g_Lua.Pop(2);
			return false;
		}
		if (!g_Lua.isUserData(-1)) {
			g_Lua.Pop(2);
			return false;
		}
		return true;
	}

	T GetMemberValue<T>(LuaType type, in T def) where T : unmanaged {
		if (!PushTypedMember(type))
			return def;
		T val = g_Lua!.GetValueUserType<T>(-1, type);
		bool present = Marshal.ReadIntPtr(g_Lua.GetUserdata(-1)) != 0;
		g_Lua.Pop(2);
		return present ? val : default;
	}

	public void SetMemberVector(ReadOnlySpan<char> name, in Vector3 vec) {
		if (!isTable())
			return;
		g_Lua!.PushVector(vec);
		g_Lua.SetMember(this, name);
	}

	public void SetMemberVector(float key, in Vector3 vec) {
		if (!isTable())
			return;
		g_Lua!.PushVector(vec);
		g_Lua.SetMember(this, key);
	}

	public Vector3 GetMemberVector(ReadOnlySpan<char> name, in Vector3 def) => PushMember(name) ? GetMemberValue(LuaType.Vector, def) : def;

	public Vector3 GetMemberVector(int key) => PushMember(key) ? GetMemberValue(LuaType.Vector, default(Vector3)) : default;

	public Vector3 GetVector() {
		if (!isVector())
			return default;
		Push();
		Vector3 val = g_Lua!.GetValueUserType<Vector3>(-1, LuaType.Vector);
		g_Lua.Pop(1);
		return val;
	}

	public void SetMemberAngle(ReadOnlySpan<char> name, in QAngle ang) {
		if (!isTable())
			return;
		g_Lua!.PushAngle(ang);
		g_Lua.SetMember(this, name);
	}

	public QAngle GetMemberAngle(ReadOnlySpan<char> name, in QAngle def) => PushMember(name) ? GetMemberValue(LuaType.Angle, def) : def;

	public QAngle GetAngle() {
		if (!isAngle())
			return default;
		Push();
		QAngle val = g_Lua!.GetValueUserType<QAngle>(-1, LuaType.Angle);
		g_Lua.Pop(1);
		return val;
	}

	public void SetMemberMatrix(ReadOnlySpan<char> name, in Matrix4x4 mat) {
		if (!isTable())
			return;
		g_Lua!.PushValueUserType(mat, LuaType.Matrix);
		g_Lua.SetMember(this, name);
	}

	public void SetMemberMatrix(float key, in Matrix4x4 mat) {
		if (!isTable())
			return;
		g_Lua!.PushValueUserType(mat, LuaType.Matrix);
		g_Lua.SetMember(this, key);
	}

	public void SetMemberMatrix(int key, in Matrix4x4 mat) => SetMemberMatrix((float)key, mat);

	public Matrix4x4 GetMemberMatrix(int key, in Matrix4x4 def) => PushMember(key) ? GetMemberValue(LuaType.Matrix, def) : def;

	public IHandleEntity? GetMemberEntity(ReadOnlySpan<char> name, IHandleEntity? def) => throw new NotImplementedException();
	public IHandleEntity? GetMemberEntity(int key, IHandleEntity? def) => throw new NotImplementedException();
	public void SetMemberEntity(float key, IHandleEntity? ent) => throw new NotImplementedException();
#if MENU_DLL
	public void SetMemberEntity(ReadOnlySpan<char> name, IHandleEntity? ent) => throw new NotImplementedException();
	public IHandleEntity? GetEntity() => throw new NotImplementedException();
#else
	public void SetMemberEntity(ReadOnlySpan<char> name, IHandleEntity? ent) {
		if (!isTable())
			return;
		LuaEntity.Push_Entity(EntityFromEntityHandle(ent));
		SetMember(name);
	}

	public IHandleEntity? GetEntity() {
		if (!isEntity())
			return null;
		Push();
		BaseEntity? ent = LuaEntity.UserGet(-1);
		g_Lua!.Pop(1);
		return ent;
	}
#endif
	public void SetEntity(IHandleEntity? ent) => throw new NotImplementedException();
	public void SetMemberPhysObject(ReadOnlySpan<char> name, IPhysicsObject? obj) => throw new NotImplementedException();
}

public class LuaTable : LuaObject
{
	public LuaTable(string? name = null, uint size = 0) {
		if (g_Lua == null)
			return;

		g_Lua.PreCreateTable((int)size, 0);
		SetReference(-1);
		g_Lua.Pop(1);

		if (name == null)
			return;

		if (g_Lua.Global() == null)
			Dbg.Error("This should never happen! No global in CLuaTable!\n");

		g_Lua.Global().SetMember(name, this);
	}
}

public class LuaDisposable : LuaObject, IDisposable
{
	public LuaDisposable(int stackPos, LuaType type) : base(stackPos, type) { }

	public void Dispose() {
		SetUserData(0);
		UnReference();
	}
}
#endif
