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
