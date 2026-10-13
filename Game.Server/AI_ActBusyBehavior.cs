using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;
using Source.Common.SoundEmitterSystem;

using System.Numerics;

using DEFINE = Source.DEFINE<Game.Server.AI_ActBusyGoal>;

using static Game.Server.SCOND_t;

namespace Game.Server;

public enum ActBusyType
{
	Default = 0,
	Combat,
}

public enum BusyInterrupt
{
	None,
	Danger,
	Player,
	Ambush,
	Combat,
	ZombieSlump,
	SiegeDefense,
}

public enum BusyAnimParts
{
	Busy,
	Entry,
	Exit,

	MaxAnims,
}

public class BusyAnim
{
	public string? Name;
	public readonly Activity[] Activities = new Activity[(int)BusyAnimParts.MaxAnims];
	public readonly string?[] Sequences = new string?[(int)BusyAnimParts.MaxAnims];
	public readonly string?[] Sounds = new string?[(int)BusyAnimParts.MaxAnims];
	public float MinTime;
	public float MaxTime;
	public BusyInterrupt BusyInterruptType;
	public bool UseAutomovement;
}

public struct BusySafeZone
{
	public Vector3 Mins;
	public Vector3 Maxs;
}

public enum ActBusySightMethod
{
	Full = 0,
	LOSOnly,
}

public class ActBusyAnimData() : AutoGameSystem("CActBusyAnimData")
{
	public static readonly ActBusyAnimData g_ActBusyAnimDataSystem = new();

	readonly List<BusyAnim> ActBusyAnims = [];

	public override void LevelInitPostEntity() => ParseAnimDataFile();

	public override void LevelShutdownPostEntity() => ActBusyAnims.Clear();

	public void ParseAnimDataFile() {
		KeyValues kvAnimData = new("ActBusyAnimDatafile");
		if (kvAnimData.LoadFromFile(filesystem, "scripts/actbusy.txt")) {
			KeyValues? kvAnim = kvAnimData.GetFirstSubKey();
			while (kvAnim != null) {
				BusyAnim anim = new();
				if (ParseActBusyFromKV(anim, kvAnim))
					ActBusyAnims.Add(anim);

				kvAnim = kvAnim.GetNextKey();
			}
		}
	}

	static string? AllocPooledString(ReadOnlySpan<char> str) => str.IsEmpty ? null : new(str);

	public static bool ParseActBusyFromKV(BusyAnim anim, KeyValues section) {
		anim.Name = AllocPooledString(section.Name);

		anim.Activities[(int)BusyAnimParts.Busy] = (Activity)AI_BaseNPC.GetActivityID(section.GetString("busy_anim", "ACT_INVALID"));
		anim.Activities[(int)BusyAnimParts.Entry] = (Activity)AI_BaseNPC.GetActivityID(section.GetString("entry_anim", "ACT_INVALID"));
		anim.Activities[(int)BusyAnimParts.Exit] = (Activity)AI_BaseNPC.GetActivityID(section.GetString("exit_anim", "ACT_INVALID"));

		anim.Sequences[(int)BusyAnimParts.Busy] = AllocPooledString(section.GetString("busy_sequence", null));
		anim.Sequences[(int)BusyAnimParts.Entry] = AllocPooledString(section.GetString("entry_sequence", null));
		anim.Sequences[(int)BusyAnimParts.Exit] = AllocPooledString(section.GetString("exit_sequence", null));

		anim.Sounds[(int)BusyAnimParts.Busy] = AllocPooledString(section.GetString("busy_sound", null));
		anim.Sounds[(int)BusyAnimParts.Entry] = AllocPooledString(section.GetString("entry_sound", null));
		anim.Sounds[(int)BusyAnimParts.Exit] = AllocPooledString(section.GetString("exit_sound", null));

		anim.MinTime = section.GetFloat("min_time", 10.0f);
		anim.MaxTime = section.GetFloat("max_time", 20.0f);

		anim.UseAutomovement = section.GetInt("use_automovement", 0) != 0;

		ReadOnlySpan<char> interrupt = section.GetString("interrupts", "BA_INT_DANGER");
		if (interrupt.SequenceEqual("BA_INT_PLAYER"))
			anim.BusyInterruptType = BusyInterrupt.Player;
		else if (interrupt.SequenceEqual("BA_INT_DANGER"))
			anim.BusyInterruptType = BusyInterrupt.Danger;
		else if (interrupt.SequenceEqual("BA_INT_AMBUSH"))
			anim.BusyInterruptType = BusyInterrupt.Ambush;
		else if (interrupt.SequenceEqual("BA_INT_COMBAT"))
			anim.BusyInterruptType = BusyInterrupt.Combat;
		else if (interrupt.SequenceEqual("BA_INT_ZOMBIESLUMP"))
			anim.BusyInterruptType = BusyInterrupt.ZombieSlump;
		else if (interrupt.SequenceEqual("BA_INT_SIEGE_DEFENSE"))
			anim.BusyInterruptType = BusyInterrupt.SiegeDefense;
		else
			anim.BusyInterruptType = BusyInterrupt.None;

		return true;
	}

	public int FindBusyAnim(Activity activity, ReadOnlySpan<char> sequence) {
		int count = ActBusyAnims.Count;
		for (int i = 0; i < count; i++) {
			BusyAnim busyAnim = ActBusyAnims[i];
			Assert(busyAnim != null);

			if (!sequence.IsEmpty && busyAnim.Name != null && busyAnim.Name.AsSpan().Equals(sequence, StringComparison.OrdinalIgnoreCase))
				return i;

			if (activity != Activity.ACT_INVALID && busyAnim.Activities[(int)BusyAnimParts.Busy] == activity)
				return i;
		}

		if (!sequence.IsEmpty)
			Warning($"Specified '{sequence}' as a busy anim name, and it's not in the act busy anim list.\n");
		else if (activity != Activity.ACT_INVALID)
			Warning($"Tried to use Activity {(int)activity} as a busy anim, and it's not in the act busy anim list.\n");

		return -1;
	}

	public BusyAnim? GetBusyAnim(int index) => index >= 0 && index < ActBusyAnims.Count ? ActBusyAnims[index] : null;
}

public class AI_ActBusyBehavior : AI_Behavior_AI_BaseNPC_100000
{
	const float ACTBUSY_SEE_ENTITY_TIMEOUT = 1.0f;
	const float ACTBUSY_COMBAT_PLAYER_MAX_DIST = 720.0f;
	const float ACTBUSY_ENEMY_TOO_CLOSE_DIST_SQR = 240 * 240;
	public const float NO_MAX_TIME = -1;

	public static readonly ConVar ai_actbusy_search_time = new("ai_actbusy_search_time", "10.0");
	public static readonly ConVar ai_debug_actbusy = new("ai_debug_actbusy", "0", FCvar.Cheat, "Used to debug actbusy behavior. Usage:\n" +
		"1: Constantly draw lines from NPCs to the actbusy nodes they've chosen to actbusy at.\n" +
		"2: Whenever an NPC makes a decision to use an actbusy, show which actbusy they've chosen.\n" +
		"3: Selected NPCs (with npc_select) will report why they're not choosing actbusy nodes.\n" +
		"4: Display debug output of actbusy logic.\n" +
		"5: Display safe zone volumes and info.\n");

	static AnimEventId AE_ACTBUSY_WEAPON_FIRE_ON;
	static AnimEventId AE_ACTBUSY_WEAPON_FIRE_OFF;

	public const int SCHED_ACTBUSY_START_BUSYING = NEXT_SCHEDULE;
	public const int SCHED_ACTBUSY_BUSY = NEXT_SCHEDULE + 1;
	public const int SCHED_ACTBUSY_STOP_BUSYING = NEXT_SCHEDULE + 2;
	public const int SCHED_ACTBUSY_LEAVE = NEXT_SCHEDULE + 3;
	public const int SCHED_ACTBUSY_TELEPORT_TO_BUSY = NEXT_SCHEDULE + 4;

	public const int TASK_ACTBUSY_PLAY_BUSY_ANIM = NEXT_TASK;
	public const int TASK_ACTBUSY_PLAY_ENTRY = NEXT_TASK + 1;
	public const int TASK_ACTBUSY_PLAY_EXIT = NEXT_TASK + 2;
	public const int TASK_ACTBUSY_TELEPORT_TO_BUSY = NEXT_TASK + 3;
	public const int TASK_ACTBUSY_WALK_PATH_TO_BUSY = NEXT_TASK + 4;
	public const int TASK_ACTBUSY_GET_PATH_TO_ACTBUSY = NEXT_TASK + 5;
	public const int TASK_ACTBUSY_VERIFY_EXIT = NEXT_TASK + 6;

	public const int COND_ACTBUSY_LOST_SEE_ENTITY = NEXT_CONDITION;
	public const int COND_ACTBUSY_AWARE_OF_ENEMY_IN_SAFE_ZONE = NEXT_CONDITION + 1;
	public const int COND_ACTBUSY_ENEMY_TOO_CLOSE = NEXT_CONDITION + 2;

	static readonly AI_ClassScheduleIdSpace gm_ClassScheduleIdSpace = new();

	static ActBusyAnimData g_ActBusyAnimDataSystem => ActBusyAnimData.g_ActBusyAnimDataSystem;

	bool Enabled;
	bool ForceActBusy;
	Activity ForcedActivity;
	bool TeleportToBusy;
	bool UseNearestBusy;
	bool Leaving;
	bool VisibleOnly;
	bool UseRenderBoundsForCollision;
	float ForcedMaxTime;
	bool Busy;
	bool MovingToBusy;
	bool NeedsToPlayExitAnimValue;
	TimeUnit_t NextBusySearchTime;
	TimeUnit_t EndBusyAt;
	float BusySearchRange;
	bool InQueue;
	int CurrentBusyAnim;
	Handle<AI_ActBusyGoal> ActBusyGoal = new();
	bool NeedToSetBounds;
	EHANDLE SeeEntity = new();
	TimeUnit_t TimeLastSawSeeEntity;
	bool ExitedBusyToDueLostSeeEntity;
	bool ExitedBusyToDueSeeEnemy;

	int NumConsecutivePathFailures;
	bool AutoFireWeapon;
	TimeUnit_t DeferUntil;
	int NumEnemiesInSafeZone;

	readonly List<BusySafeZone> SafeZones = [];

	public AI_ActBusyBehavior() {
		LoadSchedules();

		Enabled = false;
		UseRenderBoundsForCollision = false;
		DeferUntil = 0;
	}

	public override string GetName() => "ActBusy";

	public override AI_ClassScheduleIdSpace GetClassScheduleIdSpace() => gm_ClassScheduleIdSpace;
	protected override string GetSchedulingErrorName() => nameof(AI_ActBusyBehavior);

	public static bool LoadSchedules() => CustomSchedules.Load(AI_BaseNPC.LoadSchedules, nameof(AI_ActBusyBehavior));

	static readonly AI_CustomSchedules CustomSchedules = new() {
		IdSpace = gm_ClassScheduleIdSpace,
		ParentIdSpace = AccessClassScheduleIdSpaceDirect(),
		Schedules = [
			(nameof(SCHED_ACTBUSY_START_BUSYING), SCHED_ACTBUSY_START_BUSYING),
			(nameof(SCHED_ACTBUSY_BUSY), SCHED_ACTBUSY_BUSY),
			(nameof(SCHED_ACTBUSY_STOP_BUSYING), SCHED_ACTBUSY_STOP_BUSYING),
			(nameof(SCHED_ACTBUSY_LEAVE), SCHED_ACTBUSY_LEAVE),
			(nameof(SCHED_ACTBUSY_TELEPORT_TO_BUSY), SCHED_ACTBUSY_TELEPORT_TO_BUSY),
		],
		Tasks = [
			(nameof(TASK_ACTBUSY_PLAY_BUSY_ANIM), TASK_ACTBUSY_PLAY_BUSY_ANIM),
			(nameof(TASK_ACTBUSY_PLAY_ENTRY), TASK_ACTBUSY_PLAY_ENTRY),
			(nameof(TASK_ACTBUSY_PLAY_EXIT), TASK_ACTBUSY_PLAY_EXIT),
			(nameof(TASK_ACTBUSY_TELEPORT_TO_BUSY), TASK_ACTBUSY_TELEPORT_TO_BUSY),
			(nameof(TASK_ACTBUSY_WALK_PATH_TO_BUSY), TASK_ACTBUSY_WALK_PATH_TO_BUSY),
			(nameof(TASK_ACTBUSY_GET_PATH_TO_ACTBUSY), TASK_ACTBUSY_GET_PATH_TO_ACTBUSY),
			(nameof(TASK_ACTBUSY_VERIFY_EXIT), TASK_ACTBUSY_VERIFY_EXIT),
		],
		Conditions = [
			(nameof(COND_ACTBUSY_LOST_SEE_ENTITY), COND_ACTBUSY_LOST_SEE_ENTITY),
			(nameof(COND_ACTBUSY_AWARE_OF_ENEMY_IN_SAFE_ZONE), COND_ACTBUSY_AWARE_OF_ENEMY_IN_SAFE_ZONE),
			(nameof(COND_ACTBUSY_ENEMY_TOO_CLOSE), COND_ACTBUSY_ENEMY_TOO_CLOSE),
		],
		Declare = static () => {
			AE_ACTBUSY_WEAPON_FIRE_ON = AI_BaseNPC.DeclareAnimEvent(nameof(AE_ACTBUSY_WEAPON_FIRE_ON));
			AE_ACTBUSY_WEAPON_FIRE_OFF = AI_BaseNPC.DeclareAnimEvent(nameof(AE_ACTBUSY_WEAPON_FIRE_OFF));
		},
		Definitions = [
			new(SCHED_ACTBUSY_START_BUSYING,
				[
					(TASK_SET_TOLERANCE_DISTANCE, 4),
					TASK_ACTBUSY_GET_PATH_TO_ACTBUSY,
					TASK_ACTBUSY_WALK_PATH_TO_BUSY,
					TASK_WAIT_FOR_MOVEMENT,
					TASK_STOP_MOVING,
					TASK_FACE_HINTNODE,
					TASK_ACTBUSY_PLAY_ENTRY,
					(TASK_SET_SCHEDULE, SCHED_ACTBUSY_BUSY),
				],
				[
					COND_ACTBUSY_LOST_SEE_ENTITY,
				]),

			new(SCHED_ACTBUSY_BUSY,
				[
					TASK_ACTBUSY_PLAY_BUSY_ANIM,
				],
				[
					COND_PROVOKED,
				]),

			new(SCHED_ACTBUSY_STOP_BUSYING,
				[
					TASK_ACTBUSY_VERIFY_EXIT,
					TASK_ACTBUSY_PLAY_EXIT,
					(TASK_WAIT, 0.1f),
				],
				[
					COND_NO_CUSTOM_INTERRUPTS,
				]),

			new(SCHED_ACTBUSY_LEAVE,
				[
					(TASK_SET_TOLERANCE_DISTANCE, 4),
					TASK_ACTBUSY_GET_PATH_TO_ACTBUSY,
					TASK_ACTBUSY_WALK_PATH_TO_BUSY,
					TASK_WAIT_FOR_MOVEMENT,
				],
				[
					COND_PROVOKED,
				]),

			new(SCHED_ACTBUSY_TELEPORT_TO_BUSY,
				[
					TASK_ACTBUSY_TELEPORT_TO_BUSY,
					TASK_ACTBUSY_PLAY_ENTRY,
					(TASK_SET_SCHEDULE, SCHED_ACTBUSY_BUSY),
				],
				[
					COND_PROVOKED,
				]),
		],
	};

	public bool IsMovingToBusy() => MovingToBusy;
	public bool IsEnabled() => Enabled;
	public void SetUseRenderBounds(bool useBounds) => UseRenderBoundsForCollision = useBounds;
	public bool NeedsToPlayExitAnim() => NeedsToPlayExitAnimValue;
	public bool IsInsideActBusy() => Busy;

	Task_t? GetCurTask() => GetOuter()!.GetTask();
	BaseEntity? GetEnemy() => GetOuter()!.GetEnemy();
	Vector3 GetAbsOrigin() => GetOuter()!.GetAbsOrigin();
	void TaskComplete() => GetOuter()!.TaskComplete();
	void TaskFail(AI_TaskFailureCode code) => GetOuter()!.TaskFail(code);
	void TaskFail(string failText) => GetOuter()!.TaskFail(failText);

	public void Enable(AI_ActBusyGoal? goal, float range, bool visibleOnly) {
		NotifyBusyEnding();

		if (goal != null)
			ActBusyGoal.Set(goal);
		Enabled = true;
		Busy = false;
		MovingToBusy = false;
		NeedsToPlayExitAnimValue = false;
		Leaving = false;
		NextBusySearchTime = gpGlobals.CurTime + ai_actbusy_search_time.GetFloat();
		EndBusyAt = 0;
		VisibleOnly = visibleOnly;
		InQueue = ActBusyGoal.Get() is AI_ActBusyQueueGoal;
		ForcedActivity = Activity.ACT_INVALID;
		SeeEntity.Set(null);
		ExitedBusyToDueLostSeeEntity = false;
		ExitedBusyToDueSeeEnemy = false;
		NumConsecutivePathFailures = 0;

		SetBusySearchRange(range);

		if (ai_debug_actbusy.GetInt() == 4)
			Msg($"ACTBUSY: behavior enabled on NPC {GetOuter()!.GetClassname()} ({GetOuter()!.GetDebugName()})\n");

		if (IsCombatActBusy())
			CollectSafeZoneVolumes(goal!);

		if (IsCurScheduleOverridable()) {
			NextBusySearchTime = gpGlobals.CurTime;
			GetOuter()!.ClearSchedule("Enabling act busy");
		}

		ClearCondition(COND_ACTBUSY_AWARE_OF_ENEMY_IN_SAFE_ZONE);
	}

	public void SetBusySearchRange(float range) => BusySearchRange = range;

	public void Disable() {
		if (ai_debug_actbusy.GetInt() == 4)
			Msg($"ACTBUSY: behavior disabled on NPC {GetOuter()!.GetClassname()} ({GetOuter()!.GetDebugName()})\n");

		if (Enabled)
			SetCondition((int)SCOND_t.COND_PROVOKED);

		StopBusying();
		Enabled = false;
	}

	public void ForceActBusyNow(AI_ActBusyGoal? goal, AI_Hint? hintNode = null, float maxTime = NO_MAX_TIME, bool visibleOnly = false, bool teleportToBusy = false, bool useNearestBusy = false, BaseEntity? seeEntity = null, Activity activity = Activity.ACT_INVALID) {
		Assert(!Leaving);

		if (NeedsToPlayExitAnimValue) {
			if (HasAnimForActBusy(CurrentBusyAnim, BusyAnimParts.Exit)) {
				Warning($"ACTBUSY: {GetOuter()!.GetDebugName()}({GetOuter()!.GetClassname()}) was told to actbusy while inside an actbusy that needs to exit first. IGNORING.\n");
				return;
			}
		}

		if (ai_debug_actbusy.GetInt() == 4) {
			Msg($"ACTBUSY: ForceActBusy on NPC {GetOuter()!.GetClassname()} ({GetOuter()!.GetDebugName()}): ");
			if (hintNode != null)
				Msg($"Hintnode {hintNode.GetDebugName()}");
			else
				Msg("No Hintnode specified");
			Msg("\n");
		}

		Enable(goal, BusySearchRange, visibleOnly);
		ForceActBusy = true;
		ForcedMaxTime = maxTime;
		TeleportToBusy = teleportToBusy;
		UseNearestBusy = useNearestBusy;
		ForcedActivity = activity;
		SeeEntity.Set(seeEntity);

		if (hintNode != null) {
			if (GetHintNode() != null && GetHintNode() != hintNode)
				GetHintNode()!.Unlock();

			if (hintNode.Lock(GetOuter()))
				SetHintNode(hintNode);
		}

		SetCondition((int)SCOND_t.COND_PROVOKED);
	}

	public void ForceActBusyLeave(bool visibleOnly = false) {
		if (ai_debug_actbusy.GetInt() == 4)
			Msg($"ACTBUSY: ForceActBusyLeave on NPC {GetOuter()!.GetClassname()} ({GetOuter()!.GetDebugName()})\n");

		Enable(null, BusySearchRange, visibleOnly);
		ForceActBusy = true;
		Leaving = true;
		ForcedActivity = Activity.ACT_INVALID;
		SeeEntity.Set(null);

		SetCondition((int)SCOND_t.COND_PROVOKED);
	}

	public void StopBusying() {
		if (GetOuter() == null)
			return;

		AutoFireWeapon = false;

		if (ai_debug_actbusy.GetInt() == 4)
			Msg($"ACTBUSY: StopBusying on NPC {GetOuter()!.GetClassname()} ({GetOuter()!.GetDebugName()})\n");

		if (Busy || MovingToBusy)
			SetCondition((int)SCOND_t.COND_PROVOKED);

		EndBusyAt = gpGlobals.CurTime;
		ForceActBusy = false;
		TeleportToBusy = false;
		UseNearestBusy = false;
		Leaving = false;
		MovingToBusy = false;
		ForcedActivity = Activity.ACT_INVALID;
		SeeEntity.Set(null);
	}

	public bool IsStopBusying() => IsCurSchedule(SCHED_ACTBUSY_STOP_BUSYING);

	public AI_Hint? FindActBusyHintNode() {
		Assert(!IsCombatActBusy());

		AI_HintNodeFlags bits = AI_HintNodeFlags.UseGroup;
		if (VisibleOnly)
			bits |= AI_HintNodeFlags.Visible;

		if (ai_debug_actbusy.GetInt() == 3 && (GetOuter()!.DebugOverlays & DebugOverlayBits.NPCSelected) != 0)
			bits |= AI_HintNodeFlags.ReportFailures;

		if (UseNearestBusy)
			bits |= AI_HintNodeFlags.Nearest;
		else
			bits |= AI_HintNodeFlags.Random;

		AI_Hint? node = AI_HintManager.FindHint(GetOuter(), Hint.WorldWorkPosition, bits, BusySearchRange);

		return node;
	}

	public AI_Hint? FindCombatActBusyHintNode() {
		Assert(IsCombatActBusy());

		BasePlayer? player = GetOuter()!.AI_GetClosestPlayer();

		if (player == null)
			return null;

		HintCriteria criteria = new();

		AI_HintNodeFlags bits = AI_HintNodeFlags.UseGroup;

		if (VisibleOnly)
			bits |= AI_HintNodeFlags.Visible;

		if (ai_debug_actbusy.GetInt() == 3 && (GetOuter()!.DebugOverlays & DebugOverlayBits.NPCSelected) != 0)
			bits |= AI_HintNodeFlags.ReportFailures;

		if (UseNearestBusy)
			bits |= AI_HintNodeFlags.Nearest;
		else
			bits |= AI_HintNodeFlags.Random;

		bits |= AI_HintNodeFlags.HasEyePositionLOSToPlayer;

		criteria.AddHintType(Hint.WorldWorkPosition);
		criteria.SetFlag(bits);
		criteria.AddIncludePosition(player.GetAbsOrigin(), ACTBUSY_COMBAT_PLAYER_MAX_DIST);

		AI_Hint? node = AI_HintManager.FindHint(GetOuter(), criteria);

		return node;
	}

	public AI_Hint? FindCombatActBusyTeleportHintNode() {
		Assert(IsCombatActBusy());

		BasePlayer? player = GetOuter()!.AI_GetClosestPlayer();

		if (player == null)
			return null;

		HintCriteria criteria = new();

		AI_HintNodeFlags bits = AI_HintNodeFlags.UseGroup;

		if (ai_debug_actbusy.GetInt() == 3 && (GetOuter()!.DebugOverlays & DebugOverlayBits.NPCSelected) != 0)
			bits |= AI_HintNodeFlags.ReportFailures;

		bits |= AI_HintNodeFlags.Random;
		bits |= AI_HintNodeFlags.NotVisibleToPlayer;

		criteria.AddHintType(Hint.WorldWorkPosition);
		criteria.SetFlag(bits);
		criteria.AddIncludePosition(player.GetAbsOrigin(), ACTBUSY_COMBAT_PLAYER_MAX_DIST * 1.1f);

		AI_Hint? node = AI_HintManager.FindHint(GetOuter(), criteria);

		return node;
	}

	protected override bool FValidateHintType(AI_Hint hint) {
		if (hint.HintType() != Hint.WorldWorkPosition && hint.HintType() != Hint.NpcExitPoint)
			return false;

		ReadOnlySpan<char> sequenceOrActivity = hint.HintActivityName();
		int space = sequenceOrActivity.IndexOf(' ');
		if (space >= 0) {
			if (sequenceOrActivity[(space + 1)..].StartsWith("teleport"))
				return true;
		}

		Util.TraceHull(hint.GetAbsOrigin(), hint.GetAbsOrigin(), GetOuter()!.WorldAlignMins(), GetOuter()!.WorldAlignMaxs(), Mask.Solid, GetOuter(), Source.CollisionGroup.None, out Trace tr);
		if (tr.Fraction == 1.0)
			return true;

		return false;
	}

	public override bool CanSelectSchedule() {
		if (Busy || ForceActBusy || NeedsToPlayExitAnimValue)
			return true;

		if (!Enabled)
			return false;

		if (DeferUntil > gpGlobals.CurTime)
			return false;

		if (CountEnemiesInSafeZone() > 0)
			return false;

		if (!IsCurScheduleOverridable())
			return false;

		return NextBusySearchTime < gpGlobals.CurTime;
	}

	public bool IsCurScheduleOverridable() {
		if (IsCombatActBusy())
			return GetOuter()!.GetState() != NPCState.Script;

		if (GetOuter()!.IsInAVehicle())
			return false;

		return IsCurSchedule(SCHED_IDLE_STAND) || IsCurSchedule(SCHED_NONE);
	}

	protected override bool ShouldIgnoreSound(ref WorldSoundInstance sound) {
		if (Busy) {
			BusyAnim? busyAnim = g_ActBusyAnimDataSystem.GetBusyAnim(CurrentBusyAnim);

			if (busyAnim != null && busyAnim.BusyInterruptType == BusyInterrupt.ZombieSlump)
				return true;

			if (busyAnim != null && (busyAnim.BusyInterruptType == BusyInterrupt.Ambush || busyAnim.BusyInterruptType == BusyInterrupt.Combat)) {
				if (!GetOuter()!.FVisible(sound.GetSoundReactOrigin()))
					return true;
			}
		}

		return base.ShouldIgnoreSound(ref sound);
	}

	public int CountEnemiesInSafeZone() {
		if (!IsCombatActBusy())
			return 0;

		List<AI_BaseNPC> ais = g_AI_Manager.AccessAIs();
		int nAIs = g_AI_Manager.NumAIs();
		int count = 0;

		for (int i = 0; i < nAIs; i++) {
			if (GetOuter()!.IRelationType(ais[i]) < Disposition.LI) {
				if (IsInSafeZone(ais[i]))
					count++;
			}
		}

		return count;
	}

	public override void GatherConditions() {
		if (!IsCurSchedule(SCHED_ACTBUSY_BUSY, false))
			ClearCondition(COND_ACTBUSY_ENEMY_TOO_CLOSE);

		base.GatherConditions();

		bool checkLOS = true;
		bool checkFOV = true;

		if (ActBusyGoal.Get() != null && ActBusyGoal.Get()!.SightMethod == ActBusySightMethod.LOSOnly)
			checkFOV = false;

		BaseEntity? seeEntity = SeeEntity.Get();
		if (seeEntity != null && Busy) {
			if ((!checkFOV || GetOuter()!.FInViewCone(seeEntity)) && GetOuter()!.QuerySeeEntity(seeEntity) && (!checkLOS || GetOuter()!.FVisible(seeEntity))) {
				TimeLastSawSeeEntity = gpGlobals.CurTime;
				ClearCondition(COND_ACTBUSY_LOST_SEE_ENTITY);
			}
			else if (ActBusyGoal.Get() != null) {
				TimeUnit_t delta = gpGlobals.CurTime - TimeLastSawSeeEntity;
				if (delta >= ActBusyGoal.Get()!.SeeEntityTimeout) {
					SetCondition(COND_ACTBUSY_LOST_SEE_ENTITY);
					ActBusyGoal.Get()!.NPCLostSeeEntity(GetOuter()!);

					if (IsCombatActBusy() && (GetOuter()!.Classify() == Class_T.PlayerAllyVital && seeEntity.IsPlayer()))
						DeferUntil = gpGlobals.CurTime + 4.0f;
				}
			}
		}
		else
			ClearCondition(COND_ACTBUSY_LOST_SEE_ENTITY);

		if (Busy) {
			BusyAnim? busyAnim = g_ActBusyAnimDataSystem.GetBusyAnim(CurrentBusyAnim);
			if (busyAnim != null) {
				switch (busyAnim.BusyInterruptType) {
					case BusyInterrupt.Danger:
						break;

					case BusyInterrupt.Ambush:
						break;

					case BusyInterrupt.ZombieSlump: {
							ClearCondition((int)SCOND_t.COND_HEAR_PLAYER);
							ClearCondition((int)SCOND_t.COND_SEE_ENEMY);
							ClearCondition((int)SCOND_t.COND_NEW_ENEMY);

							BasePlayer? player = GetOuter()!.AI_GetClosestPlayer();

							if (player != null) {
								float dist = Vector3.Distance(player.GetAbsOrigin(), GetAbsOrigin());

								if (dist <= 60)
									StopBusying();
							}
						}
						break;

					case BusyInterrupt.Combat:
						ClearCondition((int)SCOND_t.COND_HEAR_PLAYER);
						ClearCondition((int)SCOND_t.COND_SEE_ENEMY);
						ClearCondition((int)SCOND_t.COND_NEW_ENEMY);
						break;

					case BusyInterrupt.Player:
						ClearCondition((int)SCOND_t.COND_HEAR_DANGER);
						ClearCondition((int)SCOND_t.COND_HEAR_COMBAT);
						ClearCondition((int)SCOND_t.COND_HEAR_WORLD);
						ClearCondition((int)SCOND_t.COND_HEAR_BULLET_IMPACT);
						break;

					case BusyInterrupt.SiegeDefense:
						ClearCondition((int)SCOND_t.COND_HEAR_PLAYER);
						ClearCondition((int)SCOND_t.COND_SEE_ENEMY);
						ClearCondition((int)SCOND_t.COND_NEW_ENEMY);
						ClearCondition((int)SCOND_t.COND_HEAR_COMBAT);
						ClearCondition((int)SCOND_t.COND_HEAR_WORLD);
						ClearCondition((int)SCOND_t.COND_HEAR_BULLET_IMPACT);
						break;

					case BusyInterrupt.None:
						ClearCondition((int)SCOND_t.COND_HEAR_DANGER);
						ClearCondition((int)SCOND_t.COND_HEAR_COMBAT);
						ClearCondition((int)SCOND_t.COND_HEAR_WORLD);
						ClearCondition((int)SCOND_t.COND_HEAR_BULLET_IMPACT);
						ClearCondition((int)SCOND_t.COND_HEAR_PLAYER);
						break;

					default:
						break;
				}
			}
		}

		if (AutoFireWeapon && random.RandomInt(0, 5) <= 3) {
			BaseCombatWeapon? weapon = GetOuter()!.GetActiveWeapon();

			weapon?.Operator_ForceNPCFire(GetOuter()!, false);
		}
	}

	public override void EndScheduleSelection() {
		NotifyBusyEnding();

		CheckAndCleanupOnExit();
	}

	protected override Activity NPC_TranslateActivity(Activity activity) {
		Activity newActivity = base.NPC_TranslateActivity(activity);

		if (activity == Activity.ACT_RUN) {
			if (GetOuter() is AI_PlayerAlly)
				return Activity.ACT_RUN_STIMULATED;
		}

		return newActivity;
	}

	protected override void HandleAnimEvent(ref AnimEvent animEvent) {
		if (animEvent.Event == (int)AE_ACTBUSY_WEAPON_FIRE_ON) {
			AutoFireWeapon = true;
			return;
		}
		else if (animEvent.Event == (int)AE_ACTBUSY_WEAPON_FIRE_OFF) {
			AutoFireWeapon = false;
			return;
		}

		base.HandleAnimEvent(ref animEvent);
	}

	public void CheckAndCleanupOnExit() {
		if (NeedsToPlayExitAnimValue && !GetOuter()!.IsMarkedForDeletion() && GetOuter()!.IsAlive()) {
			Warning($"NPC {GetOuter()!.GetDebugName()}({GetOuter()!.GetClassname()}) left actbusy without playing exit anim.\n");
			NeedsToPlayExitAnimValue = false;
		}

		GetOuter()!.RemoveFlag(EntityFlags.Fly);

		if (UseRenderBoundsForCollision)
			GetOuter()!.SetHullSizeNormal(true);
	}

	protected override void BuildScheduleTestBits() {
		base.BuildScheduleTestBits();

		if (IsCurSchedule(SCHED_ACTBUSY_START_BUSYING)) {
			if (GetOuter()!.GetTask()!.Value.Task == TASK_ACTBUSY_PLAY_ENTRY)
				return;

			GetOuter()!.SetCustomInterruptCondition((int)SCOND_t.COND_PROVOKED);

			if (IsCombatActBusy())
				GetOuter()!.SetCustomInterruptCondition(GetClassScheduleIdSpace().ConditionLocalToGlobal(COND_ACTBUSY_ENEMY_TOO_CLOSE));
		}

		if (InQueue || IsCurSchedule(SCHED_ACTBUSY_LEAVE))
			return;

		if (!Busy || IsCurSchedule(SCHED_ACTBUSY_STOP_BUSYING))
			return;

		BusyAnim? busyAnim = g_ActBusyAnimDataSystem.GetBusyAnim(CurrentBusyAnim);
		if (busyAnim != null) {
			AI_BaseNPC outer = GetOuter()!;
			switch (busyAnim.BusyInterruptType) {
				case BusyInterrupt.ZombieSlump:
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_LIGHT_DAMAGE);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAVY_DAMAGE);
					break;

				case BusyInterrupt.SiegeDefense:
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAR_DANGER);
					outer.SetCustomInterruptCondition(GetClassScheduleIdSpace().ConditionLocalToGlobal(COND_ACTBUSY_AWARE_OF_ENEMY_IN_SAFE_ZONE));
					outer.SetCustomInterruptCondition(GetClassScheduleIdSpace().ConditionLocalToGlobal(COND_ACTBUSY_ENEMY_TOO_CLOSE));
					break;

				case BusyInterrupt.Ambush:
				case BusyInterrupt.Danger:
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_LIGHT_DAMAGE);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAVY_DAMAGE);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAR_DANGER);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAR_COMBAT);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAR_BULLET_IMPACT);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_NEW_ENEMY);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_SEE_ENEMY);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_PLAYER_ADDED_TO_SQUAD);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_RECEIVED_ORDERS);
					break;

				case BusyInterrupt.Player:
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_LIGHT_DAMAGE);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAVY_DAMAGE);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAR_DANGER);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAR_COMBAT);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAR_BULLET_IMPACT);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_NEW_ENEMY);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_PLAYER_ADDED_TO_SQUAD);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_RECEIVED_ORDERS);

					outer.SetCustomInterruptCondition((int)SCOND_t.COND_SEE_PLAYER);
					break;

				case BusyInterrupt.Combat:
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_LIGHT_DAMAGE);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAVY_DAMAGE);
					outer.SetCustomInterruptCondition((int)SCOND_t.COND_HEAR_DANGER);
					break;

				case BusyInterrupt.None:
					break;

				default:
					break;
			}
		}
	}

	int SelectScheduleForLeaving() {
		if (GetHintNode() != null) {
			if (GetHintNode()!.HintType() == Hint.NpcExitPoint) {
				if (Util.DistApprox(GetHintNode()!.GetAbsOrigin(), GetAbsOrigin()) < 64) {
					if (!GetOuter()!.IsMarkedForDeletion()) {
						BaseEntity? owner = GetOuter()!.GetOwnerEntity();
						if (owner != null) {
							owner.DeathNotice(GetOuter()!);
							GetOuter()!.SetOwnerEntity(null);
						}
						GetOuter()!.SetThink(GetOuter()!.SUB_Remove);
						GetOuter()!.SetNextThink(gpGlobals.CurTime + 0.1);

						ActBusyGoal.Get()?.NPCLeft(GetOuter()!);
					}

					return SCHED_IDLE_STAND;
				}

				return SCHED_ACTBUSY_LEAVE;
			}
			else {
				GetHintNode()!.NPCStoppedUsing(GetOuter()!);
				GetHintNode()!.Unlock();
				SetHintNode(null);
			}
		}

		HintCriteria hintCriteria = new();
		hintCriteria.SetHintType(Hint.NpcExitPoint);
		hintCriteria.SetFlag(AI_HintNodeFlags.Random | AI_HintNodeFlags.Clear | AI_HintNodeFlags.UseGroup);
		AI_Hint? node = AI_HintManager.FindHintRandom(GetOuter(), GetOuter()!.GetAbsOrigin(), hintCriteria);
		if (node != null) {
			SetHintNode(node);
			return SCHED_ACTBUSY_LEAVE;
		}

		return SCHED_IDLE_STAND;
	}

	int SelectScheduleWhileNotBusy(int baseSchedule) {
		if (ForceActBusy || NextBusySearchTime < gpGlobals.CurTime) {
			if (ForceActBusy || IsCombatActBusy())
				NextBusySearchTime = gpGlobals.CurTime + 2.0;
			else
				NextBusySearchTime = gpGlobals.CurTime + random.RandomFloat(ai_actbusy_search_time.GetFloat(), ai_actbusy_search_time.GetFloat() * 2);

			bool forceTeleport = false;
			AI_Hint? node = GetHintNode();
			if (node == null) {
				if (IsCombatActBusy()) {
					if (ActBusyGoal.Get()!.IsCombatActBusyTeleportAllowed() && NumConsecutivePathFailures >= 2 && !GetOuter()!.AI_GetClosestPlayer()!.FInViewCone(GetOuter()!)) {
						node = FindCombatActBusyTeleportHintNode();
						forceTeleport = true;
					}
					else
						node = FindCombatActBusyHintNode();
				}
				else
					node = FindActBusyHintNode();
			}
			if (node != null) {
				ReadOnlySpan<char> sequenceOrActivity = node.HintActivityName();
				Activity nodeActivity;
				int busyAnim;

				int space = sequenceOrActivity.IndexOf(' ');
				if (space >= 0) {
					if (sequenceOrActivity[(space + 1)..].StartsWith("teleport"))
						TeleportToBusy = true;

					ReadOnlySpan<char> actOrSeqName = sequenceOrActivity[..space];
					nodeActivity = (Activity)AI_BaseNPC.GetActivityID(actOrSeqName);
					busyAnim = g_ActBusyAnimDataSystem.FindBusyAnim(nodeActivity, actOrSeqName);
				}
				else {
					nodeActivity = (Activity)AI_BaseNPC.GetActivityID(sequenceOrActivity);
					busyAnim = g_ActBusyAnimDataSystem.FindBusyAnim(nodeActivity, sequenceOrActivity);
				}

				if (HasAnimForActBusy(busyAnim, BusyAnimParts.Busy)) {
					if (HasCondition(COND_ACTBUSY_LOST_SEE_ENTITY)) {
						if (ForceActBusy)
							StopBusying();
						return baseSchedule;
					}

					CurrentBusyAnim = busyAnim;
					if (CurrentBusyAnim == -1)
						return baseSchedule;

					if (ai_debug_actbusy.GetInt() == 4)
						Msg($"ACTBUSY: NPC {GetOuter()!.GetClassname()} ({GetOuter()!.GetDebugName()}) found Actbusy node {node.GetDebugName()} \n");

					GetHintNode()?.Unlock();

					SetHintNode(node);
					if (GetHintNode() != null && GetHintNode()!.Lock(GetOuter())) {
						ActBusyGoal.Get()?.NPCMovingToBusy(GetOuter()!);

						MovingToBusy = true;

						if (ActBusyGoal.Get() != null && ActBusyGoal.Get()!.SeeEntityName != null)
							SeeEntity.Set(gEntList.FindEntityByName(null, ActBusyGoal.Get()!.SeeEntityName));

						ClearCondition(COND_ACTBUSY_AWARE_OF_ENEMY_IN_SAFE_ZONE);

						if (TeleportToBusy)
							return SCHED_ACTBUSY_TELEPORT_TO_BUSY;
						else if (forceTeleport) {
							NumConsecutivePathFailures = 0;
							return SCHED_ACTBUSY_TELEPORT_TO_BUSY;
						}

						return SCHED_ACTBUSY_START_BUSYING;
					}
				}
			}
			else {
				if (IsCombatActBusy())
					NextBusySearchTime = gpGlobals.CurTime + 10.0;
			}
		}

		return SCHED_NONE;
	}

	int SelectScheduleWhileBusy() {
		if (!ActBusyNodeStillActive() || (EndBusyAt != 0 && gpGlobals.CurTime >= EndBusyAt)) {
			if (ai_debug_actbusy.GetInt() == 4)
				Msg($"ACTBUSY: NPC {GetOuter()!.GetClassname()} ({GetOuter()!.GetDebugName()}) ending actbusy.\n");

			StopBusying();
			return SCHED_ACTBUSY_STOP_BUSYING;
		}

		if (IsCombatActBusy() && (HasCondition(COND_ACTBUSY_AWARE_OF_ENEMY_IN_SAFE_ZONE) || HasCondition(COND_ACTBUSY_ENEMY_TOO_CLOSE)))
			return SCHED_ACTBUSY_STOP_BUSYING;

		return SCHED_ACTBUSY_BUSY;
	}

	protected override int SelectSchedule() {
		int baseSchedule = base.SelectSchedule();

		if (!IsCombatActBusy() && !ForceActBusy && baseSchedule != SCHED_IDLE_STAND) {
			if (Busy)
				return SCHED_ACTBUSY_STOP_BUSYING;

			CheckAndCleanupOnExit();
			return baseSchedule;
		}

		if (Leaving)
			return SelectScheduleForLeaving();

		bool shouldNotBeBusy = !Enabled || HasCondition((int)SCOND_t.COND_PLAYER_ADDED_TO_SQUAD) || HasCondition((int)SCOND_t.COND_RECEIVED_ORDERS);
		if (shouldNotBeBusy) {
			if (!GetOuter()!.IsMarkedForDeletion() && GetOuter()!.IsAlive())
				return SCHED_ACTBUSY_STOP_BUSYING;
		}
		else {
			if (Busy)
				return SelectScheduleWhileBusy();

			int schedule = SelectScheduleWhileNotBusy(baseSchedule);
			if (schedule != SCHED_NONE)
				return schedule;
		}

		CheckAndCleanupOnExit();
		return baseSchedule;
	}

	public bool ActBusyNodeStillActive() {
		if (GetHintNode() == null)
			return false;

		return GetHintNode()!.IsDisabled() == false;
	}

	protected override bool IsInterruptable() {
		if (IsActive())
			return false;

		return base.IsInterruptable();
	}

	protected override bool CanFlinch() {
		if (NeedsToPlayExitAnimValue)
			return false;

		return base.CanFlinch();
	}

	protected override float GetReasonableFacingDist() => 0;

	protected override void OnScheduleChange() {
		if (IsCurSchedule(SCHED_ACTBUSY_BUSY, false)) {
			if (HasCondition((int)SCOND_t.COND_SEE_ENEMY))
				ExitedBusyToDueSeeEnemy = true;

			if (HasCondition(COND_ACTBUSY_LOST_SEE_ENTITY))
				ExitedBusyToDueLostSeeEntity = true;
		}

		base.OnScheduleChange();
	}

	protected override bool QueryHearSound(ref WorldSoundInstance sound) {
		if (sound.IsSoundType(SoundInstanceType.Combat) || sound.IsSoundType(SoundInstanceType.BulletImpact)) {
			if (GetOuter()!.IRelationType(sound.Owner.Get()) == Disposition.LI)
				return false;
		}

		return base.QueryHearSound(ref sound);
	}

	protected override void OnSeeEntity(BaseEntity entity) {
		base.OnSeeEntity(entity);

		if (IsCombatActBusy() && GetOuter()!.IRelationType(entity) < Disposition.LI) {
			if (Vector3.DistanceSquared(entity.GetAbsOrigin(), GetAbsOrigin()) <= ACTBUSY_ENEMY_TOO_CLOSE_DIST_SQR)
				SetCondition(COND_ACTBUSY_ENEMY_TOO_CLOSE);
		}
	}

	protected override bool ShouldPlayerAvoid() {
		if (IsCombatActBusy()) {
			if (IsCurSchedule(SCHED_ACTBUSY_START_BUSYING)) {
				if (GetCurTask() != null && GetCurTask()!.Value.Task == TASK_ACTBUSY_PLAY_ENTRY)
					return true;
			}
			else if (IsCurSchedule(SCHED_ACTBUSY_STOP_BUSYING)) {
				if (GetCurTask() != null && GetCurTask()!.Value.Task == TASK_ACTBUSY_PLAY_EXIT)
					return true;
			}
		}
		else {
			if (IsCurSchedule(SCHED_ACTBUSY_START_BUSYING)) {
				if ((GetCurTask() != null && GetCurTask()!.Value.Task == TASK_WAIT_FOR_MOVEMENT) || GetOuter()!.GetTask()!.Value.Task == TASK_ACTBUSY_PLAY_ENTRY)
					return true;
			}
			else if (IsCurSchedule(SCHED_ACTBUSY_STOP_BUSYING)) {
				if (GetCurTask() != null && GetCurTask()!.Value.Task == TASK_ACTBUSY_PLAY_EXIT)
					return true;
			}
		}

		return base.ShouldPlayerAvoid();
	}

	public void ComputeAndSetRenderBounds() {
		if (GetOuter()!.ComputeHitboxSurroundingBox(out Vector3 mins, out Vector3 maxs)) {
			Util.SetSize(GetOuter()!, mins - GetAbsOrigin(), maxs - GetAbsOrigin());
			if (GetOuter()!.VPhysicsGetObject() != null)
				GetOuter()!.SetupVPhysicsHull();
		}
	}

	public bool IsActive() => Busy || ForceActBusy || NeedsToPlayExitAnimValue || Leaving;

	public bool IsCombatActBusy() {
		if (ActBusyGoal.Get() != null)
			return ActBusyGoal.Get()!.GetBusyType() == ActBusyType.Combat;

		return false;
	}

	public void CollectSafeZoneVolumes(AI_ActBusyGoal actBusyGoal) {
		SafeZones.Clear();

		if (actBusyGoal.SafeZoneVolume != null) {
			BaseEntity? volume = gEntList.FindEntityByName(null, actBusyGoal.SafeZoneVolume);

			while (volume != null) {
				BusySafeZone newSafeZone = default;
				volume.CollisionProp().WorldSpaceAABB(out newSafeZone.Mins, out newSafeZone.Maxs);
				SafeZones.Add(newSafeZone);
				volume = gEntList.FindEntityByName(volume, actBusyGoal.SafeZoneVolume);
			}
		}

		if (ai_debug_actbusy.GetInt() == 5)
			Msg($"Actbusy collected {SafeZones.Count} safe zones\n");
	}

	public bool IsInSafeZone(BaseEntity entity) {
		Vector3 location = entity.WorldSpaceCenter();

		for (int i = 0; i < SafeZones.Count; i++) {
			BusySafeZone safeZone = SafeZones[i];

			if (location.X > safeZone.Mins.X &&
				location.Y > safeZone.Mins.Y &&
				location.Z > safeZone.Mins.Z &&

				location.X < safeZone.Maxs.X &&
				location.Y < safeZone.Maxs.Y &&
				location.Z < safeZone.Maxs.Z)
				return true;
		}

		return false;
	}

	bool HasAnimForActBusy(int actBusy, BusyAnimParts animPart) {
		if (actBusy == -1)
			return false;

		BusyAnim? busyAnim = g_ActBusyAnimDataSystem.GetBusyAnim(actBusy);
		if (busyAnim == null)
			return false;

		if (busyAnim.Sequences[(int)animPart] != null)
			return GetOuter()!.LookupSequence(busyAnim.Sequences[(int)animPart]) != StudioHdr.ACTIVITY_NOT_AVAILABLE;

		if (busyAnim.Activities[(int)animPart] != Activity.ACT_INVALID)
			return GetOuter()!.HaveSequenceForActivity(busyAnim.Activities[(int)animPart]);

		return false;
	}

	void PlaySoundForActBusy(BusyAnimParts animPart) {
		BusyAnim? busyAnim = g_ActBusyAnimDataSystem.GetBusyAnim(CurrentBusyAnim);
		if (busyAnim == null)
			return;

		string? sound = busyAnim.Sounds[(int)animPart];
		if (sound != null) {
			SoundParameters parms = default;
			if (BaseEntity.GetParametersForSound(sound, ref parms, GetOuter()!.GetModelName())) {
				PASAttenuationFilter filter = new(GetOuter()!);
				BaseEntity.EmitSound(filter, GetOuter()!.EntIndex(), sound);
			}
			else {
				AI_Expresser? expresser = GetOuter()!.GetExpresser();
				if (expresser != null) {
					if (!expresser.IsSpeaking() && expresser.CanSpeakConcept(sound))
						expresser.Speak(sound);
				}
			}
		}
	}

	bool PlayAnimForActBusy(BusyAnimParts animPart) {
		BusyAnim? busyAnim = g_ActBusyAnimDataSystem.GetBusyAnim(CurrentBusyAnim);
		if (busyAnim == null)
			return false;

		if (busyAnim.Sequences[(int)animPart] != null) {
			GetOuter()!.SetSequenceByName(busyAnim.Sequences[(int)animPart]);
			GetOuter()!.SetIdealActivity(Activity.ACT_DO_NOT_DISTURB);
			return true;
		}

		if (busyAnim.Activities[(int)animPart] != Activity.ACT_INVALID) {
			GetOuter()!.SetIdealActivity(busyAnim.Activities[(int)animPart]);
			return true;
		}

		return false;
	}

	protected override void StartTask(in Task_t task) {
		switch (task.Task) {
			case TASK_ACTBUSY_PLAY_BUSY_ANIM: {
					if (!Enabled) {
						TaskComplete();
						return;
					}

					NeedToSetBounds = true;

					BusyAnim? busyAnim = g_ActBusyAnimDataSystem.GetBusyAnim(CurrentBusyAnim);

					if (busyAnim == null || !busyAnim.UseAutomovement)
						GetOuter()!.AddFlag(EntityFlags.Fly);

					GetOuter()!.SetGroundEntity(null);

					if (MovingToBusy) {
						if (Util.DistApprox(GetHintNode()!.GetAbsOrigin(), GetAbsOrigin()) > 16 || !GetOuter()!.FacingIdeal()) {
							TaskFail("Not correctly on hintnode");
							EndBusyAt = gpGlobals.CurTime;
							return;
						}
					}

					MovingToBusy = false;

					if (!ActBusyNodeStillActive()) {
						TaskFail(AI_TaskFailureCode.NoHintNode);
						return;
					}

					if (!Busy) {
						Busy = true;

						GetHintNode()!.NPCStartedUsing(GetOuter()!);
						ActBusyGoal.Get()?.NPCStartedBusy(GetOuter()!);

						if (busyAnim != null) {
							float maxTime = busyAnim.MaxTime;
							float minTime = busyAnim.MinTime;

							if (ForceActBusy && ForcedMaxTime != NO_MAX_TIME) {
								maxTime = ForcedMaxTime;

								if (maxTime != 0 && maxTime < minTime)
									minTime = maxTime;
							}

							if (maxTime == 0 || InQueue) {
								EndBusyAt = 0;
								GetOuter()!.SetWait(99999);
							}
							else {
								float time = random.RandomFloat(minTime, maxTime);
								EndBusyAt = gpGlobals.CurTime + time;
								GetOuter()!.SetWait(time);
							}
						}
					}

					PlayAnimForActBusy(BusyAnimParts.Busy);
					PlaySoundForActBusy(BusyAnimParts.Busy);

					ForceActBusy = false;
					TeleportToBusy = false;
					UseNearestBusy = false;
					ForcedActivity = Activity.ACT_INVALID;

					if (UseRenderBoundsForCollision)
						ComputeAndSetRenderBounds();
				}
				break;

			case TASK_ACTBUSY_PLAY_ENTRY: {
					BusyAnim? busyAnim = g_ActBusyAnimDataSystem.GetBusyAnim(CurrentBusyAnim);

					if (busyAnim == null || !busyAnim.UseAutomovement)
						GetOuter()!.AddFlag(EntityFlags.Fly);

					GetOuter()!.SetGroundEntity(null);

					MovingToBusy = false;
					NeedsToPlayExitAnimValue = HasAnimForActBusy(CurrentBusyAnim, BusyAnimParts.Exit);

					if (!ActBusyNodeStillActive()) {
						TaskFail(AI_TaskFailureCode.NoHintNode);
						return;
					}

					if (Util.DistApprox(GetHintNode()!.GetAbsOrigin(), GetAbsOrigin()) > 16 || !GetOuter()!.FacingIdeal()) {
						Busy = false;
						TaskFail("Not correctly on hintnode");
						return;
					}

					PlaySoundForActBusy(BusyAnimParts.Entry);

					if (!PlayAnimForActBusy(BusyAnimParts.Entry))
						TaskComplete();
				}
				break;

			case TASK_ACTBUSY_VERIFY_EXIT: {
					if (!UseRenderBoundsForCollision) {
						TaskComplete();
						break;
					}

					GetOuter()!.SetWait(0);
				}
				break;

			case TASK_ACTBUSY_PLAY_EXIT: {
					if (UseRenderBoundsForCollision)
						GetOuter()!.SetHullSizeNormal(true);

					ActBusyGoal.Get()?.NPCStartedLeavingBusy(GetOuter()!);

					PlaySoundForActBusy(BusyAnimParts.Exit);

					if (!PlayAnimForActBusy(BusyAnimParts.Exit)) {
						NeedsToPlayExitAnimValue = false;
						GetOuter()!.RemoveFlag(EntityFlags.Fly);
						NotifyBusyEnding();
						TaskComplete();
					}
				}
				break;

			case TASK_ACTBUSY_TELEPORT_TO_BUSY: {
					if (!ActBusyNodeStillActive()) {
						TaskFail(AI_TaskFailureCode.NoHintNode);
						return;
					}

					Vector3 absOrigin = GetHintNode()!.GetAbsOrigin();
					QAngle absAngles = GetHintNode()!.GetAbsAngles();
					GetOuter()!.Teleport(absOrigin, absAngles, null);
					GetOuter()!.GetMotor()!.SetIdealYaw(absAngles.Y);

					TaskComplete();
				}
				break;

			case TASK_ACTBUSY_WALK_PATH_TO_BUSY: {
					if (ForcedActivity != Activity.ACT_INVALID && ForcedActivity != Activity.ACT_RESET) {
						GetNavigator()!.SetMovementActivity(ForcedActivity);

						GetOuter()!.Forget(AI_MemoryFlags.Incover);

						TaskComplete();
					}
					else {
						if (IsCombatActBusy())
							ChainStartTask(TASK_RUN_PATH);
						else
							ChainStartTask(TASK_WALK_PATH);
					}
					break;
				}

			case TASK_ACTBUSY_GET_PATH_TO_ACTBUSY: {
					ChainStartTask(TASK_GET_PATH_TO_HINTNODE);

					if (!HasCondition((int)SCOND_t.COND_TASK_FAILED)) {
						NumConsecutivePathFailures = 0;

						BusyAnim? busyAnim = g_ActBusyAnimDataSystem.GetBusyAnim(CurrentBusyAnim);
						if (busyAnim != null && busyAnim.Sequences[(int)BusyAnimParts.Entry] == null && busyAnim.Activities[(int)BusyAnimParts.Entry] == Activity.ACT_INVALID) {
							if (busyAnim.Sequences[(int)BusyAnimParts.Busy] != null)
								GetNavigator()!.SetArrivalSequence(GetOuter()!.LookupSequence(busyAnim.Sequences[(int)BusyAnimParts.Busy]));
							else if (busyAnim.Activities[(int)BusyAnimParts.Busy] != Activity.ACT_INVALID)
								GetNavigator()!.SetArrivalActivity(busyAnim.Activities[(int)BusyAnimParts.Busy]);
						}
						else {
							if (busyAnim!.Sequences[(int)BusyAnimParts.Entry] != null)
								GetNavigator()!.SetArrivalSequence(GetOuter()!.LookupSequence(busyAnim.Sequences[(int)BusyAnimParts.Entry]));
							else if (busyAnim.Activities[(int)BusyAnimParts.Entry] != Activity.ACT_INVALID)
								GetNavigator()!.SetArrivalActivity(busyAnim.Activities[(int)BusyAnimParts.Entry]);
						}
					}
					else
						NumConsecutivePathFailures++;

					break;
				}

			default:
				base.StartTask(task);
				break;
		}
	}

	protected override void RunTask(in Task_t task) {
		switch (task.Task) {
			case TASK_WAIT_FOR_MOVEMENT: {
					if (IsCurSchedule(SCHED_ACTBUSY_START_BUSYING)) {
						if (!ActBusyNodeStillActive()) {
							TaskFail(AI_TaskFailureCode.NoHintNode);
							return;
						}
					}

					base.RunTask(task);
					break;
				}

			case TASK_ACTBUSY_PLAY_BUSY_ANIM: {
					if (UseRenderBoundsForCollision) {
						if (GetOuter()!.IsSequenceFinished() && NeedToSetBounds) {
							ComputeAndSetRenderBounds();
							NeedToSetBounds = false;
						}
					}

					if (IsCombatActBusy()) {
						if (GetEnemy() != null && !HasCondition((int)SCOND_t.COND_ENEMY_OCCLUDED)) {
							float distSqr = Vector3.DistanceSquared(GetAbsOrigin(), GetEnemy()!.GetAbsOrigin());

							if (distSqr < (12.0f * 15.0f) * (12.0f * 15.0f)) {
								EndBusyAt = gpGlobals.CurTime;
								TaskComplete();
								return;
							}
						}
					}

					GetOuter()!.AutoMovement();
					if (!ActBusyNodeStillActive() || GetOuter()!.IsWaitFinished())
						TaskComplete();
					else {
						if (GetOuter() is AI_PlayerAlly ally)
							ally.SelectInterjection();

						if (HasCondition(COND_ACTBUSY_LOST_SEE_ENTITY)) {
							StopBusying();
							TaskComplete();
						}
					}
					break;
				}

			case TASK_ACTBUSY_PLAY_ENTRY: {
					GetOuter()!.AutoMovement();
					if (!ActBusyNodeStillActive() || GetOuter()!.IsSequenceFinished())
						TaskComplete();
				}
				break;

			case TASK_ACTBUSY_VERIFY_EXIT: {
					if (GetOuter()!.IsWaitFinished()) {
						TraceFilterOnlyNPCsAndPlayer filter = new(GetOuter(), Source.CollisionGroup.None);
						Util.TraceHull(GetOuter()!.GetAbsOrigin(), GetOuter()!.GetAbsOrigin(), NAI_Hull.Mins(AI_HullType.Human), NAI_Hull.Maxs(AI_HullType.Human), Mask.NPCSolid, ref filter, out Trace tr);

						if (tr.StartSolid)
							GetOuter()!.SetWait(1.0f);
						else {
							BaseEntity? blocker = EntityBlocker.Create(GetOuter()!.GetAbsOrigin(), NAI_Hull.Mins(AI_HullType.Human), NAI_Hull.Maxs(AI_HullType.Human), GetOuter(), true);
							g_EventQueue.AddEvent(blocker, "Kill", 1.0f, GetOuter(), GetOuter());
							TaskComplete();
						}
					}
				}
				break;

			case TASK_ACTBUSY_PLAY_EXIT: {
					GetOuter()!.AutoMovement();
					if (GetOuter()!.IsSequenceFinished()) {
						NeedsToPlayExitAnimValue = false;
						GetOuter()!.RemoveFlag(EntityFlags.Fly);
						NotifyBusyEnding();
						TaskComplete();
					}
				}
				break;

			default:
				base.RunTask(task);
				break;
		}
	}

	void NotifyBusyEnding() {
		AutoFireWeapon = false;

		if (GetHintNode() != null) {
			if (Busy || MovingToBusy)
				GetHintNode()!.NPCStoppedUsing(GetOuter()!);

			GetHintNode()!.Unlock();

			if (IsCombatActBusy())
				GetHintNode()!.DisableForSeconds(random.RandomFloat(10, 15));

			SetHintNode(null);
		}

		if (Busy) {
			Busy = false;

			AI_ActBusyGoal? goal = ActBusyGoal.Get();
			if (goal != null) {
				goal.NPCFinishedBusy(GetOuter()!);

				if (ExitedBusyToDueLostSeeEntity) {
					goal.NPCLostSeeEntity(GetOuter()!);
					ExitedBusyToDueLostSeeEntity = false;
				}

				if (ExitedBusyToDueSeeEnemy) {
					goal.NPCSeeEnemy(GetOuter()!);
					ExitedBusyToDueSeeEnemy = false;
				}
			}
		}
		else if (MovingToBusy && ActBusyGoal.Get() != null)
			ActBusyGoal.Get()!.NPCAbortedMoveTo(GetOuter()!);

		EndBusyAt = 0;

		if (IsCombatActBusy())
			NextBusySearchTime = gpGlobals.CurTime;
		else
			NextBusySearchTime = gpGlobals.CurTime + random.RandomFloat(ai_actbusy_search_time.GetFloat(), ai_actbusy_search_time.GetFloat() * 2);
	}

}

[LinkEntityToClass("ai_goal_actbusy")]
public class AI_ActBusyGoal : AI_GoalEntity
{
	public static readonly new DataMap DataDesc = new(typeof(AI_ActBusyGoal), AI_GoalEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(BusySearchRange), FieldType.Float, "busysearchrange"),
		DEFINE.KEYFIELD(nameof(VisibleOnly), FieldType.Boolean, "visibleonly"),
		DEFINE.KEYFIELD(nameof(BusyType), FieldType.Integer, "type"),
		DEFINE.KEYFIELD(nameof(AllowCombatActBusyTeleport), FieldType.Boolean, "allowteleport"),
		DEFINE.KEYFIELD(nameof(SeeEntityName), FieldType.String, "SeeEntity"),
		DEFINE.KEYFIELD(nameof(SeeEntityTimeout), FieldType.Float, "SeeEntityTimeout"),
		DEFINE.KEYFIELD(nameof(SafeZoneVolume), FieldType.String, "SafeZone"),
		DEFINE.KEYFIELD(nameof(SightMethod), FieldType.Integer, "sightmethod"),

		DEFINE.INPUTFUNC(FieldType.Float, "SetBusySearchRange", nameof(InputSetBusySearchRange), (INPUTFUNCPTR)((self, data) => ((AI_ActBusyGoal)self).InputSetBusySearchRange(data))),
		DEFINE.INPUTFUNC(FieldType.String, "ForceNPCToActBusy", nameof(InputForceNPCToActBusy), (INPUTFUNCPTR)((self, data) => ((AI_ActBusyGoal)self).InputForceNPCToActBusy(data))),
		DEFINE.INPUTFUNC(FieldType.EHandle, "ForceThisNPCToActBusy", nameof(InputForceThisNPCToActBusy), (INPUTFUNCPTR)((self, data) => ((AI_ActBusyGoal)self).InputForceThisNPCToActBusy(data))),
		DEFINE.INPUTFUNC(FieldType.EHandle, "ForceThisNPCToLeave", nameof(InputForceThisNPCToLeave), (INPUTFUNCPTR)((self, data) => ((AI_ActBusyGoal)self).InputForceThisNPCToLeave(data))),

		DEFINE.OUTPUT(nameof(OnNPCStartedBusy), "OnNPCStartedBusy", eventFuncs),
		DEFINE.OUTPUT(nameof(OnNPCFinishedBusy), "OnNPCFinishedBusy", eventFuncs),
		DEFINE.OUTPUT(nameof(OnNPCLeft), "OnNPCLeft", eventFuncs),
		DEFINE.OUTPUT(nameof(OnNPCLostSeeEntity), "OnNPCLostSeeEntity", eventFuncs),
		DEFINE.OUTPUT(nameof(OnNPCSeeEnemy), "OnNPCSeeEnemy", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	protected float BusySearchRange;
	protected bool VisibleOnly;
	protected ActBusyType BusyType;
	protected bool AllowCombatActBusyTeleport;

	public string? SeeEntityName;
	public float SeeEntityTimeout;
	public string? SafeZoneVolume;
	public ActBusySightMethod SightMethod;

	protected readonly OutputEHANDLE OnNPCStartedBusy = new();
	protected readonly OutputEHANDLE OnNPCFinishedBusy = new();
	protected readonly OutputEHANDLE OnNPCLeft = new();
	protected readonly OutputEHANDLE OnNPCLostSeeEntity = new();
	protected readonly OutputEHANDLE OnNPCSeeEnemy = new();

	public AI_ActBusyGoal() {
		SeeEntityTimeout = 1;
	}

	public ActBusyType GetBusyType() => BusyType;
	public bool IsCombatActBusyTeleportAllowed() => AllowCombatActBusyTeleport;

	protected static AI_ActBusyBehavior? GetBusyBehaviorForNPC(BaseEntity? entity, ReadOnlySpan<char> inputName) {
		if (entity is not AI_BaseNPC actor) {
			Msg($"ai_goal_actbusy input {inputName} fired targeting an entity that isn't an NPC.\n");
			return null;
		}

		if (!actor.GetBehavior(out AI_ActBusyBehavior? behavior)) {
			Msg($"ai_goal_actbusy input {inputName} fired on an NPC that doesn't support ActBusy behavior.\n");
			return null;
		}

		return behavior;
	}

	protected static AI_ActBusyBehavior? GetBusyBehaviorForNPC(ReadOnlySpan<char> actorName, BaseEntity? activator, BaseEntity? caller, ReadOnlySpan<char> inputName) {
		BaseEntity? entity = gEntList.FindEntityByName(null, actorName, null, activator, caller);
		if (entity == null) {
			Msg($"ai_goal_actbusy input {inputName} fired targeting a non-existant entity ({actorName}).\n");
			return null;
		}

		return GetBusyBehaviorForNPC(entity, inputName);
	}

	protected override void EnableGoal(AI_BaseNPC? ai) {
		base.EnableGoal(ai);

		if (ai!.GetBehavior(out AI_ActBusyBehavior? behavior)) {
			if (!behavior!.IsEnabled())
				behavior.Enable(this, BusySearchRange, VisibleOnly);
		}
		else {
			DevMsg($"ActBusy goal entity activated for an NPC ({ai.GetDebugName()}) that doesn't have the ActBusy behavior\n");
			return;
		}
	}

	public override void InputActivate(InputData inputdata) {
		if (AI_ActBusyBehavior.ai_debug_actbusy.GetInt() == 4)
			Msg($"ACTBUSY: Actbusy goal {GetClassname()} ({GetDebugName()}) activated.\n");

		base.InputActivate(inputdata);
	}

	public override void InputDeactivate(InputData inputdata) {
		if (AI_ActBusyBehavior.ai_debug_actbusy.GetInt() == 4)
			Msg($"ACTBUSY: Actbusy goal {GetClassname()} ({GetDebugName()}) disabled.\n");

		base.InputDeactivate(inputdata);

		for (int i = 0; i < NumActors(); i++) {
			AI_BaseNPC? actor = GetActor(i);

			if (actor != null) {
				if (actor.GetBehavior(out AI_ActBusyBehavior? behavior))
					behavior!.Disable();
				else {
					DevMsg("ActBusy goal entity deactivated for an NPC that doesn't have the ActBusy behavior\n");
					return;
				}
			}
		}
	}

	void InputSetBusySearchRange(InputData inputdata) {
		BusySearchRange = inputdata.Value.Float();

		for (int i = 0; i < NumActors(); i++) {
			AI_BaseNPC? actor = GetActor(i);

			if (actor != null) {
				if (actor.GetBehavior(out AI_ActBusyBehavior? behavior))
					behavior!.SetBusySearchRange(BusySearchRange);
			}
		}
	}

	void InputForceNPCToActBusy(InputData inputdata) {
		string parseString = new(inputdata.Value.String());

		AI_Hint? hintNode = null;
		float maxTime = AI_ActBusyBehavior.NO_MAX_TIME;
		bool teleport = false;
		bool useNearestBusy = false;
		BaseEntity? seeEntity = null;

		string[] tokens = parseString.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		int tokenIndex = 0;
		string? NextToken() => tokenIndex < tokens.Length ? tokens[tokenIndex++] : null;

		string? param = NextToken();
		AI_ActBusyBehavior? behavior = GetBusyBehaviorForNPC(param, inputdata.Activator, inputdata.Caller, "InputForceNPCToActBusy");
		if (behavior == null)
			return;

		bool episodicBugFix = hl2_episodic.GetBool();

		param = NextToken();
		if (param != null) {
			BaseEntity? entity = gEntList.FindEntityByName(null, param, null, inputdata.Activator, inputdata.Caller);
			if (entity != null) {
				hintNode = entity as AI_Hint;
				if (hintNode == null) {
					Msg("ai_goal_actbusy input ForceNPCToActBusy fired targeting an entity that isn't a hintnode.\n");
					return;
				}

				if (episodicBugFix)
					param = NextToken();
			}
		}

		Activity activity = Activity.ACT_INVALID;

		if (!episodicBugFix)
			param = NextToken();

		while (param != null) {
			if (param.StartsWith("teleport"))
				teleport = true;
			else if (param.StartsWith("nearest"))
				useNearestBusy = true;
			else if (param.StartsWith("see:"))
				seeEntity = gEntList.FindEntityByName(null, param[4..]);
			else if (param[0] == '$') {
				string animName = param[1..];
				activity = (Activity)ActivityList.IndexForName(animName);
				if (activity == Activity.ACT_INVALID) {
					behavior.GetOuter()!.SceneCustomMoveSeq = animName;
					activity = Activity.ACT_SCRIPT_CUSTOM_MOVE;
				}
			}
			else
				maxTime = (float)atof(param);

			param = NextToken();
		}

		if (AI_ActBusyBehavior.ai_debug_actbusy.GetInt() == 4)
			Msg($"ACTBUSY: Actbusy goal {GetClassname()} ({GetDebugName()}) ForceNPCToActBusy input with data: {parseString}.\n");

		behavior.SetBusySearchRange(BusySearchRange);
		behavior.ForceActBusyNow(this, hintNode, maxTime, VisibleOnly, teleport, useNearestBusy, seeEntity, activity);
	}

	void InputForceThisNPCToActBusy(InputData inputdata) {
		AI_ActBusyBehavior? behavior = GetBusyBehaviorForNPC(inputdata.Value.Entity().Get(), "InputForceThisNPCToActBusy");
		if (behavior == null)
			return;

		behavior.SetBusySearchRange(BusySearchRange);
		behavior.ForceActBusyNow(this);
	}

	void InputForceThisNPCToLeave(InputData inputdata) {
		AI_ActBusyBehavior? behavior = GetBusyBehaviorForNPC(inputdata.Value.Entity().Get(), "InputForceThisNPCToLeave");
		if (behavior == null)
			return;

		behavior.SetBusySearchRange(BusySearchRange);
		behavior.ForceActBusyLeave();
	}

	public virtual void NPCMovingToBusy(AI_BaseNPC npc) { }

	public virtual void NPCStartedBusy(AI_BaseNPC npc) {
		EHANDLE hNPC = new();
		hNPC.Set(npc);
		OnNPCStartedBusy.Set(hNPC, npc, this);
	}

	public virtual void NPCStartedLeavingBusy(AI_BaseNPC npc) { }

	public virtual void NPCAbortedMoveTo(AI_BaseNPC npc) { }

	public virtual void NPCFinishedBusy(AI_BaseNPC npc) {
		EHANDLE hNPC = new();
		hNPC.Set(npc);
		OnNPCFinishedBusy.Set(hNPC, npc, this);
	}

	public virtual void NPCLeft(AI_BaseNPC npc) {
		EHANDLE hNPC = new();
		hNPC.Set(npc);
		OnNPCLeft.Set(hNPC, npc, this);
	}

	public virtual void NPCLostSeeEntity(AI_BaseNPC npc) {
		EHANDLE hNPC = new();
		hNPC.Set(npc);
		OnNPCLostSeeEntity.Set(hNPC, npc, this);
	}

	public virtual void NPCSeeEnemy(AI_BaseNPC npc) {
		EHANDLE hNPC = new();
		hNPC.Set(npc);
		OnNPCSeeEnemy.Set(hNPC, npc, this);
	}
}

[LinkEntityToClass("ai_goal_actbusy_queue")]
public class AI_ActBusyQueueGoal : AI_ActBusyGoal
{
	public const int MAX_QUEUE_NODES = 20;

	const string QUEUE_THINK_CONTEXT = "ActBusyQueueThinkContext";
	const string QUEUE_MOVEUP_THINK_CONTEXT = "ActBusyQueueMoveUpThinkContext";

	public static readonly new DataMap DataDesc = new(typeof(AI_ActBusyQueueGoal), AI_ActBusyGoal.DataDesc, [
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(ForceReachFront), FieldType.Boolean, "mustreachfront"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node01), FieldType.String, "node01"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node02), FieldType.String, "node02"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node03), FieldType.String, "node03"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node04), FieldType.String, "node04"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node05), FieldType.String, "node05"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node06), FieldType.String, "node06"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node07), FieldType.String, "node07"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node08), FieldType.String, "node08"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node09), FieldType.String, "node09"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node10), FieldType.String, "node10"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node11), FieldType.String, "node11"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node12), FieldType.String, "node12"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node13), FieldType.String, "node13"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node14), FieldType.String, "node14"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node15), FieldType.String, "node15"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node16), FieldType.String, "node16"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node17), FieldType.String, "node17"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node18), FieldType.String, "node18"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node19), FieldType.String, "node19"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(Node20), FieldType.String, "node20"),
		DEFINE<AI_ActBusyQueueGoal>.KEYFIELD(nameof(ExitNodeName), FieldType.String, "node_exit"),

		DEFINE<AI_ActBusyQueueGoal>.INPUTFUNC(FieldType.Integer, "PlayerStartedBlocking", nameof(InputPlayerStartedBlocking), (INPUTFUNCPTR)((self, data) => ((AI_ActBusyQueueGoal)self).InputPlayerStartedBlocking(data))),
		DEFINE<AI_ActBusyQueueGoal>.INPUTFUNC(FieldType.Integer, "PlayerStoppedBlocking", nameof(InputPlayerStoppedBlocking), (INPUTFUNCPTR)((self, data) => ((AI_ActBusyQueueGoal)self).InputPlayerStoppedBlocking(data))),
		DEFINE<AI_ActBusyQueueGoal>.INPUTFUNC(FieldType.Void, "MoveQueueUp", nameof(InputMoveQueueUp), (INPUTFUNCPTR)((self, data) => ((AI_ActBusyQueueGoal)self).InputMoveQueueUp(data))),

		DEFINE<AI_ActBusyQueueGoal>.OUTPUT(nameof(OnQueueMoved), "OnQueueMoved", eventFuncs),
		DEFINE<AI_ActBusyQueueGoal>.OUTPUT(nameof(OnNPCLeftQueue), "OnNPCLeftQueue", eventFuncs),
		DEFINE<AI_ActBusyQueueGoal>.OUTPUT(nameof(OnNPCStartedLeavingQueue), "OnNPCStartedLeavingQueue", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	int CurrentQueueCount;
	Handle<AI_Hint>[] Nodes = new Handle<AI_Hint>[MAX_QUEUE_NODES];
	bool[] PlayerBlockedNodes = new bool[MAX_QUEUE_NODES];
	EHANDLE ExitNode = new();
	EHANDLE ExitingNPC = new();
	bool ForceReachFront;

	string? Node01, Node02, Node03, Node04, Node05, Node06, Node07, Node08, Node09, Node10,
		Node11, Node12, Node13, Node14, Node15, Node16, Node17, Node18, Node19, Node20;
	string? ExitNodeName;

	readonly OutputInt OnQueueMoved = new();
	readonly OutputEHANDLE OnNPCLeftQueue = new();
	readonly OutputEHANDLE OnNPCStartedLeavingQueue = new();

	string? NodeName(int i) => i switch {
		0 => Node01, 1 => Node02, 2 => Node03, 3 => Node04, 4 => Node05, 5 => Node06, 6 => Node07, 7 => Node08, 8 => Node09, 9 => Node10,
		10 => Node11, 11 => Node12, 12 => Node13, 13 => Node14, 14 => Node15, 15 => Node16, 16 => Node17, 17 => Node18, 18 => Node19, 19 => Node20,
		_ => null
	};

	public override void Spawn() {
		base.Spawn();

		RegisterThinkContext(QUEUE_MOVEUP_THINK_CONTEXT);
	}

	public override void DrawDebugGeometryOverlays() {
		base.DrawDebugGeometryOverlays();

		for (int i = 0; i < MAX_QUEUE_NODES; i++) {
			if (Nodes[i].Get() == null)
				continue;
			if (PlayerBlockedNodes[i])
				DebugOverlay.Box(Nodes[i].Get()!.GetAbsOrigin(), -new Vector3(5, 5, 5), new Vector3(5, 5, 5), 255, 0, 0, 0, 0.1f);
			else
				DebugOverlay.Box(Nodes[i].Get()!.GetAbsOrigin(), -new Vector3(5, 5, 5), new Vector3(5, 5, 5), 255, 255, 255, 0, 0.1f);
		}
	}

	public override void InputActivate(InputData inputdata) {
		if (!IsActive()) {
			for (int i = 0; i < MAX_QUEUE_NODES; i++) {
				string? nodeName = NodeName(i);
				if (nodeName == null) {
					Nodes[i].Set(null);
					continue;
				}

				BaseEntity? entity = gEntList.FindEntityByName(null, nodeName);
				if (entity == null) {
					Warning($"Unable to find ai_goal_actbusy_queue {GetEntityName()}'s node {i}: {nodeName}\n");
					Util.Remove(this);
					return;
				}
				Nodes[i].Set(entity as AI_Hint);
				if (Nodes[i].Get() == null) {
					Warning($"ai_goal_actbusy_queue {GetEntityName()}'s node {i}: '{nodeName}' is not an ai_hint.\n");
					Util.Remove(this);
					return;
				}

				if (i == 0)
					Nodes[i].Get()!.SetDisabled(false);
				else
					Nodes[i].Get()!.SetDisabled(true);
			}

			ExitNode.Set(gEntList.FindEntityByName(null, ExitNodeName));
			if (ExitNode.Get() == null) {
				Warning($"Unable to find ai_goal_actbusy_queue {GetEntityName()}'s exit node: {ExitNodeName}\n");
				Util.Remove(this);
				return;
			}

			RecalculateQueueCount();

			SetContextThink(QueueThink, gpGlobals.CurTime + 5, QUEUE_THINK_CONTEXT);
		}

		base.InputActivate(inputdata);
	}

	public void RecalculateQueueCount() {
		int count = 0;
		for (int i = 0; i < MAX_QUEUE_NODES; i++) {
			if (NodeIsOccupied(i) || PlayerBlockedNodes[i])
				count = i + 1;
		}

		if (count == CurrentQueueCount)
			return;

		for (int i = 0; i < MAX_QUEUE_NODES; i++) {
			AI_Hint? node = Nodes[i].Get();
			if (node != null) {
				if (i > count)
					node.SetDisabled(true);
				else {
					node.SetDisabled(false);

					if (i == count)
						node.SetHintType(Hint.WorldWorkPosition);
					else
						node.SetHintType(Hint.None);
				}
			}
		}

		CurrentQueueCount = count;
		OnQueueMoved.Set(CurrentQueueCount, this, this);
	}

	public void InputPlayerStartedBlocking(InputData inputdata) {
		int node = inputdata.Value.Int() - 1;
		Assert(node >= 0 && node < MAX_QUEUE_NODES);

		PlayerBlockedNodes[node] = true;

		AI_BaseNPC? npc = GetNPCOnNode(node);
		PushNPCBackInQueue(npc, node);

		RecalculateQueueCount();
	}

	public void PushNPCBackInQueue(AI_BaseNPC? npc, int startingNode) {
		while (npc != null) {
			AI_ActBusyBehavior behavior = GetQueueBehaviorForNPC(npc)!;
			behavior.StopBusying();

			for (int next = startingNode + 1; next < MAX_QUEUE_NODES; next++) {
				if (!PlayerBlockedNodes[next]) {
					AI_BaseNPC? targetNPC = GetNPCOnNode(next);
					if (targetNPC != null) {
						AI_ActBusyBehavior targetBehavior = GetQueueBehaviorForNPC(targetNPC)!;
						targetBehavior.StopBusying();
					}

					behavior.ForceActBusyNow(this, Nodes[next].Get());

					npc = targetNPC;
					startingNode = next;
					break;
				}
			}
		}
	}

	public void InputPlayerStoppedBlocking(InputData inputdata) {
		int node = inputdata.Value.Int() - 1;
		Assert(node >= 0 && node < MAX_QUEUE_NODES);

		PlayerBlockedNodes[node] = false;

		RecalculateQueueCount();
		MoveQueueUp();
	}

	public void InputMoveQueueUp(InputData inputdata) {
		AI_BaseNPC? npc = null;
		for (int i = 0; i < MAX_QUEUE_NODES; i++) {
			npc = GetNPCOnNode(i);
			if (npc != null) {
				AI_ActBusyBehavior behavior = GetQueueBehaviorForNPC(npc)!;
				if (!behavior.IsMovingToBusy() || !ForceReachFront)
					break;

				npc = null;
			}

			if (ForceReachFront)
				break;
		}

		if (npc != null) {
			AI_ActBusyBehavior behavior = GetQueueBehaviorForNPC(npc)!;
			behavior.Disable();
			ExitingNPC.Set(npc);
		}
	}

	public override void NPCMovingToBusy(AI_BaseNPC npc) {
		base.NPCMovingToBusy(npc);
		RecalculateQueueCount();
	}

	public override void NPCStartedBusy(AI_BaseNPC npc) {
		base.NPCStartedBusy(npc);
		MoveQueueUp();
	}

	public void MoveQueueUp() {
		if (GetNextThink(QUEUE_MOVEUP_THINK_CONTEXT) < gpGlobals.CurTime) {
			TimeUnit_t time = gpGlobals.CurTime + random.RandomFloat(0.3f, 0.5f);
			SetContextThink(MoveQueueUpThink, time, QUEUE_MOVEUP_THINK_CONTEXT);
		}
	}

	public void MoveQueueUpThink() {
		for (int emptyNode = 0; emptyNode < (MAX_QUEUE_NODES - 1); emptyNode++) {
			if (!NodeIsOccupied(emptyNode) && !PlayerBlockedNodes[emptyNode]) {
				for (int next = emptyNode + 1; next < MAX_QUEUE_NODES; next++) {
					if (PlayerBlockedNodes[next])
						break;

					AI_BaseNPC? npc = GetNPCOnNode(next);
					if (npc != null) {
						AI_ActBusyBehavior behavior = GetQueueBehaviorForNPC(npc)!;

						behavior.ForceActBusyNow(this, Nodes[emptyNode].Get());
						break;
					}
				}
			}
		}
	}

	public override void NPCAbortedMoveTo(AI_BaseNPC npc) {
		base.NPCAbortedMoveTo(npc);

		RemoveNPCFromQueue(npc);
	}

	public override void NPCFinishedBusy(AI_BaseNPC npc) {
		base.NPCFinishedBusy(npc);

		if (ExitingNPC.Get() == npc) {
			npc.ScheduledMoveToGoalEntity(SCHED_IDLE_WALK, ExitNode.Get()!, Activity.ACT_WALK);
			OnNPCLeftQueue.Set(npc.GetRefEHandle(), npc, this);
			ExitingNPC.Set(null);
		}

		RemoveNPCFromQueue(npc);
	}

	public override void NPCStartedLeavingBusy(AI_BaseNPC npc) {
		base.NPCStartedLeavingBusy(npc);

		if (ExitingNPC.Get() == npc)
			OnNPCStartedLeavingQueue.Set(npc.GetRefEHandle(), npc, this);
	}

	public void RemoveNPCFromQueue(AI_BaseNPC npc) {
		RecalculateQueueCount();

		MoveQueueUp();
	}

	public void QueueThink() {
		if (GetNPCOnNode(0) == null)
			MoveQueueUp();

		SetContextThink(QueueThink, gpGlobals.CurTime + 5, QUEUE_THINK_CONTEXT);
	}

	public bool NodeIsOccupied(int i) => Nodes[i].Get() != null && !Nodes[i].Get()!.IsDisabled() && Nodes[i].Get()!.IsLocked();

	public AI_BaseNPC? GetNPCOnNode(int node) {
		if (Nodes[node].Get() == null)
			return null;

		return Nodes[node].Get()!.User() as AI_BaseNPC;
	}

	public AI_ActBusyBehavior? GetQueueBehaviorForNPC(AI_BaseNPC npc) {
		npc.GetBehavior(out AI_ActBusyBehavior? behavior);
		Assert(behavior != null);
		return behavior;
	}
}
