using Source.Common.Engine;
#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
using Game.Shared;
using Source;

#if CLIENT_DLL
namespace Game.Client;
using Game.Client.GarrysMod;
using FIELD = Source.FIELD<C_SENT_Point>;
#else
namespace Game.Server;
using Game.Server.GarrysMod;
using FIELD = Source.FIELD<SENT_Point>;
#endif

using Table =
#if CLIENT_DLL
	RecvTable;
#else
	SendTable;
#endif

using Class =
#if CLIENT_DLL
	ClientClass;
#else
	ServerClass;
#endif

[LinkEntityToClass("sent_point")]
[NetworkName("CSENT_point")]
public partial class
#if CLIENT_DLL
    C_SENT_Point
#else
	SENT_Point
#endif
	: BaseEntity
{
	public static readonly Table DT_SENT_point = new(DT_BaseEntity, [
#if CLIENT_DLL
		RecvPropDataTable("m_ScriptedEntity", FIELD.OF(nameof(ScriptedEntity)), ScriptedEntity.DT_ScriptedEntity, 0, RECV_GET_OBJECT_AT_FIELD(FIELD.OF(nameof(ScriptedEntity))))
#elif GAME_DLL
		SendPropDataTable("m_ScriptedEntity", FIELD.OF(nameof(ScriptedEntity)), ScriptedEntity.DT_ScriptedEntity)
#endif
	]);

	public static readonly new Class
#if CLIENT_DLL
		ClientClass
#else
		ServerClass
#endif
		= new Class(DT_SENT_point);

	public readonly ScriptedEntity ScriptedEntity = new();

#if CLIENT_DLL
	public C_SENT_Point() {
#else
	public SENT_Point() {
#endif
		ScriptedEntity.Init(this, 1);
	}

	public override ReadOnlySpan<char> GetLuaScriptName() => ScriptedEntity.GetScriptName();

	public override void InitializeScriptedEntity(ReadOnlySpan<char> name) {
		ScriptedEntity.SetScriptName(name);
		SetClassname(name);
		ScriptedEntity.StartLua();
	}

#if GAME_DLL
	public override EdictFlags UpdateTransmitState() {
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.UpdateTransmitState) && ScriptedEntity.Call(0, 1)) {
			int transmit = g_Lua!.GetReturn(0).GetInt();
			if (transmit == 0)
				return SetTransmitState(EdictFlags.Always);
			if (transmit != 1)
				return SetTransmitState(EdictFlags.PVSCheck);
		}
		return SetTransmitState(EdictFlags.DontSend);
	}
#endif

#if CLIENT_DLL
	public override void OnDataChanged(DataUpdateType updateType) {
		base.OnDataChanged(updateType);
		if (updateType == DataUpdateType.Created)
			ScriptedEntity.StartLua();
	}

	public override void Spawn() {
		SetThink(Think);
		SetNextThink(gpGlobals.CurTime + 0.1);
		SetNextClientThink(CLIENT_THINK_ALWAYS);
	}

	public override void ClientThink() => ScriptedEntity.CallFunction((int)LUA_POOLEDSTRING.Think, false);

	public override void Think() {
		if (!ScriptedEntity.CallFunction((int)LUA_POOLEDSTRING.Think, true))
			SetNextThink(gpGlobals.CurTime + 0.2);
	}
#endif
}
#endif
