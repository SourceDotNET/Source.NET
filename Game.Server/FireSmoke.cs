using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("_firesmoke")]
[NetworkName("CFireSmoke")]
public partial class FireSmoke : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Scale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(ScaleTime)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Flags)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(FlameModelIndex)) { Bits = 14 },
		new(nameof(FlameFromAboveModelIndex)) { Bits = 14 },
	];

	[NetworkVar("m_flStartScale")]
	public partial float StartScale { get; set; }
	[NetworkVar("m_flScale")]
	public partial float Scale { get; set; }
	[NetworkVar("m_flScaleTime")]
	public partial float ScaleTime { get; set; }
	[NetworkVar("m_nFlags")]
	public partial int Flags { get; set; }
	[NetworkVar("m_nFlameModelIndex")]
	public partial int FlameModelIndex { get; set; }
	[NetworkVar("m_nFlameFromAboveModelIndex")]
	public partial int FlameFromAboveModelIndex { get; set; }
}
