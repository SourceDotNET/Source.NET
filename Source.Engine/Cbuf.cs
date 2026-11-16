using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common.Commands;
using Source.Engine.Server;

using System.Diagnostics;

namespace Source.Engine;

public class CommandBuffer
{
	public const int ARGS_BUFFER_LENGTH = 8192;

	public class Command
	{
		public long Tick;
		public int FirstArgS;
		public int BufferSize;
	}

	char[] argsBuffer = new char[ARGS_BUFFER_LENGTH];
	int lastUsedArgSize;
	int argBufferSize;
	LinkedList<Command> Commands = [];
	long currentTick;
	long lastTickToProcess;
	long waitDelayTicks;
	LinkedListNode<Command>? nextCommand;
	int maxArgBufferLength;
	bool isProcessingCommands;
	bool waitEnabled;

	TokenizedCommand currentCommand;

	public bool DequeueNextCommand() {
		currentCommand.Reset();
		Dbg.Assert(isProcessingCommands);
		if (Commands.Count == 0)
			return false;

		LinkedListNode<Command>? head = Commands.First;
		if (head == null)
			return false;

		Command cmd = head.Value;
		if (cmd.Tick > lastTickToProcess)
			return false;

		currentTick = cmd.Tick;
		if (cmd.BufferSize > 0)
			currentCommand.Tokenize(argsBuffer.AsSpan()[cmd.FirstArgS..(cmd.FirstArgS + cmd.BufferSize)]);

		Commands.Remove(head);
		nextCommand = Commands.First;
		return true;
	}

	public CommandBuffer() {
		lastUsedArgSize = 0;
		argBufferSize = 0;
		currentTick = 0;
		lastTickToProcess = -1;
		waitDelayTicks = 1;
		nextCommand = null;
		maxArgBufferLength = ARGS_BUFFER_LENGTH;
		isProcessingCommands = false;
		waitEnabled = false;
	}

	public ref TokenizedCommand GetCommand() => ref currentCommand;

	public bool AddText(ReadOnlySpan<char> text, int tickDelay = 0) {
		Dbg.Assert(tickDelay >= 0);

		long tick = currentTick + tickDelay;

		ReadOnlySpan<char> currentCommand = text.SliceNullTerminatedString();

		Span<char> argV0Stack = stackalloc char[1024];
		int len = currentCommand.Length;
		int offsetToNextCommand = 0;
		for (; len > 0; len -= offsetToNextCommand, currentCommand = currentCommand[(offsetToNextCommand)..]) {
			GetNextCommandLength(currentCommand, len, out int commandLength, out offsetToNextCommand);
			if (commandLength <= 0)
				continue;

			Span<char> argV0 = commandLength + 1 <= argV0Stack.Length ? argV0Stack[..(commandLength + 1)] : new char[commandLength + 1];
			StringReader reader = new StringReader(new(currentCommand[..commandLength]));
			ParseArgV0(reader, argV0, out ReadOnlySpan<char> argS);
			if (argV0[0] == '\0')
				continue;

			if (stricmp(argV0, "wait") == 0 && IsWaitEnabled()) {
				long delay = !argS.IsEmpty ? atoi(argS) : waitDelayTicks;
				tick += delay;
				continue;
			}

			if (!InsertCommand(currentCommand[..commandLength], tick))
				return false;
		}

		return true;
	}

	void Compact() {
		argBufferSize = 0;
		Span<char> tempBuffer = stackalloc char[ARGS_BUFFER_LENGTH];
		foreach (Command command in Commands) {
			argsBuffer.AsSpan(command.FirstArgS, command.BufferSize).CopyTo(tempBuffer[argBufferSize..]);
			command.FirstArgS = argBufferSize;
			argBufferSize += command.BufferSize;
		}

		tempBuffer[..argBufferSize].CopyTo(argsBuffer);
	}

	private unsafe bool InsertCommand(ReadOnlySpan<char> argS, long tick) {
		int commandSize = argS.Length;

		if (commandSize >= TokenizedCommand.MaxCommandLength) {
			Dbg.Warning($"WARNING: Command too long... ignoring!\n{argS}\n");
			return false;
		}

		if (argBufferSize + commandSize + 1 > maxArgBufferLength) {
			Compact();
			if (argBufferSize + commandSize + 1 > maxArgBufferLength)
				return false;
		}

		fixed (char* pArgSBuffer = argsBuffer)
			argS.CopyTo(new Span<char>((void*)(((nint)pArgSBuffer) + (argBufferSize * sizeof(char))), argsBuffer.Length));

		argsBuffer[argBufferSize + commandSize] = '\0';
		++commandSize;

		Command command = new();
		command.Tick = tick;
		command.FirstArgS = argBufferSize;
		command.BufferSize = commandSize;
		argBufferSize += commandSize;

		if (!isProcessingCommands || (tick > currentTick)) {
			InsertCommandAtAppropriateTime(command);
		}
		else {
			InsertImmediateCommand(command);
		}

		return true;
	}

	private void InsertImmediateCommand(Command command) {
		if (nextCommand == null)
			Commands.AddLast(command);
		else
			Commands.AddBefore(nextCommand, command);
	}

	private void InsertCommandAtAppropriateTime(Command command) {
		LinkedListNode<Command>? i;
		for (i = Commands.First; i != null; i = i.Next) {
			if (i.Value.Tick > command.Tick)
				break;
		}
		if (i == null)
			Commands.AddLast(command);
		else
			Commands.AddBefore(i, command);
	}

	private bool ParseArgV0(StringReader buf, Span<char> argV0, out ReadOnlySpan<char> argS) {
		argV0[0] = '\0';
		argS = null;

		if (!buf.IsValid())
			return false;

		int size = buf.ParseToken(TokenizedCommand.DefaultBreakSet, argV0);
		if (size <= 0 || argV0.Length == size) {
			argV0[0] = '\0';
			return false;
		}

		argV0[size] = '\0';

		int argsLen = buf.TellMaxPut() - buf.TellGet();
		argS = (argsLen > 0) ? buf.PeekToEnd() : null;
		return true;
	}

	private void GetNextCommandLength(ReadOnlySpan<char> text, int maxLen, out int commandLength, out int nextCommandOffset) { // FIXME!!! Multiple commands in the same input ("test;test") WILL cause this to infinitely loop!
		commandLength = 0;
		bool isQuoted = false;
		bool isCommented = false;
		for (nextCommandOffset = 0; nextCommandOffset < maxLen; ++nextCommandOffset, commandLength += isCommented ? 0 : 1) {
			char c = text[nextCommandOffset];
			if (!isCommented) {
				if (c == '"') {
					isQuoted = !isQuoted;
					continue;
				}

				if (!isQuoted && c == '/') {
					isCommented = (nextCommandOffset < maxLen - 1) && text[nextCommandOffset + 1] == '/';
					if (isCommented) {
						++nextCommandOffset;
						continue;
					}
				}

				if (!isQuoted && c == ';') {
					++nextCommandOffset;
					break;
				}
			}

			if (c == '\n') {
				nextCommandOffset++;
				break;
			}
		}
	}

	public void BeginProcessingCommands(int deltaTicks) {
		if (deltaTicks == 0)
			return;

		Dbg.Assert(!isProcessingCommands);
		isProcessingCommands = true;
		lastTickToProcess = currentTick + deltaTicks - 1;
		nextCommand = Commands.First;
	}

	public void EndProcessingCommands() {
		Dbg.Assert(isProcessingCommands);
		isProcessingCommands = false;
		currentTick = lastTickToProcess + 1;
		nextCommand = null;

		LinkedListNode<Command>? i = Commands.First;
		if (i == null) {
			argBufferSize = 0;
			return;
		}

		while (i != null) {
			if (i.Value.Tick >= currentTick)
				break;

			LinkedListNode<Command>? next = i.Next; // note: GMod loops forever here
			Dbg.Msg("Warning: Skipping command %s\n", new string(argsBuffer, i.Value.FirstArgS, i.Value.BufferSize - 1));
			Commands.Remove(i);
			i = next;
		}

		Compact();
	}

	public bool IsProcessingCommands() => isProcessingCommands;

	public LinkedListNode<Command>? GetNextCommandHandle() {
		return Commands.First;
	}

	public int GetArgumentBufferSize() {
		return argBufferSize;
	}
	public int GetMaxArgumentBufferSize() {
		return maxArgBufferLength;
	}

	public void SetWaitDelayTime(int tickDelay) {
		Dbg.Assert(tickDelay >= 0);
		waitDelayTicks = tickDelay;
	}

	public void SetWaitEnabled(bool wait) {
		waitEnabled = wait;
	}

	public bool IsWaitEnabled() => waitEnabled;
}

public class Cbuf(IServiceProvider provider)
{
	public readonly CommandBuffer Buffer = new();

	Cmd Cmd;
	Host Host;

	static bool WarnedOutsideMainThread;

	public void Init() {
		Cmd = provider.GetRequiredService<Cmd>();
		Host = provider.GetRequiredService<Host>();
		Buffer.SetWaitDelayTime(1);
	}
	public void Shutdown() { }

	public void ExecuteCommand(ref TokenizedCommand command, CommandSource source, int clientSlot = -1) {
		Cmd.ExecuteCommand(ref command, source);
	}

	public void AddText(ReadOnlySpan<char> text) {
		lock (Buffer) {
			if (!Buffer.AddText(text))
				Dbg.ConMsg("Cbuf_AddText: buffer overflow\n");
		}
	}

	public void InsertText(ReadOnlySpan<char> text) {
		lock (Buffer) {
			Dbg.Assert(Buffer.IsProcessingCommands());
			AddText(text);
		}
	}

	public void Execute() {
		if (!ThreadInMainThread()) {
			Dbg.Warning("Executing command outside main loop thread\n");
			if (!WarnedOutsideMainThread) {
				WarnedOutsideMainThread = true;
				if (Debugger.IsAttached)
					Debugger.Break();
			}
		}

		lock (Buffer) {
			if (Buffer.IsProcessingCommands())
				return;

			if (BaseServer.sv_allow_wait_command.GetBool() != Buffer.IsWaitEnabled())
				Buffer.SetWaitEnabled(BaseServer.sv_allow_wait_command.GetBool());

			Buffer.BeginProcessingCommands(1);
			while (Buffer.DequeueNextCommand()) {
				ExecuteCommand(ref Buffer.GetCommand(), CommandSource.Command);
			}
			Buffer.EndProcessingCommands();
		}
	}


	public const string CMDSTR_ADD_EXECUTION_MARKER = "[$&*,`]";
	public const int MAX_EXECUTION_MARKERS = 2048;
	List<int> ExecutionMarkers = [];
	public bool HasRoomForExecutionMarkers(int executionMarkersCount) {
		return ExecutionMarkers.Count + executionMarkersCount < MAX_EXECUTION_MARKERS;
	}
	public bool AddTextWithMarkers(CmdExecutionMarker markerLeft, ReadOnlySpan<char> text, CmdExecutionMarker markerRight) {
		if (!HasRoomForExecutionMarkers(2)) {
			Dbg.ConMsg("Cbuf_AddTextWithMarkers: execution marker overflow\n");
			return false;
		}

		int markerCodeLeft = CreateExecutionMarker();
		int markerCodeRight = CreateExecutionMarker();

		string strMarkerLeft = $";{CMDSTR_ADD_EXECUTION_MARKER} {(char)markerLeft} {markerCodeLeft};";
		string strMarkerRight = $";{CMDSTR_ADD_EXECUTION_MARKER} {(char)markerRight} {markerCodeRight};";

		int textToBeAddedLen = strMarkerLeft.Length + strMarkerRight.Length + (int)strlen(text) + 3;

		lock (Buffer) {
			if (Buffer.GetArgumentBufferSize() + textToBeAddedLen + 1 > Buffer.GetMaxArgumentBufferSize()) {
				FindAndRemoveExecutionMarker(markerCodeLeft);
				FindAndRemoveExecutionMarker(markerCodeRight);

				Dbg.ConWarning("Cbuf_AddTextWithMarkers: buffer overflow\n");
				return false;
			}

			bool success = AddExecutionMarker(markerLeft, strMarkerLeft) && Buffer.AddText(text) && AddExecutionMarker(markerRight, strMarkerRight);
			if (!success)
				Host.Error("Cbuf_AddTextWithMarkers: buffer overflow\n");

			return true;
		}
	}

	private bool AddExecutionMarker(CmdExecutionMarker marker, ReadOnlySpan<char> markerCode) {
		if (marker == CmdExecutionMarker.EnableServerCanExecute) {
			Buffer.SetWaitEnabled(false);
		}
		else if (marker == CmdExecutionMarker.DisableServerCanExecute) {
			Buffer.SetWaitEnabled(true);
		}

		return Buffer.AddText(markerCode);
	}

	public bool FindAndRemoveExecutionMarker(int code) => ExecutionMarkers.Remove(code);

	public int CreateExecutionMarker() {
		if (ExecutionMarkers.Count >= MAX_EXECUTION_MARKERS)
			Host.Error("CreateExecutionMarker called, but the max has already been reached.");

		int randomNumber;
		while (true) {
			randomNumber = Random.Shared.Next(0, 1 << 30);
			if (ExecutionMarkers.Contains(randomNumber))
				continue;
			else
				break;
		}

		ExecutionMarkers.Add(randomNumber);
		return randomNumber;
	}

	internal static bool EscapeCommandArg(ReadOnlySpan<char> s1, Span<char> s1Escaped) {
		if (s1.IsEmpty)
			return false;

		foreach (var ch in s1) {
			if (ch <= (char)31)
				return false;

			if (ch == '"')
				return false;
		}

		if (s1Escaped.Length < s1.Length + 3)
			return false;

		s1Escaped[0] = '"';

		for (int i = 0; i < s1.Length; i++)
			s1Escaped[i + 1] = s1[i];

		s1Escaped[s1.Length + 1] = '"';
		s1Escaped[s1.Length + 2] = '\0';

		return true;
	}

	internal void Clear() {
		Init();
	}
}
