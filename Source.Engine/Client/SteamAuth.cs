global using static Source.Engine.Client.Steam3ClientAccessor;

using Source.Common.Steam;

using Steamworks;

using System.Runtime.CompilerServices;
namespace Source.Engine.Client;


[EngineComponent]
public static class Steam3ClientAccessor
{
#if !SWDS
	[Dependency] static Steam3ClientImpl _client = null!;
#endif
#if SWDS
	static readonly Steam3ClientImpl _client = new();
#endif
	public static Steam3ClientImpl Steam3Client() => _client;
}

#if SWDS
public class Steam3ClientImpl
{
	public ISteamClient SteamClient() => null!;
	public ISteamUser SteamUser() => null!;
	public ISteamUtils SteamUtils() => null!;
	public ISteamUserStats SteamUserStats() => null!;
	public ISteamRemoteStorage SteamRemoteStorage() => null!;
}
#else
[EngineComponent]
public class Steam3ClientImpl : IDisposable
{
	// These exist as wrappers to the static methods
	// (kind of a stupid way to implement this, but its easiest right now
	readonly ISteamClient __SteamClient = new ImplSteamClient();
	readonly ISteamUser __SteamUser = new ImplSteamUser();
	readonly ISteamFriends __SteamFriends = new ImplSteamFriends();
	readonly ISteamUtils __SteamUtils = new ImplSteamUtils();
	readonly ISteamMatchmaking __SteamMatchmaking = new ImplSteamMatchmaking();
	readonly ISteamGameSearch __SteamGameSearch = new ImplSteamGameSearch();
	readonly ISteamUserStats __SteamUserStats = new ImplSteamUserStats();
	readonly ISteamApps __SteamApps = new ImplSteamApps();
	readonly ISteamMatchmakingServers __SteamMatchmakingServers = new ImplSteamMatchmakingServers();
	readonly ISteamNetworking __SteamNetworking = new ImplSteamNetworking();
	readonly ISteamRemoteStorage __SteamRemoteStorage = new ImplSteamRemoteStorage();
	readonly ISteamScreenshots __SteamScreenshots = new ImplSteamScreenshots();
	readonly ISteamHTTP __SteamHTTP = new ImplSteamHTTP();
	readonly ISteamController __SteamController = new ImplSteamController();
	readonly ISteamUGC __SteamUGC = new ImplSteamUGC();
	readonly ISteamAppList __SteamAppList = new ImplSteamAppList();
	readonly ISteamMusic __SteamMusic = new ImplSteamMusic();
	readonly ISteamMusicRemote __SteamMusicRemote = new ImplSteamMusicRemote();
	readonly ISteamHTMLSurface __SteamHTMLSurface = new ImplSteamHTMLSurface();
	readonly ISteamInventory __SteamInventory = new ImplSteamInventory();
	readonly ISteamVideo __SteamVideo = new ImplSteamVideo();
	readonly ISteamParentalSettings __SteamParentalSettings = new ImplSteamParentalSettings();
	readonly ISteamInput __SteamInput = new ImplSteamInput();

	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamClient SteamClient() => HasSteamClient() ? __SteamClient : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamUser SteamUser() => HasSteamClient() ? __SteamUser : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamFriends SteamFriends() => HasSteamClient() ? __SteamFriends : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamUtils SteamUtils() => HasSteamClient() ? __SteamUtils : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamMatchmaking SteamMatchmaking() => HasSteamClient() ? __SteamMatchmaking : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamGameSearch SteamGameSearch() => HasSteamClient() ? __SteamGameSearch : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamUserStats SteamUserStats() => HasSteamClient() ? __SteamUserStats : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamApps SteamApps() => HasSteamClient() ? __SteamApps : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamMatchmakingServers SteamMatchmakingServers() => HasSteamClient() ? __SteamMatchmakingServers : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamNetworking SteamNetworking() => HasSteamClient() ? __SteamNetworking : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamRemoteStorage SteamRemoteStorage() => HasSteamClient() ? __SteamRemoteStorage : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamScreenshots SteamScreenshots() => HasSteamClient() ? __SteamScreenshots : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamHTTP SteamHTTP() => HasSteamClient() ? __SteamHTTP : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamController SteamController() => HasSteamClient() ? __SteamController : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamUGC SteamUGC() => HasSteamClient() ? __SteamUGC : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamAppList SteamAppList() => HasSteamClient() ? __SteamAppList : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamMusic SteamMusic() => HasSteamClient() ? __SteamMusic : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamMusicRemote SteamMusicRemote() => HasSteamClient() ? __SteamMusicRemote : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamHTMLSurface SteamHTMLSurface() => HasSteamClient() ? __SteamHTMLSurface : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamInventory SteamInventory() => HasSteamClient() ? __SteamInventory : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamVideo SteamVideo() => HasSteamClient() ? __SteamVideo : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamParentalSettings SteamParentalSettings() => HasSteamClient() ? __SteamParentalSettings : null!;
	[MethodImpl(MethodImplOptions.AggressiveInlining)] public ISteamInput SteamInput() => HasSteamClient() ? __SteamInput : null!;

	bool Active;

	public void Dispose() { }

	public void Activate() {
		if (Active)
			return;

		Active = true;
		if (SteamAPI.InitEx(out string errMsg) != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
			Sys.Error($"Failed to initialize Steamworks API.\n\n{errMsg}\n\nMake sure Steam is running and is up to date!");
	}

	public void RunFrame() {
		SteamAPI.RunCallbacks();
	}
}
#endif


