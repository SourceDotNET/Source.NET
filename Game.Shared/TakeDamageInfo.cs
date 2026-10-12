#if CLIENT_DLL || GAME_DLL
global using static Game.Shared.TakeDamageInfoGlobals;

using Game.Server;
using Game.Shared;

using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Shared;

public enum CritType
{
	None,
	Mini,
	Full
}

public struct MultiDamage
{
	public TakeDamageInfo TakeDamageInfo;
	public MultiDamage() {

	}

	public readonly bool IsClear() => Target.Get() == null;
	public readonly BaseEntity? GetTarget() => Target.Get();
	public void SetTarget(BaseEntity? target) => Target.Set(target);

	public void Init(BaseEntity? target, BaseEntity? inflictor, BaseEntity? attacker, BaseEntity? weapon, in Vector3 damageForce, in Vector3 damagePosition, in Vector3 reportedPosition, float damage, DamageType bitsDamageType, int killType) {
		Target.Set(target);
		TakeDamageInfo.Init(target, attacker, weapon, damageForce, damagePosition, reportedPosition, damage, bitsDamageType, killType);
	}

	public EHANDLE Target;
}

public static class TakeDamageInfoGlobals
{
	public static MultiDamage g_MultiDamage = new();

	public static void ClearMultiDamage() {
		g_MultiDamage.Init(null, null, null, null, vec3_origin, vec3_origin, vec3_origin, 0, 0, 0);
	}

	public static void ApplyMultiDamage() {
		if (null == g_MultiDamage.GetTarget())
			return;

#if !CLIENT_DLL
		BaseEntity? host = te.GetSuppressHost();
		te.SetSuppressHost(null);

		g_MultiDamage.GetTarget().TakeDamage(ref g_MultiDamage.TakeDamageInfo);

		te.SetSuppressHost(host);
#endif

		// Damage is done, clear it out
		ClearMultiDamage();
	}

	static int AddMultiDamage__warningCount = 0;

	public static void AddMultiDamage(in TakeDamageInfo info, BaseEntity? entity) {
		if (entity == null)
			return;

		if (entity != g_MultiDamage.GetTarget()) {
			ApplyMultiDamage();
			g_MultiDamage.Init(entity, info.GetInflictor(), info.GetAttacker(), info.GetWeapon(), vec3_origin, vec3_origin, vec3_origin, 0.0f, info.GetDamageType(), info.GetDamageCustom());
		}

		g_MultiDamage.TakeDamageInfo.AddDamageType(info.GetDamageType());
		g_MultiDamage.TakeDamageInfo.SetDamage(g_MultiDamage.TakeDamageInfo.GetDamage() + info.GetDamage());
		g_MultiDamage.TakeDamageInfo.SetDamageForce(g_MultiDamage.TakeDamageInfo.GetDamageForce() + info.GetDamageForce());
		g_MultiDamage.TakeDamageInfo.SetDamagePosition(info.GetDamagePosition());
		g_MultiDamage.TakeDamageInfo.SetReportedPosition(info.GetReportedPosition());
		g_MultiDamage.TakeDamageInfo.SetMaxDamage(Math.Max(g_MultiDamage.TakeDamageInfo.GetMaxDamage(), info.GetDamage()));
		g_MultiDamage.TakeDamageInfo.SetAmmoType(info.GetAmmoType());
		g_MultiDamage.TakeDamageInfo.SetCritType(info.GetCritType());

		if (g_MultiDamage.TakeDamageInfo.GetPlayerPenetrationCount() == 0) 
			g_MultiDamage.TakeDamageInfo.SetPlayerPenetrationCount(info.GetPlayerPenetrationCount());

		bool bHasPhysicsForceDamage = !g_pGameRules.Damage_NoPhysicsForce(info.GetDamageType());
		if (bHasPhysicsForceDamage && g_MultiDamage.TakeDamageInfo.GetDamageType() != DamageType.Generic) {
			// If you hit this assert, you've called TakeDamage with a damage type that requires a physics damage
			// force & position without specifying one or both of them. Decide whether your damage that's causing 
			// this is something you believe should impart physics force on the receiver. If it is, you need to 
			// setup the damage force & position inside the CTakeDamageInfo (Utility functions for this are in
			// takedamageinfo.cpp. If you think the damage shouldn't cause force (unlikely!) then you can set the 
			// damage type to DMG_GENERIC, or | DMG_CRUSH if you need to preserve the damage type for purposes of HUD display.
			if (g_MultiDamage.TakeDamageInfo.GetDamageForce() == vec3_origin || g_MultiDamage.TakeDamageInfo.GetDamagePosition() == vec3_origin) {
				if (++AddMultiDamage__warningCount < 10) {
					if (g_MultiDamage.TakeDamageInfo.GetDamageForce() == vec3_origin) 
						Warning("AddMultiDamage:  g_MultiDamage.GetDamageForce() == vec3_origin\n");
					if (g_MultiDamage.TakeDamageInfo.GetDamagePosition() == vec3_origin) 
						Warning("AddMultiDamage:  g_MultiDamage.GetDamagePosition() == vec3_origin\n");
				}
			}
		}
	}

	public static readonly ConVar phys_pushscale = new( "phys_pushscale", "1", FCvar.Replicated);



	public static float ImpulseScale(float flTargetMass, float flDesiredSpeed) {
		return (flTargetMass * flDesiredSpeed);
	}

	public static void CalculateExplosiveDamageForce(ref TakeDamageInfo info, in Vector3 vecDir, in Vector3 vecForceOrigin, float flScale = 1) {
		info.SetDamagePosition(vecForceOrigin);

		float flClampForce = ImpulseScale(75, 400);

		// Calculate an impulse large enough to push a 75kg man 4 in/sec per point of damage
		float flForceScale = info.GetBaseDamage() * ImpulseScale(75, 4);

		if (flForceScale > flClampForce)
			flForceScale = flClampForce;

		// Fudge blast forces a little bit, so that each
		// victim gets a slightly different trajectory. 
		// This simulates features that usually vary from
		// person-to-person variables such as bodyweight,
		// which are all indentical for characters using the same model.
		flForceScale *= random.RandomFloat(0.85f, 1.15f);

		// Calculate the vector and stuff it into the takedamageinfo
		Vector3 vecForce = vecDir;
		MathLib.VectorNormalize(ref vecForce);
		vecForce *= flForceScale;
		vecForce *= phys_pushscale.GetFloat();
		vecForce *= flScale;
		info.SetDamageForce(vecForce);
	}

	public static void CalculateBulletDamageForce(ref TakeDamageInfo info, int iBulletType, in Vector3 vecBulletDir, in Vector3 vecForceOrigin, float flScale) {
		info.SetDamagePosition(vecForceOrigin);
		Vector3 vecForce = vecBulletDir;
		MathLib.VectorNormalize(ref vecForce);
		vecForce *= GetAmmoDef().DamageForce(iBulletType);
		vecForce *= phys_pushscale.GetFloat();
		vecForce *= flScale;
		info.SetDamageForce(vecForce);
		Assert(vecForce != vec3_origin);
	}

	public static void CalculateMeleeDamageForce(ref TakeDamageInfo info, in Vector3 vecMeleeDir, in Vector3 vecForceOrigin, float flScale) {
		info.SetDamagePosition(vecForceOrigin);

		// Calculate an impulse large enough to push a 75kg man 4 in/sec per point of damage
		float flForceScale = info.GetBaseDamage() * ImpulseScale(75, 4);
		Vector3 vecForce = vecMeleeDir;
		MathLib.VectorNormalize(ref vecForce);
		vecForce *= flForceScale;
		vecForce *= phys_pushscale.GetFloat();
		vecForce *= flScale;
		info.SetDamageForce(vecForce);
	}

	public static void GuessDamageForce(ref TakeDamageInfo info, in Vector3 vecForceDir, in Vector3 vecForceOrigin, float flScale) {
		if ((info.GetDamageType() & DamageType.Bullet) != 0)
			CalculateBulletDamageForce(ref info, GetAmmoDef().Index("SMG1"), vecForceDir, vecForceOrigin, flScale);
		else if ((info.GetDamageType() & DamageType.Blast) != 0) 
			CalculateExplosiveDamageForce(ref info, vecForceDir, vecForceOrigin, flScale);
		else 
			CalculateMeleeDamageForce(ref info, vecForceDir, vecForceOrigin, flScale);
	}


	public static string[] s_DamageTypeToStrTable = [
			"GENERIC",
		"CRUSH",
		"BULLET",
		"SLASH",
		"BURN",
		"VEHICLE",
		"FALL",
		"BLAST",
		"CLUB",
		"SHOCK",
		"SONIC",
		"ENERGYBEAM",
		"PREVENT_PHYSICS_FORCE",
		"NEVERGIB",
		"ALWAYSGIB",
		"DROWN",
		"PARALYZE",
		"NERVEGAS",
		"POISON",
		"RADIATION",
		"DROWNRECOVER",
		"ACID",
		"SLOWBURN",
		"REMOVENORAGDOLL",
		"PHYSGUN",
		"PLASMA",
		"AIRBOAT",
		"DISSOLVE",
		"BLAST_SURFACE",
		"DIRECT",
		"BUCKSHOT"
		];
	public static int DAMAGE_TYPE_STR_TABLE_ENTRIES = s_DamageTypeToStrTable.Length;
}

public struct TakeDamageInfo
{
	public const float BASEDAMAGE_NOT_SPECIFIED = float.MaxValue;


	public TakeDamageInfo()
		=> Init(null, null, null, vec3_origin, vec3_origin, vec3_origin, 0, 0, 0);
	public TakeDamageInfo(BaseEntity? inflictor, BaseEntity?  attacker, float damage, DamageType bitsDamageType, int iKillType = 0)
		=> Set(inflictor, attacker, damage, bitsDamageType, iKillType);
	public TakeDamageInfo(BaseEntity? inflictor, BaseEntity? attacker, BaseEntity weapon, float damage, DamageType bitsDamageType, int iKillType = 0)
		=> Set(inflictor, attacker, weapon, damage, bitsDamageType, iKillType);
	public TakeDamageInfo(BaseEntity? inflictor, BaseEntity? attacker, in Vector3 damageForce, in Vector3 damagePosition, float damage, DamageType bitsDamageType, int iKillType = 0, Vector3? reportedPosition = null)
		=> Set(inflictor, attacker, damageForce, damagePosition, damage, bitsDamageType, iKillType, reportedPosition);
	public TakeDamageInfo(BaseEntity? inflictor, BaseEntity? attacker, BaseEntity  weapon, in Vector3 damageForce, in Vector3 damagePosition, float damage, DamageType bitsDamageType, int iKillType = 0, Vector3? reportedPosition = null)
		=> Set(inflictor, attacker, weapon, damageForce, damagePosition, damage, bitsDamageType, iKillType, reportedPosition);



	// Inflictor is the weapon or rocket (or player) that is dealing the damage.
	public BaseEntity? GetInflictor() => Inflictor.Get();
	public void SetInflictor(BaseEntity? inflictor) => Inflictor.Set(inflictor);

	// Weapon is the weapon that did the attack.
	// For hitscan weapons, it'll be the same as the inflictor. For projectile weapons, the projectile 
	// is the inflictor, and this contains the weapon that created the projectile.
	public BaseEntity? GetWeapon() => Weapon.Get();
	public void SetWeapon(BaseEntity? weapon) => Weapon.Set(weapon);

	// Attacker is the character who originated the attack (like a player or an AI).
	public BaseEntity? GetAttacker() => Attacker.Get();
	public void SetAttacker(BaseEntity? attacker) => Attacker.Set(attacker);

	public float GetDamage() => Damage;
	public void SetDamage(float damage) => Damage = damage;
	public float GetMaxDamage() => MaxDamage;
	public void SetMaxDamage(float maxDamage) => MaxDamage = maxDamage;
	public void ScaleDamage(float scaleAmount) => Damage *= scaleAmount;
	public void AddDamage(float addAmount) => Damage += addAmount;
	public void SubtractDamage(float subtractAmount) => Damage -= subtractAmount;
	public float GetDamageBonus() => DamageBonus;
	public BaseEntity? GetDamageBonusProvider() => DamageBonusProvider.Get();
	public void SetDamageBonus(float bonus, BaseEntity? provider = null) {
		DamageBonus = bonus;
		DamageBonusProvider.Set(provider);
	}

	public float GetBaseDamage() => BaseDamageIsValid() ? BaseDamage : Damage;
	public bool BaseDamageIsValid() => BaseDamage != BASEDAMAGE_NOT_SPECIFIED;

	public Vector3 GetDamageForce() => DamageForce;
	public void SetDamageForce(in Vector3 damageForce) => DamageForce = damageForce;
	public void ScaleDamageForce(float scaleAmount) => DamageForce *= scaleAmount;
	public float GetDamageForForceCalc() => DamageForForce;
	public void SetDamageForForceCalc(float scaleAmount) => DamageForForce = scaleAmount;

	public Vector3 GetDamagePosition() => DamagePosition;
	public void SetDamagePosition(in Vector3 damagePosition) => DamagePosition = damagePosition;

	public Vector3 GetReportedPosition() => ReportedPosition;
	public void SetReportedPosition(in Vector3 reportedPosition) => ReportedPosition = reportedPosition;

	public DamageType GetDamageType() => BitsDamageType;
	public void SetDamageType(DamageType bitsDamageType) => BitsDamageType = bitsDamageType;
	public void AddDamageType(DamageType bitsDamageType) => BitsDamageType |= bitsDamageType;
	public int GetDamageCustom() => DamageCustom;
	public void SetDamageCustom(int damageCustom) => DamageCustom = damageCustom;
	public int GetDamageStats() => DamageStats;
	public void SetDamageStats(int damageStats) => DamageStats = damageStats;
	public void SetForceFriendlyFire(bool value) => ForceFriendlyFire = value;
	public bool IsForceFriendlyFire() => ForceFriendlyFire;

	public int GetAmmoType() => AmmoType;
	public void SetAmmoType(int ammoType) => AmmoType = ammoType;
	public ReadOnlySpan<char> GetAmmoName() {
		ReadOnlySpan<char> ammoType;

		if (AmmoType >= 0)
			ammoType = GetAmmoDef().GetAmmoOfIndex(AmmoType).Name;
		// no ammoType, so get the ammo name from the inflictor
		else if (Inflictor.Get() != null) {
			ammoType = Inflictor.Get()!.GetClassname();

			// check for physgun ammo. unfortunate that this is in game_shared.
			if (strcmp(ammoType, "prop_physics") == 0)
				ammoType = Inflictor.Get()!.GetModelName();
		}
		else
			ammoType = "Unknown";

		return ammoType;
	}

	public int GetPlayerPenetrationCount() => PlayerPenetrationCount;
	public void SetPlayerPenetrationCount(int playerPenetrationCount) => PlayerPenetrationCount = playerPenetrationCount;

	public int GetDamagedOtherPlayers() => DamagedOtherPlayers;
	public void SetDamagedOtherPlayers(int val) => DamagedOtherPlayers = val;

	public void Set(BaseEntity? pInflictor, BaseEntity? pAttacker, float flDamage, DamageType bitsDamageType, int iKillType) {
		Init(pInflictor, pAttacker, null, vec3_origin, vec3_origin, vec3_origin, flDamage, bitsDamageType, iKillType);
	}

	public void Set(BaseEntity? pInflictor, BaseEntity? pAttacker, BaseEntity? pWeapon, float flDamage, DamageType bitsDamageType, int iKillType) {
		Init(pInflictor, pAttacker, pWeapon, vec3_origin, vec3_origin, vec3_origin, flDamage, bitsDamageType, iKillType);
	}

	public void Set(BaseEntity? pInflictor, BaseEntity? pAttacker, in Vector3 damageForce, in Vector3 damagePosition, float flDamage, DamageType bitsDamageType, int iKillType, Vector3? reportedPosition) {
		Set(pInflictor, pAttacker, null, damageForce, damagePosition, flDamage, bitsDamageType, iKillType, reportedPosition);
	}

	public void Set(BaseEntity? pInflictor, BaseEntity? pAttacker, BaseEntity? pWeapon, in Vector3 damageForce, in Vector3 damagePosition, float flDamage, DamageType bitsDamageType, int iKillType, Vector3? reportedPosition) {
		Vector3 vecReported = vec3_origin;
		if (reportedPosition != null)
			vecReported = reportedPosition.Value;

		Init(pInflictor, pAttacker, pWeapon, damageForce, damagePosition, vecReported, flDamage, bitsDamageType, iKillType);
	}


	public void AdjustPlayerDamageInflictedForSkillLevel() {
#if !CLIENT_DLL
		CopyDamageToBaseDamage();
		SetDamage(g_pGameRules.AdjustPlayerDamageInflicted(GetDamage()));
#endif
	}
	public void AdjustPlayerDamageTakenForSkillLevel() {
#if !CLIENT_DLL
		CopyDamageToBaseDamage();
		g_pGameRules.AdjustPlayerDamageTaken(ref this);
#endif
	}

	public static void DebugGetDamageTypeString(uint damageType, Span<char> outbuf) {
		Assert(outbuf.Length > 0);

		// we need to use snprintf to actually copy out the strings here because that's the only function that returns
		// how much text was output
		if (damageType == 0) {
			int charsWrit = sprintf(outbuf, "%s").S(s_DamageTypeToStrTable[0]);
			outbuf = outbuf[charsWrit..];
		}

		// loop through the other entries in the table
		for (int i = 0; outbuf.Length > 0 && i < (DAMAGE_TYPE_STR_TABLE_ENTRIES - 1); ++i) {
			if ((damageType & (1 << i)) != 0) {
				// this bit was set. Print the corresponding entry from the table
				// (the index is +1 because entry 1 in the table corresponds to 1 << 0)
				int charsWrit = sprintf(outbuf, "%s ").S(s_DamageTypeToStrTable[i + 1]);
				outbuf = outbuf[charsWrit..];
			}
		}
	}

	public void SetCritType(CritType type) => CritType = type;

	public CritType GetCritType() => CritType;

	public void CopyDamageToBaseDamage() {
		BaseDamage = Damage;
	}

	public void Init(BaseEntity? inflictor, BaseEntity? attacker, BaseEntity? weapon, in Vector3 damageForce, in Vector3 damagePosition, in Vector3 reportedPosition, float damage, DamageType bitsDamageType, int customDamage) {
		Inflictor.Set(inflictor);
		Attacker.Set(attacker ?? inflictor);

		Weapon.Set(weapon);
		Damage = damage;
		BaseDamage = BASEDAMAGE_NOT_SPECIFIED;

		BitsDamageType = bitsDamageType;
		DamageCustom = customDamage;

		MaxDamage = damage;
		DamageForce = damageForce;
		DamagePosition = damagePosition;
		ReportedPosition = reportedPosition;
		AmmoType = -1;
		DamagedOtherPlayers = 0;
		PlayerPenetrationCount = 0;
		DamageBonus = 0;
		ForceFriendlyFire = false;
		DamageForForce = 0;
		CritType = CritType.None;
	}

	public Vector3 DamageForce;
	public Vector3 DamagePosition;
	public Vector3 ReportedPosition;   // Position players are told damage is coming from
	public EHANDLE Inflictor;
	public EHANDLE Attacker;
	public EHANDLE Weapon;
	public float Damage;
	public float MaxDamage;
	public float BaseDamage;           // The damage amount before skill leve adjustments are made. Used to get uniform damage forces.
	public DamageType BitsDamageType;
	public int DamageCustom;
	public int DamageStats;
	public int AmmoType;            // AmmoType of the weapon used to cause this damage, if any
	public int DamagedOtherPlayers;
	public int PlayerPenetrationCount;
	public float DamageBonus;      // Anything that increases damage (crit) - store the delta
	public EHANDLE DamageBonusProvider; // Who gave us the ability to do extra damage?
	public bool ForceFriendlyFire;  // Ideally this would be a dmg type, but we can't add more
	public float DamageForForce;
	public CritType CritType;
}
#endif
