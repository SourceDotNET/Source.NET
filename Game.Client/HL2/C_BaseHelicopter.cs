using Source.Common;
using Source;

using Game.Shared;

namespace Game.Client;


[NetworkName("CBaseHelicopter")]
public partial class C_BaseHelicopter : C_AI_TrackPather
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(StartupTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flStartupTime")]
	public TimeUnit_t StartupTime;
}
