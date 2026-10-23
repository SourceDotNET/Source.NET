using Source.Common;
using Source;

using Game.Shared;

using System.Numerics;
namespace Game.Client;

[NetworkName("CBreakableSurface")]
public partial class C_BreakableSurface : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(NumWide)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(NumHigh)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(PanelWidth)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(PanelHeight)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(VNormal)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(VCorner)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(IsBroken)) { Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(SurfaceType)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(RawPanelBitVec)) { Bits = 1, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nNumWide")]
	public int NumWide;
	[NetworkVar("m_nNumHigh")]
	public int NumHigh;
	[NetworkVar("m_flPanelWidth")]
	public float PanelWidth;
	[NetworkVar("m_flPanelHeight")]
	public float PanelHeight;
	[NetworkVar("m_vNormal")]
	public Vector3 VNormal;
	[NetworkVar("m_vCorner")]
	public Vector3 VCorner;
	[NetworkVar("m_bIsBroken")]
	public int IsBroken;
	[NetworkVar("m_nSurfaceType")]
	public int SurfaceType;
	[NetworkVar("m_RawPanelBitVec")]
	public InlineArray256<int> RawPanelBitVec;
}
