using Source.Common;
using Game.Shared;

namespace Game.Client.HL1;

[LinkEntityToClass("basehl1combatweapon")]
[NetworkName("CBaseHL1CombatWeapon")]
public partial class C_BaseHL1CombatWeapon : C_BaseCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
