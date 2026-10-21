using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("env_rotorwash_emitter")]
[NetworkName("CRotorWashEmitter")]
public partial class RotorWashEmitter : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Altitude)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flAltitude")]
	public partial float Altitude { get; set; }
}
