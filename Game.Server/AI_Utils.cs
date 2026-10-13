global using static Game.Server.AI_UtilsGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.Formats.BSP;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

public static class AI_UtilsGlobals
{
	public static string? g_iszFuncBrushClassname;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static BasePlayer? AI_GetSinglePlayer() {
		if (gpGlobals.MaxClients > 1)
			return null;

		return Util.GetLocalPlayer();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool AI_IsSinglePlayer() => gpGlobals.MaxClients == 1;
}

public class AI_MoveMonitor
{
	const float NO_MARK = -1;

	public AI_MoveMonitor() {
		Mark = Vector3.Zero;
		MarkTolerance = NO_MARK;
	}

	public void SetMark(BaseEntity? entity, float tolerance) {
		if (entity != null) {
			Mark = entity.GetAbsOrigin();
			MarkTolerance = tolerance;
		}
	}

	public void ClearMark() => MarkTolerance = NO_MARK;

	public bool IsMarkSet() => MarkTolerance != NO_MARK;

	public bool TargetMoved(BaseEntity? entity) {
		if (IsMarkSet() && entity != null) {
			float distance = (Mark - entity.GetAbsOrigin()).Length();
			if (distance > MarkTolerance)
				return true;
		}
		return false;
	}

	public bool TargetMoved2D(BaseEntity? entity) {
		if (IsMarkSet() && entity != null) {
			Vector3 origin = entity.GetAbsOrigin();
			float distance = new Vector2(Mark.X - origin.X, Mark.Y - origin.Y).Length();
			if (distance > MarkTolerance)
				return true;
		}
		return false;
	}

	public Vector3 GetMarkPos() => Mark;

	Vector3 Mark;
	float MarkTolerance;
}

public class AI_ShotRegulator
{
	public AI_ShotRegulator() {
		MinBurstShots = 1;
		MaxBurstShots = 1;
		MinRestInterval = 0.0f;
		MaxRestInterval = 0.0f;
		MinBurstInterval = 0.0f;
		MaxBurstInterval = 0.0f;
		NextShotTimeValue = -1;
		BurstShotsRemaining = 1;
		InRestInterval = false;
		Disabled = false;
	}

	public void SetParameters(int minShotsPerBurst, int maxShotsPerBurst, float minRestTime, float maxRestTime = 0.0f) {
		SetBurstShotCountRange(minShotsPerBurst, maxShotsPerBurst);
		SetRestInterval(minRestTime, maxRestTime);
		Reset(false);
	}

	public void SetBurstShotCountRange(int minShotsPerBurst, int maxShotsPerBurst) {
		MinBurstShots = (ushort)minShotsPerBurst;
		MaxBurstShots = (ushort)maxShotsPerBurst;
	}

	public void SetRestInterval(float minRestInterval, float maxRestInterval) {
		MinRestInterval = minRestInterval;
		MaxRestInterval = maxRestInterval;
	}

	public void SetBurstInterval(float minBurstInterval, float maxBurstInterval) {
		MinBurstInterval = minBurstInterval;
		MaxBurstInterval = maxBurstInterval;
	}

	public void GetBurstShotCountRange(out int minShotsPerBurst, out int maxShotsPerBurst) {
		minShotsPerBurst = MinBurstShots;
		maxShotsPerBurst = MaxBurstShots;
	}

	public void GetRestInterval(out float minRestInterval, out float maxRestInterval) {
		minRestInterval = MinRestInterval;
		maxRestInterval = MaxRestInterval;
	}

	public void GetBurstInterval(out float minBurstInterval, out float maxBurstInterval) {
		minBurstInterval = MinBurstInterval;
		maxBurstInterval = MaxBurstInterval;
	}

	public void Reset(bool startShooting = true) {
		Disabled = false;
		BurstShotsRemaining = (ushort)random.RandomInt(MinBurstShots, MaxBurstShots);
		if (startShooting) {
			NextShotTimeValue = gpGlobals.CurTime;
			InRestInterval = false;
		}
		else {
			NextShotTimeValue = gpGlobals.CurTime + random.RandomFloat(MinRestInterval, MaxRestInterval);
			InRestInterval = true;
		}
	}

	public bool ShouldShoot() => !Disabled && NextShotTimeValue <= gpGlobals.CurTime;

	public bool IsInRestInterval() => InRestInterval && !ShouldShoot();

	public TimeUnit_t NextShotTime() => NextShotTimeValue;

	public void FireNoEarlierThan(TimeUnit_t time) {
		if (time > NextShotTimeValue)
			NextShotTimeValue = time;
	}

	public int GetBurstShotsRemaining() => BurstShotsRemaining;

	public void SetBurstShotsRemaining(int shots) => BurstShotsRemaining = (ushort)shots;

	public void OnFiredWeapon() {
		--BurstShotsRemaining;
		if (BurstShotsRemaining <= 0)
			Reset(false);
		else {
			InRestInterval = false;
			NextShotTimeValue += random.RandomFloat(MinBurstInterval, MaxBurstInterval);
			if (NextShotTimeValue < gpGlobals.CurTime)
				NextShotTimeValue = gpGlobals.CurTime;
		}
	}

	public void EnableShooting() => Disabled = false;

	public void DisableShooting() => Disabled = true;

	TimeUnit_t NextShotTimeValue;
	bool InRestInterval;
	ushort BurstShotsRemaining;
	ushort MinBurstShots, MaxBurstShots;
	float MinRestInterval, MaxRestInterval;
	float MinBurstInterval, MaxBurstInterval;
	bool Disabled;
}

public struct TraceFilterNav : ITraceFilter
{
	public TraceFilterNav(AI_BaseNPC prober, bool ignoreTransientEntities, IHandleEntity? passedict, CollisionGroup collisionGroup, bool allowPlayerAvoid = true) {
		Simple = new(passedict, collisionGroup);
		Prober = prober;
		IgnoreTransientEntities = ignoreTransientEntities;
		AllowPlayerAvoid = allowPlayerAvoid;
		CheckCollisionTable = g_EntityCollisionHash.IsObjectInHash(prober);
	}

	public bool ShouldHitEntity(IHandleEntity handleEntity, Contents contentsMask) {
		BaseEntity? entity = EntityFromEntityHandle(handleEntity);

		if (Prober == entity)
			return false;

		if (Prober.GetMoveProbe()!.ShouldBrushBeIgnored(entity!) == true)
			return false;

		if (IgnoreTransientEntities && (entity!.IsPlayer() || entity.IsNPC()))
			return false;

		if (AllowPlayerAvoid && Prober.ShouldPlayerAvoid() && entity!.IsPlayer())
			return false;

		if (entity!.IsNavIgnored())
			return false;

		if (CheckCollisionTable) {
			if (g_EntityCollisionHash.IsObjectPairInHash(Prober, entity))
				return false;
		}

		if (Prober.ShouldProbeCollideAgainstEntity(entity) == false)
			return false;

		return Simple.ShouldHitEntity(handleEntity, contentsMask);
	}

	TraceFilterSimple Simple;
	readonly AI_BaseNPC Prober;
	readonly bool IgnoreTransientEntities;
	readonly bool CheckCollisionTable;
	readonly bool AllowPlayerAvoid;
}
