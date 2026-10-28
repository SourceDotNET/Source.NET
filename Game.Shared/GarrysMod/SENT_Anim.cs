#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
using Game.Shared;
using Source;

#if CLIENT_DLL
namespace Game.Client;
using Game.Client.GarrysMod;
using Source.Common.Engine;
#else
namespace Game.Server;

using Game.Server.GarrysMod;

#endif

[LinkEntityToClass("sent_anim")]
[NetworkName("CSENT_anim")]
public partial class
#if CLIENT_DLL
	C_SENT_Anim : C_BaseAnimatingOverlay
#elif GAME_DLL
	SENT_Anim : BaseAnimatingOverlay
#endif
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(ScriptedEntity)) { Encoding = NetEncoding.DataTable, DataTable = "DT_ScriptedEntity" },
	];

	[NetworkVar("m_ScriptedEntity")]
	public readonly ScriptedEntity ScriptedEntity = new();

#if CLIENT_DLL
	public C_SENT_Anim() {
#else
	public SENT_Anim() {
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
	public override RenderGroup GetRenderGroup() {
		RenderGroup group = RenderMode == (int)Source.RenderMode.None ? RenderGroup.OpaqueEntity : base.GetRenderGroup();
		if (!HasLuaTable())
			return group;

		if (GetLuaTable()!.GetMemberBool("WantsTranslucency", false) && group == RenderGroup.OpaqueEntity)
			group = RenderGroup.TwoPass;

		int luaGroup = GetLuaTable()!.GetMemberInt("RenderGroup", (int)group);
		if (luaGroup < 0)
			return RenderGroup.OpaqueEntityHuge;
		if (luaGroup > (int)RenderGroup.Other)
			return RenderGroup.Other;
		if (luaGroup == (int)RenderGroup.OpaqueStatic)
			return RenderGroup.OpaqueEntity;
		if (luaGroup == (int)RenderGroup.OpaqueStaticHuge)
			return RenderGroup.OpaqueEntityHuge;
		return (RenderGroup)luaGroup;
	}

	public override void OnDataChanged(DataUpdateType updateType) {
		base.OnDataChanged(updateType);
		if (updateType == DataUpdateType.Created)
			ScriptedEntity.StartLua();
	}
#endif
}
#endif
