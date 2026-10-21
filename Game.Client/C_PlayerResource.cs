global using static Game.Client.PlayerResourceGlobals;
namespace Game.Client;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.GarrysMod.Lua;

using Game.Client.GarrysMod;

public static class PlayerResourceGlobals
{
	public static C_PlayerResource? g_pPlayerResource;
}
[NetworkName("CPlayerResource", NoBase = true)]
public partial class C_PlayerResource : C_BaseEntity
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
	InlineArrayMaxPlayersPlusOne<int> Ping = new();
	[NetworkVar("m_iScore")]
	InlineArrayMaxPlayersPlusOne<int> Score = new();
	[NetworkVar("m_iDeaths")]
	InlineArrayMaxPlayersPlusOne<int> Deaths = new();
	[NetworkVar("m_bConnected")]
	InlineArrayMaxPlayersPlusOne<bool> Connected = new();
	[NetworkVar("m_iTeam")]
	InlineArrayMaxPlayersPlusOne<int> Team = new();
	[NetworkVar("m_bAlive")]
	InlineArrayMaxPlayersPlusOne<bool> Alive = new();
	[NetworkVar("m_iHealth")]
	new InlineArrayMaxPlayersPlusOne<int> Health = new();
	[NetworkVar("m_iArmor")]
	InlineArrayMaxPlayersPlusOne<int> Armor = new();

	const string PLAYER_UNCONNECTED_NAME = "unconnected";
	readonly string?[] Name = new string?[Constants.MAX_PLAYERS + 1];

	public C_PlayerResource() => g_pPlayerResource = this;

	public Color GetTeamColor(int index) {
		if (index < 16384 && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.GetTeamColor)) {
			LuaEntity.Push_Entity(cl_entitylist.GetBaseEntity(index));
			if (gGM.CallReturns(1, 1)) {
				ILuaObject? ret = g_Lua!.GetReturn(0);
				if (ret == null || !ret.isTable())
					return new(0, 200, 255, 255);

				return new((byte)(int)ret.GetMemberFloat("r", 255), (byte)(int)ret.GetMemberFloat("g", 255), (byte)(int)ret.GetMemberFloat("b", 255), (byte)(int)ret.GetMemberFloat("a", 255));
			}
		}
		return new(255, 0, 255, 255);
	}

	public bool IsConnected(int index) {
		if (index < 1 || index > Constants.MAX_PLAYERS)
			return false;
		return Connected[index];
	}

	public ReadOnlySpan<char> GetPlayerName(int index) {
		if (index < 1 || index > Constants.MAX_PLAYERS) {
			Assert(false);
			return "ERRORNAME";
		}

		if (!IsConnected(index))
			return PLAYER_UNCONNECTED_NAME;

		if (Name[index] == null || stricmp(Name[index], PLAYER_UNCONNECTED_NAME) == 0) {
			if (IsConnected(index) && engine.GetPlayerInfo(index, out PlayerInfo info))
				Name[index] = new string(((ReadOnlySpan<char>)info.Name).SliceNullTerminatedString());
			else
				Name[index] = PLAYER_UNCONNECTED_NAME;
		}

		return Name[index];
	}
}
