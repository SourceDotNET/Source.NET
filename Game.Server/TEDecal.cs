using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEDecal")]
public partial class TEDecal(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Start)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Entity)) { Bits = 13, Flags = PropFlags.Unsigned },
		new(nameof(Hitbox)) { Bits = 12, Flags = PropFlags.Unsigned },
		new(nameof(Index)) { Bits = 9, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecStart")]
	public Vector3 Start;
	[NetworkVar("m_nEntity")]
	public int Entity;
	[NetworkVar("m_nHitbox")]
	public int Hitbox;
	[NetworkVar("m_nIndex")]
	public int Index;
}
