using CommunityToolkit.HighPerformance;

using Game.Client.GarrysMod;
using Game.Client.HL2;
using Game.Client.HUD;
using Game.Shared;
using Game.Shared.GarrysMod;

using Microsoft.Extensions.DependencyInjection;

using Source;
using Source.Common;
using Source.Common.Audio;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Engine;
using Source.Common.Filesystem;
using Source.Common.GUI;
using Source.Common.Hashing;
using Source.Common.Input;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;
using Source.Common.Networking;
using Source.Engine;

using System.Runtime.CompilerServices;
using System.Text;

namespace Game.Client;

public static class CdllExts
{
	public static void TrackBoneSetupEnt(C_BaseAnimating ent) {
		// todo
	}
}

public class HLClient(IServiceProvider services, ClientGlobalVariables gpGlobals, ISurface surface, ViewRender view, IInput input, Hud HUD, UserMessages usermessages, Interpolation Interpolation) : IBaseClientDLL
{
	IVGui? vgui;

	public static void DLLInit(IServiceCollection services) {
		services.AddSingleton<IInput, HLInput>();
		services.AddSingleton<IClientRenderTargets, ClientRenderTargets>();
		services.AddSingleton<ClientEntityList>();
		services.AddSingleton<IClientEntityList>(x => x.GetRequiredService<ClientEntityList>());
		services.AddSingleton<BaseEntityList>(x => x.GetRequiredService<ClientEntityList>());
		services.AddSingleton<IPrediction, Prediction>();
		services.AddSingleton<IViewEffects, ViewEffects>(x => ViewEffects.g_ViewEffects);
		services.AddSingleton<ICenterPrint, CenterPrint>(x => CenterPrint.CenterString);
		services.AddSingleton<ClientLeafSystem>();
		services.AddSingleton<IClientLeafSystem>(x => x.GetRequiredService<ClientLeafSystem>());
		services.AddSingleton<IClientLeafSystemEngine>(x => x.GetRequiredService<ClientLeafSystem>());
		services.AddSingleton<ViewRender>();
		services.AddSingleton<Hud>();
		services.AddSingleton<HudElementHelper>();
		services.AddSingleton<ViewportClientSystem>();
		services.AddSingleton<IViewRender>(x => x.GetRequiredService<ViewRender>());

#if GMOD_DLL
		// Dumb hack, but whatever
		RuntimeHelpers.RunClassConstructor(typeof(GModWeaponParse).TypeHandle);

		garrysmod.DLLInit(services);
#endif

		services.AddSingleton<ViewportClientSystem>();
	}

	public void IN_SetSampleTime(double frameTime) {

	}

	public void LevelInitPreEntity(ReadOnlySpan<char> mapname) {
		// HACK: Bogus, but the logic is too complicated in the engine
		if (g_bLevelInitialized)
			return;
		g_bLevelInitialized = true;

		vieweffects.LevelInit();

		modemanager.LevelInit(mapname);
		IGameSystem.LevelInitPreEntityAllSystems(mapname);
#if GMOD_DLL
		garrysmod.LevelInit(mapname);
#endif

		if (gpGlobals.MaxClients > 1) {
			if (cl_predict.GetInt() == 0)
				engine.ClientCmd("cl_predict 1");
		}
		else {
			if (cl_predict.GetInt() != 0)
				engine.ClientCmd("cl_predict 0");
		}
	}

	public void LevelInitPostEntity() {
		IGameSystem.LevelInitPostEntityAllSystems();
	}

	public void PostInit() {
#if GMOD_DLL
		get.IntroScreen()!.Update("Post Init Systems", true);
		IGameSystem.PostInitAllSystems();
		get.IntroScreen()!.Update("Finished!", true);
		get.IntroScreen()!.End();
		// todo: headtrack
#endif
	}

	public void CreateMove(int sequenceNumber, double inputSampleFrametime, bool active) {
		Assert(C_BaseEntity.IsAbsRecomputationsEnabled());
		Assert(C_BaseEntity.IsAbsQueriesValid());
		using C_BaseAnimating.AutoAllowBoneAccess boneaccess = new(true, true);

		input.CreateMove(sequenceNumber, inputSampleFrametime, active);
	}

	public bool WriteUsercmdDeltaToBuffer(bf_write buf, int from, int to, bool isNewCommand) {
		return input.WriteUsercmdDeltaToBuffer(buf, from, to, isNewCommand);
	}
	public bool DisconnectAttempt() => false;

	public void HudText(ReadOnlySpan<char> text) {

	}

	public bool DispatchUserMessage(int msgType, bf_read msgData) {
		return usermessages.DispatchUserMessage(msgType, msgData);
	}

	public bool Init() {
#if GMOD_DLL
		get.IntroScreen()!.Start();
		get.IntroScreen()!.Update("Start", true);
		garrysmod.InitializeMod(services);

		get.IntroScreen()!.Update("Connecting Soundemitter", true);
		get.IntroScreen()!.Update("Initializing Convars", true);
		get.IntroScreen()!.Update("Initializing Objects", true);
		get.IntroScreen()!.Update("Initializing Particles", true);
		get.IntroScreen()!.Update("Initializing VGUI", true);
		get.IntroScreen()!.Update("Adding Game Systems", true);
#endif
		IGameSystem.Add(g_SoundEmitterSystem);
		IGameSystem.Add(Singleton<ClientLeafSystem>());
		IGameSystem.Add(DetailObjectSystem.GetDetailObjectSystem());
		IGameSystem.Add(Singleton<ViewportClientSystem>());
		IGameSystem.Add(g_ClientShadowMgr);
		IGameSystem.Add(ClientSoundscapeSystem());

		vgui = services.GetService<IVGui>();

#if GMOD_DLL
		get.IntroScreen()!.Update("Initializing Model Manager", true);
#endif
		modemanager.Init();
#if GMOD_DLL
		get.IntroScreen()!.Update("Initializing Viewport", true);
#endif
		// clientMode.InitViewport();
#if GMOD_DLL
		get.IntroScreen()!.Update("Initializing HUD", true);
#endif
		HUD.Init();
#if GMOD_DLL
		get.IntroScreen()!.Update("Initializing Clientmode", true);
#endif
		clientMode.Init();
#if GMOD_DLL
		get.IntroScreen()!.Update("Save Restore Handlers", true);
		get.IntroScreen()!.Update("Initialize All Game Systems", true);
#endif

		if (!IGameSystem.InitAllSystems())
			return false;

		clientMode.Enable();

		view.Init();
		vieweffects.Init();
		C_BaseTempEntity.PrecacheTempEnts();
		input.Init();

		// PhysicsDLLInit
		PhysParseSurfaceData(physprops, filesystem);

		C_World.ClientWorldFactoryInit();

		VGui_CreateGlobalPanels();
		materials.AddModeChangeCallBack(VGui_VideoMode_AdjustForModeChange);

		ClientVoiceMgr_Init();

		// Embed voice status icons inside chat element
		{
			IPanel? parent = enginevgui.GetPanel(VGuiPanelType.ClientDll);
			GetClientVoiceMgr().Init(g_VoiceStatusHelper, parent);
		}

#if GMOD_DLL
		get.IntroScreen()!.Update("Initializing Serverside..", true);
#endif
		return true;
	}

	public void Shutdown() {
		ClientVoiceMgr_Shutdown();
		Game.Client.GarrysMod.GarrysMod.Lua.Kill();
	}

	public void VoiceStatus(int entindex, bool talking) {
		GetClientVoiceMgr().UpdateSpeakerStatus(entindex, talking);
	}

	//-----------------------------------------------------------------------------
	// Helper interface for voice.
	//-----------------------------------------------------------------------------
	class HLVoiceStatusHelper : IVoiceStatusHelper
	{
		public void GetPlayerTextColor(int entindex, Span<int> color) {
			color[0] = color[1] = color[2] = 128;
		}

		public void UpdateCursorState() {
		}

		public bool CanShowSpeakerLabels() {
			return true;
		}
	}
	static readonly HLVoiceStatusHelper g_VoiceStatusHelper = new();

	public static bool g_bLevelInitialized;
	public static INetworkStringTable? g_pStringTableClientSideChoreoScenes;

	public void EncodeUserCmdToBuffer(bf_write buf, int slot) {
		input.EncodeUserCmdToBuffer(buf, slot);
	}

	public void DecodeUserCmdFromBuffer(bf_read buf, int slot) {
		input.DecodeUserCmdFromBuffer(buf, slot);
	}

	public bool HandleUiToggle() {
		return false;
	}

	public void IN_DeactivateMouse() {
		input.DeactivateMouse();
	}

	public void IN_ActivateMouse() {
		input.ActivateMouse();
	}

	public void ExtraMouseSample(double frametime, bool active) {
		input.ExtraMouseSample(frametime, active);
	}

	public void View_Render(ViewRects rects) {
		ref ViewRect rect = ref rects[0];
		if (rect.Width == 0 || rect.Height == 0)
			return;
		view.Render(rects);
	}

	public void InstallStringTableCallback(ReadOnlySpan<char> tableName) {
		switch (tableName) {
			case "VguiScreen": Warning("FIXME: VguiScreen string table not being processed yet\n"); break;
			case "Materials": Warning("FIXME: Materials string table not being processed yet\n"); break;
			case "EffectDispatch": Warning("FIXME: EffectDispatch string table not being processed yet\n"); break;
			// I don't think InfoPanel is a thing?
			case "Scenes":
				g_pStringTableClientSideChoreoScenes = networkstringtable.FindTable(tableName);
				break;
			case "ParticleEffectNames": Warning("FIXME: ParticleEffectNames string table not being processed yet\n"); break;
#if GMOD_DLL
			case Protocol.NETWORKSTRING_TABLENAME:
				Game.Client.GarrysMod.NetworkString.Install();
				break;
			case Protocol.NETWORKVARS_TABLENAME:
				Game.Client.GarrysMod.NetworkVarNames.Install();
				break;
			case Protocol.CLIENT_LUA_FILES_TABLENAME:
				Game.Client.GarrysMod.GModDataPack.DataPack().Initialize();
				break;
#endif
		}
		// todo: review if this exists. Came from Raphaeli's attempts, may not be present now?
		// GarrysMod.GarrysMod.StringTable.Install();

		GameRulesRegister.InstallStringTableCallback_GameRules();
	}

	public int IN_KeyEvent(int eventcode, ButtonCode keynum, ReadOnlySpan<char> currentBinding) {
		return input.KeyEvent(eventcode, keynum, currentBinding);
	}

	public void IN_OnMouseWheeled(int delta) {

	}

	public void IN_ClearStates() {
		input.ClearStates();
	}

	public bool ShouldAllowConsole() => true;
	public bool ShouldDrawDropdownConsole() => true;

	public ClientFrameStage CurFrameStage;

	public void FrameStageNotify(ClientFrameStage stage) {
		CurFrameStage = stage;
		switch (stage) {
			default:
				break;

			case ClientFrameStage.RenderStart:
				OnRenderStart();
				break;
			case ClientFrameStage.NetUpdateStart:
				C_BaseEntity.EnableAbsRecomputations(false);
				C_BaseEntity.SetAbsQueriesValid(false);
				Interpolation.SetLastPacketTimeStamp(engine.GetLastTimeStamp());
				break;
			case ClientFrameStage.NetUpdateEnd:
				C_BaseEntity.EnableAbsRecomputations(true);
				C_BaseEntity.SetAbsQueriesValid(true);
				break;
			case ClientFrameStage.RenderEnd:
				OnRenderEnd();
				break;
			case ClientFrameStage.NetUpdatePostDataUpdateStart:
				break;
			case ClientFrameStage.NetUpdatePostDataUpdateEnd:
				prediction.PostEntityPacketReceived();
				break;
			case ClientFrameStage.Start:
				C_BaseEntity.CheckCLInterpChanged();
				break;
		}
	}

	private void OnRenderStart() {
		// TODO: the rest of this, as features get implemented

		C_BaseEntity.SetAbsQueriesValid(false);
		C_BaseEntity.InterpolateServerEntities();
		C_BaseAnimating.InvalidateBoneCaches();
		C_BaseEntity.SetAbsQueriesValid(true);
		C_BaseEntity.EnableAbsRecomputations(true);
		C_BaseAnimating.PushAllowBoneAccess(true, false, new("OnRenderStart->ViewRender.SetUpView"));

		input.CAM_Think();
		view.OnRenderStart();

		C_BaseAnimating.UpdateClientSideAnimations();

		ProcessOnDataChangedEvents();

		SimulateEntities();
		PhysicsSimulate();

		{
			engine.FireEvents();
			// tempents.Update();
			beams.UpdateTempEntBeams();
		}

		C_BaseEntity.CalcAimEntPositions();

		C_BaseEntity.AddVisibleEntities();
	}

	class DataChangedEvent : IPoolableObject
	{
		public IClientNetworkable? Entity;
		public DataUpdateType UpdateType;
		public ReusableBox<ulong>? StoredEvent;

		public DataChangedEvent() { }
		public void Init(IClientNetworkable ent, DataUpdateType updateType, ReusableBox<ulong> storedEvent) {
			Entity = ent;
			UpdateType = updateType;
			StoredEvent = storedEvent;
		}

		public void Init() { }
		public void Reset() {
			Entity = null;
			UpdateType = 0;
			StoredEvent = null;
		}
	}

	static readonly PooledValueDictionary<DataChangedEvent> DataChangedEvents = new();
	public static bool AddDataChangeEvent(C_BaseEntity ent, DataUpdateType updateType, ReusableBox<ulong> storedEvent) {
		if (storedEvent.Struct != unchecked((ulong)-1)) {
			if (updateType == DataUpdateType.Created)
				DataChangedEvents[storedEvent.Struct].UpdateType = updateType;
			return false;
		}
		else {
			storedEvent.Struct = DataChangedEvents.AddToTail();
			DataChangedEvent ev = DataChangedEvents[storedEvent.Struct];
			ev.Init(ent, updateType, storedEvent);
			return true;
		}
	}

	private static void ProcessOnDataChangedEvents() {
		foreach (var ev in DataChangedEvents) {
			ev.StoredEvent!.Struct = unchecked((ulong)-1);

			// Send the event.
			IClientNetworkable pNetworkable = ev.Entity!;
			pNetworkable.OnDataChanged(ev.UpdateType);
		}
		DataChangedEvents.Purge();
	}

	private void PhysicsSimulate() {
		g_PhysicsSystem.PhysicsSimulate();
	}

	private void SimulateEntities() {
		ClientThinkList().PerformThinkFunctions();
		C_BaseEntityIterator iterator = new C_BaseEntityIterator();
		C_BaseEntity? ent;
		while ((ent = iterator.Next()) != null)
			ent.Simulate();
	}

	private void OnRenderEnd() {
		C_BaseAnimating.PopAllowBoneAccess(new("ViewRender.SetUpView->OnRenderEnd"));
	}

	public ClientClass? GetAllClasses() {
		return ClientClass.Head;
	}

	public RenamedRecvTableInfo? GetRenamedRecvTableInfos() {
		return RenamedRecvTableInfo.Head;
	}

	public void ErrorCreatingEntity(int entityIdx, int classIdx, int serialNumber) {
		Msg($"Entity creation failed.\n");
		Msg($"        Entity ID: {entityIdx}\n");
		Msg($"    Entity Serial: {serialNumber}\n");
		Msg($"         Class ID: {classIdx}\n");
		Msg($"     Class Lookup: {Enum.GetName((StaticClassIndices)classIdx) ?? "Failed"}\n");
	}


	public void InitSprite(EngineSprite? sprite, ReadOnlySpan<char> loadName) {
		sprite?.Init(loadName);
	}

	public int HudVidInit() {
		gHUD.VidInit();

		GetClientVoiceMgr().VidInit();

		return 1;
	}

	public void HudProcessInput(bool active) => gHUD.ProcessInput(active);

	public void HudUpdate(bool active) {
		TimeUnit_t frameTime = gpGlobals.FrameTime;

		GetClientVoiceMgr().Frame(frameTime);

		gHUD.UpdateHud(active);

		BaseAnimating.AutoAllowBoneAccess boneAccess = new(true, true);
		IGameSystem.UpdateAllSystems(frameTime);

		vgui!.GetAnimationController().UpdateAnimations(gpGlobals.CurTime);

		C_BaseTempEntity.CheckDynamicTempEnts();
	}

	public void HudReset() {
		gHUD.VidInit();
		// PhysicsReset();
	}

	public void LevelShutdown() {
		if (!g_bLevelInitialized)
			return;

		g_bLevelInitialized = false;

		C_BaseEntity.EnableAbsRecomputations(false);

		IGameSystem.LevelShutdownPreEntityAllSystems();

		// C_PhysPropClientside.DestroyAll();

		modemanager.LevelShutdown();
		// tempents.LevelShutdown();

		cl_entitylist.Release();

		// C_BaseEntityClassList classList = s_pClassLists;
		// while (classList != null) {
		// 	classList.LevelShutdown();
		// 	classList = classList.NextClassList;
		// }

		IGameSystem.LevelShutdownPostEntityAllSystems();

		view.LevelShutdown();
		beams.ClearBeams();

		// ParticleMgr().RemoveAllEffects();

		// StopAllRumbleEffects();

		// gHUD.LevelShutdown();

		// todo
	}

	public LookupProxyInterfaceFn GetMaterialProxyInterfaceFn() {
		return MaterialProxies.CreateProxyInterfaceFn;
	}

	public void IN_Accumulate() {
		input.AccumulateMouse();
	}

	public bool IN_IsKeyDown(ReadOnlySpan<char> name, out bool isDown) {
		isDown = false;
		return false; // Requires some more input work, probably an overall rework of the input implementation tbh
					  // since I missed a few details
	}

	public void GMOD_RequestLuaFiles() => Game.Client.GarrysMod.GModDataPack.DataPack().RequestFiles();

	public void FileReceived(ReadOnlySpan<char> fileName, uint transferID) {

	}

	public StandardRecvProxies GetStandardRecvProxies() => StandardRecvProxies.g_StandardRecvProxies;

	public bool ReplayInit() {
		throw new NotImplementedException();
	}

	public bool ReplayPostInit() {
		throw new NotImplementedException();
	}

	public void RenderView(in ViewSetup view, ClearFlags nClearFlags, int whatToDraw) {
		throw new NotImplementedException();
	}

	public void View_Fade(ref ScreenFade pSF) {
		throw new NotImplementedException();
	}

	public void SetCrosshairAngle(in QAngle angle) {
		throw new NotImplementedException();
	}

	public void ShutdownSprite(EngineSprite sprite) {
		throw new NotImplementedException();
	}

	public int GetSpriteSize() {
		throw new NotImplementedException();
	}

	public SaveRestoreData? SaveInit(int size) {
		throw new NotImplementedException();
	}

	public void SaveWriteFields(SaveRestoreData data, ReadOnlySpan<char> name, ReadOnlySpan<byte> baseData, DataMap map, ReadOnlySpan<TypeDescription> fields) {
		throw new NotImplementedException();
	}

	public void SaveReadFields(SaveRestoreData data, ReadOnlySpan<char> name, ReadOnlySpan<byte> baseData, DataMap map, ReadOnlySpan<TypeDescription> fields) {
		throw new NotImplementedException();
	}

	public void PreSave(SaveRestoreData data) {
		throw new NotImplementedException();
	}

	public void Save(SaveRestoreData data) {
		throw new NotImplementedException();
	}

	public void WriteSaveHeaders(SaveRestoreData data) {
		throw new NotImplementedException();
	}

	public void ReadRestoreHeaders(SaveRestoreData data) {
		throw new NotImplementedException();
	}

	public void Restore(SaveRestoreData data, bool unk) {
		throw new NotImplementedException();
	}

	public void DispatchOnRestore() {
		throw new NotImplementedException();
	}

	public void WriteSaveGameScreenshot(ReadOnlySpan<char> pFilename) {
		throw new NotImplementedException();
	}

	public void EmitSentenceCloseCaption(ReadOnlySpan<char> tokenstream) {
		throw new NotImplementedException();
	}

	public void EmitCloseCaption(ReadOnlySpan<char> captionname, double duration) {
		throw new NotImplementedException();
	}

	public bool CanRecordDemo(Span<char> errorMsg) {
		throw new NotImplementedException();
	}

	public void OnDemoRecordStart(ReadOnlySpan<char> pDemoBaseName) {
		throw new NotImplementedException();
	}

	public void OnDemoRecordStop() {
		throw new NotImplementedException();
	}

	public void OnDemoPlaybackStart(ReadOnlySpan<char> pDemoBaseName) {
		throw new NotImplementedException();
	}

	public void OnDemoPlaybackStop() {
		throw new NotImplementedException();
	}

	public int GetScreenWidth() {
		throw new NotImplementedException();
	}

	public int GetScreenHeight() {
		throw new NotImplementedException();
	}

	public void WriteSaveGameScreenshotOfSize(ReadOnlySpan<char> pFilename, int width, int height, bool bCreatePowerOf2Padded = false, bool bWriteVTF = false) {
		throw new NotImplementedException();
	}

	public bool GetPlayerView(ref ViewSetup playerView) {
		throw new NotImplementedException();
	}

	public uint GetPresenceID(ReadOnlySpan<char> pIDName) {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> GetPropertyIdString(uint id) {
		throw new NotImplementedException();
	}

	public void GetPropertyDisplayString(uint id, uint value, Span<char> output, int bytes) {
		throw new NotImplementedException();
	}

	public void InvalidateMdlCache() {
		throw new NotImplementedException();
	}

	public void ReloadFilesInList(IFileList filesToReload) {
		throw new NotImplementedException();
	}

	public MouthInfo? GetClientUIMouthInfo() {
		throw new NotImplementedException();
	}

	public ReadOnlySpan<char> TranslateEffectForVisionFilter(ReadOnlySpan<char> pchEffectType, ReadOnlySpan<char> pchEffectName) {
		throw new NotImplementedException();
	}

	public void ClientAdjustStartSoundParams(ref StartSoundParams parms) {
		throw new NotImplementedException();
	}

	public void GMOD_ReceiveServerMessage(bf_read buffer, int len) {
		GModMessageType type = (GModMessageType)buffer.ReadByte();
		switch (type) {
			case GModMessageType.LuaAutoRefresh:
				// todo: GarrysMod::AutoRefresh::HandleChange_Lua(buffer, len);
				return;
			case GModMessageType.RequestLuaFiles:
				return;
			case GModMessageType.LuaCmd:
				Game.Client.GarrysMod.GarrysMod.RunLuaCmd(buffer);
				return;
			case GModMessageType.LuaFile: {
					int index = (int)buffer.ReadUBitLong(16);
					uint size = (uint)((len >> 3) - 3);
					if (size > 0x10000) {
						Msg($"Lua file {index} too big, ignoring! ({size} > {0x10000})\n");
						return;
					}
					byte[] bdata = new byte[size];
					buffer.ReadBytes(bdata);
					Game.Client.GarrysMod.GModDataPack.DataPack().SetFileContents(index, bdata, true);
				}
				return;
		}

		len -= 8;
		if (type != GModMessageType.NetMessage) {
			Msg("Not net message!?\n");
			return;
		}

		int curBit = buffer.BitsRead;
		int bitOffset = curBit % 8;
		int numBits = len + bitOffset;
		byte[] data = new byte[Protocol.Bits2Bytes(numBits)];
		ReadOnlySpan<byte> source = buffer.BaseArray.AsSpan(curBit / 8);
		source[..Math.Min(source.Length, data.Length)].CopyTo(data);

		bf_read read = new("NetMessage(read_cl)", data, data.Length, numBits);
		read.Seek(bitOffset);
		Game.Client.GarrysMod.GarrysMod.RunningNetMessage = true;
		Game.Client.GarrysMod.LuaNet.g_NetIncoming = read;

		if (g_Lua != null && g_Lua.Global() != null) {
			Game.Client.GarrysMod.LuaObject net = new();
			g_Lua.Global().GetMember("net", net);
			if (net.isTable()) {
				Game.Client.GarrysMod.LuaObject incoming = new();
				net.GetMember("Incoming", incoming);
				if (incoming.isFunction()) {
					incoming.Push();
					g_Lua.PushNumber(len);
					g_Lua.CallInternalNoReturns(1);
				}
				incoming.UnReference();
			}
			net.UnReference();
		}

		Game.Client.GarrysMod.GarrysMod.RunningNetMessage = false;
		Game.Client.GarrysMod.LuaNet.g_NetIncoming = null;
	}

	public void GMOD_DoSnapshots() {
		throw new NotImplementedException();
	}

	public void GMOD_VoiceVolume(uint playerID, float volume) {
		throw new NotImplementedException();
	}

	public void GMOD_OnDrawSkybox() {
		throw new NotImplementedException();
	}

	public void IN_MouseWheelAnalog(int value) {
		throw new NotImplementedException();
	}

	public void GMOD_SignOnStateChanged(int userID, int oldState, int newState) {
		throw new NotImplementedException();
	}

	public void GMOD_OnAllSoundsStoppedCL() {
		throw new NotImplementedException();
	}
}
