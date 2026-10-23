using Source.Common;
using Source;

using Game.Shared;
using System.Numerics;
using Source.Common.MaterialSystem;

namespace Game.Server;


[LinkEntityToClass("env_lightglow")]
[NetworkName("CLightGlow", NoBase = true)]
public partial class LightGlow : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(RenderColor)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(HorizontalSize)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(VerticalSize)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(MinDist)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(MaxDist)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(OuterMaxDist)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(SpawnFlags)) { Encoding = NetEncoding.Int, Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Origin)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Rotation)) { Encoding = NetEncoding.QAngles, Bits = 13, Flags = PropFlags.RoundDown, Proxy = typeof(AnglesProxy) },
		new(nameof(MoveParent)) { Encoding = NetEncoding.EHandle },
		new(nameof(GlowProxySize)) { Bits = 6, Flags = PropFlags.RoundUp, Low = 0.0f, High = 64.0f },
		new(nameof(HDRColorScale)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_clrRender")]
	public partial Color RenderColor { get; set; }
	[NetworkVar("m_nHorizontalSize")]
	public partial int HorizontalSize { get; set; }
	[NetworkVar("m_nVerticalSize")]
	public partial int VerticalSize { get; set; }
	[NetworkVar("m_nMinDist")]
	public partial int MinDist { get; set; }
	[NetworkVar("m_nMaxDist")]
	public partial int MaxDist { get; set; }
	[NetworkVar("m_nOuterMaxDist")]
	public partial int OuterMaxDist { get; set; }
	[NetworkVar("m_flGlowProxySize")]
	public partial float GlowProxySize { get; set; }
	[NetworkVar("HDRColorScale")]
	public partial float HDRColorScale { get; set; }
}
