using Source.Common.Filesystem;
using Source.Common.GarrysMod.Lua;
using Source.Common.MaterialSystem;
using Source.Common.Steam;

using Steamworks;

namespace Source.Common.GarrysMod;

public interface IMotionSensor; // todo

public interface IGet
{
	void OnLoadFailed(ReadOnlySpan<char> module);
	ReadOnlySpan<char> GameDir();
	bool IsDedicatedServer();
	int GetClientCount();
	IFileSystem? FileSystem();
	ILuaShared? LuaShared();
	ILuaConVars? LuaConVars();
	IMenuSystem? MenuSystem();
	IResources? Resources();
	IIntroScreen? IntroScreen();
	IMaterialSystem? Materials();
	IServerAddons? ServerAddons();
	IHtmlSystem? HTML();
	ISteamHTTP? SteamHTTP();
	ISteamUtils? SteamUtils();
	ISteamUGC? SteamUGC();
	ISteamNetworking? SteamNetworking();
	void Initialize(IFileSystem fileSystem);
	void ShutDown();
	void RunSteamCallbacks();
	void SetMotionSensor(IMotionSensor? sensor);
	IMotionSensor? MotionSensor();
	int Version();
	ReadOnlySpan<char> VersionStr();
	ReadOnlySpan<char> Branch();
	IGMod_Audio? Audio();
	ReadOnlySpan<char> VersionTimeStr();
	// IAnalytics Analytics();
	void UpdateRichPresense(ReadOnlySpan<char> status);
	void ResetRichPresense();
	ReadOnlySpan<char> GameDirParent(); // todo: real name unknown
	void FilterText(ReadOnlySpan<char> input, Span<char> output, ETextFilteringContext context, CSteamID sourceSteamID);
}
