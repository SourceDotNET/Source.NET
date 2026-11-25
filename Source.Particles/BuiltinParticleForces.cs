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
		mgr.AddParticleOperator(ParticleFunctionType.ForceGenerator, C_OP_AttractToControlPoint.Definition);
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
