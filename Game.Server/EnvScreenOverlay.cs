using Game.Shared;
using Source.Common;
using Source;

namespace Game.Server;

[LinkEntityToClass("env_screenoverlay")]
[NetworkName("CEnvScreenOverlay")]
public partial class EnvScreenOverlay : BaseEntity
{
	public const int MAX_SCREEN_OVERLAYS = 10;

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(OverlayNames)) { Encoding = NetEncoding.Array },
		new(nameof(OverlayTimes)) { Encoding = NetEncoding.Array, Bits = 11, Flags = PropFlags.RoundDown, Low = -1.0f, High = 63.0f },
		new(nameof(StartTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DesiredOverlay)) { Bits = 5 },
		nameof(IsActive),
	];

	[NetworkVar("m_iszOverlayNames")]
	public partial ref readonly InlineArray10<InlineArray512<char>> OverlayNames { get; }
	[NetworkVar("m_flOverlayTimes")]
	public partial ref readonly InlineArray10<float> OverlayTimes { get; }
	[NetworkVar("m_flStartTime")]
	public partial float StartTime { get; set; }
	[NetworkVar("m_iDesiredOverlay")]
	public partial int DesiredOverlay { get; set; }
	[NetworkVar("m_bIsActive")]
	public partial bool IsActive { get; set; }
}
