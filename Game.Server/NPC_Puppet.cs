using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("npc_puppet")]
[NetworkName("CNPC_Puppet")]
public partial class NPC_Puppet : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(AnimationTarget),
		new(nameof(TargetAttachment)) { Bits = 12 },
	];

	public NPC_Puppet() {
		AnimationTarget = new();
	}


	[NetworkVar("m_hAnimationTarget")]
	public partial EHANDLE AnimationTarget { get; set; }
	[NetworkVar("m_nTargetAttachment")]
	public partial int TargetAttachment { get; set; }
}
