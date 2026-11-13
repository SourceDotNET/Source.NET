using Datamodel;

using System.Numerics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public sealed class C_INIT_VelocityRandom : ParticleOperatorInstance
{
	public int ControlPointNumber;
	public float SpeedMin;
	public float SpeedMax;
	public Vector3 LocalCoordinateSystemSpeedMin;
	public Vector3 LocalCoordinateSystemSpeedMax;
	bool HasLocalSpeed;

	public static readonly ParticleOperatorDefinition<C_INIT_VelocityRandom> Definition = new("Velocity Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_VelocityRandom>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("random_speed_min", "0", (o, v) => o.SpeedMin = v)
			.Float("random_speed_max", "0", (o, v) => o.SpeedMax = v)
			.Vector3("speed_in_local_coordinate_system_min", "0 0 0", (o, v) => o.LocalCoordinateSystemSpeedMin = v)
			.Vector3("speed_in_local_coordinate_system_max", "0 0 0", (o, v) => o.LocalCoordinateSystemSpeedMax = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 63);
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		HasLocalSpeed = LocalCoordinateSystemSpeedMin != Vector3.Zero || LocalCoordinateSystemSpeedMax != Vector3.Zero;
		if (SpeedMax < SpeedMin)
			(SpeedMin, SpeedMax) = (SpeedMax, SpeedMin);
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			Vector3 velocity = Vector3.Zero;
			if (HasLocalSpeed) {
				particles.RandomVector(LocalCoordinateSystemSpeedMin, LocalCoordinateSystemSpeedMax, out Vector3 local);
				particles.GetControlPointOrientationAtTime(ControlPointNumber, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p), out Vector3 forward, out Vector3 right, out Vector3 up);
				velocity = local.X * forward - local.Y * right + local.Z * up;
			}

			if (SpeedMax > 0.0f) {
				particles.RandomVector(SpeedMin, SpeedMax, out Vector3 random);
				velocity += random;
			}

			Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prev - velocity * particles.PreviousDt);
		}
	}
}

public sealed class C_INIT_RandomScalar : ParticleOperatorInstance
{
	public float Min;
	public float Max;
	public float Exponent;
	public int FieldOutput;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomScalar> Definition = new("Scalar Random", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_INIT_RandomScalar>()
			.Float("min", "0", (o, v) => o.Min = v)
			.Float("max", "0", (o, v) => o.Max = v)
			.Float("exponent", "1", (o, v) => o.Exponent = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v));

	public override uint GetWrittenAttributes() => 1u << (FieldOutput & 31);
	public override uint GetReadAttributes() => 0;

	public override void InitParams(ParticleSystemDefinition def, Element element)
		=> FieldOutput = ParticleInitUtil.ClampFieldUpper(FieldOutput);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		if (ParticleInitUtil.CheckField(FieldOutput))
			InitScalarAttributeRandomRangeScalar(FieldOutput, Min, Max, Exponent, particles, firstParticle, numParticles);
	}

	public override void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context) {
		if (ParticleInitUtil.CheckField(FieldOutput))
			InitScalarAttributeRandomRangeBlock(FieldOutput, Min, Max, Exponent, particles, startBlock, numBlocks, false);
	}
}

public sealed class C_INIT_RandomVector : ParticleOperatorInstance
{
	public Vector3 Min;
	public Vector3 Max;
	public int FieldOutput;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomVector> Definition = new("Vector Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RandomVector>()
			.Vector3("min", "0 0 0", (o, v) => o.Min = v)
			.Vector3("max", "0 0 0", (o, v) => o.Max = v)
			.Int("output field", "0", (o, v) => o.FieldOutput = v));

	public override uint GetWrittenAttributes() => 1u << (FieldOutput & 31);
	public override uint GetReadAttributes() => 0;

	public override void InitParams(ParticleSystemDefinition def, Element element)
		=> FieldOutput = ParticleInitUtil.ClampFieldUpper(FieldOutput);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			bool valid = ParticleInitUtil.CheckField(FieldOutput);
			particles.RandomVector(Min, Max, out Vector3 output);
			if (valid)
				particles.SetVectorAttribute(FieldOutput, p, output);
		}
	}
}

public sealed class C_INIT_RandomVectorComponent : ParticleOperatorInstance
{
	public float Min;
	public float Max;
	public int FieldOutput;
	public int Component;

	public static readonly ParticleOperatorDefinition<C_INIT_RandomVectorComponent> Definition = new("Vector Component Random", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_INIT_RandomVectorComponent>()
			.Float("min", "0", (o, v) => o.Min = v)
			.Float("max", "0", (o, v) => o.Max = v)
			.Int("component 0/1/2 X/Y/Z", "0", (o, v) => o.Component = v)
			.Int("output field", "0", (o, v) => o.FieldOutput = v));

	public override uint GetWrittenAttributes() => 1u << (FieldOutput & 31);
	public override uint GetReadAttributes() => 1u << (FieldOutput & 31);
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		Component = Math.Clamp(Component, 0, 2);
		FieldOutput = ParticleInitUtil.ClampFieldUpper(FieldOutput);
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			bool valid = ParticleInitUtil.CheckField(FieldOutput);
			Vector3 value = valid ? particles.GetVectorAttributeValue(FieldOutput, p) : Vector3.Zero;
			value[Component] = particles.RandomFloat(Min, Max);
			if (valid)
				particles.SetVectorAttribute(FieldOutput, p, value);
		}
	}
}
