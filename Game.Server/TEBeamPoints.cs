using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamPoints")]
public partial class TEBeamPoints(ReadOnlySpan<char> name) : BaseBeam(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartPoint)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(EndPoint)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_vecStartPoint")]
	public Vector3 StartPoint;
	[NetworkVar("m_vecEndPoint")]
	public Vector3 EndPoint;
}
