using Xilium.CefGlue;

namespace HTML.CEF.Process;

sealed class SubprocessApp : CefApp
{
	readonly RenderProcessHandler RenderHandler = new();

	protected override void OnRegisterCustomSchemes(CefSchemeRegistrar registrar) {
		registrar.AddCustomScheme("asset", CefSchemeOptions.Standard | CefSchemeOptions.CspBypassing);
	}

	protected override CefRenderProcessHandler GetRenderProcessHandler() => RenderHandler;
}
