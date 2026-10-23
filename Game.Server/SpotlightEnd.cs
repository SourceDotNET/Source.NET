using Source.Common;
using Source;

using Game.Shared;

namespace Game.Server;



[LinkEntityToClass("spotlight_end")]
[NetworkName("CSpotlightEnd")]
public partial class SpotlightEnd : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(LightScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Radius)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flLightScale")]
	public partial float LightScale { get; set; }
	[NetworkVar("m_Radius")]
	public partial float Radius { get; set; }
}
