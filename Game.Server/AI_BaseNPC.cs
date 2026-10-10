global using static Game.Server.AI_BaseNPCGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Mathematics;
using Source.Common.Formats.BSP;

using Source.Common.Physics;

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

using FIELD = FIELD<AI_BaseNPC>;
using DEFINE = DEFINE<AI_BaseNPC>;

/// <summary>
/// bits_MEMORY_* analogs
/// </summary>
[Flags]
public enum AI_MemoryFlags {
	Clear = 0,
	Provoked = 1 << 0,
	Incover = 1 << 1,
	Suspicious = 1 << 2,
	TaskExpensive = 1 << 3,
	PathFailed = 1 << 5,
	Flinched = 1 << 6,
	TourGuide = 1 << 8,
	LockedHint = 1 << 10,
	Turning = 1 << 13,
	TurnHack = 1 << 14,
	HadEnemy = 1 << 15,
	HadPlayer = 1 << 16,
	HadLOS = 1 << 17,
	MovedFromSpawn = 1 << 18,
	Custom4 = 1 << 28,
	Custom3 = 1 << 29,
	Custom2 = 1 << 30,
	Custom1 = 1 << 31
}

[Flags]
public enum AI_SleepFlags {
	None= 0x00000000,
	AutoPVS = 0x00000001,
	AutoPVSAfterPVS = 0x00000002
}

[Flags]
public enum AI_DebugFlags
{
	DisableAI = 0x00000001,
	StepAI = 0x00000002
}

public static class AI_BaseNPCGlobals
{
	// TODO: Enum these
	// need a good name for its enum type
	public const int SF_NPC_WAIT_TILL_SEEN = 1 << 0;
	public const int SF_NPC_GAG = 1 << 1;
	public const int SF_NPC_FALL_TO_GROUND = 1 << 2;
	public const int SF_NPC_DROP_HEALTHKIT = 1 << 3;
	public const int SF_NPC_START_EFFICIENT = 1 << 4;
	public const int SF_NPC_WAIT_FOR_SCRIPT = 1 << 7;
	public const int SF_NPC_LONG_RANGE = 1 << 8;
	public const int SF_NPC_FADE_CORPSE = 1 << 9;
	public const int SF_NPC_ALWAYSTHINK = 1 << 10;
	public const int SF_NPC_TEMPLATE = 1 << 11;
	public const int SF_NPC_ALTCOLLISION = 1 << 12;
	public const int SF_NPC_NO_WEAPON_DROP = 1 << 13;
	public const int SF_NPC_NO_PLAYER_PUSHAWAY = 1 << 14;

	public const string PLAYER_SQUADNAME = "player_squad";

	public static readonly ConVar ai_show_think_tolerance = new("ai_show_think_tolerance", "0");
	public static readonly ConVar ai_debug_think_ticks = new("ai_debug_think_ticks", "0");
	public static readonly ConVar ai_debug_doors = new("ai_debug_doors", "0");

	public static readonly ConVar ai_rebalance_thinks = new("ai_rebalance_thinks", "1");
	public static readonly ConVar ai_use_efficiency = new("ai_use_efficiency", "1");
	public static readonly ConVar ai_use_frame_think_limits = new("ai_use_frame_think_limits", "1");
	public static readonly ConVar ai_default_efficient = new("ai_default_efficient", "0");
	public static readonly ConVar ai_efficiency_override = new("ai_efficiency_override", "0");
	public static readonly ConVar ai_debug_efficiency = new("ai_debug_efficiency", "0");
	public static readonly ConVar ai_frametime_limit = new("ai_frametime_limit", "50", FCvar.None, "frametime limit for min efficiency AIE_NORMAL (in sec's).");

	public static readonly ConVar ai_use_think_optimizations = new("ai_use_think_optimizations", "1");

	public static readonly ConVar ai_test_moveprobe_ignoresmall = new("ai_test_moveprobe_ignoresmall", "0");

	public static readonly ConVar ai_strong_optimizations = new("ai_strong_optimizations", "0");
	public static bool AIStrongOpt() => ai_strong_optimizations.GetBool();

	public static readonly ConVar ai_debug_avoidancebounds = new("ai_debug_avoidancebounds", "0");

	public static readonly ConVar g_DisableAI = new("ai_disabled", "0", FCvar.Notify);
	public static readonly ConVar g_IgnorePlayers = new("ai_ignoreplayers", "0", FCvar.Notify);
	public static readonly ConVar ai_LOS_mode = new("ai_LOS_mode", "0", FCvar.Replicated);
	public static readonly ConVar ai_auto_contact_solver = new("ai_auto_contact_solver", "1");

	public static bool ShouldUseEfficiency() => ai_use_think_optimizations.GetBool() && ai_use_efficiency.GetBool();
	public static bool ShouldUseFrameThinkLimits() => ai_use_think_optimizations.GetBool() && ai_use_frame_think_limits.GetBool();
	public static bool ShouldRebalanceThinks() => ai_use_think_optimizations.GetBool() && ai_rebalance_thinks.GetBool();
	public static bool ShouldDefaultEfficient() => ai_use_think_optimizations.GetBool() && ai_default_efficient.GetBool();

	public static readonly AI_Manager g_AI_Manager = new();

	public static readonly Stopwatch g_AIRunTimer = new();

	public static float g_NpcTimeThisFrame;
	public static TimeUnit_t g_StartTimeCurThink;

#if DEBUG
	public static bool AIIsDebuggingDoors(AI_BaseNPC npc) => ai_debug_doors.GetBool() && npc.Selected;
#else
	public static bool AIIsDebuggingDoors(AI_BaseNPC npc) => false;
#endif

}

public class AI_Manager
{
	public const int MAX_AIS = 256;

	public AI_Manager() {
		AIs.EnsureCapacity(MAX_AIS);
	}

	public List<AI_BaseNPC> AccessAIs() => AIs;

	public int NumAIs() => AIs.Count;

	public void AddAI(AI_BaseNPC ai) => AIs.Add(ai);

	public void RemoveAI(AI_BaseNPC ai) {
		int i = AIs.IndexOf(ai);

		if (i != -1) {
			AIs[i] = AIs[^1];
			AIs.RemoveAt(AIs.Count - 1);
		}
	}

	readonly List<AI_BaseNPC> AIs = [];
}

public enum AI_MoveEfficiency
{
	Normal,
	Efficient,
}

public struct AIScheduleState
{
	public int CurTask;
	public TaskStatus TaskStatus;
	public TimeUnit_t TimeStarted;
	public TimeUnit_t TimeCurTaskStarted;
	public AI_TaskFailureCode TaskFailureCode;
	public int TaskInterrupt;
	public bool TaskRanAutomovement;
	public bool TaskUpdatedYaw;
	public bool ScheduleWasInterrupted;
}

public enum DesiredWeaponState
{
	Ignore = 0,
	Holstered,
	HolsteredDestroyed,
	Unholstered,
	Changing,
	ChangingDestroy,
}

public enum ScriptStateType
{
	Playing = 0,
	Wait,
	PostIdle,
	Cleanup,
	WalkToMark,
	RunToMark,
	CustomMoveToMark,
}

public enum NPCInteractionState
{
	NotRunning = 0,
	RunningActive,
	RunningPartner,
	MovingToMark,
}

public struct AIRebalanceInfo
{
	public AI_BaseNPC NPC;
	public int NextThinkTick;
	public bool InPVS;
	public float DotPlayer;
	public float DistPlayer;
}

public enum AI_Efficiency
{
	Normal,
	Efficient,
	VeryEfficient,
	SuperEfficient,
	Dormant,
}

public enum AI_SleepState
{
	Awake,
	WaitingForThreat,
	WaitingForPVS,
	WaitingForInput,
	AutoPVS,
	AutoPVSAfterPVS,
}

public ref struct TriggerTraceEnum(ref Ray ray, in TakeDamageInfo info, in Vector3 dir, Mask mask) : IEntityEnumerator
{
	Vector3 VecDir = dir;
	Mask ContentsMask = mask;
	ref Ray Ray = ref ray;
	TakeDamageInfo Info = info;

	public bool EnumEntity(IHandleEntity? handleEntity) {
		Trace tr = default;

		BaseEntity? ent = gEntList.GetBaseEntity(handleEntity!.GetRefEHandle());

		// Done to avoid hitting an entity that's both solid & a trigger.
		if (ent!.IsSolid())
			return true;

		enginetrace.ClipRayToEntity(in Ray, ContentsMask, handleEntity, ref tr);
		if (tr.Fraction < 1.0f) {
			ent.DispatchTraceAttack(Info, VecDir, ref tr);
			ApplyMultiDamage();
		}

		return true;
	}
}

[NetworkName("CAI_BaseNPC")]
public class AI_BaseNPC : BaseCombatCharacter, IAI_MovementSink
{
	public static ReadOnlySpan<char> GetActivityName(Activity actID) {
		if (actID == Activity.ACT_INVALID)
			return "ACT_INVALID";

		string? name = ActivityList.NameForIndex(actID);

		if (name == null)
			Assert(false, "AI_BaseNPC.GetActivityName() returning NULL!");

		return name;
	}

	public static readonly SendTable DT_AI_BaseNPC = new(DT_BaseCombatCharacter, [
		SendPropInt(FIELD.OF(nameof(LifeState)), 3, PropFlags.Unsigned),
		SendPropBool(FIELD.OF(nameof(PerformAvoidance))),
		SendPropBool(FIELD.OF(nameof(IsMovingValue))),
		SendPropBool(FIELD.OF(nameof(FadeCorpse))),
		SendPropInt(FIELD.OF(nameof(DeathPose)), 12),
		SendPropInt(FIELD.OF(nameof(DeathFrame)), 5),
		SendPropBool(FIELD.OF(nameof(ImportantRagdoll))),
	]);
	public static readonly new ServerClass ServerClass = new ServerClass(DT_AI_BaseNPC);

	public static readonly new DataMap DataDesc = new(typeof(AI_BaseNPC), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(SpawnEquipment), FieldType.String, "additionalequipment"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	[NetworkName("m_bPerformAvoidance")]
	public bool PerformAvoidance;
	[NetworkName("m_bIsMoving")]
	public bool IsMovingValue;
	[NetworkName("m_bFadeCorpse")]
	public bool FadeCorpse;
	[NetworkName("m_iDeathPose")]
	public int DeathPose;
	[NetworkName("m_iDeathFrame")]
	public int DeathFrame;
	public bool SpeedModActive;
	public int SpeedModRadius;
	public int SpeedModSpeed;
	[NetworkName("m_bImportanRagdoll")]
	public bool ImportantRagdoll;
	public float TimePingEffect;

	public static TimeUnit_t TimeLastSpawn;
	public static int SpawnedThisFrame;

	public float OriginalYaw;
	public AI_MemoryFlags Memory;
	public float DistTooFar;
	public AI_ScheduleBits Conditions;
	public bool ConditionsGatheredValue;
	public bool ForceConditionsGather;
	public Capability Capability;
	public string? SpawnEquipment;
	public RandStopwatch GiveUpOnDeadEnemyTimer = new();
	public TimeUnit_t TimeLastMovement;
	public TimeUnit_t IgnoreDangerSoundsUntil;
	public int EnemiesSerialNumber;
	public EHANDLE Enemy = new();
	public EHANDLE GoalEnt = new();
	public Handle<AI_Hint> HintNode = new();
	public TimeUnit_t LastRealThinkTime;
	public TimeUnit_t NextEyeLookTime;
	public Handle<AI_ScriptedSequence> Cine = new();
	public Activity ScriptArrivalActivity;
	public string? ScriptArrivalSequence;

	public NPCState NPCState;
	public TimeUnit_t LastStateChangeTime;
	public NPCState IdealNPCState;
	public AI_Efficiency Efficiency;
	public AI_SleepState SleepState;
	public AI_SleepFlags SleepFlags;

	public Activity Activity;
	public Activity IdealActivity;
	public int IdealSequence;
	public Activity IdealTranslatedActivity;
	public Activity IdealWeaponActivity;

	public AI_Senses? Senses;
	public AI_Navigator? Navigator;
	public AI_LocalNavigator? LocalNavigator;
	public AI_Pathfinder? Pathfinder;
	public AI_MoveProbe? MoveProbe;
	public AI_Motor? Motor;
	public AI_TacticalServices? TacticalServices;
	public AI_MoveAndShootOverlay MoveAndShootOverlay = new();

	public AI_Squad? Squad;
	public string? SquadName;

	public static AI_DebugFlags DebugBits = 0;
	public static int DebugPauseIndex = -1;

	public static readonly AI_ClassScheduleIdSpace ClassScheduleIdSpace = new(true);
	public static readonly AI_GlobalScheduleNamespace SchedulingSymbols = new();

	public static string? PlayerSquad;

	public static int NextThinkRebalanceTick;

	public static readonly SimpleSimTimer AnyUpdateEnemyPosTimer = new();

	public bool IsUsingSmallHullValue;
	public bool CheckContacts;
	public Vector3 DefaultEyeOffset;
	public Vector3 CommandGoal;
	public readonly AI_MoveMonitor CommandMoveMonitor = new();
	public AIScheduleState ScheduleState;
	public AI_Schedule? Schedule;
	public int IdealSchedule;
	public int FailSchedule;
	public AI_ScheduleBits ConditionsPreIgnore;
	public AI_ScheduleBits InverseIgnoreConditions;
	public TimeUnit_t TimeEnemyAcquired;
	public float LastShootAccuracy;
	public int TotalShots;
	public int TotalHits;
	public Activity TranslatedActivity;
	public bool Crouching;
	public bool ForceCrouch;
	public bool CrouchDesired;
	public bool InAScript;
	public TimeUnit_t SceneTime;
	public string? SceneCustomMoveSeq;
	public EHANDLE TargetEnt = new();
	public AI_MoveEfficiency MoveEfficiency;
	public TimeUnit_t NextDecisionTime;
	public float WakeRadius;
	public bool InChoreo;
	public bool UsingStandardThinkTime;
	public long FrameBlocked;
	public new int LastThinkTick;
	public TimeUnit_t LastAttackTime;
	public TimeUnit_t LastDamageTime;
	public float InteractionYaw;
	public EHANDLE OpeningDoor = new();
	public int DebugCurIndex;
	public bool PlayerAvoidState;
	public bool Selected;

	public TimeUnit_t LastSawPlayerTime;
	public TimeUnit_t LastEnemyTime;
	public AI_ScheduleBits CustomInterruptConditions;
	public AI_Schedule? FailedSchedule;
	public AI_Schedule? InterruptSchedule;
	public string? FailText;
	public string? InterruptText;
	public TimeUnit_t WaitFinished;
	public TimeUnit_t MoveWaitFinished;
	public bool DeferredNavigation;
	public TimeUnit_t NextFlinchTime;
	public TimeUnit_t NextWeaponSearchTime;
	public string? PendingWeapon;
	public DesiredWeaponState DesiredWeaponState;
	public ScriptStateType ScriptState;
	public readonly SimTimer CheckOnGroundTimer = new();
	public Handle<AI_BaseNPC> ForcedInteractionPartner = new();
	public NPCInteractionState InteractionState;
	public Vector3 SavePosition;
	public Vector3 EyeLookTarget;
	public Vector3 CurEyeTarget;
	public float EyeIntegRate = 0.95f;
	public float HeadYaw;
	public float HeadPitch;

	public OutputEvent OnHearWorld = new();
	public OutputEvent OnHearPlayer = new();
	public OutputEvent OnHearCombat = new();
	public OutputEvent OnLostEnemy = new();
	public OutputEvent OnLostPlayer = new();

	static readonly BASEPTR CallNPCThinkPtr = static self => ((AI_BaseNPC)self).CallNPCThink();

	public AI_BaseNPC() {
		Schedule = null;
		IdealSchedule = SCHED_NONE;

		Capability = 0;

		SetHullType(AI_HullType.Human);

		LastDamageTime = 0;
		LastAttackTime = 0;
		SpawnEquipment = null;

		Squad = null;

		IsUsingSmallHullValue = true;

		SetInAScript(false);

		g_AI_Manager.AddAI(this);

		if (g_AI_Manager.NumAIs() == 1) {
			AnyUpdateEnemyPosTimer.Force();
			TimeLastSpawn = -1;
			SpawnedThisFrame = 0;
			NextThinkRebalanceTick = 0;
		}

		FrameBlocked = -1;
		InChoreo = true;

		SetCollisionGroup(Source.CollisionGroup.NPC);
	}

	public override void PostConstructor(ReadOnlySpan<char> classname) {
		base.PostConstructor(classname);
		CreateComponents();
	}

	public override void UpdateOnRemove() {
		g_AI_Manager.RemoveAI(this);
		base.UpdateOnRemove();
	}

	public override bool IsNPC() => true;

	public override Mask PhysicsSolidMaskForEntity() => Mask.NPCSolid;

	public override void Precache() {
		PlayerSquad = PLAYER_SQUADNAME;

		if (SpawnEquipment != null && SpawnEquipment != "0")
			Util.PrecacheOther(SpawnEquipment);

		if (!LoadedSchedules()) {
			DevMsg($"ERROR: Rejecting spawn of {GetDebugName()} as error in NPC's schedules.\n");
			Util.Remove(this);
			return;
		}

		PrecacheScriptSound("AI_BaseNPC.SwishSound");
		PrecacheScriptSound("AI_BaseNPC.BodyDrop_Heavy");
		PrecacheScriptSound("AI_BaseNPC.BodyDrop_Light");
		PrecacheScriptSound("AI_BaseNPC.SentenceStop");

		base.Precache();
	}

	public virtual bool LoadedSchedules() => true;

	public virtual AI_ClassScheduleIdSpace GetClassScheduleIdSpace() => ClassScheduleIdSpace;

	public static AI_GlobalScheduleNamespace GetSchedulingSymbols() => SchedulingSymbols;

	public static StringRegistry? ActivitySR;
	public static StringRegistry? EventSR;

	public static int GetScheduleID(ReadOnlySpan<char> schedName) => GetSchedulingSymbols().ScheduleSymbolToId(schedName);
	public static int GetTaskID(ReadOnlySpan<char> taskName) => GetSchedulingSymbols().TaskSymbolToId(taskName);
	public static int GetConditionID(ReadOnlySpan<char> condName) => GetSchedulingSymbols().ConditionSymbolToId(condName);

	public static int GetActivityID(ReadOnlySpan<char> actName) {
		Assert(ActivitySR != null);
		if (ActivitySR == null)
			return (int)Activity.ACT_INVALID;

		return ActivitySR.GetStringID(actName);
	}

	public static void InitDefaultTaskSR() {
		AI_ClassScheduleIdSpace idSpace = ClassScheduleIdSpace;

		idSpace.AddTask("TASK_INVALID", TASK_INVALID, "CAI_BaseNPC");
		idSpace.AddTask("TASK_ANNOUNCE_ATTACK", TASK_ANNOUNCE_ATTACK, "CAI_BaseNPC");
		idSpace.AddTask("TASK_RESET_ACTIVITY", TASK_RESET_ACTIVITY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT", TASK_WAIT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT_FACE_ENEMY", TASK_WAIT_FACE_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT_FACE_ENEMY_RANDOM", TASK_WAIT_FACE_ENEMY_RANDOM, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT_PVS", TASK_WAIT_PVS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SUGGEST_STATE", TASK_SUGGEST_STATE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_TARGET_PLAYER", TASK_TARGET_PLAYER, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SCRIPT_WALK_TO_TARGET", TASK_SCRIPT_WALK_TO_TARGET, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SCRIPT_RUN_TO_TARGET", TASK_SCRIPT_RUN_TO_TARGET, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SCRIPT_CUSTOM_MOVE_TO_TARGET", TASK_SCRIPT_CUSTOM_MOVE_TO_TARGET, "CAI_BaseNPC");
		idSpace.AddTask("TASK_MOVE_TO_TARGET_RANGE", TASK_MOVE_TO_TARGET_RANGE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_MOVE_TO_GOAL_RANGE", TASK_MOVE_TO_GOAL_RANGE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_MOVE_AWAY_PATH", TASK_MOVE_AWAY_PATH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_AWAY_FROM_BEST_SOUND", TASK_GET_PATH_AWAY_FROM_BEST_SOUND, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SET_GOAL", TASK_SET_GOAL, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_GOAL", TASK_GET_PATH_TO_GOAL, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_ENEMY", TASK_GET_PATH_TO_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_ENEMY_LKP", TASK_GET_PATH_TO_ENEMY_LKP, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_CHASE_PATH_TO_ENEMY", TASK_GET_CHASE_PATH_TO_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_ENEMY_LKP_LOS", TASK_GET_PATH_TO_ENEMY_LKP_LOS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_RANGE_ENEMY_LKP_LOS", TASK_GET_PATH_TO_RANGE_ENEMY_LKP_LOS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_ENEMY_CORPSE", TASK_GET_PATH_TO_ENEMY_CORPSE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_PLAYER", TASK_GET_PATH_TO_PLAYER, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_ENEMY_LOS", TASK_GET_PATH_TO_ENEMY_LOS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_FLANK_ARC_PATH_TO_ENEMY_LOS", TASK_GET_FLANK_ARC_PATH_TO_ENEMY_LOS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_FLANK_RADIUS_PATH_TO_ENEMY_LOS", TASK_GET_FLANK_RADIUS_PATH_TO_ENEMY_LOS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_TARGET", TASK_GET_PATH_TO_TARGET, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_TARGET_WEAPON", TASK_GET_PATH_TO_TARGET_WEAPON, "CAI_BaseNPC");
		idSpace.AddTask("TASK_CREATE_PENDING_WEAPON", TASK_CREATE_PENDING_WEAPON, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_HINTNODE", TASK_GET_PATH_TO_HINTNODE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_STORE_LASTPOSITION", TASK_STORE_LASTPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_CLEAR_LASTPOSITION", TASK_CLEAR_LASTPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_STORE_POSITION_IN_SAVEPOSITION", TASK_STORE_POSITION_IN_SAVEPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_STORE_BESTSOUND_IN_SAVEPOSITION", TASK_STORE_BESTSOUND_IN_SAVEPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_STORE_BESTSOUND_REACTORIGIN_IN_SAVEPOSITION", TASK_STORE_BESTSOUND_REACTORIGIN_IN_SAVEPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_REACT_TO_COMBAT_SOUND", TASK_REACT_TO_COMBAT_SOUND, "CAI_BaseNPC");
		idSpace.AddTask("TASK_STORE_ENEMY_POSITION_IN_SAVEPOSITION", TASK_STORE_ENEMY_POSITION_IN_SAVEPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_COMMAND_GOAL", TASK_GET_PATH_TO_COMMAND_GOAL, "CAI_BaseNPC");
		idSpace.AddTask("TASK_MARK_COMMAND_GOAL_POS", TASK_MARK_COMMAND_GOAL_POS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_CLEAR_COMMAND_GOAL", TASK_CLEAR_COMMAND_GOAL, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_LASTPOSITION", TASK_GET_PATH_TO_LASTPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_SAVEPOSITION", TASK_GET_PATH_TO_SAVEPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_SAVEPOSITION_LOS", TASK_GET_PATH_TO_SAVEPOSITION_LOS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_BESTSOUND", TASK_GET_PATH_TO_BESTSOUND, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_BESTSCENT", TASK_GET_PATH_TO_BESTSCENT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_RANDOM_NODE", TASK_GET_PATH_TO_RANDOM_NODE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_RUN_PATH", TASK_RUN_PATH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WALK_PATH", TASK_WALK_PATH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WALK_PATH_TIMED", TASK_WALK_PATH_TIMED, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WALK_PATH_WITHIN_DIST", TASK_WALK_PATH_WITHIN_DIST, "CAI_BaseNPC");
		idSpace.AddTask("TASK_RUN_PATH_WITHIN_DIST", TASK_RUN_PATH_WITHIN_DIST, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WALK_PATH_FOR_UNITS", TASK_WALK_PATH_FOR_UNITS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_RUN_PATH_FOR_UNITS", TASK_RUN_PATH_FOR_UNITS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_RUN_PATH_FLEE", TASK_RUN_PATH_FLEE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_RUN_PATH_TIMED", TASK_RUN_PATH_TIMED, "CAI_BaseNPC");
		idSpace.AddTask("TASK_STRAFE_PATH", TASK_STRAFE_PATH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_CLEAR_MOVE_WAIT", TASK_CLEAR_MOVE_WAIT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SMALL_FLINCH", TASK_SMALL_FLINCH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_BIG_FLINCH", TASK_BIG_FLINCH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_DEFER_DODGE", TASK_DEFER_DODGE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_IDEAL", TASK_FACE_IDEAL, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_REASONABLE", TASK_FACE_REASONABLE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_PATH", TASK_FACE_PATH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_PLAYER", TASK_FACE_PLAYER, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_ENEMY", TASK_FACE_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_HINTNODE", TASK_FACE_HINTNODE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PLAY_HINT_ACTIVITY", TASK_PLAY_HINT_ACTIVITY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_TARGET", TASK_FACE_TARGET, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_LASTPOSITION", TASK_FACE_LASTPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_SAVEPOSITION", TASK_FACE_SAVEPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_AWAY_FROM_SAVEPOSITION", TASK_FACE_AWAY_FROM_SAVEPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SET_IDEAL_YAW_TO_CURRENT", TASK_SET_IDEAL_YAW_TO_CURRENT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_RANGE_ATTACK1", TASK_RANGE_ATTACK1, "CAI_BaseNPC");
		idSpace.AddTask("TASK_RANGE_ATTACK2", TASK_RANGE_ATTACK2, "CAI_BaseNPC");
		idSpace.AddTask("TASK_MELEE_ATTACK1", TASK_MELEE_ATTACK1, "CAI_BaseNPC");
		idSpace.AddTask("TASK_MELEE_ATTACK2", TASK_MELEE_ATTACK2, "CAI_BaseNPC");
		idSpace.AddTask("TASK_RELOAD", TASK_RELOAD, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SPECIAL_ATTACK1", TASK_SPECIAL_ATTACK1, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SPECIAL_ATTACK2", TASK_SPECIAL_ATTACK2, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FIND_HINTNODE", TASK_FIND_HINTNODE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_CLEAR_HINTNODE", TASK_CLEAR_HINTNODE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FIND_LOCK_HINTNODE", TASK_FIND_LOCK_HINTNODE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_LOCK_HINTNODE", TASK_LOCK_HINTNODE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SOUND_ANGRY", TASK_SOUND_ANGRY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SOUND_DEATH", TASK_SOUND_DEATH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SOUND_IDLE", TASK_SOUND_IDLE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SOUND_WAKE", TASK_SOUND_WAKE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SOUND_PAIN", TASK_SOUND_PAIN, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SOUND_DIE", TASK_SOUND_DIE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SPEAK_SENTENCE", TASK_SPEAK_SENTENCE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT_FOR_SPEAK_FINISH", TASK_WAIT_FOR_SPEAK_FINISH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SET_ACTIVITY", TASK_SET_ACTIVITY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_RANDOMIZE_FRAMERATE", TASK_RANDOMIZE_FRAMERATE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SET_SCHEDULE", TASK_SET_SCHEDULE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SET_FAIL_SCHEDULE", TASK_SET_FAIL_SCHEDULE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SET_TOLERANCE_DISTANCE", TASK_SET_TOLERANCE_DISTANCE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_SET_ROUTE_SEARCH_TIME", TASK_SET_ROUTE_SEARCH_TIME, "CAI_BaseNPC");
		idSpace.AddTask("TASK_CLEAR_FAIL_SCHEDULE", TASK_CLEAR_FAIL_SCHEDULE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PLAY_SEQUENCE", TASK_PLAY_SEQUENCE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PLAY_PRIVATE_SEQUENCE", TASK_PLAY_PRIVATE_SEQUENCE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PLAY_PRIVATE_SEQUENCE_FACE_ENEMY", TASK_PLAY_PRIVATE_SEQUENCE_FACE_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PLAY_SEQUENCE_FACE_ENEMY", TASK_PLAY_SEQUENCE_FACE_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PLAY_SEQUENCE_FACE_TARGET", TASK_PLAY_SEQUENCE_FACE_TARGET, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FIND_COVER_FROM_BEST_SOUND", TASK_FIND_COVER_FROM_BEST_SOUND, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FIND_COVER_FROM_ENEMY", TASK_FIND_COVER_FROM_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FIND_LATERAL_COVER_FROM_ENEMY", TASK_FIND_LATERAL_COVER_FROM_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FIND_BACKAWAY_FROM_SAVEPOSITION", TASK_FIND_BACKAWAY_FROM_SAVEPOSITION, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FIND_NODE_COVER_FROM_ENEMY", TASK_FIND_NODE_COVER_FROM_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FIND_NEAR_NODE_COVER_FROM_ENEMY", TASK_FIND_NEAR_NODE_COVER_FROM_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FIND_FAR_NODE_COVER_FROM_ENEMY", TASK_FIND_FAR_NODE_COVER_FROM_ENEMY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FIND_COVER_FROM_ORIGIN", TASK_FIND_COVER_FROM_ORIGIN, "CAI_BaseNPC");
		idSpace.AddTask("TASK_DIE", TASK_DIE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT_FOR_SCRIPT", TASK_WAIT_FOR_SCRIPT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PUSH_SCRIPT_ARRIVAL_ACTIVITY", TASK_PUSH_SCRIPT_ARRIVAL_ACTIVITY, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PLAY_SCRIPT", TASK_PLAY_SCRIPT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PLAY_SCRIPT_POST_IDLE", TASK_PLAY_SCRIPT_POST_IDLE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_ENABLE_SCRIPT", TASK_ENABLE_SCRIPT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PLANT_ON_SCRIPT", TASK_PLANT_ON_SCRIPT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FACE_SCRIPT", TASK_FACE_SCRIPT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PLAY_SCENE", TASK_PLAY_SCENE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT_RANDOM", TASK_WAIT_RANDOM, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT_INDEFINITE", TASK_WAIT_INDEFINITE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_STOP_MOVING", TASK_STOP_MOVING, "CAI_BaseNPC");
		idSpace.AddTask("TASK_TURN_LEFT", TASK_TURN_LEFT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_TURN_RIGHT", TASK_TURN_RIGHT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_REMEMBER", TASK_REMEMBER, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FORGET", TASK_FORGET, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT_FOR_MOVEMENT", TASK_WAIT_FOR_MOVEMENT, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT_FOR_MOVEMENT_STEP", TASK_WAIT_FOR_MOVEMENT_STEP, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WAIT_UNTIL_NO_DANGER_SOUND", TASK_WAIT_UNTIL_NO_DANGER_SOUND, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WEAPON_FIND", TASK_WEAPON_FIND, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WEAPON_PICKUP", TASK_WEAPON_PICKUP, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WEAPON_RUN_PATH", TASK_WEAPON_RUN_PATH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WEAPON_CREATE", TASK_WEAPON_CREATE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_ITEM_RUN_PATH", TASK_ITEM_RUN_PATH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_ITEM_PICKUP", TASK_ITEM_PICKUP, "CAI_BaseNPC");
		idSpace.AddTask("TASK_USE_SMALL_HULL", TASK_USE_SMALL_HULL, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FALL_TO_GROUND", TASK_FALL_TO_GROUND, "CAI_BaseNPC");
		idSpace.AddTask("TASK_WANDER", TASK_WANDER, "CAI_BaseNPC");
		idSpace.AddTask("TASK_FREEZE", TASK_FREEZE, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GATHER_CONDITIONS", TASK_GATHER_CONDITIONS, "CAI_BaseNPC");
		idSpace.AddTask("TASK_IGNORE_OLD_ENEMIES", TASK_IGNORE_OLD_ENEMIES, "CAI_BaseNPC");
		idSpace.AddTask("TASK_DEBUG_BREAK", TASK_DEBUG_BREAK, "CAI_BaseNPC");
		idSpace.AddTask("TASK_ADD_HEALTH", TASK_ADD_HEALTH, "CAI_BaseNPC");
		idSpace.AddTask("TASK_GET_PATH_TO_INTERACTION_PARTNER", TASK_GET_PATH_TO_INTERACTION_PARTNER, "CAI_BaseNPC");
		idSpace.AddTask("TASK_PRE_SCRIPT", TASK_PRE_SCRIPT, "CAI_BaseNPC");
	}

	public static void InitDefaultConditionSR() {
		AI_ClassScheduleIdSpace idSpace = ClassScheduleIdSpace;

		idSpace.AddCondition("COND_NONE", (int)SCOND_t.COND_NONE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_IN_PVS", (int)SCOND_t.COND_IN_PVS, "CAI_BaseNPC");
		idSpace.AddCondition("COND_IDLE_INTERRUPT", (int)SCOND_t.COND_IDLE_INTERRUPT, "CAI_BaseNPC");
		idSpace.AddCondition("COND_LOW_PRIMARY_AMMO", (int)SCOND_t.COND_LOW_PRIMARY_AMMO, "CAI_BaseNPC");
		idSpace.AddCondition("COND_NO_PRIMARY_AMMO", (int)SCOND_t.COND_NO_PRIMARY_AMMO, "CAI_BaseNPC");
		idSpace.AddCondition("COND_NO_SECONDARY_AMMO", (int)SCOND_t.COND_NO_SECONDARY_AMMO, "CAI_BaseNPC");
		idSpace.AddCondition("COND_NO_WEAPON", (int)SCOND_t.COND_NO_WEAPON, "CAI_BaseNPC");
		idSpace.AddCondition("COND_SEE_HATE", (int)SCOND_t.COND_SEE_HATE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_SEE_FEAR", (int)SCOND_t.COND_SEE_FEAR, "CAI_BaseNPC");
		idSpace.AddCondition("COND_SEE_DISLIKE", (int)SCOND_t.COND_SEE_DISLIKE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_SEE_ENEMY", (int)SCOND_t.COND_SEE_ENEMY, "CAI_BaseNPC");
		idSpace.AddCondition("COND_LOST_ENEMY", (int)SCOND_t.COND_LOST_ENEMY, "CAI_BaseNPC");
		idSpace.AddCondition("COND_ENEMY_WENT_NULL", (int)SCOND_t.COND_ENEMY_WENT_NULL, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HAVE_ENEMY_LOS", (int)SCOND_t.COND_HAVE_ENEMY_LOS, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HAVE_TARGET_LOS", (int)SCOND_t.COND_HAVE_TARGET_LOS, "CAI_BaseNPC");
		idSpace.AddCondition("COND_ENEMY_OCCLUDED", (int)SCOND_t.COND_ENEMY_OCCLUDED, "CAI_BaseNPC");
		idSpace.AddCondition("COND_TARGET_OCCLUDED", (int)SCOND_t.COND_TARGET_OCCLUDED, "CAI_BaseNPC");
		idSpace.AddCondition("COND_ENEMY_TOO_FAR", (int)SCOND_t.COND_ENEMY_TOO_FAR, "CAI_BaseNPC");
		idSpace.AddCondition("COND_LIGHT_DAMAGE", (int)SCOND_t.COND_LIGHT_DAMAGE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEAVY_DAMAGE", (int)SCOND_t.COND_HEAVY_DAMAGE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_PHYSICS_DAMAGE", (int)SCOND_t.COND_PHYSICS_DAMAGE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_REPEATED_DAMAGE", (int)SCOND_t.COND_REPEATED_DAMAGE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_CAN_RANGE_ATTACK1", (int)SCOND_t.COND_CAN_RANGE_ATTACK1, "CAI_BaseNPC");
		idSpace.AddCondition("COND_CAN_RANGE_ATTACK2", (int)SCOND_t.COND_CAN_RANGE_ATTACK2, "CAI_BaseNPC");
		idSpace.AddCondition("COND_CAN_MELEE_ATTACK1", (int)SCOND_t.COND_CAN_MELEE_ATTACK1, "CAI_BaseNPC");
		idSpace.AddCondition("COND_CAN_MELEE_ATTACK2", (int)SCOND_t.COND_CAN_MELEE_ATTACK2, "CAI_BaseNPC");
		idSpace.AddCondition("COND_PROVOKED", (int)SCOND_t.COND_PROVOKED, "CAI_BaseNPC");
		idSpace.AddCondition("COND_NEW_ENEMY", (int)SCOND_t.COND_NEW_ENEMY, "CAI_BaseNPC");
		idSpace.AddCondition("COND_ENEMY_FACING_ME", (int)SCOND_t.COND_ENEMY_FACING_ME, "CAI_BaseNPC");
		idSpace.AddCondition("COND_BEHIND_ENEMY", (int)SCOND_t.COND_BEHIND_ENEMY, "CAI_BaseNPC");
		idSpace.AddCondition("COND_ENEMY_DEAD", (int)SCOND_t.COND_ENEMY_DEAD, "CAI_BaseNPC");
		idSpace.AddCondition("COND_ENEMY_UNREACHABLE", (int)SCOND_t.COND_ENEMY_UNREACHABLE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_SEE_PLAYER", (int)SCOND_t.COND_SEE_PLAYER, "CAI_BaseNPC");
		idSpace.AddCondition("COND_LOST_PLAYER", (int)SCOND_t.COND_LOST_PLAYER, "CAI_BaseNPC");
		idSpace.AddCondition("COND_SEE_NEMESIS", (int)SCOND_t.COND_SEE_NEMESIS, "CAI_BaseNPC");
		idSpace.AddCondition("COND_TASK_FAILED", (int)SCOND_t.COND_TASK_FAILED, "CAI_BaseNPC");
		idSpace.AddCondition("COND_SCHEDULE_DONE", (int)SCOND_t.COND_SCHEDULE_DONE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_SMELL", (int)SCOND_t.COND_SMELL, "CAI_BaseNPC");
		idSpace.AddCondition("COND_TOO_CLOSE_TO_ATTACK", (int)SCOND_t.COND_TOO_CLOSE_TO_ATTACK, "CAI_BaseNPC");
		idSpace.AddCondition("COND_TOO_FAR_TO_ATTACK", (int)SCOND_t.COND_TOO_FAR_TO_ATTACK, "CAI_BaseNPC");
		idSpace.AddCondition("COND_NOT_FACING_ATTACK", (int)SCOND_t.COND_NOT_FACING_ATTACK, "CAI_BaseNPC");
		idSpace.AddCondition("COND_WEAPON_HAS_LOS", (int)SCOND_t.COND_WEAPON_HAS_LOS, "CAI_BaseNPC");
		idSpace.AddCondition("COND_WEAPON_BLOCKED_BY_FRIEND", (int)SCOND_t.COND_WEAPON_BLOCKED_BY_FRIEND, "CAI_BaseNPC");
		idSpace.AddCondition("COND_WEAPON_PLAYER_IN_SPREAD", (int)SCOND_t.COND_WEAPON_PLAYER_IN_SPREAD, "CAI_BaseNPC");
		idSpace.AddCondition("COND_WEAPON_PLAYER_NEAR_TARGET", (int)SCOND_t.COND_WEAPON_PLAYER_NEAR_TARGET, "CAI_BaseNPC");
		idSpace.AddCondition("COND_WEAPON_SIGHT_OCCLUDED", (int)SCOND_t.COND_WEAPON_SIGHT_OCCLUDED, "CAI_BaseNPC");
		idSpace.AddCondition("COND_BETTER_WEAPON_AVAILABLE", (int)SCOND_t.COND_BETTER_WEAPON_AVAILABLE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEALTH_ITEM_AVAILABLE", (int)SCOND_t.COND_HEALTH_ITEM_AVAILABLE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_FLOATING_OFF_GROUND", (int)SCOND_t.COND_FLOATING_OFF_GROUND, "CAI_BaseNPC");
		idSpace.AddCondition("COND_MOBBED_BY_ENEMIES", (int)SCOND_t.COND_MOBBED_BY_ENEMIES, "CAI_BaseNPC");
		idSpace.AddCondition("COND_GIVE_WAY", (int)SCOND_t.COND_GIVE_WAY, "CAI_BaseNPC");
		idSpace.AddCondition("COND_WAY_CLEAR", (int)SCOND_t.COND_WAY_CLEAR, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEAR_DANGER", (int)SCOND_t.COND_HEAR_DANGER, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEAR_THUMPER", (int)SCOND_t.COND_HEAR_THUMPER, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEAR_COMBAT", (int)SCOND_t.COND_HEAR_COMBAT, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEAR_WORLD", (int)SCOND_t.COND_HEAR_WORLD, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEAR_PLAYER", (int)SCOND_t.COND_HEAR_PLAYER, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEAR_BULLET_IMPACT", (int)SCOND_t.COND_HEAR_BULLET_IMPACT, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEAR_BUGBAIT", (int)SCOND_t.COND_HEAR_BUGBAIT, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEAR_PHYSICS_DANGER", (int)SCOND_t.COND_HEAR_PHYSICS_DANGER, "CAI_BaseNPC");
		idSpace.AddCondition("COND_HEAR_MOVE_AWAY", (int)SCOND_t.COND_HEAR_MOVE_AWAY, "CAI_BaseNPC");
		idSpace.AddCondition("COND_NO_HEAR_DANGER", (int)SCOND_t.COND_NO_HEAR_DANGER, "CAI_BaseNPC");
		idSpace.AddCondition("COND_PLAYER_PUSHING", (int)SCOND_t.COND_PLAYER_PUSHING, "CAI_BaseNPC");
		idSpace.AddCondition("COND_RECEIVED_ORDERS", (int)SCOND_t.COND_RECEIVED_ORDERS, "CAI_BaseNPC");
		idSpace.AddCondition("COND_PLAYER_ADDED_TO_SQUAD", (int)SCOND_t.COND_PLAYER_ADDED_TO_SQUAD, "CAI_BaseNPC");
		idSpace.AddCondition("COND_PLAYER_REMOVED_FROM_SQUAD", (int)SCOND_t.COND_PLAYER_REMOVED_FROM_SQUAD, "CAI_BaseNPC");
		idSpace.AddCondition("COND_NPC_FREEZE", (int)SCOND_t.COND_NPC_FREEZE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_NPC_UNFREEZE", (int)SCOND_t.COND_NPC_UNFREEZE, "CAI_BaseNPC");
		idSpace.AddCondition("COND_TALKER_RESPOND_TO_QUESTION", (int)SCOND_t.COND_TALKER_RESPOND_TO_QUESTION, "CAI_BaseNPC");
		idSpace.AddCondition("COND_NO_CUSTOM_INTERRUPTS", (int)SCOND_t.COND_NO_CUSTOM_INTERRUPTS, "CAI_BaseNPC");
	}

	public static void InitDefaultScheduleSR() {
		AI_ClassScheduleIdSpace idSpace = ClassScheduleIdSpace;

		idSpace.AddSchedule("SCHED_NONE", SCHED_NONE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_IDLE_STAND", SCHED_IDLE_STAND, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_IDLE_WALK", SCHED_IDLE_WALK, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_IDLE_WANDER", SCHED_IDLE_WANDER, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_WAKE_ANGRY", SCHED_WAKE_ANGRY, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_ALERT_FACE", SCHED_ALERT_FACE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_ALERT_FACE_BESTSOUND", SCHED_ALERT_FACE_BESTSOUND, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_ALERT_REACT_TO_COMBAT_SOUND", SCHED_ALERT_REACT_TO_COMBAT_SOUND, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_ALERT_SCAN", SCHED_ALERT_SCAN, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_ALERT_STAND", SCHED_ALERT_STAND, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_ALERT_WALK", SCHED_ALERT_WALK, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_INVESTIGATE_SOUND", SCHED_INVESTIGATE_SOUND, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_COMBAT_FACE", SCHED_COMBAT_FACE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_COMBAT_SWEEP", SCHED_COMBAT_SWEEP, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_FEAR_FACE", SCHED_FEAR_FACE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_COMBAT_STAND", SCHED_COMBAT_STAND, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_COMBAT_WALK", SCHED_COMBAT_WALK, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_CHASE_ENEMY", SCHED_CHASE_ENEMY, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_CHASE_ENEMY_FAILED", SCHED_CHASE_ENEMY_FAILED, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_VICTORY_DANCE", SCHED_VICTORY_DANCE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_TARGET_FACE", SCHED_TARGET_FACE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_TARGET_CHASE", SCHED_TARGET_CHASE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SMALL_FLINCH", SCHED_SMALL_FLINCH, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_BIG_FLINCH", SCHED_BIG_FLINCH, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_BACK_AWAY_FROM_ENEMY", SCHED_BACK_AWAY_FROM_ENEMY, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_MOVE_AWAY_FROM_ENEMY", SCHED_MOVE_AWAY_FROM_ENEMY, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_BACK_AWAY_FROM_SAVE_POSITION", SCHED_BACK_AWAY_FROM_SAVE_POSITION, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_TAKE_COVER_FROM_ENEMY", SCHED_TAKE_COVER_FROM_ENEMY, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_TAKE_COVER_FROM_BEST_SOUND", SCHED_TAKE_COVER_FROM_BEST_SOUND, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_FLEE_FROM_BEST_SOUND", SCHED_FLEE_FROM_BEST_SOUND, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_TAKE_COVER_FROM_ORIGIN", SCHED_TAKE_COVER_FROM_ORIGIN, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_FAIL_TAKE_COVER", SCHED_FAIL_TAKE_COVER, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_RUN_FROM_ENEMY", SCHED_RUN_FROM_ENEMY, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_RUN_FROM_ENEMY_FALLBACK", SCHED_RUN_FROM_ENEMY_FALLBACK, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_MOVE_TO_WEAPON_RANGE", SCHED_MOVE_TO_WEAPON_RANGE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_ESTABLISH_LINE_OF_FIRE", SCHED_ESTABLISH_LINE_OF_FIRE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SHOOT_ENEMY_COVER", SCHED_SHOOT_ENEMY_COVER, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_ESTABLISH_LINE_OF_FIRE_FALLBACK", SCHED_ESTABLISH_LINE_OF_FIRE_FALLBACK, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_PRE_FAIL_ESTABLISH_LINE_OF_FIRE", SCHED_PRE_FAIL_ESTABLISH_LINE_OF_FIRE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_FAIL_ESTABLISH_LINE_OF_FIRE", SCHED_FAIL_ESTABLISH_LINE_OF_FIRE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_COWER", SCHED_COWER, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_MELEE_ATTACK1", SCHED_MELEE_ATTACK1, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_MELEE_ATTACK2", SCHED_MELEE_ATTACK2, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_RANGE_ATTACK1", SCHED_RANGE_ATTACK1, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_RANGE_ATTACK2", SCHED_RANGE_ATTACK2, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SPECIAL_ATTACK1", SCHED_SPECIAL_ATTACK1, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SPECIAL_ATTACK2", SCHED_SPECIAL_ATTACK2, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_STANDOFF", SCHED_STANDOFF, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_ARM_WEAPON", SCHED_ARM_WEAPON, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_DISARM_WEAPON", SCHED_DISARM_WEAPON, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_HIDE_AND_RELOAD", SCHED_HIDE_AND_RELOAD, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_RELOAD", SCHED_RELOAD, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_AMBUSH", SCHED_AMBUSH, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_DIE", SCHED_DIE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_DIE_RAGDOLL", SCHED_DIE_RAGDOLL, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_WAIT_FOR_SCRIPT", SCHED_WAIT_FOR_SCRIPT, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_AISCRIPT", SCHED_AISCRIPT, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SCRIPTED_WALK", SCHED_SCRIPTED_WALK, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SCRIPTED_RUN", SCHED_SCRIPTED_RUN, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SCRIPTED_CUSTOM_MOVE", SCHED_SCRIPTED_CUSTOM_MOVE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SCRIPTED_WAIT", SCHED_SCRIPTED_WAIT, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SCRIPTED_FACE", SCHED_SCRIPTED_FACE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SCENE_GENERIC", SCHED_SCENE_GENERIC, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_NEW_WEAPON", SCHED_NEW_WEAPON, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_NEW_WEAPON_CHEAT", SCHED_NEW_WEAPON_CHEAT, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SWITCH_TO_PENDING_WEAPON", SCHED_SWITCH_TO_PENDING_WEAPON, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_GET_HEALTHKIT", SCHED_GET_HEALTHKIT, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_MOVE_AWAY", SCHED_MOVE_AWAY, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_MOVE_AWAY_FAIL", SCHED_MOVE_AWAY_FAIL, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_MOVE_AWAY_END", SCHED_MOVE_AWAY_END, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_WAIT_FOR_SPEAK_FINISH", SCHED_WAIT_FOR_SPEAK_FINISH, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_FORCED_GO", SCHED_FORCED_GO, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_FORCED_GO_RUN", SCHED_FORCED_GO_RUN, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_PATROL_WALK", SCHED_PATROL_WALK, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_COMBAT_PATROL", SCHED_COMBAT_PATROL, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_PATROL_RUN", SCHED_PATROL_RUN, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_RUN_RANDOM", SCHED_RUN_RANDOM, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_FAIL", SCHED_FAIL, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_FAIL_NOSTOP", SCHED_FAIL_NOSTOP, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_FALL_TO_GROUND", SCHED_FALL_TO_GROUND, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_DROPSHIP_DUSTOFF", SCHED_DROPSHIP_DUSTOFF, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_NPC_FREEZE", SCHED_NPC_FREEZE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_FLINCH_PHYSICS", SCHED_FLINCH_PHYSICS, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_RUN_FROM_ENEMY_MOB", SCHED_RUN_FROM_ENEMY_MOB, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_DUCK_DODGE", SCHED_DUCK_DODGE, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_INTERACTION_MOVE_TO_PARTNER", SCHED_INTERACTION_MOVE_TO_PARTNER, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_INTERACTION_WAIT_FOR_PARTNER", SCHED_INTERACTION_WAIT_FOR_PARTNER, "CAI_BaseNPC");
		idSpace.AddSchedule("SCHED_SLEEP", SCHED_SLEEP, "CAI_BaseNPC");
	}

	public static int NumActivities;
	static int LastActID = -2;

	public static void AddActivityToSR(ReadOnlySpan<char> actName, int actID) {
		Assert(ActivitySR != null);
		if (ActivitySR == null)
			return;

		Assert(actID >= (int)Activity.LAST_SHARED_ACTIVITY || actID == LastActID + 1 || actID == (int)Activity.ACT_INVALID);
		LastActID = actID;

		ActivitySR.AddString(actName, actID);
		NumActivities++;
	}

	public static void InitDefaultActivitySR() {
		AddActivityToSR("ACT_INVALID", (int)Activity.ACT_INVALID);
		AddActivityToSR("ACT_RESET", (int)Activity.ACT_RESET);
		AddActivityToSR("ACT_IDLE", (int)Activity.ACT_IDLE);
		AddActivityToSR("ACT_TRANSITION", (int)Activity.ACT_TRANSITION);
		AddActivityToSR("ACT_COVER", (int)Activity.ACT_COVER);
		AddActivityToSR("ACT_COVER_MED", (int)Activity.ACT_COVER_MED);
		AddActivityToSR("ACT_COVER_LOW", (int)Activity.ACT_COVER_LOW);
		AddActivityToSR("ACT_WALK", (int)Activity.ACT_WALK);
		AddActivityToSR("ACT_WALK_AIM", (int)Activity.ACT_WALK_AIM);
		AddActivityToSR("ACT_WALK_CROUCH", (int)Activity.ACT_WALK_CROUCH);
		AddActivityToSR("ACT_WALK_CROUCH_AIM", (int)Activity.ACT_WALK_CROUCH_AIM);
		AddActivityToSR("ACT_RUN", (int)Activity.ACT_RUN);
		AddActivityToSR("ACT_RUN_AIM", (int)Activity.ACT_RUN_AIM);
		AddActivityToSR("ACT_RUN_CROUCH", (int)Activity.ACT_RUN_CROUCH);
		AddActivityToSR("ACT_RUN_CROUCH_AIM", (int)Activity.ACT_RUN_CROUCH_AIM);
		AddActivityToSR("ACT_RUN_PROTECTED", (int)Activity.ACT_RUN_PROTECTED);
		AddActivityToSR("ACT_SCRIPT_CUSTOM_MOVE", (int)Activity.ACT_SCRIPT_CUSTOM_MOVE);
		AddActivityToSR("ACT_RANGE_ATTACK1", (int)Activity.ACT_RANGE_ATTACK1);
		AddActivityToSR("ACT_RANGE_ATTACK2", (int)Activity.ACT_RANGE_ATTACK2);
		AddActivityToSR("ACT_RANGE_ATTACK1_LOW", (int)Activity.ACT_RANGE_ATTACK1_LOW);
		AddActivityToSR("ACT_RANGE_ATTACK2_LOW", (int)Activity.ACT_RANGE_ATTACK2_LOW);
		AddActivityToSR("ACT_DIESIMPLE", (int)Activity.ACT_DIESIMPLE);
		AddActivityToSR("ACT_DIEBACKWARD", (int)Activity.ACT_DIEBACKWARD);
		AddActivityToSR("ACT_DIEFORWARD", (int)Activity.ACT_DIEFORWARD);
		AddActivityToSR("ACT_DIEVIOLENT", (int)Activity.ACT_DIEVIOLENT);
		AddActivityToSR("ACT_DIERAGDOLL", (int)Activity.ACT_DIERAGDOLL);
		AddActivityToSR("ACT_FLY", (int)Activity.ACT_FLY);
		AddActivityToSR("ACT_HOVER", (int)Activity.ACT_HOVER);
		AddActivityToSR("ACT_GLIDE", (int)Activity.ACT_GLIDE);
		AddActivityToSR("ACT_SWIM", (int)Activity.ACT_SWIM);
		AddActivityToSR("ACT_SWIM_IDLE", (int)Activity.ACT_SWIM_IDLE);
		AddActivityToSR("ACT_JUMP", (int)Activity.ACT_JUMP);
		AddActivityToSR("ACT_HOP", (int)Activity.ACT_HOP);
		AddActivityToSR("ACT_LEAP", (int)Activity.ACT_LEAP);
		AddActivityToSR("ACT_LAND", (int)Activity.ACT_LAND);
		AddActivityToSR("ACT_CLIMB_UP", (int)Activity.ACT_CLIMB_UP);
		AddActivityToSR("ACT_CLIMB_DOWN", (int)Activity.ACT_CLIMB_DOWN);
		AddActivityToSR("ACT_CLIMB_DISMOUNT", (int)Activity.ACT_CLIMB_DISMOUNT);
		AddActivityToSR("ACT_SHIPLADDER_UP", (int)Activity.ACT_SHIPLADDER_UP);
		AddActivityToSR("ACT_SHIPLADDER_DOWN", (int)Activity.ACT_SHIPLADDER_DOWN);
		AddActivityToSR("ACT_STRAFE_LEFT", (int)Activity.ACT_STRAFE_LEFT);
		AddActivityToSR("ACT_STRAFE_RIGHT", (int)Activity.ACT_STRAFE_RIGHT);
		AddActivityToSR("ACT_ROLL_LEFT", (int)Activity.ACT_ROLL_LEFT);
		AddActivityToSR("ACT_ROLL_RIGHT", (int)Activity.ACT_ROLL_RIGHT);
		AddActivityToSR("ACT_TURN_LEFT", (int)Activity.ACT_TURN_LEFT);
		AddActivityToSR("ACT_TURN_RIGHT", (int)Activity.ACT_TURN_RIGHT);
		AddActivityToSR("ACT_CROUCH", (int)Activity.ACT_CROUCH);
		AddActivityToSR("ACT_CROUCHIDLE", (int)Activity.ACT_CROUCHIDLE);
		AddActivityToSR("ACT_STAND", (int)Activity.ACT_STAND);
		AddActivityToSR("ACT_USE", (int)Activity.ACT_USE);
		AddActivityToSR("ACT_SIGNAL1", (int)Activity.ACT_SIGNAL1);
		AddActivityToSR("ACT_SIGNAL2", (int)Activity.ACT_SIGNAL2);
		AddActivityToSR("ACT_SIGNAL3", (int)Activity.ACT_SIGNAL3);
		AddActivityToSR("ACT_SIGNAL_ADVANCE", (int)Activity.ACT_SIGNAL_ADVANCE);
		AddActivityToSR("ACT_SIGNAL_FORWARD", (int)Activity.ACT_SIGNAL_FORWARD);
		AddActivityToSR("ACT_SIGNAL_GROUP", (int)Activity.ACT_SIGNAL_GROUP);
		AddActivityToSR("ACT_SIGNAL_HALT", (int)Activity.ACT_SIGNAL_HALT);
		AddActivityToSR("ACT_SIGNAL_LEFT", (int)Activity.ACT_SIGNAL_LEFT);
		AddActivityToSR("ACT_SIGNAL_RIGHT", (int)Activity.ACT_SIGNAL_RIGHT);
		AddActivityToSR("ACT_SIGNAL_TAKECOVER", (int)Activity.ACT_SIGNAL_TAKECOVER);
		AddActivityToSR("ACT_LOOKBACK_RIGHT", (int)Activity.ACT_LOOKBACK_RIGHT);
		AddActivityToSR("ACT_LOOKBACK_LEFT", (int)Activity.ACT_LOOKBACK_LEFT);
		AddActivityToSR("ACT_COWER", (int)Activity.ACT_COWER);
		AddActivityToSR("ACT_SMALL_FLINCH", (int)Activity.ACT_SMALL_FLINCH);
		AddActivityToSR("ACT_BIG_FLINCH", (int)Activity.ACT_BIG_FLINCH);
		AddActivityToSR("ACT_MELEE_ATTACK1", (int)Activity.ACT_MELEE_ATTACK1);
		AddActivityToSR("ACT_MELEE_ATTACK2", (int)Activity.ACT_MELEE_ATTACK2);
		AddActivityToSR("ACT_RELOAD", (int)Activity.ACT_RELOAD);
		AddActivityToSR("ACT_RELOAD_START", (int)Activity.ACT_RELOAD_START);
		AddActivityToSR("ACT_RELOAD_FINISH", (int)Activity.ACT_RELOAD_FINISH);
		AddActivityToSR("ACT_RELOAD_LOW", (int)Activity.ACT_RELOAD_LOW);
		AddActivityToSR("ACT_ARM", (int)Activity.ACT_ARM);
		AddActivityToSR("ACT_DISARM", (int)Activity.ACT_DISARM);
		AddActivityToSR("ACT_DROP_WEAPON", (int)Activity.ACT_DROP_WEAPON);
		AddActivityToSR("ACT_DROP_WEAPON_SHOTGUN", (int)Activity.ACT_DROP_WEAPON_SHOTGUN);
		AddActivityToSR("ACT_PICKUP_GROUND", (int)Activity.ACT_PICKUP_GROUND);
		AddActivityToSR("ACT_PICKUP_RACK", (int)Activity.ACT_PICKUP_RACK);
		AddActivityToSR("ACT_IDLE_ANGRY", (int)Activity.ACT_IDLE_ANGRY);
		AddActivityToSR("ACT_IDLE_RELAXED", (int)Activity.ACT_IDLE_RELAXED);
		AddActivityToSR("ACT_IDLE_STIMULATED", (int)Activity.ACT_IDLE_STIMULATED);
		AddActivityToSR("ACT_IDLE_AGITATED", (int)Activity.ACT_IDLE_AGITATED);
		AddActivityToSR("ACT_IDLE_STEALTH", (int)Activity.ACT_IDLE_STEALTH);
		AddActivityToSR("ACT_IDLE_HURT", (int)Activity.ACT_IDLE_HURT);
		AddActivityToSR("ACT_WALK_RELAXED", (int)Activity.ACT_WALK_RELAXED);
		AddActivityToSR("ACT_WALK_STIMULATED", (int)Activity.ACT_WALK_STIMULATED);
		AddActivityToSR("ACT_WALK_AGITATED", (int)Activity.ACT_WALK_AGITATED);
		AddActivityToSR("ACT_WALK_STEALTH", (int)Activity.ACT_WALK_STEALTH);
		AddActivityToSR("ACT_RUN_RELAXED", (int)Activity.ACT_RUN_RELAXED);
		AddActivityToSR("ACT_RUN_STIMULATED", (int)Activity.ACT_RUN_STIMULATED);
		AddActivityToSR("ACT_RUN_AGITATED", (int)Activity.ACT_RUN_AGITATED);
		AddActivityToSR("ACT_RUN_STEALTH", (int)Activity.ACT_RUN_STEALTH);
		AddActivityToSR("ACT_IDLE_AIM_RELAXED", (int)Activity.ACT_IDLE_AIM_RELAXED);
		AddActivityToSR("ACT_IDLE_AIM_STIMULATED", (int)Activity.ACT_IDLE_AIM_STIMULATED);
		AddActivityToSR("ACT_IDLE_AIM_AGITATED", (int)Activity.ACT_IDLE_AIM_AGITATED);
		AddActivityToSR("ACT_IDLE_AIM_STEALTH", (int)Activity.ACT_IDLE_AIM_STEALTH);
		AddActivityToSR("ACT_WALK_AIM_RELAXED", (int)Activity.ACT_WALK_AIM_RELAXED);
		AddActivityToSR("ACT_WALK_AIM_STIMULATED", (int)Activity.ACT_WALK_AIM_STIMULATED);
		AddActivityToSR("ACT_WALK_AIM_AGITATED", (int)Activity.ACT_WALK_AIM_AGITATED);
		AddActivityToSR("ACT_WALK_AIM_STEALTH", (int)Activity.ACT_WALK_AIM_STEALTH);
		AddActivityToSR("ACT_RUN_AIM_RELAXED", (int)Activity.ACT_RUN_AIM_RELAXED);
		AddActivityToSR("ACT_RUN_AIM_STIMULATED", (int)Activity.ACT_RUN_AIM_STIMULATED);
		AddActivityToSR("ACT_RUN_AIM_AGITATED", (int)Activity.ACT_RUN_AIM_AGITATED);
		AddActivityToSR("ACT_RUN_AIM_STEALTH", (int)Activity.ACT_RUN_AIM_STEALTH);
		AddActivityToSR("ACT_CROUCHIDLE_STIMULATED", (int)Activity.ACT_CROUCHIDLE_STIMULATED);
		AddActivityToSR("ACT_CROUCHIDLE_AIM_STIMULATED", (int)Activity.ACT_CROUCHIDLE_AIM_STIMULATED);
		AddActivityToSR("ACT_CROUCHIDLE_AGITATED", (int)Activity.ACT_CROUCHIDLE_AGITATED);
		AddActivityToSR("ACT_WALK_HURT", (int)Activity.ACT_WALK_HURT);
		AddActivityToSR("ACT_RUN_HURT", (int)Activity.ACT_RUN_HURT);
		AddActivityToSR("ACT_SPECIAL_ATTACK1", (int)Activity.ACT_SPECIAL_ATTACK1);
		AddActivityToSR("ACT_SPECIAL_ATTACK2", (int)Activity.ACT_SPECIAL_ATTACK2);
		AddActivityToSR("ACT_COMBAT_IDLE", (int)Activity.ACT_COMBAT_IDLE);
		AddActivityToSR("ACT_WALK_SCARED", (int)Activity.ACT_WALK_SCARED);
		AddActivityToSR("ACT_RUN_SCARED", (int)Activity.ACT_RUN_SCARED);
		AddActivityToSR("ACT_VICTORY_DANCE", (int)Activity.ACT_VICTORY_DANCE);
		AddActivityToSR("ACT_DIE_HEADSHOT", (int)Activity.ACT_DIE_HEADSHOT);
		AddActivityToSR("ACT_DIE_CHESTSHOT", (int)Activity.ACT_DIE_CHESTSHOT);
		AddActivityToSR("ACT_DIE_GUTSHOT", (int)Activity.ACT_DIE_GUTSHOT);
		AddActivityToSR("ACT_DIE_BACKSHOT", (int)Activity.ACT_DIE_BACKSHOT);
		AddActivityToSR("ACT_FLINCH_HEAD", (int)Activity.ACT_FLINCH_HEAD);
		AddActivityToSR("ACT_FLINCH_CHEST", (int)Activity.ACT_FLINCH_CHEST);
		AddActivityToSR("ACT_FLINCH_STOMACH", (int)Activity.ACT_FLINCH_STOMACH);
		AddActivityToSR("ACT_FLINCH_LEFTARM", (int)Activity.ACT_FLINCH_LEFTARM);
		AddActivityToSR("ACT_FLINCH_RIGHTARM", (int)Activity.ACT_FLINCH_RIGHTARM);
		AddActivityToSR("ACT_FLINCH_LEFTLEG", (int)Activity.ACT_FLINCH_LEFTLEG);
		AddActivityToSR("ACT_FLINCH_RIGHTLEG", (int)Activity.ACT_FLINCH_RIGHTLEG);
		AddActivityToSR("ACT_FLINCH_PHYSICS", (int)Activity.ACT_FLINCH_PHYSICS);
		AddActivityToSR("ACT_IDLE_ON_FIRE", (int)Activity.ACT_IDLE_ON_FIRE);
		AddActivityToSR("ACT_WALK_ON_FIRE", (int)Activity.ACT_WALK_ON_FIRE);
		AddActivityToSR("ACT_RUN_ON_FIRE", (int)Activity.ACT_RUN_ON_FIRE);
		AddActivityToSR("ACT_RAPPEL_LOOP", (int)Activity.ACT_RAPPEL_LOOP);
		AddActivityToSR("ACT_180_LEFT", (int)Activity.ACT_180_LEFT);
		AddActivityToSR("ACT_180_RIGHT", (int)Activity.ACT_180_RIGHT);
		AddActivityToSR("ACT_90_LEFT", (int)Activity.ACT_90_LEFT);
		AddActivityToSR("ACT_90_RIGHT", (int)Activity.ACT_90_RIGHT);
		AddActivityToSR("ACT_STEP_LEFT", (int)Activity.ACT_STEP_LEFT);
		AddActivityToSR("ACT_STEP_RIGHT", (int)Activity.ACT_STEP_RIGHT);
		AddActivityToSR("ACT_STEP_BACK", (int)Activity.ACT_STEP_BACK);
		AddActivityToSR("ACT_STEP_FORE", (int)Activity.ACT_STEP_FORE);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK1", (int)Activity.ACT_GESTURE_RANGE_ATTACK1);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK2", (int)Activity.ACT_GESTURE_RANGE_ATTACK2);
		AddActivityToSR("ACT_GESTURE_MELEE_ATTACK1", (int)Activity.ACT_GESTURE_MELEE_ATTACK1);
		AddActivityToSR("ACT_GESTURE_MELEE_ATTACK2", (int)Activity.ACT_GESTURE_MELEE_ATTACK2);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK1_LOW", (int)Activity.ACT_GESTURE_RANGE_ATTACK1_LOW);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK2_LOW", (int)Activity.ACT_GESTURE_RANGE_ATTACK2_LOW);
		AddActivityToSR("ACT_MELEE_ATTACK_SWING_GESTURE", (int)Activity.ACT_MELEE_ATTACK_SWING_GESTURE);
		AddActivityToSR("ACT_GESTURE_SMALL_FLINCH", (int)Activity.ACT_GESTURE_SMALL_FLINCH);
		AddActivityToSR("ACT_GESTURE_BIG_FLINCH", (int)Activity.ACT_GESTURE_BIG_FLINCH);
		AddActivityToSR("ACT_GESTURE_FLINCH_BLAST", (int)Activity.ACT_GESTURE_FLINCH_BLAST);
		AddActivityToSR("ACT_GESTURE_FLINCH_BLAST_SHOTGUN", (int)Activity.ACT_GESTURE_FLINCH_BLAST_SHOTGUN);
		AddActivityToSR("ACT_GESTURE_FLINCH_BLAST_DAMAGED", (int)Activity.ACT_GESTURE_FLINCH_BLAST_DAMAGED);
		AddActivityToSR("ACT_GESTURE_FLINCH_BLAST_DAMAGED_SHOTGUN", (int)Activity.ACT_GESTURE_FLINCH_BLAST_DAMAGED_SHOTGUN);
		AddActivityToSR("ACT_GESTURE_FLINCH_HEAD", (int)Activity.ACT_GESTURE_FLINCH_HEAD);
		AddActivityToSR("ACT_GESTURE_FLINCH_CHEST", (int)Activity.ACT_GESTURE_FLINCH_CHEST);
		AddActivityToSR("ACT_GESTURE_FLINCH_STOMACH", (int)Activity.ACT_GESTURE_FLINCH_STOMACH);
		AddActivityToSR("ACT_GESTURE_FLINCH_LEFTARM", (int)Activity.ACT_GESTURE_FLINCH_LEFTARM);
		AddActivityToSR("ACT_GESTURE_FLINCH_RIGHTARM", (int)Activity.ACT_GESTURE_FLINCH_RIGHTARM);
		AddActivityToSR("ACT_GESTURE_FLINCH_LEFTLEG", (int)Activity.ACT_GESTURE_FLINCH_LEFTLEG);
		AddActivityToSR("ACT_GESTURE_FLINCH_RIGHTLEG", (int)Activity.ACT_GESTURE_FLINCH_RIGHTLEG);
		AddActivityToSR("ACT_GESTURE_TURN_LEFT", (int)Activity.ACT_GESTURE_TURN_LEFT);
		AddActivityToSR("ACT_GESTURE_TURN_RIGHT", (int)Activity.ACT_GESTURE_TURN_RIGHT);
		AddActivityToSR("ACT_GESTURE_TURN_LEFT45", (int)Activity.ACT_GESTURE_TURN_LEFT45);
		AddActivityToSR("ACT_GESTURE_TURN_RIGHT45", (int)Activity.ACT_GESTURE_TURN_RIGHT45);
		AddActivityToSR("ACT_GESTURE_TURN_LEFT90", (int)Activity.ACT_GESTURE_TURN_LEFT90);
		AddActivityToSR("ACT_GESTURE_TURN_RIGHT90", (int)Activity.ACT_GESTURE_TURN_RIGHT90);
		AddActivityToSR("ACT_GESTURE_TURN_LEFT45_FLAT", (int)Activity.ACT_GESTURE_TURN_LEFT45_FLAT);
		AddActivityToSR("ACT_GESTURE_TURN_RIGHT45_FLAT", (int)Activity.ACT_GESTURE_TURN_RIGHT45_FLAT);
		AddActivityToSR("ACT_GESTURE_TURN_LEFT90_FLAT", (int)Activity.ACT_GESTURE_TURN_LEFT90_FLAT);
		AddActivityToSR("ACT_GESTURE_TURN_RIGHT90_FLAT", (int)Activity.ACT_GESTURE_TURN_RIGHT90_FLAT);
		AddActivityToSR("ACT_BARNACLE_HIT", (int)Activity.ACT_BARNACLE_HIT);
		AddActivityToSR("ACT_BARNACLE_PULL", (int)Activity.ACT_BARNACLE_PULL);
		AddActivityToSR("ACT_BARNACLE_CHOMP", (int)Activity.ACT_BARNACLE_CHOMP);
		AddActivityToSR("ACT_BARNACLE_CHEW", (int)Activity.ACT_BARNACLE_CHEW);
		AddActivityToSR("ACT_DO_NOT_DISTURB", (int)Activity.ACT_DO_NOT_DISTURB);
		AddActivityToSR("ACT_VM_DRAW", (int)Activity.ACT_VM_DRAW);
		AddActivityToSR("ACT_VM_HOLSTER", (int)Activity.ACT_VM_HOLSTER);
		AddActivityToSR("ACT_VM_IDLE", (int)Activity.ACT_VM_IDLE);
		AddActivityToSR("ACT_VM_FIDGET", (int)Activity.ACT_VM_FIDGET);
		AddActivityToSR("ACT_VM_PULLBACK", (int)Activity.ACT_VM_PULLBACK);
		AddActivityToSR("ACT_VM_PULLBACK_HIGH", (int)Activity.ACT_VM_PULLBACK_HIGH);
		AddActivityToSR("ACT_VM_PULLBACK_LOW", (int)Activity.ACT_VM_PULLBACK_LOW);
		AddActivityToSR("ACT_VM_THROW", (int)Activity.ACT_VM_THROW);
		AddActivityToSR("ACT_VM_PULLPIN", (int)Activity.ACT_VM_PULLPIN);
		AddActivityToSR("ACT_VM_PRIMARYATTACK", (int)Activity.ACT_VM_PRIMARYATTACK);
		AddActivityToSR("ACT_VM_SECONDARYATTACK", (int)Activity.ACT_VM_SECONDARYATTACK);
		AddActivityToSR("ACT_VM_RELOAD", (int)Activity.ACT_VM_RELOAD);
		AddActivityToSR("ACT_VM_DRYFIRE", (int)Activity.ACT_VM_DRYFIRE);
		AddActivityToSR("ACT_VM_HITLEFT", (int)Activity.ACT_VM_HITLEFT);
		AddActivityToSR("ACT_VM_HITLEFT2", (int)Activity.ACT_VM_HITLEFT2);
		AddActivityToSR("ACT_VM_HITRIGHT", (int)Activity.ACT_VM_HITRIGHT);
		AddActivityToSR("ACT_VM_HITRIGHT2", (int)Activity.ACT_VM_HITRIGHT2);
		AddActivityToSR("ACT_VM_HITCENTER", (int)Activity.ACT_VM_HITCENTER);
		AddActivityToSR("ACT_VM_HITCENTER2", (int)Activity.ACT_VM_HITCENTER2);
		AddActivityToSR("ACT_VM_MISSLEFT", (int)Activity.ACT_VM_MISSLEFT);
		AddActivityToSR("ACT_VM_MISSLEFT2", (int)Activity.ACT_VM_MISSLEFT2);
		AddActivityToSR("ACT_VM_MISSRIGHT", (int)Activity.ACT_VM_MISSRIGHT);
		AddActivityToSR("ACT_VM_MISSRIGHT2", (int)Activity.ACT_VM_MISSRIGHT2);
		AddActivityToSR("ACT_VM_MISSCENTER", (int)Activity.ACT_VM_MISSCENTER);
		AddActivityToSR("ACT_VM_MISSCENTER2", (int)Activity.ACT_VM_MISSCENTER2);
		AddActivityToSR("ACT_VM_HAULBACK", (int)Activity.ACT_VM_HAULBACK);
		AddActivityToSR("ACT_VM_SWINGHARD", (int)Activity.ACT_VM_SWINGHARD);
		AddActivityToSR("ACT_VM_SWINGMISS", (int)Activity.ACT_VM_SWINGMISS);
		AddActivityToSR("ACT_VM_SWINGHIT", (int)Activity.ACT_VM_SWINGHIT);
		AddActivityToSR("ACT_VM_IDLE_TO_LOWERED", (int)Activity.ACT_VM_IDLE_TO_LOWERED);
		AddActivityToSR("ACT_VM_IDLE_LOWERED", (int)Activity.ACT_VM_IDLE_LOWERED);
		AddActivityToSR("ACT_VM_LOWERED_TO_IDLE", (int)Activity.ACT_VM_LOWERED_TO_IDLE);
		AddActivityToSR("ACT_VM_RECOIL1", (int)Activity.ACT_VM_RECOIL1);
		AddActivityToSR("ACT_VM_RECOIL2", (int)Activity.ACT_VM_RECOIL2);
		AddActivityToSR("ACT_VM_RECOIL3", (int)Activity.ACT_VM_RECOIL3);
		AddActivityToSR("ACT_VM_PICKUP", (int)Activity.ACT_VM_PICKUP);
		AddActivityToSR("ACT_VM_RELEASE", (int)Activity.ACT_VM_RELEASE);
		AddActivityToSR("ACT_VM_ATTACH_SILENCER", (int)Activity.ACT_VM_ATTACH_SILENCER);
		AddActivityToSR("ACT_VM_DETACH_SILENCER", (int)Activity.ACT_VM_DETACH_SILENCER);
		AddActivityToSR("ACT_SLAM_STICKWALL_IDLE", (int)Activity.ACT_SLAM_STICKWALL_IDLE);
		AddActivityToSR("ACT_SLAM_STICKWALL_ND_IDLE", (int)Activity.ACT_SLAM_STICKWALL_ND_IDLE);
		AddActivityToSR("ACT_SLAM_STICKWALL_ATTACH", (int)Activity.ACT_SLAM_STICKWALL_ATTACH);
		AddActivityToSR("ACT_SLAM_STICKWALL_ATTACH2", (int)Activity.ACT_SLAM_STICKWALL_ATTACH2);
		AddActivityToSR("ACT_SLAM_STICKWALL_ND_ATTACH", (int)Activity.ACT_SLAM_STICKWALL_ND_ATTACH);
		AddActivityToSR("ACT_SLAM_STICKWALL_ND_ATTACH2", (int)Activity.ACT_SLAM_STICKWALL_ND_ATTACH2);
		AddActivityToSR("ACT_SLAM_STICKWALL_DETONATE", (int)Activity.ACT_SLAM_STICKWALL_DETONATE);
		AddActivityToSR("ACT_SLAM_STICKWALL_DETONATOR_HOLSTER", (int)Activity.ACT_SLAM_STICKWALL_DETONATOR_HOLSTER);
		AddActivityToSR("ACT_SLAM_STICKWALL_DRAW", (int)Activity.ACT_SLAM_STICKWALL_DRAW);
		AddActivityToSR("ACT_SLAM_STICKWALL_ND_DRAW", (int)Activity.ACT_SLAM_STICKWALL_ND_DRAW);
		AddActivityToSR("ACT_SLAM_STICKWALL_TO_THROW", (int)Activity.ACT_SLAM_STICKWALL_TO_THROW);
		AddActivityToSR("ACT_SLAM_STICKWALL_TO_THROW_ND", (int)Activity.ACT_SLAM_STICKWALL_TO_THROW_ND);
		AddActivityToSR("ACT_SLAM_STICKWALL_TO_TRIPMINE_ND", (int)Activity.ACT_SLAM_STICKWALL_TO_TRIPMINE_ND);
		AddActivityToSR("ACT_SLAM_THROW_IDLE", (int)Activity.ACT_SLAM_THROW_IDLE);
		AddActivityToSR("ACT_SLAM_THROW_ND_IDLE", (int)Activity.ACT_SLAM_THROW_ND_IDLE);
		AddActivityToSR("ACT_SLAM_THROW_THROW", (int)Activity.ACT_SLAM_THROW_THROW);
		AddActivityToSR("ACT_SLAM_THROW_THROW2", (int)Activity.ACT_SLAM_THROW_THROW2);
		AddActivityToSR("ACT_SLAM_THROW_THROW_ND", (int)Activity.ACT_SLAM_THROW_THROW_ND);
		AddActivityToSR("ACT_SLAM_THROW_THROW_ND2", (int)Activity.ACT_SLAM_THROW_THROW_ND2);
		AddActivityToSR("ACT_SLAM_THROW_DRAW", (int)Activity.ACT_SLAM_THROW_DRAW);
		AddActivityToSR("ACT_SLAM_THROW_ND_DRAW", (int)Activity.ACT_SLAM_THROW_ND_DRAW);
		AddActivityToSR("ACT_SLAM_THROW_TO_STICKWALL", (int)Activity.ACT_SLAM_THROW_TO_STICKWALL);
		AddActivityToSR("ACT_SLAM_THROW_TO_STICKWALL_ND", (int)Activity.ACT_SLAM_THROW_TO_STICKWALL_ND);
		AddActivityToSR("ACT_SLAM_THROW_DETONATE", (int)Activity.ACT_SLAM_THROW_DETONATE);
		AddActivityToSR("ACT_SLAM_THROW_DETONATOR_HOLSTER", (int)Activity.ACT_SLAM_THROW_DETONATOR_HOLSTER);
		AddActivityToSR("ACT_SLAM_THROW_TO_TRIPMINE_ND", (int)Activity.ACT_SLAM_THROW_TO_TRIPMINE_ND);
		AddActivityToSR("ACT_SLAM_TRIPMINE_IDLE", (int)Activity.ACT_SLAM_TRIPMINE_IDLE);
		AddActivityToSR("ACT_SLAM_TRIPMINE_DRAW", (int)Activity.ACT_SLAM_TRIPMINE_DRAW);
		AddActivityToSR("ACT_SLAM_TRIPMINE_ATTACH", (int)Activity.ACT_SLAM_TRIPMINE_ATTACH);
		AddActivityToSR("ACT_SLAM_TRIPMINE_ATTACH2", (int)Activity.ACT_SLAM_TRIPMINE_ATTACH2);
		AddActivityToSR("ACT_SLAM_TRIPMINE_TO_STICKWALL_ND", (int)Activity.ACT_SLAM_TRIPMINE_TO_STICKWALL_ND);
		AddActivityToSR("ACT_SLAM_TRIPMINE_TO_THROW_ND", (int)Activity.ACT_SLAM_TRIPMINE_TO_THROW_ND);
		AddActivityToSR("ACT_SLAM_DETONATOR_IDLE", (int)Activity.ACT_SLAM_DETONATOR_IDLE);
		AddActivityToSR("ACT_SLAM_DETONATOR_DRAW", (int)Activity.ACT_SLAM_DETONATOR_DRAW);
		AddActivityToSR("ACT_SLAM_DETONATOR_DETONATE", (int)Activity.ACT_SLAM_DETONATOR_DETONATE);
		AddActivityToSR("ACT_SLAM_DETONATOR_HOLSTER", (int)Activity.ACT_SLAM_DETONATOR_HOLSTER);
		AddActivityToSR("ACT_SLAM_DETONATOR_STICKWALL_DRAW", (int)Activity.ACT_SLAM_DETONATOR_STICKWALL_DRAW);
		AddActivityToSR("ACT_SLAM_DETONATOR_THROW_DRAW", (int)Activity.ACT_SLAM_DETONATOR_THROW_DRAW);
		AddActivityToSR("ACT_SHOTGUN_RELOAD_START", (int)Activity.ACT_SHOTGUN_RELOAD_START);
		AddActivityToSR("ACT_SHOTGUN_RELOAD_FINISH", (int)Activity.ACT_SHOTGUN_RELOAD_FINISH);
		AddActivityToSR("ACT_SHOTGUN_PUMP", (int)Activity.ACT_SHOTGUN_PUMP);
		AddActivityToSR("ACT_SMG2_IDLE2", (int)Activity.ACT_SMG2_IDLE2);
		AddActivityToSR("ACT_SMG2_FIRE2", (int)Activity.ACT_SMG2_FIRE2);
		AddActivityToSR("ACT_SMG2_DRAW2", (int)Activity.ACT_SMG2_DRAW2);
		AddActivityToSR("ACT_SMG2_RELOAD2", (int)Activity.ACT_SMG2_RELOAD2);
		AddActivityToSR("ACT_SMG2_DRYFIRE2", (int)Activity.ACT_SMG2_DRYFIRE2);
		AddActivityToSR("ACT_SMG2_TOAUTO", (int)Activity.ACT_SMG2_TOAUTO);
		AddActivityToSR("ACT_SMG2_TOBURST", (int)Activity.ACT_SMG2_TOBURST);
		AddActivityToSR("ACT_PHYSCANNON_UPGRADE", (int)Activity.ACT_PHYSCANNON_UPGRADE);
		AddActivityToSR("ACT_RANGE_ATTACK_AR1", (int)Activity.ACT_RANGE_ATTACK_AR1);
		AddActivityToSR("ACT_RANGE_ATTACK_AR2", (int)Activity.ACT_RANGE_ATTACK_AR2);
		AddActivityToSR("ACT_RANGE_ATTACK_AR2_LOW", (int)Activity.ACT_RANGE_ATTACK_AR2_LOW);
		AddActivityToSR("ACT_RANGE_ATTACK_AR2_GRENADE", (int)Activity.ACT_RANGE_ATTACK_AR2_GRENADE);
		AddActivityToSR("ACT_RANGE_ATTACK_HMG1", (int)Activity.ACT_RANGE_ATTACK_HMG1);
		AddActivityToSR("ACT_RANGE_ATTACK_ML", (int)Activity.ACT_RANGE_ATTACK_ML);
		AddActivityToSR("ACT_RANGE_ATTACK_SMG1", (int)Activity.ACT_RANGE_ATTACK_SMG1);
		AddActivityToSR("ACT_RANGE_ATTACK_SMG1_LOW", (int)Activity.ACT_RANGE_ATTACK_SMG1_LOW);
		AddActivityToSR("ACT_RANGE_ATTACK_SMG2", (int)Activity.ACT_RANGE_ATTACK_SMG2);
		AddActivityToSR("ACT_RANGE_ATTACK_SHOTGUN", (int)Activity.ACT_RANGE_ATTACK_SHOTGUN);
		AddActivityToSR("ACT_RANGE_ATTACK_SHOTGUN_LOW", (int)Activity.ACT_RANGE_ATTACK_SHOTGUN_LOW);
		AddActivityToSR("ACT_RANGE_ATTACK_PISTOL", (int)Activity.ACT_RANGE_ATTACK_PISTOL);
		AddActivityToSR("ACT_RANGE_ATTACK_PISTOL_LOW", (int)Activity.ACT_RANGE_ATTACK_PISTOL_LOW);
		AddActivityToSR("ACT_RANGE_ATTACK_SLAM", (int)Activity.ACT_RANGE_ATTACK_SLAM);
		AddActivityToSR("ACT_RANGE_ATTACK_TRIPWIRE", (int)Activity.ACT_RANGE_ATTACK_TRIPWIRE);
		AddActivityToSR("ACT_RANGE_ATTACK_THROW", (int)Activity.ACT_RANGE_ATTACK_THROW);
		AddActivityToSR("ACT_RANGE_ATTACK_SNIPER_RIFLE", (int)Activity.ACT_RANGE_ATTACK_SNIPER_RIFLE);
		AddActivityToSR("ACT_RANGE_ATTACK_RPG", (int)Activity.ACT_RANGE_ATTACK_RPG);
		AddActivityToSR("ACT_MELEE_ATTACK_SWING", (int)Activity.ACT_MELEE_ATTACK_SWING);
		AddActivityToSR("ACT_RANGE_AIM_LOW", (int)Activity.ACT_RANGE_AIM_LOW);
		AddActivityToSR("ACT_RANGE_AIM_SMG1_LOW", (int)Activity.ACT_RANGE_AIM_SMG1_LOW);
		AddActivityToSR("ACT_RANGE_AIM_PISTOL_LOW", (int)Activity.ACT_RANGE_AIM_PISTOL_LOW);
		AddActivityToSR("ACT_RANGE_AIM_AR2_LOW", (int)Activity.ACT_RANGE_AIM_AR2_LOW);
		AddActivityToSR("ACT_COVER_PISTOL_LOW", (int)Activity.ACT_COVER_PISTOL_LOW);
		AddActivityToSR("ACT_COVER_SMG1_LOW", (int)Activity.ACT_COVER_SMG1_LOW);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_AR1", (int)Activity.ACT_GESTURE_RANGE_ATTACK_AR1);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_AR2", (int)Activity.ACT_GESTURE_RANGE_ATTACK_AR2);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_AR2_GRENADE", (int)Activity.ACT_GESTURE_RANGE_ATTACK_AR2_GRENADE);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_HMG1", (int)Activity.ACT_GESTURE_RANGE_ATTACK_HMG1);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_ML", (int)Activity.ACT_GESTURE_RANGE_ATTACK_ML);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_SMG1", (int)Activity.ACT_GESTURE_RANGE_ATTACK_SMG1);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_SMG1_LOW", (int)Activity.ACT_GESTURE_RANGE_ATTACK_SMG1_LOW);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_SMG2", (int)Activity.ACT_GESTURE_RANGE_ATTACK_SMG2);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_SHOTGUN", (int)Activity.ACT_GESTURE_RANGE_ATTACK_SHOTGUN);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_PISTOL", (int)Activity.ACT_GESTURE_RANGE_ATTACK_PISTOL);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_PISTOL_LOW", (int)Activity.ACT_GESTURE_RANGE_ATTACK_PISTOL_LOW);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_SLAM", (int)Activity.ACT_GESTURE_RANGE_ATTACK_SLAM);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_TRIPWIRE", (int)Activity.ACT_GESTURE_RANGE_ATTACK_TRIPWIRE);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_THROW", (int)Activity.ACT_GESTURE_RANGE_ATTACK_THROW);
		AddActivityToSR("ACT_GESTURE_RANGE_ATTACK_SNIPER_RIFLE", (int)Activity.ACT_GESTURE_RANGE_ATTACK_SNIPER_RIFLE);
		AddActivityToSR("ACT_GESTURE_MELEE_ATTACK_SWING", (int)Activity.ACT_GESTURE_MELEE_ATTACK_SWING);
		AddActivityToSR("ACT_IDLE_RIFLE", (int)Activity.ACT_IDLE_RIFLE);
		AddActivityToSR("ACT_IDLE_SMG1", (int)Activity.ACT_IDLE_SMG1);
		AddActivityToSR("ACT_IDLE_ANGRY_SMG1", (int)Activity.ACT_IDLE_ANGRY_SMG1);
		AddActivityToSR("ACT_IDLE_PISTOL", (int)Activity.ACT_IDLE_PISTOL);
		AddActivityToSR("ACT_IDLE_ANGRY_PISTOL", (int)Activity.ACT_IDLE_ANGRY_PISTOL);
		AddActivityToSR("ACT_IDLE_ANGRY_SHOTGUN", (int)Activity.ACT_IDLE_ANGRY_SHOTGUN);
		AddActivityToSR("ACT_IDLE_STEALTH_PISTOL", (int)Activity.ACT_IDLE_STEALTH_PISTOL);
		AddActivityToSR("ACT_IDLE_PACKAGE", (int)Activity.ACT_IDLE_PACKAGE);
		AddActivityToSR("ACT_WALK_PACKAGE", (int)Activity.ACT_WALK_PACKAGE);
		AddActivityToSR("ACT_IDLE_SUITCASE", (int)Activity.ACT_IDLE_SUITCASE);
		AddActivityToSR("ACT_WALK_SUITCASE", (int)Activity.ACT_WALK_SUITCASE);
		AddActivityToSR("ACT_IDLE_SMG1_RELAXED", (int)Activity.ACT_IDLE_SMG1_RELAXED);
		AddActivityToSR("ACT_IDLE_SMG1_STIMULATED", (int)Activity.ACT_IDLE_SMG1_STIMULATED);
		AddActivityToSR("ACT_WALK_RIFLE_RELAXED", (int)Activity.ACT_WALK_RIFLE_RELAXED);
		AddActivityToSR("ACT_RUN_RIFLE_RELAXED", (int)Activity.ACT_RUN_RIFLE_RELAXED);
		AddActivityToSR("ACT_WALK_RIFLE_STIMULATED", (int)Activity.ACT_WALK_RIFLE_STIMULATED);
		AddActivityToSR("ACT_RUN_RIFLE_STIMULATED", (int)Activity.ACT_RUN_RIFLE_STIMULATED);
		AddActivityToSR("ACT_IDLE_AIM_RIFLE_STIMULATED", (int)Activity.ACT_IDLE_AIM_RIFLE_STIMULATED);
		AddActivityToSR("ACT_WALK_AIM_RIFLE_STIMULATED", (int)Activity.ACT_WALK_AIM_RIFLE_STIMULATED);
		AddActivityToSR("ACT_RUN_AIM_RIFLE_STIMULATED", (int)Activity.ACT_RUN_AIM_RIFLE_STIMULATED);
		AddActivityToSR("ACT_IDLE_SHOTGUN_RELAXED", (int)Activity.ACT_IDLE_SHOTGUN_RELAXED);
		AddActivityToSR("ACT_IDLE_SHOTGUN_STIMULATED", (int)Activity.ACT_IDLE_SHOTGUN_STIMULATED);
		AddActivityToSR("ACT_IDLE_SHOTGUN_AGITATED", (int)Activity.ACT_IDLE_SHOTGUN_AGITATED);
		AddActivityToSR("ACT_WALK_ANGRY", (int)Activity.ACT_WALK_ANGRY);
		AddActivityToSR("ACT_POLICE_HARASS1", (int)Activity.ACT_POLICE_HARASS1);
		AddActivityToSR("ACT_POLICE_HARASS2", (int)Activity.ACT_POLICE_HARASS2);
		AddActivityToSR("ACT_IDLE_MANNEDGUN", (int)Activity.ACT_IDLE_MANNEDGUN);
		AddActivityToSR("ACT_IDLE_MELEE", (int)Activity.ACT_IDLE_MELEE);
		AddActivityToSR("ACT_IDLE_ANGRY_MELEE", (int)Activity.ACT_IDLE_ANGRY_MELEE);
		AddActivityToSR("ACT_IDLE_RPG_RELAXED", (int)Activity.ACT_IDLE_RPG_RELAXED);
		AddActivityToSR("ACT_IDLE_RPG", (int)Activity.ACT_IDLE_RPG);
		AddActivityToSR("ACT_IDLE_ANGRY_RPG", (int)Activity.ACT_IDLE_ANGRY_RPG);
		AddActivityToSR("ACT_COVER_LOW_RPG", (int)Activity.ACT_COVER_LOW_RPG);
		AddActivityToSR("ACT_WALK_RPG", (int)Activity.ACT_WALK_RPG);
		AddActivityToSR("ACT_RUN_RPG", (int)Activity.ACT_RUN_RPG);
		AddActivityToSR("ACT_WALK_CROUCH_RPG", (int)Activity.ACT_WALK_CROUCH_RPG);
		AddActivityToSR("ACT_RUN_CROUCH_RPG", (int)Activity.ACT_RUN_CROUCH_RPG);
		AddActivityToSR("ACT_WALK_RPG_RELAXED", (int)Activity.ACT_WALK_RPG_RELAXED);
		AddActivityToSR("ACT_RUN_RPG_RELAXED", (int)Activity.ACT_RUN_RPG_RELAXED);
		AddActivityToSR("ACT_WALK_RIFLE", (int)Activity.ACT_WALK_RIFLE);
		AddActivityToSR("ACT_WALK_AIM_RIFLE", (int)Activity.ACT_WALK_AIM_RIFLE);
		AddActivityToSR("ACT_WALK_CROUCH_RIFLE", (int)Activity.ACT_WALK_CROUCH_RIFLE);
		AddActivityToSR("ACT_WALK_CROUCH_AIM_RIFLE", (int)Activity.ACT_WALK_CROUCH_AIM_RIFLE);
		AddActivityToSR("ACT_RUN_RIFLE", (int)Activity.ACT_RUN_RIFLE);
		AddActivityToSR("ACT_RUN_AIM_RIFLE", (int)Activity.ACT_RUN_AIM_RIFLE);
		AddActivityToSR("ACT_RUN_CROUCH_RIFLE", (int)Activity.ACT_RUN_CROUCH_RIFLE);
		AddActivityToSR("ACT_RUN_CROUCH_AIM_RIFLE", (int)Activity.ACT_RUN_CROUCH_AIM_RIFLE);
		AddActivityToSR("ACT_RUN_STEALTH_PISTOL", (int)Activity.ACT_RUN_STEALTH_PISTOL);
		AddActivityToSR("ACT_WALK_AIM_SHOTGUN", (int)Activity.ACT_WALK_AIM_SHOTGUN);
		AddActivityToSR("ACT_RUN_AIM_SHOTGUN", (int)Activity.ACT_RUN_AIM_SHOTGUN);
		AddActivityToSR("ACT_WALK_PISTOL", (int)Activity.ACT_WALK_PISTOL);
		AddActivityToSR("ACT_RUN_PISTOL", (int)Activity.ACT_RUN_PISTOL);
		AddActivityToSR("ACT_WALK_AIM_PISTOL", (int)Activity.ACT_WALK_AIM_PISTOL);
		AddActivityToSR("ACT_RUN_AIM_PISTOL", (int)Activity.ACT_RUN_AIM_PISTOL);
		AddActivityToSR("ACT_WALK_STEALTH_PISTOL", (int)Activity.ACT_WALK_STEALTH_PISTOL);
		AddActivityToSR("ACT_WALK_AIM_STEALTH_PISTOL", (int)Activity.ACT_WALK_AIM_STEALTH_PISTOL);
		AddActivityToSR("ACT_RUN_AIM_STEALTH_PISTOL", (int)Activity.ACT_RUN_AIM_STEALTH_PISTOL);
		AddActivityToSR("ACT_RELOAD_PISTOL", (int)Activity.ACT_RELOAD_PISTOL);
		AddActivityToSR("ACT_RELOAD_PISTOL_LOW", (int)Activity.ACT_RELOAD_PISTOL_LOW);
		AddActivityToSR("ACT_RELOAD_SMG1", (int)Activity.ACT_RELOAD_SMG1);
		AddActivityToSR("ACT_RELOAD_SMG1_LOW", (int)Activity.ACT_RELOAD_SMG1_LOW);
		AddActivityToSR("ACT_RELOAD_SHOTGUN", (int)Activity.ACT_RELOAD_SHOTGUN);
		AddActivityToSR("ACT_RELOAD_SHOTGUN_LOW", (int)Activity.ACT_RELOAD_SHOTGUN_LOW);
		AddActivityToSR("ACT_GESTURE_RELOAD", (int)Activity.ACT_GESTURE_RELOAD);
		AddActivityToSR("ACT_GESTURE_RELOAD_PISTOL", (int)Activity.ACT_GESTURE_RELOAD_PISTOL);
		AddActivityToSR("ACT_GESTURE_RELOAD_SMG1", (int)Activity.ACT_GESTURE_RELOAD_SMG1);
		AddActivityToSR("ACT_GESTURE_RELOAD_SHOTGUN", (int)Activity.ACT_GESTURE_RELOAD_SHOTGUN);
		AddActivityToSR("ACT_BUSY_LEAN_LEFT", (int)Activity.ACT_BUSY_LEAN_LEFT);
		AddActivityToSR("ACT_BUSY_LEAN_LEFT_ENTRY", (int)Activity.ACT_BUSY_LEAN_LEFT_ENTRY);
		AddActivityToSR("ACT_BUSY_LEAN_LEFT_EXIT", (int)Activity.ACT_BUSY_LEAN_LEFT_EXIT);
		AddActivityToSR("ACT_BUSY_LEAN_BACK", (int)Activity.ACT_BUSY_LEAN_BACK);
		AddActivityToSR("ACT_BUSY_LEAN_BACK_ENTRY", (int)Activity.ACT_BUSY_LEAN_BACK_ENTRY);
		AddActivityToSR("ACT_BUSY_LEAN_BACK_EXIT", (int)Activity.ACT_BUSY_LEAN_BACK_EXIT);
		AddActivityToSR("ACT_BUSY_SIT_GROUND", (int)Activity.ACT_BUSY_SIT_GROUND);
		AddActivityToSR("ACT_BUSY_SIT_GROUND_ENTRY", (int)Activity.ACT_BUSY_SIT_GROUND_ENTRY);
		AddActivityToSR("ACT_BUSY_SIT_GROUND_EXIT", (int)Activity.ACT_BUSY_SIT_GROUND_EXIT);
		AddActivityToSR("ACT_BUSY_SIT_CHAIR", (int)Activity.ACT_BUSY_SIT_CHAIR);
		AddActivityToSR("ACT_BUSY_SIT_CHAIR_ENTRY", (int)Activity.ACT_BUSY_SIT_CHAIR_ENTRY);
		AddActivityToSR("ACT_BUSY_SIT_CHAIR_EXIT", (int)Activity.ACT_BUSY_SIT_CHAIR_EXIT);
		AddActivityToSR("ACT_BUSY_STAND", (int)Activity.ACT_BUSY_STAND);
		AddActivityToSR("ACT_BUSY_QUEUE", (int)Activity.ACT_BUSY_QUEUE);
		AddActivityToSR("ACT_DUCK_DODGE", (int)Activity.ACT_DUCK_DODGE);
		AddActivityToSR("ACT_DIE_BARNACLE_SWALLOW", (int)Activity.ACT_DIE_BARNACLE_SWALLOW);
		AddActivityToSR("ACT_GESTURE_BARNACLE_STRANGLE", (int)Activity.ACT_GESTURE_BARNACLE_STRANGLE);
		AddActivityToSR("ACT_PHYSCANNON_DETACH", (int)Activity.ACT_PHYSCANNON_DETACH);
		AddActivityToSR("ACT_PHYSCANNON_ANIMATE", (int)Activity.ACT_PHYSCANNON_ANIMATE);
		AddActivityToSR("ACT_PHYSCANNON_ANIMATE_PRE", (int)Activity.ACT_PHYSCANNON_ANIMATE_PRE);
		AddActivityToSR("ACT_PHYSCANNON_ANIMATE_POST", (int)Activity.ACT_PHYSCANNON_ANIMATE_POST);
		AddActivityToSR("ACT_DIE_FRONTSIDE", (int)Activity.ACT_DIE_FRONTSIDE);
		AddActivityToSR("ACT_DIE_RIGHTSIDE", (int)Activity.ACT_DIE_RIGHTSIDE);
		AddActivityToSR("ACT_DIE_BACKSIDE", (int)Activity.ACT_DIE_BACKSIDE);
		AddActivityToSR("ACT_DIE_LEFTSIDE", (int)Activity.ACT_DIE_LEFTSIDE);
		AddActivityToSR("ACT_OPEN_DOOR", (int)Activity.ACT_OPEN_DOOR);
		AddActivityToSR("ACT_DI_ALYX_ZOMBIE_MELEE", (int)Activity.ACT_DI_ALYX_ZOMBIE_MELEE);
		AddActivityToSR("ACT_DI_ALYX_ZOMBIE_TORSO_MELEE", (int)Activity.ACT_DI_ALYX_ZOMBIE_TORSO_MELEE);
		AddActivityToSR("ACT_DI_ALYX_HEADCRAB_MELEE", (int)Activity.ACT_DI_ALYX_HEADCRAB_MELEE);
		AddActivityToSR("ACT_DI_ALYX_ANTLION", (int)Activity.ACT_DI_ALYX_ANTLION);
		AddActivityToSR("ACT_DI_ALYX_ZOMBIE_SHOTGUN64", (int)Activity.ACT_DI_ALYX_ZOMBIE_SHOTGUN64);
		AddActivityToSR("ACT_DI_ALYX_ZOMBIE_SHOTGUN26", (int)Activity.ACT_DI_ALYX_ZOMBIE_SHOTGUN26);
		AddActivityToSR("ACT_READINESS_RELAXED_TO_STIMULATED", (int)Activity.ACT_READINESS_RELAXED_TO_STIMULATED);
		AddActivityToSR("ACT_READINESS_RELAXED_TO_STIMULATED_WALK", (int)Activity.ACT_READINESS_RELAXED_TO_STIMULATED_WALK);
		AddActivityToSR("ACT_READINESS_AGITATED_TO_STIMULATED", (int)Activity.ACT_READINESS_AGITATED_TO_STIMULATED);
		AddActivityToSR("ACT_READINESS_STIMULATED_TO_RELAXED", (int)Activity.ACT_READINESS_STIMULATED_TO_RELAXED);
		AddActivityToSR("ACT_READINESS_PISTOL_RELAXED_TO_STIMULATED", (int)Activity.ACT_READINESS_PISTOL_RELAXED_TO_STIMULATED);
		AddActivityToSR("ACT_READINESS_PISTOL_RELAXED_TO_STIMULATED_WALK", (int)Activity.ACT_READINESS_PISTOL_RELAXED_TO_STIMULATED_WALK);
		AddActivityToSR("ACT_READINESS_PISTOL_AGITATED_TO_STIMULATED", (int)Activity.ACT_READINESS_PISTOL_AGITATED_TO_STIMULATED);
		AddActivityToSR("ACT_READINESS_PISTOL_STIMULATED_TO_RELAXED", (int)Activity.ACT_READINESS_PISTOL_STIMULATED_TO_RELAXED);
		AddActivityToSR("ACT_IDLE_CARRY", (int)Activity.ACT_IDLE_CARRY);
		AddActivityToSR("ACT_WALK_CARRY", (int)Activity.ACT_WALK_CARRY);
		AddActivityToSR("ACT_STARTDYING", (int)Activity.ACT_STARTDYING);
		AddActivityToSR("ACT_DYINGLOOP", (int)Activity.ACT_DYINGLOOP);
		AddActivityToSR("ACT_DYINGTODEAD", (int)Activity.ACT_DYINGTODEAD);
		AddActivityToSR("ACT_RIDE_MANNED_GUN", (int)Activity.ACT_RIDE_MANNED_GUN);
		AddActivityToSR("ACT_VM_SPRINT_ENTER", (int)Activity.ACT_VM_SPRINT_ENTER);
		AddActivityToSR("ACT_VM_SPRINT_IDLE", (int)Activity.ACT_VM_SPRINT_IDLE);
		AddActivityToSR("ACT_VM_SPRINT_LEAVE", (int)Activity.ACT_VM_SPRINT_LEAVE);
		AddActivityToSR("ACT_FIRE_START", (int)Activity.ACT_FIRE_START);
		AddActivityToSR("ACT_FIRE_LOOP", (int)Activity.ACT_FIRE_LOOP);
		AddActivityToSR("ACT_FIRE_END", (int)Activity.ACT_FIRE_END);
		AddActivityToSR("ACT_CROUCHING_GRENADEIDLE", (int)Activity.ACT_CROUCHING_GRENADEIDLE);
		AddActivityToSR("ACT_CROUCHING_GRENADEREADY", (int)Activity.ACT_CROUCHING_GRENADEREADY);
		AddActivityToSR("ACT_CROUCHING_PRIMARYATTACK", (int)Activity.ACT_CROUCHING_PRIMARYATTACK);
		AddActivityToSR("ACT_OVERLAY_GRENADEIDLE", (int)Activity.ACT_OVERLAY_GRENADEIDLE);
		AddActivityToSR("ACT_OVERLAY_GRENADEREADY", (int)Activity.ACT_OVERLAY_GRENADEREADY);
		AddActivityToSR("ACT_OVERLAY_PRIMARYATTACK", (int)Activity.ACT_OVERLAY_PRIMARYATTACK);
		AddActivityToSR("ACT_OVERLAY_SHIELD_UP", (int)Activity.ACT_OVERLAY_SHIELD_UP);
		AddActivityToSR("ACT_OVERLAY_SHIELD_DOWN", (int)Activity.ACT_OVERLAY_SHIELD_DOWN);
		AddActivityToSR("ACT_OVERLAY_SHIELD_UP_IDLE", (int)Activity.ACT_OVERLAY_SHIELD_UP_IDLE);
		AddActivityToSR("ACT_OVERLAY_SHIELD_ATTACK", (int)Activity.ACT_OVERLAY_SHIELD_ATTACK);
		AddActivityToSR("ACT_OVERLAY_SHIELD_KNOCKBACK", (int)Activity.ACT_OVERLAY_SHIELD_KNOCKBACK);
		AddActivityToSR("ACT_SHIELD_UP", (int)Activity.ACT_SHIELD_UP);
		AddActivityToSR("ACT_SHIELD_DOWN", (int)Activity.ACT_SHIELD_DOWN);
		AddActivityToSR("ACT_SHIELD_UP_IDLE", (int)Activity.ACT_SHIELD_UP_IDLE);
		AddActivityToSR("ACT_SHIELD_ATTACK", (int)Activity.ACT_SHIELD_ATTACK);
		AddActivityToSR("ACT_SHIELD_KNOCKBACK", (int)Activity.ACT_SHIELD_KNOCKBACK);
		AddActivityToSR("ACT_CROUCHING_SHIELD_UP", (int)Activity.ACT_CROUCHING_SHIELD_UP);
		AddActivityToSR("ACT_CROUCHING_SHIELD_DOWN", (int)Activity.ACT_CROUCHING_SHIELD_DOWN);
		AddActivityToSR("ACT_CROUCHING_SHIELD_UP_IDLE", (int)Activity.ACT_CROUCHING_SHIELD_UP_IDLE);
		AddActivityToSR("ACT_CROUCHING_SHIELD_ATTACK", (int)Activity.ACT_CROUCHING_SHIELD_ATTACK);
		AddActivityToSR("ACT_CROUCHING_SHIELD_KNOCKBACK", (int)Activity.ACT_CROUCHING_SHIELD_KNOCKBACK);
		AddActivityToSR("ACT_TURNRIGHT45", (int)Activity.ACT_TURNRIGHT45);
		AddActivityToSR("ACT_TURNLEFT45", (int)Activity.ACT_TURNLEFT45);
		AddActivityToSR("ACT_TURN", (int)Activity.ACT_TURN);
		AddActivityToSR("ACT_OBJ_ASSEMBLING", (int)Activity.ACT_OBJ_ASSEMBLING);
		AddActivityToSR("ACT_OBJ_DISMANTLING", (int)Activity.ACT_OBJ_DISMANTLING);
		AddActivityToSR("ACT_OBJ_STARTUP", (int)Activity.ACT_OBJ_STARTUP);
		AddActivityToSR("ACT_OBJ_RUNNING", (int)Activity.ACT_OBJ_RUNNING);
		AddActivityToSR("ACT_OBJ_IDLE", (int)Activity.ACT_OBJ_IDLE);
		AddActivityToSR("ACT_OBJ_PLACING", (int)Activity.ACT_OBJ_PLACING);
		AddActivityToSR("ACT_OBJ_DETERIORATING", (int)Activity.ACT_OBJ_DETERIORATING);
		AddActivityToSR("ACT_OBJ_UPGRADING", (int)Activity.ACT_OBJ_UPGRADING);
		AddActivityToSR("ACT_DEPLOY", (int)Activity.ACT_DEPLOY);
		AddActivityToSR("ACT_DEPLOY_IDLE", (int)Activity.ACT_DEPLOY_IDLE);
		AddActivityToSR("ACT_UNDEPLOY", (int)Activity.ACT_UNDEPLOY);
		AddActivityToSR("ACT_GRENADE_ROLL", (int)Activity.ACT_GRENADE_ROLL);
		AddActivityToSR("ACT_GRENADE_TOSS", (int)Activity.ACT_GRENADE_TOSS);
		AddActivityToSR("ACT_HANDGRENADE_THROW1", (int)Activity.ACT_HANDGRENADE_THROW1);
		AddActivityToSR("ACT_HANDGRENADE_THROW2", (int)Activity.ACT_HANDGRENADE_THROW2);
		AddActivityToSR("ACT_HANDGRENADE_THROW3", (int)Activity.ACT_HANDGRENADE_THROW3);
		AddActivityToSR("ACT_SHOTGUN_IDLE_DEEP", (int)Activity.ACT_SHOTGUN_IDLE_DEEP);
		AddActivityToSR("ACT_SHOTGUN_IDLE4", (int)Activity.ACT_SHOTGUN_IDLE4);
		AddActivityToSR("ACT_GLOCK_SHOOTEMPTY", (int)Activity.ACT_GLOCK_SHOOTEMPTY);
		AddActivityToSR("ACT_GLOCK_SHOOT_RELOAD", (int)Activity.ACT_GLOCK_SHOOT_RELOAD);
		AddActivityToSR("ACT_RPG_DRAW_UNLOADED", (int)Activity.ACT_RPG_DRAW_UNLOADED);
		AddActivityToSR("ACT_RPG_HOLSTER_UNLOADED", (int)Activity.ACT_RPG_HOLSTER_UNLOADED);
		AddActivityToSR("ACT_RPG_IDLE_UNLOADED", (int)Activity.ACT_RPG_IDLE_UNLOADED);
		AddActivityToSR("ACT_RPG_FIDGET_UNLOADED", (int)Activity.ACT_RPG_FIDGET_UNLOADED);
		AddActivityToSR("ACT_CROSSBOW_DRAW_UNLOADED", (int)Activity.ACT_CROSSBOW_DRAW_UNLOADED);
		AddActivityToSR("ACT_CROSSBOW_IDLE_UNLOADED", (int)Activity.ACT_CROSSBOW_IDLE_UNLOADED);
		AddActivityToSR("ACT_CROSSBOW_FIDGET_UNLOADED", (int)Activity.ACT_CROSSBOW_FIDGET_UNLOADED);
		AddActivityToSR("ACT_GAUSS_SPINUP", (int)Activity.ACT_GAUSS_SPINUP);
		AddActivityToSR("ACT_GAUSS_SPINCYCLE", (int)Activity.ACT_GAUSS_SPINCYCLE);
		AddActivityToSR("ACT_TRIPMINE_GROUND", (int)Activity.ACT_TRIPMINE_GROUND);
		AddActivityToSR("ACT_TRIPMINE_WORLD", (int)Activity.ACT_TRIPMINE_WORLD);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_SILENCED", (int)Activity.ACT_VM_PRIMARYATTACK_SILENCED);
		AddActivityToSR("ACT_VM_RELOAD_SILENCED", (int)Activity.ACT_VM_RELOAD_SILENCED);
		AddActivityToSR("ACT_VM_DRYFIRE_SILENCED", (int)Activity.ACT_VM_DRYFIRE_SILENCED);
		AddActivityToSR("ACT_VM_IDLE_SILENCED", (int)Activity.ACT_VM_IDLE_SILENCED);
		AddActivityToSR("ACT_VM_DRAW_SILENCED", (int)Activity.ACT_VM_DRAW_SILENCED);
		AddActivityToSR("ACT_VM_IDLE_EMPTY_LEFT", (int)Activity.ACT_VM_IDLE_EMPTY_LEFT);
		AddActivityToSR("ACT_VM_DRYFIRE_LEFT", (int)Activity.ACT_VM_DRYFIRE_LEFT);
		AddActivityToSR("ACT_PLAYER_IDLE_FIRE", (int)Activity.ACT_PLAYER_IDLE_FIRE);
		AddActivityToSR("ACT_PLAYER_CROUCH_FIRE", (int)Activity.ACT_PLAYER_CROUCH_FIRE);
		AddActivityToSR("ACT_PLAYER_CROUCH_WALK_FIRE", (int)Activity.ACT_PLAYER_CROUCH_WALK_FIRE);
		AddActivityToSR("ACT_PLAYER_WALK_FIRE", (int)Activity.ACT_PLAYER_WALK_FIRE);
		AddActivityToSR("ACT_PLAYER_RUN_FIRE", (int)Activity.ACT_PLAYER_RUN_FIRE);
		AddActivityToSR("ACT_IDLETORUN", (int)Activity.ACT_IDLETORUN);
		AddActivityToSR("ACT_RUNTOIDLE", (int)Activity.ACT_RUNTOIDLE);
		AddActivityToSR("ACT_SPRINT", (int)Activity.ACT_SPRINT);
		AddActivityToSR("ACT_GET_DOWN_STAND", (int)Activity.ACT_GET_DOWN_STAND);
		AddActivityToSR("ACT_GET_UP_STAND", (int)Activity.ACT_GET_UP_STAND);
		AddActivityToSR("ACT_GET_DOWN_CROUCH", (int)Activity.ACT_GET_DOWN_CROUCH);
		AddActivityToSR("ACT_GET_UP_CROUCH", (int)Activity.ACT_GET_UP_CROUCH);
		AddActivityToSR("ACT_PRONE_FORWARD", (int)Activity.ACT_PRONE_FORWARD);
		AddActivityToSR("ACT_PRONE_IDLE", (int)Activity.ACT_PRONE_IDLE);
		AddActivityToSR("ACT_DEEPIDLE1", (int)Activity.ACT_DEEPIDLE1);
		AddActivityToSR("ACT_DEEPIDLE2", (int)Activity.ACT_DEEPIDLE2);
		AddActivityToSR("ACT_DEEPIDLE3", (int)Activity.ACT_DEEPIDLE3);
		AddActivityToSR("ACT_DEEPIDLE4", (int)Activity.ACT_DEEPIDLE4);
		AddActivityToSR("ACT_VM_RELOAD_DEPLOYED", (int)Activity.ACT_VM_RELOAD_DEPLOYED);
		AddActivityToSR("ACT_VM_RELOAD_IDLE", (int)Activity.ACT_VM_RELOAD_IDLE);
		AddActivityToSR("ACT_VM_DRAW_DEPLOYED", (int)Activity.ACT_VM_DRAW_DEPLOYED);
		AddActivityToSR("ACT_VM_DRAW_EMPTY", (int)Activity.ACT_VM_DRAW_EMPTY);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_EMPTY", (int)Activity.ACT_VM_PRIMARYATTACK_EMPTY);
		AddActivityToSR("ACT_VM_RELOAD_EMPTY", (int)Activity.ACT_VM_RELOAD_EMPTY);
		AddActivityToSR("ACT_VM_IDLE_EMPTY", (int)Activity.ACT_VM_IDLE_EMPTY);
		AddActivityToSR("ACT_VM_IDLE_DEPLOYED_EMPTY", (int)Activity.ACT_VM_IDLE_DEPLOYED_EMPTY);
		AddActivityToSR("ACT_VM_IDLE_8", (int)Activity.ACT_VM_IDLE_8);
		AddActivityToSR("ACT_VM_IDLE_7", (int)Activity.ACT_VM_IDLE_7);
		AddActivityToSR("ACT_VM_IDLE_6", (int)Activity.ACT_VM_IDLE_6);
		AddActivityToSR("ACT_VM_IDLE_5", (int)Activity.ACT_VM_IDLE_5);
		AddActivityToSR("ACT_VM_IDLE_4", (int)Activity.ACT_VM_IDLE_4);
		AddActivityToSR("ACT_VM_IDLE_3", (int)Activity.ACT_VM_IDLE_3);
		AddActivityToSR("ACT_VM_IDLE_2", (int)Activity.ACT_VM_IDLE_2);
		AddActivityToSR("ACT_VM_IDLE_1", (int)Activity.ACT_VM_IDLE_1);
		AddActivityToSR("ACT_VM_IDLE_DEPLOYED", (int)Activity.ACT_VM_IDLE_DEPLOYED);
		AddActivityToSR("ACT_VM_IDLE_DEPLOYED_8", (int)Activity.ACT_VM_IDLE_DEPLOYED_8);
		AddActivityToSR("ACT_VM_IDLE_DEPLOYED_7", (int)Activity.ACT_VM_IDLE_DEPLOYED_7);
		AddActivityToSR("ACT_VM_IDLE_DEPLOYED_6", (int)Activity.ACT_VM_IDLE_DEPLOYED_6);
		AddActivityToSR("ACT_VM_IDLE_DEPLOYED_5", (int)Activity.ACT_VM_IDLE_DEPLOYED_5);
		AddActivityToSR("ACT_VM_IDLE_DEPLOYED_4", (int)Activity.ACT_VM_IDLE_DEPLOYED_4);
		AddActivityToSR("ACT_VM_IDLE_DEPLOYED_3", (int)Activity.ACT_VM_IDLE_DEPLOYED_3);
		AddActivityToSR("ACT_VM_IDLE_DEPLOYED_2", (int)Activity.ACT_VM_IDLE_DEPLOYED_2);
		AddActivityToSR("ACT_VM_IDLE_DEPLOYED_1", (int)Activity.ACT_VM_IDLE_DEPLOYED_1);
		AddActivityToSR("ACT_VM_UNDEPLOY", (int)Activity.ACT_VM_UNDEPLOY);
		AddActivityToSR("ACT_VM_UNDEPLOY_8", (int)Activity.ACT_VM_UNDEPLOY_8);
		AddActivityToSR("ACT_VM_UNDEPLOY_7", (int)Activity.ACT_VM_UNDEPLOY_7);
		AddActivityToSR("ACT_VM_UNDEPLOY_6", (int)Activity.ACT_VM_UNDEPLOY_6);
		AddActivityToSR("ACT_VM_UNDEPLOY_5", (int)Activity.ACT_VM_UNDEPLOY_5);
		AddActivityToSR("ACT_VM_UNDEPLOY_4", (int)Activity.ACT_VM_UNDEPLOY_4);
		AddActivityToSR("ACT_VM_UNDEPLOY_3", (int)Activity.ACT_VM_UNDEPLOY_3);
		AddActivityToSR("ACT_VM_UNDEPLOY_2", (int)Activity.ACT_VM_UNDEPLOY_2);
		AddActivityToSR("ACT_VM_UNDEPLOY_1", (int)Activity.ACT_VM_UNDEPLOY_1);
		AddActivityToSR("ACT_VM_UNDEPLOY_EMPTY", (int)Activity.ACT_VM_UNDEPLOY_EMPTY);
		AddActivityToSR("ACT_VM_DEPLOY", (int)Activity.ACT_VM_DEPLOY);
		AddActivityToSR("ACT_VM_DEPLOY_8", (int)Activity.ACT_VM_DEPLOY_8);
		AddActivityToSR("ACT_VM_DEPLOY_7", (int)Activity.ACT_VM_DEPLOY_7);
		AddActivityToSR("ACT_VM_DEPLOY_6", (int)Activity.ACT_VM_DEPLOY_6);
		AddActivityToSR("ACT_VM_DEPLOY_5", (int)Activity.ACT_VM_DEPLOY_5);
		AddActivityToSR("ACT_VM_DEPLOY_4", (int)Activity.ACT_VM_DEPLOY_4);
		AddActivityToSR("ACT_VM_DEPLOY_3", (int)Activity.ACT_VM_DEPLOY_3);
		AddActivityToSR("ACT_VM_DEPLOY_2", (int)Activity.ACT_VM_DEPLOY_2);
		AddActivityToSR("ACT_VM_DEPLOY_1", (int)Activity.ACT_VM_DEPLOY_1);
		AddActivityToSR("ACT_VM_DEPLOY_EMPTY", (int)Activity.ACT_VM_DEPLOY_EMPTY);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_8", (int)Activity.ACT_VM_PRIMARYATTACK_8);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_7", (int)Activity.ACT_VM_PRIMARYATTACK_7);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_6", (int)Activity.ACT_VM_PRIMARYATTACK_6);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_5", (int)Activity.ACT_VM_PRIMARYATTACK_5);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_4", (int)Activity.ACT_VM_PRIMARYATTACK_4);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_3", (int)Activity.ACT_VM_PRIMARYATTACK_3);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_2", (int)Activity.ACT_VM_PRIMARYATTACK_2);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_1", (int)Activity.ACT_VM_PRIMARYATTACK_1);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_DEPLOYED", (int)Activity.ACT_VM_PRIMARYATTACK_DEPLOYED);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_DEPLOYED_8", (int)Activity.ACT_VM_PRIMARYATTACK_DEPLOYED_8);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_DEPLOYED_7", (int)Activity.ACT_VM_PRIMARYATTACK_DEPLOYED_7);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_DEPLOYED_6", (int)Activity.ACT_VM_PRIMARYATTACK_DEPLOYED_6);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_DEPLOYED_5", (int)Activity.ACT_VM_PRIMARYATTACK_DEPLOYED_5);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_DEPLOYED_4", (int)Activity.ACT_VM_PRIMARYATTACK_DEPLOYED_4);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_DEPLOYED_3", (int)Activity.ACT_VM_PRIMARYATTACK_DEPLOYED_3);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_DEPLOYED_2", (int)Activity.ACT_VM_PRIMARYATTACK_DEPLOYED_2);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_DEPLOYED_1", (int)Activity.ACT_VM_PRIMARYATTACK_DEPLOYED_1);
		AddActivityToSR("ACT_VM_PRIMARYATTACK_DEPLOYED_EMPTY", (int)Activity.ACT_VM_PRIMARYATTACK_DEPLOYED_EMPTY);
		AddActivityToSR("ACT_DOD_DEPLOYED", (int)Activity.ACT_DOD_DEPLOYED);
		AddActivityToSR("ACT_DOD_PRONE_DEPLOYED", (int)Activity.ACT_DOD_PRONE_DEPLOYED);
		AddActivityToSR("ACT_DOD_IDLE_ZOOMED", (int)Activity.ACT_DOD_IDLE_ZOOMED);
		AddActivityToSR("ACT_DOD_WALK_ZOOMED", (int)Activity.ACT_DOD_WALK_ZOOMED);
		AddActivityToSR("ACT_DOD_CROUCH_ZOOMED", (int)Activity.ACT_DOD_CROUCH_ZOOMED);
		AddActivityToSR("ACT_DOD_CROUCHWALK_ZOOMED", (int)Activity.ACT_DOD_CROUCHWALK_ZOOMED);
		AddActivityToSR("ACT_DOD_PRONE_ZOOMED", (int)Activity.ACT_DOD_PRONE_ZOOMED);
		AddActivityToSR("ACT_DOD_PRONE_FORWARD_ZOOMED", (int)Activity.ACT_DOD_PRONE_FORWARD_ZOOMED);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_DEPLOYED", (int)Activity.ACT_DOD_PRIMARYATTACK_DEPLOYED);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_DEPLOYED", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_DEPLOYED);
		AddActivityToSR("ACT_DOD_RELOAD_DEPLOYED", (int)Activity.ACT_DOD_RELOAD_DEPLOYED);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_DEPLOYED", (int)Activity.ACT_DOD_RELOAD_PRONE_DEPLOYED);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_PRONE", (int)Activity.ACT_DOD_SECONDARYATTACK_PRONE);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH", (int)Activity.ACT_DOD_RELOAD_CROUCH);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE", (int)Activity.ACT_DOD_RELOAD_PRONE);
		AddActivityToSR("ACT_DOD_STAND_IDLE", (int)Activity.ACT_DOD_STAND_IDLE);
		AddActivityToSR("ACT_DOD_STAND_AIM", (int)Activity.ACT_DOD_STAND_AIM);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE", (int)Activity.ACT_DOD_CROUCH_IDLE);
		AddActivityToSR("ACT_DOD_CROUCH_AIM", (int)Activity.ACT_DOD_CROUCH_AIM);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE", (int)Activity.ACT_DOD_CROUCHWALK_IDLE);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM", (int)Activity.ACT_DOD_CROUCHWALK_AIM);
		AddActivityToSR("ACT_DOD_WALK_IDLE", (int)Activity.ACT_DOD_WALK_IDLE);
		AddActivityToSR("ACT_DOD_WALK_AIM", (int)Activity.ACT_DOD_WALK_AIM);
		AddActivityToSR("ACT_DOD_RUN_IDLE", (int)Activity.ACT_DOD_RUN_IDLE);
		AddActivityToSR("ACT_DOD_RUN_AIM", (int)Activity.ACT_DOD_RUN_AIM);
		AddActivityToSR("ACT_DOD_STAND_AIM_PISTOL", (int)Activity.ACT_DOD_STAND_AIM_PISTOL);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_PISTOL", (int)Activity.ACT_DOD_CROUCH_AIM_PISTOL);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_PISTOL", (int)Activity.ACT_DOD_CROUCHWALK_AIM_PISTOL);
		AddActivityToSR("ACT_DOD_WALK_AIM_PISTOL", (int)Activity.ACT_DOD_WALK_AIM_PISTOL);
		AddActivityToSR("ACT_DOD_RUN_AIM_PISTOL", (int)Activity.ACT_DOD_RUN_AIM_PISTOL);
		AddActivityToSR("ACT_DOD_PRONE_AIM_PISTOL", (int)Activity.ACT_DOD_PRONE_AIM_PISTOL);
		AddActivityToSR("ACT_DOD_STAND_IDLE_PISTOL", (int)Activity.ACT_DOD_STAND_IDLE_PISTOL);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_PISTOL", (int)Activity.ACT_DOD_CROUCH_IDLE_PISTOL);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_PISTOL", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_PISTOL);
		AddActivityToSR("ACT_DOD_WALK_IDLE_PISTOL", (int)Activity.ACT_DOD_WALK_IDLE_PISTOL);
		AddActivityToSR("ACT_DOD_RUN_IDLE_PISTOL", (int)Activity.ACT_DOD_RUN_IDLE_PISTOL);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_PISTOL", (int)Activity.ACT_DOD_SPRINT_IDLE_PISTOL);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_PISTOL", (int)Activity.ACT_DOD_PRONEWALK_IDLE_PISTOL);
		AddActivityToSR("ACT_DOD_STAND_AIM_C96", (int)Activity.ACT_DOD_STAND_AIM_C96);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_C96", (int)Activity.ACT_DOD_CROUCH_AIM_C96);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_C96", (int)Activity.ACT_DOD_CROUCHWALK_AIM_C96);
		AddActivityToSR("ACT_DOD_WALK_AIM_C96", (int)Activity.ACT_DOD_WALK_AIM_C96);
		AddActivityToSR("ACT_DOD_RUN_AIM_C96", (int)Activity.ACT_DOD_RUN_AIM_C96);
		AddActivityToSR("ACT_DOD_PRONE_AIM_C96", (int)Activity.ACT_DOD_PRONE_AIM_C96);
		AddActivityToSR("ACT_DOD_STAND_IDLE_C96", (int)Activity.ACT_DOD_STAND_IDLE_C96);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_C96", (int)Activity.ACT_DOD_CROUCH_IDLE_C96);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_C96", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_C96);
		AddActivityToSR("ACT_DOD_WALK_IDLE_C96", (int)Activity.ACT_DOD_WALK_IDLE_C96);
		AddActivityToSR("ACT_DOD_RUN_IDLE_C96", (int)Activity.ACT_DOD_RUN_IDLE_C96);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_C96", (int)Activity.ACT_DOD_SPRINT_IDLE_C96);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_C96", (int)Activity.ACT_DOD_PRONEWALK_IDLE_C96);
		AddActivityToSR("ACT_DOD_STAND_AIM_RIFLE", (int)Activity.ACT_DOD_STAND_AIM_RIFLE);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_RIFLE", (int)Activity.ACT_DOD_CROUCH_AIM_RIFLE);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_RIFLE", (int)Activity.ACT_DOD_CROUCHWALK_AIM_RIFLE);
		AddActivityToSR("ACT_DOD_WALK_AIM_RIFLE", (int)Activity.ACT_DOD_WALK_AIM_RIFLE);
		AddActivityToSR("ACT_DOD_RUN_AIM_RIFLE", (int)Activity.ACT_DOD_RUN_AIM_RIFLE);
		AddActivityToSR("ACT_DOD_PRONE_AIM_RIFLE", (int)Activity.ACT_DOD_PRONE_AIM_RIFLE);
		AddActivityToSR("ACT_DOD_STAND_IDLE_RIFLE", (int)Activity.ACT_DOD_STAND_IDLE_RIFLE);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_RIFLE", (int)Activity.ACT_DOD_CROUCH_IDLE_RIFLE);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_RIFLE", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_RIFLE);
		AddActivityToSR("ACT_DOD_WALK_IDLE_RIFLE", (int)Activity.ACT_DOD_WALK_IDLE_RIFLE);
		AddActivityToSR("ACT_DOD_RUN_IDLE_RIFLE", (int)Activity.ACT_DOD_RUN_IDLE_RIFLE);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_RIFLE", (int)Activity.ACT_DOD_SPRINT_IDLE_RIFLE);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_RIFLE", (int)Activity.ACT_DOD_PRONEWALK_IDLE_RIFLE);
		AddActivityToSR("ACT_DOD_STAND_AIM_BOLT", (int)Activity.ACT_DOD_STAND_AIM_BOLT);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_BOLT", (int)Activity.ACT_DOD_CROUCH_AIM_BOLT);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_BOLT", (int)Activity.ACT_DOD_CROUCHWALK_AIM_BOLT);
		AddActivityToSR("ACT_DOD_WALK_AIM_BOLT", (int)Activity.ACT_DOD_WALK_AIM_BOLT);
		AddActivityToSR("ACT_DOD_RUN_AIM_BOLT", (int)Activity.ACT_DOD_RUN_AIM_BOLT);
		AddActivityToSR("ACT_DOD_PRONE_AIM_BOLT", (int)Activity.ACT_DOD_PRONE_AIM_BOLT);
		AddActivityToSR("ACT_DOD_STAND_IDLE_BOLT", (int)Activity.ACT_DOD_STAND_IDLE_BOLT);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_BOLT", (int)Activity.ACT_DOD_CROUCH_IDLE_BOLT);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_BOLT", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_BOLT);
		AddActivityToSR("ACT_DOD_WALK_IDLE_BOLT", (int)Activity.ACT_DOD_WALK_IDLE_BOLT);
		AddActivityToSR("ACT_DOD_RUN_IDLE_BOLT", (int)Activity.ACT_DOD_RUN_IDLE_BOLT);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_BOLT", (int)Activity.ACT_DOD_SPRINT_IDLE_BOLT);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_BOLT", (int)Activity.ACT_DOD_PRONEWALK_IDLE_BOLT);
		AddActivityToSR("ACT_DOD_STAND_AIM_TOMMY", (int)Activity.ACT_DOD_STAND_AIM_TOMMY);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_TOMMY", (int)Activity.ACT_DOD_CROUCH_AIM_TOMMY);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_TOMMY", (int)Activity.ACT_DOD_CROUCHWALK_AIM_TOMMY);
		AddActivityToSR("ACT_DOD_WALK_AIM_TOMMY", (int)Activity.ACT_DOD_WALK_AIM_TOMMY);
		AddActivityToSR("ACT_DOD_RUN_AIM_TOMMY", (int)Activity.ACT_DOD_RUN_AIM_TOMMY);
		AddActivityToSR("ACT_DOD_PRONE_AIM_TOMMY", (int)Activity.ACT_DOD_PRONE_AIM_TOMMY);
		AddActivityToSR("ACT_DOD_STAND_IDLE_TOMMY", (int)Activity.ACT_DOD_STAND_IDLE_TOMMY);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_TOMMY", (int)Activity.ACT_DOD_CROUCH_IDLE_TOMMY);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_TOMMY", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_TOMMY);
		AddActivityToSR("ACT_DOD_WALK_IDLE_TOMMY", (int)Activity.ACT_DOD_WALK_IDLE_TOMMY);
		AddActivityToSR("ACT_DOD_RUN_IDLE_TOMMY", (int)Activity.ACT_DOD_RUN_IDLE_TOMMY);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_TOMMY", (int)Activity.ACT_DOD_SPRINT_IDLE_TOMMY);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_TOMMY", (int)Activity.ACT_DOD_PRONEWALK_IDLE_TOMMY);
		AddActivityToSR("ACT_DOD_STAND_AIM_MP40", (int)Activity.ACT_DOD_STAND_AIM_MP40);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_MP40", (int)Activity.ACT_DOD_CROUCH_AIM_MP40);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_MP40", (int)Activity.ACT_DOD_CROUCHWALK_AIM_MP40);
		AddActivityToSR("ACT_DOD_WALK_AIM_MP40", (int)Activity.ACT_DOD_WALK_AIM_MP40);
		AddActivityToSR("ACT_DOD_RUN_AIM_MP40", (int)Activity.ACT_DOD_RUN_AIM_MP40);
		AddActivityToSR("ACT_DOD_PRONE_AIM_MP40", (int)Activity.ACT_DOD_PRONE_AIM_MP40);
		AddActivityToSR("ACT_DOD_STAND_IDLE_MP40", (int)Activity.ACT_DOD_STAND_IDLE_MP40);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_MP40", (int)Activity.ACT_DOD_CROUCH_IDLE_MP40);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_MP40", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_MP40);
		AddActivityToSR("ACT_DOD_WALK_IDLE_MP40", (int)Activity.ACT_DOD_WALK_IDLE_MP40);
		AddActivityToSR("ACT_DOD_RUN_IDLE_MP40", (int)Activity.ACT_DOD_RUN_IDLE_MP40);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_MP40", (int)Activity.ACT_DOD_SPRINT_IDLE_MP40);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_MP40", (int)Activity.ACT_DOD_PRONEWALK_IDLE_MP40);
		AddActivityToSR("ACT_DOD_STAND_AIM_MP44", (int)Activity.ACT_DOD_STAND_AIM_MP44);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_MP44", (int)Activity.ACT_DOD_CROUCH_AIM_MP44);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_MP44", (int)Activity.ACT_DOD_CROUCHWALK_AIM_MP44);
		AddActivityToSR("ACT_DOD_WALK_AIM_MP44", (int)Activity.ACT_DOD_WALK_AIM_MP44);
		AddActivityToSR("ACT_DOD_RUN_AIM_MP44", (int)Activity.ACT_DOD_RUN_AIM_MP44);
		AddActivityToSR("ACT_DOD_PRONE_AIM_MP44", (int)Activity.ACT_DOD_PRONE_AIM_MP44);
		AddActivityToSR("ACT_DOD_STAND_IDLE_MP44", (int)Activity.ACT_DOD_STAND_IDLE_MP44);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_MP44", (int)Activity.ACT_DOD_CROUCH_IDLE_MP44);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_MP44", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_MP44);
		AddActivityToSR("ACT_DOD_WALK_IDLE_MP44", (int)Activity.ACT_DOD_WALK_IDLE_MP44);
		AddActivityToSR("ACT_DOD_RUN_IDLE_MP44", (int)Activity.ACT_DOD_RUN_IDLE_MP44);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_MP44", (int)Activity.ACT_DOD_SPRINT_IDLE_MP44);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_MP44", (int)Activity.ACT_DOD_PRONEWALK_IDLE_MP44);
		AddActivityToSR("ACT_DOD_STAND_AIM_GREASE", (int)Activity.ACT_DOD_STAND_AIM_GREASE);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_GREASE", (int)Activity.ACT_DOD_CROUCH_AIM_GREASE);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_GREASE", (int)Activity.ACT_DOD_CROUCHWALK_AIM_GREASE);
		AddActivityToSR("ACT_DOD_WALK_AIM_GREASE", (int)Activity.ACT_DOD_WALK_AIM_GREASE);
		AddActivityToSR("ACT_DOD_RUN_AIM_GREASE", (int)Activity.ACT_DOD_RUN_AIM_GREASE);
		AddActivityToSR("ACT_DOD_PRONE_AIM_GREASE", (int)Activity.ACT_DOD_PRONE_AIM_GREASE);
		AddActivityToSR("ACT_DOD_STAND_IDLE_GREASE", (int)Activity.ACT_DOD_STAND_IDLE_GREASE);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_GREASE", (int)Activity.ACT_DOD_CROUCH_IDLE_GREASE);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_GREASE", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_GREASE);
		AddActivityToSR("ACT_DOD_WALK_IDLE_GREASE", (int)Activity.ACT_DOD_WALK_IDLE_GREASE);
		AddActivityToSR("ACT_DOD_RUN_IDLE_GREASE", (int)Activity.ACT_DOD_RUN_IDLE_GREASE);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_GREASE", (int)Activity.ACT_DOD_SPRINT_IDLE_GREASE);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_GREASE", (int)Activity.ACT_DOD_PRONEWALK_IDLE_GREASE);
		AddActivityToSR("ACT_DOD_STAND_AIM_MG", (int)Activity.ACT_DOD_STAND_AIM_MG);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_MG", (int)Activity.ACT_DOD_CROUCH_AIM_MG);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_MG", (int)Activity.ACT_DOD_CROUCHWALK_AIM_MG);
		AddActivityToSR("ACT_DOD_WALK_AIM_MG", (int)Activity.ACT_DOD_WALK_AIM_MG);
		AddActivityToSR("ACT_DOD_RUN_AIM_MG", (int)Activity.ACT_DOD_RUN_AIM_MG);
		AddActivityToSR("ACT_DOD_PRONE_AIM_MG", (int)Activity.ACT_DOD_PRONE_AIM_MG);
		AddActivityToSR("ACT_DOD_STAND_IDLE_MG", (int)Activity.ACT_DOD_STAND_IDLE_MG);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_MG", (int)Activity.ACT_DOD_CROUCH_IDLE_MG);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_MG", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_MG);
		AddActivityToSR("ACT_DOD_WALK_IDLE_MG", (int)Activity.ACT_DOD_WALK_IDLE_MG);
		AddActivityToSR("ACT_DOD_RUN_IDLE_MG", (int)Activity.ACT_DOD_RUN_IDLE_MG);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_MG", (int)Activity.ACT_DOD_SPRINT_IDLE_MG);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_MG", (int)Activity.ACT_DOD_PRONEWALK_IDLE_MG);
		AddActivityToSR("ACT_DOD_STAND_AIM_30CAL", (int)Activity.ACT_DOD_STAND_AIM_30CAL);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_30CAL", (int)Activity.ACT_DOD_CROUCH_AIM_30CAL);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_30CAL", (int)Activity.ACT_DOD_CROUCHWALK_AIM_30CAL);
		AddActivityToSR("ACT_DOD_WALK_AIM_30CAL", (int)Activity.ACT_DOD_WALK_AIM_30CAL);
		AddActivityToSR("ACT_DOD_RUN_AIM_30CAL", (int)Activity.ACT_DOD_RUN_AIM_30CAL);
		AddActivityToSR("ACT_DOD_PRONE_AIM_30CAL", (int)Activity.ACT_DOD_PRONE_AIM_30CAL);
		AddActivityToSR("ACT_DOD_STAND_IDLE_30CAL", (int)Activity.ACT_DOD_STAND_IDLE_30CAL);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_30CAL", (int)Activity.ACT_DOD_CROUCH_IDLE_30CAL);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_30CAL", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_30CAL);
		AddActivityToSR("ACT_DOD_WALK_IDLE_30CAL", (int)Activity.ACT_DOD_WALK_IDLE_30CAL);
		AddActivityToSR("ACT_DOD_RUN_IDLE_30CAL", (int)Activity.ACT_DOD_RUN_IDLE_30CAL);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_30CAL", (int)Activity.ACT_DOD_SPRINT_IDLE_30CAL);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_30CAL", (int)Activity.ACT_DOD_PRONEWALK_IDLE_30CAL);
		AddActivityToSR("ACT_DOD_STAND_AIM_GREN_FRAG", (int)Activity.ACT_DOD_STAND_AIM_GREN_FRAG);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_GREN_FRAG", (int)Activity.ACT_DOD_CROUCH_AIM_GREN_FRAG);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_GREN_FRAG", (int)Activity.ACT_DOD_CROUCHWALK_AIM_GREN_FRAG);
		AddActivityToSR("ACT_DOD_WALK_AIM_GREN_FRAG", (int)Activity.ACT_DOD_WALK_AIM_GREN_FRAG);
		AddActivityToSR("ACT_DOD_RUN_AIM_GREN_FRAG", (int)Activity.ACT_DOD_RUN_AIM_GREN_FRAG);
		AddActivityToSR("ACT_DOD_PRONE_AIM_GREN_FRAG", (int)Activity.ACT_DOD_PRONE_AIM_GREN_FRAG);
		AddActivityToSR("ACT_DOD_SPRINT_AIM_GREN_FRAG", (int)Activity.ACT_DOD_SPRINT_AIM_GREN_FRAG);
		AddActivityToSR("ACT_DOD_PRONEWALK_AIM_GREN_FRAG", (int)Activity.ACT_DOD_PRONEWALK_AIM_GREN_FRAG);
		AddActivityToSR("ACT_DOD_STAND_AIM_GREN_STICK", (int)Activity.ACT_DOD_STAND_AIM_GREN_STICK);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_GREN_STICK", (int)Activity.ACT_DOD_CROUCH_AIM_GREN_STICK);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_GREN_STICK", (int)Activity.ACT_DOD_CROUCHWALK_AIM_GREN_STICK);
		AddActivityToSR("ACT_DOD_WALK_AIM_GREN_STICK", (int)Activity.ACT_DOD_WALK_AIM_GREN_STICK);
		AddActivityToSR("ACT_DOD_RUN_AIM_GREN_STICK", (int)Activity.ACT_DOD_RUN_AIM_GREN_STICK);
		AddActivityToSR("ACT_DOD_PRONE_AIM_GREN_STICK", (int)Activity.ACT_DOD_PRONE_AIM_GREN_STICK);
		AddActivityToSR("ACT_DOD_SPRINT_AIM_GREN_STICK", (int)Activity.ACT_DOD_SPRINT_AIM_GREN_STICK);
		AddActivityToSR("ACT_DOD_PRONEWALK_AIM_GREN_STICK", (int)Activity.ACT_DOD_PRONEWALK_AIM_GREN_STICK);
		AddActivityToSR("ACT_DOD_STAND_AIM_KNIFE", (int)Activity.ACT_DOD_STAND_AIM_KNIFE);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_KNIFE", (int)Activity.ACT_DOD_CROUCH_AIM_KNIFE);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_KNIFE", (int)Activity.ACT_DOD_CROUCHWALK_AIM_KNIFE);
		AddActivityToSR("ACT_DOD_WALK_AIM_KNIFE", (int)Activity.ACT_DOD_WALK_AIM_KNIFE);
		AddActivityToSR("ACT_DOD_RUN_AIM_KNIFE", (int)Activity.ACT_DOD_RUN_AIM_KNIFE);
		AddActivityToSR("ACT_DOD_PRONE_AIM_KNIFE", (int)Activity.ACT_DOD_PRONE_AIM_KNIFE);
		AddActivityToSR("ACT_DOD_SPRINT_AIM_KNIFE", (int)Activity.ACT_DOD_SPRINT_AIM_KNIFE);
		AddActivityToSR("ACT_DOD_PRONEWALK_AIM_KNIFE", (int)Activity.ACT_DOD_PRONEWALK_AIM_KNIFE);
		AddActivityToSR("ACT_DOD_STAND_AIM_SPADE", (int)Activity.ACT_DOD_STAND_AIM_SPADE);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_SPADE", (int)Activity.ACT_DOD_CROUCH_AIM_SPADE);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_SPADE", (int)Activity.ACT_DOD_CROUCHWALK_AIM_SPADE);
		AddActivityToSR("ACT_DOD_WALK_AIM_SPADE", (int)Activity.ACT_DOD_WALK_AIM_SPADE);
		AddActivityToSR("ACT_DOD_RUN_AIM_SPADE", (int)Activity.ACT_DOD_RUN_AIM_SPADE);
		AddActivityToSR("ACT_DOD_PRONE_AIM_SPADE", (int)Activity.ACT_DOD_PRONE_AIM_SPADE);
		AddActivityToSR("ACT_DOD_SPRINT_AIM_SPADE", (int)Activity.ACT_DOD_SPRINT_AIM_SPADE);
		AddActivityToSR("ACT_DOD_PRONEWALK_AIM_SPADE", (int)Activity.ACT_DOD_PRONEWALK_AIM_SPADE);
		AddActivityToSR("ACT_DOD_STAND_AIM_BAZOOKA", (int)Activity.ACT_DOD_STAND_AIM_BAZOOKA);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_BAZOOKA", (int)Activity.ACT_DOD_CROUCH_AIM_BAZOOKA);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_BAZOOKA", (int)Activity.ACT_DOD_CROUCHWALK_AIM_BAZOOKA);
		AddActivityToSR("ACT_DOD_WALK_AIM_BAZOOKA", (int)Activity.ACT_DOD_WALK_AIM_BAZOOKA);
		AddActivityToSR("ACT_DOD_RUN_AIM_BAZOOKA", (int)Activity.ACT_DOD_RUN_AIM_BAZOOKA);
		AddActivityToSR("ACT_DOD_PRONE_AIM_BAZOOKA", (int)Activity.ACT_DOD_PRONE_AIM_BAZOOKA);
		AddActivityToSR("ACT_DOD_STAND_IDLE_BAZOOKA", (int)Activity.ACT_DOD_STAND_IDLE_BAZOOKA);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_BAZOOKA", (int)Activity.ACT_DOD_CROUCH_IDLE_BAZOOKA);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_BAZOOKA", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_BAZOOKA);
		AddActivityToSR("ACT_DOD_WALK_IDLE_BAZOOKA", (int)Activity.ACT_DOD_WALK_IDLE_BAZOOKA);
		AddActivityToSR("ACT_DOD_RUN_IDLE_BAZOOKA", (int)Activity.ACT_DOD_RUN_IDLE_BAZOOKA);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_BAZOOKA", (int)Activity.ACT_DOD_SPRINT_IDLE_BAZOOKA);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_BAZOOKA", (int)Activity.ACT_DOD_PRONEWALK_IDLE_BAZOOKA);
		AddActivityToSR("ACT_DOD_STAND_AIM_PSCHRECK", (int)Activity.ACT_DOD_STAND_AIM_PSCHRECK);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_PSCHRECK", (int)Activity.ACT_DOD_CROUCH_AIM_PSCHRECK);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_PSCHRECK", (int)Activity.ACT_DOD_CROUCHWALK_AIM_PSCHRECK);
		AddActivityToSR("ACT_DOD_WALK_AIM_PSCHRECK", (int)Activity.ACT_DOD_WALK_AIM_PSCHRECK);
		AddActivityToSR("ACT_DOD_RUN_AIM_PSCHRECK", (int)Activity.ACT_DOD_RUN_AIM_PSCHRECK);
		AddActivityToSR("ACT_DOD_PRONE_AIM_PSCHRECK", (int)Activity.ACT_DOD_PRONE_AIM_PSCHRECK);
		AddActivityToSR("ACT_DOD_STAND_IDLE_PSCHRECK", (int)Activity.ACT_DOD_STAND_IDLE_PSCHRECK);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_PSCHRECK", (int)Activity.ACT_DOD_CROUCH_IDLE_PSCHRECK);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_PSCHRECK", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_PSCHRECK);
		AddActivityToSR("ACT_DOD_WALK_IDLE_PSCHRECK", (int)Activity.ACT_DOD_WALK_IDLE_PSCHRECK);
		AddActivityToSR("ACT_DOD_RUN_IDLE_PSCHRECK", (int)Activity.ACT_DOD_RUN_IDLE_PSCHRECK);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_PSCHRECK", (int)Activity.ACT_DOD_SPRINT_IDLE_PSCHRECK);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_PSCHRECK", (int)Activity.ACT_DOD_PRONEWALK_IDLE_PSCHRECK);
		AddActivityToSR("ACT_DOD_STAND_AIM_BAR", (int)Activity.ACT_DOD_STAND_AIM_BAR);
		AddActivityToSR("ACT_DOD_CROUCH_AIM_BAR", (int)Activity.ACT_DOD_CROUCH_AIM_BAR);
		AddActivityToSR("ACT_DOD_CROUCHWALK_AIM_BAR", (int)Activity.ACT_DOD_CROUCHWALK_AIM_BAR);
		AddActivityToSR("ACT_DOD_WALK_AIM_BAR", (int)Activity.ACT_DOD_WALK_AIM_BAR);
		AddActivityToSR("ACT_DOD_RUN_AIM_BAR", (int)Activity.ACT_DOD_RUN_AIM_BAR);
		AddActivityToSR("ACT_DOD_PRONE_AIM_BAR", (int)Activity.ACT_DOD_PRONE_AIM_BAR);
		AddActivityToSR("ACT_DOD_STAND_IDLE_BAR", (int)Activity.ACT_DOD_STAND_IDLE_BAR);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_BAR", (int)Activity.ACT_DOD_CROUCH_IDLE_BAR);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_BAR", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_BAR);
		AddActivityToSR("ACT_DOD_WALK_IDLE_BAR", (int)Activity.ACT_DOD_WALK_IDLE_BAR);
		AddActivityToSR("ACT_DOD_RUN_IDLE_BAR", (int)Activity.ACT_DOD_RUN_IDLE_BAR);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_BAR", (int)Activity.ACT_DOD_SPRINT_IDLE_BAR);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_BAR", (int)Activity.ACT_DOD_PRONEWALK_IDLE_BAR);
		AddActivityToSR("ACT_DOD_STAND_ZOOM_RIFLE", (int)Activity.ACT_DOD_STAND_ZOOM_RIFLE);
		AddActivityToSR("ACT_DOD_CROUCH_ZOOM_RIFLE", (int)Activity.ACT_DOD_CROUCH_ZOOM_RIFLE);
		AddActivityToSR("ACT_DOD_CROUCHWALK_ZOOM_RIFLE", (int)Activity.ACT_DOD_CROUCHWALK_ZOOM_RIFLE);
		AddActivityToSR("ACT_DOD_WALK_ZOOM_RIFLE", (int)Activity.ACT_DOD_WALK_ZOOM_RIFLE);
		AddActivityToSR("ACT_DOD_RUN_ZOOM_RIFLE", (int)Activity.ACT_DOD_RUN_ZOOM_RIFLE);
		AddActivityToSR("ACT_DOD_PRONE_ZOOM_RIFLE", (int)Activity.ACT_DOD_PRONE_ZOOM_RIFLE);
		AddActivityToSR("ACT_DOD_STAND_ZOOM_BOLT", (int)Activity.ACT_DOD_STAND_ZOOM_BOLT);
		AddActivityToSR("ACT_DOD_CROUCH_ZOOM_BOLT", (int)Activity.ACT_DOD_CROUCH_ZOOM_BOLT);
		AddActivityToSR("ACT_DOD_CROUCHWALK_ZOOM_BOLT", (int)Activity.ACT_DOD_CROUCHWALK_ZOOM_BOLT);
		AddActivityToSR("ACT_DOD_WALK_ZOOM_BOLT", (int)Activity.ACT_DOD_WALK_ZOOM_BOLT);
		AddActivityToSR("ACT_DOD_RUN_ZOOM_BOLT", (int)Activity.ACT_DOD_RUN_ZOOM_BOLT);
		AddActivityToSR("ACT_DOD_PRONE_ZOOM_BOLT", (int)Activity.ACT_DOD_PRONE_ZOOM_BOLT);
		AddActivityToSR("ACT_DOD_STAND_ZOOM_BAZOOKA", (int)Activity.ACT_DOD_STAND_ZOOM_BAZOOKA);
		AddActivityToSR("ACT_DOD_CROUCH_ZOOM_BAZOOKA", (int)Activity.ACT_DOD_CROUCH_ZOOM_BAZOOKA);
		AddActivityToSR("ACT_DOD_CROUCHWALK_ZOOM_BAZOOKA", (int)Activity.ACT_DOD_CROUCHWALK_ZOOM_BAZOOKA);
		AddActivityToSR("ACT_DOD_WALK_ZOOM_BAZOOKA", (int)Activity.ACT_DOD_WALK_ZOOM_BAZOOKA);
		AddActivityToSR("ACT_DOD_RUN_ZOOM_BAZOOKA", (int)Activity.ACT_DOD_RUN_ZOOM_BAZOOKA);
		AddActivityToSR("ACT_DOD_PRONE_ZOOM_BAZOOKA", (int)Activity.ACT_DOD_PRONE_ZOOM_BAZOOKA);
		AddActivityToSR("ACT_DOD_STAND_ZOOM_PSCHRECK", (int)Activity.ACT_DOD_STAND_ZOOM_PSCHRECK);
		AddActivityToSR("ACT_DOD_CROUCH_ZOOM_PSCHRECK", (int)Activity.ACT_DOD_CROUCH_ZOOM_PSCHRECK);
		AddActivityToSR("ACT_DOD_CROUCHWALK_ZOOM_PSCHRECK", (int)Activity.ACT_DOD_CROUCHWALK_ZOOM_PSCHRECK);
		AddActivityToSR("ACT_DOD_WALK_ZOOM_PSCHRECK", (int)Activity.ACT_DOD_WALK_ZOOM_PSCHRECK);
		AddActivityToSR("ACT_DOD_RUN_ZOOM_PSCHRECK", (int)Activity.ACT_DOD_RUN_ZOOM_PSCHRECK);
		AddActivityToSR("ACT_DOD_PRONE_ZOOM_PSCHRECK", (int)Activity.ACT_DOD_PRONE_ZOOM_PSCHRECK);
		AddActivityToSR("ACT_DOD_DEPLOY_RIFLE", (int)Activity.ACT_DOD_DEPLOY_RIFLE);
		AddActivityToSR("ACT_DOD_DEPLOY_TOMMY", (int)Activity.ACT_DOD_DEPLOY_TOMMY);
		AddActivityToSR("ACT_DOD_DEPLOY_MG", (int)Activity.ACT_DOD_DEPLOY_MG);
		AddActivityToSR("ACT_DOD_DEPLOY_30CAL", (int)Activity.ACT_DOD_DEPLOY_30CAL);
		AddActivityToSR("ACT_DOD_PRONE_DEPLOY_RIFLE", (int)Activity.ACT_DOD_PRONE_DEPLOY_RIFLE);
		AddActivityToSR("ACT_DOD_PRONE_DEPLOY_TOMMY", (int)Activity.ACT_DOD_PRONE_DEPLOY_TOMMY);
		AddActivityToSR("ACT_DOD_PRONE_DEPLOY_MG", (int)Activity.ACT_DOD_PRONE_DEPLOY_MG);
		AddActivityToSR("ACT_DOD_PRONE_DEPLOY_30CAL", (int)Activity.ACT_DOD_PRONE_DEPLOY_30CAL);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_RIFLE", (int)Activity.ACT_DOD_PRIMARYATTACK_RIFLE);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_RIFLE", (int)Activity.ACT_DOD_SECONDARYATTACK_RIFLE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_RIFLE", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_RIFLE);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_PRONE_RIFLE", (int)Activity.ACT_DOD_SECONDARYATTACK_PRONE_RIFLE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_DEPLOYED_RIFLE", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_DEPLOYED_RIFLE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_DEPLOYED_RIFLE", (int)Activity.ACT_DOD_PRIMARYATTACK_DEPLOYED_RIFLE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_BOLT", (int)Activity.ACT_DOD_PRIMARYATTACK_BOLT);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_BOLT", (int)Activity.ACT_DOD_SECONDARYATTACK_BOLT);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_BOLT", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_BOLT);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_PRONE_BOLT", (int)Activity.ACT_DOD_SECONDARYATTACK_PRONE_BOLT);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_TOMMY", (int)Activity.ACT_DOD_PRIMARYATTACK_TOMMY);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_TOMMY", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_TOMMY);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_TOMMY", (int)Activity.ACT_DOD_SECONDARYATTACK_TOMMY);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_PRONE_TOMMY", (int)Activity.ACT_DOD_SECONDARYATTACK_PRONE_TOMMY);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_MP40", (int)Activity.ACT_DOD_PRIMARYATTACK_MP40);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_MP40", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_MP40);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_MP40", (int)Activity.ACT_DOD_SECONDARYATTACK_MP40);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_PRONE_MP40", (int)Activity.ACT_DOD_SECONDARYATTACK_PRONE_MP40);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_MP44", (int)Activity.ACT_DOD_PRIMARYATTACK_MP44);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_MP44", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_MP44);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_GREASE", (int)Activity.ACT_DOD_PRIMARYATTACK_GREASE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_GREASE", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_GREASE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PISTOL", (int)Activity.ACT_DOD_PRIMARYATTACK_PISTOL);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_PISTOL", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_PISTOL);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_C96", (int)Activity.ACT_DOD_PRIMARYATTACK_C96);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_C96", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_C96);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_MG", (int)Activity.ACT_DOD_PRIMARYATTACK_MG);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_MG", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_MG);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_DEPLOYED_MG", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_DEPLOYED_MG);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_DEPLOYED_MG", (int)Activity.ACT_DOD_PRIMARYATTACK_DEPLOYED_MG);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_30CAL", (int)Activity.ACT_DOD_PRIMARYATTACK_30CAL);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_30CAL", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_30CAL);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_DEPLOYED_30CAL", (int)Activity.ACT_DOD_PRIMARYATTACK_DEPLOYED_30CAL);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_DEPLOYED_30CAL", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_DEPLOYED_30CAL);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_GREN_FRAG", (int)Activity.ACT_DOD_PRIMARYATTACK_GREN_FRAG);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_GREN_FRAG", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_GREN_FRAG);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_GREN_STICK", (int)Activity.ACT_DOD_PRIMARYATTACK_GREN_STICK);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_GREN_STICK", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_GREN_STICK);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_KNIFE", (int)Activity.ACT_DOD_PRIMARYATTACK_KNIFE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_KNIFE", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_KNIFE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_SPADE", (int)Activity.ACT_DOD_PRIMARYATTACK_SPADE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_SPADE", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_SPADE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_BAZOOKA", (int)Activity.ACT_DOD_PRIMARYATTACK_BAZOOKA);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_BAZOOKA", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_BAZOOKA);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PSCHRECK", (int)Activity.ACT_DOD_PRIMARYATTACK_PSCHRECK);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_PSCHRECK", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_PSCHRECK);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_BAR", (int)Activity.ACT_DOD_PRIMARYATTACK_BAR);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_PRONE_BAR", (int)Activity.ACT_DOD_PRIMARYATTACK_PRONE_BAR);
		AddActivityToSR("ACT_DOD_RELOAD_GARAND", (int)Activity.ACT_DOD_RELOAD_GARAND);
		AddActivityToSR("ACT_DOD_RELOAD_K43", (int)Activity.ACT_DOD_RELOAD_K43);
		AddActivityToSR("ACT_DOD_RELOAD_BAR", (int)Activity.ACT_DOD_RELOAD_BAR);
		AddActivityToSR("ACT_DOD_RELOAD_MP40", (int)Activity.ACT_DOD_RELOAD_MP40);
		AddActivityToSR("ACT_DOD_RELOAD_MP44", (int)Activity.ACT_DOD_RELOAD_MP44);
		AddActivityToSR("ACT_DOD_RELOAD_BOLT", (int)Activity.ACT_DOD_RELOAD_BOLT);
		AddActivityToSR("ACT_DOD_RELOAD_M1CARBINE", (int)Activity.ACT_DOD_RELOAD_M1CARBINE);
		AddActivityToSR("ACT_DOD_RELOAD_TOMMY", (int)Activity.ACT_DOD_RELOAD_TOMMY);
		AddActivityToSR("ACT_DOD_RELOAD_GREASEGUN", (int)Activity.ACT_DOD_RELOAD_GREASEGUN);
		AddActivityToSR("ACT_DOD_RELOAD_PISTOL", (int)Activity.ACT_DOD_RELOAD_PISTOL);
		AddActivityToSR("ACT_DOD_RELOAD_FG42", (int)Activity.ACT_DOD_RELOAD_FG42);
		AddActivityToSR("ACT_DOD_RELOAD_RIFLE", (int)Activity.ACT_DOD_RELOAD_RIFLE);
		AddActivityToSR("ACT_DOD_RELOAD_RIFLEGRENADE", (int)Activity.ACT_DOD_RELOAD_RIFLEGRENADE);
		AddActivityToSR("ACT_DOD_RELOAD_C96", (int)Activity.ACT_DOD_RELOAD_C96);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_BAR", (int)Activity.ACT_DOD_RELOAD_CROUCH_BAR);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_RIFLE", (int)Activity.ACT_DOD_RELOAD_CROUCH_RIFLE);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_RIFLEGRENADE", (int)Activity.ACT_DOD_RELOAD_CROUCH_RIFLEGRENADE);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_BOLT", (int)Activity.ACT_DOD_RELOAD_CROUCH_BOLT);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_MP44", (int)Activity.ACT_DOD_RELOAD_CROUCH_MP44);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_MP40", (int)Activity.ACT_DOD_RELOAD_CROUCH_MP40);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_TOMMY", (int)Activity.ACT_DOD_RELOAD_CROUCH_TOMMY);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_BAZOOKA", (int)Activity.ACT_DOD_RELOAD_CROUCH_BAZOOKA);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_PSCHRECK", (int)Activity.ACT_DOD_RELOAD_CROUCH_PSCHRECK);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_PISTOL", (int)Activity.ACT_DOD_RELOAD_CROUCH_PISTOL);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_M1CARBINE", (int)Activity.ACT_DOD_RELOAD_CROUCH_M1CARBINE);
		AddActivityToSR("ACT_DOD_RELOAD_CROUCH_C96", (int)Activity.ACT_DOD_RELOAD_CROUCH_C96);
		AddActivityToSR("ACT_DOD_RELOAD_BAZOOKA", (int)Activity.ACT_DOD_RELOAD_BAZOOKA);
		AddActivityToSR("ACT_DOD_ZOOMLOAD_BAZOOKA", (int)Activity.ACT_DOD_ZOOMLOAD_BAZOOKA);
		AddActivityToSR("ACT_DOD_RELOAD_PSCHRECK", (int)Activity.ACT_DOD_RELOAD_PSCHRECK);
		AddActivityToSR("ACT_DOD_ZOOMLOAD_PSCHRECK", (int)Activity.ACT_DOD_ZOOMLOAD_PSCHRECK);
		AddActivityToSR("ACT_DOD_RELOAD_DEPLOYED_FG42", (int)Activity.ACT_DOD_RELOAD_DEPLOYED_FG42);
		AddActivityToSR("ACT_DOD_RELOAD_DEPLOYED_30CAL", (int)Activity.ACT_DOD_RELOAD_DEPLOYED_30CAL);
		AddActivityToSR("ACT_DOD_RELOAD_DEPLOYED_MG", (int)Activity.ACT_DOD_RELOAD_DEPLOYED_MG);
		AddActivityToSR("ACT_DOD_RELOAD_DEPLOYED_MG34", (int)Activity.ACT_DOD_RELOAD_DEPLOYED_MG34);
		AddActivityToSR("ACT_DOD_RELOAD_DEPLOYED_BAR", (int)Activity.ACT_DOD_RELOAD_DEPLOYED_BAR);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_PISTOL", (int)Activity.ACT_DOD_RELOAD_PRONE_PISTOL);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_GARAND", (int)Activity.ACT_DOD_RELOAD_PRONE_GARAND);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_M1CARBINE", (int)Activity.ACT_DOD_RELOAD_PRONE_M1CARBINE);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_BOLT", (int)Activity.ACT_DOD_RELOAD_PRONE_BOLT);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_K43", (int)Activity.ACT_DOD_RELOAD_PRONE_K43);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_MP40", (int)Activity.ACT_DOD_RELOAD_PRONE_MP40);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_MP44", (int)Activity.ACT_DOD_RELOAD_PRONE_MP44);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_BAR", (int)Activity.ACT_DOD_RELOAD_PRONE_BAR);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_GREASEGUN", (int)Activity.ACT_DOD_RELOAD_PRONE_GREASEGUN);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_TOMMY", (int)Activity.ACT_DOD_RELOAD_PRONE_TOMMY);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_FG42", (int)Activity.ACT_DOD_RELOAD_PRONE_FG42);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_RIFLE", (int)Activity.ACT_DOD_RELOAD_PRONE_RIFLE);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_RIFLEGRENADE", (int)Activity.ACT_DOD_RELOAD_PRONE_RIFLEGRENADE);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_C96", (int)Activity.ACT_DOD_RELOAD_PRONE_C96);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_BAZOOKA", (int)Activity.ACT_DOD_RELOAD_PRONE_BAZOOKA);
		AddActivityToSR("ACT_DOD_ZOOMLOAD_PRONE_BAZOOKA", (int)Activity.ACT_DOD_ZOOMLOAD_PRONE_BAZOOKA);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_PSCHRECK", (int)Activity.ACT_DOD_RELOAD_PRONE_PSCHRECK);
		AddActivityToSR("ACT_DOD_ZOOMLOAD_PRONE_PSCHRECK", (int)Activity.ACT_DOD_ZOOMLOAD_PRONE_PSCHRECK);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_DEPLOYED_BAR", (int)Activity.ACT_DOD_RELOAD_PRONE_DEPLOYED_BAR);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_DEPLOYED_FG42", (int)Activity.ACT_DOD_RELOAD_PRONE_DEPLOYED_FG42);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_DEPLOYED_30CAL", (int)Activity.ACT_DOD_RELOAD_PRONE_DEPLOYED_30CAL);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_DEPLOYED_MG", (int)Activity.ACT_DOD_RELOAD_PRONE_DEPLOYED_MG);
		AddActivityToSR("ACT_DOD_RELOAD_PRONE_DEPLOYED_MG34", (int)Activity.ACT_DOD_RELOAD_PRONE_DEPLOYED_MG34);
		AddActivityToSR("ACT_DOD_PRONE_ZOOM_FORWARD_RIFLE", (int)Activity.ACT_DOD_PRONE_ZOOM_FORWARD_RIFLE);
		AddActivityToSR("ACT_DOD_PRONE_ZOOM_FORWARD_BOLT", (int)Activity.ACT_DOD_PRONE_ZOOM_FORWARD_BOLT);
		AddActivityToSR("ACT_DOD_PRONE_ZOOM_FORWARD_BAZOOKA", (int)Activity.ACT_DOD_PRONE_ZOOM_FORWARD_BAZOOKA);
		AddActivityToSR("ACT_DOD_PRONE_ZOOM_FORWARD_PSCHRECK", (int)Activity.ACT_DOD_PRONE_ZOOM_FORWARD_PSCHRECK);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_CROUCH", (int)Activity.ACT_DOD_PRIMARYATTACK_CROUCH);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_CROUCH_SPADE", (int)Activity.ACT_DOD_PRIMARYATTACK_CROUCH_SPADE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_CROUCH_KNIFE", (int)Activity.ACT_DOD_PRIMARYATTACK_CROUCH_KNIFE);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_CROUCH_GREN_FRAG", (int)Activity.ACT_DOD_PRIMARYATTACK_CROUCH_GREN_FRAG);
		AddActivityToSR("ACT_DOD_PRIMARYATTACK_CROUCH_GREN_STICK", (int)Activity.ACT_DOD_PRIMARYATTACK_CROUCH_GREN_STICK);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_CROUCH", (int)Activity.ACT_DOD_SECONDARYATTACK_CROUCH);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_CROUCH_TOMMY", (int)Activity.ACT_DOD_SECONDARYATTACK_CROUCH_TOMMY);
		AddActivityToSR("ACT_DOD_SECONDARYATTACK_CROUCH_MP40", (int)Activity.ACT_DOD_SECONDARYATTACK_CROUCH_MP40);
		AddActivityToSR("ACT_DOD_HS_IDLE", (int)Activity.ACT_DOD_HS_IDLE);
		AddActivityToSR("ACT_DOD_HS_CROUCH", (int)Activity.ACT_DOD_HS_CROUCH);
		AddActivityToSR("ACT_DOD_HS_IDLE_30CAL", (int)Activity.ACT_DOD_HS_IDLE_30CAL);
		AddActivityToSR("ACT_DOD_HS_IDLE_BAZOOKA", (int)Activity.ACT_DOD_HS_IDLE_BAZOOKA);
		AddActivityToSR("ACT_DOD_HS_IDLE_PSCHRECK", (int)Activity.ACT_DOD_HS_IDLE_PSCHRECK);
		AddActivityToSR("ACT_DOD_HS_IDLE_KNIFE", (int)Activity.ACT_DOD_HS_IDLE_KNIFE);
		AddActivityToSR("ACT_DOD_HS_IDLE_MG42", (int)Activity.ACT_DOD_HS_IDLE_MG42);
		AddActivityToSR("ACT_DOD_HS_IDLE_PISTOL", (int)Activity.ACT_DOD_HS_IDLE_PISTOL);
		AddActivityToSR("ACT_DOD_HS_IDLE_STICKGRENADE", (int)Activity.ACT_DOD_HS_IDLE_STICKGRENADE);
		AddActivityToSR("ACT_DOD_HS_IDLE_TOMMY", (int)Activity.ACT_DOD_HS_IDLE_TOMMY);
		AddActivityToSR("ACT_DOD_HS_IDLE_MP44", (int)Activity.ACT_DOD_HS_IDLE_MP44);
		AddActivityToSR("ACT_DOD_HS_IDLE_K98", (int)Activity.ACT_DOD_HS_IDLE_K98);
		AddActivityToSR("ACT_DOD_HS_CROUCH_30CAL", (int)Activity.ACT_DOD_HS_CROUCH_30CAL);
		AddActivityToSR("ACT_DOD_HS_CROUCH_BAZOOKA", (int)Activity.ACT_DOD_HS_CROUCH_BAZOOKA);
		AddActivityToSR("ACT_DOD_HS_CROUCH_PSCHRECK", (int)Activity.ACT_DOD_HS_CROUCH_PSCHRECK);
		AddActivityToSR("ACT_DOD_HS_CROUCH_KNIFE", (int)Activity.ACT_DOD_HS_CROUCH_KNIFE);
		AddActivityToSR("ACT_DOD_HS_CROUCH_MG42", (int)Activity.ACT_DOD_HS_CROUCH_MG42);
		AddActivityToSR("ACT_DOD_HS_CROUCH_PISTOL", (int)Activity.ACT_DOD_HS_CROUCH_PISTOL);
		AddActivityToSR("ACT_DOD_HS_CROUCH_STICKGRENADE", (int)Activity.ACT_DOD_HS_CROUCH_STICKGRENADE);
		AddActivityToSR("ACT_DOD_HS_CROUCH_TOMMY", (int)Activity.ACT_DOD_HS_CROUCH_TOMMY);
		AddActivityToSR("ACT_DOD_HS_CROUCH_MP44", (int)Activity.ACT_DOD_HS_CROUCH_MP44);
		AddActivityToSR("ACT_DOD_HS_CROUCH_K98", (int)Activity.ACT_DOD_HS_CROUCH_K98);
		AddActivityToSR("ACT_DOD_STAND_IDLE_TNT", (int)Activity.ACT_DOD_STAND_IDLE_TNT);
		AddActivityToSR("ACT_DOD_CROUCH_IDLE_TNT", (int)Activity.ACT_DOD_CROUCH_IDLE_TNT);
		AddActivityToSR("ACT_DOD_CROUCHWALK_IDLE_TNT", (int)Activity.ACT_DOD_CROUCHWALK_IDLE_TNT);
		AddActivityToSR("ACT_DOD_WALK_IDLE_TNT", (int)Activity.ACT_DOD_WALK_IDLE_TNT);
		AddActivityToSR("ACT_DOD_RUN_IDLE_TNT", (int)Activity.ACT_DOD_RUN_IDLE_TNT);
		AddActivityToSR("ACT_DOD_SPRINT_IDLE_TNT", (int)Activity.ACT_DOD_SPRINT_IDLE_TNT);
		AddActivityToSR("ACT_DOD_PRONEWALK_IDLE_TNT", (int)Activity.ACT_DOD_PRONEWALK_IDLE_TNT);
		AddActivityToSR("ACT_DOD_PLANT_TNT", (int)Activity.ACT_DOD_PLANT_TNT);
		AddActivityToSR("ACT_DOD_DEFUSE_TNT", (int)Activity.ACT_DOD_DEFUSE_TNT);
		AddActivityToSR("ACT_HL2MP_WALK", (int)Activity.ACT_HL2MP_WALK);
		AddActivityToSR("ACT_HL2MP_WALK_MELEE", (int)Activity.ACT_HL2MP_WALK_MELEE);
		AddActivityToSR("ACT_HL2MP_WALK_SMG1", (int)Activity.ACT_HL2MP_WALK_SMG1);
		AddActivityToSR("ACT_HL2MP_WALK_PISTOL", (int)Activity.ACT_HL2MP_WALK_PISTOL);
		AddActivityToSR("ACT_HL2MP_WALK_SLAM", (int)Activity.ACT_HL2MP_WALK_SLAM);
		AddActivityToSR("ACT_HL2MP_WALK_PHYSGUN", (int)Activity.ACT_HL2MP_WALK_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_WALK_RPG", (int)Activity.ACT_HL2MP_WALK_RPG);
		AddActivityToSR("ACT_HL2MP_WALK_AR2", (int)Activity.ACT_HL2MP_WALK_AR2);
		AddActivityToSR("ACT_HL2MP_WALK_CROSSBOW", (int)Activity.ACT_HL2MP_WALK_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_WALK_SHOTGUN", (int)Activity.ACT_HL2MP_WALK_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_WALK_GRENADE", (int)Activity.ACT_HL2MP_WALK_GRENADE);
		AddActivityToSR("ACT_HL2MP_SWIM", (int)Activity.ACT_HL2MP_SWIM);
		AddActivityToSR("ACT_HL2MP_SWIM_MELEE", (int)Activity.ACT_HL2MP_SWIM_MELEE);
		AddActivityToSR("ACT_HL2MP_SWIM_SMG1", (int)Activity.ACT_HL2MP_SWIM_SMG1);
		AddActivityToSR("ACT_HL2MP_SWIM_PISTOL", (int)Activity.ACT_HL2MP_SWIM_PISTOL);
		AddActivityToSR("ACT_HL2MP_SWIM_SLAM", (int)Activity.ACT_HL2MP_SWIM_SLAM);
		AddActivityToSR("ACT_HL2MP_SWIM_PHYSGUN", (int)Activity.ACT_HL2MP_SWIM_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_SWIM_RPG", (int)Activity.ACT_HL2MP_SWIM_RPG);
		AddActivityToSR("ACT_HL2MP_SWIM_AR2", (int)Activity.ACT_HL2MP_SWIM_AR2);
		AddActivityToSR("ACT_HL2MP_SWIM_CROSSBOW", (int)Activity.ACT_HL2MP_SWIM_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_SWIM_SHOTGUN", (int)Activity.ACT_HL2MP_SWIM_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_SWIM_GRENADE", (int)Activity.ACT_HL2MP_SWIM_GRENADE);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE", (int)Activity.ACT_HL2MP_SWIM_IDLE);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_MELEE", (int)Activity.ACT_HL2MP_SWIM_IDLE_MELEE);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_SMG1", (int)Activity.ACT_HL2MP_SWIM_IDLE_SMG1);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_PISTOL", (int)Activity.ACT_HL2MP_SWIM_IDLE_PISTOL);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_SLAM", (int)Activity.ACT_HL2MP_SWIM_IDLE_SLAM);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_PHYSGUN", (int)Activity.ACT_HL2MP_SWIM_IDLE_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_RPG", (int)Activity.ACT_HL2MP_SWIM_IDLE_RPG);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_AR2", (int)Activity.ACT_HL2MP_SWIM_IDLE_AR2);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_CROSSBOW", (int)Activity.ACT_HL2MP_SWIM_IDLE_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_SHOTGUN", (int)Activity.ACT_HL2MP_SWIM_IDLE_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_GRENADE", (int)Activity.ACT_HL2MP_SWIM_IDLE_GRENADE);
		AddActivityToSR("ACT_HL2MP_IDLE", (int)Activity.ACT_HL2MP_IDLE);
		AddActivityToSR("ACT_HL2MP_RUN", (int)Activity.ACT_HL2MP_RUN);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH", (int)Activity.ACT_HL2MP_IDLE_CROUCH);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH", (int)Activity.ACT_HL2MP_WALK_CROUCH);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD", (int)Activity.ACT_HL2MP_GESTURE_RELOAD);
		AddActivityToSR("ACT_HL2MP_JUMP", (int)Activity.ACT_HL2MP_JUMP);
		AddActivityToSR("ACT_HL2MP_IDLE_PISTOL", (int)Activity.ACT_HL2MP_IDLE_PISTOL);
		AddActivityToSR("ACT_HL2MP_RUN_PISTOL", (int)Activity.ACT_HL2MP_RUN_PISTOL);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_PISTOL", (int)Activity.ACT_HL2MP_IDLE_CROUCH_PISTOL);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_PISTOL", (int)Activity.ACT_HL2MP_WALK_CROUCH_PISTOL);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_PISTOL", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_PISTOL);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_PISTOL", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_PISTOL);
		AddActivityToSR("ACT_HL2MP_JUMP_PISTOL", (int)Activity.ACT_HL2MP_JUMP_PISTOL);
		AddActivityToSR("ACT_HL2MP_IDLE_SMG1", (int)Activity.ACT_HL2MP_IDLE_SMG1);
		AddActivityToSR("ACT_HL2MP_RUN_SMG1", (int)Activity.ACT_HL2MP_RUN_SMG1);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_SMG1", (int)Activity.ACT_HL2MP_IDLE_CROUCH_SMG1);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_SMG1", (int)Activity.ACT_HL2MP_WALK_CROUCH_SMG1);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_SMG1", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SMG1);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_SMG1", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_SMG1);
		AddActivityToSR("ACT_HL2MP_JUMP_SMG1", (int)Activity.ACT_HL2MP_JUMP_SMG1);
		AddActivityToSR("ACT_HL2MP_IDLE_AR2", (int)Activity.ACT_HL2MP_IDLE_AR2);
		AddActivityToSR("ACT_HL2MP_RUN_AR2", (int)Activity.ACT_HL2MP_RUN_AR2);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_AR2", (int)Activity.ACT_HL2MP_IDLE_CROUCH_AR2);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_AR2", (int)Activity.ACT_HL2MP_WALK_CROUCH_AR2);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_AR2", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_AR2);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_AR2", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_AR2);
		AddActivityToSR("ACT_HL2MP_JUMP_AR2", (int)Activity.ACT_HL2MP_JUMP_AR2);
		AddActivityToSR("ACT_HL2MP_IDLE_SHOTGUN", (int)Activity.ACT_HL2MP_IDLE_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_RUN_SHOTGUN", (int)Activity.ACT_HL2MP_RUN_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_SHOTGUN", (int)Activity.ACT_HL2MP_IDLE_CROUCH_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_SHOTGUN", (int)Activity.ACT_HL2MP_WALK_CROUCH_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_SHOTGUN", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_SHOTGUN", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_JUMP_SHOTGUN", (int)Activity.ACT_HL2MP_JUMP_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_IDLE_RPG", (int)Activity.ACT_HL2MP_IDLE_RPG);
		AddActivityToSR("ACT_HL2MP_RUN_RPG", (int)Activity.ACT_HL2MP_RUN_RPG);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_RPG", (int)Activity.ACT_HL2MP_IDLE_CROUCH_RPG);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_RPG", (int)Activity.ACT_HL2MP_WALK_CROUCH_RPG);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_RPG", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_RPG);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_RPG", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_RPG);
		AddActivityToSR("ACT_HL2MP_JUMP_RPG", (int)Activity.ACT_HL2MP_JUMP_RPG);
		AddActivityToSR("ACT_HL2MP_IDLE_GRENADE", (int)Activity.ACT_HL2MP_IDLE_GRENADE);
		AddActivityToSR("ACT_HL2MP_RUN_GRENADE", (int)Activity.ACT_HL2MP_RUN_GRENADE);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_GRENADE", (int)Activity.ACT_HL2MP_IDLE_CROUCH_GRENADE);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_GRENADE", (int)Activity.ACT_HL2MP_WALK_CROUCH_GRENADE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_GRENADE", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_GRENADE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_GRENADE", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_GRENADE);
		AddActivityToSR("ACT_HL2MP_JUMP_GRENADE", (int)Activity.ACT_HL2MP_JUMP_GRENADE);
		AddActivityToSR("ACT_HL2MP_IDLE_PHYSGUN", (int)Activity.ACT_HL2MP_IDLE_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_RUN_PHYSGUN", (int)Activity.ACT_HL2MP_RUN_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_PHYSGUN", (int)Activity.ACT_HL2MP_IDLE_CROUCH_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_PHYSGUN", (int)Activity.ACT_HL2MP_WALK_CROUCH_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_PHYSGUN", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_PHYSGUN", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_JUMP_PHYSGUN", (int)Activity.ACT_HL2MP_JUMP_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_IDLE_CROSSBOW", (int)Activity.ACT_HL2MP_IDLE_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_RUN_CROSSBOW", (int)Activity.ACT_HL2MP_RUN_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_CROSSBOW", (int)Activity.ACT_HL2MP_IDLE_CROUCH_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_CROSSBOW", (int)Activity.ACT_HL2MP_WALK_CROUCH_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_CROSSBOW", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_CROSSBOW", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_JUMP_CROSSBOW", (int)Activity.ACT_HL2MP_JUMP_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_IDLE_MELEE", (int)Activity.ACT_HL2MP_IDLE_MELEE);
		AddActivityToSR("ACT_HL2MP_RUN_MELEE", (int)Activity.ACT_HL2MP_RUN_MELEE);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_MELEE", (int)Activity.ACT_HL2MP_IDLE_CROUCH_MELEE);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_MELEE", (int)Activity.ACT_HL2MP_WALK_CROUCH_MELEE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_MELEE", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_MELEE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_MELEE", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_MELEE);
		AddActivityToSR("ACT_HL2MP_JUMP_MELEE", (int)Activity.ACT_HL2MP_JUMP_MELEE);
		AddActivityToSR("ACT_HL2MP_IDLE_SLAM", (int)Activity.ACT_HL2MP_IDLE_SLAM);
		AddActivityToSR("ACT_HL2MP_RUN_SLAM", (int)Activity.ACT_HL2MP_RUN_SLAM);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_SLAM", (int)Activity.ACT_HL2MP_IDLE_CROUCH_SLAM);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_SLAM", (int)Activity.ACT_HL2MP_WALK_CROUCH_SLAM);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_SLAM", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SLAM);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_SLAM", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_SLAM);
		AddActivityToSR("ACT_HL2MP_JUMP_SLAM", (int)Activity.ACT_HL2MP_JUMP_SLAM);
		AddActivityToSR("ACT_VM_FIZZLE", (int)Activity.ACT_VM_FIZZLE);
		AddActivityToSR("ACT_MP_STAND_IDLE", (int)Activity.ACT_MP_STAND_IDLE);
		AddActivityToSR("ACT_MP_CROUCH_IDLE", (int)Activity.ACT_MP_CROUCH_IDLE);
		AddActivityToSR("ACT_MP_CROUCH_DEPLOYED_IDLE", (int)Activity.ACT_MP_CROUCH_DEPLOYED_IDLE);
		AddActivityToSR("ACT_MP_CROUCH_DEPLOYED", (int)Activity.ACT_MP_CROUCH_DEPLOYED);
		AddActivityToSR("ACT_MP_DEPLOYED_IDLE", (int)Activity.ACT_MP_DEPLOYED_IDLE);
		AddActivityToSR("ACT_MP_RUN", (int)Activity.ACT_MP_RUN);
		AddActivityToSR("ACT_MP_WALK", (int)Activity.ACT_MP_WALK);
		AddActivityToSR("ACT_MP_AIRWALK", (int)Activity.ACT_MP_AIRWALK);
		AddActivityToSR("ACT_MP_CROUCHWALK", (int)Activity.ACT_MP_CROUCHWALK);
		AddActivityToSR("ACT_MP_SPRINT", (int)Activity.ACT_MP_SPRINT);
		AddActivityToSR("ACT_MP_JUMP", (int)Activity.ACT_MP_JUMP);
		AddActivityToSR("ACT_MP_JUMP_START", (int)Activity.ACT_MP_JUMP_START);
		AddActivityToSR("ACT_MP_JUMP_FLOAT", (int)Activity.ACT_MP_JUMP_FLOAT);
		AddActivityToSR("ACT_MP_JUMP_LAND", (int)Activity.ACT_MP_JUMP_LAND);
		AddActivityToSR("ACT_MP_DOUBLEJUMP", (int)Activity.ACT_MP_DOUBLEJUMP);
		AddActivityToSR("ACT_MP_SWIM", (int)Activity.ACT_MP_SWIM);
		AddActivityToSR("ACT_MP_SWIM_IDLE", (int)Activity.ACT_MP_SWIM_IDLE);
		AddActivityToSR("ACT_MP_DEPLOYED", (int)Activity.ACT_MP_DEPLOYED);
		AddActivityToSR("ACT_MP_SWIM_DEPLOYED", (int)Activity.ACT_MP_SWIM_DEPLOYED);
		AddActivityToSR("ACT_MP_VCD", (int)Activity.ACT_MP_VCD);
		AddActivityToSR("ACT_MP_ATTACK_STAND_PRIMARYFIRE", (int)Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE);
		AddActivityToSR("ACT_MP_ATTACK_STAND_PRIMARYFIRE_DEPLOYED", (int)Activity.ACT_MP_ATTACK_STAND_PRIMARYFIRE_DEPLOYED);
		AddActivityToSR("ACT_MP_ATTACK_STAND_SECONDARYFIRE", (int)Activity.ACT_MP_ATTACK_STAND_SECONDARYFIRE);
		AddActivityToSR("ACT_MP_ATTACK_STAND_GRENADE", (int)Activity.ACT_MP_ATTACK_STAND_GRENADE);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_PRIMARYFIRE", (int)Activity.ACT_MP_ATTACK_CROUCH_PRIMARYFIRE);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_PRIMARYFIRE_DEPLOYED", (int)Activity.ACT_MP_ATTACK_CROUCH_PRIMARYFIRE_DEPLOYED);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_SECONDARYFIRE", (int)Activity.ACT_MP_ATTACK_CROUCH_SECONDARYFIRE);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_GRENADE", (int)Activity.ACT_MP_ATTACK_CROUCH_GRENADE);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_PRIMARYFIRE", (int)Activity.ACT_MP_ATTACK_SWIM_PRIMARYFIRE);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_SECONDARYFIRE", (int)Activity.ACT_MP_ATTACK_SWIM_SECONDARYFIRE);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_GRENADE", (int)Activity.ACT_MP_ATTACK_SWIM_GRENADE);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE", (int)Activity.ACT_MP_ATTACK_AIRWALK_PRIMARYFIRE);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_SECONDARYFIRE", (int)Activity.ACT_MP_ATTACK_AIRWALK_SECONDARYFIRE);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_GRENADE", (int)Activity.ACT_MP_ATTACK_AIRWALK_GRENADE);
		AddActivityToSR("ACT_MP_RELOAD_STAND", (int)Activity.ACT_MP_RELOAD_STAND);
		AddActivityToSR("ACT_MP_RELOAD_STAND_LOOP", (int)Activity.ACT_MP_RELOAD_STAND_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_STAND_END", (int)Activity.ACT_MP_RELOAD_STAND_END);
		AddActivityToSR("ACT_MP_RELOAD_CROUCH", (int)Activity.ACT_MP_RELOAD_CROUCH);
		AddActivityToSR("ACT_MP_RELOAD_CROUCH_LOOP", (int)Activity.ACT_MP_RELOAD_CROUCH_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_CROUCH_END", (int)Activity.ACT_MP_RELOAD_CROUCH_END);
		AddActivityToSR("ACT_MP_RELOAD_SWIM", (int)Activity.ACT_MP_RELOAD_SWIM);
		AddActivityToSR("ACT_MP_RELOAD_SWIM_LOOP", (int)Activity.ACT_MP_RELOAD_SWIM_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_SWIM_END", (int)Activity.ACT_MP_RELOAD_SWIM_END);
		AddActivityToSR("ACT_MP_RELOAD_AIRWALK", (int)Activity.ACT_MP_RELOAD_AIRWALK);
		AddActivityToSR("ACT_MP_RELOAD_AIRWALK_LOOP", (int)Activity.ACT_MP_RELOAD_AIRWALK_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_AIRWALK_END", (int)Activity.ACT_MP_RELOAD_AIRWALK_END);
		AddActivityToSR("ACT_MP_ATTACK_STAND_PREFIRE", (int)Activity.ACT_MP_ATTACK_STAND_PREFIRE);
		AddActivityToSR("ACT_MP_ATTACK_STAND_POSTFIRE", (int)Activity.ACT_MP_ATTACK_STAND_POSTFIRE);
		AddActivityToSR("ACT_MP_ATTACK_STAND_STARTFIRE", (int)Activity.ACT_MP_ATTACK_STAND_STARTFIRE);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_PREFIRE", (int)Activity.ACT_MP_ATTACK_CROUCH_PREFIRE);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_POSTFIRE", (int)Activity.ACT_MP_ATTACK_CROUCH_POSTFIRE);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_PREFIRE", (int)Activity.ACT_MP_ATTACK_SWIM_PREFIRE);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_POSTFIRE", (int)Activity.ACT_MP_ATTACK_SWIM_POSTFIRE);
		AddActivityToSR("ACT_MP_STAND_PRIMARY", (int)Activity.ACT_MP_STAND_PRIMARY);
		AddActivityToSR("ACT_MP_CROUCH_PRIMARY", (int)Activity.ACT_MP_CROUCH_PRIMARY);
		AddActivityToSR("ACT_MP_RUN_PRIMARY", (int)Activity.ACT_MP_RUN_PRIMARY);
		AddActivityToSR("ACT_MP_WALK_PRIMARY", (int)Activity.ACT_MP_WALK_PRIMARY);
		AddActivityToSR("ACT_MP_AIRWALK_PRIMARY", (int)Activity.ACT_MP_AIRWALK_PRIMARY);
		AddActivityToSR("ACT_MP_CROUCHWALK_PRIMARY", (int)Activity.ACT_MP_CROUCHWALK_PRIMARY);
		AddActivityToSR("ACT_MP_JUMP_PRIMARY", (int)Activity.ACT_MP_JUMP_PRIMARY);
		AddActivityToSR("ACT_MP_JUMP_START_PRIMARY", (int)Activity.ACT_MP_JUMP_START_PRIMARY);
		AddActivityToSR("ACT_MP_JUMP_FLOAT_PRIMARY", (int)Activity.ACT_MP_JUMP_FLOAT_PRIMARY);
		AddActivityToSR("ACT_MP_JUMP_LAND_PRIMARY", (int)Activity.ACT_MP_JUMP_LAND_PRIMARY);
		AddActivityToSR("ACT_MP_SWIM_PRIMARY", (int)Activity.ACT_MP_SWIM_PRIMARY);
		AddActivityToSR("ACT_MP_DEPLOYED_PRIMARY", (int)Activity.ACT_MP_DEPLOYED_PRIMARY);
		AddActivityToSR("ACT_MP_SWIM_DEPLOYED_PRIMARY", (int)Activity.ACT_MP_SWIM_DEPLOYED_PRIMARY);
		AddActivityToSR("ACT_MP_ATTACK_STAND_PRIMARY", (int)Activity.ACT_MP_ATTACK_STAND_PRIMARY);
		AddActivityToSR("ACT_MP_ATTACK_STAND_PRIMARY_DEPLOYED", (int)Activity.ACT_MP_ATTACK_STAND_PRIMARY_DEPLOYED);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_PRIMARY", (int)Activity.ACT_MP_ATTACK_CROUCH_PRIMARY);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_PRIMARY_DEPLOYED", (int)Activity.ACT_MP_ATTACK_CROUCH_PRIMARY_DEPLOYED);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_PRIMARY", (int)Activity.ACT_MP_ATTACK_SWIM_PRIMARY);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_PRIMARY", (int)Activity.ACT_MP_ATTACK_AIRWALK_PRIMARY);
		AddActivityToSR("ACT_MP_RELOAD_STAND_PRIMARY", (int)Activity.ACT_MP_RELOAD_STAND_PRIMARY);
		AddActivityToSR("ACT_MP_RELOAD_STAND_PRIMARY_LOOP", (int)Activity.ACT_MP_RELOAD_STAND_PRIMARY_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_STAND_PRIMARY_END", (int)Activity.ACT_MP_RELOAD_STAND_PRIMARY_END);
		AddActivityToSR("ACT_MP_RELOAD_CROUCH_PRIMARY", (int)Activity.ACT_MP_RELOAD_CROUCH_PRIMARY);
		AddActivityToSR("ACT_MP_RELOAD_CROUCH_PRIMARY_LOOP", (int)Activity.ACT_MP_RELOAD_CROUCH_PRIMARY_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_CROUCH_PRIMARY_END", (int)Activity.ACT_MP_RELOAD_CROUCH_PRIMARY_END);
		AddActivityToSR("ACT_MP_RELOAD_SWIM_PRIMARY", (int)Activity.ACT_MP_RELOAD_SWIM_PRIMARY);
		AddActivityToSR("ACT_MP_RELOAD_SWIM_PRIMARY_LOOP", (int)Activity.ACT_MP_RELOAD_SWIM_PRIMARY_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_SWIM_PRIMARY_END", (int)Activity.ACT_MP_RELOAD_SWIM_PRIMARY_END);
		AddActivityToSR("ACT_MP_RELOAD_AIRWALK_PRIMARY", (int)Activity.ACT_MP_RELOAD_AIRWALK_PRIMARY);
		AddActivityToSR("ACT_MP_RELOAD_AIRWALK_PRIMARY_LOOP", (int)Activity.ACT_MP_RELOAD_AIRWALK_PRIMARY_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_AIRWALK_PRIMARY_END", (int)Activity.ACT_MP_RELOAD_AIRWALK_PRIMARY_END);
		AddActivityToSR("ACT_MP_ATTACK_STAND_GRENADE_PRIMARY", (int)Activity.ACT_MP_ATTACK_STAND_GRENADE_PRIMARY);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_GRENADE_PRIMARY", (int)Activity.ACT_MP_ATTACK_CROUCH_GRENADE_PRIMARY);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_GRENADE_PRIMARY", (int)Activity.ACT_MP_ATTACK_SWIM_GRENADE_PRIMARY);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_GRENADE_PRIMARY", (int)Activity.ACT_MP_ATTACK_AIRWALK_GRENADE_PRIMARY);
		AddActivityToSR("ACT_MP_STAND_SECONDARY", (int)Activity.ACT_MP_STAND_SECONDARY);
		AddActivityToSR("ACT_MP_CROUCH_SECONDARY", (int)Activity.ACT_MP_CROUCH_SECONDARY);
		AddActivityToSR("ACT_MP_RUN_SECONDARY", (int)Activity.ACT_MP_RUN_SECONDARY);
		AddActivityToSR("ACT_MP_WALK_SECONDARY", (int)Activity.ACT_MP_WALK_SECONDARY);
		AddActivityToSR("ACT_MP_AIRWALK_SECONDARY", (int)Activity.ACT_MP_AIRWALK_SECONDARY);
		AddActivityToSR("ACT_MP_CROUCHWALK_SECONDARY", (int)Activity.ACT_MP_CROUCHWALK_SECONDARY);
		AddActivityToSR("ACT_MP_JUMP_SECONDARY", (int)Activity.ACT_MP_JUMP_SECONDARY);
		AddActivityToSR("ACT_MP_JUMP_START_SECONDARY", (int)Activity.ACT_MP_JUMP_START_SECONDARY);
		AddActivityToSR("ACT_MP_JUMP_FLOAT_SECONDARY", (int)Activity.ACT_MP_JUMP_FLOAT_SECONDARY);
		AddActivityToSR("ACT_MP_JUMP_LAND_SECONDARY", (int)Activity.ACT_MP_JUMP_LAND_SECONDARY);
		AddActivityToSR("ACT_MP_SWIM_SECONDARY", (int)Activity.ACT_MP_SWIM_SECONDARY);
		AddActivityToSR("ACT_MP_ATTACK_STAND_SECONDARY", (int)Activity.ACT_MP_ATTACK_STAND_SECONDARY);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_SECONDARY", (int)Activity.ACT_MP_ATTACK_CROUCH_SECONDARY);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_SECONDARY", (int)Activity.ACT_MP_ATTACK_SWIM_SECONDARY);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_SECONDARY", (int)Activity.ACT_MP_ATTACK_AIRWALK_SECONDARY);
		AddActivityToSR("ACT_MP_RELOAD_STAND_SECONDARY", (int)Activity.ACT_MP_RELOAD_STAND_SECONDARY);
		AddActivityToSR("ACT_MP_RELOAD_STAND_SECONDARY_LOOP", (int)Activity.ACT_MP_RELOAD_STAND_SECONDARY_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_STAND_SECONDARY_END", (int)Activity.ACT_MP_RELOAD_STAND_SECONDARY_END);
		AddActivityToSR("ACT_MP_RELOAD_CROUCH_SECONDARY", (int)Activity.ACT_MP_RELOAD_CROUCH_SECONDARY);
		AddActivityToSR("ACT_MP_RELOAD_CROUCH_SECONDARY_LOOP", (int)Activity.ACT_MP_RELOAD_CROUCH_SECONDARY_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_CROUCH_SECONDARY_END", (int)Activity.ACT_MP_RELOAD_CROUCH_SECONDARY_END);
		AddActivityToSR("ACT_MP_RELOAD_SWIM_SECONDARY", (int)Activity.ACT_MP_RELOAD_SWIM_SECONDARY);
		AddActivityToSR("ACT_MP_RELOAD_SWIM_SECONDARY_LOOP", (int)Activity.ACT_MP_RELOAD_SWIM_SECONDARY_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_SWIM_SECONDARY_END", (int)Activity.ACT_MP_RELOAD_SWIM_SECONDARY_END);
		AddActivityToSR("ACT_MP_RELOAD_AIRWALK_SECONDARY", (int)Activity.ACT_MP_RELOAD_AIRWALK_SECONDARY);
		AddActivityToSR("ACT_MP_RELOAD_AIRWALK_SECONDARY_LOOP", (int)Activity.ACT_MP_RELOAD_AIRWALK_SECONDARY_LOOP);
		AddActivityToSR("ACT_MP_RELOAD_AIRWALK_SECONDARY_END", (int)Activity.ACT_MP_RELOAD_AIRWALK_SECONDARY_END);
		AddActivityToSR("ACT_MP_ATTACK_STAND_GRENADE_SECONDARY", (int)Activity.ACT_MP_ATTACK_STAND_GRENADE_SECONDARY);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_GRENADE_SECONDARY", (int)Activity.ACT_MP_ATTACK_CROUCH_GRENADE_SECONDARY);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_GRENADE_SECONDARY", (int)Activity.ACT_MP_ATTACK_SWIM_GRENADE_SECONDARY);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_GRENADE_SECONDARY", (int)Activity.ACT_MP_ATTACK_AIRWALK_GRENADE_SECONDARY);
		AddActivityToSR("ACT_MP_STAND_MELEE", (int)Activity.ACT_MP_STAND_MELEE);
		AddActivityToSR("ACT_MP_CROUCH_MELEE", (int)Activity.ACT_MP_CROUCH_MELEE);
		AddActivityToSR("ACT_MP_RUN_MELEE", (int)Activity.ACT_MP_RUN_MELEE);
		AddActivityToSR("ACT_MP_WALK_MELEE", (int)Activity.ACT_MP_WALK_MELEE);
		AddActivityToSR("ACT_MP_AIRWALK_MELEE", (int)Activity.ACT_MP_AIRWALK_MELEE);
		AddActivityToSR("ACT_MP_CROUCHWALK_MELEE", (int)Activity.ACT_MP_CROUCHWALK_MELEE);
		AddActivityToSR("ACT_MP_JUMP_MELEE", (int)Activity.ACT_MP_JUMP_MELEE);
		AddActivityToSR("ACT_MP_JUMP_START_MELEE", (int)Activity.ACT_MP_JUMP_START_MELEE);
		AddActivityToSR("ACT_MP_JUMP_FLOAT_MELEE", (int)Activity.ACT_MP_JUMP_FLOAT_MELEE);
		AddActivityToSR("ACT_MP_JUMP_LAND_MELEE", (int)Activity.ACT_MP_JUMP_LAND_MELEE);
		AddActivityToSR("ACT_MP_SWIM_MELEE", (int)Activity.ACT_MP_SWIM_MELEE);
		AddActivityToSR("ACT_MP_ATTACK_STAND_MELEE", (int)Activity.ACT_MP_ATTACK_STAND_MELEE);
		AddActivityToSR("ACT_MP_ATTACK_STAND_MELEE_SECONDARY", (int)Activity.ACT_MP_ATTACK_STAND_MELEE_SECONDARY);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_MELEE", (int)Activity.ACT_MP_ATTACK_CROUCH_MELEE);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_MELEE_SECONDARY", (int)Activity.ACT_MP_ATTACK_CROUCH_MELEE_SECONDARY);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_MELEE", (int)Activity.ACT_MP_ATTACK_SWIM_MELEE);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_MELEE", (int)Activity.ACT_MP_ATTACK_AIRWALK_MELEE);
		AddActivityToSR("ACT_MP_ATTACK_STAND_GRENADE_MELEE", (int)Activity.ACT_MP_ATTACK_STAND_GRENADE_MELEE);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_GRENADE_MELEE", (int)Activity.ACT_MP_ATTACK_CROUCH_GRENADE_MELEE);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_GRENADE_MELEE", (int)Activity.ACT_MP_ATTACK_SWIM_GRENADE_MELEE);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_GRENADE_MELEE", (int)Activity.ACT_MP_ATTACK_AIRWALK_GRENADE_MELEE);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH", (int)Activity.ACT_MP_GESTURE_FLINCH);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH_PRIMARY", (int)Activity.ACT_MP_GESTURE_FLINCH_PRIMARY);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH_SECONDARY", (int)Activity.ACT_MP_GESTURE_FLINCH_SECONDARY);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH_MELEE", (int)Activity.ACT_MP_GESTURE_FLINCH_MELEE);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH_HEAD", (int)Activity.ACT_MP_GESTURE_FLINCH_HEAD);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH_CHEST", (int)Activity.ACT_MP_GESTURE_FLINCH_CHEST);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH_STOMACH", (int)Activity.ACT_MP_GESTURE_FLINCH_STOMACH);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH_LEFTARM", (int)Activity.ACT_MP_GESTURE_FLINCH_LEFTARM);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH_RIGHTARM", (int)Activity.ACT_MP_GESTURE_FLINCH_RIGHTARM);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH_LEFTLEG", (int)Activity.ACT_MP_GESTURE_FLINCH_LEFTLEG);
		AddActivityToSR("ACT_MP_GESTURE_FLINCH_RIGHTLEG", (int)Activity.ACT_MP_GESTURE_FLINCH_RIGHTLEG);
		AddActivityToSR("ACT_MP_GRENADE1_DRAW", (int)Activity.ACT_MP_GRENADE1_DRAW);
		AddActivityToSR("ACT_MP_GRENADE1_IDLE", (int)Activity.ACT_MP_GRENADE1_IDLE);
		AddActivityToSR("ACT_MP_GRENADE1_ATTACK", (int)Activity.ACT_MP_GRENADE1_ATTACK);
		AddActivityToSR("ACT_MP_GRENADE2_DRAW", (int)Activity.ACT_MP_GRENADE2_DRAW);
		AddActivityToSR("ACT_MP_GRENADE2_IDLE", (int)Activity.ACT_MP_GRENADE2_IDLE);
		AddActivityToSR("ACT_MP_GRENADE2_ATTACK", (int)Activity.ACT_MP_GRENADE2_ATTACK);
		AddActivityToSR("ACT_MP_PRIMARY_GRENADE1_DRAW", (int)Activity.ACT_MP_PRIMARY_GRENADE1_DRAW);
		AddActivityToSR("ACT_MP_PRIMARY_GRENADE1_IDLE", (int)Activity.ACT_MP_PRIMARY_GRENADE1_IDLE);
		AddActivityToSR("ACT_MP_PRIMARY_GRENADE1_ATTACK", (int)Activity.ACT_MP_PRIMARY_GRENADE1_ATTACK);
		AddActivityToSR("ACT_MP_PRIMARY_GRENADE2_DRAW", (int)Activity.ACT_MP_PRIMARY_GRENADE2_DRAW);
		AddActivityToSR("ACT_MP_PRIMARY_GRENADE2_IDLE", (int)Activity.ACT_MP_PRIMARY_GRENADE2_IDLE);
		AddActivityToSR("ACT_MP_PRIMARY_GRENADE2_ATTACK", (int)Activity.ACT_MP_PRIMARY_GRENADE2_ATTACK);
		AddActivityToSR("ACT_MP_SECONDARY_GRENADE1_DRAW", (int)Activity.ACT_MP_SECONDARY_GRENADE1_DRAW);
		AddActivityToSR("ACT_MP_SECONDARY_GRENADE1_IDLE", (int)Activity.ACT_MP_SECONDARY_GRENADE1_IDLE);
		AddActivityToSR("ACT_MP_SECONDARY_GRENADE1_ATTACK", (int)Activity.ACT_MP_SECONDARY_GRENADE1_ATTACK);
		AddActivityToSR("ACT_MP_SECONDARY_GRENADE2_DRAW", (int)Activity.ACT_MP_SECONDARY_GRENADE2_DRAW);
		AddActivityToSR("ACT_MP_SECONDARY_GRENADE2_IDLE", (int)Activity.ACT_MP_SECONDARY_GRENADE2_IDLE);
		AddActivityToSR("ACT_MP_SECONDARY_GRENADE2_ATTACK", (int)Activity.ACT_MP_SECONDARY_GRENADE2_ATTACK);
		AddActivityToSR("ACT_MP_MELEE_GRENADE1_DRAW", (int)Activity.ACT_MP_MELEE_GRENADE1_DRAW);
		AddActivityToSR("ACT_MP_MELEE_GRENADE1_IDLE", (int)Activity.ACT_MP_MELEE_GRENADE1_IDLE);
		AddActivityToSR("ACT_MP_MELEE_GRENADE1_ATTACK", (int)Activity.ACT_MP_MELEE_GRENADE1_ATTACK);
		AddActivityToSR("ACT_MP_MELEE_GRENADE2_DRAW", (int)Activity.ACT_MP_MELEE_GRENADE2_DRAW);
		AddActivityToSR("ACT_MP_MELEE_GRENADE2_IDLE", (int)Activity.ACT_MP_MELEE_GRENADE2_IDLE);
		AddActivityToSR("ACT_MP_MELEE_GRENADE2_ATTACK", (int)Activity.ACT_MP_MELEE_GRENADE2_ATTACK);
		AddActivityToSR("ACT_MP_STAND_BUILDING", (int)Activity.ACT_MP_STAND_BUILDING);
		AddActivityToSR("ACT_MP_CROUCH_BUILDING", (int)Activity.ACT_MP_CROUCH_BUILDING);
		AddActivityToSR("ACT_MP_RUN_BUILDING", (int)Activity.ACT_MP_RUN_BUILDING);
		AddActivityToSR("ACT_MP_WALK_BUILDING", (int)Activity.ACT_MP_WALK_BUILDING);
		AddActivityToSR("ACT_MP_AIRWALK_BUILDING", (int)Activity.ACT_MP_AIRWALK_BUILDING);
		AddActivityToSR("ACT_MP_CROUCHWALK_BUILDING", (int)Activity.ACT_MP_CROUCHWALK_BUILDING);
		AddActivityToSR("ACT_MP_JUMP_BUILDING", (int)Activity.ACT_MP_JUMP_BUILDING);
		AddActivityToSR("ACT_MP_JUMP_START_BUILDING", (int)Activity.ACT_MP_JUMP_START_BUILDING);
		AddActivityToSR("ACT_MP_JUMP_FLOAT_BUILDING", (int)Activity.ACT_MP_JUMP_FLOAT_BUILDING);
		AddActivityToSR("ACT_MP_JUMP_LAND_BUILDING", (int)Activity.ACT_MP_JUMP_LAND_BUILDING);
		AddActivityToSR("ACT_MP_SWIM_BUILDING", (int)Activity.ACT_MP_SWIM_BUILDING);
		AddActivityToSR("ACT_MP_ATTACK_STAND_BUILDING", (int)Activity.ACT_MP_ATTACK_STAND_BUILDING);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_BUILDING", (int)Activity.ACT_MP_ATTACK_CROUCH_BUILDING);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_BUILDING", (int)Activity.ACT_MP_ATTACK_SWIM_BUILDING);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_BUILDING", (int)Activity.ACT_MP_ATTACK_AIRWALK_BUILDING);
		AddActivityToSR("ACT_MP_ATTACK_STAND_GRENADE_BUILDING", (int)Activity.ACT_MP_ATTACK_STAND_GRENADE_BUILDING);
		AddActivityToSR("ACT_MP_ATTACK_CROUCH_GRENADE_BUILDING", (int)Activity.ACT_MP_ATTACK_CROUCH_GRENADE_BUILDING);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_GRENADE_BUILDING", (int)Activity.ACT_MP_ATTACK_SWIM_GRENADE_BUILDING);
		AddActivityToSR("ACT_MP_ATTACK_AIRWALK_GRENADE_BUILDING", (int)Activity.ACT_MP_ATTACK_AIRWALK_GRENADE_BUILDING);
		AddActivityToSR("ACT_MP_STAND_PDA", (int)Activity.ACT_MP_STAND_PDA);
		AddActivityToSR("ACT_MP_CROUCH_PDA", (int)Activity.ACT_MP_CROUCH_PDA);
		AddActivityToSR("ACT_MP_RUN_PDA", (int)Activity.ACT_MP_RUN_PDA);
		AddActivityToSR("ACT_MP_WALK_PDA", (int)Activity.ACT_MP_WALK_PDA);
		AddActivityToSR("ACT_MP_AIRWALK_PDA", (int)Activity.ACT_MP_AIRWALK_PDA);
		AddActivityToSR("ACT_MP_CROUCHWALK_PDA", (int)Activity.ACT_MP_CROUCHWALK_PDA);
		AddActivityToSR("ACT_MP_JUMP_PDA", (int)Activity.ACT_MP_JUMP_PDA);
		AddActivityToSR("ACT_MP_JUMP_START_PDA", (int)Activity.ACT_MP_JUMP_START_PDA);
		AddActivityToSR("ACT_MP_JUMP_FLOAT_PDA", (int)Activity.ACT_MP_JUMP_FLOAT_PDA);
		AddActivityToSR("ACT_MP_JUMP_LAND_PDA", (int)Activity.ACT_MP_JUMP_LAND_PDA);
		AddActivityToSR("ACT_MP_SWIM_PDA", (int)Activity.ACT_MP_SWIM_PDA);
		AddActivityToSR("ACT_MP_ATTACK_STAND_PDA", (int)Activity.ACT_MP_ATTACK_STAND_PDA);
		AddActivityToSR("ACT_MP_ATTACK_SWIM_PDA", (int)Activity.ACT_MP_ATTACK_SWIM_PDA);
		AddActivityToSR("ACT_MP_GESTURE_VC_HANDMOUTH", (int)Activity.ACT_MP_GESTURE_VC_HANDMOUTH);
		AddActivityToSR("ACT_MP_GESTURE_VC_FINGERPOINT", (int)Activity.ACT_MP_GESTURE_VC_FINGERPOINT);
		AddActivityToSR("ACT_MP_GESTURE_VC_FISTPUMP", (int)Activity.ACT_MP_GESTURE_VC_FISTPUMP);
		AddActivityToSR("ACT_MP_GESTURE_VC_THUMBSUP", (int)Activity.ACT_MP_GESTURE_VC_THUMBSUP);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODYES", (int)Activity.ACT_MP_GESTURE_VC_NODYES);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODNO", (int)Activity.ACT_MP_GESTURE_VC_NODNO);
		AddActivityToSR("ACT_MP_GESTURE_VC_HANDMOUTH_PRIMARY", (int)Activity.ACT_MP_GESTURE_VC_HANDMOUTH_PRIMARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_FINGERPOINT_PRIMARY", (int)Activity.ACT_MP_GESTURE_VC_FINGERPOINT_PRIMARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_FISTPUMP_PRIMARY", (int)Activity.ACT_MP_GESTURE_VC_FISTPUMP_PRIMARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_THUMBSUP_PRIMARY", (int)Activity.ACT_MP_GESTURE_VC_THUMBSUP_PRIMARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODYES_PRIMARY", (int)Activity.ACT_MP_GESTURE_VC_NODYES_PRIMARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODNO_PRIMARY", (int)Activity.ACT_MP_GESTURE_VC_NODNO_PRIMARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_HANDMOUTH_SECONDARY", (int)Activity.ACT_MP_GESTURE_VC_HANDMOUTH_SECONDARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_FINGERPOINT_SECONDARY", (int)Activity.ACT_MP_GESTURE_VC_FINGERPOINT_SECONDARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_FISTPUMP_SECONDARY", (int)Activity.ACT_MP_GESTURE_VC_FISTPUMP_SECONDARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_THUMBSUP_SECONDARY", (int)Activity.ACT_MP_GESTURE_VC_THUMBSUP_SECONDARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODYES_SECONDARY", (int)Activity.ACT_MP_GESTURE_VC_NODYES_SECONDARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODNO_SECONDARY", (int)Activity.ACT_MP_GESTURE_VC_NODNO_SECONDARY);
		AddActivityToSR("ACT_MP_GESTURE_VC_HANDMOUTH_MELEE", (int)Activity.ACT_MP_GESTURE_VC_HANDMOUTH_MELEE);
		AddActivityToSR("ACT_MP_GESTURE_VC_FINGERPOINT_MELEE", (int)Activity.ACT_MP_GESTURE_VC_FINGERPOINT_MELEE);
		AddActivityToSR("ACT_MP_GESTURE_VC_FISTPUMP_MELEE", (int)Activity.ACT_MP_GESTURE_VC_FISTPUMP_MELEE);
		AddActivityToSR("ACT_MP_GESTURE_VC_THUMBSUP_MELEE", (int)Activity.ACT_MP_GESTURE_VC_THUMBSUP_MELEE);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODYES_MELEE", (int)Activity.ACT_MP_GESTURE_VC_NODYES_MELEE);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODNO_MELEE", (int)Activity.ACT_MP_GESTURE_VC_NODNO_MELEE);
		AddActivityToSR("ACT_MP_GESTURE_VC_HANDMOUTH_BUILDING", (int)Activity.ACT_MP_GESTURE_VC_HANDMOUTH_BUILDING);
		AddActivityToSR("ACT_MP_GESTURE_VC_FINGERPOINT_BUILDING", (int)Activity.ACT_MP_GESTURE_VC_FINGERPOINT_BUILDING);
		AddActivityToSR("ACT_MP_GESTURE_VC_FISTPUMP_BUILDING", (int)Activity.ACT_MP_GESTURE_VC_FISTPUMP_BUILDING);
		AddActivityToSR("ACT_MP_GESTURE_VC_THUMBSUP_BUILDING", (int)Activity.ACT_MP_GESTURE_VC_THUMBSUP_BUILDING);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODYES_BUILDING", (int)Activity.ACT_MP_GESTURE_VC_NODYES_BUILDING);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODNO_BUILDING", (int)Activity.ACT_MP_GESTURE_VC_NODNO_BUILDING);
		AddActivityToSR("ACT_MP_GESTURE_VC_HANDMOUTH_PDA", (int)Activity.ACT_MP_GESTURE_VC_HANDMOUTH_PDA);
		AddActivityToSR("ACT_MP_GESTURE_VC_FINGERPOINT_PDA", (int)Activity.ACT_MP_GESTURE_VC_FINGERPOINT_PDA);
		AddActivityToSR("ACT_MP_GESTURE_VC_FISTPUMP_PDA", (int)Activity.ACT_MP_GESTURE_VC_FISTPUMP_PDA);
		AddActivityToSR("ACT_MP_GESTURE_VC_THUMBSUP_PDA", (int)Activity.ACT_MP_GESTURE_VC_THUMBSUP_PDA);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODYES_PDA", (int)Activity.ACT_MP_GESTURE_VC_NODYES_PDA);
		AddActivityToSR("ACT_MP_GESTURE_VC_NODNO_PDA", (int)Activity.ACT_MP_GESTURE_VC_NODNO_PDA);
		AddActivityToSR("ACT_VM_UNUSABLE", (int)Activity.ACT_VM_UNUSABLE);
		AddActivityToSR("ACT_VM_UNUSABLE_TO_USABLE", (int)Activity.ACT_VM_UNUSABLE_TO_USABLE);
		AddActivityToSR("ACT_VM_USABLE_TO_UNUSABLE", (int)Activity.ACT_VM_USABLE_TO_UNUSABLE);
		AddActivityToSR("LAST_SHARED_ACTIVITY", (int)Activity.LAST_SHARED_ACTIVITY);
		AddActivityToSR("ACT_GMOD_NOCLIP_LAYER", (int)Activity.ACT_GMOD_NOCLIP_LAYER);
		AddActivityToSR("ACT_HL2MP_SIT", (int)Activity.ACT_HL2MP_SIT);
		AddActivityToSR("ACT_HL2MP_FIST_BLOCK", (int)Activity.ACT_HL2MP_FIST_BLOCK);
		AddActivityToSR("ACT_DRIVE_AIRBOAT", (int)Activity.ACT_DRIVE_AIRBOAT);
		AddActivityToSR("ACT_DRIVE_JEEP", (int)Activity.ACT_DRIVE_JEEP);
		AddActivityToSR("ACT_GMOD_SIT_ROLLERCOASTER", (int)Activity.ACT_GMOD_SIT_ROLLERCOASTER);
		AddActivityToSR("ACT_GMOD_GESTURE_ITEM_DROP", (int)Activity.ACT_GMOD_GESTURE_ITEM_DROP);
		AddActivityToSR("ACT_GMOD_GESTURE_ITEM_THROW", (int)Activity.ACT_GMOD_GESTURE_ITEM_THROW);
		AddActivityToSR("ACT_GMOD_GESTURE_ITEM_PLACE", (int)Activity.ACT_GMOD_GESTURE_ITEM_PLACE);
		AddActivityToSR("ACT_GMOD_GESTURE_ITEM_GIVE", (int)Activity.ACT_GMOD_GESTURE_ITEM_GIVE);
		AddActivityToSR("ACT_GMOD_GESTURE_MELEE_SHOVE_2HAND", (int)Activity.ACT_GMOD_GESTURE_MELEE_SHOVE_2HAND);
		AddActivityToSR("ACT_GMOD_GESTURE_MELEE_SHOVE_1HAND", (int)Activity.ACT_GMOD_GESTURE_MELEE_SHOVE_1HAND);
		AddActivityToSR("ACT_HL2MP_SIT_PISTOL", (int)Activity.ACT_HL2MP_SIT_PISTOL);
		AddActivityToSR("ACT_HL2MP_SIT_SHOTGUN", (int)Activity.ACT_HL2MP_SIT_SHOTGUN);
		AddActivityToSR("ACT_HL2MP_SIT_SMG1", (int)Activity.ACT_HL2MP_SIT_SMG1);
		AddActivityToSR("ACT_HL2MP_SIT_AR2", (int)Activity.ACT_HL2MP_SIT_AR2);
		AddActivityToSR("ACT_HL2MP_SIT_PHYSGUN", (int)Activity.ACT_HL2MP_SIT_PHYSGUN);
		AddActivityToSR("ACT_HL2MP_SIT_GRENADE", (int)Activity.ACT_HL2MP_SIT_GRENADE);
		AddActivityToSR("ACT_HL2MP_SIT_RPG", (int)Activity.ACT_HL2MP_SIT_RPG);
		AddActivityToSR("ACT_HL2MP_SIT_CROSSBOW", (int)Activity.ACT_HL2MP_SIT_CROSSBOW);
		AddActivityToSR("ACT_HL2MP_SIT_MELEE", (int)Activity.ACT_HL2MP_SIT_MELEE);
		AddActivityToSR("ACT_HL2MP_SIT_MELEE2", (int)Activity.ACT_HL2MP_SIT_MELEE2);
		AddActivityToSR("ACT_HL2MP_SIT_KNIFE", (int)Activity.ACT_HL2MP_SIT_KNIFE);
		AddActivityToSR("ACT_HL2MP_SIT_SLAM", (int)Activity.ACT_HL2MP_SIT_SLAM);
		AddActivityToSR("ACT_HL2MP_SIT_FIST", (int)Activity.ACT_HL2MP_SIT_FIST);
		AddActivityToSR("ACT_HL2MP_IDLE_FIST", (int)Activity.ACT_HL2MP_IDLE_FIST);
		AddActivityToSR("ACT_HL2MP_WALK_FIST", (int)Activity.ACT_HL2MP_WALK_FIST);
		AddActivityToSR("ACT_HL2MP_RUN_FIST", (int)Activity.ACT_HL2MP_RUN_FIST);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_FIST", (int)Activity.ACT_HL2MP_IDLE_CROUCH_FIST);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_FIST", (int)Activity.ACT_HL2MP_WALK_CROUCH_FIST);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_FIST", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_FIST);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_FIST", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_FIST);
		AddActivityToSR("ACT_HL2MP_JUMP_FIST", (int)Activity.ACT_HL2MP_JUMP_FIST);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_FIST", (int)Activity.ACT_HL2MP_SWIM_IDLE_FIST);
		AddActivityToSR("ACT_HL2MP_SWIM_FIST", (int)Activity.ACT_HL2MP_SWIM_FIST);
		AddActivityToSR("ACT_HL2MP_IDLE_KNIFE", (int)Activity.ACT_HL2MP_IDLE_KNIFE);
		AddActivityToSR("ACT_HL2MP_WALK_KNIFE", (int)Activity.ACT_HL2MP_WALK_KNIFE);
		AddActivityToSR("ACT_HL2MP_RUN_KNIFE", (int)Activity.ACT_HL2MP_RUN_KNIFE);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_KNIFE", (int)Activity.ACT_HL2MP_IDLE_CROUCH_KNIFE);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_KNIFE", (int)Activity.ACT_HL2MP_WALK_CROUCH_KNIFE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_KNIFE", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_KNIFE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_KNIFE", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_KNIFE);
		AddActivityToSR("ACT_HL2MP_JUMP_KNIFE", (int)Activity.ACT_HL2MP_JUMP_KNIFE);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_KNIFE", (int)Activity.ACT_HL2MP_SWIM_IDLE_KNIFE);
		AddActivityToSR("ACT_HL2MP_SWIM_KNIFE", (int)Activity.ACT_HL2MP_SWIM_KNIFE);
		AddActivityToSR("ACT_HL2MP_IDLE_PASSIVE", (int)Activity.ACT_HL2MP_IDLE_PASSIVE);
		AddActivityToSR("ACT_HL2MP_WALK_PASSIVE", (int)Activity.ACT_HL2MP_WALK_PASSIVE);
		AddActivityToSR("ACT_HL2MP_RUN_PASSIVE", (int)Activity.ACT_HL2MP_RUN_PASSIVE);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_PASSIVE", (int)Activity.ACT_HL2MP_IDLE_CROUCH_PASSIVE);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_PASSIVE", (int)Activity.ACT_HL2MP_WALK_CROUCH_PASSIVE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_PASSIVE", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_PASSIVE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_PASSIVE", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_PASSIVE);
		AddActivityToSR("ACT_HL2MP_JUMP_PASSIVE", (int)Activity.ACT_HL2MP_JUMP_PASSIVE);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_PASSIVE", (int)Activity.ACT_HL2MP_SWIM_IDLE_PASSIVE);
		AddActivityToSR("ACT_HL2MP_SWIM_PASSIVE", (int)Activity.ACT_HL2MP_SWIM_PASSIVE);
		AddActivityToSR("ACT_HL2MP_IDLE_MELEE2", (int)Activity.ACT_HL2MP_IDLE_MELEE2);
		AddActivityToSR("ACT_HL2MP_WALK_MELEE2", (int)Activity.ACT_HL2MP_WALK_MELEE2);
		AddActivityToSR("ACT_HL2MP_RUN_MELEE2", (int)Activity.ACT_HL2MP_RUN_MELEE2);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_MELEE2", (int)Activity.ACT_HL2MP_IDLE_CROUCH_MELEE2);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_MELEE2", (int)Activity.ACT_HL2MP_WALK_CROUCH_MELEE2);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_MELEE2", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_MELEE2);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_MELEE2", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_MELEE2);
		AddActivityToSR("ACT_HL2MP_JUMP_MELEE2", (int)Activity.ACT_HL2MP_JUMP_MELEE2);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_MELEE2", (int)Activity.ACT_HL2MP_SWIM_IDLE_MELEE2);
		AddActivityToSR("ACT_HL2MP_SWIM_MELEE2", (int)Activity.ACT_HL2MP_SWIM_MELEE2);
		AddActivityToSR("ACT_GMOD_IN_CHAT", (int)Activity.ACT_GMOD_IN_CHAT);
		AddActivityToSR("ACT_GMOD_GESTURE_AGREE", (int)Activity.ACT_GMOD_GESTURE_AGREE);
		AddActivityToSR("ACT_GMOD_GESTURE_BECON", (int)Activity.ACT_GMOD_GESTURE_BECON);
		AddActivityToSR("ACT_GMOD_GESTURE_BOW", (int)Activity.ACT_GMOD_GESTURE_BOW);
		AddActivityToSR("ACT_GMOD_GESTURE_DISAGREE", (int)Activity.ACT_GMOD_GESTURE_DISAGREE);
		AddActivityToSR("ACT_GMOD_TAUNT_SALUTE", (int)Activity.ACT_GMOD_TAUNT_SALUTE);
		AddActivityToSR("ACT_GMOD_GESTURE_WAVE", (int)Activity.ACT_GMOD_GESTURE_WAVE);
		AddActivityToSR("ACT_GMOD_TAUNT_PERSISTENCE", (int)Activity.ACT_GMOD_TAUNT_PERSISTENCE);
		AddActivityToSR("ACT_GMOD_TAUNT_MUSCLE", (int)Activity.ACT_GMOD_TAUNT_MUSCLE);
		AddActivityToSR("ACT_GMOD_TAUNT_LAUGH", (int)Activity.ACT_GMOD_TAUNT_LAUGH);
		AddActivityToSR("ACT_GMOD_GESTURE_POINT", (int)Activity.ACT_GMOD_GESTURE_POINT);
		AddActivityToSR("ACT_GMOD_TAUNT_CHEER", (int)Activity.ACT_GMOD_TAUNT_CHEER);
		AddActivityToSR("ACT_HL2MP_RUN_FAST", (int)Activity.ACT_HL2MP_RUN_FAST);
		AddActivityToSR("ACT_HL2MP_RUN_CHARGING", (int)Activity.ACT_HL2MP_RUN_CHARGING);
		AddActivityToSR("ACT_HL2MP_RUN_PANICKED", (int)Activity.ACT_HL2MP_RUN_PANICKED);
		AddActivityToSR("ACT_HL2MP_RUN_PROTECTED", (int)Activity.ACT_HL2MP_RUN_PROTECTED);
		AddActivityToSR("ACT_HL2MP_IDLE_MELEE_ANGRY", (int)Activity.ACT_HL2MP_IDLE_MELEE_ANGRY);
		AddActivityToSR("ACT_HL2MP_ZOMBIE_SLUMP_IDLE", (int)Activity.ACT_HL2MP_ZOMBIE_SLUMP_IDLE);
		AddActivityToSR("ACT_HL2MP_ZOMBIE_SLUMP_RISE", (int)Activity.ACT_HL2MP_ZOMBIE_SLUMP_RISE);
		AddActivityToSR("ACT_HL2MP_WALK_ZOMBIE_01", (int)Activity.ACT_HL2MP_WALK_ZOMBIE_01);
		AddActivityToSR("ACT_HL2MP_WALK_ZOMBIE_02", (int)Activity.ACT_HL2MP_WALK_ZOMBIE_02);
		AddActivityToSR("ACT_HL2MP_WALK_ZOMBIE_03", (int)Activity.ACT_HL2MP_WALK_ZOMBIE_03);
		AddActivityToSR("ACT_HL2MP_WALK_ZOMBIE_04", (int)Activity.ACT_HL2MP_WALK_ZOMBIE_04);
		AddActivityToSR("ACT_HL2MP_WALK_ZOMBIE_05", (int)Activity.ACT_HL2MP_WALK_ZOMBIE_05);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_ZOMBIE_01", (int)Activity.ACT_HL2MP_WALK_CROUCH_ZOMBIE_01);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_ZOMBIE_02", (int)Activity.ACT_HL2MP_WALK_CROUCH_ZOMBIE_02);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_ZOMBIE_03", (int)Activity.ACT_HL2MP_WALK_CROUCH_ZOMBIE_03);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_ZOMBIE_04", (int)Activity.ACT_HL2MP_WALK_CROUCH_ZOMBIE_04);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_ZOMBIE_05", (int)Activity.ACT_HL2MP_WALK_CROUCH_ZOMBIE_05);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_ZOMBIE_01", (int)Activity.ACT_HL2MP_IDLE_CROUCH_ZOMBIE_01);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_ZOMBIE_02", (int)Activity.ACT_HL2MP_IDLE_CROUCH_ZOMBIE_02);
		AddActivityToSR("ACT_GMOD_GESTURE_RANGE_ZOMBIE", (int)Activity.ACT_GMOD_GESTURE_RANGE_ZOMBIE);
		AddActivityToSR("ACT_GMOD_GESTURE_TAUNT_ZOMBIE", (int)Activity.ACT_GMOD_GESTURE_TAUNT_ZOMBIE);
		AddActivityToSR("ACT_GMOD_TAUNT_DANCE", (int)Activity.ACT_GMOD_TAUNT_DANCE);
		AddActivityToSR("ACT_GMOD_TAUNT_ROBOT", (int)Activity.ACT_GMOD_TAUNT_ROBOT);
		AddActivityToSR("ACT_GMOD_GESTURE_RANGE_ZOMBIE_SPECIAL", (int)Activity.ACT_GMOD_GESTURE_RANGE_ZOMBIE_SPECIAL);
		AddActivityToSR("ACT_GMOD_GESTURE_RANGE_FRENZY", (int)Activity.ACT_GMOD_GESTURE_RANGE_FRENZY);
		AddActivityToSR("ACT_HL2MP_RUN_ZOMBIE_FAST", (int)Activity.ACT_HL2MP_RUN_ZOMBIE_FAST);
		AddActivityToSR("ACT_HL2MP_WALK_ZOMBIE_06", (int)Activity.ACT_HL2MP_WALK_ZOMBIE_06);
		AddActivityToSR("ACT_ZOMBIE_LEAP_START", (int)Activity.ACT_ZOMBIE_LEAP_START);
		AddActivityToSR("ACT_ZOMBIE_LEAPING", (int)Activity.ACT_ZOMBIE_LEAPING);
		AddActivityToSR("ACT_ZOMBIE_CLIMB_UP", (int)Activity.ACT_ZOMBIE_CLIMB_UP);
		AddActivityToSR("ACT_ZOMBIE_CLIMB_START", (int)Activity.ACT_ZOMBIE_CLIMB_START);
		AddActivityToSR("ACT_ZOMBIE_CLIMB_END", (int)Activity.ACT_ZOMBIE_CLIMB_END);
		AddActivityToSR("ACT_HL2MP_IDLE_MAGIC", (int)Activity.ACT_HL2MP_IDLE_MAGIC);
		AddActivityToSR("ACT_HL2MP_WALK_MAGIC", (int)Activity.ACT_HL2MP_WALK_MAGIC);
		AddActivityToSR("ACT_HL2MP_RUN_MAGIC", (int)Activity.ACT_HL2MP_RUN_MAGIC);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_MAGIC", (int)Activity.ACT_HL2MP_IDLE_CROUCH_MAGIC);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_MAGIC", (int)Activity.ACT_HL2MP_WALK_CROUCH_MAGIC);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_MAGIC", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_MAGIC);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_MAGIC", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_MAGIC);
		AddActivityToSR("ACT_HL2MP_JUMP_MAGIC", (int)Activity.ACT_HL2MP_JUMP_MAGIC);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_MAGIC", (int)Activity.ACT_HL2MP_SWIM_IDLE_MAGIC);
		AddActivityToSR("ACT_HL2MP_SWIM_MAGIC", (int)Activity.ACT_HL2MP_SWIM_MAGIC);
		AddActivityToSR("ACT_HL2MP_IDLE_REVOLVER", (int)Activity.ACT_HL2MP_IDLE_REVOLVER);
		AddActivityToSR("ACT_HL2MP_WALK_REVOLVER", (int)Activity.ACT_HL2MP_WALK_REVOLVER);
		AddActivityToSR("ACT_HL2MP_RUN_REVOLVER", (int)Activity.ACT_HL2MP_RUN_REVOLVER);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_REVOLVER", (int)Activity.ACT_HL2MP_IDLE_CROUCH_REVOLVER);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_REVOLVER", (int)Activity.ACT_HL2MP_WALK_CROUCH_REVOLVER);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_REVOLVER", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_REVOLVER);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_REVOLVER", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_REVOLVER);
		AddActivityToSR("ACT_HL2MP_JUMP_REVOLVER", (int)Activity.ACT_HL2MP_JUMP_REVOLVER);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_REVOLVER", (int)Activity.ACT_HL2MP_SWIM_IDLE_REVOLVER);
		AddActivityToSR("ACT_HL2MP_SWIM_REVOLVER", (int)Activity.ACT_HL2MP_SWIM_REVOLVER);
		AddActivityToSR("ACT_HL2MP_IDLE_CAMERA", (int)Activity.ACT_HL2MP_IDLE_CAMERA);
		AddActivityToSR("ACT_HL2MP_WALK_CAMERA", (int)Activity.ACT_HL2MP_WALK_CAMERA);
		AddActivityToSR("ACT_HL2MP_RUN_CAMERA", (int)Activity.ACT_HL2MP_RUN_CAMERA);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_CAMERA", (int)Activity.ACT_HL2MP_IDLE_CROUCH_CAMERA);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_CAMERA", (int)Activity.ACT_HL2MP_WALK_CROUCH_CAMERA);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_CAMERA", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_CAMERA);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_CAMERA", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_CAMERA);
		AddActivityToSR("ACT_HL2MP_JUMP_CAMERA", (int)Activity.ACT_HL2MP_JUMP_CAMERA);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_CAMERA", (int)Activity.ACT_HL2MP_SWIM_IDLE_CAMERA);
		AddActivityToSR("ACT_HL2MP_SWIM_CAMERA", (int)Activity.ACT_HL2MP_SWIM_CAMERA);
		AddActivityToSR("ACT_HL2MP_IDLE_ANGRY", (int)Activity.ACT_HL2MP_IDLE_ANGRY);
		AddActivityToSR("ACT_HL2MP_WALK_ANGRY", (int)Activity.ACT_HL2MP_WALK_ANGRY);
		AddActivityToSR("ACT_HL2MP_RUN_ANGRY", (int)Activity.ACT_HL2MP_RUN_ANGRY);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_ANGRY", (int)Activity.ACT_HL2MP_IDLE_CROUCH_ANGRY);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_ANGRY", (int)Activity.ACT_HL2MP_WALK_CROUCH_ANGRY);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_ANGRY", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_ANGRY);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_ANGRY", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_ANGRY);
		AddActivityToSR("ACT_HL2MP_JUMP_ANGRY", (int)Activity.ACT_HL2MP_JUMP_ANGRY);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_ANGRY", (int)Activity.ACT_HL2MP_SWIM_IDLE_ANGRY);
		AddActivityToSR("ACT_HL2MP_SWIM_ANGRY", (int)Activity.ACT_HL2MP_SWIM_ANGRY);
		AddActivityToSR("ACT_HL2MP_IDLE_SCARED", (int)Activity.ACT_HL2MP_IDLE_SCARED);
		AddActivityToSR("ACT_HL2MP_WALK_SCARED", (int)Activity.ACT_HL2MP_WALK_SCARED);
		AddActivityToSR("ACT_HL2MP_RUN_SCARED", (int)Activity.ACT_HL2MP_RUN_SCARED);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_SCARED", (int)Activity.ACT_HL2MP_IDLE_CROUCH_SCARED);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_SCARED", (int)Activity.ACT_HL2MP_WALK_CROUCH_SCARED);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_SCARED", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SCARED);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_SCARED", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_SCARED);
		AddActivityToSR("ACT_HL2MP_JUMP_SCARED", (int)Activity.ACT_HL2MP_JUMP_SCARED);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_SCARED", (int)Activity.ACT_HL2MP_SWIM_IDLE_SCARED);
		AddActivityToSR("ACT_HL2MP_SWIM_SCARED", (int)Activity.ACT_HL2MP_SWIM_SCARED);
		AddActivityToSR("ACT_HL2MP_IDLE_ZOMBIE", (int)Activity.ACT_HL2MP_IDLE_ZOMBIE);
		AddActivityToSR("ACT_HL2MP_WALK_ZOMBIE", (int)Activity.ACT_HL2MP_WALK_ZOMBIE);
		AddActivityToSR("ACT_HL2MP_RUN_ZOMBIE", (int)Activity.ACT_HL2MP_RUN_ZOMBIE);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_ZOMBIE", (int)Activity.ACT_HL2MP_IDLE_CROUCH_ZOMBIE);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_ZOMBIE", (int)Activity.ACT_HL2MP_WALK_CROUCH_ZOMBIE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_ZOMBIE", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_ZOMBIE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_ZOMBIE", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_ZOMBIE);
		AddActivityToSR("ACT_HL2MP_JUMP_ZOMBIE", (int)Activity.ACT_HL2MP_JUMP_ZOMBIE);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_ZOMBIE", (int)Activity.ACT_HL2MP_SWIM_IDLE_ZOMBIE);
		AddActivityToSR("ACT_HL2MP_SWIM_ZOMBIE", (int)Activity.ACT_HL2MP_SWIM_ZOMBIE);
		AddActivityToSR("ACT_HL2MP_IDLE_SUITCASE", (int)Activity.ACT_HL2MP_IDLE_SUITCASE);
		AddActivityToSR("ACT_HL2MP_WALK_SUITCASE", (int)Activity.ACT_HL2MP_WALK_SUITCASE);
		AddActivityToSR("ACT_HL2MP_RUN_SUITCASE", (int)Activity.ACT_HL2MP_RUN_SUITCASE);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_SUITCASE", (int)Activity.ACT_HL2MP_IDLE_CROUCH_SUITCASE);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_SUITCASE", (int)Activity.ACT_HL2MP_WALK_CROUCH_SUITCASE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_SUITCASE", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_SUITCASE);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_SUITCASE", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_SUITCASE);
		AddActivityToSR("ACT_HL2MP_JUMP_SUITCASE", (int)Activity.ACT_HL2MP_JUMP_SUITCASE);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_SUITCASE", (int)Activity.ACT_HL2MP_SWIM_IDLE_SUITCASE);
		AddActivityToSR("ACT_HL2MP_SWIM_SUITCASE", (int)Activity.ACT_HL2MP_SWIM_SUITCASE);
		AddActivityToSR("ACT_HL2MP_IDLE_DUEL", (int)Activity.ACT_HL2MP_IDLE_DUEL);
		AddActivityToSR("ACT_HL2MP_WALK_DUEL", (int)Activity.ACT_HL2MP_WALK_DUEL);
		AddActivityToSR("ACT_HL2MP_RUN_DUEL", (int)Activity.ACT_HL2MP_RUN_DUEL);
		AddActivityToSR("ACT_HL2MP_IDLE_CROUCH_DUEL", (int)Activity.ACT_HL2MP_IDLE_CROUCH_DUEL);
		AddActivityToSR("ACT_HL2MP_WALK_CROUCH_DUEL", (int)Activity.ACT_HL2MP_WALK_CROUCH_DUEL);
		AddActivityToSR("ACT_HL2MP_GESTURE_RANGE_ATTACK_DUEL", (int)Activity.ACT_HL2MP_GESTURE_RANGE_ATTACK_DUEL);
		AddActivityToSR("ACT_HL2MP_GESTURE_RELOAD_DUEL", (int)Activity.ACT_HL2MP_GESTURE_RELOAD_DUEL);
		AddActivityToSR("ACT_HL2MP_JUMP_DUEL", (int)Activity.ACT_HL2MP_JUMP_DUEL);
		AddActivityToSR("ACT_HL2MP_SWIM_IDLE_DUEL", (int)Activity.ACT_HL2MP_SWIM_IDLE_DUEL);
		AddActivityToSR("ACT_HL2MP_SWIM_DUEL", (int)Activity.ACT_HL2MP_SWIM_DUEL);
		AddActivityToSR("ACT_VM_CRAWL", (int)Activity.ACT_VM_CRAWL);
		AddActivityToSR("ACT_VM_CRAWL_EMPTY", (int)Activity.ACT_VM_CRAWL_EMPTY);
		AddActivityToSR("ACT_VM_HOLSTER_EMPTY", (int)Activity.ACT_VM_HOLSTER_EMPTY);
		AddActivityToSR("ACT_VM_DOWN", (int)Activity.ACT_VM_DOWN);
		AddActivityToSR("ACT_VM_DOWN_EMPTY", (int)Activity.ACT_VM_DOWN_EMPTY);
		AddActivityToSR("ACT_VM_READY", (int)Activity.ACT_VM_READY);
		AddActivityToSR("ACT_VM_ISHOOT", (int)Activity.ACT_VM_ISHOOT);
		AddActivityToSR("ACT_VM_IIN", (int)Activity.ACT_VM_IIN);
		AddActivityToSR("ACT_VM_IIN_EMPTY", (int)Activity.ACT_VM_IIN_EMPTY);
		AddActivityToSR("ACT_VM_IIDLE", (int)Activity.ACT_VM_IIDLE);
		AddActivityToSR("ACT_VM_IIDLE_EMPTY", (int)Activity.ACT_VM_IIDLE_EMPTY);
		AddActivityToSR("ACT_VM_IOUT", (int)Activity.ACT_VM_IOUT);
		AddActivityToSR("ACT_VM_IOUT_EMPTY", (int)Activity.ACT_VM_IOUT_EMPTY);
		AddActivityToSR("ACT_VM_PULLBACK_HIGH_BAKE", (int)Activity.ACT_VM_PULLBACK_HIGH_BAKE);
		AddActivityToSR("ACT_VM_HITKILL", (int)Activity.ACT_VM_HITKILL);
		AddActivityToSR("ACT_VM_DEPLOYED_IN", (int)Activity.ACT_VM_DEPLOYED_IN);
		AddActivityToSR("ACT_VM_DEPLOYED_IDLE", (int)Activity.ACT_VM_DEPLOYED_IDLE);
		AddActivityToSR("ACT_VM_DEPLOYED_FIRE", (int)Activity.ACT_VM_DEPLOYED_FIRE);
		AddActivityToSR("ACT_VM_DEPLOYED_DRYFIRE", (int)Activity.ACT_VM_DEPLOYED_DRYFIRE);
		AddActivityToSR("ACT_VM_DEPLOYED_RELOAD", (int)Activity.ACT_VM_DEPLOYED_RELOAD);
		AddActivityToSR("ACT_VM_DEPLOYED_RELOAD_EMPTY", (int)Activity.ACT_VM_DEPLOYED_RELOAD_EMPTY);
		AddActivityToSR("ACT_VM_DEPLOYED_OUT", (int)Activity.ACT_VM_DEPLOYED_OUT);
		AddActivityToSR("ACT_VM_DEPLOYED_IRON_IN", (int)Activity.ACT_VM_DEPLOYED_IRON_IN);
		AddActivityToSR("ACT_VM_DEPLOYED_IRON_IDLE", (int)Activity.ACT_VM_DEPLOYED_IRON_IDLE);
		AddActivityToSR("ACT_VM_DEPLOYED_IRON_FIRE", (int)Activity.ACT_VM_DEPLOYED_IRON_FIRE);
		AddActivityToSR("ACT_VM_DEPLOYED_IRON_DRYFIRE", (int)Activity.ACT_VM_DEPLOYED_IRON_DRYFIRE);
		AddActivityToSR("ACT_VM_DEPLOYED_IRON_OUT", (int)Activity.ACT_VM_DEPLOYED_IRON_OUT);
		AddActivityToSR("ACT_VM_DEPLOYED_LIFTED_IN", (int)Activity.ACT_VM_DEPLOYED_LIFTED_IN);
		AddActivityToSR("ACT_VM_DEPLOYED_LIFTED_IDLE", (int)Activity.ACT_VM_DEPLOYED_LIFTED_IDLE);
		AddActivityToSR("ACT_VM_DEPLOYED_LIFTED_OUT", (int)Activity.ACT_VM_DEPLOYED_LIFTED_OUT);
		AddActivityToSR("ACT_VM_RELOADEMPTY", (int)Activity.ACT_VM_RELOADEMPTY);
		AddActivityToSR("ACT_VM_IRECOIL1", (int)Activity.ACT_VM_IRECOIL1);
		AddActivityToSR("ACT_VM_IRECOIL2", (int)Activity.ACT_VM_IRECOIL2);
		AddActivityToSR("ACT_VM_FIREMODE", (int)Activity.ACT_VM_FIREMODE);
		AddActivityToSR("ACT_VM_ISHOOT_LAST", (int)Activity.ACT_VM_ISHOOT_LAST);
		AddActivityToSR("ACT_VM_IFIREMODE", (int)Activity.ACT_VM_IFIREMODE);
		AddActivityToSR("ACT_VM_DFIREMODE", (int)Activity.ACT_VM_DFIREMODE);
		AddActivityToSR("ACT_VM_DIFIREMODE", (int)Activity.ACT_VM_DIFIREMODE);
		AddActivityToSR("ACT_VM_SHOOTLAST", (int)Activity.ACT_VM_SHOOTLAST);
		AddActivityToSR("ACT_VM_ISHOOTDRY", (int)Activity.ACT_VM_ISHOOTDRY);
		AddActivityToSR("ACT_VM_DRAW_M203", (int)Activity.ACT_VM_DRAW_M203);
		AddActivityToSR("ACT_VM_DRAWFULL_M203", (int)Activity.ACT_VM_DRAWFULL_M203);
		AddActivityToSR("ACT_VM_READY_M203", (int)Activity.ACT_VM_READY_M203);
		AddActivityToSR("ACT_VM_IDLE_M203", (int)Activity.ACT_VM_IDLE_M203);
		AddActivityToSR("ACT_VM_RELOAD_M203", (int)Activity.ACT_VM_RELOAD_M203);
		AddActivityToSR("ACT_VM_HOLSTER_M203", (int)Activity.ACT_VM_HOLSTER_M203);
		AddActivityToSR("ACT_VM_HOLSTERFULL_M203", (int)Activity.ACT_VM_HOLSTERFULL_M203);
		AddActivityToSR("ACT_VM_IIN_M203", (int)Activity.ACT_VM_IIN_M203);
		AddActivityToSR("ACT_VM_IIDLE_M203", (int)Activity.ACT_VM_IIDLE_M203);
		AddActivityToSR("ACT_VM_IOUT_M203", (int)Activity.ACT_VM_IOUT_M203);
		AddActivityToSR("ACT_VM_CRAWL_M203", (int)Activity.ACT_VM_CRAWL_M203);
		AddActivityToSR("ACT_VM_DOWN_M203", (int)Activity.ACT_VM_DOWN_M203);
		AddActivityToSR("ACT_VM_ISHOOT_M203", (int)Activity.ACT_VM_ISHOOT_M203);
		AddActivityToSR("ACT_VM_RELOAD_INSERT", (int)Activity.ACT_VM_RELOAD_INSERT);
		AddActivityToSR("ACT_VM_RELOAD_INSERT_PULL", (int)Activity.ACT_VM_RELOAD_INSERT_PULL);
		AddActivityToSR("ACT_VM_RELOAD_END", (int)Activity.ACT_VM_RELOAD_END);
		AddActivityToSR("ACT_VM_RELOAD_END_EMPTY", (int)Activity.ACT_VM_RELOAD_END_EMPTY);
		AddActivityToSR("ACT_VM_RELOAD_INSERT_EMPTY", (int)Activity.ACT_VM_RELOAD_INSERT_EMPTY);
		AddActivityToSR("ACT_CROSSBOW_HOLSTER_UNLOADED", (int)Activity.ACT_CROSSBOW_HOLSTER_UNLOADED);
		AddActivityToSR("ACT_VM_FIRE_TO_EMPTY", (int)Activity.ACT_VM_FIRE_TO_EMPTY);
		AddActivityToSR("ACT_VM_UNLOAD", (int)Activity.ACT_VM_UNLOAD);
		AddActivityToSR("ACT_VM_RELOAD2", (int)Activity.ACT_VM_RELOAD2);
		AddActivityToSR("ACT_DRIVE_POD", (int)Activity.ACT_DRIVE_POD);
		AddActivityToSR("ACT_GMOD_DEATH", (int)Activity.ACT_GMOD_DEATH);
		AddActivityToSR("ACT_FLINCH", (int)Activity.ACT_FLINCH);
		AddActivityToSR("ACT_FLINCH_BACK", (int)Activity.ACT_FLINCH_BACK);
		AddActivityToSR("ACT_FLINCH_SHOULDER_LEFT", (int)Activity.ACT_FLINCH_SHOULDER_LEFT);
		AddActivityToSR("ACT_FLINCH_SHOULDER_RIGHT", (int)Activity.ACT_FLINCH_SHOULDER_RIGHT);
		AddActivityToSR("ACT_HL2MP_SIT_CAMERA", (int)Activity.ACT_HL2MP_SIT_CAMERA);
		AddActivityToSR("ACT_HL2MP_SIT_PASSIVE", (int)Activity.ACT_HL2MP_SIT_PASSIVE);
		AddActivityToSR("ACT_HL2MP_IDLE_COWER", (int)Activity.ACT_HL2MP_IDLE_COWER);
		AddActivityToSR("ACT_HL2MP_ZOMBIE_SLUMP_ALT_IDLE", (int)Activity.ACT_HL2MP_ZOMBIE_SLUMP_ALT_IDLE);
		AddActivityToSR("ACT_HL2MP_ZOMBIE_SLUMP_ALT_RISE_FAST", (int)Activity.ACT_HL2MP_ZOMBIE_SLUMP_ALT_RISE_FAST);
		AddActivityToSR("ACT_HL2MP_ZOMBIE_SLUMP_ALT_RISE_SLOW", (int)Activity.ACT_HL2MP_ZOMBIE_SLUMP_ALT_RISE_SLOW);
		AddActivityToSR("ACT_GMOD_SHOWOFF_STAND_01", (int)Activity.ACT_GMOD_SHOWOFF_STAND_01);
		AddActivityToSR("ACT_GMOD_SHOWOFF_STAND_02", (int)Activity.ACT_GMOD_SHOWOFF_STAND_02);
		AddActivityToSR("ACT_GMOD_SHOWOFF_STAND_03", (int)Activity.ACT_GMOD_SHOWOFF_STAND_03);
		AddActivityToSR("ACT_GMOD_SHOWOFF_STAND_04", (int)Activity.ACT_GMOD_SHOWOFF_STAND_04);
		AddActivityToSR("ACT_GMOD_SHOWOFF_DUCK_01", (int)Activity.ACT_GMOD_SHOWOFF_DUCK_01);
		AddActivityToSR("ACT_GMOD_SHOWOFF_DUCK_02", (int)Activity.ACT_GMOD_SHOWOFF_DUCK_02);
	}

	public static void InitSchedulingTables() {
		ClassScheduleIdSpace.Init("CAI_BaseNPC", GetSchedulingSymbols());
		InitDefaultScheduleSR();
		InitDefaultConditionSR();
		InitDefaultTaskSR();
		InitDefaultActivitySR();
	}

	public static bool LoadDefaultSchedules() {
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_IDLE_STAND, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_IDLE_WALK, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_IDLE_WANDER, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_WAKE_ANGRY, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_ALERT_FACE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_ALERT_FACE_BESTSOUND, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_ALERT_REACT_TO_COMBAT_SOUND, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_ALERT_SCAN, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_ALERT_STAND, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_ALERT_WALK, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_INVESTIGATE_SOUND, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_COMBAT_FACE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_COMBAT_SWEEP, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_COMBAT_WALK, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_FEAR_FACE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_COMBAT_STAND, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_CHASE_ENEMY, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_CHASE_ENEMY_FAILED, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_VICTORY_DANCE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_TARGET_FACE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_TARGET_CHASE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SMALL_FLINCH, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_BIG_FLINCH, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_BACK_AWAY_FROM_ENEMY, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_MOVE_AWAY_FROM_ENEMY, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_BACK_AWAY_FROM_SAVE_POSITION, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_TAKE_COVER_FROM_ENEMY, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_TAKE_COVER_FROM_BEST_SOUND, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_FLEE_FROM_BEST_SOUND, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_TAKE_COVER_FROM_ORIGIN, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_FAIL_TAKE_COVER, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_RUN_FROM_ENEMY, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_RUN_FROM_ENEMY_FALLBACK, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_MOVE_TO_WEAPON_RANGE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_ESTABLISH_LINE_OF_FIRE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SHOOT_ENEMY_COVER, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_ESTABLISH_LINE_OF_FIRE_FALLBACK, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_PRE_FAIL_ESTABLISH_LINE_OF_FIRE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_FAIL_ESTABLISH_LINE_OF_FIRE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_COWER, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_MELEE_ATTACK1, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_MELEE_ATTACK2, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_RANGE_ATTACK1, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_RANGE_ATTACK2, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SPECIAL_ATTACK1, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SPECIAL_ATTACK2, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_STANDOFF, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_ARM_WEAPON, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_DISARM_WEAPON, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_HIDE_AND_RELOAD, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_RELOAD, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_AMBUSH, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_DIE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_DIE_RAGDOLL, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_WAIT_FOR_SCRIPT, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SCRIPTED_WALK, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SCRIPTED_RUN, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SCRIPTED_CUSTOM_MOVE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SCRIPTED_WAIT, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SCRIPTED_FACE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SCENE_GENERIC, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_NEW_WEAPON, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_NEW_WEAPON_CHEAT, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SWITCH_TO_PENDING_WEAPON, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_GET_HEALTHKIT, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_MOVE_AWAY, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_MOVE_AWAY_FAIL, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_MOVE_AWAY_END, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_WAIT_FOR_SPEAK_FINISH, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_FORCED_GO, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_FORCED_GO_RUN, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_PATROL_WALK, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_COMBAT_PATROL, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_PATROL_RUN, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_RUN_RANDOM, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_FAIL, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_FAIL_NOSTOP, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_FALL_TO_GROUND, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_DROPSHIP_DUSTOFF, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_FLINCH_PHYSICS, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_RUN_FROM_ENEMY_MOB, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_DUCK_DODGE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_NPC_FREEZE, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_INTERACTION_MOVE_TO_PARTNER, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_INTERACTION_WAIT_FOR_PARTNER, ClassScheduleIdSpace))
			return false;
		if (!g_AI_SchedulesManager.LoadSchedulesFromBuffer("CAI_BaseNPC", g_pszSCHED_SLEEP, ClassScheduleIdSpace))
			return false;

		return true;
	}

	public NPCState GetState() => NPCState;

	public bool IsInAScript() => InAScript;
	public void SetInAScript(bool script) => InAScript = script;

	public bool CineCleanup() => throw new NotImplementedException();

	public bool IsInLockedScene() => SceneTime > gpGlobals.CurTime;
	public void AddSceneLock(TimeUnit_t duration = 0.2) => SceneTime = Math.Max(gpGlobals.CurTime + duration, SceneTime);
	public void ClearSceneLock(TimeUnit_t duration = 0.2) => SceneTime = gpGlobals.CurTime + duration;

	public virtual bool IsInterruptable() {
		if (GetState() == NPCState.Script) {
			AI_ScriptedSequence? cine = Cine.Get();
			if (cine != null) {
				if (!cine.CanInterrupt())
					return false;

				if ((GetFlags() & EntityFlags.Fly) != 0 && (cine.SavedFlags & EntityFlags.Fly) == 0)
					return false;
			}
		}

		return IsAlive();
	}

	public virtual void OnStartScene() { }

	public bool ExitScriptedSequence() {
		if (LifeState == (int)Source.LifeState.Dying) {
			SetIdealState(NPCState.Dead);
			return false;
		}

		Cine.Get()?.CancelScript();

		return true;
	}

	public virtual float CalcIdealYaw(in Vector3 target) => Util.VecToYaw(target - GetLocalOrigin());

	public virtual void AddFacingTarget(BaseEntity? target, float importance, float duration, float ramp = 0.0f) => GetMotor()!.AddFacingTarget(target, importance, duration, ramp);
	public virtual void AddFacingTarget(in Vector3 position, float importance, float duration, float ramp = 0.0f) => GetMotor()!.AddFacingTarget(position, importance, duration, ramp);
	public virtual void AddFacingTarget(BaseEntity? target, in Vector3 position, float importance, float duration, float ramp = 0.0f) => GetMotor()!.AddFacingTarget(target, position, importance, duration, ramp);

	public virtual void AddLookTarget(BaseEntity? target, float importance, float duration, float ramp = 0.0f) { }
	public virtual void AddLookTarget(in Vector3 position, float importance, float duration, float ramp = 0.0f) { }

	public virtual Vector3 FacingPosition() => EyePosition();

	public virtual void MaintainLookTargets(TimeUnit_t interval) {
		if (GetEnemy() != null) {
			if (ValidEyeTarget(GetEnemy()!.EyePosition())) {
				SetHeadDirection(GetEnemy()!.EyePosition(), interval);
				SetViewtarget(GetEnemy()!.EyePosition());
				return;
			}
		}

		if (NextEyeLookTime > gpGlobals.CurTime) {
			if (!ValidEyeTarget(EyeLookTarget))
				NextEyeLookTime = 0;
		}

		if (NextEyeLookTime < gpGlobals.CurTime) {
			Vector3 bodyDir = BodyDirection2D();

			EyeLookTarget = EyePosition() + 500 * bodyDir;
			NextEyeLookTime = gpGlobals.CurTime + 0.5;
		}
		SetHeadDirection(EyeLookTarget, interval);

		TimeUnit_t timeToUse = interval;
		while (timeToUse > 0) {
			CurEyeTarget = ((1 - EyeIntegRate) * CurEyeTarget + EyeIntegRate * EyeLookTarget);
			timeToUse -= 0.1;
		}
		SetViewtarget(CurEyeTarget);
	}

	public virtual bool ValidEyeTarget(in Vector3 lookTargetPos) {
		Vector3 headDir = HeadDirection3D();
		Vector3 lookTargetDir = lookTargetPos - EyePosition();
		MathLib.VectorNormalize(ref lookTargetDir);

		float dotPr = Vector3.Dot(lookTargetDir, headDir);
		if (dotPr > 0.7)
			return true;
		return false;
	}

	public virtual void SetHeadDirection(in Vector3 targetPos, TimeUnit_t interval) {
		if ((CapabilitiesGet() & Server.Capability.TurnHead) == 0)
			return;

		float desiredYaw = Util.VecToYaw(targetPos - GetLocalOrigin()) - GetLocalAngles().Y;
		if (desiredYaw > 180)
			desiredYaw -= 360;
		if (desiredYaw < -180)
			desiredYaw += 360;

		float rate = 0.8f;

		TimeUnit_t timeToUse = interval;
		while (timeToUse > 0) {
			HeadYaw = (rate * HeadYaw) + (1 - rate) * desiredYaw;
			timeToUse -= 0.1;
		}
		if (HeadYaw > 360) HeadYaw = 0;

		HeadYaw = SetBoneController(0, HeadYaw);

		Vector3 eyePosition = EyePosition();
		float targetDist = (targetPos - eyePosition).Length();
		float vertDist = targetPos.Z - eyePosition.Z;
		float desiredPitch = -MathLib.RAD2DEG(MathF.Atan(vertDist / targetDist));

		timeToUse = interval;
		while (timeToUse > 0) {
			HeadPitch = (rate * HeadPitch) + (1 - rate) * desiredPitch;
			timeToUse -= 0.1;
		}
		if (HeadPitch > 360) HeadPitch = 0;

		SetBoneController(1, HeadPitch);
	}

	public override Vector3 EyeDirection2D() => HeadDirection2D();

	public override Vector3 EyeDirection3D() => HeadDirection3D();

	public override Vector3 HeadDirection2D() {
		QAngle bodyAngles = BodyAngles();
		float worldHeadYaw = HeadYaw + bodyAngles.Y;

		return Util.YawToVector(worldHeadYaw);
	}

	public override Vector3 HeadDirection3D() {
		QAngle bodyAngles = BodyAngles();
		float worldHeadYaw = HeadYaw + bodyAngles.Y;

		MathLib.AngleVectors(new QAngle(HeadPitch, worldHeadYaw, 0), out Vector3 headDirection);
		return headDirection;
	}

	public void MaintainTurnActivity() {
		if (IsInAVehicle())
			return;

		GetMotor()!.MaintainTurnActivity();
	}

	public Capability CapabilitiesRemove(Capability capability) {
		Capability &= ~capability;

		return Capability;
	}

	protected int PoseAim_Pitch;
	protected int PoseAim_Yaw;
	protected int PoseMove_Yaw;

	protected override void PopulatePoseParameters() {
		PoseAim_Pitch = LookupPoseParameter("aim_pitch");
		PoseAim_Yaw = LookupPoseParameter("aim_yaw");
		PoseMove_Yaw = LookupPoseParameter("move_yaw");

		base.PopulatePoseParameters();
	}

	public virtual void SetAim(in Vector3 aimDir) {
		MathLib.VectorAngles(aimDir, out QAngle angDir);
		float curPitch = GetPoseParameter(PoseAim_Pitch);
		float curYaw = GetPoseParameter(PoseAim_Yaw);

		float newPitch;
		float newYaw;

		if (GetEnemy() != null) {
			newPitch = curPitch + 0.8f * Util.AngleDiff(MathLib.ApproachAngle(angDir.X, curPitch, 20), curPitch);

			float relativeYaw = Util.AngleDiff(angDir.Y, GetAbsAngles().Y);
			newYaw = curYaw + Util.AngleDiff(relativeYaw, curYaw);
		}
		else {
			newPitch = curPitch + 0.6f * Util.AngleDiff(MathLib.ApproachAngle(angDir.X, curPitch, 20), curPitch);

			float relativeYaw = Util.AngleDiff(angDir.Y, GetAbsAngles().Y);
			newYaw = curYaw + 0.6f * Util.AngleDiff(relativeYaw, curYaw);
		}

		newPitch = MathLib.AngleNormalize(newPitch);
		newYaw = MathLib.AngleNormalize(newYaw);

		SetPoseParameter(PoseAim_Pitch, newPitch);
		SetPoseParameter(PoseAim_Yaw, newYaw);

		if (MathF.Abs(newYaw) < 20)
			InteractionYaw = angDir.Y;
		else
			InteractionYaw = GetAbsAngles().Y;
	}

	public void RelaxAim() {
		float curPitch = GetPoseParameter(PoseAim_Pitch);
		float curYaw = GetPoseParameter(PoseAim_Yaw);

		float newPitch = MathLib.AngleNormalize(MathLib.ApproachAngle(0, curPitch, 3));
		float newYaw = MathLib.AngleNormalize(MathLib.ApproachAngle(0, curYaw, 2));

		SetPoseParameter(PoseAim_Pitch, newPitch);
		SetPoseParameter(PoseAim_Yaw, newYaw);
	}

	public Vector3 GetEnemyLKP() => throw new NotImplementedException();

	public virtual Vector3 GetShootEnemyDir(in Vector3 shootOrigin, bool noisy = true) {
		BaseEntity? enemy = GetEnemy();

		if (enemy != null) {
			Vector3 enemyLKP = GetEnemyLKP();

			Vector3 enemyOffset = enemy.BodyTarget(shootOrigin, noisy) - enemy.GetAbsOrigin();

			Vector3 retval = enemyOffset + enemyLKP - shootOrigin;
			MathLib.VectorNormalize(ref retval);
			return retval;
		}
		else {
			MathLib.AngleVectors(GetLocalAngles(), out Vector3 forward);
			return forward;
		}
	}

	public virtual int HolsterWeapon() => throw new NotImplementedException();
	public virtual int UnholsterWeapon() => throw new NotImplementedException();

	static int selfwarningcount = 0;
	static int playerwarningcount = 0;
	public const int FINDNAMEDENTITY_MAX_ENTITIES = 32;
	static readonly BaseEntity?[] entityList = new BaseEntity?[FINDNAMEDENTITY_MAX_ENTITIES];


	public virtual BaseEntity? FindNamedEntity(ReadOnlySpan<char> name, IEntityFindFilter? filter = null){
		if (0 == stricmp(name, "!player")) {
			return (BaseEntity?)AI_GetSinglePlayer();
		}
		else if (0 == stricmp(name, "!enemy")) {
			if (GetEnemy() != null)
				return GetEnemy();
		}
		else if (0 == stricmp(name, "!self") || 0 == stricmp(name, "!target1")) {
			return this;
		}
		else if (0 == stricmp(name, "!nearestfriend") || 0 == stricmp(name, "!friend")) {
			// FIXME: look at CBaseEntity *CNPCSimpleTalker::FindNearestFriend(bool fPlayer)
			// punt for now
			return (BaseEntity?)AI_GetSinglePlayer();
		}
		else if (0 == stricmp(name, "self")) {

			// fix the vcd, the reserved names have changed
			if (++selfwarningcount < 5) {
				DevMsg("ERROR: \"self\" is no longer used, use \"!self\" in vcd instead!\n");
			}
			return this;
		}
		else if (0 == stricmp(name, "Player")) {
			if (++playerwarningcount < 5) {
				DevMsg("ERROR: \"player\" is no longer used, use \"!player\" in vcd instead!\n");
			}
			return (BaseEntity?)AI_GetSinglePlayer();
		}
		else {
			// search for up to 32 entities with the same name and choose one randomly
			BaseEntity? entity = null;
			int iCount;

			entity = null;
			for (iCount = 0; iCount < FINDNAMEDENTITY_MAX_ENTITIES; iCount++) {
				entity = gEntList.FindEntityByName(entity, name, null, null, null, filter);
				if (entity == null) 
					break;
				
				entityList[iCount] = entity;
			}

			if (iCount > 0) {
				int index = RandomInt(0, iCount - 1);
				entity = entityList[index];
				Array.Clear(entityList);
				return entity;
			}
		}

		return null;
	}

	public virtual AI_Expresser? GetExpresser() => null;

	public virtual bool IsPlayerAlly(BasePlayer? player = null) {
		if (player == null) {
			if (!AI_IsSinglePlayer())
				return false;

			player = Util.GetLocalPlayer();
		}

		return player == null || IRelationType(player) == Disposition.LI;
	}

	public virtual bool QuerySeeEntity(BaseEntity entity, bool onlyHateOrFearIfNPC = false) {
		if (onlyHateOrFearIfNPC && entity.IsNPC()) {
			Disposition disposition = IRelationType(entity);
			return (disposition == Disposition.HT || disposition == Disposition.FR);
		}
		return true;
	}

	public void SetTarget(BaseEntity? target) => TargetEnt.Set(target);

	public float GetHullWidth() => NAI_Hull.Width(GetHullType());

	public void Forget(AI_MemoryFlags memory) => Memory &= ~memory;
	public bool HasMemory(AI_MemoryFlags memory) => (Memory & memory) != 0;

	public TimeUnit_t GetLastAttackTime() => LastAttackTime;
	public TimeUnit_t GetLastDamageTime() => LastDamageTime;

	public AI_Efficiency GetEfficiency() => Efficiency;
	public AI_MoveEfficiency GetMoveEfficiency() => MoveEfficiency;
	public void SetMoveEfficiency(AI_MoveEfficiency efficiency) => MoveEfficiency = efficiency;

	public bool IsFlaggedEfficient() => HasSpawnFlags(SF_NPC_START_EFFICIENT);

	public void RemoveSleepFlags(AI_SleepFlags flags) => SleepFlags &= ~flags;
	public bool HasSleepFlags(AI_SleepFlags flags) => (SleepFlags & flags) == flags;

	public bool IsUsingSmallHull() => IsUsingSmallHullValue;

	public ref readonly Vector3 GetHullMins() => ref NAI_Hull.Mins(GetHullType());
	public ref readonly Vector3 GetHullMaxs() => ref NAI_Hull.Maxs(GetHullType());

	public virtual Vector3 GetCrouchEyeOffset() => new(0, 0, 40);

	public override bool IsMoving() => GetNavigator()!.IsGoalSet();

	public virtual float CalcYawSpeed() => -1.0f;

	public virtual float HearingSensitivity() => 1.0f;

	public void SetTaskStatus(TaskStatus status) => ScheduleState.TaskStatus = status;

	public void ResetScheduleCurTaskIndex() {
		ScheduleState.CurTask = 0;
		ScheduleState.TaskInterrupt = 0;
		ScheduleState.TaskRanAutomovement = false;
		ScheduleState.TaskUpdatedYaw = false;
	}

	public AI_Schedule? GetCurSchedule() => Schedule;

	public virtual int TranslateSchedule(int scheduleType) {
		switch (scheduleType) {
			case SCHED_AISCRIPT: {
					AI_ScriptedSequence? cine = Cine.Get();
					Assert(cine != null);
					if (cine == null) {
						DevWarning(2, $"Script failed for {GetClassname()}\n");
						CineCleanup();
						return SCHED_IDLE_STAND;
					}

					switch (cine.MoveTo) {
						case ScriptMoveTo.Wait:
						case ScriptMoveTo.Teleport:
							return SCHED_SCRIPTED_WAIT;
						case ScriptMoveTo.Walk:
							return SCHED_SCRIPTED_WALK;
						case ScriptMoveTo.Run:
							return SCHED_SCRIPTED_RUN;
						case ScriptMoveTo.Custom:
							return SCHED_SCRIPTED_CUSTOM_MOVE;
						case ScriptMoveTo.WaitFacing:
							return SCHED_SCRIPTED_FACE;
					}
				}
				break;

			case SCHED_IDLE_WALK:
				switch (NPCState) {
					case NPCState.Alert:
						return SCHED_ALERT_WALK;
					case NPCState.Combat:
						return SCHED_COMBAT_WALK;
				}
				break;

			case SCHED_ALERT_WALK:
				Assert(NPCState == NPCState.Alert);
				break;

			case SCHED_COMBAT_WALK:
				Assert(NPCState == NPCState.Combat);
				break;
		}

		return scheduleType;
	}

	public AI_Schedule? GetScheduleOfType(int scheduleType) {
		scheduleType = TranslateSchedule(scheduleType);

		AI_Schedule? schedule = GetSchedule(scheduleType);

		if (schedule == null) {
			DevMsg($"GetScheduleOfType(): No CASE for Schedule Type {scheduleType}!\n");
			return GetSchedule(SCHED_IDLE_STAND);
		}
		return schedule;
	}

	public virtual AI_Schedule? GetSchedule(int schedule) {
		if (!GetClassScheduleIdSpace().IsGlobalBaseSet()) {
			Warning($"ERROR: {GetSchedulingErrorName()} missing schedule!\n");
			return g_AI_SchedulesManager.GetScheduleFromID(SCHED_IDLE_STAND);
		}
		if (AI_IdIsLocal(schedule))
			schedule = GetClassScheduleIdSpace().ScheduleLocalToGlobal(schedule);

		return g_AI_SchedulesManager.GetScheduleFromID(schedule);
	}

	public virtual int GetGlobalScheduleId(int localScheduleID) => AI_IdIsGlobal(localScheduleID) ? localScheduleID : GetClassScheduleIdSpace().ScheduleLocalToGlobal(localScheduleID);

	public virtual ReadOnlySpan<char> GetSchedulingErrorName() => "CAI_BaseNPC";

	public static bool LoadSchedules() => true;

	public bool IsCurSchedule(int schedId, bool ideal = true) {
		if (Schedule == null)
			return schedId == SCHED_NONE || schedId == AI_RemapToGlobal(SCHED_NONE);

		schedId = AI_IdIsLocal(schedId) ? GetClassScheduleIdSpace().ScheduleLocalToGlobal(schedId) : schedId;
		if (ideal)
			return schedId == IdealSchedule;

		return Schedule.GetId() == schedId;
	}

	public Task_t? GetTask() {
		int scheduleIndex = GetScheduleCurTaskIndex();
		if (GetCurSchedule() == null || scheduleIndex < 0 || scheduleIndex >= GetCurSchedule()!.NumTasks())
			return null;

		return GetCurSchedule()!.GetTaskList()![scheduleIndex];
	}

	public bool TaskIsRunning() {
		if (GetTaskStatus() != TaskStatus.Complete && GetTaskStatus() != TaskStatus.RunMove)
			return true;

		return false;
	}

	public void TaskInterrupt() => ScheduleState.TaskInterrupt++;
	public void ClearTaskInterrupt() => ScheduleState.TaskInterrupt = 0;
	public int GetTaskInterrupt() => ScheduleState.TaskInterrupt;

	public bool TaskIsComplete() => GetTaskStatus() == TaskStatus.Complete;

	public TimeUnit_t GetTimeTaskStarted() => ScheduleState.TimeCurTaskStarted;

	public TaskStatus GetTaskStatus() => ScheduleState.TaskStatus;

	public int GetScheduleCurTaskIndex() => ScheduleState.CurTask;

	public int IncScheduleCurTaskIndex() {
		ScheduleState.TaskInterrupt = 0;
		ScheduleState.TaskRanAutomovement = false;
		ScheduleState.TaskUpdatedYaw = false;
		return ++ScheduleState.CurTask;
	}

	public void SetUpdatedYaw() => ScheduleState.TaskUpdatedYaw = true;

	public virtual int GetLocalScheduleId(int globalScheduleID) => AI_IdIsLocal(globalScheduleID) ? globalScheduleID : GetClassScheduleIdSpace().ScheduleGlobalToLocal(globalScheduleID);

	public virtual void TaskComplete(bool ignoreSetFailedCondition = false) {
		EndTaskOverlay();

		if (ignoreSetFailedCondition || !HasCondition((int)SCOND_t.COND_TASK_FAILED))
			SetTaskStatus(TaskStatus.Complete);
	}

	public void TaskFail(AI_TaskFailureCode code) {
		EndTaskOverlay();

		if (developer.GetInt() != 0) {
			FailText = TaskFailureToString(code);

			InterruptSchedule = null;
			FailedSchedule = GetCurSchedule();

			if ((DebugOverlays & DebugOverlayBits.TaskText) != 0)
				DevMsg($"      TaskFail -> {FailText}\n");
		}

		ScheduleState.TaskFailureCode = code;
		SetCondition((int)SCOND_t.COND_TASK_FAILED);
		Forget(AI_MemoryFlags.Turning);
	}

	public void TaskFail(string generalFailText) => TaskFail(MakeFailCode(generalFailText));

	public void Remember(AI_MemoryFlags memory) => Memory |= memory;

	public void ClearConditions(ReadOnlySpan<int> conditions) {
		for (int i = 0; i < conditions.Length; ++i) {
			int condition = conditions[i];
			int interrupt = InterruptFromCondition(condition);

			if (interrupt == -1) {
				Assert(false);
				continue;
			}

			Conditions.Clear(interrupt);
		}
	}

	public bool HasInterruptCondition(int condition) {
		if (GetCurSchedule() == null)
			return false;

		int interrupt = InterruptFromCondition(condition);

		if (interrupt == -1) {
			Assert(false);
			return false;
		}
		return Conditions.IsBitSet(interrupt) && GetCurSchedule()!.HasInterrupt(interrupt);
	}

	public bool IsCustomInterruptConditionSet(int condition) {
		int interrupt = InterruptFromCondition(condition);

		if (interrupt == -1) {
			Assert(false);
			return false;
		}

		return CustomInterruptConditions.IsBitSet(interrupt);
	}

	public void SetCustomInterruptCondition(int condition) {
		int interrupt = InterruptFromCondition(condition);

		if (interrupt == -1) {
			Assert(false);
			return;
		}

		CustomInterruptConditions.Set(interrupt);
	}

	public void ClearCustomInterruptCondition(int condition) {
		int interrupt = InterruptFromCondition(condition);

		if (interrupt == -1) {
			Assert(false);
			return;
		}

		CustomInterruptConditions.Clear(interrupt);
	}

	public float SetWait(float minWait, float maxWait = 0.0f) {
		int minThinks = (int)MathF.Ceiling(minWait * 10);

		if (maxWait == 0.0)
			WaitFinished = gpGlobals.CurTime + (0.1 * minThinks);
		else {
			if (minThinks == 0)
				minThinks = 1;
			int maxThinks = (int)MathF.Ceiling(maxWait * 10);

			WaitFinished = gpGlobals.CurTime + (0.1 * RandomInt(minThinks, maxThinks));
		}
		return (float)WaitFinished;
	}

	public void ClearWait() => WaitFinished = float.MaxValue;

	public bool IsWaitFinished() => gpGlobals.CurTime >= WaitFinished;

	public bool IsWaitSet() => WaitFinished != float.MaxValue;

	public bool FScheduleDone() {
		Assert(GetCurSchedule() != null);

		if (GetScheduleCurTaskIndex() == GetCurSchedule()!.NumTasks())
			return true;

		return false;
	}

	public void NextScheduledTask() {
		Assert(GetCurSchedule() != null);

		SetTaskStatus(TaskStatus.New);
		IncScheduleCurTaskIndex();

		if (FScheduleDone()) {
			FailedSchedule = null;
			InterruptSchedule = null;

			SetCondition((int)SCOND_t.COND_SCHEDULE_DONE);
		}
	}

	public virtual void BuildScheduleTestBits() { }

	public bool IsScheduleValid() {
		if (GetCurSchedule() == null || GetCurSchedule()!.NumTasks() == 0)
			return false;

		GetCurSchedule()!.GetInterruptMask(out CustomInterruptConditions);

		if (NPCState != NPCState.Script && !IsInLockedScene() && !CustomInterruptConditions.IsBitSet((int)SCOND_t.COND_NO_CUSTOM_INTERRUPTS))
			BuildScheduleTestBits();

		SetCustomInterruptCondition((int)SCOND_t.COND_NPC_FREEZE);

		AI_ScheduleBits testBits = CustomInterruptConditions.And(Conditions);

		if (!testBits.IsAllClear()) {
			if (developer.GetInt() != 0) {
				FailedSchedule = null;
				InterruptSchedule = GetCurSchedule();

				for (int i = 0; i < MAX_CONDITIONS; i++) {
					if (testBits.IsBitSet(i)) {
						InterruptText = ConditionName(AI_RemapToGlobal(i));
						if (InterruptText == null)
							InterruptText = "(UNKNOWN CONDITION)";

						if ((DebugOverlays & DebugOverlayBits.TaskText) != 0)
							DevMsg($"      Break condition -> {InterruptText}\n");

						break;
					}
				}

				if (HasCondition((int)SCOND_t.COND_NEW_ENEMY)) {
					if ((DebugOverlays & DebugOverlayBits.TaskText) != 0)
						DevMsg($"      New enemy: {(GetEnemy() != null ? GetEnemy()!.GetDebugName() : "<NULL>")}\n");
				}
			}

			return false;
		}

		if (HasCondition((int)SCOND_t.COND_SCHEDULE_DONE) || HasCondition((int)SCOND_t.COND_TASK_FAILED))
			return false;

		return true;
	}

	public bool ShouldSelectIdealState() {
		if (IdealNPCState == NPCState.Dead)
			return false;

		if ((IdealNPCState == NPCState.Script) && (NPCState != NPCState.Script))
			return false;

		if (!HasCondition((int)SCOND_t.COND_SCHEDULE_DONE))
			return true;

		if (GetCurSchedule() != null && GetCurSchedule()!.HasInterrupt((int)SCOND_t.COND_SCHEDULE_DONE))
			return true;

		if ((NPCState == NPCState.Combat) && (GetEnemy() == null))
			return true;

		if ((NPCState == NPCState.Idle || NPCState == NPCState.Alert) && (GetEnemy() != null))
			return true;

		return false;
	}

	public virtual AI_Schedule? GetNewSchedule() {
		int scheduleType;

		if (HasCondition((int)SCOND_t.COND_NPC_FREEZE))
			scheduleType = SCHED_NPC_FREEZE;
		else {
			if (NPCState == NPCState.Combat && GetEnemy() == null) {
				DevMsg("**ERROR: Combat State with no enemy! slamming to ALERT\n");
				SetState(NPCState.Alert);
			}

			if (NPCState == NPCState.Script || NPCState == NPCState.Dead || InteractionState == NPCInteractionState.MovingToMark)
				scheduleType = BaseSelectSchedule();
			else
				scheduleType = SelectSchedule();

			IdealSchedule = GetGlobalScheduleId(scheduleType);
		}

		return GetScheduleOfType(scheduleType);
	}

	public virtual AI_Schedule? GetFailSchedule() {
		int prevSchedule;
		int failedTask;

		if (GetCurSchedule() != null)
			prevSchedule = GetLocalScheduleId(GetCurSchedule()!.GetId());
		else
			prevSchedule = SCHED_NONE;

		Task_t? task = GetTask();
		if (task != null)
			failedTask = task.Value.Task;
		else
			failedTask = TASK_INVALID;

		Assert(AI_IdIsLocal(prevSchedule));
		Assert(AI_IdIsLocal(failedTask));

		int scheduleType = SelectFailSchedule(prevSchedule, failedTask, ScheduleState.TaskFailureCode);
		return GetScheduleOfType(scheduleType);
	}

	public virtual int SelectFailSchedule(int failedSchedule, int failedTask, AI_TaskFailureCode taskFailCode) => (FailSchedule != SCHED_NONE) ? FailSchedule : SCHED_FAIL;

	const int MAX_TASKS_RUN = 10;

	static bool ShouldStopProcessingTasks(AI_BaseNPC npc, long taskTime, long timeLimit) {
		if (npc.IsNavigationDeferred())
			return true;

		if (AIStrongOpt()) {
			bool inScript = npc.GetState() == NPCState.Script || npc.IsCurSchedule(SCHED_SCENE_GENERIC, false);

			if (npc.HasMemory(AI_MemoryFlags.TaskExpensive) && inScript == false)
				return true;
		}

		if (taskTime > timeLimit) {
			if (ShouldUseEfficiency() ||
				 npc.IsMoving() ||
				 (npc.GetIdealActivity() != Activity.ACT_RUN && npc.GetIdealActivity() != Activity.ACT_WALK)) {
				return true;
			}
		}
		return false;
	}

	public void MaintainSchedule() {
		AI_Schedule? newSchedule;
		int i;
		bool runTask = true;

#if DEBUG
		const int timeLimit = 16;
#else
		const int timeLimit = 8;
#endif
		long taskTime = Environment.TickCount64;

		Forget(AI_MemoryFlags.TaskExpensive);

		bool stopProcessing = false;
		for (i = 0; i < MAX_TASKS_RUN && !stopProcessing; i++) {
			if (GetCurSchedule() != null && TaskIsComplete()) {
				NextScheduledTask();

				if (HasCondition((int)SCOND_t.COND_SCHEDULE_DONE)) {
					Conditions = ConditionsPreIgnore;
					SetCondition((int)SCOND_t.COND_SCHEDULE_DONE);

					InverseIgnoreConditions.SetAll();
				}

				if ((DebugBits & AI_DebugFlags.StepAI) != 0) {
					DebugCurIndex++;
					return;
				}
			}

			if (!IsScheduleValid() || NPCState != IdealNPCState) {
				ScheduleState.ScheduleWasInterrupted = true;
				OnScheduleChange();

				if (!HasCondition((int)SCOND_t.COND_NPC_FREEZE) && !ConditionsGatheredValue)
					GatherConditions();

				if (ShouldSelectIdealState()) {
					NPCState idealState = SelectIdealState();
					SetIdealState(idealState);
				}

				if (HasCondition((int)SCOND_t.COND_TASK_FAILED) && NPCState == IdealNPCState) {
					if ((DebugOverlays & DebugOverlayBits.TaskText) != 0)
						DevMsg("      (failed)\n");

					newSchedule = GetFailSchedule();
					IdealSchedule = newSchedule!.GetId();
					DevWarning(2, $"({GetEntityName()}) Schedule ({(GetCurSchedule() != null ? GetCurSchedule()!.GetName() : "GetCurSchedule() == NULL")}) Failed at {GetScheduleCurTaskIndex()}!\n");
					SetSchedule(newSchedule);
				}
				else {
					SetState(IdealNPCState);

					newSchedule = GetNewSchedule();

					SetSchedule(newSchedule!);
				}
			}

			if (GetCurSchedule() == null) {
				newSchedule = GetNewSchedule();

				if (newSchedule != null)
					SetSchedule(newSchedule);
			}

			if (GetCurSchedule() == null || GetCurSchedule()!.NumTasks() == 0) {
				DevMsg("ERROR: Missing or invalid schedule!\n");
				SetActivity(Activity.ACT_IDLE);
				return;
			}

			if (GetTaskStatus() == TaskStatus.New) {
				if (GetScheduleCurTaskIndex() == 0) {
					int globalId = GetCurSchedule()!.GetId();
					int localId = GetLocalScheduleId(globalId);
					OnStartSchedule((localId != -1) ? localId : globalId);
				}

				Task_t? task = GetTask();
				Assert(task != null);

				if ((DebugOverlays & DebugOverlayBits.TaskText) != 0)
					DevMsg($"  Task: {TaskName(task!.Value.Task)}\n");

				OnStartTask();

				ScheduleState.TaskFailureCode = AI_TaskFailureCode.NoTaskFailure;
				ScheduleState.TimeCurTaskStarted = gpGlobals.CurTime;

				StartTask(task!.Value);

				if (TaskIsRunning() && !HasCondition((int)SCOND_t.COND_TASK_FAILED))
					StartTaskOverlay();
			}

			MaintainActivity();

			if (!TaskIsComplete() && GetTaskStatus() != TaskStatus.New) {
				if (TaskIsRunning() && !HasCondition((int)SCOND_t.COND_TASK_FAILED) && runTask) {
					Task_t? task = GetTask();
					Assert(task != null);

					int j;
					for (j = 0; j < 8; j++) {
						RunTask(task!.Value);

						if (GetTaskInterrupt() == 0 || TaskIsComplete() || HasCondition((int)SCOND_t.COND_TASK_FAILED))
							break;

						if (ShouldUseEfficiency() && ShouldStopProcessingTasks(this, Environment.TickCount64 - taskTime, timeLimit)) {
							stopProcessing = true;
							break;
						}
					}
					AssertMsg(j < 8, "Runaway task interrupt\n");

					if (TaskIsRunning() && !HasCondition((int)SCOND_t.COND_TASK_FAILED)) {
						if (IsCurTaskContinuousMove())
							Remember(AI_MemoryFlags.MovedFromSpawn);
						RunTaskOverlay();
					}

					if (!TaskIsComplete())
						stopProcessing = true;
				}
				else
					stopProcessing = true;
			}

			if (!stopProcessing && ShouldStopProcessingTasks(this, Environment.TickCount64 - taskTime, timeLimit))
				stopProcessing = true;
		}

		MaintainActivity();

		if ((DebugBits & AI_DebugFlags.StepAI) != 0) {
			if (DebugCurIndex >= DebugPauseIndex)
				PlaybackRate = 0;
		}
	}

	public virtual string? TaskName(int taskID) {
		if (AI_IdIsLocal(taskID))
			taskID = GetClassScheduleIdSpace().TaskLocalToGlobal(taskID);
		return GetSchedulingSymbols().TaskIdToSymbol(taskID);
	}

	public virtual string? ConditionName(int conditionID) {
		if (AI_IdIsLocal(conditionID))
			conditionID = GetClassScheduleIdSpace().ConditionLocalToGlobal(conditionID);
		return GetSchedulingSymbols().ConditionIdToSymbol(conditionID);
	}

	public virtual void OnStartSchedule(int scheduleType) { }

	public void OnStartTask() => SetTaskStatus(TaskStatus.RunMoveAndTask);

	public bool IsNavigationDeferred() => DeferredNavigation;

	public virtual bool IsCurTaskContinuousMove() {
		Task_t? task = GetTask();

		if (task == null)
			return true;

		switch (task.Value.Task) {
			case TASK_WAIT_FOR_MOVEMENT:
			case TASK_MOVE_TO_TARGET_RANGE:
			case TASK_MOVE_TO_GOAL_RANGE:
			case TASK_WEAPON_RUN_PATH:
			case TASK_PLAY_SCENE:
			case TASK_RUN_PATH_TIMED:
			case TASK_WALK_PATH_TIMED:
			case TASK_RUN_PATH_FOR_UNITS:
			case TASK_WALK_PATH_FOR_UNITS:
			case TASK_RUN_PATH_FLEE:
			case TASK_WALK_PATH_WITHIN_DIST:
			case TASK_RUN_PATH_WITHIN_DIST:
				return true;

			default:
				return false;
		}
	}

	public bool ShouldMoveWait() => MoveWaitFinished > gpGlobals.CurTime;

	public virtual bool ShouldMoveAndShoot() => (CapabilitiesGet() & Server.Capability.MoveShoot) != 0;

	public void StartTaskOverlay() {
		if (IsCurTaskContinuousMove()) {
			if (ShouldMoveAndShoot())
				MoveAndShootOverlay.StartShootWhileMove();
			else
				MoveAndShootOverlay.NoShootWhileMove();
		}
	}

	public void RunTaskOverlay() {
		if (IsCurTaskContinuousMove())
			MoveAndShootOverlay.RunShootWhileMove();
	}

	public void EndTaskOverlay() => MoveAndShootOverlay.EndShootWhileMove();

	public virtual void OnScheduleChange() {
		EndTaskOverlay();

		MoveWaitFinished = 0;

		if (HasMemory(AI_MemoryFlags.LockedHint) && GetHintNode() != null) {
			float hintDelay = GetHintDelay(GetHintNode()!.HintType());
			GetHintNode()!.Unlock(hintDelay);
			SetHintNode(null);
		}
	}

	public virtual float GetHintDelay(short hintType) => 0;

	public Activity GetIdealActivity() => IdealActivity;

	public bool IsActivityStarted() => GetSequence() == IdealSequence;

	public bool IsActivityFinished() => IsSequenceFinished() && (GetSequence() == IdealSequence);

	public Activity GetStoppedActivity() => Activity.ACT_IDLE;

	public void MaintainActivity() {
		if (LifeState == (int)Source.LifeState.Dead)
			return;

		if (GetState() == NPCState.Script) {
			if (GetActivity() != Activity.ACT_TRANSITION)
				return;
		}

		if (IdealActivity == Activity.ACT_DO_NOT_DISTURB || GetModelPtr() == null)
			return;

		if ((GetActivity() != IdealActivity) || (GetSequence() != IdealSequence)) {
			if (ai_sequence_debug.GetBool() == true && (DebugOverlays & DebugOverlayBits.NPCSelected) != 0)
				DevMsg($"MaintainActivity {GetClassname()} : {GetActivityName(GetActivity())}:{Animation.GetSequenceName(GetModelPtr(), GetSequence())} -> {GetActivityName(IdealActivity)}:{Animation.GetSequenceName(GetModelPtr(), IdealSequence)}\n");

			bool advance = false;

			if (GetActivity() == Activity.ACT_TRANSITION) {
				if (IsSequenceFinished())
					advance = true;
			}
			else {
				ResolveActivityToSequence(IdealActivity, ref IdealSequence, ref IdealTranslatedActivity, ref IdealWeaponActivity);
				advance = true;
			}

			if (advance)
				AdvanceToIdealActivity();
		}
	}

	public void AdvanceToIdealActivity() {
		int nextSequence = FindTransitionSequence(GetSequence(), IdealSequence);
		if (nextSequence != -1) {
			if (nextSequence != IdealSequence) {
				Activity weaponActivity = Activity.ACT_TRANSITION;
				Activity translatedActivity = Activity.ACT_TRANSITION;

				Activity transitionActivity = GetSequenceActivity(nextSequence);
				if (transitionActivity != Activity.ACT_INVALID) {
					int discard = 0;
					ResolveActivityToSequence(transitionActivity, ref discard, ref translatedActivity, ref weaponActivity);
				}

				SetActivityAndSequence(Activity.ACT_TRANSITION, nextSequence, translatedActivity, weaponActivity);
			}
			else
				SetActivityAndSequence(IdealActivity, IdealSequence, IdealTranslatedActivity, IdealWeaponActivity);
		}
		else
			SetActivity(IdealActivity);
	}

	public void ResetIdealActivity(Activity newIdealActivity) {
		if (Activity == newIdealActivity)
			Activity = Activity.ACT_RESET;

		SetIdealActivity(newIdealActivity);
	}

	public bool FacingIdeal() {
		if (MathF.Abs(GetMotor()!.DeltaIdealYaw()) <= 0.006)
			return true;

		return false;
	}

	public void SetTurnActivity() {
		if (IsCrouching()) {
			SetIdealActivity(Activity.ACT_IDLE);
			return;
		}

		float yd;
		yd = GetMotor()!.DeltaIdealYaw();

		if (yd <= -80 && yd >= -100 && SelectWeightedSequence(Activity.ACT_90_RIGHT) != StudioHdr.ACTIVITY_NOT_AVAILABLE) {
			Remember(AI_MemoryFlags.Turning);
			SetIdealActivity(Activity.ACT_90_RIGHT);
			return;
		}
		if (yd >= 80 && yd <= 100 && SelectWeightedSequence(Activity.ACT_90_LEFT) != StudioHdr.ACTIVITY_NOT_AVAILABLE) {
			Remember(AI_MemoryFlags.Turning);
			SetIdealActivity(Activity.ACT_90_LEFT);
			return;
		}
		if (MathF.Abs(yd) >= 160 && SelectWeightedSequence(Activity.ACT_180_LEFT) != StudioHdr.ACTIVITY_NOT_AVAILABLE) {
			Remember(AI_MemoryFlags.Turning);
			SetIdealActivity(Activity.ACT_180_LEFT);
			return;
		}

		if (yd <= -45 && SelectWeightedSequence(Activity.ACT_TURN_RIGHT) != StudioHdr.ACTIVITY_NOT_AVAILABLE) {
			SetIdealActivity(Activity.ACT_TURN_RIGHT);
			return;
		}
		if (yd >= 45 && SelectWeightedSequence(Activity.ACT_TURN_LEFT) != StudioHdr.ACTIVITY_NOT_AVAILABLE) {
			SetIdealActivity(Activity.ACT_TURN_LEFT);
			return;
		}

		SetIdealActivity(Activity.ACT_IDLE);
	}

	public bool UpdateTurnGesture() {
		float yd = GetMotor()!.DeltaIdealYaw();
		return GetMotor()!.AddTurnGesture(yd);
	}

	public void ChainStartTask(int task, float taskData = 0) {
		Task_t tempTask = new() { Task = task, TaskData = taskData };
		StartTask(tempTask);
	}

	public void ChainRunTask(int task, float taskData = 0) {
		Task_t tempTask = new() { Task = task, TaskData = taskData };
		RunTask(tempTask);
	}

	public bool TaskRanAutomovement() => ScheduleState.TaskRanAutomovement;

	public virtual void StartTask(in Task_t task) {
		switch (task.Task) {
			case TASK_RESET_ACTIVITY:
				Activity = Activity.ACT_RESET;
				TaskComplete();
				break;

			case TASK_STOP_MOVING:
				SetIdealActivity(GetStoppedActivity());
				TaskComplete();
				break;

			case TASK_SET_SCHEDULE:
				if (!SetSchedule((int)task.TaskData))
					TaskFail(AI_TaskFailureCode.ScheduleNotFound);
				break;

			case TASK_FACE_IDEAL:
				SetTurnActivity();
				break;

			case TASK_WAIT_PVS:
			case TASK_WAIT_INDEFINITE:
				break;

			case TASK_WAIT:
			case TASK_WAIT_FACE_ENEMY:
				SetWait(task.TaskData);
				break;

			case TASK_WAIT_RANDOM:
			case TASK_WAIT_FACE_ENEMY_RANDOM:
				SetWait(0, task.TaskData);
				break;

			case TASK_SET_ACTIVITY: {
					Activity goalActivity = (Activity)(int)task.TaskData;
					if (goalActivity != Activity.ACT_RESET)
						SetIdealActivity(goalActivity);
					else
						Activity = Activity.ACT_RESET;
					break;
				}

			case TASK_WAIT_FOR_MOVEMENT:
				TaskComplete();
				break;

			case TASK_PLAY_SCENE:
				break;

			case TASK_SUGGEST_STATE:
				SetIdealState((NPCState)(int)task.TaskData);
				TaskComplete();
				break;

			case TASK_SET_FAIL_SCHEDULE:
				FailSchedule = (int)task.TaskData;
				TaskComplete();
				break;

			case TASK_CLEAR_FAIL_SCHEDULE:
				FailSchedule = SCHED_NONE;
				TaskComplete();
				break;

			case TASK_FALL_TO_GROUND:
				SetWait(4);
				break;

			case TASK_FREEZE:
				PlaybackRate = 0;
				break;

			case TASK_GATHER_CONDITIONS:
				GatherConditions();
				TaskComplete();
				break;

			case TASK_CREATE_PENDING_WEAPON:
			case TASK_RANDOMIZE_FRAMERATE:
			case TASK_DEFER_DODGE:
			case TASK_ANNOUNCE_ATTACK:
			case TASK_TURN_RIGHT:
			case TASK_TURN_LEFT:
			case TASK_REMEMBER:
			case TASK_FORGET:
			case TASK_FIND_HINTNODE:
			case TASK_FIND_LOCK_HINTNODE:
			case TASK_LOCK_HINTNODE:
			case TASK_STORE_LASTPOSITION:
			case TASK_CLEAR_LASTPOSITION:
			case TASK_STORE_POSITION_IN_SAVEPOSITION:
			case TASK_STORE_BESTSOUND_IN_SAVEPOSITION:
			case TASK_STORE_ENEMY_POSITION_IN_SAVEPOSITION:
			case TASK_CLEAR_HINTNODE:
			case TASK_PLAY_PRIVATE_SEQUENCE:
			case TASK_PLAY_PRIVATE_SEQUENCE_FACE_ENEMY:
			case TASK_PLAY_SEQUENCE_FACE_ENEMY:
			case TASK_PLAY_SEQUENCE_FACE_TARGET:
			case TASK_PLAY_SEQUENCE:
			case TASK_ADD_GESTURE_WAIT:
			case TASK_ADD_GESTURE:
			case TASK_PLAY_HINT_ACTIVITY:
			case TASK_FIND_BACKAWAY_FROM_SAVEPOSITION:
			case TASK_FIND_NEAR_NODE_COVER_FROM_ENEMY:
			case TASK_FIND_FAR_NODE_COVER_FROM_ENEMY:
			case TASK_FIND_NODE_COVER_FROM_ENEMY:
			case TASK_FIND_COVER_FROM_ENEMY:
			case TASK_FIND_COVER_FROM_ORIGIN:
			case TASK_FIND_COVER_FROM_BEST_SOUND:
			case TASK_FACE_HINTNODE:
			case TASK_FACE_LASTPOSITION:
			case TASK_FACE_AWAY_FROM_SAVEPOSITION:
			case TASK_SET_IDEAL_YAW_TO_CURRENT:
			case TASK_FACE_TARGET:
			case TASK_FACE_PLAYER:
			case TASK_FACE_ENEMY:
			case TASK_FACE_PATH:
			case TASK_MOVE_TO_TARGET_RANGE:
			case TASK_MOVE_TO_GOAL_RANGE:
			case TASK_WAIT_UNTIL_NO_DANGER_SOUND:
			case TASK_TARGET_PLAYER:
			case TASK_SCRIPT_RUN_TO_TARGET:
			case TASK_SCRIPT_WALK_TO_TARGET:
			case TASK_SCRIPT_CUSTOM_MOVE_TO_TARGET:
			case TASK_CLEAR_MOVE_WAIT:
			case TASK_MELEE_ATTACK1:
			case TASK_MELEE_ATTACK2:
			case TASK_RANGE_ATTACK1:
			case TASK_RANGE_ATTACK2:
			case TASK_RELOAD:
			case TASK_SPECIAL_ATTACK1:
			case TASK_SPECIAL_ATTACK2:
			case TASK_GET_CHASE_PATH_TO_ENEMY:
			case TASK_GET_PATH_TO_ENEMY_LKP:
			case TASK_GET_PATH_TO_INTERACTION_PARTNER:
			case TASK_GET_PATH_TO_RANGE_ENEMY_LKP_LOS:
			case TASK_GET_PATH_TO_ENEMY_LOS:
			case TASK_GET_FLANK_RADIUS_PATH_TO_ENEMY_LOS:
			case TASK_GET_FLANK_ARC_PATH_TO_ENEMY_LOS:
			case TASK_GET_PATH_TO_ENEMY_LKP_LOS:
			case TASK_SET_GOAL:
			case TASK_GET_PATH_TO_GOAL:
			case TASK_GET_PATH_TO_ENEMY:
			case TASK_GET_PATH_TO_ENEMY_CORPSE:
			case TASK_GET_PATH_TO_PLAYER:
			case TASK_GET_PATH_TO_SAVEPOSITION_LOS:
			case TASK_GET_PATH_TO_TARGET_WEAPON:
			case TASK_GET_PATH_TO_TARGET:
			case TASK_GET_PATH_TO_HINTNODE:
			case TASK_GET_PATH_TO_COMMAND_GOAL:
			case TASK_MARK_COMMAND_GOAL_POS:
			case TASK_CLEAR_COMMAND_GOAL:
			case TASK_GET_PATH_TO_LASTPOSITION:
			case TASK_GET_PATH_TO_SAVEPOSITION:
			case TASK_GET_PATH_TO_RANDOM_NODE:
			case TASK_GET_PATH_TO_BESTSOUND:
			case TASK_GET_PATH_TO_BESTSCENT:
			case TASK_GET_PATH_AWAY_FROM_BEST_SOUND:
			case TASK_MOVE_AWAY_PATH:
			case TASK_WEAPON_RUN_PATH:
			case TASK_ITEM_RUN_PATH:
			case TASK_RUN_PATH:
			case TASK_WALK_PATH_FOR_UNITS:
			case TASK_RUN_PATH_FOR_UNITS:
			case TASK_WALK_PATH:
			case TASK_WALK_PATH_WITHIN_DIST:
			case TASK_RUN_PATH_WITHIN_DIST:
			case TASK_RUN_PATH_FLEE:
			case TASK_WALK_PATH_TIMED:
			case TASK_RUN_PATH_TIMED:
			case TASK_STRAFE_PATH:
			case TASK_WAIT_FOR_MOVEMENT_STEP:
			case TASK_SMALL_FLINCH:
			case TASK_BIG_FLINCH:
			case TASK_DIE:
			case TASK_SOUND_WAKE:
			case TASK_SOUND_DIE:
			case TASK_SOUND_IDLE:
			case TASK_SOUND_PAIN:
			case TASK_SOUND_ANGRY:
			case TASK_SPEAK_SENTENCE:
			case TASK_WAIT_FOR_SPEAK_FINISH:
			case TASK_WAIT_FOR_SCRIPT:
			case TASK_PUSH_SCRIPT_ARRIVAL_ACTIVITY:
			case TASK_PLAY_SCRIPT:
			case TASK_PLAY_SCRIPT_POST_IDLE:
			case TASK_PRE_SCRIPT:
			case TASK_ENABLE_SCRIPT:
			case TASK_PLANT_ON_SCRIPT:
			case TASK_FACE_SCRIPT:
			case TASK_SET_TOLERANCE_DISTANCE:
			case TASK_SET_ROUTE_SEARCH_TIME:
			case TASK_WEAPON_FIND:
			case TASK_ITEM_PICKUP:
			case TASK_WEAPON_PICKUP:
			case TASK_WEAPON_CREATE:
			case TASK_USE_SMALL_HULL:
			case TASK_WANDER:
			case TASK_IGNORE_OLD_ENEMIES:
			case TASK_ADD_HEALTH:
				throw new NotImplementedException();

			default:
				break;
		}
	}

	public virtual void RunTask(in Task_t task) {
		switch (task.Task) {
			case TASK_STOP_MOVING: {
					if (task.TaskData == 1)
						ChainRunTask(TASK_WAIT_FOR_MOVEMENT);
					else {
						SetIdealActivity(GetStoppedActivity());

						TaskComplete();
					}
					break;
				}

			case TASK_SET_ACTIVITY: {
					if (IsActivityStarted())
						TaskComplete();
				}
				break;

			case TASK_FACE_SAVEPOSITION:
			case TASK_FACE_IDEAL: {
					Assert(GetMotor()!.IsYawLocked() == false);

					GetMotor()!.UpdateYaw();

					if (FacingIdeal())
						TaskComplete();
					break;
				}

			case TASK_FACE_REASONABLE: {
					Assert(GetMotor()!.IsYawLocked() == false);

					GetMotor()!.UpdateYaw();

					if (FacingIdeal())
						TaskComplete();
					break;
				}

			case TASK_WAIT_PVS: {
					if (ShouldAlwaysThink() ||
						 Util.FindClientInPVS(Edict()) != null) {
						TaskComplete();
					}
					break;
				}

			case TASK_WAIT_INDEFINITE:
				break;

			case TASK_WAIT:
			case TASK_WAIT_RANDOM: {
					if (IsWaitFinished())
						TaskComplete();
					break;
				}

			case TASK_WAIT_FOR_MOVEMENT_STEP:
			case TASK_WAIT_FOR_MOVEMENT: {
					TaskComplete();
					break;
				}

			case TASK_PLAY_SCENE: {
					if (!IsInLockedScene())
						ClearSchedule("Playing a scene, but not in a scene!");
					break;
				}

			case TASK_FALL_TO_GROUND:
				if ((GetFlags() & EntityFlags.OnGround) != 0)
					TaskComplete();
				else if ((GetFlags() & EntityFlags.Fly) != 0)
					RemoveFlag(EntityFlags.Fly);
				else {
					if (IsWaitFinished()) {
						Vector3 maxs = WorldAlignMaxs() - new Vector3(.1f, .1f, .2f);
						Vector3 mins = WorldAlignMins() + new Vector3(.1f, .1f, 0);
						Vector3 start = GetAbsOrigin() + new Vector3(0, 0, .1f);
						Vector3 down = GetAbsOrigin();
						down.Z -= 0.2f;

						MoveProbe!.TraceHull(start, down, mins, maxs, Mask.NPCSolid, out Trace trace);

						if (trace.Ent != null) {
							SetGroundEntity(trace.Ent);
							TaskComplete();
						}
						else
							SetWait(4);
					}
				}
				break;

			case TASK_WANDER:
				break;

			case TASK_FREEZE:
				break;

			case TASK_GET_PATH_TO_RANDOM_NODE:
				break;

			case TASK_TURN_RIGHT:
			case TASK_TURN_LEFT:
			case TASK_PLAY_PRIVATE_SEQUENCE_FACE_ENEMY:
			case TASK_PLAY_SEQUENCE_FACE_ENEMY:
			case TASK_PLAY_SEQUENCE_FACE_TARGET:
			case TASK_PLAY_HINT_ACTIVITY:
			case TASK_PLAY_SEQUENCE:
			case TASK_PLAY_PRIVATE_SEQUENCE:
			case TASK_ADD_GESTURE_WAIT:
			case TASK_FACE_ENEMY:
			case TASK_FACE_PLAYER:
			case TASK_FIND_COVER_FROM_BEST_SOUND:
			case TASK_FACE_HINTNODE:
			case TASK_FACE_LASTPOSITION:
			case TASK_FACE_AWAY_FROM_SAVEPOSITION:
			case TASK_FACE_TARGET:
			case TASK_FACE_SCRIPT:
			case TASK_FACE_PATH:
			case TASK_WAIT_FACE_ENEMY:
			case TASK_WAIT_FACE_ENEMY_RANDOM:
			case TASK_WAIT_UNTIL_NO_DANGER_SOUND:
			case TASK_MOVE_TO_TARGET_RANGE:
			case TASK_MOVE_TO_GOAL_RANGE:
			case TASK_GET_PATH_TO_ENEMY_LOS:
			case TASK_GET_FLANK_RADIUS_PATH_TO_ENEMY_LOS:
			case TASK_GET_FLANK_ARC_PATH_TO_ENEMY_LOS:
			case TASK_GET_PATH_TO_ENEMY_LKP_LOS:
			case TASK_GET_PATH_AWAY_FROM_BEST_SOUND:
			case TASK_MOVE_AWAY_PATH:
			case TASK_WEAPON_RUN_PATH:
			case TASK_ITEM_RUN_PATH:
			case TASK_DIE:
			case TASK_WAIT_FOR_SPEAK_FINISH:
			case TASK_SCRIPT_RUN_TO_TARGET:
			case TASK_SCRIPT_WALK_TO_TARGET:
			case TASK_SCRIPT_CUSTOM_MOVE_TO_TARGET:
			case TASK_RANGE_ATTACK1:
			case TASK_RANGE_ATTACK2:
			case TASK_MELEE_ATTACK1:
			case TASK_MELEE_ATTACK2:
			case TASK_SPECIAL_ATTACK1:
			case TASK_SPECIAL_ATTACK2:
			case TASK_RELOAD:
			case TASK_SMALL_FLINCH:
			case TASK_BIG_FLINCH:
			case TASK_WAIT_FOR_SCRIPT:
			case TASK_PLAY_SCRIPT:
			case TASK_PLAY_SCRIPT_POST_IDLE:
			case TASK_ENABLE_SCRIPT:
			case TASK_RUN_PATH_FOR_UNITS:
			case TASK_WALK_PATH_FOR_UNITS:
			case TASK_RUN_PATH_FLEE:
			case TASK_WALK_PATH_WITHIN_DIST:
			case TASK_RUN_PATH_WITHIN_DIST:
			case TASK_WALK_PATH_TIMED:
			case TASK_RUN_PATH_TIMED:
			case TASK_WEAPON_PICKUP:
			case TASK_ITEM_PICKUP:
				throw new NotImplementedException();

			default:
				TaskComplete();
				break;
		}
	}

	int InterruptFromCondition(int condition) => AI_RemapFromGlobal(AI_IdIsLocal(condition) ? GetClassScheduleIdSpace().ConditionLocalToGlobal(condition) : condition);

	public virtual void SetCondition(int condition) {
		int interrupt = InterruptFromCondition(condition);

		if (interrupt == -1) {
			Assert(false);
			return;
		}

		Conditions.Set(interrupt);
	}

	public bool HasCondition(int condition) {
		int interrupt = InterruptFromCondition(condition);

		if (interrupt == -1) {
			Assert(false);
			return false;
		}

		bool ret = Conditions.IsBitSet(interrupt);
		return ret;
	}

	public void ClearCondition(int condition) {
		int interrupt = InterruptFromCondition(condition);

		if (interrupt == -1) {
			Assert(false);
			return;
		}

		Conditions.Clear(interrupt);
	}

	public void ClearAttackConditions() {
		ClearCondition((int)SCOND_t.COND_CAN_RANGE_ATTACK1);
		ClearCondition((int)SCOND_t.COND_CAN_RANGE_ATTACK2);
		ClearCondition((int)SCOND_t.COND_CAN_MELEE_ATTACK1);
		ClearCondition((int)SCOND_t.COND_CAN_MELEE_ATTACK2);
		ClearCondition((int)SCOND_t.COND_WEAPON_HAS_LOS);
		ClearCondition((int)SCOND_t.COND_WEAPON_BLOCKED_BY_FRIEND);
		ClearCondition((int)SCOND_t.COND_WEAPON_PLAYER_IN_SPREAD);
		ClearCondition((int)SCOND_t.COND_WEAPON_PLAYER_NEAR_TARGET);
		ClearCondition((int)SCOND_t.COND_WEAPON_SIGHT_OCCLUDED);
	}

	public virtual bool IsNavigationUrgent() => throw new NotImplementedException();

	public virtual bool ShouldProbeCollideAgainstEntity(BaseEntity entity) {
		if (entity.GetMoveType() == Source.MoveType.VPhysics) {
			if (ai_test_moveprobe_ignoresmall.GetBool() && IsNavigationUrgent()) {
				IPhysicsObject physics = entity.VPhysicsGetObject()!;

				if (physics.IsMoveable() && physics.GetMass() < 40.0)
					return false;
			}
		}

		return true;
	}

	public virtual bool ShouldPlayerAvoid() {
		if (GetState() == NPCState.Script)
			return true;

		if (IsInAScript())
			return true;

		if (IsInLockedScene() == true)
			return true;

		if (HasSpawnFlags(SF_NPC_ALTCOLLISION))
			return true;

		return false;
	}

	public virtual bool IsCrouching() => (CapabilitiesGet() & Server.Capability.Duck) != 0 && Crouching;

	public virtual bool Stand() {
		if (ForceCrouch)
			return false;

		Crouching = false;
		DesireStand();
		return true;
	}

	public void DesireStand() => CrouchDesired = false;

	public virtual void OnChangeActivity(Activity newActivity) {
		if (newActivity == Activity.ACT_RUN ||
			 newActivity == Activity.ACT_RUN_AIM ||
			 newActivity == Activity.ACT_WALK) {
			Stand();
		}
	}

	public static bool IsActivityMovementPhased(Activity activity) {
		switch (activity) {
			case Activity.ACT_WALK:
			case Activity.ACT_WALK_AIM:
			case Activity.ACT_WALK_CROUCH:
			case Activity.ACT_WALK_CROUCH_AIM:
			case Activity.ACT_RUN:
			case Activity.ACT_RUN_AIM:
			case Activity.ACT_RUN_CROUCH:
			case Activity.ACT_RUN_CROUCH_AIM:
			case Activity.ACT_RUN_PROTECTED:
				return true;
		}
		return false;
	}

	public bool HaveSequenceForActivity(Activity activity) => GetModelPtr() != null && GetModelPtr()!.HaveSequenceForActivity((int)activity);

	public virtual Vector3 EyeOffset(Activity activity) {
		if ((CapabilitiesGet() & Server.Capability.Duck) != 0) {
			if (IsCrouchedActivity(activity))
				return GetCrouchEyeOffset();
		}

		if (IsCrouching())
			return GetCrouchEyeOffset();

		return DefaultEyeOffset * GetModelScale();
	}

	public virtual bool IsCrouchedActivity(Activity activity) {
		Activity realActivity = TranslateActivity(activity, out _);

		switch (realActivity) {
			case Activity.ACT_RELOAD_LOW:
			case Activity.ACT_COVER_LOW:
			case Activity.ACT_COVER_PISTOL_LOW:
			case Activity.ACT_COVER_SMG1_LOW:
			case Activity.ACT_RELOAD_SMG1_LOW:
				return true;
		}

		return false;
	}

	public override void AddEntityRelationship(BaseEntity entity, Disposition disposition, int priority) {
		base.AddEntityRelationship(entity, disposition, priority);
	}

	public override void AddClassRelationship(Class_T classType, Disposition disposition, int priority) {
		base.AddClassRelationship(classType, disposition, priority);
	}

	public virtual int SelectSchedule() => BaseSelectSchedule();

	int BaseSelectSchedule() {
		if (HasCondition((int)SCOND_t.COND_FLOATING_OFF_GROUND)) {
			SetGravity(1.0f);
			SetGroundEntity(null);
			return SCHED_FALL_TO_GROUND;
		}

		switch (NPCState) {
			case NPCState.None:
				DevWarning(2, "NPC_STATE IS NONE!\n");
				break;

			case NPCState.Prone:
				return SCHED_IDLE_STAND;

			case NPCState.Idle:
				AssertMsg(GetEnemy() == null, "NPC has enemy but is not in combat state?");
				return SelectIdleSchedule();

			case NPCState.Alert:
				AssertMsg(GetEnemy() == null, "NPC has enemy but is not in combat state?");
				return SelectAlertSchedule();

			case NPCState.Combat:
				return SelectCombatSchedule();

			case NPCState.Dead:
				return SelectDeadSchedule();

			case NPCState.Script:
				return SelectScriptSchedule();

			default:
				DevWarning(2, "Invalid State for SelectSchedule!\n");
				break;
		}

		return SCHED_FAIL;
	}

	public virtual int SelectInteractionSchedule() => throw new NotImplementedException();

	public virtual int SelectIdleSchedule() {
		if (ForcedInteractionPartner.Get() != null)
			return SelectInteractionSchedule();

		int sched = SelectFlinchSchedule();
		if (sched != SCHED_NONE)
			return sched;

		if (HasCondition((int)SCOND_t.COND_HEAR_DANGER) ||
			 HasCondition((int)SCOND_t.COND_HEAR_COMBAT) ||
			 HasCondition((int)SCOND_t.COND_HEAR_WORLD) ||
			 HasCondition((int)SCOND_t.COND_HEAR_BULLET_IMPACT) ||
			 HasCondition((int)SCOND_t.COND_HEAR_PLAYER)) {
			return SCHED_ALERT_FACE_BESTSOUND;
		}

		return SCHED_IDLE_STAND;
	}

	public virtual int SelectAlertSchedule() {
		if (ForcedInteractionPartner.Get() != null)
			return SelectInteractionSchedule();

		int sched = SelectFlinchSchedule();
		if (sched != SCHED_NONE)
			return sched;

		if (HasCondition((int)SCOND_t.COND_ENEMY_DEAD) && SelectWeightedSequence(Activity.ACT_VICTORY_DANCE) != StudioHdr.ACTIVITY_NOT_AVAILABLE)
			return SCHED_ALERT_SCAN;

		if (IsPlayerAlly() && HasCondition((int)SCOND_t.COND_HEAR_COMBAT))
			return SCHED_ALERT_REACT_TO_COMBAT_SOUND;

		if (HasCondition((int)SCOND_t.COND_HEAR_DANGER) ||
				  HasCondition((int)SCOND_t.COND_HEAR_PLAYER) ||
				  HasCondition((int)SCOND_t.COND_HEAR_WORLD) ||
				  HasCondition((int)SCOND_t.COND_HEAR_BULLET_IMPACT) ||
				  HasCondition((int)SCOND_t.COND_HEAR_COMBAT)) {
			return SCHED_ALERT_FACE_BESTSOUND;
		}

		return SCHED_ALERT_STAND;
	}

	public virtual int SelectCombatSchedule() => throw new NotImplementedException();

	public virtual int SelectDeadSchedule() => throw new NotImplementedException();

	public virtual int SelectScriptSchedule() {
		Assert(Cine.Get() != null);
		if (Cine.Get() != null)
			return SCHED_AISCRIPT;

		DevWarning(2, $"Script failed for {GetClassname()}\n");
		CineCleanup();
		return SCHED_IDLE_STAND;
	}

	public virtual int SelectFlinchSchedule() {
		if (!HasCondition((int)SCOND_t.COND_HEAVY_DAMAGE))
			return SCHED_NONE;

		if (HasMemory(AI_MemoryFlags.Flinched))
			return SCHED_NONE;

		if (!CanFlinch())
			return SCHED_NONE;

		Activity flinchActivity = GetFlinchActivity(true, false);
		if (HaveSequenceForActivity(flinchActivity))
			return SCHED_BIG_FLINCH;

		return SCHED_NONE;
	}

	public virtual Activity GetFlinchActivity(bool heavyDamage, bool gesture) => throw new NotImplementedException();

	public virtual bool CanFlinch() {
		if (IsCurSchedule(SCHED_BIG_FLINCH))
			return false;

		if (NextFlinchTime >= gpGlobals.CurTime)
			return false;

		return true;
	}

	public void PlayFlinchGesture() => throw new NotImplementedException();

	public void CheckFlinches() {
		if (IsCurSchedule(SCHED_BIG_FLINCH)) {
			ClearCondition((int)SCOND_t.COND_LIGHT_DAMAGE);
			ClearCondition((int)SCOND_t.COND_HEAVY_DAMAGE);
		}

		if (HasCondition((int)SCOND_t.COND_HEAVY_DAMAGE)) {
			if (HasMemory(AI_MemoryFlags.Flinched))
				ClearCondition((int)SCOND_t.COND_HEAVY_DAMAGE);
			else if (!HasInterruptCondition((int)SCOND_t.COND_HEAVY_DAMAGE))
				PlayFlinchGesture();
		}
		else if (HasCondition((int)SCOND_t.COND_LIGHT_DAMAGE))
			PlayFlinchGesture();

		if (HasMemory(AI_MemoryFlags.Flinched) && gpGlobals.CurTime > NextFlinchTime)
			Forget(AI_MemoryFlags.Flinched);
	}

	public virtual NPCState SelectIdleIdealState() {
		if (HasCondition((int)SCOND_t.COND_NEW_ENEMY) ||
			 HasCondition((int)SCOND_t.COND_SEE_ENEMY)) {
			return NPCState.Combat;
		}

		if (HasCondition((int)SCOND_t.COND_LIGHT_DAMAGE) ||
			 HasCondition((int)SCOND_t.COND_HEAVY_DAMAGE)) {
			Vector3 enemyLKP;

			if (GetEnemy() != null)
				throw new NotImplementedException();
			else
				enemyLKP = WorldSpaceCenter() + (g_vecAttackDir * 128);

			GetMotor()!.SetIdealYawToTarget(enemyLKP);

			return NPCState.Alert;
		}

		if (HasInterruptCondition((int)SCOND_t.COND_SMELL))
			return NPCState.Alert;

		return NPCState.Invalid;
	}

	public virtual NPCState SelectAlertIdealState() {
		if (HasCondition((int)SCOND_t.COND_NEW_ENEMY) ||
			 HasCondition((int)SCOND_t.COND_SEE_ENEMY) ||
			 GetEnemy() != null) {
			return NPCState.Combat;
		}

		if (HasCondition((int)SCOND_t.COND_LIGHT_DAMAGE) ||
			 HasCondition((int)SCOND_t.COND_HEAVY_DAMAGE)) {
			Vector3 enemyLKP;

			if (GetEnemy() != null)
				throw new NotImplementedException();
			else
				enemyLKP = WorldSpaceCenter() + (g_vecAttackDir * 128);

			GetMotor()!.SetIdealYawToTarget(enemyLKP);

			return NPCState.Alert;
		}

		if (ShouldGoToIdleState())
			return NPCState.Idle;

		return NPCState.Invalid;
	}

	public virtual NPCState SelectScriptIdealState() {
		if (HasCondition((int)SCOND_t.COND_TASK_FAILED) ||
			 HasCondition((int)SCOND_t.COND_LIGHT_DAMAGE) ||
			 HasCondition((int)SCOND_t.COND_HEAVY_DAMAGE)) {
			ExitScriptedSequence();
		}

		if (IdealNPCState == NPCState.Idle) {
			NPCState = NPCState.Idle;
			NPCState idealState = SelectIdealState();
			NPCState = NPCState.Script;
			return idealState;
		}

		return NPCState.Invalid;
	}

	public virtual NPCState SelectIdealState() {
		switch (NPCState) {
			case NPCState.Idle: {
					NPCState state = SelectIdleIdealState();
					if (state != NPCState.Invalid)
						return state;
				}
				break;

			case NPCState.Alert: {
					NPCState state = SelectAlertIdealState();
					if (state != NPCState.Invalid)
						return state;
				}
				break;

			case NPCState.Combat: {
					if (GetEnemy() == null) {
						DevWarning(2, "***Combat state with no enemy!\n");
						return NPCState.Alert;
					}
					break;
				}
			case NPCState.Script: {
					NPCState state = SelectScriptIdealState();
					if (state != NPCState.Invalid)
						return state;
				}
				break;

			case NPCState.Dead:
				return NPCState.Dead;
		}

		return IdealNPCState;
	}

	public virtual bool ShouldGoToIdleState() => false;

	public virtual void GatherConditions() {
		ConditionsGatheredValue = true;

		if (gpGlobals.CurTime > TimePingEffect && TimePingEffect > 0.0f) {
			DispatchUpdateTransmitState();
			TimePingEffect = 0.0f;
		}

		if (NPCState != NPCState.None && NPCState != NPCState.Dead) {
			if (FacingIdeal())
				Forget(AI_MemoryFlags.Turning);

			bool forcedGather = ForceConditionsGather;
			ForceConditionsGather = false;

			if (FnThink != CallNPCThinkPtr) {
				if (Util.FindClientInPVS(Edict()) != null)
					SetCondition((int)SCOND_t.COND_IN_PVS);
				else
					ClearCondition((int)SCOND_t.COND_IN_PVS);
			}

			if (!IsFlaggedEfficient() &&
				 (forcedGather ||
				   HasCondition((int)SCOND_t.COND_IN_PVS) ||
				   ShouldAlwaysThink() ||
				   NPCState == NPCState.Combat)) {
				CheckOnGround();

				if (ShouldPlayIdleSound())
					IdleSound();


				if (Weapon_IsBetterAvailable())
					SetCondition((int)SCOND_t.COND_BETTER_WEAPON_AVAILABLE);

				if (GetCurSchedule() != null &&
					(NPCState == NPCState.Idle || NPCState == NPCState.Alert) &&
					 GetEnemy() != null &&
					 !HasCondition((int)SCOND_t.COND_NEW_ENEMY) &&
					 GetCurSchedule()!.HasInterrupt((int)SCOND_t.COND_NEW_ENEMY)) {
					DevMsg(2, "Had to force COND_NEW_ENEMY\n");
					SetCondition((int)SCOND_t.COND_NEW_ENEMY);
				}
			}
			else
				ClearSenseConditions();

			if (GetEnemy() != null) {
				if (!IsFlaggedEfficient()) {
					GatherEnemyConditions(GetEnemy()!);
					LastEnemyTime = gpGlobals.CurTime;
				}
				else
					SetEnemy(null);
			}

			CheckAmmo();

			CheckFlinches();
		}
		else
			ClearCondition((int)SCOND_t.COND_IN_PVS);
	}

	public virtual void GatherEnemyConditions(BaseEntity enemy) => throw new NotImplementedException();

	public virtual void CheckAmmo() { }

	public void NotifyPushMove() => CheckOnGroundTimer.Set(0.5f);

	public bool IsMovingToPickupWeapon() => IsCurSchedule(SCHED_NEW_WEAPON);

	public bool ShouldLookForBetterWeapon() {
		if (NextWeaponSearchTime > gpGlobals.CurTime)
			return false;

		if ((CapabilitiesGet() & Server.Capability.UseWeapons) == 0)
			return false;

		if (GetActiveWeapon() != null && NPCState == NPCState.Combat)
			return false;

		if (IsMovingToPickupWeapon())
			return false;

		if (!IsPlayerAlly() && GetActiveWeapon() != null)
			return false;

		if (IsInAScript())
			return false;

		return true;
	}

	public bool Weapon_IsBetterAvailable() {
		if (PendingWeapon != null)
			return true;

		if (ShouldLookForBetterWeapon()) {
			if (GetActiveWeapon() != null)
				NextWeaponSearchTime = gpGlobals.CurTime + 2;
			else {
				if (IsInPlayerSquad())
					NextWeaponSearchTime = gpGlobals.CurTime + 1;
				else
					NextWeaponSearchTime = gpGlobals.CurTime + 2;
			}

			if (Weapon_FindUsable(WEAPON_SEARCH_DELTA) != null)
				return true;
		}

		return false;
	}

	public static readonly Vector3 WEAPON_SEARCH_DELTA = new(540, 540, 100);

	public bool IsInPlayerSquad() {
		if (Squad == null)
			return false;

		throw new NotImplementedException();
	}

	public BaseEntity? GetTarget() => TargetEnt.Get();

	public float EnemyDistance(BaseEntity enemy) {
		Vector3 enemyDelta = enemy.WorldSpaceCenter() - WorldSpaceCenter();

		float enemyHeight = enemy.CollisionProp().OBBSize().Z;
		float myHeight = CollisionProp().OBBSize().Z;

		float maxZDist = (enemyHeight + myHeight) * 0.5f;

		if (enemyDelta.Z > maxZDist)
			enemyDelta.Z -= maxZDist;
		else if (enemyDelta.Z < -maxZDist)
			enemyDelta.Z += maxZDist;
		else
			enemyDelta.Z = 0;

		return enemyDelta.Length();
	}

	public void CheckOnGround() {
		bool scriptedWait = IsCurSchedule(SCHED_WAIT_FOR_SCRIPT) || (Cine.Get() != null && ScriptState == ScriptStateType.Wait);
		if (!scriptedWait && !HasCondition((int)SCOND_t.COND_FLOATING_OFF_GROUND)) {
			if (GetMoveParent() != null)
				return;

			if ((GetState() == NPCState.Script) && (GetFlags() & EntityFlags.Fly) != 0)
				return;

			if ((GetNavType() == Navigation.Ground) && (GetMoveType() != Source.MoveType.VPhysics) && (GetMoveType() != Source.MoveType.None)) {
				if (CheckOnGroundTimer.Expired()) {
					CheckOnGroundTimer.Set(0.5f);

					Vector3 maxs = WorldAlignMaxs();
					Vector3 mins = WorldAlignMins();

					if (mins != maxs) {
						maxs -= new Vector3(0.0f, 0.0f, 0.2f);

						Vector3 start = GetAbsOrigin() + new Vector3(0, 0, .1f);
						Vector3 down = GetAbsOrigin();
						down.Z -= 4.0f;

						MoveProbe!.TraceHull(start, down, mins, maxs, Mask.NPCSolid, out Trace trace);

						if (trace.Fraction == 1.0) {
							SetCondition((int)SCOND_t.COND_FLOATING_OFF_GROUND);
							SetGroundEntity(null);
						}
						else {
							if (trace.StartSolid && trace.Ent!.GetMoveType() == Source.MoveType.VPhysics &&
								trace.Ent.VPhysicsGetObject() != null && trace.Ent.VPhysicsGetObject()!.GetMass() < VPHYSICS_LARGE_OBJECT_MASS) {
								CheckOnGroundTimer.Set(0.1f);
								NPCPhysics_CreateSolver(this, trace.Ent, true, 0.25f);
								VPhysicsGetObject()?.RecheckContactPoints();
							}

							if (trace.Ent != null && trace.Ent != GetGroundEntity())
								SetGroundEntity(trace.Ent);
						}
					}
				}
			}
		}
		else {
			if (scriptedWait || GetMoveParent() != null || (GetFlags() & EntityFlags.OnGround) != 0 || GetNavType() != Navigation.Ground)
				ClearCondition((int)SCOND_t.COND_FLOATING_OFF_GROUND);
		}
	}

	public Navigation GetNavType() => Navigator!.GetNavType();

	public virtual bool ShouldPlayIdleSound() {
		if ((NPCState == NPCState.Idle || NPCState == NPCState.Alert) &&
			   RandomInt(0, 99) == 0 && !HasSpawnFlags(SF_NPC_GAG)) {
			return true;
		}

		return false;
	}

	public virtual void IdleSound() { }
	public virtual void LostEnemySound() { }
	public virtual void FoundEnemySound() { }

	public virtual bool ShouldAlwaysThink() => HasSpawnFlags(SF_NPC_ALWAYSTHINK);

	static readonly int[] SenseConditionsToClear = [
		(int)SCOND_t.COND_SEE_HATE,
		(int)SCOND_t.COND_SEE_DISLIKE,
		(int)SCOND_t.COND_SEE_ENEMY,
		(int)SCOND_t.COND_SEE_FEAR,
		(int)SCOND_t.COND_SEE_NEMESIS,
		(int)SCOND_t.COND_SEE_PLAYER,
		(int)SCOND_t.COND_HEAR_DANGER,
		(int)SCOND_t.COND_HEAR_COMBAT,
		(int)SCOND_t.COND_HEAR_WORLD,
		(int)SCOND_t.COND_HEAR_PLAYER,
		(int)SCOND_t.COND_HEAR_THUMPER,
		(int)SCOND_t.COND_HEAR_BUGBAIT,
		(int)SCOND_t.COND_HEAR_PHYSICS_DANGER,
		(int)SCOND_t.COND_HEAR_MOVE_AWAY,
		(int)SCOND_t.COND_SMELL,
	];

	public void ClearSenseConditions() => ClearConditions(SenseConditionsToClear);

	public virtual void OnSeeEntity(BaseEntity entity) { }

	public virtual bool ShouldNotDistanceCull() => false;

	public virtual bool ShouldIgnoreSound(ref WorldSoundInstance sound) => false;

	public virtual SoundPriority GetSoundPriority(ref WorldSoundInstance sound) {
		SoundInstanceType soundTypeNoContext = sound.SoundTypeNoContext();
		SoundInstanceType soundContext = sound.SoundContext();

		if ((soundTypeNoContext & SoundInstanceType.Danger) != 0)
			return SoundPriority.Highest;

		if ((soundTypeNoContext & SoundInstanceType.Combat) != 0) {
			if ((soundContext & SoundInstanceType.ContextExplosion) != 0)
				return SoundPriority.VeryHigh;

			return SoundPriority.High;
		}

		return SoundPriority.Normal;
	}

	public void ForceDecisionThink() {
		NextDecisionTime = 0;
		SetEfficiency(AI_Efficiency.Normal);
	}

	public virtual bool IsValidEnemy(BaseEntity enemy) => throw new NotImplementedException();

	public virtual bool CanBeAnEnemyOf(BaseEntity enemy) {
		if (GetSleepState() > AI_SleepState.WaitingForThreat)
			return false;

		return true;
	}

	public void RemoveIgnoredConditions() {
		ConditionsPreIgnore = Conditions;
		Conditions = Conditions.And(InverseIgnoreConditions);

		if (NPCState == NPCState.Script && Cine.Get() != null)
			Cine.Get()!.RemoveIgnoredConditions();
	}

	public void TryRestoreHull() {
		if (IsUsingSmallHull() && GetCurSchedule() != null) {
			Vector3 upBit = GetAbsOrigin();
			upBit.Z += 1;

			Util.TraceHull(GetAbsOrigin(), upBit, GetHullMins(), GetHullMaxs(), Mask.Solid, this, Source.CollisionGroup.None, out Trace tr);
			if (!tr.StartSolid && (tr.Fraction == 1.0))
				SetHullSizeNormal();
		}
	}

	public virtual void PrescheduleThink() {
		if ((CapabilitiesGet() & Server.Capability.UseWeapons) != 0 && (DesiredWeaponState == DesiredWeaponState.Holstered || DesiredWeaponState == DesiredWeaponState.Unholstered || DesiredWeaponState == DesiredWeaponState.HolsteredDestroyed)) {
			if (IsAlive() && !IsInAScript()) {
				if (!IsCurSchedule(SCHED_MELEE_ATTACK1, false) && !IsCurSchedule(SCHED_MELEE_ATTACK2, false) &&
					 !IsCurSchedule(SCHED_RANGE_ATTACK1, false) && !IsCurSchedule(SCHED_RANGE_ATTACK2, false)) {
					if (DesiredWeaponState == DesiredWeaponState.Holstered || DesiredWeaponState == DesiredWeaponState.HolsteredDestroyed)
						HolsterWeapon();
					else if (DesiredWeaponState == DesiredWeaponState.Unholstered)
						UnholsterWeapon();
				}
			}
			else
				DesiredWeaponState = DesiredWeaponState.Ignore;
		}
	}

	public virtual void PostscheduleThink() { }

	public virtual void ClearTransientConditions() {
		ClearCondition((int)SCOND_t.COND_LIGHT_DAMAGE);
		ClearCondition((int)SCOND_t.COND_HEAVY_DAMAGE);
		ClearCondition((int)SCOND_t.COND_PHYSICS_DAMAGE);
		ClearCondition((int)SCOND_t.COND_PLAYER_PUSHING);
	}

	public virtual AI_BehaviorBase? GetRunningBehavior() => null;

	public Capability CapabilitiesAdd(Capability capability) {
		Capability |= capability;

		return Capability;
	}

	public void SetHullSizeNormal(bool force = false) {
		if (IsUsingSmallHullValue || force) {
			float scale = GetModelScale();
			Vector3 mins = GetHullMins() * scale;
			Vector3 maxs = GetHullMaxs() * scale;

			Util.SetSize(this, mins, maxs);

			IsUsingSmallHullValue = false;
			if (VPhysicsGetObject() != null)
				SetupVPhysicsHull();
		}
	}

	public virtual int GetSoundInterests() {
		return (int)(SoundInstanceType.World | SoundInstanceType.Combat | SoundInstanceType.Player | SoundInstanceType.PlayerVehicle |
			SoundInstanceType.BulletImpact);
	}

	public virtual float MaxYawSpeed() {
		return 45;
	}

	public virtual float GetTimeToNavGoal() => throw new NotImplementedException();

	public void VacateStrategySlot() => throw new NotImplementedException();

	public virtual bool CreateVPhysics() {
		if (IsAlive() && VPhysicsGetObject() == null)
			SetupVPhysicsHull();
		return true;
	}

	public virtual void NPCInit() {
		if (!g_pGameRules.FAllowNPCs()) {
			Util.Remove(this);
			return;
		}

		if (IsWaitingToRappel())
			AddFlag(EntityFlags.Fly);

		AddFlag(EntityFlags.AimTarget | EntityFlags.NPC);
		AddSolidFlags(SolidFlags.NotStandable);

		OriginalYaw = GetAbsAngles().Y;

		SetBlocksLOS(false);

		SetGravity(1.0f);
		m_takedamage = (byte)Damage.Yes;
		GetMotor()!.SetIdealYaw(GetLocalAngles().Y);
		MaxHealth = Health;
		LifeState = (int)Source.LifeState.Alive;
		SetIdealState(NPCState.Idle);
		SetIdealActivity(Activity.ACT_IDLE);
		SetActivity(Activity.ACT_IDLE);

		ClearCommandGoal();

		ClearSchedule("Initializing NPC");
		GetNavigator()!.ClearGoal();
		InitBoneControllers();
		if (GetModelPtr() != null) {
			ResetActivityIndexes();
			ResetEventIndexes();
		}

		SetHintNode(null);

		Memory = AI_MemoryFlags.Clear;

		SetEnemy(null);

		DistTooFar = 1024.0f;
		SetDistLook(2048.0f);

		if (HasSpawnFlags(SF_NPC_LONG_RANGE)) {
			DistTooFar = 1e9f;
			SetDistLook(6000.0f);
		}

		Conditions.ClearAll();

		SetDefaultEyeOffset();

		if ((CapabilitiesGet() & Server.Capability.UseWeapons) != 0) {
			if (SpawnEquipment != null && SpawnEquipment != "0") {
				BaseCombatWeapon? weapon = Weapon_Create(SpawnEquipment);
				if (weapon != null) {
					if (GetEntityName() != null)
						weapon.SetName($"{GetEntityName()}_weapon");

					if (((EntityEffects)Effects & EntityEffects.NoShadow) != 0)
						weapon.AddEffects(EntityEffects.NoShadow);

					Weapon_Equip(weapon);
				}
			}
		}

		FnUse = NPCUse;

		SetThink(NPCInitThink);
		SetNextThink(gpGlobals.CurTime + 0.01f);

		ForceGatherConditions();

		if (HasSpawnFlags(SF_NPC_WAIT_FOR_SCRIPT)) {
			string? startSequence = AI_ScriptedSequence.GetSpawnPreIdleSequenceForScript(this);
			if (startSequence != null)
				SetSequence(LookupSequence(startSequence));
		}

		CreateVPhysics();

		if (HasSpawnFlags(SF_NPC_START_EFFICIENT))
			SetEfficiency(AI_Efficiency.Efficient);

		FadeCorpse = ShouldFadeOnDeath();

		GiveUpOnDeadEnemyTimer.Set(0.75f, 2.0f);

		TimeLastMovement = float.MaxValue;

		IgnoreDangerSoundsUntil = 0;

		SetDeathPose((int)Activity.ACT_INVALID);
		SetDeathPoseFrame(0);

		EnemiesSerialNumber = -1;
	}

	public void NPCInitThink() {
		InitRelationshipTable();

		StartNPC();

		PostNPCInit();

		if (GetSleepState() == AI_SleepState.AutoPVS) {
			AddSleepFlags(AI_SleepFlags.AutoPVS);
			SetSleepState(AI_SleepState.Awake);
		}

		if (GetSleepState() == AI_SleepState.AutoPVSAfterPVS) {
			AddSleepFlags(AI_SleepFlags.AutoPVSAfterPVS);
			SetSleepState(AI_SleepState.Awake);
		}

		if (GetSleepState() > AI_SleepState.Awake)
			Sleep();

		LastRealThinkTime = gpGlobals.CurTime;
	}

	public virtual void PostNPCInit() { }

	public virtual void StartNPC() {
		if ((GetMoveType() != Source.MoveType.Fly) && (GetMoveType() != Source.MoveType.FlyGravity) &&
			 (CapabilitiesGet() & Server.Capability.MoveFly) == 0 &&
			 !HasSpawnFlags(SF_NPC_FALL_TO_GROUND) && !IsWaitingToRappel() && GetMoveParent() == null) {
			Vector3 origin = GetLocalOrigin();

			if (!GetMoveProbe()!.FloorPoint(origin + new Vector3(0, 0, 0.1f), Mask.NPCSolid, 0, -2048, out origin)) {
				Warning($"NPC {base.GetClassname()} stuck in wall--level design error at ({GetAbsOrigin().X:F2} {GetAbsOrigin().Y:F2} {GetAbsOrigin().Z:F2})\n");
				if (developer.GetInt() > 1)
					DebugOverlays |= DebugOverlayBits.BBox;
			}

			SetLocalOrigin(origin);
		}
		else
			SetGroundEntity(null);

		if (Target != null) {
			SetGoalEnt(gEntList.FindEntityByName(null, Target));

			if (GetGoalEnt() == null)
				Warning($"ReadyNPC()--{GetClassname()} couldn't find target {Target}\n");
			else
				StartTargetHandling(GetGoalEnt()!);
		}

		InitSquad();

		ThinkSet(CallNPCThinkPtr, 0, default);

		if (TimeLastSpawn != gpGlobals.CurTime) {
			SpawnedThisFrame = 0;
			TimeLastSpawn = gpGlobals.CurTime;
		}

		ReadOnlySpan<float> nextThinkTimes = [
			.0f, .150f, .075f, .225f, .030f, .180f, .120f, .270f, .045f, .210f, .105f, .255f, .015f, .165f, .090f, .240f, .135f, .060f, .195f, .285f
		];

		SetNextThink(gpGlobals.CurTime + nextThinkTimes[SpawnedThisFrame % 20]);

		SpawnedThisFrame++;

		ScriptArrivalActivity = AIN_DEF_ACTIVITY;
		ScriptArrivalSequence = null;

		if (HasSpawnFlags(SF_NPC_WAIT_FOR_SCRIPT)) {
			SetState(NPCState.Idle);
			Activity = IdealActivity;
			IdealSequence = GetSequence();
			SetSchedule(SCHED_WAIT_FOR_SCRIPT);
		}
	}

	public virtual void StartTargetHandling(BaseEntity targetEnt) => throw new NotImplementedException();

	public void InitRelationshipTable() {
		AddRelationship(RelationshipString, null);
	}

	public void AddRelationship(ReadOnlySpan<char> relationship, BaseEntity? activator) {
		string parseString = new(relationship.Length > 999 ? relationship[..999] : relationship);

		string[] tokens = parseString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		int tokenIndex = 0;

		string? entityString = tokenIndex < tokens.Length ? tokens[tokenIndex++] : null;
		while (entityString != null) {
			string? dispositionString = tokenIndex < tokens.Length ? tokens[tokenIndex++] : null;
			Disposition disposition = Disposition.NU;
			if (dispositionString != null) {
				if (stricmp(dispositionString, "D_HT") == 0)
					disposition = Disposition.HT;
				else if (stricmp(dispositionString, "D_FR") == 0)
					disposition = Disposition.FR;
				else if (stricmp(dispositionString, "D_LI") == 0)
					disposition = Disposition.LI;
				else if (stricmp(dispositionString, "D_NU") == 0)
					disposition = Disposition.NU;
				else {
					disposition = Disposition.NU;
					Warning($"***ERROR***\nBad relationship type ({dispositionString}) to unknown entity ({entityString})!\n");
					Assert(false);
					return;
				}
			}
			else {
				Warning($"Can't parse relationship info ({relationship}) - Expecting 'name [D_HT, D_FR, D_LI, D_NU] [1-99]'\n");
				Assert(false);
				return;
			}

			string? priorityString = tokenIndex < tokens.Length ? tokens[tokenIndex++] : null;
			int priority = (priorityString != null) ? atoi(priorityString) : DEF_RELATIONSHIP_PRIORITY;

			bool foundEntity = false;

			BaseEntity? entity = gEntList.FindEntityByName(null, entityString);
			while (entity != null) {
				foundEntity = true;
				AddEntityRelationship(entity, disposition, priority);
				entity = gEntList.FindEntityByName(entity, entityString);
			}

			if (!foundEntity) {
				if (stricmp("player", entityString) == 0 || stricmp("!player", entityString) == 0)
					AddClassRelationship(Class_T.Player, disposition, priority);
				else {
					BaseEntity? pEntity = CanCreateEntityClass(entityString) ? CreateEntityByName(entityString) : null;
					if (pEntity != null) {
						AddClassRelationship(pEntity.Classify(), disposition, priority);
						Util.RemoveImmediate(pEntity);
					}
					else
						DevWarning($"Couldn't set relationship to unknown entity or class ({entityString})!\n");
				}
			}

			entityString = tokenIndex < tokens.Length ? tokens[tokenIndex++] : null;
		}
	}

	public void SetState(NPCState state) {
		NPCState oldState;

		oldState = NPCState;

		if (state != NPCState)
			LastStateChangeTime = gpGlobals.CurTime;

		switch (state) {
			case NPCState.Idle:
				if (GetEnemy() != null) {
					SetEnemy(null);
					DevMsg(2, "Stripped\n");
				}
				break;
		}

		bool notifyChange = false;

		if (NPCState != state)
			notifyChange = true;

		NPCState = state;
		SetIdealState(state);

		if (notifyChange)
			OnStateChange(oldState, NPCState);
	}

	public void SetIdealState(NPCState idealState) {
		if (idealState != IdealNPCState)
			IdealNPCState = idealState;
	}

	public virtual void OnStateChange(NPCState oldState, NPCState newState) { }

	public Activity GetActivity() => Activity;

	public virtual void SetActivity(Activity newActivity) {
		if (Activity == newActivity)
			return;

		if (newActivity != Activity.ACT_RESET && Activity == Activity.ACT_TRANSITION && IdealActivity != Activity.ACT_DO_NOT_DISTURB)
			return;

		if (ai_sequence_debug.GetBool() == true && (DebugOverlays & DebugOverlayBits.NPCSelected) != 0)
			DevMsg($"SetActivity : {GetClassname()}: {GetActivityName(GetActivity())} -> {GetActivityName(newActivity)}\n");

		if (GetModelPtr() == null)
			return;

		IdealActivity = newActivity;

		ResolveActivityToSequence(IdealActivity, ref IdealSequence, ref IdealTranslatedActivity, ref IdealWeaponActivity);

		SetActivityAndSequence(IdealActivity, IdealSequence, IdealTranslatedActivity, IdealWeaponActivity);
	}

	public void SetIdealActivity(Activity newActivity) {
		if (newActivity == Activity.ACT_TRANSITION) {
			Assert(false);
			return;
		}

		if (ai_sequence_debug.GetBool() == true && (DebugOverlays & DebugOverlayBits.NPCSelected) != 0)
			DevMsg($"SetIdealActivity : {GetClassname()}: {GetActivityName(GetActivity())} -> {GetActivityName(newActivity)}\n");

		if (newActivity == Activity.ACT_RESET) {
			SetActivity(Activity.ACT_RESET);
			return;
		}

		IdealActivity = newActivity;

		if (newActivity == Activity.ACT_DO_NOT_DISTURB)
			return;

		if (GetModelPtr() == null)
			return;

		ResolveActivityToSequence(IdealActivity, ref IdealSequence, ref IdealTranslatedActivity, ref IdealWeaponActivity);
	}

	public override Activity NPC_TranslateActivity(Activity newActivity) {
		Assert(newActivity != Activity.ACT_INVALID);

		if (newActivity == Activity.ACT_RANGE_ATTACK1) {
			if (IsCrouching())
				newActivity = Activity.ACT_RANGE_ATTACK1_LOW;
		}
		else if (newActivity == Activity.ACT_RELOAD) {
			if (IsCrouching())
				newActivity = Activity.ACT_RELOAD_LOW;
		}
		else if (newActivity == Activity.ACT_IDLE) {
			if (IsCrouching())
				newActivity = Activity.ACT_CROUCHIDLE;
		}
		else if (newActivity == Activity.ACT_IDLE_ANGRY_SMG1) {
			if (IsCrouching())
				newActivity = Activity.ACT_RANGE_AIM_LOW;
		}

		if ((CapabilitiesGet() & Server.Capability.Duck) != 0) {
			if (newActivity == Activity.ACT_RELOAD)
				return GetReloadActivity(GetHintNode());
			else if ((newActivity == Activity.ACT_COVER) ||
					 (newActivity == Activity.ACT_IDLE && HasMemory(AI_MemoryFlags.Incover))) {
				Activity coverActivity = GetCoverActivity(GetHintNode());
				if (SelectWeightedSequence(coverActivity) == StudioHdr.ACTIVITY_NOT_AVAILABLE)
					coverActivity = Activity.ACT_IDLE;

				return coverActivity;
			}
		}
		return newActivity;
	}

	public AI_Hint? GetHintNode() => HintNode.Get();

	public virtual Activity GetReloadActivity(AI_Hint? hint) => throw new NotImplementedException();
	public virtual Activity GetCoverActivity(AI_Hint? hint) => throw new NotImplementedException();

	static readonly List<Activity> sUniqueActivities = [];

	public Activity TranslateActivity(Activity idealActivity, out Activity idealWeaponActivityOut) {
		const int MAX_TRIES = 5;
		int count = 0;

		bool idealWeaponRequired = false;
		Activity idealWeaponActivity;
		Activity baseTranslation;
		bool weaponRequired = false;
		Activity weaponTranslation;
		Activity last;
		Activity current;

		idealWeaponActivity = Weapon_TranslateActivity(idealActivity, ref idealWeaponRequired);
		idealWeaponActivityOut = idealWeaponActivity;

		baseTranslation = idealActivity;
		weaponTranslation = idealActivity;
		last = idealActivity;
		while (count++ < MAX_TRIES) {
			current = NPC_TranslateActivity(last);
			if (current != last)
				baseTranslation = current;

			weaponTranslation = Weapon_TranslateActivity(current, ref weaponRequired);

			if (weaponTranslation == last)
				break;

			last = weaponTranslation;
		}
		AssertMsg(count < MAX_TRIES, "Circular activity translation!");

		if (last == Activity.ACT_SCRIPT_CUSTOM_MOVE)
			return Activity.ACT_SCRIPT_CUSTOM_MOVE;

		if (HaveSequenceForActivity(weaponTranslation))
			return weaponTranslation;

		if (weaponRequired) {
			if (!sUniqueActivities.Contains(weaponTranslation)) {
				DevWarning($"{GetClassname()} missing activity \"{GetActivityName(weaponTranslation)}\" needed by weapon\"{GetActiveWeapon()!.GetClassname()}\"\n");

				sUniqueActivities.Add(weaponTranslation);
			}
		}

		if (baseTranslation != weaponTranslation && HaveSequenceForActivity(baseTranslation))
			return baseTranslation;

		if (idealWeaponActivity != baseTranslation && HaveSequenceForActivity(idealWeaponActivity))
			return idealActivity;

		if (idealActivity != idealWeaponActivity && HaveSequenceForActivity(idealActivity))
			return idealActivity;

		Assert(!HaveSequenceForActivity(idealActivity));
		if (idealActivity == Activity.ACT_RUN)
			idealActivity = Activity.ACT_WALK;
		else if (idealActivity == Activity.ACT_WALK)
			idealActivity = Activity.ACT_RUN;

		return idealActivity;
	}

	public virtual int GetScriptCustomMoveSequence() => throw new NotImplementedException();

	static AI_BaseNPC? ResolveLastWarn;
	static Activity ResolveLastWarnActivity;
	static TimeUnit_t ResolveTimeLastWarn;

	public void ResolveActivityToSequence(Activity newActivity, ref int sequence, ref Activity translatedActivity, ref Activity weaponActivity) {
		sequence = StudioHdr.ACTIVITY_NOT_AVAILABLE;

		translatedActivity = TranslateActivity(newActivity, out weaponActivity);

		if (newActivity == Activity.ACT_SCRIPT_CUSTOM_MOVE)
			sequence = GetScriptCustomMoveSequence();
		else {
			sequence = SelectWeightedSequence(translatedActivity);

			if (sequence == StudioHdr.ACTIVITY_NOT_AVAILABLE && translatedActivity == Activity.ACT_WALK)
				sequence = SelectWeightedSequence(Activity.ACT_WALK_RIFLE);

			if (sequence == StudioHdr.ACTIVITY_NOT_AVAILABLE) {
				if ((ResolveLastWarn != this && ResolveLastWarnActivity != translatedActivity) || gpGlobals.CurTime - ResolveTimeLastWarn > 5.0) {
					DevWarning($"{GetClassname()}:{GetDebugName()}:{GetModelName()} has no sequence for act:{ActivityList.NameForIndex(translatedActivity)}\n");
					ResolveLastWarn = this;
					ResolveLastWarnActivity = translatedActivity;
					ResolveTimeLastWarn = gpGlobals.CurTime;
				}

				if (translatedActivity == Activity.ACT_RUN) {
					translatedActivity = Activity.ACT_WALK;
					sequence = SelectWeightedSequence(translatedActivity);
				}
			}
		}

		if (sequence == (int)Activity.ACT_INVALID)
			sequence = 0;
	}

	public void SetActivityAndSequence(Activity newActivity, int sequence, Activity translatedActivity, Activity weaponActivity) {
		TranslatedActivity = translatedActivity;

		if (ai_sequence_debug.GetBool() == true && (DebugOverlays & DebugOverlayBits.NPCSelected) != 0) {
			DevMsg($"SetActivityAndSequence : {GetClassname()}: {GetActivityName(GetActivity())}:{Animation.GetSequenceName(GetModelPtr(), GetSequence())} -> {GetActivityName(newActivity)}:{Animation.GetSequenceName(GetModelPtr(), sequence)} / {GetActivityName(translatedActivity)}:{GetActivityName(weaponActivity)}\n");
		}

		if (sequence > StudioHdr.ACTIVITY_NOT_AVAILABLE) {
			if (GetSequence() != sequence || !SequenceLoops) {
				if (!IsActivityMovementPhased(Activity) ||
					!IsActivityMovementPhased(newActivity)) {
					SetCycle(0);
				}
			}

			ResetSequence(sequence);
			Weapon_SetActivity(weaponActivity, (float)SequenceDuration(sequence));
		}
		else
			ResetSequence(0);

		SetViewOffset(EyeOffset(TranslatedActivity));

		if (Activity != newActivity)
			OnChangeActivity(newActivity);

		Activity = newActivity;

		GetMotor()!.RecalculateYawSpeed();
	}

	public virtual bool CreateComponents() {
		Senses = CreateSenses();
		if (Senses == null)
			return false;

		Motor = CreateMotor();
		if (Motor == null)
			return false;

		LocalNavigator = CreateLocalNavigator();
		if (LocalNavigator == null)
			return false;

		MoveProbe = CreateMoveProbe();
		if (MoveProbe == null)
			return false;

		Navigator = CreateNavigator();
		if (Navigator == null)
			return false;

		Pathfinder = CreatePathfinder();
		if (Pathfinder == null)
			return false;

		TacticalServices = CreateTacticalServices();
		if (TacticalServices == null)
			return false;

		MoveAndShootOverlay.SetOuter(this);

		Motor.Init(LocalNavigator);
		LocalNavigator.Init(Navigator);
		Navigator.Init(g_pBigAINet);
		Pathfinder.Init(g_pBigAINet);
		TacticalServices.Init(g_pBigAINet);

		return true;
	}

	public virtual AI_Senses? CreateSenses() {
		AI_Senses senses = new AI_Senses();
		senses.SetOuter(this);
		return senses;
	}

	public virtual AI_Motor? CreateMotor() => new AI_Motor(this);
	public virtual AI_MoveProbe? CreateMoveProbe() => new AI_MoveProbe(this);
	public virtual AI_LocalNavigator? CreateLocalNavigator() => new AI_LocalNavigator(this);
	public virtual AI_TacticalServices? CreateTacticalServices() => new AI_TacticalServices(this);
	public virtual AI_Navigator? CreateNavigator() => new AI_Navigator(this);
	public virtual AI_Pathfinder? CreatePathfinder() => new AI_Pathfinder(this);

	public AI_Senses? GetSenses() => Senses;
	public AI_Navigator? GetNavigator() => Navigator;
	public AI_LocalNavigator? GetLocalNavigator() => LocalNavigator;
	public AI_Pathfinder? GetPathfinder() => Pathfinder;
	public AI_MoveProbe? GetMoveProbe() => MoveProbe;
	public AI_Motor? GetMotor() => Motor;
	public AI_TacticalServices? GetTacticalServices() => TacticalServices;

	public void SetDistLook(float distLook) => Senses!.SetDistLook(distLook);

	public virtual bool IsWaitingToRappel() => false;

	public virtual void ClearCommandGoal() {
		CommandGoal = vec3_invalid;
		CommandMoveMonitor.ClearMark();
	}

	public void ClearSchedule(string? reason) {
		if (reason != null && (DebugOverlays & DebugOverlayBits.TaskText) != 0)
			DevMsg($"  Schedule cleared: {reason}\n");

		ScheduleState.TimeCurTaskStarted = ScheduleState.TimeStarted = 0;
		ScheduleState.ScheduleWasInterrupted = true;
		SetTaskStatus(TaskStatus.New);
		IdealSchedule = SCHED_NONE;
		Schedule = null;
		ResetScheduleCurTaskIndex();
		InverseIgnoreConditions.SetAll();
	}

	public virtual bool SetSchedule(int localScheduleID) {
		AI_Schedule? newSchedule = GetScheduleOfType(localScheduleID);
		if (newSchedule != null) {
			if (Cine.Get() != null) {
				if (!(localScheduleID == SCHED_SLEEP || localScheduleID == SCHED_WAIT_FOR_SCRIPT || localScheduleID == SCHED_SCRIPTED_WALK || localScheduleID == SCHED_SCRIPTED_RUN || localScheduleID == SCHED_SCRIPTED_CUSTOM_MOVE || localScheduleID == SCHED_SCRIPTED_WAIT || localScheduleID == SCHED_SCRIPTED_FACE))
					Assert(false);
			}

			IdealSchedule = GetGlobalScheduleId(localScheduleID);
			SetSchedule(newSchedule);
			return true;
		}
		return false;
	}

	public void SetSchedule(AI_Schedule newSchedule) {
		Assert(newSchedule != null);

		ScheduleState.TimeCurTaskStarted = ScheduleState.TimeStarted = gpGlobals.CurTime;
		ScheduleState.ScheduleWasInterrupted = false;

		Schedule = newSchedule;
		ResetScheduleCurTaskIndex();
		SetTaskStatus(TaskStatus.New);
		FailSchedule = SCHED_NONE;
		bool condInPVS = HasCondition((int)SCOND_t.COND_IN_PVS);
		Conditions.ClearAll();
		if (condInPVS)
			SetCondition((int)SCOND_t.COND_IN_PVS);
		ConditionsGatheredValue = false;
		InverseIgnoreConditions.SetAll();
		Forget(AI_MemoryFlags.Turning);

		if ((DebugOverlays & DebugOverlayBits.TaskText) != 0)
			DevMsg($"Schedule: {newSchedule.GetName()} (time: {gpGlobals.CurTime:F2})\n");
	}

	public void SetHintNode(AI_Hint? hintNode) => HintNode.Set(hintNode);

	public BaseEntity? GetEnemy() => Enemy.Get();

	public void SetEnemy(BaseEntity? enemy, bool setCondNewEnemy = true) {
		if (Enemy.Get() != enemy) {
			ClearAttackConditions();
			VacateStrategySlot();
			GiveUpOnDeadEnemyTimer.Stop();

			if (enemy != null && setCondNewEnemy)
				SetCondition((int)SCOND_t.COND_NEW_ENEMY);
		}

		Enemy.Set(enemy);
		TimeEnemyAcquired = gpGlobals.CurTime;

		LastShootAccuracy = -1;
		TotalShots = 0;
		TotalHits = 0;

		if (enemy == null)
			ClearCondition((int)SCOND_t.COND_NEW_ENEMY);
	}

	public BaseEntity? GetGoalEnt() => GoalEnt.Get();

	public void SetGoalEnt(BaseEntity? goalEnt) => GoalEnt.Set(goalEnt);

	public void SetDefaultEyeOffset() {
		if (GetModelPtr() != null) {
			Animation.GetEyePosition(GetModelPtr(), ref DefaultEyeOffset);

			if (DefaultEyeOffset == vec3_origin) {
				if (Classify() != Class_T.None)
					DevMsg($"WARNING: {GetClassname()}({GetModelName()}) has no eye offset in .qc!\n");
				DefaultEyeOffset = WorldAlignMins() + WorldAlignMaxs();
				DefaultEyeOffset *= 0.75f;
			}
		}
		else
			DefaultEyeOffset = vec3_origin;

		SetViewOffset(DefaultEyeOffset);
	}

	public virtual Capability CapabilitiesGet() {
		Capability capability = Capability;
		if (GetActiveWeapon() != null)
			capability |= GetActiveWeapon()!.CapabilitiesGet();
		return capability;
	}

	public void NPCUse(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		return;
	}

	public void ForceGatherConditions() {
		ForceConditionsGather = true;
		SetEfficiency(AI_Efficiency.Normal);
	}

	public void SetEfficiency(AI_Efficiency efficiency) => Efficiency = efficiency;

	public AI_SleepState GetSleepState() => SleepState;
	public void SetSleepState(AI_SleepState sleepState) => SleepState = sleepState;
	public void AddSleepFlags(AI_SleepFlags flags) => SleepFlags |= flags;

	public void Sleep() => throw new NotImplementedException();

	public virtual bool ShouldFadeOnDeath() {
#if GMOD_DLL
		return true;
#else
		throw new NotImplementedException();
#endif
	}

	public void SetDeathPose(int deathPose) => DeathPose = deathPose;
	public void SetDeathPoseFrame(int deathPoseFrame) => DeathFrame = deathPoseFrame;

	public void SetupVPhysicsHull() {
		if (GetMoveType() == Source.MoveType.VPhysics || GetMoveType() == Source.MoveType.None)
			return;

		if (VPhysicsGetObject() != null) {
			VPhysicsGetObject()!.EnableCollisions(false);
			VPhysicsDestroyObject();
		}
		VPhysicsInitShadow(true, false);
		IPhysicsObject? physObj = VPhysicsGetObject();
		if (physObj != null) {
			float mass = BoneSetup.Studio_GetMass(GetModelPtr());
			if (mass > 0)
				physObj.SetMass(mass);
#if DEBUG
			else
				DevMsg($"Warning: {GetModelName()} has no physical mass\n");
#endif
			IPhysicsShadowController controller = physObj.GetShadowController();
			float avgsize = (WorldAlignSize().X + WorldAlignSize().Y) * 0.5f;
			controller.SetTeleportDistance(avgsize * 0.5f);
			CheckContacts = true;
		}
	}

	public virtual bool InitSquad() {
		if (Squad == null && (CapabilitiesGet() & Server.Capability.Squad) != 0) {
			if (SquadName == null)
				DevMsg(2, $"Found {base.GetClassname()} that isn't in a squad\n");
			else
				throw new NotImplementedException();
		}

		return Squad != null;
	}

	public bool CanThinkRebalance() {
		if (FnThink != CallNPCThinkPtr)
			return false;

		if (InChoreo)
			return false;

		if (NPCState == NPCState.Dead)
			return false;

		if (GetSleepState() != AI_SleepState.Awake)
			return false;

		if (!UsingStandardThinkTime)
			return false;

		return true;
	}

	static int ThinkRebalanceCompare(AIRebalanceInfo left, AIRebalanceInfo right) {
		int baseCompare = left.NextThinkTick - right.NextThinkTick;
		if (baseCompare != 0)
			return baseCompare;

		if (!left.InPVS && !right.InPVS)
			return 0;

		if (!left.InPVS)
			return 1;

		if (!right.InPVS)
			return -1;

		if (left.DotPlayer < 0 && right.DotPlayer < 0)
			return 0;

		if (left.DotPlayer < 0)
			return 1;

		if (right.DotPlayer < 0)
			return -1;

		const float NEAR_PLAYER = 50 * 12;

		if (left.DistPlayer < NEAR_PLAYER && right.DistPlayer >= NEAR_PLAYER)
			return -1;

		if (right.DistPlayer < NEAR_PLAYER && left.DistPlayer >= NEAR_PLAYER)
			return 1;

		if (left.DotPlayer > right.DotPlayer)
			return -1;

		if (left.DotPlayer < right.DotPlayer)
			return 1;

		return 0;
	}

	static long RebalancePrevTick;
	static int RebalanceThinksInTick;
	static int RebalanceRebalanceableThinksInTick;
	static readonly List<AIRebalanceInfo> rebalanceCandidates = new(16);

	public void RebalanceThinks() {
		bool debugThinkTicks = ai_debug_think_ticks.GetBool();
		if (debugThinkTicks) {
			if (gpGlobals.TickCount != RebalancePrevTick) {
				DevMsg($"NPC per tick is {RebalanceRebalanceableThinksInTick} [{RebalanceThinksInTick}] (tick {RebalancePrevTick}, frame {gpGlobals.FrameCount})\n");
				RebalancePrevTick = gpGlobals.TickCount;
				RebalanceThinksInTick = 0;
				RebalanceRebalanceableThinksInTick = 0;
			}
			RebalanceThinksInTick++;
			if (CanThinkRebalance())
				RebalanceRebalanceableThinksInTick++;
		}

		if (ShouldRebalanceThinks() && gpGlobals.TickCount >= NextThinkRebalanceTick) {
			NextThinkRebalanceTick = (int)gpGlobals.TickCount + TIME_TO_TICKS(RandomFloat(3, 5));

			int i;

			BasePlayer? player = AI_GetClosestPlayer();
			Vector3 playerForward = default;
			Vector3 playerEyePosition = default;

			if (player != null)
				player.EyePositionAndVectors(out playerEyePosition, out playerForward, out _, out _);

			int ticksPer10Hz = TIME_TO_TICKS(.1);
			long minTickRebalance = gpGlobals.TickCount - 1;
			long maxTickRebalance = gpGlobals.TickCount + ticksPer10Hz;

			for (i = 0; i < g_AI_Manager.NumAIs(); i++) {
				AI_BaseNPC candidate = g_AI_Manager.AccessAIs()[i];
				if (candidate.CanThinkRebalance() &&
					(candidate.GetNextThinkTick() >= minTickRebalance &&
					candidate.GetNextThinkTick() < maxTickRebalance)) {
					AIRebalanceInfo info = default;

					info.NPC = candidate;
					info.NextThinkTick = (int)candidate.GetNextThinkTick();

					if (candidate.IsFlaggedEfficient())
						info.InPVS = false;
					else if (player != null) {
						Vector3 toCandidate = candidate.EyePosition() - playerEyePosition;
						info.InPVS = Util.FindClientInPVS(candidate.Edict()) != null;
						info.DistPlayer = MathLib.VectorNormalize(ref toCandidate);
						info.DotPlayer = Vector3.Dot(playerForward, toCandidate);
					}
					else {
						info.InPVS = true;
						info.DotPlayer = 1;
						info.DistPlayer = 0;
					}

					rebalanceCandidates.Add(info);
				}
				else if (debugThinkTicks)
					DevMsg($"   Ignoring {candidate.GetNextThinkTick()}\n");
			}

			if (rebalanceCandidates.Count != 0) {
				rebalanceCandidates.Sort(ThinkRebalanceCompare);

				int maxThinkersPerTick = (int)MathF.Ceiling((float)(rebalanceCandidates.Count + 1) / (float)ticksPer10Hz);

				long curTickDistributing = Math.Min(gpGlobals.TickCount, rebalanceCandidates[0].NextThinkTick);
				int remainingThinksToDistribute = maxThinkersPerTick - 1;

				if (debugThinkTicks) {
					DevMsg($"Rebalance {rebalanceCandidates.Count + 1}!\n");
					DevMsg($"   Distributing {curTickDistributing}\n");
				}

				for (i = 0; i < rebalanceCandidates.Count; i++) {
					if (remainingThinksToDistribute == 0 || rebalanceCandidates[i].NextThinkTick > curTickDistributing) {
						if (rebalanceCandidates[i].NextThinkTick <= curTickDistributing)
							curTickDistributing = curTickDistributing + 1;
						else
							curTickDistributing = rebalanceCandidates[i].NextThinkTick;

						if (debugThinkTicks)
							DevMsg($"   Distributing {curTickDistributing}\n");

						remainingThinksToDistribute = maxThinkersPerTick;
					}

					if (rebalanceCandidates[i].NPC.GetNextThinkTick() != curTickDistributing) {
						if (debugThinkTicks)
							DevMsg($"      Bumping {rebalanceCandidates[i].NPC.GetNextThinkTick()} to {curTickDistributing}\n");

						rebalanceCandidates[i].NPC.SetNextThink(TICKS_TO_TIME((int)curTickDistributing));
					}
					else if (debugThinkTicks)
						DevMsg($"      Leaving {rebalanceCandidates[i].NPC.GetNextThinkTick()}\n");

					remainingThinksToDistribute--;
				}
			}

			rebalanceCandidates.Clear();

			if (debugThinkTicks) {
				DevMsg("New distribution is:\n");
				for (i = 0; i < g_AI_Manager.NumAIs(); i++)
					DevMsg($"   {g_AI_Manager.AccessAIs()[i].GetNextThinkTick()}\n");
			}

			Assert(GetNextThinkTick() == TICK_NEVER_THINK);
		}
	}

	static long PreNPCThinkPrevFrame = -1;
	static float PreNPCThinkFrameTimeLimit = float.MaxValue;
	static ConVar? PreNPCThinkHostTimescale;

	public bool PreNPCThink() {
		if (PreNPCThinkFrameTimeLimit == float.MaxValue)
			PreNPCThinkHostTimescale = cvar.FindVar("host_timescale");

		bool useThinkLimits = !InChoreo && ShouldUseFrameThinkLimits();

#if DEBUG
		const float NPC_THINK_LIMIT = 30.0f / 1000.0f;
#else
		const float NPC_THINK_LIMIT = 10.0f / 1000.0f;
#endif

		g_StartTimeCurThink = 0;

		if (useThinkLimits) {
			if (FrameBlocked == gpGlobals.FrameCount) {
				SetNextThink(gpGlobals.CurTime);
				return false;
			}
			else if (gpGlobals.FrameCount != PreNPCThinkPrevFrame) {
				float timescale = PreNPCThinkHostTimescale!.GetFloat();
				if (timescale < 1)
					timescale = 1;

				PreNPCThinkPrevFrame = gpGlobals.FrameCount;
				PreNPCThinkFrameTimeLimit = NPC_THINK_LIMIT * timescale;
				g_NpcTimeThisFrame = 0;
			}
			else {
				if (g_NpcTimeThisFrame > NPC_THINK_LIMIT) {
					TimeUnit_t timeSinceLastRealThink = gpGlobals.CurTime - LastRealThinkTime;
					if (timeSinceLastRealThink <= .25) {
						FrameBlocked = gpGlobals.FrameCount;
						SetNextThink(gpGlobals.CurTime);
						return false;
					}
				}
			}

			g_StartTimeCurThink = engine.Time();

			FrameBlocked = -1;
			LastThinkTick = TIME_TO_TICKS(LastRealThinkTime);
		}

		return true;
	}

	public void PostNPCThink() {
		if (g_StartTimeCurThink != 0.0)
			g_NpcTimeThisFrame += (float)(engine.Time() - g_StartTimeCurThink);
	}

	public void CallNPCThink() {
		RebalanceThinks();

		UsingStandardThinkTime = false;

		if (!PreNPCThink())
			return;

		NPCThink();

		LastRealThinkTime = gpGlobals.CurTime;

		PostNPCThink();
	}

	public bool CheckPVSCondition() {
		bool inPVS = (Util.FindClientInPVS(Edict()) != null) || (Util.ClientPVSIsExpanded() && Util.FindClientInVisibilityPVS(Edict()) != null);

		if (inPVS)
			SetCondition((int)SCOND_t.COND_IN_PVS);
		else
			ClearCondition((int)SCOND_t.COND_IN_PVS);

		return inPVS;
	}

	public void CheckPhysicsContacts() {
		if (gpGlobals.FrameTime <= 0.0f || !ai_auto_contact_solver.GetBool())
			return;

		CheckContacts = false;
		if (GetMoveType() == Source.MoveType.Step && VPhysicsGetObject() != null) {
			IPhysicsObject physics = VPhysicsGetObject()!;
			IPhysicsFrictionSnapshot snapshot = physics.CreateFrictionSnapshot();
			BaseEntity? groundEntity = GetGroundEntity();
			float heightCheck = GetAbsOrigin().Z + GetHullMaxs().Z;
			physics.GetVelocity(out Vector3 npcVel, out _);
			BaseEntity? otherEntity = null;
			bool createSolver = false;
			float solverTime = 0.0f;
			while (snapshot.IsValid()) {
				IPhysicsObject other = snapshot.GetObject(1)!;
				otherEntity = (BaseEntity?)other.GetGameData();

				if (otherEntity != null && groundEntity != otherEntity) {
					float otherMass = PhysGetEntityMass(otherEntity);

					if (otherEntity.GetMoveType() == Source.MoveType.VPhysics && other.IsMoveable() &&
						otherMass < VPHYSICS_LARGE_OBJECT_MASS && otherEntity.GetServerVehicle() == null) {
						CheckContacts = true;
						other.GetVelocity(out Vector3 vel, out _);
						snapshot.GetContactPoint(out Vector3 point);

						vel -= npcVel;

						if (vel.LengthSquared() < 5.0f * 5.0f) {
							float topdist = MathF.Abs(point.Z - heightCheck);
							solverTime = 4.0f;
							if (topdist < 2.0f) {
								solverTime = 0.5f;
								if ((other.GetGameFlags() & PhysicsFlags.PlayerHeld) != 0)
									solverTime = 0.25f;

								createSolver = true;
								break;
							}
						}
					}
				}
				snapshot.NextFrictionData();
			}
			physics.DestroyFrictionSnapshot(snapshot);
			if (createSolver) {
				NPCPhysics_CreateSolver(this, otherEntity, true, solverTime);
				physics.RecheckContactPoints();
			}
		}
	}

	public void Wake(bool fireOutput = true) => throw new NotImplementedException();

	public void UpdateSleepState(bool inPVS) {
		if (GetSleepState() > AI_SleepState.Awake) {
			BasePlayer? localPlayer = AI_GetClosestPlayer();
			if (localPlayer == null) {
				Wake();
				return;
			}

			if (WakeRadius > .1 && (localPlayer.GetFlags() & EntityFlags.NoTarget) == 0 && (localPlayer.GetAbsOrigin() - GetAbsOrigin()).LengthSquared() <= WakeRadius * WakeRadius)
				Wake();
			else if (GetSleepState() == AI_SleepState.WaitingForPVS) {
				if (inPVS)
					Wake();
			}
			else if (GetSleepState() == AI_SleepState.WaitingForThreat) {
				if (HasCondition((int)SCOND_t.COND_LIGHT_DAMAGE) || HasCondition((int)SCOND_t.COND_HEAVY_DAMAGE))
					Wake();
				else {
					if (inPVS) {
						for (int i = 1; i <= gpGlobals.MaxClients; i++) {
							BasePlayer? player = Util.PlayerByIndex(i);
							if (player != null && (player.GetFlags() & EntityFlags.NoTarget) == 0 && player.FVisible(this))
								Wake();
						}
					}

					if ((GetSoundInterests() & (int)SoundInstanceType.Danger) != 0 && !HasSpawnFlags(SF_NPC_WAIT_TILL_SEEN)) {
						int sound = SoundEnt.ActiveList();

						while (sound != SOUNDLIST_EMPTY) {
							ref WorldSoundInstance currentSound = ref SoundEnt.SoundPointerForIndex(sound);
							Assert(!Unsafe.IsNullRef(ref currentSound));

							if ((currentSound.SoundType() & SoundInstanceType.Danger) != 0 &&
								 GetSenses()!.CanHearSound(ref currentSound) &&
								 SoundIsVisible(ref currentSound)) {
								Wake();
								break;
							}

							sound = currentSound.NextSound();
						}
					}
				}
			}
		}
		else {
			if (!IsInAScript() && NPCState != NPCState.Script) {
				if (HasSleepFlags(AI_SleepFlags.AutoPVS)) {
					if (!HasCondition((int)SCOND_t.COND_IN_PVS)) {
						SetSleepState(AI_SleepState.WaitingForPVS);
						Sleep();
					}
				}
				if (HasSleepFlags(AI_SleepFlags.AutoPVSAfterPVS)) {
					if (HasCondition((int)SCOND_t.COND_IN_PVS)) {
						AddSleepFlags(AI_SleepFlags.AutoPVS);
						RemoveSleepFlags(AI_SleepFlags.AutoPVSAfterPVS);
					}
				}
			}
		}
	}

	public bool SoundIsVisible(ref WorldSoundInstance sound) => throw new NotImplementedException();

	static Vector3 UpdateEfficiencyPlayerEyePosition;
	static Vector3 UpdateEfficiencyPlayerForward;
	static long UpdateEfficiencyPrevFrame = -1;

	static readonly AI_Efficiency[] EfficiencyMappings = [
		AI_Efficiency.Normal,
		AI_Efficiency.Efficient,
		AI_Efficiency.Efficient,
		AI_Efficiency.Efficient,
		AI_Efficiency.Efficient,
		AI_Efficiency.VeryEfficient,
		AI_Efficiency.VeryEfficient,
		AI_Efficiency.SuperEfficient,
		AI_Efficiency.SuperEfficient,

		AI_Efficiency.Normal,
		AI_Efficiency.Efficient,
		AI_Efficiency.Efficient,
		AI_Efficiency.Normal,
		AI_Efficiency.Efficient,
		AI_Efficiency.Efficient,
		AI_Efficiency.Efficient,
		AI_Efficiency.VeryEfficient,
		AI_Efficiency.SuperEfficient,

		AI_Efficiency.Normal,
		AI_Efficiency.Normal,
		AI_Efficiency.Efficient,
		AI_Efficiency.Normal,
		AI_Efficiency.Efficient,
		AI_Efficiency.Efficient,
		AI_Efficiency.Normal,
		AI_Efficiency.Efficient,
		AI_Efficiency.VeryEfficient,
	];

	static readonly int[] EfficiencyStateBase = [0, 9, 18];

	public void UpdateEfficiency(bool inPVS) {
		if (GetSleepState() != AI_SleepState.Awake) {
			SetEfficiency(AI_Efficiency.Dormant);
			return;
		}

		InChoreo = GetState() == NPCState.Script || IsCurSchedule(SCHED_SCENE_GENERIC, false);

		if (!ShouldUseEfficiency()) {
			SetEfficiency(AI_Efficiency.Normal);
			SetMoveEfficiency(AI_MoveEfficiency.Normal);
			return;
		}

		BasePlayer? player = AI_GetClosestPlayer();
		if (gpGlobals.FrameCount != UpdateEfficiencyPrevFrame) {
			UpdateEfficiencyPrevFrame = gpGlobals.FrameCount;
			if (player != null)
				player.EyePositionAndVectors(out UpdateEfficiencyPlayerEyePosition, out UpdateEfficiencyPlayerForward, out _, out _);
		}

		Vector3 toNPC = GetAbsOrigin() - UpdateEfficiencyPlayerEyePosition;
		float playerDist = MathLib.VectorNormalize(ref toNPC);
		bool playerFacing;

		bool clientPVSExpanded = Util.ClientPVSIsExpanded();

		if (player != null)
			playerFacing = clientPVSExpanded || (inPVS && Vector3.Dot(UpdateEfficiencyPlayerForward, toNPC) > 0);
		else {
			playerDist = 0;
			playerFacing = true;
		}

		bool inVisibilityPVS = clientPVSExpanded && Util.FindClientInVisibilityPVS(Edict()) != null;

		if ((inPVS && (playerFacing || playerDist < 25 * 12)) || clientPVSExpanded)
			SetMoveEfficiency(AI_MoveEfficiency.Normal);
		else
			SetMoveEfficiency(AI_MoveEfficiency.Efficient);

		if (ai_efficiency_override.GetInt() > (int)AI_Efficiency.Normal && ai_efficiency_override.GetInt() <= (int)AI_Efficiency.Dormant) {
			SetEfficiency((AI_Efficiency)ai_efficiency_override.GetInt());
			return;
		}

		if (gpGlobals.CurTime - GetLastAttackTime() < .15) {
			SetEfficiency(AI_Efficiency.Normal);
			return;
		}

		bool framerateOk = gpGlobals.FrameTime < ai_frametime_limit.GetFloat();

		if (ForceConditionsGather ||
			 gpGlobals.CurTime - GetLastAttackTime() < .2 ||
			 gpGlobals.CurTime - LastDamageTime < .2 ||
			 (GetState() < NPCState.Idle || GetState() > NPCState.Script) ||
			 ((inPVS || inVisibilityPVS) &&
			   ((GetTask() != null && !TaskIsRunning()) ||
				 GetTaskInterrupt() > 0 ||
				 InChoreo))) {
			SetEfficiency(framerateOk ? AI_Efficiency.Normal : AI_Efficiency.Efficient);
			return;
		}

		AI_Efficiency minEfficiency;

		if (!ShouldDefaultEfficient())
			minEfficiency = framerateOk ? AI_Efficiency.Normal : AI_Efficiency.Efficient;
		else
			minEfficiency = framerateOk ? AI_Efficiency.Efficient : AI_Efficiency.VeryEfficient;

		bool potentialDanger = false;

		if ((GetSoundInterests() & (int)SoundInstanceType.Danger) != 0) {
			int sound = SoundEnt.ActiveList();

			while (sound != SOUNDLIST_EMPTY) {
				ref WorldSoundInstance currentSound = ref SoundEnt.SoundPointerForIndex(sound);

				float hearingSensitivity = HearingSensitivity();
				Vector3 earPosition = EarPosition();

				if (!Unsafe.IsNullRef(ref currentSound) && (SoundInstanceType.Danger & currentSound.SoundType()) != 0) {
					float hearDistanceSq = currentSound.Volume() * hearingSensitivity;
					hearDistanceSq *= hearDistanceSq;
					if (Vector3.DistanceSquared(currentSound.GetSoundOrigin(), earPosition) <= hearDistanceSq) {
						potentialDanger = true;
						break;
					}
				}

				sound = currentSound.NextSound();
			}
		}

		if (potentialDanger) {
			SetEfficiency(minEfficiency);
			return;
		}

		if (player == null) {
			SetEfficiency(minEfficiency);
			return;
		}

		const int DIST_NEAR = 0;
		const int DIST_MID = 1;
		const int DIST_FAR = 2;

		int range;
		if (inPVS) {
			if (playerDist < 15 * 12) {
				SetEfficiency(minEfficiency);
				return;
			}

			range = (playerDist < 50 * 12) ? DIST_NEAR :
					(playerDist < 200 * 12) ? DIST_MID : DIST_FAR;
		}
		else {
			range = (playerDist < 25 * 12) ? DIST_NEAR :
					(playerDist < 100 * 12) ? DIST_MID : DIST_FAR;
		}

		NPCState state = GetState();
		if (state == NPCState.Script)
			state = NPCState.Alert;

		const int NOT_FACING_OFFSET = 3;
		const int NO_PVS_OFFSET = 6;

		int stateOffset = EfficiencyStateBase[state - NPCState.Idle];
		int facingOffset = (!inPVS || playerFacing) ? 0 : NOT_FACING_OFFSET;
		int pvsOffset = inPVS ? 0 : NO_PVS_OFFSET;
		int mapping = stateOffset + pvsOffset + facingOffset + range;

		Assert(mapping < EfficiencyMappings.Length);

		AI_Efficiency efficiency = EfficiencyMappings[mapping];

		AI_Efficiency maxEfficiency = AI_Efficiency.SuperEfficient;
		if (inVisibilityPVS && state >= NPCState.Alert)
			maxEfficiency = AI_Efficiency.Efficient;
		else if (inVisibilityPVS || HasCondition((int)SCOND_t.COND_SEE_PLAYER))
			maxEfficiency = AI_Efficiency.VeryEfficient;

		SetEfficiency((AI_Efficiency)Math.Clamp((int)efficiency, (int)minEfficiency, (int)maxEfficiency));
	}

	public void GetPlayerAvoidBounds(out Vector3 mins, out Vector3 maxs) {
		mins = WorldAlignMins();
		maxs = WorldAlignMaxs();
	}

	public void SetPlayerAvoidState() {
		bool shouldPlayerAvoid = false;

		Animation.GetSequenceLinearMotion(GetModelPtr(), GetSequence(), GetPoseParameterArray(), out Vector3 nothing);
		bool isMoving = IsMoving() || nothing != vec3_origin;

		if (PerformAvoidance || (ShouldPlayerAvoid() && isMoving)) {
			GetPlayerAvoidBounds(out Vector3 mins, out Vector3 maxs);

			BasePlayer? localPlayer = AI_GetClosestPlayer();
			if (localPlayer != null) {
				shouldPlayerAvoid = CollisionUtils.IsBoxIntersectingBox(GetAbsOrigin() + mins, GetAbsOrigin() + maxs,
					localPlayer.GetAbsOrigin() + localPlayer.WorldAlignMins(), localPlayer.GetAbsOrigin() + localPlayer.WorldAlignMaxs());
			}

			if (ai_debug_avoidancebounds.GetBool()) {
				int red = shouldPlayerAvoid ? 255 : 0;

				DebugOverlay.Box(GetAbsOrigin(), mins, maxs, red, 0, 255, 64, 0.1f);
			}
		}

		PlayerAvoidState = ShouldPlayerAvoid();
		PerformAvoidance = shouldPlayerAvoid;

		if (GetCollisionGroup() == Source.CollisionGroup.NPC || GetCollisionGroup() == Source.CollisionGroup.NPCActor) {
			if (PerformAvoidance == true)
				SetCollisionGroup(Source.CollisionGroup.NPCActor);
			else
				SetCollisionGroup(Source.CollisionGroup.NPC);
		}
	}

	public bool PreThink() {
		if (g_DisableAI.GetBool()) {
			SetActivity(Activity.ACT_IDLE);
			return false;
		}

		if ((DebugBits & AI_DebugFlags.DisableAI) != 0 || !AI_NetworkManager.NetworksLoaded()) {
			SetActivity(Activity.ACT_IDLE);
			return false;
		}

		if ((DebugBits & AI_DebugFlags.StepAI) != 0) {
			if (DebugCurIndex >= DebugPauseIndex) {
				if (!GetNavigator()!.IsGoalActive())
					PlaybackRate = 0;
				return false;
			}
			else
				PlaybackRate = 1;
		}

		if (OpeningDoor.Get() != null && AIIsDebuggingDoors(this))
			DebugOverlay.Line(EyePosition(), OpeningDoor.Get()!.WorldSpaceCenter(), 255, 255, 255, false, .1f);

		return true;
	}

	public virtual void RunAI() {
		g_AIRunTimer.Restart();

#if DEBUG
		Selected = (DebugOverlays & DebugOverlayBits.NPCSelected) != 0;
#endif

		ConditionsGatheredValue = false;

		GatherConditions();
		RemoveIgnoredConditions();

		if (!ConditionsGatheredValue)
			ConditionsGatheredValue = true;

		TryRestoreHull();

		PrescheduleThink();

		MaintainSchedule();

		PostscheduleThink();

		ClearTransientConditions();

		g_AIRunTimer.Stop();
	}

	public virtual bool AutoMovement(BaseEntity? target = null) => throw new NotImplementedException();

	public virtual void PostRun() {
		if (!IsMoving()) {
			if (GetIdealActivity() == Activity.ACT_WALK ||
				 GetIdealActivity() == Activity.ACT_RUN ||
				 GetIdealActivity() == Activity.ACT_WALK_AIM ||
				 GetIdealActivity() == Activity.ACT_RUN_AIM) {
				PostRunStopMoving();
			}
		}

		RunAnimation();

		Weapon_FrameUpdate();
	}

	public virtual void PostRunStopMoving() {
		SetIdealActivity(GetStoppedActivity());
	}

	public virtual void RunAnimation() {
		if (GetModelPtr() == null)
			return;

		TimeUnit_t interval = GetAnimTimeInterval();

		StudioFrameAdvance();

		if ((DebugBits & AI_DebugFlags.StepAI) != 0)
			interval = 0;

		if (NPCState != NPCState.Script && NPCState != NPCState.Dead && Activity == Activity.ACT_IDLE && IsActivityFinished()) {
			int sequence;

			if (SequenceLoops)
				sequence = SelectWeightedSequence(TranslatedActivity);
			else
				sequence = SelectHeaviestSequence(TranslatedActivity);

			if (sequence != StudioHdr.ACTIVITY_NOT_AVAILABLE) {
				ResetSequence(sequence);

				if (hl2_episodic.GetBool())
					IdealSequence = sequence;
			}
		}

		DispatchAnimEvents(this);
	}

	public virtual bool OverrideMove(float interval) => false;

	public virtual void PerformMovement() {
		if (!IsAlive())
			return;

		TimeLastMovement = gpGlobals.CurTime;
	}

	public virtual void PostMovement() {
		InvalidateBoneCache();

		if (GetModelPtr() != null && GetModelPtr()!.SequencesAvailable()) {
			TimeUnit_t interval = GetAnimTimeInterval();

			if ((CapabilitiesGet() & Server.Capability.AimGun) != 0)
				AimGun();
			else
				InteractionYaw = GetAbsAngles().Y;

			if ((CapabilitiesGet() & Server.Capability.AnimatedFace) != 0)
				MaintainLookTargets(interval);
		}

		MaintainTurnActivity();
	}

	public virtual void AimGun() {
		if (GetEnemy() != null) {
			Vector3 shootOrigin = Weapon_ShootPosition();
			Vector3 shootDir = GetShootEnemyDir(shootOrigin, false);

			SetAim(shootDir);
		}
		else
			RelaxAim();
	}

	static readonly float[] g_DecisionIntervals = [
		.1f,
		.2f,
		.4f,
		.6f,
	];

	static readonly string[] ppszEfficiencies = [
		"AIE_NORMAL",
		"AIE_EFFICIENT",
		"AIE_VERY_EFFICIENT",
		"AIE_SUPER_EFFICIENT",
		"AIE_DORMANT",
	];

	static readonly string[] ppszMoveEfficiencies = [
		"AIME_NORMAL",
		"AIME_EFFICIENT",
	];

	public virtual void NPCThink() {
		if (CheckContacts)
			CheckPhysicsContacts();

		Assert(!(NPCState == NPCState.Dead && LifeState == (int)Source.LifeState.Alive));

		SetNextThink(TICK_NEVER_THINK);

		bool inPVS = CheckPVSCondition();

		UpdateSleepState(inPVS);

		bool ranDecision = false;

		if (GetEfficiency() < AI_Efficiency.Dormant && GetSleepState() == AI_SleepState.Awake) {
			float thinkLimit = ai_show_think_tolerance.GetFloat();

			if (thinkLimit > 0)
				g_AIRunTimer.Restart();

			if (g_pAINetworkManager != null && g_pAINetworkManager.IsInitialized()) {
				SetPlayerAvoidState();

				if (PreThink()) {
					if (NextDecisionTime <= gpGlobals.CurTime) {
						ranDecision = true;
						ScheduleState.TaskRanAutomovement = false;
						ScheduleState.TaskUpdatedYaw = false;
						RunAI();
					}
					else {
						if (ScheduleState.TaskRanAutomovement)
							AutoMovement();
						if (ScheduleState.TaskUpdatedYaw)
							GetMotor()!.UpdateYaw();
					}

					PostRun();

					PerformMovement();

					IsMovingValue = IsMoving();

					PostMovement();

					SetSimulationTime(gpGlobals.CurTime);
				}
				else {
					PostRun();
					IsMovingValue = IsMoving();
					PostMovement();
					SetSimulationTime(gpGlobals.CurTime);
					TimeLastMovement = float.MaxValue;
				}
			}

			if (thinkLimit > 0) {
				g_AIRunTimer.Stop();

				float thinkTime = (float)g_AIRunTimer.Elapsed.TotalMilliseconds;

				if (thinkTime > thinkLimit) {
					int color = (int)MathLib.RemapVal(thinkTime, thinkLimit, thinkLimit * 3, 96.0f, 255.0f);
					if (color > 255)
						color = 255;
					else if (color < 96)
						color = 96;

					Vector3 vecPoint = EyePosition() + new Vector3(0, 0, 12);
					MathLib.AngleVectors(GetAbsAngles(), out _, out Vector3 right, out _);
					DebugOverlay.Line(vecPoint, vecPoint + new Vector3(0, 0, 64), color, 0, 0, false, 1.0f);
					DebugOverlay.Line(vecPoint, vecPoint + new Vector3(0, 0, 16) + right * 16, color, 0, 0, false, 1.0f);
					DebugOverlay.Line(vecPoint, vecPoint + new Vector3(0, 0, 16) - right * 16, color, 0, 0, false, 1.0f);
				}
			}
		}

		UsingStandardThinkTime = GetNextThinkTick() == TICK_NEVER_THINK;

		UpdateEfficiency(inPVS);

		if (UsingStandardThinkTime) {
			if (ai_debug_efficiency.GetBool())
				DevMsg($"Eff: {ppszEfficiencies[(int)GetEfficiency()]}, Move: {ppszMoveEfficiencies[(int)GetMoveEfficiency()]}\n");

			if (ranDecision)
				NextDecisionTime = gpGlobals.CurTime + g_DecisionIntervals[(int)GetEfficiency()];

			if (GetMoveEfficiency() == AI_MoveEfficiency.Normal || GetEfficiency() == AI_Efficiency.Normal)
				SetNextThink(gpGlobals.CurTime + .1);
			else
				SetNextThink(gpGlobals.CurTime + .2);
		}
		else
			NextDecisionTime = 0;
	}
}
