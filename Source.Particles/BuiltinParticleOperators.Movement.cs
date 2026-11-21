using Datamodel;

using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.Intrinsics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public abstract class C_OP_GeneralSpin : ParticleOperatorInstance
{
	public int SpinRateDegrees;
	public int SpinRateMinDegrees;
	public float SpinRateStopTime;
	float SpinRateRadians;
	float SpinRateMinRadians;

	protected abstract int AttributeToSpin { get; }

	public override uint GetWrittenAttributes() => SpinRateDegrees != 0 ? 1u << AttributeToSpin : 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		SpinRateRadians = SpinRateDegrees * 0.017453292f;
		SpinRateMinRadians = SpinRateMinDegrees * 0.017453292f;
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float curSpinRate = SpinRateRadians * opStrength;
		if (curSpinRate == 0.0f)
			return;

		float dt = particles.Dt;
		float drot = MathF.Abs(curSpinRate * 2.0f * MathF.PI) * dt;
		if (SpinRateStopTime == 0.0f)
			drot = (float)(drot % (2.0 * Math.PI));
		if (curSpinRate < 0.0f)
			drot = -drot;

		float minSpeed = MathF.Abs(SpinRateMinRadians * 2.0f * MathF.PI) * dt;
		const float twoPi = 6.2831855f;
		int attribute = AttributeToSpin;
		int count = (particles.ActiveParticles + 3) & ~3;
		for (int p = 0; p < count; p++) {
			float ooFadeRate = 0.0f;
			if (SpinRateStopTime != 0.0f)
				ooFadeRate = 1.0f / (SpinRateStopTime * ParticleOpUtil.Lifetime(particles, p));
			float scale = Math.Max(0.0f, 1.0f - ParticleOpUtil.Age(particles, p) * ooFadeRate);
			float delta = drot * scale;
			if (delta <= minSpeed)
				delta = minSpeed;
			ref float rot = ref particles.GetFloatAttributeRefForWrite(attribute, p);
			float newRot = rot + delta;
			float wrapped = newRot >= twoPi ? newRot - twoPi : newRot;
			rot = newRot <= -twoPi ? wrapped + twoPi : wrapped;
		}
	}
}

public sealed class C_OP_Spin : C_OP_GeneralSpin
{
	protected override int AttributeToSpin => PARTICLE_ATTRIBUTE_ROTATION;

	public static readonly ParticleOperatorDefinition<C_OP_Spin> Definition = new("Rotation Spin Roll", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_Spin>()
			.Int("spin_rate_degrees", "0", (o, v) => o.SpinRateDegrees = v)
			.Float("spin_stop_time", "0", (o, v) => o.SpinRateStopTime = v)
			.Int("spin_rate_min", "0", (o, v) => o.SpinRateMinDegrees = v));
}

public sealed class C_OP_PositionLock : ParticleOperatorInstance
{
	struct LockContext
	{
		public Vector3 PrevPosition;
		public Matrix3x4 PrevTransform;
	}

	public int ControlPointNumber;
	public float StartTimeMin;
	public float StartTimeMax;
	public float StartTimeExp;
	public float EndTimeMin;
	public float EndTimeMax;
	public float EndTimeExp;
	public float Range;
	public bool LockRot;

	public static readonly ParticleOperatorDefinition<C_OP_PositionLock> Definition = new("Movement Lock to Control Point", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_PositionLock>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("start_fadeout_min", "1", (o, v) => o.StartTimeMin = v)
			.Float("start_fadeout_max", "1", (o, v) => o.StartTimeMax = v)
			.Float("start_fadeout_exponent", "1", (o, v) => o.StartTimeExp = v)
			.Float("end_fadeout_min", "1", (o, v) => o.EndTimeMin = v)
			.Float("end_fadeout_max", "1", (o, v) => o.EndTimeMax = v)
			.Float("end_fadeout_exponent", "1", (o, v) => o.EndTimeExp = v)
			.Float("distance fade range", "0", (o, v) => o.Range = v)
			.Bool("lock rotation", "0", (o, v) => o.LockRot = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;
	public override void InitParams(ParticleSystemDefinition def, Element element) => ControlPointNumber = ParticleOpUtil.ClampCP(ControlPointNumber);
	public override int GetRequiredContextBytes() => System.Runtime.CompilerServices.Unsafe.SizeOf<LockContext>();

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref LockContext ctx = ref ParticleOpUtil.Context<LockContext>(context);
		ctx.PrevPosition = Vector3.Zero;
		particles.GetControlPointTransformAtTime(ControlPointNumber, particles.CurTime, out ctx.PrevTransform);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		ref LockContext ctx = ref ParticleOpUtil.Context<LockContext>(context);
		Vector3 controlPoint = particles.GetControlPointAtCurrentTime(ControlPointNumber);
		particles.GetControlPointTransformAtTime(ControlPointNumber, particles.CurTime, out Matrix3x4 currentTransform);

		if (ctx.PrevPosition == Vector3.Zero) {
			ctx.PrevPosition = controlPoint;
			particles.GetControlPointTransformAtTime(ControlPointNumber, particles.CurTime, out ctx.PrevTransform);
		}
		else if (!LockRot ? ctx.PrevPosition == controlPoint : ParticleOpUtil.MatricesAreEqual(currentTransform, ctx.PrevTransform, 1e-5f))
			return;

		Matrix3x4 transformLock = default;
		if (LockRot) {
			MathLib.MatrixInvert(ctx.PrevTransform, out Matrix3x4 prevInverse);
			MathLib.ConcatTransforms(currentTransform, prevInverse, out transformLock);
		}

		Vector3 delta = (controlPoint - ctx.PrevPosition) * opStrength;
		float dt = particles.Dt;
		float ooDt = 1.0f / dt;
		bool useRange = Range != 0.0f;
		float ooRange = useRange ? 1.0f / Range : 1.0f;
		float biasParam = ParticleOpUtil.PreCalcBiasParameter(0.2f);
		int count = ParticleOpUtil.PaddedCount(particles);

		if (StartTimeMin >= 1.0f) {
			for (int p = 0; p < count; p++) {
				float frameBias = ooDt * Math.Min(ParticleOpUtil.Age(particles, p), dt);
				Vector3 scaledDelta = delta * frameBias;
				float lockStrength = opStrength;
				Apply(particles, p, scaledDelta, lockStrength, frameBias, controlPoint, useRange, ooRange, biasParam, transformLock);
			}
		}
		else {
			int randContext = RandSIMD.GetContext();
			int startExponent = (int)(StartTimeExp * 4.0f);
			int endExponent = (int)(EndTimeExp * 4.0f);
			Vector128<float> startTimes = default, endTimes = default;
			for (int p = 0; p < count; p++) {
				if ((p & 3) == 0) {
					startTimes = RandSIMD.PowFixedPointExponent(RandSIMD.Next(randContext), startExponent) * (StartTimeMax - StartTimeMin) + Vector128.Create(StartTimeMin);
					endTimes = RandSIMD.PowFixedPointExponent(RandSIMD.Next(randContext), endExponent) * (EndTimeMax - EndTimeMin) + Vector128.Create(EndTimeMin);
				}
				float age = ParticleOpUtil.Age(particles, p);
				float frameBias = ooDt * Math.Min(age, dt);
				Vector3 scaledDelta = delta * frameBias;
				float lifeTime = Math.Max(0.0f, Math.Min(1.0f, age / ParticleOpUtil.Lifetime(particles, p)));
				float startTime = startTimes[p & 3];
				float endTime = endTimes[p & 3];
				float lockScale = 1.0f - Math.Max(0.0f, Math.Min(1.0f, (lifeTime - startTime) / (endTime - startTime)));
				if (!(lockScale > 0.0f))
					continue;
				Apply(particles, p, scaledDelta * lockScale, lockScale, frameBias, controlPoint, useRange, ooRange, biasParam, transformLock);
			}
			RandSIMD.ReleaseContext(randContext);
		}

		ctx.PrevPosition = controlPoint;
		ctx.PrevTransform = currentTransform;
	}

	void Apply(ParticleCollection particles, int p, Vector3 scaledDelta, float lockStrength, float frameBias, in Vector3 controlPoint, bool useRange, float ooRange, float biasParam, in Matrix3x4 transformLock) {
		Vector3 xyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
		Vector3 prevXyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
		if (useRange) {
			float distance = (xyz + scaledDelta - controlPoint).Length();
			distance = ParticleOpUtil.Bias(Math.Min(1.0f, distance * ooRange), biasParam);
			scaledDelta *= 1.0f - distance;
			lockStrength = 1.0f - distance * lockStrength;
		}
		if (LockRot) {
			lockStrength *= frameBias;
			Vector3 cur = (ParticleOpUtil.Transform(xyz, transformLock) - xyz) * lockStrength;
			Vector3 prev = (ParticleOpUtil.Transform(prevXyz, transformLock) - prevXyz) * lockStrength;
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, xyz + cur);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prevXyz + prev);
		}
		else {
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, xyz + scaledDelta);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prevXyz + scaledDelta);
		}
	}
}
