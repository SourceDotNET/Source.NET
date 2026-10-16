using System.Runtime.InteropServices;

using Xilium.CefGlue;

namespace HTML.CEF.Process;

static partial class Program
{
	const int MainThreadStackSize = 8 * 1024 * 1024;
	const int PrSetPdeathsig = 1;
	const int SigKill = 9;

	[LibraryImport("libc", EntryPoint = "prctl")]
	private static partial int Prctl(int option, nuint arg2);

	static int Main() {
		if (OperatingSystem.IsLinux())
			Prctl(PrSetPdeathsig, SigKill);

		if (OperatingSystem.IsWindows())
			CefRuntime.Load(AppContext.BaseDirectory);
		else
			CefRuntime.Load();

		CefMainArgs mainArgs = new(Environment.GetCommandLineArgs());
		SubprocessApp app = new();

		if (!OperatingSystem.IsWindows())
			return CefRuntime.ExecuteProcess(mainArgs, app, 0);

		int exitCode = 0;
		Thread thread = new(() => exitCode = CefRuntime.ExecuteProcess(mainArgs, app, 0), MainThreadStackSize);
		thread.Start();
		thread.Join();
		return exitCode;
	}
}
