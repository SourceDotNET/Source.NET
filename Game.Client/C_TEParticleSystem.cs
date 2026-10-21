using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;

[NetworkName("CTEParticleSystem")]
public partial class C_TEParticleSystem : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.VectorElements, Bits = 0, Flags = PropFlags.Coord | PropFlags.NoScale | PropFlags.IsAVectorElem },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
}
