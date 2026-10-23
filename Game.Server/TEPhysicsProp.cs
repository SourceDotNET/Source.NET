using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
using Source.Common.Mathematics;
namespace Game.Server;
[NetworkName("CTEPhysicsProp")]
public partial class TEPhysicsProp(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Rotation)) { Encoding = NetEncoding.VectorElements, Bits = 13, Flags = PropFlags.RoundDown | PropFlags.IsAVectorElem },
		new(nameof(Velocity)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ModelIndex)) { Bits = 14 },
		new(nameof(Skin)) { Bits = 10 },
		new(nameof(Flags)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(Effects)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(ClrRender)) { Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(ModelScale)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_angRotation")]
	public QAngle Rotation;
	[NetworkVar("m_vecVelocity")]
	public Vector3 Velocity;
	[NetworkVar("m_nModelIndex")]
	public int ModelIndex;
	[NetworkVar("m_nSkin")]
	public int Skin;
	[NetworkVar("m_nFlags")]
	public int Flags;
	[NetworkVar("m_nEffects")]
	public int Effects;
	[NetworkVar("m_clrRender")]
	public int ClrRender;
	[NetworkVar("m_fModelScale")]
	public float ModelScale;
}
