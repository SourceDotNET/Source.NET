using Datamodel;

using Source.Common.Mathematics;

using System.Numerics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public sealed class C_INIT_SequenceFromCP : ParticleOperatorInstance
{
	public int ControlPoint;
	public Vector3 Offset;
	public bool RadiusScale;
	public bool KillUnused;

	public static readonly ParticleOperatorDefinition<C_INIT_SequenceFromCP> Definition = new("Sequence From Control Point", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_SequenceFromCP>()
			.Int("control point", "1", (o, v) => o.ControlPoint = v)
			.Vector3("per particle spatial offset", "0 0 0", (o, v) => o.Offset = v)
			.Bool("offset propotional to radius", "0", (o, v) => o.RadiusScale = v));

	public override uint GetWrittenAttributes() {
		int ret = Offset != Vector3.Zero ? PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER_MASK | PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK : PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER_MASK;
		return (uint)(KillUnused ? ret | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK : ret);
	}

	public override uint GetReadAttributes() => Offset != Vector3.Zero ? (uint)(PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_RADIUS_MASK) : 0;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPoint & 63);
	public override bool InitMultipleOverride() => true;

	static int Digits(float value) => Math.Max(0, (int)Math.Log10(value * 10.0f));

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		Vector3 cp = particles.GetControlPointAtCurrentTime(ParticleInitUtil.CheckControlPoint(ControlPoint));
		int component = 2;
		int desiredCount = Math.Min(Digits(cp.X) + Digits(cp.Y) + Digits(cp.Z), numParticles) - 1;
		Vector3 offsetPos = (float)desiredCount * Offset * 0.5f;
		bool hasOffset = Offset != Vector3.Zero;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			while (component >= 0 && cp[component] < 1.0f)
				component--;

			if (component < 0 || cp[component] <= 0.0f) {
				particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p) = -1.0f;
				continue;
			}

			float value = cp[component];
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_SEQUENCE_NUMBER, p) = (float)(value % 10.0 + component * 10);
			cp[component] = MathF.Floor(value * 0.1f);

			if (hasOffset) {
				float radius = RadiusScale ? particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, p) : 1.0f;
				Vector3 delta = offsetPos * radius;
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) + delta);
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) + delta);
				offsetPos -= Offset;
			}
		}
	}
}

public sealed class C_INIT_NormalAlignToCP : ParticleOperatorInstance
{
	public int ControlPointNumber;

	public static readonly ParticleOperatorDefinition<C_INIT_NormalAlignToCP> Definition = new("Normal Align to CP", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_NormalAlignToCP>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_NORMAL_MASK;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 63);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		int cp = ParticleInitUtil.CheckControlPoint(ControlPointNumber);
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			particles.GetControlPointOrientationAtTime(cp, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p), out Vector3 forward, out _, out _);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_NORMAL, p, forward);
		}
	}
}

public sealed class C_INIT_NormalOffset : ParticleOperatorInstance
{
	public int ControlPointNumber;
	public Vector3 OffsetMin;
	public Vector3 OffsetMax;
	public bool LocalCoords;
	public bool Normalize;

	public static readonly ParticleOperatorDefinition<C_INIT_NormalOffset> Definition = new("Normal Modify Offset Random", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_INIT_NormalOffset>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Vector3("offset min", "0 0 0", (o, v) => o.OffsetMin = v)
			.Vector3("offset max", "0 0 0", (o, v) => o.OffsetMax = v)
			.Bool("offset in local space 0/1", "0", (o, v) => o.LocalCoords = v)
			.Bool("normalize output 0/1", "0", (o, v) => o.Normalize = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_NORMAL_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_NORMAL_MASK;
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 63);
	public override bool InitMultipleOverride() => true;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			particles.RandomVector(OffsetMin, OffsetMax, out Vector3 randPos);
			if (LocalCoords) {
				particles.GetControlPointTransformAtTime(ParticleInitUtil.CheckControlPoint(ControlPointNumber), particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p), out Matrix3x4 mat);
				MathLib.VectorRotate(randPos, mat, out randPos);
			}

			Vector3 normal = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_NORMAL, p) + randPos;
			if (Normalize)
				MathLib.VectorNormalize(ref normal);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_NORMAL, p, normal);
		}
	}
}

public sealed class C_INIT_RemapInitialCPDirectionToRotation : ParticleOperatorInstance
{
	public int ControlPoint;
	public int FieldOutput;
	public int Axis;
	public float OffsetRot;
	int Component1;
	int Component2;

	public static readonly ParticleOperatorDefinition<C_INIT_RemapInitialCPDirectionToRotation> Definition = new("Remap CP Orientation to Rotation", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RemapInitialCPDirectionToRotation>()
			.Int("control point", "0", (o, v) => o.ControlPoint = v)
			.Int("rotation field", "0", (o, v) => o.FieldOutput = v)
			.Int("axis", "0", (o, v) => o.Axis = v)
			.Float("offset rotation", "0", (o, v) => o.OffsetRot = v));

	public override uint GetWrittenAttributes() => 1u << (FieldOutput & 31);
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = Math.Clamp(FieldOutput, 0, ParticleInitUtil.GMOD_ATTRIBUTE_COUNT - 1);
		if (Axis < 0)
			(Component1, Component2) = (1, 0);
		else if (Axis == 0)
			(Component1, Component2) = (1, 0);
		else if (Axis == 1)
			(Component1, Component2) = (2, 0);
		else {
			Axis = 2;
			(Component1, Component2) = (2, 1);
		}
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		particles.GetControlPointOrientationAtCurrentTime(ParticleInitUtil.CheckControlPoint(ControlPoint), out Vector3 forward, out Vector3 right, out Vector3 up);
		Vector3 row = Axis switch {
			1 => right,
			2 => up,
			_ => forward
		};

		float rotation = (float)(Math.Atan2(row[Component1], row[Component2]) + Math.PI) + OffsetRot * 0.017453292f;
		for (int p = firstParticle; p < firstParticle + numParticles; p++)
			particles.GetFloatAttributeRefForWrite(FieldOutput, p) = rotation;
	}
}

public sealed class C_INIT_VelocityFromCP : ParticleOperatorInstance
{
	public int ControlPoint;
	public float VelocityScale;
	public int ControlPointCompare;
	public int ControlPointLocal;
	public bool DirectionOnly;

	public static readonly ParticleOperatorDefinition<C_INIT_VelocityFromCP> Definition = new("Velocity Set from Control Point", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_VelocityFromCP>()
			.Int("control point number", "0", (o, v) => o.ControlPoint = v)
			.Float("velocity scale", "1", (o, v) => o.VelocityScale = v)
			.Int("comparison control point number", "-1", (o, v) => o.ControlPointCompare = v)
			.Int("local space control point number", "-1", (o, v) => o.ControlPointLocal = v)
			.Bool("direction only", "0", (o, v) => o.DirectionOnly = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override bool InitMultipleOverride() => true;

	public override ulong GetReadControlPointMask() {
		ulong mask = 1UL << (ControlPoint & 63);
		if (ControlPointCompare != -1)
			mask |= 1UL << (ControlPointCompare & 63);
		if (ControlPointLocal != -1)
			mask |= 1UL << (ControlPointLocal & 63);
		return mask;
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		Vector3 delta;
		if (ControlPointCompare < 0)
			delta = particles.GetControlPointAtCurrentTime(ParticleInitUtil.CheckControlPoint(ControlPoint));
		else {
			int compare = ParticleInitUtil.CheckControlPoint(ControlPointCompare);
			delta = particles.GetControlPointAtCurrentTime(ParticleInitUtil.CheckControlPoint(ControlPoint)) - particles.GetControlPointAtCurrentTime(compare);
		}

		if (ControlPointLocal > -1) {
			particles.GetControlPointTransformAtCurrentTime(ParticleInitUtil.CheckControlPoint(ControlPointLocal), out Matrix3x4 mat);
			MathLib.VectorRotate(delta, mat, out delta);
		}

		if (DirectionOnly)
			MathLib.VectorNormalize(ref delta);

		float dt = particles.PreviousDt;
		float scale = VelocityScale;
		Vector3 step = new(dt * delta.X * scale, dt * delta.Y * scale, dt * delta.Z * scale);
		for (int p = firstParticle; p < firstParticle + numParticles; p++)
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) - step);
	}
}
