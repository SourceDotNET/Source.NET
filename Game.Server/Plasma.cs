using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("_plasma")]
[NetworkName("CPlasma")]
public partial class Plasma : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Scale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ScaleTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Flags)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(PlasmaModelIndex)) { Bits = 14 },
		new(nameof(PlasmaModelIndex2)) { Bits = 14 },
		new(nameof(GlowModelIndex)) { Bits = 14 },
	];

	[NetworkVar("m_flScale")]
	public partial float Scale { get; set; }
	[NetworkVar("m_flScaleTime")]
	public partial float ScaleTime { get; set; }
	[NetworkVar("m_nFlags")]
	public partial int Flags { get; set; }
	[NetworkVar("m_nPlasmaModelIndex")]
	public partial int PlasmaModelIndex { get; set; }
	[NetworkVar("m_nPlasmaModelIndex2")]
	public partial int PlasmaModelIndex2 { get; set; }
	[NetworkVar("m_nGlowModelIndex")]
	public partial int GlowModelIndex { get; set; }
}
