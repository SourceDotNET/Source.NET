using Game.Client;
using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;

namespace Game.Server;

[NetworkName("CSun", NoBase = true)]
public partial class C_Sun : C_BaseEntity
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
	public Color Render;
	[NetworkVar("m_clrOverlay")]
	public Color Overlay;
	[NetworkVar("m_vDirection")]
	public Vector3 Direction;
	[NetworkVar("m_bOn")]
	public bool On;
	[NetworkVar("m_nSize")]
	public int Size;
	[NetworkVar("m_nOverlaySize")]
	public int OverlaySize;
	[NetworkVar("m_nMaterial")]
	public int Material;
	[NetworkVar("m_nOverlayMaterial")]
	public int OverlayMaterial;
	// todo: RecvProxy_HDRColorScale
	[NetworkVar("HDRColorScale")]
	public int HDRColorScale;
}