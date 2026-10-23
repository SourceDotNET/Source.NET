using Source.Common;
using Game.Shared;

namespace Game.Client.HL1;

[LinkEntityToClass("basehl1mpcombatweapon")]
[NetworkName("CBaseHL1MPCombatWeapon")]
public partial class C_BaseHL1MPCombatWeapon : C_BaseHL1CombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
