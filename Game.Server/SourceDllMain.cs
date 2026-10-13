using Game.Shared;

using Microsoft.Extensions.DependencyInjection;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.DataCache;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.MaterialSystem;
using Source.Common.SceneFileCache;
using Source.Common.Server;
using Source.Common.SoundEmitterSystem;
using Source.Common.ToolFramework;

namespace Game.Server;

public static class SourceDllMain
{
	public static void Link(IServiceCollection services) {
		ClassMap.Init();
	}

	[Dependency] public static IEngineServer engine { get; private set; } = null!;
	[Dependency] public static IFileSystem filesystem { get; private set; } = null!;
	[Dependency] public static ServerGlobalVariables gpGlobals { get; private set; } = null!;
	[Dependency] public static ServerGameDLL g_ServerGameDLL { get; private set; } = null!;
	[Dependency] public static ICvar cvar { get; private set; } = null!;
	[Dependency] public static ICommandLine commandLine { get; private set; } = null!;
	[Dependency] public static ISpatialPartition partition { get; private set; } = null!;
	[Dependency] public static IUniformRandomStream random { get; private set; } = null!;
	[Dependency] public static ISoundEmitterSystemBase soundemitterbase { get; private set; } = null!;
	[Dependency] public static IGameEventManager2 gameeventmanager { get; private set; } = null!;
	[Dependency] public static IDataCache datacache { get; private set; } = null!;
	[Dependency] public static IVoiceServer g_pVoiceServer { get; private set; } = null!;
	[Dependency] public static UserMessages usermessages { get; private set; } = null!;
	[Dependency] public static IMDLCache mdlcache { get; private set; } = null!;
	[Dependency] public static IMaterialSystem materials { get; private set; } = null!;
	[Dependency] public static ISceneFileCache scenefilecache { get; private set; } = null!;
	[KeyedDependency(Key = Realm.Server)] public static IVModelInfo modelinfo { get; private set; } = null!;
	[Dependency] public static GlobalEntityList gEntList { get; private set; } = null!;
	[Dependency] public static BaseEntityList g_pEntityList { get; private set; } = null!;
	[Dependency] public static IServerPluginHelpers serverpluginhelpers { get; private set; } = null!;
	[Dependency(Required = false)] public static IServerEngineTools? serverenginetools { get; private set; } = null!;
#if GMOD_DLL
	[Dependency] public static Source.Common.GarrysMod.IGet get { get; private set; } = null!;
#endif


	[KeyedDependency(Key = Realm.Server)] public static INetworkStringTableContainer networkstringtable { get; private set; } = null!;
	[KeyedDependency(Key = Realm.Server)] public static IEngineTrace enginetrace { get; private set; } = null!;
	[KeyedDependency(Key = Realm.Server)] public static IEngineSound enginesound { get; private set; } = null!;

	public static TimeUnit_t TICK_INTERVAL => gpGlobals.IntervalPerTick;

	public static TimeUnit_t TICKS_TO_TIME(int t) => TICK_INTERVAL * t;
	public static int TIME_TO_TICKS(TimeUnit_t dt) => (int)(0.5f + dt / TICK_INTERVAL);

	public static bool IsEngineThreaded() => false;
}
