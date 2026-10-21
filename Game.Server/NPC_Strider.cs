using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("npc_strider")]
[NetworkName("CNPC_Strider")]
public partial class NPC_Strider : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(HitPos)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(IKTarget)) { Encoding = NetEncoding.Elements, Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_vecHitPos")]
	public partial Vector3 HitPos { get; set; }
	[NetworkVar("m_vecIKTarget")]
	public partial ref readonly InlineArray6<Vector3> IKTarget { get; }
}
