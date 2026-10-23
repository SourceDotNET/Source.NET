using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CCitadelEnergyCore")]
public partial class C_CitadelEnergyCore : C_BaseEntity
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
	public float Scale;
	[NetworkVar("m_nState")]
	public int State;
	[NetworkVar("m_flDuration")]
	public float Duration;
	[NetworkVar("m_flStartTime")]
	public float StartTime;
	[NetworkVar("m_spawnflags")]
	public int Spawnflags;
}
