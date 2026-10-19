#if GMOD_DLL
using Game.Server.HL2;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Server.GarrysMod;

public static class PhysGunGlobals
{
	public static bool IsPhysicsObjectValid(IPhysicsObject? obj) {
		if (obj == null)
			return false;

		ReadOnlySpan<IPhysicsObject> objects = physenv.GetObjectList();
		for (int i = 0; i < objects.Length; i++)
			if (objects[i] == obj)
				return true;

		return false;
	}

	public static BaseEntity? GetPlayerHeldEntity(BasePlayer? player) {
		if (player == null)
			return null;

		return (player.UseEntity.Get() as PlayerPickupController)?.GetGrabController().GetAttached();
	}

	public static Vector3 GetAimForward(BasePlayer player) {
		if (player.WorldClicking && !player.DisableWorldClicking)
			return player.WorldClickVector;

		if (player.IsInAVehicle()) {
			player.CacheVehicleView();
			MathLib.AngleVectors(player.VehicleViewAngles, out Vector3 vehicleForward);
			return vehicleForward;
		}

		MathLib.AngleVectors(player.EyeAngles(), out Vector3 forward);
		return forward;
	}

	public static BaseEntity? PhysCannonGetHeldEntity(BaseCombatWeapon? weapon) => (weapon as Game.Shared.GarrysMod.WeaponPhysCannon)?.AttachedObject.Get();

	public static BaseEntity? PhysGunGetHeldEntity(BaseCombatWeapon? weapon) {
		if (weapon is not WeaponPhysGun physgun)
			return null;

		return physgun.GetGravCallback().GetAttachedEntity();
	}

	public static void PhysGunForceDrop(BaseCombatWeapon? weapon, BaseEntity? ent) {
		if (weapon is not WeaponPhysGun physgun)
			return;

		if (ent == null || ent == physgun.GrabbedEntity.Get())
			physgun.DropEntity();
	}

	public static bool GMOD_IsHeldByAnyPlayer(BaseEntity? ent, BasePlayer? exclude) {
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player == null || player == exclude)
				continue;

			if (GetPlayerHeldEntity(player) == ent)
				return true;
			if (PhysCannonGetHeldEntity(player.GetActiveWeapon()) == ent)
				return true;
			if (PhysGunGetHeldEntity(player.GetActiveWeapon()) == ent)
				return true;
		}

		return false;
	}
}

public class PhysGunControllerPoint : IMotionEvent
{
	public const float PhysGunMass = 45678;

	public static readonly ConVar physgun_maxAngular = new("physgun_maxAngular", "5000");
	public static readonly ConVar physgun_maxAngularDamping = new("physgun_maxAngularDamping", "10000");
	public static readonly ConVar physgun_maxSpeed = new("physgun_maxSpeed", "5000");
	public static readonly ConVar physgun_maxSpeedDamping = new("physgun_maxSpeedDamping", "10000");
	public static readonly ConVar physgun_DampingFactor = new("physgun_DampingFactor", "0.8");
	public static readonly ConVar physgun_teleportDistance = new("physgun_teleportDistance", "0");
	public static readonly ConVar physgun_timeToArrive = new("physgun_timeToArrive", "0.05");
	public static readonly ConVar physgun_timeToArriveRagdoll = new("physgun_timeToArriveRagdoll", "0.1");

	IPhysicsObject? attachedObject;
	Vector3 localPosition;
	Vector3 targetPosition;
	QAngle targetRotation;
	EHANDLE attachedEntity = new();
	float savedMass = -1;
	float timeToArrive;
	float savedBuoyancy = -1;
	bool isVehicle;
	Handle<BasePlayer> attachedPlayer = new();
	IPhysicsMotionController? controller;

	public BaseEntity? GetAttachedEntity() => attachedEntity.Get();

	void ValidatePhysObj() {
		if (!PhysGunGlobals.IsPhysicsObjectValid(attachedObject))
			attachedObject = null;
	}

	public void SetTargetRotation(in QAngle angles) {
		if (attachedObject != null)
			targetRotation = angles;
	}

	public void SetTargetPosition(in Vector3 position, BasePlayer? player) {
		ValidatePhysObj();
		if (!attachedEntity.IsValid() || attachedObject == null)
			return;

		attachedObject.LocalToWorld(out Vector3 world, localPosition);
		attachedObject.GetPosition(out Vector3 objPosition, out _);
		targetPosition = position - (world - objPosition);

		if (player != null && player.IsPlayer())
			((GMOD_Player)player).SnapToGrid(ref targetPosition);
	}

	public void AttachEntity(BasePlayer? player, BaseEntity ent, IPhysicsObject phys, in Vector3 grabPosition) {
		if (controller != null)
			Warning("CPhysGunControllerPoint already has controller?\n");

		attachedObject = phys;
		attachedEntity.Set(ent);
		isVehicle = ent.ClassMatches("prop_vehicle_jeep");
		timeToArrive = ent.IsRagdollProp() ? physgun_timeToArriveRagdoll.GetFloat() : physgun_timeToArrive.GetFloat();
		attachedPlayer.Set(player);

		float mass = phys.GetMass();
		savedBuoyancy = -1;
		savedMass = mass;
		if (mass > PhysGunMass)
			savedMass = -1;
		else if (ent.IsRagdollProp() || ent.ClassMatches("prop_vehicle_jeep") || ent.ClassMatches("prop_vehicle_apc") || ent.ClassMatches("prop_vehicle_jeep_old"))
			savedMass = -1;
		else if (savedMass != PhysGunMass) {
			savedBuoyancy = phys.GetBuoyancyRatio();
			phys.SetMass(PhysGunMass);
		}

		phys.EnableMotion(true);
		phys.WorldToLocal(out localPosition, grabPosition);

		controller = physenv.CreateMotionController(this);
		controller.AttachObject(phys, true);
		PhysSetGameFlags(phys, PhysicsFlags.PlayerHeld);
		controller.SetPriority(IPhysicsMotionController.Priority.HighPriority);

		phys.GetPosition(out _, out targetRotation);
		SetTargetPosition(grabPosition, player);
	}

	public void DetachEntity(bool restore) {
		ValidatePhysObj();

		BaseEntity? ent = attachedEntity.Get();
		if (restore && ent != null && attachedObject != null && ent.VPhysicsGetObject() != null) {
			if (savedMass > 0 && savedMass != PhysGunMass) {
				attachedObject.SetMass(savedMass);
				attachedObject.SetBuoyancyRatio(savedBuoyancy);
			}
		}

		if (!PhysGunGlobals.GMOD_IsHeldByAnyPlayer(ent, attachedPlayer.Get()) && attachedObject != null)
			PhysClearGameFlags(attachedObject, PhysicsFlags.PlayerHeld);

		if (controller != null)
			physenv.DestroyMotionController(controller);

		attachedEntity.Set(null);
		controller = null;
		targetPosition = default;
		attachedObject = null;
		attachedPlayer.Set(null);
	}

	public SimResult Simulate(IPhysicsMotionController? controller, IPhysicsObject? obj, TimeUnit_t deltaTime, out Vector3 linear, out Vector3 angular) {
		linear = default;
		angular = default;
		if (obj == null)
			return SimResult.Nothing;

		obj.EnableMotion(true);
		obj.Wake();

		HLShadowControlParams shadow = default;
		shadow.TargetPosition = targetPosition;
		shadow.TargetRotation = targetRotation;
		shadow.MaxAngular = physgun_maxAngular.GetFloat();
		shadow.MaxDampAngular = physgun_maxAngularDamping.GetFloat();
		shadow.MaxSpeed = physgun_maxSpeed.GetFloat();
		shadow.MaxDampSpeed = physgun_maxSpeedDamping.GetFloat();
		shadow.DampFactor = physgun_DampingFactor.GetFloat();
		shadow.TeleportDistance = physgun_teleportDistance.GetFloat();

		if (isVehicle) {
			obj.GetPosition(out _, out QAngle objAngles);
			MathLib.AngleVectors(targetRotation, out Vector3 f1);
			MathLib.AngleVectors(objAngles, out Vector3 f2);
			float sum = MathF.Abs(f1.X * f2.X) + MathF.Abs(f1.Y * f2.Y) + MathF.Abs(f1.Z * f2.Z);
			shadow.MaxAngular *= 1 - sum;
		}

		obj.ComputeShadowControl(shadow, timeToArrive, deltaTime);
		return SimResult.LocalAcceleration;
	}
}
#endif
