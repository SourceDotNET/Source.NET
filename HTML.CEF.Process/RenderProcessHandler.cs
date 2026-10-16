using Xilium.CefGlue;

namespace HTML.CEF.Process;

sealed class RenderProcessHandler : CefRenderProcessHandler
{
	readonly record struct Callback(int BrowserId, CefV8Context Context, CefV8Value Function);

	sealed class FunctionHandler(RenderProcessHandler owner) : CefV8Handler
	{
		protected override bool Execute(string name, CefV8Value obj, CefV8Value[] arguments, out CefV8Value returnValue, out string exception) {
			returnValue = null!;
			exception = null!;
			return owner.Execute(name, arguments, ref exception);
		}
	}

	const int MaxDepth = 16;
	const int MaxCallbacks = 16384;

	readonly FunctionHandler Handler;
	readonly Dictionary<int, List<(string ObjName, string FuncName)>> Functions = [];
	readonly Dictionary<int, bool> Synced = [];
	readonly Dictionary<int, Callback> Callbacks = [];
	int NextCallbackId;

	public RenderProcessHandler() {
		Handler = new(this);
	}

	static bool V8ValueToCefValue(CefValue outValue, CefV8Value? inValue, int depth = 0) {
		if (depth > MaxDepth)
			return false;

		if (inValue == null || inValue.IsNull || inValue.IsUndefined) {
			outValue.SetNull();
		}
		else if (inValue.IsBool) {
			outValue.SetBool(inValue.GetBoolValue());
		}
		else if (inValue.IsInt || inValue.IsUInt || inValue.IsDouble) {
			outValue.SetDouble(inValue.GetDoubleValue());
		}
		else if (inValue.IsString) {
			outValue.SetString(inValue.GetStringValue());
		}
		else if (inValue.IsArray) {
			int arrayLen = inValue.GetArrayLength();
			CefListValue newList = CefListValue.Create();
			newList.SetSize(arrayLen);

			for (int i = 0; i < arrayLen; i++) {
				CefValue newValue = CefValue.Create();
				if (!V8ValueToCefValue(newValue, inValue.GetValue(i), depth + 1))
					return false;
				newList.SetValue(i, newValue);
			}

			outValue.SetList(newList);
		}
		else if (inValue.IsObject) {
			CefDictionaryValue newMap = CefDictionaryValue.Create();

			foreach (string key in inValue.GetKeys()) {
				CefValue newValue = CefValue.Create();
				if (!V8ValueToCefValue(newValue, inValue.GetValue(key), depth + 1))
					return false;
				newMap.SetValue(key, newValue);
			}

			outValue.SetDictionary(newMap);
		}
		else {
			outValue.SetNull();
		}

		return true;
	}

	static bool V8ValuesToCefList(CefListValue outList, CefV8Value[] inList) {
		outList.SetSize(inList.Length);

		for (int i = 0; i < inList.Length; i++) {
			CefValue newValue = CefValue.Create();
			if (!V8ValueToCefValue(newValue, inList[i]))
				return false;
			outList.SetValue(i, newValue);
		}

		return true;
	}

	static bool CefValueToV8Value(out CefV8Value outValue, CefValue inValue, int depth = 0) {
		outValue = null!;

		if (depth > MaxDepth)
			return false;

		switch (inValue.GetValueType()) {
			case CefValueType.Bool:
				outValue = CefV8Value.CreateBool(inValue.GetBool());
				break;
			case CefValueType.Int:
				outValue = CefV8Value.CreateInt(inValue.GetInt());
				break;
			case CefValueType.Double:
				outValue = CefV8Value.CreateDouble(inValue.GetDouble());
				break;
			case CefValueType.String:
				outValue = CefV8Value.CreateString(inValue.GetString());
				break;
			case CefValueType.List: {
					CefListValue inList = inValue.GetList();
					outValue = CefV8Value.CreateArray(inList.Count);

					for (int i = 0; i < inList.Count; i++) {
						if (!CefValueToV8Value(out CefV8Value entry, inList.GetValue(i), depth + 1))
							return false;
						outValue.SetValue(i, entry);
					}
					break;
				}
			case CefValueType.Dictionary: {
					CefDictionaryValue inMap = inValue.GetDictionary();
					outValue = CefV8Value.CreateObject();

					foreach (string key in inMap.GetKeys()) {
						if (!CefValueToV8Value(out CefV8Value value, inMap.GetValue(key), depth + 1))
							return false;
						outValue.SetValue(key, value, CefV8PropertyAttribute.None);
					}
					break;
				}
			default:
				outValue = CefV8Value.CreateUndefined();
				break;
		}

		return true;
	}

	static bool CefListToV8Values(out CefV8Value[] outList, ICefListValue inList) {
		outList = new CefV8Value[inList.Count];

		for (int i = 0; i < outList.Length; i++) {
			if (!CefValueToV8Value(out outList[i], inList.GetValue(i)))
				return false;
		}

		return true;
	}

	List<(string ObjName, string FuncName)> GetFunctions(int browserId) {
		if (!Functions.TryGetValue(browserId, out List<(string ObjName, string FuncName)>? functions)) {
			functions = [];
			Functions[browserId] = functions;
		}
		return functions;
	}

	protected override void OnBrowserCreated(CefBrowser browser, CefDictionaryValue? extraInfo) {
		List<(string ObjName, string FuncName)> functions = GetFunctions(browser.Identifier);

		if (extraInfo != null && extraInfo.GetValueType("functions") == CefValueType.List) {
			ICefListValue list = extraInfo.GetList("functions");
			for (int i = 0; i < list.Count; i++) {
				if (list.GetValueType(i) != CefValueType.List)
					continue;

				ICefListValue pair = list.GetList(i);
				if (pair.Count == 2 && pair.GetValueType(0) == CefValueType.String && pair.GetValueType(1) == CefValueType.String)
					functions.Add((pair.GetString(0), pair.GetString(1)));
			}
		}

		RequestFunctions(browser);
	}

	protected override void OnBrowserDestroyed(CefBrowser browser) {
		int browserId = browser.Identifier;
		Functions.Remove(browserId);
		Synced.Remove(browserId);

		foreach (KeyValuePair<int, Callback> pair in Callbacks.ToArray()) {
			if (pair.Value.BrowserId == browserId)
				Callbacks.Remove(pair.Key);
		}
	}

	void RequestFunctions(CefBrowser browser) {
		int browserId = browser.Identifier;
		if (Synced.TryGetValue(browserId, out bool synced) && synced)
			return;

		CefFrame? mainFrame = browser.GetMainFrame();
		if (mainFrame == null)
			return;

		mainFrame.SendProcessMessage(CefProcessId.Browser, CefProcessMessage.Create("SyncFunctions"));
		Synced[browserId] = true;
	}

	protected override void OnContextCreated(CefBrowser browser, CefFrame frame, CefV8Context context) {
		context.Enter();
		{
			context.GetGlobal().DeleteValue("print");
			context.GetGlobal().DeleteValue("openDatabase");
		}
		context.Exit();

		if (browser == null)
			return;

		RequestFunctions(browser);

		if (!Functions.TryGetValue(browser.Identifier, out List<(string ObjName, string FuncName)>? functions))
			return;

		foreach ((string objName, string funcName) in functions)
			RegisterFunctionInFrame(frame, objName, funcName);
	}

	protected override bool OnProcessMessageReceived(CefBrowser browser, CefFrame frame, CefProcessId sourceProcess, CefProcessMessage message) {
		if (sourceProcess != CefProcessId.Browser)
			return false;

		string name = message.Name;
		CefListValue args = message.Arguments!;

		switch (name) {
			case "RegisterFunction":
				RegisterFunction(browser, args);
				return true;
			case "ExecuteCallback":
				ExecuteCallback(args);
				return true;
			case "ForgetCallback":
				ForgetCallback(args);
				return true;
		}

		return false;
	}

	bool Execute(string name, CefV8Value[] arguments, ref string exception) {
		CefV8Context context = CefV8Context.GetCurrentContext();
		CefBrowser? browser = context.GetBrowser();
		CefFrame? frame = context.GetFrame();

		if (browser == null || frame == null) {
			exception = "CefV8Context::GetBrowser == nullptr";
			return true;
		}

		int delimPos = name.IndexOf('#');
		if (delimPos == -1) {
			exception = "SourceApp::Execute couldn't parse this function's name";
			return true;
		}

		string objName = name[..delimPos];
		string funcName = name[(delimPos + 1)..];

		CefListValue argsList = CefListValue.Create();
		if (!V8ValuesToCefList(argsList, arguments)) {
			exception = "failed to serialize parameters (did you return a deeply nested structure?)";
			return true;
		}

		CefProcessMessage message = CefProcessMessage.Create("ExecuteFunction");
		CefListValue args = message.Arguments!;

		args.SetString(0, objName);
		args.SetString(1, funcName);

		if (arguments.Length == 0 || !arguments[^1].IsFunction) {
			args.SetInt(2, -1);
		}
		else {
			args.SetInt(2, CreateCallback(browser.Identifier, context, arguments[^1]));
			argsList.Remove(argsList.Count - 1);
		}

		args.SetList(3, argsList);

		frame.SendProcessMessage(CefProcessId.Browser, message);
		return true;
	}

	int CreateCallback(int browserId, CefV8Context context, CefV8Value func) {
		if (Callbacks.ContainsKey(NextCallbackId))
			return -1;

		CefObjectTracker.Untrack(context);
		CefObjectTracker.Untrack(func);

		int callbackId = NextCallbackId;
		Callbacks.Add(callbackId, new Callback(browserId, context, func));

		NextCallbackId++;
		if (NextCallbackId >= MaxCallbacks)
			NextCallbackId = 0;

		return callbackId;
	}

	void RegisterFunctionInFrame(CefFrame? frame, string objName, string funcName) {
		if (frame == null)
			return;

		CefV8Context? context = frame.V8Context;
		if (context == null || !context.IsValid)
			return;

		string fullName = objName + "#" + funcName;

		context.Enter();
		{
			CefV8Value window = context.GetGlobal();
			CefV8Value? obj = window.GetValue(objName);

			if (obj == null || !obj.IsObject) {
				obj = CefV8Value.CreateObject();
				window.SetValue(objName, obj, CefV8PropertyAttribute.None);
			}

			obj.SetValue(funcName, CefV8Value.CreateFunction(fullName, Handler), CefV8PropertyAttribute.None);
		}
		context.Exit();
	}

	void RegisterFunction(CefBrowser browser, CefListValue args) {
		string objName = args.GetString(0);
		string funcName = args.GetString(1);

		List<(string ObjName, string FuncName)> functions = GetFunctions(browser.Identifier);
		foreach ((string ObjName, string FuncName) pair in functions) {
			if (pair.ObjName == objName && pair.FuncName == funcName)
				return;
		}
		functions.Add((objName, funcName));

		foreach (string frameId in browser.GetFrameIdentifiers())
			RegisterFunctionInFrame(browser.GetFrameByIdentifier(frameId), objName, funcName);
	}

	void ExecuteCallback(CefListValue args) {
		if (!Callbacks.Remove(args.GetInt(0), out Callback callback))
			return;

		if (!callback.Context.IsValid)
			return;

		ICefListValue argList = args.GetList(1);

		callback.Context.Enter();
		{
			if (CefListToV8Values(out CefV8Value[] arguments, argList))
				callback.Function.ExecuteFunction(null!, arguments);
		}
		callback.Context.Exit();
	}

	void ForgetCallback(CefListValue args) {
		Callbacks.Remove(args.GetInt(0));
	}
}
