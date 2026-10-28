using Source.Common;
using Source;
using Game.Shared.HL2;

namespace Game.Server;

[NetworkName("CHLMachineGun")]
public partial class HLMachineGun : BaseHLCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}


[NetworkName("CHLSelectFireMachineGun")]
public partial class HLSelectFireMachineGun : HLMachineGun
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
