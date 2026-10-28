using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_sporetrail")]
[NetworkName("SporeTrail", Table = "DT_SporeTrail")]
public partial class SporeTrail : BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SpawnRate)) { Bits = 8, Low = 1.0f, High = 1024.0f },
		new(nameof(EndColor)) { Bits = 8, Low = 0.0f, High = 1.0f },
		new(nameof(ParticleLifetime)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.1f, High = 100.0f },
		new(nameof(StartSize)) { Bits = -1, Flags = PropFlags.NoScale },
		new(nameof(EndSize)) { Bits = -1, Flags = PropFlags.NoScale },
		new(nameof(SpawnRadius)) { Bits = -1, Flags = PropFlags.NoScale },
		nameof(Emit),
	];

	[NetworkVar("m_flSpawnRate")]
	public partial float SpawnRate { get; set; }
	[NetworkVar("m_vecEndColor")]
	public partial Vector3 EndColor { get; set; }
	[NetworkVar("m_flParticleLifetime")]
	public partial float ParticleLifetime { get; set; }
	[NetworkVar("m_flStartSize")]
	public partial float StartSize { get; set; }
	[NetworkVar("m_flEndSize")]
	public partial float EndSize { get; set; }
	[NetworkVar("m_flSpawnRadius")]
	public partial float SpawnRadius { get; set; }
	[NetworkVar("m_bEmit")]
	public partial bool Emit { get; set; }
}
