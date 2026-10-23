using Source.Common;
using Game.Shared;

namespace Game.Server.HL1;

[LinkEntityToClass("basehl1mpcombatweapon")]
[NetworkName("CBaseHL1MPCombatWeapon")]
public partial class BaseHL1MPCombatWeapon : BaseHL1CombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
