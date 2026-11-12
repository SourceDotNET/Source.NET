using Source.Common.Mathematics;

using System.Numerics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public static class BuiltinParticleOperators
{
	public static void AddBuiltInParticleOperators(ParticleSystemMgr mgr) {
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_Decay.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_BasicMovement.Definition);
	}
}

public sealed class C_OP_Decay : ParticleOperatorInstance
{
	public static readonly ParticleOperatorDefinition<C_OP_Decay> Definition = new("Lifespan Decay", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_Decay>());

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int limit = particles.PaddedActiveParticles * 4;
		for (int i = 0; i < limit; i++) {
			float lifeDuration = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_LIFE_DURATION, i);
			float age = particles.CurTime - particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, i);
			if (lifeDuration <= age || lifeDuration <= 0.0f)
				particles.KillParticle(i);
		}
	}
}

public sealed class C_OP_BasicMovement : ParticleOperatorInstance
{
	public Vector3 Gravity;
	public float Drag;
	public int MaxConstraintPasses;

	public static readonly ParticleOperatorDefinition<C_OP_BasicMovement> Definition = new("Movement Basic", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_BasicMovement>()
			.Vector3("gravity", "0 0 0", (o, v) => o.Gravity = v)
			.Float("drag", "0", (o, v) => o.Drag = v)
			.Int("max constraint passes", "3", (o, v) => o.MaxConstraintPasses = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => 0;

	static readonly FourVectors[] PerParticleForceAccumulator = new FourVectors[MAX_PARTICLES_IN_A_SYSTEM / 4 + 1];
	static readonly bool[] ConstraintSatisfied = new bool[MAX_PARTICLE_CONTROL_POINTS];
	static readonly bool[] FinalConstraint = new bool[MAX_PARTICLE_CONTROL_POINTS];

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		ParticleSystemDefinition def = particles.Def!;
		float dt = particles.Dt;
		float adjustedDrag = MathF.Exp(dt * 30.0f * MathF.Log(1.0f - Math.Max(Drag, 0.0f))) * (dt / particles.PreviousDt);
		int blocks = particles.PaddedActiveParticles;

		FourVectors gravity = new(Gravity);
		int accumulatorStride;
		if (def.ForceGenerators.Count == 0) {
			PerParticleForceAccumulator[0] = gravity;
			accumulatorStride = 0;
		}
		else {
			PerParticleForceAccumulator.AsSpan(0, blocks).Fill(gravity);
			for (int i = 0; i < def.ForceGenerators.Count; i++) {
				ParticleOperatorInstance op = def.ForceGenerators[i];
				if (particles.CheckIfOperatorShouldRun(op, out float strength))
					op.AddForces(PerParticleForceAccumulator.AsSpan(0, blocks), particles, blocks, strength, particles.OperatorContextData.AsSpan(def.ForceGeneratorsCtxOffsets[i]));
			}
			accumulatorStride = 1;
		}

		float dtSquared = dt * dt;
		Span<float> memory = particles.AttributeMemory;
		int xyz = particles.GetAttributeOffset(PARTICLE_ATTRIBUTE_XYZ, out int xyzStride);
		int prevXyz = particles.GetAttributeOffset(PARTICLE_ATTRIBUTE_PREV_XYZ, out int prevXyzStride);

		for (int block = 0, accumulator = 0; block < blocks; block++, accumulator += accumulatorStride) {
			ref FourVectors force = ref PerParticleForceAccumulator[accumulator];
			for (int lane = 0; lane < 4; lane++) {
				for (int axis = 0; axis < 3; axis++) {
					float accel = axis switch { 0 => force.x[lane], 1 => force.y[lane], _ => force.z[lane] };
					ref float prev = ref memory[prevXyz + axis * 4 + lane];
					float cur = memory[xyz + axis * 4 + lane];
					prev = (cur - prev) * adjustedDrag + accel * dtSquared + cur;
				}
			}
			xyz += xyzStride;
			prevXyz += prevXyzStride;
		}

		particles.SwapPosAndPrevPos();

		int constraintCount = def.Constraints.Count;
		if (constraintCount == 0 || particles.PaddedActiveParticles == 0)
			return;

		for (int i = 0; i < constraintCount; i++) {
			ParticleOperatorInstance op = def.Constraints[i];
			FinalConstraint[i] = op.IsFinalConstraint();
			ConstraintSatisfied[i] = false;
			op.SetupConstraintPerFrameData(particles, particles.OperatorContextData.AsSpan(def.ConstraintsCtxOffsets[i]));
		}

		for (int pass = 0; pass < MaxConstraintPasses; pass++) {
			for (int i = 0; i < constraintCount; i++) {
				if (ConstraintSatisfied[i])
					continue;
				ConstraintSatisfied[i] = true;

				ParticleOperatorInstance op = def.Constraints[i];
				if (FinalConstraint[i] || !particles.CheckIfOperatorShouldRun(op, out _))
					continue;
				if (!op.EnforceConstraint(0, particles.PaddedActiveParticles, particles, particles.OperatorContextData.AsSpan(def.ConstraintsCtxOffsets[i]), particles.ActiveParticles))
					continue;

				for (int j = 0; j < constraintCount; j++) {
					if (j != i)
						ConstraintSatisfied[j] = false;
				}
			}
		}

		for (int i = 0; i < constraintCount; i++) {
			if (!FinalConstraint[i])
				continue;
			ParticleOperatorInstance op = def.Constraints[i];
			if (particles.CheckIfOperatorShouldRun(op, out _))
				op.EnforceConstraint(0, particles.PaddedActiveParticles, particles, particles.OperatorContextData.AsSpan(def.ConstraintsCtxOffsets[i]), particles.ActiveParticles);
		}
	}
}
