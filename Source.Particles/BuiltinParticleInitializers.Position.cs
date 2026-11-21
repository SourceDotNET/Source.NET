using Datamodel;

using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static Source.Particles.ParticleConstants;

using QAngle = Source.Common.Mathematics.QAngle;

namespace Source.Particles;

public sealed class C_INIT_RingWave : ParticleOperatorInstance
{
	public int ControlPointNumber;
	public int OverrideCP;
	public int OverrideCP2;
	public float ParticlesPerOrbit;
	public float InitialRadius;
	public float Thickness;
	public float InitialSpeedMin;
	public float InitialSpeedMax;
	public float Roll;
	public float Pitch;
	public float Yaw;
	public bool EvenDistribution;
	public bool XYVelocityOnly;

	public static readonly ParticleOperatorDefinition<C_INIT_RingWave> Definition = new("Position Along Ring", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RingWave>()
			.Int("control point number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("initial radius", "0", (o, v) => o.InitialRadius = v)
			.Float("thickness", "0", (o, v) => o.Thickness = v)
			.Float("min initial speed", "0", (o, v) => o.InitialSpeedMin = v)
			.Float("max initial speed", "0", (o, v) => o.InitialSpeedMax = v)
			.Float("yaw", "0", (o, v) => o.Yaw = v)
			.Float("roll", "0", (o, v) => o.Roll = v)
			.Float("pitch", "0", (o, v) => o.Pitch = v)
			.Bool("even distribution", "0", (o, v) => o.EvenDistribution = v)
			.Float("even distribution count", "-1", (o, v) => o.ParticlesPerOrbit = v)
			.Bool("XY velocity only", "1", (o, v) => o.XYVelocityOnly = v)
			.Int("Override CP (X/Y/Z *= Radius/Thickness/Speed)", "-1", (o, v) => o.OverrideCP = v)
			.Int("Override CP 2 (X/Y/Z *= Pitch/Yaw/Roll)", "-1", (o, v) => o.OverrideCP2 = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override int GetRequiredContextBytes() => sizeof(uint);

	public override ulong GetReadControlPointMask() {
		ulong mask = 1UL << (ControlPointNumber & 63);
		if (OverrideCP != -1)
			mask |= 1UL << (OverrideCP & 63);
		if (OverrideCP2 != -1)
			mask |= 1UL << (OverrideCP2 & 63);
		return mask;
	}

	static ref uint Count(Span<byte> context) => ref MemoryMarshal.AsRef<uint>(context);

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => Count(context) = 0;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		OverrideCP = Math.Clamp(OverrideCP, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
		OverrideCP2 = Math.Clamp(OverrideCP2, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
		InitialSpeedMax = Math.Max(InitialSpeedMin, InitialSpeedMax);
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		ref uint count = ref Count(context);
		if (count >= ~(uint)numParticles)
			count = 0;

		float radius = InitialRadius;
		float thickness = Thickness;
		float speedMin = InitialSpeedMin;
		float speedMax = InitialSpeedMax;
		QAngle angles = new(Pitch, Yaw, Roll);

		if (OverrideCP != -1) {
			Vector3 scale = particles.GetControlPointAtCurrentTime(ParticleInitUtil.CheckControlPoint(OverrideCP));
			radius *= scale.X;
			thickness *= scale.Y;
			speedMin *= scale.Z;
			speedMax *= scale.Z;
		}

		if (OverrideCP2 != -1) {
			Vector3 scale = particles.GetControlPointAtCurrentTime(ParticleInitUtil.CheckControlPoint(OverrideCP2));
			angles.X *= scale.X;
			angles.Y *= scale.Y;
			angles.Z *= scale.Z;
		}

		MathLib.AngleMatrix(angles, out Matrix3x4 rotation);
		float theta = (float)(2.0 * Math.PI / (EvenDistribution && ParticlesPerOrbit != -1.0f ? ParticlesPerOrbit : (float)numParticles));

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			count++;
			particles.RandomVectorInUnitSphere(out Vector3 randPos);
			particles.GetControlPointTransformAtTime(ControlPointNumber, particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p), out Matrix3x4 mat);
			MathLib.MatrixGetColumn(mat, 3, out Vector3 controlPoint);

			float angle = EvenDistribution ? theta : particles.RandomFloat(0.0f, 6.2831855f);
			(float sin, float cos) = MathF.SinCos(count * angle);
			MathLib.VectorRotate(new Vector3(cos * radius, sin * radius, 0.0f), rotation, out Vector3 pointOnCircle);

			float speed = particles.RandomFloat(speedMin, speedMax);
			randPos = randPos * thickness + pointOnCircle;
			MathLib.VectorTransform(randPos, mat, out Vector3 world);

			Vector3 dir = world - controlPoint;
			MathLib.VectorNormalize(ref dir);
			float step = speed * particles.PreviousDt;

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, world);
			if ((attributeWriteMask & PARTICLE_ATTRIBUTE_PREV_XYZ_MASK) != 0) {
				Vector3 prev = world - dir * step;
				if (XYVelocityOnly)
					prev.Z = world.Z;
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prev);
			}
		}
	}
}

public sealed class C_INIT_CreateInEpitrochoid : ParticleOperatorInstance
{
	public int Component1;
	public int Component2;
	public int ControlPointNumber;
	public int ScaleCP;
	public float ParticleDensity;
	public float Offset;
	public float Radius1;
	public float Radius2;
	public bool UseCount;
	public bool UseLocalCoords;
	public bool OffsetExistingPos;

	public static readonly ParticleOperatorDefinition<C_INIT_CreateInEpitrochoid> Definition = new("Position Along Epitrochoid", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_INIT_CreateInEpitrochoid>()
			.Int("control point number", "0", (o, v) => o.ControlPointNumber = v)
			.Int("first dimension 0-2 (-1 disables)", "0", (o, v) => o.Component1 = v)
			.Int("second dimension 0-2 (-1 disables)", "1", (o, v) => o.Component2 = v)
			.Float("radius 1", "40", (o, v) => o.Radius1 = v)
			.Float("radius 2", "24", (o, v) => o.Radius2 = v)
			.Float("point offset", "4", (o, v) => o.Offset = v)
			.Float("particle density", "10", (o, v) => o.ParticleDensity = v)
			.Bool("use particle count instead of creation time", "0", (o, v) => o.UseCount = v)
			.Bool("local space", "0", (o, v) => o.UseLocalCoords = v)
			.Bool("offset from existing position", "0", (o, v) => o.OffsetExistingPos = v)
			.Int("scale from conrol point (radius 1/radius 2/offset)", "-1", (o, v) => o.ScaleCP = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => (1UL << (ScaleCP & 63)) | (1UL << (ControlPointNumber & 63));
	public override int GetRequiredContextBytes() => sizeof(uint);
	public override bool InitMultipleOverride() => OffsetExistingPos;

	static ref uint Count(Span<byte> context) => ref MemoryMarshal.AsRef<uint>(context);

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => Count(context) = 0;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		ScaleCP = Math.Clamp(ScaleCP, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
		ParticleDensity = Math.Max(MathLibConsts.FLT_EPSILON, Math.Min(float.MaxValue, ParticleDensity));
		Component1 = Math.Clamp(Component1, -1, 2);
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		ref uint count = ref Count(context);
		int cp = ParticleInitUtil.CheckControlPoint(ControlPointNumber);
		Vector3 cpPos = particles.GetControlPointAtCurrentTime(cp);
		particles.GetControlPointTransformAtCurrentTime(cp, out Matrix3x4 mat);

		float radiiAdd, offset, radius2;
		if (ScaleCP < 0) {
			offset = Offset;
			radiiAdd = Radius2 + Radius1;
			radius2 = Radius2 + MathLibConsts.FLT_EPSILON;
		}
		else {
			Vector3 scale = particles.GetControlPointAtCurrentTime(ParticleInitUtil.CheckControlPoint(ScaleCP));
			offset = scale.Z * Offset;
			radius2 = scale.Y * Radius2 + MathLibConsts.FLT_EPSILON;
			radiiAdd = scale.X * Radius1 + radius2;
		}

		if (radiiAdd == 0.0f || radius2 == 0.0f)
			return;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float time;
			if (UseCount) {
				time = count * ParticleDensity;
				count++;
			}
			else
				time = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p) * ParticleDensity;

			Vector3 xyz = cpPos;
			Vector3 prev = cpPos;
			if (OffsetExistingPos) {
				xyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
				prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			}

			Vector3 epitrochoid = Vector3.Zero;
			double multiplier = 1.0f / radius2 * time * radiiAdd;
			if (Component1 != -1)
				epitrochoid[Component1] = (float)(Math.Cos(time) * radiiAdd + Math.Cos(multiplier) * offset);
			if (Component2 != -1)
				epitrochoid[Component2] = (float)(Math.Sin(time) * radiiAdd + Math.Sin(multiplier) * offset);

			if (UseLocalCoords)
				MathLib.VectorRotate(epitrochoid, mat, out epitrochoid);

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, xyz + epitrochoid);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prev + epitrochoid);
		}
	}
}

public sealed class C_INIT_ChaoticAttractor : ParticleOperatorInstance
{
	public float AParm;
	public float BParm;
	public float CParm;
	public float DParm;
	public float Scale;
	public float SpeedMin;
	public float SpeedMax;
	public int BaseCP;
	public bool UniformSpeed;

	public static readonly ParticleOperatorDefinition<C_INIT_ChaoticAttractor> Definition = new("Position From Chaotic Attractor", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_ChaoticAttractor>()
			.Float("Pickover A Parameter", "-0.9629629", (o, v) => o.AParm = v)
			.Float("Pickover B Parameter", "2.791139", (o, v) => o.BParm = v)
			.Float("Pickover C Parameter", "1.85185185", (o, v) => o.CParm = v)
			.Float("Pickover D Parameter", "1.5", (o, v) => o.DParm = v)
			.Float("Speed Min", "0", (o, v) => o.SpeedMin = v)
			.Float("Speed Max", "0", (o, v) => o.SpeedMax = v)
			.Bool("Uniform speed", "0", (o, v) => o.UniformSpeed = v)
			.Int("Relative Control point number", "0", (o, v) => o.BaseCP = v)
			.Float("Scale", "1", (o, v) => o.Scale = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => 1UL << (BaseCP & 63);
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Vector3>();

	static ref Vector3 LastPosition(Span<byte> context) => ref MemoryMarshal.AsRef<Vector3>(context);

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => LastPosition(context) = Vector3.Zero;

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		ref Vector3 last = ref LastPosition(context);
		int cp = ParticleInitUtil.CheckControlPoint(BaseCP);
		Vector3 cpPos = particles.GetControlPointAtCurrentTime(cp);
		particles.GetControlPointOrientationAtCurrentTime(cp, out Vector3 fwd, out Vector3 right, out Vector3 up);

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			Vector3 s = last * Scale;
			Vector3 pos = new(
				right.X * s.X + cpPos.X + fwd.X * s.Y + up.X * s.Z,
				right.Y * s.X + cpPos.Y + fwd.Y * s.Y + up.Y * s.Z,
				right.Z * s.X + cpPos.Z + fwd.Z * s.Y + up.Z * s.Z);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, pos);

			Vector3 next = new(
				(float)(Math.Sin(last.Y * AParm) - Math.Cos(last.X * BParm) * last.Z),
				(float)(Math.Sin(last.X * CParm) * last.Z - Math.Cos(last.Y * DParm)),
				MathF.Sin(last.X));

			if (MathF.Abs(SpeedMax) <= 0.0f)
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, pos);
			else {
				Vector3 dx = next - last;
				if (!UniformSpeed) {
					float len = MathF.Sqrt(dx.Y * dx.Y + dx.X * dx.X + dx.Z * dx.Z);
					if (MathF.Abs(len) > 0.0f)
						dx *= 1.0f / len;
				}

				int min = (int)SpeedMin;
				float range = (int)SpeedMax - min + 1;
				float speed = (int)(range * particles.RandomFloat(0.0f, 1.0f)) + min;
				float dt = particles.PreviousDt;
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, new Vector3(
					dx.X * speed * dt + pos.X,
					dx.Y * speed * dt + pos.Y,
					dx.Z * speed * dt + pos.Z));
			}

			last = next;
		}
	}
}

public sealed class C_INIT_PositionPlaceOnGround : ParticleOperatorInstance
{
	public float Offset;
	public float MaxTraceLength;
	public string CollisionGroupName = "NONE";
	public bool KillOnNoCollision;
	public bool IncludeWater;
	public bool SetNormal;
	int CollisionGroupNumber;
	Mask CollisionMask;

	public static readonly ParticleOperatorDefinition<C_INIT_PositionPlaceOnGround> Definition = new("Position Modify Place On Ground", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_INIT_PositionPlaceOnGround>()
			.Float("offset", "0", (o, v) => o.Offset = v)
			.Bool("kill on no collision", "0", (o, v) => o.KillOnNoCollision = v)
			.Bool("include water", "0", (o, v) => o.IncludeWater = v)
			.Bool("set normal", "0", (o, v) => o.SetNormal = v)
			.Float("max trace length", "128", (o, v) => o.MaxTraceLength = v)
			.String("collision group", "NONE", 128, (o, v) => o.CollisionGroupName = v));

	public override uint GetWrittenAttributes() => SetNormal ? 0x200007u : 7u;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		CollisionGroupNumber = def.Mgr.Query().GetCollisionGroupFromName(CollisionGroupName);
		CollisionMask = (Mask)(IncludeWater ? 0x600403b : 0x600400b);
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		IParticleSystemQuery query = particles.Mgr!.Query();
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			Vector3 start = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			Vector3 end = new(start.X, start.Y, start.Z - MaxTraceLength);
			GameTrace tr = default;
			query.TraceLine(start, end, CollisionMask, null, CollisionGroupNumber, ref tr);

			if (tr.Fraction == 1.0f && KillOnNoCollision)
				particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p) = -1.0f;
			else {
				float drop = tr.Fraction * MaxTraceLength - Offset;
				Vector3 xyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
				Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, xyz with { Z = xyz.Z - drop });
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prev with { Z = prev.Z - drop });
			}

			if (SetNormal)
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_NORMAL, p, tr.Plane.Normal);
		}
	}
}

public sealed class C_INIT_MoveBetweenPoints : ParticleOperatorInstance
{
	public float SpeedMin;
	public float SpeedMax;
	public float EndSpread;
	public float StartOffset;
	public float EndOffset;
	public int EndControlPointNumber;
	public bool TrailBias;

	public static readonly ParticleOperatorDefinition<C_INIT_MoveBetweenPoints> Definition = new("Move Particles Between 2 Control Points", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_INIT_MoveBetweenPoints>()
			.Float("minimum speed", "1", (o, v) => o.SpeedMin = v)
			.Float("maximum speed", "1", (o, v) => o.SpeedMax = v)
			.Float("end spread", "0", (o, v) => o.EndSpread = v)
			.Float("start offset", "0", (o, v) => o.StartOffset = v)
			.Float("end offset", "0", (o, v) => o.EndOffset = v)
			.Bool("bias lifetime by trail length", "0", (o, v) => o.TrailBias = v)
			.Int("end control point", "1", (o, v) => o.EndControlPointNumber = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_TRAIL_LENGTH_MASK;
	public override ulong GetReadControlPointMask() => 1UL << (EndControlPointNumber & 63);
	public override bool InitMultipleOverride() => TrailBias;

	public override void InitParams(ParticleSystemDefinition def, Element element)
		=> EndControlPointNumber = Math.Min(EndControlPointNumber, MAX_PARTICLE_CONTROL_POINTS - 1);

	static float Length(in Vector3 d) => MathF.Sqrt(d.Y * d.Y + d.X * d.X + d.Z * d.Z);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		bool moveStartPoint = StartOffset != 0.0f;
		bool moveEndPoint = EndOffset != 0.0f;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			Vector3 start = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			particles.GetControlPointAtTime(ParticleInitUtil.CheckControlPoint(EndControlPointNumber), particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p), out Vector3 end);

			Vector3 randPos = Vector3.Zero;
			if (EndSpread > 0.0f) {
				particles.RandomVectorInUnitSphere(out randPos);
				randPos *= EndSpread;
			}

			end += randPos;
			Vector3 delta = end - start;
			float len = Length(delta);

			if (moveEndPoint) {
				end += EndOffset / (len + MathLibConsts.FLT_EPSILON) * delta;
				delta = end - start;
				len = Length(delta);
			}

			if (moveStartPoint) {
				start += StartOffset / (len + MathLibConsts.FLT_EPSILON) * delta;
				delta = end - start;
				len = Length(delta);
			}

			float vel = particles.RandomFloat(SpeedMin, SpeedMax);
			float life = len / (vel + MathLibConsts.FLT_EPSILON);
			if (TrailBias)
				life += particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_TRAIL_LENGTH, p);
			particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p) = life;

			float scale = vel / (len + MathLibConsts.FLT_EPSILON);
			float dt = particles.PreviousDt;

			if (moveStartPoint) {
				ref float xyz = ref particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_XYZ, p);
				xyz = start.X;
				Unsafe.Add(ref xyz, 1) = start.Y;
				Unsafe.Add(ref xyz, 2) = start.Z;
			}

			Vector3 pos = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, new Vector3(
				pos.X - scale * delta.X * dt,
				pos.Y - delta.Y * scale * dt,
				pos.Z - delta.Z * scale * dt));
		}
	}
}
