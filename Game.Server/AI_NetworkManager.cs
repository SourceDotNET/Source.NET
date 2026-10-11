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
