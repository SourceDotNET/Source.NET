using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CNPC_RocketTurret")]
public partial class C_NPC_RocketTurret : C_AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(LaserState)) { Bits = 2 },
		new(nameof(SiteHalo)) { Bits = 16, Flags = PropFlags.Unsigned },
		new(nameof(CurrentAngles)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_iLaserState")]
	public int LaserState;
	[NetworkVar("m_nSiteHalo")]
	public int SiteHalo;
	[NetworkVar("m_vecCurrentAngles")]
	public Vector3 CurrentAngles;
}
