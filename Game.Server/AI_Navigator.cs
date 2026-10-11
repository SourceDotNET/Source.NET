global using static Game.Server.AI_NavigatorGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Server;

public static class AI_NavigatorGlobals
{
	public const float AIN_DEF_TOLERANCE = -1.0f;
	public const float AIN_HULL_TOLERANCE = -2.0f;
	public const Activity AIN_DEF_ACTIVITY = Activity.ACT_INVALID;
	public const int AIN_NO_NODE = -1;
	public static readonly Vector3 AIN_NO_DEST = new(float.MaxValue, float.MaxValue, float.MaxValue);

	public static readonly ConVar ai_vehicle_avoidance = new("ai_vehicle_avoidance", "1", FCvar.Cheat);
	public static readonly ConVar ai_navigator_generate_spikes = new("ai_navigator_generate_spikes", "0");
	public static readonly ConVar ai_navigator_generate_spikes_strength = new("ai_navigator_generate_spikes_strength", "8");
	public static readonly ConVar ai_use_clipped_paths = new("ai_use_clipped_paths", "1");
}

[Flags]
public enum AI_NavGoalFlags
{
	Default = 0,
	YawToDest = 0x01,
	UpdateTargetPos = 0x02,
	NoPathcornerPathfinding = 0x04,
}

[Flags]
public enum AI_NavSetGoalFlags
{
	ClearPreviousState = 0x01,
	ClearTarget = 0x02,
	DiscardIfFail = 0x04,
	NoPathTaskFail = 0x08,
}

public enum AI_NpcBlockHandling
{
	Block,
	Avoid,
	Ignore,
}

public enum AI_NavPathProgress
{
	NoChange,
	Advanced,
	Complete,
	Blocked,
}

public struct AI_NavGoal
{
	public AI_NavGoal() : this(NavGoalType.Invalid) { }

	public AI_NavGoal(NavGoalType type, Activity activity = AIN_DEF_ACTIVITY, float tolerance = AIN_DEF_TOLERANCE,
		AI_NavGoalFlags flags = AI_NavGoalFlags.Default, BaseEntity? target = null, bool defTarget = true)
		: this(type, AIN_NO_DEST, AIN_NO_NODE, activity, tolerance, flags, target, defTarget) { }

	public AI_NavGoal(in Vector3 dest, Activity activity = AIN_DEF_ACTIVITY, float tolerance = AIN_DEF_TOLERANCE,
		AI_NavGoalFlags flags = AI_NavGoalFlags.Default, BaseEntity? target = null, bool defTarget = true)
		: this(NavGoalType.Location, dest, AIN_NO_NODE, activity, tolerance, flags, target, defTarget) { }

	public AI_NavGoal(NavGoalType type, in Vector3 dest, Activity activity = AIN_DEF_ACTIVITY, float tolerance = AIN_DEF_TOLERANCE,
		AI_NavGoalFlags flags = AI_NavGoalFlags.Default, BaseEntity? target = null, bool defTarget = true)
		: this(type, dest, AIN_NO_NODE, activity, tolerance, flags, target, defTarget) { }

	public AI_NavGoal(int destNode, Activity activity = AIN_DEF_ACTIVITY, float tolerance = AIN_DEF_TOLERANCE,
		AI_NavGoalFlags flags = AI_NavGoalFlags.Default, BaseEntity? target = null, bool defTarget = true)
		: this(NavGoalType.Location, AIN_NO_DEST, destNode, activity, tolerance, flags, target, defTarget) { }

	public AI_NavGoal(NavGoalType type, int destNode, Activity activity = AIN_DEF_ACTIVITY, float tolerance = AIN_DEF_TOLERANCE,
		AI_NavGoalFlags flags = AI_NavGoalFlags.Default, BaseEntity? target = null, bool defTarget = true)
		: this(type, AIN_NO_DEST, destNode, activity, tolerance, flags, target, defTarget) { }

	AI_NavGoal(NavGoalType type, in Vector3 dest, int destNode, Activity activity, float tolerance, AI_NavGoalFlags flags, BaseEntity? target, bool defTarget) {
		Type = type;
		Dest = dest;
		DestNode = destNode;
		Activity = activity;
		Tolerance = tolerance;
		MaxInitialSimplificationDist = -1;
		Flags = flags;
		Target = target;
		DefTarget = defTarget && target == null;
		ArrivalActivity = AIN_DEF_ACTIVITY;
		ArrivalSequence = (int)Activity.ACT_INVALID;
	}

	public NavGoalType Type;

	public Vector3 Dest;
	public int DestNode;

	public Activity Activity;

	public Activity ArrivalActivity;
	public int ArrivalSequence;

	public float Tolerance;

	public float MaxInitialSimplificationDist;

	public AI_NavGoalFlags Flags;

	public BaseEntity? Target;
	public bool DefTarget;
}

public struct AI_ProgressFlyPathParams(Mask collisionMask, float strictPointTolerance = 32.0f, float blockTolerance = 0.0f,
	float waypointTolerance = 100, float goalTolerance = 12, AI_NpcBlockHandling blockHandling = AI_NpcBlockHandling.Block)
{
	public void SetCurrent(BaseEntity? newTarget, bool newTrySimplify = true) {
		Target = newTarget;
		TrySimplify = newTrySimplify;
	}

	public Mask CollisionMask = collisionMask;
	public float StrictPointTolerance = strictPointTolerance;
	public float BlockTolerance = blockTolerance;
	public float WaypointTolerance = waypointTolerance;
	public float GoalTolerance = goalTolerance;
	public AI_NpcBlockHandling BlockHandling = blockHandling;

	public BaseEntity? Target = null;
	public bool TrySimplify = true;
}

class AI_NavInHintGroupFilter(string? group = null) : INearestNodeFilter
{
	public bool IsValid(AI_Node node) {
		if (node.GetHint() == null)
			return false;

		if (node.GetHint()!.GetGroup() != Group)
			return false;

		return true;
	}

	public bool ShouldContinue() => true;

	public string? Group = group;
}

public class AI_Navigator : AI_Component, IAI_MovementSink
{
	const float NAV_STOP_MOVING_TOLERANCE = 6;

	const float MIN_ANGLE_COS_SIMPLIFY = 0.766f;

	static readonly float[] ROUTE_SIMPLIFY_TIME_DELAY = [0.5f, 1.0f];
	static readonly float[] NO_PVS_ROUTE_SIMPLIFY_TIME_DELAY = [1.0f, 2.0f];
	static readonly float[] QUICK_SIMPLIFY_TIME_DELAY = [float.MinValue, 0.3f];

	const float FLY_ROUTE_SIMPLIFY_TIME_DELAY = 0.3f;
	const float FLY_ROUTE_SIMPLIFY_LOOK_DIST = 12.0f * 12.0f;

	static long g_iFrameLastSimplified;

	struct SimplifyForwardScanParams(float scanDist, float radius, float increment, int maxSamples)
	{
		public float ScanDist = scanDist;
		public float Radius = radius;
		public float Increment = increment;
		public int MaxSamples = maxSamples;
	}

	static SimplifyForwardScanParams fullScanParams = new(32 * 12, 12 * 12, 3 * 12, 4);

	static readonly SimplifyForwardScanParams[] quickScanParams = [
		new((12.0f * 12.0f) - 0.1f, 12 * 12, 0.5f * 12, 1),
		new((6.0f * 12.0f) - 0.1f, 8 * 12, 1.0f * 12, 1),
	];

	public AI_Navigator(AI_BaseNPC? outer) : base(outer) {
		Path = new AI_Path();
		AINetwork = null;
		NotOnNetwork = false;
		NextSimplifyTime = 0;

		LastSuccessfulSimplifyTime = -1;

		ClippedWaypoints = new AI_WaypointList();
		TimeClipped = -1;

		ValidateActivitySpeed = true;
		CalledStartMove = false;

		NavType = Navigation.Ground;
		NavComplete = false;
		LastNavFailed = false;

		PeerWaitMoveTimer.Set(0.25f);
		PeerWaitClearTimer.Set(3.0f);
		NextSidestepTimer.Set(5.0f);

		PosBeginFailedSteer = vec3_invalid;
		TimeBeginFailedSteer = float.MaxValue;

		TimeLastAvoidanceTriangulate = -1;

		NoPathcornerPathfinds = false;
		LocalSucceedOnWithinTolerance = false;

		RememberStaleNodes = true;

		Motor = null;
		MoveProbe = null;
		LocalNavigator = null;

		NavFailCounter = 0;
		LastNavFailTime = -1;
	}

	public virtual void Init(AI_Network? network) {
		Motor = GetOuter()!.GetMotor();
		MoveProbe = GetOuter()!.GetMoveProbe();
		LocalNavigator = GetOuter()!.GetLocalNavigator();
		AINetwork = network;
	}

	public void SetPathcornerPathfinding(bool newVal) => NoPathcornerPathfinds = !newVal;
	public void SetRememberStaleNodes(bool newVal) => RememberStaleNodes = newVal;
	public void SetValidateActivitySpeed(bool validateActivitySpeed) => ValidateActivitySpeed = validateActivitySpeed;
	public void SetLocalSucceedOnWithinTolerance(bool newVal) => LocalSucceedOnWithinTolerance = newVal;

	static bool ActivityIsLocomotive(Activity activity) => activity > Activity.ACT_IDLE;

	protected AI_Pathfinder? GetPathfinder() => GetOuter()!.GetPathfinder();

	protected BaseEntity? GetNavTargetEntity() {
		if (GetGoalType() == NavGoalType.Enemy || GetGoalType() == NavGoalType.TargetEnt)
			return GetOuter()!.GetNavTargetEntity();
		return GetPath().GetTarget();
	}

	protected void TaskMovementComplete() => GetOuter()!.TaskMovementComplete();
	protected float MaxYawSpeed() => GetOuter()!.MaxYawSpeed();
	protected void SetSpeed(float speed) => GetOuter()!.Speed = speed;

	AI_HullType GetHullType() => GetOuter()!.GetHullType();
	float GetHullWidth() => GetOuter()!.GetHullWidth();
	ref readonly Vector3 GetLocalOrigin() => ref GetOuter()!.GetLocalOrigin();
	ref readonly Vector3 GetAbsOrigin() => ref GetOuter()!.GetAbsOrigin();
	Capability CapabilitiesGet() => GetOuter()!.CapabilitiesGet();
	void SetActivity(Activity activity) => GetOuter()!.SetActivity(activity);
	Activity GetActivity() => GetOuter()!.GetActivity();
	bool HasMemory(AI_MemoryFlags memory) => GetOuter()!.HasMemory(memory);
	void Remember(AI_MemoryFlags memory) => GetOuter()!.Remember(memory);
	void Forget(AI_MemoryFlags memory) => GetOuter()!.Forget(memory);
	void TaskComplete() => GetOuter()!.TaskComplete();
	void TaskFail(AI_TaskFailureCode code) => GetOuter()!.TaskFail(code);

	bool FindPath(in AI_NavGoal goal, AI_NavSetGoalFlags flags) {
		AI_Path path = GetPath();

		GetOuter()?.Remember(AI_MemoryFlags.TaskExpensive);

		if ((flags & AI_NavSetGoalFlags.ClearPreviousState) != 0)
			path.Clear();
		else if ((flags & AI_NavSetGoalFlags.ClearTarget) != 0)
			path.ClearTarget();

		if (goal.Activity != AIN_DEF_ACTIVITY)
			path.SetMovementActivity(goal.Activity);
		else if (path.GetMovementActivity() == Activity.ACT_INVALID)
			path.SetMovementActivity((GetOuter()!.GetState() == NPCState.Combat) ? Activity.ACT_RUN : Activity.ACT_WALK);

		if (goal.Tolerance == AIN_HULL_TOLERANCE)
			path.SetGoalTolerance(GetHullWidth());
		else if (goal.Tolerance != AIN_DEF_TOLERANCE)
			path.SetGoalTolerance(goal.Tolerance);
		else if (path.GetGoalTolerance() == 0)
			path.SetGoalTolerance(GetOuter()!.GetDefaultNavGoalTolerance());

		path.SetWaypointTolerance(GetHullWidth() * 0.5f);

		path.SetGoalType(NavGoalType.None);
		path.SetGoalType(goal.Type);
		path.SetGoalFlags(goal.Flags);

		BaseEntity? pathTarget = goal.Target;
		bool pathTargetIsDef = goal.DefTarget;
		if ((goal.Type == NavGoalType.TargetEnt) || (goal.Type == NavGoalType.Enemy)) {
			if (goal.Type == NavGoalType.TargetEnt)
				pathTarget = GetOuter()!.GetTarget();
			else
				pathTarget = GetOuter()!.GetEnemy();
			pathTargetIsDef = false;

			Assert(goal.DefTarget || goal.Target == pathTarget);

			if (goal.Dest != AIN_NO_DEST)
				path.SetTargetOffset(goal.Dest);
		}
		else {
			if (goal.Dest != AIN_NO_DEST)
				path.ResetGoalPosition(goal.Dest);
			else if (goal.DestNode != AIN_NO_NODE)
				path.ResetGoalPosition(GetNodePos(goal.DestNode));
		}

		if (pathTarget != null && !pathTargetIsDef)
			path.SetTarget(pathTarget);

		path.ClearWaypoints();
		bool result = FindPath((flags & AI_NavSetGoalFlags.NoPathTaskFail) == 0);

		if (result == false) {
			if ((flags & AI_NavSetGoalFlags.DiscardIfFail) != 0)
				path.Clear();
			else
				path.SetGoalType(NavGoalType.None);
		}
		else {
			if (goal.ArrivalActivity != AIN_DEF_ACTIVITY && goal.ArrivalActivity > Activity.ACT_RESET)
				path.SetArrivalActivity(goal.ArrivalActivity);
			else if (goal.ArrivalSequence != -1)
				path.SetArrivalSequence(goal.ArrivalSequence);

			if (goal.Type == NavGoalType.Enemy) {
				path.SetGoalDirection(pathTarget);
				path.SetGoalSpeed(pathTarget);
			}
			else
				path.SetGoalDirection(path.ActualGoalPosition() - GetAbsOrigin());
		}

		return result;
	}

	public virtual bool SetGoal(in AI_NavGoal goal, AI_NavSetGoalFlags flags = 0) {
		AI_Path path = GetPath();

		OnNewGoal();

		if ((flags & AI_NavSetGoalFlags.ClearPreviousState) != 0)
			ClearPath();

		if (GetOuter()!.IsCurTaskContinuousMove())
			flags |= AI_NavSetGoalFlags.NoPathTaskFail;

		bool result = FindPath(goal, flags);

		if (result == false) {
			if ((flags & AI_NavSetGoalFlags.DiscardIfFail) != 0)
				ClearPath();
		}
		else {
			if ((goal.Flags & AI_NavGoalFlags.YawToDest) != 0)
				GetMotor()!.SetIdealYawToTarget(path.ActualGoalPosition());

			SimplifyPath(true, goal.MaxInitialSimplificationDist);
		}

		return result;
	}

	public virtual bool SetGoalTarget(BaseEntity? entity, in Vector3 offset) {
		OnNewGoal();
		AI_Path path = GetPath();
		path.SetTargetOffset(offset);
		path.SetTarget(entity);
		path.ClearWaypoints();
		return FindPath(!GetOuter()!.IsCurTaskContinuousMove());
	}

	public bool SetRadialGoal(in Vector3 destination, in Vector3 center, float radius, float arc, float stepDist, bool clockwise, bool airRoute = false) {
		OnNewGoal();
		GetPath().SetGoalType(NavGoalType.Location);

		GetPath().SetWaypoints(GetPathfinder()!.BuildRadialRoute(GetLocalOrigin(), center, destination, radius, arc, stepDist, clockwise, GetPath().GetGoalTolerance(), airRoute), true);
		GetPath().SetGoalTolerance(GetOuter()!.GetDefaultNavGoalTolerance());

		return IsGoalActive();
	}

	public bool SetRandomGoal(in Vector3 from, float minPathLength, in Vector3 dir = default) {
		OnNewGoal();
		if (GetNetwork()!.NumNodes() <= 0)
			return false;

		INearestNodeFilter? filter = null;
		AI_NavInHintGroupFilter hintFilter = new();
		if (GetOuter()!.GetHintGroup() != null) {
			hintFilter.Group = GetOuter()!.GetHintGroup();
			filter = hintFilter;
		}

		int fromNodeID = GetNetwork()!.NearestNodeToPoint(GetOuter(), from, true, filter);

		if (fromNodeID == NO_NODE)
			return false;

		AI_Waypoint? route = GetPathfinder()!.FindShortRandomPath(fromNodeID, minPathLength, dir);

		if (route == null)
			return false;

		GetPath().SetGoalType(NavGoalType.Location);
		GetPath().SetWaypoints(route);
		GetPath().SetLastNodeAsGoal();
		GetPath().SetGoalTolerance(GetOuter()!.GetDefaultNavGoalTolerance());

		SimplifyPath(true);

		return true;
	}

	public bool SetRandomGoal(float minPathLength, in Vector3 dir = default) => SetRandomGoal(GetLocalOrigin(), minPathLength, dir);

	public bool SetDirectGoal(in Vector3 goalPos, Navigation navType = Navigation.Ground) {
		OnNewGoal();
		ClearPath();
		GetPath().SetGoalType(NavGoalType.Location);
		GetPath().SetWaypoints(new AI_Waypoint(goalPos, 0, navType, WaypointFlags.ToGoal, NO_NODE));
		GetPath().SetGoalTolerance(GetOuter()!.GetDefaultNavGoalTolerance());
		GetPath().SetGoalPosition(goalPos);
		return true;
	}

	static bool SetWanderGoalByRandomVector(AI_Navigator nav, float minRadius, float maxRadius, int numTries) {
		while (--numTries >= 0) {
			float dist = random.RandomFloat(minRadius, maxRadius);
			Vector3 dir = Util.YawToVector(random.RandomFloat(0, 359.99f));

			if (nav.SetVectorGoal(dir, dist, minRadius))
				return true;
		}

		return false;
	}

	public bool SetWanderGoal(float minRadius, float maxRadius) {
		if (GetOuter()!.GetHintGroup() == null)
			return SetWanderGoalByRandomVector(this, minRadius, maxRadius, 5) || SetRandomGoal(1);
		else
			return SetRandomGoal(1) || SetWanderGoalByRandomVector(this, minRadius, maxRadius, 5);
	}

	static void CalculateDeflection(in Vector3 start, in Vector3 dir, in Vector3 normal, out Vector3 result) {
		Vector3 temp = Vector3.Cross(dir, normal);
		result = Vector3.Cross(normal, temp);
		MathLib.VectorNormalize(ref result);
	}

	public bool SetVectorGoal(in Vector3 dir, float targetDist, float minDist = 0, bool shouldDeflect = false) {
		if (FindVectorGoal(out Vector3 result, dir, targetDist, minDist, shouldDeflect))
			return SetGoal(new AI_NavGoal(result));

		return false;
	}

	public bool SetVectorGoalFromTarget(in Vector3 goalPos, float minDist = 0, bool shouldDeflect = false) {
		float dist = ComputePathDirection(GetNavType(), GetLocalOrigin(), goalPos, out Vector3 dir);
		return SetVectorGoal(dir, dist, minDist, shouldDeflect);
	}

	public bool FindVectorGoal(out Vector3 result, in Vector3 dir, float targetDist, float minDist = 0, bool shouldDeflect = false) {
		float distAchieved;

		GetOuter()?.Remember(AI_MemoryFlags.TaskExpensive);

		Vector3 testLoc = GetLocalOrigin() + (dir * targetDist);
		GetMoveProbe()!.MoveLimit(GetNavType(), GetLocalOrigin(), testLoc, Mask.NPCSolid, null, out AIMoveTrace moveTrace);

		if (moveTrace.Status != AIMoveResult.Ok) {
			distAchieved = targetDist - moveTrace.DistObstructed;
			if (shouldDeflect && moveTrace.HitNormal != vec3_origin) {
				Vector3 normal = moveTrace.HitNormal;
				if (GetNavType() == Navigation.Ground)
					normal.Z = 0;

				CalculateDeflection(moveTrace.EndPosition, dir, normal, out Vector3 deflect);

				testLoc = moveTrace.EndPosition + (deflect * (targetDist - distAchieved));

				Vector3 temp = moveTrace.EndPosition;
				GetMoveProbe()!.MoveLimit(GetNavType(), temp, testLoc, Mask.NPCSolid, null, out moveTrace);

				distAchieved += (targetDist - distAchieved) - moveTrace.DistObstructed;
			}

			if (distAchieved < minDist + 0.01) {
				result = default;
				return false;
			}
		}

		result = moveTrace.EndPosition;
		return true;
	}

	public bool PrependLocalAvoidance(float distObstacle, in AIMoveTrace directTrace) {
		if (AIStrongOpt())
			return false;

		if (GetOuter()!.IsFlaggedEfficient())
			return false;

		if (TimeLastAvoidanceTriangulate >= gpGlobals.CurTime)
			return false;

		TimeLastAvoidanceTriangulate = gpGlobals.CurTime;

		AI_Waypoint? avoidanceRoute = null;

		Vector3 start = GetLocalOrigin();

		if (distObstacle < GetHullWidth() * 0.5) {
			Vector3 testBackaway = GetCurWaypointPos() - GetLocalOrigin();
			MathLib.VectorNormalize(ref testBackaway);
			testBackaway *= -GetHullWidth();
			testBackaway += GetLocalOrigin();

			AI_MoveLimitFlags backawayFlags = (GetNavType() == Navigation.Ground) ? AI_MoveLimitFlags.TwoD : AI_MoveLimitFlags.Default;

			if (GetMoveProbe()!.MoveLimit(GetNavType(), GetLocalOrigin(), testBackaway,
														 Mask.NPCSolid, GetNavTargetEntity(),
														 100.0f,
														 backawayFlags, out AIMoveTrace backawayTrace)) {
				start = backawayTrace.EndPosition;
				avoidanceRoute = new AI_Waypoint(start, 0, GetNavType(), WaypointFlags.ToDetour, NO_NODE);
			}
		}

		AI_Waypoint? triangulation = GetPathfinder()!.BuildTriangulationRoute(
			start,
			GetCurWaypointPos(),
			GetNavTargetEntity(),
			WaypointFlags.ToDetour,
			NO_NODE,
			0.0f,
			distObstacle,
			GetNavType());

		if (triangulation == null) {
			avoidanceRoute?.Delete();
			return false;
		}

		if (avoidanceRoute != null)
			avoidanceRoute.SetNext(triangulation);
		else
			avoidanceRoute = triangulation;

		float moveThisInterval = GetMotor()!.CalcIntervalMove();
		Vector3 dir = avoidanceRoute.GetPos() - GetLocalOrigin();
		float dist = MathLib.VectorNormalize(ref dir);
		Vector3 testPos;
		if (dist > moveThisInterval) {
			dist = moveThisInterval;
			testPos = GetLocalOrigin() + dir * dist;
		}
		else
			testPos = avoidanceRoute.GetPos();

		AI_MoveLimitFlags flags = (GetNavType() == Navigation.Ground) ? AI_MoveLimitFlags.TwoD : AI_MoveLimitFlags.Default;

		if (!GetMoveProbe()!.MoveLimit(GetNavType(), GetLocalOrigin(), testPos,
													 Mask.NPCSolid, GetNavTargetEntity(),
													 100.0f,
													 flags, out _)) {
			DeleteAll(avoidanceRoute);
			return false;
		}

		GetPath().PrependWaypoints(avoidanceRoute);
		return true;
	}

	public void PrependWaypoint(in Vector3 newPoint, Navigation navType, WaypointFlags waypointFlags = 0) => GetPath().PrependWaypoint(newPoint, navType, waypointFlags);

	public Vector3 GetGoalPos() => GetPath().BaseGoalPosition();

	public BaseEntity? GetGoalTarget() => GetPath().GetTarget();

	public float GetGoalTolerance() => GetPath().GetGoalTolerance();

	public void SetGoalTolerance(float tolerance) => GetPath().SetGoalTolerance(tolerance);

	public bool RefindPathToGoal(bool signalTaskStatus = true, bool dontIgnoreBadLinks = false) => FindPath(signalTaskStatus, dontIgnoreBadLinks);

	public bool UpdateGoalPos(in Vector3 goalPos) {
		if (GetNavType() == Navigation.Jump) {
			DevMsg("Updating goal pos while jumping!\n");
			Assert(false);
			return false;
		}

		Assert((GetPath().GoalType() != NavGoalType.Enemy) && (GetPath().GoalType() != NavGoalType.TargetEnt));

		GetPath().ResetGoalPosition(goalPos);
		if (FindPath(!GetOuter()!.IsCurTaskContinuousMove())) {
			SimplifyPath(true);
			return true;
		}
		return false;
	}

	public Activity SetMovementActivity(Activity activity) => GetPath().SetMovementActivity(activity);

	public void StopMoving(bool immediate = true) {
		if (IsGoalSet()) {
			if (immediate || !SetGoalFromStoppingPath())
				OnNavComplete();
		}
		else
			ClearGoal();
	}

	public bool ClearGoal() {
		ClearPath();
		OnNewGoal();
		return true;
	}

	public int GetMovementSequence() {
		int sequence = GetPath().GetMovementSequence();
		if (sequence == (int)Activity.ACT_INVALID) {
			Activity activity = GetPath().GetMovementActivity();
			Assert(activity != Activity.ACT_INVALID);

			sequence = GetOuter()!.SelectWeightedSequence(GetOuter()!.TranslateActivity(activity));
			if (sequence == (int)Activity.ACT_INVALID) {
				if (activity == Activity.ACT_SCRIPT_CUSTOM_MOVE)
					sequence = GetOuter()!.GetScriptCustomMoveSequence();
				else
					sequence = GetOuter()!.SelectWeightedSequence(GetOuter()!.TranslateActivity(Activity.ACT_WALK));
			}
			Assert(sequence != (int)Activity.ACT_INVALID);
			GetPath().SetMovementSequence(sequence);
		}
		return sequence;
	}

	public void SetMovementSequence(int sequence) => GetPath().SetMovementSequence(sequence);

	public Activity GetMovementActivity() => GetPath().GetMovementActivity();

	public void SetArrivalActivity(Activity activity) => GetPath().SetArrivalActivity(activity);

	public int GetArrivalSequence(int curSequence) {
		int sequence = GetPath().GetArrivalSequence();
		if (sequence == (int)Activity.ACT_INVALID) {
			Activity activity = GetOuter()!.GetStoppedActivity();

			Assert(activity != Activity.ACT_INVALID);
			if (activity == Activity.ACT_INVALID)
				activity = Activity.ACT_IDLE;

			sequence = GetOuter()!.SelectWeightedSequence(GetOuter()!.TranslateActivity(activity), curSequence);

			if (sequence == (int)Activity.ACT_INVALID)
				sequence = GetOuter()!.SelectWeightedSequence(GetOuter()!.TranslateActivity(Activity.ACT_IDLE), curSequence);
			Assert(sequence != (int)Activity.ACT_INVALID);
			GetPath().SetArrivalSequence(sequence);
		}
		return sequence;
	}

	public void SetArrivalSequence(int sequence) {
		GetPath().SetArrivalActivity(Activity.ACT_INVALID);
		GetPath().SetArrivalSequence(sequence);
	}

	public Activity GetArrivalActivity() => GetPath().GetArrivalActivity();

	public void SetArrivalDirection(in Vector3 goalDirection) => GetPath().SetGoalDirection(goalDirection);

	public void SetArrivalDirection(in QAngle goalAngle) {
		MathLib.AngleVectors(goalAngle, out Vector3 goalDirection);
		GetPath().SetGoalDirection(goalDirection);
	}

	public void SetArrivalDirection(BaseEntity? target) => GetPath().SetGoalDirection(target);

	public Vector3 GetArrivalDirection() => GetPath().GetGoalDirection(GetAbsOrigin());

	public void SetArrivalSpeed(float speed) => GetPath().SetGoalSpeed(speed);

	public float GetArrivalSpeed() {
		float speed = GetPath().GetGoalSpeed(GetAbsOrigin());

		if (speed >= 0.0)
			return speed;

		int sequence = GetArrivalSequence((int)Activity.ACT_INVALID);

		if (sequence != (int)Activity.ACT_INVALID) {
			speed = GetOuter()!.GetEntryVelocity(sequence);
			SetArrivalSpeed(speed);
		}
		else
			speed = 0.0f;

		return speed;
	}

	public void SetArrivalDistance(float distance) => GetPath().SetGoalStoppingDistance(distance);

	public float GetArrivalDistance() => GetPath().GetGoalStoppingDistance();

	public Vector3 GetCurWaypointPos() => GetPath().CurWaypointPos();

	public WaypointFlags GetCurWaypointFlags() => GetPath().CurWaypointFlags();

	public NavGoalType GetGoalType() => GetPath().GoalType();

	public AI_NavGoalFlags GetGoalFlags() => GetPath().GoalFlags();

	public bool CurWaypointIsGoal() => GetPath().CurWaypointIsGoal();

	public bool IsGoalSet() => GetPath().GoalType() != NavGoalType.None;

	public bool IsGoalActive() => GetPath() != null && !GetPath().IsEmpty();

	public bool GetPointAlongPath(out Vector3 result, float distance, bool reducibleOnly = false) {
		result = default;
		if (GetPath().GetCurWaypoint() == null)
			return false;

		AI_Waypoint curWaypoint = GetPath().GetCurWaypoint()!;
		AI_Waypoint endPoint = curWaypoint;
		float distRemaining = distance;
		float distToNext;
		Vector3 posPrev = GetLocalOrigin();

		while (endPoint.GetNext() != null) {
			distToNext = ComputePathDistance(GetNavType(), posPrev, endPoint.GetPos());

			if (distToNext > distRemaining)
				break;

			distRemaining -= distToNext;
			posPrev = endPoint.GetPos();
			if (reducibleOnly && !endPoint.IsReducible())
				break;
			endPoint = endPoint.GetNext()!;
		}

		float distToEnd = ComputePathDistance(GetNavType(), posPrev, endPoint.GetPos());
		if (distToEnd - distRemaining < 0.1)
			result = endPoint.GetPos();
		else {
			result = endPoint.GetPos() - posPrev;
			MathLib.VectorNormalize(ref result);
			result *= distRemaining;
			result += posPrev;
		}

		return true;
	}

	public float GetPathDistanceToGoal() => GetPath().GetPathDistanceToGoal(GetAbsOrigin());

	public float GetPathTimeToGoal() {
		if (GetOuter()!.GroundSpeed != 0)
			return GetPathDistanceToGoal() / GetOuter()!.GroundSpeed;
		return 0;
	}

	public int GetNearestNode() => GetPathfinder()!.NearestNodeToNPC();

	public Vector3 GetNodePos(int node) => GetNetwork()!.GetNode(node)!.GetPosition(GetHullType());

	public virtual void OnScheduleChange() { }

	public virtual void OnClearPath() { }

	public virtual void OnNewGoal() {
		ResetCalculations();
		NavComplete = true;
	}

	public virtual void OnNavComplete() {
		ResetCalculations();
		TaskMovementComplete();
		NavComplete = true;
	}

	void OnNavFailed(bool movement = false) {
		if (movement)
			GetOuter()!.OnMovementFailed();

		ResetCalculations();
		NavComplete = true;
		LastNavFailed = true;
	}

	void OnNavFailed(AI_TaskFailureCode code, bool movement = false) {
		if (GetOuter()!.ShouldFailNav(movement)) {
			OnNavFailed(movement);
			SetActivity(GetOuter()!.GetStoppedActivity());
			TaskFail(code);
		}
		else {
			NavFailCounter++;
			LastNavFailTime = gpGlobals.CurTime;
			if (GetOuter()!.ShouldBruteForceFailedNav()) {
				if (movement) {
					TimeBeginFailedSteer = float.MaxValue;

					BaseEntity? blocker = GetBlockingEntity();
					if (blocker != null && !blocker.IsWorld() && !blocker.IsPlayer() && !BaseEntity.FClassnameIs(blocker, "func_tracktrain")) {
						if (NPCPhysics_CreateSolver(GetOuter()!, blocker, true, 10.0f) != null)
							ClearNavFailCounter();
					}

					if (GetNavFailCounter() > 0) {
						if (TeleportAlongPath())
							ClearNavFailCounter();
					}
				}
				else {
					BaseEntity? blocker = GetMoveProbe()!.GetBlockingEntity();
					if (blocker != null) {
						if (NPCPhysics_CreateSolver(GetOuter()!, blocker, true, 10.0f) != null)
							ClearNavFailCounter();
					}
				}
			}
		}
	}

	public int GetNavFailCounter() => NavFailCounter;

	public void ClearNavFailCounter() => NavFailCounter = 0;

	public TimeUnit_t GetLastNavFailTime() => LastNavFailTime;

	public bool TeleportAlongPath() {
		while (GetPath().GetCurWaypoint() != null) {
			Vector3 start = GetPath().CurWaypointPos();
			AdvancePath();

			GetOuter()!.GetMoveProbe()!.FloorPoint(start, Mask.NPCSolid, GetOuter()!.StepHeight(), -64, out Vector3 testPoint);

			if (CanFitAtPosition(testPoint, Mask.NPCSolid, false, false)) {
				if (GetOuter()!.GetMoveProbe()!.CheckStandPosition(testPoint, Mask.NPCSolid)) {
					GetOuter()!.Teleport(testPoint, null, null);
					GetOuter()!.SetGroundEntity(null);
					GetOuter()!.PhysicsTouchTriggers(testPoint);
					return true;
				}
			}

			if (CurWaypointIsGoal())
				break;
		}
		return false;
	}

	public void ResetCalculations() {
		PeerWaitingOn.Set(null);
		PeerWaitMoveTimer.Force();
		PeerWaitClearTimer.Force();

		BigStepGroundEnt.Set(null);

		NextSidestepTimer.Force();

		CalledStartMove = false;

		PosBeginFailedSteer = vec3_invalid;
		TimeBeginFailedSteer = float.MaxValue;

		LastSuccessfulSimplifyTime = -1;

		GetLocalNavigator()!.ResetMoveCalculations();
		GetMotor()!.ResetMoveCalculations();
		GetMoveProbe()!.ClearBlockingEntity();

		NavFailCounter = 0;
		LastNavFailTime = -1;
	}

	public void SetNavType(Navigation navType) => NavType = navType;

	public bool IsInterruptable() => NavType != Navigation.Climb && NavType != Navigation.Jump;

	protected virtual AIMoveResult MoveClimb() {
		Vector3 climbDest = GetPath().CurWaypointPos();
		Vector3 climbDir = climbDest - GetLocalOrigin();
		float climbDist = MathLib.VectorNormalize(ref climbDir);

		if (GetNavType() != Navigation.Climb)
			GetMotor()!.MoveClimbStart(climbDest, climbDir, climbDist, GetPath().CurWaypointYaw());

		SetNavType(Navigation.Climb);

		if (climbDist > 0.01 &&
			 !GetMoveProbe()!.MoveLimit(Navigation.Climb, GetLocalOrigin(), GetLocalOrigin() + (climbDir * Math.Min(0.1f, climbDist - 0.005f)), Mask.NPCSolid, GetNavTargetEntity(), out AIMoveTrace moveTrace)) {
			AI_BaseNPC? other = moveTrace.Obstruction?.MyNPCPointer();
			if (other != null) {
				bool abort = false;

				if (!other.IsMoving())
					abort = true;
				else if (other.GetNavType() == Navigation.Climb && climbDir.Z <= 0.01) {
					Vector3 otherClimbDest = other.GetNavigator()!.GetPath().CurWaypointPos();
					Vector3 otherClimbDir = otherClimbDest - other.GetLocalOrigin();
					MathLib.VectorNormalize(ref otherClimbDir);

					if (Vector3.Dot(otherClimbDir, climbDir) < 0) {
						abort = true;
						if (other.GetNavigator()!.GetStoppingPath(ClippedWaypoints)) {
							TimeClipped = gpGlobals.CurTime;
							SetNavType(Navigation.Ground);
							GetOuter()!.SetGravity(1.0f);
							if (RefindPathToGoal(false))
								abort = false;
							GetOuter()!.SetGravity(0.0f);
							SetNavType(Navigation.Climb);
						}
					}
				}

				if (abort) {
					GetMotor()!.MoveClimbStop();
					SetNavType(Navigation.Ground);
					return AIMoveResult.BlockedNPC;
				}
			}
		}

		int climbNodesLeft = 0;
		AI_Waypoint? waypoint = GetPath().GetCurWaypoint();
		while (waypoint != null && waypoint.NavType() == Navigation.Climb) {
			++climbNodesLeft;
			waypoint = waypoint.GetNext();
		}

		AIMoveResult result = GetMotor()!.MoveClimbExecute(climbDest, climbDir, climbDist, GetPath().CurWaypointYaw(), climbNodesLeft);

		if (result == AIMoveResult.ChangeType) {
			if (GetPath().GetCurWaypoint()!.GetNext() != null)
				AdvancePath();
			else
				OnNavComplete();

			if (GetPath().GetCurWaypoint() == null || GetPath().GetCurWaypoint()!.GetNext() == null || !(GetPath().CurWaypointNavType() == Navigation.Climb)) {
				GetMotor()!.MoveClimbStop();
				SetNavType(Navigation.Ground);
			}
		}
		else if (result != AIMoveResult.Ok) {
			GetMotor()!.MoveClimbStop();
			SetNavType(Navigation.Ground);
			return result;
		}

		return result;
	}

	protected virtual AIMoveResult MoveJump() {
		if ((GetNavType() != Navigation.Jump) && (GetOuter()!.GetFlags() & EntityFlags.OnGround) != 0) {
			GetMoveProbe()!.MoveLimit(Navigation.Jump, GetLocalOrigin(), GetPath().CurWaypointPos(),
				Mask.NPCSolid, GetNavTargetEntity(), out AIMoveTrace moveTrace);
			if (IsMoveBlocked(moveTrace))
				return moveTrace.Status;

			SetNavType(Navigation.Jump);

			GetMotor()!.MoveJumpStart(moveTrace.JumpVelocity);
		}
		else if (GetNavType() == Navigation.Jump && (GetOuter()!.GetFlags() & EntityFlags.OnGround) != 0) {
			AIMoveResult result = GetMotor()!.MoveJumpStop();

			if (result == AIMoveResult.ChangeType) {
				SetNavType(Navigation.Ground);

				if (CurWaypointIsGoal()) {
					OnNavComplete();
					return AIMoveResult.Ok;
				}
				else {
					AdvancePath();
					return AIMoveResult.ChangeType;
				}
			}
			return AIMoveResult.Ok;
		}
		else
			GetMotor()!.MoveJumpExecute();

		return AIMoveResult.Ok;
	}

	protected virtual void MoveCalcBaseGoal(ref AILocalMoveGoal moveGoal) {
		moveGoal.NavType = GetNavType();
		moveGoal.Target = GetPath().CurWaypointPos();
		moveGoal.MaxDist = ComputePathDirection(GetNavType(), GetLocalOrigin(), moveGoal.Target, out moveGoal.Dir);
		moveGoal.Facing = moveGoal.Dir;
		moveGoal.Speed = GetMotor()!.GetSequenceGroundSpeed(GetMovementSequence());
		moveGoal.CurExpectedDist = moveGoal.Speed * GetMotor()!.GetMoveInterval();
		moveGoal.MoveTarget = GetNavTargetEntity();

		if (moveGoal.CurExpectedDist > moveGoal.MaxDist)
			moveGoal.CurExpectedDist = moveGoal.MaxDist;

		if (GetPath().CurWaypointIsGoal())
			moveGoal.Flags |= AILocalMoveGoalFlags.TargetIsGoal;
		else {
			AI_Waypoint curWaypoint = GetPath().GetCurWaypoint()!;
			if (curWaypoint.GetNext() != null && curWaypoint.GetNext()!.NavType() != curWaypoint.NavType())
				moveGoal.Flags |= AILocalMoveGoalFlags.TargetIsTransition;
		}

		Task_t? curTask = GetOuter()!.GetTask();
		if (curTask != null && curTask.Value.Task == TASK_STOP_MOVING)
			moveGoal.Flags |= AILocalMoveGoalFlags.NoSteer | AILocalMoveGoalFlags.NoAvoidancePaths;

		moveGoal.Path = GetPath();
	}

	public virtual bool OnCalcBaseMove(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		if (GetOuter()!.OnCalcBaseMove(ref moveGoal, distClear, ref result))
			return true;

		return false;
	}

	public virtual bool OnObstructionPreSteer(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		bool targetIsGoal = (moveGoal.Flags & AILocalMoveGoalFlags.TargetIsGoal) != 0;
		bool shouldAttemptHit = false;
		bool shouldAdvancePath = false;
		float tolerance = 0;

		if (targetIsGoal) {
			shouldAttemptHit = true;
			tolerance = GetPath().GetGoalTolerance();
		}
		else if ((moveGoal.Flags & AILocalMoveGoalFlags.TargetIsTransition) == 0) {
			shouldAttemptHit = true;
			shouldAdvancePath = true;
			tolerance = GetPath().GetWaypointTolerance();

			if (moveGoal.MaxDist < 4 * 12 && moveGoal.MaxDist - distClear < tolerance)
				tolerance = moveGoal.MaxDist + 1;
		}

		if (shouldAttemptHit) {
			if (distClear > moveGoal.MaxDist) {
				result = AIMoveResult.Ok;
				return true;
			}

			if (moveGoal.MaxDist < tolerance) {
				if (!targetIsGoal ||
					 (moveGoal.DirectTrace.Status != AIMoveResult.BlockedNPC) ||
					 (!((AI_BaseNPC)moveGoal.DirectTrace.Obstruction!).IsMoving())) {
					moveGoal.MaxDist = distClear;
					result = AIMoveResult.Ok;

					if (shouldAdvancePath)
						AdvancePath();
					else if (distClear < 0.025)
						result = moveGoal.DirectTrace.Status;
					return true;
				}
			}
		}

#if HL2_EPISODIC
		if (ai_vehicle_avoidance.GetBool() && moveGoal.DirectTrace.Obstruction != null && moveGoal.DirectTrace.Obstruction.GetServerVehicle() != null) {
			AI_Waypoint? obb = GetPathfinder()!.BuildOBBAvoidanceRoute(GetOuter()!.GetAbsOrigin(),
																		   GetGoalPos(),
																		   moveGoal.DirectTrace.Obstruction,
																		   GetNavTargetEntity(),
																		   GetNavType());

			if (obb != null) {
				GetPath().PrependWaypoints(obb);
				return true;
			}
		}
#endif

		if (GetOuter()!.OnObstructionPreSteer(ref moveGoal, distClear, ref result))
			return true;

		if (BigStepGroundEnt.Get() == null &&
			 moveGoal.DirectTrace.Obstruction != null &&
			 distClear < GetHullWidth() &&
			 moveGoal.DirectTrace.Obstruction == GetOuter()!.GetGroundEntity() &&
			 (moveGoal.DirectTrace.Obstruction.IsPlayer() ||
			   moveGoal.DirectTrace.Obstruction is PhysicsProp)) {
			BigStepGroundEnt.Set(moveGoal.DirectTrace.Obstruction);
			result = AIMoveResult.ChangeType;
			return true;
		}

		return false;
	}

	public virtual bool OnInsufficientStopDist(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		if (GetOuter()!.OnInsufficientStopDist(ref moveGoal, distClear, ref result))
			return true;

		if (!IsMovingOutOfWay(moveGoal, distClear)) {
			float goalDist = ComputePathDistance(GetNavType(), GetAbsOrigin(), GetPath().ActualGoalPosition());

			if (goalDist < GetGoalTolerance() + 0.01) {
				moveGoal.MaxDist = distClear;
				moveGoal.Flags |= AILocalMoveGoalFlags.ConsumeInterval;
				OnNavComplete();
				result = AIMoveResult.Ok;
				return true;
			}

			if (NextSidestepTimer.Expired()) {
				NextSidestepTimer.Reset();

				CalculateDeflection(GetLocalOrigin(), moveGoal.Dir, moveGoal.DirectTrace.HitNormal, out Vector3 deflection);

				for (int i = 1; i > -2; i -= 2) {
					Vector3 testLoc = GetLocalOrigin() + (deflection * GetHullWidth() * 2.0f) * i;
					GetMoveProbe()!.MoveLimit(GetNavType(), GetLocalOrigin(), testLoc, Mask.NPCSolid, null, out AIMoveTrace moveTrace);
					if (moveTrace.Status == AIMoveResult.Ok) {
						Vector3 newWaypoint = moveTrace.EndPosition;
						GetMoveProbe()!.MoveLimit(GetNavType(), newWaypoint, moveGoal.Target, Mask.NPCSolidBrushOnly, null, out moveTrace);
						if (moveTrace.Status == AIMoveResult.Ok) {
							PrependWaypoint(newWaypoint, GetNavType(), WaypointFlags.ToDetour);
							result = AIMoveResult.ChangeType;
							return true;
						}
					}
				}
			}

			if (distClear < 1.0) {
				result = moveGoal.DirectTrace.Status;
				moveGoal.MaxDist = 0;
				return true;
			}
			return false;
		}

		result = AIMoveResult.Ok;
		moveGoal.MaxDist = distClear;
		moveGoal.Flags |= AILocalMoveGoalFlags.ConsumeInterval;
		return true;
	}

	public virtual bool OnFailedSteer(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		if (GetOuter()!.OnFailedSteer(ref moveGoal, distClear, ref result))
			return true;

		if ((moveGoal.Flags & AILocalMoveGoalFlags.TargetIsGoal) != 0) {
			if (distClear >= GetPathDistToGoal()) {
				result = AIMoveResult.Ok;
				return true;
			}

			if (distClear > moveGoal.MaxDist - GetPath().GetGoalTolerance()) {
				Assert(CurWaypointIsGoal() && MathF.Abs(moveGoal.MaxDist - GetPathDistToCurWaypoint()) < 0.01);

				if (moveGoal.MaxDist > distClear)
					moveGoal.MaxDist = distClear;

				if (distClear < 0.125)
					OnNavComplete();

				moveGoal.Flags |= AILocalMoveGoalFlags.ConsumeInterval;
				result = AIMoveResult.Ok;

				return true;
			}
		}

		if ((moveGoal.Flags & AILocalMoveGoalFlags.TargetIsTransition) == 0) {
			float distToWaypoint = GetPathDistToCurWaypoint();
			float halfHull = GetHullWidth() * 0.5f;

			if (distToWaypoint < halfHull) {
				if (distClear > distToWaypoint + halfHull) {
					result = AIMoveResult.Ok;
					return true;
				}
			}
		}

		if (moveGoal.DirectTrace.Status == AIMoveResult.BlockedNPC && moveGoal.DirectTrace.HitNormal != vec3_origin) {
			CalculateDeflection(GetLocalOrigin(), moveGoal.Dir, moveGoal.DirectTrace.HitNormal, out Vector3 deflection);

			if (moveGoal.Dir.X * deflection.X + moveGoal.Dir.Y * deflection.Y > 0.7) {
				Vector3 testLoc = GetLocalOrigin() + (deflection * moveGoal.CurExpectedDist);
				GetMoveProbe()!.MoveLimit(GetNavType(), GetLocalOrigin(), testLoc, Mask.NPCSolid, null, out AIMoveTrace moveTrace);
				if (moveTrace.Status == AIMoveResult.Ok) {
					moveGoal.Dir = deflection;
					moveGoal.MaxDist = moveGoal.CurExpectedDist;
					result = AIMoveResult.Ok;
					return true;
				}
			}
		}

		const float MOVE_TOLERANCE = 12.0f;
		const float TIME_TOLERANCE = 1.0f;

		if (PosBeginFailedSteer == vec3_invalid || (PosBeginFailedSteer - GetAbsOrigin()).LengthSquared() > MOVE_TOLERANCE * MOVE_TOLERANCE) {
			PosBeginFailedSteer = GetAbsOrigin();
			TimeBeginFailedSteer = gpGlobals.CurTime;
		}
		else if (GetNavType() == Navigation.Ground &&
				  gpGlobals.CurTime - TimeBeginFailedSteer > TIME_TOLERANCE &&
				  GetOuter()!.GroundSpeed * TIME_TOLERANCE > MOVE_TOLERANCE) {
			result = AIMoveResult.Illegal;
			return true;
		}

		if ((moveGoal.Flags & AILocalMoveGoalFlags.NoAvoidancePaths) == 0 && distClear < moveGoal.MaxDist) {
			if (PrependLocalAvoidance(distClear, moveGoal.DirectTrace)) {
				result = AIMoveResult.ChangeType;
				return true;
			}
		}

		return false;
	}

	public virtual bool OnFailedLocalNavigation(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		if (GetOuter()!.OnFailedLocalNavigation(ref moveGoal, distClear, ref result))
			return true;

		if (DelayNavigationFailure(moveGoal.DirectTrace)) {
			result = AIMoveResult.Ok;
			moveGoal.MaxDist = distClear;
			moveGoal.Flags |= AILocalMoveGoalFlags.ConsumeInterval;
			return true;
		}

		return false;
	}

	bool DelayNavigationFailure(in AIMoveTrace trace) {
		AI_BaseNPC? blocker = trace.Obstruction?.MyNPCPointer();
		Assert(PeerMoveWait == false || blocker == PeerWaitingOn.Get());

		if (!PeerMoveWait || blocker != PeerWaitingOn.Get()) {
			if (blocker != null) {
				if (PeerWaitingOn.Get() != blocker || PeerWaitClearTimer.Expired()) {
					PeerMoveWait = true;
					PeerWaitingOn.Set(blocker);
					PeerWaitMoveTimer.Reset();
					PeerWaitClearTimer.Reset();

					if (blocker.GetGroundEntity() == GetOuter()) {
						blocker.GetMoveProbe()!.TraceHull(blocker.GetAbsOrigin(),
															 blocker.GetAbsOrigin() + new Vector3(0, 0, 2.0f),
															 Mask.NPCSolid,
															 out Trace bumpTrace);
						if (bumpTrace.Fraction == 1.0)
							Util.SetOrigin(blocker, bumpTrace.EndPos, true);
					}
				}
				else if (PeerWaitingOn.Get() == blocker && !PeerWaitMoveTimer.Expired())
					PeerMoveWait = true;
			}
		}

		return PeerMoveWait;
	}

	protected virtual bool MoveUpdateWaypoint(ref AIMoveResult result) {
		AI_Waypoint curWaypoint = GetPath().GetCurWaypoint()!;
		float waypointDist = ComputePathDistance(GetNavType(), GetLocalOrigin(), curWaypoint.GetPos());
		bool isGoal = CurWaypointIsGoal();
		float tolerance = Physics.npc_vphysics.GetBool() ? 0.25f : 0.0625f;

		if (waypointDist <= tolerance) {
			if (isGoal) {
				OnNavComplete();
				result = AIMoveResult.Ok;
			}
			else {
				AdvancePath();
				result = AIMoveResult.ChangeType;
			}
			return true;
		}

		return false;
	}

	public virtual bool OnMoveStalled(in AILocalMoveGoal move) {
		SetActivity(GetOuter()!.GetStoppedActivity());
		return true;
	}

	public virtual bool OnMoveExecuteFailed(in AILocalMoveGoal move, in AIMoveTrace trace, AIMotorMoveResult motorResult, ref AIMoveResult result) {
		if (GetOuter()!.OnMoveExecuteFailed(move, trace, motorResult, ref result))
			return true;

		if (BigStepGroundEnt.Get() == null &&
			 trace.Obstruction != null &&
			 trace.TotalDist - trace.DistObstructed < GetHullWidth() &&
			 trace.Obstruction == GetOuter()!.GetGroundEntity() &&
			 (trace.Obstruction.IsPlayer() ||
			   trace.Obstruction is PhysicsProp)) {
			BigStepGroundEnt.Set(trace.Obstruction);
			result = AIMoveResult.ChangeType;
			return true;
		}

		if (motorResult == AIMotorMoveResult.PartialHitTarget) {
			OnNavComplete();
			result = AIMoveResult.Ok;
		}
		else if (motorResult == AIMotorMoveResult.PartialHitNPC && DelayNavigationFailure(trace))
			result = AIMoveResult.Ok;

		return true;
	}

	public virtual bool OnMoveBlocked(ref AIMoveResult result) {
		if (result == AIMoveResult.BlockedNPC &&
			 GetPath().GetCurWaypoint() != null &&
			 (GetPath().GetCurWaypoint()!.Flags() & WaypointFlags.ToDoor) != 0) {
			if (GetPath().GetCurWaypoint()!.Data.Get() is BasePropDoor door) {
				GetOuter()!.OpenPropDoorBegin(door);
				result = AIMoveResult.Ok;
				return true;
			}
		}

		if (GetOuter()!.OnMoveBlocked(ref result))
			return true;

		float waypointDist;

		if (!GetPath().CurWaypointIsGoal() && GetPath().GetCurWaypoint()!.IsReducible()) {
			waypointDist = ComputePathDistance(GetNavType(), GetLocalOrigin(), GetCurWaypointPos());
			if (waypointDist < GetHullWidth()) {
				AdvancePath();
				result = AIMoveResult.ChangeType;
			}
		}

		SetActivity(GetOuter()!.GetStoppedActivity());

		const float EPS = 0.1f;

		waypointDist = ComputePathDistance(GetNavType(), GetLocalOrigin(), GetPath().ActualGoalPosition());

		if (waypointDist < GetGoalTolerance() + EPS) {
			OnNavComplete();
			result = AIMoveResult.Ok;
			return true;
		}

		return false;
	}

	protected virtual AIMoveResult MoveEnact(in AILocalMoveGoal baseMove) {
		AIMoveResult result;
		AILocalMoveGoal move = baseMove;

		result = GetLocalNavigator()!.MoveCalc(ref move, LastSuccessfulSimplifyTime == gpGlobals.CurTime);

		if (result != AIMoveResult.Ok)
			LastBlockingEnt.Set(move.DirectTrace.Obstruction);
		else {
			LastBlockingEnt.Set(null);
			GetMoveProbe()!.ClearBlockingEntity();
		}

		if (result == AIMoveResult.Ok && !NavComplete) {
			Assert(GetPath().GetCurWaypoint() != null);
			result = GetMotor()!.MoveNormalExecute(move);
		}
		else if (result != AIMoveResult.ChangeType)
			GetMotor()!.MoveStop();

		if (IsMoveBlocked(result))
			OnMoveBlocked(ref result);

		return result;
	}

	protected virtual AIMoveResult MoveNormal() {
		if (!PreMove())
			return AIMoveResult.Illegal;

		AIMoveResult result = AIMoveResult.Illegal;

		if (MoveUpdateWaypoint(ref result))
			return result;

		float preMoveSpeed = GetIdealSpeed();
		Activity preMoveActivity = GetActivity();
		int preMoveSequence = GetOuter()!.GetSequence();
		Vector3 start = GetAbsOrigin();

		SetActivity(GetMovementActivity());

		if (ValidateActivitySpeed && GetIdealSpeed() <= 0.0f) {
			if (GetActivity() == Activity.ACT_TRANSITION)
				return AIMoveResult.Ok;
			DevMsg($"{GetOuter()!.GetClassname()} moving with speed <= 0 ({GetOuter()!.GetSequenceName(GetOuter()!.GetSequence())})\n");
		}

		AILocalMoveGoal move = default;

		MoveCalcBaseGoal(ref move);

		result = MoveEnact(move);

		if (result == AIMoveResult.Ok && preMoveSpeed < 0.01) {
			if ((GetAbsOrigin() - start).Length() < 0.01) {
				GetOuter()!.SetSequence(preMoveSequence);
				SetActivity(preMoveActivity);
			}
		}

		return result;
	}

	bool PreMove() {
		Navigation goalType = GetPath().CurWaypointNavType();
		Navigation curType = GetNavType();

		PeerMoveWait = false;

		if (goalType == Navigation.Ground && curType != Navigation.Ground) {
			DevMsg($"Warning: {GetOuter()!.GetClassname()}({GetOuter()!.GetEntityName()}) appears to have wrong nav type in CAI_Navigator::MoveGround()\n");
			switch (curType) {
				case Navigation.Climb:
					GetMotor()!.MoveClimbStop();
					break;

				case Navigation.Jump:
					GetMotor()!.MoveJumpStop();
					break;
			}

			SetNavType(Navigation.Ground);
		}
		else if (goalType == Navigation.Fly && curType != Navigation.Fly) {
			AssertMsg(false, "GetNavType() == NAV_FLY");
			return false;
		}

		Assert(GetMotor()!.GetMoveInterval() > 0);

		SimplifyPath();

		return true;
	}

	bool IsMovingOutOfWay(in AILocalMoveGoal moveGoal, float distClear) {
		AI_BaseNPC? blocker = moveGoal.DirectTrace.Obstruction?.MyNPCPointer();

		if (blocker == null)
			return false;

		if (blocker.IsMoving()) {
			if (distClear > moveGoal.CurExpectedDist * 0.75)
				return true;

			ref readonly Vector3 blockerVel = ref blocker.GetMotor()!.GetCurVel();
			Vector2 velBlockerNorm = new(blockerVel.X, blockerVel.Y);

			MathLib.Vector2DNormalize(ref velBlockerNorm);

			float dot = moveGoal.Dir.X * velBlockerNorm.X + moveGoal.Dir.Y * velBlockerNorm.Y;

			if (dot > -0.25)
				return true;
		}

		return false;
	}

	enum AINavResult
	{
		Ok,
		NoGoal,
		NoRoute,
		Blocked,
		Illegal
	}

	static readonly AI_TaskFailureCode[] NavResultFailures = [
		AI_TaskFailureCode.NoTaskFailure,
		AI_TaskFailureCode.NoRouteGoal,
		AI_TaskFailureCode.NoRoute,
		AI_TaskFailureCode.NoRouteBlocked,
		AI_TaskFailureCode.NoRouteIllegal
	];

	public virtual bool Move(float interval = 0.1f) {
		if (interval > 1.0)
			interval = 1.0f;

		if (!GetOuter()!.OverrideMove(interval)) {
			bool hasGoal = GetGoalType() != NavGoalType.None;
			bool isTurning = HasMemory(AI_MemoryFlags.Turning);
			if (hasGoal) {
				if (isTurning) {
					if (gpGlobals.CurTime - GetPath().GetStartTime() > 5) {
						Forget(AI_MemoryFlags.Turning);
						isTurning = false;
					}
				}

				if (ActivityIsLocomotive(PreviousMoveActivity) && !ActivityIsLocomotive(GetMovementActivity()))
					SetMovementActivity(GetOuter()!.TranslateActivity(PreviousMoveActivity));
			}
			else {
				PreviousMoveActivity = Activity.ACT_RESET;
				PreviousArrivalActivity = Activity.ACT_RESET;
			}

			bool shouldMove = hasGoal && ActivityIsLocomotive(GetMovementActivity());
			if (shouldMove) {
				AINavResult result = AINavResult.Ok;

				GetMotor()!.SetMoveInterval(interval);

				if (GetPath().GoalType() == NavGoalType.None) {
					DevWarning("Move requested with no route!\n");
					result = AINavResult.NoGoal;
				}
				else if (GetPath().GetCurWaypoint() == null) {
					DevWarning("Move goal with no route!\n");
					GetPath().Clear();
					result = AINavResult.NoRoute;
				}

				if (result == AINavResult.Ok) {
					if (GetOuter()!.ShouldMoveWait()) {
						GetMotor()!.MovePaused();
						return false;
					}

					int loopCount = 0;

					bool moved = false;
					AIMoveResult moveResult = AIMoveResult.ChangeType;
					NavComplete = false;

					while (moveResult >= AIMoveResult.Ok && !NavComplete) {
						if (GetMotor()!.GetMoveInterval() <= 0) {
							moveResult = AIMoveResult.Ok;
							break;
						}

						if (!CalledStartMove) {
							GetMotor()!.MoveStart();
							CalledStartMove = true;
						}

						if (BigStepGroundEnt.Get() != null && BigStepGroundEnt.Get() != GetOuter()!.GetGroundEntity())
							BigStepGroundEnt.Set(null);

						switch (GetPath().CurWaypointNavType()) {
							case Navigation.Climb:
								moveResult = MoveClimb();
								break;

							case Navigation.Jump:
								moveResult = MoveJump();
								break;

							case Navigation.Ground:
							case Navigation.Fly:
								moveResult = MoveNormal();
								break;

							default:
								DevMsg("Bogus route move type!");
								moveResult = AIMoveResult.Illegal;
								break;
						}

						if (moveResult == AIMoveResult.Ok)
							moved = true;

						++loopCount;
						if (loopCount > 16) {
							DevMsg($"ERROR: {GetOuter()!.GetDebugName()} navigation not terminating. Possibly bad cyclical solving?\n");
							moveResult = AIMoveResult.Illegal;

							switch (GetPath().CurWaypointNavType()) {
								case Navigation.Ground:
								case Navigation.Fly:
									OnMoveBlocked(ref moveResult);
									break;
							}
							break;
						}
					}

					if (IsMoveBlocked(moveResult)) {
						bool recovered = false;
						if (moveResult != AIMoveResult.BlockedNPC || GetNavType() == Navigation.Climb || GetNavType() == Navigation.Jump || GetPath().CurWaypointNavType() == Navigation.Jump) {
							if (MarkCurWaypointFailedLink()) {
								AI_Waypoint? savedWaypoints = GetPath().GetCurWaypoint();
								if (savedWaypoints != null) {
									GetPath().SetWaypoints(null);
									if (RefindPathToGoal(false, true)) {
										DeleteAll(savedWaypoints);
										recovered = true;
									}
									else
										GetPath().SetWaypoints(savedWaypoints);
								}
							}
						}

						if (!recovered)
							OnNavFailed((moveResult == AIMoveResult.Illegal) ? AI_TaskFailureCode.NoRouteIllegal : AI_TaskFailureCode.NoRouteBlocked, true);
					}
					return moved;
				}

				OnNavFailed(NavResultFailures[(int)result], false);
			}
			else {
				if (GetNavType() == Navigation.Climb) {
					GetMotor()!.MoveClimbStop();
					SetNavType(Navigation.Ground);
				}
				GetMotor()!.MoveStop();
				AssertMsg(GetOuter()!.TaskIsRunning() || GetOuter()!.TaskIsComplete(), "Schedule stalled!!\n");
			}
			return false;
		}

		return true;
	}

	public float GetIdealSpeed() => GetOuter()!.GetIdealSpeed();

	public virtual float CalcYawSpeed() {
		float npcYaw = GetOuter()!.CalcYawSpeed();
		if (npcYaw >= 0.0f)
			return npcYaw;

		float maxYaw = MaxYawSpeed();

		if (IsGoalSet() && GetIdealSpeed() != 0.0) {
			if (GetPath().GetCurWaypoint() == null)
				return maxYaw;

			if (GetIdealSpeed() > 0) {
				float waypointDist = (GetPath().CurWaypointPos() - GetLocalOrigin()).Length();

				if (waypointDist < 100) {
					float scale = 1 + (0.01f * (100 - waypointDist));
					return maxYaw * scale;
				}
			}
		}
		return maxYaw;
	}

	public float GetStepDownMultiplier() {
		BaseEntity? bigStepGroundEnt = BigStepGroundEnt.Get();
		if (bigStepGroundEnt != null) {
			if (!bigStepGroundEnt.IsPlayer())
				return 2.6f;
			else
				return 10.0f;
		}
		return 1.0f;
	}

	public void SetAllowBigStep(BaseEntity? entToStepOff) {
		if (entToStepOff == null || !entToStepOff.IsWorld())
			BigStepGroundEnt.Set(entToStepOff);
	}

	public void AdvancePath() {
		AI_Waypoint curWaypoint = GetPath().GetCurWaypoint()!;
		bool passingPathcorner = (curWaypoint.Flags() & WaypointFlags.ToPathCorner) != 0;

		if (passingPathcorner) {
			Assert(curWaypoint.GetNext() == null || (curWaypoint.GetNext()!.Flags() & (WaypointFlags.ToPathCorner | WaypointFlags.ToGoal)) == (WaypointFlags.ToPathCorner | WaypointFlags.ToGoal));
			BaseEntity? entity = curWaypoint.PathCorner.Get();
			if (entity != null) {
				Variant_t emptyVariant = default;
				entity.AcceptInput("InPass", GetOuter(), entity, emptyVariant, 0);
			}
		}

		if (GetPath().CurWaypointIsGoal())
			return;

		if ((curWaypoint.Flags() & WaypointFlags.ToDoor) != 0) {
			if (curWaypoint.Data.Get() is BasePropDoor door)
				GetOuter()!.OpenPropDoorBegin(door);
			else
				DevMsg($"{GetOuter()!.GetDebugName()} trying to open a door that has been deleted!\n");
		}

		GetPath().Advance();

		if (passingPathcorner) {
			AI_Waypoint? newWaypoint = GetPath().GetCurWaypoint();

			if (newWaypoint != null) {
				Assert((newWaypoint.Flags() & (WaypointFlags.ToPathCorner | WaypointFlags.ToGoal)) == (WaypointFlags.ToPathCorner | WaypointFlags.ToGoal));

				GetOuter()!.SetGoalEnt(newWaypoint.PathCorner.Get());
				DoFindPathToPathcorner(newWaypoint.PathCorner.Get());
			}
		}
	}

	bool ShouldAttemptSimplifyTo(in Vector3 pos) {
		if (ForcedSimplify)
			return true;

		Vector3 vecToPos = pos - GetLocalOrigin();
		vecToPos.Z = 0;
		MathLib.VectorNormalize(ref vecToPos);

		Vector3 vecCurrentDirectionOfMovement = GetCurWaypointPos() - GetLocalOrigin();
		vecCurrentDirectionOfMovement.Z = 0;
		MathLib.VectorNormalize(ref vecCurrentDirectionOfMovement);

		float dot = vecCurrentDirectionOfMovement.X * vecToPos.X + vecCurrentDirectionOfMovement.Y * vecToPos.Y;

		return ForcedSimplify || dot > MIN_ANGLE_COS_SIMPLIFY;
	}

	bool ShouldSimplifyTo(bool passedDetour, in Vector3 pos) {
		AI_MoveLimitFlags flags = AI_MoveLimitFlags.QuickReject;

		if (!passedDetour)
			flags |= AI_MoveLimitFlags.IgnoreTransients;

		GetMoveProbe()!.MoveLimit(GetNavType(),
			GetLocalOrigin(), pos, Mask.NPCSolid,
			GetPath().GetTarget(), 100, flags, out AIMoveTrace moveTrace);

		return !IsMoveBlocked(moveTrace);
	}

	void SimplifyPathInsertSimplification(AI_Waypoint segmentStart, in Vector3 point) {
		if (point != segmentStart.GetPos()) {
			AI_Waypoint? nextWaypoint = segmentStart.GetNext();
			Assert(nextWaypoint != null);
			Assert(segmentStart.NavType() == nextWaypoint!.NavType());

			AI_Waypoint newWaypoint = new(point, 0, segmentStart.NavType(), 0, NO_NODE);

			while (GetPath().GetCurWaypoint() != nextWaypoint) {
				Assert(GetPath().GetCurWaypoint()!.IsReducible());
				GetPath().Advance();
			}
			newWaypoint.SetNext(nextWaypoint);
			GetPath().SetWaypoints(newWaypoint);
		}
		else {
			while (GetPath().GetCurWaypoint() != segmentStart) {
				Assert(GetPath().GetCurWaypoint()!.IsReducible());
				GetPath().Advance();
			}
		}
	}

	bool SimplifyPathForwardScan(in SimplifyForwardScanParams parms, AI_Waypoint curWaypoint, in Vector3 curPoint, float distRemaining, bool skipTest, bool passedDetour, ref int testCount) {
		AI_Waypoint? nextWaypoint = curWaypoint.GetNext();

		if (!passedDetour)
			passedDetour = (curWaypoint.Flags() & WaypointFlags.ToDetour) != 0;

		if (distRemaining > 0) {
			if (curWaypoint.IsReducible()) {
				AI_Waypoint recursionWaypoint;
				Vector3 nextPoint;

				float distToNext = ComputePathDistance(GetNavType(), curPoint, nextWaypoint!.GetPos());
				if (distToNext < parms.Increment * 1.10) {
					nextPoint = nextWaypoint.GetPos();
					distRemaining -= distToNext;
					recursionWaypoint = nextWaypoint;
				}
				else {
					Vector3 offset = nextWaypoint.GetPos() - curWaypoint.GetPos();
					MathLib.VectorNormalize(ref offset);
					offset *= parms.Increment;
					nextPoint = curPoint + offset;
					distRemaining -= parms.Increment;
					recursionWaypoint = curWaypoint;
				}

				bool skipTestNext = ComputePathDistance(GetNavType(), GetLocalOrigin(), nextPoint) > parms.Radius + 0.1;

				if (SimplifyPathForwardScan(parms, recursionWaypoint, nextPoint, distRemaining, skipTestNext, passedDetour, ref testCount))
					return true;
			}
		}

		if (!skipTest && testCount < parms.MaxSamples && ShouldAttemptSimplifyTo(curPoint)) {
			testCount++;

			if (ShouldSimplifyTo(passedDetour, curPoint)) {
				SimplifyPathInsertSimplification(curWaypoint, curPoint);
				return true;
			}
		}

		return false;
	}

	bool SimplifyPathForwardScan(in SimplifyForwardScanParams parms) {
		AI_Waypoint curWaypoint = GetPath().GetCurWaypoint()!;
		float distRemaining = parms.ScanDist - GetPathDistToCurWaypoint();
		int testCount = 0;

		if (distRemaining < 0.1)
			return false;

		if (SimplifyPathForwardScan(parms, curWaypoint, curWaypoint.GetPos(), distRemaining, true, false, ref testCount))
			return true;

		return false;
	}

	bool SimplifyPathForward(float maxDist = -1) {
		AI_Waypoint curWaypoint = GetPath().GetCurWaypoint()!;
		AI_Waypoint? nextWaypoint = curWaypoint.GetNext();

		if (nextWaypoint == null)
			return false;

		SimplifyForwardScanParams scanParams = fullScanParams;
		if (maxDist > fullScanParams.Radius) {
			float ratio = maxDist / fullScanParams.Radius;

			fullScanParams.Radius = maxDist;
			fullScanParams.ScanDist *= ratio;
			fullScanParams.Increment *= ratio;
		}

		if (SimplifyPathForwardScan(scanParams))
			return true;

		if (ShouldAttemptSimplifyTo(nextWaypoint.GetPos()) &&
			 ComputePathDistance(GetNavType(), GetLocalOrigin(), nextWaypoint.GetPos()) < scanParams.ScanDist &&
			 ShouldSimplifyTo((curWaypoint.Flags() & WaypointFlags.ToDetour) != 0, nextWaypoint.GetPos())) {
			curWaypoint.Delete();
			GetPath().SetWaypoints(nextWaypoint);
			return true;
		}

		return false;
	}

	bool SimplifyPathBacktrack() {
		AI_Waypoint curWaypoint = GetPath().GetCurWaypoint()!;
		AI_Waypoint? nextWaypoint = curWaypoint.GetNext();

		if (curWaypoint.GetNext() != null &&
			(nextWaypoint!.Flags() & WaypointFlags.ToNode) != 0 &&
			(nextWaypoint.NavType() == Navigation.Ground) &&
			(curWaypoint.NavType() == Navigation.Ground) &&
			(curWaypoint.Flags() & WaypointFlags.ToNode) != 0) {

			Vector3 firstToMe = GetLocalOrigin() - curWaypoint.GetPos();
			Vector3 firstToNext = nextWaypoint.GetPos() - curWaypoint.GetPos();
			MathLib.VectorNormalize(ref firstToNext);
			firstToMe.Z = 0;
			firstToNext.Z = 0;
			float firstDist = firstToMe.Length();
			float firstProj = Vector3.Dot(firstToMe, firstToNext);
			float goalTolerance = GetPath().GetGoalTolerance();
			if (firstProj > 0.5 * firstDist) {
				Vector3 targetPos = curWaypoint.GetPos() + (firstProj * firstToNext);

				RouteBuildFlags buildFlags = 0;
				if ((CapabilitiesGet() & Capability.MoveGround) != 0)
					buildFlags |= RouteBuildFlags.Ground;
				if ((CapabilitiesGet() & Capability.MoveJump) != 0)
					buildFlags |= RouteBuildFlags.Jump;

				AI_Waypoint? route1 = GetPathfinder()!.BuildLocalRoute(GetLocalOrigin(), targetPos, GetPath().GetTarget(), WaypointFlags.ToDetour, NO_NODE, buildFlags, goalTolerance);
				if (route1 == null)
					return false;

				AI_Waypoint? route2 = GetPathfinder()!.BuildLocalRoute(targetPos, nextWaypoint.GetPos(), GetPath().GetTarget(), WaypointFlags.ToDetour, NO_NODE, buildFlags, goalTolerance);
				if (route2 == null) {
					DeleteAll(route1);
					return false;
				}

				AddWaypointLists(route1, route2);

				AddWaypointLists(route1, nextWaypoint.GetNext());

				AI_Waypoint freeMe = curWaypoint.GetNext()!;
				curWaypoint.Delete();
				freeMe.Delete();

				GetPath().SetWaypoints(route1);
				return true;
			}
		}
		return false;
	}

	bool SimplifyPathQuick() {
		if (SimplifyPathForwardScan(quickScanParams[AIStrongOpt() ? 1 : 0]))
			return true;

		return false;
	}

	public virtual bool SimplifyPath(bool firstForPath = false, float scanDist = -1) {
		bool inPVS = GetOuter()!.HasCondition((int)SCOND_t.COND_IN_PVS);
		bool retVal = false;

		Navigation navType = GetOuter()!.GetNavType();
		if (navType == Navigation.Ground || navType == Navigation.Fly) {
			AI_Waypoint? curWaypoint = GetPath().GetCurWaypoint();
			if (curWaypoint == null || !curWaypoint.IsReducible())
				return false;

			bool fullSimplify;

			fullSimplify = NextSimplifyTime <= gpGlobals.CurTime;

			if (firstForPath && !fullSimplify)
				fullSimplify = inPVS;

			if (AIStrongOpt() && fullSimplify) {
				if (g_iFrameLastSimplified != gpGlobals.FrameCount)
					g_iFrameLastSimplified = gpGlobals.FrameCount;
				else
					fullSimplify = false;
			}

			ForcedSimplify = firstForPath;

			if (fullSimplify) {
				float simplifyDelay = inPVS ? ROUTE_SIMPLIFY_TIME_DELAY[AIStrongOpt() ? 1 : 0] : NO_PVS_ROUTE_SIMPLIFY_TIME_DELAY[AIStrongOpt() ? 1 : 0];

				if (GetOuter()!.GetMoveEfficiency() > AI_MoveEfficiency.Normal)
					simplifyDelay *= 2;

				NextSimplifyTime = gpGlobals.CurTime + simplifyDelay;

				if (SimplifyPathForward(scanDist))
					retVal = true;
				else if (SimplifyPathBacktrack())
					retVal = true;
				else if (SimplifyPathQuick())
					retVal = true;
			}
			else if (firstForPath || (inPVS && GetOuter()!.GetMoveEfficiency() == AI_MoveEfficiency.Normal)) {
				if (!AIStrongOpt() || gpGlobals.CurTime - LastSuccessfulSimplifyTime > QUICK_SIMPLIFY_TIME_DELAY[AIStrongOpt() ? 1 : 0]) {
					if (SimplifyPathQuick())
						retVal = true;
				}
			}
		}

		if (retVal)
			LastSuccessfulSimplifyTime = gpGlobals.CurTime;

		return retVal;
	}

	public AI_NavPathProgress ProgressFlyPath(in AI_ProgressFlyPathParams parms) {
		if (IsGoalActive()) {
			float waypointDist = (GetCurWaypointPos() - GetLocalOrigin()).Length();

			if (CurWaypointIsGoal()) {
				float tolerance = Math.Max(parms.GoalTolerance, GetPath().GetGoalTolerance());
				if (waypointDist <= tolerance)
					return AI_NavPathProgress.Complete;
			}
			else {
				bool isStrictWaypoint = (GetPath().CurWaypointFlags() & (WaypointFlags.ToPathCorner | WaypointFlags.DontSimplify)) != 0;
				float tolerance = isStrictWaypoint ? parms.StrictPointTolerance : parms.WaypointTolerance;
				if (waypointDist <= tolerance) {
					Util.TraceLine(GetAbsOrigin(), GetPath().GetCurWaypoint()!.GetNext()!.GetPos(), Mask.NPCSolid, GetOuter(), CollisionGroup.None, out Trace tr);
					if (tr.Fraction == 1.0f) {
						AdvancePath();
						return AI_NavPathProgress.Advanced;
					}
				}

				if (SimplifyFlyPath(parms))
					return AI_NavPathProgress.Advanced;
			}
		}

		return AI_NavPathProgress.NoChange;
	}

	public void SimplifyFlyPath(Mask collisionMask, BaseEntity? target, float strictPointTolerance = 32.0f, float blockTolerance = 0.0f, AI_NpcBlockHandling blockHandling = AI_NpcBlockHandling.Block) {
		AI_ProgressFlyPathParams parms = new(collisionMask, strictPointTolerance, blockTolerance, 0, 0, blockHandling);
		parms.SetCurrent(target);
		SimplifyFlyPath(parms);
	}

	public bool SimplifyFlyPath(in AI_ProgressFlyPathParams parms) {
		if (GetPath().GetCurWaypoint() == null)
			return false;

		if (NextSimplifyTime > gpGlobals.CurTime)
			return false;

		NextSimplifyTime = gpGlobals.CurTime + FLY_ROUTE_SIMPLIFY_TIME_DELAY;

		if (parms.TrySimplify && SimplifyPathForward(FLY_ROUTE_SIMPLIFY_LOOK_DIST))
			return true;

		bool isStrictWaypoint = !parms.TrySimplify || (GetPath().CurWaypointFlags() & (WaypointFlags.ToPathCorner | WaypointFlags.DontSimplify)) != 0;

		Vector3 dir = GetCurWaypointPos() - GetLocalOrigin();
		float length = MathLib.VectorNormalize(ref dir);

		if (!isStrictWaypoint || length < parms.StrictPointTolerance) {
			if (GetPath().CurWaypointIsGoal())
				return false;

			GetMoveProbe()!.MoveLimit(Navigation.Fly, GetLocalOrigin(), GetPath().NextWaypointPos(),
				parms.CollisionMask, parms.Target, out AIMoveTrace moveTrace);

			if (moveTrace.DistObstructed - parms.BlockTolerance < 0.01 ||
				 ((parms.BlockHandling == AI_NpcBlockHandling.Ignore) && (moveTrace.Status == AIMoveResult.BlockedNPC))) {
				AdvancePath();
				return true;
			}
			else if (moveTrace.Obstruction != null && parms.BlockHandling == AI_NpcBlockHandling.Avoid)
				PrependLocalAvoidance(parms.BlockTolerance - moveTrace.DistObstructed, moveTrace);
		}

		return false;
	}

	public float MovementCost(Capability moveType, in Vector3 start, in Vector3 end) {
		float cost = (start - end).Length();

		if (moveType == Capability.MoveJump || moveType == Capability.MoveClimb)
			cost *= 2.0f;

		GetOuter()!.MovementCost(moveType, start, end, ref cost);

		return cost;
	}

	public bool CanFitAtNode(int nodeNum, Mask collisionMask = Mask.NPCSolidBrushOnly) {
		if (GetNetwork() == null) {
			DevMsg("CanFitAtNode() called with no network!\n");
			return false;
		}

		AI_Node testNode = GetNetwork()!.GetNode(nodeNum)!;
		Vector3 startPos = testNode.GetPosition(GetOuter()!.GetHullType());

		if (testNode.NodeType == NodeType.Ground) {
			if (!GetMoveProbe()!.CheckStandPosition(startPos, collisionMask))
				return false;
		}

		if (testNode.NodeType == NodeType.Climb &&
			(testNode.NodeInfo & (NodeInfoBits.ClimbBottom | NodeInfoBits.ClimbExit)) != 0) {
			if (!GetMoveProbe()!.CheckStandPosition(startPos, collisionMask))
				return false;
		}

		if (!CanFitAtPosition(startPos, collisionMask)) {
			startPos.Z += GetOuter()!.StepHeight();
			if (!CanFitAtPosition(startPos, collisionMask))
				return false;
		}

		return true;
	}

	public bool CanFitAtPosition(in Vector3 startPos, Mask collisionMask, bool ignoreTransients = false, bool allowPlayerAvoid = true) {
		TraceFilterNav traceFilter = new(GetOuter()!, ignoreTransients, GetOuter(), CollisionGroup.None, allowPlayerAvoid);

		Vector3 endPos = startPos;
		endPos.Z += 0.01f;
		Util.TraceHull(startPos, endPos, GetOuter()!.GetHullMins(), GetOuter()!.GetHullMaxs(), collisionMask, ref traceFilter, out Trace tr);

		if (tr.StartSolid)
			return false;
		return true;
	}

	public bool IsOnNetwork() => !NotOnNetwork;

	public void SetMaxRouteRebuildTime(float time) => TimePathRebuildMax = time;

	public float GetPathDistToCurWaypoint() {
		return GetPath().GetCurWaypoint() != null ?
					ComputePathDistance(GetNavType(), GetLocalOrigin(), GetPath().CurWaypointPos()) :
					0;
	}

	public float BuildAndGetPathDistToGoal() {
		if (GetPath() == null)
			return -1;

		GetPath().GetPathLength();

		if (GetPath().GetCurWaypoint() != null && (GetPath().GetCurWaypoint()!.PathDistGoal >= 0))
			return GetPathDistToGoal();

		return -1;
	}

	public float GetPathDistToGoal() {
		return GetPath().GetCurWaypoint() != null ?
					(GetPathDistToCurWaypoint() + GetPath().GetCurWaypoint()!.PathDistGoal) :
					0;
	}

	bool FindPath(bool signalTaskStatus = true, bool dontIgnoreBadLinks = false) {
		if (ai_navigator_generate_spikes.GetBool()) {
			uint largeCount = (uint)(int.MaxValue >> ai_navigator_generate_spikes_strength.GetInt());
			while (largeCount-- != 0) { }
		}

		bool retrying = HasMemory(AI_MemoryFlags.PathFailed) && TimePathRebuildMax != 0;
		if (retrying) {
			if (TimePathRebuildFail < gpGlobals.CurTime) {
				if (signalTaskStatus)
					OnNavFailed(AI_TaskFailureCode.NoRoute);
				else
					OnNavFailed();
				return false;
			}
			else if (TimePathRebuildNext > gpGlobals.CurTime)
				return false;
		}

		bool findResult = DoFindPath();

		if (!dontIgnoreBadLinks && !findResult && GetOuter()!.IsNavigationUrgent()) {
			GetPathfinder()!.SetIgnoreBadLinks();
			findResult = DoFindPath();
		}

		if (findResult) {
			Forget(AI_MemoryFlags.PathFailed);

			if (signalTaskStatus)
				TaskComplete();
			return true;
		}

		if (TimePathRebuildMax == 0) {
			if (signalTaskStatus)
				OnNavFailed(AI_TaskFailureCode.NoRoute);
			else
				OnNavFailed();
			return false;
		}
		else {
			if (!retrying) {
				Remember(AI_MemoryFlags.PathFailed);
				TimePathRebuildFail = gpGlobals.CurTime + TimePathRebuildMax;
			}
			TimePathRebuildNext = gpGlobals.CurTime + TimePathRebuildDelay;
			return false;
		}
	}

	bool MarkCurWaypointFailedLink() {
		if (!RememberStaleNodes)
			return false;

		if (GetPath() == null || GetPath().GetCurWaypoint() == null)
			return false;

		bool didMark = false;

		int startID = GetPath().GetLastNodeReached();
		int endID = GetPath().GetCurWaypoint()!.NodeID;

		if (endID != NO_NODE) {
			bool blockAll = false;

			BaseEntity? lastBlockingEnt = LastBlockingEnt.Get();
			IPhysicsObject? blockingPhysObj = lastBlockingEnt?.VPhysicsGetObject();
			if (lastBlockingEnt != null &&
				 !lastBlockingEnt.IsPlayer() && !lastBlockingEnt.IsNPC() &&
				 lastBlockingEnt.GetMoveType() == Source.MoveType.VPhysics &&
				 blockingPhysObj != null &&
				 (!blockingPhysObj.IsMoveable() ||
				   blockingPhysObj.GetMass() > 200)) {
				CollisionProperty collisionProp = lastBlockingEnt.CollisionProp();
				bool foundLarge = false;
				bool foundSmall = false;
				Vector3 vecSize = collisionProp.OBBMaxs() - collisionProp.OBBMins();
				for (int i = 0; i < 3; i++) {
					if (vecSize[i] > 40)
						foundLarge = true;

					if (vecSize[i] < 30) {
						foundSmall = true;
						break;
					}
				}

				if (foundLarge && !foundSmall) {
					Vector3 startPos = GetNetwork()!.GetNode(endID)!.GetPosition(GetHullType());
					Vector3 endPos = startPos;
					endPos.Z += 0.01f;

					Util.TraceModel(startPos, endPos, GetOuter()!.GetHullMins(), GetOuter()!.GetHullMaxs(), lastBlockingEnt, CollisionGroup.None, out Trace tr);

					if (tr.StartSolid)
						blockAll = true;
				}
			}

			if (blockAll) {
				AI_Node destNode = GetNetwork()!.GetNode(endID)!;
				for (int i = 0; i < destNode.NumLinks(); i++) {
					ref AI_Link link = ref destNode.GetLinkByIndex(i);
					link.LinkInfo |= LinkInfo.StaleSuggested;
					link.TimeStaleExpires = gpGlobals.CurTime + 4.0;
					didMark = true;
				}
			}
			else if (startID != NO_NODE) {
				AI_Node node = GetNetwork()!.GetNode(startID)!;
				ref AI_Link link = ref node.GetLink(endID);
				if (!System.Runtime.CompilerServices.Unsafe.IsNullRef(ref link)) {
					link.LinkInfo |= LinkInfo.StaleSuggested;
					link.TimeStaleExpires = gpGlobals.CurTime + 4.0;
					didMark = true;
				}
			}
		}

		return didMark;
	}

	protected virtual bool DoFindPathToPos() {
		AI_Path path = GetPath();
		AI_Pathfinder pathfinder = GetPathfinder()!;
		Vector3 actualGoalPos = path.ActualGoalPosition();
		BaseEntity? target = path.GetTarget();
		float tolerance = path.GetGoalTolerance();
		Vector3 origin;

		if (gpGlobals.CurTime - TimeClipped > 0.11 || LastNavFailed)
			ClippedWaypoints.RemoveAll();

		if (ClippedWaypoints.IsEmpty())
			origin = GetLocalOrigin();
		else {
			AI_Waypoint lastClipped = ClippedWaypoints.GetLast()!;
			origin = lastClipped.GetPos();
		}

		LastNavFailed = false;

		path.ClearWaypoints();

		AI_Waypoint? firstWaypoint = pathfinder.BuildRoute(origin, actualGoalPos, target, tolerance, GetNavType(), LocalSucceedOnWithinTolerance);

		if (firstWaypoint == null)
			return false;

		path.SetWaypoints(firstWaypoint);

		if (!ClippedWaypoints.IsEmpty()) {
			AI_Waypoint firstClipped = ClippedWaypoints.GetFirst()!;
			ClippedWaypoints.Set(null);
			firstClipped.ModifyFlags(WaypointFlags.DontSimplify, true);
			path.PrependWaypoints(firstClipped);
			firstWaypoint = firstClipped;
		}

		if (firstWaypoint.IsReducible() && firstWaypoint.GetNext() != null && firstWaypoint.GetNext()!.NavType() == GetNavType() &&
			  ShouldOptimizeInitialPathSegment(firstWaypoint)) {
			const float EPS = 0.1f;
			MathLib.CalcClosestPointOnLineSegment(origin,
										   firstWaypoint.GetPos(), firstWaypoint.GetNext()!.GetPos(),
										   out Vector3 closest, out _);
			if ((firstWaypoint.GetPos() - closest).Length() > EPS &&
				 (origin - closest).Length() < GetHullWidth() * 0.5)
				path.Advance();
		}

		return true;
	}

	protected virtual bool ShouldOptimizeInitialPathSegment(AI_Waypoint waypoint) => true;

	public BaseEntity? GetNextPathcorner(BaseEntity? pathCorner) {
		BaseEntity? nextPathCorner = null;

		Assert(pathCorner != null);
		if (pathCorner != null) {
			nextPathCorner = pathCorner.GetNextTarget();

			if (nextPathCorner == null && pathCorner is AI_Hint hint) {
				int targetNode = hint.GetTargetNode();
				if (targetNode != NO_NODE) {
					AI_Node testNode = GetNetwork()!.GetNode(targetNode)!;
					nextPathCorner = testNode.GetHint();
				}
			}
		}

		return nextPathCorner;
	}

	bool DoFindPathToPathcorner(BaseEntity? pathCorner) {
		bool returnCode = false;
		Assert(GetPath().GoalType() == NavGoalType.PathCorner);

		if (pathCorner != null) {
			if ((GetGoalFlags() & AI_NavGoalFlags.NoPathcornerPathfinding) != 0 || NoPathcornerPathfinds) {
				if (pathCorner.Speed != 0)
					SetSpeed(pathCorner.Speed);

				GetPath().ClearWaypoints();

				AI_Waypoint route = new(pathCorner.GetLocalOrigin(), 0, GetNavType(), WaypointFlags.ToPathCorner, NO_NODE);
				route.PathCorner.Set(pathCorner);
				AI_Waypoint last = route;
				pathCorner = GetNextPathcorner(pathCorner);
				if (pathCorner != null) {
					last = new AI_Waypoint(pathCorner.GetLocalOrigin(), 0, GetNavType(), WaypointFlags.ToPathCorner, NO_NODE);
					last.PathCorner.Set(pathCorner);
					route.SetNext(last);
				}
				last.ModifyFlags(WaypointFlags.ToGoal, true);
				GetPath().SetWaypoints(route, true);
				returnCode = true;
			}
			else {
				Vector3 initPos = pathCorner.GetLocalOrigin();

				GetOuter()!.TranslateNavGoal(pathCorner, ref initPos);

				GetPath().ResetGoalPosition(initPos);

				float tolerance = GetPath().GetGoalTolerance();
				float outerTolerance = GetOuter()!.GetDefaultNavGoalTolerance();
				if (outerTolerance > tolerance) {
					GetPath().SetGoalTolerance(outerTolerance);
					tolerance = outerTolerance;
				}

				if (returnCode = DoFindPathToPos()) {
					if (pathCorner.Speed != 0)
						SetSpeed(pathCorner.Speed);

					AI_Waypoint lastWaypoint = GetPath().GetGoalWaypoint()!;
					Assert(lastWaypoint != null);

					lastWaypoint.ModifyFlags(WaypointFlags.ToPathCorner, true);
					lastWaypoint.PathCorner.Set(pathCorner);

					pathCorner = GetNextPathcorner(pathCorner);
					if (pathCorner != null) {
						lastWaypoint.ModifyFlags(WaypointFlags.ToGoal, false);
						AI_Waypoint curWaypoint = new(pathCorner.GetLocalOrigin(), 0, GetNavType(), WaypointFlags.ToPathCorner | WaypointFlags.ToGoal, NO_NODE);
						Vector3 waypointPos = curWaypoint.GetPos();
						GetOuter()!.TranslateNavGoal(pathCorner, ref waypointPos);
						curWaypoint.SetPos(waypointPos);
						GetPath().SetGoalTolerance(tolerance);
						curWaypoint.PathCorner.Set(pathCorner);
						lastWaypoint.SetNext(curWaypoint);
						GetPath().ResetGoalPosition(curWaypoint.GetPos());
					}
				}
			}
		}
		return returnCode;
	}

	bool DoFindPath() {
		GetPath().ClearWaypoints();

		bool returnCode;

		returnCode = false;

		switch (GetPath().GoalType()) {
			case NavGoalType.PathCorner:
				returnCode = DoFindPathToPathcorner(GetOuter()!.GetGoalEnt());
				break;

			case NavGoalType.Enemy: {
					BaseEntity? enemy = GetPath().GetTarget();
					if (enemy != null) {
						Assert(enemy == GetOuter()!.GetEnemy());

						Vector3 newPos = GetOuter()!.GetEnemyLKP();

						float tolerance = GetPath().GetGoalTolerance();
						float outerTolerance = GetOuter()!.GetDefaultNavGoalTolerance();
						if (outerTolerance > tolerance) {
							GetPath().SetGoalTolerance(outerTolerance);
							tolerance = outerTolerance;
						}

						GetOuter()!.TranslateNavGoal(enemy, ref newPos);

						GetPath().ResetGoalPosition(newPos);
						GetPath().SetGoalTolerance(tolerance);

						returnCode = DoFindPathToPos();
					}
				}
				break;

			case NavGoalType.Location:
			case NavGoalType.Flank:
			case NavGoalType.Cover:
				returnCode = DoFindPathToPos();
				break;

			case NavGoalType.LocationNearestNode: {
					int myNodeID;
					int destNodeID;

					returnCode = false;

					myNodeID = GetPathfinder()!.NearestNodeToNPC();
					if (myNodeID != NO_NODE) {
						destNodeID = GetPathfinder()!.NearestNodeToPoint(GetPath().ActualGoalPosition());
						if (destNodeID != NO_NODE) {
							AI_Waypoint? route = GetPathfinder()!.FindBestPath(myNodeID, destNodeID);

							if (route != null) {
								GetPath().SetWaypoints(route);
								GetPath().SetLastNodeAsGoal(true);
								returnCode = true;
							}
						}
					}
				}
				break;

			case NavGoalType.TargetEnt: {
					BaseEntity? target = GetPath().GetTarget();

					if (target != null) {
						Assert(target == GetOuter()!.GetTarget());

						Vector3 initPos = target.GetAbsOrigin();
						GetOuter()!.TranslateNavGoal(target, ref initPos);

						GetPath().ResetGoalPosition(initPos);
						returnCode = DoFindPathToPos();
					}
				}
				break;
		}

		return returnCode;
	}

	public void ClearPath() {
		OnClearPath();

		TimePathRebuildMax = 0;
		TimePathRebuildFail = 0;
		TimePathRebuildNext = 0;
		TimePathRebuildDelay = 0;

		Forget(AI_MemoryFlags.PathFailed);

		AI_Waypoint? waypoint = GetPath().GetCurWaypoint();

		if (waypoint != null) {
			SaveStoppingPath();
			PreviousMoveActivity = GetMovementActivity();
			PreviousArrivalActivity = GetArrivalActivity();

			if (ClippedWaypoints != null && ClippedWaypoints.GetFirst() != null)
				Assert(PreviousMoveActivity > Activity.ACT_RESET);

			while (waypoint != null) {
				if (waypoint.NodeID != NO_NODE) {
					AI_Node? node = GetNetwork()!.GetNode(waypoint.NodeID);

					if (node != null) {
						if (node.IsLocked())
							node.Unlock();
					}
				}
				waypoint = waypoint.GetNext();
			}
		}

		GetPath().Clear();
	}

	protected virtual bool GetStoppingPath(AI_WaypointList clippedWaypoints) {
		clippedWaypoints.RemoveAll();

		AI_Waypoint? curWaypoint = GetPath().GetCurWaypoint();
		if (curWaypoint != null) {
			bool mustCompleteCurrent = curWaypoint.NavType() == Navigation.Climb || curWaypoint.NavType() == Navigation.Jump;
			float distRemaining = GetMotor()!.MinStoppingDist(0);

			if (mustCompleteCurrent) {
				float distToCurrent = ComputePathDistance(curWaypoint.NavType(), GetLocalOrigin(), curWaypoint.GetPos());
				if (curWaypoint.NavType() == Navigation.Climb) {
					if (curWaypoint.GetNext() != null && curWaypoint.GetNext()!.NavType() == Navigation.Climb)
						distToCurrent += ComputePathDistance(Navigation.Climb, curWaypoint.GetPos(), curWaypoint.GetNext()!.GetPos());
					distToCurrent += GetHullWidth() * 2.0f;
				}

				if (distToCurrent > distRemaining)
					distRemaining = distToCurrent;
			}

			if (mustCompleteCurrent || distRemaining > 0.1) {
				Vector3 posPrev = GetLocalOrigin();
				AI_Waypoint? nextPoint = curWaypoint;
				AI_Waypoint? savedWaypoints = null;
				AI_Waypoint? lastSavedWaypoint = null;
				AI_Waypoint newWaypoint;

				while (distRemaining > 0.01 && nextPoint != null) {
					if ((nextPoint.NavType() == Navigation.Climb || nextPoint.NavType() == Navigation.Jump) &&
						 !mustCompleteCurrent)
						break;

					if (nextPoint.NavType() != Navigation.Climb || nextPoint.GetNext() == null || nextPoint.GetNext()!.NavType() != Navigation.Climb)
						mustCompleteCurrent = false;

					float distToNext = ComputePathDistance(nextPoint.NavType(), posPrev, nextPoint.GetPos());

					if (distToNext <= distRemaining + 0.01) {
						newWaypoint = new AI_Waypoint(nextPoint.GetPos(), nextPoint.Yaw, nextPoint.NavType(), nextPoint.Flags(), nextPoint.NodeID);
						newWaypoint.PathCorner = nextPoint.PathCorner;
						newWaypoint.Data = nextPoint.Data;

						if ((newWaypoint.Flags() & WaypointFlags.ToPathCorner) != 0) {
							newWaypoint.ModifyFlags(WaypointFlags.ToPathCorner, false);
							newWaypoint.PathCorner.Set(null);
						}

						newWaypoint.ModifyFlags(WaypointFlags.ToGoal | WaypointFlags.ToNode, false);
						newWaypoint.NodeID = NO_NODE;

						if (lastSavedWaypoint != null)
							lastSavedWaypoint.SetNext(newWaypoint);
						else
							savedWaypoints = newWaypoint;
						lastSavedWaypoint = newWaypoint;

						posPrev = nextPoint.GetPos();

						nextPoint = nextPoint.GetNext();
						distRemaining -= distToNext;
					}
					else {
						Assert(!(nextPoint.NavType() == Navigation.Climb || nextPoint.NavType() == Navigation.Jump));
						Vector3 remainder = nextPoint.GetPos() - posPrev;
						MathLib.VectorNormalize(ref remainder);
						float yaw = Util.VecToYaw(remainder);
						remainder *= distRemaining;
						remainder += posPrev;

						if (GetMoveProbe()!.MoveLimit(nextPoint.NavType(), posPrev, remainder, Mask.NPCSolid, null, 100, AI_MoveLimitFlags.Default | AI_MoveLimitFlags.TwoD, out AIMoveTrace trace)) {
							newWaypoint = new AI_Waypoint(trace.EndPosition, yaw, nextPoint.NavType(), WaypointFlags.ToGoal, 0);

							if (lastSavedWaypoint != null)
								lastSavedWaypoint.SetNext(newWaypoint);
							else
								savedWaypoints = newWaypoint;
						}

						distRemaining = 0;
					}
				}

				if (savedWaypoints != null) {
					clippedWaypoints.Set(savedWaypoints);
					return true;
				}
			}
		}
		return false;
	}

	public void IgnoreStoppingPath() {
		if (ClippedWaypoints != null && ClippedWaypoints.GetFirst() != null) {
			AI_Waypoint waypoint = ClippedWaypoints.GetFirst()!;

			if (waypoint.NavType() != Navigation.Jump && waypoint.NavType() != Navigation.Climb)
				ClippedWaypoints.RemoveAll();
		}
	}

	void SaveStoppingPath() {
		TimeClipped = -1;

		ClippedWaypoints.RemoveAll();
		AI_Waypoint? curWaypoint = GetPath().GetCurWaypoint();
		if (curWaypoint != null) {
			if ((curWaypoint.NavType() == Navigation.Climb || curWaypoint.NavType() == Navigation.Jump) || ai_use_clipped_paths.GetBool()) {
				if (GetStoppingPath(ClippedWaypoints))
					TimeClipped = gpGlobals.CurTime;
			}
		}
	}

	public bool SetGoalFromStoppingPath() {
		if (ClippedWaypoints != null && ClippedWaypoints.IsEmpty())
			SaveStoppingPath();
		if (ClippedWaypoints != null && !ClippedWaypoints.IsEmpty()) {
			if (PreviousMoveActivity <= Activity.ACT_RESET && GetMovementActivity() <= Activity.ACT_RESET) {
				ClippedWaypoints.RemoveAll();
				DevWarning(2, $"{GetOuter()!.GetDebugName()} has a stopping path and no valid. Movement activity: {GetMovementActivity()} (prev {PreviousMoveActivity})\n");
				return false;
			}

			if (ClippedWaypoints.GetFirst()!.NavType() == Navigation.Climb || ClippedWaypoints.GetFirst()!.NavType() == Navigation.Jump) {
				Task_t? curTask = GetOuter()!.GetTask();
				if (curTask != null && curTask.Value.Task == TASK_STOP_MOVING) {
					ClippedWaypoints.RemoveAll();
					return false;
				}
			}

			if (!GetPath().IsEmpty())
				GetPath().ClearWaypoints();
			GetPath().SetWaypoints(ClippedWaypoints.GetFirst(), true);
			ClippedWaypoints.Set(null);
			GetPath().SetGoalType(NavGoalType.None);
			GetPath().SetGoalType(NavGoalType.Location);
			GetPath().SetGoalTolerance(NAV_STOP_MOVING_TOLERANCE);
			Assert(GetPath().GetCurWaypoint() != null);

			Assert(PreviousMoveActivity != Activity.ACT_INVALID);

			if (PreviousMoveActivity != Activity.ACT_RESET)
				GetPath().SetMovementActivity(PreviousMoveActivity);
			if (PreviousArrivalActivity != Activity.ACT_RESET)
				GetPath().SetArrivalActivity(PreviousArrivalActivity);
			return true;
		}
		return false;
	}

	public BaseEntity? GetBlockingEntity() => LastBlockingEnt.Get();

	public AI_Path GetPath() => Path;

	public AI_Network? GetNetwork() => AINetwork;
	public void SetNetwork(AI_Network? network) => AINetwork = network;

	public AI_Motor? GetMotor() => Motor;
	public AI_MoveProbe? GetMoveProbe() => MoveProbe;
	public AI_LocalNavigator? GetLocalNavigator() => LocalNavigator;

	public Navigation GetNavType() => NavType;

	public AI_Motor? Motor;
	public AI_MoveProbe? MoveProbe;
	public AI_LocalNavigator? LocalNavigator;
	public AI_Network? AINetwork;
	public bool ValidateActivitySpeed;

	Navigation NavType;
	bool NavComplete;
	bool LastNavFailed;

	readonly AI_Path Path;

	readonly AI_WaypointList ClippedWaypoints;
	TimeUnit_t TimeClipped;
	Activity PreviousMoveActivity;
	Activity PreviousArrivalActivity;

	bool CalledStartMove;

	bool NotOnNetwork;
	TimeUnit_t NextSimplifyTime;
	bool ForcedSimplify;
	TimeUnit_t LastSuccessfulSimplifyTime;

	TimeUnit_t TimeLastAvoidanceTriangulate;

	float TimePathRebuildMax;
	float TimePathRebuildDelay;
	TimeUnit_t TimePathRebuildFail;
	TimeUnit_t TimePathRebuildNext;

	bool RememberStaleNodes;
	bool NoPathcornerPathfinds;
	bool LocalSucceedOnWithinTolerance;

	bool PeerMoveWait;
	EHANDLE PeerWaitingOn = new();
	readonly SimTimer PeerWaitMoveTimer = new();
	readonly SimTimer PeerWaitClearTimer = new();

	readonly SimTimer NextSidestepTimer = new();

	EHANDLE BigStepGroundEnt = new();
	EHANDLE LastBlockingEnt = new();

	Vector3 PosBeginFailedSteer;
	TimeUnit_t TimeBeginFailedSteer;

	int NavFailCounter;
	TimeUnit_t LastNavFailTime;
}
