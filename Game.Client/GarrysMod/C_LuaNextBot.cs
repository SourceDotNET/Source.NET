using Game.Client.GarrysMod;
using Game.Shared;

using Source;
using Source.Common;

namespace Game.Client.NextBot;

[LinkEntityToClass("sent_nextbot")]
[NetworkName("CLuaNextBot")]
public partial class C_LuaNextBot : C_NextBotCombatCharacter
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ScriptedEntity)) { Encoding = NetEncoding.DataTable, DataTable = "DT_ScriptedEntity" },
		nameof(LifeState),
	];

	[NetworkVar("m_ScriptedEntity")]
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
