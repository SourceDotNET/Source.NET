using Source.Common;
using Source;
using Game.Shared;
using System.Numerics;
namespace Game.Client;
[NetworkName("CTEKillPlayerAttachments")]
public partial class C_TEKillPlayerAttachments : C_BaseTempEntity
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		new(nameof(Player)) { Bits = 5, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_nPlayer")]
	public int Player;
}

public static partial class TempEnts
{
	public static void TE_KillPlayerAttachments(IRecipientFilter filter, float delay, int player) {
		throw new NotImplementedException();
	}
}
