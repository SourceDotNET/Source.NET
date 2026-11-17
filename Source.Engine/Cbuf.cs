using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common.Commands;

namespace Source.Engine;

public class CommandBuffer
{
	public const int ARGS_BUFFER_LENGTH = 2 << 13;

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

		LinkedListNode<Command>? command = Commands.First;
		if (command == null)
			return false;

		Command cmd = command.Value;

		currentTick = cmd.Tick;
		if (cmd.BufferSize > 0)
			currentCommand.Tokenize(argsBuffer.AsSpan()[cmd.FirstArgS..(cmd.FirstArgS + cmd.BufferSize)]);

		Commands.Remove(cmd);
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

	public bool AddText(ReadOnlySpan<char> text, long tickDelay = 0) {
		long tick = currentTick + tickDelay;

		ReadOnlySpan<char> currentCommand = text.SliceNullTerminatedString();

		Span<char> argV0 = stackalloc char[1024];
		int len = currentCommand.Length;
		int offsetToNextCommand = 0;
		for (; len > 0; len -= offsetToNextCommand, currentCommand = currentCommand[(offsetToNextCommand)..]) {
			GetNextCommandLength(currentCommand, len, out int commandLength, out offsetToNextCommand);
			if (commandLength <= 0)
				continue;

			StringReader reader = new StringReader(new(currentCommand[..commandLength]));
			ParseArgV0(reader, argV0[..commandLength], out ReadOnlySpan<char> argS);
			// We had this, but it seems unnecessary now due to the SliceNullTerminatedString call, and it breaks
			// single-letter commands (which don't really exist anyway, but its still a bug)
			//if (argV0[0] == '\0') continue;

			// wait command later

			if (!InsertCommand(currentCommand[..commandLength], tick))
				return false;
		}

		return true;
	}

	void Compact() {
		argBufferSize = 0;
		Span<char> tempBuffer = stackalloc char[ARGS_BUFFER_LENGTH];
		Span<char> writeBuffer = argsBuffer.AsSpan();
		foreach (Command command in Commands) {
			writeBuffer[command.FirstArgS..].CopyTo(tempBuffer[argBufferSize..]);
			command.FirstArgS = argBufferSize;
			argBufferSize += command.BufferSize;
		}

		tempBuffer[..argBufferSize].CopyTo(writeBuffer);
	}

	private unsafe bool InsertCommand(ReadOnlySpan<char> argS, long tick) {
		int commandSize = argS.Length;

		if (commandSize > TokenizedCommand.MaxCommandLength) {
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
			Commands.AddAfter(nextCommand, command);
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

		int size = buf.ParseToken(TokenizedCommand.DefaultBreakSet, argV0);
		if (size <= 0)
			return false;

		int argsLen = buf.TellMaxPut();
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
		Dbg.Assert(!isProcessingCommands);
		isProcessingCommands = true;
		lastTickToProcess = currentTick + deltaTicks - 1;
		nextCommand = Commands.First;
	}

	public void EndProcessingCommands() {
		isProcessingCommands = false;
		currentTick = lastTickToProcess + 1;
		nextCommand = null;

		// todo; better error handling here.
	}

	public bool IsProcessingCommands() => isProcessingCommands;

	public LinkedListNode<Command>? GetNextCommandHandle() {
		return nextCommand;
	}

	public int GetArgumentBufferSize() {
		return argBufferSize;
	}
	public int GetMaxArgumentBufferSize() {
		return maxArgBufferLength;
	}

	public void SetWaitEnabled(bool wait) {
		waitEnabled = wait;
	}
}

public class Cbuf(IServiceProvider provider)
{
	public readonly CommandBuffer Buffer = new();

	Cmd Cmd;

	public void Init() {
		Cmd = provider.GetRequiredService<Cmd>();
	}
	public void Shutdown() { }

	public void ExecuteCommand(ref TokenizedCommand command, CommandSource source, int clientSlot = -1) {
		Cmd.ExecuteCommand(ref command, source);
	}

	public void AddText(ReadOnlySpan<char> text) {
		lock (Buffer) {
			if (!Buffer.AddText(text))
				Dbg.ConMsg("CBuf.AddText: buffer overflow;\n");
		}
	}

	public void InsertText(ReadOnlySpan<char> text) {
		lock (Buffer) {
			Dbg.Assert(Buffer.IsProcessingCommands());
			AddText(text);
		}
	}

	public void Execute() {
		lock (Buffer) {
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
			Dbg.ConMsg("Cbuf.AddTextWithMarkers: execution marker overflow\n");
			return false;
		}

		int markerCodeLeft = CreateExecutionMarker();
		int markerCodeRight = CreateExecutionMarker();

		Span<char> strMarkerLeft = stackalloc char[512];
		Span<char> strMarkerRight = stackalloc char[512];

		$"{CMDSTR_ADD_EXECUTION_MARKER} {markerLeft} {markerCodeLeft}".CopyTo(strMarkerLeft);
		$"{CMDSTR_ADD_EXECUTION_MARKER} {markerRight} {markerCodeRight}".CopyTo(strMarkerRight);

		int strLeftLen = System.MemoryExtensions.IndexOf(strMarkerLeft, '\0');
		int strRightLen = System.MemoryExtensions.IndexOf(strMarkerRight, '\0');
		int textToBeAddedLen = strLeftLen + strRightLen + text.Length + 3;

		lock (Buffer) {
			if (Buffer.GetArgumentBufferSize() + textToBeAddedLen + 1 > Buffer.GetMaxArgumentBufferSize()) {
				FindAndRemoveExecutionMarker(markerCodeLeft);
				FindAndRemoveExecutionMarker(markerCodeRight);

				Dbg.ConMsg("Cbuf.AddTextWithMarkers: buffer overflow\n");
				return false;
			}

			bool success = AddExecutionMarker(markerLeft, strMarkerLeft[..strLeftLen]) && Buffer.AddText(text) && AddExecutionMarker(markerRight, strMarkerRight[..strRightLen]);
			if (!success)
				Dbg.ConMsg("Cbuf.AddTextWithMarkers: buffer overflow\n");

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
		if (ExecutionMarkers.Count >= MAX_EXECUTION_MARKERS) {
			// todo
		}

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
