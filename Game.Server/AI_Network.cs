global using static Game.Server.AI_NetworkGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Game.Server;

public static class AI_NetworkGlobals
{
	public static AI_Network? g_pBigAINet;

	public static readonly ConVar ai_no_node_cache = new("ai_no_node_cache", "0");
}

public interface INearestNodeFilter
{
	bool IsValid(AI_Node node);
	bool ShouldContinue();
}

public record struct AI_NearNode(int NodeIndex, float Dist);

readonly struct NodeFilter(AI_BaseNPC? npc, Vector3 pos)
{
	readonly Capability Capabilities = npc?.CapabilitiesGet() ?? 0;

	public bool NodeIsValid(AI_Node node) {
		if (node.NodeType == NodeType.Deleted)
			return false;

		if (npc == null)
			return true;

		if (npc.GetNavType() == Navigation.Fly && node.NodeType != NodeType.Air)
			return false;

		if ((node.NodeType == NodeType.Air && (Capabilities & Capability.MoveFly) == 0) ||
			(node.NodeType == NodeType.Ground && (Capabilities & Capability.MoveGround) == 0))
			return false;

		if (npc.IsUnusableNode(node.GetId(), node.GetHint()))
			return false;

		return true;
	}

	public float NodeDistanceSqr(AI_Node node) {
		if (npc != null)
			return (node.GetPosition(npc.GetHullType()) - pos).LengthSquared();
		else
			return (node.GetOrigin() - pos).LengthSquared();
	}
}

public class AI_Network
{
	public const int AI_MAX_NODE_LINKS = 30;
	public const int MAX_NODES = 1500;
	public const int NOT_CACHED = -2;

	const int MAX_NEAR_NODES = 10;
	const int NEARNODE_CACHE_SIZE = 32;
	const int NEARNODE_CACHE_LIFE = 10;

	struct NearNodeCache
	{
		public Vector3 TestPosition;
		public TimeUnit_t Expiration;
		public int Node;
		public AI_HullType Hull;
	}

	public AI_Network() {
		NearestCacheNext = NEARNODE_CACHE_SIZE - 1;
		for (int node = 0; node < NEARNODE_CACHE_SIZE; node++) {
			NearestCache[node].Hull = AI_HullType.None;
			NearestCache[node].Expiration = 1.175494351e-38f;
		}
	}

	public static int FindBSSmallest(ReadOnlySpan<bool> bitString, ReadOnlySpan<float> floatArray, int arraySize) {
		int winIndex = -1;
		float winSize = float.MaxValue;
		for (int i = 0; i < arraySize; i++) {
			if (bitString[i] && (floatArray[i] < winSize)) {
				winIndex = i;
				winSize = floatArray[i];
			}
		}
		return winIndex;
	}

	int ListNodesInBox(Span<AI_NearNode> list, in Vector3 mins, in Vector3 maxs, in NodeFilter filter) {
		int count = 0;

		for (int node = 0; node < NodeCount; node++) {
			AI_Node aiNode = AINode![node]!;
			ref readonly Vector3 origin = ref aiNode.GetOrigin();
			if (origin.X < mins.X || origin.X > maxs.X ||
				 origin.Y < mins.Y || origin.Y > maxs.Y ||
				 origin.Z < mins.Z || origin.Z > maxs.Z)
				continue;

			if (!filter.NodeIsValid(aiNode))
				continue;

			float dist = filter.NodeDistanceSqr(aiNode);

			if (count == list.Length) {
				if (dist >= list[count - 1].Dist)
					continue;
				count--;
			}

			int insert = count;
			while (insert > 0 && list[insert - 1].Dist > dist) {
				list[insert] = list[insert - 1];
				insert--;
			}
			list[insert] = new(node, dist);
			count++;
		}

		return count;
	}

	public int NearestNodeToPoint(AI_BaseNPC? npc, in Vector3 origin, bool checkVisibility = true, INearestNodeFilter? filter = null) {
		if (NodeCount == 0)
			return NO_NODE;

		int cachedNode = GetCachedNearestNode(origin, npc, out int cachePos);
		if (cachedNode != NO_NODE) {
			if (checkVisibility) {
				Vector3 testLoc = (npc != null) ?
									AINode![cachedNode]!.GetPosition(npc.GetHullType()) + npc.GetViewOffset() :
									AINode![cachedNode]!.GetOrigin();

				if (!IsVisible(npc, origin, testLoc))
					cachedNode = NO_NODE;
			}

			if (cachedNode != NO_NODE && (filter == null || filter.IsValid(AINode![cachedNode]!))) {
				NearestCache[cachePos].Expiration = gpGlobals.CurTime + NEARNODE_CACHE_LIFE;
				return cachedNode;
			}
		}

		NodeFilter nodeFilter = new(npc, origin);

		Span<AI_NearNode> list = stackalloc AI_NearNode[MAX_NEAR_NODES];

		Vector3 ext = new(AI_Node.MAX_NODE_LINK_DIST, AI_Node.MAX_NODE_LINK_DIST, AI_Node.MAX_NODE_LINK_DIST);
		if (npc != null && (npc.CapabilitiesGet() & Capability.MoveFly) != 0)
			ext = new(AI_Node.MAX_AIR_NODE_LINK_DIST, AI_Node.MAX_AIR_NODE_LINK_DIST, AI_Node.MAX_AIR_NODE_LINK_DIST);

		int count = ListNodesInBox(list, origin - ext, origin + ext, nodeFilter);

		for (int i = 0; i < count; i++) {
			int smallest = list[i].NodeIndex;

			if (smallest == cachedNode)
				continue;

			if (npc != null && !npc.GetNavigator()!.CanFitAtNode(smallest))
				continue;

			if (checkVisibility) {
				Vector3 testLoc = (npc != null) ?
									AINode![smallest]!.GetPosition(npc.GetHullType()) + npc.GetNodeViewOffset() :
									AINode![smallest]!.GetOrigin();

				Vector3 visOrigin = origin + new Vector3(0, 0, 1);

				if (!IsVisible(npc, visOrigin, testLoc))
					continue;
			}

			if (filter != null) {
				if (!filter.IsValid(AINode![smallest]!)) {
					if (!filter.ShouldContinue())
						break;
					continue;
				}
			}

			SetCachedNearestNode(origin, smallest, (npc != null) ? npc.GetHullType() : AI_HullType.None);

			return smallest;
		}

		SetCachedNearestNode(origin, NO_NODE, (npc != null) ? npc.GetHullType() : AI_HullType.None);

		return NO_NODE;
	}

	static bool IsVisible(AI_BaseNPC? npc, in Vector3 start, in Vector3 end) {
		Trace tr;
		if (npc != null) {
			TraceFilterNav traceFilter = new(npc, true, npc, CollisionGroup.None);
			Util.TraceLine(start, end, Mask.NPCSolidBrushOnly, ref traceFilter, out tr);
		}
		else
			Util.TraceLine(start, end, Mask.NPCSolidBrushOnly, null, CollisionGroup.None, out tr);

		return tr.Fraction == 1.0f;
	}

	public int NearestNodeToPoint(in Vector3 position, bool checkVisibility = true) => NearestNodeToPoint(null, position, checkVisibility);

	int GetCachedNode(in Vector3 checkPos, AI_HullType hull, out int cachePos) {
		if (ai_no_node_cache.GetBool()) {
			cachePos = -1;
			return NOT_CACHED;
		}

		int newest = NearestCacheNext + 1;
		for (int i = 0; i < NEARNODE_CACHE_SIZE; i++) {
			int current = (newest + i) % NEARNODE_CACHE_SIZE;
			if (NearestCache[current].Hull == hull && NearestCache[current].Expiration > gpGlobals.CurTime) {
				if ((NearestCache[current].TestPosition - checkPos).LengthSquared() < 24.0f * 24.0f) {
					cachePos = current;
					return NearestCache[current].Node;
				}
			}
		}

		cachePos = -1;
		return NOT_CACHED;
	}

	int GetCachedNearestNode(in Vector3 checkPos, AI_BaseNPC? npc, out int cachePos) {
		if (npc != null) {
			NodeFilter filter = new(npc, checkPos);

			int nodeID = GetCachedNode(checkPos, npc.GetHullType(), out cachePos);
			if (nodeID >= 0) {
				if (filter.NodeIsValid(AINode![nodeID]!) && npc.GetNavigator()!.CanFitAtNode(nodeID))
					return nodeID;
			}
		}
		else
			cachePos = -1;
		return NO_NODE;
	}

	void SetCachedNearestNode(in Vector3 checkPos, int nodeID, AI_HullType hull) {
		if (ai_no_node_cache.GetBool())
			return;

		NearestCache[NearestCacheNext].TestPosition = checkPos;
		NearestCache[NearestCacheNext].Node = nodeID;
		NearestCache[NearestCacheNext].Hull = hull;
		NearestCache[NearestCacheNext].Expiration = gpGlobals.CurTime + NEARNODE_CACHE_LIFE;

		NearestCacheNext--;
		if (NearestCacheNext < 0)
			NearestCacheNext = NEARNODE_CACHE_SIZE - 1;
	}

	public Vector3 GetNodePosition(AI_HullType hull, int nodeID) {
		if (AINode == null) {
			Assert(false);
			return vec3_origin;
		}

		if ((nodeID < 0) || (nodeID > NodeCount)) {
			Assert(false);
			return vec3_origin;
		}

		return AINode[nodeID]!.GetPosition(hull);
	}

	public Vector3 GetNodePosition(BaseCombatCharacter? npc, int nodeID) {
		if (npc == null) {
			Assert(false);
			return vec3_origin;
		}

		return GetNodePosition(npc.GetHullType(), nodeID);
	}

	public float GetNodeYaw(int nodeID) {
		if (AINode == null) {
			Assert(false);
			return 0.0f;
		}

		if ((nodeID < 0) || (nodeID > NodeCount)) {
			Assert(false);
			return 0.0f;
		}

		return AINode[nodeID]!.GetYaw();
	}

	readonly NearNodeCache[] NearestCache = new NearNodeCache[NEARNODE_CACHE_SIZE];
	int NearestCacheNext;

	public AI_Node AddNode(Vector3 origin, float yaw) {
		if (AINode == null || AINode[0] == null)
			AINode = new AI_Node?[MAX_NODES];

		if (NodeCount >= MAX_NODES) {
			DevMsg("ERROR: too many nodes in map, deleting last node.\n");
			NodeCount--;
		}

		AINode[NodeCount] = new AI_Node(this, NodeCount, origin, yaw);

		NodeCount++;

		return AINode[NodeCount - 1]!;
	}

	public const short NO_LINK = -1;

	public short CreateLink(int srcID, int destID) {
		AI_Node? srcNode = g_pBigAINet!.GetNode(srcID);
		AI_Node? destNode = g_pBigAINet.GetNode(destID);

		Assert(srcNode != null && destNode != null && srcNode != destNode);

		if (srcNode == null || destNode == null) {
			DevMsg("Attempted to create link to node that doesn't exist\n");
			return NO_LINK;
		}

		if (srcNode == destNode) {
			DevMsg("Attempted to link a node to itself\n");
			return NO_LINK;
		}

		if (srcNode.NumLinks() == AI_MAX_NODE_LINKS) {
			DevMsg($"Node {srcID} has too many links\n");
			return NO_LINK;
		}

		if (destNode.NumLinks() == AI_MAX_NODE_LINKS) {
			DevMsg($"Node {destID} has too many links\n");
			return NO_LINK;
		}

		short link = (short)AILinks.Count;
		AILinks.Add(new() {
			SrcID = (short)srcID,
			DestID = (short)destID
		});

		srcNode.AddLink(link);
		destNode.AddLink(link);

		return link;
	}

	public ref AI_Link GetLink(short link) => ref CollectionsMarshal.AsSpan(AILinks)[link];

	public bool IsConnected(int srcID, int destID) {
		if (srcID > NodeCount || destID > NodeCount) {
			DevMsg("IsConnected called with invalid node IDs!\n");
			return false;
		}

		if (srcID == destID)
			return true;

		int srcZone = AINode![srcID]!.GetZone();
		int destZone = AINode[destID]!.GetZone();

		if (srcZone == (int)AI_ZoneIds.Solo || destZone == (int)AI_ZoneIds.Solo)
			return false;

		if (srcZone == (int)AI_ZoneIds.Universal || destZone == (int)AI_ZoneIds.Universal)
			return true;

#if DEBUG
		if (srcZone == (int)AI_ZoneIds.Unknown || destZone == (int)AI_ZoneIds.Unknown) {
			DevMsg("Warning: Node found in unknown zone\n");
			return true;
		}
#endif

		return srcZone == destZone;
	}

	public int NumNodes() => NodeCount;

	static int GetNodeWarningCount;
	public AI_Node? GetNode(int id, bool asserted = true) {
		if (id >= 0 && id < NodeCount)
			return AINode![id];

		if (asserted) {
			if (++GetNodeWarningCount < 10)
				AssertMsg(false, $"Node ({id}) out of range ({NodeCount} total)\n");
		}
		return null;
	}

	public AI_Node?[]? AccessNodes() => AINode;

	internal int NodeCount;
	internal AI_Node?[]? AINode;
	internal readonly List<AI_Link> AILinks = [];
}
