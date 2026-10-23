using Source.Common;
using Source;

using Game.Shared;

namespace Game.Server;


[NetworkName("CBaseHelicopter")]
public partial class BaseHelicopter : AI_TrackPather
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartupTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flStartupTime")]
	public partial TimeUnit_t StartupTime { get; set; }
}
