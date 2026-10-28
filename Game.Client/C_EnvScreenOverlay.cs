using Source.Common;
using Source;

namespace Game.Client;

[NetworkName("CEnvScreenOverlay")]
public partial class C_EnvScreenOverlay : C_BaseEntity
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
	public InlineArray10<InlineArray512<char>> OverlayNames;
	[NetworkVar("m_flOverlayTimes")]
	public InlineArray10<float> OverlayTimes;
	[NetworkVar("m_flStartTime")]
	public float StartTime;
	[NetworkVar("m_iDesiredOverlay")]
	public int DesiredOverlay;
	[NetworkVar("m_bIsActive")]
	public bool IsActive;
}
