using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;

[NetworkName("CTEShatterSurface")]
public partial class TEShatterSurface(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Origin)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Angles)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Force)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(ForcePos)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Width)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Height)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ShardSize)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SurfaceType)) { Bits = 2, Flags = PropFlags.Unsigned },
		new(nameof(UchFrontColor)) { Encoding = NetEncoding.Elements, Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(UchBackColor)) { Encoding = NetEncoding.Elements, Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_vecOrigin")]
	public Vector3 Origin;
	[NetworkVar("m_vecAngles")]
	public Vector3 Angles;
	[NetworkVar("m_vecForce")]
	public Vector3 Force;
	[NetworkVar("m_vecForcePos")]
	public Vector3 ForcePos;
	[NetworkVar("m_flWidth")]
	public float Width;
	[NetworkVar("m_flHeight")]
	public float Height;
	[NetworkVar("m_flShardSize")]
	public float ShardSize;
	[NetworkVar("m_nSurfaceType")]
	public int SurfaceType;
	[NetworkVar("m_uchFrontColor")]
	public InlineArray3<byte> UchFrontColor;
	[NetworkVar("m_uchBackColor")]
	public InlineArray3<byte> UchBackColor;
}
