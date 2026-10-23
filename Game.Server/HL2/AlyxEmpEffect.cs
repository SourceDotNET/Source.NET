using Game.Shared;

using Source.Common;

namespace Game.Server.HL2;
[LinkEntityToClass("env_alyxemp")]
[NetworkName("CAlyxEmpEffect")]
public partial class AlyxEmpEffect : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(State)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Duration)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_nState")]
	public partial int State { get; set; }
	[NetworkVar("m_flDuration")]
	public partial TimeUnit_t Duration { get; set; }
	[NetworkVar("m_flStartTime")]
	public partial TimeUnit_t StartTime { get; set; }
}
