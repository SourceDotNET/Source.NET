global using static Game.Server.HL2.HL2EventLogGlobals;

using Game.Shared;

using Source.Common;

namespace Game.Server.HL2;

public static class HL2EventLogGlobals
{
	static readonly HL2EventLog s_HL2EventLog = new();
	public static IGameSystem GameLogSystem() => s_HL2EventLog;
}

public class HL2EventLog : EventLog
{
	public override ReadOnlySpan<char> Name() => "CHL2EventLog";

	protected override bool PrintEvent(IGameEvent ev) {
		if (base.PrintEvent(ev))
			return true;

		if (ev.GetName().SequenceEqual("hl2_"))
			return PrintHL2Event(ev);

		return false;
	}

	protected virtual bool PrintHL2Event(IGameEvent ev) => false;
}
