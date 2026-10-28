using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;

[LinkEntityToClass("npc_barney")]
[NetworkName("CNPC_Barney")]
public partial class NPC_Barney : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
	];

}
