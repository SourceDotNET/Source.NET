using Source;
using Source.Common.Commands;

using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Shared;

public struct Ammo
{
	public string Name;
	public DamageType DamageType;
	public AmmoTracer TracerType;
	public float PhysicsForceImpulse;
	public int MinSplashSize;
	public int MaxSplashSize;
	public AmmoFlags Flags;

	public int PlrDmg;
	public int NPCDmg;
	public int MaxCarry;


	[CvarIgnore] public ConVar? PlrDmgCVar;
	[CvarIgnore] public ConVar? NPCDmgCVar;
	[CvarIgnore] public ConVar? MaxCarryCVar;
}
public enum AmmoTracer
{
	None,
	Line,
	Rail,
	Beam,
	LineAndWhiz
}
public enum AmmoFlags
{
	ForceDropIfCarried = 0x01,
	InterpretPlrDamageAsDamageToPlayer = 0x2
}
public class AmmoDef
{
	public const int USE_CVAR = -1;
	public const int INFINITE_AMMO = -2;

	public static Ammo NULL = default;

	public int AmmoIndex = 1;
	public Ammo[] AmmoType = new Ammo[MAX_AMMO_TYPES];

	public ref Ammo GetAmmoOfIndex(int ammoIndex) {
		if (ammoIndex >= AmmoIndex)
			return ref NULL;
		return ref AmmoType[ammoIndex];
	}

	public int Index(ReadOnlySpan<char> name) {
		int i;

		if (name.IsEmpty)
			return -1;

		for (i = 1; i < AmmoIndex; i++) {
			if (stricmp(name, AmmoType[i].Name) == 0)
				return i;
		}

		return -1;
	}
	public int PlrDamage(int ammoIndex) {
		if (ammoIndex < 1 || ammoIndex >= AmmoIndex)
			return 0;

		if (AmmoType[ammoIndex].PlrDmg == USE_CVAR) {
			if (AmmoType[ammoIndex].PlrDmgCVar != null)
				return (int)AmmoType[ammoIndex].PlrDmgCVar!.GetFloat();

			return 0;
		}
		else {
			return AmmoType[ammoIndex].PlrDmg;
		}
	}
	public int NPCDamage(int ammoIndex) {
		if (ammoIndex < 1 || ammoIndex >= AmmoIndex)
			return 0;

		if (AmmoType[ammoIndex].NPCDmg == USE_CVAR) {
			if (AmmoType[ammoIndex].NPCDmgCVar != null)
				return (int)AmmoType[ammoIndex].NPCDmgCVar!.GetFloat();

			return 0;
		}
		else {
			return AmmoType[ammoIndex].NPCDmg;
		}
	}
	public int MaxCarry(int ammoIndex) {
		if (ammoIndex < 1 || ammoIndex >= AmmoIndex)
			return 0;

		if (AmmoType[ammoIndex].MaxCarry == USE_CVAR) {
			if (AmmoType[ammoIndex].MaxCarryCVar != null)
				return (int)AmmoType[ammoIndex].MaxCarryCVar!.GetFloat();

			return 0;
		}
		else {
			return AmmoType[ammoIndex].MaxCarry;
		}
	}
	public DamageType DamageType(int ammoIndex) {
		if (ammoIndex < 1 || ammoIndex >= AmmoIndex)
			return 0;

		return AmmoType[ammoIndex].DamageType;
	}
	public AmmoTracer TracerType(int ammoIndex) {
		if (ammoIndex < 1 || ammoIndex >= AmmoIndex)
			return 0;

		return AmmoType[ammoIndex].TracerType;
	}
	public float DamageForce(int ammoIndex) {
		if (ammoIndex < 1 || ammoIndex >= AmmoIndex)
			return 0;

		return AmmoType[ammoIndex].PhysicsForceImpulse;
	}
	public int MinSplashSize(int ammoIndex) {
		if (ammoIndex < 1 || ammoIndex >= AmmoIndex)
			return 0;

		return AmmoType[ammoIndex].MinSplashSize;
	}
	public int MaxSplashSize(int ammoIndex) {
		if (ammoIndex < 1 || ammoIndex >= AmmoIndex)
			return 0;

		return AmmoType[ammoIndex].MaxSplashSize;
	}
	public AmmoFlags Flags(int ammoIndex) {
		if (ammoIndex < 1 || ammoIndex >= AmmoIndex)
			return 0;

		return AmmoType[ammoIndex].Flags;
	}
	public bool AddAmmoType(ReadOnlySpan<char> name, DamageType damageType, AmmoTracer tracerType, AmmoFlags flags, int minSplashSize, int maxSplashSize) {
		if (AmmoIndex == MAX_AMMO_TYPES)
			return false;

		AmmoType[AmmoIndex].Name = new(name.SliceNullTerminatedString());
		AmmoType[AmmoIndex].DamageType = damageType;
		AmmoType[AmmoIndex].TracerType = tracerType;
		AmmoType[AmmoIndex].MinSplashSize = minSplashSize;
		AmmoType[AmmoIndex].MaxSplashSize = maxSplashSize;
		AmmoType[AmmoIndex].Flags = flags;

		return true;
	}
	public void AddAmmoType(ReadOnlySpan<char> name, DamageType damageType, AmmoTracer tracerType, int plr_dmg, int npc_dmg, int carry, float physicsForceImpulse, AmmoFlags flags, int minSplashSize = 4, int maxSplashSize = 8) {
		if (AddAmmoType(name, damageType, tracerType, flags, minSplashSize, maxSplashSize) == false)
			return;

		AmmoType[AmmoIndex].PlrDmg = plr_dmg;
		AmmoType[AmmoIndex].NPCDmg = npc_dmg;
		AmmoType[AmmoIndex].MaxCarry = carry;
		AmmoType[AmmoIndex].PhysicsForceImpulse = physicsForceImpulse;

		AmmoIndex++;
	}
	public void AddAmmoType(ReadOnlySpan<char> name, DamageType damageType, AmmoTracer tracerType, ReadOnlySpan<char> plr_cvar, ReadOnlySpan<char> npc_cvar, ReadOnlySpan<char> carry_cvar, float physicsForceImpulse, AmmoFlags flags, int minSplashSize = 4, int maxSplashSize = 8) {
		if (AddAmmoType(name, damageType, tracerType, flags, minSplashSize, maxSplashSize) == false)
			return;

#if CLIENT_DLL || GAME_DLL
		if (!plr_cvar.IsEmpty) {
			AmmoType[AmmoIndex].PlrDmgCVar = cvar.FindVar(plr_cvar);
			if (AmmoType[AmmoIndex].PlrDmgCVar == null)
				Msg($"ERROR: Ammo ({name}) found no CVar named ({plr_cvar})\n");

			AmmoType[AmmoIndex].PlrDmg = USE_CVAR;
		}
		if (!npc_cvar.IsEmpty) {
			AmmoType[AmmoIndex].NPCDmgCVar = cvar.FindVar(npc_cvar);
			if (AmmoType[AmmoIndex].NPCDmgCVar == null)
				Msg($"ERROR: Ammo ({name}) found no CVar named ({npc_cvar})\n");

			AmmoType[AmmoIndex].NPCDmg = USE_CVAR;
		}
		if (!carry_cvar.IsEmpty) {
			AmmoType[AmmoIndex].MaxCarryCVar = cvar.FindVar(carry_cvar);
			if (AmmoType[AmmoIndex].MaxCarryCVar == null)
				Msg($"ERROR: Ammo ({name}) found no CVar named ({carry_cvar})\n");

			AmmoType[AmmoIndex].MaxCarry = USE_CVAR;
		}
#endif
		AmmoType[AmmoIndex].PhysicsForceImpulse = physicsForceImpulse;
		AmmoIndex++;
	}
}
