using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Numerics;

using static Game.Server.SCOND_t;

namespace Game.Server;

using DEFINE = DEFINE<NPC_Citizen>;

public enum CitizenType
{
	Default,
	Downtrodden,
	Refugee,
	Rebel,
	Unique
}

public enum CitizenExpressionType
{
	Unassigned,

	Scared,
	Normal,
	Angry,

	LastType,
}

[LinkEntityToClass("npc_citizen")]
public class NPC_Citizen : NPC_PlayerCompanion
{
	public const int SF_CITIZEN_FOLLOW = 1 << 16;
	public const int SF_CITIZEN_MEDIC = 1 << 17;
	public const int SF_CITIZEN_RANDOM_HEAD = 1 << 18;
	public const int SF_CITIZEN_AMMORESUPPLIER = 1 << 19;
	public const int SF_CITIZEN_NOT_COMMANDABLE = 1 << 20;
	public const int SF_CITIZEN_IGNORE_SEMAPHORE = 1 << 21;
	public const int SF_CITIZEN_RANDOM_HEAD_MALE = 1 << 22;
	public const int SF_CITIZEN_RANDOM_HEAD_FEMALE = 1 << 23;
	public const int SF_CITIZEN_USE_RENDER_BOUNDS = 1 << 24;

	const int CIT_INSPECTED_DELAY_TIME = 120;

	const float SHOTGUN_DEFER_SEARCH_TIME = 20.0f;
	const float OTHER_DEFER_SEARCH_TIME = float.MaxValue;

	const int STATES_WITH_EXPRESSIONS = 3;

	public static readonly ConVar sk_citizen_health = new("sk_citizen_health", "0");
	public static readonly ConVar npc_citizen_dont_precache_all = new("npc_citizen_dont_precache_all", "1");

	static readonly string[][] ScaredExpressions = [
		["scenes/Expressions/citizen_scared_idle_01.vcd"],
		["scenes/Expressions/citizen_scared_alert_01.vcd"],
		["scenes/Expressions/citizen_scared_combat_01.vcd"],
	];

	static readonly string[][] NormalExpressions = [
		["scenes/Expressions/citizen_normal_idle_01.vcd"],
		["scenes/Expressions/citizen_normal_alert_01.vcd"],
		["scenes/Expressions/citizen_normal_combat_01.vcd"],
	];

	static readonly string[][] AngryExpressions = [
		["scenes/Expressions/citizen_angry_idle_01.vcd"],
		["scenes/Expressions/citizen_angry_alert_01.vcd"],
		["scenes/Expressions/citizen_angry_combat_01.vcd"],
	];

	static readonly string[] RandomHeads = [
		"male_01.mdl",
		"male_02.mdl",
		"female_01.mdl",
		"male_03.mdl",
		"female_02.mdl",
		"male_04.mdl",
		"female_03.mdl",
		"male_05.mdl",
		"female_04.mdl",
		"male_06.mdl",
		"female_06.mdl",
		"male_07.mdl",
		"female_07.mdl",
		"male_08.mdl",
		"male_09.mdl",
	];

	static readonly string[] ModelLocs = [
		"Group01",
		"Group01",
		"Group02",
		"Group03{0}",
	];

	public static Activity ACT_CIT_HANDSUP;
	public static Activity ACT_CIT_BLINDED;
	public static Activity ACT_CIT_SHOWARMBAND;
	public static Activity ACT_CIT_HEAL;
	public static Activity ACT_CIT_STARTLED;

	public static AnimEventId AE_CITIZEN_GET_PACKAGE;
	public static AnimEventId AE_CITIZEN_HEAL;

	const int COND_CIT_PLAYERHEALREQUEST = NPC_PlayerCompanion.NEXT_CONDITION;
	const int COND_CIT_COMMANDHEAL = NPC_PlayerCompanion.NEXT_CONDITION + 1;
	const int COND_CIT_HURTBYFIRE = NPC_PlayerCompanion.NEXT_CONDITION + 2;
	const int COND_CIT_START_INSPECTION = NPC_PlayerCompanion.NEXT_CONDITION + 3;

	const int SCHED_CITIZEN_PLAY_INSPECT_ACTIVITY = NPC_PlayerCompanion.NEXT_SCHEDULE;
	const int SCHED_CITIZEN_HEAL = NPC_PlayerCompanion.NEXT_SCHEDULE + 1;
	const int SCHED_CITIZEN_RANGE_ATTACK1_RPG = NPC_PlayerCompanion.NEXT_SCHEDULE + 2;
	const int SCHED_CITIZEN_PATROL = NPC_PlayerCompanion.NEXT_SCHEDULE + 3;
	const int SCHED_CITIZEN_MOURN_PLAYER = NPC_PlayerCompanion.NEXT_SCHEDULE + 4;
	const int SCHED_CITIZEN_SIT_ON_TRAIN = NPC_PlayerCompanion.NEXT_SCHEDULE + 5;
	const int SCHED_CITIZEN_STRIDER_RANGE_ATTACK1_RPG = NPC_PlayerCompanion.NEXT_SCHEDULE + 6;
	const int SCHED_CITIZEN_HEAL_TOSS = NPC_PlayerCompanion.NEXT_SCHEDULE + 7;

	const int TASK_CIT_HEAL = NPC_PlayerCompanion.NEXT_TASK;
	const int TASK_CIT_RPG_AUGER = NPC_PlayerCompanion.NEXT_TASK + 1;
	const int TASK_CIT_PLAY_INSPECT_SEQUENCE = NPC_PlayerCompanion.NEXT_TASK + 2;
	const int TASK_CIT_SIT_ON_TRAIN = NPC_PlayerCompanion.NEXT_TASK + 3;
	const int TASK_CIT_LEAVE_TRAIN = NPC_PlayerCompanion.NEXT_TASK + 4;
	const int TASK_CIT_SPEAK_MOURNING = NPC_PlayerCompanion.NEXT_TASK + 5;
	const int TASK_CIT_HEAL_TOSS = NPC_PlayerCompanion.NEXT_TASK + 6;

	static readonly AI_ClassScheduleIdSpace gm_ClassScheduleIdSpace = new();

	public static readonly new DataMap DataDesc = new(typeof(NPC_Citizen), NPC_PlayerCompanion.DataDesc, [
		DEFINE.KEYFIELD(nameof(Type), FieldType.Integer, "citizentype"),
		DEFINE.KEYFIELD(nameof(ExpressionType), FieldType.Integer, "expressiontype"),

		DEFINE.INPUTFUNC(FieldType.Void, "StartPatrolling", nameof(InputStartPatrolling), (INPUTFUNCPTR)((self, data) => ((NPC_Citizen)self).InputStartPatrolling(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "StopPatrolling", nameof(InputStopPatrolling), (INPUTFUNCPTR)((self, data) => ((NPC_Citizen)self).InputStopPatrolling(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	Activity InspectActivity;
	TimeUnit_t NextInspectTime;
	bool ShouldPatrol;

	CitizenType Type;
	CitizenExpressionType ExpressionType;

	int Head = -1;

	public NPC_Citizen() => LoadSchedules();

	public override AI_ClassScheduleIdSpace GetClassScheduleIdSpace() => gm_ClassScheduleIdSpace;
	public override ReadOnlySpan<char> GetSchedulingErrorName() => nameof(NPC_Citizen);

	public static new bool LoadSchedules() => CustomSchedules.Load(NPC_PlayerCompanion.LoadSchedules, nameof(NPC_Citizen));

	public override bool LoadedSchedules() => CustomSchedules.Valid;

	static readonly AI_CustomSchedules CustomSchedules = new() {
		IdSpace = gm_ClassScheduleIdSpace,
		ParentIdSpace = NPC_PlayerCompanion.ClassScheduleIdSpace,
		Schedules = [
			(nameof(SCHED_CITIZEN_PLAY_INSPECT_ACTIVITY), SCHED_CITIZEN_PLAY_INSPECT_ACTIVITY),
			(nameof(SCHED_CITIZEN_HEAL), SCHED_CITIZEN_HEAL),
			(nameof(SCHED_CITIZEN_RANGE_ATTACK1_RPG), SCHED_CITIZEN_RANGE_ATTACK1_RPG),
			(nameof(SCHED_CITIZEN_PATROL), SCHED_CITIZEN_PATROL),
			(nameof(SCHED_CITIZEN_MOURN_PLAYER), SCHED_CITIZEN_MOURN_PLAYER),
			(nameof(SCHED_CITIZEN_SIT_ON_TRAIN), SCHED_CITIZEN_SIT_ON_TRAIN),
			(nameof(SCHED_CITIZEN_STRIDER_RANGE_ATTACK1_RPG), SCHED_CITIZEN_STRIDER_RANGE_ATTACK1_RPG),
			(nameof(SCHED_CITIZEN_HEAL_TOSS), SCHED_CITIZEN_HEAL_TOSS),
		],
		Tasks = [
			(nameof(TASK_CIT_HEAL), TASK_CIT_HEAL),
			(nameof(TASK_CIT_RPG_AUGER), TASK_CIT_RPG_AUGER),
			(nameof(TASK_CIT_PLAY_INSPECT_SEQUENCE), TASK_CIT_PLAY_INSPECT_SEQUENCE),
			(nameof(TASK_CIT_SIT_ON_TRAIN), TASK_CIT_SIT_ON_TRAIN),
			(nameof(TASK_CIT_LEAVE_TRAIN), TASK_CIT_LEAVE_TRAIN),
			(nameof(TASK_CIT_SPEAK_MOURNING), TASK_CIT_SPEAK_MOURNING),
			(nameof(TASK_CIT_HEAL_TOSS), TASK_CIT_HEAL_TOSS),
		],
		Conditions = [
			(nameof(COND_CIT_PLAYERHEALREQUEST), COND_CIT_PLAYERHEALREQUEST),
			(nameof(COND_CIT_COMMANDHEAL), COND_CIT_COMMANDHEAL),
			(nameof(COND_CIT_START_INSPECTION), COND_CIT_START_INSPECTION),
		],
		Declare = static () => {
			ACT_CIT_HANDSUP = DeclareActivity(nameof(ACT_CIT_HANDSUP));
			ACT_CIT_BLINDED = DeclareActivity(nameof(ACT_CIT_BLINDED));
			ACT_CIT_SHOWARMBAND = DeclareActivity(nameof(ACT_CIT_SHOWARMBAND));
			ACT_CIT_HEAL = DeclareActivity(nameof(ACT_CIT_HEAL));
			ACT_CIT_STARTLED = DeclareActivity(nameof(ACT_CIT_STARTLED));
			AE_CITIZEN_GET_PACKAGE = DeclareAnimEvent(nameof(AE_CITIZEN_GET_PACKAGE));
			AE_CITIZEN_HEAL = DeclareAnimEvent(nameof(AE_CITIZEN_HEAL));
		},
		Definitions = [
			new(SCHED_CITIZEN_HEAL,
				[
					TASK_GET_PATH_TO_TARGET,
					(TASK_MOVE_TO_TARGET_RANGE, 50),
					TASK_STOP_MOVING,
					TASK_FACE_IDEAL,
					TASK_CIT_HEAL,
				]),

			new(SCHED_CITIZEN_HEAL_TOSS,
				[
					TASK_STOP_MOVING,
					TASK_FACE_IDEAL,
					TASK_CIT_HEAL_TOSS,
				]),

			new(SCHED_CITIZEN_RANGE_ATTACK1_RPG,
				[
					TASK_STOP_MOVING,
					TASK_FACE_ENEMY,
					(TASK_ANNOUNCE_ATTACK, 1),
					TASK_RANGE_ATTACK1,
					(TASK_CIT_RPG_AUGER, 1),
				]),

			new(SCHED_CITIZEN_STRIDER_RANGE_ATTACK1_RPG,
				[
					TASK_STOP_MOVING,
					TASK_FACE_ENEMY,
					(TASK_ANNOUNCE_ATTACK, 1),
					(TASK_WAIT, 1),
					TASK_RANGE_ATTACK1,
					(TASK_CIT_RPG_AUGER, 1),
					(TASK_SET_SCHEDULE, SCHED_TAKE_COVER_FROM_ENEMY),
				]),

			new(SCHED_CITIZEN_PATROL,
				[
					TASK_STOP_MOVING,
					(TASK_WANDER, 901024),
					TASK_WALK_PATH,
					TASK_WAIT_FOR_MOVEMENT,
					TASK_STOP_MOVING,
					(TASK_WAIT, 3),
					(TASK_WAIT_RANDOM, 3),
					(TASK_SET_SCHEDULE, SCHED_CITIZEN_PATROL),
				],
				[
					COND_ENEMY_DEAD,
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_HEAR_DANGER,
					COND_NEW_ENEMY,
				]),

			new(SCHED_CITIZEN_MOURN_PLAYER,
				[
					TASK_GET_PATH_TO_PLAYER,
					(TASK_RUN_PATH_WITHIN_DIST, 180),
					TASK_WAIT_FOR_MOVEMENT,
					TASK_STOP_MOVING,
					TASK_TARGET_PLAYER,
					TASK_FACE_TARGET,
					TASK_CIT_SPEAK_MOURNING,
					(TASK_SUGGEST_STATE, NPCState.Idle),
				],
				[
					COND_LIGHT_DAMAGE,
					COND_HEAVY_DAMAGE,
					COND_HEAR_DANGER,
					COND_NEW_ENEMY,
				]),

			new(SCHED_CITIZEN_PLAY_INSPECT_ACTIVITY,
				[
					TASK_STOP_MOVING,
					TASK_CIT_PLAY_INSPECT_SEQUENCE,
					(TASK_WAIT, 2),
				]),

			new(SCHED_CITIZEN_SIT_ON_TRAIN,
				[
					TASK_CIT_SIT_ON_TRAIN,
					(TASK_WAIT_RANDOM, 1),
					TASK_CIT_LEAVE_TRAIN,
				]),
		],
	};

	public bool IsMedic() => HasSpawnFlags(SF_CITIZEN_MEDIC);
	public bool IsAmmoResupplier() => HasSpawnFlags(SF_CITIZEN_AMMORESUPPLIER);

	public override void Precache() {
		SelectModel();
		SelectExpressionType();

		if (!npc_citizen_dont_precache_all.GetBool())
			PrecacheAllOfType(Type);
		else
			PrecacheModel(GetModelName());

		PrecacheScriptSound("NPC_Citizen.FootstepLeft");
		PrecacheScriptSound("NPC_Citizen.FootstepRight");
		PrecacheScriptSound("NPC_Citizen.Die");

		PrecacheInstancedScene("scenes/Expressions/CitizenIdle.vcd");
		PrecacheInstancedScene("scenes/Expressions/CitizenAlert_loop.vcd");
		PrecacheInstancedScene("scenes/Expressions/CitizenCombat_loop.vcd");

		for (int i = 0; i < STATES_WITH_EXPRESSIONS; i++) {
			for (int j = 0; j < ScaredExpressions[i].Length; j++)
				PrecacheInstancedScene(ScaredExpressions[i][j]);
			for (int j = 0; j < NormalExpressions[i].Length; j++)
				PrecacheInstancedScene(NormalExpressions[i][j]);
			for (int j = 0; j < AngryExpressions[i].Length; j++)
				PrecacheInstancedScene(AngryExpressions[i][j]);
		}

		base.Precache();
	}

	void PrecacheAllOfType(CitizenType type) {
		if (Type == CitizenType.Unique)
			return;

		for (int i = 0; i < RandomHeads.Length; ++i)
			PrecacheModel($"models/Humans/{string.Format(ModelLocs[(int)Type], "")}/{RandomHeads[i]}");

		if (Type == CitizenType.Rebel) {
			for (int i = 0; i < RandomHeads.Length; ++i)
				PrecacheModel($"models/Humans/{string.Format(ModelLocs[(int)Type], "m")}/{RandomHeads[i]}");
		}
	}

	public override void Spawn() {
		base.Spawn();

		if (IsAmmoResupplier())
			Skin = 2;

		ShouldPatrol = false;
		Health = (int)sk_citizen_health.GetFloat();

		if (GetMoveParent() != null && FClassnameIs(GetMoveParent(), "func_tracktrain")) {
			CapabilitiesRemove(Capability.MoveGround);
			SetMoveType(Source.MoveType.None);
			if (NameMatches("citizen_train_2")) {
				SetSequenceByName("d1_t01_TrainRide_Sit_Idle");
				SetIdealActivity(Activity.ACT_DO_NOT_DISTURB);
			}
			else {
				SetSequenceByName("d1_t01_TrainRide_Stand");
				SetIdealActivity(Activity.ACT_DO_NOT_DISTURB);
			}
		}

		IdleExpression = "scenes/expressions/citizenidle.vcd";
		AlertExpression = "scenes/expressions/citizenalert_loop.vcd";
		CombatExpression = "scenes/expressions/citizencombat_loop.vcd";

		AddEFlags(EFL.NoDissolve | EFL.NoMegaPhysCannonRagdoll | EFL.NoPhysCannonInteraction);

		RemoveEFlags(EFL.NoDissolve);

		NPCInit();

		ActBusyBehavior.SetUseRenderBounds(HasSpawnFlags(SF_CITIZEN_USE_RENDER_BOUNDS));
	}

	public override void SelectModel() {
		string? modelName = null;

		if (Type == CitizenType.Default) {
			ReadOnlySpan<char> mapName = gpGlobals.MapName;

			if (mapName.Contains("trainstation", StringComparison.OrdinalIgnoreCase))
				Type = CitizenType.Downtrodden;
			else if (mapName.Contains("canals", StringComparison.OrdinalIgnoreCase))
				Type = CitizenType.Refugee;
			else if (mapName.Contains("town", StringComparison.OrdinalIgnoreCase))
				Type = CitizenType.Refugee;
			else if (mapName.Contains("coast", StringComparison.OrdinalIgnoreCase))
				Type = CitizenType.Refugee;
			else if (mapName.Contains("prison", StringComparison.OrdinalIgnoreCase))
				Type = CitizenType.Downtrodden;
			else if (mapName.Contains("c17", StringComparison.OrdinalIgnoreCase))
				Type = CitizenType.Rebel;
			else if (mapName.Contains("citadel", StringComparison.OrdinalIgnoreCase))
				Type = CitizenType.Downtrodden;

			if (Type == CitizenType.Default)
				Type = CitizenType.Downtrodden;
		}

		if (HasSpawnFlags(SF_CITIZEN_RANDOM_HEAD | SF_CITIZEN_RANDOM_HEAD_MALE | SF_CITIZEN_RANDOM_HEAD_FEMALE) || GetModelName().IsEmpty) {
			Assert(Head == -1);
			char gender = HasSpawnFlags(SF_CITIZEN_RANDOM_HEAD_MALE) ? 'm' :
						  HasSpawnFlags(SF_CITIZEN_RANDOM_HEAD_FEMALE) ? 'f' : '\0';

			RemoveSpawnFlags(SF_CITIZEN_RANDOM_HEAD | SF_CITIZEN_RANDOM_HEAD_MALE | SF_CITIZEN_RANDOM_HEAD_FEMALE);
			if (HasSpawnFlags(SF_NPC_START_EFFICIENT)) {
				SetModelName("models/humans/male_cheaple.mdl");
				return;
			}
			else {
				Span<int> headCounts = stackalloc int[RandomHeads.Length];
				int i;

				List<AI_BaseNPC> ais = g_AI_Manager.AccessAIs();
				for (i = 0; i < g_AI_Manager.NumAIs(); i++) {
					if (ais[i] is NPC_Citizen citizen && citizen != this && citizen.Head >= 0 && citizen.Head < RandomHeads.Length)
						headCounts[citizen.Head]++;
				}

				Span<(int Head, int Heads)> candidates = stackalloc (int, int)[RandomHeads.Length];
				int numCandidates = 0;

				for (i = 0; i < RandomHeads.Length; i++) {
					if (gender == '\0' || RandomHeads[i][0] == gender)
						candidates[numCandidates++] = (i, headCounts[i]);
				}

				Assert(numCandidates != 0);
				candidates[..numCandidates].Sort(static (left, right) => left.Heads - right.Heads);

				int smallestCount = candidates[0].Heads;
				int limit;

				for (limit = 0; limit < numCandidates; limit++) {
					if (candidates[limit].Heads > smallestCount)
						break;
				}

				Head = candidates[random.RandomInt(0, limit - 1)].Head;
				modelName = RandomHeads[Head];
				SetModelName(default(ReadOnlySpan<char>));
			}
		}

		Assert(modelName != null || !GetModelName().IsEmpty);

		if (modelName == null) {
			if (GetModelName().IsEmpty)
				return;

			ReadOnlySpan<char> currentModel = GetModelName();
			int slash = currentModel.LastIndexOf('/');
			if (slash == -1)
				modelName = currentModel.ToString();
			else {
				modelName = currentModel[(slash + 1)..].ToString();
				if (Head == -1) {
					for (int i = 0; i < RandomHeads.Length; i++) {
						if (string.Equals(RandomHeads[i], modelName, StringComparison.OrdinalIgnoreCase)) {
							Head = i;
							break;
						}
					}
				}
			}
			if (modelName.Length == 0)
				return;
		}

		if (Type != CitizenType.Unique)
			SetModelName($"models/Humans/{string.Format(ModelLocs[(int)Type], IsMedic() ? "m" : "")}/{modelName}");
	}

	void SelectExpressionType() {
		if (ExpressionType != CitizenExpressionType.Unassigned)
			return;

		switch (Type) {
			case CitizenType.Downtrodden:
				ExpressionType = (CitizenExpressionType)random.RandomInt((int)CitizenExpressionType.Scared, (int)CitizenExpressionType.Normal);
				break;
			case CitizenType.Refugee:
				ExpressionType = (CitizenExpressionType)random.RandomInt((int)CitizenExpressionType.Scared, (int)CitizenExpressionType.Normal);
				break;
			case CitizenType.Rebel:
				ExpressionType = (CitizenExpressionType)random.RandomInt((int)CitizenExpressionType.Scared, (int)CitizenExpressionType.Angry);
				break;

			case CitizenType.Default:
			case CitizenType.Unique:
			default:
				break;
		}
	}

	public override Class_T Classify() {
		if (GlobalEntity.GetState("gordon_precriminal") == GlobalEState.On)
			return Class_T.CitizenPassive;

		if (GlobalEntity.GetState("citizens_passive") == GlobalEState.On)
			return Class_T.CitizenPassive;

		return Class_T.PlayerAlly;
	}

	public override void BuildScheduleTestBits() {
		base.BuildScheduleTestBits();

		if (IsCurSchedule(SCHED_IDLE_STAND) || IsCurSchedule(SCHED_ALERT_STAND))
			SetCustomInterruptCondition(COND_CIT_START_INSPECTION);

		if (!IsCurSchedule(SCHED_NEW_WEAPON))
			SetCustomInterruptCondition((int)COND_RECEIVED_ORDERS);

		if (GetCurSchedule()!.HasInterrupt((int)COND_IDLE_INTERRUPT))
			SetCustomInterruptCondition((int)COND_BETTER_WEAPON_AVAILABLE);
	}

	public override int SelectSchedule() {
		if (GetMoveType() == Source.MoveType.None) {
			Assert(GetMoveParent() != null && FClassnameIs(GetMoveParent(), "func_tracktrain"));
			return SCHED_CITIZEN_SIT_ON_TRAIN;
		}

		return base.SelectSchedule();
	}

	public override int SelectScheduleNonCombat() {
		if (NPCState == NPCState.Idle) {
			if (HasCondition(COND_CIT_START_INSPECTION)) {
				ClearCondition(COND_CIT_START_INSPECTION);
				return SCHED_CITIZEN_PLAY_INSPECT_ACTIVITY;
			}
		}

		ClearCondition(COND_CIT_START_INSPECTION);

		if (ShouldPatrol)
			return SCHED_CITIZEN_PATROL;

		return SCHED_NONE;
	}

	public override int TranslateSchedule(int scheduleType) {
		switch (scheduleType) {
			case SCHED_IDLE_STAND:
			case SCHED_ALERT_STAND: {
					BasePlayer? localPlayer = AI_GetClosestPlayer();
					if (NPCState != NPCState.Combat && localPlayer != null && !localPlayer.IsAlive() && CanJoinPlayerSquad()) {
						float dist = (localPlayer.GetAbsOrigin() - GetAbsOrigin()).Length();

						if (dist < 50 * 12) {
							AddSpawnFlags(SF_CITIZEN_NOT_COMMANDABLE);
							return SCHED_CITIZEN_MOURN_PLAYER;
						}
					}
				}
				break;
		}

		return base.TranslateSchedule(scheduleType);
	}

	public override void StartTask(in Task_t task) {
		switch (task.Task) {
			case TASK_CIT_PLAY_INSPECT_SEQUENCE:
				SetIdealActivity(InspectActivity);
				break;

			case TASK_CIT_SIT_ON_TRAIN:
				if (NameMatches("citizen_train_2")) {
					SetSequenceByName("d1_t01_TrainRide_Sit_Idle");
					SetIdealActivity(Activity.ACT_DO_NOT_DISTURB);
				}
				else {
					SetSequenceByName("d1_t01_TrainRide_Stand");
					SetIdealActivity(Activity.ACT_DO_NOT_DISTURB);
				}
				break;

			case TASK_CIT_LEAVE_TRAIN:
				if (NameMatches("citizen_train_2")) {
					SetSequenceByName("d1_t01_TrainRide_Sit_Exit");
					SetIdealActivity(Activity.ACT_DO_NOT_DISTURB);
				}
				else {
					SetSequenceByName("d1_t01_TrainRide_Stand_Exit");
					SetIdealActivity(Activity.ACT_DO_NOT_DISTURB);
				}
				break;

			case TASK_CIT_SPEAK_MOURNING:
				TaskComplete();
				break;

			default:
				base.StartTask(task);
				break;
		}
	}

	public override void RunTask(in Task_t task) {
		switch (task.Task) {
			case TASK_CIT_PLAY_INSPECT_SEQUENCE:
				AutoMovement();

				if (IsSequenceFinished())
					TaskComplete();
				break;

			case TASK_CIT_SIT_ON_TRAIN:
				if (GetMoveParent() == null) {
					SetMoveType(Source.MoveType.Step);
					CapabilitiesAdd(Capability.MoveGround);
					TaskComplete();
				}
				break;

			case TASK_CIT_LEAVE_TRAIN:
				if (IsSequenceFinished()) {
					SetupVPhysicsHull();
					TaskComplete();
				}
				break;

			default:
				base.RunTask(task);
				break;
		}
	}

	public override void HandleAnimEvent(ref AnimEvent animEvent) {
		if (animEvent.Event == (int)AE_CITIZEN_GET_PACKAGE) {
			BaseCombatWeapon? weapon = Weapon_Create("weapon_citizenpackage");
			if (weapon != null) {
				if (GetEntityName() != null)
					weapon.SetName($"{GetEntityName()}_weapon");
				Weapon_Equip(weapon);
			}
			return;
		}

		switch (animEvent.Event) {
			case NPC_EVENT_LEFTFOOT:
				EmitSound("NPC_Citizen.FootstepLeft", animEvent.EventTime);
				break;

			case NPC_EVENT_RIGHTFOOT:
				EmitSound("NPC_Citizen.FootstepRight", animEvent.EventTime);
				break;

			default:
				base.HandleAnimEvent(ref animEvent);
				break;
		}
	}

	public override string? SelectRandomExpressionForState(NPCState state) {
		int expressionState;
		switch (state) {
			case NPCState.Idle:
				expressionState = 0;
				break;

			case NPCState.Alert:
				expressionState = 1;
				break;

			case NPCState.Combat:
				expressionState = 2;
				break;

			default:
				return null;
		}

		switch (ExpressionType) {
			case CitizenExpressionType.Scared: {
					int randomIndex = random.RandomInt(0, ScaredExpressions[expressionState].Length - 1);
					return ScaredExpressions[expressionState][randomIndex];
				}

			case CitizenExpressionType.Normal: {
					int randomIndex = random.RandomInt(0, NormalExpressions[expressionState].Length - 1);
					return NormalExpressions[expressionState][randomIndex];
				}

			case CitizenExpressionType.Angry: {
					int randomIndex = random.RandomInt(0, AngryExpressions[expressionState].Length - 1);
					return AngryExpressions[expressionState][randomIndex];
				}

			default:
				break;
		}

		return null;
	}

	public override bool ShouldLookForBetterWeapon() {
		if (base.ShouldLookForBetterWeapon()) {
			if (GetActiveWeapon() != null && IsMoving())
				return false;

			if (GlobalEntity.GetState("gordon_precriminal") == GlobalEState.On) {
				NextWeaponSearchTime = float.MaxValue;
				return false;
			}

			BaseCombatWeapon? weapon = GetActiveWeapon();
			if (weapon != null) {
				bool defer = false;

				if (FClassnameIs(weapon, "weapon_ar2")) {
					NextWeaponSearchTime = OTHER_DEFER_SEARCH_TIME;
					defer = true;
				}
				else if (FClassnameIs(weapon, "weapon_rpg")) {
					NextWeaponSearchTime = OTHER_DEFER_SEARCH_TIME;
					defer = true;
				}
				else if (FClassnameIs(weapon, "weapon_shotgun")) {
					if (NumWeaponsInSquad("weapon_shotgun") > 1)
						defer = false;
					else {
						if (random.RandomInt(0, 1) == 0)
							NextWeaponSearchTime = gpGlobals.CurTime + SHOTGUN_DEFER_SEARCH_TIME;
						else
							NextWeaponSearchTime = gpGlobals.CurTime + SHOTGUN_DEFER_SEARCH_TIME + 10.0f;

						defer = true;
					}
				}

				if (defer)
					return false;
			}

			return true;
		}

		return false;
	}

	public override bool IsPlayerAlly(BasePlayer? player = null) {
		if (Classify() == Class_T.CitizenPassive && GlobalEntity.GetState("gordon_precriminal") == GlobalEState.On)
			return true;

		return base.IsPlayerAlly(player);
	}

	public bool CanJoinPlayerSquad() {
		if (NPCState == NPCState.Script || NPCState == NPCState.Prone)
			return false;

		if (HasSpawnFlags(SF_CITIZEN_NOT_COMMANDABLE))
			return false;

		if (IsInAScript())
			return false;

		if (!CanBeUsedAsAFriend())
			return false;

		if (IRelationType(AI_GetClosestPlayer()) != Disposition.LI)
			return false;

		return true;
	}

	public override bool HandleInteraction(AI_Interaction interactionType, object? data, BaseCombatCharacter? sourceEnt) {
		if (interactionType == AI_Interaction.ScannerInspect) {
			if (gpGlobals.CurTime > NextInspectTime) {
				NextInspectTime = gpGlobals.CurTime + 5.0;
				return true;
			}
			return false;
		}
		else if (interactionType == AI_Interaction.ScannerInspectBegin) {
			NextInspectTime = gpGlobals.CurTime + CIT_INSPECTED_DELAY_TIME;

			Vector3 targetDir = sourceEnt!.WorldSpaceCenter() - WorldSpaceCenter();
			MathLib.VectorNormalize(ref targetDir);

			if (Vector3.Dot(targetDir, BodyDirection3D()) < 0)
				InspectActivity = ACT_CIT_STARTLED;
			else
				InspectActivity = ACT_CIT_BLINDED;

			SetCondition(COND_CIT_START_INSPECTION);
			return true;
		}
		else if (interactionType == AI_Interaction.ScannerInspectHandsUp) {
			InspectActivity = ACT_CIT_HANDSUP;
			SetSchedule(SCHED_CITIZEN_PLAY_INSPECT_ACTIVITY);
			return true;
		}
		else if (interactionType == AI_Interaction.ScannerInspectShowArmband) {
			InspectActivity = ACT_CIT_SHOWARMBAND;
			SetSchedule(SCHED_CITIZEN_PLAY_INSPECT_ACTIVITY);
			return true;
		}
		else if (interactionType == AI_Interaction.ScannerInspectDone) {
			SetSchedule(SCHED_IDLE_WANDER);
			return true;
		}
		else if (interactionType == AI_Interaction.HitByPlayerThrownPhysObj)
			return true;

		return base.HandleInteraction(interactionType, data, sourceEnt);
	}

	public override bool FValidateHintType(AI_Hint hint) {
		switch (hint.HintType()) {
			case Hint.WorldVisuallyInteresting:
				return true;

			default:
				break;
		}

		return base.FValidateHintType(hint);
	}

	public void InputStartPatrolling(InputData inputdata) => ShouldPatrol = true;
	public void InputStopPatrolling(InputData inputdata) => ShouldPatrol = false;

	public override void DeathSound(in TakeDamageInfo info) {
		SentenceStop();

		EmitSound("NPC_Citizen.Die");
	}
}
