using Game.Client;
using Game.Shared;

using Source;
using Source.Common;

using System.Numerics;

namespace Game.Server;

[NetworkName("CShadowControl")]
public partial class C_ShadowControl : C_BaseEntity
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
	public Vector3 ShadowDirection;
	[NetworkVar("m_shadowColor")]
	public Color ShadowColor;
	[NetworkVar("m_flShadowMaxDist")]
	public float ShadowMaxDist;
	[NetworkVar("m_bDisableShadows")]
	public bool DisableShadows;
	[NetworkVar("m_bEnableLocalLightShadows")]
	public bool EnableLocalLightShadows;
}
