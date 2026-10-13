#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using Source.Common;
using Game.Shared;
using Source;

#if CLIENT_DLL
namespace Game.Client;
using Game.Client.GarrysMod;
using Source.Common.Engine;
using FIELD = Source.FIELD<C_SENT_Anim>;
#else
namespace Game.Server;

using Game.Server.GarrysMod;

using FIELD = Source.FIELD<SENT_Anim>;
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

[LinkEntityToClass("sent_anim")]
[NetworkName("CSENT_anim")]
public partial class
#if CLIENT_DLL
	C_SENT_Anim : C_BaseAnimatingOverlay
#elif GAME_DLL
	SENT_Anim : BaseAnimatingOverlay
#endif
{
	public static readonly Table DT_SENT_anim = new(DT_BaseAnimatingOverlay, [
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
		= new Class(DT_SENT_anim);

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
