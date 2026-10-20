#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
namespace Game.Shared.GarrysMod;
using FIELD = Source.FIELD<WeaponAR2>;

[LinkEntityToClass("weapon_ar2")]
[PrecacheWeaponRegister("weapon_ar2")]
[NetworkName("CWeaponAR2")]
public class WeaponAR2 : HL2MPMachineGun
{
	public static readonly
#if CLIENT_DLL
		RecvTable
#else
		SendTable
#endif
		DT_WeaponAR2 = new(DT_HL2MPMachineGun, [
#if CLIENT_DLL

#else

#endif
		]);
#if CLIENT_DLL
	public static readonly new ClientClass ClientClass = new ClientClass(DT_WeaponAR2);
	public static readonly new DataMap PredMap = new([], typeof(WeaponAR2), HL2MPMachineGun.PredMap); public override DataMap? GetPredDescMap() => PredMap;
#else
	public static readonly new ServerClass ServerClass = new ServerClass(DT_WeaponAR2);
#endif
	public override float GetFireRate() => 0.1f;
}
#endif
