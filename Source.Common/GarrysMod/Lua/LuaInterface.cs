using Source.Common.Commands;

using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Source.Common.GarrysMod.Lua;

public interface ILuaThreadedCall
{
	void Run();
	void OnThreadFinished();
	bool IsFinished();
	void DoFinish(ILuaBase lua);
	void DestroyForced();
}

public interface ILuaInterface : ILuaBase
{
	bool Init(ILuaGameCallback callbacks, bool isServer);
	void Shutdown();
	void Cycle();
	ILuaObject Global();
	ILuaObject GetObject(int index);
	void PushLuaObject(ILuaObject? obj);
	void PushLuaFunction(CFunc func);
	int HandleException(Exception e);
	[DoesNotReturn] void LuaError(ReadOnlySpan<char> err, int index);
	[DoesNotReturn] void TypeError(ReadOnlySpan<char> name, int index);
	bool CallInternal(int args, int rets);
	void CallInternalNoReturns(int args);
	bool CallInternalGetBool(int args);
	string? CallInternalGetString(int args);
	bool CallInternalGet(int args, ILuaObject obj);
	void NewGlobalTable(ReadOnlySpan<char> name);
	ILuaObject NewTemporaryObject();
	bool isUserData(int index);
	ILuaObject? GetMetaTableObject(ReadOnlySpan<char> name, int type);
	ILuaObject? GetMetaTableObject(int index);
	ILuaObject GetReturn(int index);
	bool IsServer();
	bool IsClient();
	bool IsMenu();
	void DestroyObject(ILuaObject? obj);
	ILuaObject CreateObject();
	void SetMember(ILuaObject table, ILuaObject key, ILuaObject? value);
	ILuaObject GetNewTable();
	void SetMember(ILuaObject table, float key);
	void SetMember(ILuaObject table, float key, ILuaObject? value);
	void SetMember(ILuaObject table, ReadOnlySpan<char> key);
	void SetMember(ILuaObject table, ReadOnlySpan<char> key, ILuaObject? value);
	void SetType(byte type);
	void PushLong(long num);
	int GetFlags(int index);
	bool FindOnObjectsMetaTable(int objIndex, int keyIndex);
	bool FindObjectOnTable(int tableIndex, int keyIndex);
	void SetMemberFast(ILuaObject table, int keyIndex, int valueIndex);
	bool RunString(ReadOnlySpan<char> filename, ReadOnlySpan<char> path, ReadOnlySpan<char> stringToRun, bool run, bool showErrors);
	bool IsEqual(ILuaObject? objA, ILuaObject? objB);
	[DoesNotReturn] void Error(ReadOnlySpan<char> err);
	bool TableToJSON(int stackPos, bool pretty, [NotNullWhen(false)] out string? error);
	string GetStringOrError(int index);
	bool RunLuaModule(ReadOnlySpan<char> name);
	bool FindAndRunScript(ReadOnlySpan<char> filename, bool run, bool showErrors, ReadOnlySpan<char> source, bool noReturns);
	void SetPathID(ReadOnlySpan<char> pathID);
	string GetPathID();
	void ErrorNoHalt(ReadOnlySpan<char> msg);
	void Msg(ReadOnlySpan<char> msg);
	void PushPath(ReadOnlySpan<char> path);
	void PopPath();
	string? GetPath();
	Color GetColor(int index);
	void PushColor(Color color);
	int GetStack(int level, ref lua_Debug dbg);
	int GetInfo(ReadOnlySpan<char> what, ref lua_Debug dbg);
	string? GetLocal(ref lua_Debug dbg, int n);
	string? GetUpvalue(int funcIndex, int n);
	bool RunStringEx(ReadOnlySpan<char> filename, ReadOnlySpan<char> path, ReadOnlySpan<char> stringToRun, bool run, bool printErrors, bool dontPushErrors, bool noReturns);
	ReadOnlySpan<byte> GetDataString(int index);
	void ErrorFromLua(ReadOnlySpan<char> msg);
	string GetCurrentLocation();
	void MsgColour(in Color col, ReadOnlySpan<char> msg);
	void GetCurrentFile(out string outStr);
	bool CompileString(out byte[] dump, ReadOnlySpan<char> stringToCompile);
	bool CallFunctionProtected(int args, int rets, bool showError);
	bool Require(ReadOnlySpan<char> name);
	string GetActualTypeName(int stackPos);
	void PreCreateTable(int arrelems, int nonarrelems);
	void PushPooledString(int index);
	string GetPooledString(int index);
	int AddThreadedCall(ILuaThreadedCall call);
	void AppendStackTrace(StringBuilder output);
	ConVar CreateConVar(ReadOnlySpan<char> name, ReadOnlySpan<char> defaultValue, ReadOnlySpan<char> helpString, int flags);
	ConCommand CreateConCommand(ReadOnlySpan<char> name, ReadOnlySpan<char> helpString, int flags, FnCommandCallback? callback, FnCommandCompletionCallback? completionCallback);
	string CheckStringOpt(int stackPos, ReadOnlySpan<char> def);
	double CheckNumberOpt(int stackPos, double def);
	int RegisterMetaTable(ReadOnlySpan<char> name, ILuaObject tbl);
}
