using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
using Game.Server.HL2;
namespace Game.Server;
[LinkEntityToClass("prop_energy_ball")]
[NetworkName("CPropEnergyBall")]
public partial class PropEnergyBall : PropCombineBall
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(IsInfiniteLife),
		new(nameof(TimeTillDeath)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_bIsInfiniteLife")]
	public partial bool IsInfiniteLife { get; set; }
	[NetworkVar("m_fTimeTillDeath")]
	public partial float TimeTillDeath { get; set; }
}
