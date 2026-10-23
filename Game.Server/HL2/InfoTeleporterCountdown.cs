using Game.Shared;

using Source;
using Source.Common;

namespace Game.Server.HL2;
[LinkEntityToClass("info_teleporter_countdown")]
[NetworkName("CInfoTeleporterCountdown")]
public partial class InfoTeleporterCountdown : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(CountdownStarted),
		nameof(Disabled),
		new(nameof(StartTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(TimeRemaining)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_bCountdownStarted")]
	public partial bool CountdownStarted { get; set; }
	[NetworkVar("m_bDisabled")]
	public partial bool Disabled { get; set; }
	[NetworkVar("m_flStartTime")]
	public partial TimeUnit_t StartTime { get; set; }
	[NetworkVar("m_flTimeRemaining")]
	public partial TimeUnit_t TimeRemaining { get; set; }
}
