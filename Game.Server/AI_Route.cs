using Game.Shared;

using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

public class AI_Path
{
	public const float DEF_WAYPOINT_TOLERANCE = 0.1f;

	public AI_Path() {
		GoalTypeValue = Server.NavGoalType.None;
		GoalPos = vec3_origin;
		GoalTolerance = 0.0f;
		ActivityValue = Activity.ACT_INVALID;
		Sequence = (int)Activity.ACT_INVALID;
		Target.Set(null);
		GoalFlagsValue = 0;
		RouteStartTime = float.MaxValue;
		ArrivalActivity = Activity.ACT_INVALID;
		ArrivalSequence = (int)Activity.ACT_INVALID;

		LastNodeReached = NO_NODE;

		WaypointTolerance = DEF_WAYPOINT_TOLERANCE;
	}

	public void SetWaypoints(AI_Waypoint? route, bool setGoalFromLast = false) {
		Waypoints.Set(route);

		AI_Waypoint? last = Waypoints.GetLast();
		if (last != null) {
			last.PathDistGoal = -1;
			if (setGoalFromLast) {
				GoalPosSet = false;
				last.ModifyFlags(WaypointFlags.ToGoal, true);
				SetGoalPosition(last.GetPos());
			}
		}
	}

	public void PrependWaypoints(AI_Waypoint waypoints) {
		Waypoints.PrependWaypoints(waypoints);
		AI_Waypoint last = Waypoints.GetLast()!;
		last.PathDistGoal = -1;
	}

	public void PrependWaypoint(in Vector3 newPoint, Navigation navType, WaypointFlags waypointFlags) {
		Waypoints.PrependWaypoint(newPoint, navType, waypointFlags);
		AI_Waypoint last = Waypoints.GetLast()!;
		last.PathDistGoal = -1;
	}

	public bool IsEmpty() => Waypoints.IsEmpty();

	public AI_Waypoint? GetCurWaypoint() => Waypoints.GetFirst();
	public AI_Waypoint? GetGoalWaypoint() => Waypoints.GetLast();

	public Vector3 CurWaypointPos() {
		if (GetCurWaypoint() != null)
			return GetCurWaypoint()!.GetPos();
		AssertMsg(false, "Invalid call to CurWaypointPos()");
		return gm_InvalidWaypoint.GetPos();
	}

	public Vector3 NextWaypointPos() {
		if (GetCurWaypoint() != null && GetCurWaypoint()!.GetNext() != null)
			return GetCurWaypoint()!.GetNext()!.GetPos();
		AssertMsg(false, "Invalid call to NextWaypointPos()");
		return gm_InvalidWaypoint.GetPos();
	}

	public float CurWaypointYaw() => GetCurWaypoint()!.Yaw;

	public WaypointFlags CurWaypointFlags() {
		if (GetCurWaypoint() == null)
			return 0;
		else
			return GetCurWaypoint()!.Flags();
	}

	public Navigation CurWaypointNavType() {
		if (GetCurWaypoint() == null)
			return Navigation.None;
		else
			return GetCurWaypoint()!.NavType();
	}

	public float GetPathLength() {
		AI_Waypoint? last = Waypoints.GetLast();
		if (last != null && last.PathDistGoal == -1)
			ComputeRouteGoalDistances(last);
		AI_Waypoint? current = GetCurWaypoint();
		return current != null ? current.PathDistGoal : 0;
	}

	public float GetPathDistanceToGoal(in Vector3 startPos) {
		AI_Waypoint? current = GetCurWaypoint();
		if (current != null)
			return GetPathLength() + ComputePathDistance(current.NavType(), startPos, current.GetPos());
		return 0;
	}

	public TimeUnit_t GetStartTime() => RouteStartTime;

	public void SetGoalTolerance(float tolerance) => GoalTolerance = tolerance;
	public float GetGoalTolerance() => GoalTolerance;

	public void SetWaypointTolerance(float tolerance) => WaypointTolerance = tolerance;
	public float GetWaypointTolerance() => WaypointTolerance;

	public Activity GetMovementActivity() => ActivityValue;

	public Activity SetMovementActivity(Activity activity) {
		Assert(activity != Activity.ACT_RESET && activity != Activity.ACT_INVALID);

		Sequence = (int)Activity.ACT_INVALID;
		return ActivityValue = activity;
	}

	public int GetMovementSequence() => Sequence;
	public int SetMovementSequence(int sequence) => Sequence = sequence;

	public Activity GetArrivalActivity() {
		if (!Waypoints.IsEmpty())
			return ArrivalActivity;
		return Activity.ACT_INVALID;
	}

	public void SetArrivalActivity(Activity activity) {
		ArrivalActivity = activity;
		ArrivalSequence = (int)Activity.ACT_INVALID;
	}

	public int GetArrivalSequence() {
		if (!Waypoints.IsEmpty())
			return ArrivalSequence;
		return (int)Activity.ACT_INVALID;
	}

	public void SetArrivalSequence(int sequence) => ArrivalSequence = sequence;

	public void SetGoalDirection(in Vector3 goalDirection) {
		GoalDirectionTarget.Set(null);
		GoalDirection = goalDirection;
		MathLib.VectorNormalize(ref GoalDirection);
	}

	public void SetGoalDirection(BaseEntity? target) {
		GoalDirectionTarget.Set(target);

		if (target != null) {
			AI_Waypoint? last = Waypoints.GetLast();
			if (last != null) {
				GoalDirection = target.GetAbsOrigin() - last.VecLocation;
				MathLib.VectorNormalize(ref GoalDirection);
			}
		}
	}

	public Vector3 GetGoalDirection(in Vector3 startPos) {
		BaseEntity? goalDirectionTarget = GoalDirectionTarget.Get();
		if (goalDirectionTarget != null) {
			AI_Waypoint? last = Waypoints.GetLast();
			if (last != null) {
				AI_Waypoint? prev = last.GetPrev();
				if (prev != null) {
					Vector3 goalDirection = goalDirectionTarget.GetAbsOrigin() - prev.VecLocation;
					MathLib.VectorNormalize(ref goalDirection);
					return goalDirection;
				}
				else {
					Vector3 goalDirection = goalDirectionTarget.GetAbsOrigin() - startPos;
					MathLib.VectorNormalize(ref goalDirection);
					return goalDirection;
				}
			}
		}
		else if (GoalDirection == vec3_origin) {
			AI_Waypoint? last = Waypoints.GetLast();
			if (last != null) {
				AI_Waypoint? prev = last.GetPrev();
				if (prev != null) {
					Vector3 goalDirection = last.VecLocation - prev.VecLocation;
					MathLib.VectorNormalize(ref goalDirection);
					return goalDirection;
				}
				else {
					Vector3 goalDirection = last.VecLocation - startPos;
					MathLib.VectorNormalize(ref goalDirection);
					return goalDirection;
				}
			}
		}

		return GoalDirection;
	}

	public void SetGoalSpeed(float speed) => GoalSpeed = speed;

	public void SetGoalSpeed(BaseEntity? target) => GoalSpeedTarget.Set(target);

	public float GetGoalSpeed(in Vector3 startPos) {
		BaseEntity? goalSpeedTarget = GoalSpeedTarget.Get();
		if (goalSpeedTarget != null) {
			Vector3 goalDirection = GetGoalDirection(startPos);
			Vector3 targetVelocity = goalSpeedTarget.GetSmoothedVelocity();
			float dot = Vector3.Dot(goalDirection, targetVelocity);
			dot = Math.Max(0.0f, dot);
			if (GoalSpeed > 0.0)
				return dot + GoalSpeed;
			return dot;
		}
		return GoalSpeed;
	}

	public void SetGoalStoppingDistance(float distance) => GoalStoppingDistance = distance;

	public float GetGoalStoppingDistance() => GoalStoppingDistance;

	public void SetTarget(BaseEntity? target) => Target.Set(target);
	public void ClearTarget() {
		Target.Set(null);
		TargetOffset = vec3_origin;
	}
	public void SetTargetOffset(in Vector3 offset) => TargetOffset = offset;
	public BaseEntity? GetTarget() => Target.Get();

	public void SetGoalType(NavGoalType goalType) {
#if DEBUG
		if (GoalTypeValue != Server.NavGoalType.None && goalType != Server.NavGoalType.None)
			DevMsg("GetCurWaypoint Goal Type Set Twice!\n");
#endif

		if (GoalTypeValue != Server.NavGoalType.None) {
			RouteStartTime = gpGlobals.CurTime;
			GoalTypeSet = true;
		}
		else
			GoalTypeSet = false;

		GoalTypeValue = goalType;
	}

	public void SetGoalPosition(in Vector3 goalPos) {
#if DEBUG
		if (GoalPosSet)
			DevMsg("GetCurWaypoint Goal Position Set Twice!\n");
#endif

		GoalPosSet = true;
		GoalPos = goalPos + TargetOffset;
	}

	public void SetLastNodeAsGoal(bool reset = false) {
#if DEBUG
		if (!reset && GoalPosSet)
			DevMsg("GetCurWaypoint Goal Position Set Twice!\n");
#endif

		if (GetCurWaypoint() != null) {
			AI_Waypoint? waypoint = GetCurWaypoint();

			while (waypoint != null) {
				if (waypoint.GetNext() == null) {
					GoalPos = waypoint.GetPos();
					GoalPosSet = true;
					waypoint.ModifyFlags(WaypointFlags.ToGoal, true);
					return;
				}
				waypoint = waypoint.GetNext();
			}
		}
	}

	public void ResetGoalPosition(in Vector3 goalPos) {
		GoalPosSet = true;
		GoalPos = goalPos + TargetOffset;
	}

	public Vector3 BaseGoalPosition() {
#if DEBUG
		if (!GoalPosSet)
			DevMsg("GetCurWaypoint Goal Position Never Set!\n");
#endif

		return GoalPos - TargetOffset;
	}

	public ref readonly Vector3 ActualGoalPosition() {
#if DEBUG
		if (!GoalPosSet)
			DevMsg("GetCurWaypoint Goal Position Never Set!\n");
#endif

		return ref GoalPos;
	}

	public NavGoalType GoalType() => GoalTypeValue;

	public void SetGoalFlags(AI_NavGoalFlags flags) => GoalFlagsValue = flags;
	public AI_NavGoalFlags GoalFlags() => GoalFlagsValue;

	public void Advance() {
		if (CurWaypointIsGoal())
			return;

		if (GetCurWaypoint()!.GetNext() != null) {
			AI_Waypoint next = GetCurWaypoint()!.GetNext()!;

			if ((GetCurWaypoint()!.Flags() & WaypointFlags.ToNode) != 0)
				LastNodeReached = GetCurWaypoint()!.NodeID;

			GetCurWaypoint()!.Delete();
			SetWaypoints(next);

			return;
		}
		else {
			DevMsg("!!ERROR!! Force end of route with no goal!\n");
			GetCurWaypoint()!.ModifyFlags(WaypointFlags.ToGoal, true);
		}
	}

	public bool CurWaypointIsGoal() {
		if (GetCurWaypoint() == null)
			return false;

		if ((GetCurWaypoint()!.Flags() & WaypointFlags.ToGoal) != 0) {
#if DEBUG
			if (GetCurWaypoint()!.GetNext() != null)
				DevMsg("!!ERROR!! Goal is not last waypoint!\n");
			if ((GetCurWaypoint()!.GetPos() - GoalPos).Length() > 0.1)
				DevMsg("!!ERROR!! Last waypoint isn't in goal position!\n");
#endif
			return true;
		}
#if DEBUG
		if (GetCurWaypoint()!.GetNext() == null)
			DevMsg("!!ERROR!! GetCurWaypoint has no goal!\n");
#endif

		return false;
	}

	public void Clear() {
		Waypoints.RemoveAll();

		GoalTypeValue = Server.NavGoalType.None;
		GoalPos = vec3_origin;
		GoalPosSet = false;
		GoalTypeSet = false;
		GoalFlagsValue = 0;
		TargetOffset = vec3_origin;
		RouteStartTime = float.MaxValue;

		GoalTolerance = 0.0f;

		ActivityValue = Activity.ACT_INVALID;
		Sequence = (int)Activity.ACT_INVALID;
		Target.Set(null);

		ArrivalActivity = Activity.ACT_INVALID;
		ArrivalSequence = (int)Activity.ACT_INVALID;

		GoalDirectionTarget.Set(null);
		GoalDirection = vec3_origin;

		GoalSpeedTarget.Set(null);
		GoalSpeed = -1.0f;

		GoalStoppingDistance = 0.0f;
	}

	public int GetLastNodeReached() => LastNodeReached;
	public void ClearWaypoints() {
		Waypoints.RemoveAll();
		LastNodeReached = NO_NODE;
	}

	static void ComputeRouteGoalDistances(AI_Waypoint goalWaypoint) {
		AI_Waypoint curWaypoint = goalWaypoint;
		curWaypoint.PathDistGoal = 0;
		while (curWaypoint.GetPrev() != null) {
			AI_Waypoint prev = curWaypoint.GetPrev()!;

			float waypointDist = ComputePathDistance(curWaypoint.NavType(), prev.GetPos(), curWaypoint.GetPos());
			prev.PathDistGoal = curWaypoint.PathDistGoal + waypointDist;

			curWaypoint = prev;
		}
	}

	readonly AI_WaypointList Waypoints = new();

	float GoalTolerance;
	Activity ActivityValue;
	int Sequence;
	EHANDLE Target = new();
	Vector3 TargetOffset;
	float WaypointTolerance;

	Activity ArrivalActivity;
	int ArrivalSequence;

	int LastNodeReached;

	bool GoalPosSet;
	Vector3 GoalPos;

	bool GoalTypeSet;
	NavGoalType GoalTypeValue;

	AI_NavGoalFlags GoalFlagsValue;

	TimeUnit_t RouteStartTime;

	Vector3 GoalDirection;
	EHANDLE GoalDirectionTarget = new();

	float GoalSpeed;
	EHANDLE GoalSpeedTarget = new();

	float GoalStoppingDistance;

	static readonly AI_Waypoint gm_InvalidWaypoint = new(vec3_origin, 0, Navigation.None, 0, 0);
}
