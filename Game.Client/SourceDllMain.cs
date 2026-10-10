using Game.Client.HUD;
using Game.Shared;

using Microsoft.Extensions.DependencyInjection;

using Source;
using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.GameUI;
using Source.Common.GUI;
using Source.Common.MaterialSystem;
using Source.Common.SceneFileCache;
using Source.Common.SoundEmitterSystem;
using Source.Engine;

namespace Game.Client;

public struct DataChangedEvent
{
	public DataChangedEvent() { }
	public DataChangedEvent(IClientNetworkable? entity, DataUpdateType updateType, ReusableBox<ulong> storedEvent) {
		Entity = entity;
		UpdateType = updateType;
		StoredEvent = storedEvent;
	}

	public IClientNetworkable? Entity;
	public DataUpdateType UpdateType;
	public ReusableBox<ulong>? StoredEvent;
}

public static class SourceDllMain
{

	static readonly Dictionary<ulong, DataChangedEvent> g_DataChangedEvents = [];
	static ulong datachangedevent = 0;
	public static ref DataChangedEvent g_GetDataChangedEvent(ulong idx) => ref g_DataChangedEvents.TryGetRef(idx, out _);
	public static ulong g_AddDataChangedEvent(in DataChangedEvent data) {
		var t = Interlocked.Increment(ref datachangedevent);
		lock (g_DataChangedEvents) {
			g_DataChangedEvents.Add(t, data);
		}
		return t;
	}

	public static void Link(IServiceCollection services) {
		services.AddSingleton<IPredictableList>(g_Predictables);
	}

	[Dependency] public static ClientGlobalVariables gpGlobals { get; private set; } = null!;
	[Dependency] public static IViewRender view { get; private set; } = null!;
	[Dependency] public static IRenderView render { get; private set; } = null!;
	[Dependency] public static IEngineClient engine { get; private set; } = null!;
	[Dependency] public static IMatSystemSurface surface { get; private set; } = null!;
	[Dependency] public static IEngineVGui enginevgui { get; private set; } = null!;
	[Dependency] public static ISpatialPartition partition { get; private set; } = null!;
	[Dependency] public static ISoundEmitterSystemBase soundemitterbase { get; private set; } = null!;
	[Dependency] public static Hud gHUD { get; private set; } = null!;
	[Dependency<IPrediction>] public static Prediction prediction { get; private set; } = null!;
	[Dependency] public static ICvar cvar { get; private set; } = null!;
	[Dependency] public static ClientEntityList cl_entitylist { get; private set; } = null!;
	[Dependency] public static UserMessages usermessages { get; private set; } = null!;
	[Dependency] public static IVModelInfoClient modelinfo { get; private set; } = null!;
	[Dependency] public static IGameEventManager2 gameeventmanager { get; private set; } = null!;
	[Dependency] public static IDataCache datacache { get; private set; } = null!;
	[Dependency] public static IGameUI gameUI { get; private set; } = null!;
	[Dependency] public static IMDLCache mdlcache { get; private set; } = null!;
	[Dependency] public static ICenterPrint centerprint { get; private set; } = null!;
	[Dependency] public static IMaterialSystem materials { get; private set; } = null!;
	[Dependency] public static IInput input { get; private set; } = null!;
	[Dependency] public static IModelRender modelrender { get; private set; } = null!;
	[Dependency] public static IVEfx effects { get; private set; } = null!;
	[Dependency] public static IFileSystem filesystem { get; private set; } = null!;
	[Dependency] public static ISchemeManager vguiSchemeManager { get; private set; } = null!;
	[Dependency] public static IVDebugOverlay debugoverlay { get; private set; } = null!;
	[Dependency] public static IClientLeafSystem clientLeafSystem { get; private set; } = null!;
	[Dependency] public static IUniformRandomStream random { get; private set; } = null!;
	public static readonly GaussianRandomStream randomgaussian = new();
	[Dependency] public static IPredictableList predictables { get; private set; } = null!;
	[Dependency] public static ILocalize localize { get; private set; } = null!;
	[Dependency] public static IViewEffects vieweffects { get; private set; } = null!;
	[Dependency] public static ICommandLine commandLine { get; private set; } = null!;
	[Dependency] public static ISurface Surface { get; private set; } = null!;
	[Dependency] public static ISchemeManager SchemeManager { get; private set; } = null!;
	[Dependency] public static IVGui VGui { get; private set; } = null!;
	[Dependency] public static ILocalize Localize { get; private set; } = null!;
	[Dependency] public static IVGuiInput vguiInput { get; private set; } = null!;
	[Dependency] public static ISceneFileCache scenefilecache { get; private set; } = null!;
#if GMOD_DLL
	[Dependency] public static Source.Common.GarrysMod.IGet get { get; private set; } = null!;
#endif

	[KeyedDependency(Key = Realm.Client)] public static INetworkStringTableContainer networkstringtable { get; private set; } = null!;
	[KeyedDependency(Key = Realm.Client)] public static IEngineTrace enginetrace { get; private set; } = null!;
	[KeyedDependency(Key = Realm.Client)] public static IEngineSound enginesound { get; private set; } = null!;


	public static TimeUnit_t TICK_INTERVAL => gpGlobals.IntervalPerTick;

	public static TimeUnit_t TICKS_TO_TIME(int t) => TICK_INTERVAL * t;
	public static int TIME_TO_TICKS(TimeUnit_t dt) => (int)(0.5f + dt / TICK_INTERVAL);

	public static bool IsEngineThreaded() => false;

	// TODO stubs
	public static bool IsInCommentaryMode() => false;
}
