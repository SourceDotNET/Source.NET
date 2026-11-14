using Game.Shared;

using Source;

namespace Game.Server;

[LinkEntityToClass("npc_gman")]
public class NPC_GMan : AI_PlayerAlly
{
	public override Class_T Classify() {
		return Class_T.PlayerAllyVital;
	}

	public override int GetSoundInterests() {
		return 0;
	}

	public override void HandleAnimEvent(ref AnimEvent animEvent) {
		base.HandleAnimEvent(ref animEvent);
	}

	public override void Spawn() {
		Precache();

		base.Spawn();

		SetModel("models/gman.mdl");

		SetHullType(AI_HullType.Human);
		SetHullSizeNormal();

		SetSolid(SolidType.BBox);
		AddSolidFlags(SolidFlags.NotStandable);
		SetMoveType(Source.MoveType.Step);
		Health = 8;
		FieldOfView = 0.5f;
		NPCState = NPCState.None;
		SetImpactEnergyScale(0.0f);

		CapabilitiesAdd(Server.Capability.MoveGround| Server.Capability.OpenDoors | Server.Capability.AnimatedFace | Server.Capability.TurnHead);
		CapabilitiesAdd(Server.Capability.FriendlyDmgImmune);
		AddEFlags(EFL.NoDissolve | EFL.NoMegaPhysCannonRagdoll);

		NPCInit();
	}

	public override void Precache() {
		PrecacheModel("models/gman.mdl");

		base.Precache();
	}

	public override Disposition IRelationType(BaseEntity? target) {
		return Disposition.NU;
	}

	public override bool CreateBehaviors() {
		AddBehavior(FollowBehavior);

		return base.CreateBehaviors();
	}

	public override int SelectSchedule() {
		if (!BehaviorSelectSchedule()) {
		}

		return base.SelectSchedule();
	}

	readonly AI_FollowBehavior FollowBehavior = new();
}
