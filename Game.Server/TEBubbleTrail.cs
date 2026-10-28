using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBubbleTrail")]
public partial class TEBubbleTrail(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Mins)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Maxs)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(LWaterZ)) { Bits = 17, Low = MIN_COORD_INTEGER, High = MAX_COORD_INTEGER },
		new(nameof(Count)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Speed)) { Bits = 17, Low = MIN_COORD_INTEGER, High = MAX_COORD_INTEGER },
	];

	[NetworkVar("m_vecMins")]
	public Vector3 Mins;
	[NetworkVar("m_vecMaxs")]
	public Vector3 Maxs;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_flWaterZ")]
	public float LWaterZ;
	[NetworkVar("m_nCount")]
	public int Count;
	[NetworkVar("m_fSpeed")]
	public float Speed;
}
