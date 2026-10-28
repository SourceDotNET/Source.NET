using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

[Flags]
public enum Capability
{
	MoveGround = 0x00000001,
	MoveJump = 0x00000002,
	MoveFly = 0x00000004,
	MoveClimb = 0x00000008,
	MoveSwim = 0x00000010,
	MoveCrawl = 0x00000020,
	MoveShoot = 0x00000040,
	SkipNavGroundCheck = 0x00000080,
	Use = 0x00000100,
	AutoDoors = 0x00000400,
	OpenDoors = 0x00000800,
	TurnHead = 0x00001000,
	WeaponRangeAttack1 = 0x00002000,
	WeaponRangeAttack2 = 0x00004000,
	WeaponMeleeAttack1 = 0x00008000,
	WeaponMeleeAttack2 = 0x00010000,
	InnateRangeAttack1 = 0x00020000,
	InnateRangeAttack2 = 0x00040000,
	InnateMeleeAttack1 = 0x00080000,
	InnateMeleeAttack2 = 0x00100000,
	UseWeapons = 0x00200000,
	AnimatedFace = 0x00800000,
	UseShotRegulator = 0x01000000,
	FriendlyDmgImmune = 0x02000000,
	Squad = 0x04000000,
	Duck = 0x08000000,
	NoHitPlayer = 0x10000000,
	AimGun = 0x20000000,
	NoHitSquadmates = 0x40000000,
	SimpleRadiusDamage = unchecked((int)0x80000000),
}

public enum Disposition
{
	ER,
	HT,
	FR,
	LI,
	NU
}

public class Relationship
{
	public EHANDLE Entity = new();
	public Class_T ClassType;
	public Disposition Disposition;
	public int Priority;
}

[NetworkName("CBaseCombatCharacter")]
public partial class BaseCombatCharacter : BaseFlex
{
	public BaseCombatCharacter() {
		MyWeaponsForModify() = new();
		LastWeapon = new();
		ActiveWeapon = new();
	}

	public bool ForceServerRagdoll;

	public virtual Source.Common.Mathematics.QAngle BodyAngles() => GetAbsAngles();

	public virtual Vector3 BodyDirection2D() {
		Vector3 bodyDir = BodyDirection3D();
		bodyDir.Z = 0;
		float len = MathF.Sqrt(bodyDir.X * bodyDir.X + bodyDir.Y * bodyDir.Y);
		if (len != 0) {
			bodyDir.X /= len;
			bodyDir.Y /= len;
		}
		return bodyDir;
	}

	public virtual Vector3 BodyDirection3D() {
		Source.Common.Mathematics.QAngle angles = BodyAngles();

		// FIXME: cache this
		Source.Common.Mathematics.MathLib.AngleVectors(angles, out Vector3 bodyDir);
		return bodyDir;
	}

	public virtual Vector3 HeadDirection2D() => BodyDirection2D();
	public virtual Vector3 HeadDirection3D() => BodyDirection2D(); // No head motion so just return body dir
	public virtual Vector3 EyeDirection2D() => HeadDirection2D();
	public virtual Vector3 EyeDirection3D() => HeadDirection3D(); // No eye motion so just return head dir


	public HitGroup LastHitGroupValue;
	public HitGroup LastHitGroup() => LastHitGroupValue;

	public virtual Activity GetDeathActivity() {
		Activity deathActivity;
		bool triedDirection;
		float dot;
		Vector3 src;

		if (IsPlayer()) {
			switch (random.RandomInt(0, 7)) {
				case 0: return Activity.ACT_DIESIMPLE;
				case 1: return Activity.ACT_DIEBACKWARD;
				case 2: return Activity.ACT_DIEFORWARD;
				case 3: return Activity.ACT_DIEVIOLENT;
				case 4: return Activity.ACT_DIE_HEADSHOT;
				case 5: return Activity.ACT_DIE_CHESTSHOT;
				case 6: return Activity.ACT_DIE_GUTSHOT;
				case 7: return Activity.ACT_DIE_BACKSHOT;
			}
		}

		src = WorldSpaceCenter();

		triedDirection = false;
		deathActivity = Activity.ACT_DIESIMPLE;

		MathLib.AngleVectors(GetLocalAngles(), out Vector3 forward);
		dot = -Vector3.Dot(forward, g_vecAttackDir);

		switch (LastHitGroupValue) {
			case HitGroup.Head:
				deathActivity = Activity.ACT_DIE_HEADSHOT;
				break;

			case HitGroup.Stomach:
				deathActivity = Activity.ACT_DIE_GUTSHOT;
				break;

			case HitGroup.Generic:
				triedDirection = true;

				if (dot > 0.3)
					deathActivity = Activity.ACT_DIEFORWARD;
				else if (dot <= -0.3)
					deathActivity = Activity.ACT_DIEBACKWARD;
				break;

			default:
				triedDirection = true;

				if (dot > 0.3)
					deathActivity = Activity.ACT_DIEFORWARD;
				else if (dot <= -0.3)
					deathActivity = Activity.ACT_DIEBACKWARD;
				break;
		}

		if (SelectWeightedSequence(deathActivity) == StudioHdr.ACTIVITY_NOT_AVAILABLE) {
			if (triedDirection)
				deathActivity = Activity.ACT_DIESIMPLE;
			else {
				if (dot > 0.3)
					deathActivity = Activity.ACT_DIEFORWARD;
				else if (dot <= -0.3)
					deathActivity = Activity.ACT_DIEBACKWARD;
			}
		}

		if (SelectWeightedSequence(deathActivity) == StudioHdr.ACTIVITY_NOT_AVAILABLE) {
			deathActivity = Activity.ACT_DIESIMPLE;

			if (SelectWeightedSequence(deathActivity) == StudioHdr.ACTIVITY_NOT_AVAILABLE)
				Msg($"ERROR! {GetModelName()} missing ACT_DIESIMPLE\n");
		}

		if (deathActivity == Activity.ACT_DIEFORWARD) {
			Util.TraceHull(src, src + forward * 64, new Vector3(-16, -16, -18),
				new Vector3(16, 16, 18), Mask.Solid, this, Source.CollisionGroup.None, out Trace tr);

			if (tr.Fraction != 1.0)
				deathActivity = Activity.ACT_DIESIMPLE;
		}

		if (deathActivity == Activity.ACT_DIEBACKWARD) {
			Util.TraceHull(src, src - forward * 64, new Vector3(-16, -16, -18),
				new Vector3(16, 16, 18), Mask.Solid, this, Source.CollisionGroup.None, out Trace tr);

			if (tr.Fraction != 1.0)
				deathActivity = Activity.ACT_DIESIMPLE;
		}

		return deathActivity;
	}

	public virtual bool FInViewCone(BaseEntity entity) => FInViewCone(entity.WorldSpaceCenter());

	public virtual bool FInViewCone(in Vector3 spot) {
		Vector3 los = spot - EyePosition();

		los.Z = 0;
		Source.Common.Mathematics.MathLib.VectorNormalize(ref los);

		Vector3 facingDir = EyeDirection2D();

		float dot = Vector3.Dot(los, facingDir);

		if (dot > FieldOfView)
			return true;

		return false;
	}

	public override bool IsBaseCombatCharacter() => true;

	public virtual BaseEntity? GetVehicleEntity() => null;

	public virtual bool IsInAVehicle() => false;
	public virtual bool ExitVehicle() => false;

	[NetworkTable("DT_BCCLocalPlayerExclusive")]
	public static readonly NetworkProp[] BCCLocalPlayerExclusiveProps = [
		nameof(NextAttack),
	];

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(BCCLocalPlayerExclusiveProps)) { Name = "bcc_localdata", Proxy = typeof(BCCLocalDataProxy) },
		nameof(ActiveWeapon),
		nameof(MyWeapons),
		new(nameof(BloodColor)) { Bits = 5 },
	];

	public TimeUnit_t GetNextAttack() => NextAttack;
	public void SetNextAttack(TimeUnit_t wait) => NextAttack = wait;

	[NetworkVar("m_flNextAttack")]
	public partial TimeUnit_t NextAttack { get; set; }
	public float ImpactEnergyScale;
	[NetworkVar("m_hLastWeapon")]
	public partial Handle<BaseCombatWeapon> LastWeapon { get; set; }
	[NetworkVar("m_hActiveWeapon")]
	public partial Handle<BaseCombatWeapon> ActiveWeapon { get; set; }
	[NetworkVar("m_hMyWeapons")]
	public partial ref readonly InlineArrayNewMaxWeapons<Handle<BaseCombatWeapon>> MyWeapons { get; }
	[NetworkVar("m_iAmmo")]
	public partial ref readonly InlineArray256<int> Ammo { get; }
	[NetworkVar("m_bloodColor")]
	public partial BloodColor BloodColor { get; set; }

	public static class BCCLocalDataProxy
	{
		public static object? Send(SendProp prop, object instance, IFieldAccessor data, SendProxyRecipients recipients, int objectID) {
			recipients.ClearAllRecipients();

			BaseCombatCharacter character = (BaseCombatCharacter)instance;
			if (character != null) {
				if (character.IsPlayer())
					recipients.SetOnly(character.EntIndex() - 1);
				else {
					IServerVehicle vehicle = character.GetServerVehicle();
					if (vehicle != null) {
						BaseCombatCharacter driver = vehicle.GetPassenger();
						if (driver != null)
							recipients.SetOnly(driver.EntIndex() - 1);
					}
				}
			}

			return instance;
		}
	}
	public override void SetTransmit(CheckTransmitInfo info, bool always) {
		if (info.TransmitEdict.Get(EntIndex()) != 0)
			return;

		base.SetTransmit(info, always);

		if (info.ClientEnt == Edict()) {
			for (int i = 0; i < MAX_WEAPONS; i++) {
				BaseCombatWeapon? weapon = MyWeapons[i].Get();
				if (weapon == null)
					continue;

				weapon.SetTransmit(info, always);
			}
		}
		else {
			BaseCombatWeapon? activeWeapon = ActiveWeapon.Get();
			if (activeWeapon != null && !activeWeapon.IsEffectActive(EntityEffects.NoDraw))
				activeWeapon.SetTransmit(info, always);
		}
	}

	public void ClearLastKnownArea() {
		// TODO
	}

	public string? RelationshipString;

	public AI_HullType Hull;
	public float FieldOfView;

	public const int DEF_RELATIONSHIP_PRIORITY = int.MinValue;

	public static Relationship[][]? DefaultRelationship;
	public readonly List<Relationship> Relationship = [];

	public virtual void AddEntityRelationship(BaseEntity entity, Disposition disposition, int priority) => throw new NotImplementedException();
	public virtual void AddClassRelationship(Class_T classType, Disposition disposition, int priority) => throw new NotImplementedException();

	public virtual bool RemoveEntityRelationship(BaseEntity? entity) {
		for (int i = Relationship.Count - 1; i >= 0; i--) {
			if (Relationship[i].Entity.Get() == entity) {
				Relationship.RemoveAt(i);
				return true;
			}
		}

		return false;
	}

	public static void AllocateDefaultRelationships() {
		if (DefaultRelationship == null) {
			DefaultRelationship = new Relationship[(int)Class_T.NumAIClasses][];

			for (int i = 0; i < (int)Class_T.NumAIClasses; ++i) {
				DefaultRelationship[i] = new Relationship[(int)Class_T.NumAIClasses];
				for (int j = 0; j < (int)Class_T.NumAIClasses; ++j)
					DefaultRelationship[i][j] = new();
			}
		}
	}

	public static void SetDefaultRelationship(Class_T classType, Class_T classTarget, Disposition disposition, int priority) {
		if (DefaultRelationship != null) {
			DefaultRelationship[(int)classType][(int)classTarget].Disposition = disposition;
			DefaultRelationship[(int)classType][(int)classTarget].Priority = priority;
		}
	}

	public Disposition GetDefaultRelationshipDisposition(Class_T classTarget) {
		Assert(DefaultRelationship != null);

		return DefaultRelationship![(int)Classify()][(int)classTarget].Disposition;
	}

	static readonly Relationship DummyRelationship = new();

	public Relationship FindEntityRelationship(BaseEntity? target) {
		if (target == null)
			return DummyRelationship;

		int i;
		for (i = 0; i < Relationship.Count; i++) {
			if (target == Relationship[i].Entity.Get())
				return Relationship[i];
		}

		if (target.Classify() != Class_T.None) {
			for (i = 0; i < Relationship.Count; i++) {
				if (target.Classify() == Relationship[i].ClassType)
					return Relationship[i];
			}
		}
		AllocateDefaultRelationships();
		return DefaultRelationship![(int)Classify()][(int)target.Classify()];
	}

	public virtual Disposition IRelationType(BaseEntity? target) {
		if (target != null)
			return FindEntityRelationship(target).Disposition;
		return Disposition.NU;
	}

	public virtual int IRelationPriority(BaseEntity? target) {
		if (target != null)
			return FindEntityRelationship(target).Priority;
		return 0;
	}

	public void SetImpactEnergyScale(float scale) => ImpactEnergyScale = scale;

	static readonly ConVar phys_stressbodyweights = new("phys_stressbodyweights", "5.0");

	static readonly float MaxStressDamageForce = BitConverter.Int32BitsToSingle(0x5f0ede49);

	public override void VPhysicsUpdate(IPhysicsObject physics) {
		ApplyStressDamage(physics, false);
		base.VPhysicsUpdate(physics);
	}

	public virtual float CalculatePhysicsStressDamage(out VPhysicsObjectStress stressOut, IPhysicsObject physics) {
		float mass = physics.GetMass();
		CalculateObjectStress(physics, this, out stressOut);
		float stress = (stressOut.ReceivedStress * ImpactEnergyScale) / mass;

		if (stressOut.HasNonStaticStress && stress > phys_stressbodyweights.GetFloat()) {
			if ((physics.GetGameFlags() & PhysicsFlags.Penetrating) == 0)
				return 200;
		}

		return 0;
	}

	public virtual void ApplyStressDamage(IPhysicsObject physics, bool requireLargeObject) {
		if (Classify() == Class_T.PlayerAlly || Classify() == Class_T.PlayerAllyVital) {
			if (hl2_episodic.GetBool())
				return;
		}

		float damage = CalculatePhysicsStressDamage(out VPhysicsObjectStress stressOut, physics);
		if (damage > 0) {
			if (requireLargeObject && !stressOut.HasLargeObjectContact)
				return;

			TakeDamageInfo dmgInfo = new(GetWorldEntity(), GetWorldEntity(), vec3_origin, vec3_origin, damage, DamageType.Crush);
			float force = -stressOut.ReceivedStress * GetCurrentGravity() * (float)gpGlobals.FrameTime;
			if (force > -MaxStressDamageForce && force < MaxStressDamageForce)
				dmgInfo.SetDamageForce(new Vector3(0, 0, force));
			dmgInfo.SetDamagePosition(GetAbsOrigin());

			if (stressOut.StressObject?.GetGameData() is BaseEntity stressEntity) {
				dmgInfo.SetInflictor(stressEntity);
				dmgInfo.SetAttacker(stressEntity);

				BaseEntity? owner = stressEntity.GetOwnerEntity();
				if (owner != null)
					dmgInfo.SetAttacker(owner);
				else {
					BasePlayer? physicsAttacker = stressEntity.HasPhysicsAttacker(1.0);
					if (physicsAttacker != null)
						dmgInfo.SetAttacker(physicsAttacker);
				}
			}

			TakeDamage(dmgInfo);
		}
	}

	public AI_HullType GetHullType() => Hull;
	public void SetHullType(AI_HullType hullType) => Hull = hullType;

	public virtual Activity Weapon_TranslateActivity(Activity baseAct, ref bool required) {
		Activity translated = baseAct;

		if (ActiveWeapon.Get() != null)
			translated = ActiveWeapon.Get()!.ActivityOverride(baseAct, ref required);
		else
			required = false;

		return translated;
	}

	public virtual Activity NPC_TranslateActivity(Activity baseAct) => baseAct;

	public void Weapon_SetActivity(Activity newActivity, float duration) {
		if (ActiveWeapon.Get() != null)
			ActiveWeapon.Get()!.SetActivity(newActivity, duration);
	}

	public virtual void Weapon_FrameUpdate() {
		if (ActiveWeapon.Get() != null)
			ActiveWeapon.Get()!.Operator_FrameUpdate(this);
	}

	public virtual void Weapon_HandleAnimEvent(ref AnimEvent animEvent) {
		if (ActiveWeapon.Get() != null)
			ActiveWeapon.Get()!.Operator_HandleAnimEvent(ref animEvent, this);
	}

	public static readonly ConVar ai_show_hull_attacks = new("ai_show_hull_attacks", "0");

	public virtual BaseEntity? CheckTraceHullAttack(float dist, in Vector3 mins, in Vector3 maxs, float damage, DamageType dmgType, float forceScale = 1.0f, bool damageAnyNPC = false) {
		MathLib.AngleVectors(GetAbsAngles(), out Vector3 forward);
		Vector3 start = GetAbsOrigin();

		float verticalOffset = WorldAlignSize().Z * 0.5f;

		if (verticalOffset < maxs.Z)
			verticalOffset = maxs.Z + 1.0f;

		start.Z += verticalOffset;
		Vector3 end = start + (forward * dist);
		return CheckTraceHullAttack(start, end, mins, maxs, damage, dmgType, forceScale, damageAnyNPC);
	}

	public virtual BaseEntity? CheckTraceHullAttack(in Vector3 start, in Vector3 end, in Vector3 mins, in Vector3 maxs, float damage, DamageType dmgType, float forceScale = 1.0f, bool damageAnyNPC = false) {
		if (ai_show_hull_attacks.GetBool()) {
			float length = (end - start).Length();
			Vector3 direction = end - start;
			MathLib.VectorNormalize(ref direction);
			Vector3 hullMaxs = maxs;
			hullMaxs.X = length + hullMaxs.X;
			DebugOverlay.BoxDirection(start, mins, hullMaxs, direction, 100, 255, 255, 20, 1.0f);
			DebugOverlay.BoxDirection(start, mins, maxs, direction, 255, 0, 0, 20, 1.0f);
		}

		TakeDamageInfo dmgInfo = new(this, this, damage, dmgType);

		TraceFilterMelee traceFilter = new(this, Source.CollisionGroup.Projectile, dmgInfo, forceScale, damageAnyNPC);

		Ray ray = default;
		ray.Init(start, end, mins, maxs);

		enginetrace.TraceRay(in ray, Mask.ShotHull, ref traceFilter, out Trace tr);

		BaseEntity? entity = traceFilter.Hit;

		if (entity == null) {
			Vector3 topCenter = GetAbsOrigin();
			CollisionProp().WorldSpaceAABB(out _, out Vector3 aabbMaxs);
			topCenter.Z = aabbMaxs.Z + 1.0f;

			ray.Init(topCenter, end, mins, maxs);
			enginetrace.TraceRay(in ray, Mask.ShotHull, ref traceFilter, out tr);

			entity = traceFilter.Hit;
		}

		if (entity != null && !entity.CanBeHitByMeleeAttack(this))
			entity = null;

		return entity;
	}

	public bool Weapon_IsOnGround(BaseCombatWeapon weapon) {
		if (weapon.IsConstrained())
			return false;

		if (MathF.Abs(weapon.WorldSpaceCenter().Z - GetAbsOrigin().Z) >= 12.0f)
			return false;

		return true;
	}

	public BaseEntity? Weapon_FindUsable(in Vector3 range) {
		bool conservative = false;

#if HL2_DLL
		if (hl2_episodic.GetBool() && GetActiveWeapon() == null) {
			if (Classify() != Class_T.PlayerAllyVital)
				conservative = true;
		}
#endif

		Span<BaseCombatWeapon?> weaponList = new BaseCombatWeapon?[64];
		BaseCombatWeapon? bestWeapon = null;

		Vector3 mins = GetAbsOrigin() - range;
		Vector3 maxs = GetAbsOrigin() + range;
		int listCount = BaseCombatWeapon.GetAvailableWeaponsInBox(weaponList, mins, maxs);

		float bestDist = 1e6f;

		for (int i = 0; i < listCount; i++) {
			BaseCombatWeapon weapon = weaponList[i]!;
			weapon.GetVelocity(out Vector3 velocity, out _);

			if (weapon.CanBePickedUpByNPCs() == false)
				continue;

			if (velocity.LengthSquared() > 1 || !Weapon_CanUse(weapon))
				continue;

			if (weapon.IsLocked(this))
				continue;

			if (GetActiveWeapon() != null) {
				if (GetActiveWeapon()!.GetClassname().Equals(weapon.GetClassname(), StringComparison.Ordinal))
					continue;

				if (FClassnameIs(weapon, "weapon_pistol"))
					continue;
			}

			float curDist = (weapon.GetLocalOrigin() - GetLocalOrigin()).Length();

			if (weapon.HasSpawnFlags(BaseCombatWeapon.SF.NoPlayerPickup))
				curDist *= 0.5f;

			if (bestWeapon != null) {
				if (FClassnameIs(weapon, "weapon_ar2"))
					curDist *= 0.5f;

				if ((weapon.CapabilitiesGet() & (Capability.WeaponRangeAttack1 | Capability.WeaponRangeAttack2)) == 0)
					continue;
				else if (curDist > bestDist)
					continue;
			}

			if (Weapon_IsOnGround(weapon)) {
				Vector3 aboveWeapon = weapon.GetAbsOrigin();
				TraceFilterSimple filter = new(weapon, Source.CollisionGroup.None);
				Util.TraceEntity(this, aboveWeapon, aboveWeapon + new Vector3(0, 0, 1), Mask.Solid, ref filter, out Trace tr);

				if (tr.StartSolid || (tr.Fraction < 1.0))
					continue;
			}
			else if (conservative)
				continue;

			if (FVisible(weapon)) {
				bestDist = curDist;
				bestWeapon = weapon;
			}
		}

		bestWeapon?.Lock(2.0, this);

		return bestWeapon;
	}

	public override void UpdateOnRemove() {
		int i;
		for (i = 0; i < MAX_WEAPONS; i++) {
			if (MyWeapons[i].Get() != null)
				Util.Remove(MyWeapons[i].Get());
		}

		BaseEntity? owner = GetOwnerEntity();
		if (owner != null) {
			owner.DeathNotice(this);
			SetOwnerEntity(null);
		}

		base.UpdateOnRemove();
	}

	public BaseCombatWeapon? Weapon_Create(ReadOnlySpan<char> weaponName) {
		BaseCombatWeapon? weapon = (BaseCombatWeapon?)Create(weaponName, GetLocalOrigin(), GetLocalAngles(), this);

		return weapon;
	}
	public bool NoAmmo;

	public override BaseCombatCharacter? MyCombatCharacterPointer() => this;

	public virtual void Weapon_Equip(BaseCombatWeapon weapon) {
		for (int i = 0; i < MAX_WEAPONS; i++) {
			if (MyWeapons[i].Get() == null) {
				MyWeaponsForModify()[i].Set(weapon);
				break;
			}
		}

		weapon.ChangeTeam(GetTeamNumber());

		if (!NoAmmo && !weapon.WasDropped()) {
			if (weapon.GetMaxClip1() == -1)
				GiveAmmo(weapon.GetDefaultClip1(), weapon.PrimaryAmmoType);
			else if (weapon.GetDefaultClip1() > weapon.GetMaxClip1()) {
				weapon.iClip1 = weapon.GetMaxClip1();
				GiveAmmo(weapon.GetDefaultClip1() - weapon.GetMaxClip1(), weapon.PrimaryAmmoType);
			}

			if (weapon.GetMaxClip2() == -1)
				GiveAmmo(weapon.GetDefaultClip2(), weapon.SecondaryAmmoType);
			else if (weapon.GetDefaultClip2() > weapon.GetMaxClip2()) {
				weapon.iClip2 = weapon.GetMaxClip2();
				GiveAmmo(weapon.GetDefaultClip2() - weapon.GetMaxClip2(), weapon.SecondaryAmmoType);
			}
		}

		weapon.Equip(this);

		if (IsPlayer() == false) {
			if (ActiveWeapon.Get() != null) {
				ActiveWeapon.Get()!.Holster();
				ActiveWeapon.Get()!.AddEffects(EntityEffects.NoDraw);
			}
			SetActiveWeapon(weapon);
			ActiveWeapon.Get()!.RemoveEffects(EntityEffects.NoDraw);
		}

		if (IsPlayer() == false) {
			if (HasSpawnFlags(AI_BaseNPC.SF.LongRange)) {
				ActiveWeapon.Get()!.MaxRange1 = 999999999;
				ActiveWeapon.Get()!.MaxRange2 = 999999999;
			}
		}

		WeaponProficiency proficiency;
		proficiency = CalcWeaponProficiency(weapon);

		if (BaseCombatWeapon.weapon_showproficiency.GetBool())
			Msg($"{GetClassname()} equipped with {weapon.GetClassname()}, proficiency is {GetWeaponProficiencyName(proficiency)}\n");

		SetCurrentWeaponProficiency(proficiency);

		weapon.SetLightingOriginRelative(GetLightingOriginRelative());
	}

	public bool Weapon_EquipAmmoOnly(BaseCombatWeapon weapon) {
		for (int i = 0; i < MAX_WEAPONS; i++) {
			if (MyWeapons[i].Get() != null && FClassnameIs(MyWeapons[i].Get(), weapon.GetClassname())) {
				int primaryGiven = (weapon.UsesClipsForAmmo1()) ? weapon.iClip1 : weapon.GetPrimaryAmmoCount();
				int secondaryGiven = (weapon.UsesClipsForAmmo2()) ? weapon.iClip2 : weapon.GetSecondaryAmmoCount();

				int takenPrimary = GiveAmmo(primaryGiven, weapon.PrimaryAmmoType);
				int takenSecondary = GiveAmmo(secondaryGiven, weapon.SecondaryAmmoType);

				if (weapon.UsesClipsForAmmo1())
					weapon.iClip1 -= takenPrimary;
				else
					weapon.SetPrimaryAmmoCount(weapon.GetPrimaryAmmoCount() - takenPrimary);

				if (weapon.UsesClipsForAmmo2())
					weapon.iClip2 -= takenSecondary;
				else
					weapon.SetSecondaryAmmoCount(weapon.GetSecondaryAmmoCount() - takenSecondary);

				if (takenPrimary > 0 || takenSecondary > 0)
					return true;

				return false;
			}
		}

		return false;
	}

	public virtual bool Weapon_CanUse(BaseCombatWeapon weapon) {
		ReadOnlySpan<BaseCombatWeapon.ActTable> table = weapon.ActivityList();

		if (table.Length < 1)
			return false;

		for (int i = 0; i < table.Length; i++) {
			if (table[i].Required) {
				Activity translatedActivity = NPC_TranslateActivity(table[i].WeaponAct);

				if (SelectWeightedSequence(translatedActivity) == StudioHdr.ACTIVITY_NOT_AVAILABLE)
					return false;
			}
		}

		return true;
	}

	public virtual int GiveAmmo(int count, int ammoIndex, bool suppressSound = false) {
		if (count <= 0)
			return 0;

		if (!g_pGameRules.CanHaveAmmo(this, ammoIndex))
			return 0;

		if (ammoIndex < 0 || ammoIndex >= MAX_AMMO_SLOTS)
			return 0;

		int max = GetAmmoDef().MaxCarry(ammoIndex);
		int add = Math.Min(count, max - Ammo[ammoIndex]);
		if (add < 1)
			return 0;

		if (!suppressSound)
			EmitSound("BaseCombatCharacter.AmmoPickup");

		AmmoForModify()[ammoIndex] = Ammo[ammoIndex] + add;

		return add;
	}

	public int GiveAmmo(int count, ReadOnlySpan<char> name, bool suppressSound = false) {
		int ammoType = GetAmmoDef().Index(name);
		if (ammoType == -1) {
			Msg($"ERROR: Attempting to give unknown ammo type ({name})\n");
			return 0;
		}
		return GiveAmmo(count, ammoType, suppressSound);
	}

	public void SetActiveWeapon(BaseCombatWeapon? newWeapon) {
		BaseCombatWeapon? oldWeapon = ActiveWeapon.Get();
		if (newWeapon != oldWeapon) {
			ActiveWeapon = Handle<BaseCombatWeapon>.FromEntity(newWeapon);
			OnChangeActiveWeapon(oldWeapon, newWeapon);
		}
	}

	public virtual void OnChangeActiveWeapon(BaseCombatWeapon? oldWeapon, BaseCombatWeapon? newWeapon) { }

	public bool PreventWeaponPickup;
	public bool IsAllowedToPickupWeapons() => !PreventWeaponPickup;

	public void SetCurrentWeaponProficiency(WeaponProficiency proficiency) => CurrentWeaponProficiency = proficiency;
	public virtual WeaponProficiency CalcWeaponProficiency(BaseCombatWeapon? weapon) => WeaponProficiency.Average;

	public bool DispatchInteraction(AI_Interaction interactionType, object? data, BaseCombatCharacter? sourceEnt) => (interactionType > AI_Interaction.None) ? HandleInteraction(interactionType, data, sourceEnt) : false;

	public virtual bool HandleInteraction(AI_Interaction interactionType, object? data, BaseCombatCharacter? sourceEnt) => false;

	public override void SetLightingOriginRelative(BaseEntity? lightingOrigin) {
		base.SetLightingOriginRelative(lightingOrigin);
		if (GetActiveWeapon() != null)
			GetActiveWeapon()!.SetLightingOriginRelative(lightingOrigin);
	}

	public int WeaponCount() => MAX_WEAPONS;
	public BaseCombatWeapon? GetWeapon(int i) => MyWeapons[i].Get();

	public override void DoMuzzleFlash() {
		BaseCombatWeapon? weapon = GetActiveWeapon();
		if (weapon != null)
			weapon.DoMuzzleFlash();
		else
			base.DoMuzzleFlash();
	}

	WeaponProficiency CurrentWeaponProficiency;

	public WeaponProficiency GetCurrentWeaponProficiency() => CurrentWeaponProficiency;

	public Vector3 GetAttackSpread(BaseCombatWeapon? weapon, BaseEntity? target = null) {
		if (weapon != null)
			return weapon.GetBulletSpread(GetCurrentWeaponProficiency());
		return VECTOR_CONE_15DEGREES;
	}
}

public struct TraceFilterMelee(IHandleEntity? passentity, CollisionGroup collisionGroup, TakeDamageInfo dmgInfo, float forceScale, bool damageAnyNPC) : ITraceFilter
{
	public IHandleEntity? PassEnt = passentity;
	public CollisionGroup CollisionGroup = collisionGroup;
	public TakeDamageInfo DmgInfo = dmgInfo;
	public BaseEntity? Hit;
	public float ForceScale = forceScale;
	public bool DamageAnyNPC = damageAnyNPC;

	public bool ShouldHitEntity(IHandleEntity handleEntity, Contents contentsMask) {
		if (!StandardFilterRules(handleEntity, contentsMask))
			return false;

		if (!PassServerEntityFilter(handleEntity, PassEnt))
			return false;

		BaseEntity? entity = EntityFromEntityHandle(handleEntity);

		if (entity != null) {
			if (!entity.ShouldCollide(CollisionGroup, contentsMask))
				return false;

			if (!g_pGameRules.ShouldCollide(CollisionGroup, entity.GetCollisionGroup()))
				return false;

			if (entity.m_takedamage == (byte)Damage.No)
				return false;

			Vector3 attackDir = entity.WorldSpaceCenter() - DmgInfo.GetAttacker()!.WorldSpaceCenter();
			MathLib.VectorNormalize(ref attackDir);

			TakeDamageInfo info = DmgInfo;
			CalculateMeleeDamageForce(ref info, attackDir, info.GetAttacker()!.WorldSpaceCenter(), ForceScale);

			BaseCombatCharacter? bcc = ToBaseCombatCharacter(info.GetAttacker());
			BaseCombatCharacter? victimBCC = ToBaseCombatCharacter(entity);

			if (bcc != null && victimBCC != null) {
				if (DamageAnyNPC || bcc.IRelationType(entity) == Disposition.HT) {
					if (info.GetDamage() != 0)
						entity.TakeDamage(info);

					SoundEnt.InsertSound(SoundInstanceType.Combat, info.GetDamagePosition(), 200, 0.2f, info.GetAttacker());

					Hit = entity;
					return true;
				}
			}
			else {
				Hit = entity;

				Pickup.ForcePlayerToDropThisObject(entity);

				if (info.GetDamage() != 0)
					entity.TakeDamage(info);
			}
		}

		return false;
	}

	public readonly TraceType GetTraceType() => TraceType.EntitiesOnly;
}
