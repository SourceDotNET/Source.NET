using Datamodel;

using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

internal static class ParticleRotationUtil
{
	public static Matrix3x4 RotationAboutAxis(in Vector3 axis, float angleDegrees) {
		float radians = angleDegrees * (MathF.PI / 180.0f);
		float sin = MathF.Sin(radians);
		float cos = MathF.Cos(radians);
		float xx = axis.X * axis.X;
		float yy = axis.Y * axis.Y;
		float zz = axis.Z * axis.Z;
		float oneMinusCos = 1 - cos;
		return new Matrix3x4(
			xx + (1 - xx) * cos, axis.X * axis.Y * oneMinusCos - axis.Z * sin, axis.Z * axis.X * oneMinusCos + axis.Y * sin, 0,
			axis.X * axis.Y * oneMinusCos + axis.Z * sin, yy + (1 - yy) * cos, axis.Y * axis.Z * oneMinusCos - axis.X * sin, 0,
			axis.Z * axis.X * oneMinusCos - axis.Y * sin, axis.Y * axis.Z * oneMinusCos + axis.X * sin, zz + (1 - zz) * cos, 0);
	}

	public static float OrientRotation(Vector3 direction, float current, float rotOffset, float spinStrength) {
		direction.Z = 0.0f;
		direction = ParticleOpUtil.NormalizeOrZero(direction);
		float velRot = MathF.Atan2(direction.Y, direction.X) + MathF.PI + rotOffset;
		return ParticleOpUtil.Lerp(spinStrength, current, velRot);
	}
}

public sealed class C_OP_MovementMaintainOffset : ParticleOperatorInstance
{
	public Vector3 Offset;
	public int CP;
	public bool RadiusScale;

	public static readonly ParticleOperatorDefinition<C_OP_MovementMaintainOffset> Definition = new("Movement Maintain Offset", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_MovementMaintainOffset>()
			.Int("Local Space CP", "-1", (o, v) => o.CP = v)
			.Vector3("Desired Offset", "0 0 0", (o, v) => o.Offset = v)
			.Bool("Scale by Radius", "0", (o, v) => o.RadiusScale = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 localOffset = Offset;
		if (CP > -1) {
			particles.GetControlPointTransformAtCurrentTime(Math.Min(CP, MAX_PARTICLE_CONTROL_POINTS - 1), out Matrix3x4 mat);
			localOffset = ParticleOpUtil.Rotate(Offset, mat);
		}
		int count = particles.ActiveParticles;
		Vector3 offsetPos = localOffset * (count - 1) / 2;
		Vector3 avgPos = Vector3.Zero, avgPrevPos = Vector3.Zero;
		for (int p = 0; p < count; p++) {
			avgPos += particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			avgPrevPos += particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
		}
		avgPos /= count;
		avgPrevPos /= count;
		for (int p = 0; p < count; p++) {
			float radius = RadiusScale ? particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_RADIUS, p) : 1.0f;
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, avgPos + offsetPos * radius);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, avgPrevPos + offsetPos * radius);
			offsetPos -= localOffset;
		}
	}
}

public sealed class C_OP_MovementPlaceOnGround : ParticleOperatorInstance
{
	struct PlaceOnGroundContext
	{
		public Vector3 PrevPos1;
		public Vector3 PrevPos2;
		public Vector3 PrevPosLerp;
		public float LerpTime;
	}

	public float Offset;
	public float MaxTraceLength;
	public float Tolerance;
	public float TraceOffset;
	public float LerpRate;
	public string CollisionGroupName = "NONE";
	public int RefCP1;
	public int RefCP2;
	public int LerpCP;
	public bool Kill;
	public bool IncludeWater;
	int CollisionGroupNumber;
	Mask CollisionMask;
	bool UsesCPs;
	bool UsesLerp;

	public static readonly ParticleOperatorDefinition<C_OP_MovementPlaceOnGround> Definition = new("Movement Place On Ground", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_MovementPlaceOnGround>()
			.Float("offset", "0", (o, v) => o.Offset = v)
			.Bool("kill on no collision", "0", (o, v) => o.Kill = v)
			.Bool("include water", "0", (o, v) => o.IncludeWater = v)
			.Float("max trace length", "128", (o, v) => o.MaxTraceLength = v)
			.Float("trace offset", "64", (o, v) => o.TraceOffset = v)
			.String("collision group", "NONE", 128, (o, v) => o.CollisionGroupName = v)
			.Int("reference CP 1", "-1", (o, v) => o.RefCP1 = v)
			.Int("reference CP 2", "-1", (o, v) => o.RefCP2 = v)
			.Float("CP movement tolerance", "32", (o, v) => o.Tolerance = v)
			.Float("interpolation rate", "0", (o, v) => o.LerpRate = v)
			.Int("interploation distance tolerance cp", "-1", (o, v) => o.LerpCP = v));

	public override uint GetWrittenAttributes() => 0x208007;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ_MASK;
	public override ulong GetReadControlPointMask() => (RefCP1 != -1 ? ParticleOpUtil.CPMask(RefCP1) : 0) | (RefCP2 != -1 ? ParticleOpUtil.CPMask(RefCP2) : 0) | (LerpCP != -1 ? ParticleOpUtil.CPMask(LerpCP) : 0);
	public override int GetRequiredContextBytes() => System.Runtime.CompilerServices.Unsafe.SizeOf<PlaceOnGroundContext>();

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		CollisionGroupNumber = def.Mgr.Query().GetCollisionGroupFromName(CollisionGroupName);
		CollisionMask = IncludeWater ? Mask.ShotHull | Mask.SplitAreaPortal : Mask.ShotHull;
		UsesCPs = (RefCP1 > -1 || RefCP2 > -1 || LerpCP > -1) && Tolerance > 0;
		UsesLerp = LerpCP > -1 || LerpRate > 0;
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref PlaceOnGroundContext ctx = ref ParticleOpUtil.Context<PlaceOnGroundContext>(context);
		ctx.PrevPos1 = ParticleInitUtil.Vec3Invalid;
		ctx.PrevPos2 = ParticleInitUtil.Vec3Invalid;
		ctx.PrevPosLerp = ParticleInitUtil.Vec3Invalid;
		ctx.LerpTime = particles.CurTime;
	}

	bool CheckMoved(ParticleCollection particles, int cp, ref Vector3 prev) {
		Vector3 position = particles.GetControlPointAtCurrentTime(cp);
		if (!((position - prev).Length() > Tolerance))
			return false;
		prev = position;
		return true;
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		ref PlaceOnGroundContext ctx = ref ParticleOpUtil.Context<PlaceOnGroundContext>(context);
		bool dirty = false;
		if (UsesCPs) {
			if (RefCP1 > -1 && CheckMoved(particles, RefCP1, ref ctx.PrevPos1)) {
				dirty = true;
				ctx.LerpTime = particles.CurTime;
			}
			if (RefCP2 > -1 && CheckMoved(particles, RefCP2, ref ctx.PrevPos2)) {
				dirty = true;
				ctx.LerpTime = particles.CurTime;
			}
			if (LerpCP > -1)
				CheckMoved(particles, LerpCP, ref ctx.PrevPosLerp);
		}
		else if (!UsesLerp)
			dirty = true;

		float perc = 0;
		if (UsesLerp) {
			if (LerpRate > 0) {
				perc = ParticleOpUtil.RemapClamped(particles.CurTime, ctx.LerpTime, ctx.LerpTime + LerpRate, 0.0f, 1.0f);
				if (perc == 1.0f && !UsesCPs) {
					dirty = true;
					ctx.LerpTime = particles.CurTime;
				}
			}
			else if (LerpCP > -1)
				perc = Math.Clamp((particles.GetControlPointAtCurrentTime(LerpCP) - ctx.PrevPosLerp).Length() / Tolerance, 0.0f, 1.0f);
		}

		if (!dirty && !UsesLerp)
			return;

		IParticleSystemQuery query = particles.Mgr!.Query();
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			Vector3 prevPosition = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			Vector3 desiredZ = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ, p);
			if (UsesLerp && desiredZ.Y != 1) {
				desiredZ = new Vector3(position.Z, 1, position.Z);
				dirty = true;
			}
			if (dirty) {
				Vector3 tracePos = position;
				tracePos.Z += TraceOffset;
				GameTrace trace = new();
				query.TraceLine(tracePos, tracePos + new Vector3(0, 0, -1) * MaxTraceLength, CollisionMask, null, CollisionGroupNumber, ref trace);
				if (trace.Fraction == 1.0f && Kill)
					particles.GetFloatAttributeRefForWrite(PARTICLE_ATTRIBUTE_LIFE_DURATION, p) = -1.0f;
				else {
					float endZ = trace.EndPos.Z + Offset;
					if (UsesLerp) {
						desiredZ.X = desiredZ.Z;
						desiredZ.Z = endZ;
						particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ, p, desiredZ);
					}
					else {
						position.Z = endZ;
						prevPosition.Z = endZ;
					}
				}
			}
			if (UsesLerp) {
				position.Z = ParticleOpUtil.Lerp(perc, desiredZ.X, desiredZ.Z);
				prevPosition.Z = position.Z;
			}
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, position);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prevPosition);
		}
	}
}

public sealed class C_OP_InheritFromParentParticles : ParticleOperatorInstance
{
	public float Scale;
	public int FieldOutput;
	public int Increment;
	public bool RandomDistribution;

	public static readonly ParticleOperatorDefinition<C_OP_InheritFromParentParticles> Definition = new("Inherit Attribute From Parent Particle", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_InheritFromParentParticles>()
			.Int("Inherited Field", "3", (o, v) => o.FieldOutput = v)
			.Float("Scale", "1", (o, v) => o.Scale = v)
			.Bool("Random Parent Particle Distribution", "0", (o, v) => o.RandomDistribution = v)
			.Int("Particle Increment Amount", "1", (o, v) => o.Increment = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 0;
	public override int GetRequiredContextBytes() => sizeof(int);
	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => ParticleOpUtil.Context<int>(context) = 0;
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		ParticleCollection? parent = particles.Parent;
		if (parent == null || parent.ActiveParticles == 0)
			return;
		ref int current = ref ParticleOpUtil.Context<int>(context);
		int lastParent = Math.Max(0, parent.ActiveParticles - 1);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			if (RandomDistribution)
				current = particles.RandomInt(0, lastParent);
			else if (current > lastParent)
				current = 0;
			if (ParticleOpUtil.IsVectorAttribute(FieldOutput)) {
				Vector3 value = parent.GetVectorAttributeValue(FieldOutput, current) * Scale;
				if ((ParticleOpUtil.ATTRIBUTES_WHICH_ARE_COLOR_AND_OPACITY & (1 << FieldOutput)) != 0)
					value = Vector3.Max(Vector3.Min(value, Vector3.One), Vector3.Zero);
				particles.SetVectorAttribute(FieldOutput, p, value);
			}
			else {
				float value = parent.GetFloatAttributeValue(FieldOutput, current) * Scale;
				if (ParticleOpUtil.IsUnitAttribute(FieldOutput))
					value = Math.Clamp(value, 0.0f, 1.0f);
				particles.GetFloatAttributeRefForWrite(FieldOutput, p) = value;
			}
			current += Increment;
		}
	}
}

public sealed class C_OP_OrientTo2dDirection : ParticleOperatorInstance
{
	public float RotOffset;
	public float SpinStrength;
	public int FieldOutput;

	public static readonly ParticleOperatorDefinition<C_OP_OrientTo2dDirection> Definition = new("Rotation Orient to 2D Direction", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_OrientTo2dDirection>()
			.Float("Rotation Offset", "0", (o, v) => o.RotOffset = v)
			.Float("Spin Strength", "1", (o, v) => o.SpinStrength = v)
			.Int("rotation field", "4", (o, v) => o.FieldOutput = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float rotOffset = RotOffset * (MathF.PI / 180.0f);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 velocity = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			ref float roll = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			roll = ParticleRotationUtil.OrientRotation(velocity, roll, rotOffset, SpinStrength);
		}
	}
}

public sealed class C_OP_Orient2DRelToCP : ParticleOperatorInstance
{
	public float RotOffset;
	public float SpinStrength;
	public int CP;
	public int FieldOutput;

	public static readonly ParticleOperatorDefinition<C_OP_Orient2DRelToCP> Definition = new("Rotation Orient Relative to CP", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_Orient2DRelToCP>()
			.Float("Rotation Offset", "0", (o, v) => o.RotOffset = v)
			.Float("Spin Strength", "1", (o, v) => o.SpinStrength = v)
			.Int("Control Point", "0", (o, v) => o.CP = v)
			.Int("rotation field", "4", (o, v) => o.FieldOutput = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override ulong GetReadControlPointMask() => 1UL << CP;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		CP = ParticleOpUtil.ClampCP(CP);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float rotOffset = RotOffset * (MathF.PI / 180.0f);
		Vector3 cp = particles.GetControlPointAtCurrentTime(CP);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 direction = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - cp;
			ref float roll = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			roll = ParticleRotationUtil.OrientRotation(direction, roll, rotOffset, SpinStrength);
		}
	}
}

public sealed class C_OP_RestartAfterDuration : ParticleOperatorInstance
{
	struct RestartContext
	{
		public float LastRestart;
		public float RestartDuration;
	}

	public float DurationMin;
	public float DurationMax;
	public int CP;
	public int CPField;
	public int ChildGroupID;
	public bool OnlyChildren;

	public static readonly ParticleOperatorDefinition<C_OP_RestartAfterDuration> Definition = new("Restart Effect after Duration", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RestartAfterDuration>()
			.Float("Minimum Restart Time", "0", (o, v) => o.DurationMin = v)
			.Float("Maximum Restart Time", "1", (o, v) => o.DurationMax = v)
			.Int("Control Point to Scale Duration", "-1", (o, v) => o.CP = v)
			.Int("Control Point Field X/Y/Z", "0", (o, v) => o.CPField = v)
			.Bool("Only Restart Children", "0", (o, v) => o.OnlyChildren = v)
			.Int("Child Group ID", "0", (o, v) => o.ChildGroupID = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => ParticleOpUtil.CPMask(CP);
	public override int GetRequiredContextBytes() => System.Runtime.CompilerServices.Unsafe.SizeOf<RestartContext>();

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		CP = Math.Clamp(CP, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
		CPField = Math.Clamp(CPField, 0, 2);
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref RestartContext ctx = ref ParticleOpUtil.Context<RestartContext>(context);
		ctx.RestartDuration = particles.RandomFloat(DurationMin, DurationMax);
		ctx.LastRestart = 0;
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (particles.EmissionStopped)
			return;
		ref RestartContext ctx = ref ParticleOpUtil.Context<RestartContext>(context);
		float duration = ctx.RestartDuration;
		if (CP > -1)
			duration *= ParticleOpUtil.Component(particles.GetControlPointAtCurrentTime(CP), CPField);
		if (!(particles.CurTime > duration + ctx.LastRestart))
			return;
		if (OnlyChildren) {
			for (ParticleCollection? child = particles.Children; child != null; child = child.Next) {
				if (child.GetGroupID() == ChildGroupID)
					child.Restart();
			}
		}
		else
			particles.Restart();
		ctx.LastRestart = particles.CurTime;
		ctx.RestartDuration = particles.RandomFloat(DurationMin, DurationMax);
	}
}

public sealed class C_OP_StopAfterCPDuration : ParticleOperatorInstance
{
	public float Duration;
	public int CP;
	public int CPField;
	public bool DestroyImmediately;

	public static readonly ParticleOperatorDefinition<C_OP_StopAfterCPDuration> Definition = new("Stop Effect after Duration", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_StopAfterCPDuration>()
			.Float("Duration at which to Stop", "1", (o, v) => o.Duration = v)
			.Int("Control Point to Scale Duration", "-1", (o, v) => o.CP = v)
			.Int("Control Point Field X/Y/Z", "0", (o, v) => o.CPField = v)
			.Bool("Destroy All Particles Immediately", "0", (o, v) => o.DestroyImmediately = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => ParticleOpUtil.CPMask(CP);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		CP = Math.Clamp(CP, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
		CPField = Math.Clamp(CPField, 0, 2);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (particles.EmissionStopped)
			return;
		float duration = Duration;
		if (CP > -1)
			duration *= ParticleOpUtil.Component(particles.GetControlPointAtCurrentTime(CP), CPField);
		if (particles.CurTime > duration)
			particles.StopEmission(false, DestroyImmediately, true);
	}
}

public sealed class C_OP_SetControlPointRotation : ParticleOperatorInstance
{
	public Vector3 RotAxis;
	public float RotRate;
	public int CP;
	public int LocalCP;

	public static readonly ParticleOperatorDefinition<C_OP_SetControlPointRotation> Definition = new("Set Control Point Rotation", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SetControlPointRotation>()
			.Vector3("Rotation Axis", "0 0 1", (o, v) => o.RotAxis = v)
			.Float("Rotation Rate", "180", (o, v) => o.RotRate = v)
			.Int("Control Point", "0", (o, v) => o.CP = v)
			.Int("Local Space Control Point", "-1", (o, v) => o.LocalCP = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => ParticleOpUtil.CPMask(CP) | (LocalCP != -1 ? ParticleOpUtil.CPMask(LocalCP) : 0);
	public override bool ShouldRunBeforeEmitters() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		MathLib.VectorNormalize(ref RotAxis);
		CP = ParticleOpUtil.ClampCP(CP);
		LocalCP = Math.Clamp(LocalCP, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 axis = RotAxis;
		if (LocalCP > -1) {
			particles.GetControlPointTransformAtCurrentTime(LocalCP, out Matrix3x4 localMat);
			axis = ParticleOpUtil.Rotate(RotAxis, localMat);
		}
		particles.GetControlPointTransformAtCurrentTime(CP, out Matrix3x4 cpMat);
		Matrix3x4 rot = ParticleRotationUtil.RotationAboutAxis(axis, RotRate * particles.Dt);
		MathLib.ConcatTransforms(cpMat, rot, out Matrix3x4 result);
		MathLib.MatrixAngles(result, out Quaternion q, out _);
		particles.SetControlPointOrientation(CP, q);
	}
}

public sealed class C_OP_MovementRotateParticleAroundAxis : ParticleOperatorInstance
{
	public Vector3 RotAxis;
	public float RotRate;
	public int CP;
	public bool LocalSpace;

	public static readonly ParticleOperatorDefinition<C_OP_MovementRotateParticleAroundAxis> Definition = new("Movement Rotate Particle Around Axis", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_MovementRotateParticleAroundAxis>()
			.Vector3("Rotation Axis", "0 0 1", (o, v) => o.RotAxis = v)
			.Float("Rotation Rate", "180", (o, v) => o.RotRate = v)
			.Int("Control Point", "0", (o, v) => o.CP = v)
			.Bool("Use Local Space", "0", (o, v) => o.LocalSpace = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override ulong GetReadControlPointMask() => 1UL << CP;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		MathLib.VectorNormalize(ref RotAxis);
		CP = ParticleOpUtil.ClampCP(CP);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 axis = RotAxis;
		if (LocalSpace) {
			particles.GetControlPointTransformAtCurrentTime(CP, out Matrix3x4 localMat);
			axis = ParticleOpUtil.Rotate(RotAxis, localMat);
		}
		Matrix3x4 rot = ParticleRotationUtil.RotationAboutAxis(axis, RotRate * particles.Dt);
		Vector3 cp = particles.GetControlPointAtCurrentTime(CP);
		int count = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < count; p++) {
			Vector3 xyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			Vector3 cur = (ParticleOpUtil.Rotate(xyz - cp, rot) + cp - xyz) * opStrength;
			Vector3 prevDelta = (ParticleOpUtil.Rotate(prev - cp, rot) + cp - prev) * opStrength;
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, xyz + cur);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prev + prevDelta);
		}
	}
}

public sealed class C_OP_RotateVector : ParticleOperatorInstance
{
	public int FieldOutput;
	public Vector3 RotAxisMin;
	public Vector3 RotAxisMax;
	public float RotRateMin;
	public float RotRateMax;
	public bool Normalize;

	public static readonly ParticleOperatorDefinition<C_OP_RotateVector> Definition = new("Rotate Vector Random", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RotateVector>()
			.Int("output field", "21", (o, v) => o.FieldOutput = v)
			.Vector3("Rotation Axis Min", "0 0 1", (o, v) => o.RotAxisMin = v)
			.Vector3("Rotation Axis Max", "0 0 1", (o, v) => o.RotAxisMax = v)
			.Float("Rotation Rate Min", "180", (o, v) => o.RotRateMin = v)
			.Float("Rotation Rate Max", "180", (o, v) => o.RotRateMax = v)
			.Float("Normalize Ouput", "0", (o, v) => o.Normalize = (BitConverter.SingleToInt32Bits(v) & 0xFF) != 0));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => (1u << FieldOutput) | PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		MathLib.VectorNormalize(ref RotAxisMin);
		MathLib.VectorNormalize(ref RotAxisMax);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		for (int p = 0; p < particles.ActiveParticles; p++) {
			int particleId = particles.GetIntAttributeValue(PARTICLE_ATTRIBUTE_PARTICLE_ID, p);
			float rotRate = particles.RandomFloat(particleId, RotRateMin, RotRateMax) * particles.Dt;
			Vector3 axis = Vector3.Lerp(RotAxisMin, RotAxisMax, particles.RandomFloat(particleId, 0, 1));
			MathLib.VectorNormalize(ref axis);
			Vector3 input = ParticleOpUtil.GetVector(particles, FieldOutput, p);
			Vector3 output = ParticleOpUtil.Rotate(input, ParticleRotationUtil.RotationAboutAxis(axis, rotRate));
			if (Normalize)
				MathLib.VectorNormalize(ref output);
			ParticleOpUtil.SetVector(particles, FieldOutput, p, Vector3.Lerp(input, output, opStrength));
		}
	}
}

public sealed class C_OP_MaxVelocity : ParticleOperatorInstance
{
	public float MaxVelocity;
	public int OverrideCP;
	public int OverrideCPField;

	public static readonly ParticleOperatorDefinition<C_OP_MaxVelocity> Definition = new("Movement Max Velocity", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_MaxVelocity>()
			.Float("Maximum Velocity", "0", (o, v) => o.MaxVelocity = v)
			.Int("Override Max Velocity from this CP", "-1", (o, v) => o.OverrideCP = v)
			.Int("Override CP field", "0", (o, v) => o.OverrideCPField = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override ulong GetReadControlPointMask() => ParticleOpUtil.CPMask(OverrideCP);
	public override void InitParams(ParticleSystemDefinition def, Element element) => OverrideCPField = Math.Clamp(OverrideCPField, 0, 2);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float maxVelocity = MaxVelocity;
		if (OverrideCP >= 0)
			maxVelocity = ParticleOpUtil.Component(particles.GetControlPointAtCurrentTime(ParticleOpUtil.ClampCP(OverrideCP)), OverrideCPField);
		float maxPerFrame = maxVelocity * particles.Dt;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			Vector3 velocity = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - prev;
			float speed = velocity.Length();
			MathLib.VectorNormalize(ref velocity);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, prev + velocity * Math.Min(speed, maxPerFrame));
		}
	}
}

public sealed class C_OP_LagCompensation : ParticleOperatorInstance
{
	public int DesiredVelocityCP;
	public int LatencyCP;
	public int LatencyCPField;
	public int DesiredVelocityCPField;

	public static readonly ParticleOperatorDefinition<C_OP_LagCompensation> Definition = new("Movement Lag Compensation", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_LagCompensation>()
			.Int("Desired Velocity CP", "-1", (o, v) => o.DesiredVelocityCP = v)
			.Int("Desired Velocity CP Field Override(for speed only)", "-1", (o, v) => o.DesiredVelocityCPField = v)
			.Int("Latency CP", "-1", (o, v) => o.LatencyCP = v)
			.Int("Latency CP field", "0", (o, v) => o.LatencyCPField = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => ParticleOpUtil.CPMask(DesiredVelocityCP) | ParticleOpUtil.CPMask(LatencyCP);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		LatencyCPField = Math.Clamp(LatencyCPField, 0, 2);
		DesiredVelocityCPField = Math.Clamp(DesiredVelocityCPField, -1, 2);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (DesiredVelocityCP < 0 || LatencyCP < 0)
			return;
		Vector3 desiredVelocity = particles.GetControlPointAtCurrentTime(ParticleOpUtil.ClampCP(DesiredVelocityCP));
		float latency = ParticleOpUtil.Component(particles.GetControlPointAtCurrentTime(ParticleOpUtil.ClampCP(LatencyCP)), LatencyCPField) + 1.1920929e-07f;
		float desiredSpeed = DesiredVelocityCPField > -1 ? ParticleOpUtil.Component(desiredVelocity, DesiredVelocityCPField) : desiredVelocity.Length();
		float startSpeed = desiredSpeed * 3;
		float catchupTime = latency / 1000.0f;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float currentSpeed = ParticleOpUtil.RemapClamped(ParticleOpUtil.Age(particles, p), 0.0f, catchupTime, startSpeed, desiredSpeed);
			Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			Vector3 velocity = ParticleOpUtil.NormalizeOrZero(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - prev);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, prev + velocity * (currentSpeed * particles.Dt));
		}
	}
}
