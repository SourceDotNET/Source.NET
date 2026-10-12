using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;
using Source.Common.Physics;

using System.Numerics;
using System.Runtime.CompilerServices;

using static Game.Server.SCOND_t;

namespace Game.Server;

using DEFINE = DEFINE<NPC_PlayerCompanion>;

public enum AIReadiness
{
	Panic = -2,
	Stealth = -1,
	Relaxed = 0,
	Stimulated,
	Agitated,
}

public class NPC_PlayerCompanion : AI_PlayerAlly
{
	public const float READINESS_MIN_VALUE = -2;
	public const float READINESS_MODE_PANIC = -2;
	public const float READINESS_MODE_STEALTH = -1;

	public const float READINESS_VALUE_RELAXED = 0.1f;
	public const float READINESS_VALUE_STIMULATED = 0.95f;
	public const float READINESS_VALUE_AGITATED = 1.0f;

	const float MORTAR_BLAST_RADIUS = 350;

	public static readonly ConVar ai_debug_readiness = new("ai_debug_readiness", "0");
	public static readonly ConVar ai_readiness_decay = new("ai_readiness_decay", "120");

	protected const int COND_PC_HURTBYFIRE = AI_BaseNPC.NEXT_CONDITION;
	protected const int COND_PC_SAFE_FROM_MORTAR = AI_BaseNPC.NEXT_CONDITION + 1;
	protected const int COND_PC_BECOMING_PASSENGER = AI_BaseNPC.NEXT_CONDITION + 2;
	public new const int NEXT_CONDITION = AI_BaseNPC.NEXT_CONDITION + 3;

	protected const int SCHED_PC_COWER = AI_BaseNPC.NEXT_SCHEDULE;
	protected const int SCHED_PC_MOVE_TOWARDS_COVER_FROM_BEST_SOUND = AI_BaseNPC.NEXT_SCHEDULE + 1;
	protected const int SCHED_PC_TAKE_COVER_FROM_BEST_SOUND = AI_BaseNPC.NEXT_SCHEDULE + 2;
	protected const int SCHED_PC_FLEE_FROM_BEST_SOUND = AI_BaseNPC.NEXT_SCHEDULE + 3;
	protected const int SCHED_PC_FAIL_TAKE_COVER_TURRET = AI_BaseNPC.NEXT_SCHEDULE + 4;
	protected const int SCHED_PC_FAKEOUT_MORTAR = AI_BaseNPC.NEXT_SCHEDULE + 5;
	protected const int SCHED_PC_GET_OFF_COMPANION = AI_BaseNPC.NEXT_SCHEDULE + 6;
	public new const int NEXT_SCHEDULE = AI_BaseNPC.NEXT_SCHEDULE + 7;

	protected const int TASK_PC_WAITOUT_MORTAR = AI_BaseNPC.NEXT_TASK;
	protected const int TASK_PC_GET_PATH_OFF_COMPANION = AI_BaseNPC.NEXT_TASK + 1;
	public new const int NEXT_TASK = AI_BaseNPC.NEXT_TASK + 2;

	static readonly AI_ClassScheduleIdSpace gm_ClassScheduleIdSpace = new();
	public static new AI_ClassScheduleIdSpace ClassScheduleIdSpace => gm_ClassScheduleIdSpace;

	public static readonly new DataMap DataDesc = new(typeof(NPC_PlayerCompanion), AI_BaseNPC.DataDesc, [
		DEFINE.KEYFIELD(nameof(DontPickupWeapons), FieldType.Boolean, "DontPickupWeapons"),

		DEFINE.INPUTFUNC(FieldType.Void, "SetReadinessPanic", nameof(InputSetReadinessPanic), (INPUTFUNCPTR)((self, data) => ((NPC_PlayerCompanion)self).InputSetReadinessPanic(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "SetReadinessStealth", nameof(InputSetReadinessStealth), (INPUTFUNCPTR)((self, data) => ((NPC_PlayerCompanion)self).InputSetReadinessStealth(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "SetReadinessLow", nameof(InputSetReadinessLow), (INPUTFUNCPTR)((self, data) => ((NPC_PlayerCompanion)self).InputSetReadinessLow(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "SetReadinessMedium", nameof(InputSetReadinessMedium), (INPUTFUNCPTR)((self, data) => ((NPC_PlayerCompanion)self).InputSetReadinessMedium(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "SetReadinessHigh", nameof(InputSetReadinessHigh), (INPUTFUNCPTR)((self, data) => ((NPC_PlayerCompanion)self).InputSetReadinessHigh(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "LockReadiness", nameof(InputLockReadiness), (INPUTFUNCPTR)((self, data) => ((NPC_PlayerCompanion)self).InputLockReadiness(data))),

		DEFINE.INPUTFUNC(FieldType.Void, "EnableWeaponPickup", nameof(InputEnableWeaponPickup), (INPUTFUNCPTR)((self, data) => ((NPC_PlayerCompanion)self).InputEnableWeaponPickup(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "DisableWeaponPickup", nameof(InputDisableWeaponPickup), (INPUTFUNCPTR)((self, data) => ((NPC_PlayerCompanion)self).InputDisableWeaponPickup(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	bool MovingAwayFromPlayer;

	protected float Readiness;
	protected float ReadinessSensitivity;
	protected bool ReadinessCapable;
	protected TimeUnit_t ReadinessLockedUntil;

	protected bool DontPickupWeapons;

	protected readonly AI_ActBusyBehavior ActBusyBehavior = new();

	public NPC_PlayerCompanion() => LoadSchedules();

	public override AI_ClassScheduleIdSpace GetClassScheduleIdSpace() => gm_ClassScheduleIdSpace;
	public override ReadOnlySpan<char> GetSchedulingErrorName() => nameof(NPC_PlayerCompanion);

	public static new bool LoadSchedules() => CustomSchedules.Load(AI_BaseNPC.LoadSchedules, nameof(NPC_PlayerCompanion));

	public override bool LoadedSchedules() => CustomSchedules.Valid;

	static readonly AI_CustomSchedules CustomSchedules = new() {
		IdSpace = gm_ClassScheduleIdSpace,
		ParentIdSpace = AI_BaseNPC.ClassScheduleIdSpace,
		Schedules = [
			(nameof(SCHED_PC_COWER), SCHED_PC_COWER),
			(nameof(SCHED_PC_MOVE_TOWARDS_COVER_FROM_BEST_SOUND), SCHED_PC_MOVE_TOWARDS_COVER_FROM_BEST_SOUND),
			(nameof(SCHED_PC_TAKE_COVER_FROM_BEST_SOUND), SCHED_PC_TAKE_COVER_FROM_BEST_SOUND),
			(nameof(SCHED_PC_FLEE_FROM_BEST_SOUND), SCHED_PC_FLEE_FROM_BEST_SOUND),
			(nameof(SCHED_PC_FAIL_TAKE_COVER_TURRET), SCHED_PC_FAIL_TAKE_COVER_TURRET),
			(nameof(SCHED_PC_FAKEOUT_MORTAR), SCHED_PC_FAKEOUT_MORTAR),
			(nameof(SCHED_PC_GET_OFF_COMPANION), SCHED_PC_GET_OFF_COMPANION),
		],
		Tasks = [
			(nameof(TASK_PC_WAITOUT_MORTAR), TASK_PC_WAITOUT_MORTAR),
			(nameof(TASK_PC_GET_PATH_OFF_COMPANION), TASK_PC_GET_PATH_OFF_COMPANION),
		],
		Conditions = [
			(nameof(COND_PC_HURTBYFIRE), COND_PC_HURTBYFIRE),
			(nameof(COND_PC_SAFE_FROM_MORTAR), COND_PC_SAFE_FROM_MORTAR),
			(nameof(COND_PC_BECOMING_PASSENGER), COND_PC_BECOMING_PASSENGER),
		],
		Definitions = [
			new(SCHED_PC_MOVE_TOWARDS_COVER_FROM_BEST_SOUND,
				[
					(TASK_SET_FAIL_SCHEDULE, SCHED_FLEE_FROM_BEST_SOUND),
					TASK_STOP_MOVING,
					(TASK_SET_TOLERANCE_DISTANCE, 24),
					TASK_STORE_BESTSOUND_REACTORIGIN_IN_SAVEPOSITION,
					TASK_FIND_COVER_FROM_BEST_SOUND,
					(TASK_RUN_PATH_TIMED, 1.0f),
					TASK_STOP_MOVING,
					TASK_FACE_SAVEPOSITION,
					(TASK_SET_ACTIVITY, Activity.ACT_IDLE),
				],
				[
					COND_PC_SAFE_FROM_MORTAR,
				]),

			new(SCHED_PC_TAKE_COVER_FROM_BEST_SOUND,
				[
					(TASK_SET_FAIL_SCHEDULE, SCHED_FLEE_FROM_BEST_SOUND),
					TASK_STOP_MOVING,
					(TASK_SET_TOLERANCE_DISTANCE, 24),
					TASK_STORE_BESTSOUND_REACTORIGIN_IN_SAVEPOSITION,
					TASK_FIND_COVER_FROM_BEST_SOUND,
					TASK_RUN_PATH,
					TASK_WAIT_FOR_MOVEMENT,
					TASK_STOP_MOVING,
					TASK_FACE_SAVEPOSITION,
					(TASK_SET_ACTIVITY, Activity.ACT_IDLE),
				],
				[
					COND_NEW_ENEMY,
					COND_PC_SAFE_FROM_MORTAR,
				]),

			new(SCHED_PC_COWER,
				[
					(TASK_WAIT_RANDOM, 0.1f),
					(TASK_SET_ACTIVITY, Activity.ACT_COWER),
					TASK_PC_WAITOUT_MORTAR,
					(TASK_WAIT, 0.1f),
					(TASK_WAIT_RANDOM, 0.5f),
				]),

			new(SCHED_PC_FLEE_FROM_BEST_SOUND,
				[
					(TASK_SET_FAIL_SCHEDULE, SCHED_COWER),
					(TASK_GET_PATH_AWAY_FROM_BEST_SOUND, 600),
					(TASK_RUN_PATH_TIMED, 1.5f),
					TASK_STOP_MOVING,
					(TASK_TURN_LEFT, 179),
				],
				[
					COND_NEW_ENEMY,
					COND_PC_SAFE_FROM_MORTAR,
				]),

			new(SCHED_PC_FAIL_TAKE_COVER_TURRET,
				[
					(TASK_SET_FAIL_SCHEDULE, SCHED_COWER),
					TASK_STOP_MOVING,
					(TASK_MOVE_AWAY_PATH, 600),
					(TASK_RUN_PATH_FLEE, 100),
					TASK_STOP_MOVING,
					(TASK_TURN_LEFT, 179),
				],
				[
					COND_NEW_ENEMY,
				]),

			new(SCHED_PC_FAKEOUT_MORTAR,
				[
					(TASK_MOVE_AWAY_PATH, 300),
					TASK_RUN_PATH,
					TASK_WAIT_FOR_MOVEMENT,
				],
				[
					COND_HEAR_DANGER,
				]),

			new(SCHED_PC_GET_OFF_COMPANION,
				[
					TASK_PC_GET_PATH_OFF_COMPANION,
					TASK_RUN_PATH,
					TASK_WAIT_FOR_MOVEMENT,
				]),
		],
	};

	public override bool CreateBehaviors() {
		AddBehavior(ActBusyBehavior);

		return base.CreateBehaviors();
	}

	public virtual void SelectModel() { }

	public override void Precache() {
		PrecacheModel(GetModelName());

		base.Precache();
	}

	public override void Spawn() {
		SelectModel();

		Precache();

		SetModel(GetModelName());

		SetHullType(AI_HullType.Human);
		SetHullSizeNormal();

		SetSolid(SolidType.BBox);
		AddSolidFlags(SolidFlags.NotStandable);
		SetBloodColor(Shared.BloodColor.Red);
		FieldOfView = 0.02f;
		NPCState = NPCState.None;

		CapabilitiesClear();
		CapabilitiesAdd(Capability.Squad);

		if (!HasSpawnFlags(SF_NPC_START_EFFICIENT)) {
			CapabilitiesAdd(Capability.AnimatedFace | Capability.TurnHead);
			CapabilitiesAdd(Capability.UseWeapons | Capability.AimGun | Capability.MoveShoot);
			CapabilitiesAdd(Capability.Duck | Capability.AutoDoors | Capability.OpenDoors);
			CapabilitiesAdd(Capability.UseShotRegulator);
		}
		CapabilitiesAdd(Capability.NoHitPlayer | Capability.NoHitSquadmates | Capability.FriendlyDmgImmune);
		CapabilitiesAdd(Capability.MoveGround);
		SetMoveType(Source.MoveType.Step);

		ReadinessCapable = IsReadinessCapable();
		SetReadinessValue(0.0f);
		SetReadinessSensitivity(random.RandomFloat(0.7f, 1.3f));
		ReadinessLockedUntil = 0.0f;

		if (HasSpawnFlags(SF_NPC_ALTCOLLISION)) {
			Warning($"NPC {GetEntityName()} using alternate collision! -- DISABLED\n");
			RemoveSpawnFlags(SF_NPC_ALTCOLLISION);
		}

		base.Spawn();
	}

	public override void GatherConditions() {
		base.GatherConditions();

		if (IsReadinessCapable())
			UpdateReadiness();

		PredictPlayerPush();

		for (AI_EnemyInfo? memory = GetEnemies().GetFirst(out int iter); memory != null; memory = GetEnemies().GetNext(ref iter)) {
			if (IsMortar(memory.Enemy.Get()) || IsSniper(memory.Enemy.Get())) {
				memory.Unforgettable = IRelationType(memory.Enemy.Get()) < Disposition.LI;
				memory.EludedMe = false;
			}
		}

		if (GetMotor()!.IsDeceleratingToGoal() && IsCurTaskContinuousMove() &&
			 HasCondition((int)COND_PLAYER_PUSHING) && IsCurSchedule(SCHED_MOVE_AWAY))
			ClearSchedule("Being pushed by player");

		ClearCondition(COND_PC_SAFE_FROM_MORTAR);
		if (IsCurSchedule(SCHED_TAKE_COVER_FROM_BEST_SOUND)) {
			ref WorldSoundInstance sound = ref GetBestSound(SoundInstanceType.Danger);

			if (!Unsafe.IsNullRef(ref sound) && (sound.Type & SoundInstanceType.ContextMortar) != 0) {
				float distSq = (sound.GetSoundOrigin() - GetAbsOrigin()).LengthSquared();
				float safeDist = MORTAR_BLAST_RADIUS + GetHullWidth() * 2;
				if (distSq > safeDist * safeDist)
					SetCondition(COND_PC_SAFE_FROM_MORTAR);
			}
		}

		if (hl2_episodic.GetBool() && GetEnemy() == null && HasCondition((int)COND_HEAR_PLAYER)) {
			BasePlayer? player = AI_GetClosestPlayer();

			Vector3 los = player!.EyePosition() - EyePosition();
			los.Z = 0;
			MathLib.VectorNormalize(ref los);

			if (Vector3.Dot(los, EyeDirection2D()) > BasePlayer.DOT_45DEGREE)
				ClearCondition((int)COND_HEAR_PLAYER);
		}
	}

	public virtual void PredictPlayerPush() {
		BasePlayer? player = AI_GetClosestPlayer();
		if (player != null && player.GetSmoothedVelocity().LengthSquared() >= 140 * 140) {
			Vector3 predictedPosition = player.WorldSpaceCenter() + player.GetSmoothedVelocity() * .4f;
			Vector3 delta = WorldSpaceCenter() - predictedPosition;
			float pushWidth = GetHullWidth() * 1.414f;
			if (delta.Z < GetHullHeight() * .5f && delta.Length2DSqr() < pushWidth * pushWidth)
				TestPlayerPushing(player);
		}
	}

	public override void BuildScheduleTestBits() {
		base.BuildScheduleTestBits();

		SetCustomInterruptCondition(COND_PC_BECOMING_PASSENGER);

		if (IsCurSchedule(SCHED_RANGE_ATTACK1))
			SetCustomInterruptCondition((int)COND_PLAYER_PUSHING);

		if (ConditionInterruptsCurSchedule((int)COND_GIVE_WAY) ||
			 IsCurSchedule(SCHED_HIDE_AND_RELOAD) ||
			 IsCurSchedule(SCHED_RELOAD) ||
			 IsCurSchedule(SCHED_STANDOFF) ||
			 IsCurSchedule(SCHED_TAKE_COVER_FROM_ENEMY) ||
			 IsCurSchedule(SCHED_COMBAT_FACE) ||
			 IsCurSchedule(SCHED_ALERT_FACE) ||
			 IsCurSchedule(SCHED_COMBAT_STAND) ||
			 IsCurSchedule(SCHED_ALERT_FACE_BESTSOUND) ||
			 IsCurSchedule(SCHED_ALERT_STAND)) {
			SetCustomInterruptCondition((int)COND_HEAR_MOVE_AWAY);
			SetCustomInterruptCondition((int)COND_PLAYER_PUSHING);
			SetCustomInterruptCondition(COND_PC_HURTBYFIRE);
		}
	}

	public override ref WorldSoundInstance GetBestSound(SoundInstanceType validTypes = SoundInstanceType.AllSounds) {
		ref WorldSoundInstance currentSound = ref GetSenses()!.GetFirstHeardSound(out int iter);
		while (!Unsafe.IsNullRef(ref currentSound)) {
			if (currentSound.FIsSound()) {
				if ((currentSound.SoundContext() & SoundInstanceType.ContextMortar) != 0)
					return ref currentSound;
			}

			currentSound = ref GetSenses()!.GetNextHeardSound(ref iter);
		}

		return ref base.GetBestSound(validTypes);
	}

	public override bool QueryHearSound(ref WorldSoundInstance sound) {
		if (!base.QueryHearSound(ref sound))
			return false;

		switch (sound.SoundTypeNoContext()) {
			case SoundInstanceType.ReadinessLow:
				SetReadinessLevel(AIReadiness.Relaxed, false, true);
				return false;

			case SoundInstanceType.ReadinessMedium:
				SetReadinessLevel(AIReadiness.Stimulated, false, true);
				return false;

			case SoundInstanceType.ReadinessHigh:
				SetReadinessLevel(AIReadiness.Agitated, false, true);
				return false;

			default:
				return true;
		}
	}

	public override bool QuerySeeEntity(BaseEntity entity, bool onlyHateOrFearIfNPC = false) {
		AI_BaseNPC? other = entity.MyNPCPointer();
		if (other != null &&
			 (other.GetState() == NPCState.Alert || GetState() == NPCState.Alert || other.GetState() == NPCState.Combat || GetState() == NPCState.Combat) &&
			 other.IsPlayerAlly())
			return true;

		return base.QuerySeeEntity(entity, onlyHateOrFearIfNPC);
	}

	public override bool ShouldIgnoreSound(ref WorldSoundInstance sound) {
		if (!base.ShouldIgnoreSound(ref sound)) {
			if (sound.IsSoundType(SoundInstanceType.Danger) && !SoundIsVisible(ref sound))
				return true;
		}

		return false;
	}

	public override int SelectSchedule() {
		MovingAwayFromPlayer = false;

		if (ActBusyBehavior.IsRunning() && ActBusyBehavior.NeedsToPlayExitAnim()) {
			Vector3 up = GetAbsOrigin();
			up.Z += .25f;

			Util.TraceHull(GetAbsOrigin(), up, GetHullMins(), GetHullMaxs(), Mask.Solid, this, Source.CollisionGroup.None, out Trace tr);

			if (tr.StartSolid) {
				if (HasCondition((int)COND_HEAR_DANGER))
					ActBusyBehavior.StopBusying();

				DeferSchedulingToBehavior(ActBusyBehavior);
				return base.SelectSchedule();
			}
		}

		int sched = SelectFlinchSchedule();
		if (sched != SCHED_NONE)
			return sched;

		int schedule = SelectScheduleDanger();
		if (schedule != SCHED_NONE)
			return schedule;

		schedule = SelectSchedulePriorityAction();
		if (schedule != SCHED_NONE)
			return schedule;

		if (!BehaviorSelectSchedule()) {
			if (NPCState == NPCState.Idle || NPCState == NPCState.Alert) {
				schedule = SelectScheduleNonCombat();
				if (schedule != SCHED_NONE)
					return schedule;
			}
			else if (NPCState == NPCState.Combat) {
				schedule = SelectScheduleCombat();
				if (schedule != SCHED_NONE)
					return schedule;
			}
		}

		return base.SelectSchedule();
	}

	public virtual int SelectScheduleDanger() {
		if (HasCondition((int)COND_HEAR_DANGER)) {
			ref WorldSoundInstance sound = ref GetBestSound(SoundInstanceType.Danger);

			Assert(!Unsafe.IsNullRef(ref sound));

			if (!Unsafe.IsNullRef(ref sound) && (sound.Type & SoundInstanceType.Danger) != 0) {
				if (HasCondition(COND_PC_SAFE_FROM_MORTAR))
					return SCHED_COWER;
				else if ((sound.Type & SoundInstanceType.ContextFromSniper) != 0)
					return SCHED_COWER;

				return SCHED_TAKE_COVER_FROM_BEST_SOUND;
			}
		}

		if (HasCondition((int)COND_HEAR_MOVE_AWAY))
			return SCHED_MOVE_AWAY;

		if (HasCondition(COND_PC_HURTBYFIRE)) {
			ClearCondition(COND_PC_HURTBYFIRE);
			return SCHED_MOVE_AWAY;
		}

		return SCHED_NONE;
	}

	public virtual int SelectSchedulePriorityAction() {
		BaseEntity? groundEntity = GetGroundEntity();
		if (groundEntity != null && !IsInAScript()) {
			if (groundEntity.IsPlayer())
				return SCHED_PC_GET_OFF_COMPANION;

			if (groundEntity.IsNPC() &&
				 IRelationType(groundEntity) == Disposition.LI &&
				 WorldSpaceCenter().Z - groundEntity.WorldSpaceCenter().Z > GetHullHeight() * .5f)
				return SCHED_PC_GET_OFF_COMPANION;
		}

		int schedule = SelectSchedulePlayerPush();
		if (schedule != SCHED_NONE)
			return schedule;

		return SCHED_NONE;
	}

	public virtual int SelectScheduleNonCombat() => SCHED_NONE;

	public virtual int SelectScheduleCombat() {
		if (CanReload() && (HasCondition((int)COND_NO_PRIMARY_AMMO) || HasCondition((int)COND_LOW_PRIMARY_AMMO)))
			return SCHED_HIDE_AND_RELOAD;

		return SCHED_NONE;
	}

	public int SelectSchedulePlayerPush() {
		if (HasCondition((int)COND_PLAYER_PUSHING) && !IsInAScript() && !IgnorePlayerPushing()) {
			if (GlobalEntity.GetState("gordon_precriminal") != GlobalEState.On) {
				MovingAwayFromPlayer = true;
				return SCHED_MOVE_AWAY;
			}
		}

		return SCHED_NONE;
	}

	public virtual bool IgnorePlayerPushing() => false;

	public virtual bool CanReload() {
		if (IsRunningDynamicInteraction())
			return false;

		return true;
	}

	public override int TranslateSchedule(int scheduleType) {
		switch (scheduleType) {
			case SCHED_IDLE_STAND:
			case SCHED_ALERT_STAND:
				if (GetActiveWeapon() != null) {
					BaseCombatWeapon weapon = GetActiveWeapon()!;

					if (CanReload() && weapon.UsesClipsForAmmo1() && weapon.Clip1() < (weapon.GetMaxClip1() * .5) && OccupyStrategySlot(SquadSlot.ExclusiveReload))
						return SCHED_RELOAD;
				}
				break;

			case SCHED_COWER:
				return SCHED_PC_COWER;

			case SCHED_TAKE_COVER_FROM_BEST_SOUND: {
					ref WorldSoundInstance sound = ref GetBestSound(SoundInstanceType.Danger);

					if (!Unsafe.IsNullRef(ref sound) && sound.Owner.Get() != null) {
						if (sound.Owner.Get()!.IsNPC() && FClassnameIs(sound.Owner.Get(), "npc_zombine"))
							return SCHED_PC_TAKE_COVER_FROM_BEST_SOUND;
					}

					return SCHED_PC_MOVE_TOWARDS_COVER_FROM_BEST_SOUND;
				}

			case SCHED_FLEE_FROM_BEST_SOUND:
				return SCHED_PC_FLEE_FROM_BEST_SOUND;

			case SCHED_ESTABLISH_LINE_OF_FIRE:
			case SCHED_MOVE_TO_WEAPON_RANGE:
				if (IsMortar(GetEnemy()))
					return SCHED_TAKE_COVER_FROM_ENEMY;
				break;

			case SCHED_CHASE_ENEMY:
				if (IsMortar(GetEnemy()))
					return SCHED_TAKE_COVER_FROM_ENEMY;
				if (GetEnemy() != null && FClassnameIs(GetEnemy(), "npc_combinegunship"))
					return SCHED_ESTABLISH_LINE_OF_FIRE;
				break;

			case SCHED_ESTABLISH_LINE_OF_FIRE_FALLBACK:
				if (GetEnemy() != null && FClassnameIs(GetEnemy(), "npc_combinegunship"))
					return SCHED_ESTABLISH_LINE_OF_FIRE;
				break;

			case SCHED_RANGE_ATTACK1:
				if (IsMortar(GetEnemy()))
					return SCHED_TAKE_COVER_FROM_ENEMY;

				if (GetShotRegulator().IsInRestInterval())
					return SCHED_STANDOFF;

				if (!OccupyStrategySlotRange(SquadSlot.Attack1, SquadSlot.Attack2))
					return SCHED_STANDOFF;
				break;

			case SCHED_FAIL_TAKE_COVER:
				if (IsEnemyTurret())
					return SCHED_PC_FAIL_TAKE_COVER_TURRET;
				break;

			case SCHED_RUN_FROM_ENEMY_FALLBACK:
				if (HasCondition((int)COND_CAN_RANGE_ATTACK1))
					return SCHED_RANGE_ATTACK1;
				break;
		}

		return base.TranslateSchedule(scheduleType);
	}

	public override void StartTask(in Task_t task) {
		switch (task.Task) {
			case TASK_SOUND_WAKE:
				LocateEnemySound();
				SetWait(0.5f);
				break;

			case TASK_PC_WAITOUT_MORTAR:
				if (HasCondition((int)COND_NO_HEAR_DANGER))
					TaskComplete();
				break;

			case TASK_PC_GET_PATH_OFF_COMPANION:
				Assert(GetGroundEntity() != null && (GetGroundEntity()!.IsPlayer() || (GetGroundEntity()!.IsNPC() && IRelationType(GetGroundEntity()) == Disposition.LI)));
				GetNavigator()!.SetAllowBigStep(GetGroundEntity());
				ChainStartTask(TASK_MOVE_AWAY_PATH, 48);
				break;

			default:
				base.StartTask(task);
				break;
		}
	}

	public override void RunTask(in Task_t task) {
		switch (task.Task) {
			case TASK_SOUND_WAKE:
				if (IsWaitFinished())
					TaskComplete();
				break;

			case TASK_PC_WAITOUT_MORTAR:
				if (HasCondition((int)COND_NO_HEAR_DANGER))
					TaskComplete();
				break;

			case TASK_MOVE_AWAY_PATH:
				base.RunTask(task);

				if (GetNavigator()!.IsGoalActive() && GetEnemy() == null)
					AddFacingTarget(EyePosition() + BodyDirection2D() * 240, 1.0f, 2.0f);
				break;

			case TASK_PC_GET_PATH_OFF_COMPANION:
				GetNavigator()!.SetAllowBigStep(AI_GetClosestPlayer());
				ChainRunTask(TASK_MOVE_AWAY_PATH, 48);
				break;

			default:
				base.RunTask(task);
				break;
		}
	}

	public virtual void LocateEnemySound() { }

	public override Activity NPC_TranslateActivity(Activity activity) {
		if (activity == Activity.ACT_COWER)
			return Activity.ACT_COVER_LOW;

		if (activity == Activity.ACT_RUN && (IsCurSchedule(SCHED_TAKE_COVER_FROM_BEST_SOUND) || IsCurSchedule(SCHED_FLEE_FROM_BEST_SOUND))) {
			if (random.RandomInt(0, 1) != 0 && HaveSequenceForActivity(Activity.ACT_RUN_PROTECTED))
				activity = Activity.ACT_RUN_PROTECTED;
		}

		activity = base.NPC_TranslateActivity(activity);

		if (activity == Activity.ACT_IDLE) {
			if ((NPCState == NPCState.Combat || NPCState == NPCState.Alert) && gpGlobals.CurTime - LastAttackTime < 3)
				activity = Activity.ACT_IDLE_ANGRY;
		}

		return activity;
	}

	public override void HandleAnimEvent(ref AnimEvent animEvent) {
		switch (animEvent.Event) {
			case EVENT_WEAPON_RELOAD:
				if (GetActiveWeapon() != null) {
					GetActiveWeapon()!.WeaponSound(WeaponSound.ReloadNPC);
					GetActiveWeapon()!.iClip1 = GetActiveWeapon()!.GetMaxClip1();
					ClearCondition((int)COND_LOW_PRIMARY_AMMO);
					ClearCondition((int)COND_NO_PRIMARY_AMMO);
					ClearCondition((int)COND_NO_SECONDARY_AMMO);
				}
				break;

			default:
				base.HandleAnimEvent(ref animEvent);
				break;
		}
	}

	public override bool HandleInteraction(AI_Interaction interactionType, object? data, BaseCombatCharacter? sourceEnt) {
		if (interactionType == AI_Interaction.HitByPlayerThrownPhysObj)
			return true;

		return base.HandleInteraction(interactionType, data, sourceEnt);
	}

	public override int GetSoundInterests() {
		return (int)(SoundInstanceType.World |
			SoundInstanceType.Combat |
			SoundInstanceType.Player |
			SoundInstanceType.Danger |
			SoundInstanceType.BulletImpact |
			SoundInstanceType.MoveAway |
			SoundInstanceType.ReadinessLow |
			SoundInstanceType.ReadinessMedium |
			SoundInstanceType.ReadinessHigh);
	}

	public override void Touch(BaseEntity? other) {
		base.Touch(other);

		if (other!.IsPlayer() || (other.VPhysicsGetObject() != null && (other.VPhysicsGetObject()!.GetGameFlags() & PhysicsFlags.PlayerHeld) != 0)) {
			if (HasMemory(AI_MemoryFlags.Provoked))
				return;

			TestPlayerPushing(other.IsPlayer() ? other : AI_GetClosestPlayer());
		}
	}

	protected virtual bool IsReadinessCapable() {
		if (GlobalEntity.GetState("gordon_precriminal") == GlobalEState.On)
			return false;

		if (GetActiveWeapon() != null && LookupActivity("ACT_IDLE_AIM_RIFLE_STIMULATED") == Activity.ACT_INVALID)
			return false;

		if (GetActiveWeapon() != null && FClassnameIs(GetActiveWeapon(), "weapon_rpg"))
			return false;

		return true;
	}

	protected bool IsReadinessLocked() => gpGlobals.CurTime < ReadinessLockedUntil;

	protected void AddReadiness(float add, bool overrideLock = false) {
		if (IsReadinessLocked() && !overrideLock)
			return;

		SetReadinessValue(GetReadinessValue() + add);
	}

	protected void SubtractReadiness(float sub, bool overrideLock = false) {
		if (IsReadinessLocked() && !overrideLock)
			return;

		SetReadinessValue(Math.Max(GetReadinessValue() - sub, 0));
	}

	public bool AllowReadinessValueChange() {
		if (GetIdealActivity() == Activity.ACT_IDLE || GetIdealActivity() == Activity.ACT_WALK || GetIdealActivity() == Activity.ACT_RUN)
			return true;

		if (HasActiveLayer())
			return false;

		return false;
	}

	protected void SetReadinessValue(float set) {
		if (!AllowReadinessValueChange())
			return;

		AIReadiness priorReadiness = GetReadinessLevel();

		set = Math.Min(1.0f, set);
		set = Math.Max(READINESS_MIN_VALUE, set);

		Readiness = set;

		if (GetReadinessLevel() != priorReadiness) {
			SetCondition((int)COND_IDLE_INTERRUPT);

			ResetActivity();

			GetNavigator()!.SetArrivalSequence((int)Activity.ACT_INVALID);

			ReadinessLevelChanged(priorReadiness);
		}
	}

	protected void SetReadinessSensitivity(float sensitivity) => ReadinessSensitivity = sensitivity;

	protected virtual void UpdateReadiness() {
		if (!IsInScriptedReadinessState()) {
			if (HasCondition((int)COND_HEAR_COMBAT) || HasCondition((int)COND_HEAR_BULLET_IMPACT))
				SetReadinessLevel(AIReadiness.Stimulated, false, false);

			if (HasCondition((int)COND_HEAR_DANGER) || HasCondition((int)COND_SEE_ENEMY))
				SetReadinessLevel(AIReadiness.Agitated, false, false);

			if (Readiness > 0.0f && GetReadinessDecay() > 0)
				SubtractReadiness((0.1f * (1.0f / GetReadinessDecay())) * ReadinessSensitivity);
		}

		if (ai_debug_readiness.GetBool() && AI_IsSinglePlayer()) {
			Vector3 spot;
			Vector3 offset = new(0, 0, 12);
			const float BARLENGTH = 12.0f;
			const float GRADLENGTH = 4.0f;

			AI_GetClosestPlayer()!.GetVectors(out _, out Vector3 right, out _);

			if (IsInScriptedReadinessState()) {
				spot = EyePosition() + offset;

				if (GetReadinessLevel() == AIReadiness.Stealth)
					DebugOverlay.Text(spot, "Stealth", true, 0.1f);
				else if (GetReadinessLevel() == AIReadiness.Panic)
					DebugOverlay.Text(spot, "Panic", true, 0.1f);
				else
					DebugOverlay.Text(spot, "Unspecified", true, 0.1f);
			}
			else {
				spot = EyePosition() + offset;
				DebugOverlay.Line(spot, spot + right * GRADLENGTH, 255, 255, 255, false, 0.1f);

				spot = EyePosition() + offset + new Vector3(0, 0, BARLENGTH * READINESS_VALUE_RELAXED);
				DebugOverlay.Line(spot, spot + right * GRADLENGTH, 0, 255, 0, false, 0.1f);

				spot = EyePosition() + offset + new Vector3(0, 0, BARLENGTH * READINESS_VALUE_STIMULATED);
				DebugOverlay.Line(spot, spot + right * GRADLENGTH, 255, 255, 0, false, 0.1f);

				spot = EyePosition() + offset + new Vector3(0, 0, BARLENGTH * READINESS_VALUE_AGITATED);
				DebugOverlay.Line(spot, spot + right * GRADLENGTH, 255, 0, 0, false, 0.1f);

				spot = EyePosition() + offset;
				DebugOverlay.Line(spot, spot + new Vector3(0, 0, BARLENGTH * GetReadinessValue()), 255, 255, 0, false, 0.1f);
			}
		}
	}

	protected virtual float GetReadinessDecay() => ai_readiness_decay.GetFloat();

	protected bool IsInScriptedReadinessState() => Readiness < 0;

	public float GetReadinessValue() => Readiness;

	public AIReadiness GetReadinessLevel() {
		if (!ReadinessCapable)
			return AIReadiness.Relaxed;

		if (Readiness == READINESS_MODE_PANIC)
			return AIReadiness.Panic;

		if (Readiness == READINESS_MODE_STEALTH)
			return AIReadiness.Stealth;

		if (Readiness <= READINESS_VALUE_RELAXED)
			return AIReadiness.Relaxed;

		if (Readiness <= READINESS_VALUE_STIMULATED)
			return AIReadiness.Stimulated;

		return AIReadiness.Agitated;
	}

	public void SetReadinessLevel(AIReadiness level, bool overrideLock, bool slam) {
		if (IsReadinessLocked() && !overrideLock)
			return;

		switch (level) {
			case AIReadiness.Panic:
				if (slam)
					SetReadinessValue(READINESS_MODE_PANIC);
				break;
			case AIReadiness.Stealth:
				if (slam)
					SetReadinessValue(READINESS_MODE_STEALTH);
				break;
			case AIReadiness.Relaxed:
				if (slam || GetReadinessValue() < READINESS_VALUE_RELAXED)
					SetReadinessValue(READINESS_VALUE_RELAXED);
				break;
			case AIReadiness.Stimulated:
				if (slam || GetReadinessValue() < READINESS_VALUE_STIMULATED)
					SetReadinessValue(READINESS_VALUE_STIMULATED);
				break;
			case AIReadiness.Agitated:
				if (slam || GetReadinessValue() < READINESS_VALUE_AGITATED)
					SetReadinessValue(READINESS_VALUE_AGITATED);
				break;
			default:
				DevMsg("ERROR: Bad readiness level\n");
				break;
		}
	}

	public void LockReadiness(float duration = -1.0f) {
		if (duration == -1.0f)
			ReadinessLockedUntil = float.MaxValue;
		else
			ReadinessLockedUntil = gpGlobals.CurTime + duration;
	}

	public void UnlockReadiness() => ReadinessLockedUntil = gpGlobals.CurTime - 0.1f;

	public virtual void ReadinessLevelChanged(AIReadiness priorLevel) { }

	public bool IsEnemyTurret() => GetEnemy() != null && IsTurret(GetEnemy());

	public static bool IsMortar(BaseEntity? entity) {
		if (entity == null)
			return false;
		BaseEntity? entityParent = entity.GetParent();
		return entityParent != null && entityParent.Classname == "func_tankmortar";
	}

	public static bool IsSniper(BaseEntity? entity) {
		if (entity == null)
			return false;
		return entity.Classify() == Class_T.ProtoSniper;
	}

	public static bool IsTurret(BaseEntity? entity) {
		if (entity == null)
			return false;
		string? classname = entity.Classname;
		return classname == "npc_turret_floor" || classname == "npc_turret_ground";
	}

	public override bool ShouldLookForBetterWeapon() {
		if (DontPickupWeapons)
			return false;

		return base.ShouldLookForBetterWeapon();
	}

	public override void Weapon_Equip(BaseCombatWeapon weapon) {
		base.Weapon_Equip(weapon);
		ReadinessCapable = IsReadinessCapable();
	}

	public override bool FValidateHintType(AI_Hint hint) {
		switch (hint.HintType()) {
			case Hint.PlayerSquadTransitonPoint:
			case Hint.WorldVisuallyInterestingDontAim:
			case Hint.PlayerAllyMoveAwayDest:
			case Hint.PlayerAllyFearDest:
				return true;

			default:
				break;
		}

		return base.FValidateHintType(hint);
	}

	public override float GetIdealAccel() {
		float multiplier = 1.0f;
		if (AI_IsSinglePlayer()) {
			if (MovingAwayFromPlayer && (AI_GetClosestPlayer()!.GetAbsOrigin() - GetAbsOrigin()).Length2DSqr() < (3.0f * 12.0f) * (3.0f * 12.0f))
				multiplier = 2.0f;
		}
		return base.GetIdealAccel() * multiplier;
	}

	public override bool OnObstructionPreSteer(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		if (moveGoal.DirectTrace.TotalDist - moveGoal.DirectTrace.DistObstructed < GetHullWidth() * 1.5f) {
			AI_BaseNPC? blocker = moveGoal.DirectTrace.Obstruction?.MyNPCPointer();
			if (blocker != null && blocker.IsPlayerAlly() && !blocker.IsMoving() && !blocker.IsInAScript() &&
				 (IsCurSchedule(SCHED_NEW_WEAPON) ||
				   IsCurSchedule(SCHED_GET_HEALTHKIT) ||
				   blocker.IsCurSchedule(SCHED_FAIL) ||
				   (IsInPlayerSquad() && !blocker.IsInPlayerSquad()) ||
				   Classify() == Class_T.PlayerAllyVital ||
				   IsInAScript())) {
				if (blocker.ConditionInterruptsCurSchedule((int)COND_GIVE_WAY) ||
					 blocker.ConditionInterruptsCurSchedule((int)COND_PLAYER_PUSHING)) {
					blocker.GetMotor()!.SetIdealYawToTarget(WorldSpaceCenter());
					blocker.SetSchedule(SCHED_MOVE_AWAY);
				}
			}
		}

		return base.OnObstructionPreSteer(ref moveGoal, distClear, ref result);
	}

	public void InputSetReadinessPanic(InputData inputdata) => SetReadinessLevel(AIReadiness.Panic, true, true);
	public void InputSetReadinessStealth(InputData inputdata) => SetReadinessLevel(AIReadiness.Stealth, true, true);
	public void InputSetReadinessLow(InputData inputdata) => SetReadinessLevel(AIReadiness.Relaxed, true, true);
	public void InputSetReadinessMedium(InputData inputdata) => SetReadinessLevel(AIReadiness.Stimulated, true, true);
	public void InputSetReadinessHigh(InputData inputdata) => SetReadinessLevel(AIReadiness.Agitated, true, true);

	public void InputLockReadiness(InputData inputdata) {
		float value = inputdata.Value.Float();
		LockReadiness(value);
	}

	public void InputEnableWeaponPickup(InputData inputdata) => DontPickupWeapons = false;
	public void InputDisableWeaponPickup(InputData inputdata) => DontPickupWeapons = true;
}
