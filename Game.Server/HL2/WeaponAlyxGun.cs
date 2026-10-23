using Game.Shared;
using Source.Common;

namespace Game.Server.HL2;

[LinkEntityToClass("weapon_alyxgun")]
[NetworkName("CWeaponAlyxGun")]
public partial class WeaponAlyxGun : HLSelectFireMachineGun
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
