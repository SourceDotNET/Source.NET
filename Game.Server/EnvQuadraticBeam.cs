using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_quadraticbeam")]
[NetworkName("CEnvQuadraticBeam", Table = "DT_QuadraticBeam")]
public partial class EnvQuadraticBeam : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(TargetPosition)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ControlPosition)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ScrollRate)) { Bits = 8, Low = -4, High = 4 },
		new(nameof(Width)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_targetPosition")]
	public partial Vector3 TargetPosition { get; set; }
	[NetworkVar("m_controlPosition")]
	public partial Vector3 ControlPosition { get; set; }
	[NetworkVar("m_scrollRate")]
	public partial float ScrollRate { get; set; }
	[NetworkVar("m_flWidth")]
	public partial float Width { get; set; }
}
