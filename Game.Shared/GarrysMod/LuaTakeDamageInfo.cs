#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public class TakeDamageInfoRef
{
	public TakeDamageInfo Info;
}

public static partial class LuaTakeDamageInfo
{
	[LuaClass(typeof(TakeDamageInfoRef), NullError = "Tried to use a NULL CTakeDamageInfo!")]
	public static readonly LuaClass LC_TakeDamageInfo = new("CTakeDamageInfo", LuaType.DamageInfo, null, null);

	public static void Push_TakeDamageInfo(TakeDamageInfoRef? info) => LC_TakeDamageInfo.Push(info);

	public static ref TakeDamageInfo Get_TakeDamageInfo(int stackPos) {
		TakeDamageInfoRef? info = LC_TakeDamageInfo.Get(stackPos) as TakeDamageInfoRef;
		if (info == null)
			g_Lua!.Error("Tried to use a NULL CTakeDamageInfo!");
		return ref info!.Info;
	}

	[LuaGlobal]
	static int DamageInfo(ILuaInterface lua) {
		Push_TakeDamageInfo(new TakeDamageInfoRef { Info = new() });
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__AddDamage(ILuaInterface lua) {
		Get_TakeDamageInfo(1).AddDamage((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SubtractDamage(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SubtractDamage((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__ScaleDamage(ILuaInterface lua) {
		Get_TakeDamageInfo(1).ScaleDamage((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetAmmoType(ILuaInterface lua) {
		lua.PushNumber(Get_TakeDamageInfo(1).GetAmmoType());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetAmmoType(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetAmmoType((int)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetAttacker(ILuaInterface lua) {
		LuaEntity.Push_Entity(Get_TakeDamageInfo(1).GetAttacker());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetAttacker(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetAttacker(LuaEntity.Get_Entity(2, true));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetInflictor(ILuaInterface lua) {
		LuaEntity.Push_Entity(Get_TakeDamageInfo(1).GetInflictor());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetInflictor(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetInflictor(LuaEntity.Get_Entity(2, true));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetWeapon(ILuaInterface lua) {
		LuaEntity.Push_Entity(Get_TakeDamageInfo(1).GetWeapon());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetWeapon(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetWeapon(LuaEntity.Get_Entity(2, true));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetBaseDamage(ILuaInterface lua) {
		lua.PushNumber(Get_TakeDamageInfo(1).GetBaseDamage());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetBaseDamage(ILuaInterface lua) {
		Get_TakeDamageInfo(1).BaseDamage = (float)lua.GetNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetDamage(ILuaInterface lua) {
		lua.PushNumber(Get_TakeDamageInfo(1).GetDamage());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetDamage(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetDamage((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetMaxDamage(ILuaInterface lua) {
		lua.PushNumber(Get_TakeDamageInfo(1).GetMaxDamage());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetMaxDamage(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetMaxDamage((float)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetDamageBonus(ILuaInterface lua) {
		lua.PushNumber(Get_TakeDamageInfo(1).GetDamageBonus());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetDamageBonus(ILuaInterface lua) {
		Get_TakeDamageInfo(1).DamageBonus = (float)lua.GetNumber(2);
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetDamageCustom(ILuaInterface lua) {
		lua.PushNumber(Get_TakeDamageInfo(1).GetDamageCustom());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetDamageCustom(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetDamageCustom((int)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetDamageForce(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_TakeDamageInfo(1).GetDamageForce());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetDamageForce(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetDamageForce(LuaVector.Get_Vector(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetDamagePosition(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_TakeDamageInfo(1).GetDamagePosition());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetDamagePosition(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetDamagePosition(LuaVector.Get_Vector(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetReportedPosition(ILuaInterface lua) {
		LuaVector.Push_Vector(Get_TakeDamageInfo(1).GetReportedPosition());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetReportedPosition(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetReportedPosition(LuaVector.Get_Vector(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__GetDamageType(ILuaInterface lua) {
		lua.PushNumber((int)Get_TakeDamageInfo(1).GetDamageType());
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__SetDamageType(ILuaInterface lua) {
		Get_TakeDamageInfo(1).SetDamageType((DamageType)(int)lua.GetNumber(2));
		return 0;
	}

	[LuaMethod]
	static int CTakeDamageInfo__IsDamageType(ILuaInterface lua) {
		ref TakeDamageInfo info = ref Get_TakeDamageInfo(1);
		lua.PushBool(((int)info.GetDamageType() & (int)lua.GetNumber(2)) != 0);
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__IsBulletDamage(ILuaInterface lua) {
		lua.PushBool((Get_TakeDamageInfo(1).GetDamageType() & DamageType.Bullet) != 0);
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__IsExplosionDamage(ILuaInterface lua) {
		lua.PushBool((Get_TakeDamageInfo(1).GetDamageType() & DamageType.Blast) != 0);
		return 1;
	}

	[LuaMethod]
	static int CTakeDamageInfo__IsFallDamage(ILuaInterface lua) {
		lua.PushBool((Get_TakeDamageInfo(1).GetDamageType() & DamageType.Fall) != 0);
		return 1;
	}
}
#endif
