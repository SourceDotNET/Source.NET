global using static Source.Engine.CModelPrivateGlobals;

using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

using TraceCounter_t = uint;
using TraceCounterVec = uint[];

namespace Source.Engine;

internal class TraceVisits
{
	public PlayerBitSet m_Brushes;
	public VarBitSet m_Disps;
}


internal class TraceInfo : IPoolableObject
{
	public void Init() { }
	public void Reset() { }

	public Vector3 Start;
	public Vector3 End;
	public Vector3 Mins;
	public Vector3 Maxs;
	public Vector3 Extents;
	public Vector3 Delta;
	public Vector3 InvDelta;

	public Trace Trace;
	public Trace StabTrace;

	public Contents Contents;
	public bool IsPoint;
	public bool IsSwept;

	// BSP Data
	public CollisionBSPData? BSPData;

	// Displacement Data
	public Vector3 DispStabDir;       // the direction to stab in
	public int DispHit;             // hit displacement surface last

	public bool CheckPrimary;
	public int CheckDepth = -1;
	public readonly TraceCounter_t[] Count = new TraceCounter_t[MAX_CHECK_COUNT_DEPTH];
	public readonly TraceCounterVec[] BrushCounters = NewCounters();
	public readonly TraceCounterVec[] DispCounters = NewCounters();

	static TraceCounterVec[] NewCounters() {
		TraceCounterVec[] counters = new TraceCounterVec[MAX_CHECK_COUNT_DEPTH];
		Array.Fill(counters, []);
		return counters;
	}

	public TraceCounter_t GetCount() => Count[CheckDepth];
	public Span<TraceCounter_t> GetBrushCounters() => BrushCounters[CheckDepth].AsSpan();
	public Span<TraceCounter_t> GetDispCounters() => DispCounters[CheckDepth].AsSpan();

	public bool Visit(ref CollisionBrush brush /* ?? */, int ndxBrush, TraceCounter_t cachedCount, Span<TraceCounter_t> cachedCounters) {
		ref TraceCounter_t counter = ref cachedCounters[ndxBrush];

		if (counter == cachedCount)
			return false;

		counter = cachedCount;
		return true;
	}

	public bool Visit(int dispCounter, TraceCounter_t cachedCount, Span<TraceCounter_t> cachedCounters) {
		ref TraceCounter_t counter = ref cachedCounters[dispCounter];

		if (counter == cachedCount)
			return false;

		counter = cachedCount;
		return true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Visit(ref CollisionBrush brush /* ?? */, int ndxBrush) => Visit(ref brush, ndxBrush, GetCount(), GetBrushCounters());
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public bool Visit(int ndxBrush) => Visit(ndxBrush, GetCount(), GetBrushCounters());
}

public ref struct LeafNums
{
	public int LeafTopNode;
	public int LeafMaxCount;
	public Span<int> LeafList;
	public CollisionBSPData BSPData;
}

public static class CModelPrivateGlobals
{
	public const int MAX_CHECK_COUNT_DEPTH = 2;
	public const int NEVER_UPDATED = -99999;

	public static AlignedBBox[]? g_DispBounds = null;
	public static DispCollTree[]? g_DispCollTrees = null;
}

public struct AlignedBBox
{
	public Vector3 Mins;
	public Vector3 Maxs;
	public int DispCounter;
	public Contents DispContents;
	public void SetCounter(int counter) => DispCounter = counter;
	public int GetCounter() => DispCounter;
	public void SetContents(Contents contents) => DispContents = contents;
	public Contents GetContents() => DispContents;
	public void Init(in Vector3 minsIn, in Vector3 maxsIn, int counterIn, Contents contentsIn) {
		Mins = minsIn;
		SetCounter(counterIn);
		Maxs = maxsIn;
		SetContents(contentsIn);
	}
}

class VirtualTerrain : IVirtualMeshEvent
{
	public void GetVirtualMesh(object userData, ref VirtualMeshList list) {
		int index = (int)userData;
		Assert(index >= 0 && index < CM.g_DispCollTreeCount);
		g_DispCollTrees![index].GetVirtualMeshList(ref list);
	}
	public void GetWorldspaceBounds(object userData, out Vector3 mins, out Vector3 maxs) {
		int index = (int)userData;
		mins = g_DispBounds![index].Mins;
		maxs = g_DispBounds[index].Maxs;
	}
	public void GetTrianglesInSphere(object userData, in Vector3 center, float radius, ref VirtualMeshTriangleList list) {
		throw new NotImplementedException();
	}
}

public static partial class CM
{
	static readonly ObjectPool<TraceInfo> g_TraceInfoPool = new();
	public static int PointLeafnum(in Vector3 point) {
		CollisionBSPData bspData = GetCollisionBSPData();
		if (bspData.NumPlanes == 0)
			return 0;
		return PointLeafnum_r(bspData, in point, 0);
	}

	public static ReadOnlyMemory<byte> EntityString() => GetCollisionBSPData()!.MapEntityData;

	public static int PointLeafnum_r(CollisionBSPData bspData, in Vector3 point, int num) {
		float d;
		ref CollisionNode node = ref Unsafe.NullRef<CollisionNode>();
		ref CollisionPlane plane = ref Unsafe.NullRef<CollisionPlane>();

		while (num >= 0) {
			node = ref bspData.MapNodes.AsSpan()[bspData.MapRootNode + num];
			plane = ref bspData.MapPlanes.AsSpan()[node.CollisionPlaneIdx];

			if ((int)plane.Type < 3)
				d = point[(int)plane.Type] - plane.Dist;
			else
				d = Vector3.Dot(plane.Normal, point) - plane.Dist;

			if (d < 0)
				num = node.Children[1];
			else
				num = node.Children[0];
		}

		return -1 - num;
	}

	public static Contents LeafContents(int leafnum) {
		CollisionBSPData bspData = GetCollisionBSPData();

		Assert(leafnum >= 0);
		Assert(leafnum < bspData.NumLeafs);

		return bspData.MapLeafs.AsSpan()[leafnum].Contents;
	}

	public static int LeafCluster(int leafnum) {
		CollisionBSPData bspData = GetCollisionBSPData();

		Assert(leafnum >= 0);
		Assert(leafnum < bspData.NumLeafs);

		return bspData.MapLeafs.AsSpan()[leafnum].Cluster;
	}

	static void SnapPointToReferenceLeaf_r(CollisionBSPData bspData, in Vector3 p, int num, float tolerance, ref Vector3 snapPoint) {
		float d, snapDist;
		ref CollisionNode node = ref Unsafe.NullRef<CollisionNode>();
		ref CollisionPlane plane = ref Unsafe.NullRef<CollisionPlane>();

		while (num >= 0) {
			node = ref bspData.MapNodes.AsSpan()[bspData.MapRootNode + num];
			plane = ref bspData.MapPlanes.AsSpan()[node.CollisionPlaneIdx];

			if ((int)plane.Type < 3) {
				d = p[(int)plane.Type] - plane.Dist;
				snapDist = snapPoint[(int)plane.Type] - plane.Dist;
			}
			else {
				d = MathLib.DotProduct(plane.Normal, p) - plane.Dist;
				snapDist = MathLib.DotProduct(plane.Normal, snapPoint) - plane.Dist;
			}

			if (d < 0) {
				num = node.Children[1];
				if (snapDist > 0)
					snapPoint -= plane.Normal * (snapDist + tolerance);
			}
			else {
				num = node.Children[0];
				if (snapDist < 0)
					snapPoint += plane.Normal * (-snapDist + tolerance);
			}
		}
	}

	public static void SnapPointToReferenceLeaf(in Vector3 referenceLeafPoint, float tolerance, ref Vector3 snapPoint) {
		CollisionBSPData bspData = GetCollisionBSPData();

		if (bspData.NumPlanes != 0)
			SnapPointToReferenceLeaf_r(bspData, referenceLeafPoint, 0, tolerance, ref snapPoint);
	}

	public static int LeafFlags(int leafnum) {
		CollisionBSPData bspData = GetCollisionBSPData();

		Assert(leafnum >= 0);
		Assert(leafnum < bspData.NumLeafs);

		return bspData.MapLeafs.AsSpan()[leafnum].Flags;
	}

	public static int LeafArea(int leafnum) {
		CollisionBSPData bspData = GetCollisionBSPData();

		Assert(leafnum >= 0);
		Assert(leafnum < bspData.NumLeafs);

		return bspData.MapLeafs.AsSpan()[leafnum].Area;
	}

	public static int NumClusters() => GetCollisionBSPData().NumClusters;

	public const int DVIS_PVS = 0;
	public const int DVIS_PAS = 1;

	static void NullVis(CollisionBSPData bspData, Span<byte> outVis) {
		int numClusterBytes = (bspData.NumClusters + 7) >> 3;
		int outIdx = 0;

		while (numClusterBytes != 0) {
			outVis[outIdx++] = 0xff;
			numClusterBytes--;
		}
	}

	static void DecompressVis(CollisionBSPData bspData, int cluster, int visType, Span<byte> outVis) {
		int c;
		int numClusterBytes;

		if (cluster > bspData.NumClusters || cluster < 0) {
			NullVis(bspData, outVis);
			return;
		}

		if (bspData.NumVisibility == 0 || bspData.MapVis == null) {
			NullVis(bspData, outVis);
			return;
		}

		byte[] mapVis = bspData.MapVis;
		int inIdx = BitConverter.ToInt32(mapVis, 4 + (cluster * 2 + visType) * 4);
		numClusterBytes = (bspData.NumClusters + 7) >> 3;
		int outIdx = 0;

		do {
			if (mapVis[inIdx] != 0) {
				outVis[outIdx++] = mapVis[inIdx++];
				continue;
			}

			c = mapVis[inIdx + 1];
			inIdx += 2;
			if (outIdx + c > numClusterBytes) {
				c = numClusterBytes - outIdx;
				ConMsg("warning: Vis decompression overrun\n");
			}
			while (c != 0) {
				outVis[outIdx++] = 0;
				c--;
			}
		} while (outIdx < numClusterBytes);
	}

	private static readonly byte[] PVSRow = new byte[BSPFileCommon.MAX_MAP_LEAFS / 8];

	public static int ClusterPVSSize() => PVSRow.Length;

	public static Span<byte> ClusterPVS(int cluster) => Vis(PVSRow, ClusterPVSSize(), cluster, DVIS_PVS);

	public static Span<byte> Vis(Span<byte> dest, int destlen, int cluster, int visType) {
		CollisionBSPData bspData = GetCollisionBSPData();

		if (visType > DVIS_PAS || visType < DVIS_PVS) {
			Sys.Error($"CM_Vis: Vis type {visType} is unknown");
			return null;
		}

		if (cluster == -1) {
			int len = (bspData.NumClusters + 7) >> 3;
			if (len > destlen)
				Sys.Error($"CM_Vis:  buffer not big enough ({destlen} but need {len})\n");
			memset(dest[..((bspData.NumClusters + 7) >> 3)], (byte)0);
		}
		else
			DecompressVis(bspData, cluster, visType, dest);

		return dest;
	}

	public static nint g_DispCollTreeCount = 0;

	static readonly VirtualTerrain g_VirtualTerrain = new();
	static PhysCollide?[] g_TerrainList = [];

	public static PhysCollide? PhysCollideForDisp(int index) {
		if (index < 0 || index >= g_DispCollTreeCount)
			return null;

		return g_TerrainList[index];
	}

	public static void CreateDispPhysCollide(int dispLumpSize) {
		g_TerrainList = new PhysCollide?[g_DispCollTreeCount];
		for (int i = 0; i < g_DispCollTreeCount; i++) {
			DispCollTree dispTree = g_DispCollTrees![i];
			if (dispTree != null && dispTree.CheckFlags(CoreDispInfo.SURF_NOPHYSICS_COLL)) {
				g_TerrainList[i] = null;
				continue;
			}
			VirtualMeshParams meshParams = default;
			meshParams.MeshEventHandler = g_VirtualTerrain;
			meshParams.UserData = i;
			meshParams.BuildOuterHull = dispLumpSize > 0 ? false : true;

			g_TerrainList[i] = physcollision.CreateVirtualMesh(meshParams);
		}
	}

	public static void DestroyDispPhysCollide() {
		for (int i = g_TerrainList.Length - 1; i >= 0; --i) {
			PhysCollide? collide = g_TerrainList[i];
			if (collide != null)
				physcollision.DestroyCollide(collide);
		}
		g_TerrainList = [];
	}

	internal static TraceInfo BeginTrace() {
		TraceInfo traceInfo = g_TraceInfoPool.Alloc();

		if (traceInfo.BrushCounters[0].Length != GetCollisionBSPData().NumBrushes + 1) {
			memreset(traceInfo.Count);
			traceInfo.CheckDepth = -1;

			for (int i = 0; i < MAX_CHECK_COUNT_DEPTH; i++) {
				traceInfo.BrushCounters[i] = new TraceCounter_t[GetCollisionBSPData().NumBrushes + 1];
				traceInfo.DispCounters[i] = new TraceCounter_t[g_DispCollTreeCount];
			}
		}

		PushTraceVisits(traceInfo);
		return traceInfo;
	}

	internal static unsafe int BoxLeafnums(ref LeafNums context, in Vector3 center, in Vector3 extents, int nodenum) {
		int leafCount = 0;
		const int NODELIST_MAX = 1024;
		Span<int> nodeList = stackalloc int[NODELIST_MAX];
		int nodeReadIndex = 0;
		int nodeWriteIndex = 0;
		ref CollisionPlane plane = ref Unsafe.NullRef<CollisionPlane>();
		ref CollisionNode node = ref Unsafe.NullRef<CollisionNode>();
		int prev_topnode = -1;

		while (true) {
			if (nodenum < 0) {
				// This handles the case when the box lies completely
				// within a single node. In that case, the top node should be
				// the parent of the leaf
				if (context.LeafTopNode == -1)
					context.LeafTopNode = prev_topnode;

				if (leafCount < context.LeafMaxCount) {
					context.LeafList[leafCount] = -1 - nodenum;
					leafCount++;
				}
				if (nodeReadIndex == nodeWriteIndex)
					return leafCount;
				nodenum = nodeList[nodeReadIndex];
				nodeReadIndex = (nodeReadIndex + 1) & (NODELIST_MAX - 1);
			}
			else {
				node = ref context.BSPData.MapNodes.AsSpan()[nodenum];
				plane = ref context.BSPData.MapPlanes.AsSpan()[node.CollisionPlaneIdx];
				//		s = BoxOnPlaneSide (leaf_mins, leaf_maxs, plane);
				//		s = BOX_ON_PLANE_SIDE(*leaf_mins, *leaf_maxs, plane);
				float d0 = MathLib.DotProduct(plane.Normal, center) - plane.Dist;
				float d1 = MathLib.DotProductAbs(plane.Normal, extents);
				prev_topnode = nodenum;
				if (d0 >= d1)
					nodenum = node.Children[0];
				else if (d0 < -d1)
					nodenum = node.Children[1];
				else {  // go down both
					if (context.LeafTopNode == -1)
						context.LeafTopNode = nodenum;
					nodeList[nodeWriteIndex] = node.Children[0];
					nodeWriteIndex = (nodeWriteIndex + 1) & (NODELIST_MAX - 1);
					// check for overflow of the ring buffer
					Assert(nodeWriteIndex != nodeReadIndex);
					nodenum = node.Children[1];
				}
			}
		}
	}
	static bool IsTraceBoxIntersectingBoxBrush(TraceInfo traceInfo, ref CollisionBoxBrush box) {
		var start = MathLib.LoadFloat3(traceInfo.Start);
		var mins = MathLib.LoadFloat3(traceInfo.Mins);
		var maxs = MathLib.LoadFloat3(traceInfo.Maxs);

		var boxMins = MathLib.LoadFloat3(box.Mins);
		var boxMaxs = MathLib.LoadFloat3(box.Maxs);
		var offsetMins = MathLib.AddSIMD(mins, start);
		var offsetMaxs = MathLib.AddSIMD(maxs, start);
		var minsOut = MathLib.MaxSIMD(boxMins, offsetMins);
		var maxsOut = MathLib.MinSIMD(boxMaxs, offsetMaxs);
		var separated = MathLib.CmpGtSIMD(minsOut, maxsOut);
		var sep3 = MathLib.SetWToZeroSIMD(separated);
		return MathLib.IsAllZeros(sep3);
	}

	internal static unsafe void TestBoxInBrush(TraceInfo traceInfo, in CollisionBrush brush) {
		if (brush.IsBox()) {
			ref CollisionBoxBrush box = ref traceInfo.BSPData!.MapBoxBrushes.AsSpan()[brush.GetBox()];
			if (!IsTraceBoxIntersectingBoxBrush(traceInfo, ref box))
				return;
		}
		else {
			if (brush.NumSides == 0)
				return;
			ref readonly Vector3 mins = ref traceInfo.Mins;
			ref readonly Vector3 maxs = ref traceInfo.Maxs;
			ref readonly Vector3 p1 = ref traceInfo.Start;
			int i, j;

			ref CollisionPlane plane = ref Unsafe.NullRef<CollisionPlane>();
			float dist;
			Vector3 ofs = new(0, 0, 0);
			float d1;
			ref CollisionBrushSide side = ref Unsafe.NullRef<CollisionBrushSide>();

			for (i = 0; i < brush.NumSides; i++) {
				side = ref traceInfo.BSPData!.MapBrushSides.AsSpan()[brush.FirstBrushSide + i];
				plane = ref side.Plane;

				// FIXME: special case for axial

				// general box case

				// push the plane out appropriately for mins/maxs

				// FIXME: use signbits into 8 way lookup for each mins/maxs
				for (j = 0; j < 3; j++) {
					if (plane.Normal[j] < 0)
						ofs[j] = maxs[j];
					else
						ofs[j] = mins[j];
				}
				dist = MathLib.DotProduct(ofs, plane.Normal);
				dist = plane.Dist - dist;

				d1 = MathLib.DotProduct(p1, plane.Normal) - dist;

				// if completely in front of face, no intersection
				if (d1 > 0)
					return;

			}
		}

		// inside this brush
		ref Trace trace = ref traceInfo.Trace;
		trace.StartSolid = trace.AllSolid = true;
		trace.Fraction = 0;
		trace.FractionLeftSolid = 1.0f;
		trace.Contents = brush.Contents;
	}
	internal static unsafe void TestInLeaf(TraceInfo traceInfo, int ndxLeaf) {
		ref CollisionLeaf leaf = ref traceInfo.BSPData.MapLeafs.AsSpan()[ndxLeaf];

		// trace ray/box sweep against all brushes in this leaf
		Span<TraceCounter_t> counters = traceInfo.GetBrushCounters();
		TraceCounter_t count = traceInfo.GetCount();
		for (int ndxLeafBrush = 0; ndxLeafBrush < leaf.NumLeafBrushes; ndxLeafBrush++) {
			// get the current brush
			int ndxBrush = traceInfo.BSPData.MapLeafBrushes[leaf.FirstLeafBrush + ndxLeafBrush];
			ref CollisionBrush brush = ref traceInfo.BSPData.MapBrushes.AsSpan()[ndxBrush];

			// make sure we only check this brush once per trace/stab
			if (!traceInfo.Visit(ref brush, ndxBrush, count, counters))
				continue;

			// only collide with objects you are interested in
			if ((brush.Contents & traceInfo.Contents) == 0)
				continue;

			// test to see if the point/box is inside of any solid
			// NOTE: pTraceInfo->m_trace.fraction == 0.0f only when trace starts inside of a brush!
			TestBoxInBrush(traceInfo, brush);
			if (traceInfo.Trace.Fraction == 0)
				return;
		}

		// TODO: this may be redundant
		if (traceInfo.Trace.StartSolid)
			return;

		// if there are no displacement surfaces in this leaf -- we are done testing
		if (leaf.DispCount != 0)
			// test to see if the point/box is inside of any of the displacement surface
			TestInDispTree(traceInfo, ref leaf, traceInfo.Start, traceInfo.Mins, traceInfo.Maxs, traceInfo.Contents, ref traceInfo.Trace);

	}
	internal static void TestInDispTree(TraceInfo traceInfo, ref CollisionLeaf leaf, in Vector3 traceStart, in Vector3 boxMin, in Vector3 boxMax, Contents collisionMask, ref Trace trace) {
		bool isBox = ((boxMin.X != 0.0f) || (boxMin.Y != 0.0f) || (boxMin.Z != 0.0f) ||
		(boxMax.X != 0.0f) || (boxMax.Y != 0.0f) || (boxMax.Z != 0.0f));

		// box test
		if (isBox) {
			// Box/Tree intersection test.
			Vector3 absMins = traceStart + boxMin;
			Vector3 absMaxs = traceStart + boxMax;

			// Test box against all displacements in the leaf.
			Span<TraceCounter_t> counters = traceInfo.GetDispCounters();
			int count = (int)traceInfo.GetCount();
			for (int i = 0; i < leaf.DispCount; i++) {
				int dispIndex = traceInfo.BSPData!.MapDispList[leaf.DispListStart + i];
				ref AlignedBBox dispBounds = ref g_DispBounds![dispIndex];

				// Respect trace contents
				if (0 == (dispBounds.GetContents() & collisionMask))
					continue;

				if (!traceInfo.Visit(dispBounds.GetCounter(), (TraceCounter_t)count, counters))
					continue;

				if (CollisionUtils.IsBoxIntersectingBox(absMins, absMaxs, dispBounds.Mins, dispBounds.Maxs)) {
					DispCollTree dispTree = g_DispCollTrees![dispIndex];
					if (dispTree.AABBTree_IntersectAABB(absMins, absMaxs)) {
						trace.StartSolid = true;
						trace.AllSolid = true;
						trace.Fraction = 0.0f;
						trace.FractionLeftSolid = 0.0f;
						trace.Contents = dispTree.GetContents();
						return;
					}
				}
			}
		}

		//
		// need to stab if is was a point test or the box test yeilded no intersection
		//
		Vector3 stabDir;
		Contents contents;
		PreStab(traceInfo, ref leaf, out stabDir, collisionMask, out contents);
		Stab(traceInfo, traceStart, stabDir, contents);
		PostStab(traceInfo);
	}
	internal static void PreStab(TraceInfo traceInfo, ref CollisionLeaf leaf, out Vector3 stabDir, Contents collisionMask, out Contents contents) {
		if (0 == leaf.DispCount) {
			stabDir = default;
			contents = default;
			return;
		}

		// if the point wasn't in the bounded area of any of the displacements -- stab in any
		// direction and set contents to "solid"
		int dispIndex = traceInfo.BSPData!.MapDispList[leaf.DispListStart];
		DispCollTree dispTree = g_DispCollTrees![dispIndex];
		dispTree.GetStabDirection(out stabDir);
		contents = Contents.Solid;

		//
		// if the point is inside a displacement's (in the leaf) bounded area
		// then get the direction to stab from it
		//
		for (int i = 0; i < leaf.DispCount; i++) {
			dispIndex = traceInfo.BSPData!.MapDispList[leaf.DispListStart + i];
			dispTree = g_DispCollTrees[dispIndex];

			// Respect trace contents
			if (0 == (dispTree.GetContents() & collisionMask))
				continue;

			if (dispTree.PointInBounds(traceInfo.Start, traceInfo.Mins, traceInfo.Maxs, traceInfo.IsPoint)) {
				dispTree.GetStabDirection(out stabDir);
				contents = dispTree.GetContents();
				return;
			}
		}
	}
	static Vector3 InvDelta(in Vector3 v) {
		Vector3 vecInvDelta = default;
		for (int iAxis = 0; iAxis < 3; ++iAxis) {
			if (v[iAxis] != 0.0f)
				vecInvDelta[iAxis] = 1.0f / v[iAxis];
			else
				vecInvDelta[iAxis] = float.MaxValue;
		}
		return vecInvDelta;
	}
	internal static void Stab(TraceInfo traceInfo, in Vector3 start, in Vector3 stabDir, Contents contents) {
		// initialize the displacement trace parameters
		traceInfo.Trace.Fraction = 1.0f;
		traceInfo.Trace.FractionLeftSolid = 0.0f;
		traceInfo.Trace.Surface = CollisionBSPData.NullSurface;

		traceInfo.Trace.StartSolid = false;
		traceInfo.Trace.AllSolid = false;

		traceInfo.DispHit = 0;
		traceInfo.DispStabDir = stabDir;

		Vector3 end = traceInfo.End;

		traceInfo.Start = start;
		traceInfo.End = start + (stabDir * /* world extents * 2*/ 99999.9f);
		traceInfo.Delta = traceInfo.End - traceInfo.Start;
		traceInfo.InvDelta = InvDelta(traceInfo.Delta);

		// increment the checkcount -- so we can retest objects that may have been tested
		// previous to the stab
		PushTraceVisits(traceInfo);

		// stab
		RecursiveHullCheck(traceInfo, 0 /*root*/, 0.0f, 1.0f);

		PopTraceVisits(traceInfo);

		traceInfo.End = end;
	}

	internal static void PushTraceVisits(TraceInfo traceInfo) {
		++traceInfo.CheckDepth;
		Assert((traceInfo.CheckDepth >= 0) && (traceInfo.CheckDepth < MAX_CHECK_COUNT_DEPTH));

		int i = traceInfo.CheckDepth;
		traceInfo.Count[i]++;
		if (traceInfo.Count[i] == 0) {
			traceInfo.Count[i]++;
			memreset(traceInfo.BrushCounters[i]);
			memreset(traceInfo.DispCounters[i]);
		}
	}

	internal static void PopTraceVisits(TraceInfo traceInfo) {
		--traceInfo.CheckDepth;
		Assert(traceInfo.CheckDepth >= -1);
	}

	internal static void PostStab(TraceInfo traceInfo) {
		//
		// only need to resolve things that impacted against a displacement surface,
		// this is partially resolved in the post trace phase -- so just use that
		// data to determine
		//
		if (traceInfo.DispHit != 0 && traceInfo.Trace.StartSolid) {
			traceInfo.Trace.AllSolid = true;
			traceInfo.Trace.Fraction = 0.0f;
			traceInfo.Trace.FractionLeftSolid = 0.0f;
		}
		else {
			traceInfo.Trace.StartSolid = false;
			traceInfo.Trace.AllSolid = false;
			traceInfo.Trace.Contents = 0;
			traceInfo.Trace.Fraction = 1.0f;
			traceInfo.Trace.FractionLeftSolid = 0.0f;
		}
	}

	internal static unsafe void UnsweptBoxTrace(TraceInfo traceInfo, in Ray ray, int headnode, Mask brushmask) {
		Span<int> leafs = stackalloc int[1024];
		int i, numleafs;

		LeafNums context;
		context.LeafList = leafs;
		context.LeafTopNode = -1;
		context.LeafMaxCount = leafs.Length;
		context.BSPData = traceInfo.BSPData!;

		bool foundNonSolidLeaf = false;
		numleafs = BoxLeafnums(ref context, ray.Start, ray.Extents + new Vector3(1, 1, 1), headnode);
		for (i = 0; i < numleafs; i++) {
			if ((traceInfo.BSPData!.MapLeafs[leafs[i]].Contents & Contents.Solid) == 0)
				foundNonSolidLeaf = true;

			TestInLeaf(traceInfo, leafs[i]);
			if (traceInfo.Trace.AllSolid)
				break;
		}

		if (!foundNonSolidLeaf) {
			traceInfo.Trace.AllSolid = traceInfo.Trace.StartSolid = true;
			traceInfo.Trace.Fraction = 0.0f;
			traceInfo.Trace.FractionLeftSolid = 1.0f;
		}
	}
	internal static void RecursiveHullCheckImpl(bool IS_POINT, TraceInfo traceInfo, int num, float p1f, float p2f, in Vector3 p1, in Vector3 p2) {
		if (traceInfo.Trace.Fraction <= p1f)
			return;     // already hit something nearer	

		ref CollisionNode node = ref Unsafe.NullRef<CollisionNode>();
		ref CollisionPlane plane = ref Unsafe.NullRef<CollisionPlane>();
		float t1 = 0, t2 = 0, offset = 0;
		float frac, frac2;
		float idist;
		Vector3 mid;
		int side;
		float midf;

		// find the point distances to the seperating plane
		// and the offset for the size of the box

		while (num >= 0) {
			node = ref traceInfo.BSPData!.MapNodes.AsSpan()[traceInfo.BSPData!.MapRootNode + num];
			plane = ref traceInfo.BSPData!.MapPlanes.AsSpan()[node.CollisionPlaneIdx];
			PlaneType type = plane.Type;
			float dist = plane.Dist;

			if ((int)type < 3) {
				t1 = p1[(int)type] - dist;
				t2 = p2[(int)type] - dist;
				offset = traceInfo.Extents[(int)type];
			}
			else {
				t1 = MathLib.DotProduct(plane.Normal, p1) - dist;
				t2 = MathLib.DotProduct(plane.Normal, p2) - dist;
				if (IS_POINT) {
					offset = 0;
				}
				else {
					offset = MathF.Abs(traceInfo.Extents[0] * plane.Normal[0]) +
						MathF.Abs(traceInfo.Extents[1] * plane.Normal[1]) +
						MathF.Abs(traceInfo.Extents[2] * plane.Normal[2]);
				}
			}

			// see which sides we need to consider
			if (t1 > offset && t2 > offset)
			//		if (t1 >= offset && t2 >= offset)
			{
				num = node.Children[0];
				continue;
			}

			if (t1 < -offset && t2 < -offset) {
				num = node.Children[1];
				continue;
			}

			break;
		}

		// if < 0, we are in a leaf node
		if (num < 0) {
			TraceToLeaf(IS_POINT, traceInfo, -1 - num, p1f, p2f);
			return;
		}

		// put the crosspoint DIST_EPSILON pixels on the near side
		if (t1 < t2) {
			idist = 1.0F / (t1 - t2);
			side = 1;
			frac2 = (t1 + offset + DIST_EPSILON) * idist;
			frac = (t1 - offset - DIST_EPSILON) * idist;
		}
		else if (t1 > t2) {
			idist = 1.0F / (t1 - t2);
			side = 0;
			frac2 = (t1 - offset - DIST_EPSILON) * idist;
			frac = (t1 + offset + DIST_EPSILON) * idist;
		}
		else {
			side = 0;
			frac = 1;
			frac2 = 0;
		}

		// move up to the node
		frac = Math.Clamp(frac, 0f, 1f);
		midf = p1f + (p2f - p1f) * frac;
		mid = Vector3.Lerp(p1, p2, frac);

		RecursiveHullCheckImpl(IS_POINT, traceInfo, node.Children[side], p1f, midf, p1, mid);

		// go past the node
		frac2 = Math.Clamp(frac2, 0f, 1f);
		midf = p1f + (p2f - p1f) * frac2;
		mid = Vector3.Lerp(p1, p2, frac2);

		RecursiveHullCheckImpl(IS_POINT, traceInfo, node.Children[side ^ 1], midf, p2f, mid, p2);
	}
	static readonly int[] signbits = [1, 2, 4];
	static readonly fltx4 Four_DistEpsilons = MathLib.LoadFloat4(new Vector4(DIST_EPSILON, DIST_EPSILON, DIST_EPSILON, DIST_EPSILON));
	static readonly int[] g_CubeFaceIndex0 = [0, 1, 2, -1];
	static readonly int[] g_CubeFaceIndex1 = [3, 4, 5, -1];
	internal static bool IntersectRayWithBoxBrush(TraceInfo traceInfo, in CollisionBrush brush, ref CollisionBoxBrush box) {
		// Load the unaligned ray/box parameters into SIMD registers
		fltx4 start = MathLib.LoadFloat3(traceInfo.Start);
		fltx4 extents = MathLib.LoadFloat3(traceInfo.Extents);
		fltx4 delta = MathLib.LoadFloat3(traceInfo.Delta);
		fltx4 boxMins = MathLib.LoadFloat4(box.Mins.AsVector4());
		fltx4 boxMaxs = MathLib.LoadFloat4(box.Maxs.AsVector4());

		// compute the mins/maxs of the box expanded by the ray extents
		// relocate the problem so that the ray start is at the origin.
		fltx4 offsetMins = MathLib.SubSIMD(boxMins, start);
		fltx4 offsetMaxs = MathLib.SubSIMD(boxMaxs, start);
		fltx4 offsetMinsExpanded = MathLib.SubSIMD(offsetMins, extents);
		fltx4 offsetMaxsExpanded = MathLib.AddSIMD(offsetMaxs, extents);

		// Check to see if both the origin (start point) and the end point (delta) are on the front side
		// of any of the box sides - if so there can be no intersection
		fltx4 startOutMins = MathLib.CmpLtSIMD(MathLib.Four_Zeros, offsetMinsExpanded);
		fltx4 endOutMins = MathLib.CmpLtSIMD(delta, offsetMinsExpanded);
		fltx4 minsMask = MathLib.AndSIMD(startOutMins, endOutMins);
		fltx4 startOutMaxs = MathLib.CmpGtSIMD(MathLib.Four_Zeros, offsetMaxsExpanded);
		fltx4 endOutMaxs = MathLib.CmpGtSIMD(delta, offsetMaxsExpanded);
		fltx4 maxsMask = MathLib.AndSIMD(startOutMaxs, endOutMaxs);
		if (MathLib.IsAnyNegative(MathLib.SetWToZeroSIMD(MathLib.OrSIMD(minsMask, maxsMask))))
			return false;

		fltx4 crossPlane = MathLib.OrSIMD(MathLib.XorSIMD(startOutMins, endOutMins), MathLib.XorSIMD(startOutMaxs, endOutMaxs));
		// now build the per-axis interval of t for intersections
		fltx4 invDelta = MathLib.LoadFloat3(traceInfo.InvDelta);
		fltx4 tmins = MathLib.MulSIMD(offsetMinsExpanded, invDelta);
		fltx4 tmaxs = MathLib.MulSIMD(offsetMaxsExpanded, invDelta);
		// now sort the interval per axis
		fltx4 mint = MathLib.MinSIMD(tmins, tmaxs);
		fltx4 maxt = MathLib.MaxSIMD(tmins, tmaxs);
		// only axes where we cross a plane are relevant
		mint = MathLib.MaskedAssign(crossPlane, mint, MathLib.Four_Negative_FLT_MAX);
		maxt = MathLib.MaskedAssign(crossPlane, maxt, MathLib.Four_FLT_MAX);

		// now find the intersection of the intervals on all axes
		fltx4 firstOut = MathLib.FindLowestSIMD3(maxt);
		fltx4 lastIn = MathLib.FindHighestSIMD3(mint);
		// NOTE: This is really a scalar quantity now [t0,t1] == [lastIn,firstOut]
		firstOut = MathLib.MinSIMD(firstOut, MathLib.Four_Ones);
		lastIn = MathLib.MaxSIMD(lastIn, MathLib.Four_Zeros);

		// If the final interval is valid lastIn<firstOut, check for separation
		fltx4 separation = MathLib.CmpGtSIMD(lastIn, firstOut);

		if (MathLib.IsAllZeros(separation)) {
			bool startOut = MathLib.IsAnyNegative(MathLib.SetWToZeroSIMD(MathLib.OrSIMD(startOutMins, startOutMaxs)));
			offsetMinsExpanded = MathLib.SubSIMD(offsetMinsExpanded, Four_DistEpsilons);
			offsetMaxsExpanded = MathLib.AddSIMD(offsetMaxsExpanded, Four_DistEpsilons);

			tmins = MathLib.MulSIMD(offsetMinsExpanded, invDelta);
			tmaxs = MathLib.MulSIMD(offsetMaxsExpanded, invDelta);

			fltx4 minface0 = MathLib.LoadInt4(g_CubeFaceIndex0).As<int, float>();
			fltx4 minface1 = MathLib.LoadInt4(g_CubeFaceIndex1).As<int, float>();
			fltx4 faceMask = MathLib.CmpLeSIMD(tmins, tmaxs);
			mint = MathLib.MinSIMD(tmins, tmaxs);
			maxt = MathLib.MaxSIMD(tmins, tmaxs);
			fltx4 faceId = MathLib.MaskedAssign(faceMask, minface0, minface1);
			// only axes where we cross a plane are relevant
			mint = MathLib.MaskedAssign(crossPlane, mint, MathLib.Four_Negative_FLT_MAX);
			maxt = MathLib.MaskedAssign(crossPlane, maxt, MathLib.Four_FLT_MAX);

			fltx4 firstOutTmp = MathLib.FindLowestSIMD3(maxt);

			// implement FindHighest of 3, but use intermediate masks to find the 
			// corresponding index in faceId to the highest at the same time
			fltx4 compareOne = MathLib.RotateLeft(mint);
			faceMask = MathLib.CmpGtSIMD(mint, compareOne);
			// compareOne is [y,z,G,x]
			fltx4 max_xy = MathLib.MaxSIMD(mint, compareOne);
			fltx4 faceRot = MathLib.RotateLeft(faceId);
			fltx4 faceId_xy = MathLib.MaskedAssign(faceMask, faceId, faceRot);
			// max_xy is [max(x,y), ... ]
			compareOne = MathLib.RotateLeft2(mint);
			faceRot = MathLib.RotateLeft2(faceId);
			// compareOne is [z, G, x, y]
			faceMask = MathLib.CmpGtSIMD(max_xy, compareOne);
			fltx4 max_xyz = MathLib.MaxSIMD(max_xy, compareOne);
			faceId = MathLib.MaskedAssign(faceMask, faceId_xy, faceRot);
			fltx4 lastInTmp = MathLib.SplatXSIMD(max_xyz);

			firstOut = MathLib.MinSIMD(firstOutTmp, MathLib.Four_Ones);
			lastIn = MathLib.MaxSIMD(lastInTmp, MathLib.Four_Zeros);
			separation = MathLib.CmpGtSIMD(lastIn, firstOut);
			Assert(MathLib.IsAllZeros(separation));
			if (MathLib.IsAllZeros(separation)) {
				uint faceIndex = MathLib.SubInt(faceId, 0);
				Assert(faceIndex < 6);
				float t1 = MathLib.SubFloat(ref lastIn, 0);
				ref Trace trace = ref traceInfo.Trace;

				// this condition is copied from the brush case to avoid hitting an assert and
				// overwriting a previous start solid with a new shorter fraction
				if (startOut && traceInfo.IsPoint && trace.FractionLeftSolid > t1)
					startOut = false;

				if (!startOut) {
					float t2 = MathLib.SubFloat(ref firstOut, 0);
					trace.StartSolid = true;
					trace.Contents = brush.Contents;
					if (t2 >= 1.0f) {
						trace.AllSolid = true;
						trace.Fraction = 0.0f;
					}
					else if (t2 > trace.FractionLeftSolid) {
						trace.FractionLeftSolid = t2;
						if (trace.Fraction <= t2) {
							trace.Fraction = 1.0f;
							trace.Surface = CollisionBSPData.NullSurface;
						}
					}
				}
				else {
					if (t1 < trace.Fraction) {
						traceInfo.DispHit = 0;
						trace.Fraction = t1;
						trace.Plane.Normal = vec3_origin;
						trace.Surface = traceInfo.BSPData.GetSurfaceAtIndex(box.SurfaceIndex[(int)faceIndex]);
						if (faceIndex >= 3) {
							faceIndex -= 3;
							trace.Plane.Dist = box.Maxs[(int)faceIndex];
							trace.Plane.Normal[(int)faceIndex] = 1.0f;
							trace.Plane.SignBits = 0;
						}
						else {
							trace.Plane.Dist = -box.Mins[(int)faceIndex];
							trace.Plane.Normal[(int)faceIndex] = -1.0f;
							trace.Plane.SignBits = (byte)signbits[(int)faceIndex];
						}
						trace.Plane.Type = (PlaneType)faceIndex;
						trace.Contents = brush.Contents;
						return true;
					}
				}
			}
		}
		return false;
	}
	// slightly different version of the above.  This folds in more of the trace_t output because CM_ComputeTraceEndpts() isn't called after this
	// so this routine needs to properly compute start/end points and fractions in all cases
	public static bool IntersectRayWithBox(in Ray ray, in Vector3 inInvDelta, in Vector3 inBoxMins, in Vector3 inBoxMaxs, ref Trace trace) {
		// mark trace as not hitting
		trace.StartSolid = false;
		trace.AllSolid = false;
		trace.Fraction = 1.0f;

		// Load the unaligned ray/box parameters into SIMD registers
		fltx4 start = MathLib.LoadFloat3(ray.Start);
		fltx4 extents = MathLib.LoadFloat3(ray.Extents);
		fltx4 delta = MathLib.LoadFloat3(ray.Delta);
		fltx4 boxMins = MathLib.LoadFloat3(inBoxMins);
		fltx4 boxMaxs = MathLib.LoadFloat3(inBoxMaxs);

		// compute the mins/maxs of the box expanded by the ray extents
		// relocate the problem so that the ray start is at the origin.
		fltx4 offsetMins = MathLib.SubSIMD(boxMins, start);
		fltx4 offsetMaxs = MathLib.SubSIMD(boxMaxs, start);
		fltx4 offsetMinsExpanded = MathLib.SubSIMD(offsetMins, extents);
		fltx4 offsetMaxsExpanded = MathLib.AddSIMD(offsetMaxs, extents);

		// Check to see if both the origin (start point) and the end point (delta) are on the front side
		// of any of the box sides - if so there can be no intersection
		fltx4 startOutMins = MathLib.CmpLtSIMD(MathLib.Four_Zeros, offsetMinsExpanded);
		fltx4 endOutMins = MathLib.CmpLtSIMD(delta, offsetMinsExpanded);
		fltx4 minsMask = MathLib.AndSIMD(startOutMins, endOutMins);
		fltx4 startOutMaxs = MathLib.CmpGtSIMD(MathLib.Four_Zeros, offsetMaxsExpanded);
		fltx4 endOutMaxs = MathLib.CmpGtSIMD(delta, offsetMaxsExpanded);
		fltx4 maxsMask = MathLib.AndSIMD(startOutMaxs, endOutMaxs);
		if (MathLib.IsAnyNegative(MathLib.SetWToZeroSIMD(MathLib.OrSIMD(minsMask, maxsMask))))
			return false;

		fltx4 crossPlane = MathLib.OrSIMD(MathLib.XorSIMD(startOutMins, endOutMins), MathLib.XorSIMD(startOutMaxs, endOutMaxs));
		// now build the per-axis interval of t for intersections
		fltx4 invDelta = MathLib.LoadFloat3(inInvDelta);
		fltx4 tmins = MathLib.MulSIMD(offsetMinsExpanded, invDelta);
		fltx4 tmaxs = MathLib.MulSIMD(offsetMaxsExpanded, invDelta);
		// now sort the interval per axis
		fltx4 mint = MathLib.MinSIMD(tmins, tmaxs);
		fltx4 maxt = MathLib.MaxSIMD(tmins, tmaxs);
		// only axes where we cross a plane are relevant
		mint = MathLib.MaskedAssign(crossPlane, mint, MathLib.Four_Negative_FLT_MAX);
		maxt = MathLib.MaskedAssign(crossPlane, maxt, MathLib.Four_FLT_MAX);

		// now find the intersection of the intervals on all axes
		fltx4 firstOut = MathLib.FindLowestSIMD3(maxt);
		fltx4 lastIn = MathLib.FindHighestSIMD3(mint);
		// NOTE: This is really a scalar quantity now [t0,t1] == [lastIn,firstOut]
		firstOut = MathLib.MinSIMD(firstOut, MathLib.Four_Ones);
		lastIn = MathLib.MaxSIMD(lastIn, MathLib.Four_Zeros);

		// If the final interval is valid lastIn<firstOut, check for separation
		fltx4 separation = MathLib.CmpGtSIMD(lastIn, firstOut);

		if (MathLib.IsAllZeros(separation)) {
			bool startOut = MathLib.IsAnyNegative(MathLib.SetWToZeroSIMD(MathLib.OrSIMD(startOutMins, startOutMaxs)));
			offsetMinsExpanded = MathLib.SubSIMD(offsetMinsExpanded, Four_DistEpsilons);
			offsetMaxsExpanded = MathLib.AddSIMD(offsetMaxsExpanded, Four_DistEpsilons);

			tmins = MathLib.MulSIMD(offsetMinsExpanded, invDelta);
			tmaxs = MathLib.MulSIMD(offsetMaxsExpanded, invDelta);

			fltx4 minface0 = MathLib.LoadInt4(g_CubeFaceIndex0).As<int, float>();
			fltx4 minface1 = MathLib.LoadInt4(g_CubeFaceIndex1).As<int, float>();
			fltx4 faceMask = MathLib.CmpLeSIMD(tmins, tmaxs);
			mint = MathLib.MinSIMD(tmins, tmaxs);
			maxt = MathLib.MaxSIMD(tmins, tmaxs);
			fltx4 faceId = MathLib.MaskedAssign(faceMask, minface0, minface1);
			// only axes where we cross a plane are relevant
			mint = MathLib.MaskedAssign(crossPlane, mint, MathLib.Four_Negative_FLT_MAX);
			maxt = MathLib.MaskedAssign(crossPlane, maxt, MathLib.Four_FLT_MAX);

			fltx4 firstOutTmp = MathLib.FindLowestSIMD3(maxt);

			//fltx4 lastInTmp = FindHighestSIMD3(mint);
			// implement FindHighest of 3, but use intermediate masks to find the
			// corresponding index in faceId to the highest at the same time
			fltx4 compareOne = MathLib.RotateLeft(mint);
			faceMask = MathLib.CmpGtSIMD(mint, compareOne);
			// compareOne is [y,z,G,x]
			fltx4 max_xy = MathLib.MaxSIMD(mint, compareOne);
			fltx4 faceRot = MathLib.RotateLeft(faceId);
			fltx4 faceId_xy = MathLib.MaskedAssign(faceMask, faceId, faceRot);
			// max_xy is [max(x,y), ... ]
			compareOne = MathLib.RotateLeft2(mint);
			faceRot = MathLib.RotateLeft2(faceId);
			// compareOne is [z, G, x, y]
			faceMask = MathLib.CmpGtSIMD(max_xy, compareOne);
			fltx4 max_xyz = MathLib.MaxSIMD(max_xy, compareOne);
			faceId = MathLib.MaskedAssign(faceMask, faceId_xy, faceRot);
			fltx4 lastInTmp = MathLib.SplatXSIMD(max_xyz);

			firstOut = MathLib.MinSIMD(firstOutTmp, MathLib.Four_Ones);
			lastIn = MathLib.MaxSIMD(lastInTmp, MathLib.Four_Zeros);
			separation = MathLib.CmpGtSIMD(lastIn, firstOut);
			Assert(MathLib.IsAllZeros(separation));
			if (MathLib.IsAllZeros(separation)) {
				uint faceIndex = MathLib.SubInt(faceId, 0);
				Assert(faceIndex < 6);
				float t1 = MathLib.SubFloat(ref lastIn, 0);

				// this condition is copied from the brush case to avoid hitting an assert and
				// overwriting a previous start solid with a new shorter fraction
				if (startOut && ray.IsRay && trace.FractionLeftSolid > t1)
					startOut = false;

				if (!startOut) {
					float t2 = MathLib.SubFloat(ref firstOut, 0);
					trace.StartSolid = true;
					trace.Contents = Contents.Solid;
					trace.Fraction = 0.0f;
					trace.StartPos = ray.Start + ray.StartOffset;
					trace.EndPos = trace.StartPos;
					if (t2 >= 1.0f)
						trace.AllSolid = true;
					else if (t2 > trace.FractionLeftSolid) {
						trace.FractionLeftSolid = t2;
						trace.StartPos += ray.Delta * trace.FractionLeftSolid;
					}
					return true;
				}
				else {
					if (t1 <= 1.0f) {
						trace.Fraction = t1;
						trace.Plane.Normal = vec3_origin;
						if (faceIndex >= 3) {
							faceIndex -= 3;
							trace.Plane.Dist = inBoxMaxs[(int)faceIndex];
							trace.Plane.Normal[(int)faceIndex] = 1.0f;
							trace.Plane.SignBits = 0;
						}
						else {
							trace.Plane.Dist = -inBoxMins[(int)faceIndex];
							trace.Plane.Normal[(int)faceIndex] = -1.0f;
							trace.Plane.SignBits = (byte)signbits[(int)faceIndex];
						}
						trace.Plane.Type = (PlaneType)faceIndex;
						trace.Contents = Contents.Solid;
						MathLib.VectorAdd(ray.Start, ray.StartOffset, out Vector3 startVec);

						if (trace.Fraction == 1)
							MathLib.VectorAdd(startVec, ray.Delta, out trace.EndPos);
						else
							MathLib.VectorMA(startVec, trace.Fraction, ray.Delta, out trace.EndPos);
						return true;
					}
				}
			}
		}
		return false;
	}

	internal static void ClipBoxToBrush(bool IS_POINT, TraceInfo traceInfo, in CollisionBrush brush) {
		if (brush.IsBox()) {
			ref CollisionBoxBrush box = ref traceInfo.BSPData!.MapBoxBrushes.AsSpan()[brush.GetBox()];
			IntersectRayWithBoxBrush(traceInfo, in brush, ref box);
			return;
		}
		if (0 == brush.NumSides)
			return;

		ref Trace trace = ref traceInfo.Trace;
		ref readonly Vector3 p1 = ref traceInfo.Start;
		ref readonly Vector3 p2 = ref traceInfo.End;
		Contents brushContents = brush.Contents;

		float enterfrac = NEVER_UPDATED;
		float leavefrac = 1f;

		bool getout = false;
		bool startout = false;
		ref CollisionBrushSide leadside = ref Unsafe.NullRef<CollisionBrushSide>();

		float dist;

		for (int sideIdx = brush.FirstBrushSide, sideLimit = sideIdx + brush.NumSides; sideIdx < sideLimit; sideIdx++) {
			ref CollisionBrushSide side = ref traceInfo.BSPData!.MapBrushSides.AsSpan()[sideIdx];
			ref CollisionPlane plane = ref side.Plane;
			ref Vector3 planeNormal = ref plane.Normal;

			if (!IS_POINT) {
				dist = MathLib.DotProductAbs(planeNormal, traceInfo.Extents);
				dist = plane.Dist + dist;
			}
			else {
				// special point case
				dist = plane.Dist;
				// don't trace rays against bevel planes 
				if (side.Bevel)
					continue;
			}

			float d1 = MathLib.DotProduct(p1, planeNormal) - dist;
			float d2 = MathLib.DotProduct(p2, planeNormal) - dist;

			// if completely in front of face, no intersection
			if (d1 > 0f) {
				startout = true;

				// d1 > 0.f && d2 > 0.f
				if (d2 > 0f)
					return;
			}
			else {
				// d1 <= 0.f && d2 <= 0.f
				if (d2 <= 0f)
					continue;

				// d2 > 0.f
				getout = true;
			}

			// crosses face
			if (d1 > d2) {  // enter

				// NOTE: This could be negative if d1 is less than the epsilon.
				// If the trace is short (d1-d2 is small) then it could produce a large
				// negative fraction.
				float f = (d1 - DIST_EPSILON);
				if (f < 0f)
					f = 0f;
				f = f / (d1 - d2);
				if (f > enterfrac) {
					enterfrac = f;
					leadside = ref side;
				}
			}
			else {  // leave
				float f = (d1 + DIST_EPSILON) / (d1 - d2);
				if (f < leavefrac)
					leavefrac = f;
			}
		}

		// when this happens, we entered the brush *after* leaving the previous brush.
		// Therefore, we're still outside!

		// NOTE: We only do this test against points because fractionleftsolid is
		// not possible to compute for brush sweeps without a *lot* more computation
		// So, client code will never get fractionleftsolid for box sweeps
		if (IS_POINT && startout) {
			// Add a little sludge.  The sludge should already be in the fractionleftsolid
			// (for all intents and purposes is a leavefrac value) and enterfrac values.  
			// Both of these values have +/- DIST_EPSILON values calculated in.  Thus, I 
			// think the test should be against "0.0."  If we experience new "left solid"
			// problems you may want to take a closer look here!
			//		if ((trace->fractionleftsolid - enterfrac) > -1e-6)
			if ((trace.FractionLeftSolid - enterfrac) > 0.0f)
				startout = false;
		}

		if (!startout) {    // original point was inside brush
			trace.StartSolid = true;
			// return starting contents
			trace.Contents = brushContents;

			if (!getout) {
				trace.AllSolid = true;
				trace.Fraction = 0.0f;
				trace.FractionLeftSolid = 1.0f;
			}
			else {
				// if leavefrac == 1, this means it's never been updated or we're in allsolid
				// the allsolid case was handled above
				if ((leavefrac != 1) && (leavefrac > trace.FractionLeftSolid)) {
					trace.FractionLeftSolid = leavefrac;

					// This could occur if a previous trace didn't start us in solid
					if (trace.Fraction <= leavefrac) {
						trace.Fraction = 1.0f;
						trace.Surface = CollisionBSPData.NullSurface;
					}
				}
			}
			return;
		}

		// We haven't hit anything at all until we've left...
		if (enterfrac < leavefrac) {
			if (enterfrac > NEVER_UPDATED && enterfrac < trace.Fraction) {
				// WE HIT SOMETHING!!!!!
				if (enterfrac < 0)
					enterfrac = 0;
				trace.Fraction = enterfrac;
				traceInfo.DispHit = 0;
				trace.Plane = leadside.Plane; // intentional copy
				trace.Surface = traceInfo.BSPData!.GetSurfaceAtIndex(leadside.SurfaceIndex); // intentional copy
				trace.Contents = brushContents;
			}
		}
	}
	internal static void TraceToLeaf(bool IS_POINT, TraceInfo traceInfo, int ndxLeaf, float startFrac, float endFrac) {
		ref CollisionLeaf leaf = ref traceInfo.BSPData!.MapLeafs.AsSpan()[ndxLeaf];

		// trace ray/box sweep against all brushes in this leaf
		int numleafbrushes = leaf.NumLeafBrushes;
		int lastleafbrush = leaf.FirstLeafBrush + numleafbrushes;
		ushort[] map_leafbrushes = traceInfo.BSPData!.MapLeafBrushes;
		CollisionBrush[] map_brushes = traceInfo.BSPData!.MapBrushes;
		Span<TraceCounter_t> counters = traceInfo.GetBrushCounters();
		TraceCounter_t count = traceInfo.GetCount();
		for (int ndxLeafBrush = leaf.FirstLeafBrush; ndxLeafBrush < lastleafbrush; ndxLeafBrush++) {
			// get the current brush
			int ndxBrush = map_leafbrushes[ndxLeafBrush];

			ref CollisionBrush brush = ref map_brushes[ndxBrush];

			// make sure we only check this brush once per trace/stab
			if (!traceInfo.Visit(ref brush, ndxBrush, count, counters))
				continue;

			Contents traceContents = traceInfo.Contents;
			Contents releventContents = (brush.Contents & traceContents);

			// only collide with objects you are interested in
			if (0 == releventContents)
				continue;

			// Many traces rely on CONTENTS_OPAQUE always being hit, even if it is nodraw.  AI blocklos brushes
			// need this, for instance.  CS and Terror visibility checks don't want this behavior, since
			// blocklight brushes also are CONTENTS_OPAQUE and SURF_NODRAW, and are actually in the playable
			// area in several maps.
			// NOTE: This is no longer true - no traces should rely on hitting CONTENTS_OPAQUE unless they
			// actually want to hit blocklight brushes.  No other brushes are marked with those bits
			// so it should be renamed CONTENTS_BLOCKLIGHT.  CONTENTS_BLOCKLOS has its own field now
			// so there is no reason to ignore nodraw opaques since you can merely remove CONTENTS_OPAQUE to
			// get that behavior
			if (releventContents == Contents.Opaque && (traceContents & Contents.IgnoreNoDrawOpaque) != 0) {
				// if the only reason we hit this brush is because it is opaque, make sure it isn't nodraw
				bool isNoDraw = false;

				if (brush.IsBox()) {
					ref CollisionBoxBrush box = ref traceInfo.BSPData!.MapBoxBrushes.AsSpan()[brush.GetBox()];
					for (int i = 0; i < 6 && !isNoDraw; i++) {
						ref readonly CollisionSurface surface = ref traceInfo.BSPData!.GetSurfaceAtIndex(box.SurfaceIndex[i]);
						if ((surface.Flags & (ushort)Surf.NoDraw) != 0) {
							isNoDraw = true;
							break;
						}
					}
				}
				else {
					int idx = brush.FirstBrushSide;
					for (int i = 0; i < brush.NumSides && !isNoDraw; i++, idx++) {
						ref CollisionBrushSide side = ref traceInfo.BSPData!.MapBrushSides.AsSpan()[idx];
						ref readonly CollisionSurface surface = ref traceInfo.BSPData!.GetSurfaceAtIndex(side.SurfaceIndex);
						if ((surface.Flags & (ushort)Surf.NoDraw) != 0) {
							isNoDraw = true;
							break;
						}
					}
				}

				if (isNoDraw) {
					continue;
				}
			}

			// trace against the brush and find impact point -- if any?
			// NOTE: pTraceInfo->m_trace.fraction == 0.0f only when trace starts inside of a brush!
			ClipBoxToBrush(IS_POINT, traceInfo, ref brush);
			if (0 == traceInfo.Trace.Fraction)
				return;
		}

		// TODO: this may be redundant
		if (traceInfo.Trace.StartSolid)
			return;

		// Collide (test) against displacement surfaces in this leaf.
		if (leaf.DispCount != 0) {
			// trace ray/swept box against all displacement surfaces in this leaf
			counters = traceInfo.GetDispCounters();
			count = traceInfo.GetCount();

			// utterly nonoptimal FPU pathway
			for (int i = 0; i < leaf.DispCount; i++) {
				int dispIndex = traceInfo.BSPData.MapDispList.AsSpan()[leaf.DispListStart + i];
				ref AlignedBBox dispBounds = ref g_DispBounds![dispIndex];

				// only collide with objects you are interested in
				if (0 == (dispBounds.GetContents() & traceInfo.Contents))
					continue;

				if (traceInfo.IsSwept) {
					// make sure we only check this brush once per trace/stab
					if (!traceInfo.Visit(dispBounds.GetCounter(), count, counters))
						continue;
				}

				if (IS_POINT && !CollisionUtils.IsBoxIntersectingRay(
									dispBounds.Mins, dispBounds.Maxs,
									traceInfo.Start, traceInfo.Delta, traceInfo.InvDelta, DISPCOLL_DIST_EPSILON))
					continue;


				if (!IS_POINT && !CollisionUtils.IsBoxIntersectingRay(
									dispBounds.Mins - traceInfo.Extents, dispBounds.Maxs + traceInfo.Extents,
									traceInfo.Start, traceInfo.Delta, traceInfo.InvDelta, DISPCOLL_DIST_EPSILON))
					continue;


				DispCollTree dispTree = g_DispCollTrees![dispIndex];
				TraceToDispTree(IS_POINT, traceInfo, dispTree, startFrac, endFrac);
				if (0 == traceInfo.Trace.Fraction)
					break;
			}

			PostTraceToDispTree(traceInfo);
		}
	}
	internal static void PostTraceToDispTree(TraceInfo traceInfo) {
		if (0 == traceInfo.DispHit)
			return;

		//
		// determine whether or not we are in solid
		//	
		if (MathLib.DotProduct(traceInfo.Trace.Plane.Normal, traceInfo.Delta) > 0.0f) {
			traceInfo.Trace.StartSolid = true;
			traceInfo.Trace.AllSolid = true;
		}
	}
	internal static void TraceToDispTree(bool IS_POINT, TraceInfo traceInfo, DispCollTree dispTree, float startFrac, float endFrac) {
		Ray ray = default;
		ray.Start = traceInfo.Start;
		ray.Delta = traceInfo.Delta;
		ray.IsSwept = true;

		ref Trace trace = ref traceInfo.Trace;

		// ray cast
		if (IS_POINT) {
			ray.Extents.Init();
			ray.IsRay = true;

			if (dispTree.AABBTree_Ray(ray, traceInfo.InvDelta, ref trace)) {
				traceInfo.DispHit = 1;
				trace.Contents = dispTree.GetContents();
				SetDispTraceSurfaceProps(ref trace, dispTree);
			}
		}
		// box sweep
		else {
			ray.Extents = traceInfo.Extents;
			ray.IsRay = false;
			if (dispTree.AABBTree_SweepAABB(ray, traceInfo.InvDelta, ref trace)) {
				traceInfo.DispHit = 1;
				trace.Contents = dispTree.GetContents();
				SetDispTraceSurfaceProps(ref trace, dispTree);
			}
		}
	}
	internal static void SetDispTraceSurfaceProps(ref Trace trace, DispCollTree disp) {
		trace.Surface.Name = "**displacement**";
		trace.Surface.Flags = 0;
		if (trace.IsDispSurfaceProp2())
			trace.Surface.SurfaceProps = (ushort)disp.GetSurfaceProps(1);
		else
			trace.Surface.SurfaceProps = (ushort)disp.GetSurfaceProps(0);
	}
	internal static void RecursiveHullCheck(TraceInfo traceInfo, int num, float p1f, float p2f) {
		ref readonly Vector3 p1 = ref traceInfo.Start;
		ref readonly Vector3 p2 = ref traceInfo.End;

		if (traceInfo.IsPoint)
			RecursiveHullCheckImpl(true, traceInfo, num, p1f, p2f, p1, p2);
		else
			RecursiveHullCheckImpl(false, traceInfo, num, p1f, p2f, p1, p2);
	}
	public static void TransformedBoxTrace(in Ray ray, int headnode, Mask brushmask, in Vector3 origin, in QAngle angles, ref Trace tr) {
		Matrix3x4 localToWorld = default;
		Ray ray_l = default;

		// subtract origin offset
		MathLib.VectorCopy(ray.StartOffset, out ray_l.StartOffset);
		MathLib.VectorCopy(ray.Extents, out ray_l.Extents);

		// Are we rotated?
		bool rotated = (angles[0] != 0 || angles[1] != 0 || angles[2] != 0);

		// rotate start and end into the models frame of reference
		if (rotated) {
			// NOTE: In this case, the bbox is rotated into the space of the BSP as well
			// to insure consistency at all orientations, we must rotate the origin of the ray
			// and reapply the offset to the center of the box.  That way all traces with the
			// same box centering will have the same transformation into local space
			Vector3 worldOrigin = ray.Start + ray.StartOffset;
			MathLib.AngleMatrix(angles, origin, out localToWorld);
			MathLib.VectorIRotate(ray.Delta, localToWorld, out ray_l.Delta);
			MathLib.VectorITransform(worldOrigin, localToWorld, out ray_l.Start);
			ray_l.Start -= ray.StartOffset;
		}
		else {
			MathLib.VectorSubtract(ray.Start, origin, out ray_l.Start);
			MathLib.VectorCopy(ray.Delta, out ray_l.Delta);
		}

		ray_l.IsRay = ray.IsRay;
		ray_l.IsSwept = ray.IsSwept;

		// sweep the box through the model, don't compute endpoints
		BoxTrace(ray_l, headnode, brushmask, false, ref tr);

		// If we hit, gotta fix up the normal...
		if ((tr.Fraction != 1) && rotated) {
			// transform the normal from the local space of this entity to world space
			Vector3 temp;
			MathLib.VectorCopy(tr.Plane.Normal, out temp);
			MathLib.VectorRotate(temp, localToWorld, out tr.Plane.Normal);
		}

		ComputeTraceEndpoints(ray, ref tr);
	}

	internal static void ComputeTraceEndpoints(in Ray ray, ref Trace tr) {
		// The ray start is the center of the extents; compute the actual start
		Vector3 start;
		MathLib.VectorAdd(ray.Start, ray.StartOffset, out start);

		if (tr.Fraction == 1)
			MathLib.VectorAdd(start, ray.Delta, out tr.EndPos);
		else
			MathLib.VectorMA(start, tr.Fraction, ray.Delta, out tr.EndPos);

		if (tr.FractionLeftSolid == 0)
			MathLib.VectorCopy(start, out tr.StartPos);
		else {
			if (tr.FractionLeftSolid == 1.0f) {
				tr.StartSolid = tr.AllSolid = true;
				tr.Fraction = 0.0f;
				MathLib.VectorCopy(start, out tr.EndPos);
			}

			MathLib.VectorMA(start, tr.FractionLeftSolid, ray.Delta, out tr.StartPos);
		}
	}
	internal static void BoxTrace(in Ray ray, int headnode, Mask brushmask, bool computeEndPt, ref Trace tr) {
		TraceInfo traceInfo = BeginTrace();

		// fill in a default trace
		ClearTrace(ref traceInfo.Trace);

		traceInfo.BSPData = GetCollisionBSPData();

		// check if the map is not loaded
		if (traceInfo.BSPData.NumNodes == 0) {
			tr = traceInfo.Trace;
			EndTrace(traceInfo);
			return;
		}

		traceInfo.DispHit = 0;
		traceInfo.DispStabDir.Init();
		traceInfo.Contents = (Contents)brushmask;
		MathLib.VectorCopy(ray.Start, out traceInfo.Start);
		MathLib.VectorAdd(ray.Start, ray.Delta, out traceInfo.End);
		MathLib.VectorMultiply(ray.Extents, -1.0f, out traceInfo.Mins);
		MathLib.VectorCopy(ray.Extents, out traceInfo.Maxs);
		MathLib.VectorCopy(ray.Extents, out traceInfo.Extents);
		traceInfo.Delta = ray.Delta;
		traceInfo.InvDelta = ray.InvDelta();
		traceInfo.IsPoint = ray.IsRay;
		traceInfo.IsSwept = ray.IsSwept;

		if (!ray.IsSwept) // check for position test special case
			UnsweptBoxTrace(traceInfo, ray, headnode, brushmask);
		else // general sweeping through world
			RecursiveHullCheck(traceInfo, headnode, 0, 1);

		// Compute the trace start + end points
		if (computeEndPt)
			ComputeTraceEndpoints(ray, ref traceInfo.Trace);

		// Copy off the results
		tr = traceInfo.Trace;
		EndTrace(traceInfo);
		Assert(!ray.IsRay || tr.AllSolid || (tr.Fraction >= tr.FractionLeftSolid));
	}

	private static void EndTrace(TraceInfo traceInfo) {
		PopTraceVisits(traceInfo);
		g_TraceInfoPool.Free(traceInfo);
	}

	static readonly ConVar map_noareas = new("map_noareas", "0", 0, "Disable area to area connection testing.");

	public static void SetAreaPortalState(int portalnum, int isOpen) {
		CollisionBSPData bspData = GetCollisionBSPData();

		if (portalnum > bspData.NumAreaPortals)
			Sys.Error("portalnum > numareaportals");

		bspData.PortalOpen[portalnum] = isOpen != 0;
		FloodAreaConnections(bspData);
	}

	public static void SetAreaPortalStates(ReadOnlySpan<int> portalnums, ReadOnlySpan<int> isOpen) {
		if (portalnums.Length == 0)
			return;

		CollisionBSPData bspData = GetCollisionBSPData();

		for (int i = 0; i < portalnums.Length; i++) {
			if (portalnums[i] > bspData.NumAreaPortals)
				Sys.Error("portalnum > numareaportals");

			bspData.PortalOpen[portalnums[i]] = isOpen[i] != 0;
		}

		FloodAreaConnections(bspData);
	}

	internal static int AreasConnected(int area1, int area2) {
		CollisionBSPData bspData = GetCollisionBSPData();

		if (map_noareas.GetInt() != 0)
			return 1;

		if (area1 >= bspData.NumAreas || area2 >= bspData.NumAreas)
			Sys.Error($"area(1=={area1}, 2=={area2}) >= numareas ({bspData.NumAreas}):  Check if engine->ResetPVS() was called from ClientSetupVisibility");

		return bspData.MapAreas[area1].FloodNum == bspData.MapAreas[area2].FloodNum ? 1 : 0;
	}

	public static int WriteAreaBits(Span<byte> buffer, int area) {
		CollisionBSPData bspData = GetCollisionBSPData();

		int bytes = (bspData.NumAreas + 7) >> 3;

		if (map_noareas.GetInt() != 0)
			buffer[..3].Fill(255);
		else {
			if (buffer.Length < 32)
				Sys.Error($"CM_WriteAreaBits with buffer {buffer.Length} size < 32\n");

			buffer[..32].Clear();

			int floodnum = bspData.MapAreas[area].FloodNum;
			for (int i = 0; i < bspData.NumAreas; i++) {
				if (bspData.MapAreas[i].FloodNum == floodnum || area == 0)
					buffer[i >> 3] |= (byte)(1 << (i & 7));
			}
		}

		return bytes;
	}

	public static bool GetAreaPortalPlane(in Vector3 viewOrigin, int portalKey, out VPlane plane) {
		plane = default;
		CollisionBSPData bspData = GetCollisionBSPData();

		int leaf = PointLeafnum(viewOrigin);
		if (leaf < 0 || leaf >= bspData.NumLeafs)
			return false;

		int areaIndex = bspData.MapLeafs.AsSpan()[leaf].Area;
		if (areaIndex < 0 || areaIndex >= bspData.NumAreas)
			return false;

		ref CollisionArea area = ref bspData.MapAreas[areaIndex];
		for (int i = 0; i < area.NumAreaPortals; i++) {
			ref BSPDAreaPortal portal = ref bspData.MapAreaPortals[area.FirstAreaPortal + i];

			if (portal.PortalKey == portalKey) {
				CollisionPlane mapPlane = bspData.MapPlanes[portal.PlaneNum];
				plane.Normal = mapPlane.Normal;
				plane.Dist = mapPlane.Dist;
				return true;
			}
		}

		return false;
	}

	public static bool HeadnodeVisible(int nodenum, ReadOnlySpan<byte> visbits) {
		CollisionBSPData bspData = GetCollisionBSPData();

		if (nodenum < 0) {
			int leafnum = -1 - nodenum;
			int cluster = bspData.MapLeafs.AsSpan()[leafnum].Cluster;
			if (cluster == -1)
				return false;
			return (visbits[cluster >> 3] & (1 << (cluster & 7))) != 0;
		}

		ref CollisionNode node = ref bspData.MapNodes.AsSpan()[bspData.MapRootNode + nodenum];
		if (HeadnodeVisible(node.Children[0], visbits))
			return true;
		return HeadnodeVisible(node.Children[1], visbits);
	}

	public static void SetupAreaFloodNums(Span<byte> areaFloodNums, out int numAreas) {
		CollisionBSPData bspData = GetCollisionBSPData();

		numAreas = bspData.NumAreas;
		if (bspData.NumAreas > BSPFileCommon.MAX_MAP_AREAS)
			Error("pBSPData->numareas > MAX_MAP_AREAS");

		for (int i = 0; i < bspData.NumAreas; i++) {
			Assert(bspData.MapAreas[i].FloodNum < BSPFileCommon.MAX_MAP_AREAS);
			areaFloodNums[i] = (byte)bspData.MapAreas[i].FloodNum;
		}
	}
}
