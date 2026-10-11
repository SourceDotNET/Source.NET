global using static Game.Server.AI_MoveTypesGlobals;

using System.Numerics;

namespace Game.Server;

public enum AIMoveResult
{
	BlockedEntity = -1,
	BlockedWorld = -2,
	BlockedNPC = -3,
	Illegal = -4,

	Ok = 0,

	ChangeType,
}

public enum StepGroundTest
{
	DontCheckGround = 0,
	OnValidGround,
	OnInvalidGround,
}

public struct AIMoveTrace
{
	public AIMoveResult Status;
	public Vector3 EndPosition;
	public Vector3 HitNormal;
	public BaseEntity? Obstruction;
	public float TotalDist;
	public float DistObstructed;
	public Vector3 JumpVelocity;
	public float StepUpDistance;
}

[Flags]
public enum AILocalMoveGoalFlags
{
	None,
	TargetIsGoal = 0x01,
	ConsumeInterval = 0x02,
	TargetIsTransition = 0x04,
	NoSteer = 0x08,
	NoAvoidancePaths = 0x10,
}

public struct AILocalMoveGoal
{
	public Vector3 Target;

	public Vector3 Dir;
	public Vector3 Facing;
	public float Speed;

	public float MaxDist;

	public float CurExpectedDist;

	public Navigation NavType;
	public BaseEntity? MoveTarget;

	public AILocalMoveGoalFlags Flags;

	public AI_Path? Path;

	public bool HasTraced;
	public AIMoveTrace DirectTrace;
	public AIMoveTrace ThinkTrace;
}

public enum AIMotorMoveResult
{
	Failed,
	Success,

	PartialHitNPC,
	PartialHitWorld,
	PartialHitTarget,

	NumResults
}

public static class AI_MoveTypesGlobals
{
	public static bool IsMoveBlocked(AIMoveResult moveResult) => moveResult < AIMoveResult.Ok;
	public static bool IsMoveBlocked(in AIMoveTrace moveTrace) => moveTrace.Status < AIMoveResult.Ok;

	public static AIMoveResult AIComputeBlockerMoveResult(BaseEntity blocker) {
		if (blocker.MyNPCPointer() != null)
			return AIMoveResult.BlockedNPC;
		else if (blocker.EntIndex() == 0)
			return AIMoveResult.BlockedWorld;
		return AIMoveResult.BlockedEntity;
	}

	public static float ComputePathDistance(Navigation navType, in Vector3 start, in Vector3 end) {
		if (navType == Navigation.Ground) {
			Vector2 delta = new(end.X - start.X, end.Y - start.Y);
			return delta.Length();
		}
		else
			return (end - start).Length();
	}

	public static Vector3 ComputePathVector(Navigation navType, in Vector3 start, in Vector3 end) {
		if (navType == Navigation.Ground)
			return new(end.X - start.X, end.Y - start.Y, 0.0f);
		else
			return end - start;
	}

	public static float ComputePathDirection(Navigation navType, in Vector3 start, in Vector3 end, out Vector3 direction) {
		direction = end - start;
		if (navType == Navigation.Ground)
			direction.Z = 0.0f;

		float length = direction.Length();
		if (length != 0.0f)
			direction /= length;
		return length;
	}
}
