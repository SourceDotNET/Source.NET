global using static Game.Server.AI_BehaviorGlobals;

using Game.Shared;

namespace Game.Server;

public static class AI_BehaviorGlobals
{
	public static bool g_bBehaviorHost_PreventBaseClassGatherConditions;
}

public abstract class AI_BehaviorBase : AI_Component
{
	public AI_BehaviorBase(AI_BaseNPC? outer = null) : base(outer) {
		BackBridge = null;
	}

	public abstract string GetName();

	public bool IsRunning() {
		Assert(GetOuter() != null);
		return GetOuter()!.GetRunningBehavior() == this;
	}

	public virtual bool CanSelectSchedule() => true;
	public virtual void BeginScheduleSelection() { }
	public virtual void EndScheduleSelection() { }

	public void SetBackBridge(IBehaviorBackBridge? backBridge) {
		Assert(BackBridge == null || backBridge == null);
		BackBridge = backBridge;
	}

	public void BridgePrecache() => Precache();
	public void BridgeSpawn() => Spawn();
	public void BridgeUpdateOnRemove() => UpdateOnRemove();
	public void BridgeEvent_Killed(in TakeDamageInfo info) => Event_Killed(info);

	public void BridgeGatherConditions() => GatherConditions();
	public void BridgePrescheduleThink() => PrescheduleThink();
	public void BridgeOnScheduleChange() => OnScheduleChange();

	public void BridgeOnStartSchedule(int scheduleType) {
		int localId = AI_IdIsGlobal(scheduleType) ? GetClassScheduleIdSpace().ScheduleGlobalToLocal(scheduleType) : scheduleType;
		OnStartSchedule(localId);
	}

	public int BridgeSelectSchedule() {
		int result = SelectSchedule();

		if (IsBehaviorSchedule(result))
			return GetClassScheduleIdSpace().ScheduleLocalToGlobal(result);

		return result;
	}

	public bool BridgeSelectFailSchedule(int failedSchedule, int failedTask, AI_TaskFailureCode taskFailCode, out int result) {
		result = 0;
		Overrode = true;
		int selected = SelectFailSchedule(failedSchedule, failedTask, taskFailCode);
		if (Overrode) {
			if (selected != SCHED_NONE) {
				if (IsBehaviorSchedule(selected))
					result = GetClassScheduleIdSpace().ScheduleLocalToGlobal(selected);
				else
					result = selected;
				return true;
			}
			Warning("An AI behavior is in control but has no recommended schedule\n");
		}
		return false;
	}

	public bool BridgeStartTask(in Task_t task) {
		Overrode = true;
		StartTask(task);
		return Overrode;
	}

	public bool BridgeRunTask(in Task_t task) {
		Overrode = true;
		RunTask(task);
		return Overrode;
	}

	public bool BridgeAimGun() {
		Overrode = true;
		AimGun();
		return Overrode;
	}

	public int BridgeTranslateSchedule(int scheduleType) {
		int localId = AI_IdIsGlobal(scheduleType) ? GetClassScheduleIdSpace().ScheduleGlobalToLocal(scheduleType) : scheduleType;
		return TranslateSchedule(localId);
	}

	public bool BridgeGetSchedule(int localScheduleID, out AI_Schedule? result) {
		result = GetSchedule(localScheduleID);
		return result != null;
	}

	public Activity BridgeNPC_TranslateActivity(Activity activity) => NPC_TranslateActivity(activity);
	public void BridgeBuildScheduleTestBits() => BuildScheduleTestBits();

	public bool BridgeIsCurTaskContinuousMove(out bool result) {
		bool prevOverride = Overrode;
		Overrode = true;
		result = IsCurTaskContinuousMove();
		bool overrode = Overrode;
		Overrode = prevOverride;
		return overrode;
	}

	public void BridgeOnMovementFailed() => OnMovementFailed();
	public void BridgeOnMovementComplete() => OnMovementComplete();
	public float BridgeGetDefaultNavGoalTolerance() => GetDefaultNavGoalTolerance();
	public bool BridgeIsNavigationUrgent() => IsNavigationUrgent();
	public bool BridgeShouldPlayerAvoid() => ShouldPlayerAvoid();
	public bool BridgeIsCrouching() => IsCrouching();
	public bool BridgeOnCalcBaseMove(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) => OnCalcBaseMove(ref moveGoal, distClear, ref result);

	public bool BridgeFValidateHintType(AI_Hint hint, out bool result) {
		bool prevOverride = Overrode;
		Overrode = true;
		result = FValidateHintType(hint);
		bool overrode = Overrode;
		Overrode = prevOverride;
		return overrode;
	}

	public bool BridgeShouldIgnoreSound(ref WorldSoundInstance sound) => ShouldIgnoreSound(ref sound);
	public void BridgeOnSeeEntity(BaseEntity entity) => OnSeeEntity(entity);
	public bool BridgeIsInterruptable() => IsInterruptable();
	public bool BridgeCanFlinch() => CanFlinch();
	public bool BridgeQueryHearSound(ref WorldSoundInstance sound) => QueryHearSound(ref sound);
	public float BridgeGetReasonableFacingDist() => GetReasonableFacingDist();
	public void BridgeHandleAnimEvent(ref AnimEvent animEvent) => HandleAnimEvent(ref animEvent);

	protected virtual bool FValidateHintType(AI_Hint hint) {
		Overrode = false;
		return false;
	}

	protected virtual bool ShouldIgnoreSound(ref WorldSoundInstance sound) {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_ShouldIgnoreSound(ref sound);
	}

	protected virtual void OnSeeEntity(BaseEntity entity) {
		Assert(BackBridge != null);

		BackBridge!.BackBridge_OnSeeEntity(entity);
	}

	protected virtual bool IsInterruptable() {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_IsInterruptable();
	}

	protected virtual bool CanFlinch() {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_CanFlinch();
	}

	protected virtual bool QueryHearSound(ref WorldSoundInstance sound) {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_QueryHearSound(ref sound);
	}

	protected virtual float GetReasonableFacingDist() {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_GetReasonableFacingDist();
	}

	protected virtual void HandleAnimEvent(ref AnimEvent animEvent) {
		Assert(BackBridge != null);

		BackBridge!.BackBridge_HandleAnimEvent(ref animEvent);
	}

	protected AI_Hint? GetHintNode() => GetOuter()!.GetHintNode();
	protected void SetHintNode(AI_Hint? hintNode) => GetOuter()!.SetHintNode(hintNode);
	protected void ClearHintNode(float reuseDelay = 0.0f) => GetOuter()!.ClearHintNode(reuseDelay);

	public virtual void GatherConditions() {
		Assert(BackBridge != null);

		BackBridge!.BackBridge_GatherConditions();
	}

	public virtual void GatherConditionsNotActive() { }

	public virtual AI_ClassScheduleIdSpace GetClassScheduleIdSpace() => GetOuter()!.GetClassScheduleIdSpace();

	protected virtual void Precache() { }
	protected virtual void Spawn() { }
	protected virtual void UpdateOnRemove() { }
	protected virtual void Event_Killed(in TakeDamageInfo info) { }

	protected virtual void PrescheduleThink() { }
	protected virtual void OnScheduleChange() { }
	protected virtual void OnStartSchedule(int scheduleType) { }

	protected virtual int SelectSchedule() {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_SelectSchedule();
	}

	protected virtual int SelectFailSchedule(int failedSchedule, int failedTask, AI_TaskFailureCode taskFailCode) {
		Overrode = false;
		return SCHED_NONE;
	}

	protected virtual void StartTask(in Task_t task) => Overrode = false;
	protected virtual void RunTask(in Task_t task) => Overrode = false;
	protected virtual void AimGun() => Overrode = false;

	protected virtual int TranslateSchedule(int scheduleType) {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_TranslateSchedule(scheduleType);
	}

	protected virtual AI_Schedule? GetSchedule(int schedule) {
		if (!GetClassScheduleIdSpace().IsGlobalBaseSet()) {
			Warning($"ERROR: {GetSchedulingErrorName()} missing schedule!\n");
			return g_AI_SchedulesManager.GetScheduleFromID(SCHED_IDLE_STAND);
		}
		if (AI_IdIsLocal(schedule))
			schedule = GetClassScheduleIdSpace().ScheduleLocalToGlobal(schedule);

		if (schedule == -1)
			return null;

		return g_AI_SchedulesManager.GetScheduleFromID(schedule);
	}

	protected virtual string GetSchedulingErrorName() => "CAI_Behavior";

	protected virtual void BuildScheduleTestBits() { }

	protected bool IsCurSchedule(int schedule, bool ideal = true) {
		if (AI_IdIsLocal(schedule))
			schedule = GetClassScheduleIdSpace().ScheduleLocalToGlobal(schedule);

		return GetOuter()!.IsCurSchedule(schedule, ideal);
	}

	protected void ChainStartTask(int task, float taskData = 0) {
		Task_t tempTask = new() { Task = task, TaskData = taskData };

		bool prevOverride = Overrode;
		GetOuter()!.StartTask(tempTask);
		Overrode = prevOverride;
	}

	protected void ChainRunTask(int task, float taskData = 0) {
		Task_t tempTask = new() { Task = task, TaskData = taskData };

		bool prevOverride = Overrode;
		GetOuter()!.RunTask(tempTask);
		Overrode = prevOverride;
	}

	protected virtual Activity NPC_TranslateActivity(Activity activity) {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_NPC_TranslateActivity(activity);
	}

	protected virtual bool IsCurTaskContinuousMove() {
		Overrode = false;
		return false;
	}

	protected virtual void OnMovementFailed() { }
	protected virtual void OnMovementComplete() { }

	protected virtual float GetDefaultNavGoalTolerance() {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_GetDefaultNavGoalTolerance();
	}

	protected virtual bool IsNavigationUrgent() {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_IsNavigationUrgent();
	}

	protected virtual bool ShouldPlayerAvoid() {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_ShouldPlayerAvoid();
	}

	protected virtual bool IsCrouching() {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_IsCrouching();
	}

	protected virtual bool OnCalcBaseMove(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		Assert(BackBridge != null);

		return BackBridge!.BackBridge_OnCalcBaseMove(ref moveGoal, distClear, ref result);
	}

	protected virtual bool IsBehaviorSchedule(int scheduleType) => false;

	protected AI_Navigator? GetNavigator() => GetOuter()!.GetNavigator();
	protected AI_Motor? GetMotor() => GetOuter()!.GetMotor();

	protected bool Overrode;
	protected IBehaviorBackBridge? BackBridge;
}
