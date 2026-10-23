global using static Game.Server.TeamGlobals;

using Source.Common;
using Source;

using Game.Shared;

namespace Game.Server;

public static class TeamGlobals
{
	public static readonly List<Team> g_Teams = [];
	public static int GetNumberOfTeams() => g_Teams.Count;
	public static Team? GetGlobalTeam(int index) {
		if (index < 0 || index >= GetNumberOfTeams())
			return null;

		return g_Teams[index];
	}
}

[LinkEntityToClass("team_manager")]
[NetworkName("CTeam")]
public partial class Team : BaseEntity
{
	[NetworkTable(NoBase = true)]
	public static readonly new NetworkProp[] Props = [
		new(nameof(TeamNum)) { Bits = 5 },
		new(nameof(Score)) { Bits = 0 },
		new(nameof(RoundsWon)) { Bits = 8 },
		nameof(Teamname),

		new("player_array_element") { Encoding = NetEncoding.Int, Bits = 10, Flags = PropFlags.Unsigned, Proxy = typeof(PlayerListProxy) },
		new("\"player_array\"") { Encoding = NetEncoding.Array2, Count = Constants.MAX_PLAYERS, Proxy = typeof(PlayerArrayLengthProxy) },
	];

	public static class PlayerArrayLengthProxy
	{
		public static int Send(object instance, int objectID) => ((Team)instance).Players.Count;
	}

	public static class PlayerListProxy
	{
		public static void Send(SendProp prop, object instance, IFieldAccessor field, ref DVariant outData, int element, int objectID) {
			Team team = (Team)instance;
			Assert(element < team.Players.Count);

			BasePlayer player = team.Players[element];
			outData.Int = player.EntIndex();
		}
	}

	internal ReadOnlySpan<char> GetName() {
		throw new NotImplementedException();
	}

	public void AddPlayer(BasePlayer player) {
		Players.Add(player);
		NetworkStateChanged();
	}

	public void RemovePlayer(BasePlayer player) {
		Players.Remove(player);
		NetworkStateChanged();
	}

	public int GetNumPlayers() => Players.Count;

	public readonly List<BasePlayer> Players = [];
	[NetworkVar("m_szTeamname")]
	public partial ref readonly InlineArray32<char> Teamname { get; }
	[NetworkVar("m_iScore")]
	public partial int Score { get; set; }
	[NetworkVar("m_iRoundsWon")]
	public partial int RoundsWon { get; set; }
	public int Deaths;
	public int LastSpawn;
	[NetworkVar("m_iTeamNum")]
	public new partial int TeamNum { get; set; }
}
