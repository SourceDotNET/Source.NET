using Source.Common;
using Source.Common.Mathematics;

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
		mgr.AddParticleOperator(ParticleFunctionType.Emitter, C_OP_NoiseEmitter.Definition);
		mgr.AddParticleOperator(ParticleFunctionType.Emitter, C_OP_MaintainEmitter.Definition);
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

	internal static int ClampScaleControlPoint(int controlPoint) => controlPoint < -1 ? -1 : Math.Min(controlPoint, MAX_PARTICLE_CONTROL_POINTS - 1);

	internal static float GetScaleAtStartTime(ParticleCollection particles, int controlPoint, int field, float startTime) {
		Vector3 scale;
		if (particles.CurTime < startTime || startTime < particles.CurTime - particles.PreviousDt)
			particles.GetControlPointAtPrevTime(controlPoint, out scale);
		else
			particles.GetControlPointAtTime(controlPoint, startTime, out scale);
		return scale[field];
	}

	internal static float GetPositiveScaleAtCurrentTime(ParticleCollection particles, int controlPoint, int field) {
		particles.GetControlPointAtTime(controlPoint, particles.CurTime, out Vector3 value);
		float scale = value[field];
		if (scale <= 0.0f)
			scale = 0.0f;
		return scale;
	}

	internal static uint EmitBetween(ParticleCollection particles, ref float emissionCounter, ref int emittedSoFar, float rate, float startTime, float duration, bool requirePositive) {
		float currDrawTime = particles.CurTime;
		float prevDrawTime = currDrawTime - particles.Dt;
		if (duration != 0.0f) {
			if (prevDrawTime <= startTime)
				prevDrawTime = startTime;
			if (duration + startTime <= currDrawTime)
				currDrawTime = duration + startTime;
		}

		emissionCounter = (currDrawTime - prevDrawTime) * rate + emissionCounter;
		int toEmit = (int)(Math.Floor((double)emissionCounter) - emittedSoFar);
		emittedSoFar += toEmit;
		if (toEmit == 0)
			return 0;

		int actual = Math.Min(toEmit, particles.MaxAllowedParticles - particles.ActiveParticles);
		if (requirePositive ? actual <= 0 : actual == 0)
			return 0;

		StampNewParticles(particles, actual, prevDrawTime, (currDrawTime - prevDrawTime) / actual, currDrawTime);
		return PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	}
}

public sealed class C_OP_InstantaneousEmitter : ParticleOperatorInstance
{
	struct Context
	{
		public int ActualParticlesToEmit;
		public int ParticlesToEmit;
		public bool Scaled;
		public float TimeOffset;
		public float StartTime;
		public bool Active;
	}

	public int ParticlesToEmit;
	public int MinParticlesToEmit;
	public float StartTime;
	public float StartTimeMax;
	public int PerFrameNum;
	public int ScaleControlPoint;
	public int ScaleControlPointField;

	public static readonly ParticleOperatorDefinition<C_OP_InstantaneousEmitter> Definition = new("emit_instantaneously", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_InstantaneousEmitter>()
			.Float("emission_start_time", "0", (o, v) => o.StartTime = v)
			.Float("emission_start_time max", "-1", (o, v) => o.StartTimeMax = v)
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
			PerFrameNum = Math.Min(5000, def.MaxParticles);
		ScaleControlPointField = Math.Clamp(ScaleControlPointField, 0, 2);
		ScaleControlPoint = BuiltinParticleEmitters.ClampScaleControlPoint(ScaleControlPoint);
	}

	float RandomStartTime(ParticleCollection particles) {
		if (StartTimeMax > 0.0f)
			return (StartTimeMax - StartTime) * particles.RandomFloat(0.0f, 1.0f) + StartTime;
		return StartTime;
	}

	void UpdateParticlesToEmit(ParticleCollection particles, ref Context ctx) {
		int count = ctx.ParticlesToEmit;
		if (ScaleControlPoint >= 0)
			count = (int)(ctx.ParticlesToEmit * BuiltinParticleEmitters.GetScaleAtStartTime(particles, ScaleControlPoint, ScaleControlPointField, StartTime + ctx.TimeOffset));
		ctx.Scaled = true;
		ctx.ActualParticlesToEmit = Math.Max(0, count);
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		int toEmit = ParticlesToEmit;
		if (MinParticlesToEmit >= 0)
			toEmit = (int)((ParticlesToEmit - MinParticlesToEmit + 1) * particles.RandomFloat(0.0f, 1.0f)) + MinParticlesToEmit;
		ctx.ParticlesToEmit = toEmit;
		ctx.ActualParticlesToEmit = toEmit;
		ctx.Scaled = false;
		ctx.TimeOffset = 0.0f;
		ctx.Active = true;
		ctx.StartTime = RandomStartTime(particles);
	}

	public override void StopEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) {
		if (!infiniteOnly)
			Ctx(context).Active = false;
	}

	public override void StartEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) {
		if (infiniteOnly)
			return;
		ref Context ctx = ref Ctx(context);
		UpdateParticlesToEmit(particles, ref ctx);
		SkipToTime(particles.CurTime, particles, context);
		ctx.Active = true;
	}

	public override void Restart(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		UpdateParticlesToEmit(particles, ref ctx);
		ctx.TimeOffset = particles.CurTime;
		ctx.Active = true;
		ctx.StartTime = RandomStartTime(particles);
	}

	public override void SkipToTime(float time, ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		if (StartTime + ctx.TimeOffset + 2.0f < time)
			ctx.Active = false;
	}

	public override bool MayCreateMoreParticles(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		return ctx.Active && ctx.ActualParticlesToEmit >= 1;
	}

	public override uint Emit(ParticleCollection particles, float opCurStrength, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		if (!ctx.Active)
			return 0;

		float startTime = ctx.StartTime + ctx.TimeOffset;
		if (!(startTime <= particles.CurTime))
			return 0;

		if (!ctx.Scaled)
			UpdateParticlesToEmit(particles, ref ctx);

		int remaining = ctx.ActualParticlesToEmit;
		if (remaining <= 0)
			return 0;

		int particlesThisFrame = Math.Min(remaining, PerFrameNum);
		ctx.ActualParticlesToEmit = remaining - particlesThisFrame;
		int actual = Math.Min(particlesThisFrame, particles.MaxAllowedParticles - particles.ActiveParticles);
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
		public float TimeOffset;
		public bool Active;
	}

	public float EmissionDuration;
	public float StartTime;
	public float EmitRate;
	public float EmissionScale;
	public bool ScalePerParticle;
	public int ScaleControlPoint;
	public int ScaleControlPointField;

	public static readonly ParticleOperatorDefinition<C_OP_ContinuousEmitter> Definition = new("emit_continuously", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_OP_ContinuousEmitter>()
			.Float("emission_start_time", "0", (o, v) => o.StartTime = v)
			.Float("emission_rate", "100", (o, v) => o.EmitRate = v)
			.Float("emission_duration", "0", (o, v) => o.EmissionDuration = v)
			.Float("scale emission to used control points", "0.0", (o, v) => o.EmissionScale = v)
			.Bool("use parent particles for emission scaling", "0", (o, v) => o.ScalePerParticle = v)
			.Int("emission count scale control point", "-1", (o, v) => o.ScaleControlPoint = v)
			.Int("emission count scale control point field", "0", (o, v) => o.ScaleControlPointField = v));

	static ref Context Ctx(Span<byte> context) => ref MemoryMarshal.AsRef<Context>(context);

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => ScaleControlPoint < 0 ? 0 : 1UL << (ScaleControlPoint & 63);
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Context>();

	public override void InitParams(ParticleSystemDefinition def, Datamodel.Element element) {
		float rate = EmitRate;
		if (rate < 0.0f)
			rate = 0.0f;
		if (EmissionDuration < 0.0f)
			EmissionDuration = 0.0f;
		EmitRate = ParticleSystemMgr.g_nParticle_Multiplier * rate;
		ScaleControlPointField = Math.Clamp(ScaleControlPointField, 0, 2);
		ScaleControlPoint = BuiltinParticleEmitters.ClampScaleControlPoint(ScaleControlPoint);
	}

	public override void StopEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) {
		if (!infiniteOnly || EmissionDuration == 0.0f)
			Ctx(context).Active = false;
	}

	public override void StartEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) {
		if (infiniteOnly && EmissionDuration != 0.0f)
			return;
		Ctx(context).Active = true;
		SkipToTime(particles.CurTime, particles, context);
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.ParticleEmissionCounter = 0.0f;
		ctx.ActualParticlesToEmit = 0;
		ctx.NextEmitTime = StartTime;
		ctx.TimeOffset = 0.0f;
		ctx.Active = true;
	}

	public override void Restart(ParticleCollection particles, Span<byte> context) {
		if (EmissionDuration == 0.0f)
			return;
		ref Context ctx = ref Ctx(context);
		ctx.ParticleEmissionCounter = 0.0f;
		ctx.ActualParticlesToEmit = 0;
		ctx.NextEmitTime = StartTime + particles.CurTime;
		ctx.TimeOffset = particles.CurTime;
	}

	public override bool MayCreateMoreParticles(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		if (!ctx.Active || !(EmitRate > 0.0f))
			return false;
		return EmissionDuration == 0.0f || particles.CurTime - particles.Dt <= StartTime + ctx.TimeOffset + EmissionDuration;
	}

	public override void SkipToTime(float time, ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		float startTime = StartTime + ctx.TimeOffset;
		if (time <= startTime)
			return;

		float emissionRate = EmitRate;
		float controlPointScale = particles.GetHighestControlPoint() * EmissionScale;
		if (particles.CheckIfOperatorShouldRun(this, out float strength))
			emissionRate *= strength;
		if (ScaleControlPoint >= 0)
			emissionRate *= BuiltinParticleEmitters.GetPositiveScaleAtCurrentTime(particles, ScaleControlPoint, ScaleControlPointField);
		if (controlPointScale != 0.0f)
			emissionRate *= controlPointScale;

		float currDrawTime = particles.CurTime;
		float prevDrawTime = currDrawTime - time;
		if (EmissionDuration != 0.0f && prevDrawTime <= startTime)
			prevDrawTime = startTime;

		float deltaTime = currDrawTime - prevDrawTime;
		if (deltaTime >= 4.0f)
			deltaTime = 4.0f;

		ctx.ActualParticlesToEmit = 0;
		float toEmit = deltaTime * emissionRate;
		prevDrawTime = currDrawTime - deltaTime;
		ctx.ParticleEmissionCounter = toEmit;

		int actual = (int)Math.Floor((double)toEmit);
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
		float startTime = StartTime + ctx.TimeOffset;
		if (particles.CurTime < startTime)
			return 0;

		float emissionRate = opCurStrength * EmitRate;
		float controlPointScale = particles.GetHighestControlPoint() * EmissionScale;
		if (ScaleControlPoint >= 0)
			emissionRate *= BuiltinParticleEmitters.GetPositiveScaleAtCurrentTime(particles, ScaleControlPoint, ScaleControlPointField);

		if (ScalePerParticle) {
			controlPointScale = particles.Parent != null ? particles.Parent.ActiveParticles * EmissionScale : EmissionScale;
			emissionRate *= controlPointScale;
		}
		else if (controlPointScale != 0.0f)
			emissionRate *= controlPointScale;

		if (emissionRate == 0.0f || !ctx.Active || !(EmitRate > 0.0f))
			return 0;
		if (EmissionDuration != 0.0f && !(particles.CurTime - particles.Dt <= startTime + EmissionDuration))
			return 0;

		return BuiltinParticleEmitters.EmitBetween(particles, ref ctx.ParticleEmissionCounter, ref ctx.ActualParticlesToEmit, emissionRate, startTime, EmissionDuration, false);
	}
}

public sealed class C_OP_NoiseEmitter : ParticleOperatorInstance
{
	struct Context
	{
		public float ParticleEmissionCounter;
		public int ActualParticlesToEmit;
		public float NextEmitTime;
		public float TimeOffset;
		public bool Active;
	}

	public float EmissionDuration;
	public float StartTime;
	public float EmissionScale;
	public bool AbsVal;
	public bool AbsValInv;
	public float TimeOffset;
	public float OutputMin;
	public float OutputMax;
	public float NoiseScale;
	public float WorldNoiseScale;

	public static readonly ParticleOperatorDefinition<C_OP_NoiseEmitter> Definition = new("emit noise", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_OP_NoiseEmitter>()
			.Float("emission_start_time", "0", (o, v) => o.StartTime = v)
			.Float("emission_duration", "0", (o, v) => o.EmissionDuration = v)
			.Float("scale emission to used control points", "0.0", (o, v) => o.EmissionScale = v)
			.Float("time noise coordinate scale", "0.1", (o, v) => o.NoiseScale = v)
			.Float("time coordinate offset", "0", (o, v) => o.TimeOffset = v)
			.Bool("absolute value", "0", (o, v) => o.AbsVal = v)
			.Bool("invert absolute value", "0", (o, v) => o.AbsValInv = v)
			.Float("emission minimum", "0", (o, v) => o.OutputMin = v)
			.Float("emission maximum", "100", (o, v) => o.OutputMax = v)
			.Float("world time noise coordinate scale", "0", (o, v) => o.WorldNoiseScale = v));

	static ref Context Ctx(Span<byte> context) => ref MemoryMarshal.AsRef<Context>(context);

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override uint GetReadAttributes() => 0;
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Context>();

	public override void InitParams(ParticleSystemDefinition def, Datamodel.Element element) {
		if (EmissionDuration < 0.0f)
			EmissionDuration = 0.0f;
	}

	public override void StopEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) {
		if (!infiniteOnly || EmissionDuration == 0.0f)
			Ctx(context).Active = false;
	}

	public override void StartEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) {
		if (infiniteOnly && EmissionDuration != 0.0f)
			return;
		Ctx(context).Active = true;
		SkipToTime(particles.CurTime, particles, context);
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.ParticleEmissionCounter = 1.0f;
		ctx.ActualParticlesToEmit = 0;
		ctx.NextEmitTime = StartTime;
		ctx.TimeOffset = 0.0f;
		ctx.Active = true;
	}

	public override void Restart(ParticleCollection particles, Span<byte> context) {
		if (EmissionDuration == 0.0f)
			return;
		ref Context ctx = ref Ctx(context);
		ctx.ParticleEmissionCounter = 1.0f;
		ctx.ActualParticlesToEmit = 0;
		ctx.NextEmitTime = StartTime + particles.CurTime;
		ctx.TimeOffset = particles.CurTime;
	}

	public override bool MayCreateMoreParticles(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		if (!ctx.Active)
			return false;
		return EmissionDuration == 0.0f || particles.CurTime - particles.Dt <= StartTime + ctx.TimeOffset + EmissionDuration;
	}

	public override void SkipToTime(float time, ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		if (!(StartTime + ctx.TimeOffset < time))
			return;
		particles.CheckIfOperatorShouldRun(this, out _);
		ctx.ParticleEmissionCounter = 1.0f;
		ctx.ActualParticlesToEmit = 0;
	}

	public override uint Emit(ParticleCollection particles, float opCurStrength, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		float controlPointScale = particles.GetHighestControlPoint() * EmissionScale;
		float absScale = AbsVal ? 1.0f : 0.5f;

		float coord = (float)(uint)Platform.MSTime * WorldNoiseScale + (TimeOffset + particles.CurTime) * NoiseScale;
		float noise = NoiseSIMD.Noise(coord, coord, coord);
		if (AbsVal)
			noise = MathF.Abs(noise);
		if (AbsValInv)
			noise = 1.0f - noise;

		float range = OutputMax - OutputMin;
		float emissionRate = range * absScale * noise + (1.0f - absScale) * range + OutputMin;
		if (emissionRate <= 0.0f)
			emissionRate = 0.0f;
		if (2.1474836e+09f <= emissionRate)
			emissionRate = 2.1474836e+09f;
		emissionRate *= opCurStrength;
		if (controlPointScale != 0.0f)
			emissionRate *= controlPointScale;

		if (emissionRate == 0.0f || !ctx.Active)
			return 0;
		float startTime = StartTime + ctx.TimeOffset;
		if (EmissionDuration != 0.0f && !(particles.CurTime - particles.Dt <= startTime + EmissionDuration))
			return 0;
		if (!(startTime <= particles.CurTime))
			return 0;

		return BuiltinParticleEmitters.EmitBetween(particles, ref ctx.ParticleEmissionCounter, ref ctx.ActualParticlesToEmit, emissionRate, startTime, EmissionDuration, true);
	}
}

public sealed class C_OP_MaintainEmitter : ParticleOperatorInstance
{
	struct Context
	{
		public int ActualParticlesToMaintain;
		public float TimeOffset;
		public bool Active;
	}

	public int ParticlesToMaintain;
	public float StartTime;
	public int ScaleControlPoint;
	public int ScaleControlPointField;

	public static readonly ParticleOperatorDefinition<C_OP_MaintainEmitter> Definition = new("emit to maintain count", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_MaintainEmitter>()
			.Float("emission start time", "0", (o, v) => o.StartTime = v)
			.Int("count to maintain", "100", (o, v) => o.ParticlesToMaintain = v)
			.Int("maintain count scale control point", "-1", (o, v) => o.ScaleControlPoint = v)
			.Int("maintain count scale control point field", "0", (o, v) => o.ScaleControlPointField = v));

	static ref Context Ctx(Span<byte> context) => ref MemoryMarshal.AsRef<Context>(context);

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => ScaleControlPoint < 0 ? 0 : 1UL << (ScaleControlPoint & 63);
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Context>();

	public override void InitParams(ParticleSystemDefinition def, Datamodel.Element element) {
		ScaleControlPointField = Math.Clamp(ScaleControlPointField, 0, 2);
		ScaleControlPoint = BuiltinParticleEmitters.ClampScaleControlPoint(ScaleControlPoint);
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.ActualParticlesToMaintain = ParticlesToMaintain;
		ctx.TimeOffset = 0.0f;
		ctx.Active = true;
	}

	public override void StopEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) => Ctx(context).Active = false;
	public override void StartEmission(ParticleCollection particles, Span<byte> context, bool infiniteOnly = false) => Ctx(context).Active = true;

	public override void Restart(ParticleCollection particles, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		ctx.TimeOffset = particles.CurTime;
		ctx.Active = true;
	}

	public override bool MayCreateMoreParticles(ParticleCollection particles, Span<byte> context) => Ctx(context).Active;

	public override uint Emit(ParticleCollection particles, float opCurStrength, Span<byte> context) {
		ref Context ctx = ref Ctx(context);
		if (!ctx.Active)
			return 0;

		float startTime = ctx.TimeOffset + StartTime;
		if (!(startTime <= particles.CurTime))
			return 0;

		int count = ParticlesToMaintain;
		if (ScaleControlPoint >= 0)
			count = (int)(ParticlesToMaintain * BuiltinParticleEmitters.GetScaleAtStartTime(particles, ScaleControlPoint, ScaleControlPointField, startTime));
		count = Math.Max(0, count);
		ctx.ActualParticlesToMaintain = count;

		int startParticle = particles.ActiveParticles;
		if (startParticle >= count || count <= 0)
			return 0;

		int actual = Math.Min(particles.MaxAllowedParticles - startParticle, count - startParticle);
		if (actual < 0)
			return 0;

		particles.SetNActiveParticles(startParticle + actual);
		float emitTime = Math.Max(particles.PrevSimTime, startTime);
		float step = (particles.CurTime - emitTime) / actual;
		for (int i = startParticle; i < startParticle + actual; i++) {
			emitTime = Math.Min(emitTime, particles.CurTime);
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_CREATION_TIME, i) = emitTime;
			emitTime += step;
		}
		return PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	}
}
