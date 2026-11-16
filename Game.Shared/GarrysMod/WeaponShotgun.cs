#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;
namespace Game.Shared.GarrysMod;

[LinkEntityToClass("weapon_shotgun")]
[PrecacheWeaponRegister("weapon_shotgun")]
[NetworkName("CWeaponShotgun")]
public partial class WeaponShotgun : BaseHL2MPCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(NeedPump),
		nameof(DelayedFire1),
		nameof(DelayedFire2),
		nameof(DelayedReload),
	];

	static readonly ActTable[] acttable = [
		new() { BaseAct = Activity.ACT_MP_STAND_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCH_IDLE, WeaponAct = Activity.ACT_HL2MP_IDLE_CROUCH_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_RUN, WeaponAct = Activity.ACT_HL2MP_RUN_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_WALK, WeaponAct = Activity.ACT_HL2MP_WALK_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_CROUCHWALK, WeaponAct = Activity.ACT_HL2MP_WALK_CROUCH_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_ATTACK_CROUCH_PRIMARYFIRE, WeaponAct = Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_STAND, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_RELOAD_CROUCH, WeaponAct = Activity.ACT_HL2MP_GESTURE_RELOAD_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_JUMP, WeaponAct = Activity.ACT_HL2MP_JUMP_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM, WeaponAct = Activity.ACT_HL2MP_SWIM_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_MP_SWIM_IDLE, WeaponAct = Activity.ACT_HL2MP_SWIM_IDLE_SHOTGUN, Required = false },
	];

	static readonly ActTable[] acttable_npc = [
		new() { BaseAct = Activity.ACT_IDLE, WeaponAct = Activity.ACT_IDLE_SMG1, Required = true },
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1, WeaponAct = Activity.ACT_RANGE_ATTACK_SHOTGUN, Required = true },
		new() { BaseAct = Activity.ACT_RELOAD, WeaponAct = Activity.ACT_RELOAD_SHOTGUN, Required = false },
		new() { BaseAct = Activity.ACT_WALK, WeaponAct = Activity.ACT_WALK_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_IDLE_ANGRY, WeaponAct = Activity.ACT_IDLE_ANGRY_SHOTGUN, Required = true },
		new() { BaseAct = Activity.ACT_IDLE_RELAXED, WeaponAct = Activity.ACT_IDLE_SHOTGUN_RELAXED, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_STIMULATED, WeaponAct = Activity.ACT_IDLE_SHOTGUN_STIMULATED, Required = false },
		new() { BaseAct = Activity.ACT_IDLE_AGITATED, WeaponAct = Activity.ACT_IDLE_SHOTGUN_AGITATED, Required = false },
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
		new() { BaseAct = Activity.ACT_WALK_AIM, WeaponAct = Activity.ACT_WALK_AIM_SHOTGUN, Required = true },
		new() { BaseAct = Activity.ACT_WALK_CROUCH, WeaponAct = Activity.ACT_WALK_CROUCH_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_WALK_CROUCH_AIM, WeaponAct = Activity.ACT_WALK_CROUCH_AIM_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_RUN, WeaponAct = Activity.ACT_RUN_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_RUN_AIM, WeaponAct = Activity.ACT_RUN_AIM_SHOTGUN, Required = true },
		new() { BaseAct = Activity.ACT_RUN_CROUCH, WeaponAct = Activity.ACT_RUN_CROUCH_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_RUN_CROUCH_AIM, WeaponAct = Activity.ACT_RUN_CROUCH_AIM_RIFLE, Required = true },
		new() { BaseAct = Activity.ACT_GESTURE_RANGE_ATTACK1, WeaponAct = Activity.ACT_GESTURE_RANGE_ATTACK_SHOTGUN, Required = true },
		new() { BaseAct = Activity.ACT_RANGE_ATTACK1_LOW, WeaponAct = Activity.ACT_RANGE_ATTACK_SHOTGUN_LOW, Required = true },
		new() { BaseAct = Activity.ACT_RELOAD_LOW, WeaponAct = Activity.ACT_RELOAD_SHOTGUN_LOW, Required = false },
		new() { BaseAct = Activity.ACT_GESTURE_RELOAD, WeaponAct = Activity.ACT_GESTURE_RELOAD_SHOTGUN, Required = false },
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
		nameof(NeedPump),
		nameof(DelayedFire1),
		nameof(DelayedFire2),
		nameof(DelayedReload),
	];

	[NetworkVar("m_bNeedPump")]
	public partial bool NeedPump { get; set; }
	[NetworkVar("m_bDelayedFire1")]
	public partial bool DelayedFire1 { get; set; }
	[NetworkVar("m_bDelayedFire2")]
	public partial bool DelayedFire2 { get; set; }
	[NetworkVar("m_bDelayedReload")]
	public partial bool DelayedReload { get; set; }
	public WeaponShotgun() {
		ReloadsSingly = true;

		NeedPump = false;
		DelayedFire1 = false;
		DelayedFire2 = false;

		MinRange1 = 0.0f;
		MaxRange1 = 500f;
		MinRange2 = 0.0f;
		MaxRange2 = 200f;
	}
	public override float GetFireRate() => 0.7f;
	public override void ItemPostFrame() {
		BasePlayer? owner = ToBasePlayer(GetOwner());
		if (owner == null)
			return;

		if (NeedPump && 0 != (owner.Buttons & InButtons.Reload))
			DelayedReload = true;


		if (InReload) {
			// If I'm primary firing and have one round stop reloading and fire
			if ((owner.Buttons & InButtons.Attack) != 0 && (iClip1 >= 1) && !NeedPump) {
				InReload = false;
				NeedPump = false;
				DelayedFire1 = true;
			}
			// If I'm secondary firing and have two rounds stop reloading and fire
			else if ((owner.Buttons & InButtons.Attack2) != 0 && (iClip1 >= 2) && !NeedPump) {
				InReload = false;
				NeedPump = false;
				DelayedFire2 = true;
			}
			else if (NextPrimaryAttack <= gpGlobals.CurTime) {
				// If out of ammo end reload
				if (owner.GetAmmoCount(PrimaryAmmoType) <= 0) {
					FinishReload();
					return;
				}
				// If clip not full reload again
				if (iClip1 < GetMaxClip1()) {
					Reload();
					return;
				}
				// Clip full, stop reloading
				else {
					FinishReload();
					return;
				}
			}
		}
		else {
			// Make shotgun shell invisible
			SetBodygroup(1, 1);
		}

		if ((NeedPump) && (NextPrimaryAttack <= gpGlobals.CurTime)) {
			Pump();
			return;
		}

		// Shotgun uses same timing and ammo for secondary attack
		if ((DelayedFire2 || (owner.Buttons & InButtons.Attack2) != 0) && (NextPrimaryAttack <= gpGlobals.CurTime)) {
			DelayedFire2 = false;

			if ((iClip1 <= 1 && UsesClipsForAmmo1())) {
				// If only one shell is left, do a single shot instead	
				if (iClip1 == 1)
					PrimaryAttack();
				else if (0 == owner.GetAmmoCount(PrimaryAmmoType))
					DryFire();
				else
					StartReload();
			}

			// Fire underwater?
			else if (GetOwner()!.GetWaterLevel() == (WaterLevel)3 && FiresUnderwater == false) {
				WeaponSound(Shared.WeaponSound.Empty);
				NextPrimaryAttack = gpGlobals.CurTime + 0.2;
				return;
			}
			else {
				// If the firing button was just pressed, reset the firing time
				if ((owner.AfButtonPressed & InButtons.Attack) != 0)
					NextPrimaryAttack = gpGlobals.CurTime;
				SecondaryAttack();
			}
		}
		else if ((DelayedFire1 || (owner.Buttons & InButtons.Attack) != 0) && NextPrimaryAttack <= gpGlobals.CurTime) {
			DelayedFire1 = false;
			if ((iClip1 <= 0 && UsesClipsForAmmo1()) || (!UsesClipsForAmmo1() && 0 == owner.GetAmmoCount(PrimaryAmmoType))) {
				if (0 == owner.GetAmmoCount(PrimaryAmmoType))
					DryFire();
				else
					StartReload();
			}
			// Fire underwater?
			else if (owner.GetWaterLevel() == (WaterLevel)3 && FiresUnderwater == false) {
				WeaponSound(Shared.WeaponSound.Empty);
				NextPrimaryAttack = gpGlobals.CurTime + 0.2;
				return;
			}
			else {
				// If the firing button was just pressed, reset the firing time
				BasePlayer? player = ToBasePlayer(GetOwner());
				if (player != null && (player.AfButtonPressed & InButtons.Attack) != 0)
					NextPrimaryAttack = gpGlobals.CurTime;
				PrimaryAttack();
			}
		}

		if ((owner.Buttons & InButtons.Reload) != 0 && UsesClipsForAmmo1() && !InReload) {
			// reload when reload is pressed, or if no buttons are down and weapon is empty.
			StartReload();
		}
		else {
			// no fire buttons down
			FireOnEmpty = false;

			if (!HasAnyAmmo() && NextPrimaryAttack < gpGlobals.CurTime) {
				// weapon isn't useable, switch.
				if (0 == (GetWeaponFlags() & WeaponFlags.NoAutoSwitchEmpty) && owner.SwitchToNextBestWeapon(this)) {
					NextPrimaryAttack = gpGlobals.CurTime + 0.3;
					return;
				}
			}
			else {
				// weapon is useable. Reload if empty and weapon has waited as long as it has to after firing
				if (iClip1 <= 0 && 0 == (GetWeaponFlags() & WeaponFlags.NoAutoReload) && NextPrimaryAttack < gpGlobals.CurTime) {
					if (StartReload()) {
						// if we've successfully started to reload, we're done
						return;
					}
				}
			}

			WeaponIdle();
			return;
		}
	}

	private void Pump() {
		BaseCombatCharacter? owner = GetOwner();
		if (owner == null)
			return;

		NeedPump = false;

		if (DelayedReload) {
			DelayedReload = false;
			StartReload();
		}

		WeaponSound(Shared.WeaponSound.Special1);

		// Finish reload animation
		SendWeaponAnim(Activity.ACT_SHOTGUN_PUMP);

		owner.NextAttack = gpGlobals.CurTime + SequenceDuration();
		NextPrimaryAttack = gpGlobals.CurTime + SequenceDuration();
	}

	private void DryFire() {
		WeaponSound(Shared.WeaponSound.Empty);
		SendWeaponAnim(Activity.ACT_VM_DRYFIRE);
		NextPrimaryAttack = gpGlobals.CurTime + SequenceDuration();
	}

	private bool StartReload() {
		if (NeedPump)
			return false;

		BaseCombatCharacter? owner = GetOwner();

		if (owner == null)
			return false;

		if (owner.GetAmmoCount(PrimaryAmmoType) <= 0)
			return false;

		if (iClip1 >= GetMaxClip1())
			return false;


		int j = Math.Min(1, owner.GetAmmoCount(PrimaryAmmoType));

		if (j <= 0)
			return false;

		SendWeaponAnim(Activity.ACT_SHOTGUN_RELOAD_START);

		// Make shotgun shell visible
		SetBodygroup(1, 0);

		owner.NextAttack = gpGlobals.CurTime;
		NextPrimaryAttack = gpGlobals.CurTime + SequenceDuration();

		InReload = true;
		return true;
	}

	public override bool Reload() {
		if (!InReload)
			Warning("ERROR: Shotgun Reload called incorrectly!\n");

		BaseCombatCharacter? owner = GetOwner();

		if (owner == null)
			return false;

		if (owner.GetAmmoCount(PrimaryAmmoType) <= 0)
			return false;

		if (iClip1 >= GetMaxClip1())
			return false;

		int j = Math.Min(1, owner.GetAmmoCount(PrimaryAmmoType));

		if (j <= 0)
			return false;

		FillClip();
		WeaponSound(Shared.WeaponSound.Reload);
		SendWeaponAnim(Activity.ACT_VM_RELOAD);

		owner.NextAttack = gpGlobals.CurTime;
		NextPrimaryAttack = gpGlobals.CurTime + SequenceDuration();

		return true;
	}

	public override void FinishReload() {
		SetBodygroup(1, 1);

		BaseCombatCharacter? owner = GetOwner();

		if (owner == null)
			return;

		InReload = false;

		SendWeaponAnim(Activity.ACT_SHOTGUN_RELOAD_FINISH);

		owner.NextAttack = gpGlobals.CurTime;
		NextPrimaryAttack = gpGlobals.CurTime + SequenceDuration();
	}

	private void FillClip() {
		BaseCombatCharacter? owner = GetOwner();

		if (owner == null)
			return;

		if (owner.GetAmmoCount(PrimaryAmmoType) > 0) {
			if (Clip1() < GetMaxClip1()) {
				iClip1++;
				owner.RemoveAmmo(1, PrimaryAmmoType);
			}
		}
	}

	public override void PrimaryAttack() {
		// Only the player fires this way so we can cast
		BasePlayer? player = ToBasePlayer(GetOwner());

		if (player == null)
			return;

		// MUST call sound before removing a round from the clip of a CMachineGun
		WeaponSound(Shared.WeaponSound.Single);

		player.DoMuzzleFlash();

		SendWeaponAnim(Activity.ACT_VM_PRIMARYATTACK);

		// Don't fire again until fire animation has completed
		NextPrimaryAttack = gpGlobals.CurTime + SequenceDuration();
		iClip1 -= 1;

		// player "shoot" animation
		player.SetAnimation(PlayerAnim.Attack1);

		Vector3 vecSrc = player.Weapon_ShootPosition();
		Vector3 vecAiming = player.GetAutoaimVector(AUTOAIM_10DEGREES);

		FireBulletsInfo info = new(7, vecSrc, vecAiming, GetBulletSpread(), MAX_TRACE_LENGTH, PrimaryAmmoType);
		info.Attacker = player;

		// Fire the bullets, and force the first shot to be perfectly accuracy
		player.FireBullets(info);

		QAngle punch = default;
		punch.Init(SharedRandomFloat("shotgunpax", -2, -1), SharedRandomFloat("shotgunpay", -2, 2), 0);
		player.ViewPunch(punch);

		if (0 == iClip1 && player.GetAmmoCount(PrimaryAmmoType) <= 0)
			// HEV suit - indicate out of ammo condition
			player.SetSuitUpdate("!HEV_AMO0", false, 0);


		NeedPump = true;
	}
	public override void SecondaryAttack() {
		// Only the player fires this way so we can cast
		BasePlayer? player = ToBasePlayer(GetOwner());

		if (player == null)
			return;

		player.Buttons &= ~InButtons.Attack2;
		// MUST call sound before removing a round from the clip of a CMachineGun
		WeaponSound(Shared.WeaponSound.Double);

		player.DoMuzzleFlash();

		SendWeaponAnim(Activity.ACT_VM_SECONDARYATTACK);

		// Don't fire again until fire animation has completed
		NextPrimaryAttack = gpGlobals.CurTime + SequenceDuration();
		iClip1 -= 2;  // Shotgun uses same clip for primary and secondary attacks

		// player "shoot" animation
		player.SetAnimation(PlayerAnim.Attack1);

		Vector3 vecSrc = player.Weapon_ShootPosition();
		Vector3 vecAiming = player.GetAutoaimVector(AUTOAIM_10DEGREES);

		FireBulletsInfo info = new(12, vecSrc, vecAiming, GetBulletSpread(), MAX_TRACE_LENGTH, PrimaryAmmoType);
		info.Attacker = player;

		// Fire the bullets, and force the first shot to be perfectly accuracy
		player.FireBullets(info);
		player.ViewPunch(new QAngle(SharedRandomFloat("shotgunsax", -5, 5), 0, 0));

		if (0 == iClip1 && player.GetAmmoCount(PrimaryAmmoType) <= 0) {
			// HEV suit - indicate out of ammo condition
			player.SetSuitUpdate("!HEV_AMO0", false, 0);
		}

		NeedPump = true;
	}
	public override void ItemHolsterFrame() {
		var owner = GetOwner();
		if (owner == null) return;

		// Must be player held
		if (!owner.IsPlayer())
			return;

		// We can't be active
		if (owner.GetActiveWeapon() == this)
			return;

		// If it's been longer than three seconds, reload
		if ((gpGlobals.CurTime - HolsterTime) > sk_auto_reload_time.GetDouble()) {
			// Reset the timer
			HolsterTime = gpGlobals.CurTime;

			if (iClip1 == GetMaxClip1())
				return;

			// Just load the clip with no animations
			int ammoFill = Math.Min((GetMaxClip1() - iClip1), owner.GetAmmoCount(GetPrimaryAmmoType()));

			owner.RemoveAmmo(ammoFill, GetPrimaryAmmoType());
			iClip1 += ammoFill;
		}
	}
}
#endif
