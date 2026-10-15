using CefSharp;
using CefSharp.RenderProcess;

using System.Security.Cryptography;

namespace ChromiumProcess;

class ChromiumApp : IRenderProcessHandler
{
	readonly string? Secret = Environment.GetEnvironmentVariable(HtmlBridge.SecretVariable);
	HtmlBridge.RegistryReader? Registry;

	public void OnContextCreated(IBrowser browser, IFrame frame, IV8Context context) {
		context.Execute("delete this.print;delete this.openDatabase;", "", 0, out _);

		if (browser == null || string.IsNullOrEmpty(Secret))
			return;

		Registry ??= new HtmlBridge.RegistryReader(Secret);
		string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
		context.Execute(HtmlBridge.BootstrapScript(Secret, Registry.Read(browser.Identifier), frame.Identifier, nonce), "", 0, out _);
	}

	public void OnContextReleased(IBrowser browser, IFrame frame, IV8Context context) { }

	public void OnWebKitInitialized() { }
}
