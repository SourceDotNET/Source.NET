using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_starfield")]
[NetworkName("CEnvStarfield")]
public partial class EnvStarfield : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(On),
		new(nameof(Density)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_bOn")]
	public partial bool On { get; set; }
	[NetworkVar("m_flDensity")]
	public partial float Density { get; set; }
}
