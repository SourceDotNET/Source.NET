using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("npc_combinegunship")]
[NetworkName("CNPC_CombineGunship", Table = "DT_CombineGunship")]
public partial class NPC_CombineGunship : BaseHelicopter
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(HitPos)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_vecHitPos")]
	public partial Vector3 HitPos { get; set; }
}
