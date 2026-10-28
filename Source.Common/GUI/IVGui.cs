using Source.Common.Formats.Keyvalues;

namespace Source.Common.GUI;

/// <summary>
/// Special sub-types if the message isn't designed for a panel.
/// </summary>
public enum MessageItemType
{
	TargettingPanel,
	SetCursorPos
}

public struct MessageItem
{
	public KeyValues Params;
	public IPanel? To;
	public IPanel? Sender;
	public double ArrivalTime;
	public ulong MessageID;
	public MessageItemType Special;
}

public struct Tick
{
	public WeakReference<IPanel> Panel;
	public long Interval;
	public long NextTick;
	public bool MarkDeleted;
}

public interface IVGui
{
	void Quit();
	void RunFrame();
	bool DispatchMessages();
	void PostMessage(IPanel? to, KeyValues message, IPanel? sender, double delay = 0, MessageItemType type = MessageItemType.TargettingPanel);
	void Stop();
	IVGuiInput GetInput();
	void MarkPanelForDeletion(IPanel? panel);
	IAnimationController GetAnimationController();
	void AddTickSignal(IPanel panel, long intervalMilliseconds = 0);
	void RemoveTickSignal(IPanel panel);
	void PanelDeleted(IPanel focus);
}
