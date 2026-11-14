using Source.Common;
using Source.Common.Mathematics;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Server;
[NetworkName("CTEKillPlayerAttachments")]
public partial class TEKillPlayerAttachments(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Player)) { Bits = 5, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nPlayer")]
	public int Player;

	public override void Test(in Vector3 current_origin, in QAngle current_angles) {
		Player = 1;

		BroadcastRecipientFilter filter = new();
		Create(ref filter, 0.0f);
	}
}

public static partial class TempEnts
{
	static readonly TEKillPlayerAttachments g_TEKillPlayerAttachments = new("KillPlayerAttachments");

	public static void TE_KillPlayerAttachments<IRF>(scoped ref IRF filter, float delay, int player) where IRF : IRecipientFilter {
		g_TEKillPlayerAttachments.Player = player;

		g_TEKillPlayerAttachments.Create(ref filter, delay);
	}
}
