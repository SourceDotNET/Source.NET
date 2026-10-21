using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;

namespace Game.Server;

[LinkEntityToClass("shadow_control")]
[NetworkName("CShadowControl", NoBase = true)]
public partial class ShadowControl : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ShadowDirection)) { Bits = -1, Flags = PropFlags.NoScale },
		new(nameof(ShadowColor)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(ShadowMaxDist)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(DisableShadows),
		nameof(EnableLocalLightShadows),
	];

	[NetworkVar("m_shadowDirection")]
	public partial Vector3 ShadowDirection { get; set; }
	[NetworkVar("m_shadowColor")]
	public partial Color ShadowColor { get; set; }
	[NetworkVar("m_flShadowMaxDist")]
	public partial float ShadowMaxDist { get; set; }
	[NetworkVar("m_bDisableShadows")]
	public partial bool DisableShadows { get; set; }
	[NetworkVar("m_bEnableLocalLightShadows")]
	public partial bool EnableLocalLightShadows { get; set; }
}