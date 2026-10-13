namespace Game.Server;

public interface IAI_MovementSink
{
	float CalcYawSpeed() => -1.0f;

	bool OnCalcBaseMove(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) => false;
	bool OnObstructionPreSteer(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) => false;
	bool OnFailedSteer(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) => false;
	bool OnFailedLocalNavigation(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) => false;
	bool OnInsufficientStopDist(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) => false;
	bool OnMoveBlocked(ref AIMoveResult result) => false;

	bool OnMoveStalled(in AILocalMoveGoal move) => false;
	bool OnMoveExecuteFailed(in AILocalMoveGoal move, in AIMoveTrace trace, AIMotorMoveResult motorResult, ref AIMoveResult result) => false;
}
