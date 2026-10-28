using Game.Shared;

using Source;
using Source.Common;
namespace Game.Client;

[NetworkName("CFuncRotating")]
public partial class C_FuncRotating : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(NetworkOrigin),
		new(nameof(NetworkAngles)) { Encoding = NetEncoding.VectorElements },
		new(nameof(SimulationTime)) { Encoding = NetEncoding.Int, Proxy = typeof(SimulationTimeProxy) },
	];
}

