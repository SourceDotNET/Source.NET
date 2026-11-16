using Source.Common.GarrysMod.Lua;

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Game.Lua;

internal static unsafe partial class LuaApi
{
	const string Lib = "lua51";

	public const int MULTRET = -1;

	public const int TNONE = -1;
	public const int TNIL = 0;
	public const int TBOOLEAN = 1;
	public const int TLIGHTUSERDATA = 2;
	public const int TNUMBER = 3;
	public const int TSTRING = 4;
	public const int TTABLE = 5;
	public const int TFUNCTION = 6;
	public const int TUSERDATA = 7;
	public const int TTHREAD = 8;

	public const int OK = 0;
	public const int YIELD = 1;
	public const int ERRRUN = 2;
	public const int ERRSYNTAX = 3;
	public const int ERRMEM = 4;
	public const int ERRERR = 5;

	public const int SN_ERROR = -2;
	public const int SN_ERRORMSG = -3;
	public const int SN_ARGERROR = -4;
	public const int SN_TYPEERROR = -5;
	public static int SN_RETHROW(int status) => -10 - status;
	public static int SN_YIELD(int nresults) => -100 - nresults;

	public const int SN_GETTABLE = 0;
	public const int SN_SETTABLE = 1;
	public const int SN_GETFIELD = 2;
	public const int SN_SETFIELD = 3;
	public const int SN_RAWSET = 4;
	public const int SN_EQUAL = 5;
	public const int SN_LESSTHAN = 6;
	public const int SN_CONCAT = 7;
	public const int SN_NEXT = 8;

	[LibraryImport(Lib)] public static partial nint luaL_newstate();
	[LibraryImport(Lib)] public static partial void luaL_openlibs(nint L);
	[LibraryImport(Lib)] public static partial void lua_close(nint L);
	[LibraryImport(Lib)] public static partial nint lua_atpanic(nint L, delegate* unmanaged<nint, int> panicf);
	[LibraryImport(Lib)] public static partial int luaJIT_setmode(nint L, int idx, int mode);

	[LibraryImport(Lib)] public static partial int lua_sn_init(nint L);
	[LibraryImport(Lib)] public static partial int lua_sn_op(nint L, int op, int idx, int n, byte* k, int* result);
	[LibraryImport(Lib)] public static partial int lua_sn_call(nint L, int nargs, int nresults);
	[LibraryImport(Lib)] public static partial void lua_setoutputf(nint L, delegate* unmanaged<nint, byte*, nuint, nint, void> f, nint ud);
	[LibraryImport(Lib)] public static partial void lua_setstatef(nint L, delegate* unmanaged<nint, nint, void> f, nint ud);

	[LibraryImport(Lib), SuppressGCTransition] public static partial int lua_gettop(nint L);
	[LibraryImport(Lib), SuppressGCTransition] public static partial void lua_settop(nint L, int idx);
	[LibraryImport(Lib)] public static partial void lua_pushvalue(nint L, int idx);
	[LibraryImport(Lib)] public static partial void lua_remove(nint L, int idx);
	[LibraryImport(Lib)] public static partial void lua_insert(nint L, int idx);
	[LibraryImport(Lib)] public static partial int lua_checkstack(nint L, int sz);

	[LibraryImport(Lib), SuppressGCTransition] public static partial int lua_type(nint L, int idx);
	[LibraryImport(Lib), SuppressGCTransition] public static partial int lua_toboolean(nint L, int idx);
	[LibraryImport(Lib), SuppressGCTransition] public static partial double lua_tonumber(nint L, int idx);
	[LibraryImport(Lib), SuppressGCTransition] public static partial nint lua_touserdata(nint L, int idx);
	[LibraryImport(Lib), SuppressGCTransition] public static partial nint lua_tocfunction(nint L, int idx);
	[LibraryImport(Lib), SuppressGCTransition] public static partial nuint lua_objlen(nint L, int idx);
	[LibraryImport(Lib), SuppressGCTransition] public static partial int lua_isnumber(nint L, int idx);
	[LibraryImport(Lib), SuppressGCTransition] public static partial int lua_isstring(nint L, int idx);
	[LibraryImport(Lib), SuppressGCTransition] public static partial int lua_rawequal(nint L, int idx1, int idx2);
	[LibraryImport(Lib)] public static partial byte* lua_tolstring(nint L, int idx, nuint* len);
	[LibraryImport(Lib), SuppressGCTransition] public static partial nint lua_topointer(nint L, int idx);
	[LibraryImport(Lib)] public static partial byte* lua_typename(nint L, int tp, int idx);

	[LibraryImport(Lib), SuppressGCTransition] public static partial void lua_pushnil(nint L);
	[LibraryImport(Lib)] public static partial void lua_pushnumber(nint L, double n);
	[LibraryImport(Lib)] public static partial void lua_pushlstring(nint L, byte* s, nuint len);
	[LibraryImport(Lib)] public static partial void lua_pushboolean(nint L, int b);
	[LibraryImport(Lib)] public static partial void lua_pushcclosure(nint L, nint fn, int n);
	[LibraryImport(Lib)] public static partial void lua_pushlightuserdata(nint L, nint p);

	[LibraryImport(Lib)] public static partial void lua_createtable(nint L, int narr, int nrec);
	[LibraryImport(Lib)] public static partial nint lua_newuserdata(nint L, nuint sz);
	[LibraryImport(Lib), SuppressGCTransition] public static partial int lua_getmetatable(nint L, int idx);
	[LibraryImport(Lib)] public static partial int lua_setmetatable(nint L, int idx);
	[LibraryImport(Lib)] public static partial void lua_rawget(nint L, int idx);
	[LibraryImport(Lib), SuppressGCTransition] public static partial void lua_rawgeti(nint L, int idx, int n);
	[LibraryImport(Lib)] public static partial void lua_rawseti(nint L, int idx, int n);

	[LibraryImport(Lib)] public static partial int lua_pcall(nint L, int nargs, int nresults, int errfunc);
	[LibraryImport(Lib)] public static partial int luaL_loadbuffer(nint L, byte* buff, nuint sz, byte* name);
	[LibraryImport(Lib)] public static partial int luaL_loadbufferx(nint L, byte* buff, nuint sz, byte* name, byte* mode);
	[LibraryImport(Lib)] public static partial int lua_dump(nint L, delegate* unmanaged<nint, void*, nuint, void*, int> writer, void* data);

	[LibraryImport(Lib)] public static partial int luaL_ref(nint L, int t);
	[LibraryImport(Lib)] public static partial void luaL_unref(nint L, int t, int reference);
	[LibraryImport(Lib)] public static partial int luaL_newmetatable_type(nint L, byte* tname, int tid);

	[LibraryImport(Lib)] public static partial int lua_getstack(nint L, int level, lua_Debug* ar);
	[LibraryImport(Lib)] public static partial int lua_getinfo(nint L, byte* what, lua_Debug* ar);
	[LibraryImport(Lib)] public static partial byte* lua_getlocal(nint L, lua_Debug* ar, int n);
	[LibraryImport(Lib)] public static partial byte* lua_getupvalue(nint L, int funcindex, int n);

	// note: no GC transition, strings only and tables nothing writes to meanwhile
	[LibraryImport(Lib, EntryPoint = "lua_tolstring"), SuppressGCTransition] public static partial byte* lua_tolstring_fast(nint L, int idx, nuint* len);
	[LibraryImport(Lib, EntryPoint = "lua_next"), SuppressGCTransition] public static partial int lua_next_fast(nint L, int idx);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void lua_pop(nint L, int n) => lua_settop(L, -n - 1);
}
