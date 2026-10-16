using Game.Shared;

using Source;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Server;

public class AI_Motor : AI_Component, IAI_MovementSink
{
	public AI_Motor(AI_BaseNPC? outer) : base(outer) {
		MoveInterval = 0;

		IdealYaw = 0;
		YawSpeed = 0;
		Velocity = new(0, 0, 0);
		MoveProbe = null;
		YawLocked = false;
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

	public const float AI_CALC_YAW_SPEED = -1;
	public const float AI_KEEP_YAW_SPEED = -2;

	public float GetMoveInterval() => MoveInterval;
	public float SetMoveInterval(float interval) => MoveInterval = interval;

	public AIMotorMoveResult MoveGroundStep(in Vector3 newPos, BaseEntity? moveTarget = null, float yaw = -1, bool asFarAsCan = true, bool testZ = true)
		=> MoveGroundStep(newPos, moveTarget, yaw, asFarAsCan, testZ, out _);

	public AIMotorMoveResult MoveGroundStep(in Vector3 newPos, BaseEntity? moveTarget, float yaw, bool asFarAsCan, bool testZ, out AIMoveTrace traceResult) {
		AI_TestGroundMoveFlags testFlags = AI_TestGroundMoveFlags.IgnoreFloor;

		if (!testZ)
			testFlags |= AI_TestGroundMoveFlags.TwoD;

		GetMoveProbe()!.TestGroundMove(GetOuter()!.GetLocalOrigin(), newPos, Mask.NPCSolid, testFlags, out AIMoveTrace moveTrace);
		traceResult = moveTrace;

		bool hitTarget = moveTrace.Obstruction != null && moveTarget == moveTrace.Obstruction;

		bool isBlocked = IsMoveBlocked(moveTrace.Status);
		if (!isBlocked || asFarAsCan || hitTarget) {
			Util.SetOrigin(GetOuter()!, moveTrace.EndPosition, true);

			if ((GetOuter()!.GetFlags() & EntityFlags.OnGround) != 0)
				GetOuter()!.PhysicsStepRecheckGround();

			if (moveTrace.StepUpDistance > 0.1f) {
				float height = Math.Clamp(moveTrace.StepUpDistance, 0.0f, StepHeight());
				IPhysicsObject? physicsObject = GetOuter()!.VPhysicsGetObject();
				if (physicsObject != null) {
					IPhysicsShadowController? shadow = physicsObject.GetShadowController();
					shadow?.StepUp(height);
				}
			}
			if (yaw != -1) {
				QAngle angles = GetOuter()!.GetLocalAngles();
				angles.Y = yaw;
				GetOuter()!.SetLocalAngles(angles);
			}
			if (hitTarget)
				return AIMotorMoveResult.PartialHitTarget;

			if (!isBlocked)
				return AIMotorMoveResult.Success;

			if (moveTrace.Status == AIMoveResult.BlockedNPC)
				return AIMotorMoveResult.PartialHitNPC;

			return AIMotorMoveResult.PartialHitWorld;
		}
		return AIMotorMoveResult.Failed;
	}

	public virtual void MoveClimbStart(in Vector3 climbDest, in Vector3 climbDir, float climbDist, float yaw) {
		if (MathF.Abs(climbDir.Z) < .1)
			GetOuter()!.SetActivity(GetNavigator()!.GetMovementActivity());
		else
			GetOuter()!.SetActivity((climbDir.Z > -0.01) ? Activity.ACT_CLIMB_UP : Activity.ACT_CLIMB_DOWN);

		DismountSequence = SelectWeightedSequence(Activity.ACT_CLIMB_DISMOUNT);
		if (DismountSequence != (int)Activity.ACT_INVALID)
			GetOuter()!.GetSequenceLinearMotion(DismountSequence, out Dismount);
		else
			Dismount = default;

		GetOuter()!.AddFlag(EntityFlags.Fly);
		GetOuter()!.SetSolid(SolidType.BBox);
		GetOuter()!.SetGravity(0.0f);
		GetOuter()!.SetGroundEntity(null);
	}

	public virtual AIMoveResult MoveClimbExecute(in Vector3 climbDest, in Vector3 climbDir, float climbDist, float yaw, int climbNodesLeft) {
		if (MathF.Abs(climbDir.Z) > .1) {
			if (GetOuter()!.GetActivity() != Activity.ACT_CLIMB_DISMOUNT) {
				Activity desiredActivity = (climbDir.Z > -0.01) ? Activity.ACT_CLIMB_UP : Activity.ACT_CLIMB_DOWN;
				if (GetOuter()!.GetActivity() != desiredActivity)
					GetOuter()!.SetActivity(desiredActivity);
			}

			if (GetOuter()!.GetActivity() != Activity.ACT_CLIMB_UP && GetOuter()!.GetActivity() != Activity.ACT_CLIMB_DOWN && GetOuter()!.GetActivity() != Activity.ACT_CLIMB_DISMOUNT) {
				DevMsg("Climber not in a climb activity!\n");
				return AIMoveResult.Illegal;
			}

			if (DismountSequence != (int)Activity.ACT_INVALID) {
				if (GetOuter()!.GetActivity() == Activity.ACT_CLIMB_UP) {
					if (climbNodesLeft <= 2 && climbDist < MathF.Abs(Dismount.Z)) {
						GetOuter()!.SetActivity(Activity.ACT_CLIMB_DISMOUNT);
						GetOuter()!.SetCycle(GetOuter()!.GetMovementFrame(Dismount.Z - climbDist));
					}
				}
			}
		}

		float climbSpeed = GetOuter()!.GetInstantaneousVelocity();

		if (DismountSequence != (int)Activity.ACT_INVALID)
			climbSpeed = Math.Max(climbSpeed, 30.0f);
		else
			climbSpeed = 100.0f;

		SetSmoothedVelocity(climbDir * climbSpeed);

		if (climbDist < climbSpeed * GetMoveInterval()) {
			if (climbDist <= 1e-2)
				climbDist = 0;

			float climbTime = climbDist / climbSpeed;

			SetMoveInterval(GetMoveInterval() - climbTime);
			GetOuter()!.SetLocalOrigin(climbDest);

			return AIMoveResult.ChangeType;
		}
		else
			SetMoveInterval(0);

		SetIdealYawAndUpdate(yaw);

		return AIMoveResult.Ok;
	}

	public virtual void MoveClimbStop() {
		if (GetNavigator()!.GetMovementActivity() > Activity.ACT_RESET)
			GetOuter()!.SetActivity(GetNavigator()!.GetMovementActivity());
		else
			GetOuter()!.SetActivity(Activity.ACT_IDLE);

		GetOuter()!.RemoveFlag(EntityFlags.Fly);
		SetSmoothedVelocity(vec3_origin);
		GetOuter()!.SetGravity(1.0f);
	}

	public virtual void MoveJumpStart(in Vector3 velocity) {
		SetSmoothedVelocity(velocity);
		GetOuter()!.SetGravity(GetOuter()!.GetJumpGravity());
		GetOuter()!.SetGroundEntity(null);

		GetOuter()!.SetActivity(Activity.ACT_JUMP);

		SetIdealYawAndUpdate(velocity);
	}

	public virtual AIMoveResult MoveJumpExecute() {
		UpdateYaw();

		if (GetOuter()!.GetActivity() == Activity.ACT_JUMP && GetOuter()!.IsActivityFinished())
			GetOuter()!.SetActivity(Activity.ACT_GLIDE);

		SetMoveInterval(0);

		return AIMoveResult.Ok;
	}

	public virtual AIMoveResult MoveJumpStop() {
		SetSmoothedVelocity(new(0, 0, 0));

		if (GetOuter()!.GetActivity() == Activity.ACT_GLIDE) {
			float time = GetOuter()!.GetGroundChangeTime();
			GetOuter()!.AddStepDiscontinuity(time, GetOuter()!.GetAbsOrigin(), GetOuter()!.GetAbsAngles());

			if (SelectWeightedSequence(Activity.ACT_LAND) == (int)Activity.ACT_INVALID)
				return AIMoveResult.ChangeType;

			GetOuter()!.SetActivity(Activity.ACT_LAND);
		}
		if (GetOuter()!.GetActivity() != Activity.ACT_LAND || GetOuter()!.IsActivityFinished())
			return AIMoveResult.ChangeType;

		SetMoveInterval(0);

		GetOuter()!.SetGravity(1.0f);

		return AIMoveResult.Ok;
	}

	public float GetIdealSpeed() => GetOuter()!.GetIdealSpeed();
	public float GetIdealAccel() => GetOuter()!.GetIdealAccel();
	public float GetCurSpeed() => Velocity.Length();
	public ref readonly Vector3 GetCurVel() => ref Velocity;

	public virtual float OverrideMaxYawSpeed(Activity activity) => -1;
	public bool IsDeceleratingToGoal() => false;

	public virtual float MinStoppingDist(float minResult = 10.0f) {
		float decelRate = GetIdealAccel();

		if (decelRate > 0.0) {
			float t = GetCurSpeed() / decelRate;
			float dist = GetCurSpeed() * t - 0.5f * decelRate * t * t;

			if (dist > minResult)
				return dist;
			return minResult;
		}
		return minResult;
	}

	protected float IdealVelocity() => GetIdealSpeed() * (float)GetOuter()!.GetPlaybackRate();

	public virtual void ResetMoveCalculations() { }

	public virtual void MoveStart() { }

	public virtual void MoveStop() {
		Velocity = default;
		GetOuter()!.GetLocalNavigator()!.ResetMoveCalculations();
	}

	public virtual void MovePaused() { }

	public static float DeltaV(float v0, float v1, float d) => (float)(0.5 * (v1 * v1 - v0 * v0) / d);

	public float CalcIntervalMove() => 0.5f * (GetCurSpeed() + GetIdealSpeed()) * GetMoveInterval();

	protected virtual AIMotorMoveResult MoveGroundExecute(in AILocalMoveGoal move, out AIMoveTrace traceResult) {
		MoveFacing(move);

		return MoveGroundExecuteWalk(move, GetIdealSpeed(), CalcIntervalMove(), out traceResult);
	}

	protected AIMotorMoveResult MoveGroundExecuteWalk(in AILocalMoveGoal move, float speed, float dist, out AIMoveTrace traceResult) {
		traceResult = default;
		bool reachingLocalGoal = dist > move.MaxDist;

		if (reachingLocalGoal) {
			if ((move.Flags & AILocalMoveGoalFlags.ConsumeInterval) == 0)
				SetMoveInterval(GetMoveInterval() * (1 - move.MaxDist / dist));
			else
				SetMoveInterval(0);
			dist = move.MaxDist;
		}
		else
			SetMoveInterval(0);

		SetMoveVel(move.Dir * speed);

		AIMotorMoveResult result = AIMotorMoveResult.Success;
		if (dist > 0.0) {
			Vector3 vecFrom = GetOuter()!.GetLocalOrigin();
			Vector3 vecTo = vecFrom + move.Dir * dist;

			result = MoveGroundStep(vecTo, move.MoveTarget, -1, true, reachingLocalGoal, out traceResult);

			if (result == AIMotorMoveResult.Failed)
				MoveStop();
		}
		else if (!OnMoveStalled(move))
			result = AIMotorMoveResult.Failed;

		return result;
	}

	protected virtual AIMotorMoveResult MoveFlyExecute(in AILocalMoveGoal move, out AIMoveTrace traceResult) {
		MoveFacing(move);

		float newSpeed = GetIdealSpeed();
		SetMoveVel(move.Dir * newSpeed);

		float total = 0.5f * (GetCurSpeed() + newSpeed) * GetMoveInterval();

		float distance = move.MaxDist;

		if (total > distance) {
			SetMoveInterval(GetMoveInterval() * (1 - distance / total));
			total = distance;
		}
		else
			SetMoveInterval(0);

		Vector3 vecStart = GetOuter()!.GetLocalOrigin();
		Vector3 vecEnd = vecStart + total * move.Dir;

		GetMoveProbe()!.MoveLimit(Navigation.Fly, vecStart, vecEnd, Mask.NPCSolid, null, out AIMoveTrace moveTrace);
		traceResult = moveTrace;

		if (MathF.Abs(moveTrace.DistObstructed - total) <= 1e-1) {
			if (move.MoveTarget != null && (moveTrace.Obstruction == move.MoveTarget))
				return AIMotorMoveResult.PartialHitTarget;

			return AIMotorMoveResult.Failed;
		}

		Util.SetOrigin(GetOuter()!, moveTrace.EndPosition, true);

		return IsMoveBlocked(moveTrace.Status) ? AIMotorMoveResult.PartialHitWorld : AIMotorMoveResult.Success;
	}

	protected virtual void MoveFacing(in AILocalMoveGoal move) {
		if (GetOuter()!.OverrideMoveFacing(move, GetMoveInterval()))
			return;

		float moveYaw = Util.VecToYaw(move.Dir);

		int sequence = GetOuter()!.GetSequence();
		float sequenceMoveYaw = GetOuter()!.GetSequenceMoveYaw(sequence);
		if (sequenceMoveYaw == Animation.NOMOTION)
			sequenceMoveYaw = 0;

		if (!GetOuter()!.HasPoseParameter(sequence, GetOuter()!.LookupPoseMoveYaw()))
			SetIdealYawAndUpdate(Util.AngleMod(moveYaw - sequenceMoveYaw));
		else {
			float influence = GetFacingDirection(out Vector3 dir);
			dir = move.Facing * (1 - influence) + dir * influence;
			MathLib.VectorNormalize(ref dir);

			float idealYaw = Util.AngleMod(Util.VecToYaw(dir));

			SetIdealYawAndUpdate(idealYaw);

			float diff = Util.AngleDiff(moveYaw, GetOuter()!.GetLocalAngles().Y);
			GetOuter()!.SetPoseParameter(GetOuter()!.LookupPoseMoveYaw(), diff);
		}
	}

	public void SetIdealYaw(float idealYaw) => IdealYaw = idealYaw;
	public void SetIdealYaw(in Vector3 facing) => SetIdealYaw(Util.VecToYaw(facing));

	public void SetIdealYawAndUpdate(float idealYaw, float yawSpeed = AI_CALC_YAW_SPEED) {
		SetIdealYaw(idealYaw);
		if (yawSpeed == AI_CALC_YAW_SPEED)
			RecalculateYawSpeed();
		else if (yawSpeed != AI_KEEP_YAW_SPEED)
			SetYawSpeed(yawSpeed);
		UpdateYaw(-1);
	}

	public void SetIdealYawAndUpdate(in Vector3 facing, float yawSpeed = AI_CALC_YAW_SPEED) => SetIdealYawAndUpdate(Util.VecToYaw(facing), yawSpeed);

	public void SetIdealYawToTargetAndUpdate(in Vector3 target, float yawSpeed = AI_CALC_YAW_SPEED) => SetIdealYawAndUpdate(GetOuter()!.CalcIdealYaw(target), yawSpeed);

	public virtual void AddFacingTarget(BaseEntity? target, float importance, float duration, float ramp = 0.0f) => FacingQueue.Add(target, importance, duration, ramp);
	public virtual void AddFacingTarget(in Vector3 position, float importance, float duration, float ramp = 0.0f) => FacingQueue.Add(position, importance, duration, ramp);
	public virtual void AddFacingTarget(BaseEntity? target, in Vector3 position, float importance, float duration, float ramp = 0.0f) => FacingQueue.Add(target, position, importance, duration, ramp);

	public virtual float GetFacingDirection(out Vector3 vecDir) {
		float totalInterest = 0.0f;
		vecDir = new(0, 0, 0);

		int i;

		for (i = 0; i < FacingQueue.Count;) {
			if (!FacingQueue[i].IsActive())
				FacingQueue.RemoveAt(i);
			else
				i++;
		}

		for (i = 0; i < FacingQueue.Count; i++) {
			float interest = FacingQueue[i].Interest();
			Vector3 tmp = FacingQueue[i].GetPosition() - GetOuter()!.GetAbsOrigin();

			MathLib.VectorNormalize(ref tmp);

			vecDir = vecDir * (1 - interest) + tmp * interest;

			totalInterest = 1 - (1 - totalInterest) * (1 - interest);

			MathLib.VectorNormalize(ref vecDir);
		}

		return totalInterest;
	}

	public void SnapYaw() => UpdateYaw(360);

	public void SetYawSpeed(float yawSpeed) => YawSpeed = yawSpeed;
	public float GetYawSpeed() => YawSpeed;

	public virtual void RecalculateYawSpeed() {
		SetYawSpeed(CalcYawSpeed());
	}

	public static float AI_ClampYaw(float yawSpeedPerSec, float current, float target, TimeUnit_t time) {
		if (current != target) {
			float speed = (float)(yawSpeedPerSec * time);
			float move = target - current;

			if (target > current) {
				if (move >= 180)
					move = move - 360;
			}
			else {
				if (move <= -180)
					move = move + 360;
			}

			if (move > 0) {
				if (move > speed)
					move = speed;
			}
			else {
				if (move < -speed)
					move = -speed;
			}

			return Util.AngleMod(current + move);
		}

		return target;
	}

	public virtual void UpdateYaw(int yawSpeed = -1) {
		if (IsYawLocked())
			return;

		GetOuter()!.SetUpdatedYaw();

		float ideal, current, newYaw;

		if (yawSpeed == -1)
			yawSpeed = (int)GetYawSpeed();

		current = Util.AngleMod(GetOuter()!.GetLocalAngles().Y);
		ideal = Util.AngleMod(GetIdealYaw());

		TimeUnit_t dt = Math.Min(0.2, gpGlobals.CurTime - GetOuter()!.GetLastThink(null));

		newYaw = AI_ClampYaw((float)yawSpeed * 10.0f, current, ideal, dt);

		if (newYaw != current) {
			QAngle angles = GetOuter()!.GetLocalAngles();
			angles.Y = newYaw;
			GetOuter()!.SetLocalAngles(angles);
		}
	}

	public float DeltaIdealYaw() {
		float currentYaw;

		currentYaw = Util.AngleMod(GetOuter()!.GetLocalAngles().Y);

		if (currentYaw == GetIdealYaw())
			return 0;

		return Util.AngleDiff(GetIdealYaw(), currentYaw);
	}

	public float GetIdealYaw() => IdealYaw;

	public void SetIdealYawToTarget(in Vector3 target, float noise = 0.0f, float offset = 0.0f) {
		float baseYaw = GetOuter()!.CalcIdealYaw(target);
		baseYaw += offset;
		if (noise > 0) {
			noise *= 0.5f;
			baseYaw += RandomFloat(-noise, noise);
			if (baseYaw < 0)
				baseYaw += 360;
			else if (baseYaw >= 360)
				baseYaw -= 360;
		}
		SetIdealYaw(baseYaw);
	}

	public virtual void MaintainTurnActivity() { }
	public virtual bool AddTurnGesture(float yd) => false;

	public AIMoveResult MoveNormalExecute(in AILocalMoveGoal move) {
		AIMotorMoveResult motorResult;
		AIMoveTrace moveTrace;

		if (move.NavType == Navigation.Ground)
			motorResult = MoveGroundExecute(move, out moveTrace);
		else {
			Assert(move.NavType == Navigation.Fly);
			motorResult = MoveFlyExecute(move, out moveTrace);
		}

		AIMoveResult result = MoveResults[(int)motorResult];

		if (result != AIMoveResult.Ok) {
			OnMoveExecuteFailed(move, moveTrace, motorResult, ref result);
			SetMoveInterval(0);
		}

		return result;
	}

	static readonly AIMoveResult[] MoveResults = [
		AIMoveResult.Illegal,
		AIMoveResult.Ok,
		AIMoveResult.BlockedNPC,
		AIMoveResult.BlockedWorld,
		AIMoveResult.BlockedWorld,
	];

	public virtual float MinCheckDist() {
		float moveDist = GetMoveInterval() * GetIdealSpeed();
		float minDist = Math.Max(MinStoppingDist(), moveDist);
		if (minDist < GetOuter()!.GetHullWidth())
			minDist = GetOuter()!.GetHullWidth();
		return minDist;
	}

	public AI_Navigator? GetNavigator() => GetOuter()!.GetNavigator();
	public int SelectWeightedSequence(Activity activity) => GetOuter()!.SelectWeightedSequence(activity);
	public float GetSequenceGroundSpeed(int sequence) => (float)GetOuter()!.GetSequenceGroundSpeed(sequence);

	protected AI_MoveProbe? GetMoveProbe() => MoveProbe;
	protected void SetSmoothedVelocity(in Vector3 velocity) => GetOuter()!.SetAbsVelocity(velocity);
	protected Vector3 GetSmoothedVelocity() => GetOuter()!.GetSmoothedVelocity();
	protected float StepHeight() => GetOuter()!.StepHeight();
	protected void SetMoveVel(in Vector3 velocity) => Velocity = velocity;

	public bool IsYawLocked() => YawLocked;
	public void SetYawLocked(bool state) => YawLocked = state;

	public float MoveInterval;

	public float IdealYaw;
	public float YawSpeed;

	public Vector3 Velocity;
	public Vector3 AngularVelocity;

	public int DismountSequence;
	public Vector3 Dismount;

	public readonly AI_InterestTarget FacingQueue = new();

	public AI_MoveProbe? MoveProbe;
	public IAI_MovementSink? Proxied;

	public bool YawLocked;
}
