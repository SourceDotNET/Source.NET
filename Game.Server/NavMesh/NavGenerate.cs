using static Game.Server.NavMesh.NavGenerate;
using static Game.Server.NavMesh.Nav;

using Source.Common.Commands;

using System.Numerics;
using Source.Common;
using Source;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;
using Source.Common.Formats.BSP;
using System.Runtime.CompilerServices;

namespace Game.Server.NavMesh;

static class NavGenerate
{
	public const int MAX_BLOCKED_AREAS = 256;

	public const float MaxObstacleAreaWidth = StepHeight;
	public const float MinObstacleAreaWidth = 10.0f;
	public const float MaxTraversableHeight = StepHeight;

	public static uint[] BlockedID = new uint[MAX_BLOCKED_AREAS];
	public static int BlockedIDCount = 0;

	public static double LastMsgTime = 0.0f;

	public static Vector3 NavTraceMins = new(-0.45f, -0.45f, 0);
	public static Vector3 NavTraceMaxs = new(0.45f, 0.45f, HumanCrouchHeight);
}

class ApproachAreaCost
{
	public float Invoke(NavArea area, NavArea? fromArea, NavLadder? ladder, FuncElevator? elevator) {
		for (int i = 0; i < BlockedIDCount; i++) {
			if (area.GetID() == BlockedID[i])
				return -1.0f;
		}

		if (fromArea == null)
			return 0.0f;

		float dist;

		if (ladder != null)
			dist = ladder.Length;
		else
			dist = (area.GetCenter() - fromArea.GetCenter()).Length();

		return dist + fromArea.GetCostSoFar();
	}
}

class JumpConnector
{
	struct Connection
	{
		public NavArea Source;
		public NavArea Dest;
		public NavDirType Dir;
	}

	public bool Invoke(NavArea jumpArea) {
		if (!nav_generate_jump_connections.GetBool())
			return true;

		if ((jumpArea.GetAttributes() & NavAttributeType.Jump) == 0)
			return true;

		for (int i = 0; i < (int)NavDirType.NumDirections; i++) {
			NavDirType incomingDir = (NavDirType)i;
			NavDirType outgoingDir = OppositeDirection(incomingDir);

			List<NavConnect> incoming = jumpArea.GetIncomingConnections(incomingDir);
			List<NavConnect> from = jumpArea.GetAdjacentAreas(incomingDir);
			List<NavConnect> dest = jumpArea.GetAdjacentAreas(outgoingDir);

			TryToConnect(jumpArea, incoming, dest, outgoingDir);
			TryToConnect(jumpArea, from, dest, outgoingDir);
		}

		return true;
	}

	void TryToConnect(NavArea jumpArea, List<NavConnect> source, List<NavConnect> dest, NavDirType outgoingDir) {
		foreach (NavConnect sourceConnect in source) {
			NavArea sourceArea = sourceConnect.Area!;
			if (!sourceArea.IsConnected(jumpArea, outgoingDir))
				continue;

			if ((sourceArea.GetAttributes() & NavAttributeType.Jump) != 0) {
				NavDirType incomingDir = OppositeDirection(outgoingDir);
				List<NavConnect> in1 = sourceArea.GetIncomingConnections(incomingDir);
				List<NavConnect> in2 = sourceArea.GetAdjacentAreas(incomingDir);

				TryToConnect(jumpArea, in1, dest, outgoingDir);
				TryToConnect(jumpArea, in2, dest, outgoingDir);

				continue;
			}

			TryToConnect(jumpArea, sourceArea, dest, outgoingDir);
		}
	}

	void TryToConnect(NavArea jumpArea, NavArea sourceArea, List<NavConnect> dest, NavDirType outgoingDir) {
		foreach (NavConnect destConnect in dest) {
			NavArea destArea = destConnect.Area!;

			if ((destArea.GetAttributes() & NavAttributeType.Jump) != 0)
				continue;

			Vector3 center = Vector3.Zero;
			sourceArea.ComputePortal(destArea, outgoingDir, ref center, out float halfWidth);

			if (halfWidth <= 0.0f)
				continue;

			Vector3 dir = Vector3.Zero;
			AddDirectionVector(ref dir, outgoingDir, 5.0f);

			sourceArea.GetClosestPointOnArea(ref center, out Vector3 sourcePos);
			destArea.GetClosestPointOnArea(ref center, out Vector3 destPos);

			if (sourceArea.HasAttributes(NavAttributeType.Stairs) && sourcePos.Z + StepHeight < destPos.Z)
				continue;

			if ((sourcePos - destPos).Length() < GenerationStepSize * 3)
				sourceArea.ConnectTo(destArea, outgoingDir);
		}
	}
}

class IncrementallyGeneratedAreas
{
	public bool Invoke(NavArea area) => area.HasNodes();
}

class AreaSet(List<NavArea> areas)
{
	readonly List<NavArea> Areas = areas;
	public bool Invoke(NavArea area) => Areas.Contains(area);
}

class TestOverlapping(Vector3 nw, Vector3 ne, Vector3 sw, Vector3 se)
{
	Vector3 NW = nw;
	Vector3 NE = ne;
	Vector3 SW = sw;
	Vector3 SE = se;

	private float GetZ(Vector3 pos) {
		float dx = SE.X - NW.X;
		float dy = SE.Y - NW.Y;

		if (dx == 0.0f || dy == 0.0f)
			return NE.Z;

		float u = (pos.X - NW.X) / dx;
		float v = (pos.Y - NW.Y) / dy;

		if (u < 0.0f)
			u = 0.0f;
		else if (u > 1.0f)
			u = 1.0f;

		if (v < 0.0f)
			v = 0.0f;
		else if (v > 1.0f)
			v = 1.0f;

		float northZ = NW.Z + u * (NE.Z - NW.Z);
		float southZ = SW.Z + u * (SE.Z - SW.Z);

		return northZ + v * (southZ - northZ);
	}

	public bool OverlapsExistingArea() {
		Vector3 nw = NW;
		Vector3 se = SE;
		Vector3 start = nw;

		start.X += GenerationStepSize / 2.0f;
		start.Y += GenerationStepSize / 2.0f;

		while (start.X < se.X) {
			start.Y = nw.Y + GenerationStepSize / 2.0f;
			while (start.Y < se.Y) {
				start.Z = GetZ(start);
				Vector3 end = start;
				start.Z -= StepHeight;
				end.Z += HalfHumanHeight;

				if (NavMesh.Instance!.FindNavAreaOrLadderAlongRay(start, end, out NavArea? overlappingArea, out _, null) && overlappingArea != null)
					return true;

				start.Y += GenerationStepSize;
			}

			start.X += GenerationStepSize;
		}

		return false;
	}
}

class Subdivider(int depth)
{
	int Depth = depth;

	public bool Invoke(NavArea area) {
		SubdivideX(area, true, true, Depth);
		return true;
	}

	public void SubdivideX(NavArea area, bool canDivideX, bool canDivideY, int depth) {
		if (!canDivideX || depth <= 0)
			return;

		float split = area.GetSizeX() / 2.0f;

		if (split < GenerationStepSize) {
			if (canDivideY)
				SubdivideY(area, false, canDivideY, depth);
			return;
		}

		split += area.GetCorner(NavCornerType.NorthWest).X;
		split = NavMesh.Instance!.SnapToGrid(split);

		if (area.SplitEdit(false, split, out NavArea? alpha, out NavArea? beta)) {
			SubdivideX(alpha, canDivideX, canDivideY, depth);
			SubdivideX(beta, canDivideX, canDivideY, depth);
		}
	}

	public void SubdivideY(NavArea area, bool canDivideX, bool canDivideY, int depth) {
		if (!canDivideY || depth <= 0)
			return;

		float split = area.GetSizeY() / 2.0f;

		if (split < GenerationStepSize) {
			if (canDivideX)
				SubdivideX(area, canDivideX, false, depth);
			return;
		}

		split += area.GetCorner(NavCornerType.NorthWest).Y;
		split = NavMesh.Instance!.SnapToGrid(split);

		if (area.SplitEdit(true, split, out NavArea? alpha, out NavArea? beta)) {
			SubdivideY(alpha, canDivideX, canDivideY, depth - 1);
			SubdivideY(beta, canDivideX, canDivideY, depth - 1);
		}
	}
}

public partial class NavMesh
{
	void BuildLadders() {
		DestroyLadders();
	}

	void CreateLadder(Vector3 absMin, Vector3 absMax, float maxHeightAboveTopArea) { }

	void CreateLadder(Vector3 top, Vector3 bottom, float width, Vector2 ladderDir, float maxHeightAboveTopArea) { }

	void MarkPlayerClipAreas() { }

	void MarkJumpAreas() {
		for (int it = 0; it < NavArea.TheNavAreas.Count; it++) {
			NavArea area = NavArea.TheNavAreas[it];
			if (!area.HasNodes())
				continue;

			Vector3 normal = default, otherNormal = default;
			area.ComputeNormal(ref normal);
			area.ComputeNormal(ref otherNormal, true);

			float lowestNormalZ = Math.Min(normal.Z, otherNormal.Z);
			if (lowestNormalZ < nav_slope_limit.GetFloat())
				area.SetAttributes(area.GetAttributes() | NavAttributeType.Jump | NavAttributeType.NoMerge);
			else if (lowestNormalZ < nav_slope_limit.GetFloat() + nav_slope_tolerance.GetFloat()) {
				Vector3 testPos = area.GetCenter();
				testPos.Z += HalfHumanHeight;

				if (GetSimpleGroundHeight(testPos, out float _, out Vector3 groundNormal)) {
					float deltaNormalZ = Math.Abs(groundNormal.Z - lowestNormalZ);
					if (deltaNormalZ > nav_slope_tolerance.GetFloat()) {
						area.SetAttributes(area.GetAttributes() | NavAttributeType.Jump | NavAttributeType.NoMerge);
					}
				}
			}
		}
	}

	void StichAndRemoveJumpAreas() {
		JumpConnector jumpConnector = new();
		ForAllAreas(jumpConnector.Invoke);
		RemoveJumpAreas();
	}

	void HandleObstacleTopAreas() {
		if (!nav_generate_fencetops.GetBool())
			return;

		RaiseAreasWithInternalObstacles();
		CreateObstacleTopAreas();
		RemoveOverlappingObstacleTopAreas();
	}

	static void AdjustObstacleDistances(ref float obstacleStartDist, ref float obstacleEndDist, float maxAllowedDist) {
		float obstacleWidth = obstacleEndDist - obstacleStartDist;
		if (obstacleWidth < MinObstacleAreaWidth) {
			float halfDelta = (MinObstacleAreaWidth - obstacleWidth) / 2;
			obstacleStartDist = Math.Max(obstacleStartDist - halfDelta, 0);
			obstacleEndDist = obstacleStartDist + MinObstacleAreaWidth;

			if (obstacleEndDist > maxAllowedDist) {
				float delta = obstacleEndDist - maxAllowedDist;
				obstacleStartDist -= delta;
				obstacleEndDist -= delta;
			}
		}
	}

	void RaiseAreasWithInternalObstacles() {
		List<NavArea> areasToDelete = [];

		foreach (NavArea area in NavArea.TheNavAreas) {
			if (!area.HasNodes() || area.GetSizeX() != GenerationStepSize || area.GetSizeY() != GenerationStepSize)
				continue;

			float[] obstacleZ = [-float.MaxValue, -float.MaxValue];
			float obstacleZMax = -float.MaxValue;
			NavDirType obstacleDir = NavDirType.North;
			float obstacleStartDist = GenerationStepSize;
			float obstacleEndDist = 0;

			bool isStairNeighbor = false;

			for (int i = 0; i < (int)NavDirType.NumDirections; i++) {
				NavDirType dir = (NavDirType)i;

				NavCornerType[] corner = new NavCornerType[2];
				int edgesBlocked = 0;
				corner[0] = (NavCornerType)((i + 3) % (int)NavCornerType.NumCorners);
				corner[1] = (NavCornerType)((i + 2) % (int)NavCornerType.NumCorners);
				float[] obstacleZThisDir = [-float.MaxValue, -float.MaxValue];
				float obstacleStartDistThisDir = GenerationStepSize;
				float obstacleEndDistThisDir = 0;

				for (int iEdge = 0; iEdge < 2; iEdge++) {
					NavCornerType cornerType = corner[iEdge];
					NavNode? nodeFrom = area.Node[(int)cornerType];
					if (nodeFrom != null) {
						float obstacleHeight = nodeFrom.ObstacleHeight[(int)dir];
						if (obstacleHeight > StepHeight) {
							edgesBlocked++;
							float obstacle = nodeFrom.GetPosition().Z + obstacleHeight;
							if (obstacle > obstacleZThisDir[iEdge])
								obstacleZThisDir[iEdge] = obstacle;
							obstacleStartDistThisDir = Math.Min(nodeFrom.ObstacleStartDist[(int)dir], obstacleStartDistThisDir);
							obstacleEndDistThisDir = Math.Max(nodeFrom.ObstacleEndDist[(int)dir], obstacleEndDistThisDir);
						}
					}
				}

				int BlockedEdgeCutoff = 2;
				List<NavConnect>? connections = area.GetAdjacentAreas(dir);
				if (connections != null) {
					foreach (NavConnect connect in connections) {
						NavArea? connectedArea = connect.Area;
						if (connectedArea != null && connectedArea.HasAttributes(NavAttributeType.Stairs)) {
							isStairNeighbor = true;
							BlockedEdgeCutoff = 1;
							break;
						}
					}
				}

				if (edgesBlocked >= BlockedEdgeCutoff && Math.Max(obstacleZThisDir[0], obstacleZThisDir[1]) > obstacleZMax) {
					obstacleZ[0] = obstacleZThisDir[0];
					obstacleZ[1] = obstacleZThisDir[1];
					obstacleZMax = Math.Max(obstacleZ[0], obstacleZ[1]);
					obstacleDir = dir;
					obstacleStartDist = obstacleStartDistThisDir;
					obstacleEndDist = obstacleStartDistThisDir;
				}

				if (isStairNeighbor && obstacleZMax > -float.MaxValue) {
					areasToDelete.Add(area);
					continue;
				}
			}

			if (obstacleZMax > -float.MaxValue) {
				AdjustObstacleDistances(ref obstacleStartDist, ref obstacleEndDist, GenerationStepSize);
				Assert(obstacleEndDist - obstacleStartDist >= MinObstacleAreaWidth);

				Vector3[] corner2 = new Vector3[4];
				for (int i = 0; i < (int)NavCornerType.NumCorners; i++)
					corner2[i] = area.GetCorner((NavCornerType)i);

				switch (obstacleDir) {
					case NavDirType.North:
						corner2[(int)NavCornerType.NorthWest].Y = corner2[(int)NavCornerType.SouthWest].Y - obstacleEndDist;
						corner2[(int)NavCornerType.NorthEast].Y = corner2[(int)NavCornerType.SouthEast].Y - obstacleEndDist;
						corner2[(int)NavCornerType.SouthWest].Y -= obstacleStartDist;
						corner2[(int)NavCornerType.SouthEast].Y -= obstacleStartDist;
						(float temp1, float temp2) = (obstacleZ[0], obstacleZ[1]);
						obstacleZ[0] = temp2;
						obstacleZ[1] = temp1;
						break;
					case NavDirType.South:
						corner2[(int)NavCornerType.SouthWest].Y = corner2[(int)NavCornerType.NorthWest].Y + obstacleEndDist;
						corner2[(int)NavCornerType.SouthEast].Y = corner2[(int)NavCornerType.NorthEast].Y + obstacleEndDist;
						corner2[(int)NavCornerType.NorthWest].Y += obstacleStartDist;
						corner2[(int)NavCornerType.NorthEast].Y += obstacleStartDist;
						(float temp3, float temp4) = (obstacleZ[0], obstacleZ[1]);
						obstacleZ[0] = temp4;
						obstacleZ[1] = temp3;
						break;
					case NavDirType.East:
						corner2[(int)NavCornerType.NorthEast].X = corner2[(int)NavCornerType.NorthWest].X + obstacleEndDist;
						corner2[(int)NavCornerType.SouthEast].X = corner2[(int)NavCornerType.SouthWest].X + obstacleEndDist;
						corner2[(int)NavCornerType.NorthWest].X += obstacleStartDist;
						corner2[(int)NavCornerType.SouthWest].X += obstacleStartDist;
						(float temp5, float temp6) = (obstacleZ[0], obstacleZ[1]);
						obstacleZ[0] = temp6;
						obstacleZ[1] = temp5;
						break;
					case NavDirType.West:
						corner2[(int)NavCornerType.NorthWest].X = corner2[(int)NavCornerType.NorthEast].X - obstacleEndDist;
						corner2[(int)NavCornerType.SouthWest].X = corner2[(int)NavCornerType.SouthEast].X - obstacleEndDist;
						corner2[(int)NavCornerType.NorthEast].X -= obstacleStartDist;
						corner2[(int)NavCornerType.SouthEast].X -= obstacleStartDist;
						(float temp7, float temp8) = (obstacleZ[0], obstacleZ[1]);
						obstacleZ[0] = temp8;
						obstacleZ[1] = temp7;
						break;
				}

				corner2[(int)NavCornerType.NorthWest].Z = obstacleZ[0];
				corner2[(int)NavCornerType.NorthEast].Z = obstacleZ[1];
				corner2[(int)NavCornerType.SouthEast].Z = obstacleZ[1];
				corner2[(int)NavCornerType.SouthWest].Z = obstacleZ[0];

				RemoveNavArea(area);
				area.Build(corner2[(int)NavCornerType.NorthWest], corner2[(int)NavCornerType.NorthEast], corner2[(int)NavCornerType.SouthEast], corner2[(int)NavCornerType.SouthWest]);
				Assert(!area.IsDegenerate());
				AddNavArea(area);

				area.RemoveOrthogonalConnections(obstacleDir);
				area.SetAttributes(area.GetAttributes() | NavAttributeType.NoMerge | NavAttributeType.ObstacleTop);
				area.SetAttributes(area.GetAttributes() & ~NavAttributeType.Jump);
				for (int i = 0; i < (int)NavCornerType.NumCorners; i++)
					area.Node[i] = null;
			}
		}

		foreach (NavArea area in areasToDelete) {
			NavArea.TheNavAreas.Remove(area);
			DestroyArea(area);
		}
	}

	void CreateObstacleTopAreas() {
		foreach (NavArea area in NavArea.TheNavAreas) {
			if (!area.HasNodes() || area.GetAttributes() != 0)
				continue;

			for (int i = 0; i < (int)NavDirType.NumDirections; i++) {
				NavDirType dir = (NavDirType)i;
				List<NavConnect>? connections = area.GetAdjacentAreas(dir);
				if (connections == null)
					continue;

				foreach (NavConnect connect in connections) {
					NavArea? areaOther = connect.Area;
					if (areaOther != null && areaOther.HasAttributes(NavAttributeType.Jump | NavAttributeType.ObstacleTop))
						continue;

					if (!CreateObstacleTopAreaIfNecessary(area, areaOther!, dir, false))
						CreateObstacleTopAreaIfNecessary(area, areaOther!, dir, true);
				}
			}
		}
	}

	bool CreateObstacleTopAreaIfNecessary(NavArea area, NavArea areaOther, NavDirType dir, bool multiNode) {
		float obstacleHeightMin = float.MaxValue;
		float obstacleHeightMax = 0;
		float obstacleHeightStart = 0;
		float obstacleHeightEnd = 0;
		float obstacleDistMin = GenerationStepSize;
		float obstacleDistMax = 0;

		Vector3 center = default;

		area.ComputePortal(areaOther, dir, ref center, out float halfPortalWidth);

		if (halfPortalWidth > 0) {
			NavCornerType cornerStart = (NavCornerType)dir;
			NavCornerType cornerEnd = (NavCornerType)(((int)dir + 1) % (int)NavCornerType.NumCorners);
			NavNode node = area.Node[(int)cornerStart]!;
			NavNode nodeEnd = area.Node[(int)cornerEnd]!;
			NavDirType dirEdge = (NavDirType)(((int)dir + 1) % (int)NavDirType.NumDirections);
			obstacleHeightMin = float.MaxValue;
			float zStart = 0, zEnd = 0;
			while (node != null) {
				Vector3 vecToPortalCenter = node.GetPosition() - center;
				vecToPortalCenter.Z = 0;
				if (vecToPortalCenter.Length() < halfPortalWidth + 1.0f) {
					float obstacleHeight = 0;
					float obstacleDistStartCur = node.ObstacleStartDist[(int)dir];
					float obstacleDistEndCur = node.ObstacleEndDist[(int)dir];

					if (!multiNode)
						obstacleHeight = node.ObstacleHeight[(int)dir];
					else {
						if (!areaOther.Contains(node.GetPosition())) {
							NavNode nodeTowardOtherArea = node.GetConnectedNode(dir);
							if (nodeTowardOtherArea != null) {
								float deltaZ = nodeTowardOtherArea.GetPosition().Z - node.GetPosition().Z;
								if (deltaZ > MaxTraversableHeight) {
									bool inOtherArea = false;
									if (areaOther.Contains(nodeTowardOtherArea.GetPosition())) {
										float z = areaOther.GetZ(nodeTowardOtherArea.GetPosition().X, nodeTowardOtherArea.GetPosition().Y);
										float deltaZ2 = Math.Abs(nodeTowardOtherArea.GetPosition().Z - z);
										if (deltaZ2 < 2.0f)
											inOtherArea = true;
									}

									if (!inOtherArea) {
										NavNode nodeTowardOtherArea2 = nodeTowardOtherArea.GetConnectedNode(dir);
										if (nodeTowardOtherArea2 != null && areaOther.Contains(nodeTowardOtherArea2.GetPosition())) {
											float areaDeltaZ = node.GetPosition().Z - nodeTowardOtherArea2.GetPosition().Z;
											if (Math.Abs(areaDeltaZ) <= MaxTraversableHeight) {
												obstacleHeight = deltaZ;
												obstacleDistStartCur = GenerationStepSize - (MinObstacleAreaWidth / 2);
												obstacleDistEndCur = GenerationStepSize + (MinObstacleAreaWidth / 2);
											}
										}
									}
								}
							}
						}
					}

					obstacleHeightMin = Math.Min(obstacleHeight, obstacleHeightMin);
					obstacleHeightMax = Math.Max(obstacleHeight, obstacleHeightMax);
					obstacleDistMin = Math.Min(obstacleDistStartCur, obstacleDistMin);
					obstacleDistMax = Math.Max(obstacleDistEndCur, obstacleDistMax);

					if (obstacleHeightStart == 0) {
						obstacleHeightStart = obstacleHeight;
						zStart = node.GetPosition().Z;
					}
					obstacleHeightEnd = obstacleHeight;
					zEnd = node.GetPosition().Z;
				}
				if (node == nodeEnd)
					break;

				node = node.GetConnectedNode(dirEdge);
			}

			if ((obstacleHeightMax > MaxTraversableHeight) && (obstacleHeightMin > MaxTraversableHeight)) {
				if ((obstacleHeightMax > obstacleHeightStart) && (obstacleHeightMax > obstacleHeightEnd)) {
					obstacleHeightStart = obstacleHeightMax;
					obstacleHeightEnd = obstacleHeightMax;
				}

				if (dir == NavDirType.South || dir == NavDirType.West) {
					(float temp9, float temp10) = (obstacleHeightStart, obstacleHeightEnd);
					obstacleHeightStart = temp10;
					obstacleHeightEnd = temp9;
					(float temp11, float temp12) = (zStart, zEnd);
					zStart = temp12;
					zEnd = temp11;
				}

				AdjustObstacleDistances(ref obstacleDistMin, ref obstacleDistMax, multiNode ? GenerationStepSize * 2 : GenerationStepSize);
				Assert(obstacleDistMin < obstacleDistMax);
				Assert(obstacleDistMax - obstacleDistMin >= MinObstacleAreaWidth);
				float newAreaWidth = obstacleDistMax - obstacleDistMin;
				Assert(newAreaWidth > 0);

				AddDirectionVector(ref center, dir, obstacleDistMin + (newAreaWidth / 2));

				Vector3 cornerNW = default, cornerNE = default, cornerSE = default, cornerSW = default;
				switch (dir) {
					case NavDirType.North:
					case NavDirType.South:
						cornerNW.Init(center.X - halfPortalWidth, center.Y - (newAreaWidth / 2), zStart + obstacleHeightStart);
						cornerNE.Init(center.X + halfPortalWidth, center.Y - (newAreaWidth / 2), zEnd + obstacleHeightEnd);
						cornerSE.Init(center.X + halfPortalWidth, center.Y + (newAreaWidth / 2), zEnd + obstacleHeightEnd);
						cornerSW.Init(center.X - halfPortalWidth, center.Y + (newAreaWidth / 2), zStart + obstacleHeightStart);
						break;
					case NavDirType.East:
					case NavDirType.West:
						cornerNW.Init(center.X - (newAreaWidth / 2), center.Y - halfPortalWidth, zStart + obstacleHeightStart);
						cornerNE.Init(center.X + (newAreaWidth / 2), center.Y - halfPortalWidth, zEnd + obstacleHeightEnd);
						cornerSE.Init(center.X + (newAreaWidth / 2), center.Y + halfPortalWidth, zEnd + obstacleHeightEnd);
						cornerSW.Init(center.X - (newAreaWidth / 2), center.Y + halfPortalWidth, zStart + obstacleHeightStart);
						break;
				}

				NavArea areaNew = CreateArea();
				areaNew.Build(cornerNW, cornerNE, cornerSE, cornerSW);

				NavArea.TheNavAreas.Add(areaNew);
				AddNavArea(areaNew);

				Assert(!areaNew.IsDegenerate());

				Msg($"Created new fencetop area {areaNew.GetID()}({areaNew.GetDebugID()}) between {area.GetID()}({area.GetDebugID()}) and {areaOther.GetID()}({areaOther.GetDebugID()})\n");

				areaNew.SetAttributes(area.GetAttributes());
				areaNew.SetAttributes(areaNew.GetAttributes() | NavAttributeType.NoMerge | NavAttributeType.ObstacleTop);

				area.Disconnect(areaOther);
				area.ConnectTo(areaNew, dir);

				areaNew.ConnectTo(area, OppositeDirection(dir));
				areaNew.ConnectTo(areaOther, dir);
				if (areaOther.IsConnected(area, OppositeDirection(dir))) {
					areaOther.Disconnect(area);
					areaOther.ConnectTo(areaNew, OppositeDirection(dir));
				}
				return true;
			}
		}

		return false;
	}

	void RemoveOverlappingObstacleTopAreas() {
		List<NavArea> vecObstacleTopAreas = [];
		for (int it = 0; it < NavArea.TheNavAreas.Count; it++) {
			NavArea area = NavArea.TheNavAreas[it];
			if ((area.GetAttributes() & NavAttributeType.ObstacleTop) != 0)
				vecObstacleTopAreas.Add(area);
		}

		List<NavArea> vecAreasToRemove = [];
		for (int it = 0; it < vecObstacleTopAreas.Count; it++) {
			NavArea area = vecObstacleTopAreas[it];

			Vector3 normal = default, otherNormal = default;
			area.ComputeNormal(ref normal);
			area.ComputeNormal(ref otherNormal, true);

			float lowestNormalZ = Math.Min(normal.Z, otherNormal.Z);
			if (lowestNormalZ < nav_slope_limit.GetFloat())
				vecAreasToRemove.Add(area);

			for (int it2 = it + 1; it2 < vecObstacleTopAreas.Count; it2++) {
				NavArea areaOther = vecObstacleTopAreas[it2];
				if (area.IsOverlapping(areaOther)) {
					if (area.Contains(areaOther))
						vecAreasToRemove.Add(areaOther);
					else if (areaOther.Contains(area))
						vecAreasToRemove.Add(area);
					else {
						NavArea areaToRemove = area.GetSizeX() * area.GetSizeY() > areaOther.GetSizeX() * areaOther.GetSizeY() ? areaOther : area;
						vecAreasToRemove.Add(areaToRemove);
					}
				}
			}
		}

		while (vecAreasToRemove.Count > 0) {
			NavArea areaToDelete = vecAreasToRemove[0];
			RemoveFromSelectedSet(areaToDelete);
			OnEditDestroyNotify(areaToDelete);
			NavArea.TheNavAreas.Remove(areaToDelete);
			DestroyArea(areaToDelete);

			vecAreasToRemove.RemoveAll(area => area == areaToDelete);
		}
	}

	void MarkStairAreas() {
		foreach (NavArea area in NavArea.TheNavAreas)
			area.TestStairs();
	}

	void RemoveJumpAreas() {
		if (!nav_generate_fixup_jump_areas.GetBool())
			return;

		List<NavArea> unusedAreas = [];

		foreach (NavArea area in NavArea.TheNavAreas) {
			if ((area.GetAttributes() & NavAttributeType.Jump) == 0)
				continue;

			unusedAreas.Add(area);
		}

		foreach (NavArea area in unusedAreas) {
			OnEditDestroyNotify(area);
			NavArea.TheNavAreas.Remove(area);
			DestroyArea(area);
		}

		StripNavigationAreas();

		SetMarkedArea(null);
		MarkedCorner = NavCornerType.NumCorners;
	}

	public void CommandNavRemoveJumpAreas() { }

	static void SplitX(NavArea area) {
		if (area.IsRoughlySquare())
			return;

		float split = area.GetSizeX();
		split /= 2.0f;
		split += area.GetCorner(NavCornerType.NorthWest).X;
		split = Instance!.SnapToGrid(split);

		const float epsilon = 0.1f;
		if (Math.Abs(split - area.GetCorner(NavCornerType.NorthWest).X) < epsilon ||
			Math.Abs(split - area.GetCorner(NavCornerType.SouthEast).X) < epsilon)
			return;

		if (area.SplitEdit(false, split, out NavArea? alpha, out NavArea? beta)) {
			SplitX(alpha);
			SplitX(beta);
		}
	}

	static void SplitY(NavArea area) {
		if (area.IsRoughlySquare())
			return;

		float split = area.GetSizeY();
		split /= 2.0f;
		split += area.GetCorner(NavCornerType.NorthWest).Y;
		split = Instance!.SnapToGrid(split);

		const float epsilon = 0.1f;
		if (Math.Abs(split - area.GetCorner(NavCornerType.NorthWest).Y) < epsilon ||
			Math.Abs(split - area.GetCorner(NavCornerType.SouthEast).Y) < epsilon)
			return;

		if (area.SplitEdit(true, split, out NavArea? alpha, out NavArea? beta)) {
			SplitY(alpha);
			SplitY(beta);
		}
	}

	void SquareUpAreas() {
		int it = 0;
		while (it < NavArea.TheNavAreas.Count) {
			NavArea area = NavArea.TheNavAreas[it];
			++it;

			if (area.HasNodes() && !area.IsRoughlySquare()) {
				if (area.GetSizeX() > area.GetSizeY())
					SplitX(area);
				else
					SplitY(area);
			}
		}
	}

	void StitchGeneratedAreas() {
		if (GenerationMode == GenerationModeType.Incremental) {
			IncrementallyGeneratedAreas incrementalAreas = new();
			StitchMesh(incrementalAreas.Invoke);
		}
	}

	void StitchAreaSet(List<NavArea> areas) {
		AreaSet areaSet = new(areas);
		StitchMesh(areaSet.Invoke);
	}

	void StitchAreaIntoMesh(NavArea area, NavDirType dir, Func<NavArea, bool> func) {
		Vector3 corner1 = default, corner2 = default;
		switch (dir) {
			default:
				Assert(false);
				break;
			case NavDirType.North:
				corner1 = area.GetCorner(NavCornerType.NorthWest);
				corner2 = area.GetCorner(NavCornerType.NorthEast);
				break;
			case NavDirType.South:
				corner1 = area.GetCorner(NavCornerType.SouthWest);
				corner2 = area.GetCorner(NavCornerType.SouthEast);
				break;
			case NavDirType.East:
				corner1 = area.GetCorner(NavCornerType.NorthEast);
				corner2 = area.GetCorner(NavCornerType.SouthEast);
				break;
			case NavDirType.West:
				corner1 = area.GetCorner(NavCornerType.NorthWest);
				corner2 = area.GetCorner(NavCornerType.SouthWest);
				break;
		}

		Vector3 edgeDir = corner2 - corner1;
		edgeDir.Z = 0.0f;

		float edgeLength = edgeDir.Length();
		edgeDir /= edgeLength;

		for (float n = 0; n < edgeLength - 1.0f; n += GenerationStepSize) {
			Vector3 sourcePos = corner1 + edgeDir * (n + 0.5f);
			sourcePos.Z += HalfHumanHeight;

			Vector3 targetPos = sourcePos;
			switch (dir) {
				case NavDirType.North:
					targetPos.Y -= GenerationStepSize * 0.5f;
					break;
				case NavDirType.South:
					targetPos.Y += GenerationStepSize * 0.5f;
					break;
				case NavDirType.East:
					targetPos.X += GenerationStepSize * 0.5f;
					break;
				case NavDirType.West:
					targetPos.X -= GenerationStepSize * 0.5f;
					break;
			}

			if (Instance!.FindNavAreaOrLadderAlongRay(sourcePos, targetPos, out NavArea? targetArea, out _, null) && targetArea != null && !func(targetArea)) {
				targetPos.Z = targetArea.GetZ(targetPos.X, targetPos.Y) + HalfHumanHeight;

				if (TestStitchConnection(area, targetArea, sourcePos, targetPos))
					area.ConnectTo(targetArea, dir);
			}
			else {
				sourcePos.Z -= HalfHumanHeight;
				sourcePos.Z += 1;
				NavArea? downArea = FindJumpDownArea(sourcePos, dir);
				if (downArea != null && downArea != area && !func(downArea))
					area.ConnectTo(downArea, dir);
			}
		}
	}

	public bool CheckCliff(Vector3 fromPos, NavDirType dir) {
		return false;

		Vector3 toPos = fromPos;
		AddDirectionVector(ref toPos, dir, GenerationStepSize);

		if (TraceAdjacentNode(0, fromPos, toPos, out Trace trace, DeathDrop * 10) && !trace.AllSolid && !trace.StartSolid) {
			float deltaZ = fromPos.Z - trace.EndPos.Z;
			if (deltaZ > CliffHeight)
				return true;

			if ((dir == NavDirType.South || dir == NavDirType.East) && Math.Abs(deltaZ) < StepHeight)
				return CheckCliff(trace.EndPos, dir);
		}

		return false;
	}

	static NavArea? FindFirstAreaInDirection(Vector3 start, NavDirType dir, float range, float beneathLimit, BaseEntity? traceIgnore, out Vector3? closePos) {
		NavArea? area = null;
		Vector3 pos = start;

		int end = (int)((range / GenerationStepSize) + 0.5f);

		closePos = null;

		for (int i = 1; i <= end; i++) {
			AddDirectionVector(ref pos, dir, GenerationStepSize);

			Util.TraceHull(start, pos, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), traceIgnore, CollisionGroup.None, out Trace result);

			if (result.Fraction < 1.0f)
				break;

			area = Instance!.GetNavArea(pos, beneathLimit);
			if (area != null) {
				closePos = new Vector3(pos.X, pos.Y, area.GetZ(pos.X, pos.Y));
				break;
			}
		}

		return area;
	}

	static NavArea? FindJumpDownArea(Vector3 pos, NavDirType dir) {
		if (!nav_generate_jump_connections.GetBool())
			return null;

		Vector3 start = new Vector3(pos.X, pos.Y, pos.Z + HalfHumanHeight);
		AddDirectionVector(ref start, dir, GenerationStepSize / 2.0f);

		NavArea? downArea = FindFirstAreaInDirection(start, dir, GenerationStepSize * 4, DeathDrop, null, out Vector3? toPos);

		if (downArea != null && TestJumpDown(pos, toPos.Value))
			return downArea;

		return null;
	}

	static bool TestJumpDown(Vector3 fromPos, Vector3 toPos) {
		float dz = fromPos.Z - toPos.Z;

		if (dz <= JumpCrouchHeight || dz >= DeathDrop)
			return false;

		Vector3 from, to = default;
		float up;
		Trace result;

		for (up = 1.0f; up <= ClimbUpHeight; up += 1.0f) {
			from = fromPos;
			to = fromPos;
			to.Z += up;

			Util.TraceHull(from, to, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), null, CollisionGroup.None, out result);
			if (result.Fraction <= 0.0f || result.StartSolid)
				continue;

			from = to;
			from.Z -= 0.5f;
			to = toPos;
			to.Z = from.Z;

			Util.TraceHull(from, to, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), null, CollisionGroup.None, out result);
			if (result.Fraction != 1.0f || result.StartSolid)
				continue;

			break;
		}

		if (up > ClimbUpHeight)
			return false;

		from = to;
		to.Z = toPos.Z + 2.0f;
		Util.TraceHull(from, to, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), null, CollisionGroup.None, out result);
		if (result.Fraction <= 0.0f || result.StartSolid)
			return false;

		if (result.EndPos.Z > to.Z + StepHeight)
			return false;

		return true;
	}

	bool TestStitchConnection(NavArea source, NavArea target, Vector3 sourcePos, Vector3 targetPos) {
		Vector3 from = sourcePos;
		Vector3 pos = targetPos;
		TraceFilterWalkableEntities filter = new(null, CollisionGroup.None, WalkThruFlags.Everything);
		Vector3 to = default;
		Vector3 toNormal = default;
		bool success = false;

		if (TraceAdjacentNode(0, from, pos, out Trace result)) {
			to = result.EndPos;
			toNormal = result.Plane.Normal;
			success = true;
		}
		else {
			for (float height = StepHeight; height <= ClimbUpHeight; height += 1.0f) {
				Vector3 start = from;
				Vector3 end = pos;
				start.Z += height;
				end.Z += height;
				Util.TraceHull(start, end, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out Trace tr);
				if (!tr.StartSolid && tr.Fraction == 1.0f) {
					if (!StayOnFloor(ref tr))
						break;

					to = tr.EndPos;
					toNormal = tr.Plane.Normal;

					start = end = from;
					end.Z += height;
					Util.TraceHull(start, end, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out tr);
					if (tr.Fraction < 1.0f)
						break;

					success = true;
					break;
				}
			}
		}

		return success;
	}

	void ConnectGeneratedAreas() {
		Msg("Connecting navigation areas...\n");

		for (int it = 0; it < NavArea.TheNavAreas.Count; it++) {
			NavArea area = NavArea.TheNavAreas[it];

			NavNode? node;
			for (node = area.Node[(int)NavCornerType.NorthWest]; node != area.Node[(int)NavCornerType.NorthEast]; node = node.GetConnectedNode(NavDirType.East)) {
				NavNode? adj = node!.GetConnectedNode(NavDirType.North);

				if (adj != null && adj.GetArea() != null && adj.GetConnectedNode(NavDirType.South) == node) {
					area.ConnectTo(adj.GetArea()!, NavDirType.North);
				}
				else {
					NavArea? downArea = FindJumpDownArea(node.GetPosition(), NavDirType.North);
					if (downArea != null && downArea != area)
						area.ConnectTo(downArea, NavDirType.North);
				}
			}

			for (node = area.Node[(int)NavCornerType.NorthWest]; node != area.Node[(int)NavCornerType.SouthWest]; node = node.GetConnectedNode(NavDirType.South)) {
				NavNode? adj = node!.GetConnectedNode(NavDirType.West);
				if (adj != null && adj.GetArea() != null && adj.GetConnectedNode(NavDirType.East) == node)
					area.ConnectTo(adj.GetArea()!, NavDirType.West);
				else {
					NavArea? downArea = FindJumpDownArea(node.GetPosition(), NavDirType.West);
					if (downArea != null && downArea != area)
						area.ConnectTo(downArea, NavDirType.West);
				}
			}

			node = area.Node[(int)NavCornerType.SouthWest];
			if (node != null)
				node = node.GetConnectedNode(NavDirType.North);

			if (node != null) {
				NavNode? end = area.Node[(int)NavCornerType.SouthEast]!.GetConnectedNode(NavDirType.North);
				for (; node != null && node != end; node = node.GetConnectedNode(NavDirType.East)) {
					NavNode? adj = node.GetConnectedNode(NavDirType.South);

					if (adj != null && adj.GetArea() != null && adj.GetConnectedNode(NavDirType.North) == node)
						area.ConnectTo(adj.GetArea()!, NavDirType.South);
					else {
						NavArea? downArea = FindJumpDownArea(node.GetPosition(), NavDirType.South);
						if (downArea != null && downArea != area)
							area.ConnectTo(downArea, NavDirType.South);
					}
				}
			}

			for (node = area.Node[(int)NavCornerType.SouthWest]; node != area.Node[(int)NavCornerType.SouthEast]; node = node.GetConnectedNode(NavDirType.East)) {
				if (node!.GetArea() != null)
					continue;

				NavNode? adj = node.GetConnectedNode(NavDirType.South);

				if (node.IsBlockedInAnyDirection() || (adj != null && adj.IsBlockedInAnyDirection()))
					continue;

				if (adj == null || adj.GetArea() == null) {
					NavArea? downArea = FindJumpDownArea(node.GetPosition(), NavDirType.South);
					if (downArea != null && downArea != area)
						area.ConnectTo(downArea, NavDirType.South);
				}
			}

			node = area.Node[(int)NavCornerType.NorthEast];
			if (node != null)
				node = node.GetConnectedNode(NavDirType.West);

			if (node != null) {
				NavNode? end = area.Node[(int)NavCornerType.SouthEast]!.GetConnectedNode(NavDirType.West);
				for (; node != null && node != end; node = node.GetConnectedNode(NavDirType.South)) {
					NavNode? adj = node.GetConnectedNode(NavDirType.East);

					if (adj != null && adj.GetArea() != null && adj.GetConnectedNode(NavDirType.West) == node) {
						area.ConnectTo(adj.GetArea()!, NavDirType.East);
					}
					else {
						NavArea? downArea = FindJumpDownArea(node.GetPosition(), NavDirType.East);
						if (downArea != null && downArea != area)
							area.ConnectTo(downArea, NavDirType.East);
					}
				}
			}

			for (node = area.Node[(int)NavCornerType.NorthEast]; node != area.Node[(int)NavCornerType.SouthEast]; node = node.GetConnectedNode(NavDirType.South)) {
				if (node!.GetArea() != null)
					continue;

				NavNode? adj = node.GetConnectedNode(NavDirType.East);

				if (node.IsBlockedInAnyDirection() || (adj != null && adj.IsBlockedInAnyDirection()))
					continue;

				if (adj == null || adj.GetArea() == null) {
					NavArea? downArea = FindJumpDownArea(node.GetPosition(), NavDirType.East);
					if (downArea != null && downArea != area)
						area.ConnectTo(downArea, NavDirType.East);
				}
			}
		}

		StitchGeneratedAreas();
	}

	void MergeGeneratedAreas() {
		Msg("Merging navigation areas...\n");

		bool merged;

		do {
			merged = false;

			foreach (NavArea area in NavArea.TheNavAreas) {
				if (!area.HasNodes() || (area.GetAttributes() & NavAttributeType.NoMerge) != 0)
					continue;

				for (int nit = 0; nit < area.Connect[(int)NavDirType.North].Count; nit++) {
					NavArea adjArea = area.Connect[(int)NavDirType.North][nit].Area!;
					if (!area.IsAbleToMergeWith(adjArea))
						continue;

					if (area.GetSizeY() + adjArea.GetSizeY() > GenerationStepSize * nav_area_max_size.GetInt())
						continue;

					if (area.Node[(int)NavCornerType.NorthWest] == adjArea.Node[(int)NavCornerType.SouthWest] &&
							area.Node[(int)NavCornerType.NorthEast] == adjArea.Node[(int)NavCornerType.SouthEast] &&
							area.GetAttributes() == adjArea.GetAttributes() &&
							area.IsCoplanar(adjArea)) {
						area.Node[(int)NavCornerType.NorthWest] = adjArea.Node[(int)NavCornerType.NorthWest];
						area.Node[(int)NavCornerType.NorthEast] = adjArea.Node[(int)NavCornerType.NorthEast];

						merged = true;

						area.FinishMerge(adjArea);

						break;
					}
				}

				if (merged)
					break;

				for (int sit = 0; sit < area.Connect[(int)NavDirType.South].Count; sit++) {
					NavArea adjArea = area.Connect[(int)NavDirType.South][sit].Area!;
					if (!area.IsAbleToMergeWith(adjArea))
						continue;

					if (area.GetSizeY() + adjArea.GetSizeY() > GenerationStepSize * nav_area_max_size.GetInt())
						continue;

					if (adjArea.Node[(int)NavCornerType.NorthWest] == area.Node[(int)NavCornerType.SouthWest] &&
							adjArea.Node[(int)NavCornerType.NorthEast] == area.Node[(int)NavCornerType.SouthEast] &&
							area.GetAttributes() == adjArea.GetAttributes() &&
							area.IsCoplanar(adjArea)) {
						area.Node[(int)NavCornerType.SouthWest] = adjArea.Node[(int)NavCornerType.SouthWest];
						area.Node[(int)NavCornerType.SouthEast] = adjArea.Node[(int)NavCornerType.SouthEast];

						merged = true;

						area.FinishMerge(adjArea);

						break;
					}
				}

				if (merged)
					break;

				for (int wit = 0; wit < area.Connect[(int)NavDirType.West].Count; wit++) {
					NavArea adjArea = area.Connect[(int)NavDirType.West][wit].Area!;
					if (!area.IsAbleToMergeWith(adjArea))
						continue;

					if (area.GetSizeX() + adjArea.GetSizeX() > GenerationStepSize * nav_area_max_size.GetInt())
						continue;

					if (area.Node[(int)NavCornerType.NorthWest] == adjArea.Node[(int)NavCornerType.NorthEast] &&
							area.Node[(int)NavCornerType.SouthWest] == adjArea.Node[(int)NavCornerType.SouthEast] &&
							area.GetAttributes() == adjArea.GetAttributes() &&
							area.IsCoplanar(adjArea)) {
						area.Node[(int)NavCornerType.NorthWest] = adjArea.Node[(int)NavCornerType.NorthWest];
						area.Node[(int)NavCornerType.SouthWest] = adjArea.Node[(int)NavCornerType.SouthWest];

						merged = true;

						area.FinishMerge(adjArea);

						break;
					}
				}

				if (merged)
					break;

				for (int eit = 0; eit < area.Connect[(int)NavDirType.East].Count; eit++) {
					NavArea adjArea = area.Connect[(int)NavDirType.East][eit].Area!;
					if (!area.IsAbleToMergeWith(adjArea))
						continue;

					if (area.GetSizeX() + adjArea.GetSizeX() > GenerationStepSize * nav_area_max_size.GetInt())
						continue;

					if (adjArea.Node[(int)NavCornerType.NorthWest] == area.Node[(int)NavCornerType.NorthEast] &&
							adjArea.Node[(int)NavCornerType.SouthWest] == area.Node[(int)NavCornerType.SouthEast] &&
							area.GetAttributes() == adjArea.GetAttributes() &&
							area.IsCoplanar(adjArea)) {
						area.Node[(int)NavCornerType.NorthEast] = adjArea.Node[(int)NavCornerType.NorthEast];
						area.Node[(int)NavCornerType.SouthEast] = adjArea.Node[(int)NavCornerType.SouthEast];

						merged = true;

						area.FinishMerge(adjArea);

						break;
					}
				}

				if (merged)
					break;
			}
		} while (merged);
	}

	void FixUpGeneratedAreas() {
		FixCornerOnCornerAreas();
		FixConnections();
	}

	void FixConnections() {
		for (int it = 0; it < NavArea.TheNavAreas.Count; it++) {
			NavArea area = NavArea.TheNavAreas[it];
			if (!area.HasAttributes(NavAttributeType.Stairs))
				continue;

			if (!area.HasNodes())
				continue;

			for (int dir = 0; dir < (int)NavDirType.NumDirections; ++dir) {
				NavCornerType[] cornerType = new NavCornerType[2];
				GetCornerTypesInDirection((NavDirType)dir, out cornerType[0], out cornerType[1]);

				float cornerDeltaZ = Math.Abs(area.GetCorner(cornerType[0]).Z - area.GetCorner(cornerType[1]).Z);
				if (cornerDeltaZ < StepHeight)
					continue;

				List<NavConnect> connectedAreas = area.GetAdjacentAreas((NavDirType)dir);
				List<NavArea> areasToDisconnect = [];
				for (int i = 0; i < connectedAreas.Count; ++i) {
					NavArea adjArea = connectedAreas[i].Area!;
					if (!adjArea.HasNodes())
						continue;

					Vector3 pos = default;
					area.ComputePortal(adjArea, (NavDirType)dir, ref pos, out _);
					adjArea.GetClosestPointOnArea(ref pos, out Vector3 adjPos);

					NavNode node = area.FindClosestNode(pos, (NavDirType)dir)!;
					NavNode adjNode = adjArea.FindClosestNode(adjPos, OppositeDirection((NavDirType)dir))!;
					pos = node.GetPosition();
					adjPos = adjNode.GetPosition();

					if (node == null || adjNode == null)
						continue;

					NavCornerType[] adjCornerType = new NavCornerType[2];
					GetCornerTypesInDirection(OppositeDirection((NavDirType)dir), out adjCornerType[0], out adjCornerType[1]);

					if (node.GetGroundHeightAboveNode(cornerType[0]) > StepHeight)
						areasToDisconnect.Add(adjArea);
					else if (node.GetGroundHeightAboveNode(cornerType[1]) > StepHeight)
						areasToDisconnect.Add(adjArea);
					else if (adjPos.Z + adjNode.GetGroundHeightAboveNode(adjCornerType[0]) > pos.Z + StepHeight)
						areasToDisconnect.Add(adjArea);
					else if (adjPos.Z + adjNode.GetGroundHeightAboveNode(adjCornerType[1]) > pos.Z + StepHeight)
						areasToDisconnect.Add(adjArea);
				}

				for (int i = 0; i < areasToDisconnect.Count; ++i)
					area.Disconnect(areasToDisconnect[i]);
			}
		}

		for (int it = 0; it < NavArea.TheNavAreas.Count; it++) {
			NavArea area = NavArea.TheNavAreas[it];
			List<NavArea> areasToDisconnect = [];
			for (int dir = 0; dir < (int)NavDirType.NumDirections; ++dir) {
				List<NavConnect> connectedAreas = area.GetAdjacentAreas((NavDirType)dir);
				for (int i = 0; i < connectedAreas.Count; ++i) {
					NavArea adjArea = connectedAreas[i].Area!;
					List<NavConnect> adjConnectedAreas = adjArea.GetAdjacentAreas((NavDirType)dir);
					for (int j = 0; j < adjConnectedAreas.Count; ++j) {
						NavArea farArea = adjConnectedAreas[j].Area!;
						if (area.IsConnected(farArea, (NavDirType)dir))
							areasToDisconnect.Add(farArea);
					}
				}
			}

			for (int i = 0; i < areasToDisconnect.Count; ++i)
				area.Disconnect(areasToDisconnect[i]);
		}
	}

	void ClassifyCorners(Vector3[] vec, ref Vector3 vecNW, ref Vector3 vecNE, ref Vector3 vecSE, ref Vector3 vecSW) {
		vecNW = vecNE = vecSE = vecSW = vec[0];

		for (int i = 0; i < 4; i++) {
			if (vec[i].X <= vecNW.X && vec[i].Y <= vecNW.Y)
				vecNW = vec[i];

			if (vec[i].X >= vecNE.X && vec[i].Y <= vecNE.Y)
				vecNE = vec[i];

			if (vec[i].X >= vecSE.X && vec[i].Y >= vecSE.Y)
				vecSE = vec[i];

			if (vec[i].X <= vecSW.X && vec[i].Y >= vecSW.Y)
				vecSW = vec[i];
		}
	}

	void FixCornerOnCornerAreas() {
		const float MaxDrop = StepHeight;

		for (int it = 0; it < NavArea.TheNavAreas.Count; it++) {
			NavArea area = NavArea.TheNavAreas[it];
			for (int iCorner = (int)NavCornerType.NorthWest; iCorner < (int)NavCornerType.NumCorners; iCorner++) {
				NavDirType dirToRight = (NavDirType)iCorner;
				NavDirType dirToLeft = (NavDirType)((iCorner + 3) % (int)NavDirType.NumDirections);

				if (area.GetAdjacentCount(dirToLeft) > 0 || area.GetAdjacentCount(dirToRight) > 0 ||
						area.GetIncomingConnections(dirToLeft).Count > 0 || area.GetIncomingConnections(dirToRight).Count > 0)
					continue;

				Vector3 cornerPos = area.GetCorner((NavCornerType)iCorner);
				NavDirType dirToRightTwice = DirectionRight(dirToRight);
				NavDirType dirToLeftTwice = DirectionLeft(dirToLeft);
				NavDirType[] dirsAlongOtherEdge = [dirToLeft, dirToRight];
				NavDirType[] dirsAlongOurEdge = [dirToLeftTwice, dirToRightTwice];

				for (int iDir = 0; iDir < dirsAlongOtherEdge.Length; iDir++) {
					NavDirType dirAlongOtherEdge = dirsAlongOtherEdge[iDir];
					NavDirType dirAlongOurEdge = dirsAlongOurEdge[iDir];

					Vector3 vecDeltaOtherEdge = default;
					DirectionToVector2D(dirAlongOtherEdge, ref Unsafe.As<Vector3, Vector2>(ref vecDeltaOtherEdge));
					vecDeltaOtherEdge.Z = 0;
					vecDeltaOtherEdge *= GenerationStepSize * 0.5f;
					Vector3 vecOtherEdgePos = cornerPos + vecDeltaOtherEdge;

					NavArea? areaOther = GetNavArea(vecOtherEdgePos);
					Assert(areaOther != area);
					if (areaOther == null)
						continue;

					if (!TraceAdjacentNode(0, cornerPos, vecOtherEdgePos, out Trace result, MaxDrop))
						continue;

					int iCornerOther = (iCorner + 2) % (int)NavCornerType.NumCorners;
					Vector3 cornerPosOther = areaOther.GetCorner((NavCornerType)iCornerOther);

					if (cornerPos != cornerPosOther)
						continue;

					Vector3 vecDeltaOurEdge = default;
					DirectionToVector2D(dirAlongOurEdge, ref Unsafe.As<Vector3, Vector2>(ref vecDeltaOurEdge));
					vecDeltaOurEdge.Z = 0;
					vecDeltaOurEdge *= GenerationStepSize * 0.5f;
					Vector3[] vecCorner =
					[
						cornerPos + vecDeltaOtherEdge + vecDeltaOurEdge,
						cornerPos + vecDeltaOtherEdge,
						cornerPos,
						cornerPos + vecDeltaOurEdge,
					];
					TraceFilterWalkableEntities filter = new(null, CollisionGroup.None, WalkThruFlags.Everything);
					if (!TraceAdjacentNode(0, vecCorner[1], vecCorner[0], out result, MaxDrop) ||
							!TraceAdjacentNode(0, vecCorner[3], vecCorner[0], out result, MaxDrop))
						continue;

					NavArea? areaTest = GetNavArea(vecCorner[0]);
					Assert(areaTest == null);
					if (areaTest != null)
						continue;

					vecCorner[0] = result.EndPos;

					NavArea areaNew = CreateArea();

					Vector3 vecNW = default, vecNE = default, vecSE = default, vecSW = default;
					ClassifyCorners(vecCorner, ref vecNW, ref vecNE, ref vecSE, ref vecSW);
					areaNew.Build(vecNW, vecNE, vecSE, vecSW);

					NavArea.TheNavAreas.Add(areaNew);
					AddNavArea(areaNew);

					areaNew.SetAttributes(area.GetAttributes());

					area.ConnectTo(areaNew, dirAlongOtherEdge);
					areaNew.ConnectTo(area, OppositeDirection(dirAlongOtherEdge));

					areaOther.ConnectTo(areaNew, dirAlongOurEdge);
					areaNew.ConnectTo(areaOther, OppositeDirection(dirAlongOurEdge));
				}
			}
		}
	}

	void SplitAreasUnderOverhangs() {
		bool restartProcessing = false;

		do {
			restartProcessing = false;

			for (int it = 0; it < NavArea.TheNavAreas.Count && !restartProcessing; it++) {
				NavArea area = NavArea.TheNavAreas[it];
				Extent areaExtent = default;
				area.GetExtent(ref areaExtent);

				for (int dir = (int)NavDirType.North; dir < (int)NavDirType.NumDirections && !restartProcessing; dir++) {
					List<NavConnect> connections = area.GetAdjacentAreas((NavDirType)dir);
					for (int iConnection = 0; iConnection < connections.Count && !restartProcessing; iConnection++) {
						NavArea otherArea = connections[iConnection].Area!;
						Extent otherAreaExtent = default;
						otherArea.GetExtent(ref otherAreaExtent);

						if (area.IsOverlapping(otherArea)) {
							const float flMinSeparation = HumanCrouchHeight;
							if (!(areaExtent.Lo.Z > otherAreaExtent.Hi.Z + flMinSeparation) &&
									!(otherAreaExtent.Lo.Z > areaExtent.Hi.Z + flMinSeparation))
								continue;

							NavArea areaBelow = area, areaAbove = otherArea;
							NavDirType dirFromAboveToBelow = OppositeDirection((NavDirType)dir);
							if (otherAreaExtent.Lo.Z < areaExtent.Lo.Z) {
								areaBelow = otherArea;
								areaAbove = area;
								dirFromAboveToBelow = OppositeDirection(dirFromAboveToBelow);
							}
							NavDirType dirFromBelowToAbove = OppositeDirection(dirFromAboveToBelow);

							Extent extentBelow = default, extentAbove = default;
							areaBelow.GetExtent(ref extentBelow);
							areaAbove.GetExtent(ref extentAbove);

							float splitCoord;
							float splitLen;
							float splitEdgeSize;
							bool splitAlongX = false;

							if ((dirFromAboveToBelow == NavDirType.East) || (dirFromAboveToBelow == NavDirType.West)) {
								splitEdgeSize = extentBelow.Hi.X - extentBelow.Lo.X;
								if (extentAbove.Hi.X < extentBelow.Hi.X) {
									splitCoord = extentAbove.Hi.X;
									splitLen = splitCoord - extentBelow.Lo.X;
								}
								else {
									splitCoord = extentAbove.Lo.X;
									splitLen = extentBelow.Hi.X - splitCoord;
								}
							}
							else {
								splitEdgeSize = extentBelow.Hi.Y - extentBelow.Lo.Y;
								splitAlongX = true;
								if (extentAbove.Hi.Y < extentBelow.Hi.Y) {
									splitCoord = extentAbove.Hi.Y;
									splitLen = splitCoord - extentBelow.Lo.Y;
								}
								else {
									splitCoord = extentAbove.Lo.Y;
									splitLen = extentBelow.Hi.Y - splitCoord;
								}
							}
							Assert(splitLen >= 0);
							Assert(splitEdgeSize > 0);

							if (splitLen < GenerationStepSize) {
								if ((splitLen < GenerationStepSize * 0.3) || (splitEdgeSize <= GenerationStepSize * 2))
									continue;

								float splitDelta = GenerationStepSize - splitLen;
								splitCoord += splitDelta * (((dirFromAboveToBelow == (int)NavDirType.North) || (dirFromAboveToBelow == NavDirType.West)) ? -1 : 1);
							}

							bool connectionFromBelow = false, connectionFromAbove = false;
							if (areaBelow.IsConnected(areaAbove, dirFromBelowToAbove)) {
								connectionFromBelow = true;
								areaBelow.Disconnect(areaAbove);
							}
							if (areaAbove.IsConnected(areaBelow, dirFromAboveToBelow)) {
								connectionFromAbove = true;
								areaAbove.Disconnect(areaBelow);
							}

							if (areaBelow.SplitEdit(splitAlongX, splitCoord, out NavArea? newAlpha, out NavArea? newBeta)) {
								NavArea pNewNonoverlappedArea = ((dirFromAboveToBelow == (int)NavDirType.North) || (dirFromAboveToBelow == NavDirType.West)) ? newAlpha : newBeta;

								if (connectionFromAbove)
									areaAbove.ConnectTo(pNewNonoverlappedArea, dirFromAboveToBelow);
								if (connectionFromBelow)
									areaBelow.ConnectTo(pNewNonoverlappedArea, OppositeDirection(dirFromAboveToBelow));

								restartProcessing = true;
							}
						}
					}
				}
			}
		} while (restartProcessing);
	}

	bool TestForValidJumpArea(NavNode node) {
		return true;

		NavNode? east = node.GetConnectedNode(NavDirType.East);
		NavNode? south = node.GetConnectedNode(NavDirType.South);
		if (east == null || south == null)
			return false;

		NavNode? southEast = east.GetConnectedNode(NavDirType.South);
		if (southEast == null)
			return false;

		if (!IsHeightDifferenceValid(
			node.GetPosition().Z,
			south.GetPosition().Z,
			southEast.GetPosition().Z,
			east.GetPosition().Z))
			return false;

		if (!IsHeightDifferenceValid(
			south.GetPosition().Z,
			node.GetPosition().Z,
			southEast.GetPosition().Z,
			east.GetPosition().Z))
			return false;

		if (!IsHeightDifferenceValid(
			southEast.GetPosition().Z,
			south.GetPosition().Z,
			node.GetPosition().Z,
			east.GetPosition().Z))
			return false;

		if (!IsHeightDifferenceValid(
			east.GetPosition().Z,
			south.GetPosition().Z,
			southEast.GetPosition().Z,
			node.GetPosition().Z))
			return false;

		return true;
	}

	bool TestForValidCrouchArea(NavNode node) {
		TraceFilterWalkableEntities filter = new(null, CollisionGroup.PlayerMovement, WalkThruFlags.Everything);
		Vector3 start = node.GetPosition();
		Vector3 end = node.GetPosition();
		end.Z += JumpCrouchHeight;

		Vector3 mins = new(0, 0, 0);
		Vector3 maxs = new(GenerationStepSize, GenerationStepSize, HumanCrouchHeight);

		Util.TraceHull(start, end, mins, maxs, GetGenerationTraceMask(), ref filter, out Trace tr);

		return !tr.AllSolid;
	}

	bool IsHeightDifferenceValid(float test, float other1, float other2, float other3) {
		const float CloseDelta = StepHeight / 2;
		if (Math.Abs(other1 - other2) > CloseDelta)
			return true;

		if (Math.Abs(other1 - other3) > CloseDelta)
			return true;

		if (Math.Abs(other2 - other3) > CloseDelta)
			return true;

		const float MaxDelta = StepHeight;
		if (Math.Abs(test - other1) > MaxDelta)
			return false;

		if (Math.Abs(test - other2) > MaxDelta)
			return false;

		if (Math.Abs(test - other3) > MaxDelta)
			return false;

		return true;
	}

	public bool TestArea(NavNode node, int width, int height) {
		Vector3 normal = node.GetNormal();
		float d = -MathLib.DotProduct(normal, node.GetPosition());

		bool nodeCrouch = node.Crouch[(int)NavCornerType.SouthEast];

		if (node.IsBlocked[(int)NavCornerType.SouthEast])
			return false;

		int nodeAttributes = (int)(node.GetAttributes() & ~NavAttributeType.Crouch);

		const float offPlaneTolerance = 5.0f;

		NavNode vertNode, horizNode;

		vertNode = node;
		int x, y;
		for (y = 0; y < height; y++) {
			horizNode = vertNode;

			for (x = 0; x < width; x++) {
				bool horizNodeCrouch = false;
				bool westEdge = (x == 0);
				bool eastEdge = (x == width - 1);
				bool northEdge = (y == 0);
				bool southEdge = (y == height - 1);

				if (northEdge && westEdge) {
					horizNodeCrouch = horizNode.Crouch[(int)NavCornerType.SouthEast];
					if (horizNode.IsBlocked[(int)NavCornerType.SouthEast])
						return false;
				}
				else if (northEdge && eastEdge) {
					horizNodeCrouch = horizNode.Crouch[(int)NavCornerType.SouthEast] || horizNode.Crouch[(int)NavCornerType.SouthWest];
					if (horizNode.IsBlocked[(int)NavCornerType.SouthEast] || horizNode.IsBlocked[(int)NavCornerType.SouthWest])
						return false;
				}
				else if (southEdge && westEdge) {
					horizNodeCrouch = horizNode.Crouch[(int)NavCornerType.SouthEast] || horizNode.Crouch[(int)NavCornerType.NorthEast];
					if (horizNode.IsBlocked[(int)NavCornerType.SouthEast] || horizNode.IsBlocked[(int)NavCornerType.NorthEast])
						return false;
				}
				else if (southEdge && eastEdge) {
					horizNodeCrouch = (horizNode.GetAttributes() & NavAttributeType.Crouch) != 0;
					if (horizNode.IsBlockedInAnyDirection())
						return false;
				}
				else if (northEdge) {
					horizNodeCrouch = horizNode.Crouch[(int)NavCornerType.SouthEast] || horizNode.Crouch[(int)NavCornerType.SouthWest];
					if (horizNode.IsBlocked[(int)NavCornerType.SouthEast] || horizNode.IsBlocked[(int)NavCornerType.SouthWest])
						return false;
				}
				else if (southEdge) {
					horizNodeCrouch = (horizNode.GetAttributes() & NavAttributeType.Crouch) != 0;
					if (horizNode.IsBlockedInAnyDirection())
						return false;
				}
				else if (eastEdge) {
					horizNodeCrouch = (horizNode.GetAttributes() & NavAttributeType.Crouch) != 0;
					if (horizNode.IsBlockedInAnyDirection())
						return false;
				}
				else if (westEdge) {
					horizNodeCrouch = horizNode.Crouch[(int)NavCornerType.SouthEast] || horizNode.Crouch[(int)NavCornerType.NorthEast];
					if (horizNode.IsBlocked[(int)NavCornerType.SouthEast] || horizNode.IsBlocked[(int)NavCornerType.NorthEast])
						return false;
				}
				else {
					horizNodeCrouch = (horizNode.GetAttributes() & NavAttributeType.Crouch) != 0;
					if (horizNode.IsBlockedInAnyDirection())
						return false;
				}

				if (nodeCrouch != horizNodeCrouch)
					return false;

				int horizNodeAttributes = (int)(horizNode.GetAttributes() & ~NavAttributeType.Crouch);
				if (horizNodeAttributes != nodeAttributes)
					return false;

				if (horizNode.IsCovered)
					return false;

				if (!horizNode.IsClosedCell())
					return false;

				if (!CheckObstacles(horizNode, width, height, x, y))
					return false;

				horizNode = horizNode.GetConnectedNode(NavDirType.East);
				if (horizNode == null)
					return false;

				if (width > 1 || height > 1) {
					float dist = (float)Math.Abs(MathLib.DotProduct(horizNode.GetPosition(), normal) + d);
					if (dist > offPlaneTolerance)
						return false;
				}
			}

			if (!CheckObstacles(horizNode, width, height, x, y))
				return false;

			vertNode = vertNode.GetConnectedNode(NavDirType.South);
			if (vertNode == null)
				return false;

			if (width > 1 || height > 1) {
				float dist = (float)Math.Abs(MathLib.DotProduct(vertNode.GetPosition(), normal) + d);
				if (dist > offPlaneTolerance)
					return false;
			}
		}

		if (width > 1 || height > 1) {
			horizNode = vertNode;

			for (x = 0; x < width; x++) {
				if (!CheckObstacles(horizNode, width, height, x, y))
					return false;

				horizNode = horizNode.GetConnectedNode(NavDirType.East);
				if (horizNode == null)
					return false;

				float dist = (float)Math.Abs(MathLib.DotProduct(horizNode.GetPosition(), normal) + d);
				if (dist > offPlaneTolerance)
					return false;
			}

			if (!CheckObstacles(horizNode, width, height, x, y))
				return false;
		}

		vertNode = node;
		for (y = 0; y < height; ++y) {
			horizNode = vertNode;

			for (x = 0; x < width; ++x) {
				if (!TestForValidJumpArea(horizNode))
					return false;

				if (nodeCrouch && !TestForValidCrouchArea(horizNode))
					return false;

				horizNode = horizNode.GetConnectedNode(NavDirType.East);
			}

			vertNode = vertNode.GetConnectedNode(NavDirType.South);
		}

		if (GenerationMode == GenerationModeType.Incremental) {
			Vector3 nw = node.GetPosition();

			vertNode = node;
			for (y = 0; y < height; ++y)
				vertNode = vertNode.GetConnectedNode(NavDirType.South);
			Vector3 sw = vertNode.GetPosition();

			horizNode = node;
			for (x = 0; x < width; ++x)
				horizNode = horizNode.GetConnectedNode(NavDirType.East);
			Vector3 ne = horizNode.GetPosition();

			vertNode = horizNode;
			for (y = 0; y < height; ++y)
				vertNode = vertNode.GetConnectedNode(NavDirType.South);
			Vector3 se = vertNode.GetPosition();

			TestOverlapping test = new(nw, ne, sw, se);
			if (test.OverlapsExistingArea())
				return false;
		}

		return true;
	}

	bool CheckObstacles(NavNode node, int width, int height, int x, int y) {
		if (width > 1 || height > 1) {
			if ((x > 0) && (node.ObstacleHeight[(int)NavDirType.West] > StepHeight))
				return false;

			if ((y > 0) && (node.ObstacleHeight[(int)NavDirType.North] > StepHeight))
				return false;

			if ((x < width - 1) && (node.ObstacleHeight[(int)NavDirType.East] > StepHeight))
				return false;

			if ((y < height - 1) && (node.ObstacleHeight[(int)NavDirType.South] > StepHeight))
				return false;
		}

		return true;
	}

	int BuildArea(NavNode node, int width, int height) {
		NavNode? nwNode = node;
		NavNode? neNode = null;
		NavNode? swNode = null;
		NavNode? seNode = null;

		NavNode? vertNode = node;
		NavNode? horizNode;

		int coveredNodes = 0;

		for (int y = 0; y < height; y++) {
			horizNode = vertNode;

			for (int x = 0; x < width; x++) {
				horizNode.Cover();
				++coveredNodes;

				horizNode = horizNode.GetConnectedNode(NavDirType.East);
			}

			if (y == 0)
				neNode = horizNode;

			vertNode = vertNode.GetConnectedNode(NavDirType.South);
		}

		swNode = vertNode;

		horizNode = vertNode;
		for (int x = 0; x < width; x++) {
			horizNode = horizNode.GetConnectedNode(NavDirType.East);
		}
		seNode = horizNode;

		if (nwNode == null || neNode == null || swNode == null || seNode == null) {
			Error("BuildArea - null node.\n");
			return -1;
		}

		NavArea? area = CreateArea();
		if (area == null) {
			Error("BuildArea: Out of memory.\n");
			return -1;
		}

		area.Build(nwNode, neNode, seNode, swNode);

		NavArea.TheNavAreas.Add(area);

		area.SetAttributes(node.GetAttributes());

		if (nwNode.ObstacleHeight[(int)NavDirType.South] > StepHeight || nwNode.ObstacleHeight[(int)NavDirType.East] > StepHeight ||
				neNode.ObstacleHeight[(int)NavDirType.West] > StepHeight || neNode.ObstacleHeight[(int)NavDirType.South] > StepHeight ||
				seNode.ObstacleHeight[(int)NavDirType.North] > StepHeight || seNode.ObstacleHeight[(int)NavDirType.West] > StepHeight ||
				swNode.ObstacleHeight[(int)NavDirType.East] > StepHeight || swNode.ObstacleHeight[(int)NavDirType.North] > StepHeight
			) {
			Assert(width == 1);
			Assert(height == 1);

			area.SetAttributes(area.GetAttributes() | NavAttributeType.NoMerge);
		}

		bool nodeCrouch = node.Crouch[(int)NavCornerType.SouthEast];
		if ((area.GetAttributes() & NavAttributeType.Crouch) != 0 && !nodeCrouch)
			area.SetAttributes(area.GetAttributes() & ~NavAttributeType.Crouch);

		return coveredNodes;
	}

	void CreateNavAreasFromNodes() {
		int tryWidth = nav_area_max_size.GetInt();
		int tryHeight = tryWidth;
		int uncoveredNodes = (int)NavNode.GetListLength();

		while (uncoveredNodes > 0) {
			for (NavNode? node = NavNode.GetFirst(); node != null; node = node.GetNext()) {
				if (node.IsCovered)
					continue;

				if (TestArea(node, tryWidth, tryHeight)) {
					int covered = BuildArea(node, tryWidth, tryHeight);
					if (covered < 0) {
						Error("Generate: Error - Data corrupt.\n");
						return;
					}

					uncoveredNodes -= covered;
				}
			}

			if (tryWidth >= tryHeight)
				--tryWidth;
			else
				--tryHeight;

			if (tryWidth <= 0 || tryHeight <= 0)
				break;
		}

#if DEBUG
		Console.WriteLine($"Num areas generated: {NavArea.TheNavAreas.Count}");
#endif

		if (NavArea.TheNavAreas.Count == 0) {
			AllocateGrid(0, 0, 0, 0);
			return;
		}

		Extent extent;
		extent.Lo.X = 9999999999.9f;
		extent.Lo.Y = 9999999999.9f;
		extent.Hi.X = -9999999999.9f;
		extent.Hi.Y = -9999999999.9f;

		foreach (NavArea area in NavArea.TheNavAreas) {
			Extent areaExtent = default;
			area.GetExtent(ref areaExtent);

			if (areaExtent.Lo.X < extent.Lo.X)
				extent.Lo.X = areaExtent.Lo.X;
			if (areaExtent.Lo.Y < extent.Lo.Y)
				extent.Lo.Y = areaExtent.Lo.Y;
			if (areaExtent.Hi.X > extent.Hi.X)
				extent.Hi.X = areaExtent.Hi.X;
			if (areaExtent.Hi.Y > extent.Hi.Y)
				extent.Hi.Y = areaExtent.Hi.Y;
		}

		AllocateGrid(extent.Lo.X, extent.Hi.X, extent.Lo.Y, extent.Hi.Y);

		foreach (NavArea area in NavArea.TheNavAreas)
			AddNavArea(area);

		ConnectGeneratedAreas();
		MarkPlayerClipAreas();
		MarkJumpAreas();
		MergeGeneratedAreas();
		SplitAreasUnderOverhangs();
		SquareUpAreas();
		MarkStairAreas();
		StichAndRemoveJumpAreas();
		HandleObstacleTopAreas();
		FixUpGeneratedAreas();

		if (GenerationMode != GenerationModeType.Incremental) {
			for (int i = 0; i < Ladders.Count; ++i) {
				NavLadder ladder = Ladders[i];
				ladder.ConnectGeneratedLadder(0.0f);
			}
		}
	}

	void AddWalkableSeeds() {
		BaseEntity? spawn = gEntList.FindEntityByClassname(null, GetPlayerSpawnName());

		if (spawn != null) {
			Vector3 pos = spawn.GetAbsOrigin();
			pos.X = SnapToGrid(pos.X);
			pos.Y = SnapToGrid(pos.Y);

			if (FindGroundForNode(ref pos, out Vector3 normal))
				AddWalkableSeed(pos, normal);
		}
	}

	public void ClearWalkableSeeds() => WalkableSeeds.Clear();

	public void BeginGeneration(bool incremental = false) {
		IGameEvent? evnt = gameeventmanager.CreateEvent("nav_generate");
		if (evnt != null)
			gameeventmanager.FireEvent(evnt);

		engine.ServerCommand("bot_kick\n");

		if (incremental)
			nav_quicksave.SetValue(1);

		GenerationState = GenerationStateType.SampleWalkableSpace;
		SampleTick = 0;
		GenerationMode = incremental ? GenerationModeType.Incremental : GenerationModeType.Full;
		LastMsgTime = 0.0f;

		DestroyNavigationMesh(incremental);
		SetNavPlace(UndefinedPlace);

		if (!incremental) {
			BuildLadders();
			AddWalkableSeeds();
		}

		CurrentNode = null;

		if (WalkableSeeds.Count == 0) {
			GenerationMode = GenerationModeType.None;
			Msg("No valid walkable seed positions.  Cannot generate Navigation Mesh.\n");
			return;
		}

		SeedIdx = 0;

		Msg("Generating Navigation Mesh...\n");
		GenerationStartTime = Platform.Time;
	}

	public void BeginAnalysis(bool quitWhenFinished = false) {
		List<NavArea> tmpSet = [];
		foreach (NavArea area in NavArea.TheNavAreas) tmpSet.Add(area);
		NavArea.TheNavAreas.Clear();
		foreach (NavArea area in tmpSet) NavArea.TheNavAreas.Add(area);

		DestroyHidingSpots();

		GenerationState = GenerationStateType.FindHidingSpots;
		GenerationIndex = 0;
		GenerationMode = GenerationModeType.AnalysisOnly;
		QuitWhenFinished = quitWhenFinished;
		LastMsgTime = 0.0f;
		GenerationStartTime = Platform.Time;
	}

	static uint MovedPlayerToArea;
	static CountdownTimer? PlayerSettleTimer;
	static readonly List<NavArea> UnlitAreas = [];
	static readonly List<NavArea> UnlitSeedAreas = [];
	static ConVarRef host_thread_mode = new("host_thread_mode");
	bool UpdateGeneration(float maxTime) {
		double startTime = Platform.Time;
		PlayerSettleTimer ??= new();

		switch (GenerationState) {
			case GenerationStateType.SampleWalkableSpace:
				AnalysisProgress("Sampling walkable space...", 100, SampleTick / 10, false);
				SampleTick = (SampleTick + 1) % 1000;

				while (SampleStep()) {
					if (Platform.Time - startTime > maxTime)
						return true;
				}

				GenerationState = GenerationStateType.CreateAreasFromSamples;
				return true;
			case GenerationStateType.CreateAreasFromSamples:
				Msg("Creating navigation areas from sampled data...\n");
				if (GenerationMode == GenerationModeType.Incremental) {
					ClearSelectedSet();
					foreach (NavArea area in NavArea.TheNavAreas)
						AddToSelectedSet(area);
				}

				CreateNavAreasFromNodes();

				if (GenerationMode == GenerationModeType.Incremental)
					CommandNavToggleSelectedSet();

				DestroyHidingSpots();

				List<NavArea> tmpSet = [];
				foreach (NavArea area in NavArea.TheNavAreas) tmpSet.Add(area);
				NavArea.TheNavAreas.Clear();
				foreach (NavArea area in tmpSet) NavArea.TheNavAreas.Add(area);

				GenerationState = GenerationStateType.FindHidingSpots;
				GenerationIndex = 0;
				return true;
			case GenerationStateType.FindHidingSpots:
				while (GenerationIndex < NavArea.TheNavAreas.Count) {
					NavArea area = NavArea.TheNavAreas[GenerationIndex];
					GenerationIndex++;

					area.ComputeHidingSpots();

					if (Platform.Time - startTime > maxTime) {
						AnalysisProgress("Finding hiding spots...", 100, 100 * GenerationIndex / NavArea.TheNavAreas.Count);
						return true;
					}
				}

				Msg("Finding hiding spots...DONE\n");

				GenerationState = GenerationStateType.FindSniperSpots;
				GenerationIndex = 0;
				return true;
			case GenerationStateType.FindEncounterSpots:
				while (GenerationIndex < NavArea.TheNavAreas.Count) {
					NavArea area = NavArea.TheNavAreas[GenerationIndex];
					GenerationIndex++;

					area.ComputeSpotEncounters();

					if (Platform.Time - startTime > maxTime) {
						AnalysisProgress("Finding encounter spots...", 100, 100 * GenerationIndex / NavArea.TheNavAreas.Count);
						return true;
					}
				}

				Msg("Finding encounter spots...DONE\n");

				GenerationState = GenerationStateType.FindSniperSpots;
				GenerationIndex = 0;
				return true;
			case GenerationStateType.FindSniperSpots:
				while (GenerationIndex < NavArea.TheNavAreas.Count) {
					NavArea area = NavArea.TheNavAreas[GenerationIndex];
					GenerationIndex++;

					area.ComputeSniperSpots();

					if (Platform.Time - startTime > maxTime) {
						AnalysisProgress("Finding sniper spots...", 100, 100 * GenerationIndex / NavArea.TheNavAreas.Count);
						return true;
					}
				}

				Msg("Finding sniper spots...DONE\n");

				GenerationState = GenerationStateType.ComputeMeshVisibility;
				GenerationIndex = 0;
				BeginVisibilityComputations();
				Msg("Computing mesh visibility...\n");
				return true;
			case GenerationStateType.ComputeMeshVisibility:
				while (GenerationIndex < NavArea.TheNavAreas.Count) {
					NavArea area = NavArea.TheNavAreas[GenerationIndex];
					GenerationIndex++;

					area.ComputeVisibilityToMesh();

					if (Platform.Time - startTime > maxTime) {
						AnalysisProgress("Computing mesh visibility...", 100, 100 * GenerationIndex / NavArea.TheNavAreas.Count);
						return true;
					}
				}

				Msg("Optimizing mesh visibility...\n");
				EndVisibilityComputations();
				Msg("Computing mesh visibility...DONE\n");

				GenerationState = GenerationStateType.FindEarliestOccupyTimes;
				GenerationIndex = 0;
				return true;
			case GenerationStateType.FindEarliestOccupyTimes:
				while (GenerationIndex < NavArea.TheNavAreas.Count) {
					NavArea area = NavArea.TheNavAreas[GenerationIndex];
					GenerationIndex++;

					area.ComputeEarliestOccupyTimes();

					if (Platform.Time - startTime > maxTime) {
						AnalysisProgress("Finding earliest occupy times...", 100, 100 * GenerationIndex / NavArea.TheNavAreas.Count);
						return true;
					}
				}

				Msg("Finding earliest occupy times...DONE\n");

				bool shouldSkipLightComputation = GenerationMode == GenerationModeType.Incremental || engine.IsDedicatedServer();

				if (shouldSkipLightComputation)
					GenerationState = GenerationStateType.Custom;
				else {
					GenerationState = GenerationStateType.FindLightIntensity;
					PlayerSettleTimer.Invalidate();
					NavArea.MakeNewMarker();
					UnlitAreas.Clear();
					foreach (NavArea nit in NavArea.TheNavAreas) {
						UnlitAreas.Add(nit);
						UnlitSeedAreas.Add(nit);
					}
				}

				GenerationIndex = 0;
				return true;
			case GenerationStateType.FindLightIntensity:
				// host_thread_mode 0

				BasePlayer? host = Util.GetListenServerHost();

				if (UnlitAreas.Count == 0 || host == null) {
					Msg("Finding light intensity...DONE\n");
					GenerationState = GenerationStateType.Custom;
					GenerationIndex = 0;
					return true;
				}

				if (!PlayerSettleTimer.IsElapsed())
					return true;

				int sit = 0;
				while (sit < UnlitAreas.Count) {
					NavArea area = UnlitAreas[sit];

					if (area.ComputeLighting()) {
						UnlitSeedAreas.Remove(area);
						UnlitAreas.RemoveAt(sit);
						continue;
					}
					else
						sit++;
				}

				if (UnlitAreas.Count > 0) {
					if (UnlitSeedAreas.Count > 0) {
						NavArea moveArea = UnlitSeedAreas[0];
						UnlitSeedAreas.RemoveAt(0);

						Vector3 eyePos = moveArea.GetCenter();
						if (GetGroundHeight(eyePos, out float height, out _))
							eyePos.Z = height + HalfHumanHeight - StepHeight;
						else
							eyePos.Z += HalfHumanHeight - StepHeight;

						host.SetAbsOrigin(eyePos);
						AnalysisProgress("Finding light intensity...", 100, 100 * (NavArea.TheNavAreas.Count - UnlitAreas.Count) / NavArea.TheNavAreas.Count);
						MovedPlayerToArea = moveArea.GetID();
						PlayerSettleTimer.Start(0.1f);
						return true;
					}
					else {
						Msg($"Finding light intensity...DONE ({UnlitAreas.Count} unlit areas)\n");
						if (UnlitAreas.Count > 0) {
							Warning($"To see unlit areas:\n");
							for (int i = 0; i < UnlitAreas.Count; i++) {
								NavArea area = UnlitAreas[i];
								Warning($"nav_unmark; nav_mark {area.GetID()}; nav_warp_to_mark;\n");
							}
						}

						GenerationState = GenerationStateType.Custom;
						GenerationIndex = 0;
					}
				}

				Msg("Finding light intensity...DONE\n");

				GenerationState = GenerationStateType.Custom;
				GenerationIndex = 0;
				return true;
			case GenerationStateType.Custom:
				if (GenerationIndex == 0) {
					BeginCustomAnalysis(GenerationMode == GenerationModeType.Incremental);
					Msg("Start custom...\n");
				}

				while (GenerationIndex < NavArea.TheNavAreas.Count) {
					NavArea area = NavArea.TheNavAreas[GenerationIndex];
					++GenerationIndex;

					area.CustomAnalysis(GenerationMode == GenerationModeType.Incremental);

					if (Platform.Time - startTime > maxTime) {
						AnalysisProgress("Custom game-specific analysis...", 100, 100 * GenerationIndex / NavArea.TheNavAreas.Count);
						return true;
					}
				}

				Msg("Post custom...\n");
				PostCustomAnalysis();
				EndCustomAnalysis();

				Msg("Custom game-specific analysis...DONE\n");

				GenerationState = GenerationStateType.SaveNavMesh;
				GenerationIndex = 0;

				ConVarRef mat_queue_mode = new("mat_queue_mode");
				mat_queue_mode.SetValue(-1);
				host_thread_mode.SetValue(HostThreatModeRestoreValue);
				return true;
			case GenerationStateType.SaveNavMesh:
				if (GenerationMode == GenerationModeType.AnalysisOnly || GenerationMode == GenerationModeType.Full)
					bIsAnalyzed = true;

				float generationTime = (float)(Platform.Time - GenerationStartTime);
				Msg($"Generation complete! {generationTime:F1} seconds elapsed.\n");

				bool restart = GenerationMode != GenerationModeType.Incremental;
				GenerationMode = GenerationModeType.None;
				bIsLoaded = true;

				ClearWalkableSeeds();
				HideAnalysisProgress();

				if (Save())
					Msg($"Navigation map '{GetFilename()}' saved.\n");
				else {
					ReadOnlySpan<char> filename = GetFilename();
					Msg($"ERROR: Cannot save navigation map '{(filename.IsEmpty ? "" : filename)}'.\n");
				}

				if (QuitWhenFinished)
					engine.ServerCommand("quit\n");
				else if (restart)
					engine.ChangeLevel(gpGlobals.MapName, null);
				else {
					foreach (NavArea area in NavArea.TheNavAreas)
						area.ResetNodes();
				}
				return false;
		}

		return false;
	}

	public virtual void BeginCustomAnalysis(bool incremental) { }
	public virtual void PostCustomAnalysis() { }
	public virtual void EndCustomAnalysis() { }

	static void AnalysisProgress(ReadOnlySpan<char> msg, int ticks, int current, bool showPercent = true) {
		const double MsgInterval = 10.0f;
		double now = Platform.Time;
		if (now > LastMsgTime + MsgInterval) {
			if (showPercent && ticks != 0)
				Msg($"{msg} {current * 100.0f / ticks:0}%\n");
			else
				Msg($"{msg}\n");

			LastMsgTime = now;
		}

		KeyValues data = new("data");
		data.SetString("msg", msg);
		data.SetInt("total", ticks);
		data.SetInt("current", current);

		ShowViewPortPanelToAll("nav_progress", true, data);
	}

	static void HideAnalysisProgress() => ShowViewPortPanelToAll("nav_progress", false, null);

	static void ShowViewPortPanelToAll(ReadOnlySpan<char> panelName, bool show, KeyValues? data) {
		RecipientFilter filter = new();
		filter.AddAllPlayers();
		filter.MakeReliable();

		int count = 0;
		KeyValues? subkey = null;

		if (data != null) {
			subkey = data.GetFirstSubKey();
			while (subkey != null) {
				count++;
				subkey = subkey.GetNextKey();
			}
			subkey = data.GetFirstSubKey();
		}

		UserMessageBegin(filter, "VGUIMenu");
		MessageWriteString(panelName);
		MessageWriteByte(show ? 1 : 0);
		MessageWriteByte(count);
		while (subkey != null) {
			MessageWriteString(subkey.Name);
			MessageWriteString(subkey.GetString());
			subkey = subkey.GetNextKey();
		}
		MessageEnd();
	}

	void SetPlayerSpawnName(ReadOnlySpan<char> name) => SpawnName = name.ToString();

	ReadOnlySpan<char> GetPlayerSpawnName() => SpawnName ?? "info_player_start";

	NavNode AddNode(Vector3 destPos, Vector3 normal, NavDirType dir, NavNode source, bool isOnDisplacement, float obstacleHeight, float obstacleStartDist, float obstacleEndDist) {
		NavNode? node = NavNode.GetNode(destPos);

		bool useNew = false;
		if (node == null) {
			node = new(destPos, normal, source, isOnDisplacement);
			useNew = true;
		}

		source.ConnectTo(node, dir, obstacleHeight, obstacleStartDist, obstacleEndDist);

		const float zTolerance = 50.0f;
		float deltaZ = source.GetPosition().Z - destPos.Z;
		if (Math.Abs(deltaZ) < zTolerance) {
			if (obstacleHeight > 0)
				obstacleHeight = Math.Max(obstacleHeight + deltaZ, 0);

			node.ConnectTo(source, OppositeDirection(dir), obstacleHeight, GenerationStepSize - obstacleEndDist, GenerationStepSize - obstacleStartDist);
			node.MarkAsVisited(OppositeDirection(dir));
		}

		if (useNew)
			CurrentNode = node;

		node.CheckCrouch();

		for (int i = 0; i < (int)NavDirType.NumDirections; i++) {
			NavDirType checkDir = (NavDirType)i;
			if (CheckCliff(node.GetPosition(), checkDir))
				node.SetAttributes(node.GetAttributes() | NavAttributeType.Cliff);
		}

		return node;
	}

	NavNode LadderEndSearch(Vector3 pos, NavDirType mountDir) {
		Vector3 center = pos;
		AddDirectionVector(ref center, mountDir, HalfHumanWidth);

		for (int d = -1; d < 2 * (int)NavDirType.NumDirections; ++d) {
			Vector3 tryPos = center;

			if (d >= (int)NavDirType.NumDirections)
				AddDirectionVector(ref tryPos, (NavDirType)(d - (int)NavDirType.NumDirections), 2.0f * GenerationStepSize);
			else if (d >= 0)
				AddDirectionVector(ref tryPos, (NavDirType)d, GenerationStepSize);

			tryPos.Z += GenerationStepSize;

			tryPos.X = SnapToGrid(tryPos.X);
			tryPos.Y = SnapToGrid(tryPos.Y);

			if (!GetGroundHeight(tryPos, out tryPos.Z, out _))
				continue;

			TraceFilterWalkableEntities filter = new(null, CollisionGroup.None, WalkThruFlags.Everything);
			Util.TraceHull(center + new Vector3(0, 0, 4), tryPos + new Vector3(0, 0, 4), NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out Trace trace);
			if (trace.Fraction != 1.0f || trace.StartSolid)
				continue;

			NavNode? node = NavNode.GetNode(tryPos);
			if (node == null)
				return new NavNode(tryPos, trace.Plane.Normal, null, false);
		}

		return null;
	}

	bool FindGroundForNode(ref Vector3 pos, out Vector3 normal) {
		TraceFilterWalkableEntities filter = new(null, CollisionGroup.PlayerMovement, WalkThruFlags.Everything);

		Vector3 start = new(pos.X, pos.Y, pos.Z + VEC_DUCK_HULL_MAX.Z - 0.1f);
		Vector3 end = new(pos.X, pos.Y, pos.Z - DeathDrop);

		Util.TraceHull(start, end, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out Trace tr);

		pos = tr.EndPos;
		normal = tr.Plane.Normal;

		return !tr.AllSolid;
	}

	public bool StayOnFloor(ref Trace trace, float zLimit = DeathDrop) {
		Vector3 start = trace.EndPos;
		Vector3 end = start;
		end.Z -= zLimit;

		TraceFilterWalkableEntities filter = new(null, CollisionGroup.None, WalkThruFlags.Everything);
		Util.TraceHull(start, end, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out trace);

		if (trace.StartSolid || trace.Fraction >= 1.0f)
			return false;

		if (trace.Plane.Normal.Z < nav_slope_limit.GetFloat())
			return false;

		return true;
	}

	public bool TraceAdjacentNode(int depth, Vector3 start, Vector3 end, out Trace trace, float zLimit = DeathDrop) {
		const float MinDistance = 1.0f;

		TraceFilterWalkableEntities filter = new(null, CollisionGroup.None, WalkThruFlags.Everything);
		Util.TraceHull(start, end, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out trace);

		if (trace.StartSolid)
			return false;

		if (end.X == trace.EndPos.X && end.Y == trace.EndPos.Y)
			return StayOnFloor(ref trace, zLimit);

		if (depth > 0 && new Vector2(trace.EndPos.X - start.X, trace.EndPos.Y - start.Y).LengthSquared() < (MinDistance * MinDistance))
			return false;

		if (!StayOnFloor(ref trace, zLimit))
			return false;

		Vector3 testStart = trace.EndPos;
		Vector3 testEnd = testStart;
		testEnd.Z += StepHeight;

		if (!TraceAdjacentNode(depth + 1, testStart, testEnd, out trace))
			return false;

		Vector3 forwardTestStart = trace.EndPos;
		Vector3 forwardTestEnd = end;
		forwardTestEnd.Z = forwardTestStart.Z;

		return TraceAdjacentNode(depth + 1, forwardTestStart, forwardTestEnd, out trace);
	}

	public bool IsNodeOverlapped(Vector3 pos, Vector3 offset) {
		bool overlap = GetNavArea(pos + offset, HumanHeight) != null;
		if (!overlap) {
			Vector3 mins = new(-0.5f, -0.5f, -0.5f);
			Vector3 maxs = new(0.5f, 0.5f, 0.5f);

			Vector3 start = pos;
			start.Z += HalfHumanHeight;
			Vector3 end = start;
			end.X += offset.X * GenerationStepSize;
			end.Y += offset.Y * GenerationStepSize;

			TraceFilterWalkableEntities filter = new(null, CollisionGroup.None, WalkThruFlags.Everything);
			Util.TraceHull(start, end, mins, maxs, GetGenerationTraceMask(), ref filter, out Trace trace);

			if (trace.StartSolid || trace.AllSolid)
				return true;

			if (trace.Fraction < 0.1f)
				return true;

			start = trace.EndPos;
			end = start;
			end.Z -= HalfHumanHeight * 2;

			Util.TraceHull(start, end, mins, maxs, GetGenerationTraceMask(), ref filter, out trace);

			if (trace.StartSolid || trace.AllSolid)
				return true;

			if (trace.Fraction == 1.0f)
				return true;

			if (trace.Plane.Normal.Z < 0.7f)
				return true;
		}
		return overlap;
	}

	bool SampleStep() {
		while (true) {
			if (CurrentNode == null) {
				CurrentNode = GetNextWalkableSeedNode();

				if (CurrentNode == null) {
					if (GenerationMode == GenerationModeType.Incremental || GenerationMode == GenerationModeType.Simplify)
						return false;

					for (int i = 0; i < Ladders.Count; ++i) {
						NavLadder ladder = Ladders[i];

						if ((CurrentNode = LadderEndSearch(ladder.Bottom, ladder.GetDir())) != null)
							break;

						if ((CurrentNode = LadderEndSearch(ladder.Top, ladder.GetDir())) != null)
							break;
					}

					if (CurrentNode == null) {
						return false;
					}
				}
			}

			for (NavDirType dir = NavDirType.North; dir < NavDirType.NumDirections; dir++) {
				if (!CurrentNode.HasVisited(dir)) {
					Vector3 pos = CurrentNode.GetPosition();

					int cx = (int)SnapToGrid(pos.X);
					int cy = (int)SnapToGrid(pos.Y);

					switch (dir) {
						case NavDirType.North: cy -= (int)GenerationStepSize; break;
						case NavDirType.South: cy += (int)GenerationStepSize; break;
						case NavDirType.East: cx += (int)GenerationStepSize; break;
						case NavDirType.West: cx -= (int)GenerationStepSize; break;
					}

					pos.X = cx;
					pos.Y = cy;

					GenerationDir = dir;

					CurrentNode.MarkAsVisited(GenerationDir);

					float incrementalRange = nav_generate_incremental_range.GetFloat();
					if (GenerationMode == GenerationModeType.Incremental && incrementalRange > 0) {
						bool inRange = false;
						for (int i = 0; i < WalkableSeeds.Count; ++i) {
							Vector3 seedPos = WalkableSeeds[i].Pos;
							if ((seedPos - pos).Length() < incrementalRange) {
								inRange = true;
								break;
							}
						}

						if (!inRange)
							return true;
					}

					if (GenerationMode == GenerationModeType.Simplify) {
						if (!SimplifyGenerationExtent.Contains(pos))
							return true;
					}

					Vector3 from = CurrentNode.GetPosition();
					TraceFilterWalkableEntities filter = new(null, CollisionGroup.None, WalkThruFlags.Everything);
					Vector3 to = new(0, 0, 0);
					Vector3 toNormal = new(0, 0, 0);
					float obstacleHeight = 0, obstacleStartDist = 0, obstacleEndDist = GenerationStepSize;
					if (TraceAdjacentNode(0, from, pos, out Trace result)) {
						to = result.EndPos;
						toNormal = result.Plane.Normal;
					}
					else {
						bool success = false;
						for (float height = StepHeight; height <= ClimbUpHeight; height += 1.0f) {
							Vector3 start = from;
							Vector3 end = pos;
							start.Z += height;
							end.Z += height;
							Util.TraceHull(start, end, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out Trace tr);
							if (!tr.StartSolid && tr.Fraction == 1.0f) {
								if (!StayOnFloor(ref tr))
									break;

								to = tr.EndPos;
								toNormal = tr.Plane.Normal;

								start = end = from;
								end.Z += height;
								Util.TraceHull(start, end, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out tr);
								if (tr.Fraction < 1.0f)
									break;

								obstacleHeight = height;
								success = true;
								break;
							}
							else {
								Vector3 vecToObstacleStart = tr.EndPos - start;
								Assert(vecToObstacleStart.LengthSqr() <= (GenerationStepSize * GenerationStepSize));
								if (vecToObstacleStart.LengthSqr() <= (GenerationStepSize * GenerationStepSize)) {
									Util.TraceHull(end, start, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out tr);
									if (!tr.StartSolid && tr.Fraction < 1.0) {
										Vector3 vecToObstacleEnd = tr.EndPos - start;
										Assert(vecToObstacleEnd.LengthSqr() <= (GenerationStepSize * GenerationStepSize));
										if (vecToObstacleEnd.LengthSqr() <= (GenerationStepSize * GenerationStepSize)) {
											obstacleStartDist = vecToObstacleStart.Length();
											obstacleEndDist = vecToObstacleEnd.Length();
											if (obstacleEndDist == 0)
												obstacleEndDist = GenerationStepSize;
										}
									}
								}
							}
						}

						if (!success)
							return true;
					}

					if ((result.Surface.Flags & (ushort)(Surf.Sky | Surf.Sky2D)) != 0)
						return true;

					Vector3 testPos = to;
					bool overlapSE = IsNodeOverlapped(testPos, new Vector3(1, 1, HalfHumanHeight));
					bool overlapSW = IsNodeOverlapped(testPos, new Vector3(-1, 1, HalfHumanHeight));
					bool overlapNE = IsNodeOverlapped(testPos, new Vector3(1, -1, HalfHumanHeight));
					bool overlapNW = IsNodeOverlapped(testPos, new Vector3(-1, -1, HalfHumanHeight));
					if (overlapSE && overlapSW && overlapNE && overlapNW && GenerationMode != GenerationModeType.Simplify)
						return true;

					int tolerance = nav_generate_incremental_tolerance.GetInt();
					if (tolerance > 0 && GenerationMode == GenerationModeType.Incremental) {
						bool valid = false;
						int zPos = (int)to.Z;
						for (int i = 0; i < WalkableSeeds.Count; ++i) {
							Vector3 seedPos = WalkableSeeds[i].Pos;
							int zMin = (int)(seedPos.Z - tolerance);
							int zMax = (int)(seedPos.Z + tolerance);

							if (zPos >= zMin && zPos <= zMax) {
								valid = true;
								break;
							}
						}

						if (!valid)
							return true;
					}


					bool isOnDisplacement = result.IsDispSurface();

					if (nav_displacement_test.GetInt() > 0) {
						Vector3 start = to + new Vector3(0, 0, 0);
						Vector3 end = start + new Vector3(0, 0, nav_displacement_test.GetInt());
						Util.TraceHull(start, end, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out result);

						if (result.Fraction > 0) {
							end = start;
							start = result.EndPos;
							Util.TraceHull(start, end, NavTraceMins, NavTraceMaxs, GetGenerationTraceMask(), ref filter, out result);
							if (result.Fraction < 1) {
								if (result.EndPos.Z > to.Z + StepHeight)
									return true;
							}
						}
					}

					float deltaZ = to.Z - CurrentNode.GetPosition().Z;
					if ((obstacleHeight < StepHeight) || (deltaZ > (obstacleHeight - 2.0f))) {
						obstacleHeight = 0;
						obstacleStartDist = 0;
						obstacleEndDist = GenerationStepSize;
					}

					AddNode(to, toNormal, GenerationDir, CurrentNode, isOnDisplacement, obstacleHeight, obstacleStartDist, obstacleEndDist);

					return true;
				}
			}

			CurrentNode = CurrentNode.GetParent();
		}
	}

	void AddWalkableSeed(Vector3 pos, Vector3 normal) {
		WalkableSeedSpot seed = new();
		seed.Pos.X = RoundToUnits(pos.X, GenerationStepSize);
		seed.Pos.Y = RoundToUnits(pos.Y, GenerationStepSize);
		seed.Pos.Z = pos.Z;
		seed.Normal = normal;

		WalkableSeeds.Add(seed);
	}

	NavNode? GetNextWalkableSeedNode() {
		if (SeedIdx >= WalkableSeeds.Count)
			return null;

		WalkableSeedSpot seed = WalkableSeeds[SeedIdx];
		++SeedIdx;

		NavNode? node = NavNode.GetNode(seed.Pos);
		if (node != null)
			return null;

		return new NavNode(seed.Pos, seed.Normal, null, false);
	}

	void CommandNavSubdivide(in TokenizedCommand args) { }

	[ConCommand("nav_subdivide", "Subdivides all selected areas.", FCvar.Cheat | FCvar.GameDLL)]
	static void nav_subdivide(in TokenizedCommand args) {
		if (!Util.IsCommandIssuedByServerAdmin())
			return;

		Instance!.CommandNavSubdivide(args);
	}

	void ValidateNavAreaConnections() {
		NavConnect connect = new();

		for (int it = 0; it < NavArea.TheNavAreas.Count; it++) {
			NavArea area = NavArea.TheNavAreas[it];

			for (NavDirType dir = NavDirType.North; dir < NavDirType.NumDirections; dir = (NavDirType)(((int)dir) + 1)) {
				List<NavConnect> outgoing = area.GetAdjacentAreas(dir);
				List<NavConnect> incoming = area.GetIncomingConnections(dir);

				for (int con = 0; con < outgoing.Count; con++) {
					NavArea areaOther = outgoing[con].Area!;
					connect.Area = areaOther;
					if (incoming.Contains(connect)) {
						Msg($"Area {area.GetID()} has area {areaOther.GetID()} on both 2-way and incoming list, should only be on one\n");
						Assert(false);
					}

					for (int connectCheck = con + 1; connectCheck < outgoing.Count; connectCheck++) {
						NavArea areaCheck = outgoing[connectCheck].Area!;
						if (areaOther == areaCheck) {
							Msg($"Area {area.GetID()} has multiple outgoing connections to area {areaOther.GetID()} in direction {dir}\n");
							Assert(false);
						}
					}

					List<NavConnect> outgoingOther = areaOther.GetAdjacentAreas(OppositeDirection(dir));
					List<NavConnect> incomingOther = areaOther.GetIncomingConnections(OppositeDirection(dir));

					connect.Area = area;
					if (!outgoingOther.Contains(connect)) {
						connect.Area = area;
						if (!incomingOther.Contains(connect))
							Msg($"Area {area.GetID()} has one-way connect to area {areaOther.GetID()} but does not appear on the latter's incoming list\n");
					}
				}

				for (int con = 0; con < incoming.Count; con++) {
					NavArea areaOther = incoming[con].Area!;

					for (int connectCheck = con + 1; connectCheck < incoming.Count; connectCheck++) {
						NavArea areaCheck = incoming[connectCheck].Area!;
						if (areaOther == areaCheck) {
							Msg("Area %d has multiple incoming connections to area %d in direction %d\n", area.GetID(), areaOther.GetID(), dir);
							Assert(false);
						}
					}

					List<NavConnect> outgoingOther = areaOther.GetAdjacentAreas(OppositeDirection(dir));
					connect.Area = area;
					if (!outgoingOther.Contains(connect)) {
						Msg($"Area {area.GetID()} has incoming connection from area {areaOther.GetID()} but does not appear on latter's outgoing connection list\n");
						Assert(false);
					}
				}
			}
		}
	}

	void PostProcessCliffAreas() {
		foreach (NavArea area in NavArea.TheNavAreas) {
			if ((area.GetAttributes() & NavAttributeType.Cliff) != 0)
				continue;

			for (NavDirType dir = NavDirType.North; dir < NavDirType.NumDirections; dir++) {
				bool hasCliff = false;
				NavCornerType[] corner = [(NavCornerType)dir, (NavCornerType)(((int)dir + 1) % (int)NavCornerType.NumCorners)];

				foreach (NavCornerType c in corner) {
					Vector3 cornerPos = area.GetCorner(c);
					if (CheckCliff(cornerPos, dir)) {
						hasCliff = true;
						break;
					}
				}

				if (hasCliff) {
					area.SetAttributes(area.GetAttributes() | NavAttributeType.Cliff);
					break;
				}
			}
		}
	}

	[ConCommand("nav_gen_cliffs_approx", "Mark cliff areas, post-processing approximation", FCvar.Cheat)]
	static void nav_gen_cliffs_approx() {
		if (!Util.IsCommandIssuedByServerAdmin())
			return;

		Instance!.PostProcessCliffAreas();
	}
}

enum StairTestType
{
	No,
	Yes,
	Maybe
}

public partial class NavArea
{
	StairTestType IsStairs(Vector3 start, Vector3 end, StairTestType ret) {
		if (ret == StairTestType.No)
			return ret;

		const float inc = 5.0f;

		float minStepZ = inc * MathF.Tan(MathF.Acos(nav_slope_limit.GetFloat()));
		const float MinStairNormal = 0.97f;

		float t;
		Vector3 pos, normal;
		float height, priorHeight;

		float length = new Vector2(start.X - end.X, start.Y - end.Y).Length();

		TraceFilterWalkableEntities filter = new(null, CollisionGroup.PlayerMovement, WalkThruFlags.Everything);
		Vector3 hullMins = new(-inc / 2, -inc / 2, 0);
		Vector3 hullMaxs = new(inc / 2, inc / 2, 0);
		hullMaxs.Z = 1;

		if (Math.Abs(start.X - end.X) > Math.Abs(start.Y - end.Y)) {
			hullMins.X = -8;
			hullMaxs.X = 8;
		}
		else {
			hullMins.Y = -8;
			hullMaxs.Y = 8;
		}

		Vector3 traceOffset = new(0, 0, VEC_DUCK_HULL_MAX.Z);

		if (Math.Abs(start.Z - end.Z) > StepHeight) {
			Util.TraceHull(start + traceOffset, start - traceOffset, hullMins, hullMaxs, Mask.SolidBrushOnly, ref filter, out Trace trace);
			if (trace.StartSolid || trace.IsDispSurface())
				return StairTestType.No;
			priorHeight = trace.EndPos.Z;

			Vector3 prevGround = start;
			prevGround.Z = priorHeight;

			float traceIncrement = inc / length;
			for (t = 0.0f; t <= 1.0f; t += traceIncrement) {
				pos = start + t * (end - start);

				Util.TraceHull(pos + traceOffset, pos - traceOffset, hullMins, hullMaxs, Mask.SolidBrushOnly, ref filter, out trace);
				if (trace.StartSolid || trace.IsDispSurface())
					return StairTestType.No;

				height = trace.EndPos.Z;
				normal = trace.Plane.Normal;

				Vector3 ground = pos;
				ground.Z = height;

				if (t == 0.0f && Math.Abs(height - start.Z) > StepHeight)
					return StairTestType.No;

				if (t == 1.0f && Math.Abs(height - end.Z) > StepHeight)
					return StairTestType.No;

				if (normal.Z < MinStairNormal)
					return StairTestType.No;

				float deltaZ = Math.Abs(height - priorHeight);

				if (deltaZ >= minStepZ && deltaZ <= StepHeight)
					ret = StairTestType.Yes;
				else if (deltaZ > StepHeight)
					return StairTestType.No;

				prevGround = pos;
				prevGround.Z = height;
				priorHeight = height;
			}
		}

		return ret;
	}

	public bool TestStairs() {
		SetAttributes(GetAttributes() & ~NavAttributeType.Stairs);

		if (GetSizeX() <= GenerationStepSize && GetSizeY() <= GenerationStepSize)
			return false;

		const float MatchingNormalDot = 0.95f;
		Vector3 firstNormal = default, secondNormal = default;
		ComputeNormal(ref firstNormal);
		ComputeNormal(ref secondNormal, true);
		if (firstNormal.Dot(secondNormal) < MatchingNormalDot) {
			return false;
		}

		StairTestType ret = StairTestType.Maybe;
		Vector3 from, to;

		const float inset = 5.0f;

		from = GetCorner(NavCornerType.NorthWest) + new Vector3(inset, inset, 0);
		to = GetCorner(NavCornerType.NorthEast) + new Vector3(-inset, inset, 0);
		ret = IsStairs(from, to, ret);

		from = GetCorner(NavCornerType.SouthWest) + new Vector3(inset, -inset, 0);
		to = GetCorner(NavCornerType.SouthEast) + new Vector3(-inset, -inset, 0);
		ret = IsStairs(from, to, ret);

		from = GetCorner(NavCornerType.NorthWest) + new Vector3(inset, inset, 0);
		to = GetCorner(NavCornerType.SouthWest) + new Vector3(inset, -inset, 0);
		ret = IsStairs(from, to, ret);

		from = GetCorner(NavCornerType.NorthEast) + new Vector3(-inset, inset, 0);
		to = GetCorner(NavCornerType.SouthEast) + new Vector3(-inset, -inset, 0);
		ret = IsStairs(from, to, ret);

		from = (GetCorner(NavCornerType.NorthWest) + GetCorner(NavCornerType.NorthEast)) / 2.0f + new Vector3(0, inset, 0);
		to = (GetCorner(NavCornerType.SouthWest) + GetCorner(NavCornerType.SouthEast)) / 2.0f + new Vector3(0, -inset, 0);
		ret = IsStairs(from, to, ret);

		from = (GetCorner(NavCornerType.NorthEast) + GetCorner(NavCornerType.SouthEast)) / 2.0f + new Vector3(-inset, 0, 0);
		to = (GetCorner(NavCornerType.NorthWest) + GetCorner(NavCornerType.SouthWest)) / 2.0f + new Vector3(inset, 0, 0);
		ret = IsStairs(from, to, ret);

		if (ret == StairTestType.Yes) {
			SetAttributes(NavAttributeType.Stairs);
			return true;
		}

		return false;
	}

	public bool IsAbleToMergeWith(NavArea other) {
		if (!HasNodes() || (GetAttributes() & NavAttributeType.NoMerge) != 0)
			return false;

		if (!other.HasNodes() || (other.GetAttributes() & NavAttributeType.NoMerge) != 0)
			return false;

		return true;
	}

	[ConCommand("nav_test_stairs", "Test the selected set for being on stairs", FCvar.Cheat)]
	static void nav_test_stairs(in TokenizedCommand args) {
		throw new NotImplementedException();
	}
}

public partial class NavLadder
{
	public void ConnectGeneratedLadder(float maxHeightAboveTopArea) {
		throw new NotImplementedException();
	}
}