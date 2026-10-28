using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CEnvAmbientLight")]
public partial class C_EnvAmbientLight : C_SpatialEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Color)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_vecColor")]
	public Vector3 Color;
}
