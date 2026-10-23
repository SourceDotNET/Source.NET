using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEEnergySplash", NoBase = true)]
public partial class TEEnergySplash(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Pos)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Dir)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Explosive)) { Bits = 1, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecPos")]
	public Vector3 Pos;
	[NetworkVar("m_vecDir")]
	public Vector3 Dir;
	[NetworkVar("m_bExplosive")]
	public int Explosive;
}
