#include <sys/prctl.h>
#include <csignal>

#include "include/cef_app.h"

#include "App.h"

int main(int argc, char* argv[])
{
	prctl(PR_SET_PDEATHSIG, SIGKILL);

	CefMainArgs mainArgs(argc, argv);
	CefRefPtr<SourceApp> app(new SourceApp);
	return CefExecuteProcess(mainArgs, app, nullptr);
}
