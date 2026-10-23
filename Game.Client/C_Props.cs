using Game.Client;
using Game.Shared;

using Source.Common;


namespace Game.Client;
using System.Numerics;
using FIELD_PBM = Source.FIELD<PhysBoxMultiplayer>;
using FIELD_BPD = Source.FIELD<C_BasePropDoor>;
using FIELD_PPM = Source.FIELD<PhysicsPropMultiplayer>;

[NetworkName("CDynamicProp")]
public partial class C_DynamicProp : C_BreakableProp
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(UseHitboxesForRenderBox),
	];

	[NetworkVar("m_bUseHitboxesForRenderBox")]
	public bool UseHitboxesForRenderBox;
}


[NetworkName("CPhysicsPropMultiplayer")]
public partial class PhysicsPropMultiplayer : C_PhysicsProp
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(PhysicsMode)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(Mass)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(CollisionMins)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(CollisionMaxs)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_iPhysicsMode")]
	public int PhysicsMode;
	[NetworkVar("m_fMass")]
	public float Mass;
	[NetworkVar("m_collisionMins")]
	public Vector3 CollisionMins;
	[NetworkVar("m_collisionMaxs")]
	public Vector3 CollisionMaxs;
}

[NetworkName("CPhysBoxMultiplayer")]
public partial class PhysBoxMultiplayer : C_PhysBox
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(PhysicsMode)) { Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(Mass)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_iPhysicsMode")]
	public int PhysicsMode;
	[NetworkVar("m_fMass")]
	public float Mass;
}


[NetworkName("CBasePropDoor")]
public partial class C_BasePropDoor : C_DynamicProp
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Locked),
		new(nameof(DoorState)) { Bits = 3, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_bLocked")]
	bool Locked;
	[NetworkVar("m_eDoorState")]
	int DoorState;
}


[NetworkName("CPropDoorRotating")]
public partial class C_PropDoorRotating : C_BasePropDoor
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
