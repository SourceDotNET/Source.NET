#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.InteropServices;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public class LuaEntityClass(string name, LuaType type, Action? initFn, string? derivedFrom) : LuaClass(name, type, initFn, derivedFrom)
{
	public override void MetaTableDerive() {
		if (DerivedFrom == null)
			return;

		ILuaObject? baseMeta = g_Lua!.GetMetaTableObject(DerivedFrom, -1);
		MetaTable.SetMember("MetaBaseClass", baseMeta);
		MetaTable.SetMember("__index", LuaEntity.__index_Entity);

		LuaObject gc = new();
		MetaTable.GetMember("__gc", gc);
		if (gc.isNil()) {
			baseMeta!.GetMember("__gc", gc);
			if (!gc.isNil())
				MetaTable.SetMember("__gc", gc);
		}
		gc.UnReference();

		LuaObject newindex = new();
		MetaTable.GetMember("__newindex", newindex);
		if (newindex.isNil()) {
			baseMeta!.GetMember("__newindex", newindex);
			if (!newindex.isNil())
				MetaTable.SetMember("__newindex", newindex);
		}
		newindex.UnReference();
	}
}

public static partial class LuaEntity
{
	[LuaClass]
	public static readonly LuaClass LC_Entity = new("Entity", LuaType.Entity, null, null);

	public static readonly LuaEntityClass LC_NPC = new("NPC", LuaType.Entity, null, "Entity");
	public static readonly LuaEntityClass LC_Player = new("Player", LuaType.Entity, null, "Entity");
	public static readonly LuaEntityClass LC_Vehicle = new("Vehicle", LuaType.Entity, null, "Entity");
	public static readonly LuaEntityClass LC_Weapon = new("Weapon", LuaType.Entity, null, "Entity");

	static bool bWarning;

	public static BaseEntity? GetEntityFromHandle(uint handle) {
		if (handle == 0xFFFFFFFF)
			return null;
#if CLIENT_DLL
		return cl_entitylist.GetBaseEntityFromHandle(new BaseHandle(handle));
#else
		return gEntList.GetBaseEntity(new BaseHandle(handle));
#endif
	}

	public static BaseEntity? GetEntityFromUserData(nint data) {
		if (data == 0)
			return null;
		return GetEntityFromHandle((uint)Marshal.ReadInt32(data));
	}

	public static BaseEntity? UserGet(int stackPos) {
		if (!g_Lua!.IsType(stackPos, LuaType.Entity))
			return null;

		nint ud = g_Lua.GetUserdata(stackPos);
		if (ud == 0)
			return null;

		return GetEntityFromUserData(Marshal.ReadIntPtr(ud));
	}

	public static BaseEntity? Get_Entity(int stackPos, bool allowNull) {
		LuaType type = g_Lua!.GetType(stackPos);
		if (type != LuaType.Entity && (!allowNull || type != LuaType.Nil))
			g_Lua.TypeError("Entity", stackPos);

		BaseEntity? ent = UserGet(stackPos);
		if (ent == null && !allowNull)
			g_Lua.Error("Tried to use a NULL entity!");

		return ent;
	}

	public static void Push_Entity(BaseEntity? ent) {
		if (g_Lua == null || g_Lua.Global() == null)
			return;

		if (ent != null) {
			ent.PushEntity();
			return;
		}

		LuaObject NULL = new();
		g_Lua.Global().GetMember("NULL", NULL);
		if (NULL.GetType() != LuaType.Entity)
			Warning("Global 'NULL' is not an entity! It has been replaced somehow\n");
		NULL.Push();
		NULL.UnReference();
	}

	public static ILuaObject FindEntityMetaTable(BaseEntity? ent) {
		if (ent == null)
			return LC_Entity.MetaTable;
		if (ent.IsPlayer())
			return LC_Player.MetaTable;
		if (ent.IsNPC())
			return LC_NPC.MetaTable;
		if (ent.IsVehicle())
			return LC_Vehicle.MetaTable;
		return ent.Lua_GetLuaClass().MetaTable;
	}

	public static void MakeLuaNULLEntity() {
		LuaObject NULL = new();
		g_Lua!.PushUserType(0, LuaType.Entity);
		LC_Entity.MetaTable.Push();
		g_Lua.SetMetaTable(-2);
		NULL.SetFromStack(-1);
		g_Lua.Pop(1);
		g_Lua.Global().SetMember("NULL", NULL);
		NULL.UnReference();
	}

	static int EntityBaseIndex() {
		if (LC_Entity.MetaTable.PushMemberFast(2))
			return 1;

		BaseEntity? ent = Get_Entity(1, true);
		if (ent != null) {
			ILuaObject? table = ent.GetLuaTable();
			if (table != null && table.PushMemberFast(2))
				return 1;

			if (ent.IsWeapon()) {
				string? key = g_Lua!.CheckString(2);
				if (key != null && key.Equals("owner", StringComparison.OrdinalIgnoreCase)) {
					Push_Entity(((BaseCombatWeapon)ent).GetOwner());
					return 1;
				}
			}
		}

		string? str = g_Lua!.CheckString(2);
		if (str != null && str == "Entity") {
			if (!bWarning) {
				Warning($"[Deprecated] Entity.Entity [{g_Lua.GetCurrentLocation()}]\n");
				bWarning = true;
			}
			Push_Entity(ent);
			return 1;
		}

		return 0;
	}

	public static int __index_Entity(ILuaInterface lua) {
		if (lua.FindOnObjectsMetaTable(1, 2))
			return 1;
		return EntityBaseIndex();
	}

	[LuaMethod]
	static int Entity____index(ILuaInterface lua) => EntityBaseIndex();

	[LuaMethod]
	static int Entity__IsValid(ILuaInterface lua) {
		BaseEntity? ent = Get_Entity(1, true);
		if (ent != null) {
#if CLIENT_DLL
			lua.PushBool(ent != C_World.GetClientWorldEntity());
#else
			lua.PushBool(ent != GetWorldEntity());
#endif
			return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	// todo: populate from Entity:Remove
	public static readonly List<EHANDLE> RemovedEntities = [];

	public static bool IsEntityRemoving(BaseEntity ent, bool ignoreRemoved) {
		if (!ignoreRemoved) {
			foreach (EHANDLE handle in RemovedEntities) {
				if (handle.Get() == ent)
					return true;
			}
		}
		return ent.IsMarkedForDeletion();
	}

	[LuaMethod]
	static int Entity__SetBodyGroups(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsEntityRemoving(ent, false))
			return 0;

		string? groups = lua.CheckString(2);
		if (groups == null)
			return 0;

		for (int i = 0; i < groups.Length; i++) {
			char c = groups[i];
			int value = -1;
			if (c >= '0' && c <= '9')
				value = c - '0';
			if (c >= 'a' && c <= 'z')
				value = c - 'a' + 10;
			if (c >= 'A' && c <= 'Z')
				value = c - 'A' + 10;
			if (value >= 0)
				animating.SetBodygroup(i, value);
		}
		return 0;
	}

	[LuaMethod]
	static int Entity__LookupAttachment(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating != null && !ent.IsMarkedForDeletion()) {
			lua.PushNumber(animating.LookupAttachment(lua.CheckString(2)));
			return 1;
		}
		lua.PushNumber(0);
		return 1;
	}

	[LuaMethod]
	static int Entity__SelectWeightedSequence(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsEntityRemoving(ent, false))
			return 0;

		if ((uint)(int)lua.CheckNumber(2) > int.MaxValue)
			lua.ArgError(2, "invalid act");

		lua.PushNumber(animating.SelectWeightedSequence((Activity)(int)lua.CheckNumber(2)));
		return 1;
	}

	[LuaMethod]
	static int Entity__GetModel(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (ent.IsWeapon() && ent is BaseCombatWeapon weapon) {
			string worldModel = new string(weapon.GetWorldModel()).ToLowerInvariant();
			lua.PushString(worldModel);
			return 1;
		}

#if CLIENT_DLL
		ReadOnlySpan<char> name = ent.GetModelName();
		if (name.IsEmpty) {
			Model? model = ent.GetModel();
			if (model != null)
				name = modelinfo.GetModelName(model);
		}

		if (name.IsEmpty) {
			C_BaseAnimating? animating = ent.GetBaseAnimating();
			if (animating != null && !ent.IsMarkedForDeletion()) {
				StudioHdr? hdr = animating.GetModelPtr();
				if (hdr != null) {
					string path = $"models/{hdr.Name()}".Replace('\\', '/');
					name = path;
				}
			}
		}

		if (name.IsEmpty)
			return 0;
#else
		ReadOnlySpan<char> name = ent.GetModelName();
		if (name.IsEmpty)
			return 0;
#endif
		lua.PushString(new string(name).ToLowerInvariant());
		return 1;
	}

#if CLIENT_DLL
	[LuaMethod]
	static int Entity__GetRenderBounds(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ent.GetRenderBounds(out Vector3 mins, out Vector3 maxs);
		LuaVector.Push_Vector(mins);
		LuaVector.Push_Vector(maxs);
		return 2;
	}
#endif

	[LuaMethod]
	static int Entity__NearestPoint(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ref Vector3 point = ref LuaVector.Get_Vector(2);
		ent.CollisionProp().CalcNearestPoint(point, out Vector3 nearest);
		LuaVector.Push_Vector(nearest);
		return 1;
	}

	[LuaMethod]
	static int Entity__Spawn(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (IsEntityRemoving(ent, false))
			return 0;
		if (!ent.IsPlayer() && LuaEnts.IsProtectedEntity(ent))
			return 0;

		Handle<BaseEntity> handle = ent.GetRefEHandle();
#if CLIENT_DLL
		ent.Spawn();
#else
		Util.DispatchSpawn(ent);
#endif
		if (handle.Get() != null)
			ent.PostLuaSpawn();
		return 0;
	}

	[LuaMethod]
	static int Entity__Activate(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (!IsEntityRemoving(ent, false))
			ent.Activate();
		return 0;
	}

	[LuaMethod]
	static int Entity__SetPos(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ref Vector3 pos = ref LuaVector.Get_Vector(2);
		float max = MAX_COORD_FLOAT * 8;
		if (!(-max < pos.X && pos.X < max && -max < pos.Y && pos.Y < max && -max < pos.Z && pos.Z < max)) {
			Warning($"{ent.GetClassname()}[{ent.EntIndex()}]:SetPos( {pos.X:F6} {pos.Y:F6} {pos.Z:F6} ): Ignoring unreasonable position.\n");
			return 0;
		}

		ent.SetAbsOrigin(pos);
		if (ent.GetMoveType() == MoveType.None || ent.VPhysicsGetObject() == null || ent.IsRagdollProp() || ent.IsJeep())
			return 0;
#if CLIENT_DLL
		IPhysicsObject phys = ent.VPhysicsGetObject()!;
		phys.GetPosition(out _, out QAngle angles);
		phys.SetPosition(pos, angles, true);
#else
		ent.Teleport(pos, null, null);
#endif
		return 0;
	}

	[LuaMethod]
	static int Entity__SetAngles(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ref QAngle angles = ref LuaAngle.Get_Angle(2);
		float max = 360000f * 360000f;
		if (!(-max < angles.X && angles.X < max && -max < angles.Y && angles.Y < max && -max < angles.Z && angles.Z < max)) {
			Warning($"{ent.GetClassname()}[{ent.EntIndex()}]:SetAngles( {angles.X:F6} {angles.Y:F6} {angles.Z:F6} ): Ignoring unreasonable angles.\n");
			return 0;
		}

		ent.SetAbsAngles(angles);
#if GAME_DLL
		if (ent.IsJeep())
			ent.Teleport(null, angles, null);
#endif
		if (ent.GetMoveType() == MoveType.None || ent.VPhysicsGetObject() == null || ent.IsRagdollProp() || ent.IsJeep())
			return 0;
		IPhysicsObject phys = ent.VPhysicsGetObject()!;
		phys.GetPosition(out Vector3 pos, out _);
		phys.SetPosition(pos, angles, true);
		return 0;
	}

	[LuaMethod]
	static int Entity__SetSkin(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsEntityRemoving(ent, false))
			return 0;

		int skin = (int)lua.CheckNumber(2);
		if (animating.Skin != skin)
			animating.Skin = skin;
		return 0;
	}

	[LuaMethod]
	static int Entity__SetModel(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		string? modelName = lua.CheckString(2);
		if (string.IsNullOrEmpty(modelName) || (modelName[0] & 0xDF) == 0 || modelName.Length < 2)
			return 0;
		if (!stristr(modelName, ".bsp").IsEmpty)
			return 0;
		if (stristr(modelName, ".mdl").IsEmpty && modelName[0] != '*')
			return 0;

#if CLIENT_DLL
		engine.LoadModel(modelName, true);
		Model? model = modelinfo.FindOrLoadModel(modelName);
		ent.SetModel(modelName);
		Vector3 mins = vec3_origin, maxs = vec3_origin;
		if (model != null) {
			ent.SetModelPointer(model);
			ent.ModelName = modelName;
			modelinfo.GetModelBounds(model, out mins, out maxs);
		}
		ent.SetCollisionBounds(mins, maxs);
#else
		Util.SetModel(ent, modelName);
		ent.SetModel(modelName);
#endif

		MDLHandle_t handle = mdlcache.FindMDL(ent.GetModelName());
		if (handle == MDLHANDLE_INVALID || mdlcache.IsErrorModel(handle))
			Warning($"Model missing: {modelName}\n");
		mdlcache.Release(handle);
		return 0;
	}

	[LuaMethod]
	static int Entity__GetPos(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		LuaVector.Push_Vector(ent.GetAbsOrigin());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetForward(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ent.GetVectors(out Vector3 forward, out _, out _);
		LuaVector.Push_Vector(forward);
		return 1;
	}

	[LuaMethod]
	static int Entity__EyeAngles(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		LuaAngle.Push_Angle(ent.EyeAngles());
		return 1;
	}

	[LuaMethod]
	static int Entity__EyePos(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		LuaVector.Push_Vector(ent.EyePosition());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetClass(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ReadOnlySpan<char> classname = ent.GetClassname();
		if (strcmp(classname, "prop_vehicle_jeep_old") == 0) {
			lua.PushString("prop_vehicle_jeep");
			return 1;
		}
		if (classname.Length >= 3 && classname[0] == 'a' && classname[1] == 'i' && classname[2] == '_')
			classname = classname[3..];
		lua.PushString(new string(classname));
		return 1;
	}

	[LuaMethod]
	static int Entity__EntIndex(ILuaInterface lua) {
		LuaType type = lua.GetType(1);
		if (type != LuaType.Entity && type != LuaType.Nil)
			lua.TypeError("Entity", 1);

		BaseEntity? ent = UserGet(1);
		if (ent == null) {
			lua.PushNumber(0);
			return 1;
		}

		lua.PushNumber(ent.EntIndex());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetTable(ILuaInterface lua) {
		LuaType type = lua.GetType(1);
		if (type != LuaType.Entity && type != LuaType.Nil)
			lua.TypeError("Entity", 1);

		BaseEntity? ent = UserGet(1);
		if (ent == null)
			return 0;

		ILuaObject? table = ent.GetLuaTable();
		if (table == null)
			return 0;

		table.Push();
		return 1;
	}

	[LuaMethod]
	static int Entity____newindex(ILuaInterface lua) {
		BaseEntity? ent = Get_Entity(1, true);
		if (ent == null)
			return 0;

		ILuaObject? table = ent.GetLuaTable();
		if (table == null || !table.isTable())
			return 0;

		table.SetMemberFast(2, 3);
		if (g_Lua!.GetType(2) != LuaType.String || lua.GetString(2) != "CalcAbsolutePosition")
			return 0;

		if (lua.GetType(3) == LuaType.Function)
			ent.LuaCalcAbsolutePosition.SetFromStack(3);
		else
			ent.LuaCalcAbsolutePosition.UnReference();
		return 0;
	}
}
#endif
