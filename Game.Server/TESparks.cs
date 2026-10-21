using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTESparks")]
public partial class TESparks(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Magnitude)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(TrailLength)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(Dir)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_nMagnitude")]
	public int Magnitude;
	[NetworkVar("m_nTrailLength")]
	public int TrailLength;
	[NetworkVar("m_vecDir")]
	public Vector3 Dir;
}
