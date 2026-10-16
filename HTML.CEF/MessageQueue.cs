using Source.Common.GarrysMod;

namespace HTML.CEF;

public class MessageQueue
{
	public enum Type
	{
		OnAddressChange,
		OnConsoleMessage,
		OnTitleChange,
		OnCursorChange,
		OnLoadStart,
		OnLoadEnd,
		OnCreateChildView,
		OnJavaScriptCall
	}

	public struct Message
	{
		public Type Type;
		public int Integer;
		public int Integer2;
		public string FrameId;
		public string String1;
		public string String2;
		public JSValue JSParams;
	}

	readonly Lock Mutex = new();
	readonly Queue<Message> Messages = new();

	public void Lock() => Mutex.Enter();
	public void Unlock() => Mutex.Exit();
	public void Push(in Message message) => Messages.Enqueue(message);
	public bool Empty() => Messages.Count == 0;
	public Message Pop() => Messages.Dequeue();
}
