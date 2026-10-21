using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEDynamicLight")]
public partial class TEDynamicLight(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(R)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(G)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(B)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Exponent)) { Bits = 8 },
		new(nameof(Radius)) { Bits = 8, Flags = PropFlags.RoundUp, Low = 0, High = 2560.0f },
		new(nameof(Time)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0, High = 25.6f },
		new(nameof(Decay)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0, High = 2560.0f },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("r")]
	public int R;
	[NetworkVar("g")]
	public int G;
	[NetworkVar("b")]
	public int B;
	[NetworkVar("exponent")]
	public int Exponent;
	[NetworkVar("m_fRadius")]
	public float Radius;
	[NetworkVar("m_fTime")]
	public float Time;
	[NetworkVar("m_fDecay")]
	public float Decay;
}
