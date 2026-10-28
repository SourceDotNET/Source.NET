using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("vgui_slideshow_display")]
[NetworkName("CSlideshowDisplay")]
public partial class SlideshowDisplay : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Enabled),
		new(nameof(DisplayText)) { Encoding = NetEncoding.String },
		new(nameof(SlideshowDirectory)) { Encoding = NetEncoding.String },
		new(nameof(ChCurrentSlideLists)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(MinSlideTime)) { Bits = 11, Low = 0.0f, High = 20.0f },
		new(nameof(MaxSlideTime)) { Bits = 11, Low = 0.0f, High = 20.0f },
		new(nameof(CycleType)) { Bits = 2, Flags = PropFlags.Unsigned },
		nameof(NoListRepeats),
	];

	[NetworkVar("m_bEnabled")]
	public partial bool Enabled { get; set; }
	[NetworkVar("m_szDisplayText")]
	public partial ref readonly InlineArray128<char> DisplayText { get; }
	[NetworkVar("m_szSlideshowDirectory")]
	public partial ref readonly InlineArray128<char> SlideshowDirectory { get; }
	[NetworkVar("m_chCurrentSlideLists")]
	public partial ref readonly InlineArray16<byte> ChCurrentSlideLists { get; }
	[NetworkVar("m_fMinSlideTime")]
	public partial float MinSlideTime { get; set; }
	[NetworkVar("m_fMaxSlideTime")]
	public partial float MaxSlideTime { get; set; }
	[NetworkVar("m_iCycleType")]
	public partial int CycleType { get; set; }
	[NetworkVar("m_bNoListRepeats")]
	public partial bool NoListRepeats { get; set; }
}
