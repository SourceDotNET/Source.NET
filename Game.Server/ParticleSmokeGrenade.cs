using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_particlesmokegrenade")]
[NetworkName("ParticleSmokeGrenade", Table = "DT_ParticleSmokeGrenade")]
public partial class ParticleSmokeGrenade : BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SpawnTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeStartTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeEndTime)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(CurrentStage),
	];

	[NetworkVar("m_flSpawnTime")]
	public partial float SpawnTime { get; set; }
	[NetworkVar("m_FadeStartTime")]
	public partial float FadeStartTime { get; set; }
	[NetworkVar("m_FadeEndTime")]
	public partial float FadeEndTime { get; set; }
	[NetworkVar("m_CurrentStage")]
	public partial bool CurrentStage { get; set; }
}
