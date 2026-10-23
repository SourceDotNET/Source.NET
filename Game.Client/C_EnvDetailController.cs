using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[LinkEntityToClass("env_detail_controller")]
[NetworkName("CEnvDetailController", Table = "DT_DetailController", NoBase = true)]
public partial class C_EnvDetailController : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(FadeStartDist)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(FadeEndDist)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flFadeStartDist")]
	public float FadeStartDist;
	[NetworkVar("m_flFadeEndDist")]
	public float FadeEndDist;

	static C_EnvDetailController? s_DetailController;

	public C_EnvDetailController() {
		s_DetailController = this;
	}

	public static C_EnvDetailController? GetDetailController() => s_DetailController;
}
