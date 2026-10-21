using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("npc_barnacle")]
[NetworkName("CNPC_Barnacle", Table = "DT_Barnacle")]
public partial class NPC_Barnacle : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Altitude)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Root)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Tip)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(TipDrawOffset)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flAltitude")]
	public partial float Altitude { get; set; }
	[NetworkVar("m_vecRoot")]
	public partial Vector3 Root { get; set; }
	[NetworkVar("m_vecTip")]
	public partial Vector3 Tip { get; set; }
	[NetworkVar("m_vecTipDrawOffset")]
	public partial Vector3 TipDrawOffset { get; set; }
}
