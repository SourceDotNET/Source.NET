using Game.Shared;

using Source.Common;
using Source.Common.GarrysMod.Lua;

using Steamworks;

#if MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Client.GarrysMod;
#endif

public static partial class LuaAchievements
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_achievements = new("achievements");

	static bool UnsetIncrementFlag;

	static IAchievement? GetAchievement(int index) {
		IAchievementMgr? mgr = engine.GetAchievementMgr();
		if (mgr == null || (uint)index >= (uint)mgr.GetAchievementCount())
			return null;
		return mgr.GetAchievementByIndex(index);
	}

	static void IncrementAchievement(GMODAchievementID id) {
		if (engine.GetAchievementMgr()!.GetAchievementByID((int)id) is BaseAchievement achievement)
			achievement.IncrementCount(0);
	}

	[LuaFunction]
	static int Count(ILuaInterface lua) {
		IAchievementMgr? mgr = engine.GetAchievementMgr();
		if (mgr == null)
			return 0;
		lua.PushNumber(mgr.GetAchievementCount());
		return 1;
	}

	static int PushDisplayAttribute(ILuaInterface lua, ReadOnlySpan<char> key) {
		IAchievement? achievement = GetAchievement((int)lua.GetNumber(1));
		if (achievement == null)
			return 0;

		ReadOnlySpan<char> name = achievement.GetName();
		if (name.IsEmpty)
			return 0;

		string? value = SteamUserStats.GetAchievementDisplayAttribute(new(name), new(key));
		if (value == null)
			return 0;

		lua.PushString(value);
		return 1;
	}

	[LuaFunction]
	static int GetName(ILuaInterface lua) => PushDisplayAttribute(lua, "name");

	[LuaFunction]
	static int GetDesc(ILuaInterface lua) => PushDisplayAttribute(lua, "desc");

	[LuaFunction]
	static int GetGoal(ILuaInterface lua) {
		IAchievement? achievement = GetAchievement((int)lua.GetNumber(1));
		if (achievement == null)
			return 0;
		lua.PushNumber(achievement.GetGoal());
		return 1;
	}

	[LuaFunction]
	static int GetCount(ILuaInterface lua) {
		IAchievement? achievement = GetAchievement((int)lua.GetNumber(1));
		if (achievement == null)
			return 0;
		lua.PushNumber(achievement.GetCount());
		return 1;
	}

	[LuaFunction]
	static int IsAchieved(ILuaInterface lua) {
		IAchievement? achievement = GetAchievement((int)lua.GetNumber(1));
		if (achievement == null)
			return 0;
		lua.PushBool(achievement.IsAchieved());
		return 1;
	}

	static int IncrementFromNetMessage(GMODAchievementID id) {
		if (UnsetIncrementFlag || GarrysMod.RunningNetMessage) {
			IncrementAchievement(id);
			UnsetIncrementFlag = false;
			GarrysMod.RunningNetMessage = false;
		}
		return 0;
	}

	static int IncrementFromLuaCmd(GMODAchievementID id) {
		if (!GarrysMod.RunningLuaCmd)
			return 0;
		IncrementAchievement(id);
		GarrysMod.RunningLuaCmd = false;
		return 0;
	}

	[LuaFunction] static int IncBaddies(ILuaInterface lua) => IncrementFromNetMessage(GMODAchievementID.GMA_BADDIES);
	[LuaFunction] static int IncGoodies(ILuaInterface lua) => IncrementFromNetMessage(GMODAchievementID.GMA_GOODIES);
	[LuaFunction] static int IncBystander(ILuaInterface lua) => IncrementFromNetMessage(GMODAchievementID.GMA_BYSTANDER);
	[LuaFunction] static int EatBall(ILuaInterface lua) => IncrementFromLuaCmd(GMODAchievementID.GMA_BALLEATER);
	[LuaFunction] static int SpawnedProp(ILuaInterface lua) => IncrementFromLuaCmd(GMODAchievementID.GMA_PROPSPAWNER);
	[LuaFunction] static int SpawnedNPC(ILuaInterface lua) => IncrementFromLuaCmd(GMODAchievementID.GMA_NPCSPAWNER);
	[LuaFunction] static int SpawnedRagdoll(ILuaInterface lua) => IncrementFromLuaCmd(GMODAchievementID.GMA_RAGDOLLSPAWNER);

	[LuaFunction]
	static int SpawnMenuOpen(ILuaInterface lua) {
		IncrementAchievement(GMODAchievementID.GMA_SPAWNMENUER);
		return 0;
	}

	[LuaFunction] static int BalloonPopped(ILuaInterface lua) => IncrementFromLuaCmd(GMODAchievementID.GMA_BALLOONPOPPER);
	[LuaFunction] static int Remover(ILuaInterface lua) => IncrementFromLuaCmd(GMODAchievementID.GMA_REMOVER);
}
