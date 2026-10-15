#include <windows.h>
#include <shellapi.h>

#include <cstdio>
#include <string>
#include <vector>

#include "include/cef_app.h"
#include "include/cef_sandbox_win.h"

#include <nethost.h>
#include <hostfxr.h>

#include "App.h"

static const wchar_t* const ManagedAssembly = L"Source.LauncherMain.dll";

static std::wstring GetExeDirectory()
{
	std::wstring path(MAX_PATH, L'\0');
	for (;;) {
		DWORD length = GetModuleFileNameW(nullptr, path.data(), static_cast<DWORD>(path.size()));
		if (length == 0)
			return L"";
		if (length < path.size()) {
			path.resize(length);
			break;
		}
		path.resize(path.size() * 2);
	}

	size_t slash = path.find_last_of(L"\\/");
	return slash == std::wstring::npos ? L"" : path.substr(0, slash + 1);
}

static bool IsSubprocess()
{
	return wcsstr(GetCommandLineW(), L"--type=") != nullptr;
}

static void Fail(const wchar_t* message)
{
	fwprintf(stderr, L"%s\n", message);
	MessageBoxW(nullptr, message, L"Source.NET", MB_OK | MB_ICONERROR);
}

static int RunSubprocess(void* sandboxInfo)
{
	CefMainArgs mainArgs(GetModuleHandleW(nullptr));
	CefRefPtr<SourceApp> app(new SourceApp);
	return CefExecuteProcess(mainArgs, app, sandboxInfo);
}

static int RunManaged(void* sandboxInfo)
{
	std::wstring exeDir = GetExeDirectory();
	std::wstring assemblyPath = exeDir + ManagedAssembly;

	get_hostfxr_parameters params{ sizeof(get_hostfxr_parameters), assemblyPath.c_str(), nullptr };
	wchar_t hostfxrPath[MAX_PATH];
	size_t hostfxrPathSize = MAX_PATH;
	if (get_hostfxr_path(hostfxrPath, &hostfxrPathSize, &params) != 0) {
		Fail(L"Could not locate the .NET runtime (hostfxr). Is the .NET 10 runtime installed?");
		return 1;
	}

	HMODULE hostfxr = LoadLibraryW(hostfxrPath);
	if (!hostfxr) {
		Fail(L"Could not load hostfxr.");
		return 1;
	}

	auto initialize = reinterpret_cast<hostfxr_initialize_for_dotnet_command_line_fn>(GetProcAddress(hostfxr, "hostfxr_initialize_for_dotnet_command_line"));
	auto setProperty = reinterpret_cast<hostfxr_set_runtime_property_value_fn>(GetProcAddress(hostfxr, "hostfxr_set_runtime_property_value"));
	auto runApp = reinterpret_cast<hostfxr_run_app_fn>(GetProcAddress(hostfxr, "hostfxr_run_app"));
	auto close = reinterpret_cast<hostfxr_close_fn>(GetProcAddress(hostfxr, "hostfxr_close"));
	if (!initialize || !setProperty || !runApp || !close) {
		Fail(L"hostfxr is missing required exports.");
		return 1;
	}

	int argc = 0;
	wchar_t** argv = CommandLineToArgvW(GetCommandLineW(), &argc);
	std::vector<const wchar_t*> managedArgv;
	managedArgv.push_back(assemblyPath.c_str());
	for (int i = 1; i < argc; i++)
		managedArgv.push_back(argv[i]);

	hostfxr_handle handle = nullptr;
	int rc = initialize(static_cast<int>(managedArgv.size()), managedArgv.data(), nullptr, &handle);
	if (rc != 0 || !handle) {
		LocalFree(argv);
		wchar_t message[256];
		swprintf(message, 256, L"Failed to initialize the .NET runtime for %s (0x%08X).", ManagedAssembly, static_cast<unsigned int>(rc));
		Fail(message);
		if (handle)
			close(handle);
		return 1;
	}

	wchar_t sandboxHex[32];
	swprintf(sandboxHex, 32, L"%llX", static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(sandboxInfo)));
	setProperty(handle, L"SN_CEF_SANDBOX_INFO", sandboxHex);

	rc = runApp(handle);
	close(handle);
	LocalFree(argv);
	return rc;
}

static int Run(void* sandboxInfo)
{
	if (IsSubprocess())
		return RunSubprocess(sandboxInfo);
	return RunManaged(sandboxInfo);
}

CEF_BOOTSTRAP_EXPORT int RunWinMain(HINSTANCE hInstance, LPWSTR lpCmdLine, int nCmdShow, void* sandbox_info, cef_version_info_t* version_info)
{
	return Run(sandbox_info);
}

CEF_BOOTSTRAP_EXPORT int RunConsoleMain(int argc, char* argv[], void* sandbox_info, cef_version_info_t* version_info)
{
	return Run(sandbox_info);
}
