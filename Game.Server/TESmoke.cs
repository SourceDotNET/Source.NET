using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTESmoke")]
public partial class TESmoke(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Scale)) { Bits = 8, Flags = PropFlags.RoundDown, Low = 0.0f, High = 25.6f },
		new(nameof(FrameRate)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_fScale")]
	public float Scale;
	[NetworkVar("m_nFrameRate")]
	public int FrameRate;
}
