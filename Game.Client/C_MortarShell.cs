using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CMortarShell")]
public partial class C_MortarShell : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Lifespan)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Radius)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SurfaceNormal)) { Bits = 0, Flags = PropFlags.VarInt | PropFlags.VarInt },
	];

	[NetworkVar("m_flLifespan")]
	public float Lifespan;
	[NetworkVar("m_flRadius")]
	public float Radius;
	[NetworkVar("m_vecSurfaceNormal")]
	public Vector3 SurfaceNormal;
}
