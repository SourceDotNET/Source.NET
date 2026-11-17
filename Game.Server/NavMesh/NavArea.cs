using static Game.Server.NavMesh.Nav;
using static Game.Server.NavMesh.NavColors;

namespace Game.Server.NavMesh;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Game.Server.NextBot;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Hashing;
using Source.Common.Mathematics;

public class FuncElevator;

public struct NavConnect() : IEquatable<NavConnect>
{
	public uint ID = 0;
	public NavArea? Area;
	public float Length = -1;
	public readonly bool Equals(NavConnect other) => Area == other.Area;
	public override readonly bool Equals(object? obj) => obj is NavConnect other && Equals(other);
	public override readonly int GetHashCode() => Area?.GetHashCode() ?? 0;
}

public struct NavLadderConnect() : IEquatable<NavLadderConnect>
{
	public uint ID;
	public NavLadder? Ladder;
	public readonly bool Equals(NavLadderConnect other) => Ladder == other.Ladder;
	public override readonly bool Equals(object? obj) => obj is NavLadderConnect other && Equals(other);
	public override readonly int GetHashCode() => Ladder?.GetHashCode() ?? 0;
}

public struct SpotOrder
{
	public float T;
	public HidingSpot? Spot;
	public uint ID;
}

struct SpotEncounter()
{
	public NavConnect From;
	public NavDirType FromDir;
	public NavConnect To;
	public NavDirType ToDir;
	public Ray Path;
	public List<SpotOrder> Spots = [];
}

class SplitNotification(NavArea originalArea, NavArea alphaArea, NavArea betaArea)
{
	private NavArea OriginalArea = originalArea;
	private NavArea AlphaArea = alphaArea;
	private NavArea BetaArea = betaArea;
	public bool Invoke(NavLadder ladder) {
		ladder.OnSplit(OriginalArea, AlphaArea, BetaArea);
		return true;
	}
}

class OverlapCheck(NavArea me, Vector3 pos)
{
	NavArea Me = me;
	float MyZ = me.GetZ(pos);
	Vector3 Pos = pos;

	public bool Invoke(NavArea area) {
		if (area == Me)
			return true;

		if (!area.IsOverlapping(Pos))
			return true;

		float theirZ = area.GetZ(Pos);
		if (theirZ > Pos.Z)
			return true;

		if (theirZ > MyZ)
			return false;

		return true;
	}
}

class LadderConnectionReplacement(NavArea originalArea, NavArea replacementArea)
{
	NavArea OriginalArea = originalArea;
	NavArea ReplacementArea = replacementArea;

	public bool Invoke(NavLadder ladder) {
		if (ladder.TopForwardArea == OriginalArea)
			ladder.TopForwardArea = ReplacementArea;

		if (ladder.TopRightArea == OriginalArea)
			ladder.TopRightArea = ReplacementArea;

		if (ladder.TopLeftArea == OriginalArea)
			ladder.TopLeftArea = ReplacementArea;

		if (ladder.TopBehindArea == OriginalArea)
			ladder.TopBehindArea = ReplacementArea;

		if (ladder.BottomArea == OriginalArea)
			ladder.BottomArea = ReplacementArea;

		return true;
	}
}

public class NavAreaCriticalData
{
	public Vector3 NWCorner;
	public Vector3 SECorner;
	public float InvDXCorners;
	public float InvDYCorners;
	public float NEZ;
	public float SWZ;
	public Vector3 Center;

	public byte[] PlayerCount = new byte[MAX_NAV_TEAMS];
	public bool[] _IsBlocked = new bool[MAX_NAV_TEAMS];

	public uint Marker;
	public float TotalCost;
	public float CostSoFar;

	public NavArea? NextOpen, PrevOpen;
	public uint OpenMarker;
	public NavAttributeType AttributeFlags;

	public readonly List<NavConnect>[] Connect = new List<NavConnect>[(int)NavDirType.NumDirections];
	public readonly List<NavLadderConnect>[] Ladder = new List<NavLadderConnect>[(int)NavLadder.LadderDirectionType.NumLadderDirections];
	public readonly List<NavConnect> ElevatorAreas = [];

	public uint NearNavSearchMarker;

	public NavArea? Parent;
	public NavTraverseType ParentHow;

	public float PathLengthSoFar;

	public FuncElevator? Elevator;
}

public partial class NavArea : NavAreaCriticalData
{
	public static List<NavArea> TheNavAreas = [];

	public struct AreaBindInfo
	{
		public NavArea? Area;
		public uint ID;
		public byte Attributes;

		public readonly bool Equals(AreaBindInfo other) => Area == other.Area;
	}

	public static bool IsReset;
	public static uint NextID;
	uint ID;
	uint DebugID;
	NavPlace Place;
	CountdownTimer BlockedTimer;
	public bool IsUnderwater;
	bool IsBattlefront;
	float AvoidanceObstacleHeight;
	CountdownTimer AvoidanceObstacleTimer;
	readonly TimeUnit_t[] ClearedTimestamp = new TimeUnit_t[MAX_NAV_TEAMS];
	readonly float[] Danger = new float[MAX_NAV_TEAMS];
	readonly TimeUnit_t[] DangerTimestamp = new TimeUnit_t[MAX_NAV_TEAMS];
	public readonly List<HidingSpot> HidingSpots = [];
	readonly List<SpotEncounter> SpotEncounters = [];
	readonly float[] EarliestOccupyTime = new float[MAX_NAV_TEAMS];
	readonly float[] LightIntensity = new float[(int)NavCornerType.NumCorners];
	static uint MasterMarker;
	static NavArea? OpenList;
	static NavArea? OpenListTail;
	readonly List<NavConnect>[] IncomingConnect = new List<NavConnect>[(int)NavDirType.NumDirections];
	public readonly NavNode?[] Node = new NavNode[(int)NavCornerType.NumCorners];
	readonly List<Handle<FuncNavPrerequisite>> PrerequisiteVector = [];
	public NavArea? PrevHash, NextHash;
	int DamagingTickCount;
	public AreaBindInfo InheritVisibilityFrom;
	public List<AreaBindInfo> PotentiallyVisibleAreas = [];
	public bool IsInheritedFrom;
	UInt32 VisTestCounter;
	static UInt32 CurrVisTestCounter;
	List<FuncNavCost> FuncNavCostVector;

	static Color SelectedSetColor = new(255, 255, 200, 96);
	static Color SelectedSetBorderColor = new(100, 100, 0, 255);
	static Color DragSelectionSetBorderColor = new(50, 50, 50, 255);

	static ConVar nav_selected_set_color = new("255 255 200 96", FCvar.Cheat, "Color used to draw the selected set background while editing.", callback: SelectedSetColorChaged);
	static ConVar nav_selected_set_border_color = new("100 100 0 255", FCvar.Cheat, "Color used to draw the selected set borders while editing.", callback: SelectedSetColorChaged);

	static void SelectedSetColorChaged(IConVar var, in ConVarChangeContext ctx) {
		ConVarRef colorVar = new(var.GetName());

		ref Color color = ref (FStrEq(var.GetName(), "nav_selected_set_border_color") ? ref SelectedSetBorderColor : ref SelectedSetColor);

		int r = color.R;
		int g = color.G;
		int b = color.B;
		int a = color.A;
		ScanF scan = new(colorVar.GetString(), "%d %d %d %d");
		scan.Read(out r).Read(out g).Read(out b).Read(out a);

		color[0] = (byte)r;
		color[1] = (byte)g;
		color[2] = (byte)b;
		if (scan.ReadArguments > 3)
			color[3] = (byte)a;
	}

	public static void CompressIDs() {
		NextID = 1;

		foreach (NavArea area in TheNavAreas) {
			area.ID = NextID++;
			NavMesh.Instance!.RemoveNavArea(area);
			NavMesh.Instance.AddNavArea(area);
		}
	}

	public uint GetDebugID() => DebugID;

	public NavArea() {
		Marker = 0;
		NearNavSearchMarker = 0;
		DamagingTickCount = 0;
		OpenMarker = 0;

		Parent = null;
		ParentHow = NavTraverseType.North;
		AttributeFlags = 0;
		Place = NavMesh.Instance!.GetNavPlace();
		IsUnderwater = false;
		AvoidanceObstacleHeight = 0.0f;

		TotalCost = 0.0f;
		CostSoFar = 0.0f;
		PathLengthSoFar = 0.0f;

		ResetNodes();

		for (int i = 0; i < MAX_NAV_TEAMS; i++) {
			_IsBlocked[i] = false;
			Danger[i] = 0.0f;
			DangerTimestamp[i] = 0.0f;
			ClearedTimestamp[i] = 0.0f;
			EarliestOccupyTime[i] = 0.0f;
			PlayerCount[i] = 0;
		}

		for (int i = 0; i < IncomingConnect.Length; i++) IncomingConnect[i] = [];

		ID = NextID++;
		DebugID = ID;

		PrevHash = null;
		NextHash = null;

		IsBattlefront = false;

		for (int i = 0; i < (int)NavDirType.NumDirections; i++)
			Connect[i] = [];

		for (int i = 0; i < (int)NavLadder.LadderDirectionType.NumLadderDirections; i++)
			Ladder[i] = [];

		for (int i = 0; i < (int)NavCornerType.NumCorners; i++)
			LightIntensity[i] = 1.0f;

		Elevator = null;

		InvDXCorners = 0;
		InvDYCorners = 0;

		InheritVisibilityFrom.Area = null;
		IsInheritedFrom = false;

		FuncNavCostVector = [];

		VisTestCounter = UInt32.MaxValue - 1;

		BlockedTimer = new();
		AvoidanceObstacleTimer = new();
	}

	public void Build(Vector3 corner, Vector3 otherCorner) {
		if (corner.X < otherCorner.X) {
			NWCorner.X = corner.X;
			SECorner.X = otherCorner.X;
		}
		else {
			SECorner.X = corner.X;
			NWCorner.X = otherCorner.X;
		}

		if (corner.Y < otherCorner.Y) {
			NWCorner.Y = corner.Y;
			SECorner.Y = otherCorner.Y;
		}
		else {
			SECorner.Y = corner.Y;
			NWCorner.Y = otherCorner.Y;
		}

		NWCorner.Z = corner.Z;
		SECorner.Z = corner.Z;

		Center.X = (NWCorner.X + SECorner.X) / 2.0f;
		Center.Y = (NWCorner.Y + SECorner.Y) / 2.0f;
		Center.Z = (NWCorner.Z + SECorner.Z) / 2.0f;

		if ((SECorner.X - NWCorner.X) > 0.0f && (SECorner.Y - NWCorner.Y) > 0.0f) {
			InvDXCorners = 1.0f / (SECorner.X - NWCorner.X);
			InvDYCorners = 1.0f / (SECorner.Y - NWCorner.Y);
		}
		else
			InvDXCorners = InvDYCorners = 0;

		NEZ = corner.Z;
		SWZ = otherCorner.Z;

		CalcDebugID();
	}

	public void Build(Vector3 nwCorner, Vector3 neCorner, Vector3 seCorner, Vector3 swCorner) {
		NWCorner = nwCorner;
		SECorner = seCorner;

		Center.X = (NWCorner.X + SECorner.X) / 2.0f;
		Center.Y = (NWCorner.Y + SECorner.Y) / 2.0f;
		Center.Z = (NWCorner.Z + SECorner.Z) / 2.0f;

		NEZ = neCorner.Z;
		SWZ = swCorner.Z;

		if ((SECorner.X - NWCorner.X) > 0.0f && (SECorner.Y - NWCorner.Y) > 0.0f) {
			InvDXCorners = 1.0f / (SECorner.X - NWCorner.X);
			InvDYCorners = 1.0f / (SECorner.Y - NWCorner.Y);
		}
		else {
			InvDXCorners = InvDYCorners = 0;
		}

		CalcDebugID();
	}

	public void Build(NavNode nwNode, NavNode neNode, NavNode seNode, NavNode swNode) {
		NWCorner = nwNode.GetPosition();
		SECorner = seNode.GetPosition();

		Center.X = (NWCorner.X + SECorner.X) / 2.0f;
		Center.Y = (NWCorner.Y + SECorner.Y) / 2.0f;
		Center.Z = (NWCorner.Z + SECorner.Z) / 2.0f;

		NEZ = neNode.GetPosition().Z;
		SWZ = swNode.GetPosition().Z;

		Node[(int)NavCornerType.NorthWest] = nwNode;
		Node[(int)NavCornerType.NorthEast] = neNode;
		Node[(int)NavCornerType.SouthEast] = seNode;
		Node[(int)NavCornerType.SouthWest] = swNode;

		if ((SECorner.X - NWCorner.X) > 0.0f && (SECorner.Y - NWCorner.Y) > 0.0f) {
			InvDXCorners = 1.0f / (SECorner.X - NWCorner.X);
			InvDYCorners = 1.0f / (SECorner.Y - NWCorner.Y);
		}
		else
			InvDXCorners = InvDYCorners = 0;

		AssignNodes(this);

		CalcDebugID();
	}

	public void GetExtent(ref Extent extent) {
		extent.Lo = NWCorner;
		extent.Hi = SECorner;

		extent.Lo.Z = MathF.Min(extent.Lo.Z, NWCorner.Z);
		extent.Lo.Z = MathF.Min(extent.Lo.Z, SECorner.Z);
		extent.Lo.Z = MathF.Min(extent.Lo.Z, NEZ);
		extent.Lo.Z = MathF.Min(extent.Lo.Z, SWZ);

		extent.Hi.Z = MathF.Max(extent.Hi.Z, NWCorner.Z);
		extent.Hi.Z = MathF.Max(extent.Hi.Z, SECorner.Z);
		extent.Hi.Z = MathF.Max(extent.Hi.Z, NEZ);
		extent.Hi.Z = MathF.Max(extent.Hi.Z, SWZ);
	}

	public Vector3 GetCenter() => Center;

	public NavNode? FindClosestNode(Vector3 pos, NavDirType dir) {
		if (!HasNodes())
			return null;

		List<NavNode> nodes = [];
		GetNodes(dir, nodes);

		NavNode? bestNode = null;
		float bestDistanceSq = float.MaxValue;

		foreach (NavNode node in nodes) {
			float distSq = Vector3.DistanceSquared(pos, node.GetPosition());
			if (distSq < bestDistanceSq) {
				bestDistanceSq = distSq;
				bestNode = node;
			}
		}

		return bestNode;
	}

	void GetNodes(NavDirType dir, List<NavNode> nodes) {
		if (nodes == null)
			return;

		nodes.Clear();

		NavCornerType startCorner;
		NavCornerType endCorner;
		NavDirType traversalDirection;

		switch (dir) {
			case NavDirType.North:
				startCorner = NavCornerType.NorthWest;
				endCorner = NavCornerType.NorthEast;
				traversalDirection = NavDirType.East;
				break;

			case NavDirType.South:
				startCorner = NavCornerType.SouthWest;
				endCorner = NavCornerType.SouthEast;
				traversalDirection = NavDirType.East;
				break;

			case NavDirType.East:
				startCorner = NavCornerType.NorthEast;
				endCorner = NavCornerType.SouthEast;
				traversalDirection = NavDirType.South;
				break;

			case NavDirType.West:
				startCorner = NavCornerType.NorthWest;
				endCorner = NavCornerType.SouthWest;
				traversalDirection = NavDirType.South;
				break;

			default:
				return;
		}

		for (NavNode? node = Node[(int)startCorner]; node != null && node != Node[(int)endCorner]; node = node.GetConnectedNode(traversalDirection))
			nodes.Add(node);

		if (Node[(int)endCorner] != null)
			nodes.Add(Node[(int)endCorner]!);
	}

	void ConnectElevators() {
		Elevator = null;
		AttributeFlags &= ~NavAttributeType.HasElevator;
		ElevatorAreas.Clear();
	}

	public void OnServerActivate() {
		ConnectElevators();
		DamagingTickCount = 0;
		ClearAllNavCostEntities();
	}

	void OnRoundRestart() {
		ConnectElevators();
		DamagingTickCount = 0;
		ClearAllNavCostEntities();
	}

	public void ResetNodes() {
		for (int i = 0; i < (int)NavCornerType.NumCorners; i++)
			Node[i] = null;
	}

	public bool HasNodes() {
		for (int i = 0; i < (int)NavCornerType.NumCorners; i++)
			if (Node[i] != null)
				return true;

		return false;
	}

	void OnDestroyNotify(NavArea dead) {
		for (int i = 0; i < (int)NavDirType.NumDirections; i++) {
			Connect[i].RemoveAll(c => c.Area == dead);
			IncomingConnect[i].RemoveAll(c => c.Area == dead);
		}

		InheritVisibilityFrom.Area = null;
		PotentiallyVisibleAreas.Clear();
		IsInheritedFrom = false;
	}

	void OnDestroyNotify(NavLadder dead) => Disconnect(dead);

	public void ConnectTo(NavArea area, NavDirType dir) {
		if (area == this)
			return;

		foreach (NavConnect connect in Connect[(int)dir])
			if (connect.Area == area)
				return;

		NavConnect con = new() {
			Area = area,
			Length = (area.GetCenter() - GetCenter()).Length()
		};
		Connect[(int)dir].Add(con);
		IncomingConnect[(int)dir].RemoveAll(c => c.Area == area);

		NavDirType opposite = OppositeDirection(dir);
		con.Area = this;

		if (area.Connect[(int)opposite].FindIndex(c => c.Area == this) == -1)
			area.AddIncomingConnection(this, opposite);
	}

	public void ConnectTo(NavLadder ladder) {
		float center = (ladder.Top.Z + ladder.Bottom.Z) * 0.5f;

		Disconnect(ladder);

		if (GetCenter().Z > center)
			AddLadderDown(ladder);
		else
			AddLadderUp(ladder);
	}

	public void Disconnect(NavArea area) {
		for (int i = 0; i < (int)NavDirType.NumDirections; i++) {
			NavDirType dir = (NavDirType)i;
			NavDirType opposite = OppositeDirection(dir);

			if (Connect[(int)dir].FindIndex(c => c.Area == area) != -1) {
				Connect[(int)dir].RemoveAll(c => c.Area == area);
				if (area.IsConnected(this, opposite))
					AddIncomingConnection(area, opposite);
				else
					area.IncomingConnect[(int)opposite].RemoveAll(c => c.Area == this);
			}
		}
	}

	public void Disconnect(NavLadder ladder) {
		for (int i = 0; i < (int)NavLadder.LadderDirectionType.NumLadderDirections; i++)
			Ladder[i].RemoveAll(c => c.Ladder == ladder);
	}

	public uint GetID() => ID;

	public void SetAttributes(NavAttributeType bits) => AttributeFlags = bits;

	public NavAttributeType GetAttributes() => AttributeFlags;

	public bool HasAttributes(NavAttributeType bits) => (AttributeFlags & bits) != 0;

	void RemoveAttributes(NavAttributeType bits) => AttributeFlags &= ~bits;

	public void SetPlace(NavPlace place) => Place = place;

	public NavPlace GetPlace() => Place;

	void AddLadderUp(NavLadder ladder) {
		Disconnect(ladder);
		Ladder[(int)NavLadder.LadderDirectionType.Up].Add(new() { Ladder = ladder });
	}

	void AddLadderDown(NavLadder ladder) {
		Disconnect(ladder);
		Ladder[(int)NavLadder.LadderDirectionType.Down].Add(new() { Ladder = ladder });
	}

	public void FinishMerge(NavArea adjArea) {
		NWCorner = Node[(int)NavCornerType.NorthWest]!.GetPosition();
		SECorner = Node[(int)NavCornerType.SouthEast]!.GetPosition();

		Center.X = (NWCorner.X + SECorner.X) / 2.0f;
		Center.Y = (NWCorner.Y + SECorner.Y) / 2.0f;
		Center.Z = (NWCorner.Z + SECorner.Z) / 2.0f;

		NEZ = Node[(int)NavCornerType.NorthEast]!.GetPosition().Z;
		SWZ = Node[(int)NavCornerType.SouthWest]!.GetPosition().Z;

		if ((SECorner.X - NWCorner.X) > 0.0f && (SECorner.Y - NWCorner.Y) > 0.0f) {
			InvDXCorners = 1.0f / (SECorner.X - NWCorner.X);
			InvDYCorners = 1.0f / (SECorner.Y - NWCorner.Y);
		}
		else
			InvDXCorners = InvDYCorners = 0;

		adjArea.AssignNodes(this);

		MergeAdjacentConnections(adjArea);

		TheNavAreas.Remove(adjArea);
		NavMesh.Instance!.OnEditDestroyNotify(adjArea);
		NavMesh.Instance.DestroyArea(adjArea);
	}

	void MergeAdjacentConnections(NavArea adjArea) {
		int dir;
		for (dir = 0; dir < (int)NavDirType.NumDirections; dir++) {
			foreach (NavConnect connect in adjArea.Connect[dir]) {
				if (connect.Area != adjArea && connect.Area != this)
					ConnectTo(connect.Area!, (NavDirType)dir);
			}
		}

		Disconnect(adjArea);

		foreach (NavArea area in TheNavAreas) {
			if (area == this || area == adjArea)
				continue;

			for (dir = 0; dir < (int)NavDirType.NumDirections; dir++) {
				bool connected = false;
				foreach (NavConnect connect in area.Connect[dir]) {
					if (connect.Area == adjArea) {
						connected = true;
						break;
					}
				}

				if (connected) {
					area.Disconnect(adjArea);
					area.Disconnect(this);
					area.ConnectTo(this, (NavDirType)dir);
				}
			}
		}

		for (dir = 0; dir < (int)NavLadder.LadderDirectionType.NumLadderDirections; ++dir) {
			foreach (NavLadderConnect ladderConnect in adjArea.Ladder[dir])
				ConnectTo(ladderConnect.Ladder!);
		}

		LadderConnectionReplacement replacement = new(adjArea, this);
		NavMesh.Instance!.ForAllLadders(replacement.Invoke);
	}

	public void AssignNodes(NavArea area) {
		NavNode? horizLast = Node[(int)NavCornerType.NorthEast];
		for (NavNode? vertNode = Node[(int)NavCornerType.NorthWest]; vertNode != Node[(int)NavCornerType.SouthWest]; vertNode = vertNode!.GetConnectedNode(NavDirType.South)) {
			for (NavNode? horizNode = vertNode; horizNode != horizLast; horizNode = horizNode!.GetConnectedNode(NavDirType.East))
				horizNode!.AssignArea(area);

			horizLast = horizLast!.GetConnectedNode(NavDirType.South);
		}
	}

	public bool SplitEdit(bool splitAlongX, float splitEdge, [NotNullWhen(true)] out NavArea? outAlpha, [NotNullWhen(true)] out NavArea? outBeta) {
		outAlpha = null;
		outBeta = null;

		NavArea? alpha = null;
		NavArea? beta = null;

		if (splitAlongX) {
			if (splitEdge <= NWCorner.Y + 1.0f)
				return false;

			if (splitEdge >= SECorner.Y - 1.0f)
				return false;

			alpha = NavMesh.CreateArea();
			alpha.NWCorner = NWCorner;

			alpha.SECorner.X = SECorner.X;
			alpha.SECorner.Y = splitEdge;
			alpha.SECorner.Z = GetZ(alpha.SECorner);

			beta = NavMesh.CreateArea();
			beta.NWCorner.X = NWCorner.X;
			beta.NWCorner.Y = splitEdge;
			beta.NWCorner.Z = GetZ(beta.NWCorner);

			beta.SECorner = SECorner;

			alpha.ConnectTo(beta, NavDirType.South);
			beta.ConnectTo(alpha, NavDirType.North);

			FinishSplitEdit(alpha, NavDirType.South);
			FinishSplitEdit(beta, NavDirType.North);
		}
		else {
			if (splitEdge <= NWCorner.X + 1.0f)
				return false;

			if (splitEdge >= SECorner.X - 1.0f)
				return false;

			alpha = NavMesh.CreateArea();
			alpha.NWCorner = NWCorner;

			alpha.SECorner.X = splitEdge;
			alpha.SECorner.Y = SECorner.Y;
			alpha.SECorner.Z = GetZ(alpha.SECorner);

			beta = NavMesh.CreateArea();
			beta.NWCorner.X = splitEdge;
			beta.NWCorner.Y = NWCorner.Y;
			beta.NWCorner.Z = GetZ(beta.NWCorner);

			beta.SECorner = SECorner;

			alpha.ConnectTo(beta, NavDirType.East);
			beta.ConnectTo(alpha, NavDirType.West);

			FinishSplitEdit(alpha, NavDirType.East);
			FinishSplitEdit(beta, NavDirType.West);
		}

		if (!NavMesh.Instance!.IsGenerating() && nav_split_place_on_ground.GetBool()) {
			alpha.PlaceOnGround(NavCornerType.NumCorners);
			beta.PlaceOnGround(NavCornerType.NumCorners);
		}

		int dir;
		for (dir = 0; dir < (int)NavLadder.LadderDirectionType.NumLadderDirections; ++dir) {
			for (int it = 0; it < Ladder[dir].Count; it++) {
				NavLadder ladder = Ladder[dir][it].Ladder!;
				Vector3 ladderPos = ladder.Top;

				float alphaDistance = alpha.GetDistanceSquaredToPoint(ladderPos);
				float betaDistance = beta.GetDistanceSquaredToPoint(ladderPos);

				if (alphaDistance < betaDistance)
					alpha.ConnectTo(ladder);
				else
					beta.ConnectTo(ladder);
			}
		}

		SplitNotification notify = new(this, alpha, beta);
		NavMesh.Instance!.ForAllLadders(notify.Invoke);

		outAlpha = alpha;
		outBeta = beta;

		NavMesh.Instance!.OnEditCreateNotify(alpha);
		NavMesh.Instance!.OnEditCreateNotify(beta);
		if (NavMesh.Instance!.IsInSelectedSet(this)) {
			NavMesh.Instance!.AddToSelectedSet(alpha);
			NavMesh.Instance!.AddToSelectedSet(beta);
		}

		NavMesh.Instance!.OnEditDestroyNotify(this);
		TheNavAreas.Remove(this);
		NavMesh.Instance!.RemoveFromSelectedSet(this);
		NavMesh.Instance!.DestroyArea(this);

		return true;
	}

	public bool IsConnected(NavLadder ladder, NavLadder.LadderDirectionType dir) {
		for (int i = 0; i < Ladder[(int)dir].Count; i++) {
			if (Ladder[(int)dir][i].Ladder == ladder)
				return true;
		}

		return false;
	}

	public bool IsConnected(NavArea area, NavDirType dir) {
		if (area == this)
			return true;

		if (dir == NavDirType.NumDirections) {
			for (int d = 0; d < (int)NavDirType.NumDirections; d++) {
				foreach (NavConnect connect in Connect[d]) {
					if (connect.Area == area)
						return true;
				}
			}

			foreach (NavLadderConnect ladderConnect in Ladder[(int)NavLadder.LadderDirectionType.Up]) {
				NavLadder ladder = ladderConnect.Ladder!;

				if (ladder.TopBehindArea == area || ladder.TopForwardArea == area || ladder.TopLeftArea == area || ladder.TopRightArea == area)
					return true;
			}

			foreach (NavLadderConnect ladderConnect in Ladder[(int)NavLadder.LadderDirectionType.Down]) {
				NavLadder ladder = ladderConnect.Ladder!;

				if (ladder.BottomArea == area)
					return true;
			}
		}
		else {
			foreach (NavConnect connect in Connect[(int)dir]) {
				if (connect.Area == area)
					return true;
			}
		}

		return false;
	}

	float ComputeGroundHeightChange(NavArea area) {
		GetClosestPointOnArea(area.GetCenter(), out Vector3 closeFrom);
		area.GetClosestPointOnArea(GetCenter(), out Vector3 closeTo);

		if (!NavMesh.GetSimpleGroundHeight(closeTo + new Vector3(0, 0, StepHeight), out float toZ, out _))
			return 0.0f;

		if (!NavMesh.GetSimpleGroundHeight(closeFrom + new Vector3(0, 0, StepHeight), out float fromZ, out _))
			return 0.0f;

		return toZ - fromZ;
	}

	public List<NavConnect> GetIncomingConnections(NavDirType dir) => IncomingConnect[(int)dir];

	public void AddIncomingConnection(NavArea source, NavDirType incomingEdgeDir) {
		NavConnect connect = new() {
			Area = source
		};

		if (!IncomingConnect[(int)incomingEdgeDir].Contains(connect)) {
			connect.Length = (source.GetCenter() - GetCenter()).Length();
			IncomingConnect[(int)incomingEdgeDir].Add(connect);
		}
	}

	public List<NavLadderConnect> GetLadders(NavLadder.LadderDirectionType dir) => Ladder[(int)dir];

	public FuncElevator? GetElevator() => Elevator;

	public List<NavConnect> GetElevatorAreas() => ElevatorAreas;

	void FinishSplitEdit(NavArea newArea, NavDirType ignoreEdge) {
		newArea.InheritAttributes(this);

		newArea.Center.X = (newArea.NWCorner.X + newArea.SECorner.X) / 2.0f;
		newArea.Center.Y = (newArea.NWCorner.Y + newArea.SECorner.Y) / 2.0f;
		newArea.Center.Z = (newArea.NWCorner.Z + newArea.SECorner.Z) / 2.0f;

		newArea.NEZ = GetZ(newArea.SECorner.X, newArea.NWCorner.Y);
		newArea.SWZ = GetZ(newArea.NWCorner.X, newArea.SECorner.Y);

		if ((SECorner.X - NWCorner.X) > 0.0f && (SECorner.Y - NWCorner.Y) > 0.0f) {
			newArea.InvDXCorners = 1.0f / (newArea.SECorner.X - newArea.NWCorner.X);
			newArea.InvDYCorners = 1.0f / (newArea.SECorner.Y - newArea.NWCorner.Y);
		}
		else
			newArea.InvDXCorners = newArea.InvDYCorners = 0;

		for (NavDirType d = 0; d < NavDirType.NumDirections; ++d) {
			if (d == ignoreEdge)
				continue;

			int count = GetAdjacentCount(d);

			for (int a = 0; a < count; ++a) {
				NavArea adj = GetAdjacentArea(d, a)!;

				switch (d) {
					case NavDirType.North:
					case NavDirType.South:
						if (newArea.IsOverlappingX(adj)) {
							newArea.ConnectTo(adj, d);

							if (adj.IsConnected(this, OppositeDirection(d)))
								adj.ConnectTo(newArea, OppositeDirection(d));
						}
						break;

					case NavDirType.East:
					case NavDirType.West:
						if (newArea.IsOverlappingY(adj)) {
							newArea.ConnectTo(adj, d);

							if (adj.IsConnected(this, OppositeDirection(d)))
								adj.ConnectTo(newArea, OppositeDirection(d));
						}
						break;
				}

				for (int b = 0; b < IncomingConnect[(int)d].Count; b++) {
					NavArea adj2 = IncomingConnect[(int)d][b].Area!;

					switch (d) {
						case NavDirType.North:
						case NavDirType.South:
							if (newArea.IsOverlappingX(adj2))
								adj2.ConnectTo(newArea, OppositeDirection(d));
							break;

						case NavDirType.East:
						case NavDirType.West:
							if (newArea.IsOverlappingY(adj2))
								adj2.ConnectTo(newArea, OppositeDirection(d));
							break;
					}
				}
			}
		}

		TheNavAreas.Add(newArea);
		NavMesh.Instance!.AddNavArea(newArea);

		if (HasNodes()) {
			newArea.Node[(int)NavCornerType.NorthWest] = Node[(int)NavCornerType.NorthWest];
			newArea.Node[(int)NavCornerType.NorthEast] = Node[(int)NavCornerType.NorthEast];
			newArea.Node[(int)NavCornerType.SouthEast] = Node[(int)NavCornerType.SouthEast];
			newArea.Node[(int)NavCornerType.SouthWest] = Node[(int)NavCornerType.SouthWest];

			NavDirType dir = NavDirType.NumDirections;
			NavCornerType[] corner = { NavCornerType.NumCorners, NavCornerType.NumCorners };

			switch (ignoreEdge) {
				case NavDirType.North:
					dir = NavDirType.South;
					corner[0] = NavCornerType.NorthWest;
					corner[1] = NavCornerType.NorthEast;
					break;
				case NavDirType.South:
					dir = NavDirType.North;
					corner[0] = NavCornerType.SouthWest;
					corner[1] = NavCornerType.SouthEast;
					break;
				case NavDirType.East:
					dir = NavDirType.West;
					corner[0] = NavCornerType.NorthEast;
					corner[1] = NavCornerType.SouthEast;
					break;
				case NavDirType.West:
					dir = NavDirType.East;
					corner[0] = NavCornerType.NorthWest;
					corner[1] = NavCornerType.SouthWest;
					break;
			}

			while (!newArea.IsOverlapping(newArea.Node[(int)corner[0]]!.GetPosition(), GenerationStepSize / 2)) {
				for (int i = 0; i < 2; ++i) {
					Assert(newArea.Node[(int)corner[i]]);
					Assert(newArea.Node[(int)corner[i]]!.GetConnectedNode(dir));
					newArea.Node[(int)corner[i]] = newArea.Node[(int)corner[i]]!.GetConnectedNode(dir);
				}
			}

			newArea.AssignNodes(newArea);

			newArea.NEZ = newArea.Node[(int)NavCornerType.NorthEast]!.GetPosition().Z;
			newArea.NWCorner.Z = newArea.Node[(int)NavCornerType.NorthWest]!.GetPosition().Z;
			newArea.SWZ = newArea.Node[(int)NavCornerType.SouthWest]!.GetPosition().Z;
			newArea.SECorner.Z = newArea.Node[(int)NavCornerType.SouthEast]!.GetPosition().Z;
		}
	}

	public bool SpliceEdit(NavArea other) {
		NavArea? newArea;
		Vector3 nw = default, ne = default, se = default, sw = default;

		if (NWCorner.X > other.SECorner.X) {
			float top = Math.Max(NWCorner.Y, other.NWCorner.Y);
			float bottom = Math.Min(SECorner.Y, other.SECorner.Y);

			nw.X = other.SECorner.X;
			nw.Y = top;
			nw.Z = other.GetZ(nw);

			se.X = NWCorner.X;
			se.Y = bottom;
			se.Z = GetZ(se);

			ne.X = se.X;
			ne.Y = nw.Y;
			ne.Z = GetZ(ne);

			sw.X = nw.X;
			sw.Y = se.Y;
			sw.Z = other.GetZ(sw);

			newArea = NavMesh.CreateArea();
			if (newArea == null) {
				Warning("SpliceEdit: Out of memory.\n");
				return false;
			}

			newArea.Build(nw, ne, se, sw);

			ConnectTo(newArea, NavDirType.West);
			newArea.ConnectTo(this, NavDirType.East);

			other.ConnectTo(newArea, NavDirType.East);
			newArea.ConnectTo(other, NavDirType.West);
		}
		else if (SECorner.X < other.NWCorner.X) {
			float top = Math.Max(NWCorner.Y, other.NWCorner.Y);
			float bottom = Math.Min(SECorner.Y, other.SECorner.Y);

			nw.X = SECorner.X;
			nw.Y = top;
			nw.Z = GetZ(nw);

			se.X = other.NWCorner.X;
			se.Y = bottom;
			se.Z = other.GetZ(se);

			ne.X = se.X;
			ne.Y = nw.Y;
			ne.Z = other.GetZ(ne);

			sw.X = nw.X;
			sw.Y = se.Y;
			sw.Z = GetZ(sw);

			newArea = NavMesh.CreateArea();
			if (newArea == null) {
				Warning("SpliceEdit: Out of memory.\n");
				return false;
			}

			newArea.Build(nw, ne, se, sw);

			ConnectTo(newArea, NavDirType.East);
			newArea.ConnectTo(this, NavDirType.West);

			other.ConnectTo(newArea, NavDirType.West);
			newArea.ConnectTo(other, NavDirType.East);
		}
		else {
			if (NWCorner.Y > other.SECorner.Y) {
				float left = Math.Max(NWCorner.X, other.NWCorner.X);
				float right = Math.Min(SECorner.X, other.SECorner.X);

				nw.X = left;
				nw.Y = other.SECorner.Y;
				nw.Z = other.GetZ(nw);

				se.X = right;
				se.Y = NWCorner.Y;
				se.Z = GetZ(se);

				ne.X = se.X;
				ne.Y = nw.Y;
				ne.Z = other.GetZ(ne);

				sw.X = nw.X;
				sw.Y = se.Y;
				sw.Z = GetZ(sw);

				newArea = NavMesh.CreateArea();
				if (newArea == null) {
					Warning("SpliceEdit: Out of memory.\n");
					return false;
				}

				newArea.Build(nw, ne, se, sw);

				ConnectTo(newArea, NavDirType.North);
				newArea.ConnectTo(this, NavDirType.South);

				other.ConnectTo(newArea, NavDirType.South);
				newArea.ConnectTo(other, NavDirType.North);
			}
			else if (SECorner.Y < other.NWCorner.Y) {
				float left = Math.Max(NWCorner.X, other.NWCorner.X);
				float right = Math.Min(SECorner.X, other.SECorner.X);

				nw.X = left;
				nw.Y = SECorner.Y;
				nw.Z = GetZ(nw);

				se.X = right;
				se.Y = other.NWCorner.Y;
				se.Z = other.GetZ(se);

				ne.X = se.X;
				ne.Y = nw.Y;
				ne.Z = GetZ(ne);

				sw.X = nw.X;
				sw.Y = se.Y;
				sw.Z = other.GetZ(sw);

				newArea = NavMesh.CreateArea();
				if (newArea == null) {
					Warning("SpliceEdit: Out of memory.\n");
					return false;
				}

				newArea.Build(nw, ne, se, sw);

				ConnectTo(newArea, NavDirType.South);
				newArea.ConnectTo(this, NavDirType.North);

				other.ConnectTo(newArea, NavDirType.North);
				newArea.ConnectTo(other, NavDirType.South);
			}
			else
				return false;
		}

		newArea.InheritAttributes(this, other);

		TheNavAreas.Add(newArea);
		NavMesh.Instance!.AddNavArea(newArea);

		NavMesh.Instance!.OnEditCreateNotify(newArea);

		return true;
	}

	unsafe void CalcDebugID() {
		if (DebugID == 0) {
			int[] coords = [(int)NWCorner.X, (int)NWCorner.X, (int)NWCorner.Z, (int)SECorner.X, (int)SECorner.Y, (int)SECorner.Z];

			fixed (int* ptr = coords)
				DebugID = CRC32.ProcessSingleBuffer(ptr, sizeof(int) * coords.Length);
		}
	}

	public bool MergeEdit(NavArea adj) {
		const float tolerance = 1.0f;
		bool merge = false;

		if (MathF.Abs(NWCorner.X - adj.NWCorner.X) < tolerance &&
				MathF.Abs(SECorner.X - adj.SECorner.X) < tolerance)
			merge = true;

		if (MathF.Abs(NWCorner.Y - adj.NWCorner.Y) < tolerance &&
				MathF.Abs(SECorner.Y - adj.SECorner.Y) < tolerance)
			merge = true;

		if (!merge)
			return false;

		Vector3 originalNWCorner = NWCorner;
		Vector3 originalSECorner = SECorner;

		if (NWCorner.X > adj.NWCorner.X || NWCorner.Y > adj.NWCorner.Y)
			NWCorner = adj.NWCorner;

		if (SECorner.X < adj.SECorner.X || SECorner.Y < adj.SECorner.Y)
			SECorner = adj.SECorner;

		Center.X = (NWCorner.X + SECorner.X) / 2.0f;
		Center.Y = (NWCorner.Y + SECorner.Y) / 2.0f;
		Center.Z = (NWCorner.Z + SECorner.Z) / 2.0f;

		if ((SECorner.X - NWCorner.X) > 0.0f && (SECorner.Y - NWCorner.Y) > 0.0f) {
			InvDXCorners = 1.0f / (SECorner.X - NWCorner.X);
			InvDYCorners = 1.0f / (SECorner.Y - NWCorner.Y);
		}
		else
			InvDXCorners = InvDYCorners = 0;

		if (SECorner.X > originalSECorner.X || NWCorner.Y < originalNWCorner.Y)
			NEZ = adj.GetZ(SECorner.X, NWCorner.Y);
		else
			NEZ = GetZ(SECorner.X, NWCorner.Y);

		if (NWCorner.X < originalNWCorner.X || SECorner.Y > originalSECorner.Y)
			SWZ = adj.GetZ(NWCorner.X, SECorner.Y);
		else
			SWZ = GetZ(NWCorner.X, SECorner.Y);

		MergeAdjacentConnections(adj);
		InheritAttributes(adj);

		TheNavAreas.Remove(adj);
		NavMesh.Instance!.OnEditDestroyNotify(adj);
		NavMesh.Instance.DestroyArea(adj);

		NavMesh.Instance.OnEditCreateNotify(this);

		return true;
	}

	public void InheritAttributes(NavArea first, NavArea? second = null) {
		if (first != null && second != null) {
			SetAttributes(first.GetAttributes() | second.GetAttributes());

			if (first.GetPlace() == second.GetPlace())
				SetPlace(first.GetPlace());
			else if (first.GetPlace() == UndefinedPlace)
				SetPlace(second.GetPlace());
			else if (second.GetPlace() == UndefinedPlace)
				SetPlace(first.GetPlace());
			else {
				if (RandomInt(0, 100) < 50)
					SetPlace(first.GetPlace());
				else
					SetPlace(second.GetPlace());
			}
		}
		else if (first != null) {
			SetAttributes(GetAttributes() | first.GetAttributes());

			if (GetPlace() == UndefinedPlace)
				SetPlace(first.GetPlace());
		}
	}

	public void Strip() => SpotEncounters.Clear();

	public bool IsRoughlySquare() {
		float aspect = GetSizeX() / GetSizeY();

		const float maxAspect = 3.01f;
		const float minAspect = 1.0f / maxAspect;

		if (aspect < minAspect || aspect > maxAspect)
			return false;

		return true;
	}

	public bool IsOverlapping(Vector3 pos, float tolerance = 0.0f) => pos.X + tolerance >= NWCorner.X && pos.X - tolerance <= SECorner.X && pos.Y + tolerance >= NWCorner.Y && pos.Y - tolerance <= SECorner.Y;

	public bool IsOverlapping(NavArea area) => area.NWCorner.X < SECorner.X && area.SECorner.X > NWCorner.X && area.NWCorner.Y < SECorner.Y && area.SECorner.Y > NWCorner.Y;

	public bool IsOverlapping(Extent extent) => extent.Lo.X < SECorner.X && extent.Hi.X > NWCorner.X && extent.Lo.Y < SECorner.Y && extent.Hi.Y > NWCorner.Y;

	bool IsOverlappingX(NavArea area) => area.NWCorner.X < SECorner.X && area.SECorner.X > NWCorner.X;

	bool IsOverlappingY(NavArea area) => area.NWCorner.Y < SECorner.Y && area.SECorner.Y > NWCorner.Y;

	public bool Contains(Vector3 pos) {
		if (!IsOverlapping(pos))
			return false;

		float myZ = GetZ(pos);

		if (myZ - StepHeight > pos.Z)
			return false;

		Extent areaExtent = default;
		GetExtent(ref areaExtent);

		OverlapCheck overlap = new(this, pos);
		return NavMesh.Instance!.ForAllAreasOverlappingExtent(overlap.Invoke, areaExtent);
	}

	public bool Contains(NavArea area) => (NWCorner.X <= area.NWCorner.X) && (SECorner.X >= area.SECorner.X) &&
						(NWCorner.Y <= area.NWCorner.Y) && (SECorner.Y >= area.SECorner.Y) &&
						(NWCorner.Z <= area.NWCorner.Z) && (SECorner.Z >= area.SECorner.Z);

	public void ComputeNormal(ref Vector3 normal, bool alternate = false) {
		Vector3 u, v;

		if (!alternate) {
			u.X = SECorner.X - NWCorner.X;
			u.Y = 0.0f;
			u.Z = NEZ - NWCorner.Z;

			v.X = 0.0f;
			v.Y = SECorner.Y - NWCorner.Y;
			v.Z = SWZ - NWCorner.Z;
		}
		else {
			u.X = NWCorner.X - SECorner.X;
			u.Y = 0.0f;
			u.Z = SWZ - SECorner.Z;

			v.X = 0.0f;
			v.Y = NWCorner.Y - SECorner.Y;
			v.Z = NEZ - SECorner.Z;
		}

		MathLib.CrossProduct(u, v, out normal);
		normal.NormalizeInPlace();
	}

	public void RemoveOrthogonalConnections(NavDirType dir) {
		NavDirType[] dirToRemove = [DirectionLeft(dir), DirectionRight(dir)];
		for (int i = 0; i < 2; i++) {
			dir = dirToRemove[i];
			while (GetAdjacentCount(dir) > 0) {
				NavArea adj = GetAdjacentArea(dir, 0)!;
				Disconnect(adj);
				adj.Disconnect(this);
			}
		}
	}

	bool IsFlat() {
		Vector3 normal = default, otherNormal = default;
		ComputeNormal(ref normal);
		ComputeNormal(ref otherNormal, true);

		float tolerance = nav_coplanar_slope_limit.GetFloat();
		if ((Node[(int)NavCornerType.NorthWest] != null && Node[(int)NavCornerType.NorthWest]!.IsOnDisplacementSurface()) ||
				(Node[(int)NavCornerType.NorthEast] != null && Node[(int)NavCornerType.NorthEast]!.IsOnDisplacementSurface()) ||
				(Node[(int)NavCornerType.SouthEast] != null && Node[(int)NavCornerType.SouthEast]!.IsOnDisplacementSurface()) ||
				(Node[(int)NavCornerType.SouthWest] != null && Node[(int)NavCornerType.SouthWest]!.IsOnDisplacementSurface())) {
			tolerance = nav_coplanar_slope_limit_displacement.GetFloat();
		}

		if (MathLib.DotProduct(normal, otherNormal) > tolerance)
			return true;

		return false;
	}

	public bool IsCoplanar(NavArea area) {
		bool isOnDisplacement = (Node[(int)NavCornerType.NorthWest] != null && Node[(int)NavCornerType.NorthWest]!.IsOnDisplacementSurface()) ||
														(Node[(int)NavCornerType.NorthEast] != null && Node[(int)NavCornerType.NorthEast]!.IsOnDisplacementSurface()) ||
														(Node[(int)NavCornerType.SouthEast] != null && Node[(int)NavCornerType.SouthEast]!.IsOnDisplacementSurface()) ||
														(Node[(int)NavCornerType.SouthWest] != null && Node[(int)NavCornerType.SouthWest]!.IsOnDisplacementSurface());

		if (!isOnDisplacement && !IsFlat())
			return false;

		bool areaIsOnDisplacement = (area.Node[(int)NavCornerType.NorthWest] != null && area.Node[(int)NavCornerType.NorthWest]!.IsOnDisplacementSurface()) ||
																(area.Node[(int)NavCornerType.NorthEast] != null && area.Node[(int)NavCornerType.NorthEast]!.IsOnDisplacementSurface()) ||
																(area.Node[(int)NavCornerType.SouthEast] != null && area.Node[(int)NavCornerType.SouthEast]!.IsOnDisplacementSurface()) ||
																(area.Node[(int)NavCornerType.SouthWest] != null && area.Node[(int)NavCornerType.SouthWest]!.IsOnDisplacementSurface());

		if (!areaIsOnDisplacement && !area.IsFlat())
			return false;

		Vector3 normal = default, otherNormal = default;
		ComputeNormal(ref normal);
		area.ComputeNormal(ref otherNormal);

		float tolerance = nav_coplanar_slope_limit.GetFloat();
		if ((Node[(int)NavCornerType.NorthWest] != null && Node[(int)NavCornerType.NorthWest]!.IsOnDisplacementSurface()) ||
				(Node[(int)NavCornerType.NorthEast] != null && Node[(int)NavCornerType.NorthEast]!.IsOnDisplacementSurface()) ||
				(Node[(int)NavCornerType.SouthEast] != null && Node[(int)NavCornerType.SouthEast]!.IsOnDisplacementSurface()) ||
				(Node[(int)NavCornerType.SouthWest] != null && Node[(int)NavCornerType.SouthWest]!.IsOnDisplacementSurface())) {
			tolerance = nav_coplanar_slope_limit_displacement.GetFloat();
		}

		if (MathLib.DotProduct(normal, otherNormal) > tolerance)
			return true;

		return false;
	}

	public float GetZ(float x, float y) {
		if (InvDXCorners == 0 || InvDYCorners == 0)
			return NEZ;

		float u = (x - NWCorner.X) * InvDXCorners;
		float v = (y - NWCorner.Y) * InvDYCorners;

		u = u >= 0 ? u : 0;
		u = u >= 1 ? 1 : u;
		v = v >= 0 ? v : 0;
		v = v >= 1 ? 1 : v;

		float northZ = NWCorner.Z + u * (NEZ - NWCorner.Z);
		float southZ = SWZ + u * (SECorner.Z - SWZ);

		return northZ + v * (southZ - northZ);
	}

	public void GetClosestPointOnArea(ref Vector3 pos, out Vector3 close) {
		float x = pos.X >= NWCorner.X ? pos.X : NWCorner.X;
		x = x <= SECorner.X ? x : SECorner.X;

		float y = pos.Y >= NWCorner.Y ? pos.Y : NWCorner.Y;
		y = y <= SECorner.Y ? y : SECorner.Y;

		float z = GetZ(x, y);

		close = new(x, y, z);
	}

	public void GetClosestPointOnArea(Vector3 pos, out Vector3 close) => GetClosestPointOnArea(ref pos, out close);

	public float GetDistanceSquaredToPoint(Vector3 pos) {
		if (pos.X < NWCorner.X) {
			if (pos.Y < NWCorner.Y)
				return (NWCorner - pos).LengthSqr();
			else if (pos.Y > SECorner.Y) {
				Vector3 d;
				d.X = NWCorner.X - pos.X;
				d.Y = SECorner.Y - pos.Y;
				d.Z = SWZ - pos.Z;
				return d.LengthSqr();
			}
			else {
				float d = NWCorner.X - pos.X;
				return d * d;
			}
		}
		else if (pos.X > SECorner.X) {
			if (pos.Y < NWCorner.Y) {
				Vector3 d;
				d.X = SECorner.X - pos.X;
				d.Y = NWCorner.Y - pos.Y;
				d.Z = NEZ - pos.Z;
				return d.LengthSqr();
			}
			else if (pos.Y > SECorner.Y)
				return (SECorner - pos).LengthSqr();
			else {
				float d = pos.X - SECorner.X;
				return d * d;
			}
		}
		else if (pos.Y < NWCorner.Y) {
			float d = NWCorner.Y - pos.Y;
			return d * d;
		}
		else if (pos.Y > SECorner.Y) {
			float d = pos.Y - SECorner.Y;
			return d * d;
		}
		else {
			float z = GetZ(pos);
			float d = z - pos.Z;
			return d * d;
		}
	}

	NavArea? GetRandomAdjacentArea(NavDirType dir) {
		int count = Connect[(int)dir].Count;
		int which = RandomInt(0, count - 1);

		int i = 0;
		foreach (NavConnect connect in Connect[(int)dir]) {
			if (i == which)
				return connect.Area!;

			i++;
		}

		return null;
	}

	public void CollectAdjacentAreas(List<NavArea> adjVector) {
		for (int dir = 0; dir < (int)NavDirType.NumDirections; dir++) {
			foreach (NavConnect connect in Connect[dir]) {
				if (connect.Area != this)
					adjVector.Add(connect.Area!);
			}
		}
	}

	public List<NavConnect> GetAdjacentAreas(NavDirType dir) => Connect[(int)dir];

	public void ComputePortal(NavArea to, NavDirType dir, ref Vector3 center, out float halfWidth) {
		if (dir == NavDirType.North || dir == NavDirType.South) {
			center.Y = dir == NavDirType.North ? NWCorner.Y : SECorner.Y;

			float left = Math.Max(NWCorner.X, to.NWCorner.X);
			float right = Math.Min(SECorner.X, to.SECorner.X);

			if (left < NWCorner.X)
				left = NWCorner.X;
			else if (left > SECorner.X)
				left = SECorner.X;

			if (right < NWCorner.X)
				right = NWCorner.X;
			else if (right > SECorner.X)
				right = SECorner.X;

			center.X = (left + right) / 2.0f;
			halfWidth = (right - left) / 2.0f;
		}
		else {
			center.X = dir == NavDirType.West ? NWCorner.X : SECorner.X;

			float top = Math.Max(NWCorner.Y, to.NWCorner.Y);
			float bottom = Math.Min(SECorner.Y, to.SECorner.Y);

			if (top < NWCorner.Y)
				top = NWCorner.Y;
			else
				if (top > SECorner.Y)
					top = SECorner.Y;

			if (bottom < NWCorner.Y)
				bottom = NWCorner.Y;
			else if (bottom > SECorner.Y)
				bottom = SECorner.Y;

			center.Y = (top + bottom) / 2.0f;
			halfWidth = (bottom - top) / 2.0f;
		}

		center.Z = GetZ(center.X, center.Y);
	}

	public NavDirType ComputeLargestPortal(NavArea to, out Vector3 center, out float halfWidth) {
		throw new NotImplementedException();
	}

	void ComputeClosestPointInPortal(NavArea to, NavDirType dir, Vector3 fromPos, Vector3 closePos) { }

	bool IsContiguous(NavArea other) {
		NavDirType dir;
		for (dir = NavDirType.North; dir <= NavDirType.West; dir++) {
			if (IsConnected(other, dir))
				return true;
		}

		if (dir == NavDirType.NumDirections)
			return false;

		Vector3 myEdge = default;
		ComputePortal(other, dir, ref myEdge, out _);

		Vector3 otherEdge = default;
		other.ComputePortal(this, OppositeDirection(dir), ref otherEdge, out _);

		return (myEdge - otherEdge).LengthSquared() < (StepHeight * StepHeight);
	}

	public float ComputeAdjacentConnectionHeightChange(NavArea destinationArea) {
		NavDirType dir;
		for (dir = NavDirType.North; dir <= NavDirType.West; dir++) {
			if (IsConnected(destinationArea, dir))
				break;
		}

		if (dir == NavDirType.NumDirections)
			return float.MaxValue;

		Vector3 myEdge = default;
		ComputePortal(destinationArea, dir, ref myEdge, out _);

		Vector3 otherEdge = default;
		destinationArea.ComputePortal(this, OppositeDirection(dir), ref otherEdge, out _);

		return otherEdge.Z - myEdge.Z;
	}

	bool IsEdge(NavDirType dir) {
		foreach (NavConnect connect in Connect[(int)dir]) {
			if (connect.Area != null && connect.Area.IsConnected(this, OppositeDirection(dir)))
				return false;
		}

		return true;
	}

	NavDirType ComputeDirection(Vector3 point) {
		if (point.X >= NWCorner.X && point.X <= SECorner.X) {
			if (point.Y < NWCorner.Y)
				return NavDirType.North;
			else if (point.Y > SECorner.Y)
				return NavDirType.South;
		}
		else if (point.Y >= NWCorner.Y && point.Y <= SECorner.Y) {
			if (point.X < NWCorner.X)
				return NavDirType.West;
			else if (point.X > SECorner.X)
				return NavDirType.East;
		}

		Vector3 to = point - Center;

		if (Math.Abs(to.X) > Math.Abs(to.Y)) {
			if (to.X > 0.0f)
				return NavDirType.East;
			return NavDirType.West;
		}
		else {
			if (to.Y > 0.0f)
				return NavDirType.South;
			return NavDirType.North;
		}
	}

	bool GetCornerHotspot(NavCornerType corner, out Vector3[] hotspot) {
		hotspot = new Vector3[4];
		Vector3 nw = NWCorner;
		Vector3 ne = new(NWCorner.X, SECorner.Y, NEZ);
		Vector3 sw = new(SECorner.X, NWCorner.Y, SWZ);
		Vector3 se = SECorner;

		float size = 9.0f;
		size = MathF.Min(size, GetSizeX() / 3);
		size = MathF.Min(size, GetSizeY() / 3);

		switch (corner) {
			case NavCornerType.NorthWest:
				hotspot[0] = nw;
				hotspot[1] = hotspot[0] + new Vector3(size, 0, 0);
				hotspot[2] = hotspot[0] + new Vector3(size, size, 0);
				hotspot[3] = hotspot[0] + new Vector3(0, size, 0);
				break;
			case NavCornerType.NorthEast:
				hotspot[0] = ne;
				hotspot[1] = hotspot[0] + new Vector3(-size, 0, 0);
				hotspot[2] = hotspot[0] + new Vector3(-size, size, 0);
				hotspot[3] = hotspot[0] + new Vector3(0, size, 0);
				break;
			case NavCornerType.SouthWest:
				hotspot[0] = sw;
				hotspot[1] = hotspot[0] + new Vector3(size, 0, 0);
				hotspot[2] = hotspot[0] + new Vector3(size, -size, 0);
				hotspot[3] = hotspot[0] + new Vector3(0, -size, 0);
				break;
			case NavCornerType.SouthEast:
				hotspot[0] = se;
				hotspot[1] = hotspot[0] + new Vector3(-size, 0, 0);
				hotspot[2] = hotspot[0] + new Vector3(-size, -size, 0);
				hotspot[3] = hotspot[0] + new Vector3(0, -size, 0);
				break;
			default:
				return false;
		}

		for (int i = 0; i < (int)NavCornerType.NumCorners; i++)
			hotspot[i].Z = GetZ(hotspot[i].X, hotspot[i].Y);

		NavMesh.Instance!.GetEditVectors(out Vector3 eyePos, out Vector3 eyeFoward);

		Source.Common.Ray ray = new();
		ray.Init(eyePos, eyePos + 10000.0f * eyeFoward, Vector3.Zero, Vector3.Zero);

		float dist = CollisionUtils.IntersectRayWithTriangle(ray, hotspot[0], hotspot[1], hotspot[2], false);
		if (dist > 0)
			return true;

		dist = CollisionUtils.IntersectRayWithTriangle(ray, hotspot[2], hotspot[3], hotspot[0], false);

		return dist > 0;
	}

	NavCornerType GetCornerUnderCursor() {
		NavMesh.Instance!.GetEditVectors(out Vector3 eyePos, out Vector3 eyeFoward);

		for (int i = 0; i < (int)NavCornerType.NumCorners; i++) {
			if (GetCornerHotspot((NavCornerType)i, out _))
				return (NavCornerType)i;
		}

		return NavCornerType.NumCorners;
	}

	static IntervalTimer? blink;
	static bool blinkOn = false;
	public void Draw() {
		NavEditColor color;
		bool useAttributeColors = true;

		const float DebugDuration = (float)IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER;

		if (NavMesh.Instance!.IsEditMode(NavMesh.EditModeType.PlacePainting)) {
			useAttributeColors = false;

			if (Place == UndefinedPlace)
				color = NavEditColor.NavNoPlaceColor;
			else if (NavMesh.Instance!.GetNavPlace() == Place)
				color = NavEditColor.NavSamePlaceColor;
			else
				color = NavEditColor.NavDifferentPlaceColor;
		}
		else {
			// normal edit mode
			if (this == NavMesh.Instance!.GetMarkedArea()) {
				useAttributeColors = false;
				color = NavEditColor.NavMarkedColor;
			}
			else if (this == NavMesh.Instance!.GetSelectedArea())
				color = NavEditColor.NavSelectedColor;
			else
				color = NavEditColor.NavNormalColor;
		}

		if (IsDegenerate()) {
			blink ??= new();
			if (blink.GetElapsedTime() > 1.0f) {
				blink.Reset();
				blinkOn = !blinkOn;
			}

			useAttributeColors = false;

			if (blinkOn)
				color = NavEditColor.NavDegenerateFirstColor;
			else
				color = NavEditColor.NavDegenerateSecondColor;

			Shared.DebugOverlay.Text(Center, $"Degenerate area {ID}", true, DebugDuration);
		}

		Vector3 nw, ne, sw, se;

		nw = NWCorner;
		se = SECorner;
		ne.X = se.X;
		ne.Y = nw.Y;
		ne.Z = NEZ;
		sw.X = nw.X;
		sw.Y = se.Y;
		sw.Z = SWZ;

		if (nav_show_light_intensity.GetBool()) {
			for (int i = 0; i < (int)NavCornerType.NumCorners; ++i) {
				Vector3 pos = GetCorner((NavCornerType)i);
				Vector3 end = pos;
				float lightIntensity = GetLightIntensity(pos);
				end.Z += HumanHeight * lightIntensity;
				lightIntensity *= 255;
				Shared.DebugOverlay.Line(end, pos, (int)lightIntensity, (int)lightIntensity, Math.Max(192, (int)lightIntensity), true, DebugDuration);
			}
		}

		int[] bgcolor = new int[4];
		ScanF scan = new ScanF(nav_area_bgcolor.GetString(), "%d %d %d %d").Read(out bgcolor[0]).Read(out bgcolor[1]).Read(out bgcolor[2]).Read(out bgcolor[3]);
		if (scan.ReadArguments == 4) {
			for (int i = 0; i < 4; ++i)
				bgcolor[i] = Math.Clamp(bgcolor[i], 0, 255);

			if (bgcolor[3] > 0) {
				Vector3 offset = new(0, 0, 0.8f);
				Shared.DebugOverlay.Triangle(nw + offset, se + offset, ne + offset, bgcolor[0], bgcolor[1], bgcolor[2], bgcolor[3], true, DebugDuration);
				Shared.DebugOverlay.Triangle(se + offset, nw + offset, sw + offset, bgcolor[0], bgcolor[1], bgcolor[2], bgcolor[3], true, DebugDuration);
			}
		}

		const float inset = 0.2f;
		nw.X += inset;
		nw.Y += inset;
		ne.X -= inset;
		ne.Y += inset;
		sw.X += inset;
		sw.Y -= inset;
		se.X -= inset;
		se.Y -= inset;

		if ((GetAttributes() & NavAttributeType.Transient) != 0) {
			NavDrawDashedLine(nw, ne, color);
			NavDrawDashedLine(ne, se, color);
			NavDrawDashedLine(se, sw, color);
			NavDrawDashedLine(sw, nw, color);
		}
		else {
			NavDrawLine(nw, ne, color);
			NavDrawLine(ne, se, color);
			NavDrawLine(se, sw, color);
			NavDrawLine(sw, nw, color);
		}

		if (this == NavMesh.Instance!.GetMarkedArea() && NavMesh.Instance!.MarkedCorner != NavCornerType.NumCorners) {
			Vector3[] p = new Vector3[(int)NavCornerType.NumCorners];
			GetCornerHotspot(NavMesh.Instance!.MarkedCorner, out p);

			NavDrawLine(in p[1], in p[2], NavEditColor.NavMarkedColor);
			NavDrawLine(in p[2], in p[3], NavEditColor.NavMarkedColor);
		}
		if (this != NavMesh.Instance!.GetMarkedArea() && this == NavMesh.Instance!.GetSelectedArea() && NavMesh.Instance!.IsEditMode(NavMesh.EditModeType.Normal)) {
			NavCornerType bestCorner = GetCornerUnderCursor();

			if (GetCornerHotspot(bestCorner, out Vector3[] p)) {
				NavDrawLine(p[1], p[2], NavEditColor.NavSelectedColor);
				NavDrawLine(p[2], p[3], NavEditColor.NavSelectedColor);
			}
		}

		if ((GetAttributes() & NavAttributeType.Crouch) != 0) {
			if (useAttributeColors)
				color = NavEditColor.NavAttributeCrouchColor;

			NavDrawLine(nw, se, color);
		}

		if ((GetAttributes() & NavAttributeType.Jump) != 0) {
			if (useAttributeColors)
				color = NavEditColor.NavAttributeJumpColor;

			if ((GetAttributes() & NavAttributeType.Crouch) == 0) {
				NavDrawLine(nw, se, color);
			}
			NavDrawLine(ne, sw, color);
		}

		if ((GetAttributes() & NavAttributeType.Precice) != 0) {
			if (useAttributeColors)
				color = NavEditColor.NavAttributePreciseColor;

			float size = 8.0f;
			Vector3 up = new(Center.X, Center.Y - size, Center.Z);
			Vector3 down = new(Center.X, Center.Y + size, Center.Z);
			NavDrawLine(up, down, color);

			Vector3 left = new(Center.X - size, Center.Y, Center.Z);
			Vector3 right = new(Center.X + size, Center.Y, Center.Z);
			NavDrawLine(left, right, color);
		}

		if ((GetAttributes() & NavAttributeType.NoJump) != 0) {
			if (useAttributeColors)
				color = NavEditColor.NavAttributeNoJumpColor;

			float size = 8.0f;
			Vector3 up = new(Center.X, Center.Y - size, Center.Z);
			Vector3 down = new(Center.X, Center.Y + size, Center.Z);
			Vector3 left = new(Center.X - size, Center.Y, Center.Z);
			Vector3 right = new(Center.X + size, Center.Y, Center.Z);
			NavDrawLine(up, right, color);
			NavDrawLine(right, down, color);
			NavDrawLine(down, left, color);
			NavDrawLine(left, up, color);
		}

		if ((GetAttributes() & NavAttributeType.Stairs) != 0) {
			if (useAttributeColors)
				color = NavEditColor.NavAttributeStairColor;

			float northZ = (GetCorner(NavCornerType.NorthWest).Z + GetCorner(NavCornerType.NorthEast).Z) / 2.0f;
			float southZ = (GetCorner(NavCornerType.SouthWest).Z + GetCorner(NavCornerType.SouthEast).Z) / 2.0f;
			float westZ = (GetCorner(NavCornerType.NorthWest).Z + GetCorner(NavCornerType.SouthWest).Z) / 2.0f;
			float eastZ = (GetCorner(NavCornerType.NorthEast).Z + GetCorner(NavCornerType.SouthEast).Z) / 2.0f;

			float deltaEastWest = Math.Abs(westZ - eastZ);
			float deltaNorthSouth = Math.Abs(northZ - southZ);

			float stepSize = StepHeight / 2.0f;
			float t;

			if (deltaEastWest > deltaNorthSouth) {
				float inc = stepSize / GetSizeX();

				for (t = 0.0f; t <= 1.0f; t += inc) {
					float x = NWCorner.X + t * GetSizeX();
					NavDrawLine(new Vector3(x, NWCorner.Y, GetZ(x, NWCorner.Y)), new Vector3(x, SECorner.Y, GetZ(x, SECorner.Y)), color);
				}
			}
			else {
				float inc = stepSize / GetSizeY();

				for (t = 0.0f; t <= 1.0f; t += inc) {
					float y = NWCorner.Y + t * GetSizeY();
					NavDrawLine(new Vector3(NWCorner.X, y, GetZ(NWCorner.X, y)), new Vector3(SECorner.X, y, GetZ(SECorner.X, y)), color);
				}
			}
		}

		if ((GetAttributes() & NavAttributeType.Stop) != 0) {
			if (useAttributeColors)
				color = NavEditColor.NavAttributeStopColor;

			float dist = 8.0f;
			float length = dist / 2.5f;
			Vector3 start, end;

			start = Center + new Vector3(dist, -length, 0);
			end = Center + new Vector3(dist, length, 0);
			NavDrawLine(start, end, color);

			start = Center + new Vector3(dist, length, 0);
			end = Center + new Vector3(length, dist, 0);
			NavDrawLine(start, end, color);

			start = Center + new Vector3(-dist, -length, 0);
			end = Center + new Vector3(-dist, length, 0);
			NavDrawLine(start, end, color);

			start = Center + new Vector3(-dist, length, 0);
			end = Center + new Vector3(-length, dist, 0);
			NavDrawLine(start, end, color);

			start = Center + new Vector3(-length, dist, 0);
			end = Center + new Vector3(length, dist, 0);
			NavDrawLine(start, end, color);

			start = Center + new Vector3(-dist, -length, 0);
			end = Center + new Vector3(-length, -dist, 0);
			NavDrawLine(start, end, color);

			start = Center + new Vector3(-length, -dist, 0);
			end = Center + new Vector3(length, -dist, 0);
			NavDrawLine(start, end, color);

			start = Center + new Vector3(length, -dist, 0);
			end = Center + new Vector3(dist, -length, 0);
			NavDrawLine(start, end, color);
		}

		if ((GetAttributes() & NavAttributeType.Walk) != 0) {
			if (useAttributeColors)
				color = NavEditColor.NavAttributeWalkColor;

			float size = 8.0f;
			NavDrawHorizontalArrow(Center + new Vector3(-size, 0, 0), Center + new Vector3(size, 0, 0), 4, color);
		}

		if ((GetAttributes() & NavAttributeType.Run) != 0) {
			if (useAttributeColors)
				color = NavEditColor.NavAttributeRunColor;

			float size = 8.0f;
			float dist = 4.0f;
			NavDrawHorizontalArrow(Center + new Vector3(-size, dist, 0), Center + new Vector3(size, dist, 0), 4, color);
			NavDrawHorizontalArrow(Center + new Vector3(-size, -dist, 0), Center + new Vector3(size, -dist, 0), 4, color);
		}

		if ((GetAttributes() & NavAttributeType.Avoid) != 0) {
			if (useAttributeColors)
				color = NavEditColor.NavAttributeAvoidColor;

			float topHeight = 8.0f;
			float topWidth = 3.0f;
			float bottomHeight = 3.0f;
			float bottomWidth = 2.0f;
			NavDrawTriangle(Center, Center + new Vector3(-topWidth, topHeight, 0), Center + new Vector3(+topWidth, topHeight, 0), color);
			NavDrawTriangle(Center + new Vector3(0, -bottomHeight, 0), Center + new Vector3(-bottomWidth, -bottomHeight * 2, 0), Center + new Vector3(bottomWidth, -bottomHeight * 2, 0), color);
		}

		if (IsBlocked(Constants.TEAM_ANY) || HasAvoidanceObstacle() || IsDamaging()) {
			NavEditColor clr = (IsBlocked(Constants.TEAM_ANY) && (AttributeFlags & NavAttributeType.NavBlocker) != 0) ? NavEditColor.NavBlockedByFuncNavBlockerColor : NavEditColor.NavBlockedByDoorColor;
			const float blockedInset = 4.0f;
			nw.X += blockedInset;
			nw.Y += blockedInset;
			ne.X -= blockedInset;
			ne.Y += blockedInset;
			sw.X += blockedInset;
			sw.Y -= blockedInset;
			se.X -= blockedInset;
			se.Y -= blockedInset;
			NavDrawLine(nw, ne, clr);
			NavDrawLine(ne, se, clr);
			NavDrawLine(se, sw, clr);
			NavDrawLine(sw, nw, clr);
		}
	}

	public void DrawFilled(int r, int g, int b, int a, float deltaT = 0.1f, bool noDepthTest = true, float margin = 5.0f) {
		Vector3 nw = GetCorner(NavCornerType.NorthWest) + new Vector3(margin, margin, 0);
		Vector3 ne = GetCorner(NavCornerType.NorthEast) + new Vector3(-margin, margin, 0);
		Vector3 sw = GetCorner(NavCornerType.SouthWest) + new Vector3(margin, -margin, 0);
		Vector3 se = GetCorner(NavCornerType.SouthEast) + new Vector3(-margin, -margin, 0);

		if (a == 0) {
			Shared.DebugOverlay.Line(nw, ne, r, g, b, true, deltaT);
			Shared.DebugOverlay.Line(nw, sw, r, g, b, true, deltaT);
			Shared.DebugOverlay.Line(sw, se, r, g, b, true, deltaT);
			Shared.DebugOverlay.Line(se, ne, r, g, b, true, deltaT);
		}
		else {
			Shared.DebugOverlay.Triangle(nw, se, ne, r, g, b, a, noDepthTest, deltaT);
			Shared.DebugOverlay.Triangle(se, nw, sw, r, g, b, a, noDepthTest, deltaT);
		}
	}

	public void DrawSelectedSet(Vector3 shift) {
		const float deltaT = (float)IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER;
		int r = SelectedSetColor.R;
		int g = SelectedSetColor.G;
		int b = SelectedSetColor.B;
		int a = SelectedSetColor.A;

		Vector3 nw = GetCorner(NavCornerType.NorthWest) + shift;
		Vector3 ne = GetCorner(NavCornerType.NorthEast) + shift;
		Vector3 sw = GetCorner(NavCornerType.SouthWest) + shift;
		Vector3 se = GetCorner(NavCornerType.SouthEast) + shift;

		Shared.DebugOverlay.Triangle(nw, se, ne, r, g, b, a, true, deltaT);
		Shared.DebugOverlay.Triangle(se, nw, sw, r, g, b, a, true, deltaT);

		r = SelectedSetBorderColor.R;
		g = SelectedSetBorderColor.G;
		b = SelectedSetBorderColor.B;
		a = SelectedSetBorderColor.A;
		Shared.DebugOverlay.Line(nw, ne, r, g, b, true, deltaT);
		Shared.DebugOverlay.Line(nw, sw, r, g, b, true, deltaT);
		Shared.DebugOverlay.Line(sw, se, r, g, b, true, deltaT);
		Shared.DebugOverlay.Line(se, ne, r, g, b, true, deltaT);
	}

	public void DrawDragSelectionSet(Color dragSelectionSetColor) {
		const float deltaT = (float)IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER;
		int r = dragSelectionSetColor.R;
		int g = dragSelectionSetColor.G;
		int b = dragSelectionSetColor.B;
		int a = dragSelectionSetColor.A;

		Vector3 nw = GetCorner(NavCornerType.NorthWest);
		Vector3 ne = GetCorner(NavCornerType.NorthEast);
		Vector3 sw = GetCorner(NavCornerType.SouthWest);
		Vector3 se = GetCorner(NavCornerType.SouthEast);

		Shared.DebugOverlay.Triangle(nw, se, ne, r, g, b, a, true, deltaT);
		Shared.DebugOverlay.Triangle(se, nw, sw, r, g, b, a, true, deltaT);

		r = DragSelectionSetBorderColor.R;
		g = DragSelectionSetBorderColor.G;
		b = DragSelectionSetBorderColor.B;
		Shared.DebugOverlay.Line(nw, ne, r, g, b, true, deltaT);
		Shared.DebugOverlay.Line(nw, sw, r, g, b, true, deltaT);
		Shared.DebugOverlay.Line(sw, se, r, g, b, true, deltaT);
		Shared.DebugOverlay.Line(se, ne, r, g, b, true, deltaT);
	}

	public void DrawHidingSpots() {
		foreach (HidingSpot spot in GetHidingSpots()) {
			NavEditColor color;

			if (spot.IsIdealSniperSpot())
				color = NavEditColor.NavIdealSniperColor;
			else if (spot.IsGoodSniperSpot())
				color = NavEditColor.NavGoodSniperColor;
			else if (spot.HasGoodCover())
				color = NavEditColor.NavGoodCoverColor;
			else
				color = NavEditColor.NavExposedColor;

			NavDrawLine(spot.GetPosition(), spot.GetPosition() + new Vector3(0, 0, 50), color);
		}
	}

	public void DrawConnectedAreas() {
		int i;

		BasePlayer? player = Util.GetListenServerHost();
		if (player == null)
			return;

		if (NavMesh.Instance!.IsEditMode(NavMesh.EditModeType.PlacePainting))
			Draw();
		else {
			Draw();
			DrawHidingSpots();
		}

		for (int it = 0; it < Ladder[(int)NavLadder.LadderDirectionType.Up].Count; it++) {
			NavLadder ladder = Ladder[(int)NavLadder.LadderDirectionType.Up][it].Ladder!;

			ladder.DrawLadder();

			if (!ladder.IsConnected(this, NavLadder.LadderDirectionType.Down))
				NavDrawLine(Center, ladder.Bottom, NavEditColor.NavConnectedOneWayColor);
		}

		for (int it = 0; it < Ladder[(int)NavLadder.LadderDirectionType.Down].Count; it++) {
			NavLadder ladder = Ladder[(int)NavLadder.LadderDirectionType.Down][it].Ladder!;

			ladder.DrawLadder();

			if (!ladder.IsConnected(this, NavLadder.LadderDirectionType.Up))
				NavDrawLine(Center, ladder.Top, NavEditColor.NavConnectedOneWayColor);
		}

		for (i = 0; i < (int)NavDirType.NumDirections; ++i) {
			NavDirType dir = (NavDirType)i;

			int count = GetAdjacentCount(dir);

			for (int a = 0; a < count; ++a) {
				NavArea adj = GetAdjacentArea(dir, a)!;

				adj.Draw();

				if (!NavMesh.Instance!.IsEditMode(NavMesh.EditModeType.PlacePainting)) {
					adj.DrawHidingSpots();

					Vector3 from = default, to = default;
					Vector3 hookPos = default;
					float size = 5.0f;
					ComputePortal(adj, dir, ref hookPos, out float halfWidth);

					switch (dir) {
						case NavDirType.North:
							from = hookPos + new Vector3(0.0f, size, 0.0f);
							to = hookPos + new Vector3(0.0f, -size, 0.0f);
							break;
						case NavDirType.South:
							from = hookPos + new Vector3(0.0f, -size, 0.0f);
							to = hookPos + new Vector3(0.0f, size, 0.0f);
							break;
						case NavDirType.East:
							from = hookPos + new Vector3(-size, 0.0f, 0.0f);
							to = hookPos + new Vector3(+size, 0.0f, 0.0f);
							break;
						case NavDirType.West:
							from = hookPos + new Vector3(size, 0.0f, 0.0f);
							to = hookPos + new Vector3(-size, 0.0f, 0.0f);
							break;
					}

					from.Z = GetZ(from);
					to.Z = adj.GetZ(to);

					adj.GetClosestPointOnArea(ref to, out Vector3 drawTo);

					if (nav_show_contiguous.GetBool()) {
						if (IsContiguous(adj))
							NavDrawLine(from, drawTo, NavEditColor.NavConnectedContiguous);
						else
							NavDrawLine(from, drawTo, NavEditColor.NavConnectedNonContiguous);
					}
					else {
						if (adj.IsConnected(this, OppositeDirection(dir)))
							NavDrawLine(from, drawTo, NavEditColor.NavConnectedTwoWaysColor);
						else
							NavDrawLine(from, drawTo, NavEditColor.NavConnectedOneWayColor);
					}
				}
			}
		}
	}

	public void AddToOpenList() {
		Assert((OpenList != null && OpenList.PrevOpen == null) || OpenList == null);

		if (IsOpen())
			return;

		OpenMarker = MasterMarker;

		if (OpenList == null) {
			OpenList = this;
			OpenListTail = this;
			PrevOpen = null;
			NextOpen = null;
			return;
		}

		NavArea? area, last = null;

		for (area = OpenList; area != null; area = area.NextOpen) {
			if (GetTotalCost() < area.GetTotalCost())
				break;

			last = area;
		}

		if (area != null) {
			PrevOpen = area.PrevOpen;

			if (PrevOpen != null)
				PrevOpen.NextOpen = this;
			else
				OpenList = this;

			NextOpen = area;
			area.PrevOpen = this;
		}
		else {
			last!.NextOpen = this;
			PrevOpen = last;

			NextOpen = null;

			OpenListTail = this;
		}

		Assert((OpenList != null && OpenList.PrevOpen == null) || OpenList == null);
	}

	void AddToOpenListTail() { }

	public void UpdateOnOpenList() {
		while (PrevOpen != null && GetTotalCost() < PrevOpen.GetTotalCost()) {
			NavArea other = PrevOpen;
			NavArea? before = other.PrevOpen;
			NavArea? after = NextOpen;

			NextOpen = other;
			PrevOpen = before;

			other.PrevOpen = this;
			other.NextOpen = after;

			if (before != null)
				before.NextOpen = this;
			else
				OpenList = this;

			if (after != null)
				after.PrevOpen = other;
			else
				OpenListTail = other;
		}
	}

	void RemoveFromOpenList() {
		if (!IsOpen())
			return;

		if (PrevOpen != null)
			PrevOpen.NextOpen = NextOpen;
		else
			OpenList = NextOpen;

		if (NextOpen != null)
			NextOpen.PrevOpen = PrevOpen;
		else
			OpenListTail = PrevOpen;

		OpenMarker = 0;
	}

	public static void ClearSearchLists() {
		MakeNewMarker();
		OpenList = null;
		OpenListTail = null;
	}

	public void SetTotalCost(float value) {
		Assert(value >= 0);
		TotalCost = value;
	}

	public float GetTotalCost() => TotalCost;

	public void SetCostSoFar(float value) {
		Assert(value >= 0);
		CostSoFar = value;
	}

	public float GetCostSoFar() => CostSoFar;

	public void SetPathLengthSoFar(float value) {
		Assert(value >= 0);
		PathLengthSoFar = value;
	}

	float GetPathLengthSoFar() => PathLengthSoFar;

	void SetCorner(NavCornerType corner, Vector3 newPosition) {
		switch (corner) {
			case NavCornerType.NorthWest:
				NWCorner = newPosition;
				break;
			case NavCornerType.NorthEast:
				SECorner.X = newPosition.X;
				NWCorner.Y = newPosition.Y;
				NEZ = newPosition.Z;
				break;
			case NavCornerType.SouthWest:
				NWCorner.X = newPosition.X;
				SECorner.Y = newPosition.Y;
				SWZ = newPosition.Z;
				break;
			case NavCornerType.SouthEast:
				SECorner = newPosition;
				break;
			default: {
					Vector3 oldPosition = Center;
					Vector3 delta = newPosition - oldPosition;
					NWCorner += delta;
					SECorner += delta;
					NEZ += delta.Z;
					SWZ += delta.Z;
				}
				break;
		}

		Center.X = (NWCorner.X + SECorner.X) / 2.0f;
		Center.Y = (NWCorner.Y + SECorner.Y) / 2.0f;
		Center.Z = (NWCorner.Z + SECorner.Z) / 2.0f;

		if ((SECorner.X - NWCorner.X) > 0.0f && (SECorner.Y - NWCorner.Y) > 0.0f) {
			InvDXCorners = 1.0f / (SECorner.X - NWCorner.X);
			InvDYCorners = 1.0f / (SECorner.Y - NWCorner.Y);
		}
		else {
			InvDXCorners = 0;
			InvDYCorners = 0;
		}

		CalcDebugID();
	}

	bool IsHidingSpotCollision(Vector3 pos) {
		const float collisionRange = 30.0f;

		foreach (HidingSpot spot in HidingSpots) {
			if ((spot.GetPosition() - pos).Length() <= collisionRange)
				return true;
		}

		return false;
	}

	bool IsHidingSpotInCover(Vector3 spot) {
		int coverCount = 0;

		Vector3 from = spot;
		from.Z += HalfHumanHeight;

		Vector3 to = from + new Vector3(0, 0, 20.0f);
		Util.TraceLine(from, to, Mask.NPCSolidBrushOnly, null, CollisionGroup.None, out Trace result);
		if (result.Fraction != 1.0f)
			return true;

		const float coverRange = 100.0f;
		const float inc = (float)(Math.PI / 8.0f);

		for (float angle = 0.0f; angle < 2.0f * Math.PI; angle += inc) {
			to = from + new Vector3(coverRange * (float)Math.Cos(angle), coverRange * (float)Math.Sin(angle), HalfHumanHeight);

			Util.TraceLine(from, to, Mask.NPCSolidBrushOnly, null, CollisionGroup.None, out result);

			if (result.Fraction != 1.0f)
				++coverCount;
		}

		const int halfCover = 8;
		if (coverCount < halfCover)
			return false;

		return true;
	}

	static Vector3 FindPositionInArea(NavArea area, NavCornerType corner) {
		int multX = 1, multY = 1;
		switch (corner) {
			case NavCornerType.NorthWest:
				break;
			case NavCornerType.NorthEast:
				multX = -1;
				break;
			case NavCornerType.SouthWest:
				multY = -1;
				break;
			case NavCornerType.SouthEast:
				multX = -1;
				multY = -1;
				break;
		}

		const float offset = 12.5f;
		Vector3 cornerPos = area.GetCorner(corner);

		Vector3 pos = cornerPos + new Vector3(offset * multX, offset * multY, 0.0f);
		if (!area.IsOverlapping(pos)) {
			pos = cornerPos + new Vector3(offset * multX, area.GetSizeY() * 0.5f * multY, 0.0f);
			if (!area.IsOverlapping(pos)) {
				pos = cornerPos + new Vector3(area.GetSizeX() * 0.5f * multX, offset * multY, 0.0f);
				if (!area.IsOverlapping(pos)) {
					pos = cornerPos + new Vector3(area.GetSizeX() * 0.5f * multX, area.GetSizeY() * 0.5f * multY, 0.0f);
					if (!area.IsOverlapping(pos)) {
						AssertMsg(false, $"A Hiding Spot can't be placed on its area at ({cornerPos.X}, {cornerPos.Y}, {cornerPos.Z})");

						pos = cornerPos + new Vector3(1.0f * multX, 1.0f * multY, 0.0f);
						if (!area.IsOverlapping(pos))
							pos = cornerPos;
					}
				}
			}
		}

		return pos;
	}

	struct HidingSpotExtent
	{
		public float Lo;
		public float Hi;
	}

	public void ComputeHidingSpots() {
		HidingSpotExtent extent = new();

		HidingSpots.Clear();

		if ((GetAttributes() & NavAttributeType.Crouch) != 0)
			return;

		if ((GetAttributes() & NavAttributeType.DontHide) != 0)
			return;

		int[] cornerCount = new int[(int)NavCornerType.NumCorners];
		for (int i = 0; i < (int)NavCornerType.NumCorners; ++i)
			cornerCount[i] = 0;

		const float cornerSize = 20.0f;

		for (int d = 0; d < (int)NavDirType.NumDirections; ++d) {
			extent.Lo = 999999.9f;
			extent.Hi = -999999.9f;

			bool isHoriz = d == (int)NavDirType.North || d == (int)NavDirType.South;

			for (int it = 0; it < Connect[d].Count; it++) {
				NavConnect connect = Connect[d][it];

				if (connect.Area!.IsConnected(this, OppositeDirection((NavDirType)d)) == false)
					continue;

				if ((connect.Area.GetAttributes() & NavAttributeType.Jump) != 0)
					continue;

				if (isHoriz) {
					if (connect.Area.NWCorner.X < extent.Lo)
						extent.Lo = connect.Area.NWCorner.X;

					if (connect.Area.SECorner.X > extent.Hi)
						extent.Hi = connect.Area.SECorner.X;
				}
				else {
					if (connect.Area.NWCorner.Y < extent.Lo)
						extent.Lo = connect.Area.NWCorner.Y;

					if (connect.Area.SECorner.Y > extent.Hi)
						extent.Hi = connect.Area.SECorner.Y;
				}
			}

			switch (d) {
				case (int)NavDirType.North:
					if (extent.Lo - NWCorner.X >= cornerSize)
						++cornerCount[(int)NavCornerType.NorthWest];

					if (SECorner.X - extent.Hi >= cornerSize)
						++cornerCount[(int)NavCornerType.NorthEast];
					break;

				case (int)NavDirType.South:
					if (extent.Lo - NWCorner.X >= cornerSize)
						++cornerCount[(int)NavCornerType.SouthWest];

					if (SECorner.X - extent.Hi >= cornerSize)
						++cornerCount[(int)NavCornerType.SouthEast];
					break;

				case (int)NavDirType.East:
					if (extent.Lo - NWCorner.Y >= cornerSize)
						++cornerCount[(int)NavCornerType.NorthEast];

					if (SECorner.Y - extent.Hi >= cornerSize)
						++cornerCount[(int)NavCornerType.SouthEast];
					break;

				case (int)NavDirType.West:
					if (extent.Lo - NWCorner.Y >= cornerSize)
						++cornerCount[(int)NavCornerType.NorthWest];

					if (SECorner.Y - extent.Hi >= cornerSize)
						++cornerCount[(int)NavCornerType.SouthWest];
					break;
			}
		}

		for (int c = 0; c < (int)NavCornerType.NumCorners; ++c) {
			if (cornerCount[c] == 2) {
				Vector3 pos = FindPositionInArea(this, (NavCornerType)c);
				if (c == 0 || !IsHidingSpotCollision(pos)) {
					HidingSpot spot = NavMesh.CreateHidingSpot();
					spot.SetPosition(pos);
					spot.SetFlags(IsHidingSpotInCover(pos) ? HidingSpotFlags.InCover : HidingSpotFlags.Exposed);
					HidingSpots.Add(spot);
				}
			}
		}
	}

	void ClassifySniperSpot(HidingSpot spot) {
		Vector3 eye = spot.GetPosition();

		NavArea? hidingArea = NavMesh.Instance!.GetNavArea(spot.GetPosition());
		if (hidingArea != null && (hidingArea.GetAttributes() & NavAttributeType.Stand) != 0)
			eye.Z += HumanEyeHeight;
		else
			eye.Z += HumanCrouchEyeHeight;

		Vector3 walkable = default;

		Extent sniperExtent = default;
		float farthestRangeSq = 0.0f;
		const float minSniperRangeSq = 1000.0f * 1000.0f;
		bool found = false;

		sniperExtent.Lo = Vector3.Zero;
		sniperExtent.Hi = Vector3.Zero;

		Extent areaExtent = default;
		for (int it = 0; it < TheNavAreas.Count; it++) {
			NavArea area = TheNavAreas[it];

			area.GetExtent(ref areaExtent);

			for (walkable.Y = areaExtent.Lo.Y + GenerationStepSize / 2.0f; walkable.Y < areaExtent.Hi.Y; walkable.Y += GenerationStepSize) {
				for (walkable.X = areaExtent.Lo.X + GenerationStepSize / 2.0f; walkable.X < areaExtent.Hi.X; walkable.X += GenerationStepSize) {
					walkable.Z = area.GetZ(walkable) + HalfHumanHeight;

					Util.TraceLine(eye, walkable, (Mask)(Contents.Solid | Contents.Moveable | Contents.PlayerClip), null, CollisionGroup.None, out Trace result);

					if (result.Fraction == 1.0f && !result.StartSolid) {
						float rangeSq = (eye - walkable).LengthSquared();
						if (rangeSq > farthestRangeSq) {
							farthestRangeSq = rangeSq;

							if (rangeSq >= minSniperRangeSq) {
								if (found) {
									if (walkable.X < sniperExtent.Lo.X)
										sniperExtent.Lo.X = walkable.X;
									if (walkable.X > sniperExtent.Hi.X)
										sniperExtent.Hi.X = walkable.X;

									if (walkable.Y < sniperExtent.Lo.Y)
										sniperExtent.Lo.Y = walkable.Y;
									if (walkable.Y > sniperExtent.Hi.Y)
										sniperExtent.Hi.Y = walkable.Y;
								}
								else {
									sniperExtent.Lo = walkable;
									sniperExtent.Hi = walkable;
									found = true;
								}
							}
						}
					}
				}
			}
		}

		if (found) {
			float snipableArea = sniperExtent.Area();

			const float minIdealSniperArea = 200.0f * 200.0f;
			const float longSniperRangeSq = 1500.0f * 1500.0f;

			if (snipableArea >= minIdealSniperArea || farthestRangeSq >= longSniperRangeSq)
				spot.Flags |= (byte)HidingSpotFlags.IdealSniperSpot;
			else
				spot.Flags |= (byte)HidingSpotFlags.GoodSniperSpot;
		}
	}

	public void ComputeSniperSpots() {
		if (nav_quicksave.GetBool())
			return;

		foreach (HidingSpot spot in HidingSpots)
			ClassifySniperSpot(spot);
	}

	SpotEncounter? GetSpotEncounter(NavArea from, NavArea to) {
		if (from == null || to == null)
			return null;

		foreach (SpotEncounter e in SpotEncounters) {
			if (e.From.Area == from && e.To.Area == to)
				return e;
		}

		return null;
	}

	void AddSpotEncounters(NavArea from, NavDirType fromDir, NavArea to, NavDirType toDir) {
		SpotEncounter e = new();

		e.From.Area = from;
		e.FromDir = fromDir;

		e.To.Area = to;
		e.ToDir = toDir;

		ComputePortal(to, toDir, ref e.Path.To, out float halfWidth);
		ComputePortal(from, fromDir, ref e.Path.From, out halfWidth);

		const float eyeHeight = HumanEyeHeight;
		e.Path.From.Z = from.GetZ(e.Path.From) + eyeHeight;
		e.Path.To.Z = to.GetZ(e.Path.To) + eyeHeight;

		Vector3 dir = e.Path.To - e.Path.From;
		float length = dir.NormalizeInPlace();

		HidingSpot.ChangeMasterMarker();

		const float stepSize = 25.0f;
		const float seeSpotRange = 2000.0f;

		Vector3 eye, delta;
		HidingSpot spot;
		SpotOrder spotOrder = default;

		bool done = false;
		for (float along = 0.0f; !done; along += stepSize) {
			if (along >= length) {
				along = length;
				done = true;
			}

			eye = e.Path.From + along * dir;

			for (int it = 0; it < HidingSpot.TheHidingSpots.Count; it++) {
				spot = HidingSpot.TheHidingSpots[it];

				if (!spot.HasGoodCover())
					continue;

				if (spot.IsMarked())
					continue;

				Vector3 spotPos = spot.GetPosition();

				delta.X = spotPos.X - eye.X;
				delta.Y = spotPos.Y - eye.Y;
				delta.Z = spotPos.Z + eyeHeight - eye.Z;

				if (delta.LengthSquared() > seeSpotRange * seeSpotRange)
					continue;

				Util.TraceLine(eye, new Vector3(spotPos.X, spotPos.Y, spotPos.Z + eyeHeight), Mask.NPCSolidBrushOnly, null, CollisionGroup.None, out Trace result);
				if (result.Fraction != 1.0f)
					continue;

				delta.NormalizeInPlace();
				float dot = MathLib.DotProduct(dir, delta);
				if (dot < 0.7071f && dot > -0.7071f) {
					if (along > 0.0f) {
						spotOrder.Spot = spot;
						spotOrder.T = along / length;
						e.Spots.Add(spotOrder);
					}
				}

				spot.Mark();
			}
		}

		SpotEncounters.Add(e);
	}

	public void ComputeSpotEncounters() {
		SpotEncounters.Clear();

		for (int fromDir = 0; fromDir < (int)NavDirType.NumDirections; fromDir++) {
			foreach (NavConnect fromConnect in Connect[fromDir]) {
				for (int toDir = 0; toDir < (int)NavDirType.NumDirections; toDir++) {
					foreach (NavConnect toConnect in Connect[toDir]) {
						if (fromConnect.Area == toConnect.Area)
							continue;

						AddSpotEncounters(fromConnect.Area!, (NavDirType)fromDir, toConnect.Area!, (NavDirType)toDir);
					}
				}
			}
		}
	}

	void DecayDanger() {
		for (int i = 0; i < MAX_NAV_TEAMS; i++) {
			TimeUnit_t deltaT = gpGlobals.CurTime - DangerTimestamp[i];
			float decayAmount = (float)(GetDangerDecayRate() * deltaT);

			Danger[i] -= decayAmount;
			if (Danger[i] < 0.0f)
				Danger[i] = 0.0f;

			DangerTimestamp[i] = gpGlobals.CurTime;
		}
	}

	void IncreaseDanger(int teamID, float amount) {
		DecayDanger();

		int teamIdx = teamID % MAX_NAV_TEAMS;
		Danger[teamIdx] += amount;
		DangerTimestamp[teamIdx] = gpGlobals.CurTime;
	}

	public float GetDanger(int teamID) {
		DecayDanger();

		int teamIdx = teamID % MAX_NAV_TEAMS;
		return Danger[teamIdx];
	}

	float GetLightIntensity(Vector3 pos) {
		Vector3 testPos;
		testPos.X = Math.Clamp(pos.X, NWCorner.X, SECorner.X);
		testPos.Y = Math.Clamp(pos.Y, NWCorner.Y, SECorner.Y);
		testPos.Z = pos.Z;

		float dX = (testPos.X - NWCorner.X) * InvDXCorners;
		float dY = (testPos.Y - NWCorner.Y) * InvDYCorners;

		float northLight = LightIntensity[(int)NavCornerType.NorthWest] * (1 - dX) + LightIntensity[(int)NavCornerType.NorthEast] * dX;
		float southLight = LightIntensity[(int)NavCornerType.SouthWest] * (1 - dX) + LightIntensity[(int)NavCornerType.SouthEast] * dX;
		float light = northLight * (1 - dY) + southLight * dY;

		return light;
	}

	float GetLightIntensity(float x, float y) => GetLightIntensity(new Vector3(x, y, 0));

	float GetLightIntensity() {
		float light = LightIntensity[(int)NavCornerType.NorthWest];
		light += LightIntensity[(int)NavCornerType.NorthEast];
		light += LightIntensity[(int)NavCornerType.SouthWest];
		light += LightIntensity[(int)NavCornerType.SouthEast];
		return light / 4.0f;
	}

	public static void MakeNewMarker() {
		++MasterMarker;
		if (MasterMarker == 0)
			MasterMarker = 1;
	}

	public void Mark() => Marker = MasterMarker;

	public bool IsMarked() => Marker == MasterMarker;

	public void SetParent(NavArea? parent, NavTraverseType how = NavTraverseType.NumTraverseTypes) {
		Parent = parent;
		ParentHow = how;
	}

	public NavArea? GetParent() => Parent;

	public NavTraverseType GetParentHow() => ParentHow;

	public bool ComputeLighting() {
		if (engine.IsDedicatedServer()) {
			for (int i = 0; i < (int)NavCornerType.NumCorners; i++)
				LightIntensity[i] = 1.0f;

			return true;
		}

		for (int i = 0; i < (int)NavCornerType.NumCorners; i++) {
			Vector3 pos = FindPositionInArea(this, (NavCornerType)i);
			pos.Z = GetZ(pos.X, pos.Y) + HalfHumanHeight - StepHeight;

			if (NavMesh.Instance!.GetGroundHeight(pos, out float height, out _))
				pos.Z = height + HalfHumanHeight - StepHeight;

#pragma warning disable CS0162 // Unreachable code detected
			{
				return false;
			}
			Vector3 light = Vector3.Zero;
			Vector3 ambient = Vector3.Zero;

			float amientIntensity = ambient.X + ambient.Y + ambient.Z;
			float lightIntensity = light.X + light.Y + light.Z;
			lightIntensity = Math.Clamp(lightIntensity, 0.0f, 1.0f);
			lightIntensity = Math.Max(lightIntensity, amientIntensity);

			LightIntensity[i] = lightIntensity;
#pragma warning restore CS0162 // Unreachable code detected
		}

		return true;
	}

	[ConCommand("nav_update_lighting", "Recomputes lighting values", FCvar.Cheat)]
	static void nav_update_lighting(in TokenizedCommand args) {
		throw new NotImplementedException();
	}

	public void RaiseCorner(NavCornerType corner, int amount, bool raiseAdjacentCorners = true) {
		if (corner == NavCornerType.NumCorners) {
			RaiseCorner(NavCornerType.NorthWest, amount, raiseAdjacentCorners);
			RaiseCorner(NavCornerType.NorthEast, amount, raiseAdjacentCorners);
			RaiseCorner(NavCornerType.SouthWest, amount, raiseAdjacentCorners);
			RaiseCorner(NavCornerType.SouthEast, amount, raiseAdjacentCorners);
			return;
		}

		switch (corner) {
			case NavCornerType.NorthWest:
				NWCorner.Z += amount;
				break;
			case NavCornerType.NorthEast:
				NEZ += amount;
				break;
			case NavCornerType.SouthWest:
				SWZ += amount;
				break;
			case NavCornerType.SouthEast:
				SECorner.Z += amount;
				break;
		}

		Center.X = (NWCorner.X + SECorner.X) / 2.0f;
		Center.Y = (NWCorner.Y + SECorner.Y) / 2.0f;
		Center.Z = (NWCorner.Z + SECorner.Z) / 2.0f;

		if ((SECorner.X - NWCorner.X) > 0.0f && (SECorner.Y - NWCorner.Y) > 0.0f) {
			InvDXCorners = 1.0f / (SECorner.X - NWCorner.X);
			InvDYCorners = 1.0f / (SECorner.Y - NWCorner.Y);
		}
		else {
			InvDXCorners = 0;
			InvDYCorners = 0;
		}

		if (!raiseAdjacentCorners || nav_corner_adjust_adjacent.GetFloat() <= 0.0f)
			return;

		MakeNewMarker();
		Mark();

		Vector3 cornerPos = GetCorner(corner);
		cornerPos.Z -= amount;

		int gridX = NavMesh.Instance!.WorldToGridX(cornerPos.X);
		int gridY = NavMesh.Instance.WorldToGridY(cornerPos.Y);

		const int shift = 1;

		for (int x = gridX - shift; x <= gridX + shift; ++x) {
			if (x < 0 || x >= NavMesh.Instance.GridSizeX)
				continue;

			for (int y = gridY - shift; y <= gridY + shift; ++y) {
				if (y < 0 || y >= NavMesh.Instance.GridSizeY)
					continue;

				List<NavArea> areas = NavMesh.Instance.Grid[x + y * NavMesh.Instance.GridSizeX];

				foreach (NavArea area in areas) {
					if (area.IsMarked())
						continue;

					area.Mark();

					Vector3 areaPos;
					for (int i = 0; i < (int)NavCornerType.NumCorners; ++i) {
						areaPos = area.GetCorner((NavCornerType)i);
						if ((areaPos - cornerPos).Length() < nav_corner_adjust_adjacent.GetFloat()) {
							float heightDiff = cornerPos.Z + amount - areaPos.Z;
							area.RaiseCorner((NavCornerType)i, (int)heightDiff, false);
						}
					}
				}
			}
		}
	}

	static float FindGroundZFromPoint(Vector3 end, Vector3 start) {
		Vector3 step = new(0, 0, StepHeight);
		if (Math.Abs(end.X - start.X) > Math.Abs(end.Y - start.Y)) {
			step.X = GenerationStepSize;
			if (end.X < start.X)
				step.X = -step.X;
		}
		else {
			step.Y = GenerationStepSize;
			if (end.Y < start.Y)
				step.Y = -step.Y;
		}

		Vector3 point = start;
		float z;
		while ((point.AsVector2D() - end.AsVector2D()).Length() > GenerationStepSize) {
			point += step;
			z = point.Z;
			if (NavMesh.Instance!.GetGroundHeight(point, out z, out _))
				point.Z = z;
			else
				point.Z -= step.Z;
		}

		z = point.Z + step.Z;
		point = end;
		point.Z = z;
		if (NavMesh.Instance!.GetGroundHeight(point, out z, out _))
			point.Z = z;
		else
			point.Z -= step.Z;

		return point.Z;
	}

	static float FindGroundZ(Vector3 original, Vector3 corner1, Vector3 corner2) {
		float first = FindGroundZFromPoint(original, corner1);
		float second = FindGroundZFromPoint(original, corner2);

		if (Math.Abs(first - second) > StepHeight) {
			if (Math.Abs(original.Z - first) > Math.Abs(original.Z - second))
				return second;
			else
				return first;
		}

		return first;
	}

	public void PlaceOnGround(NavCornerType corner, float inset = 0.0f) {
		Vector3 nw = NWCorner + new Vector3(inset, inset, 0);
		Vector3 se = SECorner + new Vector3(-inset, -inset, 0);
		Vector3 ne = default, sw = default;
		ne.X = se.X;
		ne.Y = nw.Y;
		ne.Z = NEZ;
		sw.X = nw.X;
		sw.Y = se.Y;
		sw.Z = SWZ;

		if (corner == NavCornerType.NorthWest || corner == NavCornerType.NumCorners) {
			float newZ = FindGroundZ(nw, ne, sw);
			RaiseCorner(NavCornerType.NorthWest, (int)(newZ - nw.Z));
		}

		if (corner == NavCornerType.NorthEast || corner == NavCornerType.NumCorners) {
			float newZ = FindGroundZ(ne, nw, se);
			RaiseCorner(NavCornerType.NorthEast, (int)(newZ - ne.Z));
		}

		if (corner == NavCornerType.SouthWest || corner == NavCornerType.NumCorners) {
			float newZ = FindGroundZ(sw, nw, se);
			RaiseCorner(NavCornerType.SouthWest, (int)(newZ - sw.Z));
		}

		if (corner == NavCornerType.SouthEast || corner == NavCornerType.NumCorners) {
			float newZ = FindGroundZ(se, ne, sw);
			RaiseCorner(NavCornerType.SouthEast, (int)(newZ - se.Z));
		}
	}

	public void Shift(Vector3 shift) {
		NWCorner += shift;
		SECorner += shift;
		Center += shift;
	}

	public bool IsBlocked(int teamID, bool ignoreNavBlockers = false) {
		if (ignoreNavBlockers && (GetAttributes() & NavAttributeType.NavBlocker) != 0)
			return false;

		if (teamID == Constants.TEAM_ANY) {
			bool isBlocked = false;
			for (int i = 0; i < MAX_NAV_TEAMS; i++)
				isBlocked = _IsBlocked[i];
			return isBlocked;
		}

		int teamIdx = teamID % MAX_NAV_TEAMS;
		return _IsBlocked[teamIdx];
	}

	void MarkAsBlocked(int teamID, BaseEntity? blocker, bool generateEvent = true) {
		if (blocker != null && blocker.ClassMatches("func_nav_blocker"))
			AttributeFlags |= NavAttributeType.NavBlocker;

		bool wasBlocked = false;
		if (teamID == Constants.TEAM_ANY) {
			for (int i = 0; i < MAX_NAV_TEAMS; ++i) {
				wasBlocked |= _IsBlocked[i];
				_IsBlocked[i] = true;
			}
		}
		else {
			int teamIdx = teamID % MAX_NAV_TEAMS;
			wasBlocked |= _IsBlocked[teamIdx];
			_IsBlocked[teamIdx] = true;
		}

		if (!wasBlocked) {
			if (generateEvent) {
				IGameEvent? evnt = gameeventmanager.CreateEvent("nav_blocked");
				if (evnt != null) {
					evnt.SetInt("area", (int)ID);
					evnt.SetInt("blocked", 1);
					gameeventmanager.FireEvent(evnt);
				}
			}

			if (nav_debug_blocked.GetBool()) {
				if (blocker != null)
					ConColorMsg(new Color(0, 255, 128, 255), $"{blocker.GetDebugName()} {blocker.EntIndex} blocked area {ID}\n");
				else
					ConColorMsg(new Color(0, 255, 128, 255), $"non-entity blocked area {ID}\n");
			}
			NavMesh.Instance!.OnAreaBlocked(this);
		}
		else if (nav_debug_blocked.GetBool()) {
			if (blocker != null)
				ConColorMsg(new Color(0, 255, 128, 255), $"DUPE: {blocker.GetDebugName()} {blocker.EntIndex} blocked area {ID}\n");
			else
				ConColorMsg(new Color(0, 255, 128, 255), $"DUPE: non-entity blocked area {ID}\n");
		}
	}

	void UpdateBlockedFromNavBlockers() {
		Extent bounds = default;
		GetExtent(ref bounds);

		AttributeFlags &= ~NavAttributeType.NavBlocker;
		bool[] oldBlocked = new bool[MAX_NAV_TEAMS];
		bool wasBlocked = false;
		for (int i = 0; i < MAX_NAV_TEAMS; ++i) {
			oldBlocked[i] = _IsBlocked[i];
			wasBlocked = wasBlocked || _IsBlocked[i];
			_IsBlocked[i] = false;
		}

		bool isBlocked = FuncNavBlocker.CalculateBlocked(_IsBlocked, bounds.Lo, bounds.Hi);

		if (isBlocked)
			AttributeFlags |= NavAttributeType.NavBlocker;

		if (wasBlocked != isBlocked) {
			IGameEvent? evnt = gameeventmanager.CreateEvent("nav_blocked");
			if (evnt != null) {
				evnt.SetInt("area", (int)ID);
				evnt.SetInt("blocked", isBlocked ? 1 : 0);
				gameeventmanager.FireEvent(evnt);
			}

			if (isBlocked)
				NavMesh.Instance!.OnAreaBlocked(this);
			else
				NavMesh.Instance!.OnAreaUnblocked(this);
		}

		if (isBlocked) {
			if (nav_debug_blocked.GetBool())
				ConColorMsg(new Color(0, 255, 128, 255), $"area {ID} is blocked by a nav blocker\n");
			NavMesh.Instance!.OnAreaBlocked(this);
		}
		else {
			if (nav_debug_blocked.GetBool())
				ConColorMsg(new Color(0, 128, 255, 255), $"area {ID} is unblocked by a nav blocker\n");
			NavMesh.Instance!.OnAreaUnblocked(this);
		}
	}

	void UnblockArea(int teamID) {
		bool wasBlocked = IsBlocked(teamID);

		if (teamID == Constants.TEAM_ANY) {
			for (int i = 0; i < MAX_NAV_TEAMS; ++i)
				_IsBlocked[i] = false;
		}
		else {
			int teamIdx = teamID % MAX_NAV_TEAMS;
			_IsBlocked[teamIdx] = false;
		}

		if (wasBlocked) {
			IGameEvent? evnt = gameeventmanager.CreateEvent("nav_blocked");
			if (evnt != null) {
				evnt.SetInt("area", (int)ID);
				evnt.SetInt("blocked", 0);
				gameeventmanager.FireEvent(evnt);
			}

			if (nav_debug_blocked.GetBool()) {
				if (teamID == Constants.TEAM_ANY)
					ConColorMsg(new Color(255, 0, 128, 255), $"area {ID} is unblocked by UnblockArea for all teams\n");
				else
					ConColorMsg(new Color(255, 0, 128, 255), $"area {ID} is unblocked by UnblockArea for team {teamID}\n");
			}
			NavMesh.Instance!.OnAreaUnblocked(this);
		}
	}

	public void UpdateBlocked(bool force = false, int teamID = -2) {
		if (!force && !BlockedTimer.IsElapsed())
			return;

		const float MaxBlockedCheckInterval = 5;
		float interval = (float)(BlockedTimer.GetCountdownDuration() + 1);
		if (interval > MaxBlockedCheckInterval)
			interval = MaxBlockedCheckInterval;
		BlockedTimer.Start(interval);

		if ((AttributeFlags & NavAttributeType.NavBlocker) != 0) {
			if (force)
				UpdateBlockedFromNavBlockers();

			return;
		}

		Vector3 origin = GetCenter();
		origin.Z += HalfHumanHeight;

		float sizeX = Math.Max(1, Math.Min(GetSizeX() / 2 - 5, HalfHumanWidth));
		float sizeY = Math.Max(1, Math.Min(GetSizeY() / 2 - 5, HalfHumanWidth));

		Extent bounds = default;
		bounds.Lo = new Vector3(-sizeX, -sizeY, 0);
		bounds.Hi = new Vector3(sizeX, sizeY, VEC_DUCK_HULL_MAX.Z - HalfHumanHeight);

		bool wasBlocked = IsBlocked(Constants.TEAM_ANY);

		TraceFilterWalkableEntities filter = new(null, CollisionGroup.PlayerMovement, WalkThruFlags.Doors | WalkThruFlags.Breakables);
		Util.TraceHull(origin, origin, bounds.Lo, bounds.Hi, Mask.NPCSolidBrushOnly, ref filter, out Trace result);

		if (!result.StartSolid) {
			if (false)
#pragma warning disable CS0162 // Unreachable code detected
				Shared.DebugOverlay.Box(origin, bounds.Lo, bounds.Hi, 0, 255, 0, 10, 5.0f);
#pragma warning restore CS0162 // Unreachable code detected
			else {
				for (int i = 0; i < MAX_NAV_TEAMS; ++i)
					_IsBlocked[i] = false;
			}
		}
		else if (force) {
			if (teamID == Constants.TEAM_ANY) {
				for (int i = 0; i < MAX_NAV_TEAMS; ++i)
					_IsBlocked[i] = true;
			}
			else {
				int teamIdx = teamID % MAX_NAV_TEAMS;
				_IsBlocked[teamIdx] = true;
			}
		}

		bool isBlocked = IsBlocked(Constants.TEAM_ANY);

		if (wasBlocked != isBlocked) {
			IGameEvent? evnt = gameeventmanager.CreateEvent("nav_blocked");
			if (evnt != null) {
				evnt.SetInt("area", (int)ID);
				evnt.SetInt("blocked", isBlocked ? 1 : 0);
				gameeventmanager.FireEvent(evnt);
			}

			if (isBlocked)
				NavMesh.Instance!.OnAreaBlocked(this);
			else
				NavMesh.Instance!.OnAreaUnblocked(this);
		}

		if (NavMesh.Instance!.GetMarkedArea() == this) {
			if (isBlocked)
				Shared.DebugOverlay.Box(origin, bounds.Lo, bounds.Hi, 255, 0, 0, 64, 3.0f);
			else
				Shared.DebugOverlay.Box(origin, bounds.Lo, bounds.Hi, 0, 255, 0, 64, 3.0f);
		}
	}

	void CheckFloor(BaseEntity ignore) {
		if (IsBlocked(Constants.TEAM_ANY))
			return;

		Vector3 origin = GetCenter();
		origin.Z -= JumpCrouchHeight;

		const float size = GenerationStepSize * 0.5f;
		Vector3 mins = new(-size, -size, 0);
		Vector3 maxs = new(size, size, JumpCrouchHeight + 10.0f);

		Util.TraceHull(origin, origin, mins, maxs, Mask.NPCSolidBrushOnly, ignore, CollisionGroup.PlayerMovement, out Trace result);

		if (!result.StartSolid)
			MarkAsBlocked(Constants.TEAM_ANY, null);
	}

	public void MarkObstacleToAvoid(float obstructionHeight) {
		if (AvoidanceObstacleHeight < obstructionHeight) {
			if (AvoidanceObstacleHeight == 0)
				NavMesh.Instance!.OnAvoidanceObstacleEnteredArea(this);

			AvoidanceObstacleHeight = obstructionHeight;
		}
	}

	public void UpdateAvoidanceObstacles() {
		if (!AvoidanceObstacleTimer.IsElapsed())
			return;

		const float MaxBlockedCheckInterval = 5;
		float interval = (float)(BlockedTimer.GetCountdownDuration() + 1);
		if (interval > MaxBlockedCheckInterval)
			interval = MaxBlockedCheckInterval;

		AvoidanceObstacleTimer.Start(interval);

		Vector3 mins = NWCorner;
		Vector3 maxs = SECorner;

		mins.Z = Math.Min(NWCorner.Z, SECorner.Z);
		maxs.Z = Math.Max(NWCorner.Z, SECorner.Z) + HumanCrouchHeight;

		float obstructionHeight = 0.0f;
		for (int i = 0; i < NavMesh.Instance!.GetObstructions().Count; ++i) {
			INavAvoidanceObstacle obstruction = NavMesh.Instance!.GetObstructions()[i];
			BaseEntity obstructingEntity = obstruction.GetObstructingEntity();
			if (obstructingEntity == null)
				continue;

			obstructingEntity.CollisionProp().WorldSpaceSurroundingBounds(out Vector3 vecSurroundMins, out Vector3 vecSurroundMaxs);
			if (!CollisionUtils.IsBoxIntersectingBox(mins, maxs, vecSurroundMins, vecSurroundMaxs))
				continue;

			if (!obstruction.CanObstructNavAreas())
				continue;

			float propHeight = obstruction.GetNavObstructionHeight();

			obstructionHeight = Math.Max(obstructionHeight, propHeight);
		}

		AvoidanceObstacleHeight = obstructionHeight;

		if (AvoidanceObstacleHeight == 0.0f)
			NavMesh.Instance!.OnAvoidanceObstacleLeftArea(this);
	}

	void ClearAllNavCostEntities() {
		RemoveAttributes(NavAttributeType.FuncCost);
		FuncNavCostVector.Clear();
	}

	void AddFuncNavCostEntity(FuncNavCost cost) {
		SetAttributes(NavAttributeType.FuncCost);
		FuncNavCostVector.Add(cost);
	}


	float ComputeFuncNavCost(BaseCombatCharacter who) {
		float funcCost = 1.0f;

		foreach (FuncNavCost cost in FuncNavCostVector)
			funcCost *= cost.GetCostMultiplier(who);

		return funcCost;
	}

	public bool HasFuncNavAvoid() {
		foreach (FuncNavCost cost in FuncNavCostVector) {
			if (cost is FuncNavAvoid)
				return true;
		}

		return false;
	}

	public bool HasFuncNavPrefer() {
		foreach (FuncNavCost cost in FuncNavCostVector) {
			if (cost is FuncNavPrefer)
				return true;
		}

		return false;
	}

	void CheckWaterLevel() {
		Vector3 pos = GetCenter();
		if (!NavMesh.Instance!.GetGroundHeight(pos, out _, out _)) {
			IsUnderwater = false;
			return;
		}

		pos.Z += 1;
		IsUnderwater = (enginetrace.GetPointContents(pos, out _) & Contents.Water) != 0;
	}

	void SetupPVS() { }

	bool IsInPVS() {
		throw new NotImplementedException();
	}

	static readonly byte[] PVS = new byte[PAD_NUMBER(BSPFileCommon.MAX_MAP_CLUSTERS, 8) / 8];

	public VisibilityType ComputeVisibility(NavArea area, bool isPVSValid, bool checkPVS, out bool outsidePVS) {
		outsidePVS = false;
		float distanceSq = (area.GetCenter() - GetCenter()).LengthSquared();

		if (nav_max_view_distance.GetFloat() > 0.00001f) {
			if (distanceSq > nav_max_view_distance.GetFloat() * nav_max_view_distance.GetFloat())
				return VisibilityType.NotVisible;
		}

		if (!isPVSValid)
			SetupPVS();

		Vector3 eye = new(0, 0, 0.75f * HumanHeight);

		if (checkPVS) {
			Extent areaExtent = new();
			areaExtent.Lo = areaExtent.Hi = area.GetCenter() + eye;
			areaExtent.Encompass(area.GetCorner(NavCornerType.NorthWest) + eye);
			areaExtent.Encompass(area.GetCorner(NavCornerType.NorthEast) + eye);
			areaExtent.Encompass(area.GetCorner(NavCornerType.SouthWest) + eye);
			areaExtent.Encompass(area.GetCorner(NavCornerType.SouthEast) + eye);

			if (!engine.CheckBoxInPVS(areaExtent.Lo, areaExtent.Hi, PVS)) {
				outsidePVS = true;
				return VisibilityType.NotVisible;
			}

			outsidePVS = false;
		}

		Vector3 thisNW = GetCorner(NavCornerType.NorthWest) + eye;
		Vector3 thisNE = GetCorner(NavCornerType.NorthEast) + eye;
		Vector3 thisSW = GetCorner(NavCornerType.SouthWest) + eye;
		Vector3 thisSE = GetCorner(NavCornerType.SouthEast) + eye;
		Vector3 thisCenter = GetCenter() + eye;

		Vector3 traceMins = thisNW;
		Vector3 traceMaxs = thisSE;

		traceMins.Z = Math.Min(Math.Min(Math.Min(thisNW.Z, thisNE.Z), thisSE.Z), thisSW.Z);
		traceMaxs.Z = Math.Max(Math.Max(Math.Max(thisNW.Z, thisNE.Z), thisSE.Z), thisSW.Z) + 0.1f;

		traceMins -= thisCenter;
		traceMaxs -= thisCenter;

		Vector3 vOtherMins = area.GetCorner(NavCornerType.NorthWest);
		Vector3 vOtherMaxs = area.GetCorner(NavCornerType.SouthEast);

		MathLib.CalcClosestPointOnAABB(vOtherMins, vOtherMaxs, thisCenter, out Vector3 target);
		target.Z = area.GetZ(target) + eye.Z;

		TraceFilterNoNPCsOrPlayer traceFilter = new(null, CollisionGroup.None);

		Util.TraceHull(thisCenter, target, traceMins, traceMaxs, Mask.BlockLOS | ((Mask)Contents.IgnoreNoDrawOpaque), ref traceFilter, out Trace tr);

		if (tr.Fraction == 1.0f || (tr.EndPos.X > vOtherMins.X && tr.EndPos.X < vOtherMaxs.X && tr.EndPos.Y > vOtherMins.Y && tr.EndPos.Y < vOtherMaxs.Y))
			return VisibilityType.CompletelyVisible;

		byte vis = (byte)VisibilityType.CompletelyVisible;

		float margin = GenerationStepSize / 2.0f;
		Vector3 shift = new(0, 0, 0.75f * HumanHeight);

		if (area.IsPartiallyVisible(GetCenter() + eye))
			vis |= (byte)VisibilityType.PotentiallyVisible;
		else
			vis = (byte)(vis & ~(byte)VisibilityType.CompletelyVisible);

		Vector3 eyeToCenter = GetCenter() - area.GetCenter();
		eyeToCenter = Vector3.Normalize(eyeToCenter);

		float angleTolerance = nav_potentially_visible_dot_tolerance.GetFloat();

		for (shift.Y = margin; shift.Y <= GetSizeY() - margin; shift.Y += GenerationStepSize) {
			for (shift.X = margin; shift.X <= GetSizeX() - margin; shift.X += GenerationStepSize) {
				if (vis == (byte)VisibilityType.PotentiallyVisible)
					return VisibilityType.PotentiallyVisible;

				Vector3 testPos = GetCorner(NavCornerType.NorthWest) + shift;
				testPos.Z = GetZ(testPos) + eye.Z;

				if (distanceSq > 1000 * 1000) {
					Vector3 eyeToCorner = testPos - (GetCenter() + eye);
					eyeToCorner = Vector3.Normalize(eyeToCorner);
					if (Vector3.Dot(eyeToCorner, eyeToCenter) >= angleTolerance)
						continue;
				}

				if (area.IsPartiallyVisible(testPos))
					vis |= (byte)VisibilityType.PotentiallyVisible;
				else
					vis = (byte)(vis & ~(byte)VisibilityType.CompletelyVisible);
			}
		}

		return (VisibilityType)vis;
	}

	static List<AreaBindInfo> AreaBindDelta = [];
	public List<AreaBindInfo> ComputeVisibilityDelta(NavArea other) {
		AreaBindDelta.Clear();

		if (other.InheritVisibilityFrom.Area != null) {
			AssertMsg(false, "Visibility inheriting from inherited area");

			AreaBindDelta = PotentiallyVisibleAreas;
			return AreaBindDelta;
		}

		int i, j;
		for (i = 0; i < PotentiallyVisibleAreas.Count; ++i) {
			if (PotentiallyVisibleAreas[i].Area != null) {
				for (j = 0; j < other.PotentiallyVisibleAreas.Count; ++j) {
					if (PotentiallyVisibleAreas[i].Area == other.PotentiallyVisibleAreas[j].Area &&
							PotentiallyVisibleAreas[i].Attributes == other.PotentiallyVisibleAreas[j].Attributes) {
						break;
					}
				}

				if (j == other.PotentiallyVisibleAreas.Count)
					AreaBindDelta.Add(PotentiallyVisibleAreas[i]);
			}
		}

		for (j = 0; j < other.PotentiallyVisibleAreas.Count; ++j) {
			if (other.PotentiallyVisibleAreas[j].Area != null) {
				for (i = 0; i < PotentiallyVisibleAreas.Count; ++i) {
					if (PotentiallyVisibleAreas[i].Area == other.PotentiallyVisibleAreas[j].Area)
						break;
				}

				if (i == PotentiallyVisibleAreas.Count) {
					AreaBindInfo info = new() {
						Area = other.PotentiallyVisibleAreas[j].Area,
						Attributes = (byte)VisibilityType.NotVisible
					};

					AreaBindDelta.Add(info);
				}
			}
		}

		return AreaBindDelta;
	}

	public void ResetPotentiallyVisibleAreas() => PotentiallyVisibleAreas.Clear();

	[Flags]
	public enum VisibilityType : byte
	{
		NotVisible,
		PotentiallyVisible,
		CompletelyVisible
	}

	void ComputeVisToArea(NavArea currentArea, NavArea otherArea) {
		NavArea area = otherArea;
		VisibilityType visThisToOther = (area == currentArea) ? VisibilityType.CompletelyVisible : VisibilityType.NotVisible;
		VisibilityType visOtherToThis = VisibilityType.NotVisible;

		if (area != currentArea) {
			visOtherToThis = currentArea.ComputeVisibility(area, true, true, out bool outsidePVS);

			if (!outsidePVS &&
					(visOtherToThis != VisibilityType.NotVisible ||
					 (currentArea.GetCenter() - area.GetCenter()).LengthSquared() < nav_max_view_distance.GetFloat() * nav_max_view_distance.GetFloat())) {
				visThisToOther = area.ComputeVisibility(currentArea, true, false, out _);
			}

			if (visOtherToThis == VisibilityType.NotVisible && visThisToOther != VisibilityType.NotVisible)
				visOtherToThis = VisibilityType.PotentiallyVisible;

			if (visThisToOther == VisibilityType.NotVisible && visOtherToThis != VisibilityType.NotVisible)
				visThisToOther = VisibilityType.PotentiallyVisible;
		}

		AreaBindInfo info = new();

		if (visThisToOther != VisibilityType.NotVisible) {
			info.Area = area;
			info.Attributes = (byte)visThisToOther;
			g_ComputedVis.Add(info);
		}

		if (visOtherToThis != VisibilityType.NotVisible) {
			info.Area = currentArea;
			info.Attributes = (byte)visOtherToThis;
			g_ComputedVis.Add(info);
		}
	}

	ConcurrentBag<AreaBindInfo> g_ComputedVis = [];

	public void ComputeVisibilityToMesh() {
		InheritVisibilityFrom.Area = null;
		IsInheritedFrom = false;

		NavAreaCollector collector = new();

		float radius = nav_max_view_distance.GetFloat();
		if (radius == 0.0f)
			radius = 1500.0f;

		collector.Areas.EnsureCapacity(1000);
		NavMesh.Instance!.ForAllAreasInRadius(collector.Invoke, GetCenter(), radius);

		NavVisPair_t visPair = new();

		for (int i = collector.Areas.Count - 1; i >= 0; --i) {
			visPair.SetPair(this, collector.Areas[i]);

			if (g_NavVisPairHash.Contains(visPair))
				collector.Areas.RemoveAt(i);
		}

		SetupPVS();

		Parallel.ForEach(collector.Areas, area => ComputeVisToArea(this, area));

		PotentiallyVisibleAreas.EnsureCapacity(g_ComputedVis.Count);

		while (g_ComputedVis.TryTake(out AreaBindInfo info))
			PotentiallyVisibleAreas.Add(info);

		for (int i = 0; i < collector.Areas.Count; i++) {
			visPair.SetPair(this, collector.Areas[i]);
			Assert(!g_NavVisPairHash.Contains(visPair));
			g_NavVisPairHash.Add(visPair);
		}
	}

	bool IsEntirelyVisible(Vector3 eye, BaseEntity? ignore = null) {
		Vector3 corner;
		TraceFilterNoNPCsOrPlayer traceFilter = new(ignore, CollisionGroup.None);
		const float offset = 0.75f * HumanHeight;

		Util.TraceLine(eye, GetCenter() + new Vector3(0, 0, offset), Mask.BlockLOSAndNPCs | ((Mask)Contents.IgnoreNoDrawOpaque), ref traceFilter, out Trace result);
		if (result.Fraction < 1.0f)
			return false;

		for (int c = 0; c < (int)NavCornerType.NumCorners; ++c) {
			corner = GetCorner((NavCornerType)c) + new Vector3(0, 0, offset);

			Util.TraceLine(eye, corner, Mask.BlockLOSAndNPCs | ((Mask)Contents.IgnoreNoDrawOpaque), ref traceFilter, out result);
			if (result.Fraction < 1.0f)
				return false;
		}

		return true;
	}

	public bool IsPartiallyVisible(Vector3 eye, BaseEntity? ignore = null) {
		Vector3 corner;
		TraceFilterNoNPCsOrPlayer traceFilter = new(ignore, CollisionGroup.None);
		const float offset = 0.75f * HumanHeight;

		Util.TraceLine(eye, GetCenter() + new Vector3(0, 0, offset), Mask.BlockLOSAndNPCs | ((Mask)Contents.IgnoreNoDrawOpaque), ref traceFilter, out Trace result);
		if (result.Fraction >= 1.0f)
			return true;

		Vector3 eyeToCenter = GetCenter() + new Vector3(0, 0, offset) - eye;
		eyeToCenter = Vector3.Normalize(eyeToCenter);
		float angleTolerance = nav_potentially_visible_dot_tolerance.GetFloat();

		for (int c = 0; c < (int)NavCornerType.NumCorners; ++c) {
			corner = GetCorner((NavCornerType)c) + new Vector3(0, 0, offset);

			Vector3 eyeToCorner = corner - eye;
			eyeToCorner = Vector3.Normalize(eyeToCorner);
			if (Vector3.Dot(eyeToCorner, eyeToCenter) >= angleTolerance)
				continue;

			Util.TraceLine(eye, corner + new Vector3(0, 0, offset), Mask.BlockLOSAndNPCs | ((Mask)Contents.IgnoreNoDrawOpaque), ref traceFilter, out result);
			if (result.Fraction >= 1.0f)
				return true;
		}

		return false;
	}

	bool IsPotentiallyVisible(NavArea viewedArea) {
		if (viewedArea == null)
			return false;

		if (viewedArea == this)
			return true;

		for (int i = 0; i < PotentiallyVisibleAreas.Count; ++i) {
			if (PotentiallyVisibleAreas[i].Area == viewedArea)
				return PotentiallyVisibleAreas[i].Attributes != (byte)VisibilityType.NotVisible;
		}

		if (InheritVisibilityFrom.Area != null) {
			List<AreaBindInfo> inherited = InheritVisibilityFrom.Area.PotentiallyVisibleAreas;

			for (int i = 0; i < inherited.Count; ++i) {
				if (inherited[i].Area == viewedArea)
					return inherited[i].Attributes != (byte)VisibilityType.NotVisible;
			}
		}

		return false;
	}

	bool IsCompletelyVisible(NavArea viewedArea) {
		if (viewedArea == null)
			return false;

		if (viewedArea == this)
			return true;

		for (int i = 0; i < PotentiallyVisibleAreas.Count; ++i) {
			if (PotentiallyVisibleAreas[i].Area == viewedArea)
				return (PotentiallyVisibleAreas[i].Attributes & (byte)VisibilityType.CompletelyVisible) != 0;
		}

		if (InheritVisibilityFrom.Area != null) {
			List<AreaBindInfo> inherited = InheritVisibilityFrom.Area.PotentiallyVisibleAreas;

			for (int i = 0; i < inherited.Count; ++i) {
				if (inherited[i].Area == viewedArea)
					return (inherited[i].Attributes & (byte)VisibilityType.CompletelyVisible) != 0;
			}
		}

		return false;
	}

	bool IsPotentiallyVisibleToTeam(int teamIndex) {
		throw new NotImplementedException();
	}

	bool IsCompletelyVisibleToTeam(int teamIndex) {
		throw new NotImplementedException();
	}

	Vector3 GetRandomPoint() {
		Extent extent = default;
		GetExtent(ref extent);

		Vector3 spot;
		spot.X = RandomFloat(extent.Lo.X, extent.Hi.X);
		spot.Y = RandomFloat(extent.Lo.Y, extent.Hi.Y);
		spot.Z = GetZ(spot.X, spot.Y);

		return spot;
	}

	public bool HasPrerequisite(BaseCombatCharacter? actor = null) => PrerequisiteVector.Count > 0;

	List<Handle<FuncNavPrerequisite>> GetPrerequisiteVector() => PrerequisiteVector;

	void RemoveAllPrerequisites() => PrerequisiteVector.Clear();

	void AddPrerequisite(FuncNavPrerequisite prereq) {
		throw new NotImplementedException();
	}

	static float GetDangerDecayRate() => 1.0f / 120.0f;

	public float GetSizeX() => SECorner.X - NWCorner.X;
	public float GetSizeY() => SECorner.Y - NWCorner.Y;

	public bool IsDegenerate() => (NWCorner.X >= SECorner.X) || (NWCorner.Y >= SECorner.Y);

	public int GetAdjacentCount(NavDirType dir) => Connect[(int)dir].Count;

	public NavArea? GetAdjacentArea(NavDirType dir, int i) {
		if (i < 0 || i >= Connect[(int)dir].Count)
			return null;

		return Connect[(int)dir][i].Area;
	}

	public bool IsOpen() => OpenMarker == MasterMarker;

	public static bool IsOpenListEmpty() {
		Assert((OpenList != null && OpenList.PrevOpen == null) || OpenList == null);
		return OpenList == null;
	}

	public static NavArea PopOpenList() {
		throw new NotImplementedException();
	}

	public bool IsClosed() => IsMarked() && !IsOpen();

	public void AddToClosedList() => Mark();

	public void RemoveFromClosedList() { }

	void SetClearedTimestamp(int teamID) => ClearedTimestamp[teamID % MAX_NAV_TEAMS] = gpGlobals.CurTime;

	TimeUnit_t GetClearedTimestamp(int teamID) => ClearedTimestamp[teamID % MAX_NAV_TEAMS];

	List<HidingSpot> GetHidingSpots() => HidingSpots;

	TimeUnit_t GetEarliestOccupyTime(int teamID) => EarliestOccupyTime[teamID % MAX_NAV_TEAMS];

	public bool IsDamaging() => gpGlobals.TickCount <= DamagingTickCount;

	void MarkAsDamaging(float duration) => DamagingTickCount = (int)(gpGlobals.TickCount + TIME_TO_TICKS(duration));

	public bool HasAvoidanceObstacle(float maxObstructionHeight = 0) => AvoidanceObstacleHeight > maxObstructionHeight;

	float GetAvoidanceObstacleHeight() => AvoidanceObstacleHeight;

	public bool IsVisible(Vector3 eye, out Vector3? visSpot) {
		Vector3 corner;
		TraceFilterNoNPCsOrPlayer traceFilter = new(null, CollisionGroup.None);
		float offset = 0.75f * HumanHeight;

		Vector3 center = GetCenter();
		Util.TraceLine(eye, center + new Vector3(0, 0, offset), Mask.BlockLOSAndNPCs | ((Mask)Contents.IgnoreNoDrawOpaque), ref traceFilter, out Trace result);

		if (result.Fraction == 1.0f) {
			visSpot = center;
			return true;
		}

		for (int c = 0; c < (int)NavCornerType.NumCorners; ++c) {
			corner = GetCorner((NavCornerType)c);
			Util.TraceLine(eye, corner + new Vector3(0, 0, offset), Mask.BlockLOSAndNPCs | ((Mask)Contents.IgnoreNoDrawOpaque), ref traceFilter, out result);

			if (result.Fraction == 1.0f) {
				visSpot = corner;
				return true;
			}
		}

		visSpot = null;
		return false;
	}

	void IncrementPlayerCount(int teamID, int entIndex) {
		teamID %= MAX_NAV_TEAMS;

		if (PlayerCount[teamID] == 255) {
			DevMsg("CNavArea::IncrementPlayerCount: Overflow\n");
			return;
		}

		++PlayerCount[teamID];
	}

	void DecrementPlayerCount(int teamID, int entIndex) {
		teamID %= MAX_NAV_TEAMS;

		if (PlayerCount[teamID] == 0) {
			DevMsg("CNavArea::DecrementPlayerCount: Underflow\n");
			return;
		}

		--PlayerCount[teamID];
	}

	public byte GetPlayerCount(int teamID = 0) {
		if (teamID != 0)
			return PlayerCount[teamID % MAX_NAV_TEAMS];

		byte total = 0;
		for (int i = 0; i < MAX_NAV_TEAMS; ++i)
			total += PlayerCount[i];

		return total;
	}

	public float GetZ(Vector3 pos) => GetZ(pos.X, pos.Y);

	public Vector3 GetCorner(NavCornerType corner) {
		return corner switch {
			NavCornerType.NorthWest => NWCorner,
			NavCornerType.NorthEast => new(SECorner.X, NWCorner.Y, NEZ),
			NavCornerType.SouthEast => SECorner,
			NavCornerType.SouthWest => new(NWCorner.X, SECorner.Y, SWZ),
			_ => throw new ArgumentOutOfRangeException(nameof(corner), corner, null)
		};
	}
}
