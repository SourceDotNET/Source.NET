using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTELargeFunnel")]
public partial class TELargeFunnel(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Reversed)) { Bits = 2, Flags = PropFlags.Unsigned },
	];

	public int ModelIndex;
	[NetworkVar("m_nReversed")]
	public int Reversed;
}
