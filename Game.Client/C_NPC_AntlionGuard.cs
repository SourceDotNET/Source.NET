using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CNPC_AntlionGuard")]
public partial class C_NPC_AntlionGuard : C_AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(CavernBreed),
		nameof(InCavern),
		new(nameof(BleedingLevel)) { Bits = 2, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_bCavernBreed")]
	public bool CavernBreed;
	[NetworkVar("m_bInCavern")]
	public bool InCavern;
	[NetworkVar("m_iBleedingLevel")]
	public int BleedingLevel;
}
