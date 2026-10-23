using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("prop_coreball")]
[LinkEntityToClass("prop_scalable")]
[NetworkName("CPropScalable")]
public partial class PropScalable : BaseAnimating
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ScaleX)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ScaleY)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ScaleZ)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(LerpTimeX)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(LerpTimeY)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(LerpTimeZ)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(GoalTimeX)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(GoalTimeY)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(GoalTimeZ)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flScaleX")]
	public partial float ScaleX { get; set; }
	[NetworkVar("m_flScaleY")]
	public partial float ScaleY { get; set; }
	[NetworkVar("m_flScaleZ")]
	public partial float ScaleZ { get; set; }
	[NetworkVar("m_flLerpTimeX")]
	public partial float LerpTimeX { get; set; }
	[NetworkVar("m_flLerpTimeY")]
	public partial float LerpTimeY { get; set; }
	[NetworkVar("m_flLerpTimeZ")]
	public partial float LerpTimeZ { get; set; }
	[NetworkVar("m_flGoalTimeX")]
	public partial float GoalTimeX { get; set; }
	[NetworkVar("m_flGoalTimeY")]
	public partial float GoalTimeY { get; set; }
	[NetworkVar("m_flGoalTimeZ")]
	public partial float GoalTimeZ { get; set; }
}
