using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("npc_portal_turret_floor")]
[NetworkName("CNPC_Portal_FloorTurret")]
public partial class NPC_Portal_FloorTurret : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(OutOfAmmo),
		nameof(LaserOn),
		new(nameof(LaserHaloSprite)) { Bits = 16, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_bOutOfAmmo")]
	public partial bool OutOfAmmo { get; set; }
	[NetworkVar("m_bLaserOn")]
	public partial bool LaserOn { get; set; }
	[NetworkVar("m_sLaserHaloSprite")]
	public partial int LaserHaloSprite { get; set; }
}
