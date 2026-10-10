#if CLIENT_DLL || GAME_DLL
using Source;
using Source.Common.GarrysMod;
using Source.Common.GarrysMod.Lua;

using Game.Shared;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaGame
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_game = new("game");

	[LuaFunction]
	static int GetMap(ILuaInterface lua) {
#if CLIENT_DLL
		string? mapName = IGameSystem.s_MapName;
		if (mapName == null) {
			g_Lua!.PushString("");
			return 1;
		}
#else
		string mapName = IGameSystem.s_MapName ?? gpGlobals.MapName ?? "";
#endif
		Span<char> buffer = stackalloc char[256];
		StrTools.FileBase(mapName, buffer);
		StrTools.StripExtension(buffer.SliceNullTerminatedString(), buffer);
		g_Lua!.PushString(((ReadOnlySpan<char>)buffer).SliceNullTerminatedString());
		return 1;
	}
	// todo: GetMapChangeCount
	// todo: GetMapVersion
	// todo: LoadNextMap
	// todo: MapLoadType
	// todo: StartSpot
	// todo: GetMapNext
	// todo: ConsoleCommand
	// todo: RemoveRagdolls
	// todo: SetTimeScale
	// todo: SetSkillLevel
	// todo: GetTimeScale
	// todo: GetSkillLevel
	// todo: AddParticles
	// todo: IsDedicated

	[LuaFunction]
	static int MountGMA(ILuaInterface lua) {
		string path = g_Lua!.CheckString(1);
		if (!GMOD.IsValidPath(path)) {
			g_Lua.PushBool(false);
			return 1;
		}

		List<string> files = [];
		if (!filesystem.Addons().MountFile(path, files, 0, 0, 2)) {
			g_Lua.PushBool(false);
			return 1;
		}

		g_Lua.PushBool(true);
		g_Lua.CreateTable();
		for (int i = 0; i < files.Count; i++) {
			g_Lua.PushNumber(i + 1);
			g_Lua.PushString(files[i]);
			g_Lua.SetTable(-3);
			// todo: engine.GMOD_LoadModel
			// if (Bootil.String.Test.EndsWith(files[i], ".mdl"))
			// 	engine.GMOD_LoadModel(files[i]);
		}
		return 2;
	}
	[LuaFunction]
	static int GetWorld(ILuaInterface lua) {
#if CLIENT_DLL
		LuaEntity.Push_Entity(C_World.GetClientWorldEntity());
#else
		LuaEntity.Push_Entity(GetWorldEntity());
#endif
		return 1;
	}
	// todo: MaxPlayers
	// todo: GetAmmoID
	// todo: GetAmmoTypes

	[LuaFunction]
	static int SinglePlayer(ILuaInterface lua) {
		g_Lua!.PushBool(gpGlobals.MaxClients == 1);
		return 1;
	}
}
#endif
