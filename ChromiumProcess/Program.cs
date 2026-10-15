using CefSharp.BrowserSubprocess;

namespace ChromiumProcess;

static class Program
{
	static int Main(string[] args) {
		SubProcess.EnableHighDPISupport();
		BrowserSubprocessExecutable executable = new();
		return executable.Main(args, new ChromiumApp());
	}
}
