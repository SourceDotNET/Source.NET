using Game.Shared;
using Game.Shared.GarrysMod;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

using static Game.Server.SCOND_t;

namespace Game.Server;

using DEFINE = DEFINE<NPC_MetroPolice>;

[LinkEntityToClass("npc_metropolice")]
public class NPC_MetroPolice : AI_BaseActor
{
	public const int SF_METROPOLICE_SIMPLE_VERSION = 0x00020000;
	public const int SF_METROPOLICE_ALWAYS_STITCH = 0x00080000;
	public const int SF_METROPOLICE_NOCHATTER = 0x00100000;
	public const int SF_METROPOLICE_ARREST_ENEMY = 0x00200000;
	public const int SF_METROPOLICE_NO_FAR_STITCH = 0x00400000;
	public const int SF_METROPOLICE_NO_MANHACK_DEPLOY = 0x00800000;
	public const int SF_METROPOLICE_ALLOWED_TO_RESPOND = 0x01000000;
	public const int SF_METROPOLICE_MID_RANGE_ATTACK = 0x02000000;

	public const float METROPOLICE_MID_RANGE_ATTACK_RANGE = 3500.0f;

	const float METROPOLICE_SQUAD_STITCH_MIN_INTERVAL = 1.0f;
	const float METROPOLICE_SQUAD_STITCH_MAX_INTERVAL = 1.2f;

	const float RECENT_DAMAGE_INTERVAL = 3.0f;

	const int METROPOLICE_MAX_WARNINGS = 3;

	public const int METROPOLICE_BODYGROUP_MANHACK = 1;

	const int METROPOLICE_NUM_CLIPS = 5;
	const int METROPOLICE_BURST_RELOAD_COUNT = 20;

	const int METROPOLICE_SENTENCE_FREEZE = 0;
	const int METROPOLICE_SENTENCE_HES_OVER_HERE = 1;
	const int METROPOLICE_SENTENCE_HES_RUNNING = 2;
	const int METROPOLICE_SENTENCE_TAKE_HIM_DOWN = 3;
	const int METROPOLICE_SENTENCE_ARREST_IN_POSITION = 4;
	const int METROPOLICE_SENTENCE_DEPLOY_MANHACK = 5;
	const int METROPOLICE_SENTENCE_MOVE_INTO_POSITION = 6;
	const int METROPOLICE_SENTENCE_HEARD_SOMETHING = 7;

	const int METROPOLICE_CHATTER_WAIT_FOR_RESPONSE = 0;
	const int METROPOLICE_CHATTER_ASK_QUESTION = 1;
	const int METROPOLICE_CHATTER_RESPONSE = 2;
	const int METROPOLICE_CHATTER_RESPONSE_TYPE_COUNT = 2;

	const AI_MemoryFlags bits_MEMORY_PAIN_LIGHT_SOUND = AI_MemoryFlags.Custom1;
	const AI_MemoryFlags bits_MEMORY_PAIN_HEAVY_SOUND = AI_MemoryFlags.Custom2;
	const AI_MemoryFlags bits_MEMORY_PLAYER_HURT = AI_MemoryFlags.Custom3;
	const AI_MemoryFlags bits_MEMORY_PLAYER_HARASSED = AI_MemoryFlags.Custom4;

	public static int g_interactionMetrocopStartedStitch = 0;
	public static int g_interactionMetrocopIdleChatter = 0;
	public static int g_interactionMetrocopClearSentenceQueues = 0;

	public static readonly ConVar sk_metropolice_health = new("sk_metropolice_health", "0");
	public static readonly ConVar sk_metropolice_simple_health = new("sk_metropolice_simple_health", "26");

	public static readonly ConVar metropolice_chase_use_follow = new("metropolice_chase_use_follow", "0");
	public static readonly ConVar metropolice_move_and_melee = new("metropolice_move_and_melee", "1");
	public static readonly ConVar metropolice_charge = new("metropolice_charge", "1");

	public static AnimEventId AE_METROPOLICE_BATON_ON;
	public static AnimEventId AE_METROPOLICE_BATON_OFF;
	public static AnimEventId AE_METROPOLICE_SHOVE;
	public static AnimEventId AE_METROPOLICE_START_DEPLOY;
	public static AnimEventId AE_METROPOLICE_DRAW_PISTOL;
	public static AnimEventId AE_METROPOLICE_DEPLOY_MANHACK;

	const SquadSlot SQUAD_SLOT_POLICE_CHARGE_ENEMY = SquadSlot.LastShared;
	const SquadSlot SQUAD_SLOT_POLICE_HARASS = SquadSlot.LastShared + 1;
	const SquadSlot SQUAD_SLOT_POLICE_DEPLOY_MANHACK = SquadSlot.LastShared + 2;
	const SquadSlot SQUAD_SLOT_POLICE_ADVANCE = SquadSlot.LastShared + 3;
	const SquadSlot SQUAD_SLOT_POLICE_ATTACK_OCCLUDER1 = SquadSlot.LastShared + 4;
	const SquadSlot SQUAD_SLOT_POLICE_ATTACK_OCCLUDER2 = SquadSlot.LastShared + 5;
	const SquadSlot SQUAD_SLOT_POLICE_COVERING_FIRE1 = SquadSlot.LastShared + 6;
	const SquadSlot SQUAD_SLOT_POLICE_COVERING_FIRE2 = SquadSlot.LastShared + 7;
	const SquadSlot SQUAD_SLOT_POLICE_ARREST_ENEMY = SquadSlot.LastShared + 8;

	public static Activity ACT_METROPOLICE_DRAW_PISTOL;
	public static Activity ACT_METROPOLICE_DEPLOY_MANHACK;
	public static Activity ACT_METROPOLICE_FLINCH_BEHIND;

	public static Activity ACT_WALK_BATON;
	public static Activity ACT_IDLE_ANGRY_BATON;
	public static Activity ACT_PUSH_PLAYER;
	public static Activity ACT_MELEE_ATTACK_THRUST;
	public static Activity ACT_ACTIVATE_BATON;
	public static Activity ACT_DEACTIVATE_BATON;

	const int COND_METROPOLICE_ON_FIRE = NEXT_CONDITION;
	const int COND_METROPOLICE_ENEMY_RESISTING_ARREST = NEXT_CONDITION + 1;
	const int COND_METROPOLICE_PLAYER_TOO_CLOSE = NEXT_CONDITION + 2;
	const int COND_METROPOLICE_CHANGE_BATON_STATE = NEXT_CONDITION + 3;
	const int COND_METROPOLICE_PHYSOBJECT_ASSAULT = NEXT_CONDITION + 4;

	const int SCHED_METROPOLICE_WALK = NEXT_SCHEDULE;
	const int SCHED_METROPOLICE_WAKE_ANGRY = NEXT_SCHEDULE + 1;
	const int SCHED_METROPOLICE_HARASS = NEXT_SCHEDULE + 2;
	const int SCHED_METROPOLICE_CHASE_ENEMY = NEXT_SCHEDULE + 3;
	const int SCHED_METROPOLICE_ESTABLISH_LINE_OF_FIRE = NEXT_SCHEDULE + 4;
	const int SCHED_METROPOLICE_DRAW_PISTOL = NEXT_SCHEDULE + 5;
	const int SCHED_METROPOLICE_DEPLOY_MANHACK = NEXT_SCHEDULE + 6;
	const int SCHED_METROPOLICE_ADVANCE = NEXT_SCHEDULE + 7;
	const int SCHED_METROPOLICE_CHARGE = NEXT_SCHEDULE + 8;
	const int SCHED_METROPOLICE_BURNING_RUN = NEXT_SCHEDULE + 9;
	const int SCHED_METROPOLICE_BURNING_STAND = NEXT_SCHEDULE + 10;
	const int SCHED_METROPOLICE_SMG_NORMAL_ATTACK = NEXT_SCHEDULE + 11;
	const int SCHED_METROPOLICE_SMG_BURST_ATTACK = NEXT_SCHEDULE + 12;
	const int SCHED_METROPOLICE_AIM_STITCH_AT_AIRBOAT = NEXT_SCHEDULE + 13;
	const int SCHED_METROPOLICE_AIM_STITCH_IN_FRONT_OF_AIRBOAT = NEXT_SCHEDULE + 14;
	const int SCHED_METROPOLICE_AIM_STITCH_TIGHTLY = NEXT_SCHEDULE + 15;
	const int SCHED_METROPOLICE_AIM_STITCH_ALONG_SIDE_OF_AIRBOAT = NEXT_SCHEDULE + 16;
	const int SCHED_METROPOLICE_AIM_STITCH_BEHIND_AIRBOAT = NEXT_SCHEDULE + 17;
	const int SCHED_METROPOLICE_ESTABLISH_STITCH_LINE_OF_FIRE = NEXT_SCHEDULE + 18;
	const int SCHED_METROPOLICE_INVESTIGATE_SOUND = NEXT_SCHEDULE + 19;
	const int SCHED_METROPOLICE_WARN_AND_ARREST_ENEMY = NEXT_SCHEDULE + 20;
	const int SCHED_METROPOLICE_ARREST_ENEMY = NEXT_SCHEDULE + 21;
	const int SCHED_METROPOLICE_ENEMY_RESISTING_ARREST = NEXT_SCHEDULE + 22;
	const int SCHED_METROPOLICE_WARN_TARGET = NEXT_SCHEDULE + 23;
	const int SCHED_METROPOLICE_HARASS_TARGET = NEXT_SCHEDULE + 24;
	const int SCHED_METROPOLICE_SUPPRESS_TARGET = NEXT_SCHEDULE + 25;
	const int SCHED_METROPOLICE_RETURN_FROM_HARASS = NEXT_SCHEDULE + 26;
	const int SCHED_METROPOLICE_SHOVE = NEXT_SCHEDULE + 27;
	const int SCHED_METROPOLICE_ACTIVATE_BATON = NEXT_SCHEDULE + 28;
	const int SCHED_METROPOLICE_DEACTIVATE_BATON = NEXT_SCHEDULE + 29;
	const int SCHED_METROPOLICE_ALERT_FACE_BESTSOUND = NEXT_SCHEDULE + 30;
	const int SCHED_METROPOLICE_RETURN_TO_PRECHASE = NEXT_SCHEDULE + 31;
	const int SCHED_METROPOLICE_SMASH_PROP = NEXT_SCHEDULE + 32;

	const int TASK_METROPOLICE_HARASS = NEXT_TASK;
	const int TASK_METROPOLICE_DIE_INSTANTLY = NEXT_TASK + 1;
	const int TASK_METROPOLICE_BURST_ATTACK = NEXT_TASK + 2;
	const int TASK_METROPOLICE_STOP_FIRE_BURST = NEXT_TASK + 3;
	const int TASK_METROPOLICE_AIM_STITCH_AT_PLAYER = NEXT_TASK + 4;
	const int TASK_METROPOLICE_AIM_STITCH_AT_AIRBOAT = NEXT_TASK + 5;
	const int TASK_METROPOLICE_AIM_STITCH_TIGHTLY = NEXT_TASK + 6;
	const int TASK_METROPOLICE_AIM_STITCH_IN_FRONT_OF_AIRBOAT = NEXT_TASK + 7;
	const int TASK_METROPOLICE_AIM_STITCH_ALONG_SIDE_OF_AIRBOAT = NEXT_TASK + 8;
	const int TASK_METROPOLICE_AIM_STITCH_BEHIND_AIRBOAT = NEXT_TASK + 9;
	const int TASK_METROPOLICE_RELOAD_FOR_BURST = NEXT_TASK + 10;
	const int TASK_METROPOLICE_GET_PATH_TO_STITCH = NEXT_TASK + 11;
	const int TASK_METROPOLICE_RESET_LEDGE_CHECK_TIME = NEXT_TASK + 12;
	const int TASK_METROPOLICE_GET_PATH_TO_BESTSOUND_LOS = NEXT_TASK + 13;
	const int TASK_METROPOLICE_AIM_WEAPON_AT_ENEMY = NEXT_TASK + 14;
	const int TASK_METROPOLICE_ARREST_ENEMY = NEXT_TASK + 15;
	const int TASK_METROPOLICE_LEAD_ARREST_ENEMY = NEXT_TASK + 16;
	const int TASK_METROPOLICE_SIGNAL_FIRING_TIME = NEXT_TASK + 17;
	const int TASK_METROPOLICE_ACTIVATE_BATON = NEXT_TASK + 18;
	const int TASK_METROPOLICE_WAIT_FOR_SENTENCE = NEXT_TASK + 19;
	const int TASK_METROPOLICE_GET_PATH_TO_PRECHASE = NEXT_TASK + 20;
	const int TASK_METROPOLICE_CLEAR_PRECHASE = NEXT_TASK + 21;

	static readonly AI_ClassScheduleIdSpace gm_ClassScheduleIdSpace = new();
	static readonly AI_LocalIdSpace gm_SquadSlotIdSpace = new();
	const string gm_pszErrorClassName = "CNPC_MetroPolice";

	public static readonly new DataMap DataDesc = new(typeof(NPC_MetroPolice), AI_BaseNPC.DataDesc, [
		DEFINE.KEYFIELD(nameof(WeaponDrawn), FieldType.Boolean, "weapondrawn"),
		DEFINE.KEYFIELD(nameof(Manhacks), FieldType.Integer, "manhacks"),

		DEFINE.INPUTFUNC(FieldType.Void, "EnableManhackToss", nameof(InputEnableManhackToss), (INPUTFUNCPTR)((self, data) => ((NPC_MetroPolice)self).InputEnableManhackToss(data))),
		DEFINE.INPUTFUNC(FieldType.String, "SetPoliceGoal", nameof(InputSetPoliceGoal), (INPUTFUNCPTR)((self, data) => ((NPC_MetroPolice)self).InputSetPoliceGoal(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "ActivateBaton", nameof(InputActivateBaton), (INPUTFUNCPTR)((self, data) => ((NPC_MetroPolice)self).InputActivateBaton(data))),

		DEFINE.OUTPUT(nameof(OnStunnedPlayer), "OnStunnedPlayer", eventFuncs),
		DEFINE.OUTPUT(nameof(OnCupCopped), "OnCupCopped", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public int PistolClips;
	public int Manhacks;
	public bool WeaponDrawn;
	bool SimpleCops;
	public readonly SimpleSimTimer BatonSwingTimer = new();
	public readonly SimpleSimTimer NextChargeTimer = new();

	int BurstReloadCount;
	TimeUnit_t ValidStitchTime;
	TimeUnit_t NextLedgeCheckTime;
	TimeUnit_t TaskCompletionTime;

	bool ShouldActivateBaton;
	TimeUnit_t BatonDebounceTime;
	TimeUnit_t LastPhysicsFlinchTime;
	TimeUnit_t LastDamageFlinchTime;

	TimeUnit_t NextPainSoundTime;
	TimeUnit_t NextLostSoundTime;
	int IdleChatterType;
	bool PlayerIsNear;

	bool PlayerTooClose;
	bool KeepFacingPlayer;
	TimeUnit_t ChasePlayerTime;
	Vector3 PreChaseOrigin;
	float PreChaseYaw;
	int NumWarnings;
	int NumPlayerHits;

	public OutputEvent OnStunnedPlayer = new();
	public OutputEvent OnCupCopped = new();

	Handle<AI_BaseNPC> Manhack = new();
	Handle<PhysicsProp> BlockingProp = new();

	readonly AI_PolicingBehavior PolicingBehavior = new();
	readonly AI_ActBusyBehavior ActBusyBehavior = new();

	readonly AI_Sentence_NPC_MetroPolice Sentences = new();

	int RecentDamage;
	TimeUnit_t RecentDamageTime;

	public NPC_MetroPolice() => LoadSchedules();

	public override AI_ClassScheduleIdSpace GetClassScheduleIdSpace() => gm_ClassScheduleIdSpace;

	public static new bool LoadSchedules() => CustomSchedules.Load(AI_BaseNPC.LoadSchedules);

	public override bool LoadedSchedules() => CustomSchedules.Valid;

	static readonly AI_CustomSchedules CustomSchedules = new() {
		ClassName = gm_pszErrorClassName,
		IdSpace = gm_ClassScheduleIdSpace,
		ParentIdSpace = AI_BaseNPC.ClassScheduleIdSpace,
		SquadSlotIdSpace = gm_SquadSlotIdSpace,
		Schedules = [
			(nameof(SCHED_METROPOLICE_WAKE_ANGRY), SCHED_METROPOLICE_WAKE_ANGRY),
			(nameof(SCHED_METROPOLICE_INVESTIGATE_SOUND), SCHED_METROPOLICE_INVESTIGATE_SOUND),
			(nameof(SCHED_METROPOLICE_HARASS), SCHED_METROPOLICE_HARASS),
			(nameof(SCHED_METROPOLICE_DRAW_PISTOL), SCHED_METROPOLICE_DRAW_PISTOL),
			(nameof(SCHED_METROPOLICE_CHASE_ENEMY), SCHED_METROPOLICE_CHASE_ENEMY),
			(nameof(SCHED_METROPOLICE_ESTABLISH_LINE_OF_FIRE), SCHED_METROPOLICE_ESTABLISH_LINE_OF_FIRE),
			(nameof(SCHED_METROPOLICE_ESTABLISH_STITCH_LINE_OF_FIRE), SCHED_METROPOLICE_ESTABLISH_STITCH_LINE_OF_FIRE),
			(nameof(SCHED_METROPOLICE_DEPLOY_MANHACK), SCHED_METROPOLICE_DEPLOY_MANHACK),
			(nameof(SCHED_METROPOLICE_ADVANCE), SCHED_METROPOLICE_ADVANCE),
			(nameof(SCHED_METROPOLICE_CHARGE), SCHED_METROPOLICE_CHARGE),
			(nameof(SCHED_METROPOLICE_BURNING_RUN), SCHED_METROPOLICE_BURNING_RUN),
			(nameof(SCHED_METROPOLICE_BURNING_STAND), SCHED_METROPOLICE_BURNING_STAND),
			(nameof(SCHED_METROPOLICE_RETURN_TO_PRECHASE), SCHED_METROPOLICE_RETURN_TO_PRECHASE),
			(nameof(SCHED_METROPOLICE_ALERT_FACE_BESTSOUND), SCHED_METROPOLICE_ALERT_FACE_BESTSOUND),
			(nameof(SCHED_METROPOLICE_ENEMY_RESISTING_ARREST), SCHED_METROPOLICE_ENEMY_RESISTING_ARREST),
			(nameof(SCHED_METROPOLICE_WARN_AND_ARREST_ENEMY), SCHED_METROPOLICE_WARN_AND_ARREST_ENEMY),
			(nameof(SCHED_METROPOLICE_ARREST_ENEMY), SCHED_METROPOLICE_ARREST_ENEMY),
			(nameof(SCHED_METROPOLICE_SMG_NORMAL_ATTACK), SCHED_METROPOLICE_SMG_NORMAL_ATTACK),
			(nameof(SCHED_METROPOLICE_SMG_BURST_ATTACK), SCHED_METROPOLICE_SMG_BURST_ATTACK),
			(nameof(SCHED_METROPOLICE_AIM_STITCH_TIGHTLY), SCHED_METROPOLICE_AIM_STITCH_TIGHTLY),
			(nameof(SCHED_METROPOLICE_AIM_STITCH_AT_AIRBOAT), SCHED_METROPOLICE_AIM_STITCH_AT_AIRBOAT),
			(nameof(SCHED_METROPOLICE_AIM_STITCH_IN_FRONT_OF_AIRBOAT), SCHED_METROPOLICE_AIM_STITCH_IN_FRONT_OF_AIRBOAT),
			(nameof(SCHED_METROPOLICE_AIM_STITCH_ALONG_SIDE_OF_AIRBOAT), SCHED_METROPOLICE_AIM_STITCH_ALONG_SIDE_OF_AIRBOAT),
			(nameof(SCHED_METROPOLICE_AIM_STITCH_BEHIND_AIRBOAT), SCHED_METROPOLICE_AIM_STITCH_BEHIND_AIRBOAT),
			(nameof(SCHED_METROPOLICE_SHOVE), SCHED_METROPOLICE_SHOVE),
			(nameof(SCHED_METROPOLICE_ACTIVATE_BATON), SCHED_METROPOLICE_ACTIVATE_BATON),
			(nameof(SCHED_METROPOLICE_DEACTIVATE_BATON), SCHED_METROPOLICE_DEACTIVATE_BATON),
			(nameof(SCHED_METROPOLICE_SMASH_PROP), SCHED_METROPOLICE_SMASH_PROP),
		],
		Tasks = [
			(nameof(TASK_METROPOLICE_HARASS), TASK_METROPOLICE_HARASS),
			(nameof(TASK_METROPOLICE_DIE_INSTANTLY), TASK_METROPOLICE_DIE_INSTANTLY),
			(nameof(TASK_METROPOLICE_BURST_ATTACK), TASK_METROPOLICE_BURST_ATTACK),
			(nameof(TASK_METROPOLICE_STOP_FIRE_BURST), TASK_METROPOLICE_STOP_FIRE_BURST),
			(nameof(TASK_METROPOLICE_AIM_STITCH_AT_PLAYER), TASK_METROPOLICE_AIM_STITCH_AT_PLAYER),
			(nameof(TASK_METROPOLICE_AIM_STITCH_AT_AIRBOAT), TASK_METROPOLICE_AIM_STITCH_AT_AIRBOAT),
			(nameof(TASK_METROPOLICE_AIM_STITCH_IN_FRONT_OF_AIRBOAT), TASK_METROPOLICE_AIM_STITCH_IN_FRONT_OF_AIRBOAT),
			(nameof(TASK_METROPOLICE_AIM_STITCH_TIGHTLY), TASK_METROPOLICE_AIM_STITCH_TIGHTLY),
			(nameof(TASK_METROPOLICE_AIM_STITCH_ALONG_SIDE_OF_AIRBOAT), TASK_METROPOLICE_AIM_STITCH_ALONG_SIDE_OF_AIRBOAT),
			(nameof(TASK_METROPOLICE_AIM_STITCH_BEHIND_AIRBOAT), TASK_METROPOLICE_AIM_STITCH_BEHIND_AIRBOAT),
			(nameof(TASK_METROPOLICE_RELOAD_FOR_BURST), TASK_METROPOLICE_RELOAD_FOR_BURST),
			(nameof(TASK_METROPOLICE_GET_PATH_TO_STITCH), TASK_METROPOLICE_GET_PATH_TO_STITCH),
			(nameof(TASK_METROPOLICE_RESET_LEDGE_CHECK_TIME), TASK_METROPOLICE_RESET_LEDGE_CHECK_TIME),
			(nameof(TASK_METROPOLICE_GET_PATH_TO_BESTSOUND_LOS), TASK_METROPOLICE_GET_PATH_TO_BESTSOUND_LOS),
			(nameof(TASK_METROPOLICE_ARREST_ENEMY), TASK_METROPOLICE_ARREST_ENEMY),
			(nameof(TASK_METROPOLICE_LEAD_ARREST_ENEMY), TASK_METROPOLICE_LEAD_ARREST_ENEMY),
			(nameof(TASK_METROPOLICE_SIGNAL_FIRING_TIME), TASK_METROPOLICE_SIGNAL_FIRING_TIME),
			(nameof(TASK_METROPOLICE_ACTIVATE_BATON), TASK_METROPOLICE_ACTIVATE_BATON),
			(nameof(TASK_METROPOLICE_WAIT_FOR_SENTENCE), TASK_METROPOLICE_WAIT_FOR_SENTENCE),
			(nameof(TASK_METROPOLICE_GET_PATH_TO_PRECHASE), TASK_METROPOLICE_GET_PATH_TO_PRECHASE),
			(nameof(TASK_METROPOLICE_CLEAR_PRECHASE), TASK_METROPOLICE_CLEAR_PRECHASE),
		],
		Conditions = [
			(nameof(COND_METROPOLICE_ON_FIRE), COND_METROPOLICE_ON_FIRE),
			(nameof(COND_METROPOLICE_ENEMY_RESISTING_ARREST), COND_METROPOLICE_ENEMY_RESISTING_ARREST),
			(nameof(COND_METROPOLICE_PLAYER_TOO_CLOSE), COND_METROPOLICE_PLAYER_TOO_CLOSE),
			(nameof(COND_METROPOLICE_CHANGE_BATON_STATE), COND_METROPOLICE_CHANGE_BATON_STATE),
			(nameof(COND_METROPOLICE_PHYSOBJECT_ASSAULT), COND_METROPOLICE_PHYSOBJECT_ASSAULT),
		],
		SquadSlots = [
			(nameof(SQUAD_SLOT_POLICE_CHARGE_ENEMY), (int)SQUAD_SLOT_POLICE_CHARGE_ENEMY),
			(nameof(SQUAD_SLOT_POLICE_HARASS), (int)SQUAD_SLOT_POLICE_HARASS),
			(nameof(SQUAD_SLOT_POLICE_DEPLOY_MANHACK), (int)SQUAD_SLOT_POLICE_DEPLOY_MANHACK),
			(nameof(SQUAD_SLOT_POLICE_ATTACK_OCCLUDER1), (int)SQUAD_SLOT_POLICE_ATTACK_OCCLUDER1),
			(nameof(SQUAD_SLOT_POLICE_ATTACK_OCCLUDER2), (int)SQUAD_SLOT_POLICE_ATTACK_OCCLUDER2),
			(nameof(SQUAD_SLOT_POLICE_ARREST_ENEMY), (int)SQUAD_SLOT_POLICE_ARREST_ENEMY),
		],
		Declare = static () => {
			AE_METROPOLICE_BATON_ON = DeclareAnimEvent(nameof(AE_METROPOLICE_BATON_ON));
			AE_METROPOLICE_BATON_OFF = DeclareAnimEvent(nameof(AE_METROPOLICE_BATON_OFF));
			AE_METROPOLICE_SHOVE = DeclareAnimEvent(nameof(AE_METROPOLICE_SHOVE));
			AE_METROPOLICE_START_DEPLOY = DeclareAnimEvent(nameof(AE_METROPOLICE_START_DEPLOY));
			AE_METROPOLICE_DRAW_PISTOL = DeclareAnimEvent(nameof(AE_METROPOLICE_DRAW_PISTOL));
			AE_METROPOLICE_DEPLOY_MANHACK = DeclareAnimEvent(nameof(AE_METROPOLICE_DEPLOY_MANHACK));
			ACT_METROPOLICE_DRAW_PISTOL = DeclareActivity(nameof(ACT_METROPOLICE_DRAW_PISTOL));
			ACT_METROPOLICE_DEPLOY_MANHACK = DeclareActivity(nameof(ACT_METROPOLICE_DEPLOY_MANHACK));
			ACT_METROPOLICE_FLINCH_BEHIND = DeclareActivity(nameof(ACT_METROPOLICE_FLINCH_BEHIND));
			ACT_PUSH_PLAYER = DeclareActivity(nameof(ACT_PUSH_PLAYER));
			ACT_MELEE_ATTACK_THRUST = DeclareActivity(nameof(ACT_MELEE_ATTACK_THRUST));
			ACT_ACTIVATE_BATON = DeclareActivity(nameof(ACT_ACTIVATE_BATON));
			ACT_DEACTIVATE_BATON = DeclareActivity(nameof(ACT_DEACTIVATE_BATON));
			ACT_WALK_BATON = DeclareActivity(nameof(ACT_WALK_BATON));
			ACT_IDLE_ANGRY_BATON = DeclareActivity(nameof(ACT_IDLE_ANGRY_BATON));
			g_interactionMetrocopStartedStitch = GetInteractionID();
			g_interactionMetrocopIdleChatter = GetInteractionID();
			g_interactionMetrocopClearSentenceQueues = GetInteractionID();
		},
		Definitions = [
			new(SCHED_METROPOLICE_WAKE_ANGRY,
				[
					TASK_STOP_MOVING,
					(TASK_SET_ACTIVITY, Activity.ACT_IDLE),
					TASK_FACE_ENEMY,
				]),

			new(SCHED_METROPOLICE_INVESTIGATE_SOUND,
				[
					TASK_STOP_MOVING,
					TASK_STORE_LASTPOSITION,
					TASK_METROPOLICE_GET_PATH_TO_BESTSOUND_LOS,
					TASK_FACE_IDEAL,
					TASK_RUN_PATH,
					TASK_WAIT_FOR_MOVEMENT,
					TASK_STOP_MOVING,
					(TASK_WAIT, 5),
					TASK_GET_PATH_TO_LASTPOSITION,
					TASK_WALK_PATH,
					TASK_WAIT_FOR_MOVEMENT,
					TASK_STOP_MOVING,
					TASK_CLEAR_LASTPOSITION,
					TASK_FACE_REASONABLE,
				],
				[
					COND_NEW_ENEMY,
					COND_SEE_FEAR,
					COND_SEE_ENEMY,
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_HEAR_DANGER,
				]),

			new(SCHED_METROPOLICE_HARASS,
				[
					TASK_STOP_MOVING,
					TASK_FACE_ENEMY,
					(TASK_WAIT_FACE_ENEMY, 6),
					TASK_METROPOLICE_HARASS,
					TASK_WAIT_PVS,
				],
				[
					COND_CAN_RANGE_ATTACK1,
					COND_NEW_ENEMY,
				]),

			new(SCHED_METROPOLICE_DRAW_PISTOL,
				[
					TASK_STOP_MOVING,
					(TASK_PLAY_SEQUENCE_FACE_ENEMY, nameof(ACT_METROPOLICE_DRAW_PISTOL)),
					(TASK_WAIT_FACE_ENEMY, 0.1f),
				]),

			new(SCHED_METROPOLICE_CHASE_ENEMY,
				[
					TASK_STOP_MOVING,
					(TASK_SET_FAIL_SCHEDULE, SCHED_METROPOLICE_ESTABLISH_LINE_OF_FIRE),
					(TASK_SET_TOLERANCE_DISTANCE, 24),
					(TASK_GET_CHASE_PATH_TO_ENEMY, 300),
					(TASK_SPEAK_SENTENCE, 6),
					TASK_RUN_PATH,
					TASK_METROPOLICE_RESET_LEDGE_CHECK_TIME,
					TASK_WAIT_FOR_MOVEMENT,
					TASK_FACE_ENEMY,
				],
				[
					COND_NEW_ENEMY,
					COND_ENEMY_DEAD,
					COND_ENEMY_UNREACHABLE,
					COND_CAN_RANGE_ATTACK1,
					COND_CAN_MELEE_ATTACK1,
					COND_CAN_RANGE_ATTACK2,
					COND_CAN_MELEE_ATTACK2,
					COND_TOO_CLOSE_TO_ATTACK,
					COND_TASK_FAILED,
					COND_LOST_ENEMY,
					COND_BETTER_WEAPON_AVAILABLE,
					COND_HEAR_DANGER,
				]),

			new(SCHED_METROPOLICE_ESTABLISH_LINE_OF_FIRE,
				[
					(TASK_SET_FAIL_SCHEDULE, SCHED_FAIL_ESTABLISH_LINE_OF_FIRE),
					TASK_FACE_ENEMY,
					(TASK_SET_TOLERANCE_DISTANCE, 48),
					TASK_GET_PATH_TO_ENEMY_LKP_LOS,
					(TASK_SPEAK_SENTENCE, 6),
					TASK_RUN_PATH,
					TASK_METROPOLICE_RESET_LEDGE_CHECK_TIME,
					TASK_WAIT_FOR_MOVEMENT,
					(TASK_SET_SCHEDULE, SCHED_COMBAT_FACE),
				],
				[
					COND_NEW_ENEMY,
					COND_ENEMY_DEAD,
					COND_CAN_RANGE_ATTACK1,
					COND_CAN_RANGE_ATTACK2,
					COND_CAN_MELEE_ATTACK1,
					COND_CAN_MELEE_ATTACK2,
					COND_HEAR_DANGER,
					COND_HEAVY_DAMAGE,
				]),

			new(SCHED_METROPOLICE_ESTABLISH_STITCH_LINE_OF_FIRE,
				[
					(TASK_SET_FAIL_SCHEDULE, SCHED_FAIL_ESTABLISH_LINE_OF_FIRE),
					TASK_FACE_ENEMY,
					(TASK_SET_TOLERANCE_DISTANCE, 48),
					TASK_METROPOLICE_GET_PATH_TO_STITCH,
					TASK_RUN_PATH,
					TASK_WAIT_FOR_MOVEMENT,
					(TASK_SET_SCHEDULE, SCHED_COMBAT_FACE),
				],
				[
					COND_NEW_ENEMY,
					COND_ENEMY_DEAD,
					COND_HEAR_DANGER,
					COND_HEAVY_DAMAGE,
				]),

			new(SCHED_METROPOLICE_DEPLOY_MANHACK,
				[
					(TASK_SPEAK_SENTENCE, 5),
					(TASK_PLAY_SEQUENCE, nameof(ACT_METROPOLICE_DEPLOY_MANHACK)),
				]),

			new(SCHED_METROPOLICE_ADVANCE,
				[
					TASK_STOP_MOVING,
					(TASK_SET_ACTIVITY, Activity.ACT_IDLE_ANGRY),
					TASK_FACE_ENEMY,
					(TASK_WAIT_FACE_ENEMY, 1),
					(TASK_WAIT_FACE_ENEMY_RANDOM, 3),
					TASK_GET_PATH_TO_ENEMY_LOS,
					TASK_RUN_PATH,
					TASK_WAIT_FOR_MOVEMENT,
					(TASK_SET_ACTIVITY, Activity.ACT_IDLE_ANGRY),
					TASK_FACE_ENEMY,
				],
				[
					COND_CAN_RANGE_ATTACK1,
					COND_ENEMY_DEAD,
				]),

			new(SCHED_METROPOLICE_CHARGE,
				[
					TASK_STOP_MOVING,
					(TASK_SET_FAIL_SCHEDULE, SCHED_METROPOLICE_ADVANCE),
					TASK_STORE_LASTPOSITION,
					(TASK_GET_CHASE_PATH_TO_ENEMY, 300),
					(TASK_RUN_PATH_FOR_UNITS, 150),
					(TASK_STOP_MOVING, 1),
					TASK_FACE_ENEMY,
				],
				[
					COND_NEW_ENEMY,
					COND_ENEMY_DEAD,
					COND_LOST_ENEMY,
					COND_CAN_MELEE_ATTACK1,
					COND_CAN_MELEE_ATTACK2,
					COND_HEAR_DANGER,
					COND_METROPOLICE_PLAYER_TOO_CLOSE,
				]),

			new(SCHED_METROPOLICE_BURNING_RUN,
				[
					(TASK_SET_FAIL_SCHEDULE, SCHED_METROPOLICE_BURNING_STAND),
					(TASK_SET_TOLERANCE_DISTANCE, 24),
					TASK_GET_PATH_TO_ENEMY,
					(TASK_RUN_PATH_TIMED, 10),
					TASK_METROPOLICE_DIE_INSTANTLY,
				]),

			new(SCHED_METROPOLICE_BURNING_STAND,
				[
					(TASK_SET_ACTIVITY, Activity.ACT_IDLE_ON_FIRE),
					(TASK_WAIT, 1.5f),
					(TASK_METROPOLICE_DIE_INSTANTLY, DamageType.Burn),
					(TASK_WAIT, 1f),
				]),

			new(SCHED_METROPOLICE_RETURN_TO_PRECHASE,
				[
					(TASK_WAIT_RANDOM, 1),
					TASK_METROPOLICE_GET_PATH_TO_PRECHASE,
					TASK_WALK_PATH,
					TASK_WAIT_FOR_MOVEMENT,
					TASK_STOP_MOVING,
					TASK_METROPOLICE_CLEAR_PRECHASE,
				],
				[
					COND_NEW_ENEMY,
					COND_CAN_MELEE_ATTACK1,
					COND_CAN_MELEE_ATTACK2,
					COND_TASK_FAILED,
					COND_LOST_ENEMY,
					COND_HEAR_DANGER,
				]),

			new(SCHED_METROPOLICE_ALERT_FACE_BESTSOUND,
				[
					(TASK_SPEAK_SENTENCE, 7),
					(TASK_SET_SCHEDULE, SCHED_ALERT_FACE_BESTSOUND),
				]),

			new(SCHED_METROPOLICE_ENEMY_RESISTING_ARREST,
				[
					TASK_METROPOLICE_SIGNAL_FIRING_TIME,
				]),

			new(SCHED_METROPOLICE_WARN_AND_ARREST_ENEMY,
				[
					(TASK_SET_FAIL_SCHEDULE, SCHED_METROPOLICE_ENEMY_RESISTING_ARREST),
					TASK_STOP_MOVING,
					(TASK_PLAY_SEQUENCE_FACE_ENEMY, Activity.ACT_IDLE_ANGRY),
					TASK_SPEAK_SENTENCE,
					(TASK_METROPOLICE_ARREST_ENEMY, 0.5f),
					TASK_STORE_ENEMY_POSITION_IN_SAVEPOSITION,
					(TASK_METROPOLICE_ARREST_ENEMY, 1),
					(TASK_METROPOLICE_WAIT_FOR_SENTENCE, 1),
					(TASK_SPEAK_SENTENCE, 1),
					(TASK_METROPOLICE_LEAD_ARREST_ENEMY, 5),
					(TASK_METROPOLICE_ARREST_ENEMY, 2),
					(TASK_METROPOLICE_WAIT_FOR_SENTENCE, 1),
					(TASK_SPEAK_SENTENCE, 3),
					(TASK_METROPOLICE_ARREST_ENEMY, 1.5f),
					(TASK_METROPOLICE_WAIT_FOR_SENTENCE, 2),
					TASK_METROPOLICE_SIGNAL_FIRING_TIME,
				],
				[
					COND_NEW_ENEMY,
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_HEAR_DANGER,
					COND_ENEMY_DEAD,
					COND_METROPOLICE_ENEMY_RESISTING_ARREST,
				]),

			new(SCHED_METROPOLICE_ARREST_ENEMY,
				[
					(TASK_SET_FAIL_SCHEDULE, SCHED_METROPOLICE_ENEMY_RESISTING_ARREST),
					TASK_GET_PATH_TO_ENEMY_LOS,
					TASK_RUN_PATH,
					TASK_WAIT_FOR_MOVEMENT,
					TASK_STOP_MOVING,
					(TASK_PLAY_SEQUENCE_FACE_ENEMY, Activity.ACT_IDLE_ANGRY),
					TASK_METROPOLICE_WAIT_FOR_SENTENCE,
					(TASK_SPEAK_SENTENCE, 4),
					(TASK_METROPOLICE_ARREST_ENEMY, 30),
				],
				[
					COND_NEW_ENEMY,
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_HEAR_DANGER,
					COND_ENEMY_DEAD,
					COND_METROPOLICE_ENEMY_RESISTING_ARREST,
					COND_WEAPON_BLOCKED_BY_FRIEND,
					COND_WEAPON_SIGHT_OCCLUDED,
				]),

			new(SCHED_METROPOLICE_SMG_NORMAL_ATTACK,
				[
					TASK_STOP_MOVING,
					TASK_FACE_ENEMY,
					(TASK_ANNOUNCE_ATTACK, 1),
					TASK_METROPOLICE_STOP_FIRE_BURST,
					TASK_RANGE_ATTACK1,
				],
				[
					COND_NEW_ENEMY,
					COND_ENEMY_DEAD,
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_ENEMY_OCCLUDED,
					COND_NO_PRIMARY_AMMO,
					COND_HEAR_DANGER,
					COND_WEAPON_BLOCKED_BY_FRIEND,
					COND_WEAPON_SIGHT_OCCLUDED,
				]),

			new(SCHED_METROPOLICE_SMG_BURST_ATTACK,
				[
					TASK_STOP_MOVING,
					TASK_FACE_ENEMY,
					(TASK_ANNOUNCE_ATTACK, 1),
					(TASK_METROPOLICE_RELOAD_FOR_BURST, 1.4f),
					(TASK_METROPOLICE_AIM_STITCH_AT_PLAYER, 1.4f),
					TASK_METROPOLICE_BURST_ATTACK,
					TASK_FACE_ENEMY,
				],
				[
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_NO_PRIMARY_AMMO,
					COND_HEAR_DANGER,
					COND_WEAPON_BLOCKED_BY_FRIEND,
				]),

			new(SCHED_METROPOLICE_AIM_STITCH_TIGHTLY,
				[
					TASK_STOP_MOVING,
					TASK_FACE_ENEMY,
					(TASK_ANNOUNCE_ATTACK, 1),
					(TASK_METROPOLICE_RELOAD_FOR_BURST, 1f),
					(TASK_METROPOLICE_AIM_STITCH_TIGHTLY, 1f),
					TASK_METROPOLICE_BURST_ATTACK,
					TASK_FACE_ENEMY,
				],
				[
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_NO_PRIMARY_AMMO,
					COND_HEAR_DANGER,
					COND_WEAPON_BLOCKED_BY_FRIEND,
				]),

			new(SCHED_METROPOLICE_AIM_STITCH_AT_AIRBOAT,
				[
					TASK_STOP_MOVING,
					TASK_FACE_ENEMY,
					(TASK_ANNOUNCE_ATTACK, 1),
					(TASK_METROPOLICE_RELOAD_FOR_BURST, 2.5f),
					(TASK_METROPOLICE_AIM_STITCH_AT_AIRBOAT, 2.5f),
					TASK_METROPOLICE_BURST_ATTACK,
					TASK_FACE_ENEMY,
				],
				[
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_NO_PRIMARY_AMMO,
					COND_HEAR_DANGER,
					COND_WEAPON_BLOCKED_BY_FRIEND,
				]),

			new(SCHED_METROPOLICE_AIM_STITCH_IN_FRONT_OF_AIRBOAT,
				[
					TASK_STOP_MOVING,
					TASK_FACE_ENEMY,
					(TASK_ANNOUNCE_ATTACK, 1),
					(TASK_METROPOLICE_RELOAD_FOR_BURST, 2.5f),
					(TASK_METROPOLICE_AIM_STITCH_IN_FRONT_OF_AIRBOAT, 2.5f),
					TASK_METROPOLICE_BURST_ATTACK,
					TASK_FACE_ENEMY,
				],
				[
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_NO_PRIMARY_AMMO,
					COND_HEAR_DANGER,
					COND_WEAPON_BLOCKED_BY_FRIEND,
				]),

			new(SCHED_METROPOLICE_AIM_STITCH_ALONG_SIDE_OF_AIRBOAT,
				[
					TASK_STOP_MOVING,
					TASK_FACE_ENEMY,
					(TASK_ANNOUNCE_ATTACK, 1),
					(TASK_METROPOLICE_RELOAD_FOR_BURST, 2.5f),
					(TASK_METROPOLICE_AIM_STITCH_ALONG_SIDE_OF_AIRBOAT, 2.5f),
					TASK_METROPOLICE_BURST_ATTACK,
					TASK_FACE_ENEMY,
				],
				[
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_NO_PRIMARY_AMMO,
					COND_HEAR_DANGER,
					COND_WEAPON_BLOCKED_BY_FRIEND,
				]),

			new(SCHED_METROPOLICE_AIM_STITCH_BEHIND_AIRBOAT,
				[
					TASK_STOP_MOVING,
					TASK_FACE_ENEMY,
					(TASK_ANNOUNCE_ATTACK, 1),
					(TASK_METROPOLICE_RELOAD_FOR_BURST, 2.5f),
					(TASK_METROPOLICE_AIM_STITCH_BEHIND_AIRBOAT, 2.5f),
					TASK_METROPOLICE_BURST_ATTACK,
					TASK_FACE_ENEMY,
				],
				[
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_NO_PRIMARY_AMMO,
					COND_HEAR_DANGER,
					COND_WEAPON_BLOCKED_BY_FRIEND,
				]),

			new(SCHED_METROPOLICE_SHOVE,
				[
					TASK_STOP_MOVING,
					(TASK_FACE_PLAYER, 0.1f),
					(TASK_METROPOLICE_ACTIVATE_BATON, 1),
					(TASK_PLAY_SEQUENCE, nameof(ACT_PUSH_PLAYER)),
				]),

			new(SCHED_METROPOLICE_ACTIVATE_BATON,
				[
					TASK_STOP_MOVING,
					TASK_FACE_TARGET,
					(TASK_METROPOLICE_ACTIVATE_BATON, 1),
				]),

			new(SCHED_METROPOLICE_DEACTIVATE_BATON,
				[
					TASK_STOP_MOVING,
					TASK_METROPOLICE_ACTIVATE_BATON,
				]),

			new(SCHED_METROPOLICE_SMASH_PROP,
				[
					TASK_GET_PATH_TO_TARGET,
					(TASK_MOVE_TO_TARGET_RANGE, 50),
					TASK_STOP_MOVING,
					TASK_FACE_TARGET,
					(TASK_ANNOUNCE_ATTACK, 1),
					(TASK_PLAY_SEQUENCE, Activity.ACT_MELEE_ATTACK1),
				],
				[
					COND_NEW_ENEMY,
					COND_ENEMY_DEAD,
				]),
		],
	};

	public override BaseEntity? CheckTraceHullAttack(in Vector3 start, in Vector3 end, in Vector3 mins, in Vector3 maxs, float damage, DamageType dmgType, float forceScale = 1.0f, bool damageAnyNPC = false) {
		TakeDamageInfo dmgInfo = new(this, this, damage, DamageType.Slash);

		TraceFilterMetroPolice traceFilter = new(this, Source.CollisionGroup.None, dmgInfo, forceScale, damageAnyNPC);

		Ray ray = default;
		ray.Init(start, end, mins, maxs);

		enginetrace.TraceRay(in ray, Mask.Shot, ref traceFilter, out Trace tr);

		BaseEntity? entity = traceFilter.Hit;

		if (entity == null) {
			Vector3 topCenter = GetAbsOrigin();
			CollisionProp().WorldSpaceAABB(out _, out Vector3 aabbMaxs);
			topCenter.Z = aabbMaxs.Z + 1.0f;

			ray.Init(topCenter, end, mins, maxs);
			enginetrace.TraceRay(in ray, Mask.ShotHull, ref traceFilter, out tr);

			entity = traceFilter.Hit;
		}

		return entity;
	}

	public override void OnScheduleChange() {
		base.OnScheduleChange();

		if (GetEnemy() != null && HasCondition((int)SCOND_t.COND_ENEMY_DEAD))
			AnnounceEnemyKill(GetEnemy());
	}

	public override void PrescheduleThink() {
		base.PrescheduleThink();

		Sentences.UpdateSentenceQueue();

		PlayerIsNear = false;
		if (PlayerIsCriminal() == false) {
			BasePlayer? player = AI_GetClosestPlayer();

			if (player != null && (player.WorldSpaceCenter() - WorldSpaceCenter()).LengthSquared() < (128 * 128)) {
				PlayerIsNear = true;
				AddLookTarget(player, 0.75f, 5.0f);

				if ((PolicingBehavior.IsEnabled() == false) && (NumWarnings >= METROPOLICE_MAX_WARNINGS)) {
					BatonDebounceTime = gpGlobals.CurTime + random.RandomFloat(2.5f, 4.0f);
					SetTarget(player);
					SetBatonState(true);
				}
			}
			else {
				if (PolicingBehavior.IsEnabled() == false && gpGlobals.CurTime > BatonDebounceTime)
					SetBatonState(false);

				KeepFacingPlayer = false;
			}
		}

		if (IsOnFire())
			SetCondition(COND_METROPOLICE_ON_FIRE);
		else
			ClearCondition(COND_METROPOLICE_ON_FIRE);

		if (gpGlobals.CurTime > RecentDamageTime + RECENT_DAMAGE_INTERVAL) {
			RecentDamage = 0;
			RecentDamageTime = 0;
		}
	}

	public override void Precache() {
		if (HasSpawnFlags(SF_NPC_START_EFFICIENT))
			SetModelName("models/police_cheaple.mdl");
		else
			SetModelName("models/police.mdl");

		PrecacheModel(GetModelName());

		Util.PrecacheOther("npc_manhack");

		PrecacheScriptSound("NPC_Metropolice.Shove");
		PrecacheScriptSound("NPC_MetroPolice.WaterSpeech");
		PrecacheScriptSound("NPC_MetroPolice.HidingSpeech");
		enginesound.PrecacheSentenceGroup("METROPOLICE");

		base.Precache();
	}

	public override bool CreateComponents() {
		if (!base.CreateComponents())
			return false;

		Sentences.Init(this, "NPC_Metropolice.SentenceParameters");
		return true;
	}

	public override void Spawn() {
		Precache();

		SetModel(GetModelName());

		SetHullType(AI_HullType.Human);
		SetHullSizeNormal();

		SetSolid(SolidType.BBox);
		AddSolidFlags(SolidFlags.NotStandable);
		SetMoveType(Source.MoveType.Step);
		SetBloodColor(Shared.BloodColor.Red);
		IdleChatterType = METROPOLICE_CHATTER_ASK_QUESTION;
		SimpleCops = HasSpawnFlags(SF_METROPOLICE_SIMPLE_VERSION);
		if (HasSpawnFlags(SF_METROPOLICE_NOCHATTER))
			AddSpawnFlags(SF_NPC_GAG);

		if (!SimpleCops)
			Health = (int)sk_metropolice_health.GetFloat();
		else
			Health = (int)sk_metropolice_simple_health.GetFloat();

		FieldOfView = -0.2f;
		NPCState = NPCState.None;
		if (!HasSpawnFlags(SF_NPC_START_EFFICIENT)) {
			CapabilitiesAdd(Capability.TurnHead | Capability.AnimatedFace);
			CapabilitiesAdd(Capability.AimGun | Capability.MoveShoot);
		}
		CapabilitiesAdd(Capability.MoveGround);
		CapabilitiesAdd(Capability.UseWeapons | Capability.NoHitSquadmates);
		CapabilitiesAdd(Capability.Squad);
		CapabilitiesAdd(Capability.Duck | Capability.AutoDoors | Capability.OpenDoors);
		CapabilitiesAdd(Capability.UseShotRegulator);

		PistolClips = METROPOLICE_NUM_CLIPS;

		NPCInit();

		if (HasSpawnFlags(SF_METROPOLICE_MID_RANGE_ATTACK)) {
			DistTooFar = METROPOLICE_MID_RANGE_ATTACK_RANGE;
			SetDistLook(METROPOLICE_MID_RANGE_ATTACK_RANGE);
		}

		Manhack.Set(null);

		BaseCombatWeapon? weapon = GetActiveWeapon();
		if (weapon != null) {
			if (!FClassnameIs(weapon, "weapon_pistol"))
				WeaponDrawn = true;

			if (!WeaponDrawn)
				weapon.AddEffects(EntityEffects.NoDraw);
		}

		GetEnemies().SetFreeKnowledgeDuration(6.0f);

		ShouldActivateBaton = false;
		ValidStitchTime = -1.0f;
		NextLedgeCheckTime = -1.0f;
		BurstReloadCount = METROPOLICE_BURST_RELOAD_COUNT;

		if (HasSpawnFlags(SF_METROPOLICE_ALWAYS_STITCH)) {
			if (Weapon_OwnsThisType("weapon_smg1", 0) == null) {
				Warning("Warning! Metrocop is trying to use the stitch behavior but he has no smg1!\n");
				RemoveSpawnFlags(SF_METROPOLICE_ALWAYS_STITCH);
			}
		}

		NumWarnings = 0;
		PlayerTooClose = false;
		KeepFacingPlayer = false;
		ChasePlayerTime = 0;
		PreChaseOrigin = vec3_origin;
		PreChaseYaw = 0;

		FnUse = PrecriminalUse;

		if (Manhacks != 0)
			SetBodygroup(METROPOLICE_BODYGROUP_MANHACK, 1);
	}

	public override void Weapon_Equip(BaseCombatWeapon weapon) {
		base.Weapon_Equip(weapon);

		if (HasSpawnFlags(SF_METROPOLICE_MID_RANGE_ATTACK) && GetActiveWeapon() != null) {
			GetActiveWeapon()!.MaxRange1 = METROPOLICE_MID_RANGE_ATTACK_RANGE;
			GetActiveWeapon()!.MaxRange2 = METROPOLICE_MID_RANGE_ATTACK_RANGE;
		}
	}

	public override EntityCapabilities ObjectCaps() => UsableNPCObjectCaps(base.ObjectCaps());

	public override void SpeakSentence(int sentenceType) {
		if (!PlayerIsCriminal())
			return;

		switch (sentenceType) {
			case METROPOLICE_SENTENCE_FREEZE:
				Sentences.Speak("METROPOLICE_FREEZE", SentencePriority.Medium, SentenceCriteria.Normal);
				break;

			case METROPOLICE_SENTENCE_HES_OVER_HERE:
				Sentences.Speak("METROPOLICE_OVER_HERE", SentencePriority.Medium, SentenceCriteria.Normal);
				break;

			case METROPOLICE_SENTENCE_HES_RUNNING:
				Sentences.Speak("METROPOLICE_HES_RUNNING", SentencePriority.High, SentenceCriteria.Normal);
				break;

			case METROPOLICE_SENTENCE_TAKE_HIM_DOWN:
				Sentences.Speak("METROPOLICE_TAKE_HIM_DOWN", SentencePriority.High, SentenceCriteria.Normal);
				break;

			case METROPOLICE_SENTENCE_ARREST_IN_POSITION:
				Sentences.Speak("METROPOLICE_ARREST_IN_POS", SentencePriority.Medium, SentenceCriteria.Normal);
				break;

			case METROPOLICE_SENTENCE_DEPLOY_MANHACK:
				Sentences.Speak("METROPOLICE_DEPLOY_MANHACK");
				break;

			case METROPOLICE_SENTENCE_MOVE_INTO_POSITION: {
					BaseEntity? entity = GetEnemy();

					if (entity != null && !HasSpawnFlags(SF_METROPOLICE_ARREST_ENEMY)) {
						if (entity.IsPlayer() && (entity.GetHealth() <= 20)) {
							if (!HasMemory(bits_MEMORY_PLAYER_HURT)) {
								if (Sentences.Speak("METROPOLICE_PLAYERHIT", SentencePriority.High) >= 0)
									Squad!.SquadRemember(bits_MEMORY_PLAYER_HURT);
							}
						}

						if (GetNavigator()!.GetPath().GetPathLength() > 20 * 12.0f)
							Sentences.Speak("METROPOLICE_FLANK");
					}
				}
				break;

			case METROPOLICE_SENTENCE_HEARD_SOMETHING:
				if ((GetState() == NPCState.Alert) || (GetState() == NPCState.Idle))
					Sentences.Speak("METROPOLICE_HEARD_SOMETHING", SentencePriority.Medium);
				break;
		}
	}

	void AnnounceEnemyKill(BaseEntity? enemy) {
		if (enemy == null)
			return;

		string sentenceName = "METROPOLICE_KILL_MONST";
		switch (enemy.Classify()) {
			case Class_T.Player:
				sentenceName = "METROPOLICE_KILL_PLAYER";
				break;

			case Class_T.PlayerAlly:
			case Class_T.CitizenRebel:
			case Class_T.CitizenPassive:
			case Class_T.Vortigaunt:
				sentenceName = "METROPOLICE_KILL_CITIZENS";
				break;

			case Class_T.PlayerAllyVital:
				sentenceName = "METROPOLICE_KILL_CHARACTER";
				break;

			case Class_T.Antlion:
				sentenceName = "METROPOLICE_KILL_BUGS";
				break;

			case Class_T.Zombie:
				sentenceName = "METROPOLICE_KILL_ZOMBIES";
				break;

			case Class_T.Headcrab:
			case Class_T.Barnacle:
				sentenceName = "METROPOLICE_KILL_PARASITES";
				break;
		}

		Sentences.Speak(sentenceName, SentencePriority.High);
	}

	void AnnounceOutOfAmmo() {
		if (HasCondition((int)SCOND_t.COND_NO_PRIMARY_AMMO))
			Sentences.Speak("METROPOLICE_COVER_NO_AMMO");
		else
			Sentences.Speak("METROPOLICE_COVER_LOW_AMMO");
	}

	void AnnounceTakeCoverFromDanger(ref WorldSoundInstance sound) {
		BaseEntity? soundOwner = sound.Owner.Get();
		if (soundOwner != null) {
			if (soundOwner is BaseGrenade grenade) {
				if (IRelationType(grenade.Thrower.Get()) != Disposition.LI)
					Sentences.Speak("METROPOLICE_DANGER_GREN", SentencePriority.High, SentenceCriteria.Normal);
				return;
			}

			if (soundOwner.GetServerVehicle() != null) {
				Sentences.Speak("METROPOLICE_DANGER_VEHICLE", SentencePriority.High, SentenceCriteria.Normal);
				return;
			}

			if (FClassnameIs(soundOwner, "npc_manhack"))
				throw new NotImplementedException();
		}

		Sentences.Speak("METROPOLICE_DANGER", SentencePriority.High, SentenceCriteria.Normal);
	}

	public override bool OnBeginMoveAndShoot() {
		if (base.OnBeginMoveAndShoot()) {
			if (HasStrategySlotRange(SquadSlot.Attack1, SquadSlot.Attack2))
				return true;

			if (OccupyStrategySlotRange(SquadSlot.Attack1, SquadSlot.Attack2))
				return true;
		}

		return false;
	}

	public override void OnEndMoveAndShoot() => VacateStrategySlot();

	public override bool CreateBehaviors() {
		AddBehavior(PolicingBehavior);
		AddBehavior(ActBusyBehavior);

		return base.CreateBehaviors();
	}

	public void InputEnableManhackToss(InputData inputdata) {
		if (HasSpawnFlags(SF_METROPOLICE_NO_MANHACK_DEPLOY))
			RemoveSpawnFlags(SF_METROPOLICE_NO_MANHACK_DEPLOY);
	}

	public void InputSetPoliceGoal(InputData inputdata) {
		BaseEntity? goal = gEntList.FindEntityByName(null, inputdata.Value.String());

		if (goal == null) {
			DevMsg($"SetPoliceGoal: {GetClassname()} ({GetDebugName()}) unable to find ai_goal_police: {inputdata.Value.String()}\n");
			return;
		}

		if (goal is not AI_PoliceGoal policeGoal) {
			DevMsg($"SetPoliceGoal: {GetClassname()} ({GetDebugName()})'s target {inputdata.Value.String()} is not an ai_goal_police entity!\n");
			return;
		}

		PolicingBehavior.Enable(policeGoal);
	}

	public void InputActivateBaton(InputData inputdata) => SetBatonState(inputdata.Value.Bool());

	public override void DeathSound(in TakeDamageInfo info) {
		if (IsOnFire())
			return;

		Sentences.Speak("METROPOLICE_DIE", SentencePriority.Invalid, SentenceCriteria.Always);
	}

	public override void LostEnemySound() {
		if (!PlayerIsCriminal())
			return;

		if (gpGlobals.CurTime <= NextLostSoundTime)
			return;

		string sentence;
		if (GetEnemy() == null || gpGlobals.CurTime - GetEnemyLastTimeSeen() > 10)
			sentence = "METROPOLICE_LOST_LONG";
		else
			sentence = "METROPOLICE_LOST_SHORT";

		if (Sentences.Speak(sentence) >= 0)
			NextLostSoundTime = gpGlobals.CurTime + random.RandomFloat(5.0f, 15.0f);
	}

	public override void FoundEnemySound() {
		if (HasSpawnFlags(SF_METROPOLICE_ARREST_ENEMY))
			return;

		Sentences.Speak("METROPOLICE_REFIND_ENEMY", SentencePriority.High);
	}

	public override bool ShouldPlayIdleSound() {
		if ((NPCState == NPCState.Idle) || (NPCState == NPCState.Alert)) {
			if (IdleChatterType >= METROPOLICE_CHATTER_RESPONSE)
				return FOkToMakeSound();
		}

		return base.ShouldPlayIdleSound();
	}

	static readonly string[,] IdleQuestions = {
		{ "METROPOLICE_IDLE_CHECK", "METROPOLICE_IDLE_QUEST" },
		{ "METROPOLICE_IDLE_CHECK_CR", "METROPOLICE_IDLE_QUEST_CR" },
	};

	static readonly string[,] IdleResponses = {
		{ "METROPOLICE_IDLE_CLEAR", "METROPOLICE_IDLE_ANSWER" },
		{ "METROPOLICE_IDLE_CLEAR_CR", "METROPOLICE_IDLE_ANSWER_CR" },
	};

	public override void IdleSound() {
		bool isCriminal = PlayerIsCriminal();

		switch (IdleChatterType) {
			case METROPOLICE_CHATTER_WAIT_FOR_RESPONSE:
				break;

			case METROPOLICE_CHATTER_ASK_QUESTION: {
					if (PlayerIsNear && !HasMemory(bits_MEMORY_PLAYER_HARASSED)) {
						if (Sentences.Speak("METROPOLICE_IDLE_HARASS_PLAYER", SentencePriority.Normal, SentenceCriteria.Normal) >= 0) {
							Remember(bits_MEMORY_PLAYER_HARASSED);
							GetSquad()?.SquadRemember(bits_MEMORY_PLAYER_HARASSED);
						}
						return;
					}

					if (random.RandomInt(0, 1) == 0)
						break;

					int questionType = random.RandomInt(0, METROPOLICE_CHATTER_RESPONSE_TYPE_COUNT);
					if (!IsInSquad() || (questionType == METROPOLICE_CHATTER_RESPONSE_TYPE_COUNT)) {
						Sentences.Speak(isCriminal ? "METROPOLICE_IDLE_CR" : "METROPOLICE_IDLE");
						break;
					}

					if (Sentences.Speak(IdleQuestions[isCriminal ? 1 : 0, questionType]) >= 0) {
						GetSquad()!.BroadcastInteraction(g_interactionMetrocopIdleChatter, METROPOLICE_CHATTER_RESPONSE + questionType, this);
						IdleChatterType = METROPOLICE_CHATTER_WAIT_FOR_RESPONSE;
					}
				}
				break;

			default: {
					int responseType = IdleChatterType - METROPOLICE_CHATTER_RESPONSE;

					if (Sentences.Speak(IdleResponses[isCriminal ? 1 : 0, responseType]) >= 0) {
						GetSquad()!.BroadcastInteraction(g_interactionMetrocopIdleChatter, METROPOLICE_CHATTER_ASK_QUESTION, this);
						IdleChatterType = METROPOLICE_CHATTER_ASK_QUESTION;
					}
				}
				break;
		}
	}

	public override int GetSoundInterests() {
		return (int)(SoundInstanceType.World | SoundInstanceType.Combat | SoundInstanceType.Player | SoundInstanceType.PlayerVehicle | SoundInstanceType.Danger |
			SoundInstanceType.PhysicsDanger | SoundInstanceType.BulletImpact | SoundInstanceType.MoveAway);
	}

	public override float MaxYawSpeed() {
		switch (GetActivity()) {
			case Activity.ACT_TURN_LEFT:
			case Activity.ACT_TURN_RIGHT:
				return 120;

			case Activity.ACT_RUN:
			case Activity.ACT_RUN_HURT:
				return 15;

			case Activity.ACT_WALK:
			case Activity.ACT_WALK_CROUCH:
			case Activity.ACT_RUN_CROUCH:
				return 25;

			default:
				return 45;
		}
	}

	public override Class_T Classify() => Class_T.MetroPolice;

	bool PlayerIsCriminal() {
		if (PolicingBehavior.IsEnabled() && PolicingBehavior.TargetIsHostile())
			return true;

		if (GlobalEntity.GetState("gordon_precriminal") == GlobalEState.On)
			return false;

		return true;
	}

	public override Disposition IRelationType(BaseEntity? target) {
		Disposition disp = base.IRelationType(target);

		if (target == null)
			return disp;

		if (target.Classify() == Class_T.Player) {
			if (!PlayerIsCriminal() && (disp == Disposition.HT)) {
				if (ChasePlayerTime != 0 && ChasePlayerTime > gpGlobals.CurTime)
					return Disposition.HT;
				return Disposition.NU;
			}
		}

		return disp;
	}

	void OnAnimEventShove() {
		BaseEntity? hurt = CheckTraceHullAttack(16, new Vector3(-16, -16, -16), new Vector3(16, 16, 16), 15, DamageType.Club, 1.0f, false);

		if (hurt != null) {
			BasePlayer? player = ToBasePlayer(hurt);

			if (player != null) {
				player.ViewPunch(new QAngle(8, 14, 0));

				Vector3 dir = hurt.GetAbsOrigin() - GetAbsOrigin();
				MathLib.VectorNormalize(ref dir);

				MathLib.VectorAngles(dir, out QAngle angles);
				MathLib.AngleVectors(angles, out Vector3 forward, out _, out _);

				if ((hurt.GetFlags() & EntityFlags.OnGround) == 0)
					forward.Z = 0.0f;

				hurt.ApplyAbsVelocityImpulse(forward * 250.0f);

				player.ForceDropOfCarriedPhysObjects(null);
			}

			EmitSound("NPC_Metropolice.Shove");
		}
	}

	void OnAnimEventBatonOn() {
		if (GetActiveWeapon() is WeaponStunStick stick)
			stick.SetStunState(true);
	}

	void OnAnimEventBatonOff() {
		if (GetActiveWeapon() is WeaponStunStick stick)
			stick.SetStunState(false);
	}

	public override void HandleAnimEvent(ref AnimEvent animEvent) {
		if (animEvent.Event == (int)AE_METROPOLICE_SHOVE) {
			OnAnimEventShove();
			return;
		}

		if (animEvent.Event == (int)AE_METROPOLICE_BATON_ON) {
			OnAnimEventBatonOn();
			return;
		}

		if (animEvent.Event == (int)AE_METROPOLICE_BATON_OFF) {
			OnAnimEventBatonOff();
			return;
		}

		if (animEvent.Event == (int)AE_METROPOLICE_START_DEPLOY)
			throw new NotImplementedException();

		if (animEvent.Event == (int)AE_METROPOLICE_DRAW_PISTOL) {
			WeaponDrawn = true;
			GetActiveWeapon()?.RemoveEffects(EntityEffects.NoDraw);
			return;
		}

		if (animEvent.Event == (int)AE_METROPOLICE_DEPLOY_MANHACK)
			throw new NotImplementedException();

		base.HandleAnimEvent(ref animEvent);
	}

	public override bool HandleInteraction(int interactionType, object? data, BaseCombatCharacter? sourceEnt) {
		if (interactionType == g_interactionMetrocopStartedStitch) {
			ValidStitchTime = gpGlobals.CurTime + random.RandomFloat(METROPOLICE_SQUAD_STITCH_MIN_INTERVAL, METROPOLICE_SQUAD_STITCH_MAX_INTERVAL);
			return true;
		}

		if (interactionType == g_interactionMetrocopIdleChatter) {
			IdleChatterType = (int)data!;
			return true;
		}

		if (interactionType == g_interactionMetrocopClearSentenceQueues) {
			Sentences.ClearQueue();
			return true;
		}

		if (interactionType == Props.g_interactionHitByPlayerThrownPhysObj) {
			if (!IsInAScript() && (NPCState != NPCState.Script))
				SetCondition(COND_METROPOLICE_PHYSOBJECT_ASSAULT);
			else
				AdministerJustice();

			if (data is BaseProp prop) {
				if (prop.NameMatches("cupcop_can"))
					OnCupCopped.FireOutput(this, null);
			}

			return true;
		}

		return base.HandleInteraction(interactionType, data, sourceEnt);
	}

	public override Activity NPC_TranslateActivity(Activity newActivity) {
		if (IsOnFire() && newActivity == Activity.ACT_RUN)
			return Activity.ACT_RUN_ON_FIRE;

		if (newActivity == ACT_PUSH_PLAYER) {
			if (NumWarnings >= METROPOLICE_MAX_WARNINGS)
				return Activity.ACT_MELEE_ATTACK1;
		}

		newActivity = base.NPC_TranslateActivity(newActivity);

		if (WeaponDrawn && newActivity == Activity.ACT_IDLE && (GetState() == NPCState.Combat || BatonActive()))
			newActivity = Activity.ACT_IDLE_ANGRY;

		return newActivity;
	}

	int SelectScheduleInvestigateSound() {
		if (PlayerIsCriminal() && !HasCondition((int)SCOND_t.COND_SEE_ENEMY)) {
			if (HasCondition((int)SCOND_t.COND_HEAR_COMBAT) || HasCondition((int)SCOND_t.COND_HEAR_PLAYER)) {
				if (Squad != null && OccupyStrategySlot(SquadSlot.InvestigateSound))
					return SCHED_METROPOLICE_INVESTIGATE_SOUND;
			}
		}

		return SCHED_NONE;
	}

	public override bool OnObstructionPreSteer(ref AILocalMoveGoal moveGoal, float distClear, ref AIMoveResult result) {
		if (moveGoal.DirectTrace.Obstruction != null) {
			if (moveGoal.DirectTrace.Obstruction is PhysicsProp prop && prop.GetHealth() != 0)
				BlockingProp.Set(prop);
			else
				BlockingProp.Set(null);
		}

		return base.OnObstructionPreSteer(ref moveGoal, distClear, ref result);
	}

	int SelectCombatSchedule() => throw new NotImplementedException();

	int SelectAirboatCombatSchedule() => throw new NotImplementedException();

	int SelectMoveToLedgeSchedule() => throw new NotImplementedException();

	int SelectStitchSchedule() => throw new NotImplementedException();

	bool ShouldAttemptToStitch() => throw new NotImplementedException();

	bool TryToEnterPistolSlot(SquadSlot squadSlot) => throw new NotImplementedException();

	bool IsEnemyInAnAirboat() {
		if (GetEnemy() == null || !GetEnemy()!.IsPlayer())
			return false;

		BaseEntity? vehicle = ((BasePlayer)GetEnemy()!).GetVehicleEntity();
		if (vehicle == null)
			return false;

		return FClassnameIs(vehicle, "prop_vehicle_airboat");
	}

	static readonly string[] HarassmentWarnings = [
		"METROPOLICE_BACK_UP_A",
		"METROPOLICE_BACK_UP_B",
		"METROPOLICE_BACK_UP_C",
	];

	void AnnounceHarrassment() =>
		Sentences.Speak(HarassmentWarnings[random.RandomInt(0, HarassmentWarnings.Length - 1)], SentencePriority.Medium, SentenceCriteria.Normal);

	void IncrementPlayerCriminalStatus() {
		BasePlayer? player = AI_GetClosestPlayer();

		if (player != null) {
			AddLookTarget(player, 0.8f, 5.0f);

			if (NumWarnings < METROPOLICE_MAX_WARNINGS)
				NumWarnings++;

			if (NumWarnings >= (METROPOLICE_MAX_WARNINGS - 1)) {
				SetTarget(player);
				SetBatonState(true);
			}
		}

		BatonDebounceTime = gpGlobals.CurTime + random.RandomFloat(2.0f, 4.0f);

		AnnounceHarrassment();

		KeepFacingPlayer = true;
	}

	int SelectShoveSchedule() {
		IncrementPlayerCriminalStatus();

		ChasePlayerTime = 0;
		return SCHED_METROPOLICE_SHOVE;
	}

	public override float GetIdealAccel() => GetIdealSpeed() * 2.0f;

	void AdministerJustice() {
		if (!AI_IsSinglePlayer())
			return;

		if (!IsInAScript() && (NPCState != NPCState.Script) && HasSpawnFlags(SF_METROPOLICE_ALLOWED_TO_RESPOND)) {
			if (PreChaseOrigin == vec3_origin) {
				PreChaseOrigin = GetAbsOrigin();
				PreChaseYaw = GetAbsAngles().Y;
			}
			ChasePlayerTime = gpGlobals.CurTime + RandomFloat(3, 7);

			BasePlayer? player = AI_GetClosestPlayer();
			SetEnemy(player);
			SetState(NPCState.Combat);
			UpdateEnemyMemory(player, player!.GetAbsOrigin());
		}
		else {
			KeepFacingPlayer = true;

			List<AI_BaseNPC> ais = g_AI_Manager.AccessAIs();
			int numAIs = g_AI_Manager.NumAIs();
			for (int i = 0; i < numAIs; i++) {
				if (ais[i] == this)
					continue;

				if (ais[i].Classify() == Class_T.MetroPolice && FClassnameIs(ais[i], "npc_metropolice")) {
					NPC_MetroPolice npc = (NPC_MetroPolice)ais[i];
					if (npc.HasSpawnFlags(SF_METROPOLICE_ALLOWED_TO_RESPOND)) {
						BasePlayer? player = AI_GetClosestPlayer();
						if (FVisible(npc) && player != null && npc.FVisible(player) &&
							Util.DistApprox(WorldSpaceCenter(), npc.WorldSpaceCenter()) < 512) {
							npc.AdministerJustice();
							break;
						}
					}
				}
			}
		}
	}

	public override int SelectSchedule() {
		BasePlayer? player = AI_GetClosestPlayer();
		if (GetEnemy() == null && HasCondition((int)SCOND_t.COND_IN_PVS) && player != null && !player.IsAlive())
			return SCHED_PATROL_WALK;

		if (HasCondition(COND_METROPOLICE_ON_FIRE)) {
			Sentences.Speak("METROPOLICE_ON_FIRE", SentencePriority.Invalid, SentenceCriteria.Always);
			return SCHED_METROPOLICE_BURNING_STAND;
		}

		if (HasCondition(COND_METROPOLICE_PHYSOBJECT_ASSAULT)) {
			ClearCondition(COND_METROPOLICE_PHYSOBJECT_ASSAULT);

			if (PlayerIsCriminal() == false) {
				Sentences.Speak("METROPOLICE_HIT_BY_PHYSOBJECT", SentencePriority.Invalid, SentenceCriteria.Always);
				NumWarnings = METROPOLICE_MAX_WARNINGS;
				AdministerJustice();
			}
			else if (GlobalEntity.GetState("gordon_precriminal") == GlobalEState.On)
				Sentences.Speak("METROPOLICE_IDLE_HARASS_PLAYER", SentencePriority.Invalid, SentenceCriteria.Always);
		}

		int sched = SelectFlinchSchedule();
		if (sched != SCHED_NONE)
			return sched;

		if (HasBaton()) {
			if (ShouldActivateBaton && BatonActive() == false && IsCurSchedule(SCHED_METROPOLICE_ACTIVATE_BATON) == false)
				return SCHED_METROPOLICE_ACTIVATE_BATON;

			if (ShouldActivateBaton == false && BatonActive() && IsCurSchedule(SCHED_METROPOLICE_DEACTIVATE_BATON) == false)
				return SCHED_METROPOLICE_DEACTIVATE_BATON;

			if (metropolice_chase_use_follow.GetBool()) {
				if (GetEnemy() != null)
					throw new NotImplementedException();
			}
		}

		if (PlayerIsCriminal() == false) {
			if (!IsInAScript() && (HasCondition(COND_METROPOLICE_PLAYER_TOO_CLOSE) || PlayerTooClose)) {
				if (NumPlayerHits < 3 || PreChaseOrigin == vec3_origin) {
					ClearCondition(COND_METROPOLICE_PLAYER_TOO_CLOSE);
					PlayerTooClose = false;

					return SelectShoveSchedule();
				}
			}
			else if (NumPlayerHits != 0) {
				if ((NPCState != NPCState.Combat) &&
					 (PreChaseOrigin != vec3_origin) &&
					 (ChasePlayerTime < gpGlobals.CurTime)) {
					return SCHED_METROPOLICE_RETURN_TO_PRECHASE;
				}
			}
		}

		if (HasCondition((int)SCOND_t.COND_HEAR_PHYSICS_DANGER)) {
			if (LastPhysicsFlinchTime + 4.0f <= gpGlobals.CurTime) {
				LastPhysicsFlinchTime = gpGlobals.CurTime;
				return SCHED_FLINCH_PHYSICS;
			}
		}

		if (HasCondition((int)SCOND_t.COND_HEAR_DANGER)) {
			ref WorldSoundInstance sound = ref GetBestSound();

			Assert(!Unsafe.IsNullRef(ref sound));
			if (!Unsafe.IsNullRef(ref sound)) {
				if ((sound.Type & SoundInstanceType.Danger) != 0) {
					AnnounceTakeCoverFromDanger(ref sound);
					return SCHED_TAKE_COVER_FROM_BEST_SOUND;
				}
				if (!HasCondition((int)SCOND_t.COND_SEE_ENEMY) && (sound.Type & (SoundInstanceType.Player | SoundInstanceType.PlayerVehicle | SoundInstanceType.Combat)) != 0)
					GetMotor()!.SetIdealYawToTarget(sound.GetSoundReactOrigin());
			}
		}

		bool highHealth = ((float)GetHealth() / (float)GetMaxHealth() > 0.75f);

		if (!highHealth && !HasBaton()) {
			if (GetActiveWeapon() != null && (GetActiveWeapon()!.Clip1() <= 5)) {
				Sentences.Speak("METROPOLICE_COVER_LOW_AMMO");
				return SCHED_HIDE_AND_RELOAD;
			}
		}

		if (HasCondition((int)SCOND_t.COND_NO_PRIMARY_AMMO)) {
			if (highHealth)
				return SCHED_RELOAD;

			AnnounceOutOfAmmo();
			return SCHED_HIDE_AND_RELOAD;
		}

		if (NPCState == NPCState.Combat && ChasePlayerTime > gpGlobals.CurTime)
			return SCHED_CHASE_ENEMY;

		if (!BehaviorSelectSchedule()) {
			if (KeepFacingPlayer && !PlayerIsCriminal())
				return SCHED_TARGET_FACE;

			switch (NPCState) {
				case NPCState.Idle: {
						sched = SelectScheduleInvestigateSound();
						if (sched != SCHED_NONE)
							return sched;
						break;
					}

				case NPCState.Alert: {
						sched = SelectScheduleInvestigateSound();
						if (sched != SCHED_NONE)
							return sched;
					}
					break;

				case NPCState.Combat:
					if (!IsEnemyInAnAirboat() || Weapon_OwnsThisType("weapon_smg1", 0) == null) {
						int result = SelectCombatSchedule();
						if (result != SCHED_NONE)
							return result;
					}
					else {
						int result = SelectAirboatCombatSchedule();
						if (result != SCHED_NONE)
							return result;
					}
					break;
			}
		}

		if ((NPCState != NPCState.Combat) &&
			 (PreChaseOrigin != vec3_origin) &&
			 (ChasePlayerTime < gpGlobals.CurTime)) {
			return SCHED_METROPOLICE_RETURN_TO_PRECHASE;
		}

		return base.SelectSchedule();
	}

	public override int SelectFailSchedule(int failedSchedule, int failedTask, AI_TaskFailureCode taskFailCode) {
		if (failedSchedule == SCHED_METROPOLICE_CHASE_ENEMY)
			return SCHED_METROPOLICE_ESTABLISH_LINE_OF_FIRE;

		return base.SelectFailSchedule(failedSchedule, failedTask, taskFailCode);
	}

	public override int TranslateSchedule(int scheduleType) {
		switch (scheduleType) {
			case SCHED_ALERT_FACE_BESTSOUND:
				if (!IsCurSchedule(SCHED_METROPOLICE_ALERT_FACE_BESTSOUND, false))
					return SCHED_METROPOLICE_ALERT_FACE_BESTSOUND;
				return SCHED_ALERT_FACE_BESTSOUND;

			case SCHED_CHASE_ENEMY:
				if (!IsRunningBehavior())
					return SCHED_METROPOLICE_CHASE_ENEMY;

				break;

			case SCHED_ESTABLISH_LINE_OF_FIRE:
			case SCHED_METROPOLICE_ESTABLISH_LINE_OF_FIRE:
				if (IsEnemyInAnAirboat()) {
					int sched = SelectMoveToLedgeSchedule();
					if (sched != SCHED_NONE)
						return sched;
				}
				return SCHED_METROPOLICE_ESTABLISH_LINE_OF_FIRE;

			case SCHED_WAKE_ANGRY:
				return SCHED_METROPOLICE_WAKE_ANGRY;

			case SCHED_FAIL_TAKE_COVER:
				if (HasCondition((int)SCOND_t.COND_CAN_RANGE_ATTACK1)) {
					if (TryToEnterPistolSlot(SquadSlot.Attack1) || TryToEnterPistolSlot(SquadSlot.Attack2))
						return SCHED_RANGE_ATTACK1;
				}

				if (HasCondition((int)SCOND_t.COND_NO_PRIMARY_AMMO))
					return SCHED_RELOAD;
				return SCHED_RUN_RANDOM;

			case SCHED_RANGE_ATTACK1:
				Assert(!HasCondition((int)SCOND_t.COND_NO_PRIMARY_AMMO));

				if (!WeaponDrawn)
					return SCHED_METROPOLICE_DRAW_PISTOL;

				if (Weapon_OwnsThisType("weapon_smg1", 0) != null) {
					if (IsEnemyInAnAirboat()) {
						int sched = SelectStitchSchedule();
						if (sched != SCHED_NONE)
							return sched;
					}

					if (ShouldAttemptToStitch())
						return SCHED_METROPOLICE_SMG_BURST_ATTACK;
					else
						return SCHED_METROPOLICE_SMG_NORMAL_ATTACK;
				}
				break;

			case SCHED_METROPOLICE_ADVANCE:
				if (NextChargeTimer.Expired() && metropolice_charge.GetBool()) {
					if (Weapon_OwnsThisType("weapon_pistol", 0) != null) {
						if (GetEnemy() != null && Vector3.DistanceSquared(GetEnemy()!.GetAbsOrigin(), GetAbsOrigin()) > 300 * 300) {
							if (OccupyStrategySlot(SQUAD_SLOT_POLICE_CHARGE_ENEMY)) {
								NextChargeTimer.Set(3, 7);
								return SCHED_METROPOLICE_CHARGE;
							}
						}
					}
					else
						NextChargeTimer.Set(99999);
				}
				break;
		}

		return base.TranslateSchedule(scheduleType);
	}

	public override void StartTask(in Task_t task) {
		switch (task.Task) {
			case TASK_METROPOLICE_WAIT_FOR_SENTENCE: {
					if (FOkToMakeSound((SentencePriority)(int)task.TaskData))
						TaskComplete();
				}
				break;

			case TASK_METROPOLICE_GET_PATH_TO_PRECHASE: {
					Assert(PreChaseOrigin != vec3_origin);
					if (GetNavigator()!.SetGoal(new AI_NavGoal(PreChaseOrigin))) {
						QAngle angles = new(0, PreChaseYaw, 0);
						GetNavigator()!.SetArrivalDirection(angles);
						TaskComplete();
					}
					else
						TaskFail(AI_TaskFailureCode.NoRoute);
					break;
				}

			case TASK_METROPOLICE_CLEAR_PRECHASE: {
					PreChaseOrigin = vec3_origin;
					PreChaseYaw = 0;
					TaskComplete();
					break;
				}

			case TASK_METROPOLICE_ACTIVATE_BATON: {
					if (HasBaton() == false) {
						TaskComplete();
						break;
					}

					bool activate = (task.TaskData != 0);

					if (activate) {
						if (BatonActive() || ShouldActivateBaton == false) {
							TaskComplete();
							break;
						}

						Sentences.Speak("METROPOLICE_ACTIVATE_BATON", SentencePriority.Normal, SentenceCriteria.Normal);
						SetIdealActivity(ACT_ACTIVATE_BATON);
					}
					else {
						if (BatonActive() == false || ShouldActivateBaton) {
							TaskComplete();
							break;
						}

						Sentences.Speak("METROPOLICE_DEACTIVATE_BATON", SentencePriority.Normal, SentenceCriteria.Normal);
						SetIdealActivity(ACT_DEACTIVATE_BATON);
					}
				}
				break;

			case TASK_METROPOLICE_DIE_INSTANTLY: {
					TakeDamageInfo info = new();

					info.SetAttacker(this);
					info.SetInflictor(this);
					info.SetDamage(Health);
					info.SetDamageType((DamageType)(int)task.TaskData);
					info.SetDamageForce(new Vector3(0.1f, 0.1f, 0.1f));

					TakeDamage(info);

					TaskComplete();
				}
				break;

			case TASK_METROPOLICE_RESET_LEDGE_CHECK_TIME:
				NextLedgeCheckTime = gpGlobals.CurTime;
				TaskComplete();
				break;

			case TASK_METROPOLICE_LEAD_ARREST_ENEMY:
			case TASK_METROPOLICE_ARREST_ENEMY:
				TaskCompletionTime = gpGlobals.CurTime + task.TaskData;
				break;

			case TASK_METROPOLICE_SIGNAL_FIRING_TIME:
				EnemyResistingArrest();
				TaskComplete();
				break;

			case TASK_METROPOLICE_GET_PATH_TO_STITCH:
			case TASK_METROPOLICE_AIM_STITCH_TIGHTLY:
			case TASK_METROPOLICE_AIM_STITCH_AT_PLAYER:
			case TASK_METROPOLICE_AIM_STITCH_AT_AIRBOAT:
			case TASK_METROPOLICE_AIM_STITCH_IN_FRONT_OF_AIRBOAT:
			case TASK_METROPOLICE_AIM_STITCH_ALONG_SIDE_OF_AIRBOAT:
			case TASK_METROPOLICE_AIM_STITCH_BEHIND_AIRBOAT:
			case TASK_METROPOLICE_BURST_ATTACK:
			case TASK_METROPOLICE_STOP_FIRE_BURST:
			case TASK_METROPOLICE_RELOAD_FOR_BURST:
				throw new NotImplementedException();

			case TASK_METROPOLICE_HARASS: {
					if ((SpawnFlags & SF_METROPOLICE_NOCHATTER) == 0) {
						if (GetEnemy() != null && GetEnemy()!.GetWaterLevel() > 0)
							EmitSound("NPC_MetroPolice.WaterSpeech");
						else
							EmitSound("NPC_MetroPolice.HidingSpeech");
					}

					TaskComplete();
				}
				break;

			case TASK_RELOAD:
				BurstReloadCount = METROPOLICE_BURST_RELOAD_COUNT;
				base.StartTask(task);
				break;

			case TASK_METROPOLICE_GET_PATH_TO_BESTSOUND_LOS:
				break;

			default:
				base.StartTask(task);
				break;
		}
	}

	void EnemyResistingArrest() {
		if (Squad != null) {
			AI_BaseNPC? squadmate = Squad.GetFirstMember(out int iter);
			while (squadmate != null) {
				squadmate.RemoveSpawnFlags(SF_METROPOLICE_ARREST_ENEMY);
				squadmate.SetCondition(COND_METROPOLICE_ENEMY_RESISTING_ARREST);
				squadmate = Squad.GetNextMember(ref iter);
			}
		}
	}

	const float FLEEING_DISTANCE_SQR = 100 * 100;

	int SquadArrestCount() {
		int count = 0;

		AI_BaseNPC? squadmate = Squad!.GetFirstMember(out int iter);
		while (squadmate != null) {
			if (squadmate.IsCurSchedule(SCHED_METROPOLICE_ARREST_ENEMY) ||
				squadmate.IsCurSchedule(SCHED_METROPOLICE_WARN_AND_ARREST_ENEMY)) {
				++count;
			}

			squadmate = Squad.GetNextMember(ref iter);
		}

		return count;
	}

	public override void RunTask(in Task_t task) {
		switch (task.Task) {
			case TASK_WAIT_FOR_MOVEMENT:
				base.RunTask(task);
				break;

			case TASK_METROPOLICE_WAIT_FOR_SENTENCE: {
					if (FOkToMakeSound((SentencePriority)(int)task.TaskData))
						TaskComplete();
				}
				break;

			case TASK_METROPOLICE_ACTIVATE_BATON:
				AutoMovement();

				if (IsActivityFinished())
					TaskComplete();
				break;

			case TASK_METROPOLICE_BURST_ATTACK:
			case TASK_METROPOLICE_RELOAD_FOR_BURST:
			case TASK_METROPOLICE_GET_PATH_TO_BESTSOUND_LOS:
				throw new NotImplementedException();

			case TASK_METROPOLICE_LEAD_ARREST_ENEMY:
			case TASK_METROPOLICE_ARREST_ENEMY: {
					if (GetEnemy() == null) {
						TaskComplete();
						break;
					}

					if (gpGlobals.CurTime >= TaskCompletionTime) {
						TaskComplete();
						break;
					}

					if (task.Task == TASK_METROPOLICE_LEAD_ARREST_ENEMY) {
						int arrestCount = SquadArrestCount();
						if (arrestCount == Squad!.NumMembers()) {
							TaskComplete();
							break;
						}

						if (Vector3.DistanceSquared(SavePosition, GetEnemy()!.GetAbsOrigin()) > FLEEING_DISTANCE_SQR) {
							SpeakSentence(METROPOLICE_SENTENCE_HES_RUNNING);
							EnemyResistingArrest();
							break;
						}
					}

					if (GetEnemy() != null && FacingIdeal()) {
						float newIdealYaw = CalcIdealYaw(GetEnemy()!.EyePosition());
						if (MathF.Abs(Util.AngleDiff(GetMotor()!.GetIdealYaw(), newIdealYaw)) >= 45.0f) {
							GetMotor()!.SetIdealYawToTarget(GetEnemy()!.EyePosition());
							SetTurnActivity();
						}
					}
					GetMotor()!.UpdateYaw();
				}
				break;

			default:
				base.RunTask(task);
				break;
		}
	}

	bool CanDeployManhack() {
		if (HasSpawnFlags(SF_METROPOLICE_NO_MANHACK_DEPLOY))
			return false;

		if (Manhack.Get() != null)
			return false;

		if (Manhacks < 1)
			return false;

		return true;
	}

	public override void BuildScheduleTestBits() {
		base.BuildScheduleTestBits();

		if (PlayerIsCriminal() == false)
			SetCustomInterruptCondition(COND_METROPOLICE_PHYSOBJECT_ASSAULT);

		if (!IsInAScript() &&
			 !IsCurSchedule(SCHED_METROPOLICE_SHOVE) &&
			 !IsCurSchedule(SCHED_MELEE_ATTACK1) &&
			 !IsCurSchedule(SCHED_RELOAD) &&
			 !IsCurSchedule(SCHED_METROPOLICE_ACTIVATE_BATON)) {
			SetCustomInterruptCondition(COND_METROPOLICE_PLAYER_TOO_CLOSE);
		}

		if (!IsCurSchedule(SCHED_METROPOLICE_BURNING_RUN) && !IsCurSchedule(SCHED_METROPOLICE_BURNING_STAND) && !IsMoving())
			SetCustomInterruptCondition(COND_METROPOLICE_ON_FIRE);

		if (IsCurSchedule(SCHED_TAKE_COVER_FROM_ENEMY)) {
			ClearCustomInterruptCondition((int)SCOND_t.COND_LIGHT_DAMAGE);
			ClearCustomInterruptCondition((int)SCOND_t.COND_HEAVY_DAMAGE);
		}

		if (!IsCurSchedule(SCHED_CHASE_ENEMY) &&
			 !IsCurSchedule(SCHED_METROPOLICE_ACTIVATE_BATON) &&
			 !IsCurSchedule(SCHED_METROPOLICE_DEACTIVATE_BATON) &&
			 !IsCurSchedule(SCHED_METROPOLICE_SHOVE) &&
			 !IsCurSchedule(SCHED_METROPOLICE_RETURN_TO_PRECHASE)) {
			SetCustomInterruptCondition(COND_METROPOLICE_CHANGE_BATON_STATE);
		}

		if (IsCurSchedule(SCHED_MELEE_ATTACK1)) {
			if (gpGlobals.CurTime - LastDamageFlinchTime < 10.0) {
				ClearCustomInterruptCondition((int)SCOND_t.COND_LIGHT_DAMAGE);
				ClearCustomInterruptCondition((int)SCOND_t.COND_HEAVY_DAMAGE);
			}
		}
		else if (HasBaton() && IsCurSchedule(SCHED_COMBAT_FACE) && !BatonSwingTimer.Expired())
			ClearCustomInterruptCondition((int)SCOND_t.COND_CAN_MELEE_ATTACK1);
	}

	public override void GatherConditions() {
		base.GatherConditions();

		if (PlayerTooClose == false)
			ClearCondition(COND_METROPOLICE_PLAYER_TOO_CLOSE);

		BasePlayer? player = AI_GetClosestPlayer();

		if (player == null)
			return;

		float distToPlayerSqr = (player.GetAbsOrigin() - GetAbsOrigin()).LengthSquared();

		if (player.GetGroundEntity() == this) {
			NumPlayerHits = 0;
			SetCondition(COND_METROPOLICE_PLAYER_TOO_CLOSE);
		}
		else if (distToPlayerSqr < (42.0f * 42.0f) && FVisible(player)) {
			if (NumPlayerHits < 3 || PreChaseOrigin == vec3_origin)
				SetCondition(COND_METROPOLICE_PLAYER_TOO_CLOSE);
		}
		else {
			ClearCondition(COND_METROPOLICE_PLAYER_TOO_CLOSE);

			if ((gpGlobals.CurTime - GetLastAttackTime()) > 3)
				NumPlayerHits = 0;

			PlayerTooClose = false;
		}

		if (metropolice_move_and_melee.GetBool()) {
			if (IsMoving() && HasCondition((int)SCOND_t.COND_CAN_MELEE_ATTACK1) && HasBaton()) {
				if (BatonSwingTimer.Expired()) {
					BatonSwingTimer.Set(1.0f, 1.75f);

					Activity activity = TranslateActivity(Activity.ACT_MELEE_ATTACK_SWING_GESTURE);
					Assert(activity != Activity.ACT_INVALID);
					AddGesture(activity);
				}
			}
		}
	}

	bool HasBaton() {
		BaseCombatWeapon? weapon = GetActiveWeapon();

		if (weapon != null)
			return FClassnameIs(weapon, "weapon_stunstick");

		return false;
	}

	public bool BatonActive() {
		if (GetActiveWeapon() is WeaponStunStick stick)
			return stick.GetStunState();

		return false;
	}

	public void SetBatonState(bool state) {
		if (!HasBaton())
			return;

		if (ShouldActivateBaton != state) {
			ShouldActivateBaton = state;
			SetCondition(COND_METROPOLICE_CHANGE_BATON_STATE);
		}
	}

	public override bool QueryHearSound(ref WorldSoundInstance sound) {
		if (PlayerIsCriminal() == false) {
			if (sound.IsSoundType(SoundInstanceType.Danger) && sound.Owner.Get() != null && IRelationType(sound.Owner.Get()) == Disposition.NU)
				return false;
		}

		return base.QueryHearSound(ref sound);
	}

	public void StunnedTarget(BaseEntity? target) {
		SetLastAttackTime(gpGlobals.CurTime);

		if (target != null && target.IsPlayer()) {
			OnStunnedPlayer.FireOutput(this, this);
			NumPlayerHits++;
		}
	}

	void PrecriminalUse(BaseEntity? activator, BaseEntity? caller, UseType useType, float value) {
		if (IsInAScript())
			return;

		if (IRelationType(activator) == Disposition.HT || ((GetState() != NPCState.Alert) && (GetState() != NPCState.Idle)))
			return;
		if (PlayerIsCriminal())
			return;

		IncrementPlayerCriminalStatus();

		if (NumWarnings == METROPOLICE_MAX_WARNINGS)
			AdministerJustice();
	}

	public AI_Sentence_NPC_MetroPolice GetSentences() => Sentences;

}

public struct TraceFilterMetroPolice(IHandleEntity? passentity, CollisionGroup collisionGroup, TakeDamageInfo dmgInfo, float forceScale, bool damageAnyNPC) : ITraceFilter
{
	public IHandleEntity? PassEnt = passentity;
	public CollisionGroup CollisionGroup = collisionGroup;
	public TakeDamageInfo DmgInfo = dmgInfo;
	public BaseEntity? Hit = null;
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

			if (entity.GetServerVehicle() != null) {
				BaseEntity? driver = entity.GetServerVehicle()!.GetPassenger();

				if (driver != null)
					entity = driver;
			}

			Vector3 attackDir = entity.WorldSpaceCenter() - DmgInfo.GetAttacker()!.WorldSpaceCenter();
			MathLib.VectorNormalize(ref attackDir);

			TakeDamageInfo info = DmgInfo;
			CalculateMeleeDamageForce(ref info, attackDir, info.GetAttacker()!.WorldSpaceCenter(), ForceScale);

			if ((entity.GetFlags() & EntityFlags.OnGround) == 0)
				info.ScaleDamageForce(0.001f);

			BaseCombatCharacter? bcc = ToBaseCombatCharacter(info.GetAttacker());
			BaseCombatCharacter? victimBCC = ToBaseCombatCharacter(entity);

			if (bcc != null && victimBCC != null) {
				if (DamageAnyNPC || bcc.IRelationType(entity) == Disposition.HT || entity.IsPlayer()) {
					if (info.GetDamage() != 0) {
						if (!entity.IsPlayer() || GlobalEntity.GetState("gordon_precriminal") == GlobalEState.Off) {
							if (entity.IsPlayer() && ((BasePlayer)entity).IsSuitEquipped()) {
								info.ScaleDamage(.25f);
								info.ScaleDamageForce(.25f);
							}

							entity.TakeDamage(info);
						}
					}

					Hit = entity;
					return true;
				}
			}
			else {
				Pickup.ForcePlayerToDropThisObject(entity);

				if (info.GetDamage() != 0)
					entity.TakeDamage(info);
			}
		}

		return false;
	}

	public readonly TraceType GetTraceType() => TraceType.EntitiesOnly;
}
