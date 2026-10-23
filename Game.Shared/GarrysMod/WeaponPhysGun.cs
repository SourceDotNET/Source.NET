#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
#if CLIENT_DLL
global using PhysBeam = Game.Client.C_PhysBeam;
#else
global using PhysBeam = Game.Server.PhysBeam;
#endif

using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;

#if CLIENT_DLL
namespace Game.Client;
#else
namespace Game.Server;
#endif



[LinkEntityToClass("weapon_physgun")]
[PrecacheWeaponRegister("weapon_physgun")]
[NetworkName("CWeaponPhysGun")]
public partial class
#if CLIENT_DLL
	C_WeaponPhysGun : C_BaseHL2MPCombatWeapon
#else
	WeaponPhysGun : BaseHL2MPCombatWeapon
#endif
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(PhysBeam),
		new(nameof(HitPosLocal)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(GrabbedEntity),
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(HitPosLocal),
		nameof(GrabbedEntity),
	];

	[NetworkVar("m_hPhysBeam")]
	public partial EHANDLE PhysBeam { get; set; }
	[NetworkVar("m_vHitPosLocal")]
	public partial Vector3 HitPosLocal { get; set; }
	[NetworkVar("m_hGrabbedEntity")]
	public partial EHANDLE GrabbedEntity { get; set; }

	public override void Activate() {
		base.Activate();
	}

	// ActivityList?

	public
#if CLIENT_DLL
	C_WeaponPhysGun
#else
	WeaponPhysGun 
#endif
	() {
		PhysBeam = new();
		GrabbedEntity = new();
	}
	public override bool CanBePickedUpByNPCs() => false;
	public EHANDLE CreatePhysBeam() {
		RemovePhysBeam();

		if ((GetEFlags() & EFL.DirtyAbsTransform) != 0)
			CalcAbsolutePosition();

		return default;
	}
	public override bool Deploy() {
		DropEntity();
		CreatePhysBeam();
		return base.Deploy();
	}
	public override void Drop(in Vector3 velocity) {
		DropEntity();
		RemovePhysBeam();
		base.Drop(velocity);
	}
	public void DropEntity(){

	}
	public override float GetFireRate() => 0.01f;
	public override ReadOnlySpan<char> GetHoldType() => "physgun";
	public override bool HasAnyAmmo() => true;
	public override bool Holster(BaseCombatWeapon switchingTo) {
		DropEntity();
		RemovePhysBeam();
		return base.Holster(switchingTo);
	}
	public override void ItemPostFrame() {
		base.ItemPostFrame();
	}
	public override void Precache() {
		base.Precache();
	}
	public override void PrimaryAttack() {
		base.PrimaryAttack();
	}
	public override bool Reload() {
		return base.Reload();
	}
	public void RemovePhysBeam(){

	}
	public override void SecondaryAttack() {
		base.SecondaryAttack();
	}
	public override void UpdateOnRemove() {
		base.UpdateOnRemove();
	}
	public void UpdatePosition(){ }
	public void UpdateRotation(){ }
	public bool ValidatePhysObj() => false;
}
#endif
