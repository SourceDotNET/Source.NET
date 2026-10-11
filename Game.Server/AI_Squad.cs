using System.Numerics;

namespace Game.Server;

public enum SquadSlot
{
	None = -1,

	Attack1 = 0,
	Attack2,

	InvestigateSound,

	ExclusiveHandsign,
	ExclusiveReload,

	PickupWeapon1,
	PickupWeapon2,

	SpecialAttack,

	LastShared,
}

public class AI_Squad
{
	public string GetName() => throw new NotImplementedException();
	public int NumMembers(bool ignoreSilentMembers = true) => throw new NotImplementedException();
	public bool FOkToMakeSound(SentencePriority soundPriority) => throw new NotImplementedException();
	public void JustMadeSound(SentencePriority soundPriority, TimeUnit_t time) => throw new NotImplementedException();
	public bool IsLeader(AI_BaseNPC npc) => throw new NotImplementedException();
	public AI_BaseNPC? GetFirstMember(out int iter) => throw new NotImplementedException();
	public AI_BaseNPC? GetNextMember(ref int iter) => throw new NotImplementedException();
	public void VacateStrategySlot(BaseEntity? enemy, SquadSlot slot) => throw new NotImplementedException();
	public void UpdateEnemyMemory(AI_BaseNPC updater, BaseEntity enemy, in Vector3 position) => throw new NotImplementedException();
}
