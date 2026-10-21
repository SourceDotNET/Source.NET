using Source.Common;
using Source;
using Game.Shared.HL2;

namespace Game.Server;

[NetworkName("CBaseHLBludgeonWeapon")]
public partial class BaseHLBludgeonWeapon : BaseHLCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
