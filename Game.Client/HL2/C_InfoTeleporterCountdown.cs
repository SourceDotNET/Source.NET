using Game.Shared;

using Source;
using Source.Common;

namespace Game.Client.HL2;

[NetworkName("CInfoTeleporterCountdown")]
public partial class C_InfoTeleporterCountdown : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(CountdownStarted),
		nameof(Disabled),
		new(nameof(StartTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(TimeRemaining)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_bCountdownStarted")]
	public bool CountdownStarted;
	[NetworkVar("m_bDisabled")]
	public bool Disabled;
	[NetworkVar("m_flStartTime")]
	public TimeUnit_t StartTime;
	[NetworkVar("m_flTimeRemaining")]
	public TimeUnit_t TimeRemaining;
}
