#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;

using FIELD = Source.FIELD<WeaponSMG1>;

[LinkEntityToClass("weapon_smg1")]
[PrecacheWeaponRegister("weapon_smg1")]
[NetworkName("CWeaponSMG1")]
public class WeaponSMG1 : HL2MPMachineGun
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponSMG1 = new(DT_HL2MPMachineGun, [
#if CLIENT_DLL

#else

#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponSMG1);
	public static readonly new DataMap PredMap = new([], typeof(WeaponSMG1), HL2MPMachineGun.PredMap); public override DataMap? GetPredDescMap() => PredMap;
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponSMG1);
#endif
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
