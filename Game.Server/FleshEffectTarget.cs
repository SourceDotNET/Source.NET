using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("point_flesh_effect_target")]
[NetworkName("CFleshEffectTarget")]
public partial class FleshEffectTarget : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Radius)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ScaleTime)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flRadius")]
	public partial float Radius { get; set; }
	[NetworkVar("m_flScaleTime")]
	public partial float ScaleTime { get; set; }
}
