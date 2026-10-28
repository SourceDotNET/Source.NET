using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("color_correction_volume")]
[NetworkName("CColorCorrectionVolume", NoBase = true)]
public partial class ColorCorrectionVolume : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(Enabled),
		new(nameof(MaxWeight)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeDuration)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Weight)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(LookupFilename)) { Encoding = NetEncoding.String },
		new(nameof(ModelIndex)) { Encoding = NetEncoding.Int, Bits = 14 },
	];

	[NetworkVar("m_bEnabled")]
	public partial bool Enabled { get; set; }
	[NetworkVar("m_MaxWeight")]
	public partial float MaxWeight { get; set; }
	[NetworkVar("m_FadeDuration")]
	public partial float FadeDuration { get; set; }
	[NetworkVar("m_Weight")]
	public partial float Weight { get; set; }
	[NetworkVar("m_lookupFilename")]
	public partial ref readonly InlineArrayMaxPath<char> LookupFilename { get; }
}
