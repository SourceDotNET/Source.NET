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
	static int AddParticles(ILuaInterface lua) {
		string file = g_Lua!.CheckString(1);
		if (LuaFileLibrary.IsFileAccessAllowed(file, "GAME", false, false)) {
			// GMOD_LoadParticleConfigFile(file); // TODO!
		}
		return 0;
	}

	[LuaFunction]
	static int AddDecal(ILuaInterface lua) {
		LuaObject materials = new();
		materials.SetFromStack(2);
		if (!materials.isTable()) {
			// decalsystem.AddDecal(lua.CheckString(1), lua.CheckString(2)); // TODO!!!
			materials.UnReference();
			return 0;
		}

		List<string> list = [];
		LuaObject entry = new();
		for (int i = 1; ; i++) {
			materials.GetMember(i, entry);
			if (!entry.isString())
				break;
			list.Add(entry.GetString()!);
		}
		entry.UnReference();

		// if (list.Count != 0)
		// 	decalsystem.AddDecal(lua.CheckString(1), list); // TODO!!!
		materials.UnReference();
		return 0;
	}

	[LuaFunction]
	static int MaxPlayers(ILuaInterface lua) {
		g_Lua!.PushNumber(gpGlobals.MaxClients);
		return 1;
	}

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

	[LuaFunction]
	static int IsDedicated(ILuaInterface lua) {
		g_Lua!.PushBool(engine.IsDedicatedServer());
		return 1;
	}
}
#endif
