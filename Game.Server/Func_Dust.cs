using Source.Common;
using Source;

using Game.Shared;

namespace Game.Server;



[NetworkName("CFunc_Dust", NoBase = true)]
public partial class Func_Dust : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Color)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(SpawnRate)) { Bits = 12, Flags = PropFlags.Unsigned },
		new(nameof(SpeedMax)) { Bits = 12, Flags = PropFlags.Unsigned },
		new(nameof(SizeMin)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(SizeMax)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(DistMax)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(LifetimeMin)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(LifetimeMax)) { Bits = 4, Flags = PropFlags.Unsigned },
		new(nameof(DustFlags)) { Bits = 3, Flags = PropFlags.Unsigned },
		new(nameof(ModelIndex)) { Encoding = NetEncoding.ModelIndex },
		new(nameof(FallSpeed)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(AffectedByWind),
		new(nameof(Collision)) { Encoding = NetEncoding.DataTable, DataTable = "DT_CollisionProperty" },
	];

	[NetworkVar("m_Color")]
	public partial Color Color { get; set; }
	[NetworkVar("m_SpawnRate")]
	public partial int SpawnRate { get; set; }
	[NetworkVar("m_SpeedMax")]
	public partial int SpeedMax { get; set; }
	[NetworkVar("m_flSizeMin")]
	public partial float SizeMin { get; set; }
	[NetworkVar("m_flSizeMax")]
	public partial float SizeMax { get; set; }
	[NetworkVar("m_DistMax")]
	public partial int DistMax { get; set; }
	[NetworkVar("m_LifetimeMin")]
	public partial int LifetimeMin { get; set; }
	[NetworkVar("m_LifetimeMax")]
	public partial int LifetimeMax { get; set; }
	[NetworkVar("m_DustFlags")]
	public partial int DustFlags { get; set; }
	[NetworkVar("m_FallSpeed")]
	public partial float FallSpeed { get; set; }
	[NetworkVar("m_bAffectedByWind")]
	public partial bool AffectedByWind { get; set; }
}
