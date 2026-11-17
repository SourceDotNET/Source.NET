using Microsoft.Extensions.DependencyInjection;

using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;

using System.Reflection;

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Source.Engine;

public class DedicatedServerAPI(Cbuf Cbuf, IGame game, IServiceProvider services, Common COM, Sys Sys, EngineParms host_parms, SV SV) : IDedicatedServerAPI
{
	public List<MemberInfo> __INTERNAL_FilledDependencies { get; set; } = [];
	public object? GetService(Type serviceType) => services.GetService(serviceType);
	public object? GetKeyedService(Type serviceType, object? key) => ((IKeyedServiceProvider)services).GetKeyedService(serviceType, key);
	public object GetRequiredKeyedService(Type serviceType, object? key) => ((IKeyedServiceProvider)services).GetRequiredKeyedService(serviceType, key);

	static string GetModDirFromPath(string path) {
		int slash = path.LastIndexOf('\\');
		if (slash == -1) slash = path.LastIndexOf('/');
		return slash != -1 ? path.Substring(slash + 1) : path;
	}

	Mod? dedicatedServer = null;
	IEngine eng = null!;
	Host? host;
	Host Host => host ??= services.GetRequiredService<Host>();

	public bool ModInit(in StartupInfo info) {
		ConVar.Register();
		IEngineAPI engineAPI = services.GetRequiredService<IEngineAPI>();
		eng ??= services.GetRequiredService<IEngine>();
		eng.SetQuitting(IEngine.Quit.NotQuitting);

		host_parms.BaseDir = info.BaseDirectory;
		host_parms.Mod = GetModDirFromPath(info.InitialMod);
		host_parms.Game = info.InitialGame;
		COM.InitFilesystem(info.InitialMod);

		Sys.TextMode = info.TextMode;

		materials.ModInit();

		dedicatedServer = new Mod(true, engineAPI);
		dedicatedServer.Main();
		return true;
	}

	public void ModShutdown() {
		if (dedicatedServer != null) {
			dedicatedServer = null;
		}
		eng ??= services.GetRequiredService<IEngine>();

		eng.Unload();
		materials.ModShutdown();
	}

	public bool RunFrame() {
		if (eng.GetQuitting() != IEngine.Quit.NotQuitting)
			return false;

		eng.Frame();
		return true;
	}

	public void AddConsoleText(ReadOnlySpan<char> text) {
		text = text.SliceNullTerminatedString();
		if (text.IsEmpty)
			return;
		Cbuf.InsertText(text);
	}

	public void UpdateStatus(out float fps, out int active, out int maxPlayers, Span<char> map) {
		Host.GetHostInfo(out fps, out active, out maxPlayers, map);
	}

	public void UpdateHostname(Span<char> hostname) {
		if (!hostname.IsEmpty)
			strcpy(hostname, sv.GetName());
	}
}
