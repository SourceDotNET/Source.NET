using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[LinkEntityToClass("point_worldtext")]
[NetworkName("CPointWorldText")]
public partial class C_PointWorldText : C_BaseEntity
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
	public InlineArray512<char> SzText;
	[NetworkVar("m_colTextColor")]
	public int ColTextColor;
	[NetworkVar("m_flTextSize")]
	public float TextSize;
	[NetworkVar("m_flTextSpacingX")]
	public float TextSpacingX;
	[NetworkVar("m_flTextSpacingY")]
	public float TextSpacingY;
	[NetworkVar("m_nOrientation")]
	public int Orientation;
	[NetworkVar("m_bRainbow")]
	public bool Rainbow;
	[NetworkVar("m_bTextEnabled")]
	public bool TextEnabled;
}
