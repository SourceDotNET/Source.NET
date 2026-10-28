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
