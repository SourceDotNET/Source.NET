#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;

[LinkEntityToClass("weapon_ar2")]
[PrecacheWeaponRegister("weapon_ar2")]
[NetworkName("CWeaponAR2")]
public partial class WeaponAR2 : HL2MPMachineGun
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_MP_STAND_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCH_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_RUN, WeaponAct = Activity.ACT_HL2MP_RUN_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_WALK, WeaponAct = Activity.ACT_HL2MP_WALK_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCHWALK, WeaponAct = Activity.ACT_HL2MP_WALK_CROUCH_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_CROUCH_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_STAND, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_CROUCH, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_JUMP, WeaponAct = Activity.ACT_HL2MP_JUMP_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM, WeaponAct = Activity.ACT_HL2MP_SWIM_AR2, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM_IDLE, WeaponAct = Activity.ACT_HL2MP_SWIM_IDLE_AR2, Required = false },
	];

	static readonly ActTable[] acttable_npc = [
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1, WeaponAct = Activity.ACT_RANGE_ATTACK_AR2, Required = true },
		new() { BaseAct = Activity.ACT_RELOAD, WeaponAct = Activity.ACT_RELOAD_SMG1, Required = true },
		new() { BaseAct = Activity.ACT_IDLE, WeaponAct = Activity.ACT_IDLE_SMG1, Required = true },
		new() { BaseAct = Activity.ACT_IDLE_ANGRY, WeaponAct = Activity.ACT_IDLE_ANGRY_SMG1, Required = true },
		new() { BaseAct = Activity.ACT_WALK, WeaponAct = Activity.ACT_WALK_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_IDLE_RELAXED, WeaponAct = Activity.ACT_IDLE_SMG1_RELAXED, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_STIMULATED, WeaponAct = Activity.ACT_IDLE_SMG1_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_AGITATED, WeaponAct = Activity.ACT_IDLE_ANGRY_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_WALK_RELAXED, WeaponAct = Activity.ACT_WALK_RIFLE_RELAXED, Required = false },
		new() { BaseAct = Activity.ACT_WALK_STIMULATED, WeaponAct = Activity.ACT_WALK_RIFLE_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_WALK_AGITATED, WeaponAct = Activity.ACT_WALK_AIM_RIFLE, Required = false },
		new() { BaseAct = Activity.ACT_RUN_RELAXED, WeaponAct = Activity.ACT_RUN_RIFLE_RELAXED, Required = false },
		new() { BaseAct = Activity.ACT_RUN_STIMULATED, WeaponAct = Activity.ACT_RUN_RIFLE_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_RUN_AGITATED, WeaponAct = Activity.ACT_RUN_AIM_RIFLE, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_AIM_RELAXED, WeaponAct = Activity.ACT_IDLE_SMG1_RELAXED, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_AIM_STIMULATED, WeaponAct = Activity.ACT_IDLE_AIM_RIFLE_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_AIM_AGITATED, WeaponAct = Activity.ACT_IDLE_ANGRY_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_WALK_AIM_RELAXED, WeaponAct = Activity.ACT_WALK_RIFLE_RELAXED, Required = false },
		new() { BaseAct = Activity.ACT_WALK_AIM_STIMULATED, WeaponAct = Activity.ACT_WALK_AIM_RIFLE_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_WALK_AIM_AGITATED, WeaponAct = Activity.ACT_WALK_AIM_RIFLE, Required = false },
		new() { BaseAct = Activity.ACT_RUN_AIM_RELAXED, WeaponAct = Activity.ACT_RUN_RIFLE_RELAXED, Required = false },
		new() { BaseAct = Activity.ACT_RUN_AIM_STIMULATED, WeaponAct = Activity.ACT_RUN_AIM_RIFLE_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_RUN_AIM_AGITATED, WeaponAct = Activity.ACT_RUN_AIM_RIFLE, Required = false },
		new() { BaseAct = Activity.ACT_WALK_AIM, WeaponAct = Activity.ACT_WALK_AIM_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_WALK_CROUCH, WeaponAct = Activity.ACT_WALK_CROUCH_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_WALK_CROUCH_AIM, WeaponAct = Activity.ACT_WALK_CROUCH_AIM_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_RUN, WeaponAct = Activity.ACT_RUN_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_RUN_AIM, WeaponAct = Activity.ACT_RUN_AIM_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_RUN_CROUCH, WeaponAct = Activity.ACT_RUN_CROUCH_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_RUN_CROUCH_AIM, WeaponAct = Activity.ACT_RUN_CROUCH_AIM_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_GESTURE_RANGE_ATTACK1, WeaponAct = Activity.ACT_GESTURE_RANGE_ATTACK_AR2, Required = false },
		new() { BaseAct = Activity.ACT_COVER_LOW, WeaponAct = Activity.ACT_COVER_SMG1_LOW, Required = false },
		new() { BaseAct = Activity.ACT_RANGE_AIM_LOW, WeaponAct = Activity.ACT_RANGE_AIM_AR2_LOW, Required = false },
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1_LOW, WeaponAct = Activity.ACT_RANGE_ATTACK_SMG1_LOW, Required = true },
		new() { BaseAct = Activity.ACT_RELOAD_LOW, WeaponAct = Activity.ACT_RELOAD_SMG1_LOW, Required = false },
		new() { BaseAct = Activity.ACT_GESTURE_RELOAD, WeaponAct = Activity.ACT_GESTURE_RELOAD_SMG1, Required = true },
		new() { BaseAct = Activity.ACT_CROUCHIDLE_STIMULATED, WeaponAct = Activity.ACT_RANGE_AIM_SMG1_LOW, Required = false },
		new() { BaseAct = Activity.ACT_CROUCHIDLE_AIM_STIMULATED, WeaponAct = Activity.ACT_RANGE_AIM_SMG1_LOW, Required = false },
		new() { BaseAct = Activity.ACT_CROUCHIDLE_AGITATED, WeaponAct = Activity.ACT_RANGE_AIM_SMG1_LOW, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() {
		BaseCombatCharacter? owner = GetOwner();
		if (owner != null && owner.IsNPC())
			return acttable_npc;
		return acttable;
	}

	public override float GetFireRate() => 0.1f;
}
#endif
