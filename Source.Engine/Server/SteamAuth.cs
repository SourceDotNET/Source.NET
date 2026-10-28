#define USE_GS_AUTH_API
global using static Source.Engine.Server.Steam3ServerAccessor;

using Source.Common;
using Source.Common.Commands;
using Source.Common.Networking;
using Source.Common.Server;
using Source.Common.Steam;

using Steamworks;

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Source.Engine.Server;

public enum ServerType
{
	Normal,
	TVRelay
}

[EngineComponent]
public static class Steam3ServerAccessor
{
	[Dependency] static Steam3ServerImpl _server = null!;
	public static Steam3ServerImpl Steam3Server() => _server;
}

[EngineComponent]
public class Steam3ServerImpl : IDisposable
{
	readonly ICommandLine CommandLine;
	readonly Net Net;

	// These exist as wrappers to the static methods
	// (kind of a stupid way to implement this, but its easiest right now
	readonly ISteamClient __SteamClient = new ImplSteamClient();
	readonly ISteamGameServer __SteamGameServer = new ImplSteamGameServer();
	readonly ISteamUtils __SteamUtils = new ImplSteamUtils();
	readonly ISteamNetworking __SteamNetworking = new ImplSteamNetworking();
	readonly ISteamGameServerStats __SteamGameServerStats = new ImplSteamGameServerStats();
	readonly ISteamHTTP __SteamHTTP = new ImplSteamHTTP();
	readonly ISteamInventory __SteamInventory = new ImplSteamInventory();
	readonly ISteamUGC __SteamUGC = new ImplSteamUGC();

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamClient SteamClient() => HasSteamGameServer() ? __SteamClient : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamGameServer SteamGameServer() => HasSteamGameServer() ? __SteamGameServer : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamUtils SteamGameServerUtils() => HasSteamGameServer() ? __SteamUtils : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamNetworking SteamGameServerNetworking() => HasSteamGameServer() ? __SteamNetworking : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamGameServerStats SteamGameServerStats() => HasSteamGameServer() ? __SteamGameServerStats : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamHTTP SteamHTTP() => HasSteamGameServer() ? __SteamHTTP : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamInventory SteamInventory() => HasSteamGameServer() ? __SteamInventory : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamUGC SteamUGC() => HasSteamGameServer() ? __SteamUGC : null!;

	public Steam3ServerImpl(ICommandLine commandLine, Net net) {
		CommandLine = commandLine;
		Net = net;

		HasActivePlayers = false;
		LogOnResult = false;
		ServerMode = EServerMode.eServerModeInvalid;
		ServerType = ServerType.Normal;
		WantsSecure = false;
		Initialized = false;
		WantsPersistentAccountLogon = false;
		LogOnFinished = false;
		MasterServerUpdaterSharingGameSocket = false;
		steamIDLanOnly.InstancedSet((AccountID_t)0, 0, EUniverse.k_EUniversePublic, EAccountType.k_EAccountTypeInvalid);
		SteamIDGS.InstancedSet((AccountID_t)1, 0, EUniverse.k_EUniverseInvalid, EAccountType.k_EAccountTypeInvalid);
		QueryPort = 0;
	}

	EServerMode ServerMode;
	ServerType ServerType;

	bool MasterServerUpdaterSharingGameSocket;
	bool LogOnFinished;
	bool LoggedOn;
	bool LogOnResult;
	bool HasActivePlayers;
	CSteamID SteamIDGS;
	CSteamID steamIDLanOnly;
	bool Active;
	bool WantsSecure;
	bool Initialized;
	bool WantsPersistentAccountLogon;

	uint IP;
	ushort Port;
	ushort QueryPort;

	string? AccountToken;

	CSteamID SteamIDGroupForBlocking;

	public EServerMode GetCurrentServerMode() {
		if (SV.sv_lan.GetBool())
			return EServerMode.eServerModeNoAuthentication;
		else if (CommandLine.FindParm("-insecure") != 0)
			return EServerMode.eServerModeAuthentication;
		else
			return EServerMode.eServerModeAuthenticationAndSecure;
	}

	public bool BSecure() => SteamGameServer()?.BSecure() ?? false;
	public bool BIsActive() => SteamGameServer() != null && (ServerMode >= EServerMode.eServerModeNoAuthentication);
	public bool BLanOnly() => ServerMode == EServerMode.eServerModeNoAuthentication;
	public bool BWantsSecure() => ServerMode == EServerMode.eServerModeAuthenticationAndSecure;
	public bool BLoggedOn() => SteamGameServer()?.BLoggedOn() ?? false;

	public void Activate(ServerType serverType) {
		if (GetCurrentServerMode() == ServerMode && ServerType == serverType)
			return;

		if (BIsActive())
			Shutdown();

		IP = 0;
		Port = 26900;

		if (CommandLine.FindParm("-steamport") != 0)
			Port = (ushort)CommandLine.ParmValue("-steamport", 26900);

		ServerMode = GetCurrentServerMode();
		ServerType = serverType;

		ReadOnlySpan<char> gamedir = StrTools.FileBase(Common.Gamedir, stackalloc char[MAX_OSPATH]);

		ushort gamePort = 0;
		if (serverType == ServerType.Normal)
			gamePort = (ushort)Net.GetUDPPort(NetSocketType.Server);

		switch (ServerMode) {
			case EServerMode.eServerModeNoAuthentication:
				Msg("Initializing Steam libraries for LAN server\n");
				break;
			case EServerMode.eServerModeAuthentication:
				Msg("Initializing Steam libraries for INSECURE Internet server.  Authentication and VAC not requested.\n");
				break;
			case EServerMode.eServerModeAuthenticationAndSecure:
				Msg("Initializing Steam libraries for secure Internet server\n");
				break;
			default:
				Warning($"Bogus ServerMode {ServerMode}!\n");
				AssertMsg(false, "Bogus server mode?!");
				break;
		}

		Steamworks.GameServer.Init(IP, gamePort, (ushort)(Port + 1), ServerMode, GetSteamInfIDVersionInfo().PatchVersion);

		if (!Init()) {
			Assert(false);
			return;
		}

		if (sv.IsDedicated())
			SteamAPI_SetBreakpadAppID((uint)GetSteamInfIDVersionInfo().ServerAppID);

		SteamGameServer()!.SetProduct(GetSteamInfIDVersionInfo().ProductName);
		SteamGameServer()!.SetGameDescription(serverGameDLL.GetGameDescription());
		SteamGameServer()!.SetDedicatedServer(sv.IsDedicated());
		SteamGameServer()!.SetModDir(gamedir);

		if (AccountToken == null) {
			WantsPersistentAccountLogon = false;
			Msg("No account token specified; logging into anonymous game server account. (Use sv_setsteamaccount to login to a persistent account.)\n");
			SteamGameServer()!.LogOnAnonymous();
		}
		else {
			WantsPersistentAccountLogon = true;
			Msg("Logging into Steam game server account\n");

			// TODO: Change this to use just the token when the SDK is updated
			SteamGameServer()!.LogOn(AccountToken);
		}
	}
	protected Callback<SteamServersConnected_t> m_CallbackSteamServersConnected = null!;
	protected Callback<SteamServerConnectFailure_t> m_CallbackSteamServersConnectFailure = null!;
	protected Callback<SteamServersDisconnected_t> m_CallbackSteamServersDisconnected = null!;
	protected Callback<GSPolicyResponse_t> m_CallbackPolicyResponse = null!;
	protected Callback<ValidateAuthTicketResponse_t> m_CallbackGSAuthTicketResponse = null!;
	protected Callback<P2PSessionRequest_t> m_CallbackP2PSessionRequest = null!;
	protected Callback<P2PSessionConnectFail_t> m_CallbackP2PSessionConnectFail = null!;
	public bool Init() {
		m_CallbackSteamServersConnected = Callback<SteamServersConnected_t>.CreateGameServer(OnLogonSuccess);
		m_CallbackSteamServersConnectFailure = Callback<SteamServerConnectFailure_t>.CreateGameServer(OnLogonFailure);
		m_CallbackSteamServersDisconnected = Callback<SteamServersDisconnected_t>.CreateGameServer(OnLoggedOff);
		m_CallbackPolicyResponse = Callback<GSPolicyResponse_t>.CreateGameServer(OnPolicyResponse);
		m_CallbackGSAuthTicketResponse = Callback<ValidateAuthTicketResponse_t>.CreateGameServer(OnValidateAuthTicketResponse);
		m_CallbackP2PSessionRequest = Callback<P2PSessionRequest_t>.CreateGameServer(OnP2PSessionRequest);
		m_CallbackP2PSessionConnectFail = Callback<P2PSessionConnectFail_t>.CreateGameServer(OnP2PSessionConnectFail);

		return true;
	}
	Filter? _filter = null; Filter Filter => _filter ??= Singleton<Filter>();

	void OnLogonSuccess(SteamServersConnected_t logonSuccess) {
		if (!BIsActive())
			return;

		if (!LogOnResult)
			LogOnResult = true;

		if (!BLanOnly()) {
			Msg("Connection to Steam servers successful.\n");
			SteamIPAddress_t ip = SteamGameServer()!.GetPublicIP();
			Msg($"   Public IP is {ip.ToIPAddress()}\n");
		}

		if (SteamGameServer()!.GetSteamID().IsValid()) {
			SteamIDGS = SteamGameServer().GetSteamID();
			if (SteamIDGS.BAnonGameServerAccount())
				Msg($"Assigned anonymous gameserver Steam ID {SteamIDGS}.\n");
			else if (SteamIDGS.BPersistentGameServerAccount())
				Msg($"Assigned persistent gameserver Steam ID {SteamIDGS}.\n");
			else {
				Warning($"Assigned Steam ID {SteamIDGS}, which is of an unexpected type!\n");
				AssertMsg(false, "Unexpected steam ID type!");
			}

			if (CommandLine.FindParm("-p2p") != 0) {
				ConMsg("\n------------------------ ");
				ConColorMsg(new Color(249, 241, 165), "Steam P2P");
				ConMsg(" ------------------------\n");

				ConMsg("Run the following command in a client's console to connect:\n");
				ConMsg("    `connect p2p:0`\n"); // ToDo: Finish this!
				ConMsg("-----------------------------------------------------------\n");
			}
		}
		else
			SteamIDGS = CSteamID.NotInitYetGS;

		// send updated server details
		// OnLogonSuccess() gets called each time we logon, so if we get dropped this gets called
		// again and we get need to retell the AM our details
		SendUpdatedServerDetails();
	}
	void OnLogonFailure(SteamServerConnectFailure_t logonFailure) {
		if (!BIsActive())
			return;

		if (!LogOnResult) {
			if (logonFailure.m_eResult == EResult.k_EResultServiceUnavailable) {
				if (!BLanOnly())
					Msg("Connection to Steam servers successful (SU).\n");
			}
			else {
				if (!BLanOnly())
					Warning($"Could not establish connection to Steam servers.  (Result = {logonFailure.m_eResult})\n");
			}
		}

		LogOnResult = true;
	}
	void OnLoggedOff(SteamServersDisconnected_t pLoggedOff) {
		if (!BLanOnly()) {
			Warning($"Connection to Steam servers lost.  (Result = {pLoggedOff.m_eResult})\n");
		}
	}
	void OnPolicyResponse(GSPolicyResponse_t pPolicyResponse) {

	}

	static void sv_setsteamblockingcheck_f(IConVar pConVar, in ConVarChangeContext ctx) {
		if (SV.sv_lan.GetBool())
			Warning("Warning: sv_steamblockingcheck is not applicable in LAN mode.\n");
	}

	static readonly ConVar sv_steamblockingcheck = new("sv_steamblockingcheck", "0", 0, "Check each new player for Steam blocking compatibility, 1 = message only, 2 >= drop if any member of owning clan blocks,\n3 >= drop if any player has blocked, 4 >= drop if player has blocked anyone on server", callback: sv_setsteamblockingcheck_f);

	public BaseClient? ClientFindFromSteamID(in CSteamID steamIDFind) {
		for (int i = 0; i < sv.GetClientCount(); i++) {
			BaseClient cl = (BaseClient)sv.GetClient(i)!;

			if (!cl.IsConnected() || cl.IsFakeClient())
				continue;

			if (cl.GetNetworkID().IDType != IDType.Steam)
				continue;

			USERID id = cl.GetNetworkID();
			if (id.SteamID == steamIDFind)
				return cl;
		}
		return null;
	}

	void OnValidateAuthTicketResponse(ValidateAuthTicketResponse_t validateAuthTicketResponse) {
		if (!BIsActive())
			return;

		BaseClient? client = ClientFindFromSteamID(validateAuthTicketResponse.m_SteamID);
		if (client == null)
			return;

		if (validateAuthTicketResponse.m_eAuthSessionResponse != EAuthSessionResponse.k_EAuthSessionResponseOK) {
			OnValidateAuthTicketResponseHelper(client, validateAuthTicketResponse.m_eAuthSessionResponse);
			return;
		}

		if (Filter.IsUserBanned(client.GetNetworkID())) {
			sv.RejectConnection(client.GetNetChannel()!.GetRemoteAddress()!, client.GetClientChallenge(), "#GameUI_ServerRejectBanned");
			client.Disconnect($"STEAM UserID {client.GetNetworkIDString()} is banned");
		}
		else if (CheckForDuplicateSteamID(client))
			client.Disconnect($"STEAM UserID {client.GetNetworkIDString()} is already\nin use on this server");
		else {
			Span<char> msg = stackalloc char[512];
			sprintf(msg, "\"%s<%i><%s><>\" STEAM USERID validated\n").S(client.GetClientName()).I(client.GetUserID()).S(client.GetNetworkIDString());

			DevMsg(msg);
			SV.ServerGameClients!.NetworkIDValidated(client.GetClientName(), client.GetNetworkIDString());
		}

		if (sv_steamblockingcheck.GetInt() >= 1)
			SteamGameServer().ComputeNewPlayerCompatibility(validateAuthTicketResponse.m_SteamID);

		client.SetFullyAuthenticated();
	}

	public bool CheckForDuplicateSteamID(BaseClient client) {
		if (BLanOnly())
			return false;

		for (int i = 0; i < sv.GetClientCount(); i++) {
			IClient cl = sv.GetClient(i)!;

			if (!cl.IsConnected() || cl.IsFakeClient())
				continue;

			if (cl.GetNetworkID().IDType != IDType.Steam)
				continue;

			if (client == cl)
				continue;

			if (!CompareUserID(client.GetNetworkID(), cl.GetNetworkID()))
				continue;

			return true;
		}

		return false;
	}

	private void OnValidateAuthTicketResponseHelper(BaseClient client, EAuthSessionResponse authSessionResponse) {
		INetChannel netchan = client.GetNetChannel();

		if (netchan != null && netchan.IsTimingOut()) {
			client.Disconnect($"{client.GetClientName()} timed out");
			return;
		}

		WarningAndLog("STEAMAUTH: Client {client.GetClientName()} received failure code {(int)authSessionResponse)\n");

		switch (authSessionResponse) {
			case EAuthSessionResponse.k_EAuthSessionResponseUserNotConnectedToSteam:
				if (!BLanOnly())
					client.Disconnect("Client not connected to Steam\n");
				break;
			case EAuthSessionResponse.k_EAuthSessionResponseLoggedInElseWhere:
				if (!BLanOnly())
					client.Disconnect("This Steam account is being used in another location\n");
				break;
			case EAuthSessionResponse.k_EAuthSessionResponseNoLicenseOrExpired:
				client.Disconnect("This Steam account does not own this game. \nPlease login to the correct Steam account");
				break;
			case EAuthSessionResponse.k_EAuthSessionResponseVACBanned:
				if (!BLanOnly())
					client.Disconnect("VAC banned from secure server\n");
				break;
			case EAuthSessionResponse.k_EAuthSessionResponseAuthTicketCanceled:
				if (!BLanOnly())
					client.Disconnect("Client left game (Steam auth ticket has been canceled)\n");
				break;
			case EAuthSessionResponse.k_EAuthSessionResponseAuthTicketInvalidAlreadyUsed:
			case EAuthSessionResponse.k_EAuthSessionResponseAuthTicketInvalid:
				if (!BLanOnly())
					client.Disconnect("Invalid STEAM UserID Ticket\n");
				break;
			case EAuthSessionResponse.k_EAuthSessionResponseVACCheckTimedOut:
				client.Disconnect("An issue with your computer is blocking the VAC system. You cannot play on secure servers.\n\nhttps://support.steampowered.com/kb_article.php?ref=2117-ILZV-2837");
				break;
			default:
				client.Disconnect("Client dropped by server");
				break;
		}
	}

	private static void WarningAndLog(ReadOnlySpan<char> v) {
		Warning(v);
		Log(v);
	}

	void OnP2PSessionRequest(P2PSessionRequest_t pCallback) {

	}
	void OnP2PSessionConnectFail(P2PSessionConnectFail_t pCallback) {

	}
	public void SendUpdatedServerDetails() {
		if (!BIsActive())
			return;

		int nNumClients = sv.GetNumClients();
		int nMaxClients = sv.GetMaxClients();
		int nFakeClients = sv.GetNumFakeClients();

		for (int i = 0; i < sv.GetClientCount(); ++i) {
			BaseClient cl = (BaseClient)sv.GetClient(i)!;
			if (!cl.IsConnected())
				continue;

			bool hideClient = false;
			if (cl.IsReplay() || cl.IsHLTV()) {
				Assert(cl.IsFakeClient());
				hideClient = true;
			}

			if (cl.IsFakeClient() && !cl.ShouldReportThisFakeClient())
				hideClient = true;

			if (hideClient) {
				--nNumClients;
				--nMaxClients;
				--nFakeClients;

				if (cl.SteamID.IsValid()) {
					Assert(cl.SteamID.BAnonGameServerAccount());
					SteamGameServer().SendUserDisconnect_DEPRECATED(cl.SteamID);
					cl.SteamID = new();
				}
			}
		}

		// sv_visiblemaxplayers todo

		SteamGameServer().SetMaxPlayerCount(nMaxClients);
		SteamGameServer().SetBotPlayerCount(nFakeClients);
		SteamGameServer().SetPasswordProtected(!sv.GetPassword().IsEmpty);
		SteamGameServer().SetRegion(BaseServer.sv_region.GetString());
		SteamGameServer().SetServerName(sv.GetName());
		SteamGameServer().SetMapName(sv.GetMapName());

		SteamGameServer().SetSpectatorPort(0);

		// UpdateGroupSteamID(false);
	}

	public void Shutdown() {
		if (!BIsActive())
			return;

		Steamworks.GameServer.Shutdown();

		HasActivePlayers = false;
		LogOnResult = false;
		SteamIDGS = CSteamID.NotInitYetGS;
		ServerMode = EServerMode.eServerModeInvalid;

		Clear();
	}
	public void Clear() {

	}
	public void Dispose() {
		Shutdown();
		GC.SuppressFinalize(this);
	}

	internal bool CompareUserID(USERID id1, USERID id2) {
		if (id1.IDType != id2.IDType)
			return false;

		switch (id1.IDType) {
			case IDType.Steam:
			case IDType.Valve:
				return (id1.SteamID == id2.SteamID);
			default:
				break;
		}

		return false;
	}

	internal void NotifyClientDisconnect(BaseClient client) {
		if (client == null || !BIsActive() || !client.IsConnected() || !client.SteamID.IsValid())
			return;

		// Check if the client has a local (anonymous) steam account.  This is the
		// case for bots.  Currently it's also the case for people who connect
		// directly to the SourceTV port.
		if (client.SteamID.GetEAccountType() == EAccountType.k_EAccountTypeAnonGameServer) {
			SteamGameServer().SendUserDisconnect_DEPRECATED(client.SteamID);

			// Clear the steam ID, as it was a dummy one that should not be used again
			client.SteamID = new CSteamID(new AccountID_t(0), EUniverse.k_EUniverseInvalid, EAccountType.k_EAccountTypeInvalid);
		}
		else {

			// All bots should have an anonymous account ID
			Assert(!client.IsFakeClient());

			USERID id = client.GetNetworkID();
			if (id.IDType != IDType.Steam)
				return;

			// Msg("S3: Sending client disconnect for %x\n", steamIDClient.ConvertToUint64( ) );
			SteamGameServer().EndAuthSession(client.SteamID);
		}
	}

	static double LastRunCallback;
	public void RunFrame() {
		bool hasPlayers = sv.GetNumClients() > 0;

		if (HasActivePlayers != hasPlayers) {
			HasActivePlayers = hasPlayers;
			SendUpdatedServerDetails();
		}

		double CurTime = Platform.Time;
		if (CurTime - LastRunCallback > 0.1f) {
			LastRunCallback = CurTime;
			SteamGameServer_RunCallbacks();
		}
	}

	internal CSteamID GetGSSteamID() {
		return SteamIDGS;
	}

	internal bool NotifyLocalClientConnect(BaseClient client) {
		CSteamID steamID = default;

		steamID = SteamGameServer()?.CreateUnauthenticatedUserConnection() ?? default;
		client.SetSteamID(steamID);

		SendUpdatedServerDetails();

		return true;
	}

	internal bool NotifyClientConnect(BaseClient? client, int unUserID, NetAddress adr, ReadOnlySpan<byte> pvCookie, int ucbCookie) {
		if (!BIsActive())
			return true;

		if (client == null || client.IsFakeClient())
			return false;

		if (ucbCookie <= sizeof(ulong)) {
			Warning("Client UserID {unUserID} connected with invalid ticket size {ucbCookie}\n");
			return false;
		}

		SpanBinaryReader br = new(pvCookie);
		ulong ulSteamID = br.Read<ulong>();

		CSteamID steamID = new(ulSteamID);
		if (steamID.GetEUniverse() != SteamGameServer().GetSteamID().GetEUniverse()) {
			Warning($"Client {unUserID} {steamID.Render()} connected to universe {steamID.GetEUniverse()}, but game server {SteamGameServer().GetSteamID().Render()} is running in universe {SteamGameServer().GetSteamID().GetEUniverse()}\n");
			return false;
		}
		if (!steamID.IsValid() || !steamID.BIndividualAccount()) {
			Warning($"Client {unUserID} connected from {adr.ToString()} with invalid Steam ID {steamID.Render()}\n");
			return false;
		}

		pvCookie = pvCookie[sizeof(ulong)..];
		ucbCookie -= sizeof(ulong);
		EBeginAuthSessionResult eResult = SteamGameServer().BeginAuthSession(pvCookie.ToArray() /* This sucks, but Steamworks.NET forces our hand */, ucbCookie, steamID);
		switch (eResult) {
			case EBeginAuthSessionResult.k_EBeginAuthSessionResultOK:
				break;
			case EBeginAuthSessionResult.k_EBeginAuthSessionResultInvalidTicket:
				Warning("S3: Client connected with invalid ticket: UserID: %x\n", unUserID);
				return false;
			case EBeginAuthSessionResult.k_EBeginAuthSessionResultDuplicateRequest:
				Warning("S3: Duplicate client connection: UserID: %x SteamID %x\n", unUserID, steamID.ConvertToUint64());
				return false;
			case EBeginAuthSessionResult.k_EBeginAuthSessionResultInvalidVersion:
				Warning("S3: Client connected with invalid ticket ( old version ): UserID: %x\n", unUserID);
				return false;
			case EBeginAuthSessionResult.k_EBeginAuthSessionResultGameMismatch:
				Warning("S3: Client connected with ticket for the wrong game: UserID: %x\n", unUserID);
				return false;
			case EBeginAuthSessionResult.k_EBeginAuthSessionResultExpiredTicket:
				Warning("S3: Client connected with expired ticket: UserID: %x\n", unUserID);
				return false;
			default:
				Warning("S3: Client failed auth session for unknown reason. UserID: %x\n", unUserID);
				return false;
		}

		client.SetSteamID(steamID);

		SendUpdatedServerDetails();

		return true;
	}
}
