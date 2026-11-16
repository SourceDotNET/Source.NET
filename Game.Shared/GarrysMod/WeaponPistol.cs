#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL

using Source.Common;
using Source.Common.Mathematics;
namespace Game.Shared.GarrysMod;


[LinkEntityToClass("weapon_pistol")]
[PrecacheWeaponRegister("weapon_pistol")]
[NetworkName("CWeaponPistol")]
public partial class WeaponPistol : HL2MPMachineGun
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(SoonestPrimaryAttack),
		nameof(LastAttackTime),
		new(nameof(AccuracyPenalty)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(NumShotsFired)) { Bits = 16 },
	];

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_MP_STAND_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCH_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_RUN, WeaponAct = Activity.ACT_HL2MP_RUN_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_WALK, WeaponAct = Activity.ACT_HL2MP_WALK_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCHWALK, WeaponAct = Activity.ACT_HL2MP_WALK_CROUCH_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM, WeaponAct = Activity.ACT_HL2MP_SWIM_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM_IDLE, WeaponAct = Activity.ACT_HL2MP_SWIM_IDLE_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_CROUCH_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_STAND, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_CROUCH, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_PISTOL, Required = false },
		new() { BaseAct = Activity.ACT_MP_JUMP, WeaponAct = Activity.ACT_HL2MP_JUMP_PISTOL, Required = false },
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

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(SoonestPrimaryAttack),
		nameof(LastAttackTime),
		nameof(AccuracyPenalty),
		nameof(NumShotsFired),
	];

	[NetworkVar("m_flSoonestPrimaryAttack")]
	public partial TimeUnit_t SoonestPrimaryAttack { get; set; }
	[NetworkVar("m_flLastAttackTime")]
	public partial TimeUnit_t LastAttackTime { get; set; }
	[NetworkVar("m_flAccuracyPenalty")]
	public partial float AccuracyPenalty { get; set; }
	[NetworkVar("m_nNumShotsFired")]
	public partial int NumShotsFired { get; set; }
	public override float GetFireRate() => 0.5f;
	public const TimeUnit_t PISTOL_FASTEST_REFIRE_TIME = 0.1;
	public const TimeUnit_t PISTOL_FASTEST_DRY_REFIRE_TIME = 0.2;
	public const TimeUnit_t PISTOL_ACCURACY_SHOT_PENALTY_TIME = 0.2;// Applied amount of time each shot adds to the time we must recover from
	public const TimeUnit_t PISTOL_ACCURACY_MAXIMUM_PENALTY_TIME = 1.5;// Maximum penalty to deal out

	public override bool Reload() {
		bool ret = DefaultReload(GetMaxClip1(), GetMaxClip2(), Activity.ACT_VM_RELOAD);
		if (ret) {
			WeaponSound(Shared.WeaponSound.Reload);
			AccuracyPenalty = 0.0f;
		}
		return ret;
	}

	public override void ItemPostFrame() {
		base.ItemPostFrame();

		if (InReload)
			return;

		BasePlayer? owner = ToBasePlayer(GetOwner());

		if (owner == null)
			return;

		if ((owner.Buttons & InButtons.Attack2) != 0) {
			LastAttackTime = gpGlobals.CurTime + PISTOL_FASTEST_REFIRE_TIME;
			SoonestPrimaryAttack = gpGlobals.CurTime + PISTOL_FASTEST_REFIRE_TIME;
			NextPrimaryAttack = gpGlobals.CurTime + PISTOL_FASTEST_REFIRE_TIME;
		}

		//Allow a refire as fast as the player can click
		if (((owner.Buttons & InButtons.Attack) == 0) && (SoonestPrimaryAttack < gpGlobals.CurTime))
			NextPrimaryAttack = gpGlobals.CurTime - 0.1;
		else if ((owner.Buttons & InButtons.Attack) != 0 && (NextPrimaryAttack < gpGlobals.CurTime) && (iClip1 <= 0))
			DryFire();
	}

	public void DryFire() {
		WeaponSound(Shared.WeaponSound.Empty);
		SendWeaponAnim(Activity.ACT_VM_DRYFIRE);

		SoonestPrimaryAttack = gpGlobals.CurTime + PISTOL_FASTEST_DRY_REFIRE_TIME;
		NextPrimaryAttack = gpGlobals.CurTime + SequenceDuration();
	}

	public override void PrimaryAttack() {
		if ((gpGlobals.CurTime - LastAttackTime) > 0.5f) 
			NumShotsFired = 0;
		else 
			NumShotsFired++;

		LastAttackTime = gpGlobals.CurTime;
		SoonestPrimaryAttack = gpGlobals.CurTime + PISTOL_FASTEST_REFIRE_TIME;

		BasePlayer? owner = ToBasePlayer(GetOwner());

		if (owner != null) {
			// Each time the player fires the pistol, reset the view punch. This prevents
			// the aim from 'drifting off' when the player fires very quickly. This may
			// not be the ideal way to achieve this, but it's cheap and it works, which is
			// great for a feature we're evaluating. (sjb)
			owner.ViewPunchReset();
		}

		base.PrimaryAttack();

		// Add an accuracy penalty which can move past our maximum penalty time if we're really spastic
		AccuracyPenalty += (float)PISTOL_ACCURACY_SHOT_PENALTY_TIME;
	}

	public override void AddViewKick() {
		BasePlayer? player = ToBasePlayer(GetOwner());

		if (player == null)
			return;

		QAngle viewPunch = default;

		viewPunch.X = SharedRandomFloat("pistolpax", 0.25f, 0.5f);
		viewPunch.Y = SharedRandomFloat("pistolpay", -0.6f, 0.6f);
		viewPunch.Z = 0.0f;

		player.ViewPunch(viewPunch);
	}
}
#endif
