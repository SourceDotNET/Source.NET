using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamRingPoint")]
public partial class TEBeamRingPoint(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Center)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(LStartRadius)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.0f, High = 4096.0f },
		new(nameof(LEndRadius)) { Bits = 16, Flags = PropFlags.RoundUp, Low = 0.0f, High = 4096.0f },
	];

	[NetworkVar("m_vecCenter")]
	public Vector3 Center;
	[NetworkVar("m_flStartRadius")]
	public float LStartRadius;
	[NetworkVar("m_flEndRadius")]
	public float LEndRadius;
}
