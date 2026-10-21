using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEProjectedDecal")]
public partial class TEProjectedDecal(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Rotation)) { Encoding = NetEncoding.QAngles, Bits = 10, Flags = PropFlags.RoundDown },
		new(nameof(LDistance)) { Bits = 10, Flags = PropFlags.RoundUp },
		new(nameof(Index)) { Bits = 9, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_angRotation")]
	public Vector3 Rotation;
	[NetworkVar("m_flDistance")]
	public float LDistance;
	[NetworkVar("m_nIndex")]
	public int Index;
}
