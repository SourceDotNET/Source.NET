global using static Game.Server.GameServerClientGlobals;

using Game.Server.GarrysMod;
using Game.Shared;
using Game.Shared.GarrysMod;

using Source;
using Source.Common;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Formats.BSP;
using Source.Common.GarrysMod.Lua;

using System.Numerics;

namespace Game.Server;

public static class GameServerClientGlobals
{
	public static ConVar sv_cheats { get => field ??= cvar.FindVar("sv_cheats")!; }

	public static BaseEntity? GetNextCommandEntity(BasePlayer? player, ReadOnlySpan<char> name, BaseEntity? ent) {
		if (player == null)
			return null;

		if (FStrEq(name, "")) {
			if (ent != null)
				return null;

			return FindPickerEntity(player);
		}

		int index = atoi(name);
		if (index != 0) {
			if (ent != null)
				return null;

			return BaseEntity.Instance(index);
		}

		while ((ent = gEntList.NextEnt(ent)) != null)
			if ((ent.GetEntityName() != null && ent.NameMatches(name)) || (ent.Classname != null && ent.ClassMatches(name)))
				return ent;

		return null;
	}

	public static void ClientCommand(BasePlayer? player, in TokenizedCommand args) {
		ReadOnlySpan<char> cmd = args[0];

		if (player == null)
			return;

		if (FStrEq(cmd, "killtarget")) {
			// if (g_pDeveloper.GetBool() && sv_cheats.GetBool() && Util.IsCommandIssuedByServerAdmin())
			// 	ConsoleKillTarget(player, args[1]);
		}
		else if (FStrEq(cmd, "demorestart")) {
			// player.ForceClientDllUpdate();
		}
		else if (FStrEq(cmd, "fade"))
			Util.ScreenFade(player, new(32, 63, 100, 200), 3, 3, FadeFlags.Out);
		else if (FStrEq(cmd, "te")) {
			if (sv_cheats.GetBool() && Util.IsCommandIssuedByServerAdmin()) {
				if (FStrEq(args[1], "stop")) {
					BaseEntity? ent = gEntList.FindEntityByClassname(null, "te_tester");
					while (ent != null) {
						BaseEntity? next = gEntList.FindEntityByClassname(ent, "te_tester");
						Util.Remove(ent);
						ent = next;
					}
				}
				// else
				// 	TempEntTester.Create(player.WorldSpaceCenter(), player.EyeAngles(), args[1], args[2]);
			}
		}
		else {
			if (!g_pGameRules.ClientCommand(player, args)) {
				if (strlen(cmd) > 128)
					Util.ClientPrint(player, Shared.HudPrint.Console, "Console command too long.\n");
				else
					Util.ClientPrint(player, Shared.HudPrint.Console, $"Unknown command: {cmd}\n");
			}
		}
	}

	public static void SetDebugBits(BasePlayer? player, ReadOnlySpan<char> name, DebugOverlayBits bit) {
		if (player == null)
			return;

		BaseEntity? entity = null;
		while ((entity = GetNextCommandEntity(player, name, entity)) != null)
			if ((entity.DebugOverlays & bit) != 0)
				entity.DebugOverlays &= ~bit;
			else
				entity.DebugOverlays |= bit;
	}
}

[EngineComponent]
public class GameServerClientMethods
{
	public const TimeUnit_t TALK_INTERVAL = 0.66; // min time between say commands from a client

	[ConCommand(helpText: "Display player message")]
	void say(in TokenizedCommand args, CommandSource source, int clientslot) {
		BasePlayer? player = ToBasePlayer(Util.GetCommandClient());
		if (player != null) {
			if ((player.LastTimePlayerTalked() + TALK_INTERVAL) < gpGlobals.CurTime) {
				HostSV.Host_Say(player.Edict(), args, false);
				player.NotePlayerTalked();
			}
		}
		// This will result in a "console" say.  Ignore anything from
		// an index greater than 0 when we don't have a player pointer, 
		// as would be the case when a client that's connecting generates 
		// text via a script.  This can be exploited to flood everyone off.
		else if (Util.GetCommandClientIndex() == 0) {
			HostSV.Host_Say(null, args, false);
		}
	}


	static void kill_helper(in TokenizedCommand args, bool explode) {
		// TODO: if (args.ArgC() > 1 && sv_cheats.GetBool()) {
		// TODO: 	// Find the matching netname
		// TODO: 	for (int i = 1; i <= gpGlobals->maxClients; i++) {
		// TODO: 		CBasePlayer* pPlayer = ToBasePlayer(UTIL_PlayerByIndex(i));
		// TODO: 		if (pPlayer) {
		// TODO: 			if (Q_strstr(pPlayer->GetPlayerName(), args[1])) {
		// TODO: 				pPlayer->CommitSuicide(bExplode);
		// TODO: 			}
		// TODO: 		}
		// TODO: 	}
		// TODO: }
		//else {
		BasePlayer? player = Util.GetCommandClient();
		if (player != null) {
#if GMOD_DLL
			if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.CanPlayerSuicide)) {
				LuaEntity.Push_Entity(player);
				if (!gGM.CallFinish(1))
					return;
			}
#endif
			player.CommitSuicide(explode);
		}
		//}
	}

	[ConCommand(helpText: "Kills the player with generic damage")]
	static void kill(in TokenizedCommand args) => kill_helper(in args, false);
	[ConCommand(helpText: "Kills the player with explosive damage")]
	static void explode(in TokenizedCommand args) => kill_helper(in args, true);

	public static void ClientPrecache() {
		BaseEntity.PrecacheModel("cable/cable.vmt");
		BaseEntity.PrecacheModel("cable/cable_lit.vmt");
		BaseEntity.PrecacheModel("cable/chain.vmt");
		BaseEntity.PrecacheModel("cable/rope.vmt");
		BaseEntity.PrecacheModel("sprites/blueglow1.vmt");
		BaseEntity.PrecacheModel("sprites/purpleglow1.vmt");
		BaseEntity.PrecacheModel("sprites/purplelaser1.vmt");

#if !HL2MP
		BaseEntity::PrecacheScriptSound("Hud.Hint");
#endif
		BaseEntity.PrecacheScriptSound("Player.FallDamage");
		BaseEntity.PrecacheScriptSound("Player.Swim");

		// General HUD sounds
		BaseEntity.PrecacheScriptSound("Player.PickupWeapon");
		BaseEntity.PrecacheScriptSound("Player.DenyWeaponSelection");
		BaseEntity.PrecacheScriptSound("Player.WeaponSelected");
		BaseEntity.PrecacheScriptSound("Player.WeaponSelectionClose");
		BaseEntity.PrecacheScriptSound("Player.WeaponSelectionMoveSlot");

		// General legacy temp ents sounds
		BaseEntity.PrecacheScriptSound("Bounce.Glass");
		BaseEntity.PrecacheScriptSound("Bounce.Metal");
		BaseEntity.PrecacheScriptSound("Bounce.Flesh");
		BaseEntity.PrecacheScriptSound("Bounce.Wood");
		BaseEntity.PrecacheScriptSound("Bounce.Shrapnel");
		BaseEntity.PrecacheScriptSound("Bounce.ShotgunShell");
		BaseEntity.PrecacheScriptSound("Bounce.Shell");
		BaseEntity.PrecacheScriptSound("Bounce.Concrete");

		ClientGamePrecache();
	}

	public static ReadOnlySpan<char> CheckChatText(BasePlayer? player, ReadOnlySpan<char> text) => text[..Math.Min(text.Length, 127)];
}

public static class HostSV
{
#if GMOD_DLL
	public static void Host_Say(Edict? edict, in TokenizedCommand args, bool teamOnly) {
		Span<char> text = stackalloc char[256];
		Span<char> temp = stackalloc char[256];
		Span<char> replaced = stackalloc char[256];
		scoped ReadOnlySpan<char> p;

		if (args.ArgC() == 0)
			return;

		ReadOnlySpan<char> cmd = args[0];
		if (stricmp(cmd, "say") == 0 || stricmp(cmd, "say_team") == 0) {
			if (args.ArgC() < 2)
				return;
			p = args.ArgS();
		}
		else {
			if (args.ArgC() < 2)
				sprintf(temp, "%s").S(cmd);
			else
				sprintf(temp, "%s %s").S(cmd).S(args.ArgS());
			p = temp.SliceNullTerminatedString();
		}

		BasePlayer? player = null;
		if (edict != null) {
			player = (BasePlayer?)BaseEntity.Instance(edict);
			p = GameServerClientMethods.CheckChatText(player, p);
			if (p.IsEmpty || !player!.CanSpeak())
				return;

			player.CheckChatText(p[..Math.Min(p.Length, 127)]);

			if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerSay)) {
				LuaEntity.Push_Entity(player);
				g_Lua!.PushString(p);
				g_Lua.PushBool(teamOnly);
				if (gGM.CallReturns(3, 1)) {
					ILuaObject ret = g_Lua.GetReturn(0);
					string? str = ret.GetString();
					if (str == null) {
						if (ret.GetType() == LuaType.Bool) {
							if (!ret.GetBool())
								return;
						}
						else
							g_Lua.ErrorFromLua("Error: PlayerSay hook returned a non-string!\n");
					}
					else {
						sprintf(replaced, "%s").S(str);
						p = replaced.SliceNullTerminatedString();
					}
				}
				if (p.IsEmpty)
					return;
			}
		}

		ReadOnlySpan<char> prefix = null;
		if (g_pGameRules != null)
			prefix = g_pGameRules.GetChatPrefix(teamOnly, player);
		ReadOnlySpan<char> playerName = player != null ? player.GetPlayerName() : "Console";

		if (prefix.IsStringEmpty)
			sprintf(text, "%s: ").S(playerName);
		else
			sprintf(text, "%s %s: ").S(prefix).S(playerName);

		nint j = text.Length - 2 - strlen(text);
		if (strlen(p) > j)
			p = p[..(int)j];

		strcat(text, p);
		strcat(text, "\n");
		ReadOnlySpan<char> fullText = text.SliceNullTerminatedString();

		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			BasePlayer? client = Util.PlayerByIndex(i);
			if (client == null || client.Edict() == null || client.Edict() == edict || !client.IsNetClient())
				continue;

			if (gGM != null && gGM.CallWithArgs((int)LUA_POOLEDSTRING.PlayerCanSeePlayersChat)) {
				g_Lua!.PushString(p);
				g_Lua.PushBool(teamOnly);
				LuaEntity.Push_Entity(client);
				LuaEntity.Push_Entity(player);
				if (!gGM.CallFinish(4))
					continue;
			}

			SingleUserRecipientFilter user = new(client);
			user.MakeReliable();
			Util.SayTextFilter(user, p, player, true, teamOnly, player != null && !player.IsAlive());
		}

		if (player != null) {
			SingleUserRecipientFilter user = new(player);
			user.MakeReliable();
			Util.SayTextFilter(user, p, player, true, teamOnly, !player.IsAlive());
		}

		if (engine.IsDedicatedServer())
			Msg(fullText);

		int userid = 0;
		ReadOnlySpan<char> networkID = "Console";
		ReadOnlySpan<char> logName = "Console";
		ReadOnlySpan<char> playerTeam = "Console";
		if (player != null) {
			userid = engine.GetPlayerUserId(player.Edict());
			networkID = player.GetNetworkIDString();
			logName = player.GetPlayerName();
			Team? team = player.GetTeam();
			playerTeam = team != null ? team.GetName() : "Team";
		}

		if (teamOnly)
			Util.LogPrintf($"\"{logName}<{userid}><{networkID}><{playerTeam}>\" say_team \"{p}\"\n");
		else
			Util.LogPrintf($"\"{logName}<{userid}><{networkID}><{playerTeam}>\" say \"{p}\"\n");

		IGameEvent? ev = gameeventmanager.CreateEvent("player_say", false);
		if (ev != null) {
			ev.SetInt("userid", userid);
			ev.SetString("text", p);
			ev.SetInt("priority", 1);
			ev.SetBool("teamonly", teamOnly);
			gameeventmanager.FireEvent(ev, false);
		}
	}
#else
	public static void Host_Say(Edict? edict, in TokenizedCommand args, bool teamOnly) {
		BasePlayer? client;
		nint j;
		scoped ReadOnlySpan<char> p;
		Span<char> text = stackalloc char[256];
		Span<char> temp = stackalloc char[256];
		ReadOnlySpan<char> say = "say";
		ReadOnlySpan<char> sayTeam = "say_team";
		ReadOnlySpan<char> cmd = args[0];
		bool senderDead = false;

		// We can get a raw string now, without the "say " prepended
		if (args.ArgC() == 0)
			return;

		if (stricmp(cmd, say) == 0 || stricmp(cmd, sayTeam) == 0) {
			if (args.ArgC() >= 2)
				p = args.ArgS();
			else // say with a blank message, nothing to do
				return;
		}
		else  // Raw text, need to prepend argv[0]
		{
			if (args.ArgC() >= 2)
				sprintf(temp, "%s %s").S(cmd).S(args.ArgS());
			else
				// Just a one word command, use the first word...sigh
				sprintf(temp, "%s").S(cmd);

			p = temp;
		}

		BasePlayer? player = null;
		if (edict != null) {
			player = ((BasePlayer?)BaseEntity.Instance(edict));
			Assert(player != null);

			// make sure the text has valid content
			p = GameServerClientMethods.CheckChatText(player, p);
		}

		if (p.IsEmpty)
			return;

		if (edict != null && player != null) {
			if (!player.CanSpeak())
				return;

			Assert(player.GetPlayerName()[0] != '\0');
			senderDead = (player.LifeState != (int)LifeState.Alive);
		}
		else
			senderDead = false;

		ReadOnlySpan<char> pszFormat = null;
		ReadOnlySpan<char> pszPrefix = null;
		ReadOnlySpan<char> pszLocation = null;
		if (g_pGameRules != null) {
			pszFormat = g_pGameRules.GetChatFormat(teamOnly, player);
			pszPrefix = g_pGameRules.GetChatPrefix(teamOnly, player);
			pszLocation = g_pGameRules.GetChatLocation(teamOnly, player);
		}

		ReadOnlySpan<char> pszPlayerName = player != null ? player.GetPlayerName() : "Console";

		if (!pszPrefix.IsStringEmpty) {
			if (!pszLocation.IsStringEmpty)
				sprintf(text, "%s %s @ %s: ").S(pszPrefix).S(pszPlayerName).S(pszLocation);
			else
				sprintf(text, "%s %s: ").S(pszPrefix).S(pszPlayerName);
		}
		else
			sprintf(text, "%s: ").S(pszPlayerName);

		j = text.Length - 2 - strlen(text);
		if (strlen(p) > j)
			p = p[..(int)j];

		strcat(text, p);
		strcat(text, "\n");
		text = text.SliceNullTerminatedString();

		// loop through all players
		// Start with the first player.
		// This may return the world in single player if the client types something between levels or during spawn
		// so check it, or it will infinite loop

		client = null;
		for (int i = 1; i <= gpGlobals.MaxClients; i++) {
			client = ToHL2MPPlayer(Util.PlayerByIndex(i));
			if (client == null)
				continue;

			if (client.Edict() == null)
				continue;

			if (client.Edict() == edict)
				continue;

			if (!(client.IsNetClient()))   // Not a client ? (should never be true)
				continue;

			if (teamOnly && !g_pGameRules!.PlayerCanHearChat(client, player))
				continue;

			// if (player != null && !client.CanHearAndReadChatFrom(player))
			// continue;

			if (player != null && GetVoiceGameMgr() != null && GetVoiceGameMgr().IsPlayerIgnoringPlayer(player.EntIndex(), i))
				continue;

			SingleUserRecipientFilter user = new(client);
			user.MakeReliable();

			if (!pszFormat.IsStringEmpty)
				Util.SayText2Filter(user, player, true, pszFormat, pszPlayerName, p, pszLocation);
			else
				Util.SayTextFilter(user, text, player, true);
		}

		if (player != null) {
			// print to the sending client
			SingleUserRecipientFilter user = new(player);
			user.MakeReliable();

			if (!pszFormat.IsStringEmpty)
				Util.SayText2Filter(user, player, true, pszFormat, pszPlayerName, p, pszLocation);
			else
				Util.SayTextFilter(user, text, player, true);
		}

		// echo to server console
		// Adrian: Only do this if we're running a dedicated server since we already print to console on the client.
		if (engine.IsDedicatedServer())
			Msg(text);

		Assert(!p.IsEmpty);

		int userid = 0;
		ReadOnlySpan<char> networkID = "Console";
		ReadOnlySpan<char> playerName = "Console";
		ReadOnlySpan<char> playerTeam = "Console";
		if (player != null) {
			player.CheckChatText(text);
			userid = player.GetUserID();
			networkID = player.GetNetworkIDString();
			playerName = player.GetPlayerName();
			Team? team = player.GetTeam();
			if (team != null)
				playerTeam = team.GetName();
		}

		if (teamOnly)
			Util.LogPrintf($"\"{playerName}<{userid}><{networkID}><{playerTeam}>\" say_team \"{p}\"\n");
		else
			Util.LogPrintf($"\"{playerName}<{userid}><{networkID}><{playerTeam}>\" say \"{p}\"\n");

		IGameEvent? ev = gameeventmanager.CreateEvent("player_say", true);

		if (ev != null) {
			ev.SetInt("userid", userid);
			ev.SetString("text", p);
			ev.SetInt("priority", 1);   // HLTV event priority, not transmitted
			gameeventmanager.FireEvent(ev, true);
		}
	}
#endif
}

public static class ServerClient
{
	[ConCommand("trace", "Traces from the player's eyes and prints everything the trace and the hit entity's visibility provide", FCvar.Cheat)]
	static void trace() {
		BasePlayer? player = Util.GetCommandClient();
		if (player == null)
			return;

		Vector3 start = player.EyePosition();
		player.EyeVectors(out Vector3 forward);
		Util.TraceLine(start, start + forward * MAX_COORD_RANGE, Mask.Shot, player, CollisionGroup.None, out Trace tr);

		BaseEntity? entity = tr.Ent;
		Surf surfaceFlags = (Surf)tr.Surface.Flags;

		Msg($"AllSolid: {tr.AllSolid}\n");
		Msg($"Contents: {tr.Contents}\n");
		Msg($"DispFlags: {tr.DispFlags}\n");
		Msg($"Distance: {Vector3.Distance(start, tr.EndPos)}\n");
		Msg($"Entity: {(entity == null ? "NULL" : $"[{entity.EntIndex()}][{entity.GetClassname()}]")}\n");
		Msg($"Fraction: {tr.Fraction}\n");
		Msg($"FractionLeftSolid: {tr.FractionLeftSolid}\n");
		Msg($"Hit: {tr.DidHit()}\n");
		Msg($"HitBox: {tr.HitBox}\n");
		Msg($"HitGroup: {tr.HitGroup}\n");
		Msg($"HitNoDraw: {(surfaceFlags & Surf.NoDraw) != 0}\n");
		Msg($"HitNonWorld: {tr.DidHitNonWorldEntity()}\n");
		Msg($"HitNormal: {tr.Plane.Normal.X} {tr.Plane.Normal.Y} {tr.Plane.Normal.Z}\n");
		Msg($"HitPos: {tr.EndPos.X} {tr.EndPos.Y} {tr.EndPos.Z}\n");
		Msg($"HitSky: {(surfaceFlags & Surf.Sky) != 0}\n");
		Msg($"HitTexture: {tr.Surface.Name}\n");
		Msg($"HitWorld: {tr.DidHitWorld()}\n");
		Msg($"MatType: {physprops.GetSurfaceData(tr.Surface.SurfaceProps)?.Game.Material}\n");
		Msg($"Model: {(entity == null ? "" : entity.GetModelName())}\n");
		Msg($"Normal: {forward.X} {forward.Y} {forward.Z}\n");
		Msg($"PhysicsBone: {tr.PhysicsBone}\n");
		Msg($"StartPos: {start.X} {start.Y} {start.Z}\n");
		Msg($"StartSolid: {tr.StartSolid}\n");
		Msg($"SurfaceFlags: {tr.Surface.Flags}\n");
		Msg($"SurfaceName: {physprops.GetPropName(tr.Surface.SurfaceProps)}\n");
		Msg($"SurfaceProps: {tr.Surface.SurfaceProps}\n");

		int eyeCluster = engine.GetClusterForOrigin(start);
		int eyeArea = engine.GetArea(start);
		int hitArea = engine.GetArea(tr.EndPos);
		Msg($"EyeCluster: {eyeCluster}\n");
		Msg($"EyeArea: {eyeArea}\n");
		Msg($"HitCluster: {engine.GetClusterForOrigin(tr.EndPos)}\n");
		Msg($"HitArea: {hitArea}\n");
		Msg($"HitAreaConnected: {engine.CheckAreasConnected(eyeArea, hitArea) != 0}\n");

		if (entity == null)
			return;

		ServerNetworkProperty netProp = entity.NetworkProp();
		Edict? edict = netProp.GetEdict();
		if (edict == null) {
			Msg("EntityEdict: NULL\n");
			return;
		}

		int entityArea = netProp.AreaNum();
		ref PVSInfo pvsInfo = ref netProp.GetPVSInfo();

		byte[] pvs = new byte[engine.GetPVSForCluster(eyeCluster, default)];
		engine.GetPVSForCluster(eyeCluster, pvs);

		Msg($"EntityName: {entity.GetEntityName()}\n");
		Msg($"EntityArea: {pvsInfo.AreaNum}\n");
		Msg($"EntityArea2: {pvsInfo.AreaNum2}\n");
		Msg($"EntityClusterCount: {pvsInfo.ClusterCount}\n");
		Msg($"EntityHeadNode: {pvsInfo.HeadNode}\n");
		Msg($"EntityCenter: {pvsInfo.Center.X} {pvsInfo.Center.Y} {pvsInfo.Center.Z}\n");
		Msg($"EntityInEyePVS: {netProp.IsInPVS(player.Edict(), pvs)}\n");
		Msg($"EntityAreaConnected: {engine.CheckAreasConnected(eyeArea, entityArea) != 0}\n");
		Msg($"EntityTransmitFlags: {edict.StateFlags & (EdictFlags.DontSend | EdictFlags.Always | EdictFlags.PVSCheck | EdictFlags.FullCheck)}\n");
	}

#if !GMOD_DLL
	[ConCommand(helpText: "Noclip. Player becomes non-solid and flies.")]
	static void noclip() {

	}
#endif
}
