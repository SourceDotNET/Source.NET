global using static Game.Server.ItemConstants;

using Game.Shared;

using Source.Common.Mathematics;
using Source.Common.Physics;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Game.Server;

public static class ItemConstants
{
	public const int MAX_NORMAL_BATTERY = 100;
	public const int SIZE_AMMO_PISTOL = 20;
	public const int SIZE_AMMO_PISTOL_LARGE = 100;
	public const int SIZE_AMMO_SMG1 = 45;
	public const int SIZE_AMMO_SMG1_LARGE = 225;
	public const int SIZE_AMMO_AR2 = 20;
	public const int SIZE_AMMO_AR2_LARGE = 100;
	public const int SIZE_AMMO_RPG_ROUND = 1;
	public const int SIZE_AMMO_SMG1_GRENADE = 1;
	public const int SIZE_AMMO_BUCKSHOT = 20;
	public const int SIZE_AMMO_357 = 6;
	public const int SIZE_AMMO_357_LARGE = 20;
	public const int SIZE_AMMO_CROSSBOW = 6;
	public const int SIZE_AMMO_AR2_ALTFIRE = 1;
	public const int SF_ITEM_START_CONSTRAINED = 0x00000001;
}

public class Item : BaseAnimating, IPlayerPickupVPhysics
{
	readonly DefaultPlayerPickupVPhysics pickup = new();

	public bool ForcePhysgunOpen(BasePlayer player) => ((IPlayerPickupVPhysics)pickup).ForcePhysgunOpen(player);
	public bool HasPreferredCarryAnglesForPlayer(BasePlayer? player = null) => ((IPlayerPickupVPhysics)pickup).HasPreferredCarryAnglesForPlayer(player);
	public bool OnAttemptPhysGunPickup(BasePlayer physGunUser, PhysGunPickup reason = PhysGunPickup.PickedUpByCannon) => ((IPlayerPickupVPhysics)pickup).OnAttemptPhysGunPickup(physGunUser, reason);
	public BaseEntity? OnFailedPhysGunPickup(Vector3 physgunPos) => ((IPlayerPickupVPhysics)pickup).OnFailedPhysGunPickup(physgunPos);
	public void OnPhysGunDrop(BasePlayer physGunUser, PhysGunDrop reason) => ((IPlayerPickupVPhysics)pickup).OnPhysGunDrop(physGunUser, reason);
	public void OnPhysGunPickup(BasePlayer physGunUser, PhysGunPickup reason = PhysGunPickup.PickedUpByCannon) => ((IPlayerPickupVPhysics)pickup).OnPhysGunPickup(physGunUser, reason);
	public Vector3 PhysGunLaunchAngularImpulse() => ((IPlayerPickupVPhysics)pickup).PhysGunLaunchAngularImpulse();
	public Vector3 PhysGunLaunchVelocity(in Vector3 forward, float flMass) => ((IPlayerPickupVPhysics)pickup).PhysGunLaunchVelocity(forward, flMass);
	public QAngle PreferredCarryAngles() => ((IPlayerPickupVPhysics)pickup).PreferredCarryAngles();
	public bool ShouldPuntUseLaunchForces(PhysGunForce reason) => ((IPlayerPickupVPhysics)pickup).ShouldPuntUseLaunchForces(reason);

	public override EntityCapabilities ObjectCaps() => base.ObjectCaps() | EntityCapabilities.ImpulseUse | EntityCapabilities.WCEditPosition;

	public override void Use(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		BasePlayer? player = ToBasePlayer(activator);

		if (player != null)
			player.PickupObject(this);
	}

	// todoOutputEvent OnPlayerTouch;
	// todoOutputEvent OnCacheInteraction;

	Vector3 OriginalSpawnOrigin;
	QAngle OriginalSpawnAngles;

	IPhysicsConstraint? Constraint;
}
