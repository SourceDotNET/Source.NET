using Game.Shared;

using Source.Common;

namespace Game.Server;

public class EventLog : BaseGameSystem, IGameEventListener2
{
	public void FireGameEvent(IGameEvent ev) => PrintEvent(ev);

	protected virtual bool PrintEvent(IGameEvent ev) {
		ReadOnlySpan<char> name = ev.GetName();

		if (name.StartsWith("server_"))
			return true;
		else if (name.StartsWith("player_"))
			return PrintPlayerEvent(ev);
		else if (name.StartsWith("team_"))
			return PrintTeamEvent(ev);
		else if (name.StartsWith("game_"))
			return PrintGameEvent(ev);
		else
			return PrintOtherEvent(ev);
	}

	protected virtual bool PrintGameEvent(IGameEvent ev) => false;

	protected virtual bool PrintPlayerEvent(IGameEvent ev) {
		ReadOnlySpan<char> eventName = ev.GetName();
		int userid = ev.GetInt("userid");

		if (eventName.StartsWith("player_connect")) {
			ReadOnlySpan<char> name = ev.GetString("name");
			ReadOnlySpan<char> address = ev.GetString("address");
			ReadOnlySpan<char> networkid = ev.GetString("networkid");
			Util.LogPrintf($"\"{name}<{userid}><{networkid}><>\" connected, address \"{address}\"\n");
			return true;
		}
		else if (eventName.StartsWith("player_disconnect")) {
			ReadOnlySpan<char> reason = ev.GetString("reason");
			ReadOnlySpan<char> name = ev.GetString("name");
			ReadOnlySpan<char> networkid = ev.GetString("networkid");
			Team? team = Util.PlayerByUserId(userid)?.GetTeam();
			ReadOnlySpan<char> teamName = team != null ? team.GetName() : "";
			Util.LogPrintf($"\"{name}<{userid}><{networkid}><{teamName}>\" disconnected (reason \"{reason}\")\n");
			return true;
		}

		BasePlayer? player = Util.PlayerByUserId(userid);
		if (player == null) {
			DevMsg($"CEventLog::PrintPlayerEvent: Failed to find player (userid: {userid}, event: {eventName})\n");
			return false;
		}

		if (eventName.StartsWith("player_team") || eventName.StartsWith("player_death")) {
#if GMOD_DLL
			return false;
#else
			// todo: joined team, killed and committed suicide lines
			return false;
#endif
		}
		else if (eventName.StartsWith("player_activate")) {
			Util.LogPrintf($"\"{player.GetPlayerName()}<{userid}><{player.GetNetworkIDString()}><>\" entered the game\n");
			return true;
		}
		else if (eventName.StartsWith("player_changename")) {
			ReadOnlySpan<char> newName = ev.GetString("newname");
			ReadOnlySpan<char> oldName = ev.GetString("oldname");
			Team? team = player.GetTeam();
			ReadOnlySpan<char> teamName = team != null ? team.GetName() : "";
			Util.LogPrintf($"\"{oldName}<{userid}><{player.GetNetworkIDString()}><{teamName}>\" changed name to \"{newName}\"\n");
			return true;
		}

		return false;
	}

	protected virtual bool PrintTeamEvent(IGameEvent ev) => false;

	protected virtual bool PrintOtherEvent(IGameEvent ev) => false;

	public override bool Init() {
		gameeventmanager.AddListener(this, "player_changename", true);
		gameeventmanager.AddListener(this, "player_activate", true);
		gameeventmanager.AddListener(this, "player_death", true);
		gameeventmanager.AddListener(this, "player_team", true);
		gameeventmanager.AddListener(this, "player_disconnect", true);
		gameeventmanager.AddListener(this, "player_connect", true);

		return true;
	}

	public override void Shutdown() => gameeventmanager.RemoveListener(this);
}
