using Game.Server;

using Source.Common;

using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Server;

// Reasons behind a pickup
public enum PhysGunPickup
{
	PickedUpByCannon,
	PuntedByCannon,
	PickedUpByPlayer, // Picked up by +USE, not physgun.
}

// Reasons behind a drop
public enum PhysGunDrop
{
	DroppedByPlayer,
	ThrownByPlayer,
	DroppedByCannon,
	LaunchedByCannon,
}

public enum PhysGunForce
{
	Dropped,  // Dropped by +USE
	Thrown,   // Thrown from +USE
	Punted,   // Punted by cannon
	Launched, // Launched by cannon
}

public static class Pickup
{
	public static void PlayerPickupObject(BasePlayer player, BaseEntity obj) => HL2.PlayerPickupController.PlayerPickupObject(player, obj);

	// player pickup utility routine
	public static void ForcePlayerToDropThisObject(BaseEntity? target) {
		if (target == null)
			return;

		IPhysicsObject? physics = target.VPhysicsGetObject();

		if (physics == null)
			return;

		if ((physics.GetGameFlags() & PhysicsFlags.PlayerHeld) != 0) {
			for (int i = 1; i <= gpGlobals.MaxClients; i++) {
				BasePlayer? player = Util.PlayerByIndex(i);
				if (player == null)
					return;
				player.ForceDropOfCarriedPhysObjects(target);
			}
		}
	}

	public static void OnPhysGunDrop(BaseEntity? droppedObject, BasePlayer player, PhysGunDrop reason) {
		if (droppedObject is IPlayerPickupVPhysics pickup)
			pickup.OnPhysGunDrop(player, reason);
	}

	public static void OnPhysGunPickup(BaseEntity? pickedUpObject, BasePlayer player, PhysGunPickup reason = PhysGunPickup.PickedUpByCannon) {
		if (pickedUpObject is IPlayerPickupVPhysics pickup)
			pickup.OnPhysGunPickup(player, reason);

		// send phys gun pickup item event, but only in single player
		if (!g_pGameRules.IsMultiplayer()) {
			IGameEvent? ev = gameeventmanager.CreateEvent("physgun_pickup");
			if (ev != null) {
				ev.SetInt("entindex", pickedUpObject!.EntIndex());
				gameeventmanager.FireEvent(ev);
			}
		}
	}

	public static bool OnAttemptPhysGunPickup(BaseEntity? pickedUpObject, BasePlayer player, PhysGunPickup reason = PhysGunPickup.PickedUpByCannon) {
		if (pickedUpObject is IPlayerPickupVPhysics pickup)
			return pickup.OnAttemptPhysGunPickup(player, reason);
		return true;
	}

	public static BaseEntity? OnFailedPhysGunPickup(BaseEntity? pickedUpObject, Vector3 physgunPos) {
		if (pickedUpObject is IPlayerPickupVPhysics pickup)
			return pickup.OnFailedPhysGunPickup(physgunPos);

		return null;
	}

	public static bool GetPreferredCarryAngles(BaseEntity? obj, BasePlayer player, in Matrix3x4 localToWorld, ref QAngle outputAnglesWorldSpace) {
		if (obj is IPlayerPickupVPhysics pickup) {
			if (pickup.HasPreferredCarryAnglesForPlayer(player)) {
				outputAnglesWorldSpace = MathLib.TransformAnglesToWorldSpace(pickup.PreferredCarryAngles(), localToWorld);
				return true;
			}
		}
		return false;
	}

	public static bool ForcePhysGunOpen(BaseEntity? obj, BasePlayer player) {
		if (obj is IPlayerPickupVPhysics pickup)
			return pickup.ForcePhysgunOpen(player);
		return false;
	}

	public static Vector3 PhysGunLaunchAngularImpulse(BaseEntity? obj, PhysGunForce reason) {
		if (obj is IPlayerPickupVPhysics pickup && pickup.ShouldPuntUseLaunchForces(reason))
			return pickup.PhysGunLaunchAngularImpulse();
		return RandomAngularImpulse(-600, 600);
	}

	public static Vector3 DefaultPhysGunLaunchVelocity(in Vector3 forward, float mass) => throw new NotImplementedException();

	public static Vector3 PhysGunLaunchVelocity(BaseEntity? obj, in Vector3 forward, PhysGunForce reason) {
		// The object must be valid
		if (obj == null) {
			Assert(false);
			return vec3_origin;
		}

		// Shouldn't ever get here with a non-vphysics object.
		IPhysicsObject? physicsObject = obj.VPhysicsGetObject();
		if (physicsObject == null) {
			Assert(false);
			return vec3_origin;
		}

		// Call the pickup entity's callback
		if (obj is IPlayerPickupVPhysics pickup && pickup.ShouldPuntUseLaunchForces(reason))
			return pickup.PhysGunLaunchVelocity(forward, physicsObject.GetMass());

		// Do our default behavior
		return DefaultPhysGunLaunchVelocity(forward, physicsObject.GetMass());
	}

	public static bool ShouldPuntUseLaunchForces(BaseEntity? obj, PhysGunForce reason) {
		if (obj is IPlayerPickupVPhysics pickup)
			return pickup.ShouldPuntUseLaunchForces(reason);
		return false;
	}
}

public interface IPlayerPickupVPhysics
{
	// Callbacks for the physgun/cannon picking up an entity
	public bool OnAttemptPhysGunPickup(BasePlayer physGunUser, PhysGunPickup reason = PhysGunPickup.PickedUpByCannon);
	public BaseEntity? OnFailedPhysGunPickup(Vector3 physgunPos);
	public void OnPhysGunPickup(BasePlayer physGunUser, PhysGunPickup reason = PhysGunPickup.PickedUpByCannon);
	public void OnPhysGunDrop(BasePlayer physGunUser, PhysGunDrop reason);
	public bool HasPreferredCarryAnglesForPlayer(BasePlayer? player = null);
	public QAngle PreferredCarryAngles();
	public bool ForcePhysgunOpen(BasePlayer player);
	public Vector3 PhysGunLaunchAngularImpulse();
	public bool ShouldPuntUseLaunchForces(PhysGunForce reason);
	public Vector3 PhysGunLaunchVelocity(in Vector3 forward, float flMass);
}

public class DefaultPlayerPickupVPhysics : IPlayerPickupVPhysics
{
	public bool OnAttemptPhysGunPickup(BasePlayer? _, PhysGunPickup reason = PhysGunPickup.PickedUpByCannon) => true;
	public BaseEntity? OnFailedPhysGunPickup(Vector3 _) => null;
	public void OnPhysGunPickup(BasePlayer player, PhysGunPickup reason = PhysGunPickup.PickedUpByCannon) { }
	public void OnPhysGunDrop(BasePlayer player, PhysGunDrop _) { }
	public bool HasPreferredCarryAnglesForPlayer(BasePlayer? player) => false;
	public QAngle PreferredCarryAngles() => vec3_angle;
	public bool ForcePhysgunOpen(BasePlayer player) => false;
	public Vector3 PhysGunLaunchAngularImpulse() => RandomAngularImpulse(-600, 600);
	public bool ShouldPuntUseLaunchForces(PhysGunForce _) => false;
	public Vector3 PhysGunLaunchVelocity(in Vector3 forward, float mass) {
		return Pickup.DefaultPhysGunLaunchVelocity(forward, mass);
	}
}
