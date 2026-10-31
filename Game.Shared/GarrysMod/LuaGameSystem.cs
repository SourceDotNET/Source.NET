
#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source.Common;

using static Source.Common.GarrysMod.Lua.PooledStrings;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static class LuaGameSystemGlobals
{
	public static readonly LuaGameSystem g_LuaGameSystem = new();
}

public class LuaGameSystem : AutoGameSystemPerFrame, IGameEventListener2
{
	bool RegisteredForEvents;
#if CLIENT_DLL
	string Gamemode = "";
	static bool UserDataDownloaded;
#endif

	public void ListenForGameEvent(ReadOnlySpan<char> name) {
		RegisteredForEvents = true;
#if CLIENT_DLL
		bool serverSide = false;
#else
		bool serverSide = true;
#endif
		gameeventmanager?.AddListener(this, name, serverSide);
	}

#if CLIENT_DLL
	public override void LevelInitPreEntity() {
		GarrysMod.Lua.Create();
		if (g_Lua != null && gGM != null) {
			gGM.LoadGamemode(Gamemode, false);
			gGM.SetGamemode(Gamemode, true);
		}
		RecacheMaterials();
	}

	static void RecacheMaterials() {
		materials.UncacheAllMaterials();
		materials.CacheUsedMaterials();
	}
	public override void Update(double frametime) {
		// RemoveQueuedRenderTargets();
		LuaEntity.RemoveQueuedEntities();
		// RunMapCleanupIfNeeded();
		if (g_Lua == null || GarrysMod.g_LuaManager == null || gGM == null)
			return;

		if (!engine.IsPaused() && C_BasePlayer.GetLocalPlayer() != null)
			gGM.Call((int)LUA_POOLEDSTRING.Think);

		if (gpGlobals.TickCount == LastTick)
			return;

		if (engine.GetNetChannelInfo() != null && engine.GetNetChannelInfo()!.IsTimingOut())
			garrysmod.Think();
		LastTick = gpGlobals.TickCount;
		gGM.Call((int)LUA_POOLEDSTRING.Tick);
	}
#else
	public override void FrameUpdatePreEntityThink() {
		if (gGM == null)
			return;

		mdlcache.BeginLock();
		if (gpGlobals.CurTime != LastThink) {
			gGM.Call((int)LUA_POOLEDSTRING.Think);
			LastThink = gpGlobals.CurTime;
		}
		garrysmod.Think();
		// RemoveQueuedRecipientFilters();
		LuaEntity.RemoveQueuedEntities();
		// RunMapCleanupIfNeeded();
		if (gpGlobals.TickCount != LastTick) {
			LastTick = gpGlobals.TickCount;
			gGM.Call((int)LUA_POOLEDSTRING.Tick);
		}
		mdlcache.EndLock();
	}
#endif

	static double LastThink;
	static long LastTick;

	public override void LevelInitPostEntity() {
		gGM?.Call((int)LUA_POOLEDSTRING.InitPostEntity);
	}

	public void StopListeningForAllEvents() {
		if (RegisteredForEvents) {
			gameeventmanager?.RemoveListener(this);
			RegisteredForEvents = false;
		}
	}

	public override ReadOnlySpan<char> Name() => "LuaGameSystem";

	public override bool Init() {
#if CLIENT_DLL
		Gamemode = "sandbox";
		ListenForGameEvent("user_data_downloaded");
		ListenForGameEvent("server_spawn");
#endif
		ListenForGameEvent("player_death");
		ListenForGameEvent("player_connect");
		ListenForGameEvent("player_activate");
		ListenForGameEvent("break_prop");
		return true;
	}

	// todo LevelShutdownPostEntity

	public void FireGameEvent(IGameEvent ev) {
#if CLIENT_DLL
		if (strcmp(ev.GetName(), "server_spawn") == 0) {
			Gamemode = new(ev.GetString("gamemode", ""));
			filesystem.Gamemodes().SetActive(Gamemode);
		}
#endif
		if (strcmp(ev.GetName(), "player_connect") == 0) {
			if (g_Lua != null && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerConnect)) {
				g_Lua.PushString(ev.GetString("name", ""));
#if CLIENT_DLL
				gGM.CallFinish(1);
#else
				g_Lua.PushString(ev.GetString("address", ""));
				gGM.CallFinish(2);
#endif
			}
		}
		else if (strcmp(ev.GetName(), "break_prop") == 0) {
			if (g_Lua != null && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PropBreak)) {
				BasePlayer? player = null;
				if (ev.GetInt("userid", 0) > 0)
#if CLIENT_DLL
					player = ClientModeShared.USERID2PLAYER(ev.GetInt("userid", 0));
#else
					player = Util.PlayerByUserId(ev.GetInt("userid", 0));
#endif
				LuaEntity.Push_Entity(player);
				BaseEntity? entity = null;
				if (ev.GetInt("entindex", 0) > 0)
#if CLIENT_DLL
					entity = cl_entitylist.GetBaseEntity(ev.GetInt("entindex", 0));
#else
					entity = LuaEntity.GMEntityByIndex(ev.GetInt("entindex", 0));
#endif
				LuaEntity.Push_Entity(entity);
				gGM.CallFinish(2);
			}
		}
#if CLIENT_DLL
		else if (strcmp(ev.GetName(), "user_data_downloaded") == 0 && !UserDataDownloaded) {
			UserDataDownloaded = true;
			BaseAchievement? achievement = (BaseAchievement?)engine.GetAchievementMgr()!.GetAchievementByID((int)GMODAchievementID.GMA_X_STARTUPS);
			achievement?.IncrementCount();
		}
#endif
	}
}
#endif
