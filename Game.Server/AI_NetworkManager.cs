global using static Game.Server.AI_NetworkManagerGlobals;

using Game.Shared;

using Source.Common.Commands;
using Source.Common.Utilities;

using System.Numerics;

namespace Game.Server;

public static class AI_NetworkManagerGlobals
{
	public static AI_NetworkManager? g_pAINetworkManager;
}

public class AI_NetworkEditTools
{
	public int[]? NodeIndexTable;
}

[LinkEntityToClass("ai_network")]
public class AI_NetworkManager : PointEntity
{
	const int AINET_VERSION_NUMBER = 37;

	static readonly ConVar g_ai_norebuildgraph = new("ai_norebuildgraph", "0");

	public static void InitializeAINetworks() {
		g_pAINetworkManager = (AI_NetworkManager)CreateEntityByName("ai_network")!;
		g_pBigAINet = g_pAINetworkManager.GetNetwork();
		g_pAINetworkManager.LoadNetworkGraph();
	}

	public void LoadNetworkGraph() {
		if (!g_pGameRules.FAllowNPCs()) {
			DevMsg("Not loading AI due to games rules\n");
			return;
		}

		string nrpFilename = $"maps/graphs/{gpGlobals.MapName}.ain";

		long fileSize = filesystem.Size(nrpFilename, "GAME");
		byte[]? data = fileSize > 0 ? new byte[fileSize] : null;
		if (data == null || !filesystem.ReadFile(nrpFilename, "GAME", data, 0)) {
			DevWarning(2, $"Couldn't read {nrpFilename}!\n");
			return;
		}

		UtlBuffer buf = new(data, UtlBuffer.BufferFlags.ReadOnly);

		if (buf.GetChar() == 'V' && buf.GetChar() == 'e' && buf.GetChar() == 'r') {
			DevMsg($"AI node graph {nrpFilename} is out of date\n");
			return;
		}

		buf.SeekGet(SeekOrigin.Begin, 0);

		int version = buf.GetInt();

		if (version != AINET_VERSION_NUMBER) {
			DevMsg($"AI node graph {nrpFilename} is out of date\n");
			return;
		}

		int mapversion = buf.GetInt();

		if (mapversion != gpGlobals.MapVersion && !g_ai_norebuildgraph.GetBool()) {
			DevMsg($"AI node graph {nrpFilename} is out of date (map version changed)\n");
			return;
		}

		int numNodes = buf.GetInt();

		if (numNodes > AI_Network.MAX_NODES || numNodes < 0) {
			Error($"AI node graph {nrpFilename} is corrupt\n");
			Assert(false);
			return;
		}

		int node;
		for (node = 0; node < numNodes; node++) {
			Vector3 origin;
			float yaw;
			origin.X = buf.GetFloat();
			origin.Y = buf.GetFloat();
			origin.Z = buf.GetFloat();
			yaw = buf.GetFloat();

			AI_Node newNode = Network.AddNode(origin, yaw);

			for (int hull = 0; hull < newNode.VOffset.Length; hull++)
				newNode.VOffset[hull] = buf.GetFloat();
			newNode.NodeType = (NodeType)buf.GetUnsignedChar();

			newNode.NodeInfo = (NodeInfoBits)buf.GetUnsignedShort();
			newNode.Zone = buf.GetShort();
		}

		int totalNumLinks = buf.GetInt();

		Span<byte> moveTypes = stackalloc byte[(int)AI_HullType.NumHulls];
		for (int link = 0; link < totalNumLinks; link++) {
			int srcID, destID;

			srcID = buf.GetShort();
			destID = buf.GetShort();

			short newLink = Network.CreateLink(srcID, destID);

			buf.Get(moveTypes);
			if (newLink != AI_Network.NO_LINK) {
				ref AI_Link loadedLink = ref Network.GetLink(newLink);
				for (int hull = 0; hull < moveTypes.Length; hull++)
					loadedLink.AcceptedMoveTypes[hull] = (Capability)moveTypes[hull];
			}
		}

		GetEditOps().NodeIndexTable = new int[Math.Max(Network.NumNodes(), 1)];

		for (node = 0; node < Network.NumNodes(); node++)
			GetEditOps().NodeIndexTable![node] = buf.GetInt();

		HashSet<int> usedIds = [];
		HashSet<int> reportedIds = [];

		bool printedHeader = false;

		for (node = 0; node < Network.NumNodes(); node++) {
			int editorId = GetEditOps().NodeIndexTable![node];
			if (editorId != NO_NODE) {
				if (usedIds.Contains(editorId)) {
					if (!printedHeader) {
						Warning("** Duplicate Hammer Node IDs: ");
						printedHeader = true;
					}

					if (reportedIds.Add(editorId))
						DevMsg($"{editorId}, ");
				}
				else
					usedIds.Add(editorId);
			}
		}

		if (printedHeader)
			DevMsg("\n** Should run \"Check For Problems\" on the VMF then verify dynamic links\n");

		gm_fNetworksLoaded = true;
	}

	public static bool NetworksLoaded() => gm_fNetworksLoaded;
	public bool IsInitialized() => Initalized;

	public AI_NetworkEditTools GetEditOps() => EditOps;
	public AI_Network GetNetwork() => Network;

	static bool gm_fNetworksLoaded = true;

	readonly AI_Network Network = new();
	readonly AI_NetworkEditTools EditOps = new();
	bool Initalized = true;
}
