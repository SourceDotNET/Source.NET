using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("npc_antlionguard")]
[NetworkName("CNPC_AntlionGuard")]
public partial class NPC_AntlionGuard : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(CavernBreed),
		nameof(InCavern),
		new(nameof(BleedingLevel)) { Bits = 2, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_bCavernBreed")]
	public partial bool CavernBreed { get; set; }
	[NetworkVar("m_bInCavern")]
	public partial bool InCavern { get; set; }
	[NetworkVar("m_iBleedingLevel")]
	public partial int BleedingLevel { get; set; }
}
