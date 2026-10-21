using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_ambient_light")]
[NetworkName("CEnvAmbientLight")]
public partial class EnvAmbientLight : SpatialEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Color)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_vecColor")]
	public partial Vector3 Color { get; set; }
}
