using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Mathematics;

using System.Numerics;

using DEFINE = Source.DEFINE<Game.Server.AI_ScriptedSequence>;

namespace Game.Server;

public enum ScriptMoveTo
{
	Wait = 0,
	Walk = 1,
	Run = 2,
	Custom = 3,
	Teleport = 4,
	WaitFacing = 5,
}

public enum ScriptPlayerDeath
{
	DoNothing = 0,
	Cancel = 1,
}

public enum ScriptInterrupt
{
	ByClass = 0,
	ByName,
}

[LinkEntityToClass("scripted_sequence")]
public class AI_ScriptedSequence : BaseEntity
{
	public const int MAX_SCRIPT_EVENTS = 8;

	[Flags]
	public enum SF
	{
		WaitTillSeen = 1,
		ExitAgitated = 2,
		Repeatable = 4,
		LeaveCorpse = 8,
		StartOnSpawn = 16,
		NoInterrupt = 32,
		OverrideState = 64,
		DontTeleportAtEnd = 128,
		LoopInPostIdle = 256,
		HighPriority = 512,
		SearchCyclically = 1024,
		NoComplaints = 2048,
		AllowDeath = 4096,
	}

	public static readonly ConVar ai_task_pre_script = new("ai_task_pre_script", "0", FCvar.None);

	public static readonly new DataMap DataDesc = new(typeof(AI_ScriptedSequence), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(EntryName), FieldType.String, "m_iszEntry"),
		DEFINE.KEYFIELD(nameof(PreIdle), FieldType.String, "m_iszIdle"),
		DEFINE.KEYFIELD(nameof(Play), FieldType.String, "m_iszPlay"),
		DEFINE.KEYFIELD(nameof(PostIdle), FieldType.String, "m_iszPostIdle"),
		DEFINE.KEYFIELD(nameof(CustomMove), FieldType.String, "m_iszCustomMove"),
		DEFINE.KEYFIELD(nameof(NextScript), FieldType.String, "m_iszNextScript"),
		DEFINE.KEYFIELD(nameof(Entity), FieldType.String, "m_iszEntity"),
		DEFINE.KEYFIELD(nameof(MoveTo), FieldType.Integer, "m_fMoveTo"),
		DEFINE.KEYFIELD(nameof(Radius), FieldType.Float, "m_flRadius"),
		DEFINE.KEYFIELD(nameof(Repeat), FieldType.Float, "m_flRepeat"),

		DEFINE.FIELD(nameof(IsPlayingEntryValue), FieldType.Boolean),
		DEFINE.KEYFIELD(nameof(LoopActionSequence), FieldType.Boolean, "m_bLoopActionSequence"),
		DEFINE.KEYFIELD(nameof(SynchPostIdles), FieldType.Boolean, "m_bSynchPostIdles"),
		DEFINE.KEYFIELD(nameof(IgnoreGravity), FieldType.Boolean, "m_bIgnoreGravity"),
		DEFINE.KEYFIELD(nameof(DisableNPCCollisions), FieldType.Boolean, "m_bDisableNPCCollisions"),

		DEFINE.FIELD(nameof(Delay), FieldType.Integer),
		DEFINE.FIELD(nameof(Delayed), FieldType.Boolean),
		DEFINE.FIELD(nameof(StartTime), FieldType.Time),
		DEFINE.FIELD(nameof(WaitForBeginSequence), FieldType.Boolean),

		DEFINE.FIELD(nameof(SavedEffects), FieldType.Integer),
		DEFINE.FIELD(nameof(SavedFlags), FieldType.Integer),
		DEFINE.FIELD(nameof(SavedCollisionGroup), FieldType.Integer),

		DEFINE.FIELD(nameof(Interruptable), FieldType.Boolean),
		DEFINE.FIELD(nameof(SequenceStarted), FieldType.Boolean),
		DEFINE.FIELD(nameof(TargetEnt), FieldType.EHandle),
		DEFINE.FIELD(nameof(NextCine), FieldType.EHandle),
		DEFINE.FIELD(nameof(LastFoundEntity), FieldType.EHandle),
		DEFINE.FIELD(nameof(ForcedTarget), FieldType.EHandle),
		DEFINE.FIELD(nameof(DontCancelOtherSequences), FieldType.Boolean),
		DEFINE.FIELD(nameof(ForceSynch), FieldType.Boolean),

		DEFINE.FIELD(nameof(Thinking), FieldType.Boolean),
		DEFINE.FIELD(nameof(InitiatedSelfDelete), FieldType.Boolean),

		DEFINE.FIELD(nameof(IsTeleportingDueToMoveToValue), FieldType.Boolean),

		DEFINE.FIELD(nameof(InteractionRelativeEntity), FieldType.EHandle),

		DEFINE.FIELD(nameof(TargetWasAsleep), FieldType.Boolean),

		DEFINE.INPUTFUNC(FieldType.Void, "MoveToPosition", nameof(InputMoveToPosition), (INPUTFUNCPTR)((self, data) => ((AI_ScriptedSequence)self).InputMoveToPosition(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "BeginSequence", nameof(InputBeginSequence), (INPUTFUNCPTR)((self, data) => ((AI_ScriptedSequence)self).InputBeginSequence(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "CancelSequence", nameof(InputCancelSequence), (INPUTFUNCPTR)((self, data) => ((AI_ScriptedSequence)self).InputCancelSequence(data))),

		DEFINE.KEYFIELD(nameof(PlayerDeathBehavior), FieldType.Integer, "onplayerdeath"),
		DEFINE.INPUTFUNC(FieldType.Void, "ScriptPlayerDeath", nameof(InputScriptPlayerDeath), (INPUTFUNCPTR)((self, data) => ((AI_ScriptedSequence)self).InputScriptPlayerDeath(data))),

		DEFINE.OUTPUT(nameof(OnBeginSequenceOutput), "OnBeginSequence", eventFuncs),
		DEFINE.OUTPUT(nameof(OnEndSequence), "OnEndSequence", eventFuncs),
		DEFINE.OUTPUT(nameof(OnPostIdleEndSequence), "OnPostIdleEndSequence", eventFuncs),
		DEFINE.OUTPUT(nameof(OnCancelSequence), "OnCancelSequence", eventFuncs),
		DEFINE.OUTPUT(nameof(OnCancelFailedSequence), "OnCancelFailedSequence", eventFuncs),
		DEFINE.OUTPUT(nameof(OnScriptEvent01), "OnScriptEvent01", eventFuncs),
		DEFINE.OUTPUT(nameof(OnScriptEvent02), "OnScriptEvent02", eventFuncs),
		DEFINE.OUTPUT(nameof(OnScriptEvent03), "OnScriptEvent03", eventFuncs),
		DEFINE.OUTPUT(nameof(OnScriptEvent04), "OnScriptEvent04", eventFuncs),
		DEFINE.OUTPUT(nameof(OnScriptEvent05), "OnScriptEvent05", eventFuncs),
		DEFINE.OUTPUT(nameof(OnScriptEvent06), "OnScriptEvent06", eventFuncs),
		DEFINE.OUTPUT(nameof(OnScriptEvent07), "OnScriptEvent07", eventFuncs),
		DEFINE.OUTPUT(nameof(OnScriptEvent08), "OnScriptEvent08", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public string? EntryName;
	public string? PreIdle;
	public string? Play;
	public string? PostIdle;
	public string? CustomMove;
	public string? NextScript;
	public string? Entity;

	public ScriptMoveTo MoveTo;
	bool IsPlayingEntryValue;
	bool LoopActionSequence;
	bool SynchPostIdles;
	public bool IgnoreGravity;
	bool DisableNPCCollisions;

	float Radius;
	float Repeat;

	int Delay;

	bool Delayed;

	TimeUnit_t StartTime;
	bool WaitForBeginSequence;

	public EntityEffects SavedEffects;
	public EntityFlags SavedFlags;
	public Source.CollisionGroup SavedCollisionGroup;

	bool Interruptable;
	bool SequenceStarted;

	EHANDLE TargetEnt = new();

	public EHANDLE NextCine = new();

	bool Thinking;
	bool InitiatedSelfDelete;

	bool IsTeleportingDueToMoveToValue;

	EHANDLE LastFoundEntity = new();

	EHANDLE ForcedTarget = new();
	bool DontCancelOtherSequences;
	public bool ForceSynch;

	bool TargetWasAsleep;

	readonly OutputEvent OnBeginSequenceOutput = new();
	readonly OutputEvent OnEndSequence = new();
	readonly OutputEvent OnPostIdleEndSequence = new();
	readonly OutputEvent OnCancelSequence = new();
	readonly OutputEvent OnCancelFailedSequence = new();
	readonly OutputEvent OnScriptEvent01 = new();
	readonly OutputEvent OnScriptEvent02 = new();
	readonly OutputEvent OnScriptEvent03 = new();
	readonly OutputEvent OnScriptEvent04 = new();
	readonly OutputEvent OnScriptEvent05 = new();
	readonly OutputEvent OnScriptEvent06 = new();
	readonly OutputEvent OnScriptEvent07 = new();
	readonly OutputEvent OnScriptEvent08 = new();

	EHANDLE InteractionRelativeEntity = new();

	ScriptPlayerDeath PlayerDeathBehavior;

	public override EntityCapabilities ObjectCaps() => base.ObjectCaps() & ~EntityCapabilities.AcrossTransition;

	public void SetTarget(BaseEntity? target) => TargetEnt.Set(target);
	public BaseEntity? GetTarget() => TargetEnt.Get();

	public bool PlayedSequence() => SequenceStarted;

	public bool IsPlayingEntry() => IsPlayingEntryValue;
	public bool IsPlayingAction() => SequenceStarted && !IsPlayingEntryValue;
	public void SetLoopActionSequence(bool loop) => LoopActionSequence = loop;
	public bool ShouldLoopActionSequence() => LoopActionSequence;
	public void SetSynchPostIdles(bool synch) => SynchPostIdles = synch;

	public bool IsTeleportingDueToMoveTo() => IsTeleportingDueToMoveToValue;

	static void ScriptEntityCancel(BaseEntity cine, bool pretendSuccess = false) {
		if (FClassnameIs(cine, "scripted_sequence")) {
			AI_ScriptedSequence cineTarget = (AI_ScriptedSequence)cine;

			BaseEntity? entity = cineTarget.GetTarget();
			AI_BaseNPC? target = null;
			if (entity != null)
				target = entity.MyNPCPointer();

			if (target != null) {
				if (target.NPCState == NPCState.Script) {
					target.ScriptState = ScriptStateType.Cleanup;

					EntityFlags savedFlags = target.Cine.Get() != null ? target.Cine.Get()!.SavedFlags : 0;

					target.CineCleanup();
					cineTarget.FixScriptNPCSchedule(target, savedFlags);
				}
				else {
					cineTarget.SetTarget(null);
					target.SetEffects(cineTarget.SavedEffects);
					target.Cine.Set(null);
					target.SetTarget(null);
					target.SetGoalEnt(null);
					target.SetIdealState(NPCState.Idle);
				}
			}

			cineTarget.Delay = 0;

			if (pretendSuccess) {
				cineTarget.OnEndSequence.FireOutput(null, cineTarget);
				cineTarget.OnPostIdleEndSequence.FireOutput(null, cineTarget);
			}
			else {
				cineTarget.OnCancelSequence.FireOutput(null, cineTarget);

				if (cineTarget.StartTime == 0)
					cineTarget.OnCancelFailedSequence.FireOutput(null, cineTarget);
			}
		}
	}

	public override void Spawn() {
		SetSolid(SolidType.None);

		if (string.IsNullOrEmpty(GetEntityName()) || HasSpawnFlags(SF.StartOnSpawn)) {
			StartThink();
			SetNextThink(gpGlobals.CurTime + 1.0f);

			if (!string.IsNullOrEmpty(GetEntityName()))
				WaitForBeginSequence = true;
		}

		if (HasSpawnFlags(SF.NoInterrupt))
			Interruptable = false;
		else
			Interruptable = true;

		SequenceStarted = false;
		StartTime = 0;
		NextCine.Set(null);

		LastFoundEntity.Set(null);
	}

	public override void UpdateOnRemove() {
		ScriptEntityCancel(this);
		base.UpdateOnRemove();
	}

	void StartThink() {
		SequenceStarted = false;
		Thinking = true;
		SetThink(ScriptThink);
	}

	void StopThink() {
		if (Thinking) {
			Assert(!InitiatedSelfDelete);
			SetThink(null);
			Thinking = false;
		}
	}

	public bool FCanOverrideState() {
		if (HasSpawnFlags(SF.OverrideState))
			return true;
		return false;
	}

	public void FireScriptEvent(int nEvent) {
		switch (nEvent) {
			case 1: OnScriptEvent01.FireOutput(this, this); break;
			case 2: OnScriptEvent02.FireOutput(this, this); break;
			case 3: OnScriptEvent03.FireOutput(this, this); break;
			case 4: OnScriptEvent04.FireOutput(this, this); break;
			case 5: OnScriptEvent05.FireOutput(this, this); break;
			case 6: OnScriptEvent06.FireOutput(this, this); break;
			case 7: OnScriptEvent07.FireOutput(this, this); break;
			case 8: OnScriptEvent08.FireOutput(this, this); break;
		}
	}

	void InputMoveToPosition(InputData inputdata) {
		if (InitiatedSelfDelete)
			return;

		BaseEntity? entity = GetTarget();
		AI_BaseNPC? target = null;

		if (entity != null)
			target = entity.MyNPCPointer();

		if (target != null) {
			if (target.ScriptState == ScriptStateType.Playing || target.ScriptState == ScriptStateType.PostIdle) {
				if (target.CanPlaySequence(FCanOverrideState(), ScriptInterrupt.ByName) != CanPlaySequence_t.CannotPlay) {
					StartScript();
					WaitForBeginSequence = true;
				}
			}
		}
		else {
			StartThink();
			SetNextThink(gpGlobals.CurTime);
			WaitForBeginSequence = true;
		}
	}

	void InputBeginSequence(InputData inputdata) {
		if (InitiatedSelfDelete)
			return;

		WaitForBeginSequence = false;

		BaseEntity? entity = GetTarget();
		AI_BaseNPC? target = null;

		if (entity == null && ForcedTarget.Get() != null) {
			if (FindEntity())
				entity = GetTarget();
		}

		if (entity != null)
			target = entity.MyNPCPointer();

		if (target != null) {
			if (target.ScriptState == ScriptStateType.Playing || target.ScriptState == ScriptStateType.PostIdle) {
				if (target.CanPlaySequence(FCanOverrideState(), ScriptInterrupt.ByName) != CanPlaySequence_t.CannotPlay)
					StartScript();
			}
		}
		else {
			StartThink();

			SetNextThink(TICK_NEVER_THINK);
			ScriptThink();
		}
	}

	void InputCancelSequence(InputData inputdata) {
		if (InitiatedSelfDelete)
			return;

		DevMsg(2, $"InputCancelScript: Cancelling script '{Play}'\n");
		StopThink();
		ScriptEntityCancel(this);
	}

	void InputScriptPlayerDeath(InputData inputdata) {
		if (PlayerDeathBehavior == ScriptPlayerDeath.Cancel) {
			if (InitiatedSelfDelete)
				return;

			DevMsg(2, $"InputCancelScript: Cancelling script '{Play}'\n");
			StopThink();
			ScriptEntityCancel(this);
		}
	}

	public bool IsTimeToStart() {
		Assert(!WaitForBeginSequence);

		return Delay == 0;
	}

	public bool IsWaitingForBegin() => WaitForBeginSequence;

	AI_BaseNPC? FindScriptEntity() {
		AI_BaseNPC? enqueueNPC = null;

		BaseEntity? entity;
		ScriptInterrupt interrupt;
		if (ForcedTarget.Get() != null) {
			interrupt = ScriptInterrupt.ByName;
			entity = ForcedTarget.Get();
		}
		else {
			interrupt = ScriptInterrupt.ByName;

			entity = gEntList.FindEntityByNameWithin(LastFoundEntity.Get(), Entity, GetAbsOrigin(), Radius);
			if (entity == null) {
				entity = gEntList.FindEntityByClassnameWithin(LastFoundEntity.Get(), Entity, GetAbsOrigin(), Radius);
				interrupt = ScriptInterrupt.ByClass;
			}
		}

		while (entity != null) {
			AI_BaseNPC? npc = entity.MyNPCPointer();
			if (npc != null) {
				CanPlaySequence_t canPlay = npc.CanPlaySequence(FCanOverrideState(), interrupt);
				if (canPlay == CanPlaySequence_t.CanPlayNow)
					return npc;
				else if (canPlay == CanPlaySequence_t.CanPlayEnqueued)
					enqueueNPC = npc;
				else if (!HasSpawnFlags(SF.NoComplaints))
					DevMsg($"Found {Entity}, but can't play!\n");
			}

			if (ForcedTarget.Get() != null) {
				Warning($"Code forced {entity.GetClassname()}({entity.GetDebugName()}), to be the target of scripted sequence {GetDebugName()}, but it can't play it.\n");
				entity = null;
				Util.Remove(this);
				return null;
			}
			else {
				if (interrupt == ScriptInterrupt.ByName)
					entity = gEntList.FindEntityByNameWithin(entity, Entity, GetAbsOrigin(), Radius);
				else
					entity = gEntList.FindEntityByClassnameWithin(entity, Entity, GetAbsOrigin(), Radius);
			}
		}

		if (enqueueNPC != null)
			return enqueueNPC;

		return null;
	}

	bool FindEntity() {
		AI_BaseNPC? target = FindScriptEntity();

		if (HasSpawnFlags(SF.SearchCyclically))
			LastFoundEntity.Set(target);

		SetTarget(target);

		return target != null;
	}

	public void StartScript() {
		BaseEntity? entity = GetTarget();
		AI_BaseNPC? target = null;
		if (entity != null)
			target = entity.MyNPCPointer();

		if (target != null) {
			target.RemoveSpawnFlags(AI_BaseNPC.SF.WaitForScript);

			AI_ScriptedSequence? targetCine = target.Cine.Get();
			if (targetCine != null) {
				if (targetCine.NextCine.Get() != null) {
					AI_ScriptedSequence cine = (AI_ScriptedSequence)targetCine.NextCine.Get()!;

					if (targetCine.NextCine.Get() != targetCine)
						cine.SetTarget(null);
					DevMsg(2, $"script \"{GetDebugName()}\" kicking script \"{cine.GetDebugName()}\" out of the queue\n");
				}

				targetCine.NextCine.Set(this);
				return;
			}

			if (string.IsNullOrEmpty(NextScript))
				NextCine.Set(null);

			target.SetGoalEnt(this);
			target.ForceDecisionThink();
			target.Cine.Set(this);
			target.SetTarget(this);

			target.OnStartScene();

			{
				TargetWasAsleep = target.GetSleepState() != AI_SleepState.Awake;
				bool justAwoke = target.WokeThisTick();
				if (TargetWasAsleep || justAwoke) {
					if (TargetWasAsleep)
						target.Wake();
					TargetWasAsleep = true;

					target.AddEffects(EntityEffects.NoDraw);
				}
			}

			SavedEffects = (EntityEffects)target.Effects & ~EntityEffects.NoDraw;
			target.AddEffects((EntityEffects)Effects);
			SavedFlags = target.GetFlags();
			SavedCollisionGroup = target.GetCollisionGroup();

			if (DisableNPCCollisions)
				target.SetCollisionGroup(Source.CollisionGroup.NPCScripted);

			switch (MoveTo) {
				case ScriptMoveTo.Wait:
				case ScriptMoveTo.WaitFacing:
					target.ScriptState = ScriptStateType.Wait;

					if (IgnoreGravity) {
						target.AddFlag(EntityFlags.Fly);
						target.SetGroundEntity(null);
					}

					break;

				case ScriptMoveTo.Walk:
					target.ScriptState = ScriptStateType.WalkToMark;
					break;

				case ScriptMoveTo.Run:
					target.ScriptState = ScriptStateType.RunToMark;
					break;

				case ScriptMoveTo.Custom:
					target.ScriptState = ScriptStateType.CustomMoveToMark;
					break;

				case ScriptMoveTo.Teleport:
					IsTeleportingDueToMoveToValue = true;
					target.Teleport(GetAbsOrigin(), null, vec3_origin);
					IsTeleportingDueToMoveToValue = false;
					target.GetMotor()!.SetIdealYaw(GetLocalAngles().Y);
					target.SetLocalAngularVelocity(vec3_angle);
					target.IncrementInterpolationFrame();
					QAngle angles = target.GetLocalAngles();
					angles.Y = GetLocalAngles().Y;
					target.SetLocalAngles(angles);
					target.ScriptState = ScriptStateType.Wait;

					if (IgnoreGravity) {
						target.AddFlag(EntityFlags.Fly);
						target.SetGroundEntity(null);
					}

					break;
			}

			Delayed = false;
			DelayStart(true);

			target.SetIdealState(NPCState.Script);

			if (target.IsEFlagSet(EFL.NoThinkFunction) && target.GetNextThink() != TICK_NEVER_THINK) {
				DevWarning($"scripted_sequence {EntIndex()}:{GetDebugName()} - restarting dormant entity {target.EntIndex()}:{target.GetDebugName()} : {gpGlobals.CurTime:0.0}:{target.GetNextThink():0.0}\n");
				target.SetNextThink(gpGlobals.CurTime);
			}
		}
	}

	void ScriptThink() {
		if (g_pAINetworkManager != null && !g_pAINetworkManager.IsInitialized())
			SetNextThink(gpGlobals.CurTime + 0.1f);
		else if (FindEntity()) {
			StartScript();
			DevMsg(2, $"scripted_sequence {EntIndex()}:\"{GetDebugName()}\" using NPC {GetTarget()!.EntIndex()}:\"{GetTarget()!.GetEntityName()}\"({Entity})\n");
		}
		else {
			CancelScript();
			DevMsg(2, $"scripted_sequence {EntIndex()}:\"{GetDebugName()}\" can't find NPC \"{Entity}\"\n");
			SetNextThink(gpGlobals.CurTime + 1.0f);
		}
	}

	public void OnBeginSequence() => OnBeginSequenceOutput.FireOutput(this, this);

	public bool StartSequence(AI_BaseNPC target, string? seq, bool completeOnEmpty) {
		Assert(target != null);
		SequenceStarted = true;
		IsPlayingEntryValue = seq == EntryName;

		if (string.IsNullOrEmpty(seq) && completeOnEmpty) {
			SequenceDone(target!);
			return false;
		}

		int sequence = target!.LookupSequence(seq);
		if (sequence == -1) {
			Warning($"{target.GetDebugName()}: unknown scripted sequence \"{seq}\"\n");
			sequence = 0;
		}

		Activity act = target.GetSequenceActivity(sequence);
		if (act == Activity.ACT_INVALID)
			act = Activity.ACT_IDLE;

		target.SetActivityAndSequence(act, sequence, act, act);

		if (TargetWasAsleep && seq != PreIdle) {
			TargetWasAsleep = false;
			target.RemoveEffects(EntityEffects.NoDraw);
			target.IncrementInterpolationFrame();
		}

		return true;
	}

	public void SynchronizeSequence(AI_BaseNPC npc) {
		Assert(Delay == 0);
		Assert(WaitForBeginSequence == false);
		ForceSynch = false;

		float cycleRate = npc.GetSequenceCycleRate(npc.GetSequence());
		TimeUnit_t interval = gpGlobals.CurTime - StartTime;

		interval = Math.Clamp(interval, 0.0, 0.15);

		if (interval == 0)
			return;

		npc.SetCycle(0.0f);
		npc.AutoMovement(interval, null, out _);

		npc.SetCycle(interval * cycleRate);
	}

	public bool FinishedActionSequence(AI_BaseNPC npc) {
		if (IsPlayingEntry()) {
			if (!string.IsNullOrEmpty(GetEntityName()))
				SynchNewSequence(ScriptStateType.Playing, Play, true);
			else
				StartSequence(npc, Play, true);
			return false;
		}

		if (ShouldLoopActionSequence()) {
			if (npc.ScriptState == ScriptStateType.PostIdle)
				return true;

			return false;
		}

		return true;
	}

	public void SequenceDone(AI_BaseNPC npc) {
		if (SynchPostIdles && !string.IsNullOrEmpty(GetEntityName())) {
			if (npc.ScriptState != ScriptStateType.PostIdle) {
				if (!string.IsNullOrEmpty(PostIdle) && NextCine.Get() == null)
					SynchNewSequence(ScriptStateType.PostIdle, PostIdle, true);
				else
					PostIdleDone(npc);
			}
		}
		else {
			if (!string.IsNullOrEmpty(PostIdle) && NextCine.Get() == null) {
				npc.ScriptState = ScriptStateType.PostIdle;
				StartSequence(npc, PostIdle, false);
			}
			else
				PostIdleDone(npc);
		}

		OnEndSequence.FireOutput(null, this);
	}

	public void SynchNewSequence(ScriptStateType newState, string? sequence, bool synchOtherScenes) {
		if (synchOtherScenes) {
			BaseEntity? entCine = gEntList.FindEntityByName(null, GetEntityName(), null);
			while (entCine != null) {
				if (entCine is AI_ScriptedSequence scene && scene != this)
					scene.SynchNewSequence(newState, sequence, false);
				entCine = gEntList.FindEntityByName(entCine, GetEntityName(), null);
			}
		}

		if (GetTarget() == null)
			return;
		AI_BaseNPC? npc = GetTarget()!.MyNPCPointer();
		if (npc == null)
			return;

		StartTime = gpGlobals.CurTime;
		npc.ScriptState = newState;
		StartSequence(npc, sequence, false);

		ForceSynch = true;
	}

	public void PostIdleDone(AI_BaseNPC npc) {
		if (!string.IsNullOrEmpty(PostIdle) && HasSpawnFlags(SF.LoopInPostIdle) && NextCine.Get() == null) {
			npc.ScriptState = ScriptStateType.PostIdle;
			StartSequence(npc, PostIdle, false);
		}
		else {
			if (!HasSpawnFlags(SF.Repeatable)) {
				SetThink(SUB_Remove);
				SetNextThink(gpGlobals.CurTime + 0.1f);
				Thinking = false;
				InitiatedSelfDelete = true;
			}

			npc.CineCleanup();

			FixScriptNPCSchedule(npc, SavedFlags);

			if (NextCine.Get() != null) {
				AI_ScriptedSequence nextCine = (AI_ScriptedSequence)NextCine.Get()!;

				if (nextCine != this || HasSpawnFlags(SF.Repeatable)) {
					nextCine.SetTarget(npc);
					nextCine.StartScript();
				}
			}
		}

		OnPostIdleEndSequence.FireOutput(null, this);
	}

	public void FixScriptNPCSchedule(AI_BaseNPC npc, EntityFlags savedCineFlags) {
		if (npc.GetIdealState() != NPCState.Dead)
			npc.SetIdealState(NPCState.Idle);

		if (npc == null)
			return;

		FixFlyFlag(npc, savedCineFlags);

		npc.ClearSchedule("Finished scripted sequence");
	}

	public void FixFlyFlag(AI_BaseNPC npc, EntityFlags savedCineFlags) {
		Task_t? task = npc.GetTask();
		if (task != null && (task.Value.Task == TASK_PLAY_SCRIPT || task.Value.Task == TASK_PLAY_SCRIPT_POST_IDLE)) {
			if ((savedCineFlags & EntityFlags.Fly) == 0) {
				if ((npc.GetFlags() & EntityFlags.Fly) != 0)
					npc.RemoveFlag(EntityFlags.Fly);
			}
		}
	}

	public void AllowInterrupt(bool allow) {
		if (HasSpawnFlags(SF.NoInterrupt))
			return;
		Interruptable = allow;
	}

	public bool CanInterrupt() {
		if (!Interruptable)
			return false;

		BaseEntity? target = GetTarget();

		if (target != null && target.IsAlive())
			return true;

		return false;
	}

	public void RemoveIgnoredConditions() {
		if (CanInterrupt())
			return;

		BaseEntity? entity = GetTarget();
		if (entity == null)
			return;

		AI_BaseNPC? target = entity.MyNPCPointer();
		if (target == null)
			return;

		if (target != null) {
			target.ClearCondition((int)SCOND_t.COND_LIGHT_DAMAGE);
			target.ClearCondition((int)SCOND_t.COND_HEAVY_DAMAGE);
		}
	}

	public bool CanEnqueueAfter() {
		if (NextCine.Get() == null)
			return true;

		if (!string.IsNullOrEmpty(NextScript)) {
			DevMsg(2, $"{NextCine.Get()!.GetDebugName()} is specified as the 'Next Script' and cannot be kicked out of the queue\n");
			return false;
		}

		if (!NextCine.Get()!.HasSpawnFlags(SF.HighPriority))
			return true;

		DevMsg(2, $"{NextCine.Get()!.GetDebugName()} is a priority script and cannot be kicked out of the queue\n");

		return false;
	}

	public void StopActionLoop(bool stopSynchronizedScenes) {
		LoopActionSequence = false;

		if (!stopSynchronizedScenes || string.IsNullOrEmpty(GetEntityName()))
			return;

		BaseEntity? entCine = gEntList.FindEntityByName(null, GetEntityName(), null);
		while (entCine != null) {
			if (entCine is AI_ScriptedSequence scene && scene != this)
				scene.StopActionLoop(false);

			entCine = gEntList.FindEntityByName(entCine, GetEntityName(), null);
		}
	}

	public void ForceSetTargetEntity(AI_BaseNPC target, bool dontCancelOtherSequences) {
		ForcedTarget.Set(target);
		Entity = ForcedTarget.Get()!.GetEntityName();
		DontCancelOtherSequences = dontCancelOtherSequences;
	}

	public void ModifyScriptedAutoMovement(ref Vector3 newPos) {
		if (InteractionRelativeEntity.Get() != null)
			throw new NotImplementedException();
	}

	public void CancelScript() {
		DevMsg(2, $"Cancelling script: {Play}\n");

		bool dontCancelOther = (DontCancelOtherSequences || HasSpawnFlags(SF.AllowDeath)) && StartTime != 0;
		if (dontCancelOther || string.IsNullOrEmpty(GetEntityName())) {
			ScriptEntityCancel(this);
			return;
		}

		BaseEntity? entCineTarget = gEntList.FindEntityByName(null, GetEntityName());

		while (entCineTarget != null) {
			ScriptEntityCancel(entCineTarget);
			entCineTarget = gEntList.FindEntityByName(entCineTarget, GetEntityName());
		}
	}

	public void DelayStart(bool delay) {
		if (ai_task_pre_script.GetBool()) {
			if (delay == Delayed)
				return;

			Delayed = delay;
		}

		if (string.IsNullOrEmpty(GetEntityName())) {
			Delay = delay ? 1 : 0;
			StartTime = gpGlobals.CurTime;
			return;
		}

		BaseEntity? entCine = gEntList.FindEntityByName(null, GetEntityName());

		while (entCine != null) {
			if (FClassnameIs(entCine, "scripted_sequence")) {
				AI_ScriptedSequence target = (AI_ScriptedSequence)entCine;
				if (delay)
					Delay++;
				else {
					target.Delay--;

					if (target.Delay == 0)
						target.StartTime = gpGlobals.CurTime;
				}
			}
			entCine = gEntList.FindEntityByName(entCine, GetEntityName());
		}
	}

	public override void Activate() {
		base.Activate();

		NextCine.Set(gEntList.FindEntityByName(null, NextScript));
		if (NextCine.Get() == null)
			NextScript = null;
	}

	public static string? GetSpawnPreIdleSequenceForScript(BaseEntity entity) {
		AI_ScriptedSequence? script = gEntList.NextEntByClass<AI_ScriptedSequence>(null);
		while (script != null) {
			if (script.HasSpawnFlags(SF.StartOnSpawn) && script.Entity == entity.GetEntityName()) {
				if (script.PreIdle != null)
					return script.PreIdle;
				return null;
			}
			script = gEntList.NextEntByClass(script);
		}
		return null;
	}
}
