using Game.Server;

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
	public static void PlayerPickupObject(BasePlayer player, BaseEntity? obj) => throw new NotImplementedException();
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

	public static void OnPhysGunDrop(BaseEntity? obj, BasePlayer player, PhysGunDrop reason) => throw new NotImplementedException();
	public static void OnPhysGunPickup(BaseEntity? obj, BasePlayer player, PhysGunPickup reason = PhysGunPickup.PickedUpByCannon) => throw new NotImplementedException();
	public static bool OnAttemptPhysGunPickup(BaseEntity? obj, BasePlayer player, PhysGunPickup reason = PhysGunPickup.PickedUpByCannon) => throw new NotImplementedException();
	public static bool GetPreferredCarryAngles(BaseEntity? obj, BasePlayer player, ref Matrix3x4 localToWorld, out QAngle outputAnglesWorldSpace) => throw new NotImplementedException();
	public static bool ForcePhysGunOpen(BaseEntity? obj, BasePlayer player) => throw new NotImplementedException();
	public static bool ShouldPuntUseLaunchForces(BaseEntity? obj, PhysGunForce reason) => throw new NotImplementedException();
	public static Vector3 PhysGunLaunchAngularImpulse(BaseEntity? obj, PhysGunForce reason) => throw new NotImplementedException();
	public static Vector3 DefaultPhysGunLaunchVelocity(in Vector3 forward, float mass) => throw new NotImplementedException();
	public static Vector3 PhysGunLaunchVelocity(BaseEntity? obj, in Vector3 forward, PhysGunForce reason) => throw new NotImplementedException();

	public static BaseEntity? OnFailedPhysGunPickup(BaseEntity? pickedUpObject, Vector3 physgunPos) => throw new NotImplementedException();
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
