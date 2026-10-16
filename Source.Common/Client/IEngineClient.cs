using Source.Common.Audio;
using Source.Common.Engine;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.Common.Input;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Networking;
using Source.Common.Physics;

using Steamworks;

using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Source.Common.Client;

/// <summary>
/// Engine player info. (replica of player_info_s)
/// </summary>
public struct PlayerInfo
{
	public const int SIZEOF = 228;

	public InlineArray128<char> Name;
	public int UserID;
	public InlineArray33<byte> GUID;
	public uint FriendsID;
	public InlineArray32<char> FriendsName;
	public bool FakePlayer;
	public bool IsHLTV;
	public bool IsReplay;
	public InlineArray4<CRC32_t> CustomFiles;
	public byte FilesDownloaded;

	public static bool FromBytes(ReadOnlySpan<byte> bytes, out PlayerInfo info) {
		if (bytes.Length < SIZEOF) {
			info = default;
			return false;
		}

		info = new();

		ReadOnlySpan<byte> asciiName = bytes[0..128];
		int asciiNull = asciiName.IndexOf<byte>(0);
		if (asciiNull != -1)
			asciiName = asciiName[..asciiNull];
		Encoding.ASCII.GetChars(asciiName, info.Name);

		info.UserID = MemoryMarshal.Cast<byte, int>(bytes[128..132])[0];
		bytes[132..165].CopyTo(info.GUID);
		info.FriendsID = MemoryMarshal.Cast<byte, uint>(bytes[168..172])[0];

		ReadOnlySpan<byte> friendsName = bytes[172..204];
		int friendsNull = friendsName.IndexOf<byte>(0);
		if (friendsNull != -1)
			friendsName = friendsName[..];
		Encoding.ASCII.GetChars(friendsName, info.FriendsName);

		info.FakePlayer = bytes[204] != 0;
		info.IsHLTV = bytes[205] != 0;
		info.IsReplay = bytes[206] != 0;

		MemoryMarshal.Cast<byte, CRC32_t>(bytes[208..(208 + 16)]).CopyTo(info.CustomFiles);
		info.FilesDownloaded = bytes[224];
		return true;
	}

	public readonly void ToBytes(Span<byte> bytes) {
		bytes[..SIZEOF].Clear();

		ReadOnlySpan<char> name = ((ReadOnlySpan<char>)Name).SliceNullTerminatedString();
		Encoding.ASCII.GetBytes(name[..Math.Min(name.Length, 127)], bytes[0..128]);

		MemoryMarshal.Write(bytes[128..132], in UserID);
		((ReadOnlySpan<byte>)GUID).CopyTo(bytes[132..165]);
		MemoryMarshal.Write(bytes[168..172], in FriendsID);

		ReadOnlySpan<char> friendsName = ((ReadOnlySpan<char>)FriendsName).SliceNullTerminatedString();
		Encoding.ASCII.GetBytes(friendsName[..Math.Min(friendsName.Length, 31)], bytes[172..204]);

		bytes[204] = (byte)(FakePlayer ? 1 : 0);
		bytes[205] = (byte)(IsHLTV ? 1 : 0);
		bytes[206] = (byte)(IsReplay ? 1 : 0);

		MemoryMarshal.Cast<CRC32_t, byte>((ReadOnlySpan<CRC32_t>)CustomFiles).CopyTo(bytes[208..224]);
		bytes[224] = FilesDownloaded;
	}
}

public struct AudioState
{
	public Vector3 Origin;
	public QAngle Angles;
	public bool IsUnderwater;
}
public enum SkyboxVisibility
{
	NotVisible,
	Skybox3D,
	Skybox2D
}

public enum ClientFrameStage
{
	Undefined = -1,
	Start,
	NetUpdateStart,
	NetUpdatePostDataUpdateStart,
	NetUpdatePostDataUpdateEnd,
	NetUpdateEnd,
	RenderStart,
	RenderEnd
}

public struct OcclusionParams
{
	public float MaxOccludeeArea;
	public float MinOccluderArea;
}

/// <summary>
/// Interface the engine exposes to the client DLL
/// </summary>
public interface IEngineClient
{
	/// <summary>
	/// Find the model's surfaces that intersect the given sphere.
	/// </summary>
	/// <returns>The number of surfaces filled in.</returns>
	int GetIntersectingSurfaces(Model? model, in Vector3 center, float radius, bool onlyVisibleSurfaces, ref SurfInfo infos, int maxInfos);

	/// <summary>
	/// Get the lighting intensivty for a specified point
	/// If clamp is specified, the resulting Vector is restricted to the 0.0 to 1.0 for each element
	/// </summary>
	Vector3 GetLightForPoint(in Vector3 pos, bool clamp);

	/// <summary>
	/// Traces the line and reports the material impacted as well as the lighting information for the impact point
	/// </summary>
	IMaterial? TraceLineMaterialAndLighting(in Vector3 start, in Vector3 end, out Vector3 diffuseLightColor, out Vector3 baseColor);

	// Given an input text buffer data pointer, parses a single token into the variable token and returns the new
	//  reading position
	ReadOnlySpan<byte> ParseFile(ReadOnlySpan<byte> data, Span<char> token);
	bool CopyLocalFile(ReadOnlySpan<char> source, ReadOnlySpan<char> destination);

	// Gets the dimensions of the game window
	void GetScreenSize(out int width, out int height);

	// Forwards szCmdString to the server, sent reliably if bReliable is set
	void ServerCmd(ReadOnlySpan<char> szCmdString, bool bReliable = true);
	// Inserts szCmdString into the command buffer as if it was typed by the client to his/her console.
	// Note: Calls to this are checked against FCVAR_CLIENTCMD_CAN_EXECUTE (if that bit is not set, then this function can't change it).
	//       Call ClientCmd_Unrestricted to have access to FCVAR_CLIENTCMD_CAN_EXECUTE vars.
	void ClientCmd(ReadOnlySpan<char> szCmdString);

	// Fill in the player info structure for the specified player index (name, model, etc.)
	bool GetPlayerInfo(int ent_num, out PlayerInfo pinfo);

	// Retrieve the player entity number for a specified userID
	int GetPlayerForUserID(int userID);

	// Retrieves text message system information for the specified message by name
	ref ClientTextMessage TextMessageGet(ReadOnlySpan<char> name);

	// Returns true if the console is visible
	bool Con_IsVisible();

	// Get the entity index of the local player
	int GetLocalPlayer();

	// Client DLL is hooking a model, loads the model into memory and returns  pointer to the model_t
	Model? LoadModel(ReadOnlySpan<char> name, bool prop = false);

	// Get accurate, sub-frame clock ( profiling use )
	TimeUnit_t Time();

	// Get the exact server timesstamp ( server time ) from the last message received from the server
	TimeUnit_t GetLastTimeStamp();

	// Given a CAudioSource (opaque pointer), retrieve the underlying CSentence object ( stores the words, phonemes, and close
	//  captioning data )
	Sentence? GetSentence(AudioSource? audioSource);
	// Given a CAudioSource, determines the length of the underlying audio file (.wav, .mp3, etc.)
	float GetSentenceLength(AudioSource? audioSource);
	// Returns true if the sound is streaming off of the hard disk (instead of being memory resident)
	bool IsStreaming(AudioSource? audioSource);

	// Copy current view orientation into va
	void GetViewAngles(out QAngle va);
	// Set current view orientation from va
	void SetViewAngles(in QAngle va);

	// Retrieve the current game's maxclients setting
	int GetMaxClients();

	// Given the string pBinding which may be bound to a key, 
	//  returns the string name of the key to which this string is bound. Returns NULL if no such binding exists
	ReadOnlySpan<char> Key_LookupBinding(ReadOnlySpan<char> pBinding);

	// Given the name of the key "mouse1", "e", "tab", etc., return the string it is bound to "+jump", "impulse 50", etc.
	ReadOnlySpan<char> Key_BindingForKey(ButtonCode code);

	// key trapping (for binding keys)
	void StartKeyTrapMode();
	bool CheckDoneKeyTrapping(out ButtonCode code);

	// Returns true if the player is fully connected and active in game (i.e, not still loading)
	bool IsInGame();
	// Returns true if the player is connected, but not necessarily active in game (could still be loading)
	bool IsConnected();
	// Returns true if the loading plaque should be drawn
	bool IsDrawingLoadingImage();

	// Prints the formatted string to the notification area of the screen ( down the right hand edge
	//  numbered lines starting at position 0
	void Con_NPrintf(int pos, ReadOnlySpan<char> text);
	// Similar to Con_NPrintf, but allows specifying custom text color and duration information
	void Con_NXPrintf(in Con_NPrint_s info, ReadOnlySpan<char> text);

	// Is the specified world-space bounding box inside the view frustum?
	bool IsBoxVisible(in Vector3 mins, in Vector3 maxs);

	// Is the specified world-space boudning box in the same PVS cluster as the view origin?
	bool IsBoxInViewCluster(in Vector3 mins, in Vector3 maxs);

	// Returns true if the specified box is outside of the view frustum and should be culled
	bool CullBox(in Vector3 mins, in Vector3 maxs);

	// Allow the sound system to paint additional data (during lengthy rendering operations) to prevent stuttering sound.
	void Sound_ExtraUpdate();

	// Get the current game directory ( e.g., hl2, tf2, cstrike, hl1 )
	ReadOnlySpan<char> GetGameDirectory();

	// Get access to the world to screen transformation matrix
	ref readonly Matrix4x4 WorldToScreenMatrix();

	// Get the matrix to move a point from world space into view space
	// (translate and rotate so the camera is at the origin looking down X).
	ref readonly Matrix4x4 WorldToViewMatrix();

	// The .bsp file can have mod-specified data lumps. These APIs are for working with such game lumps.

	// Get mod-specified lump version id for the specified game data lump
	int GameLumpVersion(int lumpId);
	// Get the raw size of the specified game data lump.
	int GameLumpSize(int lumpId);
	// Loads a game lump off disk, writing the data into the buffer pointed to bye pBuffer
	// Returns false if the data can't be read or the destination buffer is too small
	bool LoadGameLump(int lumpId, Span<byte> buffer);

	// Returns the number of leaves in the level
	int LevelLeafCount();

	// Gets a way to perform spatial queries on the BSP tree
	ISpatialQuery? GetBSPTreeQuery();

	// Convert texlight to gamma...
	void LinearToGamma(Span<float> linear, Span<float> gamma);

	// Get the lightstyle value
	float LightStyleValue(int style);

	// Computes light due to dynamic lighting at a point
	// If the normal isn't specified, then it'll return the maximum lighting
	void ComputeDynamicLighting(in Vector3 pt, in Vector3 normal, out Vector3 color);

	// Returns the color of the ambient light
	void GetAmbientLightColor(out Vector3 color);

	// Returns the dx support level
	int GetDXSupportLevel();

	// GR - returns the HDR support status
	bool SupportsHDR();

	// Replace the engine's material system pointer.
	void Mat_Stub(IMaterialSystem? matSys);

	// Get the name of the current map
	void GetChapterName(Span<char> buff);
	ReadOnlySpan<char> GetLevelName();
	int GetLevelVersion();
#if !NO_VOICE
	// Obtain access to the voice tweaking API
	ref IVoiceTweak GetVoiceTweakAPI();
#endif
	// Tell engine stats gathering system that the rendering frame is beginning/ending
	void EngineStats_BeginFrame();
	void EngineStats_EndFrame();

	// This tells the engine to fire any events (temp entity messages) that it has queued up this frame. 
	// It should only be called once per frame.
	void FireEvents();

	// Returns an area index if all the leaves are in the same area. If they span multple areas, then it returns -1.
	int GetLeavesArea(Span<int> leaves);

	// Returns true if the box touches the specified area's frustum.
	bool DoesBoxTouchAreaFrustum(in Vector3 mins, in Vector3 maxs, int iArea);

	// Sets the hearing origin (i.e., the origin and orientation of the listener so that the sound system can spatialize 
	//  sound appropriately ).
	void SetAudioState(in AudioState state);

	// Sentences / sentence groups
	int SentenceGroupPick(int groupIndex, Span<char> name, int nameBufLen);
	int SentenceGroupPickSequential(int groupIndex, Span<char> name, int nameBufLen, int sentenceIndex, int reset);
	int SentenceIndexFromName(ReadOnlySpan<char> sentenceName);
	ReadOnlySpan<char> SentenceNameFromIndex(int sentenceIndex);
	int SentenceGroupIndexFromName(ReadOnlySpan<char> grouname);
	ReadOnlySpan<char> SentenceGrounameFromIndex(int groupIndex);
	float SentenceLength(int sentenceIndex);

	// Computes light due to dynamic lighting at a point
	// If the normal isn't specified, then it'll return the maximum lighting
	// If pBoxColors is specified (it's an array of 6), then it'll copy the light contribution at each box side.
	void ComputeLighting(in Vector3 pt, in Vector3 normal, bool clamp, out Vector3 color, Span<Vector3> boxColors = default);

	// Activates/deactivates an occluder...
	void ActivateOccluder(int nOccluderIndex, bool bActive);
	bool IsOccluded(in Vector3 absMins, in Vector3 absMaxs);

	// returns info interface for client netchannel
	INetChannelInfo GetNetChannelInfo();

	// Debugging functionality:
	// Very slow routine to draw a physics model
	void DebugDrawPhysCollide(PhysCollide collide, IMaterial? material, in Matrix3x4 transform, in Color color);
	// This can be used to notify test scripts that we're at a particular spot in the code.
	void CheckPoint(ReadOnlySpan<char> name);
	// Draw portals if r_DrawPortals is set (Debugging only)
	void DrawPortals();
	// Determine whether the client is playing back or recording a demo
	bool IsPlayingDemo();
	bool CopyFile(ReadOnlySpan<char> source, ReadOnlySpan<char> destination);
	bool IsRecordingDemo();
	bool IsPlayingTimeDemo();
	int GetDemoRecordingTick();
	int GetDemoPlaybackTick();
	int GetDemoPlaybackStartTick();
	float GetDemoPlaybackTimeScale();
	int GetDemoPlaybackTotalTicks();
	// Is the game paused?
	bool IsPaused();
	// Is the game currently taking a screenshot?
	bool IsTakingScreenshot();
	// Is this a HLTV broadcast ?
	bool IsHLTV();
	// is this level loaded as just the background to the main menu? (active, but unplayable)
	bool IsLevelMainMenuBackground();
	// returns the name of the background level
	void GetMainMenuBackgroundName(Span<char> dest);

	// Occlusion system control
	void SetOcclusionParameters(in OcclusionParams parms);

	// What language is the user expecting to hear .wavs in, "english" or another...
	void GetUILanguage(Span<char> dest);

	// Can skybox be seen from a particular point?
	SkyboxVisibility IsSkyboxVisibleFromPoint(in Vector3 vecPoint);

	// Get the pristine map entity lump string.  (e.g., used by CS to reload the map entities when restarting a round.)
	ReadOnlySpan<char> GetMapEntitiesString();

	// Is the engine in map edit mode ?
	bool IsInEditMode();

	// current screen aspect ratio (eg. 4.0f/3.0f, 16.0f/9.0f)
	float GetScreenAspectRatio();


	// allow other modules to know about engine versioning (one use is a proxy for network compatability)
	uint GetEngineBuildNumber(); // engines build
	ReadOnlySpan<char> GetProductVersionString(); // mods version number (steam.inf)

	// Communicates to the color correction editor that it's time to grab the pre-color corrected frame
	// Passes in the actual size of the viewport
	void GrabPreColorCorrectedFrame(int x, int y, int width, int height);

	bool IsHammerRunning();

	// Inserts szCmdString into the command buffer as if it was typed by the client to his/her console.
	// And then executes the command string immediately (vs ClientCmd() which executes in the next frame)
	//
	// Note: this is NOT checked against the FCVAR_CLIENTCMD_CAN_EXECUTE vars.
	void ExecuteClientCmd(ReadOnlySpan<char> szCmdString);

	// returns if the loaded map was processed with HDR info. This will be set regardless
	// of what HDR mode the player is in.
	bool MapHasHDRLighting();

	int GetAppID();

	// Just get the leaf ambient light - no caching, no samples
	Vector3 GetLightForPointFast(in Vector3 pos, bool bClamp);

	// This version does NOT check against FCVAR_CLIENTCMD_CAN_EXECUTE.
	void ClientCmd_Unrestricted(ReadOnlySpan<char> szCmdString);

	// This used to be accessible through the cl_restrict_server_commands cvar.
	// By default, Valve games restrict the server to only being able to execute commands marked with FCVAR_SERVER_CAN_EXECUTE.
	// By default, mods are allowed to execute any server commands, and they can restrict the server's ability to execute client
	// commands with this function.
	void SetRestrictServerCommands(bool restrict);

	// If set to true (defaults to true for Valve games and false for others), then IVEngineClient::ClientCmd
	// can only execute things marked with FCVAR_CLIENTCMD_CAN_EXECUTE.
	void SetRestrictClientCommands(bool restrict);

	// Sets the client renderable for an overlay's material proxy to bind to
	void SetOverlayBindProxy(int overlayID, object bindProxy);

	bool CopyFrameBufferToMaterial(ReadOnlySpan<char> materialName);

	// Matchmaking
	void ChangeTeam(ReadOnlySpan<char> teamName);

	// Causes the engine to read in the user's configuration on disk
	void ReadConfiguration(bool readDefault = false);

	void SetAchievementMgr(IAchievementMgr? achievementMgr);
	IAchievementMgr? GetAchievementMgr();

	bool MapLoadFailed();
	void SetMapLoadFailed(bool bState);

	bool IsLowViolence();
	ReadOnlySpan<char> GetMostRecentSaveGame();
	void SetMostRecentSaveGame(ReadOnlySpan<char> lpszFilename);

	bool IsSaveInProgress();
	uint OnStorageDeviceAttached();
	void OnStorageDeviceDetached();

	void ResetDemoInterpolation();

	// Methods to set/get a gamestats data container so client & server running in same process can send combined data
	void SetGamestatsData(object? gamestatsData);
	object? GetGamestatsData();

	// we need to pull delta's from the cocoa mgr, the engine vectors this for us
	void GetMouseDelta(out int x, out int y, bool ignoreNextMouseDelta = false);

	// Sends a key values server command, not allowed from scripts execution
	// Params:
	//	pKeyValues	- key values to be serialized and sent to server
	//				  the pointer is deleted inside the function: pKeyValues->deleteThis()
	void ServerCmdKeyValues(KeyValues keyValues);

	bool IsSkippingPlayback();
	bool IsLoadingDemo();

	// Returns true if the engine is playing back a "locally recorded" demo, which includes
	// both SourceTV and replay demos, since they're recorded locally (on servers), as opposed
	// to a client recording a demo while connected to a remote server.
	bool IsPlayingDemoALocallyRecordedDemo();

	// Given the string pBinding which may be bound to a key, 
	//  returns the string name of the key to which this string is bound. Returns NULL if no such binding exists
	// Unlike Key_LookupBinding, leading '+' characters are not stripped from bindings.
	ReadOnlySpan<char> Key_LookupBindingExact(ReadOnlySpan<char> pBinding);

	void GMOD_SetTimeManipulator(float scaleFramerate);
	void GMOD_SendToServer(ReadOnlySpan<byte> data, int dataBits, bool reliable);
	void GMOD_PlaceDecalMaterial(IMaterial material, bool unk1, int unk2, IClientEntity ent, in Vector3 origin /*?*/, in Vector3 normal /*?*/, in Color color, float unk3, float unk4);
	void GMOD_GetSpew(Span<char> spewBuffer);
	void GMOD_SetViewEntity(BaseHandle entity);
	void GMOD_BrushMaterialOverride(IMaterial? matOverride);
	void GMOD_R_RedownloadAllLightmaps(bool unk);
	void GMOD_RawClientCmd_Unrestricted( ReadOnlySpan<char> command );
	IGMODDataTable GMOD_CreateDataTable(GMODDataTableCallbackFn fnCallback);
	void GMOD_DestroyDataTable(IGMODDataTable dataTable);
	MDLHandle_t GMOD_LoadModel( ReadOnlySpan<char> path );
	void GMOD_DecalRemoveEntity(int index);
	ReadOnlySpan<char> GMOD_TranslateAlias( ReadOnlySpan<char> cmd );
	void GMOD_R_StudioInitLightingCache();
	int PrecacheSentenceFile( ReadOnlySpan<char> fileName );
	float GetPlayerVoiceVolume(CSteamID steamID);
	void SetPlayerVoiceVolume(CSteamID steamID, float volume);
	bool NET_IsHostLocal( ReadOnlySpan<char> hostName );
#if GMOD_DLL
	bool IsHostingGame();
#endif
	bool IsDedicatedServer();

	uint GetProtocolVersion();
	bool IsWindowedMode();

	// Flash the window (os specific)
	void FlashWindow();

	// Client version from the steam.inf, this will be compared to the GC version
	int GetClientVersion(); // engines build

	// Is App Active 
	bool IsActiveApp();
	void DisconnectInternal();
}
