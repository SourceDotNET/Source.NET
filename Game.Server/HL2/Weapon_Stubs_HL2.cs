using Game.Shared;
using Game.Shared.HL2;

using Source.Common;

using System.Numerics;

namespace Game.Server.HL2;

[LinkEntityToClass("cycler_weapon")]
[NetworkName("CWeaponCycler")]
public partial class WeaponCycler : BaseCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

[LinkEntityToClass("weapon_cubemap")]
[NetworkName("CWeaponCubemap")]
public partial class WeaponCubemap : BaseCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

[LinkEntityToClass("weapon_citizenpackage")]
[NetworkName("CWeaponCitizenPackage")]
public partial class WeaponCitizenPackage : BaseHLCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_IDLE, WeaponAct = Activity.ACT_IDLE_PACKAGE, Required = false },
		new() { BaseAct = Activity.ACT_WALK, WeaponAct = Activity.ACT_WALK_PACKAGE, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() => acttable;

	public override void ItemPostFrame() { }

	public override void Drop(in Vector3 velocity) {
		base.Drop(velocity);
		Util.Remove(this);
	}
}

[LinkEntityToClass("weapon_citizensuitcase")]
[NetworkName("CWeaponCitizenSuitcase")]
public partial class WeaponCitizenSuitcase : WeaponCitizenPackage
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_IDLE, WeaponAct = Activity.ACT_IDLE_SUITCASE, Required = false },
		new() { BaseAct = Activity.ACT_WALK, WeaponAct = Activity.ACT_WALK_SUITCASE, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() => acttable;
}

[LinkEntityToClass("weapon_oldmanharpoon")]
[NetworkName("CWeaponOldManHarpoon")]
public partial class WeaponOldManHarpoon : WeaponCitizenPackage
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_IDLE, WeaponAct = Activity.ACT_IDLE_SUITCASE, Required = false },
		new() { BaseAct = Activity.ACT_WALK, WeaponAct = Activity.ACT_WALK_SUITCASE, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() => acttable;
}
