global using static Game.Server.AI_LocalNavigatorGlobals;

using Game.Shared;

using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

public static class AI_LocalNavigatorGlobals
{
	public static readonly ConVar ai_no_steer = new("ai_no_steer", "0");
}

public class AI_LocalNavigator : AI_Component, IAI_MovementSink
{
	static readonly float[] TIME_DELAY_FULL_DIRECT_PROBE = [0.25f, 0.35f];

	public AI_LocalNavigator(AI_BaseNPC? outer) : base(outer) {
		MoveProbe = null;
		PlaneSolver = new AI_PlaneSolver(outer!);

		LastWasClear = false;
		LastMoveGoal = default;
	}

	public void Init(IAI_MovementSink? movementServices) {
		Proxied = movementServices;
		MoveProbe = GetOuter()!.GetMoveProbe();
	}

	public virtual float CalcYawSpeed() {
		float result;
		if (Proxied != null && (result = Proxied.CalcYawSpeed()) != -1.0f)
			return result;
		return -1.0f;
	}

	public virtual bool OnCalcBaseMove(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result)
		=> Proxied != null && Proxied.OnCalcBaseMove(ref moveGoal, distClear, ref result);

	public virtual bool OnObstructionPreSteer(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result)
		=> Proxied != null && Proxied.OnObstructionPreSteer(ref moveGoal, distClear, ref result);

	public virtual bool OnFailedSteer(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result)
		=> Proxied != null && Proxied.OnFailedSteer(ref moveGoal, distClear, ref result);

	public virtual bool OnFailedLocalNavigation(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result)
		=> Proxied != null && Proxied.OnFailedLocalNavigation(ref moveGoal, distClear, ref result);

	public virtual bool OnInsufficientStopDist(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result)
		=> Proxied != null && Proxied.OnInsufficientStopDist(ref moveGoal, distClear, ref result);

	public virtual bool OnMoveBlocked(ref AIMoveResult result)
		=> Proxied != null && Proxied.OnMoveBlocked(ref result);

	public virtual bool OnMoveStalled(in AILocalMoveGoal move)
		=> Proxied != null && Proxied.OnMoveStalled(move);

	public virtual bool OnMoveExecuteFailed(in AILocalMoveGoal move, in AIMoveTrace trace, AIMotorMoveResult motorResult, ref AIMoveResult result)
		=> Proxied != null && Proxied.OnMoveExecuteFailed(move, trace, motorResult, ref result);

	public void ResetMoveCalculations() {
		FullDirectTimer.Force();
		PlaneSolver.Reset();
	}

	public void AddObstacle(in Vector3 pos, float radius, AI_MoveSuggType type = AI_MoveSuggType.AvoidObject) => PlaneSolver.AddObstacle(pos, radius, null, type);

	public bool HaveObstacles() => PlaneSolver.HaveObstacles();

	protected AI_MoveProbe? GetMoveProbe() => MoveProbe;

	protected bool MoveCalcDirect(ref AILocalMoveGoal moveGoal, bool onlyCurThink, ref float distClearOut, ref AIMoveResult result) {
		bool retVal = false;

		if (moveGoal.Speed != 0) {
			AI_Motor motor = GetOuter()!.GetMotor()!;
			float minCheckDist = motor.MinCheckDist();
			float probeDist = PlaneSolver.CalcProbeDist(moveGoal.Speed);
			float checkDist = Math.Max(minCheckDist, probeDist);
			float checkStepDist = Math.Max(16.0f, probeDist * 0.5f);

			if ((moveGoal.Flags & (AILocalMoveGoalFlags.TargetIsTransition | AILocalMoveGoalFlags.TargetIsGoal)) != 0)
				checkDist = Math.Min(checkDist, moveGoal.MaxDist);

			if (checkDist <= 0.0) {
				result = AIMoveResult.Ok;
				return true;
			}

			float moveThisInterval = motor.CalcIntervalMove();
			bool expectingArrival = moveThisInterval >= checkDist;

			if (!FullDirectTimer.Expired()) {
				if (!LastWasClear ||
					 (!MathLib.VectorsAreEqual(moveGoal.Target, LastMoveGoal.Target, 0.1f) ||
					   !MathLib.VectorsAreEqual(moveGoal.Dir, LastMoveGoal.Dir, 0.1f)) ||
					 expectingArrival)
					FullDirectTimer.Force();
			}

			if (onlyCurThink)
				FullDirectTimer.Set(TIME_DELAY_FULL_DIRECT_PROBE[AIStrongOpt() ? 1 : 0]);

			bool traceClear = true;
			Vector3 testPos;
			ref readonly Vector3 localOrigin = ref GetOuter()!.GetLocalOrigin();

			if (!expectingArrival) {
				testPos = localOrigin + moveGoal.Dir * moveThisInterval;
				traceClear = GetMoveProbe()!.MoveLimit(moveGoal.NavType, localOrigin, testPos,
														 Mask.NPCSolid, moveGoal.MoveTarget,
														 100.0f,
														 (moveGoal.NavType == Navigation.Ground) ? AI_MoveLimitFlags.TwoD : AI_MoveLimitFlags.Default,
														 out moveGoal.DirectTrace);

				if (!traceClear)
					moveGoal.DirectTrace.DistObstructed = (checkDist - moveThisInterval) + moveGoal.DirectTrace.DistObstructed;

				moveGoal.ThinkTrace = moveGoal.DirectTrace;
			}

			if (traceClear) {
				if (FullDirectTimer.Expired()) {
					testPos = localOrigin + moveGoal.Dir * checkDist;
					float checkStepPct = (checkStepDist / checkDist) * 100.0f;
					if (checkStepPct > 100.0)
						checkStepPct = 100.0f;

					traceClear = GetMoveProbe()!.MoveLimit(moveGoal.NavType, localOrigin, testPos,
															 Mask.NPCSolid, moveGoal.MoveTarget,
															 checkStepPct,
															 (moveGoal.NavType == Navigation.Ground) ? AI_MoveLimitFlags.TwoD : AI_MoveLimitFlags.Default,
															 out moveGoal.DirectTrace);
					if (expectingArrival)
						moveGoal.ThinkTrace = moveGoal.DirectTrace;
				}
			}

			moveGoal.HasTraced = true;

			float distClear = checkDist - moveGoal.DirectTrace.DistObstructed;
			if (distClear < 0.001)
				distClear = 0;

			if (traceClear) {
				result = AIMoveResult.Ok;
				retVal = true;
				LastWasClear = true;
			}
			else if ((moveGoal.Flags & (AILocalMoveGoalFlags.TargetIsTransition | AILocalMoveGoalFlags.TargetIsGoal)) != 0 &&
				 moveGoal.MaxDist < distClear) {
				result = AIMoveResult.Ok;
				retVal = true;
				LastWasClear = true;
			}
			else {
				distClearOut = distClear;
				LastWasClear = false;
			}
		}
		else {
			result = AIMoveResult.Illegal;
			retVal = true;
		}

		LastMoveGoal = moveGoal;
		if (retVal && FullDirectTimer.Expired())
			FullDirectTimer.Set(TIME_DELAY_FULL_DIRECT_PROBE[AIStrongOpt() ? 1 : 0]);

		return retVal;
	}

	protected bool MoveCalcSteer(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		if ((moveGoal.Flags & AILocalMoveGoalFlags.NoSteer) != 0)
			return false;

		if (ai_no_steer.GetBool())
			return false;

		if (GetOuter()!.IsFlaggedEfficient())
			return false;

		if (PlaneSolver.Solve(moveGoal, distClear, out Vector3 moveSolution)) {
			if (moveSolution != moveGoal.Dir) {
				float dot = moveSolution.X * moveGoal.Dir.X + moveSolution.Y * moveGoal.Dir.Y;

				const float COS_HALF_30 = 0.966f;
				if (dot > COS_HALF_30) {
					float probeDist = PlaneSolver.CalcProbeDist(moveGoal.Speed);
					if (moveGoal.MaxDist < probeDist * 0.33333 && distClear > probeDist * 0.6666) {
						result = AIMoveResult.Ok;
						return true;
					}
				}

				moveGoal.Facing = moveGoal.Dir = moveSolution;
			}
			result = AIMoveResult.Ok;
			return true;
		}

		return false;
	}

	protected static bool MoveCalcStop(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		if (distClear < moveGoal.MaxDist) {
			if (distClear < 0.1)
				result = AIMoveResult.Illegal;
			else {
				moveGoal.MaxDist = distClear;
				result = AIMoveResult.Ok;
			}

			return true;
		}
		result = AIMoveResult.Ok;
		return true;
	}

	protected AIMoveResult MoveCalcRaw(ref AILocalMoveGoal moveGoal, bool onlyCurThink) {
		AIMoveResult result = AIMoveResult.Ok;
		float distClear = 0;

		bool directClear = MoveCalcDirect(ref moveGoal, onlyCurThink, ref distClear, ref result);
		if (OnCalcBaseMove(ref moveGoal, distClear, ref result))
			return result;

		bool shouldSteer = (moveGoal.Flags & AILocalMoveGoalFlags.NoSteer) == 0 && (!directClear || HaveObstacles());

		if (directClear && !shouldSteer)
			return result;

		if (shouldSteer) {
			if (!directClear) {
				if (OnObstructionPreSteer(ref moveGoal, distClear, ref result))
					return result;
			}

			if (MoveCalcSteer(ref moveGoal, distClear, ref result))
				return result;
		}

		if (OnFailedSteer(ref moveGoal, distClear, ref result))
			return result;

		if (OnFailedLocalNavigation(ref moveGoal, distClear, ref result))
			return result;

		if (distClear < GetOuter()!.GetMotor()!.MinStoppingDist()) {
			if (OnInsufficientStopDist(ref moveGoal, distClear, ref result))
				return result;

			if (MoveCalcStop(ref moveGoal, distClear, ref result))
				return result;
		}

		if (distClear > moveGoal.CurExpectedDist)
			return AIMoveResult.Ok;

		return IsMoveBlocked(moveGoal.DirectTrace.Status) ? moveGoal.DirectTrace.Status : AIMoveResult.Illegal;
	}

	public AIMoveResult MoveCalc(ref AILocalMoveGoal moveGoal, bool previouslyValidated = false) {
		bool onlyCurThink = previouslyValidated && !HaveObstacles();

		AIMoveResult result = MoveCalcRaw(ref moveGoal, onlyCurThink);

		if (moveGoal.CurExpectedDist > moveGoal.MaxDist)
			moveGoal.CurExpectedDist = moveGoal.MaxDist;

		if (result == AIMoveResult.Ok) {
			float interval = GetOuter()!.GetMotor()!.GetMoveInterval();
			float currentYaw = Util.AngleMod(GetOuter()!.GetLocalAngles().Y);
			float goalYaw;
			float deltaYaw;
			float speed;
			float clampedYaw;

			goalYaw = Util.VecToYaw(moveGoal.Facing);
			deltaYaw = MathF.Abs(Util.AngleDiff(goalYaw, currentYaw));
			if (deltaYaw > 15) {
				speed = deltaYaw * 4.0f;
				clampedYaw = AI_Motor.AI_ClampYaw(speed, currentYaw, goalYaw, interval);

				if (clampedYaw != goalYaw)
					moveGoal.Facing = Util.YawToVector(clampedYaw);
			}
		}

		return result;
	}

	public AI_MoveProbe? MoveProbe;
	public IAI_MovementSink? Proxied;

	bool LastWasClear;
	AILocalMoveGoal LastMoveGoal;
	readonly SimpleSimTimer FullDirectTimer = new();
	readonly AI_PlaneSolver PlaneSolver;
}
