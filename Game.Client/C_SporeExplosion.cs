using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("SporeExplosion", Table = "DT_SporeExplosion")]
public partial class C_SporeExplosion : C_BaseParticleEntity
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
	public float SpawnRate;
	[NetworkVar("m_flParticleLifetime")]
	public float ParticleLifetime;
	[NetworkVar("m_flStartSize")]
	public float StartSize;
	[NetworkVar("m_flEndSize")]
	public float EndSize;
	[NetworkVar("m_flSpawnRadius")]
	public float SpawnRadius;
	[NetworkVar("m_bEmit")]
	public bool Emit;
	[NetworkVar("m_bDontRemove")]
	public bool DontRemove;
}
