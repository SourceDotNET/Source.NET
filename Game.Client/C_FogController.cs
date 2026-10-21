using Game.Client;
using Game.Shared;

using Source.Common;

namespace Game.Server;
[NetworkName("CFogController")]
public partial class C_FogController : C_BaseEntity
{
	[NetworkVar("m_fog")]
	public FogParams Fog;
	[NetworkTable(NoBase = true)]
	public static readonly new NetworkProp[] Props = [
		nameof(Fog.Enable),
		nameof(Fog.Blend),
		nameof(Fog.Radial),
		nameof(Fog.DirPrimary),
		new(nameof(Fog.ColorPrimary)) { Encoding = NetEncoding.Int },
		new(nameof(Fog.ColorSecondary)) { Encoding = NetEncoding.Int },
		new(nameof(Fog.ColorPrimaryHDR)) { Encoding = NetEncoding.Int },
		new(nameof(Fog.ColorSecondaryHDR)) { Encoding = NetEncoding.Int },
		nameof(Fog.Start),
		nameof(Fog.End),
		nameof(Fog.FarZ),
		nameof(Fog.MaxDensity),

		new(nameof(Fog.ColorPrimaryLerpTo)) { Encoding = NetEncoding.Int },
		new(nameof(Fog.ColorSecondaryLerpTo)) { Encoding = NetEncoding.Int },
		nameof(Fog.StartLerpTo),
		nameof(Fog.EndLerpTo),
		nameof(Fog.MaxDensityLerpTo),
		new(nameof(Fog.LerpTime)) { Encoding = NetEncoding.Float },
		new(nameof(Fog.Duration)) { Encoding = NetEncoding.Float },
		nameof(Fog.HDRColorScale),
	];
}