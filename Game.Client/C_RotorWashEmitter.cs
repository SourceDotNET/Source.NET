using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CRotorWashEmitter")]
public partial class C_RotorWashEmitter : C_BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Altitude)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flAltitude")]
	public float Altitude;
}
