global using static Game.Server.BaseFlexGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Filesystem;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.Mathematics;

using System.Numerics;

namespace Game.Server;

using EventType = Game.Shared.ChoreoEvent.EventType;

[LinkEntityToClass("funCBaseFlex")]
[NetworkName("CBaseFlex")]
public partial class BaseFlex : BaseAnimatingOverlay
{
	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		new(nameof(FlexWeight)) { Bits = 12, Flags = PropFlags.RoundDown, Low = 0.0f, High = 1.0f },
		new(nameof(BlinkToggle)) { Bits = 1, Flags = PropFlags.Unsigned },
		new(nameof(ViewTarget)) { Bits = -1, Flags = PropFlags.Coord },

		new(nameof(ViewOffset)) { Encoding = NetEncoding.VectorElements, Bits = 0, Flags = PropFlags.NoScale },

		new(nameof(Lean)) { Bits = -1, Flags = PropFlags.Coord },
		new(nameof(Shift)) { Bits = -1, Flags = PropFlags.Coord },
	];

	[NetworkVar("m_flexWeight")]
	public partial ref readonly InlineArray96<float> FlexWeight { get; }
	[NetworkVar("m_blinktoggle")]
	public partial int BlinkToggle { get; set; }
	[NetworkVar("m_viewtarget")]
	public partial Vector3 ViewTarget { get; set; }
	[NetworkVar("m_vecLean")]
	public partial Vector3 Lean { get; set; }
	[NetworkVar("m_vecShift")]
	public partial Vector3 Shift { get; set; }

	public override void SetModel(ReadOnlySpan<char> modelName) {
		base.SetModel(modelName);

		for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++)
			SetFlexWeight(i, 0.0f);
	}

	public void SetFlexWeight(LocalFlexController index, float value) {
		if (index >= 0 && index < GetNumFlexControllers()) {
			StudioHdr? studioHdr = GetModelPtr();
			if (studioHdr == null)
				return;

			MStudioFlexController flexcontroller = studioHdr.FlexController(index);

			if (flexcontroller.Max != flexcontroller.Min) {
				value = (value - flexcontroller.Min) / (flexcontroller.Max - flexcontroller.Min);
				value = Math.Clamp(value, 0.0f, 1.0f);
			}

			FlexWeightForModify()[(int)index] = value;
		}
	}

	public float GetFlexWeight(LocalFlexController index) {
		if (index >= 0 && index < GetNumFlexControllers()) {
			StudioHdr? studioHdr = GetModelPtr();
			if (studioHdr == null)
				return 0;

			MStudioFlexController flexcontroller = studioHdr.FlexController(index);

			if (flexcontroller.Max != flexcontroller.Min)
				return FlexWeight[(int)index] * (flexcontroller.Max - flexcontroller.Min) + flexcontroller.Min;

			return FlexWeight[(int)index];
		}
		return 0.0f;
	}

	public LocalFlexController FindFlexController(ReadOnlySpan<char> name) {
		for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++) {
			if (stricmp(GetFlexControllerName(i), name) == 0)
				return i;
		}

		return 0;
	}

	const int REQUEST_DEFERRED_LAYER_ALLOCATION = -2;

	readonly List<SceneEventInfo> SceneEvents = [];
	readonly Dictionary<FlexSettingHdr, LocalFlexController[]> LocalToGlobal = new();
	TimeUnit_t AllowResponsesEndTime;
	readonly List<ChoreoScene> ActiveChoreoScenes = [];
	bool UpdateLayerPriorities;
	TimeUnit_t LastFlexAnimationTime;

	public BaseFlex() {
		UpdateLayerPriorities = true;
		LastFlexAnimationTime = 0.0;
	}

	public void Blink() => BlinkToggle = BlinkToggle == 0 ? 1 : 0;

	public virtual void SetViewtarget(in Vector3 viewtarget) => ViewTarget = viewtarget;
	public Vector3 GetViewtarget() => ViewTarget;

	public Vector3 PrevOrigin;
	public Vector3 PrevVelocity;

	public override void Teleport(Vector3? newPosition, QAngle? newAngles, Vector3? newVelocity) {
		base.Teleport(newPosition, newAngles, newVelocity);
		PrevOrigin = vec3_origin;
	}

	public void DoBodyLean() {
		AI_BaseNPC? myNpc = MyNPCPointer();

		if (myNpc != null) {
			Vector3 delta;
			Vector3 pos;
			Vector3 origin = GetAbsOrigin();

			if (PrevOrigin == vec3_origin)
				PrevOrigin = origin;

			delta = origin - PrevOrigin;
			delta.X = Math.Clamp(delta.X, -50, 50);
			delta.Y = Math.Clamp(delta.Y, -50, 50);
			delta.Z = Math.Clamp(delta.Z, -50, 50);

			TimeUnit_t dt = gpGlobals.CurTime - GetLastThink(default);
			bool skip = ((GetFlags() & (EntityFlags.Fly | EntityFlags.Swim)) != 0) || (GetMoveParent() != null) || (GetGroundEntity() == null) || (GetGroundEntity()!.IsMoving());
			skip |= myNpc.ScheduleState.TaskRanAutomovement || (myNpc.GetVehicleEntity() != null);

			if (!skip) {
				if (delta.LengthSquared() > PrevVelocity.LengthSquared()) {
					float decay = MathLib.ExponentialDecay(0.6f, 0.1f, (float)dt);
					PrevVelocity = PrevVelocity * decay + delta * (1.0f - decay);
				}
				else {
					float decay = MathLib.ExponentialDecay(0.4f, 0.1f, (float)dt);
					PrevVelocity = PrevVelocity * decay + delta * (1.0f - decay);
				}

				pos = PrevOrigin + PrevVelocity;

				float decay2 = MathLib.ExponentialDecay(0.5f, 0.1f, (float)dt);
				Shift = Shift * decay2 + (origin - pos) * (1.0f - decay2);
				Lean = (origin - pos) * 1.0f;
			}
			else {
				PrevVelocity = delta;
				float decay = MathLib.ExponentialDecay(0.5f, 0.1f, (float)dt);
				Shift = Lean * decay;
				Lean = Shift * decay;
			}

			PrevOrigin = origin;
		}
	}

	public void StartChoreoScene(ChoreoScene scene) {
		if (ActiveChoreoScenes.Contains(scene))
			return;

		ActiveChoreoScenes.Add(scene);
		UpdateLayerPriorities = true;
	}

	public void RemoveChoreoScene(ChoreoScene scene, bool canceled = false) {
		ActiveChoreoScenes.Remove(scene);
		UpdateLayerPriorities = true;

		if (canceled) {
			AI_BaseNPC? myNpc = MyNPCPointer();
			myNpc?.ClearSceneLock();
		}
	}

	public int GetScenePriority(ChoreoScene? scene) {
		int priority = 0;
		int c = ActiveChoreoScenes.Count;
		for (int i = 0; i < c; i++) {
			ChoreoScene? activeScene = ActiveChoreoScenes[i];
			if (activeScene == null)
				continue;

			if (activeScene == scene)
				break;

			priority += activeScene.GetNumChannels();
		}
		return priority;
	}

	public void ClearSceneEvents(ChoreoScene? scene, bool canceled) {
		if (scene == null) {
			SceneEvents.Clear();
			return;
		}

		for (int i = SceneEvents.Count - 1; i >= 0; i--) {
			SceneEventInfo info = SceneEvents[i];

			Assert(info != null);
			Assert(info!.Scene != null);
			Assert(info.Event != null);

			if (info.Scene != scene)
				continue;

			if (!ClearSceneEvent(info, false, canceled))
				Assert(false);

			info.Event = null;
			info.Scene = null;
			info.Started = false;

			SceneEvents.RemoveAt(i);
		}
	}

	public virtual bool ClearSceneEvent(SceneEventInfo info, bool fastKill, bool canceled) {
		Assert(info != null);
		Assert(info!.Scene != null);
		Assert(info.Event != null);

		switch (info.Event!.GetType()) {
			case EventType.Gesture:
			case EventType.Sequence: {
					if (info.Layer >= 0) {
						if (fastKill)
							FastRemoveLayer(info.Layer);
						else if (info.Event.GetType() == EventType.Gesture) {
							if (canceled)
								RemoveLayer(info.Layer, 0.5f);
							else
								RemoveLayer(info.Layer, 0.1f);
						}
						else
							RemoveLayer(info.Layer, 0.3f);
					}
				}
				return true;

			case EventType.MoveTo: {
					AI_BaseNPC? myNpc = MyNPCPointer();
					if (myNpc == null)
						return true;
				}
				return true;
			case EventType.Face:
			case EventType.FlexAnimation:
			case EventType.Expression:
			case EventType.LookAt:
			case EventType.Generic:
				return true;
			case EventType.Speak: {
					if (canceled) {
						StopSound(info.Event.GetParameters());

#if HL2_EPISODIC
						if (this is AI_BaseActor baseActor)
							baseActor.GetExpresser()!.ForceNotSpeaking();
#endif
					}
				}
				return true;
		}
		return false;
	}

	public void AddSceneEvent(ChoreoScene? scene, ChoreoEvent? ev, BaseEntity? target = null) {
		if (scene == null || ev == null) {
			Msg("CBaseFlex::AddSceneEvent:  scene or event was NULL!!!\n");
			return;
		}

		ChoreoActor? actor = ev.GetActor();
		if (actor == null) {
			Msg("CBaseFlex::AddSceneEvent:  event.GetActor() was NULL!!!\n");
			return;
		}

		SceneEventInfo info = new() { Layer = 0 };

		info.Event = ev;
		info.Scene = scene;
		info.Target.Set(target);
		info.Started = false;

		if (StartSceneEvent(info, scene, ev, actor, target))
			SceneEvents.Add(info);
		else
			Scene_Printf("CBaseFlex::AddSceneEvent:  event failed\n");
	}

	bool RequestStartSequenceSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev, ChoreoActor actor, BaseEntity? target) {
		info.Sequence = LookupSequence(ev.GetParameters());

		if (info.Sequence < 0) {
			Warning($"CSceneEntity {GetEntityName()} :\"{actor.GetName()}\" unable to find sequence \"{ev.GetParameters()}\"\n");
			return false;
		}

		info.Layer = REQUEST_DEFERRED_LAYER_ALLOCATION;
		info.Actor = actor;
		return true;
	}

	bool RequestStartGestureSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev, ChoreoActor actor, BaseEntity? target) {
		info.Sequence = LookupSequence(ev.GetParameters());

		if (info.Sequence < 0) {
			Warning($"CSceneEntity {GetEntityName()} :\"{actor.GetName()}\" unable to find gesture \"{ev.GetParameters()}\"\n");
			return false;
		}

		info.Layer = REQUEST_DEFERRED_LAYER_ALLOCATION;
		info.Actor = actor;
		return true;
	}

	bool HandleStartSequenceSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev, ChoreoActor actor) {
		Assert(info.Layer == REQUEST_DEFERRED_LAYER_ALLOCATION);

		info.Sequence = LookupSequence(ev.GetParameters());
		info.Layer = -1;

		if (info.Sequence < 0) {
			Warning($"CSceneEntity {GetEntityName()} :\"{actor.GetName()}\" unable to find sequence \"{ev.GetParameters()}\"\n");
			return false;
		}

		if (!EnterSceneSequence(scene, ev)) {
			if (!ev.GetPlayOverScript()) {
				Warning($"CSceneEntity {GetEntityName()} :\"{actor.GetName()}\" failed to start sequence \"{ev.GetParameters()}\"\n");
				return false;
			}
		}

		info.Priority = actor.FindChannelIndex(ev.GetChannel()!);
		info.Layer = AddLayeredSequence(info.Sequence, info.Priority + GetScenePriority(scene));
		SetLayerNoRestore(info.Layer, true);
		SetLayerWeight(info.Layer, 0.0f);

		bool looping = (Animation.GetSequenceFlags(GetModelPtr(), info.Sequence) & StudioAnimSeqFlags.Looping) != 0;
		if (!looping) {
			TimeUnit_t dt = scene.GetTime() - ev.GetStartTime();
			TimeUnit_t seqDuration = SequenceDuration(info.Sequence);
			float cycle = (float)(dt / seqDuration);
			cycle = cycle - (int)cycle;
			SetLayerCycle(info.Layer, cycle, cycle);

			SetLayerPlaybackRate(info.Layer, 0.0);
		}
		else
			SetLayerPlaybackRate(info.Layer, 1.0);

		if (IsMoving())
			info.Weight = 0.0f;
		else
			info.Weight = 1.0f;

		return true;
	}

	bool HandleStartGestureSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev, ChoreoActor actor) {
		Assert(info.Layer == REQUEST_DEFERRED_LAYER_ALLOCATION);

		info.Sequence = LookupSequence(ev.GetParameters());
		info.Layer = -1;

		if (info.Sequence < 0) {
			Warning($"CSceneEntity {GetEntityName()} :\"{actor.GetName()}\" unable to find gesture \"{ev.GetParameters()}\"\n");
			return false;
		}

		info.IsGesture = false;
		KeyValues? seqKeyValues = GetSequenceKeyValues(info.Sequence);
		if (seqKeyValues != null) {
			KeyValues? kvAllFaceposer = seqKeyValues.FindKey("faceposer");
			if (kvAllFaceposer != null) {
				KeyValues? kvType = kvAllFaceposer.FindKey("type");

				if (kvType != null)
					info.IsGesture = stricmp(kvType.GetString(), "gesture") == 0;
			}

			string startLoop = "loop";
			string endLoop = "end";

			KeyValues? kvFaceposer;
			for (kvFaceposer = kvAllFaceposer!.GetFirstSubKey(); kvFaceposer != null; kvFaceposer = kvFaceposer.GetNextKey()) {
				if (stricmp(kvFaceposer.Name, "startloop") == 0)
					startLoop = new(kvFaceposer.GetString());
				else if (stricmp(kvFaceposer.Name, "endloop") == 0)
					endLoop = new(kvFaceposer.GetString());
			}

			EventAbsoluteTag? tag;
			tag = ev.FindAbsoluteTag(ChoreoEvent.AbsTagType.Original, startLoop);
			tag?.SetLinear(true);
			tag = ev.FindAbsoluteTag(ChoreoEvent.AbsTagType.Playback, startLoop);
			tag?.SetLinear(true);
			tag = ev.FindAbsoluteTag(ChoreoEvent.AbsTagType.Original, endLoop);
			tag?.SetLinear(true);
			tag = ev.FindAbsoluteTag(ChoreoEvent.AbsTagType.Playback, endLoop);
			tag?.SetLinear(true);

			if (kvAllFaceposer != null) {
				StudioHdr studioHdr = GetModelPtr()!;

				MStudioSeqDesc seqdesc = studioHdr.Seqdesc(info.Sequence);
				MStudioAnimDesc animdesc = studioHdr.Animdesc(studioHdr.iRelativeAnim(info.Sequence, seqdesc.Anim(0, 0)));

				for (kvFaceposer = kvAllFaceposer.GetFirstSubKey(); kvFaceposer != null; kvFaceposer = kvFaceposer.GetNextKey()) {
					if (stricmp(kvFaceposer.Name, "tags") == 0) {
						KeyValues? kvTags;
						for (kvTags = kvFaceposer.GetFirstSubKey(); kvTags != null; kvTags = kvTags.GetNextKey()) {
							int maxFrame = animdesc.NumFrames - 2;

							if (maxFrame > 0) {
								float percentage = (float)kvTags.GetInt() / maxFrame;

								EventAbsoluteTag? origTag = ev.FindAbsoluteTag(ChoreoEvent.AbsTagType.Original, kvTags.Name);
								if (origTag != null) {
									if (MathF.Abs(origTag.GetPercentage() - percentage) > 0.05) {
										DevWarning($"{scene.GetFilename()} repositioned tag: {kvTags.Name} : {origTag.GetPercentage():F3} . {percentage:F3} ({scene.GetFilename()}:{actor.GetName()}:{ev.GetParameters()})\n");
										origTag.SetPercentage(percentage);
									}
								}
							}
						}
					}
				}

				if (!ev.VerifyTagOrder())
					DevWarning($"out of order tags : {scene.GetFilename()} : ({actor.GetName()}:{ev.GetName()}:{ev.GetParameters()})\n");
			}
		}

		if (!info.IsGesture && IsMoving())
			info.Weight = 0.0f;
		else
			info.Weight = 1.0f;

		info.Priority = actor.FindChannelIndex(ev.GetChannel()!);
		info.Layer = AddLayeredSequence(info.Sequence, info.Priority + GetScenePriority(scene));
		SetLayerNoRestore(info.Layer, true);
		SetLayerDuration(info.Layer, ev.GetDuration());
		SetLayerWeight(info.Layer, 0.0f);

		bool looping = (Animation.GetSequenceFlags(GetModelPtr(), info.Sequence) & StudioAnimSeqFlags.Looping) != 0;
		if (looping)
			DevMsg(1, $"vcd error, gesture {ev.GetParameters()} of model {GetModelName()} is marked as STUDIO_LOOPING!\n");

		SetLayerLooping(info.Layer, false);

		float duration = ev.GetDuration();

		float eventCycle = (float)((scene.GetTime() - ev.GetStartTime()) / duration);
		float layerCycle = ev.GetOriginalPercentageFromPlaybackPercentage(eventCycle);
		SetLayerCycle(info.Layer, layerCycle, 0.0f);
		SetLayerPlaybackRate(info.Layer, 0.0);

		return true;
	}

	bool StartFacingSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev, ChoreoActor actor, BaseEntity? target) {
		if (target != null) {
			AI_BaseNPC? myNpc = MyNPCPointer();
			if (myNpc != null && myNpc.IsInAVehicle())
				return false;

			info.IsMoving = false;
			return true;
		}
		return false;
	}

	bool StartMoveToSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev, ChoreoActor actor, BaseEntity? target) {
		if (target != null) {
			info.IsMoving = false;
			info.HasArrived = false;
			AI_BaseNPC? myNpc = MyNPCPointer();
			if (myNpc == null)
				return false;

			EnterSceneSequence(scene, ev, true);

			return true;
		}

		return false;
	}

	public virtual bool StartSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev, ChoreoActor actor, BaseEntity? target) {
		switch (ev.GetType()) {
			case EventType.Sequence:
				return RequestStartSequenceSceneEvent(info, scene, ev, actor, target);

			case EventType.Gesture:
				return RequestStartGestureSceneEvent(info, scene, ev, actor, target);

			case EventType.Face:
				return StartFacingSceneEvent(info, scene, ev, actor, target);

			case EventType.MoveTo:
				return StartMoveToSceneEvent(info, scene, ev, actor, target);

			case EventType.LookAt:
				info.Target.Set(target);
				return true;

			case EventType.FlexAnimation:
				info.InitWeight(this);
				return true;

			case EventType.Speak:
				return true;

			case EventType.Expression:
				return true;
		}

		return false;
	}

	public void RemoveSceneEvent(ChoreoScene? scene, ChoreoEvent? ev, bool fastKill) {
		Assert(ev != null);

		for (int i = 0; i < SceneEvents.Count; i++) {
			SceneEventInfo info = SceneEvents[i];

			Assert(info != null);
			Assert(info!.Event != null);

			if (info.Scene != scene)
				continue;

			if (info.Event != ev)
				continue;

			if (ClearSceneEvent(info, fastKill, false)) {
				info.Event = null;
				info.Scene = null;
				info.Started = false;

				SceneEvents.RemoveAt(i);
				return;
			}
		}
	}

	public bool CheckSceneEvent(TimeUnit_t currenttime, ChoreoScene? scene, ChoreoEvent? ev) {
		for (int i = 0; i < SceneEvents.Count; i++) {
			SceneEventInfo info = SceneEvents[i];

			Assert(info != null);
			Assert(info!.Event != null);

			if (info.Scene != scene)
				continue;

			if (info.Event != ev)
				continue;

			return CheckSceneEventCompletion(info, currenttime, scene!, ev!);
		}
		return true;
	}

	public virtual bool CheckSceneEventCompletion(SceneEventInfo info, TimeUnit_t currenttime, ChoreoScene scene, ChoreoEvent ev) {
		switch (ev.GetType()) {
			case EventType.MoveTo: {
					AI_BaseNPC? npc = MyNPCPointer();

					if (npc != null) {
						if (npc.GetNavigator()!.IsGoalActive()) {
							Task_t? curTask = npc.GetTask();
							if (curTask != null && (curTask.Value.Task == TASK_PLAY_SCENE || curTask.Value.Task == TASK_WAIT_FOR_MOVEMENT)) {
								TimeUnit_t preload = ev.GetEndTime() - currenttime;
								if (preload < 0)
									return false;

								float t = npc.GetTimeToNavGoal();

								if (t > 0.0f && t <= preload)
									return true;

								return false;
							}
						}
						else if (info.HasArrived)
							return true;
						else if (info.Started && !npc.IsCurSchedule(SCHED_SCENE_GENERIC)) {
							Warning($"{scene.GetFilename()} : {scene.GetTime(),8:F2}: waiting for actor {ev.GetActor()!.GetName()} to complete MOVETO but actor not in SCHED_SCENE_GENERIC\n");
							return true;
						}
						return false;
					}
				}
				break;
			default:
				break;
		}
		return true;
	}

	public virtual void ProcessSceneEvents() {
		for (LocalFlexController i = 0; i < GetNumFlexControllers(); i++)
			SetFlexWeight(i, GetFlexWeight(i) * 0.95f);

		bool hasForegroundEvents = false;

		for (int i = 0; i < SceneEvents.Count; i++) {
			SceneEventInfo info = SceneEvents[i];
			Assert(info != null);

			ChoreoEvent? ev = info!.Event;
			Assert(ev != null);

			ChoreoScene? scene = info.Scene;
			Assert(scene != null);

			if (scene != null && !scene.IsBackground())
				hasForegroundEvents = true;

			if (ProcessSceneEvent(info, scene!, ev!))
				info.Started = true;
		}

		if (hasForegroundEvents && scene_showunlock.GetBool()) {
			AI_BaseNPC? myNpc = MyNPCPointer();
			if (myNpc != null && !(myNpc.GetState() == NPCState.Script || myNpc.IsCurSchedule(SCHED_SCENE_GENERIC))) {
				Vector3 p0 = myNpc.GetHullMins();
				Vector3 p1 = myNpc.GetHullMaxs();
				p0.Z = p1.Z + 2;
				p1.Z = p1.Z + 2;
				DebugOverlay.Box(myNpc.GetAbsOrigin(), p0, p1, 255, 0, 0, 0, 0.12f);
			}
		}

		UpdateLayerPriorities = false;
	}

	public void EnsureTranslations(FlexSettingHdr settinghdr) {
		Assert(settinghdr != null);

		if (LocalToGlobal.ContainsKey(settinghdr!))
			return;

		Assert(settinghdr!.NumKeys > 0);
		LocalFlexController[] mapping = new LocalFlexController[settinghdr.NumKeys];

		for (int i = 0; i < settinghdr.NumKeys; ++i)
			mapping[i] = FindFlexController(settinghdr.LocalName(i));

		LocalToGlobal.Add(settinghdr, mapping);
	}

	protected LocalFlexController FlexControllerLocalToGlobal(FlexSettingHdr settinghdr, int key) {
		if (!LocalToGlobal.TryGetValue(settinghdr, out LocalFlexController[]? mapping)) {
			Assert(false);
			Warning($"Unable to find mapping for flexcontroller {key}, settings {settinghdr.GetHashCode():X8} on {EntIndex()}/{GetClassname()}\n");
			EnsureTranslations(settinghdr);
			if (!LocalToGlobal.TryGetValue(settinghdr, out mapping))
				Error("CBaseFlex::FlexControllerLocalToGlobal failed!\n");
		}

		Assert(mapping!.Length != 0 && key < mapping.Length);
		LocalFlexController index = mapping[key];
		return index;
	}

	protected FlexSettingHdr? FindSceneFile(ReadOnlySpan<char> filename) => FlexSceneFileManager.g_FlexSceneFileManager.FindSceneFile(this, filename, false);

	bool ProcessFlexAnimationSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		if (ev.HasEndTime()) {
			AI_BaseNPC? myNpc = MyNPCPointer();
			if (myNpc != null) {
				if (!myNpc.HasCondition((int)SCOND_t.COND_IN_PVS))
					return true;

				if (ai_expression_optimization.GetBool()) {
					if (scene.IsBackground()) {
						if (gpGlobals.FrameTime > ai_expression_frametime.GetFloat()) {
							info.HasArrived = true;
							info.Next = gpGlobals.CurTime + RandomFloat(0.7f, 1.2f);
						}
						else if (info.Next <= gpGlobals.CurTime) {
							BasePlayer? player = Util.GetLocalPlayer();

							info.HasArrived = player != null && !player.FInViewCone(this);
							info.Next = gpGlobals.CurTime + RandomFloat(0.7f, 1.2f);
						}

						if (info.HasArrived)
							return true;
					}
				}
			}

			AddFlexAnimation(info);
		}
		return true;
	}

	bool ProcessFlexSettingSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		if (!ev.HasEndTime())
			return true;

		string scenefile = ev.GetParameters();
		string name = ev.GetParameters2();

		if (scenefile != null && name != null) {
			FlexSettingHdr? expHdr = FindSceneFile(scenefile);
			if (expHdr != null) {
				TimeUnit_t scenetime = scene.GetTime();

				float scale = ev.GetIntensity(scenetime);

				AddFlexSetting(name, scale, expHdr, !info.Started);
			}
		}

		return true;
	}

	bool ProcessFacingSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		if (info.Target.Get() == null)
			return false;

		if (!EnterSceneSequence(scene, ev, true))
			return false;

		if (!info.Started)
			info.InitialYaw = GetLocalAngles().Y;

		if (info.Target.Get() == this)
			return true;

		AI_BaseNPC? myNpc = MyNPCPointer();
		if (myNpc != null) {
			if (info.IsMoving != IsMoving())
				info.InitialYaw = GetLocalAngles().Y;
			info.IsMoving = IsMoving();

			float intensity = ev.GetIntensity(scene.GetTime());
			if (info.IsMoving)
				myNpc.AddFacingTarget(info.Target.Get(), intensity, 0.2f);
			else {
				float goalYaw = myNpc.CalcIdealYaw(info.Target.Get()!.EyePosition());

				float diff = Util.AngleDiff(goalYaw, info.InitialYaw);

				float idealYaw = Util.AngleMod(info.InitialYaw + diff * intensity);

				myNpc.GetMotor()!.SetIdealYawAndUpdate(idealYaw);
			}

			return true;
		}
		return false;
	}

	static Activity DetermineExpressionMoveActivity(ChoreoEvent ev, AI_BaseNPC npc) {
		Activity activity = Activity.ACT_WALK;
		string param2 = ev.GetParameters2();
		if (string.IsNullOrEmpty(param2))
			return activity;

		int space = param2.IndexOf(' ');
		string actName = space >= 0 ? param2[..space] : param2;

		if (stricmp(actName, "Walk") == 0)
			activity = Activity.ACT_WALK;
		else if (stricmp(actName, "Run") == 0)
			activity = Activity.ACT_RUN;
		else if (stricmp(actName, "CrouchWalk") == 0)
			activity = Activity.ACT_WALK_CROUCH;
		else {
			activity = (Activity)ActivityList.IndexForName(actName);
			if (activity == Activity.ACT_INVALID) {
				npc.SceneCustomMoveSeq = actName;
				activity = Activity.ACT_SCRIPT_CUSTOM_MOVE;
			}
		}

		return activity;
	}

	bool ProcessMoveToSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		if (info.Target.Get() == null)
			return false;

		AI_BaseNPC? myNpc = MyNPCPointer();
		if (myNpc == null)
			return false;

		if (!EnterSceneSequence(scene, ev, true))
			return false;

		if (info.Target.Get() == this)
			return true;

		if (myNpc.IsInAVehicle()) {
			myNpc.ExitVehicle();
			return false;
		}

		Task_t? curTask = myNpc.GetTask();
		if (!info.IsMoving && (!IsMoving() || curTask!.Value.Task == TASK_STOP_MOVING)) {
			if (curTask != null && (curTask.Value.Task == TASK_PLAY_SCENE || curTask.Value.Task == TASK_WAIT_FOR_MOVEMENT || curTask.Value.Task == TASK_STOP_MOVING)) {
				Activity moveActivity = DetermineExpressionMoveActivity(ev, myNpc);
				myNpc.SetTarget(info.Target.Get());

				float distTolerance;
				distTolerance = myNpc.GetHullWidth() / 2.0f;

				if (ev.GetForceShortMovement())
					distTolerance = 0.1f;

				AI_NavGoal goal = new AI_NavGoal(NavGoalType.TargetEnt, moveActivity, distTolerance, AI_NavGoalFlags.UpdateTargetPos);
				float dist = (info.Target.Get()!.EyePosition() - GetAbsOrigin()).Length2D();

				if (dist > Math.Max(Math.Max(distTolerance, 0.1f), ev.GetDistanceToTarget())) {
					bool result = false;

					if (!myNpc.IsUnreachable(info.Target.Get()!)) {
						result = myNpc.GetNavigator()!.SetGoal(in goal, AI_NavSetGoalFlags.ClearTarget);
						if (!result)
							myNpc.RememberUnreachable(info.Target.Get()!, 1.5);
					}

					if (result) {
						myNpc.GetNavigator()!.SetMovementActivity(moveActivity);
						myNpc.GetNavigator()!.SetArrivalDistance(ev.GetDistanceToTarget());
						info.IsMoving = true;
					}
					else {
						if (developer.GetInt() > 0 && scene_showmoveto.GetBool()) {
							myNpc.GetMoveProbe()!.FloorPoint(info.Target.Get()!.EyePosition(), Mask.NPCSolid, 0, -64, out Vector3 testPoint);
							DebugOverlay.HorzArrow(GetAbsOrigin() + new Vector3(0, 0, 1), testPoint + new Vector3(0, 0, 1), 4, 255, 0, 255, 0, false, 0.12f);
							DebugOverlay.Box(testPoint, myNpc.GetHullMins(), myNpc.GetHullMaxs(), 255, 0, 255, 0, 0.12f);
						}
					}
				}
				else
					info.HasArrived = true;
			}
		}
		else if (IsMoving()) {
			float dist = (info.Target.Get()!.EyePosition() - GetAbsOrigin()).Length2D();

			if (dist <= ev.GetDistanceToTarget())
				info.HasArrived = true;
		}
		else
			info.IsMoving = false;

		return true;
	}

	bool ProcessLookAtSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		AI_BaseNPC? myNpc = MyNPCPointer();
		if (myNpc != null && info.Target.Get() != null) {
			float intensity = ev.GetIntensity(scene.GetTime());

			TimeUnit_t duration = scene.GetTime() - ev.GetStartTime();
			float maxIntensity = duration < 0.3 ? MathLib.SimpleSpline((float)(duration / 0.3)) : 1.0f;
			intensity = Math.Clamp(intensity, 0.0f, maxIntensity);

			myNpc.AddLookTarget(info.Target.Get(), intensity, 0.1f);
			if (developer.GetInt() > 0 && scene_showlook.GetBool() && info.Target.Get() != null) {
				Vector3 tmp = info.Target.Get()!.EyePosition() - myNpc.EyePosition();
				MathLib.VectorNormalize(ref tmp);
				Vector3 p0 = myNpc.EyePosition();
				DebugOverlay.VertArrow(p0, p0 + tmp * (4 + 16 * intensity), 4, 255, 255, 255, 0, true, 0.12f);
			}
		}
		return true;
	}

	public virtual bool ProcessSceneEvent(SceneEventInfo info, ChoreoScene scene, ChoreoEvent ev) {
		switch (ev.GetType()) {
			case EventType.FlexAnimation:
				return ProcessFlexAnimationSceneEvent(info, scene, ev);

			case EventType.Expression:
				return ProcessFlexSettingSceneEvent(info, scene, ev);

			case EventType.Sequence:
				return ProcessSequenceSceneEvent(info, scene, ev);

			case EventType.Gesture:
				return ProcessGestureSceneEvent(info, scene, ev);

			case EventType.Face:
				return ProcessFacingSceneEvent(info, scene, ev);

			case EventType.MoveTo:
				return ProcessMoveToSceneEvent(info, scene, ev);

			case EventType.LookAt:
				return ProcessLookAtSceneEvent(info, scene, ev);

			case EventType.Speak:
				return true;

			default: {
					Msg($"unknown type {(int)ev.GetType()} in ProcessSceneEvent()\n");
					Assert(false);
				}
				break;
		}

		return false;
	}

	protected bool IsRunningSceneMoveToEvent() {
		for (int i = SceneEvents.Count - 1; i >= 0; i--) {
			SceneEventInfo info = SceneEvents[i];
			ChoreoEvent? ev = info.Event;
			if (ev != null && ev.GetType() == EventType.MoveTo)
				return true;
		}

		return false;
	}

	protected FlexSetting? FindNamedSetting(FlexSettingHdr settinghdr, ReadOnlySpan<char> expr) {
		int i;
		FlexSetting? setting = null;

		for (i = 0; i < settinghdr.NumFlexSettings; i++) {
			setting = settinghdr.Setting(i);
			if (setting == null)
				continue;

			string name = setting.Name();

			if (stricmp(name, expr) == 0)
				break;
		}

		if (i >= settinghdr.NumFlexSettings)
			return null;

		return setting;
	}

	protected void AddFlexAnimation(SceneEventInfo? info) {
		if (info == null)
			return;

		AI_BaseNPC? myNpc = MyNPCPointer();
		if (myNpc != null && !myNpc.HasCondition((int)SCOND_t.COND_IN_PVS))
			return;

		ChoreoEvent? ev = info.Event;
		if (ev == null)
			return;

		ChoreoScene? scene = info.Scene;
		if (scene == null)
			return;

		if (!ev.GetTrackLookupSet()) {
			for (int i = 0; i < ev.GetNumFlexAnimationTracks(); i++) {
				FlexAnimationTrack? track = ev.GetFlexAnimationTrack(i);
				if (track == null)
					continue;

				if (track.IsComboType()) {
					string name = "right_" + track.GetFlexControllerName();

					track.SetFlexControllerIndex(FindFlexController(name), 0, 0);

					if (AI_BaseActor.IsServerSideFlexController(name)) {
						Assert(false);
						track.SetServerSide(true);
					}

					name = "left_" + track.GetFlexControllerName();

					track.SetFlexControllerIndex(FindFlexController(name), 0, 1);

					if (AI_BaseActor.IsServerSideFlexController(name)) {
						Assert(false);
						track.SetServerSide(true);
					}
				}
				else {
					track.SetFlexControllerIndex(FindFlexController(track.GetFlexControllerName()), 0);

					track.SetServerSide(AI_BaseActor.IsServerSideFlexController(track.GetFlexControllerName()));
				}
			}

			ev.SetTrackLookupSet(true);
		}

		TimeUnit_t scenetime = scene.GetTime();
		float weight = ev.GetIntensity(scenetime) * info.UpdateWeight(this);

		for (int i = 0; i < ev.GetNumFlexAnimationTracks(); i++) {
			FlexAnimationTrack? track = ev.GetFlexAnimationTrack(i);
			if (track == null)
				continue;

			if (!track.IsTrackActive())
				continue;

			if (g_bClientFlex && !track.IsServerSide())
				continue;

			if (track.IsComboType()) {
				for (int side = 0; side < 2; side++) {
					LocalFlexController controller = track.GetRawFlexControllerIndex(side);

					float intensity = track.GetIntensity((float)scenetime, side);
					if (controller >= 0) {
						float orig = GetFlexWeight(controller);
						SetFlexWeight(controller, orig * (1 - weight) + intensity * weight);
					}
				}
			}
			else {
				LocalFlexController controller = track.GetRawFlexControllerIndex(0);

				float intensity = track.GetIntensity((float)scenetime, 0);
				if (controller >= 0) {
					float orig = GetFlexWeight(controller);
					SetFlexWeight(controller, orig * (1 - weight) + intensity * weight);
				}
			}
		}

		info.Started = true;
	}

	protected void AddFlexSetting(ReadOnlySpan<char> expr, float scale, FlexSettingHdr settinghdr, bool newexpression) {
		int i;
		FlexSetting? setting = null;

		for (i = 0; i < settinghdr.NumFlexSettings; i++) {
			setting = settinghdr.Setting(i);
			if (setting == null)
				continue;

			string name = setting.Name();

			if (stricmp(name, expr) == 0)
				break;
		}

		if (i >= settinghdr.NumFlexSettings)
			return;

		int truecount = setting!.PSetting(0, out ReadOnlySpan<FlexSettingWeight> weights);

		for (i = 0; i < truecount; i++) {
			ref readonly FlexSettingWeight w = ref weights[i];

			LocalFlexController index = FlexControllerLocalToGlobal(settinghdr, w.Key);

			float s = Math.Clamp(scale * w.Influence, 0.0f, 1.0f);
			float value = GetFlexWeight(index) * (1.0f - s) + w.Weight * s;
			SetFlexWeight(index, value);
		}
	}

	bool ProcessGestureSceneEvent(SceneEventInfo? info, ChoreoScene? scene, ChoreoEvent? ev) {
		if (info == null || ev == null || scene == null)
			return false;

		if (info.Layer == REQUEST_DEFERRED_LAYER_ALLOCATION)
			HandleStartGestureSceneEvent(info, scene, ev, info.Actor!);

		if (info.Layer >= 0) {
			float duration = ev.GetDuration();
			float eventCycle = (float)((scene.GetTime() - ev.GetStartTime()) / duration);
			float cycle = ev.GetOriginalPercentageFromPlaybackPercentage(eventCycle);

			SetLayerCycle(info.Layer, cycle);

			float weight = ev.GetIntensity(scene.GetTime());

			if (!info.IsGesture) {
				if (IsMoving())
					info.Weight = (float)Math.Max(info.Weight - 0.2, 0.0);
				else
					info.Weight = (float)Math.Min(info.Weight + 0.2, 1.0);
			}

			float spline = 3 * info.Weight * info.Weight - 2 * info.Weight * info.Weight * info.Weight;
			SetLayerWeight(info.Layer, weight * spline);

			if (UpdateLayerPriorities)
				SetLayerPriority(info.Layer, info.Priority + GetScenePriority(scene));
		}

		return true;
	}

	bool ProcessSequenceSceneEvent(SceneEventInfo? info, ChoreoScene? scene, ChoreoEvent? ev) {
		if (info == null || ev == null || scene == null)
			return false;

		bool newlyAllocated = false;
		if (info.Layer == REQUEST_DEFERRED_LAYER_ALLOCATION) {
			bool result = HandleStartSequenceSceneEvent(info, scene, ev, info.Actor!);
			if (!result)
				return false;
			newlyAllocated = true;
		}

		if (info.Layer >= 0) {
			float weight = ev.GetIntensity(scene.GetTime());

			if (newlyAllocated)
				weight = 0.0f;

			AI_BaseNPC? myNpc = MyNPCPointer();

			bool fadeOut = IsMoving();
			if (myNpc != null && !(myNpc.IsCurSchedule(SCHED_SCENE_GENERIC) || myNpc.GetActivity() == Activity.ACT_IDLE_ANGRY || myNpc.GetActivity() == Activity.ACT_IDLE)) {
				fadeOut = true;
				if (info.Weight == 1.0f)
					Warning($"{GetEntityName()} playing CChoreoEvent::SEQUENCE but AI has forced them to do something different\n");
			}

			if (fadeOut)
				info.Weight = (float)Math.Max(info.Weight - 0.2, 0.0);
			else
				info.Weight = (float)Math.Min(info.Weight + 0.2, 1.0);

			float spline = 3 * info.Weight * info.Weight - 2 * info.Weight * info.Weight * info.Weight;
			SetLayerWeight(info.Layer, weight * spline);

			bool looping = (Animation.GetSequenceFlags(GetModelPtr(), info.Sequence) & StudioAnimSeqFlags.Looping) != 0;
			if (!looping) {
				TimeUnit_t dt = scene.GetTime() - ev.GetStartTime();
				TimeUnit_t seqDuration = SequenceDuration(info.Sequence);
				float cycle = (float)(dt / seqDuration);
				cycle = Math.Clamp(cycle, 0.0f, 1.0f);
				SetLayerCycle(info.Layer, cycle);
			}

			myNpc?.AddSceneLock(0.2);

			if (UpdateLayerPriorities)
				SetLayerPriority(info.Layer, info.Priority + GetScenePriority(scene));
		}

		return true;
	}

	public bool EnterSceneSequence(ChoreoScene scene, ChoreoEvent ev, bool restart = false) {
		AI_BaseNPC? myNpc = MyNPCPointer();

		if (myNpc == null) {
			if (IsPlayer())
				return true;

			return false;
		}

		TimeUnit_t duration = Math.Min(2.0, Math.Min(ev.GetEndTime() - scene.GetTime() + 2.0, scene.FindStopTime() - scene.GetTime() + 0.2));

		if (myNpc.IsCurSchedule(SCHED_SCENE_GENERIC)) {
			myNpc.AddSceneLock(duration);
			return true;
		}

		if (myNpc.GetCurSchedule() != null) {
			myNpc.GetCurSchedule()!.GetInterruptMask(out AI_ScheduleBits testBits);

			testBits.Clear((int)SCOND_t.COND_PROVOKED);

			if (testBits.IsAllClear())
				return false;
		}

		if (myNpc.IsInterruptable()) {
			if (myNpc.Cine.Get() != null)
				myNpc.ExitScriptedSequence();

			myNpc.OnStartScene();
			myNpc.SetSchedule(SCHED_SCENE_GENERIC);
			myNpc.AddSceneLock(duration);
			return true;
		}

		return false;
	}

	public bool IsSuppressedFlexAnimation(SceneEventInfo info) {
		if (info.Scene != null && info.Scene.IsBackground())
			return LastFlexAnimationTime > gpGlobals.CurTime - GetAnimTimeInterval() * 1.5;

		LastFlexAnimationTime = gpGlobals.CurTime;
		return false;
	}

	public bool PermitResponse(float responseLength) {
		if (AllowResponsesEndTime <= 0.0f)
			return false;

		if (gpGlobals.CurTime + responseLength <= AllowResponsesEndTime)
			return true;

		return false;
	}

	public void SetPermitResponse(TimeUnit_t endtime) => AllowResponsesEndTime = endtime;

	public bool HasSceneEvents() => SceneEvents.Count != 0;

	public virtual int GetSpecialDSP() => 0;
}

public class FlexSceneFileManager(ReadOnlySpan<char> name) : AutoGameSystem(name)
{
	public static readonly FlexSceneFileManager g_FlexSceneFileManager = new("CFlexSceneFileManager");

	class FlexSceneFile
	{
		public const int MAX_FLEX_FILENAME = 128;

		public string Filename = "";
		public FlexSettingHdr? Buffer;
	}

	readonly List<FlexSceneFile> FileList = [];

	public override bool Init() {
		FindSceneFile(null, "phonemes", true);
		FindSceneFile(null, "phonemes_weak", true);
		FindSceneFile(null, "phonemes_strong", true);
#if HL2_DLL
		FindSceneFile(null, "random", true);
		FindSceneFile(null, "randomAlert", true);
#endif
		return true;
	}

	public override void Shutdown() => DeleteSceneFiles();

	static void EnsureTranslations(BaseFlex? instance, FlexSettingHdr settinghdr) {
		instance?.EnsureTranslations(settinghdr);
	}

	public FlexSettingHdr? FindSceneFile(BaseFlex? instance, ReadOnlySpan<char> filename, bool allowBlockingIO) {
		int i;
		for (i = 0; i < FileList.Count; i++) {
			FlexSceneFile? file = FileList[i];
			if (file != null && stricmp(file.Filename, filename) == 0) {
				EnsureTranslations(instance, file.Buffer!);
				return file.Buffer;
			}
		}

		if (!allowBlockingIO)
			return null;

		byte[] buffer;
		using (IFileHandle? handle = filesystem.Open($"expressions/{filename}.vfe", FileOpenOptions.Read | FileOpenOptions.Binary, "GAME")) {
			if (handle == null)
				return null;

			buffer = new byte[handle.Stream.Length];
			handle.Stream.ReadExactly(buffer);
		}

		if (buffer.Length == 0)
			return null;

		FlexSceneFile pfile = new();
		pfile.Filename = new(filename[..Math.Min(filename.Length, FlexSceneFile.MAX_FLEX_FILENAME - 1)]);
		pfile.Buffer = new FlexSettingHdr(buffer);
		FileList.Add(pfile);

		EnsureTranslations(instance, pfile.Buffer);

		return pfile.Buffer;
	}

	void DeleteSceneFiles() => FileList.Clear();
}

public static class BaseFlexGlobals
{
	public static readonly ConVar flex_expression = new("flex_expression", "-");
	public static readonly ConVar flex_talk = new("flex_talk", "0");

	public static readonly ConVar scene_showlook = new("scene_showlook", "0", FCvar.Archive, "When playing back, show the directions of look events.");
	public static readonly ConVar scene_showmoveto = new("scene_showmoveto", "0", FCvar.Archive, "When moving, show the end location.");
	public static readonly ConVar scene_showunlock = new("scene_showunlock", "0", FCvar.Archive, "Show when a vcd is playing but normal AI is running.");

	public static readonly ConVar ai_expression_optimization = new("ai_expression_optimization", "0", FCvar.None, "Disable npc background expressions when you can't see them.");
	public static readonly ConVar ai_expression_frametime = new("ai_expression_frametime", "0.05", FCvar.None, "Maximum frametime to still play background expressions.");

	public static bool g_bClientFlex = true;

	public static readonly string?[] predef_flexcontroller_names = [
		"right_lid_raiser",
		"left_lid_raiser",
		"right_lid_tightener",
		"left_lid_tightener",
		"right_lid_droop",
		"left_lid_droop",
		"right_inner_raiser",
		"left_inner_raiser",
		"right_outer_raiser",
		"left_outer_raiser",
		"right_lowerer",
		"left_lowerer",
		"right_cheek_raiser",
		"left_cheek_raiser",
		"wrinkler",
		"right_upper_raiser",
		"left_upper_raiser",
		"right_corner_puller",
		"left_corner_puller",
		"corner_depressor",
		"chin_raiser",
		"right_puckerer",
		"left_puckerer",
		"right_funneler",
		"left_funneler",
		"tightener",
		"jaw_clencher",
		"jaw_drop",
		"right_mouth_drop",
		"left_mouth_drop",
		null
	];

	public static readonly float[,] predef_flexcontroller_values = {
		{ 0.700f, 0.560f, 0.650f, 0.650f, 0.650f, 0.585f, 0.000f, 0.000f, 0.400f, 0.040f, 0.000f, 0.000f, 0.450f, 0.450f, 0.000f, 0.000f, 0.000f, 0.750f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.150f, 1.000f, 0.000f, 0.000f, 0.000f },
		{ 0.450f, 0.450f, 0.450f, 0.450f, 0.000f, 0.000f, 0.000f, 0.000f, 0.300f, 0.300f, 0.000f, 0.000f, 0.250f, 0.250f, 0.000f, 0.000f, 0.000f, 0.750f, 0.750f, 0.000f, 0.000f, 0.000f, 0.000f, 0.400f, 0.400f, 0.000f, 1.000f, 0.000f, 0.050f, 0.050f },
		{ 0.200f, 0.200f, 0.500f, 0.500f, 0.150f, 0.150f, 0.100f, 0.100f, 0.150f, 0.150f, 0.000f, 0.000f, 0.700f, 0.700f, 0.000f, 0.000f, 0.000f, 0.750f, 0.750f, 0.000f, 0.200f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.850f, 0.000f, 0.000f, 0.000f },
		{ 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.300f, 0.300f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.100f, 0.000f, 0.000f, 0.000f, 0.000f, 0.700f, 0.300f, 0.000f, 0.000f, 0.200f, 0.200f, 0.000f, 0.000f, 0.300f, 0.000f, 0.000f },
		{ 0.450f, 0.450f, 0.000f, 0.000f, 0.450f, 0.450f, 0.000f, 0.000f, 0.000f, 0.000f, 0.450f, 0.450f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.300f, 0.000f, 0.000f, 0.000f, 0.000f },
		{ 0.000f, 0.000f, 0.350f, 0.350f, 0.150f, 0.150f, 0.300f, 0.300f, 0.450f, 0.450f, 0.000f, 0.000f, 0.200f, 0.200f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.200f, 0.200f, 0.000f, 0.000f, 0.300f, 0.000f, 0.000f, 0.000f, 0.000f },
		{ 0.000f, 0.000f, 0.650f, 0.650f, 0.750f, 0.750f, 0.000f, 0.000f, 0.000f, 0.000f, 0.300f, 0.300f, 0.000f, 0.000f, 0.000f, 0.250f, 0.250f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f, 0.000f }
	};
}
