using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.Keyvalues;
using Source.Common.GUI;
using Source.Common.Input;
using Source.Common.Launcher;

using System.Runtime.CompilerServices;

namespace Source.GUI;

public class MessageItemComparer : IComparer<double>
{
	public int Compare(double x, double y) => x.CompareTo(y);
}

public class VGui : IVGui
{
	readonly List<Tick> TickSignals = [];
	readonly Queue<MessageItem> MessageQueue = [];
	readonly Queue<MessageItem> SecondaryQueue = [];
	readonly PriorityQueue<MessageItem, double> DelayedMessageQueue = new(new MessageItemComparer());

	public readonly VGuiInput Input;
	public readonly ISurface surface;
	public readonly ISystem system;
	public readonly IEngineAPI engineAPI;

	public IAnimationController? animController;
	public IAnimationController GetAnimationController() => animController ??= engineAPI.GetRequiredService<IAnimationController>();

	public VGui(ICommandLine commandLine, ISurface surface, ISystem system, IEngineAPI engineAPI) {
		Input = new(commandLine, this, surface, engineAPI.GetRequiredService<IInputSystem>());
		this.surface = surface;
		this.system = system;
		this.engineAPI = engineAPI;
	}

	public void PanelDeleted(IPanel focus) {
		surface.ReleasePanel(focus);
		Input.PanelDeleted(focus);
		RemoveTickSignal(focus);
	}

	public void AddTickSignal(IPanel panel, long intervalMilliseconds = 0) {
		ref Tick t = ref CreateNewTick(panel, intervalMilliseconds);
	}
	public void RemoveTickSignal(IPanel search) {
		Span<Tick> ticks = TickSignals.AsSpan();
		for (int i = 0; i < ticks.Length; i++) {
			ref Tick tick = ref ticks[i];
			if (tick.Panel.TryGetTarget(out IPanel? target) && target == search) {
				if (CanRemoveTickSignal)
					TickSignals.RemoveAt(i);
				else
					tick.MarkDeleted = true;

				return;
			}
		}
	}

	private ref Tick CreateNewTick(IPanel panel, long intervalMilliseconds) {
		Span<Tick> ticks = TickSignals.AsSpan();
		for (int i = 0; i < ticks.Length; i++) {
			ref Tick t = ref ticks[i];
			if (t.Panel.TryGetTarget(out IPanel? target) && target == panel) {
				t.Interval = intervalMilliseconds;
				t.NextTick = system.GetTimeMillis() + t.Interval;
				t.MarkDeleted = false;
				return ref Unsafe.NullRef<Tick>();
			}
		}

		TickSignals.Add(new());
		ticks = TickSignals.AsSpan(); // The previous span is no longer a valid representation, re-fetch it
		ref Tick tRet = ref ticks[ticks.Length - 1];
		tRet.Panel = new(panel);
		tRet.Interval = intervalMilliseconds;
		tRet.NextTick = system.GetTimeMillis() + tRet.Interval;
		tRet.MarkDeleted = false;
		return ref tRet;
	}

	public void Quit() {

	}

	bool InDispatcher;
	bool CanRemoveTickSignal;
	int ReentrancyCount;
	bool IsReentrant() => ReentrancyCount > 0;

	public void RunFrame() {
		bool isReentrant = InDispatcher;
		if (isReentrant)
			ReentrancyCount++;
		// Generate all key and mouse events
		surface.RunFrame();
		// Let the system process what it needs
		system.RunFrame();

		// Update the mouse cursor
		if (!IsReentrant()) {
			Input.GetCursorPosition(out int x, out int y);
			Input.UpdateMouseFocus(x, y);
		}

		// Update the input state
		if (!isReentrant)
			Input.RunFrame();

		// Message dispatch
		if (!isReentrant) {
			DispatchMessages();

			long time = system.GetTimeMillis();
			CanRemoveTickSignal = false;
			{
				Span<Tick> ticks = TickSignals.AsSpan();
				for (int count = ticks.Length, i = count - 1; i >= 0; i--) {
					ref Tick tick = ref ticks[i];
					if (tick.MarkDeleted)
						continue;

					if (tick.Interval != 0) {
						if (time < tick.NextTick)
							continue;
						tick.NextTick = time + tick.Interval;
					}

					if (tick.Panel.TryGetTarget(out IPanel? target))
						target.OnTick();
				}
			}
			CanRemoveTickSignal = true;

			for (int count = TickSignals.Count, i = count - 1; i >= 0; i--) {
				Tick tick = TickSignals[i];
				if (tick.MarkDeleted)
					TickSignals.RemoveAt(i);
			}
		}

		surface.SolveTraverse(surface.GetEmbeddedPanel());
		surface.ApplyChanges();

		if (isReentrant)
			ReentrancyCount--;
	}

	public bool DispatchMessages() {
		long time = system.GetTimeMillis();
		InDispatcher = true;
		bool doneWork = MessageQueue.Count > 12;
		bool usingDelayedQueue = DelayedMessageQueue.Count > 0;

		int passCount = 0;
		while (passCount < 2) {
			while (MessageQueue.Count > 0 || SecondaryQueue.Count > 0 || usingDelayedQueue) {
				MessageItem messageItem;

				bool usingSecondaryQueue = SecondaryQueue.Count > 0;
				if (usingSecondaryQueue) {
					doneWork = true;
					SecondaryQueue.TryDequeue(out messageItem);
				}
				else if (usingDelayedQueue) {
					if (!DelayedMessageQueue.TryDequeue(out messageItem, out _))
						usingDelayedQueue = false;
				}
				else {
					MessageQueue.TryDequeue(out messageItem);
				}

				KeyValues parms = messageItem.Params;

				if (messageItem.Special == MessageItemType.SetCursorPos) {
					int xpos = parms.GetInt("xpos", 0);
					int ypos = parms.GetInt("ypos", 0);
					Input.UpdateCursorPosInternal(xpos, ypos);
				}

				messageItem.To?.SendMessage(parms, messageItem.Sender);
			}
			passCount += 1;
			if (passCount == 1)
				Input.PostCursorMessage();
		}
		Input.HandleExplicitSetCursor();
		InDispatcher = false;
		return doneWork;
	}

	public void PostMessage(IPanel? target, KeyValues parms, IPanel? sender, double delay = 0, MessageItemType type = MessageItemType.TargettingPanel) {
		if (IsReentrant()) {
			Assert(false);
			return;
		}

		if (target == null && type == MessageItemType.TargettingPanel)
			return;

		MessageItem messageItem = new();
		messageItem.To = target;

		messageItem.Params = parms;
		messageItem.Sender = sender;
		messageItem.ArrivalTime = 0;
		messageItem.MessageID = CurrentMessageID++;
		messageItem.Special = type;

		if (delay > 0) {
			messageItem.ArrivalTime = system.GetTimeMillis() + (delay * 1000);
			DelayedMessageQueue.Enqueue(messageItem, messageItem.ArrivalTime);
		}
		else if (InDispatcher)
			SecondaryQueue.Enqueue(messageItem);
		else
			MessageQueue.Enqueue(messageItem);
	}

	public void ClearMessageQueues() {
		Assert(!InDispatcher);

		MessageQueue.Clear();
		SecondaryQueue.Clear();
		DelayedMessageQueue.Clear();
	}

	ulong CurrentMessageID;

	public void MarkPanelForDeletion(IPanel? panel) {
		PostMessage(panel, new("Delete"), null);
	}

	public void Stop() {

	}

	public IVGuiInput GetInput() => Input;

	internal bool IsDispatchingMessages() {
		return InDispatcher;
	}
}