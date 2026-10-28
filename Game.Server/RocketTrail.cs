using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_rockettrail")]
[NetworkName("RocketTrail", Table = "DT_RocketTrail")]
public partial class RocketTrail : BaseParticleEntity
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
	public partial float SpawnRate { get; set; }
	[NetworkVar("m_StartColor")]
	public partial Vector3 StartColor { get; set; }
	[NetworkVar("m_EndColor")]
	public partial Vector3 EndColor { get; set; }
	[NetworkVar("m_ParticleLifetime")]
	public partial float ParticleLifetime { get; set; }
	[NetworkVar("m_StopEmitTime")]
	public partial float StopEmitTime { get; set; }
	[NetworkVar("m_MinSpeed")]
	public partial float MinSpeed { get; set; }
	[NetworkVar("m_MaxSpeed")]
	public partial float MaxSpeed { get; set; }
	[NetworkVar("m_StartSize")]
	public partial float StartSize { get; set; }
	[NetworkVar("m_EndSize")]
	public partial float EndSize { get; set; }
	[NetworkVar("m_SpawnRadius")]
	public partial float SpawnRadius { get; set; }
	[NetworkVar("m_bEmit")]
	public partial bool Emit { get; set; }
	[NetworkVar("m_nAttachment")]
	public partial int Attachment { get; set; }
	[NetworkVar("m_Opacity")]
	public partial float Opacity { get; set; }
	[NetworkVar("m_bDamaged")]
	public partial bool Damaged { get; set; }
	[NetworkVar("m_flFlareScale")]
	public partial float FlareScale { get; set; }
}
