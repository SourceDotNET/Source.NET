using Datamodel;

using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

internal static class ParticleVectorUtil
{
	public static Vector3 RSqrtNormalize(Vector3 v) => v * (1.0f / MathF.Sqrt(v.LengthSquared()));

	public static float InvPrevDt(ParticleCollection particles) => 1.0f / MathF.Max(1e-20f, particles.PreviousDt);

	public static Vector3 ControlPointVelocity(ParticleCollection particles, int cp) {
		particles.GetControlPointAtPrevTime(cp, out Vector3 prev);
		return particles.GetControlPointAtCurrentTime(cp) - prev;
	}
}

public sealed class C_OP_NormalizeVector : ParticleOperatorInstance
{
	public int FieldOutput;
	public float Scale;

	public static readonly ParticleOperatorDefinition<C_OP_NormalizeVector> Definition = new("Normalize Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_NormalizeVector>()
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Float("scale factor", "1", (o, v) => o.Scale = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 1u << FieldOutput;
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		for (int p = 0; p < particles.ActiveParticles; p++)
			ParticleOpUtil.SetVector(particles, FieldOutput, p, ParticleVectorUtil.RSqrtNormalize(ParticleOpUtil.GetVector(particles, FieldOutput, p)) * Scale);
	}
}

public sealed class C_OP_RemapVelocityToVector : ParticleOperatorInstance
{
	public int FieldOutput;
	public float Scale;
	public bool Normalize;

	public static readonly ParticleOperatorDefinition<C_OP_RemapVelocityToVector> Definition = new("Remap Velocity to Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapVelocityToVector>()
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Bool("normalize", "0", (o, v) => o.Normalize = v)
			.Float("scale factor", "1", (o, v) => o.Scale = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override void InitParams(ParticleSystemDefinition def, Element element) => FieldOutput = ParticleOpUtil.ClampField(FieldOutput);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float scale = Normalize ? Scale : Scale * ParticleVectorUtil.InvPrevDt(particles);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 vel = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			if (Normalize)
				vel = ParticleVectorUtil.RSqrtNormalize(vel);
			ParticleOpUtil.SetVector(particles, FieldOutput, p, vel * scale);
		}
	}
}

public sealed class C_OP_RemapCPVelocityToVector : ParticleOperatorInstance
{
	public int ControlPoint;
	public int FieldOutput;
	public float Scale;
	public bool Normalize;

	public static readonly ParticleOperatorDefinition<C_OP_RemapCPVelocityToVector> Definition = new("Remap CP Velocity to Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapCPVelocityToVector>()
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Int("control point", "0", (o, v) => o.ControlPoint = v)
			.Bool("normalize", "0", (o, v) => o.Normalize = v)
			.Float("scale factor", "1", (o, v) => o.Scale = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => 1UL << ControlPoint;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		ControlPoint = ParticleOpUtil.ClampCP(ControlPoint);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 vel = ParticleVectorUtil.ControlPointVelocity(particles, ControlPoint);
		if (Normalize) {
			MathLib.VectorNormalize(ref vel);
			vel *= Scale;
		}
		else
			vel *= Scale * ParticleVectorUtil.InvPrevDt(particles);
		for (int p = 0; p < particles.ActiveParticles; p++)
			ParticleOpUtil.SetVector(particles, FieldOutput, p, vel);
	}
}

public sealed class C_OP_SetCPOrientationToDirection : ParticleOperatorInstance
{
	public int InputControlPoint;
	public int OutputControlPoint;

	public static readonly ParticleOperatorDefinition<C_OP_SetCPOrientationToDirection> Definition = new("Set CP Orientation to CP Direction", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SetCPOrientationToDirection>()
			.Int("input control point", "0", (o, v) => o.InputControlPoint = v)
			.Int("output control point", "0", (o, v) => o.OutputControlPoint = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => (1UL << (OutputControlPoint & 0x3f)) | (1UL << (InputControlPoint & 0x3f));

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if ((uint)OutputControlPoint > 0x3f)
			return;
		int input = (uint)InputControlPoint > 0x3f ? 0 : InputControlPoint;
		Vector3 forward = ParticleVectorUtil.ControlPointVelocity(particles, input);
		MathLib.VectorNormalize(ref forward);
		MathLib.VectorVectors(forward, out Vector3 right, out Vector3 up);
		particles.SetControlPointOrientation(OutputControlPoint, forward, right, up);
	}
}

public sealed class C_OP_RemapDirectionToCPToVector : ParticleOperatorInstance
{
	public int ControlPoint;
	public int FieldOutput;
	public float Scale;
	public float OffsetRot;
	public Vector3 OffsetAxis;
	public bool Normalize;

	public static readonly ParticleOperatorDefinition<C_OP_RemapDirectionToCPToVector> Definition = new("Remap Direction to CP to Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapDirectionToCPToVector>()
			.Int("control point", "0", (o, v) => o.ControlPoint = v)
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Bool("normalize", "0", (o, v) => o.Normalize = v)
			.Vector3("offset axis", "0 0 0", (o, v) => o.OffsetAxis = v)
			.Float("offset rotation", "0", (o, v) => o.OffsetRot = v)
			.Float("scale factor", "1", (o, v) => o.Scale = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPoint;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		ControlPoint = ParticleOpUtil.ClampCP(ControlPoint);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Matrix3x4 rot = ParticleRotationUtil.RotationAboutAxis(OffsetAxis, OffsetRot);
		Vector3 cp = particles.GetControlPointAtCurrentTime(ControlPoint);
		if (Normalize) {
			for (int p = 0; p < particles.ActiveParticles; p++) {
				Vector3 v = ParticleOpUtil.Rotate(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - cp, rot);
				ParticleOpUtil.SetVector(particles, FieldOutput, p, ParticleVectorUtil.RSqrtNormalize(v) * Scale);
			}
			return;
		}
		float scale = Scale * ParticleVectorUtil.InvPrevDt(particles);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			int lane = p & 3;
			Vector3 offset = new(rot[0, lane], rot[1, lane], rot[2, lane]);
			Vector3 v = ParticleOpUtil.Rotate(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - cp + offset, rot);
			ParticleOpUtil.SetVector(particles, FieldOutput, p, v * scale);
		}
	}
}

public sealed class C_OP_RemapControlPointDirectionToVector : ParticleOperatorInstance
{
	public int FieldOutput;
	public float Scale;
	public int ControlPointNumber;

	public static readonly ParticleOperatorDefinition<C_OP_RemapControlPointDirectionToVector> Definition = new("Remap Control Point Direction to Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapControlPointDirectionToVector>()
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Int("control point number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("scale factor", "1", (o, v) => o.Scale = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		ControlPointNumber = ParticleOpUtil.ClampCP(ControlPointNumber);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 v = particles.ControlPoint(ControlPointNumber).ForwardVector * Scale;
		for (int p = 0; p < particles.ActiveParticles; p++)
			ParticleOpUtil.SetVector(particles, FieldOutput, p, v);
	}
}

public sealed class C_OP_RemapDotProductToScalar : ParticleOperatorInstance
{
	public int InputCP1;
	public int InputCP2;
	public int FieldOutput;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;
	public bool UseParticleVelocity;
	public bool ScaleInitialRange;
	public bool ScaleCurrent;
	public bool ActiveRange;

	public static readonly ParticleOperatorDefinition<C_OP_RemapDotProductToScalar> Definition = new("Remap Dot Product to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapDotProductToScalar>()
			.Bool("use particle velocity for first input", "0", (o, v) => o.UseParticleVelocity = v)
			.Int("first input control point", "0", (o, v) => o.InputCP1 = v)
			.Int("second input control point", "0", (o, v) => o.InputCP2 = v)
			.Float("input minimum (-1 to 1)", "0", (o, v) => o.InputMin = v)
			.Float("input maximum (-1 to 1)", "1", (o, v) => o.InputMax = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("output is scalar of current value", "0", (o, v) => o.ScaleCurrent = v)
			.Bool("only active within specified input range", "0", (o, v) => o.ActiveRange = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadInitialAttributes() => ScaleInitialRange ? 1u << FieldOutput : 0;
	public override ulong GetReadControlPointMask() => (1UL << InputCP2) | (1UL << InputCP1);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		InputCP1 = ParticleOpUtil.ClampCP(InputCP1);
		InputCP2 = ParticleOpUtil.ClampCP(InputCP2);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float outMin = OutputMin;
		float outMax = OutputMax;
		if (ParticleOpUtil.IsUnitAttribute(FieldOutput)) {
			outMin = Math.Clamp(outMin, 0.0f, 1.0f);
			outMax = Math.Clamp(outMax, 0.0f, 1.0f);
		}

		particles.GetControlPointOrientationAtTime(InputCP1, particles.CurTime, out Vector3 forward1, out _, out _);
		particles.GetControlPointOrientationAtTime(InputCP2, particles.CurTime, out Vector3 forward2, out _, out _);
		float input = Vector3.Dot(forward1, forward2);
		if (ActiveRange && !UseParticleVelocity && (input < InputMin || input > InputMax))
			return;

		bool isInt = (ATTRIBUTES_WHICH_ARE_INTS & (1 << FieldOutput)) != 0;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			if (UseParticleVelocity) {
				Vector3 vel = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
				MathLib.VectorNormalize(ref vel);
				float velInput = Vector3.Dot(vel, forward2);
				if (ActiveRange && (velInput < InputMin || velInput > InputMax))
					continue;
			}

			float output = ParticleOpUtil.RemapClamped(input, InputMin, InputMax, outMin, outMax);
			if (ScaleInitialRange)
				output *= particles.GetInitialFloatAttributeValue(FieldOutput, p);
			ref float value = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			if (ScaleCurrent)
				output *= value;
			value = isInt ? (int)output : output;
		}
	}
}

public sealed class C_OP_RemapCPtoScalar : ParticleOperatorInstance
{
	public int CPInput;
	public int FieldOutput;
	public int Field;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;
	public float StartTime;
	public float EndTime;
	public bool ScaleInitialRange;
	public bool ScaleCurrent;

	public static readonly ParticleOperatorDefinition<C_OP_RemapCPtoScalar> Definition = new("Remap Control Point to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapCPtoScalar>()
			.Float("emitter lifetime start time (seconds)", "-1", (o, v) => o.StartTime = v)
			.Float("emitter lifetime end time (seconds)", "-1", (o, v) => o.EndTime = v)
			.Int("input control point number", "0", (o, v) => o.CPInput = v)
			.Float("input minimum", "0", (o, v) => o.InputMin = v)
			.Float("input maximum", "1", (o, v) => o.InputMax = v)
			.Int("input field 0-2 X/Y/Z", "0", (o, v) => o.Field = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("output is scalar of current value", "0", (o, v) => o.ScaleCurrent = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 0;
	public override uint GetReadInitialAttributes() => ScaleInitialRange ? 1u << FieldOutput : 0;
	public override ulong GetReadControlPointMask() => 1UL << CPInput;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		CPInput = ParticleOpUtil.ClampCP(CPInput);
		Field = Math.Clamp(Field, 0, 2);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float outMin = OutputMin;
		float outMax = OutputMax;
		if (ParticleOpUtil.IsUnitAttribute(FieldOutput)) {
			outMin = Math.Clamp(outMin, 0.0f, 1.0f);
			outMax = Math.Clamp(outMax, 0.0f, 1.0f);
		}
		float input = ParticleOpUtil.Component(particles.GetControlPointAtCurrentTime(CPInput), Field);
		bool isInt = (ATTRIBUTES_WHICH_ARE_INTS & (1 << FieldOutput)) != 0;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			if (!(StartTime <= creationTime && creationTime < EndTime) && StartTime != -1.0f && EndTime != -1.0f)
				continue;
			float output = ParticleOpUtil.RemapClamped(input, InputMin, InputMax, outMin, outMax);
			if (ScaleInitialRange)
				output *= particles.GetInitialFloatAttributeValue(FieldOutput, p);
			ref float value = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			if (ScaleCurrent)
				output *= value;
			value = isInt ? (int)output : output;
		}
	}
}

public sealed class C_OP_RemapCPtoVector : ParticleOperatorInstance
{
	public int CPInput;
	public int FieldOutput;
	public int LocalSpaceCP;
	public Vector3 InputMin;
	public Vector3 InputMax;
	public Vector3 OutputMin;
	public Vector3 OutputMax;
	public float StartTime;
	public float EndTime;
	public bool ScaleInitialRange;
	public bool ScaleCurrent;
	public bool Offset;
	public bool Accelerate;

	public static readonly ParticleOperatorDefinition<C_OP_RemapCPtoVector> Definition = new("Remap Control Point to Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_RemapCPtoVector>()
			.Float("emitter lifetime start time (seconds)", "-1", (o, v) => o.StartTime = v)
			.Float("emitter lifetime end time (seconds)", "-1", (o, v) => o.EndTime = v)
			.Int("input control point number", "0", (o, v) => o.CPInput = v)
			.Vector3("input minimum", "0 0 0", (o, v) => o.InputMin = v)
			.Vector3("input maximum", "0 0 0", (o, v) => o.InputMax = v)
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Vector3("output minimum", "0 0 0", (o, v) => o.OutputMin = v)
			.Vector3("output maximum", "0 0 0", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("output is scalar of current value", "0", (o, v) => o.ScaleCurrent = v)
			.Bool("offset position", "0", (o, v) => o.Offset = v)
			.Bool("accelerate position", "0", (o, v) => o.Accelerate = v)
			.Int("local space CP", "-1", (o, v) => o.LocalSpaceCP = v));

	public override uint GetWrittenAttributes() => (1u << FieldOutput) | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override uint GetReadInitialAttributes() => ScaleInitialRange ? 1u << FieldOutput : 0;
	public override ulong GetReadControlPointMask() => (1UL << CPInput) | (LocalSpaceCP != -1 ? 1UL << LocalSpaceCP : 0);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		CPInput = ParticleOpUtil.ClampCP(CPInput);
		LocalSpaceCP = Math.Clamp(LocalSpaceCP, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
	}

	static float Remap(float value, float inMin, float inMax, float outMin, float outMax) => ParticleOpUtil.RemapClamped(value, inMin, inMax, outMin, outMax);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 cp = particles.GetControlPointAtCurrentTime(CPInput);
		Vector3 outMin = OutputMin;
		Vector3 outMax = OutputMax;
		if (LocalSpaceCP != -1) {
			particles.GetControlPointTransformAtTime(LocalSpaceCP, particles.CurTime, out Matrix3x4 mat);
			outMin = ParticleOpUtil.Rotate(outMin, mat);
			outMax = ParticleOpUtil.Rotate(outMax, mat);
		}

		bool isColor = (ParticleOpUtil.ATTRIBUTES_WHICH_ARE_COLOR_AND_OPACITY & (1 << FieldOutput)) != 0;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			if (!(StartTime <= creationTime && creationTime < EndTime) && StartTime != -1.0f && EndTime != -1.0f)
				continue;

			Vector3 output = new(
				Remap(cp.X, InputMin.X, InputMax.X, outMin.X, outMax.X),
				Remap(cp.Y, InputMin.Y, InputMax.Y, outMin.Y, outMax.Y),
				Remap(cp.Z, InputMin.Z, InputMax.Z, outMin.Z, outMax.Z));
			if (ScaleInitialRange)
				output *= ParticleOpUtil.GetInitialVector(particles, FieldOutput, p);
			Vector3 current = ParticleOpUtil.GetVector(particles, FieldOutput, p);
			if (ScaleCurrent)
				output *= current.X;

			if (isColor) {
				ParticleOpUtil.SetVector(particles, FieldOutput, p, Vector3.Clamp(output, Vector3.Zero, Vector3.One));
				continue;
			}

			Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			if (Accelerate) {
				if (Offset) {
					output += current;
					particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prev + output);
					ParticleOpUtil.SetVector(particles, FieldOutput, p, output + output * particles.Dt);
				}
				else
					ParticleOpUtil.SetVector(particles, FieldOutput, p, output * particles.Dt);
				continue;
			}

			if (Offset) {
				output += current;
				prev += output;
			}
			else
				prev = output;
			ParticleOpUtil.SetVector(particles, FieldOutput, p, output);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, prev);
		}
	}
}

public sealed class C_OP_MaintainSequentialPath : ParticleOperatorInstance
{
	struct SequentialPositionContext
	{
		public int ParticleCount;
		public float Step;
		public int CountAmount;
		public int Padding;
		public Vector3 PrevPosStart;
		public Vector3 PrevPosEnd;
	}

	public float MaxDistance;
	public float NumToAssign;
	public float CohesionStrength;
	public float Tolerance;
	public bool Loop;
	public bool UseParticleCount;
	public PathParameters PathParams;

	public static readonly ParticleOperatorDefinition<C_OP_MaintainSequentialPath> Definition = new("Movement Maintain Position Along Path", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_MaintainSequentialPath>()
			.Float("maximum distance", "0", (o, v) => o.MaxDistance = v)
			.Float("bulge", "0", (o, v) => o.PathParams.Bulge = v)
			.Int("start control point number", "0", (o, v) => o.PathParams.StartControlPointNumber = v)
			.Int("end control point number", "0", (o, v) => o.PathParams.EndControlPointNumber = v)
			.Int("bulge control 0=random 1=orientation of start pnt 2=orientation of end point", "0", (o, v) => o.PathParams.BulgeControl = v)
			.Float("mid point position", "0.5", (o, v) => o.PathParams.MidPoint = v)
			.Float("particles to map from start to end", "100", (o, v) => o.NumToAssign = v)
			.Bool("restart behavior (0 = bounce, 1 = loop )", "1", (o, v) => o.Loop = v)
			.Float("cohesion strength", "1", (o, v) => o.CohesionStrength = v)
			.Bool("use existing particle count", "0", (o, v) => o.UseParticleCount = v)
			.Float("control point movement tolerance", "0", (o, v) => o.Tolerance = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => 0;
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<SequentialPositionContext>();
	public override void InitParams(ParticleSystemDefinition def, Element element) => PathParams.ClampControlPointIndices();

	public override ulong GetReadControlPointMask() {
		ulong startMask = (1UL << PathParams.StartControlPointNumber) - 1;
		ulong endMask = (1UL << ((PathParams.EndControlPointNumber + 1) & 0x3f)) - 1;
		return endMask & ~startMask;
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		ref SequentialPositionContext ctx = ref ParticleOpUtil.Context<SequentialPositionContext>(context);
		ctx.ParticleCount = 0;
		ctx.Step = NumToAssign > 1.0f ? 1.0f / (NumToAssign - 1.0f) : 0.0f;
		ctx.CountAmount = 1;
		if (Tolerance > 0.0f) {
			ctx.PrevPosStart = ParticleInitUtil.Vec3Invalid;
			ctx.PrevPosEnd = ParticleInitUtil.Vec3Invalid;
		}
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		ref SequentialPositionContext ctx = ref ParticleOpUtil.Context<SequentialPositionContext>(context);
		if (Tolerance > 0.0f) {
			Vector3 start = particles.GetControlPointAtCurrentTime(PathParams.StartControlPointNumber);
			Vector3 end = particles.GetControlPointAtCurrentTime(PathParams.EndControlPointNumber);
			if ((start - ctx.PrevPosStart).Length() < Tolerance && (end - ctx.PrevPosEnd).Length() < Tolerance)
				return;
			ctx.PrevPosStart = start;
			ctx.PrevPosEnd = end;
		}

		float cohesion = 1.0f - CohesionStrength;
		float numToAssign = NumToAssign;
		if (UseParticleCount) {
			numToAssign = particles.ActiveParticles;
			ctx.Step = numToAssign > 1.0f ? 1.0f / (numToAssign - 1.0f) : 0.0f;
		}

		for (int p = 0; p < particles.ActiveParticles; p++) {
			particles.CalculatePathValues(PathParams, particles.CurTime, out Vector3 startPnt, out Vector3 midP, out Vector3 endPnt);
			if (ctx.ParticleCount >= numToAssign || ctx.ParticleCount < 0) {
				if (Loop)
					ctx.ParticleCount = 0;
				else {
					ctx.CountAmount = -ctx.CountAmount;
					ctx.ParticleCount = Math.Max(1, (int)MathF.Min(ctx.ParticleCount, numToAssign - 1.0f));
				}
			}

			float t = ctx.ParticleCount * ctx.Step;
			Vector3 l0 = startPnt + t * (midP - startPnt);
			Vector3 l1 = midP + t * (endPnt - midP);
			Vector3 pnt = l0 + (l1 - l0) * t;

			Vector3 xyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - pnt;
			Vector3 pxyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) - pnt;
			float xyzOffset = MathF.Min(xyz.Length(), MaxDistance);
			float pxyzOffset = MathF.Min(pxyz.Length(), MaxDistance);
			MathLib.VectorNormalize(ref xyz);
			MathLib.VectorNormalize(ref pxyz);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, xyz * (xyzOffset * cohesion) + pnt);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, pxyz * (pxyzOffset * cohesion) + pnt);
			ctx.ParticleCount += ctx.CountAmount;
		}
	}
}

public sealed class C_OP_LockToSavedSequentialPath : ParticleOperatorInstance
{
	public bool CPPairs;
	public PathParameters PathParams;
	int PathCount;

	public static readonly ParticleOperatorDefinition<C_OP_LockToSavedSequentialPath> Definition = new("Movement Lock to Saved Position Along Path", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_LockToSavedSequentialPath>()
			.Float("bulge", "0", (o, v) => o.PathParams.Bulge = v)
			.Int("start control point number", "0", (o, v) => o.PathParams.StartControlPointNumber = v)
			.Int("end control point number", "1", (o, v) => o.PathParams.EndControlPointNumber = v)
			.Int("bulge control 0=random 1=orientation of start pnt 2=orientation of end point", "0", (o, v) => o.PathParams.BulgeControl = v)
			.Float("mid point position", "0.5", (o, v) => o.PathParams.MidPoint = v)
			.Bool("Use sequential CP pairs between start and end point", "0", (o, v) => o.CPPairs = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_HITBOX_INDEX_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ_MASK;
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Vector3>() * MAX_PARTICLE_CONTROL_POINTS * 3;

	public override ulong GetReadControlPointMask() {
		ulong startMask = (1UL << PathParams.StartControlPointNumber) - 1;
		ulong endMask = CPPairs ? ulong.MaxValue : (1UL << ((PathParams.EndControlPointNumber + 1) & 0x3f)) - 1;
		return endMask & ~startMask;
	}

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		PathParams.ClampControlPointIndices();
		PathCount = CPPairs ? Math.Max(1, PathParams.EndControlPointNumber - PathParams.StartControlPointNumber) : 1;
	}

	static Span<Vector3> Points(Span<byte> context) => System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Vector3>(context);

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) {
		Span<Vector3> prevStart = Points(context)[..MAX_PARTICLE_CONTROL_POINTS];
		for (int i = 0; i < PathCount; i++)
			prevStart[i] = ParticleInitUtil.Vec3Invalid;
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Span<Vector3> points = Points(context);
		Span<Vector3> prevStart = points[..MAX_PARTICLE_CONTROL_POINTS];
		Span<Vector3> prevMid = points.Slice(MAX_PARTICLE_CONTROL_POINTS, MAX_PARTICLE_CONTROL_POINTS);
		Span<Vector3> prevEnd = points.Slice(MAX_PARTICLE_CONTROL_POINTS * 2, MAX_PARTICLE_CONTROL_POINTS);

		Span<Vector3> startPnt = stackalloc Vector3[MAX_PARTICLE_CONTROL_POINTS];
		Span<Vector3> midP = stackalloc Vector3[MAX_PARTICLE_CONTROL_POINTS];
		Span<Vector3> endPnt = stackalloc Vector3[MAX_PARTICLE_CONTROL_POINTS];
		Span<Vector3> delta0 = stackalloc Vector3[MAX_PARTICLE_CONTROL_POINTS];
		Span<Vector3> delta1 = stackalloc Vector3[MAX_PARTICLE_CONTROL_POINTS];
		Span<Vector3> prevDelta0 = stackalloc Vector3[MAX_PARTICLE_CONTROL_POINTS];
		Span<Vector3> prevDelta1 = stackalloc Vector3[MAX_PARTICLE_CONTROL_POINTS];

		for (int i = 0; i < PathCount; i++) {
			PathParameters current = PathParams;
			if (CPPairs) {
				current.StartControlPointNumber += i;
				current.EndControlPointNumber = current.StartControlPointNumber + 1;
			}
			particles.CalculatePathValues(current, particles.CurTime, out startPnt[i], out midP[i], out endPnt[i]);
			if (prevStart[i] == ParticleInitUtil.Vec3Invalid)
				particles.CalculatePathValues(current, particles.CurTime - particles.Dt, out prevStart[i], out prevMid[i], out prevEnd[i]);
			delta0[i] = midP[i] - startPnt[i];
			delta1[i] = endPnt[i] - midP[i];
			prevDelta0[i] = prevMid[i] - prevStart[i];
			prevDelta1[i] = prevEnd[i] - prevMid[i];
		}

		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 saved = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ, p);
			float bias = MathF.Min(ParticleOpUtil.Age(particles, p), particles.Dt) / particles.Dt;
			float t = saved.X;
			int cp = Math.Min((int)MathF.Max(0.0f, saved.Y - PathParams.StartControlPointNumber), MAX_PARTICLE_CONTROL_POINTS - 1);

			Vector3 prevL0 = prevStart[cp] + t * prevDelta0[cp];
			Vector3 prevL1 = prevMid[cp] + t * prevDelta1[cp];
			Vector3 prevPnt = prevL0 + (prevL1 - prevL0) * t;
			Vector3 l0 = startPnt[cp] + t * delta0[cp];
			Vector3 l1 = midP[cp] + t * delta1[cp];
			Vector3 pnt = (l0 + (l1 - l0) * t - prevPnt) * bias;

			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) + pnt);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) + pnt);
		}

		for (int i = 0; i < PathCount; i++) {
			prevStart[i] = startPnt[i];
			prevMid[i] = midP[i];
			prevEnd[i] = endPnt[i];
		}
	}
}

public sealed class C_OP_NormalLock : ParticleOperatorInstance
{
	public int ControlPointNumber;

	public static readonly ParticleOperatorDefinition<C_OP_NormalLock> Definition = new("Normal Lock to Control Point", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_NormalLock>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_NORMAL_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;
	public override int GetRequiredContextBytes() => Unsafe.SizeOf<Matrix3x4>();
	public override void InitParams(ParticleSystemDefinition def, Element element) => ControlPointNumber = ParticleOpUtil.ClampCP(ControlPointNumber);

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) =>
		particles.GetControlPointTransformAtTime(ControlPointNumber, particles.CurTime, out ParticleOpUtil.Context<Matrix3x4>(context));

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		ref Matrix3x4 prevTransform = ref ParticleOpUtil.Context<Matrix3x4>(context);
		particles.GetControlPointTransformAtTime(ControlPointNumber, particles.CurTime, out Matrix3x4 current);
		MathLib.MatrixInvert(prevTransform, out Matrix3x4 inverse);
		MathLib.ConcatTransforms(current, inverse, out Matrix3x4 delta);

		for (int p = 0; p < particles.ActiveParticles; p++) {
			float bias = MathF.Min(ParticleOpUtil.Age(particles, p), particles.Dt) / particles.Dt;
			Vector3 normal = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_NORMAL, p);
			Vector3 rotated = ParticleOpUtil.Rotate(normal, delta);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_NORMAL, p, normal + (rotated - normal) * bias);
		}

		prevTransform = current;
	}
}

public sealed class C_OP_SetControlPointToImpactPoint : ParticleOperatorInstance
{
	public int CPOut;
	public int CPIn;
	public float UpdateRate;
	public float TraceLength;
	public float Offset;
	public Vector3 TraceDir;
	public string CollisionGroupName = "NONE";
	int CollisionGroupNumber;

	public static readonly ParticleOperatorDefinition<C_OP_SetControlPointToImpactPoint> Definition = new("Set Control Point to Impact Point", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SetControlPointToImpactPoint>()
			.Int("Control Point to Set", "1", (o, v) => o.CPOut = v)
			.Int("Control Point to Trace From", "1", (o, v) => o.CPIn = v)
			.Vector3("Trace Direction Override", "0 0 0", (o, v) => o.TraceDir = v)
			.Float("Trace Update Rate", "0.5", (o, v) => o.UpdateRate = v)
			.Float("Max Trace Length", "1024", (o, v) => o.TraceLength = v)
			.Float("Offset End Point Amount", "0", (o, v) => o.Offset = v)
			.String("trace collision group", "NONE", 128, (o, v) => o.CollisionGroupName = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override bool ShouldRunBeforeEmitters() => true;
	public override int GetRequiredContextBytes() => sizeof(float);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		CollisionGroupNumber = def.Mgr.Query().GetCollisionGroupFromName(CollisionGroupName);
		CPIn = ParticleOpUtil.ClampCP(CPIn);
		CPOut = ParticleOpUtil.ClampCP(CPOut);
	}

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => ParticleOpUtil.Context<float>(context) = -UpdateRate;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		ref float nextUpdateTime = ref ParticleOpUtil.Context<float>(context);
		if (nextUpdateTime > particles.CurTime)
			return;

		Vector3 dir = TraceDir;
		if (dir == Vector3.Zero)
			particles.GetControlPointOrientationAtTime(CPIn, particles.CurTime, out dir, out _, out _);

		Vector3 start = particles.GetControlPointAtCurrentTime(CPIn);
		GameTrace trace = new();
		particles.Mgr!.Query().TraceLine(start, start + dir * TraceLength, Mask.All, null, CollisionGroupNumber, ref trace);

		Vector3 normal = trace.Plane.Normal;
		MathLib.VectorVectors(normal, out Vector3 right, out Vector3 up);
		particles.SetControlPoint(CPOut, trace.EndPos - dir * Offset);
		particles.SetControlPointOrientation(CPOut, normal, right, up);
		nextUpdateTime = UpdateRate + particles.CurTime;
	}
}
