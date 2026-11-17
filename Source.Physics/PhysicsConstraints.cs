using Box3D;

using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.InteropServices;


namespace Source.Physics;

internal static class ConstraintMath
{
	public static readonly Vector3 AxisX = new() { X = 1.0f, Y = 0.0f, Z = 0.0f };
	public static readonly Vector3 AxisY = new() { X = 0.0f, Y = 1.0f, Z = 0.0f };
	public static readonly Vector3 AxisZ = new() { X = 0.0f, Y = 0.0f, Z = 1.0f };
	public static readonly Quaternion IdentityQuat = Quaternion.Identity;
	public static readonly Transform IdentityTransform = new() { p = default, q = IdentityQuat };

	public static Vector3 SafeNormalize(Vector3 v) {
		Vector3 vec = BoxToSource.Unitless(v);
		float len = vec.Length();
		return len > 1e-9f ? SourceToBox.Unitless(vec / len) : AxisZ;
	}

	public static Vector3 WorldToLocalPoint(Body body, Vector3 worldPoint) {
		Transform wt = body.Transform;
		return BoxMath.InvRotateVector(wt.q, Sub(worldPoint, wt.p));
	}

	public static Quaternion BodyRotation(Body body) => body.Transform.q;

	public static Quaternion LocalFrameForAxis(Body body, Vector3 fromAxis, Vector3 worldAxis) {
		Quaternion qWorld = B3.ComputeQuatBetweenUnitVectors(fromAxis, SafeNormalize(worldAxis));
		return BoxMath.InvMulQuat(BodyRotation(body), qWorld);
	}

	public static float ClampAngle(float radians, float limit) => Math.Clamp(radians, -limit, limit);

	public static Vector3 Add(Vector3 a, Vector3 b) => SourceToBox.Unitless(BoxToSource.Unitless(a) + BoxToSource.Unitless(b));
	public static Vector3 Sub(Vector3 a, Vector3 b) => SourceToBox.Unitless(BoxToSource.Unitless(a) - BoxToSource.Unitless(b));
	public static Vector3 Mul(float s, Vector3 v) => SourceToBox.Unitless(BoxToSource.Unitless(v) * s);
	public static float Dot(Vector3 a, Vector3 b) => Vector3.Dot(BoxToSource.Unitless(a), BoxToSource.Unitless(b));
	public static Vector3 Cross(Vector3 a, Vector3 b) => SourceToBox.Unitless(Vector3.Cross(BoxToSource.Unitless(a), BoxToSource.Unitless(b)));
	public static float Length(Vector3 v) => BoxToSource.Unitless(v).Length();
}

internal class PhysicsConstraint : IPhysicsConstraint
{
	readonly PhysicsEnvironment Environment;
	PhysicsObject? Reference;
	PhysicsObject? Attached;
	PhysicsConstraintGroup? Group;
	object? GameData;
	Joint JointId;
	Func<Joint>? BuildFn;
	ConstraintBreakableParams BreakParams;
	bool Broken;
	GCHandle Handle;

	bool Pulley;
	Vector3 PulleyWorld0, PulleyWorld1;
	Vector3 PulleyLocal0, PulleyLocal1;
	float PulleyTotalLength;
	float PulleyGearRatio = 1.0f;
	bool PulleyRigid;

	bool AngularLimits;
	Quaternion AngFrameRef;
	Quaternion AngFrameAtt;
	bool AngCone;
	float AngConeAngle;
	bool AngTwist;
	float AngTwistMin;
	float AngTwistMax;
	float TwistUnwrapped;
	float TwistLastRaw;
	bool TwistInit;
	float AngFriction;
	float FrictionRefTwist;
	float FrictionRefSwing;
	bool FrictionInit;
	bool AngHasJoint;
	Vector3 AngAnchorRef;
	Vector3 AngAnchorAtt;

	public PhysicsConstraint(PhysicsEnvironment environment, PhysicsObject reference, PhysicsObject attached) {
		Environment = environment;
		Reference = reference;
		Attached = attached;
		Handle = GCHandle.Alloc(this, GCHandleType.Normal);
	}

	public static PhysicsConstraint? FromUserData(nint userData) {
		if (userData == 0)
			return null;
		return GCHandle.FromIntPtr(userData).Target as PhysicsConstraint;
	}

	public void Destroy() {
		Group?.RemoveConstraint(this);
		DestroyJoint();
		if (Handle.IsAllocated)
			Handle.Free();
	}

	public void Init(Func<Joint>? buildFn, bool active) {
		BuildFn = buildFn;
		if (active)
			Activate();
	}

	public void SetGroup(PhysicsConstraintGroup? group) => Group = group;
	public PhysicsConstraintGroup? GetGroup() => Group;
	public Joint GetJointId() => JointId;
	public void SetBreakParams(in ConstraintBreakableParams parms) => BreakParams = parms;
	public bool IsBroken() => Broken;
	public bool IsPulley() => Pulley;
	public bool IsAngularLimits() => AngularLimits;
	public void SetAngularFriction(float friction) => AngFriction = friction;

	void DestroyJoint() {
		if (JointId.IsValid)
			JointId.Destroy(true);
		JointId = default;
	}

	public void Activate() {
		if (Broken)
			return;
		if (!JointId.IsValid && BuildFn != null) {
			JointId = BuildFn();
			if (JointId.IsValid) {
				JointId.UserData = GCHandle.ToIntPtr(Handle);
				JointId.CollideConnected = true;
				ApplyConstraintTuning();
			}
		}
	}

	void ApplyConstraintTuning() {
		Environment.GetGravity(out Vector3 gravity);
		float gravityLength = SourceToBox.Distance(gravity.Length());
		if (gravityLength < 1e-3f)
			gravityLength = 9.80665f;

		if (BreakParams.ForceLimit > 0.0f)
			JointId.ForceThreshold = BreakParams.ForceLimit * gravityLength;
		if (BreakParams.TorqueLimit > 0.0f)
			JointId.TorqueThreshold = BreakParams.TorqueLimit * gravityLength * BoxUnits.InchesToMetres;

		if (BreakParams.Strength > 0.0f && BreakParams.Strength < 0.999f) {
			JointId.GetConstraintTuning(out float hertz, out float damping);
			JointId.SetConstraintTuning(hertz * BreakParams.Strength, damping);
		}
	}

	public void OnBroken() {
		DestroyJoint();
		Broken = true;
	}

	public void SetupPulley(Vector3 pulleyWorld0, Vector3 pulleyWorld1, Vector3 localAttach0, Vector3 localAttach1, float totalLength, float gearRatio, bool rigid) {
		Pulley = true;
		PulleyWorld0 = pulleyWorld0;
		PulleyWorld1 = pulleyWorld1;
		PulleyLocal0 = localAttach0;
		PulleyLocal1 = localAttach1;
		PulleyTotalLength = totalLength;
		PulleyGearRatio = gearRatio > 1e-4f ? gearRatio : 1.0f;
		PulleyRigid = rigid;
	}

	public void SolvePulley(float dt) {
		if (!Pulley || Reference == null || Attached == null || dt <= 0.0f)
			return;

		Body refBody = Reference.BodyId;
		Body attBody = Attached.BodyId;
		if (!refBody.IsAwake && !attBody.IsAwake)
			return;

		Transform xfRef = refBody.Transform;
		Transform xfAtt = attBody.Transform;
		Vector3 worldA = BoxMath.TransformWorldPoint(xfRef, PulleyLocal0);
		Vector3 worldB = BoxMath.TransformWorldPoint(xfAtt, PulleyLocal1);

		Vector3 dA = ConstraintMath.Sub(worldA, PulleyWorld0);
		Vector3 dB = ConstraintMath.Sub(worldB, PulleyWorld1);
		float lenA = ConstraintMath.Length(dA), lenB = ConstraintMath.Length(dB);
		if (lenA < 1e-6f || lenB < 1e-6f)
			return;
		Vector3 uA = ConstraintMath.Mul(1.0f / lenA, dA);
		Vector3 uB = ConstraintMath.Mul(1.0f / lenB, dB);

		float gear = PulleyGearRatio;
		float c = lenA + gear * lenB - PulleyTotalLength;
		if (!PulleyRigid && c < 0.0f)
			return;

		Vector3 comA = BoxMath.TransformWorldPoint(xfRef, refBody.MassData.center);
		Vector3 comB = BoxMath.TransformWorldPoint(xfAtt, attBody.MassData.center);
		Vector3 crossA = ConstraintMath.Cross(ConstraintMath.Sub(worldA, comA), uA);
		Vector3 crossB = ConstraintMath.Cross(ConstraintMath.Sub(worldB, comB), uB);
		Matrix3 invIA = refBody.WorldInverseRotationalInertia;
		Matrix3 invIB = attBody.WorldInverseRotationalInertia;
		float kA = refBody.InverseMass + ConstraintMath.Dot(crossA, BoxMath.MulMV(invIA, crossA));
		float kB = attBody.InverseMass + ConstraintMath.Dot(crossB, BoxMath.MulMV(invIB, crossB));
		float k = kA + gear * gear * kB;
		if (k <= 1e-9f)
			return;

		float clampC = SourceToBox.Distance(24.0f);
		float bias = (0.2f / dt) * Math.Clamp(c, -clampC, clampC);

		for (int i = 0; i < 4; i++) {
			Vector3 vA = refBody.GetWorldPointVelocity(worldA);
			Vector3 vB = attBody.GetWorldPointVelocity(worldB);
			float cdot = ConstraintMath.Dot(uA, vA) + gear * ConstraintMath.Dot(uB, vB);
			float impulse = -(cdot + bias) / k;
			if (!PulleyRigid && impulse > 0.0f)
				impulse = 0.0f;
			refBody.ApplyLinearImpulse(ConstraintMath.Mul(impulse, uA), worldA, true);
			attBody.ApplyLinearImpulse(ConstraintMath.Mul(gear * impulse, uB), worldB, true);
		}
	}

	public void SetupAngularLimits(in Transform frameRef, in Transform frameAtt, bool cone, float coneAngle, bool twist, float twistMin, float twistMax, float friction, bool hasJoint) {
		AngularLimits = true;
		AngFrameRef = frameRef.q;
		AngFrameAtt = frameAtt.q;
		AngAnchorRef = frameRef.p;
		AngAnchorAtt = frameAtt.p;
		AngHasJoint = hasJoint;
		AngCone = cone;
		AngConeAngle = coneAngle;
		AngTwist = twist;
		AngTwistMin = twistMin;
		AngTwistMax = twistMax;
		AngFriction = friction;
	}

	static Vector3 AnchorRelativeVelocity(Body refBody, Body attBody, Vector3 anchorRefLocal, Vector3 anchorAttLocal) {
		Vector3 worldRef = BoxMath.TransformWorldPoint(refBody.Transform, anchorRefLocal);
		Vector3 worldAtt = BoxMath.TransformWorldPoint(attBody.Transform, anchorAttLocal);
		return ConstraintMath.Sub(attBody.GetWorldPointVelocity(worldAtt), refBody.GetWorldPointVelocity(worldRef));
	}

	static void RepinAnchorVelocity(Body refBody, Body attBody, Vector3 anchorRefLocal, Vector3 anchorAttLocal, Vector3 vRelTarget) {
		Transform xfRef = refBody.Transform;
		Transform xfAtt = attBody.Transform;
		Vector3 worldRef = BoxMath.TransformWorldPoint(xfRef, anchorRefLocal);
		Vector3 worldAtt = BoxMath.TransformWorldPoint(xfAtt, anchorAttLocal);
		Matrix3 invIRef = refBody.WorldInverseRotationalInertia;
		Matrix3 invIAtt = attBody.WorldInverseRotationalInertia;
		Vector3 rRef = ConstraintMath.Sub(worldRef, refBody.WorldCenter);
		Vector3 rAtt = ConstraintMath.Sub(worldAtt, attBody.WorldCenter);
		float invMass = refBody.InverseMass + attBody.InverseMass;

		for (int i = 0; i < 4; i++) {
			Vector3 vRel = ConstraintMath.Sub(ConstraintMath.Sub(attBody.GetWorldPointVelocity(worldAtt), refBody.GetWorldPointVelocity(worldRef)), vRelTarget);
			float len = ConstraintMath.Length(vRel);
			if (len < 1e-4f)
				break;
			Vector3 dir = ConstraintMath.Mul(1.0f / len, vRel);
			Vector3 crossRef = ConstraintMath.Cross(rRef, dir);
			Vector3 crossAtt = ConstraintMath.Cross(rAtt, dir);
			float k = invMass + ConstraintMath.Dot(crossRef, BoxMath.MulMV(invIRef, crossRef)) + ConstraintMath.Dot(crossAtt, BoxMath.MulMV(invIAtt, crossAtt));
			if (k <= 1e-9f)
				break;
			Vector3 impulse = ConstraintMath.Mul(-len / k, dir);
			attBody.ApplyLinearImpulse(impulse, worldAtt, false);
			refBody.ApplyLinearImpulse(ConstraintMath.Mul(-1.0f, impulse), worldRef, false);
		}
	}

	static float SolveAngularFrictionImpulse(Body refBody, Body attBody, Vector3 axis, float alpha, float refPos, float friction, float dt, ref bool applied) {
		Matrix3 invIRef = refBody.WorldInverseRotationalInertia;
		Matrix3 invIAtt = attBody.WorldInverseRotationalInertia;
		float k = ConstraintMath.Dot(axis, BoxMath.MulMV(invIRef, axis)) + ConstraintMath.Dot(axis, BoxMath.MulMV(invIAtt, axis));
		if (k <= 1e-9f)
			return refPos;

		float vel = ConstraintMath.Dot(axis, ConstraintMath.Sub(attBody.AngularVelocity, refBody.AngularVelocity));
		float dAlpha = refPos - alpha;
		float correction = dAlpha * 0.8f / dt - vel;
		if (MathF.Abs(correction) < 0.03f)
			return refPos;
		float impulse = correction / k;

		float maxImpulse = friction * dt;
		if (MathF.Abs(impulse) > maxImpulse) {
			float factor = maxImpulse / MathF.Abs(impulse);
			impulse *= factor;
			refPos -= (1.0f - factor) * dAlpha;
		}

		if (MathF.Abs(impulse) < 1e-6f)
			return refPos;

		applied = true;
		attBody.ApplyAngularImpulse(ConstraintMath.Mul(impulse, axis), false);
		refBody.ApplyAngularImpulse(ConstraintMath.Mul(-impulse, axis), false);
		return refPos;
	}

	static void SolveAngularLimitImpulse(Body refBody, Body attBody, Vector3 axis, float alpha, float min, float max, float tau, float dt, ref bool applied) {
		Matrix3 invIRef = refBody.WorldInverseRotationalInertia;
		Matrix3 invIAtt = attBody.WorldInverseRotationalInertia;
		float k = ConstraintMath.Dot(axis, BoxMath.MulMV(invIRef, axis)) + ConstraintMath.Dot(axis, BoxMath.MulMV(invIAtt, axis));
		if (k <= 1e-9f)
			return;

		const float slop = 0.015f;
		float vel = ConstraintMath.Dot(axis, ConstraintMath.Sub(attBody.AngularVelocity, refBody.AngularVelocity));
		float next = alpha + vel * dt;
		float impulse = 0.0f;
		if (next > max + slop)
			impulse = -tau * (next - (max + slop)) / (dt * k);
		else if (next < min - slop)
			impulse = -tau * (next - (min - slop)) / (dt * k);
		if (MathF.Abs(impulse) > 1e-6f) {
			applied = true;
			attBody.ApplyAngularImpulse(ConstraintMath.Mul(impulse, axis), false);
			refBody.ApplyAngularImpulse(ConstraintMath.Mul(-impulse, axis), false);
		}
	}

	public bool SolveAngularLimits(float dt, bool applyFriction) {
		if (!AngularLimits || Broken || Reference == null || Attached == null || dt <= 0.0f)
			return false;

		Body refBody = Reference.BodyId;
		Body attBody = Attached.BodyId;
		if (!refBody.IsAwake && !attBody.IsAwake)
			return false;

		bool applied = false;

		Quaternion qRef = BoxMath.MulQuat(ConstraintMath.BodyRotation(refBody), AngFrameRef);
		Quaternion qAtt = BoxMath.MulQuat(ConstraintMath.BodyRotation(attBody), AngFrameAtt);
		Vector3 xRef = BoxMath.RotateVector(qRef, ConstraintMath.AxisX);
		Vector3 xAtt = BoxMath.RotateVector(qAtt, ConstraintMath.AxisX);

		bool friction = applyFriction && AngFriction > 0.0f;

		Vector3 anchorBefore = AngHasJoint ? AnchorRelativeVelocity(refBody, attBody, AngAnchorRef, AngAnchorAtt) : default;

		if (AngCone) {
			Vector3 crossRA = ConstraintMath.Cross(xRef, xAtt);
			float crossLen = ConstraintMath.Length(crossRA);
			if (crossLen > 1e-6f) {
				Vector3 axis = ConstraintMath.Mul(1.0f / crossLen, crossRA);
				float swing = MathF.Atan2(crossLen, ConstraintMath.Dot(xAtt, xRef));
				if (friction) {
					if (!FrictionInit)
						FrictionRefSwing = swing;
					FrictionRefSwing = SolveAngularFrictionImpulse(refBody, attBody, axis, swing, FrictionRefSwing, AngFriction, dt, ref applied);
				}
				SolveAngularLimitImpulse(refBody, attBody, axis, swing, -MathF.PI, AngConeAngle, 1.0f, dt, ref applied);
			}
		}

		Vector3 axisSum = ConstraintMath.Add(xRef, xAtt);
		float sumLenSq = ConstraintMath.Dot(axisSum, axisSum);
		if (AngTwist && sumLenSq > 1e-4f) {
			Quaternion qRel = BoxMath.InvMulQuat(qRef, qAtt);
			if (qRel.W < 0.0f)
				qRel = -qRel;
			float raw = 2.0f * MathF.Atan2(qRel.X, qRel.W);
			if (!TwistInit) {
				TwistInit = true;
				TwistUnwrapped = raw;
			}
			else {
				float delta = raw - TwistLastRaw;
				if (delta > MathF.PI)
					delta -= 2.0f * MathF.PI;
				else if (delta < -MathF.PI)
					delta += 2.0f * MathF.PI;
				TwistUnwrapped += delta;
			}
			TwistLastRaw = raw;

			float sumLen = MathF.Sqrt(sumLenSq);
			Vector3 twistAxis = ConstraintMath.Mul(1.0f / sumLen, axisSum);
			float twistTau = 0.5f * sumLen;
			if (friction) {
				if (!FrictionInit)
					FrictionRefTwist = TwistUnwrapped;
				FrictionRefTwist = SolveAngularFrictionImpulse(refBody, attBody, twistAxis, TwistUnwrapped, FrictionRefTwist, AngFriction, dt, ref applied);
			}
			SolveAngularLimitImpulse(refBody, attBody, twistAxis, TwistUnwrapped, AngTwistMin, AngTwistMax, twistTau, dt, ref applied);
		}

		if (friction)
			FrictionInit = true;

		if (applied && AngHasJoint)
			RepinAnchorVelocity(refBody, attBody, AngAnchorRef, AngAnchorAtt, anchorBefore);
		return applied;
	}

	public void Deactivate() => DestroyJoint();

	public void SetGameData(object? gameData) => GameData = gameData;
	public object? GetGameData() => GameData;

	public IPhysicsObject? GetReferenceObject() => Reference;
	public IPhysicsObject? GetAttachedObject() => Attached;

	public bool NotifyObjectDestroyed(PhysicsObject obj) {
		if (Reference != obj && Attached != obj)
			return false;
		bool fireBroken = !Broken;
		DestroyJoint();
		Broken = true;
		if (Reference == obj)
			Reference = null;
		if (Attached == obj)
			Attached = null;
		return fireBroken;
	}

	public void SetLinearMotor(float speed, float maxLinearImpulse) {
		if (!JointId.IsValid || JointId.Type != JointType.Prismatic)
			return;

		PrismaticJoint prismatic = (PrismaticJoint)JointId;
		prismatic.IsMotorEnabled = speed != 0.0f;
		prismatic.MotorSpeed = SourceToBox.Distance(speed);
		prismatic.MaxMotorForce = MathF.Abs(SourceToBox.Distance(maxLinearImpulse));
	}

	public void SetAngularMotor(float rotSpeed, float maxAngularImpulse) {
		if (AngularLimits) {
			if (rotSpeed == 0.0f)
				AngFriction = MathLib.DEG2RAD(MathF.Abs(maxAngularImpulse));
			return;
		}

		if (!JointId.IsValid)
			return;

		switch (JointId.Type) {
			case JointType.Revolute:
				RevoluteJoint revolute = (RevoluteJoint)JointId;
				revolute.IsMotorEnabled = maxAngularImpulse != 0.0f;
				revolute.MotorSpeed = MathLib.DEG2RAD(-rotSpeed);
				revolute.MaxMotorTorque = MathF.Abs(MathLib.DEG2RAD(maxAngularImpulse));
				break;
			case JointType.Spherical:
				if (rotSpeed == 0.0f) {
					SphericalJoint spherical = (SphericalJoint)JointId;
					spherical.IsMotorEnabled = maxAngularImpulse != 0.0f;
					spherical.MotorVelocity = default;
					spherical.MaxMotorTorque = MathF.Abs(MathLib.DEG2RAD(maxAngularImpulse));
				}
				break;
		}
	}

	public void UpdateRagdollTransforms(in Matrix3x4 constraintToReference, in Matrix3x4 constraintToAttached) { }

	public bool GetConstraintTransform(out Matrix3x4 constraintToReference, out Matrix3x4 constraintToAttached) {
		constraintToReference = default;
		constraintToAttached = default;
		Reference?.GetPositionMatrix(out constraintToReference);
		Attached?.GetPositionMatrix(out constraintToAttached);
		return true;
	}

	public bool GetConstraintParams(out ConstraintBreakableParams parms) {
		parms = BreakParams;
		return true;
	}

	public void OutputDebugInfo() { }
}

internal partial class PhysicsEnvironment
{
	readonly List<PhysicsConstraint> Constraints = [];
	readonly List<PhysicsConstraint> Pulleys = [];
	readonly List<PhysicsSpring> Springs = [];

	IPhysicsConstraint FinishConstraint(PhysicsConstraint constraint, IPhysicsConstraintGroup? group, in ConstraintBreakableParams breakParams, Func<Joint>? buildFn) {
		constraint.SetBreakParams(breakParams);
		Constraints.Add(constraint);
		if (group is PhysicsConstraintGroup boxGroup) {
			boxGroup.AddConstraint(constraint);
			constraint.SetGroup(boxGroup);
		}
		constraint.Init(buildFn, group == null && breakParams.IsActive);
		return constraint;
	}

	public IPhysicsConstraint CreateFixedConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintFixedParams fixedParams) {
		PhysicsObject refObj = (PhysicsObject)pReferenceObject;
		PhysicsObject attObj = (PhysicsObject)pAttachedObject;
		World world = WorldId;
		Body refBody = refObj.BodyId, attBody = attObj.BodyId;

		Transform relative = SourceToBox.Transform(fixedParams.AttachedRefXform);
		bool anchorAtAttached = refObj.IsStatic() || (!attObj.IsStatic() && attObj.GetMass() < refObj.GetMass());
		Transform frameA = ConstraintMath.IdentityTransform;
		Transform frameB = BoxMath.InvertTransform(relative);
		if (anchorAtAttached) {
			frameA = relative;
			frameB = ConstraintMath.IdentityTransform;
		}

		Joint Build() {
			WeldJointDef def = WeldJointDef.Default;
			def.@base.bodyIdA = refBody;
			def.@base.bodyIdB = attBody;
			def.@base.localFrameA = frameA;
			def.@base.localFrameB = frameB;
			return (Joint)WeldJoint.Create(world, def);
		}
		return FinishConstraint(new PhysicsConstraint(this, refObj, attObj), group, fixedParams.Constraint, Build);
	}

	public IPhysicsConstraint CreateHingeConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintHingeParams hinge) {
		PhysicsObject refObj = (PhysicsObject)pReferenceObject;
		PhysicsObject attObj = (PhysicsObject)pAttachedObject;
		World world = WorldId;
		Body refBody = refObj.BodyId, attBody = attObj.BodyId;

		Vector3 worldPos = SourceToBox.Distance(hinge.WorldPosition);
		Vector3 worldAxis = SourceToBox.Unitless(hinge.WorldAxisDirection);
		Transform frameA = new() { p = ConstraintMath.WorldToLocalPoint(refBody, worldPos), q = ConstraintMath.LocalFrameForAxis(refBody, ConstraintMath.AxisZ, worldAxis) };
		Transform frameB = new() { p = ConstraintMath.WorldToLocalPoint(attBody, worldPos), q = ConstraintMath.LocalFrameForAxis(attBody, ConstraintMath.AxisZ, worldAxis) };

		bool limit = hinge.HingeAxis.MinRotation != hinge.HingeAxis.MaxRotation;
		float lower = ConstraintMath.ClampAngle(MathLib.DEG2RAD(-hinge.HingeAxis.MaxRotation), 0.99f * MathF.PI);
		float upper = ConstraintMath.ClampAngle(MathLib.DEG2RAD(-hinge.HingeAxis.MinRotation), 0.99f * MathF.PI);

		bool motor = hinge.HingeAxis.AngularVelocity != 0.0f || hinge.HingeAxis.Torque != 0.0f;
		float motorSpeed = MathLib.DEG2RAD(-hinge.HingeAxis.AngularVelocity);
		float maxTorque = MathF.Abs(hinge.HingeAxis.Torque) * (BoxUnits.InchesToMetres * BoxUnits.InchesToMetres);

		Joint Build() {
			RevoluteJointDef def = RevoluteJointDef.Default;
			def.@base.bodyIdA = refBody;
			def.@base.bodyIdB = attBody;
			def.@base.localFrameA = frameA;
			def.@base.localFrameB = frameB;
			if (limit) {
				def.enableLimit = true;
				def.lowerAngle = lower;
				def.upperAngle = upper;
			}
			if (motor) {
				def.enableMotor = true;
				def.motorSpeed = motorSpeed;
				def.maxMotorTorque = maxTorque;
			}
			return (Joint)RevoluteJoint.Create(world, def);
		}
		return FinishConstraint(new PhysicsConstraint(this, refObj, attObj), group, hinge.Constraint, Build);
	}

	public IPhysicsConstraint CreateBallsocketConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintBallSocketParams ballsocket) {
		PhysicsObject refObj = (PhysicsObject)pReferenceObject;
		PhysicsObject attObj = (PhysicsObject)pAttachedObject;
		World world = WorldId;
		Body refBody = refObj.BodyId, attBody = attObj.BodyId;

		Vector3 posA = SourceToBox.Distance(ballsocket.ConstraintPosition[0]);
		Vector3 posB = SourceToBox.Distance(ballsocket.ConstraintPosition[1]);

		Joint Build() {
			SphericalJointDef def = SphericalJointDef.Default;
			def.@base.bodyIdA = refBody;
			def.@base.bodyIdB = attBody;
			def.@base.localFrameA.p = posA;
			def.@base.localFrameB.p = posB;
			return (Joint)SphericalJoint.Create(world, def);
		}
		return FinishConstraint(new PhysicsConstraint(this, refObj, attObj), group, ballsocket.Constraint, Build);
	}

	public IPhysicsConstraint CreateSlidingConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintSlidingParams sliding) {
		PhysicsObject refObj = (PhysicsObject)pReferenceObject;
		PhysicsObject attObj = (PhysicsObject)pAttachedObject;
		World world = WorldId;
		Body refBody = refObj.BodyId, attBody = attObj.BodyId;

		Transform attToRef = SourceToBox.Transform(sliding.AttachedRefXform);
		Vector3 slideAxis = ConstraintMath.SafeNormalize(SourceToBox.Unitless(sliding.SlideAxisRef));
		Transform frameA = new() { p = attToRef.p, q = B3.ComputeQuatBetweenUnitVectors(ConstraintMath.AxisX, slideAxis) };
		Transform frameB = BoxMath.InvMulTransforms(attToRef, frameA);

		bool limit = sliding.LimitMin != sliding.LimitMax;
		float lo = SourceToBox.Distance(sliding.LimitMin);
		float hi = SourceToBox.Distance(sliding.LimitMax);
		bool motor = sliding.Friction != 0.0f || sliding.Velocity != 0.0f;
		float motorSpeed = SourceToBox.Distance(sliding.Velocity);
		float maxForce = sliding.Friction;

		Joint Build() {
			PrismaticJointDef def = PrismaticJointDef.Default;
			def.@base.bodyIdA = refBody;
			def.@base.bodyIdB = attBody;
			def.@base.localFrameA = frameA;
			def.@base.localFrameB = frameB;
			if (limit) {
				def.enableLimit = true;
				def.lowerTranslation = lo;
				def.upperTranslation = hi;
			}
			if (motor) {
				def.enableMotor = true;
				def.motorSpeed = motorSpeed;
				def.maxMotorForce = maxForce;
			}
			return (Joint)PrismaticJoint.Create(world, def);
		}
		return FinishConstraint(new PhysicsConstraint(this, refObj, attObj), group, sliding.Constraint, Build);
	}

	public IPhysicsConstraint CreateLengthConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintLengthParams length) {
		PhysicsObject refObj = (PhysicsObject)pReferenceObject;
		PhysicsObject attObj = (PhysicsObject)pAttachedObject;
		World world = WorldId;
		Body refBody = refObj.BodyId, attBody = attObj.BodyId;

		Vector3 posA = SourceToBox.Distance(length.ObjectPosition[0]);
		Vector3 posB = SourceToBox.Distance(length.ObjectPosition[1]);
		float total = SourceToBox.Distance(length.TotalLength);
		float min = SourceToBox.Distance(length.MinLength);
		bool rigid = length.MinLength >= length.TotalLength;

		Joint Build() {
			DistanceJointDef def = DistanceJointDef.Default;
			def.@base.bodyIdA = refBody;
			def.@base.bodyIdB = attBody;
			def.@base.localFrameA.p = posA;
			def.@base.localFrameB.p = posB;
			def.length = total;
			if (rigid)
				def.enableSpring = false;
			else {
				def.enableSpring = true;
				def.hertz = 0.0f;
				def.dampingRatio = 0.0f;
				def.enableLimit = true;
				def.minLength = min;
				def.maxLength = total;
			}
			return (Joint)DistanceJoint.Create(world, def);
		}
		return FinishConstraint(new PhysicsConstraint(this, refObj, attObj), group, length.Constraint, Build);
	}

	public IPhysicsConstraint CreateRagdollConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintRagdollParams ragdoll) {
		PhysicsObject refObj = (PhysicsObject)pReferenceObject;
		PhysicsObject attObj = (PhysicsObject)pAttachedObject;
		World world = WorldId;
		Body refBody = refObj.BodyId, attBody = attObj.BodyId;

		Transform frameRef = SourceToBox.Transform(ragdoll.ConstraintToReference);
		Transform frameAtt = SourceToBox.Transform(ragdoll.ConstraintToAttached);

		float[] mins = new float[3], maxs = new float[3];
		bool[] free = new bool[3];
		int dof = 0, dofAxis = 0;
		for (int i = 0; i < 3; i++) {
			if (ragdoll.UseClockwiseRotations) {
				mins[i] = MathLib.DEG2RAD(-ragdoll.Axes[i].MaxRotation);
				maxs[i] = MathLib.DEG2RAD(-ragdoll.Axes[i].MinRotation);
			}
			else {
				mins[i] = MathLib.DEG2RAD(ragdoll.Axes[i].MinRotation);
				maxs[i] = MathLib.DEG2RAD(ragdoll.Axes[i].MaxRotation);
			}
			free[i] = (ragdoll.Axes[i].MaxRotation - ragdoll.Axes[i].MinRotation) >= 359.0f;
			if (ragdoll.Axes[i].MinRotation != ragdoll.Axes[i].MaxRotation) {
				dof++;
				dofAxis = i;
			}
		}

		float swing1 = free[1] ? MathF.PI : 0.5f * (maxs[1] - mins[1]);
		float swing2 = free[2] ? MathF.PI : 0.5f * (maxs[2] - mins[2]);
		float cone = Math.Clamp(MathF.Max(swing1, swing2), 0.0f, MathF.PI);
		float rawTorque = MathF.Max(ragdoll.Axes[0].Torque, MathF.Max(ragdoll.Axes[1].Torque, ragdoll.Axes[2].Torque));
		float friction = rawTorque * refObj.GetMass();

		ConstraintBreakableParams breakParams = ragdoll.Constraint;
		breakParams.IsActive = ragdoll.IsActive;

		bool hasCone = cone < MathF.PI - 1e-3f;

		if (ragdoll.OnlyAngularLimits) {
			bool twistFree = free[0];
			bool twistRigid = !free[0] && MathF.Abs(ragdoll.Axes[0].MaxRotation - ragdoll.Axes[0].MinRotation) <= 2.0f;
			if (cone <= MathLib.DEG2RAD(2.0f) && twistFree) {
				Quaternion qZtoX = B3.ComputeQuatBetweenUnitVectors(ConstraintMath.AxisZ, ConstraintMath.AxisX);
				Joint BuildBearing() {
					ParallelJointDef def = ParallelJointDef.Default;
					def.@base.bodyIdA = refBody;
					def.@base.bodyIdB = attBody;
					def.@base.localFrameA.p = frameRef.p;
					def.@base.localFrameA.q = BoxMath.MulQuat(frameRef.q, qZtoX);
					def.@base.localFrameB.p = frameAtt.p;
					def.@base.localFrameB.q = BoxMath.MulQuat(frameAtt.q, qZtoX);
					def.hertz = 120.0f;
					def.dampingRatio = 2.0f;
					return (Joint)ParallelJoint.Create(world, def);
				}
				return FinishConstraint(new PhysicsConstraint(this, refObj, attObj), group, breakParams, BuildBearing);
			}
			if (cone <= MathLib.DEG2RAD(2.0f) && twistRigid) {
				Joint BuildLock() {
					MotorJointDef def = MotorJointDef.Default;
					def.@base.bodyIdA = refBody;
					def.@base.bodyIdB = attBody;
					def.@base.localFrameA = frameRef;
					def.@base.localFrameB = frameAtt;
					def.angularHertz = 120.0f;
					def.angularDampingRatio = 2.0f;
					def.maxSpringTorque = float.MaxValue;
					return (Joint)MotorJoint.Create(world, def);
				}
				return FinishConstraint(new PhysicsConstraint(this, refObj, attObj), group, breakParams, BuildLock);
			}

			PhysicsConstraint angular = new(this, refObj, attObj);
			angular.SetupAngularLimits(frameRef, frameAtt, hasCone, cone, !free[0], mins[0], maxs[0], friction, false);
			Pulleys.Add(angular);
			return FinishConstraint(angular, group, breakParams, null);
		}

		float angleLimit = 0.99f * MathF.PI;

		Joint Build() {
			if (dof == 0) {
				WeldJointDef def = WeldJointDef.Default;
				def.@base.bodyIdA = refBody;
				def.@base.bodyIdB = attBody;
				def.@base.localFrameA = frameRef;
				def.@base.localFrameB = frameAtt;
				return (Joint)WeldJoint.Create(world, def);
			}
			if (dof == 1) {
				Vector3 axis = dofAxis switch { 0 => ConstraintMath.AxisX, 1 => ConstraintMath.AxisY, _ => ConstraintMath.AxisZ };
				Quaternion qRemap = B3.ComputeQuatBetweenUnitVectors(ConstraintMath.AxisZ, axis);
				RevoluteJointDef def = RevoluteJointDef.Default;
				def.@base.bodyIdA = refBody;
				def.@base.bodyIdB = attBody;
				def.@base.localFrameA.p = frameRef.p;
				def.@base.localFrameA.q = BoxMath.MulQuat(frameRef.q, qRemap);
				def.@base.localFrameB.p = frameAtt.p;
				def.@base.localFrameB.q = BoxMath.MulQuat(frameAtt.q, qRemap);
				if (!free[dofAxis]) {
					def.enableLimit = true;
					def.lowerAngle = ConstraintMath.ClampAngle(mins[dofAxis], angleLimit);
					def.upperAngle = ConstraintMath.ClampAngle(maxs[dofAxis], angleLimit);
				}
				def.enableMotor = true;
				def.maxMotorTorque = friction;
				return (Joint)RevoluteJoint.Create(world, def);
			}

			Quaternion qZtoX = B3.ComputeQuatBetweenUnitVectors(ConstraintMath.AxisZ, ConstraintMath.AxisX);
			SphericalJointDef sdef = SphericalJointDef.Default;
			sdef.@base.bodyIdA = refBody;
			sdef.@base.bodyIdB = attBody;
			sdef.@base.localFrameA.p = frameRef.p;
			sdef.@base.localFrameA.q = BoxMath.MulQuat(frameRef.q, qZtoX);
			sdef.@base.localFrameB.p = frameAtt.p;
			sdef.@base.localFrameB.q = BoxMath.MulQuat(frameAtt.q, qZtoX);
			if (hasCone) {
				sdef.enableConeLimit = true;
				sdef.coneAngle = MathF.Min(cone, 0.99f * 0.5f * MathF.PI);
			}
			if (!free[0]) {
				sdef.enableTwistLimit = true;
				sdef.lowerTwistAngle = ConstraintMath.ClampAngle(mins[0], angleLimit);
				sdef.upperTwistAngle = ConstraintMath.ClampAngle(maxs[0], angleLimit);
			}
			sdef.enableMotor = true;
			sdef.maxMotorTorque = friction;
			return (Joint)SphericalJoint.Create(world, sdef);
		}
		return FinishConstraint(new PhysicsConstraint(this, refObj, attObj), group, breakParams, Build);
	}

	public IPhysicsConstraint CreatePulleyConstraint(IPhysicsObject pReferenceObject, IPhysicsObject pAttachedObject, IPhysicsConstraintGroup group, in ConstraintPulleyParams pulley) {
		PhysicsObject refObj = (PhysicsObject)pReferenceObject;
		PhysicsObject attObj = (PhysicsObject)pAttachedObject;
		PhysicsConstraint constraint = new(this, refObj, attObj);

		constraint.SetupPulley(
			SourceToBox.Distance(pulley.PulleyPosition[0]), SourceToBox.Distance(pulley.PulleyPosition[1]),
			SourceToBox.Distance(pulley.ObjectPosition[0]), SourceToBox.Distance(pulley.ObjectPosition[1]),
			SourceToBox.Distance(pulley.TotalLength), pulley.GearRatio, pulley.IsRigid);
		Pulleys.Add(constraint);

		return FinishConstraint(constraint, group, pulley.Constraint, null);
	}

	public void DestroyConstraint(IPhysicsConstraint constraint) {
		if (constraint is not PhysicsConstraint boxConstraint)
			return;
		Constraints.Remove(boxConstraint);
		Pulleys.Remove(boxConstraint);
		boxConstraint.Destroy();
	}

	void SolvePulleys(float dt) {
		for (int i = 0; i < Pulleys.Count; i++)
			Pulleys[i].SolvePulley(dt);
	}

	public IPhysicsConstraintGroup CreateConstraintGroup(in ConstraintGroupParams groupParams) {
		PhysicsConstraintGroup group = new();
		group.SetErrorParams(groupParams);
		return group;
	}

	public void DestroyConstraintGroup(IPhysicsConstraintGroup group) { }

	public IPhysicsSpring CreateSpring(IPhysicsObject objStart, IPhysicsObject objEnd, ref SpringParams springParams) {
		if (objStart is not PhysicsObject start || objEnd is not PhysicsObject end)
			return null!;
		PhysicsSpring spring = new(start, end, springParams);
		Springs.Add(spring);
		return spring;
	}

	public void DestroySpring(IPhysicsSpring spring) {
		if (spring is PhysicsSpring boxSpring)
			Springs.Remove(boxSpring);
	}

	void DrainJointEvents() {
		JointEvents events = WorldId.JointEvents;
		if (events.count <= 0)
			return;

		List<PhysicsConstraint> broken = [];
		for (int i = 0; i < events.count; i++) {
			PhysicsConstraint? constraint = PhysicsConstraint.FromUserData(events.jointEvents[i].userData);
			if (constraint != null)
				broken.Add(constraint);
		}

		foreach (PhysicsConstraint constraint in broken) {
			if (!Constraints.Contains(constraint))
				continue;
			constraint.OnBroken();
			if (ConstraintEvent != null && ConstraintNotify)
				ConstraintEvent.ConstraintBroken(constraint);
		}
	}
}

internal class PhysicsConstraintGroup : IPhysicsConstraintGroup
{
	readonly List<PhysicsConstraint> Constraints = [];
	ConstraintGroupParams Params;

	public void Activate() {
		foreach (PhysicsConstraint constraint in Constraints)
			constraint.Activate();
	}

	public bool IsInErrorState() => false;
	public void ClearErrorState() { }
	public void GetErrorParams(out ConstraintGroupParams parms) => parms = Params;
	public void SetErrorParams(in ConstraintGroupParams parms) => Params = parms;
	public void SolvePenetration(IPhysicsObject obj0, IPhysicsObject obj1) { }

	public void AddConstraint(PhysicsConstraint constraint) => Constraints.Add(constraint);
	public void RemoveConstraint(PhysicsConstraint constraint) => Constraints.Remove(constraint);
}

internal class PhysicsSpring : IPhysicsSpring
{
	PhysicsObject? Start;
	PhysicsObject? End;
	readonly Vector3 AnchorStart;
	readonly Vector3 AnchorEnd;
	float NaturalLength;
	float Constant;
	float Damping;
	readonly float RelativeDamping;
	readonly bool OnlyStretch;

	public PhysicsSpring(PhysicsObject start, PhysicsObject end, in SpringParams parms) {
		Start = start;
		End = end;
		NaturalLength = SourceToBox.Distance(parms.NaturalLength);
		Constant = parms.Constant;
		Damping = parms.Damping;
		RelativeDamping = parms.RelativeDamping;
		OnlyStretch = parms.OnlyStretch;

		if (parms.UseLocalPositions) {
			AnchorStart = SourceToBox.Distance(parms.StartPosition);
			AnchorEnd = SourceToBox.Distance(parms.EndPosition);
		}
		else {
			AnchorStart = ConstraintMath.WorldToLocalPoint(start.BodyId, SourceToBox.Distance(parms.StartPosition));
			AnchorEnd = ConstraintMath.WorldToLocalPoint(end.BodyId, SourceToBox.Distance(parms.EndPosition));
		}
	}

	public void Simulate(float dt) {
		if (Start == null || End == null || dt <= 0.0f)
			return;

		Body start = Start.BodyId;
		Body end = End.BodyId;
		if (!start.IsAwake && !end.IsAwake)
			return;

		Transform xfStart = start.Transform;
		Transform xfEnd = end.Transform;
		Vector3 posStart = ConstraintMath.Add(xfStart.p, BoxMath.RotateVector(xfStart.q, AnchorStart));
		Vector3 posEnd = ConstraintMath.Add(xfEnd.p, BoxMath.RotateVector(xfEnd.q, AnchorEnd));

		Vector3 dir = ConstraintMath.Sub(posStart, posEnd);
		float len = ConstraintMath.Length(dir);
		if (len < 1e-6f)
			return;
		dir = ConstraintMath.Mul(1.0f / len, dir);

		if (OnlyStretch && len <= NaturalLength)
			return;

		Vector3 vRel = ConstraintMath.Sub(end.GetWorldPointVelocity(posEnd), start.GetWorldPointVelocity(posStart));
		float dampSpeed = ConstraintMath.Dot(dir, vRel);
		float force = (len - NaturalLength) * Constant - Damping * dampSpeed;

		Vector3 impulse = ConstraintMath.Mul(force * dt, dir);
		impulse = ConstraintMath.Add(impulse, ConstraintMath.Mul(-dt * RelativeDamping, vRel));
		end.ApplyLinearImpulse(impulse, posEnd, false);
		start.ApplyLinearImpulse(ConstraintMath.Mul(-1.0f, impulse), posStart, false);
	}

	public void GetEndpoints(out Vector3 worldPositionStart, out Vector3 worldPositionEnd) {
		worldPositionStart = default;
		worldPositionEnd = default;
		if (Start != null) {
			Transform wt = Start.BodyId.Transform;
			worldPositionStart = BoxToSource.Distance(ConstraintMath.Add(wt.p, BoxMath.RotateVector(wt.q, AnchorStart)));
		}
		if (End != null) {
			Transform wt = End.BodyId.Transform;
			worldPositionEnd = BoxToSource.Distance(ConstraintMath.Add(wt.p, BoxMath.RotateVector(wt.q, AnchorEnd)));
		}
	}

	void WakeEnds() {
		Start?.Wake();
		End?.Wake();
	}

	public void SetSpringConstant(float springContant) {
		Constant = springContant;
		WakeEnds();
	}

	public void SetSpringDamping(float springDamping) {
		Damping = springDamping;
		WakeEnds();
	}

	public void SetSpringLength(float springLenght) {
		NaturalLength = SourceToBox.Distance(springLenght);
		WakeEnds();
	}

	public IPhysicsObject GetStartObject() => Start!;
	public IPhysicsObject GetEndObject() => End!;

	public void NotifyObjectDestroyed(PhysicsObject obj) {
		if (Start == obj)
			Start = null;
		if (End == obj)
			End = null;
	}
}
