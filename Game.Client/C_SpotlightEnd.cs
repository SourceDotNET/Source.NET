using Game.Shared;

using Source;
using Source.Common;

namespace Game.Client;

[NetworkName("CSpotlightEnd")]
public partial class C_SpotlightEnd : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(LightScale)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Radius)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flLightScale")]
	public float LightScale;
	[NetworkVar("m_Radius")]
	public float Radius;
}

