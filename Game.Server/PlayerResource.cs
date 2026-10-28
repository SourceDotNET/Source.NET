global using static Game.Server.PlayerResourceGlobals;

namespace Game.Server;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;

public static class PlayerResourceGlobals
{
	public static PlayerResource? g_pPlayerResource;
}

[LinkEntityToClass("player_manager")]
[NetworkName("CPlayerResource", NoBase = true)]
public partial class PlayerResource : BaseEntity
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(Ping)) { Bits = 12, Flags = PropFlags.Unsigned },
		new(nameof(Score)) { Bits = 32 },
		new(nameof(Deaths)) { Bits = 32 },
		new(nameof(Connected)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(Team)) { Bits = 16 },
		new(nameof(Alive)) { Encoding = NetEncoding.Int, Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(Health)) { Bits = 32, Flags = PropFlags.VarInt | PropFlags.Unsigned | PropFlags.Normal },
		new(nameof(Armor)) { Bits = 32, Flags = PropFlags.Unsigned },
	];


	[NetworkVar("m_iPing")]
	partial ref readonly InlineArrayMaxPlayersPlusOne<int> Ping { get; }
	[NetworkVar("m_iScore")]
	partial ref readonly InlineArrayMaxPlayersPlusOne<int> Score { get; }
	[NetworkVar("m_iDeaths")]
	partial ref readonly InlineArrayMaxPlayersPlusOne<int> Deaths { get; }
	[NetworkVar("m_bConnected")]
	partial ref readonly InlineArrayMaxPlayersPlusOne<bool> Connected { get; }
	[NetworkVar("m_iTeam")]
	partial ref readonly InlineArrayMaxPlayersPlusOne<int> Team { get; }
	[NetworkVar("m_bAlive")]
	partial ref readonly InlineArrayMaxPlayersPlusOne<bool> Alive { get; }
	[NetworkVar("m_iHealth")]
	new partial ref readonly InlineArrayMaxPlayersPlusOne<int> Health { get; }
	[NetworkVar("m_iArmor")]
	partial ref readonly InlineArrayMaxPlayersPlusOne<int> Armor { get; }

	public PlayerResource() {
		PingForModify() = new();
		ScoreForModify() = new();
		DeathsForModify() = new();
		ConnectedForModify() = new();
		TeamForModify() = new();
		AliveForModify() = new();
		HealthForModify() = new();
		ArmorForModify() = new();
	}

	public override void Spawn() {
		SetThink(ResourceThink);
		SetNextThink(gpGlobals.CurTime);
	}

	public override EdictFlags UpdateTransmitState() => SetTransmitState(EdictFlags.Always);

	void ResourceThink() {
		UpdatePlayerData();
		SetNextThink(gpGlobals.CurTime + 0.1f);
	}

	void UpdatePlayerData() {
		for (int i = 1; i <= Constants.MAX_PLAYERS; i++) {
			BasePlayer? player = Util.PlayerByIndex(i);
			if (player != null && player.IsConnected())
				UpdateConnectedPlayer(i, player);
			else
				UpdateDisconnectedPlayer(i);
		}
	}

	void UpdateConnectedPlayer(int index, BasePlayer player) {
		ScoreForModify()[index] = player.FragCount();
		DeathsForModify()[index] = player.DeathCount();
		ConnectedForModify()[index] = true;
		TeamForModify()[index] = player.GetTeamNumber();
		AliveForModify()[index] = player.IsAlive();
		HealthForModify()[index] = Math.Max(0, player.GetHealth());
	}

	void UpdateDisconnectedPlayer(int index) => ConnectedForModify()[index] = false;
}
