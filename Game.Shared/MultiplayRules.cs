#if CLIENT_DLL || GAME_DLL
#if CLIENT_DLL
using Game.Client;
#else
using Game.Server;

using Microsoft.VisualBasic;

#endif

using Source.Common;
using Source.Common.Client;
using Source.Common.Commands;
using Source.Common.Engine;
using Source.Common.Mathematics;
using Source.Common.Networking;

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace Game.Shared;

public class MultiplayRules :
#if CLIENT_DLL
C_GameRules
#else
GameRules
#endif
{
	public static readonly ConVar mp_timelimit = new("mp_timelimit", "0", FCvar.Notify | FCvar.Replicated, "game time per map in minutes", callback: MPTimeLimitCallback);
	public static readonly ConVar fraglimit = new("mp_fraglimit", "0", FCvar.Notify | FCvar.Replicated, "The number of kills at which the map ends");
	public static readonly ConVar mp_show_voice_icons = new("mp_show_voice_icons", "1", FCvar.Replicated, "Show overhead player voice icons when players are speaking.\n");

#if GAME_DLL
	public static readonly ConVar tv_delaymapchange = new("tv_delaymapchange", "0", 0, "Delays map change until broadcast is complete");
	public static readonly ConVar tv_delaymapchange_protect = new("tv_delaymapchange_protect", "1", 0, "Protect against doing a manual map change if HLTV is broadcasting and has not caught up with a major game event such as round_end");
	public static readonly ConVar mp_restartgame = new("mp_restartgame", "0", FCvar.GameDLL, "If non-zero, game will restart in the specified number of seconds");
	public static readonly ConVar mp_mapcycle_empty_timeout_seconds = new("mp_mapcycle_empty_timeout_seconds", "0", FCvar.Replicated, "If nonzero, server will cycle to the next map if it has been empty on the current map for N seconds");
	public static readonly ConVar mp_restartgame_immediate = new("mp_restartgame_immediate", "0", FCvar.GameDLL, "If non-zero, game will restart immediately");
#endif

	private static void MPTimeLimitCallback(IConVar var, in ConVarChangeContext ctx) {
		// todo
	}

	public override bool IsMultiplayer() {
		return true;
	}

	public override bool ShouldDrawHeadLabels() {
		if (mp_show_voice_icons.GetBool() == false)
			return false;

		return base.ShouldDrawHeadLabels();
	}
	public override DamageType Damage_GetTimeBased() {
		DamageType damage = (DamageType.Paralyze | DamageType.NerveGas | DamageType.Poison | DamageType.Radiation | DamageType.DrownRecover | DamageType.Acid | DamageType.SlowBurn);
		return damage;
	}
	public override DamageType Damage_GetShouldGibCorpse() {
		DamageType damage = (DamageType.Crush | DamageType.Fall | DamageType.Blast | DamageType.Sonic | DamageType.Club);
		return damage;
	}
	public override DamageType Damage_GetShowOnHud() {
		DamageType damage = (DamageType.Poison | DamageType.Acid | DamageType.Drown | DamageType.Burn | DamageType.SlowBurn | DamageType.NerveGas | DamageType.Radiation | DamageType.Shock);
		return damage;
	}
	public override DamageType Damage_GetNoPhysicsForce() {
		DamageType timeBasedDamage = Damage_GetTimeBased();
		DamageType iDamage = (DamageType.Fall | DamageType.Burn | DamageType.Plasma | DamageType.Drown | timeBasedDamage | DamageType.Crush | DamageType.Physgun | DamageType.PreventPhysicsForce);
		return iDamage;
	}
	public override DamageType Damage_GetShouldNotBleed() {
		return DamageType.Poison | DamageType.Acid;
	}
	public override bool Damage_IsTimeBased(DamageType dmgType) {
		return ((dmgType & (DamageType.Paralyze | DamageType.NerveGas | DamageType.Poison | DamageType.Radiation | DamageType.DrownRecover | DamageType.Acid | DamageType.SlowBurn)) != 0);
	}
	public override bool Damage_ShouldGibCorpse(DamageType dmgType) {
		return ((dmgType & (DamageType.Crush | DamageType.Fall | DamageType.Blast | DamageType.Sonic | DamageType.Club)) != 0);
	}
	public override bool Damage_ShowOnHUD(DamageType dmgType) {
		return ((dmgType & (DamageType.Poison | DamageType.Acid | DamageType.Drown | DamageType.Burn | DamageType.SlowBurn | DamageType.NerveGas | DamageType.Radiation | DamageType.Shock)) != 0);
	}
	public override bool Damage_NoPhysicsForce(DamageType dmgType) {
		DamageType timeBasedDamage = Damage_GetTimeBased();
		return ((dmgType & (DamageType.Fall | DamageType.Burn | DamageType.Plasma | DamageType.Drown | timeBasedDamage | DamageType.Crush | DamageType.Physgun | DamageType.PreventPhysicsForce)) != 0);
	}
	public override bool Damage_ShouldNotBleed(DamageType dmgType) {
		return ((dmgType & (DamageType.Poison | DamageType.Acid)) != 0);
	}

	public static readonly ConVar nextlevel = new("nextlevel",
				  "",
				  FCvar.GameDLL | FCvar.Notify,
#if CSTRIKE_DLL || TF_DLL
				  "If set to a valid map name, will trigger a changelevel to the specified map at the end of the round" );
#else
				  "If set to a valid map name, will change to this map during the next changelevel");
#endif // CSTRIKE_DLL || TF_DLL

	public MultiplayRules() {
#if !CLIENT_DLL
		TimeLastMapChangeOrPlayerWasConnected = 0;
		RefreshSkillData(true);
		ReadOnlySpan<char> cfgfile = (engine.IsDedicatedServer() ? servercfgfile : lservercfgfile).GetString();

		if (!cfgfile.IsStringEmpty) {
			Span<char> command = stackalloc char[MAX_PATH];

			Log($"Executing {(engine.IsDedicatedServer() ? "dedicated" : "listen")} server config file {cfgfile}\n");
			engine.ServerCommand(sprintf(command, "exec %s\n").S(cfgfile).ToSpan());
		}

		nextlevel.SetValue("");
		LoadMapCycleFile();
#endif
		// todo: LoadVoiceCommandScript();
	}

	public override bool Init() {
#if GAME_DLL
		InitCustomResponseRulesDicts();
#endif
		return base.Init();
	}

#if CLIENT_DLL
#else
	public const int ITEM_RESPAWN_TIME = 30;
	public const int WEAPON_RESPAWN_TIME = 20;
	public const int AMMO_RESPAWN_TIME = 20;

	public override void RefreshSkillData(bool forceUpdate) {
		base.RefreshSkillData(forceUpdate);
#if !TF_DLL && !CSTRIKE_DLL
		ConVarRef suitcharger = new("sk_suitcharger");
		suitcharger.SetValue(30);
#endif
	}

	public override void Think() {
		base.Think();

		if (g_fGameOver) {
			// todo: ChangeLevel();
			return;
		}

		float flTimeLimit = mp_timelimit.GetFloat() * 60;
		float flFragLimit = fraglimit.GetFloat();

		if (flTimeLimit != 0 && gpGlobals.CurTime >= flTimeLimit) {
			GoToIntermission();
			return;
		}

		if (flFragLimit != 0) {
			// check if any player is over the frag limit
			for (int i = 1; i <= gpGlobals.MaxClients; i++) {
				BasePlayer? player = Util.PlayerByIndex(i);

				if (player != null && player.FragCount() >= flFragLimit) {
					GoToIntermission();
					return;
				}
			}
		}
	}
	public override ReadOnlySpan<char> GetTeamID(BaseEntity entity) {
		return "";
	}
	public override void FrameUpdatePostEntityThink() {
		base.FrameUpdatePostEntityThink();

		TimeUnit_t flNow = Source.Platform.Time;

		// Update time when client was last connected
		if (TimeLastMapChangeOrPlayerWasConnected <= 0.0)
			TimeLastMapChangeOrPlayerWasConnected = flNow;
		else {
			for (int iPlayerIndex = 1; iPlayerIndex <= Source.Constants.MAX_PLAYERS; iPlayerIndex++) {
				PlayerInfo pi;
				if (!engine.GetPlayerInfo(iPlayerIndex, out pi))
					continue;
#if REPLAY_ENABLED
				if ( pi.IsHLTV || pi.IsReplay || pi.FakePlayer )
#else
				if (pi.IsHLTV || pi.FakePlayer)
#endif
					continue;

				TimeLastMapChangeOrPlayerWasConnected = flNow;
				break;
			}
		}

		// Check if we should cycle the map because we've been empty
		// for long enough
		if (mp_mapcycle_empty_timeout_seconds.GetInt() > 0) {
			int iIdleSeconds = (int)(flNow - TimeLastMapChangeOrPlayerWasConnected);
			if (iIdleSeconds >= mp_mapcycle_empty_timeout_seconds.GetInt()) {

				Log($"Server has been empty for {iIdleSeconds} seconds on this map, cycling map as per mp_mapcycle_empty_timeout_seconds\n");
				// todo: ChangeLevel();
			}
		}
	}
	public override bool IsDeathmatch() => true;
	public override bool IsCoOp() => false;
	public override bool FShouldSwitchWeapon(BasePlayer player, BaseCombatWeapon? weapon) {
		if (!player.Weapon_CanSwitchTo(weapon!))
			return false;

		if (player.GetActiveWeapon() == null)
			return true;

		if (!weapon!.AllowsAutoSwitchTo())
			return false;

		if (!player.GetActiveWeapon()!.AllowsAutoSwitchFrom())
			return false;

		if (weapon.GetWeight() > player.GetActiveWeapon()!.GetWeight())
			return true;

		return false;
	}
	public override BaseCombatWeapon? GetNextBestWeapon(BaseCombatCharacter player, BaseCombatWeapon? currentWeapon) {
		return base.GetNextBestWeapon(player, currentWeapon);
	}
	public override bool SwitchToNextBestWeapon(BaseCombatCharacter player, BaseCombatWeapon? currentWeapon) {
		return base.SwitchToNextBestWeapon(player, currentWeapon);
	}
	public override bool ClientConnected(Edict entity, ReadOnlySpan<char> pszName, ReadOnlySpan<char> pszAddress, Span<char> reject) {
		GetVoiceGameMgr().ClientConnected(entity);
		return true;
	}
	public override void InitHUD(BasePlayer pl) {

	}
	public override void ClientDisconnected(Edict client) {
		// todo
	}
	public override float FlPlayerFallDamage(BasePlayer player) {
		throw new NotImplementedException();
	}
	public override bool AllowDamage(BaseEntity victim, in TakeDamageInfo info) => true;
	public override bool FPlayerCanTakeDamage(BasePlayer player, BaseEntity ent, in TakeDamageInfo info) => true;
	public override void PlayerThink(BasePlayer player) {
		if (g_fGameOver) {
			player.AfButtonPressed = 0;
			player.Buttons = 0;
			player.AfButtonReleased = 0;
		}
	}
	public override void PlayerSpawn(BasePlayer player) {
		bool addDefault;
		BaseEntity? weaponEntity = null;

		player.EquipSuit();

		addDefault = true;

		while ((weaponEntity = gEntList.FindEntityByClassname(weaponEntity, "game_player_equip")) != null) {
			weaponEntity.Touch(player);
			addDefault = false;
		}
	}
	public override bool FPlayerCanRespawn(BasePlayer player) => true;
	public override TimeUnit_t FlPlayerSpawnTime(BasePlayer player) => gpGlobals.CurTime;
	public override int IPointsForKill(BasePlayer attacker, BasePlayer killed) {
		return 1;
	}
	public override void PlayerKilled(BasePlayer victim, in TakeDamageInfo info) {
		// todo
	}
	public override void DeathNotice(BasePlayer victim, in TakeDamageInfo info) {
		// todo
	}
	public override TimeUnit_t FlWeaponRespawnTime(BaseCombatWeapon weapon) {
		if (weaponstay.GetInt() > 0) {
			// make sure it's only certain weapons
			if ((weapon.GetWeaponFlags() & WeaponFlags.LimitInWorld) == 0)
				return gpGlobals.CurTime + 0;      // weapon respawns almost instantly
		}

		return gpGlobals.CurTime + WEAPON_RESPAWN_TIME;
	}
	public const int ENTITY_INTOLERANCE = 100;
	public override double FlWeaponTryRespawn(BaseCombatWeapon? weapon) {
		if (weapon != null && (weapon.GetWeaponFlags() & WeaponFlags.LimitInWorld) == 0) {
			if (gEntList.NumberOfEntities() < (gpGlobals.MaxEntities - ENTITY_INTOLERANCE))
				return 0;

			// we're past the entity tolerance level,  so delay the respawn
			return FlWeaponRespawnTime(weapon);
		}

		return 0;
	}
	public override Vector3 VecWeaponRespawnSpot(BaseCombatWeapon weapon) {
		return weapon.GetAbsOrigin();
	}
	public override GameRulesRespawnReturnCode WeaponShouldRespawn(BaseCombatWeapon weapon) {
		return weapon.HasSpawnFlags(BasePlayer.SF.NoRespawn) ? GameRulesRespawnReturnCode.WeaponRespawnNo : GameRulesRespawnReturnCode.WeaponRespawnYes;
	}
	public override bool CanHavePlayerItem(BasePlayer player, BaseCombatWeapon item) {
		if (weaponstay.GetInt() > 0) {
			if ((item.GetWeaponFlags() & WeaponFlags.LimitInWorld) != 0)
				return base.CanHavePlayerItem(player, item);

			// check if the player already has this weapon
			for (int i = 0; i < player.WeaponCount(); i++)
				if (player.GetWeapon(i) == item)
					return false;
		}

		return base.CanHavePlayerItem(player, item);
	}
	public override bool CanHaveItem(BasePlayer player, Item item) {
		return true;
	}
	public override void PlayerGotItem(BasePlayer player, Item item) {

	}
	public override GameRulesRespawnReturnCode ItemShouldRespawn(Item item) {
		return item.HasSpawnFlags(BasePlayer.SF.NoRespawn) ? GameRulesRespawnReturnCode.ItemRespawnNo : GameRulesRespawnReturnCode.ItemRespawnYes;
	}
	public override TimeUnit_t FlItemRespawnTime(Item item) {
		return gpGlobals.CurTime + ITEM_RESPAWN_TIME;
	}
	public override Vector3 VecItemRespawnSpot(Item item) {
		return item.GetAbsOrigin();
	}
	public override QAngle VecItemRespawnAngles(Item item) {
		return item.GetAbsAngles();
	}
	public override void PlayerGotAmmo(BaseCombatCharacter player, Span<char> name) {

	}
	public override bool IsAllowedToSpawn(BaseEntity entity) {
		return true;
	}
	public override double FlHealthChargerRechargeTime() {
		return 60;
	}
	public override double FlHEVChargerRechargeTime() {
		return 30;
	}
	public override GameRulesRespawnReturnCode DeadPlayerWeapons(BasePlayer player) {
		return GameRulesRespawnReturnCode.PlayerDropGunActive;
	}
	public override GameRulesRespawnReturnCode DeadPlayerAmmo(BasePlayer player) {
		return GameRulesRespawnReturnCode.PlayerDropAmmoActive;
	}
	public override BaseEntity? GetPlayerSpawnSpot(BasePlayer player) {
		BaseEntity? entSpawnSpot = base.GetPlayerSpawnSpot(player);
		return entSpawnSpot;
	}
	public override bool PlayerCanHearChat(BasePlayer? listener, BasePlayer speaker) {
		// return PlayerRelationship(listener, speaker) == GameRulesPlayerRelationship.Teammate;
		return true; // todo ^^ review how gmod overrides this and do it there
	}
	public override GameRulesPlayerRelationship PlayerRelationship(BaseEntity? player, BaseEntity? target) {
		return GameRulesPlayerRelationship.NotTeammate;
	}
	public override bool PlayFootstepSounds(BasePlayer pl) {
		if (footsteps.GetInt() == 0)
			return false;

		if (pl.IsOnLadder() || pl.GetAbsVelocity().Length2D() > 220)
			return true;  // only make step sounds in multiplayer if the player is moving fast enough

		return false;
	}
	public override bool FAllowFlashlight() {
		return flashlight.GetInt() != 0;
	}
	public override bool FAllowNPCs() {
		return allowNPCs.GetInt() != 0;
	}
#endif

	public virtual void InitCustomResponseRulesDicts() { }
	public virtual void ShutdownCustomResponseRulesDicts() { }

	protected virtual void GoToIntermission() {
		// todo
	}
	protected virtual void LoadMapCycleFile() {
		// todo
	}
	protected void ChangeLevelToMap(ReadOnlySpan<char> map) {
		// todo
	}

	protected TimeUnit_t IntermissionEndTime;
	protected static int MapCycleTimeStamp;
	protected static int MapCycleindex;
	protected static readonly List<char[]> MapList = [];

	protected TimeUnit_t TimeLastMapChangeOrPlayerWasConnected;
}
#endif
