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

// 	public override void LevelInitPreEntity() => throw new NotImplementedException();
// 	public override void LevelInitPostEntity() => throw new NotImplementedException();
// 	public override void LevelShutdownPreEntity() => throw new NotImplementedException();
// 	public override void LevelShutdownPostEntity() => throw new NotImplementedException();
#if CLIENT_DLL
// 	public override void Update(TimeUnit_t frametime) => throw new NotImplementedException();
#else
// 	public override void FrameUpdatePreEntityThink() => throw new NotImplementedException();
#endif

	public void FireGameEvent(IGameEvent ev) {
#if CLIENT_DLL
		if (strcmp(ev.GetName(), "server_spawn") == 0) {
			Gamemode = new(ev.GetString("gamemode", ""));
			// todo
		}
		else
#endif
		if (strcmp(ev.GetName(), "player_connect") == 0) {
			// todo
		}
		else if (strcmp(ev.GetName(), "break_prop") == 0) {
			// todo
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
