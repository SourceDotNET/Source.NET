using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("npc_rollermine")]
[NetworkName("CNPC_RollerMine", Table = "DT_RollerMine")]
public partial class NPC_RollerMine : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(IsOpen),
		new(nameof(ActiveTime)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(HackedByAlyx),
		nameof(PowerDown),
	];

	[NetworkVar("m_bIsOpen")]
	public partial bool IsOpen { get; set; }
	[NetworkVar("m_flActiveTime")]
	public partial float ActiveTime { get; set; }
	[NetworkVar("m_bHackedByAlyx")]
	public partial bool HackedByAlyx { get; set; }
	[NetworkVar("m_bPowerDown")]
	public partial bool PowerDown { get; set; }
}
