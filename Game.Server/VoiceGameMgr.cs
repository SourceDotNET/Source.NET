global using static Game.Server.VoiceGameMgrGlobals;

using Game.Shared;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Server;

using System.Globalization;

using static Source.Constants;

namespace Game.Server;

public interface IVoiceGameMgrHelper
{
	// Called each frame to determine which players are allowed to hear each other.	This overrides
	// whatever squelch settings players have.
	bool CanPlayerHearPlayer(BasePlayer listener, BasePlayer talker, ref bool proximity);
}

public static class VoiceGameMgrGlobals
{
	public const double UPDATE_INTERVAL = 0.3;

	// These are stored off as VoiceGameMgr is created and deleted.
	public static PlayerBitSet g_PlayerModEnable;      // Set to 1 for each player if the player wants to use voice in this mod.
													   // (If it's zero, then the server reports that the game rules are saying the
													   // player can't hear anyone).

	public static readonly PlayerBitSet[] g_BanMasks = new PlayerBitSet[VOICE_MAX_PLAYERS];  // Tells which players don't want to hear each other.
																							  // These are indexed as clients and each bit represents a client
																							  // (so player entity is bit+1).

	public static readonly PlayerBitSet[] g_SentGameRulesMasks = new PlayerBitSet[VOICE_MAX_PLAYERS];    // These store the masks we last sent to each client so we can determine if
	public static readonly PlayerBitSet[] g_SentBanMasks = new PlayerBitSet[VOICE_MAX_PLAYERS];          // we need to resend them.
	public static PlayerBitSet g_bWantModEnable;

	public static readonly ConVar voice_serverdebug = new("voice_serverdebug", "0");

	// Set game rules to allow all clients to talk to each other.
	// Muted players still can't talk to each other.
#if GMOD_DLL
	public static readonly ConVar sv_alltalk = new("sv_alltalk", "0", FCvar.Notify, "Players can hear all other players, no team restrictions");
#else
	public static readonly ConVar sv_alltalk = new("sv_alltalk", "0", FCvar.Notify | FCvar.Replicated, "Players can hear all other players, no team restrictions");
#endif

	public static readonly VoiceGameMgr g_VoiceGameMgr = new();

#if HL2MP
	public static readonly GarrysMod.VoiceGameMgrHelper g_VoiceGameMgrHelper = new();
	public static IVoiceGameMgrHelper? g_pVoiceGameMgrHelper = g_VoiceGameMgrHelper;
#else
	public static IVoiceGameMgrHelper? g_pVoiceGameMgrHelper;
#endif

	public static void VoiceServerDebug(ReadOnlySpan<char> msg) {
		if (voice_serverdebug.GetInt() == 0)
			return;

		Msg(msg);
	}

	// Use this to access VoiceGameMgr.
	public static VoiceGameMgr GetVoiceGameMgr() => g_VoiceGameMgr;
}

// VoiceGameMgr manages which clients can hear which other clients.
public class VoiceGameMgr
{
#if GMOD_DLL
	double LastVoicePacketTime;
#endif
	IVoiceGameMgrHelper? Helper;
	int MaxPlayers;
	double UpdateInterval;                     // How long since the last update.
#if GMOD_DLL
	double FullUpdateInterval;
#endif
	int ProximityDistance;

	public VoiceGameMgr() {
		UpdateInterval = 0;
		MaxPlayers = 0;
		ProximityDistance = -1;
	}

	public bool Init(IVoiceGameMgrHelper? helper, int maxClients) {
		Helper = helper;
		MaxPlayers = VOICE_MAX_PLAYERS < maxClients ? VOICE_MAX_PLAYERS : maxClients;

		return true;
	}

	public void SetHelper(IVoiceGameMgrHelper? helper) {
		Helper = helper;
	}

	// Updates which players can hear which other players.
	// If gameplay mode is DM, then only players within the PVS can hear each other.
	// If gameplay mode is teamplay, then only players on the same team can hear each other.
	// Player masks are always applied.
	public void Update(double frametime) {
#if GMOD_DLL
		UpdateInterval += frametime;
		FullUpdateInterval += frametime;
		if (UpdateInterval < UPDATE_INTERVAL)
			return;

		if (gpGlobals.CurTime - LastVoicePacketTime > UPDATE_INTERVAL && UpdateInterval < 5.0)
			return;

		if (UpdateInterval > 3.0 || FullUpdateInterval > 5.0) {
			FullUpdateInterval = 0;
			UpdatePlayers(true, null);
		}
		else
			UpdatePlayers(false, null);
#else
		// Only update periodically.
		UpdateInterval += frametime;
		if (UpdateInterval < UPDATE_INTERVAL)
			return;

		UpdateMasks();
#endif
	}

#if GMOD_DLL
	public void OnReceivedVoicePacket(BaseEntity? entity) {
		if (entity == null || !entity.IsPlayer())
			return;

		BasePlayer player = (BasePlayer)entity;
		bool startedTalking = gpGlobals.CurTime - player.LastVoicePacketTime > UPDATE_INTERVAL;
		player.OnVoicePacketReceived();
		LastVoicePacketTime = gpGlobals.CurTime;

		if (startedTalking)
			UpdatePlayers(false, player);
	}

	void UpdatePlayers(bool full, BasePlayer? talker) {
		UpdateInterval = 0;

		for (int iClient = 0; iClient < MaxPlayers; iClient++) {
			BaseEntity? ent = Util.PlayerByIndex(iClient + 1);
			if (ent != null && ent.IsPlayer())
				UpdatePlayer((BasePlayer)ent, full, talker);
		}
	}
#endif

	// Called when a new client connects (unsquelches its entity for everyone).
	public void ClientConnected(Edict? edict) {
#if GMOD_DLL
		if (edict == null)
			return;
#endif
		int index = edict!.EdictIndex - 1;
#if GMOD_DLL
		if ((uint)index >= VOICE_MAX_PLAYERS)
			return;
#endif

		// Clear out everything we use for deltas on this guy.
		g_bWantModEnable.Set(index, true);
		g_SentGameRulesMasks[index].ClearAll();
		g_SentBanMasks[index].ClearAll();
	}

	// Called on ClientCommand. Checks for the squelch and unsquelch commands.
	// Returns true if it handled the command.
	public bool ClientCommand(BasePlayer player, in TokenizedCommand args) {
		int playerClientIndex = player.EntIndex() - 1;
		if (playerClientIndex < 0 || playerClientIndex >= MaxPlayers) {
			VoiceServerDebug($"CVoiceGameMgr::ClientCommand: cmd {args[0]} from invalid client ({playerClientIndex})\n");
			return true;
		}

		bool ban = args[0].Equals("vban", StringComparison.OrdinalIgnoreCase);
		if (ban && args.ArgC() >= 2) {
			for (int i = 1; i < args.ArgC(); i++) {
				uint.TryParse(args[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint mask);

				if (i <= VOICE_MAX_PLAYERS_DW) {
					VoiceServerDebug($"CVoiceGameMgr::ClientCommand: vban (0x{mask:x}) from {playerClientIndex}\n");
					g_BanMasks[playerClientIndex].SetWord(i - 1, mask);
				}
				else
					VoiceServerDebug($"CVoiceGameMgr::ClientCommand: invalid index ({i})\n");
			}

			// Force it to update the masks now.
			//UpdateMasks();
			return true;
		}
		else if (args[0].Equals("VModEnable", StringComparison.OrdinalIgnoreCase) && args.ArgC() >= 2) {
			VoiceServerDebug($"CVoiceGameMgr::ClientCommand: VModEnable ({(atoi(args[1]) != 0 ? 1 : 0)})\n");
			g_PlayerModEnable.Set(playerClientIndex, atoi(args[1]) != 0);
			g_bWantModEnable.Set(playerClientIndex, false);
			//UpdateMasks();
			return true;
		}
		else
			return false;
	}

	public bool CheckProximity(int distance) {
		if (ProximityDistance >= distance)
			return true;

		return false;
	}

	public void SetProximityDistance(int distance) {
		ProximityDistance = distance;
	}

	public bool IsPlayerIgnoringPlayer(int talker, int listener) {
		return g_BanMasks[listener - 1].IsBitSet(talker - 1);
	}

#if GMOD_DLL
	void UpdatePlayer(BasePlayer player, bool full, BasePlayer? talker) {
		int playerIndex = player.EntIndex();
		int iClient = playerIndex - 1;

		SingleUserRecipientFilter user = new(player);

		// Request the state of their "VModEnable" cvar
		if (g_bWantModEnable.IsBitSet(iClient)) {
			UserMessageBegin(user, "RequestState");
			MessageEnd();
			// Only send it once
			g_bWantModEnable.Set(iClient, false);
		}

		PlayerBitSet gameRulesMask = default;
		PlayerBitSet proximityMask = default;
		if (g_PlayerModEnable.IsBitSet(iClient)) {
			bool proximity = false;

			// Build a mask of who they can hear based on the game rules
			for (int iOtherClient = 0; iOtherClient < MaxPlayers; iOtherClient++) {
				BaseEntity? otherEnt = Util.PlayerByIndex(iOtherClient + 1);
				if (otherEnt == null || !otherEnt.IsPlayer())
					continue;

				BasePlayer other = (BasePlayer)otherEnt;
				bool canHear;
				if (full || other == talker || gpGlobals.CurTime - other.LastVoicePacketTime < 1.0)
					canHear = Helper!.CanPlayerHearPlayer(player, other, ref proximity);
				else {
					canHear = g_pVoiceServer.GetClientListening(playerIndex, iOtherClient + 1);
					proximity = g_pVoiceServer.GetClientProximity(playerIndex, iOtherClient + 1);
				}

				if (canHear) {
					gameRulesMask.Set(iOtherClient, true);
					proximityMask.Set(iOtherClient, proximity);
				}
			}
		}

		// If this is different from what the client has, send an update
		if (gameRulesMask != g_SentGameRulesMasks[iClient] ||
			g_BanMasks[iClient] != g_SentBanMasks[iClient]) {
			g_SentGameRulesMasks[iClient] = gameRulesMask;
			g_SentBanMasks[iClient] = g_BanMasks[iClient];

			UserMessageBegin(user, "VoiceMask");
			int dw;
			for (dw = 0; dw < VOICE_MAX_PLAYERS_DW; dw++) {
				MessageWriteLong((int)gameRulesMask.GetWord(dw));
				MessageWriteLong((int)g_BanMasks[iClient].GetWord(dw));
			}
			MessageWriteByte(g_PlayerModEnable.IsBitSet(iClient) ? 1 : 0);
			MessageEnd();
		}

		// Tell the engine.
		for (int iOtherClient = 0; iOtherClient < MaxPlayers; iOtherClient++) {
			bool canHear = gameRulesMask.IsBitSet(iOtherClient) && !g_BanMasks[iClient].IsBitSet(iOtherClient);
			g_pVoiceServer.SetClientListening(playerIndex, iOtherClient + 1, canHear);

			if (canHear)
				g_pVoiceServer.SetClientProximity(playerIndex, iOtherClient + 1, proximityMask.IsBitSet(iOtherClient));
		}
	}
#else
	// Force it to update the client masks.
	void UpdateMasks() {
		UpdateInterval = 0;

		bool allTalk = sv_alltalk.GetInt() != 0;

		for (int iClient = 0; iClient < MaxPlayers; iClient++) {
			BaseEntity? ent = Util.PlayerByIndex(iClient + 1);
			if (ent == null || !ent.IsPlayer())
				continue;

			BasePlayer player = (BasePlayer)ent;

			SingleUserRecipientFilter user = new(player);

			// Request the state of their "VModEnable" cvar.
			if (g_bWantModEnable.IsBitSet(iClient)) {
				UserMessageBegin(user, "RequestState");
				MessageEnd();
				// Since this is reliable, only send it once
				g_bWantModEnable.Set(iClient, false);
			}

			PlayerBitSet gameRulesMask = default;
			PlayerBitSet proximityMask = default;
			bool proximity = false;
			if (g_PlayerModEnable.IsBitSet(iClient)) {
				// Build a mask of who they can hear based on the game rules.
				for (int iOtherClient = 0; iOtherClient < MaxPlayers; iOtherClient++) {
					BaseEntity? otherEnt = Util.PlayerByIndex(iOtherClient + 1);
					if (otherEnt != null && otherEnt.IsPlayer() &&
						(allTalk || Helper!.CanPlayerHearPlayer(player, (BasePlayer)otherEnt, ref proximity))) {
						gameRulesMask.Set(iOtherClient, true);
						proximityMask.Set(iOtherClient, proximity);
					}
				}
			}

			// If this is different from what the client has, send an update.
			if (gameRulesMask != g_SentGameRulesMasks[iClient] ||
				g_BanMasks[iClient] != g_SentBanMasks[iClient]) {
				g_SentGameRulesMasks[iClient] = gameRulesMask;
				g_SentBanMasks[iClient] = g_BanMasks[iClient];

				UserMessageBegin(user, "VoiceMask");
				int dw;
				for (dw = 0; dw < VOICE_MAX_PLAYERS_DW; dw++) {
					MessageWriteLong((int)gameRulesMask.GetWord(dw));
					MessageWriteLong((int)g_BanMasks[iClient].GetWord(dw));
				}
				MessageWriteByte(g_PlayerModEnable.IsBitSet(iClient) ? 1 : 0);
				MessageEnd();
			}

			// Tell the engine.
			for (int iOtherClient = 0; iOtherClient < MaxPlayers; iOtherClient++) {
				bool canHear = gameRulesMask.IsBitSet(iOtherClient) && !g_BanMasks[iClient].IsBitSet(iOtherClient);
				g_pVoiceServer.SetClientListening(iClient + 1, iOtherClient + 1, canHear);

				if (canHear)
					g_pVoiceServer.SetClientProximity(iClient + 1, iOtherClient + 1, proximityMask.IsBitSet(iOtherClient));
			}
		}
	}
#endif
}
