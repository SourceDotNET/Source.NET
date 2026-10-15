#if CLIENT_DLL || GAME_DLL
using Source;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaResource // TODO vvv
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_resource = new("resource");

	[LuaFunction]
	static int AddFile(ILuaInterface lua) {
#if GAME_DLL
		if (gpGlobals.MaxClients > 1) {
			Span<char> file = stackalloc char[260];
			strcpy(file, lua.CheckString(1));
			StrTools.FixSlashes(file, '/');
			// AddResource(file, true);
		}
#endif
		return 0;
	}

	[LuaFunction]
	static int AddSingleFile(ILuaInterface lua) {
#if GAME_DLL
		if (gpGlobals.MaxClients > 1) {
			Span<char> file = stackalloc char[260];
			strcpy(file, lua.CheckString(1));
			StrTools.FixSlashes(file, '/');
			// AddResource(file, false);
		}
#endif
		return 0;
	}

#if GAME_DLL
	[LuaFunction]
	static int AddWorkshop(ILuaInterface lua) {
		// if (gpGlobals.MaxClients > 1) {
		// 	g_StringTablesDownloadables = networkstringtable.FindTable("downloadables");
		// 	if (g_StringTablesDownloadables != null)
		// 		AddWorkshop(lua.CheckString(1));
		// }
		return 0;
	}
#endif
}
#endif
