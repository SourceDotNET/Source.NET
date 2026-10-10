#if CLIENT_DLL || GAME_DLL
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
using Game.Client.GarrysMod;

namespace Game.Client;
#else
using Game.Server.GarrysMod;

namespace Game.Server;
#endif

public partial class
#if CLIENT_DLL
	C_BaseEntity
#else
	BaseEntity
#endif
{
	public LuaObject LuaCalcAbsolutePosition = new();
	public LuaObject? LuaTableObject = new();
	public LuaObject? LuaEntityObject = new();

	public virtual bool IsWeapon() => false;
	public virtual bool IsVehicle() => false;
	public virtual bool IsRagdollProp() => false;
	public virtual bool IsJeep() => false;
	public virtual void PostLuaSpawn() { }
	public virtual LuaClass Lua_GetLuaClass() => LuaEntity.LC_Entity;

	public virtual void PushEntity() {
		if (g_Lua == null) {
			Msg("CBaseEntity::PushEntity !g_Lua\n");
			return;
		}

		LuaObject? luaEntity = GetLuaEntity();
		if (luaEntity != null) {
			luaEntity.Push();
			return;
		}

		Msg("CBaseEntity::PushEntity !GetLuaEntity)\n");
		g_Lua.PushNil();
	}

	public virtual bool HasLuaTable() => LuaTableObject != null && LuaTableObject.isTable();

	public virtual LuaObject? GetLuaEntity() {
		if (g_Lua == null)
			return null;

		if (LuaEntityObject == null) {
			Warning("CBaseEntity::GetLuaEntity called too soon?\n");
			return null;
		}

		if (LuaEntityObject.isEntity())
			return LuaEntityObject;

		ILuaObject meta = LuaEntity.FindEntityMetaTable(this);
		g_Lua.PushValueUserType(GetRefEHandle().Index, LuaType.Entity);
		meta.Push();
		g_Lua.SetMetaTable(-2);
		LuaEntityObject.SetFromStack(-1);
		g_Lua.Pop(1);

		if (LuaEntityObject.GetType() != LuaType.Entity)
			Error("m_LuaEntity != ENTITY!");

		if (!HasLuaTable()) {
			LuaTable table = new(null, 0);
			SetLuaTable(table);
			table.UnReference();
		}

		return LuaEntityObject;
	}

	public virtual LuaObject? GetLuaTable() {
		if (!ThreadInMainThread())
			Warning("Entity Lua table accessed in a thread! Probably will crash soon!\n");

		if (!LuaTableObject!.isTable() && GetLuaEntity() == null)
			return null;

		if (!LuaTableObject.isTable())
			Error($"GetLuaTable != TABLE! Type is: {(int)LuaTableObject.GetType()}");

		return LuaTableObject;
	}

	public virtual void SetLuaTable(ILuaObject? table) {
		if (table == null || !table.isTable()) {
			Warning("SetLuaTable: Wasn't a table!\n");
			return;
		}

		LuaTableObject!.Set(table);
		if (LuaTableObject.isTable())
			LuaTableObject.GetMember("CalcAbsolutePosition", LuaCalcAbsolutePosition);
	}
}
#endif
