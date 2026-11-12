using Source.Common.Commands;

namespace Source.Common.GarrysMod.Lua;

public interface ILuaConVars
{
	void Init();
	ConVar CreateConVar(ReadOnlySpan<char> name, ReadOnlySpan<char> defaultValue, ReadOnlySpan<char> helpString, int flags);
	ConCommand CreateConCommand(ReadOnlySpan<char> name, ReadOnlySpan<char> helpString, int flags, FnCommandCallback? callback, FnCommandCompletionCallback? completionFunc);
	void DestroyManaged();
	void Cache(ReadOnlySpan<char> name, ReadOnlySpan<char> value);
	void ClearCache();
	void SaveManagedConVars();
}
