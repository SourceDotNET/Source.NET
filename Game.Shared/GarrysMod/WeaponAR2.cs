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

	public override float GetFireRate() => 0.1f;
}
#endif
