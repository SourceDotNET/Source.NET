using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CNPC_Barnacle", Table = "DT_Barnacle")]
public partial class C_NPC_Barnacle : C_AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Altitude)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(Root)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(Tip)) { Bits = 0, Flags = PropFlags.Coord },
		new(nameof(TipDrawOffset)) { Bits = 0, Flags = PropFlags.NoScale },
	];

	[NetworkVar("m_flAltitude")]
	public float Altitude;
	[NetworkVar("m_vecRoot")]
	public Vector3 Root;
	[NetworkVar("m_vecTip")]
	public Vector3 Tip;
	[NetworkVar("m_vecTipDrawOffset")]
	public Vector3 TipDrawOffset;
}
