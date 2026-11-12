using Datamodel;

using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.Intrinsics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public static class BuiltinParticleOperators
{
	public static void AddBuiltInParticleOperators(ParticleSystemMgr mgr) {
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_Decay.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_BasicMovement.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_FadeAndKill.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_FadeAndKillForTracers.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_FadeIn.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_FadeOut.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_FadeInSimple.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_FadeOutSimple.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_ClampScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_ClampVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_OscillateScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_OscillateScalarSimple.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_OscillateVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_OscillateVectorSimple.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_InterpolateRadius.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_ColorInterpolate.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_DifferencePreviousParticle.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_LerpScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_LerpEndCapScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_LerpEndCapVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_LerpVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapSpeed.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapSpeedtoCP.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapBoundingVolumetoCP.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapAverageScalarValuetoCP.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RampScalarLinear.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RampScalarSpline.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RampScalarLinearSimple.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RampScalarSplineSimple.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_Noise.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_VectorNoise.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_VelocityDecay.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_AlphaDecay.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RadiusDecay.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_DecayMaintainCount.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_Cull.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_Spin.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SpinUpdate.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SpinYaw.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_PositionLock.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_ControlpointLight.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SetChildControlPoints.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SetControlPointsToParticle.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SetPerChildControlPoint.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SetControlPointPositions.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_DampenToCP.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_DistanceBetweenCPs.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_DistanceBetweenCPsToCP.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_PercentageBetweenCPs.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_PercentageBetweenCPsVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_DistanceToCP.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SetControlPointToPlayer.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_LockToBone.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_CPOffsetToPercentageBetweenCPs.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_PlaneCull.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_DistanceCull.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_ModelCull.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SetControlPointToCenter.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_VelocityMatchingForce.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_MovementMaintainOffset.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_MovementPlaceOnGround.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_InheritFromParentParticles.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_OrientTo2dDirection.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_Orient2DRelToCP.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RestartAfterDuration.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_StopAfterCPDuration.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SetControlPointRotation.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_MovementRotateParticleAroundAxis.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RotateVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_MaxVelocity.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_LagCompensation.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_NormalizeVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapVelocityToVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapCPVelocityToVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SetCPOrientationToDirection.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapDirectionToCPToVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapControlPointDirectionToVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapDotProductToScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapCPtoScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_RemapCPtoVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_MaintainSequentialPath.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_LockToSavedSequentialPath.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_NormalLock.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_SetControlPointToImpactPoint.Definition);
	}
}

internal static class ParticleOpUtil
{
	public const int MAX_GMOD_ATTRIBUTE = 21;
	public const int ATTRIBUTES_WHICH_ARE_0_TO_1 = 0x10080;
	public const int ATTRIBUTES_WHICH_ARE_SIZE = 0x408;
	public const int ATTRIBUTES_WHICH_ARE_COLOR_AND_OPACITY = 0x100c0;

	public static int ClampField(int field) => Math.Clamp(field, 0, MAX_GMOD_ATTRIBUTE);

	public static bool IsVectorAttribute(int attribute) => (ATTRIBUTES_WHICH_ARE_VEC3S_MASK & (1 << attribute)) != 0;

	public static float SimpleSpline(float t) => t * t * 3.0f - t * 2.0f * t * t;

	public static float PreCalcBiasParameter(float bias) => 1.0f / bias - 2.0f;

	public static float Bias(float x, float biasParam) => x / ((1.0f - x) * biasParam + 1.0f);

	public static float SinEst01(float x) {
		float a = MathF.Abs(x);
		float rounded = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(a + 8388608.0f) & ~1) - 8388608.0f;
		if (a < rounded)
			rounded -= 2.0f;
		a -= rounded;
		bool flip = a >= 1.0f;
		if (flip)
			a -= 1.0f;
		float value = (4.0f - a * 4.0f) * a;
		return (x < 0.0f || (x == 0.0f && float.IsNegative(x))) != flip ? -value : value;
	}

	public static float PowFixedPoint(float x, float exponent) => RandSIMD.PowFixedPointExponent(Vector128.Create(x), (int)(exponent * 4.0f))[0];

	public static float Age(ParticleCollection particles, int p) => particles.CurTime - particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);

	public static float Lifetime(ParticleCollection particles, int p) => particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_LIFE_DURATION, p);

	public static float NormalizedAge(ParticleCollection particles, int p) => Age(particles, p) / Lifetime(particles, p);

	public static int PaddedCount(ParticleCollection particles) => particles.PaddedActiveParticles * 4;

	public static Vector3 GetVector(ParticleCollection particles, int attribute, int p) {
		if (IsVectorAttribute(attribute))
			return particles.GetVectorAttributeValue(attribute, p);
		return new Vector3(particles.GetFloatAttributeValue(attribute, p), 0, 0);
	}

	public static void SetVector(ParticleCollection particles, int attribute, int p, Vector3 v) {
		if (IsVectorAttribute(attribute))
			particles.SetVectorAttribute(attribute, p, v);
		else
			particles.GetFloatAttributeRefForWrite(attribute, p) = v.X;
	}

	public static Vector3 GetInitialVector(ParticleCollection particles, int attribute, int p) {
		if (IsVectorAttribute(attribute))
			return particles.GetInitialVectorAttributeValue(attribute, p);
		return new Vector3(particles.GetInitialFloatAttributeValue(attribute, p), 0, 0);
	}

	public const int ATTRIBUTES_WHICH_ARE_ANGLES = 0x101010;
	public const float MIN_PARTICLE_SPEED = 0.001f;

	public static bool IsUnitAttribute(int attribute) => (ATTRIBUTES_WHICH_ARE_0_TO_1 & (1 << attribute)) != 0;

	public static void GetClampRange(int field, out float min, out float max) {
		if ((ATTRIBUTES_WHICH_ARE_0_TO_1 & (1 << field)) != 0) {
			min = 0.0f;
			max = 1.0f;
		}
		else if ((ATTRIBUTES_WHICH_ARE_SIZE & (1 << field)) != 0) {
			min = 0.0f;
			max = float.MaxValue;
		}
		else {
			min = -float.MaxValue;
			max = float.MaxValue;
		}
	}

	public static float RemapClamped(float value, float inMin, float inMax, float outMin, float outMax) => ParticleInitUtil.RemapClamped(value, inMin, inMax, outMin, outMax);

	public static float Lerp(float t, float a, float b) => a + (b - a) * t;

	public static ulong CPMask(int cp) => cp >= 0 && cp < 64 ? 1UL << cp : 0;

	public static float ReadScalar(ParticleCollection particles, int attribute, int p) => ParticleInitUtil.ReadScalar(particles, attribute, p);

	public static void WriteScalar(ParticleCollection particles, int attribute, int p, float value) => ParticleInitUtil.WriteScalar(particles, attribute, p, value);

	public static void SetControlPointComponent(ParticleCollection particles, int cp, int component, float value) {
		Vector3 v = particles.GetControlPointAtCurrentTime(cp);
		switch (component) {
			case 0: v.X = value; break;
			case 1: v.Y = value; break;
			default: v.Z = value; break;
		}
		particles.SetControlPoint(cp, v);
	}

	public static float Component(in Vector3 v, int component) => component switch { 0 => v.X, 1 => v.Y, _ => v.Z };

	public static ref T Context<T>(Span<byte> context) where T : unmanaged => ref System.Runtime.InteropServices.MemoryMarshal.AsRef<T>(context);

	public static int ClampCP(int cp) => Math.Clamp(cp, 0, MAX_PARTICLE_CONTROL_POINTS - 1);

	public static bool MatricesAreEqual(in Matrix3x4 a, in Matrix3x4 b, float tolerance) {
		for (int r = 0; r < 3; r++) {
			for (int c = 0; c < 4; c++) {
				if (MathF.Abs(a[r, c] - b[r, c]) > tolerance)
					return false;
			}
		}
		return true;
	}

	public static Vector3 Transform(in Vector3 v, in Matrix3x4 m) {
		MathLib.VectorTransform(v, m, out Vector3 result);
		return result;
	}

	public static Vector3 Rotate(in Vector3 v, in Matrix3x4 m) {
		MathLib.VectorRotate(v, m, out Vector3 result);
		return result;
	}

	public static Vector3 NormalizeOrZero(Vector3 v) {
		float length = v.Length();
		return length > 0.0f ? v / length : Vector3.Zero;
	}

	public static float SimpleSplineRemapValClamped(float value, float a, float b, float c, float d) {
		if (a == b)
			return value >= b ? d : c;
		float t = Math.Clamp((value - a) / (b - a), 0.0f, 1.0f);
		return c + (d - c) * SimpleSpline(t);
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

public abstract class C_OP_FadeAndKillBase : ParticleOperatorInstance
{
	public float StartFadeInTime;
	public float EndFadeInTime;
	public float StartFadeOutTime;
	public float EndFadeOutTime;
	public float StartAlpha;
	public float EndAlpha;

	protected static DmxElementUnpackStructure<T> Unpack<T>() where T : C_OP_FadeAndKillBase
		=> ParticleOperatorUnpack.Begin<T>()
			.Float("start_alpha", "1", (o, v) => o.StartAlpha = v)
			.Float("end_alpha", "0", (o, v) => o.EndAlpha = v)
			.Float("start_fade_in_time", "0", (o, v) => o.StartFadeInTime = v)
			.Float("end_fade_in_time", "0.5", (o, v) => o.EndFadeInTime = v)
			.Float("start_fade_out_time", "0.5", (o, v) => o.StartFadeOutTime = v)
			.Float("end_fade_out_time", "1", (o, v) => o.EndFadeOutTime = v);

	public override uint GetReadInitialAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		if (EndFadeInTime < StartFadeInTime)
			EndFadeInTime = StartFadeInTime;
		if (EndFadeOutTime < StartFadeOutTime)
			EndFadeOutTime = StartFadeOutTime;
		if (StartFadeOutTime < StartFadeInTime)
			(StartFadeInTime, StartFadeOutTime) = (StartFadeOutTime, StartFadeInTime);
		if (EndFadeOutTime < EndFadeInTime)
			(EndFadeInTime, EndFadeOutTime) = (EndFadeOutTime, EndFadeInTime);
	}

	protected static float SplineRemapClamped(float value, float a, float oneOverRange, float c, float deltaC)
		=> c + deltaC * ParticleOpUtil.SimpleSpline(Math.Clamp((value - a) * oneOverRange, 0.0f, 1.0f));

	protected bool FadeParticle(ParticleCollection particles, int p) {
		float age = ParticleOpUtil.Age(particles, p);
		float lifetime = ParticleOpUtil.Lifetime(particles, p);
		bool kill = lifetime <= age;
		float t = age / lifetime;
		if (kill)
			return true;

		float initialAlpha = particles.GetInitialFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA, p);
		ref float alpha = ref particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_ALPHA, p);
		if (StartFadeInTime <= t && t < EndFadeInTime) {
			float goal = initialAlpha * StartAlpha;
			alpha = SplineRemapClamped(t, StartFadeInTime, 1.0f / (EndFadeInTime - StartFadeInTime), goal, initialAlpha - goal);
		}
		if (StartFadeOutTime <= t && t < EndFadeOutTime) {
			float goal = initialAlpha * EndAlpha;
			alpha = SplineRemapClamped(t, StartFadeOutTime, 1.0f / (EndFadeOutTime - StartFadeOutTime), initialAlpha, goal - initialAlpha);
		}
		return false;
	}
}

public sealed class C_OP_FadeAndKill : C_OP_FadeAndKillBase
{
	public static readonly ParticleOperatorDefinition<C_OP_FadeAndKill> Definition = new("Alpha Fade and Decay", ParticleOperatorId.Generic, false, Unpack<C_OP_FadeAndKill>());

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int limit = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < limit; p++) {
			if (FadeParticle(particles, p))
				particles.KillParticle(p);
		}
	}
}

public sealed class C_OP_FadeAndKillForTracers : C_OP_FadeAndKillBase
{
	public static readonly ParticleOperatorDefinition<C_OP_FadeAndKillForTracers> Definition = new("Alpha Fade and Decay for Tracers", ParticleOperatorId.Generic, false, Unpack<C_OP_FadeAndKillForTracers>());

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK | PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_TRAIL_LENGTH_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_TRAIL_LENGTH_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int limit = ParticleOpUtil.PaddedCount(particles);
		float previousTime = particles.CurTime - particles.Dt;
		for (int p = 0; p < limit; p++) {
			if (!FadeParticle(particles, p))
				continue;

			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			float lifetime = ParticleOpUtil.Lifetime(particles, p);
			float previousAge = previousTime - creationTime;
			if (previousAge < lifetime) {
				float fraction = (lifetime - previousAge) / particles.Dt;
				Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
				Vector3 previous = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, (position - previous) * fraction + previous);
				particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_TRAIL_LENGTH, p) *= fraction;
			}
			else
				particles.KillParticle(p);
		}
	}
}

public sealed class C_OP_FadeIn : ParticleOperatorInstance
{
	public float FadeInTimeMin;
	public float FadeInTimeMax;
	public float FadeInTimeExp;
	public bool Proportional;

	public static readonly ParticleOperatorDefinition<C_OP_FadeIn> Definition = new("Alpha Fade In Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_FadeIn>()
			.Float("fade in time min", ".25", (o, v) => o.FadeInTimeMin = v)
			.Float("fade in time max", ".25", (o, v) => o.FadeInTimeMax = v)
			.Float("fade in time exponent", "1", (o, v) => o.FadeInTimeExp = v)
			.Bool("proportional 0/1", "1", (o, v) => o.Proportional = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;
	public override uint GetReadInitialAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float fadeInTime = ParticleOpUtil.PowFixedPoint(particles.OperatorRandomFloat(p), FadeInTimeExp) * (FadeInTimeMax - FadeInTimeMin) + FadeInTimeMin;
			float lifeTime = ParticleOpUtil.Age(particles, p);
			if (Proportional)
				lifeTime = Math.Max(0.0f, Math.Min(1.0f, lifeTime / ParticleOpUtil.Lifetime(particles, p)));
			if (fadeInTime <= lifeTime)
				continue;

			float t = Math.Clamp(lifeTime / fadeInTime, 0.0f, 1.0f);
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_ALPHA, p) = ParticleOpUtil.SimpleSpline(t) * particles.GetInitialFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA, p);
		}
	}
}

public sealed class C_OP_FadeOut : ParticleOperatorInstance
{
	public float FadeOutTimeMin;
	public float FadeOutTimeMax;
	public float FadeOutTimeExp;
	public float FadeBias;
	public bool Proportional;
	public bool EaseInAndOut;
	float BiasParam;

	public static readonly ParticleOperatorDefinition<C_OP_FadeOut> Definition = new("Alpha Fade Out Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_FadeOut>()
			.Float("fade out time min", ".25", (o, v) => o.FadeOutTimeMin = v)
			.Float("fade out time max", ".25", (o, v) => o.FadeOutTimeMax = v)
			.Float("fade out time exponent", "1", (o, v) => o.FadeOutTimeExp = v)
			.Bool("proportional 0/1", "1", (o, v) => o.Proportional = v)
			.Bool("ease in and out", "1", (o, v) => o.EaseInAndOut = v)
			.Float("fade bias", "0.5", (o, v) => o.FadeBias = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;
	public override uint GetReadInitialAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		BiasParam = ParticleOpUtil.PreCalcBiasParameter(FadeBias == 0.0f ? 0.5f : FadeBias);
		if (FadeOutTimeMin == 0.0f && FadeOutTimeMax == 0.0f) {
			FadeOutTimeMin = 1.1920929e-07f;
			FadeOutTimeMax = 1.1920929e-07f;
		}
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float fadeOutTime = ParticleOpUtil.PowFixedPoint(particles.OperatorRandomFloat(p), FadeOutTimeExp) * (FadeOutTimeMax - FadeOutTimeMin) + FadeOutTimeMin;
			float lifetime = ParticleOpUtil.Lifetime(particles, p);
			float lifeTime = ParticleOpUtil.Age(particles, p);
			float baseTime = lifetime;
			if (Proportional) {
				lifeTime /= lifetime;
				baseTime = 1.0f;
			}

			float start = baseTime - fadeOutTime;
			float span = baseTime - start;
			if (lifeTime <= start)
				continue;

			float t = Math.Clamp((lifeTime - start) / span, 0.0f, 1.0f);
			float initialAlpha = particles.GetInitialFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA, p);
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_ALPHA, p) = EaseInAndOut
				? Math.Max(0.0f, ParticleOpUtil.SimpleSpline(t) * (0.0f - initialAlpha) + initialAlpha)
				: (1.0f - ParticleOpUtil.Bias(t, BiasParam)) * initialAlpha;
		}
	}
}

public sealed class C_OP_FadeInSimple : ParticleOperatorInstance
{
	public float FadeInTime;

	public static readonly ParticleOperatorDefinition<C_OP_FadeInSimple> Definition = new("Alpha Fade In Simple", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_FadeInSimple>()
			.Float("proportional fade in time", ".25", (o, v) => o.FadeInTime = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadInitialAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float t = Math.Max(0.0f, Math.Min(1.0f, ParticleOpUtil.NormalizedAge(particles, p)));
			if (FadeInTime <= t)
				continue;
			float frac = Math.Min(1.0f, Math.Max(0.0f, t / FadeInTime));
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_ALPHA, p) = ParticleOpUtil.SimpleSpline(frac) * particles.GetInitialFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA, p);
		}
	}
}

public sealed class C_OP_FadeOutSimple : ParticleOperatorInstance
{
	public float FadeOutTime;

	public static readonly ParticleOperatorDefinition<C_OP_FadeOutSimple> Definition = new("Alpha Fade Out Simple", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_FadeOutSimple>()
			.Float("proportional fade out time", ".25", (o, v) => o.FadeOutTime = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadInitialAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float start = 1.0f - FadeOutTime;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float t = ParticleOpUtil.NormalizedAge(particles, p);
			if (t <= start)
				continue;
			float frac = Math.Clamp((t - start) / FadeOutTime, 0.0f, 1.0f);
			ref float alpha = ref particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_ALPHA, p);
			alpha = Math.Min((1.0f - ParticleOpUtil.SimpleSpline(frac)) * particles.GetInitialFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA, p), alpha);
		}
	}
}

public sealed class C_OP_ClampScalar : ParticleOperatorInstance
{
	public int FieldOutput;
	public float OutputMin;
	public float OutputMax;

	public static readonly ParticleOperatorDefinition<C_OP_ClampScalar> Definition = new("Clamp Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_ClampScalar>()
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 0;
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		for (int p = 0; p < particles.ActiveParticles; p++) {
			ref float output = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			float clamped = Math.Min(Math.Max(output, OutputMin), OutputMax);
			output = (clamped - output) * opStrength + output;
		}
	}
}

public sealed class C_OP_ClampVector : ParticleOperatorInstance
{
	public int FieldOutput;
	public Vector3 OutputMin;
	public Vector3 OutputMax;

	public static readonly ParticleOperatorDefinition<C_OP_ClampVector> Definition = new("Clamp Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_ClampVector>()
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Vector3("output minimum", "0 0 0", (o, v) => o.OutputMin = v)
			.Vector3("output maximum", "1 1 1", (o, v) => o.OutputMax = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 0;
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 output = ParticleOpUtil.GetVector(particles, FieldOutput, p);
			Vector3 clamped = Vector3.Min(Vector3.Max(output, OutputMin), OutputMax);
			ParticleOpUtil.SetVector(particles, FieldOutput, p, (clamped - output) * opStrength + output);
		}
	}
}

public sealed class C_OP_OscillateScalar : ParticleOperatorInstance
{
	public float RateMin;
	public float RateMax;
	public float FrequencyMin;
	public float FrequencyMax;
	public int Field;
	public bool Proportional;
	public bool ProportionalOp;
	public float StartTimeMin;
	public float StartTimeMax;
	public float EndTimeMin;
	public float EndTimeMax;
	public float OscMult;
	public float OscAdd;

	public static readonly ParticleOperatorDefinition<C_OP_OscillateScalar> Definition = new("Oscillate Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_OscillateScalar>()
			.Int("oscillation field", "7", (o, v) => o.Field = v)
			.Float("oscillation rate min", "0", (o, v) => o.RateMin = v)
			.Float("oscillation rate max", "0", (o, v) => o.RateMax = v)
			.Float("oscillation frequency min", "1", (o, v) => o.FrequencyMin = v)
			.Float("oscillation frequency max", "1", (o, v) => o.FrequencyMax = v)
			.Bool("proportional 0/1", "1", (o, v) => o.Proportional = v)
			.Float("start time min", "0", (o, v) => o.StartTimeMin = v)
			.Float("start time max", "0", (o, v) => o.StartTimeMax = v)
			.Float("end time min", "1", (o, v) => o.EndTimeMin = v)
			.Float("end time max", "1", (o, v) => o.EndTimeMax = v)
			.Bool("start/end proportional", "1", (o, v) => o.ProportionalOp = v)
			.Float("oscillation multiplier", "2", (o, v) => o.OscMult = v)
			.Float("oscillation start phase", ".5", (o, v) => o.OscAdd = v));

	public override uint GetWrittenAttributes() => 1u << Field;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;
	public override void InitParams(ParticleSystemDefinition def, Element element) => Field = ParticleOpUtil.ClampField(Field);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int offset = particles.OperatorRandomSampleOffset();
		float scale = opStrength * particles.Dt;
		float cosFactor = OscMult * particles.CurTime + OscAdd;
		bool clamp = (ParticleOpUtil.ATTRIBUTES_WHICH_ARE_0_TO_1 & (1 << Field)) != 0;

		for (int p = 0; p < particles.ActiveParticles; p++) {
			float lifetime = ParticleOpUtil.Lifetime(particles, p);
			float age = ParticleOpUtil.Age(particles, p);
			float lifeTime = ProportionalOp ? age / lifetime : age;
			float startTime = particles.OperatorRandomFloat(p, offset + 11) * (StartTimeMax - StartTimeMin) + StartTimeMin;
			float endTime = particles.OperatorRandomFloat(p, offset + 12) * (EndTimeMax - EndTimeMin) + EndTimeMin;
			if (!(lifetime > 0.0f && startTime <= lifeTime && lifeTime < endTime))
				continue;

			float frequency = particles.OperatorRandomFloat(p, offset) * (FrequencyMax - FrequencyMin) + FrequencyMin;
			float rate = particles.OperatorRandomFloat(p, offset + 1) * (RateMax - RateMin) + RateMin;
			float cos = Proportional ? age / lifetime * frequency * OscMult + OscAdd : frequency * cosFactor;
			ref float value = ref particles.GetFloatAttributeRefForWrite(Field, p);
			float result = ParticleOpUtil.SinEst01(cos) * rate * scale + value;
			value = clamp ? Math.Max(Math.Min(result, 1.0f), 0.0f) : result;
		}
	}
}

public sealed class C_OP_OscillateScalarSimple : ParticleOperatorInstance
{
	public float Rate;
	public float Frequency;
	public int Field;
	public float OscMult;
	public float OscAdd;
	float MinCmp;
	float MaxCmp;

	public static readonly ParticleOperatorDefinition<C_OP_OscillateScalarSimple> Definition = new("Oscillate Scalar Simple", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_OscillateScalarSimple>()
			.Int("oscillation field", "7", (o, v) => o.Field = v)
			.Float("oscillation rate", "0", (o, v) => o.Rate = v)
			.Float("oscillation frequency", "1", (o, v) => o.Frequency = v)
			.Float("oscillation multiplier", "2", (o, v) => o.OscMult = v)
			.Float("oscillation start phase", ".5", (o, v) => o.OscAdd = v));

	public override uint GetWrittenAttributes() => 1u << Field;
	public override uint GetReadAttributes() => 0;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		Field = ParticleOpUtil.ClampField(Field);
		MinCmp = -float.MaxValue;
		MaxCmp = float.MaxValue;
		if ((ParticleOpUtil.ATTRIBUTES_WHICH_ARE_0_TO_1 & (1 << Field)) != 0) {
			MinCmp = 0.0f;
			MaxCmp = 1.0f;
		}
		else if ((ParticleOpUtil.ATTRIBUTES_WHICH_ARE_SIZE & (1 << Field)) != 0)
			MinCmp = 0.0f;
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float amount = ParticleOpUtil.SinEst01((OscMult * particles.CurTime + OscAdd) * Frequency) * (opStrength * particles.Dt * Rate);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			ref float value = ref particles.GetFloatAttributeRefForWrite(Field, p);
			value = Math.Max(Math.Min(value + amount, MaxCmp), MinCmp);
		}
	}
}

public sealed class C_OP_OscillateVector : ParticleOperatorInstance
{
	public Vector3 RateMin;
	public Vector3 RateMax;
	public Vector3 FrequencyMin;
	public Vector3 FrequencyMax;
	public int Field;
	public bool Proportional;
	public bool ProportionalOp;
	public float StartTimeMin;
	public float StartTimeMax;
	public float EndTimeMin;
	public float EndTimeMax;
	public float OscMult;
	public float OscAdd;

	public static readonly ParticleOperatorDefinition<C_OP_OscillateVector> Definition = new("Oscillate Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_OscillateVector>()
			.Int("oscillation field", "0", (o, v) => o.Field = v)
			.Vector3("oscillation rate min", "0 0 0", (o, v) => o.RateMin = v)
			.Vector3("oscillation rate max", "0 0 0", (o, v) => o.RateMax = v)
			.Vector3("oscillation frequency min", "1 1 1", (o, v) => o.FrequencyMin = v)
			.Vector3("oscillation frequency max", "1 1 1", (o, v) => o.FrequencyMax = v)
			.Bool("proportional 0/1", "1", (o, v) => o.Proportional = v)
			.Float("start time min", "0", (o, v) => o.StartTimeMin = v)
			.Float("start time max", "0", (o, v) => o.StartTimeMax = v)
			.Float("end time min", "1", (o, v) => o.EndTimeMin = v)
			.Float("end time max", "1", (o, v) => o.EndTimeMax = v)
			.Bool("start/end proportional", "1", (o, v) => o.ProportionalOp = v)
			.Float("oscillation multiplier", "2", (o, v) => o.OscMult = v)
			.Float("oscillation start phase", ".5", (o, v) => o.OscAdd = v));

	public override uint GetWrittenAttributes() => 1u << Field;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;
	public override void InitParams(ParticleSystemDefinition def, Element element) => Field = ParticleOpUtil.ClampField(Field);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int offset = particles.OperatorRandomSampleOffset();
		float scale = opStrength * particles.Dt;
		float cosFactor = OscMult * particles.CurTime + OscAdd;
		Vector3 frequencyWidth = FrequencyMax - FrequencyMin;
		Vector3 rateWidth = RateMax - RateMin;

		for (int p = 0; p < particles.ActiveParticles; p++) {
			float lifetime = ParticleOpUtil.Lifetime(particles, p);
			float age = ParticleOpUtil.Age(particles, p);
			float lifeTime = ProportionalOp ? age / lifetime : age;
			float startTime = particles.OperatorRandomFloat(p, offset + 11) * (StartTimeMax - StartTimeMin) + StartTimeMin;
			float endTime = particles.OperatorRandomFloat(p, offset + 12) * (EndTimeMax - EndTimeMin) + EndTimeMin;
			if (!(lifetime > 0.0f && startTime <= lifeTime && lifeTime < endTime))
				continue;

			Vector3 frequency = new Vector3(particles.OperatorRandomFloat(p, offset + 8), particles.OperatorRandomFloat(p, offset + 12), particles.OperatorRandomFloat(p, offset + 15)) * frequencyWidth + FrequencyMin;
			Vector3 rate = new Vector3(particles.OperatorRandomFloat(p, offset + 3), particles.OperatorRandomFloat(p, offset + 7), particles.OperatorRandomFloat(p, offset + 9)) * rateWidth + RateMin;
			Vector3 cos = Proportional ? frequency * (age / lifetime) * OscMult + new Vector3(OscAdd) : frequency * cosFactor;
			Vector3 sin = new(ParticleOpUtil.SinEst01(cos.X), ParticleOpUtil.SinEst01(cos.Y), ParticleOpUtil.SinEst01(cos.Z));
			Vector3 value = sin * rate * scale + ParticleOpUtil.GetVector(particles, Field, p);
			if (Field == PARTICLE_ATTRIBUTE_TINT_RGB)
				value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
			ParticleOpUtil.SetVector(particles, Field, p, value);
		}
	}
}

public sealed class C_OP_OscillateVectorSimple : ParticleOperatorInstance
{
	public Vector3 Rate;
	public Vector3 Frequency;
	public int Field;
	public float OscMult;
	public float OscAdd;

	public static readonly ParticleOperatorDefinition<C_OP_OscillateVectorSimple> Definition = new("Oscillate Vector Simple", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_OscillateVectorSimple>()
			.Int("oscillation field", "0", (o, v) => o.Field = v)
			.Vector3("oscillation rate", "0 0 0", (o, v) => o.Rate = v)
			.Vector3("oscillation frequency", "1 1 1", (o, v) => o.Frequency = v)
			.Float("oscillation multiplier", "2", (o, v) => o.OscMult = v)
			.Float("oscillation start phase", ".5", (o, v) => o.OscAdd = v));

	public override uint GetWrittenAttributes() => 1u << Field;
	public override uint GetReadAttributes() => 0;
	public override void InitParams(ParticleSystemDefinition def, Element element) => Field = ParticleOpUtil.ClampField(Field);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float cosFactor = OscMult * particles.CurTime + OscAdd;
		Vector3 cos = Frequency * cosFactor;
		Vector3 amount = new Vector3(ParticleOpUtil.SinEst01(cos.X), ParticleOpUtil.SinEst01(cos.Y), ParticleOpUtil.SinEst01(cos.Z)) * Rate * (opStrength * particles.Dt);
		bool clamp = (ParticleOpUtil.ATTRIBUTES_WHICH_ARE_COLOR_AND_OPACITY & (1 << Field)) != 0;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 value = ParticleOpUtil.GetVector(particles, Field, p) + amount;
			if (clamp)
				value = Vector3.Max(Vector3.Min(value, Vector3.One), Vector3.Zero);
			ParticleOpUtil.SetVector(particles, Field, p, value);
		}
	}
}

public sealed class C_OP_InterpolateRadius : ParticleOperatorInstance
{
	public float StartTime;
	public float EndTime;
	public float StartScale;
	public float EndScale;
	public bool EaseInAndOut;
	public float ScaleBias;
	float BiasParam;

	public static readonly ParticleOperatorDefinition<C_OP_InterpolateRadius> Definition = new("Radius Scale", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_InterpolateRadius>()
			.Float("start_time", "0", (o, v) => o.StartTime = v)
			.Float("end_time", "1", (o, v) => o.EndTime = v)
			.Float("radius_start_scale", "1", (o, v) => o.StartScale = v)
			.Float("radius_end_scale", "1", (o, v) => o.EndScale = v)
			.Bool("ease_in_and_out", "0", (o, v) => o.EaseInAndOut = v)
			.Float("scale_bias", "0.5", (o, v) => o.ScaleBias = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_RADIUS_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadInitialAttributes() => PARTICLE_ATTRIBUTE_RADIUS_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		if (ScaleBias == 0.0f)
			ScaleBias = 0.5f;
		BiasParam = ParticleOpUtil.PreCalcBiasParameter(ScaleBias);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (!(StartTime < EndTime))
			return;

		float oneOverDuration = 1.0f / (EndTime - StartTime);
		float scaleWidth = EndScale - StartScale;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float lifetime = ParticleOpUtil.Lifetime(particles, p);
			float t = ParticleOpUtil.Age(particles, p) / lifetime;
			if (!(lifetime > 0.0f && StartTime <= t && t < EndTime))
				continue;

			float frac = (t - StartTime) * oneOverDuration;
			if (EaseInAndOut)
				frac = ParticleOpUtil.SimpleSpline(frac);
			else if (ScaleBias != 0.5f)
				frac = ParticleOpUtil.Bias(frac, BiasParam);
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_RADIUS, p) = (frac * scaleWidth + StartScale) * particles.GetInitialFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, p);
		}
	}
}

public sealed class C_OP_ColorInterpolate : ParticleOperatorInstance
{
	public Color ColorFade;
	public float FadeStartTime;
	public float FadeEndTime;
	public int FieldOutput;
	public bool EaseInOut;
	Vector3 NormColorFade;

	public static readonly ParticleOperatorDefinition<C_OP_ColorInterpolate> Definition = new("Color Fade", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_ColorInterpolate>()
			.Color("color_fade", "255 255 255 255", (o, v) => o.ColorFade = v)
			.Float("fade_start_time", "0", (o, v) => o.FadeStartTime = v)
			.Float("fade_end_time", "1", (o, v) => o.FadeEndTime = v)
			.Bool("ease_in_and_out", "1", (o, v) => o.EaseInOut = v)
			.Int("output field", "6", (o, v) => o.FieldOutput = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadInitialAttributes() => 1u << FieldOutput;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		NormColorFade = new Vector3(ColorFade.R, ColorFade.G, ColorFade.B) * 0.003921569f;
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (FadeEndTime == FadeStartTime)
			return;

		float oneOverDuration = 1.0f / (FadeEndTime - FadeStartTime);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float lifetime = ParticleOpUtil.Lifetime(particles, p);
			if (!(lifetime > 0.0f))
				continue;

			float t = Math.Min(1.0f, Math.Max(0.0f, (ParticleOpUtil.Age(particles, p) / lifetime - FadeStartTime) * oneOverDuration));
			if (EaseInOut)
				t = ParticleOpUtil.SimpleSpline(t);
			Vector3 initial = ParticleOpUtil.GetInitialVector(particles, FieldOutput, p);
			ParticleOpUtil.SetVector(particles, FieldOutput, p, (NormColorFade - initial) * t + initial);
		}
	}
}

public sealed class C_OP_DifferencePreviousParticle : ParticleOperatorInstance
{
	public int FieldInput;
	public int FieldOutput;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;
	public bool ScaleInitialRange;
	public bool ActiveRange;
	public bool SetPreviousParticle;

	public static readonly ParticleOperatorDefinition<C_OP_DifferencePreviousParticle> Definition = new("Remap Difference of Sequential Particle Vector to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_DifferencePreviousParticle>()
			.Float("difference minimum", "0", (o, v) => o.InputMin = v)
			.Float("difference maximum", "128", (o, v) => o.InputMax = v)
			.Int("input field", "0", (o, v) => o.FieldInput = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("only active within specified difference", "0", (o, v) => o.ActiveRange = v)
			.Bool("also set ouput to previous particle", "0", (o, v) => o.SetPreviousParticle = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 1u << FieldInput;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldInput = ParticleOpUtil.ClampField(FieldInput);
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float min = OutputMin, max = OutputMax;
		if (ParticleOpUtil.IsUnitAttribute(FieldOutput)) {
			min = Math.Clamp(OutputMin, 0.0f, 1.0f);
			max = Math.Clamp(OutputMax, 0.0f, 1.0f);
		}

		Vector3 previous = ParticleInitUtil.Vec3Invalid;
		int previousParticle = 0;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 input = ParticleOpUtil.GetVector(particles, FieldInput, p);
			if (previous != ParticleInitUtil.Vec3Invalid) {
				float distance = Vector3.Distance(previous, input);
				if (!(ActiveRange && (distance < InputMin || distance > InputMax))) {
					float output = ParticleOpUtil.RemapClamped(distance, InputMin, InputMax, min, max);
					float output2 = output;
					if (ScaleInitialRange)
						output *= particles.GetFloatAttributeValue(FieldOutput, p);
					particles.GetFloatAttributeRefForWrite(FieldOutput, p) = output;
					if (SetPreviousParticle)
						particles.GetFloatAttributeRefForWrite(FieldOutput, previousParticle) *= output2;
				}
				else
					continue;
			}
			previous = input;
			previousParticle = p;
		}
	}
}

public sealed class C_OP_RemapScalar : ParticleOperatorInstance
{
	public int FieldInput;
	public int FieldOutput;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;

	public static readonly ParticleOperatorDefinition<C_OP_RemapScalar> Definition = new("Remap Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapScalar>()
			.Int("input field", "7", (o, v) => o.FieldInput = v)
			.Float("input minimum", "0", (o, v) => o.InputMin = v)
			.Float("input maximum", "1", (o, v) => o.InputMax = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 1u << FieldInput;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldInput = ParticleOpUtil.ClampField(FieldInput);
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float min = OutputMin, max = OutputMax;
		if (ParticleOpUtil.IsUnitAttribute(FieldOutput)) {
			min = Math.Clamp(OutputMin, 0.0f, 1.0f);
			max = Math.Clamp(OutputMax, 0.0f, 1.0f);
		}
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float output = ParticleOpUtil.RemapClamped(particles.GetFloatAttributeValue(FieldInput, p), InputMin, InputMax, min, max);
			ref float value = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			value = ParticleOpUtil.Lerp(opStrength, value, output);
		}
	}
}

public sealed class C_OP_LerpScalar : ParticleOperatorInstance
{
	public int FieldOutput;
	public float Output;
	public float StartTime;
	public float EndTime;

	public static readonly ParticleOperatorDefinition<C_OP_LerpScalar> Definition = new("Lerp Initial Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_LerpScalar>()
			.Float("start time", "0", (o, v) => o.StartTime = v)
			.Float("end time", "1", (o, v) => o.EndTime = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("value to lerp to", "1", (o, v) => o.Output = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadInitialAttributes() => 1u << FieldOutput;
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float age = ParticleOpUtil.Age(particles, p) / (ParticleOpUtil.Lifetime(particles, p) + 1.1920929e-07f);
			if (age < StartTime || age > EndTime)
				continue;
			float output = ParticleOpUtil.RemapClamped(age, StartTime, EndTime, particles.GetInitialFloatAttributeValue(FieldOutput, p), Output);
			ref float value = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			value = ParticleOpUtil.Lerp(opStrength, value, output);
		}
	}
}

public sealed class C_OP_LerpEndCapScalar : ParticleOperatorInstance
{
	public int FieldOutput;
	public float Output;
	public float LerpTime;

	public static readonly ParticleOperatorDefinition<C_OP_LerpEndCapScalar> Definition = new("Lerp EndCap Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_LerpEndCapScalar>()
			.Float("lerp time", "1", (o, v) => o.LerpTime = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("value to lerp to", "1", (o, v) => o.Output = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 0;
	public override uint GetReadInitialAttributes() => 1u << FieldOutput;
	public override int GetRequiredContextBytes() => sizeof(float);
	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => BitConverter.TryWriteBytes(context, -float.MaxValue);
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (!particles.InEndCap)
			return;
		ref float endCapStart = ref System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(context)[0];
		if (endCapStart < 0.0f) {
			endCapStart = particles.CurTime;
			for (int p = 0; p < particles.ActiveParticles; p++)
				particles.GetInitialFloatAttributeRefForWrite(FieldOutput, p) = particles.GetFloatAttributeValue(FieldOutput, p);
		}
		float age = (particles.CurTime - endCapStart) / (LerpTime + 1.1920929e-07f);
		if (age > 1.0f)
			return;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float output = ParticleOpUtil.RemapClamped(age, 0.0f, 1.0f, particles.GetInitialFloatAttributeValue(FieldOutput, p), Output);
			ref float value = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			value = ParticleOpUtil.Lerp(opStrength, value, output);
		}
	}
}

public sealed class C_OP_LerpEndCapVector : ParticleOperatorInstance
{
	public int FieldOutput;
	public Vector3 Output;
	public float LerpTime;

	public static readonly ParticleOperatorDefinition<C_OP_LerpEndCapVector> Definition = new("Lerp EndCap Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_LerpEndCapVector>()
			.Float("lerp time", "1", (o, v) => o.LerpTime = v)
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Vector3("value to lerp to", "0 0 0", (o, v) => o.Output = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 0;
	public override uint GetReadInitialAttributes() => 1u << FieldOutput;
	public override int GetRequiredContextBytes() => sizeof(float);
	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => BitConverter.TryWriteBytes(context, -float.MaxValue);
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (!particles.InEndCap)
			return;
		ref float endCapStart = ref System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(context)[0];
		if (endCapStart < 0.0f) {
			endCapStart = particles.CurTime;
			for (int p = 0; p < particles.ActiveParticles; p++)
				particles.GetInitialFloatAttributeRefForWrite(FieldOutput, p) = particles.GetFloatAttributeValue(FieldOutput, p);
		}
		float age = (particles.CurTime - endCapStart) / (LerpTime + 1.1920929e-07f);
		if (age > 1.0f)
			return;
		age *= opStrength;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 start = ParticleOpUtil.GetInitialVector(particles, FieldOutput, p);
			ParticleOpUtil.SetVector(particles, FieldOutput, p, Vector3.Lerp(start, Output, age));
		}
	}
}

public sealed class C_OP_LerpVector : ParticleOperatorInstance
{
	public int FieldOutput;
	public Vector3 Output;
	public float StartTime;
	public float EndTime;

	public static readonly ParticleOperatorDefinition<C_OP_LerpVector> Definition = new("Lerp Initial Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_LerpVector>()
			.Float("start time", "0", (o, v) => o.StartTime = v)
			.Float("end time", "1", (o, v) => o.EndTime = v)
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Vector3("value to lerp to", "0 0 0", (o, v) => o.Output = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadInitialAttributes() => 1u << FieldOutput;
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float age = ParticleOpUtil.Age(particles, p) / (ParticleOpUtil.Lifetime(particles, p) + 1.1920929e-07f);
			if (age < StartTime || age > EndTime)
				continue;
			float percent = ParticleOpUtil.RemapClamped(age, StartTime, EndTime, 0.0f, 1.0f) * opStrength;
			Vector3 start = ParticleOpUtil.GetInitialVector(particles, FieldOutput, p);
			ParticleOpUtil.SetVector(particles, FieldOutput, p, Vector3.Lerp(start, Output, percent));
		}
	}
}

public sealed class C_OP_RemapSpeed : ParticleOperatorInstance
{
	public int FieldOutput;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;
	public bool ScaleInitialRange;
	public bool ScaleCurrent;

	public static readonly ParticleOperatorDefinition<C_OP_RemapSpeed> Definition = new("Remap Speed to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapSpeed>()
			.Float("input minimum", "0", (o, v) => o.InputMin = v)
			.Float("input maximum", "1", (o, v) => o.InputMax = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("output is scalar of current value", "0", (o, v) => o.ScaleCurrent = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadInitialAttributes() => 1u << FieldOutput;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		InputMin = Math.Max(ParticleOpUtil.MIN_PARTICLE_SPEED, InputMin);
		InputMax = Math.Max(ParticleOpUtil.MIN_PARTICLE_SPEED, InputMax);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float min = OutputMin, max = OutputMax;
		if (ParticleOpUtil.IsUnitAttribute(FieldOutput)) {
			min = Math.Clamp(OutputMin, 0.0f, 1.0f);
			max = Math.Clamp(OutputMax, 0.0f, 1.0f);
		}
		float inputRange = InputMax == InputMin ? 1.0f : InputMax - InputMin;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 delta = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			float t = Math.Clamp((delta.Length() / particles.Dt - InputMin) / inputRange, 0.0f, 1.0f);
			float output = t * (max - min) + min;
			ref float value = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			if (ScaleInitialRange)
				output *= particles.GetInitialFloatAttributeValue(FieldOutput, p);
			if (ScaleCurrent)
				output *= value;
			value = ParticleOpUtil.Lerp(opStrength, value, output);
		}
	}
}

public sealed class C_OP_RemapSpeedtoCP : ParticleOperatorInstance
{
	public int InControlPoint;
	public int OutControlPoint;
	public int Field;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;

	public static readonly ParticleOperatorDefinition<C_OP_RemapSpeedtoCP> Definition = new("Remap CP Speed to CP", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapSpeedtoCP>()
			.Int("input control point", "0", (o, v) => o.InControlPoint = v)
			.Float("input minimum", "0", (o, v) => o.InputMin = v)
			.Float("input maximum", "1", (o, v) => o.InputMax = v)
			.Int("output control point", "-1", (o, v) => o.OutControlPoint = v)
			.Int("Output field 0-2 X/Y/Z", "0", (o, v) => o.Field = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => ParticleOpUtil.CPMask(InControlPoint) | ParticleOpUtil.CPMask(OutControlPoint);
	public override bool ShouldRunBeforeEmitters() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		if (InControlPoint == OutControlPoint)
			OutControlPoint = -1;
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (OutControlPoint < 0)
			return;
		particles.GetControlPointAtPrevTime(InControlPoint, out Vector3 prev);
		float speed = (particles.GetControlPointAtCurrentTime(InControlPoint) - prev).Length() / particles.PreviousDt;
		float output = ParticleOpUtil.RemapClamped(speed, InputMin, InputMax, OutputMin, OutputMax);
		ParticleOpUtil.SetControlPointComponent(particles, OutControlPoint, Math.Clamp(Field, 0, 2), output);
	}
}

public sealed class C_OP_RemapBoundingVolumetoCP : ParticleOperatorInstance
{
	public int OutControlPoint;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;

	public static readonly ParticleOperatorDefinition<C_OP_RemapBoundingVolumetoCP> Definition = new("Remap Particle BBox Volume to CP", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapBoundingVolumetoCP>()
			.Float("input volume minimum in cubic units", "0", (o, v) => o.InputMin = v)
			.Float("input volume maximum in cubic units", "128", (o, v) => o.InputMax = v)
			.Int("output control point", "-1", (o, v) => o.OutControlPoint = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => ParticleOpUtil.CPMask(OutControlPoint);
	public override bool ShouldRunBeforeEmitters() => true;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (OutControlPoint < 0)
			return;
		particles.GetBounds(out Vector3 min, out Vector3 max);
		Vector3 size = max - min;
		float volume = MathF.Pow(size.X * size.Y * size.Z, 0.33333333f);
		particles.SetControlPoint(OutControlPoint, new Vector3(ParticleOpUtil.RemapClamped(volume, InputMin, InputMax, OutputMin, OutputMax), 0, 0));
	}
}

public sealed class C_OP_RemapAverageScalarValuetoCP : ParticleOperatorInstance
{
	public int OutControlPoint;
	public int Field;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;

	public static readonly ParticleOperatorDefinition<C_OP_RemapAverageScalarValuetoCP> Definition = new("Remap Average Scalar Value to CP", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapAverageScalarValuetoCP>()
			.Int("Scalar field", "3", (o, v) => o.Field = v)
			.Float("input volume minimum", "0", (o, v) => o.InputMin = v)
			.Float("input volume maximum", "1", (o, v) => o.InputMax = v)
			.Int("output control point", "1", (o, v) => o.OutControlPoint = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 1u << Field;
	public override ulong GetReadControlPointMask() => ParticleOpUtil.CPMask(OutControlPoint);
	public override bool ShouldRunBeforeEmitters() => true;
	public override void InitParams(ParticleSystemDefinition def, Element element) => Field = ParticleOpUtil.ClampField(Field);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float sum = 0.0f;
		for (int p = 0; p < particles.ActiveParticles; p++)
			sum += particles.GetFloatAttributeValue(Field, p);
		float average = sum / particles.ActiveParticles;
		particles.SetControlPoint(OutControlPoint, new Vector3(ParticleOpUtil.RemapClamped(average, InputMin, InputMax, OutputMin, OutputMax), 0, 0));
	}
}

public sealed class C_OP_RampScalarLinear : ParticleOperatorInstance
{
	public float RateMin;
	public float RateMax;
	public float StartTimeMin;
	public float StartTimeMax;
	public float EndTimeMin;
	public float EndTimeMax;
	public int Field;
	public bool ProportionalOp;
	bool UsesStartEnd;
	float MinCmp;
	float MaxCmp;

	public static readonly ParticleOperatorDefinition<C_OP_RampScalarLinear> Definition = new("Ramp Scalar Linear Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RampScalarLinear>()
			.Int("ramp field", "3", (o, v) => o.Field = v)
			.Float("ramp rate min", "0", (o, v) => o.RateMin = v)
			.Float("ramp rate max", "0", (o, v) => o.RateMax = v)
			.Float("start time min", "0", (o, v) => o.StartTimeMin = v)
			.Float("start time max", "0", (o, v) => o.StartTimeMax = v)
			.Float("end time min", "1", (o, v) => o.EndTimeMin = v)
			.Float("end time max", "1", (o, v) => o.EndTimeMax = v)
			.Bool("start/end proportional", "1", (o, v) => o.ProportionalOp = v));

	public override uint GetWrittenAttributes() => 1u << Field;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		Field = ParticleOpUtil.ClampField(Field);
		UsesStartEnd = !(StartTimeMin == 0 && StartTimeMax == 0 && EndTimeMin == 1 && EndTimeMax == 1 && ProportionalOp);
		ParticleOpUtil.GetClampRange(Field, out MinCmp, out MaxCmp);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int offset = particles.OperatorRandomSampleOffset();
		float scale = opStrength * particles.Dt;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float lifetime = ParticleOpUtil.Lifetime(particles, p);
			if (!(lifetime > 0.0f))
				continue;
			if (UsesStartEnd) {
				float age = ParticleOpUtil.Age(particles, p);
				float lifeTime = ProportionalOp ? age / lifetime : age;
				float startTime = StartTimeMin + (StartTimeMax - StartTimeMin) * particles.OperatorRandomFloat(p, offset + 11);
				float endTime = EndTimeMin + (EndTimeMax - EndTimeMin) * particles.OperatorRandomFloat(p, offset + 12);
				if (!(lifeTime >= startTime && lifeTime < endTime))
					continue;
			}
			float rate = RateMin + (RateMax - RateMin) * particles.OperatorRandomFloat(p, offset);
			ref float value = ref particles.GetFloatAttributeRefForWrite(Field, p);
			value = Math.Max(Math.Min(value + rate * scale, MaxCmp), MinCmp);
		}
	}
}

public sealed class C_OP_RampScalarSpline : ParticleOperatorInstance
{
	public float RateMin;
	public float RateMax;
	public float StartTimeMin;
	public float StartTimeMax;
	public float EndTimeMin;
	public float EndTimeMax;
	public float Bias;
	public int Field;
	public bool ProportionalOp;
	public bool EaseOut;
	float MinCmp;
	float MaxCmp;

	public static readonly ParticleOperatorDefinition<C_OP_RampScalarSpline> Definition = new("Ramp Scalar Spline Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RampScalarSpline>()
			.Int("ramp field", "3", (o, v) => o.Field = v)
			.Float("ramp rate min", "0", (o, v) => o.RateMin = v)
			.Float("ramp rate max", "0", (o, v) => o.RateMax = v)
			.Float("start time min", "0", (o, v) => o.StartTimeMin = v)
			.Float("start time max", "0", (o, v) => o.StartTimeMax = v)
			.Float("end time min", "1", (o, v) => o.EndTimeMin = v)
			.Float("end time max", "1", (o, v) => o.EndTimeMax = v)
			.Bool("start/end proportional", "1", (o, v) => o.ProportionalOp = v)
			.Bool("ease out", "0", (o, v) => o.EaseOut = v)
			.Float("bias", ".5", (o, v) => o.Bias = v));

	public override uint GetWrittenAttributes() => 1u << Field;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		Field = ParticleOpUtil.ClampField(Field);
		ParticleOpUtil.GetClampRange(Field, out MinCmp, out MaxCmp);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int offset = particles.OperatorRandomSampleOffset();
		float scale = opStrength * particles.Dt;
		float biasParam = ParticleOpUtil.PreCalcBiasParameter(Bias);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float lifetime = ParticleOpUtil.Lifetime(particles, p);
			float startTime = StartTimeMin + (StartTimeMax - StartTimeMin) * particles.OperatorRandomFloat(p, offset + 11);
			float endTime = EndTimeMin + (EndTimeMax - EndTimeMin) * particles.OperatorRandomFloat(p, offset + 12);
			float age = ParticleOpUtil.Age(particles, p);
			float lifeTime = ProportionalOp ? age / lifetime : age;
			if (!(lifetime > 0.0f && lifeTime >= startTime && lifeTime < endTime))
				continue;

			float rate = RateMin + (RateMax - RateMin) * particles.OperatorRandomFloat(p, offset);
			float spline = Math.Min(1.0f, Math.Max(0.0f, (lifeTime - startTime) * (1.0f / (1.1920929e-07f + endTime - startTime))));
			if (EaseOut) {
				if (spline > 0.5f)
					spline = 1.0f - spline;
				spline *= 2.0f;
			}
			spline = ParticleOpUtil.Bias(spline, biasParam);
			ref float value = ref particles.GetFloatAttributeRefForWrite(Field, p);
			value = Math.Max(Math.Min(value + rate * spline * scale, MaxCmp), MinCmp);
		}
	}
}

public sealed class C_OP_RampScalarLinearSimple : ParticleOperatorInstance
{
	public float Rate;
	public float StartTime;
	public float EndTime;
	public int Field;
	float MinCmp;
	float MaxCmp;

	public static readonly ParticleOperatorDefinition<C_OP_RampScalarLinearSimple> Definition = new("Ramp Scalar Linear Simple", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RampScalarLinearSimple>()
			.Int("ramp field", "3", (o, v) => o.Field = v)
			.Float("ramp rate", "0", (o, v) => o.Rate = v)
			.Float("start time", "0", (o, v) => o.StartTime = v)
			.Float("end time", "1", (o, v) => o.EndTime = v));

	public override uint GetWrittenAttributes() => 1u << Field;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		Field = ParticleOpUtil.ClampField(Field);
		ParticleOpUtil.GetClampRange(Field, out MinCmp, out MaxCmp);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float amount = Rate * opStrength * particles.Dt;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float lifetime = ParticleOpUtil.Lifetime(particles, p);
			float t = ParticleOpUtil.Age(particles, p) / lifetime;
			if (!(lifetime > 0.0f && t >= StartTime && t < EndTime))
				continue;
			ref float value = ref particles.GetFloatAttributeRefForWrite(Field, p);
			value = Math.Max(Math.Min(value + amount, MaxCmp), MinCmp);
		}
	}
}

public sealed class C_OP_RampScalarSplineSimple : ParticleOperatorInstance
{
	public float Rate;
	public float StartTime;
	public float EndTime;
	public int Field;
	public bool EaseOut;
	float MinCmp;
	float MaxCmp;

	public static readonly ParticleOperatorDefinition<C_OP_RampScalarSplineSimple> Definition = new("Ramp Scalar Spline Simple", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RampScalarSplineSimple>()
			.Int("ramp field", "3", (o, v) => o.Field = v)
			.Float("ramp rate", "0", (o, v) => o.Rate = v)
			.Float("start time", "0", (o, v) => o.StartTime = v)
			.Float("end time", "1", (o, v) => o.EndTime = v)
			.Bool("ease out", "0", (o, v) => o.EaseOut = v));

	public override uint GetWrittenAttributes() => 1u << Field;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		Field = ParticleOpUtil.ClampField(Field);
		ParticleOpUtil.GetClampRange(Field, out MinCmp, out MaxCmp);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float scale = opStrength * particles.Dt;
		float ooRange = 1.0f / (1.1920929e-07f + EndTime - StartTime);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float lifetime = ParticleOpUtil.Lifetime(particles, p);
			float t = ParticleOpUtil.Age(particles, p) / lifetime;
			if (!(lifetime > 0.0f && t >= StartTime && t < EndTime))
				continue;
			float spline = Math.Min(1.0f, Math.Max(0.0f, (t - StartTime) * ooRange));
			if (EaseOut) {
				if (spline > 0.5f)
					spline = 1.0f - spline;
				spline *= 2.0f;
			}
			spline = ParticleOpUtil.SimpleSpline(spline);
			ref float value = ref particles.GetFloatAttributeRefForWrite(Field, p);
			value = Math.Max(Math.Min(value + Rate * spline * scale, MaxCmp), MinCmp);
		}
	}
}

public sealed class C_OP_Noise : ParticleOperatorInstance
{
	public int FieldOutput;
	public float OutputMin;
	public float OutputMax;
	public float NoiseScale;
	public bool Additive;

	public static readonly ParticleOperatorDefinition<C_OP_Noise> Definition = new("Noise Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_Noise>()
			.Float("noise coordinate scale", "0.1", (o, v) => o.NoiseScale = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Bool("additive", "0", (o, v) => o.Additive = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float min = OutputMin, max = OutputMax;
		if ((ParticleOpUtil.ATTRIBUTES_WHICH_ARE_ANGLES & (1 << FieldOutput)) != 0) {
			min *= 0.017453292f;
			max *= 0.017453292f;
		}
		float valueScale = (max - min) * 0.5f;
		float valueBase = valueScale + min;
		int count = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < count; p++) {
			float noise = NoiseSIMD.Noise(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) * NoiseScale);
			ref float value = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			if (Additive)
				value = noise * valueScale * particles.Dt + valueBase * particles.Dt + value;
			else
				value = noise * valueScale + valueBase;
		}
	}
}

public sealed class C_OP_VectorNoise : ParticleOperatorInstance
{
	public int FieldOutput;
	public Vector3 OutputMin;
	public Vector3 OutputMax;
	public float NoiseScale;
	public bool Additive;

	static readonly Vector3 OffsetY = new(100000.5f, 300000.25f, 9000000.75f);
	static readonly Vector3 OffsetZ = new(110000.25f, 310000.75f, 9100000.5f);

	public static readonly ParticleOperatorDefinition<C_OP_VectorNoise> Definition = new("Noise Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_VectorNoise>()
			.Float("noise coordinate scale", "0.1", (o, v) => o.NoiseScale = v)
			.Int("output field", "6", (o, v) => o.FieldOutput = v)
			.Vector3("output minimum", "0 0 0", (o, v) => o.OutputMin = v)
			.Vector3("output maximum", "1 1 1", (o, v) => o.OutputMax = v)
			.Bool("additive", "0", (o, v) => o.Additive = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 valueScale = (OutputMax - OutputMin) * 0.5f;
		Vector3 valueBase = OutputMin + valueScale;
		int count = (particles.ActiveParticles + 3) & ~3;
		for (int p = 0; p < count; p++) {
			Vector3 coord = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) * NoiseScale;
			float x = NoiseSIMD.Noise(coord);
			coord += OffsetY;
			float y = NoiseSIMD.Noise(coord);
			coord += OffsetZ;
			float z = NoiseSIMD.Noise(coord);
			Vector3 noise = valueBase + valueScale * new Vector3(x, y, z);
			if (Additive)
				noise = ParticleOpUtil.GetVector(particles, FieldOutput, p) + noise * particles.Dt;
			ParticleOpUtil.SetVector(particles, FieldOutput, p, noise);
		}
	}
}

public sealed class C_OP_VelocityDecay : ParticleOperatorInstance
{
	public float MinVelocity;

	public static readonly ParticleOperatorDefinition<C_OP_VelocityDecay> Definition = new("Lifespan Minimum Velocity Decay", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_VelocityDecay>()
			.Float("minimum velocity", "1", (o, v) => o.MinVelocity = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override void InitParams(ParticleSystemDefinition def, Element element) => MinVelocity = Math.Max(ParticleOpUtil.MIN_PARTICLE_SPEED, MinVelocity);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float invDt = 1.0f / particles.Dt;
		int limit = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < limit; p++) {
			Vector3 delta = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			if (delta.Length() * invDt <= MinVelocity)
				particles.KillParticle(p);
		}
	}
}

public sealed class C_OP_AlphaDecay : ParticleOperatorInstance
{
	public float MinAlpha;

	public static readonly ParticleOperatorDefinition<C_OP_AlphaDecay> Definition = new("Lifespan Minimum Alpha Decay", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_AlphaDecay>()
			.Float("minimum alpha", "0", (o, v) => o.MinAlpha = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_ALPHA_MASK | PARTICLE_ATTRIBUTE_ALPHA2_MASK;
	public override void InitParams(ParticleSystemDefinition def, Element element) => MinAlpha = Math.Max(0.0f, MinAlpha);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float min = MinAlpha + 1.1920929e-07f;
		int limit = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < limit; p++) {
			if (particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA, p) * particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ALPHA2, p) <= min)
				particles.KillParticle(p);
		}
	}
}

public sealed class C_OP_RadiusDecay : ParticleOperatorInstance
{
	public float MinRadius;

	public static readonly ParticleOperatorDefinition<C_OP_RadiusDecay> Definition = new("Lifespan Minimum Radius Decay", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RadiusDecay>()
			.Float("minimum radius", "1", (o, v) => o.MinRadius = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_RADIUS_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int limit = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < limit; p++) {
			if (particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, p) <= MinRadius)
				particles.KillParticle(p);
		}
	}
}

public sealed class C_OP_DecayMaintainCount : ParticleOperatorInstance
{
	public int ParticlesToMaintain;
	public int ScaleControlPoint;
	public int ScaleControlPointField;
	public float DecayDelay;

	public static readonly ParticleOperatorDefinition<C_OP_DecayMaintainCount> Definition = new("Lifespan Maintain Count Decay", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_DecayMaintainCount>()
			.Int("count to maintain", "100", (o, v) => o.ParticlesToMaintain = v)
			.Float("decay delay", "0", (o, v) => o.DecayDelay = v)
			.Int("maintain count scale control point", "-1", (o, v) => o.ScaleControlPoint = v)
			.Int("maintain count scale control point field", "0", (o, v) => o.ScaleControlPointField = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override int GetRequiredContextBytes() => sizeof(int);
	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => BitConverter.TryWriteBytes(context, 0);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		ScaleControlPoint = Math.Clamp(ScaleControlPoint, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
		ScaleControlPointField = Math.Clamp(ScaleControlPointField, 0, 2);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		ref int pendingDecay = ref System.Runtime.InteropServices.MemoryMarshal.Cast<byte, int>(context)[0];
		int toMaintain = ParticlesToMaintain;
		if (ScaleControlPoint >= 0)
			toMaintain = (int)Math.Min((float)particles.Def!.MaxParticles, ParticlesToMaintain * ParticleOpUtil.Component(particles.GetControlPointAtCurrentTime(ScaleControlPoint), ScaleControlPointField));

		int killQueue = 0;
		if (toMaintain < particles.ActiveParticles)
			killQueue = particles.ActiveParticles - pendingDecay - toMaintain;

		for (int p = 0; p < particles.ActiveParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			ref float lifeDuration = ref particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p);
			if (lifeDuration < particles.CurTime - creationTime) {
				particles.KillParticle(p);
				killQueue--;
				pendingDecay = Math.Max(0, pendingDecay - 1);
			}
			else if (killQueue > 0 && particles.CurTime + DecayDelay < lifeDuration) {
				lifeDuration = particles.CurTime + DecayDelay - creationTime;
				killQueue--;
				pendingDecay++;
			}
		}
	}
}

public sealed class C_OP_Cull : ParticleOperatorInstance
{
	public float CullPerc;
	public float CullStart;
	public float CullEnd;
	public float CullExp;

	public static readonly ParticleOperatorDefinition<C_OP_Cull> Definition = new("Cull Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_Cull>()
			.Float("Cull Start Time", "0", (o, v) => o.CullStart = v)
			.Float("Cull End Time", "1", (o, v) => o.CullEnd = v)
			.Float("Cull Time Exponent", "1", (o, v) => o.CullExp = v)
			.Float("Cull Percentage", "0.5", (o, v) => o.CullPerc = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float rank = particles.RandomFloat(0.0f, 1.0f);
			float cullTime = particles.RandomFloatExp(CullStart, CullEnd, CullExp);
			if (rank > CullPerc * opStrength)
				continue;
			float t = Math.Clamp(ParticleOpUtil.NormalizedAge(particles, p), 0.0f, 1.0f);
			if (t >= CullStart && t <= CullEnd && t >= cullTime)
				particles.KillParticle(p);
		}
	}
}
