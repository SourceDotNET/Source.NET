
using SharpCompress.Common;

using Source.Common.Bitbuffers;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.Keyvalues;

using Steamworks;

using System.Collections.Generic;
using System.Drawing;
using System.Numerics;

namespace Source.Common.Server;


/// <summary>
/// Interface the game DLL exposes to the engine
/// </summary>
public interface IServerGameDLL
{
	bool DLLInit(IServiceProvider services);

	// This is called when a new game is started. (restart, map)
	bool GameInit();

	// Called any time a new level is started (after GameInit() also on level transitions within a game)
	bool LevelInit(ReadOnlySpan<char> pMapName,
									ReadOnlyMemory<byte> pMapEntities, ReadOnlySpan<char> pOldLevel,
									ReadOnlySpan<char> pLandmarkName, bool loadGame, bool background);

	// The server is about to activate
	void ServerActivate(Edict[] pEdictList, int edictCount, int clientMax);

	// The server should run physics/think on all edicts
	void GameFrame(bool simulating);

	// Called once per simulation frame on the final tick
	void PreClientUpdate(bool simulating);

	// Called when a level is shutdown (including changing levels)
	void LevelShutdown();
	// This is called when a game ends (server disconnect, death, restart, load)
	// NOT on level transitions within a game
	void GameShutdown();

	// Called once during DLL shutdown
	void DLLShutdown();

	// Get the simulation interval (must be compiled with identical values into both client and game .dll for MOD!!!)
	// Right now this is only requested at server startup time so it can't be changed on the fly, etc.
	TimeUnit_t GetTickInterval();

	// Give the list of datatable classes to the engine.  The engine matches class names from here with
	//  edict_t::classname to figure out how to encode a class's data for networking
	ServerClass? GetAllServerClasses();

	// Returns string describing current .dll.  e.g., TeamFortress 2, Half-Life 2.  
	//  Hey, it's more descriptive than just the name of the game directory
	ReadOnlySpan<char> GetGameDescription();

	// Let the game .dll allocate it's own network/shared string tables
	void CreateNetworkStringTables();

	// TODO: Save/Restore


	// Build the list of maps adjacent to the current map
	void BuildAdjacentMapList();

	// Retrieve info needed for parsing the specified user message
	bool GetUserMessageInfo(int msg_type, Span<char> name, out int size);

	StandardSendProxies GetStandardSendProxies();


	// Called once during startup, after the game .dll has been loaded and after the client .dll has also been loaded
	void PostInit();
	// Called once per frame even when no level is loaded...
	void Think(bool finalTick);
	void PreSaveGameLoaded(ReadOnlySpan<char> pSaveName, bool bCurrentlyInGame);

	// Returns true if the game DLL wants the server not to be made public.
	// Used by commentary system to hide multiplayer commentary servers from the master.
	bool ShouldHideServer();

	void InvalidateMdlCache();

	// * This function is new with version 6 of the interface.
	//
	// This is called when a query from IServerPluginHelpers::StartQueryCvarValue is finished.
	// iCookie is the value returned by IServerPluginHelpers::StartQueryCvarValue.
	// Added with version 2 of the interface.
	// OnQueryCvarValueFinished todo

	// Called after the steam API has been activated post-level startup
	void GameServerSteamAPIActivated();

	// Called after the steam API has been shutdown post-level startup
	void GameServerSteamAPIShutdown();

	void SetServerHibernation(bool bHibernating);

	// Return override string to show in the server browser
	// "map" column, or NULL to just use the default value
	// (the map name)
	ReadOnlySpan<char> GetServerBrowserMapOverride();

	// Get gamedata string to send to the master serer updater.
	ReadOnlySpan<char> GetServerBrowserGameData();

	void PrepareLevelResources(Span<char> mapName, Span<char> mapFile);
	PrepareLevelResourcesResult AsyncPrepareLevelResources(Span<char> mapName, Span<char> mapFile, out float progress);

	bool GMOD_CheckPassword(CSteamID steamID, ReadOnlySpan<char> ipAddress, ReadOnlySpan<char> serverPassword, ReadOnlySpan<char> clientPassword, ReadOnlySpan<char> name, Span<char> rejectionMessage);
	void GMOD_ClientSignOnStateChanged(int userID, int oldState, int newState);
	void GMOD_OnAllSoundsStoppedSV();
	void GMOD_OnStringTablesRemoved();
}

public enum PrepareLevelResourcesResult
{
	Prepared,
	InProgress
}

/// <summary>
/// Interface to get at server entities
/// </summary>
public interface IServerGameEnts
{
	void SetDebugEdictBase(Edict[] edict);
	void MarkEntitiesAsTouching(Edict e1, Edict e2);
	void FreeContainingEntity(Edict e);
	void CheckTransmit(CheckTransmitInfo info, ushort[] ushorts, int numValidEntities);
}

/// <summary>
/// Player/client related functions
/// </summary>
public interface IServerGameClients
{
	void GetPlayerLimits(out int minPlayers, out int maxPlayers, out int defaultMaxPlayers);
	bool ClientConnect(Edict entity, ReadOnlySpan<char> name, ReadOnlySpan<char> address, Span<char> reject);
	void ClientActive(Edict entity, bool loadGame);
	void ClientDisconnect(Edict entity);
	void ClientPutInServer(Edict entity, ReadOnlySpan<char> playerName);
	void ClientCommand(Edict entity, in TokenizedCommand args);
	void SetCommandClient(int index);
	void ClientSettingsChanged(Edict edict);
	void ClientSetupVisibility(Edict? viewEntity, Edict client, byte[] pvs, int pvssize);
	TimeUnit_t ProcessUsercmds(Edict player, bf_read buf, int numCmds, int totalCmds, int droppedPackets, bool ignore, bool paused);
	PlayerEngineState GetPlayerState(Edict player);
	void ClientEarPosition(Edict entity, out Vector3 earOrigin);
	void GetBugReportInfo(Span<char> buf);
	void NetworkIDValidated(ReadOnlySpan<char> userName, ReadOnlySpan<char> networkID);
	void ClientCommandKeyValues(Edict entity, KeyValues keyValues);
	void ClientSpawned(Edict player);

	void GMOD_ReceiveClientMessage(int userID, Edict player, bf_read msg, int bits);
	void GMOD_ClientConnected(int userID);
	void GMOD_SentClientStringTables(int userID);
}
