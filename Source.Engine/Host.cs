using Microsoft.Extensions.DependencyInjection;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Input;
using Source.Common.Networking;
using Source.Common.Server;
using Source.Common.Utilities;
using Source.Engine.Client;
using Source.Engine.Server;

using Steamworks;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using static Source.Constants;

namespace Source.Engine;

public sealed class HostErrorException(string message) : Exception(message);

public class CommonHostState
{
	public Model? WorldModel;
	public WorldBrushData? WorldBrush;
	public TimeUnit_t IntervalPerTick;
	public void SetWorldModel(Model? model) {
		WorldModel = model;
		if (model != null)
			WorldBrush = model.Brush.Shared;
		else
			WorldBrush = null;
	}
}

public partial class Host
{
	public int TimeToTicks(TimeUnit_t dt) => (int)(0.5 + dt / host_state.IntervalPerTick);
	public TimeUnit_t TicksToTime(int dt) => host_state.IntervalPerTick * dt;

	public string GetCurrentMod() => host_parms.Mod;
	public string GetCurrentGame() => host_parms.Game;
	public string GetBaseDirectory() => host_parms.BaseDir;

	public static readonly ConVar host_name = new("hostname", "", 0, "Hostname for server.");
	public static readonly ConVar host_map = new("host_map", "", 0, "Current map name.");
	public static readonly ConVar developer = new("developer", "0", 0, "Set developer message level");
	public static readonly ConVar host_timescale = new("host_timescale", "1.0", FCvar.Replicated, "Prescale the clock by this amount.");
	public static readonly ConVar host_limitlocal = new("host_limitlocal", "0", 0, "Apply cl_cmdrate and cl_updaterate to loopback connection");

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
	public static GameClient? Client;
	public ClientGlobalVariables clientGlobalVariables = null!;
	public CL CL = null!;
	public MatSysInterface MatSysInterface = null!;
	public IModelLoader modelloader = null!;
	public SV SV = null!;
	public ServerGlobalVariables serverGlobalVariables = null!;
	public Cbuf Cbuf = null!;
	public Cmd Cmd = null!;
	public Con Con = null!;
	public Key Key = null!;
#if !SWDS
	public EngineVGui EngineVGui = null!;
#endif
	public Cvar Cvar = null!;
	public View View = null!;
	public Render Render = null!;
	public Common Common = null!;
	public IEngine Engine = null!;
	public Scr Scr = null!;
	public Net Net = null!;
	public Sys Sys = null!;
	public ISoundServices soundServices = null!;
	public ClientDLL ClientDLL = null!;
	public Sound Sound = null!;
	public IHostState HostState = null!;
	public IBaseClientDLL? clientDLL;
	public IGameEventManager2 GameEventManager = null!;
	public IServerGameDLL? serverDLL;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

	public bool Initialized;
	public TimeUnit_t Time;
	public TimeUnit_t FrameTime;
	public TimeUnit_t FrameTimeUnbounded;
	public TimeUnit_t FrameTimeStandardDeviation;
	public TimeUnit_t RealTime;
	public TimeUnit_t IdealTime;
	public TimeUnit_t NextTick;
	public TimeUnit_t[] JitterHistory = new TimeUnit_t[128];
	public int JitterHistoryPos;
	public long FrameCount;
	public int HunkLevel;
	public int FrameTicks;
	public int TickCount;
	public int CurrentFrameTick;

	public int NumTicksLastFrame;
	public TimeUnit_t RemainderLastFrame;
	public TimeUnit_t PrevRemainderLastFrame;
	public TimeUnit_t LastFrameTime;

	public bool IsSinglePlayerGame() {
		if (sv.IsActive())
			return !sv.IsMultiplayer();
		else
			return cl.MaxClients == 1;
	}

	const TimeUnit_t MAX_FRAMETIME = 0.1;

	void AccumulateTime(TimeUnit_t dt) {
		RealTime += dt;
		FrameTime = dt;
		FrameTimeUnbounded = FrameTime;
		if (host_timescale.GetFloat() > 0 && CanCheat()) {
			TimeUnit_t fullscale = host_timescale.GetFloat();
			FrameTime *= fullscale;
			FrameTimeUnbounded = FrameTime;
			FrameTime = Math.Min(FrameTime, MAX_FRAMETIME * fullscale);
		}

		soundServices.SetSoundFrametime(dt, FrameTime);
	}

	int gHostSpawnCount;

	public int GetServerCount() {
		if (cl.SignOnState >= SignOnState.New)
			return cl.ServerCount;
		else if (sv.State >= ServerState.Loading)
			return sv.GetSpawnCount();

		return gHostSpawnCount;
	}

	void _SetGlobalTime() {
		serverGlobalVariables.RealTime = RealTime;
		serverGlobalVariables.FrameCount = FrameCount;
		serverGlobalVariables.AbsoluteFrameTime = FrameTime;
		serverGlobalVariables.IntervalPerTick = host_state.IntervalPerTick;
		serverGlobalVariables.ServerCount = GetServerCount();
#if !SWDS
		clientGlobalVariables.RealTime = RealTime;
		clientGlobalVariables.FrameCount = FrameCount;
		clientGlobalVariables.AbsoluteFrameTime = FrameTime;
		clientGlobalVariables.IntervalPerTick = host_state.IntervalPerTick;
#endif
	}

	TimeUnit_t Remainder;
	public TimeUnit_t FramesPerSecond;

	void _RunFrame(TimeUnit_t time) {
		int numticks;
		TimeUnit_t prevRemainder;
		bool shouldRender;

		{
			AccumulateTime(time);
			_SetGlobalTime();
			shouldRender = !sv.IsDedicated();
			prevRemainder = Remainder;
			if (prevRemainder < 0)
				prevRemainder = 0;

			Remainder += FrameTime;
			numticks = 0;
			if (Remainder >= host_state.IntervalPerTick) {
				numticks = (int)(Math.Floor(Remainder / host_state.IntervalPerTick));

				if (IsSinglePlayerGame() && BaseServer.sv_alternateticks.GetBool()) {
					int startTick = (int)serverGlobalVariables.TickCount;
					int endTick = startTick + numticks;
					endTick = endTick.AlignValue(2);
					numticks = endTick - startTick;
				}

				Remainder -= numticks * host_state.IntervalPerTick;
			}
			NextTick = host_state.IntervalPerTick - Remainder;
			mdlcache.MarkFrame();
		}

		{

			Cbuf.Execute();
			if (Net.Dedicated && !Net.IsMultiplayer())
				Net.SetMultiplayer(true);

			serverGlobalVariables.InterpolationAmount = 0.0;
#if !SWDS
			clientGlobalVariables.InterpolationAmount = 0.0;
			cl.InSimulation = true;
#endif

			FrameTicks = numticks;
			CurrentFrameTick = 0;

#if !SWDS
			// TODO: enginetool
#endif

			if (!EngineThreads.IsEngineThreaded()) {
#if !SWDS
				clientDLL?.IN_SetSampleTime(FrameTime);
				clientGlobalVariables.SimTicksThisFrame = 1;
#endif

				cl.TickRemainder = Remainder;
				serverGlobalVariables.SimTicksThisFrame = 1;
				cl.SetFrameTime(FrameTime);

				for (int tick = 0; tick < numticks; tick++) {
					TimeUnit_t now = Platform.Time;
					TimeUnit_t jitter = now - IdealTime;
					JitterHistory[JitterHistoryPos] = jitter;
					JitterHistoryPos = (JitterHistoryPos + 1) % JitterHistory.Length;

					if (Math.Abs(jitter) > 1.0)
						IdealTime = now;
					else
						IdealTime = 0.99 * IdealTime + 0.01 * now;

					Net.RunFrame(now);
					bool finalTick = tick == (numticks - 1);

					if (Net.Dedicated && !Net.IsMultiplayer())
						Net.SetMultiplayer(true);

					serverGlobalVariables.TickCount = sv.TickCount;
					TickCount++;
					CurrentFrameTick++;

#if !SWDS
					clientGlobalVariables.TickCount = cl.GetClientTickCount();
					CL.CheckClientState();
#endif
					_RunFrame_Input(prevRemainder, finalTick);
					prevRemainder = 0;
					_RunFrame_Server(finalTick);
					Net.SendQueuedPackets();

#if !SWDS
					if (!sv.IsDedicated())
						_RunFrame_Client(finalTick);
					// todo: tool framework
#endif

					IdealTime += host_state.IntervalPerTick;
				}

#if !SWDS
				if (numticks == 0 && false) // todo: demoplayer
					_RunFrame_Client(true);

				if (!sv.IsDedicated()) {
					SetClientInSimulation(false);
					clientGlobalVariables.InterpolationAmount = cl.TickRemainder / host_state.IntervalPerTick;
					// replay?
					CL.RunPrediction(PredictionReason.Normal);
					CL.ApplyAddAngle();
					CL.ExtraMouseUpdate(clientGlobalVariables.FrameTime);
				}
#endif


			}
#if !SWDS
			else {
				throw new NotImplementedException("Threaded Host.RunFrame path is not implemented yet");
			}

			if (shouldRender) {
				_RunFrame_Render();
				_RunFrame_Sound();
			}
			// else
			// updatedynamicmodels

#if !SWDS
			if (!sv.IsDedicated()) {
				ClientDLL.Update();
			}
#endif

			Speeds();
			UpdateMapList();
			FrameCount++;
			Time = TickCount * host_state.IntervalPerTick + cl.TickRemainder;
			PostFrameRate(FrameTime);
#endif
		}
	}
	public static void DefaultMapFileName(ReadOnlySpan<char> fullMapName, Span<char> diskName) {
		sprintf(diskName, "maps/%s.bsp").S(fullMapName);
	}
	const TimeUnit_t FPS_AVG_FRAC = 0.9;

	private void PostFrameRate(TimeUnit_t frameTime) {
		frameTime = Math.Clamp(frameTime, 0.0001, 1.0);
		TimeUnit_t fps = 1.0 / frameTime;
		FramesPerSecond = fps * FPS_AVG_FRAC + (1.0 - FPS_AVG_FRAC) * fps;
	}

	private void UpdateMapList() {

	}

	private void Speeds() {

	}

	public void UpdateScreen() {
		Scr.UpdateScreen();
	}

	private void _RunFrame_Render() {
		UpdateScreen();
		CL.DecayLights();
	}

	private void _RunFrame_Sound() {
		UpdateSounds();
	}

	AudioState audioState;

	public void SetAudioState(in AudioState state) => audioState = state;

	[ConCommand("snd_restart", "Restart sound system.")]
	public void Snd_Restart_f() {
#if !SWDS
		Span<char> szVoiceCodec = stackalloc char[MAX_PATH];
		szVoiceCodec.Clear();
		int nVoiceSampleRate = Voice.ConfiguredSampleRate();

		{
			// This is not valid after voice shuts down
			ReadOnlySpan<char> pPreviousCodec = Voice.ConfiguredCodec();
			if (!pPreviousCodec.IsEmpty && pPreviousCodec[0] != '\0')
				strcpy(szVoiceCodec, pPreviousCodec);
		}

		Sound.Shutdown();
		g_AudioSystem.MarkFirstTime();
		cl.ClearSounds();
		Sound.Init();

		// Restart voice if it was running
		if (szVoiceCodec[0] != '\0')
			Voice.Init(szVoiceCodec, nVoiceSampleRate);

		// Do this or else it won't have anything in the cache.
		if (!sv.GetMapName().IsEmpty && sv.GetMapName()[0] != '\0')
			g_AudioSystem.LevelInit(sv.GetMapName());

		// Flush soundscapes so they don't stop. We don't insert text in the buffer here because
		// cl_soundscape_flush is normally cheat-protected.
		ConCommand? pCommand = cvar.FindCommand("cl_soundscape_flush");
		if (pCommand != null) {
			TokenizedCommand cmd = new();
			cmd.Tokenize("cl_soundscape_flush");
			pCommand.Dispatch(in cmd, CommandSource.Command, -1);
		}
#endif
	}

	public void UpdateSounds() {
		if (cl.IsActive()) {
			Sound.Update(in audioState);
		}
		else {
			Sound.Update();
		}
	}

	public void SetClientInSimulation(bool inSimulation) {
		cl.InSimulation = inSimulation || cl.IsPaused();
		clientGlobalVariables.CurTime = cl.GetTime();
		clientGlobalVariables.FrameTime = cl.GetFrameTime();
	}

	private void _RunFrame_Client(bool finalTick) {
#if !SWDS
		CL.ReadPackets(finalTick);
		CL.ProcessVoiceData();

		cl.CheckUpdatingSteamResources();
		cl.CheckFileCRCsWithServer();

		cl.RunFrame();
		Steam3Client()?.RunFrame();
#endif
	}

	private void _RunFrame_Server(bool finalTick) {
		SV.Frame(finalTick);
	}

	private bool input_firstFrame = true;

	public bool LowViolence { get; set; } = false;

	private void _RunFrame_Input(TimeUnit_t accumulatedExtraSamples, bool finalTick) {
		if (input_firstFrame) {
			input_firstFrame = false;
			// test script?
		}

#if !SWDS
		ClientDLL.ProcessInput();
		Cbuf.Execute();
		CL.Move(accumulatedExtraSamples, finalTick);
#endif
	}

	public void RunFrame(TimeUnit_t frameTime) {
		_RunFrame(frameTime);
	}

	public void PostInit() {
		if (SV.ServerGameDLL != null)
			SV.ServerGameDLL.PostInit();
		serverDLL = SV.ServerGameDLL;

		var clientDLL = services.GetService<IBaseClientDLL>();
		if (clientDLL != null)
			clientDLL.PostInit();
		this.clientDLL = clientDLL;
	}

	bool ConfigCfgExecuted = false;
	public void ReadConfiguration() {
		if (sv.IsDedicated())
			return;

		if (fileSystem == null)
			Sys.Error("Host_ReadConfiguration:  g_pFileSystem == NULL\n");

		bool saveconfig = false;

		if (fileSystem!.FileExists("cfg/config.cfg", "MOD"))
			Cbuf.AddText("exec config.cfg\n");
		else {
			Cbuf.AddText("exec config_default.cfg\n");
			saveconfig = true;
		}

		Cbuf.Execute();

		int numBinds = Key.CountBindings();
		if (numBinds == 0)
			UseDefaultBindings();
		else
			SetupNewBindings();

		Key.SetBinding(ButtonCode.KeyEscape, "cancelselect");

		if (Key.NameForBinding("toggleconsole").IsEmpty)
			Key.SetBinding(ButtonCode.KeyBackquote, "toggleconsole");

		ConfigCfgExecuted = true;

		if (saveconfig) {
			bool saveinit = Initialized;
			Initialized = true;
			WriteConfiguration();
			Initialized = saveinit;
		}
	}

	private void SetupNewBindings() {
		// todo
	}

	private void UseDefaultBindings() {
		// todo
	}

	public void WriteConfiguration(ReadOnlySpan<char> filename = default, bool allVars = false) {
		bool isUserRequested = !filename.IsEmpty;
		if (filename.IsEmpty)
			filename = "config.cfg";

		if (!Initialized)
			return;

		if (!isUserRequested && commandLine.CheckParm("-default"))
			return;

		if (sv.IsDedicated())
			return;

		using MemoryStream str = new();
		using StreamWriter configBuff = new(str);

		configBuff.Write($"cfg/{filename}");
		fileSystem.CreateDirHierarchy("cfg", "MOD");
	}

	public bool IsSecureServerAllowed() => true;

	public void Init(bool dedicated) {
		RealTime = 0;
		IdealTime = 0;

		OverlayText.IsDeadFn += OverlayText_IsDeadFn;
		OverlayText.SetEndTimeFn += OverlayText_SetEndTimeFn;

		host_state.IntervalPerTick = DEFAULT_TICK_INTERVAL;

		Engine = services.GetRequiredService<IEngine>();
		var engineAPI = services.GetRequiredService<IEngineAPI>();
		var hostState = services.GetRequiredService<IHostState>();
		Sys = services.GetRequiredService<Sys>();

		clientGlobalVariables = services.GetRequiredService<ClientGlobalVariables>();
		serverGlobalVariables = services.GetRequiredService<ServerGlobalVariables>();

		Con = engineAPI.InitSubsystem<Con>()!;
		Cbuf = engineAPI.InitSubsystem<Cbuf>()!;
		Cmd = engineAPI.InitSubsystem<Cmd>()!;
		Cvar = engineAPI.InitSubsystem<Cvar>()!;
		Cvar.Changed += CvarUtilities.ConVarNetworkChangeCallback; // TODO CvarQuery
		soundServices = engineAPI.GetRequiredService<ISoundServices>();
#if !SWDS
		View = engineAPI.InitSubsystem<View>()!;
#endif
		Common = engineAPI.InitSubsystem<Common>()!;
		Key = engineAPI.InitSubsystem<Key>()!;
		engineAPI.InitSubsystem<Filter>();
#if !SWDS
		engineAPI.InitSubsystem<Key>();
#endif
		Net = engineAPI.InitSubsystem<Net>(dedicated)!;
		GameEventManager = engineAPI.InitSubsystem<IGameEventManager2>()!;
		sv.Init(dedicated);
		SV = services.GetRequiredService<SV>();
#if GMOD_DLL
		g_pFileSystem.SetGet(get);
		get.Initialize(g_pFileSystem);
#endif
		SV.InitGameDLL();
#if !SWDS
		if (!dedicated) {
			CL = engineAPI.InitSubsystem<CL>()!;
			MatSysInterface = engineAPI.InitSubsystem<MatSysInterface>()!;
			modelloader = engineAPI.InitSubsystem<IModelLoader>()!;
			StaticPropMgr().Init();
			EngineVGui = engineAPI.InitSubsystem<EngineVGui>()!;
			ClientDLL = engineAPI.InitSubsystem<ClientDLL>()!;
			HostState = engineAPI.GetRequiredService<IHostState>();
			Scr = engineAPI.InitSubsystem<Scr>()!;
			Render = engineAPI.InitSubsystem<Render>()!;
			// engineAPI.InitSubsystem<Decal>();
		}
		else {
			cl.SignOnState = SignOnState.None;
		}
#else
		modelloader = engineAPI.InitSubsystem<IModelLoader>()!;
		HostState = engineAPI.GetRequiredService<IHostState>();
		MatSysInterface = engineAPI.InitSubsystem<MatSysInterface>()!;
#endif
#if GMOD_DLL
		g_pFileSystem.Gamemodes().Refresh();
#endif

#if !SWDS
		ReadConfiguration();
		Sound = engineAPI.InitSubsystem<Sound>()!;
#endif
		Cbuf.AddText("exec valve.rc");

		AllowQueuedMaterialSystem(false);

		Initialized = true;
		hostState.Init();

		PostInit();
	}

	public void Shutdown() {
		OverlayText.IsDeadFn -= OverlayText_IsDeadFn;
		OverlayText.SetEndTimeFn -= OverlayText_SetEndTimeFn;

		Disconnect(true);

		CM.FreeMap();

#if !SWDS
		Scr.DisabledForLoading = true;
		if (!sv.IsDedicated()) {
			// Decal.Shutdown();
			Render.Shutdown();
			Scr.Shutdown();
			Sound.Shutdown();
			ClientDLL.Shutdown();
			// TextMessageShutdown();
			EngineVGui.Shutdown();
			g_StaticPropMgr.Shutdown();
			modelloader.Shutdown();
			// ShutdownStudioRender();
			// ShutdownMaterialSystem();
			CL.Shutdown();
		}
		else
#endif
		{
			// Decal.Shutdown();
			modelloader.Shutdown();
			// ShutdownStudioRender();
			// StaticPropMgr.Shutdown();
			// ShutdownMaterialSystem();
		}


		// HLTV.Shutdown();
		// Log.Shutdown();
		// GameEventManager.Shutdown();

		sv.Shutdown();
		Net.Shutdown();

#if !SWDS
		Key.Shutdown();
#endif


		Common.Shutdown();

#if !SWDS
		View.Shutdown();
#endif

		Cvar.Shutdown();
		Cmd.Shutdown();
		Cbuf.Shutdown();
		Con.Shutdown();
	}

	private void OverlayText_SetEndTimeFn(OverlayText text, TimeUnit_t duration) {
		text.ServerCount = cl.ServerCount;

		if (duration <= 0.0f) {
			text.EndTime = 0.0f;
			text.CreationTick = (int)GetOverlayTick();
			return;
		}

		if (duration == IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER)
			text.EndTime = IVDebugOverlay.NDEBUG_PERSIST_TILL_NEXT_SERVER;
		else
			text.EndTime = cl.GetTime() + duration;
	}

	private bool OverlayText_IsDeadFn(OverlayText text) {
		if (text.ServerCount != cl.ServerCount)
			return true;

		if (text.CreationTick != -1)
			return GetOverlayTick() > text.CreationTick;

		if (text.EndTime == 0)
			return false;

		return cl.GetTime() >= text.EndTime;
	}

	private long GetOverlayTick() {
		if (sv.IsActive())
			return sv.TickCount;
		return cl.GetClientTickCount();
	}

	public void Disconnect(bool showMainMenu, ReadOnlySpan<char> reason = default) {
#if !SWDS
		if (!sv.IsDedicated())
			cl.Disconnect(reason, showMainMenu);
#endif
		HostState.GameShutdown();
	}

	public void Disconnect() {
		Disconnect(true);
	}

	[ConCommand(helpText: "Print version info string.")]
	void version() {
		// todo
		// We should probably just use the steam inf information. Right now build_number is implemented to use a custom solution in the meantime.
		// Can we use that + add some custom stuff for Source.NET?
	}

	[ConCommand]
	void disconnect(in TokenizedCommand args) {
		if (clientDLL == null || !clientDLL.DisconnectAttempt()) {
			string test = args.ArgS(1).ToString();
			if (string.IsNullOrEmpty(test))
				Disconnect();
			else
				Disconnect(false, test.ToString());
		}
	}

	[ConCommand("pause", "Toggle the server pause state.")]
	void pause(in TokenizedCommand args, CommandSource source, int clientSlot) {
#if !SWDS
		if (!sv.IsDedicated()) {
			if (cl.LevelFileName == null || cl.LevelFileName.Length == 0)
				return;
		}
#endif

		if (source == CommandSource.Command) {
			Cmd.ForwardToServer(args);
			return;
		}

		if (!sv.IsPausable())
			return;

		sv.SetPaused(!sv.IsPaused());

		// sv.BroadcastPrintf( "%s %s the game\n", host_client->GetClientName(), sv.IsPaused() ? "paused" : "unpaused" );
	}

	[ConCommand("setpause", "Set the pause state of the server.")]
	void setpause(in TokenizedCommand args, CommandSource source, int clientSlot) {
#if !SWDS
		if (cl.LevelFileName == null || cl.LevelFileName.Length == 0)
			return;
#endif

		if (source == CommandSource.Command) {
			Cmd.ForwardToServer(args);
			return;
		}

		sv.SetPaused(true);
	}

	[ConCommand("unpause", "Unpause the game.")]
	void unpause(in TokenizedCommand args, CommandSource source, int clientSlot) {
#if !SWDS
		if (cl.LevelFileName == null || cl.LevelFileName.Length == 0)
			return;
#endif

		if (source == CommandSource.Command) {
			Cmd.ForwardToServer(args);
			return;
		}

		sv.SetPaused(false);
	}

	static public bool CanCheat() {
		return SV.sv_cheats.GetBool();
	}

	internal void CheckGore() {
		// todo
	}

	public bool ChangeLevel(bool loadFromSavedGame, ReadOnlySpan<char> levelName, ReadOnlySpan<char> landmarkName) {
		if (!sv.IsActive()) {
			Dbg.ConMsg("Only the server may changelevel\n");
			return false;
		}

#if !SWDS
		Scr.BeginLoadingPlaque();
		// stop sounds
#endif

		sv.InactivateClients();
		// do the rest later
		return true;
	}

	IEnumerable<string> Map_CompletionFunc(string partial) { // todo: properly implement this once host maplist stuff is done
		int space = partial.IndexOf(' ');
		string prefix = space >= 0 ? partial[..(space + 1)] : "map ";
		string arg = space >= 0 ? partial[(space + 1)..] : "";

		ReadOnlySpan<char> filename = fileSystem.FindFirstEx("maps/*.bsp", null, out FileFindHandle_t findHandle);
		while (!filename.IsEmpty) {
#pragma warning disable CA2014 // Do not use stackalloc in loops
			ReadOnlySpan<char> mapName = filename.StripExtension(stackalloc char[256]);
#pragma warning restore CA2014 // Do not use stackalloc in loops
			string name = mapName.SliceNullTerminatedString().ToString();
			if (name.StartsWith(arg, StringComparison.OrdinalIgnoreCase))
				yield return prefix + name;
			filename = fileSystem.FindNext(findHandle);
		}
		fileSystem.FindClose(findHandle);
	}

	private void Map_Helper(in TokenizedCommand args, CommandSource source, bool editmode, bool background, bool commentary) {
		if (source != CommandSource.Command)
			return;

		if (args.ArgC() < 2) {
			Warning("No map specified\n");
			return;
		}

		Span<char> mapName = stackalloc char[128];
		strcpy(mapName, args[1]);

		ReadOnlySpan<char> reason = null;
		// lots to do here still
		Disconnect(false);
		HostState.NewGame(mapName, false, background);
		// TODO: accept setpos, setang.
	}


	const int STATUS_COLUMN_LENGTH_LINEPREFIX = 1;
	const int STATUS_COLUMN_LENGTH_USERID = 6;
	const string STATUS_COLUMN_LENGTH_USERID_STR = "6";
	const int STATUS_COLUMN_LENGTH_NAME = 19;
	const int STATUS_COLUMN_LENGTH_STEAMID = 19;
	const int STATUS_COLUMN_LENGTH_TIME = 9;
	const int STATUS_COLUMN_LENGTH_PING = 4;
	const string STATUS_COLUMN_LENGTH_PING_STR = "4";
	const int STATUS_COLUMN_LENGTH_LOSS = 4;
	const string STATUS_COLUMN_LENGTH_LOSS_STR = "4";
	const int STATUS_COLUMN_LENGTH_STATE = 6;
	const int STATUS_COLUMN_LENGTH_ADDR = 21;

	ref struct StatusLineBuilder
	{
		int CurPosition;
		InlineArray512<char> Line;
		internal void AddColumnText(ReadOnlySpan<char> text, int columnWidth) {
			int len = (int)strlen(Line);

			if (CurPosition > len) {
				for (int i = len; i < CurPosition; i++)
					Line[i] = ' ';

				Line[CurPosition] = '\0';
			}
			else if (len != 0) {
				Line[len] = ' ';
				Line[len + 1] = '\0';
			}
			StrTools.StrConcat(Line, text);
			CurPosition += columnWidth + 1;
		}
		internal void Reset() { CurPosition = 0; Line[0] = '\0'; }
		internal void InsertEmptyColumn(int columnWidth) => CurPosition += columnWidth + 1;
		internal readonly unsafe ReadOnlySpan<char> GetLine() {
#pragma warning disable CS9084 // Struct member returns 'this' or other instance members by reference
			return ((ReadOnlySpan<char>)Line).SliceNullTerminatedString();
#pragma warning restore CS9084 // Struct member returns 'this' or other instance members by reference
		}
	}

	public bool NewGame(ReadOnlySpan<char> mapName, bool loadGame, bool backgroundLevel, ReadOnlySpan<char> oldMap = default, ReadOnlySpan<char> landmark = default, bool oldSave = false) {
		Common.TimestampedLog("Host_NewGame");
		Span<char> previousMapName = stackalloc char[MAX_PATH];
		host_map.GetString().CopyTo(previousMapName);
#if !SWDS
		Scr.BeginLoadingPlaque();
#endif
		Span<char> _mapName = stackalloc char[MAX_PATH];
		Span<char> _mapFile = stackalloc char[MAX_PATH];
		mapName.CopyTo(_mapName);
		DefaultMapFileName(_mapName, _mapFile);

		SV.InitGameServerSteam();

		if (!modelloader.Map_IsValid(_mapFile)) {
#if !SWDS
			Scr.EndLoadingPlaque();
#endif
			return false;
		}

		DevMsg("---- Host_NewGame ----\n");
		host_map.SetValue(_mapName);
		if (!loadGame) {
			HostState.RunGameInit();
		}

		Net.SetMultiplayer(sv.IsMultiplayer());
		Net.ListenSocket(sv.Socket, true);

		if (host_name.GetString().Length == 0)
			host_name.SetValue(serverDLL!.GetGameDescription());

		g_DownloadListGenerator.OnLevelLoadStart(mapName);

		if (!sv.SpawnServer(mapName, _mapFile, null))
			return false;

		sv.LevelMainMenuBackground = backgroundLevel;
		serverGlobalVariables.CurTime = sv.GetTime();

		Common.TimestampedLog("serverGameDLL.LevelInit");
#if !SWDS
		EngineVGui.UpdateProgressBar(LevelLoadingProgress.LevelInit);
#endif

		serverPluginHandler.LevelInit(mapName, GetCollisionBSPData().MapEntityData, oldMap, landmark, loadGame && !oldSave, backgroundLevel);

		if (loadGame && !oldSave) {
			sv.SetPaused(true);
			sv.LoadGame = true;
			serverGlobalVariables.CurTime = sv.GetTime();
		}

		if (!SV.ActivateServer())
			return false;

		if (!sv.IsDedicated()) {
			Common.TimestampedLog("Stuff 'connect localhost' to console");

			Span<char> str = stackalloc char[512];
			sprintf(str, "connect localhost:%d listenserver").D(sv.GetUDPPort());
			Cbuf.AddText(str);
		}


		return true;
	}


	public EUniverse GetSteamUniverse() {
#if !SWDS
		return Steam3Client().SteamUtils()?.GetConnectedUniverse() ?? EUniverse.k_EUniverseInvalid;
#else
		return Steam3Server().SteamGameServerUtils()?.GetConnectedUniverse() ?? EUniverse.k_EUniverseInvalid;
#endif
	}


	delegate void PrinterFn(ReadOnlySpan<char> text);

	static ConVarRef sv_tags;
	[ConCommand(helpText: "Display map and connection status.")]
	void status(in TokenizedCommand args, CommandSource source, int clientslot) {
		PrinterFn print;
		if (source == CommandSource.Command) {
			if (!sv.IsActive()) {
				Cmd.ForwardToServer(in args);
				return;
			}

			print = (txt) => Dbg.ConMsg(txt);
		}
		else {
			print = Client_Print;
		}

		print($"hostname: {host_name.GetString()}\n");
		ReadOnlySpan<char> pchSecureReasonString = "";
		ReadOnlySpan<char> pchUniverse = "";
		bool bGSSecure = Steam3Server().BSecure();
		if (!bGSSecure && Steam3Server().BWantsSecure())
			if (Steam3Server().BLoggedOn())
				pchSecureReasonString = " (secure mode enabled, connected to Steam3)";
			else
				pchSecureReasonString = " (secure mode enabled, disconnected from Steam3)";


		switch (GetSteamUniverse()) {
			case EUniverse.k_EUniversePublic:
				pchUniverse = "";
				break;
			case EUniverse.k_EUniverseBeta:
				pchUniverse = " (beta)";
				break;
			case EUniverse.k_EUniverseInternal:
				pchUniverse = " (internal)";
				break;
			case EUniverse.k_EUniverseDev:
				pchUniverse = " (dev)";
				break;
			default:
				pchUniverse = " (unknown)";
				break;
		}

		print($"version : {GetSteamInfIDVersionInfo().PatchVersion}/{Protocol.VERSION} {build_number()} {(bGSSecure ? "secure" : "insecure")}{pchSecureReasonString}{pchUniverse}\n");

		if (Net.IsMultiplayer()) {
			// todo
		}

		ConVarRef sv_registration_successful = new("sv_registration_successful", true);
		if (sv_registration_successful.IsValid()) {
			Span<char> sExtraInfo = stackalloc char[256];
			ConVarRef sv_registration_message = new("sv_registration_message", true);
			if (sv_registration_message.IsValid()) {
				ReadOnlySpan<char> msg = sv_registration_message.GetString();
				if (!msg.IsEmpty)
					sprintf(sExtraInfo, "  (%s)").S(msg);
			}

			if (sv_registration_successful.GetBool())
				print($"account : logged in{sExtraInfo}\n");
			else
				print($"account : not logged in{sExtraInfo}\n");
		}

		print($"map     : {sv.GetMapName()} at: {(int)MainViewOrigin()[0]} x, {(int)MainViewOrigin()[1]} y, {(int)MainViewOrigin()[2]} z\n");
		if (sv_tags.IsEmpty) sv_tags = new("sv_tags");
		print($"tags    : {sv_tags.GetString()}\n");

		int players = sv.GetNumClients();
		int nBots = sv.GetNumFakeClients();
		int nHumans = players - nBots;

		print($"players : {nHumans} humans, {nBots} bots ({sv.GetMaxClients()} max)\n");
		print($"edicts  : {sv.NumEdicts - sv.FreeEdicts} used of {sv.MaxEdicts} max\n");

		// the header for the status rows
		// print( "# userid %-19s %-19s connected ping loss state%s\n", "name", "uniqueid", cmd_source == src_command ? "  adr" : "" );
		StatusLineBuilder header = new();
		header.AddColumnText("#", STATUS_COLUMN_LENGTH_LINEPREFIX);
		header.AddColumnText("userid", STATUS_COLUMN_LENGTH_USERID);
		header.AddColumnText("name", STATUS_COLUMN_LENGTH_NAME);
		header.AddColumnText("uniqueid", STATUS_COLUMN_LENGTH_STEAMID);
		header.AddColumnText("connected", STATUS_COLUMN_LENGTH_TIME);
		header.AddColumnText("ping", STATUS_COLUMN_LENGTH_PING);
		header.AddColumnText("loss", STATUS_COLUMN_LENGTH_LOSS);
		header.AddColumnText("state", STATUS_COLUMN_LENGTH_STATE);
		if (source == CommandSource.Command) {
			header.AddColumnText("adr", STATUS_COLUMN_LENGTH_ADDR);
		}

		print($"{header.GetLine()}\n");

		for (int j = 0; j < sv.GetClientCount(); j++) {
			IClient? client = sv.GetClient(j);

			if (!client!.IsConnected())
				continue;
			Status_PrintClient(client, (source == CommandSource.Command), print);
		}
	}

	private void Status_PrintClient(IClient client, bool showAddress, PrinterFn print) {
		INetChannelInfo? nci = client.GetNetChannel();

		ReadOnlySpan<char> state = "challenging";
		if (client.IsActive())
			state = "active";
		else if (client.IsSpawned())
			state = "spawning";
		else if (client.IsConnected())
			state = "connecting";

		StatusLineBuilder builder = new();
		builder.AddColumnText("#", STATUS_COLUMN_LENGTH_LINEPREFIX);
		builder.AddColumnText($"{client.GetUserID()}", STATUS_COLUMN_LENGTH_USERID);
		builder.AddColumnText(client.GetClientName(), STATUS_COLUMN_LENGTH_NAME);
		builder.AddColumnText(client.GetNetworkIDString(), STATUS_COLUMN_LENGTH_STEAMID);

		if (nci != null) {
			builder.AddColumnText(Common.FormatSeconds(nci.GetTimeConnected()), STATUS_COLUMN_LENGTH_TIME);
			builder.AddColumnText($"{(int)(1000.0f * nci.GetAverageLatency(NetFlow.FLOW_OUTGOING))}", STATUS_COLUMN_LENGTH_PING);
			builder.AddColumnText($"{(int)(100.0f * nci.GetAverageLoss(NetFlow.FLOW_INCOMING))}", STATUS_COLUMN_LENGTH_LOSS);
			builder.AddColumnText(state, STATUS_COLUMN_LENGTH_STATE);
			if (showAddress)
				builder.AddColumnText(nci.GetAddress(), STATUS_COLUMN_LENGTH_ADDR);
		}
		else {
			builder.InsertEmptyColumn(STATUS_COLUMN_LENGTH_TIME);
			builder.InsertEmptyColumn(STATUS_COLUMN_LENGTH_PING);
			builder.InsertEmptyColumn(STATUS_COLUMN_LENGTH_LOSS);
			builder.AddColumnText(state, STATUS_COLUMN_LENGTH_STATE);
		}

		print($"{builder.GetLine()}\n");
	}

	public void Client_Print(ReadOnlySpan<char> text) {
		Client?.ClientPrintf(text);
	}

	public void BuildConVarUpdateMessage(NET_SetConVar convars, FCvar flags, bool nonDefault) {
		int count = CountVariablesWithFlags(flags, nonDefault);
		if (count <= 0)
			return;

#if GMOD_DLL
		const int MAX_CONVARS = 4096;
#else
		const int MAX_CONVARS = 255;
#endif
		if (count > MAX_CONVARS) {
			Sys.Error($"Engine only supported {MAX_CONVARS} ConVars marked {flags}\n");
		}

		foreach (var var in Cvar.GetCommands()) {
			if (var.IsCommand())
				continue;

			ConVar convar = (ConVar)var;
			if (!convar.IsFlagSet(flags))
				continue;

			// It's == to the default value, don't count
			if (nonDefault && stricmp(convar.GetDefault(), convar.GetString()) == 0)
				continue;

			cvar_s acvar = new();
			acvar.Name = convar.GetName();
			acvar.Value = CleanupConVarStringValue(convar.GetString());
			convars.ConVars.Add(acvar);
		}
	}

#if GMOD_DLL
	public void GMOD_BuildConVarUpdateMessage(INetChannel chan, FCvar flags, bool nonDefault) {
		int count = CountVariablesWithFlags(flags, nonDefault);
		if (count <= 0)
			return;

		NET_SetConVar convars = new();
		count = 0;

		foreach (var var in Cvar.GetCommands()) {
			if (count > 32) {
				chan.SendNetMsg(convars);
				convars.ConVars.Clear();
				count = 0;
			}

			if (var.IsCommand())
				continue;

			ConVar convar = (ConVar)var;
			if (!convar.IsFlagSet(flags))
				continue;

			if (nonDefault && stricmp(convar.GetDefault(), convar.GetString()) == 0)
				continue;

			cvar_s acvar = new();
			acvar.Name = convar.GetName();
			acvar.Value = CleanupConVarStringValue(convar.GetString());
			convars.ConVars.Add(acvar);
			count++;
		}

		if (count != 0)
			chan.SendNetMsg(convars);
	}

	public void GMOD_BuildConVarUpdateMessageBF(bf_write buf, FCvar flags, bool nonDefault) {
		int count = CountVariablesWithFlags(flags, nonDefault);
		if (count <= 0)
			return;

		NET_SetConVar convars = new();
		count = 0;

		foreach (var var in Cvar.GetCommands()) {
			if (count > 128) {
				convars.WriteToBuffer(buf);
				convars.ConVars.Clear();
				count = 0;
			}

			if (var.IsCommand())
				continue;

			ConVar convar = (ConVar)var;
			if (!convar.IsFlagSet(flags))
				continue;

			if (nonDefault && stricmp(convar.GetDefault(), convar.GetString()) == 0)
				continue;

			cvar_s acvar = new();
			acvar.Name = convar.GetName();
			acvar.Value = CleanupConVarStringValue(convar.GetString());
			convars.ConVars.Add(acvar);
			count++;
		}

		if (count != 0)
			convars.WriteToBuffer(buf);
	}
#endif

	[ConCommand(helpText: "Exits the engine")]
	void quit(in TokenizedCommand args) {
#if !SWDS
		if (!args.FindArg("prompt").IsEmpty) {
			// EngineVGui.ConfirmQuit();
			return;
		}

		// TODO: game events.
		HostState.Shutdown();
#endif
	}

	readonly ConVar voice_recordtofile = new("voice_recordtofile", "0", 0, "Record mic data and decompressed voice data into 'voice_micdata.wav' and 'voice_decompressed.wav'");
	readonly ConVar voice_inputfromfile;
	readonly ConVar sv_allow_voice_from_file;

	private void voiceconvar_file_changed_f(IConVar conVar, in ConVarChangeContext ctx) {
#if !SWDS
		ConVarRef var = new(conVar);
		if (var.GetInt() == 0)
			Host_VoiceRecordStop();
#endif
	}


#if !SWDS
	[ConCommand(name: "+voicerecord")]
	public void Host_VoiceRecordStart() {
		if (cl.IsActive()) {
			ReadOnlySpan<char> uncompressedFile = null;
			ReadOnlySpan<char> decompressedFile = null;
			ReadOnlySpan<char> inputFile = null;

			if (voice_recordtofile.GetInt() != 0) {
				uncompressedFile = "voice_micdata.wav";
				decompressedFile = "voice_decompressed.wav";
			}

			if (voice_inputfromfile.GetInt() != 0)
				inputFile = "voice_input.wav";

			if (!sv_allow_voice_from_file.GetBool())
				inputFile = null;

#if !NO_VOICE
			if (Voice.Record_Start(uncompressedFile, decompressedFile, inputFile))
				Msg("Started voice recording...\n");
#endif
		}
	}

	[ConCommand(name: "-voicerecord")]
	public void Host_VoiceRecordStop() {
		if (cl.IsActive()) {
#if !NO_VOICE
			if (Voice.IsRecording()) {
				CL.SendVoicePacket(Voice.UsingSteamVoice ? false : true);
				Voice.UserDesiresStop();

				Msg("Stopped voice recording...\n");
			}
#endif
		}
	}
#endif

	public string CleanupConVarStringValue(string v) {
		// todo.
		return v;
	}

	public int CountVariablesWithFlags(FCvar flags, bool nonDefault) {
		int count = 0;
		foreach (var var in Cvar.GetCommands()) {
			if (var.IsCommand())
				continue;

			ConVar convar = (ConVar)var;
			if (!convar.IsFlagSet(flags))
				continue;

			// It's == to the default value, don't count
			if (nonDefault && stricmp(convar.GetDefault(), convar.GetString()) == 0)
				continue;

			count++;
		}

		return count;
	}

	internal void AllowQueuedMaterialSystem(bool v) {
		// todo
	}

	[ConCommand(helpText: "Reload the most recent saved game (add setpos to jump to current view position on reload).")]
	void reload(in TokenizedCommand args, CommandSource source, int clientSlot = -1) {
		if (
#if !SWDS
#endif
			!sv.IsActive())
			return;

		if (sv.IsMultiplayer())
			return;

		if (source != CommandSource.Command)
			return;

		bool rememberLocation = args.ArgC() == 2 && args[1].Equals("setpos", StringComparison.OrdinalIgnoreCase);
#if !SWDS
		Scr.BeginLoadingPlaque();
#endif
		Disconnect(false);
		Dbg.Msg("reload incomplete!\n");
	}

	public void ShutdownServer() {
		if (!sv.IsActive())
			return;

		AllowQueuedMaterialSystem(false);
#if !SWDS
		g_ShadowMgr.LevelShutdown();
#endif
		StaticPropMgr().LevelShutdown();

		FreeStateAndWorld(true);
		sv.Shutdown();
		FreeToLowMark(true);

		IGameEvent? ev = g_GameEventManager.CreateEvent("server_shutdown");

		if (ev != null) {
			ev.SetString("reason", "restart");
			g_GameEventManager.FireEvent(ev);
		}


		GC.WaitForPendingFinalizers();
		GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive);
	}

	[ConCommand]
	void dumpstringtables() {
		SV.DumpStringTables();
#if !SWDS
		CL.DumpStringTables();
#endif
	}

	bool inerror;

	[DoesNotReturn]
	public void Error(ReadOnlySpan<char> error) {
		if (inerror)
			Sys.Error("Host_Error: recursively entered");
		inerror = true;

		if (sv.IsDedicated()) {
			Sys.Error($"Host_Error: {error}\n");
			return;
		}
#if !SWDS
		Scr.EndLoadingPlaque();
#endif
		ConMsg($"\nHost_Error: {error}\n\n");
		Disconnect(true, error);
		inerror = false;

		// Unwind back to the host-state loop (IHostState.Frame), mirroring the
		// reference engine's longjmp(host_abortserver). This stops the caller
		// from continuing to parse/execute on the now-disconnected state.
		throw new HostErrorException(new string(error));
	}

	static readonly ConVar singlestep = new("singlestep", "0", FCvar.Cheat, "Run engine in single step mode ( set next to 1 to advance a frame )");
	static readonly ConVar cvarNext = new("next", "0", FCvar.Cheat, "Set to 1 to advance to next frame ( when singlestep == 1 )");

	int ShouldRun_CurrentTick;
	internal int SpawnCount;

	public bool ShouldRun() {
		if (singlestep.GetInt() == 0)
			return true;

		if (cvarNext.GetInt() != 0) {
			if (ShouldRun_CurrentTick != (TickCount - 1)) {
				cvarNext.SetValue(0);
				return false;
			}

			return true;
		}
		else {
			ShouldRun_CurrentTick = TickCount;
			return false;
		}
	}

	public void EndGame(bool showMainMenu, ReadOnlySpan<char> message) {
		ConMsg($"Host_EndGame: {message}");
		Disconnect(showMainMenu);
		if (sv.IsDedicated()) {
			Sys.Error($"Host_EndGame: {message}\n");
			return;
		}
	}

	public readonly ConVar skill = new("skill", "1", FCvar.Archive, "Game skill level (1-3).", 1, 3);
	public readonly ConVar deathmatch = new("deathmatch", "0", FCvar.Notify | FCvar.InternalUse, "Running a deathmatch server.");
	public readonly ConVar coop = new("coop", "0", FCvar.Notify, "Cooperative play.");
	private readonly EngineParms host_parms;
	private readonly CommonHostState host_state;
	private readonly IServiceProvider services;
	private readonly ICommandLine commandLine;
	private readonly IFileSystem fileSystem;

	public Host(
		EngineParms host_parms, CommonHostState host_state,
		IServiceProvider services, ICommandLine CommandLine, IFileSystem fileSystem
	) {
		this.host_parms = host_parms;
		this.host_state = host_state;
		this.services = services;
		commandLine = CommandLine;
		this.fileSystem = fileSystem;

		voice_inputfromfile = new("voice_inputfromfile", "0", 0, "Get voice input from 'voice_input.wav' rather than from the microphone.", callback: voiceconvar_file_changed_f);
		sv_allow_voice_from_file = new("sv_allow_voice_from_file", "1", FCvar.Replicated, "Allow or disallow clients from using voice_inputfromfile on this server.", callback: voiceconvar_file_changed_f);
	}

	internal bool ValidGame() {
		if (sv.IsMultiplayer()) {
			if (deathmatch.GetInt() != 0)
				return true;
		}
		else
			return true;

		ConDMsg("Unable to launch game\n");
		return false;
	}

	internal void FreeStateAndWorld(bool server) {
		bool needsPurge = false;

		Assert(Initialized);
		Assert(HunkLevel != 0);

		if (!server && sv.IsActive())
			return;

#if !SWDS
		if (server && !sv.IsDedicated())
			CL.ClearState();
#endif

		if (host_state.WorldModel != null) {
			modelloader.UnreferenceModel(host_state.WorldModel, ModelLoaderFlags.Server);
			modelloader.UnreferenceModel(host_state.WorldModel, ModelLoaderFlags.Client);
			host_state.SetWorldModel(null);
			needsPurge = server && true;
		}

		if (server) {
			modelInfo.OnLevelChange();
		}
#if !SWDS
		else {
			// modelinfoclient.OnLevelChange();
		}
#endif

		modelloader.UnloadUnreferencedModels();

		// TimeLastMemTest = 0;
	}

	internal void FreeToLowMark(bool server) {
		Assert(Initialized);
		Assert(HunkLevel != 0);

		if (!server && sv.IsActive())
			return;

		CM.FreeMap();

		// if (HunkLevel != 0) {
		// 	Hunk_FreeToLowMark(HunkLevel);
		// }
	}

	internal static TimeUnit_t GetSoundDuration(ReadOnlySpan<char> sample) {
#if !SWDS
		if (!sv.IsDedicated()) {
			int index = cl.LookupSoundIndex(sample);
			if (index >= 0)
				return g_AudioSystem.GetSoundDuration(cl.GetSound(index));
			return g_AudioSystem.GetSoundDuration(SoundCharsUtils.SkipSoundChars(sample));
		}
#endif
		return 0.0f;
	}

	ref struct LocalMapAccessScope : IDisposable
	{
		public LocalMapAccessScope() => Enabled = false;
		public void Dispose() { if (Enabled) g_pFileSystem.EndMapAccess(); }
		public bool Enabled;
	};

	internal bool Changelevel(bool loadfromsavedgame, ReadOnlySpan<char> mapname, ReadOnlySpan<char> start) {
		Span<char> _startspot = stackalloc char[MAX_PATH];
		Span<char> startspot;
		Span<char> oldlevel = stackalloc char[MAX_PATH];
#if !SWDS
		// SaveRestoreData? saveData = null;
#endif
		bool bTransitionBySave = false;

		if (!sv.IsActive()) {
			ConMsg("Only the server may changelevel\n");
			return false;
		}

#if SWDS
		// todo: demo?
#endif

#if !SWDS
		Scr.BeginLoadingPlaque();

		// stop sounds (especially looping!)
		Sound.StopAllSounds(true);
#endif

		// Prepare new level
		sv.InactivateClients();

		// The qualified name of the map, excluding path/extension
		Span<char> szMapName = stackalloc char[MAX_PATH];
		// The file to load the map from.
		Span<char> szMapFile = stackalloc char[MAX_PATH];
		strcpy(szMapName, mapname);
		Host.DefaultMapFileName(szMapName, szMapFile);

		// Ask serverDLL to prepare this load
#if !BUILD_GMOD
		serverGameDLL.PrepareLevelResources(szMapName, szMapFile);
#endif

		if (!modelloader.Map_IsValid(szMapFile)) {
#if !SWDS
			Scr.EndLoadingPlaque();
#endif
			// We have already inactivated clients at this point due to PrepareLevelResources being blocking, false alarm,
			// tell them to reconnect (which doesn't mean full reconnect, just start rejoining the map)
			//
			// In the likely case that the game DLL tries another map this is harmless, they'll wait on the game server in
			// the connect process if its in another level change by time they get there.
			sv.ReconnectClients();
			return false;
		}

		// If changing from the same map to the same map, optimize by not closing and reopening
		// the packfile which is embedded in the .bsp; we do this by incrementing the packfile's
		// refcount via BeginMapAccess()/EndMapAccess() through the base filesystem API.

		LocalMapAccessScope mapscope = new();
		if (strcmp(sv.GetMapName(), szMapName) == 0) {
			g_pFileSystem.BeginMapAccess();
			mapscope.Enabled = true;
		}

		// g_pFileSystem.AsyncFinishAll();

		if (start.IsEmpty)
			startspot = null;
		else {
			strcpy(_startspot, start);
			unsafe {
				startspot = _startspot;
			}
		}

		Warning("---- Host_Changelevel ----\n");

#if !SWDS
		// Add on time passed since the last time we kept track till this transition
		// int iAdditionalSeconds = g_ServerGlobalVariables.curtime - saverestore->GetMostRecentElapsedTimeSet();
		// int iElapsedSeconds = saverestore->GetMostRecentElapsedSeconds() + iAdditionalSeconds;
		// int iElapsedMinutes = saverestore->GetMostRecentElapsedMinutes() + (iElapsedSeconds / 60);
		// saverestore->SetMostRecentElapsedMinutes(iElapsedMinutes);
		// saverestore->SetMostRecentElapsedSeconds((iElapsedSeconds % 60));

		if (bTransitionBySave) {
			// char comment[80];
			// Pass in the total elapsed time so it gets added to the elapsed time for this map.
			// serverGameDLL.GetSaveComment(
			// 	comment,
			// 	sizeof(comment),
			// 	saverestore->GetMostRecentElapsedMinutes(),
			// 	saverestore->GetMostRecentElapsedSeconds());
			// 
			// if (!saverestore->SaveGameSlot("_transition", comment, false, true, szMapName, startspot)) {
			// 	Warning("Failed to save data for transition\n");
			// 	SCR_EndLoadingPlaque();
			// 	return false;
			// }

			// Not going to load a save after the transition, so add this map's elapsed time to the total elapsed time
			// int totalSeconds = serverGlobalVariables.CurTime + saverestore->GetMostRecentElapsedSeconds();
			// todo: saverestore->SetMostRecentElapsedMinutes((int)(totalSeconds / 60.0f) + saverestore->GetMostRecentElapsedMinutes());
			// todo: saverestore->SetMostRecentElapsedSeconds((int)fmod(totalSeconds, 60.0f));
		}
#endif

		strcpy(oldlevel, sv.GetMapName());

#if !SWDS
		if (loadfromsavedgame) {
			if (!bTransitionBySave) {
				// save the current level's state
				// todo saverestore->SaveGameState(true, &pSaveData);

				// todo if (!pSaveData) {
				// todo 	Warning("Failed to save data for transition\n");
				// todo 	Scr.EndLoadingPlaque();
				// todo 	mapscope.Dispose();
				// todo 	return false;
				// todo }
			}

			// ensure resources in the transition volume stay
			// todo AddTransitionResources(pSaveData, szMapName, startspot);
		}
#endif
		serverPluginHandler.LevelShutdown();

#if !SWDS
		// todo audiosourcecache.LevelShutdown();
#endif

#if !SWDS
		// todo saverestore.FinishAsyncSave();
#endif

		if (sv.RestartOnLevelChange()) {
			Cbuf.Clear();
			Cbuf.AddText("quit\n");
			mapscope.Dispose();
			return false;
		}

		g_DownloadListGenerator.OnLevelLoadStart(szMapName);

		if (!sv.SpawnServer(szMapName, szMapFile, startspot)) {
#if !SWDS
			Scr.EndLoadingPlaque();
#endif
			mapscope.Dispose();
			return false;
		}

#if !SWDS
		if (loadfromsavedgame) {
			if (!bTransitionBySave) {
				// Finish saving gamestate
				// todo saverestore->Finish(pSaveData);
			}

			serverGlobalVariables.CurTime = sv.GetTime();

			// todo audiosourcecache.LevelInit(szMapName);
			serverPluginHandler.LevelInit(szMapName, CM.EntityString(), oldlevel, startspot, true, false);

			sv.SetPaused(true); // pause until client connects
			sv.LoadGame = true;
		}
		else
#endif
		{
			serverGlobalVariables.CurTime = sv.GetTime();
#if !SWDS
			// todo audiosourcecache.LevelInit(szMapName);
#endif
			serverPluginHandler.LevelInit(szMapName, CM.EntityString(), null, null, false, false);
		}

		SV.ActivateServer();

#if !SWDS
		// Offset stored elapsed time by the current elapsed time for this new map
		// int maptime = sv.GetTime();
		// int minutes = (int)(maptime / 60.0f);
		// int seconds = (int)fmod(maptime, 60.0f);
		// todo: saverestore->SetMostRecentElapsedMinutes(saverestore->GetMostRecentElapsedMinutes() - minutes);
		// todo: saverestore->SetMostRecentElapsedSeconds(saverestore->GetMostRecentElapsedSeconds() - seconds);
#endif

		// NotifyDedicatedServerUI("UpdateMap");

		g_DownloadListGenerator.OnLevelLoadEnd();

		mapscope.Dispose();
		return true;
	}
}
