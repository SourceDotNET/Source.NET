using Source.Common;
using Source;

using Game.Shared;

using System.Numerics;
namespace Game.Server;

[LinkEntityToClass("func_breakable_surf")]
[NetworkName("CBreakableSurface")]
public partial class BreakableSurface : BaseEntity
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
	public partial int NumWide { get; set; }
	[NetworkVar("m_nNumHigh")]
	public partial int NumHigh { get; set; }
	[NetworkVar("m_flPanelWidth")]
	public partial float PanelWidth { get; set; }
	[NetworkVar("m_flPanelHeight")]
	public partial float PanelHeight { get; set; }
	[NetworkVar("m_vNormal")]
	public partial Vector3 VNormal { get; set; }
	[NetworkVar("m_vCorner")]
	public partial Vector3 VCorner { get; set; }
	[NetworkVar("m_bIsBroken")]
	public partial int IsBroken { get; set; }
	[NetworkVar("m_nSurfaceType")]
	public partial int SurfaceType { get; set; }
	[NetworkVar("m_RawPanelBitVec")]
	public partial ref readonly InlineArray256<int> RawPanelBitVec { get; }
}
