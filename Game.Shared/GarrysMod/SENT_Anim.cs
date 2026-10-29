#if (CLIENT_DLL || GAME_DLL) && GMOD_DLL
using System.Numerics;

using Source.Common;
using Source.Common.GarrysMod.Lua;
using Source.Common.Physics;
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

#if GAME_DLL
	public override void Use(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		if (IsMarkedForDeletion() || !ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.Use))
			return;
		LuaEntity.Push_Entity(activator);
		LuaEntity.Push_Entity(caller);
		g_Lua!.PushNumber((int)useType);
		g_Lua.PushNumber(value);
		ScriptedEntity.Call(4, 0);
	}

	public override void Think() {
		if (IsMarkedForDeletion())
			return;
		if (!ScriptedEntity.CallFunction((int)LUA_POOLEDSTRING.Think, true))
			SetNextThink(gpGlobals.CurTime + 0.2);
		if (HasLuaTable() && GetLuaTable()!.GetMemberBool("AutomaticFrameAdvance", true)) {
			StudioFrameAdvance();
			DispatchAnimEvents(this);
		}
	}

	public override void UpdateOnRemove() {
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.OnRemove)) {
			g_Lua!.PushBool(false);
			ScriptedEntity.Call(1, 0);
		}
		base.UpdateOnRemove();
	}

	void CallEntityHook(LUA_POOLEDSTRING hook, BaseEntity? other) {
		if (ScriptedEntity.StartFunction((int)hook)) {
			LuaEntity.Push_Entity(other);
			ScriptedEntity.Call(1, 0);
		}
	}

	public override int OnTakeDamage(in TakeDamageInfo info) {
		if (IsMarkedForDeletion())
			return 0;
		int ret = 0;
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.OnTakeDamage)) {
			TakeDamageInfoRef infoRef = new() { Info = info };
			LuaTakeDamageInfo.Push_TakeDamageInfo(infoRef);
			if (ScriptedEntity.Call(1, 1)) {
				ILuaObject obj = g_Lua!.GetReturn(0);
				if (obj.GetType() == LuaType.Number)
					ret = obj.GetInt();
			}
		}
		return ret;
	}

	public override void VPhysicsUpdate(IPhysicsObject physics) {
		if (IsMarkedForDeletion())
			return;
		base.VPhysicsUpdate(physics);
		if (!physics.IsAsleep() && ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.PhysicsUpdate)) {
			LuaPhysObj.Push_PhysObj(physics);
			ScriptedEntity.Call(1, 0);
		}
	}

	public override void HandleAnimEvent(ref AnimEvent animEvent) {
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.HandleAnimEvent)) {
			g_Lua!.PushNumber(animEvent.Event);
			g_Lua.PushNumber(animEvent.EventTime);
			g_Lua.PushNumber(animEvent.Cycle);
			g_Lua.PushNumber((int)animEvent.Type);
			g_Lua.PushString(animEvent.Options ?? "");
			if (ScriptedEntity.Call(5, 1)) {
				ILuaObject ret = g_Lua.GetReturn(0);
				if (ret.GetType() == LuaType.Bool && ret.GetBool())
					return;
			}
		}
		base.HandleAnimEvent(ref animEvent);
	}

	public override void OnRestore() {
		base.OnRestore();
		ScriptedEntity.StartLua();
		ScriptedEntity.CallFunction("OnRestore", true);
	}

	public override void StartTouch(BaseEntity? other) => CallEntityHook(LUA_POOLEDSTRING.StartTouch, other);
	public override void Touch(BaseEntity? other) => CallEntityHook(LUA_POOLEDSTRING.Touch, other);
	public override void EndTouch(BaseEntity? other) => CallEntityHook(LUA_POOLEDSTRING.EndTouch, other);
	public override void Blocked(BaseEntity? other) => CallEntityHook(LUA_POOLEDSTRING.Blocked, other);
#else
	public override void UpdateOnRemove() {
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.OnRemove)) {
			g_Lua!.PushBool(DestroyedOnRecreateEntities);
			ScriptedEntity.Call(1, 0);
		}
		base.UpdateOnRemove();
	}

	bool InDraw;
	StudioFlags DrawFlags;

	public override int DrawModel(StudioFlags flags) {
		if (!ShouldDraw())
			return 0;
		if (InDraw)
			return base.DrawModel(flags | DrawFlags);

		InDraw = true;
		DrawFlags = flags & StudioFlags.ShadowDepthTexture;
		if (ScriptedEntity.StartFunction((int)((flags & StudioFlags.Transparency) != 0 ? LUA_POOLEDSTRING.DrawTranslucent : LUA_POOLEDSTRING.Draw))) {
			g_Lua!.PushNumber((int)flags);
			ScriptedEntity.Call(1, 0);
		}
		DrawFlags = 0;
		InDraw = false;
		return 1;
	}

	public override bool GetShadowCastDirection(ref Vector3 direction, ShadowType shadowType) {
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.GetShadowCastDirection)) {
			g_Lua!.PushNumber((int)shadowType);
			if (ScriptedEntity.Call(1, 1)) {
				ILuaObject ret = g_Lua.GetReturn(0);
				if (ret.GetType() == LuaType.Vector) {
					direction = ret.GetVector();
					return true;
				}
			}
		}
		return base.GetShadowCastDirection(ref direction, shadowType);
	}

	public override void ClientThink() {
		if (!IsMarkedForDeletion() && !ScriptedEntity.CallFunction((int)LUA_POOLEDSTRING.Think, true))
			SetNextClientThink(gpGlobals.CurTime + 0.2);
	}
#endif

	public override bool KeyValue(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) {
		if (stricmp(keyName, "ScriptName") == 0)
			ScriptedEntity.SetScriptName(value);
		if (ScriptedEntity.StartFunction((int)LUA_POOLEDSTRING.KeyValue)) {
			g_Lua!.PushString(keyName);
			g_Lua.PushString(value);
			ScriptedEntity.Call(2, 0);
		}
		return base.KeyValue(keyName, value);
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
