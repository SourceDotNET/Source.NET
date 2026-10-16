using Xilium.CefGlue;

namespace HTML.CEF;

sealed class CefApplication : CefApp
{
	protected override void OnBeforeCommandLineProcessing(string processType, CefCommandLine commandLine) {
		if (!string.IsNullOrEmpty(processType))
			return;

		if (OperatingSystem.IsLinux())
			commandLine.AppendSwitch("no-zygote");

		commandLine.AppendSwitch("disable-gpu");
		commandLine.AppendSwitch("disable-gpu-compositing");
		commandLine.AppendSwitch("disable-smooth-scrolling");
		if (OperatingSystem.IsWindows())
			commandLine.AppendSwitch("enable-begin-frame-scheduling");
		commandLine.AppendSwitch("enable-system-flash");
		commandLine.AppendSwitch("disable-in-process-stack-traces");

		commandLine.AppendSwitch("disable-features", "TouchpadAndWheelScrollLatching,AsyncWheelEvents");
		commandLine.AppendSwitch("autoplay-policy", "no-user-gesture-required");
		commandLine.AppendSwitch("enable-blink-features", "HTMLImports");
		commandLine.AppendSwitch("disable-site-isolation-trials");
	}

	protected override void OnRegisterCustomSchemes(CefSchemeRegistrar registrar) {
		registrar.AddCustomScheme("asset", CefSchemeOptions.Standard | CefSchemeOptions.CspBypassing);
	}
}
