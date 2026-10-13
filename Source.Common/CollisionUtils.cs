using Source.Common.Formats.BSP;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Source.Common;

public struct BoxTraceInfo
{
	public float T1;
	public float T2;
	public int HitSide;
	public bool StartSolid;
}

public static class CollisionUtils
{
	public static void Collision_ClearTrace(in Vector3 rayStart, in Vector3 rayDelta, ref GameTrace trace) {
		trace.StartPos = rayStart;
		trace.EndPos = rayStart;
		trace.EndPos += rayDelta;
		trace.StartSolid = false;
		trace.AllSolid = false;
		trace.Fraction = 1.0f;
		trace.Contents = 0;
	}

	public static bool IntersectRayWithBox(in Vector3 rayStart, in Vector3 rayDelta, in Vector3 boxMins, in Vector3 boxMaxs, float tolerance, out BoxTraceInfo trace) {
		int i;
		float d1, d2;
		float f;

		trace.T1 = -1.0f;
		trace.T2 = 1.0f;
		trace.HitSide = -1;

		trace.StartSolid = true;

		for (i = 0; i < 6; ++i) {
			if (i >= 3) {
				d1 = rayStart[i - 3] - boxMaxs[i - 3];
				d2 = d1 + rayDelta[i - 3];
			}
			else {
				d1 = -rayStart[i] + boxMins[i];
				d2 = d1 - rayDelta[i];
			}

			if (d1 > 0 && d2 > 0) {
				trace.StartSolid = false;
				return false;
			}

			if (d1 <= 0 && d2 <= 0)
				continue;

			if (d1 > 0)
				trace.StartSolid = false;

			if (d1 > d2) {
				f = d1 - tolerance;
				if (f < 0)
					f = 0;
				f /= (d1 - d2);
				if (f > trace.T1) {
					trace.T1 = f;
					trace.HitSide = i;
				}
			}
			else {
				f = (d1 + tolerance) / (d1 - d2);
				if (f < trace.T2)
					trace.T2 = f;
			}
		}

		return trace.StartSolid || (trace.T1 < trace.T2 && trace.T1 >= 0.0f);
	}

	public static bool IntersectRayWithBox(in Vector3 rayStart, in Vector3 rayDelta, in Vector3 boxMins, in Vector3 boxMaxs, float tolerance, ref GameTrace trace, ref float fractionLeftSolid) {
		Collision_ClearTrace(rayStart, rayDelta, ref trace);

		if (IntersectRayWithBox(rayStart, rayDelta, boxMins, boxMaxs, tolerance, out BoxTraceInfo boxTrace)) {
			trace.StartSolid = boxTrace.StartSolid;
			if (boxTrace.T1 < boxTrace.T2 && boxTrace.T1 >= 0.0f) {
				trace.Fraction = boxTrace.T1;
				MathLib.VectorMA(trace.StartPos, boxTrace.T1, rayDelta, out trace.EndPos);
				trace.Contents = Contents.Solid;
				trace.Plane.Normal = vec3_origin;
				if (boxTrace.HitSide >= 3) {
					boxTrace.HitSide -= 3;
					trace.Plane.Dist = boxMaxs[boxTrace.HitSide];
					trace.Plane.Normal[boxTrace.HitSide] = 1.0f;
					trace.Plane.Type = (PlaneType)boxTrace.HitSide;
				}
				else {
					trace.Plane.Dist = -boxMins[boxTrace.HitSide];
					trace.Plane.Normal[boxTrace.HitSide] = -1.0f;
					trace.Plane.Type = (PlaneType)boxTrace.HitSide;
				}
				return true;
			}

			if (trace.StartSolid) {
				trace.AllSolid = (boxTrace.T2 <= 0.0f) || (boxTrace.T2 >= 1.0f);
				trace.Fraction = 0;
				if (!Unsafe.IsNullRef(ref fractionLeftSolid))
					fractionLeftSolid = boxTrace.T2;
				trace.EndPos = trace.StartPos;
				trace.Contents = Contents.Solid;
				trace.Plane.Dist = trace.StartPos[0];
				trace.Plane.Normal = new(1.0f, 0.0f, 0.0f);
				trace.Plane.Type = 0;
				trace.StartPos = rayStart + (boxTrace.T2 * rayDelta);
				return true;
			}
		}

		return false;
	}

	public static bool IntersectRayWithBox(in Ray ray, in Vector3 boxMins, in Vector3 boxMaxs, float tolerance, ref GameTrace trace, ref float fractionLeftSolid) {
		if (!ray.IsRay) {
			Vector3 expandedMins = boxMins - ray.Extents;
			Vector3 expandedMaxs = boxMaxs + ray.Extents;
			bool intersects = IntersectRayWithBox(ray.Start, ray.Delta, expandedMins, expandedMaxs, tolerance, ref trace, ref fractionLeftSolid);
			trace.StartPos += ray.StartOffset;
			trace.EndPos += ray.StartOffset;
			return intersects;
		}
		return IntersectRayWithBox(ray.Start, ray.Delta, boxMins, boxMaxs, tolerance, ref trace, ref fractionLeftSolid);
	}

	public static bool IntersectInfiniteRayWithSphere(in Vector3 rayOrigin, in Vector3 rayDelta, in Vector3 sphereCenter, float radius, out float t1, out float t2) {
		Vector3 sphereToRay = rayOrigin - sphereCenter;

		float a = Vector3.Dot(rayDelta, rayDelta);

		if (a == 0.0f) {
			t1 = t2 = 0.0f;
			return sphereToRay.LengthSquared() <= radius * radius;
		}

		float b = 2 * Vector3.Dot(sphereToRay, rayDelta);
		float c = Vector3.Dot(sphereToRay, sphereToRay) - radius * radius;
		float discrim = b * b - 4 * a * c;
		if (discrim < 0.0f) {
			t1 = t2 = 0.0f;
			return false;
		}

		discrim = MathF.Sqrt(discrim);
		float oo2a = 0.5f / a;
		t1 = (-b - discrim) * oo2a;
		t2 = (-b + discrim) * oo2a;
		return true;
	}

	public static bool IsSphereIntersectingCone(in Vector3 sphereCenter, float sphereRadius, in Vector3 coneOrigin, in Vector3 coneNormal, float coneSine, float coneCosine) {
		Vector3 backCenter = coneOrigin - (sphereRadius / coneSine) * coneNormal;
		Vector3 delta = sphereCenter - backCenter;
		float deltaLen = delta.Length();
		if (MathLib.DotProduct(coneNormal, delta) >= deltaLen * coneCosine) {
			delta = sphereCenter - coneOrigin;
			deltaLen = delta.Length();
			if (-MathLib.DotProduct(coneNormal, delta) >= deltaLen * coneSine)
				return deltaLen <= sphereRadius;
			return true;
		}
		return false;
	}

	public static bool IsBoxIntersectingRay(in Vector3 boxMin, in Vector3 boxMax, in Ray ray, float tolerance = 0.0f) {
		if (!ray.IsSwept) {
			Vector3 rayMins = ray.Start - ray.Extents;
			Vector3 rayMaxs = ray.Start + ray.Extents;
			rayMins += new Vector3(tolerance);
			rayMaxs += new Vector3(tolerance);

			return IsBoxIntersectingBox(boxMin, boxMax, rayMins, rayMaxs);
		}

		Vector3 expandedBoxMin = boxMin - ray.Extents;
		Vector3 expandedBoxMax = boxMax + ray.Extents;

		return IsBoxIntersectingRay(expandedBoxMin, expandedBoxMax, ray.Start, ray.Delta, Vector3.One / ray.Delta, tolerance);
	}

	public static float IntersectRayWithPlane(in Vector3 org, in Vector3 dir, in Vector3 normal, float dist) {
		float denom = MathLib.DotProduct(dir, normal);
		if (denom == 0.0f)
			return 0.0f;

		denom = 1.0f / denom;
		return (dist - MathLib.DotProduct(org, normal)) * denom;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float IntersectRayWithTriangle(in Ray ray, in Vector3 v1, in Vector3 v2, in Vector3 v3, bool oneSided) {
		Vector3 edge1 = v2 - v1;
		Vector3 edge2 = v3 - v1;

		if (oneSided) {
			Vector3 normal = Vector3.Cross(edge1, edge2);
			if (Vector3.Dot(normal, ray.Delta) >= 0.0f)
				return -1.0f;
		}

		Vector3 dirCrossEdge2 = Vector3.Cross(ray.Delta, edge2);

		float denom = Vector3.Dot(dirCrossEdge2, edge1);
		if (MathF.Abs(denom) < 1e-6f)
			return -1.0f;
		float invDenom = 1.0f / denom;

		Vector3 org = ray.Start - v1;
		float u = Vector3.Dot(dirCrossEdge2, org) * invDenom;
		if (u < 0.0f || u > 1.0f)
			return -1.0f;

		Vector3 orgCrossEdge1 = Vector3.Cross(org, edge1);
		float v = Vector3.Dot(orgCrossEdge1, ray.Delta) * invDenom;
		if (v < 0.0f || u + v > 1.0f)
			return -1.0f;

		float boxt = ComputeBoxOffset(ray);
		float t = Vector3.Dot(orgCrossEdge1, edge2) * invDenom;
		if (t < -boxt || t > 1.0f + boxt)
			return -1.0f;

		return Math.Clamp(t, 0.0f, 1.0f);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static bool IsBoxIntersectingRay(in Vector3 boxMin, in Vector3 boxMax, in Vector3 origin, in Vector3 vecDelta, in Vector3 vecInvDelta, float tolerance = 0.0f) {
		Vector128<float> start = MathLib.LoadFloat3(origin);
		Vector128<float> delta = MathLib.LoadFloat3(vecDelta);
		Vector128<float> boxMins = MathLib.LoadFloat3(boxMin);
		Vector128<float> boxMaxs = MathLib.LoadFloat3(boxMax);

		boxMins = MathLib.SubSIMD(boxMins, start);
		boxMaxs = MathLib.SubSIMD(boxMaxs, start);

		// Check to see if both the origin (start point) and the end point (delta) are on the front side
		// of any of the box sides - if so there can be no intersection
		Vector128<float> startOutMins = MathLib.CmpLtSIMD(MathLib.Four_Zeros, boxMins);
		Vector128<float> endOutMins = MathLib.CmpLtSIMD(delta, boxMins);
		Vector128<float> minsMask = MathLib.AndSIMD(startOutMins, endOutMins);
		Vector128<float> startOutMaxs = MathLib.CmpGtSIMD(MathLib.Four_Zeros, boxMaxs);
		Vector128<float> endOutMaxs = MathLib.CmpGtSIMD(delta, boxMaxs);
		Vector128<float> maxsMask = MathLib.AndSIMD(startOutMaxs, endOutMaxs);
		if (MathLib.IsAnyNegative(MathLib.SetWToZeroSIMD(MathLib.OrSIMD(minsMask, maxsMask))))
			return false;

		// now build the per-axis interval of t for intersections
		Vector128<float> epsilon = MathLib.ReplicateX4(tolerance);
		Vector128<float> invDelta = MathLib.LoadFloat3(vecInvDelta);
		boxMins = MathLib.SubSIMD(boxMins, epsilon);
		boxMaxs = MathLib.AddSIMD(boxMaxs, epsilon);

		boxMins = MathLib.MulSIMD(boxMins, invDelta);
		boxMaxs = MathLib.MulSIMD(boxMaxs, invDelta);

		Vector128<float> crossPlane = MathLib.OrSIMD(MathLib.XorSIMD(startOutMins, endOutMins), MathLib.XorSIMD(startOutMaxs, endOutMaxs));
		// only consider axes where we crossed a plane
		boxMins = MathLib.MaskedAssign(crossPlane, boxMins, MathLib.Four_Negative_FLT_MAX);
		boxMaxs = MathLib.MaskedAssign(crossPlane, boxMaxs, MathLib.Four_FLT_MAX);

		// now sort the interval per axis
		Vector128<float> mint = MathLib.MinSIMD(boxMins, boxMaxs);
		Vector128<float> maxt = MathLib.MaxSIMD(boxMins, boxMaxs);

		// now find the intersection of the intervals on all axes
		Vector128<float> firstOut = MathLib.FindLowestSIMD3(maxt);
		Vector128<float> lastIn = MathLib.FindHighestSIMD3(mint);
		// NOTE: This is really a scalar quantity now [t0,t1] == [lastIn,firstOut]
		firstOut = MathLib.MinSIMD(firstOut, MathLib.Four_Ones);
		lastIn = MathLib.MaxSIMD(lastIn, MathLib.Four_Zeros);

		// If the final interval is valid lastIn<firstOut, check for separation
		Vector128<float> separation = MathLib.CmpGtSIMD(lastIn, firstOut);

		return MathLib.IsAllZeros(separation);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float ComputeBoxOffset(in Ray ray) {
		if (ray.IsRay)
			return 1e-3f;

		float offset =
			Math.Abs(ray.Extents.X * ray.Delta.X) +
			Math.Abs(ray.Extents.Y * ray.Delta.Y) +
			Math.Abs(ray.Extents.Z * ray.Delta.Z);

		float invRSquared = 1.0f / ray.Delta.LengthSquared();
		offset *= invRSquared;

		return offset + 1e-3f;
	}

	public static bool IsBoxIntersectingBox(in Vector3 boxMin1, in Vector3 boxMax1, in Vector3 boxMin2, in Vector3 boxMax2) {
		if ((boxMin1[0] > boxMax2[0]) || (boxMax1[0] < boxMin2[0]))
			return false;
		if ((boxMin1[1] > boxMax2[1]) || (boxMax1[1] < boxMin2[1]))
			return false;
		if ((boxMin1[2] > boxMax2[2]) || (boxMax1[2] < boxMin2[2]))
			return false;
		return true;
	}

	public static bool IsBoxIntersectingBoxExtents(in Vector3 boxCenter1, in Vector3 boxHalfDiagonal1, in Vector3 boxCenter2, in Vector3 boxHalfDiagonal2) {
		Vector3 delta = Vector3.Abs(boxCenter1 - boxCenter2);
		Vector3 size = boxHalfDiagonal1 + boxHalfDiagonal2;
		return delta.X <= size.X && delta.Y <= size.Y && delta.Z <= size.Z;
	}

	public static bool IsSphereIntersectingSphere(in Vector3 center1, float radius1, in Vector3 center2, float radius2) {
		MathLib.VectorSubtract(center2, center1, out Vector3 delta);
		float distSq = delta.LengthSquared();
		float radiusSum = radius1 + radius2;
		return distSq <= (radiusSum * radiusSum);
	}

	public static bool IsBoxIntersectingSphere(in Vector3 boxMin, in Vector3 boxMax, in Vector3 center, float radius) {
		float dmin = 0.0f;
		float delta;

		if (center[0] < boxMin[0]) {
			delta = center[0] - boxMin[0];
			dmin += delta * delta;
		}
		else if (center[0] > boxMax[0]) {
			delta = boxMax[0] - center[0];
			dmin += delta * delta;
		}

		if (center[1] < boxMin[1]) {
			delta = center[1] - boxMin[1];
			dmin += delta * delta;
		}
		else if (center[1] > boxMax[1]) {
			delta = boxMax[1] - center[1];
			dmin += delta * delta;
		}

		if (center[2] < boxMin[2]) {
			delta = center[2] - boxMin[2];
			dmin += delta * delta;
		}
		else if (center[2] > boxMax[2]) {
			delta = boxMax[2] - center[2];
			dmin += delta * delta;
		}

		return dmin < radius * radius;
	}

	public static bool IsCircleIntersectingRectangle(in Vector2 boxMin, in Vector2 boxMax, in Vector2 center, float radius) {
		float dmin = 0.0f;
		float delta;

		if (center[0] < boxMin[0]) {
			delta = center[0] - boxMin[0];
			dmin += delta * delta;
		}
		else if (center[0] > boxMax[0]) {
			delta = boxMax[0] - center[0];
			dmin += delta * delta;
		}

		if (center[1] < boxMin[1]) {
			delta = center[1] - boxMin[1];
			dmin += delta * delta;
		}
		else if (center[1] > boxMax[1]) {
			delta = boxMax[1] - center[1];
			dmin += delta * delta;
		}

		return dmin < radius * radius;
	}

	public static bool IsBoxIntersectingSphereExtents(in Vector3 boxCenter, in Vector3 boxHalfDiag, in Vector3 center, float radius) {
		float dmin = 0.0f;
		float delta, diff;

		diff = MathF.Abs(center.X - boxCenter.X);
		if (diff > boxHalfDiag.X) {
			delta = diff - boxHalfDiag.X;
			dmin += delta * delta;
		}

		diff = MathF.Abs(center.Y - boxCenter.Y);
		if (diff > boxHalfDiag.Y) {
			delta = diff - boxHalfDiag.Y;
			dmin += delta * delta;
		}

		diff = MathF.Abs(center.Z - boxCenter.Z);
		if (diff > boxHalfDiag.Z) {
			delta = diff - boxHalfDiag.Z;
			dmin += delta * delta;
		}

		return dmin < radius * radius;
	}

	public static bool IsPointInBox(in Vector3 pt, in Vector3 boxMin, in Vector3 boxMax) {
		Assert(boxMin.X <= boxMax.X && boxMin.Y <= boxMax.Y && boxMin.Z <= boxMax.Z);
		return pt.X >= boxMin.X && pt.X <= boxMax.X && pt.Y >= boxMin.Y && pt.Y <= boxMax.Y && pt.Z >= boxMin.Z && pt.Z <= boxMax.Z;
	}

	private static void FindMinMax(float v1, float v2, float v3, out float min, out float max) {
		min = max = v1;
		if (v2 < min) min = v2; else if (v2 > max) max = v2;
		if (v3 < min) min = v3; else if (v3 > max) max = v3;
	}

	// Separating-axis edge tests (triangle edge crossed with an axial axis). Returns false on a separating axis.
	private static bool AxisTestEdgeCrossX(float edgeZ, float edgeY, float absEdgeZ, float absEdgeY, in Vector3 pA, in Vector3 pB, in Vector3 ext, float tol) {
		float distA = edgeZ * pA.Y - edgeY * pA.Z;
		float distB = edgeZ * pB.Y - edgeY * pB.Z;
		float distBox = absEdgeZ * ext.Y + absEdgeY * ext.Z;
		if (distA < distB) {
			if ((distA > (distBox + tol)) || (distB < -(distBox + tol))) return false;
		}
		else {
			if ((distB > (distBox + tol)) || (distA < -(distBox + tol))) return false;
		}
		return true;
	}

	private static bool AxisTestEdgeCrossY(float edgeZ, float edgeX, float absEdgeZ, float absEdgeX, in Vector3 pA, in Vector3 pB, in Vector3 ext, float tol) {
		float distA = -edgeZ * pA.X + edgeX * pA.Z;
		float distB = -edgeZ * pB.X + edgeX * pB.Z;
		float distBox = absEdgeZ * ext.X + absEdgeX * ext.Z;
		if (distA < distB) {
			if ((distA > (distBox + tol)) || (distB < -(distBox + tol))) return false;
		}
		else {
			if ((distB > (distBox + tol)) || (distA < -(distBox + tol))) return false;
		}
		return true;
	}

	private static bool AxisTestEdgeCrossZ(float edgeY, float edgeX, float absEdgeY, float absEdgeX, in Vector3 pA, in Vector3 pB, in Vector3 ext, float tol) {
		float distA = edgeY * pA.X - edgeX * pA.Y;
		float distB = edgeY * pB.X - edgeX * pB.Y;
		float distBox = absEdgeY * ext.X + absEdgeX * ext.Y;
		if (distB < distA) {
			if ((distB > (distBox + tol)) || (distA < -(distBox + tol))) return false;
		}
		else {
			if ((distA > (distBox + tol)) || (distB < -(distBox + tol))) return false;
		}
		return true;
	}

	// Separating-Axis Theorem test for an AABB (center/extents) vs. a triangle (with its precomputed plane).
	public static bool IsBoxIntersectingTriangle(in Vector3 boxCenter, in Vector3 boxExtents, in Vector3 v1, in Vector3 v2, in Vector3 v3, in CollisionPlane plane, float tolerance) {
		// Test the axial planes (x,y,z) against the min/max of the triangle.
		Vector3 p1, p2, p3;

		p1.X = v1.X - boxCenter.X; p2.X = v2.X - boxCenter.X; p3.X = v3.X - boxCenter.X;
		FindMinMax(p1.X, p2.X, p3.X, out float min, out float max);
		if ((min > (boxExtents.X + tolerance)) || (max < -(boxExtents.X + tolerance))) return false;

		p1.Y = v1.Y - boxCenter.Y; p2.Y = v2.Y - boxCenter.Y; p3.Y = v3.Y - boxCenter.Y;
		FindMinMax(p1.Y, p2.Y, p3.Y, out min, out max);
		if ((min > (boxExtents.Y + tolerance)) || (max < -(boxExtents.Y + tolerance))) return false;

		p1.Z = v1.Z - boxCenter.Z; p2.Z = v2.Z - boxCenter.Z; p3.Z = v3.Z - boxCenter.Z;
		FindMinMax(p1.Z, p2.Z, p3.Z, out min, out max);
		if ((min > (boxExtents.Z + tolerance)) || (max < -(boxExtents.Z + tolerance))) return false;

		// Test the 9 edge cases.
		Vector3 edge, absEdge;

		// edge 0 (p2 - p1)
		edge = p2 - p1;
		absEdge.Y = MathF.Abs(edge.Y); absEdge.Z = MathF.Abs(edge.Z);
		if (!AxisTestEdgeCrossX(edge.Z, edge.Y, absEdge.Z, absEdge.Y, p1, p3, boxExtents, tolerance)) return false;
		absEdge.X = MathF.Abs(edge.X);
		if (!AxisTestEdgeCrossY(edge.Z, edge.X, absEdge.Z, absEdge.X, p1, p3, boxExtents, tolerance)) return false;
		if (!AxisTestEdgeCrossZ(edge.Y, edge.X, absEdge.Y, absEdge.X, p2, p3, boxExtents, tolerance)) return false;

		// edge 1 (p3 - p2)
		edge = p3 - p2;
		absEdge.Y = MathF.Abs(edge.Y); absEdge.Z = MathF.Abs(edge.Z);
		if (!AxisTestEdgeCrossX(edge.Z, edge.Y, absEdge.Z, absEdge.Y, p1, p2, boxExtents, tolerance)) return false;
		absEdge.X = MathF.Abs(edge.X);
		if (!AxisTestEdgeCrossY(edge.Z, edge.X, absEdge.Z, absEdge.X, p1, p2, boxExtents, tolerance)) return false;
		if (!AxisTestEdgeCrossZ(edge.Y, edge.X, absEdge.Y, absEdge.X, p1, p3, boxExtents, tolerance)) return false;

		// edge 2 (p1 - p3)
		edge = p1 - p3;
		absEdge.Y = MathF.Abs(edge.Y); absEdge.Z = MathF.Abs(edge.Z);
		if (!AxisTestEdgeCrossX(edge.Z, edge.Y, absEdge.Z, absEdge.Y, p1, p2, boxExtents, tolerance)) return false;
		absEdge.X = MathF.Abs(edge.X);
		if (!AxisTestEdgeCrossY(edge.Z, edge.X, absEdge.Z, absEdge.X, p1, p2, boxExtents, tolerance)) return false;
		if (!AxisTestEdgeCrossZ(edge.Y, edge.X, absEdge.Y, absEdge.X, p2, p3, boxExtents, tolerance)) return false;

		// Test against the triangle face plane.
		Vector3 vecMin = boxCenter - boxExtents;
		Vector3 vecMax = boxCenter + boxExtents;
		if (MathLib.BoxOnPlaneSide(vecMin, vecMax, plane) != 3) return false;

		return true;
	}

	static void ComputeCenterMatrix(in Vector3 origin, in QAngle angles, in Vector3 mins, in Vector3 maxs, out Matrix3x4 matrix) {
		MathLib.VectorAdd(mins, maxs, out Vector3 centroid);
		centroid *= 0.5f;
		MathLib.AngleMatrix(angles, out matrix);

		MathLib.VectorRotate(centroid, matrix, out Vector3 worldCentroid);
		worldCentroid += origin;
		MathLib.MatrixSetColumn(worldCentroid, 3, ref matrix);
	}

	static void ComputeCenterIMatrix(in Vector3 origin, in QAngle angles, in Vector3 mins, in Vector3 maxs, out Matrix3x4 matrix) {
		MathLib.VectorAdd(mins, maxs, out Vector3 centroid);
		centroid *= -0.5f;
		MathLib.AngleIMatrix(angles, out matrix);

		MathLib.VectorRotate(origin, matrix, out Vector3 localOrigin);
		centroid -= localOrigin;
		MathLib.MatrixSetColumn(centroid, 3, ref matrix);
	}

	static void ComputeAbsMatrix(in Matrix3x4 input, out Matrix3x4 output) {
		output = default;
		output[0, 0] = MathF.Abs(input[0, 0]);
		output[0, 1] = MathF.Abs(input[0, 1]);
		output[0, 2] = MathF.Abs(input[0, 2]);
		output[1, 0] = MathF.Abs(input[1, 0]);
		output[1, 1] = MathF.Abs(input[1, 1]);
		output[1, 2] = MathF.Abs(input[1, 2]);
		output[2, 0] = MathF.Abs(input[2, 0]);
		output[2, 1] = MathF.Abs(input[2, 1]);
		output[2, 2] = MathF.Abs(input[2, 2]);
	}

	static bool ComputeSeparatingPlane(in Matrix3x4 worldToBox1, in Matrix3x4 box2ToWorld, in Vector3 box1Size, in Vector3 box2Size, float tolerance, out CollisionPlane plane) {
		plane = default;

		MathLib.ConcatTransforms(worldToBox1, box2ToWorld, out Matrix3x4 box2ToBox1);
		MathLib.MatrixGetColumn(box2ToBox1, 3, out Vector3 box2Origin);

		ComputeAbsMatrix(box2ToBox1, out Matrix3x4 absBox2ToBox1);

		Vector3 tmp;
		float boxProjectionSum;
		float originProjection;

		boxProjectionSum = box1Size.X + MathLib.MatrixRowDotProduct(absBox2ToBox1, 0, box2Size);
		originProjection = FloatMakePositive(box2Origin.X) + tolerance;
		if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
			plane.Normal = new(worldToBox1[0, 0], worldToBox1[0, 1], worldToBox1[0, 2]);
			return true;
		}

		boxProjectionSum = box1Size.Y + MathLib.MatrixRowDotProduct(absBox2ToBox1, 1, box2Size);
		originProjection = FloatMakePositive(box2Origin.Y) + tolerance;
		if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
			plane.Normal = new(worldToBox1[1, 0], worldToBox1[1, 1], worldToBox1[1, 2]);
			return true;
		}

		boxProjectionSum = box1Size.Z + MathLib.MatrixRowDotProduct(absBox2ToBox1, 2, box2Size);
		originProjection = FloatMakePositive(box2Origin.Z) + tolerance;
		if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
			plane.Normal = new(worldToBox1[2, 0], worldToBox1[2, 1], worldToBox1[2, 2]);
			return true;
		}

		boxProjectionSum = box2Size.X + MathLib.MatrixColumnDotProduct(absBox2ToBox1, 0, box1Size);
		originProjection = FloatMakePositive(MathLib.MatrixColumnDotProduct(box2ToBox1, 0, box2Origin)) + tolerance;
		if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
			MathLib.MatrixGetColumn(box2ToWorld, 0, out plane.Normal);
			return true;
		}

		boxProjectionSum = box2Size.Y + MathLib.MatrixColumnDotProduct(absBox2ToBox1, 1, box1Size);
		originProjection = FloatMakePositive(MathLib.MatrixColumnDotProduct(box2ToBox1, 1, box2Origin)) + tolerance;
		if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
			MathLib.MatrixGetColumn(box2ToWorld, 1, out plane.Normal);
			return true;
		}

		boxProjectionSum = box2Size.Z + MathLib.MatrixColumnDotProduct(absBox2ToBox1, 2, box1Size);
		originProjection = FloatMakePositive(MathLib.MatrixColumnDotProduct(box2ToBox1, 2, box2Origin)) + tolerance;
		if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
			MathLib.MatrixGetColumn(box2ToWorld, 2, out plane.Normal);
			return true;
		}

		if (absBox2ToBox1[0, 0] < 1.0f - 1e-3f) {
			boxProjectionSum =
				box1Size.Y * absBox2ToBox1[2, 0] + box1Size.Z * absBox2ToBox1[1, 0] +
				box2Size.Y * absBox2ToBox1[0, 2] + box2Size.Z * absBox2ToBox1[0, 1];
			originProjection = FloatMakePositive(-box2Origin.Y * box2ToBox1[2, 0] + box2Origin.Z * box2ToBox1[1, 0]) + tolerance;
			if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
				MathLib.MatrixGetColumn(box2ToWorld, 0, out tmp);
				MathLib.CrossProduct(new(worldToBox1[0, 0], worldToBox1[0, 1], worldToBox1[0, 2]), tmp, out plane.Normal);
				return true;
			}
		}

		if (absBox2ToBox1[0, 1] < 1.0f - 1e-3f) {
			boxProjectionSum =
				box1Size.Y * absBox2ToBox1[2, 1] + box1Size.Z * absBox2ToBox1[1, 1] +
				box2Size.X * absBox2ToBox1[0, 2] + box2Size.Z * absBox2ToBox1[0, 0];
			originProjection = FloatMakePositive(-box2Origin.Y * box2ToBox1[2, 1] + box2Origin.Z * box2ToBox1[1, 1]) + tolerance;
			if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
				MathLib.MatrixGetColumn(box2ToWorld, 1, out tmp);
				MathLib.CrossProduct(new(worldToBox1[0, 0], worldToBox1[0, 1], worldToBox1[0, 2]), tmp, out plane.Normal);
				return true;
			}
		}

		if (absBox2ToBox1[0, 2] < 1.0f - 1e-3f) {
			boxProjectionSum =
				box1Size.Y * absBox2ToBox1[2, 2] + box1Size.Z * absBox2ToBox1[1, 2] +
				box2Size.X * absBox2ToBox1[0, 1] + box2Size.Y * absBox2ToBox1[0, 0];
			originProjection = FloatMakePositive(-box2Origin.Y * box2ToBox1[2, 2] + box2Origin.Z * box2ToBox1[1, 2]) + tolerance;
			if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
				MathLib.MatrixGetColumn(box2ToWorld, 2, out tmp);
				MathLib.CrossProduct(new(worldToBox1[0, 0], worldToBox1[0, 1], worldToBox1[0, 2]), tmp, out plane.Normal);
				return true;
			}
		}

		if (absBox2ToBox1[1, 0] < 1.0f - 1e-3f) {
			boxProjectionSum =
				box1Size.X * absBox2ToBox1[2, 0] + box1Size.Z * absBox2ToBox1[0, 0] +
				box2Size.Y * absBox2ToBox1[1, 2] + box2Size.Z * absBox2ToBox1[1, 1];
			originProjection = FloatMakePositive(box2Origin.X * box2ToBox1[2, 0] - box2Origin.Z * box2ToBox1[0, 0]) + tolerance;
			if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
				MathLib.MatrixGetColumn(box2ToWorld, 0, out tmp);
				MathLib.CrossProduct(new(worldToBox1[1, 0], worldToBox1[1, 1], worldToBox1[1, 2]), tmp, out plane.Normal);
				return true;
			}
		}

		if (absBox2ToBox1[1, 1] < 1.0f - 1e-3f) {
			boxProjectionSum =
				box1Size.X * absBox2ToBox1[2, 1] + box1Size.Z * absBox2ToBox1[0, 1] +
				box2Size.X * absBox2ToBox1[1, 2] + box2Size.Z * absBox2ToBox1[1, 0];
			originProjection = FloatMakePositive(box2Origin.X * box2ToBox1[2, 1] - box2Origin.Z * box2ToBox1[0, 1]) + tolerance;
			if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
				MathLib.MatrixGetColumn(box2ToWorld, 1, out tmp);
				MathLib.CrossProduct(new(worldToBox1[1, 0], worldToBox1[1, 1], worldToBox1[1, 2]), tmp, out plane.Normal);
				return true;
			}
		}

		if (absBox2ToBox1[1, 2] < 1.0f - 1e-3f) {
			boxProjectionSum =
				box1Size.X * absBox2ToBox1[2, 2] + box1Size.Z * absBox2ToBox1[0, 2] +
				box2Size.X * absBox2ToBox1[1, 1] + box2Size.Y * absBox2ToBox1[1, 0];
			originProjection = FloatMakePositive(box2Origin.X * box2ToBox1[2, 2] - box2Origin.Z * box2ToBox1[0, 2]) + tolerance;
			if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
				MathLib.MatrixGetColumn(box2ToWorld, 2, out tmp);
				MathLib.CrossProduct(new(worldToBox1[1, 0], worldToBox1[1, 1], worldToBox1[1, 2]), tmp, out plane.Normal);
				return true;
			}
		}

		if (absBox2ToBox1[2, 0] < 1.0f - 1e-3f) {
			boxProjectionSum =
				box1Size.X * absBox2ToBox1[1, 0] + box1Size.Y * absBox2ToBox1[0, 0] +
				box2Size.Y * absBox2ToBox1[2, 2] + box2Size.Z * absBox2ToBox1[2, 1];
			originProjection = FloatMakePositive(-box2Origin.X * box2ToBox1[1, 0] + box2Origin.Y * box2ToBox1[0, 0]) + tolerance;
			if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
				MathLib.MatrixGetColumn(box2ToWorld, 0, out tmp);
				MathLib.CrossProduct(new(worldToBox1[2, 0], worldToBox1[2, 1], worldToBox1[2, 2]), tmp, out plane.Normal);
				return true;
			}
		}

		if (absBox2ToBox1[2, 1] < 1.0f - 1e-3f) {
			boxProjectionSum =
				box1Size.X * absBox2ToBox1[1, 1] + box1Size.Y * absBox2ToBox1[0, 1] +
				box2Size.X * absBox2ToBox1[2, 2] + box2Size.Z * absBox2ToBox1[2, 0];
			originProjection = FloatMakePositive(-box2Origin.X * box2ToBox1[1, 1] + box2Origin.Y * box2ToBox1[0, 1]) + tolerance;
			if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
				MathLib.MatrixGetColumn(box2ToWorld, 1, out tmp);
				MathLib.CrossProduct(new(worldToBox1[2, 0], worldToBox1[2, 1], worldToBox1[2, 2]), tmp, out plane.Normal);
				return true;
			}
		}

		if (absBox2ToBox1[2, 2] < 1.0f - 1e-3f) {
			boxProjectionSum =
				box1Size.X * absBox2ToBox1[1, 2] + box1Size.Y * absBox2ToBox1[0, 2] +
				box2Size.X * absBox2ToBox1[2, 1] + box2Size.Y * absBox2ToBox1[2, 0];
			originProjection = FloatMakePositive(-box2Origin.X * box2ToBox1[1, 2] + box2Origin.Y * box2ToBox1[0, 2]) + tolerance;
			if (originProjection.FloatBits() > boxProjectionSum.FloatBits()) {
				MathLib.MatrixGetColumn(box2ToWorld, 2, out tmp);
				MathLib.CrossProduct(new(worldToBox1[2, 0], worldToBox1[2, 1], worldToBox1[2, 2]), tmp, out plane.Normal);
				return true;
			}
		}
		return false;
	}

	public static bool ComputeSeparatingPlane(in Vector3 org1, in QAngle angles1, in Vector3 min1, in Vector3 max1,
		in Vector3 org2, in QAngle angles2, in Vector3 min2, in Vector3 max2,
		float tolerance, out CollisionPlane plane) {
		ComputeCenterIMatrix(org1, angles1, min1, max1, out Matrix3x4 worldToBox1);
		ComputeCenterMatrix(org2, angles2, min2, max2, out Matrix3x4 box2ToWorld);

		MathLib.VectorSubtract(max1, min1, out Vector3 box1Size);
		MathLib.VectorSubtract(max2, min2, out Vector3 box2Size);
		box1Size *= 0.5f;
		box2Size *= 0.5f;

		return ComputeSeparatingPlane(worldToBox1, box2ToWorld, box1Size, box2Size, tolerance, out plane);
	}

	public static bool IsOBBIntersectingOBB(in Vector3 vecOrigin1, in QAngle vecAngles1, in Vector3 boxMin1, in Vector3 boxMax1,
		in Vector3 vecOrigin2, in QAngle vecAngles2, in Vector3 boxMin2, in Vector3 boxMax2, float flTolerance = 0.0f) {
		// OBB test...
		bool bFoundPlane = ComputeSeparatingPlane(vecOrigin1, vecAngles1, boxMin1, boxMax1,
			vecOrigin2, vecAngles2, boxMin2, boxMax2, flTolerance, out _);
		return (bFoundPlane == false);
	}
}
