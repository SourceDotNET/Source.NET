using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_detail_controller")]
[NetworkName("CEnvDetailController", Table = "DT_DetailController", NoBase = true)]
public partial class EnvDetailController : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(FadeStartDist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeEndDist)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flFadeStartDist")]
	public partial float FadeStartDist { get; set; }
	[NetworkVar("m_flFadeEndDist")]
	public partial float FadeEndDist { get; set; }
}
