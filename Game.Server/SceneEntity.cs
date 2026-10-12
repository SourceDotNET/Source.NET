global using static Game.Server.SceneEntityGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.SceneFileCache;
using Source.Common.Utilities;

using System.Numerics;

namespace Game.Server;

using FIELD = FIELD<SceneEntity>;
using DEFINE = Source.DEFINE<SceneEntity>;
using EventType = Game.Shared.ChoreoEvent.EventType;

public static class SceneEntityGlobals
{
	public static readonly ConVar scene_async_prefetch_spew = new("scene_async_prefetch_spew", "0", FCvar.None, "Display async .ani file loading info.");

	public const float SOUND_SYSTEM_LATENCY_DEFAULT = 0.1f;
	public const TimeUnit_t SCENE_THINK_INTERVAL = 0.001;
	public const int FINDNAMEDENTITY_MAX_ENTITIES = 32;
	public const float SCENE_MIN_PITCH = 0.25f;
	public const float SCENE_MAX_PITCH = 2.5f;

	public static readonly ChoreoStringPool g_ChoreoStringPool = new();

	public static bool CopySceneFileIntoMemory(ReadOnlySpan<char> filename, out byte[]? buffer, out int size) {
		nuint bufSize = scenefilecache.GetSceneBufferSize(filename);
		if (bufSize > 0) {
			buffer = new byte[bufSize];
			size = (int)bufSize;
			return scenefilecache.GetSceneData(filename, buffer);
		}

		buffer = null;
		size = 0;
		return false;
	}

	static readonly HashSet<string> MissingScenes = [];

	public static void MissingSceneWarning(ReadOnlySpan<char> scenename) {
		if (MissingScenes.Add(new string(scenename.SliceNullTerminatedString())))
			Warning($"Scene '{scenename}' missing!\n");
	}

	static Handle<SceneManager> s_SceneManager = new();

	public static SceneManager? GetSceneManager() {
		if (s_SceneManager.Get() == null) {
			s_SceneManager.Set(CreateEntityByName("scene_manager"));
			Assert(s_SceneManager.Get() != null);
			s_SceneManager.Get()?.Spawn();
		}

		Assert(s_SceneManager.Get() != null);
		return s_SceneManager.Get();
	}

	public static void SceneManager_ClientActive(BasePlayer player) {
		Assert(GetSceneManager() != null);

		GetSceneManager()?.OnClientActive(player);
	}

	public static float GetSceneDuration(ReadOnlySpan<char> scene) {
		uint msecs = 0;

		if (scenefilecache.GetSceneCachedData(scene, out SceneCachedData cachedData))
			msecs = cachedData.Msecs;

		return msecs * 0.001f;
	}

	static int nMakingReslists = -1;

	public static void PrecacheInstancedScene(ReadOnlySpan<char> scene) {
		if (nMakingReslists == -1)
			nMakingReslists = Singleton<ICommandLine>().FindParm("-makereslists") > 0 ? 1 : 0;

		if (nMakingReslists == 1)
			filesystem.Size(scene);

		if (scenefilecache.GetSceneCachedData(scene, out SceneCachedData sceneData)) {
			for (int i = 0; i < sceneData.NumSounds; ++i) {
				short stringId = scenefilecache.GetSceneCachedSound(sceneData.SceneId, i);
				BaseEntity.PrecacheScriptSound(scenefilecache.GetSceneString(stringId));
			}
		}

		g_pStringTableClientSideChoreoScenes!.AddString(true, scene);
	}

	public static void RemoveActorFromScriptedScenes(BaseFlex actor, bool instancedscenesonly, bool nonidlescenesonly = false, ReadOnlySpan<char> thisSceneOnly = default) => GetSceneManager()!.RemoveActorFromScenes(actor, instancedscenesonly, nonidlescenesonly, thisSceneOnly);
	public static void RemoveAllScenesInvolvingActor(BaseFlex actor) => GetSceneManager()!.RemoveScenesInvolvingActor(actor);
	public static void PauseActorsScriptedScenes(BaseFlex actor, bool instancedscenesonly) => GetSceneManager()!.PauseActorsScenes(actor, instancedscenesonly);
	public static bool IsInInterruptableScenes(BaseFlex actor) => GetSceneManager()!.IsInInterruptableScenes(actor);
	public static void ResumeActorsScriptedScenes(BaseFlex actor, bool instancedscenesonly) => GetSceneManager()!.ResumeActorsScenes(actor, instancedscenesonly);
	public static void QueueActorsScriptedScenesToResume(BaseFlex actor, bool instancedscenesonly) => GetSceneManager()!.QueueActorsScenesToResume(actor, instancedscenesonly);
	public static bool IsRunningScriptedScene(BaseFlex actor, bool ignoreInstancedScenes = true) => GetSceneManager()!.IsRunningScriptedScene(actor, ignoreInstancedScenes);
	public static bool IsRunningScriptedSceneAndNotPaused(BaseFlex actor, bool ignoreInstancedScenes = true) => GetSceneManager()!.IsRunningScriptedSceneAndNotPaused(actor, ignoreInstancedScenes);
	public static bool IsRunningScriptedSceneWithSpeech(BaseFlex actor, bool ignoreInstancedScenes = false) => GetSceneManager()!.IsRunningScriptedSceneWithSpeech(actor, ignoreInstancedScenes);
	public static bool IsRunningScriptedSceneWithSpeechAndNotPaused(BaseFlex actor, bool ignoreInstancedScenes = false) => GetSceneManager()!.IsRunningScriptedSceneWithSpeechAndNotPaused(actor, ignoreInstancedScenes);
}

public class ChoreoStringPool : IChoreoStringPool
{
	public short FindOrAddString(ReadOnlySpan<char> str) {
		Assert(false);
		return -1;
	}

	public bool GetString(short stringId, Span<char> buff) {
		string? str = scenefilecache.GetSceneString(stringId);
		if (str == null) {
			strcpy(buff, "");
			return false;
		}
		strcpy(buff, str);
		return true;
	}
}

class SceneFindMarkFilter : IEntityFindFilter
{
	public void SetActor(BaseEntity? actor) => Actor.Set(actor);

	public bool ShouldFindEntity(BaseEntity entity) {
		BaseEntity? actor = Actor.Get();
		if (actor == null)
			return true;

		if (EntityFound.Get() == null)
			EntityFound.Set(entity);

		Vector3 origin = entity.GetAbsOrigin();
		Util.TraceHull(origin, origin, actor.WorldAlignMins(), actor.WorldAlignMaxs(), Mask.Solid, actor, CollisionGroup.None, out Trace tr);
		if (tr.StartSolid)
			return false;

		EntityFound.Set(entity);
		return true;
	}

	public BaseEntity? GetFilterResult() => EntityFound.Get();

	EHANDLE Actor = new();
	EHANDLE EntityFound = new();
}

class SceneFindNearestMarkFilter : IEntityFindFilter
{
	public SceneFindNearestMarkFilter(BaseEntity? actor, in Vector3 pos2, float maxRadius = MAX_TRACE_LENGTH) {
		Pos2 = pos2;

		MaxSegmentDistance = maxRadius;

		NearestToTargetDist = maxRadius;
		NearestToTarget = null;
		NearestToActorDist = maxRadius;
		NearestToActor = null;

		Actor.Set(actor);
		if (actor != null) {
			Pos1 = actor.GetAbsOrigin();
			MaxSegmentDistance = Math.Min(maxRadius, (Pos1 - Pos2).Length() + 1.0f);
			if (MaxSegmentDistance <= 1.0f)
				MaxSegmentDistance = Math.Min(maxRadius, MAX_TRACE_LENGTH);
		}
	}

	public bool ShouldFindEntity(BaseEntity entity) {
		BaseEntity? actor = Actor.Get();
		if (actor == null)
			return true;

		NearestToActor ??= entity;

		Vector3 origin = entity.GetAbsOrigin();
		Util.TraceHull(origin, origin, actor.WorldAlignMins(), actor.WorldAlignMaxs(), Mask.Solid, actor, CollisionGroup.None, out Trace tr);
		if (!tr.StartSolid || tr.Ent == actor) {
			float dist1 = (Pos1 - entity.GetAbsOrigin()).Length();
			float dist2 = (Pos2 - entity.GetAbsOrigin()).Length();

			if (dist1 <= NearestToActorDist) {
				NearestToActor = entity;
				NearestToActorDist = dist2;
			}

			if (dist1 <= MaxSegmentDistance && dist2 <= MaxSegmentDistance && dist2 < NearestToTargetDist) {
				NearestToTarget = entity;
				NearestToTargetDist = dist2;
			}
		}

		return false;
	}

	public BaseEntity? GetFilterResult() {
		if (NearestToTarget != null)
			return NearestToTarget;
		return NearestToActor;
	}

	EHANDLE Actor = new();
	Vector3 Pos1;
	Vector3 Pos2;
	float MaxSegmentDistance;
	float NearestToTargetDist;
	BaseEntity? NearestToTarget;
	float NearestToActorDist;
	BaseEntity? NearestToActor;
}

public class SceneListManager : LogicalEntity
{
	public void SceneStarted(BaseEntity sceneOrManager) => throw new NotImplementedException();
}

public class InstancedSceneEntity : SceneEntity
{
	public EHANDLE Owner = new();
}

[LinkEntityToClass("logic_choreographed_scene")]
[LinkEntityToClass("scripted_scene")]
[NetworkName("CSceneEntity")]
public partial class SceneEntity : PointEntity, IChoreoEventCallback
{
	public enum SceneAction
	{
		Unknown = 0,
		Cancel,
		Resume,
	}

	public enum SceneBusyActor
	{
		Default = 0,
		Wait,
		Interrupt,
		InterruptCancel,
	}

	public struct SpeakEventSound
	{
		public string Symbol;
		public float StartTime;
	}

	public const int MAX_ACTORS_IN_SCENE = 16;

	[NetworkName("m_nSceneStringIndex")]
	public int SceneStringIndex;
	[NetworkName("m_bIsPlayingBack")]
	public bool IsPlayingBack;
	[NetworkName("m_bPaused")]
	public bool Paused;
	[NetworkName("m_bMultiplayer")]
	public bool Multiplayer;
	[NetworkName("m_flForceClientTime")]
	public float ForceClientTime;
	[NetworkName("m_hActorList")]
	readonly List<EHANDLE> ActorList = [];

	public static readonly SendTable DT_SceneEntity = new([
		SendPropInt(FIELD.OF(nameof(SceneStringIndex)), 12, PropFlags.Unsigned),
		SendPropBool(FIELD.OF(nameof(IsPlayingBack))),
		SendPropBool(FIELD.OF(nameof(Paused))),
		SendPropBool(FIELD.OF(nameof(Multiplayer))),
		SendPropFloat(FIELD.OF(nameof(ForceClientTime)), 0, PropFlags.NoScale),
		SendPropList<EHANDLE>(FIELD.OF(nameof(ActorList)), MAX_ACTORS_IN_SCENE, SendPropEHandle()),
	]);
	public static new readonly ServerClass ServerClass = new ServerClass(DT_SceneEntity);

	public static readonly new DataMap DataDesc = new(typeof(SceneEntity), BaseEntity.DataDesc, [
		DEFINE.KEYFIELD(nameof(SceneFile), FieldType.String, "SceneFile"),
		DEFINE.KEYFIELD(nameof(ResumeSceneFile), FieldType.String, "ResumeSceneFile"),
		DEFINE.FIELD(nameof(WaitingForThisResumeScene), FieldType.EHandle),
		DEFINE.FIELD(nameof(WaitingForResumeScene), FieldType.Boolean),

		DEFINE.KEYFIELD(nameof(Target1), FieldType.String, "target1"),
		DEFINE.KEYFIELD(nameof(Target2), FieldType.String, "target2"),
		DEFINE.KEYFIELD(nameof(Target3), FieldType.String, "target3"),
		DEFINE.KEYFIELD(nameof(Target4), FieldType.String, "target4"),
		DEFINE.KEYFIELD(nameof(Target5), FieldType.String, "target5"),
		DEFINE.KEYFIELD(nameof(Target6), FieldType.String, "target6"),
		DEFINE.KEYFIELD(nameof(Target7), FieldType.String, "target7"),
		DEFINE.KEYFIELD(nameof(Target8), FieldType.String, "target8"),

		DEFINE.KEYFIELD(nameof(BusyActor), FieldType.Integer, "busyactor"),

		DEFINE.FIELD(nameof(HTarget1), FieldType.EHandle),
		DEFINE.FIELD(nameof(HTarget2), FieldType.EHandle),
		DEFINE.FIELD(nameof(HTarget3), FieldType.EHandle),
		DEFINE.FIELD(nameof(HTarget4), FieldType.EHandle),
		DEFINE.FIELD(nameof(HTarget5), FieldType.EHandle),
		DEFINE.FIELD(nameof(HTarget6), FieldType.EHandle),
		DEFINE.FIELD(nameof(HTarget7), FieldType.EHandle),
		DEFINE.FIELD(nameof(HTarget8), FieldType.EHandle),

		DEFINE.FIELD(nameof(IsPlayingBack), FieldType.Boolean),
		DEFINE.FIELD(nameof(Paused), FieldType.Boolean),
		DEFINE.FIELD(nameof(CurrentTime), FieldType.Float),
		DEFINE.FIELD(nameof(ForceClientTime), FieldType.Float),
		DEFINE.FIELD(nameof(FrameTime), FieldType.Float),
		DEFINE.FIELD(nameof(CancelAtNextInterrupt), FieldType.Boolean),
		DEFINE.FIELD(nameof(Pitch), FieldType.Float),
		DEFINE.FIELD(nameof(Automated), FieldType.Boolean),
		DEFINE.FIELD(nameof(AutomatedAction), FieldType.Integer),
		DEFINE.FIELD(nameof(AutomationDelay), FieldType.Float),
		DEFINE.FIELD(nameof(AutomationTime), FieldType.Float),

		DEFINE.FIELD(nameof(PausedViaInput), FieldType.Boolean),
		DEFINE.FIELD(nameof(WaitingForActor), FieldType.Boolean),
		DEFINE.FIELD(nameof(WaitingForInterrupt), FieldType.Boolean),
		DEFINE.FIELD(nameof(InterruptedActorsScenes), FieldType.Boolean),
		DEFINE.FIELD(nameof(BreakOnNonIdle), FieldType.Boolean),

		DEFINE.UTLVECTOR(nameof(ActorList), FieldType.EHandle),
		DEFINE.UTLVECTOR(nameof(RemoveActorList), FieldType.EHandle),

		DEFINE.FIELD(nameof(InterruptCount), FieldType.Integer),
		DEFINE.FIELD(nameof(Interrupted), FieldType.Boolean),
		DEFINE.FIELD(nameof(InterruptScene), FieldType.EHandle),
		DEFINE.FIELD(nameof(CompletedEarly), FieldType.Boolean),
		DEFINE.FIELD(nameof(InterruptSceneFinished), FieldType.Boolean),

		DEFINE.FIELD(nameof(Generated), FieldType.Boolean),
		DEFINE.FIELD(nameof(SoundName), FieldType.String),
		DEFINE.FIELD(nameof(Actor), FieldType.EHandle),
		DEFINE.FIELD(nameof(Activator), FieldType.EHandle),

		DEFINE.UTLVECTOR(nameof(NotifySceneCompletion), FieldType.EHandle),
		DEFINE.UTLVECTOR(nameof(ListManagers), FieldType.EHandle),

		DEFINE.FIELD(nameof(Multiplayer), FieldType.Boolean),

		DEFINE.INPUTFUNC(FieldType.Void, "Start", nameof(InputStartPlayback), (INPUTFUNCPTR)((self, data) => ((SceneEntity)self).InputStartPlayback(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Pause", nameof(InputPausePlayback), (INPUTFUNCPTR)((self, data) => ((SceneEntity)self).InputPausePlayback(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Resume", nameof(InputResumePlayback), (INPUTFUNCPTR)((self, data) => ((SceneEntity)self).InputResumePlayback(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "Cancel", nameof(InputCancelPlayback), (INPUTFUNCPTR)((self, data) => ((SceneEntity)self).InputCancelPlayback(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "CancelAtNextInterrupt", nameof(InputCancelAtNextInterrupt), (INPUTFUNCPTR)((self, data) => ((SceneEntity)self).InputCancelAtNextInterrupt(data))),
		DEFINE.INPUTFUNC(FieldType.Float, "PitchShift", nameof(InputPitchShiftPlayback), (INPUTFUNCPTR)((self, data) => ((SceneEntity)self).InputPitchShiftPlayback(data))),
		DEFINE.INPUTFUNC(FieldType.Void, "StopWaitingForActor", nameof(InputStopWaitingForActor), (INPUTFUNCPTR)((self, data) => ((SceneEntity)self).InputStopWaitingForActor(data))),
		DEFINE.INPUTFUNC(FieldType.Integer, "Trigger", nameof(InputTriggerEvent), (INPUTFUNCPTR)((self, data) => ((SceneEntity)self).InputTriggerEvent(data))),

		DEFINE.KEYFIELD(nameof(PlayerDeathBehavior), FieldType.Integer, "onplayerdeath"),
		DEFINE.INPUTFUNC(FieldType.Void, "ScriptPlayerDeath", nameof(InputScriptPlayerDeath), (INPUTFUNCPTR)((self, data) => ((SceneEntity)self).InputScriptPlayerDeath(data))),

		DEFINE.OUTPUT(nameof(OnStart), "OnStart", eventFuncs),
		DEFINE.OUTPUT(nameof(OnCompletion), "OnCompletion", eventFuncs),
		DEFINE.OUTPUT(nameof(OnCanceled), "OnCanceled", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger1), "OnTrigger1", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger2), "OnTrigger2", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger3), "OnTrigger3", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger4), "OnTrigger4", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger5), "OnTrigger5", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger6), "OnTrigger6", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger7), "OnTrigger7", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger8), "OnTrigger8", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger9), "OnTrigger9", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger10), "OnTrigger10", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger11), "OnTrigger11", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger12), "OnTrigger12", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger13), "OnTrigger13", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger14), "OnTrigger14", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger15), "OnTrigger15", eventFuncs),
		DEFINE.OUTPUT(nameof(OnTrigger16), "OnTrigger16", eventFuncs),
	]);
	public override DataMap? GetDataDescMap() => DataDesc;

	public string? SceneFile;

	public string? ResumeSceneFile;
	public EHANDLE WaitingForThisResumeScene = new();
	public bool WaitingForResumeScene;

	public string? Target1;
	public string? Target2;
	public string? Target3;
	public string? Target4;
	public string? Target5;
	public string? Target6;
	public string? Target7;
	public string? Target8;

	public EHANDLE HTarget1 = new();
	public EHANDLE HTarget2 = new();
	public EHANDLE HTarget3 = new();
	public EHANDLE HTarget4 = new();
	public EHANDLE HTarget5 = new();
	public EHANDLE HTarget6 = new();
	public EHANDLE HTarget7 = new();
	public EHANDLE HTarget8 = new();

	public TimeUnit_t CurrentTime;
	public TimeUnit_t FrameTime;
	public bool CancelAtNextInterrupt;

	public float Pitch;

	public bool Automated;
	public SceneAction AutomatedAction;
	public float AutomationDelay;
	public TimeUnit_t AutomationTime;

	public bool PausedViaInput;

	public bool WaitingForActor;

	public bool WaitingForInterrupt;
	public bool InterruptedActorsScenes;

	public bool BreakOnNonIdle;

	readonly List<EHANDLE> RemoveActorList = [];

	bool SceneMissing;

	ChoreoScene? Scene;

	static ConVar? SndMixahead;

	public OutputEvent OnStart = new();
	public OutputEvent OnCompletion = new();
	public OutputEvent OnCanceled = new();
	public OutputEvent OnTrigger1 = new();
	public OutputEvent OnTrigger2 = new();
	public OutputEvent OnTrigger3 = new();
	public OutputEvent OnTrigger4 = new();
	public OutputEvent OnTrigger5 = new();
	public OutputEvent OnTrigger6 = new();
	public OutputEvent OnTrigger7 = new();
	public OutputEvent OnTrigger8 = new();
	public OutputEvent OnTrigger9 = new();
	public OutputEvent OnTrigger10 = new();
	public OutputEvent OnTrigger11 = new();
	public OutputEvent OnTrigger12 = new();
	public OutputEvent OnTrigger13 = new();
	public OutputEvent OnTrigger14 = new();
	public OutputEvent OnTrigger15 = new();
	public OutputEvent OnTrigger16 = new();

	int InterruptCount;
	bool Interrupted;
	Handle<SceneEntity> InterruptScene = new();

	bool CompletedEarly;

	bool InterruptSceneFinished;
	readonly List<Handle<SceneEntity>> NotifySceneCompletion = [];
	readonly List<Handle<SceneListManager>> ListManagers = [];

	bool Restoring;

	bool Generated;
	string? SoundName;
	Handle<BaseFlex> Actor = new();

	EHANDLE Activator = new();

	SceneBusyActor BusyActor;

	ScriptPlayerDeath PlayerDeathBehavior;

	RecipientFilter? RecipientFilter;

	public SceneEntity() {
		WaitingForActor = false;
		WaitingForInterrupt = false;
		InterruptedActorsScenes = false;
		IsPlayingBack = false;
		Paused = false;
		Multiplayer = false;
		Pitch = 1.0f;
		SceneFile = null;
		ResumeSceneFile = null;
		WaitingForThisResumeScene.Set(null);
		WaitingForResumeScene = false;
		SetCurrentTime(0.0, false);
		CancelAtNextInterrupt = false;

		Automated = false;
		AutomatedAction = SceneAction.Unknown;
		AutomationDelay = 0.0f;
		AutomationTime = 0.0;

		PausedViaInput = false;
		ClearInterrupt();

		Scene = null;

		CompletedEarly = false;

		SndMixahead ??= cvar.FindVar("snd_mixahead");

		BusyActor = SceneBusyActor.Default;
	}

	public bool IsPaused() => Paused;
	public bool IsMultiplayer() => Multiplayer;

	public void SetBreakOnNonIdle(bool breakOnNonIdle) => BreakOnNonIdle = breakOnNonIdle;
	public bool ShouldBreakOnNonIdle() => BreakOnNonIdle;

	public virtual float GetPostSpeakDelay() => 1.0f;

	public override void Think() { }

	public void SetCurrentTime(TimeUnit_t t, bool forceClientSync) {
		CurrentTime = t;
		if (gpGlobals.MaxClients == 1 || forceClientSync)
			ForceClientTime = (float)t;
	}

	public override void UpdateOnRemove() {
		UnloadScene();
		base.UpdateOnRemove();

		GetSceneManager()?.RemoveSceneEntity(this);
	}

	ChoreoScene? GenerateSceneForSound(BaseFlex? flexActor, ReadOnlySpan<char> soundname) => throw new NotImplementedException();

	public override void Activate() {
		if (Generated && Scene == null)
			Scene = GenerateSceneForSound(Actor.Get(), SoundName);

		base.Activate();

		GetSceneManager()?.AddSceneEntity(this);
	}

	float GetSoundSystemLatency() {
		if (SndMixahead != null)
			return SndMixahead.GetFloat();

		return SOUND_SYSTEM_LATENCY_DEFAULT;
	}

	public override void Precache() {
		if (Generated)
			return;

		if (string.IsNullOrEmpty(SceneFile))
			return;

		if (!string.IsNullOrEmpty(ResumeSceneFile))
			PrecacheInstancedScene(ResumeSceneFile);

		PrecacheInstancedScene(SceneFile);
	}

	public void GenerateSoundScene(BaseFlex? actor, ReadOnlySpan<char> soundname) {
		Generated = true;
		SoundName = new string(soundname);
		Actor.Set(actor);
	}

	public bool HasUnplayedSpeech() {
		if (Scene != null)
			return Scene.HasUnplayedSpeech();

		return false;
	}

	public bool HasFlexAnimation() {
		if (Scene != null)
			return Scene.HasFlexAnimation();

		return false;
	}

	public void SetBackground(bool isBackground) => Scene?.SetBackground(isBackground);

	public bool IsBackground() {
		if (Scene != null)
			return Scene.IsBackground();

		return false;
	}

	public void SetRestoring(bool restoring) {
		Restoring = restoring;
		Scene?.SetRestoring(restoring);
	}

	public override void Spawn() {
		Precache();
	}

	public virtual void PauseThink() {
		if (Scene == null)
			return;

		if (Interrupted)
			return;

		if (PausedViaInput) {
			if (WaitingForResumeScene && WaitingForThisResumeScene.Get() == null)
				WaitingForResumeScene = false;
			else
				return;
		}

		if (!Automated) {
			bool allFinished = Scene.CheckEventCompletion();

			if (allFinished) {
				switch (AutomatedAction) {
					case SceneAction.Resume:
						ResumePlayback();
						break;
					case SceneAction.Cancel:
						Scene_Printf($"{SceneFile} : PauseThink canceling playback\n");
						CancelPlayback();
						break;
					default:
						ResumePlayback();
						break;
				}

				Automated = false;
				AutomatedAction = SceneAction.Unknown;
				AutomationTime = 0.0;
				AutomationDelay = 0.0f;
				PausedViaInput = false;
			}
			return;
		}

		AutomationTime += gpGlobals.FrameTime;

		if (AutomationDelay > 0.0f && AutomationTime < AutomationDelay)
			return;

		switch (AutomatedAction) {
			case SceneAction.Resume:
				Scene_Printf($"{SceneFile} : Automatically resuming playback\n");
				ResumePlayback();
				break;
			case SceneAction.Cancel:
				Scene_Printf($"{SceneFile} : Automatically canceling playback\n");
				CancelPlayback();
				break;
			default:
				Scene_Printf($"{SceneFile} : Unknown action {(int)AutomatedAction}, automatically resuming playback\n");
				ResumePlayback();
				break;
		}

		Automated = false;
		AutomatedAction = SceneAction.Unknown;
		AutomationTime = 0.0;
		AutomationDelay = 0.0f;
		PausedViaInput = false;
	}

	public bool IsInterruptable() => InterruptCount > 0;

	public virtual float EstimateLength() {
		if (Scene == null)
			return GetSceneDuration(SceneFile);

		return Scene.FindStopTime();
	}

	public void CancelIfSceneInvolvesActor(BaseEntity actor) {
		if (InvolvesActor(actor)) {
			Scene_Printf($"{SceneFile} : cancelled for '{actor.GetDebugName()}'\n");
			CancelPlayback();
		}
	}

	public bool InvolvesActor(BaseEntity? actor) {
		if (Scene == null)
			return false;

		for (int i = 0; i < Scene.GetNumActors(); i++) {
			BaseFlex? testActor = FindNamedActor(i);
			if (testActor == null)
				continue;

			if (testActor == actor)
				return true;
		}
		return false;
	}

	public virtual void DoThink(TimeUnit_t frametime) {
		CheckInterruptCompletion();

		if (WaitingForActor || WaitingForInterrupt)
			StartPlayback();

		if (Scene == null)
			return;

		if (!IsPlayingBack)
			return;

		Assert(Pitch >= SCENE_MIN_PITCH && Pitch <= SCENE_MAX_PITCH);
		Pitch = Math.Clamp(Pitch, SCENE_MIN_PITCH, SCENE_MAX_PITCH);

		if (Paused) {
			PauseThink();
			return;
		}

		FrameTime = frametime;

		Scene.SetSoundFileStartupLatency(GetSoundSystemLatency());

		Scene.Think(CurrentTime);

		if (!Paused) {
			SetCurrentTime(CurrentTime + FrameTime * Pitch, false);

			if (Scene.SimulationFinished()) {
				OnSceneFinished(false, true);

				ClearSchedules(Scene);
			}
		}
		else
			SetCurrentTime(Scene.GetTime(), true);
	}

	public void InputStartPlayback(in InputData inputdata) {
		if (IsPlayingBack)
			return;

		if (WaitingForActor || WaitingForInterrupt)
			return;

		ClearActivatorTargets();
		Activator.Set(inputdata.Activator);
		StartPlayback();
	}

	public void InputPausePlayback(in InputData inputdata) {
		PausePlayback();
		PausedViaInput = true;
	}

	public void InputResumePlayback(in InputData inputdata) => ResumePlayback();

	public void InputCancelPlayback(in InputData inputdata) {
		Scene_Printf($"{SceneFile} : cancelled via input\n");
		CancelPlayback();
	}

	public void InputScriptPlayerDeath(in InputData inputdata) {
		if (PlayerDeathBehavior == ScriptPlayerDeath.Cancel) {
			Scene_Printf($"{SceneFile} : cancelled via player death\n");
			CancelPlayback();
		}
	}

	public void InputCancelAtNextInterrupt(in InputData inputdata) {
		if (IsInterruptable()) {
			Scene_Printf($"{SceneFile} : cancelled via input at interrupt point\n");
			CancelPlayback();
			return;
		}

		CancelAtNextInterrupt = true;
	}

	public void InputPitchShiftPlayback(in InputData inputdata) => PitchShiftPlayback(inputdata.Value.Float());

	public void InputTriggerEvent(in InputData inputdata) {
		BaseEntity activator = this;
		switch (inputdata.Value.Int()) {
			case 1:
				OnTrigger1.FireOutput(activator, this, 0);
				break;
			case 2:
				OnTrigger2.FireOutput(activator, this, 0);
				break;
			case 3:
				OnTrigger3.FireOutput(activator, this, 0);
				break;
			case 4:
				OnTrigger4.FireOutput(activator, this, 0);
				break;
			case 5:
				OnTrigger5.FireOutput(activator, this, 0);
				break;
			case 6:
				OnTrigger6.FireOutput(activator, this, 0);
				break;
			case 7:
				OnTrigger7.FireOutput(activator, this, 0);
				break;
			case 8:
				OnTrigger8.FireOutput(activator, this, 0);
				break;
			case 9:
				OnTrigger9.FireOutput(activator, this, 0);
				break;
			case 10:
				OnTrigger10.FireOutput(activator, this, 0);
				break;
			case 11:
				OnTrigger11.FireOutput(activator, this, 0);
				break;
			case 12:
				OnTrigger12.FireOutput(activator, this, 0);
				break;
			case 13:
				OnTrigger13.FireOutput(activator, this, 0);
				break;
			case 14:
				OnTrigger14.FireOutput(activator, this, 0);
				break;
			case 15:
				OnTrigger15.FireOutput(activator, this, 0);
				break;
			case 16:
				OnTrigger16.FireOutput(activator, this, 0);
				break;
		}
	}

	public void InputStopWaitingForActor(in InputData inputdata) {
		if (IsPlayingBack)
			return;

		WaitingForActor = false;
	}

	bool CheckActors() {
		Assert(Scene != null);
		if (Scene == null)
			return false;

		for (int i = 0; i < Scene.GetNumActors(); i++) {
			BaseFlex? testActor = FindNamedActor(i);
			if (testActor == null)
				continue;

			BaseCombatCharacter? combatCharacter = testActor as BaseCombatCharacter;
			if (combatCharacter == null)
				continue;

			if (!combatCharacter.IsAlive())
				return false;

			if (BusyActor == SceneBusyActor.Wait) {
				AI_BaseNPC? actor = testActor.MyNPCPointer();

				if (actor != null) {
					bool shouldWait = false;
					if (hl2_episodic.GetBool()) {
						if (IsRunningScriptedSceneWithSpeech(actor))
							shouldWait = true;
					}

					if (actor.GetExpresser() != null && actor.GetExpresser()!.IsSpeaking())
						shouldWait = true;

					if (shouldWait) {
						WaitingForActor = true;
						return false;
					}
				}
			}
			else if (BusyActor == SceneBusyActor.Interrupt || BusyActor == SceneBusyActor.InterruptCancel) {
				BaseCombatCharacter actor = combatCharacter;
				if (!IsInInterruptableScenes(actor)) {
					WaitingForInterrupt = true;
					return false;
				}

				if (BusyActor == SceneBusyActor.InterruptCancel)
					RemoveActorFromScriptedScenes(actor, false);
				else {
					PauseActorsScriptedScenes(actor, false);
					InterruptedActorsScenes = true;
				}
			}

			testActor.StartChoreoScene(Scene);
		}

		return true;
	}

	void PrefetchAnimBlocks(ChoreoScene scene) {
		Assert(scene != null);

		Dictionary<ChoreoActor, BaseFlex?> actorMap = [];

		int spew = scene_async_prefetch_spew.GetInt();

		int resident = 0;
		int checkedCount = 0;

		for (int i = 0; i < scene.GetNumEvents(); i++) {
			ChoreoEvent? ev = scene.GetEvent(i);
			if (ev == null)
				continue;

			switch (ev.GetType()) {
				default:
					break;
				case EventType.Sequence:
				case EventType.Gesture: {
						ChoreoActor? actor = ev.GetActor();
						if (actor != null) {
							if (!actorMap.TryGetValue(actor, out BaseFlex? flexActor)) {
								flexActor = FindNamedActor(actor);
								actorMap[actor] = flexActor;
							}

							if (flexActor != null) {
								int seq = flexActor.LookupSequence(ev.GetParameters());
								if (seq >= 0) {
									StudioHdr? studioHdr = flexActor.GetModelPtr();
									if (studioHdr != null) {
										MStudioSeqDesc seqdesc = studioHdr.Seqdesc(seq);
										for (int x = 0; x < seqdesc.GroupSize[0]; ++x) {
											for (int y = 0; y < seqdesc.GroupSize[1]; ++y) {
												int animation = seqdesc.Anim(x, y);
												int baseanimation = studioHdr.iRelativeAnim(seq, animation);
												MStudioAnimDesc animdesc = studioHdr.Animdesc(baseanimation);

												++checkedCount;

												if (spew != 0)
													Msg($"{studioHdr.Name()} checking block {animdesc.AnimBlock}\n");

												int frame = 0;
												MStudioAnim? anim = animdesc.Anim(ref frame);
												if (anim != null) {
													++resident;
													if (spew > 1)
														Msg($"{studioHdr.Name()}:{animdesc.Name()}[{x}:{y}] was resident\n");
												}
												else {
													if (spew != 0)
														Msg($"{studioHdr.Name()}:{animdesc.Name()}[{x}:{y}] async load\n");
												}
											}
										}
									}
								}
							}
						}
					}
					break;
			}
		}

		if (spew == 0 || checkedCount <= 0)
			return;

		Msg($"{resident} of {checkedCount} animations resident\n");
	}

	public virtual void OnLoaded() { }

	public virtual void StartPlayback() {
		if (Scene == null) {
			if (SceneMissing)
				return;

			Scene = LoadScene(SceneFile, this);
			if (Scene == null) {
				DevMsg($"{SceneFile} missing from scenes.image\n");
				SceneMissing = true;
				return;
			}

			OnLoaded();

			if (ShouldNetwork())
				SceneStringIndex = g_pStringTableClientSideChoreoScenes!.AddString(IsServer(), SceneFile);

			UpdateTransmitState();
		}

		if (IsPlayingBack)
			return;

		if (!CheckActors())
			return;

		CompletedEarly = false;
		WaitingForActor = false;
		WaitingForInterrupt = false;
		IsPlayingBack = true;
		NetworkProp().NetworkStateForceUpdate();
		Paused = false;
		SetCurrentTime(0.0, true);
		Scene.ResetSimulation();
		ClearInterrupt();

		ClearSceneEvents(Scene, false);

		OnStart.FireOutput(this, this, 0);

		List<SpeakEventSound> soundnames = [];

		BuildSortedSpeakEventSoundsPrefetchList(Scene, soundnames, 0.0f);
		PrefetchSpeakEventSounds(soundnames);

		int c = ListManagers.Count;
		for (int i = 0; i < c; i++)
			ListManagers[i].Get()?.SceneStarted(this);

		PrefetchAnimBlocks(Scene);
	}

	public static bool SpeakEventSoundLessFunc(in SpeakEventSound lhs, in SpeakEventSound rhs) => lhs.StartTime < rhs.StartTime;

	public void PrefetchSpeakEventSounds(List<SpeakEventSound> soundnames) {
		for (int i = 0; i < soundnames.Count; i++)
			PrefetchScriptSound(soundnames[i].Symbol);
	}

	public void BuildSortedSpeakEventSoundsPrefetchList(ChoreoScene scene, List<SpeakEventSound> soundnames, float timeOffset) {
		Assert(scene != null);

		Span<char> soundname = stackalloc char[ChoreoEvent.MAX_CCTOKEN_STRING];

		for (int i = 0; i < scene.GetNumEvents(); i++) {
			ChoreoEvent? ev = scene.GetEvent(i);
			if (ev == null)
				continue;

			switch (ev.GetType()) {
				default:
					break;
				case EventType.Speak: {
						strcpy(soundname, ev.GetParameters());

						SpeakEventSound ses;
						ses.Symbol = new string(((ReadOnlySpan<char>)soundname).SliceNullTerminatedString());
						ses.StartTime = timeOffset + ev.GetStartTime();

						int insertAt = soundnames.Count;
						while (insertAt > 0 && SpeakEventSoundLessFunc(ses, soundnames[insertAt - 1]))
							insertAt--;
						soundnames.Insert(insertAt, ses);
					}
					break;
				case EventType.SubScene: {
						if (!scene.IsSubScene()) {
							ChoreoScene? subscene = ev.GetSubScene();
							if (subscene == null) {
								subscene = LoadScene(ev.GetParameters(), this);
								subscene!.SetSubScene(true);
								ev.SetSubScene(subscene);

								BuildSortedSpeakEventSoundsPrefetchList(subscene, soundnames, ev.GetStartTime());
							}
						}
					}
					break;
			}
		}
	}

	public virtual void PausePlayback() {
		if (!IsPlayingBack)
			return;

		if (Paused)
			return;

		Paused = true;
	}

	public virtual void ResumePlayback() {
		if (!IsPlayingBack)
			return;

		if (!Paused)
			return;

		Assert(Scene != null);
		if (Scene == null)
			return;

		Scene.ResumeSimulation();

		Paused = false;
		PausedViaInput = false;
	}

	public virtual void CancelPlayback() {
		if (!IsPlayingBack)
			return;

		IsPlayingBack = false;
		Paused = false;

		OnCanceled.FireOutput(this, this, 0);

		Scene_Printf($"{SceneFile} : {CurrentTime,8:F2}:  canceled\n");

		OnSceneFinished(true, false);
	}

	public virtual void PitchShiftPlayback(float fPitch) {
		fPitch = Math.Clamp(fPitch, SCENE_MIN_PITCH, SCENE_MAX_PITCH);

		Pitch = fPitch;

		if (Scene == null)
			return;

		Span<char> buff = stackalloc char[256];

		for (int iActor = 0; iActor < Scene.GetNumActors(); ++iActor) {
			BaseFlex? testActor = FindNamedActor(iActor);

			if (testActor == null)
				continue;

			if (Scene.GetPlayingSoundName(buff)) {
				PASAttenuationFilter filter = new(testActor);
				scoped EmitSound_t parms = new();
				parms.SoundName = ((ReadOnlySpan<char>)buff).SliceNullTerminatedString();
				parms.Pitch = (int)(100.0f * fPitch);
				parms.Flags = SoundFlags.ChangePitch;
				BaseEntity.EmitSound(filter, testActor.EntIndex(), in parms);
			}
		}
	}

	public virtual void QueueResumePlayback() {
		if (!string.IsNullOrEmpty(ResumeSceneFile))
			throw new NotImplementedException();
		else
			ResumePlayback();
	}

	public bool ValidScene() => Scene != null;

	bool ShouldNetwork() {
		if (Multiplayer) {
			if (Scene != null &&
				(Scene.HasEventsOfType(EventType.FlexAnimation) ||
				 Scene.HasEventsOfType(EventType.Expression) ||
				 Scene.HasEventsOfType(EventType.Gesture) ||
				 Scene.HasEventsOfType(EventType.Sequence)))
				return true;
		}
		else {
			if (Scene != null &&
				(Scene.HasEventsOfType(EventType.FlexAnimation) ||
				 Scene.HasEventsOfType(EventType.Expression)))
				return true;
		}

		return false;
	}

	public static ChoreoScene? LoadScene(ReadOnlySpan<char> filename, IChoreoEventCallback? callback) {
		Span<char> loadfile = stackalloc char[MAX_PATH];
		strcpy(loadfile, filename);
		StrTools.SetExtension(loadfile, ".vcd");
		StrTools.FixSlashes(loadfile);
		ReadOnlySpan<char> loadfileName = ((ReadOnlySpan<char>)loadfile).SliceNullTerminatedString();

		ChoreoScene? scene;
		long fileSize = filesystem.Size(loadfileName, "GAME");
		byte[]? buffer = fileSize > 0 ? new byte[fileSize] : null;
		if (buffer != null && filesystem.ReadFile(loadfileName, "GAME", buffer, 0)) {
			g_TokenProcessor.SetBuffer(buffer);
			scene = ChoreoSceneGlobals.ChoreoLoadScene(loadfileName, null, g_TokenProcessor, Scene_Printf);
		}
		else {
			if (!CopySceneFileIntoMemory(loadfileName, out buffer, out _)) {
				MissingSceneWarning(loadfileName);
				return null;
			}

			scene = new ChoreoScene(null);
			UtlBuffer buf = new(buffer!, UtlBuffer.BufferFlags.ReadOnly);
			if (!scene.RestoreFromBinaryBuffer(buf, loadfileName, g_ChoreoStringPool)) {
				Warning($"CSceneEntity::LoadScene: Unable to load binary scene '{loadfileName}'\n");
				scene = null;
			}
		}

		if (scene != null) {
			scene.SetPrintFunc(Scene_Printf);
			scene.SetEventCallbackInterface(callback);
		}

		return scene;
	}

	public void UnloadScene() {
		if (Scene != null) {
			ClearSceneEvents(Scene, false);

			for (int i = 0; i < Scene.GetNumActors(); i++) {
				BaseFlex? testActor = FindNamedActor(i);

				if (testActor == null)
					continue;

				testActor.RemoveChoreoScene(Scene);
			}
		}
		Scene = null;
	}

	public virtual BaseFlex? FindNamedActor(int index) {
		if (ActorList.Count == 0) {
			int count = Scene!.GetNumActors();
			for (int i = 0; i < count; i++)
				ActorList.Add(new EHANDLE());
			NetworkProp().NetworkStateForceUpdate();
		}

		if (index < 0 || index >= ActorList.Count) {
			DevWarning($"Scene {Scene!.GetFilename()} has {Scene.GetNumActors()} actors, but scene entity only has {ActorList.Count} actors\n");
			return null;
		}

		BaseFlex? actor = ActorList[index].Get() as BaseFlex;

		if (actor == null || !actor.IsAlive()) {
			ChoreoActor? choreoActor = Scene!.GetActor(index);
			if (choreoActor == null)
				return null;

			actor = FindNamedActor(choreoActor.GetName());

			if (actor != null) {
				ActorList[index] = actor.GetRefEHandle();
				NetworkProp().NetworkStateForceUpdate();
			}
		}

		return actor;
	}

	public virtual BaseFlex? FindNamedActor(ChoreoActor? choreoActor) {
		int index = Scene!.FindActorIndex(choreoActor!);

		if (index >= 0)
			return FindNamedActor(index);

		return null;
	}

	public virtual BaseFlex? FindNamedActor(ReadOnlySpan<char> name) {
		BaseEntity? entity = FindNamedEntity(name, null, true);

		if (entity == null)
			return null;

		BaseFlex? flexEntity = entity as BaseFlex;
		if (flexEntity == null)
			return null;

		return flexEntity;
	}

	public BaseEntity? FindNamedTarget(ReadOnlySpan<char> target, bool baseFlexOnly = false) {
		if (stricmp(target, "!activator") == 0)
			return Activator.Get();

		if (!target.Contains('*'))
			return gEntList.FindEntityByName(null, target);

		BaseEntity? targetEnt = null;
		while ((targetEnt = gEntList.FindEntityByName(targetEnt, target)) != null) {
			if (baseFlexOnly) {
				if (targetEnt is BaseFlex)
					return targetEnt;
			}
			else
				return targetEnt;
		}

		return null;
	}

	public virtual BaseEntity? FindNamedEntity(ReadOnlySpan<char> name, BaseEntity? actor = null, bool baseFlexOnly = false, bool useClear = false) {
		BaseEntity? entity = null;

		if (stricmp(name, "Player") == 0 || stricmp(name, "!player") == 0) {
			entity = (actor ?? this).AI_GetClosestPlayer();
		}
		else if (stricmp(name, "!target1") == 0) {
			if (HTarget1.Get() == null)
				HTarget1.Set(FindNamedTarget(Target1, baseFlexOnly));
			return HTarget1.Get();
		}
		else if (stricmp(name, "!target2") == 0) {
			if (HTarget2.Get() == null)
				HTarget2.Set(FindNamedTarget(Target2, baseFlexOnly));
			return HTarget2.Get();
		}
		else if (stricmp(name, "!target3") == 0) {
			if (HTarget3.Get() == null)
				HTarget3.Set(FindNamedTarget(Target3, baseFlexOnly));
			return HTarget3.Get();
		}
		else if (stricmp(name, "!target4") == 0) {
			if (HTarget4.Get() == null)
				HTarget4.Set(FindNamedTarget(Target4, baseFlexOnly));
			return HTarget4.Get();
		}
		else if (stricmp(name, "!target5") == 0) {
			if (HTarget5.Get() == null)
				HTarget5.Set(FindNamedTarget(Target5, baseFlexOnly));
			return HTarget5.Get();
		}
		else if (stricmp(name, "!target6") == 0) {
			if (HTarget6.Get() == null)
				HTarget6.Set(FindNamedTarget(Target6, baseFlexOnly));
			return HTarget6.Get();
		}
		else if (stricmp(name, "!target7") == 0) {
			if (HTarget7.Get() == null)
				HTarget7.Set(FindNamedTarget(Target7, baseFlexOnly));
			return HTarget7.Get();
		}
		else if (stricmp(name, "!target8") == 0) {
			if (HTarget8.Get() == null)
				HTarget8.Set(FindNamedTarget(Target8, baseFlexOnly));
			return HTarget8.Get();
		}
		else if (actor != null && actor.MyNPCPointer() != null) {
			SceneFindMarkFilter? filter = null;
			if (useClear) {
				filter = new SceneFindMarkFilter();
				filter.SetActor(actor);
			}

			entity = actor.MyNPCPointer()!.FindNamedEntity(name, filter);
			if (entity == null && filter != null)
				entity = filter.GetFilterResult();
		}
		else {
			BaseEntity?[] entityList = new BaseEntity?[FINDNAMEDENTITY_MAX_ENTITIES];
			int count;

			entity = null;
			for (count = 0; count < FINDNAMEDENTITY_MAX_ENTITIES; count++) {
				entity = gEntList.FindEntityByName(entity, name, null, actor);
				if (entity == null)
					break;
				entityList[count] = entity;
			}

			if (count > 0)
				entity = entityList[RandomInt(0, count - 1)];
			else
				entity = null;
		}

		return entity;
	}

	public virtual BaseEntity? FindNamedEntityClosest(ReadOnlySpan<char> name, BaseEntity? actor = null, bool baseFlexOnly = false, bool useClear = false, ReadOnlySpan<char> secondary = default) {
		BaseEntity? entity = null;

		if (stricmp(name, "!activator") == 0)
			return Activator.Get();
		else if (stricmp(name, "Player") == 0 || stricmp(name, "!player") == 0) {
			return (actor ?? this).AI_GetClosestPlayer();
		}
		else if (stricmp(name, "!target1") == 0)
			name = Target1;
		else if (stricmp(name, "!target2") == 0)
			name = Target2;
		else if (stricmp(name, "!target3") == 0)
			name = Target3;
		else if (stricmp(name, "!target4") == 0)
			name = Target4;
		else if (stricmp(name, "!target5") == 0)
			name = Target5;
		else if (stricmp(name, "!target6") == 0)
			name = Target6;
		else if (stricmp(name, "!target7") == 0)
			name = Target7;

		if (actor != null && actor.MyNPCPointer() != null) {
			if (strlen(secondary) > 0) {
				BaseEntity? actor2 = FindNamedEntityClosest(secondary, actor, false, false, default);

				if (actor2 != null) {
					SceneFindNearestMarkFilter filter = new(actor, actor2.GetAbsOrigin());

					entity = actor.MyNPCPointer()!.FindNamedEntity(name, filter);
					if (entity == null)
						entity = filter.GetFilterResult();
				}
			}
			if (entity == null) {
				SceneFindMarkFilter? filter = null;
				if (useClear) {
					filter = new SceneFindMarkFilter();
					filter.SetActor(actor);
				}

				entity = actor.MyNPCPointer()!.FindNamedEntity(name, filter);
				if (entity == null && filter != null)
					entity = filter.GetFilterResult();
			}
		}
		else {
			int count;
			entity = null;
			BaseEntity? current = null;
			for (count = 0; count < FINDNAMEDENTITY_MAX_ENTITIES; count++) {
				current = gEntList.FindEntityByName(current, name, null, actor);
				if (current != null) {
					if (RandomInt(0, count) == 0)
						entity = current;
				}
			}

			entity = null;
		}

		return entity;
	}

	void ClearSceneEvents(ChoreoScene scene, bool canceled) {
		if (Scene == null)
			return;

		Scene_Printf($"{SceneFile} : {CurrentTime,8:F2}:  clearing events\n");

		for (int i = 0; i < Scene.GetNumActors(); i++) {
			BaseFlex? actor = FindNamedActor(i);
			if (actor == null)
				continue;

			actor.ClearSceneEvents(scene, canceled);
		}

		for (int i = 0; i < scene.GetNumEvents(); i++) {
			ChoreoEvent? ev = scene.GetEvent(i);
			if (ev == null)
				continue;

			switch (ev.GetType()) {
				default:
					break;
				case EventType.SubScene: {
						if (!scene.IsSubScene()) {
							ChoreoScene? subscene = ev.GetSubScene();
							if (subscene != null)
								ClearSceneEvents(subscene, canceled);
						}
					}
					break;
			}
		}
	}

	void ClearSchedules(ChoreoScene scene) {
		if (Scene == null)
			return;

		for (int i = 0; i < Scene.GetNumActors(); i++) {
			BaseFlex? actor = FindNamedActor(i);
			if (actor == null)
				continue;

			AI_BaseNPC? npc = actor.MyNPCPointer();

			if (npc == null) {
				actor.ResetSequence(actor.SelectWeightedSequence(Activity.ACT_IDLE));
				actor.SetCycle(0);
			}
		}

		for (int i = 0; i < scene.GetNumEvents(); i++) {
			ChoreoEvent? ev = scene.GetEvent(i);
			if (ev == null)
				continue;

			switch (ev.GetType()) {
				default:
					break;
				case EventType.SubScene: {
						if (!scene.IsSubScene()) {
							ChoreoScene? subscene = ev.GetSubScene();
							if (subscene != null)
								ClearSchedules(subscene);
						}
					}
					break;
			}
		}
	}

	public virtual bool InterruptThisScene(SceneEntity otherScene) {
		Assert(otherScene != null);

		if (!IsInterruptable())
			return false;

		if (Interrupted)
			return false;

		Interrupted = true;
		InterruptScene.Set(otherScene);

		otherScene!.RequestCompletionNotification(this);

		PausePlayback();
		return true;
	}

	public virtual void CheckInterruptCompletion() {
		if (!Interrupted)
			return;

		if (InterruptScene.Get() != null && !InterruptSceneFinished)
			return;

		Interrupted = false;
		InterruptScene.Set(null);

		ResumePlayback();
	}

	public virtual void ClearInterrupt() {
		InterruptCount = 0;
		Interrupted = false;
		InterruptScene.Set(null);
	}

	public void RequestCompletionNotification(SceneEntity notify) {
		Handle<SceneEntity> h = new();
		h.Set(notify);
		if (!NotifySceneCompletion.Contains(h))
			NotifySceneCompletion.Add(h);
	}

	public virtual void NotifyOfCompletion(SceneEntity interruptor) {
		Assert(Interrupted);
		Assert(InterruptScene.Get() == interruptor);
		InterruptSceneFinished = true;

		CheckInterruptCompletion();
	}

	public void AddListManager(SceneListManager manager) {
		Handle<SceneListManager> h = new();
		h.Set(manager);
		if (!ListManagers.Contains(h))
			ListManagers.Add(h);
	}

	public void ClearActivatorTargets() {
		if (stricmp(Target1, "!activator") == 0) {
			ActorList.Clear();
			NetworkProp().NetworkStateForceUpdate();
			HTarget1.Set(null);
		}
		if (stricmp(Target2, "!activator") == 0) {
			ActorList.Clear();
			NetworkProp().NetworkStateForceUpdate();
			HTarget2.Set(null);
		}
		if (stricmp(Target3, "!activator") == 0) {
			ActorList.Clear();
			NetworkProp().NetworkStateForceUpdate();
			HTarget3.Set(null);
		}
		if (stricmp(Target4, "!activator") == 0) {
			ActorList.Clear();
			NetworkProp().NetworkStateForceUpdate();
			HTarget4.Set(null);
		}
		if (stricmp(Target5, "!activator") == 0) {
			ActorList.Clear();
			NetworkProp().NetworkStateForceUpdate();
			HTarget5.Set(null);
		}
		if (stricmp(Target6, "!activator") == 0) {
			ActorList.Clear();
			NetworkProp().NetworkStateForceUpdate();
			HTarget6.Set(null);
		}
		if (stricmp(Target7, "!activator") == 0) {
			ActorList.Clear();
			NetworkProp().NetworkStateForceUpdate();
			HTarget7.Set(null);
		}
		if (stricmp(Target8, "!activator") == 0) {
			ActorList.Clear();
			NetworkProp().NetworkStateForceUpdate();
			HTarget8.Set(null);
		}
	}

	public virtual void OnSceneFinished(bool canceled, bool fireoutput) {
		if (Scene == null)
			return;

		Scene_Printf($"{SceneFile} : {CurrentTime,8:F2}:  finished\n");

		int c = NotifySceneCompletion.Count;
		for (int i = 0; i < c; i++) {
			SceneEntity? ent = NotifySceneCompletion[i].Get();
			if (ent == null)
				continue;

			ent.NotifyOfCompletion(this);
		}
		NotifySceneCompletion.Clear();

		Scene.ResetSimulation();
		IsPlayingBack = false;
		Paused = false;
		SetCurrentTime(0.0, false);

		ClearInterrupt();

		if (fireoutput && !CompletedEarly)
			OnCompletion.FireOutput(this, this, 0);

		ClearSceneEvents(Scene, canceled);

		for (int i = 0; i < Scene.GetNumActors(); i++) {
			BaseFlex? testActor = FindNamedActor(i);

			if (testActor == null)
				continue;

			testActor.RemoveChoreoScene(Scene, canceled);

			if (InterruptedActorsScenes)
				QueueActorsScriptedScenesToResume(testActor, false);
		}
	}

	public override EdictFlags UpdateTransmitState() {
		if (!ShouldNetwork())
			return SetTransmitState(EdictFlags.DontSend);

		if (RecipientFilter != null)
			return SetTransmitState(EdictFlags.FullCheck);

		return SetTransmitState(EdictFlags.Always);
	}

	public override EdictFlags ShouldTransmit(CheckTransmitInfo info) {
		EdictFlags result = base.ShouldTransmit(info);

		if (RecipientFilter != null && result != EdictFlags.DontSend) {
			bool found = false;

			for (int i = 0; i < RecipientFilter.GetRecipientCount(); i++) {
				int recipient = RecipientFilter.GetRecipientIndex(i);

				BasePlayer? player = (BasePlayer?)Instance(recipient);

				if (player != null && player.Edict() == info.ClientEnt) {
					found = true;
					break;
				}
			}

			if (!found)
				result = EdictFlags.DontSend;
		}

		return result;
	}

	public void SetRecipientFilter(IRecipientFilter? filter) {
		if (filter != null) {
			RecipientFilter = new RecipientFilter();
			RecipientFilter.CopyFrom((RecipientFilter)filter);
		}
	}
}
