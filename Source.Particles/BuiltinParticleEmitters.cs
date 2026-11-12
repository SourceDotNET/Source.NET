using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public static class BuiltinParticleEmitters
{
	public static bool g_bDontMakeSkipToTimeTakeForever = false;

	public static void AddBuiltInParticleEmitters(ParticleSystemMgr mgr) {
		mgr.AddParticleOperator(ParticleFunctionType.Emitter, C_OP_InstantaneousEmitter.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Emitter, C_OP_ContinuousEmitter.Definition);
	}

	internal static void StampNewParticles(ParticleCollection particles, int count, float startTime, float step, float endTime) {
		int startParticle = particles.ActiveParticles;
		particles.SetNActiveParticles(startParticle + count);
		float timeStamp = startTime;
		for (int i = startParticle; i < startParticle + count; i++) {
			if (step != 0.0f) {
				timeStamp += step;
				if (timeStamp >= endTime)
					timeStamp = endTime;
			}
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_CREATION_TIME, i) = timeStamp;
		}
	}
}

public sealed class C_OP_InstantaneousEmitter : ParticleOperatorInstance
{
	struct Context
	{
		public int ActualParticlesToEmit;
		public int ParticlesToEmit;
		public float StartTime;
		public bool Scaled;
	}

	public int ParticlesToEmit;
	public int MinParticlesToEmit;
	public float StartTime;
	public int PerFrameNum;
	public int ScaleControlPoint;
	public int ScaleControlPointField;

	public static readonly ParticleOperatorDefinition<C_OP_InstantaneousEmitter> Definition = new("emit_instantaneously", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_InstantaneousEmitter>()
			.Float("emission_start_time", "0", (o, v) => o.StartTime = v)
			.Int("num_to_emit_minimum", "-1", (o, v) => o.MinParticlesToEmit = v)
			.Int("num_to_emit", "100", (o, v) => o.ParticlesToEmit = v)
			.Int("maximum emission per frame", "-1", (o, v) => o.PerFrameNum = v)
			.Int("emission count scale control point", "-1", (o, v) => o.ScaleControlPoint = v)
			.Int("emission count scale control point field", "0", (o, v) => o.ScaleControlPointField = v));

	static ref Context Ctx(Span<byte> context) => ref MemoryMarshal.AsRef<Context>(context);

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => ScaleControlPoint < 0 ? 0 : 1UL << (ScaleControlPoint & 63);
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Context>();

	public override void InitParams(ParticleSystemDefinition def, Datamodel.Element element) {
		if (MinParticlesToEmit >= 0 && ParticlesToEmit < MinParticlesToEmit)
			(ParticlesToEmit, MinParticlesToEmit) = (MinParticlesToEmit, ParticlesToEmit);
		if (PerFrameNum < 0)
			PerFrameNum = int.MaxValue;
		ScaleControlPointField = Math.Clamp(ScaleControlPointField, 0, 2);
	}

	public override void StopEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) {
		if (!infiniteOnly)
			Ctx(context).ActualParticlesToEmit = 0;
	}

	public override void StartEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) {
		if (infiniteOnly)
			return;
		ref Context ctx = ref Ctx(context);
		ctx.ActualParticlesToEmit = ctx.ParticlesToEmit;
		SkipToTime(particles.CurTime, particles, context);
	}

	public override void SkipToTime(float time, ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		if (StartTime + 2.0f + ctx.StartTime < time)
			ctx.ActualParticlesToEmit = 0;
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		int toEmit = ParticlesToEmit;
		if (MinParticlesToEmit >= 0)
			toEmit = (int)((ParticlesToEmit + 1 - MinParticlesToEmit) * particles.RandomFloat(0.0f, 1.0f)) + MinParticlesToEmit;
		ctx.ParticlesToEmit = toEmit;
		ctx.ActualParticlesToEmit = toEmit;
		ctx.StartTime = 0.0f;
		ctx.Scaled = false;
	}

	public override void Restart(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.ActualParticlesToEmit = ctx.ParticlesToEmit;
		ctx.Scaled = false;
		ctx.StartTime = particles.CurTime;
	}

	public override bool MayCreateMoreParticles(ParticleCollection particles, Span<byte> context) => Ctx(context).ActualParticlesToEmit > 0;

	public override uint Emit(ParticleCollection particles, float opCurStrength, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		int remaining = ctx.ActualParticlesToEmit;
		if (remaining < 1)
			return 0;

		float startTime = StartTime + ctx.StartTime;
		if (startTime > particles.CurTime || ctx.ParticlesToEmit == 0)
			return 0;

		if (ScaleControlPoint >= 0 && !ctx.Scaled) {
			Vector3 scale;
			if (particles.CurTime - particles.PreviousDt <= startTime)
				particles.GetControlPointAtTime(ScaleControlPoint, startTime, out scale);
			else
				particles.GetControlPointAtPrevTime(ScaleControlPoint, out scale);
			float scaleValue = scale[ScaleControlPointField];
			ctx.ParticlesToEmit = (int)(ctx.ParticlesToEmit * scaleValue);
			ctx.Scaled = true;
			remaining = (int)(ctx.ActualParticlesToEmit * scaleValue);
		}

		if (remaining < 0)
			remaining = 0;

		int particlesThisFrame = Math.Min(remaining, PerFrameNum);
		int allowed = particles.MaxAllowedParticles - particles.ActiveParticles;
		int actual = Math.Min(ctx.ParticlesToEmit * ParticleSystemMgr.g_nParticle_Multiplier, allowed);
		actual = Math.Min(actual, particlesThisFrame);
		ctx.ActualParticlesToEmit = remaining - particlesThisFrame;

		if (actual == 0)
			return 0;

		BuiltinParticleEmitters.StampNewParticles(particles, actual, startTime, 0.0f, startTime);
		return PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	}
}

public sealed class C_OP_ContinuousEmitter : ParticleOperatorInstance
{
	struct Context
	{
		public float ParticleEmissionCounter;
		public int ActualParticlesToEmit;
		public float NextEmitTime;
		public float StartTime;
		public bool StoppedEmission;
	}

	public float EmissionDuration;
	public float StartTime;
	public float EmitRate;
	public float EmissionScale;
	public bool ScalePerParticle;

	public static readonly ParticleOperatorDefinition<C_OP_ContinuousEmitter> Definition = new("emit_continuously", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_ContinuousEmitter>()
			.Float("emission_start_time", "0", (o, v) => o.StartTime = v)
			.Float("emission_rate", "100", (o, v) => o.EmitRate = v)
			.Float("emission_duration", "0", (o, v) => o.EmissionDuration = v)
			.Float("scale emission to used control points", "0.0", (o, v) => o.EmissionScale = v)
			.Bool("use parent particles for emission scaling", "0", (o, v) => o.ScalePerParticle = v));

	static ref Context Ctx(Span<byte> context) => ref MemoryMarshal.AsRef<Context>(context);

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override uint GetReadAttributes() => 0;
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Context>();

	public override void InitParams(ParticleSystemDefinition def, Datamodel.Element element) {
		float rate = EmitRate;
		if (rate <= 0.0f)
			rate = 0.0f;
		if (EmissionDuration < 0.0f)
			EmissionDuration = 0.0f;
		EmitRate = ParticleSystemMgr.g_nParticle_Multiplier * rate;
	}

	public override void StopEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) {
		if (!infiniteOnly || EmissionDuration == 0.0f)
			Ctx(context).StoppedEmission = true;
	}

	public override void StartEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) {
		if (infiniteOnly && EmissionDuration != 0.0f)
			return;
		Ctx(context).StoppedEmission = false;
		SkipToTime(particles.CurTime, particles, context);
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.ParticleEmissionCounter = 0.0f;
		ctx.ActualParticlesToEmit = 0;
		ctx.NextEmitTime = StartTime;
		ctx.StartTime = 0.0f;
		ctx.StoppedEmission = false;
	}

	public override void Restart(ParticleCollection particles, Span<byte> context) {
		if (EmissionDuration == 0.0f)
			return;
		ref Context ctx = ref Ctx(context);
		ctx.ParticleEmissionCounter = 0.0f;
		ctx.ActualParticlesToEmit = 0;
		ctx.NextEmitTime = particles.CurTime + StartTime;
		ctx.StartTime = particles.CurTime;
	}

	public override bool MayCreateMoreParticles(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		if (ctx.StoppedEmission)
			return false;
		if (EmitRate <= 0.0f)
			return false;
		if (EmissionDuration != 0.0f)
			return particles.CurTime - particles.Dt <= EmissionDuration + StartTime + ctx.StartTime;
		return true;
	}

	float GetEmissionRate(ParticleCollection particles) {
		float controlPointScale = particles.GetHighestControlPoint() * EmissionScale;
		float rate = EmitRate;
		if (particles.CheckIfOperatorShouldRun(this, out float strength))
			rate *= strength;
		if (controlPointScale != 0.0f)
			rate *= controlPointScale;
		return rate;
	}

	public override void SkipToTime(float time, ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		float startTime = StartTime + ctx.StartTime;
		if (startTime >= time)
			return;

		float emissionRate = GetEmissionRate(particles);
		float currDrawTime = particles.CurTime;
		float prevDrawTime = currDrawTime - time;
		if (EmissionDuration != 0.0f && prevDrawTime <= startTime)
			prevDrawTime = startTime;

		ctx.ActualParticlesToEmit = 0;
		float deltaTime = currDrawTime - prevDrawTime;
		if (deltaTime >= 4.0f)
			deltaTime = 4.0f;

		float toEmit = emissionRate * deltaTime;
		prevDrawTime = currDrawTime - deltaTime;
		ctx.ParticleEmissionCounter = toEmit;

		int actual = (int)MathF.Floor(toEmit);
		int startParticle = particles.ActiveParticles;
		if (actual + startParticle > particles.MaxAllowedParticles)
			actual = particles.MaxAllowedParticles - startParticle;

		BuiltinParticleEmitters.StampNewParticles(particles, actual, prevDrawTime, deltaTime / actual, currDrawTime);

		if (BuiltinParticleEmitters.g_bDontMakeSkipToTimeTakeForever)
			return;

		float t = Math.Max(currDrawTime - particles.Def!.NoDrawTimeToGoToSleep, prevDrawTime);
		particles.CurTime = t;
		for (; t < currDrawTime; t += 0.1f)
			particles.Simulate(0.1f, false);
	}

	public override uint Emit(ParticleCollection particles, float opCurStrength, Span<byte> context) {
		ref Context ctx = ref Ctx(context);

		float controlPointScale = particles.GetHighestControlPoint() * EmissionScale;
		float emissionRate = opCurStrength * EmitRate;
		if (ScalePerParticle) {
			float scale = EmissionScale;
			if (particles.Parent != null)
				scale *= particles.Parent.ActiveParticles;
			emissionRate *= scale;
		}
		else if (controlPointScale != 0.0f)
			emissionRate *= controlPointScale;

		if (emissionRate == 0.0f || EmitRate <= 0.0f || ctx.StoppedEmission)
			return 0;

		float currDrawTime = particles.CurTime;
		float prevDrawTime = StartTime + ctx.StartTime;
		if (EmissionDuration == 0.0f) {
			if (currDrawTime < prevDrawTime)
				return 0;
			prevDrawTime = currDrawTime - particles.Dt;
		}
		else {
			float endTime = EmissionDuration + prevDrawTime;
			float frameStart = currDrawTime - particles.Dt;
			if (endTime < frameStart)
				return 0;
			if (currDrawTime < prevDrawTime)
				return 0;
			if (prevDrawTime <= frameStart)
				prevDrawTime = frameStart;
			if (endTime <= currDrawTime)
				currDrawTime = endTime;
		}

		float deltaTime = currDrawTime - prevDrawTime;
		ctx.ParticleEmissionCounter += emissionRate * deltaTime;

		int toEmit = (int)(MathF.Floor(ctx.ParticleEmissionCounter) - ctx.ActualParticlesToEmit);
		ctx.ActualParticlesToEmit += toEmit;
		if (toEmit == 0)
			return 0;

		int actual = Math.Min(toEmit, particles.MaxAllowedParticles - particles.ActiveParticles);
		if (actual == 0)
			return 0;

		BuiltinParticleEmitters.StampNewParticles(particles, actual, prevDrawTime, deltaTime / actual, currDrawTime);
		return PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	}
}
