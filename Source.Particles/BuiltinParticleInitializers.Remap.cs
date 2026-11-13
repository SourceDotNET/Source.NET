using Datamodel;

using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.InteropServices;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

public sealed class C_INIT_RemapParticleCountToScalar : ParticleOperatorInstance
{
	public int InputMin;
	public int InputMax;
	public int FieldOutput;
	public float OutputMin;
	public float OutputMax;
	public bool ScaleInitialRange;
	public bool ActiveRange;

	public static readonly ParticleOperatorDefinition<C_INIT_RemapParticleCountToScalar> Definition = new("Remap Particle Count to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RemapParticleCountToScalar>()
			.Int("input minimum", "0", (o, v) => o.InputMin = v)
			.Int("input maximum", "10", (o, v) => o.InputMax = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("only active within specified input range", "0", (o, v) => o.ActiveRange = v));

	public override uint GetWrittenAttributes() => 1u << (FieldOutput & 31);
	public override uint GetReadAttributes() => 0;
	public override int GetRequiredContextBytes() => sizeof(int);
	public override bool InitMultipleOverride() => true;

	static ref int Count(Span<byte> context) => ref MemoryMarshal.AsRef<int>(context);

	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => Count(context) = 0;
	public override void Restart(ParticleCollection particles, Span<byte> context) => Count(context) = 0;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleInitUtil.ClampFieldUpper(FieldOutput);
		if ((ParticleInitUtil.ALPHA_ATTRIBUTES_MASK & (1 << (FieldOutput & 31))) != 0) {
			OutputMin = Math.Clamp(OutputMin, 0.0f, 1.0f);
			OutputMax = Math.Clamp(OutputMax, 0.0f, 1.0f);
		}
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		ref int count = ref Count(context);
		if (ActiveRange && count > InputMax)
			return;

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			if (ActiveRange && (count < InputMin || count > InputMax)) {
				count++;
				continue;
			}

			bool valid = ParticleInitUtil.CheckField(FieldOutput);
			float output = ParticleInitUtil.RemapClamped(count, InputMin, InputMax, OutputMin, OutputMax);
			if (valid) {
				ref float attr = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
				if (ScaleInitialRange)
					output *= attr;
				if ((ParticleInitUtil.INT_ATTRIBUTES_MASK & (1 << FieldOutput)) != 0)
					output = (int)output;
				attr = output;
			}
			count++;
		}
	}
}

public sealed class C_INIT_OffsetVectorToVector : ParticleOperatorInstance
{
	public int FieldInput;
	public int FieldOutput;
	public Vector3 OutputMin;
	public Vector3 OutputMax;
	int ControlPointNumber;

	public static readonly ParticleOperatorDefinition<C_INIT_OffsetVectorToVector> Definition = new("Offset Vector to Vector", ParticleOperatorId.Generic, true,
		ParticleOperatorUnpack.Begin<C_INIT_OffsetVectorToVector>()
			.Int("input field", "0", (o, v) => o.FieldInput = v)
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Vector3("output offset minimum", "0 0 0", (o, v) => o.OutputMin = v)
			.Vector3("output offset maximum", "1 1 1", (o, v) => o.OutputMax = v));

	public override uint GetWrittenAttributes() => 1u << (FieldOutput & 31);
	public override uint GetReadAttributes() => 1u << (FieldInput & 31);
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 63);
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = Math.Clamp(FieldOutput, 0, ParticleInitUtil.GMOD_ATTRIBUTE_COUNT - 1);
		FieldInput = Math.Clamp(FieldInput, 0, ParticleInitUtil.GMOD_ATTRIBUTE_COUNT - 1);
		ControlPointNumber = Math.Min(ControlPointNumber, MAX_PARTICLE_CONTROL_POINTS - 1);
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			Vector3 input = particles.GetVectorAttributeValue(FieldInput, p);
			particles.RandomVector(OutputMin, OutputMax, out Vector3 offset);
			particles.SetVectorAttribute(FieldOutput, p, offset + input);
		}
	}
}

public sealed class C_INIT_DistanceToCPInit : ParticleOperatorInstance
{
	public int FieldOutput;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;
	public int StartCP;
	public bool LOS;
	public string CollisionGroupName = "NONE";
	public float MaxTraceLength;
	public float LOSScale;
	public bool ScaleInitialRange;
	public bool ActiveRange;
	int CollisionGroupNumber;

	public static readonly ParticleOperatorDefinition<C_INIT_DistanceToCPInit> Definition = new("Remap Initial Distance to Control Point to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_DistanceToCPInit>()
			.Float("distance minimum", "0", (o, v) => o.InputMin = v)
			.Float("distance maximum", "128", (o, v) => o.InputMax = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Int("control point", "0", (o, v) => o.StartCP = v)
			.Bool("ensure line of sight", "0", (o, v) => o.LOS = v)
			.String("LOS collision group", "NONE", 128, (o, v) => o.CollisionGroupName = v)
			.Float("Maximum Trace Length", "-1", (o, v) => o.MaxTraceLength = v)
			.Float("LOS Failure Scalar", "0", (o, v) => o.LOSScale = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("only active within specified distance", "0", (o, v) => o.ActiveRange = v));

	public override uint GetWrittenAttributes() => 1u << (FieldOutput & 31);
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override ulong GetReadControlPointMask() => 1UL << (StartCP & 63);
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = Math.Clamp(FieldOutput, 0, ParticleInitUtil.GMOD_ATTRIBUTE_COUNT - 1);
		CollisionGroupNumber = def.Mgr.Query().GetCollisionGroupFromName(CollisionGroupName);
		StartCP = Math.Clamp(StartCP, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		if ((ParticleInitUtil.ALPHA_ATTRIBUTES_MASK & (1 << FieldOutput)) != 0) {
			OutputMin = Math.Clamp(OutputMin, 0.0f, 1.0f);
			OutputMax = Math.Clamp(OutputMax, 0.0f, 1.0f);
		}
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		Vector3 cp = particles.GetControlPointAtCurrentTime(ParticleInitUtil.CheckControlPoint(StartCP));
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			Vector3 xyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			Vector3 d = cp - xyz;
			float distance = MathF.Sqrt(d.Y * d.Y + d.X * d.X + d.Z * d.Z);
			if (ActiveRange && (distance < InputMin || distance > InputMax))
				continue;

			if (LOS)
				distance = ParticleCPUtil.LOSDistance(particles, cp, xyz, distance, MaxTraceLength, CollisionGroupNumber, LOSScale);

			float output = ParticleInitUtil.RemapClamped(distance, InputMin, InputMax, OutputMin, OutputMax);
			ref float attr = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			if (ScaleInitialRange)
				output *= attr;
			attr = output;
		}
	}
}

public sealed class C_INIT_RemapInitialDirectionToCPToVector : ParticleOperatorInstance
{
	public int CP;
	public int FieldOutput;
	public float Scale;
	public float OffsetRot;
	public Vector3 OffsetAxis;
	public bool Normalize;

	public static readonly ParticleOperatorDefinition<C_INIT_RemapInitialDirectionToCPToVector> Definition = new("Remap Initial Direction to CP to Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RemapInitialDirectionToCPToVector>()
			.Int("control point", "0", (o, v) => o.CP = v)
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Bool("normalize", "0", (o, v) => o.Normalize = v)
			.Vector3("offset axis", "0 0 0", (o, v) => o.OffsetAxis = v)
			.Float("offset rotation", "0", (o, v) => o.OffsetRot = v)
			.Float("scale factor", "1", (o, v) => o.Scale = v));

	public override uint GetWrittenAttributes() => 1u << (FieldOutput & 31);
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override ulong GetReadControlPointMask() => 1UL << (CP & 63);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		CP = Math.Clamp(CP, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		FieldOutput = ParticleInitUtil.ClampFieldUpper(FieldOutput);
	}

	static Vector3 Rotate(in Matrix4x4 m, in Vector3 v) => new(
		m.M12 * v.Y + m.M11 * v.X + m.M13 * v.Z,
		m.M22 * v.Y + m.M21 * v.X + m.M23 * v.Z,
		m.M32 * v.Y + m.M31 * v.X + m.M33 * v.Z);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		MathLib.MatrixBuildRotationAboutAxis(out Matrix4x4 rot, OffsetAxis, OffsetRot);
		Vector3 cp = particles.GetControlPointAtCurrentTime(ParticleInitUtil.CheckControlPoint(CP));
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			bool valid = ParticleInitUtil.CheckField(FieldOutput);
			Vector3 output = Rotate(rot, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - cp);
			if (Normalize)
				MathLib.VectorNormalize(ref output);
			output *= Scale;
			if (valid)
				particles.SetVectorAttribute(FieldOutput, p, output);
		}
	}

	public override void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context) {
		if (!ParticleInitUtil.CheckField(FieldOutput))
			return;

		MathLib.MatrixBuildRotationAboutAxis(out Matrix4x4 rot, OffsetAxis, OffsetRot);
		Vector3 cp = particles.GetControlPointAtCurrentTime(ParticleInitUtil.CheckControlPoint(CP));
		int count = particles.PaddedActiveParticles * 4;

		if (Normalize) {
			for (int p = 0; p < count; p++) {
				Vector3 r = Rotate(rot, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - cp);
				float lenSq = r.Y * r.Y + r.X * r.X + r.Z * r.Z;
				float inv = MathF.ReciprocalSqrtEstimate(lenSq);
				inv = (3.0f - inv * inv * lenSq) * inv * 0.5f;
				particles.SetVectorAttribute(FieldOutput, p, new Vector3(r.X * inv * Scale, r.Y * inv * Scale, r.Z * inv * Scale));
			}
			return;
		}

		float scale = Scale / Math.Max(particles.PreviousDt, 1e-20f);
		for (int p = 0; p < count; p++) {
			int lane = p & 3;
			Vector3 garbageOffset = new(rot[0, lane], rot[1, lane], rot[2, lane]);
			Vector3 r = Rotate(rot, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - cp + garbageOffset);
			particles.SetVectorAttribute(FieldOutput, p, r * scale);
		}
	}
}

public sealed class C_INIT_RemapScalarToVector : ParticleOperatorInstance
{
	public float StartTime;
	public float EndTime;
	public int FieldInput;
	public float InputMin;
	public float InputMax;
	public int FieldOutput;
	public Vector3 OutputMin;
	public Vector3 OutputMax;
	public bool ScaleInitialRange;
	public bool LocalCoords;
	public int ControlPointNumber;

	public static readonly ParticleOperatorDefinition<C_INIT_RemapScalarToVector> Definition = new("Remap Scalar to Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RemapScalarToVector>()
			.Float("emitter lifetime start time (seconds)", "-1", (o, v) => o.StartTime = v)
			.Float("emitter lifetime end time (seconds)", "-1", (o, v) => o.EndTime = v)
			.Int("input field", "8", (o, v) => o.FieldInput = v)
			.Float("input minimum", "0", (o, v) => o.InputMin = v)
			.Float("input maximum", "1", (o, v) => o.InputMax = v)
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Vector3("output minimum", "0 0 0", (o, v) => o.OutputMin = v)
			.Vector3("output maximum", "1 1 1", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("use local system", "1", (o, v) => o.LocalCoords = v)
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | (1u << (FieldOutput & 31));
	public override uint GetReadAttributes() => 1u << (FieldInput & 31);
	public override ulong GetReadControlPointMask() => 1UL << (ControlPointNumber & 63);
	public override bool InitMultipleOverride() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = Math.Clamp(FieldOutput, 0, ParticleInitUtil.GMOD_ATTRIBUTE_COUNT - 1);
		FieldInput = Math.Clamp(FieldInput, 0, ParticleInitUtil.GMOD_ATTRIBUTE_COUNT - 1);
		ControlPointNumber = Math.Min(ControlPointNumber, MAX_PARTICLE_CONTROL_POINTS - 1);
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			if ((creationTime < StartTime || creationTime >= EndTime) && StartTime != -1.0f && EndTime != -1.0f)
				continue;

			float input = particles.GetFloatAttributeValue(FieldInput, p);
			Vector3 output = new(
				ParticleInitUtil.RemapClamped(input, InputMin, InputMax, OutputMin.X, OutputMax.X),
				ParticleInitUtil.RemapClamped(input, InputMin, InputMax, OutputMin.Y, OutputMax.Y),
				ParticleInitUtil.RemapClamped(input, InputMin, InputMax, OutputMin.Z, OutputMax.Z));

			if (FieldOutput != PARTICLE_ATTRIBUTE_XYZ) {
				if (ScaleInitialRange)
					output *= particles.GetVectorAttributeValue(FieldOutput, p);
				particles.SetVectorAttribute(FieldOutput, p, output);
				continue;
			}

			if (!LocalCoords) {
				particles.GetControlPointAtTime(ParticleInitUtil.CheckControlPoint(ControlPointNumber), creationTime, out Vector3 cp);
				output += cp;
				Vector3 outputPrev = output;
				if (ScaleInitialRange) {
					output *= particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
					outputPrev *= particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
				}
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, output);
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, outputPrev);
			}
			else {
				particles.GetControlPointTransformAtTime(ParticleInitUtil.CheckControlPoint(ControlPointNumber), creationTime, out Matrix3x4 mat);
				MathLib.VectorTransform(output, mat, out output);
				if (ScaleInitialRange)
					output *= particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, output);
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, output);
			}
		}
	}
}

public sealed class C_INIT_RemapCPtoVector : ParticleOperatorInstance
{
	public float StartTime;
	public float EndTime;
	public int CPInput;
	public Vector3 InputMin;
	public Vector3 InputMax;
	public int FieldOutput;
	public Vector3 OutputMin;
	public Vector3 OutputMax;
	public bool ScaleInitialRange;
	public bool Offset;
	public bool Accelerate;
	public int LocalSpaceCP;

	public static readonly ParticleOperatorDefinition<C_INIT_RemapCPtoVector> Definition = new("Remap Control Point to Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RemapCPtoVector>()
			.Float("emitter lifetime start time (seconds)", "-1", (o, v) => o.StartTime = v)
			.Float("emitter lifetime end time (seconds)", "-1", (o, v) => o.EndTime = v)
			.Int("input control point number", "0", (o, v) => o.CPInput = v)
			.Vector3("input minimum", "0 0 0", (o, v) => o.InputMin = v)
			.Vector3("input maximum", "0 0 0", (o, v) => o.InputMax = v)
			.Int("output field", "0", (o, v) => o.FieldOutput = v)
			.Vector3("output minimum", "0 0 0", (o, v) => o.OutputMin = v)
			.Vector3("output maximum", "0 0 0", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("offset position", "0", (o, v) => o.Offset = v)
			.Bool("accelerate position", "0", (o, v) => o.Accelerate = v)
			.Int("local space CP", "-1", (o, v) => o.LocalSpaceCP = v));

	public override uint GetWrittenAttributes() => (1u << (FieldOutput & 31)) | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;

	public override ulong GetReadControlPointMask() {
		ulong mask = 1UL << (CPInput & 63);
		if (LocalSpaceCP != -1)
			mask |= 1UL << (LocalSpaceCP & 63);
		return mask;
	}

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		LocalSpaceCP = Math.Clamp(LocalSpaceCP, -1, MAX_PARTICLE_CONTROL_POINTS - 1);
		CPInput = Math.Clamp(CPInput, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		FieldOutput = ParticleInitUtil.ClampFieldUpper(FieldOutput);
	}

	static Vector3 Rotate(in Vector3 v, in Matrix3x4 m) => new(
		m[0, 1] * v.Y + v.X * m[0, 0] + m[0, 2] * v.Z,
		m[1, 1] * v.Y + m[1, 0] * v.X + m[1, 2] * v.Z,
		m[2, 1] * v.Y + m[2, 0] * v.X + m[2, 2] * v.Z);

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		particles.GetControlPointAtTime(CPInput, particles.CurTime, out Vector3 input);
		Vector3 outMin = OutputMin;
		Vector3 outMax = OutputMax;
		if (LocalSpaceCP != -1) {
			particles.GetControlPointTransformAtTime(LocalSpaceCP, particles.CurTime, out Matrix3x4 mat);
			outMin = Rotate(OutputMin, mat);
			outMax = Rotate(OutputMax, mat);
		}

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float creationTime = particles.GetFloatAttributeValue(PARTICLE_ATTRIBUTE_CREATION_TIME, p);
			if ((creationTime < StartTime || creationTime >= EndTime) && StartTime != -1.0f && EndTime != -1.0f)
				continue;

			bool valid = ParticleInitUtil.CheckField(FieldOutput);
			Vector3 value = new(
				ParticleInitUtil.RemapClamped(input.X, InputMin.X, InputMax.X, outMin.X, outMax.X),
				ParticleInitUtil.RemapClamped(input.Y, InputMin.Y, InputMax.Y, outMin.Y, outMax.Y),
				ParticleInitUtil.RemapClamped(input.Z, InputMin.Z, InputMax.Z, outMin.Z, outMax.Z));

			if (ScaleInitialRange)
				value *= valid ? particles.GetVectorAttributeValue(FieldOutput, p) : Vector3.Zero;

			if (!Accelerate) {
				if (Offset) {
					value += valid ? particles.GetVectorAttributeValue(FieldOutput, p) : Vector3.Zero;
					particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) + value);
				}
			}
			else {
				float dt = particles.Dt;
				if (Offset) {
					Vector3 sum = (valid ? particles.GetVectorAttributeValue(FieldOutput, p) : Vector3.Zero) + value;
					Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
					if (valid)
						particles.SetVectorAttribute(FieldOutput, p, sum * dt + sum);
					particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, sum + prev);
					value.X = dt;
				}
				value *= dt;
			}

			if (valid)
				particles.SetVectorAttribute(FieldOutput, p, value);
		}
	}
}

public sealed class C_INIT_RemapSpeedToScalar : ParticleOperatorInstance
{
	public float StartTime;
	public float EndTime;
	public int ControlPointNumber;
	public bool PerParticle;
	public float InputMin;
	public float InputMax;
	public int FieldOutput;
	public float OutputMin;
	public float OutputMax;
	public bool ScaleInitialRange;

	public static readonly ParticleOperatorDefinition<C_INIT_RemapSpeedToScalar> Definition = new("Remap Speed to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_INIT_RemapSpeedToScalar>()
			.Float("emitter lifetime start time (seconds)", "-1", (o, v) => o.StartTime = v)
			.Float("emitter lifetime end time (seconds)", "-1", (o, v) => o.EndTime = v)
			.Int("control point number (ignored if per particle)", "0", (o, v) => o.ControlPointNumber = v)
			.Bool("per particle", "0", (o, v) => o.PerParticle = v)
			.Float("input minimum", "0", (o, v) => o.InputMin = v)
			.Float("input maximum", "1", (o, v) => o.InputMax = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v));

	public override uint GetWrittenAttributes() => 1u << (FieldOutput & 31);
	public override uint GetReadAttributes() => (1u << (FieldOutput & 31)) | PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		ControlPointNumber = Math.Clamp(ControlPointNumber, 0, MAX_PARTICLE_CONTROL_POINTS - 1);
		FieldOutput = Math.Clamp(FieldOutput, 0, ParticleInitUtil.GMOD_ATTRIBUTE_COUNT - 1);
		InputMin = InputMin <= 0.001f ? 0.001f : InputMin;
		InputMax = InputMax <= 0.001f ? 0.001f : InputMax;
		if (((1 << FieldOutput) & ParticleInitUtil.ALPHA_ATTRIBUTES_MASK) != 0) {
			OutputMin = OutputMin <= 0.0f ? 0.0f : OutputMin;
			OutputMin = 1.0f <= OutputMin ? 1.0f : OutputMin;
			OutputMax = OutputMax <= 0.0f ? 0.0f : OutputMax;
			OutputMax = 1.0f <= OutputMax ? 1.0f : OutputMax;
		}
	}

	bool InTimeRange(float time) => (StartTime <= time && time < EndTime) || StartTime == -1.0f || EndTime == -1.0f;

	static float Speed(in Vector3 delta, float dt) => MathF.Sqrt(delta.Y * delta.Y + delta.X * delta.X + delta.Z * delta.Z) / dt;

	float ControlPointSpeed(ParticleCollection particles) {
		particles.GetControlPointAtPrevTime(ControlPointNumber, out Vector3 prev);
		Vector3 cur = particles.ControlPoints[ParticleInitUtil.CheckControlPoint(ControlPointNumber)].Position;
		return Speed(cur - prev, particles.PreviousDt);
	}

	float ParticleSpeed(ParticleCollection particles, int p)
		=> Speed(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p), particles.PreviousDt);

	float RemapBlock(float speed) {
		float t = (speed - InputMin) / (InputMax == InputMin ? 1.0f : InputMax - InputMin);
		t = 1.0f < t ? 1.0f : t;
		t = 0.0f > t ? 0.0f : t;
		return t * (OutputMax - OutputMin) + OutputMin;
	}

	public override void InitNewParticlesScalar(ParticleCollection particles, int firstParticle, int numParticles, int attributeWriteMask, Span<byte> context) {
		if (!InTimeRange(particles.CurTime))
			return;

		if (!PerParticle) {
			float value = ParticleInitUtil.RemapClamped(ControlPointSpeed(particles), InputMin, InputMax, OutputMin, OutputMax);
			for (int p = firstParticle; p < firstParticle + numParticles; p++) {
				if (ScaleInitialRange)
					value *= particles.GetInitialFloatAttributeValue(FieldOutput, p);
				particles.GetFloatAttributeRefForWrite(FieldOutput, p) = value;
			}
			return;
		}

		for (int p = firstParticle; p < firstParticle + numParticles; p++) {
			float value = ParticleInitUtil.RemapClamped(ParticleSpeed(particles, p), InputMin, InputMax, OutputMin, OutputMax);
			if (ScaleInitialRange)
				value *= particles.GetInitialFloatAttributeValue(FieldOutput, p);
			particles.GetFloatAttributeRefForWrite(FieldOutput, p) = value;
		}
	}

	public override void InitNewParticlesBlock(ParticleCollection particles, int startBlock, int numBlocks, int attributeWriteMask, Span<byte> context) {
		if (!InTimeRange(particles.CurTime))
			return;

		int first = startBlock * 4;
		int end = first + numBlocks * 4;

		if (!PerParticle) {
			float value = RemapBlock(ControlPointSpeed(particles));
			Span<float> lanes = [value, value, value, value];
			for (int p = first; p < end; p++) {
				if (ScaleInitialRange)
					lanes[p & 3] *= particles.GetInitialFloatAttributeValue(FieldOutput, p);
				particles.GetFloatAttributeRefForWrite(FieldOutput, p) = lanes[p & 3];
			}
			return;
		}

		for (int p = first; p < end; p++) {
			float value = RemapBlock(ParticleSpeed(particles, p));
			if (ScaleInitialRange)
				value *= particles.GetInitialFloatAttributeValue(FieldOutput, p);
			particles.GetFloatAttributeRefForWrite(FieldOutput, p) = value;
		}
	}
}
