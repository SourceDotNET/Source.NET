using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("mortarshell")]
[NetworkName("CMortarShell")]
public partial class MortarShell : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Lifespan)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Radius)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SurfaceNormal)) { Bits = 0, Flags = PropFlags.VarInt | PropFlags.VarInt },
	];

	[NetworkVar("m_flLifespan")]
	public partial float Lifespan { get; set; }
	[NetworkVar("m_flRadius")]
	public partial float Radius { get; set; }
	[NetworkVar("m_vecSurfaceNormal")]
	public partial Vector3 SurfaceNormal { get; set; }
}
