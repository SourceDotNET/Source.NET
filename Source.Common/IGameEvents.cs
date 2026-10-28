using Source.Common.Bitbuffers;
using Source.Common.Formats.Keyvalues;

using System.Runtime.CompilerServices;

namespace Source.Common;

public static class GameEventConstants
{
	public const int MAX_EVENT_NAME_LENGTH = 32;    // max game event name length
	public const int MAX_EVENT_BITS = 9;// max bits needed for an event index
	public const int MAX_EVENT_NUMBER = (1 << MAX_EVENT_BITS);     // max number of events allowed
	public const int MAX_EVENT_BYTES = 1024;// max size in bytes for a serialized event
}

[InlineArray(MAX_EVENT_NAME_LENGTH)] public struct InlineArrayMaxEventNameLength<T> { T first; }
[InlineArray(MAX_EVENT_BITS)] public struct InlineArrayMaxEventBits<T> { T first; }
[InlineArray(MAX_EVENT_NUMBER)] public struct InlineArrayMaxEventNumber<T> { T first; }
[InlineArray(MAX_EVENT_BYTES)] public struct InlineArrayMaxEventBytes<T> { T first; }

public interface IGameEvent
{
	ReadOnlySpan<char> GetName();
	bool IsReliable();
	bool IsLocal();
	bool IsEmpty(ReadOnlySpan<char> keyName = default);
	bool GetBool(ReadOnlySpan<char> keyName = default, bool defaultValue = false);
	int GetInt(ReadOnlySpan<char> keyName = default, int defaultValue = 0);
	float GetFloat(ReadOnlySpan<char> keyName = default, float defaultValue = 0.0f);
	ReadOnlySpan<char> GetString(ReadOnlySpan<char> keyName = default, ReadOnlySpan<char> defaultValue = default);
	void SetBool(ReadOnlySpan<char> keyName, bool value);
	void SetInt(ReadOnlySpan<char> keyName, int value);
	void SetFloat(ReadOnlySpan<char> keyName, float value);
	void SetString(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value);
#if GMOD_DLL
	KeyValues? GetDataKeys();
#endif
}

public interface IGameEventListener2
{
	void FireGameEvent(IGameEvent ev);
}

public interface IGameEventManager2
{
	int LoadEventsFromFile(ReadOnlySpan<char> filename);
	void Reset();
	bool AddListener(object listener, GameEventDescriptor descriptor, GameEventListenerType listenerType);
	bool AddListener(IGameEventListener2 listener, ReadOnlySpan<char> name, bool serverSide);
	bool FindListener(IGameEventListener2 listener, ReadOnlySpan<char> name);
	void RemoveListener(IGameEventListener2 listener);
	IGameEvent? CreateEvent(ReadOnlySpan<char> name, bool force = false);
	GameEventDescriptor? GetEventDescriptor(int eventid);
	bool FireEvent(IGameEvent ev, bool dontBroadcast = false);
	bool FireEventClientSide(IGameEvent ev);
	IGameEvent DuplicateEvent(IGameEvent ev);
	void FreeEvent(IGameEvent ev);
	bool SerializeEvent(IGameEvent ev, bf_write buf);
	IGameEvent? UnserializeEvent(bf_read buf);
}

public interface IGameEventListener
{
	void FireGameEvent(KeyValues ev);
}
public interface IGameEventManager
{
	int LoadEventsFromFile(ReadOnlySpan<char> filename);
	void Reset();
	KeyValues GetEvent(ReadOnlySpan<char> name); // returns keys for event
	bool AddListener(IGameEventListener listener, ReadOnlySpan<char> ev, bool isServerSide);
	bool AddListener(IGameEventListener listener, bool isServerSide);
	void RemoveListener(IGameEventListener listener);
	bool FireEvent(KeyValues ev);
	bool FireEventServerOnly(KeyValues ev);
	bool FireEventClientOnly(KeyValues ev);
	bool SerializeKeyValues(KeyValues ev, bf_write buf, IGameEvent? eventtype = null);
	KeyValues UnserializeKeyValue(bf_read msg);
}
public class GameEventCallback
{
	public object? Callback;
	public GameEventListenerType ListenerType;
}

public class GameEventDescriptor
{
	public InlineArrayMaxEventNameLength<char> Name;
	public int EventID;
	public KeyValues? Keys;
	public bool Local;
	public bool Reliable;
	public readonly List<GameEventCallback> Listeners = [];
}

public class GameEvent : IGameEvent
{
	public GameEvent(GameEventDescriptor descriptor) {
		Descriptor = descriptor;
		DataKeys = new(descriptor.Name);
	}
	public ReadOnlySpan<char> GetName() => DataKeys!.Name.SliceNullTerminatedString();
	public bool IsEmpty(ReadOnlySpan<char> keyName = default) => DataKeys!.IsEmpty(keyName);
	public bool IsLocal() => Descriptor!.Local;
	public bool IsReliable() => Descriptor!.Reliable;

	public bool GetBool(ReadOnlySpan<char> keyName = default, bool defaultValue = false) => DataKeys!.GetInt(keyName, defaultValue ? 1 : 0) != 0;
	public int GetInt(ReadOnlySpan<char> keyName = default, int defaultValue = 0) => DataKeys!.GetInt(keyName, defaultValue);
	public float GetFloat(ReadOnlySpan<char> keyName = default, float defaultValue = 0.0f) => DataKeys!.GetFloat(keyName, defaultValue);
	public ReadOnlySpan<char> GetString(ReadOnlySpan<char> keyName = default, ReadOnlySpan<char> defaultValue = "") => DataKeys!.GetString(keyName, defaultValue);

	public void SetBool(ReadOnlySpan<char> keyName, bool value) => DataKeys!.SetInt(keyName, value ? 1 : 0);
	public void SetInt(ReadOnlySpan<char> keyName, int value) => DataKeys!.SetInt(keyName, value);
	public void SetFloat(ReadOnlySpan<char> keyName, float value) => DataKeys!.SetFloat(keyName, value);
	public void SetString(ReadOnlySpan<char> keyName, ReadOnlySpan<char> value) => DataKeys!.SetString(keyName, value);
#if GMOD_DLL
	public KeyValues? GetDataKeys() => DataKeys;
#endif

	public GameEventDescriptor? Descriptor;
	public KeyValues? DataKeys;
}

public enum GameEventListenerType
{
	Serverside,
	Clientside,
	Clientstub,
	ServersideOld,
	ClientsideOld
}

public enum GameEventType
{
	Local,
	String,
	Float,
	Long,
	Short,
	Byte,
	Bool
}
