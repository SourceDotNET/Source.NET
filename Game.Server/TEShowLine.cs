using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEShowLine")]
public partial class TEShowLine(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(End)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_vecEnd")]
	public Vector3 End;
}
