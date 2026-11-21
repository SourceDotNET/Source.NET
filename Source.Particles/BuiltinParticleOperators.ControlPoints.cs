using Datamodel;

using System.Numerics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public sealed class C_OP_DampenToCP : ParticleOperatorInstance
{
	public int ControlPointNumber;
	public float Range;
	public float Scale;

	public static readonly ParticleOperatorDefinition<C_OP_DampenToCP> Definition = new("Movement Dampen Relative to Control Point", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_DampenToCP>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("falloff range", "100", (o, v) => o.Range = v)
			.Float("dampen scale", "1", (o, v) => o.Scale = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;
	public override void InitParams(ParticleSystemDefinition def, Element element) => ControlPointNumber = ParticleOpUtil.ClampCP(ControlPointNumber);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (Range <= 0.0f)
			return;
		Vector3 controlPoint = particles.GetControlPointAtCurrentTime(ControlPointNumber);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			float distance = (position - controlPoint).Length();
			if (distance > Range)
				continue;
			float dampen = MathF.Pow(distance / Range, Scale);
			Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			Vector3 dampened = prev + (position - prev) * dampen;
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, Vector3.Lerp(position, dampened, opStrength));
		}
	}
}
