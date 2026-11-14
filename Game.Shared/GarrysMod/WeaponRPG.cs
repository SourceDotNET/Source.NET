#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;

using System.Numerics;
namespace Game.Shared.GarrysMod;


[LinkEntityToClass("weapon_rpg")]
[PrecacheWeaponRegister("weapon_rpg")]
[NetworkName("CWeaponRPG")]
public partial class WeaponRPG : BaseHL2MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(InitialStateUpdate),
		nameof(Guiding),
		nameof(HideGuiding),
		nameof(Missile),
		new(nameof(LaserDot)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_MP_STAND_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCH_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_RUN, WeaponAct = Activity.ACT_HL2MP_RUN_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_WALK, WeaponAct = Activity.ACT_HL2MP_WALK_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCHWALK, WeaponAct = Activity.ACT_HL2MP_WALK_CROUCH_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_CROUCH_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_STAND, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_CROUCH, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_JUMP, WeaponAct = Activity.ACT_HL2MP_JUMP_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM, WeaponAct = Activity.ACT_HL2MP_SWIM_RPG, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM_IDLE, WeaponAct = Activity.ACT_HL2MP_SWIM_IDLE_RPG, Required = false },
	];

	static readonly ActTable[] acttable_npc = [
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1, WeaponAct = Activity.ACT_RANGE_ATTACK_RPG, Required = true },
		new() { BaseAct = Activity.ACT_IDLE_RELAXED, WeaponAct = Activity.ACT_IDLE_RPG_RELAXED, Required = true },
		new() { BaseAct = Activity.ACT_IDLE_STIMULATED, WeaponAct = Activity.ACT_IDLE_ANGRY_RPG, Required = true },
		new() { BaseAct = Activity.ACT_IDLE_AGITATED, WeaponAct = Activity.ACT_IDLE_ANGRY_RPG, Required = true },
		new() { BaseAct = Activity.ACT_IDLE, WeaponAct = Activity.ACT_IDLE_RPG, Required = true },
		new() { BaseAct = Activity.ACT_IDLE_ANGRY, WeaponAct = Activity.ACT_IDLE_ANGRY_RPG, Required = true },
		new() { BaseAct = Activity.ACT_WALK, WeaponAct = Activity.ACT_WALK_RPG, Required = true },
		new() { BaseAct = Activity.ACT_WALK_CROUCH, WeaponAct = Activity.ACT_WALK_CROUCH_RPG, Required = true },
		new() { BaseAct = Activity.ACT_RUN, WeaponAct = Activity.ACT_RUN_RPG, Required = true },
		new() { BaseAct = Activity.ACT_RUN_CROUCH, WeaponAct = Activity.ACT_RUN_CROUCH_RPG, Required = true },
		new() { BaseAct = Activity.ACT_COVER_LOW, WeaponAct = Activity.ACT_COVER_LOW_RPG, Required = true },
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1_LOW, WeaponAct = Activity.ACT_RANGE_ATTACK_SMG1_LOW, Required = true },
		new() { BaseAct = Activity.ACT_RANGE_AIM_LOW, WeaponAct = Activity.ACT_RANGE_AIM_SMG1_LOW, Required = false },
		new() { BaseAct = Activity.ACT_RELOAD_LOW, WeaponAct = Activity.ACT_RELOAD_SMG1_LOW, Required = false },
		new() { BaseAct = Activity.ACT_CROUCHIDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_CROUCHIDLE_STIMULATED, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_CROUCHIDLE_AIM_STIMULATED, WeaponAct = Activity.ACT_RANGE_AIM_SMG1_LOW, Required = false },
		new() { BaseAct = Activity.ACT_CROUCHIDLE_AGITATED, WeaponAct = Activity.ACT_RANGE_AIM_SMG1_LOW, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() {
		BaseCombatCharacter? owner = GetOwner();
		if (owner != null && owner.IsNPC())
			return acttable_npc;
		return acttable;
	}

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(Guiding),
		nameof(HideGuiding),
	];

	public WeaponRPG() {
		Missile = new();
	}

	[NetworkVar("m_bInitialStateUpdate")]
	public partial bool InitialStateUpdate { get; set; }
	[NetworkVar("m_bGuiding")]
	public partial bool Guiding { get; set; }
	[NetworkVar("m_bHideGuiding")]
	public partial bool HideGuiding { get; set; }
	// todo: RecvProxy_MissileDied
	[NetworkVar("m_hMissile")]
	public partial EHANDLE Missile { get; set; }
	[NetworkVar("m_vecLaserDot")]
	public partial Vector3 LaserDot { get; set; }
	public override float GetFireRate() => 1f;

	public override bool Reload() {
		BaseCombatCharacter? owner = GetOwner();
		if (owner == null)
			return false;

		if (owner.GetAmmoCount(PrimaryAmmoType) <= 0)
			return false;

		WeaponSound(Shared.WeaponSound.Reload);

		SendWeaponAnim(Activity.ACT_VM_RELOAD);

		return true;
	}
}

[LinkEntityToClass("env_laserdot")]
#if CLIENT_DLL
[LinkEntityToClass("laser_spot")]
#endif
[NetworkName("CLaserDot")]
public partial class LaserDot : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
#endif
