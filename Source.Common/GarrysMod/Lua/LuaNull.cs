using Source.Common.Commands;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;

namespace Source.Common.GarrysMod.Lua;

public sealed class LuaNullObject : ILuaObject
{
	public static readonly LuaNullObject Instance = new();

	public void Set(ILuaObject? obj) { }
	public void SetFromStack(int i) { }
	public void UnReference() { }

	public new LuaType GetType() => LuaType.Nil;
	public string? GetString() => null;
	public float GetFloat() => 0;
	public int GetInt() => 0;
	public nint GetUserData() => 0;

	public void SetMember(ReadOnlySpan<char> name) { }
	public void SetMember(ReadOnlySpan<char> name, ILuaObject? obj) { }
	public void SetMember(ReadOnlySpan<char> name, float val) { }
	public void SetMember(ReadOnlySpan<char> name, bool val) { }
	public void SetMember(ReadOnlySpan<char> name, ReadOnlySpan<char> val) { }
	public void SetMember(ReadOnlySpan<char> name, CFunc f) { }
	public unsafe void SetMember(ReadOnlySpan<char> name, delegate* unmanaged[Cdecl]<nint, int> f) { }

	public bool GetMemberBool(ReadOnlySpan<char> name, bool b = true) => b;
	public int GetMemberInt(ReadOnlySpan<char> name, int i = 0) => i;
	public float GetMemberFloat(ReadOnlySpan<char> name, float f = 0.0f) => f;
	public string? GetMemberStr(ReadOnlySpan<char> name, string? s = "") => s;
	public nint GetMemberUserData_DontUseMe(ReadOnlySpan<char> name, nint u = 0) => u;
	public nint GetMemberUserData_DontUseMe(float name, nint u = 0) => u;
	public void GetMember(ReadOnlySpan<char> name, ILuaObject obj) { }
	public void GetMember(ILuaObject key, ILuaObject obj) { }

	public void SetMetaTable(ILuaObject obj) { }
	public void SetUserData(nint obj) { }

	public void Push() { }

	public bool isNil() => true;
	public bool isTable() => false;
	public bool isString() => false;
	public bool isNumber() => false;
	public bool isFunction() => false;
	public bool isUserData() => false;

	public void GetMember(float key, ILuaObject obj) { }

	public void SetMember(float key) { }
	public void SetMember(float key, ILuaObject? obj) { }
	public void SetMember(float key, float val) { }
	public void SetMember(float key, bool val) { }
	public void SetMember(float key, ReadOnlySpan<char> val) { }
	public void SetMember(float key, CFunc f) { }

	public string? GetMemberStr(float name, string? s = "") => s;

	public void SetMember(ILuaObject key, ILuaObject? value) { }
	public bool GetBool() => false;

	public bool PushMemberFast(int stackPos) => false;
	public void SetMemberFast(int key, int value) { }

	public void SetFloat(float val) { }
	public void SetString(ReadOnlySpan<char> val) { }

	public double GetDouble() => 0;

	public void SetMember_FixKey(ReadOnlySpan<char> key, float val) { }
	public void SetMember_FixKey(ReadOnlySpan<char> key, ReadOnlySpan<char> val) { }
	public void SetMember_FixKey(ReadOnlySpan<char> key, ILuaObject? val) { }
	public void SetMember_FixKey(ReadOnlySpan<char> key, double val) { }
	public void SetMember_FixKey(ReadOnlySpan<char> key, int val) { }

	public bool isBool() => false;

	public void SetMemberDouble(scoped ReadOnlySpan<char> name, double val) { }

	public void SetMemberNil(ReadOnlySpan<char> name) { }
	public void SetMemberNil(float key) { }

	public void Init() { }

	public void SetFromGlobal(ReadOnlySpan<char> name) { }

	public string? GetStringLen(out uint len) {
		len = 0;
		return null;
	}

	public uint GetMemberUInt(ReadOnlySpan<char> name, uint def) => def;

	public void SetMember(ReadOnlySpan<char> name, ulong val) { }
	public void SetMember(ReadOnlySpan<char> name, int val) { }
	public void SetReference(int i) { }

	public void RemoveMember(ReadOnlySpan<char> name) { }
	public void RemoveMember(float key) { }

	public bool MemberIsNil(ReadOnlySpan<char> name) => true;

	public void SetMemberDouble(float key, double val) { }
	public double GetMemberDouble(ReadOnlySpan<char> name, double def) => def;

	public IHandleEntity? GetMemberEntity(ReadOnlySpan<char> name, IHandleEntity? def) => def;
	public void SetMemberEntity(float key, IHandleEntity? ent) { }
	public void SetMemberEntity(ReadOnlySpan<char> name, IHandleEntity? ent) { }
	public bool isEntity() => false;
	public IHandleEntity? GetEntity() => null;
	public void SetEntity(IHandleEntity? ent) { }

	public void SetMemberVector(ReadOnlySpan<char> name, in Vector3 vec) { }
	public void SetMemberVector(float key, in Vector3 vec) { }
	public Vector3 GetMemberVector(ReadOnlySpan<char> name, in Vector3 def) => def;
	public Vector3 GetMemberVector(int key) => default;
	public Vector3 GetVector() => default;
	public bool isVector() => false;

	public void SetMemberAngle(ReadOnlySpan<char> name, in QAngle ang) { }
	public QAngle GetMemberAngle(ReadOnlySpan<char> name, in QAngle def) => def;
	public QAngle GetAngle() => default;
	public bool isAngle() => false;

	public void SetMemberMatrix(ReadOnlySpan<char> name, in Matrix4x4 mat) { }
	public void SetMemberMatrix(float key, in Matrix4x4 mat) { }
	public void SetMemberMatrix(int key, in Matrix4x4 mat) { }

	public void SetMemberPhysObject(ReadOnlySpan<char> name, IPhysicsObject? obj) { }
	public double GetMemberDouble(float key, double def) => def;
	public IHandleEntity? GetMemberEntity(int key, IHandleEntity? def) => def;
	public Matrix4x4 GetMemberMatrix(int key, in Matrix4x4 def) => def;
}

public sealed class LuaNullInterface : ILuaInterface
{
	static class Dummy<T> where T : unmanaged
	{
		public static T Value;
	}

	byte Type;
	string PathID = "";

	public int Top() => 0;
	public void Push(int stackPos) { }
	public void Pop(int amt = 1) { }
	public void GetTable(int stackPos) { }
	public void GetField(int stackPos, ReadOnlySpan<char> name) { }
	public void SetField(int stackPos, ReadOnlySpan<char> name) { }
	public void CreateTable() { }
	public void SetTable(int stackPos) { }
	public void SetMetaTable(int stackPos) { }
	public bool GetMetaTable(int stackPos) => false;
	public void Call(int args, int results) { }
	public int PCall(int args, int results, int errorFunc) => 0;
	public bool Equal(int a, int b) => false;
	public bool RawEqual(int a, int b) => false;
	public void Insert(int stackPos) { }
	public void Remove(int stackPos) { }
	public bool Next(int stackPos) => false;
	public nint NewUserdata(uint size) => 0;
	[DoesNotReturn] public void ThrowError(ReadOnlySpan<char> error) => throw new NotSupportedException(error.ToString());
	public void CheckType(int stackPos, LuaType type) { }
	[DoesNotReturn] public void ArgError(int argNum, ReadOnlySpan<char> message) => throw new NotSupportedException(message.ToString());
	public void RawGet(int stackPos) { }
	public void RawSet(int stackPos) { }
	public string? GetString(int stackPos = -1) => null;
	public double GetNumber(int stackPos = -1) => 0;
	public bool GetBool(int stackPos = -1) => false;
	public CFunc? GetCFunction(int stackPos = -1) => null;
	public nint GetUserdata(int stackPos = -1) => 0;
	public void PushNil() { }
	public void PushString(ReadOnlySpan<char> val) { }
	public void PushNumber(double val) { }
	public void PushBool(bool val) { }
	public void PushCFunction(CFunc val) { }
	public unsafe void PushCFunction(delegate* unmanaged[Cdecl]<nint, int> val) { }
	public void PushCClosure(CFunc val, int vars) { }
	public void PushUserdata(nint userdata) { }
	public int ReferenceCreate() => -1;
	public void ReferenceFree(int i) { }
	public void ReferencePush(int i) { }
	public void PushSpecial(Special type) { }
	public bool IsType(int stackPos, LuaType type) => type == LuaType.Nil;
	public LuaType GetType(int stackPos) => LuaType.Nil;
	public string GetTypeName(LuaType type) => "nil";
	public void CreateMetaTableType(ReadOnlySpan<char> name, int type) { }
	public string CheckString(int stackPos = -1) => "";
	public ReadOnlySpan<char> CheckString(int stackPos, Span<char> buffer) => [];
	public double CheckNumber(int stackPos = -1) => 0;
	public int ObjLen(int stackPos = -1) => 0;
	public QAngle GetAngle(int stackPos = -1) => default;
	public Vector3 GetVector(int stackPos = -1) => default;
	public void PushAngle(in QAngle val) { }
	public void PushVector(in Vector3 val) { }
	public void SetState(lua_State state) { }
	public int CreateMetaTable(ReadOnlySpan<char> name) => 0;
	public bool PushMetaTable(LuaType type) => false;
	public void PushUserType(nint data, LuaType type) { }
	public void SetUserType(int stackPos, nint data) { }

	public ReadOnlySpan<byte> GetStringBytes(int stackPos = -1) => [];
	public void PushString(ReadOnlySpan<byte> val) { }

	public void PushValueUserType<T>(in T val, LuaType type) where T : unmanaged { }
	public ref T GetValueUserType<T>(int stackPos, LuaType type) where T : unmanaged => ref Dummy<T>.Value;

	public void PushObjectUserType<T>(T? obj, LuaType type) where T : class { }
	public T? GetObjectUserType<T>(int stackPos, LuaType type) where T : class => null;
	public void ReleaseUserTypeObject(object obj) { }

	public bool Init(ILuaGameCallback callbacks, bool isServer) => true;
	public void Shutdown() { }
	public void Cycle() { }
	public ILuaObject Global() => LuaNullObject.Instance;
	public ILuaObject GetObject(int index) => LuaNullObject.Instance;
	public void PushLuaObject(ILuaObject? obj) { }
	public void PushLuaFunction(CFunc func) { }
	public int HandleException(Exception e) => 0;
	[DoesNotReturn] public void LuaError(ReadOnlySpan<char> err, int index) => throw new NotSupportedException(err.ToString());
	[DoesNotReturn] public void TypeError(ReadOnlySpan<char> name, int index) => throw new NotSupportedException(name.ToString());
	public bool CallInternal(int args, int rets) => false;
	public void CallInternalNoReturns(int args) { }
	public bool CallInternalGetBool(int args) => false;
	public string? CallInternalGetString(int args) => null;
	public bool CallInternalGet(int args, ILuaObject obj) => false;
	public void NewGlobalTable(ReadOnlySpan<char> name) { }
	public ILuaObject NewTemporaryObject() => LuaNullObject.Instance;
	public bool isUserData(int index) => false;
	public ILuaObject? GetMetaTableObject(ReadOnlySpan<char> name, int type) => LuaNullObject.Instance;
	public ILuaObject? GetMetaTableObject(int index) => LuaNullObject.Instance;
	public ILuaObject GetReturn(int index) => LuaNullObject.Instance;
	public bool IsServer() => Type == 1;
	public bool IsClient() => Type == 0;
	public bool IsMenu() => Type == 2;
	public void DestroyObject(ILuaObject? obj) { }
	public ILuaObject CreateObject() => LuaNullObject.Instance;
	public void SetMember(ILuaObject table, ILuaObject key, ILuaObject? value) { }
	public ILuaObject GetNewTable() => LuaNullObject.Instance;
	public void SetMember(ILuaObject table, float key) { }
	public void SetMember(ILuaObject table, float key, ILuaObject? value) { }
	public void SetMember(ILuaObject table, ReadOnlySpan<char> key) { }
	public void SetMember(ILuaObject table, ReadOnlySpan<char> key, ILuaObject? value) { }
	public void SetType(byte type) => Type = type;
	public void PushLong(long num) { }
	public int GetFlags(int index) => 0;
	public bool FindOnObjectsMetaTable(int objIndex, int keyIndex) => false;
	public bool FindObjectOnTable(int tableIndex, int keyIndex) => false;
	public void SetMemberFast(ILuaObject table, int keyIndex, int valueIndex) { }
	public bool RunString(ReadOnlySpan<char> filename, ReadOnlySpan<char> path, ReadOnlySpan<char> stringToRun, bool run, bool showErrors) => false;
	public bool IsEqual(ILuaObject? objA, ILuaObject? objB) => false;
	[DoesNotReturn] public void Error(ReadOnlySpan<char> err) => throw new NotSupportedException(err.ToString());
	public string GetStringOrError(int index) => "";
	public bool RunLuaModule(ReadOnlySpan<char> name) => false;
	public bool FindAndRunScript(ReadOnlySpan<char> filename, bool run, bool showErrors, ReadOnlySpan<char> source, bool noReturns) => false;
	public void SetPathID(ReadOnlySpan<char> pathID) => PathID = pathID.ToString();
	public string GetPathID() => PathID;
	public void ErrorNoHalt(ReadOnlySpan<char> msg) { }
	public void Msg(ReadOnlySpan<char> msg) { }
	public void PushPath(ReadOnlySpan<char> path) { }
	public void PopPath() { }
	public string? GetPath() => null;
	public Color GetColor(int index) => default;
	public void PushColor(Color color) { }
	public int GetStack(int level, ref lua_Debug dbg) => 0;
	public int GetInfo(ReadOnlySpan<char> what, ref lua_Debug dbg) => 0;
	public string? GetLocal(ref lua_Debug dbg, int n) => null;
	public string? GetUpvalue(int funcIndex, int n) => null;
	public bool RunStringEx(ReadOnlySpan<char> filename, ReadOnlySpan<char> path, ReadOnlySpan<char> stringToRun, bool run, bool printErrors, bool dontPushErrors, bool noReturns) => false;
	public ReadOnlySpan<byte> GetDataString(int index) => [];
	public void ErrorFromLua(ReadOnlySpan<char> msg) { }
	public string GetCurrentLocation() => "";
	public void MsgColour(in Color col, ReadOnlySpan<char> msg) { }
	public void GetCurrentFile(out string outStr) => outStr = "";
	public bool CompileString(out byte[] dump, ReadOnlySpan<char> stringToCompile) {
		dump = [];
		return false;
	}
	public bool CallFunctionProtected(int args, int rets, bool showError) => false;
	public bool Require(ReadOnlySpan<char> name) => false;
	public string GetActualTypeName(int stackPos) => "nil";
	public void PreCreateTable(int arrelems, int nonarrelems) { }
	public void PushPooledString(int index) { }
	public string GetPooledString(int index) => "";
	public int AddThreadedCall(ILuaThreadedCall call) => 0;
	public void AppendStackTrace(StringBuilder output) { }
	public ConVar CreateConVar(ReadOnlySpan<char> name, ReadOnlySpan<char> defaultValue, ReadOnlySpan<char> helpString, int flags) => throw new NotSupportedException();
	public ConCommand CreateConCommand(ReadOnlySpan<char> name, ReadOnlySpan<char> helpString, int flags, FnCommandCallback? callback, FnCommandCompletionCallback? completionCallback) => throw new NotSupportedException();
	public string CheckStringOpt(int stackPos, ReadOnlySpan<char> def) => def.ToString();
	public double CheckNumberOpt(int stackPos, double def) => def;
	public int RegisterMetaTable(ReadOnlySpan<char> name, ILuaObject tbl) => 0;
}
