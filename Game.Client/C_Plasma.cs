using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CPlasma")]
public partial class C_Plasma : C_BaseEntity
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
	public float Scale;
	[NetworkVar("m_flScaleTime")]
	public float ScaleTime;
	[NetworkVar("m_nFlags")]
	public int Flags;
	[NetworkVar("m_nPlasmaModelIndex")]
	public int PlasmaModelIndex;
	[NetworkVar("m_nPlasmaModelIndex2")]
	public int PlasmaModelIndex2;
	[NetworkVar("m_nGlowModelIndex")]
	public int GlowModelIndex;
}
