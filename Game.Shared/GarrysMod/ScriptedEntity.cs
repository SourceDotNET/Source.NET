#if CLIENT_DLL || GAME_DLL
using Source;
using Source.Common;
using Source.Common.GarrysMod.Lua;

using static Game.Client.GarrysMod.GarrysMod;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
using FIELD = Source.FIELD<Game.Client.GarrysMod.ScriptedEntity>;
#else
namespace Game.Server.GarrysMod;

using FIELD = Source.FIELD<Game.Server.GarrysMod.ScriptedEntity>;
#endif

public class ScriptedEntity
{
#if CLIENT_DLL
	public static readonly RecvTable DT_ScriptedEntity = new(nameof(DT_ScriptedEntity), [
		RecvPropString(FIELD.OF(nameof(ScriptName)))
	]);
#else
	public static readonly SendTable DT_ScriptedEntity = new(nameof(DT_ScriptedEntity), [
		SendPropString(FIELD.OF(nameof(ScriptName)))
	]);
#endif

	public bool Started;
	public int EntityType;
	public BaseEntity? ParentEntity;
	[NetworkName("m_strScriptName")]
	public InlineArray64<char> ScriptName;
#if GAME_DLL
	public string? ScriptNamePooled;
#endif
	public InlineArray64<char> ModuleName;
	public InlineArray64<char> SelfName;

	public ScriptedEntity() {
		ScriptName[0] = '\0';
#if GAME_DLL
		ScriptNamePooled = null;
#endif
		strcpy(ModuleName, "scripted_ents");
		strcpy(SelfName, "Entity");
		SetScriptName("");
#if GAME_DLL
		ScriptNamePooled = null;
#endif
		EntityType = 0;
		Started = false;
	}

	public virtual void NetworkStateChanged() {
#if GAME_DLL
		ParentEntity?.NetworkStateChanged();
#endif
	}

	public void Init(BaseEntity parent, int type) {
		ParentEntity = parent;
		EntityType = type;
	}

	public void SetModuleName(ReadOnlySpan<char> name) => strcpy(ModuleName, name);
	public void SetSelfName(ReadOnlySpan<char> name) => strcpy(SelfName, name);
	public ReadOnlySpan<char> GetScriptName() => ((ReadOnlySpan<char>)ScriptName).SliceNullTerminatedString();

	public void SetScriptName(ReadOnlySpan<char> name) {
		Span<char> lowered = stackalloc char[64];
		strcpy(lowered, name);
		strlower(lowered);
		NetworkStateChanged();
		strcpy(ScriptName, lowered);
#if GAME_DLL
		ScriptNamePooled = new string(((ReadOnlySpan<char>)ScriptName).SliceNullTerminatedString());
#endif
	}

	public bool Call(int args, int rets) {
		if (rets == 0) {
			g_Lua!.CallInternalNoReturns(args + 1);
			return true;
		}
		return g_Lua!.CallInternal(args + 1, rets);
	}

	public bool StartFunction(int pooledName) {
		if (!ThreadInMainThread() || !Started)
			return false;

		ParentEntity!.GetLuaTable()!.Push();
		g_Lua!.PushPooledString(pooledName);
		g_Lua.GetTable(-2);
		g_Lua.Remove(-2);
		if (g_Lua.GetType(-1) == LuaType.Function) {
			LuaEntity.Push_Entity(ParentEntity);
			return true;
		}
		g_Lua.Pop(1);
		return false;
	}

	public bool StartFunction(ReadOnlySpan<char> name) {
		if (!ThreadInMainThread() || !Started)
			return false;

		ParentEntity!.GetLuaTable()!.Push();
		g_Lua!.PushString(name);
		g_Lua.GetTable(-2);
		g_Lua.Remove(-2);
		if (g_Lua.GetType(-1) == LuaType.Function) {
			LuaEntity.Push_Entity(ParentEntity);
			return true;
		}
		g_Lua.Pop(1);
		return false;
	}

	public bool CallFunction(int pooledName, bool getBool) {
		if (!Started)
			return false;

		ParentEntity!.GetLuaTable()!.Push();
		g_Lua!.PushPooledString(pooledName);
		g_Lua.GetTable(-2);
		g_Lua.Remove(-2);
		if (g_Lua.GetType(-1) == LuaType.Function) {
			LuaEntity.Push_Entity(ParentEntity);
			if (getBool)
				return g_Lua.CallInternalGetBool(1);
			g_Lua.CallInternalNoReturns(1);
			return false;
		}
		g_Lua.Pop(1);
		return false;
	}

	public bool CallFunction(ReadOnlySpan<char> name, bool getBool) {
		if (!Started)
			return false;

		bool ret;
		LuaObject func = new();
		ParentEntity!.GetLuaTable()!.GetMember(name, func);
		if (!func.isFunction()) {
			LuaEntity.LC_Entity.MetaTable.GetMember(name, func);
			if (!func.isFunction()) {
				func.UnReference();
				return false;
			}
		}

		func.Push();
		LuaEntity.Push_Entity(ParentEntity);
		if (!getBool) {
			g_Lua!.CallInternalNoReturns(1);
			ret = false;
		}
		else
			ret = g_Lua!.CallInternalGetBool(1);
		func.UnReference();
		return ret;
	}

	public void InitializeDataTables() {
		if (!Started)
			return;

		LuaObject setupDataTables = new();
		ParentEntity!.GetLuaTable()!.GetMember("SetupDataTables", setupDataTables);
		if (setupDataTables.isFunction()) {
			CallFunction("InstallDataTable", true);
			CallFunction("SetupDataTables", true);
		}
		setupDataTables.UnReference();
	}

#if CLIENT_DLL
	static void LuaEntityCreated(BaseEntity? ent) {
		// todo
	}
#endif

	public void StartLua() {
		if (g_Lua == null || g_LuaManager == null || Started || ScriptName[0] == '\0')
			return;

		Started = true;
		if (ParentEntity == null)
			Error("CScriptedEntity with no m_ParentEntity");

		if (ParentEntity.GetLuaEntity() == null)
			Error("CScriptedEntity with invalid m_ParentEntity->GetLuaEntity()");

		if (!ParentEntity.GetLuaEntity()!.isEntity()) {
			Warning("CScriptedEntity: Entity isn't entity!\n");
			return;
		}

		if (!ParentEntity.GetLuaTable()!.isTable()) {
			Warning("CScriptedEntity: Table isn't table!\n");
			return;
		}

		ReadOnlySpan<char> scriptName = GetScriptName();
		ReadOnlySpan<char> moduleName = ((ReadOnlySpan<char>)ModuleName).SliceNullTerminatedString();

		LuaObject module = new();
		module.SetFromGlobal(moduleName);
		if (!module.isTable()) {
			g_Lua.ErrorFromLua($"Error: Scripted Entity {scriptName} couldn't be created! '{moduleName}' library doesn't exist or is not a table!\n");
			module.UnReference();
			return;
		}

		LuaObject get = new();
		module.GetMember("Get", get);
		if (!get.isFunction()) {
			g_Lua.ErrorFromLua($"Error: Scripted Entity {scriptName} couldn't be created! {moduleName}.Get() doesn't exist or is not a function!\n");
			get.UnReference();
			module.UnReference();
			return;
		}

		get.Push();
		g_Lua.PushString(scriptName);
		if (ParentEntity.HasLuaTable())
			ParentEntity.GetLuaTable()!.Push();
		else
			g_Lua.PushNil();

		LuaObject table = new();
		if (!g_Lua.CallInternalGet(2, table)) {
			g_Lua.ErrorFromLua($"Error: Scripted Entity {scriptName} couldn't be created! {moduleName}.Get() failed!\n");
			Started = false;
		}
		else {
			if (!table.isTable()) {
				Warning($"Error creating '{scriptName}' (Make sure the file is AddCSLuaFile'd and there aren't any errors!)\n");
				LuaTable empty = new(null, 0);
				table.Set(empty);
				empty.UnReference();
			}

			ParentEntity.SetLuaTable(table);
			ParentEntity.GetLuaTable()!.SetMemberEntity(((ReadOnlySpan<char>)SelfName).SliceNullTerminatedString(), ParentEntity);
#if CLIENT_DLL
			LuaEntityCreated(ParentEntity);
#endif
			ParentEntity.Lua_OnEntityInitialized();
			InitializeDataTables();
#if CLIENT_DLL
			if (ParentEntity.EntIndex() != -1) {
				C_BaseEntity.SetAllowPrecache(true);
				CallFunction("Initialize", true);
				C_BaseEntity.SetAllowPrecache(false);
			}
#endif
		}

		table.UnReference();
		get.UnReference();
		module.UnReference();
	}
}
#endif
