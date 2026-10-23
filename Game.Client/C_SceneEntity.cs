using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Utilities;

namespace Game.Client;

using EventType = Game.Shared.ChoreoEvent.EventType;

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

[NetworkName("CSceneEntity")]
public partial class C_SceneEntity : C_BaseEntity, IChoreoEventCallback
{
	public const int MAX_ACTORS_IN_SCENE = 16;

	static readonly ChoreoStringPool g_ChoreoStringPool = new();

	[NetworkVar("m_nSceneStringIndex")]
	public int SceneStringIndex;
	[NetworkVar("m_bIsPlayingBack")]
	public bool IsPlayingBack;
	[NetworkVar("m_bPaused")]
	public bool Paused;
	[NetworkVar("m_bMultiplayer")]
	public bool Multiplayer;
	[NetworkVar("m_flForceClientTime")]
	public float ForceClientTime;
	[NetworkVar("m_hActorList")]
	readonly List<EHANDLE> ActorList = [];

	[NetworkTable]
	public static readonly new NetworkProp[] Props = [
		nameof(SceneStringIndex),
		nameof(IsPlayingBack),
		nameof(Paused),
		nameof(Multiplayer),
		new(nameof(ForceClientTime)) { Proxy = typeof(ForceClientTimeProxy) },
		new(nameof(ActorList)) { Encoding = NetEncoding.List, Count = MAX_ACTORS_IN_SCENE, Proxy = typeof(ActorListProxy) },
	];

	public static class ActorListProxy
	{
		public static void Resize(object instance, object list, int len) {
			var vec = (List<EHANDLE>)list;
			while (vec.Count < len) vec.Add(new());
			while (vec.Count > len) vec.RemoveAt(vec.Count - 1);
		}
	}

	public static class ForceClientTimeProxy
	{
		public static void Recv(ref readonly RecvProxyData data, object instance, IFieldAccessor field) {
			C_SceneEntity scene = (C_SceneEntity)instance;
			field.SetValue(instance, data.Value.Float);
			scene.OnResetClientTime();
		}
	}

	struct QueuedEvents
	{
		public TimeUnit_t StartTime;
		public ChoreoScene Scene;
		public ChoreoEvent Event;
	}

	TimeUnit_t CurrentTime;
	bool WasPlaying;
	ChoreoScene? Scene;
	readonly List<QueuedEvents> QueuedEventsList = [];

	public override void Term() {
		UnloadScene();
		base.Term();
	}

	public void OnResetClientTime() => CurrentTime = ForceClientTime;

	ReadOnlySpan<char> GetSceneFileName() => HLClient.g_pStringTableClientSideChoreoScenes!.GetString(SceneStringIndex);

	public override void PostDataUpdate(DataUpdateType updateType) {
		base.PostDataUpdate(updateType);

		ReadOnlySpan<char> filename = GetSceneFileName();

		if (updateType == DataUpdateType.Created) {
			Assert(!filename.IsEmpty);
			if (!filename.IsEmpty) {
				LoadSceneFromFile(filename);

				Assert(Scene != null);

				if (Multiplayer) {
					Scene?.RemoveEventsExceptTypes([EventType.FlexAnimation, EventType.Expression, EventType.Gesture, EventType.Sequence, EventType.Speak, EventType.Loop]);
				}
				else {
					Scene?.RemoveEventsExceptTypes([EventType.FlexAnimation, EventType.Expression]);
				}

				SetNextClientThink(CLIENT_THINK_ALWAYS);
			}

			WasPlaying = !IsPlayingBack;
		}

		if (WasPlaying != IsPlayingBack) {
			for (int i = 0; i < ActorList.Count; ++i) {
				if (ActorList[i].Get() is not C_BaseFlex actor)
					continue;

				Assert(Scene != null);

				if (Scene != null) {
					ClearSceneEvents(Scene, false);

					if (IsPlayingBack) {
						Scene.ResetSimulation();
						actor.StartChoreoScene(Scene);
					}
					else {
						Scene.ResetSimulation();
						actor.RemoveChoreoScene(Scene);
					}
				}
			}
		}
	}

	public override void PreDataUpdate(DataUpdateType updateType) {
		base.PreDataUpdate(updateType);

		WasPlaying = IsPlayingBack;
	}

	public void ProcessEvent(TimeUnit_t currenttime, ChoreoScene scene, ChoreoEvent ev) {
		if (currenttime < ev.PrevTime) {
			C_BaseFlex? actor = null;
			ChoreoActor? choreoActor = ev.GetActor();
			if (choreoActor != null) {
				actor = FindNamedActor(choreoActor);
				if (actor == null)
					return;
			}

			switch (ev.GetType()) {
				case EventType.Gesture:
					Assert(Multiplayer);
					Assert(scene != null);
					Assert(ev != null);

					if (actor != null)
						DispatchProcessGesture(scene!, actor, ev!);
					break;
				case EventType.Sequence:
					Assert(Multiplayer);
					Assert(scene != null);
					Assert(ev != null);

					if (actor != null)
						DispatchProcessSequence(scene!, actor, ev!);
					break;
			}
		}

		ev.PrevTime = (float)currenttime;
	}

	public bool CheckEvent(TimeUnit_t currenttime, ChoreoScene scene, ChoreoEvent ev) => true;

	C_BaseFlex? FindNamedActor(ChoreoActor? choreoActor) {
		if (Scene == null || choreoActor == null)
			return null;

		int idx = Scene.FindActorIndex(choreoActor);
		if (idx < 0 || idx >= ActorList.Count)
			return null;

		return ActorList[idx].Get() as C_BaseFlex;
	}

	public void StartEvent(TimeUnit_t currenttime, ChoreoScene scene, ChoreoEvent ev) {
		Assert(ev != null);

		if (stricmp(ev!.GetName(), "NULL") == 0) {
			Scene_Printf($"{GetSceneFileName()} : {currenttime,8:F2}:  ignored {ev.GetDescription()}\n");
			return;
		}

		C_BaseFlex? actor = null;
		ChoreoActor? choreoActor = ev.GetActor();
		if (choreoActor != null) {
			actor = FindNamedActor(choreoActor);
			if (actor == null) {
				QueueStartEvent(currenttime, scene, ev);
				return;
			}
		}

		Scene_Printf($"{GetSceneFileName()} : {currenttime,8:F2}:  start {ev.GetDescription()}\n");

		switch (ev.GetType()) {
			case EventType.FlexAnimation:
				if (actor != null)
					DispatchStartFlexAnimation(scene, actor, ev);
				break;
			case EventType.Expression:
				if (actor != null)
					DispatchStartExpression(scene, actor, ev);
				break;
			case EventType.Gesture:
				Assert(Multiplayer);
				Assert(scene != null);

				if (actor != null)
					DispatchStartGesture(scene!, actor, ev);
				break;
			case EventType.Sequence:
				Assert(Multiplayer);
				Assert(scene != null);

				if (actor != null)
					DispatchStartSequence(scene!, actor, ev);
				break;
			case EventType.Loop:
				Assert(Multiplayer);
				Assert(scene != null);

				DispatchProcessLoop(scene!, ev);
				break;
			default:
				break;
		}

		ev.PrevTime = (float)currenttime;
	}

	void DispatchProcessLoop(ChoreoScene scene, ChoreoEvent ev) {
		Assert(ev.GetType() == EventType.Loop);

		TimeUnit_t backtime = atof(ev.GetParameters());

		bool process = true;
		int counter = ev.GetLoopCount();
		if (counter != -1) {
			int remaining = ev.GetNumLoopsRemaining();
			if (remaining <= 0)
				process = false;
			else
				ev.SetNumLoopsRemaining(--remaining);
		}

		if (!process)
			return;

		scene.LoopToTime(backtime);
		SetCurrentTime(backtime, true);
	}

	public void EndEvent(TimeUnit_t currenttime, ChoreoScene scene, ChoreoEvent ev) {
		Assert(ev != null);

		if (stricmp(ev!.GetName(), "NULL") == 0)
			return;

		C_BaseFlex? actor = null;
		ChoreoActor? choreoActor = ev.GetActor();
		if (choreoActor != null)
			actor = FindNamedActor(choreoActor);

		Scene_Printf($"{GetSceneFileName()} : {currenttime,8:F2}:  finish {ev.GetDescription()}\n");

		switch (ev.GetType()) {
			case EventType.FlexAnimation:
				if (actor != null)
					DispatchEndFlexAnimation(scene, actor, ev);
				break;
			case EventType.Expression:
				if (actor != null)
					DispatchEndExpression(scene, actor, ev);
				break;
			case EventType.Gesture:
				if (actor != null)
					DispatchEndGesture(scene, actor, ev);
				break;
			case EventType.Sequence:
				if (actor != null)
					DispatchEndSequence(scene, actor, ev);
				break;
			default:
				break;
		}
	}

	ChoreoScene? LoadScene(ReadOnlySpan<char> filename) {
		Span<char> loadfile = stackalloc char[512];
		strcpy(loadfile, filename);
		StrTools.SetExtension(loadfile, ".vcd");
		StrTools.FixSlashes(loadfile);
		ReadOnlySpan<char> loadfileName = ((ReadOnlySpan<char>)loadfile).SliceNullTerminatedString();

		nuint bufsize = scenefilecache.GetSceneBufferSize(loadfileName);
		if (bufsize <= 0)
			return null;

		byte[] buffer = new byte[bufsize];
		if (!scenefilecache.GetSceneData(filename, buffer))
			return null;

		ChoreoScene? scene;
		if (ChoreoSceneGlobals.IsBufferBinaryVCD(buffer)) {
			scene = new ChoreoScene(this);
			UtlBuffer buf = new(buffer, UtlBuffer.BufferFlags.ReadOnly);
			if (!scene.RestoreFromBinaryBuffer(buf, loadfileName, g_ChoreoStringPool)) {
				Warning($"Unable to restore binary scene '{loadfileName}'\n");
				scene = null;
			}
			else {
				scene.SetPrintFunc(Scene_Printf);
				scene.SetEventCallbackInterface(this);
			}
		}
		else {
			g_TokenProcessor.SetBuffer(buffer);
			scene = ChoreoSceneGlobals.ChoreoLoadScene(loadfileName, this, g_TokenProcessor, Scene_Printf);
		}

		return scene;
	}

	void LoadSceneFromFile(ReadOnlySpan<char> filename) {
		UnloadScene();
		Scene = LoadScene(filename);
	}

	void ClearSceneEvents(ChoreoScene scene, bool canceled) {
		if (Scene == null)
			return;

		Scene_Printf($"{GetSceneFileName()} : {CurrentTime,8:F2}:  clearing events\n");

		for (int i = 0; i < Scene.GetNumActors(); i++) {
			C_BaseFlex? actor = FindNamedActor(Scene.GetActor(i));
			if (actor == null)
				continue;

			actor.ClearSceneEvents(scene, canceled);
		}

		WipeQueuedEvents();
	}

	void UnloadScene() {
		WipeQueuedEvents();

		if (Scene != null) {
			ClearSceneEvents(Scene, false);
			for (int i = 0; i < Scene.GetNumActors(); i++) {
				C_BaseFlex? testActor = FindNamedActor(Scene.GetActor(i));

				if (testActor == null)
					continue;

				testActor.RemoveChoreoScene(Scene);
			}
		}
		Scene = null;
	}

	void DispatchStartFlexAnimation(ChoreoScene scene, C_BaseFlex actor, ChoreoEvent ev) => actor.AddSceneEvent(scene, ev, null, false);

	void DispatchEndFlexAnimation(ChoreoScene scene, C_BaseFlex actor, ChoreoEvent ev) => actor.RemoveSceneEvent(scene, ev, false);

	void DispatchStartExpression(ChoreoScene scene, C_BaseFlex actor, ChoreoEvent ev) => actor.AddSceneEvent(scene, ev, null, false);

	void DispatchEndExpression(ChoreoScene scene, C_BaseFlex actor, ChoreoEvent ev) => actor.RemoveSceneEvent(scene, ev, false);

	void DispatchStartGesture(ChoreoScene scene, C_BaseFlex actor, ChoreoEvent ev) {
		if (stricmp(ev.GetName(), "NULL") == 0)
			return;

		actor.AddSceneEvent(scene, ev, null, false);
	}

	void DispatchProcessGesture(ChoreoScene scene, C_BaseFlex actor, ChoreoEvent ev) {
		if (stricmp(ev.GetName(), "NULL") == 0)
			return;

		actor.RemoveSceneEvent(scene, ev, false);
		actor.AddSceneEvent(scene, ev, null, false);
	}

	void DispatchEndGesture(ChoreoScene scene, C_BaseFlex actor, ChoreoEvent ev) {
		if (stricmp(ev.GetName(), "NULL") == 0)
			return;

		actor.RemoveSceneEvent(scene, ev, false);
	}

	void DispatchStartSequence(ChoreoScene scene, C_BaseFlex actor, ChoreoEvent ev) => actor.AddSceneEvent(scene, ev, null, false);

	void DispatchProcessSequence(ChoreoScene scene, C_BaseFlex actor, ChoreoEvent ev) {
		actor.RemoveSceneEvent(scene, ev, false);
		actor.AddSceneEvent(scene, ev, null, false);
	}

	void DispatchEndSequence(ChoreoScene scene, C_BaseFlex actor, ChoreoEvent ev) => actor.RemoveSceneEvent(scene, ev, false);

	void DoThink(TimeUnit_t frametime) {
		if (Scene == null)
			return;

		if (!IsPlayingBack) {
			WipeQueuedEvents();
			return;
		}

		CheckQueuedEvents();

		if (Paused)
			return;

		Scene.Think(CurrentTime);
		CurrentTime += gpGlobals.FrameTime;
	}

	public override void ClientThink() => DoThink(gpGlobals.FrameTime);

	void CheckQueuedEvents() {
		QueuedEvents[] events = [.. QueuedEventsList];
		QueuedEventsList.Clear();

		for (int i = 0; i < events.Length; ++i) {
			ref readonly QueuedEvents check = ref events[i];
			StartEvent(check.StartTime, check.Scene, check.Event);
		}
	}

	void WipeQueuedEvents() => QueuedEventsList.Clear();

	void QueueStartEvent(TimeUnit_t starttime, ChoreoScene scene, ChoreoEvent ev) {
		int c = QueuedEventsList.Count;
		for (int i = 0; i < c; ++i) {
			QueuedEvents check = QueuedEventsList[i];
			if (check.Scene == scene && check.Event == ev)
				return;
		}

		QueuedEventsList.Add(new QueuedEvents { Scene = scene, Event = ev, StartTime = starttime });
	}

	void SetCurrentTime(TimeUnit_t t, bool forceClientSync) {
		CurrentTime = t;
		ForceClientTime = (float)t;
	}
}
