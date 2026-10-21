using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[LinkEntityToClass("npc_manhack")]
[NetworkName("CNPC_Manhack")]
public partial class NPC_Manhack : AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(EnginePitch1)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(EnginePitch1Time)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EnginePitch2)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nEnginePitch1")]
	public partial int EnginePitch1 { get; set; }
	[NetworkVar("m_flEnginePitch1Time")]
	public partial float EnginePitch1Time { get; set; }
	[NetworkVar("m_nEnginePitch2")]
	public partial int EnginePitch2 { get; set; }
}
