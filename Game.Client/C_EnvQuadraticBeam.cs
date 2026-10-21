using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CEnvQuadraticBeam", Table = "DT_QuadraticBeam")]
public partial class C_EnvQuadraticBeam : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(TargetPosition)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ControlPosition)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ScrollRate)) { Bits = 8, Low = -4, High = 4 },
		new(nameof(Width)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_targetPosition")]
	public Vector3 TargetPosition;
	[NetworkVar("m_controlPosition")]
	public Vector3 ControlPosition;
	[NetworkVar("m_scrollRate")]
	public float ScrollRate;
	[NetworkVar("m_flWidth")]
	public float Width;
}
