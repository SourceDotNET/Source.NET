using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("point_worldtext")]
[NetworkName("CPointWorldText")]
public partial class PointWorldText : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(SzText)) { Encoding = NetEncoding.String },
		new(nameof(ColTextColor)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(TextSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(TextSpacingX)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(TextSpacingY)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Orientation)) { Bits = 3, Flags = PropFlags.Unsigned },
		nameof(Rainbow),
		nameof(TextEnabled),
	];

	[NetworkVar("m_szText")]
	public partial ref readonly InlineArray512<char> SzText { get; }
	[NetworkVar("m_colTextColor")]
	public partial int ColTextColor { get; set; }
	[NetworkVar("m_flTextSize")]
	public partial float TextSize { get; set; }
	[NetworkVar("m_flTextSpacingX")]
	public partial float TextSpacingX { get; set; }
	[NetworkVar("m_flTextSpacingY")]
	public partial float TextSpacingY { get; set; }
	[NetworkVar("m_nOrientation")]
	public partial int Orientation { get; set; }
	[NetworkVar("m_bRainbow")]
	public partial bool Rainbow { get; set; }
	[NetworkVar("m_bTextEnabled")]
	public partial bool TextEnabled { get; set; }
}
