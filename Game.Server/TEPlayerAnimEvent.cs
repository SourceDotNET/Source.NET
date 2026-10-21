using Game.Shared;

using Source.Common;

namespace Game.Server;

[NetworkName("CTEPlayerAnimEvent", NoBase = true)]
public partial class TEPlayerAnimEvent(ReadOnlySpan<char> name) : BaseTempEntity(name)
{
	[NetworkTable]
	public static readonly NetworkProp[] Props = [
		nameof(Player),
		new(nameof(Event)) { Bits = 6, Flags = PropFlags.Unsigned },
		new(nameof(Data)) { Bits = 32 },
	];

	[NetworkVar("m_hPlayer")]
	public EHANDLE Player = new();
	[NetworkVar("m_iEvent")]
	public int Event;
	[NetworkVar("m_nData")]
	public int Data;

	public static readonly TEPlayerAnimEvent g_TEPlayerAnimEvent = new("PlayerAnimEvent");

	public static void TE_PlayerAnimEvent(BasePlayer player, PlayerAnimEvent evt, int data, bool predicted) {
		PVSFilter filter = new(player.EyePosition());
		if (predicted)
			filter.UsePredictionRules();
		g_TEPlayerAnimEvent.Player.Set(player);
		g_TEPlayerAnimEvent.Event = (int)evt;
		g_TEPlayerAnimEvent.Data = data;
		g_TEPlayerAnimEvent.Create(ref filter, 0);
	}
}
