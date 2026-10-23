using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBeamSpline", NoBase = true)]
public partial class TEBeamSpline(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(NumPoints)) { Bits = 5, Flags = PropFlags.Unsigned },
		new(nameof(Points)) { Encoding = NetEncoding.Array, Bits = -1, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_nPoints")]
	public int NumPoints;
	[NetworkVar("m_vecPoints")]
	public InlineArrayMaxSplinePoints<Vector3> Points;
}
