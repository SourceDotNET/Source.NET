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
#if GAME_DLL
	[LuaFunction]
	static int SetTimeScale(ILuaInterface lua) {
		if (g_pGameRules is not GMODGameRules rules)
			return 0;
		rules.TimeScale = (float)g_Lua!.CheckNumber(1);
		return 0;
	}

	[LuaFunction]
	static int SetSkillLevel(ILuaInterface lua) {
		if (g_pGameRules is not GMODGameRules rules)
			return 0;
		rules.SkillLevel = (int)g_Lua!.CheckNumber(1);
		return 0;
	}

	[LuaFunction]
	static int GetGlobalState(ILuaInterface lua) {
		int index = GlobalEntity.GetIndex(g_Lua!.CheckString(1));
		g_Lua.PushNumber(index < 0 ? (int)GlobalEState.Dead : (int)GlobalEntity.GetState(index));
		return 1;
	}

	[LuaFunction]
	static int SetGlobalState(ILuaInterface lua) {
		string name = g_Lua!.CheckString(1);
		int index = GlobalEntity.GetIndex(name);
		GlobalEState state = (GlobalEState)Math.Clamp((int)g_Lua.CheckNumber(2), (int)GlobalEState.Off, (int)GlobalEState.Dead);
		if (index < 0) {
			GlobalEntity.Add(name, gpGlobals.MapName ?? "", state);
			GlobalEntity.SetCounter(GlobalEntity.GetIndex(name), 0);
		}
		else
			GlobalEntity.SetState(index, state);
		return 0;
	}

	[LuaFunction]
	static int GetGlobalCounter(ILuaInterface lua) {
		int index = GlobalEntity.GetIndex(g_Lua!.CheckString(1));
		g_Lua.PushNumber(index < 0 ? 0 : GlobalEntity.GetCounter(index));
		return 1;
	}

	[LuaFunction]
	static int SetGlobalCounter(ILuaInterface lua) {
		string name = g_Lua!.CheckString(1);
		int index = GlobalEntity.GetIndex(name);
		int counter = (int)g_Lua.CheckNumber(2);
		if (index < 0) {
			GlobalEntity.Add(name, gpGlobals.MapName ?? "", GlobalEState.Off);
			index = GlobalEntity.GetIndex(name);
		}
		GlobalEntity.SetCounter(index, counter);
		return 0;
	}

	public static bool MapCleanupRunning;
	public static bool MapCleanupDeferred;
	static bool MapCleanupPending;
	static bool MapCleanupDontSendToClients;
	static readonly LuaObject MapCleanupFilters = new();
	static readonly LuaObject MapCleanupCallback = new();

	[LuaFunction]
	static int CleanUpMap(ILuaInterface lua) {
		// todo: refuse with "Cannot run game.CleanUpMap while deleting entities!\n" while entities are being deleted
		if (MapCleanupRunning || MapCleanupPending) {
			lua.ErrorFromLua("Cannot run game.CleanUpMap while it is already running!\n");
			return 0;
		}

		bool dontSendToClients = lua.GetType(1) != LuaType.Nil && lua.GetBool(1);

		LuaObject filters = new(2, LuaType.None);
		if (!filters.isNil() && !filters.isTable()) {
			filters.UnReference();
			lua.TypeError("table", 2);
		}

		LuaObject callback = new(3, LuaType.None);
		if (!callback.isNil() && !callback.isFunction()) {
			callback.UnReference();
			filters.UnReference();
			lua.TypeError("function", 3);
		}

		if (callback.isNil())
			DoCleanUpMap(dontSendToClients, filters);
		else {
			MapCleanupPending = true;
			MapCleanupCallback.Set(callback);
			MapCleanupFilters.Set(filters);
			MapCleanupDontSendToClients = dontSendToClients;
		}

		callback.UnReference();
		filters.UnReference();
		return 0;
	}

	static void DoCleanUpMap(bool dontSendToClients, ILuaObject filters) {
		MapCleanupRunning = true;
		gGM?.Call((int)LUA_POOLEDSTRING.PreCleanupMap);

		if (g_pGameRules is GMODGameRules rules)
			rules.CleanUpMap(filters);

		if (!dontSendToClients) {
			RecipientFilter filter = new();
			filter.AddAllPlayers();
			filter.MakeReliable();
			GarrysMod.BroadcastLua(filter, "game.CleanUpMap()");
		}

		// todo: unidentified step between the client broadcast and AllowImmediateEdictReuse
		engine.AllowImmediateEdictReuse();

		gGM?.Call((int)LUA_POOLEDSTRING.PostCleanupMap);
		MapCleanupRunning = false;
	}

	public static void RunMapCleanupIfNeeded() {
		if (!MapCleanupPending)
			return;

		MapCleanupDeferred = true;
		DoCleanUpMap(MapCleanupDontSendToClients, MapCleanupFilters);
		MapCleanupDeferred = false;
		MapCleanupPending = false;

		if (g_Lua != null && MapCleanupCallback.isFunction()) {
			MapCleanupCallback.Push();
			g_Lua.CallInternalNoReturns(0);
		}

		MapCleanupCallback.UnReference();
		MapCleanupFilters.UnReference();
		MapCleanupDontSendToClients = false;
	}
#endif

#if CLIENT_DLL
	public static bool MapCleanupRunning;
	static bool MapCleanupPending;
	static readonly LuaObject MapCleanupCallback = new();

	[LuaFunction]
	static int CleanUpMap(ILuaInterface lua) {
		// todo: refuse with "Cannot run game.CleanUpMap while deleting entities!\n" while entities are being deleted
		if (MapCleanupRunning || MapCleanupPending) {
			lua.ErrorFromLua("Cannot run game.CleanUpMap while it is already running!\n");
			return 0;
		}

		LuaObject callback = new(3, LuaType.None);
		if (!callback.isNil() && !callback.isFunction()) {
			callback.UnReference();
			lua.TypeError("function", 3);
		}

		if (callback.isNil())
			DoCleanUpMap();
		else {
			MapCleanupPending = true;
			MapCleanupCallback.Set(callback);
		}

		callback.UnReference();
		return 0;
	}

	static void DoCleanUpMap() {
		MapCleanupRunning = true;
		gGM?.Call((int)LUA_POOLEDSTRING.PreCleanupMap);

		// todo: remove the Lua-created clientside entities
		engine.ClientCmd("r_cleardecals\n");
		enginesound.StopAllSounds(true);
		// todo: unidentified call that follows StopAllSounds
		// todo: release every C_Hairball in the client entity list
		// todo: unidentified step before the PostCleanupMap hook

		gGM?.Call((int)LUA_POOLEDSTRING.PostCleanupMap);
		MapCleanupRunning = false;
	}

	public static void RunMapCleanupIfNeeded() {
		if (!MapCleanupPending)
			return;

		DoCleanUpMap();
		MapCleanupPending = false;

		if (g_Lua != null && MapCleanupCallback.isFunction()) {
			MapCleanupCallback.Push();
			g_Lua.CallInternalNoReturns(0);
		}

		MapCleanupCallback.UnReference();
	}
#endif

	[LuaFunction]
	static int GetTimeScale(ILuaInterface lua) {
		if (g_pGameRules is not GMODGameRules rules)
			return 0;
		g_Lua!.PushNumber(rules.TimeScale);
		return 1;
	}

	[LuaFunction]
	static int GetSkillLevel(ILuaInterface lua) {
		if (g_pGameRules is not GMODGameRules rules)
			return 0;
		g_Lua!.PushNumber(rules.SkillLevel);
		return 1;
	}

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
	[LuaFunction]
	static int GetAmmoID(ILuaInterface lua) {
		g_Lua!.PushNumber(GetAmmoDef().Index(g_Lua.CheckString(1)));
		return 1;
	}
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
