using Game.Server;
using Game.Shared;
using Game.Shared.HL2;

using Source;
using Source.Common;
using Source.Common.Commands;

namespace Game.Client.HL2;

[NetworkName("CHLMachineGun")]
public partial class C_HLMachineGun : BaseHLCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

[NetworkName("CHLSelectFireMachineGun")]
public partial class C_HLSelectFireMachineGun : C_HLMachineGun
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}

[NetworkName("CBaseHLBludgeonWeapon")]
public partial class C_BaseHLBludgeonWeapon : BaseHLCombatWeapon
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
