global using static Game.Server.AI_WaypointGlobals;

using Source.Common;

using System.Numerics;

namespace Game.Server;

public static class AI_WaypointGlobals
{
	public const int NO_NODE = -1;

	public static void DeleteAll(AI_Waypoint? waypointList) {
		while (waypointList != null) {
			AI_Waypoint prevWaypoint = waypointList;
			waypointList = waypointList.GetNext();
			prevWaypoint.Delete();
		}
	}

	public static void AddWaypointLists(AI_Waypoint oldRoute, AI_Waypoint? addRoute) {
		AI_Waypoint waypoint = oldRoute;

		while (waypoint.GetNext() != null)
			waypoint = waypoint.GetNext()!;

		waypoint.ModifyFlags(WaypointFlags.ToGoal, false);

		if (addRoute != null &&
			waypoint.NodeID != NO_NODE &&
			waypoint.NodeID == addRoute.NodeID) {
			AI_Waypoint? next = addRoute.GetNext();
			addRoute.Delete();
			waypoint.SetNext(next);
		}
		else
			waypoint.SetNext(addRoute);

		while (waypoint.GetNext() != null)
			waypoint = waypoint.GetNext()!;

		waypoint.ModifyFlags(WaypointFlags.ToGoal, true);
	}
}

[Flags]
public enum WaypointFlags
{
	ToDetour = 0x01,
	ToPathCorner = 0x02,
	ToNode = 0x04,
	ToGoal = 0x08,
	ToDoor = 0x10,

	DontSimplify = 0x20,
}

public class AI_Waypoint
{
	public AI_Waypoint() {
		VecLocation = vec3_invalid;
		NodeID = NO_NODE;
		PathDistGoal = -1;
	}

	public AI_Waypoint(in Vector3 initPosition, float initYaw, Navigation initNavType, WaypointFlags initWaypointFlags, int initNodeID) {
		VecLocation = initPosition;
		Yaw = initYaw;
		WPType = initNavType;
		WaypointFlags = initWaypointFlags;
		NodeID = initNodeID;

		PathDistGoal = -1;
	}

	public void Delete() {
		AssertValid();
		if (Next != null) {
			Next.AssertValid();
			Next.Prev = Prev;
		}
		if (Prev != null) {
			Prev.AssertValid();
			Prev.Next = Next;
		}
	}

	public void AssertValid() {
#if DEBUG
		Assert(Next == null || Next.Prev == this);
		Assert(Prev == null || Prev.Next == this);
#endif
	}

	public WaypointFlags Flags() => WaypointFlags;
	public Navigation NavType() => WPType;

	public void ModifyFlags(WaypointFlags flags, bool enable) {
		if (enable)
			WaypointFlags |= flags;
		else
			WaypointFlags &= ~flags;
	}

	public bool IsReducible() => Next != null && WPType == Next.WPType && (WaypointFlags & (WaypointFlags.ToGoal | WaypointFlags.ToPathCorner | WaypointFlags.DontSimplify)) == 0;

	public void SetNext(AI_Waypoint? p) {
		if (Next != null)
			Next.Prev = null;

		Next = p;

		if (Next != null) {
			if (Next.Prev != null)
				Next.Prev.Next = null;

			Next.Prev = this;
		}
	}

	public AI_Waypoint? GetNext() => Next;
	public AI_Waypoint? GetPrev() => Prev;

	public ref readonly Vector3 GetPos() => ref VecLocation;
	public void SetPos(in Vector3 newPos) => VecLocation = newPos;

	public Vector3 VecLocation;
	public float Yaw;
	public int NodeID;

	public float PathDistGoal;

	public EHANDLE PathCorner = new();

	public EHANDLE Data = new();

	WaypointFlags WaypointFlags;
	Navigation WPType;

	AI_Waypoint? Next;
	AI_Waypoint? Prev;
}

public class AI_WaypointList
{
	public AI_WaypointList() {
		FirstWaypoint = null;
	}

	public AI_WaypointList(AI_Waypoint? firstWaypoint) {
		FirstWaypoint = firstWaypoint;
	}

	public bool IsEmpty() => FirstWaypoint == null;

	public AI_Waypoint? GetFirst() => FirstWaypoint;

	public AI_Waypoint? GetLast() {
		AI_Waypoint? p = GetFirst();
		if (p == null)
			return null;
		while (p.GetNext() != null)
			p = p.GetNext()!;

		return p;
	}

	public void Set(AI_Waypoint? route) => FirstWaypoint = route;

	public void PrependWaypoints(AI_Waypoint waypoints) {
		AddWaypointLists(waypoints, GetFirst());
		Set(waypoints);
	}

	public void PrependWaypoint(in Vector3 newPoint, Navigation navType, WaypointFlags waypointFlags, float yaw = 0) =>
		PrependWaypoints(new AI_Waypoint(newPoint, yaw, navType, waypointFlags, NO_NODE));

	public void RemoveAll() {
		DeleteAll(FirstWaypoint);
		FirstWaypoint = null;
		Assert(FirstWaypoint == null);
	}

	AI_Waypoint? FirstWaypoint;
}
