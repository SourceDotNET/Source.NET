using Game.Shared;

using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

public enum AI_SuggestorResult
{
	None,
	Ok,
	Fail
}

public class AI_PlaneSolver
{
	static readonly float[] PLANE_SOLVER_THINK_FREQUENCY = [0.0f, 0.2f];
	static readonly float[] MAX_PROBE_DIST = [10.0f * 12.0f, 8.0f * 12.0f];

	const float DEGREES_POSITIVE_ARC = 270;
	const float DEGREES_POSITIVE_ARC_CLOSE_OBSTRUCTION = 340;
	const int NUM_PROBES = 5;

	const float MIN_ZDIR_TO_RADIUS = 0.1f;

	struct CircleObstacles(in Vector3 center, float radius, BaseEntity? entity, AI_MoveSuggType type)
	{
		public Vector3 Center = center;
		public float Radius = radius;
		public AI_MoveSuggType Type = type;
		public EHANDLE Entity = entity?.GetRefEHandle() ?? new EHANDLE();
	}

	static float Sq(float f) => f * f;

	public AI_PlaneSolver(AI_BaseNPC npc) {
		Npc = npc;
		SolvedPrev = false;
		PrevTarget = new(float.MaxValue, float.MaxValue, float.MaxValue);
		PrevSolution = 0;
		ClosestHaveBeenToCurrent = float.MaxValue;
		TimeLastProgress = float.MaxValue;
		CannotSolveCurrent = false;
		RefreshSamplesTimer = new(PLANE_SOLVER_THINK_FREQUENCY[AIStrongOpt() ? 1 : 0] - 0.05f);
	}

	AI_BaseNPC GetNpc() => Npc;
	AI_Motor GetMotor() => Npc.GetMotor()!;
	ref readonly Vector3 GetLocalOrigin() => ref Npc.GetLocalOrigin();

	static float GetLookaheadTime() => 1.0f;

	bool MoveLimit(Navigation navType, in Vector3 target, bool ignoreTransients, bool checkStep, Mask contents, out AIMoveTrace moveTrace) {
		AI_MoveLimitFlags flags = (navType == Navigation.Ground) ? AI_MoveLimitFlags.TwoD : AI_MoveLimitFlags.Default;

		if (ignoreTransients)
			flags |= AI_MoveLimitFlags.IgnoreTransients;

		AI_MoveProbe probe = Npc.GetMoveProbe()!;
		return probe.MoveLimit(navType, GetLocalOrigin(), target, contents,
			Npc.GetNavTargetEntity(), checkStep ? 100 : 0,
			flags,
			out moveTrace);
	}

	bool MoveLimit(Navigation navType, in Vector3 target, bool ignoreTransients, bool checkStep, out AIMoveTrace moveTrace)
		=> MoveLimit(navType, target, ignoreTransients, checkStep, Mask.NPCSolid, out moveTrace);

	bool DetectUnsolvable(in AILocalMoveGoal goal) {
		float curDistance = new Vector2(goal.Target.X - GetLocalOrigin().X, goal.Target.Y - GetLocalOrigin().Y).Length();
		if (PrevTarget != goal.Target) {
			TimeLastProgress = gpGlobals.CurTime;
			ClosestHaveBeenToCurrent = curDistance;
			CannotSolveCurrent = false;
		}
		else {
			if (CannotSolveCurrent)
				return true;

			if (ClosestHaveBeenToCurrent - curDistance > 0) {
				TimeLastProgress = gpGlobals.CurTime;
				ClosestHaveBeenToCurrent = curDistance;
			}
			else {
				if (gpGlobals.CurTime - TimeLastProgress > 0.75) {
					CannotSolveCurrent = true;
					return true;
				}
			}
		}
		return false;
	}

	float AdjustRegulationWeight(BaseEntity entity, float weight) {
		AI_BaseNPC? blocker = entity.MyNPCPointer();
		if (blocker != null) {
			ref readonly Vector3 ownerVel = ref GetNpc().GetMotor()!.GetCurVel();
			ref readonly Vector3 blockerVel = ref blocker.GetMotor()!.GetCurVel();

			Vector2 velOwnerNorm = new(ownerVel.X, ownerVel.Y);
			Vector2 velBlockerNorm = new(blockerVel.X, blockerVel.Y);

			float speedOwner = MathLib.Vector2DNormalize(ref velOwnerNorm);
			float speedBlocker = MathLib.Vector2DNormalize(ref velBlockerNorm);

			float dot = Vector2.Dot(velOwnerNorm, velBlockerNorm);

			if (speedBlocker > 0) {
				if (dot > 0 && speedBlocker >= speedOwner * 0.9) {
					if (dot > 0.86)
						weight = 0;
					else if (dot > 0.7)
						weight *= Sq(weight);
					else
						weight *= weight;
				}
			}
		}

		return weight;
	}

	float CalculateRegulationWeight(in AIMoveTrace moveTrace, float pctBlocked) {
		float weight;

		if (pctBlocked > 0.9)
			weight = 1;
		else if (pctBlocked < 0.1)
			weight = 0;
		else {
			weight = Sq((pctBlocked - 0.1f) / 0.8f);
			weight = AdjustRegulationWeight(moveTrace.Obstruction!, weight);
		}

		return weight;
	}

	void GenerateSuggestionFromTrace(in AILocalMoveGoal goal, in AIMoveTrace moveTrace, float probeDist, float arcCenter, float arcSpan, int probeOffset) {
		AI_MoveSuggestion suggestion = new();
		AI_MoveSuggType type;

		switch (moveTrace.Status) {
			case AIMoveResult.BlockedEntity: type = AI_MoveSuggType.AvoidObject; break;
			case AIMoveResult.BlockedWorld: type = AI_MoveSuggType.AvoidWorld; break;
			case AIMoveResult.BlockedNPC: type = AI_MoveSuggType.AvoidNPC; break;
			case AIMoveResult.Illegal: type = AI_MoveSuggType.AvoidDanger; break;
			default: type = AI_MoveSuggType.NoKnowledge; AssertMsg(false, "Unexpected mode status"); break;
		}

		if (goal.MoveTarget != null && goal.MoveTarget == moveTrace.Obstruction) {
			suggestion.Set(type, 0,
							arcCenter, arcSpan,
							moveTrace.Obstruction);

			Solver.AddRegulation(suggestion);

			return;
		}

		float clearDist = probeDist - moveTrace.DistObstructed;
		float pctBlocked = 1.0f - (clearDist / probeDist);

		float weight = CalculateRegulationWeight(moveTrace, pctBlocked);

		if (weight < 0.001)
			return;

		if (pctBlocked < 0.5)
			arcSpan *= pctBlocked * 2.0f;

		Vector3 vecToEnd = moveTrace.EndPosition - GetLocalOrigin();
		Vector3 crossProduct;
		bool favorLeft = false, favorRight = false;

		if (moveTrace.Status == AIMoveResult.BlockedNPC) {
			Vector3 vecToOther = moveTrace.Obstruction!.GetLocalOrigin() - GetLocalOrigin();

			crossProduct = Vector3.Cross(vecToEnd, vecToOther);

			favorLeft = crossProduct.Z < 0;
			favorRight = crossProduct.Z > 0;
		}
		else if (moveTrace.HitNormal != vec3_origin) {
			crossProduct = Vector3.Cross(vecToEnd, moveTrace.HitNormal);
			favorLeft = crossProduct.Z > 0;
			favorRight = crossProduct.Z < 0;
		}

		float thirdSpan = arcSpan / 3.0f;
		float favoredWeight = weight * pctBlocked;

		suggestion.Set(type, weight,
						arcCenter, thirdSpan,
						moveTrace.Obstruction);

		Solver.AddRegulation(suggestion);

		suggestion.Set(type, favorRight ? favoredWeight : weight,
						arcCenter - thirdSpan, thirdSpan,
						moveTrace.Obstruction);

		Solver.AddRegulation(suggestion);

		suggestion.Set(type, favorLeft ? favoredWeight : weight,
						arcCenter + thirdSpan, thirdSpan,
						moveTrace.Obstruction);

		Solver.AddRegulation(suggestion);
	}

	static void CalcYawsFromOffset(float yawScanCenter, float spanPerProbe, int probeOffset, out float yawTest, out float yawCenter) {
		if (probeOffset != 0) {
			float sign = (probeOffset > 0) ? 1 : -1;

			yawCenter = yawScanCenter + probeOffset * spanPerProbe;
			if (yawCenter < 0)
				yawCenter += 360;
			else if (yawCenter >= 360)
				yawCenter -= 360;

			yawTest = yawCenter - (sign * spanPerProbe * 0.5f);
			if (yawTest < 0)
				yawTest += 360;
			else if (yawTest >= 360)
				yawTest -= 360;
		}
		else
			yawCenter = yawTest = yawScanCenter;
	}

	void GenerateObstacleNpcs(in AILocalMoveGoal goal, float probeDist) {
		List<AI_BaseNPC> ais = g_AI_Manager.AccessAIs();
		Npc.CollisionProp().WorldSpaceSurroundingBounds(out Vector3 minsSelf, out Vector3 maxsSelf);
		float radiusSelf = new Vector2(minsSelf.X - maxsSelf.X, minsSelf.Y - maxsSelf.Y).Length() * 0.5f;

		Vector3 mins, maxs;

		for (int i = 0; i < g_AI_Manager.NumAIs(); i++) {
			AI_BaseNPC ai = ais[i];
			if (ai != Npc && ai.IsAlive() && (goal.Path == null || ai != goal.Path.GetTarget())) {
				ai.CollisionProp().WorldSpaceSurroundingBounds(out mins, out maxs);
				if (mins.Z < maxsSelf.Z + 12.0 && maxs.Z > minsSelf.Z - 12.0) {
					float radius = new Vector2(mins.X - maxs.X, mins.Y - maxs.Y).Length() * 0.5f;
					float distance = new Vector2(ai.GetAbsOrigin().X - Npc.GetAbsOrigin().X, ai.GetAbsOrigin().Y - Npc.GetAbsOrigin().Y).Length();
					if (distance - radius < radiusSelf + probeDist)
						AddObstacle(ai.WorldSpaceCenter(), radius, ai, AI_MoveSuggType.AvoidNPC);
				}
			}
		}

		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player == null)
				continue;

			player.CollisionProp().WorldSpaceSurroundingBounds(out mins, out maxs);
			if (mins.Z < maxsSelf.Z + 12.0 && maxs.Z > minsSelf.Z - 12.0) {
				float radius = new Vector2(mins.X - maxs.X, mins.Y - maxs.Y).Length();
				float distance = new Vector2(player.GetAbsOrigin().X - Npc.GetAbsOrigin().X, player.GetAbsOrigin().Y - Npc.GetAbsOrigin().Y).Length();
				if (distance - radius < radiusSelf + probeDist)
					AddObstacle(player.WorldSpaceCenter(), radius, player, AI_MoveSuggType.AvoidNPC);
			}
		}
	}

	AI_SuggestorResult GenerateObstacleSuggestion(in AILocalMoveGoal goal, float yawScanCenter, float probeDist, float spanPerProbe, int probeOffset) {
		CalcYawsFromOffset(yawScanCenter, spanPerProbe, probeOffset, out float yawTest, out float arcCenter);

		Vector3 probeDir = Util.YawToVector(yawTest);
		float requiredMovement = goal.Speed * GetMotor().GetMoveInterval();

		AIMoveTrace moveTrace = default;
		bool traceClear = true;
		if (probeDist > requiredMovement) {
			if (!MoveLimit(goal.NavType, GetLocalOrigin() + probeDir * requiredMovement, true, true, out moveTrace)) {
				traceClear = false;
				moveTrace.DistObstructed = (probeDist - requiredMovement) + moveTrace.DistObstructed;
			}
		}

		if (traceClear)
			traceClear = MoveLimit(goal.NavType, GetLocalOrigin() + probeDir * probeDist, true, false, out moveTrace);

		if (!traceClear) {
			GenerateSuggestionFromTrace(goal, moveTrace, probeDist, arcCenter, spanPerProbe, probeOffset);
			return AI_SuggestorResult.Ok;
		}

		return AI_SuggestorResult.None;
	}

	AI_SuggestorResult GenerateObstacleSuggestions(in AILocalMoveGoal goal, in AIMoveTrace directTrace, float distClear, float probeDist, float degreesToProbe, int nProbes) {
		Assert(nProbes % 2 == 1);

		AI_SuggestorResult seekResult = AI_SuggestorResult.None;
		bool newTarget = !SolvedPrev || PrevTarget != goal.Target;

		if (newTarget)
			RefreshSamplesTimer.Force();

		if (PLANE_SOLVER_THINK_FREQUENCY[AIStrongOpt() ? 1 : 0] == 0.0 || RefreshSamplesTimer.Expired()) {
			Solver.ClearRegulations();

			GenerateObstacleNpcs(goal, probeDist);

			if (GenerateCircleObstacleSuggestions(goal, probeDist))
				seekResult = AI_SuggestorResult.Ok;

			float spanPerProbe = degreesToProbe / nProbes;
			int nSideProbes = (nProbes - 1) / 2;
			float yawGoalDir = Util.VecToYaw(goal.Dir);

			int i;

			if (MathF.Abs(probeDist - (distClear + directTrace.DistObstructed)) < 0.1 &&
				 directTrace.Status != AIMoveResult.BlockedNPC) {
				if (directTrace.Status != AIMoveResult.Ok) {
					seekResult = AI_SuggestorResult.Ok;
					GenerateSuggestionFromTrace(goal, directTrace, probeDist, yawGoalDir, spanPerProbe, 0);
				}
			}
			else if (GenerateObstacleSuggestion(goal, yawGoalDir, probeDist, spanPerProbe, 0) == AI_SuggestorResult.Ok)
				seekResult = AI_SuggestorResult.Ok;

			bool foundClear = false;

			for (i = 1; i <= nSideProbes; i++) {
				if (!foundClear) {
					AI_SuggestorResult curSeekResult = GenerateObstacleSuggestion(goal, yawGoalDir, probeDist, spanPerProbe, i);
					if (curSeekResult == AI_SuggestorResult.Ok)
						seekResult = AI_SuggestorResult.Ok;
					else
						foundClear = true;
				}
				else {
					CalcYawsFromOffset(yawGoalDir, spanPerProbe, i, out _, out float arcCenter);
					Solver.AddRegulation(new AI_MoveSuggestion(AI_MoveSuggType.NoKnowledge, 1, arcCenter, spanPerProbe));
				}
			}

			foundClear = false;

			for (i = -1; i >= -nSideProbes; i--) {
				if (!foundClear) {
					AI_SuggestorResult curSeekResult = GenerateObstacleSuggestion(goal, yawGoalDir, probeDist, spanPerProbe, i);
					if (curSeekResult == AI_SuggestorResult.Ok)
						seekResult = AI_SuggestorResult.Ok;
					else
						foundClear = true;
				}
				else {
					CalcYawsFromOffset(yawGoalDir, spanPerProbe, i, out _, out float arcCenter);
					Solver.AddRegulation(new AI_MoveSuggestion(AI_MoveSuggType.NoKnowledge, 1, arcCenter, spanPerProbe));
				}
			}

			if (seekResult == AI_SuggestorResult.Ok) {
				float arcCenter = yawGoalDir - 180;
				if (arcCenter < 0)
					arcCenter += 360;

				Solver.AddRegulation(new AI_MoveSuggestion(AI_MoveSuggType.NoKnowledge, 1, arcCenter, 360 - degreesToProbe));
			}

			RefreshSamplesTimer.Reset(PLANE_SOLVER_THINK_FREQUENCY[AIStrongOpt() ? 1 : 0]);
		}
		else if (Solver.HaveRegulations())
			seekResult = AI_SuggestorResult.Ok;

		return seekResult;
	}

	void AdjustSolutionForFliers(in AILocalMoveGoal goal, float solutionYaw, ref Vector3 solution) {
		Assert(solution.Z == 0.0f);

		Vector3 vecDelta = goal.Target - GetLocalOrigin();
		Vector3 vecDir = vecDelta;
		MathLib.VectorNormalize(ref vecDir);
		float radius = MathF.Sqrt(vecDir.X * vecDir.X + vecDir.Y * vecDir.Y);
		solution *= radius;
		solution.Z = vecDir.Z;

		float upAmount;
		if (vecDir.Z >= -radius * MIN_ZDIR_TO_RADIUS)
			upAmount = 1.0f;
		else if ((vecDir.Z <= -radius) || (MathF.Abs(vecDir.Z) < 1e-3f))
			upAmount = 0.0f;
		else {
			upAmount = (-radius / vecDir.Z) - 1.0f;
			upAmount *= MIN_ZDIR_TO_RADIUS;
			Assert((upAmount >= 0.0f) && (upAmount <= 1.0f));
		}

		solution.Z += upAmount * 5.0f;

		MathLib.VectorNormalize(ref solution);
	}

	static AI_MoveSuggestionFlags ComputeTurnBiasFlags(in AILocalMoveGoal goal, in AIMoveTrace directTrace) {
		if (directTrace.Status == AIMoveResult.BlockedWorld)
			return 0;

		if (directTrace.Status == AIMoveResult.BlockedNPC)
			return AI_MoveSuggestionFlags.FavorLeft;

		return 0;
	}

	bool RunMoveSolver(in AILocalMoveGoal goal, in AIMoveTrace directTrace, float degreesPositiveArc, bool deterOscillation, out Vector3 result) {
		result = default;

		if (Solver.HaveRegulations()) {
			bool newTarget = !SolvedPrev || PrevTarget != goal.Target;

			Span<AI_MoveSuggestion> moveSuggestions = [new(), new()];
			int nSuggestions = 1;

			moveSuggestions[0].Set(AI_MoveSuggType.Move, 1, Util.VecToYaw(goal.Dir), degreesPositiveArc);
			moveSuggestions[0].Flags |= ComputeTurnBiasFlags(goal, directTrace);

			if (deterOscillation && !newTarget)
				moveSuggestions[nSuggestions++].Set(AI_MoveSuggType.OscillationDeterrance, 1, PrevSolution - 180, 180);

			if (Solver.Solve(moveSuggestions[..nSuggestions], out AI_MoveSolution solution)) {
				result = Util.YawToVector(solution.Dir);

				if (goal.NavType == Navigation.Fly)
					AdjustSolutionForFliers(goal, solution.Dir, ref result);

				if (!newTarget) {
					float delta = solution.Dir - PrevSolution;
					if (delta < 0)
						delta += 360;
					if (delta > 165 && delta < 195)
						return false;
				}

				PrevSolution = solution.Dir;
				PrevSolutionVector = result;

				Vector3 curVelocity = Npc.GetSmoothedVelocity();
				if (curVelocity != vec3_origin) {
					MathLib.VectorNormalize(ref curVelocity);
					if (!newTarget)
						result = curVelocity * 0.1f + PrevSolutionVector * 0.1f + result * 0.8f;
					else
						result = curVelocity * 0.2f + result * 0.8f;
				}

				return true;
			}
		}
		else {
			if (goal.NavType != Navigation.Fly)
				result = goal.Dir;
			else {
				result = goal.Target - GetLocalOrigin();
				MathLib.VectorNormalize(ref result);
			}
			return true;
		}

		return false;
	}

	public float CalcProbeDist(float speed) {
		float result = GetLookaheadTime() * speed;
		if (result < Npc.GetHullWidth())
			return Npc.GetHullWidth();
		if (result > MAX_PROBE_DIST[AIStrongOpt() ? 1 : 0])
			return MAX_PROBE_DIST[AIStrongOpt() ? 1 : 0];
		return result;
	}

	public void AddObstacle(in Vector3 center, float radius, BaseEntity? entity = null, AI_MoveSuggType type = AI_MoveSuggType.AvoidObject)
		=> Obstacles.Add(new CircleObstacles(center, radius, entity, type));

	public bool HaveObstacles() => Obstacles.Count != 0;

	bool GenerateCircleObstacleSuggestions(in AILocalMoveGoal moveGoal, float probeDist) {
		bool result = false;
		Vector3 npcLoc = Npc.WorldSpaceCenter();

		Npc.CollisionProp().WorldSpaceSurroundingBounds(out Vector3 mins, out Vector3 maxs);
		float radiusNpc = new Vector2(mins.X - maxs.X, mins.Y - maxs.Y).Length() * 0.5f;

		for (int i = 0; i < Obstacles.Count; i++) {
			CircleObstacles obstacle = Obstacles[i];
			BaseEntity? obstacleEntity = obstacle.Entity.Get();

			float zDistTooFar;
			if (obstacleEntity != null && obstacleEntity.CollisionProp() != null) {
				if (obstacleEntity == moveGoal.MoveTarget && (obstacleEntity.IsNPC() || obstacleEntity.IsPlayer()))
					continue;

				obstacleEntity.CollisionProp().WorldSpaceSurroundingBounds(out mins, out maxs);
				zDistTooFar = (maxs.Z - mins.Z) * 0.5f + GetNpc().GetHullHeight() * 0.5f;
			}
			else
				zDistTooFar = GetNpc().GetHullHeight();

			if (MathF.Abs(obstacle.Center.Z - npcLoc.Z) > zDistTooFar)
				continue;

			Vector3 vecToNpc = npcLoc - obstacle.Center;
			vecToNpc.Z = 0;
			float distToObstacleSq = Sq(vecToNpc.X) + Sq(vecToNpc.Y);
			float radius = obstacle.Radius + radiusNpc;

			if (distToObstacleSq > 0.001 && distToObstacleSq < Sq(radius + probeDist)) {
				Vector3 vecToObstacle = vecToNpc * -1;
				float distToObstacle = MathLib.VectorNormalize(ref vecToObstacle);
				float weight;
				float arc;
				float radiusSq = Sq(radius);

				float dot = Vector3.Dot(vecToObstacle, moveGoal.Dir);

				if (dot < 0.0f && distToObstacleSq > radiusSq)
					continue;

				if (radiusSq < distToObstacleSq) {
					Vector3 vecTangent;
					float distToTangent = MathF.Sqrt(distToObstacleSq - radiusSq);

					float oneOverDistToObstacleSq = 1 / distToObstacleSq;

					vecTangent.X = (-distToTangent * vecToNpc.X + radius * vecToNpc.Y) * oneOverDistToObstacleSq;
					vecTangent.Y = (-distToTangent * vecToNpc.Y - radius * vecToNpc.X) * oneOverDistToObstacleSq;
					vecTangent.Z = 0;

					float cosHalfArc = Vector3.Dot(vecToObstacle, vecTangent);
					arc = MathLib.RAD2DEG(MathF.Acos(cosHalfArc)) * 2.0f;
					weight = 1.0f - (distToObstacle - radius) / probeDist;
					if (weight > 0.75)
						arc += (arc * 0.5f) * (weight - 0.75f) / 0.25f;

					Assert(weight >= 0.0 && weight <= 1.0);
				}
				else {
					arc = 210;
					weight = 1.0f;
				}

				if (obstacleEntity != null)
					weight = AdjustRegulationWeight(obstacleEntity, weight);

				AI_MoveSuggestion suggestion = new(obstacle.Type, weight, Util.VecToYaw(vecToObstacle), arc);
				Solver.AddRegulation(suggestion);
				result = true;
			}
		}

		Obstacles.Clear();
		return result;
	}

	public bool Solve(in AILocalMoveGoal goal, float distClear, out Vector3 solution) {
		bool solved = false;
		solution = default;

		if (goal.Speed == 0)
			return false;

		if (DetectUnsolvable(goal))
			return false;

		bool veryClose = distClear < 1.0;
		float degreesPositiveArc = !veryClose ? DEGREES_POSITIVE_ARC : DEGREES_POSITIVE_ARC_CLOSE_OBSTRUCTION;
		float probeDist = CalcProbeDist(goal.Speed);

		if ((goal.Flags & (AILocalMoveGoalFlags.TargetIsTransition | AILocalMoveGoalFlags.TargetIsGoal)) != 0)
			probeDist = Math.Min(goal.MaxDist, probeDist);

		if (GenerateObstacleSuggestions(goal, goal.DirectTrace, distClear, probeDist, degreesPositiveArc, NUM_PROBES) != AI_SuggestorResult.Fail) {
			if (RunMoveSolver(goal, goal.DirectTrace, degreesPositiveArc, !veryClose, out solution)) {
				float requiredMovement = goal.Speed * GetMotor().GetMoveInterval();

				MoveLimit(goal.NavType, GetLocalOrigin() + solution * requiredMovement, false, true, out AIMoveTrace moveTrace);

				if (!IsMoveBlocked(moveTrace))
					solved = true;
				else
					solved = false;
			}
		}

		SolvedPrev = solved && goal.Speed != 0;
		PrevTarget = goal.Target;

		return solved;
	}

	public void Reset() {
		RefreshSamplesTimer.Force();

		SolvedPrev = false;
		PrevTarget = new(float.MaxValue, float.MaxValue, float.MaxValue);
		PrevSolution = 0;
		ClosestHaveBeenToCurrent = float.MaxValue;
		TimeLastProgress = float.MaxValue;
		CannotSolveCurrent = false;
	}

	readonly AI_BaseNPC Npc;

	Vector3 PrevTarget;
	bool SolvedPrev;
	float PrevSolution;
	Vector3 PrevSolutionVector;

	float ClosestHaveBeenToCurrent;
	TimeUnit_t TimeLastProgress;

	bool CannotSolveCurrent;

	readonly SimTimer RefreshSamplesTimer;

	readonly List<CircleObstacles> Obstacles = [];

	readonly AI_MoveSolver Solver = new();
}
