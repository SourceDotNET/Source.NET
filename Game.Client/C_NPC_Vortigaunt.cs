using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CNPC_Vortigaunt")]
public partial class C_NPC_Vortigaunt : C_AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BlueEndFadeTime)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(IsBlue),
		nameof(IsBlack),
	];

	[NetworkVar("m_flBlueEndFadeTime")]
	public float BlueEndFadeTime;
	[NetworkVar("m_bIsBlue")]
	public bool IsBlue;
	[NetworkVar("m_bIsBlack")]
	public bool IsBlack;
}
