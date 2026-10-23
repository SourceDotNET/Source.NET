using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CNPC_CombineGunship", Table = "DT_CombineGunship")]
public partial class C_NPC_CombineGunship : C_BaseHelicopter
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(HitPos)) { Bits = 0, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_vecHitPos")]
	public Vector3 HitPos;
}
