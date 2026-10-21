using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBSPDecal")]
public partial class TEBSPDecal(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Entity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(Index)) { Bits = 9, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_nEntity")]
	public int Entity;
	[NetworkVar("m_nIndex")]
	public int Index;
}
