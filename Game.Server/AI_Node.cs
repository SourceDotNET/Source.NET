using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

public enum AI_ZoneIds
{
	Unknown = 0,
	Solo = 1,
	Universal = 3,
	FirstZone = 4,
}

public enum NodeType
{
	Any,
	Deleted,
	Ground,
	Air,
	Climb,
	Water,
}

[Flags]
public enum NodeInfoBits : uint
{
	ClimbBottom = 1 << 0,
	ClimbOn = 1 << 1,
	ClimbOffForward = 1 << 2,
	ClimbOffLeft = 1 << 3,
	ClimbOffRight = 1 << 4,

	ClimbExit = ClimbOffForward | ClimbOffLeft | ClimbOffRight,

	DontDrop = 1 << 14,

	WCNeedRebuild = 0x10000000,
	WCChanged = 0x20000000,

	WontFitHull = 0x40000000,
	Fallen = 0x80000000,
}

public class AI_Node(AI_Network network, int id, Vector3 origin, float yaw)
{
	public const int NODE_ENT_FLAGS_SHIFT = 5;
	public const float NODE_CLIMB_OFFSET = 8;

	public const float MAX_NODE_LINK_DIST = 60 * 12;
	public const float MAX_NODE_LINK_DIST_SQ = MAX_NODE_LINK_DIST * MAX_NODE_LINK_DIST;
	public const float MAX_AIR_NODE_LINK_DIST = 120 * 12;
	public const float MAX_AIR_NODE_LINK_DIST_SQ = MAX_AIR_NODE_LINK_DIST * MAX_AIR_NODE_LINK_DIST;

	public AI_Hint? GetHint() => Hint;
	public void SetHint(AI_Hint? hint) => Hint = hint;

	public int NumLinks() => Links.Count;
	public ref AI_Link GetLink(int destNodeId) {
		for (int link = 0; link < NumLinks(); link++) {
			ref AI_Link nodeLink = ref GetLinkByIndex(link);
			if (nodeLink.DestNodeID(ID) == destNodeId)
				return ref nodeLink;
		}

		return ref Unsafe.NullRef<AI_Link>();
	}
	public ref AI_Link GetLinkByIndex(int i) => ref Network.GetLink(Links[i]);

	public bool IsLocked() => NextUseTime > gpGlobals.CurTime;
	public void Lock(TimeUnit_t duration) => NextUseTime = gpGlobals.CurTime + duration;
	public void Unlock() => NextUseTime = gpGlobals.CurTime;

	public int GetZone() => Zone;
	public void SetZone(int zone) => Zone = zone;

	public int GetId() => ID;

	public Vector3 GetPosition(AI_HullType hull) {
		if (NodeType == NodeType.Climb) {
			float shift = (0.5f * NAI_Hull.Length(hull)) + NODE_CLIMB_OFFSET;

			Vector3 offsetDir = new(MathF.Cos(MathLib.DEG2RAD(Yaw)), MathF.Sin(MathLib.DEG2RAD(Yaw)), 0);

			Vector3 origin;
			if ((NodeInfo & NodeInfoBits.ClimbOffForward) != 0)
				origin = Origin + (shift * offsetDir);
			else if ((NodeInfo & NodeInfoBits.ClimbOffLeft) != 0) {
				Vector3 leftDir = Vector3.Cross(offsetDir, Vector3.UnitZ);
				origin = Origin - (2 * shift * leftDir) - (shift * offsetDir);
			}
			else if ((NodeInfo & NodeInfoBits.ClimbOffRight) != 0) {
				Vector3 leftDir = Vector3.Cross(offsetDir, Vector3.UnitZ);
				origin = Origin + (2 * shift * leftDir) - (shift * offsetDir);
			}
			else
				origin = Origin - (shift * offsetDir);

			return new(origin.X, origin.Y, origin.Z + VOffset[(int)hull]);
		}
		else if (NodeType == NodeType.Ground)
			return new(Origin.X, Origin.Y, Origin.Z + VOffset[(int)hull]);
		else
			return Origin;
	}

	public ref readonly Vector3 GetOrigin() => ref Origin;
	public float GetYaw() => Yaw;

	public void AddLink(short newLink) {
		if (NumLinks() == AI_Network.AI_MAX_NODE_LINKS) {
			DevMsg($"Node {ID} has too many links\n");
			return;
		}

#if DEBUG
		for (int link = 0; link < NumLinks(); link++) {
			if (Links[link] == newLink) {
				AssertMsg(false, "Link added to node multiple times!");
				return;
			}
		}

		ref AI_Link added = ref Network.GetLink(newLink);
		AssertMsg(added.DestID == ID || added.SrcID == ID, "Added link to node that doesn't reference the node");
#endif

		Links.Add(newLink);
	}

	readonly AI_Network Network = network;

	public int ID = id;
	public Vector3 Origin = origin;

	public readonly float[] VOffset = new float[(int)AI_HullType.NumHulls];
	public float Yaw = yaw;

	public NodeType NodeType = NodeType.Ground;

	public NodeInfoBits NodeInfo;

	public int Zone = (int)AI_ZoneIds.Unknown;
	public readonly List<short> Links = new(4);

	public TimeUnit_t NextUseTime;
	public AI_Hint? Hint;
	public int FirstShuffledLink;
}
