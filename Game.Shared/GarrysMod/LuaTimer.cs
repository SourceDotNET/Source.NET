#if CLIENT_DLL || GAME_DLL || MENU_DLL
using Source;
using Source.Common.Commands;
using Source.Common.GarrysMod.Lua;

#if CLIENT_DLL
namespace Game.Client.GarrysMod;
#elif MENU_DLL
namespace Game.MenuSystem.GarrysMod;
#else
namespace Game.Server.GarrysMod;
#endif

public class AdvancedTimer_t
{
	public string Location = "";
	public double NextTime;
	public int Function;
	public string Name = "";
	public float Delay;
	public int Reps;
	public bool Paused;
	public bool Removed;
	public bool Stopped;
}

public class SimpleTimer_t
{
	public string Location = "";
	public double Time;
	public int Function = -1;
}

public static partial class LuaTimer
{
	[LuaLibrary]
	static readonly LuaLibrary LL_Factory_timer = new("timer");

	static readonly SortedList<string, AdvancedTimer_t> g_TimerMap = new(StringComparer.Ordinal);
	static readonly LinkedList<SimpleTimer_t> g_SimpleTimerList = new();
	static readonly LinkedList<SimpleTimer_t> g_SimpleTimerListQueue = new();
	static readonly LuaObject currentSimpleTimerCallback = new();
	static bool g_isDoingSimpleTimes;

#if CLIENT_DLL
	static readonly Color cMsgColor = new(255, 241, 122, 255);
#else
	static readonly Color cMsgColor = new(156, 241, 255, 255);
#endif
	static readonly Color offColor = new(200, 200, 200, 255);

	static AdvancedTimer_t? FindTimer(string name, bool create, out bool created) {
		created = false;
		if (g_TimerMap.TryGetValue(name, out AdvancedTimer_t? timer))
			return timer;
		if (!create)
			return null;

		created = true;
		timer = new();
		g_TimerMap.Add(name, timer);
		return timer;
	}

	[LuaFunction]
	static int Check(ILuaInterface lua) {
		if (Game.Client.GarrysMod.GarrysMod.lua_strict.GetInt() != 0)
			lua.ErrorFromLua("timer.Check is deprecated and does nothing. Do not use it.");
		return 0;
	}

	[LuaFunction]
	static int Exists(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		g_Lua.PushBool(timer != null && !timer.Removed);
		return 1;
	}

	[LuaFunction]
	static int Create(ILuaInterface lua) {
		string name = g_Lua!.CheckString(1);
		double delay = g_Lua.CheckNumber(2);
		double reps = g_Lua.CheckNumber(3);
		if (!g_Lua.IsType(4, LuaType.Function)) {
			g_Lua.TypeError("function", 4);
			return 0;
		}

		string? location = g_Lua.GetCurrentLocation();
		int repetitions = (int)reps;
		if (repetitions == 0)
			repetitions = -1;

		AdvancedTimer_t timer = FindTimer(name, true, out bool created)!;
		if (!created)
			g_Lua.ReferenceFree(timer.Function);

		timer.Name = name;
		timer.Reps = repetitions;
		timer.Delay = (float)delay;
		timer.Removed = false;
		timer.Paused = false;
		timer.Stopped = false;
		timer.NextTime = timer.Delay + gpGlobals.CurTime;
		timer.Location = location ?? "Unknown Location";
		g_Lua.Push(4);
		timer.Function = g_Lua.ReferenceCreate();
		return 0;
	}

	[LuaFunction]
	static int Start(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		if (timer != null) {
			timer.Stopped = false;
			timer.Paused = false;
			timer.NextTime = timer.Delay + gpGlobals.CurTime;
		}
		return 0;
	}

	[LuaFunction]
	static int Adjust(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		if (timer == null)
			return 0;

		timer.Delay = (float)g_Lua.CheckNumber(2);
		if (g_Lua.GetType(3) == LuaType.Number) {
			int reps = (int)g_Lua.CheckNumber(3);
			timer.Reps = reps != 0 ? reps : -1;
		}

		if (g_Lua.IsType(4, LuaType.Function)) {
			g_Lua.ReferenceFree(timer.Function);
			g_Lua.Push(4);
			timer.Function = g_Lua.ReferenceCreate();
		}

		timer.NextTime = timer.Delay + gpGlobals.CurTime;
		return 0;
	}

	[LuaFunction]
	static int Pause(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		if (timer != null && !timer.Paused) {
			timer.Paused = true;
			timer.NextTime -= gpGlobals.CurTime;
		}
		return 0;
	}

	[LuaFunction]
	static int UnPause(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		if (timer != null && timer.Paused) {
			timer.Stopped = false;
			timer.Paused = false;
			timer.NextTime += gpGlobals.CurTime;
		}
		return 0;
	}

	[LuaFunction]
	static int IsPaused(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		if (timer != null)
			g_Lua.PushBool(timer.Paused);
		return timer != null ? 1 : 0;
	}

	[LuaFunction]
	static int Toggle(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		if (timer == null)
			return 0;

		if (timer.Paused) {
			timer.Stopped = false;
			timer.Paused = false;
			timer.NextTime += gpGlobals.CurTime;
			return 0;
		}

		timer.Paused = true;
		timer.NextTime -= gpGlobals.CurTime;
		return 0;
	}

	[LuaFunction]
	static int Stop(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		if (timer != null) {
			timer.Stopped = true;
			timer.NextTime = 0;
		}
		return 0;
	}

	[LuaFunction]
	static int Remove(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		if (timer != null)
			timer.Removed = true;
		return 0;
	}

	[LuaFunction]
	static int Destroy(ILuaInterface lua) => Remove(lua);

	[LuaFunction]
	static int TimeLeft(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		if (timer == null || timer.Removed || timer.Reps == 0 || timer.Stopped)
			return 0;

		if (!timer.Paused)
			g_Lua.PushNumber(timer.NextTime - gpGlobals.CurTime);
		else
			g_Lua.PushNumber(-timer.NextTime);
		return 1;
	}

	[LuaFunction]
	static int RepsLeft(ILuaInterface lua) {
		AdvancedTimer_t? timer = FindTimer(g_Lua!.CheckString(1), false, out _);
		if (timer == null || timer.Removed || timer.Stopped)
			return 0;

		g_Lua.PushNumber(timer.Reps);
		return 1;
	}

	[LuaFunction]
	static int Simple(ILuaInterface lua) {
		double delay = g_Lua!.CheckNumber(1);
		if (!g_Lua.IsType(2, LuaType.Function)) {
			g_Lua.TypeError("function", 2);
			return 0;
		}

		SimpleTimer_t timer = new() {
			Time = (float)delay + gpGlobals.CurTime
		};
		g_Lua.Push(2);
		timer.Function = g_Lua.ReferenceCreate();
		timer.Location = g_Lua.GetCurrentLocation() ?? "Unknown Location";

		if (g_isDoingSimpleTimes) {
			currentSimpleTimerCallback.Push();
			bool same = lua.RawEqual(2, 3);
			lua.Pop(1);
			if (same) {
				g_SimpleTimerListQueue.AddLast(timer);
				return 0;
			}
		}

		g_SimpleTimerList.AddLast(timer);
		return 0;
	}

	public static bool CallTimerFunction(int function, string name, string location) {
		g_Lua!.ReferencePush(function);
		if (g_isDoingSimpleTimes)
			currentSimpleTimerCallback.SetFromStack(-1);

		bool ok = g_Lua.CallFunctionProtected(0, 0, true);
		if (g_isDoingSimpleTimes)
			currentSimpleTimerCallback.UnReference();

		if (!ok)
			g_Lua.Msg($"Timer Failed! [{name}][{location}]\n");
		return ok;
	}

	static readonly string strTimerName = "Simple";

	public static void DoSimpleTimers() {
		while (g_SimpleTimerListQueue.First != null) {
			g_SimpleTimerList.AddLast(g_SimpleTimerListQueue.First.Value);
			g_SimpleTimerListQueue.RemoveFirst();
		}

		if (g_SimpleTimerList.First == null)
			return;

		g_isDoingSimpleTimes = true;
		LinkedListNode<SimpleTimer_t>? node = g_SimpleTimerList.First;
		while (node != null) {
			if (node.Value.Time <= gpGlobals.CurTime) {
				CallTimerFunction(node.Value.Function, strTimerName, node.Value.Location);
				g_Lua!.ReferenceFree(node.Value.Function);
				LinkedListNode<SimpleTimer_t>? next = node.Next;
				g_SimpleTimerList.Remove(node);
				node = next;
				continue;
			}
			node = node.Next;
		}
		g_isDoingSimpleTimes = false;
	}

	public static void DoAdvancedTimers() {
		if (g_TimerMap.Count == 0)
			return;

		int i = 0;
		while (i < g_TimerMap.Count) {
			string key = g_TimerMap.Keys[i];
			AdvancedTimer_t timer = g_TimerMap.Values[i];
			if (timer.Reps != 0 && !timer.Removed) {
				if (timer.Paused || timer.Stopped || gpGlobals.CurTime < timer.NextTime) {
					i++;
					continue;
				}

				timer.NextTime = timer.Delay + timer.NextTime;
				if (timer.Reps > 0)
					timer.Reps--;

				if (!CallTimerFunction(timer.Function, timer.Name, timer.Location))
					timer.Removed = true;

				i = g_TimerMap.IndexOfKey(key);
				if (timer.Reps != 0 && !timer.Removed) {
					i++;
					continue;
				}
			}

			g_Lua!.ReferenceFree(timer.Function);
			g_TimerMap.RemoveAt(i);
		}
	}

	public static void Cycle() {
		DoSimpleTimers();
		DoAdvancedTimers();
	}

	public static void Shutdown() {
		currentSimpleTimerCallback.UnReference();
		g_SimpleTimerListQueue.Clear();
		g_SimpleTimerList.Clear();
		g_TimerMap.Clear();
	}

	public static void Dump() {
		ConColorMsg(cMsgColor, "Simple Timers:\n");
		if (g_SimpleTimerList.First == null && g_SimpleTimerListQueue.First == null)
			Msg("    No simple timers!\n");
		else {
			foreach (SimpleTimer_t timer in g_SimpleTimerList) {
				Msg($"    Simple Timer\tTime Left: {timer.Time - gpGlobals.CurTime:0.00}");
				ConColorMsg(offColor, $"\t{timer.Location}\n");
			}
			foreach (SimpleTimer_t timer in g_SimpleTimerListQueue) {
				Msg($"    Queued Timer\tTime Left: {timer.Time - gpGlobals.CurTime:0.00}");
				ConColorMsg(offColor, $"\t{timer.Location}\n");
			}
		}

		ConColorMsg(cMsgColor, "Timers:\n");
		if (g_TimerMap.Count == 0) {
			Msg("    No timers!\n");
			return;
		}

		int longest = 0;
		foreach (AdvancedTimer_t timer in g_TimerMap.Values)
			if (longest < timer.Name.Length)
				longest = timer.Name.Length;
		int pad = Math.Min(longest, 32);

		foreach (AdvancedTimer_t timer in g_TimerMap.Values) {
			int spaces = timer.Name.Length < pad ? 1 - timer.Name.Length + pad : 1;
			Msg($"    Timer '{timer.Name}'{new string(' ', spaces)}Time Left: {timer.NextTime - gpGlobals.CurTime:0.00}, Reps Left: {timer.Reps}, Paused: {(timer.Paused ? 1 : 0)}");
			ConColorMsg(offColor, $"\t{timer.Location}\n");
		}
	}

#if GAME_DLL
	[ConCommand("lua_dumptimers_sv", "Dumps all the currently active Lua timers on the server")]
	static void lua_dumptimers_sv(in TokenizedCommand args) {
		if (!Util.IsCommandIssuedByServerAdmin())
			return;
		Dump();
	}
#else
	[ConCommand("lua_dumptimers_cl", "Dumps all the currently active Lua timers on the client")]
	static void lua_dumptimers_cl(in TokenizedCommand args) => Dump();
#endif
}
#endif
