global using static Game.Server.AI_NetworkGlobals;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Game.Server;

public static class AI_NetworkGlobals
{
	public static AI_Network? g_pBigAINet;
}

public class AI_Network
{
	public const int AI_MAX_NODE_LINKS = 30;
	public const int MAX_NODES = 1500;

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
