using Source.Common;
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
}
