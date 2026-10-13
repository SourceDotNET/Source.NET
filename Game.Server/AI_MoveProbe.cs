global using static Game.Server.AI_MoveProbeGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

public static class AI_MoveProbeGlobals
{
	public static float MOVE_HEIGHT_EPSILON = 0.0625f;

	public static readonly ConVar ai_strong_optimizations_no_checkstand = new("ai_strong_optimizations_no_checkstand", "0");
	public static readonly ConVar ai_moveprobe_debug = new("ai_moveprobe_debug", "0");
	public static readonly ConVar ai_moveprobe_jump_debug = new("ai_moveprobe_jump_debug", "0");

	public static bool g_bAIDebugStep = false;
}

[Flags]
public enum AI_TestGroundMoveFlags
{
	Default = 0,
	IgnoreFloor = 0x01,
	IgnoreInitialStandPos = 0x02,
	TwoD = 0x04,
	DrawResults = 0x08,
}

[Flags]
public enum AI_MoveLimitFlags
{
	Default = 0,
	TwoD = 0x01,
	DrawResults = 0x02,
	IgnoreTransients = 0x04,
	QuickReject = 0x08,
}

public class AI_MoveProbe : AI_Component
{
	const float LOCAL_STEP_SIZE = 16.0f;

	struct CheckStepArgs
	{
		public Vector3 Start;
		public Vector3 StepDir;
		public float StepSize;
		public float StepHeight;
		public float StepDownMultiplier;
		public float MinStepLanding;
		public Mask CollisionMask;
		public StepGroundTest GroundTest;
	}

	struct CheckStepResult
	{
		public Vector3 EndPoint;
		public Vector3 HitNormal;
		public bool StartSolid;
		public BaseEntity? Blocker;
	}

	public AI_MoveProbe(AI_BaseNPC? outer) : base(outer) {
		IgnoreTransientEntities = false;
		TraceListData = null;
	}

	public void TraceLine(in Vector3 start, in Vector3 end, Mask mask, bool useCollisionGroup, out Trace result) {
		CollisionGroup collisionGroup = useCollisionGroup ? GetOuter()!.GetCollisionGroup() : CollisionGroup.None;

		TraceFilterNav traceFilter = new(GetOuter()!, IgnoreTransientEntities, GetOuter(), collisionGroup);

		Util.TraceLine(start, end, mask, ref traceFilter, out result);
	}

	bool CheckStep(in CheckStepArgs args, out CheckStepResult result) {
		Mask collisionMask = args.CollisionMask;
		Vector3 end = args.Start + args.StepSize * args.StepDir;

		result.EndPoint = args.Start;
		result.StartSolid = false;
		result.HitNormal = vec3_origin;
		result.Blocker = null;

		Vector3 stepStart = new(args.Start.X, args.Start.Y, args.Start.Z + MOVE_HEIGHT_EPSILON);
		Vector3 stepEnd = new(end.X, end.Y, args.Start.Z + MOVE_HEIGHT_EPSILON);

		if (g_bAIDebugStep) {
			DebugOverlay.Line(stepStart, stepEnd, 255, 255, 255, true, 5);
			DebugOverlay.Cross3D(stepEnd, 32, 255, 255, 255, true, 5);
		}

		TraceHull(stepStart, stepEnd, collisionMask, out Trace trace);

		if (trace.StartSolid || (trace.Fraction < 1)) {
			Trace stepTrace;

			if (!trace.StartSolid) {
				if (g_bAIDebugStep)
					DebugOverlay.Box(trace.EndPos, GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), 64, 64, 64, 0, 5);

				stepStart = trace.EndPos;

				Vector3 stepUp = stepStart;
				stepUp.Z += args.StepHeight;
				TraceHull(stepStart, stepUp, collisionMask, out stepTrace);

				if (g_bAIDebugStep)
					DebugOverlay.Box(stepTrace.EndPos, GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), 96, 96, 96, 0, 5);

				stepStart = stepTrace.EndPos;
			}
			else
				stepStart.Z += args.StepHeight;

			stepEnd.Z = stepStart.Z;

			TraceHull(stepStart, stepEnd, collisionMask, out stepTrace);
			bool rejectStep = false;

			if (stepTrace.StartSolid || stepTrace.Fraction <= 0.01f) {
				if (trace.StartSolid) {
					result.StartSolid = true;
					result.Blocker = trace.Ent;
					result.HitNormal = trace.Plane.Normal;
					return false;
				}

				rejectStep = true;
			}
			else {
				if (g_bAIDebugStep)
					DebugOverlay.Box(stepTrace.EndPos, GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), 128, 128, 128, 0, 5);

				float landingDistSq = new Vector2(stepEnd.X - stepStart.X, stepEnd.Y - stepStart.Y).LengthSquared();
				float requiredLandingDistSq = args.MinStepLanding * args.MinStepLanding;
				if (landingDistSq < requiredLandingDistSq) {
					Vector3 stepEndWithLanding = stepStart + args.MinStepLanding * args.StepDir;
					TraceHull(stepStart, stepEndWithLanding, collisionMask, out Trace landingTrace);
					if (landingTrace.Fraction < 1) {
						if (g_bAIDebugStep)
							DebugOverlay.Box(landingTrace.EndPos, GetOuter()!.WorldAlignMins() + new Vector3(0, 0, 0.1f), GetOuter()!.WorldAlignMaxs() + new Vector3(0, 0, 0.1f), 255, 0, 0, 0, 5);

						rejectStep = true;
						if (landingTrace.Ent != null)
							result.Blocker = landingTrace.Ent;
					}
				}
				else if (new Vector2(stepTrace.EndPos.X - stepStart.X, stepTrace.EndPos.Y - stepStart.Y).LengthSquared() < requiredLandingDistSq) {
					if (g_bAIDebugStep)
						DebugOverlay.Box(stepTrace.EndPos, GetOuter()!.WorldAlignMins() + new Vector3(0, 0, 0.1f), GetOuter()!.WorldAlignMaxs() + new Vector3(0, 0, 0.1f), 255, 0, 0, 0, 5);

					rejectStep = true;
				}
			}

			if (!rejectStep)
				trace = stepTrace;

			if (trace.Fraction < 1.0f) {
				if (result.Blocker == null)
					result.Blocker = trace.Ent;
				result.HitNormal = trace.Plane.Normal;
			}

			stepEnd = trace.EndPos;
		}

		stepStart = stepEnd;
		stepEnd.Z = args.Start.Z - args.StepHeight * args.StepDownMultiplier - MOVE_HEIGHT_EPSILON;

		TraceHull(stepStart, stepEnd, collisionMask, out trace);

		if (trace.Fraction == 1.0f) {
			if (g_bAIDebugStep)
				DebugOverlay.Box(trace.EndPos, GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), 255, 0, 0, 0, 5);

			Assert(result.EndPoint == args.Start);
			if (GetOuter()!.GetGroundEntity() != null)
				result.Blocker = GetOuter()!.GetGroundEntity();
			else
				result.Blocker = BaseEntity.GetContainingEntity(engine.PEntityOfEntIndex(0));
			return false;
		}

		if (g_bAIDebugStep)
			DebugOverlay.Box(trace.EndPos, GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), 160, 160, 160, 0, 5);

		BaseEntity? floor = trace.Ent;
		if (floor != GetOuter()!.GetGroundEntity() && !CanStandOn(floor)) {
			if (g_bAIDebugStep)
				DebugOverlay.Cross3D(trace.EndPos, 32, 255, 0, 0, true, 5);

			Assert(result.EndPoint == args.Start);
			result.Blocker = floor;
			return false;
		}

		if (trace.EndPos.Z - args.Start.Z > args.StepHeight * 0.5f &&
			((floor!.IsWorld() && trace.HitBox > 0) || floor is PhysicsProp)) {
			if (MathF.Abs(Vector3.Dot(trace.Plane.Normal, new Vector3(1, 0, 0))) > .4f) {
				Assert(result.EndPoint == args.Start);
				result.Blocker = floor;

				if (g_bAIDebugStep)
					DebugOverlay.Cross3D(trace.EndPos, 32, 0, 0, 255, true, 5);
				return false;
			}
		}

		if (args.GroundTest != StepGroundTest.DontCheckGround) {
			bool isFloorFlat = CheckStandPosition(trace.EndPos, collisionMask);
			if (args.GroundTest != StepGroundTest.OnInvalidGround && !isFloorFlat) {
				result.Blocker = floor;

				if (g_bAIDebugStep)
					DebugOverlay.Cross3D(trace.EndPos, 32, 255, 0, 255, true, 5);
				return false;
			}
		}

		result.EndPoint = trace.EndPos;
		result.EndPoint.Z += MOVE_HEIGHT_EPSILON;

		if (g_bAIDebugStep)
			DebugOverlay.Cross3D(trace.EndPos, 32, 0, 255, 0, true, 5);

		return result.Blocker == null;
	}

	public bool TestGroundMove(in Vector3 actualStart, in Vector3 desiredEnd, Mask collisionMask, AI_TestGroundMoveFlags flags, out AIMoveTrace moveTrace)
		=> TestGroundMove(actualStart, desiredEnd, collisionMask, 100, flags, out moveTrace);

	public bool TestGroundMove(in Vector3 actualStart, in Vector3 desiredEnd, Mask collisionMask, float pctToCheckStandPositions, AI_TestGroundMoveFlags flags, out AIMoveTrace moveTrace) {
		moveTrace = default;
		moveTrace.DistObstructed = 0.0f;
		moveTrace.Obstruction = null;
		moveTrace.HitNormal = vec3_origin;
		moveTrace.Status = AIMoveResult.Ok;
		moveTrace.EndPosition = actualStart;
		moveTrace.StepUpDistance = 0;

		moveTrace.TotalDist = ComputePathDirection(Navigation.Ground, actualStart, desiredEnd, out Vector3 moveDir);
		if (moveTrace.TotalDist == 0.0f)
			return true;

		StepGroundTest groundTest;
		if ((flags & AI_TestGroundMoveFlags.IgnoreFloor) != 0 || pctToCheckStandPositions < 0.001f) {
			groundTest = StepGroundTest.DontCheckGround;
			pctToCheckStandPositions = 0;
		}
		else {
			if (pctToCheckStandPositions > 99.999f)
				pctToCheckStandPositions = 100;

			if ((flags & AI_TestGroundMoveFlags.IgnoreInitialStandPos) != 0 || CheckStandPosition(actualStart, collisionMask))
				groundTest = StepGroundTest.OnValidGround;
			else
				groundTest = StepGroundTest.OnInvalidGround;
		}

		if ((flags & AI_TestGroundMoveFlags.DrawResults) != 0 && !CheckStandPosition(actualStart, collisionMask))
			DebugOverlay.Cross3D(actualStart, 16, 128, 0, 0, true, 2.0f);

		float distClear = 0;
		int i;

		CheckStepArgs checkStepArgs;
		CheckStepResult checkStepResult;

		checkStepArgs.Start = actualStart;
		checkStepArgs.StepDir = moveDir;
		checkStepArgs.StepSize = 0;
		checkStepArgs.StepHeight = StepHeight();
		checkStepArgs.StepDownMultiplier = GetOuter()!.GetStepDownMultiplier();
		checkStepArgs.MinStepLanding = GetOuter()!.GetHullWidth() * 0.3333333f;
		checkStepArgs.CollisionMask = collisionMask;
		checkStepArgs.GroundTest = groundTest;

		checkStepResult.EndPoint = actualStart;
		checkStepResult.HitNormal = vec3_origin;
		checkStepResult.Blocker = null;
		checkStepResult.StartSolid = false;

		float distStartToIgnoreGround = (pctToCheckStandPositions == 100) ? moveTrace.TotalDist : moveTrace.TotalDist * (pctToCheckStandPositions * 0.01f);
		Vector3 localOrigin = GetOuter()!.GetLocalOrigin();
		bool tryNavIgnore = (new Vector2(actualStart.X - localOrigin.X, actualStart.Y - localOrigin.Y).LengthSquared() < 0.1f && MathF.Abs(actualStart.Z - localOrigin.Z) < checkStepArgs.StepHeight * 0.5f);

		List<BaseEntity> ignoredEntities = [];

		for (; ; ) {
			float stepSize = Math.Min(LOCAL_STEP_SIZE, moveTrace.TotalDist - distClear);
			if (stepSize < 0.001f)
				break;

			checkStepArgs.StepSize = stepSize;
			if (distClear - distStartToIgnoreGround > 0.001f)
				checkStepArgs.GroundTest = StepGroundTest.DontCheckGround;

			for (i = 0; i < 16; i++) {
				CheckStep(checkStepArgs, out checkStepResult);

				if (!tryNavIgnore || checkStepResult.Blocker == null || !checkStepResult.StartSolid)
					break;

				if (checkStepResult.Blocker.GetMoveType() != Source.MoveType.VPhysics && !checkStepResult.Blocker.IsNPC())
					break;

				if (actualStart != checkStepArgs.Start) {
					tryNavIgnore = false;
					break;
				}

				if (checkStepResult.Blocker.GetMoveType() == Source.MoveType.VPhysics) {
					Vector3 moveDirection = desiredEnd - actualStart;
					MathLib.VectorNormalize(ref moveDirection);

					Vector3 obstacleDir = checkStepResult.Blocker.WorldSpaceCenter() - GetOuter()!.WorldSpaceCenter();
					MathLib.VectorNormalize(ref obstacleDir);

					if (Vector3.Dot(moveDirection, obstacleDir) >= 0)
						break;
				}

				if ((flags & AI_TestGroundMoveFlags.DrawResults) != 0 && checkStepResult.StartSolid && checkStepResult.Blocker.IsNPC()) {
					DebugOverlay.EntityBounds(GetOuter()!, 0, 0, 255, 0, .5f);
					DebugOverlay.EntityBounds(checkStepResult.Blocker, 255, 0, 0, 0, .5f);
				}

				ignoredEntities.Add(checkStepResult.Blocker);
				checkStepResult.Blocker.SetNavIgnore();
			}

			if ((flags & AI_TestGroundMoveFlags.DrawResults) != 0) {
				if (!CheckStandPosition(checkStepResult.EndPoint, collisionMask)) {
					DebugOverlay.Box(checkStepResult.EndPoint, GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), 255, 0, 0, 0, 0.1f);
					DebugOverlay.Cross3D(checkStepResult.EndPoint, 16, 255, 0, 0, true, 0.1f);
				}
				else {
					DebugOverlay.Box(checkStepResult.EndPoint, GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), 0, 255, 0, 0, 0.1f);
					DebugOverlay.Cross3D(checkStepResult.EndPoint, 16, 0, 255, 0, true, 0.1f);
				}
			}

			if (checkStepResult.Blocker != null) {
				distClear += new Vector2(checkStepResult.EndPoint.X - checkStepArgs.Start.X, checkStepResult.EndPoint.Y - checkStepArgs.Start.Y).Length();
				break;
			}

			float dz = checkStepResult.EndPoint.Z - checkStepArgs.Start.Z;
			if (dz < 0)
				dz = 0;

			moveTrace.StepUpDistance += dz;
			distClear += stepSize;
			checkStepArgs.Start = checkStepResult.EndPoint;
		}

		for (i = 0; i < ignoredEntities.Count; i++)
			ignoredEntities[i].ClearNavIgnore();

		moveTrace.EndPosition = checkStepResult.EndPoint;

		if (checkStepResult.Blocker != null) {
			moveTrace.Obstruction = checkStepResult.Blocker;
			moveTrace.HitNormal = checkStepResult.HitNormal;
			moveTrace.Status = AIComputeBlockerMoveResult(checkStepResult.Blocker);
			moveTrace.DistObstructed = moveTrace.TotalDist - distClear;

			if ((flags & AI_TestGroundMoveFlags.DrawResults) != 0)
				DebugOverlay.Box(checkStepResult.EndPoint, GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), 255, 0, 0, 0, 0.5f);

			return false;
		}

		if ((flags & AI_TestGroundMoveFlags.TwoD) == 0) {
			float threshold = Math.Max(0.5f * GetOuter()!.GetHullHeight(), StepHeight() + 0.1f);
			if (MathF.Abs(moveTrace.EndPosition.Z - desiredEnd.Z) > threshold) {
				moveTrace.Obstruction = BaseEntity.GetContainingEntity(engine.PEntityOfEntIndex(0));
				moveTrace.HitNormal = vec3_origin;
				moveTrace.Status = AIMoveResult.BlockedWorld;
				moveTrace.DistObstructed = ComputePathDistance(Navigation.Ground, moveTrace.EndPosition, desiredEnd);
				return false;
			}
		}

		return true;
	}

	void GroundMoveLimit(in Vector3 start, in Vector3 end, Mask collisionMask, BaseEntity? target, AI_TestGroundMoveFlags testGroundMoveFlags, float pctToCheckStandPositions, ref AIMoveTrace trace) {
		Assert(!IsMoveBlocked(trace));

		Vector3 desiredEnd;

		trace.TotalDist = ComputePathDistance(Navigation.Ground, start, end);

		if (!IterativeFloorPoint(start, collisionMask, out Vector3 actualStart)) {
			trace.DistObstructed = trace.TotalDist;
			trace.Obstruction = BaseEntity.GetContainingEntity(engine.PEntityOfEntIndex(0));
			trace.HitNormal = vec3_origin;
			trace.Status = AIMoveResult.BlockedWorld;
			trace.EndPosition = start;
			return;
		}

		if ((testGroundMoveFlags & AI_TestGroundMoveFlags.TwoD) == 0)
			IterativeFloorPoint(end, collisionMask, out desiredEnd);
		else
			desiredEnd = end;

		TestGroundMove(actualStart, desiredEnd, collisionMask, pctToCheckStandPositions, testGroundMoveFlags, out trace);

		bool vehicleMatchesObstruction = false;

		if (target != null) {
			if (target is BaseCombatCharacter ccTarget && ccTarget.IsInAVehicle()) {
				BaseEntity? vehicleEnt = ccTarget.GetVehicleEntity();
				if (vehicleEnt == trace.Obstruction)
					vehicleMatchesObstruction = true;
			}
		}

		if ((target != null && (target == trace.Obstruction)) || vehicleMatchesObstruction) {
			trace.DistObstructed = 0.0f;
			trace.Obstruction = null;
			trace.HitNormal = vec3_origin;
			trace.Status = AIMoveResult.Ok;
		}
	}

	void FlyMoveLimit(in Vector3 start, in Vector3 end, Mask collisionMask, BaseEntity? target, ref AIMoveTrace moveTrace) {
		Assert(!IsMoveBlocked(moveTrace));

		TraceHull(start, end, collisionMask, out Trace tr);

		if (tr.Fraction < 1) {
			BaseEntity? blocker = tr.Ent;
			if (blocker != null) {
				if (target == blocker) {
					moveTrace.EndPosition = tr.EndPos;
					return;
				}

				moveTrace.Obstruction = blocker;
				moveTrace.HitNormal = vec3_origin;
				moveTrace.Status = AIComputeBlockerMoveResult(blocker);
			}
			moveTrace.DistObstructed = ComputePathDistance(Navigation.Fly, tr.EndPos, end);
			moveTrace.EndPosition = tr.EndPos;
			return;
		}

		moveTrace.EndPosition = end;
	}

	void JumpMoveLimit(in Vector3 start, in Vector3 end, Mask collisionMask, BaseEntity? target, ref AIMoveTrace moveTrace) {
		moveTrace.JumpVelocity = Vector3.Zero;

		float dist = ComputePathDistance(Navigation.Jump, start, end);

		if (!IsJumpLegal(start, end, end)) {
			moveTrace.Status = AIMoveResult.Illegal;
			moveTrace.DistObstructed = dist;
			return;
		}

		IterativeFloorPoint(start, collisionMask, out Vector3 from);

		IterativeFloorPoint(end, collisionMask, StepHeight() * 0.5f, out Vector3 to);
		if (!CheckStandPosition(to, collisionMask)) {
			moveTrace.Status = AIMoveResult.Illegal;
			moveTrace.DistObstructed = dist;
			return;
		}

		if (from == to) {
			moveTrace.Status = AIMoveResult.Illegal;
			moveTrace.DistObstructed = dist;
			return;
		}

		if (new Vector2(from.X - to.X, from.Y - to.Y).Length() == 0.0f) {
			moveTrace.Status = AIMoveResult.Illegal;
			moveTrace.DistObstructed = dist;
			return;
		}

		float maxHorzVel = GetOuter()!.GetMaxJumpSpeed();

		Vector3 gravity = new(0, 0, GetCurrentGravity() * GetOuter()!.GetJumpGravity());

		if (gravity.Z < 0.01f) {
			moveTrace.Status = AIMoveResult.Illegal;
			moveTrace.DistObstructed = dist;
			return;
		}

		BaseEntity? obstruction = null;
		AIMoveResult status = AIMoveResult.Illegal;
		float distObstructed = dist;

		float minSuccessfulJumpHeight = 1024.0f;
		float minJumpHeight = 0.0f;
		float minJumpStep = 1024.0f;

		Vector3 rawJumpVel = CalcJumpLaunchVelocity(from, to, gravity.Z, ref minJumpHeight, maxHorzVel, out Vector3 apex);
		float baselineJumpHeight = minJumpHeight;

		do {
			rawJumpVel = CalcJumpLaunchVelocity(from, to, gravity.Z, ref minJumpHeight, maxHorzVel, out apex);

			if (!IsJumpLegal(from, apex, to)) {
				minJumpStep = minJumpStep / 2.0f;
				minJumpHeight = minJumpHeight - minJumpStep;
			}
			else {
				float jumpTime = new Vector2(from.X - to.X, from.Y - to.Y).Length() / new Vector2(rawJumpVel.X, rawJumpVel.Y).Length();
				float timeStep = jumpTime / 10.0f;

				Vector3 test = from;
				bool madeIt = true;

				for (float time = 0; time < jumpTime - 0.01f; time += timeStep) {
					Vector3 nextPos = test + (rawJumpVel - 0.5f * gravity * timeStep) * timeStep;

					TraceHull(test, nextPos, collisionMask, out Trace trace);

					if (trace.StartSolid || trace.Fraction < 0.99f) {
						obstruction = trace.Ent;
						status = AIComputeBlockerMoveResult(obstruction!);
						distObstructed = ComputePathDistance(Navigation.Jump, test, to);

						if (trace.Plane.Normal.Z < 0.0f) {
							minJumpStep = minJumpStep / 2.0f;
							minJumpHeight = minJumpHeight - minJumpStep;
						}
						else {
							minJumpStep = minJumpStep / 2.0f;
							minJumpHeight += minJumpStep;
						}

						if (ai_moveprobe_jump_debug.GetBool())
							DebugOverlay.Line(test, nextPos, 255, 0, 0, true, 2.0f);

						madeIt = false;
						break;
					}
					else {
						if (ai_moveprobe_jump_debug.GetBool())
							DebugOverlay.Line(test, nextPos, 0, 255, 0, true, 2.0f);
					}

					rawJumpVel = rawJumpVel - gravity * timeStep;
					test = nextPos;
				}

				if (madeIt) {
					minSuccessfulJumpHeight = minJumpHeight;
					minJumpStep = minJumpStep / 2.0f;
					minJumpHeight -= minJumpStep;
				}
			}
		}
		while (minJumpHeight > baselineJumpHeight && minJumpHeight <= 1024.0f && minJumpStep >= 16.0f);

		if (minSuccessfulJumpHeight != 1024.0f)
			moveTrace.JumpVelocity = CalcJumpLaunchVelocity(from, to, gravity.Z, ref minSuccessfulJumpHeight, maxHorzVel, out apex);
		else {
			moveTrace.Obstruction = obstruction;
			moveTrace.HitNormal = vec3_origin;
			moveTrace.Status = status;
			moveTrace.DistObstructed = distObstructed;
		}
	}

	void ClimbMoveLimit(in Vector3 start, in Vector3 end, BaseEntity? target, ref AIMoveTrace moveTrace) {
		TraceHull(start, end, Mask.NPCSolid, out Trace tr);

		if (tr.Fraction < 1.0f) {
			BaseEntity? entity = tr.Ent;
			if (entity == target)
				return;
			else {
				moveTrace.Obstruction = entity;
				moveTrace.HitNormal = vec3_origin;
				moveTrace.Status = AIComputeBlockerMoveResult(entity!);

				float dist = (1.0f - tr.Fraction) * ComputePathDistance(Navigation.Climb, start, end);
				if (dist <= 0.001f)
					dist = 0.001f;
				moveTrace.DistObstructed = dist;
				return;
			}
		}
	}

	public bool MoveLimit(Navigation navType, in Vector3 start, in Vector3 end, Mask collisionMask, BaseEntity? target, out AIMoveTrace trace)
		=> MoveLimit(navType, start, end, collisionMask, target, 100.0f, AI_MoveLimitFlags.Default, out trace);

	public bool MoveLimit(Navigation navType, in Vector3 start, in Vector3 end, Mask collisionMask, BaseEntity? target, float pctToCheckStandPositions, out AIMoveTrace trace)
		=> MoveLimit(navType, start, end, collisionMask, target, pctToCheckStandPositions, AI_MoveLimitFlags.Default, out trace);

	public bool MoveLimit(Navigation navType, in Vector3 start, in Vector3 end, Mask collisionMask, BaseEntity? target, float pctToCheckStandPositions, AI_MoveLimitFlags flags, out AIMoveTrace trace) {
		trace = default;
		trace.TotalDist = ComputePathDistance(navType, start, end);
		trace.DistObstructed = 0.0f;
		trace.Obstruction = null;
		trace.HitNormal = vec3_origin;
		trace.Status = AIMoveResult.Ok;
		trace.EndPosition = start;

		switch (navType) {
			case Navigation.Ground: {
					AI_TestGroundMoveFlags testGroundMoveFlags = AI_TestGroundMoveFlags.Default;
					if ((flags & AI_MoveLimitFlags.TwoD) != 0)
						testGroundMoveFlags |= AI_TestGroundMoveFlags.TwoD;
					if ((flags & AI_MoveLimitFlags.DrawResults) != 0)
						testGroundMoveFlags |= AI_TestGroundMoveFlags.DrawResults;
					if (ai_moveprobe_debug.GetBool() && (GetOuter()!.DebugOverlays & DebugOverlayBits.NPCSelected) != 0)
						testGroundMoveFlags |= AI_TestGroundMoveFlags.DrawResults;

					if ((flags & AI_MoveLimitFlags.IgnoreTransients) != 0)
						IgnoreTransientEntities = true;

					bool doIt = true;
					if ((flags & AI_MoveLimitFlags.QuickReject) != 0) {
						Assert(start == GetOuter()!.GetLocalOrigin());
						TraceLine(GetOuter()!.EyePosition(), end, collisionMask, true, out Trace tr);
						doIt = tr.Fraction > 0.99f;
					}

					if (doIt)
						GroundMoveLimit(start, end, collisionMask, target, testGroundMoveFlags, pctToCheckStandPositions, ref trace);
					else {
						trace.Obstruction = BaseEntity.GetContainingEntity(engine.PEntityOfEntIndex(0));
						trace.HitNormal = vec3_origin;
						trace.Status = AIMoveResult.BlockedWorld;
						trace.DistObstructed = ComputePathDistance(Navigation.Ground, start, end);
					}

					IgnoreTransientEntities = false;

					break;
				}

			case Navigation.Fly:
				FlyMoveLimit(start, end, collisionMask, target, ref trace);
				break;

			case Navigation.Jump:
				JumpMoveLimit(start, end, collisionMask, target, ref trace);
				break;

			case Navigation.Climb:
				ClimbMoveLimit(start, end, target, ref trace);
				break;

			default:
				trace.Status = AIMoveResult.Illegal;
				trace.DistObstructed = ComputePathDistance(navType, start, end);
				break;
		}

		if (IsMoveBlocked(trace.Status) && trace.Obstruction != null && !trace.Obstruction.IsWorld())
			LastBlockingEnt.Set(trace.Obstruction);

		return !IsMoveBlocked(trace.Status);
	}

	public Vector3 CalcJumpLaunchVelocity(in Vector3 startPos, in Vector3 endPos, float gravity, ref float minHeight, float maxHorzVelocity, out Vector3 apex) {
		float stepHeight = endPos.Z - startPos.Z;

		Vector3 targetDir2D = endPos - startPos;
		targetDir2D.Z = 0;
		float distance = targetDir2D.Length();
		if (distance != 0.0f)
			targetDir2D /= distance;

		Assert(maxHorzVelocity > 0);

		float minHorzTime = distance / maxHorzVelocity;
		float minHorzHeight = 0.5f * gravity * (minHorzTime * 0.5f) * (minHorzTime * 0.5f);

		minHeight = Math.Max(minHeight, minHorzHeight);
		minHeight = Math.Max(minHeight, stepHeight);

		float t0 = MathF.Sqrt((2.0f * minHeight) / gravity);
		float t1 = MathF.Sqrt((2.0f * MathF.Abs(minHeight - stepHeight)) / gravity);

		float velHorz = distance / (t0 + t1);

		Vector3 jumpVel = targetDir2D * velHorz;

		jumpVel.Z = MathF.Sqrt(2.0f * gravity * minHeight);

		apex = startPos + targetDir2D * velHorz * t0 + new Vector3(0, 0, minHeight);

		return jumpVel;
	}

	bool IterativeFloorPoint(in Vector3 start, Mask collisionMask, out Vector3 result) => IterativeFloorPoint(start, collisionMask, 0, out result);

	bool IterativeFloorPoint(in Vector3 start, Mask collisionMask, float addedStep, out Vector3 result)
		=> FloorPoint(start, collisionMask, StepHeight() * GetOuter()!.GetStepDownMultiplier() + addedStep, -(12 * 60), out result);

	bool IsJumpLegal(in Vector3 startPos, in Vector3 apex, in Vector3 endPos) => GetOuter()!.IsJumpLegal(startPos, apex, endPos);

	public bool ShouldBrushBeIgnored(BaseEntity entity) {
		if (entity.Classname == g_iszFuncBrushClassname) {
			FuncBrush funcBrush = (FuncBrush)entity;

			bool nameMatches = funcBrush.ExcludedClass == GetOuter()!.Classname;

			return funcBrush.InvertExclusion ? !nameMatches : nameMatches;
		}

		return false;
	}

	public void TraceHull(in Vector3 start, in Vector3 end, in Vector3 hullMin, in Vector3 hullMax, Mask mask, out Trace result) {
		TraceFilterNav traceFilter = new(GetOuter()!, IgnoreTransientEntities, GetOuter(), GetOuter()!.GetCollisionGroup());

		Ray ray = default;
		ray.Init(start, end, hullMin, hullMax);

		if (TraceListData == null || TraceListData.IsEmpty())
			enginetrace.TraceRay(ray, mask, ref traceFilter, out result);
		else {
			result = default;
			enginetrace.TraceRayAgainstLeafAndEntityList(ray, TraceListData, mask, ref traceFilter, ref result);
		}

		if (r_visualizetraces.GetBool())
			DebugOverlay.DebugDrawLine(result.StartPos, result.EndPos, 255, 255, 0, true, -1.0f);

		Assert(!result.AllSolid || result.StartSolid);
	}

	public void TraceHull(in Vector3 start, in Vector3 end, Mask mask, out Trace result) {
		TraceHull(start, end, GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), mask, out result);
	}

	public bool FloorPoint(in Vector3 start, Mask collisionMask, float startZ, float endZ, out Vector3 result) {
		Vector3 mins = GetOuter()!.WorldAlignMins();
		Vector3 maxs = new(GetOuter()!.WorldAlignMaxs().X, GetOuter()!.WorldAlignMaxs().Y, mins.Z);

		Vector3 vecUp = new(start.X, start.Y, start.Z + startZ + MOVE_HEIGHT_EPSILON);
		Vector3 vecDown = new(start.X, start.Y, start.Z + endZ);

		TraceHull(vecUp, vecDown, mins, maxs, collisionMask, out Trace trace);

		bool startedInObject = false;

		if (trace.StartSolid) {
			if (trace.Ent != null &&
				 (trace.Ent.GetMoveType() == Source.MoveType.VPhysics || trace.Ent.IsNPC()) &&
				 (start - GetOuter()!.GetLocalOrigin()).Length() < 0.1) {
				startedInObject = true;
			}

			vecUp.Z = start.Z + MOVE_HEIGHT_EPSILON;
			TraceHull(vecUp, vecDown, mins, maxs, collisionMask, out trace);
		}

		if (trace.Fraction == 1 || trace.AllSolid || (startedInObject && trace.StartSolid)) {
			result = start;
			if (startedInObject)
				return true;
			return false;
		}

		result = trace.EndPos;
		return true;
	}

	public bool CheckStandPosition(in Vector3 start, Mask collisionMask) {
		if ((GetOuter()!.CapabilitiesGet() & Capability.SkipNavGroundCheck) != 0)
			return true;

		if (ai_strong_optimizations_no_checkstand.GetBool())
			return true;

		Vector3 contactMin, contactMax;

		Vector3 vecUp = new(start.X, start.Y, start.Z + 0.1f);
		Vector3 vecDown = new(start.X, start.Y, start.Z - StepHeight() * GetOuter()!.GetStepDownMultiplier());

		Vector3 hullMins = GetOuter()!.WorldAlignMins();
		Vector3 hullMaxs = GetOuter()!.WorldAlignMaxs();

		if (hullMaxs == vec3_origin && hullMins == hullMaxs) {
			hullMins = GetOuter()!.GetHullMins();
			hullMaxs = GetOuter()!.GetHullMaxs();
		}

		contactMin.X = hullMins.X * 0.75f + hullMaxs.X * 0.25f;
		contactMax.X = hullMins.X * 0.25f + hullMaxs.X * 0.75f;
		contactMin.Y = hullMins.Y * 0.75f + hullMaxs.Y * 0.25f;
		contactMax.Y = hullMins.Y * 0.25f + hullMaxs.Y * 0.75f;
		contactMin.Z = hullMins.Z;
		contactMax.Z = hullMins.Z;

		Trace trace1, trace2;

		if (!GetOuter()!.IsFlaggedEfficient()) {
			Vector3 hullBottomCenter = new(0, 0, hullMins.Z);

			TraceHull(vecUp, vecDown, contactMin, hullBottomCenter, collisionMask, out trace1);
			if (trace1.Fraction != 1.0f && CanStandOn(trace1.Ent)) {
				TraceHull(vecUp, vecDown, hullBottomCenter, contactMax, collisionMask, out trace2);
				if (trace2.Fraction != 1.0f && (trace1.Ent == trace2.Ent || CanStandOn(trace2.Ent)))
					return true;
			}

			Vector3 testMin = new(contactMin.X, 0, hullMins.Z);
			Vector3 testMax = new(0, contactMax.Y, hullMins.Z);

			TraceHull(vecUp, vecDown, testMin, testMax, collisionMask, out trace1);
			if (trace1.Fraction != 1.0f && CanStandOn(trace1.Ent)) {
				testMin = new(0, contactMin.Y, hullMins.Z);
				testMax = new(contactMax.X, 0, hullMins.Z);
				TraceHull(vecUp, vecDown, testMin, testMax, collisionMask, out trace2);
				if (trace2.Fraction != 1.0f && (trace1.Ent == trace2.Ent || CanStandOn(trace2.Ent)))
					return true;
			}
		}
		else {
			TraceHull(vecUp, vecDown, contactMin, contactMax, collisionMask, out trace1);
			if (trace1.Fraction != 1.0f && CanStandOn(trace1.Ent))
				return true;
		}

		return false;
	}

	public bool CanStandOn(BaseEntity? surface) => GetOuter()!.CanStandOn(surface);

	public float StepHeight() => GetOuter()!.StepHeight();

	public void ClearBlockingEntity() => LastBlockingEnt.Set(null);
	public BaseEntity? GetBlockingEntity() => LastBlockingEnt.Get();

	bool IgnoreTransientEntities;
	TraceListData? TraceListData;
	EHANDLE LastBlockingEnt = new();
}
