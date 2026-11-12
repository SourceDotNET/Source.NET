using Datamodel;

using Source.Common;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

using static Source.Particles.ParticleConstants;

namespace Source.Particles;

internal static class ParticleCPUtil
{
	public static void OrientationFromMovement(ParticleCollection particles, int p, out Vector3 forward, out Vector3 right, out Vector3 up) {
		forward = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
		MathLib.VectorNormalize(ref forward);
		MathLib.VectorVectors(forward, out right, out up);
	}

	public static void SetFromParticle(ParticleCollection target, int cp, ParticleCollection source, int p, bool setOrientation) {
		target.SetControlPoint(cp, source.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p));
		if (setOrientation) {
			OrientationFromMovement(source, p, out Vector3 forward, out Vector3 right, out Vector3 up);
			target.SetControlPointOrientation(cp, forward, right, up);
		}
	}

	public static float LOSDistance(ParticleCollection particles, in Vector3 start, in Vector3 end, float distance, float maxTraceLength, int collisionGroup, float losScale) {
		Vector3 endPoint = end;
		if (maxTraceLength != -1.0f && maxTraceLength < distance) {
			MathLib.VectorNormalize(ref endPoint);
			endPoint = endPoint * maxTraceLength + start;
		}
		GameTrace trace = new();
		particles.Mgr!.Query().TraceLine(start, endPoint, Mask.OpaqueAndNPCs, null, collisionGroup, ref trace);
		if (trace.Fraction != 1.0f)
			distance *= trace.Fraction * losScale;
		return distance;
	}

	public static float ClosestPointFraction(in Vector3 p, in Vector3 a, in Vector3 b) {
		Vector3 dir = b - a;
		float div = Vector3.Dot(dir, dir);
		return div < 0.00001f ? 0.0f : (Vector3.Dot(dir, p) - Vector3.Dot(dir, a)) / div;
	}

	public static float PercentageBetween(in Vector3 cp1, in Vector3 cp2, in Vector3 position, bool radial) {
		if (radial) {
			float distance = (cp1 - cp2).Length();
			float particleDistance = (cp1 - position).Length() + 1.1920929e-07f;
			return 1.0f / (distance / particleDistance);
		}
		return ClosestPointFraction(position, cp1, cp2);
	}
}

public sealed class C_OP_SetChildControlPoints : ParticleOperatorInstance
{
	public int ChildGroupID;
	public int FirstControlPoint;
	public int NumControlPoints;
	public int FirstSourcePoint;
	public bool SetOrientation;

	public static readonly ParticleOperatorDefinition<C_OP_SetChildControlPoints> Definition = new("Set child control points from particle positions", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SetChildControlPoints>()
			.Int("Group ID to affect", "0", (o, v) => o.ChildGroupID = v)
			.Int("First control point to set", "0", (o, v) => o.FirstControlPoint = v)
			.Int("# of control points to set", "1", (o, v) => o.NumControlPoints = v)
			.Int("first particle to copy", "0", (o, v) => o.FirstSourcePoint = v)
			.Bool("set orientation", "0", (o, v) => o.SetOrientation = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int first = ParticleOpUtil.ClampCP(FirstControlPoint);
		int toSet = Math.Min(particles.ActiveParticles - FirstSourcePoint, NumControlPoints);
		toSet = Math.Min(toSet, MAX_PARTICLE_CONTROL_POINTS - first);
		if (toSet <= 0)
			return;
		for (ParticleCollection? child = particles.Children; child != null; child = child.Next) {
			if (child.GetGroupID() != ChildGroupID)
				continue;
			for (int p = 0; p < toSet; p++)
				ParticleCPUtil.SetFromParticle(child, p + first, particles, p + FirstSourcePoint, SetOrientation);
		}
	}
}

public sealed class C_OP_SetControlPointsToParticle : ParticleOperatorInstance
{
	public int FirstControlPoint;
	public int NumControlPoints;
	public int FirstSourcePoint;
	public bool SetOrientation;

	public static readonly ParticleOperatorDefinition<C_OP_SetControlPointsToParticle> Definition = new("Set control points from particle positions", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SetControlPointsToParticle>()
			.Int("First control point to set", "0", (o, v) => o.FirstControlPoint = v)
			.Int("# of control points to set", "1", (o, v) => o.NumControlPoints = v)
			.Int("first particle to copy", "0", (o, v) => o.FirstSourcePoint = v)
			.Bool("set orientation", "0", (o, v) => o.SetOrientation = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int first = ParticleOpUtil.ClampCP(FirstControlPoint);
		int toSet = Math.Min(particles.ActiveParticles - FirstSourcePoint, NumControlPoints);
		toSet = Math.Min(toSet, MAX_PARTICLE_CONTROL_POINTS - first);
		for (int p = 0; p < toSet; p++)
			ParticleCPUtil.SetFromParticle(particles, p + first, particles, p + FirstSourcePoint, SetOrientation);
	}
}

public sealed class C_OP_SetPerChildControlPoint : ParticleOperatorInstance
{
	public int ChildGroupID;
	public int FirstControlPoint;
	public int NumControlPoints;
	public int FirstSourcePoint;
	public bool SetOrientation;

	public static readonly ParticleOperatorDefinition<C_OP_SetPerChildControlPoint> Definition = new("Set per child control point from particle positions", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SetPerChildControlPoint>()
			.Int("Group ID to affect", "0", (o, v) => o.ChildGroupID = v)
			.Int("control point to set", "0", (o, v) => o.FirstControlPoint = v)
			.Int("# of children to set", "1", (o, v) => o.NumControlPoints = v)
			.Int("first particle to copy", "0", (o, v) => o.FirstSourcePoint = v)
			.Bool("set orientation", "0", (o, v) => o.SetOrientation = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_PARTICLE_ID;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		int childCount = 0;
		for (ParticleCollection? child = particles.Children; child != null; child = child.Next)
			childCount++;
		int toSet = Math.Min(NumControlPoints, Math.Min(particles.ActiveParticles - FirstSourcePoint, childCount));
		if (toSet <= 0)
			return;
		int current = FirstSourcePoint;
		int cp = ParticleOpUtil.ClampCP(FirstControlPoint);
		for (ParticleCollection? child = particles.Children; child != null && toSet > 0; child = child.Next) {
			if (child.GetGroupID() != ChildGroupID)
				continue;
			ParticleCPUtil.SetFromParticle(child, cp, particles, current, SetOrientation);
			toSet--;
			current++;
		}
	}
}

public sealed class C_OP_SetControlPointPositions : ParticleOperatorInstance
{
	public bool UseWorldLocation;
	public readonly int[] CP = new int[4];
	public readonly int[] CPParent = new int[4];
	public readonly Vector3[] CPPos = new Vector3[4];
	public int HeadLocation;

	public static readonly ParticleOperatorDefinition<C_OP_SetControlPointPositions> Definition = new("Set Control Point Positions", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SetControlPointPositions>()
			.Int("First Control Point Number", "1", (o, v) => o.CP[0] = v)
			.Int("First Control Point Parent", "0", (o, v) => o.CPParent[0] = v)
			.Vector3("First Control Point Location", "128 0 0", (o, v) => o.CPPos[0] = v)
			.Int("Second Control Point Number", "2", (o, v) => o.CP[1] = v)
			.Int("Second Control Point Parent", "0", (o, v) => o.CPParent[1] = v)
			.Vector3("Second Control Point Location", "0 128 0", (o, v) => o.CPPos[1] = v)
			.Int("Third Control Point Number", "3", (o, v) => o.CP[2] = v)
			.Int("Third Control Point Parent", "0", (o, v) => o.CPParent[2] = v)
			.Vector3("Third Control Point Location", "-128 0 0", (o, v) => o.CPPos[2] = v)
			.Int("Fourth Control Point Number", "4", (o, v) => o.CP[3] = v)
			.Int("Fourth Control Point Parent", "0", (o, v) => o.CPParent[3] = v)
			.Vector3("Fourth Control Point Location", "0 -128 0", (o, v) => o.CPPos[3] = v)
			.Bool("Set positions in world space", "0", (o, v) => o.UseWorldLocation = v)
			.Int("Control Point to offset positions from", "0", (o, v) => o.HeadLocation = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override bool ShouldRunBeforeEmitters() => true;

	public override ulong GetReadControlPointMask() {
		ulong mask = ParticleOpUtil.CPMask(CP[0]) | ParticleOpUtil.CPMask(CP[1]) | ParticleOpUtil.CPMask(CP[2]) | ParticleOpUtil.CPMask(CP[3]);
		return UseWorldLocation ? mask : mask | ParticleOpUtil.CPMask(HeadLocation);
	}

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		for (int i = 0; i < 4; i++)
			CP[i] = ParticleOpUtil.ClampCP(CP[i]);
		HeadLocation = ParticleOpUtil.ClampCP(HeadLocation);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Matrix3x4 mat = default;
		if (!UseWorldLocation)
			particles.GetControlPointTransformAtTime(HeadLocation, particles.CurTime, out mat);
		for (int i = 0; i < 4; i++) {
			particles.SetControlPoint(CP[i], UseWorldLocation ? CPPos[i] : ParticleOpUtil.Transform(CPPos[i], mat));
			particles.SetControlPointParent(CP[i], CPParent[i]);
		}
	}
}

public sealed class C_OP_DampenToCP : ParticleOperatorInstance
{
	public int ControlPointNumber;
	public float Range;
	public float Scale;

	public static readonly ParticleOperatorDefinition<C_OP_DampenToCP> Definition = new("Movement Dampen Relative to Control Point", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_DampenToCP>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("falloff range", "100", (o, v) => o.Range = v)
			.Float("dampen scale", "1", (o, v) => o.Scale = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_CREATION_TIME_MASK | PARTICLE_ATTRIBUTE_LIFE_DURATION_MASK | PARTICLE_ATTRIBUTE_PARTICLE_ID_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;
	public override void InitParams(ParticleSystemDefinition def, Element element) => ControlPointNumber = ParticleOpUtil.ClampCP(ControlPointNumber);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (Range <= 0.0f)
			return;
		Vector3 controlPoint = particles.GetControlPointAtCurrentTime(ControlPointNumber);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			float distance = (position - controlPoint).Length();
			if (distance > Range)
				continue;
			float dampen = MathF.Pow(distance / Range, Scale);
			Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			Vector3 dampened = prev + (position - prev) * dampen;
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, Vector3.Lerp(position, dampened, opStrength));
		}
	}
}

public sealed class C_OP_DistanceBetweenCPs : ParticleOperatorInstance
{
	public int FieldOutput;
	public int StartCP;
	public int EndCP;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;
	public float MaxTraceLength;
	public float LOSScale;
	public string CollisionGroupName = "NONE";
	public bool LOS;
	public bool ScaleInitialRange;
	public bool ScaleCurrent;
	int CollisionGroupNumber;

	public static readonly ParticleOperatorDefinition<C_OP_DistanceBetweenCPs> Definition = new("Remap Distance Between Two Control Points to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_DistanceBetweenCPs>()
			.Float("distance minimum", "0", (o, v) => o.InputMin = v)
			.Float("distance maximum", "128", (o, v) => o.InputMax = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Int("starting control point", "0", (o, v) => o.StartCP = v)
			.Int("ending control point", "1", (o, v) => o.EndCP = v)
			.Bool("ensure line of sight", "0", (o, v) => o.LOS = v)
			.String("LOS collision group", "NONE", 128, (o, v) => o.CollisionGroupName = v)
			.Float("Maximum Trace Length", "-1", (o, v) => o.MaxTraceLength = v)
			.Float("LOS Failure Scalar", "0", (o, v) => o.LOSScale = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("output is scalar of current value", "0", (o, v) => o.ScaleCurrent = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => 0;
	public override uint GetReadInitialAttributes() => 1u << FieldOutput;
	public override ulong GetReadControlPointMask() => (1UL << StartCP) | (1UL << EndCP);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		CollisionGroupNumber = def.Mgr.Query().GetCollisionGroupFromName(CollisionGroupName);
		StartCP = ParticleOpUtil.ClampCP(StartCP);
		EndCP = ParticleOpUtil.ClampCP(EndCP);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float min = OutputMin, max = OutputMax;
		if (ParticleOpUtil.IsUnitAttribute(FieldOutput)) {
			min = Math.Clamp(OutputMin, 0.0f, 1.0f);
			max = Math.Clamp(OutputMax, 0.0f, 1.0f);
		}
		Vector3 cp1 = particles.GetControlPointAtCurrentTime(StartCP);
		Vector3 cp2 = particles.GetControlPointAtCurrentTime(EndCP);
		float distance = (cp1 - cp2).Length();
		if (LOS)
			distance = ParticleCPUtil.LOSDistance(particles, cp1, cp2, distance, MaxTraceLength, CollisionGroupNumber, LOSScale);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float output = ParticleOpUtil.RemapClamped(distance, InputMin, InputMax, min, max);
			ref float value = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			if (ScaleInitialRange)
				output *= particles.GetInitialFloatAttributeValue(FieldOutput, p);
			if (ScaleCurrent)
				output *= value;
			value = ParticleOpUtil.Lerp(opStrength, value, output);
		}
	}
}

public sealed class C_OP_DistanceBetweenCPsToCP : ParticleOperatorInstance
{
	public int StartCP;
	public int EndCP;
	public int OutputCP;
	public int OutputCPField;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;
	public float MaxTraceLength;
	public float LOSScale;
	public bool LOS;
	public string CollisionGroupName = "NONE";
	int CollisionGroupNumber;

	public static readonly ParticleOperatorDefinition<C_OP_DistanceBetweenCPsToCP> Definition = new("Remap Distance Between Two Control Points to CP", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_DistanceBetweenCPsToCP>()
			.Float("distance minimum", "0", (o, v) => o.InputMin = v)
			.Float("distance maximum", "128", (o, v) => o.InputMax = v)
			.Int("output control point", "2", (o, v) => o.OutputCP = v)
			.Int("output control point field", "0", (o, v) => o.OutputCPField = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Int("starting control point", "0", (o, v) => o.StartCP = v)
			.Int("ending control point", "1", (o, v) => o.EndCP = v)
			.Bool("ensure line of sight", "0", (o, v) => o.LOS = v)
			.String("LOS collision group", "NONE", 128, (o, v) => o.CollisionGroupName = v)
			.Float("Maximum Trace Length", "-1", (o, v) => o.MaxTraceLength = v)
			.Float("LOS Failure Scale", "0", (o, v) => o.LOSScale = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => (1UL << StartCP) | (1UL << EndCP) | (1UL << OutputCP);
	public override bool ShouldRunBeforeEmitters() => true;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		CollisionGroupNumber = def.Mgr.Query().GetCollisionGroupFromName(CollisionGroupName);
		StartCP = ParticleOpUtil.ClampCP(StartCP);
		EndCP = ParticleOpUtil.ClampCP(EndCP);
		OutputCP = ParticleOpUtil.ClampCP(OutputCP);
		OutputCPField = Math.Clamp(OutputCPField, 0, 2);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 cp1 = particles.GetControlPointAtCurrentTime(StartCP);
		Vector3 cp2 = particles.GetControlPointAtCurrentTime(EndCP);
		float distance = (cp1 - cp2).Length();
		if (LOS)
			distance = ParticleCPUtil.LOSDistance(particles, cp1, cp2, distance, MaxTraceLength, CollisionGroupNumber, LOSScale);
		distance = ParticleOpUtil.RemapClamped(distance, InputMin, InputMax, OutputMin, OutputMax);
		ParticleOpUtil.SetControlPointComponent(particles, OutputCP, OutputCPField, distance);
	}
}

public sealed class C_OP_PercentageBetweenCPs : ParticleOperatorInstance
{
	public int FieldOutput;
	public float InputMin;
	public float InputMax;
	public float OutputMin;
	public float OutputMax;
	public int StartCP;
	public int EndCP;
	public bool ScaleInitialRange;
	public bool ScaleCurrent;
	public bool ActiveRange;
	public bool RadialCheck;

	public static readonly ParticleOperatorDefinition<C_OP_PercentageBetweenCPs> Definition = new("Remap Percentage Between Two Control Points to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_PercentageBetweenCPs>()
			.Float("percentage minimum", "0", (o, v) => o.InputMin = v)
			.Float("percentage maximum", "1", (o, v) => o.InputMax = v)
			.Int("output field", "3", (o, v) => o.FieldOutput = v)
			.Float("output minimum", "0", (o, v) => o.OutputMin = v)
			.Float("output maximum", "1", (o, v) => o.OutputMax = v)
			.Int("starting control point", "0", (o, v) => o.StartCP = v)
			.Int("ending control point", "1", (o, v) => o.EndCP = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("output is scalar of current value", "0", (o, v) => o.ScaleCurrent = v)
			.Bool("only active within input range", "0", (o, v) => o.ActiveRange = v)
			.Bool("treat distance between points as radius", "1", (o, v) => o.RadialCheck = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override uint GetReadInitialAttributes() => 1u << FieldOutput;
	public override ulong GetReadControlPointMask() => (1UL << StartCP) | (1UL << EndCP);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		StartCP = ParticleOpUtil.ClampCP(StartCP);
		EndCP = ParticleOpUtil.ClampCP(EndCP);
		if (ParticleOpUtil.IsUnitAttribute(FieldOutput)) {
			OutputMin = Math.Clamp(OutputMin, 0.0f, 1.0f);
			OutputMax = Math.Clamp(OutputMax, 0.0f, 1.0f);
		}
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 cp1 = particles.GetControlPointAtCurrentTime(StartCP);
		Vector3 cp2 = particles.GetControlPointAtCurrentTime(EndCP);
		int count = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < count; p++) {
			float perc = ParticleCPUtil.PercentageBetween(cp1, cp2, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p), RadialCheck);
			float output = ParticleOpUtil.RemapClamped(perc, InputMin, InputMax, OutputMin, OutputMax);
			ref float value = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			if (ScaleInitialRange)
				output *= particles.GetInitialFloatAttributeValue(FieldOutput, p);
			if (ScaleCurrent)
				output *= value;
			if (!ActiveRange || (perc >= InputMin && perc <= InputMax))
				value = output;
		}
	}
}

public sealed class C_OP_PercentageBetweenCPsVector : ParticleOperatorInstance
{
	public int FieldOutput;
	public float InputMin;
	public float InputMax;
	public Vector3 OutputMin;
	public Vector3 OutputMax;
	public int StartCP;
	public int EndCP;
	public bool ScaleInitialRange;
	public bool ScaleCurrent;
	public bool ActiveRange;
	public bool RadialCheck;

	public static readonly ParticleOperatorDefinition<C_OP_PercentageBetweenCPsVector> Definition = new("Remap Percentage Between Two Control Points to Vector", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_PercentageBetweenCPsVector>()
			.Float("percentage minimum", "0", (o, v) => o.InputMin = v)
			.Float("percentage maximum", "1", (o, v) => o.InputMax = v)
			.Int("output field", "6", (o, v) => o.FieldOutput = v)
			.Vector3("output minimum", "0 0 0", (o, v) => o.OutputMin = v)
			.Vector3("output maximum", "1 1 1", (o, v) => o.OutputMax = v)
			.Int("starting control point", "0", (o, v) => o.StartCP = v)
			.Int("ending control point", "1", (o, v) => o.EndCP = v)
			.Bool("output is scalar of initial random range", "0", (o, v) => o.ScaleInitialRange = v)
			.Bool("output is scalar of current value", "0", (o, v) => o.ScaleCurrent = v)
			.Bool("only active within input range", "0", (o, v) => o.ActiveRange = v)
			.Bool("treat distance between points as radius", "1", (o, v) => o.RadialCheck = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override uint GetReadInitialAttributes() => 1u << FieldOutput;
	public override ulong GetReadControlPointMask() => (1UL << StartCP) | (1UL << EndCP);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		StartCP = ParticleOpUtil.ClampCP(StartCP);
		EndCP = ParticleOpUtil.ClampCP(EndCP);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 cp1 = particles.GetControlPointAtCurrentTime(StartCP);
		Vector3 cp2 = particles.GetControlPointAtCurrentTime(EndCP);
		int count = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < count; p++) {
			float perc = ParticleCPUtil.PercentageBetween(cp1, cp2, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p), RadialCheck);
			Vector3 output = new(
				ParticleOpUtil.RemapClamped(perc, InputMin, InputMax, OutputMin.X, OutputMax.X),
				ParticleOpUtil.RemapClamped(perc, InputMin, InputMax, OutputMin.Y, OutputMax.Y),
				ParticleOpUtil.RemapClamped(perc, InputMin, InputMax, OutputMin.Z, OutputMax.Z));
			if (ScaleInitialRange)
				output *= ParticleOpUtil.GetInitialVector(particles, FieldOutput, p);
			if (ScaleCurrent)
				output *= ParticleOpUtil.GetVector(particles, FieldOutput, p);
			if (!ActiveRange || (perc >= InputMin && perc <= InputMax))
				ParticleOpUtil.SetVector(particles, FieldOutput, p, output);
		}
	}
}

public sealed class C_OP_DistanceToCP : ParticleOperatorInstance
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
	public bool ScaleCurrent;
	public bool ActiveRange;
	int CollisionGroupNumber;

	public static readonly ParticleOperatorDefinition<C_OP_DistanceToCP> Definition = new("Remap Distance to Control Point to Scalar", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_DistanceToCP>()
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
			.Bool("output is scalar of current value", "0", (o, v) => o.ScaleCurrent = v)
			.Bool("only active within specified distance", "0", (o, v) => o.ActiveRange = v));

	public override uint GetWrittenAttributes() => 1u << FieldOutput;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override uint GetReadInitialAttributes() => 1u << FieldOutput;
	public override ulong GetReadControlPointMask() => 1UL << StartCP;

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		FieldOutput = ParticleOpUtil.ClampField(FieldOutput);
		CollisionGroupNumber = def.Mgr.Query().GetCollisionGroupFromName(CollisionGroupName);
		StartCP = ParticleOpUtil.ClampCP(StartCP);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		float min = OutputMin, max = OutputMax;
		if (ParticleOpUtil.IsUnitAttribute(FieldOutput)) {
			min = Math.Clamp(OutputMin, 0.0f, 1.0f);
			max = Math.Clamp(OutputMax, 0.0f, 1.0f);
		}
		Vector3 cp = particles.GetControlPointAtCurrentTime(StartCP);
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 position = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			float distance = (cp - position).Length();
			if (ActiveRange && (distance < InputMin || distance > InputMax))
				continue;
			if (LOS)
				distance = ParticleCPUtil.LOSDistance(particles, cp, position, distance, MaxTraceLength, CollisionGroupNumber, LOSScale);
			float output = ParticleOpUtil.RemapClamped(distance, InputMin, InputMax, min, max);
			ref float value = ref particles.GetFloatAttributeRefForWrite(FieldOutput, p);
			if (ScaleInitialRange)
				output *= particles.GetInitialFloatAttributeValue(FieldOutput, p);
			if (ScaleCurrent)
				output *= value;
			value = ParticleOpUtil.Lerp(opStrength, value, output);
		}
	}
}

public sealed class C_OP_SetControlPointToPlayer : ParticleOperatorInstance
{
	public int CP1;
	public Vector3 CP1Pos;
	public bool OrientToEyes;

	public static readonly ParticleOperatorDefinition<C_OP_SetControlPointToPlayer> Definition = new("Set Control Point To Player", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SetControlPointToPlayer>()
			.Int("Control Point Number", "1", (o, v) => o.CP1 = v)
			.Vector3("Control Point Offset", "0 0 0", (o, v) => o.CP1Pos = v)
			.Bool("Use Eye Orientation", "0", (o, v) => o.OrientToEyes = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override bool ShouldRunBeforeEmitters() => true;
	public override void InitParams(ParticleSystemDefinition def, Element element) => CP1 = ParticleOpUtil.ClampCP(CP1);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		IParticleSystemQuery query = particles.Mgr!.Query();
		particles.SetControlPoint(CP1, CP1Pos + query.GetLocalPlayerPos());
		query.GetLocalPlayerEyeVectors(out Vector3 forward, out Vector3 right, out Vector3 up);
		if (!OrientToEyes && MathF.Abs(forward.Z - 1.0f) > 1e-3f) {
			forward.Z = 0;
			MathLib.VectorNormalize(ref forward);
			up = new Vector3(0, 0, 1);
			right = new Vector3(forward.Y, -forward.X, 0.0f);
		}
		particles.SetControlPointOrientation(CP1, forward, right, up);
	}
}

public sealed class C_OP_LockToBone : ParticleOperatorInstance
{
	public int ControlPointNumber;
	public float LifeTimeFadeStart;
	public float LifeTimeFadeEnd;
	public string HitboxSetName = "effects";

	public static readonly ParticleOperatorDefinition<C_OP_LockToBone> Definition = new("Movement Lock to Bone", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_LockToBone>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Float("lifetime fade start", "0", (o, v) => o.LifeTimeFadeStart = v)
			.Float("lifetime fade end", "0", (o, v) => o.LifeTimeFadeEnd = v)
			.String("hitbox set", "effects", 128, (o, v) => o.HitboxSetName = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK | PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ_MASK | PARTICLE_ATTRIBUTE_HITBOX_INDEX_MASK | PARTICLE_ATTRIBUTE_CREATION_TIME_MASK;
	public override ulong GetReadControlPointMask() => 1UL << ControlPointNumber;
	public override void InitParams(ParticleSystemDefinition def, Element element) => ControlPointNumber = ParticleOpUtil.ClampCP(ControlPointNumber);

	static Vector3 BoxPoint(in ModelHitBoxInfo box, in Vector3 uvw)
		=> ParticleOpUtil.Transform(new Vector3(
			ParticleOpUtil.Lerp(uvw.X, box.BoxMins.X, box.BoxMaxes.X),
			ParticleOpUtil.Lerp(uvw.Y, box.BoxMins.Y, box.BoxMaxes.Y),
			ParticleOpUtil.Lerp(uvw.Z, box.BoxMins.Z, box.BoxMaxes.Z)), box.Transform);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		particles.UpdateHitBoxInfo(ControlPointNumber, HitboxSetName);
		ModelHitBoxesInfo info = particles.ControlPointHitBoxes[ControlPointNumber];
		if (!info.CurAndPrevValid())
			return;

		float ageThreshold = LifeTimeFadeEnd <= 0.0f ? 1.0e20f : LifeTimeFadeEnd;
		float iScale = LifeTimeFadeEnd > LifeTimeFadeStart ? 1.0f / (LifeTimeFadeEnd - LifeTimeFadeStart) : 0.0f;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			float age = ParticleOpUtil.Age(particles, p);
			float frameBias = Math.Min(age, particles.Dt) * (1.0f / particles.Dt);
			if (!(age < ageThreshold))
				continue;
			int box = particles.GetIntAttributeValue(PARTICLE_ATTRIBUTE_HITBOX_INDEX, p);
			if (box < 0 || box >= info.NumHitBoxes || box >= info.NumPrevHitBoxes)
				continue;
			Vector3 uvw = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_HITBOX_RELATIVE_XYZ, p);
			Vector3 delta = (BoxPoint(info.HitBoxes![box], uvw) - BoxPoint(info.PrevBoxes![box], uvw)) * frameBias;
			if (age > LifeTimeFadeStart)
				delta *= opStrength * (1.0f - (age - LifeTimeFadeStart) * iScale);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) + delta);
			particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_PREV_XYZ, p, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p) + delta);
		}
	}
}

public sealed class C_OP_CPOffsetToPercentageBetweenCPs : ParticleOperatorInstance
{
	public float InputMin;
	public float InputMax;
	public float InputBias;
	public int StartCP;
	public int EndCP;
	public int OffsetCP;
	public int OutputCP;
	public int InputCP;
	public bool RadialCheck;
	public bool ScaleOffset;
	public Vector3 Offset;

	public static readonly ParticleOperatorDefinition<C_OP_CPOffsetToPercentageBetweenCPs> Definition = new("Set CP Offset to CP Percentage Between Two Control Points", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_CPOffsetToPercentageBetweenCPs>()
			.Float("percentage minimum", "0", (o, v) => o.InputMin = v)
			.Float("percentage maximum", "1", (o, v) => o.InputMax = v)
			.Float("percentage bias", ".5", (o, v) => o.InputBias = v)
			.Int("starting control point", "0", (o, v) => o.StartCP = v)
			.Int("ending control point", "1", (o, v) => o.EndCP = v)
			.Int("offset control point", "2", (o, v) => o.OffsetCP = v)
			.Int("input control point", "3", (o, v) => o.InputCP = v)
			.Int("output control point", "4", (o, v) => o.OutputCP = v)
			.Vector3("offset amount", "0 0 0", (o, v) => o.Offset = v)
			.Bool("treat distance between points as radius", "1", (o, v) => o.RadialCheck = v)
			.Bool("treat offset as scale of total distance", "0", (o, v) => o.ScaleOffset = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override ulong GetReadControlPointMask() => (1UL << StartCP) | (1UL << EndCP) | (1UL << OffsetCP) | (1UL << OutputCP);

	public override void InitParams(ParticleSystemDefinition def, Element element) {
		StartCP = ParticleOpUtil.ClampCP(StartCP);
		EndCP = ParticleOpUtil.ClampCP(EndCP);
		OffsetCP = ParticleOpUtil.ClampCP(OffsetCP);
		OutputCP = ParticleOpUtil.ClampCP(OutputCP);
		InputCP = ParticleOpUtil.ClampCP(InputCP);
	}

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 cp1 = particles.GetControlPointAtCurrentTime(StartCP);
		Vector3 cp2 = particles.GetControlPointAtCurrentTime(EndCP);
		Vector3 cpOffset = particles.GetControlPointAtCurrentTime(OffsetCP);
		Vector3 cpInput = particles.GetControlPointAtCurrentTime(InputCP);
		float totalDistance = (cp1 - cp2).Length();
		Vector3 offset = ScaleOffset ? Offset * totalDistance : Offset;
		float percentage = ParticleCPUtil.PercentageBetween(cp1, cp2, cpInput, RadialCheck);
		percentage = ParticleOpUtil.RemapClamped(percentage, InputMin, InputMax, 0.0f, 1.0f);
		percentage = ParticleOpUtil.Bias(percentage, ParticleOpUtil.PreCalcBiasParameter(InputBias));
		particles.SetControlPoint(OutputCP, cpOffset + offset * percentage);
	}
}

public sealed class C_OP_PlaneCull : ParticleOperatorInstance
{
	public int PlaneControlPoint;
	public Vector3 PlaneDirection;
	public float PlaneOffset;

	public static readonly ParticleOperatorDefinition<C_OP_PlaneCull> Definition = new("Cull when crossing plane", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_PlaneCull>()
			.Int("Control Point for point on plane", "0", (o, v) => o.PlaneControlPoint = v)
			.Float("Cull plane offset", "0", (o, v) => o.PlaneOffset = v)
			.Vector3("Plane Normal", "0 0 1", (o, v) => o.PlaneDirection = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override ulong GetReadControlPointMask() => ParticleOpUtil.CPMask(PlaneControlPoint);
	public override void InitParams(ParticleSystemDefinition def, Element element) => PlaneControlPoint = ParticleOpUtil.ClampCP(PlaneControlPoint);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 normal = Vector3.Normalize(PlaneDirection);
		Vector3 point = particles.GetControlPointAtCurrentTime(PlaneControlPoint) - normal * PlaneOffset;
		int limit = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < limit; p++) {
			float plane = Vector3.Dot(particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - point, normal);
			if (plane < 0.0f || (plane == 0.0f && float.IsNegative(plane)))
				particles.KillParticle(p);
		}
	}
}

public sealed class C_OP_DistanceCull : ParticleOperatorInstance
{
	public int ControlPoint;
	public Vector3 PointOffset;
	public float Distance;
	public bool CullInside;

	public static readonly ParticleOperatorDefinition<C_OP_DistanceCull> Definition = new("Cull when crossing sphere", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_DistanceCull>()
			.Int("Control Point", "0", (o, v) => o.ControlPoint = v)
			.Float("Cull Distance", "0", (o, v) => o.Distance = v)
			.Vector3("Control Point offset", "0 0 0", (o, v) => o.PointOffset = v)
			.Bool("Cull inside instead of outside", "0", (o, v) => o.CullInside = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override ulong GetReadControlPointMask() => ParticleOpUtil.CPMask(ControlPoint);
	public override void InitParams(ParticleSystemDefinition def, Element element) => ControlPoint = ParticleOpUtil.ClampCP(ControlPoint);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		Vector3 cullPosition = particles.GetControlPointAtCurrentTime(ControlPoint) + PointOffset;
		int limit = ParticleOpUtil.PaddedCount(particles);
		for (int p = 0; p < limit; p++) {
			float distance = (particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p) - cullPosition).Length();
			if (CullInside ? distance < Distance : distance > Distance)
				particles.KillParticle(p);
		}
	}
}

public sealed class C_OP_ModelCull : ParticleOperatorInstance
{
	public int ControlPointNumber;
	public bool BoundBox;
	public bool CullOutside;
	public string HitboxSetName = "effects";

	public static readonly ParticleOperatorDefinition<C_OP_ModelCull> Definition = new("Cull relative to model", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_ModelCull>()
			.Int("control_point_number", "0", (o, v) => o.ControlPointNumber = v)
			.Bool("use only bounding box", "0", (o, v) => o.BoundBox = v)
			.Bool("cull outside instead of inside", "0", (o, v) => o.CullOutside = v)
			.String("hitbox set", "effects", 128, (o, v) => o.HitboxSetName = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK;
	public override void InitParams(ParticleSystemDefinition def, Element element) => ControlPointNumber = ParticleOpUtil.ClampCP(ControlPointNumber);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		if (particles.ControlPoint(ControlPointNumber).Object == null)
			return;
		particles.UpdateHitBoxInfo(ControlPointNumber, HitboxSetName);
		if (!particles.ControlPointHitBoxes[ControlPointNumber].CurAndPrevValid())
			return;
		IParticleSystemQuery query = particles.Mgr!.Query();
		for (int p = 0; p < particles.ActiveParticles; p++) {
			bool inside = query.IsPointInControllingObjectHitBox(particles, ControlPointNumber, particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p), BoundBox);
			if (inside != CullOutside)
				particles.KillParticle(p);
		}
	}
}

public sealed class C_OP_SetControlPointToCenter : ParticleOperatorInstance
{
	public int CP1;
	public Vector3 CP1Pos;

	public static readonly ParticleOperatorDefinition<C_OP_SetControlPointToCenter> Definition = new("Set Control Point To Particles' Center", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_SetControlPointToCenter>()
			.Int("Control Point Number to Set", "1", (o, v) => o.CP1 = v)
			.Vector3("Center Offset", "0 0 0", (o, v) => o.CP1Pos = v));

	public override uint GetWrittenAttributes() => 0;
	public override uint GetReadAttributes() => 0;
	public override bool ShouldRunBeforeEmitters() => true;
	public override void InitParams(ParticleSystemDefinition def, Element element) => CP1 = ParticleOpUtil.ClampCP(CP1);

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		particles.GetBounds(out Vector3 min, out Vector3 max);
		particles.SetControlPoint(CP1, CP1Pos + (min + max) / 2);
	}
}

public sealed class C_OP_VelocityMatchingForce : ParticleOperatorInstance
{
	struct MatchContext
	{
		public Vector3 AvgVelocity;
		public float AvgSpeed;
	}

	public float DirScale;
	public float SpdScale;
	public int CPBroadcast;

	public static readonly ParticleOperatorDefinition<C_OP_VelocityMatchingForce> Definition = new("Movement Match Particle Velocities", ParticleOperatorId.Generic, false,
		ParticleOperatorUnpack.Begin<C_OP_VelocityMatchingForce>()
			.Float("Direction Matching Strength", "0.25", (o, v) => o.DirScale = v)
			.Float("Speed Matching Strength", "0.25", (o, v) => o.SpdScale = v)
			.Int("Control Point to Broadcast Speed and Direction To", "-1", (o, v) => o.CPBroadcast = v));

	public override uint GetWrittenAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override uint GetReadAttributes() => PARTICLE_ATTRIBUTE_XYZ_MASK | PARTICLE_ATTRIBUTE_PREV_XYZ_MASK;
	public override int GetRequiredContextBytes() => System.Runtime.CompilerServices.Unsafe.SizeOf<MatchContext>();
	public override void InitializeContextData(ParticleCollection particles, Span<byte> context) => ParticleOpUtil.Context<MatchContext>(context) = default;

	public override void Operate(ParticleCollection particles, float opStrength, Span<byte> context) {
		ref MatchContext ctx = ref ParticleOpUtil.Context<MatchContext>(context);
		Vector3 velocityAvg = Vector3.Zero;
		float avgSpeed = 0;
		for (int p = 0; p < particles.ActiveParticles; p++) {
			Vector3 xyz = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_XYZ, p);
			Vector3 prev = particles.GetVectorAttributeValue(PARTICLE_ATTRIBUTE_PREV_XYZ, p);
			Vector3 velocity = (xyz - prev) / particles.Dt;
			velocityAvg += velocity;
			float speed = velocity.Length();
			avgSpeed += speed;
			if (ctx.AvgVelocity != Vector3.Zero) {
				Vector3 scaled = ParticleOpUtil.NormalizeOrZero(Vector3.Lerp(ParticleOpUtil.NormalizeOrZero(velocity), ctx.AvgVelocity, DirScale));
				speed = ParticleOpUtil.Lerp(SpdScale, speed, ctx.AvgSpeed);
				particles.SetVectorAttribute(PARTICLE_ATTRIBUTE_XYZ, p, scaled * speed * particles.Dt + prev);
			}
		}
		ctx.AvgVelocity = ParticleOpUtil.NormalizeOrZero(velocityAvg);
		ctx.AvgSpeed = avgSpeed / particles.ActiveParticles;
		if (CPBroadcast != -1) {
			particles.SetControlPoint(CPBroadcast, new Vector3(ctx.AvgSpeed));
			particles.SetControlPointForwardVector(CPBroadcast, ctx.AvgVelocity);
		}
	}
}
