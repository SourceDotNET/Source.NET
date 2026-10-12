using Game.Shared;

using System.Numerics;

namespace Game.Server;

public class AI_MoveAndShootOverlay : AI_Component
{
	const TimeUnit_t MOVESHOOT_DO_NOT_SUSPEND = -1.0;

	public AI_MoveAndShootOverlay() {
		MovingAndShooting = false;
		InitialDelay = 0;
		SuspendUntilTime = MOVESHOOT_DO_NOT_SUSPEND;
		NoShootWhileMoveValue = false;
	}

	public void NoShootWhileMove() => NoShootWhileMoveValue = true;

	public bool HasAvailableRangeAttack() {
		return GetOuter()!.GetActiveWeapon() != null ||
				(GetOuter()!.CapabilitiesGet() & Capability.InnateRangeAttack1) != 0 ||
				(GetOuter()!.CapabilitiesGet() & Capability.InnateRangeAttack2) != 0;
	}

	public void StartShootWhileMove() {
		if (GetOuter()!.GetState() == NPCState.Script ||
			 !HasAvailableRangeAttack() ||
			 !GetOuter()!.HaveSequenceForActivity(GetOuter()!.TranslateActivity(Activity.ACT_WALK_AIM, out _)) ||
			 !GetOuter()!.HaveSequenceForActivity(GetOuter()!.TranslateActivity(Activity.ACT_RUN_AIM, out _))) {
			NoShootWhileMove();
			return;
		}

		GetOuter()!.GetShotRegulator().FireNoEarlierThan(gpGlobals.CurTime + InitialDelay);
		NoShootWhileMoveValue = false;
	}

	bool CanAimAtEnemy() {
		AI_BaseNPC outer = GetOuter()!;
		bool result = false;
		bool resetConditions = false;
		AI_ScheduleBits savedConditions = default;

		if (!GetOuter()!.ConditionsGathered()) {
			savedConditions = GetOuter()!.Conditions;
			GetOuter()!.GatherEnemyConditions(GetOuter()!.GetEnemy()!);
		}

		if (outer.HasCondition((int)SCOND_t.COND_CAN_RANGE_ATTACK1))
			result = true;
		else if (!outer.HasCondition((int)SCOND_t.COND_ENEMY_DEAD) &&
				  !outer.HasCondition((int)SCOND_t.COND_TOO_FAR_TO_ATTACK) &&
				  !outer.HasCondition((int)SCOND_t.COND_ENEMY_TOO_FAR) &&
				  !outer.HasCondition((int)SCOND_t.COND_ENEMY_OCCLUDED))
			result = true;

		if (!HasAvailableRangeAttack())
			result = false;

		if (resetConditions)
			GetOuter()!.Conditions = savedConditions;

		return result;
	}

	void UpdateMoveShootActivity(bool moveAimAtEnemy) {
		Activity curActivity = GetOuter()!.GetNavigator()!.GetMovementActivity();
		Activity newActivity = curActivity;

		if (moveAimAtEnemy) {
			switch (curActivity) {
				case Activity.ACT_WALK:
					newActivity = Activity.ACT_WALK_AIM;
					break;
				case Activity.ACT_RUN:
					newActivity = Activity.ACT_RUN_AIM;
					break;
			}
		}
		else {
			switch (curActivity) {
				case Activity.ACT_WALK_AIM:
					newActivity = Activity.ACT_WALK;
					break;
				case Activity.ACT_RUN_AIM:
					newActivity = Activity.ACT_RUN;
					break;
			}
		}

		if (curActivity != newActivity) {
			GetOuter()!.GetShotRegulator().FireNoEarlierThan(gpGlobals.CurTime + 0.3f);
			GetOuter()!.GetNavigator()!.SetMovementActivity(newActivity);
		}
	}

	public void RunShootWhileMove() {
		if (NoShootWhileMoveValue)
			return;

		if (gpGlobals.CurTime < SuspendUntilTime)
			return;

		SuspendUntilTime = MOVESHOOT_DO_NOT_SUSPEND;

		AI_BaseNPC outer = GetOuter()!;

		if (outer.GetEnemy() == null || !outer.GetEnemy()!.IsAlive()) {
			BaseEntity? newEnemy = outer.BestEnemy();

			if (newEnemy != null) {
				outer.SetEnemy(newEnemy);
				outer.SetState(NPCState.Combat);
			}
			else
				outer.ClearAttackConditions();
		}

		if (!outer.GetNavigator()!.IsGoalActive())
			return;

		if (outer.GetEnemy() == null) {
			if (outer.GetAlternateMoveShootTarget() != null)
				outer.AddFacingTarget(outer.GetAlternateMoveShootTarget(), outer.GetAlternateMoveShootTarget()!.GetAbsOrigin(), 1.0f, 0.2f);

			return;
		}

		bool moveAimAtEnemy = CanAimAtEnemy();
		UpdateMoveShootActivity(moveAimAtEnemy);
		if (!moveAimAtEnemy) {
			EndShootWhileMove();
			return;
		}

		Assert(HasAvailableRangeAttack());

		Activity activity;
		bool isReloading = false;

		if ((activity = outer.TranslateActivity(Activity.ACT_GESTURE_RELOAD)) != Activity.ACT_INVALID)
			isReloading = outer.IsPlayingGesture(activity);

		if (!isReloading && HasAvailableRangeAttack()) {
			if (outer.HasCondition((int)SCOND_t.COND_CAN_RANGE_ATTACK1, false)) {
				if (outer.GetShotRegulator().IsInRestInterval())
					EndShootWhileMove();
				else if (outer.GetShotRegulator().ShouldShoot()) {
					if (MovingAndShooting || outer.OnBeginMoveAndShoot()) {
						MovingAndShooting = true;
						outer.OnRangeAttack1();

						activity = outer.TranslateActivity(Activity.ACT_GESTURE_RANGE_ATTACK1);
						Assert(activity != Activity.ACT_INVALID);

						outer.RestartGesture(activity);

						bool identical = false;
						outer.Weapon_SetActivity(outer.Weapon_TranslateActivity(Activity.ACT_RANGE_ATTACK1, ref identical), 0);
					}
				}
			}
			else if (outer.HasCondition((int)SCOND_t.COND_NO_PRIMARY_AMMO, false)) {
				if (outer.GetNavigator()!.GetPathTimeToGoal() > 1.0) {
					activity = outer.TranslateActivity(Activity.ACT_GESTURE_RELOAD);
					if (activity != Activity.ACT_INVALID && GetOuter()!.HaveSequenceForActivity(activity))
						outer.AddGesture(activity);
				}
			}
		}

		Vector3 enemyLKP = outer.GetEnemyLKP();
		outer.AddFacingTarget(outer.GetEnemy(), enemyLKP, 1.0f, 0.8f);
	}

	public void EndShootWhileMove() {
		if (MovingAndShooting) {
			if (!GetOuter()!.GetShotRegulator().IsInRestInterval())
				GetOuter()!.GetShotRegulator().Reset(false);

			MovingAndShooting = false;
			GetOuter()!.OnEndMoveAndShoot();
		}
	}

	public void SuspendMoveAndShoot(float duration) {
		EndShootWhileMove();
		SuspendUntilTime = gpGlobals.CurTime + duration;
	}

	public void SetInitialDelay(float delay) => InitialDelay = delay;

	public bool IsMovingAndShooting() => MovingAndShooting;

	bool MovingAndShooting;
	bool NoShootWhileMoveValue;
	float InitialDelay;
	TimeUnit_t SuspendUntilTime;
}
