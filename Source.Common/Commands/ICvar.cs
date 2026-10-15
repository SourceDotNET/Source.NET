using System.Reflection;

namespace Source.Common.Commands;

public delegate void FnChangeCallback(IConVar var, in ConVarChangeContext ctx);
public delegate void FnChangeCallbackSrc(IConVar var, ReadOnlySpan<char> oldString, float oldFloat);

// todo write a better summary later
/// <summary>
/// Marks a field/property as being a reference to a convar rather than a convar that the engine is supposed to register.
/// By default, all class fields with the <see cref="ConVar"/> type are pulled into the engine. This attribute can suppress that.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class CvarIgnoreAttribute : Attribute;

public interface IConsoleDisplayFunc
{
	public void ColorPrint(in Color clr, ReadOnlySpan<char> message);
	public void Print(ReadOnlySpan<char> message);
	public void DPrint(ReadOnlySpan<char> message);
}

public interface ICvarQuery
{
	public bool AreConVarsLinkable(ConVar child, ConVar parent);
}

public interface ICvar
{
	void RegisterConCommand(ConCommandBase commandBase);
	void UnregisterConCommand(ConCommandBase commandBase);
	void UnregisterConCommands(Assembly sourceAssembly);

	void SetAssemblyIdentifier(Assembly assembly);
	
	string? GetCommandLineValue(ReadOnlySpan<char> variableName);

	ConCommandBase? FindCommandBase(ReadOnlySpan<char> name);
	ConVar? FindVar(ReadOnlySpan<char> name);
	ConCommand? FindCommand(ReadOnlySpan<char> name);

	IEnumerable<ConCommandBase> GetCommands();

	event FnChangeCallback? Changed;
	void CallGlobalChangeCallbacks(ConVar var, in ConVarChangeContext ctx);

	void InstallConsoleDisplayFunc(IConsoleDisplayFunc displayFunc);
	void RemoveConsoleDisplayFunc(IConsoleDisplayFunc displayFunc);

	void ConsoleColorPrintf(in Color clr, ReadOnlySpan<char> format, params object?[]? args);
	void ConsolePrintf(ReadOnlySpan<char> format, params object?[]? args);
	void ConsoleDPrintf(ReadOnlySpan<char> format, params object?[]? args);

	void RevertFlaggedConVars(FCvar flag);

	void NotifyConCommandNameChanged(ConCommandBase cmd, ReadOnlySpan<char> name);
}
