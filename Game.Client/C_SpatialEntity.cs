using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;

[NetworkName("CSpatialEntity")]
public partial class C_SpatialEntity : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Origin)) { Encoding = NetEncoding.Vector, Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MinFalloff)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(MaxFalloff)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(CurWeight)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(Enabled),
	];

	[NetworkVar("m_minFalloff")]
	public float MinFalloff;
	[NetworkVar("m_maxFalloff")]
	public float MaxFalloff;
	[NetworkVar("m_flCurWeight")]
	public float CurWeight;
	[NetworkVar("m_bEnabled")]
	public bool Enabled;
}
