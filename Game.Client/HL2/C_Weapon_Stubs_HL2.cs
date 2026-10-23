using Game.Shared;
using Game.Client.HL2;

using Source.Common;
using Source.Engine;

namespace Game.Client.HL2;

[LinkEntityToClass("cycler_weapon")]
[NetworkName("CWeaponCycler")]
public partial class C_WeaponCycler : C_BaseCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

[LinkEntityToClass("weapon_annabelle")]
[NetworkName("CWeaponAnnabelle")]
public partial class C_WeaponAnnabelle : C_BaseHLCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

[LinkEntityToClass("weapon_alyxgun")]
[NetworkName("CWeaponAlyxGun")]
public partial class C_WeaponAlyxGun : C_HLSelectFireMachineGun
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

[LinkEntityToClass("weapon_citizenpackage")]
[NetworkName("CWeaponCitizenPackage")]
public partial class C_WeaponCitizenPackage : C_BaseHLCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

[LinkEntityToClass("weapon_citizensuitcase")]
[NetworkName("CWeaponCitizenSuitcase")]
public partial class C_WeaponCitizenSuitcase : C_WeaponCitizenPackage
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

[LinkEntityToClass("weapon_cubemap")]
[NetworkName("CWeaponCubemap")]
public partial class C_WeaponCubemap : C_BaseCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}


[LinkEntityToClass("weapon_oldmanharpoon")]
[NetworkName("CWeaponOldManHarpoon")]
public partial class C_WeaponOldManHarpoon : C_WeaponCitizenPackage
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
