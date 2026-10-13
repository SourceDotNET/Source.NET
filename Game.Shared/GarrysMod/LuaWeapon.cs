#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public static partial class LuaWeapon
{
	[LuaClass]
	static readonly LuaEntityClass LC_Weapon = LuaEntity.LC_Weapon;

	[LuaMethod]
	static int Weapon__IsScripted(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		ReadOnlySpan<char> name = weapon.GetLuaScriptName();
		lua.PushBool(!name.IsEmpty && name[0] != '\0');
		return 1;
	}

	[LuaMethod]
	static int Weapon__IsWeapon(ILuaInterface lua) {
		lua.PushBool(true);
		return 1;
	}

	[LuaMethod]
	static int Weapon__AllowsAutoSwitchFrom(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushBool(weapon.AllowsAutoSwitchFrom());
		return 1;
	}

	[LuaMethod]
	static int Weapon__AllowsAutoSwitchTo(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushBool(weapon.AllowsAutoSwitchTo());
		return 1;
	}

	[LuaMethod]
	static int Weapon__Clip1(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.iClip1);
		return 1;
	}

	[LuaMethod]
	static int Weapon__Clip2(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.iClip2);
		return 1;
	}

	[LuaMethod]
	static int Weapon__SetClip1(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		weapon.SetDropped();
		int clip = (int)lua.CheckNumber(2);
		if (weapon.iClip1 != clip) {
#if GAME_DLL
			weapon.NetworkStateChanged();
#endif
			weapon.iClip1 = clip;
		}
		return 0;
	}

	[LuaMethod]
	static int Weapon__SetClip2(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		weapon.SetDropped();
		int clip = (int)lua.CheckNumber(2);
		if (weapon.iClip2 != clip) {
#if GAME_DLL
			weapon.NetworkStateChanged();
#endif
			weapon.iClip2 = clip;
		}
		return 0;
	}

	[LuaMethod]
	static int Weapon__DefaultReload(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		Activity act = (Activity)(int)lua.CheckNumber(2);
		bool ret = weapon.DefaultReload(weapon.GetMaxClip1(), weapon.GetMaxClip2(), act);
#if GAME_DLL
		BaseCombatCharacter? owner = weapon.GetOwner();
		if (ret && owner != null && owner.IsPlayer())
			((HL2MP_Player)owner).DoAnimationEvent(PlayerAnimEvent.Reload, 0, true);
#endif
		lua.PushBool(ret);
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetActivity(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber((int)weapon.GetActivity());
		return 1;
	}

	[LuaMethod]
	static int Weapon__SetActivity(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		weapon.SetActivity((Activity)(int)lua.CheckNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Weapon__GetHoldType(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushString(weapon.GetHoldType());
		return 1;
	}

	[LuaMethod]
	static int Weapon__SetHoldType(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		weapon.SetHoldType(lua.CheckString(2));
		return 0;
	}

	[LuaMethod]
	static int Weapon__GetMaxClip1(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.GetMaxClip1());
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetMaxClip2(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.GetMaxClip2());
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetNextPrimaryFire(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.NextPrimaryAttack);
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetNextSecondaryFire(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.NextSecondaryAttack);
		return 1;
	}

	[LuaMethod]
	static int Weapon__SetNextPrimaryFire(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		double time = lua.CheckNumber(2);
		if (weapon.NextPrimaryAttack != time) {
#if GAME_DLL
			weapon.NetworkStateChanged();
#endif
			weapon.NextPrimaryAttack = time;
		}
		return 0;
	}

	[LuaMethod]
	static int Weapon__SetNextSecondaryFire(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		double time = lua.CheckNumber(2);
		if (weapon.NextSecondaryAttack != time) {
#if GAME_DLL
			weapon.NetworkStateChanged();
#endif
			weapon.NextSecondaryAttack = time;
		}
		return 0;
	}

	[LuaMethod]
	static int Weapon__GetPrimaryAmmoType(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.GetPrimaryAmmoType());
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetSecondaryAmmoType(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.GetSecondaryAmmoType());
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetPrintName(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushString(weapon.GetPrintName());
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetSlot(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.GetSlot());
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetSlotPos(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.GetPosition());
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetWeaponViewModel(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushString(weapon.GetViewModel(0));
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetWeaponWorldModel(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushString(weapon.GetWorldModel());
		return 1;
	}

	[LuaMethod]
	static int Weapon__GetWeight(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.GetWeight());
		return 1;
	}

	[LuaMethod]
	static int Weapon__HasAmmo(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushBool(weapon.HasAmmo());
		return 1;
	}

	[LuaMethod]
	static int Weapon__IsWeaponVisible(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushBool(weapon.IsWeaponVisible());
		return 1;
	}

#if CLIENT_DLL
	[LuaMethod]
	static int Weapon__IsCarriedByLocalPlayer(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushBool(weapon.IsCarriedByLocalPlayer());
		return 1;
	}
#endif

	[LuaMethod]
	static int Weapon__SendWeaponAnim(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		if ((int)lua.CheckNumber(2) < 0)
			lua.ArgError(2, "invalid act");
		weapon.SendWeaponAnim((Activity)(int)lua.CheckNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Weapon__GetDeploySpeed(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.GetDeploySpeed());
		return 1;
	}

	[LuaMethod]
	static int Weapon__SetDeploySpeed(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		weapon.SetDeploySpeed((float)lua.CheckNumber(2));
		return 0;
	}

	[LuaMethod]
	static int Weapon__LastShootTime(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		lua.PushNumber(weapon.LastShootTime());
		return 1;
	}

	[LuaMethod]
	static int Weapon__SetLastShootTime(ILuaInterface lua) {
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		TimeUnit_t time = gpGlobals.CurTime;
		if (lua.GetType(2) == LuaType.Number)
			time = lua.GetNumber(2);
		weapon.SetLastShootTime(time);
		return 0;
	}
	[LuaMethod]
	static int Weapon__CallOnClient(ILuaInterface lua) {
#if GAME_DLL
		BaseCombatWeapon weapon = LuaEntity.Get_Weapon(1, false)!;
		BaseCombatCharacter? owner = weapon.GetOwner();
		if (owner != null && owner.IsPlayer()) {
			SingleUserRecipientFilter filter = new((BasePlayer)owner);
			filter.MakeReliable();
			ReadOnlySpan<char> function = lua.CheckString(2);
			ReadOnlySpan<char> args = lua.GetString(3) ?? "";
			UserMessageBegin(filter, "SWEPCmd");
			WRITE_EHANDLE(weapon);
			WRITE_STRING(function);
			WRITE_STRING(args);
			MessageEnd();
			return 0;
		}
		DevMsg("ERROR! CallOnClient called on weapon without an owner!\n");
#endif
		return 0;
	}
}
#endif
