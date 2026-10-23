using Game.Shared;

using Source.Common;

namespace Game.Server;

[LinkEntityToClass("env_fog_controller")]
[NetworkName("CFogController")]
public partial class FogController : BaseEntity
{
	[NetworkVar("m_fog")]
	public partial ref readonly FogParams Fog { get; }
	[NetworkTable(NoBase = true)]
	public static readonly new NetworkProp[] Props = [
		nameof(Fog.Enable),
		nameof(Fog.Blend),
		nameof(Fog.Radial),
		new(nameof(Fog.DirPrimary)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Fog.ColorPrimary)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.ColorSecondary)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.ColorPrimaryHDR)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.ColorSecondaryHDR)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.Start)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Fog.End)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Fog.MaxDensity)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Fog.FarZ)) { Bits = 0, Flags = PropFlags.NoScale },

		new(nameof(Fog.ColorPrimaryLerpTo)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.ColorSecondaryLerpTo)) { Encoding = NetEncoding.Int, Bits = 32, Flags = PropFlags.Unsigned },
		new(nameof(Fog.StartLerpTo)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(Fog.EndLerpTo)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(Fog.MaxDensityLerpTo)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(Fog.LerpTime)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(Fog.Duration)) { Encoding = NetEncoding.Float, Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
		new(nameof(Fog.HDRColorScale)) { Bits = 0, Flags = PropFlags.NoScale, Low = 0.0f, High = 0.0f },
	];
}
