using Game.Shared;

using Source.Common;

namespace Game.Client.HL2;

[NetworkName("CAlyxEmpEffect")]
public partial class C_AlyxEmpEffect : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(State)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Duration)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(StartTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_nState")]
	public int State;
	[NetworkVar("m_flDuration")]
	public TimeUnit_t Duration;
	[NetworkVar("m_flStartTime")]
	public TimeUnit_t StartTime;
}
