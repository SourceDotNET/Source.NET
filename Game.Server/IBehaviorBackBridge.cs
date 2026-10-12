namespace Game.Server;

public interface IBehaviorBackBridge
{
	void BackBridge_GatherConditions();
	int BackBridge_SelectSchedule();
	int BackBridge_TranslateSchedule(int scheduleType);
	Activity BackBridge_NPC_TranslateActivity(Activity activity);
	bool BackBridge_IsNavigationUrgent();
	bool BackBridge_ShouldPlayerAvoid();
	float BackBridge_GetDefaultNavGoalTolerance();
	bool BackBridge_IsCrouching();
	bool BackBridge_OnCalcBaseMove(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result);
	bool BackBridge_ShouldIgnoreSound(ref WorldSoundInstance sound);
	void BackBridge_OnSeeEntity(BaseEntity entity);
	bool BackBridge_IsInterruptable();
	bool BackBridge_CanFlinch();
	bool BackBridge_QueryHearSound(ref WorldSoundInstance sound);
	float BackBridge_GetReasonableFacingDist();
	void BackBridge_HandleAnimEvent(ref Game.Shared.AnimEvent animEvent);
}
