using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Physics;

using System.Numerics;

namespace Game.Server;

using DEFINE = Source.DEFINE<BaseCombatWeapon>;

public partial class BaseCombatWeapon : BaseAnimating
{
	public static readonly ConVar weapon_showproficiency = new("weapon_showproficiency", "0");

	public OutputEvent OnPlayerPickup = new();
	public OutputEvent OnNPCPickup = new();
	public OutputEvent OnCacheInteraction = new();

	public static readonly new DataMap DataDesc = new(typeof(BaseCombatWeapon), BaseEntity.DataDesc, [
		DEFINE.OUTPUT(nameof(OnPlayerPickup), "OnPlayerPickup", eventFuncs),
		DEFINE.OUTPUT(nameof(OnNPCPickup), "OnNPCPickup", eventFuncs),
		DEFINE.OUTPUT(nameof(OnCacheInteraction), "OnCacheInteraction", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public BaseCombatWeapon() {
		OnBaseCombatWeaponCreated(this);
	}

	public override void UpdateOnRemove() {
		OnBaseCombatWeaponDestroyed(this);
		base.UpdateOnRemove();
	}

	public class WeaponList(ReadOnlySpan<char> name) : AutoGameSystem(name)
	{
		public readonly List<BaseCombatWeapon> List = [];

		public override void LevelShutdownPostEntity() => List.Clear();

		public void AddWeapon(BaseCombatWeapon weapon) => List.Add(weapon);
		public void RemoveWeapon(BaseCombatWeapon weapon) => List.Remove(weapon);
	}

	public static readonly WeaponList g_WeaponList = new("CWeaponList");

	static void OnBaseCombatWeaponCreated(BaseCombatWeapon weapon) => g_WeaponList.AddWeapon(weapon);
	static void OnBaseCombatWeaponDestroyed(BaseCombatWeapon weapon) => g_WeaponList.RemoveWeapon(weapon);

	public static int GetAvailableWeaponsInBox(Span<BaseCombatWeapon?> list, in Vector3 mins, in Vector3 maxs) {
		int count = 0;
		foreach (BaseCombatWeapon weapon in g_WeaponList.List) {
			if (weapon.GetOwner() == null) {
				if (CollisionUtils.IsPointInBox(weapon.GetAbsOrigin(), mins, maxs)) {
					if (count < list.Length) {
						list[count] = weapon;
						count++;
					}
				}
			}
		}

		return count;
	}

	public bool IsConstrained() => false;

	public override bool IsWeapon() => true;
	public override GarrysMod.LuaClass Lua_GetLuaClass() => GarrysMod.LuaEntity.LC_Weapon;

	public static void W_Precache(){

	}

	public virtual Capability CapabilitiesGet() => 0;

	public virtual void Operator_FrameUpdate(BaseCombatCharacter op) {
		StudioFrameAdvance();

		if (IsSequenceFinished()) {
			if (SequenceLoops) {
				int sequence = SelectWeightedSequence(GetActivity());
				if (sequence != StudioHdr.ACTIVITY_NOT_AVAILABLE)
					ResetSequence(sequence);
			}
		}

		DispatchAnimEvents(op);

		BasePlayer? owner = ToBasePlayer(GetOwner());

		if (owner == null)
			return;

		BaseViewModel? vm = owner.GetViewModel(nViewModelIndex);

		if (vm != null) {
			vm.StudioFrameAdvance();
			vm.DispatchAnimEvents(this);
		}
	}

	public virtual void Operator_HandleAnimEvent(ref AnimEvent animEvent, BaseCombatCharacter op) {
		if ((animEvent.Type & AnimEventType.NewEventSystem) != 0 && (animEvent.Type & AnimEventType.Server) != 0) {
			if (animEvent.Event == (int)Animevent.AE_NPC_WEAPON_FIRE) {
				bool secondary = int.TryParse(animEvent.Options, out int value) && value != 0;
				Operator_ForceNPCFire(op, secondary);
				return;
			}
			else if (animEvent.Event == (int)Animevent.AE_WPN_PLAYWPNSOUND) {
				int snd = WeaponParse.GetWeaponSoundFromString(animEvent.Options);
				if (snd != -1)
					WeaponSound((WeaponSound)snd);
			}
		}

		DevWarning(2, $"Unhandled animation event {animEvent.Event} from {op.GetClassname()} --> {GetClassname()}\n");
	}

	public override void HandleAnimEvent(ref AnimEvent animEvent) {
		BasePlayer? owner = ToBasePlayer(GetOwner());

		if (owner != null)
			Operator_HandleAnimEvent(ref animEvent, owner);
	}

	public virtual void Operator_ForceNPCFire(BaseCombatCharacter op, bool secondary) { }

	public virtual SCOND_t WeaponRangeAttack1Condition(float dot, float dist) {
		if (UsesPrimaryAmmo() && !HasPrimaryAmmo())
			return SCOND_t.COND_NO_PRIMARY_AMMO;
		else if (dist < MinRange1)
			return SCOND_t.COND_TOO_CLOSE_TO_ATTACK;
		else if (dist > MaxRange1)
			return SCOND_t.COND_TOO_FAR_TO_ATTACK;
		else if (dot < 0.5)
			return SCOND_t.COND_NOT_FACING_ATTACK;

		return SCOND_t.COND_CAN_RANGE_ATTACK1;
	}

	public virtual SCOND_t WeaponRangeAttack2Condition(float dot, float dist) => SCOND_t.COND_NONE;
	public virtual SCOND_t WeaponMeleeAttack1Condition(float dot, float dist) => SCOND_t.COND_NONE;
	public virtual SCOND_t WeaponMeleeAttack2Condition(float dot, float dist) => SCOND_t.COND_NONE;

	public BaseEntity? Respawn() {
		BaseEntity? newWeapon = Create(GetClassname(), g_pGameRules.VecWeaponRespawnSpot(this), GetLocalAngles(), GetOwnerEntity());

		if (newWeapon != null) {
			newWeapon.AddEffects(EntityEffects.NoDraw);
			newWeapon.SetTouch(null);
			newWeapon.SetThink(((BaseCombatWeapon)newWeapon).AttemptToMaterialize);

			Util.DropToFloor(this, Mask.Solid);

			newWeapon.SetNextThink(gpGlobals.CurTime + g_pGameRules.FlWeaponRespawnTime(this));
		}
		else
			Warning($"Respawn failed to create {GetClassname()}!\n");

		return newWeapon;
	}

	public void FallInit() {
		SetModel(GetWorldModel());
		VPhysicsDestroyObject();

		if (VPhysicsInitNormal(SolidType.BBox, GetSolidFlags() | SolidFlags.Trigger, false) == null) {
			SetMoveType(Source.MoveType.FlyGravity);
			SetSolid(SolidType.BBox);
			AddSolidFlags(SolidFlags.Trigger);
		}

		SetPickupTouch();

		SetThink(FallThink);

		SetNextThink(gpGlobals.CurTime + 0.1f);
	}

	public void FallThink() {
		SetNextThink(gpGlobals.CurTime + 0.1f);

		bool shouldMaterialize = false;
		IPhysicsObject? physics = VPhysicsGetObject();
		if (physics != null)
			shouldMaterialize = physics.IsAsleep();
		else
			shouldMaterialize = (GetFlags() & EntityFlags.OnGround) != 0;

		if (shouldMaterialize) {
			if (GetOwnerEntity() != null)
				EmitSound("BaseCombatWeapon.WeaponDrop");

			Materialize();
		}
	}

	public void Materialize() {
		if (IsEffectActive(EntityEffects.NoDraw)) {
			EmitSound("AlyxEmp.Charge");

			RemoveEffects(EntityEffects.NoDraw);
			DoMuzzleFlash();
		}

		if (HasSpawnFlags(BasePlayer.SF_NORESPAWN) == false) {
			VPhysicsInitNormal(SolidType.BBox, GetSolidFlags() | SolidFlags.Trigger, false);
			SetMoveType(Source.MoveType.VPhysics);
		}

		SetPickupTouch();

		SetThink(null);
	}

	public void AttemptToMaterialize() {
		TimeUnit_t time = g_pGameRules.FlWeaponTryRespawn(this);

		if (time == 0) {
			Materialize();
			return;
		}

		SetNextThink(gpGlobals.CurTime + time);
	}

	public void CheckRespawn() {
		switch (g_pGameRules.WeaponShouldRespawn(this)) {
			case GameRulesRespawnReturnCode.WeaponRespawnYes:
				Respawn();
				break;
			case GameRulesRespawnReturnCode.WeaponRespawnNo:
				return;
		}
	}
}
