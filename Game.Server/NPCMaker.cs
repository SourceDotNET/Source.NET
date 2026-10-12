using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Formats.BSP;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

[LinkEntityToClass("info_npc_spawn_destination")]
public class NPCSpawnDestination : PointEntity
{
	public static readonly new DataMap DataDesc = new(typeof(NPCSpawnDestination), PointEntity.DataDesc, [
		DEFINE<NPCSpawnDestination>.KEYFIELD(nameof(ReuseDelay), FieldType.Float, "ReuseDelay"),
		DEFINE<NPCSpawnDestination>.KEYFIELD(nameof(RenameNPC), FieldType.String, "RenameNPC"),

		DEFINE<NPCSpawnDestination>.OUTPUT(nameof(OnSpawnNPC), "OnSpawnNPC", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public float ReuseDelay;
	public string? RenameNPC;
	public TimeUnit_t TimeNextAvailable;

	public OutputEvent OnSpawnNPC = new();

	public NPCSpawnDestination() => TimeNextAvailable = gpGlobals.CurTime;

	public bool IsAvailable() {
		if (TimeNextAvailable > gpGlobals.CurTime)
			return false;

		return true;
	}

	public void OnSpawnedNPC(AI_BaseNPC npc) {
		if (RenameNPC != null)
			npc.SetName(RenameNPC);

		OnSpawnNPC.FireOutput(npc, this);
		TimeNextAvailable = gpGlobals.CurTime + ReuseDelay;
	}
}

public abstract class BaseNPCMaker : BaseEntity
{
	public const int SF_NPCMAKER_START_ON = 1;
	public const int SF_NPCMAKER_NPCCLIP = 8;
	public const int SF_NPCMAKER_FADE = 16;
	public const int SF_NPCMAKER_INF_CHILD = 32;
	public const int SF_NPCMAKER_NO_DROP = 64;
	public const int SF_NPCMAKER_HIDEFROMPLAYER = 128;
	public const int SF_NPCMAKER_ALWAYSUSERADIUS = 256;
	public const int SF_NPCMAKER_NOPRELOADMODELS = 512;

	public static readonly ConVar ai_inhibit_spawners = new("ai_inhibit_spawners", "0", FCvar.Cheat);

	public static readonly new DataMap DataDesc = new(typeof(BaseNPCMaker), BaseEntity.DataDesc, [
		DEFINE<BaseNPCMaker>.KEYFIELD(nameof(MaxNumNPCs), FieldType.Integer, "MaxNPCCount"),
		DEFINE<BaseNPCMaker>.KEYFIELD(nameof(MaxLiveChildren), FieldType.Integer, "MaxLiveChildren"),
		DEFINE<BaseNPCMaker>.KEYFIELD(nameof(SpawnFrequency), FieldType.Float, "SpawnFrequency"),
		DEFINE<BaseNPCMaker>.KEYFIELD(nameof(Disabled), FieldType.Boolean, "StartDisabled"),

		DEFINE<BaseNPCMaker>.INPUTFUNC(FieldType.Void, "Spawn", nameof(InputSpawnNPC), (INPUTFUNCPTR)((self, data) => ((BaseNPCMaker)self).InputSpawnNPC(data))),
		DEFINE<BaseNPCMaker>.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((BaseNPCMaker)self).InputEnable(data))),
		DEFINE<BaseNPCMaker>.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((BaseNPCMaker)self).InputDisable(data))),
		DEFINE<BaseNPCMaker>.INPUTFUNC(FieldType.Void, "Toggle", nameof(InputToggle), (INPUTFUNCPTR)((self, data) => ((BaseNPCMaker)self).InputToggle(data))),
		DEFINE<BaseNPCMaker>.INPUTFUNC(FieldType.Integer, "SetMaxChildren", nameof(InputSetMaxChildren), (INPUTFUNCPTR)((self, data) => ((BaseNPCMaker)self).InputSetMaxChildren(data))),
		DEFINE<BaseNPCMaker>.INPUTFUNC(FieldType.Integer, "AddMaxChildren", nameof(InputAddMaxChildren), (INPUTFUNCPTR)((self, data) => ((BaseNPCMaker)self).InputAddMaxChildren(data))),
		DEFINE<BaseNPCMaker>.INPUTFUNC(FieldType.Integer, "SetMaxLiveChildren", nameof(InputSetMaxLiveChildren), (INPUTFUNCPTR)((self, data) => ((BaseNPCMaker)self).InputSetMaxLiveChildren(data))),
		DEFINE<BaseNPCMaker>.INPUTFUNC(FieldType.Float, "SetSpawnFrequency", nameof(InputSetSpawnFrequency), (INPUTFUNCPTR)((self, data) => ((BaseNPCMaker)self).InputSetSpawnFrequency(data))),

		DEFINE<BaseNPCMaker>.OUTPUT(nameof(OnAllSpawned), "OnAllSpawned", eventFuncs),
		DEFINE<BaseNPCMaker>.OUTPUT(nameof(OnAllSpawnedDead), "OnAllSpawnedDead", eventFuncs),
		DEFINE<BaseNPCMaker>.OUTPUT(nameof(OnAllLiveChildrenDead), "OnAllLiveChildrenDead", eventFuncs),
		DEFINE<BaseNPCMaker>.OUTPUT(nameof(OnSpawnNPC), "OnSpawnNPC", eventFuncs),

		DEFINE<BaseNPCMaker>.KEYFIELD(nameof(IgnoreEntName), FieldType.String, "IgnoreEntity"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public int MaxNumNPCs;
	public float SpawnFrequency;

	public readonly OutputEHANDLE OnSpawnNPC = new();
	public OutputEvent OnAllSpawned = new();
	public OutputEvent OnAllSpawnedDead = new();
	public OutputEvent OnAllLiveChildrenDead = new();

	public int LiveChildren;
	public int MaxLiveChildren;

	public bool Disabled;

	public EHANDLE IgnoreEntity = new();
	public string? IgnoreEntName;

	public override void Spawn() {
		SetSolid(SolidType.None);
		LiveChildren = 0;
		Precache();

		if (HasSpawnFlags(SF_NPCMAKER_INF_CHILD))
			AddSpawnFlags(SF_NPCMAKER_FADE);

		if (!Disabled) {
			SetThink(MakerThink);
			SetNextThink(gpGlobals.CurTime + 0.1f);
		}
		else
			SetThink(SUB_DoNothing);
	}

	public override EntityCapabilities ObjectCaps() => base.ObjectCaps() & ~EntityCapabilities.AcrossTransition;

	public bool HumanHullFits(in Vector3 location) {
		Util.TraceHull(location, location + new Vector3(0, 0, 1), NAI_Hull.Mins(AI_HullType.Human), NAI_Hull.Maxs(AI_HullType.Human), Mask.NPCSolid, IgnoreEntity.Get(), Source.CollisionGroup.None, out Trace tr);

		if (tr.Fraction == 1.0f)
			return true;

		return false;
	}

	public bool CanMakeNPC(bool ignoreSolidEntities = false) {
		if (ai_inhibit_spawners.GetBool())
			return false;

		if (MaxLiveChildren > 0 && LiveChildren >= MaxLiveChildren)
			return false;

		if (IgnoreEntName != null)
			IgnoreEntity.Set(gEntList.FindEntityByName(null, IgnoreEntName));

		Vector3 mins = GetAbsOrigin() - new Vector3(34, 34, 0);
		Vector3 maxs = GetAbsOrigin() + new Vector3(34, 34, 0);
		maxs.Z = GetAbsOrigin().Z;

		if (!ignoreSolidEntities) {
			BaseEntity[] list = new BaseEntity[128];

			int count = Util.EntitiesInBox(list, mins, maxs, EntityFlags.Client | EntityFlags.NPC);
			if (count != 0) {
				for (int i = 0; i < count; i++) {
					if (list[i] == null)
						continue;

					if ((list[i].GetSolidFlags() & SolidFlags.NotSolid) == 0) {
						Util.TraceHull(GetAbsOrigin() + new Vector3(0, 0, 2), GetAbsOrigin() - new Vector3(0, 0, 8192), NAI_Hull.Mins(AI_HullType.Human), NAI_Hull.Maxs(AI_HullType.Human), Mask.NPCSolid, IgnoreEntity.Get(), Source.CollisionGroup.None, out Trace tr);

						if (!HumanHullFits(tr.EndPos + new Vector3(0, 0, 1)))
							return false;
					}
				}
			}
		}

		if (HasSpawnFlags(SF_NPCMAKER_HIDEFROMPLAYER)) {
			for (int i = 1; i <= gpGlobals.MaxClients; i++) {
				BasePlayer? player = Util.PlayerByIndex(i);
				if (player != null) {
					if (player.FInViewCone(GetAbsOrigin()) && player.FVisible(GetAbsOrigin())) {
						if ((player.GetFlags() & EntityFlags.NoTarget) == 0)
							return false;
						DevMsg(2, $"Spawner {GetEntityName()} spawning even though seen due to notarget\n");
					}
				}
			}
		}

		return true;
	}

	public override void DeathNotice(BaseEntity victim) {
		LiveChildren--;

		AssertMsg(LiveChildren >= 0, "npc_maker receiving child death notice but thinks has no children\n");

		if (LiveChildren <= 0) {
			OnAllLiveChildrenDead.FireOutput(this, this);

			if (!HasSpawnFlags(SF_NPCMAKER_INF_CHILD) && IsDepleted())
				OnAllSpawnedDead.FireOutput(this, this);
		}
	}

	public abstract void MakeNPC();

	public virtual void ChildPreSpawn(AI_BaseNPC child) { }

	public virtual void ChildPostSpawn(AI_BaseNPC child) {
		bool found = true;
		while (found) {
			Util.TraceHull(child.GetAbsOrigin(), child.GetAbsOrigin(), child.WorldAlignMins(), child.WorldAlignMaxs(), Mask.NPCSolid, child, Source.CollisionGroup.None, out Trace tr);
			if (tr.Fraction != 1.0f && tr.Ent != null) {
				if (FClassnameIs(tr.Ent, "prop_physics")) {
					tr.Ent.AddSolidFlags(SolidFlags.NotSolid);
					Util.RemoveImmediate(tr.Ent);
					continue;
				}
			}

			found = false;
		}
		if (IgnoreEntity.Get() != null)
			child.SetOwnerEntity(IgnoreEntity.Get());
	}

	public void MakerThink() {
		SetNextThink(gpGlobals.CurTime + SpawnFrequency);

		MakeNPC();
	}

	public virtual bool IsDepleted() {
		if (HasSpawnFlags(SF_NPCMAKER_INF_CHILD) || MaxNumNPCs > 0)
			return false;

		return true;
	}

	public void Toggle() {
		if (Disabled)
			Enable();
		else
			Disable();
	}

	public virtual void Enable() {
		if (IsDepleted())
			return;

		Disabled = false;
		SetThink(MakerThink);
		SetNextThink(gpGlobals.CurTime);
	}

	public virtual void Disable() {
		Disabled = true;
		SetThink(null);
	}

	public void InputSpawnNPC(InputData inputdata) {
		if (!IsDepleted())
			MakeNPC();
	}

	public void InputEnable(InputData inputdata) => Enable();
	public void InputDisable(InputData inputdata) => Disable();
	public void InputToggle(InputData inputdata) => Toggle();
	public void InputSetMaxChildren(InputData inputdata) => MaxNumNPCs = inputdata.Value.Int();
	public void InputAddMaxChildren(InputData inputdata) => MaxNumNPCs += inputdata.Value.Int();
	public void InputSetMaxLiveChildren(InputData inputdata) => MaxLiveChildren = inputdata.Value.Int();
	public void InputSetSpawnFrequency(InputData inputdata) => SpawnFrequency = inputdata.Value.Float();
}

[LinkEntityToClass("npc_maker")]
public class NPCMaker : BaseNPCMaker
{
	public static readonly new DataMap DataDesc = new(typeof(NPCMaker), BaseNPCMaker.DataDesc, [
		DEFINE<NPCMaker>.KEYFIELD(nameof(NPCClassname), FieldType.String, "NPCType"),
		DEFINE<NPCMaker>.KEYFIELD(nameof(ChildTargetName), FieldType.String, "NPCTargetname"),
		DEFINE<NPCMaker>.KEYFIELD(nameof(SquadName), FieldType.String, "NPCSquadName"),
		DEFINE<NPCMaker>.KEYFIELD(nameof(SpawnEquipment), FieldType.String, "additionalequipment"),
		DEFINE<NPCMaker>.KEYFIELD(nameof(HintGroup), FieldType.String, "NPCHintGroup"),
		DEFINE<NPCMaker>.KEYFIELD(nameof(RelationshipString), FieldType.String, "Relationship"),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public string? NPCClassname;
	public string? SquadName;
	public string? HintGroup;
	public string? SpawnEquipment;
	public string? RelationshipString;
	public string? ChildTargetName;

	public override void Precache() {
		base.Precache();

		if (string.IsNullOrEmpty(NPCClassname))
			Warning($"npc_maker {GetEntityName()} has no specified NPC-to-spawn classname.\n");
		else
			Util.PrecacheOther(NPCClassname);
	}

	public override void MakeNPC() {
		if (!CanMakeNPC())
			return;

		AI_BaseNPC? ent = (AI_BaseNPC?)CreateEntityByName(NPCClassname);

		if (ent == null) {
			Warning("NULL Ent in NPCMaker!\n");
			return;
		}

		ent.RelationshipString = RelationshipString;

		OnSpawnNPC.Set(ent.GetRefEHandle(), ent, this);

		ent.SetAbsOrigin(GetAbsOrigin());

		QAngle angles = GetAbsAngles();
		angles.X = 0.0f;
		angles.Z = 0.0f;
		ent.SetAbsAngles(angles);

		ent.AddSpawnFlags(SF_NPC_FALL_TO_GROUND);

		if (HasSpawnFlags(SF_NPCMAKER_FADE))
			ent.AddSpawnFlags(SF_NPC_FADE_CORPSE);

		ent.SpawnEquipment = SpawnEquipment;
		ent.SquadName = SquadName;
		ent.HintGroup = HintGroup;

		ChildPreSpawn(ent);

		Util.DispatchSpawn(ent);
		ent.SetOwnerEntity(this);
		ent.Activate();

		if (ChildTargetName != null)
			ent.SetName(ChildTargetName);

		ChildPostSpawn(ent);

		LiveChildren++;

		if (!HasSpawnFlags(SF_NPCMAKER_INF_CHILD)) {
			MaxNumNPCs--;

			if (IsDepleted()) {
				OnAllSpawned.FireOutput(this, this);

				SetThink(null);
				FnUse = null;
			}
		}
	}
}

[LinkEntityToClass("npc_template_maker")]
public class TemplateNPCMaker : BaseNPCMaker
{
	public enum ThreeStateYesNo
	{
		Yes = 0,
		No,
		DontCare,
	}

	public enum ThreeStateDist
	{
		Nearest = 0,
		Farthest,
		DontCare,
	}

	const int MAX_DESTINATION_ENTS = 100;

	public static readonly new DataMap DataDesc = new(typeof(TemplateNPCMaker), BaseNPCMaker.DataDesc, [
		DEFINE<TemplateNPCMaker>.KEYFIELD(nameof(TemplateName), FieldType.String, "TemplateName"),
		DEFINE<TemplateNPCMaker>.KEYFIELD(nameof(Radius), FieldType.Float, "radius"),
		DEFINE<TemplateNPCMaker>.KEYFIELD(nameof(DestinationGroup), FieldType.String, "DestinationGroup"),
		DEFINE<TemplateNPCMaker>.KEYFIELD(nameof(CriterionVisibility), FieldType.Integer, "CriterionVisibility"),
		DEFINE<TemplateNPCMaker>.KEYFIELD(nameof(CriterionDistance), FieldType.Integer, "CriterionDistance"),
		DEFINE<TemplateNPCMaker>.KEYFIELD(nameof(MinSpawnDistance), FieldType.Integer, "MinSpawnDistance"),

		DEFINE<TemplateNPCMaker>.INPUTFUNC(FieldType.Void, "SpawnNPCInRadius", nameof(InputSpawnInRadius), (INPUTFUNCPTR)((self, data) => ((TemplateNPCMaker)self).InputSpawnInRadius(data))),
		DEFINE<TemplateNPCMaker>.INPUTFUNC(FieldType.Void, "SpawnNPCInLine", nameof(InputSpawnInLine), (INPUTFUNCPTR)((self, data) => ((TemplateNPCMaker)self).InputSpawnInLine(data))),
		DEFINE<TemplateNPCMaker>.INPUTFUNC(FieldType.Integer, "SpawnMultiple", nameof(InputSpawnMultiple), (INPUTFUNCPTR)((self, data) => ((TemplateNPCMaker)self).InputSpawnMultiple(data))),
		DEFINE<TemplateNPCMaker>.INPUTFUNC(FieldType.String, "ChangeDestinationGroup", nameof(InputChangeDestinationGroup), (INPUTFUNCPTR)((self, data) => ((TemplateNPCMaker)self).InputChangeDestinationGroup(data))),
		DEFINE<TemplateNPCMaker>.INPUTFUNC(FieldType.Integer, "SetMinimumSpawnDistance", nameof(InputSetMinimumSpawnDistance), (INPUTFUNCPTR)((self, data) => ((TemplateNPCMaker)self).InputSetMinimumSpawnDistance(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	protected float Radius;

	protected string? TemplateName;
	protected Memory<byte> TemplateData;
	protected string? DestinationGroup;

	protected int MinSpawnDistance;

	ThreeStateYesNo CriterionVisibility;
	ThreeStateDist CriterionDistance;

	protected virtual void PrecacheTemplateEntity(BaseEntity entity) => entity.Precache();

	public override void Precache() {
		base.Precache();

		if (TemplateData.IsEmpty) {
			if (TemplateName == null) {
				Warning($"npc_template_maker {GetEntityName()} has no template NPC!\n");
				Util.Remove(this);
				return;
			}
			else {
				TemplateData = Templates.FindByTargetName(TemplateName);
				if (TemplateData.IsEmpty) {
					DevWarning($"npc_template_maker {GetEntityName()}: template NPC {TemplateName} not found!\n");
					Util.Remove(this);
					return;
				}
			}
		}

		Assert(!TemplateData.IsEmpty);

		if (!TemplateData.IsEmpty) {
			ParseEntity(out BaseEntity? entity, TemplateData, null);
			if (entity != null) {
				PrecacheTemplateEntity(entity);
				Util.RemoveImmediate(entity);
			}
		}
	}

	public virtual NPCSpawnDestination? FindSpawnDestination() {
		NPCSpawnDestination[] destinations = new NPCSpawnDestination[MAX_DESTINATION_ENTS];
		BaseEntity? ent;
		BasePlayer? player = AI_GetClosestPlayer();
		int count = 0;

		if (player == null)
			return null;

		ent = gEntList.FindEntityByName(null, DestinationGroup);

		if (ent == null) {
			DevWarning($"Template NPC Spawner ({GetDebugName()}) doesn't have any spawn destinations!\n");
			return null;
		}

		while (ent != null) {
			if (ent is NPCSpawnDestination destination && destination.IsAvailable()) {
				bool valid = true;
				Vector3 test = destination.GetAbsOrigin();

				if (CriterionVisibility != ThreeStateYesNo.DontCare) {
					Vector3 topOfHull = NAI_Hull.Maxs(AI_HullType.Human);
					topOfHull.X = 0;
					topOfHull.Y = 0;
					bool visible = player.FVisible(test) || player.FVisible(test + topOfHull);

					if (CriterionVisibility == ThreeStateYesNo.Yes) {
						if (!visible)
							valid = false;
					}
					else {
						if (visible) {
							if ((player.GetFlags() & EntityFlags.NoTarget) == 0)
								valid = false;
							else
								DevMsg(2, $"Spawner {GetEntityName()} spawning even though seen due to notarget\n");
						}
					}
				}

				if (valid) {
					destinations[count] = destination;
					count++;
				}
			}

			ent = gEntList.FindEntityByName(ent, DestinationGroup);
		}

		if (count < 1)
			return null;

		if (CriterionDistance == ThreeStateDist.DontCare) {
			for (int i = 0; i < 5; i++) {
				NPCSpawnDestination randomDest = destinations[random.RandomInt(0, count - 1)];

				if (HumanHullFits(randomDest.GetAbsOrigin()))
					return randomDest;
			}

			return null;
		}
		else {
			if (CriterionDistance == ThreeStateDist.Nearest) {
				float nearest = float.MaxValue;
				NPCSpawnDestination? nearestDest = null;

				for (int i = 0; i < count; i++) {
					Vector3 test = destinations[i].GetAbsOrigin();

					float dist = (test - player.GetAbsOrigin()).Length();

					if (MinSpawnDistance != 0 && MinSpawnDistance > dist)
						continue;

					if (dist < nearest && HumanHullFits(test)) {
						nearest = dist;
						nearestDest = destinations[i];
					}
				}

				return nearestDest;
			}
			else {
				float farthest = 0;
				NPCSpawnDestination? farthestDest = null;

				for (int i = 0; i < count; i++) {
					Vector3 test = destinations[i].GetAbsOrigin();

					float dist = (test - player.GetAbsOrigin()).Length();

					if (MinSpawnDistance != 0 && MinSpawnDistance > dist)
						continue;

					if (dist > farthest && HumanHullFits(test)) {
						farthest = dist;
						farthestDest = destinations[i];
					}
				}

				return farthestDest;
			}
		}
	}

	public override void MakeNPC() {
		if (Radius != 0 && HasSpawnFlags(SF_NPCMAKER_ALWAYSUSERADIUS)) {
			MakeNPCInRadius();
			return;
		}

		if (!CanMakeNPC(DestinationGroup != null))
			return;

		NPCSpawnDestination? destination = null;
		if (DestinationGroup != null) {
			destination = FindSpawnDestination();
			if (destination == null) {
				DevMsg(2, $"{GetClassname()} '{GetEntityName()}' failed to find a valid spawnpoint in destination group: '{DestinationGroup}'\n");
				return;
			}
		}

		AI_BaseNPC? ent = null;
		ParseEntity(out BaseEntity? entity, TemplateData, null);
		if (entity != null)
			ent = (AI_BaseNPC)entity;

		if (ent == null) {
			Warning("NULL Ent in NPCMaker!\n");
			return;
		}

		if (destination != null) {
			ent.SetAbsOrigin(destination.GetAbsOrigin());

			QAngle angles = destination.GetAbsAngles();
			angles.X = 0.0f;
			angles.Z = 0.0f;
			ent.SetAbsAngles(angles);

			destination.OnSpawnedNPC(ent);
		}
		else {
			ent.SetAbsOrigin(GetAbsOrigin());

			QAngle angles = GetAbsAngles();
			angles.X = 0.0f;
			angles.Z = 0.0f;
			ent.SetAbsAngles(angles);
		}

		OnSpawnNPC.Set(entity!.GetRefEHandle(), entity, this);

		if (HasSpawnFlags(SF_NPCMAKER_FADE))
			ent.AddSpawnFlags(SF_NPC_FADE_CORPSE);

		ent.RemoveSpawnFlags(SF_NPC_TEMPLATE);

		if (!HasSpawnFlags(SF_NPCMAKER_NO_DROP))
			ent.RemoveSpawnFlags(SF_NPC_FALL_TO_GROUND);

		ChildPreSpawn(ent);

		Util.DispatchSpawn(ent);
		ent.SetOwnerEntity(this);
		ent.Activate();

		ChildPostSpawn(ent);

		LiveChildren++;

		if (!HasSpawnFlags(SF_NPCMAKER_INF_CHILD)) {
			MaxNumNPCs--;

			if (IsDepleted()) {
				OnAllSpawned.FireOutput(this, this);

				SetThink(null);
				FnUse = null;
			}
		}
	}

	public void MakeNPCInLine() {
		if (!CanMakeNPC(true))
			return;

		AI_BaseNPC? ent = null;
		ParseEntity(out BaseEntity? entity, TemplateData, null);
		if (entity != null)
			ent = (AI_BaseNPC)entity;

		if (ent == null) {
			Warning("NULL Ent in NPCMaker!\n");
			return;
		}

		OnSpawnNPC.Set(entity!.GetRefEHandle(), entity, this);

		PlaceNPCInLine(ent);

		ent.AddSpawnFlags(SF_NPC_FALL_TO_GROUND);

		ent.RemoveSpawnFlags(SF_NPC_TEMPLATE);

		ChildPreSpawn(ent);

		Util.DispatchSpawn(ent);
		ent.SetOwnerEntity(this);
		ent.Activate();

		ChildPostSpawn(ent);

		LiveChildren++;

		if (!HasSpawnFlags(SF_NPCMAKER_INF_CHILD)) {
			MaxNumNPCs--;

			if (IsDepleted()) {
				OnAllSpawned.FireOutput(this, this);

				SetThink(null);
				FnUse = null;
			}
		}
	}

	protected bool PlaceNPCInLine(AI_BaseNPC npc) {
		Vector3 place;

		GetVectors(out Vector3 line, out _, out _);

		line *= -1;

		Util.TraceLine(GetAbsOrigin(), GetAbsOrigin() - new Vector3(0, 0, 8192), Mask.Shot, npc, Source.CollisionGroup.None, out Trace tr);
		place = tr.EndPos;
		float stepSize = npc.GetHullWidth();

		for (int i = 0; i < 10; i++) {
			Util.TraceHull(place, place + new Vector3(0, 0, 10), npc.GetHullMins(), npc.GetHullMaxs(), Mask.Shot, npc, Source.CollisionGroup.None, out tr);

			if (tr.Fraction == 1.0f) {
				npc.SetAbsOrigin(tr.EndPos);
				return true;
			}

			place += line * stepSize;
		}

		DevMsg("**Failed to place NPC in line!\n");
		return false;
	}

	public void MakeNPCInRadius() {
		if (!CanMakeNPC(true))
			return;

		AI_BaseNPC? ent = null;
		ParseEntity(out BaseEntity? entity, TemplateData, null);
		if (entity != null)
			ent = (AI_BaseNPC)entity;

		if (ent == null) {
			Warning("NULL Ent in NPCMaker!\n");
			return;
		}

		if (!PlaceNPCInRadius(ent)) {
			Util.RemoveImmediate(ent);
			return;
		}

		OnSpawnNPC.Set(entity!.GetRefEHandle(), entity, this);

		ent.AddSpawnFlags(SF_NPC_FALL_TO_GROUND);

		ent.RemoveSpawnFlags(SF_NPC_TEMPLATE);

		ChildPreSpawn(ent);

		Util.DispatchSpawn(ent);
		ent.SetOwnerEntity(this);
		ent.Activate();

		ChildPostSpawn(ent);

		LiveChildren++;

		if (!HasSpawnFlags(SF_NPCMAKER_INF_CHILD)) {
			MaxNumNPCs--;

			if (IsDepleted()) {
				OnAllSpawned.FireOutput(this, this);

				SetThink(null);
				FnUse = null;
			}
		}
	}

	protected bool PlaceNPCInRadius(AI_BaseNPC npc) {
		if (AI_BaseNPC.FindSpotForNPCInRadius(out Vector3 pos, GetAbsOrigin(), npc, Radius)) {
			npc.SetAbsOrigin(pos);
			return true;
		}

		DevMsg("**Failed to place NPC in radius!\n");
		return false;
	}

	public virtual void MakeMultipleNPCS(int npcs) {
		bool inRadius = DestinationGroup == null && Radius > 0.1f;
		while (npcs-- != 0) {
			if (!inRadius)
				MakeNPC();
			else
				MakeNPCInRadius();
		}
	}

	public void InputSpawnInRadius(InputData inputdata) => MakeNPCInRadius();
	public void InputSpawnInLine(InputData inputdata) => MakeNPCInLine();
	public void InputSpawnMultiple(InputData inputdata) => MakeMultipleNPCS(inputdata.Value.Int());
	public void InputChangeDestinationGroup(InputData inputdata) => DestinationGroup = inputdata.Value.StringID();
	public void InputSetMinimumSpawnDistance(InputData inputdata) => MinSpawnDistance = inputdata.Value.Int();
}
