using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("SporeTrail", Table = "DT_SporeTrail")]
public partial class C_SporeTrail : C_BaseParticleEntity
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
	public float SpawnRate;
	[NetworkVar("m_vecEndColor")]
	public Vector3 EndColor;
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
}
