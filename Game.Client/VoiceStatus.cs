global using static Game.Client.VoiceStatusGlobals;

using Game.Client.HUD;
using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Bitbuffers;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.GUI;
using Source.Common.MaterialSystem;
using Source.Common.Mathematics;

using System.Numerics;

using static Source.Constants;

namespace Game.Client;

// This is provided by each mod to access data that may not be the same across mods.
public interface IVoiceStatusHelper
{
	// Get RGB color for voice status text about this player.
	void GetPlayerTextColor(int entindex, Span<int> color);

	// Force it to update the cursor state.
	void UpdateCursorState();

	// Return true if the voice manager is allowed to show speaker labels
	// (mods usually return false when the scoreboard is up).
	bool CanShowSpeakerLabels();
}

public static class VoiceStatusGlobals
{
	public const double VOICE_MODEL_INTERVAL = 0.3;
	public const float SQUELCHOSCILLATE_PER_SECOND = 2.0f;

#if !GMOD_DLL
	public static readonly ConVar voice_modenable = new("voice_modenable", "1", FCvar.Archive | FCvar.ClientCmdCanExecute, "Enable/disable voice in this mod.");
#endif
	public static readonly ConVar voice_clientdebug = new("voice_clientdebug", "0");

	// ---------------------------------------------------------------------- //
	// The voice manager for the client.
	// ---------------------------------------------------------------------- //
	static VoiceStatus? g_VoiceStatus = null;

	// Get the (global) voice manager.
	public static VoiceStatus GetClientVoiceMgr() {
		if (g_VoiceStatus == null)
			ClientVoiceMgr_Init();

		return g_VoiceStatus!;
	}

	public static void ClientVoiceMgr_Init() {
		if (g_VoiceStatus != null)
			return;

		g_VoiceStatus = new VoiceStatus();
	}

	public static void ClientVoiceMgr_Shutdown() {
		g_VoiceStatus?.Shutdown();
		g_VoiceStatus = null;
	}

	// ---------------------------------------------------------------------- //
	// VoiceStatus.
	// ---------------------------------------------------------------------- //

	internal static VoiceStatus? g_pInternalVoiceStatus = null;

	static void MsgFunc_VoiceMask(bf_read msg) {
		g_pInternalVoiceStatus?.HandleVoiceMaskMsg(msg);
	}

	static void MsgFunc_RequestState(bf_read msg) {
		g_pInternalVoiceStatus?.HandleReqStateMsg(msg);
	}

	internal static void HookMessages() {
		IHudElement.HookMessage("VoiceMask", MsgFunc_VoiceMask);
		IHudElement.HookMessage("RequestState", MsgFunc_RequestState);
	}

	public static float g_flHeadOffset = 35;
	public static float g_flHeadIconSize = 8;
}

public class VoiceStatus
{
	double LastUpdateServerState;        // Last time we called this function.
	int ServerModEnable;             // What we've sent to the server about our "voice_modenable" cvar.

	IPanel? ParentPanel;
	PlayerBitSet VoicePlayers;     // Who is currently talking. Indexed by client index.

	// This is the gamerules-defined list of players that you can hear. It is based on what teams people are on
	// and is totally separate from the ban list. Indexed by client index.
	PlayerBitSet AudiblePlayers;

	// Players who have spoken at least once in the game so far
	PlayerBitSet VoiceEnabledPlayers;

	// This is who the server THINKS we have banned (it can become incorrect when a new player arrives on the server).
	// It is checked periodically, and the server is told to squelch or unsquelch the appropriate players.
	PlayerBitSet ServerBannedPlayers;

	IVoiceStatusHelper? Helper;     // Each mod provides an implementation of this.

	// Squelch mode stuff.
	bool InSquelchMode;

	bool Talking;             // Set to true when the client thinks it's talking.
	bool ServerAcked;         // Set to true when the server knows the client is talking.

	public readonly VoiceBanMgr BanMgr = new();              // Tracks which users we have squelched and don't want to hear.

	IMaterial? HeadLabelMaterial;  // For labels above players' heads.

	bool BanMgrInitialized;

	int ControlSize;

	bool HeadLabelsDisabled;

	public VoiceStatus() {
		ControlSize = 0;
		BanMgrInitialized = false;
		LastUpdateServerState = 0;

		Talking = ServerAcked = false;

		ServerModEnable = -1;

		HeadLabelMaterial = null;

		HeadLabelsDisabled = false;
	}

	public void Shutdown() {
		HeadLabelMaterial?.DecrementReferenceCount();

		g_pInternalVoiceStatus = null;

		ReadOnlySpan<char> gameDir = engine.GetGameDirectory();
		if (!gameDir.IsEmpty) {
			if (BanMgrInitialized)
				BanMgr.SaveState(gameDir);
		}
	}

	// Initialize the cl_dll's voice manager.
	public int Init(IVoiceStatusHelper helper, IPanel? parentPanel) {
		ReadOnlySpan<char> gameDir = engine.GetGameDirectory();
		if (!gameDir.IsEmpty) {
			BanMgr.Init(gameDir);
			BanMgrInitialized = true;
		}

		Assert(g_pInternalVoiceStatus == null);
		g_pInternalVoiceStatus = this;


		HeadLabelMaterial = materials.FindMaterial("voice/icntlk_pl", MaterialDefines.TEXTURE_GROUP_VGUI);
		HeadLabelMaterial?.IncrementReferenceCount();

		InSquelchMode = false;

		Helper = helper;
		ParentPanel = parentPanel;

		HookMessages();

		return 1;
	}

	// ackPosition is the bottom position of where VoiceStatus will draw the voice acknowledgement labels.
	public void VidInit() {
	}

	// Call from HUD_Frame each frame.
	public void Frame(double frametime) {
		// check server banned players once per second
		if (gpGlobals.CurTime - LastUpdateServerState > 1)
			UpdateServerState(false);
	}

	public void SetHeadLabelOffset(float offset) {
		g_flHeadOffset = offset;
	}

	public float GetHeadLabelOffset() {
		return g_flHeadOffset;
	}

	public void SetHeadLabelsDisabled(bool disabled) => HeadLabelsDisabled = disabled;

	// Call from the HUD_CreateEntities function so it can add sprites above player heads.
	public void DrawHeadLabels() {
		if (HeadLabelsDisabled)
			return;

		if (g_pGameRules != null && (g_pGameRules.ShouldDrawHeadLabels() == false))
			return;

		if (HeadLabelMaterial == null)
			return;

		using MatRenderContextPtr renderContext = new(materials);

		for (int i = 0; i < VOICE_MAX_PLAYERS; i++) {
			if (!VoicePlayers.IsBitSet(i))
				continue;

			C_BaseEntity? client = cl_entitylist.GetEnt(i + 1);

			// Don't show an icon if the player is not in our PVS.
			if (client == null || client.IsDormant())
				continue;

			if (client is not C_BasePlayer player)
				continue;

			// Don't show an icon for dead or spectating players (ie: invisible entities).
			if (player.IsPlayerDead())
				continue;

			// Place it 20 units above his head.
			Vector3 origin = player.WorldSpaceCenter();
			origin.Z += g_flHeadOffset;


			// Align it so it never points up or down.
			Vector3 up = new(0, 0, 1);
			Vector3 right = CurrentViewRight();
			if (MathF.Abs(right.Z) > 0.95f)  // don't draw it edge-on
				continue;

			right.Z = 0;
			MathLib.VectorNormalize(ref right);


			float size = g_flHeadIconSize;

			renderContext.Bind(player.GetHeadLabelMaterial()!);
			IMesh mesh = renderContext.GetDynamicMesh();
			MeshBuilder meshBuilder = new();
			meshBuilder.Begin(mesh, MaterialPrimitiveType.Quads, 1);

			meshBuilder.Color3f(1.0f, 1.0f, 1.0f);
			meshBuilder.TexCoord2f(0, 0, 0);
			meshBuilder.Position3fv(origin + (right * -size) + (up * size));
			meshBuilder.AdvanceVertex();

			meshBuilder.Color3f(1.0f, 1.0f, 1.0f);
			meshBuilder.TexCoord2f(0, 1, 0);
			meshBuilder.Position3fv(origin + (right * size) + (up * size));
			meshBuilder.AdvanceVertex();

			meshBuilder.Color3f(1.0f, 1.0f, 1.0f);
			meshBuilder.TexCoord2f(0, 1, 1);
			meshBuilder.Position3fv(origin + (right * size) + (up * -size));
			meshBuilder.AdvanceVertex();

			meshBuilder.Color3f(1.0f, 1.0f, 1.0f);
			meshBuilder.TexCoord2f(0, 0, 1);
			meshBuilder.Position3fv(origin + (right * -size) + (up * -size));
			meshBuilder.AdvanceVertex();
			meshBuilder.End();
			mesh.Draw();
		}
	}

	// Called when a player starts or stops talking.
	// entindex is -1 to represent the local client talking (before the data comes back from the server).
	// When the server acknowledges that the local client is talking, then entindex will be gEngfuncs.GetLocalPlayer().
	// entindex is -2 to represent the local client's voice being acked by the server.
	public void UpdateSpeakerStatus(int entindex, bool talking) {
		if (ParentPanel == null)
			return;

		if (voice_clientdebug.GetInt() != 0)
			Msg($"CVoiceStatus::UpdateSpeakerStatus: ent {entindex} talking = {(talking ? 1 : 0)}\n");

		// Is it the local player talking?
		if (entindex == -1) {
			Talking = talking;
#if !GMOD_DLL
			if (talking) {
				// Enable voice for them automatically if they try to talk.
				engine.ClientCmd("voice_modenable 1");
			}
#endif
		}
		else if (entindex == -2)
			ServerAcked = talking;
		else if (entindex > 0 && entindex <= VOICE_MAX_PLAYERS) {
			int iClient = entindex - 1;
			if (iClient < 0)
				return;

			if (talking) {
				VoicePlayers.Set(iClient, true);
				VoiceEnabledPlayers.Set(iClient, true);
			}
			else
				VoicePlayers.Set(iClient, false);
		}
	}

	void UpdateServerState(bool force) {
		// Can't do anything when we're not in a level.
		if (!HLClient.g_bLevelInitialized) {
			if (voice_clientdebug.GetInt() != 0)
				Msg("CVoiceStatus::UpdateServerState: g_bLevelInitialized\n");

			return;
		}

#if GMOD_DLL
		ConVarRef voice_enable = new("voice_enable");
		int cvarModEnable = voice_enable.GetInt() != 0 ? 1 : 0;
#else
		int cvarModEnable = voice_modenable.GetInt() != 0 ? 1 : 0;
#endif
		if (force || ServerModEnable != cvarModEnable) {
			ServerModEnable = cvarModEnable;

			string modStr = $"VModEnable {ServerModEnable}";
			engine.ServerCmd(modStr);

			if (voice_clientdebug.GetInt() != 0)
				Msg($"CVoiceStatus::UpdateServerState: Sending '{modStr}'\n");
		}

		System.Text.StringBuilder str = new("vban");
		bool change = false;

		for (uint dw = 0; dw < VOICE_MAX_PLAYERS_DW; dw++) {
			uint serverBanMask = 0;
			uint banMask = 0;
			for (uint i = 0; i < 32; i++) {
				int playerIndex = (int)(dw * 32 + i);
				if (playerIndex >= MAX_PLAYERS)
					break;

				if (!engine.GetPlayerInfo(playerIndex + 1, out PlayerInfo pi))
					continue;

				if (BanMgr.GetPlayerBan(pi.GUID))
					banMask |= 1u << (int)i;

				if (ServerBannedPlayers.IsBitSet(playerIndex))
					serverBanMask |= 1u << (int)i;
			}

			if (serverBanMask != banMask)
				change = true;

			// Ok, the server needs to be updated.
			str.Append($" {banMask:x}");
		}

		if (change || force) {
			if (voice_clientdebug.GetInt() != 0)
				Msg($"CVoiceStatus::UpdateServerState: Sending '{str}'\n");

			engine.ServerCmd(str.ToString(), false);  // Tell the server..
		}
		else {
			if (voice_clientdebug.GetInt() != 0)
				Msg("CVoiceStatus::UpdateServerState: no change\n");
		}

		LastUpdateServerState = gpGlobals.CurTime;
	}

	// Called when the server registers a change to who this client can hear.
	public void HandleVoiceMaskMsg(bf_read msg) {
		uint dw;
		for (dw = 0; dw < VOICE_MAX_PLAYERS_DW; dw++) {
			AudiblePlayers.SetWord((int)dw, (uint)msg.ReadLong());
			ServerBannedPlayers.SetWord((int)dw, (uint)msg.ReadLong());

			if (voice_clientdebug.GetInt() != 0) {
				Msg("CVoiceStatus::HandleVoiceMaskMsg\n");
				Msg($"    - m_AudiblePlayers[{dw}] = {AudiblePlayers.GetWord((int)dw)}\n");
				Msg($"    - m_ServerBannedPlayers[{dw}] = {ServerBannedPlayers.GetWord((int)dw)}\n");
			}
		}

		ServerModEnable = msg.ReadByte();
	}

	// The server sends this message initially to tell the client to send their state.
	public void HandleReqStateMsg(bf_read msg) {
		if (voice_clientdebug.GetInt() != 0)
			Msg("CVoiceStatus::HandleReqStateMsg\n");

		UpdateServerState(true);
	}

	// When you enter squelch mode, pass in
	public void StartSquelchMode() {
		if (InSquelchMode)
			return;

		InSquelchMode = true;
		Helper!.UpdateCursorState();
	}

	public void StopSquelchMode() {
		InSquelchMode = false;
		Helper!.UpdateCursorState();
	}

	public bool IsInSquelchMode() {
		return InSquelchMode;
	}

	// returns true if the target client has been banned
	// playerIndex is of range 1..maxplayers
	public bool IsPlayerBlocked(int player) {
		if (!engine.GetPlayerInfo(player, out PlayerInfo pi))
			return false;

		return BanMgr.GetPlayerBan(pi.GUID);
	}

	// returns false if the player can't hear the other client due to game rules (eg. the other team)
	public bool IsPlayerAudible(int player) {
		return AudiblePlayers.IsBitSet(player - 1);
	}

	// returns true if the player is currently speaking
	public bool IsPlayerSpeaking(int playerIndex) {
		return VoicePlayers.IsBitSet(playerIndex - 1);
	}

	// returns true if the local player is attempting to speak
	public bool IsLocalPlayerSpeaking() {
		return Talking;
	}

	// blocks the target client from being heard
	public void SetPlayerBlockedState(int player, bool blocked) {
		if (voice_clientdebug.GetInt() != 0)
			Msg("CVoiceStatus::SetPlayerBlockedState part 1\n");

		if (!engine.GetPlayerInfo(player, out PlayerInfo pi))
			return;

		if (voice_clientdebug.GetInt() != 0)
			Msg("CVoiceStatus::SetPlayerBlockedState part 2\n");

		// Squelch or (try to) unsquelch this player.
		if (voice_clientdebug.GetInt() != 0)
			Msg($"CVoiceStatus::SetPlayerBlockedState: setting player {player} ban to {(!BanMgr.GetPlayerBan(pi.GUID) ? 1 : 0)}\n");

		BanMgr.SetPlayerBan(pi.GUID, !BanMgr.GetPlayerBan(pi.GUID));
		UpdateServerState(false);
	}

	public void SetHeadLabelMaterial(ReadOnlySpan<char> material) {
		if (HeadLabelMaterial != null) {
			HeadLabelMaterial.DecrementReferenceCount();
			HeadLabelMaterial = null;
		}

		HeadLabelMaterial = materials.FindMaterial(material, MaterialDefines.TEXTURE_GROUP_VGUI);
		HeadLabelMaterial?.IncrementReferenceCount();
	}

	public IMaterial? GetHeadLabelMaterial() => HeadLabelMaterial;
}
