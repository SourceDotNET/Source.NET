using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEGaussExplosion")]
public partial class C_TEGaussExplosion : C_TEParticleSystem
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Type)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(Direction)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_nType")]
	public int Type;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
}
