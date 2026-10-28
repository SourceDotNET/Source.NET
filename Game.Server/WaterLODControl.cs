using Source.Common;
using Source;

using Game.Shared;

namespace Game.Server;

[LinkEntityToClass("water_lod_control")]
[NetworkName("CWaterLODControl", NoBase = true)]
public partial class WaterLODControl : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(CheapWaterStartDistance)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(CheapWaterEndDistance)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flCheapWaterStartDistance")]
	public partial float CheapWaterStartDistance { get; set; }
	[NetworkVar("m_flCheapWaterEndDistance")]
	public partial float CheapWaterEndDistance { get; set; }
}
