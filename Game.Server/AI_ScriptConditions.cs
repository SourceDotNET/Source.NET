using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Numerics;
using System.Runtime.CompilerServices;

namespace Game.Server;

using DEFINE = Source.DEFINE<AI_ScriptConditions>;

public class AI_ProxTester
{
	public void Init(float dist) {
		Inside = (dist > 0);
		DistSq = dist * dist;
	}

	public bool Check(BaseEntity entity1, BaseEntity entity2) {
		if (DistSq != 0) {
			float distSq = (entity1.GetAbsOrigin() - entity2.GetAbsOrigin()).LengthSquared();
			bool inside = (distSq < DistSq);

			return (Inside == inside);
		}
		return true;
	}

	float DistSq;
	bool Inside;

	public static readonly DataMap DataDesc = new(typeof(AI_ProxTester), [
		DEFINE<AI_ProxTester>.FIELD(nameof(DistSq), FieldType.Float),
		DEFINE<AI_ProxTester>.FIELD(nameof(Inside), FieldType.Boolean),
	]);
}

public class AI_ScriptConditionsElement
{
	public void SetActor(BaseEntity? entity) => Actor.Set(entity);
	public BaseEntity? GetActor() => Actor.Get();

	public void SetTimer(SimTimer timer) => Timer = timer;
	public SimTimer GetTimer() => Timer;

	public void SetTimeOut(SimTimer timeout) => Timeout = timeout;
	public SimTimer GetTimeOut() => Timeout;

	EHANDLE Actor;
	SimTimer Timer = new();
	SimTimer Timeout = new();

	public static readonly DataMap DataDesc = new(typeof(AI_ScriptConditionsElement), [
		DEFINE<AI_ScriptConditionsElement>.FIELD(nameof(Actor), FieldType.EHandle),
		DEFINE<AI_ScriptConditionsElement>.EMBEDDED(nameof(Timer)),
		DEFINE<AI_ScriptConditionsElement>.EMBEDDED(nameof(Timeout)),
	]);
}

/// <summary>
/// Watches a set of conditions relative to a given NPC, and when they
/// are all satisfied, fires the relevant output
/// </summary>
[LinkEntityToClass("ai_script_conditions")]
public class AI_ScriptConditions : BaseEntity, IEntityListener
{
	[Flags]
	public enum SF
	{
		ActorAsActivator = 1 << 0,
	}

	static readonly ConVar debugscriptconditions = new("ai_debugscriptconditions", "0");

	public static readonly new DataMap DataDesc = new(typeof(AI_ScriptConditions), BaseEntity.DataDesc, [
		DEFINE.THINKFUNC(nameof(EvaluationThink)),

		DEFINE.OUTPUT(nameof(OnConditionsSatisfied), "OnConditionsSatisfied", eventFuncs),
		DEFINE.OUTPUT(nameof(OnConditionsTimeout), "OnConditionsTimeout", eventFuncs),
		DEFINE.OUTPUT(nameof(NoValidActors), "NoValidActors", eventFuncs),

		DEFINE.INPUTFUNC(FieldType.Void, "Enable", nameof(InputEnable), (INPUTFUNCPTR)((self, data) => ((AI_ScriptConditions)self).InputEnable(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Disable", nameof(InputDisable), (INPUTFUNCPTR)((self, data) => ((AI_ScriptConditions)self).InputDisable(data))),

		// Inputs
		DEFINE.KEYFIELD(nameof(Disabled), FieldType.Boolean, "StartDisabled"),

		DEFINE.FIELD(nameof(TargetEnt), FieldType.EHandle),
		DEFINE.KEYFIELD(nameof(Actor), FieldType.String, "Actor"),

		DEFINE.KEYFIELD(nameof(RequiredTime), FieldType.Float, "RequiredTime"),

#if !HL2_EPISODIC
		DEFINE.FIELD(nameof(ActorHandle), FieldType.EHandle),
		DEFINE.EMBEDDED(nameof(Timer)),
		DEFINE.EMBEDDED(nameof(Timeout)),
#endif

		DEFINE.KEYFIELD(nameof(MinState), FieldType.Integer, "MinimumState"),
		DEFINE.KEYFIELD(nameof(MaxState), FieldType.Integer, "MaximumState"),

		DEFINE.KEYFIELD(nameof(ScriptStatus), FieldType.Integer, "ScriptStatus"),
		DEFINE.KEYFIELD(nameof(ActorSeePlayer), FieldType.Integer, "ActorSeePlayer"),

		DEFINE.KEYFIELD(nameof(PlayerActorProximity), FieldType.Float, "PlayerActorProximity"),
		DEFINE.EMBEDDED(nameof(PlayerActorProxTester)),

		DEFINE.KEYFIELD(nameof(PlayerActorFOV), FieldType.Float, "PlayerActorFOV"),
		DEFINE.KEYFIELD(nameof(PlayerActorFOVTrueCone), FieldType.Boolean, "PlayerActorFOVTrueCone"),

		DEFINE.KEYFIELD(nameof(PlayerActorLOS), FieldType.Integer, "PlayerActorLOS"),
		DEFINE.KEYFIELD(nameof(ActorSeeTarget), FieldType.Integer, "ActorSeeTarget"),

		DEFINE.KEYFIELD(nameof(ActorTargetProximity), FieldType.Float, "ActorTargetProximity"),
		DEFINE.EMBEDDED(nameof(ActorTargetProxTester)),

		DEFINE.KEYFIELD(nameof(PlayerTargetProximity), FieldType.Float, "PlayerTargetProximity"),
		DEFINE.EMBEDDED(nameof(PlayerTargetProxTester)),

		DEFINE.KEYFIELD(nameof(PlayerTargetFOV), FieldType.Float, "PlayerTargetFOV"),
		DEFINE.KEYFIELD(nameof(PlayerTargetFOVTrueCone), FieldType.Boolean, "PlayerTargetFOVTrueCone"),

		DEFINE.KEYFIELD(nameof(PlayerTargetLOS), FieldType.Integer, "PlayerTargetLOS"),
		DEFINE.KEYFIELD(nameof(PlayerBlockingActor), FieldType.Integer, "PlayerBlockingActor"),

		DEFINE.KEYFIELD(nameof(MinTimeout), FieldType.Float, "MinTimeout"),
		DEFINE.KEYFIELD(nameof(MaxTimeout), FieldType.Float, "MaxTimeout"),

		DEFINE.KEYFIELD(nameof(ActorInPVS), FieldType.Integer, "ActorInPVS"),

		DEFINE.KEYFIELD(nameof(ActorInVehicle), FieldType.Integer, "ActorInVehicle"),
		DEFINE.KEYFIELD(nameof(PlayerInVehicle), FieldType.Integer, "PlayerInVehicle"),

		DEFINE.UTLVECTOR(nameof(ElementList), FieldType.Embedded),
		DEFINE.FIELD(nameof(LeaveAsleep), FieldType.Boolean),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public AI_ScriptConditions() {
		Disabled = true;
		RequiredTime = 0;
		MinState = NPCState.Idle;
		MaxState = NPCState.Idle;
		ScriptStatus = ThreeState.None;
		ActorSeePlayer = ThreeState.None;
		PlayerActorProximity = 0;
		PlayerActorFOV = -1;
		PlayerActorLOS = ThreeState.None;
		ActorSeeTarget = ThreeState.None;
		ActorTargetProximity = 0;
		PlayerTargetProximity = 0;
		PlayerTargetFOV = 0;
		PlayerTargetLOS = ThreeState.None;
		PlayerBlockingActor = ThreeState.None;
		ActorInPVS = ThreeState.None;
		MinTimeout = 0;
		MaxTimeout = 0;
		ActorInVehicle = ThreeState.None;
		PlayerInVehicle = ThreeState.None;
#if !HL2_EPISODIC
		ActorHandle.Set(null);
#endif
	}

	public override void OnRestore() {
		base.OnRestore();

#if !HL2_EPISODIC
		//Old HL2 save game! Fix up to new system.
		if (ActorHandle.Get() != null) {
			AI_ScriptConditionsElement conditionactor = new();

			conditionactor.SetActor(ActorHandle.Get());
			conditionactor.SetTimeOut(Timeout);
			conditionactor.SetTimer(Timer);

			ElementList.Add(conditionactor);

			ActorHandle.Set(null);
		}

		if (ElementList.Count == 0 && string.IsNullOrEmpty(Actor) && Disabled == false)
			AddNewElement(null);
#endif
	}

	static bool EvalState(AI_ScriptConditions self, in EvalArgs args) {
		if (args.Actor == null)
			return true;

		AI_BaseNPC npc = args.Actor.MyNPCPointer()!;

		// !!!LATER - fix this code, we shouldn't need the table anymore
		// now that we've placed the NPC state defs in a logical order (sjb)
		ReadOnlySpan<int> stateVals = [
			-1, // NPC_STATE_NONE
			0, // NPC_STATE_IDLE
			1, // NPC_STATE_ALERT
			2, // NPC_STATE_COMBAT
			-1, // NPC_STATE_SCRIPT
			-1, // NPC_STATE_PLAYDEAD
			-1, // NPC_STATE_PRONE
			-1, // NPC_STATE_DEAD
		];

		int valState = stateVals[(int)npc.NPCState];

		if (valState < 0) {
			if (npc.NPCState == NPCState.Script && self.ScriptStatus >= ThreeState.True)
				return true;

			return false;
		}

		int valLow = stateVals[(int)self.MinState];
		int valHigh = stateVals[(int)self.MaxState];

		if (valLow > valHigh) {
			DevMsg("Script condition warning: Invalid setting for Maximum/Minimum state\n");
			self.Disable();
			return false;
		}

		return (valState >= valLow && valState <= valHigh);
	}

	static bool EvalActorSeePlayer(AI_ScriptConditions self, in EvalArgs args) {
		if (self.ActorSeePlayer == ThreeState.None) {
			// Don't care, so don't do any work.
			return true;
		}

		if (args.Actor == null)
			return true;

		bool canSeePlayer = args.Actor.MyNPCPointer()!.HasCondition((int)SCOND_t.COND_SEE_PLAYER);
		return ((int)self.ActorSeePlayer == (canSeePlayer ? 1 : 0));
	}

	static bool EvalActorSeeTarget(AI_ScriptConditions self, in EvalArgs args) {
		if (self.ActorSeeTarget == ThreeState.None) {
			// Don't care, so don't do any work.
			return true;
		}

		if (args.Target != null) {
			if (args.Actor == null)
				return true;

			AI_BaseNPC npcActor = args.Actor.MyNPCPointer()!;

#if HL2_EPISODIC
			// This is the code we want to have written for HL2, but HL2 shipped without the QuerySeeEntity() call. This #ifdef really wants to be
			// something like #ifndef HL2_RETAIL, since this change does want to be in any products that are built henceforth. (sjb)
			bool see = npcActor.FInViewCone(args.Target) && npcActor.FVisible(args.Target) && npcActor.QuerySeeEntity(args.Target);
#else
			bool see = npcActor.FInViewCone(args.Target) && npcActor.FVisible(args.Target);
#endif

			if (see) {
				if (self.ActorSeeTarget == ThreeState.True)
					return true;

				return false;
			}
			else {
				if (self.ActorSeeTarget == ThreeState.False)
					return true;

				return false;
			}
		}

		return true;
	}

	static bool EvalPlayerActorProximity(AI_ScriptConditions self, in EvalArgs args) {
		return (args.Actor == null || self.PlayerActorProxTester.Check(args.Player!, args.Actor));
	}

	static bool EvalPlayerTargetProximity(AI_ScriptConditions self, in EvalArgs args) {
		return (args.Target == null ||
			self.PlayerTargetProxTester.Check(args.Player!, args.Target));
	}

	static bool EvalActorTargetProximity(AI_ScriptConditions self, in EvalArgs args) {
		return (args.Target == null || args.Actor == null ||
			self.ActorTargetProxTester.Check(args.Actor, args.Target));
	}

	static bool EvalPlayerActorLook(AI_ScriptConditions self, in EvalArgs args) {
		return (args.Actor == null ||
			IsInFOV(args.Player, args.Actor, self.PlayerActorFOV, self.PlayerActorFOVTrueCone));
	}

	static bool EvalPlayerTargetLook(AI_ScriptConditions self, in EvalArgs args) {
		return (args.Target == null || IsInFOV(args.Player, args.Target, self.PlayerTargetFOV, self.PlayerTargetFOVTrueCone));
	}

	static bool EvalPlayerActorLOS(AI_ScriptConditions self, in EvalArgs args) {
		if (self.PlayerActorLOS == ThreeState.None) {
			// Don't execute expensive code if we don't care.
			return true;
		}

		return (args.Actor == null || PlayerHasLineOfSight(args.Player!, args.Actor, self.PlayerActorLOS == ThreeState.False));
	}

	static bool EvalPlayerTargetLOS(AI_ScriptConditions self, in EvalArgs args) {
		if (self.PlayerTargetLOS == ThreeState.None) {
			// Don't execute expensive code if we don't care.
			return true;
		}

		return (args.Target == null || PlayerHasLineOfSight(args.Player!, args.Target, self.PlayerTargetLOS == ThreeState.False));
	}

	static bool EvalActorInPVS(AI_ScriptConditions self, in EvalArgs args) {
		if (self.ActorInPVS == ThreeState.None) {
			// Don't execute expensive code if we don't care.
			return true;
		}

		return (args.Actor == null || ActorInPlayersPVS(args.Actor, self.ActorInPVS == ThreeState.False));
	}

	static bool EvalPlayerBlockingActor(AI_ScriptConditions self, in EvalArgs args) {
		if (self.PlayerBlockingActor == ThreeState.None)
			return true;

		if (self.PlayerBlockingActor == ThreeState.False)
			return true;

		return false; // for now, never say player is blocking
	}

	static bool EvalPlayerInVehicle(AI_ScriptConditions self, in EvalArgs args) {
		// We don't care
		if (self.PlayerInVehicle == ThreeState.None)
			return true;

		// Need a player to test
		if (args.Player == null)
			return false;

		// Desired states must match
		return ((args.Player.IsInAVehicle() ? 1 : 0) == (int)self.PlayerInVehicle);
	}

	static bool EvalActorInVehicle(AI_ScriptConditions self, in EvalArgs args) {
		// We don't care
		if (self.ActorInVehicle == ThreeState.None)
			return true;

		if (args.Actor == null)
			return true;

		// Must be able to be in a vehicle at all
		if (args.Actor is not BaseCombatCharacter bcc)
			return false;

		// Desired states must match
		return ((bcc.IsInAVehicle() ? 1 : 0) == (int)self.ActorInVehicle);
	}

	public override void Spawn() {
		Assert((MinState == NPCState.Idle || MinState == NPCState.Combat || MinState == NPCState.Alert) &&
			(MaxState == NPCState.Idle || MaxState == NPCState.Combat || MaxState == NPCState.Alert));

		PlayerActorProxTester.Init(PlayerActorProximity);
		PlayerTargetProxTester.Init(PlayerTargetProximity);
		ActorTargetProxTester.Init(ActorTargetProximity);

		LeaveAsleep = Disabled;
	}

	public override void Activate() {
		base.Activate();

		// When we spawn, m_fDisabled is initial state as given by worldcraft.
		// following that, we keep it updated and it reflects current state.
		if (!Disabled)
			Enable();

#if HL2_EPISODIC
		gEntList.AddListenerEntity(this);
#endif
	}

	public override void UpdateOnRemove() {
		gEntList.RemoveListenerEntity(this);
		base.UpdateOnRemove();

		ElementList.Clear();
	}

	void EvaluationThink() {
		if (Disabled == true)
			return;

		int actorsDone = 0;

#if HL2_DLL
		BasePlayer? player = AI_GetSinglePlayer();
		if (player != null && (player.GetFlags() & EntityFlags.NoTarget) != 0) {
			if (debugscriptconditions.GetBool())
				DevMsg($"{GetDebugName()} WARNING: Player is NOTARGET. This will affect all LOS conditiosn involving the player!\n");
		}
#endif

		for (int i = 0; i < ElementList.Count;) {
			AI_ScriptConditionsElement? conditionElement = ElementList[i];

			if (conditionElement == null) {
				i++;
				continue;
			}

			BaseEntity? actor = conditionElement.GetActor();
			BaseEntity activator = this;

#if HL2_EPISODIC
			if (actor != null && HasSpawnFlags(SF.ActorAsActivator))
				activator = actor;
#endif

			AssertMsg(!Disabled, "Violated invariant between CAI_ScriptConditions disabled state and think func setting");

			if (!string.IsNullOrEmpty(Actor) && actor == null) {
				if (ElementList.Count == 1) {
					DevMsg("Warning: Active AI script conditions associated with an non-existant or destroyed NPC\n");
					NoValidActors.FireOutput(this, this, 0);
				}

				actorsDone++;
				ElementList.RemoveAt(i);
				continue;
			}

			i++;

			if (MinTimeout > 0 && conditionElement.GetTimeOut().Expired()) {
				if (debugscriptconditions.GetBool())
					DevMsg($"{GetEntityName()} firing output OnConditionsTimeout ({conditionElement.GetTimeOut().GetInterval()} seconds)\n");

				actorsDone++;
				OnConditionsTimeout.FireOutput(activator, this);
				continue;
			}

			bool result = true;
			int nEvaluators = Evaluators.Length;

			EvalArgs args = new() {
				Actor = actor,
				Player = GetPlayer(),
				Target = TargetEnt.Get()
			};

			for (int j = 0; j < nEvaluators; ++j) {
				if (!Evaluators[j].Evaluator(this, in args)) {
					conditionElement.GetTimer().Reset();
					result = false;

					if (debugscriptconditions.GetBool())
						DevMsg($"{GetDebugName()} failed on: {Evaluators[j].Name}\n");

					break;
				}
			}

			if (result) {
				if (debugscriptconditions.GetBool())
					DevMsg($"{GetDebugName()} waiting... {conditionElement.GetTimer().GetRemaining()}\n");
			}

			if (result && conditionElement.GetTimer().Expired()) {
				if (debugscriptconditions.GetBool())
					DevMsg($"{GetDebugName()} firing output OnConditionsSatisfied\n");

				// Default behavior for now, provide worldcraft option later.
				actorsDone++;
				OnConditionsSatisfied.FireOutput(activator, this);
			}
		}

		//All done!
		if (actorsDone == ElementList.Count) {
			Disable();
			ElementList.Clear();
		}

		SetThinkTime();
	}

	int AddNewElement(BaseEntity? actor) {
		AI_ScriptConditionsElement conditionelement = new();
		conditionelement.SetActor(actor);

		if (MaxTimeout > 0)
			conditionelement.GetTimeOut().Set(RandomFloat(MinTimeout, MaxTimeout), false);
		else
			conditionelement.GetTimeOut().Set(MinTimeout, false);

		conditionelement.GetTimer().Set(RequiredTime);

		if (RequiredTime > 0)
			conditionelement.GetTimer().Reset();

		ElementList.Add(conditionelement);
		return ElementList.Count - 1;
	}

	void Enable() {
		TargetEnt.Set(gEntList.FindEntityByName(null, Target));

		BaseEntity? actor = gEntList.FindEntityByName(null, Actor);
		if (ElementList.Count == 0) {
			if (!string.IsNullOrEmpty(Actor) && actor == null) {
				DevMsg($"Warning: Spawning AI script conditions ({GetDebugName()}) associated with an non-existant NPC\n");
				NoValidActors.FireOutput(this, this, 0);
				Disable();
				return;
			}

			if (actor != null && actor.MyNPCPointer() == null) {
				Warning("Script condition warning: warning actor is not an NPC\n");
				Disable();
				return;
			}
		}

		while (actor != null) {
			if (!ActorInList(actor))
				AddNewElement(actor);

			actor = gEntList.FindEntityByName(actor, Actor);
		}

		//If we are hitting this it means we are using a Target->Player condition
		if (string.IsNullOrEmpty(Actor)) {
			if (!ActorInList(actor))
				AddNewElement(null);
		}

		Disabled = false;

		SetThink(EvaluationThink);
		SetThinkTime();
	}

	void Disable() {
		SetThink(null);

		Disabled = true;
	}

	void SetThinkTime() => SetNextThink(gpGlobals.CurTime + 0.250f);

	void InputEnable(InputData inputdata) {
		LeaveAsleep = false;
		Enable();
	}

	void InputDisable(InputData inputdata) {
		LeaveAsleep = true;
		Disable();
	}

	static bool IsInFOV(BaseEntity? viewer, BaseEntity viewed, float fov, bool trueCone) {
		BaseCombatCharacter? combatantViewer = viewer as BaseCombatCharacter;

		if (fov < 360 && combatantViewer != null /*&& pViewed*/ ) {
			Vector3 lookDir;
			Vector3 actorDir;

			// Halve the fov. As expressed here, fov is the full size of the viewcone.
			float fovDotResult;
			fovDotResult = MathF.Cos(MathLib.DEG2RAD(fov / 2));
			float dotPr = 1;

			if (trueCone) {
				// 3D Check
				lookDir = combatantViewer.EyeDirection3D();
				actorDir = viewed.EyePosition() - viewer!.EyePosition();
				actorDir.NormalizeInPlace();
				dotPr = Vector3.Dot(lookDir, actorDir);
			}
			else {
				// 2D Check
				lookDir = combatantViewer.EyeDirection2D();
				actorDir = viewed.EyePosition() - viewer!.EyePosition();
				actorDir.Z = 0.0f;
				Vector2 actorDir2D = actorDir.AsVector2D();
				actorDir2D.NormalizeInPlace();
				dotPr = Vector2.Dot(lookDir.AsVector2D(), actorDir2D);
			}

			if (dotPr < fovDotResult) {
				if (fov < 0) {
					// Designer has requested that the player
					// NOT be looking at this place.
					return true;
				}

				return false;
			}
		}

		if (fov < 0)
			return false;

		return true;
	}

	static bool PlayerHasLineOfSight(BaseEntity viewer, BaseEntity viewed, bool not) {
		if (viewer is BaseCombatCharacter combatantViewer) {
			// We always trace towards the player, so we handle players-in-vehicles
			if (viewed.FVisible(combatantViewer)) {
				// Line of sight exists.
				if (not)
					return false;
				else
					return true;
			}
			else {
				// No line of sight.
				if (not)
					return true;
				else
					return false;
			}
		}

		return true;
	}

	static bool ActorInPlayersPVS(BaseEntity? actor, bool not) {
		if (actor == null)
			return true;

		bool inPVS = Util.FindClientInPVS(actor.Edict()) != null;

		if (inPVS) {
			if (not)
				return false;
			else
				return true;
		}
		else {
			if (not)
				return true;
			else
				return false;
		}
	}

	bool ActorInList(BaseEntity? actor) {
		for (int i = 0; i < ElementList.Count; i++) {
			if (ElementList[i].GetActor() == actor)
				return true;
		}

		return false;
	}

	public void OnEntitySpawned(BaseEntity entity) {
		if (Disabled && LeaveAsleep) {
			// Don't add elements if we're not currently running and don't want to automatically wake up.
			// Any spawning NPC's we miss during this time will be found and added when manually Enabled().
			return;
		}

		if (entity.MyNPCPointer() == null)
			return;

		if (entity.NameMatches(Actor)) {
			if (ActorInList(entity) == false) {
				AddNewElement(entity);

				if (Disabled == true && LeaveAsleep == false)
					Enable();
			}
		}
	}

	// Evaluators
	struct EvalArgs
	{
		public BaseEntity? Actor;
		public BasePlayer? Player;
		public BaseEntity? Target;
	}

	// Output handlers
	readonly OutputEvent OnConditionsSatisfied = new();
	readonly OutputEvent OnConditionsTimeout = new();
	readonly OutputEvent NoValidActors = new();

#if !HL2_EPISODIC
	BaseEntity? GetActor() => ActorHandle.Get();
#endif
	BasePlayer? GetPlayer() => Util.GetLocalPlayer();

	// @Note (toml 07-17-02): At some point, it may be desireable to switch to using function objects instead of functions. Probably
	// if support for NPCs addiing custom conditions becomes necessary
	delegate bool EvaluationFunc(AI_ScriptConditions self, in EvalArgs args);

	struct EvaluatorInfo
	{
		public EvaluationFunc Evaluator;
		public string Name;
	}

	static EvaluatorInfo EVALUATOR(EvaluationFunc func, [CallerArgumentExpression(nameof(func))] string? exp = null) {
		return new() {
			Evaluator = func,
			Name = new(exp.AsSpan()["Eval".Length..])
		};
	}

	static readonly EvaluatorInfo[] Evaluators = [
		EVALUATOR(EvalActorSeePlayer),
		EVALUATOR(EvalState),
		EVALUATOR(EvalPlayerActorProximity),
		EVALUATOR(EvalPlayerTargetProximity),
		EVALUATOR(EvalActorTargetProximity),
		EVALUATOR(EvalPlayerBlockingActor),
		EVALUATOR(EvalPlayerActorLook),
		EVALUATOR(EvalPlayerTargetLook),
		EVALUATOR(EvalActorSeeTarget),
		EVALUATOR(EvalPlayerActorLOS),
		EVALUATOR(EvalPlayerTargetLOS),

#if HL2_EPISODIC
		EVALUATOR(EvalActorInPVS),
		EVALUATOR(EvalPlayerInVehicle),
		EVALUATOR(EvalActorInVehicle),
#endif
	];

	// General conditions info

	bool Disabled;
	bool LeaveAsleep;
	EHANDLE TargetEnt;

	float RequiredTime; // How long should the conditions me true

#if !HL2_EPISODIC
	EHANDLE ActorHandle;
	readonly SimTimer Timer = new();          // @TODO (toml 07-16-02): save/load of timer once Jay has save/load of contained objects
	readonly SimTimer Timeout = new();
#endif

	// Specific conditions data
	NPCState MinState;
	NPCState MaxState;
	ThreeState ScriptStatus;
	ThreeState ActorSeePlayer;
	string? Actor;

	float PlayerActorProximity;
	readonly AI_ProxTester PlayerActorProxTester = new();

	float PlayerActorFOV;
	bool PlayerActorFOVTrueCone;
	ThreeState PlayerActorLOS;
	ThreeState ActorSeeTarget;

	float ActorTargetProximity;
	readonly AI_ProxTester ActorTargetProxTester = new();

	float PlayerTargetProximity;
	readonly AI_ProxTester PlayerTargetProxTester = new();

	float PlayerTargetFOV;
	bool PlayerTargetFOVTrueCone;
	ThreeState PlayerTargetLOS;
	ThreeState PlayerBlockingActor;
	ThreeState ActorInPVS;

	float MinTimeout;
	float MaxTimeout;

	ThreeState ActorInVehicle;
	ThreeState PlayerInVehicle;

	readonly List<AI_ScriptConditionsElement> ElementList = [];
}
