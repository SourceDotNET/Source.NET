using Game.Shared;

namespace Game.Server;

public class AI_BehaviorHost_AI_BaseNPC : AI_BaseNPC, IBehaviorBackBridge
{
	public AI_BehaviorHost_AI_BaseNPC() {
		CurBehavior = null;
	}

	public override bool CreateComponents() {
		if (base.CreateComponents()) {
			bool result = CreateBehaviors();
			return result;
		}
		return false;
	}

	public virtual bool CreateBehaviors() => true;

	public override bool ScheduledMoveToGoalEntity(int scheduleType, BaseEntity goalEntity, Activity movementActivity) {
		ChangeBehaviorTo(null);

		return base.ScheduledMoveToGoalEntity(scheduleType, goalEntity, movementActivity);
	}

	public override bool ScheduledFollowPath(int scheduleType, BaseEntity pathStart, Activity movementActivity) {
		ChangeBehaviorTo(null);

		return base.ScheduledFollowPath(scheduleType, pathStart, movementActivity);
	}

	public override void Precache() {
		base.Precache();
		for (int i = 0; i < Behaviors.Count; i++)
			Behaviors[i].BridgePrecache();
	}

	public override void NPCInit() {
		base.NPCInit();
		for (int i = 0; i < Behaviors.Count; i++)
			Behaviors[i].BridgeSpawn();
	}

	public override void UpdateOnRemove() {
		for (int i = 0; i < Behaviors.Count; i++)
			Behaviors[i].BridgeUpdateOnRemove();
		base.UpdateOnRemove();
	}

	public override void Event_Killed(in TakeDamageInfo info) {
		for (int i = 0; i < Behaviors.Count; i++)
			Behaviors[i].BridgeEvent_Killed(info);
		base.Event_Killed(info);
	}

	public override void GatherConditions() {
		for (int i = 0; i < Behaviors.Count; i++) {
			if (Behaviors[i] != CurBehavior)
				Behaviors[i].GatherConditionsNotActive();
		}

		if (CurBehavior != null)
			CurBehavior.BridgeGatherConditions();
		else
			base.GatherConditions();
	}

	public override void OnScheduleChange() {
		CurBehavior?.BridgeOnScheduleChange();
		base.OnScheduleChange();
	}

	public override void OnStartSchedule(int scheduleType) {
		CurBehavior?.BridgeOnStartSchedule(scheduleType);
		base.OnStartSchedule(scheduleType);
	}

	public override int TranslateSchedule(int scheduleType) {
		if (CurBehavior != null)
			return CurBehavior.BridgeTranslateSchedule(scheduleType);
		return base.TranslateSchedule(scheduleType);
	}

	public override void PrescheduleThink() {
		base.PrescheduleThink();
		CurBehavior?.BridgePrescheduleThink();
	}

	public override int SelectSchedule() {
		CalledBehaviorSelectSchedule = true;
		if (CurBehavior != null)
			return CurBehavior.BridgeSelectSchedule();

		return base.SelectSchedule();
	}

	public void KeepRunningBehavior() {
		if (CurBehavior != null)
			CalledBehaviorSelectSchedule = true;
	}

	public override int SelectFailSchedule(int failedSchedule, int failedTask, AI_TaskFailureCode taskFailCode) {
		CalledBehaviorSelectSchedule = true;
		if (CurBehavior != null && CurBehavior.BridgeSelectFailSchedule(failedSchedule, failedTask, taskFailCode, out int result))
			return result;
		return base.SelectFailSchedule(failedSchedule, failedTask, taskFailCode);
	}

	public override void StartTask(in Task_t task) {
		if (CurBehavior != null && CurBehavior.BridgeStartTask(task))
			return;
		base.StartTask(task);
	}

	public override void RunTask(in Task_t task) {
		if (CurBehavior != null && CurBehavior.BridgeRunTask(task))
			return;
		base.RunTask(task);
	}

	public override void AimGun() {
		if (CurBehavior != null && CurBehavior.BridgeAimGun())
			return;
		base.AimGun();
	}

	public override AI_Schedule? GetSchedule(int localScheduleID) {
		if (CurBehavior != null && CurBehavior.BridgeGetSchedule(localScheduleID, out AI_Schedule? result))
			return result;
		return base.GetSchedule(localScheduleID);
	}

	public override void BuildScheduleTestBits() {
		CurBehavior?.BridgeBuildScheduleTestBits();
		base.BuildScheduleTestBits();
	}

	public override Activity NPC_TranslateActivity(Activity activity) {
		if (CurBehavior != null)
			return CurBehavior.BridgeNPC_TranslateActivity(activity);
		return base.NPC_TranslateActivity(activity);
	}

	public override bool IsCurTaskContinuousMove() {
		if (CurBehavior != null && CurBehavior.BridgeIsCurTaskContinuousMove(out bool result))
			return result;
		return base.IsCurTaskContinuousMove();
	}

	public override void OnMovementFailed() {
		CurBehavior?.BridgeOnMovementFailed();
		base.OnMovementFailed();
	}

	public override void OnMovementComplete() {
		CurBehavior?.BridgeOnMovementComplete();
		base.OnMovementComplete();
	}

	public override float GetDefaultNavGoalTolerance() {
		if (CurBehavior != null)
			return CurBehavior.BridgeGetDefaultNavGoalTolerance();
		return base.GetDefaultNavGoalTolerance();
	}

	public override bool IsNavigationUrgent() {
		if (CurBehavior != null)
			return CurBehavior.BridgeIsNavigationUrgent();

		return base.IsNavigationUrgent();
	}

	public override bool ShouldPlayerAvoid() {
		if (CurBehavior != null)
			return CurBehavior.BridgeShouldPlayerAvoid();

		return base.ShouldPlayerAvoid();
	}

	public override bool IsCrouching() {
		if (CurBehavior != null)
			return CurBehavior.BridgeIsCrouching();

		return base.IsCrouching();
	}

	public override bool OnCalcBaseMove(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		if (CurBehavior != null)
			return CurBehavior.BridgeOnCalcBaseMove(ref moveGoal, distClear, ref result);

		return base.OnCalcBaseMove(ref moveGoal, distClear, ref result);
	}

	public override bool FValidateHintType(AI_Hint hint) {
		if (CurBehavior != null && CurBehavior.BridgeFValidateHintType(hint, out bool result))
			return result;
		return base.FValidateHintType(hint);
	}

	public override bool ShouldIgnoreSound(ref WorldSoundInstance sound) {
		if (CurBehavior != null)
			return CurBehavior.BridgeShouldIgnoreSound(ref sound);

		return base.ShouldIgnoreSound(ref sound);
	}

	public override void OnSeeEntity(BaseEntity entity) {
		if (CurBehavior != null) {
			CurBehavior.BridgeOnSeeEntity(entity);
			return;
		}

		base.OnSeeEntity(entity);
	}

	public override bool IsInterruptable() {
		if (CurBehavior != null)
			return CurBehavior.BridgeIsInterruptable();

		return base.IsInterruptable();
	}

	public override bool CanFlinch() {
		if (CurBehavior != null)
			return CurBehavior.BridgeCanFlinch();

		return base.CanFlinch();
	}

	public override bool QueryHearSound(ref WorldSoundInstance sound) {
		if (CurBehavior != null)
			return CurBehavior.BridgeQueryHearSound(ref sound);

		return base.QueryHearSound(ref sound);
	}

	public override float GetReasonableFacingDist() {
		if (CurBehavior != null)
			return CurBehavior.BridgeGetReasonableFacingDist();

		return base.GetReasonableFacingDist();
	}

	public override void HandleAnimEvent(ref AnimEvent animEvent) {
		if (CurBehavior != null) {
			CurBehavior.BridgeHandleAnimEvent(ref animEvent);
			return;
		}

		base.HandleAnimEvent(ref animEvent);
	}

	protected override List<AI_BehaviorBase>? AccessBehaviors() => Behaviors;

	public virtual bool OnBehaviorChangeStatus(AI_BehaviorBase behavior, bool canFinishSchedule) {
		if (behavior == GetRunningBehavior() && !behavior.CanSelectSchedule() && !canFinishSchedule) {
			DeferSchedulingToBehavior(null);
			return true;
		}
		return false;
	}

	public virtual void OnChangeRunningBehavior(AI_BehaviorBase? oldBehavior, AI_BehaviorBase? newBehavior) { }

	protected void AddBehavior(AI_BehaviorBase behavior) {
		Behaviors.Add(behavior);
		behavior.SetOuter(this);
		behavior.SetBackBridge(this);
	}

	protected bool BehaviorSelectSchedule() {
		for (int i = 0; i < Behaviors.Count; i++) {
			if (Behaviors[i].CanSelectSchedule() && ShouldBehaviorSelectSchedule(Behaviors[i])) {
				DeferSchedulingToBehavior(Behaviors[i]);
				return true;
			}
		}

		DeferSchedulingToBehavior(null);
		return false;
	}

	protected virtual bool ShouldBehaviorSelectSchedule(AI_BehaviorBase behavior) => true;

	protected bool IsRunningBehavior() => CurBehavior != null;

	public override AI_BehaviorBase? GetRunningBehavior() => CurBehavior;

	protected AI_BehaviorBase? DeferSchedulingToBehavior(AI_BehaviorBase? newBehavior) {
		AI_BehaviorBase? oldBehavior = CurBehavior;
		ChangeBehaviorTo(newBehavior);
		return oldBehavior;
	}

	protected void ChangeBehaviorTo(AI_BehaviorBase? newBehavior) {
		bool change = CurBehavior != newBehavior;
		AI_BehaviorBase? oldBehavior = CurBehavior;
		CurBehavior = newBehavior;

		if (change) {
			if (CurBehavior != null) {
				CurBehavior.BeginScheduleSelection();

				g_bBehaviorHost_PreventBaseClassGatherConditions = true;
				CurBehavior.GatherConditions();
				g_bBehaviorHost_PreventBaseClassGatherConditions = false;
			}

			if (oldBehavior != null) {
				oldBehavior.EndScheduleSelection();
				VacateStrategySlot();
			}

			OnChangeRunningBehavior(oldBehavior, newBehavior);
		}
	}

	public override AI_Schedule? GetNewSchedule() {
		CalledBehaviorSelectSchedule = false;
		AI_Schedule? result = base.GetNewSchedule();
		if (!CalledBehaviorSelectSchedule && CurBehavior != null)
			DeferSchedulingToBehavior(null);
		return result;
	}

	public override AI_Schedule? GetFailSchedule() {
		CalledBehaviorSelectSchedule = false;
		AI_Schedule? result = base.GetFailSchedule();
		if (!CalledBehaviorSelectSchedule && CurBehavior != null)
			DeferSchedulingToBehavior(null);
		return result;
	}

	void IBehaviorBackBridge.BackBridge_GatherConditions() {
		if (g_bBehaviorHost_PreventBaseClassGatherConditions)
			return;

		base.GatherConditions();
	}

	int IBehaviorBackBridge.BackBridge_SelectSchedule() => base.SelectSchedule();
	int IBehaviorBackBridge.BackBridge_TranslateSchedule(int scheduleType) => base.TranslateSchedule(scheduleType);
	Activity IBehaviorBackBridge.BackBridge_NPC_TranslateActivity(Activity activity) => base.NPC_TranslateActivity(activity);
	bool IBehaviorBackBridge.BackBridge_IsNavigationUrgent() => base.IsNavigationUrgent();
	bool IBehaviorBackBridge.BackBridge_ShouldPlayerAvoid() => base.ShouldPlayerAvoid();
	float IBehaviorBackBridge.BackBridge_GetDefaultNavGoalTolerance() => base.GetDefaultNavGoalTolerance();
	bool IBehaviorBackBridge.BackBridge_IsCrouching() => base.IsCrouching();
	bool IBehaviorBackBridge.BackBridge_OnCalcBaseMove(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) => base.OnCalcBaseMove(ref moveGoal, distClear, ref result);
	bool IBehaviorBackBridge.BackBridge_ShouldIgnoreSound(ref WorldSoundInstance sound) => base.ShouldIgnoreSound(ref sound);
	void IBehaviorBackBridge.BackBridge_OnSeeEntity(BaseEntity entity) => base.OnSeeEntity(entity);
	bool IBehaviorBackBridge.BackBridge_IsInterruptable() => base.IsInterruptable();
	bool IBehaviorBackBridge.BackBridge_CanFlinch() => base.CanFlinch();
	bool IBehaviorBackBridge.BackBridge_QueryHearSound(ref WorldSoundInstance sound) => base.QueryHearSound(ref sound);
	float IBehaviorBackBridge.BackBridge_GetReasonableFacingDist() => base.GetReasonableFacingDist();
	void IBehaviorBackBridge.BackBridge_HandleAnimEvent(ref AnimEvent animEvent) => base.HandleAnimEvent(ref animEvent);

	AI_BehaviorBase? CurBehavior;
	readonly List<AI_BehaviorBase> Behaviors = [];
	bool CalledBehaviorSelectSchedule;
}
