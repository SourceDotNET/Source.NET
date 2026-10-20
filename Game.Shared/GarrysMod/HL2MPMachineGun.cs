#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;
namespace Game.Shared.GarrysMod;

using FIELD = Source.FIELD<HL2MPMachineGun>;
[NetworkName("CHL2MPMachineGun")]
public class HL2MPMachineGun : BaseHL2MPCombatWeapon
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_HL2MPMachineGun = new(DT_BaseHL2MPCombatWeapon, [
#if CLIENT_DLL
		RecvPropInt(FIELD.OF(nameof(ShotsFired))),
#else
		SendPropInt(FIELD.OF(nameof(ShotsFired)), 16),
#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_HL2MPMachineGun);
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_HL2MPMachineGun);
#endif
	[NetworkName("m_nShotsFired")]
	public int ShotsFired;

	public void DoMachineGunKick(BasePlayer player, float dampEasy, float maxVerticleKickAngle, TimeUnit_t fireDurationTime, TimeUnit_t slideLimitTime) {
		const float KICK_MIN_X = 0.2f;//Degrees
		const float KICK_MIN_Y = 0.2f;//Degrees
		const float KICK_MIN_Z = 0.1f;//Degrees

		QAngle vecScratch;
		int iSeed = BaseEntity.GetPredictionRandomSeed() & 255;

		//Find how far into our accuracy degradation we are
		TimeUnit_t duration = (fireDurationTime > slideLimitTime) ? slideLimitTime : fireDurationTime;
		float kickPerc = (float)duration / (float)slideLimitTime;

		// do this to get a hard discontinuity, clear out anything under 10 degrees punch
		player.ViewPunchReset(10);

		//Apply this to the view angles as well
		vecScratch.X = -(KICK_MIN_X + (maxVerticleKickAngle * kickPerc));
		vecScratch.Y = -(KICK_MIN_Y + (maxVerticleKickAngle * kickPerc)) / 3;
		vecScratch.Z = KICK_MIN_Z + (maxVerticleKickAngle * kickPerc) / 8;

		RandomSeed(iSeed);

		//Wibble left and right
		if (RandomInt(-1, 1) >= 0)
			vecScratch.Y *= -1;

		iSeed++;

		//Wobble up and down
		if (RandomInt(-1, 1) >= 0)
			vecScratch.Z *= -1;

		//Clip this to our desired min/max
		Util.ClipPunchAngleOffset(ref vecScratch, player.Local.PunchAngle, new QAngle(24.0f, 3.0f, 1.0f));

		//Add it to the view punch
		// NOTE: 0.5 is just tuned to match the old effect before the punch became simulated
		player.ViewPunch(vecScratch * 0.5f);
	}
	public override void PrimaryAttack() {
		BasePlayer? player = ToBasePlayer(GetOwner());
		if (player == null)
			return;

		// Abort here to handle burst and auto fire modes
		if ((UsesClipsForAmmo1() && iClip1 == 0) || (!UsesClipsForAmmo1() && 0 == player.GetAmmoCount(PrimaryAmmoType)))
			return;

		ShotsFired++;

		player.DoMuzzleFlash();

		// To make the firing framerate independent, we may have to fire more than one bullet here on low-framerate systems, 
		// especially if the weapon we're firing has a really fast rate of fire.
		int iBulletsToFire = 0;
		float fireRate = GetFireRate();

		while (NextPrimaryAttack <= gpGlobals.CurTime) {
			// MUST call sound before removing a round from the clip of a CHLMachineGun
			WeaponSound(Shared.WeaponSound.Single, NextPrimaryAttack);
			NextPrimaryAttack = NextPrimaryAttack + fireRate;
			iBulletsToFire++;
		}

		// Make sure we don't fire more than the amount in the clip, if this weapon uses clips
		if (UsesClipsForAmmo1()) {
			if (iBulletsToFire > iClip1)
				iBulletsToFire = iClip1;
			iClip1 -= iBulletsToFire;
		}

		HL2MP_Player hL2MPPlayer = ToHL2MPPlayer(player)!;

		// Fire the bullets
		FireBulletsInfo info = default;
		info.Shots = iBulletsToFire;
		info.Src = hL2MPPlayer.Weapon_ShootPosition();
		info.DirShooting = player.GetAutoaimVector(0.08715574274766f);
		info.Spread = hL2MPPlayer.GetAttackSpread(this);
		info.Distance = MAX_TRACE_LENGTH;
		info.AmmoType = PrimaryAmmoType;
		info.TracerFreq = 2;
		FireBullets(info);

		//Factor in the view kick
		AddViewKick();

		if (0 == iClip1 && player.GetAmmoCount(PrimaryAmmoType) <= 0)
			// HEV suit - indicate out of ammo condition
			player.SetSuitUpdate("!HEV_AMO0", false, 0);

		SendWeaponAnim(GetPrimaryAttackActivity());
		player.SetAnimation(PlayerAnim.Attack1);
	}
	public override void ItemPostFrame() {
		BasePlayer? owner = ToBasePlayer(GetOwner());

		if (owner == null)
			return;

		// Debounce the recoiling counter
		if ((owner.Buttons & InButtons.Attack) == 0) 
			ShotsFired = 0;

		base.ItemPostFrame();
	}
	public override bool Deploy() {
		ShotsFired = 0;
		return base.Deploy();
	}
	public override void FireBullets(in FireBulletsInfo info) {
		BasePlayer? player;
		if ((player = ToBasePlayer(GetOwner())) != null) 
			player.FireBullets(info);
	}
	static Vector3 cone = VECTOR_CONE_3DEGREES;
	public override ref readonly Vector3 GetBulletSpread() {
		return ref cone;
	}
}
#endif
