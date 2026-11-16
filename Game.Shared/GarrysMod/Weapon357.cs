#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL

using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;
namespace Game.Shared.GarrysMod;

[LinkEntityToClass("weapon_357")]
[PrecacheWeaponRegister("weapon_357")]
[NetworkName("CWeapon357")]
public partial class Weapon357 : BaseHL2MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_MP_STAND_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCH_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_RUN, WeaponAct = Activity.ACT_HL2MP_RUN_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_WALK, WeaponAct = Activity.ACT_HL2MP_WALK_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCHWALK, WeaponAct = Activity.ACT_HL2MP_WALK_CROUCH_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM, WeaponAct = Activity.ACT_HL2MP_SWIM_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM_IDLE, WeaponAct = Activity.ACT_HL2MP_SWIM_IDLE_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_CROUCH_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_STAND, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_CROUCH, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_REVOLVER, Required = false },
		new() { BaseAct = Activity.ACT_MP_JUMP, WeaponAct = Activity.ACT_HL2MP_JUMP_REVOLVER, Required = false },
	];

	static readonly ActTable[] acttable_npc = [
		new() { BaseAct = Activity.ACT_IDLE, WeaponAct = Activity.ACT_IDLE_PISTOL, Required = true },
		new() { BaseAct = Activity.ACT_IDLE_ANGRY, WeaponAct = Activity.ACT_IDLE_ANGRY_PISTOL, Required = true },
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1, WeaponAct = Activity.ACT_RANGE_ATTACK_PISTOL, Required = true },
		new() { BaseAct = Activity.ACT_RELOAD, WeaponAct = Activity.ACT_RELOAD_PISTOL, Required = true },
		new() { BaseAct = Activity.ACT_WALK_AIM, WeaponAct = Activity.ACT_WALK_AIM_PISTOL, Required = true },
		new() { BaseAct = Activity.ACT_RUN_AIM, WeaponAct = Activity.ACT_RUN_AIM_PISTOL, Required = true },
		new() { BaseAct = Activity.ACT_GESTURE_RANGE_ATTACK1, WeaponAct = Activity.ACT_GESTURE_RANGE_ATTACK_PISTOL, Required = true },
		new() { BaseAct = Activity.ACT_RELOAD_LOW, WeaponAct = Activity.ACT_RELOAD_PISTOL_LOW, Required = false },
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1_LOW, WeaponAct = Activity.ACT_RANGE_ATTACK_PISTOL_LOW, Required = false },
		new() { BaseAct = Activity.ACT_COVER_LOW, WeaponAct = Activity.ACT_COVER_PISTOL_LOW, Required = false },
		new() { BaseAct = Activity.ACT_RANGE_AIM_LOW, WeaponAct = Activity.ACT_RANGE_AIM_PISTOL_LOW, Required = false },
		new() { BaseAct = Activity.ACT_GESTURE_RELOAD, WeaponAct = Activity.ACT_GESTURE_RELOAD_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_WALK, WeaponAct = Activity.ACT_WALK_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_RUN, WeaponAct = Activity.ACT_RUN_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_RELAXED, WeaponAct = Activity.ACT_IDLE_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_STIMULATED, WeaponAct = Activity.ACT_IDLE_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_AGITATED, WeaponAct = Activity.ACT_IDLE_ANGRY_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_STEALTH, WeaponAct = Activity.ACT_IDLE_STEALTH_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_WALK_RELAXED, WeaponAct = Activity.ACT_WALK, Required = false },
		new() { BaseAct = Activity.ACT_WALK_STIMULATED, WeaponAct = Activity.ACT_WALK_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_WALK_AGITATED, WeaponAct = Activity.ACT_WALK_AIM_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_WALK_STEALTH, WeaponAct = Activity.ACT_WALK_STEALTH_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_RUN_RELAXED, WeaponAct = Activity.ACT_RUN, Required = false },
		new() { BaseAct = Activity.ACT_RUN_STIMULATED, WeaponAct = Activity.ACT_RUN_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_RUN_AGITATED, WeaponAct = Activity.ACT_RUN_AIM_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_RUN_STEALTH, WeaponAct = Activity.ACT_RUN_STEALTH_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_AIM_RELAXED, WeaponAct = Activity.ACT_IDLE_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_AIM_STIMULATED, WeaponAct = Activity.ACT_IDLE_ANGRY_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_AIM_AGITATED, WeaponAct = Activity.ACT_IDLE_ANGRY_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_AIM_STEALTH, WeaponAct = Activity.ACT_IDLE_STEALTH_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_WALK_AIM_RELAXED, WeaponAct = Activity.ACT_WALK, Required = false },
		new() { BaseAct = Activity.ACT_WALK_AIM_STIMULATED, WeaponAct = Activity.ACT_WALK_AIM_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_WALK_AIM_AGITATED, WeaponAct = Activity.ACT_WALK_AIM_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_WALK_AIM_STEALTH, WeaponAct = Activity.ACT_WALK_AIM_STEALTH_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_RUN_AIM_RELAXED, WeaponAct = Activity.ACT_RUN, Required = false },
		new() { BaseAct = Activity.ACT_RUN_AIM_STIMULATED, WeaponAct = Activity.ACT_RUN_AIM_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_RUN_AIM_AGITATED, WeaponAct = Activity.ACT_RUN_AIM_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_RUN_AIM_STEALTH, WeaponAct = Activity.ACT_RUN_AIM_STEALTH_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_CROUCHIDLE_STIMULATED, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_CROUCHIDLE_AIM_STIMULATED, WeaponAct = Activity.ACT_RANGE_AIM_PISTOL_LOW, Required = false },
		new() { BaseAct = Activity.ACT_CROUCHIDLE_AGITATED, WeaponAct = Activity.ACT_RANGE_AIM_PISTOL_LOW, Required = false },
		new() { BaseAct = Activity.ACT_READINESS_RELAXED_TO_STIMULATED, WeaponAct = Activity.ACT_READINESS_PISTOL_RELAXED_TO_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_READINESS_RELAXED_TO_STIMULATED_WALK, WeaponAct = Activity.ACT_READINESS_PISTOL_RELAXED_TO_STIMULATED_WALK, Required = false },
		new() { BaseAct = Activity.ACT_READINESS_AGITATED_TO_STIMULATED, WeaponAct = Activity.ACT_READINESS_PISTOL_AGITATED_TO_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_READINESS_STIMULATED_TO_RELAXED, WeaponAct = Activity.ACT_READINESS_PISTOL_STIMULATED_TO_RELAXED, Required = false },
	];

	public override ReadOnlySpan<ActTable> ActivityList() {
		BaseCombatCharacter? owner = GetOwner();
		if (owner != null && owner.IsNPC())
			return acttable_npc;
		return acttable;
	}

	public override void PrimaryAttack() {
		BasePlayer? player = ToBasePlayer(GetOwner());

		if (player == null)
			return;

		if (iClip1 <= 0) {
			if (!FireOnEmpty)
				Reload();
			else {
				WeaponSound(Shared.WeaponSound.Empty);
				NextPrimaryAttack = 0.15;
			}

			return;
		}

		WeaponSound(Shared.WeaponSound.Single);
		player.DoMuzzleFlash();

		SendWeaponAnim(Activity.ACT_VM_PRIMARYATTACK);
		player.SetAnimation(PlayerAnim.Attack1);

		NextPrimaryAttack = gpGlobals.CurTime + 0.75;
		NextSecondaryAttack = gpGlobals.CurTime + 0.75;

		iClip1--;

		Vector3 vecSrc = player.Weapon_ShootPosition();
		Vector3 vecAiming = player.GetAutoaimVector(AUTOAIM_5DEGREES);

		FireBulletsInfo info = new( 1, vecSrc, vecAiming, vec3_origin, MAX_TRACE_LENGTH, PrimaryAmmoType);
		info.Attacker = player;

		// Fire the bullets, and force the first shot to be perfectly accuracy
		player.FireBullets(info);

		//Disorient the player
		QAngle angles = player.GetLocalAngles();

		angles.X += random.RandomInt(-1, 1);
		angles.Y += random.RandomInt(-1, 1);
		angles.Z = 0;

#if !CLIENT_DLL
		player.SnapEyeAngles(angles);
#endif

		player.ViewPunch(new QAngle(-8, random.RandomFloat(-2, 2), 0));

		if (0 == iClip1 && player.GetAmmoCount(PrimaryAmmoType) <= 0) {
			// HEV suit - indicate out of ammo condition
			player.SetSuitUpdate("!HEV_AMO0", false, 0);
		}
	}
}
#endif
