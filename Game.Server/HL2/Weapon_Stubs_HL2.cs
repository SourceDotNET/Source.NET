using Game.Shared;
using Game.Shared.HL2;

using Source.Common;

using System.Numerics;

namespace Game.Server.HL2;

[LinkEntityToClass("cycler_weapon")]
[NetworkName("CWeaponCycler")]
public class WeaponCycler : BaseCombatWeapon
{
	public static readonly SendTable DT_WeaponCycler = new(DT_BaseCombatWeapon, []);
	public static new readonly ServerClass ServerClass = new ServerClass(DT_WeaponCycler);
}

[LinkEntityToClass("weapon_cubemap")]
[NetworkName("CWeaponCubemap")]
public class WeaponCubemap : BaseCombatWeapon
{
	public static readonly SendTable DT_WeaponCubemap = new(DT_BaseCombatWeapon, []);
	public static new readonly ServerClass ServerClass = new ServerClass(DT_WeaponCubemap);
}

[LinkEntityToClass("weapon_citizenpackage")]
[NetworkName("CWeaponCitizenPackage")]
public class WeaponCitizenPackage : BaseHLCombatWeapon
{
	public static readonly SendTable DT_WeaponCitizenPackage = new(DT_BaseHLCombatWeapon, []);
	public static new readonly ServerClass ServerClass = new ServerClass(DT_WeaponCitizenPackage);

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
public class WeaponCitizenSuitcase : WeaponCitizenPackage
{
	public static readonly SendTable DT_WeaponCitizenSuitcase = new(DT_WeaponCitizenPackage, []);
	public static new readonly ServerClass ServerClass = new ServerClass(DT_WeaponCitizenSuitcase);

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_IDLE, WeaponAct = Activity.ACT_IDLE_SUITCASE, Required = false },
		new() { BaseAct = Activity.ACT_WALK, WeaponAct = Activity.ACT_WALK_SUITCASE, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() => acttable;
}

[LinkEntityToClass("weapon_oldmanharpoon")]
[NetworkName("CWeaponOldManHarpoon")]
public class WeaponOldManHarpoon : WeaponCitizenPackage
{
	public static readonly SendTable DT_WeaponOldManHarpoon = new(DT_WeaponCitizenPackage, []);
	public static new readonly ServerClass ServerClass = new ServerClass(DT_WeaponOldManHarpoon);

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_IDLE, WeaponAct = Activity.ACT_IDLE_SUITCASE, Required = false },
		new() { BaseAct = Activity.ACT_WALK, WeaponAct = Activity.ACT_WALK_SUITCASE, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() => acttable;
}
