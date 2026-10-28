using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTESpriteSpray")]
public partial class TESpriteSpray(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Direction)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Noise)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0.0f, High = 2.56f },
		new(nameof(Speed)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Count)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecDirection")]
	public Vector3 Direction;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_fNoise")]
	public float Noise;
	[NetworkVar("m_nSpeed")]
	public int Speed;
	[NetworkVar("m_nCount")]
	public int Count;
}
