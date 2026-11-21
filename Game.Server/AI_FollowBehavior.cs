global using static Game.Server.AI_FollowBehaviorGlobals;

using Game.Shared;

using Source;

namespace Game.Server;

public static class AI_FollowBehaviorGlobals
{
	public static readonly AI_FollowManager g_AIFollowManager = new();
}

public enum AI_Formations
{
	Simple,
	Wide,
	Antlion,
	Commander,
	Tight,
	Medium,
	Sidekick,
	Hunter,
	Vortigaunt
}

public class AI_FollowGroup;

public struct AI_FollowManagerInfoHandle_t
{
	public AI_FollowGroup? Group;
	public int Follower;
}

public struct AI_FollowParams(AI_Formations formation = AI_Formations.Simple, bool normalMemoryDiscard = false)
{
	public AI_Formations Formation = formation;
	public bool NormalMemoryDiscard = normalMemoryDiscard;
}

public class AI_FollowManager
{
	public bool AddFollower(BaseEntity? target, AI_BaseNPC? follower, AI_Formations formation, ref AI_FollowManagerInfoHandle_t handle) => throw new NotImplementedException();
}

public class AI_FollowBehavior : AI_Behavior_AI_BaseNPC_100000
{
	public AI_FollowBehavior() {
		FollowDelay.Set(1.0f, 3.0f);
	}

	public override string GetName() => "Follow";

	public BaseEntity? GetFollowTarget() => FollowTarget.Get();

	public override bool CanSelectSchedule() {
		if (!GetOuter()!.IsInterruptable())
			return false;

		if (!ShouldFollow())
			return false;

		return true;
	}

	protected virtual bool ShouldFollow() {
		if (GetFollowTarget() == null)
			return false;

		if ((GetFollowTarget()!.GetFlags() & EntityFlags.NoTarget) != 0)
			return false;

		if (FollowNavFailed && FollowDelay.IsRunning() && !FollowDelay.Expired())
			return false;

		FollowNavFailed = false;

		return true;
	}

	public override void GatherConditions() => throw new NotImplementedException();

	public override void BeginScheduleSelection() => throw new NotImplementedException();
	public override void EndScheduleSelection() => throw new NotImplementedException();

	protected override void Precache() {
		if (FollowTarget.Get() != null && FollowManagerInfo.Group == null) {
			if (!g_AIFollowManager.AddFollower(FollowTarget.Get(), GetOuter(), Params.Formation, ref FollowManagerInfo))
				FollowTarget.Set(null);
		}
	}

	protected override int SelectSchedule() => throw new NotImplementedException();

	protected EHANDLE FollowTarget = new();

	protected AI_FollowManagerInfoHandle_t FollowManagerInfo;
	protected AI_FollowParams Params = new();

	bool FollowNavFailed;
	readonly RandStopwatch FollowDelay = new();
}
