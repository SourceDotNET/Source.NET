using Game.Shared;

using Source.Common;
using Source.Common.Engine;

using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Server;

[LinkEntityToClass("npc_breen")]
public class NPC_Breen : AI_BaseActor
{
	[Flags]
	public enum SF
	{
		BackgroundTalk = 1 << 16,
	}

	public override void Spawn( ){
		// Breen is allowed to use multiple models, because he has a torso version for monitors.
		// He defaults to his normal model.
		ReadOnlySpan<char> model = GetModelName();
		if (model.IsStringEmpty) {
			model = "models/breen.mdl";
			SetModelName(model);
		}

		Precache();
		SetModel(model);

		base.Spawn();

		SetHullType(AI_HullType.Human);
		SetHullSizeNormal();

		SetSolid(Source.SolidType.BBox);
		AddSolidFlags(Source.SolidFlags.NotStandable);
		SetMoveType(Source.MoveType.Step);
		SetBloodColor(Shared.BloodColor.Red);
		Health = 8;
		FieldOfView = 0.5f;// indicates the width of this NPC's forward view cone ( as a dotproduct result )
		NPCState = NPCState.None;

		CapabilitiesAdd(Capability.MoveGround | Capability.OpenDoors | Capability.AnimatedFace | Capability.TurnHead);
		CapabilitiesAdd(Capability.FriendlyDmgImmune);
		AddEFlags(EFL.NoDissolve | EFL.NoMegaPhysCannonRagdoll| EFL.NoPhysCannonInteraction);

		NPCInit();
	}
	public override void Precache( ){
		PrecacheModel(GetModelName());
		base.Precache();
	}
	public override Class_T Classify() => Class_T.None;
	public override void HandleAnimEvent(ref AnimEvent ev) => base.HandleAnimEvent(ref ev); // not sure why this was overridden?
	public override int GetSoundInterests() => 0;
	public override bool UseSemaphore() => HasSpawnFlags(SF.BackgroundTalk) ? false : base.UseSemaphore();
}
