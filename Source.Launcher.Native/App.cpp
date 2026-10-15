#include "App.h"

#include "include/cef_process_message.h"
#include "include/cef_values.h"

static bool V8ValueToCefValue(CefRefPtr<CefValue> outValue, const CefRefPtr<CefV8Value>& inValue, int depth = 0)
{
	if (depth > 16)
		return false;

	if (!inValue || inValue->IsNull() || inValue->IsUndefined()) {
		outValue->SetNull();
	}
	else if (inValue->IsBool()) {
		outValue->SetBool(inValue->GetBoolValue());
	}
	else if (inValue->IsInt() || inValue->IsUInt() || inValue->IsDouble()) {
		outValue->SetDouble(inValue->GetDoubleValue());
	}
	else if (inValue->IsString()) {
		outValue->SetString(inValue->GetStringValue());
	}
	else if (inValue->IsArray()) {
		int arrayLen = inValue->GetArrayLength();
		CefRefPtr<CefListValue> newList = CefListValue::Create();
		newList->SetSize(arrayLen);

		for (int i = 0; i < arrayLen; i++) {
			CefRefPtr<CefValue> newValue = CefValue::Create();
			if (!V8ValueToCefValue(newValue, inValue->GetValue(i), depth + 1))
				return false;
			newList->SetValue(i, newValue);
		}

		outValue->SetList(newList);
	}
	else if (inValue->IsObject()) {
		CefRefPtr<CefDictionaryValue> newMap = CefDictionaryValue::Create();
		std::vector<CefString> keys;
		inValue->GetKeys(keys);

		for (const CefString& key : keys) {
			CefRefPtr<CefValue> newValue = CefValue::Create();
			if (!V8ValueToCefValue(newValue, inValue->GetValue(key), depth + 1))
				return false;
			newMap->SetValue(key, newValue);
		}

		outValue->SetDictionary(newMap);
	}
	else {
		outValue->SetNull();
	}

	return true;
}

static bool V8ValuesToCefList(CefRefPtr<CefListValue>& outList, const CefV8ValueList& inList)
{
	outList->SetSize(inList.size());

	size_t index = 0;
	for (const CefRefPtr<CefV8Value>& x : inList) {
		CefRefPtr<CefValue> newValue = CefValue::Create();
		if (!V8ValueToCefValue(newValue, x))
			return false;
		outList->SetValue(index, newValue);
		index++;
	}

	return true;
}

static bool CefValueToV8Value(CefRefPtr<CefV8Value>& outValue, const CefRefPtr<CefValue>& inValue, int depth = 0)
{
	if (depth > 16)
		return false;

	switch (inValue->GetType()) {
		case VTYPE_BOOL:
			outValue = CefV8Value::CreateBool(inValue->GetBool());
			break;
		case VTYPE_INT:
			outValue = CefV8Value::CreateInt(inValue->GetInt());
			break;
		case VTYPE_DOUBLE:
			outValue = CefV8Value::CreateDouble(inValue->GetDouble());
			break;
		case VTYPE_STRING:
			outValue = CefV8Value::CreateString(inValue->GetString());
			break;
		case VTYPE_LIST: {
			CefRefPtr<CefListValue> inList = inValue->GetList();
			outValue = CefV8Value::CreateArray(static_cast<int>(inList->GetSize()));

			for (size_t i = 0; i < inList->GetSize(); i++) {
				CefRefPtr<CefV8Value> entry;
				if (!CefValueToV8Value(entry, inList->GetValue(i), depth + 1))
					return false;
				outValue->SetValue(static_cast<int>(i), entry);
			}
			break;
		}
		case VTYPE_DICTIONARY: {
			CefRefPtr<CefDictionaryValue> inMap = inValue->GetDictionary();
			outValue = CefV8Value::CreateObject(nullptr, nullptr);

			CefDictionaryValue::KeyList keys;
			inMap->GetKeys(keys);

			for (const CefString& key : keys) {
				CefRefPtr<CefV8Value> value;
				if (!CefValueToV8Value(value, inMap->GetValue(key), depth + 1))
					return false;
				outValue->SetValue(key, value, V8_PROPERTY_ATTRIBUTE_NONE);
			}
			break;
		}
		default:
			outValue = CefV8Value::CreateUndefined();
			break;
	}

	return true;
}

static bool CefListToV8Values(CefV8ValueList& outList, const CefRefPtr<CefListValue>& inList)
{
	outList.reserve(inList->GetSize());

	for (size_t i = 0; i < inList->GetSize(); i++) {
		CefRefPtr<CefV8Value> entry;
		if (!CefValueToV8Value(entry, inList->GetValue(i)))
			return false;
		outList.emplace_back(entry);
	}

	return true;
}

void SourceApp::OnRegisterCustomSchemes(CefRawPtr<CefSchemeRegistrar> registrar)
{
	registrar->AddCustomScheme("asset", CEF_SCHEME_OPTION_STANDARD | CEF_SCHEME_OPTION_CSP_BYPASSING);
}

CefRefPtr<CefRenderProcessHandler> SourceApp::GetRenderProcessHandler()
{
	return this;
}

void SourceApp::OnBrowserCreated(CefRefPtr<CefBrowser> browser, CefRefPtr<CefDictionaryValue> extra_info)
{
	FunctionList& functions = m_Functions[browser->GetIdentifier()];

	if (extra_info && extra_info->GetType("functions") == VTYPE_LIST) {
		CefRefPtr<CefListValue> list = extra_info->GetList("functions");
		for (size_t i = 0; i < list->GetSize(); i++) {
			if (list->GetType(i) != VTYPE_LIST)
				continue;

			CefRefPtr<CefListValue> pair = list->GetList(i);
			if (pair->GetSize() == 2 && pair->GetType(0) == VTYPE_STRING && pair->GetType(1) == VTYPE_STRING)
				functions.emplace_back(pair->GetString(0), pair->GetString(1));
		}
	}

	RequestFunctions(browser);
}

void SourceApp::OnBrowserDestroyed(CefRefPtr<CefBrowser> browser)
{
	int browserId = browser->GetIdentifier();
	m_Functions.erase(browserId);
	m_Synced.erase(browserId);

	for (auto it = m_Callbacks.begin(); it != m_Callbacks.end();) {
		if (it->second.BrowserId == browserId)
			it = m_Callbacks.erase(it);
		else
			++it;
	}
}

void SourceApp::RequestFunctions(CefRefPtr<CefBrowser> browser)
{
	int browserId = browser->GetIdentifier();
	if (m_Synced[browserId])
		return;

	CefRefPtr<CefFrame> mainFrame = browser->GetMainFrame();
	if (!mainFrame)
		return;

	mainFrame->SendProcessMessage(PID_BROWSER, CefProcessMessage::Create("SyncFunctions"));
	m_Synced[browserId] = true;
}

void SourceApp::OnContextCreated(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame, CefRefPtr<CefV8Context> context)
{
	context->Enter();
	{
		context->GetGlobal()->DeleteValue("print");
		context->GetGlobal()->DeleteValue("openDatabase");
	}
	context->Exit();

	if (!browser)
		return;

	RequestFunctions(browser);

	auto it = m_Functions.find(browser->GetIdentifier());
	if (it == m_Functions.end())
		return;

	for (const auto& pair : it->second)
		RegisterFunctionInFrame(frame, pair.first, pair.second);
}

bool SourceApp::OnProcessMessageReceived(CefRefPtr<CefBrowser> browser, CefRefPtr<CefFrame> frame, CefProcessId source_process, CefRefPtr<CefProcessMessage> message)
{
	if (source_process != PID_BROWSER)
		return false;

	CefString name = message->GetName();
	CefRefPtr<CefListValue> args = message->GetArgumentList();

	if (name == "RegisterFunction") {
		RegisterFunction(browser, args);
		return true;
	}

	if (name == "ExecuteCallback") {
		ExecuteCallback(args);
		return true;
	}

	if (name == "ForgetCallback") {
		ForgetCallback(args);
		return true;
	}

	return false;
}

bool SourceApp::Execute(const CefString& name, CefRefPtr<CefV8Value> object, const CefV8ValueList& arguments, CefRefPtr<CefV8Value>& retval, CefString& exception)
{
	CefRefPtr<CefV8Context> context = CefV8Context::GetCurrentContext();
	CefRefPtr<CefBrowser> browser = context->GetBrowser();
	CefRefPtr<CefFrame> frame = context->GetFrame();

	if (!browser || !frame) {
		exception = "CefV8Context::GetBrowser == nullptr";
		return true;
	}

	std::string fullName = name.ToString();
	size_t delimPos = fullName.find('#');
	if (delimPos == std::string::npos) {
		exception = "SourceApp::Execute couldn't parse this function's name";
		return true;
	}

	std::string objName = fullName.substr(0, delimPos);
	std::string funcName = fullName.substr(delimPos + 1);

	CefRefPtr<CefListValue> argsList = CefListValue::Create();
	if (!V8ValuesToCefList(argsList, arguments)) {
		exception = "failed to serialize parameters (did you return a deeply nested structure?)";
		return true;
	}

	CefRefPtr<CefProcessMessage> message = CefProcessMessage::Create("ExecuteFunction");
	CefRefPtr<CefListValue> args = message->GetArgumentList();

	args->SetString(0, objName);
	args->SetString(1, funcName);

	if (arguments.empty() || !arguments.back()->IsFunction()) {
		args->SetInt(2, -1);
	}
	else {
		args->SetInt(2, CreateCallback(browser->GetIdentifier(), context, arguments.back()));
		argsList->Remove(argsList->GetSize() - 1);
	}

	args->SetList(3, argsList);

	frame->SendProcessMessage(PID_BROWSER, message);
	return true;
}

int SourceApp::CreateCallback(int browserId, CefRefPtr<CefV8Context> context, CefRefPtr<CefV8Value> func)
{
	if (m_Callbacks.find(m_NextCallbackId) != m_Callbacks.end())
		return -1;

	int callbackId = m_NextCallbackId;
	m_Callbacks.emplace(callbackId, Callback{ browserId, context, func });

	m_NextCallbackId++;
	if (m_NextCallbackId >= 16384)
		m_NextCallbackId = 0;

	return callbackId;
}

void SourceApp::RegisterFunctionInFrame(CefRefPtr<CefFrame> frame, const CefString& objName, const CefString& funcName)
{
	if (!frame)
		return;

	CefRefPtr<CefV8Context> context = frame->GetV8Context();
	if (!context || !context->IsValid())
		return;

	CefString fullName(objName.ToString() + "#" + funcName.ToString());

	context->Enter();
	{
		CefRefPtr<CefV8Value> window = context->GetGlobal();
		CefRefPtr<CefV8Value> obj = window->GetValue(objName);

		if (!obj || !obj->IsObject()) {
			obj = CefV8Value::CreateObject(nullptr, nullptr);
			window->SetValue(objName, obj, V8_PROPERTY_ATTRIBUTE_NONE);
		}

		obj->SetValue(funcName, CefV8Value::CreateFunction(fullName, this), V8_PROPERTY_ATTRIBUTE_NONE);
	}
	context->Exit();
}

void SourceApp::RegisterFunction(CefRefPtr<CefBrowser> browser, CefRefPtr<CefListValue> args)
{
	CefString objName = args->GetString(0);
	CefString funcName = args->GetString(1);

	FunctionList& functions = m_Functions[browser->GetIdentifier()];
	for (const auto& pair : functions) {
		if (pair.first == objName && pair.second == funcName)
			return;
	}
	functions.emplace_back(objName, funcName);

	std::vector<CefString> frameIds;
	browser->GetFrameIdentifiers(frameIds);
	for (const CefString& frameId : frameIds)
		RegisterFunctionInFrame(browser->GetFrameByIdentifier(frameId), objName, funcName);
}

void SourceApp::ExecuteCallback(CefRefPtr<CefListValue> args)
{
	auto it = m_Callbacks.find(args->GetInt(0));
	if (it == m_Callbacks.end())
		return;

	CefRefPtr<CefV8Context> context = it->second.Context;
	CefRefPtr<CefV8Value> func = it->second.Function;
	m_Callbacks.erase(it);

	if (!context->IsValid())
		return;

	CefRefPtr<CefListValue> argList = args->GetList(1);

	context->Enter();
	{
		CefV8ValueList arguments;
		if (CefListToV8Values(arguments, argList))
			func->ExecuteFunction(nullptr, arguments);
	}
	context->Exit();
}

void SourceApp::ForgetCallback(CefRefPtr<CefListValue> args)
{
	m_Callbacks.erase(args->GetInt(0));
}
