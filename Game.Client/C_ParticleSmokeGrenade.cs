using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("ParticleSmokeGrenade", Table = "DT_ParticleSmokeGrenade")]
public partial class C_ParticleSmokeGrenade : C_BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SpawnTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeStartTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeEndTime)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(CurrentStage),
	];

	[NetworkVar("m_flSpawnTime")]
	public float SpawnTime;
	[NetworkVar("m_FadeStartTime")]
	public float FadeStartTime;
	[NetworkVar("m_FadeEndTime")]
	public float FadeEndTime;
	[NetworkVar("m_CurrentStage")]
	public bool CurrentStage;
}
