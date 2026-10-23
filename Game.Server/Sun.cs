using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;

namespace Game.Server;

[LinkEntityToClass("env_sun")]
[NetworkName("CSun", NoBase = true)]
public partial class Sun : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Render)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned, Proxy = typeof(Color32Proxy) },
		new(nameof(Overlay)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned, Proxy = typeof(Color32Proxy) },
		new(nameof(Direction)) { Bits = 0, Flags = PropFlags.Normal },
		new(nameof(On)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(Size)) { Bits = 10, Flags = PropFlags.Unsigned },
		new(nameof(OverlaySize)) { Bits = 10, Flags = PropFlags.Unsigned },
		new(nameof(Material)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(OverlayMaterial)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(HDRColorScale)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 100.0f },
	];

	[NetworkVar("m_clrRender")]
	public partial Color Render { get; set; }
	[NetworkVar("m_clrOverlay")]
	public partial Color Overlay { get; set; }
	[NetworkVar("m_vDirection")]
	public partial Vector3 Direction { get; set; }
	[NetworkVar("m_bOn")]
	public partial bool On { get; set; }
	[NetworkVar("m_nSize")]
	public partial int Size { get; set; }
	[NetworkVar("m_nOverlaySize")]
	public partial int OverlaySize { get; set; }
	[NetworkVar("m_nMaterial")]
	public partial int Material { get; set; }
	[NetworkVar("m_nOverlayMaterial")]
	public partial int OverlayMaterial { get; set; }
	[NetworkVar("HDRColorScale")]
	public partial int HDRColorScale { get; set; }
}
