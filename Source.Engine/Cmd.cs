using Microsoft.Extensions.DependencyInjection;

using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Engine.Client;
using Source.Engine.Server;

namespace Source.Engine;

public class Cmd(IEngineAPI provider, IFileSystem fileSystem)
{
	public const string CMDSTR_ADD_EXECUTION_MARKER = "[$&*,`]";

	Cbuf Cbuf;
	Host Host;
	ICvar cvar;
	ICommandLine CommandLine;
	CvarUtilities cv;
	public void Init() {
		Cbuf = provider.GetRequiredService<Cbuf>();
		Host = provider.GetRequiredService<Host>();
		cvar = provider.GetRequiredService<ICvar>();
		cv = provider.GetRequiredService<CvarUtilities>();
		CommandLine = provider.GetRequiredService<ICommandLine>();
	}
	public void Shutdown() { }

	Dictionary<string, string> aliases = [];

	public CommandSource Source;
	public int ClientSlot;

	static int InvalidExecutionMarkerCount;

	private void HandleExecutionMarker(ReadOnlySpan<char> command, ReadOnlySpan<char> markerCodeS) {
		char cCommand = command.IsEmpty ? '\0' : command[0];
		int markerCode = atoi(markerCodeS);

		if (Cbuf.FindAndRemoveExecutionMarker(markerCode)) {
			if (cCommand == (char)CmdExecutionMarker.EnableServerCanExecute) ++filterCommandsByServerCanExecute;
			else if (cCommand == (char)CmdExecutionMarker.DisableServerCanExecute) --filterCommandsByServerCanExecute;
			else if (cCommand == (char)CmdExecutionMarker.EnableClientCmdCanExecute) ++filterCommandsByClientCmdCanExecute;
			else if (cCommand == (char)CmdExecutionMarker.DisableClientCmdCanExecute) --filterCommandsByClientCmdCanExecute;
		}
		else {
			if (++InvalidExecutionMarkerCount < 3)
				Dbg.Warning("Invalid execution marker code.\n");
		}
	}

	public ConCommandBase? ExecuteCommand(ref TokenizedCommand command, CommandSource source, int clientSlot = -1) {
		if (command.ArgC() == 0)
			return null;

		if (command[0].Equals(CMDSTR_ADD_EXECUTION_MARKER, StringComparison.OrdinalIgnoreCase)) {
			if (command.ArgC() == 3)
				HandleExecutionMarker(command[1], command[2]);
			else
				Dbg.Warning("WARNING: INVALID EXECUTION MARKER.\n");

			return null;
		}

		if (aliases.TryGetValue(new(command[0]), out var aliasValue)) {
			Cbuf.InsertText(aliasValue);
			return null;
		}

		Source = source;
		ClientSlot = clientSlot;

		ConCommandBase? commandBase = cvar.FindCommandBase(command[0]);

		if (ShouldPreventServerCommand(commandBase))
			return null;

		if (commandBase != null && commandBase.IsCommand()) {
			if (!ShouldPreventClientCommand(commandBase) && commandBase.IsCommand()) {
				bool isServerCommand = commandBase.IsFlagSet(FCvar.GameDLL) && source == CommandSource.Command && !sv.IsDedicated();

				if (sv.IsActive()) {
					serverPluginHandler.SetCommandClient(source == CommandSource.Client ? clientSlot : -1);
#if !SWDS
					if (isServerCommand)
						serverPluginHandler.SetCommandClient(cl.PlayerSlot);
#endif
				}
				else if (isServerCommand) {
					// We're not the server, but we are connected - so we'll try to forward it
					if (cl.IsConnected()) {
						ForwardToServer(in command);
						return null;
					}

					// Server command, not connected, not executing
					return null;
				}

				if (commandBase.IsFlagSet(FCvar.Cheat)) {
					if (!Host.IsSinglePlayerGame() && !Host.CanCheat()) {
						if (filterCommandsByServerCanExecute == 0 || !commandBase.IsFlagSet(FCvar.ServerCanExecute)) {
							Dbg.Msg($"Can't use cheat command {commandBase.GetName()} in multiplayer, unless the server has sv_cheats set to 1.\n");
							return null;
						}
					}
				}

				if (commandBase.IsFlagSet(FCvar.SingleplayerOnly)) {
					if (!Host.IsSinglePlayerGame()) {
						Dbg.Msg($"Can't use command {commandBase.GetName()} in multiplayer.\n");
						return null;
					}
				}

				// development only stuff

				Dispatch(commandBase, in command);
				return commandBase;
			}
		}

		if (commandBase != null && source == CommandSource.Command && CommandLine.CheckParm("-default") && !commandBase.IsFlagSet(FCvar.ExecDespiteDefault)) {
			Dbg.Msg($"Ignoring cvar \"{commandBase.GetName()}\" due to -default on command line\n");
			return null;
		}

		if (cv.IsCommand(command))
			return commandBase;

		if (source == CommandSource.Command && cl.IsConnected()) {
			ForwardToServer(command);
			return null;
		}

		Dbg.Msg("Unknown command \"%s\"\n", command[0].ToString());
		return null;
	}

	public void ForwardToServer(in TokenizedCommand command) {
#if !SWDS
		Span<char> str = stackalloc char[1024];
		if (command.Arg(0).Equals("cmd", StringComparison.OrdinalIgnoreCase))
			command.ArgS(1).CopyTo(str);
		else
			command.ArgS(0).CopyTo(str);
		cl.SendStringCmd(str);
#endif
	}

	public void Dispatch(ConCommandBase commandBase, in TokenizedCommand command) {
		ConCommand conCommand = (ConCommand)commandBase;
		conCommand.Dispatch(in command, Source, ClientSlot);
	}

	readonly HashSet<string> ExtraClientCmdCanExecuteCvars = new(StringComparer.OrdinalIgnoreCase);

	public void AddClientCmdCanExecuteVar(string name) => ExtraClientCmdCanExecuteCvars.Add(name);

	public bool ShouldPreventClientCommand(ConCommandBase? cmd) {
		if (filterCommandsByClientCmdCanExecute > 0 && cmd != null && !cmd.IsFlagSet(FCvar.ClientCmdCanExecute) && !ExtraClientCmdCanExecuteCvars.Contains(cmd.GetName())) {
			// If this command is in the game DLL, don't mention it because we're going to forward this
			// request to the server and let the server handle it.
			if (!cmd.IsFlagSet(FCvar.GameDLL))
				Dbg.Warning("FCVAR_CLIENTCMD_CAN_EXECUTE prevented running command: %s\n", cmd.GetName());

			return true;
		}

		return false;
	}

	int filterCommandsByServerCanExecute = 0;
	int filterCommandsByClientCmdCanExecute = 0;

	public bool ShouldPreventServerCommand(ConCommandBase? cmd) {
		if (!Host.IsSinglePlayerGame()) {
			if (filterCommandsByServerCanExecute > 0) {
				if (cmd == null)
					return false;

				if (!cmd.IsFlagSet(FCvar.ServerCanExecute)) {
					Dbg.Warning("FCVAR_SERVER_CAN_EXECUTE prevented server running command: %s\n", cmd.GetName());
					return true;
				}
			}
		}

		return false;
	}

	[ConCommand(helpText: "Forward command to server.")]
	void cmd(in TokenizedCommand args) {
		ForwardToServer(in args);
	}

	[ConCommand(helpText: "Alias a command.")]
	void alias(in TokenizedCommand args) {
		int argc = args.ArgC();
		if (argc == 1) {
			ConMsg("Current alias commands:\n");
			foreach (var alias in aliases) {
				ConMsg($"  {alias.Key} : {alias.Value}\n");
			}
			return;
		}

		ReadOnlySpan<char> s = args[1];
		ReadOnlySpan<char> restOfCommandLine = args.ArgS(startingArg: 2);

		if (restOfCommandLine.Equals(s, StringComparison.OrdinalIgnoreCase)) {
			ConMsg($"Can't alias '{s}' to itself.\n");
			return;
		}

		aliases[new string(s)] = new string(restOfCommandLine);
	}
	[ConCommand(helpText: "Echo text to console.", flags: FCvar.ServerCanExecute)]
	void echo(in TokenizedCommand args) {
		for (int i = 1, argc = args.ArgC(); i < argc; i++)
			ConMsg($"{args[i]} ");
		ConMsg("\n");
	}
	[ConCommand(helpText: "Parses and stuffs command line + commands to command buffer.")]
	void stuffcmds(in TokenizedCommand args) {
		if (args.ArgC() != 1) {
			ConMsg("stuffcmds: execute command line parameters\n");
			return;
		}

		string build = "";
		for (int i = 1; i < CommandLine.ParmCount(); i++) {
			ReadOnlySpan<char> parm = CommandLine.GetParm(i);
			if (parm.IsEmpty || parm.Length == 0) continue;

			if (parm[0] == '-') {
				ReadOnlySpan<char> value = CommandLine.ParmValueByIndex(i);
				if (!value.IsEmpty)
					i++;
				continue;
			}
			if (parm[0] == '+') {
				ReadOnlySpan<char> value = CommandLine.ParmValueByIndex(i);
				if (!value.IsEmpty && value.Length > 0) {
					build += ($"{parm[1..]} {value}\n");
					i++;
				}
				else {
					build += new string(parm[1..]);
					build += ('\n');
				}
			}
			else {
				ReadOnlySpan<char> translated = TranslateFileAssociation(CommandLine.GetParm(i));
				if (!translated.IsEmpty) {
					build += new string(translated);
					build += ('\n');
				}
			}
		}
		build += ('\0');
		if (build.Length > 0)
			Cbuf.InsertText(build);
	}

	static bool IsValidFileExtension(ReadOnlySpan<char> filename) {
		if (filename.IsEmpty)
			return false;

		if (
			filename.Contains(".exe", StringComparison.OrdinalIgnoreCase) ||
			filename.Contains(".vbs", StringComparison.OrdinalIgnoreCase) ||
			filename.Contains(".com", StringComparison.OrdinalIgnoreCase) ||
			filename.Contains(".bat", StringComparison.OrdinalIgnoreCase) ||
			filename.Contains(".dll", StringComparison.OrdinalIgnoreCase) ||
			filename.Contains(".ini", StringComparison.OrdinalIgnoreCase) ||
			filename.Contains(".gcf", StringComparison.OrdinalIgnoreCase) ||
			filename.Contains(".sys", StringComparison.OrdinalIgnoreCase) ||
			filename.Contains(".blob", StringComparison.OrdinalIgnoreCase)
		)
			return false;

		return true;
	}

	[ConCommand(helpText: "Execute script file.")]
	void exec(in TokenizedCommand args) {
		lock (Cbuf.Buffer) {
			int argc = args.ArgC();
			if (argc != 2) {
				Dbg.ConMsg("exec <filename>: execute a script file\n");
				return;
			}

			ReadOnlySpan<char> file = $"cfg/{args[1]}";
			ReadOnlySpan<char> pathID = "MOD";

			if (file.GetFileExtension().IsEmpty)
				file = $"{file}.cfg";

			if (!Common.IsValidPath(file)) {
				Dbg.ConMsg("exec %s: invalid path.\n");
				return;
			}

			if (!IsValidFileExtension(file)) {
				Dbg.ConMsg("exec %s: invalid file type.\n");
				return;
			}

			if (true) {
				if (fileSystem.FileExists(file, "MOD")) {
					long size = fileSystem.Size(file);
					if (size > 1 * 1024 * 1024) {
						Dbg.ConMsg($"exec {file}: file size larger than 1 MiB!\n");
						return;
					}
				}
				else {
					Dbg.ConMsg($"'{file}' not present; not executing.\n");
					return;
				}
			}

			byte[] buf;
			using (IFileHandle? execFile = fileSystem.Open(file, FileOpenOptions.Read, pathID)) {
				if (execFile == null) {
					Dbg.ConMsg($"exec: couldn't exec {args[1]}\n");
					return;
				}

				buf = new byte[execFile.Stream.Length];
				execFile.Stream.ReadExactly(buf);
			}

			using StreamReader reader = new StreamReader(new MemoryStream(buf, false));
			Dbg.ConDMsg($"execing {file}\n");
			LinkedListNode<CommandBuffer.Command>? hCommand = Cbuf.Buffer.GetNextCommandHandle();
			// check to make sure we're not going to overflow later
			while (!reader.EndOfStream) {
				string? line = reader.ReadLine().Trim() + "\0";
				if (line == null)
					break;

				Cbuf.InsertText(line);

				while (Cbuf.Buffer.GetNextCommandHandle() != hCommand) {
					if (Cbuf.Buffer.DequeueNextCommand()) {
						Cbuf.ExecuteCommand(ref Cbuf.Buffer.GetCommand(), CommandSource.Command);
					}
					else {
						Dbg.Assert(false);
						break;
					}
				}
			}
		}
	}

	private ReadOnlySpan<char> TranslateFileAssociation(string v) {
		// do later!
		return null;
	}
}
