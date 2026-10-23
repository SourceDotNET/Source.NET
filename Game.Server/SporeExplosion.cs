using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_sporeexplosion")]
[NetworkName("SporeExplosion", Table = "DT_SporeExplosion")]
public partial class SporeExplosion : BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SpawnRate)) { Bits = 8, Low = 1.0f, High = 1024.0f },
		new(nameof(ParticleLifetime)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.1f, High = 100.0f },
		new(nameof(StartSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EndSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpawnRadius)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Emit),
		nameof(DontRemove),
	];

	[NetworkVar("m_flSpawnRate")]
	public partial float SpawnRate { get; set; }
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
	[NetworkVar("m_bDontRemove")]
	public partial bool DontRemove { get; set; }
}
