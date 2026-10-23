using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CSlideshowDisplay")]
public partial class C_SlideshowDisplay : C_BaseEntity
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
	public bool Enabled;
	[NetworkVar("m_szDisplayText")]
	public InlineArray128<char> DisplayText;
	[NetworkVar("m_szSlideshowDirectory")]
	public InlineArray128<char> SlideshowDirectory;
	[NetworkVar("m_chCurrentSlideLists")]
	public InlineArray16<byte> ChCurrentSlideLists;
	[NetworkVar("m_fMinSlideTime")]
	public float MinSlideTime;
	[NetworkVar("m_fMaxSlideTime")]
	public float MaxSlideTime;
	[NetworkVar("m_iCycleType")]
	public int CycleType;
	[NetworkVar("m_bNoListRepeats")]
	public bool NoListRepeats;
}
