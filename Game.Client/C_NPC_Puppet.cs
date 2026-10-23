using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CNPC_Puppet")]
public partial class C_NPC_Puppet : C_AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(AnimationTarget),
		new(nameof(TargetAttachment)) { Bits = 12 },
	];

	[NetworkVar("m_hAnimationTarget")]
	public EHANDLE AnimationTarget = new();
	[NetworkVar("m_nTargetAttachment")]
	public int TargetAttachment;
}
