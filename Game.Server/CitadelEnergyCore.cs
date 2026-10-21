using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_citadel_energy_core")]
[NetworkName("CCitadelEnergyCore")]
public partial class CitadelEnergyCore : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Scale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(State)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Duration)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Spawnflags)) { Bits = 32, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_flScale")]
	public partial float Scale { get; set; }
	[NetworkVar("m_nState")]
	public partial int State { get; set; }
	[NetworkVar("m_flDuration")]
	public partial float Duration { get; set; }
	[NetworkVar("m_flStartTime")]
	public partial float StartTime { get; set; }
	[NetworkVar("m_spawnflags")]
	public partial int Spawnflags { get; set; }
}
