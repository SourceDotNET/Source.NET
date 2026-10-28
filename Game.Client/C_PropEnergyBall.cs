using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
using Game.Client.HL2;
namespace Game.Client;
[LinkEntityToClass("prop_energy_ball")]
[NetworkName("CPropEnergyBall")]
public partial class C_PropEnergyBall : C_PropCombineBall
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(IsInfiniteLife),
		new(nameof(TimeTillDeath)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[PredictionTable]
	public static readonly new PredictionField[] Prediction = [
		nameof(IsInfiniteLife),
		nameof(TimeTillDeath),
	];

	[NetworkVar("m_bIsInfiniteLife")]
	public bool IsInfiniteLife;
	[NetworkVar("m_fTimeTillDeath")]
	public float TimeTillDeath;
}
