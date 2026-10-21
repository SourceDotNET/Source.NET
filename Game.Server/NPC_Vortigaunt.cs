using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("npc_vortigaunt")]
[NetworkName("CNPC_Vortigaunt")]
public partial class NPC_Vortigaunt : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BlueEndFadeTime)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(IsBlue),
		nameof(IsBlack),
	];

	[NetworkVar("m_flBlueEndFadeTime")]
	public partial float BlueEndFadeTime { get; set; }
	[NetworkVar("m_bIsBlue")]
	public partial bool IsBlue { get; set; }
	[NetworkVar("m_bIsBlack")]
	public partial bool IsBlack { get; set; }
}
