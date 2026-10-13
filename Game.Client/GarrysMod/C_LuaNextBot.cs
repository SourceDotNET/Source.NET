using Game.Client.GarrysMod;
using Game.Shared;

using Source;
using Source.Common;

using FIELD = Source.FIELD<Game.Client.NextBot.C_LuaNextBot>;

namespace Game.Client.NextBot;

[LinkEntityToClass("sent_nextbot")]
[NetworkName("CLuaNextBot")]
public class C_LuaNextBot : C_NextBotCombatCharacter
{
	public static readonly RecvTable DT_LuaNextBot = new(DT_NextBot, [
		RecvPropDataTable("m_ScriptedEntity", FIELD.OF(nameof(ScriptedEntity)), ScriptedEntity.DT_ScriptedEntity, 0, RECV_GET_OBJECT_AT_FIELD(FIELD.OF(nameof(ScriptedEntity)))),
		RecvPropInt(FIELD.OF(nameof(LifeState)))
	]);
	public static readonly new ClientClass ClientClass = new ClientClass(DT_LuaNextBot);

	public readonly ScriptedEntity ScriptedEntity = new();

	public C_LuaNextBot() {
		ScriptedEntity.Init(this, 1);
	}

	public override ReadOnlySpan<char> GetLuaScriptName() => ScriptedEntity.GetScriptName();

	public override void InitializeScriptedEntity(ReadOnlySpan<char> name) {
		ScriptedEntity.SetScriptName(name);
		SetClassname(name);
		ScriptedEntity.StartLua();
	}

	public override void OnDataChanged(DataUpdateType updateType) {
		base.OnDataChanged(updateType);
		if (updateType == DataUpdateType.Created)
			ScriptedEntity.StartLua();
	}
}
