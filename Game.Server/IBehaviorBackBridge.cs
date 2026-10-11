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
}
