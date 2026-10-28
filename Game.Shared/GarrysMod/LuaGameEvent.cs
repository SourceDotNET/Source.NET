#if CLIENT_DLL || GAME_DLL
using Game.Shared;

using Source.Common;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public class LuaGameEventListener : AutoGameSystemPerFrame, IGameEventListener2
{
	public static readonly LuaGameEventListener g_LuaGameEventListener = new();

	bool RegisteredForEvents;

	public void ListenForGameEvent(ReadOnlySpan<char> name) {
		RegisteredForEvents = true;
#if CLIENT_DLL
		bool serverSide = false;
#else
		bool serverSide = true;
#endif
		gameeventmanager?.AddListener(this, name, serverSide);
	}

	public void StopListeningForAllEvents() {
		if (RegisteredForEvents) {
			gameeventmanager?.RemoveListener(this);
			RegisteredForEvents = false;
		}
	}

	public override ReadOnlySpan<char> Name() => "LuaGameEventListener";

	public override void LevelShutdownPostEntity() => StopListeningForAllEvents();

	public void FireGameEvent(IGameEvent ev) {
		if (g_Lua == null || gGM == null)
			return;

		LuaTable data = new();
		for (KeyValues? key = ev.GetDataKeys()?.GetFirstValue(); key != null; key = key.GetNextValue()) {
			switch (key.Type) {
				case KeyValues.Types.String:
					data.SetMember(key.Name, key.GetString());
					break;
				case KeyValues.Types.Int:
					data.SetMember(key.Name, key.GetInt());
					break;
				case KeyValues.Types.Double:
					data.SetMember(key.Name, key.GetFloat());
					break;
				case KeyValues.Types.Uint64:
					data.SetMember(key.Name, key.GetUint64(null).ToString());
					break;
			}
		}

		if (gGM.CallWithArgs(new string(ev.GetName()))) {
			data.Push();
			gGM.CallFinish(1);
		}
		data.UnReference();
	}
}

public static partial class LuaGameEventLib
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_gameevent = new("gameevent");

	[LuaFunction]
	static int Listen(ILuaInterface lua) {
		LuaGameEventListener.g_LuaGameEventListener.ListenForGameEvent(g_Lua!.CheckString(1));
		return 0;
	}
}
#endif
