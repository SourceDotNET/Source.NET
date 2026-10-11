namespace Game.Server;

public abstract class AI_Behavior_AI_BaseNPC_100000 : AI_ComponentWithOuter_AI_BaseNPC_AI_BehaviorBase
{
	const int ID_SPACE_OFFSET = 100000;

	public const int NEXT_TASK = ID_SPACE_OFFSET;
	public const int NEXT_SCHEDULE = ID_SPACE_OFFSET;
	public const int NEXT_CONDITION = ID_SPACE_OFFSET;

	protected AI_Behavior_AI_BaseNPC_100000(AI_BaseNPC? outer = null) : base(outer) { }

	public void SetCondition(int condition) {
		if (condition >= ID_SPACE_OFFSET && condition < ID_SPACE_OFFSET + 10000)
			condition = GetClassScheduleIdSpace().ConditionLocalToGlobal(condition);
		GetOuter()!.SetCondition(condition);
	}

	public bool HasCondition(int condition) {
		if (condition >= ID_SPACE_OFFSET && condition < ID_SPACE_OFFSET + 10000)
			condition = GetClassScheduleIdSpace().ConditionLocalToGlobal(condition);
		return GetOuter()!.HasCondition(condition);
	}

	public bool HasInterruptCondition(int condition) {
		if (condition >= ID_SPACE_OFFSET && condition < ID_SPACE_OFFSET + 10000)
			condition = GetClassScheduleIdSpace().ConditionLocalToGlobal(condition);
		return GetOuter()!.HasInterruptCondition(condition);
	}

	public void ClearCondition(int condition) {
		if (condition >= ID_SPACE_OFFSET && condition < ID_SPACE_OFFSET + 10000)
			condition = GetClassScheduleIdSpace().ConditionLocalToGlobal(condition);
		GetOuter()!.ClearCondition(condition);
	}

	protected static AI_GlobalScheduleNamespace GetSchedulingSymbols() => AI_BaseNPC.GetSchedulingSymbols();

	protected static AI_ClassScheduleIdSpace AccessClassScheduleIdSpaceDirect() => AI_BaseNPC.ClassScheduleIdSpace;

	protected override bool IsBehaviorSchedule(int scheduleType) => scheduleType >= ID_SPACE_OFFSET && scheduleType < ID_SPACE_OFFSET + 10000;
}
