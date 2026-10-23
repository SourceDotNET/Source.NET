using Source.Common;
using Game.Shared;

namespace Game.Server.HL1;

[LinkEntityToClass("basehl1combatweapon")]
[NetworkName("CBaseHL1CombatWeapon")]
public partial class BaseHL1CombatWeapon : BaseCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
