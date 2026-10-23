using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CNPC_RollerMine", Table = "DT_RollerMine")]
public partial class C_NPC_RollerMine : C_AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(IsOpen),
		new(nameof(ActiveTime)) { Bits = 0, Flags = PropFlags.NoScale },
		nameof(HackedByAlyx),
		nameof(PowerDown),
	];

	[NetworkVar("m_bIsOpen")]
	public bool IsOpen;
	[NetworkVar("m_flActiveTime")]
	public float ActiveTime;
	[NetworkVar("m_bHackedByAlyx")]
	public bool HackedByAlyx;
	[NetworkVar("m_bPowerDown")]
	public bool PowerDown;
}
