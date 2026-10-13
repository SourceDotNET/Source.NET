using Box3D;

using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;


namespace Source.Physics;

internal sealed unsafe class BoxPhysConvex : PhysConvex
{
	public HullData Hull;
	public HullData SimHull;
	public uint GameData;

	public Vector3[]? QueryVerts;
	public byte[]? QueryMaterials;

	public HullData GetSimHull() {
		if (!SimHull.IsNull)
			return SimHull;
		if (Hull.IsNull)
			return default;

		int vertexCount = Hull.VertexCount;
		if (Hull.FaceCount < 4 || vertexCount < 4)
			return Hull;

		ReadOnlySpan<Vector3> points = Hull.Points;
		ReadOnlySpan<HullVertex> vertices = Hull.Vertices;
		ReadOnlySpan<HullHalfEdge> edges = Hull.Edges;
		ReadOnlySpan<Box3D.Plane> planes = Hull.Planes;

		float inflate = SourceToBox.Distance(0.25f);

		Vector3[] verts = new Vector3[vertexCount];
		Span<int> faces = stackalloc int[32];
		for (int v = 0; v < vertexCount; v++) {
			Vector3 p = BoxToSource.Unitless(points[v]);

			int faceCount = 0;
			int start = vertices[v].edge;
			int e = start;
			do {
				faces[faceCount++] = edges[e].face;
				e = edges[edges[e].twin].next;
			} while (e != start && faceCount < 32);

			Vector3 n0 = BoxToSource.Unitless(planes[faces[0]].normal);
			int i1 = -1, i2 = -1;
			float bestCross = 1e-3f;
			for (int i = 1; i < faceCount; i++) {
				float c = Vector3.Cross(n0, BoxToSource.Unitless(planes[faces[i]].normal)).Length();
				if (c > bestCross) {
					bestCross = c;
					i1 = i;
				}
			}
			float bestDet = 1e-4f;
			if (i1 >= 0) {
				Vector3 c01 = Vector3.Cross(n0, BoxToSource.Unitless(planes[faces[i1]].normal));
				for (int i = 1; i < faceCount; i++) {
					if (i == i1)
						continue;
					float d = MathF.Abs(Vector3.Dot(c01, BoxToSource.Unitless(planes[faces[i]].normal)));
					if (d > bestDet) {
						bestDet = d;
						i2 = i;
					}
				}
			}

			Vector3 x = default;
			if (i2 >= 0) {
				Vector3 n1 = BoxToSource.Unitless(planes[faces[i1]].normal), n2 = BoxToSource.Unitless(planes[faces[i2]].normal);
				float det = Vector3.Dot(n0, Vector3.Cross(n1, n2));
				float d0 = planes[faces[0]].offset + inflate;
				float d1 = planes[faces[i1]].offset + inflate;
				float d2 = planes[faces[i2]].offset + inflate;
				x = (d0 * Vector3.Cross(n1, n2) + d1 * Vector3.Cross(n2, n0) + d2 * Vector3.Cross(n0, n1)) * (1.0f / det);
			}
			if (i2 < 0 || (x - p).Length() > 20.0f * inflate) {
				Vector3 sum = n0;
				for (int i = 1; i < faceCount; i++)
					sum += BoxToSource.Unitless(planes[faces[i]].normal);
				float len = sum.Length();
				x = p + inflate * (len > 1e-6f ? sum * (1.0f / len) : n0);
			}
			verts[v] = SourceToBox.Unitless(x);
		}

		SimHull = BoxHullCooking.CreateHullSafe(verts, BoxHullCooking.MaxHullVertices);
		return !SimHull.IsNull ? SimHull : Hull;
	}

	public void Free() {
		if (!Hull.IsNull)
			Hull.Destroy();
		if (!SimHull.IsNull)
			SimHull.Destroy();
		Hull = default;
		SimHull = default;
	}
}

internal sealed unsafe class BoxPhysCollide : PhysCollide
{
	public readonly List<BoxPhysConvex> Convexes = [];
	public MeshData Mesh;

	public Vector3 MassCenter;
	public Vector3 UnitInertia;
	public Vector3 OrthographicAreas = new(1.0f, 1.0f, 1.0f);
}

internal sealed class BoxPhysPolysoup : PhysPolysoup
{
	public readonly List<Vector3> Vertices = [];
	public readonly List<byte> MaterialIndices = [];
}

internal static unsafe class BoxHullCooking
{
	public const int MaxHullVertices = 44;
	public const int MaxCloudPoints = 40;

	static bool CloudIsCookable(Vector3* points, int count) {
		if (count < 4)
			return false;

		Vector3 min = BoxToSource.Unitless(points[0]), max = min, sum = default;
		for (int i = 0; i < count; i++) {
			Vector3 p = BoxToSource.Unitless(points[i]);
			if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
				return false;
			min = Vector3.Min(min, p);
			max = Vector3.Max(max, p);
			sum += p;
		}
		Vector3 extent = max - min;
		float maxExtent = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
		if (maxExtent <= 0.0f)
			return false;

		Vector3 mean = sum * (1.0f / count);
		float xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
		for (int i = 0; i < count; i++) {
			Vector3 d = BoxToSource.Unitless(points[i]) - mean;
			xx += d.X * d.X;
			yy += d.Y * d.Y;
			zz += d.Z * d.Z;
			xy += d.X * d.Y;
			xz += d.X * d.Z;
			yz += d.Y * d.Z;
		}
		float inv = 1.0f / count;
		xx *= inv; yy *= inv; zz *= inv; xy *= inv; xz *= inv; yz *= inv;

		float minEig;
		float p1 = xy * xy + xz * xz + yz * yz;
		if (p1 == 0.0f)
			minEig = MathF.Min(xx, MathF.Min(yy, zz));
		else {
			float q = (xx + yy + zz) / 3.0f;
			float p2 = (xx - q) * (xx - q) + (yy - q) * (yy - q) + (zz - q) * (zz - q) + 2.0f * p1;
			float p = MathF.Sqrt(p2 / 6.0f);
			float invp = 1.0f / p;
			float b00 = (xx - q) * invp, b11 = (yy - q) * invp, b22 = (zz - q) * invp;
			float b01 = xy * invp, b02 = xz * invp, b12 = yz * invp;
			float r = 0.5f * (b00 * (b11 * b22 - b12 * b12) - b01 * (b01 * b22 - b12 * b02) + b02 * (b01 * b12 - b11 * b02));
			r = Math.Clamp(r, -1.0f, 1.0f);
			minEig = q + 2.0f * p * MathF.Cos(MathF.Acos(r) / 3.0f + 2.0f * MathF.PI / 3.0f);
		}

		return MathF.Sqrt(MathF.Max(minEig, 0.0f)) >= 1e-4f * maxExtent;
	}

	static int DecimateCloud(Vector3* input, int count, Vector3* output) {
		int first = 0;
		for (int i = 1; i < count; i++)
			if (input[i].X < input[first].X)
				first = i;
		output[0] = input[first];

		float[] dist = new float[count];
		for (int i = 0; i < count; i++)
			dist[i] = Vector3.DistanceSquared(BoxToSource.Unitless(input[i]), BoxToSource.Unitless(output[0]));

		int outCount = 1;
		while (outCount < MaxCloudPoints) {
			int best = 0;
			for (int i = 1; i < count; i++)
				if (dist[i] > dist[best])
					best = i;
			if (dist[best] <= 0.0f)
				break;
			output[outCount++] = input[best];
			for (int i = 0; i < count; i++) {
				float d = Vector3.DistanceSquared(BoxToSource.Unitless(input[i]), BoxToSource.Unitless(input[best]));
				if (d < dist[i])
					dist[i] = d;
			}
		}
		return outCount;
	}

	public static HullData CreateHullSafe(Vector3* points, int count, int maxVerts) {
		if (!CloudIsCookable(points, count))
			return default;

		if (count <= MaxCloudPoints)
			return HullData.Create(new ReadOnlySpan<Vector3>(points, count), maxVerts);

		Vector3* decimated = stackalloc Vector3[MaxCloudPoints];
		int decimatedCount = DecimateCloud(points, count, decimated);
		return HullData.Create(new ReadOnlySpan<Vector3>(decimated, decimatedCount), maxVerts);
	}

	public static HullData CreateHullSafe(ReadOnlySpan<Vector3> points, int maxVerts) {
		fixed (Vector3* p = points)
			return CreateHullSafe(p, points.Length, maxVerts);
	}
}

internal static unsafe class IVPCompat
{
	[StructLayout(LayoutKind.Sequential)]
	public struct CollideHeader
	{
		public int VPhysicsID;
		public short Version;
		public short ModelType;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct CompactSurfaceHeader
	{
		public int SurfaceSize;
		public Vector3 DragAxisAreas;
		public int AxisMapSize;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct CompactSurface
	{
		public Vector3 MassCenter;
		public Vector3 RotationInertia;
		public float UpperLimitRadius;
		public uint BitfieldDeviationByteSize;
		public int OffsetLedgetreeRoot;
		public int Dummy0;
		public int Dummy1;
		public int Dummy2;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct CompactLedge
	{
		public int CPointOffset;
		public int ClientData;
		public uint Flags;
		public short NTriangles;
		public short ForFutureUse;
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct CompactEdge
	{
		public uint Data;
		public readonly int StartPointIndex => (int)(Data & 0xFFFF);
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct CompactTriangle
	{
		public uint Data;
		public CompactEdge Edge0;
		public CompactEdge Edge1;
		public CompactEdge Edge2;

		public readonly uint MaterialIndex => (Data >> 24) & 0x7F;
		public readonly CompactEdge GetEdge(int i) => i switch { 0 => Edge0, 1 => Edge1, _ => Edge2 };
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct CompactLedgeNode
	{
		public int OffsetRightNode;
		public int OffsetCompactLedge;
		public Vector3 Center;
		public float Radius;
		public byte BoxSize0, BoxSize1, BoxSize2;
		public byte Free0;
	}

	public const int IVP_COMPACT_SURFACE_SUPER_LEGACY = 0;
	public static readonly int IVP_COMPACT_SURFACE_ID = MAKEID('I', 'V', 'P', 'S');
	public static readonly int IVP_COMPACT_SURFACE_ID_SWAPPED = MAKEID('S', 'P', 'V', 'I');
	public static readonly int IVP_COMPACT_MOPP_ID = MAKEID('M', 'O', 'P', 'P');
	public static readonly int VPHYSICS_COLLISION_ID = MAKEID('V', 'P', 'H', 'Y');
	public const short VPHYSICS_COLLISION_VERSION = 0x0100;

	public const int COLLIDE_POLY = 0;
	public const int COLLIDE_MOPP = 1;
	public const int COLLIDE_BALL = 2;
	public const int COLLIDE_VIRTUAL = 3;

	const int IVPAlignedVectorSize = 16;

	static BoxPhysConvex? LedgeToConvex(CompactLedge* ledge) {
		if (ledge->NTriangles == 0)
			return null;

		byte* vertices = (byte*)ledge + ledge->CPointOffset;
		CompactTriangle* triangles = (CompactTriangle*)(ledge + 1);
		int vertCount = ledge->NTriangles * 3;

		Vector3[] verts = new Vector3[vertCount];
		for (int i = 0; i < ledge->NTriangles; i++) {
			for (int j = 0; j < 3; j++) {
				int index = triangles[i].GetEdge(j).StartPointIndex;
				float* vertex = (float*)(vertices + index * IVPAlignedVectorSize);
				verts[i * 3 + j] = new Vector3 { X = vertex[0], Y = vertex[2], Z = -vertex[1] };
			}
		}

		HullData hull = BoxHullCooking.CreateHullSafe(verts, BoxHullCooking.MaxHullVertices);
		if (hull.IsNull) {
			Vector3 min = BoxToSource.Unitless(verts[0]), max = min;
			for (int i = 1; i < vertCount; i++) {
				min = Vector3.Min(min, BoxToSource.Unitless(verts[i]));
				max = Vector3.Max(max, BoxToSource.Unitless(verts[i]));
			}
			float pad = SourceToBox.Distance(0.1f);
			min -= new Vector3(pad);
			max += new Vector3(pad);
			Span<Vector3> corners = stackalloc Vector3[8];
			for (int i = 0; i < 8; i++)
				corners[i] = new Vector3 { X = (i & 1) != 0 ? max.X : min.X, Y = (i & 2) != 0 ? max.Y : min.Y, Z = (i & 4) != 0 ? max.Z : min.Z };
			hull = BoxHullCooking.CreateHullSafe(corners, 8);
			if (hull.IsNull)
				return null;
			Warning($"Uncookable ledge ({vertCount} points) approximated by its bounding box\n");
		}

		BoxPhysConvex convex = new() {
			Hull = hull,
			GameData = (uint)ledge->ClientData,
			QueryVerts = verts,
			QueryMaterials = new byte[ledge->NTriangles]
		};
		for (int i = 0; i < ledge->NTriangles; i++)
			convex.QueryMaterials[i] = (byte)triangles[i].MaterialIndex;

		return convex;
	}

	static void GetAllLedges(CompactLedgeNode* node, List<nint> output) {
		if (node == null)
			return;

		if (node->OffsetRightNode != 0) {
			GetAllLedges((CompactLedgeNode*)((byte*)node + node->OffsetRightNode), output);
			GetAllLedges(node + 1, output);
		}
		else
			output.Add((nint)((byte*)node + node->OffsetCompactLedge));
	}

	public static BoxPhysCollide? DeserializePoly(CompactSurface* surface) {
		CompactLedgeNode* firstNode = (CompactLedgeNode*)((byte*)surface + surface->OffsetLedgetreeRoot);

		List<nint> ledges = [];
		GetAllLedges(firstNode, ledges);

		BoxPhysCollide collide = new() {
			MassCenter = BoxToSource.Distance(new Vector3 { X = surface->MassCenter.X, Y = surface->MassCenter.Z, Z = -surface->MassCenter.Y }),
			UnitInertia = new Vector3(surface->RotationInertia.X, surface->RotationInertia.Z, surface->RotationInertia.Y)
		};
		foreach (nint ledge in ledges) {
			BoxPhysConvex? convex = LedgeToConvex((CompactLedge*)ledge);
			if (convex != null)
				collide.Convexes.Add(convex);
		}

		if (collide.Convexes.Count == 0)
			return null;
		return collide;
	}

	public static BoxPhysCollide? DeserializePoly(CollideHeader* collideHeader) {
		CompactSurfaceHeader* surfaceHeader = (CompactSurfaceHeader*)(collideHeader + 1);
		CompactSurface* surface = (CompactSurface*)(surfaceHeader + 1);
		return DeserializePoly(surface);
	}
}

public unsafe class PhysicsCollide : IPhysicsCollision
{
	const float TraceDistEpsilon = 0.15f;

	static BoxPhysCollide? Box(PhysCollide? collide) => collide as BoxPhysCollide;
	static BoxPhysConvex? Box(PhysConvex? convex) => convex as BoxPhysConvex;

	static BoxPhysConvex? HullToConvex(HullData hull) => hull.IsNull ? null : new BoxPhysConvex { Hull = hull };

	public PhysConvex ConvexFromVerts(Span<Vector3> verts) {
		Vector3[] points = new Vector3[verts.Length];
		for (int i = 0; i < verts.Length; i++)
			points[i] = SourceToBox.Distance(verts[i]);

		return HullToConvex(BoxHullCooking.CreateHullSafe(points, BoxHullCooking.MaxHullVertices))!;
	}

	public PhysConvex ConvexFromPlanes(Span<float> planes, float mergeDistance) => null!;

	public float ConvexVolume(PhysConvex convex) {
		BoxPhysConvex? box = Box(convex);
		if (box == null || box.Hull.IsNull)
			return 0.0f;

		MassData massData = box.Hull.ComputeMass(1.0f);
		return BoxToSource.Volume(massData.mass);
	}

	public float ConvexSurfaceArea(PhysConvex convex) => 0.0f;

	public void SetConvexGameData(PhysConvex convex, uint gameData) {
		BoxPhysConvex? box = Box(convex);
		if (box != null)
			box.GameData = gameData;
	}

	public void ConvexFree(PhysConvex convex) => Box(convex)?.Free();

	public PhysConvex BBoxToConvex(in Vector3 mins, in Vector3 maxs) {
		Span<Vector3> corners = stackalloc Vector3[8];
		for (int i = 0; i < 8; i++) {
			Vector3 corner = new((i & 1) != 0 ? maxs.X : mins.X, (i & 2) != 0 ? maxs.Y : mins.Y, (i & 4) != 0 ? maxs.Z : mins.Z);
			corners[i] = SourceToBox.Distance(corner);
		}

		return HullToConvex(BoxHullCooking.CreateHullSafe(corners, 8))!;
	}

	public PhysConvex ConvexFromConvexPolyhedron<T>(in T convexPolyhedron) where T : IPolyhedron => throw new NotImplementedException();

	public void ConvexesFromConvexPolygon(in Vector3 polyNormal, ReadOnlySpan<Vector3> points, int pointCount, Span<PhysConvex> output) { }

	public PhysPolysoup PolysoupCreate() => new BoxPhysPolysoup();

	public void PolysoupDestroy(PhysPolysoup soup) { }

	public void PolysoupAddTriangle(PhysPolysoup soup, in Vector3 a, in Vector3 b, in Vector3 c, int materialIndex7bits) {
		if (soup is not BoxPhysPolysoup box)
			return;

		box.Vertices.Add(SourceToBox.Distance(a));
		box.Vertices.Add(SourceToBox.Distance(b));
		box.Vertices.Add(SourceToBox.Distance(c));
		box.MaterialIndices.Add((byte)materialIndex7bits);
	}

	public PhysCollide ConvertPolysoupToCollide(PhysPolysoup soup, bool useMOPP) {
		if (soup is not BoxPhysPolysoup box || box.Vertices.Count < 3)
			return null!;

		int[] indices = new int[box.Vertices.Count];
		for (int i = 0; i < indices.Length; i++)
			indices[i] = i;

		Span<Vector3> vertices = CollectionsMarshal.AsSpan(box.Vertices);
		Span<byte> materials = CollectionsMarshal.AsSpan(box.MaterialIndices);

		MeshDef def = default;
		def.vertices = vertices;
		def.indices = indices;
		def.materialIndices = materials;
		def.weldVertices = true;
		def.weldTolerance = SourceToBox.Distance(0.1f);
		def.identifyEdges = true;
		MeshData mesh = MeshData.Create(def, default);

		if (mesh.IsNull)
			return null!;

		return new BoxPhysCollide { Mesh = mesh };
	}

	public PhysCollide ConvertConvexToCollide(Span<PhysConvex> convex) => ConvertConvexToCollideParams(convex, default);

	public PhysCollide ConvertConvexToCollideParams(Span<PhysConvex> convex, in ConvertConvexParams convertParams) {
		BoxPhysCollide collide = new();

		Vector3 weightedCenter = default;
		Vector3 weightedInertia = default;
		float totalMass = 0.0f;

		for (int i = 0; i < convex.Length; i++) {
			BoxPhysConvex? box = Box(convex[i]);
			if (box == null)
				continue;

			collide.Convexes.Add(box);

			if (!box.Hull.IsNull) {
				MassData massData = box.Hull.ComputeMass(1.0f);
				weightedCenter += massData.mass * BoxToSource.Unitless(massData.center);
				weightedInertia += new Vector3(massData.inertia.cx.X, massData.inertia.cy.Y, massData.inertia.cz.Z);
				totalMass += massData.mass;
			}
		}

		if (totalMass > 0.0f) {
			collide.MassCenter = BoxToSource.Distance(SourceToBox.Unitless(weightedCenter / totalMass));
			collide.UnitInertia = weightedInertia / totalMass;
		}

		return collide;
	}

	public void DestroyCollide(PhysCollide collide) {
		BoxPhysCollide? box = Box(collide);
		if (box == null)
			return;

		foreach (BoxPhysConvex convex in box.Convexes)
			convex.Free();
		box.Convexes.Clear();

		if (!box.Mesh.IsNull)
			box.Mesh.Destroy();
		box.Mesh = default;
	}

	public int CollideSize(PhysCollide collide) => 0;
	public int CollideWrite(Span<byte> dest, PhysCollide collide, bool swap = false) => 0;
	public PhysCollide UnserializeCollide(ReadOnlySpan<byte> buffer, int size, int index) => null!;

	public float CollideVolume(PhysCollide collide) {
		BoxPhysCollide? box = Box(collide);
		if (box == null)
			return 0.0f;

		float volume = 0.0f;
		foreach (BoxPhysConvex convex in box.Convexes)
			volume += ConvexVolume(convex);

		return volume;
	}

	public float CollideSurfaceArea(PhysCollide collide) => 0.0f;

	public Vector3 CollideGetExtent(PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles, in Vector3 direction) {
		BoxPhysCollide? box = Box(collide);
		if (box == null)
			return collideOrigin;

		Transform xf = SourceToBox.Transform(collideOrigin, collideAngles);
		Vector3 localDir = BoxMath.InvRotateVector(xf.q, SourceToBox.Unitless(direction));

		float best = float.MinValue;
		Vector3 bestPoint = default;
		bool found = false;

		foreach (BoxPhysConvex convex in box.Convexes) {
			HullData hull = convex.Hull;
			if (hull.IsNull)
				continue;

			ReadOnlySpan<Vector3> points = hull.Points;
			for (int p = 0; p < points.Length; p++) {
				float dot = Vector3.Dot(BoxToSource.Unitless(points[p]), BoxToSource.Unitless(localDir));
				if (dot > best) {
					best = dot;
					bestPoint = points[p];
					found = true;
				}
			}
		}

		if (!found)
			return collideOrigin;

		return BoxToSource.Distance(BoxMath.TransformPoint(xf, bestPoint));
	}

	public void CollideGetAABB(out Vector3 mins, out Vector3 maxs, PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles) {
		BoxPhysCollide? box = Box(collide);
		if (box == null) {
			mins = collideOrigin;
			maxs = collideOrigin;
			return;
		}

		Transform xf = SourceToBox.Transform(collideOrigin, collideAngles);

		AABB bounds = default;
		bool hasBounds = false;
		foreach (BoxPhysConvex convex in box.Convexes) {
			if (convex.Hull.IsNull)
				continue;

			AABB hullBounds = convex.Hull.ComputeAABB(xf);
			bounds = hasBounds ? BoxMath.AABB_Union(bounds, hullBounds) : hullBounds;
			hasBounds = true;
		}

		if (!box.Mesh.IsNull) {
			AABB meshBounds = box.Mesh.ComputeAABB(xf, Vector3.One);
			bounds = hasBounds ? BoxMath.AABB_Union(bounds, meshBounds) : meshBounds;
			hasBounds = true;
		}

		if (!hasBounds) {
			mins = collideOrigin;
			maxs = collideOrigin;
			return;
		}

		BoxToSource.AABBBounds(bounds, out mins, out maxs);
	}

	public void CollideGetMassCenter(PhysCollide collide, out Vector3 outMassCenter) => outMassCenter = Box(collide)?.MassCenter ?? default;

	public void CollideSetMassCenter(PhysCollide collide, in Vector3 massCenter) {
		BoxPhysCollide? box = Box(collide);
		if (box != null)
			box.MassCenter = massCenter;
	}

	public Vector3 CollideGetOrthographicAreas(PhysCollide collide) => Box(collide)?.OrthographicAreas ?? new Vector3(1.0f, 1.0f, 1.0f);

	public void CollideSetOrthographicAreas(PhysCollide collide, in Vector3 areas) {
		BoxPhysCollide? box = Box(collide);
		if (box != null)
			box.OrthographicAreas = areas;
	}

	public int CollideIndex(PhysCollide collide) => 0;

	public PhysCollide BBoxToCollide(in Vector3 mins, in Vector3 maxs) {
		PhysConvex? convex = BBoxToConvex(mins, maxs);
		if (convex == null)
			return null!;

		Span<PhysConvex> convexes = [convex];
		return ConvertConvexToCollide(convexes);
	}

	public int GetConvexesUsedInCollideable(PhysCollide collideable, Span<PhysConvex> outputArray) {
		BoxPhysCollide? box = Box(collideable);
		if (box == null)
			return 0;

		int count = Math.Min(box.Convexes.Count, outputArray.Length);
		for (int i = 0; i < count; i++)
			outputArray[i] = box.Convexes[i];

		return count;
	}

	static void ClearTrace(out Trace trace) {
		trace = default;
		trace.Fraction = 1.0f;
		trace.FractionLeftSolid = 0.0f;
		trace.Surface.Name = "**empty**";
	}

	static float CalculateSourceFraction(in Vector3 delta, float fraction, in Vector3 normal) {
		float length = delta.Length();
		if (length == 0.0f)
			return 0.0f;

		Vector3 dir = delta / length;
		float hitLength = length * fraction;

		float dot = Vector3.Dot(dir, normal);
		if (dot < 0.0f)
			hitLength += TraceDistEpsilon / dot;

		return MathF.Max(hitLength, 0.0f) / length;
	}

	static void SetSolid(ref Trace trace, in Vector3 start, in Vector3 normal) {
		trace.Fraction = 0.0f;
		trace.EndPos = start;
		trace.Plane.Normal = normal;
		trace.Plane.Dist = Vector3.Dot(trace.EndPos, normal);
		trace.Contents = Contents.Solid;
		trace.AllSolid = true;
		trace.StartSolid = true;
	}

	static float MinSeparation(LocalManifoldPoint* points, int count, float start) {
		float separation = start;
		for (int p = 0; p < count; p++)
			separation = MathF.Min(separation, points[p].separation);
		return separation;
	}

	struct BestHit
	{
		public float TraceLength;
		public float TotalTraceLength;
		public Vector3 Normal;
		public uint Contents;
		public bool Hit;
		public bool StartSolid;
		public bool AllSolid;
	}

	static void AcceptHit(ref BestHit best, float traceLength, in Vector3 normal, uint contents, bool startSolid) {
		if (traceLength < best.TotalTraceLength) {
			best.TotalTraceLength = traceLength;
			best.TraceLength = traceLength;
			best.Normal = normal;
			best.Contents = contents;
			best.StartSolid = startSolid;
			best.AllSolid = startSolid;
			best.Hit = true;
		}
	}

	static float PullBack(float hitLength, in Vector3 rayDir, in Vector3 normal, float epsilon) {
		float dot = Vector3.Dot(rayDir, normal);
		if (dot < 0)
			hitLength += epsilon / dot;
		return hitLength < 0.0f ? 0.0f : hitLength;
	}

	static void AcceptCast(ref BestHit best, in CastOutput output, uint contents, in Transform surfaceXf, float baseLength, in Vector3 rayDir) {
		if (!output.hit)
			return;

		if (output.fraction == 0.0f && BoxMath.Dot(output.normal, output.normal) == 0.0f) {
			AcceptHit(ref best, 0.0f, default, contents, true);
			return;
		}

		Vector3 worldNormal = BoxToSource.Unitless(BoxMath.RotateVector(surfaceXf.q, output.normal));
		MathLib.VectorNormalize(ref worldNormal);
		float len = PullBack(output.fraction * baseLength, rayDir, worldNormal, DIST_EPSILON);
		AcceptHit(ref best, len, worldNormal, contents, false);
	}

	static void TraceBoxVsCollide(in Ray ray, uint contentsMask, IConvexInfo? convexInfo, PhysCollide? collide, in Vector3 collideOrigin, in QAngle collideAngles, out Trace trace) {
		ClearTrace(out trace);

		BoxPhysCollide? surface = Box(collide);
		if (surface == null) {
			Assert(surface != null);
			return;
		}

		Transform surfaceXf = SourceToBox.Transform(collideOrigin, collideAngles);

		Vector3 delta = ray.Delta;
		float baseLength = MathF.Sqrt(Vector3.Dot(delta, delta));
		Vector3 rayDir = delta;
		if (baseLength > 0)
			rayDir /= baseLength;

		Vector3 localStart = BoxMath.InvTransformPoint(surfaceXf, SourceToBox.Distance(ray.Start));
		Vector3 localDelta = BoxMath.InvRotateVector(surfaceXf.q, SourceToBox.Distance(delta));

		BestHit best = default;
		best.TotalTraceLength = MathF.Max(baseLength, 1e-8f);

		uint defaultContents = (uint)Contents.Solid;
		Vector3 unitScale = Vector3.One;

		if (ray.IsRay) {
			RayCastInput input = new() { origin = localStart, translation = localDelta, maxFraction = 1.0f };

			if (!surface.Mesh.IsNull) {
				uint contents = convexInfo?.GetContents(0) ?? defaultContents;
				if ((contentsMask & contents) != 0) {
					Mesh mesh = new() { data = surface.Mesh, scale = unitScale };
					AcceptCast(ref best, mesh.RayCast(input), contents, surfaceXf, baseLength, rayDir);
				}
			}
			foreach (BoxPhysConvex convex in surface.Convexes) {
				if (convex.Hull.IsNull)
					continue;
				uint contents = convexInfo?.GetContents((int)convex.GameData) ?? defaultContents;
				if ((contentsMask & contents) == 0)
					continue;
				AcceptCast(ref best, convex.Hull.RayCast(input), contents, surfaceXf, baseLength, rayDir);
			}
		}
		else {
			Span<Vector3> corners = stackalloc Vector3[8];
			Vector3 e = ray.Extents;
			for (int i = 0; i < 8; i++) {
				Vector3 corner = new(
					ray.Start.X + ((i & 1) != 0 ? e.X : -e.X), ray.Start.Y + ((i & 2) != 0 ? e.Y : -e.Y),
					ray.Start.Z + ((i & 4) != 0 ? e.Z : -e.Z));
				corners[i] = BoxMath.InvTransformPoint(surfaceXf, SourceToBox.Distance(corner));
			}

			scoped ShapeCastInput input = default;
			input.proxy.points = corners;
			input.proxy.radius = 0.0f;
			input.translation = localDelta;
			input.maxFraction = 1.0f;
			input.canEncroach = true;

			if (!surface.Mesh.IsNull) {
				uint contents = convexInfo?.GetContents(0) ?? defaultContents;
				if ((contentsMask & contents) != 0) {
					Mesh mesh = new() { data = surface.Mesh, scale = unitScale };
					AcceptCast(ref best, mesh.ShapeCast(input), contents, surfaceXf, baseLength, rayDir);
				}
			}
			foreach (BoxPhysConvex convex in surface.Convexes) {
				if (convex.Hull.IsNull)
					continue;
				uint contents = convexInfo?.GetContents((int)convex.GameData) ?? defaultContents;
				if ((contentsMask & contents) == 0)
					continue;
				AcceptCast(ref best, convex.Hull.ShapeCast(input), contents, surfaceXf, baseLength, rayDir);
			}
		}

		if (best.Hit) {
			float ooBaseLength = baseLength > 0.0f ? 1.0f / baseLength : 0.0f;
			trace.Fraction = Math.Clamp(best.TraceLength * ooBaseLength, 0.0f, 1.0f);
			trace.Plane.Normal = best.Normal;
			trace.StartSolid = best.StartSolid;
			trace.AllSolid = best.AllSolid;
			trace.Contents = (Contents)best.Contents;
		}

		trace.StartPos = ray.Start + ray.StartOffset;
		trace.EndPos = trace.StartPos + trace.Fraction * ray.Delta;
		if (trace.DidHit())
			trace.Plane.Dist = Vector3.Dot(trace.EndPos, trace.Plane.Normal);
	}

	public void TraceBox(in Vector3 start, in Vector3 end, in Vector3 mins, in Vector3 maxs, PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles, out Trace trace) {
		Ray ray = default;
		ray.Init(start, end, mins, maxs);
		TraceBoxVsCollide(ray, (uint)Mask.All, null, collide, collideOrigin, collideAngles, out trace);
	}

	public void TraceBox(in Ray ray, PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles, out Trace trace)
		=> TraceBoxVsCollide(ray, (uint)Mask.All, null, collide, collideOrigin, collideAngles, out trace);

	public void TraceBox(in Ray ray, Contents contentsMask, IConvexInfo? convexInfo, PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles, out Trace trace)
		=> TraceBoxVsCollide(ray, (uint)contentsMask, convexInfo, collide, collideOrigin, collideAngles, out trace);

	public void TraceCollide(in Vector3 start, in Vector3 end, PhysCollide pSweepCollide, in QAngle sweepAngles, PhysCollide collide, in Vector3 collideOrigin, in QAngle collideAngles, out Trace trace) {
		ClearTrace(out trace);
		trace.StartPos = start;
		trace.EndPos = start;

		BoxPhysCollide? sweep = Box(pSweepCollide);
		BoxPhysCollide? target = Box(collide);
		if (sweep == null || target == null)
			return;

		if (start != end)
			return;

		Transform xfSweep = SourceToBox.Transform(start, sweepAngles);
		Transform xfHit = SourceToBox.Transform(collideOrigin, collideAngles);
		Transform sweepToHit = BoxMath.InvMulTransforms(xfHit, xfSweep);

		foreach (BoxPhysConvex convex in target.Convexes) {
			HullData hull = convex.Hull;
			if (hull.IsNull)
				continue;

			foreach (BoxPhysConvex sweepConvex in sweep.Convexes) {
				HullData sweepHull = sweepConvex.Hull;
				if (sweepHull.IsNull)
					continue;

				DistanceInput input = default;
				input.proxyA = new ShapeProxy { points = hull.Points, radius = 0.0f };
				input.proxyB = new ShapeProxy { points = sweepHull.Points, radius = 0.0f };
				input.transform = sweepToHit;
				input.useRadii = true;

				SimplexCache cache = default;
				DistanceOutput output = B3.ShapeDistance(input, ref cache, default);
				if (output.distance < Box3D.Constants.OverlapSlop) {
					trace.Fraction = 0.0f;
					trace.Contents = Contents.Solid;
					trace.AllSolid = true;
					trace.StartSolid = true;
					return;
				}
			}
		}
	}

	public bool IsBoxIntersectingCone(in Vector3 boxAbsMins, in Vector3 boxAbsMaxs, in TruncatedCone cone) => false;

	public void VCollideLoad(VCollide output, int solidCount, ReadOnlySpan<byte> buffer, bool swap = false) {
		if (swap)
			return;

		output.SolidCount = (ushort)solidCount;
		output.Solids = new PhysCollide[solidCount];

		int cursor = 0;
		fixed (byte* pBuffer = buffer) {
			for (int i = 0; i < solidCount; i++) {
				output.Solids[i] = null;

				int solidSize = *(int*)(pBuffer + cursor);
				cursor += sizeof(int);

				IVPCompat.CollideHeader* collideHeader = (IVPCompat.CollideHeader*)(pBuffer + cursor);

				if (collideHeader->VPhysicsID == IVPCompat.VPHYSICS_COLLISION_ID) {
					if (collideHeader->Version != IVPCompat.VPHYSICS_COLLISION_VERSION)
						Warning($"Solid with unknown version: 0x{collideHeader->Version:x}, may crash!\n");

					if (collideHeader->ModelType == IVPCompat.COLLIDE_POLY)
						output.Solids[i] = IVPCompat.DeserializePoly(collideHeader);
					else
						Warning($"Unsupported solid type 0x{collideHeader->ModelType:x} on solid {i}. Skipping...\n");
				}
				else {
					IVPCompat.CompactSurface* compactSurface = (IVPCompat.CompactSurface*)(pBuffer + cursor);
					int legacyModelType = compactSurface->Dummy2;
					if (legacyModelType == IVPCompat.IVP_COMPACT_SURFACE_SUPER_LEGACY || legacyModelType == IVPCompat.IVP_COMPACT_SURFACE_ID || legacyModelType == IVPCompat.IVP_COMPACT_SURFACE_ID_SWAPPED)
						output.Solids[i] = IVPCompat.DeserializePoly(compactSurface);
					else
						Warning($"Unsupported legacy solid type 0x{legacyModelType:x} on solid {i}. Skipping...\n");
				}

				cursor += solidSize;
			}
		}

		int keyValuesSize = buffer.Length - cursor;
		output.KeyValues = buffer.Slice(cursor, keyValuesSize).ToArray();
		output.DescSize = (short)keyValuesSize;
		output.IsPacked = false;
	}

	public void VCollideUnload(VCollide vCollide) {
		if (vCollide.Solids != null) {
			foreach (PhysCollide? solid in vCollide.Solids)
				if (solid != null)
					DestroyCollide(solid);
		}

		vCollide.Solids = null;
		vCollide.KeyValues = null;
		vCollide.SolidCount = 0;
		vCollide.DescSize = 0;
		vCollide.IsPacked = false;
	}

	public IVPhysicsKeyParser VPhysicsKeyParserCreate(ReadOnlySpan<byte> keyData) => new VPhysicsKeyParser(keyData);

	public void VPhysicsKeyParserDestroy(IVPhysicsKeyParser parser) { }

	static void TriangulateHull(HullData hull, List<Vector3> verts, List<int>? materials) {
		ReadOnlySpan<Vector3> points = hull.Points;
		ReadOnlySpan<HullFace> faces = hull.Faces;
		ReadOnlySpan<HullHalfEdge> edges = hull.Edges;

		List<int> loop = [];
		for (int f = 0; f < faces.Length; f++) {
			loop.Clear();
			int start = faces[f].edge;
			int e = start;
			do {
				loop.Add(edges[e].origin);
				e = edges[e].next;
			} while (e != start && loop.Count < 256);

			for (int k = 1; k + 1 < loop.Count; k++) {
				verts.Add(BoxToSource.Distance(points[loop[0]]));
				verts.Add(BoxToSource.Distance(points[loop[k]]));
				verts.Add(BoxToSource.Distance(points[loop[k + 1]]));
				materials?.Add(0);
			}
		}
	}

	public int CreateDebugMesh(PhysCollide collisionModel, out Span<Vector3> outVerts) {
		outVerts = default;
		BoxPhysCollide? box = Box(collisionModel);
		if (box == null)
			return 0;

		List<Vector3> verts = [];

		foreach (BoxPhysConvex convex in box.Convexes) {
			if (convex.QueryVerts != null && convex.QueryVerts.Length > 0) {
				foreach (Vector3 v in convex.QueryVerts)
					verts.Add(BoxToSource.Distance(v));
			}
			else if (!convex.Hull.IsNull)
				TriangulateHull(convex.Hull, verts, null);
		}

		if (!box.Mesh.IsNull) {
			ReadOnlySpan<Vector3> meshVerts = box.Mesh.Vertices;
			ReadOnlySpan<MeshTriangle> tris = box.Mesh.Triangles;
			for (int t = 0; t < tris.Length; t++) {
				verts.Add(BoxToSource.Distance(meshVerts[tris[t].index1]));
				verts.Add(BoxToSource.Distance(meshVerts[tris[t].index2]));
				verts.Add(BoxToSource.Distance(meshVerts[tris[t].index3]));
			}
		}

		if (verts.Count == 0)
			return 0;

		outVerts = verts.ToArray();
		return verts.Count;
	}

	public void DestroyDebugMesh(int vertCount, Span<Vector3> outVerts) { }

	sealed class BoxCollisionQuery : ICollisionQuery
	{
		struct ConvexInfo
		{
			public int TriStart;
			public int TriCount;
			public uint GameData;
		}

		readonly List<Vector3> Verts = [];
		readonly List<int> Materials = [];
		readonly List<ConvexInfo> Convexes = [];

		public BoxCollisionQuery(BoxPhysCollide? collide) {
			if (collide == null)
				return;

			foreach (BoxPhysConvex convex in collide.Convexes) {
				ConvexInfo info = new() { GameData = convex.GameData, TriStart = Materials.Count };

				if (convex.QueryMaterials != null && convex.QueryMaterials.Length > 0) {
					for (int t = 0; t < convex.QueryMaterials.Length; t++) {
						Verts.Add(BoxToSource.Distance(convex.QueryVerts![t * 3 + 0]));
						Verts.Add(BoxToSource.Distance(convex.QueryVerts![t * 3 + 1]));
						Verts.Add(BoxToSource.Distance(convex.QueryVerts![t * 3 + 2]));
						Materials.Add(convex.QueryMaterials[t]);
					}
				}
				else if (!convex.Hull.IsNull)
					TriangulateHull(convex.Hull, Verts, Materials);

				info.TriCount = Materials.Count - info.TriStart;
				Convexes.Add(info);
			}

			if (!collide.Mesh.IsNull) {
				MeshData mesh = collide.Mesh;
				ReadOnlySpan<Vector3> verts = mesh.Vertices;
				ReadOnlySpan<MeshTriangle> tris = mesh.Triangles;
				ReadOnlySpan<byte> mats = mesh.MaterialIndices;

				ConvexInfo info = new() { GameData = 0, TriStart = Materials.Count };
				for (int t = 0; t < tris.Length; t++) {
					Verts.Add(BoxToSource.Distance(verts[tris[t].index1]));
					Verts.Add(BoxToSource.Distance(verts[tris[t].index2]));
					Verts.Add(BoxToSource.Distance(verts[tris[t].index3]));
					Materials.Add(!mats.IsEmpty ? mats[t] : 0);
				}
				info.TriCount = Materials.Count - info.TriStart;
				Convexes.Add(info);
			}
		}

		bool Valid(int convexIndex, int triangleIndex) => convexIndex >= 0 && convexIndex < Convexes.Count && triangleIndex >= 0 && triangleIndex < Convexes[convexIndex].TriCount;

		public int ConvexCount() => Convexes.Count;
		public int TriangleCount(int convexIndex) => convexIndex >= 0 && convexIndex < Convexes.Count ? Convexes[convexIndex].TriCount : 0;
		public uint GetGameData(int convexIndex) => convexIndex >= 0 && convexIndex < Convexes.Count ? Convexes[convexIndex].GameData : 0;

		public void GetTriangleVerts(int convexIndex, int triangleIndex, Span<Vector3> verts) {
			if (!Valid(convexIndex, triangleIndex)) {
				verts[0] = verts[1] = verts[2] = default;
				return;
			}
			int baseIndex = (Convexes[convexIndex].TriStart + triangleIndex) * 3;
			verts[0] = Verts[baseIndex + 0];
			verts[1] = Verts[baseIndex + 1];
			verts[2] = Verts[baseIndex + 2];
		}

		public void SetTriangleVerts(int convexIndex, int triangleIndex, ReadOnlySpan<Vector3> verts) { }

		public int GetTriangleMaterialIndex(int convexIndex, int triangleIndex)
			=> Valid(convexIndex, triangleIndex) ? Materials[Convexes[convexIndex].TriStart + triangleIndex] : 0;

		public void SetTriangleMaterialIndex(int convexIndex, int triangleIndex, int index7bits) { }
	}

	public ICollisionQuery CreateQueryModel(PhysCollide collide) => new BoxCollisionQuery(Box(collide));

	public void DestroyQueryModel(ICollisionQuery query) { }

	public IPhysicsCollision ThreadContextCreate() => this;

	public void ThreadContextDestroy(IPhysicsCollision threadContext) { }

	public PhysCollide? CreateVirtualMesh(in VirtualMeshParams meshParams) {
		if (meshParams.MeshEventHandler == null)
			return null;

		VirtualMeshList list = default;
		meshParams.MeshEventHandler.GetVirtualMesh(meshParams.UserData!, ref list);
		if (list.TriangleCount <= 0 || list.VertexCount <= 0)
			return null;

		Vector3[] verts = new Vector3[list.VertexCount];
		for (int i = 0; i < list.VertexCount; i++)
			verts[i] = SourceToBox.Distance(list.Verts![i]);

		int[] indices = new int[list.TriangleCount * 3];
		for (int i = 0; i < list.TriangleCount; i++) {
			indices[i * 3 + 0] = list.Indices[i * 3 + 0];
			indices[i * 3 + 1] = list.Indices[i * 3 + 2];
			indices[i * 3 + 2] = list.Indices[i * 3 + 1];
		}

		MeshDef def = default;
		def.vertices = verts;
		def.indices = indices;
		def.useMedianSplit = true;
		def.identifyEdges = true;
		MeshData mesh = MeshData.Create(def, default);
		if (mesh.IsNull)
			return null;

		return new BoxPhysCollide { Mesh = mesh };
	}

	public bool SupportsVirtualMesh() => true;

	public bool GetBBoxCacheSize(out uint cachedSize, out nint cachedCount) {
		cachedSize = 0;
		cachedCount = 0;
		return false;
	}

	public Polyhedron PolyhedronFromConvex(PhysConvex convex, bool useTempPolyhedron) => null!;

	public void OutputDebugInfo(PhysCollide collide) { }

	public uint ReadStat(int statID) => 0;
}
