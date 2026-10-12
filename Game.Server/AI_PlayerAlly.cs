using Game.Shared;

namespace Game.Server;

public class AI_PlayerAlly : AI_BaseActor
{
	public override EntityCapabilities ObjectCaps() => UsableNPCObjectCaps(base.ObjectCaps());

	public bool SelectInterjection() => false;

	public override int TranslateSchedule(int scheduleType) {
		switch (scheduleType) {
			case SCHED_CHASE_ENEMY_FAILED: {
					int baseType = base.TranslateSchedule(scheduleType);
					if (baseType != SCHED_CHASE_ENEMY_FAILED)
						return baseType;

					return SCHED_TAKE_COVER_FROM_ENEMY;
				}
		}
		return base.TranslateSchedule(scheduleType);
	}

	public override void StartTask(in Task_t task) {
		switch (task.Task) {
			case TASK_MOVE_AWAY_PATH:
				if (HasCondition((int)SCOND_t.COND_PLAYER_PUSHING))
					GetMotor()!.SetIdealYawToTarget(AI_GetClosestPlayer()!.WorldSpaceCenter());

				base.StartTask(task);
				break;

			default:
				base.StartTask(task);
				break;
		}
	}

	public override void Touch(BaseEntity? other) {
		base.Touch(other);

		if (other!.IsPlayer()) {
			if (HasMemory(AI_MemoryFlags.Provoked))
				return;

			if (GetExpresser()!.IsSpeaking())
				return;

			TestPlayerPushing(other);
		}
	}

	public override void Event_Killed(in TakeDamageInfo info) {
		if (IsInPlayerSquad()) {
			BasePlayer? player = AI_GetClosestPlayer();
			if (player != null) {
				Variant_t emptyVariant = default;
				player.AcceptInput("OnSquadMemberKilled", this, this, emptyVariant, 0);
			}
		}

		if (GetSpeechSemaphore(this).GetOwner() == this)
			GetSpeechSemaphore(this).Release();

		SetTarget(null);
		SentenceStop();
		FnUse = null;
		base.Event_Killed(info);
	}
}
