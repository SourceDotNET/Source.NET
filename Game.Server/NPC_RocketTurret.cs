using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("npc_rocket_turret")]
[NetworkName("CNPC_RocketTurret")]
public partial class NPC_RocketTurret : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(LaserState)) { Bits = 2 },
		new(nameof(SiteHalo)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(CurrentAngles)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_iLaserState")]
	public partial int LaserState { get; set; }
	[NetworkVar("m_nSiteHalo")]
	public partial int SiteHalo { get; set; }
	[NetworkVar("m_vecCurrentAngles")]
	public partial Vector3 CurrentAngles { get; set; }
}
