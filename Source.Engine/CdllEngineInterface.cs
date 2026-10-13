using CommunityToolkit.HighPerformance;

using Source.Common;
using Source.Common.Audio;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.Formats.Keyvalues;
using Source.Common.GarrysMod;
using Source.Common.Input;
using Source.Common.Launcher;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Networking;
using Source.Common.Physics;
using Source.Engine.Client;
using Source.Engine.Server;

using Steamworks;

using System.Numerics;

namespace Source.Engine;

#if !SWDS
public class EngineClient(Cbuf Cbuf, Scr Scr, Con Con, Key Key, IGame game, Host Host,
							MaterialSystem_Config MaterialSystemConfig,
							MatSysInterface MatSys, ModelLoader modelloader, CL CL, Cmd Cmd) : IEngineClient
{
	public ReadOnlySpan<char> Key_LookupBinding(ReadOnlySpan<char> binding) => Key.NameForBinding(binding);
	public void GetMainMenuBackgroundName(Span<char> dest) {
		"background05".CopyTo(dest);
	}

	public void Con_NXPrintf(in Con_NPrint_s np, ReadOnlySpan<char> text) {
#if !SWDS
		Con.NXPrintF(in np, text);
#endif
	}

	public bool IsDrawingLoadingImage() => Scr.DrawLoading;

	public int GetMaxClients() => cl.MaxClients;

	public ReadOnlySpan<char> GetLevelName() {
		if (sv.IsDedicated())
			return "Dedicated Server";
		else if (!cl.IsConnected())
			return "";
		return cl.LevelFileName;
	}

	public int GetLocalPlayer() => cl.PlayerSlot + 1;
	public void ClientCmd_Unrestricted(ReadOnlySpan<char> cmdString) => Cbuf.AddText(cmdString);
	public void ExecuteClientCmd(ReadOnlySpan<char> cmdString) {
		Cbuf.AddText(cmdString);
		Cbuf.Execute();
	}

	public void ClientCmd(ReadOnlySpan<char> cmdString) {
		if (cl.RestrictClientCommands && !Cbuf.HasRoomForExecutionMarkers(2)) {
			AssertMsg(false, "EngineClient.ClientCmd called but there is no room for the execution markers. Ignoring command.");
			return;
		}

		if (cl.RestrictClientCommands)
			Cbuf.AddTextWithMarkers(CmdExecutionMarker.EnableClientCmdCanExecute, cmdString, CmdExecutionMarker.DisableClientCmdCanExecute);
		else
			Cbuf.AddText(cmdString);
	}

	public bool IsLevelMainMenuBackground() => sv.IsLevelMainMenuBackground();

	public bool IsPaused() => cl.IsPaused();

	public bool GetPlayerInfo(int playerIndex, out PlayerInfo playerInfo) {
		playerIndex--;
		if (playerIndex >= cl.MaxClients || playerIndex < 0) {
			playerInfo = new();
			return false;
		}

		Assert(cl.UserInfoTable != null);
		if (cl.UserInfoTable == null) {
			playerInfo = new();
			return false;
		}

		Assert(playerIndex < cl.UserInfoTable.GetNumStrings());
		if (playerIndex >= cl.UserInfoTable.GetNumStrings()) {
			playerInfo = new();
			return false;
		}

		Span<byte> pi = cl.UserInfoTable.GetStringUserData(playerIndex);
		PlayerInfo.FromBytes(pi, out playerInfo);
		return true;
	}

	public bool Con_IsVisible() =>
#if SWDS
		false;
#else
		 Con.IsVisible();
#endif

	ReadOnlySpan<byte> IEngineClient.ParseFile(ReadOnlySpan<byte> data, Span<char> token) => Common.ParseFile(data, token);

	public void GetViewAngles(out QAngle viewangles) {
		viewangles = cl.ViewAngles;
	}

	public void SetViewAngles(in QAngle viewangles) {
		cl.ViewAngles = QAngle.Normalize(in viewangles);
	}

	public void GetScreenSize(out int w, out int h) {
		// Is this even right???
		using MatRenderContextPtr renderContext = new(materials);
		renderContext.GetWindowSize(out w, out h);
	}

	readonly ILauncherManager launcherMgr = Singleton<ILauncherManager>();
	public void GetMouseDelta(out int dx, out int dy, bool ignoreNextMouseDelta = false) {
		launcherMgr.GetMouseDelta(out dx, out dy);
	}

	public bool IsConnected() {
		return cl.IsConnected();
	}

	public bool IsInGame() {
		return cl.IsActive();
	}

	public double GetLastTimeStamp() {
		return cl.LastServerTickTime;
	}

	public uint GetProtocolVersion() => Protocol.VERSION;

	public SkyboxVisibility IsSkyboxVisibleFromPoint(in Vector3 point) {
		if (MaterialSystemConfig.Fullbright == 1)
			return SkyboxVisibility.Skybox3D;

		int leaf = CM.PointLeafnum(point);
		int flags = GetCollisionBSPData()!.MapLeafs[leaf].Flags;
		if ((flags & BSPFileCommon.LEAF_FLAGS_SKY) != 0)
			return SkyboxVisibility.Skybox3D;

		return ((flags & BSPFileCommon.LEAF_FLAGS_SKY2D) != 0) ? SkyboxVisibility.Skybox2D : SkyboxVisibility.NotVisible;
	}

	public float GetScreenAspectRatio() {
		return MatSys.GetScreenAspect();
	}

	public bool IsPlayingDemo() => false; // Demos arent implemented yet
	public bool CopyFile(ReadOnlySpan<char> source, ReadOnlySpan<char> destination) => Common.CopyFile(source, destination);
	public bool IsPlayingTimeDemo() => false; // Demos arent implemented yet
	public INetChannelInfo? GetNetChannelInfo() => cl.NetChannel;
	public void FireEvents() => CL.FireEvents();

	public Model? LoadModel(ReadOnlySpan<char> name, bool prop) {
		return modelloader.GetModelForName(name, prop ? ModelLoaderFlags.DetailProp : ModelLoaderFlags.ClientDLL);
	}

	// These need more BSP work.
	public bool IsBoxVisible(in Vector3 mins, in Vector3 maxs) {
		return true;
	}

	public int GetPlayerForUserID(int userID) {
		if (cl.UserInfoTable == null)
			return 0;

		int maxClients = Math.Min(cl.MaxClients, cl.UserInfoTable.GetNumStrings());
		for (int i = 0; i < maxClients; i++) {
			Span<byte> pi = cl.UserInfoTable.GetStringUserData(i);
			if (!PlayerInfo.FromBytes(pi, out PlayerInfo playerInfo))
				continue;

			if (playerInfo.UserID == userID)
				return (i + 1);
		}

		return 0;
	}

	public TimeUnit_t Time() => Sys.Time;

	public int GetIntersectingSurfaces(Model? model, in Vector3 center, float radius, bool onlyVisibleSurfaces, ref SurfInfo infos, int maxInfos) {
		throw new NotImplementedException();
	}

	public Vector3 GetLightForPoint(in Vector3 pos, bool clamp) {
		throw new NotImplementedException();
	}

	public IMaterial? TraceLineMaterialAndLighting(in Vector3 start, in Vector3 end, out Vector3 diffuseLightColor, out Vector3 baseColor) {
		throw new NotImplementedException();
	}

	public bool CopyLocalFile(ReadOnlySpan<char> source, ReadOnlySpan<char> destination) {
		throw new NotImplementedException();
	}

	public void ServerCmd(ReadOnlySpan<char> szCmdString, bool bReliable = true) {
		// info handling
		string buf = $"cmd {szCmdString}";

		TokenizedCommand args = new();
		args.Tokenize(buf);
		Cmd.ForwardToServer(args);
	}

	public ref ClientTextMessage TextMessageGet(ReadOnlySpan<char> name) {
		throw new NotImplementedException();
	}

	public Sentence? GetSentence(AudioSource? audioSource) {
		if (audioSource != null)
			return audioSource.GetSentence();
		return null;
	}

	public float GetSentenceLength(AudioSource? audioSource) {
		if (audioSource != null && audioSource.SampleRate() > 0) {
			float length = (float)audioSource.SampleCount() / (float)audioSource.SampleRate();
			return length;
		}
		return 0.0f;
	}

	public bool IsStreaming(AudioSource? audioSource) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> Key_BindingForKey(ButtonCode code) {
		throw new NotImplementedException();
	}

	public void StartKeyTrapMode() => Key.StartTrapMode();

	public bool CheckDoneKeyTrapping(out ButtonCode code) => Key.CheckDoneTrapping(out code);

	public void Con_NPrintf(int pos, ReadOnlySpan<char> text) {
#if !SWDS
		Con.NPrintF(pos, text);
#endif
	}

	public bool IsBoxInViewCluster(in Vector3 mins, in Vector3 maxs) {
		throw new NotImplementedException();
	}

	public bool CullBox(in Vector3 mins, in Vector3 maxs) {
		return MathLib.R_CullBoxSkipNear(mins, maxs, g_Frustum);
	}

	public void Sound_ExtraUpdate() {
		Host.Sound.ExtraUpdate();
	}

	public ReadOnlySpan<char> GetGameDirectory() {
		return Common.Gamedir;
	}

	public ref readonly Matrix4x4 WorldToScreenMatrix() => ref g_EngineRenderer.WorldToScreenMatrix();
	public ref readonly Matrix4x4 WorldToViewMatrix() => ref g_EngineRenderer.ViewMatrix();

	public int GameLumpVersion(int lumpId) => ModelLoader.GameLumpVersion(lumpId);

	public int GameLumpSize(int lumpId) => ModelLoader.GameLumpSize(lumpId);
	public bool LoadGameLump(int lumpId, Span<byte> buffer) => ModelLoader.LoadGameLump(lumpId, buffer);

	public int LevelLeafCount() => host_state.WorldBrush!.Leafs!.Length;
	public ISpatialQuery? GetBSPTreeQuery() {
		return g_ToolBSPTree;
	}

	public void LinearToGamma(Span<float> linear, Span<float> gamma) {
		gamma[0] = MathLib.LinearToTexture(linear[0]) / 255.0f;
		gamma[1] = MathLib.LinearToTexture(linear[1]) / 255.0f;
		gamma[2] = MathLib.LinearToTexture(linear[2]) / 255.0f;
	}

	public float LightStyleValue(int style) => Render.LightStyleValue((byte)style);
	public void ComputeDynamicLighting(in Vector3 pt, in Vector3 normal, out Vector3 color) {
		// throw new NotImplementedException();
		// TODO
		color = default;
	}

	public void GetAmbientLightColor(out Vector3 color) {
		BSPDWorldLight? worldLight = ((Render)g_EngineRenderer).FindAmbientLight();
		if (worldLight == null)
			color = new(0, 0, 0);
		else
			MathLib.VectorCopy(worldLight.Value.Intensity, out color);
	}

	public int GetDXSupportLevel() {
		return 0;
	}

	public bool SupportsHDR() {
		throw new NotImplementedException();
	}

	public void Mat_Stub(IMaterialSystem? matSys) {
		throw new NotImplementedException();
	}

	public void GetChapterName(Span<char> buff) {
		throw new NotImplementedException();
	}

	public int GetLevelVersion() {
		throw new NotImplementedException();
	}

	public ref IVoiceTweak GetVoiceTweakAPI() {
		return ref Voice.g_VoiceTweakAPI;
	}

	public void EngineStats_BeginFrame() {
		throw new NotImplementedException();
	}

	public void EngineStats_EndFrame() {
		throw new NotImplementedException();
	}

	public int GetLeavesArea(Span<int> leaves) {
		throw new NotImplementedException();
	}

	public bool DoesBoxTouchAreaFrustum(in Vector3 mins, in Vector3 maxs, int iArea) {
		throw new NotImplementedException();
	}

	public void SetAudioState(in AudioState state) {
		Host.SetAudioState(in state);
	}

	public int SentenceGroupPick(int groupIndex, Span<char> name, int nameBufLen) {
		int pick = g_AudioSystem.SentenceGroupPick(groupIndex, out string found);
		strcpy(name, found);
		return pick;
	}

	public int SentenceGroupPickSequential(int groupIndex, Span<char> name, int nameBufLen, int sentenceIndex, int reset) {
		int pick = g_AudioSystem.SentenceGroupPickSequential(groupIndex, out string found, sentenceIndex, reset != 0);
		strcpy(name, found);
		return pick;
	}

	public int SentenceIndexFromName(ReadOnlySpan<char> sentenceName) {
		g_AudioSystem.LookupSentence(sentenceName, out int sentenceIndex);
		return sentenceIndex;
	}

	public ReadOnlySpan<char> SentenceNameFromIndex(int sentenceIndex) {
		return g_AudioSystem.SentenceNameFromIndex(sentenceIndex);
	}

	public int SentenceGroupIndexFromName(ReadOnlySpan<char> grouname) {
		return g_AudioSystem.SentenceGroupIndexFromName(grouname);
	}

	public ReadOnlySpan<char> SentenceGrounameFromIndex(int groupIndex) {
		return g_AudioSystem.SentenceGroupNameFromIndex(groupIndex);
	}

	public float SentenceLength(int sentenceIndex) {
		return g_AudioSystem.SentenceLength(sentenceIndex);
	}

	public void ComputeLighting(in Vector3 pt, in Vector3 normal, bool clamp, out Vector3 color, Span<Vector3> boxColors = default) {
		throw new NotImplementedException();
	}

	public void ActivateOccluder(int nOccluderIndex, bool bActive) {
		throw new NotImplementedException();
	}

	public bool IsOccluded(in Vector3 absMins, in Vector3 absMaxs) {
		throw new NotImplementedException();
	}

	public static void DebugDrawPhysCollide(PhysCollide collide, IMaterial? material, in Matrix3x4 transform, in Color color, bool drawAxes) {
		material ??= materials.FindMaterial("shadertest/wireframevertexcolor", MaterialDefines.TEXTURE_GROUP_OTHER);

		using MatRenderContextPtr renderContext = new(materials);

		Span<Vector3> outVerts;
		int vertCount = physcollision.CreateDebugMesh(collide, out outVerts);
		if (vertCount != 0) {
			IMesh mesh = renderContext.GetDynamicMesh(true, null, null, material);

			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Triangles, vertCount / 3);

			for (int j = 0; j < vertCount; j++) {
				MathLib.VectorTransform(outVerts[j], transform, out Vector3 @out);
				meshBuilder.Position3fv(@out.Base());
				meshBuilder.Color4ub(color.R, color.G, color.B, color.A);
				meshBuilder.TexCoord2f(0, 0, 0);
				meshBuilder.AdvanceVertex();
			}
			meshBuilder.End();
			mesh.Draw();
		}
		physcollision.DestroyDebugMesh(vertCount, outVerts);

		// draw the axes
		if (drawAxes) {
			Vector3 xaxis = new(10, 0, 0), yaxis = new(0, 10, 0), zaxis = new(0, 0, 10);
			Vector3 @out;

			MathLib.MatrixGetColumn(transform, 3, out Vector3 center);
			IMesh mesh = renderContext.GetDynamicMesh(true, null, null, material);
			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Lines, 3);

			// X
			meshBuilder.Position3fv(center.Base());
			meshBuilder.Color4ub(255, 0, 0, 255);
			meshBuilder.TexCoord2f(0, 0, 0);
			meshBuilder.AdvanceVertex();
			MathLib.VectorTransform(xaxis, transform, out @out);
			meshBuilder.Position3fv(@out.Base());
			meshBuilder.Color4ub(255, 0, 0, 255);
			meshBuilder.TexCoord2f(0, 0, 0);
			meshBuilder.AdvanceVertex();

			// Y
			meshBuilder.Position3fv(center.Base());
			meshBuilder.Color4ub(0, 255, 0, 255);
			meshBuilder.TexCoord2f(0, 0, 0);
			meshBuilder.AdvanceVertex();
			MathLib.VectorTransform(yaxis, transform, out @out);
			meshBuilder.Position3fv(@out.Base());
			meshBuilder.Color4ub(0, 255, 0, 255);
			meshBuilder.TexCoord2f(0, 0, 0);
			meshBuilder.AdvanceVertex();

			// Z
			meshBuilder.Position3fv(center.Base());
			meshBuilder.Color4ub(0, 0, 255, 255);
			meshBuilder.TexCoord2f(0, 0, 0);
			meshBuilder.AdvanceVertex();
			MathLib.VectorTransform(zaxis, transform, out @out);
			meshBuilder.Position3fv(@out.Base());
			meshBuilder.Color4ub(0, 0, 255, 255);
			meshBuilder.TexCoord2f(0, 0, 0);
			meshBuilder.AdvanceVertex();
			meshBuilder.End();

			mesh.Draw();
		}
	}
	public void DebugDrawPhysCollide(PhysCollide collide, IMaterial? material, in Matrix3x4 transform, in Color color) {
		DebugDrawPhysCollide(collide, material, transform, color, false);
	}

	public void CheckPoint(ReadOnlySpan<char> name) {
		throw new NotImplementedException();
	}

	public void DrawPortals() {
		throw new NotImplementedException();
	}

	public bool IsRecordingDemo() {
		return false; // demorecorder.IsRecording();
	}

	public int GetDemoRecordingTick() {
		throw new NotImplementedException();
	}

	public int GetDemoPlaybackTick() {
		throw new NotImplementedException();
	}

	public int GetDemoPlaybackStartTick() {
		throw new NotImplementedException();
	}

	public float GetDemoPlaybackTimeScale() {
		throw new NotImplementedException();
	}

	public int GetDemoPlaybackTotalTicks() {
		throw new NotImplementedException();
	}

	public bool IsTakingScreenshot() {
		// throw new NotImplementedException();
		return false;// todo
	}

	public bool IsHLTV() => false; // not hltv ever, hltv probably will never be implemented

	public void SetOcclusionParameters(in OcclusionParams parms) {
		throw new NotImplementedException();
	}

	public void GetUILanguage(Span<char> dest) {
		strcpy(dest, Source.Engine.CL.cl_language.GetString());
	}

	public ReadOnlySpan<char> GetMapEntitiesString() {
		throw new NotImplementedException();
	}

	public bool IsInEditMode() => false; //todo

	public uint GetEngineBuildNumber() => Protocol.VERSION;

	public ReadOnlySpan<char> GetProductVersionString() {
		throw new NotImplementedException();
	}

	public void GrabPreColorCorrectedFrame(int x, int y, int width, int height) {
		throw new NotImplementedException();
	}

	public bool IsHammerRunning() {
		throw new NotImplementedException();
	}

	public bool MapHasHDRLighting() {
		return modelloader.LastLoadedMapHasHDRLighting();
	}

	public int GetAppID() {
		throw new NotImplementedException();
	}

	public Vector3 GetLightForPointFast(in Vector3 pos, bool bClamp) {
		throw new NotImplementedException();
	}

	public void SetRestrictServerCommands(bool restrict) => cl.RestrictServerCommands = restrict;
	public void SetRestrictClientCommands(bool restrict) => cl.RestrictClientCommands = restrict;

	public void SetOverlayBindProxy(int overlayID, object bindProxy) {
		throw new NotImplementedException();
	}

	public bool CopyFrameBufferToMaterial(ReadOnlySpan<char> materialName) {
		throw new NotImplementedException();
	}

	public void ChangeTeam(ReadOnlySpan<char> teamName) {
		throw new NotImplementedException();
	}

	public void ReadConfiguration(bool readDefault = false) => Host.ReadConfiguration();

	static IAchievementMgr? AchievementMgr;
	public void SetAchievementMgr(IAchievementMgr? achievementMgr) => AchievementMgr = achievementMgr;
	public IAchievementMgr? GetAchievementMgr() => AchievementMgr;

	public bool MapLoadFailed() => serverGlobalVariables.MapLoadFailed;
	public void SetMapLoadFailed(bool state) => serverGlobalVariables.MapLoadFailed = state;
	public bool IsLowViolence() => Host.LowViolence;

	public ReadOnlySpan<char> GetMostRecentSaveGame() {
		throw new NotImplementedException();
	}

	public void SetMostRecentSaveGame(ReadOnlySpan<char> lpszFilename) {
		throw new NotImplementedException();
	}

	public bool IsSaveInProgress() {
		throw new NotImplementedException();
	}

	public uint OnStorageDeviceAttached() {
		throw new NotImplementedException();
	}

	public void OnStorageDeviceDetached() {
		throw new NotImplementedException();
	}

	public void ResetDemoInterpolation() {
		throw new NotImplementedException();
	}

	public void SetGamestatsData(object? gamestatsData) {
		throw new NotImplementedException();
	}

	public object? GetGamestatsData() {
		throw new NotImplementedException();
	}

	public void ServerCmdKeyValues(KeyValues keyValues) => cl.SendServerCmdKeyValues(keyValues);
	public bool IsSkippingPlayback() => false; // TODO: demo support?
	public bool IsLoadingDemo() => false; // TODO: demo support?
	public bool IsPlayingDemoALocallyRecordedDemo() => false; // TODO: demo support?
	public ReadOnlySpan<char> Key_LookupBindingExact(ReadOnlySpan<char> binding) => Key.NameForBindingExact(binding);

	public bool IsWindowedMode() => videoMode!.IsWindowedMode();
	public void FlashWindow() => launcherMgr.FlashWindow(true);

	public int GetClientVersion() => GetSteamInfIDVersionInfo().ClientVersion;
	public bool IsActiveApp() => game.IsActiveApp();

	public void DisconnectInternal() => Host.Disconnect(true, "");

	public int GetInstancesRunningCount() {
		throw new NotImplementedException();
	}

	public void GMOD_SetTimeManipulator(float scaleFramerate) {
		throw new NotImplementedException();
	}

	public void GMOD_SendToServer(ReadOnlySpan<byte> data, int dataBits, bool reliable) {
		CLC_GMod_ClientToServer msg = new();
		msg.SetReliable(reliable);
		msg.ReadPayload(new bf_read(data.ToArray(), data.Length), dataBits);

		if (cl.NetChannel == null) {
			Warning("Client sending to server with no netchannel!\n");
			return;
		}

		cl.NetChannel.SendNetMsg(msg);
	}

	public void GMOD_PlaceDecalMaterial(IMaterial material, bool unk1, int unk2, IClientEntity ent, in Vector3 origin, in Vector3 normal, in Color color, float unk3, float unk4) {
		throw new NotImplementedException();
	}

	public void GMOD_GetSpew(Span<char> spewBuffer) {
		throw new NotImplementedException();
	}

	public void GMOD_SetViewEntity(ClientEntityHandle entity) {
		throw new NotImplementedException();
	}

	public void GMOD_BrushMaterialOverride(IMaterial? matOverride) {
		throw new NotImplementedException();
	}

	public void GMOD_R_RedownloadAllLightmaps(bool unk) {
		throw new NotImplementedException();
	}

	public void GMOD_RawClientCmd_Unrestricted(ReadOnlySpan<char> command) {
		throw new NotImplementedException();
	}

	public IGMODDataTable GMOD_CreateDataTable(GMODDataTableCallbackFn fnCallback) => new GMODDataTable(fnCallback);

	public void GMOD_DestroyDataTable(IGMODDataTable dataTable) { }

	public MDLHandle_t GMOD_LoadModel(ReadOnlySpan<char> path) {
		throw new NotImplementedException();
	}

	public void GMOD_DecalRemoveEntity(int index) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GMOD_TranslateAlias(ReadOnlySpan<char> cmd) {
		throw new NotImplementedException();
	}

	public void GMOD_R_StudioInitLightingCache() {
		throw new NotImplementedException();
	}

	public int PrecacheSentenceFile(ReadOnlySpan<char> fileName) {
		throw new NotImplementedException();
	}

	public float GetPlayerVoiceVolume(CSteamID steamID) {
		throw new NotImplementedException();
	}

	public void SetPlayerVoiceVolume(CSteamID steamID, float volume) {
		throw new NotImplementedException();
	}

	public bool NET_IsHostLocal(ReadOnlySpan<char> hostName) {
		throw new NotImplementedException();
	}

	public bool IsDedicatedServer() {
		throw new NotImplementedException();
	}
}
#endif
