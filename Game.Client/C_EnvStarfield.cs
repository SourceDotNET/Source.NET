using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CEnvStarfield")]
public partial class C_EnvStarfield : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(On),
		new(nameof(Density)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_bOn")]
	public bool On;
	[NetworkVar("m_flDensity")]
	public float Density;
}
