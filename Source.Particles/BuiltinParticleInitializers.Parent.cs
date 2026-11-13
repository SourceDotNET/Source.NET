using Datamodel;

using System.Numerics;
using System.Runtime.InteropServices;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

internal static class ParentParticleUtil
{
	public const uint VECTOR_ATTRIBUTES_MASK = 0x228045;

	public static ref int Current(Span<byte> context) => ref MemoryMarshal.AsRef<int>(context);

	public static int NextParent(ParticleCollection particles, ref int current, int maxIndex, bool random) {
		if (random)
			current = (int)(uint)((maxIndex + 1) * particles.RandomFloat(0.0f, 1.0f));
		else if (maxIndex < current || current < 0)
			current = 0;
		return current;
	}
}

public sealed class C_INIT_CreateFromParentParticles : ParticleOperatorInstance
{
	public float VelocityScale;
	public bool RandomDistribution;
	public int Increment;

	public static readonly ParticleOperatorDefinition<C_INIT_CreateFromParentParticles> Definition = new("Position From Parent Particles", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_INIT_CreateFromParentParticles>()
			.Float("Inherited Velocity Scale", "0", (o, v) => o.VelocityScale = v)
			.Bool("Random Parent Particle Distribution", "0", (o, v) => o.RandomDistribution = v)
			.Int("Particle Increment Amount", "1", (o, v) => o.Increment = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override int GetRequiredContextBytes() => sizeof(int);

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => ParentParticleUtil.Current(context) = 0;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		ParticleCollection? parent = particles.Parent;
		if (parent == null) {
			for (int p = firstParticle; p < firstParticle + numParticles; p++) {
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, Vector3.Zero);
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, Vector3.Zero);
			}
			return;
		}

		if (parent.ActiveParticles == 0) {
			for (int p = firstParticle; p < firstParticle + numParticles; p++)
				particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p) = 0.0f;
			return;
		}

		ref int current = ref ParentParticleUtil.Current(context);
		int maxIndex = Math.Max(0, parent.ActiveParticles - 1);
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			int index = ParentParticleUtil.NextParent(particles, ref current, maxIndex, RandomDistribution);

			float curTime = particles.CurTime;
			float prevTime = curTime - particles.Dt;
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			float t;
			if (prevTime == curTime)
				t = creationTime < curTime ? 0.0f : 1.0f;
			else
				t = Math.Clamp((creationTime - prevTime) / particles.Dt, 0.0f, 1.0f);

			Vector3 parentPrev = parent.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, index);
			Vector3 parentXYZ = parent.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, index);
			Vector3 pos = (parentXYZ - parentPrev) * t + parentPrev;
			Vector3 prev = (parentPrev - pos) * VelocityScale + pos;

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prev);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, pos);
			current += Increment;
		}
	}
}

public sealed class C_INIT_InheritFromParentParticles : ParticleOperatorInstance
{
	public float Scale;
	public int FieldOutput;
	public int Increment;
	public bool RandomDistribution;

	public static readonly ParticleOperatorDefinition<C_INIT_InheritFromParentParticles> Definition = new("Inherit Initial Value From Parent Particle", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_INIT_InheritFromParentParticles>()
			.Int("Inherited Field", "3", (o, v) => o.FieldOutput = v)
			.Float("Scale", "1", (o, v) => o.Scale = v)
			.Bool("Random Parent Particle Distribution", "0", (o, v) => o.RandomDistribution = v)
			.Int("Particle Increment Amount", "1", (o, v) => o.Increment = v));

	public override uint GetWrittenAttributes() => 1u << (FieldOutput & 31);
	public override uint GetReadAttributes() => 0;
	public override int GetRequiredContextBytes() => sizeof(int);
	public override bool InitMultipleOverride() => true;

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => ParentParticleUtil.Current(context) = 0;

	public override void InitParams(ParticleSystemDefinition def, Element element)
		=> FieldOutput = ParticleInitUtil.ClampFieldUpper(FieldOutput);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		ParticleCollection? parent = particles.Parent;
		if (parent == null || parent.ActiveParticles == 0)
			return;

		ref int current = ref ParentParticleUtil.Current(context);
		int maxIndex = Math.Max(0, parent.ActiveParticles - 1);
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			int index = ParentParticleUtil.NextParent(particles, ref current, maxIndex, RandomDistribution);
			if (ParticleInitUtil.CheckField(FieldOutput)) {
				uint mask = 1u << FieldOutput;
				if ((mask & ParentParticleUtil.VECTOR_ATTRIBUTES_MASK) == 0) {
					float value = parent.GetFloatAttributeValue(FieldOutput, index) * Scale;
					if ((mask & ParticleInitUtil.ALPHA_ATTRIBUTES_MASK) != 0)
						value = Math.Clamp(value, 0.0f, 1.0f);
					particles.GetFloatAttributeRefForWrite(FieldOutput, p) = value;
				}
				else
					particles.SetVectorAttribute(FieldOutput, p, parent.GetVectorAttributeValue(FieldOutput, index) * Scale);
			}
			current += Increment;
		}
	}
}
