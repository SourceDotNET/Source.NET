using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;

namespace ChromiumProcess;

public static class HtmlBridge
{
	public const string SecretVariable = "SOURCENET_HTML_BRIDGE";
	const int RegistryCapacity = 4 * 1024 * 1024;

	public static string ApiName(string secret) => $"__SN{secret}";
	public static string BridgeName(string secret) => $"__SN{secret}_b";
	static string RegistryName(string secret) => $"Local\\SourceNET_HTML_{secret}";
	static string RegistryLockName(string secret) => $"Local\\SourceNET_HTML_{secret}_lock";

	public static string Quote(string value) => JsonSerializer.Serialize(value);

	public static string BootstrapScript(string secret, IReadOnlyList<(string Obj, string Func)> registered, long frameId, string nonce) {
		StringBuilder list = new("[");
		for (int i = 0; i < registered.Count; i++) {
			if (i > 0)
				list.Append(',');
			list.Append('[').Append(Quote(registered[i].Obj)).Append(',').Append(Quote(registered[i].Func)).Append(']');
		}
		list.Append(']');

		return $"({Bootstrap})(this,{Quote(ApiName(secret))},{Quote(BridgeName(secret))},{list},{Quote(frameId.ToString())},{Quote(nonce)});";
	}

	public static string RegisterScript(string secret, string objName, string funcName)
		=> $"(function(b){{if(b)b.r({Quote(objName)},{Quote(funcName)});}})(window[{Quote(BridgeName(secret))}]);";

	public static string CallbackScript(string secret, int callbackId, string nonce, string encodedArgs)
		=> $"(function(b){{if(b)b.c({callbackId},{Quote(nonce)},{encodedArgs});}})(window[{Quote(BridgeName(secret))}]);";

	const string Bootstrap = """
	function (G, apiName, bridgeName, registered, frameId, nonce) {
		var defineProperty = Object.defineProperty;
		var keys = Object.keys;
		var isArray = Array.isArray;
		var apply = Reflect.apply;
		var stringify = JSON.stringify;
		var toNumber = Number;
		var toString = String;
		var callbacks = Object.create(null);
		var nextCallbackId = 0;
		var FAIL = {};

		function serialize(value, depth) {
			if (depth > 16)
				throw FAIL;
			if (value === null || value === undefined)
				return 0;
			var type = typeof value;
			if (type === 'boolean')
				return [1, value];
			if (type === 'number')
				return [2, (value === value && value !== Infinity && value !== -Infinity) ? value : toString(value)];
			if (type === 'string')
				return [3, value];
			if (isArray(value)) {
				var length = value.length, list = [];
				for (var i = 0; i < length; i++)
					list[i] = serialize(value[i], depth + 1);
				return [4, list];
			}
			if (type === 'object' || type === 'function') {
				var names = keys(value), map = [];
				for (var j = 0; j < names.length; j++) {
					var entry;
					try {
						entry = value[names[j]];
					}
					catch (e) {
						entry = null;
					}
					map[j] = [names[j], serialize(entry, depth + 1)];
				}
				return [5, map];
			}
			return 0;
		}

		function deserialize(value, depth) {
			if (depth > 16)
				throw FAIL;
			if (value === 0)
				return undefined;
			switch (value[0]) {
				case 1:
				case 3:
					return value[1];
				case 2:
					return typeof value[1] === 'string' ? toNumber(value[1]) : value[1];
				case 4: {
					var list = [];
					for (var i = 0; i < value[1].length; i++)
						list[i] = deserialize(value[1][i], depth + 1);
					return list;
				}
				case 5: {
					var object = {};
					for (var j = 0; j < value[1].length; j++)
						defineProperty(object, value[1][j][0], { value: deserialize(value[1][j][1], depth + 1), writable: true, enumerable: true, configurable: true });
					return object;
				}
			}
			return undefined;
		}

		function createCallback(func) {
			if (callbacks[nextCallbackId] !== undefined)
				return -1;
			var callbackId = nextCallbackId;
			callbacks[callbackId] = func;
			nextCallbackId++;
			if (nextCallbackId >= 16384)
				nextCallbackId = 0;
			return callbackId;
		}

		function execute(objName, funcName, args) {
			var count = args.length, list = [];
			try {
				for (var i = 0; i < count; i++)
					list[i] = serialize(args[i], 0);
			}
			catch (e) {
				if (e === FAIL)
					throw new Error('failed to serialize parameters (did you return a deeply nested structure?)');
				throw e;
			}

			var callbackId = -1;
			if (count > 0 && typeof args[count - 1] === 'function') {
				callbackId = createCallback(args[count - 1]);
				list.length = count - 1;
			}

			var api = G[apiName];
			if (api && typeof api.PostMessage === 'function')
				api.PostMessage(stringify(['ExecuteFunction', objName, funcName, callbackId, list, frameId, nonce]));
		}

		function registerFunction(objName, funcName) {
			try {
				var obj = G[objName];
				if (!obj || (typeof obj !== 'object' && typeof obj !== 'function')) {
					obj = {};
					defineProperty(G, objName, { value: obj, writable: true, enumerable: true, configurable: true });
				}
				var func = function () { return execute(objName, funcName, arguments); };
				defineProperty(func, 'name', { value: objName + '#' + funcName });
				defineProperty(obj, funcName, { value: func, writable: true, enumerable: true, configurable: true });
			}
			catch (e) { }
		}

		function executeCallback(callbackId, callbackNonce, encodedArgs) {
			if (callbackNonce !== nonce)
				return;
			var func = callbacks[callbackId];
			if (func === undefined)
				return;
			delete callbacks[callbackId];

			var args = [];
			try {
				for (var i = 0; i < encodedArgs.length; i++)
					args[i] = deserialize(encodedArgs[i], 0);
			}
			catch (e) {
				return;
			}
			apply(func, G, args);
		}

		var bridge = Object.create(null);
		bridge.c = executeCallback;
		bridge.r = registerFunction;
		try {
			defineProperty(G, bridgeName, { value: bridge });
		}
		catch (e) { }

		for (var k = 0; k < registered.length; k++)
			registerFunction(registered[k][0], registered[k][1]);
	}
	""";

	public sealed class RegistryWriter : IDisposable
	{
		readonly MemoryMappedFile File;
		readonly MemoryMappedViewAccessor View;
		readonly Mutex Lock;
		readonly Dictionary<int, List<(string Obj, string Func)>> Registrations = [];

		public RegistryWriter(string secret) {
			File = MemoryMappedFile.CreateNew(RegistryName(secret), RegistryCapacity);
			View = File.CreateViewAccessor();
			Lock = new Mutex(false, RegistryLockName(secret));
			Flush();
		}

		public void Add(int browserId, string objName, string funcName) {
			lock (Registrations) {
				if (!Registrations.TryGetValue(browserId, out List<(string, string)>? list))
					Registrations[browserId] = list = [];
				list.Add((objName, funcName));
				Flush();
			}
		}

		public void Remove(int browserId) {
			lock (Registrations) {
				if (Registrations.Remove(browserId))
					Flush();
			}
		}

		void Flush() {
			using MemoryStream stream = new();
			using (Utf8JsonWriter writer = new(stream)) {
				writer.WriteStartObject();
				foreach (KeyValuePair<int, List<(string Obj, string Func)>> pair in Registrations) {
					writer.WriteStartArray(pair.Key.ToString());
					foreach ((string obj, string func) in pair.Value) {
						writer.WriteStartArray();
						writer.WriteStringValue(obj);
						writer.WriteStringValue(func);
						writer.WriteEndArray();
					}
					writer.WriteEndArray();
				}
				writer.WriteEndObject();
			}

			ReadOnlySpan<byte> data = stream.GetBuffer().AsSpan(0, (int)stream.Length);
			if (data.Length + sizeof(int) > RegistryCapacity)
				return;

			try {
				Lock.WaitOne();
			}
			catch (AbandonedMutexException) { }
			try {
				View.WriteArray(sizeof(int), stream.GetBuffer(), 0, data.Length);
				View.Write(0, data.Length);
			}
			finally {
				Lock.ReleaseMutex();
			}
		}

		public void Dispose() {
			View.Dispose();
			File.Dispose();
			Lock.Dispose();
		}
	}

	public sealed class RegistryReader(string secret)
	{
		MemoryMappedFile? File;
		MemoryMappedViewAccessor? View;
		Mutex? Lock;

		public IReadOnlyList<(string Obj, string Func)> Read(int browserId) {
			try {
				File ??= MemoryMappedFile.OpenExisting(RegistryName(secret), MemoryMappedFileRights.Read);
				View ??= File.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
				Lock ??= Mutex.OpenExisting(RegistryLockName(secret));
			}
			catch (Exception) {
				return [];
			}

			bool acquired;
			try {
				acquired = Lock.WaitOne(1000);
			}
			catch (AbandonedMutexException) {
				acquired = true;
			}
			if (!acquired)
				return [];

			byte[] data;
			try {
				int length = View.ReadInt32(0);
				if (length <= 0 || length + sizeof(int) > RegistryCapacity)
					return [];
				data = new byte[length];
				View.ReadArray(sizeof(int), data, 0, length);
			}
			finally {
				Lock.ReleaseMutex();
			}

			List<(string, string)> result = [];
			using JsonDocument document = JsonDocument.Parse(data);
			if (!document.RootElement.TryGetProperty(browserId.ToString(), out JsonElement list))
				return result;
			foreach (JsonElement entry in list.EnumerateArray())
				result.Add((entry[0].GetString() ?? "", entry[1].GetString() ?? ""));
			return result;
		}
	}
}
