using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Source.Common;

public class DispVector<T> : List<T>;

public static class DispCollCommon
{
	public const int MAX_DISP_AABB_NODES = 341;
	public const int MAX_AABB_LIST = 344;
	public const int DISPCOLL_TREETRI_SIZE = BSPFileCommon.MAX_DISPTRIS;
	public const float DISPCOLL_DIST_EPSILON = 0.03125f;
	public const int DISPCOLL_ROOTNODE_INDEX = 0;
	public const int DISPCOLL_INVALID_TRI = -1;
	public const float DISPCOLL_INVALID_FRAC = -99999.9f;
	public const int DISPCOLL_NORMAL_UNDEF = 0xffff;
	public const float DISP_ALPHA_PROP_DELTA = 382.5f;
}

[InlineArray(MAX_DISP_AABB_NODES)] public struct InlineArrayMaxDispAABBNodes<T> { public T? first; }
[InlineArray(MAX_AABB_LIST)] public struct InlineArrayMaxAABBList<T> { public T? first; }
[InlineArray(BSPFileCommon.MAX_MAP_DISPINFO)] public struct InlineArrayMaxMapDispInfo<T> { public T? first; }

public class DispCollTri
{
	record struct index_t
	{
		private ushort _value;

		public ushort Vert {
			readonly get => (ushort)(_value & 0x1FF);
			set => _value = (ushort)((_value & ~0x1FF) | (value & 0x1FF));
		}

		public ushort Min {
			readonly get => (ushort)((_value >> 9) & 0x3);
			set => _value = (ushort)((_value & ~(0x3 << 9)) | ((value & 0x3) << 9));
		}

		public ushort Max {
			readonly get => (ushort)((_value >> 11) & 0x3);
			set => _value = (ushort)((_value & ~(0x3 << 11)) | ((value & 0x3) << 11));
		}
	}
	InlineArray3<index_t> TriData;
	private ushort _packeddata;
	public ushort SignBits {
		get => (ushort)(_packeddata & 0x7);
		set => _packeddata = (ushort)((_packeddata & ~0x7) | (value & 0x7));
	}

	public ushort PlaneType {
		get => (ushort)((_packeddata >> 3) & 0x7);
		set => _packeddata = (ushort)((_packeddata & ~(0x7 << 3)) | ((value & 0x7) << 3));
	}

	public ushort Flags {
		get => (ushort)((_packeddata >> 6) & 0x1F);
		set => _packeddata = (ushort)((_packeddata & ~(0x1F << 6)) | ((value & 0x1F) << 6));
	}

	public Vector3 Normal;
	public float Dist;

	// PLANE_ANYZ from the engine plane-type enum (>= 3 means non-axial).
	private const ushort PLANE_ANYZ = 5;

	public DispCollTri() {
		Init();
	}

	void Init() {
		Normal = default;
		Dist = 0.0f;
		TriData[0] = default;
		TriData[1] = default;
		TriData[2] = default;
	}

	public void CalcPlane(DispVector<Vector3> verts) {
		Vector3 edge0 = verts[GetVert(1)] - verts[GetVert(0)];
		Vector3 edge1 = verts[GetVert(2)] - verts[GetVert(0)];

		Normal = Vector3.Cross(edge1, edge0);
		MathLib.VectorNormalize(ref Normal);
		Dist = Vector3.Dot(Normal, verts[GetVert(0)]);

		// Calculate the signbits for the plane - fast test.
		ushort signBits = 0;
		ushort planeType = PLANE_ANYZ;
		for (int axis = 0; axis < 3; ++axis) {
			float c = Normal[axis];
			if (c < 0.0f)
				signBits |= (ushort)(1 << axis);
			if (c == 1.0f)
				planeType = (ushort)axis;
		}
		SignBits = signBits;
		PlaneType = planeType;
	}

	static void FindMin(float v1, float v2, float v3, out int iMin) {
		float min = v1; iMin = 0;
		if (v2 < min) { min = v2; iMin = 1; }
		if (v3 < min) { iMin = 2; }
	}
	static void FindMax(float v1, float v2, float v3, out int iMax) {
		float max = v1; iMax = 0;
		if (v2 > max) { max = v2; iMax = 1; }
		if (v3 > max) { iMax = 2; }
	}

	public void FindMinMax(DispVector<Vector3> verts) {
		Vector3 a = verts[GetVert(0)], b = verts[GetVert(1)], c = verts[GetVert(2)];

		FindMin(a.X, b.X, c.X, out int iMin); FindMax(a.X, b.X, c.X, out int iMax);
		SetMin(0, iMin); SetMax(0, iMax);

		FindMin(a.Y, b.Y, c.Y, out iMin); FindMax(a.Y, b.Y, c.Y, out iMax);
		SetMin(1, iMin); SetMax(1, iMax);

		FindMin(a.Z, b.Z, c.Z, out iMin); FindMax(a.Z, b.Z, c.Z, out iMax);
		SetMin(2, iMin); SetMax(2, iMax);
	}

	public void SetVert(int pos, int vert) {
		Assert((pos >= 0) && (pos < 3));
		Assert((vert >= 0) && (vert < (1 << 9)));
		TriData[pos].Vert = (ushort)vert;
	}

	public int GetVert(int pos) {
		Assert((pos >= 0) && (pos < 3));
		return TriData[pos].Vert;
	}

	public void SetMin(int axis, int min) {
		Assert((axis >= 0) && (axis < 3));
		Assert((min >= 0) && (min < 3));
		TriData[axis].Min = (ushort)min;
	}

	public int GetMin(int axis) {
		Assert((axis >= 0) && (axis < 3));
		return TriData[axis].Min;
	}

	public void SetMax(int axis, int max) {
		Assert((axis >= 0) && (axis < 3));
		Assert((max >= 0) && (max < 3));
		TriData[axis].Max = (ushort)max;
	}

	public int GetMax(int axis) {
		Assert((axis >= 0) && (axis < 3));
		return TriData[axis].Max;
	}
}

public class DispCollHelper
{
	public float StartFrac;
	public float EndFrac;
	public Vector3 ImpactNormal;
	public float ImpactDist;
}

public class DispCollTriCache
{
	public InlineArray3<ushort> CrossX;
	public InlineArray3<ushort> CrossY;
	public InlineArray3<ushort> CrossZ;
}

public class DispCollNode
{
	public FourVectors Mins;
	public FourVectors Maxs;
}

public class DispCollLeaf
{
	public InlineArray2<short> Tris;
}


public struct RayLeafList
{
	public FourVectors RayStart;
	public FourVectors RayExtents;
	public FourVectors InvDelta;
	public InlineArrayMaxAABBList<int> NodeList;
	public int MaxIndex;
}

public class DispCollTree
{
	public Vector3 Mins;
	public int Counter;
	public Vector3 Maxs;
	protected Contents Contents;
	protected int Power;
	protected int Flags;
	protected InlineArray4<Vector3> SurfPoints;
	protected Vector3 StabDir;
	protected InlineArray2<short> SurfaceProps;
	protected readonly DispVector<Vector3> Verts = [];
	protected readonly DispVector<DispCollTri> Tris = [];
	protected readonly DispVector<DispCollNode> Nodes = [];
	protected readonly DispVector<DispCollLeaf> Leaves = [];
	protected readonly DispVector<DispCollTriCache> TrisCache = [];
	protected readonly DispVector<Vector3> EdgePlanes = [];
	protected readonly DispCollHelper Helper = new();
	protected uint Size;


	static readonly Vector3 Vec3DispCollEpsilons = new(DISPCOLL_DIST_EPSILON, DISPCOLL_DIST_EPSILON, DISPCOLL_DIST_EPSILON);

	static readonly Vector3[,] ImpactNormalVecs = new Vector3[2, 3] {
		{ new(-1, 0, 0), new(0, -1, 0), new(0, 0, -1) },
		{ new(1, 0, 0), new(0, 1, 0), new(0, 0, 1) }
	};

	// Plane dedup for the swept-box edge cache (replaces the engine CUtlHash).
	private readonly Dictionary<Vector3, int> EdgePlaneHash = [];

	//=========================================================================
	// Node helpers
	[MethodImpl(MethodImplOptions.AggressiveInlining)] bool IsLeafNode(int iNode) => iNode >= Nodes.Count;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] static int Nodes_GetChild(int iNode, int direction) => (iNode << 2) + (direction + 1);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] static int Nodes_CalcCount(int power) => (1 << ((power + 1) << 1)) / 3;
	static int Nodes_GetIndexFromComponents(int x, int y) {
		int index = 0;
		int shift;
		for (shift = 0; x != 0; shift += 2, x >>= 1)
			index |= (x & 1) << shift;
		for (shift = 1; y != 0; shift += 2, y >>= 1)
			index |= (y & 1) << shift;
		return index;
	}

	//=========================================================================
	// Tree creation
	public bool Create(CoreDispInfo disp) => AABBTree_Create(disp);

	bool AABBTree_Create(CoreDispInfo disp) {
		Flags = disp.GetSurface().GetFlags();
		AABBTree_CopyDispData(disp);
		AABBTree_CreateLeafs();
		AABBTree_CalcBounds();
		return true;
	}

	void AABBTree_CopyDispData(CoreDispInfo disp) {
		Power = disp.GetPower();

		CoreDispSurface surf = disp.GetSurface();
		Contents = (Contents)surf.GetContents();
		surf.GetNormal(out StabDir);
		for (int iPoint = 0; iPoint < 4; iPoint++)
			surf.GetPoint(iPoint, out SurfPoints[iPoint]);

		// Allocate collision tree data.
		Verts.SetCount(GetSize());
		Tris.Clear(); Tris.SetSizeInitialized(GetTriSize());

		int numLeaves = (GetWidth() - 1) * (GetHeight() - 1);
		Leaves.Clear(); Leaves.SetSizeInitialized(numLeaves);
		int numNodes = Nodes_CalcCount(Power) - numLeaves;
		Nodes.Clear(); Nodes.SetSizeInitialized(numNodes);

		Size = (uint)(sizeof(float) * 3 * Verts.Count);

		// Copy vertex data.
		for (int iVert = 0; iVert < Verts.Count; iVert++) {
			disp.GetVert(iVert, out Vector3 v);
			Verts[iVert] = v;
		}

		// Copy and set up triangle data.
		for (int iTri = 0; iTri < Tris.Count; ++iTri) {
			disp.GetTriIndices(iTri, out ushort v0, out ushort v1, out ushort v2);
			DispCollTri tri = Tris[iTri];
			tri.SetVert(0, v0);
			tri.SetVert(1, v1);
			tri.SetVert(2, v2);

			ushort flags = disp.GetTriTagValue(iTri);

			// Calculate the surface props and set flags.
			float totalAlpha = 0.0f;
			for (int iVert = 0; iVert < 3; ++iVert)
				totalAlpha += disp.GetAlpha(tri.GetVert(iVert));

			if (totalAlpha > DISP_ALPHA_PROP_DELTA)
				flags |= (ushort)DispTriTags.FlagSurfProp2;
			else
				flags |= (ushort)DispTriTags.FlagSurfProp1;

			flags |= (ushort)DispTriTags.TagSurface;
			tri.Flags = flags;

			tri.CalcPlane(Verts);
			tri.FindMinMax(Verts);
		}
	}

	void AABBTree_CreateLeafs() {
		int nWidth = GetWidth() - 1;
		int nHeight = GetHeight() - 1;

		for (int iHgt = 0; iHgt < nHeight; ++iHgt) {
			for (int iWid = 0; iWid < nWidth; ++iWid) {
				int iLeaf = Nodes_GetIndexFromComponents(iWid, iHgt);
				int iIndex = iHgt * nWidth + iWid;
				int iTri = iIndex * 2;

				Leaves[iLeaf].Tris[0] = (short)iTri;
				Leaves[iLeaf].Tris[1] = (short)(iTri + 1);
			}
		}
	}

	static void AddPointToBounds(in Vector3 p, ref Vector3 mins, ref Vector3 maxs) {
		mins = Vector3.Min(mins, p);
		maxs = Vector3.Max(maxs, p);
	}

	void AABBTree_GenerateBoxes_r(int nodeIndex, out Vector3 mins, out Vector3 maxs) {
		mins = new Vector3(float.MaxValue);
		maxs = new Vector3(-float.MaxValue);

		if (nodeIndex >= Nodes.Count) {
			// leaf
			int iLeaf = nodeIndex - Nodes.Count;
			for (int iTri = 0; iTri < 2; ++iTri) {
				int triIndex = Leaves[iLeaf].Tris[iTri];
				DispCollTri tri = Tris[triIndex];
				AddPointToBounds(Verts[tri.GetVert(0)], ref mins, ref maxs);
				AddPointToBounds(Verts[tri.GetVert(1)], ref mins, ref maxs);
				AddPointToBounds(Verts[tri.GetVert(2)], ref mins, ref maxs);
			}
		}
		else {
			// node
			Span<Vector3> childMins = stackalloc Vector3[4];
			Span<Vector3> childMaxs = stackalloc Vector3[4];
			for (int i = 0; i < 4; i++) {
				int child = Nodes_GetChild(nodeIndex, i);
				AABBTree_GenerateBoxes_r(child, out childMins[i], out childMaxs[i]);
				AddPointToBounds(childMins[i], ref mins, ref maxs);
				AddPointToBounds(childMaxs[i], ref mins, ref maxs);
			}
			Nodes[nodeIndex].Mins.LoadAndSwizzle(childMins[0], childMins[1], childMins[2], childMins[3]);
			Nodes[nodeIndex].Maxs.LoadAndSwizzle(childMaxs[0], childMaxs[1], childMaxs[2], childMaxs[3]);
		}
	}

	void AABBTree_CalcBounds() {
		if ((Verts.Count == 0) || (Nodes.Count == 0))
			return;

		AABBTree_GenerateBoxes_r(0, out Mins, out Maxs);

		// Bloat a little.
		Mins -= Vector3.One;
		Maxs += Vector3.One;
	}

	//=========================================================================
	// AABB tree traversal
	private int BuildRayLeafList(int iNode, ref RayLeafList list) {
		list.NodeList[0] = iNode;
		int listIndex = 0;
		list.MaxIndex = 0;
		while (listIndex <= list.MaxIndex) {
			iNode = list.NodeList[listIndex];
			// the rest are all leaves
			if (IsLeafNode(iNode))
				return listIndex;
			listIndex++;
			DispCollNode node = Nodes[iNode];
			int mask = FourVectors.IntersectRayWithFourBoxes(list.RayStart, list.InvDelta, list.RayExtents, node.Mins, node.Maxs);
			if (mask != 0) {
				int child = Nodes_GetChild(iNode, 0);
				if ((mask & 1) != 0) { ++list.MaxIndex; list.NodeList[list.MaxIndex] = child; }
				if ((mask & 2) != 0) { ++list.MaxIndex; list.NodeList[list.MaxIndex] = child + 1; }
				if ((mask & 4) != 0) { ++list.MaxIndex; list.NodeList[list.MaxIndex] = child + 2; }
				if ((mask & 8) != 0) { ++list.MaxIndex; list.NodeList[list.MaxIndex] = child + 3; }
			}
		}
		return listIndex;
	}

	//=========================================================================
	// Ray cast
	public bool AABBTree_Ray(in Ray ray, in Vector3 invDelta, ref Trace trace, bool side = true) {
		if (CheckFlags(CoreDispInfo.SURF_NORAY_COLL))
			return false;

		if (((uint)Contents & (uint)Mask.Opaque) == 0)
			return false;

		DispCollTri? impactTri = null;
		AABBTree_TreeTrisRayTest(ray, invDelta, DISPCOLL_ROOTNODE_INDEX, ref trace, side, ref impactTri);

		if (impactTri != null) {
			trace.Plane.Normal = impactTri.Normal;
			trace.Plane.Dist = impactTri.Dist;
			trace.DispFlags = (DispSurfFlags)impactTri.Flags;
			return true;
		}

		return false;
	}

	void AABBTree_TreeTrisRayTest(in Ray ray, in Vector3 invDelta, int iNode, ref Trace trace, bool side, ref DispCollTri? impactTri) {
		RayLeafList list = new();
		list.InvDelta.DuplicateVector(invDelta);
		list.RayStart.DuplicateVector(ray.Start);
		list.RayExtents.DuplicateVector(ray.Extents + Vec3DispCollEpsilons);
		int listIndex = BuildRayLeafList(iNode, ref list);

		for (; listIndex <= list.MaxIndex; listIndex++) {
			int leafIndex = list.NodeList[listIndex] - Nodes.Count;
			DispCollTri tri0 = Tris[Leaves[leafIndex].Tris[0]];
			DispCollTri tri1 = Tris[Leaves[leafIndex].Tris[1]];

			float frac = CollisionUtils.IntersectRayWithTriangle(ray, Verts[tri0.GetVert(0)], Verts[tri0.GetVert(2)], Verts[tri0.GetVert(1)], side);
			if ((frac >= 0.0f) && (frac < trace.Fraction)) {
				trace.Fraction = frac;
				impactTri = tri0;
			}

			frac = CollisionUtils.IntersectRayWithTriangle(ray, Verts[tri1.GetVert(0)], Verts[tri1.GetVert(2)], Verts[tri1.GetVert(1)], side);
			if ((frac >= 0.0f) && (frac < trace.Fraction)) {
				trace.Fraction = frac;
				impactTri = tri1;
			}
		}
	}

	//=========================================================================
	// AABB intersection (solid test)
	public bool AABBTree_IntersectAABB(Vector3 absMins, Vector3 absMaxs) {
		if (CheckFlags(CoreDispInfo.SURF_NOHULL_COLL))
			return false;

		Vector3 center = 0.5f * (absMins + absMaxs);
		Vector3 extents = absMaxs - center;

		Span<int> nodeList = stackalloc int[MAX_AABB_LIST];
		nodeList[0] = 0;
		int listIndex = 0;
		int maxIndex = 0;

		FourVectors mins0 = default; mins0.DuplicateVector(absMins);
		FourVectors maxs0 = default; maxs0.DuplicateVector(absMaxs);

		CollisionPlane plane = default;
		while (listIndex <= maxIndex) {
			int iNode = nodeList[listIndex];
			listIndex++;
			if (IsLeafNode(iNode)) {
				for (--listIndex; listIndex <= maxIndex; listIndex++) {
					int leafIndex = nodeList[listIndex] - Nodes.Count;
					DispCollTri tri0 = Tris[Leaves[leafIndex].Tris[0]];
					DispCollTri tri1 = Tris[Leaves[leafIndex].Tris[1]];

					plane.Normal = tri0.Normal;
					plane.Dist = tri0.Dist;
					plane.SignBits = (byte)tri0.SignBits;
					plane.Type = (PlaneType)tri0.PlaneType;
					if (CollisionUtils.IsBoxIntersectingTriangle(center, extents, Verts[tri0.GetVert(0)], Verts[tri0.GetVert(2)], Verts[tri0.GetVert(1)], plane, 0.0f))
						return true;

					plane.Normal = tri1.Normal;
					plane.Dist = tri1.Dist;
					plane.SignBits = (byte)tri1.SignBits;
					plane.Type = (PlaneType)tri1.PlaneType;
					if (CollisionUtils.IsBoxIntersectingTriangle(center, extents, Verts[tri1.GetVert(0)], Verts[tri1.GetVert(2)], Verts[tri1.GetVert(1)], plane, 0.0f))
						return true;
				}
				break;
			}
			else {
				DispCollNode node = Nodes[iNode];
				int mask = FourVectors.IntersectFourBoxPairs(mins0, maxs0, node.Mins, node.Maxs);
				if (mask != 0) {
					int child = Nodes_GetChild(iNode, 0);
					if ((mask & 1) != 0) { ++maxIndex; nodeList[maxIndex] = child; }
					if ((mask & 2) != 0) { ++maxIndex; nodeList[maxIndex] = child + 1; }
					if ((mask & 4) != 0) { ++maxIndex; nodeList[maxIndex] = child + 2; }
					if ((mask & 8) != 0) { ++maxIndex; nodeList[maxIndex] = child + 3; }
				}
			}
		}

		return false;
	}

	//=========================================================================
	// Swept-box cast
	public bool AABBTree_SweepAABB(in Ray ray, in Vector3 invDelta, ref Trace trace) {
		if (CheckFlags(CoreDispInfo.SURF_NOHULL_COLL))
			return false;

		Vector3 rayDir = ray.Delta;
		MathLib.VectorNormalize(ref rayDir);

		float frac = trace.Fraction;

		RayLeafList list = new();
		list.InvDelta.DuplicateVector(invDelta);
		list.RayStart.DuplicateVector(ray.Start);
		list.RayExtents.DuplicateVector(ray.Extents + Vec3DispCollEpsilons);
		int listIndex = BuildRayLeafList(0, ref list);

		if (listIndex <= list.MaxIndex) {
			LockCache();
			for (; listIndex <= list.MaxIndex; listIndex++) {
				int leafIndex = list.NodeList[listIndex] - Nodes.Count;
				int iTri0 = Leaves[leafIndex].Tris[0];
				int iTri1 = Leaves[leafIndex].Tris[1];

				SweepAABBTriIntersect(ray, rayDir, iTri0, Tris[iTri0], ref trace);
				SweepAABBTriIntersect(ray, rayDir, iTri1, Tris[iTri1], ref trace);
			}
			UnlockCache();
		}

		return trace.Fraction < frac;
	}

	static void CalcClosestExtents(in Vector3 planeNormal, in Vector3 boxExtents, out Vector3 boxPoint) {
		boxPoint = default;
		boxPoint.X = planeNormal.X < 0.0f ? boxExtents.X : -boxExtents.X;
		boxPoint.Y = planeNormal.Y < 0.0f ? boxExtents.Y : -boxExtents.Y;
		boxPoint.Z = planeNormal.Z < 0.0f ? boxExtents.Z : -boxExtents.Z;
	}

	bool ResolveRayPlaneIntersect(float flStart, float flEnd, in Vector3 vecNormal, float flDist, DispCollHelper helper) {
		if ((flStart > 0.0f) && (flEnd > 0.0f))
			return false;
		if ((flStart < 0.0f) && (flEnd < 0.0f))
			return true;

		float flDenom = flStart - flEnd;
		bool bDenomIsZero = flDenom == 0.0f;
		if ((flStart >= 0.0f) && (flEnd <= 0.0f)) {
			float t = !bDenomIsZero ? (flStart - DISPCOLL_DIST_EPSILON) / flDenom : 0.0f;
			if (t > helper.StartFrac) {
				helper.StartFrac = t;
				helper.ImpactNormal = vecNormal;
				helper.ImpactDist = flDist;
			}
		}
		else {
			float t = !bDenomIsZero ? (flStart + DISPCOLL_DIST_EPSILON) / flDenom : 0.0f;
			if (t < helper.EndFrac)
				helper.EndFrac = t;
		}

		return true;
	}

	bool FacePlane(in Ray ray, in Vector3 rayDir, DispCollTri tri, DispCollHelper helper) {
		CalcClosestExtents(tri.Normal, ray.Extents, out Vector3 vecExtent);

		float flExpandDist = tri.Dist - Vector3.Dot(tri.Normal, vecExtent);
		float flStart = Vector3.Dot(tri.Normal, ray.Start) - flExpandDist;
		float flEnd = Vector3.Dot(tri.Normal, ray.Start + ray.Delta) - flExpandDist;

		return ResolveRayPlaneIntersect(flStart, flEnd, tri.Normal, tri.Dist, helper);
	}

	bool AxisPlanesXYZ(in Ray ray, DispCollTri tri, DispCollHelper helper) {
		for (int axis = 2; axis >= 0; --axis) {
			float rayStart = ray.Start[axis];
			float rayExtent = ray.Extents[axis];
			float rayDelta = ray.Delta[axis];

			// Min
			float flDist = Verts[tri.GetVert(tri.GetMin(axis))][axis];
			float flExpDist = flDist - rayExtent;
			float flStart = flExpDist - rayStart;
			float flEnd = flStart - rayDelta;
			if (!ResolveRayPlaneIntersect(flStart, flEnd, ImpactNormalVecs[0, axis], flDist, helper))
				return false;

			// Max
			flDist = Verts[tri.GetVert(tri.GetMax(axis))][axis];
			flExpDist = flDist + rayExtent;
			flStart = rayStart - flExpDist;
			flEnd = flStart + rayDelta;
			if (!ResolveRayPlaneIntersect(flStart, flEnd, ImpactNormalVecs[1, axis], flDist, helper))
				return false;
		}

		return true;
	}

	bool EdgeCrossAxis(int axis, in Ray ray, ushort iPlane, DispCollHelper helper) {
		if (iPlane == DISPCOLL_NORMAL_UNDEF)
			return true;

		// Get the edge plane.
		Vector3 vecNormal;
		if ((iPlane & 0x8000) != 0) {
			vecNormal = EdgePlanes[iPlane & 0x7fff];
			vecNormal = -vecNormal;
		}
		else {
			vecNormal = EdgePlanes[iPlane];
		}

		int o1 = (axis + 1) % 3;
		int o2 = (axis + 2) % 3;

		// Get the plane distance and "fix" the normal.
		float flDist = vecNormal[axis];
		MathLib.SubFloat(ref vecNormal, axis) = 0.0f;

		Vector3 vecExtent = default;
		MathLib.SubFloat(ref vecExtent, o1) = vecNormal[o1] < 0.0f ? ray.Extents[o1] : -ray.Extents[o1];
		MathLib.SubFloat(ref vecExtent, o2) = vecNormal[o2] < 0.0f ? ray.Extents[o2] : -ray.Extents[o2];

		Vector3 vecEnd = default;
		MathLib.SubFloat(ref vecEnd, o1) = ray.Start[o1] + ray.Delta[o1];
		MathLib.SubFloat(ref vecEnd, o2) = ray.Start[o2] + ray.Delta[o2];

		float flExpandDist = flDist - ((vecNormal[o1] * vecExtent[o1]) + (vecNormal[o2] * vecExtent[o2]));
		float flStart = (vecNormal[o1] * ray.Start[o1]) + (vecNormal[o2] * ray.Start[o2]) - flExpandDist;
		float flEnd = (vecNormal[o1] * vecEnd[o1]) + (vecNormal[o2] * vecEnd[o2]) - flExpandDist;

		return ResolveRayPlaneIntersect(flStart, flEnd, vecNormal, flDist, helper);
	}

	void SweepAABBTriIntersect(in Ray ray, in Vector3 rayDir, int iTri, DispCollTri tri, ref Trace trace) {
		DispCollHelper helper = new() {
			EndFrac = 1.0f,
			StartFrac = DISPCOLL_INVALID_FRAC
		};

		// Make sure objects are traveling toward one another.
		float flDistAlongNormal = Vector3.Dot(tri.Normal, ray.Delta);
		if (flDistAlongNormal > DISPCOLL_DIST_EPSILON)
			return;

		// Axis planes.
		if (!AxisPlanesXYZ(ray, tri, helper))
			return;

		DispCollTriCache cache = TrisCache[iTri];

		if (!EdgeCrossAxis(0, ray, cache.CrossX[0], helper)) return;
		if (!EdgeCrossAxis(0, ray, cache.CrossX[1], helper)) return;
		if (!EdgeCrossAxis(0, ray, cache.CrossX[2], helper)) return;

		if (!EdgeCrossAxis(1, ray, cache.CrossY[0], helper)) return;
		if (!EdgeCrossAxis(1, ray, cache.CrossY[1], helper)) return;
		if (!EdgeCrossAxis(1, ray, cache.CrossY[2], helper)) return;

		if (!EdgeCrossAxis(2, ray, cache.CrossZ[0], helper)) return;
		if (!EdgeCrossAxis(2, ray, cache.CrossZ[1], helper)) return;
		if (!EdgeCrossAxis(2, ray, cache.CrossZ[2], helper)) return;

		// Triangle face plane.
		if (!FacePlane(ray, rayDir, tri, helper))
			return;

		if ((helper.StartFrac < helper.EndFrac) || (MathF.Abs(helper.StartFrac - helper.EndFrac) < 0.001f)) {
			if ((helper.StartFrac != DISPCOLL_INVALID_FRAC) && (helper.StartFrac < trace.Fraction)) {
				if (helper.StartFrac < 0.0f)
					helper.StartFrac = 0.0f;

				trace.Fraction = helper.StartFrac;
				trace.Plane.Normal = helper.ImpactNormal;
				trace.Plane.Dist = helper.ImpactDist;
				trace.DispFlags = (DispSurfFlags)tri.Flags;
			}
		}
	}

	//=========================================================================
	// Edge-plane cache (built lazily for the swept-box test)
	void LockCache() {
		if (!IsCached())
			Cache();
	}
	static void UnlockCache() { }

	void Cache() {
		if (TrisCache.Count == GetTriSize())
			return;

		int nTriCount = GetTriSize();
		TrisCache.Clear(); TrisCache.SetSizeInitialized(nTriCount);
		EdgePlanes.Clear();
		EdgePlaneHash.Clear();

		for (int iTri = 0; iTri < nTriCount; ++iTri)
			Cache_Create(Tris[iTri], iTri);

		EdgePlaneHash.Clear();
	}

	int AddPlane(in Vector3 vecNormal) {
		if (EdgePlaneHash.TryGetValue(vecNormal, out int index))
			return index;
		if (EdgePlaneHash.TryGetValue(-vecNormal, out int negIndex))
			return negIndex | 0x8000;

		index = EdgePlanes.Count;
		EdgePlanes.Add(vecNormal);
		EdgePlaneHash[vecNormal] = index;
		return index;
	}

	void Cache_Create(DispCollTri tri, int iTri) {
		Vector3 v0 = Verts[tri.GetVert(0)];
		Vector3 v1 = Verts[tri.GetVert(1)];
		Vector3 v2 = Verts[tri.GetVert(2)];

		DispCollTriCache cache = TrisCache[iTri];

		// Edge 1
		Vector3 edge = v1 - v0;
		cache.CrossX[0] = Cache_EdgeCrossAxisX(edge, v0, v2);
		cache.CrossY[0] = Cache_EdgeCrossAxisY(edge, v0, v2);
		cache.CrossZ[0] = Cache_EdgeCrossAxisZ(edge, v0, v2);

		// Edge 2
		edge = v2 - v1;
		cache.CrossX[1] = Cache_EdgeCrossAxisX(edge, v1, v0);
		cache.CrossY[1] = Cache_EdgeCrossAxisY(edge, v1, v0);
		cache.CrossZ[1] = Cache_EdgeCrossAxisZ(edge, v1, v0);

		// Edge 3
		edge = v0 - v2;
		cache.CrossX[2] = Cache_EdgeCrossAxisX(edge, v2, v1);
		cache.CrossY[2] = Cache_EdgeCrossAxisY(edge, v2, v1);
		cache.CrossZ[2] = Cache_EdgeCrossAxisZ(edge, v2, v1);
	}

	ushort Cache_EdgeCrossAxisX(in Vector3 vecEdge, in Vector3 vecOnEdge, in Vector3 vecOffEdge) {
		// edge x axisX = ( 0.0, edgeZ, -edgeY )
		Vector3 vecNormal = new(0.0f, vecEdge.Z, -vecEdge.Y);
		MathLib.VectorNormalize(ref vecNormal);

		if ((vecNormal.Y == 0.0f) || (vecNormal.Z == 0.0f))
			return DISPCOLL_NORMAL_UNDEF;

		float flDist = (vecNormal.Y * vecOnEdge.Y) + (vecNormal.Z * vecOnEdge.Z);
		float flOffDist = (vecNormal.Y * vecOffEdge.Y) + (vecNormal.Z * vecOffEdge.Z);
		if (!(MathF.Abs(flOffDist - flDist) < DISPCOLL_DIST_EPSILON) && (flOffDist > flDist)) {
			vecNormal.X = -flDist; vecNormal.Y = -vecNormal.Y; vecNormal.Z = -vecNormal.Z;
		}
		else {
			vecNormal.X = flDist;
		}

		return (ushort)AddPlane(vecNormal);
	}

	ushort Cache_EdgeCrossAxisY(in Vector3 vecEdge, in Vector3 vecOnEdge, in Vector3 vecOffEdge) {
		// edge x axisY = ( -edgeZ, 0.0, edgeX )
		Vector3 vecNormal = new(-vecEdge.Z, 0.0f, vecEdge.X);
		MathLib.VectorNormalize(ref vecNormal);

		if ((vecNormal.X == 0.0f) || (vecNormal.Z == 0.0f))
			return DISPCOLL_NORMAL_UNDEF;

		float flDist = (vecNormal.X * vecOnEdge.X) + (vecNormal.Z * vecOnEdge.Z);
		float flOffDist = (vecNormal.X * vecOffEdge.X) + (vecNormal.Z * vecOffEdge.Z);
		if (!(MathF.Abs(flOffDist - flDist) < DISPCOLL_DIST_EPSILON) && (flOffDist > flDist)) {
			vecNormal.X = -vecNormal.X; vecNormal.Y = -flDist; vecNormal.Z = -vecNormal.Z;
		}
		else {
			vecNormal.Y = flDist;
		}

		return (ushort)AddPlane(vecNormal);
	}

	ushort Cache_EdgeCrossAxisZ(in Vector3 vecEdge, in Vector3 vecOnEdge, in Vector3 vecOffEdge) {
		// edge x axisZ = ( edgeY, -edgeX, 0.0 )
		Vector3 vecNormal = new(vecEdge.Y, -vecEdge.X, 0.0f);
		MathLib.VectorNormalize(ref vecNormal);

		if ((vecNormal.X == 0.0f) || (vecNormal.Y == 0.0f))
			return DISPCOLL_NORMAL_UNDEF;

		float flDist = (vecNormal.X * vecOnEdge.X) + (vecNormal.Y * vecOnEdge.Y);
		float flOffDist = (vecNormal.X * vecOffEdge.X) + (vecNormal.Y * vecOffEdge.Y);
		if (!(MathF.Abs(flOffDist - flDist) < DISPCOLL_DIST_EPSILON) && (flOffDist > flDist)) {
			vecNormal.X = -vecNormal.X; vecNormal.Y = -vecNormal.Y; vecNormal.Z = -flDist;
		}
		else {
			vecNormal.Z = flDist;
		}

		return (ushort)AddPlane(vecNormal);
	}

	public void GetVirtualMeshList(ref VirtualMeshList list) {
		int i;
		int triangleCount = GetTriSize();
		list.IndexCount = triangleCount * 3;
		list.TriangleCount = triangleCount;
		list.VertexCount = Verts.Count;
		list.Verts = Verts;
		list.SurfacePropsIndex = GetSurfaceProps(0);
		int index = 0;
		for (i = 0; i < triangleCount; i++) {
			list.Indices[index + 0] = (ushort)Tris[i].GetVert(0);
			list.Indices[index + 1] = (ushort)Tris[i].GetVert(1);
			list.Indices[index + 2] = (ushort)Tris[i].GetVert(2);
			index += 3;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetPower(int power) => Power = power;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int GetPower() => Power;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int GetFlags() => Flags;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetFlags(int flags) => Flags = flags;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool CheckFlags(int flags) => (flags & GetFlags()) != 0;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int GetWidth() => ((1 << Power) + 1);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int GetHeight() => ((1 << Power) + 1);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int GetSize() => ((1 << Power) + 1) * ((1 << Power) + 1);
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public int GetTriSize() => ((1 << Power) * (1 << Power) * 2);

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void GetStabDirection(out Vector3 dir) => dir = StabDir;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void GetBounds(out Vector3 boxMin, out Vector3 boxMax) { boxMin = Mins; boxMax = Maxs; }
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public Contents GetContents() => Contents;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public void SetSurfaceProps(int prop, short surfProp) => SurfaceProps[prop] = surfProp;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public short GetSurfaceProps(int prop) => SurfaceProps[prop];

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public uint GetMemorySize() => Size;

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public bool IsCached() => TrisCache.Count == Tris.Count;

	public bool PointInBounds(in Vector3 start, in Vector3 mins, in Vector3 maxs, bool isPoint) {
		if (isPoint)
			return CollisionUtils.IsPointInBox(start, mins, maxs);

		MathLib.VectorSubtract(maxs, mins, out Vector3 extents);
		extents *= 0.5f;

		return CollisionUtils.IsPointInBox(start, mins - extents, maxs + extents);
	}
}
