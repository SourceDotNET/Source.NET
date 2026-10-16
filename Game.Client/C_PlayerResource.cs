global using static Game.Client.PlayerResourceGlobals;
namespace Game.Client;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.GarrysMod.Lua;

using Game.Client.GarrysMod;

using FIELD = Source.FIELD<C_PlayerResource>;

public static class PlayerResourceGlobals
{
	public static C_PlayerResource? g_pPlayerResource;
}
[NetworkName("CPlayerResource")]
public class C_PlayerResource : C_BaseEntity
{
	public static readonly RecvTable DT_PlayerResource = new([
		RecvPropArray3(FIELD.OF_ARRAY(nameof(Ping)), RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(Ping), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(Score)), RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(Score), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(Deaths)), RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(Deaths), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(Connected)), RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(Connected), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(Team)), RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(Team), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(Alive)), RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(Alive), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(Health)), RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(Health), 0))),
		RecvPropArray3(FIELD.OF_ARRAY(nameof(Armor)), RecvPropInt(FIELD.OF_ARRAYINDEX(nameof(Armor)))),
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_PlayerResource);

	[NetworkName("m_iPing")]
	InlineArrayMaxPlayersPlusOne<int> Ping = new();
	[NetworkName("m_iScore")]
	InlineArrayMaxPlayersPlusOne<int> Score = new();
	[NetworkName("m_iDeaths")]
	InlineArrayMaxPlayersPlusOne<int> Deaths = new();
	[NetworkName("m_bConnected")]
	InlineArrayMaxPlayersPlusOne<bool> Connected = new();
	[NetworkName("m_iTeam")]
	InlineArrayMaxPlayersPlusOne<int> Team = new();
	[NetworkName("m_bAlive")]
	InlineArrayMaxPlayersPlusOne<bool> Alive = new();
	[NetworkName("m_iHealth")]
	new InlineArrayMaxPlayersPlusOne<int> Health = new();
	[NetworkName("m_iArmor")]
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
