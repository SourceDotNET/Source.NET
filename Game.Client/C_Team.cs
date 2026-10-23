global using static Game.Client.TeamGlobals;

using Game.Shared;

using Source;
using Source.Common;

namespace Game.Client;

public static class TeamGlobals
{
	public static readonly List<C_Team> g_Teams = [];
	public static int GetNumberOfTeams() => g_Teams.Count;
	public static int GetNumTeams() => g_Teams.Count + 1;
	public static C_Team? GetLocalTeam() {
		C_BasePlayer? player = C_BasePlayer.GetLocalPlayer();
		if (player == null) return null;
		return GetPlayersTeam(player.Index);
	}
	public static C_Team? GetPlayersTeam(int playerIndex) {
		for (int i = 0; i < g_Teams.Count; i++) 
			if (g_Teams[i].ContainsPlayer(playerIndex))
				return g_Teams[i];

		return null;
	}
	public static C_Team? GetPlayersTeam(C_BasePlayer player) => GetPlayersTeam(player.EntIndex()); 
	public static bool ArePlayersOnSameTeam(int playerIndex1, int playerIndex2){
		for (int i = 0; i < g_Teams.Count; i++) 
			if (g_Teams[i].ContainsPlayer(playerIndex1) && g_Teams[i].ContainsPlayer(playerIndex2))
				return true;

		return false;
	} 
	public static C_Team? GetGlobalTeam(int index) {
		if (index < 0 || index >= GetNumberOfTeams())
			return null;

		return g_Teams[index];
	}
}


[NetworkName("CTeam")]
public partial class C_Team : C_BaseEntity
{
	[NetworkTable(NoBase = true)]
	public static readonly new NetworkProp[] Props = [
		nameof(TeamNum),
		nameof(Score),
		nameof(RoundsWon),
		nameof(Teamname),

		new("player_array_element") { Encoding = NetEncoding.Int, Proxy = typeof(PlayerListProxy) },
		new("\"player_array\"") { Encoding = NetEncoding.Array2, Count = Constants.MAX_PLAYERS, Proxy = typeof(PlayerArrayLengthProxy) },
	];

	public static class PlayerArrayLengthProxy
	{
		public static void Recv(object instance, int objectID, int currentArrayLength) {
			C_Team team = (C_Team)instance;

			if (team.Players.Count != currentArrayLength)
				team.Players.SetSize(currentArrayLength);
		}
	}

	public static class PlayerListProxy
	{
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
			C_Team team = (C_Team)instance;
			team.Players[data.Element] = data.Value.Int;
		}
	}

	public readonly List<int> Players = [];
	[NetworkVar("m_szTeamname")]
	public InlineArray32<char> Teamname;
	[NetworkVar("m_iScore")]
	public int Score;
	[NetworkVar("m_iRoundsWon")]
	public int RoundsWon;

	public int Deaths;
	public int Ping;
	public int Packetloss;
	[NetworkVar("m_iTeamNum")]
	public new int TeamNum;

	public void RemoveAllPlayers() => Players.Clear();
	public override void PreDataUpdate(DataUpdateType updateType) => base.PreDataUpdate(updateType);
	public C_BasePlayer? GetPlayer(int idx) => (C_BasePlayer?)cl_entitylist.GetEnt(Players[idx]);
	public int GetTeamNumber() => TeamNum;
	public ReadOnlySpan<char> Get_Name() => ((Span<char>)Teamname).SliceNullTerminatedString();
	public int Get_Score() => Score;
	public int Get_Deaths() => Deaths;
	public int Get_Ping() => Ping;
	public int Get_Number_Players() => Players.Count;

	public bool ContainsPlayer(int playerIndex) {
		for (int i = 0; i < Players.Count; i++)
			if (Players[i] == playerIndex)
				return true;

		return false;
	}
}
