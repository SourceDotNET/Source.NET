using Game.Shared;

using Source;
using Source.Common;

using DEFINE = Source.DEFINE<Game.Server.AI_GoalEntity>;

namespace Game.Server;

public class AI_GoalEntity : BaseEntity, IEntityListener
{
	[Flags]
	public enum GoalFlags
	{
		Active = 0x01,
		ResolvedName = 0x02,
		Dormant = 0x04,
	}

	public enum SearchType
	{
		EntName,
		ClassName,
	}

	public static readonly new DataMap DataDesc = new(typeof(AI_GoalEntity), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(Actor), FieldType.String, "Actor"),
		DEFINE.KEYFIELD(nameof(Goal), FieldType.String, "Goal"),
		DEFINE.KEYFIELD(nameof(StartActive), FieldType.Boolean, "StartActive"),
		DEFINE.KEYFIELD(nameof(ConceptModifiers), FieldType.String, "BaseConceptModifiers"),
		DEFINE.KEYFIELD(nameof(Search), FieldType.Integer, "SearchType"),
		DEFINE.UTLVECTOR(nameof(Actors), FieldType.EHandle),
		DEFINE.FIELD(nameof(GoalEntity), FieldType.EHandle),
		DEFINE.FIELD(nameof(Flags), FieldType.Integer),

		DEFINE.INPUTFUNC(FieldType.Void, "Activate", nameof(InputActivate), (INPUTFUNCPTR)((self, data) => ((AI_GoalEntity)self).InputActivate(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "UpdateActors", nameof(InputUpdateActors), (INPUTFUNCPTR)((self, data) => ((AI_GoalEntity)self).InputUpdateActors(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Deactivate", nameof(InputDeactivate), (INPUTFUNCPTR)((self, data) => ((AI_GoalEntity)self).InputDeactivate(data))),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	string? Actor;
	string? Goal;
	bool StartActive;
	SearchType Search;
	string? ConceptModifiers;

	readonly List<Handle<AI_BaseNPC>> Actors = [];
	EHANDLE GoalEntity = new();
	GoalFlags Flags;

	public AI_GoalEntity() {
		Actor = null;
		Goal = null;
		StartActive = false;
		Search = SearchType.EntName;
		ConceptModifiers = null;
		GoalEntity.Set(null);
		Flags = 0;
	}

	public override EntityCapabilities ObjectCaps() => (base.ObjectCaps() & ~EntityCapabilities.AcrossTransition) | EntityCapabilities.NotifyOnTransition;

	public override void Spawn() {
		SetThink(DelayedRefresh);
		SetNextThink(gpGlobals.CurTime + 0.1f);
	}

	public override void OnRestore() {
		base.OnRestore();

		ExitDormant();

		if ((Flags & GoalFlags.Active) != 0)
			gEntList.AddListenerEntity(this);
	}

	void DelayedRefresh() {
		InputData ignored = default;
		if (StartActive) {
			Assert((Flags & GoalFlags.Active) == 0);
			InputActivate(ignored);
			StartActive = false;
		}
		else
			InputUpdateActors(ignored);

		SetThink(null);
	}

	void PruneActors() {
		for (int i = Actors.Count - 1; i >= 0; i--) {
			AI_BaseNPC? actor = Actors[i].Get();
			if (actor == null || actor.IsMarkedForDeletion() || actor.GetState() == NPCState.Dead) {
				Actors[i] = Actors[^1];
				Actors.RemoveAt(Actors.Count - 1);
			}
		}
	}

	void ResolveNames() {
		Actors.Clear();

		BaseEntity? entity = null;
		for (; ; ) {
			switch (Search) {
				case SearchType.EntName:
					entity = gEntList.FindEntityByName(entity, Actor);
					break;

				case SearchType.ClassName:
					entity = gEntList.FindEntityByClassname(entity, Actor);
					break;
			}

			if (entity == null)
				break;

			AI_BaseNPC? actor = entity.MyNPCPointer();

			if (actor != null && actor.GetState() != NPCState.Dead) {
				Handle<AI_BaseNPC> temp = new();
				temp.Set(actor);
				Actors.Add(temp);
			}
		}

		GoalEntity.Set(gEntList.FindEntityByName(null, Goal));
	}

	protected void UpdateActors() {
		if ((Flags & GoalFlags.Active) == 0 || (Flags & GoalFlags.ResolvedName) == 0) {
			ResolveNames();
			Flags |= GoalFlags.ResolvedName;
		}
		else
			PruneActors();
	}

	public bool IsActive() {
		if ((Flags & GoalFlags.Active) != 0) {
			UpdateActors();
			return Actors.Count != 0;
		}
		return false;
	}

	public int NumActors() {
		UpdateActors();
		return Actors.Count;
	}

	public AI_BaseNPC? GetActor(int actor = 0) {
		UpdateActors();
		if (Actors.Count > actor)
			return Actors[actor].Get();
		return null;
	}

	public void SetGoalEntity(BaseEntity goalEntity) {
		Goal = goalEntity.GetEntityName();
		GoalEntity.Set(goalEntity);
	}

	public BaseEntity? GetGoalEntity() {
		UpdateActors();
		return GoalEntity.Get();
	}

	public string? GetGoalEntityName() => Goal;

	public string? GetConceptModifiers() => ConceptModifiers;

	protected List<Handle<AI_BaseNPC>> AccessActors() => Actors;

	protected virtual void EnableGoal(AI_BaseNPC? ai) { }
	protected virtual void DisableGoal(AI_BaseNPC? ai) { }

	public virtual void InputActivate(InputData inputdata) {
		if ((Flags & GoalFlags.Active) == 0) {
			gEntList.AddListenerEntity(this);

			UpdateActors();
			Flags |= GoalFlags.Active;

			for (int i = 0; i < Actors.Count; i++)
				EnableGoal(Actors[i].Get());
		}
	}

	public virtual void InputUpdateActors(InputData inputdata) {
		int i;
		HashSet<AI_BaseNPC?> prevActors = [];

		PruneActors();

		for (i = 0; i < Actors.Count; i++)
			prevActors.Add(Actors[i].Get());

		ResolveNames();

		for (i = 0; i < Actors.Count; i++) {
			if (!prevActors.Contains(Actors[i].Get())) {
				if ((Flags & GoalFlags.Active) != 0)
					EnableGoal(Actors[i].Get());
			}
			else
				prevActors.Remove(Actors[i].Get());
		}

		foreach (AI_BaseNPC? prev in prevActors) {
			if ((Flags & GoalFlags.Active) != 0)
				DisableGoal(prev);
		}
	}

	public virtual void InputDeactivate(InputData inputdata) {
		if ((Flags & GoalFlags.Active) != 0) {
			gEntList.RemoveListenerEntity(this);
			UpdateActors();
			Flags &= ~GoalFlags.Active;

			for (int i = 0; i < Actors.Count; i++)
				DisableGoal(Actors[i].Get());
		}
	}

	public void EnterDormant() {
		if ((Flags & GoalFlags.Active) != 0) {
			Flags |= GoalFlags.Dormant;
			for (int i = 0; i < Actors.Count; i++)
				DisableGoal(Actors[i].Get());
		}
	}

	public void ExitDormant() {
		if ((Flags & GoalFlags.Dormant) != 0) {
			Flags &= ~GoalFlags.Dormant;

			InputData ignored = default;
			InputUpdateActors(ignored);
		}
	}

	public override void UpdateOnRemove() {
		if ((Flags & GoalFlags.Active) != 0) {
			InputData inputdata = default;
			InputDeactivate(inputdata);
		}
		base.UpdateOnRemove();
	}

	public virtual void OnEntityCreated(BaseEntity entity) {
		Assert((Flags & GoalFlags.Active) != 0);

		if (entity.MyNPCPointer() != null) {
			SetThink(DelayedRefresh);
			SetNextThink(gpGlobals.CurTime + 0.1f);
		}
	}

	public virtual void OnEntityDeleted(BaseEntity entity) {
		Assert(entity != this);
	}
}
