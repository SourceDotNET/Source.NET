using Source.Common.MaterialSystem;
#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
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

	static readonly List<EHANDLE> RemoveList = [];
	static bool ProcessingRemoveList;

	public static void RemoveQueuedEntities() {
		ProcessingRemoveList = true;
		for (int i = 0; i < RemoveList.Count; i++) {
			BaseEntity? ent = RemoveList[i].Get();
			if (ent != null)
				Util.Remove(ent);
		}
		RemoveList.Clear();
		ProcessingRemoveList = false;
	}

	[LuaMethod]
	static int Entity__Remove(ILuaInterface lua) {
		if (LuaEnts.IsRendering()) {
			lua.ErrorNoHalt("Entity:Remove cannot be called while rendering\n");
			return 0;
		}

		BaseEntity ent = Get_Entity(1, false)!;
		if (LuaEnts.IsProtectedEntity(ent) || IsQueuedForRemoval(ent))
			return 0;
		if (ent.IsMarkedForDeletion())
			return 0;
#if CLIENT_DLL
		if (ent.EntIndex() != -1) {
			lua.ErrorNoHalt($"Trying to remove server entity {ent.GetClassname()}#{ent.EntIndex()} on client!\n");
			return 0;
		}
#endif
		if (ent.VPhysicsGetObject() == null || !PhysIsInCallback())
		// TODO!
			// if (ent.VPhysicsGetObject() == null || (!PhysIsInCallback() && !ent.GMOD_VPhysicsTest(ent.VPhysicsGetObject(), true)))
			ent.AddSolidFlags(SolidFlags.NotSolid);
		ent.AddEffects(EntityEffects.NoDraw);
		RemoveList.Add(new(ent.GetRefEHandle()));
		return 0;
	}

	static bool IsQueuedForRemoval(BaseEntity ent) {
		foreach (Handle<BaseEntity> handle in RemoveList) {
			if (handle.Get() == ent)
				return true;
		}
		return false;
	}

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

	public static BaseCombatWeapon? Get_Weapon(int stackPos, bool allowNull) {
		BaseEntity? ent = Get_Entity(stackPos, allowNull);
		if (ent != null && ent.IsBaseCombatWeapon())
			return (BaseCombatWeapon)ent;
		if (!allowNull)
			g_Lua!.Error("Weapon is NULL/Not a Weapon");
		return null;
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

#if GAME_DLL
	public static BaseEntity? GMEntityByIndex(int index) {
		if (index == 0)
			return GetWorldEntity();
		return Util.EntityByIndex(index);
	}
#endif

	[LuaGlobal]
	static int Entity(ILuaInterface lua) {
#if CLIENT_DLL
		Push_Entity(cl_entitylist.GetBaseEntity((int)lua.CheckNumber(1)));
#else
		Push_Entity(GMEntityByIndex((int)lua.CheckNumber(1)));
#endif
		return 1;
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
	static int Entity____tostring(ILuaInterface lua) {
		BaseEntity? ent = Get_Entity(1, true);
		if (ent == null) {
			lua.PushString("[NULL Entity]");
			return 1;
		}

		string str = $"Entity [{ent.EntIndex()}][{ent.GetClassname()}]";
		lua.PushString(str.Length > 0xFF ? str[..0xFF] : str);
		return 1;
	}

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

#if GAME_DLL
	[LuaMethod]
	static int Entity__DeleteOnRemove(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ent.DeleteOnRemove(Get_Entity(2, false));
		return 0;
	}
#endif

	[LuaMethod]
	static int Entity__Health(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		lua.PushNumber(ent.Health);
		return 1;
	}

	[LuaMethod]
	static int Entity__GetMaxHealth(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		lua.PushNumber(ent.GetMaxHealth());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetFlags(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		lua.PushNumber((int)ent.GetFlags());
		return 1;
	}

	[LuaMethod]
	static int Entity__IsDormant(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		lua.PushBool(ent.IsDormant());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetCollisionGroup(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		lua.PushNumber((int)ent.GetCollisionGroup());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetNoDraw(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		lua.PushBool(ent.IsEffectActive(EntityEffects.NoDraw));
		return 1;
	}

	[LuaMethod]
	static int Entity__WorldSpaceCenter(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		LuaVector.Push_Vector(ent.WorldSpaceCenter());
		return 1;
	}

	[LuaMethod]
	static int Entity__OBBMins(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (ent.CollisionProp() == null) {
			LuaVector.Push_Vector(vec3_origin);
			return 1;
		}
		LuaVector.Push_Vector(ent.CollisionProp().OBBMins());
		return 1;
	}

	[LuaMethod]
	static int Entity__OBBMaxs(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (ent.CollisionProp() == null) {
			LuaVector.Push_Vector(vec3_origin);
			return 1;
		}
		LuaVector.Push_Vector(ent.CollisionProp().OBBMaxs());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetMoveParent(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		Push_Entity(ent.GetMoveParent());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetRight(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ent.GetVectors(out _, out Vector3 right, out _);
		LuaVector.Push_Vector(right);
		return 1;
	}

	[LuaMethod]
	static int Entity__IsWeapon(ILuaInterface lua) {
		lua.PushBool(false);
		return 1;
	}

	[LuaMethod]
	static int Entity__IsVehicle(ILuaInterface lua) {
		lua.PushBool(false);
		return 1;
	}

	[LuaMethod]
	static int Entity__IsNextBot(ILuaInterface lua) {
		lua.PushBool(false);
		return 1;
	}

	[LuaMethod]
	static int Entity__AddEffects(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ent.AddEffects((EntityEffects)(int)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Entity__DrawShadow(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (lua.GetBool(2)) {
			ent.RemoveEffects(EntityEffects.NoShadow);
			return 0;
		}
		ent.AddEffects(EntityEffects.NoShadow);
		return 0;
	}

	[LuaMethod]
	static int Entity__SetMoveType(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		float moveType = (int)lua.GetNumber(2);
		if (moveType <= 0.0f)
			moveType = 0.0f;
		if (11.0f <= moveType)
			moveType = 11.0f;
		ent.SetMoveType((MoveType)(int)moveType, MoveCollide.Default);
		return 0;
	}

	[LuaMethod]
	static int Entity__SetNotSolid(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		SolidFlags flags;
		if (!lua.GetBool(2))
			flags = ent.GetSolidFlags() & ~SolidFlags.NotSolid;
		else
			flags = ent.GetSolidFlags() | SolidFlags.NotSolid;
		ent.CollisionProp().SetSolidFlags(flags);
		return 0;
	}

	[LuaMethod]
	static int Entity__SetTransmitWithParent(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
#if GAME_DLL
		if (lua.GetBool(2)) {
			ent.NetworkStateChanged();
			ent.GModFlags |= 1 << 6; // TODO, need an enum
			return 0;
		}
		ent.NetworkStateChanged();
		ent.GModFlags &= ~(1 << 6);
#else
		ent.GModFlags = lua.GetBool(2) ? ent.GModFlags | 1 << 6 : ent.GModFlags & ~(1 << 6);
#endif
		return 0;
	}

	[LuaMethod]
	static int Entity__SetParent(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		LuaType type = lua.GetType(2);
		if (type != LuaType.Entity && type != LuaType.Nil)
			lua.TypeError("Entity", 2);
		BaseEntity? parent = UserGet(2);
		if (ent == parent)
			return 0;
#if CLIENT_DLL
		int attachment;
		if (lua.GetType(3) == LuaType.Number) {
			attachment = (int)lua.GetNumber(3);
			if ((uint)(attachment + 1) > 256)
				lua.ErrorFromLua("Invalid attachment supplied to Entity.SetParent, must be 0 or above, and less than the attachment count.");
		}
		else
			attachment = 0;
		ent.SetParent(parent, attachment);
#else
		if (lua.GetType(3) == LuaType.Number) {
			int attachment = (int)lua.GetNumber(3);
			if ((uint)(attachment + 1) > 256)
				lua.ErrorFromLua("Invalid attachment supplied to Entity.SetParent, must be 0 or above, and less than the attachment count.");
			ent.SetParent(parent, attachment);
			return 0;
		}
		ent.SetParent(parent, -1);
#endif
		return 0;
	}

	[LuaMethod]
	static int Entity__GetOwner(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseEntity? owner;
		if (ent is BaseViewModel viewModel)
			owner = viewModel.GetOwner();
		else
			owner = ent.GetOwnerEntity();
		Push_Entity(owner);
		return 1;
	}

	[LuaMethod]
	static int Entity__SetOwner(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		LuaType type = lua.GetType(2);
		if (type != LuaType.Entity && type != LuaType.Nil)
			lua.TypeError("Entity", 2);
		ent.SetOwnerEntity(UserGet(2));
		return 0;
	}

	[LuaMethod]
	static int Entity__SetDTBool(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		bool value = lua.GetBool(3);
		int index = (int)lua.GetNumber(2);
		if ((uint)index < 32 && ent.GMOD_bool[index] != value) {
#if GAME_DLL
			ent.NetworkStateChanged();
#endif
			ent.GMOD_boolForModify()[index] = value;
		}
		return 0;
	}

	[LuaMethod]
	static int Entity__GetDTBool(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		int index = (int)lua.GetNumber(2);
		lua.PushBool(ent.GMOD_bool[(uint)index < 32 ? index : 0]);
		return 1;
	}

	[LuaMethod]
	static int Entity__SetDTFloat(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		float value = (float)lua.GetNumber(3);
		int index = (int)lua.GetNumber(2);
		if ((uint)index < 32 && ent.GMOD_float[index] != value) {
#if GAME_DLL
			ent.NetworkStateChanged();
#endif
			ent.GMOD_floatForModify()[index] = value;
		}
		return 0;
	}

	[LuaMethod]
	static int Entity__GetDTFloat(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		int index = (int)lua.GetNumber(2);
		lua.PushNumber(ent.GMOD_float[(uint)index < 32 ? index : 0]);
		return 1;
	}

	[LuaMethod]
	static int Entity__SetDTInt(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		int value = (int)lua.GetNumber(3);
		int index = (int)lua.GetNumber(2);
		if ((uint)index < 32 && ent.GMOD_int[index] != value) {
#if GAME_DLL
			ent.NetworkStateChanged();
#endif
			ent.GMOD_intForModify()[index] = value;
		}
		return 0;
	}

	[LuaMethod]
	static int Entity__GetDTInt(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		int index = (int)lua.GetNumber(2);
		lua.PushNumber(ent.GMOD_int[(uint)index < 32 ? index : 0]);
		return 1;
	}

	[LuaMethod]
	static int Entity__SetDTVector(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		Vector3 value = LuaVector.Get_Vector(3);
		int index = (int)lua.GetNumber(2);
		if ((uint)index < 32 && ent.GMOD_Vector[index] != value) {
#if GAME_DLL
			ent.NetworkStateChanged();
#endif
			ent.GMOD_VectorForModify()[index] = value;
		}
		return 0;
	}

	[LuaMethod]
	static int Entity__GetDTVector(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		int index = (int)lua.GetNumber(2);
		LuaVector.Push_Vector(ent.GMOD_Vector[(uint)index < 32 ? index : 0]);
		return 1;
	}

	[LuaMethod]
	static int Entity__SetDTAngle(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		QAngle value = LuaAngle.Get_Angle(3);
		int index = (int)lua.GetNumber(2);
		if ((uint)index < 32 && ent.GMOD_QAngle[index] != value) {
#if GAME_DLL
			ent.NetworkStateChanged();
#endif
			ent.GMOD_QAngleForModify()[index] = value;
		}
		return 0;
	}

	[LuaMethod]
	static int Entity__GetDTAngle(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		int index = (int)lua.GetNumber(2);
		LuaAngle.Push_Angle(ent.GMOD_QAngle[(uint)index < 32 ? index : 0]);
		return 1;
	}

	[LuaMethod]
	static int Entity__SetDTEntity(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		LuaType type = lua.GetType(3);
		if (type != LuaType.Entity && type != LuaType.Nil)
			lua.TypeError("Entity", 3);
		BaseEntity? value = UserGet(3);
		int index = (int)lua.GetNumber(2);
		if ((uint)index < 32 && ent.GMOD_EHANDLE[index].Get() != value) {
#if GAME_DLL
			ent.NetworkStateChanged();
#endif
			ent.GMOD_EHANDLEForModify()[index].Set(value);
		}
		return 0;
	}

	[LuaMethod]
	static int Entity__GetDTEntity(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		int index = (int)lua.GetNumber(2);
		Push_Entity(ent.GMOD_EHANDLE[(uint)index < 32 ? index : 0].Get());
		return 1;
	}

	[LuaMethod]
	static int Entity__SetDTString(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		string? value = lua.GetString(3);
		switch ((int)lua.GetNumber(2)) {
			case 0:
#if GAME_DLL
				ent.NetworkStateChanged();
#endif
				strcpy(ent.GMOD_String0ForModify(), value);
				break;
			case 1:
#if GAME_DLL
				ent.NetworkStateChanged();
#endif
				strcpy(ent.GMOD_String1ForModify(), value);
				break;
			case 2:
#if GAME_DLL
				ent.NetworkStateChanged();
#endif
				strcpy(ent.GMOD_String2ForModify(), value);
				break;
			case 3:
#if GAME_DLL
				ent.NetworkStateChanged();
#endif
				strcpy(ent.GMOD_String3ForModify(), value);
				break;
		}
		return 0;
	}

	[LuaMethod]
	static int Entity__GetDTString(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		int index = (int)lua.GetNumber(2);
		if (index == 1)
			lua.PushString(((ReadOnlySpan<char>)ent.GMOD_String1).SliceNullTerminatedString());
		else if (index == 2)
			lua.PushString(((ReadOnlySpan<char>)ent.GMOD_String2).SliceNullTerminatedString());
		else if (index == 3)
			lua.PushString(((ReadOnlySpan<char>)ent.GMOD_String3).SliceNullTerminatedString());
		else
			lua.PushString(((ReadOnlySpan<char>)ent.GMOD_String0).SliceNullTerminatedString());
		return 1;
	}

#if CLIENT_DLL
	[LuaMethod]
	static int Entity__DrawModel(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ent.DrawModel((StudioFlags)(int)lua.CheckNumberOpt(2, (int)StudioFlags.Render));
		return 0;
	}
#endif

	[LuaMethod]
	static int Entity__SetNoDraw(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (lua.GetBool(2))
			ent.AddEffects(EntityEffects.NoDraw);
		else
			ent.RemoveEffects(EntityEffects.NoDraw);
		return 0;
	}

	[LuaMethod]
	static int Entity__IsPlayer(ILuaInterface lua) {
		lua.PushBool(false);
		return 1;
	}

	[LuaMethod]
	static int Entity__IsNPC(ILuaInterface lua) {
		lua.PushBool(false);
		return 1;
	}

	[LuaMethod]
	static int Entity__IsRagdoll(ILuaInterface lua) {
		BaseEntity? ent = Get_Entity(1, true);
		lua.PushBool(ent != null && ent.IsRagdoll());
		return 1;
	}

	[LuaMethod]
	static int Entity__IsScripted(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ReadOnlySpan<char> name = ent.GetLuaScriptName();
		lua.PushBool(!name.IsEmpty && name[0] != '\0');
		return 1;
	}

	[LuaMethod]
	static int Entity__GetSequence(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating != null && !ent.IsMarkedForDeletion()) {
			lua.PushNumber(animating.GetSequence());
			return 1;
		}
		lua.PushNumber(0);
		return 1;
	}

	[LuaMethod]
	static int Entity__GetCycle(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating != null && !ent.IsMarkedForDeletion()) {
			lua.PushNumber((float)animating.GetCycle());
			return 1;
		}
		lua.PushNumber(0);
		return 1;
	}

	[LuaMethod]
	static int Entity__GetPlaybackRate(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating != null && !ent.IsMarkedForDeletion()) {
			lua.PushNumber(animating.GetPlaybackRate());
			return 1;
		}
		lua.PushNumber(0);
		return 1;
	}

	[LuaMethod]
	static int Entity__GetSequenceActivity(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating != null && !ent.IsMarkedForDeletion()) {
			lua.PushNumber((int)animating.GetSequenceActivity((int)lua.GetNumber(2)));
			return 1;
		}
		lua.PushNumber(0);
		return 1;
	}

	[LuaMethod]
	static int Entity__GetSequenceGroundSpeed(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating != null && !ent.IsMarkedForDeletion()) {
			int sequence = (int)lua.GetNumber(2);
			lua.PushNumber((float)animating.GetSequenceGroundSpeed(animating.GetModelPtr(), sequence));
			return 1;
		}
		lua.PushNumber(0);
		return 1;
	}

	[LuaMethod]
	static int Entity__GetNumBodyGroups(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating != null && !ent.IsMarkedForDeletion()) {
			lua.PushNumber(animating.GetNumBodyGroups());
			return 1;
		}
		lua.PushNumber(0);
		return 1;
	}

	[LuaMethod]
	static int Entity__GetBodyGroups(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		StudioHdr? studioHdr = animating != null && !ent.IsMarkedForDeletion() ? animating.GetModelPtr() : null;
		lua.CreateTable();
		if (studioHdr == null)
			return 1;

		for (int i = 0; i < studioHdr.NumBodyParts(); i++) {
			MStudioBodyParts bodypart = studioHdr.Bodypart(i);
			lua.PushNumber(i + 1);
			lua.CreateTable();
			lua.PushNumber(i);
			lua.SetField(-2, "id");
			lua.PushString(bodypart.Name());
			lua.SetField(-2, "name");
			lua.PushNumber(bodypart.NumModels);
			lua.SetField(-2, "num");
			lua.CreateTable();
			for (int j = 0; j < bodypart.NumModels; j++) {
				lua.PushNumber(j);
				lua.PushString(bodypart.Model(j).Name());
				lua.SetTable(-3);
			}
			lua.SetField(-2, "submodels");
			lua.SetTable(-3);
		}
		return 1;
	}

	[LuaMethod]
	static int Entity__GetBodygroup(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || ent.IsMarkedForDeletion())
			return 0;
		lua.PushNumber(animating.GetBodygroup((int)lua.GetNumber(2)));
		return 1;
	}

	[LuaMethod]
	static int Entity__GetBodygroupCount(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || ent.IsMarkedForDeletion()) {
			lua.PushNumber(0);
			return 1;
		}
		lua.PushNumber(animating.GetBodygroupCount((int)lua.GetNumber(2)));
		return 1;
	}

	[LuaMethod]
	static int Entity__GetAttachment(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		int attachment = (int)lua.GetNumber(2);
		BaseAnimating? animating = ent.GetBaseAnimating();
		StudioHdr? studioHdr = animating?.GetModelPtr();
		if (animating == null || studioHdr == null || ent.IsMarkedForDeletion() || attachment <= 0)
			return 0;
		if (attachment > studioHdr.GetNumAttachments())
			return 0;
		if (!animating.GetAttachment(attachment, out Vector3 origin, out QAngle angles))
			return 0;

		int bone = studioHdr.GetAttachmentBone(attachment - 1);
		lua.CreateTable();
		LuaVector.Push_Vector(origin);
		lua.SetField(-2, "Pos");
		LuaAngle.Push_Angle(angles);
		lua.SetField(-2, "Ang");
		lua.PushNumber(bone);
		lua.SetField(-2, "Bone");
		return 1;
	}

	[LuaMethod]
	static int Entity__GetBoneCount(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		StudioHdr? studioHdr = animating != null && !ent.IsMarkedForDeletion() ? animating.GetModelPtr() : null;
		if (studioHdr == null) {
			lua.PushNumber(0);
			return 1;
		}
		lua.PushNumber(studioHdr.NumBones());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetBodygroupName(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || ent.IsMarkedForDeletion())
			return 0;
		lua.PushString(animating.GetBodygroupName((int)lua.GetNumber(2)));
		return 1;
	}

	[LuaMethod]
	static int Entity__SetBodygroup(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsEntityRemoving(ent, false))
			return 0;

		int value = 0;
		if ((int)lua.GetNumber(3) > 0)
			value = (int)lua.GetNumber(3);
		int group = 0;
		if ((int)lua.GetNumber(2) > 0)
			group = (int)lua.GetNumber(2);
		animating.SetBodygroup(group, value);
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

	[LuaMethod]
	static int Entity__SetRenderBounds(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		Vector3 mins = Vector3.Min(LuaVector.Get_Vector(3), LuaVector.Get_Vector(2));
		Vector3 maxs = Vector3.Max(LuaVector.Get_Vector(3), LuaVector.Get_Vector(2));
		if (lua.GetType(4) == LuaType.Vector) {
			mins -= LuaVector.Get_Vector(4);
			maxs += LuaVector.Get_Vector(4);
		}
		ent.SetRenderBounds(mins, maxs);
		UpdateRenderable(ent);
		return 0;
	}

	static void UpdateRenderable(BaseEntity ent) {
		if (ent.RenderHandle() != INVALID_CLIENT_RENDER_HANDLE)
			clientLeafSystem.RenderableChanged(ent.RenderHandle());
	}
#endif

	[LuaMethod]
	static int Entity__GetMaterials(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		PushModelMaterials(ent.GetModel());
		return 1;
	}

	static void PushModelMaterials(Model? model) {
		if (model == null) {
			g_Lua!.CreateTable();
			return;
		}
		int count = modelinfo.GetModelMaterialCount(model);
		if (count <= 0)
			count = 128;
		Span<IMaterial> mats = new IMaterial[count + 1];
		modelinfo.GetModelMaterials(model, mats[..count]);
		g_Lua!.CreateTable();
		for (int i = 0; mats[i] != null; i++) {
			g_Lua.PushNumber(i + 1);
			g_Lua.PushString(mats[i].GetName());
			g_Lua.SetTable(-3);
		}
	}

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
	static int Entity__SetHealth(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		int health = (int)lua.GetNumber(2);
#if CLIENT_DLL
		ent.SetHealth(health);
#else
		if (ent.Health != health) {
			ent.NetworkStateChanged();
			ent.SetHealth(health);
		}
#endif
		return 0;
	}

#if GAME_DLL
	[LuaMethod]
	static int Entity__SetMaxHealth(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ent.SetMaxHealth((int)lua.GetNumber(2));
		return 0;
	}
#endif

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

	const int MAX_PHYSOBJECT_LIST = 0x400;

	[LuaMethod]
	static int Entity__GetPhysicsObject(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
#if GAME_DLL
		if (ent.Edict() != null && ent.EntIndex() != 0)
			LuaPhysObj.Push_PhysObj(ent.VPhysicsGetObject());
		else
			LuaPhysObj.Push_PhysObj(g_PhysWorldObject);
#else
		LuaPhysObj.Push_PhysObj(ent.VPhysicsGetObject());
#endif
		return 1;
	}

	[LuaMethod]
	static int Entity__GetPhysicsObjectNum(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
#if GAME_DLL
		if (ent.Edict() == null || ent.EntIndex() == 0) {
			LuaPhysObj.Push_PhysObj(g_PhysWorldObject);
			return 1;
		}
#endif
		int index = (int)lua.GetNumber(2);
		if (index < 0) {
			DevWarning($"Entity:GetPhysicsObjectNum - Out of bounds physics object - min 0, got {index}\n");
			return 0;
		}

		IPhysicsObject[] list = System.Buffers.ArrayPool<IPhysicsObject>.Shared.Rent(MAX_PHYSOBJECT_LIST);
		try {
			int count = ent.VPhysicsGetObjectList(list.AsSpan(0, MAX_PHYSOBJECT_LIST));
			if (count == 0)
				return 0;

			if (index >= count) {
				DevWarning($"Entity:GetPhysicsObjectNum - Out of bounds physics object - max {count - 1}, got {index}\n");
				return 0;
			}

			LuaPhysObj.Push_PhysObj(list[index]);
			return 1;
		}
		finally {
			System.Buffers.ArrayPool<IPhysicsObject>.Shared.Return(list, true);
		}
	}

	[LuaMethod]
	static int Entity__GetPhysicsObjectCount(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		IPhysicsObject[] list = System.Buffers.ArrayPool<IPhysicsObject>.Shared.Rent(MAX_PHYSOBJECT_LIST);
		try {
			lua.PushNumber(ent.VPhysicsGetObjectList(list.AsSpan(0, MAX_PHYSOBJECT_LIST)));
			return 1;
		}
		finally {
			System.Buffers.ArrayPool<IPhysicsObject>.Shared.Return(list, true);
		}
	}

	[LuaMethod]
	static int Entity__GetPos(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		LuaVector.Push_Vector(ent.GetAbsOrigin());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetMoveType(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		lua.PushNumber((int)ent.GetMoveType());
		return 1;
	}

	[LuaMethod]
	static int Entity__WaterLevel(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (!ent.IsPlayer())
			ent.UpdateWaterState();
		lua.PushNumber((int)ent.GetWaterLevel());
		return 1;
	}

	[LuaMethod]
	[LuaMethod("IsOnGround")]
	static int Entity__OnGround(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		lua.PushBool((ent.GetFlags() & EntityFlags.OnGround) != 0);
		return 1;
	}

	[LuaMethod]
	static int Entity__IsFlagSet(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		lua.PushBool(((int)ent.GetFlags() & (int)lua.CheckNumber(2)) != 0);
		return 1;
	}

	[LuaMethod]
	static int Entity__GetParent(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		Push_Entity(ent.GetMoveParent());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetAngles(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		LuaAngle.Push_Angle(ent.GetAbsAngles());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetUp(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		ent.GetVectors(out _, out _, out Vector3 up);
		LuaVector.Push_Vector(up);
		return 1;
	}

	[LuaMethod]
	static int Entity__GetVelocity(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		Vector3 velocity;
#if CLIENT_DLL
		ent.EstimateAbsVelocity(out velocity);
		if (ent.GetMoveType() == MoveType.VPhysics && ent.VPhysicsGetObject() != null)
			ent.VPhysicsGetObject()!.GetVelocity(out velocity, out _);
#else
		if (ent.GetMoveType() == MoveType.VPhysics) {
			velocity = default;
			ent.VPhysicsGetObject()?.GetVelocity(out velocity, out _);
		}
		else
			velocity = ent.GetAbsVelocity();
#endif
		LuaVector.Push_Vector(velocity);
		return 1;
	}

	[LuaMethod]
	static int Entity__SequenceDuration(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null)
			return 0;

		if (lua.GetType(2) == LuaType.Number) {
			lua.PushNumber((float)animating.SequenceDuration((int)lua.GetNumber(2)));
			return 1;
		}

		lua.PushNumber((float)animating.SequenceDuration(animating.GetModelPtr(), animating.GetSequence()));
		return 1;
	}

	[LuaMethod]
	static int Entity__SetSequence(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsQueuedForRemoval(ent) || ent.IsMarkedForDeletion())
			return 0;

		int sequence;
		if (lua.GetType(2) == LuaType.Number)
			sequence = (int)lua.GetNumber(2);
		else
			sequence = animating.LookupSequence(lua.GetString(2));

		animating.SetSequence(sequence);
		lua.PushNumber((float)animating.SequenceDuration(animating.GetModelPtr(), sequence));
		return 1;
	}

	[LuaMethod]
	static int Entity__ResetSequence(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsQueuedForRemoval(ent) || ent.IsMarkedForDeletion())
			return 0;

		int sequence;
		if (lua.GetType(2) == LuaType.Number)
			sequence = (int)lua.GetNumber(2);
		else
			sequence = animating.LookupSequence(lua.GetString(2));

		animating.ResetSequence(sequence);
		return 0;
	}

	[LuaMethod]
	static int Entity__GetSkin(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || ent.IsMarkedForDeletion()) {
			lua.PushNumber(0);
			return 1;
		}
		lua.PushNumber(animating.Skin);
		return 1;
	}

	[LuaMethod]
	static int Entity__FrameAdvance(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsQueuedForRemoval(ent) || ent.IsMarkedForDeletion())
			return 0;

		animating.StudioFrameAdvance();
		return 0;
	}

	[LuaMethod]
	static int Entity__GetCreationID(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		lua.PushNumber(ent.CreationID);
		return 1;
	}

	[LuaMethod]
	static int Entity__SetCycle(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsQueuedForRemoval(ent) || ent.IsMarkedForDeletion())
			return 0;

		float cycle = (float)lua.GetNumber(2);
#if GAME_DLL
		if (!float.IsFinite(cycle))
			cycle = 0.0f;
#endif
		animating.SetCycle(cycle);
		return 0;
	}

	[LuaMethod]
	static int Entity__GetColor4Part(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		Color color = ent.ColorRender;
		lua.PushNumber(color.R);
		lua.PushNumber(color.G);
		lua.PushNumber(color.B);
		lua.PushNumber(color.A);
		return 4;
	}

	[LuaMethod]
	static int Entity__AddCallback(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		string name = lua.CheckString(2);
		if (!lua.IsType(3, LuaType.Function)) {
			lua.TypeError("function", 3);
			return 0;
		}

		int index = BaseEntity.LuaCallbackIndex(name);
		if (index == -1)
			return 0;

#if CLIENT_DLL
		if (ent.GetBaseAnimating() != null && index == 1)
			ent.GetBaseAnimating()!.RemoveFromPreviousBoneSetups();
#endif
		BaseEntity.LuaCallbackList list = ent.LuaCallbacks[index];
		list.Changes++;
		BaseEntity.LuaCallback callback = new() { ID = list.NextID++ };
		callback.Function.SetFromStack(3);
		callback.Removed = false;
		list.Callbacks.AddLast(callback);
		lua.PushNumber(callback.ID);
		return 1;
	}

	[LuaMethod]
	static int Entity__EmitSound(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		string soundName = new(lua.CheckString(2));
		Bootil.String.Util.Trim(ref soundName);
		if (soundName.Length == 0 || soundName[0] == ' ')
			return 0;

		double level = lua.CheckNumberOpt(3, 75.0);
		double pitch = lua.CheckNumberOpt(4, 100.0);
		double volume = lua.CheckNumberOpt(5, 1.0);

		BaseEntity soundEnt = ent;
		if (ent.IsWeapon())
			soundEnt = ((BaseCombatWeapon)ent).GetOwner() ?? ent;

#if CLIENT_DLL
		PASAttenuationFilter filter = new(soundEnt);
		if (ent.IsPlayer() && prediction.InPrediction())
			filter.UsePredictionRules();
#else
		// if (lua.GetType(9) == LuaType.RecipientFilter) { filter = Get_CRecipientFilter(9) ... } // really important TODO
		PASAttenuationFilter filter = new(soundEnt, ATTN_NORM);
		if (ent.IsPlayer())
			filter.UsePredictionRules();
#endif

		EmitSound_t ep = new() {
			Channel = ent.IsWeapon() ? 1 : 0,
			SoundName = soundName,
			Volume = (float)volume,
			SoundLevel = (SoundLevel)(int)level,
			Pitch = (int)pitch
		};
		if (lua.GetType(6) != LuaType.Nil)
			ep.Channel = (int)lua.CheckNumber(6);
		if (lua.GetType(7) != LuaType.Nil)
			ep.Flags = (SoundFlags)(int)lua.CheckNumber(7);
		if (lua.GetType(8) != LuaType.Nil)
			ep.SpecialDSP = (int)lua.CheckNumber(8);

		if (!enginesound.IsSoundPrecached(soundName))
			BaseEntity.PrecacheScriptSound(soundName);

		BaseEntity.EmitSound(filter, soundEnt.EntIndex(), ep);
		return 0;
	}

	[LuaMethod]
	static int Entity__ViewModelIndex(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (ent is not BaseViewModel viewModel)
			return 0;
		lua.PushNumber(viewModel.ViewModelIndex());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetSequenceName(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating != null && !ent.IsMarkedForDeletion()) {
			lua.PushString(animating.GetSequenceName((int)lua.GetNumber(2)));
			return 1;
		}
		lua.PushString("");
		return 1;
	}

	[LuaMethod]
	static int Entity__SendViewModelMatchingSequence(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (ent is BaseViewModel viewModel)
			viewModel.SendViewModelMatchingSequence((int)lua.CheckNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Entity__SetSaveValue(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		foreach (EHANDLE handle in RemoveList)
			if (handle.Get() == ent)
				return 0;
		if (ent.IsMarkedForDeletion())
			return 0;

		string name = lua.CheckString(2);
		if (stricmp(name, "classname") == 0) {
			lua.PushBool(false);
			return 1;
		}

		if (strnicmp(name, "m_ragdoll.", 10) == 0) {
			if (stricmp(name, "m_ragdoll.allowStretch") != 0) {
				if (name.Length < 17 || strcmp(name.AsSpan(name.Length - 17), "originParentSpace") != 0) {
					lua.PushBool(false);
					return 1;
				}
			}
		}
		else if (stricmp(name, "m_iEFlags") == 0) {
			if (((int)lua.CheckNumber(3) & 1) != 0) {
				lua.PushBool(false);
				return 1;
			}
		}
		else if (stricmp(name, "m_bActive") == 0) {
			lua.PushBool(false);
			return 1;
		}

		TypeDescription? td = null;
		for (DataMap? map = ent.GetDataDescMap(); map != null && td == null; map = map.BaseMap) {
			foreach (TypeDescription field in map.DataDesc) {
				if (field.Flags != FieldTypeDescFlags.Input && (field.Flags & FieldTypeDescFlags.Output) == 0 && ((field.ExternalName.Length != 0 && stricmp(name, field.ExternalName) == 0) || (field.FieldName.Length != 0 && stricmp(name, field.FieldName) == 0))) {
					td = field;
					break;
				}
			}
		}
#if CLIENT_DLL
		for (DataMap? map = ent.GetPredDescMap(); map != null && td == null; map = map.BaseMap) {
			foreach (TypeDescription field in map.DataDesc) {
				if (field.Flags != FieldTypeDescFlags.Input && (field.Flags & FieldTypeDescFlags.Output) == 0 && ((field.ExternalName.Length != 0 && stricmp(name, field.ExternalName) == 0) || (field.FieldName.Length != 0 && stricmp(name, field.FieldName) == 0))) {
					td = field;
					break;
				}
			}
		}
#endif
		if (td == null || (td.FieldSize > 1 && lua.GetType(3) != LuaType.Table)) {
			lua.PushBool(false);
			return 1;
		}

		bool isArray = td.FieldSize > 1 && lua.GetType(3) == LuaType.Table;
		Array? array = isArray ? (Array?)td.FieldInfo.GetValue(ent) : null;

		object? Convert1(LuaObject elem) {
			return td.FieldType switch {
				FieldType.EHandle => elem.GetType() == LuaType.Entity ? elem.GetEntity() : null,
				FieldType.String or FieldType.ModelName or FieldType.SoundName => (object?)(elem.isString() ? elem.GetString() : null),
				FieldType.Integer => elem.isNumber() ? (int)elem.GetFloat() : null,
				FieldType.Float => elem.isNumber() ? elem.GetFloat() : null,
				FieldType.Double => elem.isNumber() ? elem.GetDouble() : null,
				FieldType.Character => elem.isNumber() ? (byte)elem.GetFloat() : null,
				FieldType.Boolean => elem.isBool() ? elem.GetBool() : null,
				FieldType.Time => elem.isNumber() ? elem.GetDouble() + gpGlobals.CurTime : null,
				FieldType.Vector or FieldType.PositionVector => elem.isVector() ? elem.GetVector() : null,
				_ => null,
			};
		}

		if (isArray) {
			switch (td.FieldType) {
				case FieldType.EHandle:
				case FieldType.String:
				case FieldType.ModelName:
				case FieldType.SoundName:
				case FieldType.Integer:
				case FieldType.Float:
				case FieldType.Double:
				case FieldType.Character:
				case FieldType.Boolean:
				case FieldType.Time:
				case FieldType.Vector:
				case FieldType.PositionVector: {
						if (td.FieldType == FieldType.EHandle && !IsSettableHandle(name))
							break;
						LuaObject table = new();
						table.SetFromStack(3);
						bool set = false;
						LuaObject elem = new();
						for (int i = 0; i < td.FieldSize; i++) {
							table.GetMember(i + 1, elem);
							object? v = Convert1(elem);
							if (v != null) {
								set = true;
								if (td.FieldType == FieldType.EHandle) {
									EHANDLE handle = (EHANDLE)array!.GetValue(i)!;
									handle.Set((BaseEntity?)v);
									array.SetValue(handle, i);
								}
								else
									array!.SetValue(v, i);
							}
						}
						elem.UnReference();
						table.UnReference();
						lua.PushBool(set);
						return 1;
					}
			}
		}

		switch (td.FieldType) {
			case FieldType.EHandle:
				if (!IsSettableHandle(name))
					break;
				EHANDLE handle = (EHANDLE)td.FieldInfo.GetValue(ent)!;
				handle.Set(Get_Entity(3, true));
				td.FieldInfo.SetValue(ent, handle);
				lua.PushBool(true);
				return 1;
			case FieldType.String:
			case FieldType.ModelName:
			case FieldType.SoundName:
				td.FieldInfo.SetValue(ent, new string(lua.CheckString(3)));
				lua.PushBool(true);
				return 1;
			case FieldType.Integer:
				td.FieldInfo.SetValue(ent, (int)lua.CheckNumber(3));
				lua.PushBool(true);
				return 1;
			case FieldType.Float:
				td.FieldInfo.SetValue(ent, (float)lua.CheckNumber(3));
				lua.PushBool(true);
				return 1;
			case FieldType.Double:
				td.FieldInfo.SetValue(ent, lua.CheckNumber(3));
				lua.PushBool(true);
				return 1;
			case FieldType.Short:
				td.FieldInfo.SetValue(ent, (short)(int)lua.CheckNumber(3));
				lua.PushBool(true);
				return 1;
			case FieldType.Character:
				td.FieldInfo.SetValue(ent, (byte)(int)lua.CheckNumber(3));
				lua.PushBool(true);
				return 1;
			case FieldType.Boolean:
				td.FieldInfo.SetValue(ent, lua.GetBool(3));
				lua.PushBool(true);
				return 1;
			case FieldType.ModelIndex:
#if GAME_DLL
				td.FieldInfo.SetValue(ent, BaseEntity.PrecacheModel(lua.CheckString(3), true));
#endif
				lua.PushBool(true);
				return 1;
			case FieldType.MaterialIndex: {
					string material = lua.CheckString(3);
#if CLIENT_DLL
					td.FieldInfo.SetValue(ent, HLClient.GetMaterialIndex(material));
#else
					td.FieldInfo.SetValue(ent, GameInterface.GetMaterialIndex(material));
					GameInterface.PrecacheMaterial(material);
#endif
					lua.PushBool(true);
					return 1;
				}
			case FieldType.Time:
				td.FieldInfo.SetValue(ent, lua.CheckNumber(3) + gpGlobals.CurTime);
				lua.PushBool(true);
				return 1;
			case FieldType.Tick:
				td.FieldInfo.SetValue(ent, gpGlobals.TickCount + (int)lua.CheckNumber(3));
				lua.PushBool(true);
				return 1;
			case FieldType.Vector:
			case FieldType.PositionVector: {
					Vector3 vec;
					if (lua.GetType(3) == LuaType.String) {
						Span<float> f = stackalloc float[3];
						UTIL_StringToVector(f, lua.CheckString(3));
						vec = new(f[0], f[1], f[2]);
					}
					else
						vec = LuaVector.Get_Vector(3);
					td.FieldInfo.SetValue(ent, vec);
					lua.PushBool(true);
					return 1;
				}
			default:
				lua.PushBool(ent.KeyValue(lua.CheckString(2), lua.CheckString(3)));
				return 1;
		}
		lua.PushBool(false);
		return 1;
	}

	static bool IsSettableHandle(ReadOnlySpan<char> name) =>
		stricmp(name, "m_hThrower") == 0 || stricmp(name, "m_hOriginalThrower") == 0 || stricmp(name, "m_hFilter") == 0
		|| stricmp(name, "m_hSpeechFilter") == 0 || stricmp(name, "m_hEnemyFilter") == 0 || stricmp(name, "m_hDamageFilter") == 0;

	[LuaMethod]
	static int Entity__IsWorld(ILuaInterface lua) {
		LuaType type = lua.GetType(1);
		if (type != LuaType.Entity && type != LuaType.Nil)
			lua.TypeError("Entity", 1);
		BaseEntity? ent = UserGet(1);
		if (ent == null) {
			lua.PushBool(false);
			return 1;
		}
#if CLIENT_DLL
		lua.PushBool(ent == cl_entitylist.GetBaseEntity(0));
#else
		Edict? edict = ent.Edict();
		lua.PushBool(edict == null || edict.EdictIndex == 0);
#endif
		return 1;
	}

	static void PushInternalValue(TypeDescription td, object? value) {
		switch (td.FieldType) {
			case FieldType.Float:
				g_Lua!.PushNumber(Convert.ToSingle(value));
				break;
			case FieldType.Double:
				g_Lua!.PushNumber(Convert.ToDouble(value));
				break;
			case FieldType.String:
			case FieldType.ModelName:
			case FieldType.SoundName:
				g_Lua!.PushString(value?.ToString() ?? "");
				break;
			case FieldType.Vector:
			case FieldType.PositionVector:
				g_Lua!.PushVector((Vector3)value!);
				break;
			case FieldType.Integer:
			case FieldType.Short:
			case FieldType.Character:
				g_Lua!.PushNumber(Convert.ToInt32(value));
				break;
			case FieldType.Boolean:
				g_Lua!.PushBool(Convert.ToBoolean(value));
				break;
			case FieldType.EHandle:
				Push_Entity(((EHANDLE)value!).Get());
				break;
			case FieldType.Time:
				g_Lua!.PushNumber(Convert.ToDouble(value) - gpGlobals.CurTime);
				break;
			case FieldType.Tick:
				g_Lua!.PushNumber(Convert.ToInt32(value) - gpGlobals.TickCount);
				break;
			default:
				g_Lua!.PushNil();
				break;
		}
	}

	[LuaMethod]
	static int Entity__GetInternalVariable(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		string name = lua.CheckString(2);

		TypeDescription? td = null;
		for (DataMap? map = ent.GetDataDescMap(); map != null && td == null; map = map.BaseMap) {
			foreach (TypeDescription field in map.DataDesc) {
				if (field.Flags != FieldTypeDescFlags.Input && (field.Flags & FieldTypeDescFlags.Output) == 0 && ((field.ExternalName.Length != 0 && stricmp(name, field.ExternalName) == 0) || (field.FieldName.Length != 0 && stricmp(name, field.FieldName) == 0))) {
					td = field;
					break;
				}
			}
		}
#if CLIENT_DLL
		for (DataMap? map = ent.GetPredDescMap(); map != null && td == null; map = map.BaseMap) {
			foreach (TypeDescription field in map.DataDesc) {
				if (field.Flags != FieldTypeDescFlags.Input && (field.Flags & FieldTypeDescFlags.Output) == 0 && ((field.ExternalName.Length != 0 && stricmp(name, field.ExternalName) == 0) || (field.FieldName.Length != 0 && stricmp(name, field.FieldName) == 0))) {
					td = field;
					break;
				}
			}
		}
#endif
		if (td == null)
			return 0;

		object? value = td.FieldInfo.GetValue(ent);

		switch (td.FieldType) {
			case FieldType.Float:
			case FieldType.Double:
			case FieldType.String:
			case FieldType.ModelName:
			case FieldType.SoundName:
			case FieldType.Vector:
			case FieldType.PositionVector:
			case FieldType.Integer:
			case FieldType.Boolean:
			case FieldType.Character:
			case FieldType.EHandle:
			case FieldType.Time:
				if (td.FieldSize < 2 || value is not Array array) {
					PushInternalValue(td, value);
					return 1;
				}
				LuaTable table = new(null, td.FieldSize);
				for (int i = 0; i < td.FieldSize; i++) {
					PushInternalValue(td, array.GetValue(i));
					table.SetMember(i + 1);
				}
				table.Push();
				table.UnReference();
				return 1;
			case FieldType.Short:
			case FieldType.Tick:
				PushInternalValue(td, value);
				return 1;
			case FieldType.Color32: {
					Color color = (Color)value!;
					lua.PushString($"{color.R} {color.G} {color.B} {color.A}");
					return 1;
				}
			case FieldType.ModelIndex: {
					Model? model = modelinfo.GetModel(Convert.ToInt32(value));
					if (model == null)
						return 0;
					lua.PushString(modelinfo.GetModelName(model));
					return 1;
				}
			case FieldType.MaterialIndex: {
#if CLIENT_DLL
					ReadOnlySpan<char> material = HLClient.GetMaterialNameFromIndex(Convert.ToInt32(value));
#else
					ReadOnlySpan<char> material = GameInterface.GetMaterialNameFromIndex(Convert.ToInt32(value));
#endif
					if (material.IsEmpty)
						return 0;
					lua.PushString(material);
					return 1;
				}
		}
		return 0;
	}


	[LuaMethod]
	static int Entity__GetGroundEntity(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		Push_Entity(ent.GetGroundEntity());
		return 1;
	}

	[LuaMethod]
	static int Entity__GetPoseParameterRange(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || ent.IsMarkedForDeletion())
			return 0;

		int index;
		if (lua.GetType(2) == LuaType.Number)
			index = (int)lua.GetNumber(2);
		else
#if CLIENT_DLL
			index = animating.LookupPoseParameter(animating.GetModelPtr(), lua.CheckString(2));
#else
			index = animating.LookupPoseParameter(lua.CheckString(2));
#endif

		if (!animating.GetPoseParameterRange(index, out float minValue, out float maxValue))
			return 0;

		lua.PushNumber(minValue);
		lua.PushNumber(maxValue);
		return 2;
	}

	[LuaMethod]
	static int Entity__LookupPoseParameter(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || ent.IsMarkedForDeletion())
			return 0;
#if CLIENT_DLL
		lua.PushNumber(animating.LookupPoseParameter(animating.GetModelPtr(), lua.CheckString(2)));
#else
		lua.PushNumber(animating.LookupPoseParameter(lua.CheckString(2)));
#endif
		return 1;
	}

	[LuaMethod]
	static int Entity__LookupBone(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || ent.IsMarkedForDeletion() || animating.GetModelPtr() == null)
			return 0;

		int bone = animating.LookupBone(lua.CheckString(2));
		if (bone == -1)
			return 0;

		lua.PushNumber(bone);
		return 1;
	}

	[LuaMethod]
	static int Entity__LookupSequence(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || ent.IsMarkedForDeletion())
			return 0;

		int sequence = animating.LookupSequence(lua.CheckString(2));
		lua.PushNumber(sequence);
		if (sequence == -1) {
			lua.PushNil();
			return 2;
		}

		lua.PushNumber((float)animating.SequenceDuration(sequence));
		return 2;
	}

	static int GetPoseParameterIndex(ILuaInterface lua, BaseAnimating animating) {
		if (lua.GetType(2) == LuaType.Number)
			return (int)lua.CheckNumber(2);
		return animating.LookupPoseParameter(lua.CheckString(2));
	}

	[LuaMethod]
	static int Entity__GetPoseParameter(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || ent.IsMarkedForDeletion())
			return 0;

		lua.PushNumber(animating.GetPoseParameter(GetPoseParameterIndex(lua, animating)));
		return 1;
	}

	[LuaMethod]
	static int Entity__SetPoseParameter(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsQueuedForRemoval(ent) || ent.IsMarkedForDeletion())
			return 0;

		int index = GetPoseParameterIndex(lua, animating);
		animating.SetPoseParameter(index, (float)lua.CheckNumber(3));
		return 0;
	}

	[LuaMethod]
	static int Entity__SetPlaybackRate(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsQueuedForRemoval(ent) || ent.IsMarkedForDeletion())
			return 0;

		float rate = (float)lua.CheckNumber(2);
		if (!float.IsFinite(rate))
			rate = 1.0f;
		animating.SetPlaybackRate(rate);
		return 0;
	}

#if CLIENT_DLL
	[LuaMethod]
	static int Entity__SetIK(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (lua.GetBool(2))
			ent.EntClientFlags &= ~EntClientFlags.DontUseIK;
		else
			ent.EntClientFlags |= EntClientFlags.DontUseIK;
		return 0;
	}
#else
	[LuaMethod]
	static int Entity__SetIK(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null)
			return 0;

		if (IsQueuedForRemoval(ent))
			return 0;

		if (ent.IsMarkedForDeletion())
			return 0;

		if (lua.GetBool(2))
			animating.EnableServerIK();
		else
			animating.DisableServerIK();
		return 0;
	}
#endif

	[LuaMethod]
	static int Entity__GetFlexIDByName(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || ent.IsMarkedForDeletion())
			return 0;

		StudioHdr? studioHdr = animating.GetModelPtr();
		if (studioHdr == null)
			return 0;

		string name = lua.CheckString(2);
		for (int i = 0; i < (int)studioHdr.NumFlexControllers(); i++) {
			if (strnicmp(studioHdr.FlexController((LocalFlexController)i).Name(), name, name.Length) == 0) {
				lua.PushNumber(i);
				return 1;
			}
		}

		return 0;
	}

	[LuaMethod]
	static int Entity__SetFlexWeight(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		BaseAnimating? animating = ent.GetBaseAnimating();
		if (animating == null || IsQueuedForRemoval(ent) || ent.IsMarkedForDeletion())
			return 0;

		int index = Math.Clamp((int)lua.CheckNumber(2), 0, 95);
		float weight = (float)lua.CheckNumber(3);

#if CLIENT_DLL
		if (animating.GetFlexManipulator(false) == null && animating is C_BaseFlex flex) {
			flex.SetFlexWeight((LocalFlexController)index, weight, false);
			return 0;
		}

		if (animating.GetFlexManipulator(true) is C_FlexManipulate manipulator)
			manipulator.SetFlexWeight((byte)index, weight);
#else
		if (animating.GetFlexManipulator(true) is FlexManipulate manipulator)
			manipulator.SetFlexWeight((byte)index, weight);
#endif
		return 0;
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
		BaseEntity? ent = Get_Entity(1, true);
		if (ent == null)
			return 0;

		ILuaObject? table = ent.GetLuaTable();
		if (table == null)
			return 0;

		table.Push();
		return 1;
	}

	[LuaMethod]
	static int Entity__SetTable(ILuaInterface lua) {
		BaseEntity ent = Get_Entity(1, false)!;
		if (g_Lua!.GetType(2) != LuaType.Table) {
			g_Lua.TypeError("table", 2);
			return 0;
		}

		ILuaObject? table = g_Lua.GetObject(2);
		if (table != null)
			ent.SetLuaTable(table);
		return 0;
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
