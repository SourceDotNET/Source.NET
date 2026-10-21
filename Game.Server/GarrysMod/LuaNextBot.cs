using Game.Server;
using Game.Server.GarrysMod;
using Game.Shared;

using Source.Common;

namespace Game.Server.NextBot;

[LinkEntityToClass("sent_nextbot")]
[NetworkName("CLuaNextBot")]
public partial class LuaNextBot : NextBotCombatCharacter
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ScriptedEntity)) { Encoding = NetEncoding.DataTable, DataTable = "DT_ScriptedEntity" },
		new(nameof(LifeState)) { Bits = 3, Flags = PropFlags.Unsigned },
	];

	[NetworkVar("m_ScriptedEntity")]
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
