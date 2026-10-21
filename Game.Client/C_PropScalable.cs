using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CPropScalable")]
public partial class C_PropScalable : C_BaseAnimating
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

	// todo: RecvProxy_ScaleX
	[NetworkVar("m_flScaleX")]
	public float ScaleX;
	// todo: RecvProxy_ScaleY
	[NetworkVar("m_flScaleY")]
	public float ScaleY;
	// todo: RecvProxy_ScaleZ
	[NetworkVar("m_flScaleZ")]
	public float ScaleZ;
	[NetworkVar("m_flLerpTimeX")]
	public float LerpTimeX;
	[NetworkVar("m_flLerpTimeY")]
	public float LerpTimeY;
	[NetworkVar("m_flLerpTimeZ")]
	public float LerpTimeZ;
	[NetworkVar("m_flGoalTimeX")]
	public float GoalTimeX;
	[NetworkVar("m_flGoalTimeY")]
	public float GoalTimeY;
	[NetworkVar("m_flGoalTimeZ")]
	public float GoalTimeZ;
}
