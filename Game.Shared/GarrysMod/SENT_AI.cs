#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
using Game.Shared;
using Source;

#if CLIENT_DLL
namespace Game.Client;
using Game.Client.GarrysMod;
#else
namespace Game.Server;
using Game.Server.GarrysMod;
#endif

[LinkEntityToClass("sent_ai")]
[NetworkName("CSENT_AI")]
public partial class
#if CLIENT_DLL
	C_SENT_AI : C_AI_BaseNPC
#elif GAME_DLL
	SENT_AI 	: AI_BaseNPC
#endif
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ScriptedEntity)) { Encoding = NetEncoding.DataTable, DataTable = "DT_ScriptedEntity" },
	];

	[NetworkVar("m_ScriptedEntity")]
	public readonly ScriptedEntity ScriptedEntity = new();

#if CLIENT_DLL
	public C_SENT_AI() {
#else
	public SENT_AI() {
#endif
		ScriptedEntity.Init(this, 1);
	}

	public override ReadOnlySpan<char> GetLuaScriptName() => ScriptedEntity.GetScriptName();

	public override void InitializeScriptedEntity(ReadOnlySpan<char> name) {
		ScriptedEntity.SetScriptName(name);
		SetClassname(name);
		ScriptedEntity.StartLua();
	}

#if CLIENT_DLL
	public override void OnDataChanged(DataUpdateType updateType) {
		base.OnDataChanged(updateType);
		if (updateType == DataUpdateType.Created)
			ScriptedEntity.StartLua();
	}
#endif
}
#endif
