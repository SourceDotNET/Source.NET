using Game.Shared;

using Source.Common;

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

	public bool IsEmpty() => Waypoints.IsEmpty();

	public AI_Waypoint? GetCurWaypoint() => Waypoints.GetFirst();

	public Vector3 CurWaypointPos() {
		if (GetCurWaypoint() != null)
			return GetCurWaypoint()!.GetPos();
		AssertMsg(false, "Invalid call to CurWaypointPos()");
		return gm_InvalidWaypoint.GetPos();
	}

	public Activity GetMovementActivity() => ActivityValue;

	public Activity GetArrivalActivity() {
		if (!Waypoints.IsEmpty())
			return ArrivalActivity;
		return Activity.ACT_INVALID;
	}

	public NavGoalType GoalType() => GoalTypeValue;

	public BaseEntity? GetTarget() => Target.Get();

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

	uint GoalFlagsValue;

	float RouteStartTime;

	Vector3 GoalDirection;
	EHANDLE GoalDirectionTarget = new();

	float GoalSpeed;
	EHANDLE GoalSpeedTarget = new();

	float GoalStoppingDistance;

	static readonly AI_Waypoint gm_InvalidWaypoint = new();
}
