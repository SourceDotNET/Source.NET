global using static Game.Server.AI_ScheduleGlobals;

using Source;

namespace Game.Server;

public static class AI_ScheduleGlobals
{
	public static readonly AI_SchedulesManager g_AI_SchedulesManager = new();
}

/// <summary>
/// Analog of goalType_t in C++. (capitalization matters!!)
/// </summary>
public enum ScheduleGoalType
{
	None = -1,
	Enemy,
	Target,
	EnemyLKP,
	SavedPosition,
}

public enum PathType
{
	None = -1,
	Travel,
	LOS,
	Cover,
}

public class AI_SchedulesManager
{
	public AI_SchedulesManager() {
		allSchedules = null;
		CurLoadSig = 0;
	}

	public int GetScheduleLoadSignature() => CurLoadSig;

	int CurLoadSig;
	AI_Schedule? allSchedules;

	public void DestroyStringRegistries() {
		AI_BaseNPC.GetSchedulingSymbols().Clear();

		AI_BaseNPC.ActivitySR = null;
		AI_BaseNPC.NumActivities = 0;
	}

	public void CreateStringRegistries() {
		AI_BaseNPC.GetSchedulingSymbols().Clear();

		AI_BaseNPC.ActivitySR = new();
		AI_BaseNPC.EventSR = new();
	}

	public bool LoadAllSchedules() {
		if (allSchedules == null) {
			AI_BaseNPC.InitSchedulingTables();
			if (!AI_BaseNPC.LoadDefaultSchedules()) {
				AI_BaseNPC.DebugBits |= AI_DebugFlags.DisableAI;
				DevMsg("ERROR:  Mistake in default schedule definitions, AI Disabled.\n");
			}
		}
		return true;
	}

	AI_Schedule CreateSchedule(ReadOnlySpan<char> name, int scheduleID) {
		AI_Schedule sched = new(name, scheduleID, allSchedules);
		allSchedules = sched;

		return sched;
	}

	public bool DefineSchedules(ReadOnlySpan<char> prefix, AI_ClassScheduleIdSpace idSpace, ReadOnlySpan<AI_ScheduleDef> schedules) {
		for (int i = 0; i < schedules.Length; i++) {
			if (!DefineSchedule(prefix, idSpace, schedules[i]))
				return false;
		}

		return true;
	}

	bool DefineSchedule(ReadOnlySpan<char> prefix, AI_ClassScheduleIdSpace idSpace, in AI_ScheduleDef schedule) {
		int scheduleLocalID = schedule.Schedule;
		int scheduleID = idSpace.ScheduleLocalToGlobal(scheduleLocalID);
		string? name = scheduleID != -1 ? AI_BaseNPC.GetSchedulingSymbols().ScheduleIdToSymbol(scheduleID) : null;
		if (name == null) {
			DevMsg($"ERROR: LoadSchd ({prefix}): Unknown schedule type ({scheduleLocalID})\n");
			return true;
		}

		if (GetScheduleByName(name) != null) {
			DevMsg($"ERROR: file contains a schedule ({name}) that has already been defined!\n");
			DevMsg("       Aborting schedule load.\n");
			Assert(false);
			return false;
		}

		AI_Schedule newSchedule = CreateSchedule(name, scheduleID);

		AI_TaskDef[] tasks = schedule.Tasks;
		Task_t[] taskList = new Task_t[tasks.Length];

		for (int i = 0; i < tasks.Length; i++) {
			if (idSpace.TaskLocalToGlobal(tasks[i].Task) == -1) {
				DevMsg($"ERROR: LoadSchd ({prefix}): ({newSchedule.GetName()}) Unknown task {tasks[i].Task}!\n");
				Assert(false);
				return false;
			}

			taskList[i].Task = tasks[i].Task;
			taskList[i].TaskData = tasks[i].Data;

			if (tasks[i].ActivityName != null) {
				taskList[i].TaskData = AI_BaseNPC.GetActivityID(tasks[i].ActivityName);
				if (taskList[i].TaskData == -1) {
					DevMsg($"ERROR: LoadSchd ({prefix}): ({newSchedule.GetName()}) Unknown activity {tasks[i].ActivityName}!\n");
					Assert(false);
					return false;
				}
			}
		}

		newSchedule.NumTasksValue = taskList.Length;
		newSchedule.TaskList = taskList;

		AI_ScheduleCondition[] interrupts = schedule.Interrupts ?? [];
		for (int i = 0; i < interrupts.Length; i++) {
			int condID = idSpace.ConditionLocalToGlobal(interrupts[i].Condition);

			if (condID == -1) {
				DevMsg($"ERROR: LoadSchd ({prefix}): Schedule ({newSchedule.GetName()}), Unknown condition {interrupts[i].Condition}!\n");
				Assert(false);
			}
			else {
				int interrupt = AI_RemapFromGlobal(condID);
				Assert(AI_IdIsGlobal(condID) && interrupt >= 0 && interrupt < MAX_CONDITIONS);
				newSchedule.InterruptMask.Set(interrupt);
			}
		}

		return true;
	}

	public AI_Schedule? GetScheduleFromID(int schedID) {
		for (AI_Schedule? schedule = allSchedules; schedule != null; schedule = schedule.NextSchedule) {
			if (schedule.ScheduleID == schedID)
				return schedule;
		}

		DevMsg($"Couldn't find schedule ({AI_BaseNPC.GetSchedulingSymbols().ScheduleIdToSymbol(schedID)})\n");

		return null;
	}

	public AI_Schedule? GetScheduleByName(ReadOnlySpan<char> name) {
		for (AI_Schedule? schedule = allSchedules; schedule != null; schedule = schedule.NextSchedule) {
			if (FStrEq(schedule.GetName(), name))
				return schedule;
		}

		return null;
	}

	public void DeleteAllSchedules() {
		CurLoadSig++;

		if (CurLoadSig < 0)
			CurLoadSig = 0;

		allSchedules = null;
	}
}

public readonly struct AI_ScheduleCondition(int condition)
{
	public readonly int Condition = condition;

	public static implicit operator AI_ScheduleCondition(int condition) => new(condition);
	public static implicit operator AI_ScheduleCondition(SCOND_t condition) => new((int)condition);
}

public readonly record struct AI_ScheduleDef(int Schedule, AI_TaskDef[] Tasks, AI_ScheduleCondition[]? Interrupts = null);

public sealed class AI_CustomSchedules
{
	public required string ClassName { get; init; }
	public required AI_ClassScheduleIdSpace IdSpace { get; init; }
	public required AI_ClassScheduleIdSpace ParentIdSpace { get; init; }

	public (string Name, int LocalId)[] Schedules { get; init; } = [];
	public (string Name, int LocalId)[] Tasks { get; init; } = [];
	public (string Name, int LocalId)[] Conditions { get; init; } = [];

	public Action? Declare { get; init; }
	public AI_ScheduleDef[] Definitions { get; init; } = [];

	AI_SchedLoadStatus LoadStatus = new() { Valid = true, Signature = -1 };

	public bool Valid => LoadStatus.Valid;

	public bool Load(Func<bool> baseLoad) => AI_DoLoadSchedules(baseLoad, Init, ref LoadStatus);

	void Init() {
		Declare?.Invoke();

		IdSpace.Init(ClassName, AI_BaseNPC.GetSchedulingSymbols(), ParentIdSpace);

		foreach ((string name, int localId) in SortedByLocalId(Schedules)) {
			if (!IdSpace.AddSchedule(name, localId, ClassName))
				return;
		}

		foreach ((string name, int localId) in SortedByLocalId(Tasks)) {
			if (!IdSpace.AddTask(name, localId, ClassName))
				return;
		}

		foreach ((string name, int localId) in SortedByLocalId(Conditions)) {
			if (ValidateConditionLimits(name)) {
				if (!IdSpace.AddCondition(name, localId, ClassName))
					return;
			}
		}

		if (LoadStatus.Valid)
			LoadStatus.Valid = g_AI_SchedulesManager.DefineSchedules(ClassName, IdSpace, Definitions);
	}

	static (string Name, int LocalId)[] SortedByLocalId((string Name, int LocalId)[] symbols) {
		(string Name, int LocalId)[] sorted = [.. symbols];
		Array.Sort(sorted, static (left, right) => left.LocalId - right.LocalId);
		return sorted;
	}
}

public class AI_Schedule
{
	public int GetId() => ScheduleID;

	public Task_t[]? GetTaskList() => TaskList;

	public int NumTasks() => NumTasksValue;

	public void GetInterruptMask(out AI_ScheduleBits bits) => bits = InterruptMask;

	public bool HasInterrupt(int condition) => InterruptMask.IsBitSet(condition);

	public string GetName() => Name;

	internal int ScheduleID;

	internal Task_t[]? TaskList;
	internal int NumTasksValue;

	internal AI_ScheduleBits InterruptMask;
	internal string Name;

	internal AI_Schedule? NextSchedule;

	internal AI_Schedule(ReadOnlySpan<char> name, int scheduleID, AI_Schedule? next) {
		ScheduleID = scheduleID;

		Name = new(name);

		TaskList = null;
		NumTasksValue = 0;

		NextSchedule = next;
	}
}
