using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CNPC_Portal_FloorTurret")]
public partial class C_NPC_Portal_FloorTurret : C_AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(OutOfAmmo),
		nameof(LaserOn),
		new(nameof(LaserHaloSprite)) { Bits = 16, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_bOutOfAmmo")]
	public bool OutOfAmmo;
	[NetworkVar("m_bLaserOn")]
	public bool LaserOn;
	[NetworkVar("m_sLaserHaloSprite")]
	public int LaserHaloSprite;
}
