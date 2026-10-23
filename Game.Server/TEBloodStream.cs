using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBloodStream")]
public partial class TEBloodStream(ReadOnlySpan<char> name) : TEParticleSystem(name)
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Direction)) { Bits = 11, Low = -10.0f, High = 10.0f },
		new(nameof(R)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(G)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(B)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(A)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Amount)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

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
	[NetworkVar("m_nAmount")]
	public int Amount;
}
