#pragma once

#include "include/cef_app.h"

#include <unordered_map>
#include <utility>
#include <vector>

class SourceApp
	: public CefApp
	, public CefRenderProcessHandler
	, public CefV8Handler
{
public:
	void OnRegisterCustomSchemes(CefRawPtr<CefSchemeRegistrar> registrar) override;
	CefRefPtr<CefRenderProcessHandler> GetRenderProcessHandler() override;

	void OnBrowserCreated(CefRefPtr<CefBrowser> browser, CefRefPtr<CefDictionaryValue> extra_info) override;
	void OnBrowserDestroyed(CefRefPtr<CefBrowser> browser) override;
	void OnContextCreated(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame, CefRefPtr<CefV8Context> context) override;
	bool OnProcessMessageReceived(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame, CefProcessId source_process, CefRefPtr<CefProcessMessage> message) override;

	bool Execute(const CefString& name, CefRefPtr<CefV8Value> object, const CefV8ValueList& arguments, CefRefPtr<CefV8Value>& retval, CefString& exception) override;

private:
	using FunctionList = std::vector<std::pair<CefString, CefString>>;

	struct Callback
	{
		int BrowserId;
		CefRefPtr<CefV8Context> Context;
		CefRefPtr<CefV8Value> Function;
	};

	int CreateCallback(int browserId, CefRefPtr<CefV8Context> context, CefRefPtr<CefV8Value> func);
	void RegisterFunctionInFrame(CefRefPtr<CefFrame> frame, const CefString& objName, const CefString& funcName);
	void RequestFunctions(CefRefPtr<CefBrowser> browser);

	void RegisterFunction(CefRefPtr<CefBrowser> browser, CefRefPtr<CefListValue> args);
	void ExecuteCallback(CefRefPtr<CefListValue> args);
	void ForgetCallback(CefRefPtr<CefListValue> args);

	std::unordered_map<int, FunctionList> m_Functions;
	std::unordered_map<int, bool> m_Synced;
	std::unordered_map<int, Callback> m_Callbacks;
	int m_NextCallbackId = 0;

	IMPLEMENT_REFCOUNTING(SourceApp);
};
