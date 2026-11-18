using System.Numerics;

namespace Game.Server;

public class AI_TacticalServices : AI_Component
{
	public AI_TacticalServices(AI_BaseNPC? outer) : base(outer) {
		Network = null;
	}

	public void Init(AI_Network? network) {
		Assert(network != null);
		Network = network;
		Pathfinder = GetOuter()!.GetPathfinder();
		Assert(Pathfinder != null);
	}

	public bool FindCoverPos(in Vector3 threatPos, in Vector3 threatEyePos, float minDist, float maxDist, out Vector3 result)
		=> FindCoverPos(GetLocalOrigin(), threatPos, threatEyePos, minDist, maxDist, out result);

	public bool FindCoverPos(in Vector3 nearPos, in Vector3 threatPos, in Vector3 threatEyePos, float minDist, float maxDist, out Vector3 result) {
		int node = FindCoverNode(nearPos, threatPos, threatEyePos, minDist, maxDist);

		if (node == NO_NODE) {
			result = default;
			return false;
		}

		result = GetNodePos(node);
		return true;
	}

	static int SearchRandomizer;
	static readonly PriorityQueue<int, float> NodeList = new();

	public int FindCoverNode(in Vector3 threatPos, in Vector3 threatEyePos, float minDist, float maxDist)
		=> FindCoverNode(GetLocalOrigin(), threatPos, threatEyePos, minDist, maxDist);

	public int FindCoverNode(in Vector3 nearPos, in Vector3 threatPos, in Vector3 threatEyePos, float minDist, float maxDist) {
		if (!AI_NetworkManager.NetworksLoaded())
			return NO_NODE;

		AI_BaseNPC outer = GetOuter()!;
		int myNode = Pathfinder!.NearestNodeToPoint(nearPos);

		if (myNode == NO_NODE) {
			Vector3 pos = outer.GetAbsOrigin();
			DevWarning(2, $"FindCover() - {outer.GetClassname()} has no nearest node! (Check near {pos.X} {pos.Y} {pos.Z})\n");
			return NO_NODE;
		}

		if (maxDist == 0)
			maxDist = 784;

		if (minDist > 0.5f * maxDist)
			minDist = 0.5f * maxDist;

		int numNodes = Network!.NumNodes();
		PriorityQueue<int, float> list = NodeList;
		list.Clear();
		Span<bool> wasVisited = stackalloc bool[numNodes];

		list.Enqueue(myNode, 0);
		wasVisited[myNode] = true;
		float minDistSqr = minDist * minDist;
		float maxDistSqr = maxDist * maxDist;

		while (list.Count > 0) {
			int nodeIndex = list.Dequeue();

			AI_Node node = Network.GetNode(nodeIndex)!;
			Vector3 nodeOrigin = node.GetPosition(GetHullType());

			float dist = (nearPos - nodeOrigin).LengthSquared();
			if (dist >= minDistSqr && dist < maxDistSqr) {
				Activity coverActivity = outer.GetCoverActivity(node.GetHint());
				Vector3 eyePos = nodeOrigin + outer.EyeOffset(coverActivity);

				if (outer.IsValidCover(nodeOrigin, node.GetHint())) {
					if (outer.IsCoverPosition(threatEyePos, eyePos)) {
						node.Lock(1.0);

						AI_Hint? hint = node.GetHint();
						if (hint != null && (hint.HintType() == Hint.TacticalCoverMed || hint.HintType() == Hint.TacticalCoverLow)) {
							if (outer.GetHintNode() != null) {
								outer.GetHintNode()!.Unlock(outer.GetHintDelay(outer.GetHintNode()!.HintType()));
								outer.SetHintNode(null);
							}

							outer.SetHintNode(hint);
						}

						SearchRandomizer = nodeIndex;
						return nodeIndex;
					}
				}
			}

			int numLinks = node.NumLinks();
			for (int link = 0; link < numLinks; link++) {
				int index = (link + SearchRandomizer) % numLinks;
				ref AI_Link nodeLink = ref node.GetLinkByIndex(index);

				if (!Pathfinder.IsLinkUsable(ref nodeLink, myNode))
					continue;

				int newID = nodeLink.DestNodeID(nodeIndex);

				if (!wasVisited[newID]) {
					AI_Node newNode = Network.GetNode(newID)!;
					if (newNode.NodeType != NodeType.Climb && !newNode.IsLocked()) {
						nodeOrigin = newNode.GetPosition(GetHullType());
						dist = (nearPos - nodeOrigin).LengthSquared();

						// Use the distance to the threat as a heuristic so the NPC does not run towards it
						float threatDist = (threatPos - nodeOrigin).LengthSquared();

						if (dist < threatDist * 1.5f)
							list.Enqueue(newID, dist);
					}
					wasVisited[newID] = true;
				}
			}
		}

		outer.ClearHintNode();
		return NO_NODE;
	}

	public Vector3 GetNodePos(int node) => Network!.GetNode(node)!.GetPosition(GetHullType());

	Vector3 GetLocalOrigin() => GetOuter()!.GetLocalOrigin();
	AI_HullType GetHullType() => GetOuter()!.GetHullType();

	public AI_Network? Network;
	public AI_Pathfinder? Pathfinder;
}
