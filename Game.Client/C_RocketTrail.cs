using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("RocketTrail", Table = "DT_RocketTrail")]
public partial class C_RocketTrail : C_BaseParticleEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SpawnRate)) { Bits = 8, Low = 1.0f, High = 1024.0f },
		new(nameof(StartColor)) { Bits = 8, Low = 0.0f, High = 1.0f },
		new(nameof(EndColor)) { Bits = 8, Low = 0.0f, High = 1.0f },
		new(nameof(ParticleLifetime)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.1f, High = 100.0f },
		new(nameof(StopEmitTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MinSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MaxSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EndSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SpawnRadius)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Emit),
		new(nameof(Attachment)) { Bits = 12 },
		new(nameof(Opacity)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Damaged),
		new(nameof(FlareScale)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_SpawnRate")]
	public float SpawnRate;
	[NetworkVar("m_StartColor")]
	public Vector3 StartColor;
	[NetworkVar("m_EndColor")]
	public Vector3 EndColor;
	[NetworkVar("m_ParticleLifetime")]
	public float ParticleLifetime;
	[NetworkVar("m_StopEmitTime")]
	public float StopEmitTime;
	[NetworkVar("m_MinSpeed")]
	public float MinSpeed;
	[NetworkVar("m_MaxSpeed")]
	public float MaxSpeed;
	[NetworkVar("m_StartSize")]
	public float StartSize;
	[NetworkVar("m_EndSize")]
	public float EndSize;
	[NetworkVar("m_SpawnRadius")]
	public float SpawnRadius;
	[NetworkVar("m_bEmit")]
	public bool Emit;
	[NetworkVar("m_nAttachment")]
	public int Attachment;
	[NetworkVar("m_Opacity")]
	public float Opacity;
	[NetworkVar("m_bDamaged")]
	public bool Damaged;
	[NetworkVar("m_flFlareScale")]
	public float FlareScale;
}
