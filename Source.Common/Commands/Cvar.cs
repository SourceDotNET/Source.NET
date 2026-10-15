using CommunityToolkit.HighPerformance;

using Microsoft.Extensions.DependencyInjection;

using Source.Common.Client;
using Source.Common.Server;
using Source.Common.Utilities;

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Source.Common.Commands;

public class Cvar : ICvar
{
	readonly ICommandLine CommandLine;
	readonly IServiceProvider services;
	readonly ICvarQuery cvarQuery;
	public Cvar(ICommandLine CommandLine, IServiceProvider services, ICvarQuery cvarQuery) {
		this.CommandLine = CommandLine;
		this.services = services;
		this.cvarQuery = cvarQuery;
	}
	public event FnChangeCallback? Changed;
	public void CallGlobalChangeCallbacks(ConVar var, in ConVarChangeContext ctx) => Changed?.Invoke(var, in ctx);
	List<IConsoleDisplayFunc> DisplayFuncs = [];
	Assembly? NextDLLIdentifier;
	
	// Linked list of concommands
	ConCommandBase? ConCommandList;
	private readonly ConcurrentDictionary<UtlSymId_t, ConCommandBase> StringToConCommand = [];
	private readonly ConcurrentDictionary<ConCommandBase, UtlSymId_t> ConCommandToString = [];

	public void NotifyConCommandNameChanged(ConCommandBase cmd, ReadOnlySpan<char> name) {
		// Remove the old name.
		if(ConCommandToString.TryGetValue(cmd, out UtlSymId_t oldName))
			StringToConCommand.Remove(oldName, out _);

		UtlSymId_t newName = name.Hash();
		StringToConCommand[newName] = cmd;
		ConCommandToString[cmd] = newName;
	}
	
	IBaseClientDLL? clientDLL;
	IServerGameDLL? serverDLL;

	FCvar assemblyFlags = FCvar.None;
	public void SetAssemblyIdentifier(Assembly? assembly) {
		NextDLLIdentifier = assembly;

		// Pull in dependencies if they weren't resolved already
		// done independently from ctor to avoid any headaches in the client/game dlls
		clientDLL ??= services.GetService<IBaseClientDLL>();
		serverDLL ??= services.GetService<IServerGameDLL>();
		assemblyFlags = FCvar.None;

		if (assembly == null) 
			return;

		if (clientDLL != null && assembly == clientDLL.GetType().Assembly)
			assemblyFlags |= FCvar.ClientDLL;
		else if (serverDLL != null && assembly == serverDLL.GetType().Assembly)
			assemblyFlags |= FCvar.GameDLL;
	}

	public void ConsoleColorPrintf(in Color clr, ReadOnlySpan<char> format, params object?[]? args) {
		if (DisplayFuncs.Count == 0) {
			TempConsoleBuffer.EnsureCapacity(TempConsoleBuffer.Count + format.Length + 3);

			TempConsoleBuffer.Add((char)CONSOLE_COLOR_PRINT);
			ReadOnlySpan<char> cast = MemoryMarshal.Cast<int, char>([clr.GetRawColor()]);
			TempConsoleBuffer.Add(cast[0]);
			TempConsoleBuffer.Add(cast[1]);
			for (int i = 0; i < format.Length; i++)
				TempConsoleBuffer.Add(format[i]);
		}

		foreach (var func in DisplayFuncs)
			func.ColorPrint(in clr, format);
	}

	public void ConsoleDPrintf(ReadOnlySpan<char> format, params object?[]? args) {
		if (DisplayFuncs.Count == 0) {
			TempConsoleBuffer.EnsureCapacity(TempConsoleBuffer.Count + format.Length + 1);

			TempConsoleBuffer.Add((char)CONSOLE_DPRINT);
			for (int i = 0; i < format.Length; i++)
				TempConsoleBuffer.Add(format[i]);
		}

		foreach (var func in DisplayFuncs)
			func.DPrint(format);
	}

	public void ConsolePrintf(ReadOnlySpan<char> format, params object?[]? args) {
		if (DisplayFuncs.Count == 0) {
			TempConsoleBuffer.EnsureCapacity(TempConsoleBuffer.Count + format.Length + 1);

			TempConsoleBuffer.Add((char)CONSOLE_PRINT);
			for (int i = 0; i < format.Length; i++)
				TempConsoleBuffer.Add(format[i]);
		}

		foreach (var func in DisplayFuncs)
			func.Print(format);
	}

	public ConCommand? FindCommand(ReadOnlySpan<char> name) {
		ConCommandBase? var = FindCommandBase(name);
		if (var == null || !var.IsCommand())
			return null;

		return (ConCommand)var!;
	}

	public ConCommandBase? FindCommandBase(ReadOnlySpan<char> name) {
		return StringToConCommand.GetValueOrDefault(name.Hash());
	}

	public ConVar? FindVar(ReadOnlySpan<char> name) {
		ConCommandBase? var = FindCommandBase(name);
		if (var == null || var.IsCommand())
			return null;

		return (ConVar)var!;
	}

	public string? GetCommandLineValue(ReadOnlySpan<char> variableName) {
		return CommandLine.ParmValue($"+{variableName}", null);
	}

	public IEnumerable<ConCommandBase> GetCommands() {
		ConCommandBase? command = ConCommandList;
		while (command != null) {
			yield return command;
			command = command.Next;
		}
	}

	public void InstallConsoleDisplayFunc(IConsoleDisplayFunc displayFunc) {
		DisplayFuncs.Add(displayFunc);
		DisplayQueuedMessages();
	}

	private readonly List<char> TempConsoleBuffer = new List<char>(1024);

	const int CONSOLE_COLOR_PRINT = 0;
	const int CONSOLE_PRINT = 1;
	const int CONSOLE_DPRINT = 2;

	private void DisplayQueuedMessages() {
		Color clr = new();
		Span<char> tempConsoleBuffer = TempConsoleBuffer.AsSpan();
		while (tempConsoleBuffer.Length > 0) {
			int nType = tempConsoleBuffer[0]; tempConsoleBuffer = tempConsoleBuffer[1..];
			if (nType == CONSOLE_COLOR_PRINT) {
				clr.SetRawColor(MemoryMarshal.Cast<char, int>(tempConsoleBuffer)[0]);
				tempConsoleBuffer = tempConsoleBuffer[2..];
			}
			ReadOnlySpan<char> temp = tempConsoleBuffer.SliceNullTerminatedString();

			switch (nType) {
				case CONSOLE_COLOR_PRINT:
					ConsoleColorPrintf(clr, temp);
					break;

				case CONSOLE_PRINT:
					ConsolePrintf(temp);
					break;

				case CONSOLE_DPRINT:
					ConsoleDPrintf(temp);
					break;
			}

			tempConsoleBuffer = tempConsoleBuffer[temp.Length..];
		}

		TempConsoleBuffer.Clear();
	}

	public void RegisterConCommand(ConCommandBase variable) {
		if (variable.IsRegistered())
			return;

		variable.Registered = true;

		string name = variable.GetName();
		if (string.IsNullOrEmpty(name)) {
			variable.Next = null;
			return;
		}

		variable.Flags |= assemblyFlags;

		ConCommandBase? other = FindVar(variable.GetName());
		if (other != null) {
			if (variable.IsCommand() || other.IsCommand()) {
				Dbg.Warning($"WARNING: unable to link {variable.GetName()} and {other.GetName()} because one or more is a ConCommand.\n");
			}
			else {
				ConVar childVar = (ConVar)variable;
				ConVar parentVar = (ConVar)other;

				if (cvarQuery.AreConVarsLinkable(childVar, parentVar)) {
					if (childVar.defaultValue != null && parentVar.defaultValue != null && childVar.IsFlagSet(FCvar.Replicated) && parentVar.IsFlagSet(FCvar.Replicated)) {
						if (!childVar.defaultValue.Equals(parentVar.defaultValue, StringComparison.OrdinalIgnoreCase))
							Dbg.Warning($"Parent and child ConVars with different default values! " +
								$"{childVar.defaultValue} child, {childVar.defaultValue} parent (parent wins)\n");
					}

					childVar.parent = parentVar.parent;
					parentVar.Flags |= childVar.Flags & (FCvar.AccessibleFromThreads);
					if (childVar.HasChangeCallback) {
						if (!parentVar.HasChangeCallback)
							parentVar.SyncChangeTo(childVar);
					}

					if (!string.IsNullOrEmpty(childVar.HelpString)) {
						if (!string.IsNullOrEmpty(parentVar.HelpString)) {
							if (!parentVar.HelpString.Equals(childVar.HelpString, StringComparison.OrdinalIgnoreCase))
								Dbg.Warning($"Convar {variable.GetName()} has multiple help strings (parent wins)\n");
						}
						else {
							parentVar.HelpString = childVar.HelpString;
						}
					}

					if ((childVar.Flags & FCvar.Cheat) != (parentVar.Flags & FCvar.Cheat))
						Dbg.Warning($"Convar {variable.GetName()} has conflicting Cheat flags (parent wins)\n");
					if ((childVar.Flags & FCvar.Replicated) != (parentVar.Flags & FCvar.Replicated))
						Dbg.Warning($"Convar {variable.GetName()} has conflicting Replicated flags (parent wins)\n");
				}
			}

			variable.Next = null;
			return;
		}

		if (!variable.IsFlagSet(FCvar.Unregistered)) {
			variable.Next = ConCommandList;
			ConCommandList = variable;
		}

		NotifyConCommandNameChanged(variable, variable.GetName());
		variable.Assembly = NextDLLIdentifier;
	}

	public void RemoveConsoleDisplayFunc(IConsoleDisplayFunc displayFunc) {
		DisplayFuncs.Remove(displayFunc);
	}

	public void UnregisterConCommand(ConCommandBase commandToRemove) {
		if (!commandToRemove.IsRegistered())
			return;

		commandToRemove.Registered = false;
		
		ConCommandBase? prev = null;
		for (ConCommandBase? command = ConCommandList; command != null; command = command.Next) {
			if (command != commandToRemove) {
				prev = command;
				continue;
			}

			if (prev == null)
				ConCommandList = command.Next;
			else
				prev.Next = command.Next;

			command.Next = null;
			break;
		}
		
		if (ConCommandToString.TryGetValue(commandToRemove, out UtlSymId_t oldName)) {
			StringToConCommand.Remove(oldName, out _);
			ConCommandToString.Remove(commandToRemove, out _);
		}
	}

	public void UnregisterConCommands(Assembly sourceAssembly) {
		ConCommandBase? newList = null;
		ConCommandBase? command = ConCommandList;
		while (command != null) {
			ConCommandBase? next = command.Next;
			if (command.Assembly != sourceAssembly) {
				command.Next = newList;
				newList = command;
			}
			else {
				command.Registered = false;
				command.Next = null;
			}

			if (ConCommandToString.TryGetValue(command, out UtlSymId_t oldName)) {
				StringToConCommand.Remove(oldName, out _);
				ConCommandToString.Remove(command, out _);
			}

			command = next;
		}

		ConCommandList = newList;
	}

	[ConCommand(helpText: "Show the list of convars/concommands.")]
	void cvarlist(in TokenizedCommand args) {
		ConMsg("cvar list\n--------------\n");
		int count = 0;
		ConCommandBase? command = ConCommandList;
		List<ConCommandBase> cmds = [];

		while (command != null) {
			cmds.Add(command);
			command = command.Next;
		}

		if (args.ArgC() == 2) {
			string filter = args[1].ToString();
			cmds = [.. cmds.Where(cmd => cmd.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase))];
		}

		cmds.Sort((x, y) => x.Name.CompareTo(y.Name));
		foreach (var cmd in cmds) {
			if (cmd.IsCommand())
				PrintCommand((ConCommand)cmd);
			else
				PrintCvar((ConVar)cmd);
			count++;
		}

		ConMsg($"--------------\n{count} total convars/concommands\n");
	}

	[ConCommand(helpText: "Find help about a convar/concommand.")]
	void help(in TokenizedCommand args) {
		if (args.ArgC() != 2) {
			Dbg.ConMsg("Usage:  help <cvarname>\n");
			return;
		}

		ConCommandBase? var = FindCommandBase(args[1]);
		if (var == null) {
			Dbg.ConMsg($"help:  no cvar or command named {args[1]}\n");
			return;
		}

		ConVar.PrintDescription(var);
	}

	[ConCommand(helpText: "Find concommands with the specified string in their name/help text.")]
	void find(in TokenizedCommand args) {
		if (args.ArgC() != 2) {
			Dbg.ConMsg("Usage:  find <string>\n");
			return;
		}

		ReadOnlySpan<char> search = args[1];

		foreach (var var in GetCommands()) {
			if (var.IsFlagSet(FCvar.DevelopmentOnly) || var.IsFlagSet(FCvar.Hidden))
				continue;

			if (!var.GetName().Contains(search, StringComparison.OrdinalIgnoreCase) && !var.GetHelpText().Contains(search, StringComparison.OrdinalIgnoreCase))
				continue;

			ConVar.PrintDescription(var);
		}
	}

	struct ConVarFlagsDesc
	{
		public FCvar bit;
		public string desc;
		public string shortdesc;
		public ConVarFlagsDesc(FCvar bit, string desc, string shortdesc) {
			this.bit = bit;
			this.desc = desc;
			this.shortdesc = shortdesc;
		}
	}
	static ConVarFlagsDesc[] conVarFlags =
	{
		new(FCvar.Archive,  "ARCHIVE", "a" ),
		new(FCvar.SingleplayerOnly,  "SPONLY", "sp" ),
		new(FCvar.GameDLL,  "GAMEDLL", "sv" ),
		new(FCvar.Cheat,  "CHEAT", "cheat" ),
		new(FCvar.UserInfo,  "USERINFO", "user" ),
		new(FCvar.Notify,  "NOTIFY", "nf" ),
		new(FCvar.Protected,  "PROTECTED", "prot" ),
		new(FCvar.PrintableOnly,  "PRINTABLEONLY", "print" ),
		new(FCvar.Unlogged,  "UNLOGGED", "log" ),
		new(FCvar.NeverAsString,  "NEVER_AS_STRING", "numeric" ),
		new(FCvar.Replicated,  "REPLICATED", "rep" ),
		new(FCvar.Demo,  "DEMO", "demo" ),
		new(FCvar.DontRecord,  "DONTRECORD", "norecord" ),
		new(FCvar.ServerCanExecute,  "SERVER_CAN_EXECUTE", "server_can_execute" ),
		new(FCvar.ClientCmdCanExecute,  "CLIENTCMD_CAN_EXECUTE", "clientcmd_can_execute" ),
		new(FCvar.ClientDLL, "CLIENTDLL", "cl" ),
	};

	private void PrintCvar(ConVar var) {
		string[] flagstr = new string[conVarFlags.Length];
		int i = 0;
		foreach (var entry in conVarFlags) {
			if (var.IsFlagSet(entry.bit)) {
				flagstr[i++] = entry.shortdesc;
			}
		}
		string flagstrF = string.Join(", ", flagstr[0..i]);
		string valstr;
		if (var.GetInt() == Convert.ToInt32(var.GetDouble()))
			valstr = $"{var.GetInt()}";
		else
			valstr = $"{Math.Round(var.GetDouble(), 3)}";

		Dbg.ConMsg($"{var.Name.PadRight(40)} : {valstr.PadRight(8)} : {flagstrF.PadRight(16)} : {var.GetHelpText()?.Replace("\t", "")?.Replace("\n", "")}\n");
	}

	private void PrintCommand(ConCommand cmd) {
		Dbg.ConMsg($"{cmd.Name.PadRight(40)} : {"cmd".PadRight(8)} : {"".PadRight(16)} : {cmd.GetHelpText()?.Replace("\t", "")?.Replace("\n", "")}\n");
	}

	public void RevertFlaggedConVars(FCvar flag) {
		foreach (var var in GetCommands()) {
			if (var.IsCommand())
				continue;

			ConVar cvar = (ConVar)var;
			if (!cvar.IsFlagSet(flag))
				continue;

			if (cvar.GetString().Equals(cvar.GetDefault(), StringComparison.OrdinalIgnoreCase))
				continue;

			cvar.Revert();
		}
	}

	public void Shutdown() {

	}
}
