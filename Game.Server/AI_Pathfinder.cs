global using static Game.Server.AI_PathfinderGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

[Flags]
public enum RouteBuildFlags
{
	Ground = 0x00000001,
	Jump = 0x00000002,
	Fly = 0x00000004,
	Climb = 0x00000008,
	GiveWay = 0x00000010,
	Triang = 0x00000020,
	IgnoreNPCs = 0x00000040,
	CollideNPCs = 0x00000080,
	GetClose = 0x00000100,
}

public static class AI_PathfinderGlobals
{
	public static readonly float[] MAX_LOCAL_NAV_DIST_GROUND = [50 * 12, 25 * 12];
	public static readonly float[] MAX_LOCAL_NAV_DIST_FLY = [750 * 12, 750 * 12];

	public static readonly ConVar ai_no_local_paths = new("ai_no_local_paths", "0");

	public static Navigation MoveBitsToNavType(Capability bits) {
		switch (bits) {
			case Capability.MoveGround:
				return Navigation.Ground;
			case Capability.MoveFly:
				return Navigation.Fly;
			case Capability.MoveClimb:
				return Navigation.Climb;
			case Capability.MoveJump:
				return Navigation.Jump;
			default:
				Assert(false);
				return Navigation.None;
		}
	}
}

public class AI_Pathfinder : AI_Component
{
	const int MAX_NODE_TRIES = 4;
	const int MAX_TRIANGULATIONS = 2;
	const float MAX_TRIAGULATION_DIST = 32 * 12;

	public AI_Pathfinder(AI_BaseNPC? outer) : base(outer) {
		Network = null;
	}

	public void Init(AI_Network? network) {
		Assert(network != null);
		Network = network;
	}

	AI_HullType GetHullType() => GetOuter()!.GetHullType();
	Capability CapabilitiesGet() => GetOuter()!.CapabilitiesGet();

	bool UseStrongOptimizations() {
		if (!AIStrongOpt())
			return false;

		if (GetOuter()!.Classify() == Class_T.PlayerAllyVital)
			return false;

		return true;
	}

	Navigation ComputeWaypointType(AI_Node?[] nodes, int parentID, int destID) {
		Navigation navType = Navigation.None;

		AI_Node node = nodes[parentID]!;
		for (int link = 0; link < node.NumLinks(); link++) {
			ref AI_Link nodeLink = ref node.GetLinkByIndex(link);
			if (nodeLink.DestNodeID(parentID) == destID) {
				Capability linkMoveTypeBits = nodeLink.AcceptedMoveTypes[(int)GetHullType()];
				Capability moveTypeBits = linkMoveTypeBits & CapabilitiesGet();
				if (moveTypeBits == 0 && linkMoveTypeBits == Capability.MoveJump) {
					Assert(node.GetHint() != null && node.GetHint()!.HintType() == Hint.JumpOverride);
					nodes[destID]!.Lock(0.3);
					moveTypeBits = linkMoveTypeBits;
				}
				Navigation linkType = MoveBitsToNavType(moveTypeBits);

				Assert(navType == Navigation.None || navType == linkType);
				navType = linkType;
				break;
			}
		}

		if (navType == Navigation.None) {
			node = nodes[destID]!;
			for (int link = 0; link < node.NumLinks(); link++) {
				ref AI_Link nodeLink = ref node.GetLinkByIndex(link);
				if (nodeLink.DestNodeID(parentID) == destID) {
					Capability npcMoveBits = CapabilitiesGet();
					Capability nodeMoveBits = nodeLink.AcceptedMoveTypes[(int)GetHullType()];
					Capability moveTypeBits = npcMoveBits & nodeMoveBits;
					Navigation linkType = MoveBitsToNavType(moveTypeBits);

					Assert(navType == Navigation.None || navType == linkType);
					navType = linkType;

					DevMsg("Note: Strange link found between nodes in AI node graph\n");
					break;
				}
			}
		}

		AssertMsg(navType != Navigation.None, "Pathfinder appears to have output a path with consecutive nodes thate are not actually connected\n");

		return navType;
	}

	AI_Waypoint? MakeRouteFromParents(ReadOnlySpan<int> parentArray, int endID) {
		AI_Waypoint? oldWaypoint = null;
		int currentID = endID;

		AI_Node?[] aiNode = GetNetwork()!.AccessNodes()!;

		while (currentID != NO_NODE) {
			int prevID = parentArray[currentID];

			int destID;
			if (prevID != NO_NODE)
				destID = prevID;
			else {
				if (oldWaypoint == null)
					return null;
				destID = oldWaypoint.NodeID;
			}

			Navigation waypointType = ComputeWaypointType(aiNode, currentID, destID);

			Assert(waypointType != Navigation.None);

			AI_Waypoint newWaypoint = new(aiNode[currentID]!.GetPosition(GetHullType()),
				aiNode[currentID]!.GetYaw(), waypointType, WaypointFlags.ToNode, currentID);

			newWaypoint.SetNext(oldWaypoint);
			oldWaypoint = newWaypoint;

			currentID = prevID;
		}

		return oldWaypoint;
	}

	bool IsLinkStillStale(Capability moveType, ref AI_Link nodeLink) {
		if (IgnoreStaleLinks)
			return false;

		if ((nodeLink.LinkInfo & LinkInfo.StaleSuggested) == 0)
			return false;

		if (gpGlobals.CurTime < nodeLink.TimeStaleExpires)
			return true;

		if (gpGlobals.CurTime == LastStaleLinkCheckTime)
			return true;
		else
			LastStaleLinkCheckTime = gpGlobals.CurTime;

		if (CheckStaleRoute(GetNetwork()!.GetNode(nodeLink.SrcID)!.GetPosition(GetHullType()),
			GetNetwork()!.GetNode(nodeLink.DestID)!.GetPosition(GetHullType()), moveType)) {
			nodeLink.LinkInfo &= ~LinkInfo.StaleSuggested;
			return false;
		}

		nodeLink.TimeStaleExpires = gpGlobals.CurTime + 1.0;

		return true;
	}

	public int NearestNodeToNPC() => GetNetwork()!.NearestNodeToPoint(GetOuter(), GetOuter()!.GetAbsOrigin());

	public int NearestNodeToPoint(in Vector3 origin) => GetNetwork()!.NearestNodeToPoint(GetOuter(), origin);

	public AI_Waypoint? FindBestPath(int startID, int endID) {
		if (GetNetwork()!.NumNodes() == 0)
			return null;

		int nodes = GetNetwork()!.NumNodes();
		AI_Node?[] aiNode = GetNetwork()!.AccessNodes()!;
		AI_HullType hull = GetHullType();

		Span<bool> openBS = stackalloc bool[nodes];
		Span<bool> closeBS = stackalloc bool[nodes];

		Span<float> nodeG = stackalloc float[nodes];
		Span<float> nodeH = stackalloc float[nodes];
		Span<float> nodeF = stackalloc float[nodes];
		Span<int> nodeP = stackalloc int[nodes];

		for (int node = 0; node < nodes; node++) {
			nodeG[node] = float.MaxValue;
			nodeP[node] = -1;
		}

		nodeG[startID] = 0;

		nodeH[startID] = 0.1f * (aiNode[startID]!.GetPosition(hull) - aiNode[endID]!.GetPosition(hull)).Length();
		nodeF[startID] = nodeG[startID] + nodeH[startID];

		openBS[startID] = true;
		closeBS[startID] = true;

		while (openBS.Contains(true)) {
			int smallestID = AI_Network.FindBSSmallest(openBS, nodeF, nodes);

			openBS[smallestID] = false;

			AI_Node smallestNode = aiNode[smallestID]!;

			if (GetOuter()!.IsUnusableNode(smallestID, smallestNode.GetHint()))
				continue;

			if (smallestID == endID)
				return MakeRouteFromParents(nodeP, endID);

			for (int link = 0; link < smallestNode.NumLinks(); link++) {
				ref AI_Link nodeLink = ref smallestNode.GetLinkByIndex(link);

				if (!IsLinkUsable(ref nodeLink, smallestID))
					continue;

				Capability moveType = nodeLink.AcceptedMoveTypes[(int)hull] & CapabilitiesGet();
				int testID = nodeLink.DestNodeID(smallestID);

				Vector3 r1 = smallestNode.GetPosition(hull);
				Vector3 r2 = aiNode[testID]!.GetPosition(hull);
				float dist = GetOuter()!.GetNavigator()!.MovementCost(moveType, r1, r2);

				if (dist == float.MaxValue)
					continue;

				float newG = nodeG[smallestID] + dist;

				if (!closeBS[testID] || (newG < nodeG[testID])) {
					nodeP[testID] = smallestID;
					nodeG[testID] = newG;
					nodeH[testID] = (aiNode[testID]!.GetPosition(hull) - aiNode[endID]!.GetPosition(hull)).Length();
					nodeF[testID] = nodeG[testID] + nodeH[testID];

					closeBS[testID] = true;
					openBS[testID] = true;
				}
			}
		}

		return null;
	}

	public AI_Waypoint? FindShortRandomPath(int startID, float minPathLength, in Vector3 directionIn = default) {
		Span<int> neighbor = stackalloc int[AI_Network.AI_MAX_NODE_LINKS];
		Span<int> staleNeighbor = stackalloc int[AI_Network.AI_MAX_NODE_LINKS];
		int numNeighbors = 1;
		int numStaleNeighbors = 0;
		int neighborID = NO_NODE;

		int nodes = GetNetwork()!.NumNodes();
		AI_Node?[]? aiNode = GetNetwork()!.AccessNodes();

		if (nodes == 0)
			return null;

		GetOuter()?.Remember(AI_MemoryFlags.TaskExpensive);

		Span<int> nodeParent = stackalloc int[nodes];
		Span<bool> closeBS = stackalloc bool[nodes];
		Vector3 direction = directionIn;

		if (aiNode![startID]!.NumLinks() == 0)
			return null;

		nodeParent[startID] = NO_NODE;
		neighbor[0] = startID;

		float pathLength = 0;
		int searchCount = 0;
		while ((pathLength < minPathLength) ||
				(neighborID != NO_NODE && aiNode[neighborID]!.NodeType == NodeType.Climb)) {
			searchCount++;

			if (neighborID != NO_NODE &&
				numNeighbors == 0 &&
				numStaleNeighbors == 0) {
				if (aiNode[neighborID]!.NodeType == NodeType.Climb)
					return null;
				else
					return MakeRouteFromParents(nodeParent, neighborID);
			}

			int lastID = neighborID;

			if (direction != vec3_origin) {
				float bestDot = -1;
				Vector3 lastPos;

				if (lastID == NO_NODE)
					lastPos = GetOuter()!.GetLocalOrigin();
				else
					lastPos = aiNode[lastID]!.GetOrigin();

				if (numNeighbors == 0)
					neighborID = staleNeighbor[random.RandomInt(0, numStaleNeighbors - 1)];
				else {
					for (int i = 0; i < numNeighbors; i++) {
						Vector3 nodeDir = lastPos - aiNode[neighbor[i]]!.GetOrigin();
						MathLib.VectorNormalize(ref nodeDir);
						float dotPr = Vector3.Dot(direction, nodeDir);
						if (dotPr > bestDot) {
							bestDot = dotPr;
							neighborID = neighbor[i];
						}
					}
				}

				if (neighborID != NO_NODE) {
					direction = lastPos - aiNode[neighborID]!.GetOrigin();
					MathLib.VectorNormalize(ref direction);
				}
			}
			else if (numNeighbors != 0)
				neighborID = neighbor[random.RandomInt(0, numNeighbors - 1)];
			else
				neighborID = staleNeighbor[random.RandomInt(0, numStaleNeighbors - 1)];

			if (neighborID < 0)
				return null;

			nodeParent[neighborID] = lastID;
			closeBS[neighborID] = true;

			if (lastID != NO_NODE)
				pathLength += (aiNode[lastID]!.GetOrigin() - aiNode[neighborID]!.GetOrigin()).Length();

			if ((pathLength >= minPathLength || searchCount > 20) &&
				aiNode[neighborID]!.NodeType != NodeType.Climb)
				return MakeRouteFromParents(nodeParent, neighborID);

			numNeighbors = 0;
			numStaleNeighbors = 0;

			AI_Node neighborNode = aiNode[neighborID]!;
			neighborNode.ShuffleLinks();
			for (int link = 0; link < neighborNode.NumLinks(); link++) {
				if (numStaleNeighbors == staleNeighbor.Length) {
					AssertMsg(false, "Array overflow");
					return null;
				}
				if (numNeighbors == staleNeighbor.Length) {
					AssertMsg(false, "Array overflow");
					return null;
				}

				ref AI_Link nodeLink = ref neighborNode.GetShuffledLink(link);
				int testID = nodeLink.DestNodeID(neighborID);

				if (closeBS[testID])
					continue;

				if (testID == lastID)
					continue;

				if (!IsLinkUsable(ref nodeLink, neighborID))
					continue;

				if (aiNode[testID]!.IsLocked()) {
					staleNeighbor[numStaleNeighbors] = testID;
					numStaleNeighbors++;
				}
				else {
					neighbor[numNeighbors] = testID;
					numNeighbors++;
				}
			}
		}

		return MakeRouteFromParents(nodeParent, neighborID);
	}

	public bool IsLinkUsable(ref AI_Link link, int startID) {
		if ((link.LinkInfo & LinkInfo.Off) != 0) {
			AI_DynamicLink? dynamicLink = link.DynamicLink;

			if (dynamicLink == null || string.IsNullOrEmpty(dynamicLink.AllowUse))
				return false;

			string allowUse = dynamicLink.AllowUse;
			if (dynamicLink.InvertAllow) {
				if (GetOuter()!.NameMatches(allowUse) || GetOuter()!.ClassMatches(allowUse))
					return false;
			}
			else {
				if (!GetOuter()!.NameMatches(allowUse) && !GetOuter()!.ClassMatches(allowUse))
					return false;
			}
		}

		int endID = link.DestNodeID(startID);

		Capability linkMoveTypes = link.AcceptedMoveTypes[(int)GetHullType()];
		Capability moveType = linkMoveTypes & CapabilitiesGet();

		AI_Node startNode = GetNetwork()!.GetNode(startID)!;
		AI_Node endNode = GetNetwork()!.GetNode(endID)!;

		if ((linkMoveTypes & Capability.MoveJump) != 0 && moveType == 0) {
			AI_Hint? startHint = startNode.GetHint();
			AI_Hint? endHint = endNode.GetHint();
			if (startHint != null && endHint != null) {
				if (startHint.HintType() == Hint.JumpOverride &&
					endHint.HintType() == Hint.JumpOverride &&
					(((startHint.SpawnFlags | endHint.SpawnFlags) & AI_Hint.SF_ALLOW_JUMP_UP) != 0 || startHint.GetAbsOrigin().Z > endHint.GetAbsOrigin().Z)) {
					if (!startNode.IsLocked()) {
						if (startHint.GetTargetNode() == -1 || startHint.GetTargetNode() == endID)
							moveType = Capability.MoveJump;
					}
				}
			}
		}

		if (moveType == 0)
			return false;

		if (GetOuter()!.IsUnusableNode(endID, endNode.GetHint()))
			return false;

		if (moveType == Capability.MoveJump) {
			if (!GetOuter()!.IsJumpLegal(startNode.GetPosition(GetHullType()),
										endNode.GetPosition(GetHullType()),
										endNode.GetPosition(GetHullType())))
				return false;
		}

		if ((link.LinkInfo & LinkInfo.StaleSuggested) != 0) {
			if (IsLinkStillStale(moveType, ref link))
				return false;
		}
		return true;
	}

	static RouteBuildFlags NPCBuildFlags(AI_BaseNPC npc, in Vector3 origin) {
		if (npc.GetLocalOrigin() == origin && npc.GetNavType() == Navigation.Climb)
			return RouteBuildFlags.Climb;
		else if ((npc.CapabilitiesGet() & Capability.MoveFly) != 0)
			return RouteBuildFlags.Fly | RouteBuildFlags.GiveWay;
		else if ((npc.CapabilitiesGet() & Capability.MoveGround) != 0) {
			RouteBuildFlags buildFlags = RouteBuildFlags.Ground | RouteBuildFlags.GiveWay;
			if ((npc.CapabilitiesGet() & Capability.MoveJump) != 0)
				buildFlags |= RouteBuildFlags.Jump;

			return buildFlags;
		}
		return 0;
	}

	AI_Waypoint CreateNodeWaypoint(AI_HullType hullType, int nodeID, WaypointFlags nodeFlags = 0) {
		AI_Node node = GetNetwork()!.GetNode(nodeID)!;

		Navigation navType = node.NodeType switch {
			NodeType.Climb => Navigation.Climb,
			NodeType.Air => Navigation.Fly,
			_ => Navigation.Ground,
		};

		return new AI_Waypoint(node.GetPosition(hullType), node.GetYaw(), navType, WaypointFlags.ToNode | nodeFlags, nodeID);
	}

	AI_Waypoint? RouteToNode(in Vector3 origin, RouteBuildFlags buildFlags, int nodeID, float goalTolerance) {
		buildFlags |= NPCBuildFlags(GetOuter()!, origin);
		buildFlags &= ~RouteBuildFlags.GetClose;

		Vector3 nodePosition = GetNetwork()!.GetNode(nodeID)!.GetPosition(GetHullType());
		if (origin == nodePosition)
			return CreateNodeWaypoint(GetHullType(), nodeID, WaypointFlags.ToGoal);

		AI_Waypoint? result = BuildLocalRoute(origin, nodePosition, null, WaypointFlags.ToNode, nodeID, buildFlags, goalTolerance);
		if (result != null)
			result.NodeID = nodeID;
		return result;
	}

	AI_Waypoint? RouteFromNode(in Vector3 origin, RouteBuildFlags buildFlags, int nodeID, float goalTolerance) {
		buildFlags |= NPCBuildFlags(GetOuter()!, origin);
		buildFlags |= RouteBuildFlags.GetClose;

		AI_Node node = GetNetwork()!.GetNode(nodeID)!;
		Vector3 nodePosition = node.GetPosition(GetHullType());

		if (origin == nodePosition)
			return CreateNodeWaypoint(GetHullType(), nodeID, WaypointFlags.ToGoal);

		AI_Waypoint? result = BuildLocalRoute(nodePosition, origin, null, WaypointFlags.ToGoal, NO_NODE, buildFlags, goalTolerance);

		if (result == null &&
			node.NodeType == NodeType.Climb &&
			(origin - nodePosition).Length2DSqr() < 32.0f * 32.0f &&
			GetOuter()!.GetMoveProbe()!.CheckStandPosition(nodePosition, Mask.NPCSolidBrushOnly))
			result = new AI_Waypoint(origin, 0, Navigation.Ground, WaypointFlags.ToGoal, nodeID);

		return result;
	}

	AI_Waypoint? BuildSimpleRoute(Navigation navType, in Vector3 start, in Vector3 end, BaseEntity? target, WaypointFlags endFlags, int nodeID, NodeType nodeTargetType, float yaw) {
		Assert(navType == Navigation.Jump || navType == Navigation.Climb);
		if ((nodeID == NO_NODE) || (GetNetwork()!.GetNode(nodeID)!.NodeType == nodeTargetType)) {
			GetOuter()!.GetMoveProbe()!.MoveLimit(navType, start, end, Mask.NPCSolid, target, out AIMoveTrace moveTrace);

			if (!IsMoveBlocked(moveTrace))
				return new AI_Waypoint(end, yaw, navType, endFlags, nodeID);
		}
		return null;
	}

	AI_Waypoint? BuildComplexRoute(Navigation navType, in Vector3 start, in Vector3 end, BaseEntity? target, WaypointFlags endFlags, int nodeID,
		RouteBuildFlags buildFlags, float yaw, float goalTolerance, float maxLocalNavDistance) {
		float totalDist = ComputePathDistance(navType, start, end);
		if (totalDist < 0.0625f)
			return new AI_Waypoint(end, yaw, navType, endFlags, nodeID);

		Mask collideFlags = (buildFlags & RouteBuildFlags.IgnoreNPCs) != 0 ? Mask.NPCSolidBrushOnly : Mask.NPCSolid;

		bool checkGround = (GetOuter()!.CapabilitiesGet() & Capability.SkipNavGroundCheck) == 0;

		if (totalDist <= maxLocalNavDistance) {
			GetOuter()!.GetMoveProbe()!.MoveLimit(navType, start, end, collideFlags, target, checkGround ? 100 : 0, out AIMoveTrace moveTrace);

			if (!IsMoveBlocked(moveTrace))
				return new AI_Waypoint(end, yaw, navType, endFlags, nodeID);

			if ((buildFlags & RouteBuildFlags.GetClose) != 0 &&
				(endFlags & WaypointFlags.ToGoal) != 0 &&
				moveTrace.DistObstructed <= goalTolerance)
				return new AI_Waypoint(end, yaw, navType, endFlags, nodeID);

			if ((buildFlags & RouteBuildFlags.Triang) != 0) {
				if (!UseStrongOptimizations() || (GetOuter()!.GetState() == NPCState.Script || GetOuter()!.IsCurSchedule(SCHED_SCENE_GENERIC, false))) {
					AI_Waypoint? triangRoute = BuildTriangulationRoute(start, end, target,
						endFlags, nodeID, yaw, totalDist - moveTrace.DistObstructed, navType);

					if (triangRoute != null)
						return triangRoute;
				}
			}

			if (moveTrace.Status == AIMoveResult.BlockedNPC && (buildFlags & RouteBuildFlags.GiveWay) != 0) {
				GetOuter()!.GetMoveProbe()!.MoveLimit(navType, start, end, Mask.NPCSolidBrushOnly, target, checkGround ? 100 : 0, out AIMoveTrace moveTrace2);

				if (!IsMoveBlocked(moveTrace2)) {
					if (CanGiveWay(start, end, moveTrace.Obstruction!))
						return new AI_Waypoint(end, yaw, navType, endFlags, nodeID);
				}
			}
		}
		return null;
	}

	AI_Waypoint? BuildJumpRoute(in Vector3 start, in Vector3 end, BaseEntity? target, WaypointFlags endFlags, int nodeID, RouteBuildFlags buildFlags, float yaw)
		=> BuildSimpleRoute(Navigation.Jump, start, end, target, endFlags, nodeID, NodeType.Ground, yaw);

	AI_Waypoint? BuildClimbRoute(in Vector3 start, in Vector3 end, BaseEntity? target, WaypointFlags endFlags, int nodeID, RouteBuildFlags buildFlags, float yaw)
		=> BuildSimpleRoute(Navigation.Climb, start, end, target, endFlags, nodeID, NodeType.Climb, yaw);

	AI_Waypoint? BuildGroundRoute(in Vector3 start, in Vector3 end, BaseEntity? target, WaypointFlags endFlags, int nodeID, RouteBuildFlags buildFlags, float yaw, float goalTolerance)
		=> BuildComplexRoute(Navigation.Ground, start, end, target, endFlags, nodeID, buildFlags, yaw, goalTolerance, MAX_LOCAL_NAV_DIST_GROUND[UseStrongOptimizations() ? 1 : 0]);

	AI_Waypoint? BuildFlyRoute(in Vector3 start, in Vector3 end, BaseEntity? target, WaypointFlags endFlags, int nodeID, RouteBuildFlags buildFlags, float yaw, float goalTolerance)
		=> BuildComplexRoute(Navigation.Fly, start, end, target, endFlags, nodeID, buildFlags, yaw, goalTolerance, MAX_LOCAL_NAV_DIST_FLY[UseStrongOptimizations() ? 1 : 0]);

	bool CanGiveWay(in Vector3 start, in Vector3 end, BaseEntity blocker) {
		AI_BaseNPC? npcBlocker = blocker.MyNPCPointer();
		if (npcBlocker != null && npcBlocker.Edict() != null) {
			Disposition dispBlockerToMe = npcBlocker.IRelationType(GetOuter());
			if (dispBlockerToMe == Disposition.LI || dispBlockerToMe == Disposition.NU)
				return true;

			return false;
		}
		return false;
	}

	public virtual AI_Waypoint? BuildTriangulationRoute(in Vector3 start, in Vector3 end, BaseEntity? target, WaypointFlags endFlags, int nodeID,
		float yaw, float distToBlocker, Navigation navType) {
		if (!Triangulate(navType, start, end, distToBlocker, target, out Vector3 apex))
			return null;

		AI_Waypoint waypoint2 = new(end, yaw, navType, endFlags, nodeID);

		AI_Waypoint waypoint1 = new(apex, 0, navType, WaypointFlags.ToDetour, NO_NODE);
		waypoint1.SetNext(waypoint2);

		return waypoint1;
	}

	static int GetNextPoint(int lastNode, int direction, int numNodes) {
		int nextNode = lastNode + direction;
		if (nextNode > (numNodes - 1))
			nextNode = 0;
		else if (nextNode < 0)
			nextNode = numNodes - 1;

		return nextNode;
	}

	AI_Waypoint? BuildRouteThroughPoints(ReadOnlySpan<Vector3> points, int numPoints, int direction, int startIndex, int endIndex, Navigation navType, BaseEntity? target) {
		AI_MoveProbe moveProbe = GetOuter()!.GetMoveProbe()!;

		AI_Waypoint? firstRoute = null;
		AI_Waypoint? headRoute = null;

		int curIndex = startIndex;
		int nextIndex;

		moveProbe.MoveLimit(navType, GetOuter()!.GetAbsOrigin(), points[startIndex], Mask.NPCSolid, target, out AIMoveTrace endTrace);
		if (IsMoveBlocked(endTrace))
			return null;

		int runAwayCount = 0;
		while (runAwayCount++ < numPoints) {
			nextIndex = GetNextPoint(curIndex, direction, numPoints);

			moveProbe.MoveLimit(navType, points[curIndex], points[nextIndex], Mask.NPCSolid, target, out endTrace);
			if (IsMoveBlocked(endTrace)) {
				if (headRoute != null)
					DeleteAll(headRoute);

				return null;
			}

			if (headRoute == null)
				firstRoute = headRoute = new AI_Waypoint(points[curIndex], 0.0f, navType, WaypointFlags.ToDetour, NO_NODE);
			else {
				AI_Waypoint newNode = new(points[curIndex], 0.0f, navType, WaypointFlags.ToDetour | WaypointFlags.DontSimplify, NO_NODE);
				headRoute.SetNext(newNode);
				headRoute = newNode;
			}

			if (nextIndex == endIndex) {
				AI_Waypoint newNode = new(points[endIndex], 0.0f, navType, WaypointFlags.ToDetour, NO_NODE);
				headRoute.SetNext(newNode);
				headRoute = newNode;
				break;
			}

			curIndex = nextIndex;
		}

		return firstRoute;
	}

	static int ClosestPointToPosition(in Vector3 position, ReadOnlySpan<Vector3> points, int numPoints) {
		int bestNode = -1;
		float bestDistSqr = float.MaxValue;
		float distSqr;
		for (int i = 0; i < numPoints; i++) {
			distSqr = (points[i] - position).LengthSquared();
			if (distSqr < bestDistSqr) {
				bestDistSqr = distSqr;
				bestNode = i;
			}
		}

		return bestNode;
	}

	static int ShortestDirectionThroughPoints(in Vector3 start, int startPoint, int endPoint, ReadOnlySpan<Vector3> points, int numPoints) {
		const int clockwise = 1;
		const int counterClockwise = -1;

		int curPoint = startPoint;
		int nextPoint = GetNextPoint(startPoint, 1, numPoints);

		float startDistSqr = (start - points[startPoint]).LengthSquared();
		float distanceSqr = startDistSqr;

		for (int i = 0; i < numPoints; i++) {
			distanceSqr += (points[curPoint] - points[nextPoint]).LengthSquared();

			if (nextPoint == endPoint)
				break;

			nextPoint = GetNextPoint(nextPoint, 1, numPoints);
		}

		float bestDistanceSqr = distanceSqr;

		distanceSqr = startDistSqr;

		curPoint = startPoint;
		nextPoint = GetNextPoint(startPoint, -1, numPoints);

		for (int i = 0; i < numPoints; i++) {
			distanceSqr += (points[curPoint] - points[nextPoint]).LengthSquared();

			if (distanceSqr > bestDistanceSqr)
				break;

			if (nextPoint == endPoint)
				return counterClockwise;

			nextPoint = GetNextPoint(nextPoint, -1, numPoints);
		}

		return clockwise;
	}

	public virtual AI_Waypoint? BuildOBBAvoidanceRoute(in Vector3 start, in Vector3 end, BaseEntity obstruction, BaseEntity? target, Navigation navType) {
		if (obstruction.CollisionProp().IsPointInBounds(end))
			return null;

		Vector3 size = obstruction.CollisionProp().OBBSize();
		float width = GetOuter()!.GetHullWidth() * 0.5f;

		float widthPercX = width / size.X;
		float widthPercY = width / size.Y;

		Span<Vector3> points = [
			obstruction.CollisionProp().NormalizedToWorldSpace(new(-widthPercX, 1 + widthPercY, 0.25f)),
			obstruction.CollisionProp().NormalizedToWorldSpace(new(1 + widthPercX, 1 + widthPercY, 0.25f)),
			obstruction.CollisionProp().NormalizedToWorldSpace(new(1 + widthPercX, -widthPercY, 0.25f)),
			obstruction.CollisionProp().NormalizedToWorldSpace(new(-widthPercX, -widthPercY, 0.25f)),
		];

		int startPoint = ClosestPointToPosition(start, points, points.Length);
		int endPoint = ClosestPointToPosition(end, points, points.Length);

		if (startPoint == endPoint)
			return null;

		int direction = ShortestDirectionThroughPoints(start, startPoint, endPoint, points, points.Length);

		AI_Waypoint? route = BuildRouteThroughPoints(points, points.Length, direction, startPoint, endPoint, navType, target);
		if (route == null) {
			route = BuildRouteThroughPoints(points, points.Length, -direction, startPoint, endPoint, navType, target);
			if (route == null)
				return null;
		}

		return route;
	}

	public virtual AI_Waypoint? BuildLocalRoute(in Vector3 start, in Vector3 end, BaseEntity? target, WaypointFlags endFlags, int nodeID, RouteBuildFlags buildFlags, float goalTolerance) {
		float yaw;
		if (nodeID != NO_NODE)
			yaw = GetNetwork()!.GetNode(nodeID)!.GetYaw();
		else
			yaw = 0;

		if ((buildFlags & RouteBuildFlags.Ground) != 0) {
			AI_Waypoint? groundRoute = BuildGroundRoute(start, end, target, endFlags, nodeID, buildFlags, yaw, goalTolerance);

			if (groundRoute != null)
				return groundRoute;
		}

		if ((buildFlags & RouteBuildFlags.Fly) != 0) {
			AI_Waypoint? flyRoute = BuildFlyRoute(start, end, target, endFlags, nodeID, buildFlags, yaw, goalTolerance);

			if (flyRoute != null)
				return flyRoute;
		}

		if ((buildFlags & RouteBuildFlags.Jump) != 0 && (CapabilitiesGet() & Capability.MoveJump) != 0) {
			AI_Waypoint? jumpRoute = BuildJumpRoute(start, end, target, endFlags, nodeID, buildFlags, yaw);

			if (jumpRoute != null)
				return jumpRoute;
		}

		if ((buildFlags & RouteBuildFlags.Climb) != 0 && (CapabilitiesGet() & Capability.MoveClimb) != 0) {
			AI_Waypoint? climbRoute = BuildClimbRoute(start, end, target, endFlags, nodeID, buildFlags, yaw);

			if (climbRoute != null)
				return climbRoute;
		}

		return null;
	}

	public AI_Waypoint? BuildRoute(in Vector3 start, in Vector3 end, BaseEntity? target, float goalTolerance, Navigation curNavType = Navigation.None, bool localSucceedOnWithinTolerance = false) {
		RouteBuildFlags buildFlags = 0;
		bool tryLocal = !ai_no_local_paths.GetBool();

		if (curNavType == Navigation.Climb) {
			buildFlags = RouteBuildFlags.Climb;
			tryLocal = false;
		}
		else if ((CapabilitiesGet() & Capability.MoveFly) != 0 || (CapabilitiesGet() & Capability.MoveSwim) != 0)
			buildFlags = RouteBuildFlags.Fly | RouteBuildFlags.GiveWay | RouteBuildFlags.Triang;
		else if ((CapabilitiesGet() & Capability.MoveGround) != 0) {
			buildFlags = RouteBuildFlags.Ground | RouteBuildFlags.GiveWay | RouteBuildFlags.Triang;
			if ((CapabilitiesGet() & Capability.MoveJump) != 0)
				buildFlags |= RouteBuildFlags.Jump;
		}

		if (localSucceedOnWithinTolerance)
			buildFlags |= RouteBuildFlags.GetClose;

		AI_Waypoint? result = null;

		if (tryLocal && CanUseLocalNavigation())
			result = BuildLocalRoute(start, end, target, WaypointFlags.ToGoal, NO_NODE, buildFlags, goalTolerance);

		if (result == null)
			result = BuildNodeRoute(start, end, buildFlags, goalTolerance);

		IgnoreStaleLinks = false;

		return result;
	}

	public void UnlockRouteNodes(AI_Waypoint? path) {
		AI_Node? node;
		while (path != null) {
			if (path.NodeID != NO_NODE && (node = GetNetwork()!.GetNode(path.NodeID)) != null && node.IsLocked())
				node.Unlock();
			path = path.GetNext();
		}
	}

	public void SetIgnoreBadLinks() => IgnoreStaleLinks = true;

	public virtual AI_Waypoint? BuildRadialRoute(in Vector3 startPos, in Vector3 centerPos, in Vector3 goalPos, float radius, float arc, float stepDist, bool clockwise, float goalTolerance, bool airRoute) {
		GetOuter()?.Remember(AI_MemoryFlags.TaskExpensive);

		float angleStep = 2.0f * MathF.Atan((0.5f * stepDist) / radius);

		if (clockwise) {
			arc *= -1;
			angleStep *= -1;
		}

		Vector3 startDir = startPos - centerPos;
		MathLib.VectorNormalize(ref startDir);

		float startAngle = MathLib.DEG2RAD(Util.VecToYaw(startDir));
		float endAngle = startAngle + MathLib.DEG2RAD(arc);

		startAngle += angleStep;

		AI_Waypoint? headRoute = null;
		AI_Waypoint? nextRoute;
		Vector3 lastPos = startPos;
		RouteBuildFlags routeBits = airRoute ? RouteBuildFlags.Fly : RouteBuildFlags.Ground;
		float curAngle = startAngle;
		Vector3 nextPos;

		Assert((clockwise && curAngle > endAngle) || (!clockwise && curAngle < endAngle));

		while (true) {
			if ((clockwise && curAngle <= endAngle) || (!clockwise && curAngle >= endAngle))
				break;

			nextPos = centerPos;
			nextPos.X += radius * MathF.Cos(curAngle);
			nextPos.Y += radius * MathF.Sin(curAngle);

			nextRoute = BuildLocalRoute(lastPos, nextPos, null, 0, NO_NODE, routeBits, goalTolerance);

			if (nextRoute == null)
				return null;

			nextRoute.ModifyFlags(WaypointFlags.DontSimplify, true);

			if (headRoute != null)
				AddWaypointLists(headRoute, nextRoute);
			else
				headRoute = nextRoute;

			lastPos = nextPos;

			curAngle += angleStep;
		}

		if (headRoute == null)
			return null;

		AI_Waypoint? lastRoute = BuildLocalRoute(lastPos, goalPos, null, 0, NO_NODE, airRoute ? RouteBuildFlags.Fly : RouteBuildFlags.Ground, goalTolerance);
		if (lastRoute == null)
			return null;

		lastRoute.ModifyFlags(WaypointFlags.DontSimplify, false);
		lastRoute.ModifyFlags(WaypointFlags.ToGoal, true);

		AddWaypointLists(headRoute, lastRoute);

		return headRoute;
	}

	bool CheckStaleNavTypeRoute(Navigation navType, in Vector3 start, in Vector3 end) {
		GetOuter()!.GetMoveProbe()!.MoveLimit(navType, start, end, Mask.NPCSolid, null, 100, AI_MoveLimitFlags.IgnoreTransients, out AIMoveTrace moveTrace);

		if (!IsMoveBlocked(moveTrace))
			return true;

		Vector3 delta = end - start;
		float totalDist = delta.Length();

		if (Triangulate(navType, start, end, totalDist - moveTrace.DistObstructed, null, out _))
			return true;

		if (moveTrace.Obstruction != null && moveTrace.Obstruction.MyNPCPointer() != null) {
			GetOuter()!.GetMoveProbe()!.MoveLimit(navType, start, end, Mask.NPCSolidBrushOnly, null, out moveTrace);

			if (!IsMoveBlocked(moveTrace))
				return true;
		}

		return false;
	}

	bool CheckStaleRoute(in Vector3 start, in Vector3 end, Capability moveTypes) {
		if ((moveTypes & Capability.MoveGround) != 0) {
			if (CheckStaleNavTypeRoute(Navigation.Ground, start, end))
				return true;
		}

		if ((moveTypes & Capability.MoveFly) != 0) {
			if (CheckStaleNavTypeRoute(Navigation.Fly, start, end))
				return true;
		}

		if ((moveTypes & Capability.MoveJump) != 0) {
			GetOuter()!.GetMoveProbe()!.MoveLimit(Navigation.Jump, start, end, Mask.NPCSolid, null, out AIMoveTrace moveTrace);
			if (!IsMoveBlocked(moveTrace))
				return true;
			else {
				GetOuter()!.GetMoveProbe()!.MoveLimit(Navigation.Jump, end, start, Mask.NPCSolid, null, out moveTrace);
				if (!IsMoveBlocked(moveTrace))
					return true;
			}
		}

		if ((moveTypes & Capability.MoveClimb) != 0) {
			GetOuter()!.GetMoveProbe()!.MoveLimit(Navigation.Climb, start, end, Mask.NPCSolid, null, out AIMoveTrace moveTrace);
			if (!IsMoveBlocked(moveTrace))
				return true;
		}

		return false;
	}

	sealed class PathfindNearestNodeFilter(AI_Pathfinder pathfinder, Vector3 goal, bool toNode, RouteBuildFlags buildFlags, float goalTolerance) : INearestNodeFilter
	{
		int Tries;
		readonly Capability MoveTypes = (Capability)(buildFlags & (RouteBuildFlags.Ground | RouteBuildFlags.Fly | RouteBuildFlags.Jump | RouteBuildFlags.Climb));

		public AI_Waypoint? Route;

		public bool IsValid(AI_Node node) {
			int staleLinks = 0;
			if (!pathfinder.IgnoreStaleLinks) {
				int hull = (int)pathfinder.GetOuter()!.GetHullType();
				for (int i = 0; i < node.NumLinks(); i++) {
					ref AI_Link link = ref node.GetLinkByIndex(i);
					if ((link.LinkInfo & (LinkInfo.StaleSuggested | LinkInfo.Off)) != 0)
						staleLinks++;
					else if ((link.AcceptedMoveTypes[hull] & MoveTypes) == 0)
						staleLinks++;
				}
			}

			if (staleLinks != 0 && staleLinks == node.NumLinks())
				return false;

			RouteBuildFlags routeFlags = (Tries < MAX_TRIANGULATIONS) ? (RouteBuildFlags.IgnoreNPCs | RouteBuildFlags.Triang) : RouteBuildFlags.IgnoreNPCs;

			if (toNode)
				Route = pathfinder.RouteToNode(goal, routeFlags, node.GetId(), goalTolerance);
			else
				Route = pathfinder.RouteFromNode(goal, routeFlags, node.GetId(), goalTolerance);

			Tries++;

			return Route != null;
		}

		public bool ShouldContinue() => Route == null && Tries < MAX_NODE_TRIES;
	}

	AI_Waypoint? BuildNearestNodeRoute(in Vector3 goal, bool toNode, RouteBuildFlags buildFlags, float goalTolerance, out int nearestNode) {
		PathfindNearestNodeFilter filter = new(this, goal, toNode, buildFlags, goalTolerance);
		nearestNode = GetNetwork()!.NearestNodeToPoint(GetOuter(), goal, true, filter);

		return filter.Route;
	}

	public virtual AI_Waypoint? BuildNodeRoute(in Vector3 start, in Vector3 end, RouteBuildFlags buildFlags, float goalTolerance) {
		if (GetNetwork()!.NumNodes() == 0)
			return null;

		AI_Waypoint? srcRoute = BuildNearestNodeRoute(start, true, buildFlags, goalTolerance, out int srcID);
		if (srcRoute == null)
			return null;

		AI_Waypoint? destRoute = BuildNearestNodeRoute(end, false, buildFlags, goalTolerance, out int destID);
		if (destRoute == null) {
			DeleteAll(srcRoute);
			return null;
		}

		if (destID == srcID) {
			AddWaypointLists(srcRoute, destRoute);
			return srcRoute;
		}

		if (!GetNetwork()!.IsConnected(srcID, destID))
			return null;

		AI_Waypoint? path = FindBestPath(srcID, destID);

		if (path == null) {
			DeleteAll(srcRoute);
			DeleteAll(destRoute);
			return null;
		}

		AddWaypointLists(srcRoute, path);
		AddWaypointLists(srcRoute, destRoute);

		return srcRoute;
	}

	bool TestTriangulationRoute(Navigation navType, in Vector3 start, in Vector3 apex, in Vector3 end, BaseEntity? targetEnt, out AIMoveTrace startTrace) {
		AI_MoveProbe moveProbe = GetOuter()!.GetMoveProbe()!;

		bool pathClear = false;

		if (moveProbe.MoveLimit(navType, start, apex, Mask.NPCSolid, targetEnt, out startTrace)) {
			if (moveProbe.MoveLimit(navType, apex, end, Mask.NPCSolid, targetEnt, out _))
				pathClear = true;
		}

		return pathClear;
	}

	public bool Triangulate(Navigation navType, in Vector3 start, in Vector3 endIn, float distToBlocker, BaseEntity? targetEnt, out Vector3 apex) {
		apex = default;

		if (GetOuter()!.IsFlaggedEfficient())
			return false;

		Vector3 forward = endIn - start;
		float totalDist = MathLib.VectorNormalize(ref forward);

		Vector3 end;

		if (navType != Navigation.Fly && totalDist > MAX_TRIAGULATION_DIST) {
			end = forward * MAX_TRIAGULATION_DIST;
			totalDist = MAX_TRIAGULATION_DIST;
			if (!GetOuter()!.GetMoveProbe()!.MoveLimit(navType, end, endIn, Mask.NPCSolid, targetEnt, out _))
				return false;
		}
		else
			end = endIn;

		Vector3 up, perpendicular;
		if (1.0f - MathF.Abs(forward.Z) > 1e-3f) {
			up = new(0, 0, 1);
			perpendicular = Vector3.Cross(forward, up);
		}
		else {
			up = new(0, 1, 0);
			perpendicular = new(1, 0, 0);
		}

		float sizeX = 0.5f * NAI_Hull.Length(GetHullType());
		float sizeZ = 0.5f * NAI_Hull.Height(GetHullType());

		float apexDist = distToBlocker + sizeX;
		if (apexDist > totalDist)
			apexDist = totalDist;

		Span<Vector3> delta = stackalloc Vector3[2];
		Span<Vector3> apexes = stackalloc Vector3[4];
		Span<float> apexDists = stackalloc float[4];

		int numToTest = 2;
		delta[0] = perpendicular * sizeX;

		Vector3 center = start + apexDist * forward;
		apexes[0] = center - delta[0];
		apexes[1] = center + delta[0];
		delta[0] *= 2.0f;
		apexDists[0] = apexDists[1] = apexDist;

		if (navType == Navigation.Fly) {
			delta[1] = up * (3.0f * sizeZ);
			apexes[2] = center - delta[1];
			apexes[3] = center + delta[1];
			apexDists[2] = apexDists[3] = apexDist;
			numToTest = 4;
		}

		for (int i = 0; i < 2; ++i) {
			for (int j = numToTest; --j >= 0;) {
				if (TestTriangulationRoute(navType, start, apexes[j], end, targetEnt, out AIMoveTrace moveTrace)) {
					apex = apexes[j];
					return true;
				}

				if (IsMoveBlocked(moveTrace)) {
					Vector3 startToObstruction = moveTrace.EndPosition - start;
					float distToObstruction = Vector3.Dot(startToObstruction, forward);

					float newApexDist = apexDists[j];
					if (apexDists[j] > distToObstruction)
						newApexDist = distToObstruction;

					apexes[j] += (newApexDist - apexDists[j]) * forward;
					apexDists[j] = newApexDist;
				}

				if ((j & 0x1) != 0)
					apexes[j] += delta[j >> 1];
				else
					apexes[j] -= delta[j >> 1];
			}
		}

		return false;
	}

	protected virtual bool CanUseLocalNavigation() => true;

	AI_Network? GetNetwork() => Network;

	TimeUnit_t LastStaleLinkCheckTime;
	bool IgnoreStaleLinks;

	public AI_Network? Network;
}
