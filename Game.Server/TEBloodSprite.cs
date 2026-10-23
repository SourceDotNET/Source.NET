using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBloodSprite", NoBase = true)]
public partial class TEBloodSprite(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Direction)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(R)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(G)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(B)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(A)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Size)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
	[NetworkVar("r")]
	public int R;
	[NetworkVar("g")]
	public int G;
	[NetworkVar("b")]
	public int B;
	[NetworkVar("a")]
	public int A;
	public int SprayModel;
	public int DropModel;
	[NetworkVar("m_nSize")]
	public int Size;
}
