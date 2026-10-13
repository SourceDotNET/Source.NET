using Game.Server;
using Game.Server.GarrysMod;
using Game.Shared;

using Source.Common;

using FIELD = Source.FIELD<Game.Server.NextBot.LuaNextBot>;
namespace Game.Server.NextBot;

[LinkEntityToClass("sent_nextbot")]
[NetworkName("CLuaNextBot")]
public class LuaNextBot : NextBotCombatCharacter
{
	public static readonly SendTable DT_LuaNextBot = new(DT_NextBot, [
		SendPropDataTable("m_ScriptedEntity", FIELD.OF(nameof(ScriptedEntity)), ScriptedEntity.DT_ScriptedEntity),
		SendPropInt(FIELD.OF(nameof(LifeState)), 3, PropFlags.Unsigned)
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_LuaNextBot);

	public readonly ScriptedEntity ScriptedEntity = new();

	public LuaNextBot() {
		ScriptedEntity.Init(this, 1);
	}

	public override ReadOnlySpan<char> GetLuaScriptName() => ScriptedEntity.GetScriptName();

	public override void InitializeScriptedEntity(ReadOnlySpan<char> name) {
		ScriptedEntity.SetScriptName(name);
		SetClassname(name);
		ScriptedEntity.StartLua();
	}
}
