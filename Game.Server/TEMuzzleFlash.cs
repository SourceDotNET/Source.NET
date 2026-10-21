using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEMuzzleFlash")]
public partial class TEMuzzleFlash(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Angles)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Scale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Type)) { Bits = 4, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecAngles")]
	public Vector3 Angles;
	[NetworkVar("m_flScale")]
	public float Scale;
	[NetworkVar("m_nType")]
	public int Type;
}
