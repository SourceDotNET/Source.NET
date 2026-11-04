global using static Game.Server.PhysControllerGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;
using Source.Common.Physics;

#if GMOD_DLL
using Game.Server.GarrysMod;
#endif

using System.Numerics;

namespace Game.Server;

public static class PhysControllerGlobals
{
	public const int SF_THRUST_STARTACTIVE = 1;
	public const int SF_THRUST_FORCE = 2;
	public const int SF_THRUST_TORQUE = 4;
	public const int SF_THRUST_LOCAL_ORIENTATION = 8;
	public const int SF_THRUST_MASS_INDEPENDENT = 16;
	public const int SF_THRUST_IGNORE_POS = 32;

	public const int SF_MOTOR_START_ON = 1;
	public const int SF_MOTOR_NOCOLLIDE = 2;
	public const int SF_MOTOR_HINGE = 4;
	public const int SF_MOTOR_LOCAL = 8;

	public const int SF_KEEPUPRIGHT_START_INACTIVE = 1;

	public static void CalculateVelocityOffsetLocal(IPhysicsObject phys, in Vector3 forceLocal, in Vector3 positionLocal, out Vector3 outVelLocal, out Vector3 outAngular) {
		phys.LocalToWorld(out Vector3 posWorld, positionLocal);
		phys.LocalToWorldVector(out Vector3 forceWorld, forceLocal);
		phys.CalculateVelocityOffset(forceWorld, posWorld, out Vector3 velWorld, out outAngular);
		phys.WorldToLocalVector(out outVelLocal, velWorld);
	}

	public static BaseEntity? CreateKeepUpright(in Vector3 vecOrigin, in QAngle vecAngles, BaseEntity? owner, float angularLimit, bool active) {
		KeepUpright? keepUpright = (KeepUpright?)BaseEntity.Create("phys_keepupright", vecOrigin, vecAngles, owner);
		if (keepUpright != null) {
			keepUpright.AttachedObject.Set(owner);
			keepUpright.AngularLimit = angularLimit;
			if (!active)
				keepUpright.AddSpawnFlags(SF_KEEPUPRIGHT_START_INACTIVE);
			keepUpright.Spawn();
			keepUpright.Activate();
		}

		return keepUpright;
	}

	public static Vector3 ComputeRotSpeedToAlignAxes(in Vector3 testAxis, in Vector3 alignAxis, in Vector3 currentSpeed, float damping, float scale, float maxSpeed) {
		Vector3 rotationAxis = Vector3.Cross(testAxis, alignAxis);

		float cosine = Vector3.Dot(testAxis, alignAxis);
		float sine = MathLib.VectorNormalize(ref rotationAxis);
		float angle = MathF.Atan2(sine, cosine);

		angle = MathLib.RAD2DEG(angle);
		Vector3 angular = rotationAxis * scale * angle;
		angular -= rotationAxis * damping * Vector3.Dot(currentSpeed, rotationAxis);

		float len = MathLib.VectorNormalize(ref angular);

		if (len > maxSpeed)
			len = maxSpeed;

		return angular * len;
	}
}

public class ConstantForceController : IMotionEvent
{
	public static readonly DataMap DataDesc = new(typeof(ConstantForceController), null, [
		DEFINE<ConstantForceController>.FIELD(nameof(ControlType), FieldType.Integer),
		DEFINE<ConstantForceController>.FIELD(nameof(Linear), FieldType.Vector),
		DEFINE<ConstantForceController>.FIELD(nameof(Angular), FieldType.Vector),
		DEFINE<ConstantForceController>.FIELD(nameof(LinearSave), FieldType.Vector),
		DEFINE<ConstantForceController>.FIELD(nameof(AngularSave), FieldType.Vector),
	]);

	public SimResult ControlType;
	public Vector3 Linear;
	public Vector3 Angular;
	public Vector3 LinearSave;
	public Vector3 AngularSave;

	public void Init(SimResult controlType) {
		ControlType = controlType;
	}

	public void SetConstantForce(in Vector3 linear, in Vector3 angular) {
		Linear = linear;
		Angular = angular;
		LinearSave = linear;
		AngularSave = angular;
	}

	public void ScaleConstantForce(float scale) {
		Linear = LinearSave * scale;
		Angular = AngularSave * scale;
	}

	public SimResult Simulate(IPhysicsMotionController? controller, IPhysicsObject? obj, TimeUnit_t deltaTime, out Vector3 linear, out Vector3 angular) {
#if GMOD_DLL
		for (int i = 0; i < 3; i++) {
			if (!float.IsFinite(Linear[i]))
				Linear[i] = 10000.0f;
		}
		for (int i = 0; i < 3; i++) {
			if (!float.IsFinite(Angular[i]))
				Angular[i] = 10000.0f;
		}
#endif
		linear = Linear;
		angular = Angular;

		return ControlType;
	}
}

public abstract class PhysForce : PointEntity
{
	public static readonly new DataMap DataDesc = new(typeof(PhysForce), BaseEntity.DataDesc, [
		DEFINE<PhysForce>.KEYFIELD(nameof(NameAttach), FieldType.String, "attach1"),
		DEFINE<PhysForce>.KEYFIELD(nameof(Force), FieldType.Float, "force"),
		DEFINE<PhysForce>.KEYFIELD(nameof(ForceTime), FieldType.Float, "forcetime"),

		DEFINE<PhysForce>.FIELD(nameof(AttachedObject), FieldType.EHandle),
		DEFINE<PhysForce>.EMBEDDED(nameof(Integrator)),

		DEFINE<PhysForce>.INPUTFUNC(FieldType.Void, "Activate", nameof(InputActivate), (INPUTFUNCPTR)((self, data) => ((PhysForce)self).InputActivate(data))),
		DEFINE<PhysForce>.INPUTFUNC(FieldType.Void, "Deactivate", nameof(InputDeactivate), (INPUTFUNCPTR)((self, data) => ((PhysForce)self).InputDeactivate(data))),
		DEFINE<PhysForce>.INPUTFUNC(FieldType.Float, "scale", nameof(InputForceScale), (INPUTFUNCPTR)((self, data) => ((PhysForce)self).InputForceScale(data))),

		DEFINE<PhysForce>.THINKFUNC(nameof(ForceOff)),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	protected IPhysicsMotionController? Controller;

	protected string? NameAttach;
	protected float Force;
	protected float ForceTime;
	protected EHANDLE AttachedObject = new();
	protected bool WasRestored;
#if GMOD_DLL
	protected IPhysicsObject? PhysObject;
#endif

	protected readonly ConstantForceController Integrator = new();

	public PhysForce() {
		Controller = null;
		WasRestored = false;
	}

	public override void Term() {
		if (Controller != null)
			physenv.DestroyMotionController(Controller);
		base.Term();
	}

	public override void Spawn() {
		if ((SpawnFlags & SF_THRUST_LOCAL_ORIENTATION) != 0)
			Integrator.Init(SimResult.LocalAcceleration);
		else
			Integrator.Init(SimResult.GlobalAcceleration);
	}

	public override void OnRestore() {
		base.OnRestore();

		Controller?.SetEventHandler(Integrator);
		WasRestored = true;
	}

	public override void Activate() {
		base.Activate();

		Controller?.WakeObjects();
		if (WasRestored)
			return;

#if GMOD_DLL
		if (PhysObject == null && AttachedObject.Get() == null)
#else
		if (AttachedObject.Get() == null)
#endif
			AttachedObject.Set(gEntList.FindEntityByName(null, NameAttach));

		OnActivate();

		if ((SpawnFlags & SF_THRUST_STARTACTIVE) != 0)
			ForceOn();
	}

#if GMOD_DLL
	public override void SetPhysObject(int index, IPhysicsObject? obj) {
		if (index == 0)
			PhysObject = obj;
		else
			obj = PhysObject;
		if (obj != null) {
			BaseEntity? entity = (BaseEntity?)obj.GetGameData();
			AttachedObject.Set(entity);
		}
	}
#endif

	void InputActivate(in InputData inputdata) {
		ForceOn();
	}

	void InputDeactivate(in InputData inputdata) {
		ForceOff();
	}

	void InputForceScale(in InputData inputdata) {
		ScaleForce(inputdata.Value.Float());
	}

	public void ForceOn() {
		if (Controller != null)
			return;

		ActivateForce();
		if (ForceTime != 0) {
			SetNextThink(gpGlobals.CurTime + ForceTime);
			SetThink(ForceOff);
		}
	}

	public void ActivateForce() {
#if GMOD_DLL
		if (Controller != null)
			return;

		if (PhysObject != null && !LuaPhysObj.IsPhysicsObjectValid(PhysObject))
			PhysObject = null;

		BaseEntity? attached = AttachedObject.Get();
		if (attached == null)
			return;
		IPhysicsObject? phys = PhysObject ?? attached.VPhysicsGetObject();
		if (phys == null || !LuaPhysObj.IsPhysicsObjectValid(phys))
			return;
#else
		IPhysicsObject? phys = null;
		BaseEntity? attached = AttachedObject.Get();
		if (attached != null)
			phys = attached.VPhysicsGetObject();

		if (phys == null)
			return;
#endif

		SetupForces(phys, out Vector3 linear, out Vector3 angular);

		Integrator.SetConstantForce(linear, angular);
		Controller = physenv.CreateMotionController(Integrator);
		Controller.AttachObject(phys, true);
		phys.Wake();
	}

	public void ForceOff() {
		if (Controller == null)
			return;

		physenv.DestroyMotionController(Controller);
		Controller = null;
		SetThink(null);
		SetNextThink(TICK_NEVER_THINK);
		BaseEntity? attached = AttachedObject.Get();
		if (attached != null) {
#if GMOD_DLL
			if (PhysObject != null && !LuaPhysObj.IsPhysicsObjectValid(PhysObject))
				PhysObject = null;
			IPhysicsObject? phys = PhysObject ?? attached.VPhysicsGetObject();
			if (phys != null && LuaPhysObj.IsPhysicsObjectValid(phys))
				phys.Wake();
#else
			IPhysicsObject? phys = attached.VPhysicsGetObject();
			phys?.Wake();
#endif
		}
	}

	public void ScaleForce(float scale) {
#if GMOD_DLL
		if (Controller == null) {
			ForceOn();
			if (Controller == null)
				return;
		}

		if (scale == 0.0f)
			scale = 0.001f;
#else
		if (Controller == null)
			ForceOn();
#endif

		Integrator.ScaleConstantForce(scale);
		Controller!.WakeObjects();
	}

	public abstract void SetupForces(IPhysicsObject phys, out Vector3 linear, out Vector3 angular);

	public virtual void OnActivate() { }
}

[LinkEntityToClass("phys_thruster")]
public class PhysThruster : PhysForce
{
	public static readonly new DataMap DataDesc = new(typeof(PhysThruster), PhysForce.DataDesc, [
		DEFINE<PhysThruster>.FIELD(nameof(LocalOrigin), FieldType.Vector),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	Vector3 LocalOrigin;

	public override void OnActivate() {
		BaseEntity? attached = AttachedObject.Get();
#if GMOD_DLL
		if (attached == null && PhysObject == null)
			return;
		{
#else
		if (attached != null) {
#endif
			MathLib.MatrixInvert(attached!.EntityToWorldTransform(), out Matrix3x4 worldToAttached);
			MathLib.ConcatTransforms(worldToAttached, EntityToWorldTransform(), out Matrix3x4 thrusterToAttached);
			MathLib.MatrixGetColumn(thrusterToAttached, 3, out LocalOrigin);

			if (HasSpawnFlags(SF_THRUST_LOCAL_ORIENTATION)) {
				MathLib.MatrixAngles(thrusterToAttached, out QAngle angles);
				SetLocalAngles(angles);
			}
			if (HasSpawnFlags(SF_THRUST_IGNORE_POS))
				LocalOrigin.Init();
		}
	}

	public override void SetupForces(IPhysicsObject phys, out Vector3 linear, out Vector3 angular) {
		MathLib.AngleVectors(GetLocalAngles(), out Vector3 thrustVector);
		thrustVector *= Force;

		if ((SpawnFlags & SF_THRUST_MASS_INDEPENDENT) != 0)
			thrustVector *= phys.GetMass();
		if ((SpawnFlags & SF_THRUST_LOCAL_ORIENTATION) != 0)
			CalculateVelocityOffsetLocal(phys, thrustVector, LocalOrigin, out linear, out angular);
		else {
			MathLib.VectorTransform(LocalOrigin, AttachedObject.Get()!.EntityToWorldTransform(), out Vector3 position);
			phys.CalculateVelocityOffset(thrustVector, position, out linear, out angular);
		}

		if ((SpawnFlags & SF_THRUST_FORCE) == 0)
			linear.Init();

		if ((SpawnFlags & SF_THRUST_TORQUE) == 0)
			angular.Init();
	}
}

[LinkEntityToClass("phys_torque")]
public class PhysTorque : PhysForce
{
	public static readonly new DataMap DataDesc = new(typeof(PhysTorque), PhysForce.DataDesc, [
		DEFINE<PhysTorque>.KEYFIELD(nameof(Axis), FieldType.Vector, "axis"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	Vector3 Axis;

	public override void Spawn() {
		SpawnFlags |= SF_THRUST_TORQUE | SF_THRUST_MASS_INDEPENDENT;
		SpawnFlags &= ~SF_THRUST_FORCE;

		Axis -= GetAbsOrigin();
		MathLib.VectorNormalize(ref Axis);
#if !GMOD_DLL
		Util.SnapDirectionToAxis(ref Axis);
#endif
		base.Spawn();
	}

	public override void SetupForces(IPhysicsObject phys, out Vector3 linear, out Vector3 angular) {
		linear = default;
		linear.Init();

#if GMOD_DLL
		if (Force == 0.0f || Force > 500000.0f || Force < -500000.0f) {
			angular = default;
			return;
		}
#endif

		phys.GetPositionMatrix(out Matrix3x4 matrix);

		MathLib.VectorIRotate(Axis, matrix, out Vector3 axis_ls);

		angular = axis_ls * Force;
	}
}

public class MotorController : IMotionEvent
{
	public static readonly DataMap DataDesc = new(typeof(MotorController), null, [
		DEFINE<MotorController>.FIELD(nameof(Speed), FieldType.Float),
		DEFINE<MotorController>.FIELD(nameof(MaxTorque), FieldType.Float),
		DEFINE<MotorController>.KEYFIELD(nameof(Axis), FieldType.Vector, "axis"),
		DEFINE<MotorController>.KEYFIELD(nameof(InertiaFactor), FieldType.Float, "inertiafactor"),
		DEFINE<MotorController>.FIELD(nameof(LastSpeed), FieldType.Float),
		DEFINE<MotorController>.FIELD(nameof(LastAcceleration), FieldType.Float),
		DEFINE<MotorController>.FIELD(nameof(LastForce), FieldType.Float),
		DEFINE<MotorController>.FIELD(nameof(RestistanceDamping), FieldType.Float),
	]);

	public float Speed;
	public float MaxTorque;
	public Vector3 Axis;
	public float InertiaFactor;

	public float LastSpeed;
	public float LastAcceleration;
	public float LastForce;
	public float RestistanceDamping;

	public SimResult Simulate(IPhysicsMotionController? controller, IPhysicsObject? obj, TimeUnit_t deltaTime, out Vector3 linear, out Vector3 angular) {
		linear = vec3_origin;
		angular = vec3_origin;

		if (Speed == 0)
			return SimResult.Nothing;

		obj!.GetPositionMatrix(out Matrix3x4 matrix);

		obj.GetVelocity(out _, out Vector3 currentRotAxis);
		MathLib.VectorIRotate(Axis, matrix, out Vector3 motorAxis_ls);
		float currentSpeed = Vector3.Dot(currentRotAxis, motorAxis_ls);

		float inertia = MathLib.DotProductAbs(obj.GetInertia(), motorAxis_ls);

		float accel = Speed - currentSpeed;
		float rotForce = accel * inertia * InertiaFactor;

		if (MathF.Abs(LastAcceleration) > 0) {
			float deltaSpeed = currentSpeed - LastSpeed;
			if (deltaSpeed * accel > 0) {
				float factor = deltaSpeed / LastAcceleration;
				factor = 1 - Math.Clamp(factor, 0.0f, 1.0f);
				rotForce += LastForce * factor * RestistanceDamping;
			}
			else {
				if (currentSpeed != 0) {
					float increase = deltaSpeed / LastAcceleration;
					if (MathF.Abs(increase) < 0.05)
						rotForce += LastForce * RestistanceDamping;
				}
			}
		}

		if (MaxTorque != 0) {
			if (rotForce > MaxTorque)
				rotForce = MaxTorque;
			else if (rotForce < -MaxTorque)
				rotForce = -MaxTorque;
		}

		LastForce = rotForce;
		LastAcceleration = (rotForce / inertia);
		LastSpeed = currentSpeed;

		angular = motorAxis_ls * rotForce;

		return SimResult.LocalForce;
	}
}

[LinkEntityToClass("phys_motor")]
public class PhysMotor : LogicalEntity
{
	public static readonly new DataMap DataDesc = new(typeof(PhysMotor), BaseEntity.DataDesc, [
		DEFINE<PhysMotor>.KEYFIELD(nameof(NameAttach), FieldType.String, "attach1"),
		DEFINE<PhysMotor>.FIELD(nameof(AttachedObject), FieldType.EHandle),
		DEFINE<PhysMotor>.KEYFIELD(nameof(SpinUp), FieldType.Float, "spinup"),
		DEFINE<PhysMotor>.KEYFIELD(nameof(AdditionalAcceleration), FieldType.Float, "addangaccel"),
		DEFINE<PhysMotor>.FIELD(nameof(AngularAcceleration), FieldType.Float),
		DEFINE<PhysMotor>.FIELD(nameof(LastTime), FieldType.Time),

		DEFINE<PhysMotor>.INPUTFUNC(FieldType.Float, "SetSpeed", nameof(InputSetTargetSpeed), (INPUTFUNCPTR)((self, data) => ((PhysMotor)self).InputSetTargetSpeed(data))),
		DEFINE<PhysMotor>.INPUTFUNC(FieldType.Void, "TurnOn", nameof(InputTurnOn), (INPUTFUNCPTR)((self, data) => ((PhysMotor)self).InputTurnOn(data))),
		DEFINE<PhysMotor>.INPUTFUNC(FieldType.Void, "TurnOff", nameof(InputTurnOff), (INPUTFUNCPTR)((self, data) => ((PhysMotor)self).InputTurnOff(data))),

		DEFINE<PhysMotor>.EMBEDDED(nameof(Motor)),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	string? NameAttach;
	EHANDLE AttachedObject = new();
	float SpinUp;
	float AdditionalAcceleration;
	float AngularAcceleration;
	float LastTime;
#if GMOD_DLL
	IPhysicsObject? PhysObject;
#endif

	IPhysicsConstraint? Hinge;
	IPhysicsMotionController? Controller;
	readonly MotorController Motor = new();

	void CalculateAcceleration() {
		if (SpinUp != 0)
			AngularAcceleration = MathF.Abs(Speed / SpinUp);
		else
			AngularAcceleration = MathF.Abs(Speed);
	}

	void InputSetTargetSpeed(in InputData inputdata) {
		if (Speed == inputdata.Value.Float())
			return;

		Speed = inputdata.Value.Float();
		TargetSpeedChanged();
		CalculateAcceleration();
	}

	void TargetSpeedChanged() {
		SetNextThink(gpGlobals.CurTime);
		LastTime = (float)gpGlobals.CurTime;
		Controller!.WakeObjects();
	}

	void InputTurnOn(in InputData inputdata) {
		TurnOn();
	}

	void InputTurnOff(in InputData inputdata) {
		Motor.Speed = 0;
		SetNextThink(TICK_NEVER_THINK);
	}

	public override void Term() {
		BaseEntity? attached = AttachedObject.Get();
		if (attached != null && Hinge != null) {
			IPhysicsObject? phys = attached.VPhysicsGetObject();
			if (phys != null)
				PhysClearGameFlags(phys, PhysicsFlags.NoPlayerPickup);
		}

		physenv.DestroyConstraint(Hinge!);
		physenv.DestroyMotionController(Controller!);
		base.Term();
	}

	public override void Spawn() {
		Motor.Axis -= GetLocalOrigin();
		float axisLength = MathLib.VectorNormalize(ref Motor.Axis);
		if (axisLength > 1.0f)
			Util.SnapDirectionToAxis(ref Motor.Axis);
		else {
			Warning($"phys_motor {GetDebugName()} does not have a valid axis helper, and self-destructed!\n");

			Motor.Speed = 0;
			SetNextThink(TICK_NEVER_THINK);

			Util.Remove(this);
		}
	}

#if GMOD_DLL
	public override void SetPhysObject(int index, IPhysicsObject? obj) {
		if (index == 0)
			PhysObject = obj;
		else
			obj = PhysObject;
		if (obj != null) {
			BaseEntity? entity = (BaseEntity?)obj.GetGameData();
			AttachedObject.Set(entity);
		}
	}
#endif

	void TurnOn() {
		BaseEntity? attached = AttachedObject.Get();
		if (attached == null)
			return;

		IPhysicsObject? phys = attached.VPhysicsGetObject();
		if (phys != null) {
#if GMOD_DLL
			Controller?.WakeObjects();
#else
			Controller!.WakeObjects();
#endif
			LastTime = (float)(gpGlobals.CurTime - TICK_INTERVAL);
			Think();
		}
	}

	public override void Activate() {
		base.Activate();

		if (PhysObject == null && AttachedObject.Get() == null) {
			BaseEntity? attach = gEntList.FindEntityByName(null, NameAttach);
			if (attach != null && attach.GetMoveType() == Source.MoveType.VPhysics) {
				AttachedObject.Set(attach);
				IPhysicsObject phys = attach.VPhysicsGetObject()!;
				CalculateAcceleration();
				phys.GetPositionMatrix(out Matrix3x4 matrix);
				MathLib.VectorIRotate(Motor.Axis, matrix, out Vector3 motorAxis_ls);
				float inertia = MathLib.DotProductAbs(phys.GetInertia(), motorAxis_ls);
				Motor.MaxTorque = inertia * Motor.InertiaFactor * (AngularAcceleration + AdditionalAcceleration);
				Motor.RestistanceDamping = 1.0f;
			}
		}

#if GMOD_DLL
		if (PhysObject != null && !LuaPhysObj.IsPhysicsObjectValid(PhysObject))
			PhysObject = null;

		IPhysicsObject? attachedPhys = PhysObject;
		BaseEntity? attachedEntity = AttachedObject.Get();
		if (attachedEntity != null) {
			attachedPhys ??= attachedEntity.VPhysicsGetObject();
			if (attachedPhys == null) {
				Warning("Phys Motor Error: No physics object found. Deleting.\n");
				Util.Remove(this);
				return;
			}
		}
		else if (attachedPhys == null)
			return;
		{
			IPhysicsObject? pPhys = attachedPhys;
#else
		BaseEntity? attachedEntity = AttachedObject.Get();
		if (attachedEntity != null) {
			IPhysicsObject? pPhys = attachedEntity.VPhysicsGetObject();
#endif

			if ((SpawnFlags & SF_MOTOR_HINGE) != 0) {
				if (Hinge == null) {
					ConstraintHingeParams hingeParams = default;
					hingeParams.Defaults();
					hingeParams.WorldAxisDirection = Motor.Axis;
					hingeParams.WorldPosition = GetLocalOrigin();

					Hinge = physenv.CreateHingeConstraint(g_PhysWorldObject!, pPhys!, null!, hingeParams);
					Hinge.SetGameData(this);
					PhysSetGameFlags(pPhys!, PhysicsFlags.NoPlayerPickup);
				}

				if ((SpawnFlags & SF_MOTOR_NOCOLLIDE) != 0)
					PhysDisableEntityCollisions(g_PhysWorldObject, pPhys);
			}
			else
				Hinge = null;

			if (Controller == null) {
				Controller = physenv.CreateMotionController(Motor);
#if GMOD_DLL
				Controller.AttachObject(pPhys!, false);
#else
				Controller.AttachObject(attachedEntity.VPhysicsGetObject()!, false);
#endif

				if ((SpawnFlags & SF_MOTOR_START_ON) != 0)
					TurnOn();
			}
		}
	}

	public override void OnRestore() {
		base.OnRestore();
		Controller?.SetEventHandler(Motor);
	}

	public override void Think() {
		Assert(AngularAcceleration >= 0);

		Motor.Speed = MathLib.Approach(Speed, Motor.Speed, AngularAcceleration * (float)(gpGlobals.CurTime - LastTime));
		LastTime = (float)gpGlobals.CurTime;
		if (Motor.Speed != Speed)
			SetNextThink(gpGlobals.CurTime);
	}
}

[LinkEntityToClass("phys_keepupright")]
public class KeepUpright : PointEntity, IMotionEvent
{
	public static readonly new DataMap DataDesc = new(typeof(KeepUpright), BaseEntity.DataDesc, [
		DEFINE<KeepUpright>.FIELD(nameof(WorldGoalAxis), FieldType.Vector),
		DEFINE<KeepUpright>.FIELD(nameof(LocalTestAxis), FieldType.Vector),
		DEFINE<KeepUpright>.KEYFIELD(nameof(NameAttach), FieldType.String, "attach1"),
		DEFINE<KeepUpright>.FIELD(nameof(AttachedObject), FieldType.EHandle),
		DEFINE<KeepUpright>.KEYFIELD(nameof(AngularLimit), FieldType.Float, "angularlimit"),
		DEFINE<KeepUpright>.FIELD(nameof(Active), FieldType.Boolean),
		DEFINE<KeepUpright>.FIELD(nameof(DampAllRotation), FieldType.Boolean),

		DEFINE<KeepUpright>.INPUTFUNC(FieldType.Void, "TurnOn", nameof(InputTurnOn), (INPUTFUNCPTR)((self, data) => ((KeepUpright)self).InputTurnOn(data))),
		DEFINE<KeepUpright>.INPUTFUNC(FieldType.Void, "TurnOff", nameof(InputTurnOff), (INPUTFUNCPTR)((self, data) => ((KeepUpright)self).InputTurnOff(data))),
		DEFINE<KeepUpright>.INPUTFUNC(FieldType.Float, "SetAngularLimit", nameof(InputSetAngularLimit), (INPUTFUNCPTR)((self, data) => ((KeepUpright)self).InputSetAngularLimit(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	Vector3 WorldGoalAxis;
	Vector3 LocalTestAxis;
	IPhysicsMotionController? Controller;
	string? NameAttach;
	internal EHANDLE AttachedObject = new();
	internal float AngularLimit;
	bool Active;
	bool DampAllRotation;
#if GMOD_DLL
	IPhysicsObject? PhysObject;
#endif

	public KeepUpright() {
		AngularLimit = 15;
		AttachedObject.Set(null);
		DampAllRotation = false;
	}

	public override void Term() {
		if (Controller != null) {
			physenv.DestroyMotionController(Controller);
			Controller = null;
		}
		base.Term();
	}

	void InputTurnOn(in InputData inputdata) {
		Active = true;
	}

	void InputTurnOff(in InputData inputdata) {
		Active = false;
	}

	void InputSetAngularLimit(in InputData inputdata) {
		AngularLimit = inputdata.Value.Float();
	}

#if GMOD_DLL
	public override void SetPhysObject(int index, IPhysicsObject? obj) {
		if (index == 0)
			PhysObject = obj;
		else
			obj = PhysObject;
		if (obj != null) {
			BaseEntity? entity = (BaseEntity?)obj.GetGameData();
			AttachedObject.Set(entity);
		}
	}
#endif

	public override void Spawn() {
		LocalTestAxis.Init(0, 0, 1);
		GetVectors(out _, out _, out WorldGoalAxis);

		SetMoveType(Source.MoveType.None);

		if ((SpawnFlags & SF_KEEPUPRIGHT_START_INACTIVE) != 0)
			Active = false;
		else
			Active = true;
	}

	public override void Activate() {
		base.Activate();

#if GMOD_DLL
		if (PhysObject != null && !LuaPhysObj.IsPhysicsObjectValid(PhysObject))
			PhysObject = null;
#endif

		if (Controller == null) {
			IPhysicsObject? phys;
			BaseEntity? attached = AttachedObject.Get();
#if GMOD_DLL
			if (attached != null)
				phys = PhysObject ?? attached.VPhysicsGetObject();
			else {
				IPhysicsObject? found = PhysicsHook.FindPhysicsObjectByName(NameAttach, this);
				phys = PhysObject ?? found;
				if (phys != null && Controller != null) {
					physenv.DestroyMotionController(Controller);
					Controller = null;
				}
			}
#else
			if (attached != null)
				phys = attached.VPhysicsGetObject();
			else
				phys = PhysicsHook.FindPhysicsObjectByName(NameAttach, this);
#endif

			if (phys == null) {
				Util.Remove(this);
				return;
			}
#if HL2_DLL && !GMOD_DLL
			if (AngularLimit == 150.0f) {
				BaseEntity? entity = (BaseEntity?)phys.GetGameData();
				if (entity != null && entity.GetServerVehicle() != null && !stristr(gpGlobals.MapName, "d2_coast_01").IsEmpty)
					DampAllRotation = true;
			}
#endif

			Controller = physenv.CreateMotionController(this);
			Controller.AttachObject(phys, false);
		}
		else
			Controller.SetEventHandler(this);
	}

	public SimResult Simulate(IPhysicsMotionController? controller, IPhysicsObject? obj, TimeUnit_t deltaTime, out Vector3 linear, out Vector3 angular) {
		linear = default;
		angular = default;
		if (!Active)
			return SimResult.Nothing;

		linear.Init();

		obj!.GetVelocity(out _, out Vector3 angVel);

		obj.GetPositionMatrix(out Matrix3x4 matrix);

		MathLib.VectorIRotate(WorldGoalAxis, matrix, out Vector3 currentLocalTargetAxis);

		float invDeltaTime = (float)(1 / deltaTime);

		if (DampAllRotation) {
			angular = ComputeRotSpeedToAlignAxes(LocalTestAxis, currentLocalTargetAxis, angVel, 0, invDeltaTime, AngularLimit);
			angular -= angVel;
			angular *= invDeltaTime;
			return SimResult.LocalAcceleration;
		}

		angular = ComputeRotSpeedToAlignAxes(LocalTestAxis, currentLocalTargetAxis, angVel, 1.0f, invDeltaTime, AngularLimit);
		angular *= invDeltaTime;

		return SimResult.LocalAcceleration;
	}
}
