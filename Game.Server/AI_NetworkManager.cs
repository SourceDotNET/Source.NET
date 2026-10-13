global using static Game.Server.AI_NetworkManagerGlobals;

using Game.Shared;

using Source.Common.Commands;
using Source.Common.Filesystem;

using System.Numerics;
using System.Text;

namespace Game.Server;

public static class AI_NetworkManagerGlobals
{
	public static AI_NetworkManager? g_pAINetworkManager;
	public static readonly AI_NetworkBuilder g_AINetworkBuilder = new();
}

public class AI_NetworkBuilder
{
	void FloodFillZone(AI_Node?[] nodes, AI_Node node, int zone) {
		node.SetZone(zone);

		for (int link = 0; link < node.NumLinks(); link++) {
			ref AI_Link nodeLink = ref node.GetLinkByIndex(link);
			AI_Node linkedNode = (nodeLink.DestID == node.GetId()) ? nodes[nodeLink.SrcID]! : nodes[nodeLink.DestID]!;
			if (linkedNode.GetZone() == (int)AI_ZoneIds.Unknown)
				FloodFillZone(nodes, linkedNode, zone);

			Assert(linkedNode.GetZone() == node.GetZone() && node.GetZone() == zone);
		}
	}

	public void InitZones(AI_Network network) {
		int nodeCount = network.NumNodes();
		AI_Node?[]? nodes = network.AccessNodes();

		if (nodeCount == 0)
			return;

		int i;

		for (i = 0; i < nodeCount; i++)
			nodes![i]!.SetZone((int)AI_ZoneIds.Unknown);

		for (i = 0; i < nodeCount; i++) {
			if (nodes![i]!.NumLinks() == 0)
				nodes[i]!.SetZone((int)AI_ZoneIds.Solo);
		}

		int curZone = (int)AI_ZoneIds.FirstZone;

		for (i = 0; i < nodeCount; i++) {
			if (nodes![i]!.GetZone() == (int)AI_ZoneIds.Unknown) {
				FloodFillZone(nodes, nodes[i]!, curZone);
				curZone++;
			}
		}
	}
}

public class AI_NetworkEditTools(AI_Network network)
{
	public int[]? NodeIndexTable;
	public int NextWCIndex;

	public int GetNodeIdFromWCId(int wcId) {
		if (wcId == -1)
			return -1;

		if (NodeIndexTable == null) {
			DevMsg("ERROR: Trying to get WC ID with no table!\n");
			return -1;
		}

		if (network.NumNodes() == 0) {
			DevMsg("ERROR: Trying to get WC ID with no network!\n");
			return -1;
		}

		for (int i = 0; i < network.NumNodes(); i++) {
			if (NodeIndexTable[i] == wcId)
				return i;
		}
		return -1;
	}

	public int GetWCIdFromNodeId(int nodeId) {
		if (nodeId == -1 || nodeId >= network.NumNodes())
			return -1;
		return NodeIndexTable![nodeId];
	}
}

[LinkEntityToClass("ai_network")]
public class AI_NetworkManager : PointEntity
{
	const int AINET_VERSION_NUMBER = 37;

	static readonly ConVar g_ai_norebuildgraph = new("ai_norebuildgraph", "0");

	public AI_NetworkManager() {
		EditOps = new(Network);
		AI_DynamicLink.gm_bInitialized = false;
	}

	public static void InitializeAINetworks() {
		AI_NetworkManager network = (AI_NetworkManager)CreateEntityByName("ai_network")!;
		g_pAINetworkManager = network;
		network.AddEFlags(EFL.KeepOnRecreateEntities);
		g_pBigAINet = network.GetNetwork();
		network.SetName("BigNet");
		network.Spawn();
		network.LoadNetworkGraph();

		NodeEnt.NodeCount = 0;

		network.SetThink(network.DelayedInit);
		network.SetNextThink(gpGlobals.CurTime);
	}

	public void LoadNetworkGraph() {
		if (!g_pGameRules.FAllowNPCs()) {
			DevMsg("Not loading AI due to games rules\n");
			return;
		}

		string nrpFilename = $"maps/graphs/{gpGlobals.MapName}.ain";

		using IFileHandle? handle = filesystem.Open(nrpFilename, FileOpenOptions.Read | FileOpenOptions.Binary, "GAME");
		if (handle == null) {
			DevWarning(2, $"Couldn't read {nrpFilename}!\n");
			return;
		}

		LoadNetworkGraph(handle.Stream, nrpFilename, gpGlobals.MapVersion);
	}

	public void LoadNetworkGraph(Stream stream, ReadOnlySpan<char> nrpFilename, int currentMapVersion) {
		using BinaryReader reader = new(stream, Encoding.ASCII, true);

		try {
			LoadNetworkGraph(reader, nrpFilename, currentMapVersion);
		}
		catch (EndOfStreamException) {
			Warning($"AI node graph {nrpFilename} is truncated\n");
		}
	}

	void LoadNetworkGraph(BinaryReader reader, ReadOnlySpan<char> nrpFilename, int currentMapVersion) {
		int version = reader.ReadInt32();

		if (version != AINET_VERSION_NUMBER) {
			DevMsg($"AI node graph {nrpFilename} is out of date\n");
			return;
		}

		int mapversion = reader.ReadInt32();

		if (mapversion != currentMapVersion && !g_ai_norebuildgraph.GetBool()) {
			DevMsg($"AI node graph {nrpFilename} is out of date (map version changed)\n");
			return;
		}

		int numNodes = reader.ReadInt32();

		if (numNodes > AI_Network.MAX_NODES || numNodes < 0) {
			Error($"AI node graph {nrpFilename} is corrupt\n");
			Assert(false);
			return;
		}

		int node;
		for (node = 0; node < numNodes; node++) {
			Vector3 origin;
			float yaw;
			origin.X = reader.ReadSingle();
			origin.Y = reader.ReadSingle();
			origin.Z = reader.ReadSingle();
			yaw = reader.ReadSingle();

			AI_Node newNode = Network.AddNode(origin, yaw);

			for (int hull = 0; hull < newNode.VOffset.Length; hull++)
				newNode.VOffset[hull] = reader.ReadSingle();
			newNode.NodeType = (NodeType)reader.ReadByte();

			newNode.NodeInfo = (NodeInfoBits)reader.ReadUInt16();
			newNode.Zone = reader.ReadInt16();
		}

		int totalNumLinks = reader.ReadInt32();

		for (int link = 0; link < totalNumLinks; link++) {
			int srcID, destID;

			srcID = reader.ReadInt16();
			destID = reader.ReadInt16();

			short newLink = Network.CreateLink(srcID, destID);

			for (int hull = 0; hull < (int)AI_HullType.NumHulls; hull++) {
				Capability moveTypes = (Capability)reader.ReadByte();
				if (newLink != AI_Network.NO_LINK)
					Network.GetLink(newLink).AcceptedMoveTypes[hull] = moveTypes;
			}
		}

		GetEditOps().NodeIndexTable = new int[Math.Max(Network.NumNodes(), 1)];

		for (node = 0; node < Network.NumNodes(); node++)
			GetEditOps().NodeIndexTable![node] = reader.ReadInt32();

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
		AI_DynamicLink.gm_bInitialized = false;
	}

	public static void DeleteAllAINetworks() {
		AI_DynamicLink.gm_bInitialized = false;
		gm_fNetworksLoaded = false;
		g_pBigAINet = null;
	}

	public override void Spawn() {
		SetSolid(Source.SolidType.None);
		SetMoveType(Source.MoveType.None);
	}

	void DelayedInit() {
		if (!g_pGameRules.FAllowNPCs()) {
			SetThink(null);
			return;
		}

		if (!g_ai_norebuildgraph.GetBool() && !NetworksLoaded()) {
			SetThink(null);
			return;
		}

		AI_DynamicLink.InitDynamicLinks();

		Initalized = true;

		if (g_AI_Manager.NumAIs() != 0 && g_pBigAINet!.NumNodes() == 0)
			DevMsg("WARNING: Level contains NPCs but has no path nodes\n");
	}

	public static bool NetworksLoaded() => gm_fNetworksLoaded;
	public bool IsInitialized() => Initalized;

	public AI_NetworkEditTools GetEditOps() => EditOps;
	public AI_Network GetNetwork() => Network;

	static bool gm_fNetworksLoaded;

	readonly AI_Network Network = new();
	readonly AI_NetworkEditTools EditOps;
	bool Initalized;
}
