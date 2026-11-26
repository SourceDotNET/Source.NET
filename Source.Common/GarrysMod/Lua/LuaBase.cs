using Source.Common.Mathematics;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Source.Common.GarrysMod.Lua;

public delegate int CFunc(ILuaInterface lua);

public enum Special
{
	Glob,
	Env,
	Reg
}

public static class LuaIndex
{
	public const int Registry = -10000;
	public const int Environment = -10001;
	public const int Global = -10002;

	public static int Upvalue(int i) => Global - i;
}

public interface ILuaBase
{
	int Top();
	void Push(int stackPos);
	void Pop(int amt = 1);
	void GetTable(int stackPos);
	void GetField(int stackPos, ReadOnlySpan<char> name);
	void SetField(int stackPos, ReadOnlySpan<char> name);
	void CreateTable();
	void SetTable(int stackPos);
	void SetMetaTable(int stackPos);
	bool GetMetaTable(int stackPos);
	void Call(int args, int results);
	int PCall(int args, int results, int errorFunc);
	bool Equal(int a, int b);
	bool RawEqual(int a, int b);
	void Insert(int stackPos);
	void Remove(int stackPos);
	bool Next(int stackPos);
	nint NewUserdata(uint size);
	[DoesNotReturn] void ThrowError(ReadOnlySpan<char> error);
	void CheckType(int stackPos, LuaType type);
	[DoesNotReturn] void ArgError(int argNum, ReadOnlySpan<char> message);
	void RawGet(int stackPos);
	void RawSet(int stackPos);
	string? GetString(int stackPos = -1);
	double GetNumber(int stackPos = -1);
	bool GetBool(int stackPos = -1);
	CFunc? GetCFunction(int stackPos = -1);
	nint GetUserdata(int stackPos = -1);
	void PushNil();
	void PushString(ReadOnlySpan<char> val);
	void PushNumber(double val);
	void PushBool(bool val);
	void PushCFunction(CFunc val);
	unsafe void PushCFunction(delegate* unmanaged[Cdecl]<nint, int> val);
	void PushCClosure(CFunc val, int vars);
	void PushUserdata(nint userdata);
	int ReferenceCreate();
	void ReferenceFree(int i);
	void ReferencePush(int i);
	void PushSpecial(Special type);
	bool IsType(int stackPos, LuaType type);
	LuaType GetType(int stackPos);
	string GetTypeName(LuaType type);
	void CreateMetaTableType(ReadOnlySpan<char> name, int type);
	string CheckString(int stackPos = -1);
	ReadOnlySpan<char> CheckString(int stackPos, Span<char> buffer);
	double CheckNumber(int stackPos = -1);
	int ObjLen(int stackPos = -1);
	QAngle GetAngle(int stackPos = -1);
	Vector3 GetVector(int stackPos = -1);
	void PushAngle(in QAngle val);
	void PushVector(in Vector3 val);
	void SetState(lua_State state);
	int CreateMetaTable(ReadOnlySpan<char> name);
	bool PushMetaTable(LuaType type);
	void PushUserType(nint data, LuaType type);
	void SetUserType(int stackPos, nint data);

	ReadOnlySpan<byte> GetStringBytes(int stackPos = -1);
	void PushString(ReadOnlySpan<byte> val);

	void PushValueUserType<T>(in T val, LuaType type) where T : unmanaged;
	ref T GetValueUserType<T>(int stackPos, LuaType type) where T : unmanaged;

	void PushObjectUserType<T>(T? obj, LuaType type) where T : class;
	T? GetObjectUserType<T>(int stackPos, LuaType type) where T : class;
	void ReleaseUserTypeObject(object obj);
}
