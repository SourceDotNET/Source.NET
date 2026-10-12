global using static Game.Server.AI_MemoryGlobals;

using Game.Shared;

using Source;

using System.Numerics;

namespace Game.Server;

public static class AI_MemoryGlobals
{
	public const float AI_DEF_ENEMY_DISCARD_TIME = 60.0f;
	public const TimeUnit_t AI_INVALID_TIME = -float.MaxValue;
	public const float AI_FREE_KNOWLEDGE_DURATION = 1.75f;
}

public class AI_EnemyInfo
{
	public AI_EnemyInfo() {
		Enemy.Set(null);
		LastKnownLocation = vec3_origin;
		LastSeenLocation = vec3_origin;
		TimeLastSeen = 0;
		TimeFirstSeen = 0;
		TimeLastReacquired = 0;
		TimeValidEnemy = 0;
		TimeLastReceivedDamageFrom = 0;
		TimeAtFirstHand = AI_INVALID_TIME;
		DangerMemory = false;
		EludedMe = false;
		Unforgettable = false;
		MobbedMe = false;
	}

	public EHANDLE Enemy = new();

	public Vector3 LastKnownLocation;
	public Vector3 LastSeenLocation;
	public TimeUnit_t TimeLastSeen;
	public TimeUnit_t TimeFirstSeen;
	public TimeUnit_t TimeLastReacquired;
	public TimeUnit_t TimeValidEnemy;
	public TimeUnit_t TimeLastReceivedDamageFrom;
	public TimeUnit_t TimeAtFirstHand;
	public bool DangerMemory;
	public bool EludedMe;
	public bool Unforgettable;
	public bool MobbedMe;
}

public class AI_Enemies
{
	readonly record struct MemoryEntry(BaseEntity? Key, AI_EnemyInfo Info);

	public AI_Enemies() {
		FreeKnowledgeDuration = AI_FREE_KNOWLEDGE_DURATION;
		EnemyDiscardTime = AI_DEF_ENEMY_DISCARD_TIME;
		DefaultLKP = vec3_invalid;
		DefaultLSP = vec3_invalid;
		Serial = 0;
	}

	public AI_EnemyInfo? GetFirst(out int iter) {
		iter = 0;

		if (iter >= Map.Count)
			return null;

		if (Map[iter].Info.Enemy.Get() == null)
			return GetNext(ref iter);

		return Map[iter].Info;
	}

	public AI_EnemyInfo? GetNext(ref int iter) {
		if (iter >= Map.Count)
			return null;

		iter++;
		if (iter >= Map.Count)
			return null;

		if (Map[iter].Info.Enemy.Get() == null)
			return GetNext(ref iter);

		return Map[iter].Info;
	}

	int FindIndex(BaseEntity? entity) {
		for (int i = 0; i < Map.Count; i++) {
			if (Map[i].Key == entity)
				return i;
		}
		return -1;
	}

	public AI_EnemyInfo? Find(BaseEntity? entity, bool tryDangerMemory = false) {
		int i = FindIndex(entity);
		if (i == -1) {
			if (!tryDangerMemory || (i = FindIndex(null)) == -1)
				return null;
			Assert(Map[i].Info.DangerMemory == true);
		}
		return Map[i].Info;
	}

	public AI_EnemyInfo? GetDangerMemory() {
		int i = FindIndex(null);
		if (i == -1)
			return null;
		Assert(Map[i].Info.DangerMemory == true);
		return Map[i].Info;
	}

	public int NumEnemies() => Map.Count;
	public int GetSerialNumber() => Serial;

	bool ShouldDiscardMemory(AI_EnemyInfo memory) {
		BaseEntity? enemy = memory.Enemy.Get();

		if (enemy != null) {
			AI_BaseNPC? enemyNPC = enemy.MyNPCPointer();
			if (enemyNPC != null && enemyNPC.GetState() == NPCState.Dead)
				return true;
		}
		else {
			if (!memory.DangerMemory)
				return true;
		}

		if (!memory.Unforgettable &&
			 gpGlobals.CurTime > memory.TimeLastSeen + EnemyDiscardTime)
			return true;

		return false;
	}

	public void RefreshMemories() {
		if (FreeKnowledgeDuration >= EnemyDiscardTime)
			FreeKnowledgeDuration = EnemyDiscardTime - .1f;

		int i = 0;
		while (i < Map.Count) {
			AI_EnemyInfo memory = Map[i].Info;

			if (ShouldDiscardMemory(memory)) {
				Map.RemoveAt(i);
				continue;
			}
			else {
				BaseEntity? enemy = memory.Enemy.Get();
				if (enemy != null) {
					if (!enemy.IsPlayer() || !g_IgnorePlayers.GetBool()) {
						if (gpGlobals.CurTime <= memory.TimeLastSeen + FreeKnowledgeDuration) {
							if ((enemy.GetFlags() & EntityFlags.NoTarget) == 0)
								memory.LastKnownLocation = enemy.GetAbsOrigin();
						}
					}

					if (gpGlobals.CurTime <= memory.TimeLastSeen)
						memory.LastSeenLocation = enemy.GetAbsOrigin();
				}
			}
			i++;
		}
	}

	public bool UpdateMemory(AI_Network? aiNet, BaseEntity? enemy, in Vector3 position, float reactionDelay, bool firstHand) {
		const float DIST_TRIGGER_REACQUIRE_SQ = (20.0f * 12.0f) * (20.0f * 12.0f);
		const float TIME_TRIGGER_REACQUIRE = 4.0f;
		const float MIN_DIST_TIME_TRIGGER_REACQUIRE_SQ = (4.0f * 12.0f) * (4.0f * 12.0f);

		AI_EnemyInfo? memory = Find(enemy);
		if (memory != null) {
			Assert(enemy != null || memory.DangerMemory == true);

			if (firstHand)
				memory.TimeLastSeen = gpGlobals.CurTime;
			memory.EludedMe = false;

			float deltaDist = (memory.LastKnownLocation - position).LengthSquared();

			if (deltaDist > DIST_TRIGGER_REACQUIRE_SQ || (deltaDist > MIN_DIST_TIME_TRIGGER_REACQUIRE_SQ && (gpGlobals.CurTime - memory.TimeLastSeen) > TIME_TRIGGER_REACQUIRE))
				memory.TimeLastReacquired = gpGlobals.CurTime;

			if (deltaDist > 12.0f * 12.0f)
				memory.LastKnownLocation = position;

			if (firstHand && memory.TimeAtFirstHand == AI_INVALID_TIME)
				memory.TimeAtFirstHand = gpGlobals.CurTime;

			return false;
		}

		AI_EnemyInfo addMemory = new();
		addMemory.LastKnownLocation = position;

		if (firstHand)
			addMemory.TimeLastReacquired = addMemory.TimeFirstSeen = addMemory.TimeLastSeen = addMemory.TimeAtFirstHand = gpGlobals.CurTime;
		else {
			addMemory.TimeLastReacquired = addMemory.TimeFirstSeen = addMemory.TimeLastSeen = gpGlobals.CurTime - (FreeKnowledgeDuration + 0.01);
			addMemory.TimeAtFirstHand = AI_INVALID_TIME;
		}

		if (reactionDelay > 0.0)
			addMemory.TimeValidEnemy = gpGlobals.CurTime + reactionDelay;

		addMemory.EludedMe = false;

		addMemory.Enemy.Set(enemy);
		addMemory.DangerMemory = enemy == null;

		Map.Add(new(enemy, addMemory));
		Serial++;

		return true;
	}

	public void OnTookDamageFrom(BaseEntity? enemy) {
		AI_EnemyInfo? memory = Find(enemy, true);
		if (memory != null)
			memory.TimeLastReceivedDamageFrom = gpGlobals.CurTime;
	}

	public bool HasMemory(BaseEntity? enemy) => Find(enemy) != null;

	public void ClearMemory(BaseEntity? enemy) {
		int i = FindIndex(enemy);
		if (i != -1)
			Map.RemoveAt(i);
	}

	public void MarkAsEluded(BaseEntity? enemy) {
		AI_EnemyInfo? memory = Find(enemy);
		if (memory != null)
			memory.EludedMe = true;
	}

	public Vector3 LastKnownPosition(BaseEntity? enemy) {
		AI_EnemyInfo? memory = Find(enemy, true);
		if (memory != null)
			DefaultLKP = memory.LastKnownLocation;
		else
			DevWarning(2, "Asking LastKnownPosition for enemy that's not in my memory!!\n");
		return DefaultLKP;
	}

	public Vector3 LastSeenPosition(BaseEntity? enemy) {
		AI_EnemyInfo? memory = Find(enemy, true);
		if (memory != null)
			DefaultLSP = memory.LastSeenLocation;
		else
			DevWarning(2, "Asking LastSeenPosition for enemy that's not in my memory!!\n");
		return DefaultLSP;
	}

	public TimeUnit_t TimeLastReacquired(BaseEntity? enemy) {
		if (enemy == null)
			return 0;

		AI_EnemyInfo? memory = Find(enemy, true);
		if (memory != null)
			return memory.TimeLastReacquired;

		DevWarning(2, "Asking TimeLastReacquired for enemy that's not in my memory!!\n");
		return AI_INVALID_TIME;
	}

	public TimeUnit_t LastTimeSeen(BaseEntity? enemy, bool checkDangerMemory = true) {
		if (enemy == null)
			return 0;

		AI_EnemyInfo? memory = Find(enemy, checkDangerMemory);
		if (memory != null)
			return memory.TimeLastSeen;

		DevWarning(2, "Asking LastTimeSeen for enemy that's not in my memory!!\n");
		return AI_INVALID_TIME;
	}

	public TimeUnit_t FirstTimeSeen(BaseEntity? enemy) {
		if (enemy == null)
			return 0;

		AI_EnemyInfo? memory = Find(enemy, true);
		if (memory != null)
			return memory.TimeFirstSeen;

		DevWarning(2, "Asking FirstTimeSeen for enemy that's not in my memory!!\n");
		return AI_INVALID_TIME;
	}

	public bool HasFreeKnowledgeOf(BaseEntity? enemy) {
		if (enemy == null)
			return false;

		AI_EnemyInfo? memory = Find(enemy, true);
		if (memory != null) {
			TimeUnit_t freeKnowledgeTime = memory.TimeLastSeen + FreeKnowledgeDuration;
			return gpGlobals.CurTime < freeKnowledgeTime;
		}

		DevWarning(2, "Asking HasFreeKnowledgeOf for enemy that's not in my memory!!\n");
		return AI_INVALID_TIME != 0;
	}

	public TimeUnit_t LastTimeTookDamageFrom(BaseEntity? enemy) {
		if (enemy == null)
			return 0;

		AI_EnemyInfo? memory = Find(enemy, true);
		if (memory != null)
			return memory.TimeLastReceivedDamageFrom;

		DevWarning(2, "Asking LastTimeTookDamageFrom for enemy that's not in my memory!!\n");
		return AI_INVALID_TIME;
	}

	public TimeUnit_t TimeAtFirstHand(BaseEntity? enemy) {
		if (enemy == null)
			return 0;

		AI_EnemyInfo? memory = Find(enemy, true);
		if (memory != null)
			return memory.TimeAtFirstHand;

		DevWarning(2, "Asking TimeAtFirstHand for enemy that's not in my memory!!\n");
		return AI_INVALID_TIME;
	}

	public bool HasEludedMe(BaseEntity? enemy) {
		AI_EnemyInfo? memory = Find(enemy);
		if (memory != null)
			return memory.EludedMe;
		return false;
	}

	public void SetTimeValidEnemy(BaseEntity? enemy, TimeUnit_t time) {
		AI_EnemyInfo? memory = Find(enemy);
		if (memory != null)
			memory.TimeValidEnemy = time;
	}

	public void SetUnforgettable(BaseEntity? enemy, bool unforgettable = true) {
		AI_EnemyInfo? memory = Find(enemy);
		if (memory != null)
			memory.Unforgettable = unforgettable;
	}

	public void SetMobbedMe(BaseEntity? enemy, bool mobbedMe = true) {
		AI_EnemyInfo? memory = Find(enemy);
		if (memory != null)
			memory.MobbedMe = mobbedMe;
	}

	public void SetFreeKnowledgeDuration(float duration) {
		FreeKnowledgeDuration = duration;

		if (FreeKnowledgeDuration >= EnemyDiscardTime) {
			Assert(FreeKnowledgeDuration < EnemyDiscardTime);

			FreeKnowledgeDuration = EnemyDiscardTime - .1f;
		}
	}

	public void SetEnemyDiscardTime(float time) {
		EnemyDiscardTime = time;

		if (FreeKnowledgeDuration >= EnemyDiscardTime) {
			Assert(FreeKnowledgeDuration < EnemyDiscardTime);

			FreeKnowledgeDuration = EnemyDiscardTime - .1f;
		}
	}

	public float GetEnemyDiscardTime() => EnemyDiscardTime;

	readonly List<MemoryEntry> Map = [];
	float FreeKnowledgeDuration;
	float EnemyDiscardTime;
	Vector3 DefaultLKP;
	Vector3 DefaultLSP;
	int Serial;
}
