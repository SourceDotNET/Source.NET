using Game.Shared;

using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Game.Server;

public class AI_BlendedMotor : AI_Motor
{
	public AI_BlendedMotor(AI_BaseNPC? outer) : base(outer) {
		PrimaryLayer = -1;
		PrimarySequence = (int)Activity.ACT_INVALID;

		SecondaryLayer = -1;
		SecondarySequence = (int)Activity.ACT_INVALID;
		SecondaryWeight = 0.0f;

		SavedGoalActivity = Activity.ACT_INVALID;
		SavedTranslatedGoalActivity = Activity.ACT_INVALID;
		GoalSequence = (int)Activity.ACT_INVALID;

		PrevMovementSequence = (int)Activity.ACT_INVALID;
		InteriorSequence = (int)Activity.ACT_INVALID;

		DeceleratingToGoal = false;

		StartCycle = 0.0f;

		PredictiveSpeedAdjust = 1.0f;
		ReactiveSpeedAdjust = 1.0f;
		PrevOrigin1 = Vector3.Zero;
		PrevOrigin2 = Vector3.Zero;

		PrevYaw = 0.0f;
		DoTurn = 0.0f;
		DoLeft = 0.0f;
		DoRight = 0.0f;
		NextTurnAct = 0.0;
	}

	float GetMoveScriptDist(out float newSpeed) {
		int i;
		float totalDist = 0;
		TimeUnit_t t = GetMoveInterval();

		Assert(ScriptMove.Count > 1);

		newSpeed = 0;
		for (i = 0; i < ScriptMove.Count - 1; i++) {
			if (t < ScriptMove[i].Time) {
				TimeUnit_t a = t / ScriptMove[i].Time;
				newSpeed = (float)(ScriptMove[i].MaxVelocity * (1 - a) + ScriptMove[i + 1].MaxVelocity * a);

				totalDist += (float)((ScriptMove[i].MaxVelocity + newSpeed) * 0.5 * t);
				break;
			}
			else {
				newSpeed = ScriptMove[i + 1].MaxVelocity;
				totalDist += ScriptMove[i].Dist;
				t -= ScriptMove[i].Time;
			}
		}

		return totalDist;
	}

	public float GetMoveScriptTotalTime() {
		float dist = GetNavigator()!.GetArrivalDistance();

		int i = ScriptMove.Count - 1;

		if (i < 0)
			return -1;

		while (i > 0 && dist > 1) {
			dist -= ScriptMove[i].Dist;
			i--;
		}
		return (float)ScriptMove[i].ElapsedTime;
	}

	float GetMoveScriptYaw() {
		int i;

		float newYaw = GetOuter()!.GetAbsAngles().Y;
		TimeUnit_t t = GetMoveInterval();
		for (i = 0; i < ScriptTurn.Count - 1; i++) {
			if (t < ScriptTurn[i].Time) {
				TimeUnit_t a = t / ScriptTurn[i].Time;
				float deltaYaw = Util.AngleDiff(ScriptTurn[i + 1].Yaw, ScriptTurn[i].Yaw);
				newYaw = Util.AngleMod((float)(ScriptTurn[i].Yaw + a * deltaYaw));
				break;
			}
			else
				t -= ScriptTurn[i].Time;
		}

		return newYaw;
	}

	void SetMoveScriptAnim(float newSpeed) {
		if (!GetOuter()!.IsAlive())
			return;

		AI_Navigator navigator = GetNavigator()!;

		GetOuter()!.SetPlaybackRate(CurrRate);
		float weight = 0.0f;
		if (GetIdealSpeed() > 0.0f)
			weight = 1.0f - (newSpeed / (float)(GetIdealSpeed() * GetOuter()!.GetPlaybackRate()));
		if (weight < 0.0f) {
			CurrRate = newSpeed / GetIdealSpeed();
			CurrRate = Math.Clamp(CurrRate, 0.0f, 1.0f);
			GetOuter()!.SetPlaybackRate(CurrRate);
			weight = 0.0f;
		}
		CurrRate = (float)Math.Min(CurrRate + (1.0 - CurrRate) * 0.8f, 1.0f);

		if (SavedGoalActivity == Activity.ACT_INVALID)
			ResetGoalSequence();

		Activity activity = GetOuter()!.NPC_TranslateActivity(SavedGoalActivity);
		if (activity != SavedTranslatedGoalActivity) {
			SavedTranslatedGoalActivity = activity;
			InteriorSequence = (int)Activity.ACT_INVALID;
			GoalSequence = navigator.GetArrivalSequence(PrimarySequence);
		}

		if (DeceleratingToGoal) {
			GoalSequence = navigator.GetArrivalSequence(PrimarySequence);

			if (GoalSequence == (int)Activity.ACT_INVALID)
				GoalSequence = GetInteriorSequence(PrimarySequence);

			Assert(GoalSequence != (int)Activity.ACT_INVALID);
		}

		if (SecondaryWeight == 1.0 || (SecondaryLayer != -1 && PrimarySequence == SecondarySequence)) {
			GetOuter()!.RemoveLayer(PrimaryLayer, 0.0f, 0.0f);

			PrimaryLayer = SecondaryLayer;
			PrimarySequence = SecondarySequence;
			SecondaryLayer = -1;
			SecondarySequence = (int)Activity.ACT_INVALID;
			SecondaryWeight = 0.0f;
		}

		if (SecondarySequence == (int)Activity.ACT_INVALID) {
			if (!DeceleratingToGoal && GoalSequence != GetInteriorSequence(PrimarySequence))
				GoalSequence = GetInteriorSequence(PrimarySequence);

			if (GoalSequence != (int)Activity.ACT_INVALID && PrimarySequence != GoalSequence) {
				SecondarySequence = GetOuter()!.FindTransitionSequence(PrimarySequence, GoalSequence);
				if (SecondarySequence == (int)Activity.ACT_INVALID)
					SecondarySequence = GoalSequence;
			}
		}

		if (SecondarySequence != (int)Activity.ACT_INVALID) {
			if (SecondaryLayer == -1) {
				SecondaryLayer = GetOuter()!.AddLayeredSequence(SecondarySequence, 0);
				GetOuter()!.SetLayerWeight(SecondaryLayer, 0.0f);
				if (SecondarySequence == GoalSequence)
					GetOuter()!.SetLayerPlaybackRate(SecondaryLayer, 0.0);
				else
					GetOuter()!.SetLayerPlaybackRate(SecondaryLayer, 1.0);
				GetOuter()!.SetLayerNoRestore(SecondaryLayer, true);
				SecondaryWeight = 0.0f;
			}

			SecondaryWeight = (float)Math.Min(SecondaryWeight + 0.3, 1.0);

			if (SecondaryWeight < 1.0) {
				GetOuter()!.SetLayerWeight(PrimaryLayer, (weight - SecondaryWeight * weight) / (1.0f - SecondaryWeight * weight));
				GetOuter()!.SetLayerWeight(SecondaryLayer, weight * SecondaryWeight);
			}
			else {
				GetOuter()!.SetLayerWeight(PrimaryLayer, 0.0f);
				GetOuter()!.SetLayerWeight(SecondaryLayer, weight);
			}
		}
		else {
			if (PrimaryLayer == -1)
				MoveContinue();

			if (SecondaryLayer != -1) {
				GetOuter()!.RemoveLayer(SecondaryLayer, 0.0f, 0.0f);
				SecondaryLayer = -1;
				SecondarySequence = (int)Activity.ACT_INVALID;
				SecondaryWeight = 0.0f;
			}

			GetOuter()!.SetLayerWeight(PrimaryLayer, weight);
		}
	}

	int GetInteriorSequence(int fromSequence) {
		int sequence = GetNavigator()!.GetMovementSequence();

		if (InteriorSequence != (int)Activity.ACT_INVALID && sequence == PrevMovementSequence)
			return InteriorSequence;

		PrevMovementSequence = sequence;

		KeyValues? seqKeyValues = GetOuter()!.GetSequenceKeyValues(sequence);
		if (seqKeyValues != null) {
			KeyValues? kvInterior = seqKeyValues.FindKey("interior");
			if (kvInterior != null) {
				ReadOnlySpan<char> activityName = kvInterior.GetString();

				Activity activity = GetOuter()!.LookupActivity(activityName);
				if (activity != Activity.ACT_INVALID)
					InteriorSequence = GetOuter()!.SelectWeightedSequence(GetOuter()!.TranslateActivity(activity), fromSequence);
				else {
					activity = (Activity)AI_BaseNPC.GetActivityID(activityName);
					if (activity != Activity.ACT_INVALID)
						InteriorSequence = GetOuter()!.SelectWeightedSequence(GetOuter()!.TranslateActivity(activity), fromSequence);
				}

				if (activity == Activity.ACT_INVALID || InteriorSequence == (int)Activity.ACT_INVALID)
					InteriorSequence = GetOuter()!.LookupSequence(activityName);
			}
		}

		if (InteriorSequence == (int)Activity.ACT_INVALID) {
			Activity activity = GetNavigator()!.GetMovementActivity();
			if (activity == Activity.ACT_WALK_AIM || activity == Activity.ACT_RUN_AIM)
				activity = Activity.ACT_IDLE_ANGRY;
			else
				activity = Activity.ACT_IDLE;
			InteriorSequence = GetOuter()!.SelectWeightedSequence(GetOuter()!.TranslateActivity(activity), fromSequence);

			Assert(InteriorSequence != (int)Activity.ACT_INVALID);
		}

		return InteriorSequence;
	}

	protected override AIMotorMoveResult MoveGroundExecute(in AILocalMoveGoal move, out AIMoveTrace traceResult) {
		if (move.CurExpectedDist < 0.001) {
			AIMotorMoveResult baseResult = base.MoveGroundExecute(move, out traceResult);
			SetMoveScriptAnim(0.0f);
			return baseResult;
		}

		BuildMoveScript(move);

		float totalDist = GetMoveScriptDist(out float newSpeed);

		Assert(move.MaxDist < 0.01 || totalDist > 0.0);

		float newYaw = GetMoveScriptYaw();

		AILocalMoveGoal move2 = move;
		move2.Facing = Util.YawToVector(newYaw);

		MoveFacing(move2);

		GetOuter()!.GroundSpeed = GetSequenceGroundSpeed(GetOuter()!.GetSequence());

		SetMoveScriptAnim(newSpeed);

		AIMotorMoveResult result = MoveGroundExecuteWalk(move, newSpeed, totalDist, out traceResult);

		return result;
	}

	protected override AIMotorMoveResult MoveFlyExecute(in AILocalMoveGoal move, out AIMoveTrace traceResult) {
		if (move.CurExpectedDist < 0.001)
			return base.MoveFlyExecute(move, out traceResult);

		BuildMoveScript(move);

		float totalDist = GetMoveScriptDist(out float newSpeed);

		Assert(move.MaxDist < 0.01 || totalDist > 0.0);

		float newYaw = GetMoveScriptYaw();

		AILocalMoveGoal move2 = move;
		move2.Facing = Util.YawToVector(newYaw);

		MoveFacing(move2);

		GetOuter()!.GroundSpeed = GetSequenceGroundSpeed(GetOuter()!.GetSequence());

		SetMoveScriptAnim(newSpeed);

		bool reachingLocalGoal = totalDist > move.MaxDist;

		if (reachingLocalGoal) {
			if ((move.Flags & AILocalMoveGoalFlags.ConsumeInterval) == 0)
				SetMoveInterval(GetMoveInterval() * (1 - move.MaxDist / totalDist));
			else
				SetMoveInterval(0);
			totalDist = move.MaxDist;
		}
		else
			SetMoveInterval(0);

		SetMoveVel(move.Dir * newSpeed);

		Vector3 vecStart, vecEnd;
		vecStart = GetOuter()!.GetLocalOrigin();
		vecEnd = vecStart + totalDist * move.Dir;

		GetMoveProbe()!.MoveLimit(Navigation.Fly, vecStart, vecEnd, Mask.NPCSolid, null, out AIMoveTrace moveTrace);
		traceResult = moveTrace;

		if (MathF.Abs(moveTrace.DistObstructed - totalDist) <= 1e-1) {
			if (move.MoveTarget != null && (moveTrace.Obstruction == move.MoveTarget))
				return AIMotorMoveResult.PartialHitTarget;

			return AIMotorMoveResult.Failed;
		}

		Util.SetOrigin(GetOuter()!, moveTrace.EndPosition, true);

		return IsMoveBlocked(moveTrace.Status) ? AIMotorMoveResult.PartialHitWorld : AIMotorMoveResult.Success;
	}

	void BuildMoveScript(in AILocalMoveGoal move) {
		ScriptMove.Clear();
		ScriptTurn.Clear();

		BuildVelocityScript(move);
		BuildTurnScript(move);
	}

	const float YAWSPEED = 150;

	ref AI_MovementScript Turn(int i) => ref CollectionsMarshal.AsSpan(ScriptTurn)[i];

	void BuildTurnScript(in AILocalMoveGoal move) {
		int i;

		AI_MovementScript script = default;

		script.Location = GetOuter()!.GetAbsOrigin();
		script.Yaw = GetOuter()!.GetAbsAngles().Y;
		ScriptTurn.Add(script);

		int prev = 0;
		for (i = 0; i < ScriptMove.Count; i++) {
			AI_Waypoint? curWaypoint = ScriptMove[i].Waypoint;
			if (curWaypoint != null) {
				script = default;
				script.Location = curWaypoint.VecLocation;
				script.Waypoint = curWaypoint;
				script.ElapsedTime = ScriptMove[i].ElapsedTime;

				Turn(prev).Time = script.ElapsedTime - ScriptTurn[prev].ElapsedTime;

				if (curWaypoint.GetNext() != null) {
					Vector3 d1 = curWaypoint.GetNext()!.VecLocation - script.Location;
					Vector3 d2 = script.Location - ScriptTurn[prev].Location;

					d1.Z = 0;
					MathLib.VectorNormalize(ref d1);
					d2.Z = 0;
					MathLib.VectorNormalize(ref d2);

					float y1 = Util.VecToYaw(d1);
					float y2 = Util.VecToYaw(d2);

					float deltaYaw = MathF.Abs(Util.AngleDiff(y1, y2));

					if (deltaYaw > 0.1) {
						script.Yaw = MathLib.ApproachAngle(y1, y2, deltaYaw * 0.8f);
						ScriptTurn.Add(script);
						prev++;
					}
				}
				else {
					Vector3 dir = GetNavigator()!.GetArrivalDirection();
					script.Yaw = Util.VecToYaw(dir);
					ScriptTurn.Add(script);
					prev++;
				}
			}
		}

		for (i = ScriptTurn.Count - 1; i > 1; i--) {
			float deltaYaw = Util.AngleDiff(ScriptTurn[i - 1].Yaw, ScriptTurn[i].Yaw);

			float maxYaw = (float)(YAWSPEED * ScriptTurn[i - 1].Time);

			if (MathF.Abs(deltaYaw) > maxYaw)
				Turn(i - 1).Yaw = MathLib.ApproachAngle(ScriptTurn[i - 1].Yaw, ScriptTurn[i].Yaw, maxYaw);
		}

		for (i = 0; i < ScriptTurn.Count - 1;)
			i = i + BuildTurnScript(i, i + 1) + 1;
	}

	int BuildTurnScript(int i, int j) {
		int k;

		Vector3 dir = ScriptTurn[j].Location - ScriptTurn[i].Location;
		float interiorYaw = Util.VecToYaw(dir);

		float deltaYaw;

		deltaYaw = MathF.Abs(Util.AngleDiff(interiorYaw, ScriptTurn[i].Yaw));
		TimeUnit_t t1 = deltaYaw / YAWSPEED;

		deltaYaw = MathF.Abs(Util.AngleDiff(ScriptTurn[j].Yaw, interiorYaw));
		TimeUnit_t t2 = deltaYaw / YAWSPEED;

		TimeUnit_t totalTime = ScriptTurn[j].ElapsedTime - ScriptTurn[i].ElapsedTime;

		Assert(totalTime > 0);

		if (t1 < 0.01) {
			if (t2 > totalTime * 0.8)
				return 0;

			Turn(i).Yaw = interiorYaw;

			k = BuildInsertNode(i, totalTime - t2);
			Turn(k).Yaw = interiorYaw;

			return 1;
		}
		else if (t2 < 0.01) {
			if (t1 > totalTime * 0.8)
				return 0;

			k = BuildInsertNode(i, t1);
			Turn(k).Yaw = interiorYaw;

			return 1;
		}
		else if (t1 + t2 > totalTime)
			return 0;
		else if (t1 + t2 < totalTime * 0.8) {
			k = BuildInsertNode(i, t1);
			Turn(k).Yaw = interiorYaw;

			k = BuildInsertNode(i, t2);
			Turn(k).Yaw = interiorYaw;

			return 2;
		}
		return 0;
	}

	int BuildInsertNode(int i, TimeUnit_t time) {
		AI_MovementScript script = default;

		Assert(time > 0.0);

		for (; i < ScriptTurn.Count - 1; i++) {
			if (ScriptTurn[i].Time < time)
				time -= ScriptTurn[i].Time;
			else {
				TimeUnit_t a = time / ScriptTurn[i].Time;

				script.Time = ScriptTurn[i].Time - time;

				Turn(i).Time = time;

				script.ElapsedTime = ScriptTurn[i].ElapsedTime * (1 - a) + ScriptTurn[i + 1].ElapsedTime * a;

				script.Location = ScriptTurn[i].Location * (float)(1 - a) + ScriptTurn[i + 1].Location * (float)a;

				ScriptTurn.Insert(i + 1, script);

				return i + 1;
			}
		}
		Assert(false);
		return 0;
	}

	public static readonly ConVar ai_path_insert_pause_at_obstruction = new("ai_path_insert_pause_at_obstruction", "1");
	public static readonly ConVar ai_path_adjust_speed_on_immediate_turns = new("ai_path_adjust_speed_on_immediate_turns", "1");
	public static readonly ConVar ai_path_insert_pause_at_est_end = new("ai_path_insert_pause_at_est_end", "1");

	const float MIN_VELOCITY = 0.0f;
	const float MIN_STEER_DOT = 0.0f;

	ref AI_MovementScript Move(int i) => ref CollectionsMarshal.AsSpan(ScriptMove)[i];

	void BuildVelocityScript(in AILocalMoveGoal move) {
		int i;
		float a;

		float idealVelocity = GetIdealSpeed();
		if (idealVelocity == 0)
			idealVelocity = 50;

		float idealAccel = GetIdealAccel();
		if (idealAccel == 0)
			idealAccel = 100;

		AI_MovementScript script = default;

		script.Location = GetOuter()!.GetAbsOrigin();
		script.MaxVelocity = GetCurSpeed();
		ScriptMove.Add(script);

		if (BaseAnimating.npc_height_adjust.GetBool() && move.HasTraced && move.DirectTrace.TotalDist != move.ThinkTrace.TotalDist) {
			float dist = (move.DirectTrace.EndPosition - ScriptMove[0].Location).Length2D();
			float height = move.DirectTrace.EndPosition.Z - ScriptMove[0].Location.Z;
			float delta;

			if (dist > 0)
				delta = height / dist;
			else
				delta = 0;

			PredictiveSpeedAdjust = 1.1f - MathF.Abs(delta);
			PredictiveSpeedAdjust = Math.Clamp(PredictiveSpeedAdjust, (height > 0.0f) ? 0.5f : 0.8f, 1.0f);
		}
		if (BaseAnimating.npc_height_adjust.GetBool()) {
			float dist = (move.ThinkTrace.EndPosition - PrevOrigin2).Length2D();
			float height = move.ThinkTrace.EndPosition.Z - PrevOrigin2.Z;
			float delta;

			if (dist > 0)
				delta = height / dist;
			else
				delta = 0;

			float newSpeedAdjust = 1.1f - MathF.Abs(delta);
			newSpeedAdjust = Math.Clamp(newSpeedAdjust, (height > 0.0f) ? 0.5f : 0.8f, 1.0f);

			if (newSpeedAdjust < ReactiveSpeedAdjust)
				ReactiveSpeedAdjust = ReactiveSpeedAdjust * 0.2f + newSpeedAdjust * 0.8f;
			else
				ReactiveSpeedAdjust = ReactiveSpeedAdjust * 0.5f + newSpeedAdjust * 0.5f;

			PrevOrigin2 = PrevOrigin1;
			PrevOrigin1 = GetOuter()!.GetAbsOrigin();
		}

		idealVelocity = idealVelocity * Math.Min(ReactiveSpeedAdjust, PredictiveSpeedAdjust);

		bool addedExpected = false;

		AI_Waypoint? curWaypoint = GetNavigator()!.GetPath().GetCurWaypoint();

		Assert(curWaypoint != null);

		while (curWaypoint != null && (curWaypoint.NavType() == Navigation.Ground || curWaypoint.NavType() == Navigation.Fly)) {
			script = default;
			AI_Waypoint? next = curWaypoint.GetNext();

			if (ai_path_adjust_speed_on_immediate_turns.GetBool() && !addedExpected) {
				script.Location = GetOuter()!.GetAbsOrigin() + move.Dir * move.CurExpectedDist;
				addedExpected = true;
				next = curWaypoint;
			}
			else {
				script.Location = curWaypoint.VecLocation;
				script.Waypoint = curWaypoint;
			}

			if (next != null) {
				switch (next.NavType()) {
					case Navigation.Ground:
					case Navigation.Fly: {
							Vector3 d1 = next.VecLocation - script.Location;
							Vector3 d2 = script.Location - ScriptMove[^1].Location;

							if (d1.Length2D() < 1.0) {
								curWaypoint = next;
								continue;
							}

							d1.Z = 0;
							MathLib.VectorNormalize(ref d1);
							d2.Z = 0;
							MathLib.VectorNormalize(ref d2);

							float dot = Vector3.Dot(d1, d2) + 0.2f;
							if (dot > 0) {
								dot = Math.Clamp(dot, 0.0f, 1.0f);
								script.MaxVelocity = idealVelocity * dot;
							}
							else
								script.MaxVelocity = 0;
						}
						break;
					case Navigation.Jump: {
							float minJumpHeight = 0;
							float maxHorzVel = Math.Max(GetCurSpeed(), 100);
							float gravity = GetCurrentGravity() * GetOuter()!.GetGravity();
							Vector3 rawJumpVel = GetMoveProbe()!.CalcJumpLaunchVelocity(script.Location, next.VecLocation, gravity, ref minJumpHeight, maxHorzVel, out _);

							script.MaxVelocity = rawJumpVel.Length2D();
						}
						break;
					case Navigation.Climb:
						script.MaxVelocity = 0;
						break;
				}
			}
			else
				script.MaxVelocity = GetNavigator()!.GetArrivalSpeed();

			ScriptMove.Add(script);
			curWaypoint = next;
		}

		float totalDist = 0;
		for (i = 0; i < ScriptMove.Count - 1; i++)
			totalDist += Move(i).Dist = (ScriptMove[i + 1].Location - ScriptMove[i].Location).Length2D();

		if (!DeceleratingToGoal && ScriptMove.Count != 0 && totalDist > 0) {
			float neededAccel = DeltaV(ScriptMove[0].MaxVelocity, ScriptMove[^1].MaxVelocity, totalDist);
			DeceleratingToGoal = neededAccel < -idealAccel;
		}

		if (ai_path_insert_pause_at_obstruction.GetBool() && move.DirectTrace.Obstruction != null) {
			float distToObstruction = (move.DirectTrace.EndPosition - ScriptMove[0].Location).Length2D();

			distToObstruction = distToObstruction + 16;

			InsertSlowdown(distToObstruction, idealAccel, false);
		}

		if (ai_path_insert_pause_at_est_end.GetBool() && GetNavigator()!.GetArrivalDistance() > 0.0)
			InsertSlowdown(totalDist - GetNavigator()!.GetArrivalDistance(), idealAccel, true);

		if (ai_path_adjust_speed_on_immediate_turns.GetBool() && ScriptMove.Count > 1) {
			Vector3 d1 = ScriptMove[1].Location - ScriptMove[0].Location;
			d1.Z = 0;
			MathLib.VectorNormalize(ref d1);

			Vector3 d2 = GetCurVel();
			d2.Z = 0;
			MathLib.VectorNormalize(ref d2);

			float dot = Vector3.Dot(d1, d2) + MIN_STEER_DOT;
			dot = Math.Clamp(dot, 0.0f, 1.0f);
			Move(0).MaxVelocity = ScriptMove[0].MaxVelocity * dot;
		}

		for (i = 0; i < ScriptMove.Count - 1; i++) {
			float dv = ScriptMove[i + 1].MaxVelocity - ScriptMove[i].MaxVelocity;

			if (dv > 0.0) {
				TimeUnit_t t1 = dv / idealAccel;
				float d1 = (float)(ScriptMove[i].MaxVelocity * t1 + 0.5 * idealAccel * t1 * t1);

				if (d1 > ScriptMove[i].Dist) {
					if (MathLib.SolveQuadratic(0.5f * idealAccel, ScriptMove[i].MaxVelocity, -ScriptMove[i].Dist, out float r1, out _))
						Move(i + 1).MaxVelocity = ScriptMove[i].MaxVelocity + idealAccel * r1;
				}
			}
		}

		for (i = ScriptMove.Count - 1; i > 0; i--) {
			float dv = ScriptMove[i].MaxVelocity - ScriptMove[i - 1].MaxVelocity;

			if (dv < 0.0) {
				TimeUnit_t t1 = -dv / idealAccel;
				float d1 = (float)(ScriptMove[i].MaxVelocity * t1 + 0.5 * idealAccel * t1 * t1);

				if (d1 > ScriptMove[i - 1].Dist) {
					if (MathLib.SolveQuadratic(0.5f * idealAccel, ScriptMove[i].MaxVelocity, -ScriptMove[i - 1].Dist, out float r1, out _))
						Move(i - 1).MaxVelocity = ScriptMove[i].MaxVelocity + idealAccel * r1;
				}
			}
		}

		for (i = 0; i < ScriptMove.Count - 1;) {
			TimeUnit_t t1 = (idealVelocity - ScriptMove[i].MaxVelocity) / idealAccel;
			float d1 = (float)(ScriptMove[i].MaxVelocity * t1 + 0.5 * idealAccel * t1 * t1);

			TimeUnit_t t2 = (idealVelocity - ScriptMove[i + 1].MaxVelocity) / idealAccel;
			float d2 = (float)(ScriptMove[i + 1].MaxVelocity * t2 + 0.5 * idealAccel * t2 * t2);

			Move(i).Dist = (ScriptMove[i + 1].Location - ScriptMove[i].Location).Length2D();

			if (d1 + d2 < ScriptMove[i].Dist) {
				Vector3 start = ScriptMove[i].Location;
				Vector3 end = ScriptMove[i + 1].Location;
				float dist = ScriptMove[i].Dist;

				if (d1 > 1.0 && t1 > 0.1) {
					a = d1 / dist;

					script = default;
					script.Location = end * a + start * (1 - a);
					script.MaxVelocity = idealVelocity;
					ScriptMove.Insert(i + 1, script);
					i++;
				}

				if (dist - d2 > 1.0 && t2 > 0.1) {
					a = (dist - d2) / dist;

					script = default;
					script.Location = end * a + start * (1 - a);
					script.MaxVelocity = idealVelocity;
					ScriptMove.Insert(i + 1, script);
					i++;
				}

				i++;
			}
			else {
				float neededAccel = MathF.Abs(DeltaV(ScriptMove[i].MaxVelocity, ScriptMove[i + 1].MaxVelocity, ScriptMove[i].Dist));
				if (neededAccel < idealAccel) {
					float v1 = ScriptMove[i].MaxVelocity;
					float v2 = ScriptMove[i + 1].MaxVelocity;
					float dist = ScriptMove[i].Dist;

					float tmp = (float)(idealAccel * dist + 0.5 * v1 * v1 + 0.5 * v2 * v2);
					Assert(tmp >= 0);
					t1 = (-v1 + Math.Sqrt(tmp)) / idealAccel;
					t2 = (v1 + idealAccel * t1 - v2) / idealAccel;

					if (t1 > 0.0 && t2 > 0.0) {
						d1 = (float)(v1 * t1 + 0.5 * idealAccel * t1 * t1);

						a = d1 / ScriptMove[i].Dist;
						script = default;
						script.Location = ScriptMove[i + 1].Location * a + ScriptMove[i].Location * (1 - a);
						script.MaxVelocity = (float)(ScriptMove[i].MaxVelocity + idealAccel * t1);

						if (script.MaxVelocity < idealVelocity) {
							ScriptMove.Insert(i + 1, script);
							i += 1;
						}
					}
				}
				i += 1;
			}
		}

		for (i = 0; i < ScriptMove.Count; i++)
			Move(i).MaxVelocity = Math.Max(ScriptMove[i].MaxVelocity, MIN_VELOCITY);

		Move(0).ElapsedTime = 0;
		for (i = 0; i < ScriptMove.Count - 1;) {
			Move(i).Dist = (ScriptMove[i + 1].Location - ScriptMove[i].Location).Length2D();

			if (ScriptMove[i].MaxVelocity == 0 && ScriptMove[i + 1].MaxVelocity == 0) {
				Assert(false);
				Move(i + 1).MaxVelocity = 1.0f;
			}

			TimeUnit_t t = ScriptMove[i].Dist / (0.5 * (ScriptMove[i].MaxVelocity + ScriptMove[i + 1].MaxVelocity));
			Move(i).Time = t;

			Move(i + 1).ElapsedTime = ScriptMove[i].ElapsedTime + ScriptMove[i].Time;

			i++;
		}
	}

	void InsertSlowdown(float distToObstruction, float idealAccel, bool alwaysSlowdown) {
		int i;
		AI_MovementScript script = default;

		if (distToObstruction <= 0.0)
			return;

		for (i = 0; i < ScriptMove.Count - 1; i++) {
			if (ScriptMove[i].Dist > 0 && distToObstruction - ScriptMove[i].Dist < 0) {
				float a = distToObstruction / ScriptMove[i].Dist;
				Assert(a >= 0 && a <= 1);
				script.Location = (1 - a) * ScriptMove[i].Location + a * ScriptMove[i + 1].Location;

				if (!alwaysSlowdown && MathLib.SolveQuadratic(-0.5f * idealAccel, ScriptMove[0].MaxVelocity, -distToObstruction, out float r1, out _))
					script.MaxVelocity = Math.Max(10, ScriptMove[0].MaxVelocity - idealAccel * r1);
				else
					script.MaxVelocity = 10.0f;

				script.MaxVelocity = 1.0f;
				script.Waypoint = null;
				script.Dist = ScriptMove[i].Dist - distToObstruction;
				Move(i).Dist = distToObstruction;
				ScriptMove.Insert(i + 1, script);
				break;
			}
			else
				distToObstruction -= ScriptMove[i].Dist;
		}
	}

	public override void ResetMoveCalculations() {
		base.ResetMoveCalculations();
		ScriptMove.Clear();
		ScriptTurn.Clear();
	}

	public override void MoveStart() {
		if (PrimarySequence == -1) {
			PrimarySequence = GetOuter()!.GetSequence();
			StartCycle = GetOuter()!.GetCycle();
			CurrRate = 0.4f;

			SecondarySequence = -1;

			PrimaryLayer = GetOuter()!.AddLayeredSequence(PrimarySequence, 0);
			GetOuter()!.SetLayerWeight(PrimaryLayer, 0.0f);
			GetOuter()!.SetLayerPlaybackRate(PrimaryLayer, 0.0);
			GetOuter()!.SetLayerNoRestore(PrimaryLayer, true);
			GetOuter()!.SetLayerCycle(PrimaryLayer, StartCycle, (float)StartCycle);

			SecondaryWeight = 0.0f;
		}

		if (GoalSequence == (int)Activity.ACT_INVALID)
			ResetGoalSequence();

		PrevOrigin2 = GetOuter()!.GetAbsOrigin();
		PrevOrigin1 = GetOuter()!.GetAbsOrigin();

		DeceleratingToGoal = false;
	}

	public void ResetGoalSequence() {
		SavedGoalActivity = GetNavigator()!.GetArrivalActivity();
		if (SavedGoalActivity == Activity.ACT_INVALID)
			SavedGoalActivity = GetOuter()!.GetStoppedActivity();

		SavedTranslatedGoalActivity = GetOuter()!.NPC_TranslateActivity(SavedGoalActivity);

		GoalSequence = GetNavigator()!.GetArrivalSequence(PrimarySequence);

		GoalSequence = GetInteriorSequence(PrimarySequence);

		Assert(GoalSequence != (int)Activity.ACT_INVALID);
	}

	public override float OverrideMaxYawSpeed(Activity activity) {
		if (IsYawLocked())
			return 0.0f;

		switch (activity) {
			case Activity.ACT_TURN_LEFT:
			case Activity.ACT_TURN_RIGHT:
				return 45;
			default:
				if (GetOuter()!.IsMoving())
					return 15;
				return 45;
		}
	}

	public override void MoveStop() {
		base.MoveStop();

		if (PrimaryLayer != -1) {
			GetOuter()!.RemoveLayer(PrimaryLayer, 0.2f, 0.1f);
			PrimaryLayer = -1;
		}
		if (SecondaryLayer != -1) {
			GetOuter()!.RemoveLayer(SecondaryLayer, 0.2f, 0.1f);
			SecondaryLayer = -1;
		}
		PrimarySequence = (int)Activity.ACT_INVALID;
		SecondarySequence = (int)Activity.ACT_INVALID;
		PrevMovementSequence = (int)Activity.ACT_INVALID;
		InteriorSequence = (int)Activity.ACT_INVALID;
	}

	public override void MovePaused() {
		base.MovePaused();
		SetMoveScriptAnim(0.0f);
	}

	public void MoveContinue() {
		PrimarySequence = GetInteriorSequence((int)Activity.ACT_INVALID);
		GoalSequence = PrimarySequence;

		Assert(PrimarySequence != (int)Activity.ACT_INVALID);

		if (PrimarySequence == (int)Activity.ACT_INVALID)
			return;

		StartCycle = 0.0;

		PrimaryLayer = GetOuter()!.AddLayeredSequence(PrimarySequence, 0);
		GetOuter()!.SetLayerWeight(PrimaryLayer, 0.0f);
		GetOuter()!.SetLayerPlaybackRate(PrimaryLayer, 0.0);
		GetOuter()!.SetLayerNoRestore(PrimaryLayer, true);
		GetOuter()!.SetLayerCycle(PrimaryLayer, StartCycle, (float)StartCycle);

		DeceleratingToGoal = false;
	}

	public new bool IsDeceleratingToGoal() => DeceleratingToGoal;

	public override void UpdateYaw(int speed = -1) {
		if (IsYawLocked())
			return;

		GetOuter()!.UpdateTurnGesture();
		base.UpdateYaw(speed);
	}

	public override void MaintainTurnActivity() {
		if (NextTurnGesture > gpGlobals.CurTime || NextTurnAct > gpGlobals.CurTime || GetOuter()!.IsMoving()) {
			DoTurn = DoRight = DoLeft = 0;
			if (GetOuter()!.IsMoving())
				NextTurnAct = gpGlobals.CurTime + 0.3;
		}
		else {
			if (PrevYaw != GetOuter()!.GetAbsAngles().Y) {
				float diff = Util.AngleDiff(PrevYaw, GetOuter()!.GetAbsAngles().Y);
				if (diff < 0.0)
					DoLeft += -diff;
				else
					DoRight += diff;
				PrevYaw = GetOuter()!.GetAbsAngles().Y;
			}
			DoTurn += DoRight + DoLeft;
			DoTurn += RandomFloat(0.4f, 0.6f);
		}

		if (DoTurn > 15.0f) {
			int seq = (int)Activity.ACT_INVALID;
			if (DoLeft > DoRight)
				seq = GetOuter()!.SelectWeightedSequence(Activity.ACT_GESTURE_TURN_LEFT);
			else
				seq = GetOuter()!.SelectWeightedSequence(Activity.ACT_GESTURE_TURN_RIGHT);
			DoLeft = 0;
			DoRight = 0;

			if (seq != (int)Activity.ACT_INVALID) {
				int layer = GetOuter()!.AddGestureSequence(seq);
				if (layer != -1) {
					GetOuter()!.SetLayerPriority(layer, 100);
					float rate = RandomFloat(0.8f, 1.2f);
					if (DoTurn > 90.0)
						rate *= 1.5f;
					GetOuter()!.SetLayerPlaybackRate(layer, rate);
					NextTurnAct = gpGlobals.CurTime + GetOuter()!.GetLayerDuration(layer);
				}
				else
					NextTurnAct = gpGlobals.CurTime + 0.3;
			}
			DoTurn = DoRight = DoLeft = 0;
		}
	}

	public override bool AddTurnGesture(float yd) => false;

	public TimeUnit_t NextTurnGesture;

	public override void RecalculateYawSpeed() {
		if (IsYawLocked()) {
			SetYawSpeed(0.0f);
			return;
		}

		if (GetOuter()!.HasMemory(AI_MemoryFlags.Turning))
			return;

		SetYawSpeed(CalcYawSpeed());
	}

	public struct AI_MovementScript
	{
		public TimeUnit_t Time;
		public TimeUnit_t ElapsedTime;

		public float Dist;

		public float MaxVelocity;

		public float Yaw;
		public float AngularVelocity;

		public bool Looping;
		public int Flags;

		public AI_Waypoint? Waypoint;

		public Vector3 Location;
	}

	readonly List<AI_MovementScript> ScriptMove = [];
	readonly List<AI_MovementScript> ScriptTurn = [];

	public bool DeceleratingToGoal;

	public int PrimaryLayer;
	public int SecondaryLayer;

	public int PrimarySequence;
	public int SecondarySequence;
	public float SecondaryWeight;

	public Activity SavedGoalActivity;
	public Activity SavedTranslatedGoalActivity;
	public int GoalSequence;

	public int PrevMovementSequence;
	public int InteriorSequence;

	public TimeUnit_t StartCycle;

	public float CurrRate;

	public float PredictiveSpeedAdjust;
	public float ReactiveSpeedAdjust;
	public Vector3 PrevOrigin1;
	public Vector3 PrevOrigin2;

	public float PrevYaw;
	public float DoTurn;
	public float DoLeft;
	public float DoRight;
	public TimeUnit_t NextTurnAct;
}
