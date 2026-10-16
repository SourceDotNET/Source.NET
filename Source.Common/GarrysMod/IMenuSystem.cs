using Source.Common.Engine;
using Source.Common.GarrysMod.Lua;

namespace Source.Common.GarrysMod;

public interface IMenuSystem
{
	int Init(IServiceProvider services, IGet get, IGarrysMod gmod, GlobalVarsBase vars);
	void Shutdown();
	void SetupNetworkString(INetworkStringTableContainer networkStringTableContainer);
	void Think();
	void StartLua();
	void ServerDetails(ReadOnlySpan<char> serverName, ReadOnlySpan<char> loadingURL, ReadOnlySpan<char> mapName, byte maxPlayers, short playerSlots, ReadOnlySpan<char> gamemode);
	void OnLuaError(in LuaError err, IAddonSystem.Information? addonInfo);
	void SendProblemToMenu(ReadOnlySpan<char> id, int severity, ReadOnlySpan<char> parms);
	bool IsServerBlacklisted(ReadOnlySpan<char> address, ReadOnlySpan<char> hostname, ReadOnlySpan<char> description, ReadOnlySpan<char> gamemode, ReadOnlySpan<char> map);
}
