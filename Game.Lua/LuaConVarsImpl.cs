using Source.Common.Commands;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod.Lua;

using System.Globalization;

namespace Game.Lua;

public class LuaConVarsImpl : ILuaConVars
{
	record struct ManagedCommand(ConCommandBase Command, bool IsConVar, bool Archive);

	readonly List<ManagedCommand> Managed = [];
	KeyValues? ClientCVars;
	KeyValues? ServerCVars;

	readonly SortedDictionary<string, string> CachedValues = new(StringComparer.Ordinal);

	public void Cache(ReadOnlySpan<char> name, ReadOnlySpan<char> value) {
		CachedValues[name.ToString()] = value.ToString();
	}

	public void ClearCache() {
		CachedValues.Clear();
	}

	public ConCommand CreateConCommand(ReadOnlySpan<char> name, ReadOnlySpan<char> helpString, int flags, FnCommandCallback? callback, FnCommandCompletionCallback? completionFunc) {
		bool lua = (flags & (int)(FCvar.LuaClient | FCvar.LuaServer)) != 0;
		ConCommand command = new(name.ToString(), callback!, helpString.ToString(), (FCvar)flags, completionFunc);
		cvar.SetAssemblyIdentifier(typeof(LuaConVarsImpl).Assembly);
		cvar.RegisterConCommand(command);
		if (lua)
			Managed.Add(new(command, false, false));
		return command;
	}

	public ConVar CreateConVar(ReadOnlySpan<char> name, ReadOnlySpan<char> defaultValue, ReadOnlySpan<char> helpString, int flags) {
		bool archive = (flags & (int)FCvar.Archive) != 0;
		bool replicated = (flags & (int)FCvar.Replicated) != 0;
		bool lua = (flags & (int)(FCvar.LuaClient | FCvar.LuaServer)) != 0;

		ConVar convar = new(name.ToString(), defaultValue.ToString(), (FCvar)(flags & unchecked((int)0xFF4FFF7F)), helpString.ToString());
		cvar.SetAssemblyIdentifier(typeof(LuaConVarsImpl).Assembly);
		cvar.RegisterConCommand(convar);
		if (lua)
			Managed.Add(new(convar, true, archive));

		if (archive) {
			string? value = null;
			if ((flags & (int)FCvar.LuaClient) != 0)
				value = ClientCVars!.FindKey(name) != null ? ClientCVars.GetString(name).ToString() : null;
			if ((flags & (int)FCvar.LuaServer) != 0)
				value = ServerCVars!.FindKey(name) != null ? ServerCVars.GetString(name).ToString() : null;
			if (value != null)
				convar.SetValue(value);
		}

		if (replicated && CachedValues.Remove(name.ToString(), out string? cached))
			convar.SetValue(cached);

		return convar;
	}

	public void DestroyManaged() {
		SaveManagedConVars();

		foreach (ManagedCommand managed in Managed)
			cvar.UnregisterConCommand(managed.Command);

		Managed.Clear();
	}

	public void SaveManagedConVars() {
		foreach (ManagedCommand managed in Managed) {
			if (!managed.IsConVar || !managed.Archive)
				continue;

			ConVar convar = (ConVar)managed.Command;
			if (convar.IsFlagSet(FCvar.LuaClient))
				ClientCVars!.SetString(convar.GetName(), GetSavedValue(convar));

			if (convar.IsFlagSet(FCvar.LuaServer))
				ServerCVars!.SetString(convar.GetName(), GetSavedValue(convar));
		}

		if (!ClientCVars!.IsEmpty())
			ClientCVars.WriteToFile(filesystem, "cfg/client.vdf", "MOD");

		if (!ServerCVars!.IsEmpty())
			ServerCVars.WriteToFile(filesystem, "cfg/server.vdf", "MOD");
	}

	static string GetSavedValue(ConVar convar) {
		if (convar.IsFlagSet(FCvar.NeverAsString))
			return ((float)convar.GetDouble()).ToString("F6", CultureInfo.InvariantCulture);

		return convar.GetString();
	}

	public void Init() {
		ClientCVars = new KeyValues("CVars");
		ServerCVars = new KeyValues("CVars");
		ClientCVars.LoadFromFile(filesystem, "cfg/client.vdf", "MOD");
		ServerCVars.LoadFromFile(filesystem, "cfg/server.vdf", "MOD");
	}
}
