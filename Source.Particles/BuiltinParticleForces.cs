using Datamodel;

using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public static class BuiltinParticleForces
{
	public static void AddBuiltInParticleForceGenerators(ParticleSystemMgr mgr) {
		mgr.AddParticleOperator(ParticleFunctionType.ForceGenerator, C_OP_RandomForce.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.ForceGenerator, C_OP_TwistAroundAxis.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.ForceGenerator, C_OP_ParentVortices.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.ForceGenerator, C_OP_AttractToControlPoint.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.ForceGenerator, C_OP_TimeVaryingForce.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.ForceGenerator, C_OP_TurbulenceForce.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.ForceGenerator, C_OP_ForceBasedOnDistanceToPlane.Definition);
	}
}

internal static class ParticleForceUtil
{
	public const float FLT_EPSILON = 1.1920929e-07f;

	public static void Add(Span<FourVectors> forces, int p, in Vector3 force) {
		ref FourVectors block = ref forces[p >> 2];
		int lane = p & 3;
		Unsafe.Add(ref block.x.X, lane) += force.X;
		Unsafe.Add(ref block.y.X, lane) += force.Y;
		Unsafe.Add(ref block.z.X, lane) += force.Z;
	}

	public static Vector3 Position(ParticleCollection particles, int p) => particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);

	public static Vector3 RSqrtNormalize(Vector3 v) => v * (1.0f / MathF.Sqrt(v.LengthSquared()));

	public static Vector3 TangentialForce(Vector3 ofs, in Vector3 axis, float scale) {
		float distSq = ofs.LengthSquared();
		ofs = RSqrtNormalize(ofs);
		ofs -= ofs * Vector3.Dot(ofs, axis);
		if (!(distSq > FLT_EPSILON && ofs.LengthSquared() > FLT_EPSILON))
			return Vector3.Zero;
		return Vector3.Cross(RSqrtNormalize(ofs), axis) * scale;
	}

	public static float PowFixedPoint(float x, int exponent) => RandSIMD.PowFixedPointExponent(Vector128.Create(x), exponent)[0];
}

public sealed class C_OP_RandomForce : ParticleOperatorInstance
{
	public Vector3 MinForce;
	public Vector3 MaxForce;

	public static readonly ParticleOperatorDefinition<C_OP_RandomForce> Definition = new("random force", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RandomForce>()
			.Vector3("min force", "0 0 0", (o, v) => o.MinForce = v)
			.Vector3("max force", "0 0 0", (o, v) => o.MaxForce = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;

	public override void AddForces(Span<FourVectors> accumulatedForces, ParticleCollection particles, int blocks, float strength, Span<byte> context) {
		Vector4 minX = new(MinForce.X * strength), minY = new(MinForce.Y * strength), minZ = new(MinForce.Z * strength);
		Vector4 rangeX = new Vector4(MaxForce.X * strength) - minX, rangeY = new Vector4(MaxForce.Y * strength) - minY, rangeZ = new Vector4(MaxForce.Z * strength) - minZ;
		int ctx = RandSIMD.GetContext();
		for (int i = 0; i < blocks; i++) {
			ref FourVectors force = ref accumulatedForces[i];
			force.x += rangeX * RandSIMD.Next(ctx).AsVector4() + minX;
			force.y += rangeY * RandSIMD.Next(ctx).AsVector4() + minY;
			force.z += rangeZ * RandSIMD.Next(ctx).AsVector4() + minZ;
		}
		RandSIMD.ReleaseContext(ctx);
	}
}

public sealed class C_OP_TwistAroundAxis : ParticleOperatorInstance
{
	public float ForceAmount;
	public Vector3 TwistAxis;
	public bool LocalSpace;

	public static readonly ParticleOperatorDefinition<C_OP_TwistAroundAxis> Definition = new("twist around axis", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_TwistAroundAxis>()
			.Float("amount of force", "0", (o, v) => o.ForceAmount = v)
			.Vector3("twist axis", "0 0 1", (o, v) => o.TwistAxis = v)
			.Bool("object local space axis 0/1", "0", (o, v) => o.LocalSpace = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;

	public override void AddForces(Span<FourVectors> accumulatedForces, ParticleCollection particles, int blocks, float strength, Span<byte> context) {
		Vector3 axis = ParticleForceUtil.RSqrtNormalize(particles.TransformAxis(TwistAxis, LocalSpace));
		Vector3 center = particles.GetControlPointAtCurrentTime(0);
		float scale = ForceAmount * strength;
		for (int p = 0; p < blocks * 4; p++)
			ParticleForceUtil.Add(accumulatedForces, p, ParticleForceUtil.TangentialForce(ParticleForceUtil.Position(particles, p) - center, axis, scale));
	}
}

public sealed class C_OP_ParentVortices : ParticleOperatorInstance
{
	public float ForceScale;
	public Vector3 TwistAxis;
	public bool FlipBasedOnYaw;

	public static readonly ParticleOperatorDefinition<C_OP_ParentVortices> Definition = new("Create vortices from parent particles", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_ParentVortices>()
			.Float("amount of force", "0", (o, v) => o.ForceScale = v)
			.Vector3("twist axis", "0 0 1", (o, v) => o.TwistAxis = v)
			.Bool("flip twist axis with yaw", "0", (o, v) => o.FlipBasedOnYaw = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;

	public override void AddForces(Span<FourVectors> accumulatedForces, ParticleCollection particles, int blocks, float strength, Span<byte> context) {
		ParticleCollection? parent = particles.Parent;
		if (parent == null || parent.ActiveParticles == 0)
			return;

		Vector3 axis = ParticleForceUtil.RSqrtNormalize(TwistAxis);
		int vortexCount = parent.ActiveParticles;
		Span<Vector3> centers = vortexCount <= 256 ? stackalloc Vector3[vortexCount] : new Vector3[vortexCount];
		Span<float> ooRadiusSq = vortexCount <= 256 ? stackalloc float[vortexCount] : new float[vortexCount];
		Span<float> strengths = vortexCount <= 256 ? stackalloc float[vortexCount] : new float[vortexCount];
		for (int i = 0; i < vortexCount; i++) {
			float ooRadius = 1.0f / (parent.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, i) + 1e-05f);
			ooRadiusSq[i] = ooRadius * ooRadius;
			strengths[i] = strength * ForceScale * parent.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA, i);
			centers[i] = parent.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, i);
			if (FlipBasedOnYaw && parent.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_YAW, i) >= MathF.PI)
				centers[i] *= -1.0f;
		}

		for (int p = 0; p < blocks * 4; p++) {
			Vector3 pos = ParticleForceUtil.Position(particles, p);
			Vector3 sum = Vector3.Zero;
			for (int v = 0; v < vortexCount; v++) {
				Vector3 ofs = pos - centers[v];
				float falloff = MathF.Max(0.0f, 1.0f - ofs.LengthSquared() * ooRadiusSq[v]);
				sum += ParticleForceUtil.TangentialForce(ofs, axis, strengths[v] * falloff);
			}
			ParticleForceUtil.Add(accumulatedForces, p, sum);
		}
	}
}

public sealed class C_OP_AttractToControlPoint : ParticleOperatorInstance
{
	public float ForceAmount;
	public float FalloffPower;
	public int ControlPointNumber;

	public static readonly ParticleOperatorDefinition<C_OP_AttractToControlPoint> Definition = new("Pull towards control point", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_AttractToControlPoint>()
			.Float("amount of force", "0", (o, v) => o.ForceAmount = v)
			.Float("falloff power", "2", (o, v) => o.FalloffPower = v)
			.Int("control point number", "0", (o, v) => o.ControlPointNumber = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 0x3f);

	public override void AddForces(Span<FourVectors> accumulatedForces, ParticleCollection particles, int blocks, float strength, Span<byte> context) {
		if ((uint)ControlPointNumber >= MAX_PARTICLE_CONTROL_POINTS)
			return;
		int powerFrac = (int)(FalloffPower * -4.0f);
		float forceScale = -(ForceAmount * strength);
		Vector3 center = particles.GetControlPointAtCurrentTime(ControlPointNumber);
		for (int p = 0; p < blocks * 4; p++) {
			Vector3 ofs = ParticleForceUtil.Position(particles, p) - center;
			float len = ofs.Length();
			if (!(len > ParticleForceUtil.FLT_EPSILON))
				continue;
			ofs *= forceScale / len;
			ofs *= ParticleForceUtil.PowFixedPoint(len, powerFrac);
			ParticleForceUtil.Add(accumulatedForces, p, ofs);
		}
	}
}

public sealed class C_OP_TimeVaryingForce : ParticleOperatorInstance
{
	public float StartLerpTime;
	public Vector3 StartingForce;
	public float EndLerpTime;
	public Vector3 EndingForce;

	public static readonly ParticleOperatorDefinition<C_OP_TimeVaryingForce> Definition = new("time varying force", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_TimeVaryingForce>()
			.Float("time to start transition", "0", (o, v) => o.StartLerpTime = v)
			.Vector3("starting force", "0 0 0", (o, v) => o.StartingForce = v)
			.Float("time to end transition", "10", (o, v) => o.EndLerpTime = v)
			.Vector3("ending force", "0 0 0", (o, v) => o.EndingForce = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;

	public override void AddForces(Span<FourVectors> accumulatedForces, ParticleCollection particles, int blocks, float strength, Span<byte> context) {
		Vector3 start = StartingForce * strength;
		Vector3 delta = EndingForce * strength - start;
		float ooRange = 1.0f / (EndLerpTime - StartLerpTime);
		for (int p = 0; p < blocks * 4; p++) {
			float age = particles.CurTime - particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			float t = MathF.Max(0.0f, MathF.Min(1.0f, (age - StartLerpTime) * ooRange));
			ParticleForceUtil.Add(accumulatedForces, p, delta * t + start);
		}
	}
}

public sealed class C_OP_TurbulenceForce : ParticleOperatorInstance
{
	public readonly float[] NoiseCoordScale = new float[4];
	public readonly Vector3[] NoiseAmount = new Vector3[4];

	public static readonly ParticleOperatorDefinition<C_OP_TurbulenceForce> Definition = new("turbulent force", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_TurbulenceForce>()
			.Float("Noise scale 0", "1", (o, v) => o.NoiseCoordScale[0] = v)
			.Vector3("Noise amount 0", "1 1 1", (o, v) => o.NoiseAmount[0] = v)
			.Float("Noise scale 1", "0", (o, v) => o.NoiseCoordScale[1] = v)
			.Vector3("Noise amount 1", ".5 .5 .5", (o, v) => o.NoiseAmount[1] = v)
			.Float("Noise scale 2", "0", (o, v) => o.NoiseCoordScale[2] = v)
			.Vector3("Noise amount 2", ".25 .25 .25", (o, v) => o.NoiseAmount[2] = v)
			.Float("Noise scale 3", "0", (o, v) => o.NoiseCoordScale[3] = v)
			.Vector3("Noise amount 3", ".125 .125 .125", (o, v) => o.NoiseAmount[3] = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;

	public override void AddForces(Span<FourVectors> accumulatedForces, ParticleCollection particles, int blocks, float strength, Span<byte> context) {
		for (int p = 0; p < blocks * 4; p++) {
			Vector3 pos = ParticleForceUtil.Position(particles, p);
			Vector3 sum = Vector3.Zero;
			for (int i = 0; i < 4; i++)
				sum += NoiseSIMD.DNoise(pos * NoiseCoordScale[i]) * NoiseAmount[i];
			ParticleForceUtil.Add(accumulatedForces, p, sum);
		}
	}
}

public sealed class C_OP_ForceBasedOnDistanceToPlane : ParticleOperatorInstance
{
	public float MinDist;
	public Vector3 ForceAtMinDist;
	public float MaxDist;
	public Vector3 ForceAtMaxDist;
	public Vector3 PlaneNormal;
	public int ControlPointNumber;
	public float Exponent;

	public static readonly ParticleOperatorDefinition<C_OP_ForceBasedOnDistanceToPlane> Definition = new("Force based on distance from plane", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_ForceBasedOnDistanceToPlane>()
			.Float("Min distance from plane", "0", (o, v) => o.MinDist = v)
			.Vector3("Force at Min distance", "0 0 0", (o, v) => o.ForceAtMinDist = v)
			.Float("Max Distance from plane", "1", (o, v) => o.MaxDist = v)
			.Vector3("Force at Max distance", "0 0 0", (o, v) => o.ForceAtMaxDist = v)
			.Vector3("Plane Normal", "0 0 1", (o, v) => o.PlaneNormal = v)
			.Int("Control point number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("Exponent", "1", (o, v) => o.Exponent = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 0x3f);

	public override void AddForces(Span<FourVectors> accumulatedForces, ParticleCollection particles, int blocks, float strength, Span<byte> context) {
		float deltaDistances = MaxDist - MinDist;
		float ooRange = deltaDistances != 0.0f ? 1.0f / deltaDistances : 0.0f;
		int cp = (uint)ControlPointNumber >= MAX_PARTICLE_CONTROL_POINTS ? 0 : ControlPointNumber;
		Vector3 pointOnPlane = particles.GetControlPointAtCurrentTime(cp);
		Vector3 forceDelta = ForceAtMaxDist - ForceAtMinDist;
		int powValue = (int)(Exponent * 4.0f);
		for (int p = 0; p < blocks * 4; p++) {
			float dist = (Vector3.Dot(ParticleForceUtil.Position(particles, p) - pointOnPlane, PlaneNormal) - MinDist) * ooRange;
			dist = MathF.Max(0.0f, MathF.Min(1.0f, dist));
			ParticleForceUtil.Add(accumulatedForces, p, forceDelta * ParticleForceUtil.PowFixedPoint(dist, powValue) + ForceAtMinDist);
		}
	}
}
