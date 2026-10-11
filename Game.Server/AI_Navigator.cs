global using static Game.Server.AI_NavigatorGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Formats.BSP;

using System.Numerics;

namespace Game.Server;

public static class AI_NavigatorGlobals
{
	public const Activity AIN_DEF_ACTIVITY = Activity.ACT_INVALID;
}

public class AI_Navigator : AI_Component, IAI_MovementSink
{
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

	public void SetValidateActivitySpeed(bool validateActivitySpeed) => ValidateActivitySpeed = validateActivitySpeed;

	public virtual void Init(AI_Network? network) {
		Motor = GetOuter()!.GetMotor();
		MoveProbe = GetOuter()!.GetMoveProbe();
		LocalNavigator = GetOuter()!.GetLocalNavigator();
		AINetwork = network;
	}

	public bool ClearGoal() {
		ClearPath();
		OnNewGoal();
		return true;
	}

	public Activity GetMovementActivity() => GetPath().GetMovementActivity();

	public Activity GetArrivalActivity() => GetPath().GetArrivalActivity();

	public bool IsGoalSet() => GetPath().GoalType() != NavGoalType.None;

	public bool IsGoalActive() => GetPath() != null && !GetPath().IsEmpty();

	public BaseEntity? GetGoalTarget() => GetPath().GetTarget();

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

	public float GetIdealSpeed() => throw new NotImplementedException();

	public virtual float CalcYawSpeed() {
		float npcYaw = GetOuter()!.CalcYawSpeed();
		if (npcYaw >= 0.0f)
			return npcYaw;

		float maxYaw = GetOuter()!.MaxYawSpeed();

		if (IsGoalSet() && GetIdealSpeed() != 0.0) {
			if (GetPath().GetCurWaypoint() == null)
				return maxYaw;

			if (GetIdealSpeed() > 0) {
				float waypointDist = (GetPath().CurWaypointPos() - GetOuter()!.GetLocalOrigin()).Length();

				if (waypointDist < 100) {
					float scale = 1 + (0.01f * (100 - waypointDist));
					return maxYaw * scale;
				}
			}
		}
		return maxYaw;
	}

	public virtual void OnClearPath() { }

	public virtual void OnNewGoal() {
		ResetCalculations();
		NavComplete = true;
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

	public void ClearPath() {
		OnClearPath();

		TimePathRebuildMax = 0;
		TimePathRebuildFail = 0;
		TimePathRebuildNext = 0;
		TimePathRebuildDelay = 0;

		GetOuter()!.Forget(AI_MemoryFlags.PathFailed);

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

	public void SaveStoppingPath() => throw new NotImplementedException();

	public AI_Path GetPath() => Path;

	public AI_Network? GetNetwork() => AINetwork;

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
	float TimeClipped;
	Activity PreviousMoveActivity;
	Activity PreviousArrivalActivity;

	bool CalledStartMove;

	bool NotOnNetwork;
	float NextSimplifyTime;
	float LastSuccessfulSimplifyTime;

	float TimePathRebuildMax;
	float TimePathRebuildDelay;
	float TimePathRebuildFail;
	float TimePathRebuildNext;

	bool NoPathcornerPathfinds;
	bool LocalSucceedOnWithinTolerance;
	bool RememberStaleNodes;

	EHANDLE PeerWaitingOn = new();
	readonly SimTimer PeerWaitMoveTimer = new();
	readonly SimTimer PeerWaitClearTimer = new();

	readonly SimTimer NextSidestepTimer = new();

	EHANDLE BigStepGroundEnt = new();

	Vector3 PosBeginFailedSteer;
	float TimeBeginFailedSteer;

	float TimeLastAvoidanceTriangulate;

	int NavFailCounter;
	float LastNavFailTime;
}
