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
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_FadeIn.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_FadeOut.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_OscillateScalar.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_OscillateVector.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_InterpolateRadius.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_ColorInterpolate.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_Spin.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_PositionLock.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Operator, C_OP_DampenToCP.Definition);
	}
}

internal static class ParticleOpUtil
{
	public const int MAX_GMOD_ATTRIBUTE = 21;
	public const int ATTRIBUTES_WHICH_ARE_0_TO_1 = 0x10080;

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

	public static float ReadScalar(ParticleCollection particles, int attribute, int p) => ParticleInitUtil.ReadScalar(particles, attribute, p);

	public static void WriteScalar(ParticleCollection particles, int attribute, int p, float value) => ParticleInitUtil.WriteScalar(particles, attribute, p, value);

	public static ref T Context<T>(Span<byte> context) where T : unmanaged => ref System.Runtime.InteropServices.MemoryMarshal.AsRef<T>(context);

	public static int ClampCP(int cp) => Math.Clamp(cp, 0, MAX_PARTICLE_CONTROL_POINTS - 1);

	public static bool MatricesAreEqual(in Matrix3x4 a, in Matrix3x4 b, float tolerance)
		=> MathF.Abs(a.M00 - b.M00) <= tolerance && MathF.Abs(a.M01 - b.M01) <= tolerance && MathF.Abs(a.M02 - b.M02) <= tolerance && MathF.Abs(a.M03 - b.M03) <= tolerance
		&& MathF.Abs(a.M10 - b.M10) <= tolerance && MathF.Abs(a.M11 - b.M11) <= tolerance && MathF.Abs(a.M12 - b.M12) <= tolerance && MathF.Abs(a.M13 - b.M13) <= tolerance
		&& MathF.Abs(a.M20 - b.M20) <= tolerance && MathF.Abs(a.M21 - b.M21) <= tolerance && MathF.Abs(a.M22 - b.M22) <= tolerance && MathF.Abs(a.M23 - b.M23) <= tolerance;

	public static Vector3 Transform(in Vector3 v, in Matrix3x4 m) {
		MathLib.VectorTransform(v, m, out Vector3 result);
		return result;
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
