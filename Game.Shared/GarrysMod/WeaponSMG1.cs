#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;


[LinkEntityToClass("weapon_smg1")]
[PrecacheWeaponRegister("weapon_smg1")]
[NetworkName("CWeaponSMG1")]
public partial class WeaponSMG1 : HL2MPMachineGun
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_MP_STAND_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCH_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_RUN, WeaponAct = Activity.ACT_HL2MP_RUN_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_WALK, WeaponAct = Activity.ACT_HL2MP_WALK_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCHWALK, WeaponAct = Activity.ACT_HL2MP_WALK_CROUCH_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_CROUCH_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_STAND, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_CROUCH, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_JUMP, WeaponAct = Activity.ACT_HL2MP_JUMP_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM, WeaponAct = Activity.ACT_HL2MP_SWIM_SMG1, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM_IDLE, WeaponAct = Activity.ACT_HL2MP_SWIM_IDLE_SMG1, Required = false },
	];

	static readonly ActTable[] acttable_npc = [
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1, WeaponAct = Activity.ACT_RANGE_ATTACK_SMG1, Required = true },
		new() { BaseAct = Activity.ACT_RELOAD, WeaponAct = Activity.ACT_RELOAD_SMG1, Required = true },
		new() { BaseAct = Activity.ACT_IDLE, WeaponAct = Activity.ACT_IDLE_SMG1, Required = true },
		new() { BaseAct = Activity.ACT_IDLE_ANGRY, WeaponAct = Activity.ACT_IDLE_ANGRY_SMG1, Required = true },
		new() { BaseAct = Activity.ACT_WALK, WeaponAct = Activity.ACT_WALK_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_WALK_AIM, WeaponAct = Activity.ACT_WALK_AIM_RIFLE, Required = true },
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
		new() { BaseAct = Activity.ACT_GESTURE_RANGE_ATTACK1, WeaponAct = Activity.ACT_GESTURE_RANGE_ATTACK_SMG1, Required = true },
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1_LOW, WeaponAct = Activity.ACT_RANGE_ATTACK_SMG1_LOW, Required = true },
		new() { BaseAct = Activity.ACT_COVER_LOW, WeaponAct = Activity.ACT_COVER_SMG1_LOW, Required = false },
		new() { BaseAct = Activity.ACT_RANGE_AIM_LOW, WeaponAct = Activity.ACT_RANGE_AIM_SMG1_LOW, Required = false },
		new() { BaseAct = Activity.ACT_RELOAD_LOW, WeaponAct = Activity.ACT_RELOAD_SMG1_LOW, Required = false },
		new() { BaseAct = Activity.ACT_GESTURE_RELOAD, WeaponAct = Activity.ACT_GESTURE_RELOAD_SMG1, Required = true },
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

	public override float GetFireRate() => 0.075f;
	public override Activity GetPrimaryAttackActivity() {
		if (ShotsFired < 2)
			return Activity.ACT_VM_PRIMARYATTACK;

		if (ShotsFired < 3)
			return Activity.ACT_VM_RECOIL1;

		if (ShotsFired < 4)
			return Activity.ACT_VM_RECOIL2;

		return Activity.ACT_VM_RECOIL3;
	}
	public override bool Reload() {
		bool ret;
		TimeUnit_t cacheTime = NextSecondaryAttack;

		ret = DefaultReload(GetMaxClip1(), GetMaxClip2(), Activity.ACT_VM_RELOAD);
		if (ret) {
			// Undo whatever the reload process has done to our secondary
			// attack timer. We allow you to interrupt reloading to fire
			// a grenade.
			NextSecondaryAttack = GetOwner()!.NextAttack = cacheTime;

			WeaponSound(Shared.WeaponSound.Reload);
		}

		return ret;
	}

	public override void AddViewKick() {
		const float EASY_DAMPEN = 0.5f;
		const float MAX_VERTICAL_KICK = 1.0f;//Degrees
		const float SLIDE_LIMIT = 2.0f;//Seconds

		//Get the view kick
		BasePlayer? player = ToBasePlayer(GetOwner());

		if (player == null)
			return;

		DoMachineGunKick(player, EASY_DAMPEN, MAX_VERTICAL_KICK, FireDuration, SLIDE_LIMIT);
	}
	public override void SecondaryAttack() {
		// todo: spawn grenade
	}
}
#endif
