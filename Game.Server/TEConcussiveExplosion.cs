using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEConcussiveExplosion")]
public partial class TEConcussiveExplosion(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Normal)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Scale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Radius)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Magnitude)) { Bits = 32, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecNormal")]
	public Vector3 Normal;
	[NetworkVar("m_flScale")]
	public float Scale;
	[NetworkVar("m_nRadius")]
	public int Radius;
	[NetworkVar("m_nMagnitude")]
	public int Magnitude;
}
