using Source.Common;
using Source.Common.Formats.BSP;

using System.Numerics;
using System.Runtime.CompilerServices;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public static class BuiltinParticleConstraints
{
	public static void AddBuiltInParticleConstraints(ParticleSystemMgr mgr) {
		mgr.AddParticleOperator(ParticleFunctionType.Constraint, C_OP_ConstrainDistance.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Constraint, C_OP_WorldTraceConstraint.Definition);
	}
}

public static class CollisionMode
{
	public const int PER_PARTICLE_TRACE = 0;
	public const int PER_FRAME_PLANESET = 1;
	public const int INITIAL_TRACE_DOWN = 2;
	public const int USE_NEAREST_TRACE = 3;
}

[InlineArray(WorldCollideContextData.MAX_PLANES)]
public struct WorldCollidePlaneVectors
{
	Vector3 element;
}

[InlineArray(WorldCollideContextData.MAX_PLANES)]
public struct WorldCollidePlaneFlags
{
	bool element;
}

public struct WorldCollideContextData
{
	public const int MAX_PLANES = 41;

	public WorldCollidePlaneVectors TraceStartPnt;
	public WorldCollidePlaneVectors TraceEndPnt;
	public WorldCollidePlaneVectors PointOnPlane;
	public WorldCollidePlaneVectors PlaneNormal;
	public int ActivePlanes;
	public int NumFixedPlanes;
	public float LastUpdateTime;
	public Vector3 LastUpdateOrigin;
	public WorldCollidePlaneFlags PlaneActive;

	public void SetBaseTrace(ParticleSystemMgr mgr, int index, in Vector3 rayStart, in Vector3 traceDir, int collisionGroup, bool keepMisses) {
		GameTrace tr = default;
		Vector3 rayEnd = rayStart + traceDir;
		mgr.Query().TraceLine(rayStart, rayEnd, Mask.Solid, null, collisionGroup, ref tr);
		if (tr.Fraction < 1.0f) {
			PlaneActive[index] = true;
			PointOnPlane[index] = rayStart + tr.Fraction * traceDir;
			PlaneNormal[index] = tr.Plane.Normal;
			TraceStartPnt[index] = rayStart;
			TraceEndPnt[index] = rayEnd;
		}
		else if (keepMisses) {
			PlaneNormal[index] = Vector3.Zero;
			TraceStartPnt[index] = rayStart;
			TraceEndPnt[index] = rayEnd;
			PlaneActive[index] = true;
		}
		else
			PlaneActive[index] = false;
	}

	public void CalculatePlanes(ParticleCollection particles, int collisionMode, int collisionGroup, in Vector3 cpOffset, float distanceTolerance) {
		if (ActivePlanes != 0 && collisionMode == CollisionMode.INITIAL_TRACE_DOWN)
			return;

		Vector3 rayStart = particles.GetControlPointAtCurrentTime(0) + cpOffset;
		if (LastUpdateTime > 0.0f && (rayStart - LastUpdateOrigin).LengthSquared() < distanceTolerance * distanceTolerance)
			return;

		ParticleSystemMgr mgr = particles.Mgr!;
		LastUpdateOrigin = rayStart;
		ActivePlanes = 0;

		int indexOut = 0;
		switch (collisionMode) {
			case CollisionMode.INITIAL_TRACE_DOWN:
				SetBaseTrace(mgr, 0, rayStart, new Vector3(0, 0, -1000.0f), collisionGroup, false);
				ActivePlanes = 1;
				NumFixedPlanes = 1;
				return;
			case CollisionMode.PER_FRAME_PLANESET:
			case CollisionMode.USE_NEAREST_TRACE:
				bool keepMisses = collisionMode == CollisionMode.USE_NEAREST_TRACE;
				for (int i = -1; i <= 1; i++)
					for (int j = -1; j <= 1; j++)
						for (int k = -1; k <= 1; k++) {
							if (i != 0 || j != 0 || k != 0)
								SetBaseTrace(mgr, indexOut++, rayStart, new Vector3(i * 1000.0f, j * 1000.0f, k * 1000.0f), collisionGroup, keepMisses);
						}
				break;
			default:
				return;
		}

		NumFixedPlanes = indexOut;
		ActivePlanes = indexOut;
		if (indexOut > MAX_PLANES) {
			Warning($"C_OP_WorldCollideConstraint: Insane active plane count {indexOut}! Fixing..\n");
			ActivePlanes = 0;
		}
	}
}

internal static class ParticleConstraintUtil
{
	public const float FLT_EPSILON = 1.1920929e-07f;

	public static Vector3 Load(Span<float> memory, int index, int lane) => new(memory[index + lane], memory[index + 4 + lane], memory[index + 8 + lane]);

	public static void Store(Span<float> memory, int index, int lane, in Vector3 value) {
		memory[index + lane] = value.X;
		memory[index + 4 + lane] = value.Y;
		memory[index + 8 + lane] = value.Z;
	}

	public static float RSqrt(float x) => 1.0f / MathF.Sqrt(x);

	public static float RSqrtSaturate(float x) => RSqrt(x == 0.0f ? FLT_EPSILON : x);

	public static float MaxPs(float a, float b) => a > b ? a : b;
	public static float MinPs(float a, float b) => a < b ? a : b;

	public static float Blend(uint mask, float a, float b)
		=> BitConverter.UInt32BitsToSingle((BitConverter.SingleToUInt32Bits(a) & mask) | (BitConverter.SingleToUInt32Bits(b) & ~mask));

	public static Vector3 Blend(uint mask, in Vector3 a, in Vector3 b) => new(Blend(mask, a.X, b.X), Blend(mask, a.Y, b.Y), Blend(mask, a.Z, b.Z));

	public static float DistSqrToLineSegment(in Vector3 p, in Vector3 lineA, in Vector3 lineB) {
		Vector3 aToB = lineB - lineA;
		Vector3 aToP = p - lineA;
		float t = Vector3.Dot(aToB, aToP) * (1.0f / Vector3.Dot(aToB, aToB));
		t = MaxPs(MinPs(t, 1.0f), 0.0f);
		Vector3 closest = aToB * t - aToP;
		return closest.LengthSquared();
	}
}

public sealed class C_OP_ConstrainDistance : ParticleOperatorInstance
{
	public float MinDistance;
	public float MaxDistance;
	public int ControlPointNumber;
	public Vector3 CenterOffset;
	public bool GlobalCenter;

	public static readonly ParticleOperatorDefinition<C_OP_ConstrainDistance> Definition = new("Constrain distance to control point", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_ConstrainDistance>()
			.Float("minimum distance", "0", (o, v) => o.MinDistance = v)
			.Float("maximum distance", "100", (o, v) => o.MaxDistance = v)
			.Int("control point number", "0", (o, v) => o.ControlPointNumber = v)
			.Vector3("offset of center", "0 0 0", (o, v) => o.CenterOffset = v)
			.Bool("global center point", "0", (o, v) => o.GlobalCenter = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;

	public override bool EnforceConstraint(int startBlock, int numBlocks, ParticleCollection particles, Span<byte> context, int activeParticles) {
		Vector3 center;
		if (GlobalCenter)
			center = CenterOffset;
		else {
			particles.GetControlPointAtTime(ControlPointNumber, particles.CurTime, out center);
			center += particles.TransformAxis(CenterOffset, true, ControlPointNumber);
		}

		float minDist2 = MinDistance * MinDistance;
		float maxDist2 = MaxDistance * MaxDistance;
		Span<float> memory = particles.AttributeMemory;
		int xyz = particles.GetAttributeOffset(PARTICLE_ATTRIBUTE_XYZ, out int stride) + stride * startBlock;
		bool changed = false;
		for (int block = 0; block < numBlocks; block++, xyz += stride) {
			for (int lane = 0; lane < 4; lane++) {
				Vector3 pts = ParticleConstraintUtil.Load(memory, xyz, lane) - center;
				float distSq = pts.LengthSquared();
				bool tooFar = distSq > maxDist2;
				bool tooClose = distSq < minDist2;
				if (!tooFar && !tooClose)
					continue;
				changed = true;
				pts *= ParticleConstraintUtil.RSqrtSaturate(distSq);
				ParticleConstraintUtil.Store(memory, xyz, lane, pts * (tooClose ? MinDistance : MaxDistance) + center);
			}
		}
		return changed;
	}
}

public sealed class C_OP_WorldTraceConstraint : ParticleOperatorInstance
{
	public Vector3 CpOffset;
	public int CollisionModeIndex;
	public float BounceAmount;
	public float SlideAmount;
	public float RadiusScale;
	public float CpMovementTolerance;
	public float TraceTolerance;
	public float MinSpeed;
	public bool KillOnContact;
	public bool ConfirmCollision;
	public string CollisionGroupName = "NONE";
	public int CollisionGroupNumber;
	public bool BrushOnly;

	public static readonly ParticleOperatorDefinition<C_OP_WorldTraceConstraint> Definition = new("Collision via traces", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_WorldTraceConstraint>()
			.Int("collision mode", "0", (o, v) => o.CollisionModeIndex = v)
			.Float("amount of bounce", "0", (o, v) => o.BounceAmount = v)
			.Float("amount of slide", "0", (o, v) => o.SlideAmount = v)
			.Float("radius scale", "1", (o, v) => o.RadiusScale = v)
			.Bool("brush only", "0", (o, v) => o.BrushOnly = v)
			.String("collision group", "NONE", 128, (o, v) => o.CollisionGroupName = v)
			.Vector3("control point offset for fast collisions", "0 0 0", (o, v) => o.CpOffset = v)
			.Float("control point movement distance tolerance", "5", (o, v) => o.CpMovementTolerance = v)
			.Bool("kill particle on collision", "0", (o, v) => o.KillOnContact = v)
			.Float("minimum speed to kill on collision", "-1", (o, v) => o.MinSpeed = v)
			.Bool("Confirm Collision", "0", (o, v) => o.ConfirmCollision = v)
			.Float("trace accuracy tolerance", "24", (o, v) => o.TraceTolerance = v));

	[InlineArray(4)]
	struct LaneVectors
	{
		Vector3 element;
	}

	struct ISectData
	{
		public Vector4 ISectT;
		public Vector4 LeftOverT;
		public LaneVectors Normals;
	}

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_RADIUS_MASK;
	public override ulong GetReadControlPointMask() => 1UL;
	public override bool IsFinalConstraint() => BounceAmount != 0.0f || SlideAmount != 0.0f;

	public override void InitParams(ParticleSystemDefinition def, Datamodel.Element element) {
		CollisionGroupNumber = def.Mgr.Query().GetCollisionGroupFromName(CollisionGroupName);
		CollisionModeIndex = Math.Clamp(CollisionModeIndex, 0, 3);
	}

	static void WorldIntersectTNew(ParticleSystemMgr mgr, ReadOnlySpan<Vector3> startPnt, ReadOnlySpan<Vector3> endPnt, int collisionGroup, Mask contentsMask, ref ISectData isect,
		int collisionMode, ref WorldCollideContextData ctx, ReadOnlySpan<bool> particleValid, float tolerance) {
		isect = default;
		isect.ISectT = new Vector4(2.0f);
		if (collisionMode == CollisionMode.USE_NEAREST_TRACE) {
			Span<Vector3> pointOnPlane = stackalloc Vector3[4];
			Span<Vector3> planeNormal = stackalloc Vector3[4];
			Span<float> closestDist = stackalloc float[4];
			closestDist.Fill(float.MaxValue);
			for (int i = 0; i < ctx.ActivePlanes; i++) {
				if (!ctx.PlaneActive[i])
					continue;
				Vector3 traceStart = ctx.TraceStartPnt[i];
				Vector3 traceEnd = ctx.TraceEndPnt[i];
				for (int lane = 0; lane < 4; lane++) {
					float trial = ParticleConstraintUtil.MaxPs(
						ParticleConstraintUtil.DistSqrToLineSegment(startPnt[lane], traceStart, traceEnd),
						ParticleConstraintUtil.DistSqrToLineSegment(endPnt[lane], traceStart, traceEnd));
					bool nearest = trial <= closestDist[lane];
					uint closestBits = BitConverter.SingleToUInt32Bits(closestDist[lane]);
					uint nearestMask = nearest ? 0xFFFFFFFFu : 0u;
					closestDist[lane] = BitConverter.UInt32BitsToSingle((BitConverter.SingleToUInt32Bits(trial) & closestBits) | (nearestMask & ~closestBits));
					if (nearest) {
						pointOnPlane[lane] = ctx.PointOnPlane[i];
						planeNormal[lane] = ctx.PlaneNormal[i];
					}
				}
			}

			for (int lane = 0; lane < 4; lane++) {
				if (!particleValid[lane] || !(closestDist[lane] > tolerance))
					continue;
				Vector3 start = startPnt[lane];
				Vector3 delta = endPnt[lane] - start;
				float traceScale = MathF.Max(5.0f, 300.0f / (delta.Length() + 0.01f));
				Vector3 end = start + delta * traceScale;
				GameTrace tr = default;
				mgr.Query().TraceLine(start, end, contentsMask, null, collisionGroup, ref tr);
				if (tr.Fraction < 1.0f) {
					pointOnPlane[lane] = start + tr.Fraction * (end - start);
					planeNormal[lane] = tr.Plane.Normal;
				}
				else
					planeNormal[lane] = Vector3.Zero;
			}

			for (int lane = 0; lane < 4; lane++) {
				float startDist = Vector3.Dot(startPnt[lane] - pointOnPlane[lane], planeNormal[lane]);
				float endDist = Vector3.Dot(endPnt[lane] - pointOnPlane[lane], planeNormal[lane]);
				if (!(startDist >= 0.0f && endDist < 0.0f && particleValid[lane]))
					continue;
				float t = startDist / (startDist - endDist);
				if (t < isect.ISectT[lane]) {
					isect.ISectT[lane] = t;
					isect.Normals[lane] = planeNormal[lane];
				}
			}
		}
		isect.LeftOverT = Vector4.Max(Vector4.Zero, Vector4.One - isect.ISectT);
	}

	static void WorldIntersectT(ParticleSystemMgr mgr, ReadOnlySpan<Vector3> startPnt, ReadOnlySpan<Vector3> endPnt, int collisionGroup, Mask contentsMask, ref ISectData isect,
		bool hasCtx, ref WorldCollideContextData ctx, ReadOnlySpan<bool> particleValid) {
		isect = default;
		if (hasCtx) {
			isect.ISectT = new Vector4(2.0f);
			for (int i = 0; i < ctx.ActivePlanes; i++) {
				if (!ctx.PlaneActive[i])
					continue;
				Vector3 point = ctx.PointOnPlane[i];
				Vector3 normal = ctx.PlaneNormal[i];
				for (int lane = 0; lane < 4; lane++) {
					float startDist = Vector3.Dot(startPnt[lane] - point, normal);
					float endDist = Vector3.Dot(endPnt[lane] - point, normal);
					if (!(startDist >= 0.0f && endDist < 0.0f))
						continue;
					float t = startDist / (startDist - endDist);
					if (t < isect.ISectT[lane]) {
						isect.ISectT[lane] = t;
						isect.Normals[lane] = normal;
					}
				}
			}
			isect.LeftOverT = Vector4.Max(Vector4.Zero, Vector4.One - isect.ISectT);
			return;
		}

		for (int lane = 0; lane < 4; lane++) {
			if (!particleValid[lane])
				continue;
			GameTrace tr = default;
			mgr.Query().TraceLine(startPnt[lane], endPnt[lane], contentsMask, null, collisionGroup, ref tr);
			isect.ISectT[lane] = tr.Fraction;
			isect.LeftOverT[lane] = tr.StartSolid ? 0.0f : 1.0f - tr.Fraction;
			isect.Normals[lane] = tr.Plane.Normal;
		}
	}

	public override bool EnforceConstraint(int startBlock, int numBlocks, ParticleCollection particles, Span<byte> context, int activeParticles) {
		ParticleSystemMgr mgr = particles.Mgr!;
		bool cached = CollisionModeIndex == CollisionMode.USE_NEAREST_TRACE;
		bool useLifetime = KillOnContact || MinSpeed > 0.0f;
		float minSpeed = particles.Dt * MinSpeed;
		bool bouncingOrSliding = BounceAmount != 0.0f || SlideAmount != 0.0f;
		Mask contentsMask = BrushOnly ? Mask.SolidBrushOnly : Mask.Solid;

		StrongBox<WorldCollideContextData>? ctxBox = null;
		if (CollisionModeIndex >= CollisionMode.PER_FRAME_PLANESET) {
			StrongBox<WorldCollideContextData>?[] cache = (particles.Parent ?? particles).CollisionCacheData;
			ctxBox = cache[CollisionModeIndex];
			if (ctxBox == null) {
				ctxBox = new StrongBox<WorldCollideContextData>();
				ctxBox.Value.ActivePlanes = 0;
				ctxBox.Value.LastUpdateTime = -1.0f;
				cache[CollisionModeIndex] = ctxBox;
			}
			if (ctxBox.Value.LastUpdateTime != particles.CurTime) {
				ctxBox.Value.CalculatePlanes(particles, CollisionModeIndex, CollisionGroupNumber, CpOffset, CpMovementTolerance);
				ctxBox.Value.LastUpdateTime = particles.CurTime;
			}
		}
		ref WorldCollideContextData ctx = ref ctxBox != null ? ref ctxBox.Value : ref Unsafe.NullRef<WorldCollideContextData>();

		float tolerance = TraceTolerance * TraceTolerance;
		Span<float> memory = particles.AttributeMemory;
		int xyz = particles.GetAttributeOffset(PARTICLE_ATTRIBUTE_XYZ, out int xyzStride) + xyzStride * startBlock;
		int prevXyz = particles.GetAttributeOffset(PARTICLE_ATTRIBUTE_PREV_XYZ, out int prevStride) + prevStride * startBlock;
		int radius = particles.GetAttributeOffset(PARTICLE_ATTRIBUTE_RADIUS, out int radiusStride) + radiusStride * startBlock;
		int lifetimeStride = 0;
		int lifetime = useLifetime ? particles.GetAttributeOffset(PARTICLE_ATTRIBUTE_LIFE_DURATION, out lifetimeStride) + lifetimeStride * startBlock : 0;

		Span<Vector3> prevPnt = stackalloc Vector3[4];
		Span<Vector3> endPnt = stackalloc Vector3[4];
		Span<Vector3> delta = stackalloc Vector3[4];
		Span<Vector3> deltaNormalized = stackalloc Vector3[4];
		Span<float> len2 = stackalloc float[4];
		Span<bool> valid = stackalloc bool[4];
		Span<uint> didHit = stackalloc uint[4];
		ISectData isect = default;
		bool changed = false;

		for (int remaining = numBlocks; remaining > 0; remaining--, xyz += xyzStride, prevXyz += prevStride, radius += radiusStride, lifetime += lifetimeStride) {
			for (int lane = 0; lane < 4; lane++) {
				Vector3 pos = ParticleConstraintUtil.Load(memory, xyz, lane);
				prevPnt[lane] = ParticleConstraintUtil.Load(memory, prevXyz, lane);
				delta[lane] = pos - prevPnt[lane];
				len2[lane] = delta[lane].LengthSquared();
				float rs = len2[lane] <= 0.0f ? 0.0f : ParticleConstraintUtil.RSqrt(len2[lane]);
				deltaNormalized[lane] = delta[lane] * rs;
				endPnt[lane] = pos + deltaNormalized[lane] * (memory[radius + lane] * RadiusScale);
			}

			int particlesInChunk = remaining > 1 ? 4 : (activeParticles & 3);
			if (particlesInChunk == 0)
				particlesInChunk = 4;
			for (int lane = 0; lane < 4; lane++)
				valid[lane] = lane < particlesInChunk;

			if (cached)
				WorldIntersectTNew(mgr, prevPnt, endPnt, CollisionGroupNumber, contentsMask, ref isect, CollisionModeIndex, ref ctx, valid, tolerance);
			else
				WorldIntersectT(mgr, prevPnt, endPnt, CollisionGroupNumber, contentsMask, ref isect, ctxBox != null, ref ctx, valid);

			bool anyHit = false;
			for (int lane = 0; lane < 4; lane++) {
				didHit[lane] = len2[lane] > 0.0f && isect.ISectT[lane] < 1.0f ? 0xFFFFFFFFu : 0u;
				anyHit |= didHit[lane] != 0;
			}
			if (!anyHit)
				continue;

			changed = true;
			if (ConfirmCollision) {
				for (int lane = 0; lane < 4; lane++) {
					if (didHit[lane] == 0)
						continue;
					GameTrace tr = default;
					mgr.Query().TraceLine(endPnt[lane], endPnt[lane], contentsMask, null, CollisionGroupNumber, ref tr);
					didHit[lane] = tr.StartSolid ? 0xFFFFFFFFu : 1u;
				}
			}

			if (MinSpeed > 0.0f) {
				for (int lane = 0; lane < 4; lane++) {
					uint tooSlow = MathF.Sqrt(len2[lane]) < minSpeed ? didHit[lane] : 0u;
					memory[lifetime + lane] = BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(memory[lifetime + lane]) & ~tooSlow);
				}
			}

			if (KillOnContact) {
				for (int lane = 0; lane < 4; lane++)
					memory[lifetime + lane] = BitConverter.UInt32BitsToSingle(BitConverter.SingleToUInt32Bits(memory[lifetime + lane]) & ~didHit[lane]);
				continue;
			}

			for (int lane = 0; lane < 4; lane++) {
				Vector3 newPnt = delta[lane] * isect.ISectT[lane] + prevPnt[lane];
				if (bouncingOrSliding) {
					Vector3 normal = isect.Normals[lane];
					float leftOver = isect.LeftOverT[lane];
					Vector3 dn = deltaNormalized[lane];
					Vector3 bouncePart = (dn - normal * (2.0f * Vector3.Dot(dn, normal))) * BounceAmount;
					Vector3 newVel = bouncePart;
					newPnt += bouncePart * leftOver;
					Vector3 slidePart = (delta[lane] - normal * Vector3.Dot(delta[lane], normal)) * SlideAmount;
					newVel += slidePart;
					newPnt += slidePart * leftOver;
					ParticleConstraintUtil.Store(memory, prevXyz, lane, ParticleConstraintUtil.Blend(didHit[lane], newPnt - newVel, prevPnt[lane]));
				}
				ParticleConstraintUtil.Store(memory, xyz, lane, ParticleConstraintUtil.Blend(didHit[lane], newPnt, ParticleConstraintUtil.Load(memory, xyz, lane)));
			}
		}
		return changed;
	}
}
