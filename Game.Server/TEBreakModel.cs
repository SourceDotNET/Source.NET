using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Mathematics;

using System.Numerics;
namespace Game.Server;
[NetworkName("CTEBreakModel")]
public partial class TEBreakModel(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Rotation)) { Encoding = NetEncoding.VectorElements, Bits = 13, Flags = PropFlags.RoundDown },
		new(nameof(Size)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Velocity)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Randomization)) { Bits = 9, Flags = PropFlags.Unsigned },
		new(nameof(Count)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(Time)) { Bits = 10, Low = 0.0f, High = 102.4f },
		new(nameof(Flags)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_angRotation")]
	public QAngle Rotation;
	[NetworkVar("m_vecSize")]
	public Vector3 Size;
	[NetworkVar("m_vecVelocity")]
	public Vector3 Velocity;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_nRandomization")]
	public int Randomization;
	[NetworkVar("m_nCount")]
	public int Count;
	[NetworkVar("m_fTime")]
	public float Time;
	[NetworkVar("m_nFlags")]
	public int Flags;
}
