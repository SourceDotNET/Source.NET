using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CNPC_Manhack")]
public partial class C_NPC_Manhack : C_AI_BaseNPC
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(EnginePitch1)) { Bits = 8, Flags = PropFlags.Unsigned },
		new(nameof(EnginePitch1Time)) { Bits = 0, Flags = PropFlags.NoScale },
		new(nameof(EnginePitch2)) { Bits = 8, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nEnginePitch1")]
	public int EnginePitch1;
	[NetworkVar("m_flEnginePitch1Time")]
	public float EnginePitch1Time;
	[NetworkVar("m_nEnginePitch2")]
	public int EnginePitch2;
}
