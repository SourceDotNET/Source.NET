using Game.Shared;
using Game.Shared.HL2;

using Source.Common;

namespace Game.Server.HL2;

[LinkEntityToClass("weapon_annabelle")]
[NetworkName("CWeaponAnnabelle")]
public partial class WeaponAnnabelle : BaseHLCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
