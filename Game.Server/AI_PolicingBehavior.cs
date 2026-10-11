using Game.Shared;

using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

public class AI_PolicingBehavior : AI_Behavior_AI_BaseNPC_100000
{
	const float PATROL_RADIUS_RATIO = 2.0f;
	const int POLICE_MAX_WARNINGS = 4;

	public const int SCHED_POLICE_RETURN_FROM_HARASS = NEXT_SCHEDULE;
	public const int SCHED_POLICE_WARN_TARGET = NEXT_SCHEDULE + 1;
	public const int SCHED_POLICE_HARASS_TARGET = NEXT_SCHEDULE + 2;
	public const int SCHED_POLICE_SUPPRESS_TARGET = NEXT_SCHEDULE + 3;
	public const int SCHED_POLICE_FACE_ALONG_GOAL = NEXT_SCHEDULE + 4;
	public const int SCHED_POLICE_TRACK_TARGET = NEXT_SCHEDULE + 5;

	public const int TASK_POLICE_GET_PATH_TO_HARASS_GOAL = NEXT_TASK;
	public const int TASK_POLICE_GET_PATH_TO_POLICE_GOAL = NEXT_TASK + 1;
	public const int TASK_POLICE_FACE_ALONG_GOAL = NEXT_TASK + 2;
	public const int TASK_POLICE_ANNOUNCE_HARASS = NEXT_TASK + 3;

	public const int COND_POLICE_TARGET_TOO_CLOSE_HARASS = NEXT_CONDITION;
	public const int COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS = NEXT_CONDITION + 1;

	static readonly AI_ClassScheduleIdSpace gm_ClassScheduleIdSpace = new();
	const string gm_pszErrorClassName = "CAI_PolicingBehavior";

	static readonly string[] Warnings = [
		"METROPOLICE_MOVE_ALONG_A",
		"METROPOLICE_MOVE_ALONG_B",
		"METROPOLICE_MOVE_ALONG_C",
	];

	public AI_PolicingBehavior() {
		LoadSchedules();

		Enabled = false;
		NumWarnings = 0;
		TargetIsHostileValue = false;
	}

	public override string GetName() => "Policing";

	public override AI_ClassScheduleIdSpace GetClassScheduleIdSpace() => gm_ClassScheduleIdSpace;
	protected override string GetSchedulingErrorName() => gm_pszErrorClassName;

	public static bool LoadSchedules() => CustomSchedules.Load(AI_BaseNPC.LoadSchedules);

	static readonly AI_CustomSchedules CustomSchedules = new() {
		ClassName = gm_pszErrorClassName,
		IdSpace = gm_ClassScheduleIdSpace,
		ParentIdSpace = AccessClassScheduleIdSpaceDirect(),
		Schedules = [
			(nameof(SCHED_POLICE_WARN_TARGET), SCHED_POLICE_WARN_TARGET),
			(nameof(SCHED_POLICE_HARASS_TARGET), SCHED_POLICE_HARASS_TARGET),
			(nameof(SCHED_POLICE_SUPPRESS_TARGET), SCHED_POLICE_SUPPRESS_TARGET),
			(nameof(SCHED_POLICE_RETURN_FROM_HARASS), SCHED_POLICE_RETURN_FROM_HARASS),
			(nameof(SCHED_POLICE_TRACK_TARGET), SCHED_POLICE_TRACK_TARGET),
			(nameof(SCHED_POLICE_FACE_ALONG_GOAL), SCHED_POLICE_FACE_ALONG_GOAL),
		],
		Tasks = [
			(nameof(TASK_POLICE_GET_PATH_TO_HARASS_GOAL), TASK_POLICE_GET_PATH_TO_HARASS_GOAL),
			(nameof(TASK_POLICE_GET_PATH_TO_POLICE_GOAL), TASK_POLICE_GET_PATH_TO_POLICE_GOAL),
			(nameof(TASK_POLICE_ANNOUNCE_HARASS), TASK_POLICE_ANNOUNCE_HARASS),
			(nameof(TASK_POLICE_FACE_ALONG_GOAL), TASK_POLICE_FACE_ALONG_GOAL),
		],
		Conditions = [
			(nameof(COND_POLICE_TARGET_TOO_CLOSE_HARASS), COND_POLICE_TARGET_TOO_CLOSE_HARASS),
			(nameof(COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS), COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS),
		],
		Definitions = [
			new(SCHED_POLICE_WARN_TARGET,
				[
					TASK_STOP_MOVING,
					TASK_FACE_TARGET,
					TASK_POLICE_ANNOUNCE_HARASS,
					(TASK_PLAY_SEQUENCE, Activity.ACT_POLICE_HARASS1),
				],
				[
					COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS,
				]),

			new(SCHED_POLICE_HARASS_TARGET,
				[
					TASK_STOP_MOVING,
					TASK_FACE_TARGET,
					(TASK_POLICE_GET_PATH_TO_HARASS_GOAL, 64),
					TASK_WAIT_FOR_MOVEMENT,
					TASK_POLICE_ANNOUNCE_HARASS,
					(TASK_PLAY_SEQUENCE, Activity.ACT_POLICE_HARASS1),
				],
				[
					COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS,
				]),

			new(SCHED_POLICE_SUPPRESS_TARGET,
				[
					TASK_STOP_MOVING,
					TASK_FACE_TARGET,
					TASK_POLICE_ANNOUNCE_HARASS,
					(TASK_PLAY_SEQUENCE, Activity.ACT_POLICE_HARASS1),
				]),

			new(SCHED_POLICE_RETURN_FROM_HARASS,
				[
					TASK_STOP_MOVING,
					(TASK_POLICE_GET_PATH_TO_POLICE_GOAL, 16),
					TASK_WALK_PATH,
					TASK_WAIT_FOR_MOVEMENT,
					TASK_STOP_MOVING,
				],
				[
					COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS,
				]),

			new(SCHED_POLICE_TRACK_TARGET,
				[
					TASK_FACE_TARGET,
				],
				[
					COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS,
				]),

			new(SCHED_POLICE_FACE_ALONG_GOAL,
				[
					(TASK_WAIT_RANDOM, 2),
					TASK_POLICE_FACE_ALONG_GOAL,
				],
				[
					COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS,
				]),
		],
	};

	public bool TargetIsHostile() {
		if ((TargetHostileTime < gpGlobals.CurTime) && (!TargetIsHostileValue))
			return false;

		return true;
	}

	public void Enable(AI_PoliceGoal goal) {
		PoliceGoal.Set(goal);
		Enabled = true;

		StartPolicing = true;

		GetOuter()!.ClearSchedule("Enable police behavior");
	}

	public void Disable() {
		PoliceGoal.Set(null);
		Enabled = false;
	}

	public bool IsEnabled() => Enabled;

	AI_PoliceGoal? GetPoliceGoal() => (AI_PoliceGoal?)PoliceGoal.Get();

	public override bool CanSelectSchedule() {
		if (IsEnabled() == false || GetPoliceGoal() == null || GetPoliceGoal()!.GetTarget() == null)
			return false;

		return true;
	}

	void HostSetBatonState(bool state) {
		if (GetOuter() is NPC_MetroPolice cop) {
			cop.SetBatonState(state);
			cop.SetTarget(GetPoliceGoal()!.GetTarget());
		}
	}

	bool HostBatonIsOn() {
		if (GetOuter() is NPC_MetroPolice cop)
			return cop.BatonActive();

		return false;
	}

	void HostSpeakSentence(string sentence, SentencePriority soundPriority, SentenceCriteria criteria) {
		if (GetOuter() is NPC_MetroPolice cop) {
			AI_Sentence_NPC_MetroPolice sentences = cop.GetSentences();

			sentences.Speak(sentence, soundPriority, criteria);
		}
	}

	protected override void BuildScheduleTestBits() {
		if (IsCurSchedule(SCHED_IDLE_STAND) || IsCurSchedule(SCHED_ALERT_STAND)) {
			if (NextHarassTime < gpGlobals.CurTime)
				GetOuter()!.SetCustomInterruptCondition(GetClassScheduleIdSpace().ConditionLocalToGlobal(COND_POLICE_TARGET_TOO_CLOSE_HARASS));

			GetOuter()!.SetCustomInterruptCondition(GetClassScheduleIdSpace().ConditionLocalToGlobal(COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS));
		}
	}

	public override void GatherConditions() {
		base.GatherConditions();

		if (GetPoliceGoal() == null) {
			Disable();
			return;
		}

		ClearCondition(COND_POLICE_TARGET_TOO_CLOSE_HARASS);
		ClearCondition(COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS);

		BaseEntity? target = GetPoliceGoal()!.GetTarget();

		if (target == null) {
			DevMsg("ai_goal_police with NULL target entity!\n");
			return;
		}

		if (ShouldKnockOutTarget(target))
			SetCondition(COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS);

		Vector3 goalCenter = GetPoliceGoal()!.WorldSpaceCenter();
		Vector3 targetCenter = target.WorldSpaceCenter();
		float distSqr = (goalCenter - targetCenter).Length2DSqr();
		float radius = GetPoliceGoal()!.GetRadius() * PATROL_RADIUS_RATIO;
		float zDiff = MathF.Abs(goalCenter.Z - targetCenter.Z);

		if (distSqr < (radius * radius) && zDiff < 32.0f) {
			SetCondition(COND_POLICE_TARGET_TOO_CLOSE_HARASS);

			if (distSqr < (GetPoliceGoal()!.GetRadius() * GetPoliceGoal()!.GetRadius()))
				SetCondition(COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS);
		}

		if (TargetIsHostileValue && AggressiveTime < gpGlobals.CurTime && IsCurSchedule(SCHED_CHASE_ENEMY))
			GetOuter()!.ClearSchedule("Stopped chasing, aggression over");
	}

	void AnnouncePolicing() {
		if (NumWarnings <= 3)
			HostSpeakSentence(Warnings[NumWarnings - 1], SentencePriority.Medium, SentenceCriteria.Normal);
		else {
			int sentence = random.RandomInt(0, 1);
			HostSpeakSentence(Warnings[sentence], SentencePriority.Medium, SentenceCriteria.Normal);
		}
	}

	protected override int TranslateSchedule(int scheduleType) {
		if (scheduleType == SCHED_CHASE_ENEMY) {
			if (GetPoliceGoal()!.ShouldRemainAtPost() && !MaintainGoalPosition())
				return base.TranslateSchedule(SCHED_COMBAT_FACE);
		}

		return base.TranslateSchedule(scheduleType);
	}

	protected override Activity NPC_TranslateActivity(Activity newActivity) {
		if (newActivity == Activity.ACT_POLICE_HARASS1) {
			switch (NumWarnings) {
				case 1:
					return Activity.ACT_POLICE_HARASS1;

				default:
				case 2:
					return Activity.ACT_POLICE_HARASS2;
			}
		}

		return base.NPC_TranslateActivity(newActivity);
	}

	public BaseEntity? GetGoalTarget() {
		if (GetPoliceGoal() == null) {
			Assert(false);
			return null;
		}

		return GetPoliceGoal()!.GetTarget();
	}

	protected override void StartTask(in Task_t task) {
		switch (task.Task) {
			case TASK_POLICE_GET_PATH_TO_HARASS_GOAL: {
					Vector3 harassDir = GetPoliceGoal()!.GetTarget()!.WorldSpaceCenter() - GetOuter()!.WorldSpaceCenter();
					float dist = MathLib.VectorNormalize(ref harassDir);

					if (dist < task.TaskData) {
						GetOuter()!.TaskComplete();
						break;
					}

					Vector3 harassPos = GetOuter()!.GetAbsOrigin() + (harassDir * (dist - task.TaskData));

					if (CollisionUtils.IntersectInfiniteRayWithSphere(GetOuter()!.GetAbsOrigin(), harassDir, GetPoliceGoal()!.GetAbsOrigin(), GetPoliceGoal()!.GetRadius(), out float inter1, out float inter2)) {
						Vector3 pos = GetPoliceGoal()!.GetAbsOrigin() + harassDir * Math.Max(inter1, inter2);

						float testDist = Util.DistApprox2D(GetPoliceGoal()!.GetAbsOrigin(), harassPos);

						if (testDist > Util.DistApprox2D(GetPoliceGoal()!.GetAbsOrigin(), pos))
							harassPos = pos;
					}

					if (GetNavigator()!.SetGoal(new AI_NavGoal(harassPos), (AI_NavSetGoalFlags)(uint)task.TaskData)) {
						GetNavigator()!.SetMovementActivity(Activity.ACT_WALK_ANGRY);
						GetNavigator()!.SetArrivalDirection(GetPoliceGoal()!.GetTarget());
						GetOuter()!.TaskComplete();
					}
					else
						GetOuter()!.TaskFail(AI_TaskFailureCode.NoRoute);
				}
				break;

			case TASK_POLICE_GET_PATH_TO_POLICE_GOAL: {
					if (GetNavigator()!.SetGoal(new AI_NavGoal(GetPoliceGoal()!.GetAbsOrigin()), (AI_NavSetGoalFlags)(uint)task.TaskData)) {
						GetNavigator()!.SetArrivalDirection(GetPoliceGoal()!.GetAbsAngles());
						GetOuter()!.TaskComplete();
					}
					else
						GetOuter()!.TaskFail(AI_TaskFailureCode.NoRoute);
				}
				break;

			case TASK_POLICE_ANNOUNCE_HARASS: {
					AnnouncePolicing();

					NextHarassTime = gpGlobals.CurTime + random.RandomInt(4, 6);

					SoundEnt.InsertSound(SoundInstanceType.MoveAway, GetOuter()!.GetAbsOrigin(), 256, 2.0f, GetOuter());
				}
				GetOuter()!.TaskComplete();
				break;

			case TASK_POLICE_FACE_ALONG_GOAL: {
					if (GetPoliceGoal() != null) {
						GetMotor()!.SetIdealYaw(GetPoliceGoal()!.GetAbsAngles().Y);
						GetOuter()!.SetTurnActivity();
					}
				}
				break;

			default:
				base.StartTask(task);
				break;
		}
	}

	protected override void RunTask(in Task_t task) {
		switch (task.Task) {
			case TASK_POLICE_FACE_ALONG_GOAL: {
					GetMotor()!.UpdateYaw();

					if (GetOuter()!.FacingIdeal())
						GetOuter()!.TaskComplete();
					break;
				}

			default:
				base.RunTask(task);
				break;
		}
	}

	bool MaintainGoalPosition() {
		Vector3 org = GetOuter()!.GetAbsOrigin();
		Vector3 target = GetPoliceGoal()!.GetAbsOrigin();

		if (MathF.Abs(org.Z - target.Z) > 64)
			return true;

		if ((org - target).Length2D() > 16)
			return true;

		return false;
	}

	public bool ShouldKnockOutTarget(BaseEntity target) {
		if (GetPoliceGoal() == null) {
			Assert(false);
			return false;
		}

		bool visible = GetOuter()!.FVisible(target);
		return GetPoliceGoal()!.ShouldKnockOutTarget(target.WorldSpaceCenter(), visible);
	}

	public void KnockOutTarget(BaseEntity? target) {
		if (GetPoliceGoal() == null) {
			Assert(false);
			return;
		}

		GetPoliceGoal()!.KnockOutTarget(target);
	}

	int SelectSuppressSchedule() {
		BaseEntity target = GetPoliceGoal()!.GetTarget()!;

		AggressiveTime = gpGlobals.CurTime + 4.0f;

		if (TargetIsHostileValue == false) {
			TargetIsHostileValue = true;

			GetOuter()!.SetEnemy(target);
			GetOuter()!.SetState(NPCState.Combat);
			GetOuter()!.UpdateEnemyMemory(target, target.GetAbsOrigin());

			HostSetBatonState(true);

			NumWarnings = POLICE_MAX_WARNINGS;

			return SCHED_COMBAT_FACE;
		}

		if (GetPoliceGoal()!.ShouldRemainAtPost()) {
			if (MaintainGoalPosition())
				return SCHED_CHASE_ENEMY;

			if (NextHarassTime < gpGlobals.CurTime)
				return SCHED_POLICE_WARN_TARGET;
			else
				return SCHED_COMBAT_FACE;
		}

		return SCHED_CHASE_ENEMY;
	}

	int SelectHarassSchedule() {
		BaseEntity target = GetPoliceGoal()!.GetTarget()!;

		AggressiveTime = gpGlobals.CurTime + 4.0f;

		if (MaintainGoalPosition())
			return SCHED_POLICE_RETURN_FROM_HARASS;

		GetOuter()!.AddLookTarget(target, 0.5f, 5.0f);

		if (NextHarassTime < gpGlobals.CurTime) {
			GetOuter()!.SetTarget(target);

			if (NumWarnings == 0)
				GetPoliceGoal()!.FireWarningLevelOutput(1);
			else if (NumWarnings == 1)
				GetPoliceGoal()!.FireWarningLevelOutput(2);

			if (NumWarnings < POLICE_MAX_WARNINGS)
				NumWarnings++;

			if (NumWarnings >= POLICE_MAX_WARNINGS) {
				if (TargetIsHostileValue == false) {
					TargetIsHostileValue = true;

					GetOuter()!.SetEnemy(target);
					GetOuter()!.SetState(NPCState.Combat);
					GetOuter()!.UpdateEnemyMemory(target, target.GetAbsOrigin());
					HostSetBatonState(true);

					GetPoliceGoal()!.FireWarningLevelOutput(4);

					return SCHED_COMBAT_FACE;
				}

				if (GetPoliceGoal()!.ShouldRemainAtPost() == false)
					return SCHED_CHASE_ENEMY;
			}

			if (NumWarnings == (POLICE_MAX_WARNINGS - 1)) {
				GetPoliceGoal()!.FireWarningLevelOutput(3);

				GetOuter()!.SetTarget(target);

				HostSetBatonState(true);

				if (GetPoliceGoal()!.ShouldRemainAtPost() == false)
					return SCHED_POLICE_HARASS_TARGET;
			}

			return SCHED_POLICE_WARN_TARGET;
		}

		return SCHED_NONE;
	}

	protected override int SelectSchedule() {
		BaseEntity? target = GetPoliceGoal()!.GetTarget();

		if (target == null) {
			DevMsg("ai_goal_police with NULL target entity!\n");

			Disable();
			return SCHED_NONE;
		}

		if ((AggressiveTime >= gpGlobals.CurTime) && HasCondition((int)SCOND_t.COND_CAN_MELEE_ATTACK1))
			return SCHED_MELEE_ATTACK1;

		if (HasCondition(COND_POLICE_TARGET_TOO_CLOSE_SUPPRESS))
			return SelectSuppressSchedule();

		int newSchedule = SCHED_NONE;

		if (HasCondition(COND_POLICE_TARGET_TOO_CLOSE_HARASS))
			newSchedule = SelectHarassSchedule();

		if (newSchedule != SCHED_NONE)
			return newSchedule;

		if (AggressiveTime < gpGlobals.CurTime) {
			if (GetOuter()!.GetEnemy() != null) {
				GetOuter()!.SetEnemy(null);
				GetOuter()!.SetState(NPCState.Alert);
				GetOuter()!.GetEnemies().RefreshMemories();
			}

			HostSetBatonState(false);
			TargetIsHostileValue = false;
		}

		if (MaintainGoalPosition())
			return SCHED_POLICE_RETURN_FROM_HARASS;

		if (HostBatonIsOn())
			return SCHED_POLICE_TRACK_TARGET;

		if (MathF.Abs(Util.AngleDiff(GetOuter()!.GetAbsAngles().Y, GetPoliceGoal()!.GetAbsAngles().Y)) > 15)
			return SCHED_POLICE_FACE_ALONG_GOAL;

		return SCHED_IDLE_STAND;
	}

	protected override int SelectFailSchedule(int failedSchedule, int failedTask, AI_TaskFailureCode taskFailCode) {
		if (failedSchedule == SCHED_CHASE_ENEMY) {
			if (MaintainGoalPosition())
				return SCHED_POLICE_RETURN_FROM_HARASS;

			return SCHED_POLICE_WARN_TARGET;
		}

		return base.SelectFailSchedule(failedSchedule, failedTask, taskFailCode);
	}

	protected bool Enabled;
	protected bool StartPolicing;
	protected TimeUnit_t NextHarassTime;
	protected TimeUnit_t AggressiveTime;
	protected int NumWarnings;
	protected bool TargetIsHostileValue;
	protected TimeUnit_t TargetHostileTime;

	protected EHANDLE PoliceGoal = new();
}
