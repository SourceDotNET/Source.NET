global using static Source.Engine.SourceDllMain;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Physics;
using Source.Common.Server;
using Source.Engine.Client;
using Source.Engine.Server;

using System.Runtime.CompilerServices;

namespace Source.Engine;

// Going to be switching to this style of singleton access shortly. It's starting to get a little unmaintainable, given how many things have to cross wires,
// to constantly be polling for dependencies
public static class SourceDllMain
{
	[Dependency] public static IEngineServer engine { get; private set; } = null!;
	[Dependency] public static IGameEventManager2 gameEventManager { get; private set; } = null!;
	[Dependency(Required = false)] public static IEngineVGui __EngineVGui { get; private set; } = null!;
	[Dependency] public static IRender g_EngineRenderer { get; private set; } = null!;
	public static GameEventManager g_GameEventManager => (GameEventManager)gameEventManager;
	public static IEngineVGuiInternal EngineVGui() => (IEngineVGuiInternal)__EngineVGui;
	[Dependency] public static ClientState cl { get; private set; } = null!;
	[Dependency] public static GameServer sv { get; private set; } = null!;
	[Dependency(Required = false)] public static IBaseClientDLL? g_ClientDLL { get; private set; } = null!;
	[Dependency] public static IServerGameDLL serverGameDLL { get; private set; } = null!;
	[Dependency] public static EngineRecvTable engineRecvTable { get; private set; } = null!;
	[Dependency] public static IServerGameClients serverGameClients { get; private set; } = null!;
	[Dependency] public static IServerGameEnts serverGameEnts { get; private set; } = null!;
	[Dependency(Required = false)] public static ILocalize g_Localize { get; private set; } = null!;
	[Dependency] public static IMDLCache mdlcache { get; private set; } = null!;
	[Dependency] public static ModelLoader modelLoader { get; private set; } = null!;
	[Dependency] public static IVModelInfo modelInfo { get; private set; } = null!;
#if !SWDS
	[Dependency] public static IModelRender modelrender { get; private set; } = null!;
	[Dependency] public static IStudioRender studiorender { get; private set; } = null!;
	[Dependency(Required = false)] public static IClientLeafSystemEngine clientleafsystem { get; private set; } = null!;
	[Dependency(Required = false)] public static IClientRenderTargets? g_pClientRenderTargets { get; private set; } = null!;
#endif
	[Dependency] public static IPhysicsCollision physcollision { get; private set; } = null!;
	[Dependency] public static IPhysicsSurfaceProps physprop { get; private set; } = null!;
	[Dependency] public static ServerPlugin serverPluginHandler { get; private set; } = null!;
	[Dependency] public static IModelLoader modelloader { get; private set; } = null!;
	[Dependency] public static IMaterialSystem materials { get; private set; } = null!;
	[Dependency] public static IFileSystem g_pFileSystem { get; private set; } = null!;
	[Dependency] public static RenderUtils renderUtils { get; private set; } = null!;
	[Dependency] public static MatSysInterface MatSys { get; private set; } = null!;
	[Dependency] public static IVEfx effects { get; private set; } = null!;
	[Dependency] public static CommonHostState host_state { get; private set; } = null!;
	[Dependency] public static Render R { get; private set; } = null!;
	[Dependency] public static EngineToolImpl g_EngineTool { get; private set; } = null!;
	[Dependency] public static ISoundServices g_SoundServices { get; private set; } = null!;
	[Dependency(Required = false)] public static IPrediction g_ClientSidePrediction { get; private set; } = null!;
	[Dependency] public static ClientGlobalVariables clientGlobalVariables { get; private set; } = null!;
	[Dependency] public static ServerGlobalVariables serverGlobalVariables { get; private set; } = null!;
	public static IMaterialSystem materialSystem { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => materials; }
	[Dependency(Required = false)] public static IDebugTextureInfo materialSystemDebugTextureInfo { get; private set; } = null!;
	[Dependency(Required = false)] public static IDedicatedExports dedicated { get; private set; } = null!;
	[Dependency] public static IRegistry registry { get; private set; } = null!;
	[Dependency] public static Cbuf cbuf { get; private set; } = null!;
	[Dependency] public static ICvar cvar { get; private set; } = null!;
	[Dependency] public static Cmd cmd { get; private set; } = null!;
	[Dependency] public static Log g_Log { get; private set; } = null!;
	[Dependency] public static IMaterialSystemHardwareConfig HardwareConfig { get; private set; } = null!;
	[Dependency] public static ICommandLine commandLine { get; private set; } = null!;
	[KeyedDependency(Key = Realm.Server)] public static NetworkStringTableContainer networkStringTableContainerServer { get; private set; } = null!;
	[KeyedDependency(Key = Realm.Server)] public static IEngineTrace g_pEngineTraceServer { get; private set; } = null!;
	[Dependency] public static IVDebugOverlay debugoverlay { get; private set; } = null!;
#if GMOD_DLL
	[Dependency] public static Source.Common.GarrysMod.IGet get { get; private set; } = null!;
#endif


#if !SWDS
	[Dependency] public static IAudioSystem g_AudioSystem { get; private set; } = null!;
	[Dependency] public static ILauncherManager launcherMgr { get; private set; } = null!;
	[Dependency] public static IClientEntityList entitylist { get; private set; } = null!;
	[KeyedDependency(Key = Realm.Client)] public static NetworkStringTableContainer networkStringTableContainerClient { get; private set; } = null!;
	[Dependency] public static IVideoMode videoMode { get; private set; } = null!;
	[Dependency] public static IMatSystemSurface surface { get; private set; } = null!;
	[Dependency] public static ISurface Surface { get; private set; } = null!;
	[Dependency] public static ISchemeManager SchemeManager { get; private set; } = null!;
	[Dependency] public static IVGui VGui { get; private set; } = null!;
	[Dependency] public static IVGuiInput Input { get; private set; } = null!;
	[Dependency] public static ILocalize Localize { get; private set; } = null!;
	[Dependency] public static ISystem system { get; private set; } = null!;
#else
	public static IClientEntityList entitylist { get; private set; } = null!;
#endif

	public static TimeUnit_t TICKS_TO_TIME(int t) => host_state.IntervalPerTick * t;
	public static int TIME_TO_TICKS(TimeUnit_t dt) => (int)(0.5f + dt / host_state.IntervalPerTick);

}
