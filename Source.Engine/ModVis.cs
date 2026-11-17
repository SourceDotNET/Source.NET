global using static Source.Engine.ModVisGlobals;
global using static Source.Engine.ModVis;

using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;

using System.Numerics;
using System.Runtime.InteropServices;

namespace Source.Engine;

public struct VisCluster
{
	public Vector3 Origin;
	public int ViewCluster;
	public int OldViewCluster;
}

public class VisInfo
{
	public int NumClusters;
	public int OldNumClusters;
	public readonly VisCluster[] VisClusters = new VisCluster[MAX_VIS_LEAVES];
	public readonly byte[] CurrentVis = new byte[BSPFileCommon.MAX_MAP_LEAFS / 8];
	public bool SkyVisible;
	public bool ForceFullSky;
}

public class VisCacheEntry
{
	public int NumClusters;
	public readonly int[] OriginClusters = new int[MAX_VIS_LEAVES];
	public readonly List<BSPMLeaf> LeafList = [];
	public readonly List<BSPMNode> NodeList = [];
}

public static class ModVisGlobals
{
	public const int MAX_VIS_LEAVES = 32;
	public const int VISCACHE_SIZE = 8;

	public static int r_visframecount = 0;

	public static readonly ConVar r_novis = new("r_novis", "0", FCvar.Cheat, "Turn off the PVS.");
	public static readonly ConVar r_lockpvs = new("r_lockpvs", "0", FCvar.Cheat, "Lock the PVS so you can fly around and inspect what is being drawn.");
}

public static class ModVis
{
	static readonly VisInfo vis = new();
	static readonly LinkedList<VisCacheEntry> viscache = new();

	static void SortVisViewClusters() {
		Span<VisCluster> visClusters = vis.VisClusters;
		for (int i = 1; i < vis.NumClusters; ++i) {
			int t = visClusters[i].ViewCluster;
			int j = i;
			while (j > 0 && visClusters[j - 1].ViewCluster > t) {
				visClusters[j].ViewCluster = visClusters[j - 1].ViewCluster;
				--j;
			}
			visClusters[j].ViewCluster = t;
		}
	}

	static void VisMark_Cached(VisCacheEntry cache, WorldBrushData worldBrush) {
		int count, visframe;

		visframe = r_visframecount;

		count = cache.LeafList.Count;
		Span<BSPMLeaf> leafSrc = cache.LeafList.AsSpan();

		int i = 0;
		while (count >= 8) {
			leafSrc[i + 0].VisFrame = visframe;
			leafSrc[i + 1].VisFrame = visframe;
			leafSrc[i + 2].VisFrame = visframe;
			leafSrc[i + 3].VisFrame = visframe;
			leafSrc[i + 4].VisFrame = visframe;
			leafSrc[i + 5].VisFrame = visframe;
			leafSrc[i + 6].VisFrame = visframe;
			leafSrc[i + 7].VisFrame = visframe;
			i += 8;
			count -= 8;
		}
		while (count != 0) {
			leafSrc[i].VisFrame = visframe;
			count--;
			i++;
		}

		count = cache.NodeList.Count;
		Span<BSPMNode> nodeSrc = cache.NodeList.AsSpan();

		i = 0;
		while (count >= 8) {
			nodeSrc[i + 0].VisFrame = visframe;
			nodeSrc[i + 1].VisFrame = visframe;
			nodeSrc[i + 2].VisFrame = visframe;
			nodeSrc[i + 3].VisFrame = visframe;
			nodeSrc[i + 4].VisFrame = visframe;
			nodeSrc[i + 5].VisFrame = visframe;
			nodeSrc[i + 6].VisFrame = visframe;
			nodeSrc[i + 7].VisFrame = visframe;
			i += 8;
			count -= 8;
		}
		while (count != 0) {
			nodeSrc[i].VisFrame = visframe;
			count--;
			i++;
		}
	}

	static void VisCache_Build(VisCacheEntry cache, WorldBrushData worldBrush) {
		int i;
		int cluster;

		cache.NumClusters = vis.NumClusters;
		for (i = 0; i < vis.NumClusters; ++i) {
			cache.OriginClusters[i] = vis.VisClusters[i].ViewCluster;
		}

		cache.LeafList.Clear();
		cache.NodeList.Clear();

		int visframe = r_visframecount;

		BSPMLeaf[] leafs = worldBrush.Leafs!;
		for (i = 0; i < worldBrush.NumLeafs; i++) {
			BSPMLeaf leaf = leafs[i];
			cluster = leaf.Cluster;
			if (cluster == -1)
				continue;

			if ((vis.CurrentVis[cluster >> 3] & (1 << (cluster & 7))) != 0) {
				leaf.VisFrame = visframe;
				cache.LeafList.Add(leaf);
				BSPMNode? node = leaf.Parent;
				while (node != null && node.VisFrame != visframe) {
					cache.NodeList.Add(node);
					node.VisFrame = visframe;
					node = node.Parent;
				}
			}
		}
	}

	public static bool Map_AreAnyLeavesVisible(WorldBrushData worldBrush, ReadOnlySpan<int> leafList, int numLeaves) {
		for (int i = 0; i < numLeaves; i++) {
			BSPMLeaf leaf = worldBrush.Leafs![leafList[i]];
			int cluster = leaf.Cluster;
			if (cluster == -1)
				continue;

			if ((vis.CurrentVis[cluster >> 3] & (1 << (cluster & 7))) != 0)
				return true;
		}
		return false;
	}

	public static void Map_VisMark(bool forceNoVis, Model? worldModel) {
		int i, c;

		if (r_lockpvs.GetInt() != 0)
			return;

		SortVisViewClusters();

		bool outsideWorld = false;
		for (i = 0; i < vis.NumClusters; i++) {
			if (vis.VisClusters[i].ViewCluster != vis.VisClusters[i].OldViewCluster) {
				break;
			}
		}

		if (i >= vis.NumClusters && !forceNoVis && (vis.NumClusters == vis.OldNumClusters))
			return;

		r_visframecount++;

		vis.OldNumClusters = vis.NumClusters;
		for (i = 0; i < vis.NumClusters; i++) {
			vis.VisClusters[i].OldViewCluster = vis.VisClusters[i].ViewCluster;
			if (vis.VisClusters[i].ViewCluster == -1) {
				outsideWorld = true;
				break;
			}
		}

		WorldBrushData shared = worldModel!.Brush.Shared!;

		if (r_novis.GetInt() != 0 || forceNoVis || outsideWorld) {
			for (i = 0; i < shared.NumLeafs; i++)
				shared.Leafs![i].VisFrame = r_visframecount;
			for (i = 0; i < shared.NumNodes; i++)
				shared.Nodes![i].VisFrame = r_visframecount;
			return;
		}

		Assert(vis.NumClusters >= 1);

		CM.Vis(vis.CurrentVis, vis.CurrentVis.Length, vis.VisClusters[0].ViewCluster, CM.DVIS_PVS);

		c = (CM.NumClusters() + 31) / 32;

		Span<byte> mapVis = stackalloc byte[BSPFileCommon.MAX_MAP_CLUSTERS / 8];
		for (i = 1; i < vis.NumClusters; i++) {
			mapVis.Clear();

			CM.Vis(mapVis, mapVis.Length, vis.VisClusters[i].ViewCluster, CM.DVIS_PVS);

			Span<int> currentVis = MemoryMarshal.Cast<byte, int>(vis.CurrentVis.AsSpan());
			Span<int> mapVisInt = MemoryMarshal.Cast<byte, int>(mapVis);
			for (int j = 0; j < c; j++)
				currentVis[j] |= mapVisInt[j];
		}

		for (LinkedListNode<VisCacheEntry>? cacheNode = viscache.First; cacheNode != null; cacheNode = cacheNode.Next) {
			VisCacheEntry cache = cacheNode.Value;
			if (cache.NumClusters != vis.NumClusters)
				continue;
			bool match = true;
			for (c = 0; c < cache.NumClusters; ++c) {
				if (cache.OriginClusters[c] != vis.VisClusters[c].ViewCluster) {
					match = false;
					break;
				}
			}
			if (!match)
				continue;

			viscache.Remove(cacheNode);
			viscache.AddFirst(cacheNode);
			VisMark_Cached(cache, shared);

			return;
		}

		if (viscache.Count < VISCACHE_SIZE)
			viscache.AddFirst(new VisCacheEntry());
		else {
			LinkedListNode<VisCacheEntry> tail = viscache.Last!;
			viscache.Remove(tail);
			viscache.AddFirst(tail);
		}

		VisCache_Build(viscache.First!.Value, shared);
	}

	public static void Map_VisSetup(Model? worldModel, ReadOnlySpan<Vector3> origins, bool forceNoVis, out uint returnFlags) {
		Assert(origins.Length <= MAX_VIS_LEAVES);

		vis.NumClusters = Math.Min(origins.Length, MAX_VIS_LEAVES);
		vis.ForceFullSky = false;
		vis.SkyVisible = false;
		returnFlags = 0;
		for (int i = 0; i < vis.NumClusters; i++) {
			int leafIndex = CM.PointLeafnum(origins[i]);
			int flags = CM.LeafFlags(leafIndex);
			if ((flags & (BSPFileCommon.LEAF_FLAGS_SKY | BSPFileCommon.LEAF_FLAGS_SKY2D)) != 0) {
				vis.SkyVisible = true;
			}
			if ((flags & BSPFileCommon.LEAF_FLAGS_RADIAL) != 0) {
				vis.ForceFullSky = true;
				returnFlags |= IRenderView.VIEW_SETUP_VIS_EX_RETURN_FLAGS_USES_RADIAL_VIS;
			}
			vis.VisClusters[i].ViewCluster = CM.LeafCluster(leafIndex);
			vis.VisClusters[i].Origin = origins[i];
		}

		if (!vis.SkyVisible) {
			vis.ForceFullSky = false;
		}

		Map_VisMark(forceNoVis, worldModel);
	}

	public static void Map_VisClear() {
		vis.NumClusters = 1;
		vis.OldNumClusters = 1;
		for (int i = 0; i < MAX_VIS_LEAVES; i++) {
			vis.VisClusters[i].OldViewCluster = -2;
			vis.VisClusters[i].Origin = new(0, 0, 0);
			vis.VisClusters[i].ViewCluster = -2;
		}
		viscache.Clear();
	}

	public static Span<byte> Map_VisCurrent() {
		return vis.CurrentVis;
	}

	static int VisClusterWarningCount = 0;
	public static int Map_VisCurrentCluster() {
		Assert(vis.VisClusters[0].ViewCluster >= 0);
		if (vis.VisClusters[0].ViewCluster < 0) {
			if (++VisClusterWarningCount <= 5)
				ConDMsg("Map_VisCurrentCluster() < 0!\n");
		}
		return vis.VisClusters[0].ViewCluster;
	}

	public static bool Map_VisSkyVisible() => vis.SkyVisible;
	public static bool Map_VisForceFullSky() => vis.ForceFullSky;
}
