#if CLIENT_DLL || GAME_DLL

using Source.Common;

using Game.Shared;

using System.Numerics;

#if CLIENT_DLL
namespace Game.Client;
using Game.Client.GarrysMod;
#else
namespace Game.Server;
using Game.Server.GarrysMod;
#endif

public partial class
#if CLIENT_DLL
		C_BaseCombatCharacter
#elif GAME_DLL
	BaseCombatCharacter
#endif
{
	public int GetAmmoCount(int ammoIndex) {
		if ((uint)ammoIndex >= MAX_AMMO_SLOTS)
			return 0;

		if (GetAmmoDef().MaxCarry(ammoIndex) == AmmoDef.INFINITE_AMMO)
			return 999;

		return Ammo[ammoIndex];
	}

	public virtual BaseCombatWeapon? Weapon_OwnsThisType(ReadOnlySpan<char> weapon, int subtype) {
		for (int i = 0; i < MAX_WEAPONS; i++) {
			if (MyWeapons[i].Get() != null && FClassnameIs(MyWeapons[i].Get(), weapon)) {
				// Make sure it matches the subtype
				if (MyWeapons[i].Get()!.GetSubType() == subtype)
					return MyWeapons[i].Get();
			}
		}
		return null;
	}


	public virtual bool Weapon_Switch(BaseCombatWeapon? weapon, int viewmodelindex = 0) {
		if (weapon == null)
			return false;

		if (ActiveWeapon.Get() == weapon) {
			if (!ActiveWeapon.Get()!.IsWeaponVisible() || ActiveWeapon.Get()!.IsHolstered())
				return ActiveWeapon.Get()!.Deploy();
			return false;
		}

		if (!Weapon_CanSwitchTo(weapon))
			return false;

		if (ActiveWeapon.Get() != null)
			if (!ActiveWeapon.Get()!.Holster(weapon))
				return false;

		ActiveWeapon.Set(weapon);
		return weapon.Deploy();
	}

	public virtual bool Weapon_CanSwitchTo(BaseCombatWeapon weapon) {
		if (IsPlayer()) {
			BasePlayer player = (BasePlayer)this!;
#if !CLIENT_DLL
			IServerVehicle? vehicle = player.GetVehicle();
#else
			IClientVehicle? vehicle = player.GetVehicle();
#endif

			if (vehicle != null && !player.UsingStandardWeaponsInVehicle())
				return false;
		}

		if (!weapon.HasAnyAmmo() && 0 == GetAmmoCount(weapon.PrimaryAmmoType))
			return false;

		if (!weapon.CanDeploy())
			return false;

		if (ActiveWeapon.Get() != null) {
			if (!ActiveWeapon.Get()!.CanHolster() && !weapon.ForceWeaponSwitch())
				return false;

			if (IsPlayer()) {
				BasePlayer? player = (BasePlayer)this!;
				// check if active weapon force the last weapon to switch
				if (ActiveWeapon.Get()!.ForceWeaponSwitch()) {
					// last weapon wasn't allowed to switch, don't allow to switch to new weapon
					BaseCombatWeapon? lastWeapon = player.GetLastWeapon();
					if (lastWeapon != null && weapon != lastWeapon && !lastWeapon!.CanHolster() && !weapon.ForceWeaponSwitch())
						return false;
				}
			}
		}

		return true;
	}

	public virtual BaseCombatWeapon? GetActiveWeapon() => ActiveWeapon.Get();

	public void RemoveAmmo(int count, int ammoIndex) {
		if (count <= 0)
			return;

		// Infinite ammo?
		if (GetAmmoDef().MaxCarry(ammoIndex) == AmmoDef.INFINITE_AMMO)
			return;

		// Ammo pickup sound
		Ammo.Set(ammoIndex, Math.Max(Ammo[ammoIndex] - count, 0));
	}
	public void RemoveAmmo(int count, ReadOnlySpan<char> name) => RemoveAmmo(count, GetAmmoDef().Index(name));

#if GMOD_DLL
	public static void OnAmmoChanged(BaseCombatCharacter? ent, int ammoIndex, int oldCount, int newCount) {
		if (oldCount != newCount && ent != null && ent.IsPlayer() && gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerAmmoChanged)) {
			LuaEntity.Push_Entity(ent);
			g_Lua!.PushNumber(ammoIndex);
			g_Lua.PushNumber(oldCount);
			g_Lua.PushNumber(newCount);
			gGM.CallNoReturns(4);
		}
	}

	public void RemoveAllAmmo() {
		for (int i = 0; i < MAX_AMMO_SLOTS; i++) {
			int old = Ammo[i];
			if (Ammo[i] != 0)
				Ammo.Set(i, 0);
			OnAmmoChanged(this, i, old, 0);
		}
	}
#endif

	public virtual Vector3 Weapon_ShootPosition() => EyePosition();

	public bool SwitchToNextBestWeapon(BaseCombatWeapon current) {
		BaseCombatWeapon? newWeapon = g_pGameRules.GetNextBestWeapon(this, current);

		if ((newWeapon != null) && (newWeapon != current))
			return Weapon_Switch(newWeapon);

		return false;
	}

	public void SetBloodColor(BloodColor color) => BloodColor = color;
}
#endif
