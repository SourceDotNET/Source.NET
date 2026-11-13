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

public sealed class C_OP_SpinYaw : C_OP_GeneralSpin
{
	protected override int AttributeToSpin => PARTICLE_ATTRIBUTE_YAW;

	public static readonly ParticleOperatorDefinition<C_OP_SpinYaw> Definition = new("Rotation Spin Yaw", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SpinYaw>()
			.Int("yaw_rate_degrees", "0", (o, v) => o.SpinRateDegrees = v)
			.Float("yaw_stop_time", "0", (o, v) => o.SpinRateStopTime = v)
			.Int("yaw_rate_min", "0", (o, v) => o.SpinRateMinDegrees = v));
}

public sealed class C_OP_SpinUpdate : ParticleOperatorInstance
{
	public static readonly ParticleOperatorDefinition<C_OP_SpinUpdate> Definition = new("Rotation Basic", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SpinUpdate>());

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_ROTATION_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_ROTATION_MASK | PARTICLE_ATTRIBUTE_ROTATION_SPEED_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int count = (particles.ActiveParticles + 3) & ~3;
		for (int p = 0; p < count; p++) {
			float simTime = Math.Min(particles.Dt, ParticleOpUtil.Age(particles, p));
			ref float rot = ref particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_ROTATION, p);
			rot = simTime * opStrength * particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_ROTATION_SPEED, p) + rot;
		}
	}
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

public sealed class C_OP_ControlpointLight : ParticleOperatorInstance
{
	public sealed class Light
	{
		public int ControlPoint;
		public Vector3 Offset;
		public bool Spot;
		public Color Color;
		public bool Dynamic;
		public Vector3 Direction;
		public float FiftyDist;
		public float ZeroDist;
		public float Theta;
		public float Phi;
		public bool Active;
		public LightDesc Desc;
	}

	public readonly Light[] Lights = [new(), new(), new(), new()];
	public float Scale;
	public bool ClampLowerRange;
	public bool ClampUpperRange;
	public bool UseNormal;
	public bool UseHLambert;

	static DmxElementUnpackStructure<C_OP_ControlpointLight> AddLight(DmxElementUnpackStructure<C_OP_ControlpointLight> s, int i) {
		string n = $"Light {i + 1} ";
		return s.Int(n + "Control Point", "0", (o, v) => o.Lights[i].ControlPoint = v)
			.Vector3(n + "Control Point Offset", "0 0 0", (o, v) => o.Lights[i].Offset = v)
			.Bool(n + "Type 0=Point 1=Spot", "0", (o, v) => o.Lights[i].Spot = v)
			.Color(n + "Color", "0 0 0 255", (o, v) => o.Lights[i].Color = v)
			.Bool(n + "Dynamic Light", "0", (o, v) => o.Lights[i].Dynamic = v)
			.Vector3(n + "Direction", "0 0 0", (o, v) => o.Lights[i].Direction = v)
			.Float(n + "50% Distance", "100", (o, v) => o.Lights[i].FiftyDist = v)
			.Float(n + "0% Distance", "200", (o, v) => o.Lights[i].ZeroDist = v)
			.Float(n + "Spot Inner Cone", "30.0", (o, v) => o.Lights[i].Theta = v)
			.Float(n + "Spot Outer Cone", "45.0", (o, v) => o.Lights[i].Phi = v);
	}

	public static readonly ParticleOperatorDefinition<C_OP_ControlpointLight> Definition = new("Color Light from Control Point", ParticleOperatorId.Generic, false,
		AddLight(AddLight(AddLight(AddLight(ParticleOperatorUnpack.Begin<C_OP_ControlpointLight>(), 0), 1), 2), 3)
			.Float("Initial Color Bias", "0.0", (o, v) => o.Scale = v)
			.Bool("Clamp Minimum Light Value to Initial Color", "0", (o, v) => o.ClampLowerRange = v)
			.Bool("Clamp Maximum Light Value to Initial Color", "0", (o, v) => o.ClampUpperRange = v)
			.Bool("Compute Normals From Control Points", "0", (o, v) => o.UseNormal = v)
			.Bool("Half-Lambert Normals", "1", (o, v) => o.UseHLambert = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_TINT_RGB_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override uint GetReadInitialAttributes() => PARTICLE_ATTRIBUTE_TINT_RGB_MASK;

	public override ulong GetReadControlPointMask() {
		ulong mask = 0;
		foreach (Light light in Lights)
			mask |= ParticleOpUtil.CPMask(light.ControlPoint);
		return mask;
	}

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		foreach (Light light in Lights) {
			light.ControlPoint = ParticleOpUtil.ClampCP(light.ControlPoint);
			light.Desc = new LightDesc {
				Color = new Vector3(light.Color.R, light.Color.G, light.Color.B) / 255.0f,
				Direction = light.Direction,
				Theta = light.Theta,
				Phi = light.Phi,
				Range = 0,
				Falloff = 5.0f,
				Type = light.Spot ? LightType.Spot : LightType.Point
			};
			light.Active = !light.Dynamic && !(light.Color.R == 0 && light.Color.G == 0 && light.Color.B == 0 && light.Color.A == 255);
			light.Desc.SetupNewStyleAttenuation(light.FiftyDist, light.ZeroDist);
		}
	}

	struct LightState
	{
		public LightDesc Desc;
		public Vector3 CPPosition;
		public float OneOverSpread;
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Span<LightState> states = stackalloc LightState[4];
		for (int i = 0; i < 4; i++) {
			Light light = Lights[i == 3 ? 2 : i];
			Vector3 location = particles.GetControlPointAtCurrentTime(Lights[i].ControlPoint);
			states[i].CPPosition = location;
			states[i].Desc = light.Desc;
			states[i].Desc.Position = location + Lights[i].Offset;
			if (Lights[i].Dynamic) {
				particles.Mgr!.Query().GetLightingAtPoint(states[i].Desc.Position, out Color lc);
				states[i].Desc.Color = new Vector3(lc.R, lc.G, lc.B) / 255.0f;
			}
			states[i].Desc.RecalculateDerivedValues();
			float spread = states[i].Desc.ThetaDot - states[i].Desc.PhiDot;
			states[i].OneOverSpread = spread > 1.0e-10f ? 1.0f / spread : 1.0f;
		}

		int count = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < count; p++) {
			Vector3 initial = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_TINT_RGB, p);
			Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			Vector3 lighting = initial * Scale;
			for (int i = 0; i < 4; i++) {
				Vector3? normal = UseNormal ? ParticleOpUtil.NormalizeOrZero(position - states[i].CPPosition) : null;
				lighting += ComputeLight(states[i], position, normal, UseHLambert);
			}
			lighting = ClampLowerRange ? Vector3.Max(lighting, initial) : Vector3.Max(lighting, Vector3.Zero);
			lighting = ClampUpperRange ? Vector3.Min(lighting, initial) : Vector3.Min(lighting, Vector3.One);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_TINT_RGB, p, lighting);
		}
	}

	static Vector3 ComputeLight(in LightState state, in Vector3 position, Vector3? normal, bool halfLambert) {
		ref readonly LightDesc desc = ref state.Desc;
		if (desc.Type == LightType.Directional) {
			if (normal == null)
				return Vector3.Zero;
			float d = Vector3.Dot(desc.Direction, normal.Value);
			float s = halfLambert ? d * 0.5f + 0.5f : Math.Max(0.0f, d);
			return desc.Color * s;
		}

		Vector3 delta = desc.Position - position;
		float dist2 = Math.Max(1.0f, delta.LengthSquared());
		float falloff = desc.Attenuation0 != 0.0f ? desc.Attenuation0 : 1.1920929e-07f;
		if (desc.Attenuation1 != 0.0f)
			falloff += desc.Attenuation1 * MathF.Sqrt(dist2);
		if (desc.Attenuation2 != 0.0f)
			falloff += desc.Attenuation2 * dist2;
		falloff = 1.0f / falloff;
		if (desc.Range != 0.0f && !(dist2 < desc.Range * desc.Range))
			falloff = 0.0f;

		delta = ParticleOpUtil.NormalizeOrZero(delta);
		float strength = 1.0f;
		if (normal != null) {
			float d = Vector3.Dot(delta, normal.Value);
			strength = halfLambert ? d * 0.5f + 0.5f : Math.Max(0.0f, d);
		}
		if (desc.Type == LightType.Spot) {
			float dot2 = -Vector3.Dot(delta, desc.Direction);
			float cone = Math.Min(state.OneOverSpread * (dot2 - desc.PhiDot), 1.0f);
			if (desc.Falloff != 0.0f && desc.Falloff != 1.0f)
				cone = MathF.Pow(cone, desc.Falloff);
			strength = dot2 > desc.PhiDot ? cone * strength : 0.0f;
		}
		return desc.Color * (strength * falloff);
	}
}
