using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;

[NetworkName("CTEParticleSystem")]
public partial class TEParticleSystem(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.VectorElements, Bits = 0, Flags = PropFlags.Coord | PropFlags.NoScale },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
}
