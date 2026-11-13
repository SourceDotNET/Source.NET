using Datamodel;

using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

using static Source.Particles.ParticleConstants;

using QAngle = Source.Common.Mathematics.QAngle;

namespace Source.Particles;

internal static class ParticleCollisionCache
{
	public static ref WorldCollideContextData Get(ParticleCollection owner) {
		owner.CollisionCache ??= new StrongBox<WorldCollideContextData>(default);
		return ref owner.CollisionCache.Value;
	}

	public static bool IsInvalid(in Vector3 v) => v == ParticleInitUtil.Vec3Invalid;
}

public sealed class C_INIT_LifespanFromVelocity : ParticleOperatorInstance
{
	public Vector3 ComponentScale;
	public float TraceOffset;
	public float MaxTraceLength;
	public float TraceTolerance;
	public int MaxPlanes;
	public string CollisionGroupName = "NONE";
	public bool IncludeWater;
	int CollisionGroupNumber;
	int AllowedPlanes;
	Mask CollisionMask;

	public static readonly ParticleOperatorDefinition<C_INIT_LifespanFromVelocity> Definition = new("Lifetime from Time to Impact", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_LifespanFromVelocity>()
			.String("trace collision group", "NONE", 128, (o, v) => o.CollisionGroupName = v)
			.Float("maximum trace length", "1024", (o, v) => o.MaxTraceLength = v)
			.Float("trace offset", "0", (o, v) => o.TraceOffset = v)
			.Float("trace recycle tolerance", "64", (o, v) => o.TraceTolerance = v)
			.Int("maximum points to cache", "16", (o, v) => o.MaxPlanes = v)
			.Vector3("bias distance", "1 1 1", (o, v) => o.ComponentScale = v)
			.Bool("collide with water", "1", (o, v) => o.IncludeWater = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override int GetRequiredContextBytes() => 0x1f10;
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		CollisionGroupNumber = def.Mgr.Query().GetCollisionGroupFromName(CollisionGroupName);
		AllowedPlanes = Math.Min(WorldCollideContextData.MAX_PLANES, MaxPlanes) - 1;
		CollisionMask = (Mask)(IncludeWater ? 0x600403b : 0x600400b);
	}

	Vector3 TraceAndCache(ParticleCollection particles, ref WorldCollideContextData cache, in Vector3 start, in Vector3 end, float totalTraceDist) {
		GameTrace tr = default;
		particles.Mgr!.Query().TraceLine(start, end, CollisionMask, null, CollisionGroupNumber, ref tr);

		Vector3 normal = tr.Plane.Normal;
		Vector3 pointOnPlane = tr.EndPos;
		if (tr.Fraction == 1.0f)
			normal = ParticleInitUtil.Vec3Invalid;
		if (tr.Fraction <= 1.0f - MaxTraceLength / (totalTraceDist + MathLibConsts.FLT_EPSILON) || tr.StartSolid || totalTraceDist == 0.0f || tr.AllSolid) {
			pointOnPlane = ParticleInitUtil.Vec3Invalid;
			normal = ParticleInitUtil.Vec3Invalid;
		}

		int index = cache.NumFixedPlanes;
		cache.PlaneActive[index] = true;
		cache.PointOnPlane[index] = pointOnPlane;
		cache.PlaneNormal[index] = normal;
		cache.TraceStartPnt[index] = start;
		cache.TraceEndPnt[index] = end;

		cache.NumFixedPlanes++;
		if (cache.NumFixedPlanes > AllowedPlanes)
			cache.NumFixedPlanes = 0;
		cache.ActivePlanes = Math.Min(AllowedPlanes, cache.ActivePlanes + 1);
		return pointOnPlane;
	}

	static float DistSqr(in Vector3 a, in Vector3 b) {
		Vector3 d = a - b;
		return d.Z * d.Z + d.Y * d.Y + d.X * d.X;
	}

	float TrialDistance(in WorldCollideContextData cache, int i, in Vector3 start, in Vector3 end)
		=> MathF.Max(DistSqr(cache.TraceStartPnt[i], start), DistSqr(cache.TraceEndPnt[i], end));

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		ref WorldCollideContextData cache = ref ParticleCollisionCache.Get(particles.Parent ?? particles);
		float tolerance = TraceTolerance * TraceTolerance;
		float totalTraceDist = MaxTraceLength - TraceOffset;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			ref float life = ref particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p);
			if (!(life >= 0.0f))
				continue;

			Vector3 xyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			float invDt = 1.0f / particles.PreviousDt;
			Vector3 delta = new((xyz.X - prev.X) * invDt, (xyz.Y - prev.Y) * invDt, (xyz.Z - prev.Z) * invDt);
			float velocity = MathF.Sqrt(delta.Y * delta.Y + delta.X * delta.X + delta.Z * delta.Z);
			MathLib.VectorNormalize(ref delta);

			Vector3 start = delta * TraceOffset + xyz;
			Vector3 end = delta * totalTraceDist + start;

			float closest = float.MaxValue;
			Vector3 pointOnPlane = Vector3.Zero;
			for (int i = 0; i < cache.ActivePlanes; i++) {
				if (!cache.PlaneActive[i])
					continue;
				float trial = TrialDistance(cache, i, start, end);
				if (trial <= closest) {
					closest = trial;
					pointOnPlane = cache.PointOnPlane[i];
				}
			}

			if (!(closest <= tolerance))
				pointOnPlane = TraceAndCache(particles, ref cache, start, end, totalTraceDist);

			if (ParticleCollisionCache.IsInvalid(pointOnPlane)) {
				life = -1.0f;
				continue;
			}

			Vector3 d = (start - pointOnPlane) * ComponentScale;
			life = (MathF.Sqrt(d.Y * d.Y + d.X * d.X + d.Z * d.Z) + TraceOffset) / (velocity + MathLibConsts.FLT_EPSILON);
		}
	}

	public override void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context) {
		ref WorldCollideContextData cache = ref ParticleCollisionCache.Get(particles.Parent ?? particles);
		float tolerance = TraceTolerance * TraceTolerance;
		float totalTraceDist = MaxTraceLength - TraceOffset;

		float dt = particles.PreviousDt;
		float rcp = MathF.ReciprocalEstimate(dt);
		rcp = rcp + rcp - rcp * rcp * dt;

		Span<Vector3> start = stackalloc Vector3[4];
		Span<Vector3> end = stackalloc Vector3[4];
		Span<float> velocity = stackalloc float[4];
		Span<float> closest = stackalloc float[4];
		Span<Vector3> pointOnPlane = stackalloc Vector3[4];

		for (int block = startBlock; block < startBlock + numBlocks; block++) {
			int first = block * 4;
			bool any = false;
			for (int lane = 0; lane < 4; lane++)
				any |= particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_LIFE_DURATION, first + lane) >= 0.0f;
			if (!any)
				continue;

			for (int lane = 0; lane < 4; lane++) {
				int p = first + lane;
				float life = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_LIFE_DURATION, p);
				closest[lane] = life < 0.0f ? 0.0f : float.MaxValue;

				Vector3 xyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
				Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
				Vector3 delta = new((xyz.X - prev.X) * rcp, (xyz.Y - prev.Y) * rcp, (xyz.Z - prev.Z) * rcp);
				float lenSq = delta.Y * delta.Y + delta.X * delta.X + delta.Z * delta.Z;
				velocity[lane] = MathF.Sqrt(lenSq);
				float rsqrt = MathF.ReciprocalSqrtEstimate(lenSq);
				Vector3 dir = new(delta.X * rsqrt, delta.Y * rsqrt, delta.Z * rsqrt);
				start[lane] = dir * TraceOffset + xyz;
				end[lane] = dir * totalTraceDist + start[lane];
			}

			for (int i = 0; i < cache.ActivePlanes; i++) {
				if (!cache.PlaneActive[i])
					continue;
				for (int lane = 0; lane < 4; lane++) {
					float trial = TrialDistance(cache, i, start[lane], end[lane]);
					if (trial <= closest[lane]) {
						closest[lane] = trial;
						pointOnPlane[lane] = cache.PointOnPlane[i];
					}
				}
			}

			for (int lane = 0; lane < 4; lane++) {
				if (tolerance < closest[lane])
					pointOnPlane[lane] = TraceAndCache(particles, ref cache, start[lane], end[lane], totalTraceDist);
			}

			for (int lane = 0; lane < 4; lane++) {
				int p = first + lane;
				ref float life = ref particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p);
				if (!(life >= 0.0f))
					continue;

				Vector3 point = pointOnPlane[lane];
				Vector3 invalid = ParticleInitUtil.Vec3Invalid;
				if (point.X == invalid.X || point.Y == invalid.Y || point.Z == invalid.Z) {
					life = 0.0f;
					continue;
				}

				Vector3 d = (start[lane] - point) * ComponentScale;
				life = (MathF.Sqrt(d.Y * d.Y + d.X * d.X + d.Z * d.Z) + TraceOffset) / (velocity[lane] + MathLibConsts.FLT_EPSILON);
			}
		}
	}
}

public sealed class C_INIT_CreateFromPlaneCache : ParticleOperatorInstance
{
	public Vector3 OffsetMin;
	public Vector3 OffsetMax;
	public bool UseNormal;
	bool LocalOffset;

	public static readonly ParticleOperatorDefinition<C_INIT_CreateFromPlaneCache> Definition = new("Position from Parent Cache", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_INIT_CreateFromPlaneCache>()
			.Vector3("Local Offset Min", "0 0 0", (o, v) => o.OffsetMin = v)
			.Vector3("Local Offset Max", "0 0 0", (o, v) => o.OffsetMax = v)
			.Bool("Set Normal", "0", (o, v) => o.UseNormal = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_NORMAL_MASK;
	public override uint GetReadAttributes() => 0;
	public override int GetRequiredContextBytes() => 0x1f10;

	public override void InitParams(ParticleSystemDefinition def, Element element) => LocalOffset = true;

	static void SetOrigin(ParticleCollection particles, int firstParticle, int numParticles) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, Vector3.Zero);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, Vector3.Zero);
		}
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		ParticleCollection? parent = particles.Parent;
		if (parent == null) {
			SetOrigin(particles, firstParticle, numParticles);
			return;
		}

		if (parent.CollisionCache == null)
			ParticleCollisionCache.Get(parent).PointOnPlane[0] = Vector3.Zero;
		ref WorldCollideContextData cache = ref ParticleCollisionCache.Get(parent);

		if (cache.ActivePlanes < 1) {
			SetOrigin(particles, firstParticle, numParticles);
			return;
		}

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			int index = (int)particles.RandomFloat(0.0f, cache.ActivePlanes);
			Vector3 normal = cache.PlaneNormal[index];
			Vector3 point = cache.PointOnPlane[index];
			if (ParticleCollisionCache.IsInvalid(normal) || ParticleCollisionCache.IsInvalid(point)) {
				particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p) = 0.0f;
				continue;
			}

			if (LocalOffset) {
				particles.RandomVector(OffsetMin, OffsetMax, out Vector3 randPos);
				MathLib.VectorAngles(normal, out QAngle angles);
				MathLib.AngleMatrix(angles, out Matrix3x4 rotation);
				MathLib.VectorRotate(new Vector3(randPos.Z, randPos.Y, randPos.X), rotation, out Vector3 offset);
				point += offset;
			}

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, point);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, point);
			if (UseNormal)
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_NORMAL, p, normal);
		}
	}
}
