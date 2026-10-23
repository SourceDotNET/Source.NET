using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("DustTrail", Table = "DT_DustTrail")]
public partial class C_DustTrail : C_BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SpawnRate)) { Bits = 8, Low = 1.0f, High = 1024.0f },
		new(nameof(Color)) { Bits = 8, Low = 0.0f, High = 1.0f },
		new(nameof(ParticleLifetime)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.1f, High = 100.0f },
		new(nameof(StopEmitTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MinSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MaxSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MinDirectedSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MaxDirectedSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EndSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpawnRadius)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Emit),
		new(nameof(Opacity)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_SpawnRate")]
	public float SpawnRate;
	[NetworkVar("m_Color")]
	public Vector3 Color;
	[NetworkVar("m_ParticleLifetime")]
	public float ParticleLifetime;
	[NetworkVar("m_StopEmitTime")]
	public float StopEmitTime;
	[NetworkVar("m_MinSpeed")]
	public float MinSpeed;
	[NetworkVar("m_MaxSpeed")]
	public float MaxSpeed;
	[NetworkVar("m_MinDirectedSpeed")]
	public float MinDirectedSpeed;
	[NetworkVar("m_MaxDirectedSpeed")]
	public float MaxDirectedSpeed;
	[NetworkVar("m_StartSize")]
	public float StartSize;
	[NetworkVar("m_EndSize")]
	public float EndSize;
	[NetworkVar("m_SpawnRadius")]
	public float SpawnRadius;
	[NetworkVar("m_bEmit")]
	public bool Emit;
	[NetworkVar("m_Opacity")]
	public float Opacity;
}
