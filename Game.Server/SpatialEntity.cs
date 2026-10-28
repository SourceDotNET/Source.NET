using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CSpatialEntity", NoBase = true)]
public partial class SpatialEntity : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.NoScale, Proxy = typeof(OriginProxy) },
		new(nameof(MinFalloff)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MaxFalloff)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(CurWeight)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Enabled),
	];

	[NetworkVar("m_MinFalloff")]
	public partial float MinFalloff { get; set; }
	[NetworkVar("m_MaxFalloff")]
	public partial float MaxFalloff { get; set; }
	[NetworkVar("m_flCurWeight")]
	public partial float CurWeight { get; set; }
	[NetworkVar("m_bEnabled")]
	public partial bool Enabled { get; set; }
}
